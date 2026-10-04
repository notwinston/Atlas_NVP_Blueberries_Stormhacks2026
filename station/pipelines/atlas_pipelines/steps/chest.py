"""Build the breathing chest of the real case: lungs, heart and trachea with 10 blend shapes (chest.glb).

Master Plan 10.2.4 step 5, work package P2-D06. The method is in ``atlas_pipelines.chest``.

Reads   the masks of ``atlas case masks`` (fetched and rasterised on demand) and
        out/work/<case>/frame.json, the display frame written by ``atlas case tube4d``.
Writes  out/cases/<case>/chest.glb          the package file (docs/case_contract.md section 8)
        out/work/<case>/chest_report.json   measurements: triangles, and per phase the mesh volume,
                                            the mask volume and the contour-area volume (not shipped)

``--provisional-frame`` builds the chest before the tube exists: the origin is then the mean of the
10 mask centroids of the target lesion taken from the masks summary, frame.json is NOT written, and
the report says ``"frame": "provisional"``. Use it for checks only; the packaged chest must be built
from the tube's frame.json so that the target lesion sits at the right place inside the chest.
"""

from __future__ import annotations

import numpy as np

from .. import cache, chest, contract, masks, paths
from ..cli import StepError

NAME = "chest"
HELP = "lungs, heart and trachea with 10 blend shapes -> chest.glb (about 36k triangles, limit 40k)"
CODE_VERSION = "chest-step/1+" + chest.VERSION
REPORT_NAME = "chest_report.json"
REPORT_SCHEMA = "atlas.chest_report/1"


def add_arguments(parser) -> None:
    parser.add_argument("--provisional-frame", action="store_true",
                        help="if frame.json is missing, use the mean mask centroid of the target lesion as origin "
                             "(for checks; does not write frame.json)")
    parser.add_argument("--jobs", type=int, default=4, help="worker processes, 1 to 4 (default 4)")


def provisional_origin_mm(case: str, cache_dir) -> list[float]:
    """Mean of the 10 mask centroids of the target lesion (the contract's definition of origin_mm,
    evaluated on the masks; the tube step may use a slightly different centroid)."""
    s = masks.load_summary(case, cache_dir)
    c = np.array([p["centroid_mm"] for p in s["roi"]["tumor"]], dtype=np.float64)
    return [float(v) for v in c.mean(axis=0)]


def build_report(case: str, cache_dir, meshes, origin_mm, frame_kind: str) -> dict:
    """Everything measured on the result, with the two reference volumes next to the mesh volume."""
    s = masks.load_summary(case, cache_dir)
    rep = chest.measure(meshes)
    for name, r in rep["parts"].items():
        r["contour_volume_cm3"] = [float(p["contour_volume_cm3"]) for p in s["roi"][r["roi"]]]
    rep["lungs_contour_volume_l"] = [float(p["contour_volume_l"]) for p in s["lungs"]]
    rep.update({"schema": REPORT_SCHEMA, "id": case, "frame": frame_kind,
                "origin_mm": [float(v) for v in origin_mm], "M": float(contract.M_DISPLAY),
                "max_triangles": chest.MAX_TRIS_TOTAL, "target_triangles": chest.TARGET_TRIS_TOTAL,
                "method": {"sigma_mm": float(chest.SIGMA_MM), "registration_iters": chest.REG_ITERS,
                           "registration_sigma_vox": float(chest.REG_SIGMA_VOX),
                           "registration_subsample": chest.REG_SUBSAMPLE, "newton_iters": chest.NEWTON_ITERS,
                           "base": "mean template"}})
    return rep


def run(args) -> int:
    if args.case != paths.REAL_CASE:
        raise StepError(f"chest exists only for the real case {paths.REAL_CASE!r} (the twin is tube and shells only)")
    try:
        masks.ensure(args.case, args.cache)
    except Exception as e:  # fetch or rasterisation failed
        raise StepError(f"chest: the masks are not available: {e}") from e

    frame_file = paths.frame_path(args.case, args.out)
    inputs = [masks.mask_dir(args.case, args.cache) / "summary.json", masks.stamp_path(args.case, args.cache)]
    if frame_file.exists():
        frame = contract.read_frame(frame_file)
        if frame.id != args.case:
            raise StepError(f"{frame_file} belongs to case {frame.id!r}")
        if float(frame.M) != float(contract.M_DISPLAY):
            raise StepError(f"{frame_file}: M = {frame.M}, expected {contract.M_DISPLAY}")
        origin, frame_kind = [float(v) for v in frame.origin_mm], "frame.json"
        inputs.append(frame_file)
    elif args.provisional_frame:
        origin, frame_kind = provisional_origin_mm(args.case, args.cache), "provisional"
    else:
        raise StepError(f"{frame_file} is missing: run `atlas case tube4d --case {args.case}` first "
                        "(or pass --provisional-frame for a check build)")

    out = paths.case_dir(args.case, args.out, create=True) / "chest.glb"
    report = paths.work_dir(args.case, args.out, create=True) / REPORT_NAME

    def work() -> None:
        try:
            meshes = chest.build(args.case, args.cache, jobs=args.jobs, log=print)
        except (chest.ChestError, ValueError) as e:
            raise StepError(f"chest: {e}") from e
        chest.write_chest(out, meshes, origin)
        contract.write_json(report, build_report(args.case, args.cache, meshes, origin, frame_kind))

    ran = cache.run_cached(
        stamp=paths.stamp_path(args.case, NAME, args.out),
        code_version=CODE_VERSION,
        params={"case": args.case, "frame": frame_kind, "origin_mm": origin,
                "parts": [[p.name, p.roi, p.max_tris, p.voxel_mm] for p in chest.PARTS]},
        inputs=inputs,
        outputs=[out, report],
        work=work,
        force=args.force,
    )
    rep = contract.read_json(report)
    print(f"chest: {'built' if ran else 'up to date (cached)'} {out}")
    print(f"chest: {rep['triangles']} triangles (built to {chest.TARGET_TRIS_TOTAL}, limit {chest.MAX_TRIS_TOTAL}), frame: {rep['frame']}")
    for k in (0, 5):
        print(f"chest: lungs at phase {k}: mesh {rep['lungs_mesh_volume_l'][k]:.4f} L, "
              f"mask {rep['lungs_mask_volume_l'][k]:.4f} L, contour area x slice {rep['lungs_contour_volume_l'][k]:.4f} L")
    return 0
