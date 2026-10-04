"""Colour per face from the scan photos (task MQ-D09; mesh design section 5.3,
"Colour per face", decision M8).

Each face of a cleaned mesh looks up its colour in the photos that see it and
takes the nearest object cell of the palette. The cells are then smoothed into
a few regions, so that a mesh carries at most a handful of flat colours and the
shader, the palette and the vertex format stay as they are.

Frames. The mesh is in the Forge's working frame: the GLB frame (x is the
negative of Unity's), metres, origin at the centre of the base, front faces
counter-clockwise. A view's ``cam_to_box`` carries a point of the camera frame
(Unity: +X right, +Y up, +Z forward) into Unity's box frame, and a camera point
(x, y, z) with z > 0 lands on pixel (cx + fx * x / z, cy - fy * y / z) with the
origin at the top-left of the crop; pixel (column, row) covers
[column, column + 1) x [row, row + 1).

Every function is pure: the same input gives the same output.
"""
from __future__ import annotations

import heapq
from dataclasses import dataclass
from typing import Optional, Sequence

import numpy as np

from .palette import PaletteEntry, nearest_entry

MIN_COSINE = 0.2  # a face is seen only when it turns towards the camera by more than this
MATTE_SOLID = 0.5  # the matte at a pixel that belongs to the object
WINDOW = 5  # the side of the square of pixels averaged around a face's centre
OCCLUSION_TOLERANCE = 0.01  # metres; a surface this close in front of a face does not hide it
SMOOTHING_PASSES = 3
MIN_REGION_SHARE = 0.02  # of the surface; smaller regions merge into a neighbour
SPECKLE_SHARE = 0.5  # of the seen faces; when more change cell in smoothing, the object takes one cell
DEFAULT_MAX_CELLS = 6

_NEAR = 1e-6  # metres in front of the camera
_BINS = 64  # the picture is cut into at most this many bins per side to find what may hide a face
_MAX_BIN_ENTRIES = 4_000_000
_PAIR_CHUNK = 400_000  # ray-triangle pairs tested at once
_MAX_PAIRS = 12_000_000  # per view; an ordinary mesh of 15,000 triangles needs well under a million
_NO_LIMIT = 1 << 30  # as ``max_cells``: merge the small regions only
_MAX_VALENCE = 32  # faces round one corner, beyond which the corner links no faces in the flood

_SRGB = np.arange(256, dtype=np.float64) / 255.0
_SRGB_TO_LINEAR = np.where(_SRGB <= 0.04045, _SRGB / 12.92, ((_SRGB + 0.055) / 1.055) ** 2.4)
_SRGB_TO_LINEAR.setflags(write=False)


@dataclass
class ColourView:
    """One photo of the object and where its camera stood."""

    rgb: np.ndarray  # (H, W, 3) uint8: the original crop, row 0 at the top
    alpha: Optional[np.ndarray]  # (H, W) matte, 0..1 (whole numbers are read as 0..255); None: everything is the object
    intrinsics: Sequence[float]  # fx, fy, cx, cy of the crop, in crop pixels
    cam_to_box: np.ndarray  # 4 x 4 (or 16 numbers row by row): camera frame to Unity's box frame, metres


# --------------------------------------------------------------------------- small helpers


def _face_normals(vertices: np.ndarray, faces: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    cross = np.cross(vertices[faces[:, 1]] - vertices[faces[:, 0]], vertices[faces[:, 2]] - vertices[faces[:, 0]])
    length = np.linalg.norm(cross, axis=1)
    normals = np.where(length[:, None] > 1e-20, cross / np.maximum(length, 1e-20)[:, None], [0.0, 1.0, 0.0])
    return normals, 0.5 * length


def _expand(counts: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """For groups of the given sizes: which group each item belongs to, and its place inside the group."""
    owner = np.repeat(np.arange(len(counts)), counts)
    within = np.arange(int(counts.sum())) - np.repeat(np.cumsum(counts) - counts, counts)
    return owner, within


def _chunks(counts: np.ndarray, limit: int):
    """Cut a run of groups into slices holding about ``limit`` items each."""
    ends = np.cumsum(counts)
    start = 0
    while start < len(counts):
        done = int(ends[start - 1]) if start else 0
        stop = max(start + 1, int(np.searchsorted(ends, done + limit, side="right")))
        yield start, stop
        start = stop


def _to_srgb(linear: np.ndarray) -> np.ndarray:
    c = np.clip(linear, 0.0, 1.0)
    encoded = np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1.0 / 2.4) - 0.055)
    return np.rint(encoded * 255.0).astype(np.int64)


