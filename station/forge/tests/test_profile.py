import math
import time

import numpy as np
import pytest

from forge import profile

# Clean-up's quarter turn in the GLB frame: x' = z, z' = -x (a right-handed +90 degrees about +Y).
YAW90 = np.array([[0.0, 0, 1], [0, 1, 0], [-1, 0, 0]])


def glb_box(lo, hi, diagonal=0):
    """A closed box given by its corners in Unity's box frame, as a triangle mesh in the GLB frame.

    ``diagonal`` chooses which way the top and the bottom quad are cut: 0 along
    Unity's x = z (for a box centred on the origin), 1 along x = -z.
    """
    (x0, y0, z0), (x1, y1, z1) = lo, hi
    # Unity's x is the negative of the GLB's, so the bounds swap; corner = ix + 2 * iy + 4 * iz.
    xs, ys, zs = (-x1, -x0), (y0, y1), (z0, z1)
    v = np.array([[xs[i & 1], ys[(i >> 1) & 1], zs[(i >> 2) & 1]] for i in range(8)], dtype=np.float64)
    if diagonal == 0:  # GLB corners 3 to 6 and 1 to 4: Unity's (x-, z-) to (x+, z+)
        caps = [(2, 6, 3), (3, 6, 7), (0, 1, 4), (1, 5, 4)]
    else:
        caps = [(2, 6, 7), (2, 7, 3), (0, 1, 5), (0, 5, 4)]
    sides = [(0, 2, 3), (0, 3, 1), (4, 5, 7), (4, 7, 6), (0, 4, 6), (0, 6, 2), (1, 3, 7), (1, 7, 5)]
    f = np.array(caps + sides, dtype=np.int64)
    # Front faces are counter-clockwise in the GLB frame: every normal points away from the centre.
    normal = np.cross(v[f[:, 1]] - v[f[:, 0]], v[f[:, 2]] - v[f[:, 0]])
    assert (np.einsum("ij,ij->i", normal, v[f].mean(axis=1) - v.mean(axis=0)) > 0).all()
    return v, f


def join(*meshes):
    vertices, faces, offset = [], [], 0
    for v, f in meshes:
        vertices.append(v)
        faces.append(f + offset)
        offset += len(v)
    return np.vstack(vertices), np.vstack(faces)


def grid(p):
    return np.asarray(p).reshape(16, 16)  # [j, i]


def vector_a(diagonal=0):
    seat = glb_box((-0.25, 0.0, -0.25), (0.25, 0.5, 0.25), diagonal)
    back = glb_box((-0.25, 0.0, -0.25), (0.25, 1.0, -0.2), diagonal)
    return join(seat, back)


def armchair():
    """A slab, a tall back on -Z and a low arm on Unity's +X: no quarter turn maps it onto itself."""
    slab = glb_box((-0.5, 0.0, -0.5), (0.5, 0.25, 0.5))
    back = glb_box((-0.5, 0.0, -0.5), (0.5, 1.0, -0.25))
    arm = glb_box((0.375, 0.0, -0.25), (0.5, 0.5, 0.5))
    return join(slab, back, arm)


def turned(vertices, quarter_turns):
    """The mesh turned by ``quarter_turns`` of clean-up's ``_YAW90``, in the GLB frame."""
    return vertices @ np.linalg.matrix_power(YAW90, quarter_turns % 4).T


# --------------------------------------------------------------------------- the vectors of the brief


@pytest.mark.parametrize("diagonal", [0, 1])
def test_vector_a(diagonal):
    # Rays of the cells with i = j (and with i + j = 15) fall exactly on a diagonal of the seat's top.
    v, f = vector_a(diagonal)
    p = profile.of_mesh(v, f, (0.5, 1.0, 0.5))
    assert p.dtype == np.uint8 and p.shape == (256,)
    assert (grid(p)[:2] == 255).all()
    assert (grid(p)[2:] == 128).all()
    assert profile.coverage(p) == 1.0


def test_vector_b_no_triangles():
    p = profile.of_mesh(np.zeros((0, 3)), np.zeros((0, 3), dtype=int), (0.5, 1.0, 0.5))
    assert p.shape == (256,) and not p.any()
    assert profile.coverage(p) == 0.0


