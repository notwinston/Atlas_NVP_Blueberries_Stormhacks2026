"""Build the 4D tube of the real case's target lesion: tube4d.bin, tube4d_rigid.bin, phases.glb.

Master Plan 10.2.4 step 3 (R2 D.2, D.3), work package P2-D04. Needs ``fetch`` and ``masks`` (run
automatically when their results are missing).

    atlas case tube4d [--case lung101] [--force] [--no-report]

Method: the plan's, except for item 5 (a recorded deviation; nothing here is tuned to a test):

    1. The target lesion masks of the 10 phases on the CT grid (0.9766 x 0.9766 x 3 mm), from
       ``atlas case masks``.
    2. ROI = bounding box of the union of the 10 masks + 15 mm, as a 1 mm isotropic grid.
    3. Per phase: signed distance = edt(outside) - edt(inside) with the anisotropic voxel size
       (negative inside), resampled trilinearly to the 1 mm grid, Gaussian of sigma = 1 mm
       (``tubelib.sdf_from_mask``).
    4. Mean-shape template of exactly 600 triangles, carried onto each phase (item 5), prisms,
       3 tetrahedra per prism with index-sorted diagonals (``tubelib.build_tube``,
       ``contract.tube_from_phases``): 18,000 tetrahedra.
    5. DEVIATION from plan 10.2.4 step 3, which carries the template by "Newton projection" alone.
       Here the template, moved to the phase centroid, is first warped by a demons registration of the
       mean shape onto the phase field (the iteration and the parameters of the chest step: 200
       iterations, Gaussian of 1 voxel on the displacement, steps of at most 0.5 voxel, on the full
       1 mm grid) and then projected by the plan's 5 Newton steps, a step being withheld where it would
       turn a triangle more than 90 degrees from where the registration put it
       (``tubelib.build_tube(correspondence="register")``). Reason: the contoured shape changes from
       phase to phase; the projection alone leaves lobes of a phase uncovered and makes the surface cut
       through itself. The report holds both: the tube as shipped and, under ``plan_method``, the same
       measurements of the tube the plan's projection alone gives.

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
and ``out/work/<id>/tube4d_report.json``: the crack check, the registration's numbers and, unless
``--no-report``, the distances (patient mm) between the tau-slice at each phase and that phase's
contour mesh (the undecimated marching-cubes surface of that phase's own smoothed signed distance
field): both one-sided means, 95th percentiles and maxima (the larger maximum is the two-sided
Hausdorff distance), the self-intersection count of each phase mesh and the largest vertex step
between neighbouring phases.

Acceptance (P2-D04, changed with the owner's approval; see tests/test_p2_d04_tube4d.py): per phase the
mean distance is at most 0.5 mm and the 95th percentile at most 1.5 mm, in both directions. The
plan's "within 1 mm Hausdorff" is measured and reported but not met and not asserted: no closed
600-triangle surface of one piece can meet it for this lesion (phase 3's field has a detached piece
2.5 to 3.7 mm from the body, the fields of phases 5 and 8 have a handle).

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
CODE_VERSION = "tube4d/2+" + tubelib.VERSION

ROI = "tumor"  #: the ROI key of the masks; anything shown to a user calls it the "target lesion"
ROI_MARGIN_MM = 15.0
VOXEL_MM = 1.0
SIGMA_MM = 1.0
N_TRIS = contract.TEMPLATE_TRIS
NEWTON_ITERS = tubelib.NEWTON_ITERS
INTERP = "cubic"
CORRESPONDENCE = "register"  #: deviation from the plan's "Newton projection" alone; see the module text

#: the plan's acceptance number (10.2.8, P2-D04): measured and reported, not met, not asserted
PLAN_HAUSDORFF_MM = 1.0
#: the acceptance numbers in force (fixed by the owner's delegate before the results were known)
ACCEPT_MEAN_MM = 0.5
ACCEPT_P95_MM = 1.5

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
    "correspondence": CORRESPONDENCE,
    "reg_iters": tubelib.REG_ITERS,
    "reg_sigma_vox": tubelib.REG_SIGMA_VOX,
    "reg_max_step_vox": tubelib.REG_MAX_STEP_VOX,
    "no_fold_cos_min": tubelib.NO_FOLD_COS_MIN,
    "no_fold_max_step_mm": tubelib.NO_FOLD_MAX_STEP_MM,
}


def add_arguments(parser) -> None:
    parser.add_argument("--no-report", action="store_true",
                        help="do not measure the per-phase surface distances (saves about two minutes)")


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


def build(f: Fields, correspondence: str = CORRESPONDENCE) -> tubelib.TubeResult:
    """The tube of the real case. ``correspondence="translate"`` gives the plan's projection alone
    (used only for the comparison in the report)."""
    return tubelib.build_tube(f.sdfs, f.grid, centroids_mm=f.centroids_mm, n_tris=N_TRIS,
                              newton_iters=NEWTON_ITERS, interp=INTERP, correspondence=correspondence)


# ---------------------------------------------------------------------------------------------
# measurements (the step's report and the acceptance test use the same definitions)
# ---------------------------------------------------------------------------------------------
def slice_mm(tube: contract.Tube4D, k: int, origin_mm) -> np.ndarray:
    """The tau-slice of the tube at phase k (tau = Lambda * k / layers) as triangles (K, 3, 3) in patient mm."""
    tris = tubelib.slice_tau(tube, float(tube.Lambda) * k / int(tube.layers))
    return contract.from_display(tris.reshape(-1, 3), origin_mm, tube.M).reshape(-1, 3, 3)


def mesh_distances(tris_mm: np.ndarray, f: Fields, k: int) -> dict:
    """Distances (patient mm) between a phase-k surface given as triangles (K, 3, 3) and phase k's
    contour mesh, on a lattice of 15 points per triangle of either surface, each with the exact
    point-to-triangle distance. Keys, each for the direction slice -> contour mesh and contour mesh
    -> slice: the maximum (``slice_to_contour``, ``contour_to_slice``; ``hausdorff`` is the larger),
    the mean (``mean_*``; ``mean`` is the average of the two) and the 95th percentile (``p95_*``);
    plus the share of the contour mesh's vertices farther than 1 mm from the slice."""
    Vr, Fr = f.contour_mesh(k)
    h = tubelib.hausdorff_tris(tris_mm, Vr, Fr)
    Vs = tris_mm.reshape(-1, 3)
    d = tubelib.point_mesh_distance(Vr, Vs, np.arange(len(Vs)).reshape(-1, 3))
    return {
        "slice_to_contour": h["a_to_b"],
        "contour_to_slice": h["b_to_a"],
        "hausdorff": h["hausdorff"],
        "mean": h["mean"],
        "mean_slice_to_contour": h["mean_a_to_b"],
        "mean_contour_to_slice": h["mean_b_to_a"],
        "p95_slice_to_contour": h["p95_a_to_b"],
        "p95_contour_to_slice": h["p95_b_to_a"],
        "contour_vertices_beyond_1mm": float(np.mean(d > 1.0)),
        "slice_triangles": int(len(tris_mm)),
        "contour_triangles": int(len(Fr)),
    }


