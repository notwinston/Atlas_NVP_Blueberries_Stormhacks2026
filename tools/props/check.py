#!/usr/bin/env python3
"""Validates a prop library against the prop format (standalone-phone plan section 6.3). Exits 1 on any failure.

    tools/props/.venv/bin/python tools/props/check.py [DIR ...]

Each DIR holds `manifest.json` and one `<id>.glb.bytes` per prop (default: the phone's library,
ATLAS/Assets/Atlas/Telemetry/Resources/AtlasProps). The test fixtures are checked with
`check.py ATLAS/Assets/Atlas/Tests/Objects/Fixtures/props_min`.

The rules, in the order they are checked:

  manifest  schema "atlas.props/1"; ids unique, [a-z0-9_], at most 64 characters (RoomObject.Prop);
            category an ObjectCategory JSON name other than unknown and other; at most 16 tags of at most 48
            characters each, no repeats (RoomObject.Tags); size three numbers above 0; 1 to 8 slots
            (RoomObject.PropCells), names unique and not empty, cells distinct and in 64..127; source not
            empty; licence CC0-1.0; no .glb.bytes in the folder that the manifest does not name.
  file      a GLB that GlbMeshReader reads (the kit contract, tools/kit/kitglb.py); sha256 and triangles equal
            the manifest's; 300 to 2,500 triangles.
  mesh      kitglb.check_piece with the edge rule: closed, unit normals, no edge over 0.25 m; every front face
            agrees with its normals (clockwise in Unity's frame); COLOR_0 blue is 255 (not batched).
  frame     metres, box-local: the lowest point at y = 0, the bounds centred on x = 0 and z = 0 (within
            0.1 mm), and the bounds equal to `size` (within 1 mm).
  cells     every face lies in one palette cell, that cell is one of the prop's slot cells, and every slot is used.
  profile   the manifest's profile is of_mesh of the file at `size` (profile_tool.py), byte for byte.
"""
from __future__ import annotations

import argparse
import base64
import binascii
import math
import re
import sys
from pathlib import Path

import numpy as np

import profile_tool as pt

MIN_TRIANGLES, MAX_TRIANGLES = 300, 2500
MAX_SLOTS = 8
MAX_TAGS, MAX_TAG_LENGTH = 16, 48
MAX_ID_LENGTH = 64
OBJECT_CELLS = range(64, 128)
LICENCES = {"CC0-1.0"}
FRAME_TOLERANCE = 1e-4   # metres: base and centring
SIZE_TOLERANCE = 1e-3    # metres: bounds against `size`
ID_PATTERN = re.compile(r"^[a-z0-9_]+$")
OBJECT_CATEGORY_CS = pt.ASSETS / "World" / "Room" / "ObjectCategory.cs"


def category_names() -> list[str]:
    """The ObjectCategory JSON names, read from ObjectCategories.s_names so the two cannot drift apart."""
    text = OBJECT_CATEGORY_CS.read_text(encoding="utf-8")
    block = re.search(r"s_names\s*=\s*\{(.*?)\};", text, re.S)
    if not block:
        raise RuntimeError(f"cannot find ObjectCategories.s_names in {OBJECT_CATEGORY_CS}")
    return re.findall(r'"([A-Za-z]+)"', block.group(1))