def test_vector_c_rotate():
    a = profile.of_mesh(*vector_a(), (0.5, 1.0, 0.5))
    half = grid(profile.rotate(a, 2))
    assert (half[14:] == 255).all() and (half[:14] == 128).all()
    quarter = grid(profile.rotate(a, 1))
    assert (quarter[:, 14:] == 255).all() and (quarter[:, :14] == 128).all()
    three = grid(profile.rotate(a, 3))
    assert (three[:, :2] == 255).all() and (three[:, 2:] == 128).all()


def test_rotate_composes():
    p = np.arange(256, dtype=np.uint8)
    assert np.array_equal(profile.rotate(p, 0), p)
    assert np.array_equal(profile.rotate(p, 4), p)
    assert np.array_equal(profile.rotate(profile.rotate(p, 1), 3), p)
    assert np.array_equal(profile.rotate(profile.rotate(p, 1), 1), profile.rotate(p, 2))
    assert np.array_equal(profile.rotate(p, -1), profile.rotate(p, 3))
    # New cell (i', j') takes the old cell (j', 15 - i'): the old cell (i, j) = (3, 15) lands on (0, 3).
    assert profile.rotate(p, 1)[3 * 16 + 0] == p[15 * 16 + 3]


# --------------------------------------------------------------------------- the rays


def test_only_the_ray_through_a_vertex_or_an_edge():
    w, h, d = 0.8, 1.0, 0.8
    # The ray of cell (5, 9) at the quarter points (0.25, 0.75), in Unity's frame.
    rx, rz = -w / 2 + 5.25 * w / 16, -d / 2 + 9.75 * d / 16
    tiny = 0.01  # the rays stand 0.025 m apart: the triangle reaches no other one
    for corners in (
        [(rx, rz), (rx + tiny, rz), (rx, rz + tiny)],  # the ray passes through a vertex
        [(rx - tiny, rz), (rx + tiny, rz), (rx, rz + tiny)],  # and through an edge
    ):
        v = np.array([[-x, 0.4, z] for x, z in corners])
        p = grid(profile.of_mesh(v, np.array([[0, 1, 2]]), (w, h, d)))
        assert p[9, 5] == 102  # floor(255 * 0.4 + 0.5)
        assert np.count_nonzero(p) == 1


def test_height_limits_and_the_byte():
    dims = (1.0, 1.0, 1.0)

    def plate(height):
        v = np.array([[-0.5, height, -0.5], [0.5, height, -0.5], [0.5, height, 0.5], [-0.5, height, 0.5]])
        return profile.of_mesh(v, np.array([[0, 1, 2], [0, 2, 3]]), dims)

    assert not plate(0.019).any()  # below 0.02 m: the floor
    assert (plate(0.02) == 5).all()
    assert (plate(0.001 + 0.02) == 5).all()
    assert (plate(1.0) == 255).all()
    assert (plate(1.05) == 255).all()  # the ray's own start still counts, and the byte is clamped
    assert not plate(1.06).any()  # above the start of the ray
    # A value that would round to 0 is stored as 1: 0 means that no ray hit.
    v = np.array([[-0.5, 0.02, -0.5], [0.5, 0.02, -0.5], [0.5, 0.02, 0.5], [-0.5, 0.02, 0.5]])
    assert (profile.of_mesh(v, np.array([[0, 1, 2], [0, 2, 3]]), (1.0, 20.0, 1.0)) == 1).all()


def test_the_highest_surface_wins_and_winding_does_not_matter():
    low = glb_box((-0.5, 0.0, -0.5), (0.5, 0.3, 0.5))
    high = glb_box((-0.5, 0.6, -0.5), (0.5, 0.7, 0.5))
    v, f = join(low, high)
    p = profile.of_mesh(v, f, (1.0, 1.0, 1.0))
    assert (p == 179).all()  # floor(255 * 0.7 + 0.5)
    assert np.array_equal(profile.of_mesh(v, f[:, ::-1], (1.0, 1.0, 1.0)), p)


