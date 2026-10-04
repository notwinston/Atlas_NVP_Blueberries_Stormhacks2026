# Kernel contract (lane B)

The kernel types, the metric and unit conventions, the exact tile ids, and lane B's part of the shader contract (`AtlasWarp.hlsl`, `AtlasGates.hlsl` and lane B's property ids).

| | |
|---|---|
| Owner | Lane B · Kernel and Curved Worlds |
| Written by | `P1-X06`, from Master Plan §6.4, §6.5, §8.3 and Appendix A, with §3.4, §4.6, §4.7, §8.5, §9.5.6, §11.1 and Appendix B.1 where they touch the kernel |
| Frozen by | Gate G0 (`P1-X02`). After that, any change is announced to all four lanes before it is committed (§6.4) |
| Read by | Lane A (shared shader, `ISpaceModule`, SpaceLab), lane C (the room as tile zero), lane D (`State`, Spectator, flat-twin map) |
| Code it governs | `Atlas.Kernel` (`Assets/Atlas/Kernel/`); `AtlasWarp.hlsl` and `AtlasGates.hlsl` (`Assets/Atlas/Rendering/Shaders/`); lane B's entries in `AtlasShaderIDs.cs` (`Atlas.Rendering`) |

Tags on this page:

- **[plan]**: a name, signature or value the Master Plan fixes. Binding.
- **[P1-X06]**: chosen on this page because the plan is silent. This is the smallest choice that makes the [plan] items usable. It can change until G0; after G0 it changes only with notice.
- **[derived]**: follows from the plan's formulas. Every derived value on this page was checked numerically when the page was written.

Where this page and the Master Plan disagree, the Master Plan wins. Report the conflict to lane B.

---

## 1. What each lane needs from this page

| Lane | Reads | For |
|---|---|---|
| A | §2, §6 | The shared shader calls `AtlasWarp` in its vertex stage and `AtlasGatesClip` in its fragment stage. It declares lane B's per-material fields in the single `UnityPerMaterial` block. `ISpaceModule.Enter(in WalkerPose start)` uses `WalkerPose` (§4.5) |
| B | All | Implementation (`P1-B01` to `P1-B12`) |
| C | §2.3, §4.5, §9 | The RoomModel anchor frame is tile zero's frame. Home meshes, in metres relative to the room centre, are tile-local coordinates of tile zero (§4.6). L and R come from the loop-fit in `RoomModel.curvature` |
| D | §4, §5.4, §5.7, §9 | Packing of `State.tileId` and `State.xform` (§4.5). The Spectator redraws the view with `ITileGraph.VisibleSet` and the same warp. The flat-twin map uses tile centres |

---

## 2. Conventions

### 2.1 Coordinates and metric [plan, A.1]

- **Hyperbolic plane**: the unit hyperboloid H² = { p = (x, z, w) : ⟨p, p⟩ = −1, w > 0 }, with ⟨a, b⟩ = aₓbₓ + a_z b_z − a_w b_w. The metric is **η = diag(1, 1, −1)**.
- **Sphere**: the unit sphere with the ordinary dot product.
- **Flat**: homogeneous floor coordinates (x, z, 1) [derived, the κ = 0 limit of A.2's unified form].
- **Base point** o = (0, 0, 1) in all three. o is the tile centre in a tile's own frame, and the head in the walker's frame.
- **Component order.** x and z are the floor axes and match Unity's x and z. w is the third component. In Unity.Mathematics a point is a `double3` with `.x` = x, `.y` = **z (the floor's z axis, not height)** and `.z` = w. A tangent vector or step is a `double2` with `.x` = x and `.y` = z. HLSL uses the same layout (`p.xy` spatial, `p.z` = w). Height (Unity y) never enters the kernel. [derived, from §4.6 and BP §4]

### 2.2 κ, the three geometries [plan, A.1, §6.5]

| κ | Geometry | Curvature K | Length scale | Used by |
|---|---|---|---|---|
| −1 | Hyperbolic H² | −1/L² | L, metres | S2 Curved Walk, the tile graph, Lung airway (Phase 2) |
| 0 | Flat | 0 | none; the kernel's length unit is 1 m [P1-X06] | Flat spaces, S3 cells, the κ = 0 end of the dial |
| +1 | Spherical S² | +1/R² | R, metres | S4 Small Planet |

- κ is a sign, not a magnitude. It is −1, 0 or +1 only. How strong the curvature is depends on L (or R) alone. The curvature dial (`P1-B09`) changes L or R, never κ's magnitude.
- **L** is the hyperbolic curvature radius in metres (the Engineering Spec calls it R). **R** is the Small Planet's sphere radius. **ρ** is hyperbolic distance in units of L. **q** is the number of tiles at a corner (§0.4).
- Loop-fit (A.4, lane C computes it): L = s / 1.0613 for q = 5, L = s / 1.3170 for q = 6, R = 2s/π for q = 3.

### 2.3 Units [plan, A.1, §6.4]

