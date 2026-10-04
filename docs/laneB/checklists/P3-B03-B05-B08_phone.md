# Phone checklist: P3-B03, P3-B05, P3-B08 (FieldFit)

Master Plan §10.4.3, §10.4.5, §10.4.9, §11.2; Brief §12. A human runs it (Master Plan §9.9 rule 5) and fills in the numbers.
Status: DEFERRED (needs phone A in the closed headset). Items 1-3 belong to P3-B03; 4-6 are completed by P3-B05, 7-10 by P3-B08.

## Prerequisites

- B03, B04 and B05 are merged.
- Lane A has added `Blade_FieldFit.unity` to the build list and calls `LensParamBinding.BindRegistry(registry)` at boot (lane B may not edit `EditorBuildSettings`).
- The tap binding is present (`FieldFitFeature.TapOff()` bound to the Confirm action, see `docs/laneB/fieldfit.md` "Integration at G13"), or item 6 is marked "pending G13 binding".
- Hub and Console are running; the lens panel (D02) or a placeholder is reachable.
- Release build, Frame Timing Stats on, debugger detached, Metal API Validation off, brightness about 40 %.
- Phone A in the closed headset, thermal Nominal at the start (record it: ____).
- Bump Guard is on. Lens beats are standing.
- Scene `Assets/Atlas/Blades/FieldFit/Scenes/Blade_FieldFit.unity` loaded additively over `Space_Home` (S0) with `budget_room`.

## Items

1. **Orientation and jump (B03).** Profile None, then toggle `lens.on` 0 -> 1 -> 0 five times. Inside the central 10 deg the image does not jump, flip or mirror; text on a Home object reads correctly.
   Result: ____ (Y/N). Expected: Y.
2. **Tunnel look (B03).** Profile None: black starts at about 10 deg in both eyes (Y/N ____). Morph t = 1: the room is visibly minified into 10 deg (Y/N ____). Tanh: peripheral slivers appear (Y/N ____).
3. **Fusion, Master Plan §10.4.9 (B03).** Morph t = 1; three testers each fixate an object about 1.5 m straight ahead for 30 s and report one fused image.
   Tester 1 ____, tester 2 ____, tester 3 ____ (Y/N). If any answers N: set `centreEyeSource` = true in `FieldFitSettings.asset`, rebuild, repeat, and record both runs: ____.
4. **Badge (B05).** None, Morph, Tanh: "LENS ON" readable in both eyes for 3 testers; none with the lens off. ____
5. **Auto-off, 10 trials (B05).** Step at least 0.7 m away in a different direction each time (one sit-to-stand). Pass per trial: `lensOff` cause `displacement`, dPrev_m <= 0.5 < d_m, frame = first frame over 0.5 m, view ordinary, Console `lens.on` = 0. Required 10 of 10: ____
6. **Tap-off and Console stop (B05).** Tap-off x3: off in the frame of the tap (cause `tap`) ____. Console stop-lens x3: off within 250 ms (cause `param`) ____.
7. **B08 cold search trial.** Scene `Blade_FieldFit_Soak.unity` (same content as `Blade_FieldFit`; lane A adds it to the build list). Lens on, Morph t = 1, tunnel. The tester stands at the room centre; the operator names 5 RoomModel objects one at a time; trial ends at the fifth "found" or 5 minutes (use D04's SearchTask when available). Thermal Nominal at start: ____.
   From the probe CSV over the trial window (copy to `docs/laneB/evidence/P3-B08/`):

   | Row | Limit | Measured |
   |---|---|---|
   | frame time p50 / p95 / p99 | <= 16.7 / 20 / 33.3 ms | ____ |
   | frames over 17.2 / 33.8 ms | <= 5 % / 0.5 % | ____ |
   | CPU main p95 / render p95 | <= 8 / 6 ms | ____ |
   | GPU p95 (lens on, cold) | <= 9 ms | ____ |
   | draw calls / set-pass / triangles | <= 300 / 60 / 500k | ____ |
   | resident memory | <= 1.5 GB | ____ |
   | managed allocation per frame | <= 1 kB | ____ |

   If the 1,536^2 sources exceed the budget: step to 1,280^2 or 2x MSAA (Master Plan §10.4.9), re-measure, record both; change no number.
8. **Xcode GPU capture** at Nominal, lens on: FieldFitBlit encoder time < 1 ms (mean of 3 frames: ____ / ____ / ____ ms); each source pass resolves colour (MultisampleResolve), discards depth and stencil, no pass loads a multisampled attachment (____); exactly one draw to the screen (____).
9. **Ladder.** Force tiers 0 -> 1 -> 2 -> 3 -> 0 from the Console: source size 1536 -> 1280 -> 1024 -> 1024 -> 1536 and MSAA 4 -> 4 -> 2 -> off -> 4, each within 1 s, GC column 0 (____). Needs lane A's ladder; the placeholder maps thermal state only.
10. **Soak.** 15 minutes, lens on, trials repeated continuously. Thermal timeline: no Critical, Serious not before 10 minutes (____). Hot frame rate mean >= 45, never < 30 (____ / ____). Rung history: ____. Then drag `lens.t` 0 -> 1 over 5 s and record `Atlas.FieldFit.LutRebuild` p95 (reported only): ____ ms.