def test_agrees_with_every_ray_against_every_triangle():
    """The same profile from a plain ray cast in three dimensions, which shares no step with ``of_mesh``."""
    rng = np.random.default_rng(21)
    w, h, d = 0.9, 0.8, 0.7
    first = rng.uniform(-0.5, 0.5, (400, 1, 3)) * [w, h, d] + [0, h / 2, 0]
    v = (first + rng.uniform(-0.12, 0.12, (400, 3, 3))).reshape(-1, 3)  # 400 loose triangles in and round the box
    f = np.arange(1200).reshape(-1, 3)
    unity = v[f] * [-1.0, 1.0, 1.0]
    a, e1, e2 = unity[:, 0], unity[:, 1] - unity[:, 0], unity[:, 2] - unity[:, 0]
    expected = np.zeros(256, dtype=np.uint8)
    down = np.array([0.0, -1.0, 0.0])
    for j in range(16):
        for i in range(16):
            highest = -np.inf
            for qa in (0.25, 0.75):
                for qb in (0.25, 0.75):
                    origin = np.array([-w / 2 + (i + qa) * w / 16, h + 0.05, -d / 2 + (j + qb) * d / 16])
                    p = np.cross(down, e2)
                    det = np.einsum("ij,ij->i", e1, p)
                    ok = np.abs(det) > 1e-15
                    inverse = 1.0 / np.where(ok, det, 1.0)
                    s = origin - a
                    bu = np.einsum("ij,ij->i", s, p) * inverse
                    q = np.cross(s, e1)
                    bv = (q @ down) * inverse
                    height = origin[1] - np.einsum("ij,ij->i", e2, q) * inverse
                    hit = ok & (bu >= 0) & (bv >= 0) & (bu + bv <= 1) & (height >= 0.02) & (height <= h + 0.05)
                    if hit.any():
                        highest = max(highest, float(height[hit].max()))
            if np.isfinite(highest):
                expected[j * 16 + i] = min(max(int(np.floor(255 * highest / h + 0.5)), 1), 255)
    got = profile.of_mesh(v, f, (w, h, d))
    assert 0.5 < profile.coverage(got) < 1.0 and len(np.unique(got)) > 50
    assert np.array_equal(got, expected)


def test_frame_the_tall_side_is_on_unitys_plus_x():
    # An L-shaped block: a slab over the whole footprint, tall where Unity's x is at least 0.25,
    # of middle height where z is at least 0.125 on the rest.
    slab = glb_box((-0.5, 0.0, -0.25), (0.5, 0.25, 0.25))
    tall = glb_box((0.25, 0.0, -0.25), (0.5, 1.0, 0.25))
    middle = glb_box((-0.5, 0.0, 0.125), (0.25, 0.5, 0.25))
    v, f = join(slab, tall, middle)
    assert v[:, 0].min() == -0.5 and v[v[:, 1] == 1.0][:, 0].max() == -0.25  # in the GLB the tall side is on -x
    p = grid(profile.of_mesh(v, f, (1.0, 1.0, 0.5)))
    assert (p[:, 12:] == 255).all()  # columns i = 12..15: Unity's +X
    assert (p[12:, :12] == 128).all()  # rows j = 12..15: +Z
    assert (p[:12, :12] == 64).all()


def test_rejects_a_box_without_extent():
    v, f = vector_a()
    with pytest.raises(ValueError):
        profile.of_mesh(v, f, (0.5, 0.0, 0.5))


# --------------------------------------------------------------------------- the bytes


def test_encode_and_decode_round_trip():
    p = profile.of_mesh(*armchair(), (1.0, 1.0, 1.0))
    text = profile.encode(p)
    assert len(text) == 344 and text.endswith("==")
    assert np.array_equal(profile.decode(text), p)
    assert profile.encode(profile.decode(text)) == text
    assert profile.decode(text).dtype == np.uint8
    assert profile.encode(list(range(256))) == profile.encode(np.arange(256, dtype=np.uint8))


ZEROS = profile.encode(np.zeros(256, dtype=np.uint8))


@pytest.mark.parametrize("text", ["", "AAAA", ZEROS[:-4], ZEROS + "AAAA", "!" * 344, "A" * 343, None, b"AAAA"])
def test_decode_rejects_anything_but_256_bytes(text):
    with pytest.raises(ValueError):
        profile.decode(text)


def test_encode_rejects_a_wrong_shape_or_range():
    with pytest.raises(ValueError):
        profile.encode(np.zeros(255, dtype=np.uint8))
    with pytest.raises(ValueError):
        profile.encode(np.full(256, 256))
    with pytest.raises(ValueError):
        profile.encode(np.full(256, 0.5))


def test_coverage_counts_the_cells_that_are_not_zero():
    p = np.zeros(256, dtype=np.uint8)
    p[:64] = 1
    assert profile.coverage(p) == 0.25


