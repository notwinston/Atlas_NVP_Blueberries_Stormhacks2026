"""P2-D03: `atlas case fetch` and `atlas case masks` (Master Plan 10.2.4 steps 1-2, 10.2.8).

Acceptance (10.2.8): 20 series fetched (10 CT + 10 RTSTRUCT); tumour centroid range over the 10 phases
LR / AP / SI = 3.1 / 3.1 / 6.8 mm, each within 1 mm.

The data is downloaded by the session fixture ``tcia_101`` when the cache is missing (no skip).
"""

from __future__ import annotations

import io
import json
import shutil
import zipfile
from pathlib import Path

import numpy as np
import pytest

from atlas_pipelines import cache as cachelib
from atlas_pipelines import dicomio, masks, paths
from atlas_pipelines.steps import fetch

CASE = "lung101"
PATIENT = "101_HM10395"
STUDY = "1.3.6.1.4.1.14519.5.2.1.6834.5010.300895057088920888884721386157"

# --- the plan's numbers (10.2.8 row P2-D03; R2 C.2) ---
SERIES_TOTAL = 20
SERIES_CT = 10
SERIES_RTSTRUCT = 10
CENTROID_RANGE_MM = (3.1, 3.1, 6.8)  # LR, AP, SI
CENTROID_RANGE_TOL_MM = 1.0
CT_SPACING_MM = (0.977, 0.977, 3.0)  # 10.2.4 step 2


# =============================================================================================
# acceptance
# =============================================================================================
def test_acceptance_20_series_fetched_10_ct_10_rtstruct(tcia_101):
    idx = tcia_101.index
    assert idx["patient_id"] == PATIENT and idx["collection"] == "4D-Lung" and idx["study_uid"] == STUDY
    assert idx["counts"] == {"CT": SERIES_CT, "RTSTRUCT": SERIES_RTSTRUCT}
    assert len(idx["series"]) == SERIES_TOTAL

    # count what is on disk, not what the index says
    import pydicom

    seen = {"CT": set(), "RTSTRUCT": set()}
    dirs = [d for d in tcia_101.dicom_dir.iterdir() if d.is_dir()]
    assert len(dirs) == SERIES_TOTAL
    for d in dirs:
        files = dicomio.dcm_files(d)
        assert files, f"{d} holds no DICOM"
        ds = pydicom.dcmread(files[0], stop_before_pixels=True)
        assert str(ds.SeriesInstanceUID) == d.name
        assert str(ds.PatientID) == PATIENT and str(ds.StudyInstanceUID) == STUDY
        assert len(files) == idx["series"][d.name]["instances"]
        seen[str(ds.Modality)].add(d.name)
    assert len(seen["CT"]) == SERIES_CT
    assert len(seen["RTSTRUCT"]) == SERIES_RTSTRUCT


def test_acceptance_tumour_centroid_range(tcia_101):
    """Centroid range over the 10 phases, from the masks on the CT grid: 3.1 / 3.1 / 6.8 mm, each within 1 mm."""
    cents = []
    for k in range(10):
        m, origin, spacing = masks.load_mask(CASE, "tumor", k, cache=tcia_101.cache)
        idx = np.argwhere(m)
        assert len(idx) > 0
        cents.append(origin + idx.mean(axis=0) * spacing)
    cents = np.array(cents)
    rng = cents.max(axis=0) - cents.min(axis=0)
    print("tumour centroid per phase (LPS mm):", np.round(cents, 2).tolist())
    print("tumour centroid range LR/AP/SI (mm):", np.round(rng, 3).tolist())
    for axis, (got, want) in enumerate(zip(rng, CENTROID_RANGE_MM)):
        assert abs(got - want) <= CENTROID_RANGE_TOL_MM, f"axis {'LR AP SI'.split()[axis]}: range {got:.3f} mm, plan {want} mm"
    # the summary reports the same numbers
    assert np.allclose(rng, tcia_101.summary["tumor_stats"]["centroid_range_mm"], atol=1e-3)
    assert np.allclose(cents, [p["centroid_mm"] for p in tcia_101.summary["roi"]["tumor"]], atol=1e-3)


