# Lane B: airway graph and navigation (G4)

Tasks: P2-B01 AirwayTree, P2-B06 beacon and route cost (P2-B08 Two Compasses follows after G5). Code: `Atlas.Kernel` (`TileFrame`, `RuleS`, kernel.md §4.8) and `Atlas.Blades.Lung` (`Assets/Atlas/Blades/Lung/Airway/`).
Tags: [plan] fixed by the Master Plan; [min] smallest choice where the plan is silent.

## Deviations

| Id | Deviation | Tag |
|---|---|---|
| D-G4-1 | Kernel gains the public `TileFrame` and `RuleS` (kernel.md §4.8). The plan names `FrameOf`; the types are new. Additive contract change, to be announced (§6.4). | [min] |
| D-G4-2 | The trachea frame is `TileFrame.Zero`: the carina fork is at tile zero and edge 0 faces the mouth. The trachea segment itself has no tile. | [min] |
| D-G4-3 | Window = current, parent and grandparent, each with data descendants to relative depth 3. Maximum 31 nodes and 62 tiles (plan: 60/120). Rejected: the literal reading (current + 2 ancestors + 14 descendants = 17), which classifies sibling corridor tiles 2.12 units away as tissue. The chosen window misses only the third ancestor's tiles (at least 3.862 units from the current centre, in the fog band). | [min] |
| D-G4-4 | Node data covers only nodes in `tree.json`; `FrameOf` works to 23 letters without data. | [min] |
| D-G4-5 | The beacon arrow (P2-B06) points to the next tree tile's centre, the corridor of the prefix step, not along the straight-line bearing to the target. | [min] |
| D-G4-6 | Hesitation is continuous time on the current tree tile (fork or bend), reset on every tile change. | [min] |
| D-G4-7 | "Except at the Two Compasses fork": the beacon is shown on arrival there beside the Euclidean compass and does not count as rescue (`RescueShown` false). | [min] |
| D-G4-8 | Two Compasses is active only if the compass fork is a proper prefix of the route's target. | [min] |
| D-G4-9 | The one-fork demo target is `target.address[0..1]`. | [min] |
| D-G4-10 | Insertion depth on a bend tile = sum to the parent plus half the branch length; generation, sharp turns and lumen include the branch. | [min] |
| D-G4-11 | Lumen = 2 x `radius_mm`; a scope fits if lumen >= outer diameter. No brand names on screen. | [min] |
| D-G4-12 | Arrow: world-space floor triangle 0.20 x 0.20 m, 0.6 m ahead of the head, y = 0.01 m, no warp. Palette cells 0 (beacon) and 1 (ghost) are serialized until lane A's palette rows exist. | [min] |
| D-G4-15 | The route-cost read-out uses the built-in `TextMesh` (TextMeshPro absent); text needs a font shader (rule 8): flagged for lane A. | [min] |
| D-G4-17 | Not built (superseded or Extended): Spec §11.3.1 virtual-length clamp, Roll a Lung, Pareto threads, rehearse mode, QA, pointing probe, H5/H6/H9 (MP §10.2.7, App. D.4); "1-3 straight tiles per generation" (Extended). | plan |
| D-T-2 | P2-B06 tests: N1 forces every tenth pair to an arrived case (58 of 1,000), otherwise random tile/target pairs almost never arrive. N9 searches the depth-0..5 tiles for one whose exit is canonical edge 0, because the tree-frame/canonical mapping decides which tile that is. The arrow direction uses exact (0,+-1) for p_k, not rounded cos/sin. | [min] |
| D-T-1 | Tests: `RuleS_ForkSpacing_2_1226` uses 1e-6, not 1e-9: absolute double positions at depth 7 have 8e-9 error (kernel.md §3 rule 6). `TileFrame_MatchesTileGraph` restarts its random walk from tile zero every 50 steps, because a 2,000-step walk leaves the 64-bit range (A.5). The brief's "every open edge's neighbour is locatable" cannot hold on the window boundary (the grandparent's bend leads to the fourth ancestor): the test checks every open edge against the independent tile set and requires the current tile's neighbours to be located. | [min] |

## Hand-overs

- **H-1 (renderer, G5/G1).** Classify tiles with `AirwayTree.Window.TryLocate`; orient the kit with `FrameOf(tile)` and `OpenEdgesCanonical`; tiles outside the window are tissue.
- **H-2 (G6).** Each frame inside `ISpaceModule.Tick`: `tree.SetCurrent(tile)`, then `beacon.Tick`, then the views' `Apply`. Call `beacon.Reset(route)` when the route or beat changes. The B09 end tile is `FrameOf(route.TargetAddress)`. Mode 2 uses `Step.TreeEdge` and `ExitCanonicalEdge`, not `ArrowDirection`.
- **H-4 (lanes A, C, D).** Announce: `Atlas.Blades.Lung.asmdef` gains `Unity.Mathematics` (R7); new test assembly `Atlas.Blades.Lung.Tests`; kernel.md §4.8.

## PH-39 status

The fixture route has 6 forks (`target.address = "RRRLLR"`), not the 4 of P2-D07. Route length always comes from `tree.json` (`AirwayRoute.Forks`). P2-D07's 4-fork criterion is not claimed.

## Measured (EditMode, Apple silicon, `-nographics`)

| Item | Value |
|---|---|
| `tree.json` load and validate (131,071 nodes, 20.7 MB) | 399 ms |
| `AirwayTree` build | 18 ms |
| `FrameOf` for the 32,767 nodes of depth 14 | 456 ms |
| `SetCurrent` p95 over 4,061 calls | 0.056 ms |
| Max bit length of the six id words at 23 generations | 53 (alternating path); 51 for 2,000 random addresses (brief expected 52) |

## P2-B06 (beacon and route cost)

- Files: `Airway/AirwayBeacon.cs` (`NextStep`, `Tick`, `ArrowDirection`), `Airway/RouteCost.cs`, `Airway/Views/{ArrowMesh,BeaconArrowView,RouteCostReadout}.cs`; tests `AirwayBeaconTests` (N1-N5, N9, mesh), `RouteCostTests` (N6-N8).
- Acceptance: beacon step equals the address-prefix step on 1,000 random targets: 0 mismatches for the step, for the exit canonical edge against `TileGraph`, and for the 0.6-unit arrow probe against `TileGraph.ExitEdge` (58 arrived cases agreed). Supporting: greedy routing equals the prefix step (Sarkar) on 1,000 pairs of depth <= 11, no ties.
- Fixture facts: the lumen falls below 4.2 mm first at generation 7 (3.581 mm; generation 6 is 4.51 mm). Along the fixture route (6 forks) it ends at 4.51 mm, so all three scopes fit on the route.
- Phone items: none (both rows say "Editor"). The views are driven by the module (H-2); `BeaconArrowView.material` and `paletteCell` are set in the scene (D-G4-12).
