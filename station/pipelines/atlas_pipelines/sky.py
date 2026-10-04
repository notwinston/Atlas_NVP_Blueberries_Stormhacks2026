"""The needle sky of the real case: a geometric clearance map over the sphere of directions.

Master Plan 10.2.4 step 8, work package P2-D08; file formats in docs/case_contract.md section 9.
This is a geometric map for education. It makes no statement about any procedure.

What is computed
----------------
For each of the 10 breathing phases:

1. **Distance field.** The masks of heart, oesophagus, spinal cord and trachea on the CT grid are
   united, and the Euclidean distance transform gives, at every voxel centre, the distance in mm
   to the nearest voxel centre of those four structures (0 inside them). Between voxel centres
   the field is read by trilinear interpolation.
2. **Rays.** One ray per HEALPix pixel of nside 64 (49,152 directions, ring ordering, pixel
   centres from astropy-healpix), starting at the target point (the ``argmax_rho_mm`` of
   table.json) and running outwards. A ray ends where it leaves the box of CT voxel centres or at
   ``RAY_MAX_MM``, whichever comes first. It is sampled every ``RAY_STEP_MM`` from the target and
   once more at its end point.
3. **Clearance.** The clearance of a direction is the minimum of the distance field over the
   samples of its ray. The target itself is a sample, so no direction has a larger clearance than
   the target point has.

``robust`` is the AND over the 10 phases of "clearance >= 5 mm" (the teaching threshold).

The maps are baked to an equirectangular image of 512 x 256: each image pixel takes the value of
the HEALPix pixel that contains the direction of its centre (nearest pixel, no blending), found
with :func:`ang2pix_ring`, which is written out here because the phone re-implements it.

Encoding of a ``risk_p<k>.r8`` byte: ``value = min(255, floor(clearance_mm / 0.1))``, so
``clearance_mm = 0.1 * value`` is a lower bound within 0.1 mm, and 255 means "25.5 mm or more".
The threshold of 5 mm is the byte value 50, and rounding down makes "value >= 50" identical to
"clearance >= 5 mm".

Frames and mappings
-------------------
Directions are unit vectors in the patient frame (LPS: +x left, +y posterior, +z superior),
pointing from the target outwards. Spherical angles: ``theta`` is the polar angle from +z,
``phi`` the azimuth from +x towards +y; direction = (sin theta cos phi, sin theta sin phi, cos theta).

Equirectangular pixel (col, row), row 0 at +z::

    theta = pi * (row + 0.5) / 256        phi = 2 pi * (col + 0.5) / 512        offset = col + 512 * row

HEALPix: ``theta`` is the HEALPix colatitude and ``phi`` its longitude (astropy-healpix:
``lon = phi``, ``lat = pi/2 - theta``).
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field
from pathlib import Path
from typing import Sequence

import numpy as np

from . import contract, labels
from . import masks as masklib

VERSION = "sky/1"  #: bump when this module changes what it writes (the sky step puts it in its cache key)

# ---- the plan's numbers ------------------------------------------------------------------------
NSIDE = 64
NPIX = 12 * NSIDE * NSIDE  # 49,152
WIDTH = 512
HEIGHT = 256
THRESHOLD_MM = 5.0
PHASES = 10
ORGANS = ("heart", "esophagus", "cord", "trachea")

# ---- choices of this step (recorded in sky.json) -----------------------------------------------
ORDERING = "ring"
MM_PER_COUNT = 0.1
CLEARANCE_MAX_MM = 25.5  # 255 * MM_PER_COUNT
THRESHOLD_VALUE = 50  # THRESHOLD_MM / MM_PER_COUNT
RAY_STEP_MM = 1.0
RAY_MAX_MM = 300.0
N_CHECKS = 20

SCHEMA = "atlas.sky/1"
RISK_FILES = tuple(f"risk_p{k}.r8" for k in range(PHASES))
ROBUST_FILE = "robust.r8"
JSON_FILE = "sky.json"
FILES = RISK_FILES + (ROBUST_FILE, JSON_FILE)

TARGET_FROM_TABLE = "table.json argmax_rho_mm"
TARGET_FALLBACK = (
    "fallback: table.json was absent, so the point was computed from the raw target lesion masks on the CT grid "
    "(rho = share of the 10 phases in which a voxel is inside; the voxel of highest rho nearest to the centroid of "
    "all such voxels)"
)

NOTE = (
    "Geometric clearance map for education: for each direction from the target point, the smallest distance between "
    "a straight ray and the contoured heart, oesophagus, spinal cord and trachea. Other structures are not included. "
    "The 5 mm value is a teaching threshold."
)


class SkyError(RuntimeError):
    pass


# =================================================================================================
# HEALPix, ring ordering (own implementation; the phone re-implements ang2pix_ring)
# =================================================================================================
def npix_of(nside: int) -> int:
    return 12 * nside * nside


def ang2pix_ring(nside: int, theta, phi) -> np.ndarray:
    """HEALPix pixel index in ring ordering of the direction (theta, phi), in radians.

    ``theta`` in [0, pi] from the north pole (+z), ``phi`` any real azimuth. Vectorised; int64.

    The formula (Gorski et al. 2005), with z = cos theta, za = |z|, tt = (phi mod 2 pi) / (pi / 2)
    in [0, 4):

    * equatorial belt, za <= 2/3::

          jp = floor(nside * (0.5 + tt - 0.75 z))        index of the ascending edge line
          jm = floor(nside * (0.5 + tt + 0.75 z))        index of the descending edge line
          ir = nside + 1 + jp - jm                       ring in the belt, 1 .. 2 nside + 1
          kshift = 1 - (ir & 1)
          ip = ((jp + jm - nside + kshift + 1) div 2) mod (4 nside)
          pix = 2 nside (nside - 1) + (ir - 1) * 4 nside + ip

    * polar caps, za > 2/3::

          tp = tt - floor(tt)
          tmp = nside * sin(theta) / sqrt((1 + za) / 3)  [= nside * sqrt(3 (1 - za))]
          jp = floor(tp * tmp);  jm = floor((1 - tp) * tmp)
          ir = jp + jm + 1                               ring counted from the nearer pole
          ip = floor(tt * ir) mod (4 ir)
          pix = 2 ir (ir - 1) + ip                       north (z > 0)
          pix = 12 nside^2 - 2 ir (ir + 1) + ip          south
    """
    nside = int(nside)
    theta = np.asarray(theta, dtype=np.float64)
    phi = np.asarray(phi, dtype=np.float64)
    theta, phi = np.broadcast_arrays(theta, phi)
    if theta.size and (theta.min() < 0.0 or theta.max() > math.pi):
        raise ValueError("theta must lie in [0, pi]")
    z = np.cos(theta)
    za = np.abs(z)
    sth = np.sin(theta)
    tt = np.mod(phi, 2.0 * math.pi) / (0.5 * math.pi)
    tt = np.where(tt >= 4.0, 0.0, tt)  # mod can return 2 pi itself for a tiny negative phi

    # equatorial belt
    t1 = nside * (0.5 + tt)
    t2 = nside * z * 0.75
    jp = np.floor(t1 - t2).astype(np.int64)
    jm = np.floor(t1 + t2).astype(np.int64)
    ir = nside + 1 + jp - jm
    kshift = 1 - (ir & 1)
    ip = np.mod((jp + jm - nside + kshift + 1) // 2, 4 * nside)
    pix_eq = 2 * nside * (nside - 1) + (ir - 1) * 4 * nside + ip

    # polar caps
    tp = tt - np.floor(tt)
    tmp = nside * sth / np.sqrt((1.0 + za) / 3.0)
    jp = np.floor(tp * tmp).astype(np.int64)
    jm = np.floor((1.0 - tp) * tmp).astype(np.int64)
    irp = jp + jm + 1
    ipp = np.mod(np.floor(tt * irp).astype(np.int64), 4 * irp)
    pix_cap = np.where(z > 0, 2 * irp * (irp - 1) + ipp, 12 * nside * nside - 2 * irp * (irp + 1) + ipp)

    return np.where(za <= 2.0 / 3.0, pix_eq, pix_cap).astype(np.int64)


def pix2ang_ring(nside: int, ipix) -> tuple[np.ndarray, np.ndarray]:
    """Centre (theta, phi) in radians of HEALPix pixels in ring ordering. Vectorised."""
    nside = int(nside)
    p = np.asarray(ipix, dtype=np.int64)
    npix = 12 * nside * nside
    if p.size and (p.min() < 0 or p.max() >= npix):
        raise ValueError("pixel index out of range")
    ncap = 2 * nside * (nside - 1)

    def isqrt(v):
        r = np.floor(np.sqrt(v.astype(np.float64))).astype(np.int64)
        r = np.where(r * r > v, r - 1, r)
        return np.where((r + 1) * (r + 1) <= v, r + 1, r)

    # north cap
    pn = np.clip(p, 0, None)
    i_n = np.maximum((1 + isqrt(1 + 2 * pn)) // 2, 1)
    j_n = pn - 2 * i_n * (i_n - 1)
    z_n = 1.0 - (i_n * i_n) / (3.0 * nside * nside)
    phi_n = (j_n + 0.5) * math.pi / (2.0 * i_n)
    # belt
    pe = p - ncap
    i_e = pe // (4 * nside) + nside
    j_e = np.mod(pe, 4 * nside)
    fodd = np.where(((i_e + nside) & 1) == 1, 1.0, 0.5)
    z_e = (2 * nside - i_e) * 2.0 / (3.0 * nside)
    phi_e = (j_e + 1 - fodd) * math.pi / (2.0 * nside)
    # south cap
    ps = np.clip(npix - p, 1, None)
    i_s = np.maximum((1 + isqrt(2 * ps - 1)) // 2, 1)
    j_s = 4 * i_s - (ps - 2 * i_s * (i_s - 1))
    z_s = -1.0 + (i_s * i_s) / (3.0 * nside * nside)
    phi_s = (j_s + 0.5) * math.pi / (2.0 * i_s)

    north = p < ncap
    south = p >= npix - ncap
    z = np.where(north, z_n, np.where(south, z_s, z_e))
    phi = np.where(north, phi_n, np.where(south, phi_s, phi_e))
    return np.arccos(np.clip(z, -1.0, 1.0)), phi


def ang2vec(theta, phi) -> np.ndarray:
    """Unit vectors (..., 3) of spherical angles: (sin theta cos phi, sin theta sin phi, cos theta)."""
    theta = np.asarray(theta, dtype=np.float64)
    phi = np.asarray(phi, dtype=np.float64)
    st = np.sin(theta)
    return np.stack([st * np.cos(phi), st * np.sin(phi), np.cos(theta) + 0.0 * phi], axis=-1)


def vec2ang(v) -> tuple[np.ndarray, np.ndarray]:
    """(theta, phi) of direction vectors (..., 3); phi in [0, 2 pi)."""
    v = np.asarray(v, dtype=np.float64)
    theta = np.arctan2(np.hypot(v[..., 0], v[..., 1]), v[..., 2])
    phi = np.mod(np.arctan2(v[..., 1], v[..., 0]), 2.0 * math.pi)
    return theta, phi


def healpix_directions(nside: int = NSIDE) -> np.ndarray:
    """Unit vectors (npix, 3) of the HEALPix pixel centres in ring ordering, from astropy-healpix."""
    import astropy.units as u
    from astropy_healpix import healpix_to_lonlat

    lon, lat = healpix_to_lonlat(np.arange(npix_of(nside)), nside, order="ring")
    return ang2vec(0.5 * math.pi - lat.to_value(u.rad), lon.to_value(u.rad))


# =================================================================================================
# Equirectangular image
# =================================================================================================
def pixel_angles(col, row, width: int = WIDTH, height: int = HEIGHT) -> tuple[np.ndarray, np.ndarray]:
    """(theta, phi) of the centre of image pixel (col, row); row 0 is at +z."""
    theta = math.pi * (np.asarray(row, dtype=np.float64) + 0.5) / height
    phi = 2.0 * math.pi * (np.asarray(col, dtype=np.float64) + 0.5) / width
    return theta, phi


def angles_to_pixel(theta, phi, width: int = WIDTH, height: int = HEIGHT) -> tuple[np.ndarray, np.ndarray]:
    """The image pixel (col, row) that contains the direction (theta, phi)."""
    row = np.clip(np.floor(np.asarray(theta, dtype=np.float64) / math.pi * height), 0, height - 1).astype(np.int64)
    col = np.mod(np.floor(np.mod(np.asarray(phi, dtype=np.float64), 2.0 * math.pi) / (2.0 * math.pi) * width), width)
    return col.astype(np.int64), row


def equirect_healpix_index(nside: int = NSIDE, width: int = WIDTH, height: int = HEIGHT) -> np.ndarray:
    """int64 [row, col]: the HEALPix ring pixel that contains the centre of each image pixel."""
    row, col = np.meshgrid(np.arange(height), np.arange(width), indexing="ij")
    theta, phi = pixel_angles(col, row, width, height)
    return ang2pix_ring(nside, theta, phi)


def bake(healpix_values: np.ndarray, index: np.ndarray | None = None) -> np.ndarray:
    """A HEALPix map (npix,) -> the equirectangular image [row, col], nearest pixel."""
    index = equirect_healpix_index() if index is None else index
    return np.asarray(healpix_values)[index]


# =================================================================================================
# Encoding
# =================================================================================================
def encode_clearance(clearance_mm: np.ndarray) -> np.ndarray:
    """mm -> u8: min(255, floor(clearance / 0.1)). Rounds down, so the byte never overstates."""
    c = np.asarray(clearance_mm, dtype=np.float64)
    if c.size and (not np.isfinite(c).all() or c.min() < 0.0):
        raise SkyError("clearance must be finite and not negative")
    return np.minimum(np.floor(c / MM_PER_COUNT), 255.0).astype(np.uint8)


def decode_clearance(values: np.ndarray) -> np.ndarray:
    """u8 -> mm (a lower bound within 0.1 mm; 255 means 25.5 mm or more)."""
    return np.asarray(values, dtype=np.float64) * MM_PER_COUNT


# =================================================================================================
# Target point
# =================================================================================================
def target_from_masks(case: str, cache: Path | str | None) -> np.ndarray:
    """Fallback target: the argmax of rho on the raw CT-grid masks (see TARGET_FALLBACK)."""
    stack, origin, spacing = masklib.load_mask_stack(case, "tumor", cache)
    count = stack.sum(axis=0)
    top = np.argwhere(count == count.max())  # C order
    d2 = (((top - top.mean(axis=0)) * spacing) ** 2).sum(axis=1)
    return origin + top[int(np.argmin(d2))] * spacing


def choose_target(case: str, case_dir: Path, cache: Path | str | None) -> tuple[np.ndarray, str]:
    """(target_mm, source). The table's argmax-rho point; the documented fallback when it is absent."""
    table = Path(case_dir) / "table.json"
    if table.is_file():
        return np.array(contract.read_table(table).argmax_rho_mm, dtype=np.float64), TARGET_FROM_TABLE
    return target_from_masks(case, cache), TARGET_FALLBACK


