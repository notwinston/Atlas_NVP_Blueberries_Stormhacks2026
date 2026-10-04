"""P2-D08 `atlas case sky`: the needle sky of the real case.

Acceptance (Master Plan 10.2.8, row P2-D08), both exact:

    1. ``robust.r8`` equals the pixel-wise AND of the 10 phase maps (0 differing pixels).
       ``test_robust_equals_and_of_the_ten_phase_maps`` reads the eleven baked files, decodes the ten
       ``risk_p<k>.r8`` with the encoding written in sky.json, thresholds them at 5 mm and compares.
    2. HEALPix indices match astropy-healpix on 1,000 random directions (0 mismatches).
       ``test_ang2pix_matches_astropy_healpix_on_1000_random_directions`` runs the pipeline's own
       ``ang2pix_ring`` (the formula the phone re-implements) against astropy-healpix, seeded.

The other tests check the plan's fixed numbers (nside 64, 49,152 directions, 512 x 256, 5 mm
"Teaching threshold"), the file formats of docs/case_contract.md section 9, and the clearances
themselves against an independent brute-force computation.
"""

from __future__ import annotations

import math

import numpy as np
import pytest

from atlas_pipelines import contract, labels, masks, paths, sky

# ---- the plan's numbers ------------------------------------------------------------------------
NSIDE = 64
N_DIRECTIONS = 49_152
WIDTH, HEIGHT = 512, 256
THRESHOLD_MM = 5.0
PHASES = 10
N_RANDOM = 1_000
ORGANS = {"heart", "esophagus", "cord", "trachea"}

CASE = "lung101"
SEED = 20260804
SKY_FILES = [f"risk_p{k}.r8" for k in range(PHASES)] + ["robust.r8", "sky.json"]


# ---- fixtures ----------------------------------------------------------------------------------
@pytest.fixture(scope="session")
def sky_dir(tcia_101, run_atlas, out_dir):
    """The sky directory of the real package after `atlas case sky` (cached when built)."""
    run_atlas("sky", "--case", CASE, out=out_dir)
    return paths.case_dir(CASE, out_dir) / "sky"


@pytest.fixture(scope="session")
def meta(sky_dir):
    return contract.read_json(sky_dir / "sky.json")


def read_r8(path):
    data = path.read_bytes()
    assert len(data) == WIDTH * HEIGHT, f"{path.name}: {len(data)} bytes"
    return np.frombuffer(data, dtype=np.uint8).reshape(HEIGHT, WIDTH)  # offset = col + 512 * row


@pytest.fixture(scope="session")
def risk(sky_dir, meta):
    return np.stack([read_r8(sky_dir / name) for name in meta["files"]["risk"]])


@pytest.fixture(scope="session")
def robust(sky_dir, meta):
    return read_r8(sky_dir / meta["files"]["robust"])


def astropy_ring_index(theta, phi, nside=NSIDE):
    import astropy.units as u
    from astropy_healpix import lonlat_to_healpix

    return np.asarray(lonlat_to_healpix(phi * u.rad, (0.5 * math.pi - theta) * u.rad, nside, order="ring"), dtype=np.int64)


# ---- acceptance 1 ------------------------------------------------------------------------------
def test_robust_equals_and_of_the_ten_phase_maps(sky_dir, meta):
    """ACCEPTANCE: robust.r8 == AND over the 10 phase maps of (clearance >= 5 mm), 0 differing pixels."""
    assert meta["threshold_mm"] == THRESHOLD_MM
    names = meta["files"]["risk"]
    assert names == [f"risk_p{k}.r8" for k in range(PHASES)]
    enc = meta["encoding"]
    clearance_mm = np.stack([read_r8(sky_dir / n).astype(np.float64) * enc["mm_per_count"] for n in names])
    expected = np.all(clearance_mm >= THRESHOLD_MM, axis=0)
    baked = read_r8(sky_dir / meta["files"]["robust"])
    assert set(np.unique(baked).tolist()) <= {0, 255}
    differing = int(np.count_nonzero((baked == 255) != expected))
    assert differing == 0
    # the map is not trivially constant, so the comparison says something
    assert 0 < int(expected.sum()) < WIDTH * HEIGHT


