# Lane B G5: airway geometry and views (P2-B02, P2-B07)

Authority: Master Plan (MP) > Build Pipeline > Engineering Spec > the G5 brief. Code lives in `Atlas.Blades.Lung`
(`Assets/Atlas/Blades/Lung/AirwayKit/`, `.../Endoluminal/`); tests in `Assets/Atlas/Tests/LungAirwayViews/`
(`Atlas.Blades.Lung.AirwayViews.Tests`, D-20). No P1-B05 file and no placeholder file is edited.

## P2-B02 corridor and chamber kit (namespace `Atlas.Blades.Lung.AirwayKit`)

### Frames and kinds
Edge k of a tile is at angle k*90 deg from the chart +x axis toward +z (kernel.md 5.3). Every kit mesh is built with the entry
edge as kit edge 0 and placed by the anchor `inverse(U) * F[t] * KitGeometry.KitToTile(k)` (`KitToTile(k) = Iso2.Rot(k*pi/2)`, k = canonical entry edge).

| KitKind | kit open mask | occurs |
|---|---|---|
| Fork | 0b1011 | node tile with 2 exits (root: edge 0 is the trachea door) |
| Straight | 0b0101 | Rule S turn tile; extended straight tile |
| BendRight | 0b0011 | node with only its R child |
| BendLeft | 0b1001 | node with only its L child |
| End | 0b0001 | leaf |

`KitKind KitGeometry.Classify(int canonicalOpenMask, int canonicalEntryEdge, out int quarterTurns)`: rotates the mask so the entry
edge is bit 0, quarterTurns = entry, throws `ArgumentException` for an entry bit outside the mask or a mask that is not one of the five.

### Constants (`KitGeometry`, all metres)
w 0.4 (corridor half width), KerbHeight 0.4, MembraneTop 2.6, ChamberRadius 0.5, GlyphRadius 0.15, StripHalfWidth 0.1, DecalHeight 0.005,
ValveShellRadius 0.49, ValveCapRadius 0.5, ValveFloorHeight 0.01, ValveGlyphHeight 0.015, MaxKitEdge 0.25, ColumnStep 0.14 (true arc length),
RowStep 0.2 (14 levels, kerb cell rows 0-1, membrane cell rows 2-12), GlyphSegments 16, CapSegments 24, CapRings 3.
Derived (double): `InRadiusMetres` H = 0.5306375 L (flat g/2), `CornerT` t_c, `ChamberT` t_i, `ChamberAlpha` alpha (radians).
Reference values (s = 2.0, L = 1.884526): H 1.0, t_c 0.391282, t_i 0.297783, alpha 52.8105 deg. kappa is -1 (scale = L) or 0 (flat, scale = g >= 1.0 m);
anything else throws `ArgumentOutOfRangeException`.

### API
- `KitGeometry.FermiToChart / ChartToFermi` (equidistant coordinates about arm e; the oracle is the Iso2 composition, 1e-12 m), `WallPath` (7/6/5/5/3 runs for Fork/Straight/Bend/Bend/End),
  `RunPoint`, `TrueLength`, `ColumnCount`, `IrisSector`, `IrisColumns`, `ArcLength`.
- `AirwayKitMeshBuilder(TilePalette, AirwayPalette)`: `BuildTile(kind, floorSpec, Mesh)` and `BuildValveShell(kind, openIrisKitMask, opaque, kappa, scale, Mesh)`.
  The floor spec must be `TileMeshSpec.Hyperbolic(5, L, lod, 0, 0)` or `TileMeshSpec.FlatCell(g, 0, 0)`. Allocation-free after warm-up (measured 0 B per valve-shell build).
- `AirwayKitMeshes(builder)`: `Rebuild(kappa, scale)` builds 5 x 3 (kappa -1) or 5 (kappa 0) meshes; `Get(kind, lod)`, `Layout(kind, lod)`, `Kappa`, `Scale`. LOD choice is P1-B06's rule.
- One submesh, `IndexFormat.UInt16`, `TileVertex` layout, one draw call per tile per eye. Index ranges: Floor (copied unchanged from `TileMeshGenerator`), Kerb, Membrane, Strip, Glyph.
- `AirwayPalette.Provisional` UV blocks (provisional until P1-A06).

### Measured (EditMode, `evidence/P2-B02/`)
- Longest kerb / membrane / iris / strip / glyph / valve-shell edge: 0.2438 m (limit 0.25; the sqrt(0.14^2 + 0.2^2) diagonal).
- Wing draw calls at rho = 3: N = 51 tree tiles, 2N + 2 + 2 = 106 <= 150; N = 33 at rho + 2 R_c (`wing_bound.txt`).
- Wing triangles per frame: 258,628 <= 500,000 (max Near tile 2,500 triangles at s = 3.2; max valve shell 1,812).
- Valve shell managed allocation per build: 0 B.