# =============================================================================================
# fetch
# =============================================================================================
def test_index_maps_each_phase_to_its_ct_and_rtstruct(tcia_101):
    idx = tcia_101.index
    assert [p["phase"] for p in idx["phases"]] == list(range(10))
    assert [p["percent"] for p in idx["phases"]] == list(range(0, 100, 10))
    cts = {p["ct"] for p in idx["phases"]}
    rts = {p["rtstruct"] for p in idx["phases"]}
    assert len(cts) == 10 and len(rts) == 10 and not cts & rts
    for p in idx["phases"]:
        assert idx["series"][p["ct"]] ["modality"] == "CT" and idx["series"][p["ct"]]["phase"] == p["phase"]
        assert idx["series"][p["rtstruct"]]["modality"] == "RTSTRUCT" and idx["series"][p["rtstruct"]]["phase"] == p["phase"]
        assert p["ct_instances"] == 149 and p["rtstruct_instances"] == 1
        assert dicomio.phase_from_description(p["description"]) == p["phase"]
        # cross-check on the files: the structure set references the CT series of the same phase
        ds = dicomio.read_rtstruct(tcia_101.dicom_dir / p["rtstruct"])
        assert dicomio.referenced_series_uid(ds) == p["ct"]
        assert dicomio.ct_series_uid(tcia_101.dicom_dir / p["ct"]) == p["ct"]
        # and its ROI names carry the phase
        assert f"Tumor_c{p['percent']:02d}" in dicomio.roi_contours(ds)


def test_cached_files_match_recorded_hashes(tcia_101):
    """Every RTSTRUCT and one CT series re-hashed against series.json; all sizes checked."""
    idx = tcia_101.index
    for uid in idx["series"]:
        assert fetch.series_ok(tcia_101.dicom_dir / uid, idx["series"][uid]["instances"]) is not None
    for uid in [p["rtstruct"] for p in idx["phases"]] + [idx["phases"][0]["ct"]]:
        meta = fetch.series_ok(tcia_101.dicom_dir / uid, verify=True)
        assert meta is not None, f"series {uid} fails its SHA-256 check"
        assert fetch._series_digest(meta) == idx["series"][uid]["sha256"]
    assert not list(tcia_101.dicom_dir.rglob("*.part")), "a partial download was left behind"
    assert not list(tcia_101.dicom_dir.rglob("*.zip"))


def test_second_fetch_downloads_nothing(tcia_101, monkeypatch, run_atlas):
    before = tcia_101.index_path.read_bytes()

    def no_network(*a, **k):
        raise AssertionError("fetch touched the network although the cache is complete")

    monkeypatch.setattr(fetch, "http_get_json", no_network)
    monkeypatch.setattr(fetch, "http_download", no_network)
    monkeypatch.setattr(fetch, "_session", no_network)
    res = fetch.ensure(tcia_101.cache, CASE)
    assert res.downloaded_series == [] and res.downloaded_bytes == 0 and res.requests == 0
    assert tcia_101.index_path.read_bytes() == before

    proc = run_atlas("fetch")
    assert "nothing downloaded" in proc.stdout
    assert tcia_101.index_path.read_bytes() == before


def test_phase_from_description():
    assert dicomio.phase_from_description("P4^P101^S300^I00003, Gated, 0.0%A") == 0
    assert dicomio.phase_from_description("P4^P101^S300^I00012, Gated, 90.0%A") == 9
    for bad in ("P4^P101", "x, Gated, 95.0%", "x, Gated, 100.0%", None):
        with pytest.raises(dicomio.DicomError):
            dicomio.phase_from_description(bad)


def test_fetch_rejects_the_twin(tmp_path, run_atlas):
    proc = run_atlas("fetch", "--case", "twin", check=False)
    assert proc.returncode == 1 and "no TCIA data" in proc.stderr + proc.stdout