# ---- acceptance 2 ------------------------------------------------------------------------------
def test_ang2pix_matches_astropy_healpix_on_1000_random_directions():
    """ACCEPTANCE: own ang2pix_ring == astropy-healpix on 1,000 seeded random directions, 0 mismatches."""
    rng = np.random.default_rng(SEED)
    v = rng.normal(size=(N_RANDOM, 3))
    v /= np.linalg.norm(v, axis=1, keepdims=True)  # uniform on the sphere
    theta, phi = sky.vec2ang(v)
    own = sky.ang2pix_ring(NSIDE, theta, phi)
    ref = astropy_ring_index(theta, phi)
    assert own.shape == (N_RANDOM,)
    assert sky.npix_of(NSIDE) == N_DIRECTIONS
    assert 0 <= own.min() and own.max() < N_DIRECTIONS
    mismatches = int(np.count_nonzero(own != ref))
    assert mismatches == 0
    # the sample reaches the polar caps and the belt, so both branches of the formula are exercised
    z = np.abs(np.cos(theta))
    assert (z > 2 / 3).sum() > 100 and (z <= 2 / 3).sum() > 100


# ---- HEALPix, further checks -------------------------------------------------------------------
@pytest.mark.parametrize("nside", [1, 2, 4, 8, 64])
def test_pixel_centres_agree_with_astropy_healpix(nside):
    """Own pix2ang_ring gives astropy's pixel centres, and ang2pix_ring returns each centre to its pixel."""
    import astropy.units as u
    from astropy_healpix import healpix_to_lonlat

    ip = np.arange(12 * nside * nside)
    lon, lat = healpix_to_lonlat(ip, nside, order="ring")
    theta, phi = sky.pix2ang_ring(nside, ip)
    assert np.allclose(theta, 0.5 * math.pi - lat.to_value(u.rad), atol=1e-12)
    assert np.allclose(phi, lon.to_value(u.rad), atol=1e-12)
    assert np.array_equal(sky.ang2pix_ring(nside, theta, phi), ip)


def test_every_image_pixel_maps_to_the_astropy_healpix_pixel():
    """The bake's lookup (131,072 image pixel centres) agrees with astropy-healpix everywhere."""
    row, col = np.meshgrid(np.arange(HEIGHT), np.arange(WIDTH), indexing="ij")
    theta, phi = sky.pixel_angles(col, row)
    index = sky.equirect_healpix_index()
    assert index.shape == (HEIGHT, WIDTH)
    assert int(np.count_nonzero(index != astropy_ring_index(theta, phi))) == 0


def test_ray_directions_are_the_healpix_pixel_centres():
    d = sky.healpix_directions(NSIDE)
    assert d.shape == (N_DIRECTIONS, 3)
    assert np.allclose(np.linalg.norm(d, axis=1), 1.0, atol=1e-12)
    assert np.allclose(d.mean(axis=0), 0.0, atol=1e-12)


# ---- files and sky.json ------------------------------------------------------------------------
def test_files_and_sizes(sky_dir):
    assert sorted(p.name for p in sky_dir.iterdir()) == sorted(SKY_FILES)
    for name in SKY_FILES[:-1]:
        assert (sky_dir / name).stat().st_size == WIDTH * HEIGHT == 131_072


