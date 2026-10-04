#!/usr/bin/env python3
"""Validate RoomModel JSON files (docs/contracts/lane-c-room.md, section 2.7).

    python3 validate_room.py FILE [FILE ...]     validate the files named
    python3 validate_room.py --fixtures          validate the five G0 fixtures and check
                                                 their reference values
    python3 validate_room.py -q FILE ...         print failures only

Two layers: the JSON Schema docs/contracts/roommodel.schema.json (a draft 2020-12 subset is
implemented here), then the consistency rules R1 to R20 that a schema cannot express.
Exit code 0 when every file passes, 1 otherwise. Python 3 standard library only.
"""

import json
import math
import os
import re
import sys

import roommath as rm

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
SCHEMA_PATH = os.path.join(REPO, "docs", "contracts", "roommodel.schema.json")
FIXTURE_DIR = os.path.join(REPO, "ATLAS", "Assets", "Atlas", "World", "Resources", "AtlasRooms")
MAX_FILE_BYTES = 300 * 1000        # plan section 7.4: RoomModel JSON <= 300 kB
TOL = 1e-4                         # metres; the files carry six decimals

# Reference values of the fixtures: (wf, df, s, l5, l6, r3, glued, aligned, standing, objects).
# sample_3x3 and the empty 4 x 5 m rectangle are the rows of plan section 3.2.
FIXTURES = {
    "sample_3x3":       (2.4, 2.4, 2.0, 1.884526, 1.518651, 1.273240, (2, 2, 1.2, 1.2), (3, 3, 0.8), False, 0),
    "sample_L_3x4":     (1.9, 2.4, 1.5, 1.413394, 1.138989, 0.954930, (1, 2, 1.9, 1.2), (2, 2, 0.95), False, 0),
    "sample_4x5_table": (1.6, 3.4, 1.2, 1.130715, 0.911191, 0.763944, (1, 2, 1.6, 1.7), (2, 2, 0.8), False, 1),
    "budget_room":      (2.4, 3.4, 2.0, 1.884526, 1.518651, 1.273240, (2, 2, 1.2, 1.7), (3, 4, 0.8), False, 12),
    "tiny_standing":    (1.4, 1.4, 1.0, 0.942263, 0.759326, 0.636620, (1, 1, 1.4, 1.4), (2, 2, 0.7), True, 0),
}


# --------------------------------------------------------------------------------------
# JSON Schema, the subset the RoomModel schema uses
# --------------------------------------------------------------------------------------

def _type_ok(value, name):
    if name == "object":
        return isinstance(value, dict)
    if name == "array":
        return isinstance(value, list)
    if name == "string":
        return isinstance(value, str)
    if name == "boolean":
        return isinstance(value, bool)
    if name == "null":
        return value is None
    if name == "integer":
        return (isinstance(value, int) and not isinstance(value, bool)) or \
               (isinstance(value, float) and value.is_integer())
    if name == "number":
        return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)
    raise ValueError("schema uses an unsupported type: %r" % name)


SUPPORTED = {
    "$schema", "$id", "$ref", "$defs", "title", "description", "type", "const", "enum", "required",
    "properties", "additionalProperties", "items", "minItems", "maxItems", "minimum", "maximum",
    "exclusiveMinimum", "exclusiveMaximum", "minLength", "maxLength", "pattern",
}