class _FakeTcia:
    """A stand-in for the TCIA REST API serving a reduced copy of the cached study (1 slice per CT series)."""

    def __init__(self, tcia):
        self.dir = tcia.dicom_dir
        self.listing = []
        for p in tcia.index["phases"]:
            for mod, uid in (("CT", p["ct"]), ("RTSTRUCT", p["rtstruct"])):
                self.listing.append({"SeriesInstanceUID": uid, "StudyInstanceUID": STUDY, "Modality": mod,
                                     "SeriesDescription": p["description"], "ImageCount": 1,
                                     "LicenseName": "L", "LicenseURI": "U"})
        self.downloads: list[str] = []
        self.corrupt_once: set[str] = set()
        self.extra_instance: set[str] = set()

    def get_json(self, url, params):
        assert url.endswith("/getSeries") and params["PatientID"] == PATIENT and params["StudyInstanceUID"] == STUDY
        return json.loads(json.dumps(self.listing))

    def download(self, url, params, dest: Path) -> int:
        uid = params["SeriesInstanceUID"]
        self.downloads.append(uid)
        src = dicomio.dcm_files(self.dir / uid)[0]
        buf = io.BytesIO()
        with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as z:
            z.writestr("LICENSE", "text")
            z.write(src, f"{uid}/1-1.dcm")
            if uid in self.extra_instance:
                z.write(src, f"{uid}/1-2.dcm")
        data = buf.getvalue()
        if uid in self.corrupt_once:
            self.corrupt_once.discard(uid)
            i = data.index(b"1-1.dcm") + 200  # flip bytes inside the compressed member: the CRC check must catch it
            data = data[:i] + bytes(b ^ 0xFF for b in data[i:i + 64]) + data[i + 64:]
        dest.write_bytes(data)
        return len(data)


def test_fetch_is_resumable_verified_and_retried(tcia_101, tmp_path, monkeypatch):
    fake = _FakeTcia(tcia_101)
    monkeypatch.setattr(fetch, "http_get_json", fake.get_json)
    monkeypatch.setattr(fetch, "http_download", fake.download)
    cache = tmp_path / "cache"
    ct3 = tcia_101.index["phases"][3]["ct"]
    rt7 = tcia_101.index["phases"][7]["rtstruct"]

    # first run: everything is downloaded; one zip arrives corrupted and is fetched again
    fake.corrupt_once.add(rt7)
    res = fetch.ensure(cache, CASE, jobs=4)
    assert len(res.downloaded_series) == SERIES_TOTAL
    assert sorted(fake.downloads) == sorted([s["SeriesInstanceUID"] for s in fake.listing] + [rt7])
    assert res.index["counts"] == {"CT": 10, "RTSTRUCT": 10}
    assert [p["ct"] for p in res.index["phases"]] == [p["ct"] for p in tcia_101.index["phases"]]
    assert [p["rtstruct"] for p in res.index["phases"]] == [p["rtstruct"] for p in tcia_101.index["phases"]]
    pdir = res.patient_dir
    assert not list(pdir.rglob("*.part"))
    index_bytes = res.index_path.read_bytes()

    # second run: nothing
    fake.downloads.clear()
    res = fetch.ensure(cache, CASE)
    assert res.downloaded_series == [] and fake.downloads == []
    assert res.index_path.read_bytes() == index_bytes

    # an interrupted run: one series lost entirely, one left without its completion record, a stale part file
    shutil.rmtree(pdir / ct3)
    (pdir / rt7 / "series.json").unlink()
    (pdir / rt7 / ".download.zip.part").write_bytes(b"half a zip")
    res = fetch.ensure(cache, CASE)
    assert sorted(res.downloaded_series) == sorted([ct3, rt7])
    assert sorted(fake.downloads) == sorted([ct3, rt7])
    assert res.index_path.read_bytes() == index_bytes  # same data, byte-identical index
    assert not list(pdir.rglob("*.part"))

    # a damaged instance is found by --verify and only that series is fetched again
    victim = dicomio.dcm_files(pdir / ct3)[0]
    raw = bytearray(victim.read_bytes())
    raw[-1] ^= 0xFF
    victim.write_bytes(bytes(raw))
    fake.downloads.clear()
    assert fetch.ensure(cache, CASE).downloaded_series == []  # same size: the quick check passes
    res = fetch.ensure(cache, CASE, verify=True)
    assert res.downloaded_series == [ct3] and fake.downloads == [ct3]

    # a wrong instance count is refused (3 attempts, then an error), and nothing is marked complete
    shutil.rmtree(pdir / ct3)
    fake.extra_instance.add(ct3)
    fake.downloads.clear()
    with pytest.raises(fetch.FetchError, match="instances in the zip"):
        fetch.ensure(cache, CASE)
    assert fake.downloads == [ct3] * 3
    assert not (pdir / ct3 / "series.json").exists()

    # a study that does not list 10 + 10 series is refused
    fake.extra_instance.clear()
    fake.listing.pop()
    with pytest.raises(fetch.FetchError, match="expected"):
        fetch.ensure(cache, CASE)


