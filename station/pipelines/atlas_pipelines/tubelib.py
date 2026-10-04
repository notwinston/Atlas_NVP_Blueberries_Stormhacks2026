"""Core library of the lesion tube and its shells (Master Plan 10.2.4 steps 3-4, R2 D.2 / D.3, A.8).

Generic over its input: **a list of 10 per-phase signed distance fields on one common isotropic
grid** (negative inside). The illustrative twin feeds analytic sphere fields; the real case feeds
fields built from rasterised contour masks (``sdf_from_mask``). Nothing here reads DICOM.

Conventions
    * Arrays are indexed ``[ix, iy, iz]``. Voxel (ix, iy, iz) is CENTRED at
      ``grid.origin_mm + grid.spacing_mm * (ix, iy, iz)`` in the patient frame (LPS millimetres).
    * All geometry inside this module is in patient millimetres. The display frame
      (``contract.to_display``: metres, M applied, origin subtracted) is applied only when a file
      is written.
    * A mesh is ``(V, F)``: ``V`` (n, 3) float64, ``F`` (m, 3) int, counter-clockwise from outside.
    * Everything is deterministic: the same fields give the same bytes.

API (in the order a step calls it)

    grid  = Grid(origin_mm=(x, y, z), spacing_mm=1.0, shape=sdfs[0].shape)
    sdfs  = [sdf_from_mask(mask_k, mask_origin_mm, mask_spacing_mm, grid) ...]     # real case only

    tube  = build_tube(sdfs, grid, centroids_mm=None, n_tris=600, newton_iters=5, interp="cubic") -> TubeResult
            # .V0 .F  template (mean shape at the mean centroid), exactly n_tris triangles, closed
            # .V      (10, nV, 3) template projected onto each phase
            # .V_rigid(10, nV, 3) phase-0 shape translated along the centroid path
            # .centroids_mm (10, 3), .origin_mm = their mean (the display origin of the case)
    write_tube_files(tube, case_dir)        -> tube4d.bin, tube4d_rigid.bin, phases.glb
    contract.write_frame(paths.frame_path(case, out), tube.frame(case))

    sh    = build_shells(sdfs, grid, centroids_mm=tube.centroids_mm, substeps=4)    -> ShellsResult
            # volumes, 10 x 10 gating table, occupancy, shell meshes (<= 1,500 triangles each)
    write_shell_files(sh, case_dir, case_id, origin_mm=tube.origin_mm, extra={...})
            -> itv.glb, core.glb, rho50.glb, occ.r8, table.json

    The two halves may use different grids (the twin builds the tube on 1 mm and measures the
    shells on 0.25 mm); pass the tube's centroids and origin to the shells so both agree.

Helpers (also used by the tests)
    sample_trilinear(field, grid, pts)             values and gradients, trilinear
    FieldSampler(field, grid, interp)              values and gradients, cubic B-spline (default) or trilinear
    marching_mesh(field, grid, level=0.0)          closed iso-surface in mm, outward orientation
    decimate_template(V, F, n_tris)                quadric decimation to EXACTLY n_tris, stays a closed manifold
    decimate_exact(V, F, n_tris)                   shortest-edge collapse to EXACTLY n_tris (the finisher and fallback)
    is_closed_manifold(F), mesh_volume(V, F), mesh_area(V, F), vertex_normals(V, F)
    slice_tets(P, T, n, w0)                        cut of a tet mesh by the hyperplane n.x = w0 (A.8)
    slice_tau(tube4d, tau)                         the tau = const slice, triangles (K, 3, 3) in display metres
    weld(tris)                                     triangle soup -> indexed mesh
    point_mesh_distance(pts, V, F)                 exact point-to-triangle-mesh distance
    hausdorff(Va, Fa, Vb, Fb)                      symmetric mesh-to-mesh Hausdorff distance
    face_sharing(T)                                the crack check: how many tets share each face
    voxel_volume_cm3(inside, grid)                 voxel count times voxel volume

Method notes (each is a decision of this module; they are also recorded in docs/case_contract.md)
    * Mean shape: the phase fields are first shifted so their centroids coincide at the mean
      centroid, then averaged. Averaging unshifted fields of a small moving lesion gives a
      smeared or empty shape (for the twin: 15 mm of travel against a 5 mm radius).
    * Newton projection samples each phase field with a cubic B-spline interpolant of the grid
      values, not a trilinear one (``interp="linear"`` selects trilinear). See FieldSampler for the
      reason: trilinear sampling of a distance field pulls a convex surface inward.
    * Sub-steps: sub-step j of S between phases k and k + 1 (a = j / (S + 1)) is the blend
      ``(1 - a) * sdf_k(x - a d) + a * sdf_k+1(x + (1 - a) d)`` with ``d = c_k+1 - c_k``: each
      phase shape is carried along the straight segment between the two centroids and the two
      shapes are blended. A plain blend without the shift cannot add anything to a union (a
      linear blend of two values is never below the smaller one), so it would leave the
      scalloped shell the sub-steps exist to remove. For a rigid shape the blend is exact.
    * Volumes are voxel counts of ``field < 0`` on the grid given to ``build_shells``.
    * ITV = min over the 10 phases and all sub-steps (wrapping 9 -> 0); core = max over the 10
      phases; rho = share of the 10 phases in which a voxel is inside.
    * ``rho50`` is the boundary of {rho >= 0.5}, extracted as the zero level of the 5th-smallest
      of the 10 phase fields (a point is inside at least 5 phases exactly when that value is
      negative), which gives a smooth surface instead of the staircase of a 10-level field.
    * An empty shell (the twin's core) is written as a valid GLB with no meshes, and its volume is 0.0.
"""

from __future__ import annotations

import heapq
import math
from dataclasses import dataclass, field
from pathlib import Path
from typing import Sequence

import numpy as np
from scipy import ndimage
from scipy.spatial import cKDTree

from . import contract
from .glb import GlbMesh, write_glb

VERSION = "tubelib/2"  #: bump when this module changes what it writes (steps put it in their cache key)

N_TEMPLATE_TRIS = contract.TEMPLATE_TRIS  # 600
SHELL_MAX_TRIS = 1500
NEWTON_ITERS = 5
SUBSTEPS = 4
OCC_MARGIN_MM = 2.0


class TubeError(ValueError):
    """The input fields cannot be turned into a tube or shells."""