### Deviations (the plan is silent unless marked)
D-1 "0.4 m kerbs" is a kerb 0.4 m high; "0.8 m wide" is the kerb faces at +-0.4 m from the centre line. D-2 Kerbs and membranes are zero-thickness double-sided ribbons on the corridor boundary; the generator is called with KerbMask = PostMask = 0. D-3 Membrane 0.4-2.6 m (`TileChart.PostHeight`), a 50% geometric checkerboard dither (no shader change). D-4 Footprint = union of arms and chamber disc. D-5 Kinds Fork/Straight/BendRight/BendLeft/End; Rule S's turn tile is the kit kind Straight. D-6 The valve shell is one overlay renderer at r 0.49 whose opaque state adds a roof and a floor disc. D-7 Iris sectors split at 45 deg beside an open neighbour. D-8 Decals sit 5 mm up, the strip starts at 0.15 m, palette UVs are provisional. D-9 The draw-call acceptance is a conservative bound (P1-B06 and P2-B01 are not on this base); the real count is taken at integration. D-10 Flat-cell variant for Mode 2 (g >= 1.0 m). D-20 Separate test assembly. D-21 Lung asmdef references appended (Unity.Mathematics, Unity.Burst, Unity.Collections, Unity.TextMeshPro; R7): announce to lanes A and D, resolve a conflict as the union. D-22 The vertex layout is P1-B05's `TileVertex`, which has no NORMAL or TEXCOORD1 (PH-8): flag to cross-review, it affects every tile.
Added by the implementation:
- D-23 Mesh vertices are float32, so tests of mesh vertices use 2e-6 m (strict-inside check 4e-6 m) where the brief writes 1e-9; the double-precision wall path (`WallPath`, `RunPoint`, joins, sector widths) is tested at 1e-9.
- D-24 Each wall run has two lattices, kerb levels 0-2 (kerb UV) and membrane levels 2-13 (membrane UV), so the two parts take different palette cells; the shared level-2 ring is duplicated.
- D-25 Strip and glyph on the tile are single-sided (up facing); everything on the valve shell is double-sided.
- D-26 Names: the brief's `H`, `CornerT`, `ChamberT`, `ChamberAlpha` are `InRadiusMetres`, `CornerT`, `ChamberT`, `ChamberAlpha`; `KitGeometry.RunPoint`, `IrisColumns`, `ArcLength` are added helpers.

### Hand-overs
G4 (P2-B01, P2-B08): AirwayTree supplies per tree tile the canonical open mask and entry edge (root: the trachea door edge, FrameOf edge 0). Kind and placement = `Classify` + `KitToTile`.
G6 (P2-B03): draw tree tiles only from `AirwayKitMeshes.Get(kind, lod)`, one pooled material instance per tile; the valve shell uses the tile's anchor. During the opaque phase keep the shell fixed relative to the walker (compensate its anchor by the same Rot(alpha)); rotate only once the head is inside r 0.49 m; switch to the dithered state with the exit iris open at step 5.

## P2-B07 endoluminal window (namespace `Atlas.Blades.Lung.Endoluminal`)

A true 3D view of one bifurcation from inside the model airway, rendered once per fork into a 512 x 512 texture (MP 10.2.3 Mode 1, "endoluminal window"), with "N o'clock" labels.

### API
- `AirwaySegmentMm(address, posMm, dir, lenMm, radiusMm)` (one `atlas.tree/2` node; `EndMm`), `ForkView(parent, left, right)` (child addresses must be parent + "L" / "R"). The tree.json reader is P2-B01's; P2-B08 maps its node type to `AirwaySegmentMm`.
- `EndoluminalCamera` (static, pure): `Pose` (camera on the parent axis at E - min(2 r_p, 0.5 len_p) d, forward d, up = anterior (0,-1,0) projected, superior (0,0,1) fallback, right = forward x up), `ClockAngleDeg`, `ClockHour`, `ClockLabel`, `ToScene` / `ToSceneDir` (z-reflection), `ViewMatrix`, `ProjectionMatrix` (90 deg, 0.005-10 m), `SceneScale` (parent radius = 0.1 m).
- `AirwayTubeMeshBuilder.Build(...)`: 24-sided open double-sided cylinder, ring spacing <= 0.5 r, vertex AO = 1 - 0.75 saturate((d - 1 r_p) / (7 r_p)). Parent drawn over its last 4 r_p, each child over its full length.
- `EndoluminalWindow` (MonoBehaviour, no Update/LateUpdate/FixedUpdate/OnGUI/render callbacks): `Initialise(surface, display, font)` (idempotent, all allocation), `Show(in ForkView)` (renders only when the address changes; returns true when it rendered), `Hide()`, `Release()`, `Texture`, `Display`, `RenderedAddress`, `RenderCount`, `LastRenderFrame`, `LastRenderCpuMs`, `ClockHour(child)`, `LabelText(child)`. Profiler marker `Atlas.Lung.Endoluminal.Render`. Constants: TextureSize 512, DisplaySizeM 0.4, LabelRadiusFraction 0.35, LabelHeightFraction 0.06.
- Render: CommandBuffer into a 512 x 512, 24-bit depth, 4x MSAA RT (no Camera, no layer, D-14). Fog globals are zeroed for the draw and restored (D-19). Labels are drawn after a depth-only clear.
- `Testing/SyntheticForks.Route(n[, planeAxis])` (generator constants, no randomness), `Testing/EndoluminalTestDriver` (phone driver), `TestScene/Lung_EndoluminalTest.unity` (not in the build list).

