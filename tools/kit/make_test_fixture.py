#!/usr/bin/env python3
"""Writes the kit fixture of the KitSet tests (ATLAS/Assets/Atlas/Tests/Telemetry/Assets/KitSetTests.cs).

Output, into ATLAS/Assets/Atlas/Tests/Telemetry/Assets/Fixtures/Kit:

    probe.glb.bytes     a closed 0.2 m box written by kitglb.write_glb: the top in the Flag cell, the sides
                        and the bottom in the Landmark cell, shade baked from the face normal, and line
                        coordinates uv1 = (10 x + 1, 10 y + 2) so the test can recompute them from the position
    manifest.json       kitglb.write_manifest of that one piece

Deterministic: running the script again changes no byte. `--check` compares instead of writing and exits 1 on
a difference. `.meta` files come from tools/gen_meta.py.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

import kitglb

REPO = Path(__file__).resolve().parents[2]
OUT_DIR = REPO / "ATLAS" / "Assets" / "Atlas" / "Tests" / "Telemetry" / "Assets" / "Fixtures" / "Kit"
PALETTE_CS = REPO / "ATLAS" / "Assets" / "Atlas" / "Rendering" / "Look" / "AtlasPalette.cs"

SIZE = 0.2


def line_coordinates(p) -> tuple[float, float]:
    return (10.0 * p[0] + 1.0, 10.0 * p[1] + 2.0)


def probe() -> kitglb.Piece:
    slots = kitglb.palette_slots(PALETTE_CS)
    h = 0.5 * SIZE
    piece = kitglb.Piece()
    for n in ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)):
        u = (0, 1, 0) if n[1] == 0 else (1, 0, 0)
        v = (u[1] * n[2] - u[2] * n[1], u[2] * n[0] - u[0] * n[2], u[0] * n[1] - u[1] * n[0])  # cross(v, u) == n
        centre = (n[0] * h, h + n[1] * h, n[2] * h)
        origin = tuple(centre[i] - h * (u[i] + v[i]) for i in range(3))
        cell = slots["Flag"] if n[1] > 0 else slots["Landmark"]
        normal = tuple(float(c) for c in n)
        first = len(piece.positions)
        for s, t in ((0, 0), (1, 0), (0, 1), (1, 1)):
            p = tuple(origin[i] + SIZE * (s * u[i] + t * v[i]) for i in range(3))
            piece.positions.append(p)
            piece.normals.append(normal)
            piece.uv0.append(kitglb.palette_uv(cell))
            piece.uv1.append(line_coordinates(p))
            piece.shade.append(kitglb.face_shade(normal))
        p00, p10, p01, p11 = first, first + 1, first + 2, first + 3
        piece.indices += [p00, p01, p11, p00, p11, p10]   # clockwise seen from the front
    return piece


def build() -> dict[str, bytes]:
    piece = probe()
    kitglb.check_piece("probe", piece, kitglb.BUDGETS["avatar"])
    glb = kitglb.write_glb(piece)
    return {"probe.glb.bytes": glb, "manifest.json": kitglb.write_manifest({"probe": glb})}


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--check", action="store_true", help="compare with the files on disk; write nothing")
    args = parser.parse_args(argv)
    files = build()
    stale = [name for name, data in files.items()
             if not (OUT_DIR / name).is_file() or (OUT_DIR / name).read_bytes() != data]
    if args.check:
        for name in stale:
            print(f"differs: {OUT_DIR / name}")
        return 1 if stale else 0
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for name in stale:
        (OUT_DIR / name).write_bytes(files[name])
    print(f"{len(files)} files in {OUT_DIR} ({len(stale)} written)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
