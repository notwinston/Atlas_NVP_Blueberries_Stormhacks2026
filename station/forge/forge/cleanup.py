"""Mesh clean-up (task P1-D09; Master Plan section 7.4, state `cleaning`).

Takes whatever a generator returns and produces a GLB that meets the asset
contract: welded, oriented and scaled to the scanned box, origin at the centre
of the box's base, metres, +Y up, decimated to the job's triangle target, no
edge longer than 0.25 m, one palette colour, the shade (face shade times
ambient occlusion) baked into the vertex colours, exactly one mesh and no
material.

Every step is deterministic, so the same input gives the same bytes and the
same content hash.
"""
from __future__ import annotations

import hashlib
from dataclasses import dataclass, field
from typing import Optional, Sequence

import numpy as np

from . import PIPELINE_VER
from .budget import MAX_EDGE_METRES, VALIDATOR_MAX_TRIANGLES, VALIDATOR_MIN_TRIANGLES
from .glb import write_glb
from .palette import PaletteEntry, encode_vertex_attributes, face_shade, nearest_entry
from .validator import longest_edge

# Edges are split against a slightly tighter limit so float32 storage cannot push one over.
_EDGE_MARGIN = 1e-4
_CREASE_DEGREES = 40.0
_AO_FLOOR = 0.35  # the occlusion factor of a fully enclosed vertex
_AO_DIRECTIONS = 32
_AO_STEPS = 10


class CleanupError(RuntimeError):
    """The mesh cannot be brought inside the asset contract."""


@dataclass
class CleanupReport:
    """What clean-up did to one mesh; stored with the job and returned by the API."""

    triangles: int = 0
    vertices: int = 0
    longest_edge: float = 0.0
    size: tuple[float, float, float] = (0.0, 0.0, 0.0)
    box: tuple[float, float, float] = (0.0, 0.0, 0.0)
    scale_error: tuple[float, float, float] = (0.0, 0.0, 0.0)
    source_triangles: int = 0
    tri_target: int = 0
    up_axis: str = "Y"
    yaw_degrees: int = 0
    palette_index: int = 0
    palette_name: str = ""
    palette_rgb: tuple[int, int, int] = (0, 0, 0)
    photo_rgb: tuple[int, int, int] = (0, 0, 0)
    bytes: int = 0
    sha256: str = ""
    notes: list[str] = field(default_factory=list)

    @property
    def max_scale_error(self) -> float:
        return max(abs(e) for e in self.scale_error)

    def to_dict(self) -> dict:
        return {
            "triangles": self.triangles,
            "vertices": self.vertices,
            "longestEdge": round(self.longest_edge, 5),
            "size": [round(v, 5) for v in self.size],
            "box": [round(v, 5) for v in self.box],
            "scaleError": [round(v, 5) for v in self.scale_error],
            "maxScaleError": round(self.max_scale_error, 5),
            "sourceTriangles": self.source_triangles,
            "triTarget": self.tri_target,
            "upAxis": self.up_axis,
            "yawDegrees": self.yaw_degrees,
            "colour": {
                "paletteIndex": self.palette_index,
                "name": self.palette_name,
                "rgb": list(self.palette_rgb),
                "photoRgb": list(self.photo_rgb),
            },
            "bytes": self.bytes,
            "sha256": self.sha256,
            "notes": list(self.notes),
        }


@dataclass
class CleanResult:
    glb: bytes
    report: CleanupReport


# --------------------------------------------------------------------------- welding


