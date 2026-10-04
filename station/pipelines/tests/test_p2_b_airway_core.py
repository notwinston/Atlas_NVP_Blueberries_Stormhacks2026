"""P2-B01 / P2-B06 / P2-B08 engine-free cores of the airway wing: Atlas.Airway.Core (C#, System only) and its test runner.

Acceptance (Master Plan 10.2.8; the Editor and phone halves are NOT covered here):

    P2-B01  "32,767 nodes to depth 14 map to distinct ids; window <= 60 nodes". Here: 32,767 distinct addresses and
            step sequences and the window bound. The tile ids are Phase 1's ({4,5} ITileGraph), so "distinct ids" on
            tile ids is reported NOT RUN by the runner.
            Added by the task: the C# resolver of rule atlas.airway/2 equals tree.json on every materialised node (to
            the file's 1e-6 quantisation) and equals the Python resolver on 2,000 seeded random addresses of
            generations 11 to 16 within 1e-9 (the bit-equal count is printed).
    P2-B06  "Beacon step equals the address-prefix step on 1,000 random targets"; the 8 s rule; route cost, with the
            plan's derived number "the lumen falls below 4.2 mm at generation 7" in the symmetric model.
    P2-B08  "Compass and beacon point to different children at the designed fork", the same child at the other three
            forks of the 4-fork route; margins equal tree.json; the greedy study re-run in C# gives greedy.json's
            wrong-fork count exactly.

The C# test runner is built and run with the .NET SDK (PATH, $DOTNET_ROOT or ~/.dotnet); without one the tests are
skipped with that reason, never silently passed. tree.json and greedy.json are model-generated (no patient data);
copies are committed in tests/fixtures/p2_b_airway/ (written by make_p2_b_airway_fixture.py), so every check runs
from a clean checkout. With $ATLAS_CASES_DIR set the same checks run on ``<dir>/lung101/airway/`` of the built
package as well, and the committed copies must be byte-equal to it; without it that part is skipped as NOT RUN.
"""

from __future__ import annotations

import importlib.util
import json
import os
import re
import shutil
import subprocess
from pathlib import Path

import pytest

FIXTURE_DIR = Path(__file__).resolve().parent / "fixtures" / "p2_b_airway"
GENERATOR = Path(__file__).resolve().parent / "fixtures" / "make_p2_b_airway_fixture.py"
FRESH_SEED = 20261005  # a second list, different from the committed one, generated here at test time
FRESH_N = 2000
REF_TOLERANCE = 1e-9  # mm (radius, len, pos) and absolute (unit vectors)


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
    spec = importlib.util.spec_from_file_location("make_p2_b_airway_fixture", GENERATOR)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


@pytest.fixture(scope="module")
def runner(pipelines_root):
    """Builds Atlas.Airway.Core.Tests once; returns run(*args, cases_dir=None) -> CompletedProcess."""
    exe = _find_dotnet()
    if exe is None:
        pytest.skip("C# tests NOT RUN: no .NET SDK found on PATH, in $DOTNET_ROOT or in ~/.dotnet "
                    "(install with dotnet-install.sh --channel LTS --install-dir ~/.dotnet)")
    env = _dotnet_env(exe)
    root = pipelines_root / "csharp"
    proj = root / "Atlas.Airway.Core.Tests" / "Atlas.Airway.Core.Tests.csproj"
    build = subprocess.run([exe, "build", str(proj), "-c", "Release", "-v", "quiet"], capture_output=True, text=True, env=env)
    assert build.returncode == 0, f"dotnet build Atlas.Airway.Core.Tests failed:\n{build.stdout}\n{build.stderr}"
    found = sorted((root / "Atlas.Airway.Core.Tests" / "bin" / "Release").glob("*/Atlas.Airway.Core.Tests.dll"))
    assert found, "Atlas.Airway.Core.Tests.dll not produced"
    dll = found[0]

    def run(*args: str, cases_dir: str | None = None) -> subprocess.CompletedProcess:
        e = dict(env)
        e.pop("ATLAS_CASES_DIR", None)
        if cases_dir is not None:
            e["ATLAS_CASES_DIR"] = cases_dir
        return subprocess.run([exe, str(dll), *map(str, args)], capture_output=True, text=True, env=e)

    return run


@pytest.fixture(scope="module")
def fixture_run(runner):
    """One run on the committed copies and the committed reference, without ATLAS_CASES_DIR."""
    proc = runner("--fixture", FIXTURE_DIR, "--ref", FIXTURE_DIR / "resolver_ref_200.txt")
    passed, failed = _summary(proc)
    assert proc.returncode == 0 and failed == 0 and passed > 100, f"{proc.stdout}\n{proc.stderr}"
    return proc.stdout


