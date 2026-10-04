# ATLAS: what is left to do

4 October 2026. Branch phase1-acd. For the team lead and the three engineers.


## State

**None of it has ever been opened in the Unity Editor or run on a phone. Nothing has been rendered.** Seven shaders have never been compiled, 19 Unity-only tests in five classes have never run, the Editor set-up script has never run, and the native iOS plugins have never been built.

What exists: Phase 1 of lane A (platform, look, flat and 4D spaces), lane C (scan, room, safety) and lane D (link, Station, Forge). 35 assemblies compile for the phone, the Station and the Editor. 1,393 engine-free tests pass. Hub (42), Forge (53) and Console (58) tests pass, and the Station rehearsal (Hub, Forge and ReplayPhone together) passes. Lane B (the curved-space kernel and its spaces) is on the branch laneB/integration and is not yet combined with this one, so spaces S2, S3 and S4 are absent here.

Of 48 Phase 1 tasks audited: 10 built and proven without Unity; 3 built and need the Editor; 30 built with acceptance on a phone; 3 partly built; 1 not built; 1 done earlier. Safety: nobody wears the headset on the strength of this build until the Bump Guard checks pass with a spotter.


## Do these first

1. [ ] **Push the branch.** origin/phase1-acd is behind the local branch. Run `git push origin phase1-acd` from the Mac; the machine the code was written on has no credentials. Needs: the Mac.
2. [ ] **Start the Editor check.** Hand docs/verification/UNITY_SIDE_BRIEF.md to a Claude session on the Mac (paste-to-start block in its last section). Order: import and compile, set-up script, shaders, the Unity-only tests, first look in play mode, the Station on the Mac. Findings go to docs/verification/report-<date>.md. Needs: the Mac.
3. [ ] **Commit what the Editor regenerates.** First open rewrites the URP asset, the renderer features and the layer names. Commit them. In play mode, use the Welcome card button "Sample room (no scan)" to reach Home fast (XR Simulation is on, so no room loads by itself). Needs: the Mac.
4. [ ] **Give the lead decisions** (next section), at least the Apple team id and the lens numbers, before the iOS build. Needs: the lead.
5. [ ] **iOS build, then gate G1 first light:** 60 fps in an empty stereo scene on the phone, telemetry on the Station, one-command install (tools/build_ios.sh). Needs: the Mac, a phone, the Apple team id.
6. [ ] **Gates G5 and G6:** scan, Home and Bump Guard with a spotter; then the Station link with its four functions. Needs: a phone, a headset, a spotter, the Station.
7. [ ] **Combine laneB/integration with phase1-acd** on a new branch (see "Other branches"), then gates G2, G3, G4 and G7. Can go on in parallel with items 2 to 6. Needs: one session with the headless check.
8. [ ] **Run the real generator.** Install PyTorch cu128 and TripoSG on the Station PC, pin the commit, and run one object through the Forge. Needs: the RTX 5070 PC.


## Decisions the lead must give

| Item | Why it matters | In force meanwhile |
|---|---|---|
| Headset lens numbers (distortion, lens separation, screen-to-lens distance); camera-to-eye offset | Straight lines bow; the 5 cm wall test (P1-C07) is at risk | Cardboard-class placeholders; offset is zero |
| Eye field of view | Too small wastes pixels, too large shows a black border | 50.28 deg each side, 48 deg up and down |
| Apple team id | tools/build_ios.sh cannot sign without it | Empty; read from ATLAS_TEAM_ID at build time |
| Hosted image-to-3D vendor for the Forge fallback | No fallback if the RTX 5070 PC is down | Disabled until chosen |
| Second-phone policy | Two walkers or a spare phone | The Hub refuses a second phone |
| Comfort stop value (open item 6 of the plan) | Sets when the Console alerts on the comfort score | Console has the entry; no alert threshold |
| Gamepad model | Input map and rumble (none on iOS yet) | Generic gamepad, no rumble |
| Bump Guard at the lap corners (open item 19) | Above 0.5 m/s the 0.5 m lap inset gives too little warning | Inset of 0.5 m |
| One URP asset plus a code table, instead of three tier assets (P1-A03) | Plan text and code disagree | One asset and a code table |

