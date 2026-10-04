# Integrating Lane B with lanes A, C and D: read this first

This is for the person or agent who merges `laneB/integration` with the other lanes' work.

## The short version

Lane B (Kernel and Curved Worlds) was built while lanes A and C were still being written on another computer, and before lane D had started. Lane B's code needs parts of their contracts: the space host, the shader, the RoomModel, the Home rebuild, Bump Guard, input, the parameter registry and the case data. So this branch contains **placeholders** for those parts.

- **A placeholder stands in for code another lane owns.** It sits at the path where the owning lane's real file will go (Master Plan App. C.9 and §6.2). It exposes only the contract surface the plan defines.
- **Every placeholder file starts with this header:**
  `// PLACEHOLDER (lane X, <task id>): contract surface only; replaced by lane X's implementation`
  Find them all with: `git grep -l "PLACEHOLDER (lane"`
- **The full list is in [placeholders.md](placeholders.md).** It gives every file, its lane, its plan task, the surface it exposes and its behaviour, plus the assumptions each lane must confirm.
- **Lane B code uses only the public contract surface**, never the internals of a placeholder. So replacing a placeholder with the real file should need no change to Lane B code, provided the real file keeps the plan's names and signatures.

## How to merge

1. **Merge the other lane's branch.** Conflicts at placeholder paths are expected.
2. **At every placeholder path, keep the owning lane's real file and drop the placeholder.** Before resolving any conflict, check whether the file carries the `PLACEHOLDER` header.
3. **In the same commit, delete that lane's placeholder self-tests.** These are `ATLAS/Assets/Atlas/Tests/PlaceholdersA`, `PlaceholdersC` and `PlaceholdersD`. Some of them assert behaviour only the placeholders have, so they will fail against real code. For lane C, also remove the `InternalsVisibleTo("Atlas.PlaceholdersC.Tests")` line in `Atlas/Input/AssemblyInfo.cs`.
4. **Delete the placeholder fixture generators once the real fixtures land.** These are `tools/placeholders/make_fixtures_c.py` and `make_fixtures_d.py`. The real fixtures are lane C's `recordings/room_*.json` and lane D's `StreamingAssets/cases/...` and `fieldloss/`.
5. **Do not drop Lane B's own files** when resolving conflicts.
   - **Lane B owns:**
     - `AtlasWarp.hlsl` and `AtlasGates.hlsl` (the real versions are on this branch)
     - Lane B's ids in `AtlasShaderIDs.cs`
     - everything under `Kernel/` except Mesh4D
     - `Spaces/CurvedWalk`, `Spaces/AlignedCells` and `Spaces/SmallPlanet`
     - Lane B's code in `Blades/Lung` and `Blades/FieldFit`
     - `docs/contracts/kernel.md`
     - `docs/laneB/`
   - **`AtlasShaderIDs.cs` is shared:** lane A and lane B each add their own ids, so merge the two sets.
6. **Run the full EditMode suite.** If a Lane B test fails after a swap, the real code differs from the contract that Lane B assumed. Check the matching item in "Assumptions to confirm" in [placeholders.md](placeholders.md). Settle it with the owning lane; don't weaken the test.

## Edits this branch made to other lanes' files

Announce these to the owning lanes. Details are in placeholders.md, items X1–X4 and R1–R8.

- **X1:** `Tests/Safety/SafetyDependencyTests.cs` (lane C) now checks the direct references of `Atlas.Safety`. The previous check walked references transitively, and it can no longer pass now that `Atlas.World` references `Atlas.Link`.
- **X2:** `Atlas.Safety.asmdef` (lane C) gained a reference to `Atlas.World`. Master Plan §6.2 says Safety may use RoomModel types from World only.
- **X3:** `Atlas.World.asmdef` and `Tests/World/WorldDependencyTests.cs` (lane A) gained `Atlas.Link`. `ISpaceModule` uses `ParamRegistry` and `StateMessage` from Link (§6.5).
- **X4:** `Atlas.Kernel.Tests.asmdef` (lane B) gained `Unity.Mathematics`.

## Decisions still open

- **PH-39, the airway route length.** Plan task P2-D07 asks for "exactly 1 Two Compasses fork on the 4-fork route". A Two Compasses fork is one where a plain compass points down the wrong branch. With the plan's geometry and compass rule, no 4-fork route contains such a fork. The placeholder `tree.json` therefore uses a 6-fork route. Lane B code reads the route length and the Two Compasses fork from `tree.json` and never hard-codes 4. The 4-fork acceptance is **not** claimed as passed. Lane D or the lead must decide: change the compass rule or the target-point rule (and announce it), or accept 6 forks.
- **G0 points from [kernel.md](../contracts/kernel.md) §9:**
  - `_Anchor`, `_WarpMode` and `_Gates` must sit in lane A's single `UnityPerMaterial` block.
  - Lanes A and C must decide whether the Unity world frame equals the RoomModel anchor frame, with y = 0 on the floor.
  - Lane D's Spectator must use the tile-id word order and the `State.xform` layout fixed in kernel.md.
- **Lane C's call:**
  - `Atlas.Safety` still references `Atlas.Scan`, which §6.2 does not list (R3).
  - `Atlas.App` (`AtlasFrameLoop`) does not exist yet. The placeholder seam is `SpaceLab` plus `FrameStages.cs` (R4).

## What state Lane B is in

- **All 33 Lane B tasks are implemented and merged:** X06, P1-B01–B12, P2-B01–B12 and P3-B01–B08. The per-task state is in [STATUS.md](STATUS.md).
- **Tests:** the integration merge without P2-B12 passed 454 of 454 EditMode tests. P2-B12 passed 210 of 210 on its own branch. The suite was not rerun after P2-B12 was merged.
- **Skipped headless:** some shader and GPU tests can't run under `-nographics` (FieldFit blit, lens badge, warp compute probe). Run the suite once with a graphics device.
- **Phone-only acceptance items** are listed in [TESTING.md](TESTING.md), with the scene to open, the steps and the expected numbers.
- **Not done yet:**
  - the Phase-4 maths documents (P4-B01–B05)
  - the end-to-end scenario tests
  - the Opus gate-end checks
- **Mesh4D (lane A, P1-A12) has no placeholder.** P4-B02's tesseract check needs it.
