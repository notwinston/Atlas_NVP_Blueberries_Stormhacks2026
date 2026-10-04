"""Structure masks of the real case on the CT grid: the rasteriser and THE LOADER API for later steps.

Produced by ``atlas case masks`` (after ``atlas case fetch``); read by tube4d, shells, chest and sky.

LOADER API (everything a later step needs)
------------------------------------------
    from atlas_pipelines import masks

    masks.ensure(case="lung101", cache=args.cache)              # fetch + rasterise if missing (idempotent)

    m, origin_mm, spacing_mm = masks.load_mask("lung101", "tumor", 3, cache=args.cache)
        m           bool array indexed [ix, iy, iz], CROPPED to the ROI's bounding box (pad=0 by default)
        origin_mm   (3,) float64: patient LPS position of the CENTRE of voxel m[0, 0, 0]
        spacing_mm  (3,) float64: (dx, dy, dz) = (0.9766, 0.9766, 3.0) for lung101
        => centre of voxel (i, j, k) = origin_mm + (i, j, k) * spacing_mm ; a voxel covers +/- spacing/2 around it
        options: pad=N  grow the crop by N voxels on every side (clipped at the CT volume)
                 crop=False  return the full CT grid (512 x 512 x 149 for lung101, 39 MB as bool)

    stack, origin_mm, spacing_mm = masks.load_mask_stack("lung101", "tumor", cache=args.cache, pad=2)
        stack       bool [10, nx, ny, nz]: all 10 phases on ONE common crop (the union's bounding box + pad),
                    so unions / intersections / occupancy are plain numpy reductions over axis 0

    polys = masks.load_contours("lung101", "tumor", 3, cache=args.cache)
        list of float64 (N, 3) arrays: the raw closed planar RTSTRUCT polygons, patient LPS mm, file order;
        each polygon lies in one axial plane (constant z); several polygons on one slice combine by XOR
        (even-odd), which is how a hole is encoded

    masks.load_grid("lung101", 3, cache=...)      -> dicomio.Grid(origin_mm, spacing_mm, shape) of the full CT grid
    masks.load_summary("lung101", cache=...)      -> dict: voxel counts, volumes, centroids, bounding boxes (below)
    masks.roi_names()                             -> ("tumor", "rlung", "llung", "lungs", "heart", "esophagus", "cord", "trachea")

Conventions
- ``case`` is "lung101" (the twin has no masks). ``phase`` is an INDEX 0..9; phase k is the 10k % bin.
- ``roi`` (case-insensitive): tumor (alias tumour, gtv), rlung, llung, lungs (= rlung OR llung), heart,
  esophagus (alias oesophagus), cord, trachea. The DICOM names are Tumor_cNN, RLung_cNN, LLung_cNN,
  Heart_cNN, Esophagus_cNN, Cord_cNN, Trachea_cNN with NN = 00..90. Wording for anything shown to a user:
  the real case's tumour ROI is the "target lesion".
- Frame: patient LPS millimetres, axes x = left, y = posterior, z = superior; array axes are (x, y, z).
  Not the display frame: apply contract.to_display yourself.
- All 10 phases of lung101 share one CT grid (checked by the step; ``summary["same_grid"]``).
- Rasterisation rule: a voxel is inside when its centre is inside the polygon of its slice (crossing
  number, the same rule as the R2 prototype); polygons on one slice are XOR-ed. A contour belongs to the
  slice nearest to its z (they coincide within 0.001 mm in this data set).

Storage: ``<cache>/masks/<case>/`` (intermediate, never in the package)
    phase_<k>.npz     per stored ROI: "<roi>" bool crop [ix, iy, iz], "<roi>__bbox" int64 [ix0, iy0, iz0, ix1, iy1, iz1]
                      (end exclusive, in CT voxel indices), plus grid_origin_mm, grid_spacing_mm, grid_shape
    contours_<k>.npz  per ROI: "<roi>__xyz" float64 (sum N, 3) and "<roi>__len" int64 (points per polygon)
    summary.json      schema "atlas.masks/1": grid, same_grid, rois, and per ROI per phase
                      {voxels, volume_cm3, contour_volume_cm3, centroid_mm, contour_centroid_mm, bbox_ijk, bbox_min_mm, bbox_max_mm, contours, slices},
                      plus "tumor_stats": centroid_range_mm (LR, AP, SI), contour_centroid_range_mm,
                      gtv_mean_cm3, union_cm3, intersection_cm3; and "lungs": per phase volume_l, contour_volume_l
    stamp.json        cache stamp (cache.StepCache); the input is the fetch step's index.json
  volume_cm3 = voxels x voxel volume. contour_volume_cm3 = sum of the polygon areas (shoelace; holes are
  not subtracted) x slice spacing: the "contour area x 3 mm" figure of the plan. centroid_mm = mean of the
  voxel centres. contour_centroid_mm = area-weighted centroid of the polygons themselves (holes ignored).
  bbox_*_mm are voxel-centre coordinates.

KNOWN PROPERTY OF THIS DATA: the mask is shifted against the contour by a fraction of a voxel
  The Pinnacle contours of lung101 run THROUGH the CT voxel centres (their vertices sit on the voxel-centre
  lattice, written with 0.01 mm precision), so for every boundary voxel the centre-in-polygon test is decided
  by that rounding. Measured for the tumour: mask centroid minus contour centroid = about (-0.38, +0.26, 0.00) mm
  on every phase, while the volume agrees within 0.3 %. The shift is common to all phases, so centroid RANGES,
  unions and intersections are unaffected (ranges 3.13 / 3.09 / 6.85 mm from masks, 3.14 / 3.07 / 6.87 from
  contours), but a surface built from these masks sits up to half a voxel (0.5 mm) off the contours in-plane.
  If you need a surface that follows the contours (tube4d: Hausdorff 1 mm against the contour mesh), build it
  from ``load_contours`` instead: ``masks.rasterise(polys, Grid(origin, (h, h, 3.0), shape))`` rasterises the
  same polygons on any in-plane grid (the slices must stay the CT slices), e.g. h = 0.25 mm, whose voxel
  centres do not coincide with the contour vertices.
"""

