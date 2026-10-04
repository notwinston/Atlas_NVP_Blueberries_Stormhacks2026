# Lane C, Phase 2: status

Branch `laneC/phase2`, forked from `phase1-acd`. Per-package notes: `docs/laneC/status/` (W3, W5, W6),
`docs/laneC/P2_GLUED_EXHIBIT.md`. "Headless proof" means a test that runs in the Unity Test Runner and under
`tools/headless/verify.py`; it is not a phone result. Nothing in the "Pending device checks" column has been done.

| Task | State on this branch | Headless proof | Pending device checks | Lane B / lane A seams | Cross-lane edits to announce |
|---|---|---|---|---|---|
| P2-C01 Director and beats | Built (`Shell/Director/`): `PlaygroundDirectorCore` (10 beats, by condition and by `SceneCmd`, `pg.beat {beat:int, name:string}`), `PlaygroundBeatTable` and `Beats.asset`, the composition root `PlaygroundShell` (builds and wires every shell object and the exhibit relay), `PlaygroundInstaller` (Unity side: frame hook, exhibit binding, views), `OperatorCommandTap` (the `SceneCmd` seam), `PromptChip` | `DirectorTests` (by condition, by `SceneCmd`, 10 events, one prompt per beat), `PlaygroundFlowTests` (beats 1 to 10 through the shell with fakes for the lane B ports: `pg.beat`, `walk.*`, `probe`, `fms`, `glued.*`, `pg.home`, `seq`; 7 entrances; the SceneCmd tap) | All 10 beats by condition and by `SceneCmd`; 10 `pg.beat` events in `events.jsonl` (Editor, Station); `Beats.asset` loads (`Category("Unity")` test) | Lane B ports bound in `PlaygroundParts` (see P2-C03 to C05, C08, C17); the app calls `PlaygroundInstaller.Install` and wraps its `IOperatorCommands` in `OperatorCommandTap` | `SceneCmd` to S0: variant `next`, variant `beat:N` or args `{beat:N}` (N = 1 to 10), announced to lane D as free-form; event `pg.beat {beat, name}` (lane D reads it as a free-form event) |
| P2-C02 Scan-to-Home | Built (`Shell/Home/`) | `ShellScanToHomeTests` | Every box a placeholder or a mesh; swap moves a centre under 0.05 m (phone in hand, Station) | none | none |
| P2-C03 Centre mark and dial hand-off | Built (`Shell/Walk/`) | `ShellWalkTests` | Dial ignored above 0.1 m/s in 10 of 10; q = 3 to 6 each load (phone in headset) | Lane B: `IDialPort` (dial P1-B11) | none |
| P2-C04 Guided 3-4-5 walk | Built (`Shell/Walk/GuidedWalk`) | `ShellWalkTests` | Closure under 0.25 m at q = 3, 4, 5; 90 deg +- 5 per lap at q = 5 (phone in headset) | Lane B: `IWalkPort` (P1-B09 `LapWalk`, markers and read-outs) | none |
| P2-C05 Mode compare | Built (`Shell/Compare/`) | `ShellCompareTests` | Switch keeps the tile id; beacon distance within 0.1 m of the kernel (phone in headset) | Lane B: `IModePort`, `IHomeBeaconPort` (P1-B06, B10), `IMiniMapPort` (flat twin stand-in; lane D P1-D07 map) | none |
| P2-C06 Caption chips and equation cards | Built (`Shell/Cards/`), sources for S0, S2, S3, S4 | `CaptionChipTests`, `EquationCardTests` (Unity view tests under `Category("Unity")`), `PlaygroundFlowTests` (chip at every one of the 7 entrances through the shell) | Chip at 6 of 6 entrances; card refresh at least 10 Hz in the Editor | Lane A: `IEquationSource` for S1, S5, S6 (P2-A03); lane B: `IWalkReadout`, `ICellsReadout` | none |
| P2-C07 Action twins | Built (`Shell/Actions/`) | `ActionMapTests` | Full run completed three times, once per input alone (phone in headset); lever timings to tune | none | Request to lane D: a claim hook on `SpaceActions` for `ReturnHome` (W3 note) |
| P2-C08 Exhibit menu and return Home | Built (`Shell/Menu/`) | `ExhibitMenuTests` | After return, Home walls within 0.05 m of the RoomModel (tape, phone in headset) | Lane A: `ExhibitMenu.SlicerSlot` (`IExhibitSlot`, P2-A01); lane B: `ICurvatureRamp`, `IFloorProjection` | New event `pg.home {wall_dev_m}` (lane D reads it as a free-form event) |
| P2-C09 Tutorial prompts and FMS | Built (`PromptQueue`, `ComfortMonitor`, `OperatorEntryFeed`) | `PromptQueueTests`, `ComfortMonitorTests`, `PlaygroundFlowTests` (one prompt per beat over the ten beats; FMS from beat 4, SEQ at the end) | FMS every 60 s +- 5 s; entry in the log; `pg.fmsStop` measured at G2 / `P4-C02` (phone in headset, Station) | none | Lane D: carry Console FMS and SEQ entries to the phone (`IOperatorEntryFeed`); new events `fms` (phone side), `pg.fmsAlert`; param `pg.fmsStop` (default 4, provisional) |
| P2-C10 Bump Guard check in every space | Headless harness and document built (W6) | `BumpGuardSpacesTests` (13 passed, 9 skipped for S2 to S4) | Ten wall approaches and one 3-4-5 walk in each of S0, S1, S5, S6 (phone in headset); S2, S3, S4 after lane B | Lane B: S2, S3, S4 rows pending | none |
| P2-C11 Playground run (G8) | Run script, operator checklist and `check_run.py` built (W6); the run itself needs C01 and the other packages | `check_run.py --selftest` | Three complete runs in two rooms, no restart; Spectator shows every beat (phone in headset, Station) | Lane B spaces and dial; lane A P2-A01 to A04; lane D Console SEQ field and Spectator beat display | Lane D: `{t:"seq"}` in the Hub and Console |
| P2-C12 Glued wrap-walk | Built (`Exhibits/GluedExhibit*`) | `GluedExhibitTests` | S1 frame time and draw calls against section 8.5 (phone in headset) | Lane A: `IEquationSource` for S1 | none |
| P2-C13 Chiral kit | Built (`GluedChiralKit`) | `GluedExhibitTests` (determinant = (-1)^row in 100% of copies, built-in meshes) | Look of the kit meshes on the phone | Lane A look pass (P2-A04) | none |
| P2-C14 Gluing switch and T3 | Built (`GluedGluingSwitch`, `GluedLookUp`) | `GluedExhibitTests` (up above 60 deg shows the copy) | S1 within its rows of section 8.5 (phone in headset) | none | none |
| P2-C15 Impossible Rooms exhibit | Built (`RoomsExhibit*`, `RoomsFitLint`) | `RoomsExhibitTests` (fit 100% for three RoomModels) | Run the fit test in the Editor Test Runner | Lane A: `IEquationSource` for S5 (`FloorRatio`) | none |
| P2-C16 Loop corridor and room numbers | Built (`RoomsLoopTracker`, `RoomSigns`) | `RoomsExhibitTests` | Room number after four turns differs from start in 10 of 10; fallback when min(Wf, Df) under 2.4 m (phone in headset) | Lane A look pass (sign tint) | Uses lane A's existing `rooms.loop`; the exhibit adds no event |
| P2-C17 Pointing probe and SEQ | Built (`PointingProbe*`, `SeqCollector`, `HomeBearing`) | `PointingProbeTests` | `probe` and `seq` in `events.jsonl` for S0 and S2 (phone in headset, Station) | Lane B: `IHomeBearing` geodesic from the kernel | Lane D: `{t:"seq"}` Console field |

