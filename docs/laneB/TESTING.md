# Lane B phone and headset testing (deferred acceptance)

One section per task: scene, steps, expected numbers. A human fills in the measured values. The master list is
`docs/laneB/checklists/P3-B03-B05-B08_phone.md`.

## P1-B06 Tile renderer (deferred: phone A in the headset)

- Scene: `Assets/Atlas/Spaces/CurvedWalk/Scenes/CurvedWalk_TileProbe.unity` loaded additively over `Atlas_Boot`; L = 1.885 (room_3x3 L5) or the room's loop-fit L5; ρ (`cullRho`) = 3.
- Build: Release, Frame Timing Stats on, debugger detached, phone A in the closed headset, thermal Nominal at start. Safety as for G2 (MP §11.3): team walker only, cleared floor, spotter within arm's reach (Bump Guard is still a placeholder).
- Steps and table: items 1-6 of `docs/laneB/checklists/P1-B06_phone.md`.
- Expected (MP §9.5.6 P1-B06, §8.5, §11.2): tile-shell draws (2 × drawn) ≤ 150 per frame at every pitch; total draw calls ≤ 300; 60 fps (frame time p50 ≤ 16.7 ms, p95 ≤ 20 ms); 0 B GC on every crossing frame; crossing frame ≤ 2 ms (`Atlas.Tiles.Select` + `Atlas.Tiles.Upload`); screenshot: the edge of the drawn world cannot be told from the background (blocked until lane A's P1-A06 background gradient whose horizon colour equals `_FogColor`).
- A missed number is reported with its measured value and not relaxed (MP §9.9 rule 7).

## P1-B09 Curvature dial and the 3-4-5 walk (deferred: phone A in the headset, G7 integrated build)

- Scenes: `Assets/Atlas/Spaces/CurvedWalk/Scenes/Space_CurvedWalk.unity` and `Assets/Atlas/Spaces/SmallPlanet/Scenes/Space_SmallPlanet.unity`, loaded by `Atlas_Boot` (lane A) with the live RoomModel (lane C). Each scene holds one component (`CurvedWalkSpaceScene`, `SmallPlanetSpaceScene`) whose `Module` lane A registers with `SpaceLab`.
- Host wiring (lane A): `SpaceRequested` to `SpaceLab.Request`; `Home` = `SpaceLab.Home`; `Actions` = lane C's action map. Without `Actions` the dial still follows `curved.q` (Console / registry).
- Safety as for G2 (MP §11.3): team walker, cleared floor, spotter within arm's reach.
- Steps:
  1. Stand at the room centre, dial to 5 (D-pad), wait about 4 s for the ramp, walk to the flag, follow the beacons round one lap and on to the fifth corner.
  2. Return to the centre, dial to 3 (S4 loads once K has reached 0), walk 3 corners.
  3. Dial to 4 and walk 4 corners.
  4. Repeat step 1 pressing D-pad left/right while walking.
  5. Load a standing RoomModel (min(Wf, Df) < 1.5 m) and cycle the dial 3 to 6.
- Expected (MP §9.5.6 P1-B09, §10.1.3, §11.2):
  - `walk.closed` `closure_m` < 0.25 at q = 5, 4 and 3.
  - `walk.lap` `turn_deg` 90 +- 5 at q = 5 (the world-turn read-out, logged).
  - Zero frames in `state.bin` plus the head-pose log where `p[0]` changes while head speed exceeds 0.1 m/s.
  - D-pad presses while walking change nothing; the same presses on a still frame do.
  - Standing room: the dial snaps 3..6, q = 5 reaches K = -1/1.4^2, no flag or beacon is visible, at q = 3 S4 is still requested.
- The read-outs are not drawn as text (no text package, same open lead item as P3-B01; deviation D7): read `walk.corner` / `walk.lap` / `walk.closed` on the Console or in the event log.
- Editor-proved here: `Atlas.Spaces.CurvedWalk.Tests` (closure q = 3, 4, 5, 6 on replayed walks, curvature frozen while head speed > 0.1 m/s, ramp duration, S4 request, standing room, logged events) and `Atlas.Kernel.Tests` (`CurvatureDialTests`, `LapWalkTests`, `HolonomyRigGraphTests`).

## P1-B11 The room becomes tile zero (deferred: phone A in the headset)

- Scene: `Assets/Atlas/Spaces/CurvedWalk/Scenes/Space_CurvedWalk.unity` with the live Home rebuild (P1-C07) as `Home` (`SpaceLab.Home`) and the live RoomModel.
- Steps:
  1. q = 4 (flat): touch three points on a wall and one table edge with the controller or a finger; note them.
  2. Dial 5, wait about 4 s, dial 4, wait about 4 s, touch the same points.
  3. Walk one lap, press Return Home (B held), touch the same points again.
  4. Run the WorldLint menu on the session RoomModel (`Atlas/World/Run WorldLint`).
  5. Walk the lap on the lap square and watch the tile id on the Spectator.
  6. Look at the start-corner post from the flag.
- Expected (MP §9.5.6 P1-B11, §4.6, §3.5, A.4):
  1. Each point within 1 cm of the real one (the room at kappa = 0 is exactly Home).
  2. Within 2 cm; `LastRegistrationErrorM` is logged by the space (read it in the test build's debug overlay or the event log).
  3. Within 2 cm after Return Home; `SelectedQ` reads 4.
  4. `PropTilesInFootprint` 0 and `FootprintTilesNotHome` 0.
  5. No flicker: every tile run is at least 0.25 s (15 frames at 60 Hz); the sequence is tile zero, then one neighbour per side, back to tile zero.
  6. The corner post is 0.17 m from the flag at L = 1.88 m (0.0920553 L) and fades once lane A's near-head fade lands (the 0.3 m fade is in lane A's AtlasCore, deviation D13).
- Editor-proved here: `Atlas.Spaces.CurvedWalk.Tests` (`CurvedRoomViewTests`, `TileZeroTests`), `Atlas.WorldGen.Tests` (`FootprintLintTests`) and `Atlas.Kernel.Tests` (`TileZeroPlacementTests`).
One section per task: scene, steps, expected numbers. A human fills in the measured values.

## P1-B12 Room content in Aligned Cells (DEFERRED: phone A in the headset)

- **Scene:** `Assets/Atlas/Spaces/AlignedCells/Scenes/Space_AlignedCells.unity`, loaded additively by the boot scene.
- **Preconditions:** real P1-A04 (stereo rig), P1-A06 (AtlasCore declares `_Gates` and calls `AtlasGatesClip`), P1-A08 (AtlasFrameLoop; boot calls `AlignedCellsSpaceHost.Register(spaceLab)`), P1-C06 (RoomModel from a scan) and P1-C07 (`SpaceLab.Home` set). Release build; note the thermal state at the start.

### Test 1, table edge (G7 item; criterion "within 5 cm at three touch points after ten cell crossings")

1. Scan a room with a table, enter S3 and stand in the cell next to the table.
2. Mark three points on the real table's near top edge: left corner, middle, right corner.
3. Walk ten cell-centre-to-cell-centre moves round the nearest interior post. Check `Crossings == 10` on the Spectator or in the log.
4. Touch the virtual edge at each of the three points. A helper measures the gap between fingertip and real edge with a ruler.
5. **Pass:** all three gaps <= 0.05 m. Record the three values: ____ / ____ / ____ m.

### Test 2, budget (G4; criterion "S3 on budget_room <= 170 draw calls and <= 200k triangles per frame at 60 fps")

- Load `budget_room.json` through lane A's replay or A14 tour path. Walk all 12 cells.
- Read ProfilerRecorder "Draw Calls Count" and "Triangles Count", and FrameTimingManager.
- **Expected:** <= 170 and <= 200,000 (Editor proxy worst: 76 draw calls, about 128k triangles); 60 fps; frame time p50/p95/p99 <= 16.7 / 20 / 33.3 ms (Master Plan §11.2 Must).
- Overrun: report the measured value, change no number (§9.9 rule 7).

## P1-B10 Aligned Cells S3 (DEFERRED: phone in the headset)

Same scene and preconditions as above.

- Four quarter-turns round an interior corner end in a fifth tile, and a fifth returns to the first (read the tile id on the Spectator).
- Tile instances alone cost <= 120 draw calls per frame on the 4 x 5 m sample room (Editor worst: 34).

# Lane B phone and integration checks (deferred acceptance)

## P2-B02 Corridor and chamber kit (acceptance is Editor; integration check pending other groups)
- Everything in the acceptance cell passes in EditMode: `Atlas.Blades.Lung.AirwayViews.Tests` (kit tests), evidence in `docs/laneB/evidence/P2-B02/`.
- **Integration check** (Editor, once P1-B06, P2-B01 and P2-B03 are merged, in G6):
  1. Run the Play-mode harness in `Space_CurvedWalk` with the fixture tree and the lazy window.
  2. Walk the fixture route on the centre strip (6 forks, PH-39) with looks down to -90 deg.
  3. Read `UnityEditor.UnityStats.drawCalls` per frame for the wing renderers.
  4. Expected: <= 150 per frame (both eyes) at `curved.cullRho` = 3, and <= 106 by the bound.
- **Headset look** (an observation at the P2-B12 run, not acceptance): sibling corridors visible through membranes; no flicker between strip, glyph and floor; no gaps at corridor corners or at tile edges; the valve shell fully closed, with no world visible while it is opaque.

## G10 Lung airway and sky

### P2-B05 Mode switch (deferred: phone in headset)
- Scene: Lung_Airway.unity over the scanned room; Console connected; route `lung.route` = 0.
- Steps: walk two chambers in Mode 1; while walking the third corridor set `curved.mode` = 2 on the Console; enter the next chamber;
  then set `curved.mode` = 1 and enter the following chamber.
- Expected: the switch happens only inside a chamber with the shell opaque (nothing jumps outside it); the address on the Console
  and Spectator twin (fork events) is unchanged across both switches; the walk continues to the next fork. Record: address before
  and after each switch, frame of switch. 2 of 2 switches keep the address.

## P2-B09 Lesion chamber hand-over (Editor tests pass; phone measurement deferred)
- Editor: `HandOverTests` (T9.1 trigger at end tile, T9.2 ramp only while standing 3-5 s, T9.3 S6 requested once + beat, T9.4 no simulated frame > 33 ms; evidence `evidence/P2-B09/handover_sim.json`).
- Phone, deferred (phone in headset): Lung_Airway.unity, run once with `lung.route` = 1 and once with 0 (PH-39). Walk to the end chamber, stand on the glyph, take one step mid-ramp, stand again, wait for S6.
  Expected: curvature changes only while standing; ramp 3-5 s of standing (4.0 s set); S6 loads; no frame > 33 ms from end-chamber entry to S6 + 2 s (Frame Timing Stats max). Record max, p95, ramp time.
- Deviations: DV-B09-1 route is a constructor value (AirwayParams/Mode 2 from P2-B04/B05 not on this branch; Mode 2 path S6-at-entry not built); DV-B09-2 AirwayBeat wraps AirwayWing + LesionHandOver (AirwayWingModule is B05).

## P2-B07 Endoluminal window (deferred: phone in hand)
- **Scene:** `Assets/Atlas/Blades/Lung/Endoluminal/TestScene/Lung_EndoluminalTest.unity`. It contains a Main Camera and `EndoluminalTestDriver`, whose fields reference the Atlas/AtlasSurface shader, the URP/Unlit shader and LiberationSans SDF.
- **Build:** use the on-request slot with Build Profiles = [Lung_EndoluminalTest.unity]. Release build, phone A in hand, thermal Nominal at the start. Afterwards run `git checkout ATLAS/ProjectSettings`.
- **Driver:**
  1. 120 baseline frames.
  2. Then Show for `SyntheticForks.Route(4)` (fork addresses "", "L", "LR", "LRL"), one every 300 frames.
  3. Per frame, it logs the Stopwatch ms spent in window code and `RenderCount` to `persistentDataPath/endoluminal_timing.csv` (columns frame, fork, window_ms, render_count, frame_ms), and prints a summary to the Xcode console.
- **Pass:**
  - (a) For each fork, exactly 1 frame has window time > 0, and that frame's `RenderCount` step is +1.
  - (b) In every other frame (>= 299 per fork) window time = 0.000 ms and `RenderCount` is unchanged.
  - (c) The quad shows the bifurcation with two "N o'clock" labels, upright and readable (not mirrored or upside down); for the root fork they read 9 and 3.
- **Record:** the render-frame CPU ms and frame time (report only), plus an optional Xcode GPU capture of the render frame.
- **Editor GPU test:** `EndoluminalGpuTests.Endo_Render_GPU` needs a graphics device; it is skipped under `-nographics`. It passed once on this Mac without `-nographics` (`docs/laneB/evidence/P2-B07/gpu_results.xml`); rerun it in the Editor with a graphics device when the shader or the font changes.

## P2-B08 Two Compasses (DEFERRED; needs G6 walking: P2-B03/B05)
- **Scene:** `Assets/Atlas/Blades/Lung/Scenes/Lung_Airway.unity` (beat b use-case scene, lane B, MP §9.3; created by G6, hand-over H-3). Illustrative package. Route = the `tree.json` route (PH-39: the fixture route has 6 forks, not 4; fork 1, address "", is the designed fork). Phone A in the headset, Release build, thermal state noted, Bump Guard on, spotter present.
- **Preconditions:** P2-B02, P2-B03, P2-B05, P2-B07 merged; lane D event sink live (or EventLog placeholder plus Hub).
- **Steps:**
  1. Start beat b at the trachea chamber.
  2. Without waiting, the tester reports which corridor each arrow points into (solid = beacon, other cell = Euclidean compass) and which branch the endoluminal window labels.
  3. Tester chooses a branch.
  4. Read `logs/<session>/events.jsonl`.
  5. Walk on; at a non-designed fork stand still 9 s.
- **Pass (acceptance):** at the designed fork both arrows are visible on arrival, pointing into different child corridors, the beacon into `target.address[len(fork)]`; exactly one `compass_choice` and one `fork` event for that address, choice matching the corridor taken.
- **Pass (supporting):** at the other fork no arrow before 8 s, arrow at 8 s +/- 1 frame, `fork` event `beacon_shown` = true.
- **Record:** compass, beacon, choice, t_s, thermal state. Do not claim P2-D07's 4-fork criterion (PH-39).

## P2-B09 Lesion chamber hand-over (Editor tests pass; phone measurement deferred)
- Editor: `HandOverTests` (T9.1 trigger at end tile, T9.2 ramp only while standing 3-5 s, T9.3 S6 requested once + beat, T9.4 no simulated frame > 33 ms; evidence `evidence/P2-B09/handover_sim.json`).
- Phone, deferred (phone in headset): Lung_Airway.unity, run once with `lung.route` = 1 and once with 0 (PH-39). Walk to the end chamber, stand on the glyph, take one step mid-ramp, stand again, wait for S6.
  Expected: curvature changes only while standing; ramp 3-5 s of standing (4.0 s set); S6 loads; no frame > 33 ms from end-chamber entry to S6 + 2 s (Frame Timing Stats max). Record max, p95, ramp time.
- Deviations: DV-B09-1 route is a constructor value (AirwayParams/Mode 2 from P2-B04/B05 not on this branch; Mode 2 path S6-at-entry not built); DV-B09-2 AirwayBeat wraps AirwayWing + LesionHandOver (AirwayWingModule is B05).


# Lane B phone and headset checklists

Each section: scene, steps, expected numbers. Status DEFERRED means built and ready but not yet run on the phone.

## P2-B10 Needle sky, with the P2-B11 great-circle check on the device (DEFERRED; gate G10)

Scene: `Assets/Atlas/Blades/Lung/Scenes/Lung_NeedleSkyTest.unity` (component `NeedleSkyTestScene`, settings `Assets/Atlas/Blades/Lung/Sky/NeedleSkySettings.asset`). Case: `StreamingAssets/cases/illustrative` (placeholder fixture of P2-D08 until lane D's real package lands; re-run then on both packages). Phone A in the headset, Release build, Frame Timing Stats on, thermal state noted at the start.

Build: in the on-request build slot set Build Profiles to [Lung_NeedleSkyTest.unity (index 0), Atlas_Boot]. Build and install. Do not commit ProjectSettings: run `git checkout ATLAS/ProjectSettings` afterwards. Run `git lfs pull` before building.

Steps:
1. Clearance sweep (P2-B10 acceptance). Stand still. For 20 s the scene steps through the 20 sky.json check directions, one per second; the panel shows "Clearance X mm" and the line under it shows "Sweep k/20: sky.json Y mm". Note 3 pairs X/Y by eye. Pass: the scene ends with "Sweep: 20/20 within 0.1 mm", and the 3 noted pairs differ by <= 0.1 mm.
2. Orientation (fixture field 12 + 10·sin 2az·cos el mm). Face the anchor frame's +z (on the placeholder rig: the direction the phone faced when the AR session started). At eye level, 45° to the LEFT (az -45°) and 135° to the right (az +135°): dark red (clearance about 0.6-3.4 mm, blocked). 45° to the RIGHT (az +45°) and 135° to the left: green. Straight up and straight down: green.
3. Layers and share. The panel shows "Robust windows 78.4 % of the sky" (fixture value 78.39 %). Operator, if the Console is available: lung.skyRobust = 0 removes the green; lung.skyPhase 0→7 visibly grows the red lobes (phase 7 is the minimum).
4. Gaze-dwell pick (beat g twin "look + dwell"). Hold the cursor ring on a green area: after 0.7 s a white marker appears, the debug line shows `sky_pick` with robust true, and its clear_mm equals the panel value. Holding still does not pick again; a second pick needs a look-away and another 0.7 s hold.
5. Great-circle check (P2-B11 on the device). Pick (az 0°, el 0°), (az 90°, el 0°) and straight up. Pass: three white arcs join the markers along great circles; the panel shows "Triangle angle sum" within 270 ± 5° (head-aimed picks), the debug line shows `gc_check`, and the value is > 180°.
6. Budget (MP §10.2.5, §8.5). With the sky visible, look around for 60 s. Pass: no frame over 33.8 ms; 60 fps; GC allocation 0 B per frame in steady state (Profiler or telemetry gcAllocB); the sky adds <= 4 draw calls per frame (sphere + text, both eyes); an Xcode GPU capture shows no compute encoder.
7. BLOCKED until lane C P1-C10 (live IActionSource): stick + A twin; move the cursor with the stick and press A; the pick lands at the cursor, not at the gaze.
8. BLOCKED until lane D P1-D03/P2-D10: Console twin; set lung.skyPickAz/El and bump lung.skyPick; a pick appears at that direction within 250 ms.

Record: sweep count, the 3 noted pairs, the share shown, the angle sum, frame p95 and max, thermal state.

Graphics sign-off (Editor with a graphics device, not -nographics): run `B10_Shader_RawOrientation_GpuOnly` and `B10_Textures_R8_Linear_PointClamp_Once` (both Inconclusive headless), and check the console for NeedleSky.shader compile errors.

## P3-B03 Wide source render and composed final blit (deferred: phone in the headset not needed for the < 1 px test; fusion needs headset)

- Scene: `Assets/Atlas/Blades/FieldFit/Scenes/Blade_FieldFit.unity` over `Space_Home` (S0), `budget_room`.
- Editor GPU test (run once on a machine with a graphics device, not `-nographics`):
  `FieldFitBlitTests.LensOff_RealShader_MatchesReference_GPU`. Expected: max uv difference vs the CPU mirror < 2e-3; top-row green > 0.5.
- Headset: checklist items 1-3. Expected: no jump or flip with None; black at about 10 deg; one fused image in 3 of 3 testers (else `centreEyeSource` = true).

## P3-B04 Field-loss mask and slider (deferred: headset look only)

- Scene: `Blade_FieldFit.unity`; mask assigned in `FieldFitSettings.fieldLossMask` with `maskHalfWidthDeg` matching lane D's preset.
- Steps: profile None with tunnel, then drag `lens.t` 0 -> 1 with Morph. Expected: black edge at about 10 deg (+-0.2); no visible pop while dragging; `LutRebuild` p95 reported (checklist item 10).

## P3-B05 Lens guard (deferred: phone in the headset)

- Scene: `Blade_FieldFit.unity` over `Space_Home`; badge font assigned in `FieldFitSettings.asset`.
- Editor GPU tests to run once with a graphics device: `LensGuardTests.Badge_VisibleWhileActive`, `Badge_FromFont`.
- Headset: checklist items 4-6. Expected: "LENS ON" readable in both eyes (None, Morph, Tanh), none when off; 10 of 10 auto-off trials (cause `displacement`, dPrev_m <= 0.5 < d_m, first frame over 0.5 m); tap-off in the tap frame; Console stop within 250 ms.

## P3-B06 Blind-Spot Finder (DEFERRED)

Scene: `Assets/Atlas/Blades/FieldFit/Scenes/FieldFit_BlindSpotTest.unity` (component `BlindSpotTestScene`). Phone A in the headset, Release build, note the thermal state at the start.

Preconditions: real P1-A04/P1-A05 rig with per-eye blank, P1-A06 shared look, P1-C10 confirm action and lane D event sink, all merged (`BlindSpotPlaceholderSeams.TryResolve` must return true; today it reports the look and tap seams as missing).

Build: in the on-request build slot set Build Profiles to [FieldFit_BlindSpotTest.unity (index 0), Atlas_Boot]. Build and install. Do not commit ProjectSettings: run `git checkout ATLAS/ProjectSettings` afterwards.

Steps:
1. The tester stands at least 1.5 m from walls and furniture, facing open floor, Bump Guard on. Standing or seated.
2. The tester presses A or clicks the lever.
3. The tester keeps the eye on the cross and taps when a dot is seen. A run takes about 2.5 min.
4. Read `blindspot_found.t_find_s` and the `blindspot_done` fields from the Hub's `logs/<session>/events.jsonl`. If the Station is down, the tester reads the result screen aloud.
5. 5 testers, one run each; the first run counts.

Pass: `t_find_s <= 30` for at least 4 of 5 testers. Record per tester: t_find_s, centre, radius, catch_fp / catch_trials, duration_s, thermal state.
Also check (G13): no string makes a clinical claim. If the shared shader draws grid lines or fog on the backdrop or cross, report it as a lane-A contract issue.

## P3-B07 Scotoma River, Extended (DEFERRED; feeds no gate)

Scene: the same `FieldFit_BlindSpotTest.unity`, after a P3-B06 run (result screen, then A or click shows the river text).

Steps: after the P3-B06 run press A; the tester keeps the eye on the cross.

Pass: on the device, `river_start.min_r_over_a >= 0.999999` and `river_start.scale_above` in [0.48, 0.52] (read from events.jsonl).
Also record (qualitative): whether the text visibly parts around an empty region on the temporal side.

## P3-B08 Phone performance pass (deferred: phone in the headset, Station, Xcode)

- Scene: `Assets/Atlas/Blades/FieldFit/Scenes/Blade_FieldFit_Soak.unity` (FieldFit + `FieldFitSettings.asset`), loaded additively over `Space_Home` with `budget_room`; lane A adds it to the build list.
- Build: Release, Frame Timing Stats on, debugger detached, Metal API Validation off, brightness about 40 %, phone A closed headset, thermal Nominal at start.
- Steps and the table to fill: checklist items 7-10 in `docs/laneB/checklists/P3-B03-B05-B08_phone.md`.
- Expected numbers (Master Plan §10.4.8, §11.2 Must): FieldFitBlit < 1 ms (Xcode, mean of 3 frames); GPU p95 <= 9 ms cold; frame time p50/p95/p99 <= 16.7/20/33.3 ms; over 17.2/33.8 ms <= 5 %/0.5 %; CPU main/render p95 <= 8/6 ms; draw calls <= 300, set-pass <= 60, triangles <= 500k; memory <= 1.5 GB; GC <= 1 kB per frame; soak 15 min: no Critical, Serious not before 10 min, hot fps mean >= 45 and never < 30. Reported only: `Atlas.FieldFit.LutRebuild` p95 while `lens.t` is dragged.
- Overrun: step to 1,280^2 or 2x MSAA, re-measure, record both, change no number.

## P2-B12 Airway run test (Editor tests pass; phone run deferred: phone in headset, 10 naive walkers)
- Editor: `AirwayRunTests` (T12.1 centre strip, Mode 1, 3 rooms x 0.7/1.0 m/s, tree.json route plus the 100 routes: 0 straight-side WARN starts, 0 STRONG, via the test-only `BumpWarnOracle`; corner cues counted, not judged; T12.2 run report; S12.1 bridge). Evidence `evidence/P2-B12/sim_centre_strip.json`, `run_report_sim.json`.
- Scene: Lung_Airway.unity, Mode 1, `lung.route` = 0, beacon rescue enabled (P2-B06); FMS asked each minute (lane C).
- Steps per run: a walker who did not build the airway walks from the trachea chamber to the lesion chamber. Export events.jsonl; build the report with `AirwayRunReport.FromJsonLines(lines, targetAddress)`; store as `docs/laneB/evidence/P2-B12/phone_run_<n>.json`.
- Expected: RescueFree in >= 9 of 10 runs; FmsMax <= 3 (FMS <= 3/20) in every run; on the straight sides (head > 0.5 m from a lap corner when the cue starts) no Bump Guard WARN over the first 8 tile steps (real Bump Guard, Console cue log).
- PH-39: the route has TargetAddress.Length forks (fixture 6), not 4. Report the measured run with that route; the "4 forks" criterion stays open until PH-39 is decided. It is not claimed here.
