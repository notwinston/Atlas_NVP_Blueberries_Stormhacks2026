"""Read the cached TCIA 4D-Lung DICOM of the real case (written by ``atlas case fetch``).

Cache layout (``<cache>`` is ``station/pipelines/.cache`` unless overridden)::

    <cache>/tcia/101_HM10395/
        index.json                      series index, schema "atlas.tcia_index/1" (see steps/fetch.py)
        <SeriesInstanceUID>/*.dcm       the instances of one series (149 for a CT phase, 1 for an RTSTRUCT)
        <SeriesInstanceUID>/series.json name, size and sha256 of every instance of that series

Most steps do not need this module: they read the rasterised structures through
``atlas_pipelines.masks`` (``load_mask``, ``load_contours``). Use this module for the CT voxels
(``load_ct``) or for anything else in the DICOM headers.

Conventions
    Phases are indices 0..9; phase k is the "Gated, <10k>%" series (0 % = end of inhale, 50 % = about
    end of exhale for this patient). Coordinates are patient LPS in millimetres (x to the patient's
    left, y to posterior, z to superior), exactly as stored in the DICOM. Volumes are indexed
    ``[ix, iy, iz]`` and voxel (ix, iy, iz) has its CENTRE at ``origin_mm + (ix, iy, iz) * spacing_mm``.

Functions
    patient_dir(cache, case)            -> <cache>/tcia/<PatientID>
    read_index(cache, case)             -> the series index (dict); raises FileNotFoundError if fetch has not run
    ct_series_dir / rtstruct_series_dir (cache, case, phase) -> directory of that phase's series
    ct_grid(series_dir)                 -> Grid (origin_mm, spacing_mm, shape) from the CT headers
    load_ct(cache, case, phase)         -> (int16 HU array [ix, iy, iz], Grid)
    read_rtstruct(series_dir)           -> pydicom Dataset
    roi_contours(ds)                    -> {ROI name as in the file: [float64 (N, 3) polygon in LPS mm, ...]}
    referenced_series_uid(ds)           -> SeriesInstanceUID of the CT the structure set was drawn on
"""

from __future__ import annotations

import json
import re
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from . import paths

#: Case id -> TCIA identifiers (Master Plan 10.2.4 step 1; R2 C.2).
TCIA_CASES = {
    paths.REAL_CASE: {
        "collection": "4D-Lung",
        "patient_id": "101_HM10395",
        "study_uid": "1.3.6.1.4.1.14519.5.2.1.6834.5010.300895057088920888884721386157",
    }
}

INDEX_SCHEMA = "atlas.tcia_index/1"
PHASES = 10

_PHASE_RE = re.compile(r"Gated,\s*([\d.]+)\s*%")


class DicomError(RuntimeError):
    """The cached DICOM is missing, incomplete or not what the pipeline expects."""


def tcia_ids(case: str) -> dict:
    try:
        return TCIA_CASES[case]
    except KeyError:
        raise DicomError(f"case {case!r} has no TCIA data (only {sorted(TCIA_CASES)} are built from DICOM)") from None


def phase_from_description(description: str | None) -> int:
    """Phase index 0..9 from a SeriesDescription such as "P4^P101^S300^I00003, Gated, 0.0%A"."""
    m = _PHASE_RE.search(description or "")
    if not m:
        raise DicomError(f"no 'Gated, NN%' in series description {description!r}")
    pct = float(m.group(1))
    k = int(round(pct / 10.0))
    if abs(pct - 10.0 * k) > 1e-6 or not 0 <= k < PHASES:
        raise DicomError(f"unexpected gating phase {pct} % in {description!r}")
    return k


def check_phase(phase: int) -> int:
    if isinstance(phase, bool) or int(phase) != phase or not 0 <= int(phase) < PHASES:
        raise ValueError(f"phase must be an index 0..{PHASES - 1} (phase k is the {10}k % bin), got {phase!r}")
    return int(phase)


def patient_dir(cache: Path | str | None = None, case: str = paths.REAL_CASE) -> Path:
    base = Path(cache) if cache is not None else paths.default_cache_dir()
    return base / "tcia" / tcia_ids(case)["patient_id"]


