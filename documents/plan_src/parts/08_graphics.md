---

## 8. Graphics and performance

The aim is a picture that reads clearly in both eyes at 60 frames per second for a whole session, on a phone with no fan. The Build Pipeline sets the method; the performance research (R5) checked its budgets against the hardware and corrected three numbers. Both are folded in here.

### 8.1 The look: a lit architectural diagram

Colour and line define form, not textures (BP §2). This is also the cheapest thing to draw in stereo.

| Rule | Detail |
|---|---|
| One palette | A single 128² texture of flat colour blocks and a few gradient strips. Compression off, mipmaps off |
| Other textures | Any image texture or UI sprite: ASTC 6×6 (4×4 for UI), mipmaps on, at most 1024 (BP §5). Data textures (sky clearance maps, lens lookups): uncompressed, no mipmaps, clamped, sRGB off. TextMeshPro atlases keep their defaults |
| One material | One shader for every opaque surface, so the SRP Batcher works and draw submission stays cheap |
| Shading baked into vertices | Ambient occlusion in vertex colours. No lightmaps (they do not follow warped or copied geometry), no real-time shadows |
| Lines in the shader | Tile edges and a 0.5 m floor grid drawn with `fwidth`, anti-aliased and stable in both eyes. They show the geometry directly |
| Fog in the room's hue | Lightness steps down with distance or copy depth, so repetition reads and the edge of the drawn world disappears |
| Background | Both eye cameras clear to a two-stop vertical gradient made in the shader, with no texture (BP §3c). At and below the horizon its colour equals the global fog colour, and every surface fades to that one colour, so geometry at the fog radius and beyond cannot be told from the background. The region hue tints surfaces and lines; the fog colour is the hue of the region the walker stands in and cross-fades over one second when the region changes |
| Emissive accents | Portals, cross-sections, beacons. No bloom |
| Dark base palette | The display is OLED: dark pixels cost less power, which delays thermal throttling |
| Outlines | Inverted-hull outlines only on a few hero objects (portal frames, the slicer) |
| Transparency | Sparingly. Dithered, or a rim term, instead of sorted blending |
| Text | TextMeshPro for every label and sign |

### 8.2 Render set-up

| Setting | Value | Why |
|---|---|---|
| Pipeline | URP, forward, Render Graph | (BP §4) |
| Cameras | Two eye cameras, ±32 mm, one shared renderer, no camera stacking, no AR camera background | (BP §0, §1) |
| Target | One render texture per eye, 1118 × 1032 (0.8 × the 1398 × 1290 half-screen), 4× MSAA. Multisampled colour and depth are memoryless; only the resolved colour is stored. Each eye camera targets its own texture with a full viewport. The thermal ladder swaps between texture sets allocated once; an MSAA change outside the ladder (`render.msaa`) re-creates the eye textures | Fill rate is not the limit on this GPU; line art needs the resolution (R5). With a target of its own, each eye's pass resolves in place and no multisampled surface is stored or reloaded |
| Render scale | URP Render Scale stays 1.0; the size of the eye textures carries the render scale. Texture sets for 0.8, 0.7 and 0.6, each with the MSAA level of its rung, are allocated once, and the thermal ladder swaps between them (§8.6) | A change of rung allocates nothing |
| Final pass | One full-screen blit to the screen that samples both eye textures: per-eye barrel pre-distortion for the lenses, combined with the upscale | The only full-screen pass in the app |
| Off | HDR, depth texture, opaque texture, depth priming, post-processing, FXAA, STP, shadows, additional lights, GPU Resident Drawer, baked occlusion culling, static batching of warped meshes | (BP §4) |
| On | SRP Batcher, native render pass | |
| Frame rate | `targetFrameRate = 60`; ProMotion off; Unity's thermal frame-rate adjustment off (needs Unity ≥ 6000.3.21f1) | Unity otherwise forces 30 fps at thermal state Serious (R5) |
| ARKit | Lowest-resolution 60 Hz video format. Environment depth stays on, through `AROcclusionManager`: mode Fastest, temporal smoothing off, human stencil and human depth off, read only with `TryAcquireEnvironmentDepthCpuImage` (256 × 192). Meshing, planes, light estimation and probes off after the scan | Heat. Bump Guard needs only the small depth image, read on the CPU (§4.10) |

