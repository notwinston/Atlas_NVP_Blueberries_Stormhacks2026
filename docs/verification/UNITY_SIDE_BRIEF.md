# ATLAS: Unity-side verification brief

For the Claude instance on the Mac that has Unity 6000.3.25f1, Xcode and tool permissions.
Paths in this brief are relative to the repository root: the folder that holds `ATLAS/`, `station/`, `tools/`,
`docs/` and `recordings/`. The code is the truth. Where this brief and the code disagree, trust the code and
write the difference into your report.

Last checked against the code on `phase1-integration` at commit `d1dfcab` plus the integration session's
work in the working tree that was not yet committed when this was written (its files are listed by
`git status`). `phase1-integration` joins `phase1-acd` (lanes A, C, D) and `laneB/integration`, so lane B's
curved spaces, kernel, warp and blades are part of what you check. If `git log` shows later commits that touch
`ATLAS/Assets/Atlas`, trust the code. Step 3i (the tracked hands), risk register 4.13 and the hands' rows of
section 5 were written against `hands2/contracts` on top of `tracked-hands` at `515d14c`; the rest of this brief was
not checked against that commit.

Contents: 0 who you are · 1 rules · 2 tools · 3 the steps (a to i) · 4 risk register · 5 known gaps ·
6 paste this to start.

---

## 0. Who you are and what this is

**Role.** You are the verifier on the build seat. You run the real Unity Editor, then Xcode and a phone. The
session that wrote the code has none of those. It reads `docs/verification/` and fixes what you report.

**State, in ten lines.**

1. All four lanes' Phase 1 code is on this branch: A (platform, look, flat and 4D spaces), B (kernel, warp,
   tile renderer, world generator, S2 Curved Walk, S3 Aligned Cells, S4 Small Planet, the Lung and FieldFit
   blades), C (scan, room, safety) and D (link, Station, Forge). It was written and checked without the Unity
   Editor, without Xcode and without a phone.
2. One walker kernel serves every space: `Atlas.Kernel.HolonomyWalkerKernel`, flat until S2 or S4 configures
   it (`docs/contracts/lane-b-kernel.md` §4.9, §7.2). The platform side's stub kernel, the placeholders of lane B
   and its hand-made scenes are gone.
3. Verified: all 48 C# assemblies compile against the real Unity 6000.3.25f1 and package assemblies through
   `tools/headless/verify.py`: 21 in the iOS player configuration, 21 in the Windows Station configuration (the
   others are Editor-only or reference the Editor) and all 48 in the Editor configuration, with 0 errors; `rules`
   (the reference table, F1 to F7) passes with 0 problems.
4. Verified in Unity 6000.3.25f1 batch mode (EditMode, Metal): the project imports, compiles with 0 errors and passes
   2,114 tests (1 skipped), the Unity-only tests and a compile of every Atlas shader included. Steps below that ask
   for import, compile, shader compile or the Test Runner are therefore re-checks; play mode, the Frame Debugger
   and everything on a device are still first checks.
   Verified headless: 1,995 engine-free NUnit tests run in 24 test assemblies: 1,994 pass, 1 is skipped (the
   `[Ignore]`d PH-39 four-fork test) and none fails (counts per assembly in step 3d; the run is
   `python3 tools/headless/verify.py all --tag docs`, which takes about four minutes).
5. Verified: the Hub (.NET 8), the Forge (Python, procedural generator) and the Console pass their own suites;
   `tools/e2e/run_station_rehearsal.sh` runs a real Hub and a real Forge with no Unity and passes.
6. Never verified: Unity importing the project; the generated scenes (the boot scene, the seven space scenes,
   `PC_Spectator`) and `.meta` files loading.
7. Never verified: the Atlas shaders compiling, above all `Atlas/Surface` with lane B's real `AtlasWarp.hlsl` and
   `AtlasGates.hlsl`; any rendered frame; the Windows Spectator build.
8. Never verified: the tests marked `[Category("Unity")]` of both lanes (step 3d) and the sixteen that the tracked
   hands added (step 3i); the Editor set-up script `AtlasProjectSetup`.
9. Never verified: the native iOS plugins (`AtlasThermal.mm`, `AtlasHaptics.mm`, `AtlasRoomPlan.swift`, `AtlasHands.swift`), an iOS
   build, anything on a device. No curved walk has been done by a person.
10. Never verified: the TripoSG generator (it needs the Station's RTX 5070).

**The single goal.** Find out what is broken in the real engine. Fix what is safe to fix (section 1). Report
the rest.

**The owner's priority.** Walk the full flow on a phone: launch, onboarding in the hand, self-scan, confirm
the outline, headset on, Home rebuilt 1:1 with Bump Guard live, switch spaces, Station watching. Rehearse that
flow first in the Editor with no phone (step 3e).

**The hand-back loop.**

- Findings go into `docs/verification/report-YYYY-MM-DD.md` (one file per day, appended to as you go).
- Pictures go into `docs/verification/shots/`. Logs you keep go into `docs/verification/logs/` (git ignores
  any folder named `logs`, so quote the lines that matter in the report).
- The owner's guide (`documents/ATLAS_Visual_Testing_Start.tex` on `main`) names `docs/evidence/visual/` for
  his own screenshots. Leave what he puts there; yours go to `docs/verification/shots/`.
- Write the report as you go, not at the end. An interrupted session must lose nothing.

Report template (copy it; one row per finding or per passed check):

```markdown
# Verification report YYYY-MM-DD

Unity 6000.3.25f1 · macOS <version> · Xcode <version> · branch phase1-integration @ <short commit> · phone <model, iOS>

## Summary
<three lines: what passed, what is broken, what you fixed>

## Findings
| # | Step | Expected | Observed | Evidence | Fixed or open | Severity |
|---|---|---|---|---|---|---|
| 1 | 3a | No `error CS` in the import log | `Assets/Atlas/...cs(41,17): error CS0117: ...` | logs/import.log line 812 | fixed: <file, what> | blocker |
| 2 | 3e Home | Two eye images side by side | Left eye black | shots/20261004_3e_home_sample_3x3.png | open | major |

## Fixes made (uncommitted unless the owner said to commit)
| File | Change | Why | Re-check that passed |
|---|---|---|---|

## Files Unity wrote
<paths, grouped: commit / leave / ask>

## Not reached
<steps not run, and why>
```

Severity: **blocker** stops the flow or the build; **major** is a wrong picture or behaviour on the owner's
flow; **minor** is anything else that is wrong; **note** is a measurement or an observation with no fault.

Screenshot names: `YYYYMMDD_<step>_<item>[_<room>].png`, lower case, for example
`20261004_3c_framedebugger_home.png`, `20261004_3e_glued_klein_budget_room.png`, `20261004_3h_g1_metalhud.png`.

---

## 1. Rules of engagement

**Git.**

- Work on the branch `phase1-integration`. Do not switch branch. The container and the Mac share one working tree.
- Never push. Commit only when the owner says so, and then only the files he named.
- No `git stash`, `reset`, `clean`, `checkout --` or `restore` on files you did not change.
- Other sessions may be editing the same tree. Before you edit a file, read it again.

**What you may fix yourself.**

| You may fix | How |
|---|---|
| Compile errors in Unity | The smallest change that compiles. No refactor |
| Shader errors | In the shader or include that the error names |
| Wrong Unity or package API use | Check the member in `ATLAS/Library/PackageCache/<package>@<hash>/` first |
| Missing references (asmdef, Resources, scene script GUID) | Fix the reference, not the design |
| Failures of the set-up script `AtlasProjectSetup` | In `ATLAS/Assets/Atlas/Editor/AtlasProjectSetup.cs` |
| Failing `[Category("Unity")]` tests | Fix the code when the test is right; say so in the report when you change a test |

Keep to these while fixing:

- The contracts in `docs/contracts/` are binding. Read `docs/contracts/README.md` (F1 to F9) once.
- Assembly rules (F1): `Atlas.Kernel`, `Atlas.Link` and `Atlas.Input` reference no Atlas assembly;
  `Atlas.Safety` references `Atlas.World` only and never Link, Telemetry or Hands; spaces never reference each other,
  Scan, Safety or App.
- Engine-free cores stay engine-free (F2): no `UnityEngine` type in a file that has none today.
- Composition in code (F3): no hand-authored scene content, no prefab, no `.asset` written by hand. The space
  scenes are generated by `tools/gen_scenes.py`; `.meta` files for sources by `tools/gen_meta.py`.
- One look (F7): `Atlas/Surface` and the shaders that F7 and lane A §10.1 name, and no `MaterialPropertyBlock`.
- No per-frame allocation on the frame path.
- Code reads like the code round it: XML `<summary>` on public types, short comments that say why.

**What you must not change without asking the owner.**

- Any signature or name on a contract page or in Master Plan section 6.5.
- Bump Guard: `ATLAS/Assets/Atlas/Safety/**`, its constants (`Safety/Core/BumpConstants.cs`) and its logic.
  If it does not compile, report it and stop there.
- The Master Plan and the documents under `documents/`.
- `ATLAS/Packages/manifest.json` versions.

**After any C# change, re-check.** Preferred: the headless harness on the Mac. It needs the .NET 8 SDK and one
Editor import (so that `ATLAS/Library/ScriptAssemblies` exists).

```sh
export ATLAS_UNITY_MANAGED="/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/Managed"
export ATLAS_UNITY_IOS_EXT="$(dirname "$(find /Applications/Unity/Hub/Editor/6000.3.25f1 -name 'UnityEditor.iOS.Extensions.Xcode.dll' -print -quit)")"
dotnet --version                                   # must report 8.x
python3 tools/headless/verify.py build <Assembly> --tag mac
python3 tools/headless/verify.py test <Assembly>.Tests --tag mac
python3 tools/headless/verify.py rules --tag mac
python3 tools/headless/verify.py all --tag mac     # once, at the end of a batch of fixes
```

- `ATLAS_UNITY_MANAGED` must hold a folder `UnityEngine/` (the harness exits with "Unity managed assemblies
  not found" otherwise).
- The harness sets `DOTNET_ROOT` to `~/.dotnet` when the variable is unset. If .NET is installed elsewhere and
  `dotnet test` cannot find its runtime, export `DOTNET_ROOT` yourself (the Microsoft installer uses
  `/usr/local/share/dotnet`).
- If the harness cannot be set up, re-run the EditMode tests in Unity instead (step 3d) and say so in the
  report.

**Safety.** Nobody wears the headset on the strength of this build until the Bump Guard checks of step 3h
pass with a spotter. Until then the phone is in the hand, or in the headset on a table.

---

## 2. Tools

**MCP for Unity, if your session has it.** The project carries the package `com.coplaydev.unity-mcp`. With
the Editor open the owner connects it (Window > MCP for Unity). The package defines, among others, the tools
`read_console`, `run_tests` with `get_test_job`, `manage_editor` (play, stop), `execute_menu_item`,
`execute_code`, `refresh_unity`, `manage_scene` and `manage_camera`. Use what your session lists:

| Need | With MCP for Unity |
|---|---|
| Read the Console | `read_console` |
| Recompile after an edit | `refresh_unity` |
| Run tests | `run_tests`, then `get_test_job` |
| Enter and leave play mode | `manage_editor` |
| Run a menu entry | `execute_menu_item` with `Atlas/Apply Project Settings` or `Atlas/Start Spectator (play mode)` |
| Capture the Game view in play mode | `execute_code` with `UnityEngine.ScreenCapture.CaptureScreenshot("<absolute path>.png");` |
| Set a parameter in play mode | `execute_code` with `Atlas.App.AtlasBootstrap.App.Registry.Set("glued.mode", 1);` |

Which of these tools takes a screenshot by itself is not established from the source; `ScreenCapture` through
`execute_code` is.

**Otherwise: Unity batch mode.** Batch mode cannot run while the Editor has the project open
(`ATLAS/Temp/UnityLockfile` exists then). Ask the owner to close the Editor first.

```sh
UNITY="/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity"
REPO="$(git rev-parse --show-toplevel)"
mkdir -p "$REPO/docs/verification/logs"

# Import and compile, then quit
"$UNITY" -batchmode -quit -projectPath "$REPO/ATLAS" -logFile "$REPO/docs/verification/logs/import.log"

# The set-up of step 3b
"$UNITY" -batchmode -quit -projectPath "$REPO/ATLAS" \
  -executeMethod Atlas.EditorTools.AtlasProjectSetup.Apply -logFile "$REPO/docs/verification/logs/setup.log"

# All EditMode tests (no -quit: the test run ends the Editor itself)
"$UNITY" -batchmode -projectPath "$REPO/ATLAS" -runTests -testPlatform EditMode \
  -testResults "$REPO/docs/verification/logs/editmode-results.xml" -logFile "$REPO/docs/verification/logs/editmode.log"

# Only the tests that need the engine
"$UNITY" -batchmode -projectPath "$REPO/ATLAS" -runTests -testPlatform EditMode -testCategory Unity \
  -testResults "$REPO/docs/verification/logs/unity-category-results.xml" -logFile "$REPO/docs/verification/logs/unity-category.log"
```

`tools/build_ios.sh` adds `-buildTarget iOS` to its own Unity call. Add it to the import command only if you
want the import done for the iOS target; a change of target re-imports the assets.

**Pictures without MCP.** `screencapture -x <file>.png` captures the screen (the terminal needs the macOS
Screen Recording permission). The owner's way: Cmd + Shift + 4, Space, click the Game window.

**Reading a log.** The signals that matter:

```sh
grep -nE "error CS[0-9]+|warning CS0618|Shader error|Shader warning|Burst error|^Atlas:|^Atlas\.Input:|^portal\.|^look\.|^materials\.|Exception" <log>
```

Every line the app writes itself starts with `Atlas:`, except five: `Atlas.Input:` (gaze target slots),
`portal.features:` and `portal.shader:` (the portal renderer), `look.background:` and `materials.grow:` (the
look). With the Editor open, its own log is `~/Library/Logs/Unity/Editor.log`.

---

## 3. Step by step

Order: a, b, c, d, e, f in the Editor; then parts 1 and 2 of i (the tracked hands, in the Editor); then g and h on the
phone, and part 3 of i after H3. It is the order of the owner's jobs B to G, with the hands after them. After every
fix, repeat from the step the fix touches.

### 3a. First import and compile

**Do.** Open `ATLAS/` with Editor 6000.3.25f1 (Unity Hub > Add project from disk), or run the import command
of section 2. If Unity offers Safe Mode, script errors exist; either choice is fine, the Console is what
matters.

**Correct looks like.**

- No red line in the Console after import; no `error CS` in the log.
- In an interactive Editor, shortly after load, one line:
  `Atlas: the render set-up on disk was out of date and has been applied; commit the changed settings assets.`
  It appears once. On a later load it must not appear again (step 3b).
- Window > Package Manager shows no package in error.

**Capture.** `logs/import.log` (or a copy of `Editor.log`), a screenshot of the Console
(`YYYYMMDD_3a_console.png`), and `git status --short` after the import.

**Most likely failures, and where to look.**

| Symptom | Look at |
|---|---|
| A package fails to resolve | `ATLAS/Packages/manifest.json`: `com.coplaydev.unity-mcp` is a git URL on `#main`; `com.revenantx.litenetlib` 2.1.4 comes from the OpenUPM scoped registry. Both need the network on a first resolve |
| `error CS` in an Atlas assembly | The harness compiled the same sources against the same DLLs, so suspect what differs: the `.asmdef` (references by name, `allowUnsafeCode`, `defineConstraints`), a `.meta` GUID, a define. Compare with `tools/headless/verify.py` lines 51 to 66 (the defines it used) |
| "GUID conflict" or "has no meta file" | `tools/gen_meta.py` wrote the `.meta` files. Report every `.meta` Unity creates or rewrites |
| A scene shows a missing script | `tools/gen_scenes.py` wrote the seven space scenes (`Space_Home`, `Space_Glued`, `Space_CurvedWalk`, `Space_AlignedCells`, `Space_SmallPlanet`, `Space_Portal`, `Space_Slicer`) with the script GUIDs from the `.meta` files (and `PC_Spectator`, which holds one empty object `AtlasSpectatorBoot` and no script). SpaceLab falls back to the registered factory when a scene holds no module, so the app still runs; report it. Lane B's older scenes (`Lung_*`, `Blade_FieldFit*`, `FieldFit_BlindSpotTest`) are not in the build list and were written before the merge: a missing script there is expected until someone opens them |
| `AtlasRuntimeTheme.tss` shows an import error, or its `.meta` is rewritten | `ATLAS/Assets/Atlas/App/Onboarding/Resources/AtlasUi/AtlasRuntimeTheme.tss` is one line, `@import url("unity-theme://default");`, and its `.meta` came from `tools/gen_meta.py` with a GUID only. Unity adding importer settings to that `.meta` is expected: keep the GUID and list the file |
| `Burst error` | `Safety/Core/BumpDepthJob.cs`, `Spaces/Slicer/SlicerJobs.cs`, `Kernel/Mesh4D/SliceJob.cs` |
| Errors in the test assemblies only | `ATLAS/Assets/Atlas/Tests/*/Atlas.*.Tests.asmdef` use `overrideReferences` with `nunit.framework.dll` |
| Stray `*.csproj` in `ATLAS/` | Left by an earlier Editor; git ignores them. Not a fault |

**What Unity will write, and what to do with it.**

| Unity writes | Do |
|---|---|
| `ATLAS/Library/`, `Temp/`, `Logs/`, `UserSettings/`, `obj/`, `Build/`, `*.csproj`, `*.sln` | Ignored by git. Leave |
| A new `.meta` for a file or folder that had none | Keep it for a commit (it pins the GUID). List each one in the report: it is a file `gen_meta.py` missed |
| A changed existing `.meta` | Do not commit. Report the diff. A changed script GUID breaks the generated scenes |
| `ATLAS/Assets/Atlas/Rendering/Settings/Atlas_URP_iOS.asset`, `Atlas_URP_iOS_Renderer.asset` | Regenerated by the set-up. Commit (step 3b) |
| `ATLAS/ProjectSettings/TagManager.asset`, `QualitySettings.asset`, `GraphicsSettings.asset`, `ProjectSettings.asset`, `EditorSettings.asset`, `VersionControlSettings.asset` | Written by the set-up. Commit after the check of step 3b |
| `ATLAS/Packages/packages-lock.json` | The git package follows `#main`, so its hash can move. Report; commit only if the owner agrees |
| `ATLAS/Assets/Atlas/Scenes/*.unity` re-serialised | Do not save scenes. If Unity marks one dirty, discard |
| `ATLAS/Assets/XR/**`, other `ProjectSettings/*.asset` touched by a package | Leave uncommitted and report |
| `ATLAS/Assets/Atlas/Telemetry/Resources/AtlasParams/defaults.json` | Written only by the Console's Bake. Do not press Bake |

"Commit" here means: put it on the list for the owner. You commit nothing until he says so.

### 3b. The set-up

**What `Atlas.EditorTools.AtlasProjectSetup` does** (`ATLAS/Assets/Atlas/Editor/AtlasProjectSetup.cs`).

| Entry | When | Applies |
|---|---|---|
| `Apply()`, menu **Atlas > Apply Project Settings** | By hand, or `-executeMethod Atlas.EditorTools.AtlasProjectSetup.Apply` | Editor settings, Player settings, URP settings, layers, the boot scene if missing. Logs `Atlas: project settings applied (Master Plan Appendix C.4).` |
| `ApplyRenderSetup()` through `ApplyRenderSetupWhenStale` | By itself after every Editor load or recompile, deferred, when `IsRenderSetupCurrent()` is false and no play mode or build is starting | URP asset, renderer and its features, layers. Logs the "out of date" line of step 3a |
| `BeforeBuild.OnPreprocessBuild` | First thing in every player build | The same render set-up, then throws `BuildFailedException("Atlas: the render set-up could not be applied; see the errors above.")` if the check still fails. Logs `Atlas: render set-up applied for the build.` |

**Do.** Run **Atlas > Apply Project Settings** once (or the batch command). Then restart the Editor, or
recompile, and confirm the "out of date" line does not come back.

**Correct looks like.** Open the assets as text and in the Inspector and compare with Master Plan Appendix
C.4 and section 8.2.

| Where | Must read |
|---|---|
| `Atlas_URP_iOS.asset` | `m_RenderScale: 1`; MSAA 4x; HDR off; Depth Texture off; Opaque Texture off; SRP Batcher on; dynamic batching off; `m_MainLightRenderingMode: 0`; additional lights disabled; main-light, additional-light and soft shadows off, `m_AnyShadowsSupported: 0`; adaptive performance off; GPU Resident Drawer disabled; upscaling filter Auto; renderer list entry 0 is `Atlas_URP_iOS_Renderer` |
| `Atlas_URP_iOS_Renderer.asset` | Rendering path Forward; depth priming disabled; `m_IntermediateTextureMode: 0` (Auto); no post-process data; default stencil override off; layers 6 to 22 cleared from the opaque and the transparent layer masks |
| Renderer features | 18 Render Objects features: `Atlas Portal Masks` (off), `Atlas Room 01` to `Atlas Room 15` (off; stencil compare Equal, reference 1 to 15), `Atlas HUD` and `Atlas HUD Text` (on; depth test Always, no depth write). The template's AR background feature stays as well |
| `TagManager.asset` layers | 6 `AtlasPortalMask`, 7 `AtlasHud`, 8 to 22 `AtlasRoom01` to `AtlasRoom15`, 23 `AtlasSpectator` |
| `QualitySettings.asset` | Every level: `vSyncCount` 1 and the render pipeline `Atlas_URP_iOS` |
| Graphics settings | Default render pipeline `Atlas_URP_iOS` |
| Player, iOS | Product name `ATLAS`; bundle id `com.winstonthov.atlas`; IL2CPP; ARM64; Metal only; managed stripping Low; target iOS 17.0; requires full screen; automatic signing; default orientation Landscape Left; Frame Timing Stats on; insecure HTTP always allowed; static and dynamic batching off; "Adjust iOS FPS based on thermal state" off; camera usage text set |
| Editor | Asset serialisation Force Text; Visible Meta Files |

Run the set-up a second time: `git diff` on the settings assets must stay empty (it is meant to be
idempotent).

**Capture.** `logs/setup.log`; the Inspector of the renderer with its feature list
(`YYYYMMDD_3b_renderer_features.png`); `git diff --stat` after the first and after the second run.

**Most likely failures, and where to look.**

| Symptom | Look at |
|---|---|
| `NullReferenceException` in `ApplyPlayerSettings` | The reflection call `PlayerSettings.SetBatchingForPlatform` (non-public): the method name or its arguments may differ in 6000.3.25f1 |
| `Atlas: serialized property '...' not found on ...` (a warning) | `SetBool`/`SetInt` in the set-up: a URP field name changed. The setting was not applied |
| Features appear twice after a second run | `EnsureFeature` finds a feature by its `name`; check that the name survives the save |
| The "out of date" line returns on every load | `IsRenderSetupCurrent()` gives a false negative. A build would then throw `BuildFailedException`. Blocker |
| Layer names missing | `ApplyLayers`: the `layers` property of `TagManager.asset` |
| The renderer shows "Missing" features | The sub-assets added with `AssetDatabase.AddObjectToAsset` and the `m_RendererFeatureMap` ids |

**Commit list for the owner** (after the checks pass): the two URP assets under
`ATLAS/Assets/Atlas/Rendering/Settings/`, `TagManager.asset`, `QualitySettings.asset`,
`GraphicsSettings.asset`, `ProjectSettings.asset`, and any new `.meta`.

### 3c. The shaders

There are **seven** `.shader` files and four includes (the owner's guide lists six; `AtlasFlatTwin.shader`
was added after it was written).

| File under `ATLAS/Assets/Atlas/` | Shader name | Used by |
|---|---|---|
| `Rendering/Shaders/Resources/AtlasSurface.shader` | `Atlas/Surface` | Everything in the world; pass `AtlasForward` |
| `Rendering/Shaders/Resources/AtlasBackground.shader` | `Hidden/Atlas/Background` | The skybox |
| `Rendering/Portals/Resources/AtlasPortalMask.shader` | `Hidden/Atlas/PortalMask` | Doorway masks of Impossible Rooms |
| `Rendering/Stereo/Resources/AtlasLensBlit.shader` | `Atlas/LensBlit` | The one blit to the screen |
| `Safety/Resources/AtlasCue.shader` | `Atlas/Cue` | The Bump Guard indicator |
| `Input/Resources/AtlasReticle.shader` | `Atlas/Reticle` | The gaze reticle |
| `Telemetry/Map/Resources/AtlasFlatTwin.shader` | `Atlas/FlatTwin` | The mini-map (`hud.map`) and the Spectator's map |

Includes, beside the first two: `AtlasCore.hlsl`, `AtlasWarp.hlsl`, `AtlasPortalClip.hlsl`, `AtlasGates.hlsl`.
An error in an include is reported against the shader that includes it.

**Lane B's include files are real now, and have never been compiled.** `AtlasWarp.hlsl` (the hyperbolic and
spherical warp, `AtlasWarp` in the vertex stage, `AtlasWarpClip` in the fragment stage) and `AtlasGates.hlsl`
(the four sight-line gates) are lane B's, written to run inside lane A's single `UnityPerMaterial` block
(`docs/contracts/lane-b-kernel.md` §6). Neither declares a uniform: `_Anchor`, `_WarpMode`, `_Gate0` to `_Gate3`
(per material), `_HeadPosWS` (`float4`) and `_HypL` are declared in `AtlasCore.hlsl`. Look at these first when
`Atlas/Surface` fails:

| Point | Where | Why it may fail |
|---|---|---|
| The local array `float4 gates[4] = { _Gate0, _Gate1, _Gate2, _Gate3 };` with the `[unroll]` loop | `AtlasGates.hlsl` | An array initialised from four uniforms, and `clip()` with a per-gate sign; Metal and D3D differ in what they accept |
| `AtlasWarpClip` | `AtlasWarp.hlsl`, called in `AtlasFrag` after `AtlasGatesClip` and `AtlasPortalClip` | A `clip()` in a file that is also compiled for the vertex stage and for the compute shader `Tests/Warp/AtlasWarpProbe.compute` (which declares the uniforms the include needs by hand) |
| `_HeadPosWS` is a `float4` | `AtlasCore.hlsl` against `AtlasWarp.hlsl` (`_HeadPosWS.x`, `.z`, `.xz`) | The include used to declare a `float3` |
| `[branch]` on `_WarpMode` | `AtlasWarp` | Mode 0 must be the plain `TransformObjectToWorld`: every flat space draws through it |
| One `UnityPerMaterial` block, one pass, no keyword | `AtlasSurface.shader`, `AtlasCore.hlsl` | The shader inspector must still say "SRP Batcher: compatible", one variant, with the real warp and gates included |
| `Tests/AlignedCells/AlignedCellsGatesTest.shader` | declares `float4 _Gate0, _Gate1, _Gate2, _Gate3` and includes the Resources path | A test shader; check it compiles | 

The GPU test of the warp, `WarpGpuTests.Warp_MatchesKernel_Gpu`, compares the vertex stage with the kernel's CPU
reference to 1 mm for ρ ≤ 3 (it needs a graphics device: run it with the Editor open, not with `-nographics`).

**Do.** Click each shader in the Project window and read the Inspector. Then, in play mode in the headset
view (step 3e), open Window > Analysis > Frame Debugger.

**Correct looks like, in the Inspector.**

- No error list on any of the seven; no Console line starting `Shader error`.
- `Atlas/Surface`: "SRP Batcher: compatible"; one pass; one variant; no keywords (the contract allows at most
  eight variants).
- `Hidden/Atlas/PortalMask`: "SRP Batcher: compatible".

**Correct looks like, in the Frame Debugger** (headset view, one eye camera selected).

| Check | Expected |
|---|---|
| World draws | Every `Atlas/Surface` draw of the eye sits in SRP Batch groups. No break with the reason "different MaterialPropertyBlock" |
| One draw per mesh per eye | Home on `budget_room`: 14 room draws per eye (floor, walls, twelve objects) plus one for the floor marks |
| Passes that must be absent | No depth pre-pass, no shadow pass, no post-processing pass, no copy-colour or copy-depth |
| Skybox | `Hidden/Atlas/Background` once per eye, after the opaques |
| Head-locked passes | `Atlas HUD` then `Atlas HUD Text`, after the transparents, depth test Always, no depth write. Reticle, cue and map quads in the first; text in the second |
| Portal passes | None in Home, Glued or Slicer. In Impossible Rooms with a doorway in view, in this order: Draw Opaques (layer Default) > `Atlas Portal Masks` (the mark: no colour, stencil Always/Replace with the reference; then the reset: stencil Equal, depth test Always) > `Atlas Room 01` (`Atlas/Surface`, stencil Equal reference 1, Keep) > skybox > `Atlas HUD`, `Atlas HUD Text`. No `Atlas Room NN` above the number of doorways in use |
| The screen | The Screen camera (depth 10) has exactly one pass, `Atlas Lens Blit`, which writes the back buffer. No URP final blit after it |
| Camera order | Eye Left (depth 0), Eye Right (depth 1), Screen (depth 10). The Main Camera does not render in the headset view |
| Curved Walk at q = 5 (step 3e, row 24) | The tile shells of the generated world and the room's kerbs are `Atlas/Surface` draws in SRP Batch groups, each on its own pooled material (one per tile, up to 128, plus the room's shell and objects). Telemetry's `visibleTiles` is the number of tiles drawn; the draw calls are at most 210 on `budget_room` (lane A §15) |
| Aligned Cells (row 25) | The cells' tile instances and the room's own renderers, drawn once per eye; no `Atlas Room NN` pass |

