"""Builds the rubber duck, kit piece `duck` (object Hero_Duck in collection ATLAS_Hero), from the source model.

Run headless, from the repo root; the argument after `--` is the repo root (the blend is opened, changed and saved):

    blender -b art/blender/ATLAS_Kit.blend -P art/blender/duck_build.py -- <repo_root>

then export the kit as usual (tools/kit/export_kit.py). The script rebuilds Hero_Duck from scratch each time:
hand edits to it are lost unless the object is renamed first.

Source: art/source/free_rubber_duck_3d_model.glb, "Free Rubber Duck 3d Model" by Ryan King Art, CC BY 4.0
(https://sketchfab.com/3d-models/free-rubber-duck-3d-model-c5739cd7a94547dcb091bdd527536977); see art/blender/README.md.

What it does to the source: imports it, bakes the Sketchfab parent transforms and joins its four meshes into one,
maps the four materials Rubber_Duck_<Colour> to pal_Duck<Colour> (cells DuckYellow, DuckOrange, DuckWhite, DuckBlack
of AtlasPalette), merges vertices closer than 1e-5 m, collapses the mesh to at most MAX_TRIANGLES triangles, scales it
to HEIGHT metres tall (Blender Z, which the import leaves up), puts the origin at the bottom centre of its bounding
box and links it into ATLAS_Hero. The beak points wherever the source's does: the maze draws the duck turned to face
the walker, so the export keeps the orientation as it is. Conventions read by tools/kit/export_kit.py: see
art/blender/hero_build.py.
"""
import os
import sys

import bpy

HERO = "ATLAS_Hero"
NAME = "Hero_Duck"
SOURCE = ("art", "source", "free_rubber_duck_3d_model.glb")
HEIGHT = 0.30            # metres, Blender Z
MAX_TRIANGLES = 2400
MERGE_DISTANCE = 1e-5
SIDE = (1.6, 0.0, 0.0)   # where the object stands in the blend, beside the other heroes; the export drops the location

# Source material -> palette slot of AtlasPalette; sRGB colour as in AtlasPalette (DuckYellow .. DuckBlack).
SLOTS = {
    "Rubber_Duck_Yellow": ("DuckYellow", 0xFFC21A),
    "Rubber_Duck_Orange": ("DuckOrange", 0xFF4A12),
    "Rubber_Duck_White": ("DuckWhite", 0xE8E8E8),
    "Rubber_Duck_Black": ("DuckBlack", 0x1A1A1A),
}


def _linear(c8):
    c = c8 / 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def _palette_material(slot, rgb):
    """pal_<Slot>, created when missing: an emission of the cell colour, like the other pal_ materials."""
    name = "pal_" + slot
    material = bpy.data.materials.get(name)
    if material is not None:
        return material
    r, g, b = (_linear((rgb >> s) & 255) for s in (16, 8, 0))
    material = bpy.data.materials.new(name)
    material.diffuse_color = (r, g, b, 1.0)
    material.use_nodes = True
    tree = material.node_tree
    tree.nodes.clear()
    emission = tree.nodes.new("ShaderNodeEmission")
    emission.inputs["Color"].default_value = (r, g, b, 1.0)
    out = tree.nodes.new("ShaderNodeOutputMaterial")
    tree.links.new(emission.outputs["Emission"], out.inputs["Surface"])
    return material


def _remove_old():
    old = bpy.data.objects.get(NAME)
    if old is None:
        return
    mesh = old.data
    bpy.data.objects.remove(old, do_unlink=True)
    if mesh is not None and mesh.users == 0:
        bpy.data.meshes.remove(mesh)


def _select_only(objects, active):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active


def _triangles(obj):
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def _import_and_join(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    imported = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in imported if o.type == 'MESH']
    if len(meshes) != 4:
        raise RuntimeError(f"expected the source's four meshes, found {len(meshes)}: {[o.name for o in meshes]}")

    # The Sketchfab parent chain carries the Y-up to Z-up turns: bake it into the meshes, then drop the empties.
    _select_only(meshes, meshes[0])
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in imported:
        if o.type != 'MESH':
            bpy.data.objects.remove(o, do_unlink=True)

    # Materials first, so that the join keeps one slot per colour.
    for o in meshes:
        for slot in o.material_slots:
            source = slot.material.name.split(".")[0]
            if source not in SLOTS:
                raise RuntimeError(f"{o.name}: material '{slot.material.name}' is not one of {sorted(SLOTS)}")
            name, rgb = SLOTS[source]
            slot.material = _palette_material(name, rgb)
    _select_only(meshes, meshes[0])
    bpy.ops.object.join()
    duck = bpy.context.view_layer.objects.active
    duck.name = NAME
    duck.data.name = NAME
    return duck