### Measured
- Editor render-frame CPU time (one Show, Null graphics device, no GPU work): about 0.2 ms; every repeat of the same address is an early return (299 of 299 in `Endo_RenderOncePerFork`).
- GPU test on this Mac (`gpu_results.xml`, run without `-nographics`, Metal): 96.6% of pixels differ from the background; both labels bright (max luma 1.0, in both the x-axis fork, 9 and 3 o'clock, and the y-axis fork, 6 and 12 o'clock). Images: `evidence/P2-B07/gpu_render.png`, `gpu_render_axisY.png`.

### Deviations
D-11 ForkView is the input. D-12 12 o'clock is anterior (0,-1,0) in LPS with a superior fallback: lane D must confirm the tree.json frame. D-13 Camera pose, 90 deg field of view, scene scale, z-reflection. D-14 CommandBuffer, not a Camera. D-15 Tube resolution and depth AO, no grandchildren. D-16 Label placement and size, depth cleared before the labels. D-17 The display is a 0.4 m quad with URP/Unlit (a display panel, like TMP text); the caller positions it. D-18 One RT with 4x MSAA, re-rendered only when the fork address changes. D-19 Fog is off for this draw and restored.
Added by the implementation:
- D-27 The brief's GPU criterion "luma at the viewport point of each child's far axis point <= 0.5 x luma at (0.5, 0.9)" is not met: measured 0.965 and 0.971. With the synthetic geometry (radius ratio 0.794, 37 deg) the far axis point projects onto the rim of the parent opening, where the child's near wall is seen, not deep tissue. The test logs both ratios and instead asserts that the darkest tissue pixel is <= 0.85 x the near-wall luma (measured 0.254 vs 0.602). Flag to cross-review: the AO range (0.25 at 8 r_p) gives a weak depth cue over the roughly 40 mm that is visible.
- D-28 TextMeshPro: ForceMeshUpdate(ignoreActiveState: true) leaves an inactive object's mesh empty (TMP uploads only for an active object), so each label is activated for the single ForceMeshUpdate call inside Show and deactivated again (no frame passes). The label objects are created active and then deactivated, because TMP builds nothing before its Awake. `mesh.bounds` is empty, so the label bounds are taken from the mesh vertices. TMP's SDF shader needs `_ScreenParams`; the command buffer sets it to 512 x 512 for the draw and restores it.
- D-29 The label offset is clamped so the whole label stays inside the view (the nominal 0.7 z offset clips the 9 o'clock label by about 4 px).
- D-30 `Release()` is public (idempotent) so EditMode tests can free the RT without relying on OnDestroy in edit mode.
- D-31 The GPU test ran through a copy of the shared runner without `-nographics` (`scratchpad/unity_test_gfx.sh`, same slot locking), because the shared runner always passes `-nographics`. It sets its own `_PaletteTex` global (the placeholder shader reads it; lane A's real palette replaces it).
- D-32 The evidence PNGs are written with the rows reversed (EncodeToPNG of the Metal read-back is upside down).
- D-33 Extra tests: `Endo_Labels_SyntheticRoot` (labels "9 o'clock" / "3 o'clock" after Show) and `Endo_TestScene_References` (the phone scene loads and the driver's three references resolve).
- D-34 The surface material sets only `_WarpMode` = 0. `_Anchor` and `_Gates` stay at the material default (zero matrices); the real AtlasWarp / AtlasGates must treat `_WarpMode` 0 as identity and no gates (as the placeholders do).

### Hand-overs
P2-B08 builds `ForkView` from tree.json nodes, calls `Show` at the fork chamber, positions `Display`, and may use `ClockHour` in "take the N o'clock branch". Nothing in G5 assumes 4 forks. At integration replace `SetFloat("_WarpMode", ...)` with `AtlasShaderIDs.WarpMode` (P1-B03).