**Open for the lead, from the four contract pages** (merged and deduplicated; the full text is at the end of each page):

- [ ] Lane A: lens numbers; head offset; field of view; High tier and extra registry values; ladder thresholds; flat shading (no normal term); four extra shaders against rule F7; one 0.25 m rule or two.
- [ ] Lane B: TileId.Step on a bare id; kappa tag in Iso2; how the curvature ramp is called; L at kappa = 0; no re-registration after tracking loss in curved spaces; normals under the warp; ContentSeed collision across worlds; which head position the warp reads. Decide the first four before P1-B02 to P1-B04.
- [ ] Lane C: lap-corner Bump Guard; rumble; headset lever tap; which rectangle is "largest"; Home shell in large rooms; furnished sample room size; kerb height; haptics plugin; Bump Guard armed by onboarding; depth image size; camera picture during scan; additions to the room model; furniture record for pre-scan; two world-map stores.
- [ ] Lane D: hosted image-to-3D vendor; Station address (192.168.0.10 default); where the phone CSV lives; session-delete unit; Console access control; text and shader exceptions; no Spectator interpolation; loss simulation on the real link; comfort stop value; uploading the phone CSV.


## Not built yet

| Item | What is missing | What it blocks |
|---|---|---|
| Lane B, tasks P1-B01 to P1-B12 | Not on this branch: only contract types with a flat stub, an identity warp and a fake tile provider. The real kernel, tile graph, warp, tile renderer, world generator, Small Planet, Aligned Cells and Curved Walk are on laneB/integration, not yet combined with this branch. | Spaces S2, S3, S4; the curvature dial; gates G2, G3 (lane B half), G4, G7 |
| P1-C05 RoomPlan wall bridge (Extended) | Swift plugin, wall/door/window JSON, configuration re-apply after capture | Nothing critical; walls come from mesh and planes |
| TripoSG image-to-3D | Adapter has never executed. Needs the RTX 5070, PyTorch cu128 and a pinned commit. Tests use a procedural stand-in. | Real Forge output (P1-D08, P1-D09 on real meshes) |
| Flat-twin map (P1-D07) | No Poincare disc; needs lane B tile graph | Map for S2 to S4 |
| P1-A03 quality tiers | One URP asset plus code table; the asset on disk is still the pre-build one | Lead sign-off; thermal ladder check |
| Per-lane sandbox scenes | None exist; the boot scene with a fixture room and keyboard walker serves all lanes | Strict reading of G0 acceptance |
| Gaze twins for four space actions | Return home, next and previous space, space menu work from the gamepad, and by gaze only through the menu, not as own dwell targets | Strict G5 |
| Clock sync (P1-D01) | Receive-thread timestamps are in: 2 ms p95 holds headless at one poll per frame (`Link_ReceiveStamp`); still to be measured on the phone over Wi-Fi | Link timing claim |
| Editor headset view | A mouse click on the development overlay still counts as the lever for a gaze target | Editor-only; minor |
| Baked parameter defaults (P1-D03) | No defaults file exists, so the end of the bake path is unproven | Running tuned values with the Station off |
| Fallback furniture (P1-D12) | The built-in meshes are generated box composites of 22 to about 100 triangles, not sourced CC0 models; the retry back-off has no 4 s step | Look of a room when the Forge is away |
| Gate G0 close-out | Branch not merged to main, no tag | Clean freeze of the contracts |


## Built, waiting for the Editor

Gate G0 and the look/Station pieces. Run in the Editor first; nothing here has been compiled or rendered.

| Task | What must be seen or measured |
|---|---|
| P1-X02 Contracts frozen (gate G0) | The project opens with no red line in the Console; every EditMode test is green in the Test Runner; the contracts are merged to main and tagged. No per-lane sandbox scene exists. |
| P1-A06 Shared look shader | All seven shaders compile for Metal; the Frame Debugger reports Atlas/Surface as SRP-Batcher compatible with at most 8 variants; a test room renders in stereo; the 7 look tests run. |
| P1-D05 Spectator | The Windows build made with AtlasBuild.Spectator runs; the mirror and the flat twin draw side by side; mirror lag at most 150 ms at p95 (there is no lag read-out yet). |


