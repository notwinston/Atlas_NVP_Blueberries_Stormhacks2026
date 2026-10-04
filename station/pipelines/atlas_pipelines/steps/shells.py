"""Build the shells of the real case's target lesion: itv.glb, core.glb, rho50.glb, occ.r8, table.json.

Master Plan 10.2.4 step 4 (R2 D.2), work package P2-D05. Needs ``atlas case tube4d`` (for
``frame.json``, the display origin) and the masks.

    atlas case shells [--case lung101] [--force]

Method, exactly as the plan writes it (nothing here is tuned to a test):

    * The fields are the ones ``tube4d`` builds (``steps.tube4d.fields``): per phase, the signed
      distance of the CT-grid mask, resampled to 1 mm isotropic and smoothed with a Gaussian of
      sigma = 1 mm.
    * Union (ITV) = minimum over the 10 phases and 4 sub-steps between neighbouring phases,
      wrapping 9 -> 0; intersection (core) = maximum over the 10 phases; occupancy rho = share of
      the 10 phases in which a point is inside (``tubelib.build_shells``).
    * Every volume in table.json is a count of 1 mm voxels with field < 0, times the voxel volume.

How the volumes were computed is written into table.json under ``extra``, together with the
plain raw-mask figures for comparison (``extra.raw_masks``): the union and the intersection of
the 10 raw masks counted on the CT grid, with no smoothing and no sub-steps. The R2 prototype's
47.4 and 17.2 cm3 are numbers of that raw-mask kind (rasterised at 1 mm in-plane on the 3 mm
slices). The smoothed-field method gives a smaller union: see ``extra.method_note``.

Which number is which (acceptance of P2-D05, changed with the owner's approval; see
tests/test_p2_d05_shells.py):

    itv_cm3                     43.98 cm3  the union by the plan's step 4 (smoothed fields, 4 sub-steps). It is the
                                           volume of the surface in itv.glb (the decimated mesh encloses 43.91 cm3,
                                           -0.16 %), so the app may show the two side by side. Accepted against
                                           itv.glb within 1 %, NOT against 47.4.
    extra.raw_masks.union_cm3   47.29 cm3  the plain union of the 10 raw masks, the quantity R2 C.2 measured as
                                           47.4 cm3. Accepted against 47.4 within 5 %. No file of the package shows
                                           a surface of this volume.
    core_cm3                    16.98 cm3  the intersection by step 4; accepted against 17.2 within 5 % (unchanged).

The twin's shells are written by ``atlas case twin``.
"""

from __future__ import annotations

import numpy as np

from .. import cache, contract, dicomio, paths, tubelib
from .. import masks as masklib
from . import tube4d

NAME = "shells"
HELP = "union, intersection and occupancy of the target lesion over the breath: shells, occ.r8, table.json (real case)"
CODE_VERSION = "shells/1+" + tube4d.CODE_VERSION

SUBSTEPS = tubelib.SUBSTEPS  # 4
OUTPUTS = ("itv.glb", "core.glb", "rho50.glb", "occ.r8", "table.json")

METHOD_NOTE = (
    "itv_cm3, core_cm3, gtv_cm3 and gating_cm3 are counts of 1 mm voxels of the smoothed signed distance fields "
    "(mask -> edt(outside) - edt(inside) -> 1 mm isotropic -> Gaussian sigma 1 mm), the union taken over the 10 phases "
    "and 4 sub-steps per interval. raw_masks holds the plain union and intersection of the 10 raw CT-grid masks "
    "(no smoothing, no sub-steps). Interpolating between the 3 mm slices and smoothing rounds off what a single slice "
    "or a single phase adds to the outline, so the field volumes are smaller than the raw-mask volumes."
)


def add_arguments(parser) -> None:
    pass


