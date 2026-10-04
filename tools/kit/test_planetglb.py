"""Tests of planetglb: the Small Planet tiles as kit pieces (frame, scale, colour, lines, checks, round trip).

Run: python -m pytest tools/kit -q
"""
from __future__ import annotations

import json
import math
import random

import pytest

import kitglb
import planetglb
from test_kitglb import accessor, parse

R_TEST = 2 * 3.6 / math.pi          # smallplanet_build.py at PLANET_SCALE 1.5: s = 3.6 m, R = 2s/pi


# ---- The two sets of frames, rebuilt independently ----------------------------------------------------------

def _mat(*rows):
    return [list(r) for r in rows]


def _mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def _col(m, j):
    return tuple(m[i][j] for i in range(3))


def unity_frames():
    """Atlas.Kernel.CubeTiling: frames[0] = I, frames[k + 1] = R^k T H, frames[5] = S_0 S_2 (rows as written)."""
    rot = _mat((0, -1, 0), (1, 0, 0), (0, 0, 1))
    t = _mat((0, 0, 1), (0, 1, 0), (-1, 0, 0))
    h = _mat((-1, 0, 0), (0, -1, 0), (0, 0, 1))
    rk = _mat((1, 0, 0), (0, 1, 0), (0, 0, 1))
    steps = []
    for _ in range(4):
        steps.append(_mul(_mul(rk, t), h))
        rk = _mul(rk, rot)
    frames = [_mat((1, 0, 0), (0, 1, 0), (0, 0, 1))] + steps + [_mul(steps[0], steps[2])]
    # chart x along column 0, chart y along column 1, tile centre column 2
    return [(_col(f, 2), _col(f, 0), _col(f, 1)) for f in frames]


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def blender_frames():
    """smallplanet_build.py TILE_DEFS: (centre n, chart x u, chart y v); side tiles have v = +Z (towards Home)."""
    def side(n):
        v = (0, 0, 1)
        return (n, _cross(v, n), v)
    return [((0, 0, 1), (1, 0, 0), (0, 1, 0)), side((1, 0, 0)), side((0, 1, 0)), side((-1, 0, 0)),
            side((0, -1, 0)), ((0, 0, -1), (1, 0, 0), (0, -1, 0))]


def ambient(frame, x, y):
    """The direction a chart point leaves the tile centre in (unnormalised tangent part)."""
    _, u, v = frame
    return tuple(x * u[i] + y * v[i] for i in range(3))


def test_face_order_matches_cube_tiling():
    for (nu, _, _), (nb, _, _) in zip(unity_frames(), blender_frames()):
        assert nu == pytest.approx(nb)


@pytest.mark.parametrize("tile", range(6))
def test_unity_chart_points_the_same_way_on_the_sphere(tile):
    rng = random.Random(tile)
    for _ in range(20):
        x, y = rng.uniform(-1, 1), rng.uniform(-1, 1)
        xu, yu = planetglb.unity_chart(tile, x, y)
        assert ambient(unity_frames()[tile], xu, yu) == pytest.approx(ambient(blender_frames()[tile], x, y))


def test_side_tiles_turn_a_quarter_and_home_and_beacon_do_not():
    assert planetglb.unity_chart(0, 1.0, 2.0) == (1.0, 2.0)
    assert planetglb.unity_chart(5, 1.0, 2.0) == (1.0, 2.0)
    for tile in (1, 2, 3, 4):
        assert planetglb.unity_chart(tile, 1.0, 2.0) == (2.0, -1.0)
        # Blender's +Y (towards Home) is Unity's +X on every side tile
        assert planetglb.unity_chart(tile, 0.0, 1.0) == (1.0, 0.0)
    with pytest.raises(ValueError):
        planetglb.unity_chart(6, 0.0, 0.0)


def test_to_unity_scales_the_chart_by_the_radius_and_keeps_heights_in_metres():
    assert planetglb.to_unity(0, (1.0, 2.0, 0.5), 2.0) == (0.5, 0.5, 1.0)
    assert planetglb.to_unity(1, (1.0, 2.0, 0.5), 2.0) == (1.0, 0.5, -0.5)


