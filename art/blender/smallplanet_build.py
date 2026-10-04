"""Builds the Small Planet (S4) grassland world of SmallPlanet_World.blend.

Run inside Blender: exec(open(r"<this file>").read()); then build_all(). Everything is rebuilt from scratch on each
run, so edit the parameters below and run again. Hand edits are lost on a rebuild. build_all(planet_scale=1.0)
rebuilds the original loop-fit planet for comparison (the next plain build_all() goes back to PLANET_SCALE).

Layout: the {4,3} tiling is six tiles, the faces of a cube blown onto a sphere of radius R = 2s/pi (Master Plan S4),
where s is the tile side. Home (+Z) carries the scanned room; East, North, West and South ring it; Beacon (-Z) is the
antipode with the pole beacon. A post-and-rail fence runs round the equator, through the middle of the four side
tiles, and divides the planet into the home half (green) and the far half (gold). The South fence has a gate, and a
stepping-stone path runs from the room through it to the stone circle.

Planet size: the room is fixed at ROOM = 3.4 m wall to wall (the reference room, loop-fit lap square S_LOOP = 2.4 m).
The tile side is s = PLANET_SCALE * S_LOOP, so R = 2s/pi. PLANET_SCALE = 1.0 is the plan's loop-fit planet
(s = 2.4 m, R = 1.528 m): there the room's corners (2.40 m from the room centre) touch the equator fence (2.40 m
away), so the green home half is almost all room. The default 1.5 gives s = 3.6 m, R = 2.292 m: the fence is 3.6 m
from the room centre, 1.9 m beyond the walls and 1.2 m beyond the corners, which leaves a real green band to dress.
Trade-offs of PLANET_SCALE > 1:
  * The loop-fit property is gone: three right-angle turns of side s still close a loop, but s is now larger than
    the walkable lap square (min(W, D) - 1.0), so the loop no longer fits inside the room. The walker can no longer
    close the three-corner loop on foot. The Unity side must use the same R (or its own R from the room will not
    match these meshes); that has to be agreed there.
  * Curvature is 1/R, so it is weaker: 1 m of walking turns the view 0.44 rad instead of 0.65 rad. Angular layout is
    unchanged (the equator is still pi/2 from the room centre, the discard is still 2.6 rad, which is now 5.96 m
    instead of 3.97 m), but props have fixed metric sizes, so on a bigger planet they are relatively smaller. The far
    half therefore uses bold, larger silhouettes.

Coordinates: every tile is authored in tile-local chart coordinates, which are what the warp shader receives
(Master Plan A.6): metres, origin at the tile centre, +Z up; a point at chart position c lies at geodesic distance
|c| from the tile centre in direction c/|c|. On side tiles local +Y points towards Home, so the fence is the line
y = 0. Blender +X = Unity right, +Y = Unity forward, +Z = up (same as hero_build.py). All faces wind
counter-clockwise seen from outside (the preview material culls back faces, as Unity will).

For the exporter: author scale is AUTHOR_S / AUTHOR_R (s = 3.6 m, R = 2*3.6/pi = 2.2918 m at the default
PLANET_SCALE); the current build's values are the globals S and R, the custom properties planet_R / tile_side on
each Tile_<Name> empty and SmallPlanet_R / SmallPlanet_tile_side on the scene. Normalise chart xy by R.
Colours are AtlasPalette cells (see PALETTE): the face int attribute 'Pal' holds the cell index, the corner colour
'Col' the cell colour times a small brightness factor (per-face jitter of a few percent and a per-tile 'shade' of
0.95-1.04). Only the lantern uses an emissive cell (193, Beacon; material SP_Lantern). The beacon cairn sits at the Beacon tile centre.
Geometry rules (Unity's warp bends vertices, not faces): no prop face is wider than PROP_EDGE = 0.25 m in plan
(build_all reports the widest), the ground grid grows with R (ground_grid(): 18 cells at the default, 16 at
PLANET_SCALE = 1.0), ordinary props stay under 1.2 m (hero pines and the standing stone reach 1.38 m). The Home
kerb is a preview of the room walls; Unity draws the room, so Tile_Home_Props is not exported.
render_previews(folder) renders the walker, level, east, globe and net views to sp_<view>_scale<k>.png.

Collections:
  SmallPlanet_Tiles  the six tiles laid out as an unfolded cube net. The empty Tile_<Name> carries the layout;
                     its children (_Ground, _Props, _Fence) sit at identity under it, so their mesh data is in
                     chart coordinates. Export the children's local data, not their world transforms.
  SmallPlanet_Globe  preview only: the same tiles wrapped onto the sphere, seen from outside, with heights scaled
                     by GLOBE_HEIGHT_SCALE so it reads as a map. In the headset the
                     planet is seen from inside, through the log-map warp, which looks very different.
  SmallPlanet_Walker preview only: what the warp shader shows a walker standing at the room centre. Every vertex
                     goes through the log map about that point, fragments beyond 2.6 rad are dropped and the fog
                     is baked into the 'Fog' attribute. build_walker(fog_start, fog_end) rebuilds it for other fog.
"""
import colorsys
import math
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

S_LOOP = 2.4                 # loop-fit lap square of the reference room (s = min(W, D) - 1.0)
ROOM = S_LOOP + 1.0          # wall-to-wall room size on Home: 3.4 m, independent of the planet size
PLANET_SCALE = 1.5           # tile side / S_LOOP. 1.0 = loop-fit planet (R = 1.528 m); 1.5 -> s = 3.6 m, R = 2.292 m
AUTHOR_S = S_LOOP * PLANET_SCALE          # default tile side the meshes are authored at: 3.6 m
AUTHOR_R = 2 * AUTHOR_S / math.pi        # default sphere radius the meshes are authored at: 2.2918 m
S = R = NET_GAP = 0.0        # current tile side, sphere radius and net gap; set by set_scale() (build_all)
ROOM_CLEAR = 0.3             # generated props stay this far outside the room footprint
GRID_MIN = 16                # ground grid cells per tile side (A.6: n = 16 on the walker's tile and its neighbours)
GROUND_CELL = 0.26           # m: the grid grows with the planet so a centre cell stays this small (warp bends vertices)
PROP_EDGE = 0.25             # m: longest horizontal prop edge (the warp bends vertices, not faces)
SEED = 4
FENCE_SPACING = 0.6          # target post spacing; the posts per tile are rounded so spacing is even across edges
FENCE_CLEAR = 0.3            # props stay this far from the fence line
GATE_HALF = 0.4              # half-width of the gate opening in the South fence
PATH_HALF = 0.3              # half-width of the path strip on South
GLOBE_OFFSET = Vector((20.0, 0.0, 0.0))
GLOBE_HEIGHT_SCALE = 0.4     # globe preview only: props at true height read as spikes, not a map
WALKER_OFFSET = Vector((-30.0, 0.0, 0.0))
DISCARD = 2.6                # rad: the warp drops fragments beyond this angle (Master Plan P1-B08)
PLAN_FOG = (0.5 * math.pi, 0.8 * math.pi)      # rad: fog start and fully opaque, as the plan has it today
PUSHED_FOG = (0.65 * math.pi, DISCARD)         # rad: fog pushed back so the far half stays visible
SKY = "BFE3F5"


def set_scale(k):
    """Size the planet: tile side s = k * S_LOOP, R = 2s/pi. The room stays ROOM wide."""
    global S, R, NET_GAP
    S = S_LOOP * k
    R = 2 * S / math.pi
    NET_GAP = 0.125 * S


set_scale(PLANET_SCALE)