def test_fetch_refuses_rtstruct_on_the_wrong_ct(tcia_101, tmp_path, monkeypatch):
    fake = _FakeTcia(tcia_101)
    # swap the descriptions of two RTSTRUCT series: each now claims the other's phase
    rts = [s for s in fake.listing if s["Modality"] == "RTSTRUCT"]
    rts[2]["SeriesDescription"], rts[5]["SeriesDescription"] = rts[5]["SeriesDescription"], rts[2]["SeriesDescription"]
    monkeypatch.setattr(fetch, "http_get_json", fake.get_json)
    monkeypatch.setattr(fetch, "http_download", fake.download)
    with pytest.raises(fetch.FetchError, match="references CT"):
        fetch.ensure(tmp_path / "cache", CASE)
    assert not (tmp_path / "cache" / "tcia" / PATIENT / "index.json").exists()


def test_max_four_parallel_downloads():
    assert fetch.MAX_JOBS == 4


# =============================================================================================
# masks: geometry
# =============================================================================================
def test_ct_grid_read_from_headers(tcia_101):
    s = tcia_101.summary
    assert s["same_grid"] is True
    for k in range(10):
        g = dicomio.ct_grid(dicomio.ct_series_dir(tcia_101.cache, CASE, k))
        assert g == masks.load_grid(CASE, k, cache=tcia_101.cache)
        assert g.shape == (512, 512, 149)
        assert np.allclose(g.spacing_mm, CT_SPACING_MM, atol=1e-3)  # 0.977 x 0.977 x 3 mm
    # against the DICOM headers directly
    import pydicom

    d = dicomio.ct_series_dir(tcia_101.cache, CASE, 0)
    hs = [pydicom.dcmread(f, stop_before_pixels=True) for f in dicomio.dcm_files(d)]
    z = sorted(float(h.ImagePositionPatient[2]) for h in hs)
    g = masks.load_grid(CASE, 0, cache=tcia_101.cache)
    assert g.origin_mm == (float(hs[0].ImagePositionPatient[0]), float(hs[0].ImagePositionPatient[1]), z[0])
    assert g.spacing_mm[0] == float(hs[0].PixelSpacing[1]) and g.spacing_mm[1] == float(hs[0].PixelSpacing[0])
    assert np.allclose(np.diff(z), g.spacing_mm[2], atol=1e-3)
    assert [float(v) for v in hs[0].ImageOrientationPatient] == [1, 0, 0, 0, 1, 0]


def test_contours_lie_on_ct_slices(tcia_101):
    assert tcia_101.summary["max_contour_to_slice_mm"] < 0.01
    g = masks.load_grid(CASE, 0, cache=tcia_101.cache)
    for roi in masks.STORED_ROIS:
        for p in masks.load_contours(CASE, roi, 4, cache=tcia_101.cache):
            assert p.ndim == 2 and p.shape[1] == 3 and p.dtype == np.float64 and len(p) >= 3
            assert np.ptp(p[:, 2]) == 0.0
            t = (p[0, 2] - g.origin_mm[2]) / g.spacing_mm[2]
            assert abs(t - round(t)) < 1e-3


# =============================================================================================
# masks: rasteriser
# =============================================================================================
def _brute(poly, xs, ys):
    """The R2 prototype's point-in-polygon test (R2_rt_motion.py inpoly)."""
    px, py = np.meshgrid(xs, ys, indexing="ij")
    x, y = poly[:, 0], poly[:, 1]
    inside = np.zeros(px.shape, bool)
    j = len(x) - 1
    for i in range(len(x)):
        cond = (y[i] > py) != (y[j] > py)
        with np.errstate(divide="ignore", invalid="ignore"):
            xint = (x[j] - x[i]) * (py - y[i]) / (y[j] - y[i]) + x[i]
        inside ^= cond & (px < xint)
        j = i
    return inside


