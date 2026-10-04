#!/usr/bin/env python3
"""Writes the kit pieces modelled in Blender as GLBs in the look's format (kit hero assets design, 2026-10-03).

A piece is held in Unity's frame: left-handed, +X right, +Y up, +Z forward, metres, triangles clockwise seen
from the front, palette coordinates in uv0 (AtlasPalette.Uv), line coordinates in uv1 and the baked shade
(0..1) per vertex. `write_glb` stores it the way every GLB of the contract is stored (docs/contracts/
lane-d-link.md section 4.1), which is the exact inverse of what `GlbMeshReader` undoes on the phone: x
negated, v running downwards (uv0 and uv1 alike), front faces counter-clockwise.

    POSITION, NORMAL    VEC3 FLOAT
    TEXCOORD_0          VEC2 FLOAT   palette coordinate
    TEXCOORD_1          VEC2 FLOAT   line coordinates; KitSet reads them, GlbMeshReader ignores them
    COLOR_0             VEC4 UNSIGNED_BYTE normalised: shade, line weight 255, 255, 255 (AtlasVertex.Color)
    indices             UNSIGNED_SHORT

No materials and no textures: the colour is the palette cell. Python standard library only, no bpy, so the
writer and its checks are tested on any machine (tools/kit/test_kitglb.py); tools/kit/export_kit.py is the
Blender side.
"""
from __future__ import annotations

import hashlib
import itertools
import json
import math
import re
import struct
from dataclasses import dataclass, field
from pathlib import Path

# Triangle ceilings per piece: AtlasMeshBudget.GluedCell and AtlasMeshBudget.SelfAvatar; the duck of the S3 maze
# (2026-10-04 design, section 4) is one piece in a room, at most 2,500.
BUDGETS = {"landmark": 1500, "avatar": 600, "duck": 2500}

# Positions closer than this are one point when looking for open edges, metres.
WELD_TOLERANCE = 1e-5
# A normal whose length is further than this from 1 is refused.
NORMAL_TOLERANCE = 1e-3

# AtlasShading: the one fixed diagram light, baked into the shade as the Forge and the generators bake it.
LIGHT_DIRECTION = tuple(c / math.sqrt(0.35 ** 2 + 0.85 ** 2 + 0.40 ** 2) for c in (0.35, 0.85, 0.40))
SHADE_FLOOR = 0.55

MATERIAL_PREFIX = "pal_"
CELLS_PER_SIDE = 16

_ARRAY_BUFFER, _ELEMENT_ARRAY_BUFFER = 34962, 34963
_UNSIGNED_BYTE, _UNSIGNED_SHORT, _FLOAT = 5121, 5123, 5126


@dataclass
class Piece:
    """One kit piece in Unity's frame. Every per-vertex list has one entry per position."""
    positions: list = field(default_factory=list)   # (x, y, z), metres
    normals: list = field(default_factory=list)     # (x, y, z), unit
    uv0: list = field(default_factory=list)         # (u, v), palette coordinate, v upwards
    uv1: list = field(default_factory=list)         # (u, v), line coordinates
    shade: list = field(default_factory=list)       # 0..1
    indices: list = field(default_factory=list)     # three per triangle, clockwise seen from the front

    @property
    def triangles(self) -> int:
        return len(self.indices) // 3


# ---- Palette -----------------------------------------------------------------------------------------------

def palette_slots(atlas_palette_cs_path) -> dict[str, int]:
    """The named cells of AtlasPalette.cs, name -> index.

    Only the constants declared in the blocks headed by a `// Row <n>:` comment count (today rows 0, 1 and 12:
    Void ... Text, Floor ... VirtualWall, PortalFrame ... Spare). A block runs from its heading to the next
    blank line. Size, CellsPerSide, the block bases and counts and the strip rows are not cells and are left out.
    """
    slots: dict[str, int] = {}
    in_row = False
    statement = ""
    for line in Path(atlas_palette_cs_path).read_text(encoding="utf-8").splitlines():
        text = line.strip()
        if re.match(r"//\s*Row \d+\b", text):
            in_row, statement = True, ""
            continue
        if not text:
            in_row = False
            continue
        if not in_row:
            continue
        statement += " " + text
        if not statement.rstrip().endswith(";"):
            continue                                       # a declaration that goes on over the next line
        m = re.match(r"\s*public const int (.*);\s*$", statement)
        statement = ""
        if not m:
            continue
        for name, value in re.findall(r"(\w+)\s*=\s*(\d+)", m.group(1)):
            slots[name] = int(value)
    return slots


def palette_uv(index: int) -> tuple[float, float]:
    """AtlasPalette.Uv: the centre of cell index (0..255), v upwards."""
    column = index & (CELLS_PER_SIDE - 1)
    row = (index >> 4) & (CELLS_PER_SIDE - 1)
    return ((column + 0.5) / CELLS_PER_SIDE, (row + 0.5) / CELLS_PER_SIDE)