from __future__ import annotations

import io
import json
import zipfile
from functools import lru_cache
from pathlib import Path
from typing import Sequence

import numpy as np

from . import cache as cachelib
from . import dicomio, paths
from .dicomio import Grid

SUMMARY_SCHEMA = "atlas.masks/1"
CODE_VERSION = "masks/1"
PHASES = dicomio.PHASES

#: canonical ROI key -> ROI name prefix in the RTSTRUCT (the suffix is _cNN)
STORED_ROIS = {
    "tumor": "Tumor",
    "rlung": "RLung",
    "llung": "LLung",
    "heart": "Heart",
    "esophagus": "Esophagus",
    "cord": "Cord",
    "trachea": "Trachea",
}
COMPOSITE_ROIS = {"lungs": ("rlung", "llung")}
ALIASES = {"tumour": "tumor", "gtv": "tumor", "oesophagus": "esophagus", "lung": "lungs",
           "right_lung": "rlung", "left_lung": "llung", "spinal_cord": "cord"}


class MaskError(RuntimeError):
    pass


def roi_names() -> tuple[str, ...]:
    return ("tumor", "rlung", "llung", "lungs", "heart", "esophagus", "cord", "trachea")


def canonical_roi(roi: str) -> str:
    key = roi.strip().lower()
    key = key.split("_c")[0] if key.rsplit("_c", 1)[-1].isdigit() else key  # "Tumor_c30" -> "tumor"
    key = ALIASES.get(key, key)
    if key not in STORED_ROIS and key not in COMPOSITE_ROIS:
        raise MaskError(f"unknown ROI {roi!r}; known: {', '.join(roi_names())}")
    return key


def dicom_roi_name(roi: str, phase: int) -> str:
    """The ROI name in the RTSTRUCT of ``phase``, e.g. ("tumor", 3) -> "Tumor_c30"."""
    return f"{STORED_ROIS[canonical_roi(roi)]}_c{10 * dicomio.check_phase(phase):02d}"


def mask_dir(case: str = paths.REAL_CASE, cache: Path | str | None = None) -> Path:
    base = Path(cache) if cache is not None else paths.default_cache_dir()
    return base / "masks" / case