def test_rasteriser_matches_point_in_polygon():
    rng = np.random.default_rng(7)
    x0, dx, nx, y0, dy, ny = -31.3, 0.9766, 70, 12.1, 0.9766, 64
    xs, ys = x0 + dx * np.arange(nx), y0 + dy * np.arange(ny)
    for trial in range(20):
        n = int(rng.integers(3, 60))
        ang = np.sort(rng.uniform(0, 2 * np.pi, n))
        rad = rng.uniform(4, 28, n)
        poly = np.c_[x0 + 34 + rad * np.cos(ang), y0 + 31 + rad * np.sin(ang)]  # star-shaped, partly off the grid
        got = masks.rasterise_polygon(poly, x0, dx, nx, y0, dy, ny)
        assert np.array_equal(got, _brute(poly, xs, ys)), f"trial {trial}"


def test_rasteriser_xor_makes_holes_and_measures_area():
    t = np.linspace(0, 2 * np.pi, 720, endpoint=False)
    z = 6.0
    outer = np.c_[20 * np.cos(t), 20 * np.sin(t), np.full_like(t, z)]
    inner = np.c_[8 * np.cos(-t), 8 * np.sin(-t), np.full_like(t, z)]
    grid = dicomio.Grid((-30.05, -30.05, 0.0), (0.5, 0.5, 3.0), (121, 121, 4))
    vol, off = masks.rasterise([outer, inner], grid)
    assert off == 0.0
    assert not vol[:, :, [0, 1, 3]].any() and vol[:, :, 2].any()
    assert not vol[60, 60, 2], "the hole is filled"
    assert vol[60 + 28, 60, 2], "the ring is empty"
    area = vol.sum() * 0.25
    assert abs(area - np.pi * (20**2 - 8**2)) / (np.pi * (20**2 - 8**2)) < 0.01
    c = masks.centroid_mm(vol, grid.origin_mm, grid.spacing_mm)
    assert np.allclose(c, [0.0, 0.0, 6.0], atol=0.05)
    with pytest.raises(masks.MaskError):
        masks.rasterise([outer + [0, 0, 1.0]], grid)  # 1 mm off a slice


def test_tumour_mask_matches_prototype_rule_on_ct_grid(tcia_101):
    """The stored mask equals the prototype's point-in-polygon rule evaluated on the CT voxel centres.

    Up to a handful of voxels: the contour vertices sit on voxel centres, so single ties fall either way in floating point.
    """
    g = masks.load_grid(CASE, 5, cache=tcia_101.cache)
    xs, ys, zs = g.axes()
    full, origin, spacing = masks.load_mask(CASE, "tumor", 5, cache=tcia_101.cache, crop=False)
    assert full.shape == g.shape and tuple(origin) == g.origin_mm
    bb = tcia_101.summary["roi"]["tumor"][5]["bbox_ijk"]
    sx, sy = slice(bb[0] - 3, bb[3] + 3), slice(bb[1] - 3, bb[4] + 3)
    ref = np.zeros((sx.stop - sx.start, sy.stop - sy.start, g.shape[2]), bool)
    for p in masks.load_contours(CASE, "tumor", 5, cache=tcia_101.cache):
        k = int(round((p[0, 2] - g.origin_mm[2]) / g.spacing_mm[2]))
        ref[:, :, k] ^= _brute(p, xs[sx], ys[sy])
    diff = int((ref != full[sx, sy, :]).sum())
    assert diff <= 0.001 * full.sum(), f"{diff} voxels differ of {full.sum()}"
    assert full.sum() == full[sx, sy, :].sum()


# =============================================================================================
# masks: loader API
# =============================================================================================
def test_every_roi_on_every_phase(tcia_101):
    s = tcia_101.summary
    assert s["rois"] == ["tumor", "rlung", "llung", "heart", "esophagus", "cord", "trachea"]
    for roi in masks.roi_names():
        for k in range(10):
            m, origin, spacing = masks.load_mask(CASE, roi, k, cache=tcia_101.cache)
            assert m.dtype == np.bool_ and m.ndim == 3 and m.any()
            assert origin.shape == (3,) and spacing.shape == (3,)
            assert np.allclose(spacing, s["grid"]["spacing_mm"])
            # cropped tight: every face of the box touches the mask
            for a in range(3):
                assert m.take(0, axis=a).any() and m.take(-1, axis=a).any()
            if roi in masks.STORED_ROIS:
                rec = s["roi"][roi][k]
                assert rec["dicom_name"] == masks.dicom_roi_name(roi, k)
                assert int(m.sum()) == rec["voxels"]
                assert np.allclose(origin, rec["bbox_min_mm"], atol=1e-3)
                assert rec["volume_cm3"] == pytest.approx(rec["voxels"] * np.prod(spacing) / 1000, abs=1e-3)
                # the raster volume agrees with contour area x slice spacing
                assert rec["volume_cm3"] == pytest.approx(rec["contour_volume_cm3"], rel=0.03)


