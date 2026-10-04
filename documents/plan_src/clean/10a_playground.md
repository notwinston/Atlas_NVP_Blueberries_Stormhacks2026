### 10.1 Geometry Playground

#### 10.1.1 Purpose

Promise: **"your own room, bent"**. The walker scans a room, sees it rebuilt 1:1, turns one dial, and walks the same floor in spherical, flat-wrapped, hyperbolic, portal-stitched and 4D space. The Playground adds no new geometry: it packages the Phase-1 spaces S0–S6 into one guided run in which every claim is backed by a live number. Built in Phase 2 by lanes C and A: lane C owns the shell and the Glued and Impossible Rooms exhibits, lane A the Slicer toybox, the equation sources and the look; lane B hands over S2, S3, S4, the dial and the 3-4-5 walk; lane D supplies `Event` transport, the flat-twin mini-map and the Station views. Exit gate: G8 Playground run.

#### 10.1.2 The flow

`PlaygroundDirector` (lane C) is a state machine over the beats below, stored in `Beats.asset`. A beat advances on its exit condition or on an operator `SceneCmd` from the Console, and each change emits `Event{ev:"pg.beat"}`.

| # | Beat | What happens | Exit condition |
|---|---|---|---|
| 1 | Scan | Phone in hand; walker sweeps the room and confirms the floor outline (D21). Pre-scan + ARWorldMap is the fast path. | RoomModel valid |
| 2 | Home appears | S0 drawn 1:1. Each object box shows a placeholder tinted by category; Forge meshes arrive and replace them one by one (the built-in fallback set of `P1-D12` if the Forge cannot be reached). | Walker confirms; Local Network prompt already answered (BP §0.5) |
| 3 | Headset on | Phone into the headset; stereo starts. | Head pose stable |
| 4 | Centre | A floor disc marks the free-rectangle centre c0. | Head within 0.3 m of c0 (derived) |
| 5 | Dial | Rooms per corner q = 3, 4, 5, 6: Small Planet (R = 2s/π), flat, {4,5} (L = s/1.0613), {4,6} (L = s/1.3170). Walls sink to kerbs when q ≠ 4. | q = 5 selected |
| 6 | 3-4-5 walk | Beacons lead the loop of side s (§3.3): corner counter, distance from the start flag, and "world turned by N°". The walker repeats it at q = 3 and q = 4. The pointing probe is taken after the third leg at q = 4 and at q = 5 (below). | Loop closed at q = 5 |
| 7 | Free Curved Walk | S2, Mode 1. Mini-map revealed after the first lap; home beacon on. | Walker opens the mode chip |
| 8 | Compare | Switch to S3, Mode 2. Caption: "smooth curvature" against "curvature at the corners". | Walker opens the menu |
| 9 | Exhibit menu | Signs in Home for Glued, Impossible Rooms, Slicer. | Exhibit chosen, or done |
| 10 | Return Home | Kernel reset to tile zero, κ ramps to 0, S0 registered again. | End of run |

Two measures are taken during the run without adding a beat (`P2-C17`). **Pointing probe:** after the third leg of the walk at q = 4 (flat, S0) and again at q = 5 (S2) the walker looks at where they started and presses A, or dwells; the app logs the head ray beside the geodesic and the Euclidean prediction as a `probe` event. **Single Ease Question:** at the end of the run the walker is asked for a score from 1 to 7 and the operator types it into the Console; it is logged as `seq`. Both fill the comfort and usability rows of §11.2.

#### 10.1.3 Exhibit cards

**What the walker meets**

