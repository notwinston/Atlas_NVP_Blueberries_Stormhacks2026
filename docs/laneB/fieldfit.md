> Written on the branch `laneB/integration`, against the placeholders of lanes A, C and D. Where this log names a placeholder (`PLACEHOLDER`, `IActionSource`, `HomeRebuild`, `EventLog.Emit`, `AtlasSurface`, `_Gates`, `room_*.json`, `SpaceLab.Home`), the real code has replaced it, except the Phase 2 and 3 stand-ins in [placeholders.md](placeholders.md): see [INTEGRATION_NOTES.md](INTEGRATION_NOTES.md). The numbers of the contract sections it cites ("kernel.md §n") are the numbers of [../contracts/lane-b-kernel.md](../contracts/lane-b-kernel.md).

# FieldFit (Lane B) contract page

Sources: Master Plan §10.4, Build Pipeline P3-B01..B08, Engineering Spec §11.5 (lens maths only, App. D.3).

## LensMath API (P3-B01) for lane D (P3-D03)

`Assets/Atlas/Kernel/Lens/LensMath.cs`, namespace `Atlas.Kernel`, assembly `Atlas.Kernel`.
Unity.Mathematics only; `double`; angles in **radians**; Burst-compatible. Registry values (`lens.*`) are
degrees and are converted by the caller.

- `LensProfile { None = 0, Morph = 1, Tanh = 2 }` (3 reserved: Teichmueller, Stretch, P3-D05).
- `LensParams(profile, e, rho0, e1, t)`: `Default` (Morph, 54 deg, 10 deg, 4 deg, T = 1), `IsValid`
  (finite; 0 < e1 < rho0 < E < pi/2; 0 <= T <= 1; profile 0..2), exact `Equals`.
- `LensMath.Stereo/InvStereo`, `S`, `Sigma0`, `S1`, `KStar`, `SchwarzBound`,
  `SigmaUniform/Power/Morph` and inverses, `TanhForward/TanhInverse`.
- `Forward(e, p)`: source angle to shown angle (None: identity).
- `Inverse(eOut, p)`: exact; Morph with 0 < T < 1 by 64-step bisection; +inf beyond rho0 for Morph and Tanh.
- `Dilatation(e, p)`: K(e) >= 1 at source angle e (Master Plan §7.1). Default T = 1: K = 1 for e <= e1, K* = 2.9184646 beyond.

Reference values (Default): S = 1.0190509, sigma0 = 0.1749773, s1 = 0.0698415, K* = 2.9184646;
tanh K(30 deg) = 35.03.

## Deviations

| Id | Note |
|---|---|
| DV1 | `LensMath` lives in `Atlas.Kernel` (§6.2 forbids Spectator -> Blades). Tell lane D (P3-D03). |
| DV-B01-a | `Atlas.Blades.FieldFit.Tests.asmdef` omits the `Unity.TextMeshPro` reference: the package is not in `Packages/manifest.json` (not lane B's to edit). Add it when B05 needs it and the package is present. |

## Lookup builder (P3-B02)

`Assets/Atlas/Blades/FieldFit/LensLut.cs`: `LensLut` (two RGHalf 512^2 textures, Clamp, Point, readable, stable instances),
`IEyeGeometry`, `LensLutJob` (Burst, `CompileSynchronously`, Strict). `SetEyeGeometry` is stage A (managed, once at enable);
`Rebuild(in LensParams, FieldLossMask)` is stage B. Lost texels store (-1, -d), d in [0, 4] texels. Profile None stores
continued uv clamped to [-0.5, 1.5] (DV5, DV7). Measured (Editor, Burst, both eyes): median 0.62 ms, max 0.64 ms (limit 16 ms);
round-trip max 0.0497 deg (limit 0.1 deg). Evidence: `docs/laneB/evidence/P3-B02/`.

Asmdef: `Atlas.Blades.FieldFit` now references Unity.Mathematics, Unity.Burst, Unity.Collections (shared with lane D: announce).

| Id | Note |
|---|---|
| DV-B02-a | `None_Identity` bound is 0.06 deg, not the brief's 0.05 deg: RG16F half-ulp at uv about 0.5 is 0.0385 deg per axis, 0.054 deg diagonal (the brief's own round-trip figure), so 0.05 cannot hold for any half-precision lookup. Measured 0.0527 deg. Plan numbers (0.1 deg round trip) untouched. |
| DV-B02-b | `FieldLossMask` is the Absent stub; `Rebuild` ignores the mask until P3-B04. The mask margin terms are not implemented. |
| DV-B02-c | For Morph 0 < t < 1 the job calls `LensMath.Inverse` (64-step bisection, exact) as a stand-in; P3-B04 swaps in `LensRadialTable` (DV16). |
| DV-B02-d | `FieldFitRig.cs` ships only `QualityRung` and `IFieldFitRig` (needed by the `FakeRig` double); P3-B03 appends `PlaceholderRigBinding`. |
| DV-B02-e | Tests written together with the implementation after a compile-red run (the red phase was a compile failure only). Test-first red for each assertion was not run. |

