# Lane B · Kernel contract

Task `P1-X06`, frozen at gate G0 (Master Plan §6.4, §6.5, Appendix A). Owner: lane B.

This page does two jobs.

1. It fixes the binding API of the full geometry kernel (flat, hyperbolic, spherical).
2. It specifies the **flat stub** that ships now, so that lanes A, C and D can build and run end to end before
   lane B's curved code exists.

| Part | In this build | Arrives with lane B |
|---|---|---|
| `Iso2`, `KernelMaps`, `Curvature` | κ = 0 complete and final | κ = −1, +1 (`P1-B01`) |
| `TileId` | Six words, equality, hash, `ContentSeed`: final | `Step` for {4,5} (`P1-B04`) |
| `WalkerPose`, `VisibleTile`, `ITileGraph` | Final | unchanged |
| Tile graph | `FlatTileGraph`, a {4,4} square grid (the fake tile provider of `P1-X02`) | {4,5}, {4,6}, {4,3} graphs (`P1-B04`, `P1-B08`) |
| Kernel step hook | `IWalkerKernel` final; `FlatWalkerKernel` | Holonomy rig behind the same interface (`P1-B02`) |
| `AtlasWarp.hlsl` | Mode none (identity) | Hyperbolic and spherical branches (`P1-B03`) |
| `AtlasGates.hlsl` | No clip (identity) | Gate clip (`P1-B10`) |
| Tile meshes, tile renderer, world generator, WorldLint, S2, S3, S4, dial | Not built | `P1-B05` to `P1-B12` |

Rules for every file named here: namespace `Atlas.Kernel`, assembly `Atlas.Kernel`, only `System` and
`Unity.Mathematics` (README F1, F2). No `UnityEngine` type, no static mutable state, no allocation on the frame
path. Angles are radians. All arithmetic is `double`; the only cast to `float` is in `Iso2.ToFloat3x3` and
`Iso2.ToFloat4x4`.

---

## 1 · Conventions

### 1.1 Model and metric

| Geometry | κ | Model surface, points p = (x, z, w) | Form | Base point o |
|---|---|---|---|---|
| Hyperbolic | −1 | Unit hyperboloid: x² + z² − w² = −1, w > 0 | η = diag(1, 1, −1) | (0, 0, 1) |
| Flat | 0 | The plane w = 1 (homogeneous 2D coordinates) | none: rigid motions of the plane | (0, 0, 1) |
| Spherical | +1 | Unit sphere: x² + z² + w² = 1 | ordinary dot product | (0, 0, 1) |

One statement covers all three: the surface is κ·(x² + z²) + w² = 1 (Appendix A.1, A.2).

**κ is a sign, never a strength** (decided here). The only legal values are exactly `-1.0`, `0.0` and `+1.0`
(`Curvature.Hyperbolic`, `.Flat`, `.Spherical`). Any other value is an `ArgumentOutOfRangeException`. How strongly
the space curves is carried by L alone: K = κ / L².

### 1.2 Units and the boundary

- Inside the kernel every length is in **kernel units**: metres divided by L. L is the hyperbolic curvature
  radius; on the sphere the same slot holds R. The C# name is `MetresPerUnit`; the shader name is `_HypL`.
- Metres exist only at the boundary. The boundary is exactly three places:

| Place | Direction | Conversion |
|---|---|---|
| `IWalkerKernel.Step`, `IWalkerKernel.ResetToFloor` | metres in | divide by `MetresPerUnit` |
| `AtlasWarp` in the vertex shader | metres in and out | `positionOS.xz / _HypL`, result `* _HypL` |
| Any reader of `WalkerPose.Local`, `ITileGraph.CentreSpacing`, `CircumRadius`, ρ | units out | multiply by `IWalkerKernel.MetresPerUnit` |

- Kernel units: every `Iso2` entry, `Boost`'s `step`, `KernelMaps`, `CentreSpacing`, `CircumRadius`, ρ
  (`CullRho`, the `rho` of `VisibleSet`), `CrossHysteresis`.
- Metres: `KernelConfig.TileSideMetres`, `Step`'s `headFloorMetres`, `ResetToFloor`'s `floorMetres`,
  `KernelStep.AppliedMetres`, `KernelConstants.MaxStepMetres`, everything in the shader's object and world space.
- **At κ = 0 any positive L describes the same geometry.** `SpaceLab.SetRoom` configures the kernel with
  `KernelConfig.Flat(side)`, which sets `MetresPerUnit = KernelConstants.FlatMetresPerUnit = 1.0` (decided
  here), so in this build one kernel unit is one metre. Nobody may rely on that:
  always multiply by `MetresPerUnit`. The test `FlatWalker_UnitFree` runs the kernel at L = 1.885 to keep callers
  honest.
- Height (Unity Y) never enters the kernel. The curved spaces are H² × E and S² × E: the floor is curved, height
  is ordinary (§4.6).

Values per dial position. s is the lap-square side, RoomModel `lapSquare.side`; the radii are RoomModel
`curvature.r3`, `curvature.l5` and `curvature.l6`, raised to the comfort floors `curvature.floorR` and
`curvature.floorL` where those apply (§4.3, §4.4; JSON names from `roommodel.schema.json`, C# names on lane C's
page):

| Dial q | κ | `MetresPerUnit` | Centre spacing 2r (units) | Circumradius r_c (units) |
|---|---|---|---|---|
| 3 | +1 | R = 2s / π (`curvature.r3`) | π/2 = 1.5707963267948966 | 0.955316618124509 |
| 4 | 0 | 1.0 (decided here) | s / L | spacing / √2 |
| 5 | −1 | L = s / 1.0612750619050357 (`curvature.l5`) | 1.0612750619050357 (cosh = φ) | 0.8424820814620078 |
| 6 | −1 | L = s / 1.3169578969248166 (`curvature.l6`) | 1.3169578969248166 (cosh = 2) | 1.1462158347805893 |

Only the q = 4 row is live in this build.

### 1.3 Floor to kernel plane, and the sense of rotation

Unity world space is the room frame (README F4): left-handed, Y up, metres, origin at the centre of the free
rectangle on the floor.

| Room frame (Unity) | Kernel |
|---|---|
| X, metres (along the `Wf` side) | x = X / L |
| Z, metres (along the `Df` side) | z = Z / L |
| Y, metres | not represented |
| Floor point `float2 (x, z)` | `double2` with `.x` = x and `.y` = z |
| Model point (x, z, w) | `double3` with `.x` = x, `.y` = z, `.z` = w |
| Yaw ψ about +Y (Unity: positive is clockwise seen from above, a right turn) | `Rot` angle θ = −ψ, in radians |

**Naming trap.** `double2.y` holds the floor **z**. `double3.y` holds z and `double3.z` holds **w**. There is no
Unity Y anywhere in the kernel.

**Sense of rotation.** Seen from above with +x to the right and +z up the page, `Rot(θ)` with θ > 0 turns
**anticlockwise**: +x goes towards +z. For a walker this is a **left turn**, the turn of the 3-4-5 walk
(g = Boost(s/L) · Rot(90°), Appendix A.4). A Unity heading with yaw ψ points along (sin ψ, cos ψ) in (x, z); its
kernel angle from +x is 90° − ψ.

Every `Iso2` is orientation-preserving (determinant +1). Mirror images (the Klein bottle's flipped rows) are not
`Iso2` values; lane A's Glued space keeps its own transforms for them.

### 1.4 Matrix layout and multiplication order

- An `Iso2` is a 3 × 3 matrix stored **row-major**: `Mrc` is row r, column c. Index 0 is x, 1 is z, 2 is w.
- Points are **column vectors**. `m.Apply(p)` is M · p.
- `a * b` applies **b first, then a**: `(a * b).Apply(p) == a.Apply(b.Apply(p))`.
- Moving in the walker's own frame is a **right** multiplication: U ← U · Boost(Δ / L).
- Changing the frame U is expressed in is a **left** multiplication: U ← G⁻¹ · U.
- On the wire (`State.xform`, Appendix B.1) the nine doubles are row-major: M00, M01, M02, M10, M11, M12, M20,
  M21, M22. `Iso2.CopyTo` and `Iso2.FromRowMajor` use that order.
- For upload, `ToFloat4x4()` puts `Mrc` at row r, column c of a `float4x4`, with row 3 and column 3 taken from
  the identity. The shader reads it as `mul((float3x3)_Anchor, p)` with p = (x, z, w).

### 1.5 Frames

| Symbol (§4.6) | C# | Maps | Notes |
|---|---|---|---|
| U | `WalkerPose.Local` | walker frame → frame of the walker's current tile | `U.Position` is the walker in tile coordinates |
| G[k] | `ITileGraph.EdgeStep(tile, k)` | frame of the neighbour across edge k → frame of `tile` | Re-root: U ← G[k]⁻¹ · U |
| F[t] | `VisibleTile.Chain` | frame of tile t → frame of the centre tile | Product of edge steps, rebuilt from scratch on every re-root |
| anchor[t] | `(U.Inverse() * F[t]).ToFloat4x4()` | frame of tile t → walker frame | Product in double, cast last. Per-material `_Anchor` |

- **Walker frame.** Origin at the point of the floor under the head. Axes parallel to the room's +X and +Z at
  the last `Reset`, then carried along the walked path without turning. **Head yaw is not in U.** The cameras
  apply the head's rotation as usual. In a curved space the rotation part of U is the holonomy: how far the
  world has turned against the room. At κ = 0 it stays the identity.
- **Tile frame.** Origin at the tile centre. Its +x axis passes through the midpoint of edge 0.
- **Tile zero** is `TileId.Origin`. Its frame is the room frame divided by L: tile zero is centred on the
  RoomModel anchor (§4.6, "the room is tile zero").

### 1.6 Edges and corners of a tile

Every tile has four sides. In its own frame:

```text
              +z
            edge 1
   corner 1 ┌──────┐ corner 0
     edge 2 │  o   │ edge 0      → +x
   corner 2 └──────┘ corner 3
            edge 3
```

- Edge k is the edge whose midpoint lies at angle k · 90° from the tile centre (R3 §A3).
- Corner k lies at angle 45° + k · 90°, between edge k and edge (k + 1) mod 4 (decided here).
- Which edge of the neighbour leads back is `ITileGraph.BackEdge(tile, k)`. On the flat grid it is (k + 2) mod 4.
  In {4,5} it is not. **Never compute it; always ask the graph.**
- Walking round a corner post turning left: after entering a tile through its edge b, leave through edge
  (b + 3) mod 4. The walk returns to the first tile after exactly `Q` steps (4 flat, 5 in {4,5}).

---

## 2 · Types

| File (under `/workspace/ATLAS/Assets/Atlas/Kernel/`) | Contains | Owner |
|---|---|---|
| `Curvature.cs` | `Curvature` | B |
| `KernelConstants.cs` | `KernelConstants` | B |
| `Iso2.cs` | `Iso2` | B |
| `KernelMaps.cs` | `KernelMaps` (exp, log, distance) | B |
| `TileId.cs` | `TileId` | B |
| `WalkerPose.cs` | `WalkerPose` | B |
| `VisibleTile.cs` | `VisibleTile` | B |
| `ITileGraph.cs` | `ITileGraph` | B |
| `FlatTileGraph.cs` | `FlatTileGraph` | B |
| `IWalkerKernel.cs` | `IWalkerKernel`, `KernelConfig`, `KernelStep` | B |
| `FlatWalkerKernel.cs` | `FlatWalkerKernel` | B |

Tests: `/workspace/ATLAS/Assets/Atlas/Tests/Kernel/` (assembly `Atlas.Kernel.Tests`, §7).
Shader includes: `/workspace/ATLAS/Assets/Atlas/Rendering/Shaders/Resources/AtlasWarp.hlsl` and `AtlasGates.hlsl`
(§5, §6). Both exist as the identity implementations of this page and are kept by lane A until lane B starts
(`docs/scene-ownership.md`).
Also in the assembly `Atlas.Kernel`, owned by lane A and described on lane A's page: `Kernel/Mesh4D/` (`Mesh4D`,
`Polytopes4D`, `Rotation4D`, `Projector4D`, `Slicer4D`, `SliceJob`; namespace `Atlas.Kernel.FourD`) and
`Kernel/Mesh/` (`AtlasVertex`, `AtlasShading`; namespace `Atlas.Kernel`). The rules above about `System` and
`Unity.Mathematics` only hold for the eleven files of the table; the assembly itself also references Burst and
Collections and allows unsafe code for the slicer.
`Kernel/AssemblyInfo.cs` makes internals visible to `Atlas.Kernel.Tests`.

