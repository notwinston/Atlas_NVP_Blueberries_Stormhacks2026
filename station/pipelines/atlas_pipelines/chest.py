"""The breathing chest of the real case: lungs, heart and trachea as morphing meshes (plan 10.2.4 step 5).

One closed template mesh per organ, carried onto each of the 10 phases, so that vertex i is the same
piece of surface at every phase. ``chest.glb`` stores the template as the base mesh and each phase as a
morph target (a displacement from the base), in the display frame of docs/case_contract.md.

Method (all in patient LPS millimetres; the display frame is applied only when the file is written)

    1. Mask: the rasterised contour mask of each phase (``atlas_pipelines.masks``), reduced to its
       largest connected piece. (In lung101 the left-lung ROI of phases 6 and 7 holds one stray
       single-slice contour on the patient's right side, 81 and 38 cm3; ``dropped_cm3`` in the report
       lists what was left out per phase. Nothing is dropped from either lung at phases 0 and 5 beyond
       a speck of 8 voxels.)
    2. Field: mask -> signed distance (anisotropic Euclidean transform on the CT grid, negative inside)
       -> resampled trilinearly onto one isotropic grid common to the 10 phases -> Gaussian of SIGMA_MM.
       This is ``tubelib.sdf_from_mask``, the field construction of the lesion tube, on a coarser grid
       (Part.voxel_mm: 2 mm for lungs and heart, 1 mm for the trachea) because the organs are large.
    3. Template: the mean of the 10 phase fields (not shifted: the organs are large against their
       motion), its zero surface by marching cubes, largest component, quadric decimation to at most
       Part.max_tris triangles, then projected back onto the mean field. A closed manifold.
    4. Correspondence: for each phase a smooth displacement field u_k that carries the mean field onto
       the phase field, phase_k(x + u_k(x)) = mean(x), found by demons registration of the two distance
       fields (``register``) on the grid of twice the spacing. Template vertex i goes to
       V0_i + u_k(V0_i) and is then projected onto the phase surface by Newton steps (``NEWTON_ITERS``).
       Why not project the template straight onto each phase field, as the tube does: the lower lung
       border moves up to 30 mm, and a projection along the field gradient sends the vertices of a thin
       lung edge to its side walls, so the moved edge is left uncovered (measured: 1.2 % of the volume
       lost at phase 0, surface points up to 18 mm from the mesh). Matching the whole distance field
       moves the edge to the edge.
    5. Nothing is scaled or offset to match a volume. ``measure`` reports, per phase, the closed-mesh
       volume next to the voxel volume of the mask and the contour-area volume.

What a part is: one closed surface (the outer boundary of the organ's largest connected piece in the
mean field). A pit that exists in one phase but not in the mean shape is bridged, not followed.

Known limit, stated rather than hidden: the masks sit about (-0.38, +0.26, 0.00) mm off the contours
(see masks.py, "KNOWN PROPERTY OF THIS DATA"). The chest is built from the masks, so it carries that
in-plane offset; it is 0.2 voxel of the chest's own grid and does not change any volume.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Sequence

import numpy as np
from scipy import ndimage

from . import contract, glb, masks, tubelib
from .tubelib import FieldSampler, Grid

VERSION = "chest/1"

PHASES = contract.PHASES
MAX_TRIS_TOTAL = 40_000  #: plan 10.2.5 / P2-D06
SIGMA_MM = 1.0  #: Gaussian on the distance field, as for the tube (R2 D.2)
MARGIN_MM = 8.0  #: grid margin round the union of the 10 masks
REG_SUBSAMPLE = 2  #: the registration runs on every 2nd voxel of the field grid
REG_ITERS = 200  #: demons iterations
REG_SIGMA_VOX = 1.0  #: Gaussian on the displacement field after every iteration, in registration voxels
REG_MAX_STEP_VOX = 0.5  #: largest update of one iteration, in registration voxels
NEWTON_ITERS = 6  #: Newton steps onto a field after the template is placed
TARGET_NAMES = tuple(f"phase_{k}" for k in range(PHASES))


@dataclass(frozen=True)
class Part:
    """One named mesh of chest.glb."""

    name: str  #: mesh name in the GLB
    roi: str  #: masks ROI key
    max_tris: int  #: triangle budget of this part
    voxel_mm: float  #: spacing of the isotropic field grid


#: The parts, in file order. The budgets add up to MAX_TRIS_TOTAL.
PARTS: tuple[Part, ...] = (
    Part("lung_r", "rlung", 15_000, 2.0),
    Part("lung_l", "llung", 15_000, 2.0),
    Part("heart", "heart", 6_000, 2.0),
    Part("trachea", "trachea", 4_000, 1.0),
)
LUNG_PARTS = ("lung_r", "lung_l")


class ChestError(ValueError):
    """The chest cannot be built from these masks."""


@dataclass
class PartMesh:
    """A template and its 10 phase positions, in patient millimetres."""

    part: Part
    V0: np.ndarray  #: (nV, 3) template (mean shape)
    F: np.ndarray  #: (nF, 3)
    V: np.ndarray  #: (10, nV, 3) phase positions
    residual_mm: np.ndarray  #: (10,) max |phase field| at the phase vertices
    mask_volume_cm3: np.ndarray = field(default_factory=lambda: np.zeros(PHASES))  #: voxel volume of the mask as loaded
    dropped_cm3: np.ndarray = field(default_factory=lambda: np.zeros(PHASES))  #: of which outside the largest piece
    field_volume_cm3: np.ndarray = field(default_factory=lambda: np.zeros(PHASES))  #: marching-cubes volume of the phase field
    shift_mm: np.ndarray = field(default_factory=lambda: np.zeros(PHASES))  #: largest Newton move after the registration

    @property
    def n_tris(self) -> int:
        return int(len(self.F))

    def volume_cm3(self, phase: int) -> float:
        return tubelib.mesh_volume(self.V[phase], self.F) / 1000.0


# ============================================================================================
# Fields
# ============================================================================================
def phase_fields(stack: np.ndarray, origin_mm: Sequence[float], spacing_mm: Sequence[float], voxel_mm: float,
                 sigma_mm: float = SIGMA_MM, margin_mm: float = MARGIN_MM) -> tuple[list[np.ndarray], Grid]:
    """Signed distance fields (float32, negative inside) of the masks ``stack`` [P, ix, iy, iz] on one
    isotropic grid that covers all of them with ``margin_mm`` to spare."""
    stack = np.asarray(stack).astype(bool)
    sp = np.asarray(spacing_mm, float)
    org = np.asarray(origin_mm, float)
    idx = np.argwhere(stack.any(axis=0))
    if len(idx) == 0:
        raise ChestError("empty mask stack")
    lo = org + (idx.min(axis=0) - 0.5) * sp
    hi = org + (idx.max(axis=0) + 0.5) * sp
    grid = Grid.covering(lo, hi, voxel_mm, margin_mm)
    # The mask crop must reach past the grid, or the distance transform has no outside voxel to measure
    # to on a side where the organ touches the crop: pad it with background.
    pad = [(int(np.ceil((margin_mm + voxel_mm) / s)) + 1,) * 2 for s in sp]
    fields = []
    for m in stack:
        mp = np.pad(m, pad, mode="constant", constant_values=False)
        fields.append(tubelib.sdf_from_mask(mp, org - np.array([p[0] for p in pad]) * sp, sp, grid, sigma_mm))
    return fields, grid


def mean_field(fields: Sequence[np.ndarray]) -> np.ndarray:
    acc = np.zeros(fields[0].shape, dtype=np.float64)
    for f in fields:
        acc += f
    return (acc / len(fields)).astype(np.float32)


# ============================================================================================
# Template and correspondence
# ============================================================================================
def _newton(V: np.ndarray, value_and_gradient, iters: int, max_step_mm: float) -> np.ndarray:
    """``iters`` Newton steps of every vertex onto the zero set: V -= G * grad G / |grad G|^2, each
    step no longer than ``max_step_mm``."""
    for _ in range(iters):
        val, g = value_and_gradient(V)
        g2 = np.einsum("ij,ij->i", g, g)
        step = (val / np.where(g2 > 1e-12, g2, 1.0))[:, None] * g
        ln = np.linalg.norm(step, axis=1)
        scale = np.where(ln > max_step_mm, max_step_mm / np.where(ln > 0, ln, 1.0), 1.0)
        V = V - step * scale[:, None]
    return V


def decimate(V: np.ndarray, F: np.ndarray, max_tris: int) -> tuple[np.ndarray, np.ndarray]:
    """Quadric decimation to at most ``max_tris`` triangles; the result must still be a closed manifold
    with the input's orientation, else the exact (slower) edge collapse of tubelib is used."""
    if len(F) <= max_tris:
        return np.asarray(V, float), np.asarray(F, dtype=np.int64)
    import fast_simplification

    target = max_tris
    for _ in range(6):
        v, f = fast_simplification.simplify(np.asarray(V, np.float32), np.asarray(F, np.int32), target_count=target)
        f = np.asarray(f, dtype=np.int64)
        ok = (f[:, 0] != f[:, 1]) & (f[:, 1] != f[:, 2]) & (f[:, 0] != f[:, 2])
        v, f = tubelib._compact(np.asarray(v, float), f[ok])
        if len(f) <= max_tris and tubelib.is_closed_manifold(f):
            if tubelib.mesh_volume(v, f) < 0:
                f = f[:, ::-1].copy()
            return v, f
        target = int(target * 0.97)
    n = max_tris - ((len(F) - max_tris) % 2)
    return tubelib.decimate_exact(V, F, n)


