### 10.2 Lung Mission

Phase 2, lanes B and D; the 4D medical runtime and the director of the use case (`P2-A20` to `P2-A32`) are built by lane A on its Phase-1 `Mesh4D`. Gates: G9 Lung 4D, G10 Lung airway and sky. The concept of the worldtube, slice, shadow and tilt is in §5; this section is the build spec.

#### 10.2.1 Story and claim limits

One story: a trainee plans a biopsy of one lung lesion. The lung defeats a flat static 3D picture twice: it has too many branches to fit, and it does not hold still (R2 §E). Negative curvature answers the first, a circular fourth axis the second.

Every screen of the use case carries the footer "Education and research visualisation. Not for clinical use." Each asset carries exactly one label:

| Content | Status | On-screen label |
|---|---|---|
| Tumour, lungs, heart, oesophagus, cord, trachea contours on 10 phases, patient 101_HM10395 | Real | "Public research data: TCIA 4D-Lung, CC BY 3.0. Meshes derived by ATLAS." |
| "Rigid shape" tube (phase-0 shape moved along the centroid path) | Real path, simplified shape | "Simplified: rigid shape" |
| 10 mm nodule, 15 mm motion (Lujan model) | Illustrative | "Illustrative model" |
| Airway tree (Weibel/Kitaoka-type generator) | Model-generated | "Model-generated airway" |
| Optional AeroPath proximal tree | Real, different patient | "AeroPath, CC BY 4.0, not this patient" |
| 5 mm clearance threshold, literature figures (17.6 mm, 12 ± 2 mm) | Teaching values | "Teaching threshold" / "Literature value" |

The footer, the labels and the credits panel are built by `P2-A32`; the label strings travel in each package's `manifest.json` (§10.2.4). The motion of the real case is never exaggerated: if the exaggeration slider of §12.1 (risk 24) is used, it acts on the illustrative twin only, with its factor on screen.

Not claimed: clinical benefit, low-distortion embedding (Spec §11.3.1), that the airway belongs to patient 101, that ℝ³×S¹ is curved, that space–time tilts are symmetries (R2 A.5). Wording: "target lesion" for the real case (locally advanced tumour), "nodule" only for the illustrative twin.

#### 10.2.2 Beats

`LungDirector` (lane A, `P2-A32`) is a state machine over the beats below. A beat advances when the walker completes it or on an operator `SceneCmd` from the Console, which can also enter any beat directly; each change emits `Event{ev:"lung.beat"}`. Beats a and c to f are lane A's. Beats b and g are lane B's and beat h is lane D's; the director enters those three through one interface, stubbed until they exist. The lesion chamber hand-over between b and c is `P2-B09`. The full run takes the beats in the order a to h. In the long demo slot (§13.2) the order is b on the one-fork route of §10.2.3, then a, then c to f; the short slot shows d and e only.

Twins for every action: **walk/gaze** (dwell 700 ms), **gamepad**, **Console** (operator, via `ParamSet`). The marker cube is an Extended fourth twin (R2 D.8).

