"""Rasterise the contoured structures of every phase on the CT grid (tumour, lungs, heart, oesophagus, cord, trachea).

Master Plan 10.2.4 step 2, work package P2-D03. Needs ``atlas case fetch``. Writes intermediates only, under
``<cache>/masks/<case>/`` (never into the package): ``phase_<k>.npz``, ``contours_<k>.npz``, ``summary.json``.

    atlas case masks [--case lung101] [--force] [--json]

The file formats and the loader API for later steps (``load_mask``, ``load_mask_stack``, ``load_contours``,
``load_grid``, ``load_summary``, ``ensure``) are documented at the top of ``atlas_pipelines/masks.py``.

The cache stamp is ``<cache>/masks/<case>/stamp.json`` (next to the masks, not under ``--out``), because the
masks are a cache product shared by every output directory. Its input is the fetch step's ``index.json``.
"""

from __future__ import annotations

import json

from .. import dicomio
from .. import masks as masklib

NAME = "masks"
HELP = "rasterise tumour, lungs, heart, oesophagus, cord and trachea of the 10 phases on the CT grid"


def add_arguments(parser) -> None:
    parser.add_argument("--json", action="store_true", help="print summary.json instead of the text report")


def run(args) -> int:
    from ..cli import StepError

    if args.case not in dicomio.TCIA_CASES:
        raise StepError(f"masks: case {args.case!r} has no DICOM; only {sorted(dicomio.TCIA_CASES)} have masks")
    if not dicomio.index_path(args.cache, args.case).is_file():
        raise StepError(f"masks: the DICOM cache is missing: run `atlas case fetch --case {args.case}` first")
    try:
        ran = masklib.run_step(args.case, args.cache, force=args.force, log=None if args.json else print)
    except (masklib.MaskError, dicomio.DicomError) as e:
        raise StepError(f"masks: {e}") from e
    s = masklib.load_summary(args.case, args.cache)
    if args.json:
        print(json.dumps(s, indent=1, sort_keys=True))
        return 0
    g = s["grid"]
    print(f"masks: {'built' if ran else 'up to date (cached)'} {masklib.mask_dir(args.case, args.cache)}")
    print(f"  CT grid {g['shape']} voxels, spacing {g['spacing_mm']} mm, origin {g['origin_mm']} mm, "
          f"same grid on all phases: {s['same_grid']}")
    t = s["tumor_stats"]
    if t:
        r = t["centroid_range_mm"]
        print(f"  target lesion centroid range LR / AP / SI = {r[0]:.2f} / {r[1]:.2f} / {r[2]:.2f} mm")
        print(f"  target lesion volume mean {t['gtv_mean_cm3']:.1f} cm3, union {t['union_cm3']:.1f} cm3, "
              f"intersection {t['intersection_cm3']:.1f} cm3 (raw masks)")
    print("  contoured lung volume (contour area x slice spacing): "
          + ", ".join(f"{10 * l['phase']} % {l['contour_volume_l']:.2f} L" for l in s["lungs"]))
    return 0
