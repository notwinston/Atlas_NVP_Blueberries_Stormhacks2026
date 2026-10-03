#!/usr/bin/env python3
"""Generate the geometry figures for ATLAS_Master_Plan.md.

Run:  python3 make_figures.py [output_dir]
Needs: numpy, matplotlib. Every number drawn here is computed, not typed in.
"""
import math
import sys
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
from matplotlib.patches import Circle, Polygon, Rectangle

OUT = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).parent
OUT.mkdir(parents=True, exist_ok=True)

# ---------- palette (one look for every figure) ----------
INK = "#1d2433"
MUTED = "#7a8499"
GRID = "#c9d0dc"
BG = "#ffffff"
HYP = "#7b4fd6"      # hyperbolic
FLAT = "#2f8f6b"     # Euclidean
SPH = "#d9722b"      # spherical
ACCENT = "#d43d51"   # paths, warnings
BLUE = "#2b6fd9"
SOFT = "#eef1f6"

plt.rcParams.update({
    "font.family": "DejaVu Sans",
    "font.size": 10,
    "axes.edgecolor": INK,
    "axes.labelcolor": INK,
    "xtick.color": INK,
    "ytick.color": INK,
    "text.color": INK,
    "figure.facecolor": BG,
    "axes.facecolor": BG,
    "savefig.facecolor": BG,
})

# ---------- hyperbolic helpers (hyperboloid model, metric diag(1,1,-1)) ----------
ORIGIN = np.array([0.0, 0.0, 1.0])


def boost(dx, dy):
    """Translation of H^2 by the tangent vector (dx, dy) at the origin."""
    d = math.hypot(dx, dy)
    if d < 1e-12:
        return np.eye(3)
    ux, uy = dx / d, dy / d
    ch, sh = math.cosh(d), math.sinh(d)
    return np.array([
        [1 + (ch - 1) * ux * ux, (ch - 1) * ux * uy, sh * ux],
        [(ch - 1) * ux * uy, 1 + (ch - 1) * uy * uy, sh * uy],
        [sh * ux, sh * uy, ch],
    ])


def rot(t):
    c, s = math.cos(t), math.sin(t)
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1.0]])


def mdot(p, q):
    return p[0] * q[0] + p[1] * q[1] - p[2] * q[2]


def hnorm(v):
    return v / math.sqrt(-mdot(v, v))


def geodesic(p, q, n=48):
    ts = np.linspace(0, 1, n)
    return np.array([hnorm((1 - t) * p + t * q) for t in ts])


def poincare(P):
    P = np.atleast_2d(P)
    return P[:, :2] / (1 + P[:, 2:3])


def logmap(P):
    """Azimuthal-equidistant chart: true geodesic distance and direction from the origin."""
    P = np.atleast_2d(P)
    r = np.arccosh(np.clip(P[:, 2], 1, None))
    s = np.hypot(P[:, 0], P[:, 1])
    k = np.where(s > 1e-12, r / np.where(s > 1e-12, s, 1), 1.0)
    return P[:, :2] * k[:, None]


def tiling_consts(p, q):
    half_edge = math.acosh(math.cos(math.pi / p) / math.sin(math.pi / q))
    inradius = math.acosh(math.cos(math.pi / q) / math.sin(math.pi / p))
    circum = math.acosh(1 / (math.tan(math.pi / p) * math.tan(math.pi / q)))
    area = math.pi * (p - 2) - p * 2 * math.pi / q
    return half_edge, inradius, circum, area


def build_tiling(p, q, max_dist, phase=None):
    """Breadth-first {p,q} tiling: list of isometries taking the central tile to each tile."""
    _, r_in, _, _ = tiling_consts(p, q)
    if phase is None:
        phase = 0.0
    # The neighbour across edge k is the half-turn about that edge's midpoint:
    # translate by 2*r_in towards the edge, then rotate by pi. (For even p the
    # rotation is a symmetry of the tile; for odd p it is required.)
    gens = [boost(2 * r_in * math.cos(phase + 2 * math.pi * k / p),
                  2 * r_in * math.sin(phase + 2 * math.pi * k / p)) @ rot(math.pi) for k in range(p)]
    tiles, seen, frontier = [np.eye(3)], {(0, 0)}, [np.eye(3)]
    while frontier:
        nxt = []
        for M in frontier:
            for G in gens:
                N = M @ G
                c = N @ ORIGIN
                if math.acosh(max(c[2], 1.0)) > max_dist:
                    continue
                key = (round(c[0], 3), round(c[1], 3))
                if key in seen:
                    continue
                seen.add(key)
                tiles.append(N)
                nxt.append(N)
        if len(tiles) > 20000:
            raise RuntimeError(f"tiling {{{p},{q}}} did not close up: {len(tiles)} tiles (generator bug)")
        frontier = nxt
    return tiles