Names and signatures below are binding. Bodies are free.

### 2.1 `Curvature` and `KernelConstants`

```csharp
// File: ATLAS/Assets/Atlas/Kernel/Curvature.cs
namespace Atlas.Kernel
{
    /// <summary>The three legal values of kappa (Master Plan Appendix A.1).</summary>
    public static class Curvature
    {
        public const double Hyperbolic = -1.0;
        public const double Flat       =  0.0;
        public const double Spherical  = +1.0;

        /// <summary>True when this build implements the geometry. Flat only until lane B lands.</summary>
        public static bool IsSupported(double kappa);

        /// <summary>ArgumentOutOfRangeException unless kappa is exactly -1, 0 or +1;
        /// then NotSupportedException unless IsSupported(kappa).</summary>
        public static void Require(double kappa);

        /// <summary>Curvature of the {4,q} tiling: 3 gives +1, 4 gives 0, 5 and 6 give -1.
        /// Any other q: ArgumentOutOfRangeException.</summary>
        public static double OfQ(int q);
    }
}
```

```csharp
// File: ATLAS/Assets/Atlas/Kernel/KernelConstants.cs
namespace Atlas.Kernel
{
    public static class KernelConstants
    {
        /// <summary>A floor displacement longer than this in one frame is ignored by the geometry. Metres (D8, section 4.6).</summary>
        public const double MaxStepMetres = 0.2;
        /// <summary>The walker changes tile only once this far past an edge. Kernel units (R3 section A5).</summary>
        public const double CrossHysteresis = 0.02;
        /// <summary>Upper bound on re-roots inside one Step (R3 section A5).</summary>
        public const int MaxCrossingsPerStep = 4;
        /// <summary>Default fog and cull radius rho. Kernel units (D13, section 8.5).</summary>
        public const double DefaultCullRho = 3.0;
        /// <summary>Tile side used before a RoomModel is active. Metres (decided here: the lap square of the 3 x 3 m sample room).</summary>
        public const double DefaultFlatTileSideMetres = 2.0;
        /// <summary>Metres per kernel unit at kappa = 0 (decided here).</summary>
        public const double FlatMetresPerUnit = 1.0;
        /// <summary>Inclusive tolerance on the radius test of VisibleSet. Kernel units.</summary>
        public const double RadiusEpsilon = 1e-9;
    }
}
```

### 2.2 `Iso2`

```csharp
// File: ATLAS/Assets/Atlas/Kernel/Iso2.cs
using System;
using Unity.Mathematics;

namespace Atlas.Kernel
{
    /// <summary>
    /// An orientation-preserving isometry of the kernel plane: a 3 x 3 double matrix acting on
    /// model points (x, z, w), tagged with the curvature it belongs to (Master Plan section 6.5, Appendix A.2).
    /// </summary>
    public readonly struct Iso2 : IEquatable<Iso2>
    {
        // Row-major. Mrc = row r, column c. Index 0 = x, 1 = z, 2 = w.
        public readonly double M00, M01, M02;
        public readonly double M10, M11, M12;
        public readonly double M20, M21, M22;

        /// <summary>-1 hyperbolic, 0 flat, +1 spherical. Fixed at construction.</summary>
        public readonly double Kappa;

        /// <summary>Raw entries. Checks kappa with Curvature.Require; does not check the entries.</summary>
        public Iso2(double m00, double m01, double m02,
                    double m10, double m11, double m12,
                    double m20, double m21, double m22, double kappa);

        /// <summary>The flat identity (Kappa = 0).</summary>
        public static Iso2 Identity { get; }
        /// <summary>The identity tagged with the given curvature.</summary>
        public static Iso2 IdentityFor(double kappa);

        /// <summary>Move by a tangent vector at the base point. step is in kernel units
        /// (already divided by L): step.x along +x, step.y along +z.</summary>
        public static Iso2 Boost(double2 step, double kappa);

        /// <summary>Rotation about the base point by angle radians, anticlockwise seen from above
        /// (+x towards +z: a left turn). Flat-tagged (Kappa = 0).</summary>
        public static Iso2 Rot(double angle);
        /// <summary>The same rotation, tagged with the given curvature.</summary>
        public static Iso2 Rot(double angle, double kappa);

        /// <summary>The inverse isometry. Same Kappa.</summary>
        public Iso2 Inverse();
        /// <summary>The nearest group element, by Gram-Schmidt. Same Kappa.</summary>
        public Iso2 Orthonormalised();

        /// <summary>Matrix product a·b: b is applied first. InvalidOperationException when the tags differ.</summary>
        public static Iso2 operator *(Iso2 a, Iso2 b);

        /// <summary>M · p for a model point p = (x, z, w).</summary>
        public double3 Apply(double3 p);
        /// <summary>The image of the base point (0, 0, 1): the third column (M02, M12, M22).</summary>
        public double3 Position { get; }
        /// <summary>Largest absolute entry of the group residual; 0 for an exact group element.</summary>
        public double Defect();

        public double3x3 ToDouble3x3();
        /// <summary>The only double-to-float cast in the kernel. Use it last.</summary>
        public float3x3 ToFloat3x3();
        /// <summary>Upload form for the per-material _Anchor: Mrc at row r, column c; row 3 and column 3 from the identity.</summary>
        public float4x4 ToFloat4x4();

        /// <summary>Writes M00, M01, M02, M10, ... M22 into the first nine elements.</summary>
        public void CopyTo(Span<double> rowMajor9);
        public static Iso2 FromRowMajor(ReadOnlySpan<double> rowMajor9, double kappa);

        public bool Equals(Iso2 other);            // all ten doubles equal
        public override bool Equals(object obj);
        public override int GetHashCode();
        public override string ToString();
    }
}
```

**Why a κ tag (decided here).** §6.5 gives `Inverse()` and `Orthonormalised()` no curvature argument, yet both
depend on the geometry (η·Mᵀ·η in H², transpose on S², an affine inverse in the plane). The struct therefore
stores κ. The §6.5 signatures stay exactly as printed. Consequences:

- `Iso2.Identity` and `Iso2.Rot(angle)` are **flat-tagged**. Curved code uses `IdentityFor(kappa)` and
  `Rot(angle, kappa)`.
- `a * b` with `a.Kappa != b.Kappa` throws `InvalidOperationException`. Geometries never mix silently.
- κ is not on the wire. A receiver of `State.xform` derives it from `spaceId`: S2 and S3 → −1, S4 → +1,
  everything else → 0.
- `default(Iso2)` is the zero matrix, not the identity. Never use it.
- Size: ten doubles, 80 bytes, blittable.
- `CopyTo` and `FromRowMajor` throw `ArgumentException` when the span holds fewer than nine elements.
  `FromRowMajor` checks κ first.
- `ToString()` is `[[M00, M01, M02], [M10, M11, M12], [M20, M21, M22]] kappa=K`, round-trip format, invariant
  culture. It is for logs only; nothing parses it.

**Semantics per member.**

| Member | κ = 0 (implemented now) | κ = −1 and +1 (lane B; reference from Appendix A.2) |
|---|---|---|
| `Boost(Δ, κ)` | `[[1, 0, Δx], [0, 1, Δz], [0, 0, 1]]`. Exact | With u = Δ/‖Δ‖ and d = ‖Δ‖, rows 0 and 1 are `[1+(c−1)ux², (c−1)ux·uz, s·ux]` and `[(c−1)ux·uz, 1+(c−1)uz², s·uz]`. κ = −1: c = cosh d, s = sinh d, row 2 = `[s·ux, s·uz, c]`. κ = +1: c = cos d, s = sin d, row 2 = `[−s·ux, −s·uz, c]`. ‖Δ‖ = 0 gives the identity |
| `Rot(θ)` | `[[cos θ, −sin θ, 0], [sin θ, cos θ, 0], [0, 0, 1]]` | The same matrix |
| `a * b` | Ordinary 3 × 3 product. The last row stays exactly (0, 0, 1) | Ordinary 3 × 3 product |
| `Inverse()` | With A the upper-left 2 × 2 block and t = (M02, M12): `[[Aᵀ, −Aᵀt], [0, 0, 1]]` | κ = −1: η·Mᵀ·η. κ = +1: Mᵀ |
| `Orthonormalised()` | n = sqrt(M00² + M10²), c = M00/n, s = M10/n; result `[[c, −s, M02], [s, c, M12], [0, 0, 1]]`. The translation is untouched bit for bit. `InvalidOperationException` when n < 1e-12 or not finite | Gram–Schmidt with respect to η (κ = −1) or the ordinary dot product (κ = +1) |
| `Apply(p)` | M · p. For a floor point use p = (x, z, 1) | M · p |
| `Defect()` | max of \|c0·c0 − 1\|, \|c0·c1\|, \|c1·c1 − 1\|, \|M20\|, \|M21\|, \|M22 − 1\|, with c0 = (M00, M10), c1 = (M01, M11) | Largest absolute entry of Mᵀ·J·M − J, J = diag(1, 1, −1) or the identity |