| Where | Lengths | Angles |
|---|---|---|
| Inside `Atlas.Kernel` (`Iso2`, `Maps`, tile constants, `VisibleSet`'s `rho`) | Units of L (hyperbolic) or R (spherical); 1 m when κ = 0 | Radians |
| At the boundary | **Divide by L (or R) on the way in and multiply by L (or R) on the way out.** `Iso2.Boost` takes a step that is already divided by L (§6.5) | |
| Outside the kernel: world space, RoomModel, mesh object space, shader output, `_HeadPosWS`, `_HypL` | Metres | Radians in code; degrees only in UI text |

The Engineering Spec's §6.1 formulas keep R explicit, as in ⟨p, p⟩ = −R². Code does not do that: it uses the unit model and scales once at the boundary (Spec §5.3, "coordinate convention").

### 2.4 Turning sense and handedness [derived]

- `Iso2.Rot(θ)` with θ > 0 turns +x towards +z. Seen from above in Unity's left-handed, y-up frame, that is anticlockwise: **a left turn**. A.4's "walk s, turn left 90°" is `Boost(s/L)·Rot(π/2)`.
- Unity's own yaw has the opposite sense: `Quaternion.Euler(0, ψ, 0)` turns +z towards +x. So **`Rot(θ)` is a Unity yaw of −θ**. Convert explicitly wherever a head or object yaw enters the kernel.

### 2.5 Matrices [plan, §4.6; derived]

- An isometry is a 3 × 3 matrix M acting on column vectors: p′ = M·p. Products compose right to left.
- `Iso2.M` is a Unity.Mathematics `double3x3`. Its storage is column-major: `M.c0` is column 0, and the entry in row r, column c is `M[c][r]`. The row and column words on this page always mean the mathematical M[r, c].
- `inverse(M)` = η·Mᵀ·η in H² and Mᵀ on S² (§6.5). On the flat plane M = [[A, t], [0, 1]] with A a 2 × 2 rotation, and the inverse is [[Aᵀ, −Aᵀt], [0, 1]] [derived; the plan states only the curved cases].

---

## 3. Precision rules

1. **Double precision in the kernel.** Every isometry is a `double3x3`. Matrices are multiplied in double and cast to float last, only for upload to a material or for drawing (§4.6, `P1-B03`).
2. **No global coordinate.** A position is a tile id plus a local frame, `WalkerPose { Tile, Local }` (rule 7, §0.5). No floating-point coordinate is ever accumulated across tiles.
3. **U stays small.** U, the walker's frame, is always expressed in the tile the walker stands in. The walker is never more than about r_c = 0.8425 from that tile's centre, so U's entries stay near 1 however far the walker goes (§4.6).
4. **Renormalise every frame**: η-Gram–Schmidt in H², ordinary Gram–Schmidt on S² (A.2). Test `Lorentz_Drift`: ‖CᵀηC − η‖ < 1e-9 after 100,000 random boosts.
5. **F[t] is rebuilt, never accumulated.** The step chain from the current tile to tile t is a product of a few fixed step matrices in double. It is rebuilt from those constants whenever the walker changes tile (R3 §A5).
6. **Anchors are tile-relative.** The float matrix uploaded per tile is inverse(U)·F[t], one combined matrix computed in double. Absolute anchors measured from tile zero lose precision past about 5 units (R3 §B7). Test `Float32_Upload`: |⟨p, p⟩ + 1| < 1e-3 for tile-relative anchors at ρ = 4.35.
7. **Tile ids are exact integers.** There is no tolerance, so equality is exact. The invariant multiplies coefficients and must be evaluated in a 128-bit integer or `BigInteger`, never in `long` (§5.6).
8. **In the shader, positions stay in full `float`.** Exp is evaluated once per vertex, and sinh and cosh are derived from that one exponential (§8.3).

---

## 4. Kernel types (`Atlas.Kernel`)

`Atlas.Kernel` references only Unity.Mathematics, Burst and Collections. It has no scene types, so all of it runs in EditMode tests (§6.2). The namespace is `Atlas.Kernel`.

### 4.1 Signatures

```csharp
namespace Atlas.Kernel {

// ---- [plan] §6.5: names binding, bodies free -----------------------------------------
public readonly struct Iso2 {                       // 3×3 double; κ < 0 hyperbolic, 0 flat, > 0 spherical
    public static Iso2 Boost(double2 step, double kappa);   // move by a tangent vector (already divided by L)
    public static Iso2 Rot(double angle);
    public Iso2 Inverse();                                   // η·Mᵀ·η in H², transpose on S²
    public Iso2 Orthonormalised();
    public static Iso2 operator *(Iso2 a, Iso2 b);
}
public readonly struct TileId : IEquatable<TileId> {         // six Int64: X, Y, T in ℤ[φ] for {4,5}
    public TileId Step(int edge);                            // neighbour across edge 0..3; additions only
    public ulong ContentSeed(uint worldSeed);
}
public struct WalkerPose { public TileId Tile; public Iso2 Local; }   // never a global coordinate
public interface ITileGraph {
    int Q { get; }                                           // tiles per corner
    void VisibleSet(TileId centre, double rho, List<VisibleTile> into);   // id, step chain F[t], graph distance
}

// ---- [P1-X06] additions the plan's members need in order to be used -------------------
public readonly partial struct Iso2 {
    public Iso2(double3x3 m, double kappa);                  // from a matrix, e.g. State.xform on the Station
    public double3x3 M { get; }                              // the matrix (upload, State.xform)
    public double Kappa { get; }                             // −1, 0 or +1
    public static double3 operator *(Iso2 a, double3 p);     // apply to a point of the model
}
public static class Maps {                                    // P1-B01's "exp and log maps, distance" (§9.5.6)
    public static double3 Exp(double2 t, double kappa);      // tangent vector at o (units of L) → point
    public static double2 Log(double3 p, double kappa);      // point → tangent vector at o (units of L)
    public static double  Distance(double3 p, double3 q, double kappa);   // units of L
}
public readonly partial struct TileId {
    public static readonly TileId Zero;                      // tile zero: the room's tile
    public TileId(long xu, long xv, long yu, long yv, long tu, long tv);   // encoding of §5.4
    public readonly long Xu, Xv, Yu, Yv, Tu, Tv;             // X = Xu + Xv·φ, and so on
}
public readonly struct VisibleTile {                          // the §6.5 comment: id, step chain F[t], graph distance
    public VisibleTile(TileId id, Iso2 f, int graphDistance);
    public readonly TileId Id;
    public readonly Iso2 F;                                  // tile t's frame → the centre tile's frame
    public readonly int GraphDistance;                       // edge steps from the centre tile
}
public sealed class TileGraph : ITileGraph {                  // file name from the P1-B04 brief (§9.9)
    public TileGraph(int q);                                 // q = 5 exact ids; q = 6 tolerance variant; q = 3 via CubeTileGraph (P1-B08)
    // [P1-B04, lane-internal: not part of ITileGraph]
    public int Q { get; }
    public double InRadius { get; }                          // r (A.3)
    public double CircumRadius { get; }                      // r_c (A.3)
    public const double CrossingHysteresis = 0.02;           // units of L (R3 §A5)
    public void VisibleSet(TileId centre, double rho, List<VisibleTile> into);   // breadth-first order; clears `into`
    public TileId Neighbour(TileId tile, int edge);          // §4.6 Neighbour(tile, i); any q
    public Iso2 StepMatrix(TileId tile, int edge);           // §4.6 G[i]: neighbour's frame → tile's frame
    public int ExitEdge(in Iso2 local);                      // -1 inside the tile (with hysteresis)
    public bool Reroot(ref WalkerPose pose);                 // §4.6 crossing; the caller rebuilds VisibleSet when true
}
}
```

`partial` is used above only to keep the plan's block and the additions apart on this page. The code may declare each type in one piece.

### 4.2 `Iso2` [plan, A.2]

| Member | Meaning | Formula (A.2), d = ‖Δ‖, u = Δ/d |
|---|---|---|
| `Boost(Δ, −1)` | Translate by tangent vector Δ in H² | [[1+(c−1)uₓ², (c−1)uₓu_z, s·uₓ], [(c−1)uₓu_z, 1+(c−1)u_z², s·u_z], [s·uₓ, s·u_z, c]], with c = cosh d, s = sinh d |
| `Boost(Δ, +1)` | The same on S² | As above with c = cos d, s = sin d and last row [−s·uₓ, −s·u_z, c] |
| `Boost(Δ, 0)` | Flat translation (Δ in metres) | [[1, 0, Δₓ], [0, 1, Δ_z], [0, 0, 1]] [derived] |
| Unified | All three | Matrix exponential of [[0, 0, Δₓ], [0, 0, Δ_z], [−κΔₓ, −κΔ_z, 0]] |
| `Rot(θ)` | Rotate about o by θ (θ > 0 = left turn, §2.4) | [[cos θ, −sin θ, 0], [sin θ, cos θ, 0], [0, 0, 1]] (R3 §A3) |
| `Inverse()` | Inverse isometry | η·Mᵀ·η (κ = −1); Mᵀ (κ = +1); [[Aᵀ, −Aᵀt], [0, 1]] (κ = 0, §2.5) |
| `Orthonormalised()` | Back onto the group | Gram–Schmidt with respect to η (κ = −1), ordinary (κ = +1), on the 2 × 2 block with the last row reset to (0, 0, 1) (κ = 0). P1-B01 chooses the column order |
| `a * b` | Composition, a after b | Matrix product |

How κ is carried [P1-X06]: `Boost` records the κ it was given. `Rot` gives κ = 0, which is correct for a rotation in every geometry: its inverse is its transpose and its orthonormalisation is the 2 × 2 one. A product takes the non-zero κ of its factors. A product of κ = −1 with κ = +1 is a programming error, and development builds throw on it.

Tests (§11.1, lane B): `Kernel_ExpLog`, `Kernel_EtaOrthonormal`, `Kernel_Inverse`, `Holonomy_Triangle`, `Closure_345`, `Lorentz_Drift`, `Float32_Upload`.

### 4.3 `Maps` [plan for the formulas, A.2; P1-X06 for the names]

| | κ = −1 | κ = +1 | κ = 0 [derived] |
|---|---|---|---|
| `Exp(t)`, r = ‖t‖ | (sinh r · t/r, cosh r) | (sin r · t/r, cos r) | (t, 1) |
| `Log(p)` | asinh(‖p_xz‖) · p_xz/‖p_xz‖ | acos(p_w) · p_xz/‖p_xz‖ | p_xz |
| `Distance(p, q)` | cosh d = −⟨p, q⟩ | cos d = p·q | ‖p_xz − q_xz‖ |

At r = 0 (or ‖p_xz‖ = 0) both maps return the base point or the zero vector. On the sphere, atan2(‖p_xz‖, p_w) gives the same angle as acos(p_w) with better conditioning; the body is free. Test `Kernel_ExpLog`: exp∘log is the identity to < 1e-9 on 1,000 random points with ρ ≤ 4, and to < 1 mm after the float cast.

### 4.4 `TileId` [plan, §6.5, A.5]

- An id is six `Int64` words: the tile centre's coordinates X, Y and T, each in ℤ[φ] (§5.4).
- It is equal to another id when all six words are equal. Exact, no tolerance.
- `Step(edge)` is the neighbour across edge 0..3, numbered in this tile's own frame (§5.3). It uses additions only, plus multiplications by 0, ±1 and ±φ.
- `ContentSeed(worldSeed)` is the deterministic seed for everything generated on the tile (§5.7). Archetype = table[hash(tileId, seed)] (§3.4).
- `TileId.Zero` is the room's tile: (X, Y, T) = (0, 0, 1).
- Any `GetHashCode` must be explicit and deterministic, for example the §5.7 mix with seed 0. It is never used for content.

### 4.5 `WalkerPose` [plan, §6.5, §4.6]

- `Tile` is the tile the walker stands in.
- `Local` is U, **the walker's frame in that tile**. It maps walker-frame coordinates to the tile's coordinates, so `Local * o` is the walker's position in the tile, in units of L. [plan, §4.6]
- **The walker's frame** has its origin at the head's floor point. Its axes are the axes in which head displacements d and `_HeadPosWS` are measured: the world (Unity) x and z axes, not the head's yaw. Height is not part of it. [derived from §4.6: d = head.xz − previousHead.xz goes into U, and the warp adds L·Log(·) to `_HeadPosWS` without rotating it]
- Update each frame (§4.6, `P1-B02`): U ← U·Boost(d/L, κ), then `Orthonormalised()`. On crossing edge i: U ← inverse(G[i])·U and Tile ← Tile.Step(i). G[i] maps the neighbour's frame into the current tile's frame. It equals the `F` that `VisibleSet` reports for that neighbour (§5.3).
- **In `State` (Appendix B.1, lane D):** `tileId` (6 × i64) holds the words of §5.4 in that order, and `xform` (9 × f64) holds `Local.M` **row-major**: M[0,0], M[0,1], M[0,2], M[1,0], … , M[2,2]. `p[0]` is L (B.1).
- In other spaces `Local` uses that space's κ. In flat spaces (κ = 0) its translation is in metres. B.1 lists what `tileId` holds per space.

### 4.6 `ITileGraph` and `VisibleTile` [plan, §6.5; P1-X06 for the field names]

- `Q` is the number of tiles per corner: 5 (the {4,5} world), 6 (the dial's q = 6 snap) or 3 (Small Planet, six tiles).
- `VisibleSet(centre, rho, into)` clears `into` and fills it with **every tile whose centre lies within hyperbolic distance `rho` (units of L, inclusive) of the centre tile's centre**, the centre tile included.
  - Each entry carries the tile's id; F, the step chain from that tile's frame into the centre tile's frame; and its graph distance from the centre tile (breadth-first depth, in edge steps).
  - Entries come in breadth-first order. The set is defined by distance, not by the search: it is exact.
  - The list is caller-owned and reused, so a call allocates nothing once warm. A tile crossing must not allocate (`P1-B06`).
  - On q = 3 all six tiles are always returned.
- Counts that fix the semantics (test `VisibleSet_Counts`, viewer at a tile centre, {4,5}): 13 / 25 / 45 / 81 / 129 tile centres within ρ = 2 / 2.5 / 3 / 3.5 / 4, and 105 within 3.8425.
- Graph distance counts (`TileId_Growth`, OEIS A377322) for depths 0–8 are 1, 4, 12, 28, 64, 148, 340, 780, 1792.
- Which `rho` the renderer passes is `P1-B06`'s choice. A guide [derived]: the draw rule draws a tile when its centre is within ρ + 0.8425 of the walker, and the walker is within about 0.8425 of the current tile's centre. A candidate set from the current tile's centre with `rho` = ρ + 2 × 0.8425 (plus the crossing hysteresis) therefore covers every walker position in the tile.
- **[P1-B06 D5]** The renderer passes `rho` = ρ + 2·r_c + 0.05 (units of L, r_c = 0.8424821 for q = 5), from the current tile's centre, and rebuilds the list only when the tile or ρ changes. The 0.05 covers the 0.02 crossing hysteresis at corners. Candidates: 277 at ρ = 3 and 485 at ρ = 3.5 (q = 5), 497 at ρ = 3.5 (q = 6). Test `Candidates_CoverEveryTileWithinReach`.

### 4.7 How the pieces meet each frame [plan, §4.6, §6.3, §8.5]

```text
step 4 (kernel)   U = (U · Boost(d/L, κ)).Orthonormalised()        # d = 0 if |d| > 0.2 m or tracking is not Normal
                  if the walker left the tile through edge i: U = inverse(G[i]) · U; tile = tile.Step(i);
                      graph.VisibleSet(tile, rhoCandidates, list)  # only when the tile changes
                  for each t in list: A[t] = inverse(U) · t.F       # double
                      d_t = Distance(A[t]·o, o);  draw t only if the §8.5 rule passes
step 6 (upload)   per drawn tile: _Anchor = (float) A[t] (cast last); globals _HypL, _HeadPosWS once per frame
```

The draw rule (§8.5, A.3): draw t when (a) d_t ≤ ρ + 0.8425 and (b) the disc of radius 0.8425·L round c = L·d_t along the bearing of A[t]·o is not wholly outside any side plane of the view frustum, widened by 12° to each side and 4° up and down. ρ is the registry parameter `curved.cullRho` (default 3). The test runs once per frame for both eyes. It is the only culling wherever the hyperbolic warp is on.

[P1-B06 D1, D2] What `Atlas.Rendering.TileDrawRule` does with the rule, so that no undrawn tile has a visible point nearer than ρ:

- Test (b) is applied to the vertical shell volume, the disc extruded up to H = 2.6 m (the tallest part of a tile shell, a corner post): a tile is wholly outside a plane when `n·c + r·|n_floor| + max(0, H·n_y) < 0`. With H = 0 this is exactly the plan's formula. The literal floor-disc rule culls tiles whose posts are still in view.
- A tile whose centre is within r_c of the walker is always drawn (the reference script's `drawn |= dc <= Rc`).
- The disc centre is `eye.xz + L·Log(A[t]·o)` at the floor height (world y = 0), the same placement as `AtlasWarp` gives the tile's chart origin.

### 4.8 `TileFrame` and `RuleS` [P2-B01; additive contract change, announce to all lanes (§6.4)]

New public types in `Atlas.Kernel` (`Kernel/Tiling/TileFrame.cs`, `RuleS.cs`). They are pure tile-graph arithmetic in ℤ[φ], so they stay in the kernel (§6.2 "TileId, tile graph") and keep `XFrame` and `ZPhi` internal (§3 rule 7). Nothing existing changes.

```csharp
public readonly struct TileFrame : IEquatable<TileFrame> {          // oriented {4,5} tile frame = canonical frame of Tile · R^Spin
    public static readonly TileFrame Zero;                          // tile zero, identity, Spin 0 (default(TileFrame) is invalid)
    public TileId Tile { get; }
    public int Spin { get; }                                        // 0..3: the unique j with F == F.Canon() · R^j
    public TileFrame Step(int edge);                                // F · S_edge, edge in THIS frame's numbering; the result's edge 0 faces back
    public TileFrame Rotate(int quarterTurns);                      // F · R^n, n mod 4
    public int CanonicalEdge(int edge);                             // (edge + Spin) & 3: the same edge in TileGraph / WalkerPose numbering
    public Iso2 ToIso2();                                           // absolute double frame, κ = -1: debug and tests only (§3 rule 6)
}
public static class RuleS {                                         // Master Plan §10.2.3, R3 §D3
    public const int BackEdge = 0, RightEdge = 1, StraightEdge = 2, LeftEdge = 3;   // tree-frame edges
    public const int MaxAddressLength = 23;                         // "23 generations is the addressable depth"
    public const double ForkSpacing = 2.1225501;                    // acosh(1 + 2φ), units of L
    public static TileFrame FrameOf(string address, in TileFrame trachea);          // fork tile
    public static TileFrame BendFrameOf(string address, in TileFrame trachea);      // turn tile in front of a non-root fork
    public static TileFrame ChildBend(in TileFrame fork, char branch);              // fork.Step(L ? 3 : 1)
    public static TileFrame BendToFork(in TileFrame bend);                          // bend.Step(2)
    public static TileFrame ForkToBend(in TileFrame fork);                          // fork.Step(0).Rotate(2)
    public static TileFrame BendToParentFork(in TileFrame bend, char branch);       // bend.Step(0).Rotate(-(L ? 3 : 1))
}
```

- **Tree-frame convention.** In a frame produced by `Step`, edge 0 faces back, 2 straight, 3 left, 1 right. Edge numbers given to `TileGraph` and `WalkerPose` are canonical: always convert with `CanonicalEdge`.
- **`FrameOf(a, M)`** folds `M = BendToFork(ChildBend(M, c))` over the letters of `a`. A null address throws `ArgumentNullException`, a length over 23 `ArgumentOutOfRangeException`, a letter other than L and R `ArgumentException`. All arithmetic is checked (overflow throws).
- **Identities.** S₂·S₀ = R² and S_k·S₀ = R^k, so `ForkToBend` and `BendToParentFork` invert the forward moves exactly. Verified to depth 23 (`RuleS_RoundTrip`).
- **Golden values** (trachea = `TileFrame.Zero`; words Xu, Xv, Yu, Yv, Tu, Tv): FrameOf("L") = (0,0,0,−2,1,2); its bend (0,0,−1,0,0,1) is the tile across edge 3 of tile zero; FrameOf("R") = (0,0,0,2,1,2); FrameOf("LR") = (0,−2,−4,−6,5,8); FrameOf("RL") = (0,−2,4,6,5,8); FrameOf("LL") = (0,2,−4,−6,5,8); FrameOf("RRRLLR") = (372,602,1272,2058,1685,2728).
- **Range.** The six words need at most 53 bits at 23 generations (path "LRLR…L"); 2,000 random 23-letter addresses reach 51 bits. 262,141 tiles to depth 16 are distinct.
- Do not use `TileId.Step` in tree code: it descends from tile zero on every call. `TileFrame.Step` is O(1).

---

## 5. The {4,5} tiling and exact tile ids

### 5.1 Constants [plan, A.3; more digits derived]

| Quantity | {4,5} (κ = −1) | {4,6} (κ = −1) | {4,3} (κ = +1) |
|---|---|---|---|
| Half-edge a | 0.6268697 | 0.8813736 | 0.6154797 |
| Edge 2a | 1.2537393 | 1.7627472 | 1.2309594 |
| In-radius r | 0.5306375 | 0.6584789 | π/4 |
| Centre spacing 2r | **1.0612751** (cosh = φ) | **1.3169579** (cosh = 2) | **π/2** |
| Circumradius r_c | **0.8424821** | 1.1462158 | 0.9553166 |
| Corner angle | 72° | 60° | 120° |
| Klein (gnomonic) half-width | tanh r = 0.4858683 | 0.5773503 | tan r = 1 |
| Tile area | 2π/5 | 2π/3 | 2π/3 |

All lengths are in units of L (or R). φ = (1 + √5)/2 = 1.618033988749895 and √φ = 1.272019649514069. In metres for {4,5}: edge 1.2537·L and centre spacing 1.0613·L.

### 5.2 Generators and the neighbour step [plan, A.5]

```text
R = rotation by +90° about the tile centre = | 0 −1 0 |      H = diag(−1, −1, 1)   (half-turn)
                                             | 1  0 0 |
                                             | 0  0 1 |
T = translation along +x by 2r             = | φ   0  √φ |
                                             | 0   1   0 |
                                             | √φ  0   φ |
Step through edge k:   S_k = R^k · T · H          S_0·S_0 = I;   (S_1)^5 = I and no smaller power is
```

R, T and H here are matrices. This R is not the sphere radius, and this T is not the coordinate T of a tile centre.

### 5.3 Edge numbering and tile frames [plan, A.5; R3 §A3, §A5]

- **Edge k** of a tile is the edge whose midpoint lies at angle k·90° from the tile's +x axis, turning towards +z (§2.4). In its own frame, a tile's edge 0 faces +x, edge 1 +z, edge 2 −x and edge 3 −z.
- If M is a tile's frame, the neighbour across its edge k has the raw frame M·S_k. The half-turn H makes the neighbour's edge 0 in that raw frame point back to the tile it was entered from.
- **Every tile has one frame, fixed by its id**, so the edge numbering is the same on the phone and on the Station, whatever order the tiles were found in:
  - **Tile zero's frame is the identity.** Its axes are the RoomModel anchor's axes, so edge 0 faces the room's +x side, edge 1 its +z side, edge 2 −x and edge 3 −z.
  - **Any other tile's frame** is its canonical frame. Of the four frames M·R^j (j = 0..3) that the tile has, take the one whose first column, written as six integers (a11.u, a11.v, a21.u, a21.v, c1.u, c1.v) in the pattern of §5.4, is lexicographically smallest. This is R3 §A5's `Canon()`, whose code also keeps the root's frame at the identity.
- **The step matrix.** If the neighbour's canonical frame is M·S_k·R^j, then G = S_k·R^j maps the neighbour's frame into the current tile's frame. There are 16 constants for the 16 values of (k, j), computed in double. F[t] is the product of these constants along the path from the centre tile to t.

### 5.4 Tile-id encoding and invariant [plan, A.5]

Every product of R, T and H has the pattern

```text
M = |  a11     a12    √φ·b1 |
    |  a21     a22    √φ·b2 |      all a, b, c, d in ℤ[φ] = { u + v·φ : u, v integers },  φ² = φ + 1
    | √φ·c1   √φ·c2     d   |
```

A tile centre M·o is therefore (√φ·X, √φ·Y, T) with X = b1, Y = b2 and T = d in ℤ[φ]. **Y is the floor's z coordinate**, not Unity's height.

| | |
|---|---|
| **Encoding** [P1-X06: word order] | Six `Int64` words in this order: **Xu, Xv, Yu, Yv, Tu, Tv**, where X = Xu + Xv·φ, Y = Yu + Yv·φ, T = Tu + Tv·φ. This is also the order of `State.tileId` |
| **Invariant** [plan] | **φ·(X² + Y²) − T² = −1** in ℤ[φ]. Products follow (a + bφ)(c + dφ) = (ac + bd) + (ad + bc + bd)·φ, and the right side is (−1) + 0·φ |
| **Step arithmetic** [plan] | Multiplying by S_k multiplies by 0, ±1 or ±φ only, and (u + vφ)·φ = v + (u + v)φ. Additions alone |

### 5.5 Worked values [derived; golden values for `P1-B04`]

| Tile | Xu, Xv, Yu, Yv, Tu, Tv | Centre (x, z, w) | Edge leading back to tile zero |
|---|---|---|---|
| Tile zero | 0, 0, 0, 0, 1, 0 | (0, 0, 1) | n/a |
| Across edge 0 of tile zero | 1, 0, 0, 0, 0, 1 | (√φ, 0, φ) | 0 |
| Across edge 1 | 0, 0, 1, 0, 0, 1 | (0, √φ, φ) | 1 |
| Across edge 2 | −1, 0, 0, 0, 0, 1 | (−√φ, 0, φ) | 2 |
| Across edge 3 | 0, 0, −1, 0, 0, 1 | (0, −√φ, φ) | 3 |

Check for the first neighbour: φ·1 − φ² = −1. Breadth-first search from tile zero with these rules gives graph-distance counts of 1, 4, 12, 28, 64, 148, 340, 780, 1792, 4116 and 9452 for depths 0–10. The invariant holds on all 16,737 tiles to depth 10. The canonical frame is the same along 2,000 random walks as along the breadth-first search.

### 5.6 Range of 64-bit ids [plan, A.5, §14]

- Coefficients grow by about 2.9 per straight tile step. 64-bit words are exact to 42 straight steps from tile zero and overflow at step 43. Paths that turn stay nearer tile zero.
- In Phase 1 the walker cannot get near that limit. The Lung route (Phase 2) is 32 steps, and its 46-step addressable depth needs about 52 bits. Step arithmetic uses `checked`, so an overflow throws instead of wrapping.
- The invariant's products overflow `long` from about 22 straight steps. Evaluate it in a 128-bit type or `System.Numerics.BigInteger`. Unity's .NET profile has no `System.Int128` [derived], so use `BigInteger` or a small hand-written 128-bit struct. It runs in tests and development checks only.

### 5.7 Content seed [plan: "content seed = hash of the id"; function from R3 §A5]

```csharp
// SplitMix64 over the six words in encoding order; unchecked arithmetic.
ulong h = 0x9E3779B97F4A7C15UL ^ worldSeed;
foreach (long w in new[] { Xu, Xv, Yu, Yv, Tu, Tv }) {
    h += (ulong)w + 0x9E3779B97F4A7C15UL;
    h = (h ^ (h >> 30)) * 0xBF58476D1CE4E5B9UL;
    h = (h ^ (h >> 27)) * 0x94D049BB133111EBUL;
    h ^= h >> 31;
}
return h;
```

| Tile | `ContentSeed(0)` | `ContentSeed(12345)` |
|---|---|---|
| Tile zero | 0x4194AB7A26ABAF68 | 0x1339BA6A0D8B35C0 |
| Across edge 0 | 0xB27898E099B829B2 | 0x1A402E1FD39B2A54 |
| Across edge 1 | 0x486B6F16E8179850 | 0x7EE67468759C7B41 |

The seed never uses `string.GetHashCode()` or the default struct hash. Test `TileId_Seed` checks a golden file of 64 (id, seed) pairs, identical under Mono and IL2CPP (device half at G6).

### 5.8 Other values of q

- **q = 6** [P1-B04]: tolerance-hash ids. Frames are doubles snapped to the {4,6} lattice (integers; √3·integers off the diagonal blocks). The id is the rounded centre (Xu = round(x/√3), Yu = round(z/√3), Tu = round(w)), with Xv = Yv = Tv = 0. Invariant 3(X²+Y²) − T² = −1. Tile zero (0,0,0,0,1,0) keeps the identity frame. Canonical frame: the lexicographically smallest (a11, a21, c1/√3) over M·R^j. Range ≈ 26 straight steps; beyond that, InvalidOperationException or OverflowException. Content seed: §5.7 unchanged.
- **q = 3** (S4): six tiles, one per face of a cube blown onto the sphere. As in B.1, `tileId` carries the face index in word 0 and 0 in the other words (`CubeTiling.FaceId`, `Atlas.Kernel`, `P1-B08`). Face numbering is settled:
  - Exact integer matrices (A.2 with cos(π/2) = 0, sin(π/2) = 1): R = Rot(+90°) = [[0,−1,0],[1,0,0],[0,0,1]], T = Boost((π/2, 0), +1) = [[0,0,1],[0,1,0],[−1,0,0]], H = diag(−1,−1,1), S_k = R^k·T·H.
  - Frames Φ0 = I, Φ(k+1) = S_k for k = 0..3, Φ5 = S_0·S_2. Centres Φ_f·o: 0 (0,0,1), 1 (1,0,0), 2 (0,1,0), 3 (−1,0,0), 4 (0,−1,0), 5 (0,0,−1).
  - Neighbours N[f][k], k = edge 0..3: f0 {1,2,3,4}, f1 {0,4,5,2}, f2 {0,1,5,3}, f3 {0,2,5,4}, f4 {0,3,5,1}, f5 {1,4,3,2}. Antipodes 0↔5, 1↔3, 2↔4.
  - Step(f,k) = Φ_fᵀ·Φ_N (neighbour frame → face frame) = S_k·R^J with J[f] = f0 {0,0,0,0}, f1 {0,1,0,3}, f2 {3,1,1,3}, f3 {2,1,2,3}, f4 {1,1,3,3}, f5 {2,2,2,2}. The edge of N that leads back is (4 − J) mod 4.
  - `VisibleSet` always returns all six faces: the centre, its four neighbours in edge order, then the antipode.
- **q = 4** (the dial's flat snap): the plan defines no tile graph for it.
- `TileId.Step` is the {4,5} step. For q = 3 and q = 6, neighbours come from `ITileGraph`. Lane B may add lane-internal helpers that are not part of this contract.

---

## 6. Shader contract: lane B's part

Lane A owns `AtlasCore.hlsl`, `AtlasPortalClip.hlsl` and the single `UnityPerMaterial` block; that part of the contract is on lane A's page. Lane B owns `AtlasWarp.hlsl` and `AtlasGates.hlsl` (§8.3), and adds the ids of §6.7 to `AtlasShaderIDs.cs`. Each include is edited only by its owner.

### 6.1 `AtlasWarp.hlsl`: one entry point [plan, §8.3, `P1-B03`]

```hlsl
// Object position in, world position out. Called once per vertex by the shared shader (lane A).
float3 AtlasWarp(float3 positionOS);          // [P1-X06: name]  input metres, object space; output metres, world space
```

| `_WarpMode` | Mode | Output |
|---|---|---|
| 0 | none | `TransformObjectToWorld(positionOS)`, the ordinary transform |
| 1 | hyperbolic | `p = mul((float3x3)_Anchor, ExpH2(positionOS.xz / _HypL)); t = LogH2(p) * _HypL;` → `float3(_HeadPosWS.x + t.x, positionOS.y, _HeadPosWS.z + t.y)` (§4.6, verbatim) |
| 2 | spherical | The same with ExpS2 and LogS2 (A.2, κ = +1), and `_HypL` holding R |

- **Input.** `positionOS.xz` is a point of the mesh's own chart, in metres. It is the true distance and bearing from the chart origin, which is the log or azimuthal-equidistant chart.
  - Curved tiles are generated in that chart: Klein grid point k ↦ L·atanh(‖k‖)·k/‖k‖, or R·atan(‖k‖)·k/‖k‖ on the sphere (A.6, `P1-B05`). The tile pivot is the tile centre.
  - Room content (Home shell, furniture) uses its RoomModel coordinates in metres, relative to the room centre or to its own anchor (§4.6, `P1-B11`).
- **Height.** `positionOS.y` passes through unchanged as world y (§4.6: "the floor stays level and at the real floor's height"). See §9, point 1.
- **The Transform is ignored when warped.** In modes 1 and 2 the renderer's Transform (`unity_ObjectToWorld`) does not affect the position. `_Anchor` carries all placement.
- **Once per head.** The warp is centred on `_HeadPosWS`, the centre of the head, not on each eye. Each eye's view-projection then gives correct stereo depth (§4.6, BP §4).
- **Cost.** Exp is evaluated once per vertex, and sinh and cosh are derived from that one exponential (§8.3). The helpers `ExpH2`, `LogH2`, `ExpS2` and `LogS2` are internal to the file. HyperEngine passes its matrix through a MaterialPropertyBlock; the port moves it into the per-material block (§8.3).
- **Normals.** The warp returns positions only. Nothing in `AtlasCore`'s list (palette, vertex AO, `fwidth` lines, fog; §8.3) needs transformed normals.
- **Acceptance** (`P1-B03`): GPU positions match the kernel's CPU reference (`Maps`, `Iso2`) to 1 mm for ρ ≤ 3. The shared shader stays SRP-Batcher compatible. Exp is evaluated once per vertex.

### 6.2 Inputs

| Name | Kind | HLSL type | Unit | Set by | Meaning |
|---|---|---|---|---|---|
| `_HypL` | global | `float` | metres | Lane B, once per frame (`Shader.SetGlobalFloat`) | L in hyperbolic mode; R in spherical mode [P1-X06: one id for both]. Also the registry parameter `_HypL` (§7.3) |
| `_HeadPosWS` | global | `float3` | metres, world | Lane B, once per frame (`Shader.SetGlobalVector`) | Head centre, the same sample whose displacement stepped the kernel this frame. Its y is not used |
| `_Anchor` | per material | `float4x4` | none | Lane B, per pooled material instance (`Material.SetMatrix`) | The upper-left 3 × 3 is the isometry from the mesh's chart into the walker's frame. Tile t: inverse(U)·F[t]. Room object: inverse(U)·F[tile zero]·(its placement in tile zero). Computed in double, cast last. Other entries: 0, with [3][3] = 1 |
| `_WarpMode` | per material | `float` | none | Lane B on its materials; default 0 | 0 none, 1 hyperbolic, 2 spherical [P1-X06: per-material number, as §8.3 prefers to a keyword] |

- [P1-B03] `AtlasWarpCpu` (float, line for line with the HLSL) and `AtlasWarpReference` (double, from `Geometry2` and `Iso2`) in `Atlas.Rendering` are the EditMode warp for later checks (P1-B05 seams, P1-B06 draw check). `WarpAnchor.ToMatrix` is the upload cast.

**Upload layout** [derived]: `Matrix4x4 m; m[r, c] = (float)A.M[c][r]` for r, c < 3. Unity's `m[row, column]` reaches HLSL as `_Anchor[row][column]`, so `mul((float3x3)_Anchor, p)` is A·p.

**Where they are declared.** `_Anchor` and `_WarpMode` are declared by lane A inside the single `UnityPerMaterial` block in `AtlasCore.hlsl`. `AtlasWarp.hlsl` declares no constant buffer of its own; it declares only its globals, `_HypL` and `_HeadPosWS`. `AtlasCore.hlsl` includes `AtlasWarp.hlsl` after the per-material block.

### 6.3 What the shared shader does with the output (hand-over to lane A)

- **Geodesic distance for free.** In modes 1 and 2 the log map puts every vertex at its true distance from the head, so `length(posWS.xz − _HeadPosWS.xz) / _HypL` is the geodesic distance in units of L (or R) [derived; R1 §F.6]. The fog in `AtlasCore.hlsl` uses it per vertex, interpolated: it starts at ρ − 1 and is fully opaque at ρ = `curved.cullRho` (§8.5). The fog's ids are lane A's.
- **Small Planet** (`P1-B08`): in spherical mode the fog is opaque at 0.8π, and fragments whose interpolated geodesic distance exceeds 2.6 rad are discarded, which prevents smeared triangles near the antipode. The discard sits beside the fog in `AtlasCore.hlsl`, behind `_WarpMode == 2`. [P1-B08] Fog starts at 0.5π and is opaque at 0.8π, in units of R (`planet.fogRho` moves the opaque end within [0.5π, 0.8π], start = opaque − 0.3π). The discard compares the interpolated per-vertex geodesic distance g = |posWS.xz − _HeadPosWS.xz| / _HypL with 2.6. `LogS2` returns distance π at the exact antipode (0,0,−1) instead of the head, as `Geometry2.Log` does.
- **No keyword.** The mode is a per-material number (§8.3: no new keyword without lane A).

### 6.4 CPU side [plan, §8.3]

- One pooled material instance per tile or per room object, created at run time and never saved as an asset. `_Anchor` and `_WarpMode` are set on it. **No MaterialPropertyBlocks** anywhere in `Atlas.*` (test `Shader_Contract`).
- Every renderer that uses a warped material gets huge `localBounds` (`P1-B03`'s component; `WorldLint_All` checks it). Unity then culls nothing, and the §8.5 draw rule decides what is drawn. The Small Planet always draws its six tiles.
- [P1-B03] `AtlasWarpBounds` (`Atlas.Rendering`) sets `localBounds` to a 1000 m cube centred on the object origin; `AtlasWarpBounds.Apply(Renderer)` is static, for pooled renderers after a mesh is assigned.
- Globals and anchors are written in frame step 6 (Upload, §6.3), after the kernel step and before rendering, once for both eyes.

### 6.5 `AtlasGates.hlsl`: sight-line gates for Aligned Cells [plan for the purpose, §4.7, §8.3; P1-X06 for the signature]

```hlsl
// Fragment stage. Discards the fragment if it lies on the wrong side of any active gate.
void AtlasGatesClip(float3 positionWS);       // [P1-X06: name]
```

| Name | Kind | HLSL type | Meaning |
|---|---|---|---|
| `_Gates` | per material | `float4x4` | Row i (i = 0..3) is gate i = (post.x, post.z, side, unused), with the post in world metres. side = +1 or −1; side = 0 turns the gate off. All zero (the default) means no clipping |

- Per gate i with side ≠ 0: with e = `_WorldSpaceCameraPos.xz` (the eye's own position; the rig has one camera per eye), g = post − e and d = positionWS.xz − e, the fragment is discarded when side·(g.x·d.y − g.y·d.x) < 0. This is R1 §F.7's formula.
- There is no stencil. Each eye uses its own position, and the corner post hides the seam (§4.7).
- Furniture in Aligned Cells is drawn once per eye, unclipped (gates off) (§4.7, `P1-B12`).
- The packing [P1-X06] stores R1's `_Gate[4]` array as the rows of one `float4x4`. Arrays are not settable on a material in a way the SRP Batcher is certain to keep, while a `float4x4` in `UnityPerMaterial` is.
- `AtlasCore.hlsl` calls `AtlasGatesClip` early in the fragment stage and includes `AtlasGates.hlsl` after the per-material block. Portal clipping and the warp never share a material (§8.3), and neither do the warp and the gates, since Aligned Cells is drawn flat.
- Acceptance lives in `P1-B10`: 13 / 15 / 17 instances for the centre, edge and corner cell of a 3 × 3 grid (`Mode2_SightLines`).

### 6.6 Constraints on both includes [plan, §8.3, §11.1]

SRP-Batcher compatible at every gate. No new shader keyword without lane A. ≤ 8 compiled variants for the shared shader (`Shader_Contract`, proposed). Positions in full `float`.

### 6.7 Property ids lane B adds to `AtlasShaderIDs.cs`

`AtlasShaderIDs.cs` (`Atlas.Rendering`, namespace `Atlas.Rendering`) holds every property id. It is edited by lanes A and B only, and an id is added or changed only after all four lanes are told (§6.4). Lane B's entries:

```csharp
public static class AtlasShaderIDs {
    // lane B: AtlasWarp.hlsl
    public static readonly int HypL      = Shader.PropertyToID("_HypL");       // global float, metres (L, or R in S4)
    public static readonly int HeadPosWS = Shader.PropertyToID("_HeadPosWS");  // global float3, metres, world
    public static readonly int Anchor    = Shader.PropertyToID("_Anchor");     // per material float4x4
    public static readonly int WarpMode  = Shader.PropertyToID("_WarpMode");   // per material float: 0 none, 1 hyperbolic, 2 spherical
    // lane B: AtlasGates.hlsl
    public static readonly int Gates     = Shader.PropertyToID("_Gates");      // per material float4x4, rows = gates
}
```

The shader names `_HypL`, `_HeadPosWS` and `_Anchor` are [plan] (§4.6). `_WarpMode` and `_Gates` and the C# field names are [P1-X06]. Lane A's ids (palette, fog, portal plane and the rest) are on lane A's page. `_WorldSpaceCameraPos` is Unity's built-in and needs no entry.

---

## 7. Owners and tasks

| Item | Code | Task | Lane |
|---|---|---|---|
| `Iso2`, `Maps`, η-Gram–Schmidt | `Atlas.Kernel` | `P1-B01` | B |
| U update, crossing hook, curvature ramp while standing still | Lane B's assemblies (placement chosen by `P1-B02`) | `P1-B02` | B |
| `AtlasWarp.hlsl`, the huge-`localBounds` component, warp ids | `Atlas.Rendering` | `P1-B03` | B |
| `TileId`, `TileGraph` (`ITileGraph`), `VisibleTile`, content seed | `Atlas.Kernel` (`Kernel/Tiling/`) | `P1-B04` | B |
| Tile meshes in the log chart | `Atlas.Rendering` (tiles) | `P1-B05` | B |
| Tile renderer, draw rule, anchors | `Atlas.Rendering` (tiles) | `P1-B06` | B |
| {4,3} graph and the spherical branch of the warp | `Atlas.Spaces.SmallPlanet`, `AtlasWarp.hlsl` | `P1-B08` | B |
| `AtlasGates.hlsl`, `_Gates` | `Atlas.Rendering`, `Atlas.Spaces.AlignedCells` | `P1-B10` | B |
| Stubs at G0: identity warp, fake tile provider | as above | `P1-X02` | shared |
| Calls to `AtlasWarp` and `AtlasGatesClip`; the `UnityPerMaterial` block with `_Anchor`, `_WarpMode` and `_Gates`; fog and the spherical discard | `AtlasCore.hlsl` | `P1-A06` | A |
| `Mesh4D` and the slicing job (in `Atlas.Kernel`, not part of this contract) | `Atlas.Kernel` | `P1-A12` | A |
| `TileFrame`, `RuleS` (§4.8) | `Atlas.Kernel` (`Kernel/Tiling/`) | `P2-B01` | B |

## 8. Tests that hold this contract (§11.1, all lane B, EditMode, double)

`Kernel_ExpLog`, `Kernel_EtaOrthonormal`, `Kernel_Inverse`, `Holonomy_Triangle`, `Closure_345`, `Lorentz_Drift`, `Float32_Upload`, `TileId_RingInvariant`, `TileId_Growth`, `TileId_VertexClosure`, `TileId_Unique`, `TileId_Seed`, `VisibleSet_Counts`, `TileMesh_SeamsEdges`, `Mode2_SightLines` and `WorldLint_All`. The full pass conditions are in §11.1. Where this page quotes one, §11.1 wins.

## 9. Points to settle at G0, and what later tasks add to this page

1. **World frame, floor height and yaw** (lanes A, C, D).
   - The plan's warp sets world y = object y, and adds the log map to `_HeadPosWS` in world axes (§4.6). Both hold as written when the Unity world frame is the RoomModel anchor frame: the anchor at the origin, its axes along world x and z, and y = 0 on the real floor.
   - The plan does not say who makes them so. If lane A's XR origin is placed that way after the scan, no floor offset and no yaw ψ appear anywhere.
   - If not, every warped mesh must carry world height in y. U must carry ψ, set at "Return to Home" by `P1-B11`. And the Spectator needs ψ, because `State` gives the head pose in the anchor frame while `xform` is in world axes.
   - The minimal option is the first one. It needs a decision by lanes A and C, and lane D's agreement.
2. **Resolving an id received from elsewhere** (lane D). [P1-B04] Build `TileId` from the six `State.tileId` words. Pass it to `TileGraph(q).VisibleSet / Neighbour / StepMatrix` of the same q. An id the graph has not seen is resolved by descent from tile zero: in each tile, step through the edge e maximising (X, Y, −X, −Y) of the target expressed in that tile's frame, updated with S_e⁻¹ by additions only, until the target is reached. The result is the target's canonical frame (§5.3), so edge numbering, F and content seeds equal the phone's whatever order tiles were found in. `TileId.Step` ({4,5}) uses the same rule and needs no graph. Invalid ids throw `ArgumentException`.
3. **q = 6 encoding** (`P1-B04`, now in §5.8). **S4 face numbering** is settled by `P1-B08` (§5.8).
4. **Read by the other three lanes, no question open.** This is the human half of `P1-X06`'s acceptance, signed off at G0.

## 10. Not in this contract

- The tile-crossing test and its hysteresis, the curvature ramp, level-of-detail selection, the world generator and WorldLint. These are lane B's internal work (`P1-B02`, `P1-B06`, `P1-B07`), governed by their task rows.
- `ISpaceModule`, the per-material block's other fields, the layer and stencil table and the frame loop: lane A's page.
- The RoomModel schema: lane C's page.
- The `State` layout and the parameter registry: lane D's pages.

Superseded and never to be built (Appendix D.1, D.2): Klein-chart rendering of walked space; offline tile lookup tables; global coordinates; ⟨p, p⟩ = −R² in code; MaterialPropertyBlocks; fog at 5–8 units.
