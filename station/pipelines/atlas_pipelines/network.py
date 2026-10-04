"""Network Atlas generator and study (NET-01): a port of documents/network_atlas/hypernet.py.

The hyperbolic random graph of Krioukov et al. 2010 (arXiv:1006.5169), zeta = 1, with the same numerics and random
streams as the reference, so ``build(seed)`` writes the committed ``atlas.network/1`` files byte for byte
(docs/contracts/network.md sections 2 and 3.1). Equation numbers are those of the paper / design section 3.

    atlas network build --seed 7 [--n 200 --kbar 6 --gamma 2.5 --T 0 --pairs 2000] --out DIR
    atlas network bundle
"""

from __future__ import annotations

import json
import math
from collections import deque
from pathlib import Path

import numpy as np

from . import paths

SCHEMA = "atlas.network/1"
STUDY_PAIRS = 2000
GREEDY_LIMIT = 200
BUNDLED_SEEDS = (7, 83)
DEFAULTS = dict(n=200, kbar=6, gamma=2.5, T=0)


# ---------------------------------------------------------------- model (eqs. 5, 8, 13, 17, 25, 29, 31, 41)

def radius(n: int, kbar: float, gamma: float, T: float) -> tuple[float, float]:
    """(R, alpha): alpha = (gamma - 1) / 2; xi = alpha / (alpha - 1/2);
    nu = kbar sin(pi T) / (2 T xi^2), tending to pi kbar / (2 xi^2) as T -> 0; R = 2 ln(N / nu)."""
    alpha = (gamma - 1) / 2
    xi = alpha / (alpha - 0.5)
    nu = math.pi * kbar / (2 * xi * xi) if T <= 0 else kbar * math.sin(math.pi * T) / (2 * T * xi * xi)
    return 2 * math.log(n / nu), alpha


def link_probability(x, R: float, T: float):
    """Eq. 41: p(x) = 1 / (exp((x - R) / (2T)) + 1), numpy-broadcast; T > 0."""
    return 1 / (np.exp((x - R) / (2 * T)) + 1)


def distance_matrix(r, th) -> np.ndarray:
    """Hyperbolic law of cosines, eq. 5 with zeta = 1."""
    dth = np.pi - np.abs(np.pi - np.abs(th[:, None] - th[None, :]))
    c = np.cosh(r)[:, None] * np.cosh(r)[None, :] - np.sinh(r)[:, None] * np.sinh(r)[None, :] * np.cos(dth)
    return np.arccosh(np.maximum(c, 1.0))


def generate(n: int, kbar: float, gamma: float, T: float, seed: int) -> dict:
    rng = np.random.default_rng(seed)
    R, alpha = radius(n, kbar, gamma, T)
    u = rng.random(n)
    # exact inverse of the CDF (cosh(alpha r) - 1) / (cosh(alpha R) - 1), eq. 17
    r = np.arccosh(1 + u * (math.cosh(alpha * R) - 1)) / alpha
    th = rng.random(n) * 2 * math.pi
    D = distance_matrix(r, th)
    if T <= 0:
        A = D < R
    else:
        A = rng.random((n, n)) < link_probability(D, R, T)
        A = np.triu(A, 1)
        A = A | A.T
    np.fill_diagonal(A, False)
    return dict(r=r, th=th, R=R, alpha=alpha, A=A, D=D)


# ---------------------------------------------------------------- graph helpers and routing

def giant(A) -> np.ndarray:
    n = len(A)
    seen = np.full(n, -1)
    comp = 0
    best, best_size = 0, 0
    for s in range(n):
        if seen[s] >= 0:
            continue
        q = deque([s])
        seen[s] = comp
        size = 0
        while q:
            v = q.popleft()
            size += 1
            for w in np.flatnonzero(A[v]):
                if seen[w] < 0:
                    seen[w] = comp
                    q.append(w)
        if size > best_size:
            best, best_size = comp, size
        comp += 1
    return np.flatnonzero(seen == best)