def test_sky_json_states_the_plan_numbers_and_the_mappings(sky_dir, meta):
    assert contract.is_canonical((sky_dir / "sky.json").read_bytes())
    assert meta["schema"] == "atlas.sky/1"
    assert meta["id"] == CASE
    assert (meta["width"], meta["height"]) == (WIDTH, HEIGHT)
    assert meta["nside"] == NSIDE and meta["npix"] == N_DIRECTIONS
    assert meta["ordering"] == "ring" and meta["healpix"]["ordering"] == "ring"
    assert meta["phases"] == PHASES
    assert meta["threshold_mm"] == THRESHOLD_MM
    assert meta["threshold_label"] == "Teaching threshold" == labels.LABEL_TEACHING_THRESHOLD
    assert set(meta["organs"]) == ORGANS
    # one label per asset: the file's own label is the real-data label (the maps are derived from the
    # real contours), the same string the manifest gives every sky file; "Teaching threshold" labels
    # the 5 mm value only, in threshold_label
    assert meta["label"] == "Public research data: TCIA 4D-Lung, CC BY 3.0. Meshes derived by ATLAS."
    assert all(labels.label_for(labels.KIND_REAL, "sky/" + n) == meta["label"] for n in SKY_FILES)
    assert meta["label"] != meta["threshold_label"]
    enc = meta["encoding"]
    assert enc["type"] == "u8" and enc["rounding"] == "floor"
    assert enc["clearance_max_mm"] == pytest.approx(255 * enc["mm_per_count"])
    assert meta["threshold_value"] * enc["mm_per_count"] == pytest.approx(THRESHOLD_MM)
    for key in ("type", "offset", "theta", "phi", "direction", "sampling"):
        assert meta["projection"][key]
    assert "LPS" in meta["frame"]
    ray = meta["ray"]
    assert ray["step_mm"] > 0 and ray["max_length_mm"] > 0 and ray["termination"]
    assert len(meta["target_mm"]) == 3 and meta["target_source"]


def test_target_is_the_argmax_rho_point_of_the_table(sky_dir, meta):
    table = sky_dir.parent / "table.json"
    if table.is_file():
        assert meta["target_fallback_used"] is False
        assert meta["target_mm"] == contract.read_table(table).argmax_rho_mm
    else:
        assert meta["target_fallback_used"] is True
        assert meta["target_source"].startswith("fallback")


def test_fallback_target_lies_in_every_phase_of_the_target_lesion(tcia_101):
    """The documented fallback (raw masks) gives a point inside the lesion in the phases of highest rho."""
    t = sky.target_from_masks(CASE, tcia_101.cache)
    stack, origin, spacing = masks.load_mask_stack(CASE, "tumor", tcia_101.cache)
    idx = np.round((t - origin) / spacing).astype(int)
    count = stack.sum(axis=0)
    assert count[tuple(idx)] == count.max()


def test_no_clinical_wording(sky_dir):
    text = (sky_dir / "sky.json").read_text(encoding="ascii").lower()
    for word in ("safe", "clinical", "treat", "diagnos", "patient-specific", "recommend", "surgical", "biopsy", "nodule"):
        assert word not in text, word
    assert "education" in text


# ---- the numbers in the maps -------------------------------------------------------------------
def test_checks_in_sky_json_equal_the_baked_bytes(meta, risk, robust):
    """The 20 test directions of sky.json (used by P2-B10) are consistent with the files."""
    checks = meta["checks"]
    assert len(checks) == 20
    mm = meta["encoding"]["mm_per_count"]
    for c in checks:
        col, row = c["col"], c["row"]
        theta, phi = sky.pixel_angles(col, row)
        assert np.allclose(c["dir"], sky.ang2vec(theta, phi), atol=2e-6)
        assert c["healpix"] == int(astropy_ring_index(np.array([theta]), np.array([phi]))[0])
        assert c["value"] == [int(v) for v in risk[:, row, col]]
        assert np.allclose(c["clearance_mm"], risk[:, row, col] * mm, atol=1e-6)
        assert c["min_clearance_mm"] == pytest.approx(min(c["clearance_mm"]), abs=1e-6)
        assert c["robust"] == bool(robust[row, col] == 255)


