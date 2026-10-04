import time

import numpy as np
import pytest

from forge import colour, palette
from forge.colour import ColourView, dominant_cell, face_cells, split_by_cell

RED, YELLOW, GREEN, BLUE, MAGENTA = (palette.entry(i) for i in (112, 114, 116, 120, 122))
FALLBACK = palette.entry(127)
STRIPES = tuple(palette.entry(i) for i in (112, 114, 116, 118, 120, 122, 96, 127))


# --------------------------------------------------------------------------- cameras and pictures


def look_at(position, target, focal, width, height):
    """``cam_to_box`` and ``intrinsics`` of a camera at ``position`` that looks at ``target``, in Unity's box frame."""
    position, target = np.asarray(position, dtype=np.float64), np.asarray(target, dtype=np.float64)
    forward = (target - position) / np.linalg.norm(target - position)
    right = np.cross([0.0, 1.0, 0.0], forward)
    right /= np.linalg.norm(right)
    up = np.cross(forward, right)
    m = np.eye(4)
    m[:3, 0], m[:3, 1], m[:3, 2], m[:3, 3] = right, up, forward, position
    return m, (float(focal), float(focal), width / 2.0, height / 2.0)


def to_pixel(cam_to_box, intrinsics, point):
    """Brief S8: a point of Unity's box frame, through the camera frame, to (u, v) from the top-left of the crop."""
    fx, fy, cx, cy = intrinsics
    x, y, z = (np.linalg.inv(cam_to_box) @ np.append(point, 1.0))[:3]
    assert z > 0
    return cx + fx * x / z, cy - fy * y / z


def render(boxes, paint, cam_to_box, intrinsics, width, height, background=(128, 128, 128)):
    """The picture a camera takes of boxes given in Unity's box frame, and its matte.

    Works from the pixel outwards (pixel centre, camera ray, box frame, nearest
    box), so it shares no code with the projection under test. ``paint`` gets
    the points hit and the index of the box each lies on.
    """
    fx, fy, cx, cy = intrinsics
    column, row = np.meshgrid(np.arange(width) + 0.5, np.arange(height) + 0.5)
    ray = np.stack([(column - cx) / fx, -(row - cy) / fy, np.ones_like(column)], axis=-1) @ cam_to_box[:3, :3].T
    eye = cam_to_box[:3, 3]
    nearest = np.full((height, width), np.inf)
    which = np.full((height, width), -1)
    for index, (lo, hi) in enumerate(boxes):
        with np.errstate(divide="ignore", invalid="ignore"):
            t0, t1 = (np.asarray(lo) - eye) / ray, (np.asarray(hi) - eye) / ray
        enter, leave = np.minimum(t0, t1).max(axis=-1), np.maximum(t0, t1).min(axis=-1)
        hit = (enter <= leave) & (enter > 0) & (enter < nearest)
        nearest[hit], which[hit] = enter[hit], index
    hit = which >= 0
    rgb = np.empty((height, width, 3), dtype=np.uint8)
    rgb[:] = background
    rgb[hit] = paint(eye + ray[hit] * nearest[hit][:, None], which[hit])
    return rgb, hit.astype(np.float64)


def flat(rgb, width, height):
    picture = np.empty((height, width, 3), dtype=np.uint8)
    picture[:] = rgb
    return picture


# --------------------------------------------------------------------------- meshes


def grid_box(lo, hi, step=0.05):
    """A closed box given in Unity's box frame, as a welded mesh of small triangles in the GLB frame."""
    lo, hi = np.array([-hi[0], lo[1], lo[2]], dtype=np.float64), np.array([-lo[0], hi[1], hi[2]], dtype=np.float64)
    vertices, faces, offset = [], [], 0
    for axis in range(3):
        a, b = [k for k in range(3) if k != axis]
        na, nb = (max(1, int(round((hi[k] - lo[k]) / step))) for k in (a, b))
        ga, gb = np.meshgrid(np.linspace(lo[a], hi[a], na + 1), np.linspace(lo[b], hi[b], nb + 1), indexing="ij")
        i, j = np.meshgrid(np.arange(na), np.arange(nb), indexing="ij")
        p00, p10, p01, p11 = (((i + di) * (nb + 1) + j + dj).reshape(-1) for di, dj in ((0, 0), (1, 0), (0, 1), (1, 1)))
        for side in (lo[axis], hi[axis]):
            v = np.zeros((ga.size, 3))
            v[:, axis], v[:, a], v[:, b] = side, ga.reshape(-1), gb.reshape(-1)
            f = np.concatenate([np.stack([p00, p10, p11], 1), np.stack([p00, p11, p01], 1)])
            normal = np.cross(v[f[0, 1]] - v[f[0, 0]], v[f[0, 2]] - v[f[0, 0]])
            if normal[axis] * (1.0 if side == hi[axis] else -1.0) < 0:
                f = f[:, ::-1]
            vertices.append(v)
            faces.append(f + offset)
            offset += len(v)
    v, f = np.vstack(vertices), np.vstack(faces)
    unique, inverse = np.unique(np.round(v, 9), axis=0, return_inverse=True)
    f = inverse.reshape(-1)[f]
    # Front faces are counter-clockwise in the GLB frame: every normal points away from the centre.
    normal = np.cross(unique[f[:, 1]] - unique[f[:, 0]], unique[f[:, 2]] - unique[f[:, 0]])
    assert (np.einsum("ij,ij->i", normal, unique[f].mean(axis=1) - (lo + hi) / 2) > 0).all()
    return unique, f