## Wide source render and composed final blit (P3-B03)

Files (all `Assets/Atlas/Blades/FieldFit/`): `FieldFitFeature.cs` (`FieldFitFeature`, `FieldFitShaderIDs`), `FieldFitSettings.cs` + `.asset`,
`FieldFitRig.cs` (`PlaceholderRigBinding`, the only file naming lane A), `LensParamBinding.cs` (the only file naming lane D),
`LensGuard.cs` and `LensBadge.cs` (stubs, B05 fills them), `Shaders/FieldFitBlit.shader`, `Scenes/Blade_FieldFit.unity`.

- `FieldFitFeature` is a MonoBehaviour. `OnEnable` builds the adapters, `Init(rig, src, ev)` does Brief §8 "Enable" (stage A, ten source
  RTs for the five rungs, the lookup, the material from `settings.blitShader`, the badge, `Application.onBeforeRender`).
- Integration at lane A / lane D: call `LensParamBinding.BindRegistry(registry)` once at boot (DV-B03-f). Without it, or without
  `lens.*` registered by P3-D02, the feature logs one error and stays disabled (the ordinary path is untouched).
- Measured (Editor, CPU mirror of the shader, identity and D_test, 3.2 M interior pixels): max displacement 0.539 px vs the ordinary blit (limit 1 px; the brief expected about 0.45).
  Evidence: `docs/laneB/evidence/P3-B03/lens_off_displacement.txt`.
- Tests: `FieldFitFeatureTests` (9), `FieldFitBlitTests` (1 CPU, 1 GPU ignored on `-nographics`).

| Id | Note |
|---|---|
| DV-B03-a | `FieldFitSettings` has no `badgeFont` (TextMeshPro is not in the manifest). `LensBadge.Create(left, right, lut)` therefore takes no font; B05 needs a lead decision. |
| DV-B03-b | R5 (frustum): the placeholder `StereoRig` exposes no frustum; the adapter derives (l, r, b, t) from `Camera.projectionMatrix` of the public `LeftEye` / `RightEye`, once at enable. |
| DV-B03-c | R7 (rung): the placeholder has no ladder; the adapter maps `AtlasThermal.State` 0..3 to Nominal..Last. High is never produced until lane A's ladder exists. |
| DV-B03-d | R6 (override): the placeholder `OverrideEyeTargets(left, right, vFovDeg)` has no lateral offset and keeps its explicit camera aspect. The adapter moves each eye camera's local x to the requested offset, calls `Camera.ResetAspect()` for the square source, and restores both on clear. Lane A's real R6 must do this itself. |
| DV-B03-e | R3 (eye screen size): `Screen.width / 2` x `Screen.height`, 1398 x 1290 when no screen. |
| DV-B03-f | Registry hand-over by the public static `LensParamBinding.BindRegistry` (the placeholder `SpaceLab` has no static accessor). |
| DV-B03-g | GPU tests need a graphics device. The shared runner passes `-nographics`, so they call `Assert.Ignore` there instead of failing (Brief §2 says fail); the CPU mirror `BlitReference` of the shader runs the same criterion everywhere. Run `FieldFitBlitTests` once without `-nographics` before G13. |
| DV-B03-h | `lens.on` handling: if the lookup was never built (invalid parameters on activation) the rig is left on the ordinary path. |
| DV-B03-i | Tests are written together with the implementation; the red phase was a compile failure (missing types), as in B02. |

## Field-loss mask and slider (P3-B04)

