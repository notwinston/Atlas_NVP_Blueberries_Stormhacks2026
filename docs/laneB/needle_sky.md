# Needle sky (P2-B10, P2-B11): deviations, assumptions, evidence

Code: `ATLAS/Assets/Atlas/Blades/Lung/Sky/` (assembly `Atlas.Blades.Lung`, namespace `Atlas.Blades.Lung.Sky`). Tests: `ATLAS/Assets/Atlas/Tests/LungSky/` (assembly `Atlas.Blades.Lung.Sky.Tests`). Source: G7 brief; Master Plan §10.2.

## Deviations
- D1 Dedicated shader `Atlas/NeedleSky` instead of `AtlasSurface` (data textures, no keyword, own SRP-Batcher block, 1 draw per eye). Announce to lane A.
- D2 Shader ids in `NeedleSkyShaderIDs`, not `AtlasShaderIDs.cs`.
- D3 Sky frame = Unity world axes = anchor frame (A2); az = atan2(x, z), el = asin(y); equirectangular, row 0 at the top; patient frame identity (PH-37, lane D to confirm).
- D4 Nearest-pixel lookup on the CPU, point sampling on the GPU.
- D5 Dwell cone half-angle 1.5 deg (Spec §11.3.3 3 deg disc informative); re-arm after leaving the cone.
- D6 Stick 90 deg/s, deadzone 0.2, el clamp +-89 deg; cursor re-attaches to the gaze after a pick.
- D7 Picks closer than 3 deg to a pick or its antipode are rejected; at most 3 per entry.
- D8 Console twin through `lung.skyPickAz/El` + counter `lung.skyPick`; layers are operator parameters `lung.skyPhase/skyRobust`.
- D9 Completed 5 s after the third pick (Spec §11.3.3 "(5 s)").
- D10 Colours are material constants, not palette cells (PH-9).
- D11 Sphere radius 2.0 m with ZWrite on (world content beyond 2 m hidden during beat g).
- D12 Head-locked panel, 15 deg below centre at 1.0 m.
- D13 `Tick(in FrameContext)` is public beyond `ILungBeat`; the test-scene host reads `StereoRig.Head` itself.
- D14 Area by Van Oosterom-Strackee (B11).
- D15 `sky_pick.clear_mm` is the 0.1-rounded displayed value; `gc_check` unrounded.
- D16 A robust byte counts as robust when non-zero.
- D17 Robust share uses exact equal-area row weights.
- D18 Separate test assembly `Atlas.Blades.Lung.Sky.Tests`.
- D19 Case root passed in; the test scene uses "illustrative".
- D20 P1-B03 x laneB/placeholders merge: AtlasWarp.hlsl(+meta), Shaders.meta, AtlasShaderIDs.cs.meta take ours; AtlasShaderIDs.cs = P1-B03 plus the 5 lane-A ids as a commented block.
- D21 [min] Time comparisons (dwell 0.7 s, complete 5 s) use slack 1e-6 s instead of 1e-9: dt is a float and 0.01f accumulated 70 times is 0.69999998 s.
- D22 [min] `NeedleSkyMesh` has 960 triangles, not 1,024: the two pole rows emit one triangle per quad (the other would be degenerate and fail the inward-winding test). Limit 1,100 holds.
- D23 [min] `NeedleSkyBeat.Tick` calls `presenter.SetVisible(true)` each tick while the sky is on, so toggling `lung.skyOn` back to 1 shows it again without a new Enter.
- D24 [min] During the test-scene 20 s sweep the 1-s holds also dwell-pick (up to 3 `sky_pick` events); the final `Enter()` resets them.
- D25 [min] B10 omits arcs in the shader and the angle-sum line; B11 adds them (`ArcsOn` is false in B10).

## Cross-lane announcements (a human relays, MP §9.9 rule 2)
- Lanes A and D: `Atlas.Blades.Lung.asmdef` gained `Unity.Mathematics` and `Unity.TextMeshPro`.
- Lane A: A-G7-1 the director calls `NeedleSkyBeat.Tick(in FrameContext)` at §6.3 step 5 while beat G is active, and constructs the beat with `SpaceLab.Registry`, the case root and `NeedleSkySettings`. New shader (D1).
- Lane C: a live `IActionSource` accessor is needed for the stick + A and lever twins.
- Lane D: P2-D10 panel parameters `lung.skyOn`, `lung.skyPhase`, `lung.skyRobust`, `lung.skyPickAz`, `lung.skyPickEl`, `lung.skyPick` (send az and el before bumping the counter); sky.json field names as read; `checks` must hold the values of the equirectangular pixel of each check direction (quantised), otherwise the 0.1 mm tolerance is at the R8 half-step edge.

## Evidence
- `docs/laneB/evidence/P2-B10/editmode_results_red.xml` (red run on throwing stubs), `editmode_results.xml` (green).
- Headless limits: `B10_Shader_RawOrientation_GpuOnly` and `B10_Textures_R8_Linear_PointClamp_Once` are Inconclusive under -nographics, and the shader is not compiled there. Graphics sign-off run needed (see TESTING.md).

## P2-B11 Great-circle check
- Delivered: `SphericalTriangle` (atan2 vertex angle, Van Oosterom-Strackee area, Girard sum), `GreatCircleArc`, `GreatCircleCheck`, model `Check` + `gc_check` on the third pick (unrounded, D15), panel line "Triangle angle sum", shader arcs (`_ArcsOn`, half width 0.25 deg), `ArcsOn` in the frame.
- Acceptance (Editor): octant triangle 270 deg (within 1e-9), `gc_check` via the Console twins, 1,000 random triangles within 0.5 deg of Girard (max in the test log line "B11 max |sum - Girard|"), vertex angle equals the kernel `Geometry2.Log`/`Iso2.Boost` angle at kappa = +1 to 1e-9.
- Evidence: `docs/laneB/evidence/P2-B11/editmode_results_red.xml` (red on stubs), `editmode_results.xml` (green: 121 tests, 118 passed, 2 Inconclusive GPU, 1 Skipped pre-existing).
- Deferred: on-device arcs and angle sum (TESTING.md step 5); the arc shader is not compiled headless (graphics sign-off).
