#!/usr/bin/env python3
"""Fills the derived fields of a prop manifest: profile, triangles and sha256 (standalone-phone plan section 6.3).

    tools/props/.venv/bin/python tools/props/profile_tool.py [--dir DIR] [--write]

DIR holds `manifest.json` and one `<id>.glb.bytes` per prop (default: the phone's library,
ATLAS/Assets/Atlas/Telemetry/Resources/AtlasProps). Without --write the tool prints what it computed and what
differs from the manifest, and exits 1 when anything differs; with --write it rewrites the manifest.

The profile is `station/forge/forge/profile.py:of_mesh` of the prop at its reference size (the manifest's
`size`), so it is indexed exactly like the profile the phone measures for a scanned object (RoomObject.Profile,
A/Scan/Build/ObjectProfile.cs): Unity's box frame, origin at the centre of the base, cell (i, j) at index
j * 16 + i with i along local +X and j along local +Z, row 0 at the lowest z, 0 for a cell no ray hit.
of_mesh takes the GLB frame (x is the negative of Unity's), which is the frame the file stores, so the
positions go in as they are read.

This module also holds what check.py and reference.py share: the GLB reader and the manifest writer.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import struct
import sys
from dataclasses import dataclass
from pathlib import Path

import numpy as np

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "station" / "forge"))
sys.path.insert(0, str(REPO / "tools" / "kit"))

from forge import profile as forge_profile  # noqa: E402
import kitglb  # noqa: E402

SCHEMA = "atlas.props/1"
MANIFEST = "manifest.json"
GLB_SUFFIX = ".glb.bytes"
ASSETS = REPO / "ATLAS" / "Assets" / "Atlas"
PROPS_DIR = ASSETS / "Telemetry" / "Resources" / "AtlasProps"
FIXTURES_DIR = ASSETS / "Tests" / "Objects" / "Fixtures" / "props_min"

_FLOAT, _UNSIGNED_BYTE, _UNSIGNED_SHORT, _UNSIGNED_INT = 5126, 5121, 5123, 5125
_COMPONENTS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}
_DTYPES = {_FLOAT: "<f4", _UNSIGNED_BYTE: "u1", _UNSIGNED_SHORT: "<u2", _UNSIGNED_INT: "<u4"}


@dataclass
class PropGlb:
    """The mesh of a prop file as stored: GLB frame (x negated), v downwards, front faces counter-clockwise."""
    positions: np.ndarray  # (N, 3) float64 holding the stored float32 values
    normals: np.ndarray    # (N, 3)
    uv0: np.ndarray        # (N, 2), v downwards
    colours: np.ndarray    # (N, 4) uint8: shade, line weight, slot (255: not batched), 255
    faces: np.ndarray      # (T, 3) int64, as stored

    @property
    def triangles(self) -> int:
        return len(self.faces)

    def cells(self) -> np.ndarray:
        """The palette cell (0..255) of every vertex: AtlasPalette.Uv inverted, v turned upwards."""
        u = self.uv0[:, 0]
        v = 1.0 - self.uv0[:, 1]
        column = np.clip(np.floor(u * kitglb.CELLS_PER_SIDE), 0, kitglb.CELLS_PER_SIDE - 1).astype(np.int64)
        row = np.clip(np.floor(v * kitglb.CELLS_PER_SIDE), 0, kitglb.CELLS_PER_SIDE - 1).astype(np.int64)
        return row * kitglb.CELLS_PER_SIDE + column

    def to_piece(self) -> kitglb.Piece:
        """The exact inverse of kitglb.write_glb: Unity's frame, v upwards, clockwise fronts."""
        piece = kitglb.Piece()
        piece.positions = [(-float(p[0]) + 0.0, float(p[1]), float(p[2])) for p in self.positions]
        piece.normals = [(-float(n[0]) + 0.0, float(n[1]), float(n[2])) for n in self.normals]
        piece.uv0 = [(float(t[0]), 1.0 - float(t[1])) for t in self.uv0]
        piece.uv1 = [(0.0, 0.0)] * len(self.positions)
        piece.shade = [int(c[0]) / 255.0 for c in self.colours]
        for a, b, c in self.faces:
            piece.indices.extend((int(a), int(c), int(b)))
        return piece


class PropFileError(ValueError):
    """The file is not a GLB of the kit contract."""


def _accessor(doc: dict, binary: bytes, index, component: int, kind: str) -> np.ndarray:
    try:
        acc = doc["accessors"][index]
        view = doc["bufferViews"][acc["bufferView"]]
    except (KeyError, IndexError, TypeError) as exc:
        raise PropFileError(f"accessor {index} is missing") from exc
    if acc.get("componentType") != component or acc.get("type") != kind:
        raise PropFileError(f"accessor {index} is not {kind} of component type {component}")
    if "byteStride" in view:
        raise PropFileError(f"accessor {index} is interleaved; the kit contract packs every array")
    n = _COMPONENTS[kind]
    dtype = np.dtype(_DTYPES[component])
    start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    length = acc["count"] * n * dtype.itemsize
    if start + length > len(binary) or length > view["byteLength"]:
        raise PropFileError(f"accessor {index} runs past its buffer")
    values = np.frombuffer(binary, dtype=dtype, count=acc["count"] * n, offset=start)
    return values.reshape(acc["count"], n) if n > 1 else values