| Beat | Space and why this geometry | Walker does | Number on screen | Twins (walk/gaze · pad · Console) | Acceptance test |
|---|---|---|---|---|---|
| a. Toybox recap | S6, tesseract. Same slicer as the tumour, so the three verbs are learnt on an object with known answers | Slices, looks down for the shadow, tilts once | Offset along w; 48 tets (the cross-section name appears too once the Playground's `P2-A02` exists) | step along lane · bumpers/triggers · sliders | Corner-first slice area 0.8660 at offset 0.5 and 6.9282 at 2.0, to 4 decimals (R2 D.10) |
| b. Airway wing | S2 (fallback S3). 2^g branches at constant corridor width fit only in H² | Walks 4 forks to the lesion chamber (1 fork on the demo route of §10.2.3) | Generation g; 2^16 ≈ 66,000 terminal bronchioles (R2 C.4); insertion depth in mm | walk to door · stick + A · "choose branch" | 4 forks walked by a naive walker with no rescue beacon in ≥ 9 of 10 runs (Spec §11.3.5) |
| c. Lesion chamber | Curvature ramps to flat while standing; S6 loads. "The map was one frozen slice" | Presses "unroll time": the phase-0 lesion becomes a tube | 17.6 mm, literature value (Chen 2015, R2 A.3) | dwell on plinth · A · button | Unroll completes with the 10 phase copies at k·Λ/10 ± 1 cm |
| d. Breathing worldtube | ℝ³×S¹: phase wraps, so the tube closes | Scrubs phase (slice); looks down (shadow, ρ = 0.5 shell, core) | GTV, ITV, ITV/GTV, core in cm³; "core empty" on the twin | walk/look down past −35° · bumpers, Y · sliders | On-screen volumes equal `table.json` within 1 % (R2 D.10) |
| e. Breath lane | One breath = Λ = 1.5 m along the longest clear floor segment; copies wrap because time wraps | Walks through the breath; drops two flags | Gated ITV in cm³; duty cycle in % | walk + dwell · A twice · two sliders | Tube visible in ≥ 3 copies at 60 fps; gated volume equals table entry within 1 % |
| f. Scanner artefact | Rotation of the slicing hyperplane in z–τ: tan θ = c/v | Turns the floor dial (5 detents) | Table speed v in mm/s; apparent length in mm | dial dwell · triggers · slider | 20 mm test sphere (tube generator, not the 10 mm twin), 15 mm motion, breathing period τ = 4 s, v = 20 mm/s: apparent length spans 11.9–34.8 mm ± 0.5 mm (R2 A.3) |
| g. Needle sky | S²: straight approaches to one point are the sphere of directions; head direction is a point on it | Looks at a green window; marks three directions | Minimum clearance in mm over 10 phases; robust-window share of sky in %; triangle angle sum in degrees | look + dwell · stick + A · click on sky panel | Angle sum > 180° and equal to 180° + 180°·area/π within 0.5° |
| h. Plan card | — | Reviews the card | All of the above | — | Card validates against `atlas.plancard/2`; every number traces to a logged event |

#### 10.2.3 Airway wing under free walking

**Layout (R3 D3).** The tree is snapped to the {4,5} tile graph by Rule S: each generation is one turn tile (left or right) plus one straight tile; the next fork is the tile at the end. Branch address = string over {L, R}; fork tile id = `FrameOf(address, trachea)`, exact in ℤ[φ]. Fork spacing 2.1226 units > 1.7627, so the embedding is collision-free at every depth and greedy routing is exact (Sarkar; R3 D1). Tiles not in the tree are solid tissue.

| Parameter | Value | Tag |
|---|---|---|
| L | loop-fit L from the RoomModel (never changed while walking) | §4.4 |
| Tile step (corridor, centre to centre) | 1.0613·L = s (2.0 m in a 3×3 m room) | derived |
| One generation | 2 tile steps = 2s (4.0 m at s = 2.0 m) | derived |
| Chamber | disc, radius 0.5 m, at every tree tile centre (fork chamber or bend chamber); a floor glyph of radius 0.15 m at the centre marks where to stand | Spec §11.3.1 |
| Corridor | drawn 0.8 m wide between 0.4 m kerbs plus a dithered membrane, so sibling branches are seen fanning out; a lit centre strip 0.2 m wide on the tile centre line marks where to walk, so a head on the strip stays at least 0.40 m from a real wall, 0.05 m clear of the WARN threshold | derived; R1 D.3 |
| Walkable depth | 16 conducting generations = 32 tile steps. Generations 17–23 appear on the map and counter only; 23 generations (46 tile steps) is the addressable depth. `long` tile ids suffice: 64-bit coefficients are exact to 42 straight tile steps, and on the airway's turning paths they need about 52 bits even at 46 steps (A.5) | R2 F9, R3 D3, derived |
| Lazy window | current node, 2 ancestors, descendants to depth 3: ≤ 60 nodes, ≤ 120 tiles; the drawn set is further cut by the draw rule of §8.5 (fog opaque at ρ = 3) | R3 D3 |
| Demo route | 4 forks, 8 tile steps, 8s walked (16 m at s = 2.0 m); this is the route of the G10 test. A one-fork route (2 tile steps, 2s walked) whose end tile is the lesion chamber is kept for the judged demo (§13.2) and selected from the Console (`P2-D10`); the hand-over of `P2-B09` then happens at the end of that fork | derived |
| Straight tiles per generation | 1 (Core); 1–3 by segment-length tercile (Extended; the guarantee holds) | R3 D3 |

**Why valve chambers.** A tree has no loops, so the walker cannot follow it in a small room without being re-aimed. Teleport or stick locomotion would break "walked distance tracks insertion depth" (Spec §11.3.1). Instead every tile step is really walked, and the re-aiming happens where it cannot be seen.

**Mode 1, Curved Walk (default).** Chambers land on the four corners of the lap square of the loop-fit rule, corridors on its sides; the lap square lies 0.5 m from the nearest wall or scanned obstacle (§4.4), so a run of s always exists (derived).

1. The walker enters a chamber (head within 0.5 m of the tile centre). The valve behind closes and the chamber shell turns opaque.
2. At a fork chamber the two branches are offered in the endoluminal window and as two floor glyphs; the walker chooses. At a bend chamber there is one exit.
3. `ValveSolver` picks the physical direction d*: the lap-square side from the current corner whose smallest Bump Guard clearance along its whole length (static map and dynamic layer) is larger; ties alternate. If neither side is clear to 0.30 m the valve stays closed and the turn prompt asks the walker to wait.
4. With the shell opaque, U ← U·Rot(α) about the walker so that the chosen tree edge points along d*. A floor arrow (turn prompt) points along d*.
5. One iris opens at d*; the shell returns to dithered.

The tree path is never altered; only the embedding on the floor is. Bump Guard stays the only safety system.

**Mode 2, Aligned Cells (fallback).** Each tree tile is shown in one registered cell of side g (L_eff ≈ 0.89·g). Each tree step is a cell step. In the valve state the cell's tile frame is rotated by a multiple of 90° so the chosen tree edge faces the free neighbouring cell with the longest clear run. Needs ≥ 1×2 free cells (derived).

| Criterion | Mode 1 | Mode 2 |
|---|---|---|
| Shows the claim (branches fan out at constant width) | Yes | No: each cell looks flat |
| Registered to real walls | No | Yes |
| Comfort evidence | G2 spike only (FMS ≤ 3/20) | 65 users, no sickness aborts (R1 A.6) |
| Walked distance per tile step | s | g |

Mode 1 is the default because it has passed G2 by the time Phase 2 starts and is the only mode in which the geometry is visible. Mode 2 is selected from the Console, or automatically when the walker reports FMS > 3/20 or the free rectangle gives s < 1.5 m.

**Aids and tasks.**

| Item | Specification |
|---|---|
| Beacon | Arrow to the target. Next step = prefix comparison of addresses (exact). Shown after 8 s of hesitation, except at the Two Compasses fork |
| Two Compasses | One fork chosen offline where the Euclidean compass (3D direction to the target from `pos_mm`) points down the wrong child and the beacon down the right one. Choice logged as `compass_choice` |
| Endoluminal window | True 3D view of the bifurcation from inside the model airway, rendered once per fork to a 512×512 texture, with clock positions ("take the 3 o'clock branch") |
| Route cost read-out | Insertion depth (Σ `len_mm`); sharp turns (branch angle > 45°, our definition); narrowest lumen against scope outer diameter 3.5 / 4.2 / 4.0 mm (Spec §11.3.1). In the symmetric model the lumen falls below 4.2 mm at generation 7 (derived) |
| Flat twins | Poincaré mini-map and a planar dendrogram, on the phone, the Spectator and the Console (`P2-D11`) |

**Honest limits.** The H² layout distorts 3D branch direction, so it is always paired with the endoluminal window and the dendrogram. The share of forks where the Euclidean compass is wrong is measured offline (`greedy.json`, 1,000 random targets) and that measured number is the only one quoted.

#### 10.2.4 Data and offline pipeline (Station)

Python CLI `atlas case <step>`, run on the Station before the demo; every step is idempotent and cached by input hash (Spec §12.1). It is not part of the Forge service and never on the pose path.

| Step | Command | Does | Output |
|---|---|---|---|
| 1 | `fetch` | TCIA REST `getSeries` / `getImage` for 101_HM10395, study UID in R2 C.2: 10 CT + 10 RTSTRUCT series, about 0.8 GB | DICOM cache |
| 2 | `masks` | Rasterise `Tumor_c00…c90`, lungs, heart, oesophagus, cord, trachea on the CT grid (0.977 × 0.977 × 3 mm) | masks per phase |
| 3 | `tube4d` | Masks → SDFs (1 mm isotropic, σ = 1 mm) → mean-shape template of 600 triangles → Newton projection onto each phase (5 iterations) → prisms → 3 tets each with index-sorted diagonals (R2 D.2, D.3) | `tube4d.bin`, `tube4d_rigid.bin`, `phases.glb` |
| 4 | `shells` | Union, intersection and occupancy with 4 sub-steps between phases, wrapping 9 → 0 | `itv.glb`, `core.glb`, `rho50.glb` (≤ 1.5k triangles each), `occ.r8` (64³), `table.json` |
| 5 | `chest` | Lungs, heart, trachea template with 10 blend shapes | `chest.glb` (≤ 40k triangles) |
| 6 | `twin` | Lujan z(t) = z₀ − b·cos^(2n)(πt/τ − φ₀): 10 mm sphere, b = 15 mm, n = 2, breathing period τ = 4 s, and a hysteresis loop about 3 mm wide (anterior–posterior amplitude b_AP = 4 mm, phase lag δ = 24°); then steps 3–4. Expected volumes: GTV 0.52 cm³; ITV 1.70 cm³ with the head-to-feet motion alone and about 2.2 cm³ with the hysteresis; core empty in both (`P2-D02`) | second case package |
| 7 | `airway` | Dichotomous generator: diameter ratio 0.794, length/diameter 3 ± 1, angle 37°, plane rotation 90° (R2 C.4); picks target and Two Compasses fork; runs the greedy study | `tree.json` (`atlas.tree/2`), `greedy.json` |
| 8 | `sky` | Per-phase distance fields of heart, oesophagus, cord, trachea; rays from the target (the argmax-ρ point) on HEALPix nside 64 (49,152 directions, astropy-healpix); risk = minimum clearance along the ray; robust = AND over 10 phases of clearance ≥ 5 mm; baked to equirectangular 512×256 | `sky/risk_p0…9.r8`, `sky/robust.r8`, `sky.json` |
| 9 | `pack` | Hashes, labels, licences | `manifest.json`, `CREDITS.txt` |

Package layout: `cases/<id>/{manifest.json, tube4d.bin, tube4d_rigid.bin, phases.glb, itv.glb, core.glb, rho50.glb, occ.r8, table.json, chest.glb, sky/, airway/, CREDITS.txt}`. Both packages ship inside the app build; the Hub also serves them over HTTP for updates.

| File | Format |
|---|---|
| `tube4d.bin` | Header: magic `ATL4`, version u16, nVerts u32, nTets u32, layers u16, M f32, Λ f32. Then P float32 n×4 (x, y, z, τ in metres), T int32 m×4, slab id u8 per tet. Phase 10 repeats phase 0 |
| `table.json` | GTV per phase, ITV, core (cm³); 10×10 gating table indexed [start bin][length]; centroid path (mm); argmax-ρ point; max ρ |
| `manifest.json` | `atlas.case/1`: id, kind (`real` / `illustrative`), label strings, licence, credit, M = 15, Λ = 1.5 m, file hashes |
| `tree.json` | `atlas.tree/2`: per node `address`, `gen`, `len_mm`, `radius_mm`, `pos_mm`, `dir`, `source: "model"`; `target.address`; `compass_fork.address`. No `parent_iso`, no `R_m` |

Credits (required): Hugo et al. 2016, *Data from 4D Lung Imaging of NSCLC Patients*, TCIA, doi 10.7937/K9/TCIA.2016.ELN8YGLE, CC BY 3.0, plus the TCIA citation; AeroPath (a different patient from the tumour case) under CC BY 4.0 if used (R2 F8). Shown in the app credits and the submission page. The TCIA data-use terms are acknowledged, and the citation is recorded in the licence ledger, by G9 (open item 18, §14).

#### 10.2.5 Runtime components and budgets

| Component (lane) | Budget | Tag |
|---|---|---|
| `TubeLoader`, `TubeSlicer` on `Mesh4D` (A) | 18k tets per period; ≤ 1.0 ms at the 95th percentile, 0 ms when the hyperplane is unchanged; one slab in ten touched for a pure phase slice. Above 1.5 ms: cut the template to 300 triangles | R2 D.4, D.10 |
| Slice mesh | ≤ 36k triangles, mostly degenerate; fixed slots, `SetVertexBufferData` | R2 D.10 |
| Shells | 3 × 1.5k triangles | R2 D.10 |
| Ghost phase meshes | 10 × 600 triangles; gated shadow = selected phases drawn opaque in one colour | R2 D.6 |
| Chest | ≤ 40k triangles, one material | R2 D.10 |
| Lane copies | 5–7 × 3k triangles | R2 D.10 |
| 4D scene total | ≤ 240k triangles and < 80 draw calls per frame (both eyes); a use-case scene, inside the frame budget of §8.5 | R2 D.10 |
| Memory | tube 0.4 MB, slice buffer 1.2 MB, occupancy 0.26 MB | R2 D.10 |
| Airway wing (B) | Inside the frame budget (≤ 300 draw calls and ≤ 500k triangles per frame, both eyes); target ≤ 150 draw calls per frame; must be measured | §8.5 |
| Needle sky (B) | One inverted sphere, 11 R8 textures (1.4 MB), 0 ms compute per frame | derived |

Phase colour: one cyclic row in the shared palette, u = frac(τ/Λ), inhale cool, exhale warm, lightness varied (R2 D.7). No self-avatar copies on the lane.

#### 10.2.6 Measures and tasks

| Task | Tier | Walker does | Score | Logged (`Event`, scene `lung`) |
|---|---|---|---|---|
| M2 Needle dwell point | Core | Places a point | Hit fraction H = ρ(p) from `occ.r8`; best = max ρ | `m2_place {p_mm, hit, best}` |
| M4 Gating window | Core | Chooses a phase arc | ITV reduction against the best arc of equal duty | `m4_gate {start, len, itv_cm3, duty, best_cm3}` |
| M3 Spot the artefact | Core | Picks the true shape among three | Correct or not, before and after beat f | `m3_pick {choice, correct, when}` |
| M1 Margin fit | Extended | Sizes an ellipsoid | Coverage C = min over phases; excess O = volume/ITV; pass if C ≥ 0.95 | `m1_fit {radii_mm, C, O}` |

Also logged: `fork {address, choice, correct, beacon_shown, t_s}`, `compass_choice`, `sky_pick {az_deg, el_deg, clear_mm, robust}`, `gc_check {angle_sum_deg}`, walked distance, FMS each minute, and the Single Ease Question at the end of the run (`seq`, typed into the Console field of `P2-C17`). The Hub writes `events.jsonl`. A pilot compares a static 3D model with the 4D view within subjects; the only claim permitted is "in a pilot with n people, hit fraction changed from X to Y" (R2 B.3).

#### 10.2.7 Corrections applied to the older documents

| Older text | Applied here | Tag |
|---|---|---|
| Slice Tilt in the time–left-right plane | Scanner tilt in z–τ, tan θ = c/v | R2 F1 |
| Seven precomputed tilted slices, blended | Live CPU slice; no blending | R2 F2, F3 |
| GTV and organs on all phases for all patients | True for patient 101 only among those needed; patient 112 excluded | R2 F4 |
| ITV(arc) = OR of 10 masks | 4 sub-steps between phases | R2 F5 |
| "≈ 17 mm CT-to-body divergence" | 17.6 mm is nodule displacement (Chen 2015), one contributor; labelled literature value | R2 F6 |
| 1.5 m glued cell, 0.75 m ring in the 3×3 m square | Breath lane on the longest clear segment; ring as fallback | R2 F7 |
| "≈ 23 generations" | About 16 conducting, 23 in all | R2 F9 |
| 12 mm illustrative motion expected from the real data | Real tube is subtle (6.8 mm SI); the twin is the dramatic one | R2 F10 |
| Wedge layout at R = 2.2 m, ℓ = 1.5 m (372 crossings by depth 8); parent-relative isometries | Rule S on the tile graph; addresses | R3 point 8, 9 |
| Sphere tracing on the phone; TotalSegmentator vessels | Precomputed sky from contoured organs; vessels Extended | derived |
| Roll a Lung live build, tile solver, H200 batch, kimimaro/skan skeleton | Dropped; offline generator on the Station | BP §0.5 |

#### 10.2.8 Work packages

Lane length is written Λ to keep L for the curvature radius. τ is the time coordinate along the lane, in metres; where it is called the breathing period (the Lujan curve of §10.2.4 and the scanner-tilt test) it is that period in seconds.

| ID | Work package | Lane | Depends on | Output (files, components, interfaces) | Acceptance test (binary, with numbers) | Needs (Editor / phone in hand / phone in headset / Station) |
|---|---|---|---|---|---|---|
| P2-D01 | Case contract | D | G7 Phase-1 exit | `docs/case_contract.md`; C# and Python readers for `tube4d.bin`, `table.json`, `manifest.json` | A 2-tet fixture round-trips Python → C# byte-identical | Editor, Station |
| P2-D02 | `atlas case twin` | D | P2-D01 | Illustrative package (steps 6, 3, 4) | GTV 0.52 cm³ within 3 %; with the hysteresis off (b_AP = 0) ITV 1.70 cm³ within 3 %; with b_AP = 4 mm and δ = 24° ITV about 2.2 cm³ (pin the value from the first run of the pipeline's own 0.25 mm voxel union, then hold it within 3 %); core volume 0 in both | Station |
| P2-D03 | `atlas case fetch`, `masks` | D | P2-D01 | DICOM cache; masks | 20 series fetched; tumour centroid range 3.1 / 3.1 / 6.8 mm within 1 mm | Station |
| P2-D04 | `atlas case tube4d` | D | P2-D03 | `tube4d.bin`, `tube4d_rigid.bin`, `phases.glb` | 18,000 tets; no cracks (every interior face shared by exactly 2 tets); slice at each phase within 1 mm Hausdorff of that phase's contour mesh | Station |
| P2-D05 | `atlas case shells` | D | P2-D04 | shells, `occ.r8`, `table.json` | ITV 47.4 cm³ and core 17.2 cm³ within 5 % (R2 C.2) | Station |
| P2-D06 | `atlas case chest` | D | P2-D03 | `chest.glb` | ≤ 40k triangles; lung volume 4.42 L at 0 % and 3.87 L at 50 % within 3 % | Station |
| P2-D07 | `atlas case airway` | D | P2-D01 | `tree.json`, `greedy.json` | 16 generations addressable; diameter ratio 0.794 ± 0.08; exactly 1 Two Compasses fork on the 4-fork route | Station |
| P2-D08 | `atlas case sky` | D | P2-D03 | `sky/*`, `sky.json` | `robust.r8` equals the pixel-wise AND of the 10 phase maps (0 differing pixels); HEALPix indices match astropy-healpix on 1,000 random directions | Station |
| P2-D09 | `atlas case pack` and delivery | D | P2-D02, P2-D05, P2-D06, P2-D07, P2-D08 | Two packages in the build; Hub route `/cases/<id>/…` | Hash check passes for every file; package loads from the build in ≤ 2 s with the network off | Phone in hand, Station |
| P2-D10 | Lung Console panel | D | P1-D06, P2-D01 | Sliders for phase, tilt, gating arc; branch and sky buttons; route switch (four forks, or the one-fork demo route); label preview | Each control changes the phone state in ≤ 250 ms | Phone in hand, Station |
| P2-D11 | Lung twins on Spectator and Console | D | P2-D07, P1-D05, P1-D07 | Dendrogram and Poincaré map with the walker's address; lane strip with flags | Address shown equals the phone's within 1 frame of the `fork` event | Station, phone in hand |
| P2-D12 | Plan card | D | P2-D10 | `atlas.plancard/2` writer on the phone; card page on the Hub with print stylesheet | Card validates against the schema; contains both `not_for_clinical_use: true` and the case label; prints on one page | Station, phone in hand |
| P2-D13 | Event logging and pilot export | D | G7, P1-D01 | `events.jsonl` → `lung_summary.csv` per session | All 7 Core event types of §10.2.6 (`m2_place`, `m4_gate`, `m3_pick`, `fork`, `compass_choice`, `sky_pick`, `gc_check`) present after one full run | Station |
| P2-A20 | `TubeLoader` | A | P1-A12, P2-D01, P2-D02 | Loads `tube4d.bin` into `Mesh4D` | Illustrative tube: slice at θ = 0 has area within 2 % of 4πR² | Editor |
| P2-A21 | Live slicer views | A | P2-A20 | `TubeSlicer` with dirty flag and slab skipping | ≤ 1.0 ms at the 95th percentile over 1,000 moving-slice frames; 0 allocations | Phone in hand |
| P2-A22 | Phase colouring | A | P2-A21 | Palette row, `tau` vertex channel | Colour at τ = 0 and τ = Λ identical (ΔRGB = 0) | Editor |
| P2-A23 | Shells and look-down shadow | A | P2-A20, P2-D02 | ITV, ρ = 0.5, core; fade at pitch < −35° | Displayed volumes equal `table.json` within 1 % | Phone in headset |
| P2-A24 | Breathing chest | A | P2-D06 | Blend-shape player, wrap 9 → 0 | No visible jump at the wrap (vertex step ≤ 1 mm at display scale) | Editor |
| P2-A25 | Breath lane | A | P2-A21, P1-C06 | Lane on the longest clear segment; 5–7 copies; phase stripes | ≥ 3 copies visible at 60 fps; lane lies inside free floor with ≥ 0.5 m clearance | Phone in headset |
| P2-A26 | Unroll animation and "now" plane | A | P2-A25 | Beat c sequence | 2 of 3 naive viewers answer "the tumour over time" unprompted (R2 D.9); else enable the ring | Phone in headset |
| P2-A27 | Gating flags | A | P2-A25, P2-A23 | Two flags; gated shadow; duty | Volume equals table entry within 1 %; duty = arc length/10 | Phone in headset |
| P2-A28 | Scanner tilt | A | P2-A21 | z–τ dial, 5 detents, v read-out | On a 20 mm test sphere with 15 mm motion and a breathing period τ = 4 s, from the tube generator of `P1-A12` (not the 10 mm twin): apparent length spans 11.9–34.8 mm ± 0.5 mm at v = 20 mm/s | Editor |
| P2-A29 | Tasks M2, M3, M4 | A | P2-A23, P2-A27, P2-A28 | Task UI, scoring, events | M2 hit equals `occ.r8` sample within 0.01; three event types emitted | Phone in headset |
| P2-A30 | Ring fallback | A | P2-A22 | 10 phase meshes on a 0.75–1.0 m circle | Fits the largest free disc of the RoomModel; 60 fps | Phone in headset |
| P2-A31 | Lung 4D soak | A | P2-A29, P2-A32, P2-D09 | Soak report | 95th-percentile frame time ≤ 17.5 ms with the slice moving, on both packages; thermal state never "Serious" | Phone in headset |
| P2-A32 | Lung director, labels and credits | A | P2-D09, P2-A22, P2-A24, P2-A26, P2-A29, P2-A30 | `LungDirector.cs` (beats a–h by condition and by `SceneCmd`, `lung.beat` event; beats b, g and h are entered through an interface that lanes B and D implement, stubbed until then), the footer on every screen, one label per asset from `manifest.json`, credits panel | Beats a and c–f advance both ways on both packages; the footer is present on every beat; every content class of §10.2.1 present in a package shows exactly its label; the credits show the TCIA citation; one `lung.beat` event per beat in `events.jsonl` | Phone in headset, Station |
| P2-B01 | `AirwayTree` | B | P1-B04, P2-D07 | Address → tile id (`FrameOf`), lazy window | 32,767 nodes to depth 14 map to distinct ids; window ≤ 60 nodes | Editor |
| P2-B02 | Corridor and chamber kit | B | G7, P1-B05 | Fork, bend and straight tile meshes with kerbs, membranes, iris, the lit centre strip (0.2 m wide) and the chamber's standing glyph (radius 0.15 m) | Tile meshes come from the `P1-B05` generator and keep its levels of detail, its 16 boundary segments per edge and its triangle caps (§8.4); kerbs, membranes and iris have no edge longer than 0.25 m; wing ≤ 150 draw calls per frame (both eyes) at ρ = 3 | Editor |
| P2-B03 | `ValveSolver`, Mode 1 | B | P2-B01, P1-C06, P1-C09 | Steps 1–5 of §10.2.3 | 100 simulated 8-step routes in 3 RoomModels: head always ≥ 0.4 m from walls; rotation applied only while the shell is opaque; with one side blocked in the dynamic layer the other side is chosen, and with neither side clear to 0.30 m the valve stays closed, in 20 of 20 simulated cases | Editor |
| P2-B04 | Valve walk, Mode 2 | B | P2-B01, P1-B10 | 90° frame rotation per cell | Same 100 routes complete with ≥ 1×2 cells | Editor |
| P2-B05 | Mode switch | B | P2-B02, P2-B03, P2-B04 | Console switch; automatic rule | Switch at a chamber keeps the address unchanged | Phone in headset |
| P2-B06 | Beacon and route cost | B | P2-B01 | Arrow, 8 s rule, read-out | Beacon step equals the address-prefix step on 1,000 random targets | Editor |
| P2-B07 | Endoluminal window | B | P2-D07 | 512×512 render per fork, clock labels | Render cost in 1 frame only; 0 ms afterwards | Phone in hand |
| P2-B08 | Two Compasses | B | P2-B06, P2-B07 | Fork UI, `compass_choice` event | Compass and beacon point to different children at the designed fork | Phone in headset |
| P2-B09 | Lesion chamber hand-over | B | P2-B02, P2-B03, P2-A26 | Curvature ramp to flat over 3–5 s while standing; S6 load; triggered at the end tile of the selected route (four forks, or the one-fork demo route) | No frame > 33 ms during the hand-over | Phone in headset |
| P2-B10 | Needle sky | B | P2-D08 | Sky sphere, phase and robust layers, dwell pick, read-outs | Clearance shown equals `sky.json` within 0.1 mm at 20 test directions | Phone in headset |
| P2-B11 | Great-circle check | B | P2-B10 | Three arcs, angle sum | Octant triangle gives 270° ± 0.5° | Editor |
| P2-B12 | Airway run test | B | P2-B05, P2-B08 | Run report | 4 forks without rescue in ≥ 9 of 10 naive runs; FMS ≤ 3/20; walking the centre strip for 8 tile steps raises no WARN on straight sides | Phone in headset |

G9 = P2-A31 passed with both packages (which needs P2-A32 and P2-D09). G10 = P2-B12, P2-B09, P2-B10 and P2-B11 passed, all eight beats of P2-A32 run end to end, P2-D11 and P2-D13 verified, and P2-D12 printed from a real run.