def slot_for_material(material_name, slots: dict[str, int], object_name: str) -> int:
    """The palette cell a `pal_<Slot>` material stands for. ValueError naming the object for anything else."""
    if not material_name:
        raise ValueError(f"{object_name}: a face has no material; every face needs a {MATERIAL_PREFIX}<Slot> material")
    slot = material_name[len(MATERIAL_PREFIX):] if material_name.startswith(MATERIAL_PREFIX) else None
    if slot not in slots:
        raise ValueError(f"{object_name}: material '{material_name}' is not {MATERIAL_PREFIX}<Slot> for a slot of "
                         f"AtlasPalette ({', '.join(sorted(slots))})")
    return slots[slot]


def face_shade(normal) -> float:
    """AtlasShading.FaceShade of a unit normal in Unity's frame."""
    d = sum(normal[i] * LIGHT_DIRECTION[i] for i in range(3))
    return SHADE_FLOOR + (1.0 - SHADE_FLOOR) * (0.5 + 0.5 * d)


# ---- Blender's frame ---------------------------------------------------------------------------------------

def blender_to_unity(p) -> tuple[float, float, float]:
    """Blender (x, y, z), Z up, to Unity (x, z, y): Blender +X is right, +Y forward, +Z up. Also for normals."""
    return (p[0], p[2], p[1])


def blender_triangle(a, b, c):
    """The corners of a Blender triangle in Unity's order: swapping y and z is a mirror, so the winding turns."""
    return (a, c, b)


# ---- Checks ------------------------------------------------------------------------------------------------

def check_piece(name: str, piece: Piece, max_triangles: int) -> None:
    """Raises ValueError naming the piece when it cannot ship: empty, arrays of different lengths, over
    budget, an index out of range, a normal that is not unit length, or an open edge. Corners split for flat
    normals or palette seams are welded on position (WELD_TOLERANCE) before edges are counted."""
    count = len(piece.positions)
    if count == 0 or not piece.indices:
        raise ValueError(f"{name}: the piece is empty")
    for label in ("normals", "uv0", "uv1", "shade"):
        n = len(getattr(piece, label))
        if n != count:
            raise ValueError(f"{name}: {label} has {n} entries for {count} positions")
    if len(piece.indices) % 3:
        raise ValueError(f"{name}: {len(piece.indices)} indices is not a whole number of triangles")
    if piece.triangles > max_triangles:
        raise ValueError(f"{name}: {piece.triangles} triangles, over the budget of {max_triangles}")
    for k, i in enumerate(piece.indices):
        if not 0 <= i < count:
            raise ValueError(f"{name}: index {i} at position {k} is out of range ({count} vertices)")
    for k, n in enumerate(piece.normals):
        length = math.sqrt(sum(c * c for c in n))
        if abs(length - 1.0) > NORMAL_TOLERANCE:
            raise ValueError(f"{name}: normal {k} is not unit length ({length:.4f})")

    welded = _weld(piece.positions, WELD_TOLERANCE)
    uses: dict[tuple[int, int], int] = {}
    for k in range(0, len(piece.indices), 3):
        corners = [welded[i] for i in piece.indices[k:k + 3]]
        for e in range(3):
            a, b = corners[e], corners[(e + 1) % 3]
            if a != b:
                key = (a, b) if a < b else (b, a)
                uses[key] = uses.get(key, 0) + 1
    open_edges = [edge for edge, n in uses.items() if n == 1]
    if open_edges:
        at = piece.positions[open_edges[0][0]]
        raise ValueError(f"{name}: {len(open_edges)} open edge(s); the piece must be closed "
                         f"(first near {tuple(round(c, 4) for c in at)})")


def _weld(positions, tolerance) -> list[int]:
    """Per position, the index of the first position within tolerance of it on every axis (itself if none)."""
    buckets: dict[tuple[int, int, int], list[int]] = {}
    ids: list[int] = []
    for k, p in enumerate(positions):
        cell = tuple(int(math.floor(c / tolerance)) for c in p)
        ids.append(next((ids[j] for offset in _NEIGHBOURS
                         for j in buckets.get(tuple(cell[i] + offset[i] for i in range(3)), ())
                         if all(abs(p[i] - positions[j][i]) <= tolerance for i in range(3))), k))
        buckets.setdefault(cell, []).append(k)
    return ids


_NEIGHBOURS = list(itertools.product((-1, 0, 1), repeat=3))


# ---- GLB ---------------------------------------------------------------------------------------------------

def _pad(data: bytes, fill: bytes) -> bytes:
    return data + fill * ((4 - len(data) % 4) % 4)


def _f32(x: float) -> float:
    return struct.unpack("<f", struct.pack("<f", x))[0]


def _shade_byte(shade: float) -> int:
    """AtlasShading.ToByte: round(255 * saturate(shade))."""
    return int(round(255.0 * min(1.0, max(0.0, shade))))


