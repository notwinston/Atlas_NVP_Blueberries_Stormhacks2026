
## P2-B04 Valve walk, Mode 2

Delivered: `CellValve` (MP §10.2.3 Mode 2: longest clear run, ties to the lowest direction, no run of 1 means the valve stays closed)
and `CellValveWalk` (Mode 2 frame logic: `Enter`, `Walk`, `InChamber` at 0.5 m in the target cell, `TryOpen` = the 90° valve
rotation via `CellWalker.Reset(head, new CellFrame(tile, (dir - exitEdge) & 3))`). Tests: `Tests/Lung/Walking/ValveWalkMode2Tests.cs`;
evidence `docs/laneB/evidence/P2-B04/mode2_sim.json` (5 grids x 100 routes of seed 2101, 800 valves each, all complete).

Deviations
- DV-B04-1: `AirwayWing`, `AirwaySeams`, `ValveSolver` (P2-B03) were not in this branch's base (P2-B01 + P1-B10). The Mode 2 path is
  a standalone component; `AirwayWing.KernelStep/Tick` must delegate to `CellValveWalk` (Enter at switch-in, Walk, InChamber, TryOpen
  inside the Opaque-guarded private method, raising `Reaimed` from `Turn`). `CellValve.ClearMin` duplicates `ValveSolver.ClearMin` (0.30).
- DV-B04-2: the route tiles in the test come from `AirwayTree.FrameOf/BendFrameOf` and `TileGraph.Neighbour`, not B03's
  `TileGraphQueries` (absent); the test helpers (`Clear`, `Hundred`, grids) are private to `ValveWalkMode2Tests` to avoid name clashes with B03's helpers.
  `Hundred()` replicates `SimRoutes.Hundred()` (`new Random(2101)`, 4 letters).
- DV-B04-3: room_3x3 / budget_room files are not in this branch; grids use free rectangles 2.4x2.4 and 2.4x2.5 (3x3 at 0.8 m),
  1.5x1.5 (2x2 corner demo at 0.75 m), 2.25x3 (3x4 at 0.75 m). Clearance is a fake rectangle SDF plus blocked-cell-centre points.
- DV-B04-4: `Atlas.Blades.Lung.asmdef` gains `Atlas.Spaces.AlignedCells`; the tests asmdef gains `Atlas.Safety`, `Atlas.Spaces.AlignedCells`.
- DV-B04-5: Mode 2 has no glyph gate, so the valve opens at chamber entry (G6 brief DV-G6-7).
