"""P2-D09 `atlas case pack` and `atlas case verify` (Station half).

Acceptance (Master Plan 10.2.8, row P2-D09, Station half):

    1. The hash check passes for every file of both packages (``atlas case verify`` exits 0, and
       ``contract.verify_manifest`` returns no problem) -- on the real ``out/`` directory.
    2. A tampered byte is detected (``verify`` exits non-zero and names the file); so are a missing
       file, a truncated file and an unlisted file. Done on copies of the packages.
    3. The labels are exactly the plan's strings (written out literally below, not imported), one per
       file: the sky files carry the real-data label (they are derived from the real contours), and
       "Teaching threshold" labels the 5 mm value inside sky/sky.json, not a file.
    4. CREDITS.txt carries the Hugo et al. 2016 dataset credit with its DOI and CC BY 3.0, and the TCIA
       citation (real case); the twin's says it is an illustrative model with no patient data.
    5. pack never silently completes: a missing expected file is listed on stdout and in the manifest.
       ``extra.complete`` means "every expected file is present", and ``extra.note`` says that it is
       not an acceptance claim.

The tests read the real ``out/`` tree (after ``atlas case all``, which is cached) and never force a
rewrite in it: every test that rewrites, tampers or forces works on a copy under tmp. Also here: the
stamps survive a moved ``--out`` tree, and two Python/C# edge cases of the contract reader.

Deferred to Phase 1 (not tested here): the packages inside the app build, the Hub route /cases/<id>/...,
and the 2-second load test with the network off on the phone.
"""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

from atlas_pipelines import cache, contract, paths

# ---- the plan's strings, literally -------------------------------------------------------------------
FOOTER = "Education and research visualisation. Not for clinical use."
LABEL_REAL = "Public research data: TCIA 4D-Lung, CC BY 3.0. Meshes derived by ATLAS."
LABEL_TWIN = "Illustrative model"  # plan 10.2.1: no full stop
LABEL_RIGID = "Simplified: rigid shape"
LABEL_AIRWAY = "Model-generated airway"
LABEL_THRESHOLD = "Teaching threshold"
DOI = "10.7937/K9/TCIA.2016.ELN8YGLE"
TCIA_DOI = "10.1007/s10278-013-9622-7"

REAL, TWIN = "lung101", "twin"

COMMON = ["CREDITS.txt", "core.glb", "itv.glb", "occ.r8", "phases.glb", "rho50.glb", "table.json",
          "tube4d.bin", "tube4d_rigid.bin"]
REAL_ONLY = (["airway/greedy.json", "airway/tree.json", "chest.glb"]
             + [f"sky/risk_p{k}.r8" for k in range(10)] + ["sky/robust.r8", "sky/sky.json"])
EXPECTED = {REAL: sorted(COMMON + REAL_ONLY), TWIN: sorted(COMMON)}
BUILD_NOTE = ("Build note: complete means only that every expected file of the package layout is present. "
              "It is not a statement that the files meet their acceptance tests.")


# ---- fixtures ----------------------------------------------------------------------------------------
@pytest.fixture(scope="session")
def built(tcia_101, run_atlas, out_dir):
    """`atlas case all` on the real out/ directory (every step cached when built; downloads and builds
    on a clean checkout). Returns the CompletedProcess; tests assert on it."""
    return run_atlas("all", "--case", "all", out=out_dir, check=False)


@pytest.fixture(scope="session")
def real_pkg(built, out_dir) -> Path:
    return paths.case_dir(REAL, out_dir, create=False)


@pytest.fixture(scope="session")
def twin_pkg(built, out_dir) -> Path:
    return paths.case_dir(TWIN, out_dir, create=False)


@pytest.fixture()
def pkg_copy(built, out_dir, tmp_path):
    """copy(case) -> (tmp out root, copied package dir). The frame file is copied too so pack can run."""
    def copy(case: str):
        out = tmp_path / "out"
        dst = out / "cases" / case
        shutil.copytree(paths.case_dir(case, out_dir, create=False), dst)
        (out / "work" / case).mkdir(parents=True, exist_ok=True)
        return out, dst
    return copy


def atlas(*args: str, out: Path):
    cmd = [sys.executable, "-m", "atlas_pipelines.cli", "case", *args, "--out", str(out)]
    return subprocess.run(cmd, capture_output=True, text=True)


