#!/usr/bin/env python3
"""Writes the built-in fallback furniture set (Master Plan section 4.5, task P1-D12).

One template mesh per object category, used on the phone when the Forge cannot
deliver a generated mesh (docs/contracts/lane-d-link.md section 4.4). Every
template is the team's own geometry, released CC0: a short list of boxes that
fills the unit box (x and z in [-0.5, 0.5], y in [0, 1]). The phone scales a
template to the scanned box, splits its long edges and bakes shade and palette
(`FallbackMeshBuilder`).

Output, into ATLAS/Assets/Atlas/Telemetry/Resources/AtlasFallback:

    <category>.glb.bytes    one GLB each: one mesh, POSITION and NORMAL, 16-bit indices
    manifest.json           category -> file, palette cell, triangle count

The files are right-handed glTF like every asset of the contract (section 4.1):
x is stored negated and the triangles are counter-clockwise; the reader on the
phone turns both back.

Deterministic: running the script again changes no byte. Python standard library
only. `--check` compares instead of writing and exits 1 on a difference.
`.meta` files come from tools/gen_meta.py.
"""
from __future__ import annotations

import argparse
import json
import struct
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
OUT_DIR = REPO / "ATLAS" / "Assets" / "Atlas" / "Telemetry" / "Resources" / "AtlasFallback"

MAX_TEMPLATE_TRIANGLES = 300
OBJECT_BASE = 64  # first object colour of the palette (lane A section 10.7)

# Palette columns of the object colours: twelve hues, then four neutrals.
RED, ORANGE, YELLOW, LIME, GREEN, SPRING, CYAN, AZURE, BLUE, VIOLET, MAGENTA, ROSE = range(12)
WARM_GREY, COOL_GREY, WOOD, GREY = 12, 13, 14, 15


def cell(column: int, value_step: int) -> int:
    """`AtlasPalette.Object`: the cell of an object colour."""
    return OBJECT_BASE + 16 * value_step + column


def legs(height: float, width: float) -> list[tuple]:
    """Four legs at the corners of the unit box, from the floor to `height`."""
    lo, hi = -0.5, 0.5
    return [
        (lo, lo + width, 0.0, height, lo, lo + width),
        (hi - width, hi, 0.0, height, lo, lo + width),
        (lo, lo + width, 0.0, height, hi - width, hi),
        (hi - width, hi, 0.0, height, hi - width, hi),
    ]