def join(*meshes):
    vertices, faces, offset = [], [], 0
    for v, f in meshes:
        vertices.append(v)
        faces.append(f + offset)
        offset += len(v)
    return np.vstack(vertices), np.vstack(faces)


def centres(v, f):
    """Face centres in Unity's box frame."""
    c = v[f].mean(axis=1)
    return np.stack([-c[:, 0], c[:, 1], c[:, 2]], axis=1)


def areas(v, f):
    return 0.5 * np.linalg.norm(np.cross(v[f[:, 1]] - v[f[:, 0]], v[f[:, 2]] - v[f[:, 0]]), axis=1)


BOX = ((-0.3, 0.0, -0.2), (0.3, 0.8, 0.2))


def halves(points, _):
    """Red where Unity's x is positive, blue where it is negative."""
    return np.where(points[:, [0]] > 0, RED.rgb, BLUE.rgb).astype(np.uint8)


def halves_under_a_band(points, which):
    """The same, under a yellow band above y = 0.6: the picture differs from side to side and from top to bottom."""
    return np.where(points[:, [1]] > 0.6, YELLOW.rgb, halves(points, which)).astype(np.uint8)


def front_view(paint, box=BOX, position=(0.0, 0.4, 2.5), size=640, focal=900):
    m, k = look_at(position, (0.0, 0.4, 0.0), focal, size, size)
    rgb, alpha = render([box], paint, m, k, size, size)
    return ColourView(rgb, alpha, k, m)


# --------------------------------------------------------------------------- the frames


def test_the_camera_helper_follows_the_brief():
    # A camera in front of the box (on +Z) looking back at it: Unity's +X is on its left, up is up.
    m, k = look_at((0.0, 0.5, 2.0), (0.0, 0.5, 0.0), 600, 400, 300)
    assert k == (600.0, 600.0, 200.0, 150.0)
    assert np.allclose(m[:3, :3], [[-1, 0, 0], [0, 1, 0], [0, 0, -1]])
    assert np.isclose(np.linalg.det(m[:3, :3]), 1.0)
    assert np.allclose(m @ [0, 0, 1, 1], [0.0, 0.5, 1.0, 1.0])  # one metre ahead of the camera
    # The point (0.3, 0.9, 0) is at (-0.3, 0.4, 2) in the camera frame: 200 - 600 * 0.3 / 2 and 150 - 600 * 0.4 / 2.
    assert np.allclose(to_pixel(m, k, (0.3, 0.9, 0.0)), (110.0, 30.0))
    assert np.allclose(to_pixel(m, k, (0.0, 0.5, 0.0)), (200.0, 150.0))
    # The renderer agrees: the pixel that point lands on shows the box that stands there.
    small = ((0.25, 0.85, -0.05), (0.35, 0.95, 0.0))
    rgb, alpha = render([small], lambda p, _: flat(RED.rgb, len(p), 1)[0], m, k, 400, 300)
    assert tuple(rgb[30, 110]) == RED.rgb and alpha[30, 110] == 1.0
    assert tuple(rgb[30, 290]) == (128, 128, 128) and alpha[270, 110] == 0.0