def ground_grid():
    """Even cell count per tile side: at least GRID_MIN, and centre cells (R * 2/n wide) no bigger than GROUND_CELL."""
    return max(GRID_MIN, 2 * math.ceil(R / GROUND_CELL))


TILES, GLOBE, LABELS, WALKER = "SmallPlanet_Tiles", "SmallPlanet_Globe", "SmallPlanet_Labels", "SmallPlanet_Walker"

# Every colour is a cell of Unity's AtlasPalette (ATLAS/Assets/Atlas/Rendering/Look/AtlasPalette.cs, ported in
# _atlas_srgb below): Unity's Atlas/Surface shader takes albedo from that 16 x 16 palette, not from vertex colour.
# Each face carries the cell index in the face attribute 'Pal'; 'Col' is the cell colour times a small brightness
# jitter (the exporter's shade byte). Rows 12-15 are emissive; only the lantern uses one (193 = Beacon).
PALETTE = dict(
    grass_near=116, grass_near2=100, grass_yellow=115, grass_blue=117,     # 5EBD5E 4A944A 8EBD5E 5EBD8E
    grass_olive=114, grass_far=29, grass_deep=113, grass_pale=126,          # BDBD5E C9A227 BD8E5E BD997B
    dirt=110, path=126, stepping=124,                                       # 947860 BD997B BDB1A6
    blade_near=100, blade_far=98, stem=100, leaf=84, leaf_light=115,        # 4A944A 94944A 4A944A 366B36 8EBD5E
    rock=109, rock2=108, moss=99, basalt=93,                                # 828B94 948B82 6F944A 5E656B
    post=94, rail=97, pine=133, trunk=94, birch=127, bark=79,               # 6B5746 946F4A 305743 6B5746 BDBDBD 474747
    slate=22, sandstone=159, pale=124, floor=124,                           # 6B7F99 BC6E54 BDB1A6 BDB1A6
    hay=114, hay_end=124, haystack=113, sun_petal=29, sun_disc=147,         # BDBD5E BDB1A6 BD8E5E C9A227 482A20
    lavender=121, cap=27, cap_spot=127, gill=124, bush=84,                  # 8E5EBD D9534F BDBDBD BDB1A6 366B36
    red=27, gold=29, purple=121, white=127, pink=123, blue=28,              # D9534F C9A227 8E5EBD BDBDBD BD5E8E 3FA7D6
    lantern=193,                                                            # emissive Beacon cell FFD166
)


def _side(n):
    n = Vector(n)
    v = Vector((0.0, 0.0, 1.0))      # local +Y points towards Home
    return n, v.cross(n), v


# near / far = (main, alternate) ground cells (~12% of faces take the alternate) and a per-tile brightness 'shade'
# (the exporter's shade byte), so tiles are told apart while every face stays on a palette cell
TILE_DEFS = [
    dict(name="Home", frame=(Vector((0, 0, 1)), Vector((1, 0, 0)), Vector((0, 1, 0))), net=(0, 0, 0), kind="home",
         near=("grass_near", "grass_yellow"), shade=1.0),
    dict(name="East", frame=_side((1, 0, 0)), net=(1, 0, 90), kind="side", flower="gold", hero="standing_stone",
         theme="sunflowers", near=("grass_near", "grass_yellow"), far=("grass_far", "grass_olive"), shade=1.04),
    dict(name="North", frame=_side((0, 1, 0)), net=(0, 1, 180), kind="side", flower="purple", hero="balanced_rocks",
         theme="lavender", near=("grass_near", "grass_blue"), far=("grass_far", "grass_deep"), shade=0.95),
    dict(name="West", frame=_side((-1, 0, 0)), net=(-1, 0, -90), kind="side", flower="white", hero="pines",
         theme="birches", near=("grass_near", "grass_blue"), far=("grass_far", "grass_pale"), shade=1.0),
    dict(name="South", frame=_side((0, -1, 0)), net=(0, -1, 0), kind="side", flower="red", hero="stone_circle",
         theme="mushrooms", near=("grass_near", "grass_yellow"), far=("grass_far", "grass_deep"), shade=0.97,
         gate=True),
    dict(name="Beacon", frame=(Vector((0, 0, -1)), Vector((1, 0, 0)), Vector((0, -1, 0))), net=(0, -2, 0),
         kind="beacon", far=("grass_deep", "grass_pale"), shade=0.96),
]
HOME_FRAME = TILE_DEFS[0]["frame"]


# ---- colour ---------------------------------------------------------------------------------------------------

def _lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


_STRUCTURE = [0x1B222B, 0x222B36, 0x2A3542, 0x33404F, 0x3A4858, 0x50627A, 0x6B7F99, 0x161C24,
              0x0E1319, 0x3B4654, 0x7A8794, 0xD9534F, 0x3FA7D6, 0xC9A227, 0x4A3F6B, 0x2E2A45]
_ACCENTS = [0x7DF9FF, 0xFFD166, 0x9BE564, 0xFF6B6B, 0xFFE66D, 0xFFB020, 0xFF3B30, 0x5AC8FA,
            0xFFFFFF, 0x34C759, 0xFFCC00, 0x8E8E93, 0xFFFFFF, 0xFFFFFF, 0x6FA8C8, 0x000000]


