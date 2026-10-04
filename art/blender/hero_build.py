"""Builds the hero pieces of ATLAS_Kit.blend (collection ATLAS_Hero) and their preview context (Hero_Preview).

Run inside Blender: exec(open(r"<this file>").read()); then build_all(). Each piece is rebuilt from scratch, so
edit the parameters below and run again. Hand edits to a piece are lost on a rebuild: rename the object first
(and back to Hero_<Name> when it is done) if you want to keep them.

Conventions read by tools/kit/export_kit.py (docs/superpowers/specs/2026-10-03-kit-hero-assets-design.md):
  Blender +X = Unity right, +Y = Unity forward, +Z = up; the object origin is the piece origin.
  Materials pal_<Slot> pick the palette cell; colour attribute 'ao' is the baked shade (0..1);
  UV map 'lines' carries the line coordinates (a line wherever it crosses an integer).
"""
import math

import bmesh
import bpy
from mathutils import Vector

HERO, PREVIEW = "ATLAS_Hero", "Hero_Preview"

# Glued cell of the preview, Unity's LandmarkBase for a 1.5 m cell: (-0.375, -0.375) from the cell centre.
CELL = 1.5
LANDMARK_BASE = (-0.25 * CELL, -0.25 * CELL)

LANDMARK = dict(
    pole_radius=0.025, pole_height=1.70, pole_sides=8,
    plinth_radius=0.14, plinth_height=0.05, plinth_sides=12,
    helix_start=0.22, helix_end=1.42, helix_turns=1.75, helix_segments_per_turn=24,
    helix_inner=0.035, helix_outer=0.17, helix_thickness=0.028,
    lines_per_turn=4,                       # cross-lines along the ribbon
    flag_length=0.24, flag_height=0.14, flag_notch=0.07, flag_thickness=0.012,
)


# ---- helpers ------------------------------------------------------------------------------------------------

def _collection(name):
    scene = bpy.context.scene
    c = bpy.data.collections.get(name) or bpy.data.collections.new(name)
    if c.name not in scene.collection.children:
        scene.collection.children.link(c)
    return c


def _replace_object(name, collection):
    old = bpy.data.objects.get(name)
    if old is not None:
        mesh = old.data
        bpy.data.objects.remove(old, do_unlink=True)
        if mesh is not None and mesh.users == 0:
            bpy.data.meshes.remove(mesh)
    mesh = bpy.data.meshes.new(name)
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    return obj


class Builder:
    """Collects faces with a material, per-corner line coordinates and per-corner ao, then writes a mesh."""

    def __init__(self):
        self.bm = bmesh.new()
        self.lines = self.bm.loops.layers.uv.new("lines")
        self.ao = self.bm.loops.layers.float_color.new("ao")
        self.materials = []

    def material(self, slot):
        name = "pal_" + slot
        if name not in self.materials:
            self.materials.append(name)
        return self.materials.index(name)

    def face(self, points, slot, lines=None, ao=None):
        verts = [self.bm.verts.new(p) for p in points]
        f = self.bm.faces.new(verts)
        f.material_index = self.material(slot)
        for k, loop in enumerate(f.loops):
            # No lines: a constant coordinate between integers (the shader draws nothing where it does not change).
            loop[self.lines].uv = lines[k] if lines else (0.5, 0.5)
            a = ao[k] if ao else 1.0
            loop[self.ao] = (a, a, a, 1.0)
        return f

    def write(self, obj):
        for name in self.materials:
            obj.data.materials.append(bpy.data.materials[name])
        # Faces are created as separate islands; weld coincident corners within a part, then turn every face
        # outwards, so a mistake in a vertex order above never shows as an inside-out face.
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=1e-6)
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        self.bm.normal_update()
        self.bm.to_mesh(obj.data)
        self.bm.free()
        for poly in obj.data.polygons:
            poly.use_smooth = False


def _ground_ao(z, full=0.6, low=0.55):
    """Darker towards the floor: the look's baked contact shade."""
    return low + (1.0 - low) * min(1.0, max(0.0, z / full))