def test_normals_turn_with_the_chart_and_stay_unit():
    assert planetglb.normal_to_unity(0, (0.0, 0.0, 1.0)) == (0.0, 1.0, 0.0)
    assert planetglb.normal_to_unity(0, (0.0, 2.0, 0.0)) == (0.0, 0.0, 1.0)
    assert planetglb.normal_to_unity(2, (0.0, 1.0, 0.0)) == (1.0, 0.0, 0.0)
    assert planetglb.normal_to_unity(4, (1.0, 0.0, 0.0)) == (0.0, 0.0, -1.0)


# ---- Palette and colour -------------------------------------------------------------------------------------

@pytest.mark.parametrize("cell, hexcode", [(29, "#C9A227"), (193, "#FFD166"), (116, "#5EBD5E"), (100, "#4A944A"),
                                           (16, "#1B222B"), (121, "#8E5EBD"), (133, "#305743")])
def test_palette_port_matches_atlas_palette(cell, hexcode):
    assert planetglb.hex_srgb(planetglb.palette_linear(cell)) == hexcode


def test_oklab_of_white_and_black():
    assert planetglb.oklab((1.0, 1.0, 1.0)) == pytest.approx((1.0, 0.0, 0.0), abs=1e-4)
    assert planetglb.oklab((0.0, 0.0, 0.0)) == pytest.approx((0.0, 0.0, 0.0), abs=1e-9)


@pytest.mark.parametrize("cell", [116, 100, 29, 113, 126, 109, 94, 133, 27, 127, 121])
def test_a_palette_colour_matches_its_own_cell(cell):
    for f in (1.0, 0.95, 1.05):
        rgb = tuple(c * f for c in planetglb.palette_linear(cell))
        got = planetglb.match_colour(rgb)
        assert planetglb.oklab_distance(rgb, tuple(c * got.gain for c in planetglb.palette_linear(got.cell))) < 1e-6
        assert got.error < 1e-6
        if got.cell == cell:
            assert got.gain == pytest.approx(f, rel=1e-6)


def test_the_search_never_picks_an_emissive_cell():
    near_beacon = planetglb.palette_linear(planetglb.BEACON_CELL)
    got = planetglb.match_colour(near_beacon)
    assert got.cell < 16 * planetglb.EMISSIVE_ROW
    assert got.error > 0


def test_a_given_cell_is_kept_and_only_its_gain_fitted():
    rgb = tuple(0.97 * c for c in planetglb.palette_linear(116))
    got = planetglb.match_colour(rgb, 29)
    assert got.cell == 29 and got.error > 0.05
    got = planetglb.match_colour(rgb, 116)
    assert got.cell == 116 and got.gain == pytest.approx(0.97) and got.error < 1e-6


# ---- Lines and radius ---------------------------------------------------------------------------------------

def test_ground_lines_run_zero_to_one_across_the_tile():
    h = planetglb.HALF_WIDTH
    assert planetglb.ground_lines((0.0, 0.0, 0.0)) == (0.5, 0.5)
    assert planetglb.ground_lines((h, 0.0, 0.0)) == pytest.approx((1.0, 0.5))
    assert planetglb.ground_lines((0.0, 0.0, -h)) == pytest.approx((0.5, 0.0))
    corner = math.atan(math.sqrt(2)) / math.sqrt(2)
    assert planetglb.ground_lines((corner, 0.0, corner)) == pytest.approx((1.0, 1.0))


def chart_from_gnomonic(kx, ky, radius):
    m = math.hypot(kx, ky)
    if m < 1e-12:
        return 0.0, 0.0
    d = radius * math.atan(m)
    return d * kx / m, d * ky / m


def ground_faces(n, radius, cell=116):
    """Blender's ground sheet (smallplanet_build.py ground()): an n x n gnomonic grid, two triangles a cell."""
    ks = [-1 + 2 * i / n for i in range(n + 1)]
    faces = []
    for i in range(n):
        for j in range(n):
            p = [(*chart_from_gnomonic(ks[a], ks[b], radius), 0.0) for a, b in [(i, j), (i + 1, j), (i + 1, j + 1),
                                                                                  (i, j + 1)]]
            for t in ((p[0], p[1], p[2]), (p[0], p[2], p[3])):
                faces.append(planetglb.Face(triangles=[t], normal=(0.0, 0.0, 1.0),
                                            rgb=tuple(0.98 * c for c in planetglb.palette_linear(cell)),
                                            ground=True, source="ground"))
    return faces