| Exhibit | Caption chip (the rule) | The "aha" | Tasks | Registered to the real room? |
|---|---|---|---|---|
| S1 Glued | "Every wall is a door to its opposite wall. Flat, but it wraps." | You see your own back; in the Klein rows your copy is mirrored. | Wrap-walk through one cell edge; wave at your copies; switch to Klein and compare the chiral landmark; switch to T³ and look up. | Yes. Universal cover fixed to the floor: an open-frame cell, sized from the RoomModel (§4.3), repeated 5 × 5 (T², Klein) or 3 × 3 × 3 (T³, vertical period 2.6 m; Spec §11.2). The room is drawn once: its walls (as kerbs) and furniture stand where they really are, and the copies of the cell fill the space around them. |
| S2 Curved Walk | "Five rooms meet at every corner. Constant negative curvature." | Four right turns do not bring you home; a fifth does. | Walk the loop; follow a spanning object across a tile edge; reach a region tower; use the home beacon to return. | Only at entry. Tile zero is the room, then it drifts, so walls are 0.9 m kerbs and corner posts (five wedges per post, each 72° in the curved metric). Bump Guard is the only protection. |
| S3 Aligned Cells | "Each cell is flat. All the curvature sits at the corners." | Left of a corner post is a different room from right of it. | Circle one corner post: four cells pass, a fifth room appears. | Yes, permanently. Cell grid from the RoomModel; furniture stays put; a post at each interior corner. Needs ≥ 3×2 cells for unbounded exploration; below that, the five-tile corner demo (R1 §D.4). |
| S4 Small Planet | "Three rooms meet at every corner. Six tiles, then it repeats." | Three right turns close the loop; the whole world is six rooms. | Three-corner loop; watch the cube-net twin light each room you enter and count how many of the six you reach; find the pole beacon. | No. Six tiles always loaded; fog starts 0.5π, opaque 0.8π; fragments beyond 2.6 rad discarded; props ≤ 1.2 m (R6 §2.5). |
| S5 Impossible Rooms | "Flat rooms joined so that they overlap." | A 1 m booth contains a hall; four right turns arrive somewhere new. | Enter the booth; walk the loop corridor and read the room number. | Yes. Authored in play-space coordinates; hall = free rectangle; corridor width clamp(Wf/3, 0.7, 1.0 m). Loop only when min(Wf, Df) ≥ 2.4 m, else booth → hall (R6 §3.5). |
| S6 Slicer toybox | "A 3D slice of a flat 4D shape." | A solid changes shape when moved along a direction you cannot point to. | Slice the tesseract corner-first: tetrahedron → truncated tetrahedron → octahedron; look down for the shadow; rotate in a plane containing w; repeat on the 5-cell and 16-cell. | Pedestal at c0, registered. This exhibit is the onboarding for the Lung Mission's three verbs (R2). |

**What is measured**

| Exhibit | Equation card (live numbers) | Events (`ev`) | Acceptance test |
|---|---|---|---|
| S1 | `x mod a`, `z mod b`; cell a × b; wraps; copy parity (−1)^row | `glued.wrap`, `glued.mode` | No seam in either eye on 10 of 10 crossings; avatar in all 24 other copies of 5×5; Klein copy determinant = (−1)^row in 100% of copies. |
| S2 | `cosh(s/2L) = √2·cos(π/q)` with live s, L, q; laps; world turned N° | `walk.corner`, `walk.lap`, `walk.closed`, `tile.cross` | Five-corner loop closes within 0.25 m; read-out 90° ± 5° after one lap at loop-fit L (R1 §D.1). |
| S3 | corner curvature `2π − q·π/2 = −90°`; `L_eff = 0.89·g` | `cells.cross`, `cells.postLoop` | After four cell crossings round one corner post the tile id differs from the start; after five it matches. |
| S4 | `R = 2s/π`; triangle angle sum 270°; predicted: straight ahead, home is 4s away (2πR = 4s) | `walk.*` | Three-corner loop closes within 0.25 m; no geometry smear at the antipode in 8 headings. |
| S5 | virtual floor walked ÷ free-rectangle area | `portal.cross`, `rooms.loop` | Hall visible through the booth door before entry; no pop when either eye crosses the portal, 10 of 10. |
| S6 | `w₀`, rotation angle, section name, face count | `slicer.w`, `slicer.rot`, `slicer.shadow`, `slicer.poly` | Section named "octahedron" with area 6.9282 ± 0.001 at x+y+z+w = 2.0 (R2); tesseract slice ≤ 0.5 ms at the 95th percentile (§11.2). |

**Twins.** Hand tracking is not in the Core build (rule 5 of §0.5). Every action has a gamepad input and a gaze-dwell input, and every action but the shadow also has a headset-lever input; all are mapped in `ActionMap.cs`. Gaze dwell is 700 ms (Spec §4.5).

| Action | Gamepad | Gaze dwell | Headset lever |
|---|---|---|---|
| Confirm / next beat | A | prompt chip | click |
| Dial q | D-pad left/right | floor dial, four detents | click cycles 3→4→5→6 |
| Mode 1 ↔ Mode 2 | Y | mode chip | double click |
| Exhibit menu, pick | Start, stick + A | exhibit sign | long press opens, click steps, long press picks |
| Gluing T² / Klein / T³ | bumpers | gluing sign | click |
| Slice w | bumpers | ends of the w-ruler | hold to sweep |
| Rotate in a w-plane | triggers (or marker cube pitch) | floor dial, five detents | double click steps 15° |
| Shadow | X | look down past −35° (R2) | — (gaze twin only) |
| Return Home | B held | home beacon | triple click |

