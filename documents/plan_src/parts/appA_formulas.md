---

## Appendix A. Formulas

Every formula here was checked numerically. The figure script (`figures/make_figures.py`) recomputes the tiling counts, the loop closures and the worldtube numbers each time it runs; `research/scripts/tile_draw_rule.py` recomputes the drawn-tile counts of A.3 and `research/scripts/lap_square_check.py` the lap-square numbers of A.4. Each formula has a unit test in §11.1 or an acceptance test in its task row (§9.5).

### A.1 Conventions

- Curvature K = −1/L² (hyperbolic) or +1/R² (spherical). Inside the kernel lengths are in units of L (or R); they are multiplied by L only at the boundary to metres.
- Hyperbolic plane: the hyperboloid H² = { p = (x, z, w) : ⟨p, p⟩ = −1, w > 0 } with ⟨a, b⟩ = aₓbₓ + a_z b_z − a_w b_w. η = diag(1, 1, −1). Base point o = (0, 0, 1).
- Sphere: the unit sphere with the ordinary dot product; base point (0, 0, 1).
- One parameter κ covers all three: κ = −1 hyperbolic, 0 flat, +1 spherical.

### A.2 Kernel

| Operation | Hyperbolic (κ = −1) | Spherical (κ = +1) |
|---|---|---|
| Step by a tangent vector Δ, with u = Δ/\|Δ\|, d = \|Δ\| | `Boost(Δ)` = [[1+(c−1)uₓ², (c−1)uₓu_z, s·uₓ], [(c−1)uₓu_z, 1+(c−1)u_z², s·u_z], [s·uₓ, s·u_z, c]], c = cosh d, s = sinh d | Same with c = cos d, s = sin d and last row [−s·uₓ, −s·u_z, c] |
| Unified form | Matrix exponential of [[0, 0, Δₓ], [0, 0, Δ_z], [−κΔₓ, −κΔ_z, 0]] | same |
| Inverse | M⁻¹ = η·Mᵀ·η | M⁻¹ = Mᵀ |
| Exponential map at o | Exp(t) = (sinh r · t/r, cosh r), r = \|t\| | (sin r · t/r, cos r) |
| Logarithm map at o | Log(p) = asinh(\|p_xz\|) · p_xz/\|p_xz\| | acos(p_w) · p_xz/\|p_xz\| |
| Distance | cosh d = −⟨p, q⟩ | cos d = p·q |
| Renormalise | Gram–Schmidt with respect to η, every frame | Ordinary Gram–Schmidt |
| Apparent size of width w at distance d | w / (L·sinh(d/L)) | w / (R·sin(d/R)) |

**Holonomy.** Carrying a frame round a closed geodesic polygon rotates it by the polygon's area divided by L² (hyperbolic: against the sense of the loop; spherical: with it). By Gauss–Bonnet the area of a geodesic n-gon is L²·((n − 2)π − Σ angles) in H² and R²·(Σ angles − (n − 2)π) on S².

### A.3 Regular tilings {p, q}

p-sided tiles, q at each corner. From the right triangle (centre, edge midpoint, corner) with angles π/p, π/2, π/q:

| Quantity | Formula (hyperbolic; replace cosh by cos on the sphere) | {4,5} | {4,6} | {4,3} sphere |
|---|---|---|---|---|
| Half-edge a | cosh a = cos(π/p) / sin(π/q) | 0.6269 | 0.8814 | 0.6155 |
| Edge 2a | | 1.2537 | 1.7627 | 1.2310 |
| In-radius r | cosh r = cos(π/q) / sin(π/p) | 0.5306 | 0.6585 | π/4 |
| Centre spacing 2r | | **1.0613** (cosh = φ) | **1.3170** (cosh = 2) | **π/2** |
| Circumradius r_c | cosh r_c = cot(π/p)·cot(π/q) | 0.8425 | 1.1462 | 0.9553 |
| Tile area | π(p − 2) − 2πp/q (sphere: 2πp/q − π(p − 2)) | 2π/5 | 2π/3 | 2π/3 |
| Corner angle | 2π/q | 72° | 60° | 120° |
| Klein (gnomonic) half-width of the tile | tanh r (sphere: tan r) | 0.4859 | 0.5774 | 1 |

For four-sided tiles the centre spacing is 2·asinh(√cos(2π/q)).

**Tile counts, {4,5}.** Tile centres within hyperbolic distance ρ of a viewer at a tile centre: 13 (ρ = 2), 25 (2.5), 45 (3), 81 (3.5), 129 (4), 373 (5). Tiles at graph distance 0, 1, 2, …: 1, 4, 12, 28, 64, 148, 340, 780, 1792 (OEIS A377322).

