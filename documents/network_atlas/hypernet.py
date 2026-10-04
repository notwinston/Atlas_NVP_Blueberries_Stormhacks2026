"""Prototype of the hyperbolic network generator (Krioukov et al. 2010, arXiv:1006.5169) for the ATLAS network
exhibit design. Generates the H^2 model, runs greedy routing, and draws the figures used in the design document.

    python hypernet.py              # figures + stats into ./figures and ./stats.json
    python hypernet.py --n 200 --kbar 6 --gamma 2.5 --T 0 --seed 7

Units: zeta = 1, so one unit of hyperbolic distance is one curvature radius. In ATLAS that is L metres
(the room's loop-fit, about 1.4 to 2.4 m).
"""
import argparse
import json
import math
import os
from collections import deque

import numpy as np


# ---------------------------------------------------------------- model (paper eqs. 5, 8, 17, 25, 29, 31, 41)

def radius(n, kbar, gamma, T):
    """Disk radius R for zeta = 1. alpha = (gamma - 1) / 2; xi = alpha / (alpha - 1/2);
    nu = kbar * sin(pi T) / (2 T xi^2), which tends to pi * kbar / (2 xi^2) as T -> 0; R = 2 ln(N / nu)."""
    alpha = (gamma - 1) / 2
    xi = alpha / (alpha - 0.5)
    nu = math.pi * kbar / (2 * xi * xi) if T <= 0 else kbar * math.sin(math.pi * T) / (2 * T * xi * xi)
    return 2 * math.log(n / nu), alpha


def generate(n, kbar, gamma, T, seed):
    rng = np.random.default_rng(seed)
    R, alpha = radius(n, kbar, gamma, T)
    u = rng.random(n)
    # exact inverse of the CDF (cosh(alpha r) - 1) / (cosh(alpha R) - 1)
    r = np.arccosh(1 + u * (math.cosh(alpha * R) - 1)) / alpha
    th = rng.random(n) * 2 * math.pi
    D = distance_matrix(r, th)
    if T <= 0:
        A = D < R
    else:
        p = 1 / (np.exp((D - R) / (2 * T)) + 1)
        A = rng.random((n, n)) < p
        A = np.triu(A, 1)
        A = A | A.T
    np.fill_diagonal(A, False)
    return dict(r=r, th=th, R=R, alpha=alpha, A=A, D=D)


def distance_matrix(r, th):
    """Hyperbolic law of cosines, eq. 5 with zeta = 1."""
    dth = np.pi - np.abs(np.pi - np.abs(th[:, None] - th[None, :]))
    c = np.cosh(r)[:, None] * np.cosh(r)[None, :] - np.sinh(r)[:, None] * np.sinh(r)[None, :] * np.cos(dth)
    return np.arccosh(np.maximum(c, 1.0))


# ---------------------------------------------------------------- graph helpers

def giant(A):
    n = len(A)
    seen = np.full(n, -1)
    comp = 0
    best, best_size = 0, 0
    for s in range(n):
        if seen[s] >= 0:
            continue
        q = deque([s]); seen[s] = comp; size = 0
        while q:
            v = q.popleft(); size += 1
            for w in np.flatnonzero(A[v]):
                if seen[w] < 0:
                    seen[w] = comp; q.append(w)
        if size > best_size:
            best, best_size = comp, size
        comp += 1
    return np.flatnonzero(seen == best)


def bfs(A, s):
    dist = np.full(len(A), -1); dist[s] = 0
    q = deque([s])
    while q:
        v = q.popleft()
        for w in np.flatnonzero(A[v]):
            if dist[w] < 0:
                dist[w] = dist[v] + 1; q.append(w)
    return dist


def greedy(A, metric, s, t, limit=200):
    """Original greedy forwarding: step to the neighbour closest to t under metric; drop at a local minimum."""
    path = [s]
    v = s
    while v != t and len(path) < limit:
        nb = np.flatnonzero(A[v])
        if len(nb) == 0:
            return path, False
        w = nb[np.argmin(metric[nb, t])]
        if metric[w, t] >= metric[v, t]:
            return path, False
        path.append(w); v = w
    return path, v == t


def poincare(r, th):
    rho = np.tanh(r / 2)
    return rho * np.cos(th), rho * np.sin(th)


def route_stats(g, pairs, seed):
    A, D = g["A"], g["D"]
    x, y = poincare(g["r"], g["th"])
    E = np.hypot(x[:, None] - x[None, :], y[:, None] - y[None, :])  # Euclidean distance in the Poincare disk
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


# ---------------------------------------------------------------- drawing

def geodesic_disk(r1, t1, r2, t2, d, k=24):
    """Points of the geodesic between two nodes, through the hyperboloid, projected to the Poincare disk."""
    P = np.array([np.sinh(r1) * np.cos(t1), np.sinh(r1) * np.sin(t1), np.cosh(r1)])
    Q = np.array([np.sinh(r2) * np.cos(t2), np.sinh(r2) * np.sin(t2), np.cosh(r2)])
    if d < 1e-9:
        return np.array([P[0] / (1 + P[2])]), np.array([P[1] / (1 + P[2])])
    s = np.linspace(0, 1, k)[:, None]
    G = (np.sinh((1 - s) * d) * P + np.sinh(s * d) * Q) / np.sinh(d)
    return G[:, 0] / (1 + G[:, 2]), G[:, 1] / (1 + G[:, 2])


