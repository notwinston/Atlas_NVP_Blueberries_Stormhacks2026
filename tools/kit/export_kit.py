"""Exports the kit pieces of art/blender/ATLAS_Kit.blend into the Unity project (kit hero assets design, 2026-10-03).

Run headless:

    blender -b art/blender/ATLAS_Kit.blend -P tools/kit/export_kit.py -- <repo_root>

or inside a running Blender (the Blender MCP `exec`):

    import runpy; runpy.run_path(r"<repo_root>/tools/kit/export_kit.py", run_name="export_kit")["export_all"](r"<repo_root>")

What the file holds, and how it is read:

- Collection `ATLAS_Hero` with mesh objects `Hero_Landmark` (piece `landmark`), `Hero_Avatar` (piece `avatar`) and `Hero_Duck` (piece `duck`).
  A missing object skips its piece with a note; a piece that fails its checks is not written and the error is
  printed. Either way the piece's previous file, if any, stays.
- Frame: the object origin is the piece origin. Vertices are taken in object space with the modifiers applied,
  turned and scaled by the object's world rotation and scale; its location is dropped. Blender (x, y, z), Z up,
  becomes Unity (x, z, y): Blender +X is Unity's right, +Y forward, +Z up. That is a mirror, so every triangle's
  winding is turned (Blender fronts are counter-clockwise, Unity fronts clockwise); a negative object scale
  turns it once more.
- Colour: the material of each face, named `pal_<Slot>` with `<Slot>` a cell of AtlasPalette (`pal_Landmark`,
  `pal_AvatarLeft`, ...), gives the palette coordinate of every corner of the face. A face with no material or
  another name fails the piece, naming the object and the material.
- Shade: AtlasShading.FaceShade of the corner normal (the look's one fixed light, as every generator and the
  Forge bake it) times the R channel of the colour attribute `ao` (point or corner domain, float or byte
  colour). No `ao`: 1.
- Line coordinates: the UV map `lines` (v upwards, as in Unity). None: (0, 0), so no outlines.
- Normals: Blender's corner normals (flat faces and sharp edges split them), mapped like positions.

Output, into ATLAS/Assets/Atlas/Spaces/Glued/Resources/AtlasKit: `<piece>.glb.bytes` (tools/kit/kitglb.py) and
`manifest.json`, which lists every piece file in the folder with its triangles and SHA-256, and the missing
`.meta` files with the GUIDs tools/gen_meta.py gives.
"""
from __future__ import annotations

import importlib.util
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import kitglb  # noqa: E402  (found next to this file)

COLLECTION = "ATLAS_Hero"
PIECES = {"landmark": "Hero_Landmark", "avatar": "Hero_Avatar", "duck": "Hero_Duck"}
AO_ATTRIBUTE = "ao"
LINES_UV = "lines"
OUT_PARTS = ("ATLAS", "Assets", "Atlas", "Spaces", "Glued", "Resources", "AtlasKit")
PALETTE_PARTS = ("ATLAS", "Assets", "Atlas", "Rendering", "Look", "AtlasPalette.cs")


def piece_from_object(obj, depsgraph, slots: dict[str, int]) -> kitglb.Piece:
    """The evaluated mesh of obj as a piece in Unity's frame, one vertex per distinct triangle corner."""
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)
    try:
        return _piece_from_mesh(obj, mesh, slots)
    finally:
        evaluated.to_mesh_clear()


def _piece_from_mesh(obj, mesh, slots) -> kitglb.Piece:
    mesh.calc_loop_triangles()
    turn = obj.matrix_world.to_3x3()                       # rotation and scale; the location is dropped
    normal_turn = turn.inverted_safe().transposed()
    mirrored = turn.determinant() < 0.0

    materials = [slot.material.name if slot.material else None for slot in obj.material_slots]
    cells: dict[int, tuple[float, float]] = {}
    ao = mesh.color_attributes.get(AO_ATTRIBUTE)
    lines = mesh.uv_layers.get(LINES_UV)
    corner_normals = mesh.corner_normals

    piece = kitglb.Piece()
    seen: dict[tuple, int] = {}
    for triangle in mesh.loop_triangles:
        index = triangle.material_index
        if index not in cells:
            name = materials[index] if 0 <= index < len(materials) else None
            cells[index] = kitglb.palette_uv(kitglb.slot_for_material(name, slots, obj.name))
        corners = []
        for loop, vertex in zip(triangle.loops, triangle.vertices):
            position = kitglb.blender_to_unity(tuple(turn @ mesh.vertices[vertex].co))
            n = normal_turn @ corner_normals[loop].vector
            normal = kitglb.blender_to_unity(tuple(n.normalized()))
            occlusion = 1.0
            if ao is not None:
                occlusion = ao.data[vertex if ao.domain == "POINT" else loop].color[0]
            line = tuple(lines.data[loop].uv) if lines is not None else (0.0, 0.0)
            shade = kitglb.face_shade(normal) * occlusion
            key = (position, normal, cells[index], line, shade)
            if key not in seen:
                seen[key] = len(piece.positions)
                piece.positions.append(position)
                piece.normals.append(normal)
                piece.uv0.append(cells[index])
                piece.uv1.append(line)
                piece.shade.append(shade)
            corners.append(seen[key])
        a, b, c = kitglb.blender_triangle(*corners)
        piece.indices += [a, c, b] if mirrored else [a, b, c]
    return piece


