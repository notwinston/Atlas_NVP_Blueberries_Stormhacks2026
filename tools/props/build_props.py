#!/usr/bin/env python3
"""Converts the CC0 kit sources into the phone's prop library (standalone-phone plan, task T4, section 6.3).

Run it in Blender, headless, from the repository root:

    /Applications/Blender.app/Contents/MacOS/Blender -b -P tools/props/build_props.py -- build
    /Applications/Blender.app/Contents/MacOS/Blender -b -P tools/props/build_props.py -- contact

then fill the derived manifest fields and check the library with the venv tools (tools/props/README.md):

    tools/props/.venv/bin/python tools/props/profile_tool.py --write
    tools/props/.venv/bin/python tools/props/check.py

`build` reads the kit GLBs listed in PROPS from art/props/sources/ and writes, into
ATLAS/Assets/Atlas/Telemetry/Resources/AtlasProps, one `<id>.glb.bytes` per prop and a `manifest.json` that holds
everything but the derived fields (triangles, sha256, profile: profile_tool.py --write). It is deterministic.
`contact` renders every prop of the manifest, seen from its front, into art/props/previews/contact.png.

What `build` does to a source, in order:
  1. Imports the glTF and takes every mesh in world space. glTF is the frame the prop files are stored in
     (right-handed, +Y up, front +Z), so the Unity frame is (-X, Z, -Y) of Blender's import.
  2. Turns it by `turns` quarter turns about the up axis, so that the front looks along +Z.
  3. Scales it, per axis, so that its bounds equal `size` (metres, [w, h, d]), and puts the origin at the centre of
     the base.
  4. Maps each source material to a colour slot: materials closer than SLOT_MERGE (OKLab) share a slot, at most
     MAX_SLOTS slots remain, and each slot takes the nearest still-free object palette cell (64..127) to its
     colour. A face carries its slot's cell in uv0.
  5. Splits edges (longest-edge bisection, shared midpoints, so no cracks) until none is over MAX_EDGE, and
     further until the prop has MIN_TRIANGLES triangles, which the format asks for.
  6. Bakes the shade (AtlasShading.FaceShade, as the kit pieces do), runs kitglb.check_piece with the edge rule
     and writes the GLB through kitglb.write_glb.

Python standard library and bpy only. The tables below are the only place that names a source file.
"""
from __future__ import annotations

import hashlib
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.join(REPO, "tools", "kit"))
import kitglb  # noqa: E402

SOURCES = os.path.join(REPO, "art", "props", "sources")
OUT_DIR = os.path.join(REPO, "ATLAS", "Assets", "Atlas", "Telemetry", "Resources", "AtlasProps")
PREVIEW = os.path.join(REPO, "art", "props", "previews", "contact.png")

MAX_SLOTS = 4
SLOT_MERGE = 0.045        # OKLab distance under which two source materials share a slot
MAX_EDGE = 0.249          # metres, the prop edge rule
MIN_TRIANGLES, MAX_TRIANGLES = 300, 2500
LICENCE = "CC0-1.0"

KENNEY = "Kenney Furniture Kit 2.0"
KENNEY_DIR = "kenney_furniture/glb"

