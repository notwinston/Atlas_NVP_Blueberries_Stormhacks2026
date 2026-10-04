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