def test_two_colours_land_on_the_right_faces():
    v, f = grid_box(*BOX)
    m, k = look_at((1.2, 1.6, 1.6), (0.0, 0.4, 0.0), 800, 640, 640)  # sees the front, the top and Unity's +X side
    rgb, alpha = render([BOX], halves_under_a_band, m, k, 640, 640)
    cells = face_cells(v, f, [ColourView(rgb, alpha, k, m)], FALLBACK)
    assert cells.shape == (len(f),) and cells.dtype == np.int64
    assert set(cells.tolist()) == {RED.index, BLUE.index, YELLOW.index}
    c = centres(v, f)
    front, low = np.isclose(c[:, 2], 0.2), c[:, 1] < 0.55
    assert (cells[front & low & (c[:, 0] > 0.05)] == RED.index).all()
    assert (cells[front & low & (c[:, 0] < -0.05)] == BLUE.index).all()
    assert (cells[front & (c[:, 1] > 0.65)] == YELLOW.index).all()
    assert (cells[np.isclose(c[:, 0], 0.3) & low] == RED.index).all()
    assert (cells[np.isclose(c[:, 1], 0.8)] == YELLOW.index).all()
    # The same job with camToBox as the 16 numbers of the job, row by row.
    again = face_cells(v, f, [ColourView(rgb, alpha, list(k), m.reshape(-1).tolist())], FALLBACK)
    assert np.array_equal(again, cells)


def test_a_view_sees_the_faces_that_turn_to_it_and_no_others():
    v, f = grid_box(*BOX)
    view = front_view(halves)
    normals, _ = colour._face_normals(v, f)
    seen, cosine, linear = colour._seen_by(view, v, f, v[f].mean(axis=1), normals)
    c = centres(v, f)
    assert set(seen.tolist()) == set(np.flatnonzero(np.isclose(c[:, 2], 0.2)).tolist())  # the whole front, nothing else
    assert (cosine > 0.9).all() and linear.shape == (len(seen), 3)
    # A matte given as bytes is read as 0..255.
    as_bytes = ColourView(view.rgb, (view.alpha * 255).astype(np.uint8), view.intrinsics, view.cam_to_box)
    assert np.array_equal(colour._seen_by(as_bytes, v, f, v[f].mean(axis=1), normals)[0], seen)


def test_a_hidden_face_does_not_take_the_colour_in_front_of_it():
    panel = ((-0.2, 0.0, 0.3), (0.2, 0.5, 0.35))  # red, near the camera
    back = ((-0.4, 0.0, -0.3), (0.4, 1.0, -0.2))  # blue, partly behind the panel
    v, f = join(grid_box(*panel), grid_box(*back))
    m, k = look_at((0.0, 0.5, 2.5), (0.0, 0.5, 0.0), 700, 640, 640)

    def paint(points, which):
        return np.where(which[:, None] == 0, RED.rgb, BLUE.rgb).astype(np.uint8)

    rgb, alpha = render([panel, back], paint, m, k, 640, 640)
    c = centres(v, f)
    behind = c[:, 2] < 0.0
    # The test means something: many faces of the back's front land on red pixels.
    back_front = np.flatnonzero(np.isclose(c[:, 2], -0.2))
    on_red = [tuple(rgb[int(pv), int(pu)]) == RED.rgb for pu, pv in (to_pixel(m, k, c[i]) for i in back_front)]
    assert sum(on_red) > 50

    cells = face_cells(v, f, [ColourView(rgb, alpha, k, m)], FALLBACK)
    assert (cells[behind] == BLUE.index).all()
    assert (cells[~behind] == RED.index).all()


def test_a_triangle_that_crosses_the_cameras_plane_still_hides():
    v, f = grid_box(*BOX)
    m, k = look_at((0.0, 0.5, 2.5), (0.0, 0.5, 0.0), 700, 640, 640)
    view = ColourView(flat(RED.rgb, 640, 640), None, k, m)
    normals, _ = colour._face_normals(v, f)
    front = set(np.flatnonzero(np.isclose(centres(v, f)[:, 2], 0.2)).tolist())
    assert set(colour._seen_by(view, v, f, v[f].mean(axis=1), normals)[0].tolist()) == front
    # A sheet that starts behind the camera, passes under it and rises in front of the box.
    sheet = np.array([[1.0, 0.2, 3.0], [-1.0, 0.2, 3.0], [0.0, 0.9, 0.5]])
    with_sheet_v, with_sheet_f = np.vstack([v, sheet]), np.vstack([f, [[len(v), len(v) + 1, len(v) + 2]]])
    normals, _ = colour._face_normals(with_sheet_v, with_sheet_f)
    seen = colour._seen_by(view, with_sheet_v, with_sheet_f, with_sheet_v[with_sheet_f].mean(axis=1), normals)[0]
    assert not front & set(seen.tolist())


def test_a_mesh_too_tangled_to_test_takes_the_fallback(monkeypatch):
    v, f = grid_box(*BOX)
    view = front_view(halves)
    assert set(face_cells(v, f, [view], FALLBACK).tolist()) == {RED.index, BLUE.index}
    monkeypatch.setattr(colour, "_MAX_PAIRS", 100)  # as if every ray had thousands of triangles before it
    assert (face_cells(v, f, [view], FALLBACK) == FALLBACK.index).all()