def tile_outline(M, p, q, chart=poincare, phase=None, n=24):
    _, _, R, _ = tiling_consts(p, q)
    if phase is None:
        phase = 0.0
    vs = [M @ boost(R * math.cos(phase + math.pi / p + 2 * math.pi * k / p),
                    R * math.sin(phase + math.pi / p + 2 * math.pi * k / p)) @ ORIGIN for k in range(p)]
    pts = []
    for k in range(p):
        pts.append(chart(geodesic(vs[k], vs[(k + 1) % p], n)))
    return np.vstack(pts)


def right_angle_walk(step, n):
    """Walk `step`, turn left 90 degrees, n times. Returns hyperboloid points and the final frame."""
    C = np.eye(3)
    pts = [C @ ORIGIN]
    for _ in range(n):
        C = C @ boost(step, 0.0)
        pts.append(C @ ORIGIN)
        C = C @ rot(math.pi / 2)
    return pts, C


def clean(ax):
    ax.set_aspect("equal")
    ax.axis("off")


# =====================================================================
# Figure 1: the 3-4-5 corner walk
# =====================================================================
def fig_345():
    fig, axes = plt.subplots(1, 3, figsize=(12.6, 4.9))
    s5 = 2 * math.acosh(math.sqrt(2) * math.cos(math.pi / 5))  # 1.0613

    # --- sphere: octant triangle, orthographic view ---
    ax = axes[0]
    clean(ax)
    A, B, Cn = np.array([1.0, 0, 0]), np.array([0, -1.0, 0]), np.array([0, 0, 1.0])
    w_ax = (A + B + Cn) / math.sqrt(3)                 # towards the viewer: the triangle's centroid
    v_ax = Cn - (Cn @ w_ax) * w_ax
    v_ax /= np.linalg.norm(v_ax)                        # screen up: the pole vertex sits on top
    u_ax = np.cross(v_ax, w_ax)

    def view(P):
        P = np.atleast_2d(P)
        return np.c_[P @ u_ax, P @ v_ax], -(P @ w_ax)   # screen xy, depth (< 0 = facing the viewer)

    ax.add_patch(Circle((0, 0), 1, fc="#fbefe5", ec=SPH, lw=1.6))
    tt = np.linspace(0, 2 * math.pi, 240)
    for lat in (-60, -30, 0, 30, 60):
        c = math.cos(math.radians(lat))
        P = np.c_[c * np.cos(tt), c * np.sin(tt), np.full_like(tt, math.sin(math.radians(lat)))]
        xy, depth = view(P)
        xy[depth > 0] = np.nan
        ax.plot(xy[:, 0], xy[:, 1], color=SPH, lw=0.5, alpha=0.35)
    for lon in range(0, 180, 30):
        l = math.radians(lon)
        P = np.c_[np.cos(tt) * math.cos(l), np.cos(tt) * math.sin(l), np.sin(tt)]
        xy, depth = view(P)
        xy[depth > 0] = np.nan
        ax.plot(xy[:, 0], xy[:, 1], color=SPH, lw=0.5, alpha=0.35)
    def arc(P, Q, n=60):
        ts = np.linspace(0, 1, n)
        pts = np.array([(1 - t) * P + t * Q for t in ts])
        return pts / np.linalg.norm(pts, axis=1)[:, None]

    tri = np.vstack([arc(B, A), arc(A, Cn), arc(Cn, B)])
    xy, _ = view(tri)
    ax.add_patch(Polygon(xy, closed=True, fc=SPH, alpha=0.18, ec="none"))
    ax.plot(xy[:, 0], xy[:, 1], color=ACCENT, lw=2.6)
    for P in (A, B, Cn):
        xy1, _ = view(P)
        ax.plot(*xy1[0], "o", color=ACCENT, ms=6)
    ax.set_xlim(-1.25, 1.25)
    ax.set_ylim(-1.4, 1.3)
    ax.set_title("Sphere  (K = +1/L²)", color=SPH, fontsize=13, fontweight="bold")
    ax.text(0, -1.27, "closes after 3 corners\ns = (π/2)·L   →   L = 2s/π", ha="center", va="center", fontsize=10.5)

    # --- flat: square ---
    ax = axes[1]
    clean(ax)
    for g in np.arange(-1.0, 1.01, 0.25):
        ax.plot([-1, 1], [g, g], color=FLAT, lw=0.5, alpha=0.3)
        ax.plot([g, g], [-1, 1], color=FLAT, lw=0.5, alpha=0.3)
    ax.add_patch(Rectangle((-1, -1), 2, 2, fc="#eaf5f0", ec=FLAT, lw=1.6, zorder=0))
    sq = np.array([[-0.6, -0.6], [0.6, -0.6], [0.6, 0.6], [-0.6, 0.6], [-0.6, -0.6]])
    ax.add_patch(Polygon(sq, closed=True, fc=FLAT, alpha=0.18, ec="none"))
    ax.plot(sq[:, 0], sq[:, 1], color=ACCENT, lw=2.6)
    ax.plot(sq[:-1, 0], sq[:-1, 1], "o", color=ACCENT, ms=6)
    ax.annotate("", xy=(0.25, -0.6), xytext=(-0.25, -0.6),
                arrowprops=dict(arrowstyle="-|>", color=ACCENT, lw=2.2, mutation_scale=16))
    ax.text(0, -0.76, "s", ha="center", va="center", color=ACCENT, fontsize=12, style="italic")
    ax.set_xlim(-1.25, 1.25)
    ax.set_ylim(-1.4, 1.3)
    ax.set_title("Flat  (K = 0)", color=FLAT, fontsize=13, fontweight="bold")
    ax.text(0, -1.27, "closes after 4 corners\nany s", ha="center", va="center", fontsize=10.5)

    # --- hyperbolic: right-angled pentagon in the Poincare disc ---
    ax = axes[2]
    clean(ax)
    ax.add_patch(Circle((0, 0), 1, fc="#f2edfb", ec=HYP, lw=1.6))
    for M in build_tiling(5, 4, 3.6, phase=math.pi / 2):
        o = tile_outline(M, 5, 4, phase=math.pi / 2 - math.pi / 5 + math.pi / 5)
        ax.plot(o[:, 0], o[:, 1], color=HYP, lw=0.5, alpha=0.4)
    o = tile_outline(np.eye(3), 5, 4, phase=math.pi / 2)
    ax.add_patch(Polygon(o, closed=True, fc=HYP, alpha=0.2, ec="none"))
    ax.plot(np.r_[o[:, 0], o[0, 0]], np.r_[o[:, 1], o[0, 1]], color=ACCENT, lw=2.6)
    _, _, R5, _ = tiling_consts(5, 4)
    for k in range(5):
        a = math.pi / 2 + math.pi / 5 + 2 * math.pi * k / 5
        v = poincare(boost(R5 * math.cos(a), R5 * math.sin(a)) @ ORIGIN)[0]
        ax.plot(*v, "o", color=ACCENT, ms=6)
    ax.set_xlim(-1.25, 1.25)
    ax.set_ylim(-1.4, 1.3)
    ax.set_title("Hyperbolic  (K = −1/L²)", color=HYP, fontsize=13, fontweight="bold")
    ax.text(0, -1.27, f"closes after 5 corners\ns = {s5:.4f}·L   →   L = s/{s5:.4f}", ha="center", va="center", fontsize=10.5)

    fig.suptitle("One instruction, three universes:  walk s, turn left 90°, repeat", fontsize=14, fontweight="bold", y=0.99)
    fig.tight_layout(rect=(0, 0, 1, 0.95))
    fig.savefig(OUT / "fig01_345_corner_walk.png", dpi=200, bbox_inches="tight", pad_inches=0.18)
    plt.close(fig)