def test_crop_pad_and_full_grid_agree(tcia_101):
    g = masks.load_grid(CASE, 2, cache=tcia_101.cache)
    for roi in ("tumor", "heart", "lungs", "cord"):
        m, o, sp = masks.load_mask(CASE, roi, 2, cache=tcia_101.cache)
        mp, op, _ = masks.load_mask(CASE, roi, 2, cache=tcia_101.cache, pad=3)
        full, of, _ = masks.load_mask(CASE, roi, 2, cache=tcia_101.cache, crop=False)
        assert full.shape == g.shape and np.allclose(of, g.origin_mm)
        i0 = np.rint((o - of) / sp).astype(int)
        assert np.allclose(of + i0 * sp, o)
        assert np.array_equal(full[i0[0]:i0[0] + m.shape[0], i0[1]:i0[1] + m.shape[1], i0[2]:i0[2] + m.shape[2]], m)
        assert full.sum() == m.sum() == mp.sum()
        ip = np.rint((op - of) / sp).astype(int)
        assert np.array_equal(full[ip[0]:ip[0] + mp.shape[0], ip[1]:ip[1] + mp.shape[1], ip[2]:ip[2] + mp.shape[2]], mp)
        assert np.all(ip == np.maximum(i0 - 3, 0))


def test_lungs_is_union_of_both_lungs(tcia_101):
    r, _, _ = masks.load_mask(CASE, "RLung", 0, cache=tcia_101.cache, crop=False)
    l, _, _ = masks.load_mask(CASE, "LLung_c00", 0, cache=tcia_101.cache, crop=False)
    both, _, _ = masks.load_mask(CASE, "lungs", 0, cache=tcia_101.cache, crop=False)
    assert np.array_equal(both, r | l)
    assert len(masks.load_contours(CASE, "lungs", 0, cache=tcia_101.cache)) == (
        tcia_101.summary["roi"]["rlung"][0]["contours"] + tcia_101.summary["roi"]["llung"][0]["contours"])


def test_aliases_and_bad_arguments(tcia_101):
    a, _, _ = masks.load_mask(CASE, "oesophagus", 1, cache=tcia_101.cache)
    b, _, _ = masks.load_mask(CASE, "Esophagus", 1, cache=tcia_101.cache)
    assert np.array_equal(a, b)
    assert masks.canonical_roi("tumour") == masks.canonical_roi("Tumor_c30") == "tumor"
    with pytest.raises(masks.MaskError):
        masks.load_mask(CASE, "liver", 0, cache=tcia_101.cache)
    for bad in (10, -1, 50, 1.5):
        with pytest.raises(ValueError):
            masks.load_mask(CASE, "tumor", bad, cache=tcia_101.cache)


def test_stack_gives_union_and_intersection(tcia_101):
    stack, origin, spacing = masks.load_mask_stack(CASE, "tumor", cache=tcia_101.cache, pad=2)
    assert stack.shape[0] == 10 and stack.dtype == np.bool_
    vox = float(np.prod(spacing)) / 1000.0
    t = tcia_101.summary["tumor_stats"]
    assert stack.any(axis=0).sum() * vox == pytest.approx(t["union_cm3"], abs=1e-3)
    assert stack.all(axis=0).sum() * vox == pytest.approx(t["intersection_cm3"], abs=1e-3)
    assert stack.sum(axis=(1, 2, 3)).mean() * vox == pytest.approx(t["gtv_mean_cm3"], abs=1e-3)
    for k in (0, 6):
        m, o, _ = masks.load_mask(CASE, "tumor", k, cache=tcia_101.cache)
        i0 = np.rint((o - origin) / spacing).astype(int)
        assert np.array_equal(stack[k, i0[0]:i0[0] + m.shape[0], i0[1]:i0[1] + m.shape[1], i0[2]:i0[2] + m.shape[2]], m)
        assert stack[k].sum() == m.sum()
    print("raw masks: mean GTV %.2f, union %.2f, intersection %.2f cm3" % (t["gtv_mean_cm3"], t["union_cm3"], t["intersection_cm3"]))