## Beat 9 and the exhibits (decision)

The plan's row 9 reads "Exhibit chosen, or done". Taken literally, choosing an exhibit would start beat 10, whose entry sends the
walker home, so the exhibit would last one frame. `PlaygroundShell` therefore ends beat 9 on: Done picked, the chosen exhibit
reporting `Completed` (`IExhibitLink`), or the walker's own Return Home twin (B held, home beacon, triple click). The choice itself
is logged by the exhibit's events and shown by `ExhibitRelay`. The operator can still force beat 10 with a `SceneCmd`. Lane A's Slicer
toybox joins through `ExhibitMenu.SlicerSlot` and, for its task line and completion, `ExhibitRelay.Register(ExhibitId.Slicer, ...)`.

## The kit hero assets (branch `kit-hero-assets`, not merged here)

This branch has none of that branch's files: the `landmark.glb.bytes` and `avatar.glb.bytes` pieces, `KitSet`, `GluedSpace`'s
kit loading, the optional `MeshData` parameters, `AtlasMeshBuilder.Append`, `art/blender`, `tools/kit` and the kit tests. The chiral
kit runs on the built-in meshes (landmark = the `LandmarkStep` spiral). Its signature changes are optional parameters, so lane C
code compiles on both. When that branch is merged: (1) check that the kit landmark's handedness is the hand `GluedChiralKit` and the
chiral prompts state, and if not derive the hand from the loaded piece; (2) re-check `GluedBudget` with the kit pieces (428 and 356
triangles) against the S1 rows of section 8.5. Lane C does not merge it. Both branches need the same uncommitted Scan sources
(`RoomStore`, `RoomModelBuilder`).

## Baseline note: RoomStore, ScanFailure and RoomModelBuilder

`phase1-acd` does not contain lane C Phase-1 sources that other code references: `Scan/Store/RoomStore.cs`,
`Scan/Routes/ScanFailure.cs` and `Scan/Build/RoomModelBuilder.cs`. A headless build fails without them. Verification
therefore copies stand-ins (`scratchpad/shim/`) into those paths; the third sits in the git-excluded `Scan/Build/`.
They are never committed (the two under `Scan/` show as untracked here; do not `git add` them) and must be replaced by lane C's
real files when Phase 1 of lane C is merged.

## Open before G8

1. Done: `PlaygroundDirectorCore`, `Beats.asset` and the composition root (P2-C01); the app still has to call `PlaygroundInstaller.Install` with the lane B ports bound.
2. Lane B's code (S2, S3, S4, dial, 3-4-5 walk, kernel, beacons) bound to the ports of `Shell/PlaygroundPorts.cs`.
3. Lane A: P2-A01 to P2-A04.
4. Lane D: Console SEQ field, FMS and SEQ entries back to the phone, Spectator display of the beat.