def _summary(proc: subprocess.CompletedProcess) -> tuple[int, int]:
    m = re.search(r"^PASSED (\d+) FAILED (\d+)$", proc.stdout, re.M)
    assert m, f"no summary line in the runner output:\n{proc.stdout}\n{proc.stderr}"
    return int(m.group(1)), int(m.group(2))


def _real_airway_dir() -> Path | None:
    cases = os.environ.get("ATLAS_CASES_DIR")
    if not cases:
        return None
    p = Path(cases) / "lung101" / "airway"
    return p if (p / "tree.json").is_file() and (p / "greedy.json").is_file() else None


def _check_tree_lines(out: str, name: str, tree: dict, greedy: dict) -> None:
    """The acceptance numbers of one tree.json / greedy.json pair, read from the runner's report lines."""
    n_nodes = len(tree["nodes"])
    m = re.search(rf"^TREE {name}: (\d+) nodes \(generations 0\.\.(\d+)\), (\d+)/(\d+) values equal tree\.json after the "
                  rf"file's 1e-6 quantisation; worst \|C# - file\| ([0-9.E+-]+) mm \(.*\), ([0-9.E+-]+) in dir", out, re.M)
    assert m, out
    assert int(m.group(1)) == n_nodes == 2047 and int(m.group(2)) == tree["materialised_gen"]
    assert int(m.group(3)) == int(m.group(4)) == 8 * n_nodes  # radius, len, 3 pos, 3 dir of every node
    assert float(m.group(5)) <= 5.0e-7 + 1e-12 and float(m.group(6)) <= 5.0e-7 + 1e-12  # half a file step
    print(m.group(0))

    fork, target, margin = tree["compass_fork"]["address"], tree["target"]["address"], tree["compass_fork"]["margin"]
    m = re.search(rf'^COMPASS {name}: target "(\w*)", designed fork "(\w*)": compass ([LR]), beacon ([LR]), '
                  rf"margin ([0-9.-]+) \(tree\.json ([0-9.-]+)\); (\d+) fork differs, (\d+) agree: (.*)$", out, re.M)
    assert m, out
    assert m.group(1) == target and m.group(2) == fork
    assert m.group(3) != m.group(4), "compass and beacon must point to different children at the designed fork"
    assert m.group(4) == target[len(fork)]
    assert float(m.group(5)) == float(m.group(6)) == margin and margin > 0
    assert int(m.group(7)) == 1 and int(m.group(8)) == 3
    per_fork = re.findall(r'"(\w*)" compass ([LR]) beacon ([LR]) margin ([0-9.-]+)', m.group(9))
    assert [f for f, *_ in per_fork] == tree["routes"]["four_forks"]["forks"] and len(per_fork) == 4
    for f, compass, beacon, mg in per_fork:
        assert (compass != beacon) == (f == fork) == (float(mg) > 0), f"fork {f!r}"
    print(m.group(0))

    m = re.search(rf"^MARGINS {name}: (\d+) candidates reproduced, margins equal tree\.json: (.*)$", out, re.M)
    assert m, out
    got = {a: float(v) for a, v in (item.split(" ") for item in m.group(2).split(", "))}
    assert got == {c["target"]: c["margin"] for c in tree["target_choice"]["candidates"]}
    assert int(m.group(1)) == len(got)

    m = re.search(rf"^GREEDY {name}: (\d+) wrong forks of (\d+) over (\d+) targets \(greedy\.json (\d+) of (\d+)\), "
                  rf"share ([0-9.]+) \(([0-9.]+)\), routes with a wrong fork (\d+) \((\d+)\), by generation (\d+)/(\d+) equal", out, re.M)
    assert m, out
    assert int(m.group(1)) == int(m.group(4)) == greedy["wrong_forks"]
    assert int(m.group(2)) == int(m.group(5)) == greedy["forks"] == 16000
    assert int(m.group(3)) == greedy["n_targets"] == 1000
    assert float(m.group(6)) == float(m.group(7)) == greedy["wrong_share"]
    assert int(m.group(8)) == int(m.group(9)) == greedy["routes_with_wrong_fork"]
    assert int(m.group(10)) == int(m.group(11)) == 16
    print(m.group(0))


