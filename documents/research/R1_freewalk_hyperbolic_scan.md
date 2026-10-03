# R1 — Free walking in a scanned room mapped into hyperbolic space (H²×E)

Research memo for the ATLAS lead engineer. Date: 3 Oct 2026.
Scope: replaces the fixed, taped 3×3 m square (Spec §6.4, Brief §3.7) with FREE WALKING in a room the user SCANS with the iPhone 15 Pro Max.

**Claim tags**
- **[V]** = verified: fetched primary source (paper PDF, source code, Apple/Unity doc page). URL is inline.
- **[S]** = secondary (search snippet, abstract summary, or an existing ATLAS doc).
- **[D]** = my derivation. Every formula tagged [D] was also checked numerically (scripts in `research/scripts/R1_geo*.py`).
- **[E]** = estimate / engineering judgement.

---

## 0. Bottom line

1. **The geometry cannot be beaten, only chosen.** With 1:1 walking in a smooth hyperbolic plane, the real floor and the virtual floor cannot stay registered. You must pick one of: (a) exact local 1:1 motion with no registration (rolling), (b) registration with curvature pushed into discrete corner points (Holonomy cells), or (c) registration with non-1:1 motion (a fixed chart). [D]
2. **Holonomy (TU Delft) is option (b), not a hyperbolic renderer.** It draws flat Euclidean 1 m cells and changes which tile each cell shows. The authors say this was done "to avoid the need to implement a full hyperbolic render engine" and because true hyperbolic VR was judged unpleasant. Our decided log-map warp is the Hart/Segerman and HyperRogue-VR approach instead. [V]
3. **Recommended primary: Option 1 "Rolling room"** — continuous rolling H²×E (already decided), the scan sets the curvature radius L and the tile content, and the real room is drawn as an always-present Euclidean "ghost room" in tracking space that doubles as the guardian. The mismatch between ghost room and hyperbolic room IS the demo: one lap of the room turns the world by up to 90°.
4. **Recommended fallback: Option 2 "Holonomy cells"** — the scanned free floor rasterised into cells that stay registered with the real walls; flat rendering plus per-instance clip "gates". It needs no warp shader and shares the tile-id code, the scan and the guardian.
5. **Curvature radius:** use the loop-fit rule `L = s / 1.0613` where s is the side of the square the user can comfortably lap (s ≈ min(W, D) − 1.0 m). This gives L ≈ 1.9 m in a 3×3 m room and 2.8 m in a 4×5 m room, and makes "five right-angle turns bring you home" exact. [D]
6. **A hard trade-off the team must decide:** gentle curvature (L ≈ 2–3 m) feels good but gives only local exploration by walking; unbounded exploration by walking needs at least two separate lap loops inside the room, which in 3×3 m forces L ≈ 0.94 m (the Holonomy regime). A 4×5 m room gives both at L ≈ 1.9 m. [D]
7. **Scan:** AR Foundation 6.x exposes RoomPlan only as furniture bounding boxes. Walls, doors and windows need a Swift plugin, or can be derived from ARKit planes and the classified LiDAR mesh. [V] Do the mesh/plane route first.
8. **First spike (≤ 2 h):** CPU-only rolling with line-drawn tiles and a hand-entered ghost room, on the device. Walk the five-leg pentagon and three free minutes at L = 1.0, 1.9, 2.8 m. It tests the maths, comfort and the guardian before anyone ports a shader.

---

## A. Holonomy (TU Delft) — what it actually does

Sources fetched and read in full:
- Skrodzki, Jochems, Rijsdijk, Snellenberg, Bidarra, "Holonomy: A Virtual Environment based on Hyperbolic Space", Web3D 2024 — https://arxiv.org/abs/2409.05460
- The 2022 TU Delft software-project report "Holonomy: a non-Euclidean labyrinth game in virtual reality" (Yarar, Bakker, Snellenberg, Slotboom, Li) — https://repository.tudelft.nl/file/File_5b453a96-1287-44da-862b-e2a1c7ef393e
- Slotboom 2023, "Rendering Non-Euclidean Space in Virtual Reality Using Portals" — https://resolver.tudelft.nl/uuid:efc67ab5-16bb-4f01-9478-7cee03968257 (PDF: https://repository.tudelft.nl/file/File_6caa1e43-0353-419f-b07b-2b598b224731)

### A.1 Mapping of the physical 3×3 m space

- The move area is "roughly a square of at least 3 × 3 meter unobstructed physical space, subdivided into nine 1 × 1 meter cells". [V]
- Each physical cell shows one tile of the order-5 square tiling {4,5} (five squares at every vertex). Stepping across a cell edge moves to the edge-adjacent tile. [V]
- A loop of four steps with four 90° turns returns to the same physical cell but a different virtual tile; a fifth step is needed to close the loop virtually. Repeating this in different directions reaches any tile. The paper credits Henry Segerman's 2×2 demo for the idea (https://www.youtube.com/watch?v=ztsi0CLxmjw). [V]
- There is no formula from physical position to hyperbolic position, and no curvature value. The state is purely combinatorial: which tile each cell currently shows. [V]
- "The rooms themselves use euclidean geometry, but the player needs five turns to return to their original location." This was chosen "to avoid the need to implement a full hyperbolic render engine". (2022 report) [V]
- Geometric reading: this is the piecewise-flat ("polyhedral") model of {4,5}. Every cell is a flat square with 90° corners; all curvature sits at the vertices as cone points of angle 5 × 90° = 450°, i.e. −π/2 of curvature per vertex. Averaged over the floor this equals a smooth plane of radius L_eff = g / √(2π/5) ≈ 0.89·g, so 1 m cells behave like L ≈ 0.89 m. [D] The Brief's "R ≈ 0.8–0.94 m" for this mapping is consistent. [S]

### A.2 Why 5 squares and why 3×3

- Five is "the smallest number to exhibit hyperbolic geometry", so the holonomy effect is perceived immediately; with more squares per vertex the user must walk through several extra rooms and is less likely to notice. [V]
- 3×3 is called the simplest setup that "allows making four 90° turns in all directions". [V]

### A.3 Rendering and the "7 views"

- Only the nine accessible cells are rendered. They are drawn as flat Euclidean geometry in Unity. No Poincaré/Klein/log-map projection is used for the first-person view. [V]
- The boundary of the move area is drawn as hedges that "coincide with the move area's boundaries". [V]
- Problem: two different tiles can occupy the same Euclidean cell depending on which way you look around a vertex. [V]
- Solution: split the grid into sub-grids joined by portals. The tiles in the player's own row and column are placed directly; the remaining cells are seen through portals, and the process repeats inside each sub-grid. For 3×3 this gives 1 main grid + 2 sub-grids + 4 sub-sub-grids = **7 views**. [V]
- Portal counts by player position: centre tile 8 portals, edge tile 4, corner tile 6; only two levels of depth are ever needed for 3×3. [V]
- First implementation: render-texture portals (expensive). Final: stencil polygons, one render queue. Slotboom's benchmark (Unity editor, hardware not stated): centre tile 144 fps with render textures vs 334 fps with stencil; overall "about 2.3 times faster". [V]
- Stencil limits: the number of distinguishable portals and the recursion depth are limited; sequential stencil polygons at the same position need compare/increment tricks. [V]
- Trees are placed at every vertex "as natural separating pillars with diverging views around their left or right side", which hides the seam. [V]
- My check: enumerating straight sight-lines on a 3×3 grid gives 17 tile instances from a corner cell, 15 from an edge cell and 13 from the centre cell, with at most 4 different tiles on one physical cell. That matches the 2-then-4 structure they describe. [D]

### A.4 State storage