def _nearest_cells(rgb: np.ndarray) -> np.ndarray:
    """The palette cell of each sRGB colour (whole numbers, 0-255), through today's ``nearest_entry``."""
    key = (rgb[:, 0] << 16) | (rgb[:, 1] << 8) | rgb[:, 2]
    unique, inverse = np.unique(key, return_inverse=True)
    cells = [nearest_entry(((k >> 16) & 255, (k >> 8) & 255, k & 255)).index for k in unique.tolist()]
    return np.asarray(cells, dtype=np.int64)[inverse.reshape(-1)]


# --------------------------------------------------------------------------- what a view sees


def _cross(a: np.ndarray, b: np.ndarray) -> np.ndarray:
    return np.stack([a[:, 1] * b[:, 2] - a[:, 2] * b[:, 1],
                     a[:, 2] * b[:, 0] - a[:, 0] * b[:, 2],
                     a[:, 0] * b[:, 1] - a[:, 1] * b[:, 0]], axis=1)


def _blocks(triangle: np.ndarray, target: np.ndarray, distance: np.ndarray) -> np.ndarray:
    """Whether each triangle cuts the line from the camera (the origin) to its target before the line ends.

    A cut within ``OCCLUSION_TOLERANCE`` of the target does not count, so a
    face never hides itself. Both sides of a triangle block.
    """
    a = triangle[:, 0]
    e1, e2 = triangle[:, 1] - a, triangle[:, 2] - a
    p = _cross(target, e2)
    det = np.einsum("ij,ij->i", e1, p)
    crosses = np.abs(det) > 1e-18
    inverse = 1.0 / np.where(crosses, det, 1.0)
    s = -a
    u = np.einsum("ij,ij->i", s, p) * inverse
    q = _cross(s, e1)
    v = np.einsum("ij,ij->i", target, q) * inverse
    t = np.einsum("ij,ij->i", e2, q) * inverse
    return crosses & (u >= 0) & (v >= 0) & (u + v <= 1) & (t > 0) & (t * distance < distance - OCCLUSION_TOLERANCE)