# =================================================================================================
# Distance field and rays
# =================================================================================================
def organ_mask(case: str, phase: int, cache: Path | str | None) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Union of the four structures on the full CT grid: (bool [ix, iy, iz], origin_mm, spacing_mm)."""
    union = None
    origin = spacing = None
    for roi in ORGANS:
        m, origin, spacing = masklib.load_mask(case, roi, phase, cache, crop=False)
        if not m.any():
            raise SkyError(f"phase {phase}: the mask of {roi} is empty")
        union = m.copy() if union is None else (union | m)
    return union, origin, spacing


def distance_field(mask: np.ndarray, spacing_mm: Sequence[float]) -> np.ndarray:
    """float32 distance in mm from each voxel centre to the nearest voxel centre of ``mask`` (0 inside)."""
    from scipy import ndimage as ndi

    return ndi.distance_transform_edt(~mask, sampling=[float(s) for s in spacing_mm]).astype(np.float32)


def ray_lengths(target_mm: np.ndarray, dirs: np.ndarray, lo_mm: np.ndarray, hi_mm: np.ndarray,
                max_mm: float = RAY_MAX_MM) -> np.ndarray:
    """Length in mm of each ray: to the face of the box [lo, hi] it leaves through, at most ``max_mm``."""
    with np.errstate(divide="ignore", invalid="ignore"):
        t = np.where(dirs > 0, (hi_mm - target_mm) / dirs, np.where(dirs < 0, (lo_mm - target_mm) / dirs, np.inf))
    return np.minimum(t.min(axis=1), max_mm)


def ray_clearance(dist: np.ndarray, origin_mm: np.ndarray, spacing_mm: np.ndarray, target_mm: np.ndarray,
                  dirs: np.ndarray, *, step_mm: float = RAY_STEP_MM, max_mm: float = RAY_MAX_MM,
                  chunk: int = 4096) -> np.ndarray:
    """Minimum of the distance field along each ray, float64 (N,).

    Samples lie at t = 0, step, 2 step, ... and at the ray's end; trilinear interpolation.
    """
    from scipy import ndimage as ndi

    origin_mm = np.asarray(origin_mm, dtype=np.float64)
    spacing_mm = np.asarray(spacing_mm, dtype=np.float64)
    target_mm = np.asarray(target_mm, dtype=np.float64)
    hi = origin_mm + (np.array(dist.shape) - 1) * spacing_mm
    if (target_mm < origin_mm).any() or (target_mm > hi).any():
        raise SkyError("the target point lies outside the CT volume")
    t_end = ray_lengths(target_mm, dirs, origin_mm, hi, max_mm)
    ts = np.arange(0.0, max_mm + 0.5 * step_mm, step_mm)
    start = (target_mm - origin_mm) / spacing_mm
    top = np.array(dist.shape, dtype=np.float64) - 1.0
    out = np.empty(len(dirs), dtype=np.float64)
    for a in range(0, len(dirs), chunk):
        d = dirs[a:a + chunk]
        t = np.minimum(ts[None, :], t_end[a:a + chunk, None])  # samples past the end collapse onto the end
        coords = [np.clip(start[ax] + t * (d[:, ax, None] / spacing_mm[ax]), 0.0, top[ax]) for ax in range(3)]
        v = ndi.map_coordinates(dist, coords, order=1, mode="nearest", output=np.float64)
        out[a:a + chunk] = v.min(axis=1)
    return np.maximum(out, 0.0)


# =================================================================================================
# Build and write
# =================================================================================================
@dataclass
class SkyResult:
    case: str
    target_mm: np.ndarray
    target_source: str
    grid_origin_mm: np.ndarray
    grid_spacing_mm: np.ndarray
    grid_shape: tuple[int, int, int]
    clearance_mm: np.ndarray  #: float64 [10, npix], HEALPix ring
    risk: np.ndarray  #: uint8 [10, 256, 512]
    robust: np.ndarray  #: uint8 [256, 512], 255 or 0
    robust_healpix: np.ndarray  #: bool [npix]
    target_clearance_mm: list[float] = field(default_factory=list)  #: distance field at the target, per phase


def check_points(n: int = N_CHECKS) -> list[tuple[int, int]]:
    """``n`` image pixels (col, row) spread over the sphere (a Fibonacci spiral, snapped to pixels)."""
    golden = math.pi * (3.0 - math.sqrt(5.0))
    pts = []
    for i in range(n):
        z = 1.0 - (2.0 * i + 1.0) / n
        col, row = angles_to_pixel(math.acos(z), i * golden)
        pts.append((int(col), int(row)))
    return pts


def build(case: str, cache: Path | str | None, target_mm: np.ndarray, target_source: str, log=None) -> SkyResult:
    """Compute the ten clearance maps and the robust map. Deterministic."""
    dirs = healpix_directions(NSIDE)
    theta, phi = vec2ang(dirs)
    if not np.array_equal(ang2pix_ring(NSIDE, theta, phi), np.arange(NPIX)):
        raise SkyError("ang2pix_ring does not return each astropy-healpix pixel centre to its own pixel")
    index = equirect_healpix_index()
    clearance = np.empty((PHASES, NPIX), dtype=np.float64)
    at_target = []
    origin = spacing = shape = None
    for k in range(PHASES):
        mask, o, s = organ_mask(case, k, cache)
        if origin is not None and (not np.array_equal(o, origin) or not np.array_equal(s, spacing) or mask.shape != shape):
            raise SkyError("the phases do not share one CT grid")
        origin, spacing, shape = o, s, mask.shape
        dist = distance_field(mask, spacing)
        del mask
        clearance[k] = ray_clearance(dist, origin, spacing, target_mm, dirs)
        at_target.append(float(ray_clearance(dist, origin, spacing, target_mm, dirs[:1], max_mm=0.0)[0]))
        del dist
        if log:
            log(f"  phase {k}: clearance {clearance[k].min():.2f} .. {clearance[k].max():.2f} mm, "
                f"{100.0 * np.mean(clearance[k] >= THRESHOLD_MM):.1f} % of directions >= {THRESHOLD_MM:g} mm")
    counts = np.stack([encode_clearance(c) for c in clearance])  # [10, npix]
    robust_hp = (clearance >= THRESHOLD_MM).all(axis=0)
    if not np.array_equal(robust_hp, (counts >= THRESHOLD_VALUE).all(axis=0)):
        raise SkyError("the 5 mm threshold on the clearances and the value 50 on the bytes disagree")
    risk = np.stack([bake(c, index) for c in counts]).astype(np.uint8)
    robust = np.where(bake(robust_hp, index), 255, 0).astype(np.uint8)
    return SkyResult(case=case, target_mm=np.asarray(target_mm, dtype=np.float64), target_source=target_source,
                     grid_origin_mm=origin, grid_spacing_mm=spacing, grid_shape=tuple(int(n) for n in shape),
                     clearance_mm=clearance, risk=risk, robust=robust, robust_healpix=robust_hp,
                     target_clearance_mm=at_target)


def solid_angle_share(image_mask: np.ndarray) -> float:
    """Share of the sphere covered by the True pixels of an equirectangular mask [row, col]."""
    h = image_mask.shape[0]
    w = np.sin(math.pi * (np.arange(h) + 0.5) / h)[:, None] * np.ones((1, image_mask.shape[1]))
    return float((w * image_mask).sum() / w.sum())


def sky_json(res: SkyResult) -> dict:
    """The object written to sky/sky.json (schema atlas.sky/1)."""
    hi = res.grid_origin_mm + (np.array(res.grid_shape) - 1) * res.grid_spacing_mm
    index = equirect_healpix_index()
    checks = []
    for col, row in check_points():
        theta, phi = pixel_angles(col, row)
        d = ang2vec(theta, phi)
        checks.append({
            "col": col,
            "row": row,
            "dir": [float(v) for v in d],
            "healpix": int(index[row, col]),
            "value": [int(res.risk[k, row, col]) for k in range(PHASES)],
            "clearance_mm": [float(decode_clearance(res.risk[k, row, col])) for k in range(PHASES)],
            "min_clearance_mm": float(decode_clearance(res.risk[:, row, col].min())),
            "robust": bool(res.robust[row, col] == 255),
        })
    return {
        "schema": SCHEMA,
        "id": res.case,
        "label": labels.label_for(labels.KIND_REAL, "sky/" + JSON_FILE),
        "note": NOTE,
        "width": WIDTH,
        "height": HEIGHT,
        "nside": NSIDE,
        "npix": NPIX,
        "ordering": ORDERING,
        "phases": PHASES,
        "files": {"risk": list(RISK_FILES), "robust": ROBUST_FILE},
        "frame": "patient LPS mm: +x to the patient's left, +y posterior, +z superior; a direction is a unit vector "
                 "pointing from the target outwards",
        "projection": {
            "type": "equirectangular",
            "offset": "col + 512 * row",
            "theta": "pi * (row + 0.5) / 256, polar angle from +z; row 0 is at +z (superior)",
            "phi": "2 * pi * (col + 0.5) / 512, azimuth from +x towards +y",
            "direction": "(sin(theta) * cos(phi), sin(theta) * sin(phi), cos(theta))",
            "sampling": "each image pixel holds the value of the HEALPix pixel that contains the direction of its "
                        "centre (nearest pixel, no blending)",
        },
        "healpix": {
            "nside": NSIDE,
            "ordering": ORDERING,
            "theta": "colatitude from +z",
            "phi": "longitude from +x towards +y",
        },
        "encoding": {
            "type": "u8",
            "mm_per_count": float(MM_PER_COUNT),
            "clearance_max_mm": float(CLEARANCE_MAX_MM),
            "rounding": "floor",
            "formula": "value = min(255, floor(clearance_mm / mm_per_count)); clearance_mm = value * mm_per_count",
            "saturation": "255 means clearance_max_mm or more",
            "robust": "255 where robust, 0 elsewhere",
        },
        "threshold_mm": float(THRESHOLD_MM),
        "threshold_value": THRESHOLD_VALUE,
        "threshold_label": labels.LABEL_TEACHING_THRESHOLD,
        "robust_rule": "AND over the 10 phases of (clearance >= threshold_mm), equal to value >= threshold_value",
        "target_mm": [float(v) for v in res.target_mm],
        "target_source": res.target_source,
        "target_fallback_used": res.target_source != TARGET_FROM_TABLE,
        "organs": list(ORGANS),
        "distance_field": {
            "method": "Euclidean distance transform of the union of the organ masks on the CT grid, per phase: the "
                      "distance from a voxel centre to the nearest organ voxel centre, 0 inside; trilinear "
                      "interpolation between voxel centres",
            "grid_origin_mm": [float(v) for v in res.grid_origin_mm],
            "grid_spacing_mm": [float(v) for v in res.grid_spacing_mm],
            "grid_shape": [int(n) for n in res.grid_shape],
        },
        "ray": {
            "start": "the target point",
            "step_mm": float(RAY_STEP_MM),
            "max_length_mm": float(RAY_MAX_MM),
            "termination": "where the ray leaves the box of CT voxel centres, or at max_length_mm, whichever is first; "
                           "the end point is also sampled",
            "box_min_mm": [float(v) for v in res.grid_origin_mm],
            "box_max_mm": [float(v) for v in hi],
            "clearance": "minimum of the distance field over the samples of the ray, the target point included",
        },
        "stats": {
            "target_clearance_mm": [float(v) for v in res.target_clearance_mm],
            "max_clearance_mm": [float(v) for v in res.clearance_mm.max(axis=1)],
            "clear_share": [solid_angle_share(res.risk[k] >= THRESHOLD_VALUE) for k in range(PHASES)],
            "robust_share": solid_angle_share(res.robust == 255),
            "robust_pixels": int((res.robust == 255).sum()),
            "share_note": "share of the sphere (solid angle) with clearance >= threshold_mm, from the image pixels",
        },
        "checks": checks,
    }


def write_files(res: SkyResult, sky_dir: Path | str) -> list[Path]:
    """Write the ten risk maps, robust.r8 and sky.json into ``sky_dir``."""
    sky_dir = Path(sky_dir)
    sky_dir.mkdir(parents=True, exist_ok=True)
    out = []
    for k, name in enumerate(RISK_FILES):
        (sky_dir / name).write_bytes(np.ascontiguousarray(res.risk[k]).tobytes())
        out.append(sky_dir / name)
    (sky_dir / ROBUST_FILE).write_bytes(np.ascontiguousarray(res.robust).tobytes())
    out.append(sky_dir / ROBUST_FILE)
    contract.write_json(sky_dir / JSON_FILE, sky_json(res))
    out.append(sky_dir / JSON_FILE)
    return out


def read_map(path: Path | str) -> np.ndarray:
    """An .r8 map as uint8 [row, col]."""
    data = Path(path).read_bytes()
    if len(data) != WIDTH * HEIGHT:
        raise SkyError(f"{path}: {len(data)} bytes, expected {WIDTH * HEIGHT}")
    return np.frombuffer(data, dtype=np.uint8).reshape(HEIGHT, WIDTH)


def mask_inputs(case: str, cache: Path | str | None) -> list[Path]:
    """The mask files hashed as the step's input: the summary and the 10 phase masks."""
    d = masklib.mask_dir(case, cache)
    return [d / "summary.json"] + [d / f"phase_{k}.npz" for k in range(PHASES)]

