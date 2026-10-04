> Written on the branch `laneB/integration`, against the placeholders of lanes A, C and D. Where this log names a placeholder (`PLACEHOLDER`, `IActionSource`, `HomeRebuild`, `EventLog.Emit`, `AtlasSurface`, `_Gates`, `room_*.json`, `SpaceLab.Home`), the real code has replaced it, except the Phase 2 and 3 stand-ins in [placeholders.md](placeholders.md): see [INTEGRATION_NOTES.md](INTEGRATION_NOTES.md). The numbers of the contract sections it cites ("kernel.md §n") are the numbers of [../contracts/lane-b-kernel.md](../contracts/lane-b-kernel.md).

# Aligned Cells room content (P1-B12): deviations

- D1: "Its cell" means the cell range covered by the object's footprint, clamped to the grid (objects lie outside the free rectangle, Master Plan §4.3).
- D2: The Home floor is drawn 0.005 m low and fills the margin (>= 0.30 m) between the grid edge and the walls (§4.7, §4.5).
- D3: One per-room material instance for walls, floor and furniture; no category tint.
- D4: The ISpaceModule adapter (`AlignedCellsModule`) and scene host (`AlignedCellsSpaceHost`, `Space_AlignedCells.unity`) deferred by P1-B10 are built here (G3, `Configure(RoomModel)`, `SpaceLab.Home`).
- D5: `StateMessage.Sheet` is the row-major cell index (cell.y * CountX + cell.x); `Xform` is identity.
- D6: The room view is built lazily once `SpaceLab.Home` is non-null.
- D7: The Editor draw-call and triangle proxy is enabled renderers x submeshes x 2 eyes (`S3Count`).
- D8: `AtlasShaderIDs.cs` is the union of the lane-A placeholder and the lane-B ids of P1-B10; .meta files and AtlasGates.hlsl chosen at merge time (AtlasGates.hlsl from P1-B10; the three .meta files from the placeholders). Lane A's real file must keep the lane-B ids.
- D9: budget_room stand-ins in the test double are boxes subdivided (30, 20, 13) by edge rank (0.5, 0.8, 0.3 m), giving exactly 5,000 triangles (§4.3).
- D10: The grid is computed by the B10 `CellGrid` and cross-checked against `RoomModel.cells.aligned`.
- D11: Upload goes through `IUploadStage` (PH-3) and re-uploads when the root transform changes (fixes the stale-gates issue of the B10 review).
- D12: The module computes crossings with `Walker.Walk` plus `SightLines.Build`, mirroring `AlignedCellsSpace.Tick`; no B10 file changed.
- D13 (test input): the synthetic object of `Mode2_Room_ObjectCells` is placed at z = 0 instead of the brief's 0.3, because at z = 0.3 the footprint covers rows 1-2, not 0-2. Test 8 lives in `RoomCrossingTests.cs`.

# Duck maze (S3, 2026-10-04)

Design: [../superpowers/specs/2026-10-04-s3-duck-maze-design.md](../superpowers/specs/2026-10-04-s3-duck-maze-design.md).

- What it is: a Holonomy-style labyrinth inside S3. The {4,5} tiles behind the cells carry walls (0.9 m kerbs on the walled cell edges), tiles off the maze are dark (`AtlasPalette.Void`), and a rubber duck stands on one tile of the hyperbolic world. The walker finds it by walking; it is found when the walker's tile is the duck's tile. A head-locked read-out (`MazeHud`) shows "Find the duck  m:ss  walls N" while searching and "Found the duck!  N steps, M walls crossed, m:ss" after the find; five seconds later a new maze starts.
- `DuckMaze` (engine-free) grows a random tree of up to 20 tiles from tile zero by depth-first search over the steps the 4 × 4 floor allows, so every maze can be solved by walking; the duck is the deepest tile (depth 8 to 12 for most seeds). Random numbers are its own SplitMix64, so the phone and the Station build the same maze.
- Parameters (group `cells`): `cells.maze` (bool, default **on**) and `cells.mazeSeed` (int, 0 to 1,048,575, default 0: a fresh seed each run; otherwise every run uses it). Changing `cells.maze` while S3 is entered reconfigures the space as a re-entry does (`cells.enter` is logged again).
- Grid: in maze mode always 4 × 4 (`CellGrid.Maze`), side = the short side of the free rectangle ÷ 4, which must be at least `CellGrid.MinSide` (0.4 m): a free rectangle of at least 1.6 m on its short side. A smaller floor logs `maze.unavailable` and S3 runs classic.
- Every run is rooted at tile zero, turn 0, in the walker's cell; maze mode does not keep Curved Walk's tile on entry (the classic hand-over of §4.8 applies with the maze off).
- Events (phone copy only, the Station's copy logs nothing): `maze.start {seed, tiles, duckDepth, startCell}`, `maze.wall {tile, edge}` (each wall walked through), `maze.found {steps, walls, seconds}`, `maze.unavailable`. `maze.start` and `maze.unavailable` are logged at the first Tick after Enter, when it is known whether a phone drives the copy.
- State: `P7 = seed * 16 + startCellIndex` (exact in a float), 0 = no maze. The Station's copy rebuilds the same maze from P7 and draws the duck; it follows `cells.maze` from its own registry.
- Drawing: `AlignedCellsSpace` loads the kit once per Configure (piece `duck` from the shared `Spaces/Glued/Resources/AtlasKit`, as Glued reads it) and hands it to `AlignedCellsView`; a missing or refused piece gives the procedural duck. The view is re-applied whenever the maze object changes (a new run, a new P7, a reconfigure), and the ducks are animated every frame one is drawn.
- Tests: `DuckMazeTests`, `DuckMazeRunTests`, `DuckMazeViewTests`, `DuckMazeSpaceTests`, `DuckKitTests` in `Atlas.Spaces.AlignedCells.Tests`. Because the maze is on by default, the tests of classic S3 pin `cells.maze` off (`Fixtures.ClassicRegistry()`, `UnityStage`'s default, and the E2E rehearsal rigs `RehearsalRig`, `RehearsalSpectator`). The Unity-category tests of the view and the space are compile-checked by `verify.py build ... --config editor` but run only in the Editor's Test Runner.
- The duck asset is "Free Rubber Duck 3d Model" by Ryan King Art, CC BY 4.0: credit is required wherever the app is shown or shipped. See [../../art/blender/README.md](../../art/blender/README.md).