def test_the_matte_excludes_background_pixels():
    v, f = grid_box(*BOX)
    m, k = look_at((0.0, 0.4, 2.5), (0.0, 0.4, 0.0), 900, 640, 640)
    c = centres(v, f)
    # The matte ends two pixels to the right of a face centre, so that face's 5 x 5 pixels reach over the edge.
    front = np.flatnonzero(np.isclose(c[:, 2], 0.2) & (np.abs(c[:, 0]) < 0.02))
    edge = int(to_pixel(m, k, c[front[0]])[0]) + 2
    rgb = flat(RED.rgb, 640, 640)
    rgb[:, edge:] = MAGENTA.rgb  # what stands beside the object in the photo
    alpha = np.zeros((640, 640))
    alpha[:, :edge] = 1.0
    cells = face_cells(v, f, [ColourView(rgb, alpha, k, m)], FALLBACK)
    assert (cells == RED.index).all()
    # No pixel of the background is mixed into a face at the edge of the matte.
    normals, _ = colour._face_normals(v, f)
    seen, _, linear = colour._seen_by(ColourView(rgb, alpha, k, m), v, f, v[f].mean(axis=1), normals)
    assert front[0] in seen and len(seen) > 100
    assert np.allclose(linear, colour._SRGB_TO_LINEAR[list(RED.rgb)])
    # A soft edge of the matte counts as the object from 0.5 upwards.
    soft = np.where(alpha > 0, 0.5, 0.49)
    assert (face_cells(v, f, [ColourView(rgb, soft, k, m)], FALLBACK) == RED.index).all()
    # Without the matte the background is taken for the object.
    unmatted = face_cells(v, f, [ColourView(rgb, None, k, m)], FALLBACK)
    assert set(unmatted.tolist()) == {RED.index, MAGENTA.index}


def test_an_unseen_back_face_takes_its_neighbours_cell():
    v, f = grid_box(*BOX)
    cells = face_cells(v, f, [front_view(halves)], FALLBACK)  # the camera sees the front and nothing else
    assert set(cells.tolist()) == {RED.index, BLUE.index}
    c = centres(v, f)
    back = np.isclose(c[:, 2], -0.2)
    assert (cells[back & (c[:, 0] > 0.1)] == RED.index).all()
    assert (cells[back & (c[:, 0] < -0.1)] == BLUE.index).all()
    assert (cells[np.isclose(c[:, 0], 0.3)] == RED.index).all()  # the sides take the half they touch
    assert (cells[np.isclose(c[:, 0], -0.3)] == BLUE.index).all()


def test_no_view_gives_the_fallback_everywhere():
    v, f = grid_box(*BOX)
    assert (face_cells(v, f, [], FALLBACK) == FALLBACK.index).all()
    # A camera that looks away from the object sees no face either.
    m, k = look_at((0.0, 0.4, 2.5), (0.0, 0.4, 5.0), 900, 640, 640)
    assert (face_cells(v, f, [ColourView(flat(RED.rgb, 640, 640), None, k, m)], FALLBACK) == FALLBACK.index).all()
    # So does a matte that leaves nothing of the object.
    view = front_view(halves)
    view.alpha = np.zeros_like(view.alpha)
    assert (face_cells(v, f, [view], FALLBACK) == FALLBACK.index).all()
    assert len(face_cells(v, np.zeros((0, 3), dtype=int), [view], FALLBACK)) == 0