#### 10.1.4 Comfort and legibility rules

| Rule | Value | Source |
|---|---|---|
| Real walking only; gains at identity; no vignette | Always | BP §0.5 |
| Curvature changes only while standing | Dial locked when head speed > 0.1 m/s (§4.6); κ ramps over 4 s | R1 §E.3 |
| Fog fully opaque at a fixed distance | S2: ρ = 3, and every tile that reaches inside that radius and can be in view is drawn. S1: 2 cells for the 5 × 5 lattice and 1 cell for 3 × 3, with the vertical period of T³ not fogged (§8.5). S4: 0.8π | §8.5; R6 §5.1 |
| Tiles spawn only beyond the fog | Always | R6 §5.2 |
| Corner landmark at every tile vertex; ≥ 1 spanning object within ρ ≤ 2 of entry; tower within ρ ≤ 2.5; ≥ 4 region colours | WorldLint pass | R6 §5.1, §5.3 |
| Walls ≤ 0.9 m in curved spaces | Always | R6 §5.1 |
| Caption chip at every entrance; a flat space is never called curved | Always | Brief §4.0 |
| Mini-map and UI anchored to the floor or body, never warped | Always | R1 §E.3 |
| FMS | Asked aloud once per minute; operator types it into the Console; logged as `fms`; stop value `pg.fmsStop` set from the G2 spike (must be measured) | BP §0.5 |
| Bump Guard | Warning cue + haptic only, in every space | D19 |

#### 10.1.5 Work packages

Files live under `Assets/Atlas/Blades/Playground/Exhibits/` and `Assets/Atlas/Blades/Playground/Shell/`. Exhibits wrap the Phase 1 spaces through `ISpaceModule`; a change needed inside a space is requested from the lane that owns it. Lane A's later ids (`P2-A20` onwards) belong to the Lung Mission (§10.2.8).

