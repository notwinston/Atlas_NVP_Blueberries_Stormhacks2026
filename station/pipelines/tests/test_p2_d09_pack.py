"""P2-D09 `atlas case pack` and `atlas case verify` (Station half).

Acceptance (Master Plan 10.2.8, row P2-D09, Station half):

    1. The hash check passes for every file of both packages (``atlas case verify`` exits 0, and
       ``contract.verify_manifest`` returns no problem) -- on the real ``out/`` directory.
    2. A tampered byte is detected (``verify`` exits non-zero and names the file); so are a missing
       file, a truncated file and an unlisted file. Done on copies of the packages.
    3. The labels are exactly the plan's strings (written out literally below, not imported).
    4. CREDITS.txt carries the Hugo et al. 2016 dataset credit with its DOI and CC BY 3.0, and the TCIA
       citation (real case); the twin's says it is an illustrative model with no patient data.
    5. pack never silently completes: a missing expected file is listed on stdout and in the manifest.

Deferred to Phase 1 (not tested here): the packages inside the app build, the Hub route /cases/<id>/...,
and the 2-second load test with the network off on the phone.
"""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

from atlas_pipelines import contract, paths

# ---- the plan's strings, literally -------------------------------------------------------------------
FOOTER = "Education and research visualisation. Not for clinical use."
LABEL_REAL = "Public research data: TCIA 4D-Lung, CC BY 3.0. Meshes derived by ATLAS."
LABEL_TWIN = "Illustrative model."
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
    assert m["extra"] == {"complete": True, "missing": [], "unexpected": []}
    # no DICOM, no stray files anywhere
    for p in pkg.rglob("*"):
        assert p.suffix.lower() not in (".dcm", ".dicom"), p
    twin_has = {p.relative_to(pkg).as_posix() for p in pkg.rglob("*") if p.is_file()}
    if case == TWIN:
        assert not any(r.startswith(("sky/", "airway/")) or r == "chest.glb" for r in twin_has)


def test_pack_is_idempotent(built, out_dir):
    before = {c: (paths.case_dir(c, out_dir, create=False) / "manifest.json").read_bytes() for c in (REAL, TWIN)}
    for c in (REAL, TWIN):
        proc = atlas("pack", "--case", c, out=out_dir)
        assert proc.returncode == 0, proc.stderr
        assert "up to date" in proc.stdout
    for c in (REAL, TWIN):
        assert (paths.case_dir(c, out_dir, create=False) / "manifest.json").read_bytes() == before[c]
    # forced rebuild gives the same bytes
    proc = atlas("pack", "--case", TWIN, "--force", out=out_dir)
    assert proc.returncode == 0
    assert (paths.case_dir(TWIN, out_dir, create=False) / "manifest.json").read_bytes() == before[TWIN]


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
    (pkg / "manifest.json").write_bytes(good.replace(b"Illustrative model.", b"Something else.", 1))
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
        elif path in ("sky/robust.r8", "sky/sky.json"):
            want = LABEL_THRESHOLD  # the 5 mm clearance threshold (plan 10.2.1, last row)
        else:
            want = LABEL_REAL
        assert lab == want, f"{path}: {lab!r}"
    # the strings that must occur, each at least once, and no label outside the set
    assert set(labels.values()) == {LABEL_REAL, LABEL_RIGID, LABEL_AIRWAY, LABEL_THRESHOLD}


def test_acceptance_labels_twin(twin_pkg):
    m = read_manifest_obj(twin_pkg)
    assert m["id"] == "twin" and m["kind"] == "illustrative"
    assert m["label"] == LABEL_TWIN
    assert m["footer"] == FOOTER
    assert m["M"] == 15.0 and m["Lambda"] == 1.5
    assert sorted(m["labels"]) == EXPECTED[TWIN]
    assert set(m["labels"].values()) == {LABEL_TWIN}
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


@pytest.mark.parametrize("case", [REAL, TWIN])
def test_csharp_hash_check_of_the_real_packages(case, built, out_dir, pipelines_root):
    """The phone-side reader (Atlas.Cases, System only) accepts the real manifests and their hashes."""
    exe = _dotnet()
    if exe is None:
        pytest.skip("C# hash check NOT RUN: no .NET SDK found (PATH, $DOTNET_ROOT, ~/.dotnet)")
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1",
               DOTNET_SYSTEM_GLOBALIZATION_INVARIANT="1")
    env.setdefault("DOTNET_ROOT", str(Path(exe).resolve().parent))
    proj = pipelines_root / "csharp" / "Atlas.Cases.RoundTrip" / "Atlas.Cases.RoundTrip.csproj"
    b = subprocess.run([exe, "build", str(proj), "-c", "Release", "-v", "quiet"], capture_output=True, text=True, env=env)
    assert b.returncode == 0, b.stdout + b.stderr
    dll = sorted((proj.parent / "bin" / "Release").glob("*/Atlas.Cases.RoundTrip.dll"))[0]
    pkg = paths.case_dir(case, out_dir, create=False)
    proc = subprocess.run([exe, str(dll), "verify", str(pkg)], capture_output=True, text=True, env=env)
    assert proc.returncode == 0, proc.stdout + proc.stderr
