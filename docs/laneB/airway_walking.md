> Written on the branch `laneB/integration`, against the placeholders of lanes A, C and D. Where this log names a placeholder (`PLACEHOLDER`, `IActionSource`, `HomeRebuild`, `EventLog.Emit`, `AtlasSurface`, `_Gates`, `room_*.json`, `SpaceLab.Home`), the real code has replaced it, except the Phase 2 and 3 stand-ins in [placeholders.md](placeholders.md): see [INTEGRATION_NOTES.md](INTEGRATION_NOTES.md). The numbers of the contract sections it cites ("kernel.md §n") are the numbers of [../contracts/lane-b-kernel.md](../contracts/lane-b-kernel.md).

# Airway walking (G6): P2-B03 ValveSolver, Mode 1

Source: Master Plan §10.2.3 steps 1-5 and §10.2.8 row P2-B03; G6 brief (binding, plan wins). Code: `Assets/Atlas/Blades/Lung/Walking/`
(namespace `Atlas.Blades.Lung.Walking`), kernel additions in `Assets/Atlas/Kernel/` (`HolonomyRig.Reaim`, `WalkerHeading`,
`TileGraphQueries`). Tests: `Assets/Atlas/Tests/Lung/Walking/ValveSolverTests.cs`, `Assets/Atlas/Tests/Kernel/AirwayKernelTests.cs`.
Evidence: `docs/laneB/evidence/P2-B03/valve_sim.json` (deterministic).

## P2-B01 member mapping (only `AirwayTreeBinding.cs` names these)
`IForkTiles.Root` = `AirwayTree.FrameOf("").Tile`; `ForkTile(a)` = `FrameOf(a).Tile`; `TargetAddress`, `CompassForkAddress` as named;
`Focus(a)` = `SetCurrent(AirwayTile.Fork(a))`. `fork` events are owned by G6 (G4 emits only `compass_choice`).

## Deviations
G6 DV-G6-1..4, 5, 11, 12, 13, 20, 21 are implemented as written in the brief. New in B03:

| Id | Deviation |
|---|---|
| DV-B03-1 | Valve step 3: only a side whose smallest clearance is >= 0.30 m is eligible; the tie/larger rule then applies among eligible sides (the brief tests `max < 0.30` only, so a tied 0.28 m side could be chosen). Neither eligible: closed. A closed solve does not consume the tie toggle. |
| DV-B03-2 | T3.2 stamping: the blocked-side cell is placed at t >= max(0.2, 0.4/s) of the side (not 0.2): for s = 1.1 m a cell at 0.22 m from the shared corner would also block the other side (clearance < 0.30 m at its start), so "one side blocked" would not hold. |
| DV-B03-3 | `Enter` applies `rig.Reset` before that frame's `rig.Step` (brief: after). Otherwise the first Step after Reset records no previous head and the frame's displacement is lost, moving the chamber centre by up to 1.2 cm at 0.7 m/s and breaking the 1 mm registration. |
| DV-B03-4 | The glyph gate is "within 0.15 m and still (`LastRigResult.Still`, speed <= 0.1 m/s)", not distance alone. Re-aiming a walker who is merely passing 0.15 m moves the chamber centre by up to 2 delta (G6 pitfall 3); the sim walker, which lands exactly on the centre, then registers the next centre within 1 mm of the lap corner. S3.2 is adapted accordingly (reaim on the first still frame within 0.15 m). |
| DV-B03-5 | P1-B10 (`CellGrid`, Mode 2) is not merged into this branch; `AirwayWing` has no `Grid`, `Cells`, `TargetCell`, `RequestedMode`, `WriteState` yet (P2-B04/B05). `Mode` is always `CurvedWalk`. `Atlas.Blades.Lung.asmdef` is unchanged (no `Atlas.Spaces.AlignedCells` reference yet). |
| DV-B03-6 | Tests use `Atlas.Safety` in `Atlas.Blades.Lung.Tests` (asmdef reference added). Dead-end test S3.6 uses a `FakeForkTiles` (Rule S ids, no tree.json) because a random 16-letter address need not be a tree node. |
| DV-B03-7 | `SetTurnPrompt` is called on transitions (enter Waiting, valve open, chamber entry hides it), not every frame. `AirwayWing.EndAddress` (internal) is the hook for B09's route; until then `correct` uses `TargetAddress`. |
| DV-B03-8 | Evidence file records per-room min wall distance, routes ok, reaims and frames only. The wobble test S3.4 does not check the 1 mm registration (a 0.1 m wobble rotates the frame by holonomy; the head still stays >= 0.40 m from walls). |

