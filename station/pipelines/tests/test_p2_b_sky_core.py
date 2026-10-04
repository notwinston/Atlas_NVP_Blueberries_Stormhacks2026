"""P2-B10 / P2-B11 engine-free cores of the needle sky: Atlas.Sky.Core (C#, System only) and its test runner.

Acceptance (Master Plan 10.2.8, rows P2-B10 and P2-B11; the Editor and phone halves are NOT covered here):

    P2-B10  clearance returned equals sky.json's value within 0.1 mm at the 20 test directions sky.json carries,
            on the real lung101 package (needs ATLAS_CASES_DIR; reported "NOT RUN" otherwise).
    P2-B11  the octant triangle gives 270 degrees +/- 0.5.
    P2-D08  (consumer side) C# HEALPix indices equal astropy-healpix on 1,000 seeded random directions.

The C# test runner is built and run with the .NET SDK (PATH, $DOTNET_ROOT or ~/.dotnet); without one the tests
are skipped with that reason, never silently passed. The cases directory comes from $ATLAS_CASES_DIR
(``<dir>/lung101/sky/``). Without it the real-case test is skipped; a synthetic 32 x 16 fixture
(tests/fixtures/p2_b_sky/, written by make_p2_b_sky_fixture.py) runs the loader and lookup tests.
"""

from __future__ import annotations

import importlib.util
import os
import re
import shutil
import subprocess
from pathlib import Path

import pytest

FIXTURE_DIR = Path(__file__).resolve().parent / "fixtures" / "p2_b_sky"
GENERATOR = Path(__file__).resolve().parent / "fixtures" / "make_p2_b_sky_fixture.py"
FRESH_SEED = 20260903  # a second list, different from the committed one, generated here at test time


# =============================================================================================
# C# toolchain
# =============================================================================================
def _find_dotnet() -> str | None:
    candidates = []
    on_path = shutil.which("dotnet")
    if on_path:
        candidates.append(Path(on_path))
    if os.environ.get("DOTNET_ROOT"):
        candidates.append(Path(os.environ["DOTNET_ROOT"]) / "dotnet")
    candidates.append(Path.home() / ".dotnet" / "dotnet")
    for c in candidates:
        if c.is_file() and os.access(c, os.X_OK):
            return str(c)
    return None