def phase_hausdorff(tube: contract.Tube4D, origin_mm, f: Fields, k: int) -> dict:
    """``mesh_distances`` of the tau-slice of ``tube`` at phase k."""
    return mesh_distances(slice_mm(tube, k, origin_mm), f, k)


def fold_report(V: np.ndarray, F: np.ndarray, V0: np.ndarray) -> dict:
    """Does the surface fold, and how far does it move from phase to phase? ``V`` (10, nV, 3) mm.

    self_intersections[k]          (edge, triangle) pairs of phase mesh k that cross. With 0 the closed
                                   surface does not cut through itself, and since its volume is positive
                                   every triangle faces outward.
    self_intersections_between[k]  the same for the surfaces a quarter, half and three quarters of the way
                                   from phase k to phase k + 1 (9 -> 0 for the last entry), summed.
    min_adjacent_normal_cos[k]     sharpest crease: smallest cosine between the normals of two triangles
                                   that share an edge (-1 would be a triangle folded flat onto its neighbour).
    turned_against_template[k]     triangles whose normal is more than 90 degrees from the normal of the
                                   same triangle in the template (information: a small triangle on a
                                   surface that bends can turn that far without any fold).
    mesh_volume_cm3[k]             closed-mesh volume.
    step_mm[k]                     largest vertex move from phase k to k + 1 (last entry: the wrap 9 -> 0).
    """
    n = len(V)
    n0 = np.cross(V0[F[:, 1]] - V0[F[:, 0]], V0[F[:, 2]] - V0[F[:, 0]])
    cross, between, crease, turned = [], [], [], []
    for k in range(n):
        cross.append(tubelib.self_intersections(V[k], F))
        nxt = V[(k + 1) % n]
        between.append(sum(tubelib.self_intersections((1.0 - a) * V[k] + a * nxt, F) for a in (0.25, 0.5, 0.75)))
        crease.append(tubelib.min_adjacent_normal_cos(V[k], F))
        nk = np.cross(V[k][F[:, 1]] - V[k][F[:, 0]], V[k][F[:, 2]] - V[k][F[:, 0]])
        turned.append(int((np.einsum("ij,ij->i", nk, n0) < 0).sum()))
    steps = tubelib.phase_steps_mm(V)
    return {"self_intersections": cross, "self_intersections_between": between,
            "min_adjacent_normal_cos": [float(v) for v in crease], "turned_against_template": turned,
            "mesh_volume_cm3": [float(tubelib.mesh_volume(V[k], F) / 1000.0) for k in range(n)],
            "step_mm": [float(v) for v in steps], "step_max_mm": float(steps.max())}