# id, category, tags, source folder/file, turns (quarter turns about +Y, counter-clockwise seen from above, to
# bring the model's front to +Z), size [w, h, d] in metres.
# Tags are RoomPlan attribute names as far as Apple documents them (chair type, table shape, storage kind);
# task T12 reconciles them with the strings the phone really reports (spike S1).
# The sizes keep Kenney's proportions within about +-25 % and move them to real furniture sizes.
PROPS = [
    # id, category, tags, file, turns, size
    ("bathtub_kenney_01", "bathtub", [], "bathtub", 0, (1.70, 0.58, 0.80)),

    ("bed_kenney_single", "bed", ["single"], "bedSingle", 0, (0.95, 0.55, 1.95)),
    ("bed_kenney_double", "bed", ["double"], "bedDouble", 0, (1.60, 0.60, 1.95)),

    ("chair_kenney_dining", "chair", ["dining"], "chair", 0, (0.44, 0.90, 0.46)),
    ("chair_kenney_swivel", "chair", ["swivel"], "chairDesk", 0, (0.60, 0.95, 0.58)),
    ("chair_kenney_stool", "chair", ["stool"], "stoolBar", 0, (0.38, 0.70, 0.38)),
    ("chair_kenney_armchair", "chair", ["arm"], "loungeChairRelax", 0, (0.80, 0.95, 1.05)),

    ("oven_kenney_range", "oven", [], "kitchenStoveElectric", 0, (0.62, 0.90, 0.62)),

    ("refrigerator_kenney_standard", "refrigerator", [], "kitchenFridge", 0, (0.60, 1.70, 0.65)),
    ("refrigerator_kenney_builtin", "refrigerator", [], "kitchenFridgeBuiltIn", 0, (0.60, 1.80, 0.65)),
    ("refrigerator_kenney_small", "refrigerator", [], "kitchenFridgeSmall", 0, (0.55, 0.85, 0.55)),

    ("sink_kenney_kitchen", "sink", [], "kitchenSink", 0, (0.70, 0.90, 0.62)),
    ("sink_kenney_bathroom", "sink", [], "bathroomSink", 0, (0.55, 0.85, 0.45)),
    ("sink_kenney_vanity", "sink", [], "bathroomSinkSquare", 0, (0.60, 0.85, 0.45)),

    ("couch_kenney_straight", "couch", ["straight"], "loungeSofa", 0, (2.00, 0.85, 0.90)),
    ("couch_kenney_corner", "couch", ["corner"], "loungeSofaCorner", 0, (2.00, 0.85, 2.00)),
    ("couch_kenney_chaise", "couch", ["chaise"], "loungeSofaLong", 0, (2.20, 0.85, 1.70)),
    ("couch_kenney_design", "couch", ["straight"], "loungeDesignSofa", 0, (2.10, 0.80, 0.85)),

    ("stairs_kenney_closed", "stairs", [], "stairs", 1, (1.00, 2.60, 3.40)),
    ("stairs_kenney_corner", "stairs", [], "stairsCorner", 1, (2.40, 2.60, 2.60)),

    ("storage_kenney_low_shelf", "storage", ["shelf"], "bookcaseOpenLow", 0, (0.80, 0.90, 0.35)),
    ("storage_kenney_wide_shelf", "storage", ["shelf"], "bookcaseClosedWide", 0, (1.00, 1.00, 0.32)),
    ("storage_kenney_bookcase", "storage", ["cabinet"], "bookcaseClosed", 0, (0.65, 1.45, 0.32)),
    ("storage_kenney_sideboard", "storage", ["dresser"], "cabinetTelevision", 0, (1.60, 0.62, 0.45)),
    ("storage_kenney_counter", "storage", ["cabinet"], "kitchenCabinet", 0, (0.62, 0.90, 0.62)),

    ("stove_kenney_gas", "stove", [], "kitchenStove", 0, (0.64, 0.90, 0.64)),
    ("stove_kenney_electric", "stove", [], "kitchenStoveElectric", 0, (0.62, 0.90, 0.62)),

    ("table_kenney_rect", "table", ["rectangular"], "table", 0, (1.60, 0.75, 0.90)),
    ("table_kenney_round", "table", ["circular", "oval"], "tableRound", 0, (1.20, 0.75, 1.30)),
    ("table_kenney_coffee", "table", ["rectangular"], "tableCoffee", 0, (1.10, 0.42, 0.65)),
    ("table_kenney_desk", "table", ["rectangular"], "desk", 0, (1.40, 0.75, 0.70)),

    ("screen_kenney_modern", "screen", [], "televisionModern", 0, (1.10, 0.70, 0.22)),
    ("screen_kenney_vintage", "screen", [], "televisionVintage", 0, (0.55, 0.45, 0.45)),
    ("screen_kenney_monitor", "screen", [], "computerScreen", 0, (0.55, 0.45, 0.20)),

    ("toilet_kenney_round", "toilet", [], "toilet", 0, (0.40, 0.75, 0.70)),
    ("toilet_kenney_square", "toilet", [], "toiletSquare", 0, (0.40, 0.75, 0.65)),

    ("washerdryer_kenney_washer", "washerDryer", [], "washer", 0, (0.60, 0.85, 0.62)),
    ("washerdryer_kenney_dryer", "washerDryer", [], "dryer", 0, (0.60, 0.85, 0.62)),
    ("washerdryer_kenney_stacked", "washerDryer", [], "washerDryerStacked", 0, (0.60, 1.75, 0.62)),
]

# Props whose source cannot reach MAX_EDGE within MAX_TRIANGLES as it is (hidden faces, thin slivers): id -> octree
# depth of the sharp remesh that replaces the source topology with an even, outer-shell-only one.
REMESH = {
    "bed_kenney_double": 4, "stairs_kenney_closed": 4, "stairs_kenney_corner": 4,
}


# ---- colour ------------------------------------------------------------------------------------------------

def srgb_to_linear(c: float) -> float:
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def oklab(rgb_linear):
    r, g, b = rgb_linear
    l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b
    m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b
    s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b
    l, m, s = (math.copysign(abs(x) ** (1.0 / 3.0), x) for x in (l, m, s))
    return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s)


