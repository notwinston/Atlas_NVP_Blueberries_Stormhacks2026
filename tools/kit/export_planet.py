"""Exports the six tiles of the Small Planet (S4) world, art/blender/SmallPlanet_World.blend, into the Unity project.

Run headless (never on a .blend another Blender has open for writing; a copy is fine):

    blender -b <SmallPlanet_World.blend> -P tools/kit/export_planet.py -- <repo_root> [--radius R0] [--stats out.json]

What the file holds, and how it is read (smallplanet_build.py writes it):

- Collection `SmallPlanet_Tiles`: per tile an empty `Tile_<Name>` (the net layout, ignored) with mesh children
  `Tile_<Name>_Ground`, `_Props`, `_Fence`, whose mesh data is in that tile's chart (metres, origin at the tile
  centre, +Z up). Every child of one tile is merged into one piece, `tile<i>`, i in CubeTiling's face order
  Home, East, North, West, South, Beacon. `SmallPlanet_Globe`, `_Walker` and `_Labels` are previews, not read.
- `Tile_Home_Props` is skipped: it only holds the placeholder room kerb, and Unity draws the real room there.
  Should Home ever get other non-ground parts, faces of them inside the room square (|x|, |y| <= room half
  + 2 cm) are left out for the same reason.
- Colour: the face attribute `Pal` (palette cell) when present, otherwise the nearest cell to the corner colour
  `Col` (OKLab); faces with material `SP_Lantern` take the emissive Beacon cell. See tools/kit/planetglb.py for
  the frame (side tiles turned a quarter into Unity's chart), the 1/R0 scale and the shade.
- R0, the radius the tiles were authored at: the scene's custom property `SmallPlanet_R` (else `planet_R` on
  the Tile_<Name> empties), checked against the ground sheets (their widest chart coordinate is R0 pi/4), which
  are the fallback when neither property exists. --radius overrides all of them.

Output, into ATLAS/Assets/Atlas/Spaces/SmallPlanet/Resources/PlanetKit: `tile0..5.glb.bytes`, `manifest.json`
(KitSet's {"pieces": ...} plus a "planet" block with R0) and the missing `.meta` files, with the GUIDs
tools/gen_meta.py gives (md5 of "atlas:" + the path under ATLAS/). Nothing is written unless all six tiles pass.
"""
from __future__ import annotations

import hashlib
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import kitglb  # noqa: E402  (found next to this file)
import planetglb  # noqa: E402

COLLECTION = "SmallPlanet_Tiles"
PARTS = ("Ground", "Props", "Fence")
SKIP_OBJECTS = frozenset({"Tile_Home_Props"})   # the placeholder room kerb
SCENE_RADIUS = "SmallPlanet_R"
TILE_RADIUS = "planet_R"
COLOUR_ATTRIBUTE = "Col"
CELL_ATTRIBUTE = "Pal"
ROOM_HALF = 1.7                    # smallplanet_build.py ROOM / 2: the reference room is 3.4 m wall to wall
ROOM_MARGIN = 0.02
RADIUS_AGREEMENT = 1e-3            # relative: the six ground sheets must agree on R0
OUT_PARTS = ("ATLAS", "Assets", "Atlas", "Spaces", "SmallPlanet", "Resources", "PlanetKit")

_META_FOLDER = ("folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n"
                "  assetBundleVariant: \n")
_META_TEXT = "TextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"


def _faces_of(obj, tile: int, ground: bool, depsgraph, room_half: float):
    """The polygons of obj as planetglb.Face, plus the number left out as room kerb."""
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)
    try:
        local = obj.matrix_local
        identity = all(abs(local[i][j] - (1.0 if i == j else 0.0)) < 1e-6 for i in range(4) for j in range(4))
        if not identity:
            print(f"planet: note: {obj.name} is not at identity under its tile; its local transform is applied")
        normal_turn = local.to_3x3().inverted_safe().transposed()
        mesh.calc_loop_triangles()
        colours = mesh.color_attributes.get(COLOUR_ATTRIBUTE)
        if colours is None:
            raise ValueError(f"{obj.name}: no colour attribute '{COLOUR_ATTRIBUTE}'")
        cells = mesh.attributes.get(CELL_ATTRIBUTE)
        if cells is not None and cells.domain != "FACE":
            cells = None
        materials = [slot.material.name if slot.material else "" for slot in obj.material_slots]
        by_polygon: dict[int, list] = {}
        for triangle in mesh.loop_triangles:
            corners = tuple(tuple(local @ mesh.vertices[v].co) for v in triangle.vertices)
            by_polygon.setdefault(triangle.polygon_index, []).append(corners)
        faces, kerb = [], 0
        for polygon in mesh.polygons:
            triangles = by_polygon.get(polygon.index)
            if not triangles:
                continue
            if tile == 0 and not ground and all(max(abs(p[0]), abs(p[1])) <= room_half + ROOM_MARGIN
                                                for t in triangles for p in t):
                kerb += 1
                continue
            if colours.domain == "CORNER":
                samples = [colours.data[k].color for k in polygon.loop_indices]
            else:
                samples = [colours.data[v].color for v in polygon.vertices]
            rgb = tuple(sum(s[i] for s in samples) / len(samples) for i in range(3))
            cell = cells.data[polygon.index].value if cells is not None else None
            if cell is not None and not 0 <= cell < 256:
                cell = None
            name = materials[polygon.material_index] if polygon.material_index < len(materials) else ""
            n = (normal_turn @ polygon.normal).normalized()
            faces.append(planetglb.Face(triangles=triangles, normal=tuple(n), rgb=rgb, cell=cell,
                                        lantern=name == planetglb.LANTERN_MATERIAL, ground=ground,
                                        source=f"{obj.name} face {polygon.index}"))
        return faces, kerb
    finally:
        evaluated.to_mesh_clear()


