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

## P2-B12 Airway run test (Editor tests pass; phone run deferred: phone in headset, 10 naive walkers)
- Editor: `AirwayRunTests` (T12.1 centre strip, Mode 1, 3 rooms x 0.7/1.0 m/s, tree.json route plus the 100 routes: 0 straight-side WARN starts, 0 STRONG, via the test-only `BumpWarnOracle`; corner cues counted, not judged; T12.2 run report; S12.1 bridge). Evidence `evidence/P2-B12/sim_centre_strip.json`, `run_report_sim.json`.
- Scene: Lung_Airway.unity, Mode 1, `lung.route` = 0, beacon rescue enabled (P2-B06); FMS asked each minute (lane C).
- Steps per run: a walker who did not build the airway walks from the trachea chamber to the lesion chamber. Export events.jsonl; build the report with `AirwayRunReport.FromJsonLines(lines, targetAddress)`; store as `docs/laneB/evidence/P2-B12/phone_run_<n>.json`.
- Expected: RescueFree in >= 9 of 10 runs; FmsMax <= 3 (FMS <= 3/20) in every run; on the straight sides (head > 0.5 m from a lap corner when the cue starts) no Bump Guard WARN over the first 8 tile steps (real Bump Guard, Console cue log).
- PH-39: the route has TargetAddress.Length forks (fixture 6), not 4. Report the measured run with that route; the "4 forks" criterion stays open until PH-39 is decided. It is not claimed here.
