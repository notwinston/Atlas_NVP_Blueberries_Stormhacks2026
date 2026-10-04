# PlanetKit: the Small Planet (S4) tiles modelled in Blender

`Resources/PlanetKit/` holds the six tiles of the Small Planet grassland world, modelled in
`art/blender/SmallPlanet_World.blend` (built by `art/blender/smallplanet_build.py`) and exported by
`tools/kit/export_planet.py`. `SmallPlanetView` draws them (through `PlanetKitTiles`); they are the only Small Planet
world: the procedural tiles and the pole beacon that came before them are gone, and there is no fallback. The wiring
checklist below records how. The sky over them is `SmallPlanetSky` (see "Sky" at the end).

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
  exactly as the procedural tiles were drawn.
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

## Wiring checklist (done on branch `smallplanet-wired`, 2026-10-04)

1. **Done.** `PlanetKitTiles.Load` reads the kit through `KitSet.Load` with the reader
   `name => Resources.Load<TextAsset>("PlanetKit/" + name)?.bytes` (`SmallPlanetView.ReadKitResource`; the asset is
   unloaded after its bytes are copied). `PlanetKitTiles.ResourceFolder` is "PlanetKit", not Glued's "AtlasKit".
   `SmallPlanetView.SharedKit()` loads it once per run and keeps the six parsed `MeshData` (and R0 from the
   manifest's `"planet"` block). The Small Planet assembly already referenced `Atlas.Telemetry`; no asmdef changed.
2. **Done.** `SmallPlanetView.Build` makes one `Mesh` per face from `PlanetKitTiles.Scale` (x and z times R, y kept,
   normals by the inverse transpose of the stretch R/R0; palette, shade and line channels as exported) through
   `AtlasMesh.Apply`. `SetRadius` rescales into one scratch builder sized for the largest tile and re-uploads into the
   same six meshes (no reads, no new arrays). Each face keeps its own pooled material and anchor. `Release` destroys
   the six meshes.
3. **Done; no fallback since branch `smallplanet-sky`.** Any refusal (missing file, hash mismatch, unreadable GLB or
   manifest, a triangle count that differs, not exactly six tiles, no `planet.R0`, a vertex beyond 1 R) makes
   `PlanetKitTiles.Load` return null with the reason; the view logs one error ("the Small Planet kit tiles are missing
   or invalid; the planet is not drawn") and draws no tiles (`Built` stays false, `SetRadius` and `Upload` do nothing).
   The space still enters: the walk, the lap, the dial, the room, the sky and the telemetry carry on.
4. **Done.** The procedural pole beacon (`PoleBeaconMesh`, `SmallPlanetCore.BeaconPlacement`, the beacon anchor) is
   deleted; the kit's cairn and lantern on tile5 "Beacon" are the landmark. Nothing else read the beacon (WalkEvents,
   the Station and the spectator never did). `SmallPlanetConstants.BeaconFace` (5) remains, naming the cairn's tile.
5. **Done.** `PlanetKitTests.PlanetKit_Budget` checks the kit against the exporter's 3,000 a tile. The procedural
   tests went with the procedural tiles; `SmallPlanetRenderTests` T5 (no smeared triangle beyond 2.6 rad) runs on the
   kit tiles, and T4 (seams) on the tile-chart edges themselves.
6. **Done.** Same test: 2 x (6 tiles + 2 sky renderers) = 16 draws for two eyes and 2 x (12,936 + the sky's 1,308)
   = 28,488 triangles, inside 70 and 190k.
7. **Done.** planet.R's default is now `SmallPlanetConstants.KitRadius` = 2.291831 m (this manifest's R0; a test
   checks the two agree), still a lower bound: R = max(room radius, planet.R). Setting planet.R to 0 gives the room's
   loop-fit planet again, with the props relatively taller. Later lowered to 2.0 m (`SmallPlanetCore.DefaultRadiusM`)
   for a smaller planet: the props shrink with it by about 13 %.
8. Unchanged: the committed `.meta` files are used.

Left over:

- **The default planet no longer closes the 3-corner lap on foot in a small room.** With planet.R = 2.0 m, any room
  whose loop-fit radius is smaller (the 3 x 3 m sample room: 1.27 m) walks the bigger planet, so three right turns
  of the room's lap side do not close the loop (see `art/blender/README.md`, "Planet size"). For the lap demo, set
  planet.R to 0 in the menu (or re-export at `PLANET_SCALE = 1.0`). The walk tests set planet.R = 0 explicitly.
- `planet.R`'s default is a constant, not read from the manifest at run time, so the phone, the Station and the menu
  agree before any kit is loaded; re-exporting at another scale means updating `SmallPlanetConstants.KitRadius`
  (`PlanetKitTests` fails until it matches). The default itself, `SmallPlanetCore.DefaultRadiusM`, is set apart.
- The ground lines (uv1 on the ground) were not compared on a device against the procedural tile edges.

## Sky (`SmallPlanetSky`, `SmallPlanetSkyMesh`)

- **Gradient:** the look's own background (`AtlasBackground.shader`, the skybox the eye cameras clear with). On Enter
  the space sets the fog colour, which is also the background at and below the horizon, to the Blender preview's sky
  `#BFE3F5` (`smallplanet_build.py`, `SKY`), and the zenith (`AtlasLook.SetSkyTop`) to palette cell 199 (`0x5AC8FA`).
  The grass therefore fades into the horizon seamlessly. SpaceLab resets both between spaces.
- **Sun and clouds:** two renderers (sun, clouds) sharing one pooled Atlas/Surface material: warp mode 0 (a plain
  transform, so the 2.6 rad discard of mode 2 never applies), fog weight 0, both faces drawn. The sun is a disc
  (cell 204, white) with a halo ring (cell 196, pale gold), 35 degrees up at the azimuth of the look's fixed light,
  52 m out. Eight low-poly cumulus clouds of 3 to 5 flat-shaded puffs, 40 m out and 12 to 44 degrees up: emissive white
  (cell 200) bodies, flat bases in cell 15 with the baked shade. 1,308 triangles in all.
- **Comfort:** the root follows the head's position each frame (`Upload`) with the world's rotation, so it is at
  infinity and never swims; only the clouds turn about the vertical, 0.2 degrees a second.
- **Clear colour:** the eye cameras' background colour (used only if the skybox were missing) is set to the horizon
  on Enter and given back on Exit.

## View switch: `planet.view3d` (the "Hyperbolica look")

- **Off (default): the comfortable view.** Warp mode 2, S2 x R: the floor is curved, heights are added flat, the far
  side never rises above the horizon; the daytime sky above. Exactly the behaviour described above.
- **On: full 3D spherical space.** Warp mode 3 (`AtlasWarp.hlsl`, `LogS3`; C# mirror `AtlasWarpCpu.LogS3`): the world is
  the round 3-space S3 of radius R and the floor is one of its great 2-spheres. A vertex at floor point p (unit,
  head-relative) and height h is q = (cos(h/R) p, sin(h/R)); the eye at height e is E = (0, 0, cos(e/R), sin(e/R));
  the vertex is drawn at the head plus R Log_E(q) (right = world x, forward = world z, up = world y). Every sight line
  meets the floor again: past a quarter turn (pi R / 2, about 3.6 m) the ground rises above the horizon, far things grow
  in every direction, and the foot's antipode hangs straight overhead (pi R - e away). There is no sky: the sun and
  clouds are hidden and fog, zenith and the eyes' clear colour are meadow green `#6E9F5A`
  (`SmallPlanetSkyMesh.MeadowSrgb`), so the far ground fades into it. In mode 3 the fog and the 2.6 rad discard use
  the 3D distance from the head (standing, the farthest floor is pi - e/R = 2.44 rad, under the discard).
- Tiles, the room's kerbs and furniture and the lap markers all switch (`SmallPlanetSpace.ApplyView`). Mode 3 is used by
  nothing else. Like planet.R, the switch takes effect only while the walker stands; it rides p[6] of the State, so
  the Station draws what the phone draws.
- **Strong by design:** R is about 2.3 m and the eye about 1.6 m up (e/R about 0.7 rad), so the curvature shows even close
  by: horizontal sizes at height h shrink by cos(h/R) (0.76 at eye height), so props taper upwards a little.
- Toggle: on the phone, menu > Parameters > group "planet" > `planet.view3d`; on the Hub, the same parameter in the
  planet group.
