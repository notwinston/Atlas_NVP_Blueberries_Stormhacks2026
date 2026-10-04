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