def _dotnet_env(exe: str) -> dict:
    env = dict(os.environ)
    env.setdefault("DOTNET_ROOT", str(Path(exe).resolve().parent))
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    env["DOTNET_NOLOGO"] = "1"
    env["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1"  # the SDK itself aborts without ICU otherwise
    return env


@pytest.fixture(scope="module")
def make_fixture():
    spec = importlib.util.spec_from_file_location("make_p2_b_sky_fixture", GENERATOR)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


@pytest.fixture(scope="module")
def runner(pipelines_root):
    """Builds Atlas.Sky.Core.Tests once; returns run(*args, env_extra=None, drop_cases=False) -> CompletedProcess."""
    exe = _find_dotnet()
    if exe is None:
        pytest.skip("C# tests NOT RUN: no .NET SDK found on PATH, in $DOTNET_ROOT or in ~/.dotnet "
                    "(install with dotnet-install.sh --channel LTS --install-dir ~/.dotnet)")
    env = _dotnet_env(exe)
    root = pipelines_root / "csharp"
    proj = root / "Atlas.Sky.Core.Tests" / "Atlas.Sky.Core.Tests.csproj"
    build = subprocess.run([exe, "build", str(proj), "-c", "Release", "-v", "quiet"], capture_output=True, text=True, env=env)
    assert build.returncode == 0, f"dotnet build Atlas.Sky.Core.Tests failed:\n{build.stdout}\n{build.stderr}"
    found = sorted((root / "Atlas.Sky.Core.Tests" / "bin" / "Release").glob("*/Atlas.Sky.Core.Tests.dll"))
    assert found, "Atlas.Sky.Core.Tests.dll not produced"
    dll = found[0]

    def run(*args: str, cases_dir: str | None = None) -> subprocess.CompletedProcess:
        e = dict(env)
        e.pop("ATLAS_CASES_DIR", None)
        if cases_dir is not None:
            e["ATLAS_CASES_DIR"] = cases_dir
        return subprocess.run([exe, str(dll), *map(str, args)], capture_output=True, text=True, env=e)

    return run


def _summary(proc: subprocess.CompletedProcess) -> tuple[int, int]:
    m = re.search(r"^PASSED (\d+) FAILED (\d+)$", proc.stdout, re.M)
    assert m, f"no summary line in the runner output:\n{proc.stdout}\n{proc.stderr}"
    return int(m.group(1)), int(m.group(2))


def _real_sky_dir() -> Path | None:
    cases = os.environ.get("ATLAS_CASES_DIR")
    if not cases:
        return None
    p = Path(cases) / "lung101" / "sky"
    return p if (p / "sky.json").is_file() else None


# =============================================================================================
# the committed fixtures are what the generator writes
# =============================================================================================
def test_committed_fixture_equals_the_generator_output(make_fixture, tmp_path):
    fresh = make_fixture.write(tmp_path / "regen")
    assert fresh, "generator wrote nothing"
    for rel, data in fresh.items():
        assert (FIXTURE_DIR / rel).read_bytes() == data, f"{rel}: committed fixture differs from the generator"
    on_disk = {p.relative_to(FIXTURE_DIR).as_posix() for p in FIXTURE_DIR.rglob("*") if p.is_file()}
    assert on_disk == set(fresh), f"unexpected or missing fixture files: {on_disk ^ set(fresh)}"


def test_fixture_is_small_and_has_no_patient_data():
    total = sum(p.stat().st_size for p in (FIXTURE_DIR / "sky").iterdir())
    assert total < 16 * 1024, f"the synthetic sky is {total} bytes"
    assert not list(FIXTURE_DIR.rglob("*.glb")) and not list(FIXTURE_DIR.rglob("*.dcm"))
    assert sum(p.stat().st_size for p in FIXTURE_DIR.rglob("*") if p.is_file()) < 100 * 1024
    assert b"Synthetic" in (FIXTURE_DIR / "sky" / "sky.json").read_bytes()


def test_reference_lists_have_the_planned_size():
    lines = [l for l in (FIXTURE_DIR / "healpix_ref_random_1000.txt").read_text().splitlines() if l and not l.startswith("#")]
    assert len(lines) == 1000


# =============================================================================================
# C# runner on the fixture and the committed references (no Python needed by the C# side)
# =============================================================================================
def test_csharp_core_on_fixture_and_committed_healpix_reference(runner):
    proc = runner("--fixture", FIXTURE_DIR, "--healpix", FIXTURE_DIR / "healpix_ref_random_1000.txt",
                  "--healpix", FIXTURE_DIR / "healpix_ref_edge.txt")
    passed, failed = _summary(proc)
    assert proc.returncode == 0 and failed == 0 and passed > 100, f"{proc.stdout}\n{proc.stderr}"
    assert "healpix_ref_random_1000.txt: 1000 directions, 0 mismatches" in proc.stdout
    assert "GREATCIRCLE octant angle sum 270.000000" in proc.stdout
    assert "FIXTURE fixture: 20/20 directions within 0.1 mm" in proc.stdout
    assert "ALLOC lookups: 0 bytes" in proc.stdout


# =============================================================================================
# P2-D08 consumer side: C# HEALPix == astropy-healpix on 1,000 seeded random directions, list made here
# =============================================================================================
def test_csharp_healpix_equals_astropy_on_1000_fresh_random_directions(runner, make_fixture, tmp_path):
    pytest.importorskip("astropy_healpix")
    lines = make_fixture.healpix_reference_lines(FRESH_SEED, 1000)
    assert len(lines) == 1000
    ref = tmp_path / "healpix_fresh_1000.txt"
    ref.write_text("# fresh list generated by tests/test_p2_b_sky_core.py\n" + "\n".join(lines) + "\n")
    proc = runner("--healpix", ref)
    passed, failed = _summary(proc)
    assert failed == 0 and proc.returncode == 0, f"{proc.stdout}\n{proc.stderr}"
    assert "healpix_fresh_1000.txt: 1000 directions, 0 mismatches against astropy-healpix" in proc.stdout


# =============================================================================================
# P2-B11 octant triangle
# =============================================================================================
def test_octant_triangle_is_270_degrees(runner):
    proc = runner()
    m = re.search(r"GREATCIRCLE octant angle sum ([0-9.]+) deg", proc.stdout)
    assert m, proc.stdout
    assert abs(float(m.group(1)) - 270.0) <= 0.5
    _, failed = _summary(proc)
    assert failed == 0, proc.stderr


# =============================================================================================
# P2-B10 on the real package
# =============================================================================================
def test_real_lung101_twenty_directions_within_0_1_mm(runner):
    sky_dir = _real_sky_dir()
    if sky_dir is None:
        pytest.skip("NOT RUN: P2-B10 real-case acceptance needs ATLAS_CASES_DIR=<cases dir holding lung101/sky/sky.json>")
    proc = runner(cases_dir=str(sky_dir.parent.parent))
    _, failed = _summary(proc)
    assert failed == 0 and proc.returncode == 0, f"{proc.stdout}\n{proc.stderr}"
    m = re.search(r"REAL-CASE lung101: (\d+)/20 directions within 0\.1 mm of sky\.json \(worst deviation ([0-9.]+) mm\)", proc.stdout)
    assert m, proc.stdout
    assert int(m.group(1)) == 20 and float(m.group(2)) <= 0.1
    print(m.group(0))


def test_real_run_without_cases_dir_says_not_run(runner):
    proc = runner(cases_dir=None)
    assert "NOT RUN: real-case acceptance" in proc.stdout
    assert "REAL-CASE" not in proc.stdout


def test_python_agrees_with_baked_sky_json_on_the_real_package():
    """Cross-check the other way: the Python baker's own pixel mapping hits the same pixels sky.json lists."""
    sky_dir = _real_sky_dir()
    if sky_dir is None:
        pytest.skip("NOT RUN: needs ATLAS_CASES_DIR")
    import json

    import numpy as np

    from atlas_pipelines import sky

    doc = json.loads((sky_dir / "sky.json").read_text())
    maps = [np.fromfile(sky_dir / f"risk_p{k}.r8", dtype=np.uint8).reshape(256, 512) for k in range(10)]
    for c in doc["checks"]:
        theta, phi = sky.vec2ang(np.array(c["dir"]))
        col, row = sky.angles_to_pixel(theta, phi)
        assert (int(col), int(row)) == (c["col"], c["row"])
        assert [int(m[row, col]) for m in maps] == c["value"]