def test_clearance_is_bounded_by_the_clearance_at_the_target(meta, risk):
    """The target is a sample of every ray, so no direction exceeds the target's own clearance."""
    mm = meta["encoding"]["mm_per_count"]
    for k in range(PHASES):
        top = meta["stats"]["target_clearance_mm"][k]
        assert 0 < top < meta["encoding"]["clearance_max_mm"]  # nothing saturates
        assert risk[k].max() * mm <= top
        assert risk[k].max() * mm > top - 2 * mm  # some direction keeps (almost) all of it
        assert risk[k].min() == 0  # some rays pass through a structure


@pytest.mark.parametrize("phase", [0, 5])
def test_clearance_matches_a_brute_force_computation(tcia_101, meta, risk, phase):
    """Independent check: nearest organ voxel centre by k-d tree at points along 60 rays."""
    from scipy.spatial import cKDTree

    pts = []
    grid_lo = grid_hi = None
    for roi in meta["organs"]:
        m, origin, spacing = masks.load_mask(CASE, roi, phase, tcia_101.cache, crop=False)
        pts.append(origin + np.argwhere(m) * spacing)
        grid_lo, grid_hi = origin, origin + (np.array(m.shape) - 1) * spacing
    tree = cKDTree(np.concatenate(pts))
    target = np.array(meta["target_mm"])
    mm = meta["encoding"]["mm_per_count"]
    rng = np.random.default_rng(SEED + phase)
    cols = rng.integers(0, WIDTH, 60)
    rows = rng.integers(0, HEIGHT, 60)
    hp = sky.equirect_healpix_index()[rows, cols]
    theta, phi = sky.pix2ang_ring(NSIDE, hp)  # the ray of an image pixel is its HEALPix pixel's centre
    dirs = sky.ang2vec(theta, phi)
    worst = 0.0
    for d, col, row in zip(dirs, cols, rows):
        with np.errstate(divide="ignore"):
            t_box = np.where(d > 0, (grid_hi - target) / d, np.where(d < 0, (grid_lo - target) / d, np.inf)).min()
        t_end = min(t_box, meta["ray"]["max_length_mm"])
        t = np.append(np.arange(0.0, t_end, 0.25), t_end)  # finer than the step of the pipeline
        exact = tree.query(target + t[:, None] * d)[0].min()
        baked = risk[phase, row, col] * mm
        worst = max(worst, abs(baked - exact))
        # byte rounds down by up to 0.1 mm; trilinear reading of the field and the 1 mm ray step add < 1 mm
        assert baked == pytest.approx(exact, abs=1.0), (col, row, baked, exact)
    assert worst < 1.0


def test_encoding_rounds_down_and_keeps_the_threshold_exact():
    c = np.array([0.0, 0.0999, 0.1, 4.9999999, 5.0, 5.0000001, 25.49, 25.5, 300.0])
    v = sky.encode_clearance(c)
    assert v.tolist() == [0, 0, 1, 49, 50, 50, 254, 255, 255]
    assert np.array_equal(v >= sky.THRESHOLD_VALUE, c >= THRESHOLD_MM)
    assert (sky.decode_clearance(v) <= c + 1e-9).all()


# ---- step behaviour ----------------------------------------------------------------------------
def test_second_run_is_cached_and_bytes_are_stable(sky_dir, run_atlas, out_dir):
    before = {n: (sky_dir / n).read_bytes() for n in SKY_FILES}
    proc = run_atlas("sky", "--case", CASE, out=out_dir)
    assert "up to date" in proc.stdout
    assert {n: (sky_dir / n).read_bytes() for n in SKY_FILES} == before


def test_twin_has_no_sky(run_atlas, tmp_out):
    proc = run_atlas("sky", "--case", "twin", out=tmp_out, check=False)
    assert proc.returncode == 1
    assert not (paths.case_dir("twin", tmp_out) / "sky").exists()