def schema_errors(value, schema, root, path="$"):
    """Yield one message per violation of `schema` by `value`."""
    unknown = set(schema.keys()) - SUPPORTED
    if unknown:
        raise ValueError("schema keyword not supported by this validator: %s" % sorted(unknown))
    if "$ref" in schema:
        ref = schema["$ref"]
        if not ref.startswith("#/"):
            raise ValueError("only local references are supported: %s" % ref)
        target = root
        for part in ref[2:].split("/"):
            target = target[part]
        yield from schema_errors(value, target, root, path)
    if "type" in schema:
        names = schema["type"] if isinstance(schema["type"], list) else [schema["type"]]
        if not any(_type_ok(value, n) for n in names):
            yield "%s: expected %s" % (path, " or ".join(names))
            return
    if "const" in schema and value != schema["const"]:
        yield "%s: must be %r" % (path, schema["const"])
    if "enum" in schema and value not in schema["enum"]:
        yield "%s: %r is not one of %s" % (path, value, schema["enum"])
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        if "minimum" in schema and value < schema["minimum"]:
            yield "%s: %r is below the minimum %r" % (path, value, schema["minimum"])
        if "maximum" in schema and value > schema["maximum"]:
            yield "%s: %r is above the maximum %r" % (path, value, schema["maximum"])
        if "exclusiveMinimum" in schema and value <= schema["exclusiveMinimum"]:
            yield "%s: %r must be greater than %r" % (path, value, schema["exclusiveMinimum"])
        if "exclusiveMaximum" in schema and value >= schema["exclusiveMaximum"]:
            yield "%s: %r must be less than %r" % (path, value, schema["exclusiveMaximum"])
    if isinstance(value, str):
        if "minLength" in schema and len(value) < schema["minLength"]:
            yield "%s: shorter than %d characters" % (path, schema["minLength"])
        if "maxLength" in schema and len(value) > schema["maxLength"]:
            yield "%s: longer than %d characters" % (path, schema["maxLength"])
        if "pattern" in schema and re.search(schema["pattern"], value) is None:
            shown = value if len(value) <= 40 else value[:37] + "..."
            yield "%s: %r does not match %s" % (path, shown, schema["pattern"])
    if isinstance(value, list):
        if "minItems" in schema and len(value) < schema["minItems"]:
            yield "%s: fewer than %d items" % (path, schema["minItems"])
        if "maxItems" in schema and len(value) > schema["maxItems"]:
            yield "%s: more than %d items" % (path, schema["maxItems"])
        if "items" in schema:
            for k, item in enumerate(value):
                yield from schema_errors(item, schema["items"], root, "%s[%d]" % (path, k))
    if isinstance(value, dict):
        for key in schema.get("required", []):
            if key not in value:
                yield "%s: missing key %r" % (path, key)
        props = schema.get("properties", {})
        for key, item in value.items():
            if key in props:
                yield from schema_errors(item, props[key], root, "%s.%s" % (path, key))
            elif schema.get("additionalProperties", True) is False:
                yield "%s: unknown key %r" % (path, key)


# --------------------------------------------------------------------------------------
# Consistency rules
# --------------------------------------------------------------------------------------

def near(a, b, tol=TOL):
    return abs(a - b) <= tol


def is_multiple(value, step, tol=1e-6):
    q = value / step
    return abs(q - round(q)) <= tol / step


def grid_of(room):
    sdf = room["sdf"]
    return rm.Grid(int(round(sdf["originX"] / (0.5 * rm.CELL))), int(round(sdf["originZ"] / (0.5 * rm.CELL))),
                   sdf["width"], sdf["height"])