def blocks(side, size=640, seed=3):
    """A picture of squares of ``side`` pixels, each in one of eight palette colours picked at random."""
    rng = np.random.default_rng(seed)
    pick = rng.integers(0, len(STRIPES), (size // side + 1, size // side + 1))
    table = np.array([e.rgb for e in STRIPES], dtype=np.uint8)
    return np.kron(pick, np.ones((side, side), dtype=np.int64))[:size, :size], table


def test_a_speckled_picture_falls_back_to_one_cell():
    v, f = grid_box(*BOX)
    m, k = look_at((0.0, 0.4, 2.5), (0.0, 0.4, 0.0), 900, 640, 640)
    pick, table = blocks(10)
    cells = face_cells(v, f, [ColourView(table[pick], None, k, m)], FALLBACK)
    assert len(set(cells.tolist())) == 1
    assert cells[0] in {e.index for e in STRIPES}  # a colour of the picture, not the fallback
    # Patches large enough to be regions are kept: the same colours in squares of 100 pixels.
    pick, table = blocks(100)
    coarse = face_cells(v, f, [ColourView(table[pick], None, k, m)], FALLBACK)
    assert 2 <= len(set(coarse.tolist())) <= 6


def test_max_cells_is_respected():
    box = ((-0.8, 0.0, -0.1), (0.8, 0.8, 0.1))
    v, f = grid_box(*box)

    def stripes(points, _):  # eight upright stripes of 0.2 m, each 0.16 m2 of 3.52: 4.5 % of the surface
        index = np.clip(np.floor((points[:, 0] + 0.8) / 0.2).astype(int), 0, 7)
        return np.array([e.rgb for e in STRIPES], dtype=np.uint8)[index]

    view = front_view(stripes, box, position=(0.0, 0.4, 3.0), size=768, focal=1100)
    assert len(set(face_cells(v, f, [view], FALLBACK, max_cells=8).tolist())) == 8
    assert len(set(face_cells(v, f, [view], FALLBACK).tolist())) == 6  # the default
    for limit in (5, 3, 1):
        cells = face_cells(v, f, [view], FALLBACK, max_cells=limit)
        assert len(set(cells.tolist())) == limit
        assert set(cells.tolist()) <= {e.index for e in STRIPES}
    with pytest.raises(ValueError):
        face_cells(v, f, [view], FALLBACK, max_cells=0)


def test_the_cells_are_limited_before_the_unseen_faces_are_filled():
    v, f = grid_box(*BOX)

    def paint(points, _):
        x, y = points[:, 0], points[:, 1]
        strip = x < -0.2  # blue, 0.08 m2 of the front, beside the unseen side of 0.32 m2
        patch = (x > -0.1) & (x < 0.2) & (y > 0.25) & (y < 0.55)  # green, 0.09 m2
        return np.where(strip[:, None], BLUE.rgb, np.where(patch[:, None], GREEN.rgb, RED.rgb)).astype(np.uint8)

    view = front_view(paint)
    c = centres(v, f)
    three = face_cells(v, f, [view], FALLBACK)
    assert set(three.tolist()) == {RED.index, GREEN.index, BLUE.index}
    assert (three[np.isclose(c[:, 0], -0.3)] == BLUE.index).all()  # the strip spreads over the side nobody saw
    # With room for two, the blue goes: it was the smallest region the photo showed, whatever it would have grown to.
    two = face_cells(v, f, [view], FALLBACK, max_cells=2)
    assert set(two.tolist()) == {RED.index, GREEN.index}
    assert (two[np.isclose(c[:, 0], -0.3)] == RED.index).all()


def test_views_are_averaged_in_linear_light_by_the_cosine():
    plate = ((-0.3, 0.0, -0.02), (0.3, 0.6, 0.02))
    v, f = grid_box(*plate)
    light, dark = (230, 230, 230), (20, 20, 20)
    straight, k = look_at((0.0, 0.3, 2.5), (0.0, 0.3, 0.0), 900, 640, 640)
    aside = (2.5 * np.sin(np.radians(60)), 0.3, 2.5 * np.cos(np.radians(60)))
    oblique, _ = look_at(aside, (0.0, 0.3, 0.0), 900, 640, 640)
    views = [ColourView(flat(light, 640, 640), None, k, straight), ColourView(flat(dark, 640, 640), None, k, oblique)]
    cells = face_cells(v, f, views, FALLBACK)

    def linear(c):
        c = c / 255.0
        return ((c + 0.055) / 1.055) ** 2.4

    mixed = (1.0 * linear(230) + 0.5 * linear(20)) / 1.5  # cosines of about 1 and 0.5
    expected = palette.nearest_entry([255 * (1.055 * mixed ** (1 / 2.4) - 0.055)] * 3)
    naive = palette.nearest_entry([(1.0 * 230 + 0.5 * 20) / 1.5] * 3)
    assert expected.index != naive.index
    c = centres(v, f)
    assert (cells[np.isclose(c[:, 2], 0.02)] == expected.index).all()


def test_a_mesh_split_at_its_creases_gives_the_same_cells():
    v, f = grid_box(*BOX)
    view = front_view(halves_under_a_band, position=(1.2, 1.6, 1.6), focal=800)
    welded = face_cells(v, f, [view], FALLBACK)
    # Every face with three vertices of its own: neighbours are then found by position.
    apart = face_cells(v[f].reshape(-1, 3), np.arange(len(f) * 3).reshape(-1, 3), [view], FALLBACK)
    assert np.array_equal(apart, welded)
    assert np.array_equal(face_cells(v, f, [view], FALLBACK), welded)  # the same input, the same cells


def test_a_face_takes_the_cell_two_of_its_neighbours_share():
    # A tetrahedron: every face has the other three as neighbours.
    v = np.array([[0.0, 0, 0], [1, 0, 0], [0, 1, 0], [0, 0, 1]])
    f = np.array([[0, 2, 1], [0, 1, 3], [0, 3, 2], [1, 2, 3]])
    neighbour = colour._edge_neighbours(colour._corner_ids(v, f))
    assert all(sorted(row) == sorted(set(range(4)) - {i}) for i, row in enumerate(neighbour.tolist()))
    everywhere = np.ones(4, dtype=bool)
    assert colour._smooth(np.array([70, 80, 80, 90]), everywhere, neighbour).tolist() == [80, 80, 80, 80]
    assert colour._smooth(np.array([70, 80, 90, 100]), everywhere, neighbour).tolist() == [70, 80, 90, 100]
    # A face without a cell has no say and takes nothing.
    three = np.array([True, True, False, True])
    assert colour._smooth(np.array([70, 80, -1, 90]), three, neighbour).tolist() == [70, 80, -1, 90]
    assert colour._smooth(np.array([70, 80, -1, 80]), three, neighbour).tolist() == [80, 80, -1, 80]


def test_a_single_face_that_sticks_out_of_its_region_is_taken_back():
    v, f = grid_box(*BOX, step=0.1)
    c = centres(v, f)
    m, k = look_at((0.0, 0.4, 2.5), (0.0, 0.4, 0.0), 900, 640, 640)
    # The blue face that touches the red half along an edge, half-way up.
    beside = np.isclose(c[:, 2], 0.2) & (c[:, 0] < 0) & (c[:, 0] > -0.04) & (np.abs(c[:, 1] - 0.4) < 0.05)
    odd = int(np.flatnonzero(beside)[0])

    def paint(points, _):  # the halves, and a red dot of 2 cm on that one face
        dot = np.linalg.norm(points - c[odd], axis=1) < 0.02
        return np.where((points[:, [0]] > 0) | dot[:, None], RED.rgb, BLUE.rgb).astype(np.uint8)

    rgb, alpha = render([BOX], paint, m, k, 640, 640)
    view = ColourView(rgb, alpha, k, m)
    normals, _ = colour._face_normals(v, f)
    seen, _, linear = colour._seen_by(view, v, f, v[f].mean(axis=1), normals)
    read = dict(zip(seen.tolist(), linear.tolist()))
    assert np.allclose(read[odd], colour._SRGB_TO_LINEAR[list(RED.rgb)])  # the photo shows it red
    neighbour = colour._edge_neighbours(colour._corner_ids(v, f))
    assert sum(np.allclose(read[n], colour._SRGB_TO_LINEAR[list(RED.rgb)]) for n in neighbour[odd].tolist()) == 1
    cells = face_cells(v, f, [view], FALLBACK)
    assert cells[odd] == BLUE.index
    front = np.isclose(c[:, 2], 0.2)
    assert (cells[front & (c[:, 0] > 0)] == RED.index).all() and (cells[front & (c[:, 0] < 0)] == BLUE.index).all()


def test_a_small_island_is_merged_though_the_faces_round_it_were_not_seen():
    v, f = grid_box(*BOX)
    m, k = look_at((0.0, 0.4, 2.5), (0.0, 0.4, 0.0), 900, 640, 640)
    middle = np.array([0.0, 0.4, 0.2])

    def island(radius, ring):
        """Green inside ``radius``, red elsewhere, and a ring round the green that the matte hides."""
        def away(points):
            return np.linalg.norm(points - middle, axis=1)[:, None]

        def paint(points, _):
            return np.where(away(points) < radius, GREEN.rgb, RED.rgb).astype(np.uint8)

        def mark_ring(points, _):
            inside = (away(points) >= radius) & (away(points) < ring)
            return np.where(inside, (255, 255, 255), (0, 0, 0)).astype(np.uint8)

        rgb, alpha = render([BOX], paint, m, k, 640, 640)
        marked, _ = render([BOX], mark_ring, m, k, 640, 640, background=(0, 0, 0))
        return ColourView(rgb, np.where(marked[:, :, 0] == 255, 0.0, alpha), k, m)

    # 0.05 m of green grows to about 0.07 m in the flood: 0.015 m2 of the box's 2.08, under 2 %.
    assert (face_cells(v, f, [island(0.05, 0.09)], FALLBACK) == RED.index).all()
    # 0.15 m of green is 0.07 m2, 3.4 %, before the flood: it stays.
    kept = face_cells(v, f, [island(0.15, 0.19)], FALLBACK)
    assert set(kept.tolist()) == {RED.index, GREEN.index}
    c = centres(v, f)
    assert (kept[np.isclose(c[:, 2], 0.2) & (np.linalg.norm(c - middle, axis=1) < 0.13)] == GREEN.index).all()


# --------------------------------------------------------------------------- regions


def test_regions_are_the_edge_connected_faces_of_one_cell():
    v, f = grid_box(*BOX, step=0.1)
    rng = np.random.default_rng(5)
    cells = rng.integers(64, 67, len(f))
    has_cell = rng.random(len(f)) > 0.2
    neighbour = colour._edge_neighbours(colour._corner_ids(v, f))
    assert ((neighbour >= 0).sum(axis=1) == 3).all()  # a closed surface
    for face, row in enumerate(neighbour):
        assert all(face in neighbour[n] and len(set(f[face]) & set(f[n])) == 2 for n in row)
    labels = colour._regions(cells, has_cell, neighbour)

    expected = np.arange(len(f))
    for start in range(len(f)):  # a plain flood, face by face
        if expected[start] != start or not has_cell[start]:
            continue
        stack = [start]
        while stack:
            face = stack.pop()
            for n in neighbour[face]:
                if has_cell[n] and cells[n] == cells[start] and expected[n] != start and n > start:
                    expected[n] = start
                    stack.append(n)
    assert np.array_equal(labels[has_cell], expected[has_cell])


def test_small_regions_merge_into_their_largest_neighbour():
    v, f = grid_box(*BOX)
    c = centres(v, f)
    a = areas(v, f)
    front = np.isclose(c[:, 2], 0.2)
    cells = np.where(c[:, 0] > 0, RED.index, BLUE.index)
    spot = front & (np.abs(c[:, 0] - 0.15) < 0.05) & (np.abs(c[:, 1] - 0.4) < 0.05)  # 0.01 m2 of 2.08: under 2 %
    patch = front & (c[:, 0] < -0.1) & (c[:, 1] > 0.5)  # 0.06 m2: over 2 %
    # A second spot between the patch and the blue round it: the blue is the larger neighbour.
    between = front & (c[:, 0] > -0.1) & (c[:, 0] < -0.05) & (np.abs(c[:, 1] - 0.65) < 0.05)  # 0.005 m2
    cells[spot], cells[patch], cells[between] = GREEN.index, YELLOW.index, MAGENTA.index
    everywhere = np.ones(len(f), dtype=bool)
    neighbour = colour._edge_neighbours(colour._corner_ids(v, f))
    merged = colour._merge_regions(cells, everywhere, neighbour, a, 0.02 * a.sum(), 6)
    assert spot.sum() == 8 and between.sum() == 4
    assert (merged[spot] == RED.index).all()
    assert (merged[between] == BLUE.index).all()
    assert np.array_equal(merged[~spot & ~between], cells[~spot & ~between])
    # With room for two cells only, the smallest region (the patch) goes too, into the blue around it.
    two = colour._merge_regions(cells, everywhere, neighbour, a, 0.02 * a.sum(), 2)
    assert (two[patch] == BLUE.index).all() and set(two.tolist()) == {RED.index, BLUE.index}


# --------------------------------------------------------------------------- vertices


def test_split_by_cell_gives_each_face_three_vertices_of_its_own_cell():
    v, f = grid_box(*BOX)
    normals = np.random.default_rng(1).normal(size=v.shape)  # any numbers: they are only carried along
    cells = face_cells(v, f, [front_view(halves_under_a_band, position=(1.2, 1.6, 1.6), focal=800)], FALLBACK)
    assert len(set(cells.tolist())) == 3
    positions, split_normals, faces, vertex_cells = split_by_cell(v.astype(np.float32), normals, f, cells)
    assert positions.dtype == np.float32 and len(positions) == len(split_normals) == len(vertex_cells)
    assert np.array_equal(vertex_cells[faces], np.repeat(cells[:, None], 3, axis=1))
    # Faces keep their order and their winding: corner by corner the same point and the same normal.
    assert np.array_equal(positions[faces], v.astype(np.float32)[f])
    assert np.array_equal(split_normals[faces], normals[f])
    # Only the vertices on a border between two cells were duplicated, once per further cell.
    cells_at = [set() for _ in v]
    for face, cell in zip(f, cells):
        for vertex in face:
            cells_at[vertex].add(int(cell))
    assert len(positions) == sum(len(s) for s in cells_at)
    assert len(v) < len(positions) < len(v) * 1.2
    assert 64 <= vertex_cells.min() and vertex_cells.max() <= 127


def test_split_by_cell_does_not_grow_a_mesh_of_one_cell():
    v, f = grid_box(*BOX)
    normals = np.random.default_rng(1).normal(size=v.shape)  # any numbers: they are only carried along
    positions, split_normals, faces, vertex_cells = split_by_cell(v, normals, f, np.full(len(f), 100))
    assert np.array_equal(positions, v) and np.array_equal(split_normals, normals) and np.array_equal(faces, f)
    assert (vertex_cells == 100).all() and len(vertex_cells) == len(v)
    with pytest.raises(ValueError):
        split_by_cell(v, normals, f, np.full(len(f) - 1, 100))


def test_dominant_cell():
    assert dominant_cell([70, 90, 90, 70, 100], [1.0, 0.5, 0.6, 0.2, 1.05]) == 70
    assert dominant_cell([70, 90, 90], [1.0, 0.5, 0.6]) == 90
    assert dominant_cell([70, 90, 90], [2.0, 0.5, 0.6]) == 70  # by surface, not by the number of faces
    assert dominant_cell([90, 70], [1.0, 1.0]) == 70  # the lower cell on a tie
    assert isinstance(dominant_cell(np.array([90]), np.array([2.0])), int)
    with pytest.raises(ValueError):
        dominant_cell([], [])


# --------------------------------------------------------------------------- speed


def lumpy_torus(rows=75, columns=100, seed=7, dims=(0.9, 0.8, 0.7)):
    """A closed, bumpy surface of rows * columns * 2 triangles, scaled to the box, in the working frame."""
    rng = np.random.default_rng(seed)
    a = np.linspace(0, 2 * np.pi, rows, endpoint=False)[:, None]
    b = np.linspace(0, 2 * np.pi, columns, endpoint=False)[None, :]
    tube = 0.35 + 0.08 * np.sin(3 * b) * np.cos(2 * a) + 0.03 * rng.random((rows, columns))
    ring = 1.0 + tube * np.cos(a)
    lift = tube * np.sin(a) * (1.0 + 0.3 * np.sin(b))
    v = np.stack([ring * np.cos(b), lift, ring * np.sin(b)], axis=-1).reshape(-1, 3)
    r, c = np.meshgrid(np.arange(rows), np.arange(columns), indexing="ij")
    i00, i10 = r * columns + c, ((r + 1) % rows) * columns + c
    i01, i11 = r * columns + (c + 1) % columns, ((r + 1) % rows) * columns + (c + 1) % columns
    f = np.concatenate([np.stack([i00, i11, i01], -1).reshape(-1, 3), np.stack([i00, i10, i11], -1).reshape(-1, 3)])
    lo, hi = v.min(axis=0), v.max(axis=0)
    v = (v - [(lo[0] + hi[0]) / 2, lo[1], (lo[2] + hi[2]) / 2]) * (np.asarray(dims) / (hi - lo))
    return v, f


def test_speed_with_15000_faces_and_three_views():
    v, f = lumpy_torus()
    assert len(f) == 15000
    size = 768
    rng = np.random.default_rng(11)
    row, column = np.meshgrid(np.arange(size), np.arange(size), indexing="ij")
    views = []
    for index, azimuth in enumerate((-40.0, 0.0, 40.0)):
        position = (2.0 * np.sin(np.radians(azimuth)), 1.3, 2.0 * np.cos(np.radians(azimuth)))
        m, k = look_at(position, (0.0, 0.4, 0.0), 1100, size, size)
        # Four flat patches and some noise, as a photo of a plain object has.
        patch = np.array([RED.rgb, BLUE.rgb, YELLOW.rgb, GREEN.rgb])[(row > 400) * 2 + (column > 384 + 40 * index)]
        rgb = np.clip(patch + rng.integers(-8, 9, patch.shape), 0, 255).astype(np.uint8)
        views.append(ColourView(rgb, np.ones((size, size)), k, m))
    start = time.perf_counter()
    cells = face_cells(v, f, views, FALLBACK)
    elapsed = time.perf_counter() - start
    assert elapsed < 5.0, f"{elapsed:.2f} s"
    assert cells.shape == (15000,) and cells.min() >= 64 and cells.max() <= 127
    assert 2 <= len(set(cells.tolist())) <= 6
    normals, _ = colour._face_normals(v, f)
    seen = [len(colour._seen_by(view, v, f, v[f].mean(axis=1), normals)[0]) for view in views]
    assert min(seen) > 2000  # each view really sees a good part of the surface