def _number(value) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def check_entry(folder: Path, entry: dict, categories: set[str]) -> list[str]:
    """Every rule broken by one manifest entry and its file, as messages naming the prop."""
    errors: list[str] = []
    pid = entry.get("id")
    name = pid if isinstance(pid, str) else repr(pid)

    def fail(message: str) -> None:
        errors.append(f"{name}: {message}")

    if not isinstance(pid, str) or not ID_PATTERN.match(pid) or len(pid) > MAX_ID_LENGTH:
        fail(f"id must be 1..{MAX_ID_LENGTH} characters of [a-z0-9_]")
        return errors
    category = entry.get("category")
    if category not in categories:
        fail(f"category {category!r} is not an ObjectCategory JSON name ({', '.join(sorted(categories))})")
    tags = entry.get("tags", None)
    if not isinstance(tags, list) or not all(isinstance(t, str) and 0 < len(t) <= MAX_TAG_LENGTH for t in tags) \
            or len(tags) > MAX_TAGS or len(set(tags)) != len(tags):
        fail(f"tags must be a list of at most {MAX_TAGS} distinct, non-empty strings "
             f"of at most {MAX_TAG_LENGTH} characters")
    size = entry.get("size")
    if not isinstance(size, list) or len(size) != 3 or not all(_number(s) and s > 0 for s in size):
        fail("size must be three numbers above 0, [w, h, d] in metres")
        size = None
    slots = entry.get("slots")
    slot_cells: list[int] = []
    if not isinstance(slots, list) or not 1 <= len(slots) <= MAX_SLOTS:
        fail(f"slots must be a list of 1..{MAX_SLOTS} entries")
    else:
        names = [s.get("name") if isinstance(s, dict) else None for s in slots]
        slot_cells = [s.get("cell") if isinstance(s, dict) else None for s in slots]
        if not all(isinstance(n, str) and n for n in names) or len(set(names)) != len(names):
            fail("every slot needs a distinct, non-empty name")
        if not all(isinstance(c, int) and not isinstance(c, bool) and c in OBJECT_CELLS for c in slot_cells):
            fail(f"every slot cell must be an object palette cell, {OBJECT_CELLS.start}..{OBJECT_CELLS.stop - 1}")
            slot_cells = []
        elif len(set(slot_cells)) != len(slot_cells):
            fail("two slots share a default cell; each slot is marked by its own cell")
            slot_cells = []
    if not isinstance(entry.get("source"), str) or not entry["source"].strip():
        fail("source is missing")
    if entry.get("licence") not in LICENCES:
        fail(f"licence {entry.get('licence')!r} is not one of {sorted(LICENCES)}")

    path = pt.glb_path(folder, pid)
    if not path.is_file():
        fail(f"{path.name} is missing")
        return errors
    data = path.read_bytes()
    if entry.get("sha256") != pt.sha256(data):
        fail(f"sha256 differs from the file ({pt.sha256(data)}); run profile_tool.py --write")
    try:
        mesh = pt.read_prop_glb(data)
    except pt.PropFileError as exc:
        fail(f"{path.name}: {exc}")
        return errors
    if entry.get("triangles") != mesh.triangles:
        fail(f"triangles is {entry.get('triangles')!r}; the file has {mesh.triangles}")
    if not MIN_TRIANGLES <= mesh.triangles <= MAX_TRIANGLES:
        fail(f"{mesh.triangles} triangles, outside {MIN_TRIANGLES}..{MAX_TRIANGLES}")

    piece = mesh.to_piece()
    try:
        kitglb_check(pid, piece)
    except ValueError as exc:
        errors.append(str(exc))
    errors.extend(f"{name}: {m}" for m in _winding(piece))
    if (mesh.colours[:, 2] != 255).any():
        fail("COLOR_0 blue must be 255 (not batched) on every vertex")

    unity = np.array(piece.positions)
    lo, hi = unity.min(axis=0), unity.max(axis=0)
    if abs(lo[1]) > FRAME_TOLERANCE:
        fail(f"the base is at y = {lo[1]:.5f}; the origin must be at the centre of the base")
    for axis, label in ((0, "x"), (2, "z")):
        centre = 0.5 * (lo[axis] + hi[axis])
        if abs(centre) > FRAME_TOLERANCE:
            fail(f"the bounds are centred on {label} = {centre:.5f}, not 0")
    if size is not None:
        extent = hi - lo
        if np.abs(extent - np.asarray(size, dtype=np.float64)).max() > SIZE_TOLERANCE:
            fail(f"the bounds are {[round(float(e), 4) for e in extent]}, not size {size}")

    if slot_cells:
        cells = mesh.cells()[mesh.faces]
        mixed = int((cells.min(axis=1) != cells.max(axis=1)).sum())
        if mixed:
            fail(f"{mixed} face(s) span two palette cells; every face lies in one slot cell")
        face_cells = cells[:, 0]
        foreign = sorted(set(int(c) for c in np.unique(face_cells)) - set(slot_cells))
        if foreign:
            fail(f"faces use cell(s) {foreign}, which are not slot cells {slot_cells}")
        unused = [c for c in slot_cells if not (face_cells == c).any()]
        if unused:
            fail(f"slot cell(s) {unused} mark no face")

    if size is not None:
        expected = pt.encode(pt.profile_of(mesh, size))
        profile = entry.get("profile")
        try:
            ok = isinstance(profile, str) and len(base64.b64decode(profile, validate=True)) == 256
        except (binascii.Error, ValueError):
            ok = False
        if not ok:
            fail("profile must be 256 bytes of standard base64")
        elif profile != expected:
            fail("profile differs from of_mesh of the file at its size; run profile_tool.py --write")
    return errors