# =====================================================================
# Figure 2: {4,5} tiling, tile zero, cull radii and the five-corner loop
# =====================================================================
def fig_tiling():
    fig, ax = plt.subplots(figsize=(8.6, 8.6))
    clean(ax)
    ax.add_patch(Circle((0, 0), 1, fc="#f7f4fd", ec=INK, lw=1.4))
    tiles = build_tiling(4, 5, 5.2)
    h_edge, r_in, R, area = tiling_consts(4, 5)
    for M in tiles:
        o = tile_outline(M, 4, 5)
        c = M @ ORIGIN
        d = math.acosh(max(c[2], 1.0))
        if d < 1e-6:
            continue
        ax.plot(np.r_[o[:, 0], o[0, 0]], np.r_[o[:, 1], o[0, 1]], color=HYP, lw=0.55, alpha=0.55)
    # first ring tint
    for M in tiles:
        c = M @ ORIGIN
        d = math.acosh(max(c[2], 1.0))
        if 1e-6 < d < 2 * r_in + 1e-6:
            ax.add_patch(Polygon(tile_outline(M, 4, 5), closed=True, fc=HYP, alpha=0.10, ec="none"))
    o = tile_outline(np.eye(3), 4, 5)
    ax.add_patch(Polygon(o, closed=True, fc=FLAT, alpha=0.35, ec=FLAT, lw=1.8))
    ax.text(-0.125, -0.15, "tile zero\n= your room", ha="center", va="center", fontsize=9.5, fontweight="bold", color="#14543d")

    # cull / fog radii with exact tile counts from the BFS
    def count(rho):
        return sum(1 for M in tiles if math.acosh(max((M @ ORIGIN)[2], 1.0)) <= rho + 1e-9)

    for rho, col, lab_ang in ((2.0, BLUE, 142), (3.0, ACCENT, 90), (4.0, MUTED, 38)):
        rr = math.tanh(rho / 2)
        ax.add_patch(Circle((0, 0), rr, fc="none", ec=col, lw=1.5, ls=(0, (5, 3))))
        a = math.radians(lab_ang)
        ax.annotate(f"ρ = {rho:g}:  {count(rho)} tiles",
                    xy=(rr * math.cos(a), rr * math.sin(a)),
                    xytext=(1.20 * math.cos(a), 1.13 * math.sin(a)),
                    ha="center", fontsize=9.5, color=col, fontweight="bold",
                    arrowprops=dict(arrowstyle="-", color=col, lw=1.0))

    # the five-corner loop: walk 2*r_in (centre to centre), turn left 90 deg, five times
    pts, Cend = right_angle_walk(2 * r_in, 5)
    path = np.vstack([poincare(geodesic(pts[i], pts[i + 1], 40)) for i in range(5)])
    ax.plot(path[:, 0], path[:, 1], color=ACCENT, lw=2.8, zorder=5)
    for i, P in enumerate(pts[:5]):
        xy = poincare(P)[0]
        ax.plot(*xy, "o", color=ACCENT, ms=7, zorder=6)
        dx, dy = (0.035, -0.05) if i else (-0.03, 0.035)
        ax.text(xy[0] + dx, xy[1] + dy, str(i) if i else "start = end", color=ACCENT, fontsize=9.5, fontweight="bold",
                ha="left" if i else "right", zorder=7)
    closure = np.abs(Cend - np.eye(3)).max()

    ax.set_xlim(-1.22, 1.22)
    ax.set_ylim(-1.30, 1.24)
    ax.set_title("{4,5} tiling in the Poincaré disc: five squares meet at every corner", fontsize=13, fontweight="bold")
    ax.text(0, -1.17,
            f"Unit L = 1:  edge {2 * h_edge:.4f}   in-radius {r_in:.4f}   centre-to-centre {2 * r_in:.4f}   "
            f"tile area 2π/5 = {area:.4f}  (72° of holonomy)\n"
            f"Red path: walk {2 * r_in:.4f}·L, turn left 90°, five times. It visits the five tiles around one corner and closes "
            f"(closure error {closure:.0e}).",
            ha="center", va="center", fontsize=9.3)
    fig.tight_layout()
    fig.savefig(OUT / "fig02_tiling_45.png", dpi=200, bbox_inches="tight", pad_inches=0.18)
    plt.close(fig)
    return {rho: count(rho) for rho in (2.0, 3.0, 3.5, 4.0, 5.0)}


