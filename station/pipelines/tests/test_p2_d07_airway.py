"""P2-D07 `atlas case airway`: tree.json (atlas.tree/2) and greedy.json (atlas.greedy/1).

Acceptance (Master Plan 10.2.8): 16 generations addressable; diameter ratio 0.794 +/- 0.08;
exactly 1 Two Compasses fork on the 4-fork route.
"""

from __future__ import annotations

import math
import random

import pytest

from atlas_pipelines import airway, contract, labels

RATIO = 0.794
RATIO_TOL = 0.08
FORBIDDEN_KEYS = {"parent_iso", "R_m"}
NODE_KEYS = {"address", "gen", "len_mm", "radius_mm", "pos_mm", "dir", "source"}


@pytest.fixture(scope="module")
def airway_dir(run_atlas, session_tmp_out):
    run_atlas("airway", out=session_tmp_out)
    return session_tmp_out / "cases" / "lung101" / "airway"


@pytest.fixture(scope="module")
def tree(airway_dir):
    return contract.read_json(airway_dir / "tree.json")


@pytest.fixture(scope="module")
def greedy(airway_dir):
    return contract.read_json(airway_dir / "greedy.json")


@pytest.fixture(scope="module")
def by_address(tree):
    return {n["address"]: n for n in tree["nodes"]}


def _dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def _compass_wrong_from_file(by_address, fork: str, target: str) -> bool:
    """Independent of the library: the Euclidean compass at `fork` towards `target`, from file values only."""
    f, t = by_address[fork], by_address[target]
    line = [t["pos_mm"][i] - f["pos_mm"][i] for i in range(3)]
    score = {c: _dot(by_address[fork + c]["dir"], line) for c in "LR"}
    right = target[len(fork)]
    wrong = "R" if right == "L" else "L"
    return score[wrong] > score[right]


def _all_keys(obj):
    if isinstance(obj, dict):
        for k, v in obj.items():
            yield k
            yield from _all_keys(v)
    elif isinstance(obj, list):
        for v in obj:
            yield from _all_keys(v)


# ------------------------------------------------------------------ files and schema
def test_files_are_canonical_json_under_the_real_case(airway_dir):
    for name in ("tree.json", "greedy.json"):
        data = (airway_dir / name).read_bytes()
        assert contract.is_canonical(data), name
    assert airway_dir.parent.name == "lung101"


def test_tree_schema_and_node_keys(tree, by_address):
    assert tree["schema"] == "atlas.tree/2"
    assert tree["label"] == "Model-generated airway" == labels.LABEL_AIRWAY
    assert labels.label_for(labels.KIND_REAL, "airway/tree.json") == "Model-generated airway"
    assert labels.label_for(labels.KIND_REAL, "airway/greedy.json") == "Model-generated airway"
    assert isinstance(tree["seed"], int)
    assert isinstance(tree["target"]["address"], str)
    assert "address" in tree["compass_fork"]
    g = tree["materialised_gen"]
    assert len(tree["nodes"]) == 2 ** (g + 1) - 1 == len(by_address)
    for i, n in enumerate(tree["nodes"]):
        assert set(n) == NODE_KEYS
        assert n["gen"] == len(n["address"]) and isinstance(n["gen"], int)
        assert set(n["address"]) <= {"L", "R"}
        assert airway.node_index(n["address"]) == i
        assert len(n["pos_mm"]) == 3 and len(n["dir"]) == 3
        assert abs(math.sqrt(_dot(n["dir"], n["dir"])) - 1.0) < 1e-5
        assert n["len_mm"] > 0 and n["radius_mm"] > 0


def test_every_node_source_is_model(tree):
    assert tree["nodes"]
    assert all(n["source"] == "model" for n in tree["nodes"])
    assert all(n["source"] == "model" for n in tree.get("target_route_nodes", []))


def test_no_forbidden_keys(tree, greedy):
    assert not FORBIDDEN_KEYS & set(_all_keys(tree))
    assert not FORBIDDEN_KEYS & set(_all_keys(greedy))


def test_no_clinical_wording(airway_dir):
    for name in ("tree.json", "greedy.json"):
        text = (airway_dir / name).read_text().lower()
        for word in ("patient 101", "diagnos", "treatment", "nodule", "tumour", "tumor"):
            assert word not in text, (name, word)


# ------------------------------------------------------------------ acceptance: 16 generations addressable
def test_16_generations_addressable(tree):
    seed = tree["seed"]
    assert tree["addressable_gen"] == 16
    r = airway.Resolver(seed)
    # every one of the 2^16 addresses of length 16 (and so every shorter address) resolves
    n = 0
    for a in airway.addresses_of_gen(16):
        node = r.node(a)
        assert node.gen == 16 and node.address == a
        assert node.radius_mm > 0 and node.len_mm > 0
        assert all(math.isfinite(v) for v in node.pos_mm + node.dir)
        n += 1
    assert n == 65536
    assert len(r._memo) == 2 ** 17 - 1
    # deterministic: an independent resolution of a random sample gives the identical node
    rng = random.Random(7)
    for _ in range(300):
        k = rng.randint(0, 16)
        a = "".join(rng.choice("LR") for _ in range(k))
        assert airway.resolve(seed, a) == r.node(a)