def build_template(mean: np.ndarray, grid: Grid, max_tris: int) -> tuple[np.ndarray, np.ndarray]:
    """Zero surface of the mean field: largest component, decimated, projected back onto the field."""
    V, F = tubelib.marching_mesh(mean, grid, 0.0)
    if len(F) == 0:
        raise ChestError("the mean shape is empty")
    V, F = tubelib.largest_component(V, F)
    if not tubelib.is_closed_manifold(F):
        raise ChestError("the mean-shape surface is not a closed manifold (does it touch the grid border?)")
    V, F = decimate(V, F, max_tris)
    smp = FieldSampler(mean, grid, "cubic")
    V = _newton(V, smp.value_and_gradient, NEWTON_ITERS, grid.spacing_mm)
    if tubelib.mesh_volume(V, F) <= 0:
        raise ChestError("template orientation is not outward")
    return V, F


def largest_piece(mask: np.ndarray) -> np.ndarray:
    """The largest face-connected piece of a mask (ties: the first label)."""
    lab, n = ndimage.label(mask)
    if n <= 1:
        return np.asarray(mask, bool)
    size = np.bincount(lab.ravel())
    size[0] = 0
    return lab == int(size.argmax())


def register(fixed: np.ndarray, moving: np.ndarray, spacing_mm: float, iters: int = REG_ITERS,
             sigma_vox: float = REG_SIGMA_VOX, max_step_vox: float = REG_MAX_STEP_VOX) -> np.ndarray:
    """Demons registration of two distance fields on one grid: the displacement u (3, nx, ny, nz), in
    voxels, with moving(x + u(x)) ~ fixed(x).

    Each iteration: update = -(m - f) * J / (|J|^2 + (m - f)^2 / h^2), with m the moving field at
    x + u, J the mean of the gradients of the fixed field and of the warped moving field, h the voxel
    size; the update is limited to ``max_step_vox``; then u is smoothed by a Gaussian of ``sigma_vox``.
    Deterministic, single precision.
    """
    f = np.asarray(fixed, dtype=np.float32)
    m = np.asarray(moving, dtype=np.float32)
    if f.shape != m.shape:
        raise ChestError("register: the two fields must share one grid")
    gf = np.stack(np.gradient(f))
    gm = np.gradient(m)
    idx = np.stack(np.meshgrid(*[np.arange(n, dtype=np.float32) for n in f.shape], indexing="ij"))
    u = np.zeros_like(idx)
    inv_h2 = np.float32(1.0 / (spacing_mm * spacing_mm))
    for _ in range(iters):
        at = idx + u
        diff = ndimage.map_coordinates(m, at, order=1, mode="nearest") - f
        J = np.stack([ndimage.map_coordinates(g, at, order=1, mode="nearest") for g in gm])
        J += gf
        J *= np.float32(0.5)
        du = -diff * J / ((J * J).sum(axis=0) + diff * diff * inv_h2 + np.float32(1e-9))
        ln = np.sqrt((du * du).sum(axis=0))
        du *= np.minimum(np.float32(1.0), np.float32(max_step_vox) / np.maximum(ln, np.float32(1e-9)))
        u += du
        for a in range(3):
            u[a] = ndimage.gaussian_filter(u[a], sigma_vox, mode="nearest")
    return u


