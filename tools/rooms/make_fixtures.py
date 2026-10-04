#!/usr/bin/env python3
"""Generate the RoomModel fixtures frozen with the contracts at gate G0.

    python3 make_fixtures.py            write the six fixtures
    python3 make_fixtures.py --check    fail (exit 1) if a file on disk differs from what
                                        would be written; writes nothing
    python3 make_fixtures.py --table    also print the reference table for the contract page

The rooms are described wall to wall, with one corner at (0, 0), and pushed through the
reference pipeline of roommath.py, which re-expresses them in the room frame (origin at the
centre of the free rectangle, +X along its short side). The output is deterministic: no
clock, no random numbers. Python 3 standard library only.
"""

import math
import os
import sys

import roommath as rm

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.normpath(os.path.join(
    HERE, "..", "..", "ATLAS", "Assets", "Atlas", "World", "Resources", "AtlasRooms"))


def rectangle(w, d):
    """Outline of a W x D room, counter-clockwise from the corner at the origin."""
    return [(0.0, 0.0), (w, 0.0), (w, d), (0.0, d)]


def obj(obj_id, category, x, z, w, h, d, yaw_deg=0.0, base_y=0.0, mesh="placeholder"):
    return {"id": obj_id, "category": category, "base": (x, base_y, z), "size": (w, h, d),
            "yaw": math.radians(yaw_deg), "mesh": mesh}


def door(offset, width=0.9, top=2.0):
    return {"kind": "door", "offset": offset, "width": width, "bottom": 0.0, "top": top}


def window(offset, width, bottom=0.9, top=2.1):
    return {"kind": "window", "offset": offset, "width": width, "bottom": bottom, "top": top}


def fixtures():
    """(file name, room id, outline, objects, walker, per-edge openings)."""
    out = []

    # 3 x 3 m, empty. Free rectangle 2.4 x 2.4 m; s = 2.0 m (plan section 3.2).
    out.append(("sample_3x3.json", "sample_3x3", rectangle(3.0, 3.0), [], (1.5, 1.5), None))

    # 3 x 4 m L-shape: a 2.5 x 3.0 m main part and a 1.5 x 1.5 m arm.
    #   z=3 +---------+
    #       |         |
    #  1.5  |         +------+
    #       |                |
    #   z=0 +---door---------+
    #      x=0       2.5     4
    l_outline = [(0.0, 0.0), (4.0, 0.0), (4.0, 1.5), (2.5, 1.5), (2.5, 3.0), (0.0, 3.0)]
    out.append(("sample_L_3x4.json", "sample_L_3x4", l_outline, [], (1.25, 1.5),
                [[door(0.5)], [], [], [], [], []]))

    # 4 x 5 m with a 1.0 x 0.6 m table, 0.75 m high, in the middle. The walker ends the scan
    # south of the table, which picks that half of the floor for the free rectangle.
    table = [obj(1, "table", 2.0, 2.5, 1.0, 0.75, 0.6)]
    out.append(("sample_4x5_table.json", "sample_4x5_table", rectangle(4.0, 5.0), table, (2.0, 1.1),
                [[door(0.4)], [window(1.9, 1.2)], [], []]))

    # budget_room: 4 x 5 m with twelve stand-in objects round the walls. Each is drawn as an
    # ellipsoid of exactly 5,000 triangles (the full furniture load of plan section 8.4).
    s = "standIn"
    budget = [
        obj(1, "storage", 1.00, 0.25, 1.2, 1.8, 0.5, mesh=s),               # south wall
        obj(2, "table", 3.20, 0.25, 1.2, 0.75, 0.5, mesh=s),
        obj(3, "couch", 1.40, 4.75, 1.8, 0.8, 0.5, mesh=s),                 # north wall
        obj(4, "plant", 3.00, 4.80, 0.4, 1.2, 0.4, mesh=s),
        obj(5, "storage", 0.20, 1.40, 1.0, 2.0, 0.4, yaw_deg=90, mesh=s),   # west wall
        obj(6, "chair", 0.25, 2.50, 0.5, 0.9, 0.5, yaw_deg=90, mesh=s),
        obj(7, "refrigerator", 0.25, 3.60, 0.5, 1.8, 0.5, yaw_deg=90, mesh=s),
        obj(8, "screen", 3.95, 2.50, 1.0, 0.6, 0.1, yaw_deg=90, base_y=1.0, mesh=s),   # east wall
        obj(9, "storage", 3.75, 2.50, 1.4, 0.8, 0.5, yaw_deg=90, mesh=s),
        obj(10, "lamp", 3.75, 1.00, 0.3, 1.5, 0.3, yaw_deg=30, mesh=s),
        obj(11, "chair", 3.75, 3.90, 0.5, 0.9, 0.5, yaw_deg=90, mesh=s),
        obj(12, "other", 3.70, 4.75, 0.5, 0.4, 0.5, mesh=s),
    ]
    out.append(("budget_room.json", "budget_room", rectangle(4.0, 5.0), budget, (2.0, 2.5),
                [[door(1.65)], [], [], []]))

    # budget_room_120k: the room of budget_room with twelve more stand-ins in the gaps along the walls,
    # 24 in all, 120,000 triangles (Mesh Design check M3, the full-copy load of the furniture budget).
    more = [
        obj(13, "plant", 0.20, 0.20, 0.4, 1.0, 0.4, mesh=s),                # south wall
        obj(14, "other", 2.10, 0.20, 0.8, 0.6, 0.4, mesh=s),
        obj(15, "lamp", 0.15, 4.85, 0.3, 1.5, 0.3, mesh=s),                 # north wall
        obj(16, "other", 2.55, 4.80, 0.4, 0.5, 0.4, mesh=s),
        obj(17, "lamp", 0.15, 0.65, 0.3, 1.5, 0.3, mesh=s),                  # west wall
        obj(18, "lamp", 0.15, 2.07, 0.3, 1.5, 0.3, mesh=s),
        obj(19, "plant", 0.15, 3.10, 0.3, 1.0, 0.3, mesh=s),
        obj(20, "storage", 0.20, 4.20, 0.5, 1.0, 0.4, yaw_deg=90, mesh=s),
        obj(21, "plant", 3.88, 0.65, 0.24, 1.0, 0.24, mesh=s),   # east wall
        obj(22, "chair", 3.75, 1.50, 0.5, 0.9, 0.5, yaw_deg=90, mesh=s),
        obj(23, "lamp", 3.85, 3.40, 0.3, 1.5, 0.3, mesh=s),
        obj(24, "plant", 3.925, 0.15, 0.15, 1.0, 0.15, mesh=s),
    ]
    out.append(("budget_room_120k.json", "budget_room_120k", rectangle(4.0, 5.0), budget + more, (2.0, 2.5),
                [[door(1.65)], [], [], []]))

    # 2 x 2 m, empty: free floor 1.4 x 1.4 m, which selects standing mode (gate G5).
    out.append(("tiny_standing.json", "tiny_standing", rectangle(2.0, 2.0), [], (1.0, 1.0), None))
    return out