def build(f: tube4d.Fields) -> tuple[tubelib.ShellsResult, dict]:
    """The shells in memory and the ``extra`` block of table.json."""
    sh = tubelib.build_shells(f.sdfs, f.grid, centroids_mm=f.centroids_mm, substeps=SUBSTEPS)
    phases_only = tubelib.voxel_volume_cm3(np.min(np.stack(f.sdfs), axis=0) < 0, f.grid)
    extra = {
        "fields": {
            "source": "target lesion contour masks on the CT grid",
            "ct_voxel_mm": [float(v) for v in f.spacing_mm],
            "recipe": "edt(outside) - edt(inside) with the CT voxel size; trilinear resample to the shell grid; Gaussian",
            "sigma_mm": float(tube4d.SIGMA_MM),
            "roi_margin_mm": float(tube4d.ROI_MARGIN_MM),
        },
        "centroid_source": "mean of the mask voxel centres on the CT grid",
        "itv_phases_only_cm3": float(phases_only),
        "raw_masks": {
            "method": "plain union and intersection of the 10 raw masks, voxel count on the CT grid, no smoothing, no sub-steps",
            "union_cm3": float(f.raw_union_cm3()),
            "intersection_cm3": float(f.raw_intersection_cm3()),
            "gtv_cm3": [float(v) for v in f.raw_gtv_cm3()],
        },
        "method_note": METHOD_NOTE,
    }
    return sh, extra


def run(args) -> int:
    from ..cli import StepError

    if args.case not in dicomio.TCIA_CASES:
        raise StepError(f"shells: case {args.case!r} has no contour masks (the twin builds its own shells: `atlas case twin`)")
    frame_file = paths.frame_path(args.case, args.out)
    if not frame_file.is_file():
        raise StepError(f"shells: {frame_file} is missing: run `atlas case tube4d --case {args.case}` first")
    try:
        masklib.ensure(args.case, args.cache)
    except (masklib.MaskError, dicomio.DicomError) as e:
        raise StepError(f"shells: {e}") from e
    case = paths.case_dir(args.case, args.out, create=True)
    outputs = [case / n for n in OUTPUTS]

    def work() -> None:
        frame = contract.read_frame(frame_file)
        try:
            f = tube4d.fields(args.case, args.cache)
            sh, extra = build(f)
        except tubelib.TubeError as e:
            raise StepError(f"shells: {e}") from e
        if not np.allclose(frame.origin_mm, f.centroids_mm.mean(axis=0), atol=1e-5):
            raise StepError("shells: frame.json does not match the masks (origin_mm is not the mean phase centroid): "
                            f"rerun `atlas case tube4d --case {args.case}`")
        tubelib.write_shell_files(sh, case, args.case, frame.origin_mm, extra=extra)

    ran = cache.run_cached(
        stamp=paths.stamp_path(args.case, NAME, args.out),
        code_version=CODE_VERSION,
        params={"case": args.case, "substeps": SUBSTEPS, **tube4d.PARAMS},
        inputs=tube4d.mask_inputs(args.case, args.cache) + [frame_file],
        outputs=outputs,
        work=work,
        force=args.force,
    )
    t = contract.read_table(case / "table.json")
    raw = t.extra["raw_masks"]
    print(f"shells: {'built' if ran else 'up to date (cached)'} {case}")
    print(f"  smoothed fields, 1 mm voxels, {t.substeps} sub-steps: ITV {t.itv_cm3:.2f} cm3   core {t.core_cm3:.2f} cm3   "
          f"mean GTV {float(np.mean(t.gtv_cm3)):.2f} cm3   max rho {t.max_rho:.2f}")
    print(f"  raw masks on the CT grid:                    union {raw['union_cm3']:.2f} cm3   intersection {raw['intersection_cm3']:.2f} cm3")
    r = t.centroid_range_mm
    print(f"  target lesion centroid range LR / AP / SI = {r[0]:.2f} / {r[1]:.2f} / {r[2]:.2f} mm; "
          f"30-70 % gate {t.gating_cm3[3][4]:.2f} cm3")
    return 0