### 2.3 `KernelMaps`

```csharp
// File: ATLAS/Assets/Atlas/Kernel/KernelMaps.cs
using Unity.Mathematics;

namespace Atlas.Kernel
{
    /// <summary>Exponential and logarithm maps at the base point, and distance (Appendix A.2). Kernel units.</summary>
    public static class KernelMaps
    {
        /// <summary>The base point o = (0, 0, 1) in every geometry.</summary>
        public static readonly double3 BasePoint = new double3(0.0, 0.0, 1.0);

        /// <summary>Tangent vector at o (direction and distance) to the point reached.</summary>
        public static double3 Exp(double2 t, double kappa);
        /// <summary>Point to the tangent vector at o that reaches it. Inverse of Exp.</summary>
        public static double2 Log(double3 p, double kappa);
        /// <summary>Geodesic distance between two model points.</summary>
        public static double Distance(double3 p, double3 q, double kappa);
    }
}
```

| Member | κ = 0 (now) | κ = −1 (lane B) | κ = +1 (lane B) |
|---|---|---|---|
| `Exp(t)` | (t.x, t.y, 1) | (sinh r · t/r, cosh r), r = ‖t‖ | (sin r · t/r, cos r) |
| `Log(p)` | (p.x, p.y) | asinh(‖p_xz‖) · p_xz/‖p_xz‖ | acos(p_w) · p_xz/‖p_xz‖ |
| `Distance(p, q)` | hypot(p.x − q.x, p.y − q.y) | acosh(−⟨p, q⟩_η) | acos(p · q) |

The walker's distance from the centre of their tile, in metres:
`math.length(KernelMaps.Log(pose.Local.Position, kernel.Kappa)) * kernel.MetresPerUnit`.

### 2.4 `TileId`

```csharp
// File: ATLAS/Assets/Atlas/Kernel/TileId.cs
using System;

namespace Atlas.Kernel
{
    /// <summary>
    /// The exact identity of one tile: six 64-bit integers. In {4,5} they are X, Y, T in Z[phi]
    /// (X = Xu + Xv·phi and so on) and the tile centre is (sqrt(phi)·X, sqrt(phi)·Y, T)
    /// (Master Plan section 6.5, Appendix A.5). Other tilings define their own use of the six words.
    /// </summary>
    public readonly struct TileId : IEquatable<TileId>
    {
        public readonly long Xu, Xv, Yu, Yv, Tu, Tv;

        public TileId(long xu, long xv, long yu, long yv, long tu, long tv);

        /// <summary>Tile zero, the home tile, in every tiling: (0, 0, 0, 0, 1, 0), that is X = 0, Y = 0, T = 1.</summary>
        public static TileId Origin { get; }
        /// <summary>True for default(TileId), all six words zero: "no tile". It is the value Appendix B.1 calls 0.</summary>
        public bool IsNone { get; }

        /// <summary>Word 0..5 in wire order: Xu, Xv, Yu, Yv, Tu, Tv. Other index: ArgumentOutOfRangeException.</summary>
        public long this[int word] { get; }
        public void CopyTo(Span<long> words6);
        public static TileId FromWords(ReadOnlySpan<long> words6);

        /// <summary>The {4,5} neighbour across edge 0..3 of the tile's canonical frame; additions only.
        /// NotSupportedException in this build (see section 3.1). Other lanes call ITileGraph.Neighbour.</summary>
        public TileId Step(int edge);

        /// <summary>Deterministic content seed: the same on every device, runtime and exploration order (section 3.4).</summary>
        public ulong ContentSeed(uint worldSeed);

        public bool Equals(TileId other);          // all six words equal
        public override bool Equals(object obj);
        public override int GetHashCode();         // from ContentSeed(0); see section 3.4
        public static bool operator ==(TileId a, TileId b);
        public static bool operator !=(TileId a, TileId b);

        /// <summary>The six words in wire order, decimal, comma-separated, invariant culture: "0,0,0,0,1,0".</summary>
        public override string ToString();
        public static bool TryParse(string text, out TileId id);
    }
}
```

- **Wire order** (`State.tileId`, 6 × i64, Appendix B.1): `Xu, Xv, Yu, Yv, Tu, Tv` = elements 0 to 5.
- **In JSON** (events, logs) a tile id is the `ToString()` text, never six numbers: a 64-bit integer does not
  survive a JavaScript number.
- `TileId` is an **opaque id plus a seed**. Which words mean what depends on the tiling, and the tiling is the
  `ITileGraph`. `TileId.Origin` is the one value every tiling shares.
- A space is free to put its own value into `State.tileId` as Appendix B.1 lists (S1: cell (i, j, k, 0, 0, 0);
  S5: room id; S0 and S6: zero). Those are built with the six-word constructor and are not kernel tiles.
- No ordering is defined. Do not sort tile ids.
- Size: 48 bytes, blittable.
- `CopyTo` and `FromWords` throw `ArgumentException` when the span holds fewer than six elements.
- `TryParse` accepts exactly six comma-separated decimal integers with an optional leading sign and no white
  space. For anything else, including `null` and the empty string, it returns `false` and `id` is "no tile".

### 2.5 `WalkerPose`

```csharp
// File: ATLAS/Assets/Atlas/Kernel/WalkerPose.cs
namespace Atlas.Kernel
{
    /// <summary>Where the walker is: a tile and a frame inside it. Never a global coordinate (rule 7).</summary>
    public struct WalkerPose
    {
        /// <summary>The tile the walker stands on.</summary>
        public TileId Tile;
        /// <summary>U: maps the walker frame into the frame of Tile. Kernel units.</summary>
        public Iso2 Local;

        /// <summary>At the centre of tile zero, unturned, flat-tagged: (TileId.Origin, Iso2.Identity).</summary>
        public static WalkerPose Start { get; }
        /// <summary>False for default(WalkerPose): the tile is not "none" and Local.Defect() is below 1e-6.</summary>
        public bool IsValid { get; }
    }
}
```

`default(WalkerPose)` is **not** a valid pose (no tile, zero matrix). Start from `WalkerPose.Start` or from
`IWalkerKernel.Pose`.

### 2.6 `VisibleTile`

```csharp
// File: ATLAS/Assets/Atlas/Kernel/VisibleTile.cs
namespace Atlas.Kernel
{
    /// <summary>One entry of a visible-set query (Master Plan sections 4.6 and 6.5).</summary>
    public struct VisibleTile
    {
        /// <summary>The tile.</summary>
        public TileId Tile;
        /// <summary>F[t]: maps this tile's frame into the frame of the query's centre tile. Identity for the centre tile.</summary>
        public Iso2 Chain;
        /// <summary>Number of edge steps from the centre tile. 0 for the centre tile.</summary>
        public int GraphDistance;
    }
}
```

### 2.7 `ITileGraph`

```csharp
// File: ATLAS/Assets/Atlas/Kernel/ITileGraph.cs
using System.Collections.Generic;

namespace Atlas.Kernel
{
    /// <summary>A regular tiling by four-sided tiles, addressed by exact tile id (Master Plan section 6.5).</summary>
    public interface ITileGraph
    {
        /// <summary>Tiles per corner: 3, 4, 5 or 6.</summary>
        int Q { get; }
        /// <summary>Curvature of the tiling: Curvature.OfQ(Q).</summary>
        double Kappa { get; }
        /// <summary>Distance between the centres of neighbouring tiles, 2r. Kernel units.</summary>
        double CentreSpacing { get; }
        /// <summary>Distance from a tile's centre to its corners, r_c. Kernel units.</summary>
        double CircumRadius { get; }

        /// <summary>The tile across edge 0..3 of tile.</summary>
        TileId Neighbour(TileId tile, int edge);
        /// <summary>The edge of Neighbour(tile, edge) that leads back to tile.</summary>
        int BackEdge(TileId tile, int edge);
        /// <summary>G[edge]: maps the frame of Neighbour(tile, edge) into the frame of tile.</summary>
        Iso2 EdgeStep(TileId tile, int edge);

        /// <summary>
        /// Clears into, then adds every tile whose centre lies within rho (kernel units, inclusive)
        /// of the centre of the given tile: id, step chain F[t], graph distance.
        /// </summary>
        void VisibleSet(TileId centre, double rho, List<VisibleTile> into);
    }
}
```

`Neighbour`, `BackEdge`, `EdgeStep`, `Kappa`, `CentreSpacing` and `CircumRadius` are added to the two members of
§6.5 (decided here). §4.6 already calls `Neighbour(tile, i)` and `G[i]`, and the test `TileId_Unique` names
`Neighbour(Neighbour(t, k), back)`. Without them nobody outside lane B could step the graph.

Rules every implementation obeys:

| Rule | Detail |
|---|---|
| Round trip | `Neighbour(Neighbour(t, k), BackEdge(t, k)) == t` for every tile and k |
| Step consistency | `EdgeStep(t, k).Position` is at distance `CentreSpacing` from the base point. `EdgeStep(n, BackEdge(t, k))` equals `EdgeStep(t, k).Inverse()` to 1e-12, with n = `Neighbour(t, k)` |
| `VisibleSet` radius | `rho` is a centre-to-centre distance. A tile is included when its distance is ≤ `rho + KernelConstants.RadiusEpsilon`. No padding is added: the caller pads (§4.3) |
| `VisibleSet` order | Non-decreasing `GraphDistance`; the centre tile first with `Chain` = identity. Within one distance the order is fixed for a given graph and input, so the phone and the Station produce the same list |
| `VisibleSet` chains | `Chain` is computed afresh on every call. Nothing accumulates between calls |
| Allocation | `VisibleSet` allocates nothing once `into` has the capacity. Callers pre-size the list to 1,024 (the high tier needs about 500; 373 centres lie within ρ = 5, §8.5) |
| Bad input | `edge` outside 0..3: `ArgumentOutOfRangeException`. A tile that is not of this tiling: `ArgumentException`. `rho` negative or not finite: `ArgumentOutOfRangeException`. `into` null: `ArgumentNullException` |
| Threading | Main thread only |

---

## 3 · The flat stub

### 3.1 What happens for κ ≠ 0: it throws

**Decision: every curved request throws `NotSupportedException`** (decided here). No placeholder.

Reason. A placeholder that quietly treats curved space as flat would let a curved space appear to run, let a
closure test pass for the wrong reason, and hide a missing lane behind a picture that looks right. A throw is
found by the first test or the first Editor run. The frame path is safe because `FlatWalkerKernel` only ever
passes κ = 0; nothing reachable from `AtlasFrameLoop` can throw this.

