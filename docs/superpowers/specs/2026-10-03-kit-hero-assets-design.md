# Kit hero assets for Glued: design

Date 2026-10-03 · branch `phase1-acd` · status: approved in chat, not committed

## Goal

Replace two procedural pieces of S1 Glued with pieces modelled in Blender, through a pipeline every later Blender
asset can reuse: Blender model → GLB in the look's format → `Resources` → `KitSet` → the space's mesh builder.

## Pieces

| Piece | Replaces | Frame | Budget | Must |
|---|---|---|---|---|
| `landmark` | The spiral stair, pole and pennant in `GluedCellMesh.Build` | Foot of the landmark at the origin, floor at y = 0, Unity axes (+X right, +Y up, +Z forward) | ≤ 1,500 triangles | Chiral: no 90°-step yaw makes its x-mirror match it within 2 cm. Fits a 1.2 × 1.7 m cell at `LandmarkBase`; below `GluedCellMesh.PostHeight` |
| `avatar` | `GluedAvatarMesh` | Origin at the eye midpoint, +Z where the walker faces, +X the walker's right, body below | ≤ 600 triangles | `AvatarLeft` cells on −X, `AvatarRight` on +X, a `Text` face marker on +Z; top ≤ 0.15 m above the eyes |

Unchanged: cell floor, quarter posts, chevrons, door frames, W lane, palette slots, shader, contracts (F3: meshes
from `Resources` are allowed; F7: one shader, palette cells only). A missing or invalid kit piece logs
`Atlas: kit piece '<name>' missing or invalid; using the built-in one.` and the procedural mesh is built.

## Authoring (`art/blender/ATLAS_Kit.blend`, collection `ATLAS_Hero`)

- Objects `Hero_Landmark`, `Hero_Avatar`, metres, Blender axes (Z up). The exporter converts.
- Colour: material names `pal_<Slot>` where `<Slot>` is a constant of `AtlasPalette` (`Landmark`, `Post`, `Flag`,
  `AvatarBody`, `AvatarLeft`, `AvatarRight`, `Text`, ...). Unknown name: export fails.
- Shade: colour attribute `ao` (baked ambient occlusion, 0..1) → vertex shade byte. Missing: 1.0.
- Outlines: UV map `lines` → line coordinates; a line is drawn where it crosses an integer. Missing: no outlines.

## Export

- `tools/kit/kitglb.py`: pure Python, no `bpy`. GLB writer (POSITION, NORMAL VEC3 float; TEXCOORD_0, TEXCOORD_1
  VEC2 float; COLOR_0 VEC4 normalised unsigned byte; UNSIGNED_SHORT indices), glTF conventions (right-handed,
  counter-clockwise fronts, v down) so that `GlbMeshReader` turns them into Unity's frame; `palette_uv(i)` equal to
  `AtlasPalette.Uv`; per-piece budget checks; `manifest.json` writer (triangles, sha256 per piece).
- `tools/kit/export_kit.py`: Blender wrapper. Triangulates an evaluated copy, splits vertices per face corner,
  collects arrays, calls `kitglb`. Run: `blender -b art/blender/ATLAS_Kit.blend -P tools/kit/export_kit.py -- <repo>`
  or through the Blender MCP.
- Output: `ATLAS/Assets/Atlas/Spaces/Glued/Resources/AtlasKit/<piece>.glb.bytes` and `manifest.json`.

## Runtime

- `Atlas.Telemetry.KitSet` (`Telemetry/Assets/KitSet.cs`), engine-free, `FallbackSet` style:
  `KitSet.Load(Func<string, byte[]> read)`, `bool TryGet(string name, MeshData into, out string error)`.
  Uses `GlbMeshReader.TryRead` unchanged, then reads TEXCOORD_1 through `GlbFile.TryAccessor` into `Uv1`. Checks
  the manifest hash. Never throws.
- `GluedCellMesh.Build(sizeX, sizeZ, data, MeshData landmark = null)`: appends the kit landmark at `LandmarkBase`
  instead of the spiral. One mesh per cell, no extra draw.
- `GluedAvatarMesh.Build(data, MeshData kit = null)`: copies the kit avatar when given.
- `GluedSpace.BuildContent` loads the set once (`Resources.Load<TextAsset>("AtlasKit/<name>")`).
  `GluedBudget` measures the same path.

## Tests

- Python `tools/kit/test_kitglb.py`: round trip of encodings; x negated, v flipped, CCW winding; `palette_uv`
  against known `AtlasPalette.Uv` values; refusals (unknown material, over budget, open edge).
- C# engine-free: `KitSet_ReadsTheShippedPieces`, `KitSet_ReadsLineCoordinates`,
  `KitSet_RefusesBadInput_WithAReason`, `KitLandmark_IsChiral`, `KitLandmark_FitsTheSmallestCell`,
  `KitAvatar_KeepsItsHands`, `GluedBudget_WithKitPieces`; existing Glued tests unchanged (fallback path).
- C# `[Category("Unity")]`: `KitSet_LoadsFromResources`.
- By eye: Blender preview renders (with an x-mirrored landmark beside the original); after the compile blocker,
  rows 10 and 11 of the Unity-side brief.