def test_radius_comes_back_from_the_ground_sheet():
    for n in (16, 18):
        points = [p for f in ground_faces(n, R_TEST) for t in f.triangles for p in t]
        assert planetglb.radius_from_ground(points) == pytest.approx(R_TEST, rel=1e-12)


# ---- A tile, end to end -------------------------------------------------------------------------------------

def box_faces(cx, cy, size, cell):
    """A closed box on the ground in Blender's frame, counter-clockwise fronts, as Faces (two triangles each)."""
    x0, x1, y0, y1, z0, z1 = cx - size, cx + size, cy - size, cy + size, 0.0, 2 * size
    quads = {
        (0, 0, -1): [(x0, y0, z0), (x0, y1, z0), (x1, y1, z0), (x1, y0, z0)],
        (0, 0, 1): [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)],
        (0, -1, 0): [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)],
        (0, 1, 0): [(x1, y1, z0), (x0, y1, z0), (x0, y1, z1), (x1, y1, z1)],
        (-1, 0, 0): [(x0, y1, z0), (x0, y0, z0), (x0, y0, z1), (x0, y1, z1)],
        (1, 0, 0): [(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)],
    }
    return [planetglb.Face(triangles=[(q[0], q[1], q[2]), (q[0], q[2], q[3])], normal=n,
                           rgb=planetglb.palette_linear(cell), cell=cell, source="box") for n, q in quads.items()]


def lantern_faces(cz):
    r = 0.1
    c = (0.0, 0.0, cz)
    pts = [(r, 0, 0), (0, r, 0), (-r, 0, 0), (0, -r, 0)]
    faces = []
    for k in range(4):
        a, b = pts[k], pts[(k + 1) % 4]
        for z, tri in ((r, (a, b)), (-r, (b, a))):
            corners = tuple(tuple(c[i] + p[i] for i in range(3)) for p in (tri[0], tri[1], (0, 0, z)))
            n = _cross(tuple(corners[1][i] - corners[0][i] for i in range(3)),
                       tuple(corners[2][i] - corners[0][i] for i in range(3)))
            length = math.sqrt(sum(v * v for v in n))
            faces.append(planetglb.Face(triangles=[corners], normal=tuple(v / length for v in n),
                                        rgb=(1.0, 1.0, 1.0), lantern=True, source="lantern"))
    return faces


def _sub(a, b):
    return tuple(a[i] - b[i] for i in range(3))


def _dot(a, b):
    return sum(a[i] * b[i] for i in range(3))


