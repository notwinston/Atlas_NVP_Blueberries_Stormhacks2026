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