A check that must be made once with an Xcode GPU capture (task `P1-A05`): for each eye, the opaque pass, the portal stencil passes and the tile passes are merged into one native pass; that pass resolves its colour (store action MultisampleResolve) and discards depth and stencil; no pass loads a multisampled attachment; and there is exactly one blit to the screen. If a pass break forces the MSAA surface to be stored and reloaded, a 10 % cost becomes several milliseconds.

### 8.3 One shader, four includes

| File | Owner | Does |
|---|---|---|
| `AtlasCore.hlsl` | A | Palette lookup, vertex ambient occlusion, grid and edge lines, fog, the single per-material constant block |
| `AtlasWarp.hlsl` | B | One entry point: object position in, world position out. Modes: none, hyperbolic, spherical (log map centred on the head) |
| `AtlasPortalClip.hlsl` | A | Clip against a portal plane |
| `AtlasGates.hlsl` | B | Per-pixel clip against up to four sight-line gates (Aligned Cells) |

Each include is edited only by its owning lane. The entry points, the per-material block and the property ids in `AtlasShaderIDs.cs` are a contract frozen at G0 (§6.4).

Rules that keep it fast:

- Per-tile or per-room data (the anchor matrix) lives in the per-material block, one pooled material instance per tile or room. Camera-wide data (head position, L, fog) are globals. **No MaterialPropertyBlocks.**
- No new shader keyword without telling lane A. Prefer a per-material number or a global mode.
- The warp evaluates the exponential once per vertex and derives sinh and cosh from it. Positions stay in full `float`.
- Portal rooms are flat and curved spaces have no portals, so the clip and the warp never meet in one material.
- Warped renderers get huge `localBounds`. Unity then culls nothing. Wherever the hyperbolic warp is on, the CPU draw rule of §8.5 (a distance test and a view test, once per frame) is therefore the only culling, and it is mandatory. The Small Planet always draws its six tiles.

A note on the reference code: HyperEngine's own shader passes its per-object matrix through a MaterialPropertyBlock (R3). The port must move that matrix into the per-material block; it is not a copy-and-paste.

### 8.4 Geometry and asset budgets