def bfs(A, s: int) -> np.ndarray:
    dist = np.full(len(A), -1)
    dist[s] = 0
    q = deque([s])
    while q:
        v = q.popleft()
        for w in np.flatnonzero(A[v]):
            if dist[w] < 0:
                dist[w] = dist[v] + 1
                q.append(w)
    return dist


def greedy(A, metric, s: int, t: int, limit: int = GREEDY_LIMIT) -> tuple[list[int], bool]:
    """Greedy forwarding: step to the neighbour closest to t under metric; drop at a local minimum."""
    path = [s]
    v = s
    while v != t and len(path) < limit:
        nb = np.flatnonzero(A[v])
        if len(nb) == 0:
            return path, False
        w = nb[np.argmin(metric[nb, t])]
        if metric[w, t] >= metric[v, t]:
            return path, False
        path.append(w)
        v = w
    return path, v == t


def poincare(r, th) -> tuple[np.ndarray, np.ndarray]:
    rho = np.tanh(r / 2)
    return rho * np.cos(th), rho * np.sin(th)


def flat_matrix(r, th) -> np.ndarray:
    """Euclidean distance between the Poincare disc points (the flat picture)."""
    x, y = poincare(r, th)
    return np.hypot(x[:, None] - x[None, :], y[:, None] - y[None, :])


def route_stats(g: dict, pairs: int, seed: int) -> tuple[dict, np.ndarray]:
    """Greedy success and mean hop stretch under both rules. Pairs come from the giant component with
    default_rng(seed + 1): `pairs` for the hyperbolic rule first, then `pairs` further ones for the flat rule."""
    A, D = g["A"], g["D"]
    E = flat_matrix(g["r"], g["th"])
    comp = giant(A)
    rng = np.random.default_rng(seed + 1)
    out = {}
    for name, M in (("hyperbolic", D), ("euclidean_disk", E)):
        ok, stretch = 0, []
        for _ in range(pairs):
            s, t = rng.choice(comp, 2, replace=False)
            path, success = greedy(A, M, s, t)
            if success:
                ok += 1
                stretch.append((len(path) - 1) / bfs(A, s)[t])
        out[name] = dict(success=ok / pairs, mean_hop_stretch=float(np.mean(stretch)) if stretch else None)
    return out, comp


# ---------------------------------------------------------------- geometry for drawing

def geodesic_disk(r1, t1, r2, t2, d, k: int = 24) -> tuple[np.ndarray, np.ndarray]:
    """Points of the geodesic between two nodes, through the hyperboloid, projected to the Poincare disc."""
    P = np.array([np.sinh(r1) * np.cos(t1), np.sinh(r1) * np.sin(t1), np.cosh(r1)])
    Q = np.array([np.sinh(r2) * np.cos(t2), np.sinh(r2) * np.sin(t2), np.cosh(r2)])
    if d < 1e-9:
        return np.array([P[0] / (1 + P[2])]), np.array([P[1] / (1 + P[2])])
    s = np.linspace(0, 1, k)[:, None]
    G = (np.sinh((1 - s) * d) * P + np.sinh(s * d) * Q) / np.sinh(d)
    return G[:, 0] / (1 + G[:, 2]), G[:, 1] / (1 + G[:, 2])


def mobius(x, y, a: complex) -> tuple[np.ndarray, np.ndarray]:
    """Re-centre the disc on a: z -> (z - a) / (1 - conj(a) z)."""
    z = x + 1j * y
    w = (z - a) / (1 - np.conj(a) * z)
    return w.real, w.imag


def ccdf_slope(degrees, kmin: float) -> float:
    """Slope of the least-squares line through (ln k, ln P(k)): zero degrees dropped, k the distinct degrees
    >= kmin, P(k) the share of the remaining nodes with degree >= k (contract section 3.1.1)."""
    deg = np.asarray(degrees)
    deg = deg[deg > 0]
    ks = np.unique(deg[deg >= kmin])
    p = np.array([(deg >= k).mean() for k in ks])
    return float(np.polyfit(np.log(ks), np.log(p), 1)[0])


# ---------------------------------------------------------------- the file

