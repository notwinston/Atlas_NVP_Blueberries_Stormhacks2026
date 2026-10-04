---

## 3. The spaces we generate

Phase 1 delivers seven spaces, S0 to S6. They share one geometry kernel, one shader and one host scene (SpaceLab), and every one of them is generated from the scanned room rather than authored for a fixed stage.

Two layers exist at all times and must not be confused:

- **The world layer** is everything the walker sees. It is generated, and it may be flat, curved, glued or four-dimensional.
- **The physical layer** is the scan: where the real floor, walls and furniture are, in ARKit's tracking coordinates. Only Bump Guard reads it. It is never drawn.

In a flat space the two layers coincide, so what you see is where things are. In a curved space they cannot coincide (§4.6).

### 3.1 Catalogue

| Id | Space | Geometry | What is generated, and from what | Matches the real room? | The "aha" | Flat twin |
|---|---|---|---|---|---|---|
| **S0** | Home | Flat | Floor, walls and door openings rebuilt from the scan on a 0.25 m grid; one AI-generated mesh per scanned object, for at most 20 objects, largest first (§4.5) | Yes, 1:1 | "This is my room." It is the calibration and the place every journey starts and ends | Floor plan |
| **S1** | Glued | Flat, wrapped: torus T², Klein bottle, 3-torus T³ | One open-frame cell cut from the free floor (corner posts, floor grid, one chiral landmark); copies of it laid out as a lattice fixed to the floor (5 × 5 around the walker for T² and the Klein bottle, 3 × 3 × 3 for T³); mirrored rows for the Klein bottle; a self-avatar in every copy but the walker's own. The room itself is not repeated: its walls sink to kerbs and its furniture is drawn once, where it really is | Yes | Look ahead and see your own back. Walk east and re-enter from the west. In the Klein bottle your copy raises the other hand | Lattice plan with the fundamental cell |
| **S2** | Curved Walk (Mode 1) | H² × E: hyperbolic floor, ordinary height | {4,5} tiling generated lazily by exact tile id; tile zero carries the room; other tiles get an archetype and props from a hash of their id | Only at the start | Five rooms meet at every corner. A closed square on the floor is not closed in the world. There is far more room than there should be | Poincaré disc map |
| **S3** | Aligned Cells (Mode 2) | The same tiling, built from flat pieces | The free floor cut into square cells; each cell shows one flat tile; which tile depends on the path walked | Yes | Walk once round a corner post, through its four cells, and you are standing in a fifth room. All the curvature sits at the corners | The same disc map |
| **S4** | Small Planet | S² × E: spherical floor, ordinary height | {4,3} tiling: six tiles, the faces of a cube blown onto a sphere; all always loaded | Only at the start | Three right-angle turns make a closed triangle. The world is finite: six rooms, and the map counts them | Unfolded cube net |
| **S5** | Impossible Rooms | Flat rooms joined by portals | Rooms sized from the free rectangle and stitched by stencil portals: a booth that contains a hall; a corridor with four right turns that arrives somewhere new | Yes, each room | The building is bigger than the room | Room graph |
| **S6** | Slicer | Flat ℝ⁴, shown as 3D slices and 3D shadows; and ℝ³ × S¹ | Tetrahedral meshes of 4D polytopes (5-cell, tesseract, 16-cell, 24-cell) and periodic tubes, cut by a hyperplane every frame | Yes (the object floats in the room) | A solid changes shape as it moves in a direction you cannot point to | Slice film strip and the shadow |

The challenge's four families map onto these directly: hyperbolic (S2, S3), spherical (S4), 4D projections (S6), impossible and Escher-like spaces (S1, S5).

### 3.2 The dial: how many rooms meet at a corner?

One question drives the curved spaces. Take the lap square, the largest square the walker can comfortably lap inside the scanned room (side s, §4.4). Tile the world with four-sided rooms that measure s across the middle, so that their centres are s apart. How many of them meet at each corner?

![The curvature dial counts rooms per corner](figures/fig06_rooms_per_corner.png)

