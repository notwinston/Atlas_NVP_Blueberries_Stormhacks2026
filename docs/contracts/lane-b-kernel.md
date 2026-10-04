# Lane B · Kernel contract

The kernel types, the metric and unit conventions, the exact tile ids, the walker kernel the frame loop steps, and lane B's part of the shader contract (`AtlasWarp.hlsl`, `AtlasGates.hlsl`, lane B's property ids).

| | |
|---|---|
| Owner | Lane B · Kernel and Curved Worlds, for the mathematics. Lane A's platform contract (`lane-a-platform.md`) governs everything that is not curved-space mathematics |
| Written by | `P1-X06`, from Master Plan §6.4, §6.5, §8.3 and Appendix A, with §3.4, §4.6, §4.7, §8.5, §9.5.6, §11.1 and Appendix B.1 where they touch the kernel. Merged on 4 October 2026 from the two pages written for gate G0 (the platform side's `lane-b-kernel.md` and lane B's own `kernel.md`) so that the page describes the code of branch `phase1-integration` |
| Frozen by | Gate G0 (`P1-X02`). After that, any change is announced to all four lanes before it is committed (§6.4). Changes made by the merge are listed in §9.3 |
| Read by | Lane A (shared shader, `ISpaceModule`, SpaceLab, frame loop), lane C (the room as tile zero), lane D (`State`, Spectator, flat-twin map) |
| Code it governs | `Atlas.Kernel` (`Assets/Atlas/Kernel/`); `AtlasWarp.hlsl` and `AtlasGates.hlsl` (`Assets/Atlas/Rendering/Shaders/Resources/`); lane B's use of `AtlasShaderIDs.cs` (`Atlas.Rendering`) |

`docs/contracts/kernel.md` is now a pointer to this page. Code comments that cite "kernel.md §n" keep their numbers: §1 to §10 below are numbered as that page was. Comments that cite the other page ("lane-b-kernel.md section n") use the numbers of the table in §1.1.

Tags on this page:

- **[plan]**: a name, signature or value the Master Plan fixes. Binding.
- **[P1-X06]**: chosen on this page because the plan is silent. It changes only with notice to all four lanes.
- **[derived]**: follows from the plan's formulas. Every derived value on this page was checked numerically when the page was written.
- **[merge]**: settled when the two lanes' code was joined (§9.3).

Where this page and the Master Plan disagree, the Master Plan wins. Report the conflict to lane B.

Rules for every file in `Kernel/` outside `Mesh4D/`, `Mesh/` and `Lens/`: namespace `Atlas.Kernel`, assembly `Atlas.Kernel`, only `System`, `Unity.Mathematics` and, for the unsafe slicer, Burst and Collections (README F1, F2). No `UnityEngine` type, no static mutable state, no allocation on the frame path. Angles are radians. All arithmetic is `double`; the only casts to `float` are `Iso2.ToFloat3x3` and `Iso2.ToFloat4x4`.

---

## 1. What each lane needs from this page

| Lane | Reads | For |
|---|---|---|
| A | §2, §4.9, §6, §7 | The shared shader calls `AtlasWarp` in its vertex stage and `AtlasGatesClip` in its fragment stage and declares lane B's per-material fields in the single `UnityPerMaterial` block. The frame loop steps the one `IWalkerKernel` at step 4. `ISpaceModule.Enter(in WalkerPose start)` uses `WalkerPose` (§4.5) |
| B | All | Implementation (`P1-B01` to `P1-B12`) |
| C | §2.3, §4.5, §9 | The RoomModel anchor frame is tile zero's frame. Home meshes, in metres relative to the room centre, are tile-local coordinates of tile zero (§4.6). L and R come from the loop-fit in `RoomModel.Curvature` |
| D | §4, §5.4, §5.7, §7.3, §9 | Packing of `State.tileId` and `State.xform` (§4.5, §7.3). The Spectator redraws the view with `ITileGraph.VisibleSet` and the same warp. The flat-twin map uses tile centres |

### 1.1 What is built, and where the old section numbers went

All of this page is built. Nothing on it is a stub.

| Part | Code | Section |
|---|---|---|
| `Iso2`, `Geometry2`, `Curvature`, `KernelConstants` | `Kernel/Iso2.cs`, `Geometry2.cs`, `Curvature.cs`, `KernelConstants.cs` | §4.2, §4.3 |
| `TileId`, `WalkerPose`, `VisibleTile`, `ITileGraph` | `Kernel/Tiling/TileId.cs`, `WalkerPose.cs`, `VisibleTile.cs`, `ITileGraph.cs` | §4.4 to §4.6 |
| Tile graphs: {4,4}, {4,5}, {4,6}, {4,3}; `TileFrame`, `RuleS` | `FlatTileGraph.cs`, `Tiling/TileGraph.cs`, `Tiling/CubeTiling.cs` (`CubeTileGraph`), `Tiling/TileFrame.cs`, `RuleS.cs` | §4.8, §4.10, §5 |
| The walker kernel the frame loop steps | `IWalkerKernel.cs` (with `IDialKernel`), `FlatWalkerKernel.cs`, `Walk/HolonomyWalkerKernel.cs` (with `HolonomyRig` and `SphereWalker`) | §4.9 to §4.11 |
| Warp and gates | `Rendering/Shaders/Resources/AtlasWarp.hlsl`, `AtlasGates.hlsl` | §6 |
| Tile meshes, tile renderer, draw rule, world generator, WorldLint | `Rendering/Tiles/`, `World/Generation/`, `World/Lint/` | §4.7, §7.1 |
| S2, S3, S4 | `Spaces/CurvedWalk`, `AlignedCells`, `SmallPlanet` | §7.2 |

Section numbers of the platform side's page (cited by some code comments as "lane-b-kernel.md section n") and where they are now:

| Old | Subject | Now |
|---|---|---|
| 1.1, 1.2 | Model, metric, units, dial values | §2.1 to §2.3 |
| 1.3 to 1.6 | Floor to kernel plane, matrix layout, frames, edges and corners | §2.4 to §2.7 |
| 2.1 to 2.6 | `Curvature`, `Iso2`, the maps, `TileId`, `WalkerPose`, `VisibleTile` | §4.1 to §4.5 |
| 2.7 | `ITileGraph` and its rules | §4.6 |
| 3.1, 3.2 | The flat stub and what throws | Gone: curved geometry is built. The guards that remain are in §4.2 and §4.9 |
| 3.3 | `FlatTileGraph` | §4.10 |
| 3.4 | `ContentSeed` and its golden values | §5.7 |
| 4 | The kernel step hook | §4.9 to §4.11 and §7.2 |
| 5, 6 | `AtlasWarp.hlsl`, `AtlasGates.hlsl` | §6.1, §6.5 |
| 7 | Tests | §8 |
| 8 | What other lanes may and may not assume | §10 |
| Open for the lead | The eight questions | §9.3 and §9.4 |

---

## 2. Conventions

### 2.1 Coordinates and metric [plan, A.1]

| Geometry | κ | Model surface, points p = (x, z, w) | Form | Base point o |
|---|---|---|---|---|
| Hyperbolic | −1 | Unit hyperboloid: x² + z² − w² = −1, w > 0 | η = diag(1, 1, −1) | (0, 0, 1) |
| Flat | 0 | The plane w = 1 (homogeneous floor coordinates) [derived, the κ = 0 limit of A.2's unified form] | none: rigid motions of the plane | (0, 0, 1) |
| Spherical | +1 | Unit sphere: x² + z² + w² = 1 | ordinary dot product | (0, 0, 1) |

One statement covers all three: the surface is κ·(x² + z²) + w² = 1 (A.1, A.2). o is the tile centre in a tile's own frame and the head in the walker's frame.

**Component order.** x and z are the floor axes and match Unity's x and z. w is the third component. In Unity.Mathematics a point is a `double3` with `.x` = x, `.y` = **z (the floor's z axis, not height)** and `.z` = w. A tangent vector or step is a `double2` with `.x` = x and `.y` = z. HLSL uses the same layout (`p.xy` spatial, `p.z` = w). Height (Unity y) never enters the kernel: the curved spaces are H² × E and S² × E. [derived, from §4.6 and BP §4]

### 2.2 κ, the three geometries [plan, A.1, §6.5]

| κ | Geometry | Curvature K | Length scale | Used by |
|---|---|---|---|---|
| −1 | Hyperbolic H² | −1/L² | L, metres | S2 Curved Walk, the tile graph, Lung airway (Phase 2) |
| 0 | Flat | 0 | none; the kernel's length unit is 1 m [P1-X06] | Flat spaces, S3 cells, the κ = 0 end of the dial |
| +1 | Spherical S² | +1/R² | R, metres | S4 Small Planet |

- **κ is a sign, never a strength** [P1-X06]. The members of the kernel that take a κ accept exactly −1, 0 and +1 and throw `ArgumentOutOfRangeException` for anything else (`Curvature.Require`). Exceptions, kept from lane B's code: `Iso2.Boost`, `Iso2(double3x3, kappa)` and `Geometry2` take κ by its sign and treat NaN as flat (§9.3, point 5). How strong the curvature is depends on L (or R) alone. The curvature dial (`P1-B09`) changes L or R, never κ's magnitude.
- **L** is the hyperbolic curvature radius in metres (the Engineering Spec calls it R). **R** is the Small Planet's sphere radius. **ρ** is hyperbolic distance in units of L. **q** is the number of tiles at a corner (§0.4).
- Loop-fit (A.4, lane C computes it): L = s / 1.0613 for q = 5, L = s / 1.3170 for q = 6, R = 2s/π for q = 3.