def _merge_and_decimate(duck):
    _select_only([duck], duck)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=MERGE_DISTANCE)
    bpy.ops.mesh.quads_convert_to_tris(quad_method='BEAUTY', ngon_method='BEAUTY')
    bpy.ops.object.mode_set(mode='OBJECT')
    start = _triangles(duck)

    ratio = min(1.0, MAX_TRIANGLES / start)
    while True:
        modifier = duck.modifiers.new("Decimate", 'DECIMATE')
        modifier.decimate_type = 'COLLAPSE'
        modifier.ratio = ratio
        modifier.use_collapse_triangulate = True
        _select_only([duck], duck)
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        count = _triangles(duck)
        if count <= MAX_TRIANGLES:
            return start, count
        # The collapse landed a little over: take a bit more off the (already reduced) mesh.
        ratio = max(0.5, 0.97 * MAX_TRIANGLES / count)


def _place(duck):
    """Height HEIGHT, the box's bottom centre at the origin, at SIDE; modifiers and scale applied."""
    _select_only([duck], duck)
    bounds = [v.co.copy() for v in duck.data.vertices]
    top = max(c.z for c in bounds) - min(c.z for c in bounds)
    duck.scale = (HEIGHT / top,) * 3
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    xs, ys, zs = zip(*[tuple(v.co) for v in duck.data.vertices])
    shift = (-(min(xs) + max(xs)) / 2.0, -(min(ys) + max(ys)) / 2.0, -min(zs))
    for v in duck.data.vertices:
        v.co.x += shift[0]
        v.co.y += shift[1]
        v.co.z += shift[2]
    duck.location = SIDE


def _finish_mesh(duck):
    mesh = duck.data
    mesh.shade_smooth()
    # The export reads the UV map `lines` (no lines: a constant between integers, as the other heroes) and the
    # colour attribute `ao` (none means 1): the duck has no baked occlusion.
    for layer in list(mesh.uv_layers):
        mesh.uv_layers.remove(layer)
    lines = mesh.uv_layers.new(name="lines")
    for loop in mesh.loops:
        lines.data[loop.index].uv = (0.5, 0.5)
    for name in list(mesh.color_attributes.keys()):
        mesh.color_attributes.remove(mesh.color_attributes[name])
    mesh.update()


def _link(duck):
    collection = bpy.data.collections.get(HERO)
    if collection is None:
        collection = bpy.data.collections.new(HERO)
        bpy.context.scene.collection.children.link(collection)
    for c in list(duck.users_collection):
        c.objects.unlink(duck)
    collection.objects.link(duck)


def _purge_source_leftovers():
    for material in list(bpy.data.materials):
        if material.name.split(".")[0] in SLOTS and material.users == 0:
            bpy.data.materials.remove(material)
    for image in list(bpy.data.images):
        if image.users == 0:
            bpy.data.images.remove(image)
    for mesh in list(bpy.data.meshes):
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)


def build(root):
    path = os.path.join(root, *SOURCE)
    if not os.path.isfile(path):
        raise RuntimeError(f"the source model is missing: {path}")
    _remove_old()
    duck = _import_and_join(path)
    start, count = _merge_and_decimate(duck)
    _place(duck)
    _finish_mesh(duck)
    _link(duck)
    _purge_source_leftovers()
    bpy.ops.wm.save_mainfile()
    d = duck.dimensions
    print(f"duck: {start} triangles after merging, {count} after decimation (limit {MAX_TRIANGLES}); "
          f"dimensions {d.x:.4f} x {d.y:.4f} x {d.z:.4f} m (Blender x, y, z); "
          f"slots {[s.material.name for s in duck.material_slots]}")
    return duck


if __name__ == "__main__" or "--" in sys.argv:
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        raise SystemExit("usage: blender -b art/blender/ATLAS_Kit.blend -P art/blender/duck_build.py -- <repo_root>")
    build(args[0])
