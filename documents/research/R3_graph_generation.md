# R3 — Graph generation for ATLAS spaces (tilings, portal rooms, trees in H², visible sets)

Research memo for the lead engineer, 3 Oct 2026. Scope: guarantee that the graphs behind the generated spaces are **correct** and **cheap**.

**Claim tags.** `[V]` = verified: fetched primary source (paper, source code, vendor docs) · `[S]` = secondary (search snippet, another doc quoting it) · `[D]` = my derivation, checked numerically with the scripts in `research/scripts/r3_*.py` · `[E]` = estimate, must be measured on the device.

**Conventions.** Curvature K = −1 internally ("units"); `L` = metres per unit (the Spec's R). Hyperboloid points p = (x, y, t), Minkowski form ⟨p,q⟩ = x₁x₂ + y₁y₂ − t₁t₂, ⟨p,p⟩ = −1, t > 0, origin o = (0,0,1), η = diag(1,1,−1). Isometries are 3×3 matrices M with MᵀηM = η, so M⁻¹ = ηMᵀη.

---

## 0. What the lead needs to know (12 lines)

1. **A precomputed tile table (the Spec's "Hyperbolica-style lookup") cannot support free walking.** A table to graph depth 9 holds 7,285 tiles but only reaches ≈6.6 units; minus a cull radius of 4 the user has ≈2.6 units of travel. Lazy generation by id is required, not optional. `[D]`
2. **Use exact integer ids for {4,5}.** Tile centres are exactly (√φ·X, √φ·Y, T) with X, Y, T ∈ ℤ[φ]. The id is six integers; the neighbour function is one matrix product that needs **additions only**; de-duplication is a dictionary lookup with zero tolerance. I generated 88,301 tiles this way and the per-depth counts match OEIS A377322 exactly. `[D]` + `[V]`
3. **Do not hand-derive a path-word automaton for {4,5}.** HyperRogue's simple 2-state automaton only covers {p,3} and {p,4} `[V]`; Holonomy's printed rules give a valid but *non-geodesic* spanning tree (its word NLFFL rewrites to the longer WRFRFR) `[D]`; the lexicographic geodesic language needs ≥10 states plus a special root. Exact arithmetic is simpler and testable.
4. **User pose = (tile, tile-local double matrix).** Re-root when the head crosses an edge. Coordinates never exceed cosh(0.85) ≈ 1.4, so double precision never degrades, however far the user walks.
5. **Upload camera-relative matrices.** The Build Pipeline's two-matrix shader (`_HypCamInv` · `_RoomAnchor`) loses 1.3e-3 units at 5 units from the origin and 4e-2 at 7 if both matrices are absolute floats `[D]`. Tile-relative anchors (or one combined per-instance matrix computed in double) remove the "precision horizon" as a constraint; fog then only has to hide the draw limit.
6. **Draw calls, not triangles, are the binding budget.** With the LOD ladder below, all tile shells out to 6 units cost < 60k triangles per eye without any frustum culling. One draw per tile costs 85 draws per eye at cull radius 4 and 229 at radius 5.
7. **Recommended render path:** `Graphics.RenderMeshInstanced` for tile shells (one draw per LOD mesh, per-instance Lorentz matrix as `objectToWorld`, no MaterialPropertyBlock), SRP Batcher + one material per room for unique rooms. This is compatible with the Build Pipeline; Unity's own docs say instancing can beat the SRP Batcher for many identical meshes `[V]`. Spike both in hours 3–6.
8. **Spec §6.7's airway layout overlaps.** With its stated numbers (ℓ = 1.5 m, R = 2.2 m, 120° forks) the tree has 372 edge crossings by depth 8 `[D]`. R = ℓ/ln 2 is only the growth condition. The exact condition for 120° forks is ℓ ≥ L·ln 3; for 90° T-forks ℓ ≥ 1.7627·L.
9. **Snap the airway to the tiling.** "Turn tile + one straight tile" per generation is a binary tree in the {4,5} tile graph with no collisions (brute-forced to depth 14, and guaranteed at all depths by Sarkar's lemma). The branch address is the L/R string; the tile id is exact.
10. **Portal rooms:** author every room in play-space coordinates so every portal transform is the identity. Then loops are consistent by construction and "fits the scanned polygon" is a per-room test. Stencil ids 1–15, depth 1.
11. **Test suite:** 16 invariants, all runnable in Edit Mode in < 2 s; the three that matter most are the A377322 growth test, the vertex-closure test (5 steps, not 4) and the norm test φ(X²+Y²) − T² = −1.
12. **The Poincaré-disk mini-map is a debugging tool, a comfort aid and a presentation asset at once** (the "flat twin"). Build it in Phase 1.

---

## 1. What the documents say, and where they conflict

| Topic | Spec / Brief | Build Pipeline (wins) | This memo |
|---|---|---|---|
| Tile addressing | "Cell id + local coordinates … never accumulate a float world coordinate" (Spec §6.2); "address = move sequence from the hub, HyperRogue-style" (Brief §3.8); but also "offline double-precision tile lookup tables (Hyperbolica)" (Spec §5.3) | "Address space by tile id (Holonomy / HyperRogue style), double-precision hyperboloid transform on the CPU, renormalised every frame and re-anchored at the camera before casting to float" (§0.5) | Exact id + tile-local pose (§A). The lookup-table line in Spec §5.3 should be dropped for free walking. |
| Projection | Klein chart (Spec §5.3) | Log map for walked H²×E, 0.1–0.25 m subdivision; "team decision" | All LOD numbers below assume the log map. |
| Draw / triangle budget | < 100 draws per eye, ≤ 150k tris per eye (Spec §5.3) | < 300 draws total, 300–500k tris per frame (§5) | Use Build Pipeline: 150 draws and 150–250k tris per eye. |
| Per-object data | "one draw call per module type (instancing)" (Spec §5.3) | "one material instance per room, SRP Batcher, no MaterialPropertyBlocks" (§4) | Both, split by content type (§B6). |
| Cull / fog | "hard draw radius 3–4 R + exponential fog" | "CPU-cull rooms at d > ~4" and "fog at about 5–8 units" (these two disagree) | One number: fog reaches 100% at the cull radius (§B5). |
| Play area | Taped 3×3 m, nine 1 m cells (Spec §6.4) | still mentions the taped square (§1.2) | Outdated per the team: walkable polygon from the scan (§C). |
| Airway layout | Wedge layout, R = ℓ/ln b ≈ 2.2 m (Spec §5.3, §6.7) | Procedural Weibel-style tree, "illustrative" | Replace the layout (§D). |

Two facts about the reference code the plan leans on:
- **HyperEngine uses MaterialPropertyBlocks and GPU-instanced properties**, the opposite of the Build Pipeline rule. `HyperObject.LateUpdate` calls `propBlock.SetMatrix(_HyperRot, …)` per renderer and `HyperCore.cginc` declares `UNITY_DEFINE_INSTANCED_PROP(float4x4, _HyperRot)` `[V]`. A straight port will not be SRP-Batcher compatible; the per-object matrix path has to be redesigned (§B6).
- **HyperEngine disables Unity culling** with `camera.cullingMatrix = Matrix4x4.Ortho(-100,100,…)` and culls itself by toggling `renderer.enabled` `[V]`.

---

## A. Tiling generation by tile id

### A1. How others solve it

| System | Graph | Tile id | De-duplication | Where the player is | Precision strategy | Source |
|---|---|---|---|---|---|---|
| **HyperRogue** | Cells with a `connection_table`: per edge a neighbour pointer plus `spin` ("we are spin(i)-th neighbor of move[i]"); built lazily by "treewalkers" | None global: pointer identity. State of a small automaton (`hsOrigin`, `hsA`, `hsB`) decides which edges lead to children "to generate each heptagon in an unique way" | Combinatorial: an edge is parent / left / right / child; left and right links are found by walking around a vertex | View matrix relative to the current cell | `fixmatrix()` → Gram–Schmidt under the Minkowski signature: "The numerical errors tend to accumulate, eventually destroying the projection" | `heptagon.cpp`, `locations.cpp`, `hyperpoint.cpp` `[V]`; arXiv 2111.12040 `[V]` |
| HyperRogue paper (2022) | "Regular tree structure" (Q, t, e): each tile has a state; each edge is P, L, R or a child state. "Geodesic" if depth = graph distance | — | Same, generalised to any periodic tessellation; rules found by an Angluin-like learning algorithm | — | States the problem exactly: "any coordinate system based on a fixed tuple of coordinates will fail to distinguish two points in distance 1 already in distance proportional to the number of bits" | arXiv 2111.12040 pp. 1–6 `[V]` |
| **Hypermine** | Nodes = right-angled dodecahedra of {5,3,4}, 12 neighbours; each node holds 20 chunks (one per vertex) | `NodeId(u128)` = BLAKE3 hash of (primary parent id, side); "guaranteed not to depend on the order in which nodes are added" | Parents are always created before children; a new node's other parents are found through commuting sides ("we can take the path BA instead … each edge is shared by 4 dodecas"); at most 3 parents | `Position { node: NodeId, local: MIsometry<f32> }` | `normalize_transform`: while the position faces a side, reflect into that neighbour | `common/src/graph.rs`, `proto.rs` `[V]` |
| **HyperEngine / Hyperbolica** | Finite list of tiles, {4,n} squares | String of moves (`"RRUL…"`), "generally aren't used as coordinates beyond being unique identifiers" | Offline, O(N²): reject if Poincaré gyrovector within 1e-10 of an existing tile, in double | One global camera gyrovector `worldGV` (float; optional double) | "tile coordinates are not computed at runtime, but are pre-generated with a lookup table offline. This allows double-precision computations". Shipped maps: {4,5} to 10 expansions, {4,6} to 9 | `TileMapper.cs`, `WorldBuilder.cs`, `HyperObject.cs`, `HyperEngine.pdf` `[V]` |
| **Holonomy** (TU Delft) | {4,5} squares, spanning tree | Branch N/W/S/E, then F/L/R steps | Two algorithms: a validity test and a normaliser with four rewrite rules | Tile index | "numeric coordinate representation … introduces numeric precision issues" | arXiv 2409.05460 §2 `[V]` |
| **Standard maths** | Cayley graph of the (2,p,q) triangle group ⟨a,b,c \| a²=b²=c²=(ab)²=(ac)^q=(bc)^p=e⟩ | Shortlex normal form | The group is word-hyperbolic, hence automatic: shortlex words form a regular language; Knuth–Bendix finds the DFA | — | Exact | arXiv 2111.12040 p. 5–6 `[V]` |

Details worth knowing:
- HyperRogue's built-in automaton covers three vertices per corner and four per corner only: "It relies on the fact that … the set of tiles in d steps from the chosen root tile forms a cycle. A similar approach also works for the regular tessellation {p,4} … However, this assumption is not satisfied even in very simple cases" `[V]`. {4,5} has five tiles per corner, so tiles at equal depth can be neighbours and the simple automaton does not apply `[D]`.
- HyperEngine's forward draw distance is Poincaré radius² 0.9975, i.e. 2·atanh(√0.9975) = **7.38 units**; backward 0.99 → **5.99 units** `[D]` from constants in `HyperObject.cs` `[V]`.
- HyperEngine seeds procedural choices from the coordinate string (`GetDeterministicHash(coord)`) `[V]`, the same idea as our content seed.
- Holonomy's printed rules, tested with exact ids `[D]`:
  - The validity rule ("a left step is invalid iff there is no right step between it and the last left step") accepts 144 words of length 5, but there are 148 tiles at distance 5. The four missing tiles have a unique shortest word (e.g. N·L·F·F·L) that the rule rejects.
  - Their rewrite rule sends that word to W·R·F·R·F·R, which is the same tile (checked) but one step longer.
  - The valid words up to length 10 are all distinct tiles (14,877 words, 0 duplicates) and cover every tile within graph distance 7.
  - Conclusion: it is a correct spanning tree, but not a shortest-path one, despite the paper's wording. Do not transcribe it expecting word length = distance.

### A2. Constants (closed forms, verified)

For a regular {p,q} tiling, from the right triangle (centre, edge midpoint, vertex) with angles π/p, π/2, π/q:

| Quantity | Formula | {4,5} | {4,6} |
|---|---|---|---|
| Half-edge a | cosh a = cos(π/p) / sin(π/q) | 0.626870 | 0.881374 |
| Edge 2a | | 1.253739 | 1.762747 |
| Inradius r | cosh r = cos(π/q) / sin(π/p) | 0.530638 | 0.658479 |
| Centre spacing 2r | cosh 2r = 2cos²(π/q)/sin²(π/p) − 1 | 1.061275 (cosh = φ) | 1.316958 (cosh = 2) |
| Circumradius R | cosh R = cot(π/p)·cot(π/q) | 0.842482 | 1.146216 |
| Tile area | π(p−2) − p·2π/q | 2π/5 = 1.256637 | 2π/3 = 2.094395 |
| Interior angle | 2π/q | 72° | 60° |

Checks `[D]`: cosh R = cosh r · cosh a (residual 0); tan(π/p) = tanh a / sinh r and tan(π/q) = tanh r / sinh a (residuals < 4e-16); area equals 2p × the triangle's angle defect. HyperEngine uses the same inradius: `r = cos(π/n)/sin(π/4)`, `CELL_WIDTH = sqrt(r²−1)/r` = tanh(inradius) `[V]`.

Metres: tile edge = 1.2537·L and centre spacing = 1.0613·L for {4,5}. A 1 m tile edge (Holonomy's mapping) means L ≈ 0.80 m; L = 1.37 m gives 1.72 m tiles; L = 2.2 m gives 2.76 m tiles.

### A3. Generators as Lorentz matrices

```
R  = | cos 2π/p  −sin 2π/p  0 |      T = | C 0 S |      H = diag(−1, −1, 1)
     | sin 2π/p   cos 2π/p  0 |          | 0 1 0 |      (half-turn about the centre)
     |    0          0      1 |          | S 0 C |      C = cosh 2r,  S = sinh 2r = √(C²−1)
```
- R rotates the tile about its centre by one edge. T translates along +x by the centre spacing 2r. Both are symmetries of the tiling (T = reflection in the shared edge ∘ reflection in the tile's own mirror line) `[D]`.
- Edge k of a tile is the edge whose midpoint lies at angle 2πk/p from the centre.
- **Step through edge k:** `S_k = R^k · T · H`. The half-turn makes "edge 0 of the new tile points back where you came from", for odd and even p alike.

Identities, all verified `[D]`:
- `S_k · S_0 = R^k` (step out and back returns to the same tile, rotated by k edges); `S_0 · S_0 = I`.
- `(S_1)^q = I`, and `(S_1)^j ≠ I` for 0 < j < q: walking round a vertex closes after exactly q steps. Checked exactly for {4,5} and in double for {4,6}, {5,4}, {7,3}, {3,7}, {6,4}, {4,8}.
- `(Boost(2a) · Rot(π − 2π/q))^p = I`: walking round a tile's boundary closes.

**For {4,5}:** C = φ = (1+√5)/2 and S = √φ, because φ² − 1 = φ. In double: C = 1.618033988749895, S = 1.272019649514069.

```
S_0 = T·H = | −φ    0   √φ |        R = | 0 −1 0 |
            |  0   −1    0 |            | 1  0 0 |
            | −√φ   0    φ |            | 0  0 1 |
```

**Exact ring** `[D]`. Every product of R, T, H has the pattern
```
M = |  a11     a12    √φ·b1 |
    |  a21     a22    √φ·b2 |      with all aij, bi, ci, d ∈ ℤ[φ]  (numbers u + v·φ, u, v integers, φ² = φ + 1)
    | √φ·c1   √φ·c2     d   |
```
because (√φ·x)(√φ·y) = φ·xy stays in ℤ[φ]. So:
- Tile centre = M·o = (√φ·b1, √φ·b2, d). **Tile id = (b1, b2, d) = six integers.**
- Invariant: φ(X² + Y²) − T² = −1 in ℤ[φ] (verified for all 88,301 tiles to depth 12).
- Multiplying by S_k only multiplies by 0, ±1, ±φ, and (u + vφ)·φ = v + (u+v)φ. **The neighbour function needs additions only.**
- Growth: integer coefficients multiply by e^{2r} = 2.89 per tile step (1.53 bits). `long` holds any tile within **≈ 40 tile-steps (≈ 42 units)** of the root; a 128-bit add-only struct holds ≈ 83 steps; `BigInteger` is unbounded.

**For {4,q} in general:** C = 1 + 2cos(2π/q), ring ℤ[2cos(2π/q)], same matrix pattern with √(C²−1) factored out.
- **{4,6}: C = 2, S = √3. Plain integers.** Centre = (√3·X, √3·Y, T) with 3(X²+Y²) − T² = −1; 1.90 bits per step, `long` holds ≈ 33 steps.
- {4,8}: C = 1 + √2, ring ℤ[√2].
- Other p: the rotation brings in cos and sin of 2π/p; use the tolerance method (A4, option 3) with a radius cap.

### A4. Tile id representation: options compared

| # | Representation | Exact? | Unbounded? | Neighbour cost | Effort | Verdict |
|---|---|---|---|---|---|---|
| 1 | First-found path string + coordinate tolerance (HyperEngine) | No | No (finite table) | Table lookup | Low | Fine for an authored level. **Fails free walking** (see §0 point 1). |
| 2 | Canonical word from an automaton (HyperRogue rule set, Holonomy rules, Knuth–Bendix shortlex) | Yes | Yes | Rewrite rules | High for {4,5}: ≥ 10 cone types, special root, infinite rule families (L F^n L) | Avoid. Hard to derive, easy to get subtly wrong (Holonomy's printed rule is an example). |
| 3 | Hash of the rounded Lorentz centre in double (grid 1e-3, check 27 neighbouring cells) | To ≈ 12–15 units from the root in practice; nominally error ≈ e^d·1e-16·steps | No | 1 double matrix product | Very low; any {p,q} | Good fallback and the right choice for a generic {p,q} demo with a radius cap. |
| 4 | **Exact algebraic integers** (ℤ[φ] for {4,5}, ℤ for {4,6}) | Yes, zero tolerance | Yes | ≈ 40 integer additions | Low (≈ 150 lines) | **Recommended.** |
| 5 | Hash-chained canonical parent (Hypermine) | Yes | Yes | Hash + parent search | Medium; natural only for right-angled tilings ({p,4}, {5,3,4}) | Possible via the dual {5,4} pentagons (each {4,5} square splits into four kites owned by four pentagons), but more moving parts than option 4. |

Notes:
- Minimum Euclidean separation of two distinct tile centres on the hyperboloid is ≥ 2·sinh(r) ≈ 1.11, so a 1e-3 grid with a 27-cell probe is safe while coordinate error stays below ≈ 0.1 `[D]`.
- Option 3 reproduced the exact counts to depth 11 for {4,5} (38,445 tiles) and depth 10 for {4,6} `[D]`.

### A5. The algorithm (C#-like)

```csharp
// ---- exact numbers: u + v*phi. Start with long + checked; swap Int for a 128-bit add-only struct or BigInteger later.
struct ZPhi { public long u, v;
  public static ZPhi operator +(ZPhi a, ZPhi b) => new ZPhi{ u = checked(a.u+b.u), v = checked(a.v+b.v) };
  public static ZPhi operator -(ZPhi a)         => new ZPhi{ u = -a.u, v = -a.v };
  public ZPhi TimesPhi()                         => new ZPhi{ u = v, v = checked(u+v) };   // (u+v*phi)*phi
}
// ---- exact frame, pattern [[A, sqrt(phi)*B],[sqrt(phi)*C, D]]
struct XFrame { public ZPhi a11,a12,a21,a22, b1,b2, c1,c2, d;
  public TileId Centre => new TileId(b1, b2, d);                 // six integers
  public XFrame Rot1() => new XFrame {                           // M * R (one edge, 90 deg): no arithmetic
    a11 = a12, a12 = -a11, a21 = a22, a22 = -a21, b1 = b1, b2 = b2, c1 = c2, c2 = -c1, d = d };
  public XFrame StepT() => new XFrame {                          // M * T * H: additions and TimesPhi only
    a11 = -(a11 + b1).TimesPhi(),  a12 = -a12,
    a21 = -(a21 + b2).TimesPhi(),  a22 = -a22,
    b1  = a11 + b1.TimesPhi(),     b2  = a21 + b2.TimesPhi(),
    c1  = -(c1.TimesPhi() + d),    c2  = -c2,
    d   = (c1 + d).TimesPhi() };                                 // right-hand sides use the OLD field values
  public XFrame Step(int k) { var m = this; for (int i = 0; i < k; i++) m = m.Rot1(); return m.StepT(); }   // M * S_k
  public XFrame Canon();                                         // of the 4 frames M*R^j keep the one whose first
}                                                                // column (a11, a21, c1 as 6 integers) is lexicographically smallest
// Both formulas were checked against the full matrix product on 300 random words; Canon() was checked to be
// path-independent over the depth-6 ball plus 2,000 random walks.
readonly struct TileId : IEquatable<TileId> { /* 6 longs; Equals = all equal; GetHashCode = Mix64 below (not string hash) */ }

sealed class Tile {
  public TileId id; public XFrame frame;            // canonical frame => edge numbering is exploration-independent
  public Tile[] nbr = new Tile[4]; public sbyte[] spin = {-1,-1,-1,-1};
  public ulong seed;                                // content seed
  public int stamp; public double3x3 rel;           // visit stamp + transform relative to the current root tile
}

sealed class TileGraph {
  readonly Dictionary<TileId, Tile> tiles = new();
  static readonly double3x3[,] Gd = Precompute();   // Gd[k,j] = (double)(S_k * R^j), 16 constants
  public Tile Root;

  public Tile Neighbor(Tile t, int k) {
    if (t.nbr[k] != null) return t.nbr[k];
    XFrame raw = t.frame.Step(k);
    if (!tiles.TryGetValue(raw.Centre, out Tile n)) {
      n = new Tile { id = raw.Centre, frame = raw.Canon(), seed = Mix64(raw.Centre) };
      tiles.Add(n.id, n);
    }
    int j = 0; XFrame f = raw; while (!f.Equals(n.frame)) { f = f.Rot1(); j++; }   // exact; j in 0..3, n.frame = raw * R^j
    t.nbr[k] = n; t.spin[k] = (sbyte)j;
    return n;                                                   // the reverse link n -> t resolves itself on its first query
  }
  public double3x3 StepMatrix(Tile t, int k) => Gd[k, t.spin[k]];   // = t.frame^-1 * n.frame: maps n's frame into t's frame
}
```
Do not fill the reverse link inside `Neighbor`. Letting the reverse query resolve through the dictionary costs one extra exact step per edge and removes a class of index bugs. (The edge of n that leads back to t is `(4 − j) mod 4`, should you need it.)

**Content seed** (deterministic across runs, devices and exploration order):
```csharp
static ulong Mix64(TileId id) {            // SplitMix64 over the six integers; do NOT use string.GetHashCode()
  ulong h = 0x9E3779B97F4A7C15UL ^ worldSeed;
  foreach (long w in id.Words) { h += (ulong)w + 0x9E3779B97F4A7C15UL;
    h = (h ^ (h >> 30)) * 0xBF58476D1CE4E5B9UL; h = (h ^ (h >> 27)) * 0x94D049BB133111EBUL; h ^= h >> 31; }
  return h; }
// per-edge content (doors, walls): seedEdge = Mix64(min(idA,idB)) ^ Rotl(Mix64(max(idA,idB)), 17)  -> same from both sides
```

**BFS to graph depth K from the current tile** (rebuilt only when the root changes):
```csharp
void BuildActiveSet(Tile cur, int K, List<Tile> active) {
  stamp++; active.Clear(); cur.rel = I; cur.stamp = stamp; cur.depth = 0; queue.Enqueue(cur);
  while (queue.Count > 0) { Tile t = queue.Dequeue(); active.Add(t);
    if (t.depth == K) continue;
    for (int k = 0; k < 4; k++) { Tile n = Neighbor(t, k);
      if (n.stamp == stamp) continue;                       // exact de-duplication: object identity
      n.stamp = stamp; n.depth = t.depth + 1; n.rel = mul(t.rel, StepMatrix(t, k)); queue.Enqueue(n); } } }
```
`rel` is a product of at most K constants in double, rebuilt from scratch at every re-root, so nothing accumulates.

**Per frame: move, re-root, cull** (replaces the Build Pipeline's single global `C`):
```csharp
// C_local: head frame -> current tile frame (double3x3). Build Pipeline: C = C * Boost(delta); eta-Gram-Schmidt.
C_local = EtaOrthonormalize(mul(C_local, Boost(deltaXZ / L)));
for (int guard = 0; guard < 4; guard++) {                    // re-root while outside the tile
  double3 p = C_local.c2; int cross = -1;
  for (int k = 0; k < 4; k++) {                              // n_k = R^k (cosh r, 0, sinh r); <p,n_k> = sinh(signed distance past edge k)
    double s = p.x*nk[k].x + p.y*nk[k].y - p.z*nk[k].z;
    if (s > HYST) { cross = k; break; } }                    // HYST = sinh(0.02): no flip-flop on the edge
  if (cross < 0) break;
  double3x3 G = graph.StepMatrix(cur, cross);
  C_local = mul(LorentzInverse(G), C_local);                 // eta * G^T * eta
  cur = graph.Neighbor(cur, cross); rootChanged = true; }
if (rootChanged) BuildActiveSet(cur, K, active);             // time-slice the outer ring if needed (§E)

double3x3 V = LorentzInverse(C_local);                       // tile frame -> head frame
foreach (Tile t in active) {
  double3x3 Mv = mul(V, t.rel); double3 c = Mv.c2;           // tile centre in head frame
  double d = asinh(hypot(c.x, c.y));                         // geodesic distance, well conditioned near 0
  if (d - R_CIRC > dCull) continue;
  if (d > R_CIRC && abs(angle(atan2(c.y, c.x), headYaw)) > halfFov + asin(sinh(R_CIRC)/sinh(d))) continue;
  lodList[LodOf(d)].Add(ToFloat4x4(Mv)); }                   // this list IS the per-frame visible-set graph
```
Incremental update: when the user crosses into a new tile nothing is regenerated. Existing tiles keep their objects; only tiles that enter the depth-K ball are created (roughly one outer shell on the far side).

**Eviction.** Keep at most `MAX_ALIVE` tiles (20,000). When exceeded, drop tiles whose last visit stamp is oldest and clear the pointers to them. Everything except user-made changes is a pure function of the id; keep changes in a separate `Dictionary<TileId, TileState>`.

### A6. Generic {4,q} and {p,q}

- Make q a constructor parameter: `C = 1 + 2cos(2π/q)`, `S = sqrt(C²−1)`, `r = acosh(C)/2`, `R_CIRC = acosh(cot(π/4)·cot(π/q))`. The double-side code (`Gd`, edge normals, LOD radii) is identical.
- Exact side: `ZPhi` for q = 5; plain `long` for q = 6 (multiply by 2 and 3). For any other q, or p ≠ 4, plug in option 3 behind the same `TileId` interface (rounded centre) and cap the walkable radius at ≈ 12 units.
- The tests in A7 take (p, q) as parameters, so a wrong constant is caught immediately.

### A7. Unit tests that prove correctness

| # | Test | Pass condition |
|---|---|---|
| T1 | Constants | cosh a, cosh r, cosh R match the closed forms; cosh R = cosh r·cosh a to 1e-12 |
| T2 | Generators are Lorentz | RᵀηR = η, TᵀηT = η to 1e-12 |
| T3 | Exact vs double | For 1,000 random words, `(double)XFrame` equals the double product to 1e-9·cosh(d) |
| T4 | Step-back | `S_k·S_0 = R^k` exactly; `Neighbor(Neighbor(t,k), back) == t` for every tile and k |
| T5 | **Vertex closure** | `(S_1)^q = I` exactly and `(S_1)^j ≠ I` for 0 < j < q. Also: turn-left-around-a-vertex returns to the same `Tile` object after exactly 5 steps, never 4 |
| T6 | Boundary closure | `(Boost(2a)·Rot(π − 2π/q))^p = I` to 1e-12 |
| T7 | **Growth sequence** | Tiles at graph distance n = 1, 4, 12, 28, 64, 148, 340, 780, 1792, 4116, 9452, 21708, 49856 for {4,5} (OEIS A377322; g.f. (1+2x+4x²+2x³+x⁴)/(1−2x−2x³+x⁴); a(n) = 2a(n−1) + 2a(n−3) − a(n−4)) `[V]`. For {4,6}: 1, 4, 12, 32, 84, 220, 576, 1508, 3948, 10336, 27060 (a(n) = 3a(n−1) − a(n−2)) `[D]` |
| T8 | Hyperboloid invariant | φ(X²+Y²) − T² = −1 for every tile (use `BigInteger` in the test only) |
| T9 | Separation | No two tile centres closer than 2r − 1e-9 in the active set; every neighbour pair at exactly 2r |
| T10 | Degree and faces | Every tile has 4 distinct neighbours; every vertex has q tiles; the dual faces are q-gons |
| T11 | Order independence | Build the depth-6 ball by BFS, by DFS and by 10 random walks: same id set, same canonical frames, same seeds |
| T12 | Re-root invariance | Random 500-step walk: after every step the head's position expressed through the old root and the new root agree to 1e-9; `C_local` centre stays within R_CIRC + hysteresis |
| T13 | Far-field stability | Walk 35 tiles straight out, then run T5 and T9 around the far tile: still exact (this is the test a float-only implementation fails) |
| T14 | Seed stability | Golden file of 64 (id, seed) pairs; identical in Editor (Mono) and device build (IL2CPP) |
| T15 | Overflow guard | The `checked` arithmetic throws at ≈ 40 steps; the game catches it and applies the agreed policy (§Questions) |
| T16 | Automaton sanity (documentation) | Naive Holonomy rule yields 144 words at length 5 ≠ 148: shows why we did not use path words |

---

## B. Numbers for budgeting

### B1. How many tiles

Formulas `[D]`: tile area A = π(p−2) − 2πp/q. Disc area = 2π(cosh d − 1). Estimate N(d) ≈ 2π(cosh d − 1)/A, i.e. **5(cosh d − 1)** for {4,5} and **3(cosh d − 1)** for {4,6}. Asymptotically each extra unit of radius multiplies the count by e ≈ 2.72.

Exact enumeration (the range covers the viewer at a tile centre, an edge midpoint and a vertex) `[D]`:

**{4,5}**

| Radius d (units) | Area estimate | Centres within d | Tiles touching the disc (must be drawn) | In a 100° horizontal FOV, worst heading | Graph depth K needed |
|---|---|---|---|---|---|
| 2 | 13.8 | 13–15 | 30–33 | 14 | 3 |
| 3 | 45.3 | 40–45 | 97–105 | 34–37 | 4 |
| 4 | 131.5 | 115–134 | 237–265 | 75–85 | 6 |
| 5 | 366.0 | 358–375 | 726–780 | 218–229 | 7 |
| 6 | 1003.6 | 981–1030 | 2005–2041 | 573–580 | 9 |

**{4,6}**

| Radius d | Area estimate | Centres within d | Tiles touching the disc | In 100° FOV, worst | Graph depth K |
|---|---|---|---|---|---|
| 2 | 8.3 | 5–12 | 21–30 | 11–15 | 3 |
| 3 | 27.2 | 21–30 | 60–77 | 24–30 | 5 |
| 4 | 78.9 | 72–86 | 197–240 | 63–77 | 6 |
| 5 | 219.6 | 213–224 | 510–556 | 154–173 | 7 |
| 6 | 602.1 | 582–617 | 1458–1502 | 419–447 | 9 |

Cumulative tiles within graph distance n ({4,5}): 1, 5, 17, 45, 109, 257, 597, 1377, 3169, 7285, 16737 for n = 0…10. So the active set is 597 tiles for K = 6 and 1,377 for K = 7.

### B2. Apparent size of a tile

A disc of radius ρ centred at distance d subtends a full angle **ω(d) = 2·asin(sinh ρ / sinh d)** `[D]`. With ρ = circumradius:

| d (units) | 1.5 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|
| {4,5} width | 52.7° | 30.2° | 10.8° | 3.97° | 1.46° | 0.54° | 0.20° |
| {4,5} pixels at 12 px/° | 633 | 363 | 130 | 48 | 17.5 | 6.4 | 2.4 |
| {4,6} width | 83.2° | 45.9° | 16.2° | 5.94° | 2.18° | 0.80° | 0.30° |

- 12 px/° assumes a 2796×1290 panel split in two, render scale 0.75–0.8 and a ≈ 90–100° lens field of view `[E]`.
- Width shrinks like 1/sinh d (halving every 0.7 units), but in H²×E a wall's height shrinks only like 1/d: a 3 m wall at 5 units is still 31° tall at L = 1.0 m and 15° at L = 2.2 m `[D]`. Far tiles are thin tall slivers. Below ≈ 6 px wide (d > 6) a tile carries no readable detail.

### B3. LOD ladder (log-map warp)

The log map bends straight edges, so a tile's floor needs an n×n grid. I measured the worst screen-space error between the true warped mid-point of a grid segment and the straight line the GPU draws, from eye height 1.6 m, for a grid that is straight in the Klein model (so grid lines are true geodesics) `[D]`:

| Tile-centre distance | Tiles | n = 2 | n = 4 | n = 6 | n = 8 | n = 12 |
|---|---|---|---|---|---|---|
| 0–1.8 (own tile + 12 around it) | 13 | 0.85–1.51° | 0.33–0.61° | 0.17–0.32° | 0.10–0.20° | 0.05–0.10° |
| 1.8–3.2 | 44 | 0.62–0.77° | 0.22–0.28° | 0.11–0.14° | 0.07–0.09° | 0.03–0.04° |
| 3.2–4.0 | 72 | 0.27–0.45° | 0.11–0.15° | 0.06–0.07° | 0.03–0.04° | 0.02° |
| 4.0–5.0 | 244 | 0.20–0.32° | 0.08–0.10° | 0.04–0.05° | 0.03° | 0.01° |
| 5.0–6.0 | 608 | 0.10–0.22° | 0.03–0.07° | 0.02–0.04° | 0.01–0.02° | < 0.01° |

Ranges cover L = 0.8, 1.37 and 2.2 m. Target ≤ 0.15° (≈ 2 px). Standing on a vertex of the own tile at L = 2.2 m needs n = 12–16.

| LOD | Centre distance ({4,5}) | Grid n | Triangles per tile (floor + ceiling + 4 walls = 4n² + 8n) | Content |
|---|---|---|---|---|
| 0 | < 1.8 | 12 (16 if L ≥ 2 m) | 672 | Full shell, hero prop, small props, signage |
| 1 | 1.8–3.2 | 6 | 192 | Shell + one silhouette prop |
| 2 | 3.2–4.2 | 4 | 96 | Shell only, grid lines from the shader |
| 3 | 4.2–5.2 | 2 | 32 | Shell, flat colour, mostly fogged |
| 4 | > 5.2 | 1 | 12 | Floor quad + walls, fog ≥ 70% |

- Walls need horizontal subdivision only; height passes straight through the warp.
- For {4,6} scale the band limits by ≈ 1.3 (2.4 / 3.6 / 4.6 / 5.6).
- **The Blender tile is not a Euclidean square.** Generate it by script: a uniform grid in Klein coordinates (u, v) ∈ [−tanh r, tanh r]², each vertex placed at `posOS.xz = L · atanh(ρ)/ρ · (u, v)` with ρ = |(u,v)|. Then `ExpH2(posOS.xz/L)` in the shader lands on the true geodesic grid `[D]`. tanh r = 0.4859 for {4,5}, which is HyperEngine's `KLEIN_V`/`CELL_WIDTH` `[V]`. Adjacent tiles then share edge vertices exactly (no cracks) if all LODs keep the same boundary vertices or the coarser LOD's boundary points are a subset.

### B4. Draw calls and triangles per eye

Tile shells only, worst of the three viewer positions `[D]`:

**{4,5}**

| Cull radius | Tiles drawn, no FOV cull | Triangles per eye, no FOV cull | Tiles in 100° FOV | Triangles per eye, FOV-culled | Draws per eye, one per tile | Draws per eye, instanced |
|---|---|---|---|---|---|---|
| 2 | 33 | 12.6k | 14 | 5.6k | 14 | 2 |
| 3 | 105 | 22.1k | 37 | 9.2k | 37 | 3 |
| 4 | 260 | 29.2k | 85 | 10.8k | 85 | 4 |
| 5 | 780 | 39.9k | 229 | 14.9k | 229 | 5 |
| 6 | 2,041 | 56.8k | 580 | 18.0k | 580 | 5 (+1–2 chunks) |

**{4,6}**: 30 / 77 / 240 / 556 / 1,502 tiles drawn; 15 / 30 / 77 / 173 / 447 in FOV; 14–46k triangles unculled.

Reading the table against the Build Pipeline budget (< 300 draws for both eyes, i.e. 150 per eye; 300–500k triangles per frame, i.e. 150–250k per eye):
- **Triangles are not the constraint.** Shells cost < 60k per eye even unculled at radius 6. That leaves ≥ 100k per eye for props: e.g. 6 visible LOD0 tiles × 8k + 15 visible LOD1 tiles × 1.5k ≈ 70k.
- **One draw per tile** fits at radius 4 (85 per eye, 170 both eyes, 130 left for props, portals, UI) only if each tile is one merged mesh. It does not fit at radius 5 (229 per eye).
- **Instanced** fits at any radius; the limit becomes fog and taste.

### B5. Fog curve

- Per vertex, after the warp: `dH = length(LogH2(p))` (units), `fog = smoothstep(d0, d1, dH)`, colour = the wing's fog hue. Make `d1` equal the CPU cull radius so tiles are fully fogged before they pop.
- Lightness can also step per graph ring (the Build Pipeline's "lightness steps down with copy depth"), which doubles as a readable depth cue.

| Tier | Path | d0 → d1 (units) | Tiles in FOV | View distance at L = 1.0 / 1.37 / 2.2 m |
|---|---|---|---|---|
| Nominal, instanced | B | 3.5 → 5.5 | ≈ 350 | 5.5 / 7.5 / 12.1 m |
| Nominal, one draw per tile | A | 2.5 → 4.0 | ≈ 85 | 4.0 / 5.5 / 8.8 m |
| Thermal "fair" | both | −0.5 on both | ≈ ×0.6 | |
| Thermal "serious" | both | −1.5 on both (3.0 → 4.0 instanced, 1.5 → 2.5 per-tile) | ≈ ×0.2 | |

Each unit removed cuts the tile count by ≈ 2.7×, so the fog distance is a very strong thermal lever. Note the view distance in metres is d1·L: a small L (strong curvature) also means a short visible world.

### B6. GPU instancing vs "one material per room + SRP Batcher"

What the Unity docs say `[V]`:
- SRP Batcher "reduces render-state changes between draw calls"; it does not reduce the number of draw calls. "You can still use as many different materials with the same shader as you want."
- "If you want to render many identical meshes with the exact same material, GPU instancing can be more efficient than the SRP Batcher." Two ways: `Graphics.RenderMeshInstanced`, or remove SRP Batcher compatibility. "Use the Profiler to make sure."
- `Graphics.RenderMeshInstanced`: instance data is `Matrix4x4[]` or a struct with `objectToWorld` (mandatory), optional `prevObjectToWorld`, `renderingLayerMask`; **1,023 instances per call (511 when `worldToObject` is included)**; `Material.enableInstancing` must be true; "Unity automatically calculates bounds for all instances" unless `RenderParams.worldBounds` is set; the whole call is culled and sorted "as a single entity".
- `RenderParams.worldBounds`: "Used to cull and sort the rendered geometry." `RenderParams.camera`: "If set to null (default) renders for all cameras."
- For GameObjects, "GPU instancing works with custom shaders only if you disable the SRP Batcher" (i.e. a batcher-compatible MeshRenderer is never instanced automatically).

| | A. One GameObject + one material instance per tile (Build Pipeline rule as written) | B. `RenderMeshInstanced`, one call per LOD mesh |
|---|---|---|
| Draw calls per eye | = visible tiles (85 at radius 4; 229 at radius 5) | = LOD meshes (≤ 5), plus one per extra 511 instances |
| Per-frame CPU | Only the global `_HypCamInv` changes; per-material `_RoomAnchor` changes at re-root. Must toggle `renderer.enabled` for our own culling | Fill ≤ 600 `Matrix4x4` (38 KB) per frame; no renderers, no GameObjects |
| Culling | Unity's frustum culling is wrong for warped meshes: needs `localBounds` huge (Build Pipeline) or `camera.cullingMatrix` huge (HyperEngine `[V]`); then every enabled renderer is submitted | `worldBounds` huge and centred on the head; our list is the culling |
| MaterialPropertyBlock | None | None (the matrix travels as `objectToWorld`) |
| Precision | Anchor must be relative to the current tile, else float cancellation (§B7) | One camera-relative matrix computed in double, cast once |
| Unique content (hub, lung chambers, scanned room) | Natural | Awkward: stays on path A |
| Risk | Draw-call ceiling at radius ≈ 4 | Shader needs `#pragma multi_compile_instancing` and the instanced variant of the warp; 30 minutes to prove on device |

**Verdict.** For dozens to hundreds of identical tile shells, B should win on the A17 Pro: 5–10 draws instead of 170–460, no renderer bookkeeping, and no culling hacks `[E]`. I could not find a published Metal/A17 measurement of SRP-batched draw cost, so this must be profiled. Spike: 250 tiles both ways, read render-thread ms in the Unity Profiler and the Metal HUD; take B if it saves ≥ 0.5 ms.

**How B stays compatible with the Build Pipeline** (one shader, two variants):
```hlsl
float4x4 _HypCamInv; float _HypL; float3 _HeadPosWS;                 // globals, as in the Build Pipeline
CBUFFER_START(UnityPerMaterial) float4x4 _RoomAnchor; CBUFFER_END    // path A: per-room material, SRP Batcher compatible
float3 WarpH2xE(float3 posOS) {
#ifdef UNITY_INSTANCING_ENABLED
  float3x3 A = (float3x3)UNITY_MATRIX_M;                             // path B: per-instance, already camera-relative
  float3 p = mul(A, ExpH2(posOS.xz / _HypL));
#else
  float3 p = mul((float3x3)_HypCamInv, mul((float3x3)_RoomAnchor, ExpH2(posOS.xz / _HypL)));
#endif
  float2 t = LogH2(p) * _HypL;
  return float3(_HeadPosWS.x + t.x, posOS.y, _HeadPosWS.z + t.y);
}
```
```csharp
var rp = new RenderParams(tileMaterial) { worldBounds = new Bounds(headPos, Vector3.one * 2000f),
          shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, camera = null };
for (int lod = 0; lod < 5; lod++)
  for (int start = 0; start < count[lod]; start += 511)
    Graphics.RenderMeshInstanced(rp, lodMesh[lod], 0, matrices[lod], Mathf.Min(511, count[lod] - start), start);
```
Other caveats of warped geometry:
- Unity's opaque front-to-back sort uses bounds, which are meaningless here. Issue LOD0 first (render queue Geometry+0, +1, … per LOD). On Apple's tile-based GPUs hidden-surface removal makes opaque order largely irrelevant `[S]`; keep materials that `clip()` (portal clipping) last.
- Baked occlusion culling off, static batching off for warped meshes (Build Pipeline §1.5).
- With the two-camera rig, compute the visible set once per head (the eye offset is Euclidean and tiny), not per eye.

### B7. Float precision: where the "horizon" really is

- Absolute anchors in float (both `_HypCamInv` and `_RoomAnchor` measured from the world origin): error in the relative transform is 2e-5 units at 3 units from the origin, **1.3e-3 at 5, 4.3e-2 at 7, garbage at 9** `[D]`. This is the Build Pipeline's "precision horizon at 5–8 units".
- Tile-relative anchors (anchor = `t.rel`, camera = tile-local `C_local`), or one combined matrix from doubles: error ≈ 1e-8 units out to 12 units `[D]`. No horizon inside any sensible draw distance.
- Rule: **no float matrix ever represents a transform relative to the world root.**

---

## C. Portal / room graphs

### C1. Data structure

```csharp
sealed class Room {
  public int id; public ulong seed; public string module;         // module = authored or generated room type
  public Vector2Int[] cells;                                      // footprint on the play-space grid (0.5 m, Build Pipeline)
  public Bounds boundsPS;                                         // play-space AABB of the geometry
  public List<Portal> portals = new();
  public GameObject root; public int layerSlot = -1;              // -1 hidden, 0 current, 1..15 seen through a portal
}
sealed class Portal {
  public int id; public Room from, to; public Portal twin;
  public Vector3[] quadPS = new Vector3[4];                       // doorway corners in play space, CCW seen from 'from'
  public Plane planePS;                                           // normal points into 'from'
  public Matrix4x4 T = Matrix4x4.identity;                        // from-room coords -> to-room coords
  public bool open; public Vector2Int cellFrom, cellTo;           // the two grid cells the door joins
}
sealed class RoomGraph { public List<Room> rooms; public Room start; public Polygon2D walkable, walkableEroded; }
```
Design decision that removes most failure modes: **every room is authored or generated directly in play-space coordinates, so `T` = identity for every portal.** A portal only switches which layer is drawn behind the doorway. This is the "self-overlapping architecture" form of impossible spaces and matches the Build Pipeline's stencil recipe (layer RoomN drawn with `Stencil Equal N`). Non-identity `T` is needed only for the Glued Room (deck translations, handled by explicit copies in the Build Pipeline) and for deliberately rotating portals (stretch).

Stencil facts:
- "In URP, you can use bits 0 to 3 of the stencil buffer for custom rendering effects. This means you can use stencil indices 0 to 15." `[V]` So 0 = the room you are in, 1–15 = rooms seen through doorways.
- Stencil portals are confirmed over render textures; one level of recursion at most; "portal box" when one eye has crossed (Build Pipeline §0.5).
- Holonomy needed seven views for its corner cases and used stencil polygons for the same reason `[V]`.

### C2. Visibility traversal (depth ≤ 1)

```csharp
void BuildVisibleRooms(Room cur, Pose head, Camera eyeL, Camera eyeR, List<VisibleRoom> outList) {
  outList.Clear(); outList.Add(new VisibleRoom(cur, slot: 0, planes: UnionFrustum(eyeL, eyeR)));
  candidates.Clear();
  foreach (Portal p in cur.portals) {
    if (!p.open) continue;                                           // closed door: draw the door panel, nothing behind
    float side = p.planePS.GetDistanceToPoint(head.position);
    if (side < -0.05f) continue;                                     // behind the doorway
    Rect rect = side < 0.10f ? FullScreen                            // "portal box": too close to clip reliably
                             : Union(ScreenRect(p.quadPS, eyeL), ScreenRect(p.quadPS, eyeR));   // clip quad to near plane first
    if (rect.width <= 0 || rect.height <= 0) continue;
    candidates.Add((p, rect)); }
  candidates.Sort(byRectAreaDescending);
  for (int i = 0; i < candidates.Count && i < 15; i++) {
    Portal p = candidates[i].p;
    Plane[] planes = FrustumThroughQuad(head.position, p.quadPS, margin: ipd * 0.5f);   // 4 side planes + portal plane as near
    outList.Add(new VisibleRoom(p.to, slot: i + 1, planes, clip: p.twin.planePS)); }
}
```
- For each visible room: set its renderers to layer `PortalRoom{slot}`, set `_PortalPlane` on its material, enable only renderers passing `GeometryUtility.TestPlanesAABB(planes, bounds)`.
- Rooms not in the list are disabled. This list is the room wing's per-frame visible-set graph; its length is the stencil-pass count.
- Screen-rectangle culling through portals is the classic Luebke–Georges "cull box" method `[S]`.
- Because recursion is capped at 1, a doorway of room B that is visible *through* the doorway from A must show something other than room C. Either keep such doors closed until the user is inside B, or forbid the sightline in generation (check V8).
- Room swap (which room is "current") follows the Build Pipeline: swap only when the old room fails the frustum test for both eyes and the user is outside the overlap, with a few frames of hysteresis.

### C3. Validity checks for generated layouts

Inputs: the eroded walkable polygon W (scan → floor polygon → erode 0.3–0.5 m, Spec §6.4) rasterised to free cells F on the 0.5 m grid.

| # | Check | Rule |
|---|---|---|
| V1 | Twin symmetry | Every portal has a twin; `twin.twin == p`; `twin.T · p.T = I` to 1e-4; quads coincide in play space |
| V2 | Aperture | Door width ≥ 0.8 m, height ≥ 2.0 m, both sides equal, floor heights equal |
| V3 | **Fits the scan** | Every cell of every room ∈ F; every wall vertex inside W |
| V4 | Door clearance | Both cells adjacent to a door are free and belong to the two rooms. Tea For God's rule: "there should be enough space on the other side of the door, to put another room. It can be as small as a single tile" `[V]` |
| V5 | **Reachability** | BFS from the start room over portals reaches every room; with one-way or locked doors, every room can also reach the exit (strongly connected, or key before lock) |
| V6 | Walkable interior | Each room's cells are 4-connected; every door is reachable inside the room through a corridor ≥ 2 cells (1.0 m) wide |
| V7 | Stencil budget | At most 15 doorways per room; at most 4 expected in view |
| V8 | Depth-1 sufficiency | For every pair (door in, door out) of a room, the out-door is not visible through the in-door from the neighbouring room, or it is flagged "closed until entered" |
| V9 | **Loop consistency** | For every fundamental cycle of the graph, the composed `T` is the identity. Trivially true with identity portals. If rotating portals are enabled, a non-identity cycle is allowed only when flagged as intended and the first and last rooms are never co-visible |
| V10 | Overlap is intended | Rooms may overlap in play space. Report the overlap ratio per connected pair; Spec §6.4 quotes ≈ 56% as tolerated for small rooms `[S]` (I could not re-fetch Suma et al. 2012) |
| V11 | Determinism | Same seed and same F → identical graph hash |
| V12 | Re-fit | If the user rescans and F changes, re-validate; rooms that fail V3 are regenerated from the same seed with the new F |

Prior art:
- **Tea For God** `[V]`: "The play area is built around the idea of a tile. It is divided into a grid"; "a single room cannot be smaller than a single tile"; when doors do not line up "we build a corridor. Or two. Or more … we can put an elevator"; "most of the rooms overlap each other anyway".
- **Scenograph** (Marwecki & Baudisch, UIST 2018) `[V]`: a "tracking volume-independent representation" that "instantiates the experience to a tracking volume of given size and shape by splitting the locations into smaller ones while maintaining narrative structure" (three 25 m² locations became six in an L-shaped 8 m² space). The takeaway for us: keep the logical graph independent of the room, and derive the physical rooms from F at scan time.

Glued Room (torus / Klein) as a graph: one room, two generators T_a, T_b. Tests: torus T_a·T_b = T_b·T_a; Klein T_b·T_a·T_b⁻¹·T_a = I; copies (i, j) placed by T_a^i·T_b^j with mirrored parity for Klein.

### C4. Tests

- Fuzz: 1,000 seeds × 5 walkable polygons (rectangle 3×3, 4×5, L-shape, room with a pillar, narrow 2×6): every output passes V1–V11, or the generator returns its fallback (single room + corridor loop) and logs the seed.
- Golden: three hand-built layouts (two rooms sharing one footprint; three-room loop; hub with four doors) with expected visible-room lists for scripted head poses.
- Property: after any sequence of door crossings the "current room" equals the room found by replaying the crossings on the graph.

---

## D. Trees in the hyperbolic plane

### D1. The theory, verified

- **Lamping, Rao & Pirolli (CHI '95)** `[V]`: "A node is allocated a wedge of the hyperbolic plane, angling out from itself"; children are "placed along an arc in that wedge, at an equal distance from itself, and far enough out so that the children are some minimum distance apart from each other"; "each child will typically get a wedge that spans about as big an angle as does its parent's wedge". They also describe the holonomy problem: "a series of translations forming a closed loop … will, in general, cause a rotation".
- **Sarkar (GD 2011)** `[V]`:
  - Angle of parallelism: tan(Π(x)/2) = e^{−x}.
  - Lemma 1: if two endpoints carry cones of half-angle γ and the edge length is ≥ **−2·ln tan(γ/2)**, the perpendicular bisector lies inside both cones.
  - Algorithm: embed the root; place each child inside its own cone; cones at a vertex are disjoint, including the cone pointing back at the parent. Voronoi cells of different subtrees are then disjoint, so the tree is the Delaunay graph of its vertices (Theorem 3) and "a Delaunay embedding guarantees delivery by greedy routing" (Theorem 4).
  - (1+ε) distortion: choose separation β < π/d, ν = −2·ln tan(β/2), and scale so every edge is longer than ν(1+ε)/ε.
- **Kleinberg (INFOCOM 2007)**: every finite connected graph has a greedy embedding in the hyperbolic plane, via a spanning tree `[S]`; Sarkar notes Kleinberg's embedding "is a specific instance of Delaunay embedding" `[V]`.

Minimum edge length for a vertex of degree D with equally spaced edges (cone half-angle π/D) `[D]` from Sarkar's lemma:

**ℓ_min(D) = −2·ln tan(π/(2D)) = 2·acosh(1 / sin(π/D))**

| Degree D | 3 (binary fork, 120°) | 4 (T-fork or cross, 90°) | 5 | 6 |
|---|---|---|---|---|
| ℓ_min (units) | **ln 3 = 1.0986** | **1.7627** | 2.2484 | 2.6339 |

- The ln 3 bound: cosh(ℓ/2)·sin(π/3) ≥ 1 ⇔ ℓ ≥ 2·acosh(2/√3) = ln 3 ✓ (verified to 1e-15). At equality the tree is the dual of the ideal-triangle tiling {∞,3}; below it the faces close into finite polygons and the tree collides with itself.
- General form (unequal angles and lengths) `[D]`: for two edges of lengths a, b meeting at angle θ, with third side c from cosh c = cosh a·cosh b − sinh a·sinh b·cos θ, require **sinh(c/2) ≥ sinh(a/2) + sinh(b/2)** (the triangle has no circumcircle, so the two perpendicular bisectors do not meet). For a = b = ℓ this reduces to cosh(ℓ/2)·sin(θ/2) ≥ 1. It is sufficient, not necessary.

Numerical check, full binary tree to depth 8 `[D]`:

| Layout | ℓ (units) | Edge crossings | Min clearance between non-adjacent edges |
|---|---|---|---|
| 120° forks | 0.693 (= ln 2, the Spec's value) | **372** | 0.01 |
| 120° forks | 0.900 | 81 | 0.02 |
| 120° forks | 1.0986 (= ln 3) | 0 | 1.10 (= ℓ) |
| 120° forks | 1.500 | 0 | 1.50 |
| 90° T-forks | 1.0613 (one {4,5} tile) | 314 | 0 |
| 90° T-forks | 1.7627 | 0 | 1.76 |
| 90° T-forks | 2.1226 (two {4,5} tiles) | 0 | 2.12 |
| Spec §6.7 wedge layout, SHRINK = 1 | 0.682 | 0 | 0.02 (nodes of different branches 0.009 apart) |
| Spec §6.7 wedge layout, SHRINK = 2 | 0.682 | 22 | 0.06 |

So the corridor clearance of a valid layout equals ℓ: corridors of width w need ℓ ≥ w + wall thickness in addition to the bound.

**Consequence for the Spec.** R = ℓ/ln b (b = 2) is the growth condition (2ⁿ branches must fit on a circle of length ∝ e^{nℓ/R}); it is necessary, not sufficient. With 120° forks the exact condition is **L ≤ ℓ_metres / ln 3**: for 1.5 m corridors L ≤ 1.37 m, not 2.2 m. With 90° forks L ≤ ℓ_metres / 1.7627.

### D2. Airway models (what the tree looks like)

- Weibel's symmetric model "A": trachea = generation 0, dichotomous to generation 23; diameters follow d(z) = d₀·2^{−z/3}, a factor 0.79 per generation `[S]`. 2²³ ≈ 8.4 million terminal branches.
- Horsfield-type models are asymmetric: each fork has a major and a minor child with different length, diameter and angle `[S]`.
- TotalSegmentator reaches only generations 1–2, so the weekend tree is procedural and labelled "illustrative" (Build Pipeline §0.5).

What H² buys, stated for the docs: in Euclidean space a 23-generation tree with constant corridor width cannot be drawn (it needs e^{n} room on a circle that grows like n). In H² every corridor has the same walkable width at every generation and no two branches meet. That is the geometry doing real work.

### D3. Recommended: snap the tree to the {4,5} tiling

Any subtree of the tile adjacency graph is automatically overlap-free, because the tiling is a planar graph. So the only thing to prove is that the map tree-node → tile is injective. Brute force with exact ids `[D]`:

| Rule | Tiles per generation | Result |
|---|---|---|
| **S (symmetric, Weibel-like):** each child = turn L or R, then one straight tile; the next fork is at the end of the straight tile | 2 | **Injective to depth 14** (32,767 nodes, 65,533 tiles). Fork spacing 2.1226 units > 1.7627, so Sarkar's lemma (cones of 44°) guarantees it at every depth, and planarity of a Delaunay tree extends that to the corridor tiles `[D]` |
| S with two straight tiles | 3 | Injective to depth 14 |
| Pure L / R every tile | 1 | **Fails** at depth 4 (R·L·L·L collides) — consistent with 1.0613 < 1.7627 |
| **A (asymmetric, Horsfield-like):** children = go straight, or turn opposite to the previous turn | 1 | Injective to depth 15 (65,535 nodes). No analytic guarantee beyond that; fine for a walk that never goes deeper than 15 from its start |
| Always {F, L} | 1 | Fails at depth 4 (four lefts circle a vertex) |
| {L,R} after F; {F,R} after L; {F,L} after R | 1 | Fails at depth 5 |

Layout algorithm (Rule S), no global coordinates:
```csharp
// Address = string over {L,R}, one letter per generation. Node tile = fork chamber.
XFrame FrameOf(string address, XFrame trachea) {
  XFrame M = trachea;                                  // edge 0 of the frame points back (toward the mouth)
  foreach (char c in address) M = M.Step(c == 'L' ? 3 : 1).Step(2);   // turn tile, then straight tile
  return M; }                                          // .Centre is the exact tile id of that fork
// After any Step, edge 0 points back, so edge 2 is straight on, edge 3 is the walker's left and edge 1 the right.
// Checked with exactly this convention: 32,767 nodes, 65,533 tiles, no collision.
```
- Lazy window: materialise only the current node, its ancestors up to 2 levels and descendants to depth 3–4 (≤ 60 nodes, ≤ 120 tiles). Never build 2²³ nodes.
- Tiles that are not in the tree are solid tissue: draw only the walls of tree tiles (a wall on every tile edge that is not a tree edge).
- Variable segment length: use 1–3 straight tiles per generation from the model's length; any fork spacing ≥ 2 tiles keeps the guarantee.
- Depth and id width: 23 generations × 2 tiles = 46 tile-steps from the trachea, which is beyond `long` (≈ 40). Use the 128-bit add-only integer or `BigInteger` for the lung, or root the ids at the lobar bronchus.
- Metres: one generation is 2.1226·L (2.12 m at L = 1.0, 2.91 m at L = 1.37); corridor width up to the tile edge 1.2537·L.

Routing and the beacon:
- In a tree with addresses, the next step toward a target is a string comparison: if the current address is a prefix of the target, take the next letter; otherwise go to the parent. Exact and O(1).
- Greedy geometric routing ("step to the neighbour closer to the target in hyperbolic distance") gives the same answer on a Delaunay embedding (Sarkar Theorem 4) and is worth showing as the *reason* the beacon arrow works. The arrow direction is `atan2(y, x)` of the target in the head frame, composed along the tree path in double; cosh of the largest leaf-to-leaf distance (≈ 98 units) is ≈ 1e42, well inside double range `[D]`.

### D4. General trees (the challenge's "hyperbolic file browser / mind-map")

Continuous layout with guarantees (Sarkar's construction with explicit numbers):
```csharp
// each node stores only its isometry relative to its parent
void Layout(Node v, double incomingDir /* direction back to parent, in v's frame; root: none */) {
  int D = v.children.Count + (v.parent != null ? 1 : 0);
  double sector = 2 * PI / D;                                  // equal sectors; parent keeps one
  for (int i = 0; i < v.children.Count; i++) {
    double theta = incomingDir + sector * (i + 1);
    double alpha = 0.5 * Min(sector, 2 * PI / (v.children[i].children.Count + 1)) - BETA;   // usable cone half-angle
    double len = Max(v.children[i].desiredLen, -2 * Log(Tan(alpha / 2)) * (1 + MARGIN));
    v.children[i].relToParent = mul(Rot(theta), Boost(len), Rot(PI));    // child frame: edge back to parent at angle 0
    Layout(v.children[i], 0); } }
```
- BETA = 5–10° separation gives distortion control (Sarkar §5); MARGIN = 5%.
- Weighted variant: compute η_max = max over edges of ℓ_min/weight, scale all weights by it (Sarkar §4).
- High-degree nodes need long edges (D = 10 → 3.7 units). For folders with many children use Lamping–Rao's rule instead: put children on an arc at equal distance, far enough that neighbours are ≥ one corridor width apart, and give each child a sub-wedge.
- Always validate with the bisector inequality from D1 on every pair of edges sharing a vertex; reject and lengthen if it fails.

---

## E. Graph-quality test suite, metrics and debug views

### E1. Edit-Mode / CI suite

| Group | Tests | Runtime target |
|---|---|---|
| Tiling | T1–T16 (§A7) for {4,5} and {4,6} | < 1 s |
| Visible set | Counts at d = 2, 3, 4, 5 equal the §B1 table (viewer at centre: 33, 97, 237, 737 touching); every tile whose nearest point is within d is in the list (no false culls) for 500 random poses; LOD is monotone in distance | < 0.5 s |
| Portal graph | V1–V12 (§C3) fuzz and golden layouts | < 1 s |
| Tree | Rule S injective to depth 12; random addresses round-trip (address → frame → walk back to trachea); bisector inequality on a random continuous layout | < 0.5 s |
| Determinism | Golden seeds; graph hash stable across Mono and IL2CPP | — |

### E2. Runtime metrics (shown on a debug overlay and logged every second)

| Metric | Budget | Tag |
|---|---|---|
| Initial fill to K = 7 (1,377 tiles) | ≤ 20 ms once, at scene load | `[E]` |
| Exact neighbour step | ≤ 0.1 µs with `long`, ≤ 3 µs with `BigInteger` | `[E]` |
| Re-root (BFS over the active set + new tiles) | ≤ 1.0 ms; if more, time-slice the outermost ring at ≤ 0.5 ms per frame (it is inside full fog) | `[E]` |
| Per-frame visible-set build (≤ 1,377 tiles: one 3×3 product, asinh, atan2 each) | ≤ 0.3 ms main thread (≤ 0.1 ms with Burst) | `[E]` |
| Portal traversal | ≤ 0.05 ms | `[E]` |
| GC allocations per frame from graph code | 0 bytes (pre-sized lists, structs) | target |
| Tiles alive | ≤ 20,000 (≈ 5 MB at ≈ 260 bytes per tile) | `[E]` |
| Instances drawn per eye / draw calls per eye | ≤ 600 / ≤ 12 (path B); ≤ 85 / ≤ 85 (path A) | from §B4 |
| Triangles per eye (shells) | ≤ 60k | from §B4 |
| Frame-time impact of the whole graph system | ≤ 0.5 ms CPU of the 16.7 ms frame | target |
| Invariant monitors (always on in development builds) | ‖CᵀηC − η‖ < 1e-9 after re-orthonormalisation; head inside the current tile + hysteresis; no re-root ping-pong (> 4 re-roots in 10 frames = error) | — |

Also keep the Build Pipeline's safeguards in the same component: reject pose jumps > 0.2 m per frame before they reach `Boost(Δ)`; compute the boost once per head.

### E3. Debug visualisations that double as the "flat twin"

**Poincaré-disk mini-map of the tile graph.**
- Projection: hyperboloid (x, y, t) → disk (x, y)/(1 + t) `[D]`.
- For each tile in the active set, take `Mv = V · t.rel`, map the four corners `Mv · v_i` (v_i = (sinh R·cos(π/4 + iπ/2), sinh R·sin(…), cosh R)) and draw each edge as 4–6 points along the geodesic `normalize((1−s)A + sB)` so it appears as the correct arc.
- Colour by LOD band or graph ring; outline the current tile; tint visited tiles; mark tile corners with landmarks (Holonomy's trick, Build Pipeline §2).
- User path: breadcrumbs every 0.25 units, stored in the current root's frame and multiplied by the inverse step matrix at each re-root (keep the last 200).
- Render with `GL.Lines` or one dynamic mesh on a HUD quad; same code runs in the Editor Scene view.

**Flat twin.** Next to it, draw the physical floor polygon with the user's real path. A closed loop on the floor is an open path in the disk. Example to script for the demo: walking a square whose side is one tile edge, with four 90° left turns, returns you to the start on the floor but leaves you **1.79 units (1.43 tile edges) from the start** in {4,5} `[D]`. For the lung, the same mini-map shows the airway tree in the disk (the classic hyperbolic-browser picture) with the user's branch highlighted.

Other toggles: draw tile ids' low 16 bits as colours (duplicate tiles would show as a colour mismatch at an edge); freeze-culling (stop updating the visible set and look around to see what was culled); per-LOD wireframe; portal room slots as colours; tree address as text at each fork.

---

## F. Scope for a 4-person, ~36 h build

| Level | Tiling graph | Rendering of the graph | Rooms / portals | Trees | Tests / debug |
|---|---|---|---|---|---|
| **Minimal** (must exist at end of Phase 1) | Exact ℤ[φ] ids with `long`, lazy `Neighbor`, re-rooting, active set K = 6 | Path A or B, single LOD (n = 8), cull radius 3–4, linear fog | 2–3 hand-placed rooms in play space, identity portals, V1–V5 | — | T1–T9, growth test, disk mini-map in the Editor |
| **Target** | + 128-bit or `BigInteger`, eviction, content seeds, {4,6} switch | Path B instanced, 5 LODs from the Blender script, cull radius 5.5, thermal fog tiers | Generated rooms fitted to the scanned polygon, V1–V12, depth-1 traversal with slots | Rule S airway snapped to tiles, lazy window, address routing, beacon | Full suite in CI, on-device overlay, mini-map + flat twin in the headset |
| **Stretch** | Generic {p,q} via tolerance ids; WFC content keyed by seeds | Dithered LOD cross-fade; Burst visible-set job | Rotating portals with cycle classification | General Sarkar/Lamping–Rao layout for a file-browser or mind-map blade; Rule A asymmetric airway | Recorded-walk regression replay |

Dependency tree (→ = must precede; items on the same line can run in parallel):
```
Hour 0–1   agree interfaces: ITileGraph { Tile Root; Tile Neighbor(Tile,int); double3x3 StepMatrix(Tile,int); }
                             IVisibleSet { IReadOnlyList<Matrix4x4>[] PerLod; }   IRoomGraph { BuildVisibleRooms(...) }
           |
           +-- P1 maths/graph:   constants + generators (T1,T2,T5,T6) → XFrame + TileGraph (T3,T4,T7–T11) → re-root (T12,T13) → eviction, seeds
           +-- P2 rendering:     warp shader port with a FAKE visible set (45 tiles from a 30-line double BFS)  → instanced path + LODs → fog tiers
           +-- P3 rooms/AR:      scan → walkable polygon → grid F → Room/Portal graph + V-checks → stencil slots + traversal
           +-- P4 content/tools: Blender script for Klein-grid tile LODs → disk mini-map (uses the fake set first) → CI harness → airway Rule S on P1's graph
           |
Join 1     P1 TileGraph replaces P2's fake set  → first walk in H² on device (needs phone↔PC link only for logging)
Join 2     P1 re-root + P2 instancing + P4 mini-map → free-walk test in the scanned room, metrics overlay
Join 3     P4 airway addresses on P1 ids; P3 room graph on the scanned polygon (Phase 2 blades start here)
```
Strictly sequential: generators → `TileGraph` → active set → visible set → instanced draw. Everything else is parallel behind the three interfaces.

---

## Sources

Fetched and read (primary):
- Celińska-Kopczyńska & Kopczyński, *Generating Tree Structures for Hyperbolic Tessellations*, arXiv:2111.12040 — https://arxiv.org/abs/2111.12040 (PDF pp. 1–6)
- HyperRogue source: https://raw.githubusercontent.com/zenorogue/hyperrogue/master/heptagon.cpp · …/locations.cpp · …/hyperpoint.cpp
- Hypermine source: https://raw.githubusercontent.com/Ralith/hypermine/master/common/src/graph.rs · …/proto.rs · …/docs/world_generation.md
- HyperEngine source and manual: https://github.com/HackerPoet/HyperEngine (`TileMapper.cs`, `WorldBuilder.cs`, `HyperObject.cs`, `HyperMath.cs`, `HyperCore.cginc`, `H2xEShader.shader`, `DisableCameraCull.cs`, `HyperEngine.pdf`)
- Pisani et al., *Holonomy: A Virtual Environment based on Hyperbolic Space*, arXiv:2409.05460 — https://arxiv.org/html/2409.05460
- OEIS A377322, "Number of cells that are a distance of n away in an order-5 hyperbolic square tiling" — https://oeis.org/A377322
- Sarkar, *Low Distortion Delaunay Embedding of Trees in Hyperbolic Plane*, GD 2011 — https://homepages.inf.ed.ac.uk/rsarkar/papers/HyperbolicDelaunayFull.pdf
- Lamping, Rao & Pirolli, *A Focus+Context Technique Based on Hyperbolic Geometry for Visualizing Large Hierarchies*, CHI '95 — https://www.chi1995.chistatic.hosting.acm.org/proceedings/papers/jl_bdy.htm
- Unity 6 docs: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.RenderMeshInstanced.html · …/ScriptReference/RenderParams.html · …/Manual/SRPBatcher.html · …/Manual/SRPBatcher-Incompatible.html · …/Manual/GPUInstancing.html · …/Manual/urp/urp-universal-renderer.html
- Tea For God devlog — https://void-room.itch.io/tea-for-god/devlog/76304/procedural-level-generation-using-impossible-spaces
- Scenograph project page — https://hpi.de/baudisch/projects/scenograph.html

Secondary (search results, not fetched in full):
- Kleinberg, *Geographic Routing Using Hyperbolic Space*, INFOCOM 2007 — https://www.cs.cornell.edu/courses/cs789/2007sp/2.19.07.bobbyhtm.htm
- Luebke & Georges, *Portals and Mirrors*, I3D 1995 — https://en.wikipedia.org/wiki/Potentially_visible_set
- Weibel model summaries — https://www.institut-langevin.espci.fr/biblio/2024/8/19/2129/files/Florens_2011_JApplPhysiol.pdf
- Scenograph paper — https://hpi.de/oldsite/fileadmin/user_upload/fachgebiete/baudisch/projects/VirtualSpace/Scenograph_Online_Version.pdf

Project documents: `ATLAS_Build_Pipeline_assets.md` (all), `ATLAS_Engineering_Spec.md` §5.3 and §6, `ATLAS_Implementation_Brief.md` §3.8.

Computation scripts (Python, reproducible): `research/scripts/exact45.py` (exact ℤ[φ] tiling), `r3_a.py` (constants), `r3_b.py` and `r3_c.py` (growth sequence, Holonomy rule test, word language), `r3_d.py` and `r3_i.py` (tile counts, FOV, draws, triangles), `r3_e.py` (chord error / LOD), `r3_f.py` (tree-snapping rules), `r3_g.py` (tree bounds, Spec layout test), `r3_h.py` (invariants, integer growth, float precision), `r3_j.py` (the add-only step formulas and conventions exactly as printed in §A5 and §D3, and the reach of a bounded table).

---

## Could not verify

1. **Relative cost of SRP-batched draws vs instanced draws on Metal / A17 Pro.** No published figure found; the §B6 verdict is reasoning plus Unity's general statement. Needs the 250-tile spike.
2. **`System.Numerics.BigInteger` and any 128-bit integer type under Unity 6.3 IL2CPP on iOS.** I believe `BigInteger` is available with the .NET Standard 2.1 profile but did not check; the add-only 128-bit struct avoids the question.
3. **The 511 vs 1,023 instance limit** wording comes from the Unity 6.0 scripting reference as summarised by the fetch tool; chunk at 511 to be safe.
4. **Holonomy's rules as a full bijection.** Verified injective to length 10 and covering to graph distance 7 only. No public code exists (Build Pipeline also notes this).
5. **Rule A (asymmetric tree) beyond depth 15**, and Rule S's corridor-tile argument beyond the brute-forced depth 14 (the node-tile part is covered by Sarkar's theorem; the corridor-tile part is my planarity argument).
6. **{4,6} coordination sequence** 1, 4, 12, 32, 84, 220, … is computed, not found in OEIS by term search.
7. **Suma et al. 2012 overlap thresholds (56% / 31%)** and **Luebke & Georges 1995**: cited from the Spec and from search results; primary papers not fetched.
8. **Weibel / Horsfield numerical tables** (lengths, diameters, branching angles per generation): only the 23-generation structure and the 2^{−1/3} diameter law were confirmed, from secondary sources.
9. **Kleinberg 2007**: abstract-level confirmation via search and via Sarkar's description; the paper itself was not fetched.
10. **Apple GPU hidden-surface removal making opaque draw order irrelevant**: general knowledge of tile-based deferred renderers, not checked against Apple documentation today.
11. **12 px per degree** depends on the headset lenses and the final render scale.
12. All **millisecond budgets in §E2** are estimates from operation counts; none was measured on an iPhone.
13. The chord-error table assumes a grid that is straight in the Klein model and eye height 1.6 m; a Blender grid built differently (e.g. uniform in metres) will have different, probably slightly larger, errors.

## Questions for the team

1. **Scale L (metres per hyperbolic unit).** L = 0.8 m gives 1 m tiles and strong effects (a 1 m square walk displaces you by more than a tile) but the world fogs out at ≈ 4–4.5 m. L = 2.2 m gives 2.8 m tiles and a 12 m view, but curvature is subtle inside a 4×4 m room. Which do you want for the free-walk wing, and is it the same L as the lung?
2. **{4,5} or {4,6}?** {4,5} matches Holonomy and the Brief ("five squares at every corner"). {4,6} has plain-integer ids, bigger tiles (edge 1.76 units) and fewer tiles per view (556 vs 780 at radius 5). Should the engine ship both behind a switch?
3. **How far can a user get from the spawn tile?** If more than ≈ 40 tile-steps net is plausible (long sessions, or the 23-generation lung at 46 steps), we need 128-bit or `BigInteger` ids in Phase 1 rather than `long`. What should happen at the limit if we keep `long`: fade and re-centre, or a visible "edge of the mapped world"?
4. **Projection: log map (Build Pipeline default) or Klein?** The LOD ladder, the Blender tile script and the chord-error budget all assume the log map. The Build Pipeline marks this "team decision".
5. **Is instancing acceptable** as a deliberate exception to "one material per room, no MaterialPropertyBlocks", limited to tile shells and repeated props? It uses neither MPBs nor per-renderer state.
6. **What is a tile, visually, in the free-walk wing?** An open floor tile with corner landmarks (Holonomy style), or a walled room with doors? Walled rooms occlude most far tiles, which would let us shorten the cull radius a lot.
7. **How does "scan your room into hyperbolic space" map onto tiles?** (a) the scanned mesh is one object anchored to the spawn tile and warped by the exp map (it will stretch 3× at 3 units), (b) the scan is cut per tile, or (c) only scanned *objects* are placed into tiles while the room shell is generated. This decides whether the visible set needs a second, non-tile object list.
8. **Lung fork geometry:** 90° T-forks on the square tiling (Rule S, guaranteed, 2 tiles per generation) or anatomically flavoured asymmetric forks (Rule A, 1 tile per generation, verified to depth 15 only)? Do you need all 23 generations addressable, or is "trachea to a chosen segment, ≤ 12 generations" enough for the mission?
9. **Physical fit of long corridors.** A straight virtual corridor longer than the room cannot be walked. Is the plan Holonomy-style turning within the room, portal fork chambers (Spec §5.3), or a rotating reset at forks? The tree layout supports all three but the tile content differs.
10. **Minimum physical room.** The portal-room validity checks need a smallest supported walkable polygon (2×2 m? 3×3 m?) and a grid size (0.5 m per the Build Pipeline). What do we promise judges?
11. **Mini-map in the headset or PC-only?** Holonomy's users navigated with one; the Build Pipeline asks for one in the H² wing. In-headset costs one more draw and some HUD design.
12. **Who owns the interfaces in hour 0–1** (`ITileGraph`, `IVisibleSet`, `IRoomGraph`)? Everything parallelises only after they are frozen.
