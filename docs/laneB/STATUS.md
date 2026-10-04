# Lane B status (branch laneB/integration)

Integration of every lane B branch (P1-X06 to P3-B08 plus laneB/placeholders). EditMode suite on the merge: 463 total, 454 passed, 0 failed, 7 skipped, 2 inconclusive (GPU and scene-builder tests that need a GPU or a player; listed below).

Placeholders used: lane A, C and D stand-ins from laneB/placeholders (see docs/laneB/placeholders.md). Lane B code uses only the contract surface; the real lane A/C code replaces them file for file.

| Task | Branch | Status | Tests passed/failed (task run) | Deferred |
|---|---|---|---|---|
| P1-X06 · Contract pages, lane B (no code) | P1-X06 | green | 0/0 |  The other three lanes have read it and no question is open (P1-X06 row) |
| P1-B01 | P1-B01 | green | 23/0 | - |
| P1-B02 | P1-B02 | green | 37/0 | - |
| P1-B03 Warp shader include | P1-B03 | green | 33/0 |  SRP-Batcher compatibility in real shader;  Vertex-stage GPU check inside real shader |
| P1-B04 | P1-B04 | green | 36/0 |  Draw-rule half of VisibleSet_Counts, no-allocation on crossing, ProfilerMarker, IL2CPP seed half, q=3 graph |
| P1-B05 | P1-B05 | green | 29/0 |  Visual check with lane A shader/palette; GPU warp accuracy |
| P1-B07 | P1-B07 | green | 69/0 | - |
| P1-B08 Small Planet S4 | P1-B08 | green | 53/0 |  Phone 360 look-around with no smeared triangle |
| P1-B10 | P1-B10 | green | 54/0 |  Phone runs (quarter-turn walk, frame rate) |
| P1-A08, P1-C07, P1-C10, P1-C01, P1-X07, P2-D07 | laneB/placeholders | partial | 79/0 | - |
| P1-B12 | P1-B12 | green | 138/0 | P1-B12 Phone touch test (table edge within 5 cm at three touch points) and 60 fps |
| P2-B10, P2-B11 | P2-B10, P2-B11 | green | 118/0 | P2-B10 Phone in headset: sweep 20/20 within 0.1 mm, orientation, dwell, budget (TESTING.md steps 1-6); P2-B10 Shader orientation and R8 texture format tests (B10_Shader_RawOrientation_GpuOnly, B10_Textures_R8_Line |
| P2-B01, P2-B06 | P2-B01, P2-B06 | green | 117/0 | - |
| P1-B06 | P1-B06 | green | 9/0 | P1-B06 60 fps on phone A; probe draw calls, set-pass calls, triangles; 0 B GC on crossing frames; P1-B06 Screenshot edge test: the drawn-world edge cannot be told from the background |
| P2-B02, P2-B07 | P2-B02, P2-B07 | green | 111/0 | P2-B07 phone: exactly 1 window-time frame per fork, 0.000 ms otherwise (T-B07) |

## Skipped or inconclusive in the integrated run (need GPU or scene build; run in Unity with graphics)

- FieldFitBlitTests.LensOff_RealShader_MatchesReference_GPU
- LensGuardTests.Badge_FromFont
- LensGuardTests.Badge_VisibleWhileActive
- EndoluminalGpuTests.Endo_Render_GPU
- NeedleSkyGpuTests.B10_Shader_RawOrientation_GpuOnly
- SkyDataTests.B10_Textures_R8_Linear_PointClamp_Once
- TwoCompassesTests.P2B08_FourForkRoute_P2D07 (PH-39: 6-fork route vs 4; human decision)
- SpaceSceneBuilder.Build
- WarpGpuTests.Warp_MatchesKernel_Gpu

## Cross-lane needs and deviations

Per-task deviations and cross-lane needs are in the branch logs and in docs/laneB/*.md. Open item: PH-39 (P2-D07 4-fork vs 6-fork airway route) needs a human decision.

## Merge notes

- docs/contracts/kernel.md, docs/laneB/*.md: both sides kept (union).
- *.asmdef: union of references.
- AtlasShaderIDs.cs and placeholder .meta files: the placeholder side (lane A file and guids) kept; lane B ids are identical.
- AtlasGates.hlsl: lane B real implementation (P1-B10) replaces the placeholder stub, placeholder guid kept.
- SimWalker.cs: both routes kept (Sink from P2-B09, Module from P2-B05).

## Branches without a row above

P1-B09, P1-B11 (G2), P2-B03, P2-B04, P2-B05, P2-B08, P2-B09 (G6/G4), and P3-B01 to P3-B08 (FieldFit group) results are recorded in their branch logs and docs/laneB/*.md (aligned_cells.md, airway_*.md, fieldfit*.md, needle_sky.md, TESTING.md). All were merged and compile; their EditMode tests pass in the integrated run (0 failed). Phone and GPU parts are deferred to docs/laneB/TESTING.md.
