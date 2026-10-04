"""Minimal, dependency-free GLB 2.0 writer and reader (numpy only).

Writes triangle meshes with positions, optional normals, uint32 indices and optional morph
targets (blend shapes). Morph target names go to ``mesh.extras.targetNames`` (the convention
glTFast and Blender read). Output is deterministic: the same arrays give the same bytes.

    from atlas_pipelines.glb import GlbMesh, write_glb, read_glb, triangle_count

    mesh = GlbMesh(
        name="phases",
        positions=V0,                      # (n, 3) float, base shape
        indices=F,                         # (m, 3) int, triangles
        normals=None,                      # optional (n, 3)
        targets={"phase_0": V[0] - V0,     # name -> (n, 3) DISPLACEMENT from the base shape
                 "phase_1": V[1] - V0},    # insertion order is the morph target order
    )
    write_glb(path, [mesh])
    meshes = read_glb(path)                # -> list[GlbMesh] with the same arrays (float32 / uint32)
    triangle_count(path)                   # total triangles in the file

An empty shell (for example the twin's empty core) is ``write_glb(path, [])``: a valid GLB with a
scene and no meshes. Conventions for units, axes and target names are in docs/case_contract.md.

The reader is meant for files written by this module (and for tests); it handles float32
positions/normals, uint8/16/32 indices, non-interleaved buffer views and one primitive per mesh.
"""

from __future__ import annotations

import json
import struct
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Sequence

import numpy as np

GLB_MAGIC = 0x46546C67  # "glTF"
CHUNK_JSON = 0x4E4F534A  # "JSON"
CHUNK_BIN = 0x004E4942  # "BIN\0"
GENERATOR = "atlas_pipelines.glb"

_FLOAT, _UBYTE, _USHORT, _UINT = 5126, 5121, 5123, 5125
_ARRAY_BUFFER, _ELEMENT_ARRAY_BUFFER = 34962, 34963
_INDEX_DTYPES = {_UBYTE: np.uint8, _USHORT: "<u2", _UINT: "<u4"}


@dataclass
class GlbMesh:
    positions: np.ndarray  #: (n, 3)
    indices: np.ndarray  #: (m, 3) triangles
    normals: np.ndarray | None = None  #: (n, 3) or None
    targets: dict[str, np.ndarray] = field(default_factory=dict)  #: name -> (n, 3) displacement
    target_normals: dict[str, np.ndarray] | None = None  #: optional, same keys as targets
    name: str = "mesh"
    extras: dict | None = None  #: extra JSON-able entries for mesh.extras

    @property
    def n_triangles(self) -> int:
        return int(np.asarray(self.indices).reshape(-1, 3).shape[0])

    @property
    def target_names(self) -> list[str]:
        return list(self.targets)


def _pad4(data: bytes, fill: bytes) -> bytes:
    return data + fill * ((-len(data)) % 4)


class _Builder:
    def __init__(self) -> None:
        self.blob = bytearray()
        self.views: list[dict] = []
        self.accessors: list[dict] = []

    def _view(self, data: bytes, target: int | None) -> int:
        self.blob.extend(b"\x00" * ((-len(self.blob)) % 4))
        v: dict[str, Any] = {"buffer": 0, "byteOffset": len(self.blob), "byteLength": len(data)}
        if target is not None:
            v["target"] = target
        self.blob.extend(data)
        self.views.append(v)
        return len(self.views) - 1

    def vec3(self, a: np.ndarray, n: int, what: str, minmax: bool, target: int | None = _ARRAY_BUFFER) -> int:
        a = np.ascontiguousarray(a, dtype="<f4")
        if a.shape != (n, 3):
            raise ValueError(f"{what}: expected shape ({n}, 3), got {a.shape}")
        if not np.isfinite(a).all():
            raise ValueError(f"{what}: NaN or infinity")
        acc: dict[str, Any] = {"bufferView": self._view(a.tobytes(), target), "componentType": _FLOAT,
                               "count": n, "type": "VEC3"}
        if minmax:
            acc["min"] = [float(x) for x in a.min(axis=0)]
            acc["max"] = [float(x) for x in a.max(axis=0)]
        self.accessors.append(acc)
        return len(self.accessors) - 1

    def index(self, idx: np.ndarray, n_verts: int) -> int:
        idx = np.ascontiguousarray(idx, dtype=np.int64).reshape(-1)
        if idx.size % 3:
            raise ValueError("indices: not a multiple of 3")
        if idx.size and (idx.min() < 0 or idx.max() >= n_verts):
            raise ValueError("indices: out of range")
        data = idx.astype("<u4").tobytes()
        self.accessors.append({"bufferView": self._view(data, _ELEMENT_ARRAY_BUFFER), "componentType": _UINT,
                               "count": int(idx.size), "type": "SCALAR"})
        return len(self.accessors) - 1