# ---------------------------------------------------------------------------------------------
# rasteriser
# ---------------------------------------------------------------------------------------------
def polygon_area_mm2(poly: np.ndarray) -> float:
    x, y = poly[:, 0], poly[:, 1]
    return 0.5 * abs(float(np.dot(x, np.roll(y, -1)) - np.dot(y, np.roll(x, -1))))


def polygons_centroid_mm(polys: Sequence[np.ndarray]) -> np.ndarray:
    """Area-weighted centroid of planar polygons (each weighted by its unsigned area; holes are not subtracted)."""
    total, acc = 0.0, np.zeros(3)
    for p in polys:
        x, y = p[:, 0], p[:, 1]
        xn, yn = np.roll(x, -1), np.roll(y, -1)
        cr = x * yn - xn * y
        a = 0.5 * float(cr.sum())
        if abs(a) < 1e-12:
            continue
        c = np.array([float(((x + xn) * cr).sum()) / (6 * a), float(((y + yn) * cr).sum()) / (6 * a), float(p[0, 2])])
        total += abs(a)
        acc += abs(a) * c
    if total == 0.0:
        raise MaskError("centroid of polygons without area")
    return acc / total


def rasterise_polygon(poly_xy: np.ndarray, x0: float, dx: float, nx: int, y0: float, dy: float, ny: int) -> np.ndarray:
    """Even-odd fill of one polygon on pixel centres. Returns bool [nx, ny].

    Pixel (i, j), centred at (x0 + i dx, y0 + j dy), is inside when a ray from it towards +x crosses the
    polygon an odd number of times; an edge (a, b) is crossed by row y when (ya > y) != (yb > y).
    """
    out = np.zeros((nx, ny), dtype=bool)
    xa, ya = poly_xy[:, 0], poly_xy[:, 1]
    xb, yb = np.roll(xa, 1), np.roll(ya, 1)
    j0 = max(0, int(np.ceil((ya.min() - y0) / dy)) - 1)
    j1 = min(ny, int(np.floor((ya.max() - y0) / dy)) + 2)
    if j1 <= j0:
        return out
    py = y0 + dy * np.arange(j0, j1)                       # [R]
    cross = (ya[None, :] > py[:, None]) != (yb[None, :] > py[:, None])  # [R, E]
    r, e = np.nonzero(cross)
    if r.size == 0:
        return out
    xint = (xb[e] - xa[e]) * (py[r] - ya[e]) / (yb[e] - ya[e]) + xa[e]
    # number of pixel centres strictly left of the crossing
    k = np.clip(np.ceil((xint - x0) / dx), 0, nx).astype(np.int64)
    cnt = np.zeros((j1 - j0, nx + 1), dtype=np.int64)
    np.add.at(cnt, (r, k), 1)
    # crossings per row are even, so "odd number of crossings to the right" == "odd number at or to the left"
    inside = (np.cumsum(cnt[:, :nx], axis=1) & 1).astype(bool)  # [R, nx]
    out[:, j0:j1] = inside.T
    return out


def rasterise(polys: Sequence[np.ndarray], grid: Grid) -> tuple[np.ndarray, float]:
    """XOR-fill planar polygons on ``grid``. Returns (bool [nx, ny, nz], largest |z - slice z| in mm)."""
    nx, ny, nz = grid.shape
    (x0, y0, z0), (dx, dy, dz) = grid.origin_mm, grid.spacing_mm
    vol = np.zeros(grid.shape, dtype=bool)
    worst = 0.0
    for p in polys:
        if np.ptp(p[:, 2]) > 1e-3:
            raise MaskError("a contour is not planar in z")
        t = (float(p[0, 2]) - z0) / dz
        k = int(round(t))
        worst = max(worst, abs(t - k) * dz)
        if abs(t - k) * dz > 0.25 * dz:
            raise MaskError(f"contour at z = {p[0, 2]} mm lies between CT slices (off by {abs(t - k) * dz:.3f} mm)")
        if not 0 <= k < nz:
            raise MaskError(f"contour at z = {p[0, 2]} mm is outside the CT volume")
        vol[:, :, k] ^= rasterise_polygon(p[:, :2], x0, dx, nx, y0, dy, ny)
    return vol, worst