def _number(x):
    """Written as an integer when whole (kbar 6, T 0), else as a float."""
    x = float(x)
    return int(x) if x.is_integer() else x


def to_document(g: dict, n: int, kbar: float, gamma: float, T: float, seed: int, pairs: int = STUDY_PAIRS) -> dict:
    A = g["A"]
    deg = A.sum(1)
    stats, _ = route_stats(g, pairs, seed)
    ii, jj = np.nonzero(np.triu(A, 1))
    return {
        "schema": SCHEMA,
        "model": {"n": int(n), "kbar": _number(kbar), "gamma": float(gamma), "T": _number(T), "seed": int(seed),
                  "zeta": 1, "R": float(g["R"])},
        "nodes": [{"id": i, "r": float(g["r"][i]), "theta": float(g["th"][i]), "degree": int(deg[i]),
                   "label": "AS-%d" % i} for i in range(n)],
        "edges": [[int(i), int(j)] for i, j in zip(ii, jj)],
        "study": {
            "pairs": int(pairs),
            "hyperbolic": {"success": float(stats["hyperbolic"]["success"]),
                           "stretch": stats["hyperbolic"]["mean_hop_stretch"]},
            "flat_picture": {"success": float(stats["euclidean_disk"]["success"]),
                             "stretch": stats["euclidean_disk"]["mean_hop_stretch"]},
        },
    }


def build(seed: int, n: int = 200, kbar: float = 6, gamma: float = 2.5, T: float = 0,
          pairs: int = STUDY_PAIRS) -> dict:
    return to_document(generate(n, kbar, gamma, T, seed), n, kbar, gamma, T, seed, pairs)


def dumps(doc: dict) -> bytes:
    """Sorted keys, no whitespace, floats as repr, UTF-8, one trailing LF (contract section 2.3)."""
    return (json.dumps(doc, sort_keys=True, separators=(",", ":"), ensure_ascii=False, allow_nan=False)
            + "\n").encode("utf-8")


def write(doc: dict, path) -> None:
    p = Path(path)
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_bytes(dumps(doc))


def load(path) -> dict:
    return json.loads(Path(path).read_text(encoding="utf-8"))


def schema_path() -> Path:
    return paths.repo_root() / "docs" / "contracts" / "network.schema.json"


def bundle_dir() -> Path:
    return paths.repo_root() / "ATLAS" / "Assets" / "StreamingAssets" / "networks"


# ---------------------------------------------------------------- validation (contract section 3.1.2)

def _is_int(v) -> bool:
    return isinstance(v, int) and not isinstance(v, bool)


def _is_num(v) -> bool:
    return isinstance(v, (int, float)) and not isinstance(v, bool)


_TYPES = {
    "object": lambda v: isinstance(v, dict),
    "array": lambda v: isinstance(v, list),
    "string": lambda v: isinstance(v, str),
    "integer": _is_int,
    "number": _is_num,
    "boolean": lambda v: isinstance(v, bool),
    "null": lambda v: v is None,
}


def _walk(v, s: dict, root: dict, path: str, errs: list[str]) -> None:
    if "$ref" in s:
        node = root
        for part in s["$ref"].lstrip("#/").split("/"):
            node = node[part]
        _walk(v, node, root, path, errs)
        return
    if "type" in s:
        types = s["type"] if isinstance(s["type"], list) else [s["type"]]
        if not any(_TYPES[t](v) for t in types):
            errs.append(f"{path}: expected {'|'.join(types)}")
            return
    if "const" in s and not (v == s["const"] and type(v) is type(s["const"])):
        errs.append(f"{path}: must equal {s['const']!r}")
    if _is_num(v):
        if "minimum" in s and v < s["minimum"]:
            errs.append(f"{path}: below minimum {s['minimum']}")
        if "maximum" in s and v > s["maximum"]:
            errs.append(f"{path}: above maximum {s['maximum']}")
        if "exclusiveMinimum" in s and v <= s["exclusiveMinimum"]:
            errs.append(f"{path}: not above {s['exclusiveMinimum']}")
        if "exclusiveMaximum" in s and v >= s["exclusiveMaximum"]:
            errs.append(f"{path}: not below {s['exclusiveMaximum']}")
    if isinstance(v, str) and "minLength" in s and len(v) < s["minLength"]:
        errs.append(f"{path}: shorter than {s['minLength']}")
    if isinstance(v, list):
        if "minItems" in s and len(v) < s["minItems"]:
            errs.append(f"{path}: fewer than {s['minItems']} items")
        if "maxItems" in s and len(v) > s["maxItems"]:
            errs.append(f"{path}: more than {s['maxItems']} items")
        if "items" in s:
            for i, item in enumerate(v):
                _walk(item, s["items"], root, f"{path}[{i}]", errs)
    if isinstance(v, dict):
        props = s.get("properties", {})
        for key in s.get("required", []):
            if key not in v:
                errs.append(f"{path}.{key}: missing" if path else f"{key}: missing")
        if s.get("additionalProperties") is False:
            for key in v:
                if key not in props:
                    errs.append(f"{path}.{key}: not allowed" if path else f"{key}: not allowed")
        for key, sub in props.items():
            if key in v:
                _walk(v[key], sub, root, f"{path}.{key}" if path else key, errs)