# Category -> (palette cell, boxes). A box is (x0, x1, y0, y1, z0, z1) in the unit box.
# The order of this table is the order of ObjectCategory (lane C section 6).
TEMPLATES: dict[str, tuple[int, list[tuple]]] = {
    "couch": (cell(AZURE, 2), [
        (-0.5, 0.5, 0.0, 0.45, -0.5, 0.5),          # seat
        (-0.5, 0.5, 0.45, 1.0, -0.5, -0.25),        # back
        (-0.5, -0.38, 0.45, 0.7, -0.25, 0.5),       # arms
        (0.38, 0.5, 0.45, 0.7, -0.25, 0.5),
    ]),
    "table": (cell(WOOD, 2), [(-0.5, 0.5, 0.92, 1.0, -0.5, 0.5)] + legs(0.92, 0.08)),
    "bed": (cell(BLUE, 3), [
        (-0.5, 0.5, 0.0, 0.55, -0.5, 0.5),          # mattress
        (-0.5, 0.5, 0.55, 1.0, -0.5, -0.44),        # headboard
    ]),
    "lamp": (cell(YELLOW, 3), [
        (-0.3, 0.3, 0.0, 0.04, -0.3, 0.3),          # foot
        (-0.05, 0.05, 0.04, 0.7, -0.05, 0.05),      # pole
        (-0.5, 0.5, 0.7, 1.0, -0.5, 0.5),           # shade
    ]),
    "plant": (cell(GREEN, 1), [
        (-0.3, 0.3, 0.0, 0.3, -0.3, 0.3),           # pot
        (-0.05, 0.05, 0.3, 0.5, -0.05, 0.05),       # stem
        (-0.5, 0.5, 0.5, 0.85, -0.5, 0.5),          # foliage
        (-0.3, 0.3, 0.85, 1.0, -0.3, 0.3),
    ]),
    "screen": (cell(GREY, 0), [
        (-0.25, 0.25, 0.0, 0.04, -0.5, 0.5),        # foot
        (-0.05, 0.05, 0.04, 0.14, -0.15, 0.15),     # neck
        (-0.5, 0.5, 0.14, 1.0, -0.2, 0.2),          # panel
    ]),
    "storage": (cell(WOOD, 1), [
        (-0.5, 0.5, 0.0, 1.0, -0.5, 0.5),
    ]),
    "bathtub": (cell(AZURE, 3), [
        (-0.5, 0.5, 0.0, 1.0, -0.5, -0.4),          # rim, four walls
        (-0.5, 0.5, 0.0, 1.0, 0.4, 0.5),
        (-0.5, -0.42, 0.0, 1.0, -0.4, 0.4),
        (0.42, 0.5, 0.0, 1.0, -0.4, 0.4),
        (-0.42, 0.42, 0.0, 0.25, -0.4, 0.4),        # bottom
    ]),
    "chair": (cell(ORANGE, 2), [
        (-0.5, 0.5, 0.42, 0.5, -0.5, 0.5),          # seat
        (-0.5, 0.5, 0.5, 1.0, -0.5, -0.4),          # back
    ] + legs(0.42, 0.1)),
    "dishwasher": (cell(COOL_GREY, 2), [
        (-0.5, 0.5, 0.0, 0.08, -0.5, 0.4),          # plinth
        (-0.5, 0.5, 0.08, 1.0, -0.5, 0.45),         # body
        (-0.3, 0.3, 0.8, 0.84, 0.45, 0.5),          # handle
    ]),
    "fireplace": (cell(RED, 1), [
        (-0.5, -0.25, 0.0, 0.8, -0.5, 0.5),         # jambs
        (0.25, 0.5, 0.0, 0.8, -0.5, 0.5),
        (-0.5, 0.5, 0.8, 1.0, -0.5, 0.5),           # lintel
        (-0.25, 0.25, 0.0, 0.8, -0.5, -0.3),        # back of the hearth
    ]),
    "oven": (cell(GREY, 1), [
        (-0.5, 0.5, 0.0, 1.0, -0.5, 0.45),
        (-0.35, 0.35, 0.72, 0.76, 0.45, 0.5),       # handle
    ]),
    "refrigerator": (cell(WARM_GREY, 3), [
        (-0.5, 0.5, 0.0, 1.0, -0.5, 0.45),
        (0.3, 0.36, 0.42, 0.6, 0.45, 0.5),          # handles of the two doors
        (0.3, 0.36, 0.7, 0.9, 0.45, 0.5),
    ]),
    "sink": (cell(CYAN, 3), [
        (-0.5, 0.5, 0.0, 0.8, -0.5, 0.5),           # cabinet
        (-0.5, 0.5, 0.8, 1.0, -0.5, -0.4),          # rim of the basin
        (-0.5, 0.5, 0.8, 1.0, 0.4, 0.5),
        (-0.5, -0.42, 0.8, 1.0, -0.4, 0.4),
        (0.42, 0.5, 0.8, 1.0, -0.4, 0.4),
    ]),
    "stairs": (cell(WOOD, 3), [
        (-0.5, 0.5, 0.0, 0.25, -0.5, -0.25),
        (-0.5, 0.5, 0.0, 0.5, -0.25, 0.0),
        (-0.5, 0.5, 0.0, 0.75, 0.0, 0.25),
        (-0.5, 0.5, 0.0, 1.0, 0.25, 0.5),
    ]),
    "stove": (cell(GREY, 2), [
        (-0.5, 0.5, 0.0, 0.95, -0.5, 0.5),
        (-0.35, -0.1, 0.95, 1.0, -0.35, -0.1),      # burners
        (0.1, 0.35, 0.95, 1.0, -0.35, -0.1),
        (-0.35, -0.1, 0.95, 1.0, 0.1, 0.35),
        (0.1, 0.35, 0.95, 1.0, 0.1, 0.35),
        (-0.5, 0.5, 0.95, 1.0, -0.5, -0.45),        # back rail
    ]),
    "toilet": (cell(GREY, 3), [
        (-0.3, 0.3, 0.0, 0.45, -0.5, -0.15),        # pedestal
        (-0.5, 0.5, 0.45, 1.0, -0.5, -0.15),        # cistern
        (-0.38, 0.38, 0.0, 0.45, -0.15, 0.5),       # bowl
    ]),
    "washerDryer": (cell(COOL_GREY, 3), [
        (-0.5, 0.5, 0.0, 1.0, -0.5, 0.45),
        (-0.3, 0.3, 0.3, 0.7, 0.45, 0.5),           # door
    ]),
    "keyboard": (cell(GREY, 0), [
        (-0.5, 0.5, 0.0, 0.6, -0.5, 0.5),
        (-0.5, 0.5, 0.6, 1.0, -0.5, -0.3),          # raised rear row
    ]),
    "mouse": (cell(COOL_GREY, 1), [
        (-0.5, 0.5, 0.0, 0.6, -0.5, 0.5),
        (-0.4, 0.4, 0.6, 1.0, -0.35, 0.3),          # hump
    ]),
    "laptop": (cell(COOL_GREY, 2), [
        (-0.5, 0.5, 0.0, 0.08, -0.5, 0.5),          # base
        (-0.5, 0.5, 0.08, 1.0, -0.5, -0.44),        # lid
    ]),
}