def read_prop_glb(glb: bytes) -> PropGlb:
    """The mesh of a prop file, read as GlbMeshReader reads it. PropFileError for anything else."""
    if len(glb) < 28 or struct.unpack_from("<I", glb, 0)[0] != 0x46546C67:
        raise PropFileError("not a GLB")
    try:
        doc, binary = kitglb.read_glb(glb)
        meshes = doc["meshes"]
        if len(meshes) != 1 or len(meshes[0]["primitives"]) != 1:
            raise PropFileError("a prop holds exactly one mesh with one primitive")
        primitive = meshes[0]["primitives"][0]
        attributes = primitive["attributes"]
    except (KeyError, IndexError, TypeError, ValueError, UnicodeDecodeError) as exc:
        raise PropFileError(f"unreadable GLB: {exc}") from exc
    if primitive.get("mode", 4) != 4:
        raise PropFileError("the primitive is not a triangle list")
    for name in ("POSITION", "NORMAL", "TEXCOORD_0", "COLOR_0"):
        if name not in attributes:
            raise PropFileError(f"the primitive has no {name}")
    positions = _accessor(doc, binary, attributes["POSITION"], _FLOAT, "VEC3").astype(np.float64)
    normals = _accessor(doc, binary, attributes["NORMAL"], _FLOAT, "VEC3").astype(np.float64)
    uv0 = _accessor(doc, binary, attributes["TEXCOORD_0"], _FLOAT, "VEC2").astype(np.float64)
    colours = _accessor(doc, binary, attributes["COLOR_0"], _UNSIGNED_BYTE, "VEC4").copy()
    index_acc = doc["accessors"][primitive["indices"]]
    component = index_acc.get("componentType")
    if component not in (_UNSIGNED_SHORT, _UNSIGNED_INT):
        raise PropFileError("indices are neither UNSIGNED_SHORT nor UNSIGNED_INT")
    indices = _accessor(doc, binary, primitive["indices"], component, "SCALAR").astype(np.int64)
    count = len(positions)
    if len(normals) != count or len(uv0) != count or len(colours) != count:
        raise PropFileError("the vertex arrays differ in length")
    if len(indices) % 3 or len(indices) == 0:
        raise PropFileError(f"{len(indices)} indices is not a whole, non-zero number of triangles")
    if indices.min() < 0 or indices.max() >= count:
        raise PropFileError("an index is out of range")
    return PropGlb(positions, normals, uv0, colours, indices.reshape(-1, 3))


def profile_of(mesh: PropGlb, size) -> np.ndarray:
    """The 256-byte profile of the prop at its reference size: of_mesh, GLB-frame positions as stored."""
    return forge_profile.of_mesh(mesh.positions, mesh.faces, [float(s) for s in size])


def encode(profile) -> str:
    return forge_profile.encode(profile)


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def glb_path(folder: Path, prop_id: str) -> Path:
    return Path(folder) / (prop_id + GLB_SUFFIX)


def load_manifest(folder: Path) -> dict:
    return json.loads((Path(folder) / MANIFEST).read_text(encoding="utf-8"))


def dumps_manifest(manifest: dict) -> bytes:
    """The manifest as written to disk: two-space indent, keys in the order given, a final newline."""
    return (json.dumps(manifest, indent=2, ensure_ascii=True) + "\n").encode("utf-8")


def derived_fields(folder: Path, entry: dict) -> dict:
    """{"triangles", "sha256", "profile"} of one manifest entry, computed from its file."""
    data = glb_path(folder, entry["id"]).read_bytes()
    mesh = read_prop_glb(data)
    return {"triangles": mesh.triangles, "sha256": sha256(data), "profile": encode(profile_of(mesh, entry["size"]))}


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--dir", type=Path, default=PROPS_DIR, help="folder with manifest.json and the .glb.bytes files")
    ap.add_argument("--write", action="store_true", help="write profile, triangles and sha256 into the manifest")
    args = ap.parse_args(argv)

    manifest = load_manifest(args.dir)
    differ = 0
    for entry in manifest.get("props", []):
        derived = derived_fields(args.dir, entry)
        changed = [key for key, value in derived.items() if entry.get(key) != value]
        differ += bool(changed)
        coverage = forge_profile.coverage(forge_profile.decode(derived["profile"]))
        print(f"{entry['id']:40s} {derived['triangles']:5d} tris  coverage {coverage:.3f}"
              + (f"  differs: {', '.join(changed)}" if changed else ""))
        entry.update(derived)
    if args.write:
        (Path(args.dir) / MANIFEST).write_bytes(dumps_manifest(manifest))
        print(f"wrote {Path(args.dir) / MANIFEST}")
        return 0
    return 1 if differ else 0


if __name__ == "__main__":
    sys.exit(main())