# =====================================================================
# Figure 3: Mode 1 (true curved walk) vs Mode 2 (aligned cells)
# =====================================================================
ROOM = 3.0            # example real room, wall to wall (m)
MARGIN = 0.5          # the lap square sits 0.5 m inside the walls (loop-fit rule)
W_FREE = ROOM - 2 * MARGIN   # lap square side s = width of tile zero (m)


def fig_modes():
    fig, axes = plt.subplots(1, 2, figsize=(13.6, 6.9))
    h_edge, r_in, R, area = tiling_consts(4, 5)

    # ---- Mode 1: the floor as the log-map shader lays it out around the walker (metres) ----
    ax = axes[0]
    L = W_FREE / (2 * r_in)            # five-corner rule: L = W_free / 1.0613
    rho_fog = 3.0
    fog = Circle((0, 0), rho_fog * L, fc="#faf8fe", ec=ACCENT, lw=1.5, ls=(0, (5, 3)), zorder=0)
    ax.add_patch(fog)
    tiles = build_tiling(4, 5, 3.9)
    n_in = 0
    for M in tiles:
        c = M @ ORIGIN
        d = math.acosh(max(c[2], 1.0))
        n_in += d <= rho_fog + 1e-9
        o = tile_outline(M, 4, 5, chart=logmap, n=30) * L
        (ln,) = ax.plot(np.r_[o[:, 0], o[0, 0]], np.r_[o[:, 1], o[0, 1]], color=HYP, lw=0.95,
                        alpha=max(0.10, 0.95 - 0.27 * d), zorder=1)
        ln.set_clip_path(fog)
    o = tile_outline(np.eye(3), 4, 5, chart=logmap, n=30) * L
    ax.add_patch(Polygon(o, closed=True, fc=FLAT, alpha=0.30, ec=FLAT, lw=1.9, zorder=2))
    ax.add_patch(Rectangle((-ROOM / 2, -ROOM / 2), ROOM, ROOM, fc="none", ec=INK, lw=1.7, ls=":", zorder=3))
    ax.annotate(f"real {ROOM:g} × {ROOM:g} m room", xy=(ROOM / 2, ROOM / 2), xytext=(2.7, 3.9), fontsize=9.3,
                arrowprops=dict(arrowstyle="-", color=INK, lw=0.9), zorder=6,
                bbox=dict(boxstyle="round,pad=0.2", fc="white", ec="none", alpha=0.9))
    ax.annotate(f"tile zero\n(lap square, s = {W_FREE:g} m)", xy=(-0.45, -0.6), xytext=(-4.9, -4.2), fontsize=9.3,
                color="#14543d", fontweight="bold", arrowprops=dict(arrowstyle="-", color=FLAT, lw=0.9), zorder=6,
                bbox=dict(boxstyle="round,pad=0.2", fc="white", ec="none", alpha=0.9))
    ax.plot(0, 0, marker="o", color=INK, ms=7, zorder=5)
    ax.text(0.16, 0.16, "you", fontsize=9.5, fontweight="bold", zorder=5)
    ax.text(0, rho_fog * L + 0.22, f"fog opaque at ρ = {rho_fog:g}  ({rho_fog * L:.1f} m):  {n_in} tiles drawn",
            ha="center", color=ACCENT, fontsize=9.6, fontweight="bold")
    lim = rho_fog * L + 0.9
    ax.set_xlim(-lim, lim)
    ax.set_ylim(-lim, lim)
    ax.set_aspect("equal")
    ax.set_xlabel("metres from your head (true geodesic distance and bearing)")
    ax.tick_params(length=3)
    for sp in ("top", "right"):
        ax.spines[sp].set_visible(False)
    ax.set_title("Mode 1 · true curved walk\nsmooth H²×E, log-map warp", fontsize=12, fontweight="bold", color=HYP)
    fig.text(0.27, 0.005,
             f"L = s / 1.0613 = {L:.2f} m.  Straight walls look bowed and corners recede.\n"
             "The layout re-centres on you every frame, so it cannot stay glued to the real room.",
             ha="center", va="bottom", fontsize=9.2)

    # ---- Mode 2: aligned cells ----
    ax = axes[1]
    clean(ax)
    c, cols, rows = 1.0, 3, 2
    for r in range(rows):
        for q in range(cols):
            ax.add_patch(Rectangle((q * c, r * c), c, c, fc=SOFT, ec=INK, lw=1.6))
    X = (1.0, 1.0)
    ax.plot(*X, "o", color=ACCENT, ms=11, zorder=5)
    ax.text(X[0] + 0.06, X[1] + 0.06, "X", color=ACCENT, fontsize=13, fontweight="bold", zorder=6)
    ax.plot(2.0, 1.0, "o", color=BLUE, ms=9, zorder=5)
    ax.text(2.06, 1.06, "Y", color=BLUE, fontsize=12, fontweight="bold")
    # the lap around X, drawn as a rounded square of arrows
    loop = [(0.5, 0.5), (1.5, 0.5), (1.5, 1.5), (0.5, 1.5), (0.5, 0.5)]
    for i in range(4):
        ax.annotate("", xy=loop[i + 1], xytext=loop[i],
                    arrowprops=dict(arrowstyle="-|>", color=ACCENT, lw=2.4, mutation_scale=18, shrinkA=13, shrinkB=13))
    corner_lab = [((0.34, 0.19), "tile 1,\nthen tile 5"), ((1.72, 0.19), "tile 2"), ((1.72, 1.81), "tile 3"), ((0.28, 1.81), "tile 4")]
    for (x, y), lab in corner_lab:
        ax.text(x, y, lab, ha="center", va="center", fontsize=9.6, color=HYP, fontweight="bold")
    for (x, y), lab in (((2.5, 0.5), "tile 6"), ((2.5, 1.5), "tile 7")):
        ax.text(x, y, lab, ha="center", va="center", fontsize=9.6, color=MUTED, fontweight="bold")
    ax.add_patch(Rectangle((0, 0), cols * c, rows * c, fc="none", ec=INK, lw=2.8))
    ax.text(cols * c / 2, rows * c + 0.09, "thick outline = real walkable floor, cut into square cells",
            ha="center", fontsize=9.3)
    ax.set_xlim(-0.3, cols * c + 0.3)
    ax.set_ylim(-0.98, rows * c + 0.32)
    ax.set_title("Mode 2 · aligned cells\nflat tiles locked to the real floor", fontsize=12, fontweight="bold", color=BLUE)
    ax.text(cols * c / 2, -0.50,
            "Four cells meet at X on the real floor, but five tiles meet there in the virtual world,\n"
            "so one lap around X leaves you in a new tile. Laps around X and Y in turn reach every tile.\n"
            "Each tile is flat inside; all the curvature sits at the corners (5 × 90° = 450° instead of 360°).\n"
            "Walls and furniture stay where they really are.",
            ha="center", va="center", fontsize=9.2)
    fig.tight_layout(rect=(0, 0.07, 1, 1))
    fig.savefig(OUT / "fig03_two_modes.png", dpi=200, bbox_inches="tight", pad_inches=0.18)
    plt.close(fig)
    return L