# ============================================================================================
# Grid and sampling
# ============================================================================================
@dataclass(frozen=True)
class Grid:
    """An isotropic grid. Voxel (ix, iy, iz) is centred at origin_mm + spacing_mm * (ix, iy, iz)."""

    origin_mm: tuple[float, float, float]
    spacing_mm: float
    shape: tuple[int, int, int]

    def __post_init__(self) -> None:
        object.__setattr__(self, "origin_mm", tuple(float(v) for v in self.origin_mm))
        object.__setattr__(self, "spacing_mm", float(self.spacing_mm))
        object.__setattr__(self, "shape", tuple(int(v) for v in self.shape))
        if len(self.origin_mm) != 3 or len(self.shape) != 3 or not self.spacing_mm > 0:
            raise TubeError("Grid needs a 3-vector origin, a positive spacing and a 3D shape")

    @staticmethod
    def covering(lo_mm: Sequence[float], hi_mm: Sequence[float], spacing_mm: float, margin_mm: float = 0.0) -> "Grid":
        """The smallest grid whose voxel centres cover [lo - margin, hi + margin]; voxel centres sit
        on whole multiples of the spacing, so grids of the same spacing are aligned with each other."""
        lo = np.floor((np.asarray(lo_mm, float) - margin_mm) / spacing_mm).astype(int)
        hi = np.ceil((np.asarray(hi_mm, float) + margin_mm) / spacing_mm).astype(int)
        return Grid(origin_mm=tuple(lo * spacing_mm), spacing_mm=spacing_mm, shape=tuple(hi - lo + 1))

    @property
    def voxel_mm3(self) -> float:
        return self.spacing_mm ** 3

    def axes(self) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
        return tuple(self.origin_mm[a] + self.spacing_mm * np.arange(self.shape[a]) for a in range(3))

    def points(self) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
        """Open mesh grids X, Y, Z of voxel centres (broadcastable to the grid shape)."""
        x, y, z = self.axes()
        return x[:, None, None], y[None, :, None], z[None, None, :]

    def to_mm(self, index_coords: np.ndarray) -> np.ndarray:
        return np.asarray(index_coords, float) * self.spacing_mm + np.asarray(self.origin_mm)

    def to_index(self, pts_mm: np.ndarray) -> np.ndarray:
        return (np.asarray(pts_mm, float) - np.asarray(self.origin_mm)) / self.spacing_mm


def _check_fields(sdfs: Sequence[np.ndarray], grid: Grid) -> list[np.ndarray]:
    if len(sdfs) != contract.PHASES:
        raise TubeError(f"need {contract.PHASES} phase fields, got {len(sdfs)}")
    out = []
    for k, s in enumerate(sdfs):
        s = np.asarray(s)
        if s.shape != grid.shape:
            raise TubeError(f"phase {k}: field shape {s.shape} != grid shape {grid.shape}")
        if not np.isfinite(s).all():
            raise TubeError(f"phase {k}: field holds NaN or infinity")
        out.append(s)
    return out


