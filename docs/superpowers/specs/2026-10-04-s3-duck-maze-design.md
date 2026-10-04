# S3 Duck Maze: design

Date: 2026-10-04. Branch: `s3-duck-maze` (off `main` at 1ba2838). Approved in chat section by section.

## Goal

Inside S3 Aligned Cells, a Holonomy-style labyrinth (Skrodzki et al., *Holonomy*, Web3D 2024): the free floor is a
fixed 4 × 4 grid of small cells, the {4,5} tiles behind them carry walls on some edges, and a rubber duck stands on
one tile of the hyperbolic world. The walker finds the duck by walking; it is found when the walker's tile is the
duck's tile.

## 1. Maze generation: `DuckMaze` (engine-free)

- Input: the `CellGrid`, the `TileGraph` (q = 5), the start cell, a seed. The root is always
  `CellFrame(TileId.Zero, 0)` in the start cell.
- State = (tile, turn, cell). Randomised depth-first search: from a state, a step in room direction d (0 +x, 1 +z,
  2 −x, 3 −z) is a candidate when `cell + d` is inside the grid and `frame.Cross(graph, d).Tile` is not yet in the
  tree. Stops at `TargetTiles` = 20 tiles or when no candidate is left.
- A tree: each tile has one route from the root, hence one cell and one frame. Every open edge is a step the
  physical floor allows, so the maze is always solvable by walking.
- Duck: the deepest tile (tree depth = steps from the root). Attempts `a = 0, 1, …, 63` each use seed
  `Mix(seed, a)`; the first attempt whose duck depth is in [8, 12] is kept, otherwise the attempt whose depth is
  closest to 10. Random numbers come from an own SplitMix64, never `System.Random`, so the phone and the Station
  generate the same maze.
- Walls (tile edges, bit k = tile edge k): a maze tile has a wall on every edge that is not a tree edge; a tile
  outside the maze ("off") has a wall on every edge whose neighbour is a maze tile. Both rules give the same answer
  from both sides of an edge.

## 2. Grid and parameters

- `cells.maze` (bool, default on, group of S3). On: `CellGrid.Maze(room)`: 4 × 4, side = short side of the free
  rectangle ÷ 4, needs side ≥ `CellGrid.MinSide` (0.4 m). A smaller floor logs `maze.unavailable` and S3 runs as
  today. Off: today's S3, unchanged (`CellGrid.FromRoom`, the shared `CellGridRules.Aligned` and
  `RoomModel.cells.aligned` are not touched).
- `cells.mazeSeed` (int, 0 … 1,048,575, default 0). 0: a fresh seed for each run; otherwise every run uses it.
- Changing `cells.maze` while S3 is entered reconfigures the space as a re-entry does.
- In maze mode, Enter does not keep Curved Walk's tile: the run starts at tile zero (the maze is a game; §5 needs it).

## 3. Drawing (`AlignedCellsView`)

- 16 cell meshes, one per wall mask, built once with `TileMeshSpec.FlatCell(side, kerbMask)` (0.9 m kerbs, the S2
  wall). An instance takes the mesh of its tile's wall mask; the gates clip walls exactly as they clip the floor.
- Off tiles: tint `AtlasPalette.Void`.
- Duck: one renderer per cell instance showing the duck's tile (several are possible round a post), the same gated
  `Atlas/Surface` material, centred in the cell, about 0.3 m tall, turning slowly and bobbing. After it is found it
  pops (scale up) and spins faster.
- Budget: ≤ 17 instances on 4 × 4; the duck ≤ 2,500 triangles per view.

## 4. The duck asset

- Source `free_rubber_duck_3d_model.glb` (4 meshes, 25,216 triangles, material colours, no textures) is imported into
  `art/blender/ATLAS_Kit.blend` as `Hero_Duck` in collection `ATLAS_Hero`: parts joined, decimated to ≤ 2,500
  triangles, about 0.3 m tall, faces on `pal_<Slot>` materials of four new AtlasPalette cells (duck yellow, orange,
  white, black).
- `tools/kit/export_kit.py` gains piece `duck`; output `duck.glb.bytes` + manifest entry in the existing kit folder
  (`Spaces/Glued/Resources/AtlasKit`: one `AtlasKit/manifest` resource for the whole app).
- S3 loads it with `KitSet`; a refused piece falls back to a procedural duck (yellow sphere body, smaller head,
  orange beak).

## 5. Winning, events, Station

- Found: `Walker.Frame.Tile == DuckTile`. Toast on a head-locked TextMeshPro read-out (as `CurvedWalkHud`):
  "Found the duck! N steps, M walls crossed, m:ss". After 5 s a new maze is generated, rooted at tile zero in the
  walker's cell (the walker's frame is relabelled to tile zero, turn 0).
- Events (phone copy only): `maze.start {seed, tiles, duckDepth, startCell}`, `maze.wall {tile, edge}` (each wall
  walked through), `maze.found {steps, walls, seconds}`, `maze.unavailable`.
- State: `P7 = seed * 16 + startCellIndex` (< 2^24, exact in a float); 0 = no maze. The Station's copy regenerates
  the same maze from it and draws the duck; it logs nothing.

## 6. Testing

Engine-free, in `Atlas.Spaces.AlignedCells.Tests`, run headless:

- 1,000 seeds: tree, ≤ 20 tiles, each tile's cell inside the grid, duck depth in [8, 12] for most seeds and ≥ 6 for
  all; same seed → identical maze.
- Walking the solution's room directions through the real `CellWalker` ends on the duck tile, and the predicted
  cell and frame match at every step.
- Wall symmetry for every maze tile and its four neighbours.
- Core: found on reaching the duck, `maze.wall` on walking through a wall, restart after 5 s, P7 round trip
  rebuilds the same maze on a Station copy. With `cells.maze` off S3 behaves exactly as today: the existing tests
  of classic S3 (and the E2E tests that walk through S3) pin `cells.maze` off and stay green unchanged otherwise.
- Duck asset: `GlbValidator` accepts it, ≤ 2,500 triangles, manifest hash matches.

Not checked here: the GPU picture and a walk by a person on the phone.