def kitglb_check(pid: str, piece) -> None:
    pt.kitglb.check_piece(pid, piece, MAX_TRIANGLES, check_edges=True)


def _winding(piece) -> list[str]:
    """Faces whose front (clockwise in Unity's frame: cross(b - a, c - a)) points against their normals."""
    p = np.asarray(piece.positions)
    n = np.asarray(piece.normals)
    f = np.asarray(piece.indices, dtype=np.int64).reshape(-1, 3)
    geometric = np.cross(p[f[:, 1]] - p[f[:, 0]], p[f[:, 2]] - p[f[:, 0]])
    area = np.linalg.norm(geometric, axis=1)
    facing = (geometric * n[f].sum(axis=1)).sum(axis=1)
    flipped = int(((area > 1e-12) & (facing <= 0.0)).sum())
    if not flipped:
        return []
    return [f"{flipped} face(s) wind against their normals (front faces are clockwise in Unity's frame)"]


def check_dir(folder: Path) -> list[str]:
    """Every failure in one library folder, as messages. Empty when the folder passes."""
    folder = Path(folder)
    manifest_path = folder / pt.MANIFEST
    if not manifest_path.is_file():
        return [f"{manifest_path}: missing"]
    try:
        manifest = pt.load_manifest(folder)
    except ValueError as exc:
        return [f"{manifest_path}: not JSON ({exc})"]
    if not isinstance(manifest, dict) or manifest.get("schema") != pt.SCHEMA \
            or not isinstance(manifest.get("props"), list):
        return [f"{manifest_path}: schema must be {pt.SCHEMA!r} with a list of props"]

    categories = set(category_names()) - {"unknown", "other"}
    errors: list[str] = []
    ids: list[str] = []
    for entry in manifest["props"]:
        if not isinstance(entry, dict):
            errors.append(f"{manifest_path}: a prop entry is not an object")
            continue
        ids.append(entry.get("id"))
        errors.extend(check_entry(folder, entry, categories))
    duplicates = sorted({i for i in ids if isinstance(i, str) and ids.count(i) > 1})
    if duplicates:
        errors.append(f"{manifest_path}: duplicate ids {duplicates}")
    named = {i + pt.GLB_SUFFIX for i in ids if isinstance(i, str)}
    orphans = sorted(p.name for p in folder.glob("*" + pt.GLB_SUFFIX) if p.name not in named)
    if orphans:
        errors.append(f"{folder}: files the manifest does not name: {orphans}")
    return errors


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("dirs", nargs="*", type=Path, default=[pt.PROPS_DIR])
    args = ap.parse_args(argv)
    failed = False
    for folder in args.dirs:
        errors = check_dir(folder)
        if errors:
            failed = True
            print(f"FAIL {folder}")
            for message in errors:
                print(f"  {message}")
        else:
            count = len(pt.load_manifest(folder)["props"])
            print(f"OK   {folder}: {count} prop(s)")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
