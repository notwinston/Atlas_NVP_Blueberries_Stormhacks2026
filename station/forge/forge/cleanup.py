"""Mesh clean-up (task P1-D09; Master Plan section 7.4, state `cleaning`; mesh design section 5.3).

Takes whatever a generator returns and produces GLBs that meet the asset
contract: welded, oriented and scaled to the scanned box, origin at the centre
of the box's base, metres, +Y up, decimated to the job's triangle target, no
edge longer than 0.25 m, a palette colour per face, the shade (face shade times
ambient occlusion) baked into the vertex colours, exactly one mesh and no
material. In this order:

    1  weld, decimate to the full target, drop detached fragments      `orient`
    2  orient: by the fit to the scanned profile when the job has one
    3  scale to the box, at most +-15 % of stretch per axis
    4  regrid: no edge over 0.25 m, the count at or under the target    `finish`
    5  colour per face from the photos
    6  bake face shade times ambient occlusion
    7  the light copy from the full one: decimate to the light target, then 4 to 6
    8  validate both copies at their levels

Every step is deterministic, so the same input gives the same bytes and the
same content hash.

Frames. Vertices are in the GLB frame throughout: right-handed, +Y up, x the
negative of Unity's box frame, z the same. One quarter turn of clean-up
(`_YAW90`: x' = z, z' = -x) is a right-handed turn of +90 degrees about +Y in
that frame, which is a yaw of -90 degrees in Unity's sense.
"""
from __future__ import annotations

import hashlib
from dataclasses import dataclass, field
from typing import Optional, Sequence

import numpy as np

from . import PIPELINE_VER
from . import profile as scan_profile
from .budget import (
    MAX_EDGE_METRES,
    VALIDATOR_MAX_TRIANGLES,
    VALIDATOR_MAX_TRIANGLES_LIGHT,
    VALIDATOR_MIN_TRIANGLES,
)
from .colour import ColourView, dominant_cell, face_cells, split_by_cell
from .glb import write_glb
from .palette import PaletteEntry, encode_vertex_cells, entry as palette_entry_of, face_shade, nearest_entry
from .validator import longest_edge, validate_glb

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
    level: str = "full"  # which copy this report describes: "full" or "light"
    # The fit to the scanned profile (None when the job had none), and every orientation that was scored.
    fit: Optional[float] = None
    candidates: list[dict] = field(default_factory=list)
    # The palette cells the mesh uses with the share of the surface each covers, the largest first;
    # and how many photos the colours were read from (0: one cell from the dominant colour).
    cells: list[tuple[int, float]] = field(default_factory=list)
    colour_views: int = 0
    # What the validator found; empty when the copy meets the asset contract at its level.
    violations: list[str] = field(default_factory=list)
    # The light copy, in the report of the full one: its hash, bytes and triangles, or why there is none.
    light: Optional[dict] = None

    @property
    def max_scale_error(self) -> float:
        return max(abs(e) for e in self.scale_error)

    @property
    def fit_yaw(self) -> int:
        """The turn clean-up gave the mesh about +Y, as a yaw in Unity's box frame: 0, 90, 180 or 270 degrees.

        Positive turns +Z towards +X, as a RoomModel yaw does: the mesh's own +Z side stands at this
        azimuth of the box. It is the opposite of ``yaw_degrees``, which counts the same turn right-handed
        in the GLB frame.
        """
        return (360 - self.yaw_degrees) % 360

    def to_dict(self) -> dict:
        out = self._base_dict()
        out["level"] = self.level
        out["fit"] = None if self.fit is None else round(self.fit, 4)
        out["fitYaw"] = self.fit_yaw
        out["fitUp"] = self.up_axis
        out["candidates"] = list(self.candidates)
        out["colour"]["cells"] = [{"index": int(c), "share": round(float(a), 4)} for c, a in self.cells]
        out["colour"]["views"] = self.colour_views
        out["violations"] = list(self.violations)
        if self.light is not None:
            out["light"] = dict(self.light)
        return out

    def _base_dict(self) -> dict:
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
    """The two copies of a mesh. ``light_glb`` is None when no light copy was asked for or none could be made."""

    glb: bytes
    report: CleanupReport
    light_glb: Optional[bytes] = None
    light_report: Optional[CleanupReport] = None