In the Editor an explicit MSAA resolve and an intermediate texture per eye are normal (the on-tile resolve
exists only in a Metal player build). Do not report them from the Editor; they are checked in the Xcode GPU
capture of step 3g.

**Capture.** One Inspector screenshot per shader that shows an error; `YYYYMMDD_3c_surface_inspector.png`;
`YYYYMMDD_3c_framedebugger_home.png`; `YYYYMMDD_3c_framedebugger_portal.png`.

**Most likely failures, and where to look.**

| Symptom | Look at |
|---|---|
| Magenta world | `AtlasSurface.shader` or one of its four includes failed. The first `Shader error` line names file and line |
| Magenta or black sky | `AtlasBackground.shader`; the Console says `look.background: shader 'AtlasBackground' is not in Resources.` when it is missing |
| Headset view stays black | `AtlasLensBlit.shader`; the Console says `Atlas: shader 'Atlas/LensBlit' not found in Resources; the headset view stays black.` Also check `LensBlitPass.RecordRenderGraph` (`Rendering/Stereo/LensBlitPass.cs`) |
| Eyes upside down or swapped | The V flip in `LensBlitPass` (`GetTextureUVOrigin`) and the left/right mapping in the blit shader |
| "SRP Batcher: not compatible" | The `UnityPerMaterial` block in `AtlasCore.hlsl`: every material property must be inside it |
| Doorway shows nothing behind it | The Console says `portal.features: the renderer has no 'Atlas Portal Masks' feature; run Atlas > Apply Project Settings.` Step 3b did not hold |
| HUD, reticle or cue hidden behind walls, or drawn twice | The `Atlas HUD` features and the layer masks of step 3b |
| Cue, reticle or map quad invisible | Their shaders have no `LightMode` tag and rely on the Render Objects feature drawing layer 7 |

### 3d. The tests

**Do.** Window > General > Test Runner > EditMode > Run All (or the batch command of section 2). Every Atlas
test assembly is Editor-only, so all tests are on the EditMode tab, the play-mode walk included (it enters
play mode itself). The PlayMode tab should list nothing from Atlas.

**Correct looks like.** Every test green. Exit code 0 from the batch run. The play-mode walk leaves no
`Atlas` object, no error line and no leaked-material warning when it ends.

**The tests that have never run** (compiled out of the headless run by `#if !ATLAS_HEADLESS`, marked
`[Category("Unity")]`): 113 `[Test]` and `[TestCase]` rows in 16 test assemblies, counted from the source
(a `[TestCaseSource]` that expands to several is counted as one, so the Runner will show a few more). Lane A, C
and D's are in the first six rows, lane B's in the rest.

| Class (file under `ATLAS/Assets/Atlas/Tests/`) | Tests |
|---|---|
| `BootstrapUnityTests` (`App/`) | 3: `Boot_InPlayMode_WalksTheWholeFlow_ThenStopsCleanly`, `GridSpace_BuildsItsGround_AndReturnsItsMaterial`, `SceneSpaceLoader_UsesTheRegisteredFactory_AndParentsTheModule` |
| `AtlasActionMapTests` (`Input/`) | 4 |
| `LookUnityTests` (`Rendering/Look/`) | 7: shaders load from Resources, the pooled material, the palette texture, the globals, the mesh layout |
| `SlicerUnityTests` (`Spaces/Slicer/`) | 4 |
| `HomeSpaceTests` (`Spaces/Home/`) | 1: `Module_FromTheFactory_IsNamedLikeTheScene_AndRunsBare` |
| `TileRendererTests`, `WorldContentRendererTests` (`Tiles/`) | 4 and 3: T4 allocation and timing, T6 fog, T7 pool and anchors, T8 shell cache; the generated world's renderer |
| `TileMeshSupportTests` (`TileMeshes/`) | 1: `Upload_WritesTheLookLayout` |
| `WarpGpuTests`, `WarpTests` (`Warp/`) | 1 and 2: `Warp_MatchesKernel_Gpu`, `Warp_HugeLocalBounds`, `ShaderIDs_MatchContract` |
| `WorldLintConditionsTests` (`WorldGen/`) | `WorldLint_All` (six cases), `WorldLint_DetectsCoincidentKerbs`, `WorldLint_BudgetIsBinding`, `FootprintLintTests.Run_ReportsFootprintFields` build meshes and run only in the Editor |
| `CurvedRoomViewTests` (`CurvedWalk/`) | 2 |
| `AlignedCellsModuleTests`, `AlignedCellsViewTests`, `RoomCrossingTests`, `RoomViewTests` (`AlignedCells/`) | 5, 2, 1 and 3 |
| `SmallPlanetRenderTests`, `SmallPlanetSpaceTests` (`SmallPlanet/`) | 1 and 1 |
| `AirwayBeaconTests`, `ModeSwitchTests`, `TwoCompassesTests` (`Lung/`) | 2, 1 and 1 |
| `EndoluminalGpuTests`, `EndoluminalMathTests`, `EndoluminalWindowTests`, `KitGeometryTests` (`LungAirwayViews/`) | 1, 1, 5 and 2 |
| `NeedleSkyGpuTests`, `SkyDataTests` (`LungSky/`) | 1 and 2 |
| `FieldFitBlitTests`, `FieldFitFeatureTests`, `FieldLossMaskTests`, `LensGuardTests`, `LensLutTests`, `RadialTableTests`, `RingTestSceneTests` (`FieldFit/`) | 2, 11, 4, 10, 6, 3 and 4 |
| `BlindSpotComponentTests`, `FieldFitBlindSpotGuardTests`, `RiverTextTests` (`FieldFitBlindSpot/`) | 5, 2 and 4 |

Run `Boot_InPlayMode_WalksTheWholeFlow_ThenStopsCleanly` first: it is the Editor-first path from start to
stop with `sample_3x3` and no boot scene. Then the shader tests (`WarpGpuTests`, `LookUnityTests`,
`FieldFitBlitTests`, `NeedleSkyGpuTests`, `EndoluminalGpuTests`, `LensGuardTests`, which need a graphics device:
run them with the Editor open, not with `-nographics`), then the rest. FieldFit's tests are mostly Unity-only
because they build textures and meshes; their headless part is 15 tests.

**Size per assembly.** Headless counts are from the last run (`Total tests` of `verify.py`); the last column is
counted as above.

| Assembly | Headless tests, last run | Unity-only tests, not in that count |
|---|---|---|
| `Atlas.App.Tests` | 213 | 3 |
| `Atlas.Architecture.Tests` | 5 | 0 |
| `Atlas.Blades.FieldFit.BlindSpot.Tests` | 21 | 11 |
| `Atlas.Blades.FieldFit.Tests` | 15 | 40 |
| `Atlas.Blades.Lung.AirwayViews.Tests` | 19 | 9 |
| `Atlas.Blades.Lung.Sky.Tests` | 26 | 3 |
| `Atlas.Blades.Lung.Tests` | 49 (48 pass, 1 skipped) | 4 |
| Atlas.E2E.Tests (`Tests/App/E2E`) | 32 | 0 |
| `Atlas.Input.Tests` | 52 | 4 |
| `Atlas.Kernel.Tests` | 160 | 0 |
| `Atlas.Link.Tests` | 125 | 0 |
| `Atlas.Rendering.Tests` | 200 | 7 |
| `Atlas.Safety.Tests` | 95 | 0 |
| `Atlas.Scan.Tests` | 304 | 0 |
| `Atlas.Spaces.AlignedCells.Tests` | 44 | 11 |
| `Atlas.Spaces.CurvedWalk.Tests` | 49 | 2 |
| `Atlas.Spaces.SmallPlanet.Tests` | 20 | 2 |
| `Atlas.Spaces.Tests` | 95 | 5 |
| `Atlas.Telemetry.Tests` | 268 | 0 |
| `Atlas.TileMeshes.Tests` | 6 | 1 |
| `Atlas.Tiles.Tests` | 19 | 7 |
| `Atlas.Warp.Tests` | 12 | 3 |
| `Atlas.World.Tests` | 88 | 0 |
| `Atlas.WorldGen.Tests` | 51 | 1 |

Twenty-four test assemblies. In the Unity Runner expect the headless total plus the last column, minus the
headless-only tests below.

One test runs only headless and is absent in Unity by design: `StepAndDepth_AllocateNothing` in
`Safety/BumpGuardReplayTests.cs` (`#if ATLAS_HEADLESS`). `TileId_CrossingCost` (`Kernel/TileIdTests.cs`) times the
tile crossing and asserts a median of at most 0.5 ms: it passes on .NET 8, and under Mono in the Editor its
log line says `codeOptimization=`; judge it only in a Release code optimisation.

**The gates as a whole.** `python3 tools/e2e/gates.py` (about ten minutes) runs `verify.py all`, the `.meta` and
scene generators' `--check`, the placeholder-header search, the Hub, Forge and Console suites, the performance
summariser and the Station rehearsal, and prints the checklist of each gate G0 to G7 with every line either
proven by named tests that ran, or waiting for a phone, the headset, the Editor or a person.

**Capture.** `logs/editmode-results.xml`; a screenshot of the Runner (`YYYYMMDD_3d_testrunner.png`); for
each red test its full name and the first line of its message.

**Most likely failures, and where to look.**

| Symptom | Look at |
|---|---|
| Tests that read repository files fail in Unity but pass headless | They find files through `ATLAS_PROJECT_DIR`, falling back to the current directory, which is the project folder inside Unity. A test that needs the repository root (`recordings/`) may resolve it differently |
| `TheSteadyFrame_AllocatesNothing` red | `Is.Not.AllocatingGCMemory` with the Editor's job safety checks on. Re-run with Jobs > Burst > Safety Checks off before you call it a fault |
| `BootstrapUnityTests` asserts on the display mode | The test starts the app with a room on the command-line path (`BootOptions.Room`), so the rehearsal flow owns the flow and enters the headset view by itself |
| `BootstrapUnityTests` fails with "Home is available: its own module, or the built-in grid space when Home is not in the build" | `SpaceLab.IsAvailable(SpaceId.Home)` is false: neither `Space_Home` nor a registered factory was found. See the registrations (risk I6) |
| Mono formats a number differently from .NET 8 | `RoomModelJson.Write` uses the custom format `"0.0#####"`; pose-log text likewise. A golden-text test then fails only in Unity |
| A test passes alone and fails in Run All | Static state left by another test: `AtlasServices`, `BumpCue`, `SpaceRegistry`, `AppHooks` |

### 3e. First look in play mode

#### THE ANSWER TO JOB A: "what should I see in play mode in Atlas_Boot today, and which checks do you want from me"

**Set-up.** Open `ATLAS/Assets/Atlas/Scenes/Atlas_Boot.unity`. Set the Game view to a fixed resolution of
2796 x 1290. Unmute the Game view's audio. Press Play.

**Everything is built in code.** The scene holds only `AR Session` and `XR Origin (Mobile AR)` with
`Camera Offset` and `Main Camera`. `Atlas.App.AtlasBootstrap` runs after the scene has loaded and builds an
object `Atlas` (kept across scene loads) with `Systems`, `Room` and `Space` under it, and `Atlas Head` under
the Main Camera with `Atlas Eyes`, `Eye Left`, `Eye Right` and `Hud`. The seven space scenes
(`Space_Home`, `Space_Glued`, `Space_CurvedWalk`, `Space_AlignedCells`, `Space_SmallPlanet`, `Space_Portal`,
`Space_Slicer`) are loaded additively when a space is entered.

**Console, in this order.**

