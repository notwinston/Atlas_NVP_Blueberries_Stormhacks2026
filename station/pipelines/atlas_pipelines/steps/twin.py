"""Build the illustrative twin: a 10 mm nodule on a Lujan breathing curve (tube and shells).

Master Plan 10.2.4 step 6 (then steps 3 and 4 through ``atlas_pipelines.tubelib``), R2 C.4.

Model (millimetres, axes L, P, S; the exhale position is the model's own origin):

    z(t) = z0 - b    * cos^(2n)(pi t / tau - phi0)                 head-to-feet, b = 15 mm, n = 2
    y(t) = y0 - b_AP * cos^(2n)(pi t / tau - phi0 - delta)         front-to-back, b_AP = 4 mm, delta = 24 deg

with tau = 4 s the breathing period and phase k of 10 at t = k tau / 10, so phase 0 is full
inhale (15 mm below the exhale position) and phase 5 is exhale. The lag delta between the two
axes opens the hysteresis loop; ``--b-ap 0`` switches it off.

The nodule is a sphere of 10 mm diameter. Its signed distance |x - c(t)| - 5 is analytic:
    * the tube is built from that field sampled on a 1 mm grid (as the real case is);
    * the shells, the occupancy and every volume of table.json are measured on a 0.25 mm grid,
      with 4 sub-steps between neighbouring phases (wrapping 9 -> 0).

Writes out/cases/twin/{tube4d.bin, tube4d_rigid.bin, phases.glb, itv.glb, core.glb, rho50.glb,
occ.r8, table.json} and out/work/twin/frame.json. ``pack`` adds manifest.json and CREDITS.txt.
"""

from __future__ import annotations

import math

import numpy as np

from .. import cache, contract, paths, tubelib

NAME = "twin"
HELP = "build the illustrative twin (Lujan sphere): tube, shells, occupancy, table"
DEFAULT_CASE = paths.TWIN_CASE
CODE_VERSION = "twin/1+" + tubelib.VERSION

DIAMETER_MM = 10.0
B_SI_MM = 15.0
N_EXP = 2
PERIOD_S = 4.0
B_AP_MM = 4.0
DELTA_DEG = 24.0
PHI0 = 0.0
TUBE_VOXEL_MM = 1.0
SHELL_VOXEL_MM = 0.25
TUBE_MARGIN_MM = 8.0
SHELL_MARGIN_MM = 3.0

OUTPUTS = ("tube4d.bin", "tube4d_rigid.bin", "phases.glb", "itv.glb", "core.glb", "rho50.glb", "occ.r8", "table.json")


def add_arguments(parser) -> None:
    parser.add_argument("--b-ap", type=float, default=B_AP_MM, metavar="MM",
                        help="front-to-back amplitude of the hysteresis loop in mm (default 4; 0 = no hysteresis)")
    parser.add_argument("--delta-deg", type=float, default=DELTA_DEG, metavar="DEG",
                        help="phase lag between the two axes in degrees (default 24)")


def centres_mm(b_ap_mm: float = B_AP_MM, delta_deg: float = DELTA_DEG, phases: int = contract.PHASES) -> np.ndarray:
    """Centre of the nodule at each phase, (phases, 3) in mm. Time is t = k * PERIOD_S / phases."""
    t = np.arange(phases) * PERIOD_S / phases
    arg = math.pi * t / PERIOD_S - PHI0
    z = -B_SI_MM * np.cos(arg) ** (2 * N_EXP)
    y = -float(b_ap_mm) * np.cos(arg - math.radians(delta_deg)) ** (2 * N_EXP)
    return np.stack([np.zeros(phases), y, z], axis=1)


def sphere_fields(centres: np.ndarray, grid: tubelib.Grid, radius_mm: float = DIAMETER_MM / 2.0) -> list[np.ndarray]:
    """The analytic signed distance |x - c| - r of each phase, sampled at the voxel centres."""
    X, Y, Z = grid.points()
    return [(np.sqrt((X - c[0]) ** 2 + (Y - c[1]) ** 2 + (Z - c[2]) ** 2) - radius_mm).astype(np.float32) for c in centres]


def grid_for(centres: np.ndarray, spacing_mm: float, margin_mm: float, radius_mm: float = DIAMETER_MM / 2.0) -> tubelib.Grid:
    return tubelib.Grid.covering(centres.min(axis=0) - radius_mm, centres.max(axis=0) + radius_mm, spacing_mm, margin_mm)


def build(b_ap_mm: float = B_AP_MM, delta_deg: float = DELTA_DEG) -> tuple[tubelib.TubeResult, tubelib.ShellsResult, dict]:
    """The whole twin in memory: (tube, shells, the model parameters recorded in table.json)."""
    c = centres_mm(b_ap_mm, delta_deg)
    g1 = grid_for(c, TUBE_VOXEL_MM, TUBE_MARGIN_MM)
    tube = tubelib.build_tube(sphere_fields(c, g1), g1, centroids_mm=c)
    g2 = grid_for(c, SHELL_VOXEL_MM, SHELL_MARGIN_MM)
    shells = tubelib.build_shells(sphere_fields(c, g2), g2, centroids_mm=c, substeps=tubelib.SUBSTEPS)
    model = {
        "model": "Lujan breathing curve, sphere",
        "diameter_mm": DIAMETER_MM,
        "b_mm": B_SI_MM,
        "n": N_EXP,
        "period_s": PERIOD_S,
        "b_ap_mm": float(b_ap_mm),
        "delta_deg": float(delta_deg),
        "tube_voxel_mm": TUBE_VOXEL_MM,
    }
    return tube, shells, model


def run(args) -> int:
    case = paths.case_dir(args.case, args.out, create=True)
    frame = paths.frame_path(args.case, args.out)
    outputs = [case / n for n in OUTPUTS] + [frame]

    def work() -> None:
        tube, shells, model = build(args.b_ap, args.delta_deg)
        tubelib.write_tube_files(tube, case)
        tubelib.write_shell_files(shells, case, args.case, tube.origin_mm, extra={"twin": model})
        paths.work_dir(args.case, args.out, create=True)
        contract.write_frame(frame, tube.frame(args.case))

    ran = cache.run_cached(
        stamp=paths.stamp_path(args.case, NAME, args.out),
        code_version=CODE_VERSION,
        params={"case": args.case, "b_ap_mm": float(args.b_ap), "delta_deg": float(args.delta_deg)},
        inputs=[],
        outputs=outputs,
        work=work,
        force=args.force,
    )
    t = contract.read_table(case / "table.json")
    print(f"twin: {'built' if ran else 'up to date (cached)'} {case}")
    print(f"  b_AP {args.b_ap:g} mm, delta {args.delta_deg:g} deg")
    print(f"  GTV {t.gtv_cm3[0]:.4f} cm3   ITV {t.itv_cm3:.4f} cm3   core {t.core_cm3:.4f} cm3   max rho {t.max_rho:.2f}")
    return 0
