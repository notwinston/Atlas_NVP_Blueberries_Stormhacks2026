# Lane B status (branch `phase1-integration`)

Lane B's code is part of the combined tree. It builds in the three configurations (phone, Station, Editor) and its engine-free tests run headless with the rest, through `python3 tools/headless/verify.py all`. The counts of the last run are in `docs/verification/UNITY_SIDE_BRIEF.md` §3d. The old numbers of the separate branch (463 tests, 454 passed) are history: lane B's tests were rewritten against the real contracts during the merge, and some moved or were removed with the placeholders they tested.

Nothing has been opened in the Unity Editor. What waits for the Editor is listed below and in `docs/verification/UNITY_SIDE_BRIEF.md`; what waits for a phone is in [TESTING.md](TESTING.md). How the lanes were joined: [INTEGRATION_NOTES.md](INTEGRATION_NOTES.md). The placeholders that remain: [placeholders.md](placeholders.md).

## Per task

"Headless" = engine-free tests that run under `verify.py test`. "Editor" = `[Category("Unity")]` tests, shaders, scenes. Test folders are under `ATLAS/Assets/Atlas/Tests/`.

| Task | Code | Headless proof | Waits for |
|---|---|---|---|
| P1-X06 Contract page | `docs/contracts/lane-b-kernel.md` (merged; `kernel.md` points to it) | – | The lead's confirmation of §9.4 of that page |
| P1-B01 Kernel mathematics | `Kernel/Iso2.cs`, `Geometry2.cs`, `Curvature.cs` | `Kernel/` (`KernelMathTests`, `Iso2FlatTests`, `KernelCurvedGuardTests`) | – |
| P1-B02 Holonomy rig, step hook | `Kernel/Walk/HolonomyRig.cs`, `HolonomyWalkerKernel.cs`, `SphereWalker.cs`, `IWalkerKernel.cs` | `Kernel/` (`HolonomyRigTests`, `HolonomyRigGraphTests`, `HolonomyWalkerKernelTests`), `App/FrameLoopTests.cs`, `App/E2E/` | Phone: `StepCount` rises once per frame |
| P1-B03 Warp include | `Rendering/Shaders/Resources/AtlasWarp.hlsl`, `Rendering/Warp/` | `Warp/WarpTests.cs` (CPU reference, ids, layout) | Editor: shader compile with the real warp and gates, SRP Batcher, `WarpGpuTests.Warp_MatchesKernel_Gpu` |
| P1-B04 Tile ids and graph | `Kernel/Tiling/` | `Kernel/` (`TileIdTests`, `TileIdContractTests`, `TileGraphContractTests`) | IL2CPP half of `TileId_Seed` (G6) |
| P1-B05 Tile meshes | `Rendering/Tiles/TileMeshGenerator.cs`, `LapMarkerMesh.cs` | `TileMeshes/` (`TileMesh_SeamsEdges`) | Editor: the look of the tile edge lines on a device |
| P1-B06 Tile renderer, draw rule | `Rendering/Tiles/TileRenderer.cs`, `TileDrawRule.cs`, `TileSelection.cs` | `Tiles/` (draw rule, recorded lap) | Editor: `TileRendererTests` (T4 allocation and timing, T6 fog, T7 pool, T8 shell cache). Phone: 60 fps, draw calls, 0 B on crossings |
| P1-B07 World generator, lint | `World/Generation/`, `Rendering/Tiles/Props/`, `World/Lint/` | `WorldGen/` | Editor: `WorldLint_All` and the other lint tests that build meshes |
| P1-B08 Small Planet (S4) | `Spaces/SmallPlanet/`, `Kernel/Tiling/CubeTiling.cs`, `Kernel/Walk/SphereWalker.cs` | `SmallPlanet/`, `App/E2E/` | Phone: 360° look-around with no smeared triangle |
| P1-B09 Dial and the 3-4-5 walk (S2) | `Spaces/CurvedWalk/`, `Kernel/Walk/CurvatureDial.cs`, `LapWalk.cs` | `CurvedWalk/`, `Kernel/` (`CurvatureDialTests`, `LapWalkTests`), `App/E2E/` (the five-corner lap through the real frame loop) | Phone: the lap with a spotter (gate G2); the dial and the ramp in a headset |
| P1-B10 Aligned Cells (S3) | `Spaces/AlignedCells/`, `AtlasGates.hlsl` | `AlignedCells/` (`Mode2_SightLines`, cell walk, crossings) | Editor: gate shader; phone: quarter-turn walk, frame rate |
| P1-B11 The room is tile zero | `Kernel/Walk/TileZeroPlacement.cs`, `Rendering/Tiles/CurvedRoomView.cs` | `Kernel/TileZeroPlacementTests.cs`, `CurvedWalk/`, `WorldGen/FootprintLintTests.cs` | Phone: touch points on walls and table within 1 to 2 cm |
| P1-B12 Room content in Aligned Cells | `Spaces/AlignedCells/AlignedCellsRoomView.cs` | `AlignedCells/` (`RoomContentTests`, `RoomBudgetTests`, `RoomViewTests`) | Phone: table edge within 5 cm at three points; 60 fps |
| P2-B01, P2-B06 Airway tree, beacon, route cost | `Blades/Lung/Airway/`, `Kernel/Tiling/TileFrame.cs`, `RuleS.cs` | `Lung/`, `Kernel/RuleSTests.cs` | – |
| P2-B02 Corridor and chamber kit | `Blades/Lung/AirwayKit/` | `LungAirwayViews/` | Editor: the wing's draw calls in `Space_CurvedWalk` |
| P2-B03, P2-B04, P2-B05, P2-B09, P2-B12 Walking, valves, mode switch, hand-over, run test | `Blades/Lung/Walking/` | `Lung/Walking/` | Phone runs; the FMS source (see below) |
| P2-B07 Endoluminal window | `Blades/Lung/Endoluminal/` | `LungAirwayViews/` | Editor: `EndoluminalGpuTests.Endo_Render_GPU`; the test scene was not reopened (see below). Phone: one window frame per fork |
| P2-B08 Two Compasses | `Blades/Lung/Airway/TwoCompasses.cs` | `Lung/TwoCompassesTests.cs` | PH-39: the 4-fork test is `[Ignore]`d |
| P2-B10, P2-B11 Needle sky, great circles | `Blades/Lung/Sky/` | `LungSky/` | Editor: `NeedleSkyGpuTests`, `SkyDataTests`. Phone: sweep, orientation, dwell, budget |
| P3-B01 to P3-B08 FieldFit | `Blades/FieldFit/` | `FieldFit/`, `FieldFitBlindSpot/` | Editor: `FieldFitBlitTests`, `LensGuardTests` (GPU); FieldFit's final-blit orientation. Phone: P3-B03 to P3-B08 |