def glb_bytes(meshes: Sequence[GlbMesh], asset_extras: dict | None = None) -> bytes:
    """Encode meshes as one GLB: one node and one single-primitive mesh per GlbMesh."""
    b = _Builder()
    gl_meshes, nodes = [], []
    for m in meshes:
        pos = np.asarray(m.positions)
        n = pos.shape[0]
        if n == 0 or m.n_triangles == 0:
            raise ValueError(f"mesh {m.name!r} is empty; leave empty meshes out (write_glb(path, []))")
        prim: dict[str, Any] = {"attributes": {"POSITION": b.vec3(pos, n, f"{m.name}.positions", True)},
                                "mode": 4}
        if m.normals is not None:
            prim["attributes"]["NORMAL"] = b.vec3(m.normals, n, f"{m.name}.normals", False)
        prim["indices"] = b.index(m.indices, n)
        gl_mesh: dict[str, Any] = {"name": m.name, "primitives": [prim]}
        extras = dict(m.extras or {})
        if m.targets:
            if m.target_normals is not None and list(m.target_normals) != list(m.targets):
                raise ValueError("target_normals must have the same keys, in the same order, as targets")
            tl = []
            for tname, delta in m.targets.items():
                # glTF requires a morph target accessor to have no bufferView target and to carry min/max
                t = {"POSITION": b.vec3(delta, n, f"{m.name}.targets[{tname}]", True, target=None)}
                if m.target_normals is not None:
                    t["NORMAL"] = b.vec3(m.target_normals[tname], n, f"{m.name}.target_normals[{tname}]", False,
                                         target=None)
                tl.append(t)
            prim["targets"] = tl
            gl_mesh["weights"] = [0.0] * len(tl)
            extras["targetNames"] = list(m.targets)
        if extras:
            gl_mesh["extras"] = extras
        gl_meshes.append(gl_mesh)
        nodes.append({"mesh": len(gl_meshes) - 1, "name": m.name})

    asset: dict[str, Any] = {"generator": GENERATOR, "version": "2.0"}
    if asset_extras:
        asset["extras"] = asset_extras
    doc: dict[str, Any] = {"asset": asset, "scene": 0, "scenes": [{"nodes": list(range(len(nodes)))}]}
    if nodes:
        doc["nodes"] = nodes
        doc["meshes"] = gl_meshes
        doc["accessors"] = b.accessors
        doc["bufferViews"] = b.views
        doc["buffers"] = [{"byteLength": len(b.blob)}]
    js = _pad4(json.dumps(doc, sort_keys=True, separators=(",", ":"), ensure_ascii=True,
                          allow_nan=False).encode("ascii"), b" ")
    out = bytearray()
    chunks = struct.pack("<II", len(js), CHUNK_JSON) + js
    if nodes:
        blob = _pad4(bytes(b.blob), b"\x00")
        chunks += struct.pack("<II", len(blob), CHUNK_BIN) + blob
    out += struct.pack("<III", GLB_MAGIC, 2, 12 + len(chunks))
    out += chunks
    return bytes(out)


def write_glb(path: Path | str, meshes: Sequence[GlbMesh], asset_extras: dict | None = None) -> bytes:
    """Write a GLB file. Returns the bytes written."""
    data = glb_bytes(meshes, asset_extras)
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    Path(path).write_bytes(data)
    return data


# -- reader ------------------------------------------------------------------------------------