def mobius(x, y, a):
    z = x + 1j * y
    w = (z - a) / (1 - np.conj(a) * z)
    return w.real, w.imag


def draw(g, route, path_png, focus=None, title=""):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    r, th, A, D = g["r"], g["th"], g["A"], g["D"]
    deg = A.sum(1)
    x, y = poincare(r, th)
    a = 0j if focus is None else complex(x[focus], y[focus])
    fig, ax = plt.subplots(figsize=(6.2, 6.2), dpi=150)
    ax.add_patch(plt.Circle((0, 0), 1, fill=False, lw=1.2, color="#5b6b7a"))
    ii, jj = np.nonzero(np.triu(A, 1))
    for i, j in zip(ii, jj):
        ex, ey = geodesic_disk(r[i], th[i], r[j], th[j], D[i, j])
        ex, ey = mobius(ex, ey, a)
        ax.plot(ex, ey, lw=0.35, color="#7fb3d5", alpha=0.55, zorder=1)
    if route:
        for i, j in zip(route[:-1], route[1:]):
            ex, ey = geodesic_disk(r[i], th[i], r[j], th[j], D[i, j])
            ex, ey = mobius(ex, ey, a)
            ax.plot(ex, ey, lw=2.2, color="#e67e22", zorder=3)
    nx, ny = mobius(x, y, a)
    ax.scatter(nx, ny, s=4 + 5 * np.sqrt(deg), c=np.log1p(deg), cmap="viridis", zorder=2, edgecolors="none")
    if route:
        ax.scatter([nx[route[0]]], [ny[route[0]]], s=70, c="#27ae60", zorder=4, label="start")
        ax.scatter([nx[route[-1]]], [ny[route[-1]]], s=70, c="#c0392b", zorder=4, label="target")
        ax.legend(loc="lower left", fontsize=8, frameon=False)
    ax.set_xlim(-1.03, 1.03); ax.set_ylim(-1.03, 1.03); ax.set_aspect("equal"); ax.axis("off")
    ax.set_title(title, fontsize=10)
    fig.tight_layout(); fig.savefig(path_png); plt.close(fig)


def draw_degrees(g, path_png, gamma):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    deg = g["A"].sum(1)
    deg = deg[deg > 0]
    ks = np.arange(1, deg.max() + 1)
    ccdf = np.array([(deg >= k).mean() for k in ks])
    fig, ax = plt.subplots(figsize=(5.2, 3.6), dpi=150)
    ax.loglog(ks, ccdf, "o", ms=3, color="#2e86c1", label="generated network")
    k0 = max(1, int(np.median(deg)))                       # anchor the reference slope at the median degree
    ref = ccdf[k0 - 1] * (ks / k0) ** (1 - gamma)
    ax.loglog(ks, ref, "--", color="#e67e22", label=f"slope 1 - gamma = {1 - gamma:.1f}")
    ax.set_xlabel("degree k"); ax.set_ylabel("P(degree >= k)")
    ax.legend(fontsize=8, frameon=False); ax.grid(alpha=0.3, which="both")
    fig.tight_layout(); fig.savefig(path_png); plt.close(fig)


# ---------------------------------------------------------------- main

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--n", type=int, default=200)
    ap.add_argument("--kbar", type=float, default=6)
    ap.add_argument("--gamma", type=float, default=2.5)
    ap.add_argument("--T", type=float, default=0.0)
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--pairs", type=int, default=2000)
    ap.add_argument("--out", default=os.path.dirname(os.path.abspath(__file__)))
    a = ap.parse_args()
    figs = os.path.join(a.out, "figures"); os.makedirs(figs, exist_ok=True)

    g = generate(a.n, a.kbar, a.gamma, a.T, a.seed)
    stats, comp = route_stats(g, a.pairs, a.seed)
    deg = g["A"].sum(1)
    # a long greedy route for the figure: from a peripheral node to the most distant peripheral node it reaches
    per = comp[np.argsort(-g["r"][comp])][:20]
    best = None
    for s in per:
        for t in per:
            if s == t:
                continue
            p, ok = greedy(g["A"], g["D"], s, t)
            if ok and (best is None or g["D"][s, t] > g["D"][best[0], best[-1]]):
                best = p
    route = [int(v) for v in best] if best else []
    draw(g, route, os.path.join(figs, "fig1_poincare_route.png"),
         title=f"N={a.n}, mean degree {deg.mean():.1f}, gamma={a.gamma}, R={g['R']:.2f}: greedy route in orange")
    hub = int(np.argmax(deg))
    focus = route[1] if len(route) > 2 else hub
    draw(g, route, os.path.join(figs, "fig2_recentred.png"), focus=focus,
         title="The same network re-centred on the first hub of the route (Mobius map)")
    draw_degrees(g, os.path.join(figs, "fig3_degrees.png"), a.gamma)

    summary = dict(
        params=dict(n=a.n, kbar=a.kbar, gamma=a.gamma, T=a.T, seed=a.seed),
        R=round(g["R"], 3), alpha=round(g["alpha"], 3),
        mean_degree=round(float(deg.mean()), 2), max_degree=int(deg.max()), hub_radius=round(float(g["r"][hub]), 3),
        giant_component=int(len(comp)), edges=int(g["A"].sum() // 2),
        route_hops=len(route) - 1 if route else None,
        route_distance=round(float(g["D"][route[0], route[-1]]), 3) if route else None,
        greedy=stats,
    )
    with open(os.path.join(a.out, "stats.json"), "w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2)
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