def read_manifest_obj(pkg: Path) -> dict:
    return contract.read_json(pkg / "manifest.json")


# ---- acceptance 1: the hash check passes for every file ----------------------------------------------
def test_all_steps_exit_zero(built):
    assert built.returncode == 0, f"atlas case all failed ({built.returncode}):\n{built.stdout}\n{built.stderr}"
    assert "FAILED" not in built.stdout


@pytest.mark.parametrize("case", [REAL, TWIN])
def test_acceptance_hash_check_passes_for_every_file(case, built, out_dir):
    pkg = paths.case_dir(case, out_dir, create=False)
    assert contract.verify_manifest(pkg) == []
    proc = atlas("verify", "--case", case, out=out_dir)
    assert proc.returncode == 0, proc.stdout + proc.stderr
    assert f"verify {case}: OK" in proc.stdout
    m = contract.read_manifest(pkg / "manifest.json")
    # every file on disk is listed, hashed by an independent hashlib call, and the sizes match
    import hashlib
    on_disk = sorted(p.relative_to(pkg).as_posix() for p in pkg.rglob("*")
                     if p.is_file() and p.name != "manifest.json")
    assert [f.path for f in m.files] == on_disk
    for f in m.files:
        data = (pkg / f.path).read_bytes()
        assert len(data) == f.size, f.path
        assert hashlib.sha256(data).hexdigest() == f.sha256, f.path


def test_verify_all_cases_default(built, out_dir):
    proc = atlas("verify", "-q", out=out_dir)  # default --case all
    assert proc.returncode == 0, proc.stdout + proc.stderr
    assert "verify lung101: OK" in proc.stdout and "verify twin: OK" in proc.stdout


@pytest.mark.parametrize("case", [REAL, TWIN])
def test_package_holds_exactly_the_expected_files(case, built, out_dir):
    pkg = paths.case_dir(case, out_dir, create=False)
    m = read_manifest_obj(pkg)
    assert [f["path"] for f in m["files"]] == EXPECTED[case]
    assert m["extra"] == {"complete": True, "missing": [], "note": BUILD_NOTE, "unexpected": []}
    # no DICOM, no stray files anywhere
    for p in pkg.rglob("*"):
        assert p.suffix.lower() not in (".dcm", ".dicom"), p
    twin_has = {p.relative_to(pkg).as_posix() for p in pkg.rglob("*") if p.is_file()}
    if case == TWIN:
        assert not any(r.startswith(("sky/", "airway/")) or r == "chest.glb" for r in twin_has)


def _tree_state(root: Path) -> dict:
    return {p.relative_to(root).as_posix(): (p.stat().st_mtime_ns, p.stat().st_size)
            for p in sorted(root.rglob("*")) if p.is_file()}


@pytest.mark.parametrize("case", [REAL, TWIN])
def test_pack_is_idempotent(case, built, out_dir, pkg_copy):
    """A second pack does nothing, and a forced pack gives the same bytes. The real out/ tree is only
    read here (an unforced pack of a fresh package writes nothing: checked by mtime); the forced pack
    runs on a copy."""
    real = paths.case_dir(case, out_dir, create=False)
    before = (real / "manifest.json").read_bytes()
    state = _tree_state(real)
    proc = atlas("pack", "--case", case, out=out_dir)
    assert proc.returncode == 0, proc.stderr
    assert "up to date" in proc.stdout
    assert _tree_state(real) == state, "an unforced pack of a fresh package rewrote a file"
    # on a copy: packed once (the copy has no stamp), then cached, then forced: always the same bytes
    out, pkg = pkg_copy(case)
    for args, word in (((), "packed"), ((), "up to date"), (("--force",), "packed")):
        proc = atlas("pack", "--case", case, *args, out=out)
        assert proc.returncode == 0, proc.stderr
        assert word in proc.stdout
        assert (pkg / "manifest.json").read_bytes() == before
        assert (pkg / "CREDITS.txt").read_bytes() == (real / "CREDITS.txt").read_bytes()
    assert _tree_state(real) == state