def displace(V0: np.ndarray, u_vox: np.ndarray, grid: Grid) -> np.ndarray:
    """V0 + u(V0): the displacement field ``u_vox`` (voxels of ``grid``) sampled trilinearly, in mm."""
    at = grid.to_index(V0).T
    d = np.stack([ndimage.map_coordinates(u_vox[a], at, order=1, mode="nearest") for a in range(3)], axis=1)
    return np.asarray(V0, float) + d * grid.spacing_mm


def build_part(part: Part, stack: np.ndarray, origin_mm: Sequence[float], spacing_mm: Sequence[float]) -> PartMesh:
    """Template and 10 phase meshes of one organ from its mask stack [10, ix, iy, iz]."""
    stack = np.asarray(stack).astype(bool)
    if len(stack) != PHASES:
        raise ChestError(f"{part.name}: need {PHASES} phases, got {len(stack)}")
    vox = float(np.prod(np.asarray(spacing_mm, float))) / 1000.0
    kept = np.stack([largest_piece(m) for m in stack])
    fields, grid = phase_fields(kept, origin_mm, spacing_mm, part.voxel_mm)
    mean = mean_field(fields)
    V0, F = build_template(mean, grid, part.max_tris)
    sub = REG_SUBSAMPLE
    sl = (slice(None, None, sub),) * 3
    coarse = Grid(grid.origin_mm, grid.spacing_mm * sub, mean[sl].shape)
    V = np.empty((PHASES,) + V0.shape)
    res, shift, fvol = np.empty(PHASES), np.empty(PHASES), np.empty(PHASES)
    for k, f in enumerate(fields):
        u = register(mean[sl], f[sl], coarse.spacing_mm)
        W = displace(V0, u, coarse)
        smp = FieldSampler(f, grid, "cubic")
        V[k] = _newton(W, smp.value_and_gradient, NEWTON_ITERS, grid.spacing_mm)
        res[k] = float(np.abs(smp.value(V[k])).max())
        shift[k] = float(np.linalg.norm(V[k] - W, axis=1).max())
        Vm, Fm = tubelib.largest_component(*tubelib.marching_mesh(f, grid, 0.0))
        fvol[k] = tubelib.mesh_volume(Vm, Fm) / 1000.0
    return PartMesh(part=part, V0=V0, F=F, V=V, residual_mm=res,
                    mask_volume_cm3=np.array([float(m.sum()) * vox for m in stack]),
                    dropped_cm3=np.array([float(m.sum() - q.sum()) * vox for m, q in zip(stack, kept)]),
                    field_volume_cm3=fvol, shift_mm=shift)