| ID | Work package | Lane | Depends on | Output (files, components, interfaces) | Acceptance test (binary, with numbers) | Needs (Editor / phone in hand / phone in headset / Station) |
|---|---|---|---|---|---|---|
| P2-A01 | Slicer toybox | A | P1-A12 | `ToyboxExhibit.cs`, w-ruler, cell colours (8 / 5 / 16) | Area test 6.9282 ± 0.001; tesseract slice ≤ 0.5 ms at the 95th percentile on phone A (should: ≤ 0.2 ms) | Editor, phone in headset |
| P2-A02 | Section names and shadow | A | P2-A01 | `SectionNamer.cs` (face-count table), look-down shadow | Correct name at five test offsets per polytope; shadow toggles at −35° ± 2° | Editor |
| P2-A03 | Exhibit equation sources | A | P2-C12, P2-C15, P2-A01, P2-C06 | `IEquationSource` for S1, S5, S6 | Card values equal logged values to 3 significant figures | Editor |
| P2-A04 | Look polish | A | P2-C12, P2-C13, P2-C14, P2-C15, P2-C16, P2-A01, P2-A02 | Kerb and post meshes, fog tints, emissive signs, audio cues | Frame p95 ≤ 17.5 ms, ≤ 300 draw calls, ≤ 500k triangles in every beat | Phone in headset, Station |
| P2-C01 | Director and beats | C | G7, P1-D01 | `PlaygroundDirector.cs`, `Beats.asset`, `pg.beat` | All 10 beats advance by condition and by `SceneCmd`; 10 events in `events.jsonl` | Editor, Station |
| P2-C02 | Scan-to-Home | C | P2-C01, P1-C06, P1-D10 | Beats 1–3; placeholder → mesh swap | Every object box shows a placeholder or a mesh (100%); a swap moves the box centre < 0.05 m | Phone in hand, Station |
| P2-C03 | Centre mark and dial hand-off | C | P2-C01, P1-B09, P1-B11 | Floor disc, standing lock | Dial ignored above 0.1 m/s in 10 of 10 tries; q = 3…6 each load | Phone in headset |
| P2-C04 | Guided 3-4-5 walk | C | P2-C03, P1-B09 | Beat wiring of the `P1-B09` loop markers and read-outs, floor arrows, `walk.*` events | Closure < 0.25 m at q = 3, 4, 5; 90° ± 5° per lap at q = 5 | Phone in headset |
| P2-C05 | Mode compare | C | P2-C04, P1-B06, P1-B10 | Mode chip, mini-map reveal after lap 1, home beacon | Switch keeps the tile id; beacon distance matches the kernel within 0.1 m | Phone in headset |
| P2-C06 | Caption chips and equation cards | C | P2-C01 | `CaptionChip.cs`, `EquationCard.cs`, `IEquationSource`, sources for S0, S2, S3, S4 | Chip shown at 6 of 6 entrances; card refresh ≥ 10 Hz | Editor |
| P2-C07 | Action twins | C | P2-C01 | `ActionMap.cs`, dwell reticle | Full run completed three times, once per input alone | Phone in headset |
| P2-C08 | Exhibit menu and return Home | C | P2-C01, P2-C12, P2-C15, P2-A01 | `ExhibitMenu.cs`, kernel reset | After return, Home walls within 0.05 m of the RoomModel | Phone in headset |
| P2-C09 | Tutorial prompts and FMS | C | P2-C01, P1-D06 | `PromptQueue.cs`, `fms` event, `pg.fmsStop`, operator alert when an entry ≥ `pg.fmsStop` | One prompt per beat; FMS asked every 60 s ± 5 s; the entry appears in the log | Phone in headset, Station |
| P2-C10 | Bump Guard check in every space | C | P1-C09, P2-A04 | `docs/evidence/bumpguard_spaces.md`: the scripted approach test of §11.4.5 repeated in S0–S6 with the same fixed thresholds | In each space the warning starts ≥ 0.6 s before predicted contact in 10 of 10 wall approaches; no STRONG cue and no WARN on the straight sides in one clear-floor 3-4-5 walk (corner WARNs are counted and reported, as in `P1-C09`) | Phone in headset |
| P2-C11 | Playground run (G8) | C | P2-A03, P2-C02, P2-C05, P2-C07, P2-C08, P2-C09, P2-C10, P2-C17, P1-D05, P1-D07 | Run script, operator checklist | Three complete runs in two rooms with no restart; Spectator shows every beat | Phone in headset, Station |
| P2-C12 | Glued exhibit: wrap-walk | C | G7, P1-A09, P1-A15, P1-C06 | `GluedExhibit.cs`: wrap counter, `glued.*` events, exhibit tasks (the space itself is `P1-A09`; the cell size is read from the RoomModel) | `glued.wrap` logged on 10 of 10 crossings; the counter equals the number of crossings; cell sides equal the RoomModel `cells` value | Editor, phone in headset |
| P2-C13 | Chiral kit | C | P2-C12 | Prompts for the chiral comparison, using the landmark and the avatar of `P1-A09` | Determinant test 100%; avatar in 24 copies | Editor |
| P2-C14 | Gluing switch and T³ | C | P2-C12 | Gluing sign and input twin bound to the T², Klein and T³ modes of `P1-A09` | Looking up > 60° in T³ shows the avatar; S1 stays within its rows of §8.5 | Phone in headset |
| P2-C15 | Impossible Rooms exhibit | C | G7, P1-A11, P1-C06 | `RoomsExhibit.cs`: exhibit tasks and the fallback prompt over the `P1-A11` layouts | Every footprint inside the eroded floor (WorldLint physical fit 100%) for three RoomModels | Editor |
| P2-C16 | Loop corridor and room numbers | C | P2-C15 | Room-number signs, `rooms.loop` event | Room number after four turns ≠ start in 10 of 10 runs; fallback chosen when min(Wf, Df) < 2.4 m | Phone in headset |
| P2-C17 | Pointing probe and SEQ | C | P2-C04, P2-C09, P1-D06 | `PointingProbe.cs`: after a 3-leg walk in S0 and in S2 the walker looks at where they started and presses A (gaze-dwell twin); logs `probe {ray_az, pred_geodesic, pred_euclid, err_geo, err_euc}` (Spec §13.1); Console SEQ field (1–7) logged as `seq` | In replay both predictions match the kernel values within 2° (proposed); `probe` and `seq` events appear in `events.jsonl` for S0 and S2 | Phone in headset, Station |

G8 = P2-C11 passed; every other package in this table is upstream of it.
