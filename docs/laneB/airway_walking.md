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