def _hidden(corners: np.ndarray, target: np.ndarray, u: np.ndarray, v: np.ndarray,
            intrinsics: tuple[float, float, float, float], width: int, height: int) -> np.ndarray:
    """For each face asked about: does a triangle lie between the camera and the face's centre?

    ``corners`` holds every triangle in the camera frame, (F, 3, 3); ``target``
    the centres asked about, in the camera frame, and ``u``, ``v`` the pixel
    each lands on. The picture is cut into bins, each listing the triangles
    whose footprint touches it, so a ray is tested only against the triangles
    that could cover its pixel.

    A mesh so tangled that this would take more than ``_MAX_PAIRS`` tests
    cannot be coloured from the view: every face is then reported hidden.
    """
    fx, fy, cx, cy = intrinsics
    hidden = np.zeros(len(target), dtype=bool)
    distance = np.linalg.norm(target, axis=1)
    ahead = corners[:, :, 2] > _NEAR
    whole = np.flatnonzero(ahead.all(axis=1))
    # A triangle that crosses the camera's plane has no footprint in the picture: every ray is tested against it.
    crossing = np.flatnonzero(ahead.any(axis=1) & ~ahead.all(axis=1))
    pairs = len(target) * len(crossing)

    if len(whole):
        pu = cx + fx * corners[whole, :, 0] / corners[whole, :, 2]
        pv = cy - fy * corners[whole, :, 1] / corners[whole, :, 2]
        lo_u, hi_u = pu.min(axis=1) - 1e-6, pu.max(axis=1) + 1e-6
        lo_v, hi_v = pv.min(axis=1) - 1e-6, pv.max(axis=1) + 1e-6
        on_picture = (hi_u >= 0) & (lo_u <= width) & (hi_v >= 0) & (lo_v <= height)
        whole = whole[on_picture]
        lo_u, hi_u, lo_v, hi_v = lo_u[on_picture], hi_u[on_picture], lo_v[on_picture], hi_v[on_picture]
    if len(whole):
        bins = _BINS
        while True:
            size = max(width, height) / bins
            first_u = np.clip(np.floor(lo_u / size), 0, bins - 1).astype(np.int64)
            last_u = np.clip(np.floor(hi_u / size), 0, bins - 1).astype(np.int64)
            first_v = np.clip(np.floor(lo_v / size), 0, bins - 1).astype(np.int64)
            last_v = np.clip(np.floor(hi_v / size), 0, bins - 1).astype(np.int64)
            span = last_u - first_u + 1
            entries = span * (last_v - first_v + 1)
            if bins == 1 or int(entries.sum()) <= _MAX_BIN_ENTRIES:
                break
            bins //= 2
        owner, within = _expand(entries)
        bin_of = (first_v[owner] + within // span[owner]) * bins + first_u[owner] + within % span[owner]
        order = np.argsort(bin_of, kind="stable")
        listed = owner[order]  # places in ``whole``, bin after bin
        starts = np.searchsorted(bin_of[order], np.arange(bins * bins + 1))
        query_bin = (np.clip(np.floor(v / size), 0, bins - 1).astype(np.int64) * bins
                     + np.clip(np.floor(u / size), 0, bins - 1).astype(np.int64))
        per_query = starts[query_bin + 1] - starts[query_bin]
        pairs += int(per_query.sum())
    if pairs > _MAX_PAIRS:
        return np.ones(len(target), dtype=bool)

    def test(pair_query: np.ndarray, pair_triangle: np.ndarray) -> None:
        hit = _blocks(corners[pair_triangle], target[pair_query], distance[pair_query])
        hidden[pair_query[hit]] = True

    if len(whole):
        for start, stop in _chunks(per_query, _PAIR_CHUNK):
            owner, within = _expand(per_query[start:stop])
            pair_query = owner + start
            place = listed[starts[query_bin[pair_query]] + within]
            # Most triangles of a bin do not reach the pixel itself.
            covers = ((u[pair_query] >= lo_u[place]) & (u[pair_query] <= hi_u[place])
                      & (v[pair_query] >= lo_v[place]) & (v[pair_query] <= hi_v[place]))
            test(pair_query[covers], whole[place[covers]])
    if len(crossing):
        per_query = np.full(len(target), len(crossing), dtype=np.int64)
        for start, stop in _chunks(per_query, _PAIR_CHUNK):
            owner, within = _expand(per_query[start:stop])
            test(owner + start, crossing[within])
    return hidden


def _window_mean(rgb: np.ndarray, alpha: Optional[np.ndarray], row: np.ndarray, column: np.ndarray) -> np.ndarray:
    """The mean, in linear light, of the object's pixels in the square around each (row, column)."""
    height, width = rgb.shape[:2]
    reach = np.arange(WINDOW) - WINDOW // 2
    dy, dx = np.repeat(reach, WINDOW), np.tile(reach, WINDOW)
    r, c = row[:, None] + dy[None, :], column[:, None] + dx[None, :]
    counted = (r >= 0) & (r < height) & (c >= 0) & (c < width)
    r, c = np.clip(r, 0, height - 1), np.clip(c, 0, width - 1)
    if alpha is not None:
        counted &= alpha[r, c] >= MATTE_SOLID
    linear = _SRGB_TO_LINEAR[rgb[r, c]]  # (faces, pixels, 3)
    return (linear * counted[:, :, None]).sum(axis=1) / counted.sum(axis=1)[:, None]


def _read_view(view: ColourView):
    rgb = np.asarray(view.rgb)
    if rgb.ndim != 3 or rgb.shape[2] < 3 or rgb.dtype != np.uint8:
        raise ValueError("a view's picture is (H, W, 3) uint8")
    rgb = rgb[:, :, :3]
    alpha = None
    if view.alpha is not None:
        alpha = np.asarray(view.alpha)
        if alpha.shape != rgb.shape[:2]:
            raise ValueError(f"the matte {alpha.shape} does not have the picture's size {rgb.shape[:2]}")
        alpha = alpha / 255.0 if np.issubdtype(alpha.dtype, np.integer) else alpha.astype(np.float64)
    intrinsics = tuple(float(k) for k in view.intrinsics)
    if len(intrinsics) != 4 or not (intrinsics[0] > 0 and intrinsics[1] > 0):
        raise ValueError("a view's intrinsics are fx, fy, cx, cy with positive focal lengths")
    cam_to_box = np.asarray(view.cam_to_box, dtype=np.float64)
    if cam_to_box.size != 16:
        raise ValueError("camToBox holds 16 numbers")
    return rgb, alpha, intrinsics, cam_to_box.reshape(4, 4)


def _seen_by(view: ColourView, vertices: np.ndarray, faces: np.ndarray, centres: np.ndarray, normals: np.ndarray):
    """The faces one view sees, the cosine each turns to the camera with, and its colour there in linear light."""
    rgb, alpha, intrinsics, cam_to_box = _read_view(view)
    height, width = rgb.shape[:2]
    fx, fy, cx, cy = intrinsics
    try:
        box_to_cam = np.linalg.inv(cam_to_box)
    except np.linalg.LinAlgError as exc:
        raise ValueError("camToBox cannot be inverted") from exc
    # The mesh is in the GLB frame and the camera in Unity's: x changes sign on the way.
    to_cam = box_to_cam @ np.diag([-1.0, 1.0, 1.0, 1.0])
    eye = np.array([-cam_to_box[0, 3], cam_to_box[1, 3], cam_to_box[2, 3]])

    to_eye = eye[None, :] - centres
    cosine = np.einsum("ij,ij->i", normals, to_eye) / np.maximum(np.linalg.norm(to_eye, axis=1), 1e-12)
    cam = vertices @ to_cam[:3, :3].T + to_cam[:3, 3]
    corners = cam[faces]
    centre = corners.mean(axis=1)
    candidate = (cosine > MIN_COSINE) & (centre[:, 2] > _NEAR)
    depth = np.where(candidate, centre[:, 2], 1.0)
    u = cx + fx * centre[:, 0] / depth
    v = cy - fy * centre[:, 1] / depth
    candidate &= (u >= 0) & (u < width) & (v >= 0) & (v < height)
    column = np.clip(np.floor(np.where(candidate, u, 0.0)), 0, width - 1).astype(np.int64)
    row = np.clip(np.floor(np.where(candidate, v, 0.0)), 0, height - 1).astype(np.int64)
    if alpha is not None:
        candidate &= alpha[row, column] >= MATTE_SOLID
    query = np.flatnonzero(candidate)
    if len(query) == 0:
        return query, np.zeros(0), np.zeros((0, 3))
    hidden = _hidden(corners, centre[query], u[query], v[query], intrinsics, width, height)
    seen = query[~hidden]
    return seen, cosine[seen], _window_mean(rgb, alpha, row[seen], column[seen])


# --------------------------------------------------------------------------- neighbours and regions


def _corner_ids(vertices: np.ndarray, faces: np.ndarray) -> np.ndarray:
    """The faces with every vertex named by its position, so a mesh whose vertices
    were split at creases is still one surface."""
    _, vertex = np.unique(vertices + 0.0, axis=0, return_inverse=True)
    return vertex.reshape(-1)[faces]


def _edge_neighbours(corner: np.ndarray) -> np.ndarray:
    """For each face, the face across each of its three edges (edge k runs from corner k to k + 1); -1 for none.

    Where more than two faces share an edge they are linked in a ring.
    """
    a, b = corner, corner[:, [1, 2, 0]]
    key = (np.minimum(a, b) * (int(corner.max()) + 1) + np.maximum(a, b)).reshape(-1)
    order = np.argsort(key, kind="stable")
    place = np.arange(len(order))
    first = np.r_[True, key[order][1:] != key[order][:-1]]
    group_start = np.maximum.accumulate(np.where(first, place, 0))
    last = np.r_[first[1:], True]
    neighbour = np.empty(len(order), dtype=np.int64)
    neighbour[order] = order[np.where(last, group_start, place + 1)] // 3
    neighbour = neighbour.reshape(-1, 3)
    neighbour[neighbour == np.arange(len(corner))[:, None]] = -1
    return neighbour


def _touching(corner: np.ndarray, neighbour: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """For each face, the faces that share a corner or an edge with it: ``others[starts[f]:starts[f + 1]]``.

    A corner where more than ``_MAX_VALENCE`` faces meet links nothing, so a
    fan of needles cannot make the list quadratic; its faces stay linked across
    their edges.
    """
    count = len(corner)
    vertex = corner.reshape(-1)
    order = np.argsort(vertex, kind="stable")  # corners, vertex after vertex
    starts = np.searchsorted(vertex[order], np.arange(int(vertex.max()) + 2))
    valence = starts[1:] - starts[:-1]
    per_corner = np.where(valence[vertex] <= _MAX_VALENCE, valence[vertex], 0)
    owner, within = _expand(per_corner)
    face = np.concatenate([owner // 3, np.repeat(np.arange(count), 3)])
    other = np.concatenate([order[starts[vertex[owner]] + within] // 3, neighbour.reshape(-1)])
    keep = (other >= 0) & (other != face)
    pair = np.unique(face[keep] * count + other[keep])
    return np.searchsorted(pair // count, np.arange(count + 1)), pair % count


def _smooth(cells: np.ndarray, has_cell: np.ndarray, neighbour: np.ndarray) -> np.ndarray:
    """One pass: a face takes the cell that at least two of its three neighbours share."""
    there = neighbour >= 0
    index = np.where(there, neighbour, 0)
    around = np.where(there & has_cell[index], cells[index], -1)
    c0, c1, c2 = around[:, 0], around[:, 1], around[:, 2]
    shared = np.where((c0 >= 0) & ((c0 == c1) | (c0 == c2)), c0, np.where((c1 >= 0) & (c1 == c2), c1, -1))
    return np.where(has_cell & (shared >= 0), shared, cells)


def _regions(cells: np.ndarray, has_cell: np.ndarray, neighbour: np.ndarray) -> np.ndarray:
    """A name for the region of each face: the lowest face index among the edge-connected faces of its cell."""
    labels = np.arange(len(cells))
    face, edge = np.nonzero(neighbour >= 0)
    other = neighbour[face, edge]
    same = has_cell[face] & has_cell[other] & (cells[face] == cells[other])
    a, b = face[same], other[same]
    while len(a):
        low = labels.copy()  # the lowest name next to each face
        np.minimum.at(low, a, labels[b])
        np.minimum.at(low, b, labels[a])
        new = low.copy()  # ... handed on to the face that names its group
        np.minimum.at(new, labels, low)
        new = np.minimum(new, new[labels])
        while True:
            jumped = new[new]
            if np.array_equal(jumped, new):
                break
            new = jumped
        if np.array_equal(new, labels):
            break
        labels = new
    return labels


def _merge_regions(cells: np.ndarray, has_cell: np.ndarray, neighbour: np.ndarray, areas: np.ndarray,
                   min_area: float, max_cells: int) -> np.ndarray:
    """Merge every region under ``min_area`` into its largest neighbour, then the smallest regions
    until at most ``max_cells`` cells remain. Faces without a cell are left alone and join nothing."""
    with_cell = np.flatnonzero(has_cell)
    if len(with_cell) == 0:
        return cells
    names, region = np.unique(_regions(cells, has_cell, neighbour)[with_cell], return_inverse=True)
    region = region.reshape(-1)
    count = len(names)
    area = np.bincount(region, weights=areas[with_cell], minlength=count).tolist()
    cell = cells[names].tolist()
    live: dict[int, int] = {}  # regions per cell
    surface: dict[int, float] = {}  # area per cell
    for c, a in zip(cell, area):
        live[c] = live.get(c, 0) + 1
        surface[c] = surface.get(c, 0.0) + a
    if min(area) >= min_area and len(live) <= max_cells:
        return cells

    region_of = np.full(len(cells), -1, dtype=np.int64)
    region_of[with_cell] = region
    face, edge = np.nonzero(neighbour >= 0)
    ra, rb = region_of[face], region_of[neighbour[face, edge]]
    link = (ra >= 0) & (rb >= 0) & (ra != rb)
    pairs = np.unique(np.minimum(ra, rb)[link] * count + np.maximum(ra, rb)[link])
    adjacent: list[set[int]] = [set() for _ in range(count)]
    for a, b in zip((pairs // count).tolist(), (pairs % count).tolist()):
        adjacent[a].add(b)
        adjacent[b].add(a)

    parent = list(range(count))
    version = [0] * count

    def find(r: int) -> int:
        while parent[r] != r:
            parent[r] = parent[parent[r]]
            r = parent[r]
        return r

    def around(r: int) -> set[int]:
        current = {find(n) for n in adjacent[r]}
        current.discard(r)
        adjacent[r] = current
        return current

    def recolour(r: int, new: int) -> None:
        old = cell[r]
        live[old] -= 1
        surface[old] -= area[r]
        if live[old] == 0:
            del live[old], surface[old]
        live[new] = live.get(new, 0) + 1
        surface[new] = surface.get(new, 0.0) + area[r]
        cell[r] = new

    def join(a: int, b: int) -> int:
        """Two regions of one cell become one; the lower number stays."""
        if b < a:
            a, b = b, a
        big, small = (adjacent[a], adjacent[b]) if len(adjacent[a]) >= len(adjacent[b]) else (adjacent[b], adjacent[a])
        big |= small
        adjacent[a], adjacent[b] = big, set()
        parent[b] = a
        area[a] += area[b]
        live[cell[a]] -= 1
        return a

    def absorb(r: int) -> int:
        """Region ``r`` takes the cell of its largest neighbour and joins every neighbour of that cell.

        Returns the region it has become, or -1 when it has no neighbour.
        """
        beside = around(r)
        if not beside:
            return -1
        target = max(beside, key=lambda n: (area[n], -n))
        recolour(r, cell[target])
        root = r
        for n in sorted(n for n in beside if cell[n] == cell[r]):
            root = join(root, n)
        version[root] += 1
        return root

    heap = [(area[r], r, 0) for r in range(count) if area[r] < min_area]
    heapq.heapify(heap)
    while heap:
        _, r, stamp = heapq.heappop(heap)
        if parent[r] != r or version[r] != stamp:
            continue
        root = absorb(r)
        if root >= 0 and area[root] < min_area:
            heapq.heappush(heap, (area[root], root, version[root]))

    if len(live) > max_cells:
        heap = [(area[r], r, version[r]) for r in range(count) if parent[r] == r]
        heapq.heapify(heap)
        while heap and len(live) > max_cells:
            _, r, stamp = heapq.heappop(heap)
            if parent[r] != r or version[r] != stamp:
                continue
            root = absorb(r)
            if root >= 0:
                heapq.heappush(heap, (area[root], root, version[root]))
            else:
                # An island with nothing beside it: it takes the cell that covers the most surface.
                recolour(r, max((c for c in surface if c != cell[r]), key=lambda c: (surface[c], -c)))

    out = cells.copy()
    out[with_cell] = np.asarray([cell[find(r)] for r in range(count)], dtype=np.int64)[region]
    return out


def _flood(cells: np.ndarray, has_cell: np.ndarray, centres: np.ndarray, corner: np.ndarray,
           neighbour: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Faces without a cell take the cell of the nearest face that has one, measured along the surface.

    The distance runs from face centre to face centre over faces that touch,
    so it does not follow the grain of a regular triangulation as a count of
    edges crossed would. Returns the cells and which faces now have one.
    """
    if has_cell.all():
        return cells, has_cell
    starts, others = _touching(corner, neighbour)
    face = np.repeat(np.arange(len(cells)), starts[1:] - starts[:-1])
    length = np.linalg.norm(centres[face] - centres[others], axis=1).tolist()
    sources = np.unique(face[has_cell[face] & ~has_cell[others]]).tolist()
    starts, others = starts.tolist(), others.tolist()
    label, wanted = cells.tolist(), (~has_cell).tolist()
    best = [np.inf] * len(label)
    heap = [(0.0, f) for f in sources]  # ascending, so already a heap
    for f in sources:
        best[f] = 0.0
    while heap:
        distance, f = heapq.heappop(heap)
        if distance > best[f]:
            continue
        for k in range(starts[f], starts[f + 1]):
            n = others[k]
            if wanted[n] and distance + length[k] < best[n]:
                best[n] = distance + length[k]
                label[n] = label[f]
                heapq.heappush(heap, (best[n], n))
    return np.asarray(label, dtype=np.int64), has_cell | (np.asarray(best) < np.inf)


# --------------------------------------------------------------------------- the public functions


def dominant_cell(cells, areas) -> int:
    """The cell that covers the most surface (the lower cell on a tie); sent as ``AssetReady.palette``."""
    cells = np.asarray(cells, dtype=np.int64).reshape(-1)
    areas = np.asarray(areas, dtype=np.float64).reshape(-1)
    if len(cells) == 0 or len(cells) != len(areas):
        raise ValueError("one cell and one area per face, and at least one face")
    return int(np.argmax(np.bincount(cells, weights=areas)))


def face_cells(vertices: np.ndarray, faces: np.ndarray, views: Sequence[ColourView], fallback: PaletteEntry, *,
               max_cells: int = DEFAULT_MAX_CELLS) -> np.ndarray:
    """One palette cell (64..127) per face, read from the photos.

    ``vertices`` and ``faces`` are the mesh in the Forge's working frame (see
    the top of the module); the faces must be wound counter-clockwise seen from
    outside, as the GLB stores them. ``fallback`` is the cell the whole object
    takes when no view sees any face.

    A view sees a face when the face turns towards the camera (cosine above
    0.2), its centre lands inside the crop and on the matte, and no other
    triangle lies between the camera and that centre. The face's colour is the
    mean of the 5 x 5 pixels around the centre, weighted by the cosine over the
    views that see it, in linear light, and its cell the nearest object cell.
    The cells are smoothed: three passes of two-of-three neighbours, then
    regions under 2 % of the surface merge into their largest neighbour. When
    that changed more than half of the seen faces the picture and the mesh do
    not line up, and every face takes the cell that covered the most area
    before smoothing. Otherwise the smallest regions merge until at most
    ``max_cells`` cells remain, and the faces no view saw take the cell of the
    nearest seen face along the surface.
    """
    if max_cells < 1:
        raise ValueError("max_cells is at least 1")
    v = np.asarray(vertices, dtype=np.float64).reshape(-1, 3)
    f = np.asarray(faces, dtype=np.int64).reshape(-1, 3)
    count = len(f)
    if count == 0:
        return np.zeros(0, dtype=np.int64)
    normals, areas = _face_normals(v, f)
    centres = v[f].mean(axis=1)

    light = np.zeros((count, 3))
    weight = np.zeros(count)
    for view in views:
        seen, cosine, colour = _seen_by(view, v, f, centres, normals)
        light[seen] += colour * cosine[:, None]
        weight[seen] += cosine
    seen = weight > 0
    if not seen.any():
        return np.full(count, fallback.index, dtype=np.int64)

    cells = np.full(count, -1, dtype=np.int64)
    cells[seen] = _nearest_cells(_to_srgb(light[seen] / weight[seen][:, None]))
    before = cells
    corner = _corner_ids(v, f)
    neighbour = _edge_neighbours(corner)
    min_area = MIN_REGION_SHARE * float(areas.sum())
    for _ in range(SMOOTHING_PASSES):
        cells = _smooth(cells, seen, neighbour)
    cells = _merge_regions(cells, seen, neighbour, areas, min_area, _NO_LIMIT)
    if int((cells != before)[seen].sum()) > SPECKLE_SHARE * int(seen.sum()):
        return np.full(count, dominant_cell(before[seen], areas[seen]), dtype=np.int64)
    cells = _merge_regions(cells, seen, neighbour, areas, 0.0, max_cells)

    cells, reached = _flood(cells, seen, centres, corner, neighbour)
    if not reached.all():  # a detached piece no view saw
        cells[~reached] = dominant_cell(cells[reached], areas[reached])
    return _merge_regions(cells, np.ones(count, dtype=bool), neighbour, areas, min_area, max_cells)


def split_by_cell(positions: np.ndarray, normals: np.ndarray, faces: np.ndarray, cells: np.ndarray):
    """Give every vertex one cell: a vertex is duplicated where faces of different cells share it.

    Returns ``(positions, normals, faces, vertex_cells)``. All three vertices
    of a face carry that face's cell; faces keep their order and winding; a
    vertex used by faces of one cell only is not duplicated, so a mesh of one
    cell comes back as it went in. A vertex no face uses is dropped.
    """
    positions, normals = np.asarray(positions), np.asarray(normals)
    f = np.asarray(faces, dtype=np.int64).reshape(-1, 3)
    c = np.asarray(cells, dtype=np.int64).reshape(-1)
    if len(c) != len(f):
        raise ValueError("one cell per face")
    if len(positions) != len(normals):
        raise ValueError("one normal per vertex")
    if len(f) == 0:
        return positions[:0], normals[:0], f, np.zeros(0, dtype=np.int64)
    if c.min() < 0:
        raise ValueError("every face needs a cell")
    base = int(c.max()) + 1
    used, corner = np.unique(f.reshape(-1) * base + np.repeat(c, 3), return_inverse=True)
    source = used // base
    return positions[source], normals[source], corner.reshape(-1, 3), used % base