| Call | In this build |
|---|---|
| Any κ that is not exactly −1, 0 or +1 | `ArgumentOutOfRangeException` (permanent) |
| `Iso2` constructor, `IdentityFor`, `Boost`, `Rot(angle, kappa)`, `FromRowMajor` with κ = ±1 | `NotSupportedException` |
| `KernelMaps.Exp`, `Log`, `Distance` with κ = ±1 | `NotSupportedException` |
| `Curvature.Require(±1)` | `NotSupportedException`; `Curvature.IsSupported(±1)` is `false` |
| `TileId.Step(edge)` | `NotSupportedException`: it is the {4,5} step |
| `IWalkerKernel.Configure` with `Q` = 3, 5 or 6 | `NotSupportedException` |
| `IWalkerKernel.Configure` with any other `Q` than 3, 4, 5, 6 | `ArgumentOutOfRangeException` from `Curvature.OfQ` (permanent) |
| `IWalkerKernel.Reset` with a pose whose `Local.Kappa` ≠ 0 | Cannot occur: no such `Iso2` can be built |

So in this build **no `Iso2` with `Kappa != 0` can exist**. The tag check in `operator *` is written now but
cannot fire; its test arrives with `P1-B01`. Message text (fixed, so logs read the same everywhere):

```text
Atlas.Kernel: curvature kappa = {value} is not available in this build. Only the flat kernel (kappa = 0)
is implemented; hyperbolic and spherical geometry arrive with lane B (P1-B01).
```

For `TileId.Step` and `Configure`: the same sentence with "the {4,5} tile step" or "q = {value}" in place of
"curvature kappa = {value}", and `P1-B04` for the task.

### 3.2 Member by member

| Member | Now |
|---|---|
| `Curvature`, `KernelConstants` | Final |
| `Iso2`: constructor, `Identity`, `IdentityFor(0)`, `Boost(_, 0)`, `Rot`, `Inverse`, `Orthonormalised`, `*`, `Apply`, `Position`, `Defect`, conversions, `CopyTo`, `FromRowMajor(_, 0)`, equality | Fully correct for κ = 0, as the table in §2.2 |
| `KernelMaps` at κ = 0 | Fully correct |
| `TileId`: constructor, `Origin`, `IsNone`, indexer, `CopyTo`, `FromWords`, equality, `GetHashCode`, `ToString`, `TryParse`, `ContentSeed` | **Final for every tiling** |
| `TileId.Step` | Throws (§3.1) |
| `WalkerPose`, `VisibleTile`, `ITileGraph` | Final |
| `FlatTileGraph` | Complete (§3.3) |
| `IWalkerKernel`, `KernelConfig`, `KernelStep` | Final |
| `FlatWalkerKernel` | Complete for `Q` = 4 (§4) |

### 3.3 `FlatTileGraph`: the fake tile provider

The ordinary square grid, {4,4}: four tiles at every corner, every tile frame parallel to the frame of tile
zero. It is the stub named "fake tile provider" in `P1-X02`, and it stays in the repository afterwards as the
q = 4 position of the dial.

```csharp
// File: ATLAS/Assets/Atlas/Kernel/FlatTileGraph.cs
using System.Collections.Generic;

namespace Atlas.Kernel
{
    /// <summary>The flat {4,4} square grid: four tiles per corner, all tile frames parallel.</summary>
    public sealed class FlatTileGraph : ITileGraph
    {
        /// <param name="centreSpacing">Tile side in kernel units: tile side in metres divided by L.
        /// Must be finite and positive, else ArgumentOutOfRangeException.</param>
        public FlatTileGraph(double centreSpacing);

        public int Q { get; }                     // 4
        public double Kappa { get; }              // Curvature.Flat
        public double CentreSpacing { get; }      // a
        public double CircumRadius { get; }       // a / sqrt(2)

        public TileId Neighbour(TileId tile, int edge);
        public int BackEdge(TileId tile, int edge);
        public Iso2 EdgeStep(TileId tile, int edge);
        public void VisibleSet(TileId centre, double rho, List<VisibleTile> into);

        /// <summary>The id of grid cell (i, j): i counts tiles along +x, j along +z, from tile zero.</summary>
        public static TileId Cell(long i, long j);
        /// <summary>False when the id is not a flat-grid id.</summary>
        public static bool TryGetCell(TileId tile, out long i, out long j);
    }
}
```

With a = `CentreSpacing`:

| Item | Definition |
|---|---|
| Id of cell (i, j) | `new TileId(i, 0, j, 0, 1, 0)`: X = i, Y = j, T = 1. The tile centre in the model is (a·i, a·j, 1). `Cell(0, 0) == TileId.Origin` (decided here) |
| A flat-grid id | `Xv == 0 && Yv == 0 && Tu == 1 && Tv == 0`. Anything else makes `TryGetCell` return `false` and the graph methods throw `ArgumentException` |
| Tile (i, j) on the floor | The square of side a·L metres centred on (a·L·i, a·L·j) in the room frame, sides parallel to room X and Z |
| `Neighbour(t, k)` | k = 0: (i + 1, j). k = 1: (i, j + 1). k = 2: (i − 1, j). k = 3: (i, j − 1) |
| `BackEdge(t, k)` | (k + 2) mod 4 |
| `EdgeStep(t, k)` | `Iso2.Boost(a · e_k, 0)` with e_0 = (1, 0), e_1 = (0, 1), e_2 = (−1, 0), e_3 = (0, −1). The same for every tile |
| Graph distance | Between (i, j) and (i′, j′): \|i − i′\| + \|j − j′\| |
| Tiles at graph distance n | 1 for n = 0, then 4n: 1, 4, 8, 12, 16, 20, 24, 28, 32 |
| Walk round a corner | Closes after exactly 4 steps |

`VisibleSet(centre, rho, into)` for the centre cell (ci, cj):

```text
into.Clear()
for n = 0, 1, 2, ...  while n · a / √2 ≤ rho + RadiusEpsilon:      # nearest point of ring n is on the diagonal
    for each offset (di, dj) of ring n, in this order:
        n = 0:  (0, 0)
        n ≥ 1:  for k = 0 .. n−1:  ( n − k,   k   )               # from +x round towards +z
                for k = 0 .. n−1:  ( −k,    n − k )               # from +z round towards −x
                for k = 0 .. n−1:  ( −n + k,  −k  )               # from −x round towards −z
                for k = 0 .. n−1:  (  k,   −n + k )               # from −z round towards +x
        if a · hypot(di, dj) ≤ rho + RadiusEpsilon:
            into.Add( Tile = Cell(ci + di, cj + dj),
                      Chain = Iso2.Boost((a · di, a · dj), 0),
                      GraphDistance = n )
```

No queue, no dictionary, no allocation. Reference counts, with rho / a on the left:

| rho / a | 0 | 1 | √2 | 2 | √5 | 2.5 | 2√2 | 3 | 4 | 5 |
|---|---|---|---|---|---|---|---|---|---|---|
| Tiles | 1 | 5 | 9 | 13 | 21 | 21 | 25 | 29 | 49 | 81 |

With a = 1.0613 and rho = 3 the count is 21, the figure §4.6 quotes for "a flat grid of the same spacing".

### 3.4 `ContentSeed`: final now

SplitMix64 folded over the six words in wire order (R3 §A5). Integer arithmetic only, so Mono, IL2CPP and the
Station's .NET 8 agree bit for bit. All arithmetic is `unchecked` and wraps modulo 2⁶⁴.

```csharp
public ulong ContentSeed(uint worldSeed)
{
    unchecked
    {
        ulong h = 0x9E3779B97F4A7C15UL ^ worldSeed;
        h = Mix(h, Xu); h = Mix(h, Xv);
        h = Mix(h, Yu); h = Mix(h, Yv);
        h = Mix(h, Tu); h = Mix(h, Tv);
        return h;
    }
}

static ulong Mix(ulong h, long word)
{
    unchecked
    {
        h += (ulong)word + 0x9E3779B97F4A7C15UL;
        h = (h ^ (h >> 30)) * 0xBF58476D1CE4E5B9UL;
        h = (h ^ (h >> 27)) * 0x94D049BB133111EBUL;
        return h ^ (h >> 31);
    }
}

public override int GetHashCode()
{
    ulong s = ContentSeed(0);
    return unchecked((int)(s ^ (s >> 32)));
}
```

Golden values (computed independently; the test `TileId_Seed` holds them):

| Words `Xu, Xv, Yu, Yv, Tu, Tv` | `worldSeed` | `ContentSeed` | `GetHashCode` |
|---|---|---|---|
| 0, 0, 0, 0, 1, 0 (`Origin`) | 0 | `0x4194AB7A26ABAF68` | 1732183058 |
| 0, 0, 0, 0, 1, 0 | 1 | `0x2D1912F0CC4D7056` | 1732183058 |
| 0, 0, 0, 0, 1, 0 | `0x000A71A5` | `0x32B1E53DB78ED428` | 1732183058 |
| 1, 0, 0, 0, 1, 0 (`Cell(1, 0)`) | 0 | `0x96D0F30FDF01A6F3` | 1238455804 |
| 0, 0, 1, 0, 1, 0 (`Cell(0, 1)`) | 0 | `0x88576DCBC0B24818` | 1222976979 |
| −1, 0, 2, 0, 1, 0 (`Cell(−1, 2)`) | 0 | `0x778545E01C883E4D` | 1796045741 |
| −1, 0, 2, 0, 1, 0 | `0x000A71A5` | `0x6C423E6694739896` | 1796045741 |
| 3, 0, −4, 0, 1, 0 (`Cell(3, −4)`) | `0xFFFFFFFF` | `0x54C68B49A291F26A` | −485598869 |
| 1, 0, 0, 0, 0, 1 (the {4,5} tile across edge 0 of tile zero) | 0 | `0xB27898E099B829B2` | 734048594 |
| 0, 0, 0, 0, 0, 0 (`default`) | 0 | `0xB3A523F080E922EC` | 860619036 |
| 9223372036854775807, −9223372036854775808, 5, −7, 11, −13 | 12345 | `0x9218621C55847DC5` | 1192343521 |

Never use `string.GetHashCode`, `HashCode.Combine` or `System.Random` for content: none is stable across
runtimes.

---

## 4 · The kernel step hook

Frame-loop step 4 (§6.3; README F5) is one call on one interface. The interface lives in **`Atlas.Kernel`**, not
`Atlas.World` (decided here): it uses only kernel and Mathematics types, so it needs no RoomModel type, runs in
the headless tests with no engine, and can be referenced by any assembly that may reference the kernel.