def _distance_summary(per: list[dict]) -> dict:
    return {
        "hausdorff_max_mm": float(max(p["hausdorff"] for p in per)),
        "mean_max_mm": float(max(max(p["mean_slice_to_contour"], p["mean_contour_to_slice"]) for p in per)),
        "p95_max_mm": float(max(max(p["p95_slice_to_contour"], p["p95_contour_to_slice"]) for p in per)),
    }


def crack_report(tube: contract.Tube4D) -> dict:
    """``tubelib.tube_crack_report`` with the template size taken from the tube itself."""
    nV = tube.n_verts // (int(tube.layers) + 1)
    return tubelib.tube_crack_report(tube, nV)


def _report(case_dir: Path, tube_res: tubelib.TubeResult, f: Fields, measure: bool) -> dict:
    tube = contract.read_tube4d(case_dir / "tube4d.bin")
    rigid = contract.read_tube4d(case_dir / "tube4d_rigid.bin")
    rep: dict = {
        "schema": "atlas.tube4d_report/2",
        "params": dict(PARAMS),
        "grid": {"origin_mm": list(f.grid.origin_mm), "voxel_mm": f.grid.spacing_mm, "shape": [int(n) for n in f.grid.shape]},
        "template_vertices": int(tube_res.V0.shape[0]),
        "template_triangles": int(len(tube_res.F)),
        "tets": int(tube.n_tets),
        "tets_rigid": int(rigid.n_tets),
        "cracks": {k: (bool(v) if isinstance(v, (bool, np.bool_)) else int(v)) for k, v in crack_report(tube).items()},
        "newton_residual_mm": [float(v) for v in tube_res.residual_mm],
        "registration": {
            "withheld_moves": [int(v) for v in tube_res.info["withheld_moves"]],
            "min_jacobian": [float(v) for v in tube_res.info["min_jacobian"]],
            "min_jacobian_definition": "smallest determinant of the Jacobian of x -> x + u(x) over the voxels within "
                                       "3 mm of the mean shape's surface; positive means the registration does not fold",
        },
        "folds": fold_report(tube_res.V, tube_res.F, tube_res.V0),
        "hausdorff_measured": bool(measure),
    }
    if measure:
        per = [phase_hausdorff(tube, tube_res.origin_mm, f, k) for k in range(contract.PHASES)]
        rep["hausdorff_mm"] = per
        rep.update(_distance_summary(per))
        rep["hausdorff_definition"] = ("distances in patient mm between the tau-slice of tube4d.bin at tau = Lambda * k / 10 "
                                       "and the undecimated marching-cubes surface of phase k's own smoothed signed distance "
                                       "field (1 mm grid), on a lattice of 15 points per triangle of either surface: per "
                                       "direction the maximum, the mean and the 95th percentile; hausdorff = the larger maximum")
        rep["acceptance"] = {
            "criterion": "per phase and per direction: mean distance <= mean_mm and 95th percentile <= p95_mm",
            "mean_mm": ACCEPT_MEAN_MM,
            "p95_mm": ACCEPT_P95_MM,
            "met": bool(rep["mean_max_mm"] <= ACCEPT_MEAN_MM and rep["p95_max_mm"] <= ACCEPT_P95_MM),
            "plan_hausdorff_mm": PLAN_HAUSDORFF_MM,
            "plan_hausdorff_met": bool(rep["hausdorff_max_mm"] <= PLAN_HAUSDORFF_MM),
            "note": "the plan's 1 mm Hausdorff is reported, not asserted (approved deviation)",
        }
        # the same measurements of the tube the plan's projection alone gives (not written to any file)
        plan = build(f, "translate")
        per0 = [mesh_distances(plan.V[k][plan.F], f, k) for k in range(contract.PHASES)]
        rep["plan_method"] = {"correspondence": "translate", "hausdorff_mm": per0, **_distance_summary(per0),
                              "folds": fold_report(plan.V, plan.F, plan.V0),
                              "newton_residual_mm": [float(v) for v in plan.residual_mm]}
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
    fo = rep["folds"]
    print(f"  registration + fold-free projection: self-intersections {sum(fo['self_intersections'])}, "
          f"between phases {sum(fo['self_intersections_between'])}, "
          f"sharpest crease cos {min(fo['min_adjacent_normal_cos']):.2f}, "
          f"withheld moves {sum(rep['registration']['withheld_moves'])}, "
          f"largest vertex step between neighbouring phases {fo['step_max_mm']:.2f} mm")
    if rep["hausdorff_measured"]:
        per = rep["hausdorff_mm"]
        print("  slice against contour mesh, per phase (mm), worse direction:")
        print("    mean            " + " ".join(f"{max(p['mean_slice_to_contour'], p['mean_contour_to_slice']):.2f}" for p in per)
              + f"; max {rep['mean_max_mm']:.2f} (accept <= {ACCEPT_MEAN_MM})")
        print("    95th percentile " + " ".join(f"{max(p['p95_slice_to_contour'], p['p95_contour_to_slice']):.2f}" for p in per)
              + f"; max {rep['p95_max_mm']:.2f} (accept <= {ACCEPT_P95_MM})")
        print("    Hausdorff       " + " ".join(f"{p['hausdorff']:.2f}" for p in per)
              + f"; max {rep['hausdorff_max_mm']:.2f} (plan: {PLAN_HAUSDORFF_MM}; reported, not asserted)")
        pm = rep["plan_method"]
        print(f"  for comparison, the plan's projection alone: Hausdorff max {pm['hausdorff_max_mm']:.2f}, mean max "
              f"{pm['mean_max_mm']:.2f}, 95th percentile max {pm['p95_max_mm']:.2f}, self-intersections "
              f"{sum(pm['folds']['self_intersections'])}, largest step {pm['folds']['step_max_mm']:.2f} mm")
    return 0
