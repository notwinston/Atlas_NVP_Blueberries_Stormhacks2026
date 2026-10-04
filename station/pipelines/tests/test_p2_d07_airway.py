"""P2-D07 `atlas case airway`: tree.json (atlas.tree/2) and greedy.json (atlas.greedy/1).

Acceptance (Master Plan 10.2.8): 16 generations addressable; diameter ratio 0.794 +/- 0.08;
exactly 1 Two Compasses fork on the 4-fork route.
"""

from __future__ import annotations

import hashlib
import math
import random
import statistics

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


def _margin_from_file(by_address, fork: str, target: str) -> float:
    """Independent of the library: the Euclidean compass at `fork` towards `target`, from file values only.
    (score of the child off the route - score of the child on the route) / distance fork to target."""
    f, t = by_address[fork], by_address[target]
    line = [t["pos_mm"][i] - f["pos_mm"][i] for i in range(3)]
    score = {c: _dot(by_address[fork + c]["dir"], line) for c in "LR"}
    right = target[len(fork)]
    wrong = "R" if right == "L" else "L"
    return (score[wrong] - score[right]) / math.sqrt(_dot(line, line))


def _compass_wrong_from_file(by_address, fork: str, target: str) -> bool:
    return _margin_from_file(by_address, fork, target) > 0.0


def _cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def _rule_node(seed: int, address: str):
    """Rule atlas.airway/2 written out again from docs/case_contract.md 10.2, without the library."""
    def u(x):
        h = hashlib.sha256(f"atlas.airway/2|{seed}|{x}".encode("ascii")).digest()
        return [(int.from_bytes(h[8 * k:8 * k + 8], "big") >> 11) / 2.0 ** 53 for k in range(4)]

    def q(x):
        return 2.0 ** (-1.0 / 3.0) * (0.9 + 0.2 * u(x)[0])

    def mw(a, b):
        return math.acos((1.0 + a ** 4 - b ** 4) / (2.0 * a * a))

    radius, pos, d, normal = 9.0, [0.0, 0.0, -120.0], [0.0, 0.0, -1.0], [1.0, 0.0, 0.0]
    length = 120.0
    for k in range(len(address)):
        par, x = address[:k], address[:k + 1]
        sib = par + ("R" if x[-1] == "L" else "L")
        radius = radius * q(x)
        length = 2.0 * radius * (2.0 + 2.0 * u(x)[1])
        if par == "":
            rho = 90.0
        else:
            up = u(par)
            z = math.sqrt(-2.0 * math.log(1.0 - up[2])) * math.cos(2.0 * math.pi * up[3])
            rho = min(180.0, max(0.0, 90.0 + 41.6 * z))
        rho = math.radians(rho)
        turned = _cross(d, normal)
        normal = [normal[i] * math.cos(rho) + turned[i] * math.sin(rho) for i in range(3)]
        nn = math.sqrt(_dot(normal, normal))
        normal = [v / nn for v in normal]
        side = _cross(normal, d)
        theta = math.radians(37.0) + mw(q(x), q(sib)) - math.acos(2.0 ** (-1.0 / 3.0))
        sgn = 1.0 if x[-1] == "L" else -1.0
        d = [d[i] * math.cos(theta) + sgn * side[i] * math.sin(theta) for i in range(3)]
        dn = math.sqrt(_dot(d, d))
        d = [v / dn for v in d]
        pos = [pos[i] + length * d[i] for i in range(3)]
    return radius, length, pos, d


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