# =====================================================================
# Figure 6: the rooms-per-corner dial (q = 3, 4, 5, 6) with the room as the tile
# =====================================================================
def sboost(dx, dy):
    """Rotation of the unit sphere taking the pole towards the tangent vector (dx, dy)."""
    d = math.hypot(dx, dy)
    if d < 1e-12:
        return np.eye(3)
    ux, uy = dx / d, dy / d
    c, s = math.cos(d), math.sin(d)
    return np.array([
        [1 + (c - 1) * ux * ux, (c - 1) * ux * uy, s * ux],
        [(c - 1) * ux * uy, 1 + (c - 1) * uy * uy, s * uy],
        [-s * ux, -s * uy, c],
    ])


def s_logmap(P):
    P = np.atleast_2d(P)
    r = np.arccos(np.clip(P[:, 2], -1, 1))
    s = np.hypot(P[:, 0], P[:, 1])
    k = np.where(s > 1e-12, r / np.where(s > 1e-12, s, 1), 1.0)
    return P[:, :2] * k[:, None]


def fig_dial():
    fig, axes = plt.subplots(1, 4, figsize=(15.2, 4.5))
    view = 4.2  # metres shown each side

    def frame(ax, title, sub, col):
        clip = Rectangle((-view, -view), 2 * view, 2 * view, fc="none", ec="none")
        ax.add_patch(clip)
        ax.add_patch(Rectangle((-ROOM / 2, -ROOM / 2), ROOM, ROOM, fc="none", ec=INK, lw=1.3, ls=":", zorder=4))
        ax.plot(0, 0, "o", color=INK, ms=5, zorder=5)
        ax.set_xlim(-view, view)
        ax.set_ylim(-view, view)
        ax.set_aspect("equal")
        ax.set_xticks([-4, -2, 0, 2, 4])
        ax.set_yticks([-4, -2, 0, 2, 4])
        ax.tick_params(length=2, labelsize=8)
        for sp in ("top", "right"):
            ax.spines[sp].set_visible(False)
        ax.set_title(title, fontsize=12, fontweight="bold", color=col)
        ax.set_xlabel(sub, fontsize=9.2)
        return clip

    # q = 3: sphere, {4,3}
    ax = axes[0]
    Rs = 2 * W_FREE / math.pi
    r_in_s = math.pi / 4
    R_s = math.acos(1 / (math.tan(math.pi / 4) * math.tan(math.pi / 3)))
    clip = frame(ax, "q = 3 · sphere", f"R = 2·s/π = {Rs:.2f} m\nloop closes in 3 corners · 6 rooms in total", SPH)
    gens = [sboost(2 * r_in_s * math.cos(k * math.pi / 2), 2 * r_in_s * math.sin(k * math.pi / 2)) @ rot(math.pi) for k in range(4)]
    faces = [np.eye(3)] + gens
    pole = np.array([0.0, 0.0, 1.0])
    for i, M in enumerate(faces):
        vs = [M @ sboost(R_s * math.cos(math.pi / 4 + k * math.pi / 2), R_s * math.sin(math.pi / 4 + k * math.pi / 2)) @ pole for k in range(4)]
        pts = []
        for k in range(4):
            ts = np.linspace(0, 1, 40)
            seg = np.array([(1 - t) * vs[k] + t * vs[(k + 1) % 4] for t in ts])
            seg /= np.linalg.norm(seg, axis=1)[:, None]
            pts.append(s_logmap(seg) * Rs)
        o = np.vstack(pts)
        if i == 0:
            ax.add_patch(Polygon(o, closed=True, fc=FLAT, alpha=0.30, ec=FLAT, lw=1.8, zorder=2))
        else:
            (ln,) = ax.plot(np.r_[o[:, 0], o[0, 0]], np.r_[o[:, 1], o[0, 1]], color=SPH, lw=1.2, zorder=1)
            ln.set_clip_path(clip)
    ax.add_patch(Circle((0, 0), math.pi * Rs, fc="none", ec=SPH, lw=1.0, ls=(0, (2, 3))))
    ax.text(4.05, 4.0, "far side\n(antipode)", ha="right", va="top", fontsize=7.8, color=SPH)

    # q = 4: flat
    ax = axes[1]
    clip = frame(ax, "q = 4 · flat", "any L\nloop closes in 4 corners · the glued-room cover", FLAT)
    for i in range(-3, 4):
        for j in range(-3, 4):
            ax.add_patch(Rectangle((i * W_FREE - W_FREE / 2, j * W_FREE - W_FREE / 2), W_FREE, W_FREE,
                                   fc=FLAT if (i, j) == (0, 0) else "none", alpha=0.30 if (i, j) == (0, 0) else 1.0,
                                   ec=FLAT, lw=1.2 if (i, j) != (0, 0) else 1.8, zorder=1))

    # q = 5 and q = 6: hyperbolic
    for ax, q in ((axes[2], 5), (axes[3], 6)):
        _, r_in, _, _ = tiling_consts(4, q)
        L = W_FREE / (2 * r_in)
        clip = frame(ax, f"q = {q} · hyperbolic", f"L = s/{2 * r_in:.4f} = {L:.2f} m\nloop closes in {q} corners · endless rooms", HYP)
        for M in build_tiling(4, q, 3.4):
            c = M @ ORIGIN
            d = math.acosh(max(c[2], 1.0))
            o = tile_outline(M, 4, q, chart=logmap, n=30) * L
            if d < 1e-6:
                ax.add_patch(Polygon(o, closed=True, fc=FLAT, alpha=0.30, ec=FLAT, lw=1.8, zorder=2))
            else:
                (ln,) = ax.plot(np.r_[o[:, 0], o[0, 0]], np.r_[o[:, 1], o[0, 1]], color=HYP, lw=1.0,
                                alpha=max(0.15, 1.0 - 0.3 * d), zorder=1)
                ln.set_clip_path(clip)

    fig.suptitle(f"The curvature dial counts rooms per corner.  Tile zero is your lap square (s = {W_FREE:g} m here); "
                 f"dotted = the real {ROOM:g} m room.  Axes in metres from your head.",
                 fontsize=11.5, fontweight="bold", y=0.985)
    fig.tight_layout(rect=(0, 0, 1, 0.94))
    fig.savefig(OUT / "fig06_rooms_per_corner.png", dpi=200, bbox_inches="tight", pad_inches=0.18)
    plt.close(fig)
    return dict(R3=Rs, L5=W_FREE / (2 * tiling_consts(4, 5)[1]), L6=W_FREE / (2 * tiling_consts(4, 6)[1]))