def test_resolver_rejects_non_addresses(tree):
    for bad in ("X", "LRl", "L R", "L" * 24):
        with pytest.raises(airway.AddressError):
            airway.resolve(tree["seed"], bad)
    with pytest.raises(airway.AddressError):
        airway.resolve(tree["seed"], 5)  # type: ignore[arg-type]


def test_resolver_reproduces_the_materialised_nodes(tree):
    r = airway.Resolver(tree["seed"])
    for n in tree["nodes"]:
        m = r.node(n["address"])
        assert abs(m.radius_mm - n["radius_mm"]) <= 1e-6
        assert abs(m.len_mm - n["len_mm"]) <= 1e-6
        for i in range(3):
            assert abs(m.pos_mm[i] - n["pos_mm"][i]) <= 1e-6
            assert abs(m.dir[i] - n["dir"][i]) <= 1e-6


# ------------------------------------------------------------------ acceptance: diameter ratio 0.794 +/- 0.08
def test_diameter_ratio_materialised(tree, by_address):
    ratios = [n["radius_mm"] / by_address[n["address"][:-1]]["radius_mm"] for n in tree["nodes"] if n["address"]]
    assert len(ratios) == len(tree["nodes"]) - 1 > 0
    mean = sum(ratios) / len(ratios)
    assert abs(mean - RATIO) <= RATIO_TOL
    assert all(abs(x - RATIO) <= RATIO_TOL for x in ratios), (min(ratios), max(ratios))
    print(f"diameter ratio, materialised: mean {mean:.6f}, min {min(ratios):.6f}, max {max(ratios):.6f}, n {len(ratios)}")


def test_diameter_ratio_all_16_generations(tree):
    r = airway.Resolver(tree["seed"])
    lo, hi, total, n = 9.0, 0.0, 0.0, 0
    for g in range(1, 17):
        for a in airway.addresses_of_gen(g):
            x = r.node(a).radius_mm / r.node(a[:-1]).radius_mm
            lo, hi, total, n = min(lo, x), max(hi, x), total + x, n + 1
    assert n == 2 ** 17 - 2
    assert abs(total / n - RATIO) <= RATIO_TOL
    assert abs(lo - RATIO) <= RATIO_TOL and abs(hi - RATIO) <= RATIO_TOL
    print(f"diameter ratio, generations 1..16: mean {total / n:.6f}, min {lo:.6f}, max {hi:.6f}, n {n}")


# ------------------------------------------------------------------ generator rules
def test_trachea(by_address):
    t = by_address[""]
    assert t["gen"] == 0
    assert 2 * t["radius_mm"] == pytest.approx(18.0)
    assert t["len_mm"] == pytest.approx(120.0)


def test_length_over_diameter_3_pm_1(tree):
    x = [n["len_mm"] / (2 * n["radius_mm"]) for n in tree["nodes"] if n["address"]]
    assert all(2.0 - 1e-4 <= v <= 4.0 + 1e-4 for v in x), (min(x), max(x))
    assert abs(sum(x) / len(x) - 3.0) < 0.1


def test_branch_angle_37_and_plane_rotation_90(tree, by_address):
    def cross(a, b):
        return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]

    for n in tree["nodes"]:
        a = n["address"]
        if not a:
            continue
        p = by_address[a[:-1]]
        ang = math.degrees(math.acos(max(-1.0, min(1.0, _dot(n["dir"], p["dir"])))))
        assert abs(ang - 37.0) < 1e-3
        # the branch starts where its parent ends
        for i in range(3):
            assert abs(p["pos_mm"][i] + n["len_mm"] * n["dir"][i] - n["pos_mm"][i]) < 1e-4
        if a + "L" in by_address and len(a) >= 1:
            # normal of the plane of this fork against the normal of the parent's fork
            n_here = cross(by_address[a + "L"]["dir"], by_address[a + "R"]["dir"])
            n_par = cross(by_address[a[:-1] + "L"]["dir"], by_address[a[:-1] + "R"]["dir"])
            c = _dot(n_here, n_par) / math.sqrt(_dot(n_here, n_here) * _dot(n_par, n_par))
            assert abs(math.degrees(math.acos(max(-1.0, min(1.0, c)))) - 90.0) < 1e-2


def test_first_fork_left_child_goes_left(by_address):
    assert by_address["L"]["dir"][0] > 0 > by_address["R"]["dir"][0]  # +x is left
    assert by_address["L"]["dir"][2] < 0  # and both go down