# =============================================================================================
# the committed fixtures are what the generator (the Python pipeline) writes
# =============================================================================================
def test_committed_fixture_equals_the_generator_output(make_fixture, tmp_path):
    fresh = make_fixture.write(tmp_path / "regen")
    assert set(fresh) == {"tree.json", "greedy.json", "resolver_ref_200.txt"}
    for rel, data in fresh.items():
        assert (FIXTURE_DIR / rel).read_bytes() == data, f"{rel}: committed fixture differs from the generator"
    on_disk = {p.relative_to(FIXTURE_DIR).as_posix() for p in FIXTURE_DIR.rglob("*") if p.is_file()}
    assert on_disk == set(fresh), f"unexpected or missing fixture files: {on_disk ^ set(fresh)}"


def test_fixture_is_small_and_model_generated():
    both = (FIXTURE_DIR / "tree.json").stat().st_size + (FIXTURE_DIR / "greedy.json").stat().st_size
    assert both < 400 * 1000, f"tree.json + greedy.json are {both} bytes"
    tree = json.loads((FIXTURE_DIR / "tree.json").read_text())
    assert tree["label"] == "Model-generated airway" and tree["rule"] == "atlas.airway/2"
    assert {n["source"] for n in tree["nodes"]} == {"model"}
    assert json.loads((FIXTURE_DIR / "greedy.json").read_text())["label"] == "Model-generated airway"
    lines = [l for l in (FIXTURE_DIR / "resolver_ref_200.txt").read_text().splitlines() if l and l[0] in "LR"]
    assert len(lines) == 200 and all(11 <= len(l.split(" ")[0]) <= 16 for l in lines)


# =============================================================================================
# P2-B01
# =============================================================================================
def test_p2_b01_addresses_steps_and_window(fixture_run):
    m = re.search(r"^P2-B01 addresses: (\d+) nodes to depth 14, (\d+) distinct step sequences, largest window (\d+) nodes "
                  r"\(plan bound 60\), (\d+) tiles \(plan bound 120\)$", fixture_run, re.M)
    assert m, fixture_run
    assert int(m.group(1)) == 32767 and int(m.group(2)) == 32767
    assert int(m.group(3)) <= 60 and int(m.group(4)) <= 120
    assert 'NOT RUN: P2-B01 "32,767 nodes to depth 14 map to distinct ids" on tile ids' in fixture_run
    print(m.group(0))


def test_p2_b01_resolver_equals_committed_python_reference(fixture_run):
    m = re.search(r"^REF resolver_ref_200\.txt: (\d+) addresses of generations (\d+) to (\d+), (\d+) nodes bit-equal, "
                  r"(\d+)/(\d+) doubles bit-equal, worst difference ([0-9.E+-]+) against the Python resolver$", fixture_run, re.M)
    assert m, fixture_run
    assert int(m.group(1)) == 200 and float(m.group(7)) <= REF_TOLERANCE
    print(m.group(0))


def test_p2_b01_resolver_equals_python_on_2000_fresh_random_addresses(runner, make_fixture, tmp_path):
    lines = make_fixture.reference_lines(make_fixture.TREE_SEED, FRESH_SEED, FRESH_N)
    assert len(lines) == FRESH_N + 1
    gens = {len(l.split(" ")[0]) for l in lines[1:]}
    assert gens == {11, 12, 13, 14, 15, 16}
    ref = tmp_path / "resolver_fresh_2000.txt"
    ref.write_text("# fresh list generated by tests/test_p2_b_airway_core.py\n" + "\n".join(lines) + "\n")
    proc = runner("--ref", ref)
    _, failed = _summary(proc)
    assert failed == 0 and proc.returncode == 0, f"{proc.stdout}\n{proc.stderr}"
    m = re.search(r"^REF resolver_fresh_2000\.txt: (\d+) addresses of generations 11 to 16, (\d+) nodes bit-equal, "
                  r"(\d+)/(\d+) doubles bit-equal, worst difference ([0-9.E+-]+) against the Python resolver$", proc.stdout, re.M)
    assert m, proc.stdout
    assert int(m.group(1)) == FRESH_N and int(m.group(4)) == 11 * FRESH_N
    assert float(m.group(5)) <= REF_TOLERANCE
    print(m.group(0))


def test_p2_b01_resolver_equals_committed_tree_json(fixture_run):
    tree = json.loads((FIXTURE_DIR / "tree.json").read_text())
    greedy = json.loads((FIXTURE_DIR / "greedy.json").read_text())
    _check_tree_lines(fixture_run, "fixture", tree, greedy)