def test_stamps_survive_a_moved_out_tree(pkg_copy, tmp_path):
    """Outputs are recorded relative to the stamp, so an --out tree moved as a whole is still fresh."""
    out, pkg = pkg_copy(TWIN)
    assert "packed" in atlas("pack", "--case", TWIN, out=out).stdout
    stamp = paths.stamp_path(TWIN, "pack", out)
    recorded = json.loads(stamp.read_text())["outputs"]
    assert sorted(recorded) == ["../../../cases/twin/CREDITS.txt", "../../../cases/twin/manifest.json"]
    moved = tmp_path / "elsewhere" / "out2"
    moved.parent.mkdir()
    shutil.move(str(out), str(moved))
    proc = atlas("pack", "--case", TWIN, out=moved)
    assert proc.returncode == 0, proc.stderr
    assert "up to date" in proc.stdout and "packed" not in proc.stdout
    assert atlas("verify", "--case", TWIN, out=moved).returncode == 0


def test_cache_output_paths_relative_absolute_and_old_stamps(tmp_path):
    """cache.StepCache: outputs outside the root are relative (with ..); a stamp of the old kind, with
    absolute paths, is still fresh in place; a changed output is not fresh."""
    root = tmp_path / "a" / "out"
    stamp = root / "work" / "c" / "stamps" / "s.json"
    target = root / "cases" / "c" / "f.bin"
    target.parent.mkdir(parents=True)
    target.write_bytes(b"abc")
    runs = []
    work = lambda: runs.append(1)  # noqa: E731
    assert cache.run_cached(stamp, "v", {"p": 1}, [], [target], work) is True
    assert list(json.loads(stamp.read_text())["outputs"]) == ["../../../cases/c/f.bin"]
    assert cache.run_cached(stamp, "v", {"p": 1}, [], [target], work) is False
    # moved as a whole: fresh
    shutil.move(str(tmp_path / "a"), str(tmp_path / "b"))
    root2 = tmp_path / "b" / "out"
    stamp2, target2 = root2 / "work" / "c" / "stamps" / "s.json", root2 / "cases" / "c" / "f.bin"
    assert cache.run_cached(stamp2, "v", {"p": 1}, [], [target2], work) is False
    assert cache.StepCache(stamp2).recorded_outputs() == [stamp2.parent / "../../../cases/c/f.bin"]
    # an old stamp (absolute output path) is still accepted where it is
    data = json.loads(stamp2.read_text())
    data["outputs"] = {target2.resolve().as_posix(): v for v in data["outputs"].values()}
    stamp2.write_text(json.dumps(data))
    assert cache.run_cached(stamp2, "v", {"p": 1}, [], [target2], work) is False
    # a changed output, a changed version: not fresh
    target2.write_bytes(b"abd")
    assert cache.run_cached(stamp2, "v", {"p": 1}, [], [target2], work) is True
    assert cache.run_cached(stamp2, "v2", {"p": 1}, [], [target2], work) is True
    assert len(runs) == 3


def test_sky_and_pack_keys_depend_on_the_modules_they_call():
    from atlas_pipelines import labels, masks, sky
    from atlas_pipelines.steps import pack as pack_step
    from atlas_pipelines.steps import sky as sky_step
    assert sky.VERSION in sky_step.CODE_VERSION and masks.CODE_VERSION in sky_step.CODE_VERSION
    assert labels.VERSION in pack_step.CODE_VERSION and contract.SCHEMA_CASE in pack_step.CODE_VERSION
    assert cache.compute_key(sky_step.CODE_VERSION, {}) != cache.compute_key(
        sky_step.CODE_VERSION.replace(sky.VERSION, sky.VERSION + "x"), {})


# ---- acceptance 2: a tampered byte is detected -------------------------------------------------------
@pytest.mark.parametrize("case,victim", [(REAL, "tube4d.bin"), (REAL, "sky/robust.r8"), (REAL, "airway/tree.json"),
                                         (TWIN, "table.json"), (TWIN, "itv.glb")])
