# FieldFit Blind-Spot Finder (P3-B06)

Master Plan §10.4.4 beat 1; Spec §11.5. Assembly `Atlas.Blades.FieldFit`, namespace `Atlas.Blades.FieldFit.BlindSpotFinder`,
tests in `Tests/FieldFitBlindSpot` (`Atlas.Blades.FieldFit.BlindSpot.Tests`).

## Seams (BlindSpotSeams.cs; only BlindSpotPlaceholderSeams.cs names lane A/C/D types)

| Seam | Lane | Placeholder status (`laneB/placeholders`) |
|---|---|---|
| `IBlindSpotRig` | A P1-A04/A05 | `StereoRig.Current.LeftEye/RightEye` exist; no per-eye blank, so the camera fallback is used (cullingMask 0, SolidColor, black; restored on un-blank). Deviation D17. |
| `IBlindSpotLook` | A P1-A06 | BLOCKED BY PLACEHOLDER: no shared AtlasSurface material is exposed and no bright/dark swatch is named. |
| `IBlindSpotTap` | C P1-C10 | BLOCKED BY PLACEHOLDER: `IActionSource` has no live-instance accessor, `GamepadActionSource` is internal, `GazeTarget` is dwell-only (D7). |
| `IBlindSpotEvents` | D P1-D04 | `EventLog.Emit(scene, ev, dataJson)`; adapter passes scene "fieldfit" and `JsonUtility.ToJson(data)`. |

`TryResolve` therefore returns false today with `missing` naming the look and tap seams. Tests use fakes through `BlindSpot.Bind`.

## Events (App. B Event {t_us, scene, ev, data}; `az_deg` signed, + = right, lit-eye frame)

| ev | data |
|---|---|
| `blindspot_start` | `{eye, seed}` |
| `blindspot_probe` | `{index, kind (grid/reprobe/catch), az_deg, el_deg, onset_s, seen, rt_ms (-1 if not seen)}` |
| `blindspot_found` | `{az_deg, el_deg, t_find_s}` |
| `blindspot_done` | `{eye, found, aborted, centre_az_deg, centre_el_deg, radius_deg, t_find_s, missed, catch_trials, catch_fp, trials, duration_s}` |

No NaN or Infinity is ever emitted (`found:false`, -1 and 0 instead).

## Constants

Grid 8..22 deg temporal, -8..8 deg vertical, step 2 deg: 72 points; priority band = within 5 deg of (15, 0): 22 points.
Timing: lead-in 1.0 s, dot 0.2 s, response 0.1..1.5 s, ISI 0.5 + 0.5 u s, a catch after every 4 non-catch trials since found.
Stimulus: dot at 1.0 m, diameter 0.43 deg; cross 1.0 deg, 0.15 deg thick; backdrop at 1.2 m, half-angle 61 deg.

## Deviations

| # | Deviation |
|---|---|
| D1 | Grid step 2 deg (72 points). The 22-point priority band is presented first. |
| D2 | Stimulus timings, Goldmann III dot, cross size and distances as above; the plan is silent. |
| D3 | Found = missed on first presentation and on an immediate re-probe. A catch is a re-probe of the found point on every 5th trial after found. A false positive is a tap in a catch window. |
| D4 | Centre = mean of the confirmed misses. a = equivalent-area radius. |
| D5 | Right eye, as a code constant. No registry parameter. |
| D6 | The lit eye sees a dark head-locked backdrop at 1.2 m, which hides Home. Walkers stay at least 1.5 m from objects in view. |
| D7 | "Seen" has the gamepad A and headset-lever twins only. Gaze dwell would break fixation (precedent: Shadow action, §10.1.3). |
| D8 | Event names and payloads as above. |
| D10 | Separate test assembly, public API only, sub-namespaces. Test namespace is `Atlas.Blades.FieldFit.BlindSpotTests` because `BlindSpot.Tests` would collide with the class `BlindSpot`. |
| D11 | asmdef gains `Unity.TextMeshPro`. |
| D12 | The G7/P1-A04 dependencies (§10.4.8) differ from G11 (§9.7). Built against placeholders. |
| D13 | Test scene is outside the build list; the checklist builds it locally. |
| D14 | Angles are in eye-render space. No calibrated-optics or clinical claim. |
| D15 | The full grid is presented, so a run takes about 2.5 min. |
| D16 | `BlindSpot` is `[ExecuteAlways]` so OnDisable/OnDestroy restore the eye in EditMode tests. |
| D17 | Camera-based eye blanking fallback in the placeholder rig adapter (allowed for blanking only). |
| D18 | The placeholder `EventLog.Emit` takes a scene string; the adapter passes "fieldfit". |