`FieldLossMask.FromTexture(tex, halfWidthDeg)` (R channel, threshold 0.5, Felzenszwalb signed EDT, boundary half-way between pixels, radians),
`FieldLossMaskData.Margin/MarginStatic`, mask terms phi_mask and phi_out in `LensLutJob`, `LensRadialTable` (1,024 samples) for Morph 0 < t < 1 (DV16),
`lens.t` read by `RegistryLensSource` (already in B03). Measured (Editor, CPU mirror of the blit rule, D_test, 720 rays):
tunnel edge max error (a) None+mask 0.082, (b) Morph+mask 0.081, (c) Morph no mask 0.057, (d) lane D placeholder preset 0.131 deg (limit 0.2);
radial table inverse worst 0.0067 deg (limit 0.02); partial-morph round trip 0.039 deg (limit 0.1); rebuild median 0.58 ms. Evidence: `docs/laneB/evidence/P3-B04/` (`ring_test.png` has red circles at 9.8 and 10.2 deg).

| Id | Note |
|---|---|
| DV-B04-a | The shader edge rule is unchanged (no defect found). |
| DV-B04-b | One line in `FieldFitFeature.Init` (owned by B03) builds the mask from `settings.fieldLossMask`; nothing else of B03's files changed. |
| DV-B04-c | Config (d) treats lane D's placeholder `tunnel10.png` (4 px/deg, README) as M = 64 deg, not the DV6 M = 30 deg: lane D and lane B must agree on one (set `FieldFitSettings.maskHalfWidthDeg` accordingly). `FieldFitSettings.asset` leaves `fieldLossMask` empty (StreamingAssets file, lane D's). |
| DV-B04-d | The ring test scene is the CPU mirror of the shader (DV14, DV-B03-g). |

## Lens guard (P3-B05)

`LensGuard` (3D distance from the head at lens-on, strictly > 0.5 m, tap flag), `LensBadge` (TextMeshPro mesh drawn once with `DrawMesh` into a
256 x 64 sRGB target; rect from `LensLut.InvertTangent`: nearest stage-A texel, then Newton on the bilinear interpolant; bounding box of the
images of the 8 samples of the +-4 deg x +-1 deg box at 15 deg below fixation), `FieldFitSettings.badgeFont` (assigned to LiberationSans SDF).
Measured (Editor): auto-off 10 of 10 simulated trials in the first frame over 0.5 m (`docs/laneB/evidence/P3-B05/auto_off_sim.txt`).

### Integration at G13

- Tap-off: `Atlas.Input` (placeholder) offers `IActionSource.Triggered(AtlasAction.Confirm)` but no static instance, so lane B ships `FieldFitFeature.TapOff()`.
  One-line binding for lane A/C: `if (actions.Triggered(AtlasAction.Confirm)) fieldFit.TapOff();` in the frame loop, before `onBeforeRender`.
- Lane A calls `LensParamBinding.BindRegistry(registry)` at boot (B03).

| Id | Note |
|---|---|
| DV-B05-a | `Unity.TextMeshPro` is referenced by `Atlas.Blades.FieldFit` and its tests. The brief says the package is absent from the manifest; it is present transitively (`com.unity.ugui` 2.0.0, TMP Essentials already in `Assets/TextMesh Pro`), so no manifest edit was needed. |
| DV-B05-b | The blit composites the badge as premultiplied alpha (`col*(1-a) + rgb`) because the TMP distance-field shader writes premultiplied colour. A one-line change to the B03 shader; a white opaque badge is unaffected. |
| DV-B05-c | Test hook `FieldFitFeature.TestBadgeTexture` gives a ready badge without a font; the real-font path is tested in `Badge_FromFont` (ignored without a graphics device). |
| DV-B05-d | `Badge_VisibleWhileActive` and `Badge_FromFont` need a graphics device and are ignored on the `-nographics` runner (DV-B03-g). |

## Phone performance pass (P3-B08)

Deliverable here: the soak scene `Blade_FieldFit_Soak.unity` (same content as `Blade_FieldFit`) and the ready-to-run checklist (items 7-10) with the results
table; nothing is measured yet (needs phone A in the headset, Xcode and the Station). The Editor-side proxies are in the evidence of B02-B05
(rebuild 0.58 ms median; zero managed allocation per frame over 600 frames; lens-off displacement 0.54 px).
