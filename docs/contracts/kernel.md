# Kernel contract (lane B): moved

This page is now part of [lane-b-kernel.md](lane-b-kernel.md), the one lane B contract page. The two pages that were written for gate G0 (this one, from lane B, and the platform side's `lane-b-kernel.md`) were merged on 4 October 2026 so that a single page describes the code of branch `phase1-integration`. Nothing is kept here but this table, because code comments cite "kernel.md §n".

Sections 1 to 10 of `lane-b-kernel.md` carry the numbers this page had, so a comment that cites "kernel.md §5.3", "§4.6", "§6.2" and so on is read at the same section of the other page. What changed in that text:

| This page | Now |
|---|---|
| §4.3 `Maps` | §4.3 `Geometry2` (`Kernel/Geometry2.cs`), same four members |
| §4.1 signatures, §4.2 `Iso2`, §4.4 to §4.6 | Same sections; they describe the unified types (`TileId.Step` is real, `Iso2` has the platform side's members, `VisibleTile` has `Tile` and `Chain`) |
| §6.2 `_HypL` and `_HeadPosWS` declared by the include | §6.2: declared in `AtlasCore.hlsl`; `_HeadPosWS` is `float4`; the include declares nothing |
| §6.5 and §6.7 one `float4x4 _Gates` and the id `Gates` | §6.5 and §6.7: four vectors `_Gate0` to `_Gate3`, ids `Gate0` to `Gate3`, `MaterialPool.SetGate` |
| §6 shader files under `Rendering/Shaders/` | `Rendering/Shaders/Resources/` |
| §9 points 1 and 2 | §9.1 and §9.2, settled |
| §9 point 4 "no question open" | §9.3 and §9.4: what the code decided and what the lead still confirms |

Numbers of the other page, for comments that cite "lane-b-kernel.md section n": the table in §1.1 of the new page.