```csharp
// File: ATLAS/Assets/Atlas/Kernel/IWalkerKernel.cs
using System.Collections.Generic;
using Unity.Mathematics;

namespace Atlas.Kernel
{
    /// <summary>How the kernel is set up for one room and one dial position.</summary>
    public struct KernelConfig
    {
        /// <summary>Tiles per corner: 3, 4, 5 or 6 (the dial of section 3.2). Only 4 in this build.</summary>
        public int Q;
        /// <summary>L for q = 5 and 6, R for q = 3; any positive value for q = 4. Metres per kernel unit.</summary>
        public double MetresPerUnit;
        /// <summary>q = 4 only: the tile side, which is also the centre spacing. Metres. Ignored for other q,
        /// where the spacing follows from Q and MetresPerUnit.</summary>
        public double TileSideMetres;

        /// <summary>Q = 4, the given tile side, MetresPerUnit = KernelConstants.FlatMetresPerUnit.</summary>
        public static KernelConfig Flat(double tileSideMetres);
        public static KernelConfig Flat(double tileSideMetres, double metresPerUnit);
    }

    /// <summary>What one call of Step did.</summary>
    public struct KernelStep
    {
        /// <summary>The displacement was ignored: longer than 0.2 m, a head position that is not finite,
        /// or tracking not Normal.</summary>
        public bool Rejected;
        /// <summary>Tile edges crossed in this step: 0 to KernelConstants.MaxCrossingsPerStep.</summary>
        public int Crossings;
        /// <summary>Edge index, in the tile that was left, of the last crossing; -1 when Crossings is 0.</summary>
        public int LastEdge;
        /// <summary>The displacement that was integrated. Metres. Zero when Rejected.</summary>
        public double2 AppliedMetres;
        /// <summary>Crossings > 0.</summary>
        public bool TileChanged { get; }
    }

    /// <summary>
    /// The walker's place in the tiling, advanced once per frame at step 4 of the frame loop
    /// (Master Plan sections 4.6 and 6.3).
    /// </summary>
    public interface IWalkerKernel
    {
        /// <summary>The tiling in use. Replaced by Configure.</summary>
        ITileGraph Graph { get; }
        /// <summary>Graph.Kappa.</summary>
        double Kappa { get; }
        /// <summary>L (or R): metres per kernel unit.</summary>
        double MetresPerUnit { get; }
        /// <summary>rho: the radius at which the fog is opaque. Kernel units. Default KernelConstants.DefaultCullRho.
        /// Setting it marks Candidates stale; the list is rebuilt inside the next Step.</summary>
        double CullRho { get; set; }

        /// <summary>The walker's pose after the last Step, Reset or Configure.</summary>
        WalkerPose Pose { get; }
        /// <summary>Candidate tiles round the walker's tile, rebuilt only when the tile, CullRho or the graph changes.
        /// Owned by the kernel: read it, never modify it, never keep it across a Configure.</summary>
        List<VisibleTile> Candidates { get; }
        /// <summary>Result of the last Step. Cleared by Reset, ResetToFloor and Configure.</summary>
        KernelStep LastStep { get; }
        /// <summary>Number of Step calls since construction. Never reset.</summary>
        long StepCount { get; }

        /// <summary>New room or new dial position. Builds the graph, then behaves as Reset(WalkerPose.Start).
        /// Not on the frame path. Rule 4: call it only while the walker stands still.</summary>
        void Configure(in KernelConfig config);
        /// <summary>Places the walker. Re-roots if the pose lies outside its tile, then rebuilds Candidates.
        /// Forgets the previous head position: the next Step integrates nothing.</summary>
        void Reset(in WalkerPose start);
        /// <summary>Places the walker at a floor point of the room frame (metres), tile zero centred on the
        /// RoomModel anchor and unturned. This is how a space becomes registered to the room.
        /// The point also becomes the kernel's "previous head position".</summary>
        void ResetToFloor(double2 floorMetres);

        /// <summary>
        /// Step 4 of the frame loop. headFloorMetres is the floor point under the head in the room frame,
        /// from the geometry pose (after the pose filter): (x, z) in (.x, .y). The kernel forms the
        /// displacement since its previous call itself. Exactly one call per frame.
        /// </summary>
        KernelStep Step(double2 headFloorMetres, bool trackingNormal);
    }
}
```

```csharp
// File: ATLAS/Assets/Atlas/Kernel/FlatWalkerKernel.cs
namespace Atlas.Kernel
{
    /// <summary>The kappa = 0 kernel on a FlatTileGraph. Engine-free; allocates nothing in Step.</summary>
    public sealed class FlatWalkerKernel : IWalkerKernel
    {
        /// <summary>Configure(KernelConfig.Flat(KernelConstants.DefaultFlatTileSideMetres)); CullRho = DefaultCullRho;
        /// Candidates pre-sized to 1,024.</summary>
        public FlatWalkerKernel();
        // IWalkerKernel members as declared above.
    }
}
```

### 4.1 Where the tile size comes from

The flat tile is the **lap square**: side s, centred on the RoomModel anchor, so that tile centres are s apart
(§3.2). s is RoomModel `lapSquare.side` (§4.3, §4.4: s = clamp(min(Wf, Df) − 0.4 m, 1.0 m, 3.2 m)); lane C's
page has the C# member. The kernel itself never sees a RoomModel. `headFloorMetres` below is the (x, z) of the
geometry head pose in the room frame.

| When | Who | Call |
|---|---|---|
| App start | Composition root (`App/AtlasApp.cs`) | `WalkerKernel = new FlatWalkerKernel(); AtlasServices.Register(WalkerKernel);` The property is typed `IWalkerKernel`, so that is the service type. The same instance goes to `FrameLoopCore`, `SpaceLab`, `LookUploadStep` and `AppParams.DeclareRender` |
| A RoomModel becomes active (scan confirmed, file loaded, fixture chosen) | `SpaceLab.SetRoom(room)` | `kernel.Configure(KernelConfig.Flat(side))` with `side = room.LapSide`, or `KernelConstants.DefaultFlatTileSideMetres` when `LapSide` is not positive; then `IWalkerReset.LatchRoomReset()`. The head is still in the old frame at that moment, so nothing calls `ResetToFloor` yet |
| The first frame whose head pose is in the room frame (`pipeline.Sensors.RoomActive`), after step 3 and before step 4 | `FrameLoopCore.RunUpdate` | `ResetWalkerToFloor()`, then `pipeline.ResetGeometry()` |
| Just before `ISpaceModule.Enter` | `SpaceLab` | `if (module.Registered) _walker.ResetWalkerToFloor(); module.Enter(_kernel.Pose);` A space that is not registered is entered with the pose the kernel already has |
| Operator command `home` | `CommandRouter.Home` → `SpaceLab.ReturnHome` | Already in Home: `ResetWalkerToFloor()` unless a room reset is latched. Otherwise a switch to Home, which resets on `Enter` as above |
| Operator command `recentre` | `CommandRouter.Recentre` | `IFrameLoop.ResetWalkerToFloor()`, then `pipeline.ResetGeometry()` |
| Every frame, step 4 | `FrameLoopCore.RunUpdate`, driven by `AtlasFrameLoop` | `kernel.Step(headFloorMetres, trackingNormal)` (§4.2) |

