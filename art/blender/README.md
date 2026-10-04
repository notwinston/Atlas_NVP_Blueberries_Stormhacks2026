# ATLAS Blender sources

On this branch only the Small Planet world is here. The greybox room kit (`ATLAS_Kit.blend`, its FBX exports) and
Glued's hero assets (`hero_build.py`) live on other branches; their sections of this README come with them.

## Small Planet world (S4)

Source: `SmallPlanet_World.blend`, built by `smallplanet_build.py` (`exec(open(...).read()); build_all()`; it rebuilds from scratch). A low-poly grassland on the six {4,3} tiles: the room on Home, a post-and-rail fence round the equator (green home half, gold far half) with a gate on South and a stepping-stone path from the room to the stone circle, and one landmark plus a themed set of bold props per side tile (East standing stone and sunflowers, North balanced rocks and lavender rows, West pines and birches, South stone circle and red toadstools; haystacks and bales in the gold half). The near half has bushes, rocks, tufts and mixed flowers. The rock-cairn pole beacon with its lantern sits at the Beacon (antipode) tile centre.

**Planet size.** The room stays 3.4 m wall to wall (loop-fit lap square 2.4 m); the tile side is `PLANET_SCALE` × 2.4 m and R = 2s/π. The default `PLANET_SCALE = 1.5` gives s = 3.6 m, R = 2.292 m (`AUTHOR_S`, `AUTHOR_R`): the fence is 1.9 m beyond the walls and 1.2 m beyond the room corners. `build_all(planet_scale=1.0)` rebuilds the plan's loop-fit planet (s = 2.4 m, R = 1.528 m), where the room corners touch the fence. Trade-offs of the bigger planet: three right turns of side s no longer fit inside the room (the loop can't be closed on foot), curvature is weaker (0.44 rad per metre instead of 0.65), and Unity must use the same R as the meshes. The angular layout (equator at π/2, the 2.6 rad discard) is unchanged.

**Colours.** Every face is a cell of Unity's `AtlasPalette` (ported in the script). The face attribute `Pal` (int) holds the cell index, and `Col` holds the cell colour times a small brightness factor (per-face jitter plus a per-tile shade of 0.95–1.04, so tiles can be told apart). Only the lantern uses an emissive cell (193 Beacon, material `SP_Lantern`); everything else is `SP_VertexColor`.

**Geometry.** No prop face is wider than 0.25 m in plan, because the warp bends vertices, not faces. The ground grid grows with R (18 cells per side at the default). Ordinary props stay at or under 1.2 m; hero pines and the standing stone reach 1.38 m. Triangles at the default: Home 648 (ground) + 680 (room kerb, preview only and not exported), East 2,606, North 2,679, West 2,537, South 2,479, Beacon 1,987, about 13.6k in total (12.9k exported).

Tile meshes (collection `SmallPlanet_Tiles`) are in tile-local chart coordinates under layout empties (each empty carries `planet_R` and `tile_side` custom properties); export the children's local data. `SmallPlanet_Globe` (heights × 0.4) and `SmallPlanet_Walker` (the log-map view from the room centre, with fog) are previews only and are not exported. `render_previews(folder)` renders `previews/sp_walker_scale1p5.png` (headset view south over the gate), `sp_walker_level_scale1p5.png`, `sp_walker_east_scale1p5.png`, `sp_globe_scale1p5.png` and `sp_net_scale1p5.png`. `sp_walker_compare_scale1p0_vs_1p5.png` sets the loop-fit and default planets side by side.

**Export to Unity.** `tools/kit/export_planet.py` writes the six tiles as kit GLBs into
`ATLAS/Assets/Atlas/Spaces/SmallPlanet/Resources/PlanetKit/`; run it with background Blender on a snapshot copy of the
.blend. Conventions, the command and the wiring checklist: `ATLAS/Assets/Atlas/Spaces/SmallPlanet/PlanetKit.md`.
