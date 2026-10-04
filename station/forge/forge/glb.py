"""A minimal, deterministic GLB writer and reader for the asset contract.

The Forge writes exactly one mesh with one triangle primitive and no material,
texture, image or sampler (Master Plan sections 6.4 and 7.4). Writing the file
here, instead of through a general exporter, keeps the bytes identical for
identical input, so the content hash is stable (task P1-D09).
"""
from __future__ import annotations

import json
import struct
from dataclasses import dataclass

import numpy as np

_MAGIC = 0x46546C67  # "glTF"
_JSON = 0x4E4F534A
_BIN = 0x004E4942

_FLOAT = 5126
_UBYTE = 5121
_USHORT = 5123
_UINT = 5125
_DTYPES = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
_COMPONENTS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


class GlbError(ValueError):
    """The bytes are not a GLB this module can read."""


def _pad(data: bytes, fill: bytes) -> bytes:
    return data + fill * ((4 - len(data) % 4) % 4)


def write_glb(
    positions: np.ndarray,
    normals: np.ndarray,
    uvs: np.ndarray,
    colours: np.ndarray,
    indices: np.ndarray,
    generator: str = "ATLAS Forge",
) -> bytes:
    """Serialise one mesh. ``colours`` is (N, 4) uint8, stored normalised."""
    positions = np.ascontiguousarray(positions, dtype="<f4")
    normals = np.ascontiguousarray(normals, dtype="<f4")
    uvs = np.ascontiguousarray(uvs, dtype="<f4")
    colours = np.ascontiguousarray(colours, dtype=np.uint8)
    n = len(positions)
    if n == 0 or n > 65535:
        raise GlbError(f"vertex count {n} does not fit 16-bit indices")
    if not (len(normals) == len(uvs) == len(colours) == n):
        raise GlbError("vertex attribute arrays differ in length")
    idx = np.ascontiguousarray(indices, dtype="<u2").reshape(-1)

    chunks = [idx.tobytes(), positions.tobytes(), normals.tobytes(), uvs.tobytes(), colours.tobytes()]
    targets = [34963, 34962, 34962, 34962, 34962]
    views, blob = [], b""
    for chunk, target in zip(chunks, targets):
        blob = _pad(blob, b"\x00")
        views.append({"buffer": 0, "byteOffset": len(blob), "byteLength": len(chunk), "target": target})
        blob += chunk
    blob = _pad(blob, b"\x00")

    doc = {
        "asset": {"version": "2.0", "generator": generator},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": [{"mesh": 0}],
        "meshes": [
            {
                "primitives": [
                    {
                        "attributes": {"POSITION": 1, "NORMAL": 2, "TEXCOORD_0": 3, "COLOR_0": 4},
                        "indices": 0,
                        "mode": 4,
                    }
                ]
            }
        ],
        "accessors": [
            {"bufferView": 0, "componentType": _USHORT, "count": int(idx.size), "type": "SCALAR"},
            {
                "bufferView": 1,
                "componentType": _FLOAT,
                "count": n,
                "type": "VEC3",
                "min": [float(v) for v in positions.min(axis=0)],
                "max": [float(v) for v in positions.max(axis=0)],
            },
            {"bufferView": 2, "componentType": _FLOAT, "count": n, "type": "VEC3"},
            {"bufferView": 3, "componentType": _FLOAT, "count": n, "type": "VEC2"},
            {"bufferView": 4, "componentType": _UBYTE, "normalized": True, "count": n, "type": "VEC4"},
        ],
        "bufferViews": views,
        "buffers": [{"byteLength": len(blob)}],
    }
    text = _pad(json.dumps(doc, separators=(",", ":")).encode("utf-8"), b" ")
    total = 12 + 8 + len(text) + 8 + len(blob)
    return b"".join(
        [
            struct.pack("<III", _MAGIC, 2, total),
            struct.pack("<II", len(text), _JSON),
            text,
            struct.pack("<II", len(blob), _BIN),
            blob,
        ]
    )


@dataclass
class Glb:
    """A parsed GLB: the JSON document and the binary chunk."""

    doc: dict
    blob: bytes

    def accessor(self, index: int) -> np.ndarray:
        """Decode accessor ``index`` to an array of shape (count, components)."""
        try:
            acc = self.doc["accessors"][index]
            dtype = np.dtype(_DTYPES[acc["componentType"]]).newbyteorder("<")
            comps = _COMPONENTS[acc["type"]]
            count = int(acc["count"])
            if "sparse" in acc or "bufferView" not in acc:
                raise GlbError("sparse or empty accessors are not part of the asset contract")
            view = self.doc["bufferViews"][acc["bufferView"]]
            if view.get("buffer", 0) != 0:
                raise GlbError("only the embedded buffer is allowed")
            start = int(view.get("byteOffset", 0)) + int(acc.get("byteOffset", 0))
            stride = int(view.get("byteStride", 0)) or dtype.itemsize * comps
            if stride == dtype.itemsize * comps:
                end = start + count * stride
                if end > len(self.blob):
                    raise GlbError("accessor runs past the end of the buffer")
                data = np.frombuffer(self.blob, dtype=dtype, count=count * comps, offset=start)
                return data.reshape(count, comps)
            out = np.empty((count, comps), dtype=dtype)
            for i in range(count):
                out[i] = np.frombuffer(self.blob, dtype=dtype, count=comps, offset=start + i * stride)
            return out
        except GlbError:
            raise
        except (KeyError, IndexError, TypeError, ValueError) as exc:
            raise GlbError(f"accessor {index} cannot be read: {exc}") from exc


def read_glb(data: bytes) -> Glb:
    """Parse the container; the mesh content is checked by the validator."""
    if len(data) < 20:
        raise GlbError("file is too short to be a GLB")
    magic, version, total = struct.unpack_from("<III", data, 0)
    if magic != _MAGIC:
        raise GlbError("missing the glTF magic number")
    if version != 2:
        raise GlbError(f"glTF container version {version} is not supported")
    if total != len(data):
        raise GlbError("declared length differs from the file length")
    doc, blob, offset = None, b"", 12
    while offset + 8 <= len(data):
        length, kind = struct.unpack_from("<II", data, offset)
        body = data[offset + 8 : offset + 8 + length]
        if len(body) != length:
            raise GlbError("chunk runs past the end of the file")
        if kind == _JSON and doc is None:
            try:
                doc = json.loads(body.decode("utf-8"))
            except (UnicodeDecodeError, json.JSONDecodeError) as exc:
                raise GlbError(f"JSON chunk cannot be parsed: {exc}") from exc
        elif kind == _BIN and not blob:
            blob = bytes(body)
        offset += 8 + length
    if not isinstance(doc, dict):
        raise GlbError("no JSON chunk")
    return Glb(doc=doc, blob=blob)