def _job(args) -> PartMesh:
    case, cache, part = args
    stack, origin, spacing = masks.load_mask_stack(case, part.roi, cache)
    return build_part(part, stack, origin, spacing)


def build(case: str, cache=None, parts: Sequence[Part] = PARTS, jobs: int = 4, log=None) -> list[PartMesh]:
    """All parts of the chest from the masks of ``case``; ``jobs`` worker processes (the result does
    not depend on it)."""
    work = [(case, cache, p) for p in parts]
    jobs = max(1, min(int(jobs), 4, len(work)))
    if jobs == 1:
        out = [_job(w) for w in work]
    else:
        from concurrent.futures import ProcessPoolExecutor

        with ProcessPoolExecutor(max_workers=jobs) as ex:
            out = list(ex.map(_job, work))
    for pm in out:
        if log:
            log(f"chest: {pm.part.name}: {pm.n_tris} triangles, {len(pm.V0)} vertices, "
                f"max residual {pm.residual_mm.max():.4f} mm")
    total = sum(pm.n_tris for pm in out)
    if total > MAX_TRIS_TOTAL:
        raise ChestError(f"chest has {total} triangles, over the budget of {MAX_TRIS_TOTAL}")
    return out


# ============================================================================================
# Quality
# ============================================================================================
def is_closed(pm: PartMesh) -> bool:
    return tubelib.is_closed_manifold(pm.F)


def area_ratio(pm: PartMesh, phase: int) -> np.ndarray:
    """Per triangle: area at ``phase`` over area in the template (how far the surface is stretched)."""
    def area(V):
        return np.linalg.norm(np.cross(V[pm.F[:, 1]] - V[pm.F[:, 0]], V[pm.F[:, 2]] - V[pm.F[:, 0]]), axis=1)
    a0 = area(pm.V0)
    return area(pm.V[phase]) / np.where(a0 > 0, a0, 1.0)


def reversed_triangles(pm: PartMesh, phase: int) -> int:
    """Triangles of a phase mesh whose normal points against the template's (a folded surface)."""
    def nrm(V):
        return np.cross(V[pm.F[:, 1]] - V[pm.F[:, 0]], V[pm.F[:, 2]] - V[pm.F[:, 0]])
    return int((np.einsum("ij,ij->i", nrm(pm.V0), nrm(pm.V[phase])) < 0).sum())