@pytest.mark.parametrize("tile", [0, 1, 5])
def test_a_tile_survives_the_round_trip(tile):
    n = 4
    faces = ground_faces(n, R_TEST) + box_faces(0.6, -0.4, 0.1, 109) + lantern_faces(0.5)
    piece, stats = planetglb.build_tile(tile, faces, R_TEST)
    planetglb.check_tile("tile", piece)
    assert stats.triangles == 2 * n * n + 12 + 8
    assert stats.cells == {116: 2 * n * n, 109: 12, planetglb.BEACON_CELL: 8}
    assert stats.worst_error < 0.01

    doc, blob = parse(kitglb.write_glb(piece))
    primitive = doc["meshes"][0]["primitives"][0]
    attributes = primitive["attributes"]
    _, positions = accessor(doc, blob, attributes["POSITION"])
    _, normals = accessor(doc, blob, attributes["NORMAL"])
    _, uv0 = accessor(doc, blob, attributes["TEXCOORD_0"])
    _, uv1 = accessor(doc, blob, attributes["TEXCOORD_1"])
    _, colours = accessor(doc, blob, attributes["COLOR_0"])
    _, indices = accessor(doc, blob, primitive["indices"])
    assert len(positions) == stats.vertices and len(indices) == 3 * stats.triangles

    # back in Unity's frame as GlbMeshReader reads it: x negated back, v flipped back
    unity = [(-p[0], p[1], p[2]) for p in positions]
    h = planetglb.HALF_WIDTH
    assert max(max(abs(p[0]), abs(p[2])) for p in unity) == pytest.approx(h, rel=1e-6)
    assert min(p[1] for p in unity) == pytest.approx(0.0) and max(p[1] for p in unity) == pytest.approx(0.6, abs=1e-6)
    cells = {(round(u * 16 - 0.5), round((1.0 - v) * 16 - 0.5)) for u, v in uv0}
    assert {c + 16 * r for c, r in cells} == {116, 109, planetglb.BEACON_CELL}
    assert all(c[1:] == (255, 255, 255) for c in colours)

    # the box sits where Blender put it, turned into Unity's chart and scaled by 1/R
    xu, yu = planetglb.unity_chart(tile, 0.6, -0.4)
    box = [p for p, t in zip(unity, uv0) if (round(t[0] * 16 - 0.5), round((1 - t[1]) * 16 - 0.5)) == (13, 6)]
    centre = tuple(sum(p[i] for p in box) / len(box) for i in range(3))
    assert centre == pytest.approx((xu / R_TEST, 0.1, yu / R_TEST), abs=1e-6)

    # ground lines reach the tile edges; props carry the constant (0.5, 0.5)
    ground_uv1 = [(t[0], 1.0 - t[1]) for t, c in zip(uv1, uv0) if (round(c[0] * 16 - 0.5), round((1 - c[1]) * 16 - 0.5)) == (4, 7)]
    assert min(u for u, _ in ground_uv1) == pytest.approx(0.0, abs=1e-6)
    assert max(v for _, v in ground_uv1) == pytest.approx(1.0, abs=1e-6)

    # glTF fronts are counter-clockwise: cross(p1 - p0, p2 - p0) points along the stored normal
    for k in range(0, len(indices), 3):
        p0, p1, p2 = (positions[i] for i in indices[k:k + 3])
        # positions are in units of R horizontally; undo that before comparing with the metric normal
        q0, q1, q2 = ((p[0] * R_TEST, p[1], p[2] * R_TEST) for p in (p0, p1, p2))
        assert _dot(_cross(_sub(q1, q0), _sub(q2, q0)), normals[indices[k]]) > 0

    # the up-facing ground carries the look's light times the authored brightness (0.98)
    up_shade = kitglb.face_shade((0.0, 1.0, 0.0)) * 0.98
    ground_bytes = {c[0] for c, t in zip(colours, uv0) if (round(t[0] * 16 - 0.5), round((1 - t[1]) * 16 - 0.5)) == (4, 7)}
    assert ground_bytes == {round(255 * up_shade)}


def test_home_and_a_side_tile_differ_only_by_the_quarter_turn():
    faces = box_faces(0.6, -0.4, 0.1, 109)
    home, _ = planetglb.build_tile(0, faces, R_TEST)
    east, _ = planetglb.build_tile(1, faces, R_TEST)
    for a, b in zip(home.positions, east.positions):
        assert b == pytest.approx((a[2], a[1], -a[0]))


def test_check_tile_accepts_an_open_sheet_and_refuses_what_cannot_ship():
    piece, _ = planetglb.build_tile(0, ground_faces(4, R_TEST), R_TEST)
    with pytest.raises(ValueError, match="open edge"):
        kitglb.check_piece("tile0", piece, 10000)           # what the Glued check would say
    planetglb.check_tile("tile0", piece)
    with pytest.raises(ValueError, match="tile0.*32 triangles.*31"):
        planetglb.check_tile("tile0", piece, 31)
    piece.positions[0] = (1.2, 0.0, 0.0)
    with pytest.raises(ValueError, match="tile0.*off the tile"):
        planetglb.check_tile("tile0", piece)
    with pytest.raises(ValueError, match="empty"):
        planetglb.check_tile("tile1", kitglb.Piece())


def test_manifest_keeps_kitsets_pieces_and_adds_the_planet():
    piece, _ = planetglb.build_tile(0, ground_faces(2, R_TEST), R_TEST)
    files = {"tile0": kitglb.write_glb(piece)}
    doc = json.loads(planetglb.write_manifest(files, {"R0": 2.5}).decode("utf-8"))
    assert doc["pieces"] == json.loads(kitglb.write_manifest(files).decode("utf-8"))["pieces"]
    assert doc["planet"] == {"R0": 2.5}
    assert planetglb.write_manifest(files, {"R0": 2.5}) == planetglb.write_manifest(files, {"R0": 2.5})