def index_path(cache: Path | str | None = None, case: str = paths.REAL_CASE) -> Path:
    return patient_dir(cache, case) / "index.json"


def read_index(cache: Path | str | None = None, case: str = paths.REAL_CASE) -> dict:
    p = index_path(cache, case)
    if not p.is_file():
        raise FileNotFoundError(f"{p} is missing: run `atlas case fetch --case {case}` first")
    idx = json.loads(p.read_text(encoding="utf-8"))
    if idx.get("schema") != INDEX_SCHEMA:
        raise DicomError(f"{p}: schema {idx.get('schema')!r}, expected {INDEX_SCHEMA!r}")
    return idx


def _series_dir(cache, case: str, phase: int, key: str) -> Path:
    idx = read_index(cache, case)
    uid = idx["phases"][check_phase(phase)][key]
    return patient_dir(cache, case) / uid


def ct_series_dir(cache: Path | str | None, case: str, phase: int) -> Path:
    return _series_dir(cache, case, phase, "ct")


def rtstruct_series_dir(cache: Path | str | None, case: str, phase: int) -> Path:
    return _series_dir(cache, case, phase, "rtstruct")


def dcm_files(series_dir: Path | str) -> list[Path]:
    return sorted(p for p in Path(series_dir).iterdir() if p.suffix.lower() == ".dcm")


# ---------------------------------------------------------------------------------------------
# CT geometry
# ---------------------------------------------------------------------------------------------
@dataclass(frozen=True)
class Grid:
    """A voxel grid aligned with the patient LPS axes. Voxel (ix, iy, iz) is centred at origin + i * spacing."""

    origin_mm: tuple[float, float, float]
    spacing_mm: tuple[float, float, float]
    shape: tuple[int, int, int]

    def axes(self) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
        """Voxel-centre coordinates along x, y and z (mm)."""
        return tuple(self.origin_mm[a] + self.spacing_mm[a] * np.arange(self.shape[a]) for a in range(3))

    @property
    def voxel_mm3(self) -> float:
        return float(self.spacing_mm[0] * self.spacing_mm[1] * self.spacing_mm[2])

    def to_json(self) -> dict:
        return {"origin_mm": list(self.origin_mm), "spacing_mm": list(self.spacing_mm), "shape": list(self.shape)}

    @staticmethod
    def from_json(d: dict) -> "Grid":
        return Grid(tuple(float(v) for v in d["origin_mm"]), tuple(float(v) for v in d["spacing_mm"]),
                    tuple(int(v) for v in d["shape"]))


def _slice_headers(series_dir: Path | str, pixels: bool = False) -> list:
    import pydicom

    files = dcm_files(series_dir)
    if not files:
        raise DicomError(f"no .dcm files in {series_dir}")
    out = [pydicom.dcmread(f, stop_before_pixels=not pixels) for f in files]
    for ds in out:
        if ds.Modality != "CT":
            raise DicomError(f"{series_dir}: modality {ds.Modality}, expected CT")
    return out


def _grid_from_headers(headers: list, series_dir) -> tuple[Grid, list]:
    first = headers[0]
    iop = np.array(first.ImageOrientationPatient, dtype=float)
    if not np.allclose(iop, [1, 0, 0, 0, 1, 0], atol=1e-4):
        raise DicomError(f"{series_dir}: ImageOrientationPatient {iop.tolist()} is not axial LPS (1,0,0,0,1,0); "
                         "the pipeline assumes grid axes = patient axes")
    rows, cols = int(first.Rows), int(first.Columns)
    dy, dx = (float(v) for v in first.PixelSpacing)  # PixelSpacing = (between rows, between columns)
    x0, y0 = float(first.ImagePositionPatient[0]), float(first.ImagePositionPatient[1])
    for ds in headers:
        ipp = [float(v) for v in ds.ImagePositionPatient]
        same = (
            int(ds.Rows) == rows and int(ds.Columns) == cols
            and np.allclose([float(v) for v in ds.PixelSpacing], [dy, dx], atol=1e-6)
            and np.allclose(np.array(ds.ImageOrientationPatient, dtype=float), iop, atol=1e-6)
            and abs(ipp[0] - x0) < 1e-3 and abs(ipp[1] - y0) < 1e-3
        )
        if not same:
            raise DicomError(f"{series_dir}: slices do not share one in-plane geometry")
    headers = sorted(headers, key=lambda ds: float(ds.ImagePositionPatient[2]))
    z = np.array([float(ds.ImagePositionPatient[2]) for ds in headers])
    if len(z) < 2:
        raise DicomError(f"{series_dir}: a CT volume needs at least 2 slices")
    dzs = np.diff(z)
    dz = float(np.median(dzs))
    if dz <= 0 or np.abs(dzs - dz).max() > 1e-2:
        raise DicomError(f"{series_dir}: slice positions are not uniform (steps {dzs.min():.4f}..{dzs.max():.4f} mm)")
    grid = Grid((x0, y0, float(z[0])), (dx, dy, dz), (cols, rows, len(z)))
    return grid, headers