def hsv_to_rgb(h: float, s: float, v: float):
    h = (h % 360.0) / 60.0
    i = int(math.floor(h)) % 6
    f = h - math.floor(h)
    p, q, t = v * (1 - s), v * (1 - s * f), v * (1 - s * (1 - f))
    return [(v, t, p), (q, v, p), (p, v, t), (p, q, v), (t, p, v), (v, p, q)][i]


def palette_srgb(cell: int):
    """AtlasPalette.TexelSrgb for the object cells 64..127 (rows 4 to 7), as 8-bit values over 255."""
    k = cell - 64
    column, step = k & 15, k >> 4
    value = (0.28, 0.42, 0.58, 0.74)[step]
    if column < 12:
        rgb = hsv_to_rgb(column * 30.0, 0.50, value)
    elif column == 12:
        rgb = hsv_to_rgb(30.0, 0.12, value)
    elif column == 13:
        rgb = hsv_to_rgb(210.0, 0.12, value)
    elif column == 14:
        rgb = hsv_to_rgb(28.0, 0.35, value)
    else:
        rgb = hsv_to_rgb(0.0, 0.0, value)
    return tuple(round(c * 255.0) / 255.0 for c in rgb)


def palette_linear(cell: int):
    return tuple(srgb_to_linear(c) for c in palette_srgb(cell))


PALETTE_LAB = {cell: oklab(palette_linear(cell)) for cell in range(64, 128)}


def distance(a, b) -> float:
    return math.sqrt(sum((a[i] - b[i]) ** 2 for i in range(3)))


def assign_slots(materials: dict):
    """materials: name -> (linear rgb, area). Returns (material name -> slot index, [(slot name, cell)])."""
    # Largest first; each material joins the nearest cluster within SLOT_MERGE, else opens one.
    order = sorted(materials, key=lambda n: (-materials[n][1], n))
    clusters = []   # [lab (area weighted), area, [names]]
    for name in order:
        lab = oklab(materials[name][0])
        area = materials[name][1]
        best = min(clusters, key=lambda c: distance(c[0], lab), default=None)
        if best is not None and distance(best[0], lab) < SLOT_MERGE:
            total = best[1] + area
            best[0] = tuple((best[0][i] * best[1] + lab[i] * area) / total for i in range(3))
            best[1] = total
            best[2].append(name)
        else:
            clusters.append([lab, area, [name]])
    while len(clusters) > MAX_SLOTS:        # merge the closest pair
        pairs = [(distance(clusters[i][0], clusters[j][0]), i, j)
                 for i in range(len(clusters)) for j in range(i + 1, len(clusters))]
        _, i, j = min(pairs)
        a, b = clusters[i], clusters[j]
        total = a[1] + b[1]
        a[0] = tuple((a[0][k] * a[1] + b[0][k] * b[1]) / total for k in range(3))
        a[1] = total
        a[2] += b[2]
        del clusters[j]
    clusters.sort(key=lambda c: (-c[1], c[2][0]))
    used, slots, mapping = set(), [], {}
    for index, (lab, _, names) in enumerate(clusters):
        cell = min((c for c in PALETTE_LAB if c not in used), key=lambda c: (distance(PALETTE_LAB[c], lab), c))
        used.add(cell)
        base = names[0]
        slots.append((base, cell))
        for n in names:
            mapping[n] = index
    # Distinct, non-empty slot names.
    seen = {}
    named = []
    for name, cell in slots:
        seen[name] = seen.get(name, 0) + 1
        named.append((name if seen[name] == 1 else f"{name}{seen[name]}", cell))
    return mapping, named


# ---- mesh --------------------------------------------------------------------------------------------------