def collect(room_half: float = ROOM_HALF):
    """tile index -> (faces, kerb faces left out, ground corner points, objects read)."""
    import bpy

    collection = bpy.data.collections.get(COLLECTION)
    if collection is None:
        raise ValueError(f"the file has no collection {COLLECTION}")
    depsgraph = bpy.context.evaluated_depsgraph_get()
    tiles = {}
    for tile, name in enumerate(planetglb.TILE_NAMES):
        faces, kerb, ground_points, read = [], 0, [], []
        prefix = f"Tile_{name}_"
        children = sorted((o for o in collection.all_objects if o.type == "MESH" and o.name.startswith(prefix)),
                          key=lambda o: (PARTS.index(o.name[len(prefix):]) if o.name[len(prefix):] in PARTS else 99,
                                         o.name))
        for obj in children:
            if obj.name in SKIP_OBJECTS:
                continue
            part = obj.name[len(prefix):]
            if part not in PARTS:
                print(f"planet: note: {obj.name} is not a {'/'.join(PARTS)} part; merged into the tile anyway")
            got, left_out = _faces_of(obj, tile, part == "Ground", depsgraph, room_half)
            faces += got
            kerb += left_out
            read.append(obj.name)
            if part == "Ground":
                ground_points += [p for f in got for t in f.triangles for p in t]
        if not any(o.endswith("_Ground") for o in read):
            raise ValueError(f"tile {name}: no mesh {prefix}Ground in {COLLECTION}")
        tiles[tile] = (faces, kerb, ground_points, read)
    return tiles


def ground_radius(tiles) -> float:
    radii = [planetglb.radius_from_ground(tiles[t][2]) for t in sorted(tiles)]
    lo, hi = min(radii), max(radii)
    if hi - lo > RADIUS_AGREEMENT * hi:
        raise ValueError(f"the ground sheets disagree on the radius: {', '.join(f'{r:.5f}' for r in radii)}")
    return sum(radii) / len(radii)


def property_radius():
    """(R0, where it was read) from the file's custom properties, or (None, None)."""
    import bpy

    scene = bpy.context.scene
    if SCENE_RADIUS in scene.keys():
        return float(scene[SCENE_RADIUS]), f"scene['{SCENE_RADIUS}']"
    values = {float(o[TILE_RADIUS]) for o in bpy.data.objects if o.name.startswith("Tile_") and TILE_RADIUS in o.keys()}
    if len(values) > 1:
        raise ValueError(f"the Tile_ empties disagree on {TILE_RADIUS}: {sorted(values)}")
    if values:
        return values.pop(), f"Tile_*['{TILE_RADIUS}']"
    return None, None


def _guid(repo_root, path):
    rel = os.path.relpath(path, os.path.join(repo_root, "ATLAS")).replace(os.sep, "/")
    return hashlib.md5(("atlas:" + rel).encode("utf-8")).hexdigest()


def _write_missing_metas(repo_root, paths):
    """The .meta files tools/gen_meta.py would write (same GUIDs), so every clone agrees before Unity imports."""
    for path in paths:
        meta = path + ".meta"
        if os.path.exists(meta):
            continue
        body = _META_FOLDER if os.path.isdir(path) else _META_TEXT
        with open(meta, "w", newline="\n") as f:
            f.write("fileFormatVersion: 2\nguid: %s\n%s" % (_guid(repo_root, path), body))


def _write_if_changed(path, data: bytes) -> bool:
    if os.path.isfile(path):
        with open(path, "rb") as f:
            if f.read() == data:
                return False
    with open(path, "wb") as f:
        f.write(data)
    return True