def test_acceptance_tampered_byte_is_detected(case, victim, pkg_copy):
    out, pkg = pkg_copy(case)
    assert atlas("verify", "--case", case, out=out).returncode == 0  # the copy starts clean
    f = pkg / victim
    b = bytearray(f.read_bytes())
    b[len(b) // 2] ^= 0x01  # one bit
    f.write_bytes(bytes(b))
    proc = atlas("verify", "--case", case, out=out)
    assert proc.returncode != 0
    assert f"{victim}: sha256 mismatch" in proc.stdout
    assert f"verify {case}: FAILED" in proc.stdout
    assert contract.verify_manifest(pkg) == [f"{victim}: sha256 mismatch"]


def test_missing_truncated_and_unlisted_files_are_detected(pkg_copy):
    out, pkg = pkg_copy(TWIN)
    (pkg / "core.glb").unlink()
    data = (pkg / "occ.r8").read_bytes()
    (pkg / "occ.r8").write_bytes(data[:-1])
    (pkg / "stray.txt").write_text("x")
    (pkg / ".hidden").write_text("dot-files are ignored")
    proc = atlas("verify", "--case", TWIN, out=out)
    assert proc.returncode != 0
    assert "core.glb: missing" in proc.stdout
    assert "occ.r8: size" in proc.stdout
    assert "stray.txt: present but not listed" in proc.stdout
    assert ".hidden" not in proc.stdout


def test_verify_fails_without_manifest(pkg_copy):
    out, pkg = pkg_copy(TWIN)
    (pkg / "manifest.json").unlink()
    proc = atlas("verify", "--case", TWIN, out=out)
    assert proc.returncode != 0
    assert "manifest.json: missing" in proc.stdout


def test_tampered_manifest_is_detected_by_pack_rerun(pkg_copy):
    """Editing manifest.json by hand is repaired by pack (the stamp records the manifest's hash)."""
    out, pkg = pkg_copy(TWIN)
    assert atlas("pack", "--case", TWIN, out=out).returncode == 0
    good = (pkg / "manifest.json").read_bytes()
    assert good.count(b"Illustrative model") >= 2
    (pkg / "manifest.json").write_bytes(good.replace(b"Illustrative model", b"Something else", 1))
    proc = atlas("pack", "--case", TWIN, out=out)
    assert proc.returncode == 0 and "packed" in proc.stdout and "up to date" not in proc.stdout
    assert (pkg / "manifest.json").read_bytes() == good


# ---- acceptance 3: labels are exactly the plan's strings --------------------------------------------
def test_acceptance_labels_real_case(real_pkg):
    m = read_manifest_obj(real_pkg)
    assert m["schema"] == "atlas.case/1"
    assert m["id"] == "lung101" and m["kind"] == "real"
    assert m["label"] == LABEL_REAL
    assert m["footer"] == FOOTER
    assert m["licence"] == "CC BY 3.0"
    assert m["M"] == 15.0 and m["Lambda"] == 1.5
    labels = m["labels"]
    assert sorted(labels) == EXPECTED[REAL] and sorted(labels) == [f["path"] for f in m["files"]]
    for path, lab in labels.items():
        if path == "tube4d_rigid.bin":
            want = LABEL_RIGID
        elif path.startswith("airway/"):
            want = LABEL_AIRWAY
        else:
            want = LABEL_REAL  # every sky file included: the maps are derived from the real contours
        assert lab == want, f"{path}: {lab!r}"
    # the strings that must occur, each at least once, and no label outside the set
    assert set(labels.values()) == {LABEL_REAL, LABEL_RIGID, LABEL_AIRWAY}
    # one label per asset: the label inside sky.json is the manifest's label of sky.json, and the 5 mm
    # threshold carries its own label in its own field
    sky = contract.read_json(real_pkg / "sky" / "sky.json")
    assert sky["label"] == labels["sky/sky.json"] == labels["sky/robust.r8"] == LABEL_REAL
    assert sky["threshold_label"] == LABEL_THRESHOLD
    assert LABEL_THRESHOLD not in labels.values()


def test_acceptance_labels_twin(twin_pkg):
    m = read_manifest_obj(twin_pkg)
    assert m["id"] == "twin" and m["kind"] == "illustrative"
    assert m["label"] == LABEL_TWIN
    assert m["footer"] == FOOTER
    assert m["M"] == 15.0 and m["Lambda"] == 1.5
    assert sorted(m["labels"]) == EXPECTED[TWIN]
    assert set(m["labels"].values()) == {LABEL_TWIN}
    assert not m["label"].endswith(".")
    assert b"Illustrative model." not in (twin_pkg / "manifest.json").read_bytes()
    assert "no patient data" in m["licence"].lower()


@pytest.mark.parametrize("case", [REAL, TWIN])
def test_manifest_is_canonical_with_origin_of_table(case, built, out_dir):
    pkg = paths.case_dir(case, out_dir, create=False)
    raw = (pkg / "manifest.json").read_bytes()
    assert contract.is_canonical(raw)
    m = contract.read_manifest(pkg / "manifest.json")
    t = contract.read_table(pkg / "table.json")
    assert m.origin_mm == t.origin_mm
    assert [f.path for f in m.files] == sorted(f.path for f in m.files)
    assert "manifest.json" not in [f.path for f in m.files]
    frame = contract.read_frame(paths.frame_path(case, out_dir))
    assert m.origin_mm == frame.origin_mm


# ---- acceptance 4: CREDITS content ------------------------------------------------------------------
def credits(pkg: Path) -> str:
    raw = (pkg / "CREDITS.txt").read_bytes()
    assert raw.endswith(b"\n") and not raw.endswith(b"\n\n") and b"\r" not in raw and not raw.startswith(b"\xef\xbb\xbf")
    return raw.decode("utf-8")


def test_acceptance_credits_real(real_pkg):
    c = credits(real_pkg)
    for needle in (
        "Hugo et al. 2016",
        "Data from 4D Lung Imaging of NSCLC Patients",
        "The Cancer Imaging Archive",
        f"doi {DOI}",
        "CC BY 3.0",
        "Clark K, et al. The Cancer Imaging Archive (TCIA): Maintaining and Operating a Public Information "
        "Repository. Journal of Digital Imaging 26(6):1045-1057, 2013. doi 10.1007/s10278-013-9622-7",
        LABEL_REAL, FOOTER, "Meshes derived by ATLAS", "101_HM10395",
    ):
        assert needle in c, needle
    assert "AeroPath" not in c
    assert "nodule" not in c.lower()
    assert "target lesion" in c
    m = read_manifest_obj(real_pkg)
    assert any(DOI in s for s in m["citations"]) and any(TCIA_DOI in s for s in m["citations"])
    assert DOI in m["credit"] and "Hugo" in m["credit"]


def test_acceptance_credits_twin(twin_pkg):
    c = credits(twin_pkg)
    assert LABEL_TWIN in c and FOOTER in c
    assert "illustrative model" in c.lower()
    assert "no patient data" in c.lower()
    for bad in ("AeroPath", "Hugo", "TCIA", DOI, "target lesion"):
        assert bad not in c, bad
    assert "nodule" in c


@pytest.mark.parametrize("case", [REAL, TWIN])
def test_credits_lists_every_label_once_per_file(case, built, out_dir):
    pkg = paths.case_dir(case, out_dir, create=False)
    c = credits(pkg)
    m = read_manifest_obj(pkg)
    for path in m["labels"]:
        assert f"    {path}\n" in c, path


def test_no_clinical_claims_in_package_text(real_pkg, twin_pkg):
    banned = ("diagnos", "treatment planning", "clinically validated", "patient safety", "cure")
    for pkg in (real_pkg, twin_pkg):
        text = (credits(pkg) + (pkg / "manifest.json").read_text()).lower()
        for b in banned:
            assert b not in text, (pkg.name, b)


# ---- acceptance 5: missing files are recorded, never silently completed -----------------------------
def test_missing_expected_file_is_reported_and_recorded(pkg_copy):
    out, pkg = pkg_copy(TWIN)
    assert atlas("pack", "--case", TWIN, out=out).returncode == 0
    (pkg / "core.glb").unlink()
    (pkg / "rho50.glb").unlink()
    proc = atlas("pack", "--case", TWIN, out=out)
    assert proc.returncode == 0, proc.stderr  # packs what is present
    assert "INCOMPLETE" in proc.stdout
    assert "missing core.glb" in proc.stdout and "missing rho50.glb" in proc.stdout
    m = read_manifest_obj(pkg)
    assert m["extra"]["complete"] is False
    assert m["extra"]["missing"] == ["core.glb", "rho50.glb"]
    assert m["extra"]["note"] == BUILD_NOTE  # the note is a fixed text, complete or not
    assert "core.glb" not in m["labels"] and len(m["files"]) == 7
    # the incomplete package still verifies (hashes match) and says so
    v = atlas("verify", "--case", TWIN, out=out)
    assert v.returncode == 0 and "INCOMPLETE" in v.stdout
    # --strict turns it into a failure, with the manifest still written
    s = atlas("pack", "--case", TWIN, "--strict", "--force", out=out)
    assert s.returncode == 1 and "missing" in s.stderr


def test_missing_whole_real_subsets_are_listed(pkg_copy):
    out, pkg = pkg_copy(REAL)
    shutil.rmtree(pkg / "sky")
    shutil.rmtree(pkg / "airway")
    (pkg / "chest.glb").unlink()
    proc = atlas("pack", "--case", REAL, out=out)
    assert proc.returncode == 0, proc.stderr
    m = read_manifest_obj(pkg)
    assert m["extra"]["complete"] is False
    assert m["extra"]["missing"] == sorted(["chest.glb", "airway/greedy.json", "airway/tree.json"]
                                           + [f"sky/risk_p{k}.r8" for k in range(10)]
                                           + ["sky/robust.r8", "sky/sky.json"])
    c = credits(pkg)
    assert "Model-generated airway" not in c and "Teaching threshold" not in c  # no claim about absent parts
    assert "Hugo et al. 2016" in c
    assert contract.verify_manifest(pkg) == []


def test_pack_refuses_dicom_and_empty_and_unknown(pkg_copy, tmp_path):
    out, pkg = pkg_copy(TWIN)
    (pkg / "slice.dcm").write_bytes(b"\x00" * 10)
    proc = atlas("pack", "--case", TWIN, out=out)
    assert proc.returncode == 1 and "DICOM" in proc.stderr
    (pkg / "slice.dcm").unlink()
    (pkg / "other.bin").write_bytes(b"\x00" * 128 + b"DICM" + b"\x00" * 8)  # DICOM by magic, odd name
    proc = atlas("pack", "--case", TWIN, out=out)
    assert proc.returncode == 1 and "DICOM" in proc.stderr
    (pkg / "other.bin").unlink()
    # unexpected non-DICOM files are packed but reported
    (pkg / "extra.txt").write_text("x")
    proc = atlas("pack", "--case", TWIN, out=out)
    assert proc.returncode == 0 and "unexpected file extra.txt" in proc.stdout
    assert read_manifest_obj(pkg)["extra"]["unexpected"] == ["extra.txt"]
    # nothing to pack
    assert atlas("pack", "--case", TWIN, out=tmp_path / "empty").returncode == 1
    assert atlas("pack", "--case", "nope", out=out).returncode == 1


# ---- Python agrees with C# on two edges of the contract ---------------------------------------------
@pytest.mark.parametrize("name,reader", [("manifest.json", contract.read_manifest), ("table.json", contract.read_table)])
def test_extra_null_is_rejected_as_in_csharp(name, reader, twin_pkg, tmp_path):
    """C# (CanonicalJson.AsObject) rejects "extra": null; Python must too. An object or no key is fine."""
    obj = contract.read_json(twin_pkg / name)
    obj.pop("extra", None)
    contract.write_json(tmp_path / name, obj)
    assert reader(tmp_path / name).extra is None  # the key may be absent
    contract.write_json(tmp_path / name, dict(obj, extra={}))
    assert reader(tmp_path / name).extra == {}
    for bad in (None, [], "x", 1):
        contract.write_json(tmp_path / name, dict(obj, extra=bad))
        with pytest.raises(contract.ContractError, match="extra must be an object"):
            reader(tmp_path / name)


def test_extra_null_is_rejected_by_the_csharp_reader(twin_pkg, tmp_path, pipelines_root):
    """The other half of the agreement, run for real: the C# hash check refuses the same manifest."""
    exe = _dotnet()
    if exe is None:
        pytest.skip("C# check NOT RUN: no .NET SDK found (PATH, $DOTNET_ROOT, ~/.dotnet)")
    pkg = tmp_path / "twin"
    shutil.copytree(twin_pkg, pkg)
    dll = _roundtrip_dll(exe, pipelines_root)
    env = _dotnet_env(exe)
    ok = subprocess.run([exe, str(dll), "verify", str(pkg)], capture_output=True, text=True, env=env)
    assert ok.returncode == 0, ok.stdout + ok.stderr
    obj = contract.read_json(pkg / "manifest.json")
    contract.write_json(pkg / "manifest.json", dict(obj, extra=None))
    bad = subprocess.run([exe, str(dll), "verify", str(pkg)], capture_output=True, text=True, env=env)
    assert bad.returncode != 0, bad.stdout + bad.stderr
    assert "extra" in (bad.stdout + bad.stderr)
    assert contract.verify_manifest(pkg) and "extra must be an object" in contract.verify_manifest(pkg)[0]


@pytest.mark.parametrize("v", [1e300, 1e303, -1e303, 1.7976931348623157e308, 2.0 ** 53 / 1e6, 1e10])
def test_format_float_out_of_range_is_a_contract_error(v):
    """v * 1e6 overflows to infinity for |v| > 1.8e302; that used to raise OverflowError from round()."""
    with pytest.raises(contract.ContractError, match="canonical range"):
        contract.format_float(v)
    with pytest.raises(contract.ContractError):
        contract.dumps_canonical({"x": v})


def test_format_float_in_range_is_unchanged():
    assert contract.format_float(9007199254.74) == "9007199254.74"
    assert contract.format_float(-9007199254.74) == "-9007199254.74"
    assert contract.format_float(15.0) == "15.0" and contract.format_float(-0.25) == "-0.25"
    for bad in (float("nan"), float("inf"), float("-inf")):
        with pytest.raises(contract.ContractError):
            contract.format_float(bad)


# ---- the step 'all' and the C# reader ----------------------------------------------------------------
def test_all_is_discovered_and_documents_the_sequence(run_atlas, tmp_out):
    proc = run_atlas("-h", check=False)
    assert proc.returncode == 0
    for step in ("all", "pack", "verify"):
        assert step in proc.stdout
    from atlas_pipelines.steps import all as all_step
    assert all_step.SEQUENCE[TWIN][-2:] == ("pack", "verify")
    assert all_step.SEQUENCE[REAL] == ("fetch", "masks", "tube4d", "shells", "chest", "airway", "sky", "pack", "verify")


def _dotnet() -> str | None:
    cands = [shutil.which("dotnet"), os.environ.get("DOTNET_ROOT", "") + "/dotnet", str(Path.home() / ".dotnet" / "dotnet")]
    for c in cands:
        if c and Path(c).is_file() and os.access(c, os.X_OK):
            return c
    return None


def _dotnet_env(exe: str) -> dict:
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1",
               DOTNET_SYSTEM_GLOBALIZATION_INVARIANT="1")
    env.setdefault("DOTNET_ROOT", str(Path(exe).resolve().parent))
    return env