| Dial q | Space | Curvature radius from the lap square | A square lap closes after | World |
|---|---|---|---|---|
| 3 | Small Planet (S4) | R = 2s / π | 3 corners | Finite: six rooms |
| 4 | Flat (S0, and the cover of S1) | any | 4 corners | The ordinary plane |
| 5 | Hyperbolic (S2, S3), the default | L = s / 1.0613 | 5 corners | Endless |
| 6 | Hyperbolic, stronger | L = s / 1.3170 | 6 corners | Endless, grows faster |

Where the numbers come from (derived, checked numerically; formulas in Appendix A):

- In a regular tiling with four-sided tiles and q tiles per corner, the distance between neighbouring tile centres is 2·asinh(√cos(2π/q)) in units of L. For q = 5 that is 1.0613, and its hyperbolic cosine is exactly the golden ratio φ.
- A walker who goes from one tile centre to the next and turns left 90° each time circles one corner post and passes through the q tiles around it. So a square lap of side s closes after exactly q corners when s equals the centre spacing. Setting them equal gives the table.
- On the sphere the same argument gives the triangle with three right angles, whose sides are a quarter of the circumference.

Typical values (derived):

| Real room (wall to wall, empty) | Lap square s | L at q = 5 | R at q = 3 | L at q = 6 | Tile edge at q = 5 | Fog radius (ρ = 3) |
|---|---|---|---|---|---|---|
| 2.5 × 2.5 m | 1.5 m | 1.41 m | 0.95 m | 1.14 m | 1.77 m | 4.2 m |
| 3 × 3 m | 2.0 m | 1.88 m | 1.27 m | 1.52 m | 2.36 m | 5.7 m |
| 3 × 4 m | 2.0 m | 1.88 m | 1.27 m | 1.52 m | 2.36 m | 5.7 m |
| 4 × 5 m | 3.0 m | 2.83 m | 1.91 m | 2.28 m | 3.54 m | 8.5 m |
| 6 × 8 m | 3.2 m (cap) | 3.02 m | 2.04 m | 2.43 m | 3.78 m | 9.0 m |

The table is for empty rectangular rooms. Furniture, or a room that is not a rectangle, makes the free rectangle smaller, and the lap square follows the free rectangle (§4.4).

A bigger room gives gentler curvature for the same effect. That is a direct benefit of dropping the fixed square: the old plan's 1 m cells forced a radius near 0.9 m, which its own authors called strong and disorienting (Brief §3.7).

### 3.3 The 3-4-5 walk

![One instruction, three universes](figures/fig01_345_corner_walk.png)

The instruction is the same in every space: *walk s, turn left 90°, repeat.* The guide is a beacon s metres straight ahead and, when you reach it, the next one to your left. A flag marks where you started. The beacons live in the world, not on the real floor.

- At q = 4 the flag is back under your feet after four corners.
- At q = 3 it is there after three.
- At q = 5 you return to where you physically started after four corners, but the flag is one more side away: you need a fifth.

What one physical lap does in hyperbolic space at the loop-fit curvature (derived, verified to 1e-12):

- You end displaced by exactly s, one tile spacing, from where you began, and the world has turned a quarter-turn (90°) round you.
- Equivalently, the whole world has turned 72° about the centre of the five-sided loop. In the room that centre lies 0.092·L from the room centre (0.17 m at L = 1.88 m); it is a corner post only when the loop runs through tile centres, as Figure 2 below draws it. Five laps bring everything back.

For smaller loops the turn per lap is smaller and approaches area ÷ L² (a 1 m square at L = 1.88 m turns the world by about 16°). This is curvature measured with your feet, and the read-out shows it live: corners walked, distance from the flag, and the turn accumulated.

![The {4,5} tiling with the five-corner loop](figures/fig02_tiling_45.png)

Figure 2 draws the loop through tile centres, so that it circles one corner post. In the room the same loop is walked on the lap square, whose corners lie 0.17 m inside tile zero's corner posts (at L = 1.88 m).

### 3.4 How the curved worlds are built

**Tiles are generated in code, not modelled.** The curvature radius depends on the scanned room, so tile meshes cannot be authored for one fixed L. The generator (task `P1-B05`) builds each tile like this:

1. Lay a regular grid on the tile's square in the Klein model, where the tile really is a straight-edged square (half-width 0.4859 for {4,5}).
2. Move every vertex to its position in the log chart: same direction from the tile centre, distance atanh of the Klein radius, times L.
3. Add kerbs along the edges, a wedge of a corner post at each corner, palette colours and vertex ambient occlusion. Each wedge is bounded by the tile's own two edges: 72° in the curved metric, which is 65.8° in mesh coordinates for {4,5} (Appendix A.6). It is built in Klein coordinates and mapped by step 2, and five wedges from five tiles make one post.

Because the grid lines are geodesics in the Klein model, neighbouring tiles meet exactly along their shared edge. The warp shader then needs nothing special: it receives tile-local positions and applies the log map centred on the walker's head (§4.6).

Three levels of detail keep the triangle count flat as the tile count grows. They are set by grid count, not by edge length: n = 16 grid cells per side on the walker's current tile and the 12 tiles that touch it; n = 8 on every other drawn tile; n = 4 only on the high tier, beyond ρ = 3 (far tiles are a few pixels wide). Every level keeps the same 16 boundary segments per edge, so neighbours of different level share their edge vertices exactly. The three levels hold at most 1,200, 500 and 250 triangles, for any L. They are accepted by error, not by length: the worst screen-space chord error is at most 0.15° from an eye height of 1.6 m at L = 1.4, 1.9 and 3.0 m. The rule "longest edge at most 0.25 m" stays for the Home shell, furniture and authored props.

**Content is a pure function of the tile id.** `archetype = table[hash(tileId, seed)]`, constrained by region. The same seed gives the same world on the phone and on the Station, which is what lets the Spectator redraw the walker's view from a few numbers.

| Archetype | What it is | Purpose |
|---|---|---|
| Home | Tile zero and any tile under the room's footprint: no generated props | The room itself is the content |
| Plaza | Open floor with the four corner wedges | Default; keeps sight lines open |
| Corridor | Low kerb walls (0.9 m) on two opposite edges | Shows "parallel" walls diverging |
| Doorway | One edge with a framed opening | Region gates |
| Tower | A tall emissive beacon at the tile centre | Navigation; at least one within ρ = 2.5 of every tile |
| Span A / Span B | Two neighbouring tiles that each carry half of one long object | A straight beam visibly bends across tiles |
| Garden | Low props only (under 0.5 m) | Variety without blocking the view |

Regions are the four sectors of the tiling: a tile belongs to the edge of tile zero through which its shortest path leaves; when two edges tie, the lower edge index wins. Each has its own hue; the fog and the background take the hue of the region the walker is standing in (§8.1). The world reads as four "lands" that touch only along their borders.

**WorldLint** is an editor check that every generated world must pass before it reaches the phone: every tile reachable; a tower visible from every tile; every tile has its four wedges; at least one spanning object near the start; draw calls and triangles inside budget from every tile centre and eight headings; no coincident faces between neighbours; the region of every tile to depth 6 is the same on the phone and on the Station; the same seed gives the same world twice.

### 3.5 What makes a curved world legible

These rules come from the one user-tested hyperbolic walking system we found (Holonomy, TU Delft) and from our own comfort constraints (R1, R6).

| Rule | Value |
|---|---|
| A post at every tile corner | Always. It is how "five rooms meet here" is read at a glance |
| Tile edges drawn on the floor | Always, in the shader |
| One object that spans two tiles, visible from the start | At least one |
| A tall landmark visible from every tile | Tower within ρ = 2.5; a pole beacon on the Small Planet |
| Open sight lines | Kerbs of 0.9 m, not walls; no tile closed on four sides |
| Fog in the hue of the walker's region | Starts at ρ = 2, fully opaque at ρ = 3; every tile that reaches inside that radius and can be in view is drawn (§8.5) |
| Flat twin shown after the first lap, not before | A map shown too early distracts from noticing the geometry (Spec §10.2) |
| A home beacon | Arrow and distance to tile zero; people steer home as if space were flat (R1) |
| Solid-looking things near the head | Fade within 0.3 m: virtual walls in Curved Walk are not real walls |
