# Lane B joined with lanes A, C and D: what was done

Lane B (Kernel and Curved Worlds) was built while lanes A and C were still being written on another computer, and before lane D had started, so it carried **placeholders** for the parts of their contracts it needed. The two histories were combined on branch `phase1-integration` (from `phase1-acd` and `laneB/integration`). This page records how, so that nobody has to redo it, and what is left. The placeholders that remain are in [placeholders.md](placeholders.md); the state per task is in [STATUS.md](STATUS.md); what waits for a phone is in [TESTING.md](TESTING.md).

**The integration is done as far as it can be without a phone, a headset or the Unity Editor.** It has not been opened in Unity: shaders, generated scenes and every `[Category("Unity")]` test of both lanes wait for the Editor session (`docs/verification/UNITY_SIDE_BRIEF.md`).

## Order of the work

| Commit | What |
|---|---|
| `0d88622` | Merge of `laneB/integration` into `phase1-acd`, with the conflicts resolved: at every placeholder path the owning lane's real file, under `Kernel/`, the spaces and the warp includes lane B's |
| `8b32665` | One kernel: lane B's `TileId`, `Iso2`, tile graphs and holonomy rig are the implementation and the platform side's contract surface sits on them. The Link, Telemetry and Input placeholders are gone |
| `63f7894` | Lane B's world generation, warp, gates, tile meshes and tile renderer on the real World and look contracts (`MaterialPool`, `AtlasLook`, the single `UnityPerMaterial` block, `RoomModel`) |
| `7e7aee7` | The room builder, the room store and the Station project files that `.gitignore` had hidden are tracked |
| `db8122c` | Lane B's kernel in the app's frame loop (one `HolonomyWalkerKernel` behind `IWalkerKernel`), `State` carrying the real tile id and transform, the generated world drawn, the Lung and FieldFit blades on the real contracts |
| `9320d25` | S2, S3 and S4 as `MonoBehaviour` modules registered through `SpaceRegistry` like the flat spaces, on the shared kernel and the real room content |
| `d1dfcab` | The generated scenes, build list, menu, performance tour and soak cover S2 to S4; the application and end-to-end tests run the real spaces through `SpaceLab`; the reference table admits lane B's assemblies |

## Decisions taken for the merge (by the team lead)

Each is described where the code is, on `docs/contracts/lane-b-kernel.md` (the one lane B page now; `kernel.md` points to it).

1. **One walker kernel.** The app's frame-loop kernel is the rig. `HolonomyWalkerKernel` replaced `FlatWalkerKernel` at the composition root. A curved space configures it with `KernelConfig.Curved(q, L)` on entry and SpaceLab restores flat after `Exit`. `IWalkerKernel` gained a timed `Step` so the curvature ramp advances (an announced contract change). Lane B page §4.9, §7.2.
2. **Small Planet's sphere walker moved into that kernel** (q = 3), so step 4, `_HypL`, `State` and the world hash are right in S4. §4.11.
3. **Gates use lane A's frozen layout**: `_Gate0` to `_Gate3`, `MaterialPool.SetGate`. There is no `AtlasShaderIDs.Gates` and no `float4x4 _Gates`. §6.5.
4. **The warp centres on the filtered head sample** that stepped the kernel (`FrameContext.HeadPosition`), not the raw one. §6.2.
5. **S2 to S3 (Mode 1 to Mode 2) keeps the tile id and the pose** (plan §4.8, §4.9; P1-B10). The rule is inside `SpaceLab`; no member was added to `ISpaceHost` or `ISpaceModule`. §7.2.
6. **Lane B's space modules are `MonoBehaviour`s created by a factory and registered through `SpaceRegistry`**, exactly like lane A's. Services come from `AtlasServices`. The generated scenes of `tools/gen_scenes.py` (`Assets/Atlas/Scenes/`) replace lane B's hand-made `Space_*.unity` scenes, `.mat` files and scene-host shims.
7. **Parameters are declared once, through the real `ParamRegistry`.** Where `AppParams` already declares one (`curved.cullRho`, `curved.mode`) that declaration stays. `State.p[0]` in a flat space is lane A's (`MetresPerUnit` = 1).
8. **Room content in curved spaces is lane C's real `IRoomContent`**, its own renderers warped through the shared materials, never a second copy of the room.

## What replaced what