# The six faces of a box as (normal, four corners). Corners are listed so that the two
# triangles (0, 1, 2) and (0, 2, 3) have cross(b - a, c - a) along the normal, which is
# Unity's clockwise front face in its left-handed frame.
def _faces(box: tuple) -> list[tuple]:
    x0, x1, y0, y1, z0, z1 = box
    faces = [
        ((0.0, 1.0, 0.0), [(x0, y1, z0), (x0, y1, z1), (x1, y1, z1), (x1, y1, z0)]),
        ((1.0, 0.0, 0.0), [(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)]),
        ((-1.0, 0.0, 0.0), [(x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0)]),
        ((0.0, 0.0, 1.0), [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]),
        ((0.0, 0.0, -1.0), [(x0, y0, z0), (x0, y1, z0), (x1, y1, z0), (x1, y0, z0)]),
    ]
    if y0 > 0.0:  # a box that stands on the floor needs no underside
        faces.append(((0.0, -1.0, 0.0), [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)]))
    return faces


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def build_mesh(boxes: list[tuple]) -> tuple[list[tuple], list[tuple], list[int]]:
    """Positions, normals and indices in the box-local frame (Unity's convention)."""
    positions, normals, indices = [], [], []
    for box in boxes:
        for normal, corners in _faces(box):
            a, b, c = corners[0], corners[1], corners[2]
            n = _cross(tuple(b[i] - a[i] for i in range(3)), tuple(c[i] - a[i] for i in range(3)))
            assert sum(n[i] * normal[i] for i in range(3)) > 0.0, "face winding disagrees with its normal"
            first = len(positions)
            positions.extend(corners)
            normals.extend([normal] * 4)
            indices.extend([first, first + 1, first + 2, first, first + 2, first + 3])
    return positions, normals, indices


def _pad(data: bytes, fill: bytes) -> bytes:
    return data + fill * ((4 - len(data) % 4) % 4)