def read_source(path: str):
    """Triangles of every mesh of a glTF, in the file frame (x_g, y_g, z_g) = (X, Z, -Y) of Blender's import.

    Returns (vertices [(pos, normal, material name)], triangles [(i, j, k)] with counter-clockwise fronts,
    materials {name: (linear rgb, area)})."""
    import bpy
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    vertices, triangles, materials = [], [], {}
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH":
            continue
        mesh = obj.data
        mesh.calc_loop_triangles()
        turn = obj.matrix_world.to_3x3()
        normal_turn = turn.inverted_safe().transposed()
        names = []
        for slot in obj.material_slots:
            m = slot.material
            colour = (0.8, 0.8, 0.8)
            if m is not None and m.node_tree is not None:
                bsdf = m.node_tree.nodes.get("Principled BSDF")
                if bsdf is not None:
                    colour = tuple(bsdf.inputs["Base Color"].default_value[:3])
            names.append((m.name if m is not None else "default", colour))
        seen = {}
        for tri in mesh.loop_triangles:
            name, colour = names[tri.material_index] if names else ("default", (0.8, 0.8, 0.8))
            corners = []
            for loop, v in zip(tri.loops, tri.vertices):
                p = obj.matrix_world @ mesh.vertices[v].co
                n = (normal_turn @ mesh.corner_normals[loop].vector).normalized()
                # Blender import frame back to the file frame: (X, Y, Z) -> (X, Z, -Y).
                pos = (float(p[0]), float(p[2]), float(-p[1]))
                nrm = (float(n[0]), float(n[2]), float(-n[1]))
                key = (tuple(round(c, 6) for c in pos), tuple(round(c, 4) for c in nrm), name)
                if key not in seen:
                    seen[key] = len(vertices)
                    vertices.append((pos, nrm, name))
                corners.append(seen[key])
            a, b, c = (vertices[i][0] for i in corners)
            ux, uy, uz = (b[i] - a[i] for i in range(3))
            vx, vy, vz = (c[i] - a[i] for i in range(3))
            area = 0.5 * math.sqrt((uy * vz - uz * vy) ** 2 + (uz * vx - ux * vz) ** 2 + (ux * vy - uy * vx) ** 2)
            if area < 1e-12:
                continue
            triangles.append(tuple(corners))
            prev = materials.get(name, (colour, 0.0))
            materials[name] = (tuple(srgb_or_linear(colour)), prev[1] + area)
    return vertices, triangles, materials


def srgb_or_linear(colour):
    """Blender's Principled Base Color default_value is linear already."""
    return colour


def to_unity(vertices, turns: int, size):
    """File frame -> Unity frame (x negated), quarter turns about +Y, scaled to `size`, origin at the base centre.

    Returns (positions, normals) lists in Unity's frame."""
    pos, nrm = [], []
    for p, n, _ in vertices:
        x, y, z = -p[0], p[1], p[2]
        nx, ny, nz = -n[0], n[1], n[2]
        for _ in range(turns % 4):               # (x, z) -> (-z, x): counter-clockwise seen from above (+Y down)
            x, z = -z, x
            nx, nz = -nz, nx
        pos.append([x, y, z])
        nrm.append([nx, ny, nz])
    lo = [min(p[i] for p in pos) for i in range(3)]
    hi = [max(p[i] for p in pos) for i in range(3)]
    scale = [size[i] / (hi[i] - lo[i]) for i in range(3)]
    centre = [0.5 * (lo[0] + hi[0]), lo[1], 0.5 * (lo[2] + hi[2])]
    out_p = [tuple((p[i] - centre[i]) * scale[i] for i in range(3)) for p in pos]
    out_n = []
    for n in nrm:                                 # normals go through the inverse transpose of the scale
        v = [n[i] / scale[i] for i in range(3)]
        length = math.sqrt(sum(c * c for c in v)) or 1.0
        out_n.append(tuple(c / length for c in v))
    return out_p, out_n, scale


def _sub(a, b):
    return tuple(a[i] - b[i] for i in range(3))


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _normalised(v):
    length = math.sqrt(sum(c * c for c in v))
    return tuple(c / length for c in v) if length > 1e-12 else (0.0, 1.0, 0.0)


def _boundary(positions, triangles):
    """Welded vertex ids, and the undirected welded edges used by exactly one triangle: edge -> (triangle, a, b)."""
    welded = kitglb._weld(positions, kitglb.WELD_TOLERANCE)
    uses = {}
    for t, tri in enumerate(triangles):
        for e in range(3):
            a, b = tri[e], tri[(e + 1) % 3]
            wa, wb = welded[a], welded[b]
            if wa != wb:
                uses.setdefault((wa, wb) if wa < wb else (wb, wa), []).append((t, a, b))
    return welded, {k: v[0] for k, v in uses.items() if len(v) == 1}