Nobody outside the frame loop calls `ResetToFloor` or `Reset` directly. The two entry points are on the loop
(`App/Frame/FrameLoopCore.cs`; the interfaces are on lane A's page):

```csharp
// IFrameLoop and IWalkerReset: kernel.ResetToFloor at the geometry head's floor point of the frame in progress.
// A head position that is not finite resets to the anchor, double2.zero.
public void ResetWalkerToFloor();
// IFrameLoop: kernel.Reset(in pose).
public void ResetWalker(in WalkerPose pose);
```

`Configure` in this build: `Q` must be 4 (else §3.1). `MetresPerUnit` and `TileSideMetres` must be finite and
positive, else `ArgumentOutOfRangeException`. The graph becomes `new FlatTileGraph(TileSideMetres /
MetresPerUnit)`; a ratio that underflows or overflows is refused by that constructor before any state changes.

The `CullRho` setter throws `ArgumentOutOfRangeException` for a negative or non-finite value. It is driven by
the parameter `curved.cullRho` (`AppParams.CullRho`, range 2 to 3.5, default 3), declared in
`AppParams.DeclareRender`.

### 4.2 What the caller of `Step` does

```csharp
// App/Frame/FrameLoopCore.cs, RunUpdate, step 4. Lane A's code.
_frame.Kernel = _kernel.Step(new double2(_frame.HeadPosition.x, _frame.HeadPosition.z),
    _frame.Tracking == TrackingState.Normal);
_frame.Walker = _kernel.Pose;
```

`FrameContext.HeadPosition` is the geometry head pose after the pose filter of step 3 (room frame, metres).
`FrameContext.Kernel` (`KernelStep`) and `FrameContext.Walker` (`WalkerPose`) are how spaces read the result;
`FrameContext.FloorDelta` is information for spaces only and is not what the kernel receives.

- **The hook takes the position, not the displacement** (decided here). The kernel keeps the previous head
  position itself and forms d = head.xz − previousHead.xz as §4.6 writes it. No caller keeps a "previous
  position", so a reset in SpaceLab and the step in the frame loop cannot fall out of step with each other.
- One call per frame, also when nothing moved and also while tracking is lost (`trackingNormal: false`).
  `StepCount` rising by exactly one per frame is the check of `P1-B02`.
- The pose filter (lane C, step 3) is the first line of defence against relocalisation jumps. The kernel applies
  the same 0.2 m rule again by itself, so it is safe when driven directly from a test or a replayed pose log.
  Both guards are kept.
- `State.flags` bit 2 ("pose jump rejected", `StateMessage.PoseJumpRejected`) is set when either guard fired:
  `_frame.PoseJumpRejected || _frame.Kernel.Rejected && _frame.Tracking == TrackingState.Normal`. A step the
  kernel rejects only because tracking is not Normal does not set the bit.
- The Spectator (`Telemetry/Map/SpectatorLoop.cs`) never calls `Step`: each frame it places its own kernel
  from the received State, with `Reset(pose)` for a curved space id whose tile is a flat-grid cell and
  `ResetToFloor(head)` otherwise. `StepCount` stays 0 there.

### 4.3 `Step`, exactly

With L = `MetresPerUnit`, a = `Graph.CentreSpacing`, h = `KernelConstants.CrossHysteresis`:

```text
StepCount += 1
if headFloorMetres is not finite:
    d = (0, 0) ;  Rejected = true                         # previous is left as it was
else:
    d = hasPrevious ? headFloorMetres − previous : (0, 0)
    previous = headFloorMetres ;  hasPrevious = true      # every call, also when the step is rejected
    Rejected = not trackingNormal  or  not (d.x² + d.y² ≤ MaxStepMetres²)  # "over 0.2 m"
    if Rejected:  d = (0, 0)

Crossings = 0 ;  LastEdge = −1
if not Rejected:                                           # a rejected step does not touch the pose at all
    U = (U · Boost(d / L, 0)).Orthonormalised()
    repeat at most MaxCrossingsPerStep times:
        (px, pz) = (U.M02, U.M12)                          # the walker in tile coordinates
        e[0] = px − a/2 ;  e[1] = pz − a/2 ;  e[2] = −px − a/2 ;  e[3] = −pz − a/2     # distance past each edge
        k = index of the largest e[k]; on a tie the lowest k
        if e[k] ≤ h:  stop                                # inside the tile, or within the hysteresis band
        U = Graph.EdgeStep(tile, k).Inverse() · U
        tile = Graph.Neighbour(tile, k)
        Crossings += 1 ;  LastEdge = k

if Crossings > 0 or Candidates is stale:
    Graph.VisibleSet(tile, CullRho + 2 · Graph.CircumRadius + h, Candidates)

AppliedMetres = d ;  LastStep = this result ;  return it
```

Properties the flat implementation guarantees:

| Property | Statement |
|---|---|
| 1 : 1 | An accepted displacement of d metres moves the walker by exactly d metres (rule 3) |
| Bounded local frame | After every `Step`, \|U.M02\| and \|U.M12\| are at most a/2 + h. U never grows however far the walker goes |
| No rotation | At κ = 0, U stays a pure translation when the start pose is one |
| Registration | After `ResetToFloor(p)` and steps of which none was rejected, with `Tile` = cell (i, j): L · (a·i + U.M02, a·j + U.M12) equals the last head floor point to 1e-9 m. In general it equals p plus the sum of the accepted displacements |
| A jump is rejected once | `previous` moves on every call, so the frame after a jump integrates normally. Movement made while tracking was not Normal is never integrated |
| No flicker | A walker who has just crossed an edge must come back more than 2h past it (4 cm at L = 1) to cross again |
| Crossings | At most 2 per step in practice (0.2 m against a tile side of at least 1.0 m); the loop bound is 4 |
| Rejected step | Leaves `Pose` bit for bit unchanged |
| First step | The first `Step` after `Reset` or `Configure` integrates nothing: it only records the head position |
| Cost | No allocation; no exception for any `double2` input |

### 4.4 `Reset` and `ResetToFloor`

- `Reset(start)`: `start.Tile` must be a tile of `Graph`, `start.Local.Kappa` must equal `Kappa` and
  `start.IsValid` must be true, else `ArgumentException`. The pose is taken, then re-rooted with **no
  hysteresis** (h = 0) so that the walker is inside the tile it names: a far-off pose is brought next to its
  tile in one move (whole tiles, a point exactly on an edge stays), then the edge test of `Step` runs with h = 0,
  at most 8 times, to settle rounding. A frame more than 1e15 tiles from its tile is an `ArgumentException`.
  `Candidates` is rebuilt. `LastStep` is cleared (all zero, `LastEdge` = −1). `hasPrevious` becomes false.
- `ResetToFloor(p)`: the pose that `Reset(WalkerPose.Start)` followed by one straight, unfiltered walk of p
  metres from the room centre would give. `previous` becomes p and `hasPrevious` true, so a caller that passes
  the current head floor point loses no movement. `Candidates` is rebuilt and `LastStep` cleared. At κ = 0 the
  pose is computed directly:

```text
u = p / L
i = floor(u.x / a + 0.5) ;  j = floor(u.y / a + 0.5)        # a tie goes to the higher index
Tile = FlatTileGraph.Cell(i, j) ;  Local = Iso2.Boost((u.x − a·i, u.y − a·j), 0)
```

  A non-finite p, or one more than 1e15 tiles from the anchor, is an `ArgumentOutOfRangeException`.

### 4.5 Step 6, for when a warped renderer exists

Not built now; fixed so lane A's upload step and material pool leave room for it. For each drawn tile t of
`Candidates`: `MaterialPool.SetAnchor(material, (kernel.Pose.Local.Inverse() * t.Chain).ToFloat4x4())`. The
`float4x4` converts implicitly to `Matrix4x4` (`Unity.Mathematics`, `math_unity_conversion.cs`). One pooled
material instance per tile; no `MaterialPropertyBlock` (README F7). The setters lane A provides for this
(`Rendering/Look/MaterialPool.cs`):

```csharp
public static void SetAnchor(Material material, in float4x4 anchor);
public static void SetWarpMode(Material material, int mode);                    // 0 none, 1 hyperbolic, 2 spherical
public static void SetGate(Material material, int gate, float2 post, float sign);   // gate 0..3; sign 0 = off
public static void ClearGates(Material material);
```

---

## 5 · `AtlasWarp.hlsl`

File: `/workspace/ATLAS/Assets/Atlas/Rendering/Shaders/Resources/AtlasWarp.hlsl`. Owner: lane B. One entry
point: object position in, world position out (§8.3).

```hlsl
#ifndef ATLAS_WARP_INCLUDED
#define ATLAS_WARP_INCLUDED

// Values of the per-material _WarpMode.
#define ATLAS_WARP_NONE        0.0
#define ATLAS_WARP_HYPERBOLIC  1.0
#define ATLAS_WARP_SPHERICAL   2.0

// Vertex stage.
//   positionOS  object-space position of the vertex, metres.
//   returns     world-space position, metres, ready for TransformWorldToHClip.
float3 AtlasWarp(float3 positionOS);

#endif
```

The body in this build, complete:

```hlsl
float3 AtlasWarp(float3 positionOS)
{
    return TransformObjectToWorld(positionOS);       // mode none: the identity warp
}
```

The file holds the guard, the three defines and the function with this body; there is no separate prototype.

How `Atlas/Surface` calls it (lane A's vertex shader `AtlasVert` in `AtlasSurface.shader`; `positionOS` is a
`float3`):

```hlsl
float3 positionWS = AtlasWarp(input.positionOS);          // the only place a position is made
output.positionCS = TransformWorldToHClip(positionWS);
output.positionWS = positionWS;
```

What it reads:

| Name | Scope | HLSL type | Unit and meaning | Written by | Read in this build |
|---|---|---|---|---|---|
| `_WarpMode` | per material (`UnityPerMaterial`) | `float` | 0 none, 1 hyperbolic, 2 spherical. Default 0 | Whoever configures the pooled material, with `MaterialPool.SetWarpMode`. Nothing calls it in this build | No |
| `_Anchor` | per material (`UnityPerMaterial`) | `float4x4` | Upper-left 3 × 3 = `Iso2.ToFloat4x4()` of anchor[t] (§1.5): tile frame → walker frame, kernel units. Default identity | Tile renderer, step 6, with `MaterialPool.SetAnchor` | No |
| `_HeadPosWS` | global | `float4` | xyz = the head in world space, metres: `FrameContext.RawHeadPosition`, the raw tracked head (the eye midpoint), not the filtered geometry pose. One value for both eyes. w = 0 | `LookUploadStep` (`App/Frame/FrameSteps.cs`) through `AtlasLook.Upload`, once per frame at step 6 | Not by the warp. The fog of `AtlasCore.hlsl` reads it |
| `_HypL` | global | `float` | `IWalkerKernel.MetresPerUnit`: L, or R on the sphere. Metres per unit, > 0; a value that is not positive is uploaded as 1 | The same call | No |

```csharp
// Rendering/Look/AtlasLook.cs
public static void Upload(float3 headPositionWS, double metresPerUnit, double timeSeconds, float dt);
```

The names are those of §4.6. The authoritative list of ids is `AtlasShaderIDs`
(`Rendering/Look/AtlasShaderIDs.cs`: `Anchor`, `WarpMode`, `Gate0` to `Gate3`, `HeadPosWS`, `HypL`), which
carries exactly the names of this page; lane A keeps that file until lane B starts
(`docs/scene-ownership.md`). This page fixes what each value means and who writes it.

Mode switch:

| `_WarpMode` | Mode | World position | Status |
|---|---|---|---|
| < 0.5 | None | `TransformObjectToWorld(positionOS)`: the renderer's transform applies, as in any Unity shader | **Implemented** |
| 0.5 to 1.5 | Hyperbolic | `p = mul((float3x3)_Anchor, ExpH2(positionOS.xz / _HypL)); t = LogH2(p) * _HypL;` result `(_HeadPosWS.x + t.x, positionOS.y, _HeadPosWS.z + t.y)` (§4.6) | Lane B, `P1-B03` |
| ≥ 1.5 | Spherical | The same with the sphere's exp and log; `_HypL` holds R | Lane B, `P1-B08` |

Rules of the include, now and later:

- It declares **no uniform, no keyword and no `multi_compile`**. `_WarpMode` and `_Anchor` are declared in the
  single `UnityPerMaterial` block, and the two globals beside it, by `AtlasCore.hlsl` (lane A).
- It is included after URP's `Core.hlsl` (for `TransformObjectToWorld`) and after those declarations, and before
  the vertex function that calls it. The order in `AtlasSurface.shader` is `Core.hlsl`, `AtlasCore.hlsl`,
  `AtlasWarp.hlsl`, `AtlasPortalClip.hlsl`, `AtlasGates.hlsl`, each by bare file name from the same folder.
- The mode is a per-material number, tested with an ordinary branch (decided here, following §8.3 "prefer a
  per-material number"). One material can warp while another in the same frame does not: the warning cue, the
  menu and anything fixed to the head stay unwarped.
- **In this build every value of `_WarpMode` behaves as none.** A shader cannot throw; nothing sets a non-zero
  mode.
- In the curved modes the object's transform is **not** applied: `positionOS.xz` are tile-local coordinates in
  metres (the log chart of the tile, §3.4), `positionOS.y` is height above the floor and passes through
  unchanged. The renderer's `Transform` must be the identity and its `localBounds` are made huge, so Unity culls
  nothing and the CPU draw rule of §8.5 is the only culling.
- The warp returns a position only. Normals are not warped; the look shades from vertex colours (D23).
- Fog needs nothing from the warp: the log map keeps true distance, so world-space distance from `_HeadPosWS`
  divided by `_HypL` is ρ (R1 §F.6).

---

## 6 · `AtlasGates.hlsl`

File: `/workspace/ATLAS/Assets/Atlas/Rendering/Shaders/Resources/AtlasGates.hlsl`. Owner: lane B. Per-pixel
clip against up to four sight-line gates (Aligned Cells, §4.7).

```hlsl
#ifndef ATLAS_GATES_INCLUDED
#define ATLAS_GATES_INCLUDED

// Fragment stage. Discards the fragment when it lies on the hidden side of any enabled gate.
//   positionWS  world-space position of the fragment, metres (interpolated from the vertex stage).
void AtlasGatesClip(float3 positionWS);

#endif
```

The body in this build, complete:

```hlsl
void AtlasGatesClip(float3 positionWS)
{
    // No gate exists yet: nothing is clipped.
}
```

`Atlas/Surface` calls it first thing in the fragment shader (`AtlasFrag`), always, before `AtlasPortalClip` and
`AtlasShade`. It declares no uniform and no keyword. The gates are written with `MaterialPool.SetGate` and
`ClearGates` (§4.5); nothing calls `SetGate` in this build.

What it will read (reserved now so that the `UnityPerMaterial` block does not change when lane B lands):

| Name | Scope | HLSL type | Meaning |
|---|---|---|---|
| `_Gate0`, `_Gate1`, `_Gate2`, `_Gate3` | per material (`UnityPerMaterial`) | `float4` each | `(post.x, post.z, sign, unused)`: a corner post on the floor, world space, metres, and the side to keep. `sign` = 0 switches the gate off. Default (0, 0, 0, 0) |
| `_WorldSpaceCameraPos` | URP, per eye | `float3` | Each eye clips against lines through its own position, which is what makes the clip correct in stereo |

Four separate vectors, not an array (decided here): they need no array setter and are plainly SRP-Batcher
compatible. The later body, for reference (R1 §F.7):

```hlsl
float2 e = _WorldSpaceCameraPos.xz, d = positionWS.xz - e;
// for each gate g:   float2 q = g.xy - e;   if (g.z != 0) clip(g.z * (q.x * d.y - q.y * d.x));
```

A material whose gates are all zero is never clipped, so the default stays the identity after lane B lands.

---

## 7 · Tests

All in `/workspace/ATLAS/Assets/Atlas/Tests/Kernel/`, assembly `Atlas.Kernel.Tests`, plain NUnit, no engine
(README F2). They run in the Unity Test Runner and under `tools/headless/verify.py test Atlas.Kernel.Tests`.
Random tests use `new System.Random(KernelTestUtil.Seed)`, with `Seed = 20261004` in `KernelTestUtil.cs`
(written in full: `Unity.Mathematics` has a `Random` too). The same folder holds lane A's `Mesh4D/` tests. In the
walker tests "a step of d" means one call of `Step` with the previous head position plus d and
`trackingNormal: true`; every walk starts with `ResetToFloor` at the stated point.

### 7.1 Green now

| Test (file · method) | Proves | Pass condition |
|---|---|---|
| `Iso2FlatTests.cs` · `Iso2_Conventions` | Layout, order, sense of rotation | `Boost((1, 0), 0).Apply((0, 0, 1))` = (1, 0, 1). `Rot(π/2).Apply((1, 0, 1))` = (0, 1, 1) to 1e-15: +x turns to +z. `(Boost((1, 0), 0) * Rot(π/2)).Apply((1, 0, 1))` = (1, 1, 1): the right factor acts first. `Position` equals the third column. `Identity.Kappa == 0` |
| `Iso2FlatTests.cs` · `Closure_345_Flat` | The flat part of `Closure_345` | With g = `Boost((s, 0), 0) * Rot(π/2)`: g⁴ = I to 1e-12 for s = 0.5, 1.0613, 2.0, 3.2; g, g², g³ each differ from I by more than 0.1 in some entry |
| `Iso2FlatTests.cs` · `Kernel_Inverse_Flat` | The flat part of `Kernel_Inverse` | Largest entry of M · M.Inverse() − I below 1e-12 on 1,000 random M = Boost · Rot, translations in [−10, 10], angles in [−π, π] |
| `Iso2FlatTests.cs` · `Kernel_EtaOrthonormal_Flat` | Flat matrices stay on the group | `Defect()` < 1e-12 for `Boost`, `Rot` and their products; < 1e-12 after 100,000 random steps of `m = (m * Boost(d, 0) * Rot(θ)).Orthonormalised()` with ‖d‖ ≤ 0.2, \|θ\| ≤ 0.1; the last row is exactly (0, 0, 1) throughout; `Orthonormalised` leaves M02 and M12 bit-identical |
| `Iso2FlatTests.cs` · `Kernel_ExpLog_Flat` | The flat part of `Kernel_ExpLog` | `Log(Exp(t, 0), 0)` = t and `Distance(Exp(t, 0), o, 0)` = ‖t‖ to 1e-12 on 1,000 random t with ‖t‖ ≤ 4 |
| `Iso2FlatTests.cs` · `Iso2_Upload` | The upload form | For M = Boost((0.25, −0.5), 0) · Rot(0.3): `ToFloat4x4()` has `c2.x` = (float)M02, `c2.y` = (float)M12, `c0.y` = (float)M10, `c3` = (0, 0, 0, 1), `c0.w` = `c1.w` = `c2.w` = 0; `ToFloat3x3()` equals its upper-left block |
| `Iso2FlatTests.cs` · `Iso2_RowMajorRoundTrip` | The wire form | `FromRowMajor(CopyTo(m), 0)` equals m bit for bit; element 2 is M02, element 3 is M10 |
| `KernelCurvedGuardTests.cs` · `Kernel_CurvedNotSupported` | §3.1 | Every row of the §3.1 table throws the stated exception type; `Curvature.IsSupported` is true only for 0; `Boost((0, 0), 0.5)` throws `ArgumentOutOfRangeException` |
| `TileIdTests.cs` · `TileId_Basics` | Identity of ids | `Origin` words are 0, 0, 0, 0, 1, 0. `default(TileId).IsNone`. Equality, `==`, `!=` and the dictionary key behave by the six words. `FromWords(CopyTo(id)) == id`. `TryParse(id.ToString())` round-trips, including `long.MinValue` |
| `TileIdTests.cs` · `TileId_Seed` | The Mono half of `TileId_Seed` | The eleven rows of the table in §3.4, `ContentSeed` and `GetHashCode` |
| `FlatTileGraphTests.cs` · `FlatTileGraph_NeighbourRoundTrip` | The grid is a graph | For every tile within graph distance 6 of the origin and k = 0..3: `Neighbour(Neighbour(t, k), BackEdge(t, k)) == t`; the four neighbours are distinct; `BackEdge` = (k + 2) mod 4; `EdgeStep(n, BackEdge(t, k))` = `EdgeStep(t, k).Inverse()` to 1e-12 |
| `FlatTileGraphTests.cs` · `FlatTileGraph_Growth` | The graph is {4,4} | Breadth-first search through `Neighbour` from the origin finds 1, 4, 8, 12, 16, 20, 24, 28, 32 tiles at graph distance 0 to 8, with no duplicate |
| `FlatTileGraphTests.cs` · `FlatTileGraph_VertexClosure` | Four squares meet at a corner | From any tile, step through edge 0, then repeatedly through edge (b + 3) mod 4 with b the back edge of the step just taken: the first tile returns after exactly 4 steps, not after 1, 2 or 3. `Q == 4` |
| `FlatTileGraphTests.cs` · `FlatTileGraph_VisibleSet` | The radius query | Counts of the table in §3.3 for a = 1 and for a = 2.5; 21 for a = 1.0613, rho = 3. First entry is the centre with `Chain` = identity. `GraphDistance` is non-decreasing and equals \|di\| + \|dj\|. `Chain.Position` equals (a·di, a·dj, 1) exactly. The same from centre `Cell(−7, 3)`. Calling twice gives the same list |
| `FlatTileGraphTests.cs` · `FlatTileGraph_Ids` | The encoding | `Cell(i, j)` words are i, 0, j, 0, 1, 0. `TryGetCell` inverts it and returns false for (1, 0, 0, 0, 0, 1) and for `default`. Graph methods throw `ArgumentException` for those ids and `ArgumentOutOfRangeException` for edge 4 |
| `FlatWalkerKernelTests.cs` · `FlatWalker_Integrates` | Registration at κ = 0 | Tile side 2.0 m. From (0.3, −0.4), 20,000 random steps of length ≤ 0.19 m: after every step the floor point rebuilt from `Pose` (§4.3) equals the head position to 1e-9 m, and \|M02\|, \|M12\| ≤ a/2 + h |
| `FlatWalkerKernelTests.cs` · `FlatWalker_Crossing` | Tile crossing | Tile side 1.0 m. From (0.45, 0.45) one step of (0.12, 0.12): `Crossings` = 2, `Tile` = `Cell(1, 1)`, local = (−0.43, −0.43) to 1e-12. From (0.49, 0) one step of (0.02, 0): no crossing (0.51 is inside the band). A further (0.02, 0): `Tile` = `Cell(1, 0)`, `LastEdge` = 0 |
| `FlatWalkerKernelTests.cs` · `FlatWalker_NoFlicker` | Hysteresis | Tile side 2.0 m, start (0.99, 0). 1,000 steps alternating +0.05 and −0.05 m in x: exactly one crossing in total. Alternating +0.02 and −0.02 m: none |
| `FlatWalkerKernelTests.cs` · `FlatWalker_JumpRejected` | The kernel half of the pose-jump test | A step of (0.25, 0) leaves `Pose` bit-identical with `Rejected` true and `AppliedMetres` zero, and the next step of (0.05, 0) is accepted: the jump is rejected once. A head position of (NaN, 0) or (∞, 0) is rejected and leaves `Pose` bit-identical. A step of (0.05, 0) with `trackingNormal: false` is rejected, and that movement is not integrated later. Steps of 0.19 m are accepted, steps of 0.21 m rejected. The first `Step` after `Reset` integrates nothing. `StepCount` rises by one in every case |
| `FlatWalkerKernelTests.cs` · `FlatWalker_LapSquare` | The 3-4-5 walk at q = 4, through the hook | Tile side s = 2.0 m. Start at a lap corner (−1, −1); walk four sides of 2.0 m in 5 cm steps turning left: back at the start to 1e-9 m, same tile, and no tile change on the way (the lap runs along tile zero's edges). The same lap from (−0.7, −0.8) crosses four edges and ends in the starting tile |
| `FlatWalkerKernelTests.cs` · `FlatWalker_UnitFree` | L cancels at κ = 0 | The same 2,000-step walk on a 2.0 m tile with `MetresPerUnit` 1.0 and 1.885: floor points equal to 1e-9 m after every step; the same tile whenever the walker is more than 0.04 m from every tile edge (the hysteresis is in kernel units, so the moment of crossing may differ inside that band) |
| `FlatWalkerKernelTests.cs` · `FlatWalker_Candidates` | The candidate list | After construction, `Candidates` is the `VisibleSet` of the origin at radius `CullRho + 2·CircumRadius + CrossHysteresis`. It is rebuilt on a tile change and after `CullRho` is set, and is the same `List` instance throughout |
| `FlatWalkerKernelTests.cs` · `FlatWalker_ConfigureAndReset` | Set-up paths | `Configure` with `Q` = 5 throws `NotSupportedException`; with a `Q` that is no tiling, or a tile side or `MetresPerUnit` of 0 or NaN, `ArgumentOutOfRangeException`. `ResetToFloor((3.1, −2.2))` with side 2.0 gives `Cell(2, −1)` and local (−0.9, −0.2); a non-finite point throws `ArgumentOutOfRangeException`. `Reset` with `default(WalkerPose)` throws `ArgumentException` |
| `FlatWalkerKernelTests.cs` · `FlatWalker_StepAllocatesNothing` | No allocation on the frame path | Zero bytes allocated over 5,000 steps that include tile changes |
| `TileIdTests.cs` · `TileId_SeedSpreads` | The seed separates tiles | Distinct `ContentSeed` values for distinct tiles of one world |

### 7.2 Red until lane B lands

These §11.1 tests cannot pass on the flat stub. Until their task lands they are either absent or carry
`[Ignore("lane B: P1-Bxx")]`. They are never present and failing, because the suite gates every merge. In this
build all of them are absent.

| §11.1 test | Needs | Flat part already covered by |
|---|---|---|
| `Kernel_ExpLog` (κ < 0, κ > 0) | `P1-B01` | `Kernel_ExpLog_Flat` |
| `Kernel_EtaOrthonormal` | `P1-B01` | `Kernel_EtaOrthonormal_Flat` |
| `Kernel_Inverse` (η·Mᵀ·η) | `P1-B01` | `Kernel_Inverse_Flat` |
| `Holonomy_Triangle` | `P1-B01` | none: flat holonomy is zero |
| `Closure_345` (n = 5 at 1.0613, n = 3 at π/2) | `P1-B01` | `Closure_345_Flat`, `FlatWalker_LapSquare` |
| `Lorentz_Drift` | `P1-B01` | the 100,000-step part of `Kernel_EtaOrthonormal_Flat` |
| `Float32_Upload` | `P1-B03` | `Iso2_Upload` (layout only) |
| `TileId_RingInvariant` | `P1-B04` | none |
| `TileId_Growth` (1, 4, 12, 28, 64, …) | `P1-B04` | `FlatTileGraph_Growth` |
| `TileId_VertexClosure` (5 steps, not 4) | `P1-B04` | `FlatTileGraph_VertexClosure` (same walk, expects `Q`) |
| `TileId_Unique` | `P1-B04` | `FlatTileGraph_NeighbourRoundTrip` |
| `TileId_Seed` ({4,5} golden file of 64; IL2CPP half at G6) | `P1-B04`, G6 | `TileId_Seed` (eleven golden rows, Mono) |
| `VisibleSet_Counts` (13 / 45 / 81 / 129, 105; the draw rule) | `P1-B04`, `P1-B06` | `FlatTileGraph_VisibleSet` |
| `TileMesh_SeamsEdges` | `P1-B05` | none |
| `Mode2_SightLines` | `P1-B10` | none |
| `WorldLint_All` | `P1-B07` | none |

Also owed by lane B and not startable now: every acceptance test in the rows `P1-B02` to `P1-B12` of §9.5.6, and
the pose-jump half of `Safety_Independence` on a device (G5; its kernel half is `FlatWalker_JumpRejected`).

---

## 8 · What lanes A, C and D may and may not assume

### 8.1 May assume, in this build

| # | Statement |
|---|---|
| 1 | An `IWalkerKernel` is published by the composition root. It is a `FlatWalkerKernel`: κ = 0, `Q` = 4, `MetresPerUnit` = 1.0 |
| 2 | `Step` is called once per frame at step 4, costs microseconds, allocates nothing and never throws |
| 3 | `Pose` is always valid. `Pose.Local` is a pure translation with entries below one tile side |
| 4 | Every kernel type is a blittable value type; `TileId` works as a dictionary key; `ContentSeed` will never change |
| 5 | The warp is the identity: world position = the renderer's transform applied to the vertex. Unity's own culling and bounds are valid for every renderer |
| 6 | `AtlasGatesClip` discards nothing |
| 7 | A material that never sets `_WarpMode`, `_Anchor` or a gate renders unwarped and unclipped, now and after lane B lands |
| 8 | Bump Guard needs nothing from the kernel, and the kernel needs nothing from Bump Guard |

### 8.2 Must not assume: it stops being true when lane B lands

| # | Do not assume | Because |
|---|---|---|
| 1 | That S2, S3 or S4 can be entered | They are not registered in `SpaceRegistry`. A `SceneCmd` for them is refused and logged; it must not reach the kernel |
| 2 | That `MetresPerUnit` is 1 | It is L (1.4 to 3.0 m) or R in curved spaces. Multiply every time |
| 3 | That κ, `Q`, `Graph` or `MetresPerUnit` are constant for the session | The dial changes them, while the walker stands still. Read them from the kernel each frame; do not cache the graph |
| 4 | That `Pose.Local` has no rotation | In curved space it carries the holonomy: 90° after one lap of the lap square |
| 5 | That the walker's position can be turned into a room coordinate | Only at κ = 0, and only until the first rejected jump. Flat spaces take the head position from the pose (README F4), never from the kernel. Rule 7 |
| 6 | That what is drawn is where things are | Curved Walk and the Small Planet are registered only until the first step (§4.9). Bump Guard, in room coordinates, is the only protection |
| 7 | That the edge leading back is (k + 2) mod 4, or that tile frames are parallel to the room | True only on the flat grid. Use `BackEdge` and `EdgeStep` |
| 8 | That the six words of a `TileId` are a grid cell | `FlatTileGraph.TryGetCell` returns false for a {4,5} id |
| 9 | That `Candidates` is the list of drawn tiles, or that it is short | It is the candidate list before the draw rule; about 500 entries on the high tier. In this build no tile is drawn: telemetry `visibleTiles` is 0 |
| 10 | That a renderer's `Transform`, bounds or frustum culling mean anything under the warp | In the curved modes the transform is ignored, bounds are huge, and the CPU draw rule is the only culling |
| 11 | That a vertex ends up at `Transform` × object position | Under the warp it ends up at its true distance and bearing from the head. Never place, pick or collide by transform in a curved space |
| 12 | That a mesh with long edges will look right | Anything shown in a curved space needs edges of at most 0.25 m (§8.4). The Home shell's 0.25 m grid (lane C) and the asset validator (lane D) already give such meshes; keep to the rule for anything new that may later stand in a curved space |
| 13 | That fog and draw distances are fixed in metres | They are ρ · L |
| 14 | That a frame always moves the walker | A rejected step moves nothing. The geometry ignores what Bump Guard still sees |
| 15 | That `TileId.Step` or a curved `Boost` can be called "to try it" | They throw (§3.1) |

### 8.3 What this page needs from the other pages

| From | Needed |
|---|---|
| Lane A | `AtlasFrameLoop`, through `FrameLoopCore.RunUpdate`, calls `IWalkerKernel.Step(headFloorMetres, trackingNormal)` exactly once per frame at step 4 (§4.2); `LookUploadStep` sets the globals `_HeadPosWS` and `_HypL` at step 6 |
| Lane A | The single `UnityPerMaterial` block holds `float4x4 _Anchor`, `float _WarpMode` and `float4 _Gate0` to `_Gate3`; `AtlasShaderIDs` lists them and the two globals; the material pool initialises them to identity, 0 and zero |
| Lane A | `Atlas/Surface` calls `AtlasWarp` in the vertex stage and `AtlasGatesClip` first in the fragment stage, for every material |
| Lane A | SpaceLab calls `ResetWalkerToFloor` (§4.1) before entering a registered space, and does not register S2, S3, S4 |
| Lane A | `Atlas.Kernel.Tests.asmdef` lists `Unity.Mathematics` in its references: the tests use `double2` and `double3` |
| Lane C | The geometry pose stream gives the head's floor point in the room frame after the pose filter, and the tracking state; the RoomModel exposes the lap-square side s |
| Lane D | `State.tileId` = `Pose.Tile` in wire order and `State.xform` = `Pose.Local` row-major for curved spaces; flat spaces write what Appendix B.1 lists. `State.p[0]` = `(float)MetresPerUnit`, `p[1]` = `CullRho − 1`, `p[2]` = `CullRho` (`StateMessage.P0`, `P1`, `P2`). `FrameLoopCore.FillState` writes those three for every space, and writes the zero tile and the identity `xform` before the module's `WriteState` puts in its own. Tile ids in JSON are the `ToString()` text |

---

## Open for the lead

1. **`TileId.Step` on a bare id.** §6.5 puts `Step(edge)` on `TileId`, "additions only". Six integers fix the
   tile but not which neighbour lies across "edge k": that needs the tile's exact frame, which R3 §A5 keeps in the
   graph, not in the id. This page keeps the §6.5 name, makes `ITileGraph.Neighbour` the call every other lane
   uses, and leaves lane B two ways to honour `Step`: recover the canonical frame from the id (a greedy walk from
   tile zero, exact, linear in the distance), or implement it through the graph only. Confirm before `P1-B04`.
   No other lane is affected either way.
2. **κ tag inside `Iso2`.** Chosen so that `Inverse()` and `Orthonormalised()` keep the §6.5 signatures. The cost
   is that `Iso2.Rot(angle)` and `Iso2.Identity` are flat-tagged and curved code must use the two-argument forms.
   Confirm, or rule that the two methods take κ instead.
3. **How the curvature ramp is expressed.** With κ restricted to −1, 0, +1, the ramp of §4.6 ("from zero to the
   dial value") has to vary 1 / L while the walker stands still. `Configure` resets the pose, so a smooth ramp
   needs one more call on `IWalkerKernel` (an addition, announced to all four lanes). If lane B would rather ramp
   a continuous κ at fixed L, the κ rule of §1.1 changes instead. Decide before `P1-B02`.
4. **L at κ = 0.** Chosen 1.0 m per unit. The alternative is L = s / 1.0613, the room's q = 5 value, so that
   ρ and the fog radius mean the same metres on both sides of the dial. The kernel is unit-free and tested so;
   the change is one argument in the composition root.
5. **Re-registration after tracking loss.** In this build SpaceLab may call `ResetToFloor` freely, because every
   space is flat and registered. In a curved space it must never be called on tracking recovery: the world would
   jump. The rule for curved spaces ("only on Return to Home") belongs on lane A's page once S2 exists.
6. **Normals under the warp.** The entry point returns a position only. If `AtlasCore` ever shades by normal,
   lane B adds a companion function; that would be an addition to the contract, announced to all four lanes.
7. **`ContentSeed` across worlds.** In the fold of §3.4 `worldSeed` and `Xu` enter the same addition, so
   (`worldSeed` 1, `Xu` 1) and (`worldSeed` 0, `Xu` 0) give the same seed when the other five words agree.
   Seeds are unique within one world. §3.4 calls the function final; decide whether to change it before any
   content depends on it, since a change moves every golden value.
8. **Which head position `_HeadPosWS` carries.** The upload step writes the raw head position; the kernel
   integrates the filtered one. They differ in a frame where the pose filter rejects a jump. Harmless now (only
   the fog reads the global); under the warp the world is drawn around `_HeadPosWS`, so decide before `P1-B03`
   whether the warp wants the raw or the geometry pose.
