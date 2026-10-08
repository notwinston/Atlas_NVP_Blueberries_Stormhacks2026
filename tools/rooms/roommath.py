#!/usr/bin/env python3
"""Reference maths for the ATLAS RoomModel (docs/contracts/lane-c-room.md).

An independent implementation of the FloorScan grid pipeline on synthetic rooms:
the 5 cm occupancy grid, the signed distance field, the walkable region, the free
rectangle, the loop-fit rule and both cell-grid rules (Master Plan sections 4.2
to 4.4 and 4.7, Appendix A.4). The C# core in Atlas.Scan must reproduce these
numbers; the test FloorScan_Synthetic compares against them.

Python 3 standard library only. Run it to print the reference tables:

    python3 roommath.py
"""

import base64
import hashlib
import math
import struct

# --------------------------------------------------------------------------------------
# Constants (every one is named on the contract page)
# --------------------------------------------------------------------------------------

CELL = 0.05                 # m, grid cell (section 4.2)
GRID_MARGIN_CELLS = 5       # the grid reaches 0.25 m beyond the outline's bounding box
OBST_MIN_HEIGHT = 0.10      # m above the floor: lower things are not obstacles
OBST_MAX_HEIGHT = 2.00      # m above the floor: higher things are not obstacles
WALK_SDF = 0.25             # m: WALK = {sdf > 0.25}, which is 0.30 m from a wall
BODY_CLEARANCE = 0.30       # m: what WALK_SDF means in true distance
PINHOLE_MAX_CELLS = 4       # unseen specks of at most this many cells are closed
OVERLAP_TOLERANCE = 1e-4    # m: geometry nearer than this to a cell edge does not reach into the cell

LAP_INSET = 0.40            # m: s = min(Wf, Df) - 0.40
LAP_MIN = 1.0               # m
LAP_MAX = 3.2               # m
STANDING_BELOW = 1.5        # m: standing mode when min(Wf, Df) < 1.5
RECT_SIDE_CAP = LAP_MAX + LAP_INSET   # 3.6 m: a longer short side gains nothing

K5 = 1.0612750619050357      # centre spacing of {4,5}: 2 asinh(sqrt(cos 72 deg)) = acosh(golden ratio)
K6 = 1.3169578969248166      # centre spacing of {4,6}: 2 asinh(sqrt(cos 60 deg)) = acosh(2)
FLOOR_L = 1.4               # m, provisional comfort floor for L (section 4.4)
FLOOR_R = 1.2               # m, provisional comfort floor for R

GLUED_TARGET = 1.5          # m, Glued cell target size (section 4.3)
ALIGNED_TARGET = 1.5        # m, Aligned Cells target size (section 4.7)
ALIGNED_MIN_N = 3
ALIGNED_MIN_G = 0.75        # m: below this the grid falls back to 2 x 2

MAX_MESHED_OBJECTS = 32     # Mesh Design section 5.4
FURNITURE_TRIANGLES = 120000
TRI_TARGET_MIN = 3000
TRI_TARGET_MAX = 15000
LIGHT_TRIANGLES = 60000
LIGHT_TARGET_MIN = 1500
LIGHT_TARGET_MAX = 5000
STANDIN_TRIANGLES = 5000    # the stand-in ellipsoid, and the target of every object of an all-stand-in room
PROFILE_BYTES = 256         # R21

SHELL_PITCH = 0.25          # m, Home shell grid (section 8.4)
SHELL_MAX_TRIANGLES = 3000
STANDIN_SEGMENTS = 50       # stand-in ellipsoid: 2 * 50 * (51 - 1) = 5,000 triangles
STANDIN_BANDS = 51
MAX_EDGE = 0.25             # m, longest edge of furniture and stand-ins

CATEGORIES = [
    "unknown", "couch", "table", "bed", "lamp", "plant", "screen", "storage", "bathtub",
    "chair", "dishwasher", "fireplace", "oven", "refrigerator", "sink", "stairs", "stove",
    "toilet", "washerDryer", "keyboard", "mouse", "laptop", "other",
]
PALETTE_CATEGORY_BASE = 32  # palette cell of category c is 32 + c (lane A's palette layout)
MESH_STATES = ["placeholder", "requested", "ready", "builtIn", "standIn", "matched", "scanCut"]
OPENING_KINDS = ["door", "window", "opening"]


# --------------------------------------------------------------------------------------
# Loop-fit and cell grids
# --------------------------------------------------------------------------------------

def round_half_up(x):
    """Rounding used everywhere in the contract: floor(x + 0.5)."""
    return int(math.floor(x + 0.5))


def lap_side(wf, df):
    """s = clamp(min(Wf, Df) - 0.4, 1.0, 3.2), metres (section 4.4)."""
    return min(max(min(wf, df) - LAP_INSET, LAP_MIN), LAP_MAX)


def loop_fit(wf, df):
    """Lap square and curvature radii from the free rectangle."""
    s = lap_side(wf, df)
    return {"s": s, "l5": s / K5, "l6": s / K6, "r3": 2.0 * s / math.pi}


def is_standing(wf, df):
    return min(wf, df) < STANDING_BELOW - 1e-9


def dial_radius(q, lf, strong=False):
    """Radius the dial uses for q = 3, 5, 6: the loop-fit value, raised to the comfort floor."""
    if q == 3:
        return lf["r3"] if strong else max(lf["r3"], FLOOR_R)
    value = lf["l5"] if q == 5 else lf["l6"]
    return value if strong else max(value, FLOOR_L)


def glued_cells(wf, df):
    """Glued cell (S1): n = max(1, round(side / 1.5)), cell = side / n, per side."""
    nx = max(1, round_half_up(wf / GLUED_TARGET))
    nz = max(1, round_half_up(df / GLUED_TARGET))
    return {"nx": nx, "nz": nz, "sizeX": wf / nx, "sizeZ": df / nz}


def aligned_cells(wf, df):
    """Aligned Cells grid (S3): n cells along the short side, m along the long side, side g."""
    short, long_ = min(wf, df), max(wf, df)
    n = max(ALIGNED_MIN_N, int(math.ceil(short / ALIGNED_TARGET - 1e-9)))
    g = short / n
    m = int(math.floor(long_ / g + 1e-6))
    if g < ALIGNED_MIN_G - 1e-9:
        n, g, m = 2, short / 2.0, 2          # corner demo: five tiles round one post
    return {"n": n, "m": m, "g": g}