def _prism(b, centre, radius, z0, z1, sides, slot, ao_top=1.0, line_rows=1.0):
    """A closed upright prism; each side face is outlined, the cap carries no lines."""
    cx, cy = centre
    ring = [(cx + radius * math.cos(2 * math.pi * i / sides), cy + radius * math.sin(2 * math.pi * i / sides))
            for i in range(sides)]
    for i in range(sides):
        (x0, y0), (x1, y1) = ring[i], ring[(i + 1) % sides]
        b.face([(x0, y0, z0), (x1, y1, z0), (x1, y1, z1), (x0, y0, z1)], slot,
               lines=[(0, 0), (1, 0), (1, line_rows), (0, line_rows)],
               ao=[_ground_ao(z0), _ground_ao(z0), _ground_ao(z1) * ao_top, _ground_ao(z1) * ao_top])
    b.face([(x, y, z1) for x, y in ring], slot, ao=[ao_top] * sides)
    b.face([(x, y, z0) for x, y in reversed(ring)], slot, ao=[_ground_ao(z0)] * sides)


# ---- pieces -------------------------------------------------------------------------------------------------

def build_landmark(p=LANDMARK):
    """Corkscrew beacon: plinth, pole, a helical ribbon climbing counter-clockwise seen from above, a pennant."""
    obj = _replace_object("Hero_Landmark", _collection(HERO))
    b = Builder()
    _prism(b, (0, 0), p["plinth_radius"], 0.0, p["plinth_height"], p["plinth_sides"], "Post")
    _prism(b, (0, 0), p["pole_radius"], p["plinth_height"], p["pole_height"], p["pole_sides"], "Post",
           line_rows=4.0)

    # The ribbon: a quad tube with a rectangular section, swept along the helix.
    turns, n_turn = p["helix_turns"], p["helix_segments_per_turn"]
    n = int(round(turns * n_turn))
    ri, ro, t = p["helix_inner"], p["helix_outer"], p["helix_thickness"]
    z0, z1 = p["helix_start"], p["helix_end"]
    per_line = n_turn / p["lines_per_turn"]
    def section(k):
        a = 2 * math.pi * turns * k / n
        z = z0 + (z1 - z0) * k / n
        c, s = math.cos(a), math.sin(a)
        return [Vector((ri * c, ri * s, z)), Vector((ro * c, ro * s, z)),
                Vector((ro * c, ro * s, z + t)), Vector((ri * c, ri * s, z + t))]
    for k in range(n):
        s0, s1 = section(k), section(k + 1)
        u0, u1 = k / per_line, (k + 1) / per_line
        ao0 = _ground_ao(s0[0].z, 1.0, 0.7)
        ao1 = _ground_ao(s1[0].z, 1.0, 0.7)
        # bottom (0-1), outer (1-2), top (2-3), inner (3-0); v runs 0..1 across each face so its edges are outlined
        for e, (i, j) in enumerate([(0, 1), (1, 2), (2, 3), (3, 0)]):
            shade = 0.75 if e == 0 else (0.85 if e == 3 else 1.0)
            b.face([s0[i], s1[i], s1[j], s0[j]], "Landmark",
                   lines=[(u0, 0), (u1, 0), (u1, 1), (u0, 1)],
                   ao=[ao0 * shade, ao1 * shade, ao1 * shade, ao0 * shade])
    for s, flip in ((section(0), True), (section(n), False)):
        pts = list(reversed(s)) if flip else s
        b.face(pts, "Landmark", lines=[(0, 0), (1, 0), (1, 1), (0, 1)])

    # The pennant: a swallow-tail flag on the pole's top, pointing the way the ribbon turns at its end.
    a_end = 2 * math.pi * turns
    d = Vector((-math.sin(a_end), math.cos(a_end), 0.0))     # tangent of the helix (counter-clockwise)
    zt = p["pole_height"] - 0.02
    L, H, notch, th = p["flag_length"], p["flag_height"], p["flag_notch"], p["flag_thickness"]
    side = Vector((d.y, -d.x, 0.0)) * (0.5 * th)
    base = Vector((0.0, 0.0, 0.0)) + d * p["pole_radius"]
    outline = [base + Vector((0, 0, zt)), base + d * L + Vector((0, 0, zt)),
               base + d * (L - notch) + Vector((0, 0, zt - 0.5 * H)),
               base + d * L + Vector((0, 0, zt - H)), base + Vector((0, 0, zt - H))]
    front = [v + side for v in outline]
    back = [v - side for v in outline]
    b.face(front, "Flag")
    b.face(list(reversed(back)), "Flag")
    for i in range(len(outline)):
        j = (i + 1) % len(outline)
        b.face([back[i], back[j], front[j], front[i]], "Flag")
    b.write(obj)
    obj.location = (0.0, 0.0, 0.0)
    return obj


