"""The object profile on the Forge's side of the fit (task MQ-D07; mesh design
sections 5.2 and 5.3 and Appendix B).

The phone sends, with each object, a 16 x 16 grid of heights it measured by
casting rays straight down through the scan. The same rays are cast here
through a generated mesh, so the two grids can be compared cell by cell and the
orientation that fits the scan best can be chosen.

Frames. A profile is always indexed in Unity's box frame: origin at the centre
of the base, +Y up, local +X along ``w`` and local +Z along ``d``; cell (i, j)
has index ``j * 16 + i`` and row 0 lies at the lowest z. Mesh vertices inside
the Forge are in the GLB frame, whose x is the negative of Unity's
(``x_unity = -x_glb``, y and z equal); ``of_mesh`` does that flip, nothing else
in this module needs it.

Every function is pure: the same input gives the same output.
"""
from __future__ import annotations

import base64
import binascii
import math
from typing import Sequence

import numpy as np

GRID = 16  # cells per side
CELLS = GRID * GRID
RAY_START_ABOVE = 0.05  # metres above the top of the box where each ray starts
MIN_HEIGHT = 0.02  # metres above the base; a hit below this does not count
_QUARTER_POINTS = (0.25, 0.75)  # where the two rays per cell and axis stand, in cells
_RAYS = GRID * len(_QUARTER_POINTS)  # rays per side: 32
_PAIR_CHUNK = 1_000_000  # ray-triangle pairs tested at once


# --------------------------------------------------------------------------- the bytes


def _as_profile(profile) -> np.ndarray:
    values = np.asarray(profile)
    if values.shape != (CELLS,):
        raise ValueError(f"a profile holds exactly {CELLS} values, not shape {values.shape}")
    if values.dtype != np.uint8:
        if not np.issubdtype(values.dtype, np.integer) or values.min() < 0 or values.max() > 255:
            raise ValueError("a profile holds whole numbers from 0 to 255")
        values = values.astype(np.uint8)
    return values


def decode(text: str) -> np.ndarray:
    """The 256 bytes of a ``profile`` key (standard base64 with padding), as uint8 of shape (256,).

    Raises ValueError unless the text decodes to exactly 256 bytes.
    """
    if not isinstance(text, str):
        raise ValueError("a profile is sent as base64 text")
    try:
        data = base64.b64decode(text.encode("ascii"), validate=True)
    except (binascii.Error, UnicodeEncodeError) as exc:
        raise ValueError(f"the profile is not standard base64: {exc}") from exc
    if len(data) != CELLS:
        raise ValueError(f"the profile decodes to {len(data)} bytes, not {CELLS}")
    return np.frombuffer(data, dtype=np.uint8).copy()


def encode(profile) -> str:
    """The profile as the RoomModel and the job carry it: 344 characters of standard base64."""
    return base64.b64encode(_as_profile(profile).tobytes()).decode("ascii")


def coverage(profile) -> float:
    """The share of the cells in which a ray hit something: cells that are not 0, divided by 256."""
    return int(np.count_nonzero(_as_profile(profile))) / CELLS


def rotate(profile, quarter_turns: int) -> np.ndarray:
    """The profile after the box's yaw has been increased by ``quarter_turns`` quarter turns.

    With k = 1 the new local +Z is the old local +X and the new local +X is the
    old local -Z, so what stood on -Z (the back of a chair) stands on +X. Seen
    from the mesh, that is the same object turned by one ``_YAW90`` of clean-up
    (x' = z, z' = -x in the GLB frame), which is a yaw of -90 degrees in Unity.
    """
    grid = _as_profile(profile).reshape(GRID, GRID)  # [j, i]
    k = int(quarter_turns) % 4
    i, j = np.meshgrid(np.arange(GRID), np.arange(GRID))  # i[j', i'] = i', j[j', i'] = j'
    last = GRID - 1
    if k == 0:
        source_i, source_j = i, j
    elif k == 1:
        source_i, source_j = j, last - i
    elif k == 2:
        source_i, source_j = last - i, last - j
    else:
        source_i, source_j = last - j, i
    return np.ascontiguousarray(grid[source_j, source_i]).reshape(CELLS)