def test_centroids_agree_with_r2_prototype(tcia_101, repo_root):
    """Per-phase centroids against the R2 prototype's table (rasterised at 1 mm in-plane): within 1 mm per axis."""
    table = json.loads((repo_root / "documents/research/scripts/R2_4dlung_motion_table.json").read_text())
    ref = next(r for r in table if r["patient"] == "101")
    assert ref["range_LR_AP_SI_mm"] == list(CENTROID_RANGE_MM)
    for k in range(10):
        got = np.array(tcia_101.summary["roi"]["tumor"][k]["centroid_mm"])
        want = np.array(ref["cen_by_phase"][str(10 * k)])
        assert np.abs(got - want).max() <= 1.0, f"phase {k}: {got} vs prototype {want}"
        # the polygons' own centroid agrees with the prototype to its rounding
        cc = np.array(tcia_101.summary["roi"]["tumor"][k]["contour_centroid_mm"])
        assert np.abs(cc - want).max() <= 0.2, f"phase {k}: contour centroid {cc} vs prototype {want}"


# =============================================================================================
# masks: step behaviour
# =============================================================================================
def test_masks_step_is_cached_and_deterministic(tcia_101, run_atlas, tmp_path):
    files = masks.expected_outputs(CASE, tcia_101.cache)
    before = {f.name: cachelib.sha256_file(f) for f in files}
    mtimes = {f.name: f.stat().st_mtime_ns for f in files}
    proc = run_atlas("masks")
    assert "up to date (cached)" in proc.stdout
    assert {f.name: f.stat().st_mtime_ns for f in files} == mtimes, "a cached run rewrote files"

    # a rebuild elsewhere from the same DICOM gives the same bytes
    other = tmp_path / "cache"
    (other / "tcia").mkdir(parents=True)
    (other / "tcia" / PATIENT).symlink_to(tcia_101.dicom_dir, target_is_directory=True)
    assert masks.run_step(CASE, other) is True
    assert masks.run_step(CASE, other) is False
    after = {f.name: cachelib.sha256_file(f) for f in masks.expected_outputs(CASE, other)}
    assert after == before


def test_masks_are_intermediate_not_package_files(tcia_101, out_dir):
    assert paths.default_cache_dir() in tcia_101.mask_dir.parents or tcia_101.cache in tcia_101.mask_dir.parents
    case = paths.case_dir(CASE, out_dir)
    if case.exists():
        assert not list(case.rglob("*.dcm")) and not list(case.rglob("*.npz"))


def test_masks_step_needs_fetch(tmp_path, run_atlas):
    import subprocess
    import sys

    proc = subprocess.run([sys.executable, "-m", "atlas_pipelines.cli", "case", "masks", "--cache", str(tmp_path)],
                          capture_output=True, text=True)
    assert proc.returncode == 1 and "atlas case fetch" in proc.stdout + proc.stderr
    proc = run_atlas("masks", "--case", "twin", check=False)
    assert proc.returncode == 1


def test_no_dicom_is_tracked_or_trackable(repo_root, tcia_101):
    import subprocess

    git = ["git", "-c", "filter.lfs.process=", "-c", "filter.lfs.required=false", "-C", str(repo_root)]
    tracked = subprocess.run(git + ["ls-files", "station/pipelines"], capture_output=True, text=True, check=True).stdout
    assert not [f for f in tracked.splitlines() if f.lower().endswith((".dcm", ".npz", ".zip"))]
    some = dicomio.dcm_files(tcia_101.dicom_dir / tcia_101.index["phases"][0]["rtstruct"])[0]
    ignored = subprocess.run(git + ["check-ignore", "-q", str(some)])
    assert ignored.returncode == 0, "the DICOM cache is not gitignored"
    ignored = subprocess.run(git + ["check-ignore", "-q", str(tcia_101.summary_path)])
    assert ignored.returncode == 0, "the mask cache is not gitignored"