def parse_glb(data: bytes) -> tuple[dict, bytes]:
    """Split a GLB into its JSON document and its binary chunk (b"" when absent)."""
    if len(data) < 12:
        raise ValueError("GLB: too short")
    magic, version, length = struct.unpack_from("<III", data, 0)
    if magic != GLB_MAGIC or version != 2:
        raise ValueError("GLB: bad magic or version")
    if length != len(data):
        raise ValueError(f"GLB: header length {length} != file size {len(data)}")
    doc, blob, o = None, b"", 12
    while o < length:
        clen, ctype = struct.unpack_from("<II", data, o)
        chunk = data[o + 8:o + 8 + clen]
        if ctype == CHUNK_JSON and doc is None:
            doc = json.loads(chunk.decode("utf-8"))
        elif ctype == CHUNK_BIN:
            blob = bytes(chunk)
        o += 8 + clen + ((-clen) % 4)
    if doc is None:
        raise ValueError("GLB: no JSON chunk")
    return doc, blob


def _accessor(doc: dict, blob: bytes, i: int) -> np.ndarray:
    acc = doc["accessors"][i]
    view = doc["bufferViews"][acc["bufferView"]]
    if view.get("byteStride") not in (None, 0, 12 if acc["type"] == "VEC3" else None):
        raise ValueError("GLB reader: interleaved buffer views are not supported")
    off = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    if acc["type"] == "VEC3" and acc["componentType"] == _FLOAT:
        return np.frombuffer(blob, dtype="<f4", count=3 * acc["count"], offset=off).reshape(-1, 3).copy()
    if acc["type"] == "SCALAR" and acc["componentType"] in _INDEX_DTYPES:
        a = np.frombuffer(blob, dtype=_INDEX_DTYPES[acc["componentType"]], count=acc["count"], offset=off)
        return a.astype(np.uint32)
    raise ValueError(f"GLB reader: unsupported accessor {acc['type']}/{acc['componentType']}")


def read_glb(path_or_bytes: Path | str | bytes) -> list[GlbMesh]:
    """Read every mesh of a GLB (one GlbMesh per primitive; targets are displacements)."""
    data = path_or_bytes if isinstance(path_or_bytes, (bytes, bytearray)) else Path(path_or_bytes).read_bytes()
    doc, blob = parse_glb(bytes(data))
    out: list[GlbMesh] = []
    for gm in doc.get("meshes", []):
        names = (gm.get("extras") or {}).get("targetNames")
        for prim in gm["primitives"]:
            if prim.get("mode", 4) != 4:
                raise ValueError("GLB reader: only triangle lists are supported")
            pos = _accessor(doc, blob, prim["attributes"]["POSITION"])
            if "indices" in prim:
                idx = _accessor(doc, blob, prim["indices"]).reshape(-1, 3)
            else:
                idx = np.arange(pos.shape[0], dtype=np.uint32).reshape(-1, 3)
            normals = _accessor(doc, blob, prim["attributes"]["NORMAL"]) if "NORMAL" in prim["attributes"] else None
            tl = prim.get("targets", [])
            tnames = names if names is not None else [f"target_{i}" for i in range(len(tl))]
            if len(tnames) != len(tl):
                raise ValueError("GLB: extras.targetNames does not match the number of morph targets")
            targets = {n: _accessor(doc, blob, t["POSITION"]) for n, t in zip(tnames, tl)}
            tnorm = None
            if tl and all("NORMAL" in t for t in tl):
                tnorm = {n: _accessor(doc, blob, t["NORMAL"]) for n, t in zip(tnames, tl)}
            extras = {k: v for k, v in (gm.get("extras") or {}).items() if k != "targetNames"} or None
            out.append(GlbMesh(positions=pos, indices=idx, normals=normals, targets=targets, target_normals=tnorm,
                               name=gm.get("name", "mesh"), extras=extras))
    return out


def triangle_count(path_or_bytes: Path | str | bytes) -> int:
    """Total number of triangles in a GLB."""
    return sum(m.n_triangles for m in read_glb(path_or_bytes))


def morph_target_names(path_or_bytes: Path | str | bytes) -> list[list[str]]:
    """Morph target names of every mesh, in file order."""
    return [m.target_names for m in read_glb(path_or_bytes)]