@dataclass
class Oriented:
    """A mesh after the first three steps: welded, reduced to the full target, turned and scaled to its box."""

    vertices: np.ndarray  # GLB frame, metres, origin at the centre of the base
    faces: np.ndarray
    dims: tuple[float, float, float]
    tri_target: int
    up_axis: str = "Y"
    turns: int = 0  # quarter turns of `_YAW90` that were applied after the up axis
    fit: Optional[float] = None
    source_triangles: int = 0
    candidates: list[dict] = field(default_factory=list)
    notes: list[str] = field(default_factory=list)


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
_TURNS = tuple(np.linalg.matrix_power(_YAW90, k).astype(np.float64) for k in range(4))
# Two fits nearer than this are a tie, which the generator's known front may break (mesh design section 5.3).
FIT_TIE = 0.02


@dataclass
class _Candidate:
    """The mesh in one orientation, scaled to the box."""

    up_axis: str
    turns: int  # applications of `_YAW90` after the up axis
    vertices: np.ndarray
    bounds_score: float  # how unevenly the three axes must be scaled to fill the box; 0 is a perfect match
    stretch: float  # the largest share by which one axis must be stretched beyond the uniform scale
    error: tuple[float, float, float]
    fit: Optional[float] = None


def _box(dims: Sequence[float]) -> np.ndarray:
    box = np.asarray(dims, dtype=np.float64)
    if box.shape != (3,) or not (box > 0).all():
        raise CleanupError(f"invalid box size {list(dims)}")
    return box


def _candidate(vertices: np.ndarray, box: np.ndarray, up_name: str, up: np.ndarray, turns: int,
               max_anisotropy: float) -> _Candidate:
    """Turn the mesh, scale it uniformly with at most ``max_anisotropy`` of stretch per axis, and put
    its origin at the centre of the base."""
    rotated = vertices @ (_TURNS[turns] @ up).T
    extent = np.maximum(rotated.max(axis=0) - rotated.min(axis=0), 1e-9)
    log_ratio = np.log(box / extent)
    uniform = np.exp(log_ratio.mean())
    wanted = np.exp(log_ratio) / uniform
    scaled = rotated * (uniform * np.clip(wanted, 1.0 - max_anisotropy, 1.0 + max_anisotropy))
    lo, hi = scaled.min(axis=0), scaled.max(axis=0)
    scaled = scaled - np.array([(lo[0] + hi[0]) * 0.5, lo[1], (lo[2] + hi[2]) * 0.5])
    error = (hi - lo - box) / box
    return _Candidate(
        up_axis=up_name,
        turns=turns,
        vertices=scaled,
        bounds_score=float(((log_ratio - log_ratio.mean()) ** 2).sum()),
        stretch=float(np.abs(wanted - 1.0).max()),
        error=(float(error[0]), float(error[1]), float(error[2])),
    )


def _up_axes(up_axes: Optional[Sequence[str]]):
    chosen = [(name, up) for name, up in _UP if up_axes is None or name in up_axes]
    if not chosen:
        raise CleanupError(f"no up axis among {list(up_axes or [])}; the choices are Y, Z and X")
    return chosen


def _by_bounds(vertices: np.ndarray, box: np.ndarray, max_anisotropy: float, up_axes) -> _Candidate:
    """The orientation whose bounds best match the box.

    Bounds cannot tell 0 from 180 degrees, so only no turn and one quarter turn
    are distinguished, and the generator's own frame wins unless another one
    fits clearly better.
    """
    best = None
    for up_name, up in _up_axes(up_axes):
        for turns in (0, 1):
            candidate = _candidate(vertices, box, up_name, up, turns, max_anisotropy)
            if best is None or candidate.bounds_score < best.bounds_score * 0.8 - 1e-9:
                best = candidate
    return best


def orient_and_scale(
    vertices: np.ndarray, dims: Sequence[float], max_anisotropy: float = 0.15,
    up_axes: Optional[Sequence[str]] = None,
) -> tuple[np.ndarray, str, int, tuple[float, float, float]]:
    """Fit the mesh to the scanned box by its bounds alone: the rule for a job with no profile.

    Picks the up axis (among ``up_axes``; all three when None) and the
    quarter-turn yaw whose bounds best match the box, scales uniformly with at
    most ``max_anisotropy`` of stretch per axis, and moves the origin to the
    centre of the base.

    Returns ``(vertices, up_axis, yaw_degrees, scale_error_per_axis)``.
    """
    best = _by_bounds(vertices, _box(dims), max_anisotropy, up_axes)
    return best.vertices, best.up_axis, best.turns * 90, best.error