def step_mm(pm: PartMesh) -> np.ndarray:
    """(10,) the largest vertex move from phase k to phase k + 1 (the last entry is the wrap 9 -> 0), mm."""
    return np.array([float(np.linalg.norm(pm.V[(k + 1) % PHASES] - pm.V[k], axis=1).max()) for k in range(PHASES)])


def measure(meshes: Sequence[PartMesh]) -> dict:
    """Per part and phase: mesh volume, mask volume, residual, reversed triangles, phase-to-phase step."""
    rep: dict = {"parts": {}, "triangles": int(sum(pm.n_tris for pm in meshes))}
    for pm in meshes:
        rep["parts"][pm.part.name] = {
            "roi": pm.part.roi,
            "triangles": pm.n_tris,
            "vertices": int(len(pm.V0)),
            "voxel_mm": float(pm.part.voxel_mm),
            "template_volume_cm3": float(tubelib.mesh_volume(pm.V0, pm.F) / 1000.0),
            "mesh_volume_cm3": [float(pm.volume_cm3(k)) for k in range(PHASES)],
            "mask_volume_cm3": [float(v) for v in pm.mask_volume_cm3],
            "dropped_cm3": [float(v) for v in pm.dropped_cm3],
            "field_volume_cm3": [float(v) for v in pm.field_volume_cm3],
            "residual_mm": [float(v) for v in pm.residual_mm],
            "newton_shift_mm": [float(v) for v in pm.shift_mm],
            "reversed_triangles": [reversed_triangles(pm, k) for k in range(PHASES)],
            "area_ratio_p01_p99": [[float(np.percentile(area_ratio(pm, k), 1)), float(np.percentile(area_ratio(pm, k), 99))]
                                   for k in range(PHASES)],
            "step_mm": [float(v) for v in step_mm(pm)],
        }
    lungs = [pm for pm in meshes if pm.part.name in LUNG_PARTS]
    if lungs:
        rep["lungs_mesh_volume_l"] = [float(sum(pm.volume_cm3(k) for pm in lungs) / 1000.0) for k in range(PHASES)]
        rep["lungs_mask_volume_l"] = [float(sum(pm.mask_volume_cm3[k] for pm in lungs) / 1000.0) for k in range(PHASES)]
    return rep


# ============================================================================================
# File
# ============================================================================================
def glb_meshes(meshes: Sequence[PartMesh], origin_mm: Sequence[float]) -> list[glb.GlbMesh]:
    """The parts as GLB meshes in the display frame: base = template, targets = phase displacements."""
    out = []
    for pm in meshes:
        base = contract.to_display(pm.V0, origin_mm)
        targets = {TARGET_NAMES[k]: contract.to_display(pm.V[k], origin_mm) - base for k in range(PHASES)}
        out.append(glb.GlbMesh(positions=base, indices=pm.F, normals=tubelib.vertex_normals(pm.V0, pm.F),
                               targets=targets, name=pm.part.name))
    return out


def write_chest(path: Path | str, meshes: Sequence[PartMesh], origin_mm: Sequence[float]) -> bytes:
    return glb.write_glb(path, glb_meshes(meshes, origin_mm))


def read_chest(path: Path | str) -> list[glb.GlbMesh]:
    return glb.read_glb(path)


def glb_phase_positions_mm(mesh: glb.GlbMesh, phase: int, origin_mm: Sequence[float] = (0.0, 0.0, 0.0)) -> np.ndarray:
    """Vertices of one part of chest.glb at ``phase`` (base + target), back in patient millimetres."""
    p = np.asarray(mesh.positions, np.float64) + np.asarray(mesh.targets[TARGET_NAMES[phase]], np.float64)
    return contract.from_display(p, origin_mm)


def glb_lung_volume_l(path: Path | str, phase: int) -> float:
    """Volume of both lungs at ``phase``, in litres at patient scale, measured on chest.glb itself:
    the closed-mesh volume of each lung part (base + morph target), display metres undone by M."""
    total = 0.0
    for m in read_chest(path):
        if m.name in LUNG_PARTS:
            V = glb_phase_positions_mm(m, phase)  # the origin does not change a volume
            total += tubelib.mesh_volume(V, np.asarray(m.indices).reshape(-1, 3))
    return total / 1.0e6