def ct_grid(series_dir: Path | str) -> Grid:
    """The voxel grid of a CT series from ImagePositionPatient, PixelSpacing, the slice positions and the orientation."""
    grid, _ = _grid_from_headers(_slice_headers(series_dir), series_dir)
    return grid


def ct_series_uid(series_dir: Path | str) -> str:
    import pydicom

    return str(pydicom.dcmread(dcm_files(series_dir)[0], stop_before_pixels=True).SeriesInstanceUID)


def load_ct(cache: Path | str | None = None, case: str = paths.REAL_CASE, phase: int = 0) -> tuple[np.ndarray, Grid]:
    """The CT of one phase in Hounsfield units, int16, indexed [ix, iy, iz] (about 78 MB), and its Grid."""
    d = ct_series_dir(cache, case, phase)
    grid, headers = _grid_from_headers(_slice_headers(d, pixels=True), d)
    vol = np.empty(grid.shape, dtype=np.int16)
    for k, ds in enumerate(headers):
        hu = ds.pixel_array.astype(np.float32) * float(getattr(ds, "RescaleSlope", 1.0)) + float(
            getattr(ds, "RescaleIntercept", 0.0))
        vol[:, :, k] = np.rint(hu).astype(np.int16).T  # pixel_array is [row (y), column (x)]
    return vol, grid


# ---------------------------------------------------------------------------------------------
# RTSTRUCT
# ---------------------------------------------------------------------------------------------
def read_rtstruct(series_dir: Path | str):
    import pydicom

    files = dcm_files(series_dir)
    if len(files) != 1:
        raise DicomError(f"{series_dir}: expected exactly 1 RTSTRUCT instance, found {len(files)}")
    ds = pydicom.dcmread(files[0])
    if ds.Modality != "RTSTRUCT":
        raise DicomError(f"{files[0]}: modality {ds.Modality}, expected RTSTRUCT")
    return ds


def referenced_series_uid(ds) -> str:
    """SeriesInstanceUID of the image series the structure set references (exactly one)."""
    uids = set()
    for fr in getattr(ds, "ReferencedFrameOfReferenceSequence", []):
        for st in getattr(fr, "RTReferencedStudySequence", []):
            for se in getattr(st, "RTReferencedSeriesSequence", []):
                uids.add(str(se.SeriesInstanceUID))
    if len(uids) != 1:
        raise DicomError(f"RTSTRUCT references {len(uids)} image series, expected 1: {sorted(uids)}")
    return uids.pop()


def roi_contours(ds) -> dict[str, list[np.ndarray]]:
    """Every ROI of a structure set: {ROIName: [polygon (N, 3) float64 in patient LPS mm, ...]} in file order.

    Only CLOSED_PLANAR contours are returned; an ROI without any has an empty list.
    """
    names = {int(r.ROINumber): str(r.ROIName) for r in ds.StructureSetROISequence}
    out: dict[str, list[np.ndarray]] = {n: [] for n in names.values()}
    for rc in getattr(ds, "ROIContourSequence", []):
        name = names.get(int(rc.ReferencedROINumber))
        if name is None:
            continue
        for c in getattr(rc, "ContourSequence", []):
            if str(getattr(c, "ContourGeometricType", "CLOSED_PLANAR")) != "CLOSED_PLANAR":
                continue
            pts = np.array(c.ContourData, dtype=np.float64).reshape(-1, 3)
            if len(pts) >= 3:
                out[name].append(pts)
    return out
