"""Shared pytest fixtures of the pipeline tests.

Run from station/pipelines/:   .venv/bin/python -m pytest tests/test_p2_dNN_<name>.py

Fixtures
    pipelines_root   station/pipelines/
    repo_root        the repository (worktree) root
    fixtures_dir     tests/fixtures/ (committed inputs; never *.glb, which Git LFS would take)
    out_dir          the real output root (station/pipelines/out, or $ATLAS_PIPELINES_OUT): use it for
                     tests of the real pipeline products, so cached step results are reused
    cache_dir        the real cache root (station/pipelines/.cache, or $ATLAS_PIPELINES_CACHE)
    tmp_out          an empty output root for one test (pass as --out)
    session_tmp_out  an empty output root shared by the whole test session
    run_atlas        run_atlas("info", "--case", "twin", out=tmp_out) -> CompletedProcess of `atlas case ...`
"""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path

import pytest

from atlas_pipelines import paths


@pytest.fixture(scope="session")
def pipelines_root() -> Path:
    return paths.PIPELINES_ROOT


@pytest.fixture(scope="session")
def repo_root() -> Path:
    return paths.repo_root()


@pytest.fixture(scope="session")
def fixtures_dir() -> Path:
    return Path(__file__).resolve().parent / "fixtures"


@pytest.fixture(scope="session")
def out_dir() -> Path:
    d = paths.default_out_dir()
    d.mkdir(parents=True, exist_ok=True)
    return d


@pytest.fixture(scope="session")
def cache_dir() -> Path:
    d = paths.default_cache_dir()
    d.mkdir(parents=True, exist_ok=True)
    return d


@pytest.fixture()
def tmp_out(tmp_path: Path) -> Path:
    d = tmp_path / "out"
    d.mkdir()
    return d


@pytest.fixture(scope="session")
def session_tmp_out(tmp_path_factory: pytest.TempPathFactory) -> Path:
    return tmp_path_factory.mktemp("out")


@pytest.fixture(scope="session")
def run_atlas(cache_dir: Path):
    """Run `atlas case <args...>` in a subprocess with this interpreter."""

    def _run(*args: str, out: Path | None = None, check: bool = True) -> subprocess.CompletedProcess:
        cmd = [sys.executable, "-m", "atlas_pipelines.cli", "case", *args]
        if out is not None:
            cmd += ["--out", str(out)]
        cmd += ["--cache", str(cache_dir)]
        proc = subprocess.run(cmd, capture_output=True, text=True)
        if check and proc.returncode != 0:
            raise AssertionError(f"{' '.join(cmd)} failed ({proc.returncode}):\n{proc.stdout}\n{proc.stderr}")
        return proc

    return _run


# =============================================================================================
# TCIA DATA FIXTURE (owned by the fetch/masks agent, work package P2-D03)
# =============================================================================================
@pytest.fixture(scope="session")
def tcia_101(cache_dir: Path):
    """The TCIA 4D-Lung data of patient 101_HM10395 (case "lung101"), fetched and rasterised.

    Runs `fetch` and `masks` when their results are missing from ``cache_dir`` (downloads about 0.3 GB,
    0.8 GB on disk; no skip) and does nothing when they are present. Returns a namespace:

        case         "lung101"
        cache        the cache root (pass as ``cache=`` to atlas_pipelines.masks loaders, or as --cache)
        dicom_dir    <cache>/tcia/101_HM10395/   (one directory per SeriesInstanceUID)
        index_path   <dicom_dir>/index.json      phase -> CT series, RTSTRUCT series
        index        its content (dict)
        mask_dir     <cache>/masks/lung101/
        summary_path <mask_dir>/summary.json
        summary      its content (dict): volumes, centroids, bounding boxes per ROI per phase

    Read masks with ``atlas_pipelines.masks.load_mask("lung101", roi, phase, cache=tcia_101.cache)``.
    """
    from types import SimpleNamespace

    from atlas_pipelines import dicomio, masks
    from atlas_pipelines.steps import fetch

    res = fetch.ensure(cache_dir, paths.REAL_CASE)
    masks.run_step(paths.REAL_CASE, cache_dir)
    mdir = masks.mask_dir(paths.REAL_CASE, cache_dir)
    return SimpleNamespace(
        case=paths.REAL_CASE,
        cache=cache_dir,
        dicom_dir=res.patient_dir,
        index_path=res.index_path,
        index=dicomio.read_index(cache_dir, paths.REAL_CASE),
        mask_dir=mdir,
        summary_path=mdir / "summary.json",
        summary=masks.load_summary(paths.REAL_CASE, cache_dir),
    )