- The tiling is a graph (one node per square). A spanning tree gives each tile a unique word: a first step N/E/S/W followed by F/L/R steps. Two algorithms keep words canonical: a validity check (forward always valid; right invalid if the previous step was right; left invalid if no right step since the last left) and a normalisation that rewrites invalid words onto another branch. Source of the scheme: Celińska-Kopczyńska & Kopczyński, https://arxiv.org/abs/2111.12040. [V for Holonomy's description; the cited paper itself was not fetched]
- "A discrete representation like this is preferred, because numeric coordinate representation ... introduces numeric precision issues." [V]
- Tile colour is an MD5 hash of the tile word, so the same tile always has the same colour. (2022 report) [V]
- Navigation state for path planning is (tile, physical cell, rotation): 144 states per location, reducible to 9 by symmetry. Shortest paths use A* with the unobstructed hyperbolic distance as an admissible heuristic, switching to an anytime variant when far from the goal. [V]

### A.5 World population and navigation aids

- Wave Function Collapse on the tile graph, with one hierarchy level of "biomes" spread by a simple propagation depth. [V]
- Objects that span tiles (logs over two tiles, creeks over several) make the geometry visible: they "suddenly vanish behind one side of a tree". [V]
- Mini-map in the Poincaré disk, on the HUD or on the controller; tiles coloured hot/cold by distance to the goal; the user cannot see further than three tiles on it; a shortest path is shown when the user strays. [V]

### A.6 User studies

- Study 1 (23 participants): procedural population increased immersion. Study 2 (30 participants): HUD vs controller mini-map, no performance difference, preference is individual. Plus 12 users at a public event, qualitative. [V]
- "No participant had to abort testing the prototype due to VR sickness." No SSQ or presence scores are reported in the paper. [V]
- "It does take the users some engagement with the platform to overcome their initial confusion." Three of the 12 called it "confined" or "claustrophobic"; movement control "came naturally to all participants"; most improved their navigation after a few interactions. [V]
- The quantitative results live in TU Delft tech reports (Jochems 2023, Rijsdijk 2023), which I did not fetch.

### A.7 Stated limitations, and other room sizes

- "It currently does not include support for other grid sizes." [V]
- The 2022 report says most placement algorithms "have already been made with a variable size matrix in mind", and suggests optional extra walls between cells. A wall is also placed "whenever there is a physical barrier between two squares in the grid". [V] So arbitrary polyomino floors are a natural extension, not something they shipped.
- No lighting across tiles (spotlights would need non-Euclidean light transport). [V]
- Nothing physically stops the user leaving the area; they propose physical hedge dummies for touch. [V]
- Slotboom 2023 gives the reason for avoiding true hyperbolic rendering: "Geodesic deviation and other properties of hyperbolic space cause unpleasant experiences for a player in a virtual reality game [Weeks 2021], which is why 'true' hyperbolic virtual reality is not feasible. This issue is the main reason why Holonomy is based on an order-5 square tiling but otherwise uses Euclidean rendering." [V] This is the strongest published argument against our decided renderer, so the comfort spike in §D.8 is mandatory.

---

## B. Other prior art (what each contributes to OUR problem)

| Work | What it contributes | URL | Tag |
|---|---|---|---|
| Hart, Hawksley, Matsumoto, Segerman, "Non-euclidean VR I: H³" (2017) | Room-scale tracked walking in H³. Walking forward makes the floor "fall out from under your feet"; loops rotate the world so the virtual floor "no longer appears to coincide with the real-life floor". Motivates H²×E. | https://arxiv.org/abs/1702.04004 | [V] |
| Same authors, "Non-euclidean VR II: H²×E" (2017) | Exactly our decided scheme: hyperboloid model, draw with the inverse exponential map (the Klein trick fails in H²×E: "if we tried to move towards an object we would miss"), and per-frame relative motion. They state the cost: "at any one time, any location in your real space might map to any location in the virtual space". Floor stays level. Also: height shrinks linearly with distance but width shrinks exponentially, so distant objects look tall and thin. Their precision fix is to keep the user in the central cell and re-centre. | https://arxiv.org/abs/1702.04862 | [V] |
| Weeks, "Body coherence in curved-space VR games" (2021) | Defines curvature as holonomy per unit area (θ = k·A). Holonomy makes separately tracked head and hands disagree; fix is to track everything relative to one anchor. His billiards game maps a physical square table to a virtual right-angled pentagon because "their perimeters are locally identical" (90 cm edges at 90°): one virtual lap = 1.25 physical laps. Recommends keeping content in the middle of the lab and using the "native inhabitant" stereo view. | https://arxiv.org/abs/2011.00510 | [V] |
| HyperRogue VR (Kopczyński) | Ships both headset modes we are choosing between: "reference" ("when you move your head in a loop, you return to where you started") and "holonomy" ("when you move your head in a loop, you usually don't return"), plus "holonomy Z" for 2D geometries. Default `absolute_unit_in_meters = 3`. Eye mode "equidistant" = correct perceived direction and distance. | https://github.com/zenorogue/hyperrogue/blob/master/vr.cpp | [V] |
| Hyperbolica VR (CodeParade) | H²×E-style physics with gyrovectors; mostly teleport locomotion in VR, walking limited. Holonomy authors note it "does not use it to connect physical walking with movement". | https://store.steampowered.com/app/1256230/Hyperbolica/ | [S] |
| Tea for God (void room) | Procedural impossible spaces generated to fit any play area; rule "if you choose the door on the right, you should turn right on the map"; world built from independent cells generated on the fly. Smallest supported area reported as 1.8 × 1.2 m. | https://void-room.itch.io/tea-for-god/devlog/235914/open-world-with-impossible-spaces | [V] for the rules, [S] for the minimum size |
| Scenograph (Marwecki & Baudisch, UIST 2018) | A tracking-volume-independent description of a real-walking experience, instantiated to any size/shape by splitting locations. Realism dropped when a 25 m² experience was squeezed to 9 m² or an L-shaped 8 m² with older techniques, not with Scenograph. Lesson: author content against an abstract floor, not a fixed square. | https://hpi.de/baudisch/projects/scenograph.html | [S] |
| VRoamer (Cheng, Ofek, Holz, Wilson, IEEE VR 2019) | Extracts walkable area and obstacles in real time, instantiates pre-authored rooms where they fit and generates corridors otherwise; newly seen obstacles animate into virtual objects. Lesson: obstacles become diegetic objects. | https://www.microsoft.com/en-us/research/publication/vroamer-generating-on-the-fly-vr-experiences-while-walking-inside-large-unknown-real-world-building-environments/ | [V] |
| DreamWalker (Yang, Holz, Ofek, Wilson, UIST 2019) | Real-world walking fully in VR; fuses GPS, inside-out tracking and RGBD to sense walkable paths and obstacles live. 8 participants, 15-minute route. Lesson: a live depth "tripwire" for dynamic obstacles is feasible. | https://www.microsoft.com/en-us/research/publication/dreamwalker-substituting-real-world-walking-experiences-with-a-virtual-reality/ | [V] |
| Oasis (Sra et al., VRST 2016) | Scans a room, detects obstacles, maps the walkable area, generates a VR world fitted to it, pairs detected objects with virtual counterparts for touch. This is our scan → walkable polygon pipeline. | https://www.cs.ucsb.edu/~sra/publications/procedurally-generated.pdf | [S] |
| Substitutional Reality (Simeone, Velloso, Gellersen, CHI 2015) | Every physical object paired with a virtual counterpart with some mismatch; studies how far the mismatch can go. Lesson: real furniture can be re-skinned, but only while registered. | https://doi.org/10.1145/2702123.2702389 | [S] |
| Impossible spaces (Suma et al., TVCG 2012) | Self-overlapping rooms; small rooms may overlap by up to 56 % before users notice, rooms filling a 9.14 × 9.14 m workspace by up to 31 %. Relevant to the Lung blade, not to the smooth H² sandbox. | https://doi.org/10.1109/TVCG.2012.47 | [S] |
| Detection thresholds (Steinicke et al., TVCG 2010) | Rotation can be 49 % more or 20 % less; distance down 14 % or up 26 %; curvature arc radius ≥ 22 m. Used below to judge the non-1:1 options. | https://www.uni-muenster.de/imperia/md/content/psyifp/ae_lappe/freie_dokumente/stein__bruder__jerald__frenz__lappe_2010.pdf | [S] |
| Widdowson & Wang, "Human navigation in curved spaces" (Cognition 2022) | HTC Vive, 3 × 3 × 3 m volume, H³ and S³ at radius 10 m, beam-steered travel (not walking), 24 + 12 participants. People pointed home as if space were Euclidean, even after a 5-minute learning phase. Lesson: give a home beacon and a mini-map; do not expect path integration. | https://par.nsf.gov/servlets/purl/10387047 | [V] |
| Pisani et al., "Navigation by Walking in Hyperbolic Space Using VR" (CHI PLAY 2019) | A walking-focused VR game with Euclidean and hyperbolic versions of two levels (path-following, and path-planning on a branching structure). Initial testing suggested people navigate hyperbolic environments "without major disorientation" and may handle branching structures more intuitively. | https://paper.nweon.com/1560 (listing; the ACM page and DOI were not confirmed) | [S] |

