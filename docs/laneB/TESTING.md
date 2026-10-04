# Lane B phone and headset checklists

Each section: scene, steps, expected numbers. Status DEFERRED means built and ready but not yet run on the phone.

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