def test_rule_is_the_one_in_the_contract(tree):
    """tree.json against an independent re-implementation of rule atlas.airway/2 (what lane B will write)."""
    assert tree["rule"] == "atlas.airway/2" == airway.RULE
    rng = random.Random(11)
    sample = [n for n in tree["nodes"] if n["gen"] <= 5] + rng.sample(tree["nodes"], 200)
    for n in sample:
        radius, length, pos, d = _rule_node(tree["seed"], n["address"])
        assert abs(radius - n["radius_mm"]) <= 1e-6 and abs(length - n["len_mm"]) <= 1e-6
        for i in range(3):
            assert abs(pos[i] - n["pos_mm"][i]) <= 1e-6 and abs(d[i] - n["dir"][i]) <= 1e-6
    for a in ("LRLRLRLRLRLRLRLR", "RRRRRRRRRRRRRRRR", "LLRRLRRLLLRLRRLR"):
        radius, length, pos, d = _rule_node(tree["seed"], a)
        m = airway.resolve(tree["seed"], a)
        assert abs(radius - m.radius_mm) <= 1e-9 and abs(length - m.len_mm) <= 1e-9
        assert all(abs(pos[i] - m.pos_mm[i]) <= 1e-9 and abs(d[i] - m.dir[i]) <= 1e-9 for i in range(3))


def test_generator_parameters_are_the_literature_values(tree):
    g = tree["generator"]
    assert g["diameter_ratio"] == pytest.approx(2.0 ** (-1.0 / 3.0), abs=1e-6)
    assert g["length_over_diameter"] == 3.0 and g["length_over_diameter_spread"] == 1.0
    assert g["branch_angle_deg"] == 37.0 and g["branch_angle_limit_deg"] == 60.0
    assert g["plane_rotation_deg"] == 90.0 and g["plane_rotation_sd_deg"] == 41.6
    assert g["plane_rotation_clip_deg"] == [0.0, 180.0]
    assert "R2 C.4" in g["sources"]


def test_branch_angle_centred_on_37_and_below_60(tree, by_address):
    angles = []
    for n in tree["nodes"]:
        a = n["address"]
        if not a:
            continue
        p = by_address[a[:-1]]
        angles.append(math.degrees(math.acos(max(-1.0, min(1.0, _dot(n["dir"], p["dir"]))))))
        # the branch starts where its parent ends
        for i in range(3):
            assert abs(p["pos_mm"][i] + n["len_mm"] * n["dir"][i] - n["pos_mm"][i]) < 1e-4
        # the angle is the stated function of the two diameter ratios of the fork
        sib = by_address[a[:-1] + ("R" if a[-1] == "L" else "L")]
        qa, qb = n["radius_mm"] / p["radius_mm"], sib["radius_mm"] / p["radius_mm"]
        want = 37.0 + math.degrees(math.acos((1 + qa ** 4 - qb ** 4) / (2 * qa * qa)) - math.acos(2.0 ** (-1.0 / 3.0)))
        assert abs(angles[-1] - want) < 1e-3
    assert max(angles) < 60.0
    assert 11.0 <= min(angles) and max(angles) <= 49.2  # the range the +/- 10 % diameter jitter allows (10.2)
    mean = sum(angles) / len(angles)
    assert abs(mean - 37.0) < 1.0  # 37 exactly at a symmetric fork; the relation is not linear, so the mean is near it
    print(f"branching angle: mean {mean:.4f}, sd {statistics.pstdev(angles):.4f}, min {min(angles):.4f}, "
          f"max {max(angles):.4f}, n {len(angles)}")


