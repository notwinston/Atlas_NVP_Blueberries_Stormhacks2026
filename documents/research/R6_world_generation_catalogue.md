# R6 — World generation catalogue for free-walk, scanned-room ATLAS

Research memo, 3 Oct 2026. Replaces the "3×3 m floor mapped into each geometry" catalogue (Brief §3.7, Spec §8.3).
Technology choices follow the Build Pipeline wherever it differs from the Spec/Brief.

**Claim tags:** `[V]` = verified, fetched primary source · `[S]` = secondary (search snippet or the team's own docs) · `[D]` = my derivation · `[E]` = estimate.

---

## 0. The five decisions this memo recommends

1. **Two world layers, always.** A *geometry layer* (the generated non-Euclidean world) and a *physical layer* (the scanned room: floor edge, furniture, doors). In flat spaces (hub, glued, portal rooms) the two are locked together, so furniture can be a solid in-world proxy. In curved-walking spaces (H²×E, S²×E) they cannot be locked (the map is path-dependent), so the physical layer is drawn as an unwarped, head-relative "ghost" overlay. `[D]`
2. **Free walk in glued spaces = render the universal cover fixed to the real floor.** The user simply walks through the copies. No re-centring, so no global inverted culling even for the Klein bottle. This relaxes the Build Pipeline's "don't let the user cross a glued edge" rule; flagged in §8. `[D]`
3. **Hyperbolic world = smooth H²×E via the log map (Build Pipeline), tiled by {4,5}, with L chosen from the scan** (default 1.5–2.0 m). Tile edge is then fixed by geometry: s = 1.2537·L. Tiles are generated lazily from a deterministic hash of the tile id. `[D]`
4. **Every tile owns only its own interior.** Corner landmarks are authored as a 1/q wedge inside each tile and walls as half-thickness, so copies never overlap and nothing z-fights. `[D]`
5. **A "WorldLint" editor check runs on every generated world** (reachability, landmark visibility, draw calls, triangle count, coincident faces, physical-fit). A world that fails does not ship to the phone. `[D]`

---

## 1. What prior art tells us (only the plan-relevant facts)

| Source | Finding | Tag | Consequence for ATLAS |
|---|---|---|---|
| Holonomy (TU Delft 2024), arxiv.org/html/2409.05460 | {4,5} chosen because "five meeting squares is the smallest number to exhibit hyperbolic geometry". Move area "at least 3×3 meter … subdivided into nine 1×1 meter cells". | `[V]` | {4,5} is the default tiling. Their cell mapping is the fixed-square design we are replacing. |
| Holonomy | Content: trees at vertices where five squares meet; flowers on single tiles; logs span two tiles; creeks continue across several tiles. "Creeks and logs suddenly vanish behind one side of a tree." | `[V]` | Our legibility kit: corner landmark, single-tile prop, two-tile spanning object, multi-tile ribbon. |
| Holonomy | Populated with wave-function collapse plus a biome hierarchy. | `[V]` | WFC for tile dressing is proven on this exact tiling. |
| Holonomy | State is a spanning tree of discrete step sequences, "preferred, because numeric coordinate representation … introduces numeric precision issues". | `[V]` | Tile ids must be discrete. Build Pipeline §0.5 already says "address by tile id". |
| Holonomy | Rendering: seven views (one 3×3 grid + six portal sub-grids), done with stencil polygons. | `[V]` | This is the piecewise-flat alternative to the log map (§2.3, option B). |
| Holonomy | Users: initial confusion, three of twelve called it "confined" or "claustrophobic"; "no participant had to abort … due to VR sickness"; mini-map position made no performance difference. | `[V]` | Open sight lines, low walls, a mini-map, and real walking only. |
| Tea for God devlog, void-room.itch.io/tea-for-god/devlog/235914 | World is a 2D grid of independent cells; exits sit "in a specific place within the player's VR space" so a cell is generated without knowing its neighbours. | `[V]` | Portal rooms: fixed door sockets in physical coordinates. |
| Tea for God devlog | "If you enter a room with two doors and you choose the door on the right, you should turn right. On the map." | `[V]` | Turn-consistency rule kept (already Spec §6.4). |
| Tea for God, itch.io/post/2351017 | "The smallest acceptable is 3x3m with the tile count 5x5" (for mid-sized zones). | `[V]` | 0.6 m tiles are a workable corridor grid. |
| Tea for God | Smallest supported space 1.8×1.2 m. | `[S]` | Tiny-room fallback is plausible, not verified. |
| Manifold Garden, blog.siggraph.org/?p=11525 | The whole world is duplicated in a grid out to view distance; "only one 'real' object is controlled by the CPU, and the GPU can then render many instances of it". | `[V]` | Same model as our glued copies. We use GameObject copies (Build Pipeline) because copy counts are small. |
| VRoamer (Cheng, Ofek, Holz, Wilson 2019), microsoft.com/en-us/research/…/VRoamer… | 2D height map; anything more than 20 cm off the floor is an obstacle; walkable area eroded by 50 cm; finds the maximal empty rectangle and drops a pre-authored room there, else generates a corridor; virtual elements on a 1.28 m grid; rooms regenerated on revisit. | `[V]` | Direct template for §3. |
| VRoamer | 14 participants, mean 66 m walked at 0.74 m/s, one collision (side-stepping into a table outside the field of view). | `[V]` | Side-stepping is the collision case; keep proxies visible in peripheral vision. |
| Scenograph (Marwecki & Baudisch, UIST 2018), hpi.de/…/Scenograph_Online_Version.pdf | Tracking volume given as a polygon, tessellated into 1×1 m tiles (5×5 m → 25 tiles); experience is split into more, smaller scenes when the space shrinks (25 m² → 9 m² and an L-shaped 8 m²); the largest object (2×2 m) sets the minimum space; re-instantiates at runtime when space changes. | `[V]` | Rescan = re-instantiate with logical state kept; each archetype declares a minimum footprint. |
| Oasis (Sra et al.), sites.cs.ucsb.edu/~sra/oasis.html | Scan → walkable area → generated world; real objects "paired with virtual counterparts". | `[V]` | "Substitutional reality" proxies. |
| Hyperbolic constraint puzzles, arxiv.org/abs/2002.09534 | "Valid for any puzzle based on satisfying local constraints on a graph embedded in the hyperbolic plane" — solvable in polynomial time. | `[V]` | Local-constraint generation on H² tilings is tractable in theory. |
| HyperRogue | Lands separated by hyperbolic straight lines ("Great Walls"); world generated on the fly. | `[V]` Wikipedia | Region boundaries along geodesics; lazy generation. |
| HyperRogue | One land ("Eclectic City") is generated with WFC. | `[S]` | Not fetched from a primary source. |
| HyperEngine, github.com/HackerPoet/HyperEngine | MIT; "not actively maintained … provided as a reference"; documented by an included PDF and devlogs 1–6. | `[V]` | Shader port reference only; no world-building tools to reuse. |
| RoomPlan, developer.apple.com/documentation/roomplan/capturedroom | `CapturedRoom` has `walls`, `doors`, `windows`, `openings`, `floors`, `objects`, `sections`; iOS 16+. Object categories: bathtub, bed, chair, dishwasher, fireplace, oven, refrigerator, sink, sofa, stairs, storage, stove, table, television, toilet, washerDryer. | `[V]` | 16 categories → 5 proxy meshes (§3.3). |
| RoomPlan | Floors exposed with polygon corners from iOS 17. | `[S]` | Check on device. |

---

## 2. Space types to generate in Phase 1

### 2.1 Summary table

| # | Space | Parameters | The "aha" (impossible in flat space) | Minimum content to make it legible | Generation method |
|---|---|---|---|---|---|
| S0 | **Euclidean hub** (the scanned room) | Floor polygon, free rectangle W×D, furniture boxes, door sockets | None by design: this is the calibration chart and safe harbour. Its job is "this is your real room, rebuilt". | Floor grid lines at 0.5 m; walls at true positions; furniture proxies; one door frame per blade | Deterministic rebuild from the scan (§3) |
| S1 | **Glued: T²** | Cell W×D (from scan), copies N×N (5×5 target), fog end | Look ahead and see your own back. Walk east and re-enter from the west of the same room. | Self-avatar in every copy except your own; one asymmetric landmark per cell (so copies are recognisably the same room); floor grid | Authored cell + deck-transform copies |
| S2 | **Glued: Klein bottle** | As T² plus flip axis | Your copy in alternate rows is mirror-imaged: raise your right hand, the copy raises its left. A carried asymmetric object comes back mirrored. | Chiral landmark (an "R" sign, a spiral, a one-armed statue); chiral self-avatar (coloured left/right shoulders) | Same cell; odd rows get negative scale |
| S3 | **Glued: T³** (via copies) | Vertical period H ≥ 2.6 m | Look up and see the top of your own head; look down and see yourself from below. | Open ceiling/floor grid (lattice floor), self-avatar | Same cell; copies in y too (3×3×3 = 27, or 5×5×3 = 75) |
| S4 | **Hyperbolic H²×E, {4,5}** | L (curvature radius), tile edge s = 1.2537·L, fog end ρ_fog, q (5 default) | Five squares meet at every corner. Walk a closed square on the real floor and you do not come back: you arrive in a new tile, rotated. There is exponentially more room than there should be. | Wedge landmark at every tile corner; tile edge lines in the shader; one spanning object per region; region colour; mini-map (Poincaré disc); coloured fog | Lazy, deterministic hash of tile id → archetype + props; WFC as target |
| S5 | **Spherical S²×E** ("small planet") | R, tiling {4,3} (6 tiles) default, fog end as angle | Walk straight and return to where you started. Three right-angle turns close a triangle. Far objects get wider, not smaller, past the "equator". The world is finite: 6 tiles, then it repeats because it is the same place. | Six distinct face landmarks (one per tile, unique colours); a pole beacon at the antipode; tile edge lines; lap counter | Fully authored (only 6 tiles) |
| S6 | **Portal-stitched impossible rooms** | Room footprint list, door sockets in physical coordinates, max overlap | A closet that contains a hall. A corridor that turns right four times and arrives somewhere new. | Framed doorways; distinct hue per room; view through the door before entering; room-number signs | Authored room graph for Phase 1; cyclic graph grammar as stretch |
| S7 | **4D: polytope slicer** (tesseract) | w₀ (slice offset), slice orientation (rotation in a plane containing w), polytope | A solid changes shape as you "move" in a direction you cannot point to: tetrahedron → truncated tetrahedron → octahedron and back. | Per-cell colour (8 cells, 8 colours) so the cross-section faces are identifiable; a w-ruler; the 3D shadow (projection) beside the slice | CPU slicing of 48 tetrahedra (Build Pipeline) |
| S8 | **4D: ℝ³×S¹** | Period P in w (or phase φ), sample count | The fourth direction is a circle: keep going and the object returns. This is the exact structure the medical blade needs (breathing phase). | Phase ring UI; the swept "tube" drawn along one room axis | CPU slicing of a periodic 4D mesh, or precomputed slices |

### 2.2 Formulas the generator needs

Regular tiling {p,q}, curvature radius L (hyperbolic) or R (spherical). All `[D]`, numerically checked.

- Edge length: cosh(a/2) = cos(π/p) / sin(π/q) (hyperbolic); cos(a/2) = cos(π/p) / sin(π/q) (spherical).
- In-radius: cosh(r) = cos(π/q) / sin(π/p) (hyperbolic); same with cos on the sphere.
- Tile area: L²·(π(p−2) − 2πp/q). Holonomy around one tile = area / L² radians.
- Tiles within hyperbolic distance ρ of the viewer, {4,5}: N ≈ 5·(cosh ρ − 1).

| Tiling | Edge | In-radius | Klein half-width of the square | Holonomy per tile | Tile edge at L = 1.5 m | L if edge must be 1 m |
|---|---|---|---|---|---|---|
| {4,5} | 1.2537 L | 0.5306 L | 0.4859 | 72° | 1.88 m | 0.80 m |
| {4,6} | 1.7627 L | 0.6585 L | 0.5774 | 120° | 2.64 m | 0.57 m |
| {4,7} | 2.1408 L | 0.7245 L | 0.6197 | 154° | 3.21 m | 0.47 m |
| {4,8} | 2.4485 L | 0.7643 L | 0.6436 | 180° | 3.67 m | 0.41 m |
| {5,4} right-angled pentagons | 1.0613 L | 0.6269 L | n/a | 90° | 1.59 m | 0.94 m |

| Sphere tiling | Tiles | Edge | Edge at R = 1.5 m | Note |
|---|---|---|---|---|
| {4,3} cube | 6 squares, 3 per corner | 1.2310 R | 1.85 m | Default. Corner angle 120°. Mirror image of {4,5}: three squares per corner instead of five. |
| {3,4} octahedron | 8 triangles | 1.5708 R | 2.36 m | The "three right angles" triangle. Use as the walked task, not as the tiling. |
| {5,3} dodecahedron | 12 pentagons | 0.7297 R | 1.09 m | Stretch. |

| ρ (fog end, units of L) | Tiles in range ({4,5}) | Draw calls, both eyes, 1 per tile |
|---|---|---|
| 2.0 | 14 | 28 |
| 3.0 | 45 | 90 |
| 3.5 | 78 | 156 |
| 4.0 | 131 | 262 |
| 5.0 | 366 | 732 |

**Consequence:** the Build Pipeline's "fog at 5–8 units" is the float-precision horizon, not a draw-call-safe distance. With the < 300 draw-call budget, fog must be fully opaque by **ρ ≈ 3.5** (and 3.0 on the thermal tier). `[D]` See §8.

### 2.3 Hyperbolic: two ways to build it

| | A. Smooth H²×E, log map (Build Pipeline default) | B. Piecewise-flat {4,5} with stencil portals (Holonomy) |
|---|---|---|
| What is curved | Everything, continuously | Nothing inside a tile; all curvature sits at the corners (cone angle 450°) |
| Free walk | Natural. L is a free parameter. Virtual position is path-dependent. | Physical floor must be cut into an integer cell grid; tile walls coincide with real cell edges |
| Mesh needs | 0.1–0.25 m subdivision, tiles authored in the log chart | Plain flat tiles, no subdivision |
| Rendering | One vertex-shader warp; no stencil | Seven stencil views `[V]` |
| Physical furniture | Cannot be a fixed in-world object (overlay only) | Can be fixed per physical cell |
| Geometric honesty (30% of the rubric) | True constant negative curvature; straight lines visibly bend | A square-tiled surface; arguably "portals", not curvature |
| Recommendation | **Phase 1** | Fallback only if the shader port fails by hour 8 |

### 2.4 How tiles are authored for option A

The Build Pipeline shader takes room-local metres and applies `ExpH2(posOS.xz / L)`. So a tile mesh is authored **in the log (azimuthal) chart centred on the tile**, not as a Euclidean square. `[D]`

Recipe for the Blender MCP generator (unit L = 1):
1. Build an N×N grid on the Klein square [−h, h]² with h = 0.4859 for {4,5}. In Klein coordinates the tile is a true straight-edged square.
2. Map each vertex k → chart position (atanh|k| / |k|)·k. Edges bow inward; corners land at 0.8425 from the centre, edge midpoints at 0.5306.
3. N = 12 segments gives ≈0.10 m edges at L = 1 and ≈0.16 m at L = 1.5, inside the 0.1–0.25 m rule.
4. At import, scale x and z by L (one mesh serves every L). Walls keep their real height. Props are separate objects with their own anchor, so they are not stretched.
5. For the sphere, the same recipe with the gnomonic square [−1, 1]² and atan in place of atanh ({4,3} cube face).

One CPU code path serves both curvatures: the per-frame step is the matrix exponential of [[0,0,dx],[0,0,dz],[−κdx,−κdz,0]] with κ = −1 (H²), 0 (flat), +1 (S²). For S² the state is an ordinary rotation matrix and the re-orthonormalisation is ordinary Gram–Schmidt. `[D]`

### 2.5 Spherical free walk: what is different

| Property | H²×E | S²×E | Tag |
|---|---|---|---|
| Holonomy after a counter-clockwise loop of area A | Frame turns clockwise by A/L² | Frame turns counter-clockwise by A/R² (sign flips) | `[D]` |
| Closed polygon of right angles | Pentagon, side 1.0613 L (1.59 m at L = 1.5) | Triangle, side πR/2 (2.36 m at R = 1.5) | `[D]` |
| Extent | Infinite; tiles grow as e^ρ | Finite: area 4πR² (28 m² at R = 1.5; 50 m² at R = 2) | `[D]` |
| Straight walk | Never returns | Returns after 2πR (9.4 m at R = 1.5; 12.6 m at R = 2) | `[D]` |
| Log-map problem | None (precision only) | The antipode (distance πR) smears into a ring. Triangles crossing it stretch across the whole view. | `[D]` |
| Fix | Fog by ρ ≈ 3.5 | Fog opaque by 0.8π; discard fragments beyond 2.6 rad; keep tall objects away from each tile's antipode | `[D]` |
| Tile lifetime | Lazy spawn and despawn | All 6 tiles always loaded | `[D]` |
| Precision | Needs double + re-anchoring | Float is enough; no drift to infinity | `[D]` |

Recommended R: 1.5–2.0 m. Smaller is a stronger effect but the antipode is only 4.7 m away at R = 1.5, so fog must be tight. `[E]`

### 2.6 Tile and room archetypes to author

Sizes are for L = 1.5 m (H²) and R = 1.5 m (S²). Heights in metres. Triangle budgets follow Build Pipeline §5.

**Hyperbolic {4,5} tile set (authored in chart, 12×12 subdivided floor = 288 tris)**

| Archetype | What it is | Size | Tris | Purpose |
|---|---|---|---|---|
| H-Plaza | Open floor, four corner wedges | 1.88 m edge | ≤ 600 | Default; keeps sight lines open (answers Holonomy's "claustrophobic") |
| H-Corner wedge (in every tile) | 72° wedge of a pillar, 2.6 m tall; five wedges make one pillar | 0.15 m radius | 80 each | The "five at a corner" count is readable from any corner |
| H-Corridor | Two opposite low walls (0.9 m high, half-thickness) | 1.88 m | ≤ 900 | Shows parallel walls diverging |
| H-Doorway wall | One edge with a 0.9 m-wide, 2.1 m-high framed opening | 1.88 m | ≤ 1,000 | Region gates; frames the next tile |
| H-Landmark tower | Tall emissive beacon at the tile centre, 4 m | 0.4 m base | ≤ 1,500 | Navigation; one per region |
| H-Span half (A and B) | Two tiles that each carry half of a long object (bridge beam, pipe, "creek" ribbon) meeting at the shared edge | 1.88 m each | ≤ 800 each | The long object visibly bends: straight lines are not straight |
| H-Garden | Low props only (≤ 0.5 m) | 1.88 m | ≤ 1,200 | Filler variety without blocking the view |
| H-Hub tile | The tile the user enters on; carries the return door to S0 | 1.88 m | ≤ 1,500 | Safe-harbour anchor |

**Spherical {4,3} tile set**

| Archetype | Count | Tris | Note |
|---|---|---|---|
| S-Face (floor in gnomonic → chart, 12×12) | 6, each a different hue and a different centre landmark | ≤ 1,500 each | Low landmarks (≤ 1.2 m) so the horizon reads |
| S-Corner wedge (120°) | In every tile | 60 | Three per corner |
| S-Pole beacon | 1 | ≤ 500 | Marks "the far side"; from the start tile it is seen in every direction |

**Glued cell**

| Archetype | Size | Tris | Note |
|---|---|---|---|
| G-Cell shell | W×D from the scan (1.4–3 m), posts at four corners, floor grid, no walls | ≤ 3,000 | Open frame so copies are visible. Geometry kept ≥ 2 cm inside the cell boundary. |
| G-Chiral landmark | 0.4 m | ≤ 800 | Placed off-centre; an "R"-like asymmetric shape |
| G-Avatar | Head + torso, left shoulder one colour, right another | ≤ 600 | Drawn in every copy except the user's own |
| G-Lattice floor (T³ only) | W×D | ≤ 400 | See-through grid |

**Portal rooms** (ProBuilder is enough; no warp)

| Archetype | Footprint | Tris | Note |
|---|---|---|---|
| P-Booth / "closet" | 1.0×1.0 m outside | ≤ 800 | Free-standing door frame; inside is P-Hall |
| P-Hall | Whole free rectangle | ≤ 2,000 | The bigger-inside reveal |
| P-L-corridor | 0.9 m wide along two walls of the free rectangle | ≤ 1,500 | Building block of the loop |
| P-Loop set | 4 × P-L-corridor reusing the same floor | ≤ 6,000 total | Four right turns, new room |
| P-Door frame | 0.9 × 2.1 m opening | ≤ 800 | Stencil mask + inverted-hull outline (hero object) |
| P-Penrose corridor (stretch) | Perimeter loop | ≤ 2,000 | Flat walk; floor-number signs count down every lap. A flat, honest stand-in for Escher stairs. |

**4D**

| Archetype | Tets | Slice tris (max) | Note |
|---|---|---|---|
| Tesseract | 48 | 96 | Build Pipeline default; 8 cell colours |
| 5-cell | 5 | 10 | Trivial second object; good unit test |
| 16-cell | 16 | 32 | Target |
| 24-cell | 96 (24 octahedra × 4) | 192 | Stretch |
| Periodic tube in ℝ³×S¹ | ≈ 2,000 | ≈ 2,000 | Rehearsal for the medical blade; precompute 16–32 slices if CPU slicing is too slow `[E]` |

Escher stairs proper (walking up a virtual stair on a flat real floor) stay out of Phase 1: the Brief already rules them out of the live path for sickness. `[S]`

### 2.7 Generation method per space

| Space | Phase 1 method | Deterministic key | Target upgrade |
|---|---|---|---|
| S0 hub | Rule-based rebuild from the scan | Scan id | — |
| S1–S3 glued | Authored cell × deck transforms g(i,j[,k]) | (i, j, k) | Cell contents chosen by WFC |
| S4 hyperbolic | `archetype = table[hash(tileId, seed) mod n]`, constrained by region; props from `hash(tileId, slot)` | Tile id | WFC over the tile adjacency graph with edge sockets (open / wall / door / span-A / span-B) |
| S5 sphere | Authored, 6 tiles | Face index 0–5 | — |
| S6 portal | Authored graph of 3–4 rooms; door sockets at fixed physical positions | Room id | Cyclic graph grammar + per-visit placement (Spec §6.5) |
| S7–S8 4D | Analytic meshes | Polytope name | — |

**Tile ids for {4,5}.** `[D]`
- Minimal: breadth-first expansion from the camera tile using double-precision Lorentz anchors; two tiles are the same if their centres are within 0.1 (units of L). Id = index in a dictionary keyed by the quantised centre in the origin frame. Good to a virtual distance of roughly 20 L from the start, which a demo will not exceed. `[E]`
- Target: exact discrete addresses (Holonomy's normalised step sequences `[V]`, or the tree-structure generator of arxiv.org/abs/2111.12040 `[S]`).

**Regions.** A region is everything on one side of a geodesic through tile edges (HyperRogue's "Great Walls" `[V]`). Minimal: region = which of the four first-step directions from the hub tile leads to this tile. Each region has a hue, a fog colour and one tower. `[D]`

**WFC sockets (target).** Each tile edge has one label from {open, low-wall, door, span}. Rule: labels on a shared edge must match. Extra global rules: no tile with four non-open edges; a span-A must face a span-B. Solve only for tiles entering the ρ ≤ 3.5 window; already-collapsed tiles are fixed. Contradiction fallback: H-Plaza (all open) is always legal. `[D]`

---

## 3. How the scanned room feeds generation

### 3.1 Pipeline

| Step | Input | Operation | Output | Tag |
|---|---|---|---|---|
| 1 | LiDAR scan | RoomPlan `CapturedRoom` (native Swift, exported as JSON to Unity), or AR Foundation floor planes + mesh as the fallback | Walls, doors, windows, openings, floor polygon, object boxes with category | `[V]` API; `[D]` bridge |
| 2 | Floor polygon + object boxes | Rasterise to a 0.05 m occupancy grid; anything standing more than 0.2 m above the floor is an obstacle | Free / obstacle / unknown grid | `[V]` VRoamer threshold |
| 3 | Grid | Erode free cells by the safety margin m (0.3 m default; VRoamer used 0.5 m) | Eroded free floor | `[V]` |
| 4 | Eroded grid | Largest inscribed rectangle: for each angle θ in 0–85° (5° steps, seeded with the dominant wall direction), run the maximal-rectangle scan; keep the largest area | Free rectangle (centre, θ, W, D) | `[D]` |
| 5 | Free rectangle | Derive per-space parameters (table below) | Cell size, L, R, room footprints | `[D]` |
| 6 | Object boxes | Map category → proxy mesh, scaled to the box | Virtual solids | `[V]` Oasis / VRoamer principle |
| 7 | Doors, openings | Each becomes a portal socket at its real position | Blade entrances in the hub | `[S]` Brief §3.10 |
| 8 | All | Run WorldLint (§5.3); save scan id + derived parameters | Go / no-go | `[D]` |
| 9 | — | Turn off meshing and plane detection | — | `[S]` Build Pipeline §5 |

### 3.2 Derived parameters

Let W ≤ D be the free rectangle, A = W·D.

| Parameter | Rule | Reason |
|---|---|---|
| Glued cell | n = max(1, round(W / 1.5)); cell width = W/n; same for D | Cells near 1.5 m so wraps happen on open floor; whole number of cells in the rectangle |
| Glued copies | Lattice fixed to the floor, 5×5 around the user's current cell | Free walk through copies |
| Hyperbolic L | L = clamp( sqrt(A / 0.8), 1.5 m, 3.0 m ) | One lap of the free rectangle turns the frame by roughly 45° or more (holonomy ≈ A/L²), while L never drops below the comfort floor of 1.5 m (Spec §11.1) `[D]`, small-loop approximation |
| Tile edge | s = 1.2537·L | Fixed by {4,5} |
| Sphere R | R = clamp( W / 1.6, 1.5 m, 2.5 m ) | The right-angle triangle (legs πR/2) should need at most about two passes of the room `[E]` |
| Portal rooms | P-Hall = free rectangle; corridor width = clamp(W/3, 0.7, 1.0 m) | Tea for God's 0.6 m tiles are the lower bound `[V]` |
| Fog end | Flat spaces: 3 cells. H²: ρ = 3.5. S²: 0.8π | Budget (§2.2) |

### 3.3 Furniture, doors, windows

| Scan element | Flat spaces (S0, S1–S3, S6) | Curved spaces (S4, S5) and 4D |
|---|---|---|
| Wall | Solid virtual wall at the true position (hub); play-area edge elsewhere | Unwarped ghost boundary, fades in within 1 m |
| Object box: table, storage, refrigerator, oven, stove, dishwasher, washerDryer, fireplace, television, sink | Proxy "block" or "table" scaled to the box, snapped to palette | Ghost pillar/box outline in the physical layer |
| Object box: chair, sofa, bed, toilet, bathtub | Proxy "seat" / "low block" | Ghost outline |
| Object box: stairs | Hard no-go zone, removed from the free floor with 0.5 m margin | Same |
| Door / opening | Stencil portal socket; one blade per door, extra blades get free-standing frames | Not shown |
| Window | Window panel (hub only) | Not shown |
| Unknown floor | Not walkable, fogged | Not walkable |

Five proxy meshes cover the 16 RoomPlan categories: block, table, seat, low block, column. `[D]`

Why the ghost layer in curved spaces: with the log map centred on the head, a point straight ahead at geodesic distance d is reached after walking d, so *radial* distances are true, but the correspondence between a fixed physical point and a virtual point changes as the user moves sideways. A physical wall therefore has no fixed virtual location. `[D]`

### 3.4 Rescan behaviour

| Event | Regenerated | Kept |
|---|---|---|
| Rescan, same room, free rectangle changed by less than 0.2 m | Proxies only | Everything else |
| Rescan, rectangle changed | Glued cell size, portal room footprints, L and R, fog; WorldLint re-run | World seed, tile ids, region colours, logical state (Scenograph keeps logical nodes on re-instantiation `[V]`) |
| New obstacle appears mid-session (stretch) | A ghost proxy animates in at that spot (VRoamer's spikes `[V]`) | World |
| Relocalisation jump over 0.2 m | Nothing; the pose jump is rejected (Build Pipeline §4) | — |

Hyperbolic tile *content* is independent of the room: only L (a scale) changes, so the same seed gives the same world in any room. `[D]`

### 3.5 Tiny and large rooms

| Room | Free rectangle (0.3 m margin) | Glued | Hyperbolic | Sphere | Portal rooms |
|---|---|---|---|---|---|
| 2×2 m | 1.4×1.4 m | One cell of 1.4 m; user stays in it, sees 5×5 copies | L = 1.6 m (floor of the rule); effect is modest per lap, so prompt figure-of-eight laps | R = 1.5 m; triangle legs need about two passes each | Booth + hall only; no loop corridor (too narrow) |
| 3×3 m | 2.4×2.4 m | 2×2 cells of 1.2 m | L = 2.7 m by the rule; consider forcing 2.0 m | R = 1.5 m | Full loop set with 0.8 m corridors |
| 6×8 m | 5.4×7.4 m | 4×5 cells of 1.35–1.48 m; real wrap-walking | L = 3.0 m (cap); a full-room lap gives several radians, so use sub-loops | R = 2.5 m; a full lap is 15.7 m | Several rooms without overlap; overlap used only for the reveal |
| Below 1.5×1.5 m free | Seated / standing mode: gamepad or head-gaze translation, with the Build Pipeline's comfort caveats | — | — | — |

---

## 4. Asset production plan (Build Pipeline tools)

### 4.1 Mesh list for Phase 1

| # | Asset | Source | Subdivision | Tris (each) | Time | Hour |
|---|---|---|---|---|---|---|
| 1 | Palette texture 128² (8 hue ramps × 4 lightness + gradient strips) | Hand / script | — | — | 0.5 h | 0 |
| 2 | H2_45 floor tile, L = 1 | Blender MCP, parametric (§2.4) | 12×12 in Klein → chart | 288 | 1.0 h incl. export script | 0–2 |
| 3 | H2_45 corner wedge 72° | Blender MCP | 8 height segments | 80 | 0.3 h | 2 |
| 4 | H2_45 archetypes ×7 (Plaza, Corridor, Doorway, Tower, Span-A, Span-B, Garden, Hub) built on #2 | Blender MCP + hand | walls subdivided at 0.15 m horizontally | 600–1,500 | 3 h | 2–6 |
| 5 | S2_43 floor tile, R = 1 + 120° wedge | Blender MCP (same script, atan) | 12×12 | 288 + 60 | 0.5 h | 6 |
| 6 | S2 face landmarks ×6, pole beacon | CC0 kits (KayKit / Kenney), recoloured to palette | — | ≤ 1,200 | 1.5 h | 6–8 |
| 7 | Glued cell shell, chiral landmark, lattice floor | ProBuilder → merged | none (no warp) | ≤ 3,000 | 1.0 h | 0–2 |
| 8 | Self-avatar (head, torso, coloured shoulders) | Blender, hand | — | ≤ 600 | 0.5 h | 2 |
| 9 | Portal door frame (hero, inverted-hull outline) | Blender, hand | — | ≤ 800 | 0.7 h | 3 |
| 10 | Portal rooms: booth, hall, L-corridor | ProBuilder, parametric from W, D | none | ≤ 2,000 | 1.5 h | 3–6 |
| 11 | Furniture proxies ×5 (unit boxes with bevel + palette UV) | Blender MCP | — | ≤ 300 | 0.5 h | 1 |
| 12 | Ghost boundary / ghost pillar (unlit, dithered) | Shader + unit quad / cylinder | — | ≤ 100 | 0.5 h | 4 |
| 13 | Tesseract, 5-cell, 16-cell tetrahedral meshes | C# generated (no art) | — | 48 / 5 / 16 tets | 1.5 h (engine dev) | — |
| 14 | Small props for Garden tiles ×8–10 | CC0 kits first; AI (Meshy, TripoSG) only if nothing fits | — | 300–1,500 | 2 h | 6–10 |
| 15 | Gradient skybox, per-region fog colours | Shader | — | — | 0.3 h | 1 |
| 16 | Audio: 1 ambient loop per space, portal chime, tile-edge tick | Kenney audio / Sonniss | — | — | 1 h | 8+ |

Totals: about 30 meshes; one person, roughly 14–16 working hours, of which items 1, 2, 7, 11, 15 (≈ 3.5 h) unblock the engine work and must land first. All times `[E]`.

### 4.2 Palette and vertex-AO workflow

1. Every mesh uses the single palette material; UVs are collapsed onto one palette block per face group (Blender MCP assigns them).
2. Bake AO to the vertex-colour attribute (Cycles bake to colour attribute or VertexOven). Subdivided tiles have enough vertices for AO to read; ProBuilder boxes may need one extra loop cut near corners.
3. Region hue is not baked: the shader multiplies the palette colour by a per-material region tint, so one tile mesh serves every region.
4. Tile edge lines and the 0.5 m floor grid come from the shader (`fwidth`), computed in **tile-local chart coordinates** so they follow the curved edges. For hyperbolic tiles the edge line is the Klein-square boundary; pass Klein coordinates as a second UV set from Blender. `[D]`
5. No normal maps anywhere (mirrored Klein copies would be wrong; Build Pipeline §4).

### 4.3 Naming and import conventions

| Item | Convention |
|---|---|
| File | `ATL_<Space>_<Archetype>_<Variant>.fbx`, e.g. `ATL_H245_Plaza_A.fbx`, `ATL_S243_Face_03.fbx`, `ATL_GLU_Cell_A.fbx`, `ATL_PRT_DoorFrame_A.fbx`, `ATL_PRX_Table.fbx` |
| Units and axes | Metres, scale 1.0, transforms applied, Forward −Z, Up Y (Build Pipeline §3a) |
| Pivot | Curved tiles: **tile centre on the floor** (the chart origin). Flat rooms: a floor corner (Build Pipeline). |
| Tile scale | Curved tiles authored at L = 1 (or R = 1); importer scales x and z by L |
| Sockets | Empties named `SOCKET_<type>_<n>`: `edge` (0–3, counter-clockwise from +x), `prop`, `landmark`, `portal`, `avatar` |
| Ownership | No geometry within 2 cm of a tile or cell boundary except half-thickness walls and corner wedges |
| Bounds | Importer sets `localBounds` huge on warped renderers (Build Pipeline §1) |
| Materials | One shared material asset; one instance per tile for the anchor (SRP Batcher-compatible; no MaterialPropertyBlocks) |
| Import settings | Read/Write off except 4D meshes; vertex colours kept; mesh compression off on warped tiles (keeps the grid exact) |
| Licences | A `CREDITS.md` row per third-party asset; Asset Store packs kept out of the public repo |

### 4.4 What runs in parallel from hour 0

| Track | Hour 0–2 | Hour 2–6 | Hour 6–12 |
|---|---|---|---|
| Art / assets (1 person) | Palette, glued cell, proxies, H² floor tile script | H² archetypes, avatar, door frame | Sphere tiles, landmarks, props, audio |
| Engine A (stereo, glued, portals) | Uses a ProBuilder cube | Swaps in the glued cell and door frame | Portal rooms |
| Engine B (hyperbolic, sphere) | Uses a procedurally generated flat grid in C# as a stand-in for the tile | Swaps in the Blender tile | Tile generator, regions, mini-map |
| Engine C (scan, 4D, phone–PC link) | No art dependency | Proxies arrive at hour 1 | WorldLint |

The only hard art → engine dependency is the subdivided tile (item 2), and Engine B can generate an equivalent grid in code in about 20 lines while waiting. `[D]`

---

## 5. Legibility and comfort rules, and automated checks

### 5.1 Legibility rules

| Rule | Value | Source |
|---|---|---|
| A corner landmark at every tile vertex in curved spaces | Always | Holonomy `[V]` |
| At least one object that spans two or more tiles visible from the entry tile | ≥ 1 | Holonomy `[V]` |
| Tile edges drawn on the floor | Always, shader | Build Pipeline `[S]` |
| A tall, emissive, unique landmark visible from every tile | Within ρ ≤ 2.5 (H²); pole beacon (S²) | Build Pipeline §0.5 "people home in Euclidean terms" `[S]` |
| Region hue changes across geodesic boundaries | 4 regions minimum | HyperRogue `[V]` |
| Fog in the room hue; lightness steps down with copy depth | H²: start ρ = 2.0, opaque ρ = 3.5. Glued: start 2 cells, opaque 3 cells. S²: start 0.5π, opaque 0.8π | Build Pipeline §2 + §2.2 above `[D]` |
| Mini-map / flat twin | Poincaré disc for H² (Holonomy used it `[V]`); unfolded cube net for S²; lattice plan for glued; room graph for portals. Wrist- or floor-anchored, not head-locked. | `[V]` / `[D]` |
| Sight lines stay open | Walls ≤ 0.9 m except doorway tiles; at most 1 in 4 tiles has a full-height wall | Holonomy "claustrophobic" `[V]` → `[D]` |
| Self-avatar in every copy | Glued spaces | Build Pipeline `[S]` |
| A caption chip names the space and its rule in one line | At every entrance | Spec §11.1 `[S]` |

### 5.2 Comfort rules

| Rule | Value | Source |
|---|---|---|
| Real walking only in curved spaces; no artificial translation | Always | Holonomy: no aborts with natural walking `[V]` |
| Translation gains at identity; no vignette while walking | Always | Build Pipeline §0.5 `[S]` |
| Floor stays flat and at real height; vertical is Euclidean | H²×E, S²×E | Build Pipeline `[S]` |
| Curvature radius floor | L, R ≥ 1.5 m | Spec §11.1 `[S]` |
| Curvature never changes while the user is walking | Dial changes only when standing, rate-limited | Spec §11.1 `[S]` |
| Geometry never pops | Spawn tiles beyond the fog-opaque distance only; huge bounds | Build Pipeline §1 `[S]` |
| Pose jumps over 0.2 m per frame rejected | Always | Build Pipeline §4 `[S]` |
| Physical layer always wins | Ghost boundary and proxies draw over the world within 1 m | `[D]` |
| Fast Motion Sickness score logged every minute with a stop threshold | Always | Build Pipeline §0.5 `[S]` |

### 5.3 WorldLint: automated "is this world good?" checks

Run in the Unity editor (and on scan completion on device for the physical-fit checks).

| Check | Method | Pass threshold |
|---|---|---|
| Reachability | Flood fill over the tile/room graph through open edges and doors from the entry | 100% of spawned tiles reachable; return door reachable from every tile |
| No dead enclosure | Per tile: count of non-open edges | < 4 |
| Landmark visibility | For each tile centre, hyperbolic distance to the nearest tower | ≤ 2.5; S²: beacon within 0.8π of every tile |
| Corner landmarks present | Each tile has its four wedges | 100% |
| Spanning object | Count span pairs within ρ ≤ 2 of entry | ≥ 1 |
| Draw calls | Simulated camera at each tile centre, 8 headings; count visible renderers × 2 eyes | < 300 (Build Pipeline §5) |
| Triangles | Same sweep | < 300k per frame target, 500k hard |
| Tile count in range | N within fog-opaque distance | ≤ 80 |
| Coincident faces (z-fighting) | Geometry inside the 2 cm boundary band must be tagged wedge or half-wall; for glued cells, hash boundary faces of copy (0,0) against (1,0) and (0,1) | Zero untagged; zero duplicates |
| Mirror handedness | Klein: determinant sign of each copy transform equals (−1)^row | 100% |
| Subdivision | Longest edge on warped meshes after scaling by L | ≤ 0.25 m |
| Bounds | Every warped renderer has enlarged `localBounds` | 100% |
| Antipode safety (S²) | No vertex of a tall object within 0.3 rad of the antipode of any tile centre... simplified: no object taller than 1.2 m | Pass |
| Physical fit | Every flat-space room footprint and every door socket's approach tile lies inside the eroded free floor | 100% |
| Proxy coverage | Every scan object box has a proxy or is removed from the free floor | 100% |
| Determinism | Generate twice with the same seed; compare tile-id → archetype maps | Identical |
| Frame time (on device) | 60 s scripted walk-through capture | p95 ≤ 16.7 ms; no frame over 33 ms outside loads |

---

## 6. Scope ladder (4 people, ~36 h)

| | Minimal (must exist at end of Phase 1) | Target | Stretch |
|---|---|---|---|
| Scan | AR Foundation floor plane → free rectangle; manual "confirm corners" fallback | RoomPlan JSON → proxies, door sockets | Live obstacle updates; multi-room |
| Hub | Floor grid + walls + ghost boundary | Furniture proxies, one door per space | Curvature dial on the hub itself |
| Glued | T² 5×5 with self-avatar, lattice fixed to floor | Klein toggle, T³ | Cell dressed by WFC; orbifold gluings |
| Hyperbolic | {4,5}, H-Plaza + corner wedges + edge lines, fog at ρ = 3, tiles by breadth-first hash | 7 archetypes, 4 regions, towers, span objects, Poincaré mini-map | WFC sockets; exact tile addresses; {4,q} switch; {5,4} rooms |
| Sphere | — (may slip to target) | 6-tile cube planet, pole beacon, triangle task | Dodecahedral tiling; unified κ dial H² ↔ flat ↔ S² |
| Portal rooms | Booth → hall (one stencil portal) | Four-turn loop corridor | Graph grammar; Penrose corridor |
| 4D | Tesseract CPU slice with a w slider, 8 cell colours | 5-cell and 16-cell; rotation in a w-plane; ℝ³×S¹ periodic object | 24-cell; Engine4D vertex-shader slicer; "4D peek" through portal rooms |
| Checks | Draw-call and triangle counters, reachability | Full WorldLint | WorldLint on device after each scan |
| Assets | Items 1, 2, 3, 7, 8, 11, 15 | Items 4–6, 9, 10, 12, 14 | Audio, AI props, extra regions |

---

## 7. Could not verify

- Tea for God's 1.8×1.2 m minimum play area (search snippet only) and its exact cell/corridor algorithms (the devlog is conceptual).
- HyperRogue's "Eclectic City uses WFC" and the precise link between the "Hyperbolic Minesweeper is in P" result and WFC (the abstract does not name WFC).
- Hyperbolica's own tiling and its reasons for placing things at tile corners: the HyperEngine README only points to a PDF and video devlogs, which I did not read. The corner-landmark rule here rests on Holonomy alone.
- Antichamber, Superliminal and the Manifold Garden architecture-generation details: the SIGGRAPH blog covers rendering (instancing) only.
- DreamWalker: not fetched.
- RoomPlan floor `polygonCorners` on iOS 17+ (secondary), and whether RoomPlan can run alongside the AR Foundation session in Unity without a session hand-over.
- The tile-count formula N ≈ 5(cosh ρ − 1) is an area argument, not an exact count; the dictionary-based tile id scheme's safe range (~20 L) is an estimate.
- All per-asset times and the draw-call cost of one material instance per tile on the iPhone 15 Pro Max (measure on device).
- The L rule (A / L² as "holonomy per lap") is a small-loop approximation; a physical rectangle does not map to a closed hyperbolic loop.

## 8. Conflicts and risks found

1. **Fog distance vs draw-call budget.** Build Pipeline: fog at 5–8 hyperbolic units, cull at d > 4. At ρ = 4 there are about 131 tiles (262 draws for two eyes); at ρ = 5 about 366. Recommend fog opaque at 3.5.
2. **Glued edge crossing.** Build Pipeline says treat the room as the fundamental domain and do not cross. Free walk in a large room needs crossing. Rendering the universal cover fixed to the floor makes crossing safe; needs team sign-off.
3. **Tile pivot.** Build Pipeline says pivot at a floor corner; chart-authored curved tiles need the pivot at the tile centre.
4. **Tiles are not Euclidean squares.** A plain subdivided square fed to the log-map shader does not tile {4,5}: gaps or overlaps appear at edges. The Klein → chart recipe (§2.4) is required.
5. **Furniture proxies do not exist in curved spaces** as fixed objects; they must be an overlay. This changes the Spec's "collision walls in every blade".
6. **Sphere antipode** breaks the log map; needs fragment discard and tight fog.
7. **RoomPlan is Swift-only** and needs a native bridge; AR Foundation planes are the lower-risk minimal path.
8. **Spec §8.3 kit** (glTF + KTX2 + trim sheets, curved corridors for R = 2.2 m) is superseded by the Build Pipeline's FBX + single palette material; the airway-specific modules move to the blade phase.

## 9. Questions for the team

1. Hyperbolic method: smooth log-map H²×E (recommended, Build Pipeline) or Holonomy-style flat tiles with stencil portals? The Build Pipeline notes "the plan says Klein" as still open.
2. Do you accept free crossing of glued edges by rendering the universal cover fixed to the floor, including the Klein flip?
3. Default curvature: is L = 1.5–2.0 m acceptable for comfort, or do you want the room-derived rule in §3.2?
4. What rooms will the demo actually run in (venue table area, hallway, a team member's room)? Sizes decide whether the four-turn loop corridor and wrap-walking are in.
5. Is the sphere in Phase 1, or does it move to Phase 2 so the hyperbolic world gets more polish?
6. How does the user move in w in the 4D space: a hand controller or gamepad, crouching (head height), walking along a marked axis, or the tracked cube?
7. Scan source for Phase 1: RoomPlan (native Swift bridge, about half a day) or AR Foundation planes plus a manual corner-confirm step?
8. Who is the asset person, and is one $20 Meshy month approved, or CC0 kits only?