def close_mesh(positions, normals, cells, triangles):
    """Makes the triangle soup closed in the check's sense: every welded edge is used at least twice.

    The kit models have T-junctions and a few missing faces. A vertex that lies inside a boundary edge splits the
    triangle that owns the edge; a hole that remains is closed with a fan round the centroid of its edges. Both
    only add vertices and triangles, and keep the winding. Returns new lists."""
    positions, normals, cells = list(positions), list(normals), list(cells)
    triangles = [tuple(t) for t in triangles]
    tolerance = 2.0 * kitglb.WELD_TOLERANCE
    for _ in range(200):
        welded, boundary = _boundary(positions, triangles)
        if not boundary:
            return positions, normals, cells, triangles
        anchors = {}
        for w, p in zip(welded, positions):
            anchors.setdefault(w, p)
        splits = {}                                  # triangle index -> (a, b, position of the new vertex)
        for (wa, wb), (t, a, b) in boundary.items():
            if t in splits:
                continue
            pa = positions[a]
            edge = _sub(positions[b], pa)
            length2 = sum(c * c for c in edge)
            for w, q in anchors.items():
                if w == wa or w == wb:
                    continue
                u = sum(_sub(q, pa)[i] * edge[i] for i in range(3)) / length2
                if 1e-6 < u < 1.0 - 1e-6 and \
                        distance(q, tuple(pa[i] + u * edge[i] for i in range(3))) <= tolerance:
                    splits[t] = (a, b, q)
                    break
        if splits:
            out = []
            for t, tri in enumerate(triangles):
                if t not in splits:
                    out.append(tri)
                    continue
                a, b, q = splits[t]
                m = len(positions)
                positions.append(q)
                normals.append(_normalised(tuple(normals[a][i] + normals[b][i] for i in range(3))))
                cells.append(cells[a])
                i = tri.index(a)
                c = tri[(i + 2) % 3] if tri[(i + 1) % 3] == b else tri[(i + 1) % 3]
                if tri[(i + 1) % 3] == b:            # the triangle runs a -> b -> c
                    out += [(a, m, c), (m, b, c)]
                else:                                # it runs b -> a -> c
                    out += [(b, m, c), (m, a, c)]
            triangles = out
            continue
        # Holes. Components of boundary edges (joined at welded vertices) each get a fan round their centroid.
        parent = {}

        def find(x):
            while parent.setdefault(x, x) != x:
                parent[x] = parent[parent[x]]
                x = parent[x]
            return x

        for (wa, wb) in boundary:
            parent[find(wa)] = find(wb)
        groups = {}
        for key, item in boundary.items():
            groups.setdefault(find(key[0]), []).append(item)
        for items in groups.values():
            centre = tuple(sum(positions[a][i] + positions[b][i] for _, a, b in items) / (2 * len(items))
                           for i in range(3))
            for t, a, b in items:
                # The missing triangle runs the edge the other way: (b, a, centre), with its own vertices.
                pa, pb = positions[a], positions[b]
                n = _normalised(_cross(_sub(pa, pb), _sub(centre, pb)))
                base = len(positions)
                for q in (pb, pa, centre):
                    positions.append(q)
                    normals.append(n)
                    cells.append(cells[a])
                triangles.append((base, base + 1, base + 2))
    raise ValueError("could not close the mesh")


def remesh(positions, normals, cells, triangles, depth: int):
    """Rebuilds the shape as one closed, evenly tessellated shell with Blender's Remesh modifier (sharp mode).

    Used for the models that cannot reach MAX_EDGE within MAX_TRIANGLES as they are (beds, stairs ...): the
    shell has the outer surface only, at the octree's cell size, with the sharp edges kept. Each new face takes
    the palette cell of the nearest face of the original. Returns new lists with flat normals."""
    import bpy
    import bmesh
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree

    # The numbers' cross product of (a, b, c) points out of the shape, which is what Blender calls a front.
    source = [tuple(t) for t in triangles]
    tree = BVHTree.FromPolygons([Vector(p) for p in positions], source)
    mesh = bpy.data.meshes.new("remesh_in")
    mesh.from_pydata([tuple(p) for p in positions], [], source)
    obj = bpy.data.objects.new("remesh_in", mesh)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    modifier = obj.modifiers.new("remesh", "REMESH")
    modifier.mode = "SHARP"
    modifier.octree_depth = depth
    modifier.scale = 0.99
    modifier.sharpness = 1.0
    modifier.use_remove_disconnected = False
    modifier.use_smooth_shade = False
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    out = evaluated.to_mesh()
    try:
        bm = bmesh.new()
        bm.from_mesh(out)
        bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="SHORT_EDGE")
        new_p, new_n, new_c, new_t = [], [], [], []
        for face in bm.faces:
            corners = [v.co.copy() for v in face.verts]
            # The face keeps its order: its cross product points out, as the soup's triangles do.
            n = face.normal if face.normal.length > 0.5 else Vector((0.0, 1.0, 0.0))   # a collapsed face
            centre = (corners[0] + corners[1] + corners[2]) / 3.0
            hit = tree.find_nearest(centre)
            cell = cells[triangles[hit[2]][0]] if hit[2] is not None else cells[0]
            base = len(new_p)
            for q in corners:
                new_p.append((q.x, q.y, q.z))
                new_n.append((n.x, n.y, n.z))
                new_c.append(cell)
            new_t.append((base, base + 1, base + 2))
        bm.free()
    finally:
        evaluated.to_mesh_clear()
    bpy.data.objects.remove(obj)
    return new_p, new_n, new_c, new_t