Phase 4 mathematics documents (P4-B01 to P4-B05) are not written.

## What waits for the Unity Editor

- **Shaders.** `Atlas/Surface` with the real `AtlasWarp.hlsl` and `AtlasGates.hlsl` has never been compiled (Metal, D3D). Look at: the local array `float4 gates[4] = { _Gate0, … }` with the unrolled loop; `AtlasWarpClip`'s `clip()` in a file also compiled for the vertex stage and for `Tests/Warp/AtlasWarpProbe.compute`; `_HeadPosWS` as `float4`. SRP Batcher compatibility and the variant count of `Atlas/Surface` and `Hidden/Atlas/Background`. `Tests/AlignedCells/AlignedCellsGatesTest.shader`, `FieldFitBlit.shader`, `NeedleSky.shader`.
- **Generated scenes.** `Space_CurvedWalk`, `Space_AlignedCells`, `Space_SmallPlanet` import with their script GUIDs and load additively; the three are in the build list.
- **Lane B's test scenes.** `Blades/Lung/Endoluminal/TestScene/Lung_EndoluminalTest.unity`, `Blades/Lung/Scenes/Lung_Airway.unity`, `Lung_NeedleSkyTest.unity` and `Blades/FieldFit/Scenes/*.unity` were written before the merge. No file in the project names the old placeholder shader any more, but none of them was opened since: open each and fix a missing script or shader reference.
- **The `[Category("Unity")]` tests of lane B**, compiled but never run, are listed class by class in `docs/verification/UNITY_SIDE_BRIEF.md` §3d: in `Atlas.Kernel.Tests` none; `Atlas.Tiles.Tests`, `Atlas.TileMeshes.Tests`, `Atlas.Warp.Tests` (`WarpGpuTests`, `Warp_HugeLocalBounds`, `ShaderIDs_MatchContract`), `Atlas.WorldGen.Tests` (the lint tests that build meshes), `Atlas.Spaces.CurvedWalk.Tests`, `Atlas.Spaces.AlignedCells.Tests`, `Atlas.Spaces.SmallPlanet.Tests`, `Atlas.Blades.Lung.Tests`, `Atlas.Blades.Lung.AirwayViews.Tests` (`EndoluminalGpuTests`), `Atlas.Blades.Lung.Sky.Tests` (`NeedleSkyGpuTests`, `SkyDataTests`), `Atlas.Blades.FieldFit.Tests` (`FieldFitBlitTests`, `LensGuardTests`). Also `TileId_CrossingCost`'s timing bound (median at most 0.5 ms) under Mono: it passes on .NET 8.
- **The S2 five-corner lap, gate G2**, in the Editor walker, then with a spotter on the phone.

## Cross-lane needs and deviations

- **PH-39** (P2-D07 4-fork versus 6-fork airway route) needs a human decision.
- **FMS to the phone.** `AirwayModeSwitch` reads an `fms` event on the phone's log; the Hub's `fms` never reaches the phone (lane D §2.6).
- **Built-in furniture under the warp** is drawn unscaled, because the warp ignores a Transform (lane B page §6.1, §9.4); a mesh cross-fade under the warp draws the outgoing mesh unwarped for 0.3 s.
- **Reaching the concrete `StereoRig`.** FieldFit needs `OverrideEyeTargets`; `AtlasApp` publishes the concrete rig next to the guarded `IStereoRig`.
- **Per-task deviations** are in the per-task logs of this folder (`aligned_cells.md`, `airway_*.md`, `fieldfit*.md`, `needle_sky.md`); they were written against the placeholders, and where one names a placeholder, [INTEGRATION_NOTES.md](INTEGRATION_NOTES.md) says what replaced it.
