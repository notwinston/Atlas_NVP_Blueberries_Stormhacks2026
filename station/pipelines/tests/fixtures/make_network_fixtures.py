"""Writes the base fixtures of the Network Atlas (docs/contracts/network.md section 4) from the reference
prototype documents/network_atlas/hypernet.py, imported as a module (its main() needs matplotlib and is never run).
Run from station/pipelines/ with PYTHONPATH=. :

    python tests/fixtures/make_network_fixtures.py [output directory]

Writes, for each seed of SEEDS (gamma 2.5, N 200, kbar 6, T 0), into tests/fixtures/network/:
    network_<seed>.json            the atlas.network/1 file (design section 6.3), canonical JSON
    golden_distances_<seed>.json   500 node pairs and their hyperbolic distance (eq. 5), full precision
    golden_routes_<seed>.json      1,000 pairs of the giant component, greedy routes under both rules
    golden_poincare_<seed>.json    every node's Poincare disc point (x, y) = tanh(r/2) (cos theta, sin theta), the
                                   reference of the shared projection tests of the Console (NET-03) and Spectator (NET-04)

This script is the definition of the fixtures. atlas_pipelines/network.py (NET-01) must write network_<seed>.json
byte for byte equal to these; it is not used here, so the fixtures do not depend on the code they test.
"""

from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path

import numpy as np

from atlas_pipelines import paths

SEEDS = (7, 83)
N, KBAR, GAMMA, T = 200, 6, 2.5, 0
STUDY_PAIRS = 2000
DISTANCE_PAIRS = 500
ROUTE_PAIRS = 1000
SCHEMA = "atlas.network/1"


def dumps(doc) -> bytes:
    """The bytes of every network file (contract section 2.4): sorted keys, no whitespace, floats as Python repr
    (shortest round-trip, full double precision), UTF-8, one trailing LF. contract.dumps_canonical is not used: it
    rounds floats to 1e-6, and the golden distances must hold to 1e-9."""
    return (json.dumps(doc, sort_keys=True, separators=(",", ":"), ensure_ascii=False, allow_nan=False) + "\n").encode("utf-8")


def load_hypernet():
    path = paths.repo_root() / "documents" / "network_atlas" / "hypernet.py"
    spec = importlib.util.spec_from_file_location("hypernet", path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def number(x):
    """kbar and T are written as an integer when whole (design section 6.3: "kbar": 6, "T": 0), else as a float."""
    x = float(x)
    return int(x) if x.is_integer() else x


def network_doc(h, g, seed):
    A = g["A"]
    deg = A.sum(1)
    stats, _ = h.route_stats(g, STUDY_PAIRS, seed)
    ii, jj = np.nonzero(np.triu(A, 1))
    return {
        "schema": SCHEMA,
        "model": {"n": N, "kbar": number(KBAR), "gamma": float(GAMMA), "T": number(T), "seed": seed, "zeta": 1,
                  "R": float(g["R"])},
        "nodes": [{"id": i, "r": float(g["r"][i]), "theta": float(g["th"][i]), "degree": int(deg[i]),
                   "label": "AS-%d" % i} for i in range(N)],
        "edges": [[int(i), int(j)] for i, j in zip(ii, jj)],
        "study": {
            "pairs": STUDY_PAIRS,
            "hyperbolic": {"success": float(stats["hyperbolic"]["success"]),
                           "stretch": stats["hyperbolic"]["mean_hop_stretch"]},
            "flat_picture": {"success": float(stats["euclidean_disk"]["success"]),
                             "stretch": stats["euclidean_disk"]["mean_hop_stretch"]},
        },
    }


def flat_metric(h, g):
    x, y = h.poincare(g["r"], g["th"])
    return np.hypot(x[:, None] - x[None, :], y[:, None] - y[None, :])


def distances_doc(g, seed):
    rng = np.random.default_rng(seed + 2)
    pairs = []
    for _ in range(DISTANCE_PAIRS):
        i, j = rng.choice(N, 2, replace=False)
        pairs.append([int(i), int(j), float(g["D"][i, j])])
    return {"seed": seed, "network": "network_%d.json" % seed, "pairs": pairs}


def poincare_doc(h, g, seed):
    x, y = h.poincare(g["r"], g["th"])
    return {"seed": seed, "network": "network_%d.json" % seed, "points": [[float(a), float(b)] for a, b in zip(x, y)]}


def routes_doc(h, g, seed):
    A = g["A"]
    comp = h.giant(A)
    E = flat_metric(h, g)
    rng = np.random.default_rng(seed + 3)
    rows = []
    for _ in range(ROUTE_PAIRS):
        s, t = rng.choice(comp, 2, replace=False)
        shortest = int(h.bfs(A, s)[t])
        row = {"src": int(s), "dst": int(t), "shortest": shortest}
        for name, M in (("hyperbolic", g["D"]), ("flat", E)):
            path, ok = h.greedy(A, M, s, t)
            row[name] = {"path": [int(v) for v in path], "success": bool(ok), "hops": len(path) - 1}
        rows.append(row)
    return {"seed": seed, "network": "network_%d.json" % seed, "limit": 200, "routes": rows}


def main(argv):
    out = Path(argv[1]) if len(argv) > 1 else paths.PIPELINES_ROOT / "tests" / "fixtures" / "network"
    out.mkdir(parents=True, exist_ok=True)
    h = load_hypernet()
    for seed in SEEDS:
        g = h.generate(N, KBAR, GAMMA, T, seed)
        for name, doc in (("network_%d.json", network_doc(h, g, seed)),
                          ("golden_distances_%d.json", distances_doc(g, seed)),
                          ("golden_routes_%d.json", routes_doc(h, g, seed)),
                          ("golden_poincare_%d.json", poincare_doc(h, g, seed))):
            (out / (name % seed)).write_bytes(dumps(doc))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