1. `Atlas: eye textures 1118 x 1032 <colour format> / <depth format>, memoryless ..., store-and-resolve ...,
   sRGB blit to back buffer ..., watermark visible ..., screen 2796 x 1290 at <n> dpi` (the size follows the
   Game view: half the long side by the short side, times 0.8).
2. `Atlas: AR backend XR Simulation` **or** `Atlas: AR backend none (no AR session: neutral)`. This line tells
   you which of the two branches below you are in.
3. `Atlas: booted. <N> parameters, 7 spaces registered, head from arcamera` (XR Simulation) or
   `... head from walker` (no AR). With a room or a replay on the command line the line ends
   `, rehearsal flow`.

Never: `Atlas: boot failed. The app is not running.`, `Atlas: Bump Guard missing.`, any line with
`not in Resources`, any exception.

**Which branch.** `ATLAS/Assets/XR/XRGeneralSettings.asset` lists the XR Simulation loader for the desktop
target with "Initialize XR on Startup" on, so expect **branch S**. If the loader does not start you are in
**branch N**. Whether XR Simulation shows an environment out of the box on this Mac is not established from
the source.

| | Branch N: no AR provider | Branch S: XR Simulation |
|---|---|---|
| Head (`pose.source` = `tracked`) | The keyboard walker, at the room's centre, eyes 1.6 m up, facing +Z | The simulated AR camera (`arcamera`) |
| Depth for Bump Guard | Ray-cast of the RoomModel | Ray-cast of the RoomModel |
| `dev.room` at start | `sample_3x3` (loaded in the first frame; log `Atlas: room from fixture sample_3x3, no scan`) | `none`: the flow waits for a scan |
| Background in the hand view | Black | The simulated camera picture |
| After Start and the Station step | Straight to "Building your room", then the headset prompt | "Scan the room": a real sweep of the simulated environment |

**The first picture, both branches (the hand view).**

- Right 46 % of the screen: a dark card, the onboarding screen (UI Toolkit; Unity's default runtime theme
  loaded from Resources as `AtlasUi/AtlasRuntimeTheme`, every size and colour set in code, built-in font). Title
  `Welcome to ATLAS`, a body that starts "You scan this room with the phone in your hand, then walk it in the
  headset.", a wide white button `Start` and a dark button `Sample room (no scan)`.
- Top left: a small grey box, the development overlay (`dev.overlay`, on in the Editor). It reads
  `ATLAS  no space  Empty`, `Room: none`, five buttons `sample_3x3`, `sample_L_3x4`, `sample_4x5_table`,
  `budget_room`, `tiny_standing`, the word `Space` with the buttons `S0`, `S1`, `S5`, `S6` and `Home`, then
  `Head: tracked` with the buttons `tracked`, `replay`, `walker`.
- One picture, not two: the stereo view starts only when the headset step is passed.
- No HUD, no reticle, no cue: the head-locked layer is not drawn in the hand.

**Reaching Home.** Three ways. Use the first for every check below.

| Way | Clicks | What you see on the way |
|---|---|---|
| **Fast** | `Sample room (no scan)` on the Welcome card. Then, in branch S, click `walker` in the overlay | `Building your room` / `One moment.` for a moment, then the stereo view of Home in `sample_3x3` |
| **Full flow** | `Start` > (Station screen) `Continue without Station` > branch N: wait or click `Switch now`; branch S: sweep, `Done`, `Looks right`, then `Switch now` | The screens of the table below |
| **No onboarding** | Start the Editor from a terminal with `-atlasRoom sample_3x3 -atlasSource walker` after `-projectPath` | No card. The rehearsal flow hands the room over and enters the headset view by itself within about a second. The overlay then also shows `Scan`, `End sweep`, `Confirm` and `Back in the hand` (`Headset on` when in the hand) |

The onboarding screens, in order, with their exact titles and buttons:

| Step | Title | Buttons | Notes for the Editor |
|---|---|---|---|
| welcome | `Welcome to ATLAS` | `Start`, `Sample room (no scan)` | |
| camera | `Camera` | `Back`, `Sample room (no scan)` | Skipped in the Editor: the permission reads as granted |
| station | `Station` | `Continue without Station`, `Use this address`, an address field; after 60 s also `Try again` | Body starts `Station address: 192.168.0.10:9050` until an address is stored. Shows `Station connected.` for 1 s when the Hub answers, then moves on. After 15 s a notice starting `No answer yet.`; after 60 s `The Station did not answer.` |
| room | `Your room` | `Scan this room`, `Outline by hand`, `Sample room (no scan)`; `Use the saved room` when one is saved | Shown only when a saved room exists or a scan failed; otherwise the scan starts by itself |
| sweep | `Scan the room` or `Outline the room` | `Done`, `Outline by hand` or `Add corner here` and `Undo corner`, `Back` | Branch S only |
| confirm | `Is this your free floor?` | `Looks right`, `Adjust` / `Done adjusting`, `Scan again` | White outline, green free rectangle, yellow lap square drawn on the floor |
| build | `Building your room` | none | At most 3 s |
| headset | `Put the phone in the headset` | `Switch now`, `Scan again` | Counts down from 10 s. The `Switch now` button, gamepad A, or a click anywhere that is neither on another button nor inside the overlay's box (the lever) switches at once. The body gives `Free floor: W x D m` and either `You will walk a square of S m.` or the standing-mode sentence |
| home | (nothing) | | The card is gone |

The overlay's box and the card. While the overlay is drawn, a press inside its box belongs to the overlay
only: it is not the lever tap of the headset step, and on the confirm screen it neither places nor drags a
corner (`OnboardingView.OnPointerDown` asks `DevOverlay.Covers`). The box is the rectangle 8, 8, 250 x 524 on
a layout 540 units high, scaled to the screen height (`App/Flow/DevOverlayLayout.cs`): at 2796 x 1290 it is
about the left 616 pixels, nearly the full height, blank parts of the box included. Check two things: a click
on an overlay button during the countdown does not switch to the headset view, and a click just right of the
box still does. Repeat at a Game view size that is not 16:9 and report it if the grey box and the dead zone do
not line up.

**The headset view in Home: the expected picture.**

- **Two eye images side by side**, left and right halves, upright, slightly different (64 mm apart). In the
  Editor it is a plain side-by-side copy with no lens distortion (`render.lens` is off in the Editor; switch
  it on to see the barrel pre-distortion with black beyond the lens field).
- The world fades in from black over 0.25 s.
- **The room**: a dark floor with thin grid lines every 0.5 m and stronger lines every 1 m; four walls at
  their real places (`sample_3x3`: walls 1.5 m from the centre on every side), darker at the foot; a darker
  horizon and a lighter zenith behind; fog far away. Nothing is lit by a light: the look is unlit line art.
- **Home's floor marks**: a small ring with a dot at the room's centre, and the outline of the lap square
  (2.0 m in `sample_3x3`) on the floor.
- **Head-locked, in both eyes**: a small reticle dot at the centre of each eye image; the HUD panel, five
  lines of small text about 14 degrees below the centre (`hud.visible` starts on in the Editor), which read
  like `60 fps  p95 16.7  tier 0  therm 0` / `cpu 0.0 / 0.0  gpu 0.0 ms` / `draws N  sp N  tris Nk` /
  `mem N MB  bat -  gc N` / `S0  link offline  bump 0  assets 0/0`; a link dot below it, grey while offline;
  the menu's chip down and to the left.
- The overlay box stays at the top left over the left eye (Editor only). Its first line now reads
  `ATLAS  Home  Running` and `Room: sample_3x3`.
- **Nothing is magenta.** No Console line repeats every frame.

**Keys and mouse in the Editor.** Actions are live only in the headset view; the walker keys always.

| Input | Does | Notes |
|---|---|---|
| W A S D | Walk at 0.7 m/s | Only while the head is the keyboard walker (branch N, or `pose.source` = `walker`) |
| Left Shift / Left Ctrl held | 1.0 m/s / 0.3 m/s | |
| Right mouse button held + mouse | Turn the head, 0.15 degrees per pixel, pitch limited to 85 degrees | |
| Left mouse click | The headset lever: fires the gaze target under the reticle, otherwise `Confirm` | Quirk, headset view only: a click on an overlay button is also a lever tap here (this path is in `Atlas.Input`, not in the card), so it fires a gaze target under the reticle, or `Confirm`. Look away from the menu chip before you click the overlay |
| H | Return to Home | Gamepad: B held 1.0 s (a tap of B under 0.5 s is Back). Already in Home: the walker is put back on the floor, no fade |
| Page Up / Page Down | Next / previous built space in the order S0, S1, S5, S6, wrapping at both ends | Gamepad: d-pad up / down. Only with the menu closed; with the menu open the d-pad moves the focus |
| Tab | Opens the menu on its `Spaces` page; with the menu open, jumps to that page | Gamepad: start with select up |
| F1 | Opens and closes the on-device menu | Gamepad: start while select is held |
| Up / Down arrows | Menu: move the focus | Gamepad: d-pad or left stick |
| Left / Right arrows | Menu: step the focused value; on the first row, change the page | |
| Enter or Space | Menu: confirm (applies a value shown with `(apply)`, toggles a bool, runs a `go` row) | Gamepad A |
| Backspace | Menu: close | Gamepad B tap |
| M / X | Slicer: next body / slice or shadow view | Gamepad Y / X |
| I K / J L / = - | Slicer: move the cut (`slicer.w`) / turn XW / turn ZW | Left stick / triggers. YW has no key: right stick, or its gaze handle |

H, Page Up, Page Down and Tab do nothing, by design, in four cases: in the hand view; while a space switch
is under way (fading out, loading, fading in); while `demoLock` is on; and, for the first three, while the
menu is open. The overlay's buttons, the menu's own `Spaces` rows and the Station's commands are not held
back by the lock.

Bound but read by nothing in this build (not a fault of your keyboard): comma and period (previous and next
variant; gamepad shoulders), and the left and right arrows outside the menu (the floor dial; gamepad d-pad
left and right).

**Parameters, from the command line.** Pass them after `-projectPath` when you start the Editor, or in the
Xcode scheme for the phone.

| Argument | Meaning |
|---|---|
| `-atlasRoom <fixture name or path to a RoomModel .json>` | The room; no onboarding |
| `-atlasReplay <path to a .poselog>` | Replays that walk; sets the source to `replay`; no onboarding |
| `-atlasSource tracked\|replay\|walker` | The pose source |
| `-atlasSpace S0..S6` | The space entered after the room hand-over |
| `-atlasHand` | Stay in the hand view after the hand-over |
| `-atlasPerf tour\|soak\|soakNoDepth\|ladder`, `-atlasPerfVisit <s>`, `-atlasPerfSoakMinutes <m>`, `-atlasPerfInterval <s>`, `-atlasPerfPose auto\|tracked\|replay\|synthetic`, `-atlasPerfQuit` | The scripted performance runs (`docs/evidence/README.md`) |

**Parameters, in the registry.** Change them from the menu (F1 > page `Parameters`), from the Console's
Tuner, with `POST /api/params/<name>`, or with `Registry.Set` through MCP. Ids that matter for this step:

| Id | Kind, range, default | Does |
|---|---|---|
| `space.id` | enum `S0 Home` .. `S6 Slicer` | Forces a space; snaps back when the space is not built |
| `dev.room` | enum `none`, then the five fixtures | Loads a fixture room |
| `dev.overlay` | bool, on in the Editor | The overlay box |
| `pose.source` | enum `tracked`, `replay`, `walker` | The head |
| `replay.speed`, `replay.loop`, `pose.record` | 0..4 (1); bool (on); bool (off) | Replay and recording |
| `hud.visible`, `hud.map` | bool (on in the Editor and in development builds); bool (off) | HUD panel; mini-map |
| `render.lens`, `render.ipd`, `render.lineWidth` | bool (off in the Editor, on on the phone); 0.055..0.075 (0.064); 0.5..3 (1.5) | Lens blit; eye distance; line width |
| `render.lensK1`, `render.lensK2`, `render.lensSeparation`, `render.screenToLens`, `render.lensOffsetY` | defaults 0.34, 0.55, 0.064, 0.039, 0 | The lens numbers (placeholders, section 5) |
| `render.headOffsetX/Y/Z` | -0.10..0.10 (0) | Camera-to-eye offset |
| `thermal.pinTier` | int -1..4 (-1 = automatic) | Forces a quality tier |
| `render.scale`, `render.msaa` | 0.6..0.8 (0.8); 1..4 (4) | Written back by the ladder |
| `glued.mode`, `glued.copies`, `glued.avatar` | enum `t2`, `klein`, `t3`; int 3..7 (5); bool (on) | S1 |
| `rooms.layout`, `rooms.count`, `portal.depth` | enum `booth`, `loop`; int 2..6 (6); int 0..1 (1) | S5 |
| `slicer.source`, `slicer.object`, `slicer.view` | `live`, `stepped`, `tesseract`; `tesseract`, `cell5`, `cell16`, `cell24`, `tube`, `ball`; `slice`, `shadow` | S6 |
| `slicer.w`, `slicer.xw`, `slicer.yw`, `slicer.zw`, `slicer.tilt` | -1.1..1.1; -180..180 each; 0..80 | S6 |
| `home.marks` | bool (on) | Home's floor marks |
| `scan.route`, `scan.teamPrescan` | int 1..4 (3); bool (off) | Scan route; keep the room and its world map |
| `link.enabled`, `link.stateHz`, `telemetry.mode`, `demoLock` | bool (on); 10..60 (60); `live`, `phoneCsv`, `hud`; bool (off) | Link |
| `input.dwell` | 0.6..0.8 (0.7) s | Gaze dwell time |
| `pose.gyroPredict`, `pose.gyroLeadMs` | bool (off); 0..60 (40) | Rotation prediction |
| `perf.run` | enum `off`, `tour`, `soak`, `soakNoDepth`, `ladder` | Starts a performance run |
| `curved.cullRho`, `curved.mode` | float 2..3.5 (3); enum `Mode 1 Curved Walk`, `Mode 2 Aligned Cells` | ρ of the fog and the draw rule (`State.p[2]`); S2 and S3 read the mode (on release) |
| `curved.q`, `curved.strong` | int 3..6 (5); bool (off) | The dial: 4 to 6 are S2, 3 is S4 (S2 hands over); `strong` uses the loop-fit radius below the comfort floor |
| `cells.viewDepth` | int 0..2 (2) | S3: how many neighbour cells are drawn |
| `planet.R`, `planet.fogRho` | 0..4 m (0 = the loop-fit R, on release); 0.5π..0.8π rad (0.8π) | S4: the sphere's radius; the opaque end of its fog |
| `forge.backend` | enum `local`, `hosted`, `cache`, `off` | Declared, no hosted vendor is wired |

A parameter marked "on release" in the code (`space.id`, `dev.room`, `pose.source`, `glued.mode`,
`glued.copies`, `rooms.layout`, `rooms.count`, `slicer.source`, `slicer.object`, `thermal.pinTier`,
`render.scale`, `render.msaa`, `perf.run`) shows its new value in the menu with `(apply)` and takes effect
on Enter.

**The checks I want from you** are the walk-through that follows. In short: the Console lines above; no
magenta; two eye images; Home in each of the five fixture rooms; the cue at a wall; HUD, menu and mini-map;
each built space and each of its layouts (the seven spaces, curved ones included); return to Home; standing mode; the three recorded walks; Stats
against the budgets; a clean stop; the performance tour; the four space actions from the keyboard and their
refusals; the menu's rescan.

#### The walk-through

Do it once in full with `sample_3x3`, then repeat rows 3 to 6 and 12 for the other four rooms. Rows 24 to 27 are the curved spaces; do them in `sample_3x3` and once in `sample_4x5_table` (a room whose loop-fit L is below the comfort floor, so `curved.strong` matters) and `tiny_standing` (standing mode: no lap, the dial still snaps). Change room
with the overlay buttons: the space reloads with the new room through a fade. One screenshot per row, named
`YYYYMMDD_3e_<item>_<room>.png` with the item given in the last column.