def consistency_errors(room):
    """Rules R1 to R20. Returns (errors, warnings)."""
    err, warn = [], []
    sdf_meta = room["sdf"]
    wf, df = room["freeRect"]["wf"], room["freeRect"]["df"]
    outline = [tuple(p) for p in room["outline"]]

    # R1 outline: simple, counter-clockwise, no edge under 0.05 m
    if rm.polygon_area(outline) <= 0:
        err.append("R1 outline is not counter-clockwise (signed area %.4f)" % rm.polygon_area(outline))
    if not rm.polygon_is_simple(outline):
        err.append("R1 outline crosses itself")
    for k in range(len(outline)):
        if math.dist(outline[k], outline[(k + 1) % len(outline)]) < rm.CELL - 1e-9:
            err.append("R1 outline edge %d is shorter than 0.05 m" % k)

    # R2 grid: origin on the 0.025 m lattice; covers the outline with a 0.25 m margin
    for key in ("originX", "originZ"):
        if not is_multiple(sdf_meta[key], 0.5 * rm.CELL):
            err.append("R2 sdf.%s = %r is not a multiple of 0.025 m" % (key, sdf_meta[key]))
    grid = grid_of(room)
    x_hi = grid.origin_x + grid.width * rm.CELL
    z_hi = grid.origin_z + grid.height * rm.CELL
    margin = rm.GRID_MARGIN_CELLS * rm.CELL
    xs, zs = [p[0] for p in outline], [p[1] for p in outline]
    if (min(xs) - grid.origin_x < margin - 1e-6 or x_hi - max(xs) < margin - 1e-6 or
            min(zs) - grid.origin_z < margin - 1e-6 or z_hi - max(zs) < margin - 1e-6):
        err.append("R2 the grid does not reach 0.25 m beyond the outline on every side")

    # R3 data: length, sign mask
    try:
        sdf = rm.decode_sdf(sdf_meta["data"], grid.width, grid.height)
    except Exception as exc:                     # bad base64 or wrong length
        err.append("R3 sdf.data: %s" % exc)
        return err, warn
    free = grid.new_mask()
    for j in range(grid.height):
        for i in range(grid.width):
            if sdf[j][i] > 0:
                free[j][i] = 1
            elif sdf[j][i] == 0:
                err.append("R3 sdf cell (%d, %d) is exactly 0; the field is never 0 at a cell centre" % (i, j))

    # R4 free cells lie inside the outline and not on the rim of the grid
    bad_outside = 0
    for j in range(grid.height):
        for i in range(grid.width):
            if free[j][i]:
                x, z = grid.centre(i, j)
                if not rm.point_in_polygon(x, z, outline):
                    bad_outside += 1
                if i == 0 or j == 0 or i == grid.width - 1 or j == grid.height - 1:
                    err.append("R4 free cell (%d, %d) on the rim of the grid" % (i, j))
    if bad_outside:
        err.append("R4 %d free cells lie outside the outline" % bad_outside)

    # R5 the field is the exact distance transform of its own sign
    expect = rm.signed_distance(grid, free)
    worst = 0.0
    for j in range(grid.height):
        for i in range(grid.width):
            e = max(-32.768, min(32.767, math.floor(expect[j][i] * 1000.0 + 0.5) / 1000.0))
            worst = max(worst, abs(e - sdf[j][i]))
    if worst > 0.0015:
        err.append("R5 sdf differs from the distance transform of its sign by up to %.4f m" % worst)

    # R6 free rectangle: ordered, on the lattice
    if wf > df + TOL:
        err.append("R6 freeRect.wf (%.3f) is larger than freeRect.df (%.3f)" % (wf, df))
    for key, v in (("wf", wf), ("df", df)):
        if not is_multiple(v, rm.CELL):
            err.append("R6 freeRect.%s = %r is not a multiple of 0.05 m" % (key, v))

    # R7 walkable region: the component of {sdf > 0.25} at the origin, and the rectangle inside it
    seed = (-0.25 * rm.CELL, -0.25 * rm.CELL)
    walk = rm.walk_component(grid, sdf, seed)
    si, sj = grid.cell_of(*seed)
    if not (grid.contains(si, sj) and walk[sj][si]):
        err.append("R7 the origin is not inside the walkable region")
    missing = 0
    for j in range(grid.height):
        for i in range(grid.width):
            x, z = grid.centre(i, j)
            if abs(x) < 0.5 * wf and abs(z) < 0.5 * df and not walk[j][i]:
                missing += 1
    if missing:
        err.append("R7 %d cells of the free rectangle are not walkable" % missing)

    # R8 the free rectangle is the best one: short side first (up to 3.6 m), then area
    side_cells = rm.largest_square(grid, walk)
    cap_cells = int(round(rm.RECT_SIDE_CAP / rm.CELL))
    need = min(side_cells, cap_cells)
    best_area = 0
    for (i0, j0, i1, j1) in rm.maximal_rectangles(grid, walk):
        if min(i1 - i0, j1 - j0) >= need:
            best_area = max(best_area, (i1 - i0) * (j1 - j0))
    wf_cells, df_cells = int(round(wf / rm.CELL)), int(round(df / rm.CELL))
    if min(wf_cells, df_cells) < need:
        err.append("R8 a rectangle with a short side of %.2f m fits; freeRect has %.2f m" % (
            need * rm.CELL, min(wf, df)))
    elif wf_cells * df_cells != best_area and not missing:
        err.append("R8 freeRect area is %.4f m2; the best rectangle has %.4f m2" % (
            wf * df, best_area * rm.CELL * rm.CELL))

    # R9 walkable polygon: winding, lattice, and equal to the walkable cells
    outer = [tuple(p) for p in room["walkable"]["outer"]]
    holes = [[tuple(p) for p in h] for h in room["walkable"]["holes"]]
    if rm.polygon_area(outer) <= 0:
        err.append("R9 walkable.outer is not counter-clockwise")
    for k, h in enumerate(holes):
        if rm.polygon_area(h) >= 0:
            err.append("R9 walkable.holes[%d] is not clockwise" % k)
    for ring in [outer] + holes:
        for (x, z) in ring:
            if not (is_multiple(x - grid.origin_x, rm.CELL) and is_multiple(z - grid.origin_z, rm.CELL)):
                err.append("R9 walkable vertex (%.3f, %.3f) is not on a cell corner" % (x, z))
                break
    mismatch = 0
    for j in range(grid.height):
        for i in range(grid.width):
            x, z = grid.centre(i, j)
            inside = rm.point_in_polygon(x, z, outer) and not any(rm.point_in_polygon(x, z, h) for h in holes)
            if inside != bool(walk[j][i]):
                mismatch += 1
    if mismatch:
        err.append("R9 walkable polygon and {sdf > 0.25 m} disagree on %d cells" % mismatch)

    # R10 lap square, R11 curvature, R12 cells, R13 standing
    lf = rm.loop_fit(wf, df)
    if not near(room["lapSquare"]["side"], lf["s"]):
        err.append("R10 lapSquare.side = %r, expected %.6f" % (room["lapSquare"]["side"], lf["s"]))
    for key in ("l5", "l6", "r3"):
        if not near(room["curvature"][key], lf[key]):
            err.append("R11 curvature.%s = %r, expected %.6f" % (key, room["curvature"][key], lf[key]))
    if not near(room["curvature"]["floorL"], rm.FLOOR_L) or not near(room["curvature"]["floorR"], rm.FLOOR_R):
        warn.append("curvature floors (%.2f, %.2f) differ from the constants in force (%.2f, %.2f)" % (
            room["curvature"]["floorL"], room["curvature"]["floorR"], rm.FLOOR_L, rm.FLOOR_R))
    g, a = rm.glued_cells(wf, df), rm.aligned_cells(wf, df)
    gj, aj = room["cells"]["glued"], room["cells"]["aligned"]
    if (gj["nx"], gj["nz"]) != (g["nx"], g["nz"]) or not near(gj["sizeX"], g["sizeX"]) or not near(gj["sizeZ"], g["sizeZ"]):
        err.append("R12 cells.glued = %r, expected %d x %d of %.4f x %.4f" % (gj, g["nx"], g["nz"], g["sizeX"], g["sizeZ"]))
    if (aj["n"], aj["m"]) != (a["n"], a["m"]) or not near(aj["g"], a["g"]):
        err.append("R12 cells.aligned = %r, expected %d x %d, g = %.4f" % (aj, a["n"], a["m"], a["g"]))
    if room["standing"] != rm.is_standing(wf, df):
        err.append("R13 standing = %r, expected %r" % (room["standing"], rm.is_standing(wf, df)))

    # R14 walls lie on outline edges and run the same way; R15 openings fit and do not overlap
    n = len(outline)
    for k, wall in enumerate(room["walls"]):
        a_pt, b_pt = tuple(wall["a"]), tuple(wall["b"])
        length = math.dist(a_pt, b_pt)
        if length < rm.CELL - 1e-9:
            err.append("R14 walls[%d] is shorter than 0.05 m" % k)
            continue
        on_edge = False
        for e in range(n):
            p, q = outline[e], outline[(e + 1) % n]
            if (rm.point_segment_distance(a_pt[0], a_pt[1], p[0], p[1], q[0], q[1]) <= 0.01 and
                    rm.point_segment_distance(b_pt[0], b_pt[1], p[0], p[1], q[0], q[1]) <= 0.01 and
                    (b_pt[0] - a_pt[0]) * (q[0] - p[0]) + (b_pt[1] - a_pt[1]) * (q[1] - p[1]) > 0):
                on_edge = True
        if not on_edge:
            err.append("R14 walls[%d] does not lie on an outline edge, or runs against it" % k)
        spans = []
        for m, o in enumerate(wall["openings"]):
            if o["offset"] + o["width"] > length + TOL:
                err.append("R15 walls[%d].openings[%d] runs past the end of the wall" % (k, m))
            if not (o["bottom"] < o["top"] <= wall["height"] + TOL):
                err.append("R15 walls[%d].openings[%d] needs bottom < top <= height" % (k, m))
            if o["kind"] == "door" and o["bottom"] > TOL:
                err.append("R15 walls[%d].openings[%d]: a door starts at the floor" % (k, m))
            spans.append((o["offset"], o["offset"] + o["width"]))
        spans.sort()
        for (s0, e0), (s1, e1) in zip(spans, spans[1:]):
            if s1 < e0 - TOL:
                err.append("R15 walls[%d] has overlapping openings" % k)

    # R16 objects: unique ids, hash only when ready; R17 triangle targets; R18 not on free floor
    ids = [o["id"] for o in room["objects"]]
    if len(set(ids)) != len(ids):
        err.append("R16 object ids are not unique")
    for o in room["objects"]:
        if (o["mesh"] == "ready") != (o["assetHash"] != ""):
            err.append("R16 object %d: assetHash must be set exactly when mesh is ready" % o["id"])
        if not (x_hi > o["base"][0] > grid.origin_x and z_hi > o["base"][2] > grid.origin_z):
            err.append("R16 object %d lies outside the grid" % o["id"])
        if o["base"][1] < -0.05:
            err.append("R16 object %d starts below the floor" % o["id"])
    stand_ins = [o for o in room["objects"] if o["mesh"] == "standIn"]
    if stand_ins:
        if len(stand_ins) != len(room["objects"]):
            err.append("R17 stand-in objects cannot be mixed with other mesh states")
        for o in stand_ins:
            if o["triTarget"] != rm.TRI_TARGET_MAX:
                err.append("R17 stand-in object %d must have triTarget 5000" % o["id"])
            verts, tris = rm.standin_ellipsoid(o["size"])
            edge = rm.longest_edge(verts, tris)
            if len(tris) != rm.TRI_TARGET_MAX or edge > rm.MAX_EDGE:
                err.append("R17 stand-in object %d: %d triangles, longest edge %.3f m" % (o["id"], len(tris), edge))
    else:
        expected = rm.assign_tri_targets(room["objects"])
        for o in room["objects"]:
            if o["triTarget"] != expected[o["id"]]:
                err.append("R17 object %d: triTarget = %d, expected %d" % (o["id"], o["triTarget"], expected[o["id"]]))
    for o in room["objects"]:
        box = rm.Box(o["base"], o["size"], o["yaw"])
        if not box.blocks_floor():
            continue
        covered = 0
        reach = 0.5 * math.hypot(o["size"][0], o["size"][2]) + rm.CELL
        i_lo, j_lo = grid.cell_of(o["base"][0] - reach, o["base"][2] - reach)
        i_hi, j_hi = grid.cell_of(o["base"][0] + reach, o["base"][2] + reach)
        for j in range(max(0, j_lo), min(grid.height, j_hi + 1)):
            for i in range(max(0, i_lo), min(grid.width, i_hi + 1)):
                x, z = grid.centre(i, j)
                if free[j][i] and box.overlaps_cell(x, z):
                    covered += 1
        if covered:
            err.append("R18 object %d stands on %d free cells" % (o["id"], covered))

    # R19 anchor: fixtures and unanchored rooms sit at the session origin
    if room["anchor"]["id"] == "" and room["quality"]["route"] != 0:
        warn.append("anchor.id is empty for a scanned room")
    return err, warn


