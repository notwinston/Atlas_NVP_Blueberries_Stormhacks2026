"""Build the needle sky of the real case: ten clearance maps, the robust map and sky.json.

Master Plan 10.2.4 step 8, work package P2-D08. Needs the masks; uses ``table.json`` (from
``atlas case shells``) for the target point and falls back to the raw masks when it is absent.

    atlas case sky [--case lung101] [--force]

Writes ``<out>/cases/<id>/sky/{risk_p0.r8 ... risk_p9.r8, robust.r8, sky.json}``. The method, the
frames and the byte encoding are documented in ``atlas_pipelines/sky.py`` and in section 9 of
docs/case_contract.md. This is a geometric clearance map for education.
"""

from __future__ import annotations

from .. import cache, contract, dicomio, paths
from .. import masks as masklib
from .. import sky as skylib

NAME = "sky"
HELP = "needle sky: clearance maps per phase and the robust map over the sphere of directions (real case)"
#: The key depends on the modules this step calls: the sky computation and the mask rasteriser.
CODE_VERSION = "sky-step/1+" + skylib.VERSION + "+" + masklib.CODE_VERSION

PARAMS = {
    "nside": skylib.NSIDE,
    "ordering": skylib.ORDERING,
    "width": skylib.WIDTH,
    "height": skylib.HEIGHT,
    "threshold_mm": skylib.THRESHOLD_MM,
    "mm_per_count": skylib.MM_PER_COUNT,
    "ray_step_mm": skylib.RAY_STEP_MM,
    "ray_max_mm": skylib.RAY_MAX_MM,
    "organs": list(skylib.ORGANS),
    "checks": skylib.N_CHECKS,
}


def add_arguments(parser) -> None:
    pass


def run(args) -> int:
    from ..cli import StepError

    if args.case not in dicomio.TCIA_CASES:
        raise StepError(f"sky: case {args.case!r} has no contoured organs (the sky exists only in the real case)")
    try:
        masklib.ensure(args.case, args.cache)
    except (masklib.MaskError, dicomio.DicomError) as e:
        raise StepError(f"sky: {e}") from e
    case = paths.case_dir(args.case, args.out, create=True)
    sky_dir = case / "sky"
    table = case / "table.json"
    has_table = table.is_file()
    outputs = [sky_dir / n for n in skylib.FILES]

    def work() -> None:
        try:
            target, source = skylib.choose_target(args.case, case, args.cache)
            res = skylib.build(args.case, args.cache, target, source, log=print)
        except (skylib.SkyError, masklib.MaskError, contract.ContractError) as e:
            raise StepError(f"sky: {e}") from e
        skylib.write_files(res, sky_dir)

    ran = cache.run_cached(
        stamp=paths.stamp_path(args.case, NAME, args.out),
        code_version=CODE_VERSION,
        params={"case": args.case, "target": "table" if has_table else "fallback", **PARAMS},
        inputs=skylib.mask_inputs(args.case, args.cache) + ([table] if has_table else []),
        outputs=outputs,
        work=work,
        force=args.force,
    )
    s = contract.read_json(sky_dir / skylib.JSON_FILE)
    t = s["target_mm"]
    print(f"sky: {'built' if ran else 'up to date (cached)'} {sky_dir}")
    print(f"  target {t[0]:.1f} / {t[1]:.1f} / {t[2]:.1f} mm ({'table.json' if not s['target_fallback_used'] else 'FALLBACK: raw masks'}); "
          f"{s['npix']} directions, nside {s['nside']}, {s['ordering']} ordering")
    st = s["stats"]
    print(f"  clearance at the target per phase: {min(st['target_clearance_mm']):.2f} .. {max(st['target_clearance_mm']):.2f} mm")
    print(f"  share of sky with clearance >= {s['threshold_mm']:g} mm per phase: "
          f"{100 * min(st['clear_share']):.1f} .. {100 * max(st['clear_share']):.1f} %; robust over 10 phases {100 * st['robust_share']:.1f} %")
    return 0