def refit(positions, size):
    """Scales a mesh per axis to size again and puts the origin at the centre of its base (after a remesh)."""
    lo = [min(p[i] for p in positions) for i in range(3)]
    hi = [max(p[i] for p in positions) for i in range(3)]
    scale = [size[i] / (hi[i] - lo[i]) for i in range(3)]
    centre = [0.5 * (lo[0] + hi[0]), lo[1], 0.5 * (lo[2] + hi[2])]
    return [tuple((p[i] - centre[i]) * scale[i] for i in range(3)) for p in positions]


def _area2(positions, tri):
    """The squared length of the cross product of a triangle: four times its squared area."""
    return sum(c * c for c in _cross(_sub(positions[tri[1]], positions[tri[0]]),
                                     _sub(positions[tri[2]], positions[tri[0]])))


def split_edges(positions, normals, cells, triangles, max_edge: float):
    """Longest-edge bisection with shared midpoints until no edge is over max_edge. Returns new lists."""
    positions, normals, cells = list(positions), list(normals), list(cells)
    midpoints = {}

    def midpoint(a: int, b: int) -> int:
        key = (a, b) if a < b else (b, a)
        found = midpoints.get(key)
        if found is not None:
            return found
        lo, hi = key                              # fixed order: the same value whichever side asks first
        p = tuple(0.5 * (positions[lo][i] + positions[hi][i]) for i in range(3))
        n = [normals[lo][i] + normals[hi][i] for i in range(3)]
        length = math.sqrt(sum(c * c for c in n))
        n = tuple(c / length for c in n) if length > 1e-9 else normals[lo]
        positions.append(p)
        normals.append(n)
        cells.append(cells[lo])
        midpoints[key] = len(positions) - 1
        return midpoints[key]

    def length(a: int, b: int) -> float:
        return distance(positions[a], positions[b])

    out = []
    stack = [tuple(t) for t in reversed(triangles)]
    while stack:
        a, b, c = stack.pop()
        edges = ((length(a, b), 0), (length(b, c), 1), (length(c, a), 2))
        longest, which = max(edges, key=lambda e: (e[0], -e[1]))
        if longest <= max_edge:
            out.append((a, b, c))
            continue
        # Rotate so that the long edge is (a, b); split it, keeping the winding.
        if which == 1:
            a, b, c = b, c, a
        elif which == 2:
            a, b, c = c, a, b
        m = midpoint(a, b)
        stack.append((m, b, c))
        stack.append((a, m, c))
    return positions, normals, cells, out


def build_piece(positions, normals, cells, triangles):
    piece = kitglb.Piece()
    for p, n, cell in zip(positions, normals, cells):
        piece.positions.append(tuple(p))
        piece.normals.append(tuple(n))
        piece.uv0.append(kitglb.palette_uv(cell))
        piece.uv1.append((0.0, 0.0))
        piece.shade.append(kitglb.face_shade(n))
    for a, b, c in triangles:
        piece.indices += [a, b, c]
    return piece


def drop_unused(positions, normals, cells, triangles):
    used = sorted({i for t in triangles for i in t})
    remap = {old: new for new, old in enumerate(used)}
    return ([positions[i] for i in used], [normals[i] for i in used], [cells[i] for i in used],
            [tuple(remap[i] for i in t) for t in triangles])


