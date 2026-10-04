"""Build the 4D tube of the real case's target lesion: tube4d.bin, tube4d_rigid.bin, phases.glb.

Master Plan 10.2.4 step 3 (R2 D.2, D.3), work package P2-D04. Needs ``fetch`` and ``masks`` (run
automatically when their results are missing).

    atlas case tube4d [--case lung101] [--force] [--no-report]

Method, exactly as the plan writes it (nothing here is tuned to a test):

    1. The target lesion masks of the 10 phases on the CT grid (0.9766 x 0.9766 x 3 mm), from
       ``atlas case masks``.
    2. ROI = bounding box of the union of the 10 masks + 15 mm, as a 1 mm isotropic grid.
    3. Per phase: signed distance = edt(outside) - edt(inside) with the anisotropic voxel size
       (negative inside), resampled trilinearly to the 1 mm grid, Gaussian of sigma = 1 mm
       (``tubelib.sdf_from_mask``).
    4. Mean-shape template of exactly 600 triangles, Newton projection onto each phase
       (5 iterations), prisms, 3 tetrahedra per prism with index-sorted diagonals
       (``tubelib.build_tube``, ``contract.tube_from_phases``): 18,000 tetrahedra.

Decisions of this step
    * The masks are the CT-grid masks of step 2, as the plan says, and not a finer re-rasterisation
      of the contours. The CT-grid masks sit about (-0.38, +0.26, 0.00) mm off the contours (see
      ``atlas_pipelines/masks.py``); the offset is the same on every phase and for every structure,
      and ``chest`` builds its organs from the same masks, so the lesion keeps its place relative
      to the lungs.
    * Phase centroids are the means of the mask voxel centres on the CT grid (the numbers of
      P2-D03). The display origin ``origin_mm`` is their mean and goes to ``frame.json``.
    * The Newton projection samples the fields with a cubic B-spline (``tubelib``'s default; the
      plan says only "sample").

Writes ``out/cases/<id>/{tube4d.bin, tube4d_rigid.bin, phases.glb}``, ``out/work/<id>/frame.json``
and, unless ``--no-report``, ``out/work/<id>/tube4d_report.json``: the crack check and the two-sided
Hausdorff distance (patient mm) between the tau-slice at each phase and that phase's contour mesh
(the undecimated marching-cubes surface of that phase's own smoothed signed distance field).

``fields(case, cache)`` is the one place where the phase fields of the real case are built; the
``shells`` step and the tests call it.
"""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import numpy as np

from .. import cache, contract, dicomio, paths, tubelib
from .. import masks as masklib

NAME = "tube4d"
HELP = "target lesion masks -> signed distance fields -> 600-triangle template -> 18,000-tet tube (real case)"
CODE_VERSION = "tube4d/1+" + tubelib.VERSION

ROI = "tumor"  #: the ROI key of the masks; anything shown to a user calls it the "target lesion"
ROI_MARGIN_MM = 15.0
VOXEL_MM = 1.0
SIGMA_MM = 1.0
N_TRIS = contract.TEMPLATE_TRIS
NEWTON_ITERS = tubelib.NEWTON_ITERS
INTERP = "cubic"

OUTPUTS = ("tube4d.bin", "tube4d_rigid.bin", "phases.glb")
REPORT_NAME = "tube4d_report.json"

PARAMS = {
    "roi": ROI,
    "roi_margin_mm": ROI_MARGIN_MM,
    "voxel_mm": VOXEL_MM,
    "sigma_mm": SIGMA_MM,
    "n_tris": N_TRIS,
    "newton_iters": NEWTON_ITERS,
    "interp": INTERP,
}


def add_arguments(parser) -> None:
    parser.add_argument("--no-report", action="store_true",
                        help="do not measure the per-phase Hausdorff distances (saves about a minute)")


