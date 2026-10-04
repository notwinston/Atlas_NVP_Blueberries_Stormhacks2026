# Scene and shared-asset ownership

Unity scenes are single files, so each has exactly one owner. Source: Master Plan §9.3 and Appendix C.9.
Scene names follow Appendix C.9, which matches the `Atlas.Spaces.*` assembly names (§9.3 uses shorter names for four of them: `Space_Rooms`, `Space_Curved`, `Space_Cells`, `Space_Planet`).

Lanes: **A** Platform, Look and Flat Spaces · **B** Kernel and Curved Worlds · **C** Scan, Room and Safety · **D** Link, Station and Forge.

## Scenes

| Scene | Owner | Rule |
|---|---|---|
| `Atlas_Boot` (AR Session and XR Origin, nothing else) | A | Nobody edits it. `Atlas.App.AtlasBootstrap` builds the stereo rig, the systems, the frame loop and SpaceLab in code after it loads (contracts README F3) |
| `Space_Glued`, `Space_Portal`, `Space_Slicer` | A | One additive scene per space: one root object with the space's module. Written by `tools/gen_scenes.py`; the module builds its content in code |
| `Space_CurvedWalk`, `Space_AlignedCells`, `Space_SmallPlanet` | B | One additive scene per space. Not created yet: lane B's spaces are not built |
| `Space_Home` | C | One additive scene per space, written by `tools/gen_scenes.py` |
| `Sys_Scan`, `Sys_BumpGuard`, `Sys_Onboarding` | C | Names reserved. Not created: these systems are installed in code by the composition root, one installer per assembly |
| `Sys_Link`, `Sys_Hud` | D | Names reserved. Not created: installed in code, as above |
| `PC_Spectator` | D | One marker object; the Spectator composes itself in code. Never in the iOS build list; `AtlasBuild.Spectator` builds it for the Station |
| Use-case scenes (Phase 2 onwards) | The lane that owns the use case's runtime | Reference spaces through `ISpaceModule`; never copy their objects |
| `Sandbox_<name>` | Each engineer | Never in the build list |

## Shared assets

| Asset | Owner | Rule |
|---|---|---|
| URP and renderer assets, Project and Player settings, package manifest, layer and stencil table (refs 1–15) | A | Changed only by A, announced before commit |
| The shared material and palette | A | Per-tile and per-room material instances are created at run time from a pool, never saved as assets |
| `AtlasCore.hlsl` (palette, AO, lines, fog, the single `UnityPerMaterial` block), `AtlasPortalClip.hlsl` | A | A only |
| `AtlasWarp.hlsl`, `AtlasGates.hlsl` (one entry point each) | B | B only. Until lane B starts they are identity implementations kept by lane A |
| `AtlasShaderIDs.cs` (every property id) | A and B | Lanes A and B only (Master Plan §6.4); an id is added or changed only after all four lanes are told. Lane A keeps it until lane B starts |

## Rules

- Tunables live in the parameter registry, never in scenes.
- Scenes hold no authored content in Phase 1. After adding a space module, run `tools/gen_meta.py` then `tools/gen_scenes.py`.
- No new shader keyword without lane A.
- The shared shader stays SRP-Batcher compatible at every gate.
- Asset Store packs stay out of the repository.