| # | Do | Correct looks like | Item |
|---|---|---|---|
| 1 | Press Play | The Console lines and the first picture above | `boot` |
| 2 | `Sample room (no scan)`; in branch S then `walker` | Stereo view of Home, as described above | `home` |
| 3 | Look round with the right mouse button; walk a lap | Both eyes follow together. Walls stay put. Grid lines are thin, stable and the same in both eyes. No sparkle on the floor | `home_walk` |
| 4 | Read the room against the fixture | `sample_3x3`: square room, no objects. `sample_L_3x4`: six walls, an L shape, no objects. `sample_4x5_table`: one box where the table is; about 2 s after Home is built it cross-fades (a 0.3 s dither) into the built-in table mesh. `budget_room`: twelve stand-in objects of 5,000 triangles each, never replaced. `tiny_standing`: a small room, no lap outline | `room` |
| 5 | Walk straight at a wall with W | Bump Guard cue, head-locked in both eyes, on top of the world: first an amber chevron (WARN) on the side of the wall with a soft tick twice a second; nearer, a larger red chevron (STRONG) that pulses with a tone six times a second. With a still head WARN starts at about 0.35 m from the wall and STRONG at about 0.25 m; while walking both come earlier (look-ahead 0.8 s and 0.4 s). Step back: the cue clears after about 0.3 s. The world itself never changes. HUD line 5 shows `bump 1` then `bump 2` | `cue_warn`, `cue_strong` |
| 6 | Walk through a wall and beyond the room | The cue stays STRONG outside the room. Nothing throws | `cue_outside` |
| 7 | Read the HUD; set `hud.visible` off and on; set `hud.map` on | The five lines update twice a second. The mini-map appears at the lower right, about 0.25 m wide at 1 m, with the room's outline and the head | `hud`, `map` |
| 8 | F1 | A panel opens level in front of you, title `ATLAS  Spaces ...`. Rows `S0 Home  here`, `S1 Glued  go`, `S2 Curved Walk  go`, `S3 Aligned Cells  go`, `S4 Small Planet  go`, `S5 Impossible Rooms  go`, `S6 Slicer  go`, `Return to Home  go`. Right arrow on the first row steps the pages `Spaces`, `Quality`, `Parameters`, `Session`. `Quality` starts with `Tier in force` and ends with `Reset switches to defaults`. `Parameters` lists groups under `- <group>` headings, with `(inactive)` on the groups of spaces not entered. `Session` has `Recentre`, `Rescan the room`, `Link`, `Station`, `Session`, `Room`, `Version`, `Close the menu` | `menu_spaces`, `menu_quality`, `menu_parameters`, `menu_session` |
| 9 | In the menu change `render.lineWidth`, then a value with `(apply)` such as `glued.copies` | The line width changes at once. The pending value applies on Enter. A changed value shows ` *` after its name | `menu_change` |
| 10 | Overlay `S1` (Glued) | Fade through black, then the cell repeated 5 x 5 round you with a copy of yourself (a small avatar) in every other cell; the room's walls sunk to kerbs with corner posts; the room's own floor hidden; furniture where it really is; three chevrons on the floor at the nearest cell edge until you first cross one; fog hides the end of the lattice | `glued_t2` |
| 11 | `glued.mode` = `klein`, then `t3`; `glued.copies` = 3 and 7 | `klein`: every second row is mirrored, and is not inside out; avatar shoulder colours swap in mirrored copies. `t3`: 3 x 3 x 3, a layer above with an avatar in it when you look up. Copies change the lattice within a frame | `glued_klein`, `glued_t3`, `glued_copies7` |
| 12 | Overlay `S5` (Impossible Rooms) | `rooms.layout` = `booth`: a booth in a corner of the room whose door shows a hall larger than the booth. Walk through the door: no seam, no pop, no hole when one eye is through. `rooms.layout` = `loop`: a corridor of four right turns that arrives in a different room; rooms carry tally marks 1 to `rooms.count` | `portal_booth`, `portal_loop` |
| 13 | Overlay `S6` (Slicer) | The room as in Home plus a 3D cross-section of a 4D body floating at about 1.25 m, to one side, and a lane on the floor on the other side: walking the lane moves the cut. I and K move it too. M steps through `tesseract`, `cell5`, `cell16`, `cell24`, `tube`, `ball`. X shows the dithered shadow round the slice. Small handle and chip boxes are gaze targets | `slicer_tesseract`, `slicer_tube`, `slicer_shadow` |
| 14 | Overlay `Home`; the menu row `Return to Home` from another space; and H from another space | Back in Home through a fade, each way. Pressed while already in Home: no fade, nothing visible changes | `home_return` |
| 15 | Overlay `tiny_standing`, then each space | Home: no lap outline, the centre mark stays. Glued: runs in place, no chevrons. Slicer: no lane; the body about 0.35 m from the centre. Impossible Rooms: a shallow booth whose door is in front of you and not under you (0.5 m is kept between the middle of the floor and the door where the floor allows), or the real room alone. The full flow's headset step says `This floor is small, so you will use standing mode: stay on the spot and turn; do not walk.` | `standing_<space>` |
| 16 | `sample_3x3`, then overlay `replay` | The head follows a recorded walk from `recordings/` on a loop. The newest `walk_*.poselog` by file time is played; on a tie the later name wins. To choose one, start with `-atlasReplay <path>`, or through MCP set `Atlas.App.AtlasBootstrap.App.SourceSwitch.ReplayPath` and then `pose.source` to 1 (to 0 first if it is 1 already). With onboarding in charge the replay does not pick the room: have `sample_3x3` active first. `walk_lap_sample_3x3`: one lap of the lap square, 0.5 m from the walls; no STRONG on the straight sides. `walk_wall_approach`: straight at the wall at 1.0 m/s, stops 0.3 m short; the cue must be up before it stops. `walk_jump_and_loss`: a 0.5 m pose jump at 1.5 s that the cue sees and the geometry ignores, then one second of limited tracking that raises the white "stand still" ring. Overlay `walker` returns the keys | `replay_lap`, `replay_wall`, `replay_jump` |
| 17 | Game view > Stats in each space, with `dev.overlay` off | Batches at most 300 and Tris at most 500k, for both eyes together, everywhere. Set-pass calls at most 60. Per space, on `budget_room`: Home at most 60 draw calls and 130k triangles (the room alone is 28 draws); Glued 5 x 5 at most 150 and 250k; Glued 3 x 3 x 3 at most 160 and 250k; Curved Walk at q = 5 and ρ = 3 at most 210 and 250k; Aligned Cells at most 170 and 200k; Small Planet at most 70 and 190k; Impossible Rooms at most 60 and 150k; Slicer at most 70 and 160k. The Home and Glued figures are constants in the code (`RoomBudget`, `GluedBudget`); the others are the plan's (section 8.5, lane A §15) | `stats_<space>` |
| 18 | Leave it running for a minute in each space, Console open | No red line. No message that repeats every frame. The Profiler shows 0 B of managed allocation per frame in `AtlasFrameLoop.Update` and `LateUpdate` once a space runs (the overlay and the Editor itself allocate; judge with `dev.overlay` off) | `console_clean` |
| 19 | Stop play mode | No red line. No `Atlas` object, eye texture or pooled material left in the scene. Enter play mode again: it boots the same way (also with Enter Play Mode Options set to no domain reload, if the owner uses that) | `stop` |
| 20 | Start with `-atlasRoom budget_room -atlasPerf tour -atlasPerfVisit 5` | A scripted run through S0, S1 5 x 5, S1 3 x 3 x 3, S2 at q = 5 (the head stands 5 s first so the curvature ramp is over), S3, S4, S5 and S6. Logs `Atlas: performance run tour started, ...` and `Atlas: performance run finished: ...`. In the Editor every row ends `incomplete`, which is correct there. Files `tour_<session>.csv`, `perf_<session>.csv`, `events_<session>.jsonl` under `~/Library/Application Support/DefaultCompany/ATLAS/logs` (the Editor's `persistentDataPath`; every play session writes its event log there) | `perftour` |
| 21 | In Home, headset view, menu closed: Page Up four times, Page Down four times, Tab, then Tab again from the `Quality` page, H in Glued | Page Up: Glued, Curved Walk, Aligned Cells, Small Planet, Impossible Rooms, Slicer, Home, each through a fade; a press during a fade is ignored, so wait for each. Page Down: the same backwards. Tab opens the menu on `Spaces`, and from another page jumps back to it. H returns Home. Then set `demoLock` on (menu > `Parameters`, or the Console's lock): all four keys do nothing, the overlay and the menu's rows still switch. Set it off again. In the hand view (overlay `Back in the hand` needs the no-onboarding start; otherwise before the headset step) none of the four does anything | `actions_next`, `actions_menu`, `actions_locked` |
| 22 | With onboarding in charge (the fast or the full way), in Home: F1 > page `Session` > `Rescan the room` | The menu closes, the view leaves the headset for the hand, and the card shows `Your room` with `Scan this room`, `Outline by hand` and `Sample room (no scan)`. The room of the earlier scan is not drawn over the camera picture. Started with `-atlasRoom` or `-atlasReplay` instead, the same row starts the rehearsal flow's scan (the overlay then shows `End sweep`) | `rescan` |
| 23 | With onboarding in charge and `sample_3x3` active, set `perf.run` to `tour` in the menu and let it end | The run stands in `budget_room`; when it ends the room in force is `sample_3x3` again (the overlay's `Room:` line), Home is rebuilt in it, and the walls Bump Guard watches are that room's. `perf.run` = `ladder` stays in the active room throughout | `perf_roomback` |
| 24 | Overlay `S2` (Curved Walk) in `sample_3x3`, `walker`, standing still; then `curved.q` = 5 or the D-pad (the left and right arrow keys are `DialLeft` and `DialRight`; M is `Primary`, the mode switch) | Fade through black, then the room as in Home, with a head-locked read-out and three chips (dial left, dial right, mode switch). Standing for about 4 s turns the curvature on: the walls sink to kerbs 0.9 m high, the floor is hidden, tiles of the {4,5} world appear round the room and fade into the fog at ρ = 3, the furniture stays rigid. The Console's `Map` tab shows the dial and the tile. Walking while the ramp runs stops it (the speed gate is 0.1 m/s); a D-pad press while walking changes nothing. `curved.q` = 6 gives bare {4,6} tiles, 4 turns the curvature off again | `curved_q5`, `curved_q6` |
| 25 | In S2 at q = 5 walk the 3-4-5 loop: flag corner, then four sides of the lap square turning left (or `-atlasReplay recordings/walk_lap5_3x3.poselog` with `-atlasSource replay`) | Beacons mark the corners; the read-out counts corners and the distance to the flag; after five corners the walker is at the flag again to under 0.25 m (`walk.closed`, `closure_m`), having turned about 90° against the room (`turn_deg`). The tile id in `State` changes at each crossing; every tile run lasts at least 0.25 s; no flicker at an edge. The Console's map shows the tile and the dial | `walk_lap5` |
| 26 | In S2 switch `curved.mode` to Mode 2 (Console or `Registry.Set`), then back with the menu's `Return to Home` | Fade to S3 (Aligned Cells) with the tile id and pose S2 stood in (the `cells.enter` event has `fromMode1` 1); the kernel is flat again; the room is registered: virtual and real walls coincide. The free floor is cut into square cells, each drawing one tile; the corner posts hide the seam; every cell crossing logs; the furniture is drawn once per eye and is not clipped. `cells.viewDepth` 0 to 2 changes how many neighbours are drawn | `cells_mode2` |
| 27 | Overlay `S4` (Small Planet) in `sample_3x3`; or in S2 dial to 3 and stand still | The room's kerbs and furniture sit on a small sphere of radius R = 2s/π (R3 of the room) with a pole beacon; six faces are always drawn; the fog starts at 0.5π R and is opaque at 0.8π R; looking round 360° shows no smeared triangle near the far side (`AtlasWarpClip` discards beyond 2.6 rad). Three corners of the lap close the loop (`walk.closed` with q = 3). `planet.R` and `planet.fogRho` change the size and the fog (on release for R) | `planet_s4` |

**The full flow, once, in branch S** (the rehearsal of the owner's phone flow): `Start` > `Continue without
Station` > sweep the simulated room > `Done` > `Looks right` > `Switch now` > Home. Expect the outline on the
floor during the sweep, `Floor: found, <area> m2` in the card, the white, green and yellow lines on the
confirm screen, and Home built from that scan. In XR Simulation no object photo is taken (the camera
intrinsics are zero there), so every scanned object takes a built-in mesh: correct.

**Most likely failures, and where to look.**

| Symptom | Look at |
|---|---|
| `Atlas: boot failed. The app is not running.` | The exception logged just before it; `App/AtlasApp.cs` `Build` runs the 20 steps in order |
| The card is missing or unstyled | `App/Onboarding/OnboardingView.cs`: the `PanelSettings` is made in code and takes its theme from `Resources.Load<ThemeStyleSheet>("AtlasUi/AtlasRuntimeTheme")`. A warning `Atlas: AtlasUi/AtlasRuntimeTheme not in Resources; the onboarding screen has no theme.` means the `.tss` did not import as a theme: sizes and colours are still set in code, the text field and buttons lose their base styling. `Atlas: the onboarding panel could not be created.` means no card at all. The font is the built-in `LegacyRuntime.ttf` |
| H, Page Up, Page Down or Tab does nothing in a running space | `App/Commands/SpaceActions.cs`, `Refused`: the hand view, a switch under way, or `demoLock` on. Otherwise `SpaceActionStep` (added to the frame's tail in step 20 of `AtlasApp.Build`, just before SpaceLab) and the key bindings in `Input/Core/InputTwins.cs` |
| Page Up lands on a space that is not built, or never wraps | `SpaceActions.TryNeighbour`: it skips every space `IsAvailable` rejects |
| A click on an overlay button still skips the headset countdown, or clicks right of the box are dead | `App/Flow/DevOverlayLayout.cs` against what IMGUI really draws; `OnboardingView.ToScreen` (the panel's coordinates to screen pixels from the bottom left) |
| `Rescan the room` leaves the hand view with no card | `OnboardingInstaller.OnRescan` is subscribed to `OnDeviceMenu.RescanRequested` only when a menu is installed; `OnboardingFlow.Rescan` answers only in the step `Home` |
| Buttons do not react | The Input System UI module for UI Toolkit; the overlay (IMGUI) reacting while the card does not narrows it |
| The scan never starts in branch S, or the head is far outside the fixture room | XR Simulation; use the fast way with `walker` and report what the simulation did |
| Black stereo view | Step 3c, the lens blit. Then `Rendering/Stereo/EyeTextureSet.cs` |
| Home never appears after the headset step | `App/Room/RoomHandOver.cs` (waits for the walker reset), `App/Spaces/SceneSpaceLoader.cs` (additive load, then the factory) |
| HUD text missing | `Telemetry/Hud/AtlasHud.cs` needs `TMP_Settings.defaultFontAsset`; the TextMesh Pro essentials are under `ATLAS/Assets/TextMesh Pro/` |
| Cue never shows | `Safety/BumpGuard.cs`: the cue is asked for only while `Armed`, which the headset step sets |
| A space button does nothing | The event log line `space.refused` with `noRoom` or `unavailable`; a failed load logs `space.loadFailed` and `space.error` and falls back to Home |
| `materials.grow: the material pool doubled to ...` | A leak of pooled materials across space switches; the pool starts at 256 |
| Step 8b of the frame never runs (no HUD refresh, switches never advance) | `App/Frame/AtlasFrameLoop.cs` hooks `endContextRendering` |

### 3f. The Station on the Mac

Everything here runs on the Mac alone. The Hub listens on UDP 9050 and TCP 8080, the Forge on TCP 8000.

**1. The suites, with no Unity.**

```sh
dotnet test station/hub.tests
( cd station/forge && python3.11 -m venv .venv && source .venv/bin/activate && pip install -r requirements.lock && python -m pytest tests )
( cd station/console-tests && npm test )
bash tools/e2e/run_station_rehearsal.sh        # real Hub + real Forge (procedural) + ReplayPhone; exit 0 = every check passed
```

**2. Run the Hub and the Forge.** Two terminals.

```sh
dotnet run --project station/hub -- --advertise 127.0.0.1
```

```sh
cd station/forge && source .venv/bin/activate
FORGE_GENERATOR=procedural uvicorn forge.app:app --host 0.0.0.0 --port 8000
```

Correct: the Hub prints its addresses, the pairing URL and the session folder; `curl -s
http://127.0.0.1:8000/health` answers with `"generator": "procedural"`; `curl -s
http://127.0.0.1:8080/api/session` shows `forge.up` true. Use `--advertise <the Mac's LAN address>` instead
when a phone is to connect (step 3h).

**3. Pair the Editor.** The Editor runs as the phone of the link. Press Play, `Start`, and on the `Station`
screen type `127.0.0.1` into the address field and click `Use this address`. Accepted forms are
`atlas://<ip>[:port]`, `<ip>:<port>` and `<ip>` (dotted IPv4 only).

Correct: `Station connected.` for one second, then the flow moves on; the link dot in the headset view turns
green; the HUD's last line shows `link <rtt> ms <loss>%`; the Hub prints one status line a second. The
address and the paired flag are kept in `PlayerPrefs` (`atlas.hub.address`, `atlas.hub.paired`), so the next
Play connects at boot. To test a first launch again, delete those two keys.

**4. The Console.** Open `http://127.0.0.1:8080/` in a browser. Tabs: `Tuner`, `Operator`, `Dashboard`,
`Map`, `Assets`, `Pairing`; `STOP` sits in the strip at the top of every tab.

| Check | How | Correct looks like |
|---|---|---|
| Mirror data | `Map` tab; `curl -s http://127.0.0.1:8080/api/state` | The floor plan of the room with the head moving as you walk; the space code and cell as text |
| Live tuning | `Tuner`: move `render.lineWidth`; change `glued.copies` while in Glued | The Editor follows within a frame or two; the value is clamped to its range; the groups of spaces not entered are folded |
| Telemetry | `Dashboard`; `curl -s http://127.0.0.1:8080/api/telemetry` | One row per second; charts advance; `station/hub/logs/<session>/perf.csv` grows; the folder is named after the phone's session id |
| Switch space | `Operator`: the space buttons, or `curl -s -X POST http://127.0.0.1:8080/api/scene -d '{"space":"S1","variant":"klein","args":{}}'` | The Editor fades to Glued in the Klein layout. A variant sets `glued.mode` for S1, `rooms.layout` for S5 (`booth`, `loop`) and `slicer.object` for S6 (for example `cell24`), by name or by index. |
| Return to Home | `Return to Home`, or `-d '{"cmd":"home","args":{}}'` to `/api/op` | Home |
| Recentre | `Recentre`, or `{"cmd":"recentre","args":{}}` | The walker is put back on the floor under the head; nothing else changes |
| Quality | `{"cmd":"quality","args":{"tier":2}}`; back with `"tier":-1` | `thermal.pinTier` 2: eye textures at scale 0.6 with 2x MSAA, `glued.copies` 3; the HUD shows `tier 2`. Tier 3 also drops to 30 fps. Each within 1 s |
| STOP | The `STOP` button or the space bar in the browser, or `{"cmd":"cue","args":{}}` | The STRONG cue, with tone, for 3 s, whatever Bump Guard says; the world keeps rendering |
| Lock | `{"cmd":"lock","args":{"on":true}}`, then try a slider | `demoLock` on; parameter writes answer 423; the link dot is hidden while connected; H, Page Up, Page Down and Tab do nothing in the Editor (step 3e row 21). Unlock with `"on":false` |
| Lock set on the phone | In the Editor's menu (F1 > `Parameters`) switch `demoLock` on, then off | The Hub follows the phone's `State`: its terminal prints `demo lock on (set on the phone)`, the Console shows the lock and answers 423, and back again when switched off |
| Leaving | Stop play mode with the Hub running | The Hub prints `phone left: <reason> (peer <n>)` at once, not after a time-out: the link's `Bye` travels inside the disconnect |
| Hub off | Stop the Hub (Ctrl+C) while walking | Only the link dot and the HUD's link field change; frame time and the cue are unchanged. Start it again: the link returns and parameters are restored |

**5. Assets into Home.** What the Editor can show:

- A fixture room never announces a scan, so no crop is sent. Two seconds after Home is built, a placeholder
  box takes its built-in mesh (`sample_4x5_table`: the table) with a 0.3 s dither cross-fade. The stand-ins
  of `budget_room` are left alone.
- The Forge's own half (room upload, object jobs, GLB download and validation) is exercised by
  `tools/e2e/run_station_rehearsal.sh`.
- The whole trip (crop, Forge job, GLB, mesh placed in Home) needs a scan on a phone: step 3h.

**6. The Spectator.** It has a scene of its own, `ATLAS/Assets/Atlas/Scenes/PC_Spectator.unity` (one empty
object, `AtlasSpectatorBoot`; never in the iOS build list), and a Windows build method. The walker's app
does not boot in that scene. A second Editor cannot open the same project folder, so in the Editor the
phone is played by ReplayPhone:

- Start `dotnet run --project tools/ReplayPhone -- --hub 127.0.0.1:9050` (a synthetic walk as the phone).
- In the Editor open `PC_Spectator` and press Play: the Spectator starts by itself. The older way still
  works: a new empty scene, Play, then the menu **Atlas > Start Spectator (play mode)** (in `Atlas_Boot` it
  refuses with `Atlas: the walker's app is running in this scene; the Spectator needs a scene of its own.`).
- It dials `127.0.0.1:9050`, or the Hub named with `-atlasHub <ip[:port]>` on the Editor's or the player's
  command line, and nothing else: an address the walker's app stored in `PlayerPrefs` is not used and the
  Spectator stores none.
- Correct: `Atlas: Spectator booted. <N> parameters, 7 spaces registered, Hub at 127.0.0.1:9050` in the
  Console; two viewports side by side, the mirror view (72 % of the width) and the panels (28 %); the room
  outline once the Hub has one; the head following the walk; space switches following the phone.
- Furniture: the Spectator shows the same objects as the phone. A box takes its built-in mesh where the
  phone placed one, and a Forge mesh is downloaded by its hash from the Forge the Hub announced; nothing is
  uploaded from the Spectator. With ReplayPhone's synthetic walk and no Forge, expect boxes or built-in
  meshes only. A line `Atlas: Spectator: ...` is a warning about the built-in mesh set.
- The curved spaces: the Spectator's kernel is the phone's, one `State` late (it takes tiling, scale, curvature and
  pose from each `State` and is never stepped). With a phone (or ReplayPhone) in S2, S3 or S4 the mirror shows
  the same world as the phone, a generated world whose hash differs from the phone's shows "world mismatch", and
  the flat twin draws the Poincaré disc of the walker's tile (S2 once the curvature is on, S3 round the cell's
  tile) or the cube net (S4). Replaying `recordings/walk_lap5_3x3.poselog` through ReplayPhone gives the tile
  sequence of the five-corner lap.
- Never: `Atlas: the Spectator could not start.`
- To watch the Editor-as-phone instead, the Spectator needs a second copy of the `ATLAS` folder outside the
  repository, opened in a second Editor. Optional.

The Windows build of the Spectator (never run). With the Editor closed:

```sh
"$UNITY" -batchmode -quit -projectPath "$REPO/ATLAS" -buildTarget StandaloneWindows64 \
  -executeMethod Atlas.EditorTools.AtlasBuild.Spectator -buildOutput Build/Spectator/AtlasSpectator.exe \
  -logFile "$REPO/docs/verification/logs/spectator-build.log"
```

It builds `PC_Spectator` first, then the enabled `Space_*` scenes of the build list, for
`StandaloneWindows64` with the define `ATLAS_SPECTATOR`. It needs Unity's Windows Build Support module on
the Mac; whether that module is installed is not established from the source, and `-buildTarget` re-imports
the assets for Windows. Correct: exit code 0, `ATLAS/Build/Spectator/AtlasSpectator.exe` and its data folder
exist, no `error CS` in the log (the harness compiled the same sources with `ATLAS_SPECTATOR` in its
Station configuration; this is the first build by Unity itself), and the log holds
`Atlas: render set-up applied for the build.`, because `BeforeBuild.OnPreprocessBuild` runs for every
player build. Do it after step 3e, and switch the Editor back to the iOS target afterwards. Running the
`.exe` is for the Station PC (risk K8); do not judge it on the Mac.

**7. ReplayPhone with no phone.**

```sh
dotnet run --project tools/ReplayPhone -- --hub 127.0.0.1:9050 --seconds 30
dotnet run --project tools/ReplayPhone -- station/hub/logs/<session> --loop
```

Correct: the Console shows a phone connected, the map moves, the Dashboard gets a row a second, sliders and
the six commands are answered by the synthetic walk (a recording ignores them). Exit code 1 means no Hub
answered within 10 s.

**Capture.** `YYYYMMDD_3f_console_<tab>.png` for each tab, `YYYYMMDD_3f_station_connected.png`,
`YYYYMMDD_3f_stop_cue.png`, `YYYYMMDD_3f_spectator.png`; the rehearsal script's output in
`logs/station_rehearsal.txt`; `logs/spectator-build.log` if you ran the Windows build.

**Most likely failures, and where to look.**

| Symptom | Look at |
|---|---|
| The Editor never connects | `Telemetry/TelemetryInstaller.cs` creates `Atlas.Link.LiteNetTransport` by name; the event log then holds `link: the UDP transport is missing from this build; running without a Station`. Also the macOS firewall prompt for the Hub |
| Connected, but the Hub's folder has another name than the phone's session | `LinkClient.SetSession` and the Hub's session adoption |
| A slider does nothing | The Hub log `hub.log` shows what was sent; the phone's `events_<session>.jsonl` shows the `param` event |
| `uvicorn` not found | The Forge's `.venv` is not active |
| The Spectator view is black or one viewport overwrites the other | `Spectator/SpectatorView.cs`: two cameras with viewports under URP |
| Play in `PC_Spectator` starts nothing | `Spectator/SpectatorBoot.cs`, `ShouldBoot`: the active scene's name must be `PC_Spectator`, or a root object must be named `AtlasSpectatorBoot` |
| The Spectator connects to another address than the one given | `Spectator/SpectatorApp.cs`, the end of `Build`: `pump.Pairing = null`, `SetAddress`, `Start` |
| The Spectator's furniture stays boxes while the phone shows meshes | `Spectator/SpectatorFurniture.cs` and `Telemetry/Map/SpectatorAssets.cs`; the Forge address comes from the Hub's `Welcome` |
| The Spectator build fails with errors only under `ATLAS_SPECTATOR` | Code inside `#if ATLAS_SPECTATOR` (`Spectator/SpectatorBoot.cs`, `App/AtlasBootstrap.cs`); the asmdef `Spectator/Atlas.Spectator.asmdef` (Editor and `WindowsStandalone64` only) |

### 3g. The iOS build

**Do.** Close the project in the Unity Editor. On a new clone run `tools/setup_git.sh` once. Then, from the
repository root:

```sh
ATLAS_TEAM_ID=XXXXXXXXXX ATLAS_DEV=1 tools/build_ios.sh
```

| Variable | Meaning |
|---|---|
| `ATLAS_TEAM_ID` | The Apple team id of the signing owner (his Personal Team). Needed on the first build; Xcode keeps it in the exported project afterwards. Ask the owner for it |
| `ATLAS_DEV=1` | Development build: Metal HUD on, debugger allowed, Xcode configuration Debug. Numbers that are published come from a release build (leave it out) |
| `ATLAS_NO_INSTALL=1` | Stop after the Xcode build |
| `ATLAS_LAUNCH=1` | Launch the app on each phone after installing |
| `ATLAS_UNITY=/path/Unity` | The Unity binary, if not in the Hub's default folder |

The script exports the Xcode project to `ATLAS/Build/iOS` in append mode, builds with `xcodebuild
-allowProvisioningUpdates` and installs with `xcrun devicectl` on every paired phone. Logs:
`ATLAS/Build/unity-build.log` and `ATLAS/Build/xcodebuild.log`.

**Correct looks like.**

- `ATLAS/Build/unity-build.log` contains `Atlas: render set-up applied for the build.` and no `error CS`,
  `Shader error` or `BuildFailedException`.
- The script prints `built: <path>.app`, then `Done: <commit> is on the phone(s).`
- `Info.plist`, written by `Editor/AtlasPostBuild.cs`. Check with
  `/usr/libexec/PlistBuddy -c Print ATLAS/Build/iOS/Info.plist`:

| Key | Value |
|---|---|
| `NSCameraUsageDescription` | "ATLAS uses the camera to track your position, scan your room, photograph objects and see your hands." |
| `NSLocalNetworkUsageDescription` | "ATLAS connects to the Station on your local network for tuning and telemetry." |
| `NSAppTransportSecurity` > `NSAllowsLocalNetworking` | true |
| `UIFileSharingEnabled` | true |
| `UIRequiresFullScreen` | true |
| `CFBundleURLTypes` | one entry, scheme `atlas` |
| `MetalHudEnabled` | true, development builds only |

- Build setting `ENABLE_BITCODE = NO` on the project, the main target and the UnityFramework target.

**Expected warnings; do not report these.** Obsolete-API warnings (`warning CS0618`) from the ARKit mesh
classification calls in `Atlas.Scan.ARKit` (the API is obsolete in AR Foundation 6.5 and has no replacement
before Unity 6000.4); Xcode's "Update to recommended settings"; warnings inside the generated IL2CPP C++.
The exact wording Xcode 26 uses is not established from the source.

**In Xcode** (open `ATLAS/Build/iOS/Unity-iPhone.xcodeproj` if the script cannot install, and press Run):

| Check | Correct looks like |
|---|---|
| Native plugins | `AtlasThermal.mm` and `AtlasHaptics.mm` compile. The link needs no CoreHaptics framework: the haptics are UIKit impact generators (`AtlasHapticPrepare`, `AtlasHapticPulse`, `AtlasHapticStop`; `AtlasThermalState`) |
| Deployment target | iOS 17.0 |
| Signing | Automatic, the owner's Personal Team, bundle id `com.winstonthov.atlas` |
| Launch arguments | Product > Scheme > Edit Scheme > Arguments takes the `-atlas...` arguments of step 3e |
| Device log at launch | `Atlas: eye textures 1118 x 1032 ...` (read the formats, the sRGB flag, `watermark visible False`, the dpi), `Atlas: AR backend ARKit`, `Atlas: ARKit video <W>x<H>@60`, `Atlas: ARKit depth 256x192`, `Atlas: booted. ... head from arcamera`. A warning `Atlas: ARKit depth: no image 5 s after the first request ...` is a fault |
| GPU capture, headset view, release build (task `P1-A05`) | One render pass per eye that holds the opaques, the portal stencil passes and the head-locked passes; colour store action MultisampleResolve; depth and stencil DontCare; no pass loads a multisampled attachment; memoryless MSAA accepted; exactly one blit to the screen; no intermediate texture, no extra render target |

**Most likely failures, and where to look.**

| Symptom | Look at |
|---|---|
| `BuildFailedException: Atlas: the render set-up could not be applied` | Step 3b: `IsRenderSetupCurrent()` |
| A shader error only in the player build | The Editor compiled for the Mac; this is the first compile for iOS Metal |
| Undefined symbols `_AtlasHaptic...` or `_AtlasThermalState` | `ATLAS/Assets/Plugins/iOS/*.mm` and their `.meta` (platform iOS only) |
| "requires a development team" | `ATLAS_TEAM_ID` |
| Types missing at run time (stripping Low) | `ATLAS/Assets/Atlas/Telemetry/link.xml` keeps `Atlas.Link.LiteNetTransport`; the seven spaces register themselves from `RuntimeInitializeOnLoadMethod` and nothing references their assemblies directly. `7 spaces registered` in the boot line proves they survived |
| The install fails | Cable in, phone unlocked, Trust This Computer, Developer Mode on; first launch: Settings > General > VPN & Device Management > Trust |

### 3h. On the phone

Hand first, headset second. Record the thermal state at the start of every measurement. To see and record
the phone's screen: QuickTime Player > File > New Movie Recording > the iPhone as camera. The app's files
are under Finder > the iPhone > Files > ATLAS > `logs`.

**H1. Gate G1, first light. Phone in the hand, development build.**

- [ ] The camera prompt appears with the phone in the hand. The flow expects it on the `Camera` screen after
      `Start`; the AR session of the boot scene may raise it at launch instead. Note when it came. Accept it.
- [ ] Metal HUD reads 60 fps. Record CPU and GPU time. The plan's G1 marks are for the empty stereo scene:
      CPU main at most 3 ms, GPU at most 2 ms; today's build draws more, so record and do not judge.
- [ ] Two eye images after the headset step; both move when the phone moves. Nothing is magenta.
- [ ] The thermal plugin returns a value: the HUD shows `therm 0` (or higher) and the menu's `Quality` page
      shows the tier in force.
- [ ] The log shows `Atlas: ARKit video <W>x<H>@60` (the lowest-resolution 60 Hz format) and
      `Atlas: ARKit depth 256x192`.
- [ ] The phone reports LiDAR; the rear camera and LiDAR are not covered by the headset.
- [ ] Pairing: open `http://<Mac>:8080/pair`, scan the QR code with the iPhone Camera; or type the address
      on the `Station` screen. The Local Network prompt appears there and not before. `Station connected.`
- [ ] The Hub's log has `State` packets; the Dashboard shows one telemetry row per second.

**H2. Scan and Home. Phone in the hand, then in the headset on a table.**

- [ ] At most three questions on the way: `Start`, `Done`, `Looks right`.
- [ ] Sweep: the floor outline grows on the camera picture; the card shows `Floor: found, <area> m2` and
      `Objects: <n>`.
- [ ] Confirm: the outline, the free rectangle and the lap square lie on the real floor. `Adjust` lets a
      corner be dragged.
- [ ] After `Looks right` the log shows `Atlas: scan features off: meshing, planes, boxes` and
      `Atlas: scan confirmed: meshing and plane detection are off`; depth stays on.
- [ ] Scan end to walkable Home: at most 90 s.
- [ ] Free rectangle against a tape measure: at most 0.10 m error.
- [ ] Home in the headset: a touched real wall lies within 0.05 m of the virtual wall at three points
      (should: 0.03 m). The centre mark is at the anchor; the lap square is inside the free floor.
- [ ] A 1.4 x 1.4 m free floor selects standing mode.
- [ ] Straight lines look straight through the lenses. If not, tune `render.lensK1`, `render.lensK2`,
      `render.lensSeparation`, `render.screenToLens`, `render.lensOffsetY`, and `render.headOffsetX/Y/Z`
      from the Console and record the values (section 5: today's numbers are placeholders).
- [ ] Pre-scan path, with `scan.teamPrescan` on: relaunch, `Use the saved room`; the log line
      `Atlas: relocalised in room <id> after <n> s, ...`; at most 10 s and 5 cm.
- [ ] Pre-scan path in another room: `Use the saved room` fails after at most 15 s with
      `Atlas: world map dropped and tracking restarted: the room was not recognised`, and the new sweep
      that follows finds the floor (risk C12).
- [ ] The menu's `Rescan the room` from Home: hand view, the `Your room` card, a new scan replaces the room.
- [ ] A paired gamepad in the headset: hold B for 1 s to go Home (a tap is still Back); d-pad up and down
      cycle the seven built spaces and wrap; start opens the menu on `Spaces`; all inert during a fade, under
      the Console's lock and in the hand (risk F11).
- [ ] With the Forge running: `Atlas: object photos: choosing from <N> key frames for <n> objects` with
      N above 0; boxes first, then meshes; every box ends as a generated or a built-in mesh; at most 60 s
      from job start to placed mesh; worst frame under 20 ms during a swap. Crops upright, not mirrored.
- [ ] With the Forge stopped: every box holds a built-in mesh within 10 s of entering Home.

**H3. Bump Guard, with a spotter. Do not skip, do not shorten.** Until this block passes, the walker is a
team member, the floor is clear and a spotter stays within arm's reach.

- [ ] In the hand first: walk the phone at a wall in the headset view. WARN, then STRONG; indicator, tone
      and haptic together. WARN pulses twice a second (medium), STRONG six times a second (heavy); the two
      can be told apart.
- [ ] The cue's direction: the chevron and the tick come from the side of the hazard.
- [ ] Cover the camera or shake the phone: the white "stand still" ring within 0.5 s.
- [ ] Protocol 11.4.5 of the Master Plan: 20 scripted approaches to a wall, a scanned table, a chair and a
      0.45 m stool placed after the scan. Pass: no missed warning; lead distance at 1.0 m/s at least 0.60 m;
      lead time to predicted contact at least 0.6 s.
- [ ] Five-minute lap of the lap square: at most 2 STRONG cues, and WARN cues on the straight sides, in
      total. Corner WARNs are reported, not judged.
- [ ] Static-map error after the lap: at most 0.10 m at three points.
- [ ] The cue for WARN, STRONG and tracking lost starts within one frame, 20 of 20.
- [ ] With the Hub stopped, and with the Station cable pulled while walking at an obstacle: the cue is
      identical.
- [ ] Frame-time cost of live depth, p95: at most 1.0 ms.

**H4. The Station link. Own router; `iproxy` does not carry the link.**

- [ ] Launch to connected: at most 3 s (p95 of 10 launches); with the first-launch prompt at most 5 s after
      "Allow".
- [ ] `State` at the Hub: at least 50 Hz mean, loss under 2 %. Latency p95 at most 25 ms, p99 at most 50 ms.
- [ ] Sliders: visible within 100 ms; clamped; ignored under lock.
- [ ] Each of the six operator commands within 250 ms; STOP gives indicator, tone and haptic and the world
      does not change.
- [ ] Each tier forced from the Console is in force within 1 s (`perf.run` = `ladder` measures it).
- [ ] Telemetry over 15 minutes: no gap over 2 s; the phone's `perf_<session>.csv` and the Hub's `perf.csv`
      are the same bytes.
- [ ] Router off for 10 s: offline within 3.5 s, connected within 5 s of return, parameters restored, frame
      time unchanged. App in the background for 10 s: the same.
- [ ] With the Hub off, change `link.enabled` from the menu's `Quality` page: a `param.set` event is in the
      phone's `events_<session>.jsonl`.
- [ ] `perf.run` = `tour` on `budget_room`, release build, cold, in the closed headset: the per-space table
      of `docs/evidence/README.md`. Budgets: 300 draw calls, 60 set-pass calls, 500k triangles, frame time
      p95 at most 20 ms, 1 kB of managed allocation per frame.

**H5. The curved spaces, with a spotter. Gate G2. Do not start before H3 has passed.** The walker is a team member,
the floor is clear, Bump Guard is armed, and the spotter stays within arm's reach. In a curved space the virtual
walls are no longer the real walls once the curvature is on (plan §4.9); Bump Guard, in room coordinates, is the only protection.

- [ ] S2 in `sample_3x3`'s real room: stand at the centre, turn the dial to 5 (D-pad right) and stand still for
      about 4 s. The room turns into kerbs, the tiles of the {4,5} world appear and fade into the fog at ρ = 3.
      The ramp does not run while walking.
- [ ] The 3-4-5 walk: from the flag, five sides of the lap square turning left. `walk.closed` with `closure_m`
      under 0.25 m and `turn_deg` about 90 (read them on the Console or in `events_<session>.jsonl`).
- [ ] Tile crossings: the tile id changes once per crossing and no tile flickers (every run of one tile at least
      0.25 s); frame rate 60, 0 B of managed allocation on crossing frames.
- [ ] Return to Home (B held 1 s): the room is registered again; a touched real wall lies within 0.02 m of the
      virtual wall at three points.
- [ ] S3 from S2 (Mode 2): the tile id is the one S2 stood in; the walls and furniture coincide with the real ones
      (table edge within 0.05 m at three points after ten cell crossings).
- [ ] S4: three corners close the loop; no smeared triangle in a 360° look-around; fog opaque at 0.8π R.
- [ ] Standing mode (a 1.4 × 1.4 m floor): the dial snaps 3 to 6, no flag or beacon, no walk.
- [ ] Performance: `perf.run` = `tour` on `budget_room`, release build, cold: the S2, S3 and S4 rows of
      `docs/evidence/README.md` against §3e row 17.

### 3i. The tracked hands

The walker's hands, seen by the phone's rear camera, drawn in the headset view and used to press panels, to pinch and
to touch the room. Everything is off until `hands.enabled` is on, and it starts off. The code is
`ATLAS/Assets/Atlas/Hands/` and the plugin `ATLAS/Assets/Plugins/iOS/AtlasHands.swift`; the contract is in
`docs/contracts/lane-c-room.md` §7.7 and §13.7, `lane-a-platform.md` §10.9 and `lane-d-link.md` §1.4 and §2.6; the
design is `documents/ATLAS_Hands_Design.md`. This step has three parts: the tests that need the engine (part 1), the
Editor with the stand-in hands (part 2) and the phone (part 3, after H3 of step 3h).

**When hands run.** All of these must hold: the display is the headset view; no gamepad has been connected for a second;
`hands.enabled` is on; a source is available (`hands.editor` on in the Editor, the plugin on the phone); the head
comes from the AR camera (always so for the Editor's stand-in); the quality tier is Nominal, High or Fair. The event
`hands.on` says the detector started, and `hands.off` names the first row that failed: `mode`, `pad`, `param`,
`source`, `pose` or `heat`. With a gamepad connected nothing of this exists and every panel is where it was. Every
event of the hands is in `docs/contracts/lane-d-link.md` §2.6; `hands.bench` on gives one `hands.bench` event a second
with the session's figures (`hz`, `ageMsP50`, `ageMsP95`, `requestMs`, `mainMs`, `maskFrames`, `source`, `image` and
`depth` sizes).

#### Part 1. The tests that need the engine

**Do.** Test Runner > EditMode, search by class name, or the batch command with `-testCategory Unity` (section 2). These
sixteen are the `[Category("Unity")]` tests that the tracked hands added to those of step 3d; they are compiled out of
the headless run by `#if !ATLAS_HEADLESS`, and none has run in the engine.

| Class (file under `ATLAS/Assets/Atlas/Tests/`) · assembly | Tests |
|---|---|
| `HandsSkinUnityTests` (`Hands/`) · `Atlas.Hands.Tests` | 3: `TheNativeArrayOverloads_WriteWhatTheKernelWrites_AndTheMeshTakesThem` (the skinning that takes native arrays writes what the pointer kernel writes and `AtlasMesh.Upload` takes it: index and vertex counts, bounds hold the vertices), `BuffersThatAreTooSmall_AreRefused`, `TheSteadyFrame_AllocatesNothing` (`Is.Not.AllocatingGCMemory` over 20 frames of two skinnings and an upload) |
| `HandsAdapterTests` (`Input/`) · `Atlas.Input.Tests` | 2: `Input_Hands_NoHandsNoChange_TheServiceForgetsHandsThatAreNotSetAgain` (the real `AtlasInput` as `IHandsInput`: a fingertip hovers a row, is not replayed in a frame with no `SetHands`, and fires the row once as the gaze would), `Input_Reticle_DistanceRule_TheReticleMovesAndKeepsItsSize` (the reticle object stands at 0.45 m at scale 0.225 over a near target, and back at 2 m) |
| `GluedHandsUnityTests` (`Spaces/Glued/`) · `Atlas.Spaces.Tests` | 5: `WithNoHandsService_TheAvatarMeshIsTodaysAndNothingIsUploaded`, `TrackedHands_RideInTheSharedAvatarMesh_AndNoCopyGainsADrawCall`, `TheRebuildEveryFrame_AllocatesNothing_AsFarAsTheEngineCanTell`, `AFaultInTheHandsService_TurnsTheHandsOffForTheVisit_AndKeepsTheSpace`, `TheModule_HandsBackItsMaterials_AndLeavesNothingBehind_WithHandsToo` |
| `MenuReachUnityTests` (`App/Menu/`) · `Atlas.App.Tests` | 3: `TheNearTextShader_IsInResources_AndItsMaterialCarriesTheFontsTexture`, `MenuView_WithinReach_IsOnTheWorldLayer_TimesTheScale_WithDepthTestedText_AndGoesBack`, `SpaceSignsView_WithinReach_StandsOnItsSlots_OnTheWorldLayer_WithDepthTestedText_AndGoesBack` |
| `BumpGuardMaskFrameTests` (`Safety/`) · `Atlas.Safety.Tests` | 1: `BumpGuard_Mask_TheBurstJobGivesWhatTheInlineKernelGives` (a 256 × 192 image through the Burst job and through the inline kernel, masks included: the same samples masked, the same cells forgotten, the same level; counts and levels, not the bits of every float) |
| `CurvedWalkTwinsTests` (`CurvedWalk/`) · `Atlas.Spaces.CurvedWalk.Tests` | 1: `Near_TheDrawnChips_StandOnTheirSlots_OnTheWorldLayer_AndHideWhileAMenuIsOpen` (skipped when the project's TextMeshPro font is missing) |
| `SmallPlanetSpaceTests` (`SmallPlanet/`) · `Atlas.Spaces.SmallPlanet.Tests` | 1: `Module_Near_ThePlateStandsAtTheDialsSlot_TimesTheScale_OnTheWorldLayer` |

**Correct looks like.** All sixteen green. They find `Atlas/NearText` in `Resources`, a menu and a sign on the world layer
(layer 0) with every text under them on it and on the near-text material while near, and everything back on `AtlasHud`
when far.

**Most likely failures, and where to look.**

| Symptom | Look at |
|---|---|
| `TheSteadyFrame_AllocatesNothing` (hands) red | As the same message in step 3d: the Editor's job safety checks. Re-run with Jobs > Burst > Safety Checks off before you call it a fault |
| `TheNearTextShader_IsInResources...` red | `ATLAS/Assets/Atlas/Rendering/Shaders/Resources/AtlasNearText.shader` and its `.meta` (the shader must compile; read the Console for `Shader error in 'Atlas/NearText'`) |
| A near-layer assertion red for a text | A child object does not take its parent's layer: `MenuView.SetReach` and `SpaceSignsView` set the object and each text under it one by one |
| `BumpGuard_Mask_TheBurstJob...` red with a count that differs | Burst did not compile `BumpDepthJob` and the managed fallback differs, or the job reads a mask field the inline kernel does not: report both numbers; Bump Guard is not yours to change (section 1) |
| The Curved Walk test is skipped | The TextMeshPro essentials are not imported (`Assume.That(chips[0], Is.Not.Null ...)`) |

#### Part 2. The Editor with the stand-in hands (`hands.editor`)

**Do.**

1. Start as step 3e with the sample room and the walker: `-atlasRoom sample_3x3 -atlasSource walker`. The rehearsal flow
   enters the headset view by itself within about a second (not with `-atlasHand`).
2. Switch the hands on with the menu (F1, the Parameters page, group `hands`: `hands.enabled`, then `hands.editor`) or,
   with MCP for Unity, `execute_code`: `Atlas.App.AtlasBootstrap.App.Registry.Set("hands.enabled", 1);` and the same
   with `"hands.editor"`. `hands.on` appears in the event log within a frame or two and the two stand-in hands are in
   both eye images. Nothing of the stand-in is read while `hands.editor` is off.
3. Drive them. The source reads the Input System only, and none of its keys is read by the action map, the keyboard
   walker or the overlay; no mouse button is used (the left button is the lever, the right one turns the head):

| Input | What it does |
|---|---|
| Hold Left Alt (Left Option on a Mac) and move the mouse | The active hand moves across the view, one picture pixel to a mouse pixel, y up on screen being up in the picture. The palm may leave the picture by 400 px |
| Hold Left Alt and turn the wheel | The active hand goes away (wheel up) or nearer (wheel down), 2 cm a step, 0.25 to 0.70 m from the camera |
| Q, E | The left, the right hand becomes the active one (the right at first) |
| R | Shows or hides the active hand |
| 1, 2, 3, 4 | The active hand takes the shape open, pointing, pinch or fist; the fingers ease over 0.15 s |

The stand-in is two template hands in front of a made-up 1280 × 720 camera (a focal length of 1,000 px, a 256 × 144 depth
image), at 0.45 m, the left at palm pixel (490, 400) and the right at (790, 400). It reports each result 80 ms after
its frame, as the phone does, so the filter's prediction has its work to do. A hand with no joint in the picture is not
reported, and joints outside it come with confidence 0 and no depth.

**Correct looks like.**

- [ ] Both hands draw in both eyes, in front of the world and behind nearer things (a menu panel nearer than a hand
      covers it).
- [ ] A hand moved to the border of the stand-in's picture fades out; brought back it fades in. A hand moved out of
      the picture is gone within 0.40 s and the log has `hands.exit`; it comes back with `hands.enter` (an enter within a
      second of the last one for that hand is counted in `quietEvents`, not logged).
- [ ] Half a second after `hands.on` the menu chip stands within reach: about 0.48 m from the camera on a line 25°
      below the horizon, at 0.75 of its size, on the world layer. Looking down past 10° shows the four SpaceLab signs
      at their slots there, live only while drawn. The text of a row or a sign goes behind a hand held in front of it:
      the row and its text are covered together.
- [ ] Shape 2 (pointing): the index fingertip through a menu row from in front changes the value once, the click
      sounds, and the ring under the fingertip shows while it hovers; pulled back and pushed in again it fires again. The
      same row still fires by gaze dwell (0.7 s). A finger within 0.20 m of a row holds that row's dwell back.
- [ ] Shape 3 (pinch) with the gaze on a sign fires it, and with the gaze on nothing fires nothing. `hands.pinch` and
      `hands.touch` appear in the log with the label of the target.
- [ ] In the Glued rooms (S1) the copies show the hands, mirrored in the mirrored rows; the draw-call count does not
      change when they appear.
- [ ] A fingertip near a wall or the floor of the sample room shows a small glow on the surface, and a ring when it
      touches (within 1.5 cm); the ring fades over 0.4 s.
- [ ] `hands.draw` off hides the hands and the rings while presses still fire; `hands.sound` off silences the clicks;
      `hands.reach` (0.40 to 0.60) moves the panels nearer and farther.
- [ ] `hands.bench` on: one `hands.bench` event a second with `"source":"editor"`, `hz` near 30 and `ageMsP50` near 80;
      `maskFrames` rises while a hand is in view, at the synthetic depth image's 15 Hz.
- [ ] With a gamepad connected for a second none of it: `hands.off` with reason `pad`, no hands, every panel where it
      was and every action working from the pad.
- [ ] `hands.enabled` off: `hands.off` with reason `param` and the panels back as they were within a second.

**Capture.** `shots/YYYYMMDD_3i_<item>.png` of the Game view (hands over the menu; a finger in front of a row; the
copies in S1); the event lines of the hands from the Console or from `events_<session>.jsonl`.

**Most likely failures, and where to look.**

| Symptom | Look at |
|---|---|
| No hands, and `hands.off` says `source` | `hands.editor` is off, or there is no tracked camera (`Application.isEditor` and `boot.TrackedCamera` are what builds the stand-in) |
| No hands, and no `hands.on` or `hands.off` at all | `hands.enabled` is off with the display still in the hand, or a fault: look for `hands.fault` and for `Atlas:` lines with `hands` |
| `hands.off` says `mode` | The display is not the headset: `-atlasHand`, or the countdown of the flow has not finished |
| A hand moves with the head, or the mouse turns the head instead | A mouse button is down: the walker reads the mouse only while the right button is held; hold Left Alt with no button down |
| Text of a near panel paints over the hand | `Atlas/NearText` is not in the build: the log says `shader 'AtlasNearText' is not in Resources` once |
| A pressed row fires twice, or a brush past a row fires | `HandsTouch` (lane C §13.7): report the shape, the speed and the hand's distance; do not change the numbers |

#### Part 3. On the phone: the detector and the bench

**Preconditions.** H1 passed (the build runs, two eye images, LiDAR present). For anything that walks, H3 passed with a
spotter. Two things of the hands are not built yet and `docs/contracts/lane-c-room.md` §8.8 says whether they have
landed: the rule that near returns must be seen twice (R13) and the forearm's own depth gate (R14). **Do not walk with
the hands raised before both are in the build you test**; the checks below are done standing, with the phone's own
walker still, or sitting.

Switch `hands.enabled` on from the menu (the Quality page lists it last) or the Console, in the headset on the walker
or on a table with the phone's camera free. `hands.record` on writes `hands_<UTC time>.handlog` into the app's `logs`
folder (Finder > the iPhone > Files > ATLAS > `logs`): joints and depths only, never a picture. A log is the way to read
what the detector said (the record format is in `recordings/README.md`). Copy each into `docs/verification/logs/`
(git ignores it) and read it in the Editor with `execute_code`:

```csharp
var log = Atlas.Hands.HandsLogReader.FromFile("<path to the copied .handlog>");
var d = default(Atlas.Hands.HandsDetection);
for (int i = 0; i < log.Count; i += 15)
{
    log.Read(i, ref d, out var headPosition, out var headRotation);
    Atlas.Hands.DetectedHand hand = d.Hand0;     // d.HandCount tells how many hands the record has
    Debug.Log(i + " age " + d.AgeSeconds + " request " + d.RequestMs + " skipped " + d.Skipped + " image " + d.ImageWidth + "x" +
              d.ImageHeight + " depth " + d.DepthWidth + "x" + d.DepthHeight + " palm samples " + hand.PalmCount + " side " +
              hand.Side + " wrist " + hand.GetJoint(0).Pixel);
}
```

The plugin writes one `NSLog` line at the first frame after each start, `AtlasHands: first frame: ...` (picture size and format, depth size, whether
the depth has a confidence map, whether its shape is the picture's, intrinsics, the frame's age, tracking, the request
revision), and logs failed requests. Read it in Xcode's console or with Console.app on the Mac.

Order: BENCH 8 first, it is the build. BENCH 1 to 8 are the plugin author's own list (`AtlasHands.swift`, native
report); B7 and B8 are rows of the design's table in `documents/ATLAS_Hands_Design.md` §9.3, which has eleven (B1 to B11)
and whose other rows belong to the lead's bench. The numbers go in the report's table, next to the assumption they
test; the lead copies them into `docs/evidence/hands_bench.md`.

| # | Do | Correct looks like | A failure looks like |
|---|---|---|---|
| BENCH 8 | The first Xcode export with the new Swift file (step 3g): `nm -gU` on the built `UnityFramework` binary, and `hands.bench` on the phone | Six `_atlas_hands_` symbols (`start`, `set_rate`, `tick`, `poll`, `result_size`, `stop`); `hands.bench` shows `"source":"vision"` once the AR session exists | Undefined symbols at link; `"source":"none"` for good: the registration of `Atlas.Hands.ARKit` was stripped (it carries `[assembly: AlwaysLinkAssembly]`) or the AR session's native pointer is zero |
| BENCH 1, orientation | Hold one hand in the top-left quarter of the camera's picture for two seconds with `hands.record` on; then the right hand alone | In the log the joints of `Hand0` have `Pixel.x` under half of `ImageWidth` and `Pixel.y` under half of `ImageHeight` (the origin is the picture's top left), and the right hand's `Side` is 1 (left 0) | Joints in another quarter: the orientation constant of the request (`.up`, right for landscape left) is wrong for this phone. Sides swapped: report it, the lift votes on sides but starts from this label |
| BENCH 2, depth alignment | Read the `first frame` line | It says the depth size and either "the picture's shape" or "ANOTHER SHAPE than the picture: centred, unchecked". In the second case hold a fingertip at the top edge and at the bottom edge of the picture at a measured distance and read `Hand0.GetJoint(8).Depth` in the log: each reads its own distance | The second case with edge fingertips reading the wrong depth: `ArDepthSource` and the depth mask map by plain ratio and are off by 24 depth rows |
| BENCH 3, LiDAR confidence on skin | The same line says "with confidence" or "without". Hold a hand square to the camera at 0.35, 0.45 and 0.60 m, 20 s each | In the log `Hand0.PalmCount` is 12 or more (12 is `PalmMinSamples`) at each distance | Under 12 for long: the high-confidence-only rule starves the palm-plane fit. "Without" confidence: joints carry depth confidence 0 and no palm sample, so the hands run on size alone |
| BENCH 4, the frame is read on the main thread | Five minutes with the hands on, both eyes rendering | No ARKit warning about retained `ARFrame`s in the Xcode console; tracking still position and rotation; `mainMs` of `hands.bench` (the hands' whole main-thread time a frame, sense and upload) at most 0.6 ms (`HandsConstants.GovernorMainThreadMs`: above it for 2 s the rate drops to 15) | A retained-frames warning; `hands.rate` events with `why` `governor` |
| BENCH 5, clock and timing | `hands.bench` on for a minute at 30 Hz, on both phones | `ageMsP50` and `ageMsP95` (poll time minus capture time) between 20 and 200 ms, which also shows the camera frame's timestamp is on `CACurrentMediaTime`'s clock; `requestMs` and `Skipped` noted; the first request after a start loads the model and may show `Skipped` above 0 | Every result stale and `hands.fault` with code −3 (the plugin's log line gives the frame's age): the timestamp is on another clock |
| BENCH 6, camera pose | Phone at rest in landscape left with `hands.record` on; compare a record's `CameraToSession` with the tracked camera's local pose | Within 2 mm and 0.2 degrees (`execute_code`: `var c = Atlas.App.AtlasBootstrap.App.Boot.TrackedCamera; Debug.Log(c.localPosition + " " + c.localRotation.eulerAngles);` against `d.CameraToSession.c3.xyz` and its rotation). It holds for landscape left only | A difference: the conversion of ARKit's camera transform (`HandsNativeMath.ArKitToUnity`) or the session's frame is off |
| BENCH 7, pause | Home button for 10 s and back; the lock button for 10 s and back | `hands.fault` is not logged; results return within a second (`hands.enter` for a hand held in view) | A `hands.fault` code, or no result after the return |
| B7, LiDAR on a hand | A hand square to the camera, still, 20 s each at 25, 35, 45 and 60 cm with `hands.record` on. In the log, per distance and for every record with a hand: `Hand0.PalmCount` and `Atlas.Hands.PalmPlane.Fit(in d, in d.Hand0)` (`Valid`, `Samples`, `Distance`) | Per distance: the share of records with `PalmCount` of 12 or more, the share with `Valid` true, and the spread (standard deviation) of `Distance` of the valid ones. `Valid` needs the plane between 0.32 and 0.90 m (`HandsConstants.DepthMinMetres`, `DepthMaxMetres`), so at 25 cm it is false by definition: read `PalmCount` there. Decides the 0.32 m cut-off and how often the size fallback runs | A share far under 1 at 45 and 60 cm: the size branch runs where LiDAR was expected |
| B8, fingertip depth | In Home on a scanned room, `hands.bench` on, touch a real table at five marked points with the right index fingertip, and note the table's height with a tape. `execute_code`: `Debug.Log(Atlas.World.AtlasServices.Get<Atlas.World.IHands>().Joint(Atlas.World.HandSide.Right, Atlas.World.HandJoint.IndexTip));` | The fingertip's `y` (the floor is y = 0) within 2 to 3 cm of the table's height at each point: the assumption section 6.2 of the design is built on. Write down the signed error: the lift puts palm joints on the LiDAR plane, which on the phone is skin, 1.1 to 1.4 cm nearer than the joint centres, and applies no offset for it | Worse than 4 cm: the fallback detector of design §4.1. The headless numbers to compare with (a perfect detector, 2 px and 1 cm of noise): palm 95th percentile 4 to 5 mm, index fingertip 11 to 21 mm, up to 38 mm for curled fingers with no LiDAR under them |

**The design's gates.** After the bench, the walk-through is the design's §9.4: HG1 (hands seen: within 250 ms in, gone
within 400 ms out, `hands.enter` and `hands.exit` against slow-motion video; the drawn fingertip within 2 cm of a
virtual table corner once the camera-to-eye offset is set, `render.headOffsetX/Y/Z`; a still fingertip under 3 mm at
the 95th percentile from a log), HG2 (touch and pinch: 20 of 20 presses fire once each, at most one stray firing in two
minutes, 19 of 20 pinches; counted from `hands.touch` and `hands.pinch`), HG3 (safe and cool, with a spotter, only on a
build with R13 and R14: the approach protocol of the Master Plan §11.4.5 with both hands raised, forced tiers from the
Console giving 30 Hz at Nominal, 15 at Fair and off at Serious each within a second, frame-time p95 up by at most 1.0
ms with the hands on) and HG4 (world and copies). The pass marks are proposals until the bench has run.

**Capture.** The hand logs (`YYYYMMDD_3i_<check>.handlog` in `logs/`), the `first frame` line, the `hands.bench` and
`hands.stats` events, and a table of the numbers above in the report, one row for each check.

---

## 4. The consolidated risk register

Every item that the engineers' final reports, the contract pages (lane A section 17.1) and the verified API
notes (`docs/contracts/unity-api/`) list as "only Unity or a phone can confirm", deduplicated and grouped.
"E" = check in the Editor, "X" = in Xcode or the build, "P" = on the phone, "S" = on the Station PC.
**[TOP n]** marks the ten judged most likely to fail first; do those early.

**The ten, in the order to expect them.**

1. **[TOP 1]** The set-up script has never run (A7, K3): a non-public reflection call, serialised property
   names, renderer features added as sub-assets, and a self-check that makes a build throw when it is wrong.
2. **[TOP 2]** `.meta` files and scenes written outside Unity (K7): GUID conflicts, scenes with a missing
   script, importer defaults that Unity rewrites.
3. **[TOP 3]** `Atlas/Surface` and its four includes have never been compiled (A1, L1): the whole world is
   magenta if one line is wrong, and the SRP Batcher needs the material block exactly right. Two of the four
   includes (`AtlasWarp.hlsl`, `AtlasGates.hlsl`) are lane B's real code, not the identity stubs, and run in
   every flat space too (mode 0).
4. **[TOP 4]** The lens blit (B1): a render-graph pass that writes the back buffer; black, upside down,
   swapped eyes or wrong brightness (the sRGB flag) are all plausible.
5. **[TOP 5]** The head-locked layer (A13): cue, reticle, map and HUD text draw only through the two
   `Atlas HUD` Render Objects features on layer 7; three of the shaders carry no `LightMode` tag.
6. **[TOP 6]** XR Simulation changes the Editor-first path (C7): the head is the simulated camera and the
   default room is `none`, so the Editor may not behave as the engineers' reports describe.
7. **[TOP 7]** The onboarding card (F8): a UI Toolkit panel made in code, with Unity's default runtime theme
   loaded from a one-line `.tss` under Resources that Unity has never imported, and the legacy built-in
   font; the theme import, the text field and touch pass-through are the fragile parts.
8. **[TOP 8]** The portal stencil passes (A11, A12): features switched on at run time, a depth reset to the
   far plane, and a stencil reference per room; holes or an empty doorway if any part is off.
9. **[TOP 9]** ARKit with the AR camera component disabled in the headset view (B5): if poses or depth stop,
   Home freezes and Bump Guard is blind.
10. **[TOP 10]** The depth image's orientation and intrinsics with the phone in landscape (C3): wrong, and
    Bump Guard's live layer warns at the wrong places or not at all.

### 4.1 Render pipeline and shaders

| # | Confirm | How to check | A failure looks like | Where |
|---|---|---|---|---|
| A1 **[TOP 3]** | `Atlas/Surface` and `Hidden/Atlas/Background` compile; `Atlas/Surface` is "SRP Batcher: compatible", 1 pass, 1 variant, no keywords | Step 3c, Inspector | Magenta world; "not compatible"; extra variants | E, then X for Metal |
| A2 | All `Atlas/Surface` draws of an eye are in SRP batches; no depth or shadow pass; skybox once per eye after the opaques | Frame Debugger | A batch break with "different MaterialPropertyBlock"; a DepthOnly or ShadowCaster pass | E |
| A3 | The look: thin anti-aliased floor lines every 0.5 m, stronger every 1 m, stable and equal in both eyes; a box's top brighter than its sides and each side a different shade; geometry beyond about 14 m merges with the horizon; background darker at the horizon, lighter at the zenith; a portal-frame cell bright and unshaded; half opacity gives a checker dither; fade 0 blacks out world and sky | Home on `sample_4x5_table`; a space switch for the fade | Lines that crawl or differ between the eyes; flat boxes; a hard edge at the fog; sky not fading | E, P |
| A4 | Everything loaded with `Resources.Load` is in a player build: the nine shaders (`Atlas/Surface`, `Background`, `PortalMask`, `LensBlit`, `Reticle`, `Cue`, `FlatTwin`, `HexFlip` and `NearText`), the five fixtures (`AtlasRooms/*.json`), the fallback set (`AtlasFallback/manifest` and `<name>.glb`), the onboarding theme (`AtlasUi/AtlasRuntimeTheme`) | Device log: no line with `not in Resources`; the fixtures load from the overlay in a development build | Black headset view, magenta, `The sample room is not in this build.`, boxes that never become built-in meshes, an onboarding card with bare controls | P |
| A5 | The skybox pass fills only far-depth pixels on Metal | Look at the horizon behind walls | Sky drawn over geometry, or holes | P |
| A6 | Vertex colour reaches the shader as R = shade, G = line weight | Home: shading and line weights as in A3 | Uniform brightness, or lines everywhere | E |
| A7 **[TOP 1]** | The set-up leaves the assets as step 3b's table says, and a second run changes nothing | Step 3b | See step 3b | E |
| A8 | Assets written inside `OnPreprocessBuild` take effect in the same build | Xcode GPU capture: no intermediate colour texture per eye; the HUD and portal features present | An extra full-screen copy per eye; HUD missing in the build but present in the Editor | X, P |
| A9 | `Material.CopyPropertiesFromMaterial` keeps the outgoing copy SRP-batcher compatible during a mesh cross-fade | Frame Debugger during the table's cross-fade in `sample_4x5_table` | A batch break for the fading object | E |
| A10 | Step 8b runs once per frame from `endContextRendering`, in the Editor and on Metal | HUD refreshes, space switches advance, telemetry rows arrive one per second | Frozen HUD; a switch stuck in black | E, P |
| A11 **[TOP 8]** | The portal pass order of step 3c; no `Atlas Room NN` above the doorways in use; no portal pass in Home or Glued; no object of layers 6 to 22 in Draw Opaques | Frame Debugger in S5 | Empty doorway; a room drawn twice; a room drawn over the walker's room | E |
| A12 **[TOP 8]** | `Hidden/Atlas/PortalMask` compiles on Metal and is batcher compatible; the depth reset lands exactly on the far plane | S5 with `portal.depth` 0: the background gradient fills the doorway | Clear-colour holes in the doorway; the room behind cut by old depth | E, P |
| A13 **[TOP 5]** | The head-locked layer draws in both eyes, on top of the world, in two passes: quads (cue, reticle, menu, map) in `Atlas HUD`, text in `Atlas HUD Text`; cue and reticle over the menu panel | Frame Debugger; walk into a wall with the menu open | No reticle or cue at all; HUD hidden behind a wall; HUD drawn twice | E, P |
| A14 | The HUD's TextMesh Pro default font loads in a player build; a line is about 0.02 m high and the panel fits 0.45 m; managed allocation stays 0 with the HUD on | Development build; HUD's `gc` field | No HUD on the phone; `gc` above 0 only while the HUD shows | P |
| A15 | The engine accepts memoryless multisampled eye textures; the watermark is not visible | The `Atlas: eye textures ...` line: `memoryless`, `store-and-resolve`, `watermark visible False`; the GPU capture | A stored and reloaded MSAA surface (several ms of GPU time) | P, X |
| A16 | No managed allocation on the frame path; the kernel steps exactly once per frame | Profiler: 0 B in `AtlasFrameLoop.Update` and `LateUpdate`; `BootstrapUnityTests` asserts one step per frame | `gc` above 0 in steady state | E, P |
| A17 | The pooled materials do not leak across switches | Twenty Home-to-other-space switches: no `materials.grow` warning | `materials.grow: the material pool doubled to ...` | E |

### 4.2 Stereo and lens

| # | Confirm | How to check | A failure looks like | Where |
|---|---|---|---|---|
| B1 **[TOP 4]** | `Atlas/LensBlit` compiles and loads; the plain copy is upright with left and right correct; brightness matches the eye textures | Step 3e row 2; `render.lens` off and on | Black; upside down; eyes swapped; washed out or too dark | E, P |
| B2 | Through the lenses a straight-line grid looks straight; the picture is symmetric, filled to about 50.3 degrees to each side and 48 up and down, black beyond | Home in the headset; tune the lens parameters | Bowed lines; double image; vertical offset between the eyes | P |
| B3 | One native render pass per eye; colour MultisampleResolve; depth and stencil discarded; one blit to the screen; at most 1.0 ms of GPU time per visible portal | Xcode GPU capture, release build | See step 3g | X |
| B4 | The formats Metal returns, the sRGB flag and the dpi | Read the `Atlas: eye textures ...` line and copy it into the report | An unexpected format; dpi 0 (the lens model then uses its default screen size) | P |
| B5 **[TOP 9]** | ARKit keeps delivering poses and depth with the AR `Camera` component disabled in the headset view; 60 fps holds with the session running; 30 fps on tier 3 | Headset view on the phone: the eyes follow the head; HUD `bump` reacts to a hand in front of the LiDAR | A frozen view; no depth after the switch. The contract's fallback: keep the camera enabled, culling nothing, into a 16 x 16 texture | P |
| B6 | Each ladder rung forced from the Console is in force within 1 s: the eye-texture swap, the MSAA change, the 30 fps lock | `perf.run` = `ladder`; `thermal.pinTier` | A hitch or a black frame on the swap; a rung that never applies | E, P |
| B7 | The headset refusal: with no Bump Guard a phone shows `Bump Guard missing` and stays in the hand view | Only on the owner's request: it needs a temporary throw in `SafetyInstaller.Install`, never committed | A one-frame lens view; the headset view allowed | P |
| B8 | The screen size in the first landscape frame | The eye-texture size in the log is 1118 x 1032 on both phones | Another size: the screen was read before the orientation settled | P |

### 4.3 AR session and depth

| # | Confirm | How to check | A failure looks like | Where |
|---|---|---|---|---|
| C1 | The start-up lines `Atlas: AR backend ARKit`, `Atlas: ARKit video <W>x<H>@60`, `Atlas: ARKit depth 256x192` | Device log | `no 60 Hz format listed, the default is kept`; `ARKit video format refused: ...`; a depth size warning; no depth after 5 s | P |
| C2 | 60 fps holds with the session's frame-rate matching off; choosing the video format does not restart tracking | Metal HUD; watch the first seconds | 30 fps; a tracking reset just after launch | P |
| C3 **[TOP 10]** | Depth image orientation and field of view against the scaled intrinsics, phone in landscape in the headset | Hold a board at a known place: left, right, high, low; watch the cue's side and the HUD's `bump` | The cue on the wrong side; no warning for an obstacle placed after the scan | P |
| C4 | With a room active the anchor stays at the world origin while walking; planes and meshes move with the origin | Home: walls stay on the real walls over a lap | The room drifts or turns with the walker | P |
| C5 | Adding the bounding-box manager at boot starts and stops RoomPlan once, harmlessly | Device log at launch; no stall | A camera interruption at boot | P |
| C6 | The code-built mesh prefab yields meshes | A sweep in a LiDAR room: the outline grows | No floor is ever found; the flow asks for corners | P |
| C7 **[TOP 6]** | Editor without XR Simulation: the keyboard walker moves the eyes. With it: source `arcamera`, ray-cast depth | Step 3e, the branch table | The head does not move; the scan flow cannot finish in the simulation | E |
| C8 | The pose-log recorder's thread and file under IL2CPP; the text matches the .NET 8 format | `pose.record` on for a minute; open the `walk_*.poselog` | No file; a file the replay refuses | P |
| C9 | `Camera Offset` stays at height 0 on ARKit | Home: the floor is on the real floor | The whole room floats or sinks | P |
| C10 | With depth switched off (`perf.run` = `soakNoDepth`) the LiDAR really rests and depth returns afterwards | The run's notes; `bump` reacts again after the run | Depth stays off after the run: a safety fault, report as blocker | P |
| C11 | World map: tracking leaves Normal after the map is applied; the anchor returns under its id; saving answers within 10 s; relocalised within 10 s to 5 cm | H2's pre-scan row; the lines `Atlas: world map saved for room ...`, `Atlas: world map applied for room <id>: relocalising` and `Atlas: relocalised in room ...` | Every attempt runs into the 15 s time-out (the gate `SawSearch` in `Scan/Store/Relocaliser.cs`); `ARKit gave no world map` | P |
| C12 | A relocalisation that fails takes the world map out of the session again and restarts tracking, so the new scan that follows can start | With `scan.teamPrescan` on and a room saved, relaunch in another room and choose `Use the saved room`. Expect `Atlas: room <id> not relocalised: ...`, then `Atlas: world map dropped and tracking restarted: the room was not recognised`, then the sweep with tracking Normal within a few seconds | The sweep never finds a floor after a failed attempt: the session is still searching for the saved room (`ARSession.Reset` and `ClearWorldMap` in `Scan/Store/WorldMapStore.cs`, `Scan/ARKit/WorldMap/ArKitWorldMapPlatform.cs`) | P |

### 4.4 Scan and RoomModel

| # | Confirm | How to check | A failure looks like | Where |
|---|---|---|---|---|
| D1 | AR meshes are CPU-readable | A sweep finds the floor from the mesh (route 2 or 3) | Always the manual outline | P |
| D2 | One face class per triangle; ARKit's classes map directly onto the scan's | Walls, floor and furniture separate in the proposal | Furniture in the floor; the free rectangle through a table | P |
| D3 | RoomPlan boxes arrive beside the running session and depth returns within 2 s | `Objects: <n>` above 0 in a furnished room | `Atlas: depth did not return with RoomPlan capture running: boxes off for this scan` | P |
| D4 | A box's pose is its centre with Y up; boxes sit on the furniture to 10 cm | Home: placeholder boxes against the real furniture | Boxes half in the floor or shifted | P |
| D5 | The room's anchor registers | No warning at Confirm | `Atlas: the room anchor could not be created: the room keeps its saved pose` | P |
| D6 | Meshing and plane detection are off after the scan | The two lines of H2 | Heat; the lines missing | P |
| D7 | Corner taps land on the floor plane | `Outline by hand`: tap four corners | Corners in the air or behind the wall | P, E (branch S) |
| D8 | The solver's cost on a full room mesh, on a worker thread | Frame time during the sweep (it runs twice a second) | Stutter while sweeping | P |
| D9 | Free rectangle against a tape measure, 10 cm | H2 | Larger error | P |
| D10 | Fixtures, the service slots and SHA-256 under IL2CPP with stripping; JSON text identical on Mono | `Atlas.World.Tests` green in Unity; a fixture loads on the phone | A golden-text test red in Unity only | E, P |
| D11 | Object photos: the camera image upright against the pose; JPEG rows not flipped; colours right; one key-frame copy under 2 ms; memory released after the sweep | With `FORGE_DEBUG_KEEP=1` on the Forge, open the kept crops | Crops rotated, upside down (the flag `RowsBottomUp` in `UnityJpegEncoder`), blue faces (Cb and Cr swapped), crops that miss the object | P, S |
| D12 | A sweep logs `Atlas: object photos: choosing from <N> key frames ...` with N above 0 | Device log after `Looks right` | N is 0 on a phone: every object takes the built-in mesh | P |

### 4.5 Bump Guard and cue

| # | Confirm | How to check | A failure looks like | Where |
|---|---|---|---|---|
| E1 | Burst compiles the depth job; a tick costs at most 0.3 ms with 256 x 192 depth at 60 Hz and allocates nothing | Burst Inspector; Profiler on the phone | A managed fallback; a tick over budget | E, P |
| E2 | The LiDAR's minimum range against the 0.3 m depth floor; depth noise against the three-image clear rule | H3 with an obstacle placed after the scan, approached slowly and stopped at | No WARN from live depth when standing nearer than 0.3 m to an unscanned obstacle. Measure and report; do not change constants | P |
| E3 | Haptics: the plugin builds; pulses felt at 2 and 6 per second and told apart; the first pulse is not late; pulses still come after the app was in the background | H3 | Silence after a resume; a late first pulse | X, P |
| E4 | `Atlas/Cue` compiles, draws in both eyes on top of the world, colours right (amber WARN, red STRONG, light blue rescan, white stand-still) | Step 3e row 5 | No indicator while the tick is heard | E, P |
| E5 | The tick comes from the hazard's side; the tone is audible at STRONG | Headphones or the phone's speaker, wall on the left then on the right | Sound from the wrong side or none | E, P |
| E6 | With no room active (before the first scan, during a rescan) Bump Guard works from depth alone, with the 0.10 to 2.0 m band taken in the session frame, not from the floor | The cue is armed only in the headset view, which needs a room, so this path cannot be seen from outside in this build. Note it; nothing to run | | P |
| E7 | A replayed or synthetic head with depth off draws no cue that changes the load | `perf.run` = `tour`: no cue along the built-in lap on `budget_room`, no pose-jump rejection | Cues during the tour | E, P |
| E8 | STOP holds 3 s and Bump Guard's own level cannot clear it early | Step 3f | The cue flashes for one frame | E, P |
| E9 | The cue starts within one frame of the call, 20 of 20; identical with the Station off | H3 | A late indicator, tone or haptic | P |
| E10 | The rescan icon: live depth disagreeing with the map by more than 0.15 m for 2 s | Move a wall-sized board in after the scan | The icon never shows, or shows in a correct room | P |

### 4.6 Input

| # | Confirm | How to check | A failure looks like | Where |
|---|---|---|---|---|
| F1 | `Atlas/Reticle` compiles, reaches the build, draws through the `Atlas HUD` feature | Step 3e | No dot in the headset view | E, P |
| F2 | The reticle: a dot, and a ring that fills clockwise from 12 o'clock during a dwell; hidden in the hand | Dwell on the menu chip | No ring; a ring in the hand view | E, P |
| F3 | On the phone: the lever is a touch that begins on the screen; gamepad B tap is Back; start with select held opens the menu; no errors with no gamepad | Headset on a table, press the lever; pair a gamepad | A lever press does nothing; errors every frame without a pad | P |
| F11 | The four space actions from a gamepad in the headset: B held 1 s returns Home while a tap of B is still Back; d-pad up and down cycle only the built spaces and wrap; start with select up opens the menu on `Spaces`. All four are inert during a fade, with `demoLock` on, and in the hand | Step 3e row 21 in the Editor with the keys; a paired pad on the phone | A hold of B that also fires Back; d-pad up that switches space while the menu is open; an action that works under the Console's lock | E, P |
| F12 | The overlay's dead zone for the card matches the grey box IMGUI draws, at every Game view aspect, and on a development build in the hand | Step 3e, "The overlay's box and the card" | A press on an overlay button that also skips the countdown or places a corner; a strip beside the box where the lever is dead | E, P |
| F13 | The menu's `Rescan the room` reaches onboarding: hand view, the `Your room` card, no old room drawn over the camera picture | Step 3e row 22; on the phone from the menu in the headset, then take the phone out | The hand view with no card | E, P |
| F4 | In the Editor the keyboard is reported as its own source | `AtlasActionMapTests` green | Keys counted as gamepad | E |
| F5 | The menu by gaze: the chip down-left (about 34 degrees) can be caught and is not opened by accident while walking; eight text lines sit on the row targets; 6 draw calls open, 2 closed; under 0.3 ms open | Use the menu with the reticle only | The chip runs away from the gaze; text beside its rows | E, P |
| F6 | The overlay's buttons take clicks with the Input System active; the overlay is hidden on a phone in the headset view | Step 3e | Dead overlay buttons | E, P |
| F7 | Gamepad rumble on iOS does nothing (expected) | A paired pad during a cue | Not a fault | P |
| F8 **[TOP 7]** | The onboarding card: labels and buttons render with the built-in font, text wraps, the card fits a 1920 x 1080 landscape layout and is readable in daylight; touches reach the buttons; blank card areas pass touches through; the address field opens the iOS keyboard; the lever's touch point does not land on `Scan again` | Step 3e in the Editor; H2 on the phone | Unstyled or invisible controls; a text field that cannot be typed in; a lever tap that restarts the scan | E, P |
| F9 | The floor lines of the sweep and confirm screens sit on the real floor; a corner can be picked and dragged | H2 | Lines floating off the floor; no corner picked | P |
| F10 | Camera permission: the granted, denied and retry paths; `Open Settings` opens the app's page | Deny once on a clean install | The flow stuck on the `Camera` screen | P |

### 4.7 Telemetry and link

| # | Confirm | How to check | A failure looks like | Where |
|---|---|---|---|---|
| G1 | LiteNetLib under IL2CPP with stripping Low: connect, the Local Network prompt, background and resume | H1, H4 | Never connects on the phone though the Editor does; the event log says the UDP transport is missing | P |
| G2 | The link does not flap between connected and degraded on real Wi-Fi with 500 ms keep-alives | Hub status line over five minutes | `degraded` every few seconds | P |
| G3 | Clock offset jitter; no allocation from the link per frame | Hub log; Profiler | Jitter above 2 ms (known: about 3.8 ms, section 5) | P |
| G4 | `FrameTimingManager` gives GPU time on Metal; the five profiler counters read non-zero in a release player | The HUD's `gpu`, `draws`, `sp`, `tris`, `gc` fields; a tour row is `pass` or `fail`, not `incomplete` | Zeros; `incomplete` rows on a phone ("GC Allocated In Frame" is the unconfirmed counter name) | P |
| G5 | The deep link on a warm open; files appear under `Documents/logs` | Scan the QR code with the app already running; Finder file sharing | The address is not taken; no files | P |
| G6 | No Local Network prompt at first launch until the `Station` screen; a paired phone connects at boot | Clean install | A prompt over the Welcome screen | P |
| G7 | The Hub's folder carries the phone's session id; the two perf CSV files are the same bytes | `cmp` after a run | Different names or bytes | E, P |
| G8 | The Console unfolds a space's sliders on entry; a locked Hub shows `demoLock` on in the menu; a lock set in the phone's menu reaches the Hub and the Console | Step 3f, rows "Lock" and "Lock set on the phone" | Folded groups stay folded; the Console still offers sliders the phone ignores | E |
| G9 | The `/pair` QR code decodes in the iPhone Camera | H1 | The Camera app does not offer to open ATLAS | P |
| G10 | The Console in a real browser, laptop and tablet, both orientations: layout, the STOP strip, slider feel at 30 sends a second, charts and map at the device's pixel ratio | Step 3f | Blurry canvases; STOP off screen | E |
| G11 | The performance runs: `perf.run` in the menu and the Console; each of the four runs completes on a phone and writes `tour_`, `perf_` and `events_` files; launch arguments arrive through the Xcode scheme; `-atlasPerfQuit` quits; a run started from the menu while onboarding owns the flow leaves the headset state consistent; after a tour or a soak the walker's own room and its anchor are in force again | `docs/evidence/README.md`; step 3e row 23; on the phone after a scan, walls back on the real walls | A run that never ends; the phone left in the hand view; `budget_room` still drawn after the run, which with a real room is a safety fault: report as blocker | E, P |
| G12 | The mini-map costs at most 0.3 ms to rebuild on the phone, in Glued 7 x 7 and in Home with a full trail | `hud.map` on; the probe | Frame-time steps ten times a second | P |
| G13 | Baked defaults: `Resources/AtlasParams/defaults.json` needs a `.meta` from the repository tools after a Bake | Only if the owner bakes | A build that ignores the baked values | E |
| G14 | Leaving cleanly: quitting the app, or `link.enabled` off, shows at the Hub at once with its reason | Hub terminal: `phone left: <reason> (peer <n>)` | The Hub waits for its time-out | E, P |

### 4.8 Assets and Forge

| # | Confirm | How to check | A failure looks like | Where |
|---|---|---|---|---|
| H1 | TripoSG on the RTX 5070: import, load to CUDA in float16, the call's return shape, the up axis, at most 60 s per object | `station/forge/README.md`, "What is unverified" | Any step of that list | S |
| H2 | Matting on the Station within 10 s; `/health` shows `"matting": "rembg"` | `curl <forge>/health` | Another value | S |
| H3 | A Forge GLB in the look: loads with no material, is not mirrored (use an asymmetric object), palette cell and shade read correctly, no grid lines on furniture | H2's Forge row | A mirrored chair; furniture in the wrong colour or with floor lines | P |
| H4 | The Hub accepts the Forge's job posts | The rehearsal script | Its `FAIL` lines | Mac |
| H5 | The fallback set resolves from Resources in the Editor and in a build | `sample_4x5_table`: the table mesh after 2 s | The box stays a box | E, P |
| H6 | Plain http to the Forge on iOS, the multipart upload of real crops included | H2 with the Forge running | `assets: the Forge cannot be asked: ...` in the event log | P |
| H7 | Worst frame under 20 ms during a swap; with the Forge stopped every box holds a built-in mesh within 10 s, and each is replaced after a restart | H2 | A hitch per mesh; boxes that stay | P |
| H8 | The file cache under `persistentDataPath/assets` survives a restart; built-in meshes are destroyed after the cross-fade | Relaunch in the same room | Every mesh generated again | P |
| H9 | A mesh swap: a 0.3 s dither cross-fade, the pool's rented count back to its earlier value, at most one swap begun per frame | Step 3e row 4 | A pop; a growing pool | E |

### 4.9 Spaces

| # | Confirm | How to check | A failure looks like | Where |
|---|---|---|---|---|
| I1 | Home: marks 4 mm above the floor without z-fighting at 3 to 5 m; `home.marks` hides and shows them; about 30 draw calls and 124k to 125k triangles per frame on `budget_room`; walls sink to kerbs over 0.5 s with no pop when another space asks; nothing left behind on exit | Step 3e rows 2 to 4, 17 | Flicker at the marks; counts far off | E, P |
| I2 | Glued: mirrored Klein rows render and are not inside out (they use a negative X scale and rely on Unity flipping the winding); no seam at a cell edge in either eye; the fog hides the lattice's end at 3, 5 and 7 copies; grid lines line up across mirrored rows; no z-fighting between cell floors and the hidden room floor; `Configure` plus `Enter` (99 objects) within 50 ms on the phone; a forced tier changes the copies within a frame | Step 3e rows 10, 11 | Black or missing rows in `klein`; a visible lattice edge; a hitch on entry | E, P |
| I3 | Impossible Rooms: no seam or pop when the head crosses the booth door or a corridor seam, 10 of 10; no hole when one eye is through; no sliver past the jamb; the hall visible through the door before entry; HUD and cue on top; the real room back after leaving. The frame logic (which room draws, when the walker's room swaps) now lives in the engine-free `Rendering/Portals/PortalFrameCore.cs` and is tested headless; what is left for Unity is the stencil drawing itself | Step 3e row 12 | A flash on crossing; one eye black for a frame | E, P |
| I8 | Impossible Rooms in standing mode: the booth's doorway is in front of a walker at the middle of the floor, never under the head | Step 3e row 15 in `tiny_standing`; on the phone on a 1.4 x 1.4 m floor | The view flips between the room and the hall with every sway of the head | E, P |
| I4 | Slicer: Burst compiles both jobs with the profiler markers inside; `Atlas.Slicer4D` p95 at most 0.5 ms for the tesseract and 1.0 ms for the tube on the phone; switching to the tube or the ball does not hitch; the slice is drawn with culling off; the shadow dithered; the lane 12 mm above the floor without z-fighting; handles can be gazed at | Step 3e row 13; Profiler | Missing faces on the slice; a long frame on `tube` | E, P |
| I5 | The additive scene load path, then the factory fall-back | Each space loads from the overlay; the Hierarchy shows the scene `Space_<name>` loaded | `space.loadFailed` in the event log; Home in place of the asked space | E, P |
| I6 | The space registrations survive stripping | The boot line says `7 spaces registered` on the phone | Fewer: the menu's Spaces page is short | P |
| I7 | The kernel's seed gives the same golden values under IL2CPP | `TileId_Seed` in a player build (the golden file of 64 and `TileIdContractTests`'s rows); note only | | P |

### 4.10 Spectator

| # | Confirm | How to check | A failure looks like | Where |
|---|---|---|---|---|
| J1 | `Atlas/FlatTwin` compiles; the map draws in the `Atlas HUD` pass with its labels after it; colours right | `hud.map` on | No map; labels behind the quads | E, P |
| J2 | The two viewports draw without overwriting each other under URP; the text on layer 23 is visible; portal rooms render through the mirror camera | Step 3f, part 6 | One viewport black; no panel text | E |
| J5 | `PC_Spectator` boots the Spectator and not the walker's app; it dials only the Hub it was given | Step 3f, part 6 | Nothing boots; both apps boot; a connection to a stored address | E |
| J6 | The Spectator's furniture: built-in meshes where the phone placed them, Forge meshes by hash, the same cross-fade, nothing uploaded | A phone with a furnished scan and the Forge running, Spectator watching | Boxes that stay boxes; a mirrored or misplaced mesh | E, P, S |
| J7 | `AtlasBuild.Spectator` produces a Windows player that starts in `PC_Spectator` and can load the space scenes | Step 3f, the Windows build; then on the Station PC | A build error; a player that shows the walker's onboarding or an empty window | Mac, S |
| J3 | Hub plus phone (or ReplayPhone) plus Spectator: the room arrives, the world check reads ok, space switches follow | Step 3f, part 6 | "world mismatch"; a Spectator that stays in Home | E |
| J4 | The Spectator shows what the phone shows in Glued (layout and copies) and in the Slicer (object, cut, turns) | Change `glued.mode` and `slicer.w` on the phone side | The Spectator keeps its own values | E |
| J8 | The Spectator follows the curved spaces: S2's tiling and curvature ramp, S3's tile, S4's face and radius; the world hash and the generated world's hash agree; the disc draws within 0.3 ms on the phone's mini-map (`hud.map`) | ReplayPhone or an Editor phone in S2, S3 and S4 (step 3e rows 24 to 27) | A Spectator that stays flat in S2; "world mismatch"; a disc that jumps at every tile crossing beyond re-centring | E, P |

### 4.11 Build and native plugins

| # | Confirm | How to check | A failure looks like | Where |
|---|---|---|---|---|
| K1 | The Xcode build links with no CoreHaptics symbol and no added framework | Step 3g | Undefined symbols | X |
| K2 | The thermal plugin returns a value | H1 | `therm` stuck where the phone is clearly hot; a link error | X, P |
| K3 **[TOP 1]** | The build log holds `Atlas: render set-up applied for the build.` | Step 3g | `BuildFailedException` | X |
| K4 | Managed stripping Low keeps what is created by name or by attribute: the UDP transport, the space registrations, the menu, onboarding and perf-run hooks, `[AtlasParam]` fields | The boot line; the link connects; the card shows; `perf.run` is in the menu | A phone build that boots with no onboarding card (the rehearsal flow then runs) or no menu | P |
| K5 | `tools/build_ios.sh` end to end with a Personal Team | Step 3g | See step 3g | Mac |
| K6 | Burst compiles for iOS with no managed fallback: `SliceJob`, `BumpDepthJob`, the two Slicer jobs | The Unity build log: no `Burst error` | A build failure, or a slow job on the phone | X |
| K7 **[TOP 2]** | The generated `.meta` files and scenes import cleanly; the eight scenes are in the build list in the order boot, Home, Glued, CurvedWalk, AlignedCells, SmallPlanet, Portal, Slicer; `PC_Spectator` imports and is not in that list | Step 3a; File > Build Profiles | GUID conflicts; a scene with a missing script; `PC_Spectator` in the iOS list | E |
| K8 | The Station PC: the firewall script, the timer resolution path, relay delay under 2 ms | On Windows only | | S |

### 4.12 Curved spaces and the warp (lane B)

| # | Confirm | How to check | A failure looks like | Where |
|---|---|---|---|---|
| L1 | `Atlas/Surface` compiles with lane B's real `AtlasWarp.hlsl` and `AtlasGates.hlsl`, stays SRP-Batcher compatible with one pass and no keyword, and mode 0 still draws every flat space | Step 3c, the table of points; the shader inspector; `WarpGpuTests.Warp_MatchesKernel_Gpu` | Magenta world; "SRP Batcher: not compatible"; GPU positions off the CPU reference by more than 1 mm | E |
| L2 | Every `[Category("Unity")]` test of lane B passes (step 3d): `TileRendererTests`, `WarpGpuTests`, `Warp_HugeLocalBounds`, `ShaderIDs_MatchContract`, the lint tests that build meshes, the Aligned Cells room tests, the Small Planet and Curved Walk view tests, `EndoluminalGpuTests`, `NeedleSkyGpuTests`, `SkyDataTests`, `FieldFitBlitTests`, `LensGuardTests` | Step 3d | A test red in the engine that is green headless | E |
| L3 | The three generated curved scenes import and load; `CurvedWalkSpace`, `AlignedCellsSpace`, `SmallPlanetSpace` are created by their scenes or, failing that, by the registered factories | Step 3a; step 3e rows 24 to 27 | Missing script on a scene; `space.loadFailed` | E |
| L4 | The curvature ramp: K moves for 4 s only while the head stands (speed at most 0.1 m/s, tracking Normal), and the room does not jump when it ends | Row 24 | The ramp runs while walking; a visible snap of the room | E, P |
| L5 | The warp is centred on the filtered head (`FrameContext.HeadPosition`): after a rejected pose jump the picture does not jump either | Replay `walk_jump_and_loss.poselog` in S2 | The world lurches with the raw pose | E |
| L6 | The room content under the warp: floor hidden, kerbs 0.9 m, each object rigid at its own anchor, 1000 m bounds restored on `Exit`; the outgoing mesh of a 0.3 s cross-fade is drawn unwarped (known) | Row 24 in `sample_4x5_table` (the table swaps its mesh about 2 s after Home) | Furniture sliding; the room gone after leaving S2 | E |
| L7 | A built-in mesh whose fitting scale is on its Transform is drawn unscaled under the warp (known, `lane-b-kernel.md` §9.4) | The built-in table in `sample_4x5_table` in S2 | A table of the wrong size | E |
| L8 | Material budget: `TileRenderer` rents 128 pooled materials; no `materials.grow` line during a tour with S2 | `-atlasPerf tour` log | `materials.grow: the material pool doubled to ...` | E |
| L9 | Tile edge lines: the tile floors show minor lines at the tile edges (strength 1), a look the placeholder shader did not have | Row 24 | Lines too heavy or shimmering in one eye | E, P |
| L10 | FieldFit's final blit is not upside down (`FieldFitBlit.shader` ignores `_BlitScaleBias`); `StereoRig.OverrideEyeTargets` projections | `Blade_FieldFit.unity` over Home (it is not in the build list) | The composed image flipped | E, P |
| L11 | The five-corner lap closes under 0.25 m with a spotter (gate G2); the world turns about 90° per lap | H5 | `closure_m` over 0.25 m | P |
| L12 | S3's table edge within 0.05 m after ten crossings; 60 fps on `budget_room` | H5 | A misplaced virtual edge | P |

### 4.13 Tracked hands

| # | Confirm | How to check | A failure looks like | Where |
|---|---|---|---|---|
| T1 | The six `_atlas_hands_` symbols link, and the registration of `Atlas.Hands.ARKit` survives stripping | Step 3i, BENCH 8 | Undefined symbols at link; `"source":"none"` in `hands.bench` for good | X, P |
| T2 | The plugin gets camera frames and depth with the AR camera component disabled in headset mode, and its record is `HandsDetection`'s (2,712 bytes) | BENCH 2 and 5; the `AtlasHands: first frame` line | `hands.fault` with −3 or −5; no `first frame` line | P |
| T3 | Vision's orientation `up` fits the landscape-left picture, and the sides come out right | BENCH 1 | Joints in the wrong quarter of the picture; left and right swapped | P |
| T4 | LiDAR under a hand: its shape against the picture's, a confidence map, palm samples | BENCH 2, BENCH 3, B7 | `PalmCount` under 12 for long: the palm plane is starved and the hands run on size alone | P |
| T5 | Result age 20 to 200 ms, the request time at 30 Hz, the cost to the frame | BENCH 5, BENCH 4 | `hands.rate` events with `why` `governor`; results older than 0.2 s are dropped | P |
| T6 | A fingertip's depth against a real table | B8 | Worse than 4 cm: the design's fallback is a learned 3D detector behind `IHandsSource` | P |
| T7 | `Atlas/NearText` is in the player and near text is depth-tested; panels come to the near anchor and go back | Step 3i, parts 1 and 2; HG1 on the phone | Text painted over a finger; a panel left near when the hands are off | E, P |
| T8 | The hands' own cost: `mainMs` at most 0.6 ms, frame-time p95 up by at most 1.0 ms, 30 Hz at Nominal, 15 at Fair, off at Serious | `hands.bench`; forced tiers from the Console (H4); the perf probe | The governor halves the rate; a hot phone | P |
| T9 | Bump Guard with the hands raised: no warning caused by a hand, no missed warning behind one | HG3 with a spotter, and only on a build that has R13 and R14 (lane C §8.8) | A cue from a hand held up; a hand on the walking line hiding an obstacle (accepted, decision H18) | P |
| T10 | The camera-to-eye offset: the drawn fingertip within 2 cm of a virtual table corner when the real one touches the real corner, at three points | HG1 | A drawn hand that is centimetres off the felt one | P |

---

## 5. Known gaps and open items: do not report these as bugs

Report them only if they behave differently from what is written here.

**Not built, by decision.**

| Gap | What you will see |
|---|---|
| The airway route has 6 forks, not 4 (PH-39) | `StreamingAssets/cases/illustrative/airway/tree.json` is a placeholder; `TwoCompassesTests.P2B08_FourForkRoute_P2D07` is `[Ignore]`d. A decision for the lead, not a bug |
| The FMS score does not reach the phone | The Lung blade's automatic Mode 2 rule reads an `fms` event on the phone's log; the Hub's `fms` is never sent to it (lane D §2.6). No channel exists |
| Scan route 4, the RoomPlan wall bridge (Extended) | Written and tested headless, switched off: `WallBridge.Enabled` is false and nothing sets it, so `scan.route` 4 behaves as route 3. It has never run on a device and the Swift file has never been built in Unity's Xcode project |
| Lung and FieldFit scenes | `Lung_Airway`, `Lung_NeedleSkyTest`, `Lung_EndoluminalTest`, `Blade_FieldFit`, `Blade_FieldFit_Soak` and `FieldFit_BlindSpotTest` are not in the build list and were written before the merge. Their `[Category("Unity")]` and GPU tests are in step 3d |
| Hosted fallback of the Forge | The interface exists; no vendor is wired. `forge.backend` = `hosted` generates nothing |
| A second phone | Refused by the Hub with `another phone is already connected`; it retries with back-off |
| SimNet loss and latency simulation | The Hub has no `--sim-loss` or `--sim-latency`. The loss rows of L9 and L12 cannot be run against the Hub yet |
| The Console's source selector, lattice, cube-net and room-graph drawings | The Console's map is a floor plan for every space. The Unity-side map (`hud.map`, the Spectator's panel) does have a cube-net drawing for S4, which only S4 reaches, and only in the Editor and on the phone: the Console's own map shows the tile and the dial, not the disc or the net |
| The Spectator's keyboard shortcuts and latency read-out | Not there. Its furniture is: see step 3f, part 6 |
| The tracked hands: near returns seen twice (R13) and the forearm's own depth gate (R14) | Being built, not in this tree (lane C §8.8). Until they land a hand's own cells are forgotten only by the young-cell rule and a mask region has one gate for hand and forearm. Do not walk with the hands raised |
| The tracked hands: a masked hand on the walking line | Hides what is behind it from LiDAR. Accepted (decision H18); no view-blocked rule is built |
| The tracked hands: a replay source in the app | `ReplayHandsSource` is built by tests and scripts only; no parameter or start option chooses it, as `pose.source` does for pose logs. In the Editor the hands are the mouse-and-keys stand-in (`hands.editor`) |
| The tracked hands: the design's play-mode check | `BootstrapUnityTests` finding `IHands` and the pool's rented count returning to its start value after shutdown (hands design §9.2) is not written |

**Unverified or provisional, known.**

| Item | State |
|---|---|
| TripoSG | Never run; needs the RTX 5070. Use `FORGE_GENERATOR=procedural` on the Mac |
| Headset lens numbers | Cardboard-class placeholders: `render.lensK1` 0.34, `render.lensK2` 0.55, `render.lensSeparation` 0.064, `render.screenToLens` 0.039, field limits 50.28 and 48 degrees. To be tuned in the headset |
| Camera-to-eye offset | `render.headOffsetX/Y/Z` are 0 until measured. Walls can be off by the distance between the phone's camera and the eyes |
| Clock sync | About 3.8 ms p95 with one poll per frame; the plan's mark is 2 ms |
| Reconnect allocation | While no Station is reachable, reconnect attempts allocate about 3.9 kB inside LiteNetLib roughly every 4 s. `gc` in the HUD shows it. With a Station connected, or `link.enabled` off, it must be 0 |
| Performance figures | The per-space budgets of section 8.5 are planning figures until measured at G4. In the Editor a performance run ends `incomplete` by design |
| Soak | Alternates S1 Glued 5 x 5 and S2 Curved Walk at q = 5 (the head stands 5 s after entering S2); in a build without one of them Home takes its place and the file says so |
| Gaze targets for the four space actions | Built, never seen: `SpaceSigns` (`App/Commands/SpaceSigns.cs`, `SpaceSignsView.cs`) registers a home beacon, a previous sign, a next sign and an exhibit sign as gaze targets with the actions `ReturnHome`, `PrevSpace`, `NextSpace` and `SpaceMenu`. They are drawn below the view axis only while the head looks down past 10 degrees (and always while one is dwelt on), dim when the action would be refused (hand view, a switch under way, `demoLock`, no neighbour space). S2 has gaze twins of its own for the dial and the mode switch (`CurvedWalkTwins`). Check them in step 3e row 21 |
| `AtlasHands.swift` | Type-checks against the iOS SDK. Run on a Mac against real Vision with a stand-in for ARKit (a hand drawn from the emoji font, synthetic depth) and clean under the thread sanitizer. Never in an Xcode export, never on a phone |
| The look of the hands | Judged from a software render of the skinned vertices only. The two-sided shade leaves little contrast, so a hand reads by its outline; one-sided is a one-line change in `HandsSkin.ShadeTwoSided` |
| `hands.stats` when the app stops | Logged: `AtlasApp.Shutdown` stops the hands before the telemetry, so the last `hands.stats` of a session reaches an event log that still writes. Not yet seen on a phone |
| The numbers behind the lift, the filter and touch | Starting values, tested against a perfect detector with 2 px and 1 cm of noise. The bench of step 3i replaces them |
| Overlay clicks as the lever in the headset view | `AtlasApp` sets `AtlasInput.PointerBlocked` to the overlay's own rectangle, so a click on an overlay button no longer fires the gaze target under the reticle (it did, except on the onboarding card). Never seen in the Editor: check that a click on `walker` or a space button does not also fire a target or `Confirm` |

**Behaviour that is intended.**

- A scanned room is written to the phone only with `scan.teamPrescan` on. Otherwise `Use the saved room` is
  never offered and the scan is gone when the app closes.
- The Editor runs as role `phone` on the link.
- The link starts at boot only on a phone that was paired before; otherwise at the `Station` screen.
- `budget_room`'s twelve objects are stand-ins and are never replaced by Forge or built-in meshes.
- In XR Simulation no object photo is taken; objects take built-in meshes.
- Forge GLBs are read by the project's own reader, not by glTFast.
- Home's floor runs wall to wall (the outline), not only over the walkable polygon.
- Wall cells have diagonals of 0.354 m; the 0.25 m rule is applied along the floor.
- The menu's text is legacy `TextMesh` with the built-in font; the HUD is TextMesh Pro; the onboarding card
  is UI Toolkit. Three text systems, on purpose for now.
- `demoLock` stops the four space actions of the gamepad and the Station's sliders. It does not stop the
  menu's own rows, the overlay, or the operator's commands.
- With one space in the build, next and previous space do nothing.
- The menu is not rigidly head-locked: it keeps a level heading and re-centres past 75 degrees.
- The link reports `degraded` after 500 ms of silence.
- A parameter changed while offline is logged on the phone and not queued for the Station; the next
  handshake carries the values.
- Events are named `param`, `cmd` and `link` on the wire; the menu logs its own `param.set`.
- The quality command takes -1 to 4 (4 is the high tier for a cool phone).
- The development overlay allocates every frame while shown (immediate-mode GUI).
- Gamepad rumble does nothing on iOS.
- After a Forge restart, jobs that were waiting are marked failed and sent again by the phone.

**Fixed since the first version of this brief; they are now checks, not known faults.**

- `H`, `Page Up`, `Page Down` and `Tab` (gamepad: B held 1 s, d-pad up and down, start) are read by
  `App/Commands/SpaceActions.cs`: step 3e row 21, risk F11.
- The menu row `Rescan the room` reaches onboarding: step 3e row 22, risk F13. With `-atlasRoom` or
  `-atlasReplay` nobody subscribes and the row falls back to the rehearsal flow's `BeginScan`, as intended.
- A click on an overlay button during the headset countdown is no longer the lever tap: step 3e, risk F12.
- A failed relocalisation no longer leaves the session searching for the saved room: risk C12.
- A performance run gives the walker's room back: step 3e row 23, risk G11.
- The Spectator has its scene, its Windows build, the walker's furniture, and dials only its own Hub: step
  3f part 6, risks J5 to J7.

**Seen while this brief was written; check and report what happens.**

- The Editor's default depends on XR Simulation (step 3e); the engineers' reports describe branch N.
- Comma and period outside the menu fire `VariantPrev` and `VariantNext`, and no space reads those two actions.
  The arrows (`DialLeft`, `DialRight`) and M (`Primary`) are read by S2, and the arrows by S4.

---

## 6. Paste this to start

The owner pastes the block below into the Claude session on the Mac, opened in the repository root.

```text
You are the Unity-side verifier for ATLAS on this Mac (Unity 6000.3.25f1, Xcode, tool permissions).
Read docs/verification/UNITY_SIDE_BRIEF.md in full, then docs/contracts/README.md.
Work on the branch phase1-integration in this folder. Never push. Commit nothing until I say so.
Begin at step 3a of the brief and go through the steps in order: 3a, 3b, 3c, 3d, 3e, 3f, then parts 1 and 2 of 3i
(the tracked hands, in the Editor).
Stop before step 3g (the iOS build) and ask me for the Apple team id and for a phone.
Write findings as you go into docs/verification/report-<today's date>.md with the brief's template,
and screenshots into docs/verification/shots/ with the brief's file names.
Fix only what section 1 of the brief allows, with the smallest change, and re-check after each fix.
Do not touch Bump Guard, the contracts or the plan; if one of them looks wrong, report it and ask me.
If the Unity Editor is open, use the MCP for Unity tools; if they are not in your tool list, tell me,
and I will either connect them (Window > MCP for Unity) or close the Editor so you can use batch mode.
First tell me which of the two you have, and the output of: git status --short && git log --oneline -3
```