_DLL: dict = {}


def _roundtrip_dll(exe: str, pipelines_root: Path) -> Path:
    """Build Atlas.Cases.RoundTrip once per session and return its dll."""
    if "dll" not in _DLL:
        proj = pipelines_root / "csharp" / "Atlas.Cases.RoundTrip" / "Atlas.Cases.RoundTrip.csproj"
        b = subprocess.run([exe, "build", str(proj), "-c", "Release", "-v", "quiet"], capture_output=True, text=True,
                           env=_dotnet_env(exe))
        assert b.returncode == 0, b.stdout + b.stderr
        _DLL["dll"] = sorted((proj.parent / "bin" / "Release").glob("*/Atlas.Cases.RoundTrip.dll"))[0]
    return _DLL["dll"]


@pytest.mark.parametrize("case", [REAL, TWIN])
def test_csharp_hash_check_of_the_real_packages(case, built, out_dir, pipelines_root):
    """The phone-side reader (Atlas.Cases, System only) accepts the real manifests and their hashes."""
    exe = _dotnet()
    if exe is None:
        pytest.skip("C# hash check NOT RUN: no .NET SDK found (PATH, $DOTNET_ROOT, ~/.dotnet)")
    env = _dotnet_env(exe)
    dll = _roundtrip_dll(exe, pipelines_root)
    pkg = paths.case_dir(case, out_dir, create=False)
    proc = subprocess.run([exe, str(dll), "verify", str(pkg)], capture_output=True, text=True, env=env)
    assert proc.returncode == 0, proc.stdout + proc.stderr