| Asset | Made by | How | Triangles |
|---|---|---|---|
| Tile shell, three levels of detail | Lane B | Generated in C# at run time (§3.4). Levels by grid count: n = 16 per side on the walker's current tile and the 12 tiles that touch it; n = 8 on every other drawn tile; n = 4 only on the high tier, beyond ρ = 3. Every level keeps the same 16 boundary segments per edge, so neighbours of different levels share their edge vertices exactly | ≤ 1,200 · ≤ 500 · ≤ 250 for the three levels, at any L |
| Home shell (floor, walls) | Lane C | Generated from the RoomModel on a 0.25 m grid | ≤ 3,000 |
| Furniture | Lane D | Forge: photo to mesh, then clean-up to the triangle target sent with the job (§4.5). At most 20 objects receive generated meshes, largest boxes first; any further objects stay placeholder boxes | Target per object: clamp(60,000 ÷ n, 3,000, 5,000), n being the number of objects that receive meshes, so the room's generated furniture never exceeds 60,000. The asset validator accepts 1,500–5,000; longest edge ≤ 0.25 m |
| Built-in fallback furniture | Lane D | One CC0 mesh per RoomPlan category, shipped in the build; replaces the placeholder boxes when the Forge is unreachable (task `P1-D12`) | ≤ 1,500 each; no edge over 0.25 m after scaling to the box |
| Glued cell | Lane A | An open frame cut from the free floor: corner posts, a floor grid drawn in the shader, one chiral landmark. Repeated 5 × 5 (T², Klein bottle) or 3 × 3 × 3 (T³); every copy except the walker's own carries a self-avatar. The room itself is not repeated: its walls sink to kerbs and its objects are drawn once, where they really are (§4.9) | ≤ 1,500 per cell |
| Impossible Rooms | Lane A | Parametric from the free rectangle (ProBuilder or C#) | ≤ 2,000 per room |
| Landmarks, spans, garden props, avatar, door frame | Lanes A and B | Blender, CC0 kits (Kenney, Quaternius, KayKit) recoloured to the palette | Hero 5,000–15,000 · small 300–1,500 · self-avatar ≤ 600 |
| Polytopes and tubes | Lane A | Generated | Tesseract 48 tetrahedra; tube 18,000 |

Tile levels are set by grid count, not by edge length, and are accepted by error: the worst screen-space chord error is at most 0.15° from an eye height of 1.6 m at L = 1.4, 1.9 and 3.0 m (task `P1-B05`). The 0.25 m longest-edge rule applies to the Home shell, furniture and authored props.

Authored meshes (BP §2, §3a): export FBX, metric, scale 1.0, transforms applied, Apply Scalings = FBX Units Scale, Forward −Z, Up Y. Pivot at the centre of the base for props and at a floor corner for flat rooms. Ambient occlusion is baked to the colour attribute in Blender (Cycles, or the VertexOven add-on). Anything placed in a curved space is subdivided so that no edge is longer than 0.25 m.

Licence rules: Asset Store packs never enter the repository; CC-BY assets and any hosted AI output are credited; Hunyuan3D stays off the default path (territory limits); TripoSG is MIT.

### 8.5 Budgets

**Per frame, both eyes (must):** at most 300 draw calls, 500,000 triangles, 60 set-pass calls, 1.5 GB of memory and 1 kB of managed allocation per frame (§11.2). The aim in steady state is no managed allocation at all.

**Per space (per frame, both eyes; planning figures, to be replaced by values measured on `budget_room` at G4):**

| Space | Draw calls per frame | Triangles per frame | What drives it |
|---|---|---|---|
| S0 Home | ≤ 60 | ≤ 130k | Up to 20 furniture meshes, 60,000 triangles in total |
| S1 Glued, 5 × 5 (T², Klein) | ≤ 150 | ≤ 250k | 25 cell frames, 24 avatars, the room drawn once |
| S1 Glued, T³ 3 × 3 × 3 | ≤ 160 | ≤ 250k | 27 cell frames, 26 avatars, the room drawn once |
| S2 Curved Walk, ρ = 3 | ≤ 210 | ≤ 250k | Up to 71 tiles drawn, plus the room |
| S3 Aligned Cells | ≤ 170 | ≤ 200k | Up to 57 cell instances; furniture drawn once |
| S4 Small Planet | ≤ 70 | ≤ 190k | Six tiles plus the room |
| S5 Impossible Rooms | ≤ 60 | ≤ 150k | Two rooms through one portal, plus the room's content (Home shell and furniture, 63,000 per eye) |
| S6 Slicer | ≤ 70 | ≤ 160k | Home plus the dynamic cross-section |

`budget_room` is the performance fixture frozen with the contracts at G0 (§4.3, task `P1-X02`): a 4 × 5 m room with twelve stand-in objects of exactly 5,000 triangles each and no edge over 0.25 m, which is the full furniture load of §8.4 (60,000 triangles). Budgets are measured on it; the three sample rooms stay for functional tests. In Glued the room is drawn once, among the copies of the cell. In Aligned Cells an instance is one draw call (the flat tile shell and its props merged), and each piece of furniture is drawn once per eye, however many instances of its cell are drawn (§4.7).

![Tile counts against fog radius](figures/fig05_tile_budget.png)

The figure shows why the fog radius ρ is the main performance control in hyperbolic space: the number of tiles grows exponentially with distance. It plots three counts per eye against ρ: the tile centres within ρ of the walker, the tiles that reach inside ρ, and the tiles drawn after the view test below. Counted from a tile centre, 45 tile centres lie within ρ = 3, 129 within ρ = 4 and 373 within ρ = 5. The Build Pipeline's "fog at 5–8 units" is the numerical precision horizon, not a drawable distance; the working rule is **fog fully opaque at ρ = 3; every tile that reaches inside that radius and can be in view is drawn**.

Fog starts at ρ − 1 and is fully opaque at ρ; ρ = 3 by default, so the fog runs from 2 to 3. ρ is the registry parameter `curved.cullRho`; the fog and the draw rule both follow it. Culling tiles by their centres at ρ would leave holes, because a tile whose centre lies just outside ρ still reaches up to 0.8425 inside it, where the fog is not yet opaque. The draw rule is therefore:

```text
d = hyperbolic distance from the walker to the tile's centre
c = position of the tile's centre on the floor, in metres from the head: L·d along its bearing
draw the tile when both hold:
  (a) d ≤ ρ + 0.8425                              # it can reach inside the fog radius
  (b) the disc of radius 0.8425·L round c is not wholly outside any of the four side planes
      of the view frustum, widened by 12° to each side and by 4° up and down
      (a disc of radius r round c is wholly outside a plane with inward normal n
       when  n·c + r·|n_floor| < 0, n_floor being the part of n that lies in the floor)
```

0.8425 is the circumradius of a {4,5} tile. The log map never stretches a length, so the whole tile lies inside the disc of radius 0.8425·L round the image of its centre; the test is therefore safe at any head pitch, including straight down. The view is about 50° to each side and 48° up and down; the extra 12° and 4° cover a fast head turn between the test and the display, and the optional rotation prediction of §8.7. The test runs once per frame on the CPU, for both eyes. A simulation over walker positions, headings, pitches from 20° up to straight down and L from 1.4 to 2.8 m found no floor point that is in view, nearer than ρ and in an undrawn tile (`research/scripts/tile_draw_rule.py`). Task `P1-B06` repeats the check against the camera frustums on a recorded lap.

Counts per eye (derived by enumeration, same script):

| Fog radius ρ | Tile centres within ρ (walker at a tile centre) | Tiles that reach inside ρ | Tiles drawn, gaze level | Tiles drawn, any pitch | Shell draw calls per frame, worst case |
|---|---|---|---|---|---|
| 2 (Serious rung) | 13 | 34 to 43 | at most 26 | at most 38 | 76 |
| 2.5 (fallback step, §12.2) | 25 | 62 to 73 | at most 39 | at most 52 | 104 |
| 3 (default) | 45 (40 to 50 elsewhere in the tile) | 104 to 119 | at most 59 | at most 71 | 142 |
| 3.5 (high tier) | 81 | 179 to 197 | at most 94 | at most 106 | 212 |

Looking down draws more tiles than looking ahead, because the floor near the feet fills the view. The figure marks the level-gaze worst cases. A tile shell is one draw call per eye, so the default costs at most 142 draw calls per frame, inside the 150 budgeted for tile shells (the dashed line in the figure, drawn at 75 per eye because the figure counts per eye; task `P1-B06`). The high tier can reach 212, over that budget: it is only for a cool phone and for video capture (§8.6). It is still below the solid line, the must of 300 draw calls per frame (150 per eye, as the figure draws it) for everything that is drawn.

In Glued the fog is fully opaque at the distance up to which every cell is certain to be drawn: (n − 1)/2 cells for an n × n lattice round the walker's current cell, so 2 cells at 5 × 5 and 1 cell at 3 × 3. It starts one cell nearer (half a cell at 3 × 3). The end of the lattice is therefore never seen. The vertical period of T³ is not fogged, so the copy above the walker stays visible.

If the view test is not built, the fallback is to cull tile centres at ρ = 3 and make the fog opaque at ρ = 2.15, so that every tile that reaches inside the fog is still drawn.

**Frame time at 60 fps (16.7 ms), cold targets (R5):**

| Stage | Thread | Target | Cap |
|---|---|---|---|
| ARKit update with live depth, pose, input | main | 1.5 ms | 2.5 |
| Bump Guard, kernel, visible set, slicer, space logic | main | 0.8 ms | 1.5 |
| Link, telemetry, HUD | main | 0.2 ms | 0.5 |
| URP set-up and culling for two cameras, engine overhead | main | 1.8 ms | 3.0 |
| **Main thread** | | **4.3 ms** | **7.5** |
| Draw submission (≤ 300 draws), encode, present | render | 3.0 ms | 5.0 |
| Opaque, warp, fog, portals, resolve, lens blit, both eyes | GPU | 4.6 ms | 7.6 |

The ARKit row is R5's figure plus the live depth image: 0.5 ms of its target and 1.0 ms of its cap, the "should" and "must" values of the depth-cost row in §11.2. The GPU can lose 30–50 % of its speed when hot, which is why the cold GPU target is under 5 ms.

### 8.6 Heat

The plan is never to reach thermal state Serious during a session. Levers, in order of benefit for effort (R5):

1. Dark palette and dark fog.
2. Screen brightness fixed near 40 %.
3. Lowest-resolution 60 Hz ARKit format; environment depth in its cheapest mode (§8.2); meshing and planes off after the scan.
4. No charging while running; case off; phone out of the headset and in front of a fan between sessions.
5. The quality ladder below.
6. Two phones: swap between sessions at Fair, during a session at Serious.
7. Hand tracking, if it is ever added, is on only in a space that uses it (BP §0.5).

| Trigger | Eye-texture set (render scale) | MSAA | Fog radius ρ | Glued copies | Note |
|---|---|---|---|---|---|
| Nominal | 0.8 | 4× | 3 | 5 × 5 | |
| Fair, or GPU p95 over 9 ms | 0.7 | 4× | 3 | 5 × 5 | Console shows amber; swap at the next change of walker |
| Serious, or GPU p95 over 12 ms | 0.6 | 2× | 2 | 3 × 3 | Console says "swap phone"; still 60 fps |
| Still missing 60 fps after 10 s, or Critical | 0.6 | off | 2 | 3 × 3 | Lock to 30 fps and end the session |

The ladder moves one step at a time, at most every 20 seconds. A step in render scale is a swap to another eye-texture set (§8.2); the sets are allocated once, so a step allocates nothing. ρ is the fog radius of §8.5, and the draw rule follows it. A "high" tier (ρ = 3.5, 7 × 7 copies) exists for a cool phone and for video capture.

One cost is accepted rather than avoided: LiDAR depth stays on for Bump Guard whenever someone walks. The cost has two parts, and each is measured.

- **Frame time.** Reading and processing the depth image is budgeted inside the ARKit row of §8.5. It is measured at G5 with the probe, depth on against depth off; the must is a rise of at most 1.0 ms in frame-time p95 (§11.2).
- **Heat.** The LiDAR and ARKit's depth processing warm the phone. Task `P1-A14` measures this at G4: the 15-minute soak is run with depth on, then once more with depth off, driven by a recorded pose track with nobody in the headset, and the difference in time-to-Fair is recorded.

Processing fewer depth frames saves CPU time only; it does not reduce the LiDAR's heat. (The processing rate is a constant in code and never goes below 15 Hz, §4.10.) So if the heat cost proves too high, the lever is to start the ladder one rung lower. Depth is never switched off while someone walks.

### 8.7 Latency

ARKit reports the pose of the moment the camera frame was captured, not a prediction. The expected motion-to-photon time on this stack is 55–95 ms (R5, an estimate until measured). Three decisions follow:

- Keep `TrackedPoseDriver` on Update only. One pose sample per frame feeds the kernel and both cameras, so the virtual and real motion stay consistent. Sampling again before render would gain about 1 ms and add judder.
- Gyro rotation prediction is the one cheap improvement: rotate the rig by the gyro rate times 30–60 ms after the kernel has used the unpredicted pose. It ships behind a switch and stays only if the slow-motion measurement improves and three people see no jitter.
- Measure it once (240 fps video of the phone and its screen, §11.4) and publish the number.

### 8.8 Measuring

A single component, `AtlasPerfProbe`, samples frame timings and render counters every frame, keeps one minute of history and, once per second, computes percentiles and sends one telemetry row to the Station. The row is also appended to a CSV on the phone, so a lost link loses nothing. A HUD in the headset shows the same numbers. §11.2 lists every metric with its must, should and stretch value; §11.4 gives the measurement protocols. Published numbers come from a Release build, in the headset, after the soak.

### 8.9 Where to spend optimisation effort

In order of expected gain:

1. Frame-rate settings: `targetFrameRate`, thermal adjustment off, Release configuration, debugger detached.
2. The URP switches of §8.2.
3. The ARKit configuration and switching managers off after the scan.
4. One mesh, and so one draw call per eye, for each tile shell, room shell or Glued cell; one shader; pooled materials.
5. The draw rule of §8.5: fog opaque at ρ = 3, and only the tiles that reach inside that radius and can be in view are drawn.
6. Tile levels of detail by graph distance: the finest grid (n = 16) only on the walker's current tile and the 12 tiles that touch it, n = 8 on every other drawn tile (§8.4).
7. The MSAA pass-merge check.
8. Zero allocations per frame: pooled materials, preallocated slicer buffers, no string building in Update.
9. Dark palette and brightness.
10. The thermal ladder.
11. Decimating every generated mesh to its triangle target in the Forge, so that the room's generated furniture never exceeds 60,000 triangles (§8.4).

Not worth effort: baked occlusion culling (broken by the warp), static batching, the GPU Resident Drawer, a compute-shader slicer, single-camera stereo. Stretch only: a hidden-area mask for the lens circle, instanced tile shells, 30 fps rendering with reprojection.

Profiling tools, in the order to reach for them: the HUD and the CSV; the Metal Performance HUD for an independent reading; the Unity Profiler over USB for a CPU problem; the Frame Debugger for batch breaks; the Render Graph Viewer for pass structure; an Xcode GPU capture for a GPU problem.

### 8.10 Sound

Cheap polish (BP §3c), done in Phase 4: one ambient loop per space, a chime at portals pitched by depth, a tick at tile edges and a rising tone as the 4D cut moves. The Bump Guard tick and tone are not polish: they are built in Phase 1 with the warning cue (task `P1-C14`), and Phase 4 only does their final mix. Plain Unity audio; sources are CC0 packs.
