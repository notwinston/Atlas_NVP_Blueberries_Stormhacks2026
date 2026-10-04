"""NET-01: the Network Atlas generator and study (docs/contracts/network.md sections 2, 3.1, 4, 8, 9)."""

from __future__ import annotations

import json
import math
import subprocess
import sys
from functools import lru_cache

import numpy as np
import pytest

from atlas_pipelines import network as net

FIXTURE_SEEDS = (7, 83)
N, KBAR, GAMMA, T = 200, 6, 2.5, 0


@pytest.fixture(scope="module")
def fx(fixtures_dir):
    return fixtures_dir / "network"


def golden(fx, kind, seed):
    return json.loads((fx / f"golden_{kind}_{seed}.json").read_text(encoding="utf-8"))


@lru_cache(maxsize=None)
def gen(seed):
    return net.generate(N, KBAR, GAMMA, T, seed)


# ---------------------------------------------------------------- acceptance

@pytest.mark.xfail(strict=True, reason="contract network.md section 9 C1")
def test_mean_degree_within_10pct_over_10_seeds():
    means = [gen(s)["A"].sum(1).mean() for s in range(10)]
    assert abs(float(np.mean(means)) - KBAR) <= 0.10 * KBAR


def test_ccdf_tail_slope_within_0_3_of_1_minus_gamma():
    deg = np.concatenate([gen(s)["A"].sum(1) for s in range(10)])
    slope = net.ccdf_slope(deg, KBAR)
    assert abs(slope - (1 - GAMMA)) <= 0.3, slope


def test_hyperbolic_greedy_success_at_least_99pct():
    stats, _ = net.route_stats(gen(7), net.STUDY_PAIRS, 7)
    assert stats["hyperbolic"]["success"] >= 0.99


def test_flat_picture_greedy_success_at_most_90pct():
    stats, _ = net.route_stats(gen(7), net.STUDY_PAIRS, 7)
    assert stats["euclidean_disk"]["success"] <= 0.90


def test_output_validates_against_schema():
    doc = net.build(7)
    assert net.validate(doc) == []
    assert net.validate(json.loads(net.dumps(doc))) == []


def test_validate_reports_paths():
    doc = json.loads(net.dumps(net.build(7, pairs=20)))
    doc["nodes"][3]["r"] = -1
    doc["model"]["zeta"] = 2
    errs = net.validate(doc)
    assert "nodes[3].r: below minimum 0" in errs
    assert any(e.startswith("model.zeta") for e in errs)
    doc = json.loads(net.dumps(net.build(7, pairs=20)))
    doc["nodes"][4]["degree"] += 1
    assert any(e.startswith("nodes[4].degree") for e in net.validate(doc))
    doc = json.loads(net.dumps(net.build(7, pairs=20)))
    doc["edges"][0] = [5, 2]
    assert any(e.startswith("edges[0]") for e in net.validate(doc))


@pytest.mark.parametrize("seed", FIXTURE_SEEDS)
def test_fixture_seeds_equal_base_fixtures(fx, seed):
    assert net.dumps(net.build(seed)) == (fx / f"network_{seed}.json").read_bytes()


# ---------------------------------------------------------------- formulas (section 8)

def test_alpha_xi_nu_eqs_25_29_31():
    R, alpha = net.radius(N, KBAR, GAMMA, 0)
    assert alpha == 0.75                                  # eq. 29: gamma = 2 alpha + 1
    xi = alpha / (alpha - 0.5)                            # eq. 25
    assert xi == 3.0
    nu = math.pi * KBAR / (2 * xi * xi)                   # eq. 31 at T -> 0
    assert R == pytest.approx(2 * math.log(N / nu), abs=1e-12)
    # T > 0: nu = kbar sin(pi T) / (2 T xi^2), continuous with the T -> 0 limit
    R_small, _ = net.radius(N, KBAR, GAMMA, 1e-6)
    assert R_small == pytest.approx(R, abs=1e-6)
    R_hot, _ = net.radius(N, KBAR, GAMMA, 0.5)
    assert R_hot == pytest.approx(2 * math.log(N / (KBAR * math.sin(math.pi * 0.5) / (2 * 0.5 * xi * xi))), abs=1e-12)


def test_radius_eq13():
    R, _ = net.radius(N, KBAR, GAMMA, 0)
    assert R == 10.504399538733493
    assert gen(7)["R"] == R


def test_node_radius_inverts_cdf_eq17():
    g = gen(7)
    u = np.random.default_rng(7).random(N)
    alpha, R = g["alpha"], g["R"]
    cdf = (np.cosh(alpha * g["r"]) - 1) / (math.cosh(alpha * R) - 1)
    assert np.allclose(cdf, u, atol=1e-12)
    assert g["r"].min() >= 0 and g["r"].max() <= R


def test_angle_uniform_in_0_2pi():
    th = np.concatenate([gen(s)["th"] for s in range(10)])
    assert th.min() >= 0 and th.max() < 2 * math.pi
    x = np.sort(th) / (2 * math.pi)
    ks = np.max(np.maximum(np.arange(1, len(x) + 1) / len(x) - x, x - np.arange(len(x)) / len(x)))
    assert ks < 1.63 / math.sqrt(len(x))                  # Kolmogorov-Smirnov, 1 % level


@pytest.mark.parametrize("seed", FIXTURE_SEEDS)
def test_distance_eq5_equals_golden(fx, seed):
    g = gen(seed)
    D = net.distance_matrix(g["r"], g["th"])
    assert np.array_equal(D, g["D"])
    for i, j, d in golden(fx, "distances", seed)["pairs"]:
        assert D[i, j] == pytest.approx(d, abs=1e-9)


