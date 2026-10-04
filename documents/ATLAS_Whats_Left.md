> **ATLAS_Whats_Left.pdf is out of date.** It describes branch `phase1-acd` before lane B was combined with it. This file is the current text; the PDF has not been regenerated.

# ATLAS: what is left to do

4 October 2026. Branch `phase1-integration`. For the team lead and the three engineers. Every number below was taken from a command run on this tree (the commands are in "Where things are") or from the code; where something could not be run it says so.


## State

**None of it has ever been opened in the Unity Editor or run on a phone. Nothing has been rendered.** Seven shaders have never been compiled (the shared `Atlas/Surface` now includes lane B's real warp and gate code), the Unity-only tests of both lanes have never run (list: `docs/verification/UNITY_SIDE_BRIEF.md` step 3d), the Editor set-up script has never run, the generated scenes have never been imported, and the native iOS plugins have never been built.

What exists: Phase 1 of all four lanes on one branch. Lanes A (platform, look, flat and 4D spaces), C (scan, room, safety) and D (link, Station, Forge) were written on `phase1-acd`; lane B (the curved-space kernel, warp, tile renderer, world generator, S2 Curved Walk, S3 Aligned Cells, S4 Small Planet, and the Lung and FieldFit blades of Phases 2 and 3) came from `laneB/integration` and was combined with it on this branch. There is one kernel (`HolonomyWalkerKernel`: flat, {4,5}, {4,6} or the sphere, by configuration), the placeholders of lane B for lanes A, C and D are gone, and S2, S3 and S4 are registered, generated as scenes and in the build list like the other four spaces. `docs/laneB/INTEGRATION_NOTES.md` has the order of the work and the eight decisions taken.

From `python3 tools/headless/verify.py all --tag docs` (last run on this tree): `rules` passes (48 assemblies, 0 problems); the three builds compile with 0 errors (21 assemblies for the phone, 21 for the Station, 48 in the Editor configuration); 1,968 tests run in 24 test assemblies, 1,967 pass, 1 is skipped (the `[Ignore]`d PH-39 test) and none fails. `dotnet test station/hub.tests` passes 42 tests; `npm test` in `station/console-tests` passes 59. The Forge's Python tests and the Station rehearsal (`tools/e2e/run_station_rehearsal.sh`) were not run in this session: the Forge environment on this machine lacks the `trimesh` package. The previous handout recorded 53 Forge tests and a passing rehearsal on `phase1-acd`.

What the headless run proves about lane B: the kernel mathematics (`Atlas.Kernel.Tests`: exp and log, holonomy, the five-side lap closing, exact tile ids and their golden seeds, tile graphs for q = 3, 5 and 6, the flat and the holonomy walker kernels), the CPU reference of the warp and the draw rule, the tile meshes' seams, the world generator's golden hashes, the Aligned Cells sight-line counts, and the real frame loop and SpaceLab driving all seven spaces (`E2E_CurvedWalk_TheFiveCornerLap…`: the recorded five-sided lap leaves tile zero and closes under 0.25 m; `E2E_CurvedSpaces_HomeToS2ToS3ToS4ToHome_…`: S3 keeps S2's tile id). It does not prove any GPU picture, any frame time, or any walk by a person.

Safety: nobody wears the headset on the strength of this build until the Bump Guard checks pass with a spotter. In a curved space the virtual walls are no longer the real walls once the curvature is on; Bump Guard, in room coordinates, is the only protection.


## Do these first

1. [ ] **Commit and push the rest of the integration.** `origin/phase1-integration` is at `d1dfcab`; this document, the merged contract page and every other change made after that commit are in the working tree, not committed (`git status`). Commit them, then `git push origin phase1-integration`. `phase1-acd` and `laneB/integration` are on `origin` and stay as they are. Needs: the lead's say-so to commit, and credentials.
2. [ ] **Start the Editor check.** Hand `docs/verification/UNITY_SIDE_BRIEF.md` to a Claude session on the Mac (paste-to-start block in its last section). Order: import and compile, set-up script, shaders (above all `Atlas/Surface` with the real warp and gates), the Unity-only tests, first look in play mode (rows 24 to 27 are the curved spaces), the Station on the Mac. Findings go to `docs/verification/report-<date>.md`. Needs: the Mac.
3. [ ] **Commit what the Editor regenerates.** First open rewrites the URP asset, the renderer features and the layer names. Commit them. In play mode, use the Welcome card button "Sample room (no scan)" to reach Home fast (XR Simulation is on, so no room loads by itself). Needs: the Mac.
4. [ ] **Give the lead decisions** (next section), at least the Apple team id and the lens numbers, before the iOS build. Needs: the lead.
5. [ ] **iOS build, then gate G1 first light:** 60 fps in an empty stereo scene on the phone, telemetry on the Station, one-command install (`tools/build_ios.sh`). Needs: the Mac, a phone, the Apple team id.
6. [ ] **Gates G5 and G6:** scan, Home and Bump Guard with a spotter; then the Station link with its four functions. Needs: a phone, a headset, a spotter, the Station.
7. [ ] **Gate G2, the curved-walk spike:** the five-corner lap in S2 with a spotter (brief step H5), after G5's Bump Guard checks. Then G3 (spaces in the Editor), G4 (spaces on the phone at budget) and G7. Needs: a phone, a headset, a spotter.
8. [ ] **Tag G0.** There is no tag in the repository and the branch is not merged to `main`. See the decisions below. Needs: the lead.
9. [ ] **Run the real generator.** Install PyTorch cu128 and TripoSG on the Station PC, pin the commit, and run one object through the Forge. Needs: the RTX 5070 PC.


## Decisions the lead must give

| Item | Why it matters | In force meanwhile |
|---|---|---|
| Headset lens numbers (distortion, lens separation, screen-to-lens distance); camera-to-eye offset | Straight lines bow; the 5 cm wall test (P1-C07) is at risk | Cardboard-class placeholders; offset is zero |
| Eye field of view | Too small wastes pixels, too large shows a black border | 50.28 deg each side, 48 deg up and down |
| Apple team id | `tools/build_ios.sh` cannot sign without it | Empty; read from `ATLAS_TEAM_ID` at build time |
| Hosted image-to-3D vendor for the Forge fallback | No fallback if the RTX 5070 PC is down | Disabled until chosen |
| Second-phone policy | Two walkers or a spare phone | The Hub refuses a second phone |
| Comfort stop value (open item 6 of the plan) | Sets when the Console alerts on the comfort score | Console has the entry; no alert threshold |
| Gamepad model | Input map and rumble (none on iOS yet) | Generic gamepad, no rumble |
| Bump Guard at the lap corners (open item 19) | Above 0.5 m/s the 0.5 m lap inset gives too little warning | Inset of 0.5 m |
| **P1-A03: one URP asset plus a code table, instead of three tier assets** | Plan text and code disagree; the asset on disk is still the pre-build one | One asset and a code table. Needs a sign-off |
| **The FMS channel to the phone** (Mode 2 rule) | The Lung blade switches to Mode 2 automatically when the comfort score (FMS) exceeds 3 of 20 (plan §10.2.3). The code reads an `fms` event on the phone's own log; `fms` is written by the Hub with scene "hub" and never sent to the phone (lane D §2.6). No channel exists | The rule never fires on a real link. A parameter written through `ParamSet`, a new operator command or a new message would do |
| **G0: the tag, and on which commit** | P1-X02 asks for the contracts merged to `main` and tagged. The contract pages now describe the combined code (`docs/contracts/lane-b-kernel.md` is merged; changes the merge made are in its §9.3) and `phase1-integration` has not been merged to `main` | No tag. The natural commit is the one that has the Editor check's fixes on it |
| **The per-lane sandbox clause of P1-X02** | The acceptance asks for a sandbox scene per lane that runs against the stubs with no other lane's code. None exists, and the stubs have been replaced by the real code | The boot scene with a fixture room and the keyboard walker serves every lane. Waive the clause, or ask for scenes |
| **G2 default mode after the spike** | If the five-corner lap fails at the loop-fit L, even after raising L one step, S3 becomes the walked default (plan §12.3) | `curved.mode` defaults to Mode 1 (S2); S3 is reached by the switch or by `-atlasSpace S3` |
| **PH-39: the 4-fork versus the 6-fork airway route** | P2-D07 asks for exactly one Two Compasses fork on a 4-fork route. With the plan's geometry and the Euclidean compass rule no 4-fork route has one (0 of 299 seeds × 16 targets), so the placeholder `tree.json` has 6 forks. Change the compass rule or the target rule (and announce it), or accept 6 forks (plan §9.9 rule 7) | The 6-fork route; the 4-fork test is `[Ignore]`d |

**Open for the lead, from the four contract pages** (merged and deduplicated; the full text is at the end of each page):

- [ ] Lane A: lens numbers; head offset; field of view; High tier and extra registry values; ladder thresholds; flat shading (no normal term); four extra shaders against rule F7; one 0.25 m rule or two.
- [ ] Lane B (`docs/contracts/lane-b-kernel.md` §9.3 and §9.4): the eight old questions are answered by the code as built. Confirm three: the κ tag inside `Iso2` (flat-tagged `Identity` and `Rot`, a flat factor neutral in a product); L = 1 m per unit at κ = 0; and `ContentSeed`'s collision across worlds (`worldSeed` and `Xu` enter one addition: changing it moves every golden value). Decide whether `Iso2.Boost`, `Iso2(double3x3, kappa)` and `Geometry2` become as strict about κ as the other members. Also open: built-in furniture drawn unscaled under the warp (its fitting scale is on a Transform the warp ignores); the material pool (128 per tile renderer of 256); whether normals under the warp are ever wanted.
- [ ] Lane C: lap-corner Bump Guard; rumble; headset lever tap; which rectangle is "largest"; Home shell in large rooms; furnished sample room size; kerb height; haptics plugin; Bump Guard armed by onboarding; depth image size; camera picture during scan; additions to the room model; furniture record for pre-scan; two world-map stores; whether to switch the RoomPlan wall bridge on after the device check (below).
- [ ] Lane D: hosted image-to-3D vendor; Station address (192.168.0.10 default); where the phone CSV lives; session-delete unit; Console access control; text and shader exceptions; no Spectator interpolation; loss simulation on the real link; comfort stop value; uploading the phone CSV.


## Not built yet

What Phase 1 still lacks, plainly. Everything else in the plan's Phase 1 table is written; whether it works is the next two sections. `python3 tools/e2e/gates.py` prints, gate by gate, which lines of the plan's gate checklists are proven by tests that ran and which wait for a phone, the headset, the Editor or a person.

| Item | What is missing | What it blocks |
|---|---|---|
| P1-C05 RoomPlan wall bridge (Extended) | Written, tested headless with synthetic data, and **switched off**: `WallBridge.Enabled` is false and nothing sets it. The Swift plugin (`AtlasRoomPlan.swift`) has never been built inside Unity's Xcode project and no real RoomPlan data was ever parsed. Whether a second `RoomCaptureSession` on Unity's ARSession leaves the furniture boxes and the depth image alone is untested (AR Foundation open point D7) | Nothing critical; walls come from mesh and planes. Turning it on needs the device check first |
| TripoSG image-to-3D | The adapter has never executed. Needs the RTX 5070, PyTorch cu128 and a pinned commit. Tests use a procedural stand-in | Real Forge output (P1-D08, P1-D09 on real meshes) |
| P1-A03 quality tiers | One URP asset plus a code table; the asset on disk is the pre-build one | Lead sign-off; thermal ladder check |
| Per-lane sandbox scenes | None exist | Strict reading of G0 acceptance |
| Clock sync (P1-D01) | Receive-thread timestamps are in: 2 ms p95 holds headless at one poll per frame (`Link_ReceiveStamp`); still to be measured on the phone over Wi-Fi | Link timing claim |
| Baked parameter defaults (P1-D03) | The Hub half, the phone's apply logic (`Atlas.Link.BakedDefaults`) and an empty `defaults.json` exist and are tested. Nothing has been baked: tuned values can only come from a Console session on the Station, and nothing has run the Editor path (`Resources.Load` of the file at boot) | Running tuned values with the Station off |
| Fallback furniture (P1-D12) | The built-in meshes are generated box composites of 22 to about 100 triangles, not sourced CC0 models; the retry back-off has no 4 s step | Look of a room when the Forge is away |
| Phase 2 and 3 stand-ins | The case package and its data are lane D's and are placeholders (`docs/laneB/placeholders.md`): `ILungBeat`, `CaseManifest`, `tree.json` (6 forks), `sky.json` and its maps, `tunnel10.png`. Lane A's Lung director does not exist. The Phase 4 mathematics documents (P4-B01 to P4-B05) are not written | Phase 2 and 3 acceptance |
| Gate G0 close-out | Branch not merged to `main`, no tag | Clean freeze of the contracts |


## Built, waiting for the Editor

Run in the Editor first; nothing here has been compiled or rendered.

| Task | What must be seen or measured |
|---|---|
| P1-X02 Contracts frozen (gate G0) | The project opens with no red line in the Console; every EditMode test is green in the Test Runner; the contracts are merged to main and tagged. No per-lane sandbox scene exists |
| P1-A06, P1-B03, P1-B10 Shared look shader with the real warp and gates | All seven shaders compile for Metal; the Frame Debugger reports `Atlas/Surface` as SRP-Batcher compatible with at most 8 variants (one pass, no keyword); a test room renders in stereo; the look tests run; `WarpGpuTests.Warp_MatchesKernel_Gpu` matches the CPU reference to 1 mm for ρ ≤ 3; the gate shader clips |
| The Unity-only tests of both lanes | 113 test rows in 16 test assemblies compile and have never run (counted from the source); the list is in the brief, step 3d |
| The generated scenes | `Space_CurvedWalk`, `Space_AlignedCells`, `Space_SmallPlanet` (and the four older ones, `PC_Spectator`) import with their script GUIDs and load additively; the eight-scene build list is right |
| P1-D05 Spectator | The Windows build made with `AtlasBuild.Spectator` runs; the mirror and the flat twin draw side by side; mirror lag at most 150 ms at p95 (there is no lag read-out yet) |
| P1-D07 Flat-twin map for S2 to S4 | The Poincaré disc (`FlatTwinDisc`) is wired into the map for S2 (once the curvature is on) and S3, and the Spectator's kernel follows the phone's (`SpectatorLoop`). Tested against the kernel and through the real frame loop (`StationReplayTests`, `E2EPhase1ScenarioTests`); not seen drawn, and not timed on a phone (the tests assert under 5 ms on the build host; the plan's mark is 0.3 ms) |
| P1-C10 Gaze twins of the four space actions, and of the dial | `SpaceSigns` (home beacon, previous, next, exhibit sign) and S2's dial and mode chips (`CurvedWalkTwins`) register gaze targets with the real input; the Editor overlay's rectangle no longer fires the lever (`AtlasInput.PointerBlocked`). Headless tests only; nothing has been looked at |
| P1-B05, P1-B06 look | The tile edge lines (a new visual; the placeholder shader drew a 0.5 m grid on tiles) and the edge of the drawn world against the background |
| Lane B's Phase 2 and 3 scenes | `Lung_Airway`, `Lung_NeedleSkyTest`, `Lung_EndoluminalTest`, `Blade_FieldFit`, `Blade_FieldFit_Soak`, `FieldFit_BlindSpotTest` were written before the merge and are not in the build list; each needs opening once. FieldFit's final-blit orientation and the GPU tests of the sky, the endoluminal window and the lens guard |


## Built, waiting for a phone

By the gate where each is first checked. The numbers are the plan's pass marks.

| Gate | Task | What must be seen or measured |
|---|---|---|
| G1 | P1-A02 First build on both phones | Empty AR scene runs on phone A and phone B with tracking Normal; the thermal plugin returns a value; iOS versions recorded |
| G1 | P1-A04 Stereo rig | 60 fps on the Metal HUD, CPU main <= 3 ms, GPU <= 2 ms; a cube at 1 m fuses for three testers; ARKit still delivers pose and depth in the headset view |
| G1 | P1-A13 One-command build | `tools/build_ios.sh` installs on a phone; a second engineer does it from the README alone |
| G1 | P1-C01 AR session | The session holds 60 Hz; the depth image is non-null at the chosen video format |
| G1 | P1-D04 Perf probe, HUD, telemetry | HUD under 0.2 ms and 0 B per frame; a 15-minute CSV with no gap; GPU time and thermal state read correctly in a release build |
| G1 | P1-D11 Pairing | A fresh install pairs by QR in <= 30 s; the link returns within 5 s of a Wi-Fi drop; the Local Network prompt appears in the hand |
| G2 | P1-B09 Curvature dial and the 3-4-5 walk (S2) | On phone A the loop closes after 3, 4 and 5 corners with closure error < 0.25 m each; curvature never changes while head speed exceeds 0.1 m/s; with the standing flag the dial still snaps q = 3 to 6 and no lap markers are shown. With a spotter |
| G2 | P1-B11 The room becomes tile zero | At κ = 0 the curved-space room matches Home to 1 cm; a ramp to loop-fit L and back restores registration to 2 cm; a replayed lap shows no flicker of the tile id |
| G4 | P1-B06 Tile renderer | Phone A at ρ = 3: tile shells cost <= 150 draw calls per frame at any head pitch; 60 fps; no allocation on a tile crossing; the edge of the drawn world cannot be told from the background |
| G4 | P1-B08 Small Planet (S4) | Three corners close to < 0.25 m; no smeared triangle in a 360° look-around |
| G4 | P1-B10 Aligned Cells (S3) | Four quarter-turns round an interior corner end in a fifth tile; tile instances alone <= 120 draw calls on the 4 × 5 m sample room |
| G4 | P1-B12 Room content in Aligned Cells | A virtual table edge within 5 cm of the real one at three touch points after ten cell crossings; S3 on `budget_room` <= 170 draw calls, <= 200k triangles, 60 fps |
| G4 | P1-A05 Lens blit | A straight grid looks straight to three testers; the pass adds <= 1.0 ms GPU; an Xcode capture shows one blit to the screen and eye passes that resolve on tile |
| G4 | P1-A07 Thermal ladder | Each tier forced from the Console takes effect within 1 s; at most one step per 20 s |
| G4 | P1-A08 SpaceLab host | Memory within +/-5 % after 20 switches among the spaces |
| G4 | P1-A09 Glued S1 | No seam in either eye across a cell edge; Klein rows mirrored correctly; <= 110 draw calls and 110k triangles at 5 x 5 |
| G4 | P1-A10 Portal engine | No seam or pop crossing a doorway in stereo; <= 1.0 ms GPU per visible portal; the stencil passes stay merged |
| G4 | P1-A12 Slicer S6 | Tesseract slice <= 0.5 ms, 18,000-tetrahedron tube <= 1.0 ms at p95, 0 B per frame |
| G4 | P1-A14 Perf runs and soak | The metrics table of plan section 11.2 filled for S0 to S6 (the tour includes S2, S3 and S4); 15-minute soak (S1 and S2 in turn) with no Critical thermal state, mean >= 45 fps, never below 30 |
| G5 | P1-C02 Manual outline | Polygon within 5 cm of a tape measure in three rooms |
| G5 | P1-C03 Automatic FloorScan | Free rectangle within 10 cm of a tape measure; scan to walkable in <= 90 s |
| G5 | P1-C04 Object boxes | Boxes overlay the mesh to <= 10 cm; every item wider than 0.4 m has a box |
| G5 | P1-C06 RoomModel and loop-fit | A manual and an automatic scan of one room agree within 10 cm; meshing and planes are off after the scan |
| G5 | P1-C07 Home S0 | A virtual wall within 5 cm of the real wall at three touch points; <= 60 draw calls on the budget room |
| G5 | P1-C08 Photo capture | Under 2 ms per frame while scanning; a crop for every box with a candidate frame; crops upright and correctly coloured |
| G5 | P1-C09 Bump Guard | Warning >= 0.6 s before contact in 20 of 20 real approaches; at most 2 false cues in a 5-minute lap; <= 0.3 ms per frame. With a spotter |
| G5 | P1-C10 Input and twins | Every action by the gamepad alone and by gaze alone, the four space actions and the dial included |
| G5 | P1-C12 Onboarding | Three first-time users reach Home in the headset with at most three prompts each |
| G5 | P1-C13 World map | A relocalised room aligns to <= 5 cm within 10 s |
| G5 | P1-C14 Cue and haptics | Indicator, sound and haptic start within one frame in 20 of 20 trials; <= 0.2 ms per frame |
| G6 | P1-D03 Registry and tuner | A slider change is visible on the phone within 100 ms at p95; baked defaults load with the Hub off |
| G6 | P1-D06 Operator Console | Each of the six commands acts within 250 ms; STOP gives indicator, tone and haptic; the layout is checked in a real browser |
| G6 | P1-D10 Asset client | Worst frame while a mesh swaps in under 20 ms; the plain-http upload works on iOS |
| G6 | P1-D12 Fallback set | With the Forge stopped, every box holds a fallback mesh within 10 s of entering Home; each is replaced after a Forge restart |
| G6 | P1-D13 On-device menu | With the link off, change each kill-switch parameter from the gamepad and find it in the phone's log |
| G6 | P1-B07 World generator | The same seed gives the same world hash under IL2CPP on the phone as in the Editor (the golden hashes are checked headless) |
| G7 | P1-A15 Room in Glued, standing mode | A virtual table edge within 5 cm of the real one at three points; a 1.4 x 1.4 m room loads S1 and S6 |

Phase 2 and 3 checks of lane B (Lung chambers, walking, the sky, FieldFit) are in `docs/laneB/TESTING.md`; they are not Phase 1 gates. Also built and proven headless, with a short run in the Editor still owed: P1-X05, P1-X06, P1-X07, P1-X08, P1-B01 to P1-B05, P1-A11, P1-C11, P1-C15, P1-D01, P1-D02, P1-D09. Partly built: P1-A03, P1-D07, P1-D08 (see "Not built yet").


## Branches

- `phase1-integration` holds everything above (on `origin` up to `d1dfcab`). It was made from `phase1-acd` and `laneB/integration` on a new branch, as planned; neither source branch was changed. The merge's order and decisions are in `docs/laneB/INTEGRATION_NOTES.md`.
- `p2-lung-cores` and `p2-lung-data` (Phase 2 Lung Mission, separate worktrees under `.worktrees/`) are not merged into this branch. They touch `station/pipelines`, `docs/case_contract.md` (a file that exists only on those branches) and `.gitignore`; on this branch `station/pipelines` is empty. Merging adds files, it does not integrate them: the cores are standalone .NET projects, not Unity assemblies, and parts depend on lane D's case package, which is a placeholder here.


## Where things are

- [ ] Editor brief: `docs/verification/UNITY_SIDE_BRIEF.md` (section 4 risk register, section 5 known gaps).
- [ ] Contracts: `docs/contracts/` (README, `lane-a-platform.md`, `lane-b-kernel.md` (the one lane B page; `kernel.md` points to it), `lane-c-room.md`, `lane-d-link.md`).
- [ ] Lane B: `docs/laneB/INTEGRATION_NOTES.md`, `STATUS.md`, `placeholders.md`, `TESTING.md`.
- [ ] Headless check: `python3 tools/headless/verify.py all --tag docs` (see `tools/headless/README.md`); the gates G0 to G7 as far as they can be proved without a device: `python3 tools/e2e/gates.py` (about ten minutes; `tools/e2e/README.md`).
- [ ] Station suites: `dotnet test station/hub.tests`; `cd station/console-tests && npm test`; `bash tools/e2e/run_station_rehearsal.sh`.
- [ ] Build for the phone: `tools/build_ios.sh` (needs `ATLAS_TEAM_ID`); queue rules in `docs/build-seat.md`.