| Lane B used | Now |
|---|---|
| Its own `TileId`, `Iso2`, `ITileGraph`, `VisibleTile` and the platform side's stubs | One definition each, in `Atlas.Kernel` (§4 of the lane B page). `KernelMaps` is `Geometry2`; `TileId.ToString()` is the comma-separated wire text |
| `IKernelStage.KernelStep`, `IUploadStage`, `Atlas.World.SpaceLab` (placeholder), `FrameStages.cs` | The frame loop's step 4 (`IWalkerKernel`), `IUploadStep`, `ISpaceHost` and the real `Atlas.App.SpaceLab` |
| `FrameContext` (readonly, seven-argument constructor) | Lane A's mutable struct (`FrameIndex`, `HeadPosition`, `Tracking`, `Walker`, `Kernel`, `RawHeadPosition`, …) |
| `IHomeShell`, `HomeRebuild`, `KerbMesh`, `Wall` | `IRoomContent` and `RoomContent`; `RoomShellBuilder.Kerbs`; `RoomWall` |
| The placeholder `RoomModel` (JsonUtility, `FromJson`, `Load`) | Lane C's `RoomModel`, `RoomModelJson.Read`, `RoomFixtures.LoadFromProject(name)` |
| `recordings/room_3x3.json`, `room_3x4_L.json`, `room_4x5_table.json`, `budget_room.json` and `tools/placeholders/make_fixtures_c.py` | Lane C's `sample_3x3`, `sample_L_3x4`, `sample_4x5_table`, `budget_room`, `tiny_standing` (`World/Resources/AtlasRooms/`, generated by `tools/rooms/make_fixtures.py`) |
| `Atlas.Scan.PoseLog.Read(path)` | `Atlas.World.PoseLog.Read(TextReader)`; `recordings/walk_lap5_3x3.poselog` and `walk_still_60s.poselog` were converted to `ATLAS-POSELOG 1` (tracking 2 = normal) |
| `IBumpGuard` / `IClearanceQuery` placeholders | Lane C's `BumpGuard`, which now also implements `IClearanceQuery` (a small additive change) |
| `IActionSource`, `GazeTarget` (MonoBehaviour), `AtlasParamScanner`, `ParamRegistry` (ushort ids), `StateMessage` (arrays), `EventMessage` (struct), static `EventLog.Emit` | `IAtlasInput` (`Pressed`, `Axis`, `RegisterGazeTarget`), `ParamBinder` and the real `ParamRegistry` (`ParamEntry`), the array-free `StateMessage`, `EventMessage : JsonMessage`, the instance `EventLog` (plus the scene-tagged `LogJson(scene, ev, data)`, a small addition) |
| `Atlas/AtlasSurface` shader, `AtlasCore.hlsl`, `AtlasShaderIDs`, `AtlasPalette`, `LensBlit`, `StereoRig` placeholders | Lane A's real `Atlas/Surface`, `AtlasCore.hlsl`, `AtlasShaderIDs`, `AtlasPalette`, `LensBlit` and `StereoRig` (lane B added `OverrideEyeTargets` to the rig and `FinalBlitOverride` and `SourceUV` to `LensBlit`); lane B's `AtlasWarp.hlsl` and `AtlasGates.hlsl` stand in the real shader (the warp gained `AtlasWarpClip`) |
| `TileVertex` | `Atlas.Kernel.AtlasVertex` and `AtlasMesh.Layout` |
| Lane B's hand-made scenes (`Space_CurvedWalk.unity` under `Spaces/CurvedWalk/Scenes/` and the others), `S2_Surface.mat`, `S4_Surface.mat`, `CurvedWalk_TileProbe.unity` | Deleted. The generated scenes `Space_CurvedWalk`, `Space_AlignedCells`, `Space_SmallPlanet`; materials from `MaterialPool` |
| `Tests/PlaceholdersA`, `PlaceholdersC`, `PlaceholdersD` | Deleted, with the `InternalsVisibleTo` line |

## Rooms: what changed in the numbers lane B's tests expect

Lane B's old placeholder rooms were replaced by lane C's fixtures; some are different rooms, so a number tied to one of them changed. Load with `RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3)` and so on (the names have no `.json`).