# --------------------------------------------------------------------------- the rays


def _ray_lines(extent: float) -> np.ndarray:
    """Where the 32 rays of one axis stand, ascending: two per cell, at its quarter points."""
    cell = np.repeat(np.arange(GRID, dtype=np.float64), len(_QUARTER_POINTS))
    quarter = np.tile(np.asarray(_QUARTER_POINTS, dtype=np.float64), GRID)
    return -extent / 2.0 + (cell + quarter) * extent / GRID


def _edge_function(ax, az, bx, bz, px, pz) -> np.ndarray:
    """Twice the signed area of (a, b, p) in the x-z plane.

    The edge is always walked from its lower end to its higher one, so the two
    triangles on either side of a shared edge get exactly opposite numbers and
    a ray can never slip between them.
    """
    swap = (ax > bx) | ((ax == bx) & (az > bz))
    lx, lz = np.where(swap, bx, ax), np.where(swap, bz, az)
    hx, hz = np.where(swap, ax, bx), np.where(swap, az, bz)
    value = (hx - lx) * (pz - lz) - (hz - lz) * (px - lx)
    return np.where(swap, -value, value)


def _ray_heights(x: np.ndarray, y: np.ndarray, z: np.ndarray, w: float, h: float, d: float) -> np.ndarray:
    """The highest counted hit of each of the 32 x 32 rays, -inf where there is none; index n * 32 + m.

    ``x``, ``y`` and ``z`` hold the three corners of every triangle, shape (T, 3), in Unity's box frame.
    """
    heights = np.full(_RAYS * _RAYS, -np.inf)
    xs, zs = _ray_lines(w), _ray_lines(d)
    # The rays inside each triangle's footprint, edges included.
    m0 = np.searchsorted(xs, x.min(axis=1), side="left")
    m1 = np.searchsorted(xs, x.max(axis=1), side="right")
    n0 = np.searchsorted(zs, z.min(axis=1), side="left")
    n1 = np.searchsorted(zs, z.max(axis=1), side="right")
    across = m1 - m0
    count = across * (n1 - n0)
    triangles = np.flatnonzero(count > 0)
    count = count[triangles]
    ends = np.cumsum(count)
    top = h + RAY_START_ABOVE

    start = 0
    while start < len(triangles):
        done = int(ends[start - 1]) if start else 0
        stop = max(start + 1, int(np.searchsorted(ends, done + _PAIR_CHUNK, side="right")))
        t, c = triangles[start:stop], count[start:stop]
        start = stop

        owner = np.repeat(np.arange(len(t)), c)
        within = np.arange(int(c.sum())) - np.repeat(np.cumsum(c) - c, c)
        face = t[owner]
        m = m0[face] + within % across[face]
        n = n0[face] + within // across[face]
        px, pz = xs[m], zs[n]

        ax, ay, az = x[face, 0], y[face, 0], z[face, 0]
        bx, by, bz = x[face, 1], y[face, 1], z[face, 1]
        cx, cy, cz = x[face, 2], y[face, 2], z[face, 2]
        to_c = _edge_function(ax, az, bx, bz, px, pz)  # the weight of corner c
        to_a = _edge_function(bx, bz, cx, cz, px, pz)
        to_b = _edge_function(cx, cz, ax, az, px, pz)
        total = to_a + to_b + to_c
        inside = ((to_a >= 0) & (to_b >= 0) & (to_c >= 0)) | ((to_a <= 0) & (to_b <= 0) & (to_c <= 0))
        # A triangle that stands upright has no footprint: the ray runs along it and does not hit it.
        inside &= total != 0
        safe = np.where(total != 0, total, 1.0)
        # Measured from corner a, so a level triangle gives its own height exactly.
        height = ay + (to_b * (by - ay) + to_c * (cy - ay)) / safe
        counted = inside & (height >= MIN_HEIGHT) & (height <= top)
        np.maximum.at(heights, (n * _RAYS + m)[counted], height[counted])
    return heights