## Built, waiting for a phone

By the gate where each is first checked. 30 tasks. The numbers are the plan's pass marks.

| Gate | Task | What must be seen or measured |
|---|---|---|
| G1 | P1-A02 First build on both phones | Empty AR scene runs on phone A and phone B with tracking Normal; the thermal plugin returns a value; iOS versions recorded. |
| G1 | P1-A04 Stereo rig | 60 fps on the Metal HUD, CPU main <= 3 ms, GPU <= 2 ms; a cube at 1 m fuses for three testers; ARKit still delivers pose and depth in the headset view. |
| G1 | P1-A13 One-command build | tools/build_ios.sh installs on a phone; a second engineer does it from the README alone. |
| G1 | P1-C01 AR session | The session holds 60 Hz; the depth image is non-null at the chosen video format. |
| G1 | P1-D04 Perf probe, HUD, telemetry | HUD under 0.2 ms and 0 B per frame; a 15-minute CSV with no gap; GPU time and thermal state read correctly in a release build. |
| G1 | P1-D11 Pairing | A fresh install pairs by QR in <= 30 s; the link returns within 5 s of a Wi-Fi drop; the Local Network prompt appears in the hand. |
| G4 | P1-A05 Lens blit | A straight grid looks straight to three testers; the pass adds <= 1.0 ms GPU; an Xcode capture shows one blit to the screen and eye passes that resolve on tile. |
| G4 | P1-A07 Thermal ladder | Each tier forced from the Console takes effect within 1 s; at most one step per 20 s. |
| G4 | P1-A08 SpaceLab host | Memory within +/-5 % after 20 switches among the spaces. |
| G4 | P1-A09 Glued S1 | No seam in either eye across a cell edge; Klein rows mirrored correctly; <= 110 draw calls and 110k triangles at 5 x 5. |
| G4 | P1-A10 Portal engine | No seam or pop crossing a doorway in stereo; <= 1.0 ms GPU per visible portal; the stencil passes stay merged. |
| G4 | P1-A12 Slicer S6 | Tesseract slice <= 0.5 ms, 18,000-tetrahedron tube <= 1.0 ms at p95, 0 B per frame. |
| G4 | P1-A14 Perf runs and soak | The metrics table of plan section 11.2 filled for S0, S1, S5 and S6; 15-minute soak with no Critical thermal state, mean >= 45 fps, never below 30. |
| G5 | P1-C02 Manual outline | Polygon within 5 cm of a tape measure in three rooms. |
| G5 | P1-C03 Automatic FloorScan | Free rectangle within 10 cm of a tape measure; scan to walkable in <= 90 s. |
| G5 | P1-C04 Object boxes | Boxes overlay the mesh to <= 10 cm; every item wider than 0.4 m has a box. |
| G5 | P1-C06 RoomModel and loop-fit | A manual and an automatic scan of one room agree within 10 cm; meshing and planes are off after the scan. |
| G5 | P1-C07 Home S0 | A virtual wall within 5 cm of the real wall at three touch points; <= 60 draw calls on the budget room. |
| G5 | P1-C08 Photo capture | Under 2 ms per frame while scanning; a crop for every box with a candidate frame; crops upright and correctly coloured. |
| G5 | P1-C09 Bump Guard | Warning >= 0.6 s before contact in 20 of 20 real approaches; at most 2 false cues in a 5-minute lap; <= 0.3 ms per frame. With a spotter. |
| G5 | P1-C10 Input and twins | Every action by the gamepad alone and by gaze alone. Four space actions and the dial have no gaze target of their own yet. |
| G5 | P1-C12 Onboarding | Three first-time users reach Home in the headset with at most three prompts each. |
| G5 | P1-C13 World map | A relocalised room aligns to <= 5 cm within 10 s. |
| G5 | P1-C14 Cue and haptics | Indicator, sound and haptic start within one frame in 20 of 20 trials; <= 0.2 ms per frame. |
| G6 | P1-D03 Registry and tuner | A slider change is visible on the phone within 100 ms at p95; baked defaults load with the Hub off. |
| G6 | P1-D06 Operator Console | Each of the six commands acts within 250 ms; STOP gives indicator, tone and haptic; the layout is checked in a real browser. |
| G6 | P1-D10 Asset client | Worst frame while a mesh swaps in under 20 ms; the plain-http upload works on iOS. |
| G6 | P1-D12 Fallback set | With the Forge stopped, every box holds a fallback mesh within 10 s of entering Home; each is replaced after a Forge restart. |
| G6 | P1-D13 On-device menu | With the link off, change each kill-switch parameter from the gamepad and find it in the phone's log. |
| G7 | P1-A15 Room in Glued, standing mode | A virtual table edge within 5 cm of the real one at three points; a 1.4 x 1.4 m room loads S1 and S6. |