Values per dial position. s is the lap-square side, RoomModel `LapSide`; the radii are `Curvature.R3`, `L5` and `L6` of the RoomModel (`RoomCurvature`), raised to the comfort floors `FloorR` and `FloorL` where those apply (§4.3, §4.4; JSON names in `roommodel.schema.json`, C# names on lane C's page):

| Dial q | κ | `MetresPerUnit` | Centre spacing 2r (units) | Circumradius r_c (units) |
|---|---|---|---|---|
| 3 | +1 | R = 2s / π (`R3`) | π/2 = 1.5707963267948966 | 0.955316618124509 |
| 4 | 0 | 1.0 (`KernelConstants.FlatMetresPerUnit`) | s / L | spacing / √2 |
| 5 | −1 | L = s / 1.0612750619050357 (`L5`) | 1.0612750619050357 (cosh = φ) | 0.8424820814620078 |
| 6 | −1 | L = s / 1.3169578969248166 (`L6`) | 1.3169578969248166 (cosh = 2) | 1.1462158347805893 |

### 2.3 Units and the boundary [plan, A.1, §6.4]

| Where | Lengths | Angles |
|---|---|---|
| Inside `Atlas.Kernel` (`Iso2`, `Geometry2`, tile constants, `VisibleSet`'s `rho`) | Units of L (hyperbolic) or R (spherical); 1 m when κ = 0 | Radians |
| At the boundary | **Divide by L (or R) on the way in and multiply by L (or R) on the way out.** `Iso2.Boost` takes a step that is already divided by L (§6.5) | |
| Outside the kernel: world space, RoomModel, mesh object space, shader output, `_HeadPosWS`, `_HypL` | Metres | Radians in code; degrees only in UI text |

The boundary is exactly three places:

| Place | Direction | Conversion |
|---|---|---|
| `IWalkerKernel.Step`, `IWalkerKernel.ResetToFloor` | metres in | divide by `MetresPerUnit` |
| `AtlasWarp` in the vertex shader | metres in and out | `positionOS.xz / _HypL`, result `* _HypL` |
| Any reader of `WalkerPose.Local`, `ITileGraph.CentreSpacing`, `CircumRadius`, ρ | units out | multiply by `IWalkerKernel.MetresPerUnit` |

- In kernel units: every `Iso2` entry, `Boost`'s `step`, `Geometry2`, `CentreSpacing`, `CircumRadius`, ρ (`CullRho`, the `rho` of `VisibleSet`), `CrossHysteresis`.
- In metres: `KernelConfig.TileSideMetres`, `Step`'s `headFloorMetres`, `ResetToFloor`'s `floorMetres`, `KernelStep.AppliedMetres`, `KernelConstants.MaxStepMetres`, everything in the shader's object and world space.
- **At κ = 0 any positive L describes the same geometry.** `KernelConfig.Flat(side)` sets `MetresPerUnit = KernelConstants.FlatMetresPerUnit = 1.0` [P1-X06], so in a flat space one kernel unit is one metre and `State.p[0]` is 1. Nobody may rely on that: always multiply by `MetresPerUnit`. The test `FlatWalker_UnitFree` runs the kernel at L = 1.885 to keep callers honest. While a curvature ramp stands at K = 0, `HolonomyWalkerKernel.MetresPerUnit` is 1 as well (§4.11).

The Engineering Spec's §6.1 formulas keep R explicit, as in ⟨p, p⟩ = −R². Code does not do that: it uses the unit model and scales once at the boundary (Spec §5.3, "coordinate convention").

### 2.4 Turning sense, handedness and the floor [derived]

- `Iso2.Rot(θ)` with θ > 0 turns +x towards +z. Seen from above in Unity's left-handed, y-up frame, that is anticlockwise: **a left turn**. A.4's "walk s, turn left 90°" is `Boost(s/L)·Rot(π/2)`.
- Unity's own yaw has the opposite sense: `Quaternion.Euler(0, ψ, 0)` turns +z towards +x. So **`Rot(θ)` is a Unity yaw of −θ**. Convert explicitly wherever a head or object yaw enters the kernel. A Unity heading with yaw ψ points along (sin ψ, cos ψ) in (x, z); its kernel angle from +x is 90° − ψ.
- Every `Iso2` is orientation-preserving (determinant +1). Mirror images (the Klein bottle's flipped rows) are not `Iso2` values; lane A's Glued space keeps its own transforms for them.

Unity world space is the room frame (README F4): left-handed, Y up, metres, origin at the centre of the free rectangle on the floor.

| Room frame (Unity) | Kernel |
|---|---|
| X, metres (along the `Wf` side) | x = X / L |
| Z, metres (along the `Df` side) | z = Z / L |
| Y, metres | not represented |
| Floor point `float2 (x, z)` | `double2` with `.x` = x and `.y` = z |
| Yaw ψ about +Y (positive is clockwise seen from above, a right turn) | `Rot` angle θ = −ψ |

### 2.5 Matrices and their layout [plan, §4.6; derived]

- An isometry is a 3 × 3 matrix M acting on column vectors: p′ = M·p. Products compose right to left: `(a * b).Apply(p) == a.Apply(b.Apply(p))`.
- **In memory.** `Iso2` keeps the nine entries as `double M00 … M22`, **row-major** (`Mrc` is row r, column c; index 0 is x, 1 is z, 2 is w), and the κ tag. `Iso2.M` returns them as a Unity.Mathematics `double3x3`, which stores **column-major**: `M.c0` is column 0 and the entry in row r, column c is `M[c][r]`. The words row and column on this page always mean the mathematical M[r, c].
- Moving in the walker's own frame is a **right** multiplication: U ← U · Boost(Δ / L). Changing the frame U is expressed in is a **left** multiplication: U ← G⁻¹ · U.
- `inverse(M)` = η·Mᵀ·η in H² and Mᵀ on S² (§6.5). On the flat plane M = [[A, t], [0, 1]] with A a 2 × 2 rotation, and the inverse is [[Aᵀ, −Aᵀt], [0, 1]] [derived; the plan states only the curved cases].
- **On the wire** (`State.xform`, Appendix B.1) the nine doubles are row-major: M00, M01, M02, M10, M11, M12, M20, M21, M22. `Iso2.CopyTo` and `Iso2.FromRowMajor` use that order; κ is not on the wire.
- **For upload**, `ToFloat4x4()` puts `Mrc` at row r, column c of a `float4x4`, with row 3 and column 3 taken from the identity. The shader reads it as `mul((float3x3)_Anchor, p)` with p = (x, z, w).

### 2.6 Frames

| Symbol (§4.6) | C# | Maps | Notes |
|---|---|---|---|
| U | `WalkerPose.Local` | walker frame → frame of the walker's current tile | `U.Position` is the walker in tile coordinates |
| G[k] | `ITileGraph.EdgeStep(tile, k)` | frame of the neighbour across edge k → frame of `tile` | Re-root: U ← G[k]⁻¹ · U |
| F[t] | `VisibleTile.Chain` (also `.F`) | frame of tile t → frame of the centre tile | Product of edge steps, rebuilt from scratch on every re-root |
| anchor[t] | `(U.Inverse() * F[t]).ToFloat4x4()` | frame of tile t → walker frame | Product in double, cast last. Per-material `_Anchor` |

- **Walker frame.** Origin at the point of the floor under the head. Axes parallel to the room's +X and +Z at the last `Reset`, then carried along the walked path without turning. **Head yaw is not in U.** The cameras apply the head's rotation as usual. In a curved space the rotation part of U is the holonomy: how far the world has turned against the room. At κ = 0 it stays the identity.
- **Tile frame.** Origin at the tile centre. Its +x axis passes through the midpoint of edge 0.
- **Tile zero** is `TileId.Origin`. Its frame is the room frame divided by L: tile zero is centred on the RoomModel anchor (§4.6, "the room is tile zero").

### 2.7 Edges and corners of a tile

Every tile has four sides. In its own frame:

```text
              +z
            edge 1
   corner 1 ┌──────┐ corner 0
     edge 2 │  o   │ edge 0      → +x
   corner 2 └──────┘ corner 3
            edge 3
```

- Edge k is the edge whose midpoint lies at angle k · 90° from the tile centre (R3 §A3). Corner k lies at angle 45° + k · 90°, between edge k and edge (k + 1) mod 4 [P1-X06].
- Which edge of the neighbour leads back is `ITileGraph.BackEdge(tile, k)`. On the flat grid it is (k + 2) mod 4. In {4,5} it is not. **Never compute it; always ask the graph.**
- Walking round a corner post turning left: after entering a tile through its edge b, leave through edge (b + 3) mod 4. The walk returns to the first tile after exactly `Q` steps (4 flat, 5 in {4,5}, 3 on the cube).

---

## 3. Precision rules

1. **Double precision in the kernel.** Every isometry is a `double` matrix. Matrices are multiplied in double and cast to float last, only for upload to a material or for drawing (§4.6, `P1-B03`).
2. **No global coordinate.** A position is a tile id plus a local frame, `WalkerPose { Tile, Local }` (rule 7, §0.5). No floating-point coordinate is ever accumulated across tiles.
3. **U stays small.** U, the walker's frame, is always expressed in the tile the walker stands in. The walker is never more than about r_c = 0.8425 from that tile's centre, so U's entries stay near 1 however far the walker goes (§4.6).
4. **Renormalise every frame**: η-Gram–Schmidt in H², ordinary Gram–Schmidt on S² (A.2). Test `Lorentz_Drift`: ‖CᵀηC − η‖ < 1e-9 after 100,000 random boosts.
5. **F[t] is rebuilt, never accumulated.** The step chain from the current tile to tile t is a product of a few fixed step matrices in double. It is rebuilt from those constants whenever the walker changes tile (R3 §A5).
6. **Anchors are tile-relative.** The float matrix uploaded per tile is inverse(U)·F[t], one combined matrix computed in double. Absolute anchors measured from tile zero lose precision past about 5 units (R3 §B7). Test `Float32_Upload`: |⟨p, p⟩ + 1| < 1e-3 for tile-relative anchors at ρ = 4.35.
7. **Tile ids are exact integers.** There is no tolerance, so equality is exact. The invariant multiplies coefficients and must be evaluated in a 128-bit integer or `BigInteger`, never in `long` (§5.6).
8. **In the shader, positions stay in full `float`.** Exp is evaluated once per vertex, and sinh and cosh are derived from that one exponential (§8.3).

---

## 4. Kernel types (`Atlas.Kernel`)

`Atlas.Kernel` references only Unity.Mathematics, Burst and Collections. It has no scene types, so all of it runs in EditMode and headless tests (§6.2). One definition of each type exists: there is no second `Iso2`, `TileId`, `ITileGraph` or `VisibleTile` anywhere in the repository.

### 4.1 Signatures

```csharp
namespace Atlas.Kernel {

public static class Curvature {                               // Kernel/Curvature.cs
    public const double Hyperbolic = -1.0, Flat = 0.0, Spherical = +1.0;
    public static bool IsSupported(double kappa);             // exactly -1, 0 or +1
    public static void Require(double kappa);                 // ArgumentOutOfRangeException otherwise; never NotSupportedException
    public static double OfQ(int q);                          // 3: +1, 4: 0, 5 and 6: -1; any other q: ArgumentOutOfRangeException
}

public readonly struct Iso2 : IEquatable<Iso2> {              // §4.2
    public readonly double Kappa;                             // -1, 0 or +1; the tag
    public double3x3 M { get; }                               // column-major storage of Unity.Mathematics
    public double M00 { get; } /* … */ public double M22 { get; }   // row-major reads
    public Iso2(double3x3 m, double kappa);
    public Iso2(double m00, double m01, double m02, double m10, double m11, double m12, double m20, double m21, double m22, double kappa);
    public static readonly Iso2 Identity;                     // flat-tagged
    public static Iso2 IdentityFor(double kappa);
    public static Iso2 Boost(double2 step, double kappa);     // step already divided by L
    public static Iso2 Rot(double angle);                     // flat-tagged
    public static Iso2 Rot(double angle, double kappa);
    public Iso2 Inverse();
    public Iso2 Orthonormalised();
    public static Iso2 operator *(Iso2 a, Iso2 b);
    public double3 Apply(double3 p);
    public double3 Position { get; }                          // M · o
    public double Defect();
    public double3x3 ToDouble3x3(); public float3x3 ToFloat3x3(); public float4x4 ToFloat4x4();
    public void CopyTo(Span<double> rowMajor9);
    public static Iso2 FromRowMajor(ReadOnlySpan<double> rowMajor9, double kappa);
}

public static class Geometry2 {                               // Kernel/Geometry2.cs; P1-B01's "exp and log maps, distance"
    public static readonly double3 BasePoint;                 // (0, 0, 1)
    public static double3 Exp(double2 t, double kappa);
    public static double2 Log(double3 p, double kappa);
    public static double  Distance(double3 p, double3 q, double kappa);
}

public readonly struct TileId : IEquatable<TileId> {          // §4.4
    public readonly long Xu, Xv, Yu, Yv, Tu, Tv;
    public TileId(long xu, long xv, long yu, long yv, long tu, long tv);
    public static readonly TileId Zero;  public static TileId Origin { get; }     // the same value
    public bool IsNone { get; }  public long this[int word] { get; }
    public void CopyTo(Span<long> words6);  public static TileId FromWords(ReadOnlySpan<long> words6);
    public static bool TryParse(string text, out TileId id);
    public TileId Step(int edge);                             // the {4,5} neighbour across edge 0..3
    public ulong ContentSeed(uint worldSeed);
}

public struct WalkerPose {                                    // §4.5
    public TileId Tile; public Iso2 Local;
    public static WalkerPose Start { get; }  public bool IsValid { get; }
}

public readonly struct VisibleTile {                          // §4.6
    public VisibleTile(TileId id, Iso2 f, int graphDistance);
    public readonly TileId Id;  public readonly Iso2 F;  public readonly int GraphDistance;
    public TileId Tile { get; }  public Iso2 Chain { get; }   // the names of the frame-loop pages: Id and F
}

public interface ITileGraph {                                 // Kernel/ITileGraph.cs
    int Q { get; }  double Kappa { get; }  double CentreSpacing { get; }  double CircumRadius { get; }
    TileId Neighbour(TileId tile, int edge);
    int BackEdge(TileId tile, int edge);
    Iso2 EdgeStep(TileId tile, int edge);
    void VisibleSet(TileId centre, double rho, List<VisibleTile> into);
}

public sealed class FlatTileGraph : ITileGraph  { … }         // q = 4, kappa = 0 (§4.10)
public sealed class TileGraph     : ITileGraph  { … }         // q = 5 exact, q = 6 tolerance variant, kappa = -1 (§5)
public sealed class CubeTileGraph : ITileGraph  { … }         // q = 3, kappa = +1, six faces (§5.8)

// IWalkerKernel, IDialKernel, KernelConfig, KernelStep: §4.9.   FlatWalkerKernel: §4.10.   HolonomyWalkerKernel: §4.11.
}
```

`TileGraph` also has, beyond `ITileGraph` and lane-internal: `InRadius`, `StepMatrix(tile, edge)` (the same as `EdgeStep`), `ExitEdge(in Iso2 local)` (−1 inside the tile, with the hysteresis), `Reroot(ref WalkerPose)` and the constant `CrossingHysteresis` = 0.02 (units of L, R3 §A5). `VisibleSet` clears `into` and fills it in breadth-first order.

### 4.2 `Iso2` [plan, A.2]

| Member | Meaning | Formula (A.2), d = ‖Δ‖, u = Δ/d |
|---|---|---|
| `Boost(Δ, −1)` | Translate by tangent vector Δ in H² | [[1+(c−1)uₓ², (c−1)uₓu_z, s·uₓ], [(c−1)uₓu_z, 1+(c−1)u_z², s·u_z], [s·uₓ, s·u_z, c]], with c = cosh d, s = sinh d |
| `Boost(Δ, +1)` | The same on S² | As above with c = cos d, s = sin d and last row [−s·uₓ, −s·u_z, c] |
| `Boost(Δ, 0)` | Flat translation (Δ in metres) | [[1, 0, Δₓ], [0, 1, Δ_z], [0, 0, 1]] [derived] |
| Unified | All three | Matrix exponential of [[0, 0, Δₓ], [0, 0, Δ_z], [−κΔₓ, −κΔ_z, 0]] |
| `Rot(θ)` | Rotate about o by θ (θ > 0 = left turn, §2.4) | [[cos θ, −sin θ, 0], [sin θ, cos θ, 0], [0, 0, 1]] (R3 §A3) |
| `Inverse()` | Inverse isometry | η·Mᵀ·η (κ = −1); Mᵀ (κ = +1); [[Aᵀ, −Aᵀt], [0, 1]] (κ = 0, §2.5) |
| `Orthonormalised()` | Back onto the group | κ = −1: Gram–Schmidt with respect to η. κ = +1: ordinary Gram–Schmidt. κ = 0: n = sqrt(M00² + M10²), c = M00/n, s = M10/n, result [[c, −s, M02], [s, c, M12], [0, 0, 1]], translation untouched bit for bit; `InvalidOperationException` when n < 1e-12 or not finite (so `default(Iso2)` cannot be orthonormalised) |
| `a * b` | Composition, a after b | Matrix product; the last row of a flat product stays exactly (0, 0, 1) |
| `Apply(p)` | M · p. For a floor point use p = (x, z, 1) | |
| `Defect()` | Largest absolute entry of the group residual; 0 for an exact element | κ = 0: max of \|c0·c0 − 1\|, \|c0·c1\|, \|c1·c1 − 1\|, \|M20\|, \|M21\|, \|M22 − 1\| with c0 = (M00, M10), c1 = (M01, M11). κ = ±1: Mᵀ·J·M − J, J = diag(1, 1, −1) or the identity |

**How κ is carried** [P1-X06, merge]. Every `Iso2` carries a κ tag, because `Inverse()` and `Orthonormalised()` depend on the geometry and keep the §6.5 signatures.

- `Iso2.Identity` and `Iso2.Rot(angle)` are **flat-tagged**. Curved code uses `IdentityFor(kappa)` and `Rot(angle, kappa)`. A flat-tagged rotation is an isometry of every geometry: `HolonomyWalkerKernel.Reset` accepts one (for example `WalkerPose.Start`) and tags it itself.
- `a * b` treats κ = 0 as neutral: a flat factor takes the tag of a curved one. It throws `InvalidOperationException` only for −1 × +1.
- The constructor from a `double3x3` and `Boost(step, kappa)` take κ by sign and never throw. The members that came from the platform side (the nine-double constructor, `IdentityFor`, `Rot(angle, kappa)`, `FromRowMajor`) call `Curvature.Require` and throw `ArgumentOutOfRangeException` unless κ is exactly −1, 0 or +1.
- A flat `Boost` forms the translation as length × direction, which can differ from (Δx, Δz) in the last bit for a diagonal step. `FlatTileGraph` and `FlatWalkerKernel` use an internal exact translation instead, so their results are bit for bit what the flat contract promised.
- κ is not on the wire. A receiver of `State.xform` derives it from `spaceId`: S2 and S3 → −1, S4 → +1, everything else → 0.
- `default(Iso2)` is the zero matrix, not the identity. Never use it. Size: ten doubles, 80 bytes, blittable. `Equals` compares all ten doubles with `==`; `GetHashCode` agrees; `ToString()` is for logs only.
- `CopyTo` and `FromRowMajor` throw `ArgumentException` when the span holds fewer than nine elements.

Tests (§11.1, lane B): `Kernel_ExpLog`, `Kernel_EtaOrthonormal`, `Kernel_Inverse`, `Holonomy_Triangle`, `Closure_345`, `Lorentz_Drift`, `Float32_Upload`; the flat parts `Iso2_Conventions`, `Closure_345_Flat`, `Kernel_Inverse_Flat`, `Kernel_EtaOrthonormal_Flat`, `Kernel_ExpLog_Flat`, `Iso2_Upload`, `Iso2_RowMajorRoundTrip`; and `Kernel_CurvatureGuards`.

### 4.3 `Geometry2` [plan for the formulas, A.2; P1-X06 for the names]

| | κ = −1 | κ = +1 | κ = 0 [derived] |
|---|---|---|---|
| `Exp(t)`, r = ‖t‖ | (sinh r · t/r, cosh r) | (sin r · t/r, cos r) | (t, 1) |
| `Log(p)` | asinh(‖p_xz‖) · p_xz/‖p_xz‖ | atan2(‖p_xz‖, p_w) · p_xz/‖p_xz‖ (the same angle as A.2's acos(p_w), better conditioned) | p_xz / p_w |
| `Distance(p, q)` | 2·asinh(½·sqrt(⟨p−q, p−q⟩_η)), which equals A.2's cosh d = −⟨p, q⟩ and is stable near 0 | atan2(‖p × q‖, p·q) | ‖p_xz − q_xz‖ |

`Geometry2` does not validate κ: it branches on the sign, and 0 is flat. At r = 0 (or ‖p_xz‖ = 0) both maps return the base point or the zero vector, except that `Log` of the antipode (0, 0, −1) on the sphere returns distance π along +x. Test `Kernel_ExpLog`: exp∘log is the identity to < 1e-9 on 1,000 random points with ρ ≤ 4, and to < 1 mm after the float cast.

The walker's distance from the centre of their tile, in metres: `math.length(Geometry2.Log(pose.Local.Position, kernel.Kappa)) * kernel.MetresPerUnit`.

### 4.4 `TileId` [plan, §6.5, A.5]

- An id is six `Int64` words, `Xu, Xv, Yu, Yv, Tu, Tv`. In {4,5} they are X, Y and T in ℤ[φ] (X = Xu + Xv·φ and so on) and the tile centre is (√φ·X, √φ·Y, T) (§5.4). Other tilings define their own use of the six words (§5.8); `TileId` is an **opaque id plus a seed**, and the tiling is the `ITileGraph`.
- It is equal to another id when all six words are equal. Exact, no tolerance. No ordering is defined: do not sort tile ids.
- `TileId.Zero` and `TileId.Origin` (the same value) are tile zero, the room's tile, in every tiling: (Xu, …, Tv) = (0, 0, 0, 0, 1, 0). `IsNone` is true for `default(TileId)`, all six words zero: "no tile", the value Appendix B.1 calls 0.
- `this[int word]` (0..5, else `ArgumentOutOfRangeException`), `CopyTo(Span<long>)`, `FromWords(ReadOnlySpan<long>)` (both `ArgumentException` for fewer than six elements) and `TryParse` give the wire order.
- **`Step(edge)`** is the {4,5} neighbour across edge 0..3 of the tile's canonical frame (§5.3), computed by additions only. It resolves the canonical frame by descent from tile zero (§9.2), so it works on a bare id. It throws `ArgumentOutOfRangeException` for an edge outside 0..3, `ArgumentException` for an id that is not a {4,5} tile (a `FlatTileGraph` cell other than the origin, or `default`) and `OverflowException` beyond the 64-bit range. Other lanes call `ITileGraph.Neighbour`; tree code calls `TileFrame.Step` (§4.8), because `TileId.Step` descends from tile zero on every call.
- **`ContentSeed(worldSeed)`** is the deterministic seed for everything generated on the tile (§5.7). Archetype = table[hash(tileId, seed)] (§3.4). `GetHashCode` is the §5.7 mix with seed 0, folded to 32 bits; it is never used for content.
- **In JSON** (events, logs) a tile id is the `ToString()` text, the six words in wire order, decimal, comma-separated, invariant culture: `"0,0,0,0,1,0"`. Never six numbers: a 64-bit integer does not survive a JavaScript number. `TryParse` accepts exactly six comma-separated decimal integers with an optional leading sign and no white space; anything else, including `null`, returns false with `id` = "no tile".
- A space is free to put its own value into `State.tileId` as Appendix B.1 lists (S1: cell (i, j, 0, 0, 0, 0); S5: room id; S0 and S6: zero). Those are built with the six-word constructor and are not kernel tiles.
- Size: 48 bytes, blittable.

### 4.5 `WalkerPose` [plan, §6.5, §4.6]

- `Tile` is the tile the walker stands in.
- `Local` is U, **the walker's frame in that tile**. It maps walker-frame coordinates to the tile's coordinates, so `Local * o` is the walker's position in the tile, in units of L. [plan, §4.6]
- **The walker's frame** has its origin at the head's floor point. Its axes are the axes in which head displacements d and `_HeadPosWS` are measured: the world (Unity) x and z axes, not the head's yaw. Height is not part of it. [derived from §4.6: d = head.xz − previousHead.xz goes into U, and the warp adds L·Log(·) to `_HeadPosWS` without rotating it]
- Update each frame (§4.6, `P1-B02`): U ← U·Boost(d/L, κ), then `Orthonormalised()`. On crossing edge i: U ← inverse(G[i])·U and Tile ← the neighbour across i. G[i] maps the neighbour's frame into the current tile's frame. It equals the `F` that `VisibleSet` reports for that neighbour (§5.3).
- `WalkerPose.Start` is (`TileId.Origin`, `Iso2.Identity`). `IsValid` is true when `Tile` is not "none" and `Local.Defect()` is below 1e-6, for every κ. `default(WalkerPose)` is not a valid pose: start from `Start` or from `IWalkerKernel.Pose`.
- **In `State`** (Appendix B.1, lane D): `tileId` (6 × i64) holds the words of §5.4 in that order, and `xform` (9 × f64) holds `Local` **row-major**. `p[0]` is L (B.1). §7.3 says who writes what per space.

### 4.6 `ITileGraph` and `VisibleTile` [plan, §6.5; P1-X06 for the field names]

- `Q` is the number of tiles per corner: 5 (the {4,5} world), 6 (the dial's q = 6 snap), 3 (Small Planet, six tiles) or 4 (the flat grid). `Kappa` is `Curvature.OfQ(Q)`. `CentreSpacing` (2r) and `CircumRadius` (r_c) are in kernel units.
- `VisibleSet(centre, rho, into)` clears `into` and fills it with **every tile whose centre lies within hyperbolic distance `rho` (units of L, inclusive) of the centre tile's centre**, the centre tile included.
  - Each entry carries the tile's id; F, the step chain from that tile's frame into the centre tile's frame; and its graph distance from the centre tile (breadth-first depth, in edge steps).
  - Entries come in breadth-first order: non-decreasing `GraphDistance`, the centre first with `F` = identity, a fixed order within one distance, so the phone and the Station produce the same list. The set is defined by distance, not by the search: it is exact. `rho` is a centre-to-centre distance, included up to `rho + KernelConstants.RadiusEpsilon`; no padding is added, the caller pads. `F` is computed afresh on every call.
  - The list is caller-owned and reused, so a call allocates nothing once warm (callers pre-size it to 1,024; a tile crossing must not allocate, `P1-B06`). A graph that meets tiles it has not held before may allocate once for them.
  - On q = 3 all six tiles are always returned.
- Rules every implementation obeys:

| Rule | Detail |
|---|---|
| Round trip | `Neighbour(Neighbour(t, k), BackEdge(t, k)) == t` for every tile and k |
| Step consistency | `EdgeStep(t, k).Position` is at distance `CentreSpacing` from the base point. `EdgeStep(n, BackEdge(t, k))` equals `EdgeStep(t, k).Inverse()` to 1e-12, with n = `Neighbour(t, k)` |
| Bad input | `edge` outside 0..3: `ArgumentOutOfRangeException`. A tile that is not of this tiling: `ArgumentException`. `rho` negative or not finite: `ArgumentOutOfRangeException`. `into` null: `ArgumentNullException` |
| Threading | Main thread only |

- Counts that fix the semantics (test `VisibleSet_Counts`, viewer at a tile centre, {4,5}): 13 / 25 / 45 / 81 / 129 tile centres within ρ = 2 / 2.5 / 3 / 3.5 / 4, and 105 within 3.8425.
- Graph distance counts (`TileId_Growth`, OEIS A377322) for depths 0–8 are 1, 4, 12, 28, 64, 148, 340, 780, 1792.
- `TileGraph.BackEdge` is O(1): (4 − spin) & 3 from the spin the graph stores with each tile. `CubeTileGraph` forwards to the `CubeTiling` statics. A foreign id throws `ArgumentException`.
- Which `rho` the renderer passes is `P1-B06`'s choice. A guide [derived]: the draw rule draws a tile when its centre is within ρ + 0.8425 of the walker, and the walker is within about 0.8425 of the current tile's centre. A candidate set from the current tile's centre with `rho` = ρ + 2 × 0.8425 (plus the crossing hysteresis) therefore covers every walker position in the tile.
- **[P1-B06 D5]** The tile renderer's `TileSelection` passes `rho` = ρ + 2·r_c + 0.05 (`CandidateMargin`; units of L, r_c = 0.8424821 for q = 5) from the current tile's centre, and rebuilds its list only when the tile or ρ changes. The 0.05 covers the 0.02 crossing hysteresis at corners. Candidates: 277 at ρ = 3 and 485 at ρ = 3.5 (q = 5), 497 at ρ = 3.5 (q = 6). Test `Candidates_CoverEveryTileWithinReach`. The kernel's own `Candidates` (§4.9) use ρ + 2·r_c + 0.02.

### 4.7 How the pieces meet each frame [plan, §4.6, §6.3, §8.5]

```text
step 4 (kernel)   U = (U · Boost(d/L, κ)).Orthonormalised()        # d = 0 if |d| > 0.2 m or tracking is not Normal
                  if the walker left the tile through edge i: U = inverse(G[i]) · U; tile = neighbour;
                      graph.VisibleSet(tile, rhoCandidates, list)  # only when the tile changes
                  for each t in list: A[t] = inverse(U) · t.F      # double
                      d_t = Distance(A[t]·o, o);  draw t only if the §8.5 rule passes
step 6 (upload)   per drawn tile: _Anchor = (float) A[t] (cast last); globals _HypL, _HeadPosWS once per frame
```

The draw rule (§8.5, A.3): draw t when (a) d_t ≤ ρ + 0.8425 and (b) the disc of radius 0.8425·L round c = L·d_t along the bearing of A[t]·o is not wholly outside any side plane of the view frustum, widened by 12° to each side and 4° up and down. ρ is the registry parameter `curved.cullRho` (default 3, range 2 to 3.5, declared by `AppParams.DeclareRender`; its handler sets `IWalkerKernel.CullRho`). The test runs once per frame for both eyes. It is the only culling wherever the hyperbolic warp is on.

[P1-B06 D1, D2] What `Atlas.Rendering.TileDrawRule` does with the rule, so that no undrawn tile has a visible point nearer than ρ:

- Test (b) is applied to the vertical shell volume, the disc extruded up to H = 2.6 m (the tallest part of a tile shell, a corner post): a tile is wholly outside a plane when `n·c + r·|n_floor| + max(0, H·n_y) < 0`. With H = 0 this is exactly the plan's formula. The literal floor-disc rule culls tiles whose posts are still in view.
- A tile whose centre is within r_c of the walker is always drawn (the reference script's `drawn |= dc <= Rc`).
- The disc centre is `eye.xz + L·Log(A[t]·o)` at the floor height (world y = 0), the same placement as `AtlasWarp` gives the tile's chart origin.

### 4.8 `TileFrame` and `RuleS` [P2-B01; additive contract change, announced to all lanes (§6.4)]

Public types in `Atlas.Kernel` (`Kernel/Tiling/TileFrame.cs`, `RuleS.cs`). They are pure tile-graph arithmetic in ℤ[φ], so they stay in the kernel (§6.2 "TileId, tile graph") and keep `XFrame` and `ZPhi` internal (§3 rule 7).

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

### 4.9 The walker kernel: `IWalkerKernel`, `IDialKernel`, `KernelConfig`, `KernelStep`

Frame-loop step 4 (§6.3; README F5) is one call on one interface. It lives in **`Atlas.Kernel`**, not `Atlas.World` [P1-X06]: it uses only kernel and Mathematics types, so it needs no RoomModel type, runs in headless tests with no engine, and can be referenced by any assembly that may reference the kernel. **There is one walker kernel in the app.** The composition root creates one `HolonomyWalkerKernel` and publishes it in `AtlasServices` as `IWalkerKernel` and as `IDialKernel` (the same object). It is flat until a curved space configures it, and flat again after that space's `Exit` (§7.2).

```csharp
// Kernel/IWalkerKernel.cs
public struct KernelConfig {
    public int Q;                          // tiles per corner: 3, 4, 5 or 6
    public double MetresPerUnit;           // L for q = 5 and 6, R for q = 3; any positive value for q = 4
    public double TileSideMetres;          // q = 4 only: the tile side, also the centre spacing, metres. Ignored for other q
    public static KernelConfig Flat(double tileSideMetres);                         // Q = 4, MetresPerUnit = FlatMetresPerUnit
    public static KernelConfig Flat(double tileSideMetres, double metresPerUnit);
    public static KernelConfig Curved(int q, double metresPerUnit);                 // q = 3, 5 or 6; TileSideMetres = 0
}
public struct KernelStep {
    public bool Rejected;                  // longer than 0.2 m, a head position that is not finite, or tracking not Normal
    public int Crossings;                  // 0 to KernelConstants.MaxCrossingsPerStep
    public int LastEdge;                   // edge in the tile that was left, of the last crossing; -1 when none
    public double2 AppliedMetres;          // the displacement that was integrated; zero when Rejected
    public bool TileChanged { get; }       // Crossings > 0
}
public interface IWalkerKernel {
    ITileGraph Graph { get; }  double Kappa { get; }  double MetresPerUnit { get; }
    double CullRho { get; set; }           // ρ of the fog; setting it marks Candidates stale (rebuilt inside the next Step)
    WalkerPose Pose { get; }  List<VisibleTile> Candidates { get; }  KernelStep LastStep { get; }  long StepCount { get; }
    void Configure(in KernelConfig config);                 // new room or dial position; then as Reset(WalkerPose.Start). Walker standing still only
    void Reset(in WalkerPose start);                        // re-roots if outside its tile; the next Step integrates nothing
    void ResetToFloor(double2 floorMetres);                 // tile zero centred on the anchor, registered to the room; becomes the "previous head"
    KernelStep Step(double2 headFloorMetres, bool trackingNormal);                      // integrates; never advances a ramp
    KernelStep Step(double2 headFloorMetres, bool trackingNormal, double timeSeconds);  // what the frame loop calls
}
public interface IDialKernel : IWalkerKernel {
    int Q { get; }  double ConfiguredMetresPerUnit { get; }  double CurvatureNow { get; }  double CurvatureTarget { get; }
    bool Ramping { get; }  bool Still { get; }  bool CurvatureChanged { get; }
    bool SetCurvatureTarget(double kappa, double metresPerUnit);   // 4 s ramp; false unless q is 5 or 6
    void ResetToFloorFlat(double2 floorMetres);                    // registered at K = 0 without leaving the configured tiling
    bool Reaim(double alpha);                                      // U ← U·Rot(alpha); false at q = 4
}
```

- **The timed `Step`** [merge, announced contract change]. The platform side's page had `Step(head, tracking)` only, so the curvature ramp (§4.6: "from zero to the dial value, only while the walker stands") could not advance through the interface. The interface now has both overloads. The timed one is what `FrameLoopCore` calls: its time tells standing (tracking Normal, no jump, floor speed at most 0.1 m/s) from walking. The untimed one integrates the same displacement and never advances a ramp. A flat kernel ignores the time. Exactly one of the two per frame.
- **`IDialKernel`** [merge]. What a curved space uses to drive the walker: the dial position, the ramp and the re-aim. A space never calls `Step` and never restores the flat kernel (§7.2). `ConfiguredMetresPerUnit` is the dial's target L (or R), constant through a ramp; `MetresPerUnit` is the value of this frame. `SetCurvatureTarget(κ, L)` starts a ramp of four seconds from the curvature of now to κ/L² (κ = 0: to flat, L ignored); it advances only in a timed `Step` that finds the walker standing; the same target again does not restart the four seconds. `ResetToFloorFlat(p)` stands the walker in tile zero at K = 0 (the frame a translation by p in metres, no ramp under way) so the room is where it really is; while the walker stands so, `ResetToFloor` keeps it flat too, and once a ramp has begun or ended `ResetToFloor` registers at the configured curvature and ends the ramp. `Reaim(α)` is the one rotation outside walking (MP §10.2.3 step 4, rule 3): it keeps the position, the previous head and a ramp.
- **The hook takes the position, not the displacement** [P1-X06]. The kernel keeps the previous head position itself and forms d = head.xz − previousHead.xz as §4.6 writes it. No caller keeps a "previous position", so a reset in SpaceLab and the step in the frame loop cannot fall out of step with each other.
- **The frame loop calls it with the filtered head.** `FrameLoopCore` (step 4): `_frame.Kernel = _kernel.Step(new double2(HeadPosition.x, HeadPosition.z), Tracking == Normal, Time)`, then `_frame.Walker = _kernel.Pose`. `FrameContext.HeadPosition` is the geometry head pose after the pose filter of step 3 (room frame, metres). `FrameContext.Kernel` and `Walker` are how spaces read the result; `FloorDelta` is information for spaces and is not what the kernel receives.
- One call per frame, also when nothing moved and also while tracking is lost (`trackingNormal: false`). `StepCount` rising by exactly one per frame is the check of `P1-B02`. The pose filter (lane C, step 3) is the first line of defence against relocalisation jumps; the kernel applies the same 0.2 m rule again by itself, so it is safe when driven directly from a test or a replayed pose log. Both guards are kept.
- `State.flags` bit 2 ("pose jump rejected", `StateMessage.PoseJumpRejected`) is set when either guard fired: `_frame.PoseJumpRejected || _frame.Kernel.Rejected && _frame.Tracking == TrackingState.Normal`. A step the kernel rejects only because tracking is not Normal does not set the bit.
- **`Configure`** builds the graph, then behaves as `Reset(WalkerPose.Start)`. Not on the frame path; rule 4: only while the walker stands still. `Q` outside 3..6 is `ArgumentOutOfRangeException` from `Curvature.OfQ`. `MetresPerUnit` and, for q = 4, `TileSideMetres` must be finite and positive, else `ArgumentOutOfRangeException`.
- **`Reset(start)`**: `start.Tile` must be a tile of `Graph` and the frame must lie on the configured geometry, else `ArgumentException`. The pose is taken, then re-rooted with no hysteresis so that the walker is inside the tile it names. `Candidates` is rebuilt, `LastStep` cleared, and the previous head position forgotten: the next `Step` integrates nothing.
- **`ResetToFloor(p)`** is the pose that `Reset(WalkerPose.Start)` followed by one straight, unfiltered walk of p metres from the room centre would give. `p` becomes the previous head position, so a caller that passes the current head floor point loses no movement. A non-finite p is `ArgumentOutOfRangeException`.
- `CullRho` throws `ArgumentOutOfRangeException` for a negative or non-finite value. `Candidates` is owned by the kernel: read it, never modify it, never keep it across a `Configure`.

### 4.10 The flat kernel: `FlatTileGraph`, `FlatWalkerKernel` (q = 4, κ = 0)

The ordinary square grid, {4,4}: four tiles at every corner, every tile frame parallel to the frame of tile zero. It is the "fake tile provider" of `P1-X02`, and stays as the q = 4 position of the dial and as the kernel of every flat space (S0, S1, S3 by its own walker, S5, S6) and of the Spectator.

With a = `CentreSpacing`:

| Item | Definition |
|---|---|
| `FlatTileGraph(double centreSpacing)` | Tile side in kernel units: metres divided by L. Finite and positive, else `ArgumentOutOfRangeException` |
| Id of cell (i, j) | `new TileId(i, 0, j, 0, 1, 0)`: X = i, Y = j, T = 1. `FlatTileGraph.Cell(i, j)`; `Cell(0, 0) == TileId.Origin`. `TryGetCell(tile, out i, out j)` is false for any other id |
| A flat-grid id | `Xv == 0 && Yv == 0 && Tu == 1 && Tv == 0`. Anything else makes the graph methods throw `ArgumentException` |
| `Neighbour(t, k)` | k = 0: (i + 1, j). k = 1: (i, j + 1). k = 2: (i − 1, j). k = 3: (i, j − 1) |
| `BackEdge(t, k)` | (k + 2) mod 4 |
| `EdgeStep(t, k)` | `Boost(a · e_k, 0)` with e_0 = (1, 0), e_1 = (0, 1), e_2 = (−1, 0), e_3 = (0, −1). The same for every tile |
| Tiles at graph distance n | 1 for n = 0, then 4n |
| `CircumRadius` | a / √2 |

`VisibleSet(centre, rho, into)` adds, ring by ring (ring n from +x round towards +z), every tile with a · hypot(di, dj) ≤ rho + `RadiusEpsilon`, with `Chain` = `Boost((a·di, a·dj), 0)` and `GraphDistance` = n. Reference counts, rho / a on the left: 0 → 1, 1 → 5, √2 → 9, 2 → 13, √5 → 21, 2.5 → 21, 2√2 → 25, 3 → 29, 4 → 49, 5 → 81. With a = 1.0613 and rho = 3 the count is 21, the figure §4.6 of the plan quotes for "a flat grid of the same spacing".

`FlatWalkerKernel` is the κ = 0 `IWalkerKernel`: `Configure` takes `Q` = 4 only (3, 5 and 6 throw `NotSupportedException`: "FlatWalkerKernel carries kappa = 0 (q = 4) only; HolonomyWalkerKernel carries the curved dial positions"). Its step, with L = `MetresPerUnit`, a = `CentreSpacing`, h = `CrossHysteresis`:

```text
StepCount += 1
if headFloorMetres is not finite:  d = (0, 0) ; Rejected = true                        # previous is left as it was
else:
    d = hasPrevious ? headFloorMetres − previous : (0, 0)
    previous = headFloorMetres ; hasPrevious = true       # every call, also when the step is rejected
    Rejected = not trackingNormal or not (|d|² ≤ MaxStepMetres²) ; if Rejected: d = (0, 0)
Crossings = 0 ; LastEdge = −1
if not Rejected:                                          # a rejected step does not touch the pose at all
    U = (U · Boost(d / L, 0)).Orthonormalised()
    repeat at most MaxCrossingsPerStep times:
        (px, pz) = (U.M02, U.M12)
        e[0] = px − a/2 ; e[1] = pz − a/2 ; e[2] = −px − a/2 ; e[3] = −pz − a/2       # distance past each edge
        k = index of the largest e (the lowest on a tie); if e[k] ≤ h: stop
        U = EdgeStep(tile, k).Inverse() · U ; tile = Neighbour(tile, k) ; Crossings += 1 ; LastEdge = k
if Crossings > 0 or Candidates is stale:  VisibleSet(tile, CullRho + 2 · CircumRadius + h, Candidates)
```

| Property | Statement |
|---|---|
| 1 : 1 | An accepted displacement of d metres moves the walker by exactly d metres (rule 3) |
| Bounded local frame | After every `Step`, \|U.M02\| and \|U.M12\| are at most a/2 + h |
| Registration | After `ResetToFloor(p)` and steps of which none was rejected, with `Tile` = cell (i, j): L · (a·i + U.M02, a·j + U.M12) equals the last head floor point to 1e-9 m |
| A jump is rejected once | `previous` moves on every call, so the frame after a jump integrates normally. Movement made while tracking was not Normal is never integrated |
| No flicker | A walker who has just crossed an edge must come back more than 2h past it to cross again |
| Rejected step | Leaves `Pose` bit for bit unchanged |
| First step | The first `Step` after `Reset` or `Configure` integrates nothing |
| Cost | No allocation; no exception for any `double2` input |

`ResetToFloor(p)` at κ = 0 is computed directly: u = p / L; i = floor(u.x / a + 0.5), j = floor(u.y / a + 0.5) (a tie goes to the higher index); `Tile` = `Cell(i, j)`; `Local` = `Boost((u.x − a·i, u.y − a·j), 0)`. A non-finite p, or one more than 1e15 tiles from the anchor, is an `ArgumentOutOfRangeException`. `Reset(start)` requires a flat-grid tile and a valid pose, else `ArgumentException`.

The flat tile is the **lap square**: side s, centred on the RoomModel anchor, so tile centres are s apart. s is `RoomModel.LapSide` (§4.3, §4.4 of the plan: s = clamp(min(Wf, Df) − 0.4 m, 1.0 m, 3.2 m)). The kernel itself never sees a RoomModel.

### 4.11 The holonomy kernel: `HolonomyWalkerKernel` (q = 3, 4, 5, 6)

`Kernel/Walk/HolonomyWalkerKernel.cs` is an `IDialKernel` that wraps three walkers behind one interface:

| q | Walker | Graph | κ | State |
|---|---|---|---|---|
| 4 | `FlatWalkerKernel`, by delegation | `FlatTileGraph` | 0 | Bit for bit what §4.10 says. A freshly constructed `HolonomyWalkerKernel` is a `FlatWalkerKernel` (q = 4, 2.0 m tile, L = 1) |
| 5, 6 | `HolonomyRig` (Master Plan §4.6 holonomy rig) | `TileGraph` (one kept per q across dial changes: a `TileGraph` never evicts) | −1 | `Configure(KernelConfig.Curved(q, L))` puts the pose at (`TileId.Zero`, identity) |
| 3 | `SphereWalker` (S4's walker, the mathematics unchanged) | `CubeTileGraph` | +1 | `Configure(KernelConfig.Curved(3, R))` puts the walker on face 0, the centre of which is six zeros (Appendix B.1) |

- `Configure` with Q = 4 goes back to flat; any other Q throws `ArgumentOutOfRangeException`; a `MetresPerUnit` that is not finite and positive throws as well.
- `Kappa`, `MetresPerUnit` and `CurvatureNow` follow the ramp: at q = 5 or 6 they are read from the rig, so `MetresPerUnit` is 1 while the rig stands at K = 0 (§2.3) and `ConfiguredMetresPerUnit` keeps the dial's L.
- **Reset.** `Reset(in WalkerPose)` accepts a hyperbolic pose or a flat-tagged rotation such as `WalkerPose.Start`. It throws `ArgumentException` for a tile that is not of the graph, the none tile, a spherical tag at q = 5 or 6, or a matrix off the group. It re-roots with `TileGraph.Reroot`, ends any ramp at the configured L, and makes the next `Step` integrate nothing. `ResetToFloor(p)` is `TileZeroPlacement.Registered(p, κ, L)` in tile zero, re-rooted; at q = 3 on face 0; with the walker standing flat and no ramp asked for, at q = 5 or 6, it registers flat so a recentre cannot snap the room into the curved world.
- **Step.** At q = 5 or 6 the rig steps with a `HeadSample` of the frame's time. A step is `Rejected` when the head is not finite, tracking is not Normal, or the displacement exceeds 0.2 m. The ramp (`HolonomyRig`): `RampSeconds` = 4, advanced only in a step whose floor speed is at most `StillSpeed` = 0.1 m/s with tracking Normal and no jump; K moves linearly from its value at the start of the ramp, and U is rescaled (`Rescale`) so that the log-chart position and the heading about the tile centre are kept. `Still` and `CurvatureChanged` report the last timed step. At q = 3 the same `Still` rule applies and there is no ramp.
- `Candidates` at q = 5 or 6 is `VisibleSet(tile, CullRho + 2·CircumRadius + 0.02)`; at q = 3 all six faces; at q = 4 the flat kernel's list. Rebuilt on a tile change or a stale ρ; the same `List` instance throughout a configuration.
- **Differences from the flat guarantees** (§4.10): a rejected curved step leaves the pose equal to about 1e-15, not bit for bit (the rig renormalises U every frame); `Reset` re-roots with the 0.02 hysteresis band rather than h = 0; `Step` can allocate when the `TileGraph` meets tiles it has not held before.
- `HolonomyRig Rig` is reachable for tests (null at q = 3 and 4). A space drives the kernel through `IDialKernel` only.

Tests: `HolonomyWalkerKernelTests` (flat by default and bit-identical to `FlatWalkerKernel`; `Configure`; bit-identical to a directly driven rig for q = 5 and 6; the lap closing after q sides with the expected holonomy; jump, NaN and tracking rejection; `Reset` and `ResetToFloor`; the ramp needing the timed `Step`; the dial ramp only while standing; `Reaim`; the sphere), `HolonomyRigTests`, `HolonomyRigGraphTests`, `LapWalkTests`, `CurvatureDialTests`, `TileGraphContractTests` (the §4.6 rules on q = 3, 5 and 6).

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

SplitMix64 folded over the six words in wire order. Integer arithmetic only, so Mono, IL2CPP and the Station's .NET 8 agree bit for bit. All arithmetic is `unchecked` and wraps modulo 2⁶⁴.

```csharp
ulong h = 0x9E3779B97F4A7C15UL ^ worldSeed;
foreach (long w in new[] { Xu, Xv, Yu, Yv, Tu, Tv }) {
    h += (ulong)w + 0x9E3779B97F4A7C15UL;
    h = (h ^ (h >> 30)) * 0xBF58476D1CE4E5B9UL;
    h = (h ^ (h >> 27)) * 0x94D049BB133111EBUL;
    h ^= h >> 31;
}
return h;            // GetHashCode: unchecked((int)(s ^ (s >> 32))) with s = ContentSeed(0)
```

Golden values (computed independently; `TileId_Seed` holds them):

| Words `Xu, Xv, Yu, Yv, Tu, Tv` | `worldSeed` | `ContentSeed` | `GetHashCode` |
|---|---|---|---|
| 0, 0, 0, 0, 1, 0 (`Origin`) | 0 | `0x4194AB7A26ABAF68` | 1732183058 |
| 0, 0, 0, 0, 1, 0 | 1 | `0x2D1912F0CC4D7056` | 1732183058 |
| 0, 0, 0, 0, 1, 0 | `0x000A71A5` | `0x32B1E53DB78ED428` | 1732183058 |
| 0, 0, 0, 0, 1, 0 | 12345 | `0x1339BA6A0D8B35C0` | |
| 1, 0, 0, 0, 1, 0 (`Cell(1, 0)`) | 0 | `0x96D0F30FDF01A6F3` | 1238455804 |
| 0, 0, 1, 0, 1, 0 (`Cell(0, 1)`) | 0 | `0x88576DCBC0B24818` | 1222976979 |
| −1, 0, 2, 0, 1, 0 (`Cell(−1, 2)`) | 0 | `0x778545E01C883E4D` | 1796045741 |
| −1, 0, 2, 0, 1, 0 | `0x000A71A5` | `0x6C423E6694739896` | 1796045741 |
| 3, 0, −4, 0, 1, 0 (`Cell(3, −4)`) | `0xFFFFFFFF` | `0x54C68B49A291F26A` | −485598869 |
| 1, 0, 0, 0, 0, 1 (the {4,5} tile across edge 0 of tile zero) | 0 | `0xB27898E099B829B2` | 734048594 |
| 1, 0, 0, 0, 0, 1 | 12345 | `0x1A402E1FD39B2A54` | |
| 0, 0, 1, 0, 0, 1 (across edge 1) | 0 | `0x486B6F16E8179850` | |
| 0, 0, 1, 0, 0, 1 | 12345 | `0x7EE67468759C7B41` | |
| 0, 0, 0, 0, 0, 0 (`default`) | 0 | `0xB3A523F080E922EC` | 860619036 |
| 9223372036854775807, −9223372036854775808, 5, −7, 11, −13 | 12345 | `0x9218621C55847DC5` | 1192343521 |

The seed never uses `string.GetHashCode()`, `HashCode.Combine`, `System.Random` or the default struct hash: none is stable across runtimes. Test `TileId_Seed` checks the golden file of 64 (id, seed) pairs, identical under Mono and IL2CPP (device half at G6); `TileIdContractTests` holds the rows above. `worldSeed` and `Xu` enter the same addition (§9.3, point 7).

### 5.8 Other values of q

- **q = 6** [P1-B04]: tolerance-hash ids. Frames are doubles snapped to the {4,6} lattice (integers; √3·integers off the diagonal blocks). The id is the rounded centre (Xu = round(x/√3), Yu = round(z/√3), Tu = round(w)), with Xv = Yv = Tv = 0. Invariant 3(X²+Y²) − T² = −1. Tile zero (0,0,0,0,1,0) keeps the identity frame. Canonical frame: the lexicographically smallest (a11, a21, c1/√3) over M·R^j. Range ≈ 26 straight steps; beyond that, `InvalidOperationException` or `OverflowException`. Content seed: §5.7 unchanged.
- **q = 3** (S4): six tiles, one per face of a cube blown onto the sphere. As in B.1, `tileId` carries the face index in word 0 and 0 in the other words (`CubeTiling.FaceId`, `Atlas.Kernel`, `P1-B08`). Face numbering is settled:
  - Exact integer matrices (A.2 with cos(π/2) = 0, sin(π/2) = 1): R = Rot(+90°) = [[0,−1,0],[1,0,0],[0,0,1]], T = Boost((π/2, 0), +1) = [[0,0,1],[0,1,0],[−1,0,0]], H = diag(−1,−1,1), S_k = R^k·T·H.
  - Frames Φ0 = I, Φ(k+1) = S_k for k = 0..3, Φ5 = S_0·S_2. Centres Φ_f·o: 0 (0,0,1), 1 (1,0,0), 2 (0,1,0), 3 (−1,0,0), 4 (0,−1,0), 5 (0,0,−1).
  - Neighbours N[f][k], k = edge 0..3: f0 {1,2,3,4}, f1 {0,4,5,2}, f2 {0,1,5,3}, f3 {0,2,5,4}, f4 {0,3,5,1}, f5 {1,4,3,2}. Antipodes 0↔5, 1↔3, 2↔4.
  - Step(f,k) = Φ_fᵀ·Φ_N (neighbour frame → face frame) = S_k·R^J with J[f] = f0 {0,0,0,0}, f1 {0,1,0,3}, f2 {3,1,1,3}, f3 {2,1,2,3}, f4 {1,1,3,3}, f5 {2,2,2,2}. The edge of N that leads back is (4 − J) mod 4.
  - `VisibleSet` always returns all six faces: the centre, its four neighbours in edge order, then the antipode.
- **q = 4** (the dial's flat snap): the flat grid of §4.10; the plan defines no other tile graph for it.
- `TileId.Step` is the {4,5} step. For q = 3, 4 and 6, neighbours come from `ITileGraph`.

---

## 6. Shader contract: lane B's part

Lane A owns `AtlasCore.hlsl`, `AtlasPortalClip.hlsl` and the single `UnityPerMaterial` block; that part of the contract is on lane A's page (§10). Lane B owns `AtlasWarp.hlsl` and `AtlasGates.hlsl` (§8.3). Each include is edited only by its owner. Both live in `Assets/Atlas/Rendering/Shaders/Resources/`, are included by bare file name from `AtlasSurface.shader` in the order `Core.hlsl`, `AtlasCore.hlsl`, `AtlasWarp.hlsl`, `AtlasPortalClip.hlsl`, `AtlasGates.hlsl`, and declare **no uniform, no constant buffer and no keyword** themselves: everything they read is declared by `AtlasCore.hlsl`. They have never been compiled by a shader compiler; the Editor check is in `docs/verification/UNITY_SIDE_BRIEF.md` step 3c.

### 6.1 `AtlasWarp.hlsl`: one entry point [plan, §8.3, `P1-B03`]

```hlsl
// Vertex stage. Object position in (metres, object space), world position out (metres).
float3 AtlasWarp(float3 positionOS);
// Fragment stage. Sphere only: discard beyond 2.6 rad of geodesic distance from the head.
void AtlasWarpClip(float3 positionWS);
```

`Atlas/Surface` calls `AtlasWarp` in `AtlasVert` (`positionWS = AtlasWarp(input.positionOS)`, the only place a position is made) and `AtlasWarpClip` in `AtlasFrag` after `AtlasGatesClip` and `AtlasPortalClip`. The helpers `ExpH2`, `LogH2`, `ExpS2` and `LogS2` are internal to the file.

| `_WarpMode` | Mode | Output |
|---|---|---|
| < 0.5 | none | `TransformObjectToWorld(positionOS)`, the ordinary transform |
| 0.5 to 1.5 | hyperbolic | `p = mul((float3x3)_Anchor, ExpH2(positionOS.xz / _HypL)); t = LogH2(p) * _HypL;` → `float3(_HeadPosWS.x + t.x, positionOS.y, _HeadPosWS.z + t.y)` (§4.6, verbatim) |
| ≥ 1.5 | spherical | The same with `ExpS2` and `LogS2`, and `_HypL` holding R. `LogS2` of the exact antipode returns distance π along +x |

- **Input.** `positionOS.xz` is a point of the mesh's own chart, in metres. It is the true distance and bearing from the chart origin, which is the log or azimuthal-equidistant chart.
  - Curved tiles are generated in that chart: Klein grid point k ↦ L·atanh(‖k‖)·k/‖k‖, or R·atan(‖k‖)·k/‖k‖ on the sphere (A.6, `P1-B05`). The tile pivot is the tile centre.
  - Room content (Home shell, furniture) uses its RoomModel coordinates in metres, relative to the room centre or to its own anchor (§4.6, `P1-B11`).
- **Height.** `positionOS.y` passes through unchanged as world y (§4.6: "the floor stays level and at the real floor's height"). See §9.1.
- **The Transform is ignored when warped.** In modes 1 and 2 the renderer's Transform (`unity_ObjectToWorld`) does not affect the position. `_Anchor` carries all placement. A built-in mesh whose fitting scale lives on its own Transform would therefore be drawn unscaled in a curved space: the scale must be baked into the mesh (open, §9.4).
- **Once per head.** The warp is centred on `_HeadPosWS`, the centre of the head, not on each eye. Each eye's view-projection then gives correct stereo depth (§4.6, BP §4).
- **Cost.** Exp is evaluated once per vertex, and sinh and cosh are derived from that one exponential (§8.3). The mode is a per-material number tested with an ordinary branch [P1-X06, following §8.3 "prefer a per-material number"]: one material can warp while another in the same frame does not (the warning cue, the menu and anything fixed to the head stay unwarped).
- **Normals.** The warp returns positions only. Nothing in `AtlasCore`'s list (palette, vertex AO, `fwidth` lines, fog; §8.3) needs transformed normals; the shader does not light by the normal (D23).
- **Acceptance** (`P1-B03`): GPU positions match the kernel's CPU reference (`Geometry2`, `Iso2`) to 1 mm for ρ ≤ 3. The shared shader stays SRP-Batcher compatible. Exp is evaluated once per vertex. Not yet run on a GPU (`WarpGpuTests.Warp_MatchesKernel_Gpu`, Editor).

### 6.2 Inputs

| Name | Kind | HLSL type | Unit | Declared in | Written by | Meaning |
|---|---|---|---|---|---|---|
| `_HypL` | global | `float` | metres | `AtlasCore.hlsl` | `LookUploadStep` through `AtlasLook.Upload`, once per frame at step 6 | `IWalkerKernel.MetresPerUnit`: L in hyperbolic mode, R in spherical mode [P1-X06: one id for both]. A value that is not positive is uploaded as 1 |
| `_HeadPosWS` | global | `float4` | metres, world | `AtlasCore.hlsl` | the same call | xyz = `FrameContext.HeadPosition`: the geometry head, **the same sample whose displacement stepped the kernel this frame** [merge, §9.3 point 8]. Its y is not used; w = 0 |
| `_Anchor` | per material | `float4x4` | none | `UnityPerMaterial` | the space or renderer that owns the material, with `MaterialPool.SetAnchor` | The upper-left 3 × 3 is the isometry from the mesh's chart into the walker's frame. Tile t: inverse(U)·F[t]. Room object: inverse(U)·F[tile zero]·(its placement in tile zero, `TileZeroPlacement.Object`). Computed in double, cast last. Other entries: 0, with [3][3] = 1. Default identity |
| `_WarpMode` | per material | `float` | none | `UnityPerMaterial` | `MaterialPool.SetWarpMode` | 0 none, 1 hyperbolic, 2 spherical [P1-X06: per-material number, as §8.3 prefers to a keyword]. Default 0 |

- [P1-B03] `AtlasWarpCpu` (float, line for line with the HLSL) and `AtlasWarpReference` (double, from `Geometry2` and `Iso2`) in `Atlas.Rendering` (`Rendering/Warp/`) are the EditMode warp for later checks (P1-B05 seams, P1-B06 draw check). `WarpAnchor.ToMatrix` is the upload cast.
- **Upload layout** [derived]: `Matrix4x4 m; m[r, c] = (float)A.M[c][r]` for r, c < 3. Unity's `m[row, column]` reaches HLSL as `_Anchor[row][column]`, so `mul((float3x3)_Anchor, p)` is A·p.
- A shader that includes `AtlasWarp.hlsl` or `AtlasGates.hlsl` on its own (a test shader, a compute probe) must declare what they read: for the warp `float4x4 _Anchor; float _WarpMode; float _HypL; float4 _HeadPosWS;` and `TransformObjectToWorld`; for the gates `float4 _Gate0, _Gate1, _Gate2, _Gate3;` and URP Core. `Tests/Warp/AtlasWarpProbe.compute` and `Tests/AlignedCells/AlignedCellsGatesTest.shader` do.

### 6.3 What the shared shader does with the output (hand-over to lane A)

- **Geodesic distance for free.** In modes 1 and 2 the log map puts every vertex at its true distance from the head, so `length(posWS.xz − _HeadPosWS.xz) / _HypL` is the geodesic distance in units of L (or R) [derived; R1 §F.6]. The fog in `AtlasCore.hlsl` uses it per vertex, interpolated: it starts at ρ − 1 and is fully opaque at ρ = `curved.cullRho` (§8.5). The fog's ids are lane A's (`AtlasLook.SetFog`).
- **Small Planet** (`P1-B08`): in spherical mode fog starts at 0.5π and is opaque at 0.8π, in units of R (`planet.fogRho` moves the opaque end within [0.5π, 0.8π], start = opaque − 0.3π). `AtlasWarpClip` discards fragments whose interpolated per-vertex geodesic distance g = |posWS.xz − _HeadPosWS.xz| / _HypL exceeds 2.6 rad (`_WarpMode > 1.5` and `_HypL > 0`), which prevents smeared triangles near the antipode. `SmallPlanetShading` is the CPU reference.
- **No keyword.** The mode is a per-material number (§8.3: no new keyword without lane A).

### 6.4 CPU side [plan, §8.3]

- One pooled material instance per tile or per room object, rented from `MaterialPool`, created at run time and never saved as an asset. `_Anchor` and `_WarpMode` are set on it. **No MaterialPropertyBlocks** anywhere in `Atlas.*` (`verify.py rules`, F7).
- Every renderer that uses a warped material gets huge `localBounds` (`AtlasWarpBounds`, `P1-B03`'s component; `WorldLint_All` checks it). Unity then culls nothing, and the §8.5 draw rule decides what is drawn. The Small Planet always draws its six tiles. `AtlasWarpBounds` sets a 1000 m cube centred on the object origin; `Apply(Renderer)` is static, for pooled renderers after a mesh is assigned. A space that warps the room content's renderers sets the bounds on them itself and restores them in `Exit` (lane C §9.1).
- Globals and anchors are written in frame step 6 (Upload, §6.3), after the kernel step and before rendering, once for both eyes. `_HypL` and `_HeadPosWS` have one writer: `LookUploadStep`, which runs after the entered module's own upload. `TileRenderer` and `CurvedRoomView` write per-material values (anchor, warp mode, fog through `AtlasLook.SetFog`) and never these two globals. A scene with no frame loop calls `AtlasLook.Upload` itself.
- The kernel the frame loop holds must report `MetresPerUnit` equal to the L the tile renderer was configured with: `HolonomyWalkerKernel` does, at K = −1/L² and at q = 3. During a ramp that stands at K = 0 it reports 1, and the renderer draws the flat room.

### 6.5 `AtlasGates.hlsl`: sight-line gates for Aligned Cells [plan for the purpose, §4.7, §8.3; P1-X06 for the signature]

```hlsl
// Fragment stage. Discards the fragment if it lies on the wrong side of any enabled gate.
void AtlasGatesClip(float3 positionWS);
```

| Name | Kind | HLSL type | Meaning |
|---|---|---|---|
| `_Gate0`, `_Gate1`, `_Gate2`, `_Gate3` | per material (`UnityPerMaterial`) | `float4` each | `(post.x, post.z, side, unused)`: a corner post on the floor in world metres, and the side to keep. `side` = +1 or −1; `side` = 0 turns the gate off. Default (0, 0, 0, 0) |
| `_WorldSpaceCameraPos` | URP, per eye | `float3` | Each eye clips against lines through its own position, which is what makes the clip correct in stereo |

- Per gate i with side ≠ 0: with e = `_WorldSpaceCameraPos.xz` (the eye's own position; the rig has one camera per eye), g = post − e and d = positionWS.xz − e, the fragment is discarded when side·(g.x·d.y − g.y·d.x) < 0. This is R1 §F.7's formula.
- Four separate vectors, not an array or one `float4x4` [merge, replaces kernel.md's `_Gates`]: they need no array setter and are plainly SRP-Batcher compatible. The include copies them into a local array of four for an unrolled loop. The C# side is `MaterialPool.SetGate(material, gate 0..3, float2 post, float sign)` and `ClearGates(material)`; `AtlasShaderIDs` has `Gate0` to `Gate3` and no `Gates`.
- There is no stencil. Each eye uses its own position, and the corner post hides the seam (§4.7).
- Furniture in Aligned Cells is drawn once per eye, unclipped (gates off) (§4.7, `P1-B12`). A material whose gates are all zero is never clipped. Portal clipping and the warp never share a material (§8.3), and neither do the warp and the gates, since Aligned Cells is drawn flat.
- Acceptance lives in `P1-B10`: 13 / 15 / 17 instances for the centre, edge and corner cell of a 3 × 3 grid (`Mode2_SightLines`).

### 6.6 Constraints on both includes [plan, §8.3, §11.1]

SRP-Batcher compatible at every gate. No new shader keyword without lane A. ≤ 8 compiled variants for the shared shader (`Shader_Contract`, proposed; `Atlas/Surface` has one pass and no keyword). Positions in full `float`. `AtlasWarpClip` and `AtlasGatesClip` call `clip()` and sit in files that are also compiled for the vertex stage and, in `AtlasWarpProbe.compute`, for a compute shader: the Editor compile is the first check of that.

### 6.7 Property ids

`AtlasShaderIDs.cs` (`Atlas.Rendering`, `Rendering/Look/`) holds every property id; it is edited by lanes A and B only, and an id is added or changed only after all four lanes are told (§6.4). Lane B's entries, as built:

```csharp
public static readonly int Anchor    = Shader.PropertyToID("_Anchor");     // per material float4x4
public static readonly int WarpMode  = Shader.PropertyToID("_WarpMode");   // per material float: 0 none, 1 hyperbolic, 2 spherical
public static readonly int Gate0     = Shader.PropertyToID("_Gate0");      // per material float4: post.x, post.z, side, unused
public static readonly int Gate1 /* _Gate1 */, Gate2 /* _Gate2 */, Gate3 /* _Gate3 */;
public static readonly int HeadPosWS = Shader.PropertyToID("_HeadPosWS");  // global float4: xyz metres, world; w = 0
public static readonly int HypL      = Shader.PropertyToID("_HypL");       // global float, metres (L, or R in S4)
```

The shader names `_HypL`, `_HeadPosWS` and `_Anchor` are [plan] (§4.6). `_WarpMode`, `_Gate0` to `_Gate3` and the C# field names are [P1-X06]. Lane A's ids (palette, fog, portal plane and the rest) are on lane A's page. `_WorldSpaceCameraPos` is Unity's built-in and needs no entry.

---

## 7. Owners, code and State

### 7.1 Items, code and tasks

| Item | Code | Task | Lane |
|---|---|---|---|
| `Iso2`, `Geometry2`, η-Gram–Schmidt | `Atlas.Kernel` | `P1-B01` | B |
| U update, crossing hook, curvature ramp while standing still | `Atlas.Kernel` (`Walk/HolonomyRig.cs`, `HolonomyWalkerKernel.cs`) | `P1-B02` | B |
| `AtlasWarp.hlsl`, the huge-`localBounds` component, warp ids | `Atlas.Rendering` | `P1-B03` | B |
| `TileId`, `TileGraph` (`ITileGraph`), `VisibleTile`, content seed | `Atlas.Kernel` (`Tiling/`) | `P1-B04` | B |
| Tile meshes in the log chart | `Atlas.Rendering` (`Tiles/TileMeshGenerator.cs`) | `P1-B05` | B |
| Tile renderer, draw rule, anchors | `Atlas.Rendering` (`Tiles/TileRenderer.cs`, `TileDrawRule.cs`, `TileSelection.cs`) | `P1-B06` | B |
| World generator, regions, props, WorldLint | `Atlas.World` (`Generation/`), `Atlas.Rendering` (`Tiles/Props/`), `Atlas.World.Lint.Editor` | `P1-B07` | B |
| {4,3} graph, the sphere walker, the spherical branch of the warp | `Atlas.Kernel` (`CubeTiling`, `SphereWalker`), `AtlasWarp.hlsl`, `Atlas.Spaces.SmallPlanet` | `P1-B08` | B |
| Curvature dial, the 3-4-5 walk (S2) | `Atlas.Kernel` (`CurvatureDial`, `LapWalk`), `Atlas.Spaces.CurvedWalk` | `P1-B09` | B |
| `AtlasGates.hlsl`, gates, Aligned Cells | `Atlas.Rendering`, `Atlas.Spaces.AlignedCells` | `P1-B10` | B |
| The room becomes tile zero | `Atlas.Kernel` (`TileZeroPlacement`), `Atlas.Rendering` (`Tiles/CurvedRoomView.cs`) | `P1-B11` | B |
| Room content in Aligned Cells | `Atlas.Spaces.AlignedCells` | `P1-B12` | B |
| The step hook, `FrameContext`, SpaceLab, `SpaceRegistry` | `Atlas.App`, `Atlas.World` | `P1-A08` | A |
| Calls to `AtlasWarp` and `AtlasGatesClip`; the `UnityPerMaterial` block with `_Anchor`, `_WarpMode`, `_Gate0` to `_Gate3`; fog and the look's upload | `AtlasCore.hlsl`, `AtlasLook` | `P1-A06` | A |
| `Mesh4D` and the slicing job (in `Atlas.Kernel`, not part of this contract) | `Atlas.Kernel` | `P1-A12` | A |
| `TileFrame`, `RuleS` (§4.8) | `Atlas.Kernel` (`Tiling/`) | `P2-B01` | B |

### 7.2 How the app drives the kernel, and how a space registers

| When | Who | Call |
|---|---|---|
| App start | Composition root (`App/AtlasApp.cs`) | `var kernel = new HolonomyWalkerKernel(); AtlasServices.Register<IWalkerKernel>(kernel); AtlasServices.Register<IDialKernel>(kernel);` The same instance goes to `FrameLoopCore`, `SpaceLab`, `LookUploadStep` and `AppParams.DeclareRender` |
| A RoomModel becomes active (scan confirmed, file loaded, fixture chosen) | `SpaceLab.SetRoom(room)` | With no space entered, or a flat one: `kernel.Configure(KernelConfig.Flat(side))` with `side = room.LapSide`, or `KernelConstants.DefaultFlatTileSideMetres` when `LapSide` is not positive; then `IWalkerReset.LatchRoomReset()` |
| The first frame whose head pose is in the room frame, after step 3 and before step 4 | `FrameLoopCore.RunUpdate` | `ResetWalkerToFloor()`, then `pipeline.ResetGeometry()` |
| A space is entered | `SpaceLab`, just before `ISpaceModule.Enter` | `if (module.Registered) ResetWalkerToFloor(); module.Enter(kernel.Pose);` A space that is not registered is entered with the pose the kernel already has. Registered: S0, S1, S3, S5, S6. Not registered: S2 and S4 (§4.9 of the plan: registered only until the first step) |
| S2 `Enter` | `CurvedWalkCore` | `kernel.Configure(KernelConfig.Curved(q, L))` for q = 5 or 6; `kernel.ResetToFloorFlat(0)`; then `Register(head)` on the first tick and `SetCurvatureTarget(−1, L)` while the walker stands. The dial snaps q = 3 to S4 |
| S4 `Enter` | `SmallPlanetCore` | `kernel.Configure(KernelConfig.Curved(3, R)); kernel.ResetToFloor(head)`; the dial's other positions are a switch request to S2 |
| S3 `Enter` | `AlignedCellsSpace` | The kernel stays flat and registered; the space walks its own `CellWalker` and takes the head from the frame |
| A space is left | `SpaceLab.ExitCurrent` | After `module.Exit()`, if the kernel is not flat: `kernel.Configure(KernelConfig.Flat(side))` then `ResetWalkerToFloor()` (unless a room reset is latched). A space never restores the flat kernel itself |
| S2 to S3 (Mode 1 to Mode 2) | `SpaceLab` | Keeps the tile and the frame [merge, plan §4.8 and §4.9]. Before `Exit`, with S2 entered at q = 5, κ = −1 and no room reset pending, it reads `kernel.Pose`; after the flat restore it calls `module.Enter(in keptPose)` on S3 in place of the registered start. S3 then enters with the tile id of Mode 1 and its flat-grid cell. No `ISpaceHost` member was added: the rule is inside `SpaceLab` |
| Operator command `home` / B held 1 s | `CommandRouter.Home` → `SpaceLab.ReturnHome` | Already in Home: `ResetWalkerToFloor()` unless a room reset is latched. Otherwise a switch to Home, which resets on `Enter` as above |
| Operator command `recentre` | `CommandRouter.Recentre` | `IFrameLoop.ResetWalkerToFloor()`, then `pipeline.ResetGeometry()`. In a curved space `ResetToFloor` registers at the configured curvature |
| Every frame, step 4 | `FrameLoopCore.RunUpdate`, driven by `AtlasFrameLoop` | `kernel.Step(headFloorMetres, trackingNormal, time)` (§4.9) |

- Nobody outside the frame loop and SpaceLab calls `ResetToFloor` or `Reset`, except a space on its own `Enter` (S2 and S4 above) and S4 when the dial changes R while the walker stands. **There is no reset on tracking recovery**: in a curved space the world would jump.
- The two entry points on the loop (`App/Frame/FrameLoopCore.cs`; interfaces on lane A's page): `IFrameLoop.ResetWalkerToFloor()` (kernel `ResetToFloor` at the geometry head's floor point of the frame in progress; a head that is not finite resets to the anchor) and `IFrameLoop.ResetWalker(in WalkerPose)`.
- **How a space registers.** Each space's assembly has a static class with `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` that calls `SpaceRegistry.Register(SpaceId, Func<ISpaceModule> factory, Action<ParamRegistry> declareParams)`: `CurvedWalkRegistration`, `AlignedCellsRegistration`, `SmallPlanetRegistration`, exactly like `GluedRegistration`. The factory creates a `GameObject` named `SpaceIds.SceneName(id)` and adds the `MonoBehaviour` module (`CurvedWalkSpace.Create`, `AlignedCellsSpace.Create`, `SmallPlanetSpace.Create`). The generated scenes `Space_CurvedWalk`, `Space_AlignedCells`, `Space_SmallPlanet` (`tools/gen_scenes.py`, `Assets/Atlas/Scenes/`, all three in the build list) hold one root object with that component. Services come from `AtlasServices` (`IDialKernel`, `IRoomContent`, `ISpaceHost`, `IAtlasInput`, `IEventLog`, `MaterialPool`, `IStereoRig`). A module is created per entry and unloaded after `Exit`.
- **Room content in curved spaces** [merge, plan §4.9; lane C §9.2]: the room is `IRoomContent`'s own renderers, never a second copy. `CurvedRoomView` (S2, S4) and `AlignedCellsRoomView` (S3) set wall mode, floor and object visibility, write `_WarpMode` and `_Anchor` to `IRoomContent.ShellMaterial` and to each object view's material (each with its own anchor at its scanned base, so it stays rigid), set large `localBounds` and restore all of it in `Exit`; `SpaceLab` calls `IRoomContent.ResetPresentation()` between spaces.
- **Parameters** [merge]: declared once, through the real `ParamRegistry`. `AppParams` declares `curved.cullRho` and `curved.mode`, and that stays. S2 declares `curved.q` (Int 3..6, default 5, on release) and `curved.strong` (Bool) through `CurvedDialParams`; S3 the group `cells`; S4 `planet.R` and `planet.fogRho` and shares `curved.q` and `curved.strong` (group `curved`, activated by hand while S4 is entered, because `SpaceLab` activates only `SpaceIds.ParamGroup(id)`, which is `planet` for S4).

### 7.3 What goes in `State`

`FrameLoopCore.FillState` writes, for every space: flags, scene and space ids, `worldVer`, the head pose (raw), `bumpLevel`, `sheet` = 0, and `p[0]`, `p[1]`, `p[2]` = `(float)IWalkerKernel.MetresPerUnit`, `CullRho − 1`, `CullRho` (`StateMessage.ParamL`, `ParamFogStart`, `ParamCullRho`; 1 in flat spaces), `p[3]` to `p[7]` = 0. At q = 4 `tileId` is six zeros and `xform` the identity; otherwise `tileId` is `kernel.Pose.Tile` in wire order and `xform` is `kernel.Pose.Local` row-major, read at step 8a, after the module's Tick. The entered module's `WriteState` then adds or overwrites only what it owns:

| Space | `tileId` | `sheet` | `xform` | `p[3]` to `p[7]` |
|---|---|---|---|---|
| S0 Home, S6 Slicer | six zeros | 0 | identity | zeros (S6: its own, lane A §14.3) |
| S1 Glued, S5 Impossible Rooms | the cell, or the room id (lane A §14) | 0 / room | identity | their own (lane A §14) |
| S2 Curved Walk | the kernel's tile (the module writes nothing here) | 0 | the kernel's `Local` | `p[3]` = the dial's q (`CurvedWalkCore.StateQ`), `p[4]` = the signed curvature of this frame (`StateCurvature`, 1/m², `kernel.CurvatureNow`), the rest 0 |
| S3 Aligned Cells | the cell frame's tile id, kept from Mode 1 | the cell index `y · countX + x` | the cell's quarter-turn (a rotation by `Turn` · 90°, translation 0) | `p[3]` shown depth, `p[4]`, `p[5]` the grid's cell counts, `p[6]` its cell side in metres, `p[7]` 0 |
| S4 Small Planet | the kernel's face id (word 0 = the face, the rest 0) | 0 | the kernel's `Local` (κ = +1) | `p[3]` = 3, `p[4]` = 1/R², `p[5]` = the opaque end of the fog (rad), `p[6]`, `p[7]` = 0 |

S2, S3 and S4 implement `ISpaceStateReader.ReadState`, which the Spectator host calls before `Tick` so that the Station's copy takes the tile, the frame and these values from the phone's State; κ is derived from `spaceId` (§4.2). S2 also implements `ISpaceWorld` (`WorldSeed`, `GeneratorVersion`, `ContentHash`), which the world event reports (lane D §2.6). Whether the Station's flat twin follows a curved walker is §10, item 14.

---

## 8. Tests that hold this contract (§11.1, all lane B unless stated, headless and EditMode, double)

The mathematics is tested without the engine, in `Assets/Atlas/Tests/` (`verify.py test <Assembly>.Tests --tag <t>`; counts in `docs/verification/UNITY_SIDE_BRIEF.md` §3d). Random tests use `new System.Random(KernelTestUtil.Seed)` with `Seed = 20261004`.

| §11.1 test | File (assembly) |
|---|---|
| `Kernel_ExpLog`, `Kernel_EtaOrthonormal`, `Kernel_Inverse`, `Holonomy_Triangle`, `Closure_345`, `Lorentz_Drift` | `Kernel/KernelMathTests.cs` (`Atlas.Kernel.Tests`) |
| the flat parts and the upload and wire forms | `Kernel/Iso2FlatTests.cs` |
| `TileId_RingInvariant`, `TileId_Growth`, `TileId_VertexClosure`, `TileId_Unique`, `TileId_Seed` (golden file of 64), `TileId_Resolve`, `TileId_CrossingCost`, `VisibleSet_Counts` | `Kernel/TileIdTests.cs`; `TileIdContractTests.cs` (the golden rows of §5.7, `TileId_Basics`, `TileId_SeedSpreads`) |
| the §4.6 rules on q = 3, 5, 6; the {4,5} back-edge values | `Kernel/TileGraphContractTests.cs` |
| the flat grid and the flat kernel | `Kernel/FlatTileGraphTests.cs`, `FlatWalkerKernelTests.cs` (`FlatWalker_Integrates`, `_Crossing`, `_NoFlicker`, `_JumpRejected`, `_LapSquare`, `_UnitFree`, `_Candidates`, `_ConfigureAndReset`, `_StepAllocatesNothing`) |
| the holonomy kernel, the rig, the dial, the lap, `TileZeroPlacement`, `RuleS` | `Kernel/HolonomyWalkerKernelTests.cs`, `HolonomyRigTests.cs`, `HolonomyRigGraphTests.cs`, `CurvatureDialTests.cs`, `LapWalkTests.cs`, `TileZeroPlacementTests.cs`, `RuleSTests.cs` |
| the guards that remain | `Kernel/KernelCurvedGuardTests.cs` (`Kernel_CurvatureGuards`: `OfQ`, `Require`, the strict `Iso2` members, curved `Iso2` and `Geometry2` results, the mixed-geometry product, `TileId.Origin.Step`, `FlatWalkerKernel` refusing q = 3, 5, 6) |
| `Float32_Upload`, the warp on the CPU, gates | `Warp/WarpTests.cs` (`Atlas.Warp.Tests`); `WarpGpuTests.cs` is `[Category("Unity")]` |
| `TileMesh_SeamsEdges` | `TileMeshes/TileMesh_SeamsEdges.cs` (`Atlas.TileMeshes.Tests`) |
| the draw rule, the recorded lap, the renderer | `Tiles/` (`Atlas.Tiles.Tests`): `TileDrawRuleTests`, `TileLapTests`, `TileRendererTests` (engine part `[Category("Unity")]`) |
| `Mode2_SightLines` | `AlignedCells/SightLineTests.cs` (`Atlas.Spaces.AlignedCells.Tests`) |
| `WorldLint_All` and the generator | `WorldGen/` (`Atlas.WorldGen.Tests`); `WorldLint_All` builds meshes and is `[Category("Unity")]` |
| S2, S3, S4 | `CurvedWalk/`, `AlignedCells/`, `SmallPlanet/`; the five-corner lap and the S2 to S3 to S4 walk through the real frame loop and `SpaceLab` are in `Tests/App/E2E/E2ERehearsalTests.cs` |

Where this page quotes a test, §11.1 of the plan wins.

---

## 9. Points settled, and what the merge changed

### 9.1 World frame, floor height and yaw [settled]

The plan's warp sets world y = object y, and adds the log map to `_HeadPosWS` in world axes (§4.6). Both hold as written because the Unity world frame is the RoomModel anchor frame (README F4): the anchor at the origin, its axes along world x and z, y = 0 on the real floor. The pose pipeline makes it so, so no floor offset and no yaw ψ appear anywhere, and `State` gives the head pose and `xform` in the same axes. `TileDrawRule.FloorY` = 0.

### 9.2 Resolving an id received from elsewhere (lane D) [settled]

[P1-B04] Build `TileId` from the six `State.tileId` words. Pass it to `TileGraph(q).VisibleSet / Neighbour / StepMatrix` of the same q. An id the graph has not seen is resolved by descent from tile zero: in each tile, step through the edge e maximising (X, Y, −X, −Y) of the target expressed in that tile's frame, updated with S_e⁻¹ by additions only, until the target is reached. The result is the target's canonical frame (§5.3), so edge numbering, F and content seeds equal the phone's whatever order tiles were found in. `TileId.Step` ({4,5}) uses the same rule and needs no graph. Invalid ids throw `ArgumentException`.

### 9.3 What the merge decided

The eight questions that were open for the lead when the platform side's page was written, and what the combined code does:

| # | Question | As built |
|---|---|---|
| 1 | `TileId.Step` on a bare id | Implemented: the {4,5} step by descent (§4.4). It throws for an id that is not a {4,5} tile |
| 2 | κ tag inside `Iso2` | Kept (§4.2). `Identity` and `Rot(angle)` are flat-tagged; a flat factor is neutral in a product |
| 3 | How the curvature ramp is expressed | A timed `IWalkerKernel.Step` and `IDialKernel.SetCurvatureTarget` (§4.9): κ stays a sign, L varies |
| 4 | L at κ = 0 | 1.0 m per unit (`FlatMetresPerUnit`); a ramp that stands at K = 0 reports 1 |
| 5 | Re-registration after tracking loss | None. Only room change, Return to Home, the `recentre` command and a registered space's `Enter` reset the walker (§7.2). In a curved space `ResetToFloor` registers at the configured curvature |
| 6 | Normals under the warp | The warp returns positions only; unchanged (§6.1) |
| 7 | `ContentSeed` across worlds | Unchanged: `worldSeed` and `Xu` enter the same addition, so (`worldSeed` 1, `Xu` 1) and (0, 0) give the same seed when the other five words agree. Seeds are unique within one world. Changing it moves every golden value |
| 8 | Which head position `_HeadPosWS` carries | The geometry head, `FrameContext.HeadPosition`, the sample that stepped the kernel (§6.2). It differs from the raw head only in a frame where the pose filter rejects a jump |

Answers 2, 4 and 7 are as built and need the lead's confirmation (§9.4); 1, 3, 5, 6 and 8 follow from the decisions taken for the merge.

Other changes [merge]:

1. One kernel: lane B's `TileId`, `Iso2`, `TileGraph`, `CubeTileGraph` and `HolonomyRig` are the implementation; the platform side's stubs (`Kernel/TileId.cs`, `KernelMaps`, `Curvature.NotAvailable`) are deleted. `KernelMaps` is `Geometry2`.
2. `IWalkerKernel.Step` gained the timed overload; `IDialKernel`, `KernelConfig.Curved`, `HolonomyWalkerKernel` and `SphereWalker` are new. `FlatWalkerKernel` and `FlatTileGraph` are kept, on the unified types.
3. `TileId.ToString()` is the comma-separated wire text. `TileId.Step` no longer throws `NotSupportedException`. `VisibleTile` is a readonly struct built with its constructor; `Tile` and `Chain` are read-only properties over `Id` and `F`.
4. Strictness of κ: see §2.2. `Iso2.Orthonormalised()` at κ = 0 now throws for a matrix with no rotation part instead of returning NaN. `Curvature.Require` throws only `ArgumentOutOfRangeException`.
5. The shader: gates are `_Gate0` to `_Gate3`; the warp include declares no uniforms and the globals are `float4 _HeadPosWS` and `float _HypL` in `AtlasCore.hlsl`; `AtlasWarpClip` is new; the warp centres on the filtered head.
6. `EventLog.LogJson(scene, ev, dataJson)` and the extension of the same name on `IEventLog` exist so that lane B's blade events can carry their own scene (lane D §2.6).

### 9.4 Open for the lead

1. **κ tag, L at κ = 0, `ContentSeed`** (answers 2, 4 and 7 of §9.3): confirm as built, or rule otherwise. A change to the seed moves every golden value and every generated world.
2. **κ strictness.** `Iso2.Boost`, `Iso2(double3x3, kappa)` and `Geometry2` take κ by its sign; the members from the platform side throw for any value but −1, 0 and +1. Decide whether the lenient three become strict.
3. **Normals.** If `AtlasCore` ever shades by normal, lane B adds a companion function to the warp; that would be an addition to the contract, announced to all four lanes.
4. **Built-in furniture under the warp.** A built-in mesh's fitting scale lives on its own Transform, which the warp ignores in modes 1 and 2. The scale must be baked into the mesh, or lanes A, B and C must agree another route.
5. **Material budget.** `TileRenderer` rents 128 pooled materials at `Configure` and the pool holds 256 for everything. A second curved renderer or cross-fades would make the pool double and log `materials.grow`. Decide whether to rent lazily per drawn tile.

---

## 10. What lanes A, C and D may and may not assume

| # | Statement |
|---|---|
| 1 | An `IWalkerKernel` (and `IDialKernel`) is published by the composition root. It is a `HolonomyWalkerKernel`: flat (q = 4, κ = 0, `MetresPerUnit` = 1.0) except while S2 or S4 is entered |
| 2 | `Step` is called once per frame at step 4 with the filtered head and the frame's time, costs microseconds, allocates nothing in the flat case and never throws |
| 3 | `Pose` is always valid. In a flat space `Pose.Local` is a pure translation with entries below one tile side |
| 4 | Every kernel type is a blittable value type; `TileId` works as a dictionary key; `ContentSeed` will not change without notice |
| 5 | Every material that never sets `_WarpMode`, `_Anchor` or a gate renders unwarped and unclipped. Unity's own culling and bounds are valid for every renderer except those of a curved space |
| 6 | Bump Guard needs nothing from the kernel, and the kernel needs nothing from Bump Guard |

Do not assume:

| # | Do not assume | Because |
|---|---|---|
| 1 | That `MetresPerUnit` is 1 | It is L (1.4 to 3.0 m) or R in curved spaces. Multiply every time |
| 2 | That κ, `Q`, `Graph` or `MetresPerUnit` are constant for the session | A curved space configures the kernel on `Enter`, the dial changes them while the walker stands still, and SpaceLab restores the flat kernel after `Exit`. Read them from the kernel each frame; do not cache the graph |
| 3 | That `Pose.Local` has no rotation | In curved space it carries the holonomy: 90° after one lap of the lap square |
| 4 | That the walker's position can be turned into a room coordinate | Only at κ = 0, and only until the first rejected jump. Flat spaces take the head position from the pose (README F4), never from the kernel. Rule 7 |
| 5 | That what is drawn is where things are | Curved Walk and the Small Planet are registered only until the first step (plan §4.9). Bump Guard, in room coordinates, is the only protection |
| 6 | That the edge leading back is (k + 2) mod 4, or that tile frames are parallel to the room | True only on the flat grid. Use `BackEdge` and `EdgeStep` |
| 7 | That the six words of a `TileId` are a grid cell | `FlatTileGraph.TryGetCell` returns false for a {4,5} id |
| 8 | That `Candidates` is the list of drawn tiles, or that it is short | It is the candidate list before the draw rule; 277 entries at ρ = 3 and about 500 on the high tier. Telemetry `visibleTiles` is the count the generated world's renderer drew (S2 at q = 5), 0 elsewhere |
| 9 | That a renderer's `Transform`, bounds or frustum culling mean anything under the warp | In the curved modes the transform is ignored, bounds are huge, and the CPU draw rule is the only culling |
| 10 | That a vertex ends up at `Transform` × object position | Under the warp it ends up at its true distance and bearing from the head. Never place, pick or collide by transform in a curved space |
| 11 | That a mesh with long edges will look right | Anything shown in a curved space needs edges of at most 0.25 m (lane A §10.2). The Home shell's 0.25 m grid (lane C) and the asset validator (lane D) already give such meshes |
| 12 | That fog and draw distances are fixed in metres | They are ρ · L |
| 13 | That a frame always moves the walker | A rejected step moves nothing. The geometry ignores what Bump Guard still sees |
| 14 | That the Station's kernel follows a curved walker | `SpectatorLoop` replays `State.tileId` and `xform` through `Reset` only where the Spectator's kernel can take them; whether the flat twin draws the Poincaré disc of S2 and S3 is lane D's wiring (lane D §5.9) |

What this page needs from the other pages:

| From | Needed |
|---|---|
| Lane A | `FrameLoopCore.RunUpdate` calls `IWalkerKernel.Step(headFloorMetres, trackingNormal, time)` exactly once per frame at step 4 (§4.9); `LookUploadStep` sets `_HeadPosWS` and `_HypL` at step 6 from the same frame's `HeadPosition` and the kernel's `MetresPerUnit` |
| Lane A | The single `UnityPerMaterial` block holds `float4x4 _Anchor`, `float _WarpMode` and `float4 _Gate0` to `_Gate3`; `AtlasShaderIDs` lists them and the two globals; the material pool initialises them to identity, 0 and zero |
| Lane A | `Atlas/Surface` calls `AtlasWarp` in the vertex stage and `AtlasGatesClip` then `AtlasWarpClip` in the fragment stage, for every material |
| Lane A | SpaceLab restores the flat kernel after a curved space and keeps the pose for S2 to S3 (§7.2) |
| Lane C | The geometry pose stream gives the head's floor point in the room frame after the pose filter, and the tracking state; the RoomModel exposes the lap-square side s and `Curvature` (L5, L6, R3, floors); `IRoomContent` is the room, drawn once (§7.2) |
| Lane D | `State.tileId` and `State.xform` as in §7.3; tile ids in JSON are the `ToString()` text; `State.p[0]` = `(float)MetresPerUnit` |

---

## 11. Not in this contract

- The tile-crossing test and its hysteresis, the curvature ramp's internals, level-of-detail selection, the world generator and WorldLint. These are lane B's internal work (`P1-B02`, `P1-B06`, `P1-B07`), governed by their task rows.
- `ISpaceModule`, the per-material block's other fields, the layer and stencil table and the frame loop: lane A's page.
- The RoomModel schema: lane C's page.
- The `State` layout and the parameter registry: lane D's pages.

Superseded and never to be built (Appendix D.1, D.2): Klein-chart rendering of walked space; offline tile lookup tables; global coordinates; ⟨p, p⟩ = −R² in code; MaterialPropertyBlocks; fog at 5–8 units; a placeholder that quietly treats curved space as flat.