---

## C. What the iPhone 15 Pro Max gives us on-device (Oct 2026, through Unity)

### C.1 Capabilities

| Capability | Through AR Foundation 6.5 / ARKit XR Plugin | Notes | Tag |
|---|---|---|---|
| LiDAR scene mesh | `ARMeshManager`; needs LiDAR, iOS 13.4+. Per-triangle classification via `ARKitMeshSubsystemExtensions.SetClassificationEnabled` / `GetFaceClassifications` (deprecated in 6.3 in favour of `GetMeshClassifications` → `XRMeshClassification`). Classes: None, Wall, Floor, Ceiling, Table, Seat, Window, Door. | "It usually takes about 4 seconds ... before the scanned meshes start to show up." Enabling `ARPlaneManager` smooths the mesh on planes. https://docs.unity3d.com/Packages/com.unity.xr.arkit@6.4/manual/arkit-meshing.html | [V] |
| Plane detection | `ARPlaneManager`, horizontal + vertical, boundary polygons, classifications Table, Seat, Floor, Ceiling, WallFace, DoorFrame, WindowFrame. | One classification per plane; no plane subsumption; "requires additional CPU resources and can be energy-intensive". https://docs.unity3d.com/Packages/com.unity.xr.arkit@6.4/manual/arkit-plane-detection.html | [V] |
| RoomPlan in Unity (native) | `ARBoundingBoxManager` via `RoomPlanBoundingBoxSubsystem`: `SetupRoomCapture`, `StartRoomCapture`, `StopRoomCapture`, `IsRoomCapturing`, `GetRoomCaptureInstruction`. iOS 17+, LiDAR only. | **Only object bounding boxes** (couch, table, bed, chair, storage, screen, appliances …). The docs do not expose walls, doors or windows. "Users must complete a room capture before retrieving the bounding boxes." https://docs.unity3d.com/Packages/com.unity.xr.arkit@6.4/manual/arkit-bounding-boxes.html | [V] |
| RoomPlan walls/doors/windows | Needs a **Swift native plugin**. `RoomCaptureSession(arSession:)` and `stop(pauseARSession: false)` exist since iOS 17 and keep "a common world coordinate system"; a custom ARSession with `ARWorldTrackingConfiguration` "will be honored". Unity hands over the session through `XRSessionSubsystem.nativePtr` → `struct UnityXRNativeSession { int version; void* sessionPtr; }`. | Output `CapturedRoom`: walls, doors, windows, openings (Surfaces with dimensions + 4×4 transform + confidence) and objects (cuboids with category). https://developer.apple.com/videos/play/wwdc2023/10192/ , https://docs.unity3d.com/Packages/com.unity.xr.arkit@6.4/manual/arkit-session.html | [V] |
| RoomPlan quirk | Running RoomCaptureSession on a custom ARSession removes `sceneDepth` from frames until you re-run your own configuration after `didStart`. | Apple forum, no Apple reply. Expect AR Foundation's own configuration to need re-applying too. https://developer.apple.com/forums/thread/763400 | [V] |
| RoomPlan limits | Max room about 9 × 9 m; ≥ 50 lux; avoid scans over 5 minutes (battery and thermal); "full-height mirrors and glass pose a challenge", high ceilings can exceed range, very dark surfaces are hard. | https://developer.apple.com/videos/play/wwdc2022/10127/ | [V] |
| RoomPlan accuracy | About 1–3 cm on wall dimensions in ordinary interiors. | Apple forum and small third-party studies only. | [S] |
| LiDAR range | "up to 5 meters away, works both indoors and outdoors". | https://www.apple.com/newsroom/2020/03/apple-unveils-new-ipad-pro-with-lidar-scanner-and-trackpad-support-in-ipados/ | [V] |
| World map | `ARKitSessionSubsystem.GetARWorldMapAsync()`, `ApplyWorldMap()`; relocalises and restores stored planes and anchors. | Lets us scan once (the day before / the morning) and relocalise per judge. Unity API docs. | [S] |
| Tracking drift | Benchmark on iPhone 12 Pro Max: final drift 0.14 m over 84 m (open hallway), 0.19 m over 114 m (stairs), 0.79 m over 145 m (long corridor); tracking jumps "very rare". | So about 0.2–0.5 % of path length. In one room ARKit keeps re-observing the same features, so the error stays bounded but corrections arrive as small jumps. https://arxiv.org/abs/2207.06780 | [V] numbers, [E] room-scale reading |
| Drift regression | A 2026 Apple forum thread reports accumulating world-tracking drift on LiDAR devices from iOS 26.4, acknowledged as a known issue and reported fixed in 26.6.1. | **Check the demo phone's iOS version** and run a drift test. https://developer.apple.com/forums/thread/827240 | [V] page fetched; details were machine-summarised, re-read before relying on them |
| Thermal | No Apple figures. The Build Pipeline rule stands: meshing, plane detection and depth off after the scan. | Build Pipeline §0.5, §5 | [S] |

### C.2 Scan route, ranked for a 36 h build