def write_glb(piece: Piece) -> bytes:
    """The piece as a GLB that GlbFile.TryParse and GlbMeshReader.TryRead accept. Deterministic."""
    count = len(piece.positions)
    if count == 0 or count > 65535:
        raise ValueError(f"{count} vertices; a piece has 1 to 65535 (16-bit indices)")
    # Right-handed in the file: x negated (+ 0.0 keeps -0.0 out), v downwards, winding reversed.
    gl_positions = [(_f32(-p[0] + 0.0), _f32(p[1]), _f32(p[2])) for p in piece.positions]
    gl_normals = [(-n[0] + 0.0, n[1], n[2]) for n in piece.normals]
    gl_uv0 = [(t[0], 1.0 - t[1]) for t in piece.uv0]
    gl_uv1 = [(t[0], 1.0 - t[1]) for t in piece.uv1]
    colours = [(_shade_byte(s), 255, 255, 255) for s in piece.shade]
    gl_indices = []
    for k in range(0, len(piece.indices), 3):
        a, b, c = piece.indices[k:k + 3]
        gl_indices.extend((a, c, b))

    chunks = [
        (b"".join(struct.pack("<H", i) for i in gl_indices), _ELEMENT_ARRAY_BUFFER),
        (b"".join(struct.pack("<3f", *p) for p in gl_positions), _ARRAY_BUFFER),
        (b"".join(struct.pack("<3f", *n) for n in gl_normals), _ARRAY_BUFFER),
        (b"".join(struct.pack("<2f", *t) for t in gl_uv0), _ARRAY_BUFFER),
        (b"".join(struct.pack("<2f", *t) for t in gl_uv1), _ARRAY_BUFFER),
        (b"".join(struct.pack("<4B", *c) for c in colours), _ARRAY_BUFFER),
    ]
    blob, views = b"", []
    for data, target in chunks:
        blob = _pad(blob, b"\x00")
        views.append({"buffer": 0, "byteOffset": len(blob), "byteLength": len(data), "target": target})
        blob += data
    blob = _pad(blob, b"\x00")

    lo = [min(p[i] for p in gl_positions) for i in range(3)]
    hi = [max(p[i] for p in gl_positions) for i in range(3)]
    doc = {
        "asset": {"version": "2.0", "generator": "ATLAS tools/kit/kitglb.py"},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": [{"mesh": 0}],
        "meshes": [{"primitives": [{
            "attributes": {"POSITION": 1, "NORMAL": 2, "TEXCOORD_0": 3, "TEXCOORD_1": 4, "COLOR_0": 5},
            "indices": 0, "mode": 4}]}],
        "accessors": [
            {"bufferView": 0, "componentType": _UNSIGNED_SHORT, "count": len(gl_indices), "type": "SCALAR"},
            {"bufferView": 1, "componentType": _FLOAT, "count": count, "type": "VEC3", "min": lo, "max": hi},
            {"bufferView": 2, "componentType": _FLOAT, "count": count, "type": "VEC3"},
            {"bufferView": 3, "componentType": _FLOAT, "count": count, "type": "VEC2"},
            {"bufferView": 4, "componentType": _FLOAT, "count": count, "type": "VEC2"},
            {"bufferView": 5, "componentType": _UNSIGNED_BYTE, "normalized": True, "count": count, "type": "VEC4"},
        ],
        "bufferViews": views,
        "buffers": [{"byteLength": len(blob)}],
    }
    text = _pad(json.dumps(doc, separators=(",", ":")).encode("utf-8"), b" ")
    total = 12 + 8 + len(text) + 8 + len(blob)
    return b"".join([
        struct.pack("<III", 0x46546C67, 2, total),
        struct.pack("<II", len(text), 0x4E4F534A), text,
        struct.pack("<II", len(blob), 0x004E4942), blob,
    ])


def read_glb(glb: bytes) -> tuple[dict, bytes]:
    """The JSON document and the BIN chunk of a GLB this module wrote."""
    json_length = struct.unpack_from("<I", glb, 12)[0]
    doc = json.loads(glb[20:20 + json_length].decode("utf-8"))
    bin_length = struct.unpack_from("<I", glb, 20 + json_length)[0]
    return doc, glb[28 + json_length:28 + json_length + bin_length]


def triangle_count(glb: bytes) -> int:
    doc, _ = read_glb(glb)
    primitive = doc["meshes"][0]["primitives"][0]
    return doc["accessors"][primitive["indices"]]["count"] // 3


def write_manifest(pieces: dict[str, bytes]) -> bytes:
    """{"pieces": {name: {"triangles": n, "sha256": hex}}}: what KitSet checks every piece file against."""
    entries = {name: {"triangles": triangle_count(glb), "sha256": hashlib.sha256(glb).hexdigest()}
               for name, glb in pieces.items()}
    return (json.dumps({"pieces": entries}, indent=2, sort_keys=True) + "\n").encode("utf-8")
