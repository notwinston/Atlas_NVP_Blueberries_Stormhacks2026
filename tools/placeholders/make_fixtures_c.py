#!/usr/bin/env python3
# PLACEHOLDER (lane C, P1-X07): contract surface only; replaced by lane C's implementation
# Lane-B temporary tooling (PH-21): writes the placeholder lane C fixtures into recordings/.
# Python stdlib only, deterministic. Delete when lane C's real fixtures land.
import base64, json, math, os, random, struct

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "recordings")
CELL = 0.05
FLOOR_OBJ_HASH = "budget-standin-5000"


def seg_dist(px, pz, a, b):
    ax, az = a; bx, bz = b
    dx, dz = bx - ax, bz - az
    t = max(0.0, min(1.0, ((px - ax) * dx + (pz - az) * dz) / (dx * dx + dz * dz)))
    return math.hypot(px - (ax + t * dx), pz - (az + t * dz))


def inside(poly, x, z):
    c = False
    n = len(poly)
    for i in range(n):
        a, b = poly[i], poly[(i + 1) % n]
        if (a[1] > z) != (b[1] > z) and x < (b[0] - a[0]) * (z - a[1]) / (b[1] - a[1]) + a[0]:
            c = not c
    return c


def poly_sd(poly, x, z):  # positive inside
    d = min(seg_dist(x, z, poly[i], poly[(i + 1) % len(poly)]) for i in range(len(poly)))
    return d if inside(poly, x, z) else -d


def box_sd(o, x, z):  # positive outside (yaw 0 only)
    hx, hz = o["size"][0] / 2, o["size"][2] / 2
    dx, dz = abs(x - o["pos"][0]) - hx, abs(z - o["pos"][2]) - hz
    return math.hypot(max(dx, 0), max(dz, 0)) + min(max(dx, dz), 0)


def rnd(v):
    return round(v + 0.0, 6)


def pt(p):
    return {"x": rnd(p[0]), "z": rnd(p[1])}


def room(room_id, outline, inset_outline, holes, free, objects, glued, aligned):
    xs = [p[0] for p in outline]; zs = [p[1] for p in outline]
    x0, z0 = min(xs), min(zs)
    cols, rows = round((max(xs) - x0) / CELL), round((max(zs) - z0) / CELL)
    buf = bytearray()
    for j in range(rows):
        for i in range(cols):
            x, z = x0 + (i + 0.5) * CELL, z0 + (j + 0.5) * CELL
            v = poly_sd(outline, x, z) - 0.05
            for o in objects:
                v = min(v, box_sd(o, x, z) - 0.05)
            buf += struct.pack("<f", v)
    wf, df = free
    s = max(1.0, min(3.2, min(wf, df) - 0.4))
    walls = []
    for i, a in enumerate(outline):
        b = outline[(i + 1) % len(outline)]
        walls.append({"a": pt(a), "b": pt(b), "height": 2.5, "openings": []})
    return {
        "schema": "atlas.room/1",
        "roomId": room_id,
        "anchor": {"position": {"x": 0, "y": 0, "z": 0}, "rotation": {"x": 0, "y": 0, "z": 0, "w": 1}},
        "sdf": {"originX": x0, "originZ": z0, "cols": cols, "rows": rows, "cell": CELL,
                "data": base64.b64encode(bytes(buf)).decode()},
        "walkable": {"outer": [pt(p) for p in inset_outline],
                     "holes": [{"points": [pt(p) for p in h]} for h in holes]},
        "freeRect": {"Wf": wf, "Df": df},
        "lapSquare": {"s": rnd(s)},
        "curvature": {"L5": rnd(s / 1.0613), "L6": rnd(s / 1.3170), "R3": rnd(2 * s / math.pi)},
        "cells": {"glued": glued, "aligned": aligned},
        "standing": min(wf, df) < 1.5,
        "walls": walls,
        "objects": [{"id": o["id"], "category": o["category"],
                     "position": {"x": o["pos"][0], "y": 0, "z": o["pos"][2]},
                     "size": {"x": o["size"][0], "y": o["size"][1], "z": o["size"][2]},
                     "yaw": 0, "mesh": o["mesh"], "hash": o["hash"]} for o in objects],
        "quality": {"unknownShare": 0, "route": 2},
    }


def glued(wf, df):
    nw, nd = max(1, int(wf / 1.5 + 0.5)), max(1, int(df / 1.5 + 0.5))
    return {"nW": nw, "nD": nd, "cellW": rnd(wf / nw), "cellD": rnd(df / nd)}