def convert(prop) -> tuple:
    pid, category, tags, name, turns, size = prop
    path = os.path.join(SOURCES, KENNEY_DIR, name + ".glb")
    vertices, triangles, materials = read_source(path)
    if not triangles:
        raise ValueError(f"{pid}: {path} holds no triangles")
    mapping, slots = assign_slots(materials)
    cells = [slots[mapping[v[2]]][1] for v in vertices]
    positions, normals, scale = to_unity(vertices, turns, size)
    # The file frame is counter-clockwise with x negated by to_unity (a mirror): the winding turns, as in
    # kitglb.blender_triangle, so Unity's fronts come out clockwise.
    triangles = [(a, c, b) for a, b, c in triangles]
    # Collinear slivers (zero area) have no front to agree with their normals; close_mesh restores their edges.
    triangles = [t for t in triangles
                 if sum(c * c for c in _cross(_sub(positions[t[1]], positions[t[0]]),
                                              _sub(positions[t[2]], positions[t[0]]))) > 1e-9]
    positions, normals, cells, triangles = close_mesh(positions, normals, cells, triangles)
    if pid in REMESH:
        positions, normals, cells, triangles = remesh(positions, normals, cells, triangles, REMESH[pid])
        positions, normals, cells, triangles = close_mesh(positions, normals, cells, triangles)
        positions = refit(positions, size)

    edge = MAX_EDGE
    while True:
        p, n, c, t = split_edges(positions, normals, cells, triangles, edge)
        for _ in range(8):            # collinear slivers the split made: drop them, close their edges, split again
            sound = [tri for tri in t if _area2(p, tri) > 1e-9]
            if len(sound) == len(t):
                break
            p, n, c, t = close_mesh(p, n, c, sound)
            p, n, c, t = split_edges(p, n, c, t, edge)
        if len(t) >= MIN_TRIANGLES or edge < 0.02:
            break
        edge *= 0.85
    p, n, c, t = drop_unused(p, n, c, t)
    piece = build_piece(p, n, c, t)
    kitglb.check_piece(pid, piece, MAX_TRIANGLES, check_edges=True)
    # Every slot cell must mark a face, and nothing else may.
    marked = {c[i] for tri in t for i in tri}
    slots = [(s, cell) for s, cell in slots if cell in marked]
    anisotropy = max(scale) / min(scale)
    return piece, slots, anisotropy, edge, len(t)


