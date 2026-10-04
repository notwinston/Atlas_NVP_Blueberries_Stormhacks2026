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