AVATAR = dict(
    head_radius=0.105, head_back=0.08, head_segments=10, head_rings=6,
    visor_width_deg=110.0, visor_height=0.045, visor_drop=0.02, visor_depth=0.012, visor_segments=6,
    neck_radius=0.04, neck_top=-0.09, neck_bottom=-0.25,
    # torso rings: (z, half width x, half depth y), top to bottom; the body hangs from the head
    torso=[(-0.25, 0.17, 0.10), (-0.48, 0.16, 0.10), (-0.80, 0.13, 0.085), (-1.02, 0.14, 0.09)],
    torso_sides=8,
    shoulder_x=0.20, shoulder_z=-0.31, shoulder_radius=0.075,
    arm_radius=0.042, elbow_z=-0.62, wrist_z=-0.90, arm_out=0.20, arm_sides=6,
)


def _ring(cx, cy, z, rx, ry, sides, phase=0.0):
    return [Vector((cx + rx * math.cos(phase + 2 * math.pi * i / sides),
                    cy + ry * math.sin(phase + 2 * math.pi * i / sides), z)) for i in range(sides)]


def _loft(b, rings, slot, ao_rows, cap_top=True, cap_bottom=True):
    """Side faces between consecutive rings (top to bottom, same vertex count), optional caps."""
    sides = len(rings[0])
    for r in range(len(rings) - 1):
        top, bot = rings[r], rings[r + 1]
        for i in range(sides):
            j = (i + 1) % sides
            b.face([bot[i], bot[j], top[j], top[i]], slot,
                   ao=[ao_rows[r + 1], ao_rows[r + 1], ao_rows[r], ao_rows[r]])
    if cap_top:
        b.face(list(rings[0]), slot, ao=[ao_rows[0]] * sides)
    if cap_bottom:
        b.face(list(reversed(rings[-1])), slot, ao=[ao_rows[-1]] * sides)


def _sphere(b, centre, radius, segments, rings, slot, z_from=-1.0, ao_low=0.8):
    """Faceted latitude-longitude sphere (or its top part, polar cosine from 1 down to z_from)."""
    c = Vector(centre)
    lat = [math.pi * k / rings for k in range(rings + 1)]
    lat = [a for a in lat if math.cos(a) >= z_from - 1e-6]
    def pt(a, i):
        th = 2 * math.pi * i / segments
        return c + radius * Vector((math.sin(a) * math.cos(th), math.sin(a) * math.sin(th), math.cos(a)))
    def shade(a):
        return ao_low + (1.0 - ao_low) * 0.5 * (1.0 + math.cos(a))
    for k in range(len(lat) - 1):
        a0, a1 = lat[k], lat[k + 1]
        for i in range(segments):
            j = (i + 1) % segments
            if k == 0:
                b.face([pt(a0, 0), pt(a1, i), pt(a1, j)], slot, ao=[shade(a0), shade(a1), shade(a1)])
            elif abs(math.sin(a1)) < 1e-6:
                b.face([pt(a0, i), pt(a1, 0), pt(a0, j)], slot, ao=[shade(a0), shade(a1), shade(a0)])
            else:
                b.face([pt(a0, i), pt(a1, i), pt(a1, j), pt(a0, j)], slot,
                       ao=[shade(a0), shade(a1), shade(a1), shade(a0)])
    if len(lat) - 1 < rings:                           # an open dome: close it at its lowest ring
        a = lat[-1]
        b.face([pt(a, i) for i in reversed(range(segments))], slot, ao=[shade(a)] * segments)