def fixture_errors(room):
    """R20: a file whose id names a G0 fixture must match its reference values."""
    ref = FIXTURES.get(room.get("id"))
    if ref is None:
        return []
    wf, df, s, l5, l6, r3, glued, aligned, standing, count = ref
    got = (room["freeRect"]["wf"], room["freeRect"]["df"], room["lapSquare"]["side"],
           room["curvature"]["l5"], room["curvature"]["l6"], room["curvature"]["r3"])
    out = []
    for name, g, e in zip(("wf", "df", "s", "l5", "l6", "r3"), got, (wf, df, s, l5, l6, r3)):
        if not near(g, e, 2e-6 if name in ("l5", "l6", "r3") else TOL):
            out.append("R20 fixture %s: %s = %r, reference %r" % (room["id"], name, g, e))
    gj, aj = room["cells"]["glued"], room["cells"]["aligned"]
    if (gj["nx"], gj["nz"]) != glued[:2] or not near(gj["sizeX"], glued[2]) or not near(gj["sizeZ"], glued[3]):
        out.append("R20 fixture %s: glued cells differ from the reference" % room["id"])
    if (aj["n"], aj["m"]) != aligned[:2] or not near(aj["g"], aligned[2]):
        out.append("R20 fixture %s: aligned cells differ from the reference" % room["id"])
    if room["standing"] != standing:
        out.append("R20 fixture %s: standing differs from the reference" % room["id"])
    if len(room["objects"]) != count:
        out.append("R20 fixture %s: %d objects, reference %d" % (room["id"], len(room["objects"]), count))
    if room["id"] == "budget_room":
        if any(o["mesh"] != "standIn" for o in room["objects"]):
            out.append("R20 budget_room: every object must be a stand-in")
        if sum(o["triTarget"] for o in room["objects"]) != rm.FURNITURE_TRIANGLES:
            out.append("R20 budget_room: stand-ins must total 60,000 triangles")
    return out