# --------------------------------------------------------------------------- the fit


def test_fit_score():
    p = profile.of_mesh(*armchair(), (1.0, 1.0, 1.0))
    assert profile.fit_score(p, p) == 0.0
    low, high = np.full(256, 128, dtype=np.uint8), np.full(256, 255, dtype=np.uint8)
    assert profile.fit_score(low, high) == pytest.approx(127 / 255)
    assert profile.fit_score(high, low) == pytest.approx(127 / 255)
    # Only the cells the scan saw count, and a generated cell with no surface counts as height 0.
    scan = np.zeros(256, dtype=np.uint8)
    scan[:64] = 255
    scan[64:128] = 51
    generated = np.zeros(256, dtype=np.uint8)
    generated[:64] = 204
    generated[128:] = 255  # the scan did not see these
    assert profile.fit_score(scan, generated) == pytest.approx((64 * 51 + 64 * 51) / (128 * 255))
    assert profile.fit_score(np.zeros(256, dtype=np.uint8), generated) == math.inf


def test_a_turned_mesh_and_rotate_agree():
    """One ``_YAW90`` of clean-up in the GLB frame (x' = z, z' = -x) is ``rotate(profile, 1)``.

    In Unity's box frame that turn is a yaw of -90 degrees: what stood on -Z stands on +X.
    """
    v, f = armchair()
    dims = (1.0, 1.0, 1.0)
    p = profile.of_mesh(v, f, dims)
    assert (grid(p)[:4] == 255).all() and (grid(p)[4:, 14:] == 128).all() and (grid(p)[4:, :14] == 64).all()
    for k in range(4):
        assert np.array_equal(profile.of_mesh(turned(v, k), f, dims), profile.rotate(p, k)), k
    assert (grid(profile.of_mesh(turned(v, 1), f, dims))[:, 12:] == 255).all()  # the back went to Unity's +X
    # The same in a box that is not square: w and d swap on an odd turn.
    v, f = join(glb_box((-0.5, 0.0, -0.25), (0.5, 0.25, 0.25)), glb_box((0.25, 0.0, -0.25), (0.5, 1.0, 0.0)))
    p = profile.of_mesh(v, f, (1.0, 1.0, 0.5))
    assert len(np.unique(p)) == 2
    for k in range(4):
        box = (1.0, 1.0, 0.5) if k % 2 == 0 else (0.5, 1.0, 1.0)
        assert np.array_equal(profile.of_mesh(turned(v, k), f, box), profile.rotate(p, k)), k


def test_quarter_turns_score_in_the_expected_order():
    v, f = armchair()
    dims = (1.0, 1.0, 1.0)
    scan = profile.of_mesh(v, f, dims)
    grid(scan)[:, :8] = 0  # the scan saw only the half on Unity's +X: 128 cells
    assert profile.coverage(scan) == 0.5
    score = [profile.fit_score(scan, profile.of_mesh(turned(v, k), f, dims)) for k in range(4)]
    # Worked out by hand from the three heights (bytes 64, 128 and 255) and where each turn puts them.
    assert score[0] == 0.0
    assert score[3] == pytest.approx(6624 / (128 * 255))
    assert score[1] == pytest.approx(11200 / (128 * 255))
    assert score[2] == pytest.approx(12736 / (128 * 255))
    assert score[0] < score[3] < score[1] < score[2]
    # With every cell seen, a quarter turn either way is equally wrong and the half turn is the worst.
    full = profile.of_mesh(v, f, dims)
    score = [profile.fit_score(full, profile.of_mesh(turned(v, k), f, dims)) for k in range(4)]
    assert score[0] == 0.0 and score[1] == pytest.approx(score[3]) and score[2] > score[1] > 0.2


def test_the_fit_turns_a_turned_mesh_back():
    v, f = armchair()
    dims = (1.0, 1.0, 1.0)
    scan = profile.of_mesh(v, f, dims)
    for k in range(4):
        delivered = turned(v, k)
        score = [profile.fit_score(scan, profile.of_mesh(turned(delivered, c), f, dims)) for c in range(4)]
        assert int(np.argmin(score)) == (4 - k) % 4
        assert min(score) == 0.0 and sorted(score)[1] > 0.2


# --------------------------------------------------------------------------- the view's yaw