def weld(vertices: np.ndarray, faces: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Merge coincident vertices and drop degenerate, duplicate and unreferenced elements."""
    v = np.asarray(vertices, dtype=np.float64).reshape(-1, 3)
    f = np.asarray(faces, dtype=np.int64).reshape(-1, 3)
    if len(f) and (f.min() < 0 or f.max() >= len(v)):
        raise CleanupError("the generator returned faces that index outside the vertex buffer")
    finite = np.isfinite(v).all(axis=1)
    f = f[finite[f].all(axis=1)] if len(f) else f
    if len(f) == 0:
        raise CleanupError("the generator returned no usable triangles")
    used = np.unique(f)
    lo = v[used].min(axis=0)
    diagonal = float(np.linalg.norm(v[used].max(axis=0) - lo))
    if diagonal <= 0.0:
        raise CleanupError("the generated mesh has no extent")

    # Snap to a grid of 1e-5 of the diagonal: at most 1e5 cells per axis, 17 bits each.
    v = np.where(finite[:, None], v, lo)
    q = np.clip(np.rint((v - lo) / (diagonal * 1e-5)), 0, 131071).astype(np.int64)
    key = (q[:, 0] << 34) | (q[:, 1] << 17) | q[:, 2]
    _, first, inverse = np.unique(key, return_index=True, return_inverse=True)
    v, f = v[first], inverse.reshape(-1)[f]

    keep = (f[:, 0] != f[:, 1]) & (f[:, 1] != f[:, 2]) & (f[:, 0] != f[:, 2])
    f = f[keep]
    cross = np.cross(v[f[:, 1]] - v[f[:, 0]], v[f[:, 2]] - v[f[:, 0]])
    f = f[np.linalg.norm(cross, axis=1) > diagonal * diagonal * 1e-12]
    if len(f) == 0:
        raise CleanupError("the generated mesh has no area")
    _, first_face = np.unique(np.sort(f, axis=1), axis=0, return_index=True)
    f = f[np.sort(first_face)]

    used, compact = np.unique(f, return_inverse=True)
    return v[used], compact.reshape(-1, 3)


def drop_floaters(vertices: np.ndarray, faces: np.ndarray, min_share: float = 0.005) -> tuple[np.ndarray, np.ndarray]:
    """Remove connected pieces holding less than ``min_share`` of the surface area.

    Image-to-3D output often carries small detached fragments; left in, they
    would stretch the bounds that the fit to the box is measured on.
    """
    labels = np.arange(len(vertices))
    while True:
        lowest = labels[faces].min(axis=1)
        new = labels.copy()
        np.minimum.at(new, faces.reshape(-1), np.repeat(lowest, 3))
        new = new[new]
        if np.array_equal(new, labels):
            break
        labels = new
    face_label = labels[faces[:, 0]]
    area = 0.5 * np.linalg.norm(
        np.cross(vertices[faces[:, 1]] - vertices[faces[:, 0]], vertices[faces[:, 2]] - vertices[faces[:, 0]]), axis=1
    )
    per_label = np.bincount(face_label, weights=area, minlength=len(vertices))
    keep = per_label[face_label] >= min_share * area.sum()
    if keep.all() or not keep.any():
        return vertices, faces
    faces = faces[keep]
    used, compact = np.unique(faces, return_inverse=True)
    return vertices[used], compact.reshape(-1, 3)


# --------------------------------------------------------------------------- decimation


def _cluster(vertices: np.ndarray, faces: np.ndarray, cells: int) -> tuple[np.ndarray, np.ndarray]:
    lo = vertices.min(axis=0)
    step = float((vertices.max(axis=0) - lo).max()) / cells
    q = np.minimum((vertices - lo) / step, cells - 1e-9).astype(np.int64)
    key = (q[:, 0] * (cells + 1) + q[:, 1]) * (cells + 1) + q[:, 2]
    _, inverse, counts = np.unique(key, return_inverse=True, return_counts=True)
    inverse = inverse.reshape(-1)
    merged = np.stack([np.bincount(inverse, weights=vertices[:, i]) / counts for i in range(3)], axis=1)
    f = inverse[faces]
    f = f[(f[:, 0] != f[:, 1]) & (f[:, 1] != f[:, 2]) & (f[:, 0] != f[:, 2])]
    if len(f):
        _, first = np.unique(np.sort(f, axis=1), axis=0, return_index=True)
        f = f[np.sort(first)]
    return merged, f


def decimate_by_clustering(vertices: np.ndarray, faces: np.ndarray, target: int) -> tuple[np.ndarray, np.ndarray]:
    """Vertex-clustering decimation: crude, but it cannot fail on any topology."""
    low, high, best = 2, 1024, None
    while low <= high:
        mid = (low + high) // 2
        v, f = _cluster(vertices, faces, mid)
        if 0 < len(f) <= target:
            best, low = (v, f), mid + 1
        elif len(f) == 0:
            low = mid + 1
        else:
            high = mid - 1
    if best is None:
        raise CleanupError("the mesh cannot be decimated to the triangle target")
    return weld(*best)


def decimate(vertices: np.ndarray, faces: np.ndarray, target: int) -> tuple[np.ndarray, np.ndarray]:
    """Reduce the mesh to at most ``target`` triangles.

    Quadric edge collapse when the `fast-simplification` package is present,
    vertex clustering otherwise or when the collapse stalls above the target
    (it can on badly non-manifold input).
    """
    if len(faces) <= target:
        return vertices, faces
    try:
        import fast_simplification
    except ImportError:
        return decimate_by_clustering(vertices, faces, target)
    points = np.ascontiguousarray(vertices, dtype=np.float32)
    triangles = np.ascontiguousarray(faces, dtype=np.int32)
    for aggression in (7.0, 10.0):
        try:
            out_v, out_f = fast_simplification.simplify(points, triangles, target_count=int(target), agg=aggression)
            v, f = weld(out_v, out_f)
        except (CleanupError, RuntimeError, ValueError):
            continue
        if len(f) <= target:
            return v, f
    return decimate_by_clustering(vertices, faces, target)


# --------------------------------------------------------------------------- fit to the box

# Proper rotations (no mirroring) from the generator's frame to +Y up.
_UP = (
    ("Y", np.eye(3)),
    ("Z", np.array([[1.0, 0, 0], [0, 0, 1], [0, -1, 0]])),
    ("X", np.array([[0.0, -1, 0], [1, 0, 0], [0, 0, 1]])),
)
_YAW90 = np.array([[0.0, 0, 1], [0, 1, 0], [-1, 0, 0]])


def orient_and_scale(
    vertices: np.ndarray, dims: Sequence[float], max_anisotropy: float = 0.15
) -> tuple[np.ndarray, str, int, tuple[float, float, float]]:
    """Fit the mesh to the scanned box.

    Picks the up axis and the quarter-turn yaw whose bounds best match the box
    (bounds cannot tell 0 from 180 degrees, so only 0 and 90 are distinguished),
    scales uniformly with at most ``max_anisotropy`` of stretch per axis, and
    moves the origin to the centre of the base.

    Returns ``(vertices, up_axis, yaw_degrees, scale_error_per_axis)``.
    """
    box = np.asarray(dims, dtype=np.float64)
    if box.shape != (3,) or (box <= 0).any():
        raise CleanupError(f"invalid box size {list(dims)}")
    best = None
    for up_name, up in _UP:
        for yaw, turn in ((0, np.eye(3)), (90, _YAW90)):
            rotation = turn @ up
            rotated = vertices @ rotation.T
            extent = np.maximum(rotated.max(axis=0) - rotated.min(axis=0), 1e-9)
            log_ratio = np.log(box / extent)
            score = float(((log_ratio - log_ratio.mean()) ** 2).sum())
            # The generator's own frame wins unless another one fits clearly better.
            if best is None or score < best[0] * 0.8 - 1e-9:
                best = (score, up_name, yaw, rotated, extent, log_ratio)
    _, up_name, yaw, rotated, extent, log_ratio = best
    uniform = np.exp(log_ratio.mean())
    stretch = np.clip(np.exp(log_ratio) / uniform, 1.0 - max_anisotropy, 1.0 + max_anisotropy)
    scaled = rotated * (uniform * stretch)
    lo, hi = scaled.min(axis=0), scaled.max(axis=0)
    scaled = scaled - np.array([(lo[0] + hi[0]) * 0.5, lo[1], (lo[2] + hi[2]) * 0.5])
    error = (hi - lo - box) / box
    return scaled, up_name, yaw, (float(error[0]), float(error[1]), float(error[2]))


# --------------------------------------------------------------------------- edge splitting


def _unique_edges(faces: np.ndarray, n_vertices: int):
    a, b = faces, faces[:, [1, 2, 0]]
    key = np.minimum(a, b) * n_vertices + np.maximum(a, b)
    unique, inverse, counts = np.unique(key.reshape(-1), return_inverse=True, return_counts=True)
    return unique, inverse.reshape(-1, 3), counts


def _edge_lengths(vertices: np.ndarray, unique: np.ndarray) -> np.ndarray:
    n = len(vertices)
    return np.linalg.norm(vertices[unique // n] - vertices[unique % n], axis=1)


def _split(vertices, faces, unique, edge_of, marked):
    """Bisect every marked edge; each face is re-cut by how many of its edges are marked.

    Midpoints are shared between the faces on both sides, so no T-junction appears.
    """
    n = len(vertices)
    midpoint = np.full(len(unique), -1, dtype=np.int64)
    midpoint[marked] = n + np.arange(int(marked.sum()))
    vertices = np.vstack([vertices, (vertices[unique[marked] // n] + vertices[unique[marked] % n]) * 0.5])
    m = midpoint[edge_of]
    count = (m >= 0).sum(axis=1)
    out = [faces[count == 0]]

    f, mm = faces[count == 3], m[count == 3]
    if len(f):
        a, b, c, ab, bc, ca = f[:, 0], f[:, 1], f[:, 2], mm[:, 0], mm[:, 1], mm[:, 2]
        out += [np.stack(t, axis=1) for t in ((a, ab, ca), (ab, b, bc), (ca, bc, c), (ab, bc, ca))]

    f, mm = faces[count == 1], m[count == 1]
    if len(f):
        rows, i = np.arange(len(f)), np.argmax(mm >= 0, axis=1)
        a, b, c, ab = f[rows, i], f[rows, (i + 1) % 3], f[rows, (i + 2) % 3], mm[rows, i]
        out += [np.stack(t, axis=1) for t in ((a, ab, c), (ab, b, c))]

    f, mm = faces[count == 2], m[count == 2]
    if len(f):
        # Rotate so the unmarked edge is c-a; the quad a, ab, bc, c is cut along its shorter diagonal.
        rows, j = np.arange(len(f)), np.argmax(mm < 0, axis=1)
        c, a, b = f[rows, j], f[rows, (j + 1) % 3], f[rows, (j + 2) % 3]
        ab, bc = mm[rows, (j + 1) % 3], mm[rows, (j + 2) % 3]
        first = np.linalg.norm(vertices[a] - vertices[bc], axis=1) <= np.linalg.norm(vertices[ab] - vertices[c], axis=1)
        out.append(np.stack((ab, b, bc), axis=1))
        out.append(np.where(first[:, None], np.stack((a, ab, bc), axis=1), np.stack((a, ab, c), axis=1)))
        out.append(np.where(first[:, None], np.stack((a, bc, c), axis=1), np.stack((ab, bc, c), axis=1)))
    return vertices, np.vstack(out)


def split_long_edges(vertices: np.ndarray, faces: np.ndarray, limit: float, max_triangles: int):
    """Split until no edge exceeds ``limit``. Returns None if that needs more than ``max_triangles``."""
    for _ in range(40):
        unique, edge_of, _ = _unique_edges(faces, len(vertices))
        marked = _edge_lengths(vertices, unique) > limit
        if not marked.any():
            return vertices, faces
        if len(faces) + int(marked[edge_of].sum()) > max_triangles:
            return None
        vertices, faces = _split(vertices, faces, unique, edge_of, marked)
    return None


def raise_triangle_count(vertices: np.ndarray, faces: np.ndarray, minimum: int, maximum: int):
    """Split the longest edges until the mesh has at least ``minimum`` triangles."""
    while len(faces) < minimum:
        unique, edge_of, counts = _unique_edges(faces, len(vertices))
        lengths = _edge_lengths(vertices, unique)
        order = np.lexsort((unique, -lengths))
        # Each split edge adds one triangle per face that shares it.
        added = np.cumsum(counts[order])
        need, room = minimum - len(faces), maximum - len(faces)
        take = int(np.searchsorted(added, need)) + 1
        take = min(take, int(np.searchsorted(added, room, side="right")))
        # Stay among the longer edges so triangles keep an even size.
        take = min(take, int((lengths >= 0.5 * lengths[order[0]]).sum()))
        if take < 1:
            raise CleanupError("the mesh cannot reach the minimum triangle count inside its target")
        marked = np.zeros(len(unique), dtype=bool)
        marked[order[:take]] = True
        vertices, faces = _split(vertices, faces, unique, edge_of, marked)
    return vertices, faces


# Splitting may pass through this many triangles on the way to a regular grid.
_OVERSPLIT_LIMIT = 400_000
_REGRID_SHARES = (0.8, 0.6, 0.45, 0.3, 0.2, 0.12)


def regrid(vertices: np.ndarray, faces: np.ndarray, target: int, limit: float, minimum: int):
    """Decimate and split so the mesh ends with ``minimum``..``target`` triangles and no long edge.

    Splitting alone is enough for most meshes. Slivers defeat it (bisecting a
    needle makes more needles), so when it overshoots the mesh is first split
    all the way down, whatever that costs, and then decimated: the collapse
    removes the short edges and leaves triangles of even size. Decimation can
    stretch a few edges again, so the two alternate with a falling goal.
    """
    if len(faces) > target:
        vertices, faces = decimate(vertices, faces, target)
    done = split_long_edges(vertices, faces, limit, target)
    if done is None:
        fine = split_long_edges(vertices, faces, limit, _OVERSPLIT_LIMIT)
        if fine is not None:
            for reduce in (decimate, decimate_by_clustering):
                for share in _REGRID_SHARES:
                    try:
                        v, f = reduce(fine[0], fine[1], int(target * share))
                    except CleanupError:
                        continue
                    done = split_long_edges(v, f, limit, target)
                    if done is not None:
                        break
                if done is not None:
                    break
    if done is None:
        raise CleanupError(
            f"no mesh of at most {target} triangles keeps every edge under {MAX_EDGE_METRES} m for this box"
        )
    return raise_triangle_count(done[0], done[1], minimum, target)


# --------------------------------------------------------------------------- shading


def _face_normals(vertices: np.ndarray, faces: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    cross = np.cross(vertices[faces[:, 1]] - vertices[faces[:, 0]], vertices[faces[:, 2]] - vertices[faces[:, 0]])
    length = np.linalg.norm(cross, axis=1)
    normals = np.where(length[:, None] > 1e-20, cross / np.maximum(length, 1e-20)[:, None], [0.0, 1.0, 0.0])
    return normals, 0.5 * length


def crease_normals(vertices: np.ndarray, faces: np.ndarray, crease_degrees: float = _CREASE_DEGREES):
    """Vertex normals that stay sharp across creases.

    A vertex is duplicated where the faces around it meet at more than
    ``crease_degrees``, so flat furniture panels shade flat. The triangle
    count does not change.
    """
    normals, areas = _face_normals(vertices, faces)
    cos_limit = np.cos(np.radians(crease_degrees))
    corner_vertex = faces.reshape(-1)
    order = np.argsort(corner_vertex, kind="stable")
    starts = np.flatnonzero(np.r_[True, np.diff(corner_vertex[order]) != 0, True])

    out_position, out_normal = [], []
    new_faces = np.empty(len(corner_vertex), dtype=np.int64)
    for s, e in zip(starts[:-1], starts[1:]):
        corners = order[s:e]
        face_ids = corners // 3
        n = normals[face_ids]
        smooth = (n @ n.T) >= cos_limit
        blended = (smooth * areas[face_ids][None, :]) @ n
        length = np.linalg.norm(blended, axis=1)
        blended = np.where(length[:, None] > 1e-12, blended / np.maximum(length, 1e-12)[:, None], n)
        seen: dict[tuple, int] = {}
        for corner, normal in zip(corners, np.round(blended, 3)):
            tag = (float(normal[0]), float(normal[1]), float(normal[2]))
            index = seen.get(tag)
            if index is None:
                index = seen[tag] = len(out_position)
                out_position.append(vertices[corner_vertex[corner]])
                out_normal.append(normal)
            new_faces[corner] = index
    out_normal = np.asarray(out_normal)
    out_normal /= np.maximum(np.linalg.norm(out_normal, axis=1), 1e-12)[:, None]
    return np.asarray(out_position), out_normal, new_faces.reshape(-1, 3)


def _sphere_directions(count: int) -> np.ndarray:
    i = np.arange(count) + 0.5
    polar = np.arccos(1.0 - 2.0 * i / count)
    azimuth = np.pi * (1.0 + 5.0**0.5) * i
    return np.stack([np.sin(polar) * np.cos(azimuth), np.cos(polar), np.sin(polar) * np.sin(azimuth)], axis=1)


def ambient_occlusion(positions: np.ndarray, normals: np.ndarray, faces: np.ndarray) -> np.ndarray:
    """Per-vertex ambient occlusion in [``_AO_FLOOR``, 1], 1 being fully open.

    The surface is rasterised into a coarse occupancy grid and a fixed fan of
    rays is marched through it from each vertex. That is fast, needs no ray
    tracing library, and gives the same answer on every machine.
    """
    lo, hi = positions.min(axis=0), positions.max(axis=0)
    voxel = float(np.clip((hi - lo).max() / 40.0, 0.01, 0.05))
    shape = np.ceil((hi - lo) / voxel).astype(np.int64) + 3
    origin = lo - voxel

    # Sample each triangle densely enough that no voxel it crosses is missed.
    steps = int(np.ceil(longest_edge(positions, faces) / voxel)) + 1
    u, v = np.meshgrid(np.arange(steps + 1), np.arange(steps + 1), indexing="ij")
    inside = (u + v) <= steps
    bu, bv = u[inside] / steps, v[inside] / steps
    a, b, c = positions[faces[:, 0]], positions[faces[:, 1]], positions[faces[:, 2]]
    grid = np.zeros(shape, dtype=bool)
    for start in range(0, len(faces), 2000):
        sl = slice(start, start + 2000)
        pts = (
            a[sl, None, :] * (1.0 - bu - bv)[None, :, None]
            + b[sl, None, :] * bu[None, :, None]
            + c[sl, None, :] * bv[None, :, None]
        ).reshape(-1, 3)
        cell = np.clip(((pts - origin) / voxel).astype(np.int64), 0, shape - 1)
        grid[cell[:, 0], cell[:, 1], cell[:, 2]] = True

    directions = _sphere_directions(_AO_DIRECTIONS)
    distances = voxel * (1.5 + np.arange(_AO_STEPS))
    occlusion = np.empty(len(positions))
    for start in range(0, len(positions), 2000):
        sl = slice(start, start + 2000)
        base = positions[sl] + normals[sl] * voxel
        cosine = normals[sl] @ directions.T
        facing = cosine > 0.1
        pts = base[:, None, None, :] + directions[None, :, None, :] * distances[None, None, :, None]
        cell = ((pts - origin) / voxel).astype(np.int64)
        valid = ((cell >= 0) & (cell < shape)).all(axis=-1)
        cell = np.clip(cell, 0, shape - 1)
        hit = (grid[cell[..., 0], cell[..., 1], cell[..., 2]] & valid).any(axis=-1)
        weight = np.where(facing, cosine, 0.0)
        total = weight.sum(axis=1)
        blocked = (weight * hit).sum(axis=1)
        occlusion[sl] = np.where(total > 0, blocked / np.maximum(total, 1e-12), 0.0)
    return _AO_FLOOR + (1.0 - _AO_FLOOR) * (1.0 - occlusion)


# --------------------------------------------------------------------------- the whole step


def clean(
    vertices: np.ndarray,
    faces: np.ndarray,
    dims: Sequence[float],
    tri_target: int,
    photo_rgb: Sequence[int] = (136, 134, 128),
    *,
    yaw: float = 0.0,
    max_anisotropy: float = 0.15,
    palette_entry: Optional[PaletteEntry] = None,
) -> CleanResult:
    """Run the whole clean-up and return the GLB with its report.

    ``dims`` is the scanned box ``[w, h, d]`` in metres, ``tri_target`` the
    triangle target sent with the job and ``yaw`` the box's rotation in the room
    (radians), which fixes where the room's light falls on it. Raises CleanupError when the mesh cannot
    meet the contract; the job then fails and the box takes its built-in mesh.
    """
    target = min(int(tri_target), VALIDATOR_MAX_TRIANGLES)
    if target < VALIDATOR_MIN_TRIANGLES:
        raise CleanupError(f"triangle target {tri_target} is below the contract minimum of {VALIDATOR_MIN_TRIANGLES}")
    report = CleanupReport(tri_target=target, box=tuple(float(d) for d in dims))

    v, f = weld(vertices, faces)
    report.source_triangles = int(len(f))
    if len(f) > target:
        v, f = decimate(v, f, target)
        report.notes.append(f"decimated from {report.source_triangles} triangles")
    before = len(f)
    v, f = drop_floaters(v, f)
    if len(f) != before:
        report.notes.append(f"removed {before - len(f)} triangles of detached fragments")

    v, report.up_axis, report.yaw_degrees, _ = orient_and_scale(v, dims, max_anisotropy)
    v, f = regrid(v, f, target, MAX_EDGE_METRES - _EDGE_MARGIN, VALIDATOR_MIN_TRIANGLES)

    positions, normals, f = crease_normals(v, f)
    occlusion = ambient_occlusion(positions, normals, f)
    entry = palette_entry or nearest_entry(photo_rgb)
    uv, colour = encode_vertex_attributes(entry, face_shade(normals, yaw) * occlusion)

    # Report what the file holds: positions as stored (float32), not as computed.
    stored = positions.astype(np.float32)
    lo, hi = stored.min(axis=0).astype(np.float64), stored.max(axis=0).astype(np.float64)
    stored[:, 0] -= np.float32((lo[0] + hi[0]) * 0.5)
    stored[:, 1] -= np.float32(lo[1])
    stored[:, 2] -= np.float32((lo[2] + hi[2]) * 0.5)
    glb = write_glb(stored, normals, uv, colour, f, generator=f"ATLAS Forge {PIPELINE_VER}")

    size = stored.max(axis=0).astype(np.float64) - stored.min(axis=0).astype(np.float64)
    box = np.asarray(dims, dtype=np.float64)
    report.triangles = int(len(f))
    report.vertices = int(len(stored))
    report.longest_edge = longest_edge(stored, f)
    report.size = tuple(float(s) for s in size)
    report.scale_error = tuple(float(e) for e in (size - box) / box)
    report.palette_index, report.palette_name, report.palette_rgb = entry.index, entry.name, entry.rgb
    report.photo_rgb = tuple(int(c) for c in photo_rgb)
    report.bytes = len(glb)
    report.sha256 = hashlib.sha256(glb).hexdigest()
    return CleanResult(glb=glb, report=report)