Also built and proven headless, with a short run in the Editor still owed: P1-X05, P1-X06, P1-X07, P1-X08, P1-A11, P1-C11, P1-C15, P1-D01, P1-D02, P1-D09. Partly built (P1-A03, P1-D07, P1-D08) are in the not-built table.


## Other branches

- [ ] **laneB/integration can be combined with phase1-acd, and it was built for that.** It is 113 commits and 843 files (the real kernel, tile graph, warp, tile renderer, world generator, Small Planet, Aligned Cells, Curved Walk, and lane B of the Lung and FieldFit use cases), forked one commit before this branch.
- [ ] A trial merge (nothing changed) gives 59 conflicted paths out of 843: 33 where both sides added the same file, 5 content conflicts, 2 files deleted here and edited there. About 21 are real files; the rest are their .meta files.
- [ ] The rule is in docs/laneB/INTEGRATION_NOTES.md on that branch: lane B wrote 35 placeholder files for lanes A, C and D, each marked `PLACEHOLDER (lane X, ...)`. At those paths keep this branch's real file; under Kernel/, Spaces/CurvedWalk, Spaces/AlignedCells, Spaces/SmallPlanet and the warp includes keep lane B's; merge the two id sets in AtlasShaderIDs.cs; delete the placeholder self-tests.
- [ ] The work after the conflicts is the larger part: this branch's flat stubs (Kernel/TileId.cs, ITileGraph.cs, IWalkerKernel.cs, FlatTileGraph.cs and others) define the same types as lane B's real files at other paths, so they must go and the frame loop, SpaceLab and the telemetry world hash must call the real kernel; and lane B's code must compile against the real ParamRegistry, StateMessage, RoomModel, IBumpGuard and input types where they differ from its placeholders.
- [ ] Done when `python3 tools/headless/verify.py all` is green on the merged tree and lane B's spaces are registered and in tools/gen_scenes.py. Do it on a new branch, not on either source branch.
- [ ] p2-lung-cores and p2-lung-data (Phase 2 Lung Mission, separate worktrees) both merge into phase1-acd with no conflicts. They touch only station/pipelines, docs/case_contract.md and .gitignore.
- [ ] Merging adds the files but does not integrate them. The cores are standalone .NET projects under station/pipelines/csharp, not Unity assemblies.
- [ ] Some of that work depends on the kernel that lane B has not built.


## Where things are

- [ ] Editor brief: docs/verification/UNITY_SIDE_BRIEF.md (section 4 top-ten risks, section 5 known gaps).
- [ ] Contracts: docs/contracts/ (README and lane-a-platform, lane-b-kernel, lane-c-room, lane-d-link).
- [ ] Headless check: python3 tools/headless/verify.py all
- [ ] Build for the phone: tools/build_ios.sh (needs ATLAS_TEAM_ID); queue rules in docs/build-seat.md.
- [ ] Station rehearsal (Hub, Forge and ReplayPhone together): bash tools/e2e/run_station_rehearsal.sh
