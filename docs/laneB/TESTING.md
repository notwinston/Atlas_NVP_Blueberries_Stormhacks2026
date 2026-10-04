# Lane B phone and integration checks (deferred acceptance)

## P2-B02 Corridor and chamber kit (acceptance is Editor; integration check pending other groups)
- Everything in the acceptance cell passes in EditMode: `Atlas.Blades.Lung.AirwayViews.Tests` (kit tests), evidence in `docs/laneB/evidence/P2-B02/`.
- **Integration check** (Editor, once P1-B06, P2-B01 and P2-B03 are merged, in G6):
  1. Run the Play-mode harness in `Space_CurvedWalk` with the fixture tree and the lazy window.
  2. Walk the fixture route on the centre strip (6 forks, PH-39) with looks down to -90 deg.
  3. Read `UnityEditor.UnityStats.drawCalls` per frame for the wing renderers.
  4. Expected: <= 150 per frame (both eyes) at `curved.cullRho` = 3, and <= 106 by the bound.
- **Headset look** (an observation at the P2-B12 run, not acceptance): sibling corridors visible through membranes; no flicker between strip, glyph and floor; no gaps at corridor corners or at tile edges; the valve shell fully closed, with no world visible while it is opaque.

## P2-B09 Lesion chamber hand-over (Editor tests pass; phone measurement deferred)
- Editor: `HandOverTests` (T9.1 trigger at end tile, T9.2 ramp only while standing 3-5 s, T9.3 S6 requested once + beat, T9.4 no simulated frame > 33 ms; evidence `evidence/P2-B09/handover_sim.json`).
- Phone, deferred (phone in headset): Lung_Airway.unity, run once with `lung.route` = 1 and once with 0 (PH-39). Walk to the end chamber, stand on the glyph, take one step mid-ramp, stand again, wait for S6.
  Expected: curvature changes only while standing; ramp 3-5 s of standing (4.0 s set); S6 loads; no frame > 33 ms from end-chamber entry to S6 + 2 s (Frame Timing Stats max). Record max, p95, ramp time.
- Deviations: DV-B09-1 route is a constructor value (AirwayParams/Mode 2 from P2-B04/B05 not on this branch; Mode 2 path S6-at-entry not built); DV-B09-2 AirwayBeat wraps AirwayWing + LesionHandOver (AirwayWingModule is B05).