# ------------------------------------------------------------------ acceptance: exactly 1 Two Compasses fork
def test_exactly_one_two_compasses_fork_on_the_4_fork_route(tree, by_address):
    target = tree["target"]["address"]
    assert len(target) == 4, "the 4-fork route is the 4 forks from the trachea to the target"
    forks = [target[:k] for k in range(4)]
    assert tree["routes"]["four_forks"]["forks"] == forks
    wrong = [f for f in forks if _compass_wrong_from_file(by_address, f, target)]
    print(f"Two Compasses forks on the 4-fork route to {target!r}: {len(wrong)} {wrong}")
    assert len(wrong) == 1
    fork = tree["compass_fork"]["address"]
    assert fork == wrong[0]
    # the beacon (address prefix) points down the child on the route, the compass down the other one
    assert target.startswith(fork) and airway.beacon_choice(fork, target) == target[len(fork)]
    f, t = by_address[fork], by_address[target]
    compass = airway.compass_choice(f["pos_mm"], t["pos_mm"], by_address[fork + "L"]["dir"], by_address[fork + "R"]["dir"])
    assert compass in ("L", "R") and compass != airway.beacon_choice(fork, target)


def test_compass_fork_is_never_a_false_claim(tree, by_address):
    """Whatever the outcome above, the file must not name a fork at which the compass is in fact right."""
    fork, target = tree["compass_fork"]["address"], tree["target"]["address"]
    listed = tree["routes"]["four_forks"]["compass_wrong_forks"]
    if fork is None:
        assert listed != [fork] and len(listed) != 1
    else:
        assert listed == [fork]
        nodes = dict(by_address)
        nodes.update({n["address"]: n for n in tree.get("target_route_nodes", [])})
        assert _compass_wrong_from_file(nodes, fork, target)


# ------------------------------------------------------------------ greedy study
def test_greedy_study(tree, greedy):
    assert greedy["schema"] == "atlas.greedy/1"
    assert greedy["label"] == "Model-generated airway"
    assert greedy["n_targets"] == 1000
    assert isinstance(greedy["seed"], int) and greedy["tree_seed"] == tree["seed"]
    assert set(greedy["method"]) >= {"targets", "forks", "compass", "share"}
    rows = greedy["by_generation"]
    assert [r["gen"] for r in rows] == list(range(greedy["target_gen"]))
    assert sum(r["forks"] for r in rows) == greedy["forks"] == 1000 * greedy["target_gen"]
    assert sum(r["wrong_forks"] for r in rows) == greedy["wrong_forks"]
    assert greedy["wrong_share"] == pytest.approx(greedy["wrong_forks"] / greedy["forks"], abs=1e-6)
    for r in rows:
        assert r["wrong_share"] == pytest.approx(r["wrong_forks"] / r["forks"], abs=1e-6)

    # independent recount from the resolver
    res = airway.Resolver(tree["seed"])
    wrong = [0] * greedy["target_gen"]
    for i in range(1000):
        t = airway.greedy_target(greedy["seed"], i, greedy["target_gen"])
        assert len(t) == greedy["target_gen"]
        tp = res.node(t).pos_mm
        for k in range(len(t)):
            f = res.node(t[:k])
            line = [tp[j] - f.pos_mm[j] for j in range(3)]
            s = {c: _dot(res.node(t[:k] + c).dir, line) for c in "LR"}
            other = "R" if t[k] == "L" else "L"
            wrong[k] += s[other] > s[t[k]]
        res.forget(9)
    assert wrong == [r["wrong_forks"] for r in rows]
    print(f"greedy: {greedy['wrong_forks']} of {greedy['forks']} forks wrong, share {greedy['wrong_share']}")


# ------------------------------------------------------------------ determinism and cache
def test_two_runs_are_byte_identical(run_atlas, airway_dir, tmp_out):
    run_atlas("airway", out=tmp_out)
    other = tmp_out / "cases" / "lung101" / "airway"
    for name in ("tree.json", "greedy.json"):
        assert (other / name).read_bytes() == (airway_dir / name).read_bytes(), name
    before = {name: (other / name).read_bytes() for name in ("tree.json", "greedy.json")}
    again = run_atlas("airway", out=tmp_out)
    assert "up to date" in again.stdout
    forced = run_atlas("airway", "--force", out=tmp_out)
    assert "built" in forced.stdout
    assert {name: (other / name).read_bytes() for name in before} == before


def test_seed_changes_the_tree(run_atlas, airway_dir, tmp_out):
    run_atlas("airway", "--seed", "102", out=tmp_out)
    other = contract.read_json(tmp_out / "cases" / "lung101" / "airway" / "tree.json")
    assert other["seed"] == 102
    assert (tmp_out / "cases" / "lung101" / "airway" / "tree.json").read_bytes() != (airway_dir / "tree.json").read_bytes()