def bbox_of(mask: np.ndarray) -> np.ndarray:
    """[ix0, iy0, iz0, ix1, iy1, iz1], end exclusive; zeros for an empty mask."""
    if not mask.any():
        return np.zeros(6, dtype=np.int64)
    lo, hi = [], []
    for a in range(3):
        idx = np.nonzero(mask.any(axis=tuple(b for b in range(3) if b != a)))[0]
        lo.append(int(idx[0]))
        hi.append(int(idx[-1]) + 1)
    return np.array(lo + hi, dtype=np.int64)


def centroid_mm(mask: np.ndarray, origin_mm, spacing_mm) -> np.ndarray:
    """Mean of the voxel centres of a mask indexed [ix, iy, iz]."""
    n = int(mask.sum())
    if n == 0:
        raise MaskError("centroid of an empty mask")
    c = []
    for a in range(3):
        w = mask.sum(axis=tuple(b for b in range(3) if b != a)).astype(np.float64)
        c.append(float(origin_mm[a]) + float(spacing_mm[a]) * float(np.dot(w, np.arange(mask.shape[a]))) / n)
    return np.array(c)


# ---------------------------------------------------------------------------------------------
# deterministic .npz (numpy's own writer stamps the current time into the zip)
# ---------------------------------------------------------------------------------------------
def _write_npz(path: Path, arrays: dict[str, np.ndarray]) -> None:
    tmp = path.with_name(path.name + ".tmp")
    with zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for name in sorted(arrays):
            buf = io.BytesIO()
            np.lib.format.write_array(buf, np.ascontiguousarray(arrays[name]), allow_pickle=False)
            info = zipfile.ZipInfo(name + ".npy", date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            z.writestr(info, buf.getvalue())
    tmp.replace(path)


# ---------------------------------------------------------------------------------------------
# build (called by the masks step)
# ---------------------------------------------------------------------------------------------
def _r(v, nd=4):
    return [round(float(x), nd) for x in v]


def build(case: str, cache: Path | str | None, log=None) -> list[Path]:
    """Rasterise every stored ROI of every phase. Returns the files written (the step's cache outputs)."""
    log = log or (lambda msg: None)
    out = mask_dir(case, cache)
    out.mkdir(parents=True, exist_ok=True)
    grids: list[Grid] = []
    per_roi: dict[str, list[dict]] = {r: [] for r in STORED_ROIS}
    extra_rois: dict[str, int] = {}
    tumours: list[tuple[np.ndarray, np.ndarray]] = []
    worst_z = 0.0
    files: list[Path] = []
    for k in range(PHASES):
        grid = dicomio.ct_grid(dicomio.ct_series_dir(cache, case, k))
        grids.append(grid)
        ds = dicomio.read_rtstruct(dicomio.rtstruct_series_dir(cache, case, k))
        contours = dicomio.roi_contours(ds)
        for name in contours:
            base = name.rsplit("_c", 1)[0]
            extra_rois[base] = extra_rois.get(base, 0) + 1
        arrays: dict[str, np.ndarray] = {
            "grid_origin_mm": np.array(grid.origin_mm, dtype=np.float64),
            "grid_spacing_mm": np.array(grid.spacing_mm, dtype=np.float64),
            "grid_shape": np.array(grid.shape, dtype=np.int64),
        }
        carrays: dict[str, np.ndarray] = {}
        for roi in STORED_ROIS:
            name = dicom_roi_name(roi, k)
            polys = contours.get(name)
            if not polys:
                raise MaskError(f"phase {k}: ROI {name} is missing or has no contours "
                                f"(present: {', '.join(sorted(contours))})")
            vol, wz = rasterise(polys, grid)
            worst_z = max(worst_z, wz)
            n = int(vol.sum())
            if n == 0:
                raise MaskError(f"phase {k}: ROI {name} rasterises to no voxel")
            bb = bbox_of(vol)
            crop = vol[bb[0]:bb[3], bb[1]:bb[4], bb[2]:bb[5]]
            arrays[roi] = crop
            arrays[f"{roi}__bbox"] = bb
            carrays[f"{roi}__xyz"] = np.vstack(polys)
            carrays[f"{roi}__len"] = np.array([len(p) for p in polys], dtype=np.int64)
            lo = np.array(grid.origin_mm) + bb[:3] * np.array(grid.spacing_mm)
            hi = np.array(grid.origin_mm) + (bb[3:] - 1) * np.array(grid.spacing_mm)
            per_roi[roi].append({
                "phase": k,
                "dicom_name": name,
                "voxels": n,
                "volume_cm3": round(n * grid.voxel_mm3 / 1000.0, 4),
                "contour_volume_cm3": round(sum(polygon_area_mm2(p) for p in polys) * grid.spacing_mm[2] / 1000.0, 4),
                "centroid_mm": _r(centroid_mm(crop, lo, grid.spacing_mm)),
                "contour_centroid_mm": _r(polygons_centroid_mm(polys)),
                "bbox_ijk": [int(v) for v in bb],
                "bbox_min_mm": _r(lo),
                "bbox_max_mm": _r(hi),
                "contours": len(polys),
                "slices": len({int(round((float(p[0, 2]) - grid.origin_mm[2]) / grid.spacing_mm[2])) for p in polys}),
            })
            if roi == "tumor":
                tumours.append((crop.copy(), bb))
        f1, f2 = out / f"phase_{k}.npz", out / f"contours_{k}.npz"
        _write_npz(f1, arrays)
        _write_npz(f2, carrays)
        files += [f1, f2]
        log(f"masks: phase {k}: " + ", ".join(f"{r} {per_roi[r][-1]['volume_cm3']:.1f}" for r in STORED_ROIS) + " cm3")

    same_grid = all(g == grids[0] for g in grids)
    g0 = grids[0]
    tumor_stats = None
    if same_grid:
        bbs = np.array([bb for _, bb in tumours])
        lo, hi = bbs[:, :3].min(0), bbs[:, 3:].max(0)
        stack = np.zeros((PHASES, *(hi - lo)), dtype=bool)
        for i, (crop, bb) in enumerate(tumours):
            o = bb[:3] - lo
            stack[i, o[0]:o[0] + crop.shape[0], o[1]:o[1] + crop.shape[1], o[2]:o[2] + crop.shape[2]] = crop
        cents = np.array([p["centroid_mm"] for p in per_roi["tumor"]])
        vols = [p["volume_cm3"] for p in per_roi["tumor"]]
        tumor_stats = {
            "centroid_range_mm": _r(cents.max(0) - cents.min(0)),
            "contour_centroid_range_mm": _r(np.ptp(np.array([p["contour_centroid_mm"] for p in per_roi["tumor"]]), axis=0)),
            "centroid_mean_mm": _r(cents.mean(0)),
            "gtv_mean_cm3": round(float(np.mean(vols)), 4),
            "gtv_min_cm3": round(float(np.min(vols)), 4),
            "gtv_max_cm3": round(float(np.max(vols)), 4),
            "union_cm3": round(int(stack.any(0).sum()) * g0.voxel_mm3 / 1000.0, 4),
            "intersection_cm3": round(int(stack.all(0).sum()) * g0.voxel_mm3 / 1000.0, 4),
            "note": "raw contour masks on the CT grid, no interpolation between phases",
        }
    lungs = [{
        "phase": k,
        "volume_l": round((per_roi["rlung"][k]["volume_cm3"] + per_roi["llung"][k]["volume_cm3"]) / 1000.0, 5),
        "contour_volume_l": round((per_roi["rlung"][k]["contour_volume_cm3"] + per_roi["llung"][k]["contour_volume_cm3"]) / 1000.0, 5),
    } for k in range(PHASES)]
    summary = {
        "schema": SUMMARY_SCHEMA,
        "case": case,
        "patient_id": dicomio.tcia_ids(case)["patient_id"],
        "phases": PHASES,
        "frame": "patient LPS mm; arrays indexed [ix, iy, iz]; voxel centre = origin_mm + index * spacing_mm",
        "same_grid": same_grid,
        "grid": g0.to_json(),
        "grids": [g.to_json() for g in grids],
        "voxel_mm3": round(g0.voxel_mm3, 6),
        "max_contour_to_slice_mm": round(worst_z, 6),
        "rois": list(STORED_ROIS),
        "composite_rois": {k: list(v) for k, v in COMPOSITE_ROIS.items()},
        "rtstruct_rois": {k: extra_rois[k] for k in sorted(extra_rois)},
        "roi": per_roi,
        "lungs": lungs,
        "tumor_stats": tumor_stats,
    }
    sp = out / "summary.json"
    tmp = sp.with_name(sp.name + ".tmp")
    tmp.write_text(json.dumps(summary, indent=1, sort_keys=True) + "\n", encoding="utf-8")
    tmp.replace(sp)
    files.append(sp)
    return files


def stamp_path(case: str = paths.REAL_CASE, cache: Path | str | None = None) -> Path:
    return mask_dir(case, cache) / "stamp.json"


def expected_outputs(case: str, cache: Path | str | None) -> list[Path]:
    d = mask_dir(case, cache)
    return [d / f"{kind}_{k}.npz" for k in range(PHASES) for kind in ("phase", "contours")] + [d / "summary.json"]


def run_step(case: str = paths.REAL_CASE, cache: Path | str | None = None, force: bool = False, log=None) -> bool:
    """Rasterise unless the stamp says the masks are current. Returns True when the work ran."""
    d = mask_dir(case, cache)
    d.mkdir(parents=True, exist_ok=True)
    ran = cachelib.run_cached(
        stamp=stamp_path(case, cache),
        code_version=CODE_VERSION,
        params={"case": case, "rois": list(STORED_ROIS)},
        inputs=[dicomio.index_path(cache, case)],  # carries a digest of every DICOM file
        outputs=expected_outputs(case, cache),
        work=lambda: build(case, cache, log=log),
        force=force,
        root=d,
    )
    if ran:
        _clear_caches()
    return ran


def ensure(case: str = paths.REAL_CASE, cache: Path | str | None = None, log=None) -> Path:
    """Run fetch and masks if needed (both idempotent; downloads about 0.3 GB the first time). Returns mask_dir."""
    from .steps import fetch

    fetch.ensure(cache, case, log=log)
    run_step(case, cache, log=log)
    return mask_dir(case, cache)


# ---------------------------------------------------------------------------------------------
# loaders
# ---------------------------------------------------------------------------------------------
@lru_cache(maxsize=4)
def _summary(path: str, mtime_ns: int) -> dict:
    return json.loads(Path(path).read_text(encoding="utf-8"))


def _clear_caches() -> None:
    _summary.cache_clear()


def load_summary(case: str = paths.REAL_CASE, cache: Path | str | None = None) -> dict:
    p = mask_dir(case, cache) / "summary.json"
    if not p.is_file():
        raise FileNotFoundError(f"{p} is missing: run `atlas case masks --case {case}` (or masks.ensure()) first")
    s = _summary(str(p), p.stat().st_mtime_ns)
    if s.get("schema") != SUMMARY_SCHEMA:
        raise MaskError(f"{p}: schema {s.get('schema')!r}, expected {SUMMARY_SCHEMA!r}")
    return s


def load_grid(case: str = paths.REAL_CASE, phase: int = 0, cache: Path | str | None = None) -> Grid:
    """The full CT grid of one phase."""
    return Grid.from_json(load_summary(case, cache)["grids"][dicomio.check_phase(phase)])


def _npz(case: str, cache, kind: str, phase: int):
    p = mask_dir(case, cache) / f"{kind}_{dicomio.check_phase(phase)}.npz"
    if not p.is_file():
        raise FileNotFoundError(f"{p} is missing: run `atlas case masks --case {case}` (or masks.ensure()) first")
    return np.load(p, allow_pickle=False)


def _load_stored(case: str, cache, roi: str, phase: int) -> tuple[np.ndarray, np.ndarray]:
    """(crop, bbox) of a stored or composite ROI in CT voxel indices."""
    with _npz(case, cache, "phase", phase) as z:
        if roi in STORED_ROIS:
            return z[roi], z[f"{roi}__bbox"]
        parts = [(z[r], z[f"{r}__bbox"]) for r in COMPOSITE_ROIS[roi]]
    lo = np.min([bb[:3] for _, bb in parts], axis=0)
    hi = np.max([bb[3:] for _, bb in parts], axis=0)
    m = np.zeros(tuple(hi - lo), dtype=bool)
    for crop, bb in parts:
        o = bb[:3] - lo
        m[o[0]:o[0] + crop.shape[0], o[1]:o[1] + crop.shape[1], o[2]:o[2] + crop.shape[2]] |= crop
    return m, np.concatenate([lo, hi])


def _place(crop: np.ndarray, bb: np.ndarray, lo: np.ndarray, hi: np.ndarray) -> np.ndarray:
    """Copy a crop with bounding box ``bb`` into a new array covering voxel range [lo, hi)."""
    m = np.zeros(tuple(int(v) for v in hi - lo), dtype=bool)
    a = np.maximum(bb[:3], lo)
    b = np.minimum(bb[3:], hi)
    if np.all(b > a):
        s = tuple(slice(int(a[i] - bb[i]), int(b[i] - bb[i])) for i in range(3))
        d = tuple(slice(int(a[i] - lo[i]), int(b[i] - lo[i])) for i in range(3))
        m[d] = crop[s]
    return m


def load_mask(case: str, roi: str, phase: int, cache: Path | str | None = None, *, crop: bool = True,
              pad: int = 0) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """One structure of one phase: (bool [ix, iy, iz], origin_mm (3,), spacing_mm (3,)) in patient LPS mm.

    ``origin_mm`` is the centre of voxel [0, 0, 0] of the returned array. By default the array is cropped to
    the ROI's bounding box; ``pad`` grows the crop by that many voxels (clipped at the CT volume);
    ``crop=False`` returns the full CT grid.
    """
    roi = canonical_roi(roi)
    grid = load_grid(case, phase, cache)
    data, bb = _load_stored(case, cache, roi, phase)
    shape = np.array(grid.shape)
    if crop:
        lo = np.maximum(bb[:3] - int(pad), 0)
        hi = np.minimum(bb[3:] + int(pad), shape)
    else:
        lo, hi = np.zeros(3, dtype=np.int64), shape
    m = _place(data, bb, lo, hi)
    spacing = np.array(grid.spacing_mm, dtype=np.float64)
    origin = np.array(grid.origin_mm, dtype=np.float64) + lo * spacing
    return m, origin, spacing


def load_mask_stack(case: str, roi: str, cache: Path | str | None = None, *, phases: Sequence[int] | None = None,
                    pad: int = 0) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Several phases on one common crop: (bool [P, ix, iy, iz], origin_mm, spacing_mm).

    The crop is the bounding box of the union over ``phases`` (default: all 10), grown by ``pad`` voxels.
    """
    roi = canonical_roi(roi)
    phases = list(range(PHASES)) if phases is None else [dicomio.check_phase(p) for p in phases]
    grids = [load_grid(case, p, cache) for p in phases]
    if any(g != grids[0] for g in grids):
        raise MaskError("the phases do not share one CT grid; resample before stacking")
    parts = [_load_stored(case, cache, roi, p) for p in phases]
    shape = np.array(grids[0].shape)
    lo = np.maximum(np.min([bb[:3] for _, bb in parts], axis=0) - int(pad), 0)
    hi = np.minimum(np.max([bb[3:] for _, bb in parts], axis=0) + int(pad), shape)
    stack = np.stack([_place(c, bb, lo, hi) for c, bb in parts])
    spacing = np.array(grids[0].spacing_mm, dtype=np.float64)
    origin = np.array(grids[0].origin_mm, dtype=np.float64) + lo * spacing
    return stack, origin, spacing


def load_contours(case: str, roi: str, phase: int, cache: Path | str | None = None) -> list[np.ndarray]:
    """The raw RTSTRUCT polygons of one structure of one phase: [float64 (N, 3) in patient LPS mm, ...].

    Each polygon is closed (the last point joins the first), planar in z, and in file order. For "lungs"
    the right lung's polygons come first, then the left lung's.
    """
    roi = canonical_roi(roi)
    out: list[np.ndarray] = []
    with _npz(case, cache, "contours", phase) as z:
        for r in COMPOSITE_ROIS.get(roi, (roi,)):
            xyz, lens = z[f"{r}__xyz"], z[f"{r}__len"]
            out += [a.copy() for a in np.split(xyz, np.cumsum(lens)[:-1])]
    return out