# ---------------------------------------------------------------------------------------------
# the phase fields of the real case (shared with `shells` and the tests)
# ---------------------------------------------------------------------------------------------
@dataclass
class Fields:
    """The 10 phase fields of a real case and what they were built from. Patient LPS millimetres."""

    grid: tubelib.Grid  #: the 1 mm isotropic ROI grid
    sdfs: list[np.ndarray]  #: 10 smoothed signed distance fields on ``grid`` (negative inside)
    centroids_mm: np.ndarray  #: (10, 3) mask centroids on the CT grid
    stack: np.ndarray  #: bool [10, ix, iy, iz]: the raw masks on a common crop of the CT grid
    stack_origin_mm: np.ndarray  #: centre of voxel [0, 0, 0] of ``stack``
    spacing_mm: np.ndarray  #: CT voxel size (dx, dy, dz)

    @property
    def ct_voxel_cm3(self) -> float:
        return float(np.prod(self.spacing_mm)) / 1000.0

    def raw_gtv_cm3(self) -> list[float]:
        return [float(m.sum()) * self.ct_voxel_cm3 for m in self.stack]

    def raw_union_cm3(self) -> float:
        """Plain union of the 10 raw masks: voxel count on the CT grid, no smoothing, no sub-steps."""
        return float(self.stack.any(axis=0).sum()) * self.ct_voxel_cm3

    def raw_intersection_cm3(self) -> float:
        """Plain intersection of the 10 raw masks: voxel count on the CT grid."""
        return float(self.stack.all(axis=0).sum()) * self.ct_voxel_cm3

    def contour_mesh(self, k: int) -> tuple[np.ndarray, np.ndarray]:
        """Phase k's contour mesh: the undecimated marching-cubes surface of its own smoothed field (mm)."""
        return tubelib.marching_mesh(self.sdfs[k], self.grid, 0.0)


def mask_inputs(case: str, cache_dir: Path | str | None) -> list[Path]:
    """The files a step hashes as its mask input: the summary and the 10 phase masks."""
    d = masklib.mask_dir(case, cache_dir)
    return [d / "summary.json"] + [d / f"phase_{k}.npz" for k in range(contract.PHASES)]


def fields(case: str = paths.REAL_CASE, cache_dir: Path | str | None = None) -> Fields:
    """Plan step 3 up to the smoothed fields. Needs the masks (``masks.ensure``). Deterministic."""
    probe, origin0, spacing = masklib.load_mask_stack(case, ROI, cache_dir, pad=0)
    lo = origin0
    hi = origin0 + (np.array(probe.shape[1:]) - 1) * spacing  # voxel centres of the union's bounding box
    half = 0.5 * spacing  # a voxel covers +/- half its size round its centre
    grid = tubelib.Grid.covering(lo - half, hi + half, VOXEL_MM, ROI_MARGIN_MM)
    # the masks on a crop that covers the whole ROI grid, so the distance transform is exact inside it
    pad = int(np.ceil((ROI_MARGIN_MM + VOXEL_MM) / float(spacing.min()))) + 2
    stack, origin, spacing = masklib.load_mask_stack(case, ROI, cache_dir, pad=pad)
    cents = np.array([masklib.centroid_mm(m, origin, spacing) for m in stack])
    sdfs = [tubelib.sdf_from_mask(m, origin, spacing, grid, sigma_mm=SIGMA_MM) for m in stack]
    return Fields(grid=grid, sdfs=sdfs, centroids_mm=cents, stack=stack, stack_origin_mm=origin, spacing_mm=spacing)


def build(f: Fields) -> tubelib.TubeResult:
    return tubelib.build_tube(f.sdfs, f.grid, centroids_mm=f.centroids_mm, n_tris=N_TRIS,
                              newton_iters=NEWTON_ITERS, interp=INTERP)


# ---------------------------------------------------------------------------------------------
# measurements (the step's report and the acceptance test use the same definitions)
# ---------------------------------------------------------------------------------------------
def slice_mm(tube: contract.Tube4D, k: int, origin_mm) -> np.ndarray:
    """The tau-slice of the tube at phase k (tau = Lambda * k / layers) as triangles (K, 3, 3) in patient mm."""
    tris = tubelib.slice_tau(tube, float(tube.Lambda) * k / int(tube.layers))
    return contract.from_display(tris.reshape(-1, 3), origin_mm, tube.M).reshape(-1, 3, 3)


def phase_hausdorff(tube: contract.Tube4D, origin_mm, f: Fields, k: int) -> dict:
    """Two-sided Hausdorff distance (patient mm) between the tau-slice at phase k and phase k's
    contour mesh. Keys: slice_to_contour, contour_to_slice, hausdorff, mean, plus the share of the
    contour mesh's vertices farther than 1 mm from the slice."""
    tris = slice_mm(tube, k, origin_mm)
    Vr, Fr = f.contour_mesh(k)
    h = tubelib.hausdorff_tris(tris, Vr, Fr)
    Vs = tris.reshape(-1, 3)
    d = tubelib.point_mesh_distance(Vr, Vs, np.arange(len(Vs)).reshape(-1, 3))
    return {
        "slice_to_contour": h["a_to_b"],
        "contour_to_slice": h["b_to_a"],
        "hausdorff": h["hausdorff"],
        "mean": h["mean"],
        "contour_vertices_beyond_1mm": float(np.mean(d > 1.0)),
        "slice_triangles": int(len(tris)),
        "contour_triangles": int(len(Fr)),
    }