def build_all():
    built = []
    for (name, room_id, outline, objects, walker, walls) in fixtures():
        room = rm.build_room_model(room_id, outline, objects, walker, walls=walls)
        built.append((name, room))
    return built


def reference_row(name, room):
    c, g, a = room["curvature"], room["cells"]["glued"], room["cells"]["aligned"]
    floor, walls, band = rm.shell_triangles(room)
    return ("| `%s` | %.2f x %.2f | %.3f | %.4f | %.4f | %.4f | %d x %d, %.4f x %.4f | %d x %d, g = %.4f | %s | "
            "%d x %d | %d + %d | `%s` |") % (
        name, room["freeRect"]["wf"], room["freeRect"]["df"], room["lapSquare"]["side"],
        c["l5"], c["l6"], c["r3"], g["nx"], g["nz"], g["sizeX"], g["sizeZ"], a["n"], a["m"], a["g"],
        "yes" if room["standing"] else "no", room["sdf"]["width"], room["sdf"]["height"],
        floor, walls, rm.geometry_hash(room)[:16])


def main(argv):
    check = "--check" in argv
    table = "--table" in argv
    built = build_all()
    failures = 0
    if not check:
        os.makedirs(OUT_DIR, exist_ok=True)
    for (name, room) in built:
        text = rm.dump_room(room)
        path = os.path.join(OUT_DIR, name)
        if check:
            try:
                with open(path, "r", encoding="utf-8") as fh:
                    same = fh.read() == text
            except OSError:
                same = False
            print("%-24s %s" % (name, "up to date" if same else "DIFFERS"))
            failures += 0 if same else 1
        else:
            with open(path, "w", encoding="utf-8", newline="\n") as fh:
                fh.write(text)
            print("wrote %s (%d bytes)" % (path, len(text.encode("utf-8"))))
    if table:
        print()
        print("| Fixture | Wf x Df (m) | s (m) | L5 (m) | L6 (m) | R3 (m) | Glued cells | Aligned Cells | "
              "Standing | SDF grid | Shell triangles (floor + walls) | Geometry hash (first 16) |")
        print("|---|---|---|---|---|---|---|---|---|---|---|---|")
        for (name, room) in built:
            print(reference_row(name, room))
        print()
        print("Distance-field probes, metres (RoomSdf.Sample at floor points of the room frame):")
        print("| Fixture | (0, 0) | (Wf/2, 0) | (0, Df/2) | outline[0] | (Wf/2 + 0.30, 0) | (50, 0) |")
        print("|---|---|---|---|---|---|---|")
        for (name, room) in built:
            sdf_meta = room["sdf"]
            grid = rm.Grid(int(round(sdf_meta["originX"] / 0.025)), int(round(sdf_meta["originZ"] / 0.025)),
                           sdf_meta["width"], sdf_meta["height"])
            sdf = rm.decode_sdf(sdf_meta["data"], grid.width, grid.height)
            wf, df = room["freeRect"]["wf"], room["freeRect"]["df"]
            probes = [(0.0, 0.0), (0.5 * wf, 0.0), (0.0, 0.5 * df), tuple(room["outline"][0]),
                      (0.5 * wf + 0.30, 0.0), (50.0, 0.0)]
            print("| `%s` | %s |" % (name, " | ".join("%.4f" % rm.sdf_sample(grid, sdf, x, z) for (x, z) in probes)))
        print()
        for (name, room) in built:
            stand = [o for o in room["objects"] if o["mesh"] == "standIn"]
            if not stand:
                continue
            print("Stand-in objects of %s:" % name)
            print("| Id | Category | Base (x, y, z) | Size (w, h, d) | Yaw (rad) | Triangles | Vertices | Longest edge (m) |")
            print("|---|---|---|---|---|---|---|---|")
            total = 0
            for o in stand:
                verts, tris = rm.standin_ellipsoid(o["size"])
                total += len(tris)
                print("| %d | %s | %.2f, %.2f, %.2f | %.2f, %.2f, %.2f | %.6f | %d | %d | %.4f |" % (
                    o["id"], o["category"], o["base"][0], o["base"][1], o["base"][2],
                    o["size"][0], o["size"][1], o["size"][2], o["yaw"], len(tris), len(verts),
                    rm.longest_edge(verts, tris)))
            print("Total: %d triangles" % total)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