def validate_file(path, schema):
    """Returns (errors, warnings, room or None)."""
    try:
        size = os.path.getsize(path)
        with open(path, "r", encoding="utf-8") as fh:
            room = json.load(fh)
    except (OSError, ValueError) as exc:
        return ["cannot read: %s" % exc], [], None
    errors = []
    if size > MAX_FILE_BYTES:
        errors.append("file is %d bytes; the budget is %d" % (size, MAX_FILE_BYTES))
    errors.extend(schema_errors(room, schema, schema))
    if errors:
        return errors, [], room            # the consistency rules need a well-formed document
    more, warnings = consistency_errors(room)
    errors.extend(more)
    errors.extend(fixture_errors(room))
    return errors, warnings, room


def main(argv):
    quiet = "-q" in argv
    args = [a for a in argv if a not in ("-q", "--fixtures")]
    paths = list(args)
    if "--fixtures" in argv:
        paths += [os.path.join(FIXTURE_DIR, name + ".json") for name in FIXTURES]
    if not paths:
        print(__doc__.strip())
        return 1
    with open(SCHEMA_PATH, "r", encoding="utf-8") as fh:
        schema = json.load(fh)
    failed = 0
    for path in paths:
        errors, warnings, room = validate_file(path, schema)
        name = os.path.basename(path)
        if errors:
            failed += 1
            print("FAIL  %s" % name)
            for e in errors[:40]:
                print("      %s" % e)
            if len(errors) > 40:
                print("      ... and %d more" % (len(errors) - 40))
        elif not quiet:
            fr, c = room["freeRect"], room["curvature"]
            print("PASS  %-24s Wf x Df = %.2f x %.2f  s = %.3f  L5 = %.4f  L6 = %.4f  R3 = %.4f  standing = %s  "
                  "objects = %d  hash = %s" % (
                      name, fr["wf"], fr["df"], room["lapSquare"]["side"], c["l5"], c["l6"], c["r3"],
                      "yes" if room["standing"] else "no", len(room["objects"]), rm.geometry_hash(room)[:16]))
        for w in warnings:
            print("      warning: %s" % w)
    total = len(paths)
    print("%d of %d files valid" % (total - failed, total))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