def crack_report(tube: contract.Tube4D) -> dict:
    """``tubelib.tube_crack_report`` with the template size taken from the tube itself."""
    nV = tube.n_verts // (int(tube.layers) + 1)
    return tubelib.tube_crack_report(tube, nV)


def _report(case_dir: Path, tube_res: tubelib.TubeResult, f: Fields, measure: bool) -> dict:
    tube = contract.read_tube4d(case_dir / "tube4d.bin")
    rigid = contract.read_tube4d(case_dir / "tube4d_rigid.bin")
    rep: dict = {
        "schema": "atlas.tube4d_report/1",
        "params": dict(PARAMS),
        "grid": {"origin_mm": list(f.grid.origin_mm), "voxel_mm": f.grid.spacing_mm, "shape": [int(n) for n in f.grid.shape]},
        "template_vertices": int(tube_res.V0.shape[0]),
        "template_triangles": int(len(tube_res.F)),
        "tets": int(tube.n_tets),
        "tets_rigid": int(rigid.n_tets),
        "cracks": {k: (bool(v) if isinstance(v, (bool, np.bool_)) else int(v)) for k, v in crack_report(tube).items()},
        "newton_residual_mm": [float(v) for v in tube_res.residual_mm],
        "hausdorff_measured": bool(measure),
    }
    if measure:
        per = [phase_hausdorff(tube, tube_res.origin_mm, f, k) for k in range(contract.PHASES)]
        rep["hausdorff_mm"] = per
        rep["hausdorff_max_mm"] = float(max(p["hausdorff"] for p in per))
        rep["hausdorff_definition"] = ("two-sided Hausdorff distance in patient mm between the tau-slice of tube4d.bin at "
                                       "tau = Lambda * k / 10 and the undecimated marching-cubes surface of phase k's own "
                                       "smoothed signed distance field (1 mm grid)")
    return rep


# ---------------------------------------------------------------------------------------------
# the step
# ---------------------------------------------------------------------------------------------
def run(args) -> int:
    from ..cli import StepError

    if args.case not in dicomio.TCIA_CASES:
        raise StepError(f"tube4d: case {args.case!r} has no contour masks (the twin builds its own tube: `atlas case twin`)")
    try:
        masklib.ensure(args.case, args.cache)
    except (masklib.MaskError, dicomio.DicomError) as e:
        raise StepError(f"tube4d: {e}") from e

    case = paths.case_dir(args.case, args.out, create=True)
    work = paths.work_dir(args.case, args.out, create=True)
    frame = paths.frame_path(args.case, args.out)
    report = work / REPORT_NAME
    measure = not args.no_report
    outputs = [case / n for n in OUTPUTS] + [frame, report]

    def work_fn() -> None:
        try:
            f = fields(args.case, args.cache)
            tube = build(f)
        except tubelib.TubeError as e:
            raise StepError(f"tube4d: {e}") from e
        tubelib.write_tube_files(tube, case)
        contract.write_frame(frame, tube.frame(args.case))
        contract.write_json(report, _report(case, tube, f, measure))

    ran = cache.run_cached(
        stamp=paths.stamp_path(args.case, NAME, args.out),
        code_version=CODE_VERSION,
        params={"case": args.case, "report": measure, **PARAMS},
        inputs=mask_inputs(args.case, args.cache),
        outputs=outputs,
        work=work_fn,
        force=args.force,
    )
    rep = contract.read_json(report)
    c = rep["cracks"]
    print(f"tube4d: {'built' if ran else 'up to date (cached)'} {case}")
    print(f"  template {rep['template_triangles']} triangles, {rep['template_vertices']} vertices; {rep['tets']} tetrahedra")
    print(f"  faces {c['faces']}: shared by 2 tets {c['interior']}, by 1 tet {c['boundary']} "
          f"(ring 0: {c['boundary_ring0']}, ring 10: {c['boundary_last_ring']}), by 3 or more {c['overshared']}")
    if rep["hausdorff_measured"]:
        vals = " ".join(f"{p['hausdorff']:.2f}" for p in rep["hausdorff_mm"])
        print(f"  slice-to-contour-mesh Hausdorff per phase (mm): {vals}; max {rep['hausdorff_max_mm']:.2f}")
    return 0