def _gen_meta(repo_root):
    spec = importlib.util.spec_from_file_location("gen_meta", os.path.join(repo_root, "tools", "gen_meta.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _write_missing_metas(repo_root, paths):
    """The .meta files gen_meta.py would write for these files and folders, with the same GUIDs."""
    gen_meta = _gen_meta(repo_root)
    project = os.path.join(repo_root, "ATLAS")
    for path in paths:
        meta = path + ".meta"
        if os.path.exists(meta):
            continue
        rel = os.path.relpath(path, project).replace(os.sep, "/")
        body = gen_meta.FOLDER if os.path.isdir(path) else gen_meta.BODY.get(os.path.splitext(path)[1].lower(), "")
        with open(meta, "w", newline="\n") as f:
            f.write("fileFormatVersion: 2\nguid: %s\n%s" % (gen_meta.guid_for(rel), body))


def _write_if_changed(path, data: bytes) -> bool:
    if os.path.isfile(path):
        with open(path, "rb") as f:
            if f.read() == data:
                return False
    with open(path, "wb") as f:
        f.write(data)
    return True


def export_all(repo_root) -> dict[str, str]:
    """Exports every piece; returns piece -> what happened ("written", "unchanged", "skipped: ...", "failed: ...")."""
    import bpy

    repo_root = os.path.abspath(repo_root)
    slots = kitglb.palette_slots(os.path.join(repo_root, *PALETTE_PARTS))
    out_dir = os.path.join(repo_root, *OUT_PARTS)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    collection = bpy.data.collections.get(COLLECTION)
    results: dict[str, str] = {}
    files: dict[str, bytes] = {}
    for piece_name, object_name in PIECES.items():
        obj = collection.all_objects.get(object_name) if collection is not None else None
        if obj is None or obj.type != "MESH":
            where = f"collection {COLLECTION}" if collection is not None else f"the file (no collection {COLLECTION})"
            results[piece_name] = f"skipped: no mesh object {object_name} in {where}"
            continue
        try:
            piece = piece_from_object(obj, depsgraph, slots)
            kitglb.check_piece(piece_name, piece, kitglb.BUDGETS[piece_name])
            files[piece_name] = kitglb.write_glb(piece)
        except ValueError as error:
            results[piece_name] = f"failed: {error}"

    os.makedirs(out_dir, exist_ok=True)
    for piece_name, glb in files.items():
        changed = _write_if_changed(os.path.join(out_dir, piece_name + ".glb.bytes"), glb)
        results[piece_name] = ("written" if changed else "unchanged") + f" ({kitglb.triangle_count(glb)} triangles)"

    # The manifest covers every piece file in the folder, so a skipped or failed piece keeps its last good export.
    shipped = {}
    for piece_name in PIECES:
        path = os.path.join(out_dir, piece_name + ".glb.bytes")
        if os.path.isfile(path):
            with open(path, "rb") as f:
                shipped[piece_name] = f.read()
    if shipped:
        _write_if_changed(os.path.join(out_dir, "manifest.json"), kitglb.write_manifest(shipped))
    resources = os.path.dirname(out_dir)
    _write_missing_metas(repo_root, [resources, out_dir] + [os.path.join(out_dir, name) for name in sorted(os.listdir(out_dir))
                                                            if not name.endswith(".meta")])
    for piece_name in PIECES:
        print(f"kit: {piece_name}: {results[piece_name]}")
    return results


def main(argv) -> int:
    args = argv[argv.index("--") + 1:] if "--" in argv else []
    if len(args) != 1:
        print("usage: blender -b <ATLAS_Kit.blend> -P tools/kit/export_kit.py -- <repo_root>")
        return 2
    results = export_all(args[0])
    return 1 if any(r.startswith("failed") for r in results.values()) else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
