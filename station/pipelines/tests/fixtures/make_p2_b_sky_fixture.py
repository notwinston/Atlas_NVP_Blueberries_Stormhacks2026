"""Writes the committed fixtures of the engine-free needle-sky core (P2-B10/B11), into tests/fixtures/p2_b_sky/.
Synthetic numbers; no patient data. Run from station/pipelines/ with PYTHONPATH=. :

    python tests/fixtures/make_p2_b_sky_fixture.py [output directory]

Writes
    sky/risk_p0.r8 ... risk_p9.r8, sky/robust.r8, sky/sky.json
        A tiny synthetic sky in the format of docs/case_contract.md section 9, but 32 x 16 pixels and HEALPix nside 4
        instead of 512 x 256 and nside 64 (the reader takes the sizes from sky.json). 11 files of 512 bytes and a
        sky.json of a few kB. Piecewise constant on HEALPix pixels like the real maps. The 20 "checks" are computed here
        by a separate path (atlas_pipelines.sky, numpy), not by the C# code under test.
    healpix_ref_random_1000.txt
        1,000 seeded random directions (z uniform, phi uniform) and their astropy-healpix ring pixel at nside 64:
        a line "theta phi index". Committed so the C# test runs without Python.
    healpix_ref_edge.txt
        Directions on the seams of the pixelisation (poles, equator, z = +-2/3, phi = 0 and 2 pi) and their astropy-healpix pixel.

tests/test_p2_b_sky_core.py asserts that the committed files equal what this script writes.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import numpy as np

from atlas_pipelines import contract, sky

WIDTH, HEIGHT, NSIDE = 32, 16, 4
PHASES = 10
SEED_MAPS = 20260901
SEED_REF = 20260902
N_REF = 1000


def healpix_reference_lines(seed: int, n: int, nside: int = 64) -> list[str]:
    """Lines "theta phi index" of ``n`` seeded random directions with the astropy-healpix ring index."""
    rng = np.random.default_rng(seed)
    z = rng.uniform(-1.0, 1.0, n)
    phi = rng.uniform(0.0, 2.0 * math.pi, n)
    theta = np.arccos(z)
    return _lines(theta, phi, nside)


def _lines(theta: np.ndarray, phi: np.ndarray, nside: int) -> list[str]:
    import astropy.units as u
    from astropy_healpix import lonlat_to_healpix

    lat = (0.5 * math.pi - theta) * u.rad
    idx = lonlat_to_healpix(phi * u.rad, lat, nside, order="ring")
    return [f"{float(t)!r} {float(p)!r} {int(i)}" for t, p, i in zip(theta, phi, idx)]


def healpix_edge_lines(nside: int = 64) -> list[str]:
    pi = math.pi
    # Not listed: theta = pi/2 and theta = acos(+-2/3) themselves. They sit exactly on a ring boundary (z = 0, |z| = 2/3), where
    # one ulp of z decides the ring (astropy-healpix computes z = sin(pi/2 - theta), this code cos(theta)), so the two disagree
    # by one ring there. No baked map depends on it: the maps are read by equirectangular pixel, not by HEALPix index.
    thetas = [0.0, 1e-9, pi / 2.0 + 1e-9, pi / 2.0 - 1e-9, pi - 1e-9, pi,
              math.acos(2.0 / 3.0) + 1e-7, math.acos(2.0 / 3.0) - 1e-7, math.acos(-2.0 / 3.0) + 1e-7,
              math.acos(-2.0 / 3.0) - 1e-7]
    phis = [0.0, 1e-12, 0.25 * pi, 0.5 * pi, pi, 1.5 * pi, 2.0 * pi - 1e-12, 2.0 * pi * (1 - 2 ** -53)]
    th, ph = np.meshgrid(np.array(thetas), np.array(phis), indexing="ij")
    return _lines(th.ravel(), ph.ravel(), nside)


def _healpix_values(rng: np.random.Generator) -> np.ndarray:
    """int values [10, npix]: a smooth-ish base per pixel plus a per-phase wobble; threshold and saturation cases included."""
    npix = 12 * NSIDE * NSIDE
    base = rng.integers(25, 110, npix)
    vals = np.clip(base[None, :] + rng.integers(-12, 13, (PHASES, npix)), 0, 255)
    vals[:, 5] = 255  # saturated in every phase
    vals[:, 6] = 0    # inside an organ in every phase
    vals[:, 7] = 50   # exactly the threshold byte in every phase: robust
    vals[3, 7] = 49   # one phase below it: not robust
    vals[:, 8] = 50
    return vals.astype(np.uint8)


def sky_files() -> dict[str, bytes]:
    rng = np.random.default_rng(SEED_MAPS)
    hp = _healpix_values(rng)
    index = sky.equirect_healpix_index(NSIDE, WIDTH, HEIGHT)
    risk = np.stack([hp[k][index] for k in range(PHASES)]).astype(np.uint8)  # [10, H, W]
    robust = np.where((risk >= 50).all(axis=0), 255, 0).astype(np.uint8)
    assert 0 < int((robust == 255).sum()) < robust.size

    # 20 test pixels: a Fibonacci spiral snapped to pixels, plus the seam and the pole rows, plus both threshold cases
    pts = []
    golden = math.pi * (3.0 - math.sqrt(5.0))
    for i in range(14):
        z = 1.0 - (2.0 * i + 1.0) / 14
        c, r = sky.angles_to_pixel(math.acos(z), i * golden, WIDTH, HEIGHT)
        pts.append((int(c), int(r)))
    pts += [(0, 0), (WIDTH - 1, 0), (0, HEIGHT - 1), (WIDTH - 1, HEIGHT - 1), (0, HEIGHT // 2), (WIDTH - 1, HEIGHT // 2)]
    assert len(set(pts)) == 20, pts
    checks = []
    for c, r in pts:
        theta, phi = sky.pixel_angles(c, r, WIDTH, HEIGHT)
        d = sky.ang2vec(theta, phi)
        v = [int(risk[k, r, c]) for k in range(PHASES)]
        mm = [round(0.1 * x, 6) for x in v]
        checks.append({
            "col": c, "row": r, "dir": [round(float(x), 6) for x in d],
            "healpix": int(sky.ang2pix_ring(NSIDE, theta, phi)),
            "value": v, "clearance_mm": mm, "min_clearance_mm": min(mm), "robust": bool(robust[r, c] == 255),
        })
    assert any(k["robust"] for k in checks) and any(not k["robust"] for k in checks)

    wts = np.sin(math.pi * (np.arange(HEIGHT) + 0.5) / HEIGHT)[:, None] * np.ones((1, WIDTH))
    share = float((wts * (robust == 255)).sum() / wts.sum())
    doc = {
        "schema": "atlas.sky/1", "id": "synthetic",
        "label": "Synthetic test fixture. No patient data.",
        "note": "A 32 x 16 synthetic sky for the engine-free tests of the needle-sky core. Not a clearance map of anything.",
        "width": WIDTH, "height": HEIGHT, "phases": PHASES,
        "nside": NSIDE, "npix": 12 * NSIDE * NSIDE, "ordering": "ring",
        "files": {"risk": [f"risk_p{k}.r8" for k in range(PHASES)], "robust": "robust.r8"},
        "frame": "patient LPS mm: +x to the patient's left, +y posterior, +z superior; a direction is a unit vector pointing from the target outwards",
        "encoding": {"type": "u8", "mm_per_count": 0.1, "clearance_max_mm": 25.5, "rounding": "floor",
                     "formula": "value = min(255, floor(clearance_mm / mm_per_count)); clearance_mm = value * mm_per_count",
                     "saturation": "255 means clearance_max_mm or more", "robust": "255 where robust, 0 elsewhere"},
        "threshold_mm": 5.0, "threshold_value": 50, "threshold_label": "Teaching threshold",
        "target_mm": [0.0, 0.0, 0.0],
        "stats": {"robust_share": round(share, 6), "robust_pixels": int((robust == 255).sum())},
        "checks": checks,
    }
    out = {f"risk_p{k}.r8": risk[k].tobytes() for k in range(PHASES)}
    out["robust.r8"] = robust.tobytes()
    out["sky.json"] = contract.dumps_canonical(doc)
    return out


def write(out_dir: Path) -> dict[str, bytes]:
    """Writes the fixtures under ``out_dir``; returns {relative path: bytes}."""
    files = {f"sky/{k}": v for k, v in sky_files().items()}
    files["healpix_ref_random_1000.txt"] = (
        "# theta phi index: 1000 seeded random directions, astropy-healpix ring index, nside 64\n"
        + "\n".join(healpix_reference_lines(SEED_REF, N_REF)) + "\n").encode("ascii")
    files["healpix_ref_edge.txt"] = (
        "# theta phi index: directions on the seams of the pixelisation, astropy-healpix ring index, nside 64\n"
        + "\n".join(healpix_edge_lines()) + "\n").encode("ascii")
    for rel, data in files.items():
        p = Path(out_dir) / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(data)
    return files


if __name__ == "__main__":
    target = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent / "p2_b_sky"
    written = write(target)
    print(f"wrote {len(written)} files, {sum(len(v) for v in written.values())} bytes, to {target}")