def orient_by_fit(
    vertices: np.ndarray,
    faces: np.ndarray,
    dims: Sequence[float],
    *,
    scan: Optional[np.ndarray] = None,
    prefer_turns: Optional[int] = None,
    up_axes: Optional[Sequence[str]] = None,
    max_anisotropy: float = 0.15,
) -> tuple[_Candidate, list[_Candidate]]:
    """Choose the orientation of the mesh in its box (mesh design section 5.3, "The fit").

    ``scan`` is the object profile the phone measured (256 bytes, Unity's box
    frame), or None. ``prefer_turns`` is what is known of the generator's own
    front, as a number of quarter turns of `_YAW90`: 0 when the front of its
    meshes is on their own +Z, the turn that puts the mesh's +Z on the camera's
    side when its meshes face the camera, None when nothing is known.

    The candidates are every up axis of ``up_axes`` times the four quarter
    turns, keeping those whose bounds fill the box within the stretch limit.
    With a profile, each is scored by `profile.fit_score` against the mesh's
    own profile and the lowest score wins. Scores within ``FIT_TIE`` of the
    best are a tie: one with the preferred turn wins it, then the generator's
    own up axis (the first of ``up_axes``), then the lower score. A scan seen
    from above cannot tell a cube from the same cube on its side, so another
    up axis must fit clearly better to be taken. Without a profile the
    preferred turn decides among the candidates the bounds allow, and with
    neither it is the bounds alone (`orient_and_scale`).

    Returns the chosen candidate and every candidate that was scored.
    """
    box = _box(dims)
    if scan is not None and not np.any(np.asarray(scan)):
        scan = None  # the scan saw nothing of this object
    by_bounds = _by_bounds(vertices, box, max_anisotropy, up_axes)
    if scan is None and prefer_turns is None:
        return by_bounds, []

    every = [_candidate(vertices, box, up_name, up, turns, max_anisotropy)
             for up_name, up in _up_axes(up_axes) for turns in range(4)]
    allowed = [c for c in every if c.stretch <= max_anisotropy + 1e-9]
    if not allowed:
        # No orientation fills the box within the limit. The bounds choose as before; what is
        # left to choose is between that orientation and the same turned by a half.
        allowed = [c for c in every if c.up_axis == by_bounds.up_axis and c.turns % 2 == by_bounds.turns % 2]

    if scan is None:
        preferred = [c for c in allowed if c.turns == prefer_turns % 4]
        return (min(preferred, key=lambda c: c.bounds_score) if preferred else by_bounds), []

    for candidate in allowed:
        candidate.fit = scan_profile.fit_score(scan, scan_profile.of_mesh(candidate.vertices, faces, box))
    ranked = sorted(allowed, key=lambda c: c.fit)  # stable: equal fits keep the order Y, Z, X and 0 to 3 turns
    order = [name for name, _ in _up_axes(up_axes)]
    tied = [c for c in ranked if c.fit - ranked[0].fit < FIT_TIE]
    chosen = min(tied, key=lambda c: (prefer_turns is not None and c.turns != prefer_turns % 4,
                                      order.index(c.up_axis), c.fit))
    return chosen, ranked


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


def orient(
    vertices: np.ndarray,
    faces: np.ndarray,
    dims: Sequence[float],
    tri_target: int,
    *,
    scan: Optional[np.ndarray] = None,
    prefer_turns: Optional[int] = None,
    up_axes: Optional[Sequence[str]] = None,
    max_anisotropy: float = 0.15,
) -> Oriented:
    """Steps 1 to 3: weld, decimate to the full target, drop fragments, orient and scale to the box.

    The fit of a generated mesh to the scan is known after this, so the service
    measures each attempt with it and finishes only the one it publishes.
    See `orient_by_fit` for ``scan``, ``prefer_turns`` and ``up_axes``.
    """
    target = min(int(tri_target), VALIDATOR_MAX_TRIANGLES)
    if target < VALIDATOR_MIN_TRIANGLES:
        raise CleanupError(f"triangle target {tri_target} is below the contract minimum of {VALIDATOR_MIN_TRIANGLES}")
    box = _box(dims)
    notes: list[str] = []

    v, f = weld(vertices, faces)
    source = int(len(f))
    if len(f) > target:
        v, f = decimate(v, f, target)
        notes.append(f"decimated from {source} triangles")
    before = len(f)
    v, f = drop_floaters(v, f)
    if len(f) != before:
        notes.append(f"removed {before - len(f)} triangles of detached fragments")

    chosen, scored = orient_by_fit(v, f, box, scan=scan, prefer_turns=prefer_turns, up_axes=up_axes,
                                   max_anisotropy=max_anisotropy)
    return Oriented(
        vertices=chosen.vertices,
        faces=f,
        dims=(float(box[0]), float(box[1]), float(box[2])),
        tri_target=target,
        up_axis=chosen.up_axis,
        turns=chosen.turns,
        fit=chosen.fit,
        source_triangles=source,
        candidates=[{"up": c.up_axis, "yawDegrees": c.turns * 90, "fit": round(c.fit, 4),
                     "stretch": round(c.stretch, 4)} for c in scored],
        notes=notes,
    )