def of_mesh(vertices: np.ndarray, faces: np.ndarray, dims: Sequence[float]) -> np.ndarray:
    """The profile of a mesh that stands in the Forge's working frame.

    ``vertices`` are in the GLB frame (x is the negative of Unity's), metres,
    origin at the centre of the base, +Y up, already scaled to the box
    ``dims = (w, h, d)``. The result is indexed in Unity's box frame, so it
    compares cell by cell with the profile the phone sent.

    The rays, the inclusive edges, the 0.02 m rule and the byte are those of
    the scan side. No triangle is left out: a generated mesh has no floor.
    """
    w, h, d = (float(v) for v in dims)
    if not (w > 0.0 and h > 0.0 and d > 0.0):
        raise ValueError(f"invalid box size {list(dims)}")
    v = np.asarray(vertices, dtype=np.float64).reshape(-1, 3)
    f = np.asarray(faces, dtype=np.int64).reshape(-1, 3)
    if len(f) == 0:
        return np.zeros(CELLS, dtype=np.uint8)

    corners = v[f]  # (T, 3 corners, 3 axes)
    rays = _ray_heights(-corners[:, :, 0], corners[:, :, 1], corners[:, :, 2], w, h, d)
    # Rays (m, n) = (2i + a, 2j + b): the cell keeps the highest of its four.
    cells = rays.reshape(GRID, 2, GRID, 2).max(axis=(1, 3)).reshape(CELLS)
    hit = np.isfinite(cells)
    value = np.clip(np.floor(255.0 * np.where(hit, cells, 0.0) / h + 0.5), 1, 255)
    return np.where(hit, value, 0).astype(np.uint8)


# --------------------------------------------------------------------------- the fit


def fit_score(scan, generated) -> float:
    """How far a generated mesh's profile lies from the scanned one; lower is better, 0 is a perfect match.

    The mean, over the cells the scan saw, of |generated - scanned| in
    fractions of the box height. A generated cell with no surface counts as
    height 0. ``math.inf`` when the scan saw no cell at all.
    """
    p = _as_profile(scan).astype(np.int64)
    g = _as_profile(generated).astype(np.int64)
    seen = p > 0
    cells = int(seen.sum())
    if cells == 0:
        return math.inf
    return int(np.abs(g[seen] - p[seen]).sum()) / (255.0 * cells)


# --------------------------------------------------------------------------- the view's yaw


def _camera_position(cam_to_box) -> tuple[float, float, float]:
    m = np.asarray(cam_to_box, dtype=np.float64)
    if m.size != 16:
        raise ValueError("camToBox holds 16 numbers")
    m = m.reshape(4, 4)
    return float(m[0, 3]), float(m[1, 3]), float(m[2, 3])


def view_azimuth_deg(cam_to_box) -> float:
    """Where the camera of a view stands about the box: atan2(x, z) in Unity's box frame, degrees in (-180, 180].

    ``cam_to_box`` is the view's ``camToBox``: 16 numbers row by row, or a
    4 x 4 array. 0 is the front (+Z), 90 the box's +X side.
    """
    x, _, z = _camera_position(cam_to_box)
    return math.degrees(math.atan2(x, z))


def view_quarter_turn(cam_to_box) -> int:
    """The side of the box the camera of a view stands on, as the k of ``rotate``.

    0 for +Z, 1 for +X, 2 for -Z, 3 for -X: the nearest quarter turn to the
    view's azimuth. Increasing the box's yaw by k quarter turns
    (``rotate(profile, k)``) brings the camera to the front.
    """
    return int(math.floor(view_azimuth_deg(cam_to_box) / 90.0 + 0.5)) % 4
