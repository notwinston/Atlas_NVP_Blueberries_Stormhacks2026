"""NET-02: the network contract in CI (docs/contracts/network.md, NET-02)."""

from __future__ import annotations

import copy
import json
import math
from functools import lru_cache

import pytest

from atlas_pipelines import network as net

SEEDS = (7, 83)


def bundled(seed):
    return net.bundle_dir() / f"network_{seed}.json"


@lru_cache(maxsize=None)
def _doc(seed):
    return net.load(bundled(seed))


@pytest.mark.parametrize("seed", SEEDS)
def test_schema_accepts_bundled(seed):
    assert net.validate(_doc(seed)) == []


def _missing_key(d):
    del d["study"]


def _wrong_schema(d):
    d["schema"] = "atlas.network/2"


def _theta_too_big(d):
    d["nodes"][3]["theta"] = 2 * math.pi


def _negative_r(d):
    d["nodes"][3]["r"] = -0.5


def _edge_i_ge_j(d):
    i, j = d["edges"][5]
    d["edges"][5] = [j, i]


def _edge_i_eq_j(d):
    d["edges"][5] = [d["edges"][5][0], d["edges"][5][0]]


def _wrong_degree(d):
    d["nodes"][0]["degree"] += 1


def _extra_key(d):
    d["surprise"] = 1


def _extra_node_key(d):
    d["nodes"][0]["colour"] = "red"


BREAKS = [_missing_key, _wrong_schema, _theta_too_big, _negative_r, _edge_i_ge_j, _edge_i_eq_j,
          _wrong_degree, _extra_key, _extra_node_key]


@pytest.mark.parametrize("seed", SEEDS)
@pytest.mark.parametrize("brk", BREAKS, ids=lambda f: f.__name__.lstrip("_"))
def test_schema_rejects_broken_file(seed, brk):
    doc = copy.deepcopy(_doc(seed))
    brk(doc)
    assert net.validate(doc) != []


def test_two_seeds_bundled_with_meta():
    d = net.bundle_dir()
    for seed in SEEDS:
        assert bundled(seed).is_file()
        meta = d / f"network_{seed}.json.meta"
        assert meta.is_file()
        assert "guid:" in meta.read_text(encoding="utf-8")
        assert _doc(seed)["model"]["seed"] == seed
    assert (d.parent / "networks.meta").is_file()
    assert sorted(p.name for p in d.glob("*.json")) == sorted(f"network_{s}.json" for s in SEEDS)


@pytest.mark.parametrize("seed", SEEDS)
def test_bundled_equal_regenerated(seed):
    assert net.dumps(net.build(seed)) == bundled(seed).read_bytes()