def _atlas_srgb(index):
    """sRGB (0..1) of AtlasPalette cell index at the cell centre: a port of AtlasPalette.TexelSrgb."""
    column, row = index & 15, (index >> 4) & 15
    along = (column * 8 + 4) / 127.0

    def hsv(h, s, v):
        return colorsys.hsv_to_rgb((h % 360.0) / 360.0, s, v)

    def hexrgb(u):
        return ((u >> 16) & 255) / 255.0, ((u >> 8) & 255) / 255.0, (u & 255) / 255.0

    if row == 0:
        return hsv(215, 0.25 - 0.01 * column, 0.03 + 0.05 * column)
    if row == 1:
        return hexrgb(_STRUCTURE[column])
    if row in (2, 3):
        k = (row - 2) * 16 + column
        return hsv(0, 0, 0.45) if k == 0 else hsv(k * 137.5, 0.40, 0.50)
    if 4 <= row <= 7:
        v = (0.28, 0.42, 0.58, 0.74)[row - 4]
        if column < 12:
            return hsv(column * 30, 0.5, v)
        return {12: hsv(30, 0.12, v), 13: hsv(210, 0.12, v), 14: hsv(28, 0.35, v)}.get(column, hsv(0, 0, v))
    if row == 8:
        return hsv((200, 150, 35, 320)[column // 4], 0.45, 0.22 + 0.12 * (column % 4))
    if row == 9:
        return hsv(15, 0.55, 0.15 + 0.6 * along)
    if row == 10:
        return hsv(205, 0.55, 0.15 + 0.6 * along)
    if row == 11:
        return hsv(215, 0.20, 0.40 - 0.35 * along)
    if row == 12:
        return hexrgb(_ACCENTS[column])
    if row in (13, 14):
        return hsv(((row - 13) * 16 + column) * 137.5, 0.65, 0.95)
    return tuple(a + (b - a) * along for a, b in zip(hexrgb(0xFF7F6A), hexrgb(0x8E6BFF)))


class Pc(tuple):
    """A linear rgb colour that remembers its palette cell (idx)."""
    idx = -1


def _pc(rgb, idx):
    p = Pc(rgb)
    p.idx = idx
    return p


def col(key_or_index):
    """Linear rgb of a palette cell, by PALETTE key or cell index (8-bit rounded, as the shader samples it)."""
    idx = PALETTE[key_or_index] if isinstance(key_or_index, str) else int(key_or_index)
    return _pc(tuple(_lin(round(c * 255) / 255.0) for c in _atlas_srgb(idx)), idx)


def hexcol(h):
    """Linear rgb of an sRGB hex string; previews only (sky and fog), never on exported faces."""
    return tuple(_lin(int(h[i:i + 2], 16) / 255.0) for i in (0, 2, 4))


def jitter(rgb, rng, amount=0.06):
    """Brightness-only variation (the exporter's shade byte); the palette cell is kept."""
    f = 1.0 + rng.uniform(-amount, amount)
    return _pc(tuple(min(1.0, c * f) for c in rgb), getattr(rgb, "idx", -1))


def pick(pair, rng, alt=0.12):
    return col(pair[1] if rng.random() < alt else pair[0])


# ---- chart <-> sphere -----------------------------------------------------------------------------------------

def chart_from_gnomonic(kx, ky):
    """A.6: chart position = R * atan(|k|) * k/|k| for k on the gnomonic square [-1, 1]^2."""
    m = math.hypot(kx, ky)
    if m < 1e-12:
        return 0.0, 0.0
    d = R * math.atan(m)
    return d * kx / m, d * ky / m


def to_unit(frame, x, y):
    n, u, v = frame
    d = math.hypot(x, y)
    if d < 1e-12:
        return n.copy()
    return n * math.cos(d / R) + (u * x + v * y) * (math.sin(d / R) / d)


def to_globe(frame, p):
    return to_unit(frame, p[0], p[1]) * (R + p[2] * GLOBE_HEIGHT_SCALE)


def to_chart(frame, w):
    """Log map of the unit vector w into this tile's chart."""
    n, u, v = frame
    c = max(-1.0, min(1.0, w.dot(n)))
    t = w - n * c
    if t.length < 1e-12:
        return 0.0, 0.0
    t.normalize()
    a = R * math.acos(c)
    return a * t.dot(u), a * t.dot(v)


def in_room(frame, x, y, clear=ROOM_CLEAR):
    hx, hy = to_chart(HOME_FRAME, to_unit(frame, x, y))
    return max(abs(hx), abs(hy)) < ROOM / 2 + clear


# ---- mesh building --------------------------------------------------------------------------------------------

class Faces:
    """Faces in chart coordinates, each with a flat colour and a material index (0 = vertex colour, 1 = lantern)."""

    def __init__(self):
        self.items = []

    def face(self, pts, rgb, mat=0):
        self.items.append(([tuple(p) for p in pts], rgb, mat))

    def out(self, pts, rgb, centre, mat=0):
        """Add a face wound so its normal points away from centre (for convex pieces)."""
        pts = [Vector(p) for p in pts]
        n = Vector((0.0, 0.0, 0.0))
        for a, b in zip(pts, pts[1:] + pts[:1]):
            n += a.cross(b)
        mid = sum(pts, Vector()) / len(pts)
        if n.dot(mid - Vector(centre)) < 0:
            pts.reverse()
        if len(pts) > 4:                          # fan from the middle: no long diagonals for the warp to miss
            for a, b in zip(pts, pts[1:] + pts[:1]):
                self.face([a, b, mid], rgb, mat)
        else:
            self.face(pts, rgb, mat)

    def facing(self, pts, rgb, direction, back=None):
        """A flat face whose front looks along direction; back (a colour) adds the reverse face."""
        pts = [Vector(p) for p in pts]
        n = (pts[1] - pts[0]).cross(pts[2] - pts[0])
        if n.dot(Vector(direction)) < 0:
            pts.reverse()
        self.face(pts, rgb)
        if back is not None:
            self.face(list(reversed(pts)), back)

    def box(self, lo, hi, rgb, skip=()):
        x0, y0, z0 = lo
        x1, y1, z1 = hi
        quads = dict(
            zmin=[(x0, y0, z0), (x0, y1, z0), (x1, y1, z0), (x1, y0, z0)],
            zmax=[(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)],
            ymin=[(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)],
            ymax=[(x1, y1, z0), (x0, y1, z0), (x0, y1, z1), (x1, y1, z1)],
            xmin=[(x0, y1, z0), (x0, y0, z0), (x0, y0, z1), (x0, y1, z1)],
            xmax=[(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)],
        )
        for key, q in quads.items():
            if key not in skip:
                self.face(q, rgb)

    def tris(self):
        return sum(len(p) - 2 for p, _, _ in self.items)


def _ico(sub):
    t = (1 + 5 ** 0.5) / 2
    verts = [Vector(v).normalized() for v in [(-1, t, 0), (1, t, 0), (-1, -t, 0), (1, -t, 0), (0, -1, t), (0, 1, t),
                                             (0, -1, -t), (0, 1, -t), (t, 0, -1), (t, 0, 1), (-t, 0, -1), (-t, 0, 1)]]
    faces = [(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2),
             (10, 7, 6), (7, 1, 8), (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11),
             (6, 2, 10), (8, 6, 7), (9, 8, 1)]
    for _ in range(sub):
        cache, out = {}, []

        def mid(a, b):
            key = (min(a, b), max(a, b))
            if key not in cache:
                verts.append(((verts[a] + verts[b]) / 2).normalized())
                cache[key] = len(verts) - 1
            return cache[key]

        for a, b, c in faces:
            ab, bc, ca = mid(a, b), mid(b, c), mid(c, a)
            out += [(a, ab, ca), (b, bc, ab), (c, ca, bc), (ab, bc, ca)]
        faces = out
    return verts, faces


def rock(f, rng, cx, cy, sx, sy, sz, rgb, moss=0.5, sub=1, sink=0.3, z0=0.0, moss_col=None, top=0.72):
    """A faceted lump with half-extents (sx, sy, sz); upward faces turn moss_col with chance moss. Returns its top."""
    while sub < 3 and 1.05 / 2 ** sub * max(sx, sy) * 1.12 > PROP_EDGE:
        sub += 1                                  # icosphere edge ~1.05 / 2^sub of the radius
    verts, faces = _ico(sub)
    yaw = rng.uniform(0, 2 * math.pi)
    cs, sn = math.cos(yaw), math.sin(yaw)
    zc = z0 + sz * (1 - 2 * sink)
    pts = []
    for v in verts:
        r = rng.uniform(0.8, 1.12)
        x, y, z = v.x * r * sx, v.y * r * sy, v.z * r * sz
        pts.append(Vector((cx + x * cs - y * sn, cy + x * sn + y * cs, zc + z)))
    accent = moss_col or col("moss")
    for a, b, c in faces:
        normal = (pts[b] - pts[a]).cross(pts[c] - pts[a]).normalized()
        shade = accent if normal.z > top and rng.random() < moss else rgb
        f.face([pts[a], pts[b], pts[c]], jitter(shade, rng, 0.07))
    return max(p.z for p in pts)


def blade(f, rng, bx, by, h, rgb, r=0.022):
    a0 = rng.uniform(0, 2 * math.pi)
    base = [(bx + r * math.cos(a0 + k * 2 * math.pi / 3), by + r * math.sin(a0 + k * 2 * math.pi / 3), 0.0)
            for k in range(3)]
    tip = (bx + rng.uniform(-0.04, 0.04), by + rng.uniform(-0.04, 0.04), h)
    for k in range(3):
        f.face([base[k], base[(k + 1) % 3], tip], jitter(rgb, rng, 0.08))


def tuft(f, rng, cx, cy, rgb, n=(3, 4), hmax=0.32):
    for _ in range(rng.randint(*n)):
        blade(f, rng, cx + rng.uniform(-0.06, 0.06), cy + rng.uniform(-0.06, 0.06), rng.uniform(0.14, hmax), rgb,
              r=0.03)


def flower(f, rng, cx, cy, rgb, hmax=0.36, rmax=0.08):
    h = rng.uniform(0.2, hmax)
    blade(f, rng, cx, cy, h, col("stem"), r=0.012)
    r = rng.uniform(0.055, rmax)
    c = Vector((cx, cy, h))
    X, Y, Z = Vector((r, 0, 0)), Vector((0, r, 0)), Vector((0, 0, r * 0.7))
    for a, b, z in [(X, Y, Z), (Y, -X, Z), (-X, -Y, Z), (-Y, X, Z), (Y, X, -Z), (-X, Y, -Z), (-Y, -X, -Z),
                    (X, -Y, -Z)]:
        f.face([c + a, c + b, c + z], jitter(rgb, rng, 0.06))


def cone(f, rng, cx, cy, z0, radius, height, rgb, sides=6, base=True):
    sides = max(sides, math.ceil(2 * math.pi * radius / (0.75 * PROP_EDGE)))
    bands = max(1, math.ceil(radius / (0.75 * PROP_EDGE)))   # extra rings: slant edges and diagonals short in plan
    a0 = rng.uniform(0, 2 * math.pi)
    rings = []
    for i in range(bands):
        r, z = radius * (1 - i / bands), z0 + height * i / bands
        rings.append([(cx + r * math.cos(a0 + k * 2 * math.pi / sides), cy + r * math.sin(a0 + k * 2 * math.pi / sides),
                       z) for k in range(sides)])
    tip = (cx, cy, z0 + height)
    for k in range(sides):
        k2 = (k + 1) % sides
        shade = jitter(rgb, rng, 0.08)
        for lo, hi in zip(rings, rings[1:]):
            f.face([lo[k], lo[k2], hi[k2], hi[k]], shade)
        f.face([rings[-1][k], rings[-1][k2], tip], shade)
    if base:
        f.face(list(reversed(rings[0])), jitter(rgb, rng, 0.08))


def prism(f, rng, cx, cy, z0, z1, radius, rgb, sides=6, cap=True):
    sides = max(sides, math.ceil(2 * math.pi * radius / PROP_EDGE))
    a0 = rng.uniform(0, 2 * math.pi)
    ring = [(radius * math.cos(a0 + k * 2 * math.pi / sides), radius * math.sin(a0 + k * 2 * math.pi / sides))
            for k in range(sides)]
    centre = (cx, cy, (z0 + z1) / 2)
    for k in range(sides):
        (ax, ay), (bx, by) = ring[k], ring[(k + 1) % sides]
        f.out([(cx + ax, cy + ay, z0), (cx + bx, cy + by, z0), (cx + bx, cy + by, z1), (cx + ax, cy + ay, z1)],
              jitter(rgb, rng, 0.06), centre)
    if cap:
        f.out([(cx + ax, cy + ay, z1) for ax, ay in ring], jitter(rgb, rng, 0.06), centre)


def pine(f, rng, cx, cy, scale=1.0):
    trunk = 0.05 * scale
    f.box((cx - trunk, cy - trunk, 0), (cx + trunk, cy + trunk, 0.28 * scale), jitter(col("trunk"), rng),
          skip=("zmin",))
    for z0, rad, h in [(0.2, 0.34, 0.42), (0.47, 0.27, 0.36), (0.72, 0.19, 0.38)]:
        cone(f, rng, cx, cy, z0 * scale, rad * scale, h * scale, col("pine"), base=False)   # eye never below


def birch(f, rng, cx, cy, scale=1.0):
    t, h = 0.035 * scale, 0.72 * scale
    f.box((cx - t, cy - t, 0), (cx + t, cy + t, h), jitter(col("birch"), rng, 0.04), skip=("zmin",))
    for z in (0.18, 0.4):                                      # dark bark bands
        zz = z * scale + rng.uniform(-0.04, 0.04)
        f.box((cx - t - 0.004, cy - t - 0.004, zz), (cx + t + 0.004, cy + t + 0.004, zz + 0.035), col("bark"),
              skip=("zmin", "zmax"))
    rock(f, rng, cx, cy, 0.3 * scale, 0.28 * scale, 0.3 * scale, col("leaf_light"), moss=0.35, sub=1, sink=0.0,
         z0=h - 0.22 * scale, moss_col=col("gold"), top=0.6)


def bush(f, rng, cx, cy, s, blossom=None):
    rock(f, rng, cx, cy, s * rng.uniform(1.0, 1.3), s, s * 0.75, col("bush"), moss=0.3 if blossom else 0.0,
         sub=1, sink=0.2, moss_col=blossom, top=0.3)


def haystack(f, rng, cx, cy, s=1.0):
    cone(f, rng, cx, cy, 0.0, 0.36 * s, 0.38 * s, col("haystack"), sides=8, base=False)
    cone(f, rng, cx, cy, 0.26 * s, 0.2 * s, 0.36 * s, col("hay"), sides=8, base=False)


def bale(f, rng, cx, cy, r=0.24, length=0.42, sides=8):
    """A round hay bale lying on its side."""
    yaw = rng.uniform(0, math.pi)
    ax = Vector((math.cos(yaw), math.sin(yaw), 0.0))
    pp = Vector((-math.sin(yaw), math.cos(yaw), 0.0))
    up = Vector((0.0, 0.0, 1.0))
    c = Vector((cx, cy, r * 0.92))
    rings = []
    for e in (-0.5, -1 / 6, 1 / 6, 0.5):
        rings.append([c + ax * (e * length) + (pp * math.cos(k * 2 * math.pi / sides) +
                                               up * math.sin(k * 2 * math.pi / sides)) * r for k in range(sides)])
    for k in range(sides):
        k2 = (k + 1) % sides
        shade = jitter(col("hay"), rng, 0.05) if k % 2 else jitter(col("haystack"), rng, 0.05)
        for r0, r1 in zip(rings, rings[1:]):
            f.out([r0[k], r0[k2], r1[k2], r1[k]], shade, c)
    for ring in (rings[0], rings[-1]):
        f.out(ring, jitter(col("hay_end"), rng, 0.04), c)


def sunflower(f, rng, cx, cy, facing=(0.0, 1.0)):
    h = rng.uniform(0.72, 0.95)                   # head top stays under 1.2 m
    st = 0.02
    f.box((cx - st, cy - st, 0), (cx + st, cy + st, h), jitter(col("stem"), rng), skip=("zmin",))
    for side in (-1, 1):
        z = h * rng.uniform(0.3, 0.55)
        leaf = [(cx, cy, z), (cx, cy, z + 0.09), (cx + side * 0.2, cy + rng.uniform(-0.05, 0.05), z + 0.12)]
        f.facing(leaf, jitter(col("leaf"), rng), (0, 0, 1), back=jitter(col("leaf"), rng))
    d = Vector((facing[0] + rng.uniform(-0.3, 0.3), facing[1] + rng.uniform(-0.3, 0.3), 0.5)).normalized()
    a = d.cross(Vector((0, 0, 1))).normalized()
    b = a.cross(d).normalized()
    c = Vector((cx, cy, h)) + d * 0.03
    n, rd, rp = 10, 0.075, 0.19
    ring = [c + (a * math.cos(k * 2 * math.pi / n) + b * math.sin(k * 2 * math.pi / n)) * rd for k in range(n)]
    back = jitter(col("leaf"), rng)
    f.facing(ring, jitter(col("sun_disc"), rng, 0.05), d, back=back)
    for k in range(n):
        t = (k + 0.5) * 2 * math.pi / n
        tip = c + (a * math.cos(t) + b * math.sin(t)) * rp - d * 0.02
        f.facing([ring[k], ring[(k + 1) % n], tip], jitter(col("sun_petal"), rng, 0.06), d, back=back)


def lavender(f, rng, cx, cy):
    rock(f, rng, cx, cy, 0.16, 0.14, 0.1, col("leaf"), moss=0.0, sub=0, sink=0.2)
    for _ in range(4):
        x, y = cx + rng.uniform(-0.09, 0.09), cy + rng.uniform(-0.09, 0.09)
        cone(f, rng, x, y, 0.05, 0.035, rng.uniform(0.32, 0.46), col("lavender"), sides=4, base=False)


def mushroom(f, rng, cx, cy, s=1.0):
    h = 0.3 * s
    prism(f, rng, cx, cy, 0.0, h, 0.055 * s, col("cap_spot"), sides=6, cap=False)
    sides, a0 = 9, rng.uniform(0, 2 * math.pi)
    tilt = Vector((rng.uniform(-0.03, 0.03), rng.uniform(-0.03, 0.03), 0.0)) * s
    centre = Vector((cx, cy, h + 0.05 * s)) + tilt

    def ring(r, z):
        return [Vector((cx + r * math.cos(a0 + k * 2 * math.pi / sides),
                        cy + r * math.sin(a0 + k * 2 * math.pi / sides), z)) + tilt for k in range(sides)]

    r0, r1 = ring(0.21 * s, h - 0.02 * s), ring(0.15 * s, h + 0.1 * s)
    apex = Vector((cx, cy, h + 0.19 * s)) + tilt
    f.out(r0, jitter(col("gill"), rng, 0.04), centre)
    for k in range(sides):
        k2 = (k + 1) % sides
        spot = rng.random() < 0.3
        f.out([r0[k], r0[k2], r1[k2], r1[k]], col("cap_spot") if spot else jitter(col("cap"), rng, 0.06), centre)
        f.out([r1[k], r1[k2], apex], col("cap_spot") if rng.random() < 0.25 else jitter(col("cap"), rng, 0.06),
              centre)


def slab(f, rng, cx, cy, r, rgb, sides=6, z=0.03):
    """A flat stepping stone."""
    a0 = rng.uniform(0, 2 * math.pi)
    pts = [(cx + r * rng.uniform(0.85, 1.1) * math.cos(a0 + k * 2 * math.pi / sides),
            cy + r * rng.uniform(0.85, 1.1) * math.sin(a0 + k * 2 * math.pi / sides)) for k in range(sides)]
    centre = (cx, cy, z / 2)
    f.out([(x, y, z) for x, y in pts], jitter(rgb, rng, 0.06), centre)
    for k in range(sides):
        (ax, ay), (bx, by) = pts[k], pts[(k + 1) % sides]
        f.out([(ax, ay, -0.01), (bx, by, -0.01), (bx, by, z), (ax, ay, z)], jitter(rgb, rng, 0.08), centre)


# ---- tile content ---------------------------------------------------------------------------------------------

def on_path(tile, x, y, margin=0.0):
    return tile.get("gate") and abs(x) < PATH_HALF + margin and -0.3 * S + 0.5 < y < S - ROOM / 2


def ground(f, tile, rng):
    grid = ground_grid()
    ks = [-1 + 2 * i / grid for i in range(grid + 1)]
    for i in range(grid):
        for j in range(grid):
            p = [(*chart_from_gnomonic(ks[a], ks[b]), 0.0) for a, b in [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]]
            tris = [(p[0], p[1], p[2]), (p[0], p[2], p[3])] if (i + j) % 2 == 0 else [(p[0], p[1], p[3]),
                                                                                     (p[1], p[2], p[3])]
            for t in tris:
                cx = sum(q[0] for q in t) / 3
                cy = sum(q[1] for q in t) / 3
                if on_path(tile, cx, cy):
                    base = col("path")
                elif tile["kind"] == "side" and abs(cy) < 0.16:
                    base = col("dirt")                # worn strip under the fence
                elif tile["kind"] == "home" or (tile["kind"] == "side" and cy > 0):
                    base = pick(tile["near"], rng)
                elif tile["kind"] == "side":          # gold; olive by the fence, deeper towards the antipode
                    t_far = -cy / (S / 2)
                    if t_far < 0.2 and rng.random() < 0.7:
                        base = col("grass_olive")
                    elif t_far > 0.72 and rng.random() < 0.7:
                        base = col("grass_deep")
                    else:
                        base = pick(tile["far"], rng)
                else:
                    base = pick(tile["far"], rng)
                f.face(t, jitter(_pc(tuple(c * tile["shade"] for c in base), base.idx), rng, 0.04))


def room_footprint(f):
    """Placeholder kerb where the scanned room's walls stand (wall to wall, ROOM square, centred on Home)."""
    h, w, z = ROOM / 2, 0.06, 0.02
    n = math.ceil(ROOM / 0.2)
    for k in range(n):
        a, b = -h + ROOM * k / n, -h + ROOM * (k + 1) / n
        for lo, hi in [((a, -h, 0), (b, -h + w, z)), ((a, h - w, 0), (b, h, z)),
                       ((-h, a, 0), (-h + w, b, z)), ((h - w, a, 0), (h, b, z))]:
            f.box(lo, hi, col("floor"), skip=("zmin",))


def _post(f, rng, x, top, p=0.04):
    f.box((x - p, -p, 0), (x + p, p, top), jitter(col("post"), rng), skip=("zmin",))
    c = [(x - p, -p, top), (x + p, -p, top), (x + p, p, top), (x - p, p, top)]
    apex = (x, 0, top + 0.1)
    for k in range(4):
        f.face([c[k], c[(k + 1) % 4], apex], jitter(col("post"), rng))


def fence(f, rng, tile):
    """Posts every ~FENCE_SPACING (even spacing also across tile edges), two rails in 0.2 m segments that bend with
    the warp; South gets a gate arch over the path."""
    half = S / 2
    gate = bool(tile.get("gate"))
    n_posts = max(2, round(S / FENCE_SPACING))
    step = S / n_posts
    for k in range(n_posts):
        x = -half + step * (k + 0.5)
        if gate and abs(x) < GATE_HALF + 0.12:
            continue
        _post(f, rng, x, 0.84)
    n = math.ceil(S / 0.2)
    gap = 0.0
    for z in (0.38, 0.7):
        for k in range(n):
            a, b = -half + S * k / n, -half + S * (k + 1) / n
            if gate and max(abs(a), abs(b)) <= GATE_HALF + 1e-6:
                gap = max(gap, abs(a), abs(b))
                continue
            f.box((a, -0.02, z), (b, 0.02, z + 0.06), jitter(col("rail"), rng, 0.05),
                  skip=("xmin", "xmax", "zmin"))
    if gate:
        gx = gap + 0.02
        for x in (-gx, gx):
            _post(f, rng, x, 1.16, p=0.055)
        m = 6
        for k in range(m):
            a, b = -gx - 0.16 + (2 * gx + 0.32) * k / m, -gx - 0.16 + (2 * gx + 0.32) * (k + 1) / m
            f.box((a, -0.05, 1.04), (b, 0.05, 1.13), jitter(col("rail"), rng, 0.05),
                  skip=() if k in (0, m - 1) else ("xmin", "xmax"))


def hero(f, rng, tile, cx, cy):
    kind = tile["hero"]
    if kind == "standing_stone":
        rock(f, rng, cx, cy, 0.24, 0.17, 0.66, col("slate"), moss=0.2, sink=0.1)
        for k in range(5):
            a = k * 2 * math.pi / 5 + rng.uniform(-0.3, 0.3)
            rock(f, rng, cx + 0.48 * math.cos(a), cy + 0.48 * math.sin(a), 0.1, 0.08, 0.07, col("slate"), sub=0)
    elif kind == "balanced_rocks":
        z = 0.0
        for size in (0.33, 0.26, 0.19, 0.13):
            z = rock(f, rng, cx + rng.uniform(-0.03, 0.03), cy + rng.uniform(-0.03, 0.03), size * 1.15, size,
                     size * 0.62, col("sandstone"), moss=0.15, sink=0.12 if z else 0.3, z0=z) - size * 0.12
    elif kind == "pines":
        pine(f, rng, cx, cy, 1.25)
        pine(f, rng, cx + 0.5, cy + 0.22, 0.9)
        pine(f, rng, cx - 0.38, cy + 0.36, 0.7)
    elif kind == "stone_circle":
        for k in range(7):
            a = k * 2 * math.pi / 7 + math.pi / 2 + math.pi / 7      # leave the gap facing the path (+y)
            rock(f, rng, cx + 0.55 * math.cos(a), cy + 0.55 * math.sin(a), 0.1, 0.08, 0.3, col("pale"),
                 moss=0.3, sub=0, sink=0.1)
        rock(f, rng, cx, cy, 0.22, 0.19, 0.06, col("pale"), moss=0.6, sub=1, sink=0.4)


def beacon(f, rng):
    z = 0.0
    for size in (0.36, 0.28, 0.22, 0.16, 0.12):
        z = rock(f, rng, rng.uniform(-0.03, 0.03), rng.uniform(-0.03, 0.03), size, size * 0.95, size * 0.5,
                 col("basalt"), moss=0.1, sink=0.15 if z else 0.3, z0=z) - size * 0.1
    # lantern: an emissive octahedron on a short post
    f.box((-0.02, -0.02, z), (0.02, 0.02, z + 0.12), col("post"), skip=("zmin",))
    c, r = Vector((0, 0, z + 0.24)), 0.1
    X, Y, Z = Vector((r, 0, 0)), Vector((0, r, 0)), Vector((0, 0, r * 1.3))
    for a, b, zz in [(X, Y, Z), (Y, -X, Z), (-X, -Y, Z), (-Y, X, Z), (Y, X, -Z), (-X, Y, -Z), (-Y, -X, -Z),
                     (X, -Y, -Z)]:
        f.face([c + a, c + b, c + zz], col("lantern"), mat=1)


def scatter(tile, rng, count, min_gap, taken, extra_ok=lambda x, y: True, k_max=0.9):
    """Rejection-sample chart positions on the tile, away from the room, the fence, the path and earlier picks."""
    out, tries = [], 0
    while len(out) < count and tries < count * 200:
        tries += 1
        x, y = chart_from_gnomonic(rng.uniform(-k_max, k_max), rng.uniform(-k_max, k_max))
        if in_room(tile["frame"], x, y) or not extra_ok(x, y) or on_path(tile, x, y, 0.12):
            continue
        if tile["kind"] == "side" and abs(y) < FENCE_CLEAR:
            continue
        if any(math.hypot(x - a, y - b) < max(min_gap, g) for a, b, g in taken):
            continue
        taken.append((x, y, min_gap))
        out.append((x, y))
    return out


def near(x, y):
    return y > 0


def far(x, y):
    return y < 0


def theme(f, rng, tile, taken, hx):
    """Bold far-half props that show the spherical stretch: each tile has its own silhouette."""
    kind = tile["theme"]
    away = lambda x, y: y < -0.15 and x * hx <= 0.1             # on the far half, the other side from the hero
    if kind == "sunflowers":
        for x, y in scatter(tile, rng, 7, 0.3, taken, away):
            sunflower(f, rng, x, y)
    elif kind == "lavender":
        sign = -1 if hx > 0 else 1
        for row in range(2):
            y = -0.13 * S - row * 0.34
            for k in range(int((0.4 * S - 0.15) / 0.3) + 1):
                x = sign * (0.15 + k * 0.3) + rng.uniform(-0.03, 0.03)
                if any(math.hypot(x - a, y - b) < g for a, b, g in taken):
                    continue
                lavender(f, rng, x, y)
                taken.append((x, y, 0.2))
    elif kind == "birches":
        for x, y in scatter(tile, rng, 3, 0.5, taken, away):
            birch(f, rng, x, y, rng.uniform(0.85, 1.0))
    elif kind == "mushrooms":
        for x, y in scatter(tile, rng, 5, 0.5, taken, lambda x, y: y < -0.2):
            mushroom(f, rng, x, y, rng.uniform(0.95, 1.15))


def props(f, tile, rng):
    taken = []
    if tile["kind"] == "beacon":
        beacon(f, rng)
        taken.append((0.0, 0.0, 0.55))
        flowers = [tt["flower"] for tt in TILE_DEFS if tt.get("flower")]
        visible = lambda x, y: math.hypot(x, y) > (math.pi - DISCARD) * R + 0.2   # outside the discarded cap
        for x, y in scatter(tile, rng, 4, 0.9, taken, visible):
            haystack(f, rng, x, y, rng.uniform(1.0, 1.1))
        for x, y in scatter(tile, rng, 5, 0.35, taken):
            s = rng.uniform(0.07, 0.16)
            rock(f, rng, x, y, s * rng.uniform(1.0, 1.4), s, s * rng.uniform(0.5, 0.8),
                 col(rng.choice(["rock", "rock2"])), sub=0)
        for x, y in scatter(tile, rng, 25, 0.14, taken):
            tuft(f, rng, x, y, col("blade_far"))
        for x, y in scatter(tile, rng, 22, 0.12, taken):
            flower(f, rng, x, y, col(rng.choice(flowers)))
        return

    # far half: hero landmark off the tile centre (tall objects stay away from the antipode), then the theme props
    hx = 0.0 if tile.get("gate") else rng.choice([-1, 1]) * rng.uniform(0.12, 0.2) * S
    hy = -0.3 * S
    hero(f, rng, tile, hx, hy)
    taken.append((hx, hy, 0.75))
    theme(f, rng, tile, taken, hx if hx else 1.0)
    for x, y in scatter(tile, rng, 2, 0.6, taken, lambda x, y: y < -0.4 * S + 0.3 and y > -0.47 * S):
        haystack(f, rng, x, y, rng.uniform(0.9, 1.15))
    for x, y in scatter(tile, rng, 1, 0.5, taken, lambda x, y: -0.25 * S < y < -0.125 * S):
        bale(f, rng, x, y)
    if tile.get("gate"):                                          # stepping stones from the room to the circle
        start = min(S / 2, S - ROOM / 2 - 0.15)                    # just outside the room wall (y = S - ROOM/2)
        y = start
        while y > hy + 0.62:
            if abs(y) > 0.12:
                slab(f, rng, rng.uniform(-0.05, 0.05), y, rng.uniform(0.1, 0.13), col("stepping"))
            y -= rng.uniform(0.3, 0.36)
        for x in (-1, 1):                                         # poppies line the path
            yy = start - 0.2
            while yy > hy + 0.7:
                if abs(yy) > 0.35:
                    flower(f, rng, x * (PATH_HALF + 0.08) + rng.uniform(-0.03, 0.03), yy, col(tile["flower"]),
                           rmax=0.075)
                yy -= rng.uniform(0.22, 0.32)

    # near half: low bushes, small rocks, green tufts, mixed bright flowers
    for x, y in scatter(tile, rng, 2, 0.55, taken, lambda x, y: y > 0.35):
        bush(f, rng, x, y, rng.uniform(0.17, 0.26), blossom=col(rng.choice([tile["flower"], "pink", "white"])))
    for x, y in scatter(tile, rng, 3, 0.35, taken, near):
        s = rng.uniform(0.06, 0.13)
        rock(f, rng, x, y, s * rng.uniform(1.0, 1.4), s, s * rng.uniform(0.5, 0.8),
             col(rng.choice(["rock", "rock2"])), sub=0)
    for x, y in scatter(tile, rng, 3, 0.35, taken, far):
        s = rng.uniform(0.08, 0.16)
        rock(f, rng, x, y, s * rng.uniform(1.0, 1.4), s, s * rng.uniform(0.5, 0.8),
             col(rng.choice(["rock", "rock2"])), sub=0)
    for x, y in scatter(tile, rng, 24, 0.14, taken, near):
        tuft(f, rng, x, y, col("blade_near"))
    for x, y in scatter(tile, rng, 16, 0.16, taken, far):
        tuft(f, rng, x, y, col("blade_far"))
    near_flowers = [tile["flower"], tile["flower"], "pink", "blue", "white"]
    for x, y in scatter(tile, rng, 18, 0.12, taken, near):
        flower(f, rng, x, y, col(rng.choice(near_flowers)))
    for x, y in scatter(tile, rng, 14, 0.12, taken, far):
        flower(f, rng, x, y, col(tile["flower"]), hmax=0.42, rmax=0.09)


# ---- Blender objects ------------------------------------------------------------------------------------------

def _collection(name):
    scene = bpy.context.scene
    c = bpy.data.collections.get(name) or bpy.data.collections.new(name)
    if c.name not in scene.collection.children:
        scene.collection.children.link(c)
    for o in list(c.objects):
        data = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if data is not None and data.users == 0:
            if isinstance(data, bpy.types.Mesh):
                bpy.data.meshes.remove(data)
            elif isinstance(data, bpy.types.Curve):
                bpy.data.curves.remove(data)
    return c


def _materials():
    m = bpy.data.materials.get("SP_VertexColor") or bpy.data.materials.new("SP_VertexColor")
    m.use_nodes = True
    m.use_backface_culling = True            # as in Unity: a wrongly wound face shows up as a hole in the previews
    nt = m.node_tree
    nt.nodes.clear()
    attr = nt.nodes.new("ShaderNodeAttribute")
    attr.attribute_type, attr.attribute_name = "GEOMETRY", "Col"
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = 0.9
    # Walker preview only: the baked 'Fog' attribute (0 where absent) blends to an unlit sky colour
    fog = nt.nodes.new("ShaderNodeAttribute")
    fog.attribute_type, fog.attribute_name = "GEOMETRY", "Fog"
    sky = nt.nodes.new("ShaderNodeEmission")
    sky.inputs["Color"].default_value = (*hexcol(SKY), 1.0)
    mixer = nt.nodes.new("ShaderNodeMixShader")
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(fog.outputs["Fac"], mixer.inputs[0])
    nt.links.new(bsdf.outputs["BSDF"], mixer.inputs[1])
    nt.links.new(sky.outputs["Emission"], mixer.inputs[2])
    nt.links.new(mixer.outputs["Shader"], out.inputs["Surface"])

    lamp = bpy.data.materials.get("SP_Lantern") or bpy.data.materials.new("SP_Lantern")
    lamp.use_nodes = True
    nt = lamp.node_tree
    nt.nodes.clear()
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Color"].default_value = (*col("lantern"), 1.0)
    em.inputs["Strength"].default_value = 8.0
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
    return [m, lamp]


def _mesh_object(name, collection, faces, mapper, materials, fog=None, keep=None):
    bm = bmesh.new()
    layer = bm.loops.layers.float_color.new("Col")
    fog_layer = bm.loops.layers.float.new("Fog") if fog else None
    pal_layer = bm.faces.layers.int.new("Pal")            # AtlasPalette cell index of the face
    for pts, rgb, mat in faces.items:
        if keep is not None and not keep(pts):
            continue
        verts = [bm.verts.new(mapper(p)) for p in pts]
        face = bm.faces.new(verts)
        face.material_index = mat
        face[pal_layer] = getattr(rgb, "idx", -1)
        for loop, p in zip(face.loops, pts):
            loop[layer] = (*rgb, 1.0)
            if fog_layer is not None:
                loop[fog_layer] = fog(p)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    mesh.color_attributes.active_color_name = "Col"
    for m in materials:
        mesh.materials.append(m)
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    return obj


def _label(text, collection, location, size=0.24):
    curve = bpy.data.curves.new("Label_" + text, "FONT")
    curve.body, curve.size, curve.align_x = text, size * S / S_LOOP, "CENTER"
    obj = bpy.data.objects.new("Label_" + text.split()[0], curve)
    obj.location = location
    collection.objects.link(obj)
    return obj


BUILT = {}                   # tile name -> (frame, merged Faces), filled by build_all for build_walker


def build_walker(fog_start=PUSHED_FOG[0], fog_end=PUSHED_FOG[1]):
    """Rebuild the walker preview for a walker at the room centre, with fog between the two angles (radians)."""
    walker_c = _collection(WALKER)
    materials = _materials()
    home_n = HOME_FRAME[0]

    def angle(frame, p):
        return math.acos(max(-1.0, min(1.0, to_unit(frame, p[0], p[1]).dot(home_n))))

    def fog(frame):
        def f(p):
            t = (angle(frame, p) - fog_start) / max(1e-6, fog_end - fog_start)
            t = max(0.0, min(1.0, t))
            return t * t * (3 - 2 * t)
        return f

    def warp(frame):
        def m(p):
            x, y = to_chart(HOME_FRAME, to_unit(frame, p[0], p[1]))
            return Vector((x, y, p[2]))
        return m

    for name, (frame, faces) in BUILT.items():
        o = _mesh_object("Walker_" + name, walker_c, faces, warp(frame), materials, fog=fog(frame),
                         keep=lambda pts, fr=frame: all(angle(fr, p) <= DISCARD for p in pts))
        o.location = WALKER_OFFSET
    return {o.name: len(o.data.polygons) for o in walker_c.objects}


def _setup_scene():
    scene = bpy.context.scene
    for engine in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            pass
    scene.render.resolution_x, scene.render.resolution_y = 1600, 1000
    scene.view_settings.view_transform = "Standard"     # palette colours as authored; fog matches the sky
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (*hexcol(SKY), 1.0)
    bg.inputs["Strength"].default_value = 1.0

    def obj(name, data):
        o = bpy.data.objects.get(name)
        if o is None:
            o = bpy.data.objects.new(name, data)
            scene.collection.objects.link(o)
        return o

    sun = obj("SP_Sun", bpy.data.lights.get("SP_Sun") or bpy.data.lights.new("SP_Sun", "SUN"))
    sun.data.energy, sun.data.angle = 3.5, math.radians(8)
    sun.rotation_euler = (math.radians(50), 0, math.radians(35))

    k = S / S_LOOP
    cam = obj("Cam_Globe", bpy.data.cameras.get("Cam_Globe") or bpy.data.cameras.new("Cam_Globe"))
    cam.data.lens = 50
    cam.location = GLOBE_OFFSET + Vector((5.0, -6.2, 3.4)) * k
    cam.rotation_euler = (GLOBE_OFFSET + Vector((0, 0, -0.15 * k)) - cam.location).to_track_quat("-Z", "Y").to_euler()

    top = obj("Cam_Net", bpy.data.cameras.get("Cam_Net") or bpy.data.cameras.new("Cam_Net"))
    pitch = S + NET_GAP
    top.data.type, top.data.ortho_scale = "ORTHO", 18.0 * pitch / 2.7
    top.location, top.rotation_euler = (0, -0.5 * pitch, 12), (0, 0, 0)

    # eye height 1.6 m, standing at the room centre, looking south over the fence; ~90 deg like a phone headset
    eye = obj("Cam_Walker", bpy.data.cameras.get("Cam_Walker") or bpy.data.cameras.new("Cam_Walker"))
    eye.data.lens, eye.data.sensor_width, eye.data.clip_start = 18, 36, 0.05
    eye.data.clip_end = 100.0
    eye.location = WALKER_OFFSET + Vector((0, 0.3, 1.6))
    eye.rotation_euler = (math.radians(70), 0, math.radians(180))
    # the same eye looking nearly level: south over the gate, and east to the sunflowers and standing stone
    for name, yaw in (("Cam_Walker_Level", 180), ("Cam_Walker_East", -90)):
        c = obj(name, bpy.data.cameras.get(name) or bpy.data.cameras.new(name))
        c.data.lens, c.data.sensor_width, c.data.clip_start, c.data.clip_end = 18, 36, 0.05, 100.0
        c.location = eye.location.copy()
        c.rotation_euler = (math.radians(82), 0, math.radians(yaw))
    scene.camera = eye


def render_previews(folder, tag=None, cams=("Cam_Walker", "Cam_Walker_Level", "Cam_Walker_East", "Cam_Globe",
                                            "Cam_Net"), percent=60):
    """Render the preview cameras to <folder>/sp_<view>_<tag>.png (tag defaults to the scale, e.g. scale1p5).
    Walker views hide the net and globe, which otherwise show up on the horizon."""
    import os
    scene = bpy.context.scene
    tag = tag or "scale" + f"{S / S_LOOP:.1f}".replace(".", "p")
    names = dict(Cam_Walker="walker", Cam_Walker_Level="walker_level", Cam_Walker_East="walker_east",
                 Cam_Globe="globe", Cam_Net="net")
    shows = {"Cam_Globe": {GLOBE}, "Cam_Net": {TILES, LABELS}}
    old_cam, old_pct = scene.camera, scene.render.resolution_percentage
    scene.render.resolution_percentage = percent
    out = []
    for cam in cams:
        visible = shows.get(cam, {WALKER})
        for name in (TILES, GLOBE, LABELS, WALKER):
            bpy.data.collections[name].hide_render = name not in visible
        scene.camera = bpy.data.objects[cam]
        scene.render.filepath = os.path.join(folder, f"sp_{names.get(cam, cam)}_{tag}.png")
        bpy.ops.render.render(write_still=True)
        out.append(scene.render.filepath)
    for name in (TILES, GLOBE, LABELS, WALKER):
        bpy.data.collections[name].hide_render = False
    scene.camera, scene.render.resolution_percentage = old_cam, old_pct
    return out


def build_all(planet_scale=None):
    """Rebuild everything. planet_scale overrides PLANET_SCALE for this build (1.0 = the loop-fit planet)."""
    set_scale(PLANET_SCALE if planet_scale is None else planet_scale)
    BUILT.clear()
    materials = _materials()
    tiles_c, globe_c, labels_c = _collection(TILES), _collection(GLOBE), _collection(LABELS)
    pitch = S + NET_GAP
    stats = {}
    bpy.context.scene["SmallPlanet_R"], bpy.context.scene["SmallPlanet_tile_side"] = R, S
    checks = dict(max_prop_edge=0.0, max_prop_edge_at="", max_prop_height=0.0, cells=set(), over=0)
    for tile in TILE_DEFS:
        rng = random.Random(f"{SEED}-{tile['name']}")
        parts = {"Ground": Faces(), "Props": Faces(), "Fence": Faces()}
        ground(parts["Ground"], tile, rng)
        if tile["kind"] == "home":
            room_footprint(parts["Props"])
        else:
            props(parts["Props"], tile, rng)
        if tile["kind"] == "side":
            fence(parts["Fence"], rng, tile)

        gx, gy, rot = tile["net"]
        empty = bpy.data.objects.new("Tile_" + tile["name"], None)
        empty.empty_display_type, empty.empty_display_size = "PLAIN_AXES", 0.3
        empty.location = (gx * pitch, gy * pitch, 0)
        empty.rotation_euler = (0, 0, math.radians(rot))
        empty["planet_R"], empty["tile_side"] = R, S        # authoring scale, also carried as glTF extras
        tiles_c.objects.link(empty)

        merged = Faces()
        for part, faces in parts.items():
            if not faces.items:
                continue
            o = _mesh_object(f"Tile_{tile['name']}_{part}", tiles_c, faces, lambda p: p, materials)
            o.parent, o.matrix_parent_inverse = empty, Matrix.Identity(4)
            merged.items += faces.items
        g = _mesh_object("Globe_" + tile["name"], globe_c, merged, lambda p, fr=tile["frame"]: to_globe(fr, p),
                         materials)
        g.location = GLOBE_OFFSET
        BUILT[tile["name"]] = (tile["frame"], merged)
        stats[tile["name"]] = merged.tris()
        for part in ("Props", "Fence"):
            for pts, rgb, _ in parts[part].items:
                # widest vertex pair in plan, so it covers whatever triangulation the importer picks
                edge = max(math.hypot(a[0] - b[0], a[1] - b[1]) for i, a in enumerate(pts) for b in pts[i + 1:])
                checks["over"] += edge > PROP_EDGE + 1e-6
                if edge > checks["max_prop_edge"]:
                    checks["max_prop_edge"], checks["max_prop_edge_at"] = edge, f"{tile['name']} {part} {pts[0]}"
                checks["max_prop_height"] = max(checks["max_prop_height"], max(p[2] for p in pts))
        for faces in parts.values():
            checks["cells"].update(getattr(rgb, "idx", -1) for _, rgb, _ in faces.items)

        text = {"Home": "HOME (your room)", "Beacon": "BEACON (antipode)"}.get(tile["name"], tile["name"].upper())
        _label(text, labels_c, (gx * pitch, gy * pitch - S / 2 + 0.05, 1.4))

    build_walker()
    _setup_scene()
    for area in bpy.context.screen.areas if bpy.context.screen else []:
        if area.type == "VIEW_3D":
            area.spaces.active.shading.type = "MATERIAL"
    stats["total"] = sum(stats.values())
    stats["s"] = round(S, 4)
    stats["R"] = round(R, 4)
    stats["corner_to_fence"] = round(S - ROOM / 2 * math.sqrt(2), 3)
    stats["wall_to_fence"] = round(S - ROOM / 2, 3)
    stats["grid"] = ground_grid()
    stats["max_prop_edge"] = round(checks["max_prop_edge"], 3)
    stats["max_prop_edge_at"] = checks["max_prop_edge_at"]
    stats["faces_over_prop_edge"] = checks["over"]
    stats["max_prop_height"] = round(checks["max_prop_height"], 3)
    stats["cells"] = sorted(checks["cells"])
    return stats
