# PlanetKit: the Small Planet (S4) tiles modelled in Blender

`Resources/PlanetKit/` holds the six tiles of the Small Planet grassland world, modelled in
`art/blender/SmallPlanet_World.blend` (built by `art/blender/smallplanet_build.py`) and exported by
`tools/kit/export_planet.py`. Nothing on this branch loads them yet; see the wiring checklist below.

(This note sits outside `Resources/` on purpose: everything inside a Resources folder ships in the player.)

## Files

| File | What it is |
|---|---|
| `tile0.glb.bytes` .. `tile5.glb.bytes` | one mesh per tile, in CubeTiling's face order: 0 Home, 1 East, 2 North, 3 West, 4 South, 5 Beacon. Written by `tools/kit/kitglb.py` (`write_glb`), so `GlbMeshReader` / `KitSet` read them exactly like Glued's kit pieces |
| `manifest.json` | KitSet's `{"pieces": {"tile<i>": {"triangles", "sha256"}}}`, plus a `"planet"` block (below) |

Each tile merges the Blender objects `Tile_<Name>_Ground`, `_Props` and `_Fence`. `Tile_Home_Props` (the
placeholder room kerb) is left out: Unity draws the real room on Home.

Export of 2026-10-04 (PLANET_SCALE 1.5, tile side 3.6 m, R0 = 2.2918 m, ground grid 18):

| Tile | Triangles | Vertices |
|---|---|---|
| tile0 Home | 648 | 1,934 |
| tile1 East | 2,606 | 6,575 |
| tile2 North | 2,679 | 7,609 |
| tile3 West | 2,537 | 6,869 |
| tile4 South | 2,479 | 6,696 |
| tile5 Beacon | 1,987 | 5,701 |
| all six | 12,936 | |

## Coordinate convention

- **Already in Unity's tile charts.** Vertex xz is the chart of CubeTiling face *i* that the warp shader takes in
  mode 2 (`AtlasWarp.hlsl`: `positionOS.xz` = chart, `positionOS.y` = height). Blender authors the four side
  tiles a quarter turn from Unity (Blender +Y points to Home, Unity +X does), so the exporter has turned them:
  Unity chart = (y_Blender, -x_Blender). Home and Beacon agree as they are. Draw tile *i* with tile *i*'s anchor,
  exactly as the procedural tiles are drawn.
- **xz in units of R0, y in metres.** Multiply x and z by the runtime radius R (the planet.R setting) before
  uploading; leave y. The tile edge is at |x| or |z| = pi/4 (0.7854) on the axes; a few props overhang the edge
  by up to 0.06 R (they stand on the neighbour's ground, which is fine). Heights run from -0.12 m (sunk rocks)
  to 1.38 m.
- Unity's left-handed frame, clockwise fronts (as GlbMeshReader hands them back). Normals are the metric
  normals at R = R0 (the shader does not light by them).
- Vertex data is the look's (`AtlasVertex`): uv0 = palette cell centre (one emissive cell, 193 Beacon, on the
  cairn's lantern), COLOR.r = baked shade (AtlasShading.FaceShade of the face normal times the face's authored
  brightness), COLOR.g = 255, uv1 = line coordinates: on the ground 0..1 across the tile in the gnomonic square,
  as TileMeshGenerator's floor, so with `TileRenderer.EdgeLineStrength` the lines fall on the tile edges; on
  props a constant (0.5, 0.5), so no lines.

## manifest.json, "planet" block

| Field | Meaning |
|---|---|
| `R0` | radius (m) the tiles were authored at; xz is stored divided by it |
| `xz_unit`, `y_unit` | "R0 ..." and "metres" |
| `frame` | the chart convention above |
| `tiles` | names of tile0..tile5 (CubeTiling face order) |
| `half_width_R` | pi/4: the tile's half-width along a chart axis, in units of R |
| `ground_grid` | ground cells per tile side in the export |
| `source` | the .blend it came from |

KitSet reads only `"pieces"`; the extra block does not disturb it.

## Re-exporting

Never on a .blend that an interactive Blender has open; snapshot it first. From the repository root:

```
cp art/blender/SmallPlanet_World.blend /tmp/sp_snapshot.blend
"C:/Program Files/Blender Foundation/Blender 5.1/blender.exe" -b /tmp/sp_snapshot.blend --python-exit-code 1 \
    -P tools/kit/export_planet.py -- . --stats /tmp/sp_stats.json
python -m pytest tools/kit -q
```

R0 comes from the scene's custom property `SmallPlanet_R` (else `planet_R` on the `Tile_<Name>` empties) and is
checked against the ground sheets; `--radius R0` overrides it. Each face's palette cell comes from its `Pal`
attribute (else the nearest cell to `Col` in OKLab). Nothing is written unless all six tiles pass
(≤ 3,000 triangles a tile, ≤ 65,535 vertices, xz within 1 R). Files whose bytes do not change are left alone,
so a re-export of the same .blend changes nothing. Missing `.meta` files are written with the GUIDs
`tools/gen_meta.py` gives (md5 of `atlas:` + the path under ATLAS/).

## Wiring checklist (for whoever merges this with the Small Planet space code)

1. `SmallPlanetView.Build` loads the kit with
   `KitSet.Load(name => Resources.Load<TextAsset>("PlanetKit/" + name)?.bytes)` (KitSet asks for `manifest` and
   `tile<i>.glb`; Resources drops the `.bytes`). Do not use `KitSet.ResourceFolder` ("AtlasKit"): that is
   Glued's folder, and Resources paths are global.
2. Build six meshes, one per face (today one shared `tileMesh` is drawn six times): `TryGet("tile" + f, ...)`,
   multiply every vertex's x and z by R, upload through `AtlasMesh`. Keep the source `MeshData` so `SetRadius`
   can rescale and re-upload without reading the files again. Each tile keeps its own pooled material and anchor
   as now.
3. If the manifest or any tile is refused (missing, hash mismatch, unreadable), log `KitSet.LastError` once and
   fall back to the procedural `TileMeshGenerator` tiles for all six, so the planet never mixes the two.
4. Drop the procedural pole beacon (`PoleBeaconMesh`, placed by `SmallPlanetCore.BeaconPlacement` at a corner of
   face 5) when the kit is in use: the rock cairn with its lantern stands at the Beacon tile's centre. Keep it
   on the fallback path. T7 in `SmallPlanetRenderTests` checks the beacon's corner placement; adapt it to the
   path that is taken.
5. Raise the 1,200-triangle-per-tile cap in `SmallPlanetRenderTests.SmallPlanet_Budget` (T10, around
   lines 269-279; it uses `TileChart.TriangleCap(Near)`'s 1,200) for the kit path: tiles reach 2,679 now,
   and the exporter's own ceiling is 3,000.
6. Check the S4 budget (Master Plan 8.5: ≤ 70 draw calls, ≤ 190k triangles, both eyes): six tile draws an eye
   (12 for two, the beacon gone) and 2 x 12,936 ≈ 25.9k planet triangles, before the room, the avatar and the
   Hub.
7. R must be the planet's actual radius. The tiles were authored at R0 = 2.2918 m (PLANET_SCALE 1.5); at other
   radii the layout scales with R but props keep their metric heights, so they look relatively taller on a
   smaller planet (R = 1.528 m, the loop-fit planet, is 0.67 R0).
8. On first import Unity reads the committed `.meta` files; it generates none for this folder.