def build() -> int:
    os.makedirs(OUT_DIR, exist_ok=True)
    entries, failed = [], []
    for prop in PROPS:
        pid, category, tags, name, turns, size = prop
        try:
            piece, slots, anisotropy, edge, count = convert(prop)
        except ValueError as error:
            failed.append(str(error))
            print(f"FAILED {error}")
            continue
        data = kitglb.write_glb(piece)
        with open(os.path.join(OUT_DIR, pid + ".glb.bytes"), "wb") as f:
            f.write(data)
        entries.append({
            "id": pid, "category": category, "tags": list(tags),
            "size": [float(s) for s in size], "triangles": count,
            "sha256": hashlib.sha256(data).hexdigest(),
            "slots": [{"name": s, "cell": cell} for s, cell in slots],
            "profile": "",
            "source": f"{KENNEY} / {name}.glb", "licence": LICENCE})
        print(f"ok {pid:34s} {count:5d} tris  edge {edge:.3f}  stretch {anisotropy:.2f}  slots {slots}")
    # Files of earlier runs that are no longer in the table go.
    keep = {p[0] + ".glb.bytes" for p in PROPS}
    for fname in sorted(os.listdir(OUT_DIR)):
        if fname.endswith(".glb.bytes") and fname not in keep:
            os.remove(os.path.join(OUT_DIR, fname))
    manifest = {"schema": "atlas.props/1", "props": entries}
    with open(os.path.join(OUT_DIR, "manifest.json"), "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(manifest, indent=2, ensure_ascii=True) + "\n")
    print(f"{len(entries)} props written, {len(failed)} failed")
    return 1 if failed else 0


# ---- contact sheet -----------------------------------------------------------------------------------------

def read_prop_file(path: str):
    """positions, normals, cells (palette cell per vertex), shade, faces of a prop file, in the file frame."""
    import struct
    with open(path, "rb") as f:
        glb = f.read()
    doc, binary = kitglb.read_glb(glb)
    primitive = doc["meshes"][0]["primitives"][0]
    attrs = primitive["attributes"]

    def accessor(index, fmt, n):
        acc = doc["accessors"][index]
        view = doc["bufferViews"][acc["bufferView"]]
        start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
        size = struct.calcsize(fmt) * n
        return [struct.unpack_from(fmt * n if n > 1 else fmt, binary, start + i * size) for i in range(acc["count"])]

    pos = accessor(attrs["POSITION"], "f", 3)
    nrm = accessor(attrs["NORMAL"], "f", 3)
    uv = accessor(attrs["TEXCOORD_0"], "f", 2)
    col = accessor(attrs["COLOR_0"], "B", 4)
    idx = [t[0] for t in accessor(primitive["indices"], "H", 1)]
    cells = []
    for u, v in uv:
        v = 1.0 - v
        cells.append(int(math.floor(v * 16)) * 16 + int(math.floor(u * 16)))
    faces = [tuple(idx[i:i + 3]) for i in range(0, len(idx), 3)]
    return pos, nrm, cells, [c[0] / 255.0 for c in col], faces


def contact() -> int:
    import bpy
    import mathutils
    import numpy as np

    with open(os.path.join(OUT_DIR, "manifest.json"), encoding="utf-8") as f:
        props = json.load(f)["props"]
    tile = 384
    columns = 7
    rows = (len(props) + columns - 1) // columns
    sheet = np.ones((rows * tile, columns * tile, 4), dtype=np.float32)
    sheet[..., :3] = 0.93

    for number, prop in enumerate(props):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        scene = bpy.context.scene
        scene.render.engine = "BLENDER_WORKBENCH"
        scene.render.resolution_x = scene.render.resolution_y = tile
        scene.render.film_transparent = False
        shading = scene.display.shading
        shading.light = "FLAT"
        shading.color_type = "VERTEX"
        scene.view_settings.view_transform = "Standard"
        scene.world = bpy.data.worlds.new("w")
        scene.world.color = (0.93, 0.93, 0.93)

        pos, nrm, cells, shade, faces = read_prop_file(os.path.join(OUT_DIR, prop["id"] + ".glb.bytes"))
        # File frame (x, y, z) -> Blender (x, -z, y): a proper rotation, windings keep.
        verts = [(p[0], -p[2], p[1]) for p in pos]
        mesh = bpy.data.meshes.new(prop["id"])
        mesh.from_pydata(verts, [], faces)
        attribute = mesh.color_attributes.new("c", "FLOAT_COLOR", "POINT")
        for i, (cell, s) in enumerate(zip(cells, shade)):
            r, g, b = palette_linear(cell) if 64 <= cell < 128 else (0.5, 0.5, 0.5)
            attribute.data[i].color = (r * s, g * s, b * s, 1.0)
        obj = bpy.data.objects.new(prop["id"], mesh)
        scene.collection.objects.link(obj)

        w, h, d = prop["size"]
        # A one metre ruler beside the prop, so scale errors show.
        ruler_mesh = bpy.data.meshes.new("ruler")
        x0 = w / 2 + 0.12
        corners = [(x0 + a, -0.03 + b, c) for c in (0.0, 1.0) for a, b in ((0, 0), (0.05, 0), (0.05, 0.06), (0, 0.06))]
        quads = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
        ruler_mesh.from_pydata(corners, [], quads)
        ruler_colour = ruler_mesh.color_attributes.new("c", "FLOAT_COLOR", "POINT")
        for i in range(len(corners)):
            ruler_colour.data[i].color = (0.9, 0.1, 0.1, 1.0)
        scene.collection.objects.link(bpy.data.objects.new("ruler", ruler_mesh))

        extent = max(w + 0.4, h, d) * 1.25
        cam_data = bpy.data.cameras.new("cam")
        cam_data.type = "ORTHO"
        cam_data.ortho_scale = extent * 1.9
        cam = bpy.data.objects.new("cam", cam_data)
        scene.collection.objects.link(cam)
        target = mathutils.Vector((0.05, 0.0, h * 0.5))
        # From the front (-Y in Blender is the file's +Z), a quarter turn to the left, above.
        direction = mathutils.Vector((-0.55, -0.80, 0.45)).normalized()
        cam.location = target + direction * (extent * 4)
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.camera = cam

        text_data = bpy.data.curves.new("t", "FONT")
        text_data.body = prop["id"]
        text_data.size = extent * 0.075
        text = bpy.data.objects.new("t", text_data)
        scene.collection.objects.link(text)
        text_mat = bpy.data.materials.new("tm")
        text_mat.diffuse_color = (0.0, 0.0, 0.0, 1.0)
        text_data.materials.append(text_mat)
        shading.color_type = "VERTEX"
        text.parent = cam
        text.location = (-extent * 0.93, -extent * 0.93, -extent * 3.9)
        scene.render.filepath = os.path.join(os.environ.get("TMPDIR", "/tmp"), "atlas_contact_tile.png")
        bpy.ops.render.render(write_still=True)
        image = bpy.data.images.load(scene.render.filepath)
        pixels = np.array(image.pixels[:], dtype=np.float32).reshape(tile, tile, 4)
        row, column = divmod(number, columns)
        top = (rows - 1 - row) * tile
        sheet[top:top + tile, column * tile:(column + 1) * tile] = pixels
        bpy.data.images.remove(image)

    out = bpy.data.images.new("contact", columns * tile, rows * tile, alpha=False)
    out.pixels = sheet.reshape(-1).tolist()
    os.makedirs(os.path.dirname(PREVIEW), exist_ok=True)
    out.filepath_raw = PREVIEW
    out.file_format = "PNG"
    out.save()
    print("contact sheet", PREVIEW, f"{len(props)} props")
    return 0


def main(argv) -> int:
    args = argv[argv.index("--") + 1:] if "--" in argv else []
    if args == ["build"]:
        return build()
    if args == ["contact"]:
        return contact()
    print("usage: blender -b -P tools/props/build_props.py -- build|contact")
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