def validate(doc, schema: dict | None = None) -> list[str]:
    """Errors of `doc` against atlas.network/1; [] when valid."""
    if schema is None:
        schema = json.loads(schema_path().read_text(encoding="utf-8"))
    errs: list[str] = []
    _walk(doc, schema, schema, "", errs)
    if errs:
        return errs
    nodes, edges, n = doc["nodes"], doc["edges"], doc["model"]["n"]
    if len(nodes) != n:
        errs.append(f"nodes: {len(nodes)} entries, model.n is {n}")
    count = [0] * len(nodes)
    prev = None
    for k, (i, j) in enumerate(edges):
        if not i < j < len(nodes):
            errs.append(f"edges[{k}]: needs i < j < n, got [{i}, {j}]")
            continue
        if prev is not None and (i, j) <= prev:
            errs.append(f"edges[{k}]: not sorted and unique")
        prev = (i, j)
        count[i] += 1
        count[j] += 1
    for i, node in enumerate(nodes):
        if node["id"] != i:
            errs.append(f"nodes[{i}].id: expected {i}")
        elif node["degree"] != count[i]:
            errs.append(f"nodes[{i}].degree: {node['degree']} but {count[i]} edges")
    return errs


# ---------------------------------------------------------------- command line

def add_network_parser(groups) -> None:
    """`atlas network build|bundle` (called from cli.build_parser)."""
    net = groups.add_parser("network", help="Network Atlas generator", description=__doc__)
    sub = net.add_subparsers(dest="net_cmd", metavar="<command>")
    b = sub.add_parser("build", help="write DIR/network_<seed>.json")
    b.add_argument("--seed", type=int, required=True)
    b.add_argument("--n", type=int, default=DEFAULTS["n"])
    b.add_argument("--kbar", type=float, default=DEFAULTS["kbar"])
    b.add_argument("--gamma", type=float, default=DEFAULTS["gamma"])
    b.add_argument("--T", type=float, default=DEFAULTS["T"])
    b.add_argument("--pairs", type=int, default=STUDY_PAIRS)
    b.add_argument("--out", type=Path, required=True)
    sub.add_parser("bundle", help="write seeds 7 and 83 into the StreamingAssets networks folder")
    net.set_defaults(_net_parser=net)


def run_from_args(args) -> int:
    import sys

    if args.net_cmd == "build":
        doc = build(args.seed, args.n, args.kbar, args.gamma, args.T, args.pairs)
        errs = validate(doc)
        if errs:
            print("atlas network: output invalid:\n" + "\n".join(errs), file=sys.stderr)
            return 1
        out = Path(args.out) / f"network_{args.seed}.json"
        write(doc, out)
        print(out)
        return 0
    if args.net_cmd == "bundle":
        for seed in BUNDLED_SEEDS:
            out = bundle_dir() / f"network_{seed}.json"
            write(build(seed), out)
            print(out)
        return 0
    args._net_parser.print_help()
    return 2