def test_plane_rotation_mean_90_sd_from_the_measurement(tree, by_address):
    rot = []
    for n in tree["nodes"]:
        a = n["address"]
        if not a or a + "L" not in by_address:
            continue
        # both children of a fork lie in one plane with the parent's axis
        n_here = _cross(by_address[a + "L"]["dir"], by_address[a + "R"]["dir"])
        assert abs(_dot(n_here, n["dir"])) < 1e-5
        # normal of the plane of this fork against the normal of the parent's fork
        n_par = _cross(by_address[a[:-1] + "L"]["dir"], by_address[a[:-1] + "R"]["dir"])
        c = _dot(n_here, n_par) / math.sqrt(_dot(n_here, n_here) * _dot(n_par, n_par))
        r = math.degrees(math.acos(max(-1.0, min(1.0, c))))
        assert abs(r - airway.plane_rotation_deg(tree["seed"], a)) < 1e-2
        rot.append(r)
    assert len(rot) == 2 ** tree["materialised_gen"] - 2
    assert all(0.0 <= r <= 180.0 for r in rot)
    mean, sd = sum(rot) / len(rot), statistics.pstdev(rot)
    clipped = sum(1 for r in rot if r < 1e-2 or r > 180.0 - 1e-2)
    print(f"plane rotation: mean {mean:.4f}, sd {sd:.4f}, min {min(rot):.4f}, max {max(rot):.4f}, "
          f"clamped {clipped} of {len(rot)}")
    assert abs(mean - 90.0) < 5.0
    assert 35.0 < sd < 41.6 + 2.0  # the clamp to 0..180 takes a little off 41.6
    # the trachea's fork is turned exactly 90 degrees: the main bronchi are in the x-z plane
    assert airway.plane_rotation_deg(tree["seed"], "") == 90.0
    assert abs(by_address["L"]["dir"][1]) < 1e-6 and abs(by_address["R"]["dir"][1]) < 1e-6


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
    assert tree["routes"]["four_forks"]["end"] == target and tree["routes"]["four_forks"]["compass_wrong_forks"] == [fork]
    margin = _margin_from_file(by_address, fork, target)
    assert margin > 0 and tree["compass_fork"]["margin"] == pytest.approx(margin, abs=1e-5)
    print(f"target {target!r}, Two Compasses fork {fork!r}, margin {margin:.6f}, seed {tree['seed']}")


def test_target_is_the_clearest_of_the_qualifying_targets(tree, by_address):
    """Recomputed from the file: every generation-4 target with exactly one wrong fork, and the largest margin."""
    found = []
    for a in airway.addresses_of_gen(4):
        m = [(f, _margin_from_file(by_address, f, a)) for f in (a[:k] for k in range(4))]
        w = [(f, x) for f, x in m if x > 0]
        if len(w) == 1:
            found.append((a, w[0][0], w[0][1]))
    listed = tree["target_choice"]["candidates"]
    assert [(c["target"], c["compass_fork"]) for c in listed] == [(a, f) for a, f, _ in found]
    for c, (_, _, x) in zip(listed, found):
        assert c["margin"] == pytest.approx(x, abs=1e-5)
    best = max(found, key=lambda c: c[2])
    assert (tree["target"]["address"], tree["compass_fork"]["address"]) == best[:2]
    assert tree["target_choice"]["target_gen"] == 4
    # the default seed is the plan's 101 and was not searched for
    assert airway.DEFAULT_SEED == 101 == tree["seed"] and tree["target_choice"]["seed_selected"] is False
    assert airway.first_seed_with_target(101) == 101
    print(f"qualifying targets: {[(a, f, round(x, 6)) for a, f, x in found]}")


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


def test_cache_key_follows_the_library_version():
    from atlas_pipelines.steps import airway as step

    assert airway.VERSION and step.CODE_VERSION.endswith("+" + airway.VERSION)


def test_library_edit_invalidates_the_cache(run_atlas, tmp_out, monkeypatch, capsys):
    run_atlas("airway", out=tmp_out)
    assert "up to date" in run_atlas("airway", out=tmp_out).stdout
    from atlas_pipelines import cli
    from atlas_pipelines.steps import airway as step

    assert cli.main(["case", "airway", "--out", str(tmp_out)]) == 0
    assert "up to date" in capsys.readouterr().out  # same library version: still cached
    monkeypatch.setattr(airway, "VERSION", airway.VERSION + "-edited")
    monkeypatch.setattr(step, "CODE_VERSION", "airway/2+" + airway.VERSION)
    assert cli.main(["case", "airway", "--out", str(tmp_out)]) == 0
    assert "airway: built" in capsys.readouterr().out  # a bumped library version rebuilds


def test_seed_changes_the_tree(run_atlas, airway_dir, tmp_out):
    run_atlas("airway", "--seed", "102", out=tmp_out)
    other = contract.read_json(tmp_out / "cases" / "lung101" / "airway" / "tree.json")
    assert other["seed"] == 102
    assert (tmp_out / "cases" / "lung101" / "airway" / "tree.json").read_bytes() != (airway_dir / "tree.json").read_bytes()