**Draw rule, {4,5}.** Fog starts at ρ − 1 and is fully opaque at ρ (default ρ = 3). A tile whose centre lies just outside ρ still reaches 0.8425 inside it, so culling tile centres at ρ would leave holes. A tile is therefore drawn when (a) its centre is within ρ + 0.8425 of the walker (0.8425 is the circumradius r_c, so this is every tile that can reach inside the fog radius) and (b) it can be in view: the disc of radius r_c·L round the floor position of its centre (L·d along its bearing, d being the distance to its centre) is not wholly outside any of the four side planes of the view frustum, widened by 12° to each side and 4° up and down. A disc of radius r round c is wholly outside a plane with inward normal n when n·c + r·|n_floor| < 0. The log map never stretches a length (its tangential scale is d ÷ sinh d ≤ 1 and its radial scale is 1), so the tile lies inside that disc and the test is safe at any head pitch. It runs once per frame on the CPU, for both eyes. At ρ = 3, 45 tile centres lie within the fog radius when the walker stands at a tile centre (40 to 50 elsewhere in the tile), 104 to 119 tiles reach inside it, and at most 59 are drawn with a level gaze and 71 at any pitch (enumerated; `research/scripts/tile_draw_rule.py`). If the view test is not built, the fallback is to cull tile centres at ρ = 3 and make the fog opaque at ρ = 2.15, because culling by centre covers only the disc of radius ρ − r_c = 2.1575.

### A.4 The loop-fit rule and the 3-4-5 walk

One "walk s, turn left 90°" is the isometry g = Boost(s/L)·Rot(90°). It is a rotation by an angle φ about a fixed point, with

```text
cos(φ/2) = cosh(s / 2L) / √2        (hyperbolic)
cos(φ/2) = cos(s / 2R) / √2         (spherical)
```

The walk closes after n sides when φ = 360°/n:

| n | Condition | s/L or s/R |
|---|---|---|
| 3 (sphere) | cos(s/2R) = √2·cos(π/3) | π/2 = 1.5708 |
| 4 (flat) | always | any |
| 5 | cosh(s/2L) = √2·cos(π/5) | 1.0613 |
| 6 | cosh(s/2L) = √2·cos(π/6) | 1.3170 |
| 7 | cosh(s/2L) = √2·cos(π/7) | 1.4491 |
| n → ∞ | cosh(s/2L) = √2 | 1.7627 (beyond this the walk never closes) |

These are the centre spacings of the {4, n} tilings, which is why "rooms per corner" and "corners to close" are the same number.

**After one physical lap** (four sides, four turns) at the five-corner value: the walker is displaced by exactly s and their frame is turned by 90°. The same motion is a 72° rotation about the centre of the five-sided loop, which lies 0.8425·L from each of the loop's corners; five laps are the identity. That centre is a corner post only when the loop runs through tile centres. In the room the loop is walked on the lap square of tile zero, whose corners are s/√2 = 0.7504·L from the room centre: the centre of rotation is then 0.092·L from the room centre (0.17 m at L = 1.88 m), and the lap square's corners lie 0.092·L (0.17 m) inside tile zero's corner posts. For a small loop the turn per lap tends to s²/L².

**Loop-fit rule.** s = clamp(min(W, D) − 1.0 m, 1.0 m, 3.2 m); L = s/1.0613 (q = 5); L = s/1.3170 (q = 6); R = 2s/π (q = 3). W and D are wall to wall in an empty rectangular room. The scan gives the free rectangle Wf × Df, already 0.30 m inside walls and furniture, so W = Wf + 0.6 m and the code uses s = clamp(min(Wf, Df) − 0.4 m, 1.0 m, 3.2 m). The lap square is centred on the anchor, at least 0.20 m inside the free rectangle and so at least 0.5 m from the nearest wall or scanned obstacle.

**Other closures.** A rectangular lap a × b closes after six sides when cosh(b/L)·(cosh(a/L) − 1) = cosh(a/L). An equiangular four-sided tile with q per corner and midline half-lengths m₁, m₂ satisfies sinh m₁ · sinh m₂ = cos(2π/q); a non-square tile can only tile when q is even.

### A.5 Exact tile ids for {4,5}

```text
R = rotation by 90° about the tile centre          H = diag(−1, −1, 1)  (half-turn)
T = | φ   0  √φ |                                   φ = (1 + √5)/2,  φ² = φ + 1
    | 0   1   0 |                                   cosh(2r) = φ,  sinh(2r) = √φ
    | √φ  0   φ |
Step through edge k:   S_k = R^k · T · H            S_0·S_0 = I;   (S_1)^5 = I  and no smaller power is
```

