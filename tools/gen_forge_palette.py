#!/usr/bin/env python3
"""Writes the Forge's list of object colours from the palette formulas of lane A.

The 64 object colours are cells 64 to 127 of the shared 16 x 16 palette (docs/contracts/
lane-a-platform.md section 10.7; lane-d-link.md section 4.1): rows 4 to 7, index 64 + 16 k + column,
value V = 0.28, 0.42, 0.58, 0.74 for k = 0 .. 3; columns 0 .. 11 are HSV(column * 30 deg, 0.50, V),
column 12 HSV(30, 0.12, V), 13 HSV(210, 0.12, V), 14 HSV(28, 0.35, V), 15 HSV(0, 0, V).

For each colour the file gives the index (frozen: it is `AssetReady.palette` and the cell a built-in
mesh names in its manifest), the cell-centre UV in Unity's convention, and the sRGB colour.

    gen_forge_palette.py                 print the JSON
    gen_forge_palette.py --out FILE      write it (station/forge/forge/palette.json)
    gen_forge_palette.py --check         compare with the table the Forge computes in forge/palette.py

Python standard library only. Deterministic. If lane A retunes a colour, change the constants here and
in `AtlasPalette`, and run the script again.
"""
from __future__ import annotations

import argparse
import colorsys
import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent

OBJECT_BASE = 64
CELLS_PER_SIDE = 16
VALUES = (0.28, 0.42, 0.58, 0.74)
HUE_NAMES = ("red", "orange", "yellow", "lime", "green", "spring", "cyan", "azure", "blue", "violet", "magenta", "rose")
# Columns 12 to 15: name, hue in degrees, saturation.
NEUTRALS = (("warm grey", 30.0, 0.12), ("cool grey", 210.0, 0.12), ("wood", 28.0, 0.35), ("grey", 0.0, 0.0))


def object_colours() -> list[dict]:
    entries = []
    for k, value in enumerate(VALUES):
        for column in range(CELLS_PER_SIDE):
            if column < 12:
                name, hue, saturation = HUE_NAMES[column], column * 30.0, 0.50
            else:
                name, hue, saturation = NEUTRALS[column - 12]
            r, g, b = colorsys.hsv_to_rgb(hue / 360.0, saturation, value)
            index = OBJECT_BASE + CELLS_PER_SIDE * k + column
            rgb = [int(round(r * 255)), int(round(g * 255)), int(round(b * 255))]
            entries.append({
                "index": index,
                "name": f"{name} {k}",
                # `AtlasPalette.Uv`: v from the bottom. A GLB stores (u, 1 - v).
                "uv": [(index % CELLS_PER_SIDE + 0.5) / CELLS_PER_SIDE, (index // CELLS_PER_SIDE + 0.5) / CELLS_PER_SIDE],
                "srgb": rgb,
                "hex": "#%02x%02x%02x" % tuple(rgb),
            })
    return entries


def document() -> str:
    return json.dumps({"version": 1, "objectBase": OBJECT_BASE, "cellsPerSide": CELLS_PER_SIDE,
                       "colours": object_colours()}, indent=2) + "\n"


def check() -> int:
    """The Forge computes the same table at import (forge/palette.py); the two must agree."""
    sys.path.insert(0, str(REPO / "station" / "forge"))
    try:
        from forge import palette as forge_palette
    except ImportError as error:
        print(f"cannot import the Forge's palette (run with the Forge's interpreter): {error}")
        return 2
    ours = object_colours()
    theirs = forge_palette.PALETTE
    wrong = [e["index"] for e, t in zip(ours, theirs)
             if e["index"] != t.index or tuple(e["srgb"]) != tuple(t.rgb) or tuple(e["uv"]) != tuple(forge_palette.cell_uv(t.index))]
    if len(ours) != len(theirs) or wrong:
        print(f"palette differs from the Forge's: {len(ours)} against {len(theirs)} colours, cells {wrong}")
        return 1
    print(f"{len(ours)} object colours agree with station/forge/forge/palette.py")
    return 0


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", type=Path, default=None, help="write the JSON to this file instead of printing it")
    parser.add_argument("--check", action="store_true", help="compare with the Forge's own table")
    args = parser.parse_args(argv)
    if args.check:
        return check()
    text = document()
    if args.out is None:
        sys.stdout.write(text)
    elif not args.out.is_file() or args.out.read_text() != text:
        args.out.write_text(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
