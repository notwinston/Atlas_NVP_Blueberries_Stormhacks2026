# Scene and shared-asset ownership

Unity scenes are single files, so each has exactly one owner. Source: Master Plan §9.3 and Appendix C.9.
Scene names follow Appendix C.9, which matches the `Atlas.Spaces.*` assembly names (§9.3 uses shorter names for four of them: `Space_Rooms`, `Space_Curved`, `Space_Cells`, `Space_Planet`).

Lanes: **A** Platform, Look and Flat Spaces · **B** Kernel and Curved Worlds · **C** Scan, Room and Safety · **D** Link, Station and Forge.

## Scenes

| Scene | Owner | Rule |
|---|---|---|
| `Atlas_Boot` (XR origin, stereo rig, SpaceLab, bootstraps) | A | Nobody else edits it. Systems register themselves from their own scenes |
| `Space_Glued`, `Space_Portal`, `Space_Slicer` | A | One additive scene per space |
| `Space_CurvedWalk`, `Space_AlignedCells`, `Space_SmallPlanet` | B | One additive scene per space |
| `Space_Home` | C | One additive scene per space |
| `Sys_Scan`, `Sys_BumpGuard`, `Sys_Onboarding` | C | Loaded additively by the boot scene |
| `Sys_Link`, `Sys_Hud`, `PC_Spectator` | D | `PC_Spectator` never enters the iOS build list |
| Use-case scenes (Phase 2 onwards) | The lane that owns the use case's runtime | Reference spaces through `ISpaceModule`; never copy their objects |
| `Sandbox_<name>` | Each engineer | Never in the build list |

## Shared assets

| Asset | Owner | Rule |
|---|---|---|
| URP and renderer assets, Project and Player settings, package manifest, layer and stencil table (refs 1–15) | A | Changed only by A, announced before commit |
| The shared material and palette | A | Per-tile and per-room material instances are created at run time from a pool, never saved as assets |
| `AtlasCore.hlsl` (palette, AO, lines, fog, the single `UnityPerMaterial` block), `AtlasPortalClip.hlsl` | A | A only |
| `AtlasWarp.hlsl` (one entry point); `AtlasShaderIDs.cs` (every property id) | B | B only; ids after telling A and C |

## Rules

- Tunables live in the parameter registry and in ScriptableObjects, never in scenes.
- No new shader keyword without lane A.
- The shared shader stays SRP-Batcher compatible at every gate.
- Asset Store packs stay out of the repository.