def _bake(v: np.ndarray, f: np.ndarray, yaw: float, views: Sequence[ColourView], fallback: PaletteEntry,
          report: CleanupReport) -> bytes:
    """Steps 5 and 6 and the export: a palette cell per face, the shade per vertex, the GLB. Fills ``report``."""
    cells = face_cells(v, f, views, fallback) if views else np.full(len(f), fallback.index, dtype=np.int64)
    # Vertices are split where the surface creases, and again where two colours meet: every
    # vertex of a face then points at that face's cell. Neither changes the triangles or their order.
    positions, normals, f = crease_normals(v, f)
    positions, normals, f, vertex_cells = split_by_cell(positions, normals, f, cells)
    occlusion = ambient_occlusion(positions, normals, f)
    uv, colour = encode_vertex_cells(vertex_cells, face_shade(normals, yaw) * occlusion)

    # Report what the file holds: positions as stored (float32), not as computed.
    stored = positions.astype(np.float32)
    lo, hi = stored.min(axis=0).astype(np.float64), stored.max(axis=0).astype(np.float64)
    stored[:, 0] -= np.float32((lo[0] + hi[0]) * 0.5)
    stored[:, 1] -= np.float32(lo[1])
    stored[:, 2] -= np.float32((lo[2] + hi[2]) * 0.5)
    glb = write_glb(stored, normals, uv, colour, f, generator=f"ATLAS Forge {PIPELINE_VER}")

    size = stored.max(axis=0).astype(np.float64) - stored.min(axis=0).astype(np.float64)
    box = np.asarray(report.box, dtype=np.float64)
    areas = 0.5 * np.linalg.norm(
        np.cross(positions[f[:, 1]] - positions[f[:, 0]], positions[f[:, 2]] - positions[f[:, 0]]), axis=1
    )
    surface = np.bincount(cells, weights=areas)
    used = np.flatnonzero(surface > 0)
    used = used[np.lexsort((used, -surface[used]))]  # the largest first, the lower cell on a tie
    main = palette_entry_of(dominant_cell(cells, areas))
    report.triangles = int(len(f))
    report.vertices = int(len(stored))
    report.longest_edge = longest_edge(stored, f)
    report.size = tuple(float(s) for s in size)
    report.scale_error = tuple(float(e) for e in (size - box) / box)
    report.palette_index, report.palette_name, report.palette_rgb = main.index, main.name, main.rgb
    report.cells = [(int(c), float(surface[c] / surface.sum())) for c in used]
    report.colour_views = len(views)
    report.bytes = len(glb)
    report.sha256 = hashlib.sha256(glb).hexdigest()
    verdict = validate_glb(glb, tri_target=report.tri_target, box=report.box, level=report.level)
    report.violations = [f"[{fault.code}] {fault.message}" for fault in verdict.violations]
    return glb


def _lighter(v: np.ndarray, f: np.ndarray, target: int, limit: float) -> tuple[np.ndarray, np.ndarray]:
    """The full copy's surface reduced to the light target: decimate, then regrid (step 7).

    Collapsing edges can pull the outline in by a little; the result is put
    back on the full copy's bounds, so the two copies fill the same box.
    """
    if len(f) <= target:
        return v, f
    lo, hi = v.min(axis=0), v.max(axis=0)
    lv, lf = decimate(v, f, target)
    low, high = lv.min(axis=0), lv.max(axis=0)
    span = np.where(high - low > 1e-9, high - low, 1.0)
    lv = lo + (lv - low) * ((hi - lo) / span)
    return regrid(lv, lf, target, limit, VALIDATOR_MIN_TRIANGLES)