In this section R, T and H are the matrices defined above: R is not the sphere radius, and the matrix T is not the coordinate T of a tile centre used below. Every product of R, T and H has entries of the form u + vφ with integers u, v, apart from a common factor √φ on the entries that mix space and time. A tile centre is therefore (√φ·X, √φ·Y, T) with X, Y, T in ℤ[φ], and

```text
tile id = (X, Y, T) = six integers            invariant:  φ·(X² + Y²) − T² = −1
```

The neighbour step multiplies by 0, ±1 or ±φ only, and (u + vφ)·φ = v + (u + v)φ, so it needs additions alone. Coefficients grow by a factor of about 2.9 per straight tile step: 64-bit integers are exact to 42 straight steps from tile zero and overflow at 43. The invariant multiplies coefficients, so it is evaluated in Int128 or BigInteger (or in tests only); in `long` its products overflow from about 22 straight steps. Paths that turn stay nearer tile zero. The walkable lung route is 32 tile steps (16 generations); 46 steps (23 generations) is only the addressable depth, and on the airway's paths the coefficients need about 52 bits at 46 steps, so `long` suffices there.

### A.6 Tile meshes

Lay a regular grid on the Klein square [−h, h]², h = tanh r. Move each grid point k to the log chart of the tile:

```text
chart position (metres) = L · atanh(|k|) · k/|k|            (sphere: R · atan(|k|) · k/|k| on the gnomonic square)
```

Straight grid lines in the Klein model are geodesics, so neighbouring tiles share their edge exactly.

**Subdivision rule.** By grid count, not by edge length: n = 16 grid cells per side on the walker's current tile and the tiles that touch it (12 in {4,5}); n = 8 on every other drawn tile; n = 4 only on the high tier, beyond ρ = 3. Every level keeps the same 16 boundary segments per edge, so neighbours of different level share their edge vertices exactly. Caps: 1,200, 500 and 250 triangles for the three levels, for any L. Acceptance is by error, not by length: worst screen-space chord error ≤ 0.15° from an eye height of 1.6 m at L = 1.4, 1.9 and 3.0 m. The rule "longest edge ≤ 0.25 m" stays for the Home shell, furniture and authored props.

**Corner-post wedges.** The log chart keeps angles only at the tile centre. At a corner, lengths across the radius are drawn at r_c / sinh r_c = 0.891 of the scale of lengths along it, so the tile's corner, which is 2π/q = 72° in the curved metric, spans 2·atan(0.891·tan 36°) = 65.8° in mesh coordinates for {4,5}. Each wedge is bounded by the tile's own two edges: it is built in Klein coordinates and mapped with the formula above, and five wedges make one post.

### A.7 Aligned Cells as a polyhedral model

Cell side g, q cells per corner in the world, four on the floor.

```text
cone angle at a corner post = q · 90°                         (450° for q = 5)
curvature at a post         = 2π − q·π/2                      (−π/2 for q = 5)
curvature per tile          = 4 corners × (−π/2) ÷ 5 tiles = −2π/5        (the same as the smooth tile)
equivalent radius           = g / √(2π/5) = 0.892 · g
```

### A.8 Four dimensions

- **Cut of a tetrahedron** by the hyperplane n·x = w₀: with f_i = n·x_i − w₀ at each vertex, every edge whose ends have opposite signs gives the point x_i + f_i/(f_i − f_j)·(x_j − x_i). Three such points make a triangle, four a quadrilateral.
- **Prism to tetrahedra.** A surface triangle carried from phase k to k + 1 is a prism. With its vertex indices sorted a < b < c it splits into (a_k, b_k, c_k, c_k+1), (a_k, b_k, b_k+1, c_k+1), (a_k, a_k+1, b_k+1, c_k+1); sorting makes neighbouring prisms agree on shared faces.
- **Checks.** The unit tesseract cut by x + y + z + w = c is a regular tetrahedron of surface area 0.8660 at c = 0.5 and a regular octahedron of area 6.9282 at c = 2. A unit 4D ball cut at offset c is a ball of radius √(1 − c²).
- **Scanner tilt.** A table moving at speed v samples z at time t(z) = t₀ + (z − z₀)/v. With the time axis scaled by c metres per second this is a hyperplane tilted by θ, tan θ = c/v. A sphere of diameter D moving at constant speed u along the table appears with length D·v/(v − u); this is the unit test. A breathing tumour does not move at constant speed: the slicer gives the range of apparent lengths directly, and it is never more than D + b, with b the distance travelled in the breathing curve below.
- **Illustrative breathing curve.** z(t) = z₀ − b·cos⁴(πt/τ). Mean position z₀ − 0.375·b. Gating to 30–70 % of the cycle leaves 11.9 % of the motion at a 40 % duty cycle.
