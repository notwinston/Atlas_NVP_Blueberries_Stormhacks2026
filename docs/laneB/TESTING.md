# Lane B phone and headset testing (deferred acceptance)

One section per task: scene, steps, expected numbers. A human fills in the measured values. The master list is
`docs/laneB/checklists/P3-B03-B05-B08_phone.md`.

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

## P3-B08 Phone performance pass (deferred: phone in the headset, Station, Xcode)

- Scene: `Assets/Atlas/Blades/FieldFit/Scenes/Blade_FieldFit_Soak.unity` (FieldFit + `FieldFitSettings.asset`), loaded additively over `Space_Home` with `budget_room`; lane A adds it to the build list.
- Build: Release, Frame Timing Stats on, debugger detached, Metal API Validation off, brightness about 40 %, phone A closed headset, thermal Nominal at start.
- Steps and the table to fill: checklist items 7-10 in `docs/laneB/checklists/P3-B03-B05-B08_phone.md`.
- Expected numbers (Master Plan §10.4.8, §11.2 Must): FieldFitBlit < 1 ms (Xcode, mean of 3 frames); GPU p95 <= 9 ms cold; frame time p50/p95/p99 <= 16.7/20/33.3 ms; over 17.2/33.8 ms <= 5 %/0.5 %; CPU main/render p95 <= 8/6 ms; draw calls <= 300, set-pass <= 60, triangles <= 500k; memory <= 1.5 GB; GC <= 1 kB per frame; soak 15 min: no Critical, Serious not before 10 min, hot fps mean >= 45 and never < 30. Reported only: `Atlas.FieldFit.LutRebuild` p95 while `lens.t` is dragged.
- Overrun: step to 1,280^2 or 2x MSAA, re-measure, record both, change no number.
