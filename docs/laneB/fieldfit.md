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