def build_avatar(p=AVATAR):
    """The walker's copy: origin at the eyes, facing +Y, right hand on +X; left side red, right side blue."""
    obj = _replace_object("Hero_Avatar", _collection(HERO))
    b = Builder()
    hy = -p["head_back"]
    _sphere(b, (0.0, hy, 0.0), p["head_radius"], p["head_segments"], p["head_rings"], "AvatarBody")

    # Visor: a curved band round the front of the head, a little proud of it, just below the eyes.
    r_out = p["head_radius"] + p["visor_depth"]
    half = math.radians(0.5 * p["visor_width_deg"])
    z_top, z_bot = -p["visor_drop"] + 0.5 * p["visor_height"], -p["visor_drop"] - 0.5 * p["visor_height"]
    n = p["visor_segments"]
    def vp(t, r, z):
        a = math.pi / 2 - half + 2 * half * t              # around +Y
        return Vector((r * math.cos(a), hy + r * math.sin(a), z))
    r_in = p["head_radius"] * 0.9
    # A closed curved box; the builder turns every face outwards.
    for k in range(n):
        t0, t1 = k / n, (k + 1) / n
        b.face([vp(t0, r_out, z_bot), vp(t1, r_out, z_bot), vp(t1, r_out, z_top), vp(t0, r_out, z_top)], "Text")
        b.face([vp(t1, r_in, z_bot), vp(t0, r_in, z_bot), vp(t0, r_in, z_top), vp(t1, r_in, z_top)], "Text")
        b.face([vp(t0, r_out, z_top), vp(t1, r_out, z_top), vp(t1, r_in, z_top), vp(t0, r_in, z_top)], "Text")
        b.face([vp(t1, r_out, z_bot), vp(t0, r_out, z_bot), vp(t0, r_in, z_bot), vp(t1, r_in, z_bot)], "Text")
    for t in (0.0, 1.0):
        b.face([vp(t, r_in, z_bot), vp(t, r_out, z_bot), vp(t, r_out, z_top), vp(t, r_in, z_top)], "Text")

    # Neck and torso.
    _loft(b, [_ring(0, hy, p["neck_top"], p["neck_radius"], p["neck_radius"], 6),
              _ring(0, hy, p["neck_bottom"], p["neck_radius"], p["neck_radius"], 6)],
          "AvatarBody", [0.7, 0.8])
    rings = [_ring(0, hy, z, rx, ry, p["torso_sides"], math.pi / p["torso_sides"]) for z, rx, ry in p["torso"]]
    _loft(b, rings, "AvatarBody", [0.95, 1.0, 0.85, 0.7])

    # Shoulders and arms: the left side (-X) in AvatarLeft, the right side (+X) in AvatarRight.
    for side, slot in ((-1.0, "AvatarLeft"), (1.0, "AvatarRight")):
        _sphere(b, (side * p["shoulder_x"], hy, p["shoulder_z"]), p["shoulder_radius"], 8, 4, slot,
                z_from=-0.05, ao_low=0.75)
        x = side * p["arm_out"]
        arm = [_ring(x, hy, z, p["arm_radius"], p["arm_radius"], p["arm_sides"])
               for z in (p["shoulder_z"] - 0.02, p["elbow_z"], p["wrist_z"])]
        _loft(b, arm, slot, [0.9, 0.85, 0.7])
    b.write(obj)
    obj.location = (0.0, 0.0, 0.0)
    return obj


def build_preview():
    """Context only, never exported: one Glued cell (floor grid, quarter posts) around the landmark's spot."""
    col = _collection(PREVIEW)
    obj = _replace_object("Preview_Cell", col)
    b = Builder()
    cx, cy = -LANDMARK_BASE[0], -LANDMARK_BASE[1]         # cell centre relative to the landmark's foot
    h = 0.5 * CELL
    k = 3                                                  # grid squares per side (0.5 m)
    b.face([(cx - h, cy - h, 0), (cx + h, cy - h, 0), (cx + h, cy + h, 0), (cx - h, cy + h, 0)], "Floor",
           lines=[(0, 0), (k, 0), (k, k), (0, k)])
    q = 0.04
    for sx in (-1, 1):
        for sy in (-1, 1):
            _prism(b, (cx + sx * (h - 0.5 * q), cy + sy * (h - 0.5 * q)), 0.5 * q * 1.414, 0.0, 2.2, 4, "Post",
                   line_rows=4.0)
    b.write(obj)
    return obj


def build_mirror_preview():
    """An x-mirrored copy of the landmark beside it, for judging chirality by eye."""
    src = bpy.data.objects.get("Hero_Landmark")
    col = _collection(PREVIEW)
    old = bpy.data.objects.get("Preview_LandmarkMirror")
    if old is not None:
        bpy.data.objects.remove(old, do_unlink=True)
    if src is None:
        return None
    m = bpy.data.objects.new("Preview_LandmarkMirror", src.data)
    col.objects.link(m)
    m.scale = (-1.0, 1.0, 1.0)
    m.location = (0.9, 0.0, 0.0)
    return m


def setup_camera(target=(0.2, 0.3, 0.8), distance=3.2, elevation=18.0, azimuth=-60.0, name="Preview_Camera"):
    cam = bpy.data.objects.get(name)
    if cam is None:
        cam = bpy.data.objects.new(name, bpy.data.cameras.new(name))
        _collection(PREVIEW).objects.link(cam)
    cam.data.lens = 35
    el, az = math.radians(elevation), math.radians(azimuth)
    t = Vector(target)
    cam.location = t + distance * Vector((math.cos(el) * math.cos(az), math.cos(el) * math.sin(az), math.sin(el)))
    cam.rotation_euler = (t - cam.location).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = cam
    return cam


def build_all():
    build_preview()
    build_landmark()
    build_mirror_preview()