def finish(
    oriented: Oriented,
    photo_rgb: Sequence[int] = (136, 134, 128),
    *,
    yaw: float = 0.0,
    palette_entry: Optional[PaletteEntry] = None,
    views: Sequence[ColourView] = (),
    tri_target_light: Optional[int] = None,
) -> CleanResult:
    """Steps 4 to 8 on an oriented mesh: regrid, colour, bake, the light copy, and the validator's verdicts.

    ``photo_rgb`` is the dominant colour of the photo: the cell the object
    takes where no view sees it, and everywhere when ``views`` is empty.
    ``yaw`` is the box's rotation in the room (radians), which fixes where the
    room's light falls on it. ``tri_target_light`` asks for the light copy.

    What the validator finds is in each report's ``violations``; the caller
    decides what a violation of the full copy means. A light copy that cannot
    be made, or that does not meet its level, is left out, and the full copy's
    report says why. Raises CleanupError when the full copy cannot be made.
    """
    dims, target = oriented.dims, oriented.tri_target
    limit = MAX_EDGE_METRES - _EDGE_MARGIN
    fallback = palette_entry or nearest_entry(photo_rgb)

    def new_report(level: str, tri_target: int) -> CleanupReport:
        return CleanupReport(
            tri_target=tri_target, box=dims, source_triangles=oriented.source_triangles, up_axis=oriented.up_axis,
            yaw_degrees=oriented.turns * 90, fit=oriented.fit, candidates=list(oriented.candidates),
            photo_rgb=tuple(int(c) for c in photo_rgb), notes=list(oriented.notes), level=level,
        )

    v, f = regrid(oriented.vertices, oriented.faces, target, limit, VALIDATOR_MIN_TRIANGLES)
    report = new_report("full", target)
    result = CleanResult(glb=_bake(v, f, yaw, views, fallback, report), report=report)
    if tri_target_light is None:
        return result

    light_target = min(int(tri_target_light), VALIDATOR_MAX_TRIANGLES_LIGHT)
    light_report = new_report("light", light_target)
    try:
        if light_target < VALIDATOR_MIN_TRIANGLES:
            raise CleanupError(f"light target {tri_target_light} is below the contract minimum of "
                               f"{VALIDATOR_MIN_TRIANGLES}")
        lv, lf = _lighter(v, f, light_target, limit)
        if len(lf) != len(f):
            light_report.notes.append(f"reduced from the full copy's {len(f)} triangles")
        light_glb = _bake(lv, lf, yaw, views, fallback, light_report)
        if light_report.violations:
            raise CleanupError("asset contract: " + "; ".join(light_report.violations))
    except CleanupError as exc:
        report.light = {"error": str(exc)}
        report.notes.append(f"no light copy: {exc}")
        return result
    report.light = {"sha256": light_report.sha256, "bytes": light_report.bytes, "triangles": light_report.triangles}
    result.light_glb, result.light_report = light_glb, light_report
    return result


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
    scan: Optional[np.ndarray] = None,
    prefer_turns: Optional[int] = None,
    up_axes: Optional[Sequence[str]] = None,
    views: Sequence[ColourView] = (),
    tri_target_light: Optional[int] = None,
) -> CleanResult:
    """Run the whole clean-up and return both copies with their reports.

    ``dims`` is the scanned box ``[w, h, d]`` in metres, ``tri_target`` the
    triangle target sent with the job and ``yaw`` the box's rotation in the room
    (radians). ``scan``, ``prefer_turns`` and ``up_axes`` steer the orientation
    (`orient_by_fit`); ``views`` are the photos the colours are read from; and
    ``tri_target_light`` asks for the light copy. With none of them given the
    mesh is oriented by its bounds, takes one colour and has no light copy.

    Raises CleanupError when the mesh cannot be brought inside the contract;
    the job then fails and the box keeps its first shape.
    """
    oriented = orient(vertices, faces, dims, tri_target, scan=scan, prefer_turns=prefer_turns, up_axes=up_axes,
                      max_anisotropy=max_anisotropy)
    return finish(oriented, photo_rgb, yaw=yaw, palette_entry=palette_entry, views=views,
                  tri_target_light=tri_target_light)