def cam_to_box(position):
    m = np.eye(4)
    m[:3, 3] = position
    return m


@pytest.mark.parametrize(
    "position, azimuth, quarter",
    [
        ((0.0, 1.0, 2.0), 0.0, 0),
        ((2.0, 1.0, 0.0), 90.0, 1),
        ((0.0, 1.0, -2.0), 180.0, 2),
        ((-2.0, 1.0, 0.0), -90.0, 3),
        ((1.0, 0.2, 2.0), 26.565, 0),
        ((2.0, 0.2, 1.9), 46.469, 1),
        ((-1.0, 3.0, -2.0), -153.435, 2),
        ((-2.0, 0.0, 1.9), -46.469, 3),
        ((0.0, 2.0, 0.0), 0.0, 0),  # straight above
    ],
)
def test_view_azimuth_and_quarter_turn(position, azimuth, quarter):
    m = cam_to_box(position)
    assert profile.view_azimuth_deg(m) == pytest.approx(azimuth, abs=1e-3)
    assert profile.view_quarter_turn(m) == quarter
    # Row by row, as the job carries it: the translation is at 3, 7 and 11.
    flat = [1, 0, 0, position[0], 0, 1, 0, position[1], 0, 0, 1, position[2], 0, 0, 0, 1]
    assert profile.view_azimuth_deg(flat) == pytest.approx(azimuth, abs=1e-3)
    assert profile.view_quarter_turn(flat) == quarter


def test_view_quarter_turn_means_what_rotate_means():
    # A post on the side where the camera stands: turning the profile by the view's k brings it to the front.
    for position in ((0.2, 1.0, 2.0), (2.0, 1.0, -0.3), (0.1, 1.0, -2.0), (-2.0, 1.0, 0.4)):
        side = np.array([position[0], 0.0, position[2]])
        side = side / np.abs(side).max() * 0.375  # the middle of the edge strip on that side
        post = glb_box((side[0] - 0.125, 0.0, side[2] - 0.125), (side[0] + 0.125, 1.0, side[2] + 0.125))
        p = profile.of_mesh(*post, (1.0, 1.0, 1.0))
        k = profile.view_quarter_turn(cam_to_box(position))
        front = grid(profile.rotate(p, k))
        assert front[12:].any() and not front[:12].any()
        # And a mesh whose own +Z side is the one the camera saw stands on the camera's side of the box
        # after (4 - k) % 4 turns of _YAW90: the same test puts it at the front.
        seen_side = glb_box((-0.125, 0.0, 0.25), (0.125, 1.0, 0.5))
        placed = profile.of_mesh(turned(seen_side[0], (4 - k) % 4), seen_side[1], (1.0, 1.0, 1.0))
        placed = grid(profile.rotate(placed, k))
        assert placed[12:].any() and not placed[:12].any()
        centre = turned(seen_side[0], (4 - k) % 4).mean(axis=0) * [-1.0, 1.0, 1.0]  # in Unity's frame
        assert centre[0] * position[0] + centre[2] * position[2] > 0.7  # towards the camera: 0.375 m x about 2 m


def test_view_yaw_rejects_a_malformed_matrix():
    with pytest.raises(ValueError):
        profile.view_quarter_turn([1.0, 0.0, 0.0])


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
    f = np.concatenate([np.stack([i00, i01, i11], -1).reshape(-1, 3), np.stack([i00, i11, i10], -1).reshape(-1, 3)])
    lo, hi = v.min(axis=0), v.max(axis=0)
    v = (v - [(lo[0] + hi[0]) / 2, lo[1], (lo[2] + hi[2]) / 2]) * (np.asarray(dims) / (hi - lo))
    return v, f


def test_speed_with_15000_triangles():
    dims = (0.9, 0.8, 0.7)
    v, f = lumpy_torus(dims=dims)
    assert len(f) == 15000
    best = math.inf
    for _ in range(3):
        start = time.perf_counter()
        p = profile.of_mesh(v, f, dims)
        best = min(best, time.perf_counter() - start)
    assert best < 0.5, f"{best:.3f} s"
    assert 0.5 < profile.coverage(p) < 1.0  # the hole of the torus is open
    assert p.max() >= 240  # the top of the surface, less what lies between two rays
    assert np.array_equal(p, profile.of_mesh(v, f, dims))  # the same input, the same bytes