def aligned(wf, df, m_override=None):
    short, long_ = min(wf, df), max(wf, df)
    n = max(3, math.ceil(short / 1.5))
    g = short / n
    m = math.floor(long_ / g + 1e-6)
    if g < 0.75:
        n, g, m = 2, short / 2, 2  # Master Plan §4.7 comment "a 2 x 2 grid" (PH-22)
    return {"n": n, "m": m, "g": rnd(g)}


def square(x0, z0, x1, z1):
    return [(x0, z0), (x1, z0), (x1, z1), (x0, z1)]


def write(name, data):
    with open(os.path.join(ROOT, name), "w") as f:
        json.dump(data, f, indent=1)
        f.write("\n")


def poselog(name, samples):
    with open(os.path.join(ROOT, name), "w") as f:
        f.write("# atlas.poselog/1  t_s x y z qx qy qz qw tracking\n")
        for t, p, yaw in samples:
            f.write("%.6f %.5f %.5f %.5f 0 %.6f 0 %.6f 0\n" % (t, p[0], p[1], p[2], math.sin(yaw / 2), math.cos(yaw / 2)))


def main():
    os.makedirs(ROOT, exist_ok=True)
    r3 = square(-1.5, -1.5, 1.5, 1.5)
    write("room_3x3.json", room("sample_3x3", r3, square(-1.25, -1.25, 1.25, 1.25), [], (2.4, 2.4), [],
                                glued(2.4, 2.4), aligned(2.4, 2.4)))
    L = [(-1.5, -1.5), (1.5, -1.5), (1.5, 1.5), (0.5, 1.5), (0.5, 2.5), (-1.5, 2.5)]
    Li = [(-1.25, -1.25), (1.25, -1.25), (1.25, 1.25), (0.25, 1.25), (0.25, 2.25), (-1.25, 2.25)]
    write("room_3x4_L.json", room("sample_3x4_L", L, Li, [], (2.4, 2.4), [], glued(2.4, 2.4), aligned(2.4, 2.4)))
    table = {"id": 0, "category": "table", "pos": (0, 0, 1.45), "size": (1.2, 0.75, 0.8), "mesh": "placeholder", "hash": ""}
    write("room_4x5_table.json", room(
        "sample_4x5_table", square(-2, -1.05, 2, 3.95), square(-1.75, -0.8, 1.75, 3.7),
        [square(-0.85, 0.8, 0.85, 2.1)], (3.4, 1.5), [table], glued(3.4, 1.5), aligned(3.4, 1.5)))
    objs = []
    k = 0
    for sx in (-1, 1):
        for z in (-1.25, 0.0, 1.25):
            objs.append({"id": k, "category": "storage", "pos": (sx * 1.85, 0, z), "size": (0.3, 0.8, 0.5)}); k += 1
    for sz in (-1, 1):
        for x in (-1.0, 0.0, 1.0):
            objs.append({"id": k, "category": "storage", "pos": (x, 0, sz * 2.35), "size": (0.5, 0.8, 0.3)}); k += 1
    for o in objs:
        o["mesh"], o["hash"] = "ready", FLOOR_OBJ_HASH
    write("budget_room.json", room(
        "budget_room", square(-2, -2.5, 2, 2.5), square(-1.45, -1.95, 1.45, 1.95), [], (2.8, 3.8), objs,
        glued(2.8, 3.8), aligned(2.8, 3.8)))

    # pose logs: 60 Hz, head y = 1.6 m
    corners = [(-1, -1), (1, -1), (1, 1), (-1, 1), (-1, -1), (1, -1)]  # five sides, turning left (+x then +z)
    samples, t, dt = [], 0.0, 1 / 60
    for i in range(5):
        a, b = corners[i], corners[i + 1]
        length = math.hypot(b[0] - a[0], b[1] - a[1])
        yaw = math.atan2(b[0] - a[0], b[1] - a[1])  # Unity yaw: +z turns towards +x
        steps = round(length / 0.7 / dt)
        for n in range(steps):
            f = n / steps
            samples.append((t, (a[0] + (b[0] - a[0]) * f, 1.6, a[1] + (b[1] - a[1]) * f), yaw)); t += dt
        for n in range(30):  # 0.5 s stop at the corner
            samples.append((t, (b[0], 1.6, b[1]), yaw)); t += dt
    poselog("walk_lap5_3x3.poselog", samples)
    rng = random.Random(1)
    still = [(n * dt, (rng.uniform(-5e-4, 5e-4), 1.6 + rng.uniform(-5e-4, 5e-4), rng.uniform(-5e-4, 5e-4)), 0.0)
             for n in range(3600)]
    poselog("walk_still_60s.poselog", still)


if __name__ == "__main__":
    main()