1. **Manual boundary (always works, 30 s).** Hold the phone, tap four or more floor corners (ray-cast to the floor plane) or walk the perimeter. Gives a polygon. No ML, no LiDAR dependence. [E]
2. **ARKit mesh + planes (pure AR Foundation, no Swift).** Algorithm below. Gives free floor with holes for furniture, automatically. [E]
3. **RoomPlan boxes via `ARBoundingBoxManager`** for furniture categories, layered on route 2. [V that the API exists; whether it shares AR Foundation's world origin and runs alongside meshing is undocumented]
4. **Swift RoomPlan plugin** for walls, doors and windows as clean oriented boxes. Best looking, highest integration risk. [E]

### C.3 Algorithm: walkable floor polygon + obstacle set

Input: ARMesh chunks with face classes, ARPlanes, the user's walked path during the scan. Output: free-floor polygon with holes, a signed distance field, obstacle boxes, a room frame. [D]

```
FloorScan():
  y0   = height of the largest ARPlane classified Floor (fallback: lowest large horizontal plane)
  G    = 2D grid over the mesh's xz bounding box, 5 cm cells (8 x 8 m -> 160 x 160)
  for each mesh triangle t with class c:
      if c == Floor and |t.y - y0| < 0.08:               mark cells under t as FLOOR
      else if t has a vertex with y in (y0+0.10, y0+2.0): mark cells under t as OBST   # walls, furniture, legs; ceiling ignored
  for each cell the user's feet passed over while scanning: mark FLOOR                 # walked = free, the Oasis idea
  FREE    = FLOOR and not dilate(OBST, 1 cell)
  FREE    = morphological_close(FREE, 2 cells)           # fill pinholes; never-seen cells stay not-free
  SDF_raw = signed_distance(FREE)                        # metres, positive inside, kept as a float array
  R       = connected_component(SDF_raw > 0.30, containing the user)    # 0.30 m = body radius + tracking error
  P_walk  = simplify(marching_squares(R), 5 cm)          # polygon with holes
  theta0  = dominant wall direction mod 90 deg           # length-weighted WallFace plane normals, else PCA of the boundary
  (c0, Wf, Df) = largest rectangle inside R, axis-aligned in the theta0 frame   # max-rectangle-in-histogram, O(cells)
  boxes   = oriented bounding boxes of OBST components inside the hull of R (+ RoomPlan boxes if present)
  anchor  = ARAnchor at c0                               # everything above is stored relative to this anchor
```

- Cost: one pass over the mesh triangles (a 4 × 5 m room is roughly 20–60 k triangles) plus two distance transforms on a 160 × 160 grid; well under 100 ms once, off the main thread. [E]
- Scan time: mesh appears after about 4 s [V]; covering a room by sweeping the phone takes roughly 30–60 s. [E]
- Failure handling: glass walls and mirrors produce missing or phantom floor. Unknown cells are never free, and the user confirms the outline (shown on the floor) before entering VR. If the confirmed area is under 1.5 × 1.5 m, switch to standing mode (§E.4). [D]

### C.4 Live safety boundary ("guardian") without passthrough

- Everything here is computed and drawn in **tracking space, unwarped**, relative to the ARAnchor. It never goes through the hyperbolic transform. This is the Spec's rule "safety is always computed in physical coordinates". [S]
- Per frame (cost: a handful of array look-ups) [D]:

```
d      = bilinear(SDF_raw, head.xz);   n = gradient(SDF_raw)        # n points to safety
v      = head velocity, low-passed over 0.2 s
d_pred = bilinear(SDF_raw, head.xz + 0.5 s * v)
level  = STOP if min(d, d_pred) < 0.25 m  or  tracking not Normal for > 0.5 s
         WARN if min(d, d_pred) < 0.55 m
         SAFE otherwise                 (add 0.15 m hysteresis when relaxing, as in the Spec)
draw   SAFE: thin floor outline of the real room (alpha 0.35)  -> the "ghost room"
       WARN: vertical grid on boundary segments within 1.5 m, alpha = smoothstep(0.55, 0.25, d); furniture boxes as wireframes
       STOP: grid at full alpha in red, hyperbolic world desaturated, audio tick + haptic, arrow along n
```

- Draw WARN and STOP with `ZTest Always` so no virtual object can hide a real wall. [D]
- In a 3 × 3 m room the SAFE zone is only about 1.9 × 1.9 m, so the outline will be visible most of the time. Design it as a permanent, good-looking part of the scene, not as an alarm. [D]
- Drift: the guardian follows the ARAnchor, which ARKit corrects when it refines its map. Pose jumps are rejected for the hyperbolic integration (Build Pipeline rule, > 0.2 m per frame) but accepted for the guardian. [D]
- Dynamic obstacles (people, chairs moved after the scan) are NOT covered once meshing is off. Options: a spotter (Build Pipeline), or a depth "tripwire" that samples the LiDAR depth map at 5–10 Hz and raises STOP when something is closer than about 0.7 m where the static model predicts free space. The tripwire costs heat; make it a toggle and measure it in the soak test. [E]
- The rear cameras and LiDAR must see out of the headset for any of this to work. See Questions.

---

## D. The geometry problem and the design options

### D.1 The problem, stated precisely

Notation: curvature K = −1/L². Hyperboloid model H² = {p ∈ ℝ^{2,1} : ⟨p,p⟩ = −1, p_w > 0}, ⟨a,b⟩ = a_x b_x + a_z b_z − a_w b_w.

- Exp(t) = (sinh r · t/|t|, cosh r) with r = |t|/L; Log(p) = L · asinh(|p_xz|) · p_xz/|p_xz|.
- Boost(Δ) with u = Δ/|Δ|, c = cosh|Δ|, s = sinh|Δ|:
  `[[1+(c−1)u_x², (c−1)u_x u_z, s u_x], [(c−1)u_x u_z, 1+(c−1)u_z², s u_z], [s u_x, s u_z, c]]`.
- Rolling update (decided): U ← U · Boost(Δ/L), Δ = head xz displacement in tracking axes.

Facts [D, all checked numerically]:
1. **No isometry exists** between a piece of the flat floor and a piece of H² (Theorema Egregium). So "registered" and "1:1" cannot both hold everywhere.
2. **A closed physical loop does not close virtually.** The product of boosts around the loop is a residual isometry with a rotation AND a displacement. For small loops the rotation is ≈ A/L² in the sense opposite to the loop (Weeks: holonomy per unit area is the curvature [V]); "rotates by A/L²" is only the small-loop limit.

| Physical square loop, side s | L | Virtual displacement after one lap | World rotation after one lap | A/L² |
|---|---|---|---|---|
| 1 m | 2.83 m | 0.09 m | 7.5° | 7.2° |
| 2 m | 2.83 m | 0.80 m | 34° | 29° |
| 2 m | 4.0 m | 0.38 m | 16° | 14° |
| 2 m | 10 m | 0.06 m | 2.3° | 2.3° |
| 2 m | 1.885 m | 2.00 m | 90° | 65° |
| 3 m | 2.83 m | 3.00 m | 90° | 65° |

3. **The magic ratio.** When s/L = 1.0613 the four legs are four sides of a right-angled regular pentagon: one lap shifts you exactly one tile and turns the world exactly 90°, and a fifth leg closes the loop. cosh(1.0613) = φ, the golden ratio.
4. **Same-direction laps do not travel.** Repeated square laps of side s < 1.76·L only carry you round a virtual circle (radius 1.48 m for s = 2 m, L = 2.83 m). To travel you need loops around at least two different centres.
5. **Virtual walls drift off real walls.** Walk 3 m parallel to a virtual wall 0.5 m away (L = 2.83 m): the virtual wall ends up 0.80 m away while the real wall is still at 0.5 m.

### D.2 The Lambert-quadrilateral relation (requested check)

For an equiangular hyperbolic quadrilateral with corner angle 2π/q and midline half-lengths m₁, m₂ (in units of L):

**sinh(m₁) · sinh(m₂) = cos(2π/q)** — verified. [D]

- The two midlines cut the tile into four Lambert quadrilaterals (three right angles, one angle 2π/q); the identity is the standard Lambert relation. Numerically, a tile built in the Klein model with half-widths tanh m₁, tanh m₂ has corner angle acos(sinh m₁ sinh m₂) to 6 digits for five test cases.
- Equivalent "loop" form: a rectangular walk of sides a = 2m₁L, b = 2m₂L with 90° turns closes after q legs when (cosh(a/L) − 1)(cosh(b/L) − 1) = 4cos²(2π/q). For q = 6 the right side is 1. The midlines are the tile-centre spacings, so the tile picture and the walked-polygon picture are dual descriptions of one fact.
- **Parity constraint:** a non-square equiangular tile can only tile edge-to-edge when q is even, because long and short edges must alternate around every vertex. q = 5 needs a square tile; a rectangular room needs q = 6 (or 8). [D]
- Regular tiles (units of L): {4,5}: inradius 0.5306, centre spacing 1.0613, edge 1.2537, circumradius 0.8425, area 1.2566, corner 72°. {4,6}: 0.6585, 1.3170, 1.7627, 1.1462, 2.0944, 60°. [D]

**L when the room itself is the tile (midlines = wall-to-wall distances):**

| Room W × D | L for q = 5 | L for q = 6 | Notes |
|---|---|---|---|
| 2 × 2 m | 1.885 m | 1.519 m | |
| 3 × 3 m | **2.827 m** | **2.278 m** | q = 5 tile edges are 3.54 m long, corners 72° |
| 3 × 4 m | 3.270 m (quad exists, cannot tile) | **2.637 m** | q = 6 walls become 4.27 × 5.09 m |
| 4 × 5 m | 4.218 m (cannot tile) | **3.401 m** | q = 6 walls become 5.61 × 6.43 m |
| 6 × 8 m | 6.539 m (cannot tile) | 5.274 m | too gentle; subdivide instead |

**L when the walked loop is the tile spacing (recommended, "loop-fit"): L = s / 1.0613 for {4,5}**

| Room | Loop side s = min(W, D) − 1.0 m | L (gentle, one loop) | L (explorer, two loops fit) |
|---|---|---|---|
| 2 × 2 m | 1.0 m | 0.94 m | not possible |
| 3 × 3 m | 2.0 m | **1.885 m** | 0.94 m (s = 1.0 m) |
| 3 × 4 m | 2.0 m (or 2 × 3 m loop, q = 6: 1.869 m) | **1.885 m** | 1.41 m (s = 1.5 m) |
| 4 × 5 m | 3.0 m (or 3 × 4 m loop, q = 6: 2.637 m) | **2.827 m** | 1.885 m (s = 2.0 m) |
| 6 × 8 m | capped at 3.2 m | **3.0 m** | 3.0 m |

### D.3 Option 1 — "Rolling room": continuous rolling H²×E + ghost-room guardian

- **Mapping.** U ← U · Boost(Δ/L) every frame; view = log map centred on the head (decided stack). At t = 0, U = Rot(−θ₀) · Boost((x₀ − c₀)/L), so the home tile sits on the room centre with the room's orientation. The scan fixes L (loop-fit), supplies the tile content (walkable outline, furniture as props) and the guardian. [D]
- **What the user sees.** Their room's floor outline as one tile of an infinite tiling, five rooms around every corner pillar, rooms shrinking fast with distance. A thin white outline of the real room (the ghost room) on the floor. As they walk loops the hyperbolic world turns under the ghost room. Walking the marked five-leg path returns them to the start after five right angles.
- **Safety.** Entirely the tracking-space guardian of §C.4. Virtual geometry carries no safety meaning.
- **Alignment and passive haptics.** Registered only at t = 0. Lost at a rate of about (enclosed area)/L² radians. No passive haptics. Virtual walls cannot be solid: build tiles from floor lines, low curbs, corner pillars and dithered "hologram" walls, and fade any solid within 0.3 m of the head.
- **Room size and shape.** Irrelevant to the virtual world. Any polygon with holes works because only the guardian depends on it. L-shaped: the lap loop uses the largest inscribed rectangle. Furniture in the middle: ghost boxes. 2 × 2 m: works but L ≈ 0.94 m is strong; prefer standing mode. 6 × 8 m: cap s at 3.2 m.
- **L.** Loop-fit table above; clamp to [1.0, 3.2] m.
- **Implementation cost on top of the decided stack.** Scan → SDF 4–6 h; guardian 3–4 h; tile set + ids 3–4 h; procedural tile mesh 2–3 h. The shader and the U update are already planned. [E]
- **Performance.** 25–45 tiles, one mesh and one material instance each. Details in §F.6. [D]
- **Comfort risk.** No visual–vestibular conflict (every step is locally 1:1, gains at identity). Risks are cognitive disorientation, the unfamiliar far-field parallax (§E.3), and seeing the guardian often in small rooms. No formal study of smooth H²×E room-scale walking exists; Holonomy's authors avoided it. Medium, test in the spike.
- **Feel.** Strongest. Curvature is visible (bending lines, exponential crowding) and bodily (laps turn the world, five right angles).

### D.4 Option 2 — "Holonomy cells": registered cells, path-dependent tile identity

- **Mapping.** Rasterise the eroded free floor into square cells of side g (1.0–1.5 m) aligned to θ₀. The set of free cells is a polyomino. Crossing a cell edge in direction i does tile ← Neighbour(tile, i) in {4,5}. Inside a cell, virtual = physical (identity). Blocked cells and the outer boundary are rendered solid. [V for Holonomy at 3×3; D for the generalisation]
- **What the user sees.** A flat, ordinary-looking patch (garden, cloister) exactly the shape of their free floor, walls where the real walls are, a pillar at every interior grid corner. Looking past a pillar on the left shows a different room than on the right. Walking round a pillar four times lands them in a fifth room. The mini-map carries most of the "hyperbolic" message.
- **Safety.** Virtual walls ARE the real walls and furniture; the guardian is a backup.
- **Alignment and passive haptics.** Exact and permanent. Real walls and furniture can be touched where they are seen.
- **Room size and shape.** Any polyomino. L-shape and mid-room furniture are natural (Holonomy already planned walls between cells). Needs at least two interior vertices for unbounded exploration: 3 × 2 cells minimum. 2 × 2 m is too small (cells under 0.7 m). 6 × 8 m: limit drawing to cells within two steps of the user.
- **L.** Not free: L_eff ≈ 0.89 g (q = 5). g = 1.0 m → 0.89 m; g = 1.5 m → 1.34 m. Non-square cells need q = 6.
- **Implementation cost.** New renderer but a simple one: flat cells plus a per-instance clip against up to four "gates" (§F.7), no stencil ordering. 5–7 h plus the shared scan and ids. [E]
- **Performance.** 13–17 instances on 3×3, 42 on 4×5 and 57 on 6×8 at view range 2. Trivial. [D]
- **Comfort risk.** Lowest. The only evidence we have is for this option: no sickness aborts in 65 users, some initial confusion, some claustrophobia. [V]
- **Feel.** Moderate. Exact and explorable, but each cell looks Euclidean; the judge has to notice the corner effect. The honest description is "piecewise-flat model with curvature at the corners", which is correct maths but less spectacular.

### D.5 Option 3 — "Room as fundamental polygon": fixed chart, tiling seen through the walls

- **Mapping.** No path dependence: virtual position = φ(physical position) with a fixed chart of the room onto one tile. Best chart is the Klein rectangle: k = (tanh(m₁)·2X/W, tanh(m₂)·2Z/D), p = (k, 1)/√(1 − |k|²), with m₁, m₂ from the Lambert relation. It maps walls to walls, corners to corners and straight walks to geodesics. U = pure boost from the tile centre to p. [D]
- **What the user sees.** Their room as a hyperbolic tile with 72° or 60° corners and outward-bowed walls, and the infinite tiling of copies of it through glass walls, five or six around each corner. Walls are where the real walls are when touched.
- **Safety.** Good: boundary maps to boundary, so reaching the virtual wall is reaching the real wall. But virtual walls are drawn farther than the real ones from the middle of the room (the corner is drawn at 2.38 m when the real one is at 2.12 m, for 3 × 3 m and q = 5), so the guardian is still needed.
- **Alignment and passive haptics.** Exact at the walls, approximate inside.
- **Motion is not 1:1.** Local scale 0.92 at the centre, 1.20 at wall midpoints, 1.5–1.7 in the corners; heading error up to about 9° near corners. Translation gains above 1.26 are detectable. [D, with Steinicke's threshold S]
- **No holonomy, no exploration by walking.** Moving to another tile needs an artificial step (a "door reset": walk to a wall, turn round, the neighbour room is now behind you).
- **Room size and shape.** Needs a quadrilateral fit; q = 6 for rectangles. Good for tiny rooms and standing users.
- **Variant (Weeks-style wall anchoring).** Track the head relative to the nearest wall point and replay that in the virtual tile. Exact along the walls, path-dependent only through an edge counter, but walking straight through the room centre adds a visual turn of about 10°/m (radius 5.8 m, well under the 22 m threshold). [D]
- **Cost.** About 1 h on top of Option 1 (replace the U update), plus 3 h if door resets are wanted. **Performance** as Option 1.
- **Comfort risk.** Medium: speed and heading mismatch near corners.
- **Feel.** Weak to moderate: a hyperbolic view, not a hyperbolic walk.

### D.6 Option 4 — Hybrid: Euclidean scanned hub + hyperbolic wing

- **Mapping.** The hub is the scan drawn 1:1 in tracking space (registered, touchable). A portal, placed on a free-standing frame or a real doorway, leads into a wing that is internally Option 1 or Option 2. So this is a framing, not a different mapping.
- **What it adds.** A calm, registered start and end; a clear "before/after" for judges; a guaranteed way back to registration (walking out through the portal, or a "flatten" button, resets U).
- **Costs.** One stencil portal between an unwarped and a warped scene (4–6 h) [E]. With rolling, the hub seen through the portal slides relative to the frame as the user moves, because the frame is a hyperbolic object and the hub is not. [D]
- **Feel.** Good storytelling, but all the hyperbolic "feel" still comes from whichever mode runs inside the wing.

### D.7 Comparison

| | 1 Rolling room | 2 Holonomy cells | 3 Fixed chart | 4 Hub + wing |
|---|---|---|---|---|
| Motion 1:1 | yes, everywhere | yes, everywhere | no (0.92–1.7×) | as inner mode |
| Virtual walls stay on real walls | no | yes, exactly | at the wall only | hub only |
| Passive haptics | none | walls and furniture | walls | hub only |
| Safety source | guardian only | virtual walls + guardian | virtual walls + guardian | as inner mode |
| Exploration by walking | unbounded in principle; efficient only if s ≈ L | unbounded with ≥ 2 interior corners | none | as inner mode |
| True hyperbolic metric on screen | yes | no (flat cells) | yes | as inner mode |
| Holonomy felt in the body | yes, continuous | yes, discrete | no | as inner mode |
| Room shapes | any | any polyomino, ≥ 3 × 2 cells | quadrilateral fit | any |
| Smallest sensible room | ~2.5 × 2.5 m | ~3 × 2 m | any | ~3 × 3 m |
| Curvature radius | free; loop-fit 1.9–3.0 m | forced ≈ 0.9·g | Lambert fit 2.3–3.4 m | — |
| Extra build cost [E] | 12–17 h | 5–7 h + shared 8–10 h | +1 to +4 h | +4 to +6 h |
| Tiles / instances drawn | 25–45 | 13–57 | 25–45 | + hub |
| Comfort evidence | none published for walking | 65 users, no aborts [V] | none | — |
| Comfort risk [E] | medium | low | medium | low–medium |
| "Feel it, not cosmetic" | high | medium | low–medium | depends |
| "Purposeful, not disorienting" | needs ghost room + mini-map | high | high | high |

### D.8 Recommendation

- **Primary: Option 1 (Rolling room)**, entered through the Option 4 framing in its cheapest form: the scan first appears flat and registered, then space curves while the user stands still. Reasons: it is the decided renderer and update rule; it has no sensory conflict; it works in any room shape; and with the ghost room it turns the unavoidable mismatch into the lesson.
- **Fallback: Option 2 (Holonomy cells)**. It removes both big risks of the primary at once (warp-shader trouble, and disorientation or wall contact), it is the only variant with user-study support, and it reuses the scan, the guardian, the tile ids, the content hash and the mini-map. Keep it behind a mode switch.
- **Degenerate mode for tiny rooms or seated judges: Option 3**, which is a one-line change to the U update.

**Spike to run first (≤ 2 h, 1–2 people, no shader work):**
1. Unity scene on the phone with the AR session and any camera rig (mono is fine).
2. C# only: the U update; a list of the ~25 nearest {4,5} tiles; each frame compute tile edges through the log map on the CPU and push them to line meshes (about 1 200 points); cylinders at the vertices.
3. Ghost room from four tape-measured corners, drawn in tracking space with the proximity grid.
4. Tests, three team members each:
   - Walk the five-leg pentagon (s = 2.0 m, L = 1.885 m). Pass if the closure error is under 0.25 m. This validates signs, units, ARKit scale and jitter handling.
   - Three free minutes at L = 1.0, 1.9 and 2.8 m, Fast Motion Sickness score each minute, count guardian STOP events and wall touches.
5. Gate: all FMS ≤ 3/20 at L = 1.9 m and zero wall contacts → proceed with Option 1. Otherwise raise L, or make Option 2 the default.

---

## E. Free walking without a fixed square

### E.1 How the world adapts to the scanned free floor

- **Option 1:** the virtual world does not need to fit the room. What adapts is (a) L and the tile size, from the lap loop; (b) the home-tile content; (c) the guardian; (d) where goals are placed. [D]
- **Goal placement:** put the first goals in the star of tiles around the lap loop's centre vertex (reachable by one to four laps), not "straight ahead beyond the wall". Use a planner on the tile graph (Holonomy uses A* with the hyperbolic distance heuristic [V]) to guarantee each goal is reachable in N laps.
- **Option 2:** content is authored per cell with door/wall sockets; blocked cells become solid props; corridors must be at least one cell wide. This is the Tea for God / Scenograph rule already in Spec §6.4. [S]
- **Obstacles become objects** (VRoamer, Oasis [V/S]): in registered modes re-skin them as solid props; in rolling mode show them only as ghost boxes.

### E.2 Landmarks, mini-map, orientation aids

- A pillar at every tile corner, built as q wedges, one per adjacent tile and tinted with that tile's colour, so "five rooms meet here" is readable at a glance. Holonomy's trees play the same role. [V for trees, D for wedges]
- A stable colour per tile from a hash of its id (Holonomy uses MD5 of the tile word [V]); group colours by branch so regions read as "lands".
- Objects that cross tile edges (paths, creeks) to make bending visible. [V]
- Mini-map: Poincaré disk centred on the user, tiles within three steps, plus the ghost-room outline mapped through Exp at the head. On the map the user watches the real room rotate against the tiling as they lap. [D]
- A home beacon (arrow + geodesic distance to the home tile), because people path-integrate as if space were flat. [V, Widdowson & Wang]
- A lap counter and a live "world turned by N°" read-out: this is curvature being measured.
- For spectators and the spotter: mirror the same map plus the physical outline and guardian level on the PC over the Phase 1 phone link. [E]

### E.3 What walking in H²×E looks like, and comfortable L

- **Far-field parallax saturates.** Side-stepping by 1 m swings an object at distance d by coth(d/L)/L radians, never less than 1/L. By motion parallax an object at distance d looks as if it were at L·tanh(d/L), so everything appears within L metres: at L = 1.9 m an object 8 m away has the parallax of one 1.88 m away. [D] This is the "bowl" feeling, and a likely source of the "claustrophobic" reports.
- **Angular size** falls as 1/sinh(d/L): at 6 m an object looks 52 % of its Euclidean size for L = 2.83 m, 26 % for L = 1.885 m, 3 % for L = 1 m. [D]
- **Stereo.** The decided pipeline (log map, then ordinary per-eye projection) gives Euclidean disparity at the true geodesic distance. This is HyperRogue's "equidistant" eye mode and Weeks' recommended "native inhabitant" view; Weeks finds the alternative uncomfortable. [V] Keep it.
- **Body coherence.** Draw anything attached to the body (hands, the tracked 4D cube, the ghost room, HUD) at its tracking-space offset from the head. Never integrate hand motion through the hyperbolic transform. [V, Weeks]
- **Comfortable L.** Published values: HyperRogue VR default 3 m [V]; Widdowson & Wang 10 m [V]; Holonomy's cells are equivalent to about 0.9 m [D]; the Brief plans R ≈ 2.2 m for the Lung blade [S]. Recommendation: default L ≥ 1.9 m for judges, strong mode (about 1 m) opt-in. [E]
- **Fog.** Start at 1.5·L, fully opaque at 2.5–3·L (5–8 m for L = 1.9–2.8 m). Beyond that, tiles are small and shimmer under 2× MSAA, and the tile count explodes. [D/E]
- **Other rules.** Ramp curvature over 3–5 s while the user stands still and never change L while they walk; walking pace 0.5–0.8 m/s; sessions of 3–5 min; no vignette and identity gains (Build Pipeline [S]); FMS every minute with a stop threshold. [E]
- **Evidence summary.** Polyhedral walking: no sickness aborts, initial confusion (Holonomy [V]). Smooth H³: floor falls away, "disconcerting"; H²×E chosen as "a gentler introduction" (Hart et al. [V]). Navigation: Euclidean homing bias (Widdowson & Wang [V]); "no major disorientation" (Pisani et al. [S]).

### E.4 Tiny rooms

- Free floor under about 2.5 × 2.5 m: the lap loop is ≤ 1.5 m, so loop-fit L ≤ 1.4 m. Offer (a) standing mode with Option 3 (look around, lean, no laps), or (b) rolling with L ≈ 1.1–1.4 m and a short session. [E]
- Under 1.5 × 1.5 m: standing mode only.

---

## F. Recommended design as an implementable spec

### F.1 Data flow

```
SCAN (handheld, 30-60 s)                         meshing + planes ON
  -> FloorScan()  => SDF_raw, P_walk, boxes, room frame (c0, theta0), Wf x Df, ARAnchor      (C.3)
  -> user confirms the outline on the floor; save ARWorldMap                                  (optional)
FIT
  -> s = clamp(min(Wf, Df) - 0.4, 1.0, 3.2)      # Wf, Df are already eroded by 0.30 m per side
  -> L = s / 1.0613, q = 5                        # regular {4,5}; tile-centre spacing = s
BUILD (once, < 50 ms)
  -> tile mesh (floor grid + curbs + corner wedges + furniture props) in tile-local log coordinates
  -> ghost-room mesh (outline, wall grid, boxes) in anchor space
  -> meshing, plane detection, depth OFF                                                      (Build Pipeline)
ENTER
  -> U = Rot(-theta0) * Boost((x0 - c0)/L); tile = HOME; curvature ramps 0 -> 1/L^2 over 3-5 s
RUNTIME (every frame)
  -> pose delta -> U update -> tile crossing -> per-tile anchors -> draw; guardian; mini-map
```

### F.2 Tile geometry and mesh

- Regular {4,5} tile in the Klein model is the square |k_x|, |k_z| ≤ T, with T = tanh(0.5306) = 0.4858. [D]
- Generate the floor as a uniform N × N grid in Klein coordinates (straight grid lines are geodesics, so tile edges meet their neighbours exactly), then store each vertex in **tile-local log coordinates**: `pos.xz = L · atanh(|k|) · k/|k|`, `pos.y = height`. The Build Pipeline shader then works unchanged (`ExpH2(posOS.xz/L)`). [D]
- Subdivision: the chord error of a straight segment of length ℓ in the log chart is about 2 mm for ℓ = 0.25 m and 8 mm for ℓ = 0.5 m at L = 2.83 m; 4 mm and 15 mm at L = 1.885 m; 8 mm and 31 mm at L = 0.94 m. Rule: ℓ ≤ 0.1·L. Vertical edges need no subdivision because height is Euclidean. [D]
- Props: transform each prop vertex on the CPU once (`Boost(anchor/L) · Rot · Exp(v.xz/L)`, then Log back to tile-local coordinates) and merge into the tile mesh. Keep props under 0.3·L wide so they stay visually rigid (Spec §6.3 [S]).
- Because L depends on the scan, generate the tile shell procedurally in C# at run time. Blender-authored tiles only work for a fixed L. [D]

### F.3 Tile identity and neighbours

- Neighbour frames: G_i = Boost of length 2m = 1.0613 along direction i·90° (i = 0..3). Checked: five steps with 90° turns return to the start with the frame turned by 90°. [D]
- Exact ids (recommended): cosh(1.0613) = φ and sinh(1.0613) = √φ, so every tile-centre coordinate is a number a + bφ + (c + dφ)√φ with integer a, b, c, d (φ² = φ + 1). Keep the tile's matrix in this ring with 64-bit integers (safe to about 30 steps from home, use `BigInteger` beyond); the id is the hash of its third column. No drift, no rewriting rules. For {4,6}: cosh = 2, sinh = √3. [D]
- Canonical orientation of a tile: of the four frames M·Rot(k·90°) take the lexicographically smallest, so content always faces the same way regardless of the path taken. [D]
- Alternatives: Holonomy's tree words with normalisation rules [V]; or the hackathon shortcut of double-precision matrices relative to the current tile with de-duplication by centre distance (−⟨p,q⟩ < cosh 0.2), which is enough for rendering but gives no persistent ids. [D]
- Visible set, recomputed only when the user changes tile: breadth-first from the current tile, keep tiles whose centre is within d_cull of the user. Counts for {4,5}: 13 tiles at 2.0·L, 25 at 2.5·L, 45 at 3.0·L, 81 at 3.5·L, about 130 at 4·L. [D]

### F.4 Runtime update (pseudo-code)

```
// LateUpdate, execution order after the pose driver (Build Pipeline pitfalls apply)
d = head.xz - prevHead.xz;  prevHead = head.xz
if (length(d) > 0.2 || tracking != Normal) d = 0            // reject relocalisation jumps
U = U * Boost(d / L)                                        // double3x3
U = LorentzGramSchmidt(U)                                   // metric diag(1,1,-1)
p = U * (0,0,1);  k = p.xz / p.w                            // user position in the current tile, Klein
if (abs(k.x) > T || abs(k.z) > T) {
    i = crossed edge;  U = inverse(G[i]) * U                // inverse(M) = eta * transpose(M) * eta
    tile = Neighbour(tile, i);  RebuildVisibleSet()
}
Uinv = eta * transpose(U) * eta
for each visible tile t:
    if (behind the user by more than ~110 deg and farther than 1 tile) skip
    mat[t].SetMatrix("_RoomAnchor", (float3x3)(Uinv * F[t]))   // product in double, cast last
Shader.SetGlobalVector("_HeadPosWS", head);  Shader.SetGlobalFloat("_HypL", L)
```

- With this layout `_HypCamInv` is the identity; the per-tile anchor already contains the camera. U never grows, because the user is always inside the current tile (|position| ≤ 0.84·L). [D]
- Jitter is harmless: integrating 1 mm RMS pose noise for 60 s of standing still moved the virtual position by 1.6 mm and the heading by 0.0003° in simulation. [D]

### F.5 Guardian and ghost room

As §C.4. Assets: floor outline strip, wall grid quads per boundary segment, wireframe boxes; one unlit, unwarped shader; two extra draw calls per eye. [E]

### F.6 Per-frame cost, Option 1

| Item | Cost | Tag |
|---|---|---|
| U update + Gram–Schmidt | a few 3×3 double products, < 5 µs | [E] |
| Visible-set rebuild | only on tile change; BFS over ≤ 81 tiles | [D] |
| Per-tile anchors | 25–45 double 3×3 products + `SetMatrix`, < 0.1 ms | [E] |
| Guardian | 5 array look-ups | [D] |
| Draw calls | 1 per tile per eye (shell and props merged) → 50–90, + ghost room + mini-map; budget is 300 | [D] |
| Triangles | ~1 k shell + ≤ 2 k props per tile → 75–135 k per eye; budget is 300–500 k per frame | [E] |
| Vertex shader | sinh, cosh, two 3×3 products, asinh per vertex; ≤ 300 k vertices per frame | [E] |
| Fragment | unlit palette + `fwidth` lines + distance fog. World-space distance from the head equals geodesic distance × L, so ordinary distance fog is already correct. | [D] |

Recommended settings: d_cull = 2.5·L at the thermal "Serious" tier, 3.0·L otherwise; fog end = d_cull.

### F.7 Fallback renderer (Option 2), sketch

- Instance list: for every physical cell within view range, one instance per distinct straight sight-line cell sequence from the user's cell. The sequence is fully determined by which side of each interior grid corner the sight-line passes (checked by brute force on 3×3: 17 patterns ↔ 17 sequences). [D]
- Each instance carries up to four gates and is clipped per fragment, per eye:

```hlsl
float2 e = _WorldSpaceCameraPos.xz, d = posWS.xz - e;
for (int i = 0; i < 4; i++) {                        // _Gate[i] = (corner.x, corner.z, sign, unused); sign 0 = off
    float2 g = _Gate[i].xy - e;
    if (_Gate[i].z != 0) clip(_Gate[i].z * (g.x * d.y - g.y * d.x));
}
```

- No stencil, no render-order constraints, correct in stereo because each eye uses its own position. A pillar at each corner hides the seam, as in Holonomy. [D]
- Tile for each instance = current tile followed along the sequence with the same Neighbour() as Option 1.

---

## G. Scope levels for a 4-person, ~36 h build

| Level | Contents | Rough effort [E] |
|---|---|---|
| **Minimal** | Manual boundary (tap or walk corners). Fixed regular {4,5}, L from loop-fit with a slider. Rolling U, visible set by numeric de-duplication, procedural tile shell, CPU-side everything from the spike promoted to the warp shader. Ghost-room guardian with SAFE/WARN/STOP. Read-out of laps and world rotation. | 10–12 person-hours |
| **Target** | Automatic scan from ARKit mesh + planes with furniture boxes. Exact tile ids, hashed colours, corner wedge pillars. Poincaré mini-map with ghost room, home beacon. "Curve it" transition from the registered flat room. Guided pentagon lap. ARWorldMap save/relocalise. FMS logging. Option 2 cell mode behind a switch. PC spectator map over the phone link. | 25–30 person-hours |
| **Stretch** | Swift RoomPlan plugin (walls, doors, windows, categories → stylised kit room as tile content). LiDAR depth tripwire for dynamic obstacles. Stencil portal between the Euclidean hub and the hyperbolic wing. Rectangular {4,6}-type tiles fitted to the room. Goal planner (A* on the tile graph) with lap guidance. | 15–25 person-hours more |

Suggested split once the spike passes (sequential spine: device build + stereo rig → spike → gate):
- **A — Scan and safety:** FloorScan, SDF, guardian, ARAnchor/world map.
- **B — Hyperbolic core:** U update, tile ids, visible set, cell-mode logic.
- **C — Rendering:** warp shader port, procedural tile mesh, fog/lines, corner wedges, ghost-room shader.
- **D — UX and measurement:** mini-map, beacon, transition, pentagon tutorial, FMS logging, PC spectator view.
A and B and C are independent until integration; D depends on B's interfaces only.

---

## H. Conflicts, corrections and risks against the existing documents

1. Build Pipeline §4 calls Holonomy "the closest prior art" for the H²×E floor. It is prior art for the walking mechanic and tile ids, but it deliberately does NOT warp; our renderer follows Hart et al. and HyperRogue VR. Its comfort results do not transfer to the smooth warp.
2. Build Pipeline §0.5 "fog at about 5–8 units of hyperbolic distance" and §4 "CPU-cull at d > ~4" are far too generous for a {4,5} tiling: about 130 tiles at 4 units and several hundred at 5. Use 2.5–3 units. The precision horizon is not the limiting factor; tile count is.
3. Brief §3.7 states the tile mapping "forces R ≈ 0.8–0.94 m". That holds only for 1 m cells in 3 × 3 m. With free walking L is a design choice (1.9–3.0 m by loop-fit).
4. Spec §8.1 says furniture becomes "virtual walls and haptic proxies". True only in registered modes (hub, Option 2). In rolling mode nothing virtual can be relied on for touch or safety.
5. Spec §8.1 assumes RoomPlan through `RoomCaptureSession(arSession:)` inside Unity. Feasible, but AR Foundation's built-in RoomPlan support covers furniture boxes only, and sharing the session has a known configuration quirk.
6. Odd q needs square tiles; any plan that fits a rectangular room to {4,5} must crop to a square or switch to q = 6.
7. "Unbounded travel in a small room" and "gentle curvature" conflict below roughly 4 × 5 m.
8. A depth tripwire for dynamic obstacles conflicts with the thermal rule "depth off after setup".

---

## Could not verify

- Pisani et al. 2019: only a search summary; the paper (curvature, room size, participant numbers) was not fetched.
- Hyperbolica VR and its devlogs: only secondary descriptions; how it treats walking and holonomy in VR is not confirmed.
- Whether Unity's `RoomPlanBoundingBoxSubsystem` shares AR Foundation's ARSession and world origin, and whether it can run together with `ARMeshManager` and `ARPlaneManager`. The docs are silent.
- RoomPlan dimensional accuracy (1–3 cm) and typical scan time: secondary sources and estimates only.
- The iOS 26.4+ LiDAR drift regression thread was fetched, but its dates and version numbers were machine-summarised; re-read it and test the device.
- Any Apple or Unity figure for the heat cost of meshing, depth or plane detection.
- ARKit tracking quality inside a lens headset (partly occluded field of view, head-mounted motion) and in a crowded hall.
- The exact geometry of Holonomy's portal layout (figures did not survive text extraction). The 1 + 2 + 4 = 7 views, the portal counts and the two-level depth are verified from the text; my sight-line model reproduces the structure but is my own reconstruction.
- The quantitative results of the two Holonomy user studies (in TU Delft tech reports by Jochems and Rijsdijk, not fetched).
- The scale (metres per unit) used by Hart et al.; the papers do not state it.
- Suma's 56 % overlap and Steinicke's thresholds were taken from the Spec and search summaries, not re-read in the papers.
- Tea for God's 1.8 × 1.2 m minimum: search summary only.
- Celińska-Kopczyńska & Kopczyński's tree-generation paper was cited via Holonomy, not fetched.
- No published comfort study exists, as far as I could find, for real walking in a smooth H²×E rendering at room scale with a stated L.

## Questions for the team

1. **Headset optics and cut-outs:** does the lens headset leave the rear cameras and the LiDAR unobstructed? Tracking, scanning in place and any depth tripwire depend on it.
2. **Who scans, and when?** The team before each judge (saved world map, relocalise), or the judge as part of the demo? This decides how robust and how fast the scan flow must be.
3. **Demo floor:** how much free floor will the venue give, and can it be roped off with a spotter? The curvature radius, and whether two lap loops fit, follow from this.
4. **Explore or feel?** Is unbounded exploration by walking a goal of the hyperbolic sandbox, or is "see it and feel the world turn as you lap" enough for Phase 1? (L ≈ 1 m and Holonomy-like, versus L ≈ 2–3 m and gentle.)
5. **What should a tile look like?** Copies of the judge's own room (stylised scan, furniture repeated), or an authored landscape with the real room present only as the ghost outline?
6. **Solid walls or free walking?** Are you willing to give up solid virtual walls and passive haptics in the hyperbolic space (Option 1), or is touching real walls where you see them more important (Option 2)?
7. **Swift:** is anyone comfortable writing and debugging a Swift/Objective-C plugin this weekend? If not, RoomPlan walls stay in "stretch".
8. **Comfort gate:** who are the test walkers for the spike, what FMS score stops a session, and is a default of L ≥ 1.9 m with an opt-in strong mode acceptable?