def sample_trilinear(fld: np.ndarray, grid: Grid, pts_mm: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Trilinear value and gradient (per mm) of ``fld`` at points in mm. Points outside the grid
    are clamped to its border."""
    fld = np.asarray(fld)
    u = grid.to_index(np.asarray(pts_mm, float).reshape(-1, 3))
    n = np.array(fld.shape)
    u = np.clip(u, 0.0, n - 1.0)
    i0 = np.minimum(np.floor(u).astype(np.int64), n - 2)
    t = u - i0
    x0, y0, z0 = i0[:, 0], i0[:, 1], i0[:, 2]
    tx, ty, tz = t[:, 0], t[:, 1], t[:, 2]
    c000 = fld[x0, y0, z0].astype(np.float64)
    c100 = fld[x0 + 1, y0, z0].astype(np.float64)
    c010 = fld[x0, y0 + 1, z0].astype(np.float64)
    c110 = fld[x0 + 1, y0 + 1, z0].astype(np.float64)
    c001 = fld[x0, y0, z0 + 1].astype(np.float64)
    c101 = fld[x0 + 1, y0, z0 + 1].astype(np.float64)
    c011 = fld[x0, y0 + 1, z0 + 1].astype(np.float64)
    c111 = fld[x0 + 1, y0 + 1, z0 + 1].astype(np.float64)
    c00 = c000 + (c100 - c000) * tx
    c10 = c010 + (c110 - c010) * tx
    c01 = c001 + (c101 - c001) * tx
    c11 = c011 + (c111 - c011) * tx
    c0 = c00 + (c10 - c00) * ty
    c1 = c01 + (c11 - c01) * ty
    val = c0 + (c1 - c0) * tz
    gx = ((c100 - c000) * (1 - ty) + (c110 - c010) * ty) * (1 - tz) + ((c101 - c001) * (1 - ty) + (c111 - c011) * ty) * tz
    gy = (c10 - c00) * (1 - tz) + (c11 - c01) * tz
    gz = c1 - c0
    grad = np.stack([gx, gy, gz], axis=1) / grid.spacing_mm
    return val, grad


def shifted(fld: np.ndarray, grid: Grid, shift_mm: Sequence[float]) -> np.ndarray:
    """The field of the same shape moved by ``shift_mm``: result(x) = fld(x - shift). Trilinear;
    values needed from outside the grid repeat the border value."""
    s = np.asarray(shift_mm, float) / grid.spacing_mm
    if not np.any(s):
        return np.asarray(fld, dtype=np.float32)
    return ndimage.shift(np.asarray(fld, dtype=np.float32), s, order=1, mode="nearest")


def voxel_volume_cm3(inside: np.ndarray, grid: Grid) -> float:
    """Voxel count times voxel volume, in cm3."""
    return float(np.count_nonzero(inside)) * grid.voxel_mm3 / 1000.0


def centroid_of_inside(sdf: np.ndarray, grid: Grid) -> np.ndarray:
    """Centroid (mm) of the voxels with sdf < 0."""
    idx = np.argwhere(np.asarray(sdf) < 0)
    if len(idx) == 0:
        raise TubeError("a phase field has no inside voxel")
    return grid.to_mm(idx.mean(axis=0))


def sdf_from_mask(mask: np.ndarray, mask_origin_mm: Sequence[float], mask_spacing_mm: Sequence[float],
                  grid: Grid, sigma_mm: float = 1.0) -> np.ndarray:
    """R2 D.2 for the real case: a rasterised mask (indexed [ix, iy, iz], voxel centres at
    mask_origin + mask_spacing * index, any anisotropic spacing) -> signed distance in mm
    (negative inside) -> resampled trilinearly onto the isotropic ``grid`` -> Gaussian of
    ``sigma_mm`` (shape-based interpolation between thick slices). Returns float32."""
    mask = np.asarray(mask).astype(bool)
    if not mask.any():
        raise TubeError("empty mask")
    sp = np.asarray(mask_spacing_mm, float)
    sd = ndimage.distance_transform_edt(~mask, sampling=sp) - ndimage.distance_transform_edt(mask, sampling=sp)
    x, y, z = grid.points()
    org = np.asarray(mask_origin_mm, float)
    coords = np.stack(np.broadcast_arrays((x - org[0]) / sp[0], (y - org[1]) / sp[1], (z - org[2]) / sp[2]))
    out = ndimage.map_coordinates(sd, coords, order=1, mode="nearest")
    if sigma_mm > 0:
        out = ndimage.gaussian_filter(out, sigma_mm / grid.spacing_mm, mode="nearest")
    return out.astype(np.float32)


# ============================================================================================
# Triangle meshes
# ============================================================================================
def mesh_volume(V: np.ndarray, F: np.ndarray) -> float:
    """Signed volume of a closed mesh (positive when the triangles are counter-clockwise from outside)."""
    V = np.asarray(V, float)
    F = np.asarray(F)
    if len(F) == 0:
        return 0.0
    a, b, c = V[F[:, 0]], V[F[:, 1]], V[F[:, 2]]
    return float(np.einsum("ij,ij->i", a, np.cross(b, c)).sum() / 6.0)


def mesh_area(V: np.ndarray, F: np.ndarray) -> float:
    V = np.asarray(V, float)
    F = np.asarray(F)
    if len(F) == 0:
        return 0.0
    return float(0.5 * np.linalg.norm(np.cross(V[F[:, 1]] - V[F[:, 0]], V[F[:, 2]] - V[F[:, 0]]), axis=1).sum())


def triangles_area(tris: np.ndarray) -> float:
    """Total area of a triangle soup (K, 3, 3)."""
    tris = np.asarray(tris, float)
    if len(tris) == 0:
        return 0.0
    return float(0.5 * np.linalg.norm(np.cross(tris[:, 1] - tris[:, 0], tris[:, 2] - tris[:, 0]), axis=1).sum())


def vertex_normals(V: np.ndarray, F: np.ndarray) -> np.ndarray:
    """Area-weighted unit vertex normals."""
    V = np.asarray(V, float)
    fn = np.cross(V[F[:, 1]] - V[F[:, 0]], V[F[:, 2]] - V[F[:, 0]])
    N = np.zeros_like(V)
    for j in range(3):
        np.add.at(N, F[:, j], fn)
    ln = np.linalg.norm(N, axis=1, keepdims=True)
    return N / np.where(ln > 0, ln, 1.0)


def is_closed_manifold(F: np.ndarray) -> bool:
    """True when the triangles form a closed, consistently oriented 2-manifold: every directed
    edge occurs once and its reverse occurs once, and the triangles round every vertex form one fan."""
    F = np.asarray(F, dtype=np.int64).reshape(-1, 3)
    if len(F) == 0:
        return False
    if (F[:, 0] == F[:, 1]).any() or (F[:, 1] == F[:, 2]).any() or (F[:, 0] == F[:, 2]).any():
        return False
    e = np.concatenate([F[:, [0, 1]], F[:, [1, 2]], F[:, [2, 0]]])
    big = int(F.max()) + 1
    key = e[:, 0] * big + e[:, 1]
    rev = e[:, 1] * big + e[:, 0]
    if len(np.unique(key)) != len(key):
        return False
    if not np.array_equal(np.sort(key), np.sort(rev)):
        return False
    # one fan per vertex: the number of incident triangles equals the number of neighbours, and
    # the link (the opposite edges) is one cycle
    nxt: dict[int, dict[int, int]] = {}
    for a, b, c in F.tolist():
        nxt.setdefault(a, {})[b] = c
        nxt.setdefault(b, {})[c] = a
        nxt.setdefault(c, {})[a] = b
    for v, link in nxt.items():
        start = next(iter(link))
        cur, steps = link[start], 1
        while cur != start:
            if cur not in link or steps > len(link):
                return False
            cur = link[cur]
            steps += 1
        if steps != len(link):
            return False
    return True


def _compact(V: np.ndarray, F: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    used = np.unique(F)
    remap = np.full(int(V.shape[0]), -1, dtype=np.int64)
    remap[used] = np.arange(len(used))
    return np.asarray(V, float)[used], remap[F]


def largest_component(V: np.ndarray, F: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Keep the connected component with the most triangles (ties: the one holding the lowest vertex)."""
    from scipy.sparse import coo_matrix
    from scipy.sparse.csgraph import connected_components

    n = len(V)
    e = np.concatenate([F[:, [0, 1]], F[:, [1, 2]]])
    g = coo_matrix((np.ones(len(e), dtype=np.int8), (e[:, 0], e[:, 1])), shape=(n, n))
    ncomp, lab = connected_components(g, directed=False)
    if ncomp == 1:
        return _compact(V, F)
    counts = np.bincount(lab[F[:, 0]], minlength=ncomp)
    keep = int(np.argmax(counts))
    return _compact(V, F[lab[F[:, 0]] == keep])


def marching_mesh(fld: np.ndarray, grid: Grid, level: float = 0.0) -> tuple[np.ndarray, np.ndarray]:
    """Marching cubes of ``fld`` at ``level``: (V in mm, F), oriented counter-clockwise from the
    outside of the region fld < level. Returns two empty arrays when the surface does not exist."""
    from skimage import measure

    fld = np.asarray(fld, dtype=np.float32)
    if not (fld.min() < level < fld.max()):
        return np.zeros((0, 3)), np.zeros((0, 3), dtype=np.int64)
    v, f, _, _ = measure.marching_cubes(fld, level=level)
    V = grid.to_mm(v)
    F = f.astype(np.int64)
    # drop triangles that marching cubes collapsed to a point or a line (repeated indices)
    ok = (F[:, 0] != F[:, 1]) & (F[:, 1] != F[:, 2]) & (F[:, 0] != F[:, 2])
    F = F[ok]
    if len(F) == 0:
        return np.zeros((0, 3)), np.zeros((0, 3), dtype=np.int64)
    V, F = _compact(V, F)
    if mesh_volume(V, F) < 0:
        F = F[:, ::-1].copy()
    return V, F


def decimate_exact(V: np.ndarray, F: np.ndarray, n_tris: int) -> tuple[np.ndarray, np.ndarray]:
    """Shortest-edge-first edge collapse of a closed manifold down to EXACTLY ``n_tris`` triangles.

    A collapse merges the two ends of an edge at its midpoint and removes exactly two triangles.
    It is refused when it would break the manifold (the two ends must have exactly two common
    neighbours) or fold a triangle over. The result is a closed manifold with the input's
    orientation. Deterministic (ties are broken by vertex index).
    """
    V = np.asarray(V, float)
    F = np.asarray(F, dtype=np.int64)
    if not is_closed_manifold(F):
        raise TubeError("decimate_exact: the input is not a closed manifold")
    if (len(F) - n_tris) % 2 or n_tris < 4 or len(F) < n_tris:
        raise TubeError(f"decimate_exact: cannot reach {n_tris} triangles from {len(F)}")
    pos = {i: V[i].copy() for i in range(len(V))}
    faces: dict[int, tuple[int, int, int]] = {i: tuple(int(x) for x in f) for i, f in enumerate(F)}
    vfaces: dict[int, set[int]] = {i: set() for i in range(len(V))}
    for fi, f in faces.items():
        for v in f:
            vfaces[v].add(fi)
    ver = {i: 0 for i in pos}

    def neigh(v: int) -> set[int]:
        s: set[int] = set()
        for fi in vfaces[v]:
            s.update(faces[fi])
        s.discard(v)
        return s

    def d2(a: int, b: int) -> float:
        d = pos[a] - pos[b]
        return float(d @ d)

    heap: list[tuple[float, int, int, int, int]] = []
    for a in pos:
        for b in neigh(a):
            if a < b:
                heap.append((d2(a, b), a, b, 0, 0))
    heapq.heapify(heap)

    def fnormal(f: tuple[int, int, int], override: dict[int, np.ndarray]) -> np.ndarray:
        p = [override.get(v, pos[v]) for v in f]
        return np.cross(p[1] - p[0], p[2] - p[0])

    n_faces = len(faces)
    while n_faces > n_tris:
        if not heap:
            raise TubeError(f"decimate_exact: stuck at {n_faces} triangles (no legal collapse left)")
        _, a, b, va, vb = heapq.heappop(heap)
        if a not in pos or b not in pos or ver[a] != va or ver[b] != vb:
            continue
        shared = vfaces[a] & vfaces[b]
        if len(shared) != 2 or len(neigh(a) & neigh(b)) != 2:
            continue
        mid = 0.5 * (pos[a] + pos[b])
        ov = {a: mid, b: mid}
        ok = True
        for fi in (vfaces[a] | vfaces[b]) - shared:
            f = faces[fi]
            n0 = fnormal(f, {})
            n1 = fnormal(f, ov)
            l1 = float(np.linalg.norm(n1))
            if l1 <= 1e-12 or float(n0 @ n1) <= 0.2 * float(np.linalg.norm(n0)) * l1:
                # a zero-area input triangle (n0 = 0) may be reshaped freely, as long as it gains area
                if float(np.linalg.norm(n0)) > 1e-12 or l1 <= 1e-12:
                    ok = False
                    break
        if not ok:
            continue
        # collapse b into a
        for fi in shared:
            for v in faces[fi]:
                vfaces[v].discard(fi)
            del faces[fi]
        for fi in list(vfaces[b]):
            f = faces[fi]
            faces[fi] = tuple(a if v == b else v for v in f)
            vfaces[a].add(fi)
        del vfaces[b], pos[b], ver[b]
        pos[a] = mid
        n_faces -= 2
        touched = neigh(a)
        ver[a] += 1
        for v in sorted(touched):
            # the edges round a changed length; edges between its neighbours changed their legality
            lo, hi = (a, v) if a < v else (v, a)
            heapq.heappush(heap, (d2(lo, hi), lo, hi, ver[lo], ver[hi]))
        for v in sorted(touched):
            for w in sorted(neigh(v)):
                if v < w and w != a:
                    heapq.heappush(heap, (d2(v, w), v, w, ver[v], ver[w]))
    ids = sorted(pos)
    remap = {v: i for i, v in enumerate(ids)}
    Vn = np.array([pos[v] for v in ids])
    Fn = np.array([[remap[v] for v in faces[fi]] for fi in sorted(faces)], dtype=np.int64)
    if len(Fn) != n_tris or not is_closed_manifold(Fn):
        raise TubeError("decimate_exact: internal error, the result is not a closed manifold")
    return Vn, Fn


def decimate_template(V: np.ndarray, F: np.ndarray, n_tris: int) -> tuple[np.ndarray, np.ndarray]:
    """Decimate a closed manifold to EXACTLY ``n_tris`` triangles, keeping it a closed manifold.

    Quadric decimation (fast-simplification) does the bulk, because it keeps triangles where the
    surface bends and so stays closer to the shape than a uniform collapse; ``decimate_exact``
    removes whatever is left above the target. Quadric results that are not a closed manifold are
    discarded (the next larger target is tried), and if none qualifies the whole job is done by
    ``decimate_exact``."""
    import fast_simplification

    V = np.asarray(V, float)
    F = np.asarray(F, dtype=np.int64)
    for target in (n_tris, n_tris + 50, n_tris + 100, n_tris + 200, 2 * n_tris):
        if target >= len(F):
            break
        v, f = fast_simplification.simplify(V.astype(np.float32), F.astype(np.int32), target_count=target)
        v, f = np.asarray(v, float), np.asarray(f, dtype=np.int64)
        if len(f) >= n_tris and (len(f) - n_tris) % 2 == 0 and is_closed_manifold(f) and len(np.unique(f)) == len(v) \
                and mesh_volume(v, f) > 0:
            try:
                return decimate_exact(v, f, n_tris)
            except TubeError:
                continue
    return decimate_exact(V, F, n_tris)


def decimate_shell(V: np.ndarray, F: np.ndarray, max_tris: int = SHELL_MAX_TRIS) -> tuple[np.ndarray, np.ndarray]:
    """Quadric decimation (fast-simplification) of a shell to at most ``max_tris`` triangles."""
    if len(F) <= max_tris:
        return np.asarray(V, float), np.asarray(F, dtype=np.int64)
    import fast_simplification

    target = max_tris
    for _ in range(8):
        v, f = fast_simplification.simplify(np.asarray(V, np.float32), np.asarray(F, np.int32), target_count=target)
        f = np.asarray(f, dtype=np.int64)
        ok = (f[:, 0] != f[:, 1]) & (f[:, 1] != f[:, 2]) & (f[:, 0] != f[:, 2])
        f = f[ok]
        if len(f) <= max_tris:
            v, f = _compact(np.asarray(v, float), f)
            if mesh_volume(v, f) < 0:
                f = f[:, ::-1].copy()
            return v, f
        target = int(target * 0.9)
    raise TubeError(f"decimate_shell: could not reach {max_tris} triangles")


# ============================================================================================
# Template and tube (plan step 3)
# ============================================================================================
class FieldSampler:
    """Value and gradient of a grid field at points in mm.

    interp = "cubic" (default): cubic B-spline interpolation of the grid samples (exact at the
    voxel centres, 4th-order accurate between them); the gradient is a central difference of the
    interpolant over 0.1 voxel. interp = "linear": trilinear value and gradient (``sample_trilinear``).

    Why cubic is the default for the projection: a trilinear interpolant of a distance field lies
    above the field wherever the surface is convex, by about h^2 (k1 + k2) / 12 on average (h the
    voxel size, k1 and k2 the principal curvatures), so its zero surface sits inside the true one:
    0.033 mm for a 5 mm sphere on a 1 mm grid, which is 1.3 % of its area.
    """

    def __init__(self, fld: np.ndarray, grid: Grid, interp: str = "cubic"):
        if interp not in ("cubic", "linear"):
            raise TubeError(f"unknown interpolation {interp!r}")
        self.grid, self.interp = grid, interp
        self.fld = np.asarray(fld)
        if interp == "cubic":
            self.coeff = ndimage.spline_filter(self.fld.astype(np.float64), order=3, mode="nearest")

    def value(self, pts_mm: np.ndarray) -> np.ndarray:
        if self.interp == "linear":
            return sample_trilinear(self.fld, self.grid, pts_mm)[0]
        u = self.grid.to_index(np.asarray(pts_mm, float).reshape(-1, 3))
        return ndimage.map_coordinates(self.coeff, u.T, order=3, mode="nearest", prefilter=False)

    def value_and_gradient(self, pts_mm: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
        if self.interp == "linear":
            return sample_trilinear(self.fld, self.grid, pts_mm)
        p = np.asarray(pts_mm, float).reshape(-1, 3)
        h = 0.05 * self.grid.spacing_mm
        g = np.empty_like(p)
        for a in range(3):
            e = np.zeros(3)
            e[a] = h
            g[:, a] = (self.value(p + e) - self.value(p - e)) / (2.0 * h)
        return self.value(p), g


def newton_project(V: np.ndarray, sdf: np.ndarray, grid: Grid, iters: int = NEWTON_ITERS,
                   interp: str = "cubic") -> np.ndarray:
    """R2 D.2: repeat ``iters`` times  V -= sdf(V) * normalise(grad sdf(V))."""
    V = np.array(V, dtype=np.float64)
    smp = FieldSampler(sdf, grid, interp)
    for _ in range(iters):
        val, g = smp.value_and_gradient(V)
        ln = np.linalg.norm(g, axis=1, keepdims=True)
        V -= val[:, None] * g / np.where(ln > 1e-12, ln, 1.0)
    return V


def mean_shape_field(sdfs: Sequence[np.ndarray], grid: Grid, centroids_mm: np.ndarray) -> np.ndarray:
    """Mean of the phase fields after moving each one so that its centroid sits at the mean centroid."""
    c = np.asarray(centroids_mm, float)
    cm = c.mean(axis=0)
    acc = np.zeros(grid.shape, dtype=np.float64)
    for k, s in enumerate(sdfs):
        acc += shifted(s, grid, cm - c[k])
    return (acc / len(sdfs)).astype(np.float32)


def build_template(mean_field: np.ndarray, grid: Grid, n_tris: int = N_TEMPLATE_TRIS,
                   newton_iters: int = NEWTON_ITERS, interp: str = "cubic") -> tuple[np.ndarray, np.ndarray]:
    """Marching cubes of the mean field at 0 (largest component), decimated to exactly ``n_tris``
    triangles, then projected back onto the mean field. Closed manifold, outward orientation."""
    fld, g = np.asarray(mean_field, dtype=np.float32), grid
    for _ in range(4):
        V, F = marching_mesh(fld, g, 0.0)
        if len(F) == 0:
            raise TubeError("the mean shape is empty")
        V, F = largest_component(V, F)
        if len(F) >= 2 * n_tris:
            break
        # too few triangles to decimate from: extract the surface on a grid twice as fine
        shape2 = tuple(2 * n - 1 for n in g.shape)
        coords = np.stack(np.meshgrid(*[np.arange(n) / 2.0 for n in shape2], indexing="ij"))
        fld = ndimage.map_coordinates(fld, coords, order=1, mode="nearest").astype(np.float32)
        g = Grid(origin_mm=g.origin_mm, spacing_mm=g.spacing_mm / 2.0, shape=shape2)
    if not is_closed_manifold(F):
        raise TubeError("the mean-shape surface is not a closed manifold (does it touch the grid border?)")
    V, F = decimate_template(V, F, n_tris)
    V = newton_project(V, mean_field, grid, newton_iters, interp)
    if mesh_volume(V, F) <= 0:
        raise TubeError("template orientation is not outward")
    return V, F


@dataclass
class TubeResult:
    V0: np.ndarray  #: (nV, 3) template, mm: the mean shape at the mean centroid
    F: np.ndarray  #: (600, 3) template triangles
    V: np.ndarray  #: (10, nV, 3) mm: the template projected onto each phase
    V_rigid: np.ndarray  #: (10, nV, 3) mm: phase-0 shape translated along the centroid path
    centroids_mm: np.ndarray  #: (10, 3)
    origin_mm: np.ndarray  #: (3,) mean of the 10 centroids: the display origin of the case
    residual_mm: np.ndarray  #: (10,) max |sdf_k| at the projected vertices
    interp: str = "cubic"  #: how the fields were sampled in the projection

    def frame(self, case_id: str) -> contract.Frame:
        return contract.Frame(id=case_id, origin_mm=[float(v) for v in self.origin_mm])

    def tube4d(self, rigid: bool = False) -> contract.Tube4D:
        rings = self.V_rigid if rigid else self.V
        return contract.tube_from_phases([contract.to_display(v, self.origin_mm) for v in rings], self.F)


def build_tube(sdfs: Sequence[np.ndarray], grid: Grid, centroids_mm: np.ndarray | None = None,
               n_tris: int = N_TEMPLATE_TRIS, newton_iters: int = NEWTON_ITERS, interp: str = "cubic") -> TubeResult:
    """Plan step 3 from the fields onward. ``centroids_mm`` (10, 3) may be given (mask centroids
    of the real case, analytic centres of the twin); otherwise the centroid of the inside voxels
    is used. ``interp``: how the fields are sampled in the Newton projection (see FieldSampler)."""
    sdfs = _check_fields(sdfs, grid)
    if centroids_mm is None:
        c = np.array([centroid_of_inside(s, grid) for s in sdfs])
    else:
        c = np.asarray(centroids_mm, float).reshape(contract.PHASES, 3)
    cm = c.mean(axis=0)
    mean_field = mean_shape_field(sdfs, grid, c)
    V0, F = build_template(mean_field, grid, n_tris, newton_iters, interp)
    V = np.empty((len(sdfs),) + V0.shape)
    res = np.empty(len(sdfs))
    for k, s in enumerate(sdfs):
        V[k] = newton_project(V0 + (c[k] - cm), s, grid, newton_iters, interp)
        res[k] = float(np.abs(FieldSampler(s, grid, interp).value(V[k])).max())
    V_rigid = np.stack([V[0] + (c[k] - c[0]) for k in range(len(sdfs))])
    return TubeResult(V0=V0, F=F, V=V, V_rigid=V_rigid, centroids_mm=c, origin_mm=cm, residual_mm=res, interp=interp)


def write_tube_files(tube: TubeResult, case_dir: Path | str) -> list[Path]:
    """Write tube4d.bin (as contoured), tube4d_rigid.bin and phases.glb. Returns the paths."""
    case_dir = Path(case_dir)
    case_dir.mkdir(parents=True, exist_ok=True)
    nV = tube.V0.shape[0]
    out = []
    for name, rigid in (("tube4d.bin", False), ("tube4d_rigid.bin", True)):
        t = tube.tube4d(rigid)
        contract.check_tube_layout(t, nV)
        contract.write_tube4d(case_dir / name, t)
        out.append(case_dir / name)
    base = contract.to_display(tube.V0, tube.origin_mm)
    targets = {f"phase_{k}": contract.to_display(tube.V[k], tube.origin_mm) - base for k in range(tube.V.shape[0])}
    write_glb(case_dir / "phases.glb",
              [GlbMesh(positions=base, indices=tube.F, normals=vertex_normals(base, tube.F), targets=targets, name="phases")])
    out.append(case_dir / "phases.glb")
    return out


# ============================================================================================
# Shells, occupancy and table (plan step 4)
# ============================================================================================
@dataclass
class ShellsResult:
    grid: Grid
    substeps: int
    centroids_mm: np.ndarray  #: (10, 3)
    gtv_cm3: list[float]
    itv_cm3: float
    core_cm3: float
    gating_cm3: list[list[float]]
    max_rho: float
    argmax_rho_mm: list[float]
    itv_sdf: np.ndarray  #: min over phases and sub-steps
    core_sdf: np.ndarray  #: max over the 10 phases
    rho: np.ndarray  #: share of the 10 phases inside, float32 in [0, 1]
    rho50_sdf: np.ndarray  #: negative where rho >= 0.5
    meshes: dict[str, tuple[np.ndarray, np.ndarray]]  #: "itv", "core", "rho50" -> (V mm, F), F empty when absent
    occ: np.ndarray  #: (64, 64, 64) uint8 indexed [iz, iy, ix] (the byte order of occ.r8)
    occ_bbox_min_mm: list[float]
    occ_bbox_max_mm: list[float]
    info: dict = field(default_factory=dict)  #: goes to table.json "extra"


def substep_field(sdf_a: np.ndarray, sdf_b: np.ndarray, grid: Grid, c_a: np.ndarray, c_b: np.ndarray, a: float) -> np.ndarray:
    """The field at fraction ``a`` of the way from phase A to phase B (see the module notes)."""
    d = np.asarray(c_b, float) - np.asarray(c_a, float)
    return (1.0 - a) * shifted(sdf_a, grid, a * d) + a * shifted(sdf_b, grid, -(1.0 - a) * d)


def build_shells(sdfs: Sequence[np.ndarray], grid: Grid, centroids_mm: np.ndarray | None = None,
                 substeps: int = SUBSTEPS, max_tris: int = SHELL_MAX_TRIS, occ_size: int = contract.OCC_SIZE,
                 occ_margin_mm: float = OCC_MARGIN_MM, gating_substeps: bool = True) -> ShellsResult:
    """Plan step 4 from the fields onward: union, intersection, occupancy, the 10 x 10 gating
    table and the three shell meshes. Volumes are voxel counts on ``grid``.

    gating_substeps: when True, gating_cm3[s][l] includes the sub-steps BETWEEN the gated phases
    s .. s + l (not the ones before s or after s + l), so gating_cm3[s][0] = gtv_cm3[s] and
    gating_cm3[s][9] misses only the sub-steps of the one interval (s + 9) -> s.
    """
    sdfs = _check_fields(sdfs, grid)
    N = len(sdfs)
    if centroids_mm is None:
        c = np.array([centroid_of_inside(s, grid) for s in sdfs])
    else:
        c = np.asarray(centroids_mm, float).reshape(N, 3)

    inside = [np.asarray(s) < 0 for s in sdfs]
    for k, m in enumerate(inside):
        if not m.any():
            raise TubeError(f"phase {k} has no inside voxel on the shell grid")
        edge = m[0].any() or m[-1].any() or m[:, 0].any() or m[:, -1].any() or m[:, :, 0].any() or m[:, :, -1].any()
        if edge:
            raise TubeError(f"phase {k} touches the border of the shell grid: enlarge the grid")
    gtv = [voxel_volume_cm3(m, grid) for m in inside]

    itv_sdf = np.min(np.stack([np.asarray(s, np.float32) for s in sdfs]), axis=0)
    between: list[np.ndarray] = []  # between[k]: union of the sub-steps of the interval k -> k + 1
    for k in range(N):
        kn = (k + 1) % N
        acc = np.zeros(grid.shape, dtype=bool)
        for j in range(1, substeps + 1):
            f = substep_field(sdfs[k], sdfs[kn], grid, c[k], c[kn], j / (substeps + 1.0))
            np.minimum(itv_sdf, f, out=itv_sdf)
            acc |= f < 0
        between.append(acc)
    itv_inside = itv_sdf < 0
    if itv_inside[0].any() or itv_inside[-1].any() or itv_inside[:, 0].any() or itv_inside[:, -1].any() \
            or itv_inside[:, :, 0].any() or itv_inside[:, :, -1].any():
        raise TubeError("the union touches the border of the shell grid: enlarge the grid")
    core_sdf = np.max(np.stack([np.asarray(s, np.float32) for s in sdfs]), axis=0)

    gating = [[0.0] * N for _ in range(N)]
    for s in range(N):
        acc = inside[s].copy()
        gating[s][0] = voxel_volume_cm3(acc, grid)
        for l in range(1, N):
            if gating_substeps:
                acc |= between[(s + l - 1) % N]
            acc |= inside[(s + l) % N]
            gating[s][l] = voxel_volume_cm3(acc, grid)

    count = np.zeros(grid.shape, dtype=np.int16)
    for m in inside:
        count += m
    rho = (count / float(N)).astype(np.float32)
    max_count = int(count.max())
    top = np.argwhere(count == max_count)  # C order: lowest ix, then iy, then iz first
    d2 = ((top - top.mean(axis=0)) ** 2).sum(axis=1)
    argmax_idx = top[int(np.argmin(d2))]  # nearest to the centroid of the top set; ties: first in C order
    argmax_mm = grid.to_mm(argmax_idx)

    need = int(math.ceil(0.5 * N))  # inside at least this many phases <=> rho >= 0.5
    rho50_sdf = np.partition(np.stack([np.asarray(s, np.float32) for s in sdfs]), need - 1, axis=0)[need - 1]

    meshes = {}
    for name, fld in (("itv", itv_sdf), ("core", core_sdf), ("rho50", rho50_sdf)):
        V, F = marching_mesh(fld, grid, 0.0) if (fld < 0).any() else (np.zeros((0, 3)), np.zeros((0, 3), dtype=np.int64))
        if len(F):
            V, F = decimate_shell(V, F, max_tris)
        meshes[name] = (V, F)

    # occupancy texture: rho sampled at the centres of an occ_size^3 grid over the union's box
    idx = np.argwhere(itv_inside)
    lo = grid.to_mm(idx.min(axis=0) - 0.5) - occ_margin_mm
    hi = grid.to_mm(idx.max(axis=0) + 0.5) + occ_margin_mm
    step = (hi - lo) / occ_size
    ax = [lo[a] + (np.arange(occ_size) + 0.5) * step[a] for a in range(3)]
    Z, Y, X = np.meshgrid(ax[2], ax[1], ax[0], indexing="ij")  # arrays indexed [iz, iy, ix]
    pts = np.stack([X.ravel(), Y.ravel(), Z.ravel()], axis=1)
    occ_count = np.zeros(len(pts), dtype=np.int16)
    for s in sdfs:
        occ_count += sample_trilinear(s, grid, pts)[0] < 0
    occ = np.rint(255.0 * occ_count / float(N)).astype(np.uint8).reshape(occ_size, occ_size, occ_size)

    info = {
        "voxel_mm": float(grid.spacing_mm),
        "volume_method": "voxel count of sdf < 0 on the shell grid",
        "substep_interpolation": "blend of the two neighbouring phase fields, each shifted along the straight segment between their centroids",
        "gating_includes_substeps": bool(gating_substeps),
        "rho_samples": "the 10 phases",
        "rho50": "boundary of rho >= 0.5",
        "argmax_rho_tiebreak": "voxel of highest rho nearest to the centroid of all such voxels",
        "occ_margin_mm": float(occ_margin_mm),
        "shell_max_triangles": int(max_tris),
    }
    return ShellsResult(
        grid=grid, substeps=int(substeps), centroids_mm=c, gtv_cm3=gtv,
        itv_cm3=voxel_volume_cm3(itv_inside, grid), core_cm3=voxel_volume_cm3(core_sdf < 0, grid),
        gating_cm3=gating, max_rho=max_count / float(N), argmax_rho_mm=[float(v) for v in argmax_mm],
        itv_sdf=itv_sdf, core_sdf=core_sdf, rho=rho, rho50_sdf=rho50_sdf, meshes=meshes, occ=occ,
        occ_bbox_min_mm=[float(v) for v in lo], occ_bbox_max_mm=[float(v) for v in hi], info=info,
    )


def make_table(sh: ShellsResult, case_id: str, origin_mm: Sequence[float], extra: dict | None = None) -> contract.Table:
    c = np.asarray(sh.centroids_mm, float)
    ex = dict(sh.info)
    if extra:
        ex.update(extra)
    return contract.Table(
        id=case_id, gtv_cm3=[float(v) for v in sh.gtv_cm3], itv_cm3=float(sh.itv_cm3), core_cm3=float(sh.core_cm3),
        gating_cm3=[[float(v) for v in row] for row in sh.gating_cm3],
        centroid_mm=[[float(v) for v in row] for row in c],
        centroid_range_mm=[float(v) for v in (c.max(axis=0) - c.min(axis=0))],
        argmax_rho_mm=[float(v) for v in sh.argmax_rho_mm], max_rho=float(sh.max_rho),
        origin_mm=[float(v) for v in origin_mm], occ_bbox_min_mm=sh.occ_bbox_min_mm, occ_bbox_max_mm=sh.occ_bbox_max_mm,
        occ_dims=[int(n) for n in sh.occ.shape[::-1]], substeps=int(sh.substeps), extra=ex,
    )


def write_shell_files(sh: ShellsResult, case_dir: Path | str, case_id: str, origin_mm: Sequence[float],
                      extra: dict | None = None) -> list[Path]:
    """Write itv.glb, core.glb, rho50.glb (display frame), occ.r8 and table.json. Returns the paths."""
    case_dir = Path(case_dir)
    case_dir.mkdir(parents=True, exist_ok=True)
    out = []
    for name in ("itv", "core", "rho50"):
        V, F = sh.meshes[name]
        p = case_dir / f"{name}.glb"
        if len(F) == 0:
            write_glb(p, [])  # an empty shell: a valid GLB with a scene and no meshes
        else:
            Vd = contract.to_display(V, origin_mm)
            write_glb(p, [GlbMesh(positions=Vd, indices=F, normals=vertex_normals(Vd, F), name=name)])
        out.append(p)
    (case_dir / "occ.r8").write_bytes(np.ascontiguousarray(sh.occ, dtype=np.uint8).tobytes(order="C"))
    out.append(case_dir / "occ.r8")
    contract.write_table(case_dir / "table.json", make_table(sh, case_id, origin_mm, extra))
    out.append(case_dir / "table.json")
    return out


# ============================================================================================
# Four dimensions: slicing and the crack check (Appendix A.8, R2 D.4)
# ============================================================================================
def slice_tets(P: np.ndarray, T: np.ndarray, n: Sequence[float], w0: float) -> np.ndarray:
    """Cut of a tetrahedral mesh by the hyperplane n . x = w0 (A.8).

    With f_i = n . x_i - w0, every edge whose ends lie on opposite sides (f > 0 against f <= 0)
    gives the point x_i + f_i / (f_i - f_j) * (x_j - x_i). One or three vertices on the positive
    side give a triangle, two give a quadrilateral (written as two triangles). Returns the
    triangles as (K, 3, D) points in the mesh's own D-dimensional coordinates; triangles may be
    degenerate (zero area) when the hyperplane passes through vertices.
    """
    P = np.asarray(P, dtype=np.float64)
    T = np.asarray(T, dtype=np.int64).reshape(-1, 4)
    f = P @ np.asarray(n, dtype=np.float64) - float(w0)
    ft = f[T]
    pos = ft > 0
    npos = pos.sum(axis=1)
    order = np.argsort(~pos, axis=1, kind="stable")  # positive vertices first, original order kept
    Ts = np.take_along_axis(T, order, axis=1)
    fs = np.take_along_axis(ft, order, axis=1)

    def cross(rows: np.ndarray, i: int, j: int) -> np.ndarray:
        fi, fj = fs[rows, i], fs[rows, j]
        t = fi / (fi - fj)
        return P[Ts[rows, i]] + t[:, None] * (P[Ts[rows, j]] - P[Ts[rows, i]])

    out = []
    r1 = np.nonzero(npos == 1)[0]  # lone positive vertex 0
    if len(r1):
        out.append(np.stack([cross(r1, 0, 1), cross(r1, 0, 2), cross(r1, 0, 3)], axis=1))
    r3 = np.nonzero(npos == 3)[0]  # lone non-positive vertex 3
    if len(r3):
        out.append(np.stack([cross(r3, 0, 3), cross(r3, 1, 3), cross(r3, 2, 3)], axis=1))
    r2 = np.nonzero(npos == 2)[0]  # a, b positive; c, d not: quad (a,c), (a,d), (b,d), (b,c)
    if len(r2):
        q0, q1, q2, q3 = cross(r2, 0, 2), cross(r2, 0, 3), cross(r2, 1, 3), cross(r2, 1, 2)
        out.append(np.stack([q0, q1, q2], axis=1))
        out.append(np.stack([q0, q2, q3], axis=1))
    if not out:
        return np.zeros((0, 3, P.shape[1]))
    return np.concatenate(out, axis=0)


def slice_tau(tube: contract.Tube4D, tau: float, drop_degenerate: bool = True, use_slabs: bool = True) -> np.ndarray:
    """The slice of a tube at lane coordinate ``tau`` (wrapped into [0, Lambda)): triangles
    (K, 3, 3) of x, y, z in display metres. With ``use_slabs`` only the one slab that contains
    tau is cut (one slab in ten). At tau = Lambda * k / layers the result is ring k's mesh."""
    lam, L = float(tube.Lambda), int(tube.layers)
    # the ring taus are stored as float32, so compare in float32 to make "exactly on ring k" exact
    tau32 = np.float32(math.fmod(float(tau), lam))
    if tau32 < 0:
        tau32 = np.float32(tau32 + np.float32(lam))
    ring_tau = (np.float64(lam) * np.arange(L + 1) / L).astype(np.float32)
    if tau32 >= ring_tau[L]:
        tau32 = np.float32(0.0)
    T = tube.T
    if use_slabs:
        k = int(np.searchsorted(ring_tau, tau32, side="right") - 1)
        T = T[tube.slab == k]
    tris = slice_tets(tube.P, T, (0.0, 0.0, 0.0, 1.0), float(tau32))[:, :, :3]
    if drop_degenerate and len(tris):
        a2 = np.linalg.norm(np.cross(tris[:, 1] - tris[:, 0], tris[:, 2] - tris[:, 0]), axis=1)
        tris = tris[a2 > 1e-14]
    return tris


def weld(tris: np.ndarray, tol: float = 1e-9) -> tuple[np.ndarray, np.ndarray]:
    """Triangle soup (K, 3, 3) -> indexed mesh (V, F): points closer than ``tol`` per axis (after
    rounding to the tolerance) become one vertex; triangles that lose an edge are dropped."""
    tris = np.asarray(tris, float)
    pts = tris.reshape(-1, 3)
    key = np.round(pts / tol).astype(np.int64)
    _, first, inv = np.unique(key, axis=0, return_index=True, return_inverse=True)
    F = inv.reshape(-1, 3)
    ok = (F[:, 0] != F[:, 1]) & (F[:, 1] != F[:, 2]) & (F[:, 0] != F[:, 2])
    return pts[first], F[ok]


def face_sharing(T: np.ndarray) -> dict:
    """The crack check. Counts, for every triangular face of the tets, how many tets hold it.

    Returns {"faces", "interior" (held by exactly 2), "boundary" (held by 1), "overshared"
    (held by 3 or more), "boundary_faces" (B, 3) sorted vertex indices}. A tube without cracks
    has overshared = 0 and its boundary faces are exactly the triangles of ring 0 and of the last ring.
    """
    T = np.asarray(T, dtype=np.int64).reshape(-1, 4)
    faces = np.concatenate([T[:, [0, 1, 2]], T[:, [0, 1, 3]], T[:, [0, 2, 3]], T[:, [1, 2, 3]]])
    faces = np.sort(faces, axis=1)
    uniq, counts = np.unique(faces, axis=0, return_counts=True)
    return {
        "faces": int(len(uniq)),
        "interior": int((counts == 2).sum()),
        "boundary": int((counts == 1).sum()),
        "overshared": int((counts > 2).sum()),
        "boundary_faces": uniq[counts == 1],
    }


def tube_crack_report(tube: contract.Tube4D, n_template_verts: int) -> dict:
    """face_sharing of a tube plus where its boundary lies: ``boundary_on_caps`` is True when every
    face held by one tet only has all three vertices on ring 0 or all three on the last ring, and
    ``caps_coincide`` when the last ring repeats ring 0 exactly (so the tube closes over a breath)."""
    rep = face_sharing(tube.T)
    ring = rep.pop("boundary_faces") // int(n_template_verts)
    L = int(tube.layers)
    same = (ring[:, 0] == ring[:, 1]) & (ring[:, 1] == ring[:, 2])
    rep["boundary_on_caps"] = bool(len(ring) > 0 and (same & ((ring[:, 0] == 0) | (ring[:, 0] == L))).all())
    rep["boundary_ring0"] = int((same & (ring[:, 0] == 0)).sum())
    rep["boundary_last_ring"] = int((same & (ring[:, 0] == L)).sum())
    nV = int(n_template_verts)
    rep["caps_coincide"] = bool(np.array_equal(tube.P[:nV, :3], tube.P[L * nV:(L + 1) * nV, :3]))
    return rep


# ============================================================================================
# Distances between meshes
# ============================================================================================
def _point_tri_d2(p: np.ndarray, a: np.ndarray, b: np.ndarray, c: np.ndarray) -> np.ndarray:
    """Squared distance from points p to triangles (a, b, c); all arrays broadcast to (..., 3).
    Closest point by the Voronoi regions of the triangle (Ericson, Real-Time Collision Detection 5.1.5)."""
    ab, ac, ap = b - a, c - a, p - a
    d1 = np.einsum("...i,...i", ab, ap)
    d2 = np.einsum("...i,...i", ac, ap)
    bp = p - b
    d3 = np.einsum("...i,...i", ab, bp)
    d4 = np.einsum("...i,...i", ac, bp)
    cp = p - c
    d5 = np.einsum("...i,...i", ab, cp)
    d6 = np.einsum("...i,...i", ac, cp)
    vc = d1 * d4 - d3 * d2
    vb = d5 * d2 - d1 * d6
    va = d3 * d6 - d5 * d4
    with np.errstate(divide="ignore", invalid="ignore"):
        denom = va + vb + vc
        v = np.where(denom != 0, vb / denom, 0.0)
        w = np.where(denom != 0, vc / denom, 0.0)
        t_ab = np.where(d1 - d3 != 0, d1 / (d1 - d3), 0.0)
        t_ac = np.where(d2 - d6 != 0, d2 / (d2 - d6), 0.0)
        t_bc = np.where((d4 - d3) + (d5 - d6) != 0, (d4 - d3) / ((d4 - d3) + (d5 - d6)), 0.0)
    # default: interior of the face
    v_out, w_out = v, w
    cond_bc = (va <= 0) & ((d4 - d3) >= 0) & ((d5 - d6) >= 0)
    v_out = np.where(cond_bc, 1.0 - t_bc, v_out)
    w_out = np.where(cond_bc, t_bc, w_out)
    cond_ac = (vb <= 0) & (d2 >= 0) & (d6 <= 0)
    v_out = np.where(cond_ac, 0.0, v_out)
    w_out = np.where(cond_ac, t_ac, w_out)
    cond_ab = (vc <= 0) & (d1 >= 0) & (d3 <= 0)
    v_out = np.where(cond_ab, t_ab, v_out)
    w_out = np.where(cond_ab, 0.0, w_out)
    cond_c = (d6 >= 0) & (d5 <= d6)
    v_out = np.where(cond_c, 0.0, v_out)
    w_out = np.where(cond_c, 1.0, w_out)
    cond_b = (d3 >= 0) & (d4 <= d3)
    v_out = np.where(cond_b, 1.0, v_out)
    w_out = np.where(cond_b, 0.0, w_out)
    cond_a = (d1 <= 0) & (d2 <= 0)
    v_out = np.where(cond_a, 0.0, v_out)
    w_out = np.where(cond_a, 0.0, w_out)
    q = a + v_out[..., None] * ab + w_out[..., None] * ac
    d = p - q
    return np.einsum("...i,...i", d, d)


def point_mesh_distance(pts: np.ndarray, V: np.ndarray, F: np.ndarray, k: int = 32) -> np.ndarray:
    """Exact distance from each point to the triangle mesh (V, F).

    For each point the k triangles with the nearest centroids are tried first; the answer is
    proven exact when the k-th centroid is farther than that distance plus the largest
    centroid-to-corner length, and every point that fails the proof is redone against all triangles.
    """
    pts = np.asarray(pts, float).reshape(-1, 3)
    V = np.asarray(V, float)
    F = np.asarray(F, dtype=np.int64).reshape(-1, 3)
    if len(F) == 0:
        raise TubeError("point_mesh_distance: empty mesh")
    A, B, C = V[F[:, 0]], V[F[:, 1]], V[F[:, 2]]
    cen = (A + B + C) / 3.0
    rmax = float(np.sqrt(np.max([((X - cen) ** 2).sum(axis=1).max() for X in (A, B, C)])))
    k = min(k, len(F))
    tree = cKDTree(cen)
    dc, idx = tree.query(pts, k=k)
    if k == 1:
        dc, idx = dc[:, None], idx[:, None]
    out = np.empty(len(pts))
    chunk = max(1, 400_000 // k)
    for s in range(0, len(pts), chunk):
        sl = slice(s, s + chunk)
        d2 = _point_tri_d2(pts[sl, None, :], A[idx[sl]], B[idx[sl]], C[idx[sl]])
        out[sl] = np.sqrt(d2.min(axis=1))
    if k < len(F):
        unsure = np.nonzero(dc[:, -1] < out + rmax)[0]
        chunk = max(1, 2_000_000 // len(F))
        for s in range(0, len(unsure), chunk):
            rows = unsure[s:s + chunk]
            d2 = _point_tri_d2(pts[rows, None, :], A[None], B[None], C[None])
            out[rows] = np.sqrt(d2.min(axis=1))
    return out


def sample_surface(V: np.ndarray, F: np.ndarray, level: int = 4) -> np.ndarray:
    """Deterministic surface samples: the barycentric lattice of ``level`` subdivisions on every
    triangle (its corners, edge points and interior points); level 4 gives 15 points per triangle."""
    V = np.asarray(V, float)
    F = np.asarray(F, dtype=np.int64).reshape(-1, 3)
    w = np.array([(i, j, level - i - j) for i in range(level + 1) for j in range(level + 1 - i)], float) / level
    pts = np.einsum("wj,fjd->fwd", w, V[F])
    return pts.reshape(-1, 3)


def hausdorff(Va: np.ndarray, Fa: np.ndarray, Vb: np.ndarray, Fb: np.ndarray, level: int = 4) -> dict:
    """Symmetric Hausdorff distance between two triangle meshes, in the meshes' units.

    Each surface is sampled on a barycentric lattice (``level``) and every sample's exact
    distance to the other mesh is taken. Returns {"a_to_b", "b_to_a", "hausdorff", "mean"}.
    The one-sided maxima are over samples, so they can miss the true maximum by at most half the
    sample spacing (edge length / level / 2).
    """
    dab = point_mesh_distance(sample_surface(Va, Fa, level), Vb, Fb)
    dba = point_mesh_distance(sample_surface(Vb, Fb, level), Va, Fa)
    return {"a_to_b": float(dab.max()), "b_to_a": float(dba.max()), "hausdorff": float(max(dab.max(), dba.max())),
            "mean": float(0.5 * (dab.mean() + dba.mean()))}


def hausdorff_tris(tris: np.ndarray, Vb: np.ndarray, Fb: np.ndarray, level: int = 4) -> dict:
    """``hausdorff`` for a triangle soup (K, 3, 3) against an indexed mesh."""
    tris = np.asarray(tris, float)
    V = tris.reshape(-1, 3)
    F = np.arange(len(V)).reshape(-1, 3)
    return hausdorff(V, F, Vb, Fb, level)