def tri_target(meshed_count):
    """The full target when `meshed_count` objects of equal surface area receive meshes."""
    if meshed_count <= 0:
        return 0
    return min(max(FURNITURE_TRIANGLES // meshed_count, TRI_TARGET_MIN), TRI_TARGET_MAX)


def light_target(meshed_count):
    """The room's light target: clamp(60000 // n, 1500, 5000); 0 when nothing is meshed."""
    if meshed_count <= 0:
        return 0
    return min(max(LIGHT_TRIANGLES // meshed_count, LIGHT_TARGET_MIN), LIGHT_TARGET_MAX)


def mm(v):
    """Metres to whole millimetres."""
    return int(math.floor(v * 1000 + 0.5))


def assign_tri_targets(objects):
    """The rule of Mesh Design section 5.4, in whole numbers. The 32 largest boxes (volume in whole cubic
    millimetres, ties by lower id) share 120,000 triangles by surface area, 3,000..15,000 each; the rest get 0.
    Returns {id: full target}."""
    targets, _ = plan_tri_targets(objects)
    return targets


def plan_tri_targets(objects):
    """(full target per id, the room's light target) for objects with "id" and "size" (w, h, d) in metres."""
    order = sorted(objects, key=lambda o: (-(mm(o["size"][0]) * mm(o["size"][1]) * mm(o["size"][2])), o["id"]))
    meshed = order[:MAX_MESHED_OBJECTS]
    n = len(meshed)
    targets = {o["id"]: 0 for o in objects}
    if n == 0:
        return targets, 0
    area = {}
    for o in meshed:
        w, h, d = mm(o["size"][0]), mm(o["size"][1]), mm(o["size"][2])
        area[o["id"]] = 2 * (w * h + h * d + w * d)
    total = sum(area.values())
    t = {i: min(max((FURNITURE_TRIANGLES * a) // total, TRI_TARGET_MIN), TRI_TARGET_MAX) for i, a in area.items()}
    if sum(t.values()) > FURNITURE_TRIANGLES:
        extra = sum(v - TRI_TARGET_MIN for v in t.values())
        budget = FURNITURE_TRIANGLES - TRI_TARGET_MIN * n
        t = {i: TRI_TARGET_MIN + ((v - TRI_TARGET_MIN) * budget) // extra for i, v in t.items()}
    targets.update(t)
    return targets, light_target(n)


def tri_rule_vectors():
    """The test vectors of the brief: (name, [(id, (w, h, d))], full targets in id order, light target)."""
    boxes40 = [(i, (0.3 + 0.05 * i, 0.5, 0.4)) for i in range(1, 41)]
    mid = [3000] * 9 + [3044, 3134, 3226, 3316, 3407, 3498, 3589, 3679, 3770, 3861, 3952, 4043, 4134, 4225,
                        4315, 4407, 4497, 4588, 4679, 4770, 4861, 4952, 5043]
    return [
        ("five cubes", [(i, (1.0, 1.0, 1.0)) for i in range(1, 6)], [15000] * 5, 5000),
        ("three mixed", [(1, (2.0, 0.8, 0.9)), (2, (0.5, 0.9, 0.5)), (3, (1.2, 0.75, 0.6))], [15000] * 3, 5000),
        ("eight and 24", [(i, (1.5, 1.0, 1.0)) for i in range(1, 9)] + [(i, (0.2, 0.2, 0.2)) for i in range(9, 33)],
         [6000] * 8 + [3000] * 24, 1875),
        ("forty ramp", boxes40, [0] * 8 + mid, 1875),
        ("one", [(7, (0.6, 0.45, 0.6))], [15000], 5000),
        ("twelve", [(i, (1.8, 0.8, 0.9)) for i in range(1, 13)], [10000] * 12, 5000),
    ]


def check_tri_rule():
    """True when plan_tri_targets reproduces every vector of tri_rule_vectors."""
    ok = True
    for name, boxes, full, light in tri_rule_vectors():
        targets, got_light = plan_tri_targets([{"id": i, "size": s} for i, s in boxes])
        got = [targets[i] for i, _ in boxes]
        same = got == full and got_light == light
        ok = ok and same
        print("  %-14s n = %2d  sum = %6d  light = %4d  %s" % (name, len(boxes), sum(got), got_light,
                                                             "ok" if same else "MISMATCH"))
    return ok


# --------------------------------------------------------------------------------------
# Small geometry helpers. Floor points are (x, z). Yaw follows Unity: a rotation about +Y
# by `yaw` carries local +X to (cos, -sin) and local +Z to (sin, cos) in (x, z).
# --------------------------------------------------------------------------------------

def rot_y(yaw, lx, lz):
    c, s = math.cos(yaw), math.sin(yaw)
    return (c * lx + s * lz, -s * lx + c * lz)


def wrap_angle(a):
    """Into (-pi, pi]."""
    a = math.fmod(a, 2.0 * math.pi)
    if a <= -math.pi:
        a += 2.0 * math.pi
    elif a > math.pi:
        a -= 2.0 * math.pi
    return a


def polygon_area(poly):
    """Signed area; positive when counter-clockwise with +X right and +Z up the page."""
    total = 0.0
    for k in range(len(poly)):
        x0, z0 = poly[k]
        x1, z1 = poly[(k + 1) % len(poly)]
        total += x0 * z1 - x1 * z0
    return 0.5 * total


def point_in_polygon(x, z, poly):
    """Even-odd rule. Points exactly on an edge are not specified; cell centres never are."""
    inside = False
    n = len(poly)
    for k in range(n):
        x0, z0 = poly[k]
        x1, z1 = poly[(k + 1) % n]
        if (z0 > z) != (z1 > z):
            if x < x0 + (z - z0) * (x1 - x0) / (z1 - z0):
                inside = not inside
    return inside


def point_segment_distance(px, pz, ax, az, bx, bz):
    dx, dz = bx - ax, bz - az
    length2 = dx * dx + dz * dz
    t = 0.0 if length2 == 0.0 else max(0.0, min(1.0, ((px - ax) * dx + (pz - az) * dz) / length2))
    return math.hypot(px - (ax + t * dx), pz - (az + t * dz))


def segments_cross(p, q, r, s):
    """True when the open segments pq and rs cross at one interior point."""
    def orient(a, b, c):
        return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
    d1, d2 = orient(r, s, p), orient(r, s, q)
    d3, d4 = orient(p, q, r), orient(p, q, s)
    return (d1 * d2 < -1e-12) and (d3 * d4 < -1e-12)


def polygon_is_simple(poly):
    n = len(poly)
    for a in range(n):
        for b in range(a + 1, n):
            if b == a or (b + 1) % n == a or (a + 1) % n == b:
                continue
            if segments_cross(poly[a], poly[(a + 1) % n], poly[b], poly[(b + 1) % n]):
                return False
    return True


def dominant_axis(directions):
    """Dominant wall direction modulo 90 degrees, in (-45, 45] degrees, as radians.

    directions is a list of (dx, dz, weight): wall directions (or wall normals, which differ
    by 90 degrees and so give the same answer) weighted by wall length. The result maximises
    the sum of weight * cos(4 * (angle - result))."""
    sin4 = cos4 = 0.0
    for (dx, dz, weight) in directions:
        a = math.atan2(dz, dx)
        sin4 += weight * math.sin(4.0 * a)
        cos4 += weight * math.cos(4.0 * a)
    if abs(sin4) < 1e-9 and abs(cos4) < 1e-9:
        return 0.0
    theta = 0.25 * math.atan2(sin4, cos4)
    return 0.0 if abs(theta) < 1e-9 else theta


def outline_axis(outline):
    """Dominant wall direction of an outline, from its edges weighted by length."""
    edges = []
    for k in range(len(outline)):
        (x0, z0), (x1, z1) = outline[k], outline[(k + 1) % len(outline)]
        edges.append((x1 - x0, z1 - z0, math.hypot(x1 - x0, z1 - z0)))
    return dominant_axis(edges)


class Box:
    """An oriented box: centre of its base, full size (w along local X, h up, d along local Z)."""

    def __init__(self, base, size, yaw=0.0):
        self.base = tuple(float(v) for v in base)      # (x, y, z)
        self.size = tuple(float(v) for v in size)      # (w, h, d)
        self.yaw = float(yaw)

    def blocks_floor(self):
        """True when some part lies between 0.10 m and 2.0 m above the floor."""
        bottom, top = self.base[1], self.base[1] + self.size[1]
        return top > OBST_MIN_HEIGHT and bottom < OBST_MAX_HEIGHT

    def overlaps_cell(self, cx, cz, tol=OVERLAP_TOLERANCE):
        """Separating-axis test of the footprint against the cell square centred on (cx, cz).

        Touching does not count: the overlap must exceed `tol` on all four axes."""
        hx, hz = 0.5 * self.size[0], 0.5 * self.size[2]
        hc = 0.5 * CELL
        dx, dz = cx - self.base[0], cz - self.base[2]
        ux = rot_y(self.yaw, 1.0, 0.0)     # box local X in room coordinates
        uz = rot_y(self.yaw, 0.0, 1.0)     # box local Z
        # world axes: box radius along X and Z
        rx = hx * abs(ux[0]) + hz * abs(uz[0])
        rz = hx * abs(ux[1]) + hz * abs(uz[1])
        if abs(dx) >= rx + hc - tol or abs(dz) >= rz + hc - tol:
            return False
        # box axes: cell radius along each
        for axis, half in ((ux, hx), (uz, hz)):
            dist = abs(dx * axis[0] + dz * axis[1])
            radius = hc * (abs(axis[0]) + abs(axis[1]))
            if dist >= half + radius - tol:
                return False
        return True


# --------------------------------------------------------------------------------------
# The grid. Cell (i, j) covers x in [x0 + i c, x0 + (i + 1) c), z likewise; data is
# row-major with row 0 at the lowest z: index = j * width + i.
# --------------------------------------------------------------------------------------

class Grid:
    def __init__(self, ix0, jz0, width, height, base_x=0.0, base_z=0.0):
        self.ix0 = ix0              # low-x edge, in half cells from the base: x0 = base_x + ix0 * CELL / 2
        self.jz0 = jz0              # (half cells, so that a rectangle centre can become the origin)
        self.width = width
        self.height = height
        self.base_x = base_x        # where the lattice starts; 0 for a grid in the room frame
        self.base_z = base_z

    @property
    def origin_x(self):
        return self.base_x + self.ix0 * CELL * 0.5

    @property
    def origin_z(self):
        return self.base_z + self.jz0 * CELL * 0.5

    def centre(self, i, j):
        return (self.origin_x + (i + 0.5) * CELL, self.origin_z + (j + 0.5) * CELL)

    def cell_of(self, x, z):
        return (int(math.floor((x - self.origin_x) / CELL)), int(math.floor((z - self.origin_z) / CELL)))

    def contains(self, i, j):
        return 0 <= i < self.width and 0 <= j < self.height

    def new_mask(self, value=0):
        return [bytearray([value]) * self.width for _ in range(self.height)]


def grid_for_outline(outline):
    """Grid whose 5 cm lattice starts at the low corner of the outline's bounding box and which
    reaches 5 cells beyond the box on every side. Anchoring the lattice to the footprint makes
    the cells the same wherever the room lies in the frame it is described in."""
    xs = [p[0] for p in outline]
    zs = [p[1] for p in outline]
    width = int(math.ceil((max(xs) - min(xs)) / CELL - 1e-3)) + 2 * GRID_MARGIN_CELLS
    height = int(math.ceil((max(zs) - min(zs)) / CELL - 1e-3)) + 2 * GRID_MARGIN_CELLS
    return Grid(-2 * GRID_MARGIN_CELLS, -2 * GRID_MARGIN_CELLS, width, height, min(xs), min(zs))


def rasterise_inside(grid, outline):
    mask = grid.new_mask()
    for j in range(grid.height):
        for i in range(grid.width):
            x, z = grid.centre(i, j)
            if point_in_polygon(x, z, outline):
                mask[j][i] = 1
    return mask


def rasterise_boxes(grid, boxes):
    mask = grid.new_mask()
    for box in boxes:
        if not box.blocks_floor():
            continue
        reach = 0.5 * math.hypot(box.size[0], box.size[2]) + CELL
        i_lo, j_lo = grid.cell_of(box.base[0] - reach, box.base[2] - reach)
        i_hi, j_hi = grid.cell_of(box.base[0] + reach, box.base[2] + reach)
        for j in range(max(0, j_lo), min(grid.height, j_hi + 1)):
            for i in range(max(0, i_lo), min(grid.width, i_hi + 1)):
                x, z = grid.centre(i, j)
                if box.overlaps_cell(x, z):
                    mask[j][i] = 1
    return mask


def dilate8(grid, mask):
    """Grow by one cell with the 3 x 3 square (the cell and its eight neighbours)."""
    out = grid.new_mask()
    for j in range(grid.height):
        for i in range(grid.width):
            if mask[j][i]:
                for dj in (-1, 0, 1):
                    for di in (-1, 0, 1):
                        if grid.contains(i + di, j + dj):
                            out[j + dj][i + di] = 1
    return out


def components4(grid, mask):
    """4-connected components of the set cells; returns a list of lists of (i, j)."""
    seen = grid.new_mask()
    out = []
    for j in range(grid.height):
        for i in range(grid.width):
            if mask[j][i] and not seen[j][i]:
                stack, comp = [(i, j)], []
                seen[j][i] = 1
                while stack:
                    a, b = stack.pop()
                    comp.append((a, b))
                    for da, db in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        c, d = a + da, b + db
                        if grid.contains(c, d) and mask[d][c] and not seen[d][c]:
                            seen[d][c] = 1
                            stack.append((c, d))
                out.append(comp)
    return out


def close_pinholes(grid, floor, inside):
    """Unseen specks: a 4-connected set of at most 4 non-floor cells inside the outline whose
    4-neighbours are all floor becomes floor. Larger unseen areas stay not-free."""
    unseen = grid.new_mask()
    for j in range(grid.height):
        for i in range(grid.width):
            if inside[j][i] and not floor[j][i]:
                unseen[j][i] = 1
    for comp in components4(grid, unseen):
        if len(comp) > PINHOLE_MAX_CELLS:
            continue
        cells = set(comp)
        ok = True
        for (a, b) in comp:
            for da, db in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                c, d = a + da, b + db
                if (c, d) in cells:
                    continue
                if not grid.contains(c, d) or not floor[d][c]:
                    ok = False
        if ok:
            for (a, b) in comp:
                floor[b][a] = 1
    return floor


def signed_distance(grid, free):
    """Exact distance, in metres, from every cell centre to the edge of FREE.

    Positive in free cells (distance to the nearest not-free cell's square), negative in
    the others (distance to the nearest free cell's square). Cells beyond the grid count
    as not-free. Two passes: along rows, then the exact minimum down each column."""
    w, h = grid.width, grid.height
    big = 1e9

    def unsigned_to(target):
        # row pass: horizontal gap, in cells, from each centre to the nearest target square
        row = [[big] * w for _ in range(h)]
        for j in range(h):
            last = None
            for i in range(w):
                if target(i, j):
                    last = i
                    row[j][i] = 0.0
                elif last is not None:
                    row[j][i] = (i - last) - 0.5
            last = None
            for i in range(w - 1, -1, -1):
                if target(i, j):
                    last = i
                elif last is not None:
                    row[j][i] = min(row[j][i], (last - i) - 0.5)
        out = [[big] * w for _ in range(h)]
        for i in range(w):
            col = [row[j][i] for j in range(h)]
            for j in range(h):
                best = big
                for jj in range(h):
                    hgap = col[jj]
                    if hgap >= big:
                        continue
                    vgap = max(abs(j - jj) - 0.5, 0.0)
                    if vgap >= best:
                        continue
                    d = math.hypot(hgap, vgap)
                    if d < best:
                        best = d
                out[j][i] = best
        return out

    # distance to not-free: also the grid border acts as not-free
    def not_free(i, j):
        return not free[j][i]

    def is_free(i, j):
        return bool(free[j][i])

    to_blocked = unsigned_to(not_free)
    to_free = unsigned_to(is_free)
    sdf = [[0.0] * w for _ in range(h)]
    for j in range(h):
        for i in range(w):
            if free[j][i]:
                d = to_blocked[j][i]
                # the border: cells beyond the grid are not-free
                d = min(d, i + 0.5, w - i - 0.5, j + 0.5, h - j - 0.5)
                sdf[j][i] = d * CELL
            else:
                d = to_free[j][i]
                sdf[j][i] = -(d * CELL) if d < big else -(max(w, h) * CELL)
    return sdf


def walk_component(grid, sdf, seed_xz, threshold=WALK_SDF):
    """The 4-connected part of {sdf > 0.25 m} that contains the seed point."""
    over = grid.new_mask()
    for j in range(grid.height):
        for i in range(grid.width):
            if sdf[j][i] > threshold:
                over[j][i] = 1
    comps = components4(grid, over)
    if not comps:
        return grid.new_mask()
    si, sj = grid.cell_of(seed_xz[0], seed_xz[1])
    chosen = None
    for comp in comps:
        if (si, sj) in set(comp):
            chosen = comp
            break
    if chosen is None:
        # the seed is not walkable: take the component with the nearest cell centre
        best = None
        for comp in comps:
            for (i, j) in comp:
                x, z = grid.centre(i, j)
                key = ((x - seed_xz[0]) ** 2 + (z - seed_xz[1]) ** 2, j, i)
                if best is None or key < best[0]:
                    best = (key, comp)
        chosen = best[1]
    mask = grid.new_mask()
    for (i, j) in chosen:
        mask[j][i] = 1
    return mask


def largest_square(grid, mask):
    """Side, in cells, of the largest square of set cells."""
    w, h = grid.width, grid.height
    prev = [0] * (w + 1)
    best = 0
    for j in range(h):
        cur = [0] * (w + 1)
        for i in range(w):
            if mask[j][i]:
                cur[i + 1] = 1 + min(prev[i], prev[i + 1], cur[i])
                if cur[i + 1] > best:
                    best = cur[i + 1]
        prev = cur
    return best


def maximal_rectangles(grid, mask):
    """Every rectangle of set cells that cannot be widened at its bottom row: (i0, j0, i1, j1),
    half-open, found with the histogram method. Contains every maximal rectangle."""
    w, h = grid.width, grid.height
    heights = [0] * w
    out = []
    for j in range(h):
        for i in range(w):
            heights[i] = heights[i] + 1 if mask[j][i] else 0
        stack = []            # (start column, height)
        for i in range(w + 1):
            cur = heights[i] if i < w else 0
            start = i
            while stack and stack[-1][1] >= cur:
                s_col, s_h = stack.pop()
                if s_h > 0:
                    out.append((s_col, j + 1 - s_h, i, j + 1))
                start = s_col
            stack.append((start, cur))
    return out


def free_rectangle(grid, walk, walker_xz):
    """The free rectangle, as cells (i0, j0, i1, j1), half-open.

    Rule: (1) the largest short side, counted up to 3.6 m; (2) then the largest area;
    (3) then the centre nearest the walker; (4) then the lowest row, then the lowest column."""
    side = largest_square(grid, walk)
    if side == 0:
        return None
    cap = int(round(RECT_SIDE_CAP / CELL))
    need = min(side, cap)
    best = None
    for (i0, j0, i1, j1) in maximal_rectangles(grid, walk):
        w, h = i1 - i0, j1 - j0
        if min(w, h) < need:
            continue
        cx = grid.origin_x + 0.5 * (i0 + i1) * CELL
        cz = grid.origin_z + 0.5 * (j0 + j1) * CELL
        dist2 = round((cx - walker_xz[0]) ** 2 + (cz - walker_xz[1]) ** 2, 9)
        key = (-(w * h), dist2, j0, i0)
        if best is None or key < best[0]:
            best = (key, (i0, j0, i1, j1))
    return best[1]


def trace_region(grid, mask):
    """Boundary of a 4-connected set of cells as rings of lattice points (in cells, relative
    to the grid's low corner). The outer ring is counter-clockwise, holes are clockwise,
    collinear points are removed. Returns (outer, holes)."""
    w, h = grid.width, grid.height

    def filled(i, j):
        return 0 <= i < w and 0 <= j < h and mask[j][i]

    # directed edges with the region on the left
    nxt = {}
    for j in range(h):
        for i in range(w):
            if not mask[j][i]:
                continue
            if not filled(i, j - 1):
                nxt.setdefault((i, j), []).append((i + 1, j))          # south edge, heading +x
            if not filled(i + 1, j):
                nxt.setdefault((i + 1, j), []).append((i + 1, j + 1))  # east edge, heading +z
            if not filled(i, j + 1):
                nxt.setdefault((i + 1, j + 1), []).append((i, j + 1))  # north edge, heading -x
            if not filled(i - 1, j):
                nxt.setdefault((i, j + 1), []).append((i, j))          # west edge, heading -z
    rings = []
    while nxt:
        start = min(nxt.keys(), key=lambda p: (p[1], p[0]))
        ring = [start]
        prev_dir = None
        cur = start
        while True:
            options = nxt[cur]
            if len(options) == 1 or prev_dir is None:
                choice = options[0]
            else:
                # two cells touch at this point only by their corners: turn left, which keeps
                # the ring round one 4-connected piece
                left = (-prev_dir[1], prev_dir[0])
                choice = None
                for cand in options:
                    if (cand[0] - cur[0], cand[1] - cur[1]) == left:
                        choice = cand
                if choice is None:
                    choice = options[0]
            options.remove(choice)
            if not options:
                del nxt[cur]
            prev_dir = (choice[0] - cur[0], choice[1] - cur[1])
            cur = choice
            if cur == start and cur not in nxt:
                break
            if cur == start:
                # back at the start with edges left there: the ring is closed all the same
                break
            ring.append(cur)
        rings.append(_drop_collinear(ring))
    outer = [r for r in rings if polygon_area(r) > 0]
    holes = [r for r in rings if polygon_area(r) < 0]
    return outer, holes


def _drop_collinear(ring):
    out = []
    n = len(ring)
    for k in range(n):
        a, b, c = ring[k - 1], ring[k], ring[(k + 1) % n]
        if (b[0] - a[0]) * (c[1] - b[1]) - (b[1] - a[1]) * (c[0] - b[0]) != 0:
            out.append(b)
    return out


def rotate_ring_to_lowest(ring):
    """Start a ring at its lowest point (lowest z, then lowest x), keeping its direction."""
    k = min(range(len(ring)), key=lambda q: (ring[q][1], ring[q][0]))
    return ring[k:] + ring[:k]


# --------------------------------------------------------------------------------------
# FloorScan on a synthetic room
# --------------------------------------------------------------------------------------

def floor_scan(outline, boxes, walker_xz, unseen=None):
    """Run the grid pipeline in the frame the room is described in.

    outline    list of (x, z), counter-clockwise: the outline the walker confirmed
    boxes      list of Box: furniture present at scan time
    walker_xz  where the walker stands when the sweep ends
    unseen     optional list of Box whose footprints were never seen (not floor, not obstacle)
    Returns a dict with the grid, the masks, the distance field and the free rectangle."""
    grid = grid_for_outline(outline)
    inside = rasterise_inside(grid, outline)
    obst = rasterise_boxes(grid, [b for b in boxes])
    floor = grid.new_mask()
    for j in range(grid.height):
        for i in range(grid.width):
            floor[j][i] = inside[j][i]
    unknown_cells = 0
    if unseen:
        holes = grid.new_mask()
        for u in unseen:
            fake = Box(u.base, (u.size[0], 1.0, u.size[2]), u.yaw)
            fake.base = (u.base[0], 0.5, u.base[2])
            part = rasterise_boxes(grid, [fake])
            for j in range(grid.height):
                for i in range(grid.width):
                    if part[j][i]:
                        holes[j][i] = 1
        for j in range(grid.height):
            for i in range(grid.width):
                if holes[j][i] and not obst[j][i]:
                    floor[j][i] = 0
        floor = close_pinholes(grid, floor, inside)
        for j in range(grid.height):
            for i in range(grid.width):
                if inside[j][i] and not floor[j][i] and not obst[j][i]:
                    unknown_cells += 1
    # walls: everything that is not inside the outline blocks, like an obstacle
    blocked = grid.new_mask()
    for j in range(grid.height):
        for i in range(grid.width):
            if obst[j][i] or not inside[j][i]:
                blocked[j][i] = 1
    grown = dilate8(grid, blocked)
    free = grid.new_mask()
    for j in range(grid.height):
        for i in range(grid.width):
            if floor[j][i] and not grown[j][i]:
                free[j][i] = 1
    sdf = signed_distance(grid, free)
    walk = walk_component(grid, sdf, walker_xz)
    rect = free_rectangle(grid, walk, walker_xz)
    inside_cells = sum(sum(row) for row in inside)
    return {
        "grid": grid, "inside": inside, "obst": obst, "free": free, "sdf": sdf, "walk": walk,
        "rect": rect, "unknownShare": (unknown_cells / inside_cells) if inside_cells else 0.0,
    }


def rect_metrics(grid, rect):
    i0, j0, i1, j1 = rect
    return {
        "cx": grid.origin_x + 0.5 * (i0 + i1) * CELL,
        "cz": grid.origin_z + 0.5 * (j0 + j1) * CELL,
        "wx": (i1 - i0) * CELL,
        "wz": (j1 - j0) * CELL,
    }


# --------------------------------------------------------------------------------------
# RoomModel assembly (the JSON document of roommodel.schema.json)
# --------------------------------------------------------------------------------------

def q6(v):
    """Round to 6 decimals and drop a negative zero."""
    r = round(float(v), 6)
    return 0.0 if r == 0 else r


def encode_sdf(sdf_rows):
    """Row-major, row 0 at the lowest z, signed 16-bit little-endian millimetres, base64."""
    values = []
    for row in sdf_rows:
        for v in row:
            mm = int(math.floor(v * 1000.0 + 0.5))
            values.append(max(-32768, min(32767, mm)))
    return base64.b64encode(struct.pack("<%dh" % len(values), *values)).decode("ascii")


def decode_sdf(data, width, height):
    raw = base64.b64decode(data, validate=True)
    if len(raw) != 2 * width * height:
        raise ValueError("sdf.data holds %d bytes, expected %d" % (len(raw), 2 * width * height))
    flat = struct.unpack("<%dh" % (width * height), raw)
    return [[flat[j * width + i] / 1000.0 for i in range(width)] for j in range(height)]


def sdf_sample(grid, sdf, x, z):
    """The distance field at any floor point, as RoomSdf.Sample defines it.

    Bilinear between the four nearest cell centres. Beyond the outermost centres the query is
    clamped to them and the distance to the clamped point is subtracted, so the value keeps
    falling outside the grid."""
    u = (x - grid.origin_x) / CELL - 0.5
    v = (z - grid.origin_z) / CELL - 0.5
    uc = min(max(u, 0.0), grid.width - 1.0)
    vc = min(max(v, 0.0), grid.height - 1.0)
    i0 = min(int(math.floor(uc)), grid.width - 2)
    j0 = min(int(math.floor(vc)), grid.height - 2)
    fu, fv = uc - i0, vc - j0
    value = ((sdf[j0][i0] * (1.0 - fu) + sdf[j0][i0 + 1] * fu) * (1.0 - fv) +
             (sdf[j0 + 1][i0] * (1.0 - fu) + sdf[j0 + 1][i0 + 1] * fu) * fv)
    return value - math.hypot(u - uc, v - vc) * CELL


def room_frame_grid(grid, rect, quarter_sign):
    """Re-index a scan grid into the room frame. Returns (room grid, index map).

    quarter_sign is 0 when the rectangle's short side already runs along x, +1 for a room
    frame turned +90 degrees (room +X is the old -Z) and -1 for -90 degrees (room +X is the
    old +Z). The map takes a room cell (i, j) to the scan cell it came from, so no value is
    resampled: the rotation is exact."""
    i0, j0, i1, j1 = rect
    cx_half = grid.ix0 + (i0 + i1)          # the rectangle's centre, in half cells from the grid's base
    cz_half = grid.jz0 + (j0 + j1)          # (the room grid below has its base at the room origin)
    w, h = grid.width, grid.height
    if quarter_sign == 0:
        return Grid(grid.ix0 - cx_half, grid.jz0 - cz_half, w, h), (lambda i, j: (i, j))
    if quarter_sign > 0:
        # x_room = -(z - cz), z_room = x - cx
        room = Grid(-(grid.jz0 + 2 * h - cz_half), grid.ix0 - cx_half, h, w)
        return room, (lambda i, j: (j, h - 1 - i))
    # x_room = z - cz, z_room = -(x - cx)
    room = Grid(grid.jz0 - cz_half, -(grid.ix0 + 2 * w - cx_half), h, w)
    return room, (lambda i, j: (w - 1 - j, i))


def build_room_model(room_id, outline, boxes_meta, walker_xz, walls=None, wall_height=2.4,
                     route=0, created="2026-10-01T00:00:00Z"):
    """Build the RoomModel document for a synthetic room described in any flat frame.

    boxes_meta  list of dicts: {id, category, base, size, yaw, mesh}
    walls       optional list of per-outline-edge opening lists; wall k runs from outline[k]
                to outline[k + 1]
    The result is expressed in the room frame: origin at the centre of the free rectangle,
    +X along its short side."""
    # 1. Turn the description so that the dominant wall direction lies along the axes.
    theta = outline_axis(outline)
    ct, st = math.cos(theta), math.sin(theta)

    def to_aligned(x, z):
        return (ct * x + st * z, -st * x + ct * z)

    outline_a = [to_aligned(x, z) for (x, z) in outline]
    boxes_a = []
    for b in boxes_meta:
        x, z = to_aligned(b["base"][0], b["base"][2])
        boxes_a.append(Box((x, b["base"][1], z), b["size"], b.get("yaw", 0.0) + theta))
    walker_a = to_aligned(*walker_xz)

    # 2. The grid pipeline.
    scan = floor_scan(outline_a, boxes_a, walker_a)
    if scan["rect"] is None:
        raise ValueError("no walkable floor in room %s" % room_id)
    grid = scan["grid"]
    m = rect_metrics(grid, scan["rect"])

    # 3. Room frame: +X along the short side. Its yaw in the described frame is -theta, plus a
    #    quarter turn when the rectangle is wider than deep; the turn is taken in the sense
    #    that keeps the total in (-90, 90] degrees.
    quarter_sign = 0
    if m["wx"] > m["wz"] + 1e-9:
        quarter_sign = 1 if theta >= 0.0 else -1
    frame_yaw = -theta + quarter_sign * 0.5 * math.pi

    def to_room(x, z):
        dx, dz = x - m["cx"], z - m["cz"]
        if quarter_sign > 0:
            return (-dz, dx)
        if quarter_sign < 0:
            return (dz, -dx)
        return (dx, dz)

    outline_r = [to_room(x, z) for (x, z) in outline_a]
    boxes_r = []
    for b in boxes_meta:
        x, z = to_room(*to_aligned(b["base"][0], b["base"][2]))
        boxes_r.append({
            "id": b["id"], "category": b["category"],
            "base": [x, b["base"][1], z], "size": list(b["size"]),
            "yaw": wrap_angle(b.get("yaw", 0.0) - frame_yaw), "mesh": b.get("mesh", "placeholder"),
            "assetHash": b.get("assetHash", ""),
        })

    # 4. The same cells, re-indexed into the room frame.
    grid_r, source = room_frame_grid(grid, scan["rect"], quarter_sign)

    def remap(rows):
        return [[rows[source(i, j)[1]][source(i, j)[0]] for i in range(grid_r.width)]
                for j in range(grid_r.height)]

    scan_r = {"sdf": remap(scan["sdf"]), "walk": remap(scan["walk"]), "unknownShare": scan["unknownShare"]}
    wf, df = min(m["wx"], m["wz"]), max(m["wx"], m["wz"])
    lf = loop_fit(wf, df)
    outer, holes = trace_region(grid_r, scan_r["walk"])
    if len(outer) != 1:
        raise ValueError("walkable region must have exactly one outer ring")

    def lattice(ring):
        ring = rotate_ring_to_lowest(ring)
        return [[q6(grid_r.origin_x + p[0] * CELL), q6(grid_r.origin_z + p[1] * CELL)] for p in ring]

    targets = assign_tri_targets([{"id": b["id"], "size": [q6(v) for v in b["size"]]} for b in boxes_r])
    if boxes_r and all(b["mesh"] == "standIn" for b in boxes_r):
        targets = {b["id"]: STANDIN_TRIANGLES for b in boxes_r}
    objects = []
    for b in boxes_r:
        objects.append({
            "id": b["id"], "category": b["category"],
            "base": [q6(v) for v in b["base"]], "size": [q6(v) for v in b["size"]],
            "yaw": q6(b["yaw"]), "mesh": b["mesh"], "assetHash": b["assetHash"],
            "triTarget": targets[b["id"]],
        })
    wall_list = []
    n = len(outline_r)
    for k in range(n):
        a, b = outline_r[k], outline_r[(k + 1) % n]
        openings = []
        if walls and k < len(walls) and walls[k]:
            for o in walls[k]:
                openings.append({"kind": o["kind"], "offset": q6(o["offset"]), "width": q6(o["width"]),
                                 "bottom": q6(o["bottom"]), "top": q6(o["top"])})
        wall_list.append({"a": [q6(a[0]), q6(a[1])], "b": [q6(b[0]), q6(b[1])],
                          "height": q6(wall_height), "openings": openings})
    g, al = glued_cells(wf, df), aligned_cells(wf, df)
    return {
        "version": 1,
        "id": room_id,
        "createdUtc": created,
        "anchor": {"id": "", "position": [0.0, 0.0, 0.0], "yaw": 0.0},
        "outline": [[q6(x), q6(z)] for (x, z) in outline_r],
        "sdf": {
            "cell": CELL, "width": grid_r.width, "height": grid_r.height,
            "originX": q6(grid_r.origin_x), "originZ": q6(grid_r.origin_z),
            "data": encode_sdf(scan_r["sdf"]),
        },
        "walkable": {"outer": lattice(outer[0]), "holes": [lattice(hh) for hh in sorted(
            holes, key=lambda r: min((p[1], p[0]) for p in r))]},
        "freeRect": {"wf": q6(wf), "df": q6(df)},
        "lapSquare": {"side": q6(lf["s"])},
        "curvature": {"l5": q6(lf["l5"]), "l6": q6(lf["l6"]), "r3": q6(lf["r3"]),
                      "floorL": FLOOR_L, "floorR": FLOOR_R},
        "cells": {
            "glued": {"nx": g["nx"], "nz": g["nz"], "sizeX": q6(g["sizeX"]), "sizeZ": q6(g["sizeZ"])},
            "aligned": {"n": al["n"], "m": al["m"], "g": q6(al["g"])},
        },
        "standing": is_standing(wf, df),
        "walls": wall_list,
        "objects": objects,
        "quality": {"unknownShare": q6(scan_r["unknownShare"]), "route": route},
    }


def format_number(v):
    """Canonical number text: integers as they are; other numbers in fixed notation with at
    most six decimals, trailing zeros removed, always at least one decimal (0.0, 2.4)."""
    if isinstance(v, bool):
        raise TypeError("not a number")
    if isinstance(v, int):
        return str(v)
    text = "%.6f" % v
    text = text.rstrip("0")
    if text.endswith("."):
        text += "0"
    return "0.0" if text == "-0.0" else text


def format_value(v):
    """Compact JSON with ", " and ": " separators and canonical numbers."""
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, (int, float)):
        return format_number(v)
    if isinstance(v, str):
        import json
        return json.dumps(v, ensure_ascii=True)
    if isinstance(v, list):
        return "[" + ", ".join(format_value(x) for x in v) + "]"
    if isinstance(v, dict):
        return "{" + ", ".join('"%s": %s' % (k, format_value(x)) for k, x in v.items()) + "}"
    raise TypeError("cannot write %r" % (v,))


def dump_room(room):
    """The canonical RoomModel text: one top-level key per line (two spaces), one wall and one
    object per line (four spaces), line feeds, and a final line feed."""
    lines = ["{"]
    keys = list(room.keys())
    for idx, key in enumerate(keys):
        comma = "," if idx + 1 < len(keys) else ""
        value = room[key]
        if key in ("walls", "objects") and value:
            lines.append('  "%s": [' % key)
            for k, item in enumerate(value):
                lines.append("    %s%s" % (format_value(item), "," if k + 1 < len(value) else ""))
            lines.append("  ]%s" % comma)
        else:
            lines.append('  "%s": %s%s' % (key, format_value(value), comma))
    lines.append("}")
    return "\n".join(lines) + "\n"


# --------------------------------------------------------------------------------------
# Geometry hash: the "RoomModel hash" that goes into the link's worldHash. Integers only,
# so the phone, the Station and this script agree bit for bit.
# --------------------------------------------------------------------------------------

def _mm(v):
    return int(math.floor(float(v) * 1000.0 + 0.5))


def geometry_hash(room):
    """SHA-256, lower-case hex, over the room's geometry (not its mesh states or its quality)."""
    out = bytearray(b"ATLAS-ROOM-1")

    def put(*ints):
        out.extend(struct.pack("<%di" % len(ints), *ints))

    sdf = room["sdf"]
    put(sdf["width"], sdf["height"], _mm(sdf["originX"]), _mm(sdf["originZ"]))
    out.extend(base64.b64decode(sdf["data"]))
    put(_mm(room["freeRect"]["wf"]), _mm(room["freeRect"]["df"]))
    put(len(room["outline"]))
    for p in room["outline"]:
        put(_mm(p[0]), _mm(p[1]))
    put(len(room["walls"]))
    for wall in room["walls"]:
        put(_mm(wall["a"][0]), _mm(wall["a"][1]), _mm(wall["b"][0]), _mm(wall["b"][1]), _mm(wall["height"]))
        put(len(wall["openings"]))
        for o in wall["openings"]:
            put(OPENING_KINDS.index(o["kind"]), _mm(o["offset"]), _mm(o["width"]), _mm(o["bottom"]), _mm(o["top"]))
    put(len(room["objects"]))
    for o in room["objects"]:
        put(o["id"], CATEGORIES.index(o["category"]))
        put(*[_mm(v) for v in o["base"]])
        put(*[_mm(v) for v in o["size"]])
        put(int(math.floor(float(o["yaw"]) * 10000.0 + 0.5)))
    return hashlib.sha256(bytes(out)).hexdigest()


# --------------------------------------------------------------------------------------
# Stand-in furniture and the Home shell: counts the renderer must reproduce
# --------------------------------------------------------------------------------------

def standin_ellipsoid(size, segments=STANDIN_SEGMENTS, bands=STANDIN_BANDS):
    """Stand-in mesh for a box: the ellipsoid inscribed in it, origin at the base centre.

    Returns (vertices, triangles). 2 + segments * (bands - 1) vertices and
    2 * segments * (bands - 1) triangles: 2,502 and 5,000 with the defaults."""
    a, b, c = 0.5 * size[0], 0.5 * size[1], 0.5 * size[2]
    verts = [(0.0, 2.0 * b, 0.0)]
    for k in range(1, bands):
        theta = math.pi * k / bands
        for j in range(segments):
            lam = 2.0 * math.pi * j / segments
            verts.append((a * math.sin(theta) * math.cos(lam), b + b * math.cos(theta),
                          c * math.sin(theta) * math.sin(lam)))
    verts.append((0.0, 0.0, 0.0))
    bottom = len(verts) - 1

    def ring(k, j):
        return 1 + (k - 1) * segments + (j % segments)

    tris = []
    for j in range(segments):
        tris.append((0, ring(1, j + 1), ring(1, j)))
    for k in range(1, bands - 1):
        for j in range(segments):
            p00, p01 = ring(k, j), ring(k, j + 1)
            p10, p11 = ring(k + 1, j), ring(k + 1, j + 1)
            tris.append((p00, p01, p11))
            tris.append((p00, p11, p10))
    for j in range(segments):
        tris.append((bottom, ring(bands - 1, j), ring(bands - 1, j + 1)))
    return verts, tris


def longest_edge(verts, tris):
    best = 0.0
    for (p, q, r) in tris:
        for (u, v) in ((p, q), (q, r), (r, p)):
            best = max(best, math.dist(verts[u], verts[v]))
    return best


def _clip_to_cell(poly, x0, z0, x1, z1):
    """Sutherland-Hodgman: clip a polygon to an axis-aligned cell."""
    def clip(points, inside, cross):
        out = []
        for k in range(len(points)):
            cur, prev = points[k], points[k - 1]
            cin, pin = inside(cur), inside(prev)
            if cin:
                if not pin:
                    out.append(cross(prev, cur))
                out.append(cur)
            elif pin:
                out.append(cross(prev, cur))
        return out

    def cross_x(xc):
        return lambda p, q: (xc, p[1] + (q[1] - p[1]) * (xc - p[0]) / (q[0] - p[0]))

    def cross_z(zc):
        return lambda p, q: (p[0] + (q[0] - p[0]) * (zc - p[1]) / (q[1] - p[1]), zc)

    pts = list(poly)
    for inside, cross in ((lambda p: p[0] >= x0, cross_x(x0)), (lambda p: p[0] <= x1, cross_x(x1)),
                          (lambda p: p[1] >= z0, cross_z(z0)), (lambda p: p[1] <= z1, cross_z(z1))):
        if not pts:
            break
        pts = clip(pts, inside, cross)
    # drop repeated points
    out = []
    for p in pts:
        if not out or math.dist(p, out[-1]) > 1e-9:
            out.append(p)
    if len(out) > 1 and math.dist(out[0], out[-1]) <= 1e-9:
        out.pop()
    return out


def shell_triangles(room):
    """Triangles of the Home shell under the rule of the contract page: (floor, walls, band)."""
    outline = [tuple(p) for p in room["outline"]]
    xs, zs = [p[0] for p in outline], [p[1] for p in outline]
    i0 = int(math.floor(min(xs) / SHELL_PITCH + 1e-6))
    i1 = int(math.ceil(max(xs) / SHELL_PITCH - 1e-6))
    j0 = int(math.floor(min(zs) / SHELL_PITCH + 1e-6))
    j1 = int(math.ceil(max(zs) / SHELL_PITCH - 1e-6))
    floor = 0
    for j in range(j0, j1):
        for i in range(i0, i1):
            piece = _clip_to_cell(outline, i * SHELL_PITCH, j * SHELL_PITCH,
                                  (i + 1) * SHELL_PITCH, (j + 1) * SHELL_PITCH)
            if len(piece) >= 3 and abs(polygon_area(piece)) > 1e-9:
                floor += len(piece) - 2

    def wall_count(band):
        total = 0
        for wall in room["walls"]:
            length = math.dist(wall["a"], wall["b"])
            height = wall["height"]
            cuts = {0.0, length}
            for o in wall["openings"]:
                cuts.add(o["offset"])
                cuts.add(o["offset"] + o["width"])
            cuts = sorted(cuts)
            for u0, u1 in zip(cuts, cuts[1:]):
                if u1 - u0 < 1e-6:
                    continue
                cols = int(math.ceil((u1 - u0) / SHELL_PITCH - 1e-6))
                mid = 0.5 * (u0 + u1)
                panels = [(0.0, height)]
                for o in wall["openings"]:
                    if o["offset"] - 1e-9 <= mid <= o["offset"] + o["width"] + 1e-9:
                        panels = []
                        if o["bottom"] > 1e-6:
                            panels.append((0.0, o["bottom"]))
                        if o["top"] < height - 1e-6:
                            panels.append((o["top"], height))
                for (y0, y1) in panels:
                    rows = int(math.ceil((y1 - y0) / band - 1e-6))
                    total += 2 * cols * rows
        return total

    band = SHELL_PITCH
    walls = wall_count(band)
    while floor + walls > SHELL_MAX_TRIANGLES and band < 8.0:
        band *= 2.0
        walls = wall_count(band)
    return floor, walls, band


# --------------------------------------------------------------------------------------
# Reference tables
# --------------------------------------------------------------------------------------

def empty_room_reference(w, d):
    """Loop-fit and cell grids for an empty W x D room measured wall to wall."""
    wf, df = min(w, d) - 2 * BODY_CLEARANCE, max(w, d) - 2 * BODY_CLEARANCE
    lf = loop_fit(wf, df)
    return {"wf": wf, "df": df, "lf": lf, "glued": glued_cells(wf, df),
            "aligned": aligned_cells(wf, df), "standing": is_standing(wf, df)}


def pipeline_reference(w, d, turn_deg=0.0):
    """Push an empty W x D room, optionally turned in its description frame, through the grid
    pipeline and return the RoomModel document."""
    c, sn = math.cos(math.radians(turn_deg)), math.sin(math.radians(turn_deg))

    def turn(x, z):
        return (c * x - sn * z, sn * x + c * z)

    outline = [turn(x, z) for (x, z) in ((0.0, 0.0), (w, 0.0), (w, d), (0.0, d))]
    return build_room_model("check", outline, [], turn(0.5 * w, 0.5 * d))


def main():
    print("K5 = %.16f   K6 = %.16f" % (K5, K6))
    print()
    print("Loop-fit rule on empty rooms measured wall to wall (plan section 3.2):")
    print("room      Wf    Df    s      L5      L6      R3      glued            aligned         standing")
    for (w, d) in ((2.0, 2.0), (2.5, 2.5), (3.0, 3.0), (3.0, 4.0), (4.0, 5.0), (6.0, 8.0)):
        r = empty_room_reference(w, d)
        g, a, lf = r["glued"], r["aligned"], r["lf"]
        print("%.1fx%.1f  %.2f  %.2f  %.3f  %.4f  %.4f  %.4f  %dx%d %.3fx%.3f  %dx%d g=%.4f  %s" % (
            w, d, r["wf"], r["df"], lf["s"], lf["l5"], lf["l6"], lf["r3"],
            g["nx"], g["nz"], g["sizeX"], g["sizeZ"], a["n"], a["m"], a["g"], r["standing"]))
    print()
    print("The same rooms through the grid pipeline (outline -> grid -> distance field -> rectangle):")
    ok = True
    for (w, d) in ((2.0, 2.0), (2.5, 2.5), (3.0, 3.0), (3.0, 4.0), (4.0, 5.0), (6.0, 8.0)):
        room = pipeline_reference(w, d)
        ref = empty_room_reference(w, d)
        same = (abs(room["freeRect"]["wf"] - ref["wf"]) < 1e-6 and abs(room["freeRect"]["df"] - ref["df"]) < 1e-6
                and abs(room["curvature"]["l5"] - ref["lf"]["l5"]) < 1e-6)
        ok = ok and same
        print("%.1fx%.1f  Wf x Df = %.2f x %.2f  s = %.3f  L5 = %.4f  R3 = %.4f  %s" % (
            w, d, room["freeRect"]["wf"], room["freeRect"]["df"], room["lapSquare"]["side"],
            room["curvature"]["l5"], room["curvature"]["r3"], "ok" if same else "MISMATCH"))
    print()
    print("A 3 x 4 m room described at an angle (the dominant wall direction is recovered):")
    for turn in (20.0, -33.0, 45.0, 90.0):
        room = pipeline_reference(3.0, 4.0, turn)
        close = abs(room["freeRect"]["wf"] - 2.4) <= 0.10 + 1e-9 and abs(room["freeRect"]["df"] - 3.4) <= 0.10 + 1e-9
        ok = ok and close
        print("turned %+5.1f deg  Wf x Df = %.2f x %.2f  %s" % (
            turn, room["freeRect"]["wf"], room["freeRect"]["df"], "within 0.10 m" if close else "OUT OF TOLERANCE"))
    print()
    print("Triangle rule of Mesh Design section 5.4 (full target = equal-area case, light target) by number of meshed objects:")
    print("  " + ", ".join("%d: %d, %d" % (n, tri_target(n), light_target(n)) for n in (1, 5, 8, 12, 20, 32)))
    print("Test vectors of the rule:")
    ok = check_tri_rule() and ok
    return 0 if ok else 1


if __name__ == "__main__":
    import sys
    sys.exit(main())
