# Contracts

The interfaces between the four lanes. They are agreed before the lanes' code exists, so the lanes can work in parallel (Master Plan §6.4).

- **Writing.** Each lane writes the pages for the contracts it owns, straight from the Master Plan and without code (`P1-X05` to `P1-X08`).
- **Freezing.** Gate G0 (`P1-X02`) merges the four sets and freezes them, once the stubs for every interface compile in every assembly. The stubs are the identity warp, a fake tile provider, a loopback link, a recorded pose log and the sample rooms as RoomModel files.
- **Changing.** After G0 a contract changes only after all four lanes are told, and the change is made by a human (§6.4, §9.9).
- **Reading.** Agents read these pages and may not edit another lane's page.

The Master Plan (`documents/ATLAS_Master_Plan.md`) is the source of every page. Where a page and the plan disagree, the plan wins.

## The four contract sets

| Set | Page | Owner | Task | Status | Covers |
|---|---|---|---|---|---|
| Lane A · Platform, Look and Flat Spaces | Lane A, to be written | Lane A | `P1-X05` | Not yet written | `ISpaceModule`. The shader include contract: entry points and signatures of `AtlasCore.hlsl` and `AtlasPortalClip.hlsl`, the single `UnityPerMaterial` block, and the list of ids for `AtlasShaderIDs.cs` (with lane B). The layer and stencil table. The frame-loop order (`AtlasFrameLoop`). Scene ownership (now in `docs/scene-ownership.md`) |
| Lane B · Kernel and Curved Worlds | [`kernel.md`](kernel.md) | Lane B | `P1-X06` | Written; to be read by lanes A, C and D before G0 | Kernel types (`Iso2`, `TileId`, `WalkerPose`, `ITileGraph`, `VisibleTile`); metric η = diag(1, 1, −1) on the unit hyperboloid; κ, units and precision rules; the ℤ[φ] tile-id encoding and edge numbering; the `AtlasWarp.hlsl` entry point and `AtlasGates.hlsl`; lane B's ids in `AtlasShaderIDs.cs` |
| Lane C · Scan, Room and Safety | Lane C, to be written | Lane C | `P1-X07` | Not yet written | The RoomModel JSON schema, its coordinate frame and units (§4.3). Three sample rooms: 3 × 3 m, a 3 × 4 m L-shape, and 4 × 5 m with a table. The performance fixture `budget_room`: 4 × 5 m, twelve stand-in objects of exactly 5,000 triangles each, no edge over 0.25 m, used for budget tests only |
| Lane D · Link, Station and Forge | Lane D, to be written | Lane D | `P1-X08` | Not yet written | The parameter registry: how a tunable is declared (id, group, range, default) and how it reaches the Console. Link message ids and the `State` layout (Appendix B), with a hand-packed 202-byte `State` example. The GLB asset contract |

Each owning lane adds its page or pages to this folder and fills in its row.

## Contracts by owner and consumer (§6.4)

| Contract | What it fixes | Owner | Main consumers | Page |
|---|---|---|---|---|
| `ISpaceModule` | How a space is configured from the RoomModel, entered, ticked and left, and how it describes itself to the Spectator | A | all | Lane A |
| Kernel types | `Iso2`, `TileId`, `WalkerPose`, `ITileGraph`; unit hyperboloid with metric diag(1, 1, −1); lengths divided by L at the boundary | B | A, C, D | [`kernel.md`](kernel.md) |
| RoomModel schema | The fields of §4.3, coordinate frame, units; three sample rooms; `budget_room` | C | all | Lane C |
| Shader include contract | Entry points and signatures of the four includes; the single `UnityPerMaterial` block; every property id in `AtlasShaderIDs.cs` | A and B | all | Lane A for `AtlasCore.hlsl`, `AtlasPortalClip.hlsl` and the block; [`kernel.md`](kernel.md) §6 for `AtlasWarp.hlsl`, `AtlasGates.hlsl` and lane B's ids |
| Parameter registry | How a tunable is declared and reaches the Console. Bump Guard declares none | D | all | Lane D |
| Link schema | Message ids, the `State` layout, the event format (Appendix B) | D | all | Lane D |
| Asset contract | What a generated GLB must satisfy (one mesh, metres, Y up, 1,500–5,000 triangles, longest edge ≤ 0.25 m, and the rest of §6.4) | D | C | Lane D |
| Layer and stencil table | One row per room layer and stencil reference (1–15) | A | B | Lane A |
| Frame loop | `AtlasFrameLoop` and its call order (§6.3, steps 2–6) | A | B, C | Lane A |
| Scene ownership | Who may edit which scene and prefab (§9.3) | A | all | `docs/scene-ownership.md` |

## Shared files edited by two lanes

- **`AtlasShaderIDs.cs`.** Edited by lanes A and B only. An id is added or changed only after all four lanes are told.
- **`AtlasWarp.hlsl` and `AtlasGates.hlsl`.** Lane B only.
- **`AtlasCore.hlsl` and `AtlasPortalClip.hlsl`.** Lane A only.
