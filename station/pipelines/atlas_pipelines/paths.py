"""Default directories of the pipeline, independent of the current working directory.

Layout (all under ``station/pipelines/`` unless overridden)::

    .cache/                      downloads and reusable intermediates (DICOM, masks) -- gitignored
    out/cases/<id>/              the case package, exactly the files of docs/case_contract.md
    out/work/<id>/               per-case scratch that is NOT part of the package:
        frame.json               the case frame (origin_mm, M, Lambda), see contract.Frame
        stamps/<step>.json       cache stamps, see cache.StepCache

Overrides: the CLI options ``--out`` / ``--cache``, or the environment variables
``ATLAS_PIPELINES_OUT`` / ``ATLAS_PIPELINES_CACHE``.
"""

from __future__ import annotations

import os
from pathlib import Path

#: Case ids fixed by the project owner.
REAL_CASE = "lung101"
TWIN_CASE = "twin"

#: station/pipelines/ (the directory holding pyproject.toml); valid for the editable install.
PIPELINES_ROOT = Path(__file__).resolve().parents[1]


def repo_root() -> Path:
    """The repository (or worktree) root: two levels above station/pipelines/."""
    return PIPELINES_ROOT.parents[1]


def default_out_dir() -> Path:
    env = os.environ.get("ATLAS_PIPELINES_OUT")
    return Path(env).expanduser().resolve() if env else PIPELINES_ROOT / "out"


def default_cache_dir() -> Path:
    env = os.environ.get("ATLAS_PIPELINES_CACHE")
    return Path(env).expanduser().resolve() if env else PIPELINES_ROOT / ".cache"


def cases_dir(out: Path | str | None = None) -> Path:
    return Path(out) / "cases" if out is not None else default_out_dir() / "cases"


def case_dir(case_id: str, out: Path | str | None = None, create: bool = False) -> Path:
    """``<out>/cases/<id>/``: the package directory. Only package files go here."""
    d = cases_dir(out) / case_id
    if create:
        d.mkdir(parents=True, exist_ok=True)
    return d


def work_dir(case_id: str, out: Path | str | None = None, create: bool = False) -> Path:
    """``<out>/work/<id>/``: per-case scratch outside the package (frame.json, stamps, debug dumps)."""
    base = Path(out) if out is not None else default_out_dir()
    d = base / "work" / case_id
    if create:
        d.mkdir(parents=True, exist_ok=True)
    return d


def frame_path(case_id: str, out: Path | str | None = None) -> Path:
    """Where the tube4d / twin step records the case frame for the later steps."""
    return work_dir(case_id, out) / "frame.json"


def stamp_path(case_id: str, step: str, out: Path | str | None = None) -> Path:
    """Cache stamp of one step for one case (see cache.StepCache)."""
    return work_dir(case_id, out) / "stamps" / f"{step}.json"