## Cross-lane assumptions and hand-overs
A2 (placeholders.md): Unity world xz is the RoomModel anchor frame. The live `IClearanceQuery` is passed in (C-G6-1).
H-1: the renderer and kit register chamber objects into `ChamberViewRegistry`.



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


## P2-B05 Mode switch

Delivered: `AirwayParams` (`curved.mode`, 1/2, range [1, 2], default 1; DV-G6-8), `AirwayModeSwitch` (automatic rule: `room.lapSquare.s < 1.5`
at Configure, and FMS > 3 from the `fms` event `{score}` or `ReportFms`; DV-G6-9; Console switch = `registry.ApplyRemote` ->
`AtlasParamScanner` field), `AirwayWingModule` (S2, DV-G6-18), `AirwayWingHost`, `Scenes/Lung_Airway.unity` (one `AirwayWingHost`).
`AirwayWing` gains Mode 2 (delegating to P2-B04's `CellValveWalk`, resolving DV-B04-1 and DV-B03-5), `RequestedMode`, `Grid`, `Cells`,
`TargetCell`, `WriteState`, and `TrySwitch`: applies only in AwaitChoice, AwaitGlyph or Waiting with the shell Opaque (DV-G6-10);
Address, CurrentTile, NextTile, ExitEdge and Phase are kept. 1 -> 2: `Cells.Reset(head, CellFrame(CurrentTile, 0))`. 2 -> 1: DV-G6-11 rig reset.
Tests: `Tests/Lung/Walking/ModeSwitchTests.cs` (T5.1, S5.1-S5.5, scene test); evidence `docs/laneB/evidence/P2-B05/switch_sim.json`
(40 of 40: both directions x request in a chamber / while walking x 10 routes).

Deviations
- DV-B05-1: every `Enter` starts in Mode 1; a requested Mode 2 (small room, FMS) takes effect at the root chamber (phase AwaitChoice).
- DV-B05-2: Mode 2 `CurrentChamberWorld/NextChamberWorld` are the current and target cell centres; entry needs `Cells.Cell == TargetCell`
  and head <= 0.5 m (DV-G6-7); entering with a cell frame tile other than `NextTile` throws.
- DV-B05-3: `AirwayWingHost.Register` returns the `AirwayWingModule` (G6 §3.9 returns `AirwayBeat`, which arrives with P2-B09).
- DV-B05-4: `AirwayWingModule` copies `Requested` into the wing in `Tick` only (the wing is built in `Configure`); `AirwayModeSwitch`
  is disposed in `Exit` and re-created at each `Configure`. `ModeSwitch`/`Params` are exposed for tests.
- DV-B05-5: `Lung_Airway.unity` is created by the test `Scene_LungAirway_HasOneAirwayWingHost` with `EditorSceneManager` when absent.
- DV-B05-6: `SimWalker` gains an optional `Module` so frames pass through `AirwayWingModule.Tick`.
Phone half: `docs/laneB/TESTING.md` G10-B05 (deferred).

## P2-B12 Airway run test

Delivered: `AirwayRunReport` (FromEvents, FromJsonLines, ToJsonLine, ToJson), `NavigationBridge` (IForkChoice; wing.BeaconShown from the beacon; recentres the tree window and ticks the beacon per frame), test-only `BumpWarnOracle`, `AirwayRunTests` (T12.1, T12.2, S12.1).
Member mapping (the only file naming B06/B08 members): P2-B06 `AirwayBeacon` (Reset, Tick, RescueShown), `AirwayTree.SetCurrent`, `AirwayTile`, `AirwayRoute`; P2-B08 `TwoCompasses.EuclideanChild/BeaconChild`, `CompassChoiceEventData`.
Deviations
- DV-B12-1: P2-B08 has no fork-UI class, so the fork choice source is a delegate (`ChoiceSource`) plus `Submit(letter)`; S12.1 drives those.
- DV-B12-2: the bridge emits `compass_choice` itself (not through `ForkLog`, which would emit a second `fork`); the wing emits `fork`.
- DV-B12-3: `RouteForks` = endAddress.Length (PH-39); `RescueFree` = all forks correct and no beacon shown.
- DV-B12-4: the fms source is any scene (DV-G6-9). Oracle relax hold uses the path threshold of the current level (G6 silent).
Phone half: `TESTING.md` G10-B12 (deferred). The 4-fork acceptance is not claimed (PH-39).