# =============================================================================================
# P2-B06
# =============================================================================================
def test_p2_b06_beacon_step_equals_prefix_step_on_1000_targets(fixture_run):
    m = re.search(r"^BEACON: (\d+) targets, (\d+) walker positions on the route and (\d+) off it, (\d+) mismatches against "
                  r"the address-prefix step; (\d+) steps walked in 1,000 walks from random branches, (\d+) failures$", fixture_run, re.M)
    assert m, fixture_run
    assert int(m.group(1)) == 1000 and int(m.group(4)) == 0 and int(m.group(6)) == 0
    assert int(m.group(2)) >= 17000 and int(m.group(3)) > 0
    print(m.group(0))


def test_p2_b06_beacon_targets_are_the_python_study_targets():
    """The 1,000 targets the C# beacon test walks are the greedy study's: Python and C# derive them the same way
    (the greedy counts could not agree otherwise); here the Python beacon is checked on the same list."""
    from atlas_pipelines import airway

    for i in range(1000):
        t = airway.greedy_target(101, i)
        assert len(t) == 16
        for k in range(16):
            assert airway.beacon_choice(t[:k], t) == t[k]


def test_p2_b06_symmetric_lumen_falls_below_4_2_mm_at_generation_7(fixture_run):
    m = re.search(r"^ROUTECOST symmetric model: lumen first below 4\.2 mm at generation (\d+) \(([0-9.]+) mm\)", fixture_run, re.M)
    assert m, fixture_run
    assert int(m.group(1)) == 7
    assert 18.0 * 2.0 ** (-6 / 3) >= 4.2 > 18.0 * 2.0 ** (-7 / 3)
    assert abs(float(m.group(2)) - 18.0 * 2.0 ** (-7 / 3)) < 1e-4
    print(m.group(0))


def test_p2_b06_route_cost_of_the_four_fork_route(fixture_run):
    from atlas_pipelines import airway

    tree = json.loads((FIXTURE_DIR / "tree.json").read_text())
    target = tree["target"]["address"]
    nodes = [airway.resolve(tree["seed"], target[:k]) for k in range(len(target) + 1)]
    depth = sum(n.len_mm for n in nodes)
    sharp = sum(1 for k in range(1, len(target) + 1) if airway.branch_angle_deg(tree["seed"], target[:k]) > 45.0)
    narrow = min(2.0 * n.radius_mm for n in nodes)
    m = re.search(r'^ROUTECOST fixture: route to "(\w+)": insertion depth ([0-9.]+) mm \(file sum ([0-9.]+)\), (\d+) sharp turns '
                  r"\(> 45 deg\), narrowest lumen ([0-9.]+) mm at generation (\d+)", fixture_run, re.M)
    assert m, fixture_run
    assert m.group(1) == target
    assert abs(float(m.group(2)) - depth) <= 5e-4 and abs(float(m.group(3)) - depth) <= 5e-4
    assert int(m.group(4)) == sharp
    assert abs(float(m.group(5)) - narrow) <= 5e-4
    print(m.group(0))


def test_no_allocation_on_the_per_frame_paths(fixture_run):
    assert "ALLOC resolve: 0 bytes" in fixture_run
    assert "ALLOC navigation: 0 bytes" in fixture_run


# =============================================================================================
# P2-B01 / P2-B06 / P2-B08 on the built package
# =============================================================================================
def test_real_lung101_package(runner):
    d = _real_airway_dir()
    if d is None:
        pytest.skip("NOT RUN: the real-package acceptance needs ATLAS_CASES_DIR=<cases dir holding lung101/airway/tree.json> "
                    "(the same checks ran on the committed, byte-equal copies)")
    proc = runner("--fixture", FIXTURE_DIR, cases_dir=str(d.parent.parent))
    _, failed = _summary(proc)
    assert failed == 0 and proc.returncode == 0, f"{proc.stdout}\n{proc.stderr}"
    _check_tree_lines(proc.stdout, "lung101", json.loads((d / "tree.json").read_text()), json.loads((d / "greedy.json").read_text()))
    assert "REAL-CASE lung101: committed tree.json and greedy.json are byte-equal to the built package" in proc.stdout
    assert (d / "tree.json").read_bytes() == (FIXTURE_DIR / "tree.json").read_bytes()
    assert (d / "greedy.json").read_bytes() == (FIXTURE_DIR / "greedy.json").read_bytes()


def test_run_without_cases_dir_says_not_run(fixture_run):
    assert "NOT RUN: real-case acceptance" in fixture_run
    assert "lung101:" not in fixture_run