def export_all(repo_root, radius=None, stats_path=None, room_half=ROOM_HALF) -> dict:
    """Exports the six tiles; returns the statistics. Raises ValueError, writing nothing, when a tile fails."""
    import bpy

    repo_root = os.path.abspath(repo_root)
    tiles = collect(room_half)
    found = ground_radius(tiles)
    stored, where = property_radius()
    if stored is not None and abs(stored - found) > RADIUS_AGREEMENT * stored:
        raise ValueError(f"{where} = {stored} but the ground sheets say R0 = {found:.5f}")
    r0, where = (float(radius), "--radius") if radius else (stored, where) if stored is not None else (found, "ground")
    if radius and abs(r0 - found) > RADIUS_AGREEMENT * r0:
        print(f"planet: note: --radius {r0} differs from the ground sheets' {found:.5f}")

    files, report = {}, {"R0": r0, "R0_source": where, "R0_from_ground": found, "tiles": {}}
    for tile, (faces, kerb, ground_points, read) in sorted(tiles.items()):
        name = f"tile{tile}"
        piece, st = planetglb.build_tile(tile, faces, r0)
        planetglb.check_tile(f"{name} ({planetglb.TILE_NAMES[tile]})", piece)
        files[name] = kitglb.write_glb(piece)
        ground_tris = sum(len(f.triangles) for f in faces if f.ground)
        report["tiles"][name] = dict(
            tile=planetglb.TILE_NAMES[tile], objects=read, triangles=st.triangles, vertices=st.vertices,
            faces=st.faces, kerb_faces_left_out=kerb, ground_triangles=ground_tris,
            ground_grid=round(math.sqrt(ground_tris / 2)),
            xz_min_R=[round(st.bounds_min[0], 5), round(st.bounds_min[2], 5)],
            xz_max_R=[round(st.bounds_max[0], 5), round(st.bounds_max[2], 5)],
            y_range_m=[round(st.bounds_min[1], 4), round(st.bounds_max[1], 4)],
            cells={str(c): n for c, n in sorted(st.cells.items())},
            worst_oklab_error=round(st.worst_error, 5), worst_at=st.worst_at,
            mean_oklab_error=round(st.mean_error, 5), pal_vs_oklab_disagreements=st.pal_disagreements,
            shade_clamped=st.shade_clamped, bytes=len(files[name]))

    grids = sorted({t["ground_grid"] for t in report["tiles"].values()})
    planet = {
        "R0": round(r0, 6),
        "xz_unit": "R0 (multiply x and z by the runtime radius R)",
        "y_unit": "metres",
        "frame": "Unity tile chart of CubeTiling face i (warp mode 2: positionOS.xz = chart, y = height)",
        "tiles": list(planetglb.TILE_NAMES),
        "half_width_R": round(planetglb.HALF_WIDTH, 6),
        "ground_grid": grids[0] if len(grids) == 1 else grids,
        "source": os.path.basename(bpy.data.filepath) if bpy.data.filepath else "",
    }
    out_dir = os.path.join(repo_root, *OUT_PARTS)
    os.makedirs(out_dir, exist_ok=True)
    for name, glb in files.items():
        changed = _write_if_changed(os.path.join(out_dir, name + ".glb.bytes"), glb)
        report["tiles"][name]["written"] = "written" if changed else "unchanged"
    _write_if_changed(os.path.join(out_dir, "manifest.json"), planetglb.write_manifest(files, planet))
    resources = os.path.dirname(out_dir)
    _write_missing_metas(repo_root, [resources, out_dir] + [os.path.join(out_dir, n) for n in sorted(os.listdir(out_dir))
                                                            if not n.endswith(".meta")])
    report["total_triangles"] = sum(t["triangles"] for t in report["tiles"].values())
    for name, t in report["tiles"].items():
        print(f"planet: {name} {t['tile']:<6} {t['triangles']:5d} tris {t['vertices']:6d} verts  "
              f"xz {t['xz_min_R']}..{t['xz_max_R']} R  y {t['y_range_m']} m  worst dE {t['worst_oklab_error']:.4f}"
              f"  {t['written']}")
    print(f"planet: R0 {r0:.5f} m, {report['total_triangles']} triangles in all")
    if stats_path:
        with open(stats_path, "w", newline="\n") as f:
            json.dump(report, f, indent=2, sort_keys=True)
    return report


def main(argv) -> int:
    args = argv[argv.index("--") + 1:] if "--" in argv else []
    radius = stats = None
    room_half = ROOM_HALF
    positional = []
    k = 0
    while k < len(args):
        if args[k] in ("--radius", "--stats", "--room-half") and k + 1 < len(args):
            value = args[k + 1]
            if args[k] == "--radius":
                radius = float(value)
            elif args[k] == "--stats":
                stats = value
            else:
                room_half = float(value)
            k += 2
        else:
            positional.append(args[k])
            k += 1
    if len(positional) != 1:
        print("usage: blender -b <SmallPlanet_World.blend> -P tools/kit/export_planet.py -- <repo_root> "
              "[--radius R0] [--room-half 1.7] [--stats out.json]")
        return 2
    try:
        export_all(positional[0], radius, stats, room_half)
    except ValueError as error:
        print(f"planet: failed: {error}")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