# =====================================================================
# Figure 4: the breathing worldtube (z-t diagram): slice, shadow, tilt
# =====================================================================
def fig_worldtube():
    b, rad = 12.0, 10.0   # motion amplitude (mm) and tumour radius (mm) -- illustrative, as in the Brief

    def zc(ph):
        return -b * np.cos(np.pi * ph) ** 4

    fig, ax = plt.subplots(figsize=(12.4, 5.9))
    ph = np.linspace(0, 2, 900)
    up, lo = zc(ph) + rad, zc(ph) - rad
    one = ph <= 1.0
    ax.fill_between(ph[one], lo[one], up[one], color=BLUE, alpha=0.22, lw=0)
    ax.fill_between(ph[~one], lo[~one], up[~one], color=BLUE, alpha=0.09, lw=0)
    ax.plot(ph, up, color=BLUE, lw=1.6)
    ax.plot(ph, lo, color=BLUE, lw=1.6)
    ax.plot(ph, zc(ph), color=BLUE, lw=1.0, ls="--", alpha=0.8)
    ax.text(0.50, zc(0.5) + 0.8, "tumour worldtube", color=BLUE, fontsize=11, fontweight="bold", ha="center")

    # (1) slice perpendicular to time
    t0 = 0.18
    ax.plot([t0, t0], [zc(t0) - rad, zc(t0) + rad], color=FLAT, lw=4.5, solid_capstyle="butt")
    ax.annotate("① slice across time\n= the tumour at one instant\n(true size 20 mm)", xy=(t0, zc(t0) + rad), xytext=(0.20, 21.5),
                color="#14543d", fontsize=9.8, ha="center", arrowprops=dict(arrowstyle="-", color=FLAT, lw=1.2))

    # (2) shadow along time = ITV, and the gated version
    itv_lo, itv_hi = lo[one].min(), up[one].max()
    ax.plot([-0.10, -0.10], [itv_lo, itv_hi], color=ACCENT, lw=7, solid_capstyle="butt")
    ax.text(-0.125, (itv_lo + itv_hi) / 2, f"② shadow along time\n= ITV  {itv_hi - itv_lo:.0f} mm", color=ACCENT, fontsize=9.8,
            ha="right", va="center", fontweight="bold")
    g0, g1 = 0.30, 0.70
    gmask = (ph >= g0) & (ph <= g1)
    g_lo, g_hi = lo[gmask].min(), up[gmask].max()
    ax.axvspan(g0, g1, color=SPH, alpha=0.12, lw=0)
    ax.plot([-0.045, -0.045], [g_lo, g_hi], color=SPH, lw=7, solid_capstyle="butt")
    ax.text(0.5, -27.2, f"gating window 30–70 %  →  shadow shrinks to {g_hi - g_lo:.0f} mm", color=SPH, fontsize=9.8, ha="center", fontweight="bold")
    for y in (itv_lo, itv_hi):
        ax.plot([-0.10, 1.0], [y, y], color=ACCENT, lw=0.7, ls=":", alpha=0.7)

    # (3) tilted slice: a scanner that moves along z while the patient breathes
    t_a, z_a, t_b, z_b = 1.02, 14.0, 1.62, -26.0
    tt = np.linspace(t_a, t_b, 600)
    zz = z_a + (z_b - z_a) * (tt - t_a) / (t_b - t_a)
    inside = (zz <= zc(tt) + rad) & (zz >= zc(tt) - rad)
    ax.plot(tt, zz, color=HYP, lw=1.6, ls=(0, (4, 3)))
    ax.plot(tt[inside], zz[inside], color=HYP, lw=4.5, solid_capstyle="butt")
    seen = zz[inside].max() - zz[inside].min()
    ax.annotate(f"③ tilted slice (space and time mixed)\n= what a moving scan records\nfrom a breathing patient:\nit sees {seen:.0f} mm instead of 20 mm",
                xy=(tt[inside][len(tt[inside]) // 2], zz[inside][len(zz[inside]) // 2]), xytext=(1.80, 17.0),
                color=HYP, fontsize=9.8, ha="center", arrowprops=dict(arrowstyle="-", color=HYP, lw=1.2))

    # periodicity
    ax.axvline(1.0, color=MUTED, lw=1.0, ls="-.")
    ax.text(1.0, -31.6, "④ phase wraps: 100 % is 0 % again  (time is a circle, ℝ³ × S¹)", color=MUTED, fontsize=9.8, ha="center")

    ax.set_xlim(-0.52, 2.05)
    ax.set_ylim(-34, 27)
    ax.set_xticks([0, 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0])
    ax.set_xticklabels(["0 %", "25 %", "50 %", "75 %", "100 % = 0 %", "25 %", "50 %", "75 %", "100 %"])
    ax.set_xlabel("breathing phase  (the fourth axis)")
    ax.set_ylabel("head–feet position  (mm)")
    for sp in ("top", "right"):
        ax.spines[sp].set_visible(False)
    ax.set_title("The medical 4D in one picture: a tumour over one breath is a tube in space × phase", fontsize=12.5, fontweight="bold")
    ax.text(-0.50, -33.4, "Illustrative motion:\nz(φ) = −12·cos⁴(πφ) mm,\n20 mm tumour", ha="left", va="bottom", fontsize=8, color=MUTED)
    fig.tight_layout()
    fig.savefig(OUT / "fig04_worldtube.png", dpi=200, bbox_inches="tight", pad_inches=0.18)
    plt.close(fig)
    return dict(itv=itv_hi - itv_lo, gated=g_hi - g_lo, tilted=seen)


# =====================================================================
# Figure 5: tiles in view vs cull radius, against the draw-call budget
# =====================================================================
def fig_budget(counts):
    tiles = build_tiling(4, 5, 5.05)
    dists = sorted(math.acosh(max((M @ ORIGIN)[2], 1.0)) for M in tiles)
    rho = np.linspace(0.5, 5.0, 300)
    n = np.array([np.searchsorted(dists, r + 1e-9) for r in rho])
    area = 2 * math.pi / 5
    approx = 2 * math.pi * (np.cosh(rho) - 1) / area

    fig, ax = plt.subplots(figsize=(9.6, 5.4))
    ax.step(rho, n, where="post", color=HYP, lw=2.4, label="tiles in view, exact count ({4,5})")
    ax.plot(rho, approx, color=HYP, lw=1.1, ls="--", alpha=0.75, label="area estimate  5·(cosh ρ − 1)")
    ax.axhline(150, color=ACCENT, lw=1.6)
    ax.text(0.55, 163, "MUST: 300 draw calls per frame  =  150 per eye", color=ACCENT, fontsize=9.6, fontweight="bold")
    ax.axhline(100, color=SPH, lw=1.4, ls=(0, (6, 3)))
    ax.text(0.55, 108, "SHOULD: 200 per frame  =  100 per eye", color=SPH, fontsize=9.6, fontweight="bold")
    for r, lab, col in ((3.0, "default cull\nρ = 3", FLAT), (3.5, "high tier\nρ = 3.5", BLUE), (2.0, "thermal\nρ = 2", MUTED)):
        k = int(np.searchsorted(dists, r + 1e-9))
        ax.plot([r, r], [1, k], color=col, lw=1.3, ls=":")
        ax.plot(r, k, "o", color=col, ms=8)
        ax.text(r + 0.04, k * 0.60, f"{lab}\n{k} tiles", color=col, fontsize=9.3, fontweight="bold", va="top")
    ax.set_yscale("log")
    ax.set_ylim(1, 600)
    ax.set_xlim(0.5, 5.0)
    ax.set_yticks([1, 3, 10, 30, 100, 300])
    ax.set_yticklabels(["1", "3", "10", "30", "100", "300"])
    ax.set_xlabel("cull / fog radius ρ  (hyperbolic units; metres = ρ·L)")
    ax.set_ylabel("tiles drawn per eye (one draw call each)")
    ax.grid(True, which="major", color=GRID, lw=0.6)
    for sp in ("top", "right"):
        ax.spines[sp].set_visible(False)
    ax.legend(loc="lower right", frameon=False, fontsize=9.3)
    ax.set_title("Why the fog must close at ρ ≈ 3: hyperbolic space grows exponentially", fontsize=12.5, fontweight="bold")
    fig.tight_layout()
    fig.savefig(OUT / "fig05_tile_budget.png", dpi=200, bbox_inches="tight", pad_inches=0.18)
    plt.close(fig)


if __name__ == "__main__":
    fig_345()
    counts = fig_tiling()
    L = fig_modes()
    tube = fig_worldtube()
    fig_budget(counts)
    dial = fig_dial()
    print("dial lengths (m):", {k: round(v, 3) for k, v in dial.items()})
    print("tile counts by radius:", counts)
    print(f"Mode-1 example L = {L:.3f} m")
    print("worldtube numbers (mm):", {k: round(v, 1) for k, v in tube.items()})
    print("written to", OUT)