def write_glb(positions, normals, indices) -> bytes:
    """One mesh, POSITION and NORMAL, right-handed: x negated, winding reversed."""
    gl_positions = [(-p[0], p[1], p[2]) for p in positions]
    gl_normals = [(-n[0] + 0.0, n[1], n[2]) for n in normals]
    gl_indices = []
    for k in range(0, len(indices), 3):
        gl_indices.extend([indices[k], indices[k + 2], indices[k + 1]])

    index_bytes = b"".join(struct.pack("<H", i) for i in gl_indices)
    position_bytes = b"".join(struct.pack("<3f", *p) for p in gl_positions)
    normal_bytes = b"".join(struct.pack("<3f", *n) for n in gl_normals)

    blob, views = b"", []
    for chunk, target in ((index_bytes, 34963), (position_bytes, 34962), (normal_bytes, 34962)):
        blob = _pad(blob, b"\x00")
        views.append({"buffer": 0, "byteOffset": len(blob), "byteLength": len(chunk), "target": target})
        blob += chunk
    blob = _pad(blob, b"\x00")

    lo = [min(p[i] for p in gl_positions) for i in range(3)]
    hi = [max(p[i] for p in gl_positions) for i in range(3)]
    doc = {
        "asset": {"version": "2.0", "generator": "ATLAS gen_fallback_set", "copyright": "CC0-1.0"},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": [{"mesh": 0}],
        "meshes": [{"primitives": [{"attributes": {"POSITION": 1, "NORMAL": 2}, "indices": 0, "mode": 4}]}],
        "accessors": [
            {"bufferView": 0, "componentType": 5123, "count": len(gl_indices), "type": "SCALAR"},
            {"bufferView": 1, "componentType": 5126, "count": len(gl_positions), "type": "VEC3", "min": lo, "max": hi},
            {"bufferView": 2, "componentType": 5126, "count": len(gl_normals), "type": "VEC3"},
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


def build_all() -> dict[str, bytes]:
    """File name -> contents for the whole set."""
    files: dict[str, bytes] = {}
    entries = {}
    for name, (palette, boxes) in TEMPLATES.items():
        positions, normals, indices = build_mesh(boxes)
        triangles = len(indices) // 3
        assert triangles <= MAX_TEMPLATE_TRIANGLES, f"{name}: {triangles} triangles"
        assert OBJECT_BASE <= palette < OBJECT_BASE + 64, f"{name}: palette cell {palette}"
        for axis, (lo, hi) in enumerate(((-0.5, 0.5), (0.0, 1.0), (-0.5, 0.5))):
            values = [p[axis] for p in positions]
            assert min(values) == lo and max(values) == hi, f"{name} does not fill the unit box on axis {axis}"
        file_name = f"{name}.glb.bytes"
        files[file_name] = write_glb(positions, normals, indices)
        entries[name] = {"file": file_name, "resource": f"AtlasFallback/{name}.glb", "palette": palette,
                         "triangles": triangles, "vertices": len(positions)}
    manifest = {
        "version": 1,
        "license": "CC0-1.0",
        "source": "tools/gen_fallback_set.py",
        "maxTemplateTriangles": MAX_TEMPLATE_TRIANGLES,
        "templates": entries,
    }
    files["manifest.json"] = (json.dumps(manifest, indent=2) + "\n").encode("utf-8")
    return files


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", type=Path, default=OUT_DIR, help="output folder")
    parser.add_argument("--check", action="store_true", help="compare with the files on disk; write nothing")
    args = parser.parse_args(argv)

    files = build_all()
    stale = [name for name, data in files.items()
             if not (args.out / name).is_file() or (args.out / name).read_bytes() != data]
    if args.check:
        for name in stale:
            print(f"differs: {args.out / name}")
        print(f"{len(files) - len(stale)} of {len(files)} files are up to date")
        return 1 if stale else 0
    args.out.mkdir(parents=True, exist_ok=True)
    for name in stale:
        (args.out / name).write_bytes(files[name])
    print(f"{len(files)} files in {args.out} ({len(stale)} written)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
