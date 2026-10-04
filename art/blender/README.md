# ATLAS greybox room kit

Source: `ATLAS_Kit.blend` (collection `ATLAS_Kit`). FBX exports live in `ATLAS/Assets/Atlas/Art/Kit/`.
Units are metres, on a 0.5 m grid. Exported with Y up and −Z forward, transforms baked, so pieces import into Unity at scale 1 with no rotation.

| Piece | Size | Pivot | Notes |
|---|---|---|---|
| `Tile_0p5`, `Tile_1p0` | 0.5 / 1.0 m square, 2 cm thick | floor corner (top face at y = 0) | |
| `Tile_0p5_X` | 0.5 m | floor corner | coral ✕ = start / re-anchor mark |
| `WallPanel_0p5`, `WallPanel_1p0` | 2.6 m high, 0.1 m thick | bottom-left corner | |
| `WallPanel_1p0_Window` | 1.0 × 2.6 m, window 0.6 × 0.8 m at 0.9 m | bottom-left corner | |
| `DoorFrame_Single` | opening 0.9 × 2.1 m | centre of opening, floor | child `_PortalPlane` = stencil mask quad (one-sided; use Cull Off) |
| `DoorFrame_FreeStanding` | opening 1.2 × 2.1 m | centre, floor | Post Circling branch cut; posts = branch points |
| `GluedCell_1p5` | 1.5 × 1.5 × 2.6 m open frame | floor centre | coral edges glue east↔west, teal edges glue north↔south |
| `ForkChamber_1to2` | disc r = 0.6 m, walls 2.2 m | floor centre | entrance faces −X; exits at ±50° |
| `ForkChamber_1to3` | disc r = 0.6 m, walls 2.2 m | floor centre | entrance −X; exits at −75°, 0°, +75° |
| `Corridor_Straight_1m`, `_2m` | width 0.6 m, walls 2.2 m | entrance centre, floor | runs along +X; subdivided every 0.2 m for the hyperbolic warp |
| `Corridor_Curved_L_R2p2`, `_R_R2p2` | 1.5 m along an arc of radius 2.2 m | entrance centre, floor | subdivided every 0.2 m |
| `ValveDoor` | opening 0.6 × 2.1 m | centre, floor | children `ValveDoor_LeafL` / `LeafR` pivot on their hinges (rotate about Y) |
| `BreathRing_r0p8` | ring r 0.75–0.85 m, 10 phase ticks | ring centre | coral tick at +X = phase 0 (end of exhale) |
| `WLane_2m` | 2.0 × 0.4 m strip, ticks every 0.25 m | lane centre | coral = west end (w min), purple = east end (w max) |
| `OriginX` | 0.5 m cross | centre | floor marker at the taped ✕ |

Materials (greybox palette, swap for the final palette texture later): `ATLAS_Floor` #D9D4CC, `ATLAS_Wall` #EEECE8, `ATLAS_Trim` #3A3F4B, `ATLAS_AccentA` coral #E8735A, `ATLAS_AccentB` teal #3FA7A0, `ATLAS_Portal` purple #8A6CF0.

Deviations from Engineering Spec §8.3: fork chambers are r = 0.6 m (spec ≈0.5 m) so a 0.6 m corridor fits each doorway; corridor and chamber walls are 2.2 m so rooms re-embedded behind a closed valve door stay out of sight. All pieces are far under their triangle budgets (largest: chambers at ~700 tris vs ≤3,000).