| Old placeholder | Now | What differs |
|---|---|---|
| `room_3x3` | `sample_3x3` | Same outline (±1.5), free rectangle 2.4 × 2.4, s = 2.0. L5 1.884481 → 1.884526 (`1.885` holds to three decimals; a tolerance under 5e-5 needs the new value), L6 1.518603 → 1.518651, R3 1.27324 unchanged. Wall height 2.5 → 2.4. The distance field is 70 × 70 from (−1.75, −1.75), not 60 × 60 from −1.5. Route 2 → `Fixture` (0) |
| `room_3x4_L` | `sample_L_3x4` | A different room: outline (−1.25, −1.5), (2.75, −1.5), (2.75, 0), (1.25, 0), (1.25, 1.5), (−1.25, 1.5); free rectangle 1.9 × 2.4, s = 1.5; L5 1.413394, L6 1.138989 (below the comfort floor 1.4, so the dial uses 1.4 unless `strong`), R3 0.95493 (below its floor 1.2); one wall has an opening |
| `room_4x5_table` | `sample_4x5_table` | A different placement: outline x in [−3.9, 1.1], z in [−2, 2]; free rectangle Wf 1.6 × Df 3.4 (Wf ≤ Df, along X); s = 1.2; L5 1.130715, L6 0.911191, R3 0.763944 (all below their floors). The table is object 1 at (−1.4, 0, 0), size 1.0 × 0.75 × 0.6, yaw −1.570796, `triTarget` 5000; one walkable hole; two walls have openings |
| `budget_room` | `budget_room` | Same 4 × 5 outline; free rectangle 2.4 × 3.4, s = 2.0, L5 1.884526, L6 1.518651, R3 1.27324; glued 2 × 2 (1.2 × 1.7), aligned 3 × 4 with g = 0.8; twelve objects of mixed categories (ids 1 to 12, mesh `StandIn`, `triTarget` 5000), a 60-point walkable outline |
| – | `tiny_standing` | New: 2 × 2 m, free rectangle 1.4 × 1.4, s = 1.0, `Standing` |

Pose logs: `recordings/walk_lap5_3x3.poselog` (1,005 samples) and `walk_still_60s.poselog` (3,600 samples) keep their trajectories in the real format; the tracking column is 2 for normal (code that did `if (s.Tracking != 0) continue;` tests `TrackingState.Normal`). Lane C's own walks are `walk_lap_sample_3x3`, `walk_wall_approach`, `walk_jump_and_loss`. Headless tests find the directory through `Path.Combine(ATLAS_PROJECT_DIR or the current directory, "..", "recordings")`.

## Edits lane B had made to other lanes' files

Announced on the old branch as X1 to X4; all four are settled by the merge. `Atlas.Safety` references `Atlas.World` only and its test checks direct references; `Atlas.World` references `Atlas.Link` and `Kernel`; `Atlas.Kernel.Tests` references `Unity.Mathematics`. `tools/headless/verify.py rules` and `Tests/Architecture/AssemblyGraphTests.cs` now hold the reference table (README F1, including the rows for `Atlas.World.Lint.Editor` and `Atlas.Blades.*`).

## Decisions still open

For the team lead; the full list is in `documents/ATLAS_Whats_Left.md`.

- **PH-39, the airway route length.** Plan task P2-D07 asks for "exactly 1 Two Compasses fork on the 4-fork route". With the plan's geometry (branching angle 37°, plane rotation 90°, ratio 0.794) and the Euclidean compass rule, no 4-fork route has a wrong compass at generation 4 (0 of 299 seeds × 16 targets), so the placeholder `tree.json` uses a 6-fork route (target at generation 6). Lane B reads the route length and the Two Compasses fork from `tree.json` and never hard-codes 4. The 4-fork acceptance is **not** claimed. Decide: change the compass rule or the target-point rule (and announce it), or accept 6 forks (§9.9 rule 7).
- **The FMS channel to the phone.** The automatic Mode 2 rule (FMS above 3 of 20, plan §10.2.3) reads an `fms` event on the phone's own log; `fms` is a Hub-only event (lane D §2.6) and never reaches the phone. No channel exists.
- **The eight lane B contract questions** (answers 2, 4 and 7 need confirmation): lane B page §9.3 and §9.4.
- **Built-in furniture under the warp**, **the material pool budget**, **tile edge lines** and **FieldFit's final-blit orientation**: lane B page §9.4 and `documents/ATLAS_Whats_Left.md`.

## What the merge did not do

- No code was run in the Unity Editor or on a device. The shader of the real warp and gates has never been compiled.
- The Lung and FieldFit use cases (Phase 2 and 3) build against the real contracts and pass their engine-free tests; their scenes (`Blades/*/Scenes/`, `Lung_EndoluminalTest`) and GPU tests were not opened in the Editor.
- Lane D's real case package (`atlas case`, P2-D01 and the data of P2-D07, D08, P3-D01) is not here: the stand-ins are listed in [placeholders.md](placeholders.md).
- The Phase 4 mathematics documents (P4-B01 to P4-B05) are not written.