@pytest.mark.parametrize("seed", FIXTURE_SEEDS)
def test_links_iff_distance_below_R_eq8(seed):
    g = gen(seed)
    off = ~np.eye(N, dtype=bool)
    assert np.array_equal(g["A"], (g["D"] < g["R"]) & off)
    assert not g["A"].diagonal().any() and np.array_equal(g["A"], g["A"].T)


def test_hot_link_probability_eq41():
    R = 10.5
    for Tv in (0.2, 0.5, 0.8):
        assert net.link_probability(R, R, Tv) == pytest.approx(0.5)
        x = np.linspace(0, 2 * R, 50)
        p = net.link_probability(x, R, Tv)
        assert np.all(np.diff(p) < 0) and p.min() > 0 and p.max() < 1
        assert net.link_probability(R + 2 * Tv, R, Tv) == pytest.approx(1 / (math.e + 1))
    # generated links follow it: nearer pairs link more often, and T > 0 gives a valid symmetric graph
    g = net.generate(N, KBAR, GAMMA, 0.5, 3)
    A, D, Rg = g["A"], g["D"], g["R"]
    assert np.array_equal(A, A.T) and not A.diagonal().any()
    iu = np.triu_indices(N, 1)
    near = A[iu][D[iu] < Rg - 2].mean()
    far = A[iu][D[iu] > Rg + 2].mean()
    assert near > 0.6 > 0.3 > far


@pytest.mark.parametrize("seed", FIXTURE_SEEDS)
def test_greedy_routes_equal_golden(fx, seed):
    g = gen(seed)
    A = g["A"]
    E = net.flat_matrix(g["r"], g["th"])
    gold = golden(fx, "routes", seed)
    assert gold["limit"] == net.GREEDY_LIMIT
    for row in gold["routes"]:
        s, t = row["src"], row["dst"]
        assert int(net.bfs(A, s)[t]) == row["shortest"]
        for name, M in (("hyperbolic", g["D"]), ("flat", E)):
            path, ok = net.greedy(A, M, s, t)
            assert [int(v) for v in path] == row[name]["path"]
            assert bool(ok) == row[name]["success"]
            assert len(path) - 1 == row[name]["hops"]


@pytest.mark.parametrize("seed", FIXTURE_SEEDS)
def test_poincare_point_equals_golden(fx, seed):
    g = gen(seed)
    x, y = net.poincare(g["r"], g["th"])
    pts = np.array(golden(fx, "poincare", seed)["points"])
    assert np.allclose(x, pts[:, 0], atol=1e-12, rtol=0) and np.allclose(y, pts[:, 1], atol=1e-12, rtol=0)
    assert np.allclose(np.hypot(x, y), np.tanh(g["r"] / 2), atol=1e-15)


def _disc_distance(x, y):
    z = x + 1j * y
    num = 2 * np.abs(z[:, None] - z[None, :]) ** 2
    den = (1 - np.abs(z) ** 2)[:, None] * (1 - np.abs(z) ** 2)[None, :]
    return np.arccosh(1 + num / den)


@pytest.mark.parametrize("seed", FIXTURE_SEEDS)
def test_mobius_preserves_distance_within_R(seed):
    """Contract section 9 C3: re-centre on every node, compare the stable disc distance (section 5.2) for pairs
    whose images both stay within R of the new centre; 1e-9."""
    g = gen(seed)
    x, y = net.poincare(g["r"], g["th"])
    base = _disc_distance(x, y)
    rho_max = math.tanh(g["R"] / 2)
    worst = 0.0
    for a in range(N):
        u, v = net.mobius(x, y, complex(x[a], y[a]))
        near = np.hypot(u, v) <= rho_max
        diff = np.abs(_disc_distance(u, v) - base)[np.ix_(near, near)]
        worst = max(worst, float(diff.max()))
    assert worst <= 1e-9, worst


def test_mobius_moves_point_to_origin_and_geodesic_ends():
    g = gen(7)
    x, y = net.poincare(g["r"], g["th"])
    u, v = net.mobius(x, y, complex(x[5], y[5]))
    assert abs(u[5]) < 1e-15 and abs(v[5]) < 1e-15
    gx, gy = net.geodesic_disk(g["r"][1], g["th"][1], g["r"][2], g["th"][2], g["D"][1, 2], 17)
    assert len(gx) == 17
    assert (gx[0], gy[0]) == pytest.approx((x[1], y[1]), abs=1e-12)
    assert (gx[-1], gy[-1]) == pytest.approx((x[2], y[2]), abs=1e-12)


# ---------------------------------------------------------------- file and command line

def test_dumps_is_canonical():
    b = net.dumps({"b": 1.5, "a": [1, 2]})
    assert b == b'{"a":[1,2],"b":1.5}\n'


def test_cli_build_and_bundle_paths(tmp_path, fx):
    cmd = [sys.executable, "-m", "atlas_pipelines.cli", "network", "build", "--seed", "7", "--out", str(tmp_path)]
    proc = subprocess.run(cmd, capture_output=True, text=True)
    assert proc.returncode == 0, proc.stderr
    assert (tmp_path / "network_7.json").read_bytes() == (fx / "network_7.json").read_bytes()
    assert net.load(tmp_path / "network_7.json")["model"]["seed"] == 7
    assert net.schema_path().is_file()
    assert net.bundle_dir().name == "networks" and net.BUNDLED_SEEDS == (7, 83)
