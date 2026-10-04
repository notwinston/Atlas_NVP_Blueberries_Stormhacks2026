"""Phase-1 work breakdown for ATLAS, as data.

Each task: id -> (lane, title, deps, output, acceptance, runs_on)
runs_on: E = Unity Editor, H = phone in hand, V = phone in headset, S = Station PC, M = Mac build seat, D = desk (documents only)
The generator (gen_wbs.py) turns this into Markdown tables and Mermaid diagrams and checks the graph.
"""

LANES = {
    "X": "All hands",
    "A": "Lane A · Platform, Look & Flat Spaces",
    "B": "Lane B · Kernel & Curved Worlds",
    "C": "Lane C · Scan, Room & Safety",
    "D": "Lane D · Link, Station & Forge",
    "G": "Gates",
}

T = {}


def task(tid, lane, title, deps, output, accept, runs):
    T[tid] = dict(id=tid, lane=lane, title=title, deps=deps, output=output, accept=accept, runs=runs)


# ----------------------------------------------------------------------------- all hands
task("P1-X01", "X", "Build seat, signing and Unity (lane A leads, everyone takes part)",
     [],
     "Appendix C.1 and C.2: the Mac with Xcode 26; Unity ≥ 6000.3.21f1 with iOS and Windows build support on all four machines; one signing owner for the Personal Team",
     "All four machines open an empty Unity project of the pinned version; the Mac produces an Xcode project from it without errors",
     "M")
task("P1-X02", "X", "Contracts frozen (this is gate G0)",
     ["P1-A01", "P1-X05", "P1-X06", "P1-X07", "P1-X08"],
     "The four sets of contract pages merged under `docs/contracts/`; stubs for every interface: identity warp, fake tile provider, loopback link, recorded pose log, the sample rooms as RoomModel files",
     "Stub implementations compile in every assembly; each lane's sandbox scene runs against the stubs in the Editor with no other lane's code; a change to any contract after this point is announced to all four lanes before it is committed",
     "E")
task("P1-X03", "X", "Station ready (lane D)",
     [],
     "Appendix C.7: .NET 8, Python 3.11 and a working CUDA build of PyTorch on the RTX 5070 PC; the `station/` folders reserved for the Hub and the Forge",
     "`nvidia-smi` and a PyTorch CUDA test pass; `dotnet --version` reports 8.x",
     "S")
task("P1-X04", "X", "Phones and network ready (lane C)",
     [],
     "Appendix C.1, C.6 and C.7 (the phone and router lines): both phones in Developer Mode and trusted by the Mac; the team's own Wi-Fi router with a fixed address for the Station; both phones and the Station joined to it",
     "Both phones report LiDAR (iPhone 15 Pro Max and 14 Pro Max, not Plus models); each phone opens a test page served from the Station's fixed address",
     "H S")
task("P1-X05", "X", "Contract pages, lane A",
     [],
     "Written from §6 and §8.3 of this plan, needing no code: `ISpaceModule`; the shader include contract and the list of ids for `AtlasShaderIDs.cs` (with lane B); the layer and stencil table; the frame-loop order; scene ownership",
     "Each page names every type, signature, unit and owner an implementer needs; the other three lanes have read it and no question is open",
     "D")
task("P1-X06", "X", "Contract pages, lane B",
     [],
     "Written from §6.5 and Appendix A: the kernel types (`Iso2`, `TileId`, `WalkerPose`, `ITileGraph`), the metric and the unit conventions, and the `AtlasWarp` entry point of the shader contract",
     "Each page names every type, signature, unit and owner an implementer needs; the other three lanes have read it and no question is open",
     "D")
task("P1-X07", "X", "Contract pages, lane C",
     [],
     "Written from §4.3: the RoomModel JSON schema with its coordinate frame and units; three sample rooms (3×3 m, 3×4 m L-shape, 4×5 m with a table) and the performance fixture `budget_room` (4×5 m with twelve stand-in objects of exactly 5,000 triangles each, no edge over 0.25 m; used for budget tests only)",
     "The sample rooms and `budget_room` validate against the schema; the other three lanes have read the page and no question is open",
     "D")
task("P1-X08", "X", "Contract pages, lane D",
     [],
     "Written from §7 and Appendix B: the parameter registry (how a tunable is declared and reaches the Console); the link message ids and the `State` layout; the GLB asset contract",
     "A hand-packed `State` example of 202 B is given byte by byte; the other three lanes have read the pages and no question is open",
     "D")

# ----------------------------------------------------------------------------- lane A
task("P1-A01", "A", "Unity project and repository skeleton",
     ["P1-X01"],
     "Project from the AR Mobile template; packages pinned (Appendix C); one assembly definition per area; text serialisation, Git LFS, UnityYAMLMerge; `station/` folders; scene-ownership file",
     "A clean clone opens with zero console errors on all four machines; the EditMode test runner runs (0 failing)",
     "E M")
task("P1-A02", "A", "First build on both phones",
     ["P1-A01", "P1-X04"],
     "ARKit as the only XR loader; signing with the Personal Team; post-build script for Info.plist keys; `targetFrameRate = 60`; thermal frame-rate adjustment off; Frame Timing Stats on; native `Assets/Plugins/iOS/AtlasThermal.mm` returning `NSProcessInfo.thermalState`, with an Editor stub that returns 0",
     "An empty AR scene runs on phone A and phone B with tracking state Normal; the build recipe is one paragraph in the README",
     "M H")
task("P1-A03", "A", "URP asset and quality tiers",
     ["P1-A01"],
     "Forward, Render Graph, HDR off, depth and opaque textures off, depth priming off, no post-processing, shadows off, SRP Batcher on; tier assets Nominal / Fair / Serious",
     "Render Graph Viewer shows one native pass per eye camera on the test scene; settings equal the Appendix C table",
     "E")
task("P1-A04", "A", "Two-camera stereo rig",
     ["P1-A02", "P1-A03"],
     "Eye cameras at ±IPD/2 under the tracked head, half-screen viewports (until `P1-A05` gives each eye its own texture), one shared renderer, no camera background; IPD in the parameter registry; the AR camera only carries the pose",
     "Phone A in the headset, empty scene: 60 fps on the Metal HUD, CPU main ≤ 3 ms, GPU ≤ 2 ms; a cube at 1 m fuses comfortably for three testers",
     "V")
task("P1-A05", "A", "Lens pre-distortion as the single final blit",
     ["P1-A04"],
     "Each eye renders into its own 1118 × 1032 target (0.8 × the half-screen) with 4× MSAA; multisampled colour and depth are memoryless and only the resolved colour is kept; one full-screen pass samples both eye textures, applies per-eye barrel distortion (headset coefficients) and upscales; switchable; texture sets for render scales 0.8, 0.7 and 0.6 are allocated once",
     "A straight-line grid looks straight through the lenses to three testers; the pass adds ≤ 1.0 ms GPU; Xcode GPU capture: each eye pass resolves its colour (store action MultisampleResolve) and discards depth and stencil, no pass loads a multisampled attachment, and there is exactly one blit to the screen",
     "V")
task("P1-A06", "A", "Shared look shader",
     ["P1-X02", "P1-A03"],
     "`AtlasSurface` with `AtlasCore.hlsl`: palette lookup, vertex-colour ambient occlusion, `fwidth` grid and edge lines, distance fog in the room hue, emissive accents; calls the `AtlasWarp` entry point; one `UnityPerMaterial` block; 128² palette texture (dark base); gradient background whose horizon colour equals the fog colour",
     "Frame Debugger reports the shader SRP-Batcher compatible; ≤ 8 variants; a test room renders in the Editor's stereo preview",
     "E")
task("P1-A07", "A", "Thermal quality ladder",
     ["P1-A02", "P1-A05", "P1-D04", "P1-D06"],
     "Poll of the thermal plugin (from `P1-A02`) every 2 s; ladder controller that steps the eye-texture set (render scale), MSAA, fog radius ρ and glued copies from thermal state and GPU p95",
     "Each tier can be forced from the Console and takes effect within 1 s; at most one step per 20 s with hysteresis; thermal state is visible on the HUD and in the CSV",
     "V")
task("P1-A08", "A", "SpaceLab host",
     ["P1-X02", "P1-A04"],
     "Boot scene that loads one `ISpaceModule` scene additively at a time, with a fade, and registers that space's parameters; the `AtlasFrameLoop` driver (`[DefaultExecutionOrder(100)]`) that calls Bump Guard, pose filter, kernel, space module and upload in the §6.3 order",
     "Switching among all loaded spaces 20 times leaves memory within ±5 %; works in the Editor (XR Simulation) and on the phone",
     "E V")
task("P1-A09", "A", "Glued space S1",
     ["P1-A06", "P1-A08"],
     "Universal cover fixed to the floor: an open-frame cell (corner posts, floor grid, one chiral landmark; ≤ 1,500 triangles) cut from the free floor at the RoomModel cell size; 5×5 copies around the walker's current cell; mirrored rows for the Klein bottle (negative scale); T³ as 3×3×3 copies with a vertical period of 2.6 m; a self-avatar (≤ 600 triangles) in every copy but the walker's own",
     "Walking across a cell edge shows no seam in either eye; Klein rows pass the determinant-sign test; cells and avatars alone cost ≤ 110 draw calls and ≤ 110k triangles per frame at 5×5 (T², Klein) and ≤ 120 draw calls and ≤ 120k triangles at 3×3×3 (T³)",
     "E V")
task("P1-A10", "A", "Stencil portal engine",
     ["P1-A06", "P1-A04"],
     "Render Objects features per room layer, stencil references 1–15 from one allocation table, plane clip in `AtlasPortalClip.hlsl`, room swap only when the old room's bounds fail `GeometryUtility.TestPlanesAABB` for both eye frusta and the head is outside the overlap volume, held for 3 consecutive frames (BP §4), portal box for the straddling eye; no recursion",
     "Two-room test: no seam or pop when the head crosses the doorway in stereo; ≤ 1.0 ms GPU per visible portal; passes stay merged so MSAA is resolved on-tile",
     "E V")
task("P1-A11", "A", "Impossible Rooms S5",
     ["P1-A10", "P1-A08"],
     "Booth-to-hall (bigger inside) and a four-turn loop corridor, generated from the RoomModel free rectangle and authored in play-space coordinates so every portal transform is the identity; room graph with a reachability check",
     "Layouts fit inside the eroded free floor for all three sample rooms; four right turns arrive in a different room; every room is reachable (graph test)",
     "E V")
task("P1-A12", "A", "Slicer S6 and Mesh4D",
     ["P1-X02", "P1-A06"],
     "`Mesh4D` (tetrahedra in ℝ⁴), Burst job that cuts it with a hyperplane, generators for the 5-cell, tesseract (48 tetrahedra), 16-cell and 24-cell, shadow (projection) view, rotations in planes that contain w, a colour per cell, and a periodic tube generator in ℝ³×S¹ (moving sphere)",
     "EditMode: tesseract corner-first slice areas 0.8660 at 0.5 and 6.9282 at 2.0; 4D ball slice radius √(1 − c²); moving-sphere length D·v/(v − u). Phone A: tesseract ≤ 0.5 ms (should: ≤ 0.2 ms), 18k-tetrahedron tube ≤ 1.0 ms at p95, zero allocations per frame",
     "E V")
task("P1-A13", "A", "One-command build and the build-seat rules",
     ["P1-A02"],
     "Script: clean checkout → Unity batch build → Xcode build → install on a connected phone; written rules for the build queue and phone booking",
     "One command installs the current main branch on a phone; a second engineer has done it from the README alone",
     "M")
task("P1-A14", "A", "Per-space performance runs and the soak",
     ["P1-D04", "P1-A07", "P1-A05", "P1-G3", "P1-C07", "P1-B11", "P1-B12", "P1-A15"],
     "Scripted tour that visits every space S0–S6 on `budget_room`; one CSV per run; the 15-minute in-headset soak, alternating S2 Curved Walk (ρ = 3, room as tile zero) and S1 Glued 5×5; one more soak with depth off, driven by a recorded pose track with nobody in the headset",
     "Metrics table of §11.2 filled for every space at the Must level, cold; soak: no Critical thermal state, mean ≥ 45 fps, never below 30; the difference in time-to-Fair with depth on and off is recorded",
     "V")
task("P1-A15", "A", "Room content in Glued, and standing mode",
     ["P1-A09", "P1-C07"],
     "In S1 the Home walls sink to kerbs and every object from the RoomModel (placeholder box or generated mesh) is drawn once, at its true position, as a solid among the copies; with the RoomModel's standing flag set, S1 and S6 run in place (no wrap-walk prompt; the 4D cut is moved with the stick)",
     "On phone A in S1 a virtual table edge is within 5 cm of the real one at three touch points; S1 on `budget_room` stays within its row of §8.5; with a 1.4 × 1.4 m RoomModel, S1 and S6 load and run",
     "E V")

# ----------------------------------------------------------------------------- lane B
task("P1-B01", "B", "Geometry kernel",
     ["P1-A01"],
     "`Atlas.Kernel` in double precision: boosts and rotations for κ < 0, = 0, > 0, exp and log maps, distance, η-Gram–Schmidt, inverse as ηMᵀη; no scene types",
     "EditMode: exp∘log = identity to 1e-12; holonomy of a geodesic triangle equals its angle defect to 1e-9; five steps of 1.0613 with 90° turns return to the identity, three steps of π/2 on the sphere, four flat; ‖UᵀηU − η‖ < 1e-12 after 100,000 random boosts",
     "E")
task("P1-B02", "B", "Holonomy rig",
     ["P1-B01", "P1-X02"],
     "Per-frame update U ← U·Boost(Δ/L) from the filtered head displacement, tile-crossing hook, curvature ramp that only runs while the walker stands still",
     "Replay of a recorded square lap at loop-fit L closes after five sides to 1 mm; a recorded stand-still of 60 s moves the frame by < 5 mm; the kernel step runs after the pose update and exactly once in each of 600 consecutive frames (logged frame counter)",
     "E")
task("P1-B03", "B", "Warp shader include",
     ["P1-X02", "P1-B01"],
     "`AtlasWarp.hlsl`: one entry point (object position in, world position out) for none / hyperbolic / spherical, log map centred on the head, one per-material anchor matrix computed in double on the CPU and cast last; component that sets huge `localBounds`",
     "GPU positions match the kernel's CPU reference to 1 mm for ρ ≤ 3; the shared shader is still SRP-Batcher compatible; exp is evaluated once per vertex",
     "E")
task("P1-B04", "B", "Exact tile ids and the tile graph",
     ["P1-B01"],
     "`TileId` as six integers in ℤ[φ], neighbour step S_k = R^k·T·H by additions only, breadth-first visible set, re-root when the walker crosses an edge, content seed = hash of the id; tolerance-hash variant for {4,6}",
     "Per-depth counts equal OEIS A377322 to depth 10; a walk round a vertex closes after exactly 5 steps; φ(X²+Y²) − T² = −1 for every tile (evaluated in Int128, because the products overflow 64 bits long before the ids do); tile centres within ρ = 2 / 3 / 3.5 / 4 of a tile centre number 13 / 45 / 81 / 129, and 105 within 3.8425; a crossing costs ≤ 0.5 ms in the Editor",
     "E")
task("P1-B05", "B", "Procedural tile meshes",
     ["P1-B01", "P1-A06"],
     "Generator in C#: Klein-square grid mapped to the log chart; levels of detail by grid count (n = 16 per side on the walker's tile and the tiles that touch it, n = 8 on every other drawn tile, n = 4 on the high tier beyond ρ = 3), every level keeping the same 16 boundary segments per edge; kerbs; corner-post wedges bounded by the tile's own two edges (72° in the curved metric, 65.8° in mesh coordinates for {4,5}); palette UVs; vertex ambient occlusion; a flat variant for Aligned Cells",
     "Edges of neighbouring tiles coincide to 1 mm for every pair of levels; five wedges round a post leave no gap and no overlap; worst screen-space chord error ≤ 0.15° from an eye height of 1.6 m at L = 1.4, 1.9 and 3.0 m (raise n if it fails); ≤ 1,200 / 500 / 250 triangles for the three levels at any L",
     "E")
task("P1-B06", "B", "Tile renderer",
     ["P1-B03", "P1-B04", "P1-B05"],
     "Pool of tile objects and material instances; per-tile anchor each frame; level of detail by graph distance; the CPU draw rule of §8.5 (centre within ρ + 0.8425, and the tile's floor disc not outside the widened view frustum; one test per frame for both eyes); fog from ρ − 1, opaque at ρ",
     "Phone A at ρ = 3: tile shells cost ≤ 150 draw calls per frame at any head pitch; 60 fps; no allocation on a tile crossing; over a recorded lap with head turns and looks down at the floor, no undrawn tile has a point that is nearer than ρ and inside a camera frustum (EditMode check over the pose log); in a screenshot the edge of the drawn world cannot be told from the background",
     "E V")
task("P1-B07", "B", "World generator and WorldLint",
     ["P1-B04", "P1-B05"],
     "Archetype from hash(tile id, seed): Plaza, Corridor, Doorway, Tower, Span A/B, Garden, Home; four regions with their own hue; corner posts; one spanning object near the start; WorldLint editor check",
     "WorldLint passes for three seeds (reachable, a tower within ρ ≤ 2.5 of every tile, budgets, no coincident faces, the same region for every tile to depth 6 on two runs); the same seed gives the same world hash in two Editor runs and under IL2CPP on the phone (checked at G6)",
     "E")
task("P1-B08", "B", "Small Planet S4",
     ["P1-B03", "P1-B05"],
     "{4,3} tiling: six tiles always loaded, spherical branch of the warp, fragments beyond 2.6 rad discarded, fog opaque at 0.8π, pole beacon",
     "Three steps of (π/2)·R with 90° turns close to 1 mm in the Editor; no smeared triangle in a 360° look-around on the phone",
     "E V")
task("P1-B09", "B", "Curvature dial and the 3-4-5 walk",
     ["P1-B02", "P1-B06", "P1-B08", "P1-C06"],
     "Dial with snaps q = 3, 4, 5, 6 that ramps curvature only while the walker stands still; guided loop markers from the RoomModel lap square; corner counter, closure-error and world-turn read-outs",
     "On phone A the loop closes after 3, 4 and 5 corners with closure error < 0.25 m each; curvature never changes while head speed exceeds 0.1 m/s; with the standing flag set the dial still snaps q = 3 to 6 and no lap markers are shown",
     "V")
task("P1-B10", "B", "Aligned Cells S3 (Mode 2)",
     ["P1-B04", "P1-B05", "P1-X02"],
     "Cell grid from the RoomModel (sample rooms until the live scan exists), tile per cell chosen by the walked path, sight-line instances clipped by per-fragment gates (no stencil), a post at every interior corner, switch from Mode 1 at the same tile id",
     "EditMode: 13 / 15 / 17 instances for centre / edge / corner cell of a 3×3 grid; in the Editor and on the phone four quarter-turns round an interior corner end in a fifth tile and a fifth returns to the first; tile instances alone cost ≤ 120 draw calls per frame on the 4×5 m sample room",
     "E V")
task("P1-B11", "B", "The room becomes tile zero",
     ["P1-B06", "P1-B07", "P1-C07"],
     "Home rebuild placed in curved space by the exp map about the room centre; walls sink to kerbs and corner posts when curvature is on; tiles under the room's footprint are flagged home and kept clear; return-to-Home re-registers; the lap line starts 0.17 m from a corner post of tile zero (at L = 1.88 m) and runs along tile zero's edges at a shallow angle, so the post fade and the tile-crossing rule are checked on a replayed lap",
     "At κ = 0 the curved-space room matches Home to 1 cm; a ramp to loop-fit L and back restores registration to 2 cm; WorldLint finds no generated prop inside the room footprint; a replayed lap of the lap square shows no flicker of the tile id along the edges",
     "V")
task("P1-B12", "B", "Room content in Aligned Cells",
     ["P1-B10", "P1-C07"],
     "Home walls on the edge of the cell grid; every object from the RoomModel (placeholder box or generated mesh) drawn once per eye at its true position whenever any instance of its cell is drawn, never duplicated per instance (§4.7)",
     "On phone A a virtual table edge is within 5 cm of the real one at three touch points after ten cell crossings; S3 on `budget_room` stays ≤ 170 draw calls and ≤ 200k triangles per frame at 60 fps",
     "V")

# ----------------------------------------------------------------------------- lane C
task("P1-C01", "C", "AR session and Editor simulation",
     ["P1-A02"],
     "Lowest-resolution 60 Hz video format; environment depth through `AROcclusionManager` (mode Fastest, temporal smoothing off, human stencil and human depth off), read with `TryAcquireEnvironmentDepthCpuImage` (256 × 192); meshing and plane detection on only during the scan; XR Simulation environments; pose-log and RoomModel replay in the Editor",
     "The session holds 60 Hz on the phone and the depth image is non-null at the chosen video format; a recorded walk drives the rig in the Editor",
     "E H")
task("P1-C02", "C", "Scan route 1: manual outline",
     ["P1-C01"],
     "Tap or walk the floor corners to make the floor polygon",
     "Polygon within 5 cm of a tape measure in three rooms",
     "H")
task("P1-C03", "C", "Scan route 2: automatic FloorScan on the phone",
     ["P1-C01", "P1-C15"],
     "The classified LiDAR mesh and the planes of a live scan fed into the FloorScan core of `P1-C15`, with a progress view of the floor found so far",
     "On the phone the free rectangle is within 10 cm of a tape measure and scan-to-walkable takes ≤ 90 s",
     "H")
task("P1-C04", "C", "Object boxes",
     ["P1-C01"],
     "RoomPlan furniture boxes through `ARBoundingBoxManager` (category, size, pose), merged with the FloorScan obstacles in one coordinate frame",
     "Boxes overlay the mesh to ≤ 10 cm; in three test rooms every piece of furniture wider than 0.4 m has a box (proposed)",
     "H")
task("P1-C05", "C", "Swift RoomPlan bridge for walls, doors and windows (Extended)",
     ["P1-C01"],
     "Native plugin using `RoomCaptureSession(arSession:)`; walls, doors, windows and openings as JSON in session coordinates; AR configuration re-applied afterwards",
     "Wall positions within 3 cm of the mesh-derived ones; scene depth is available again after capture",
     "H")
task("P1-C06", "C", "RoomModel and the loop-fit",
     ["P1-C02", "P1-C03", "P1-X02"],
     "RoomModel JSON from either scan route: polygon, distance field, free rectangle, lap square s, L and R from the loop-fit rule, cell grids for S1 and S3, object list, anchor; outline confirmation in hand; save and load",
     "Validates against the schema; a manual-outline scan and an automatic scan of the same room give free rectangles within 10 cm of each other; a room with less than 1.5 × 1.5 m of free floor selects standing mode; the log confirms meshing and planes are off after the scan",
     "E H")
task("P1-C07", "C", "Home rebuild S0",
     ["P1-C06", "P1-A06"],
     "Floor, walls and door openings as 0.25 m-grid meshes in the palette, registered at the anchor; a placeholder box per object, tinted by category, on an object component that later takes a generated mesh",
     "In the headset a virtual wall is within 5 cm of the real wall at three touch points (should: 3 cm); Home on `budget_room` costs ≤ 60 draw calls and ≤ 130k triangles per frame",
     "V")
task("P1-C08", "C", "Object photo capture",
     ["P1-C04", "P1-X02"],
     "For each object box, one to three key frames chosen by projected area, centredness, sharpness and spread of view angle; cropped and JPEG-encoded off the main thread; handed to the asset client",
     "Every box that has a candidate frame under the rule of §7.4 gets at least one crop; boxes with none are reported (they take the built-in fallback mesh in `P1-D12`); capture adds < 2 ms per frame; nothing is written to disk",
     "H")
task("P1-C09", "C", "Bump Guard",
     ["P1-C06", "P1-C01", "P1-C14"],
     "The logic of §4.10: static clearance from the distance field; a persistent dynamic layer stamped from live LiDAR depth (Burst); straight-path prediction over 0.4 s and 0.8 s; live depth alone while tracking is not Normal; a check of the static map against live depth; a level and a direction passed to `BumpCue`. Thresholds and the depth rate are constants in code, not in the parameter registry",
     "Scripted approaches at 0.5 and 1.0 m/s to a wall, to a chair and to a 0.45 m stool placed after the scan, with the walker looking level: warning ≥ 0.6 s before predicted contact in 20 of 20 trials; during a 5-minute lap of the lap square: at most 2 STRONG cues or straight-side WARNs (should: none), corner WARNs counted and reported; ≤ 0.3 ms per frame; identical with the Station unplugged",
     "E V")
task("P1-C10", "C", "Input and twins",
     ["P1-A04", "P1-A08"],
     "Gamepad through the Input System, gaze reticle with 600–800 ms dwell, headset lever tap; one action map",
     "Every SpaceLab action can be done with the gamepad alone and with gaze dwell alone",
     "E V")
task("P1-C11", "C", "Pose pipeline",
     ["P1-A04"],
     "`TrackedPoseDriver` on Update only; jumps over 0.2 m per frame rejected for geometry while safety keeps the raw pose; tracking-state events; gyro rotation prediction behind a switch",
     "A 0.5 m jump injected in replay is absent from the geometry pose stream and present in the safety pose stream; loss of tracking raises the tracking-lost event within 0.5 s",
     "E V")
task("P1-C12", "C", "Onboarding in hand",
     ["P1-C06", "P1-C07", "P1-D11"],
     "Permissions (camera, Local Network), pairing, scan route, outline confirmation, headset prompt",
     "Three first-time users reach Home in the headset with at most three prompts each",
     "H V")
task("P1-C13", "C", "Pre-scan path: world map save and relocalise",
     ["P1-C06"],
     "Save `ARWorldMap` with the RoomModel; relocalise at launch",
     "Relocalised RoomModel aligns to ≤ 5 cm within 10 s in the room it was scanned in (should: 3 cm within 5 s)",
     "H")
task("P1-C14", "C", "Warning cue and haptics",
     ["P1-C01", "P1-A04"],
     "`BumpCue` in `Atlas.Safety`: head-locked chevron on the hazard side, stand-still and rescan icons, spatialised tick and tone, CoreHaptics pulses at 2 and 6 per second, gamepad rumble if reachable (open item 9); one local entry point `Fire(level, direction)` used by Bump Guard and by the Console's STOP command",
     "For WARN, STRONG and tracking-lost the indicator, sound and haptic all start within one frame of the call in 20 of 20 trials; ≤ 0.2 ms per frame and no allocation (proposed); identical with the Station unplugged",
     "V")
task("P1-C15", "C", "FloorScan and loop-fit core in the Editor",
     ["P1-A01", "P1-X07"],
     "The grid pipeline as plain C# with no AR types: classified mesh in → 5 cm occupancy grid → signed distance field → walkable polygon with holes, dominant wall direction, free rectangle, obstacle boxes; then the loop-fit (s, L, R) and the cell grids for S1 and S3",
     "EditMode `FloorScan_Synthetic` passes on synthetic rooms (rectangle, L-shape, table in the middle); s, L and R equal the §3.2 table for empty 3 × 3 m and 4 × 5 m rectangles, and the reference values on lane C's contract page for the two furnished sample rooms; cell grids equal the examples of §4.3 and §4.7",
     "E")

# ----------------------------------------------------------------------------- lane D
task("P1-D01", "D", "Atlas.Link library",
     ["P1-X08"],
     "Plain C# (no Unity references): message types, hand-packed `State` and `Telemetry`, JSON control, clock sync, watchdog, `LinkClient` and `LinkHost` over LiteNetLib; loopback tests. It is built and tested as a .NET library first, from lane D's own contract pages, and compiles into the Unity project once `P1-A01` exists (checked at G0)",
     "Round trip for every message; 60 Hz `State` survives 2 % simulated loss; clock offset error ≤ 2 ms p95 on a simulated link",
     "S E")
task("P1-D02", "D", "Hub",
     ["P1-D01", "P1-X03"],
     "`atlas-hub` (.NET 8 console): LiteNetLib server, relay between phone, Spectators and Console, web server, JSONL and binary logs",
     "Relay adds < 2 ms; the Hub can be restarted and every client reconnects within 5 s",
     "S")
task("P1-D03", "D", "Parameter registry and Console tuner",
     ["P1-D01", "P1-D02"],
     "Registry on the phone with min, max and default per parameter; `ParamManifest` and `ParamSet`; clamps on the phone; demo lock; web page with sliders grouped by space; presets saved and loaded as JSON on the Station (`/api/presets`); Bake writes a preset into the project defaults",
     "A slider change is visible in the headset within 100 ms at p95 (proposed); with the lock on, `ParamSet` is ignored; a saved preset reloads all its values in one `ParamSet`; after Bake, a build started with the Hub off has those values",
     "E S V")
task("P1-D04", "D", "Perf probe, HUD and telemetry",
     ["P1-D01", "P1-D02", "P1-A04"],
     "`AtlasPerfProbe` (FrameTimingManager and ProfilerRecorders), in-headset HUD at 2 Hz, `Telemetry` at 1 Hz, dashboard charts, CSV on the Station and a local copy on the phone",
     "Every `Telemetry` field of Appendix B.2 appears in the CSV (the probe rows of §11.2 from frame time to managed allocation, plus the thermal state from the `P1-A02` plugin); counters whose source is not built yet read 0; the HUD costs < 0.2 ms and allocates nothing; a 15-minute run yields a complete CSV",
     "V S")
task("P1-D05", "D", "Spectator",
     ["P1-D01", "P1-D02", "P1-A06", "P1-A08"],
     "The same Unity project built for Windows with `ATLAS_SPECTATOR`; re-renders the walker's view from `State` (space, tile id, tile-local transform, parameters, head pose); world-hash check",
     "The mirror lags the phone by ≤ 150 ms at p95; a world mismatch shows a banner instead of a wrong picture",
     "S")
task("P1-D06", "D", "Operator Console",
     ["P1-D03", "P1-C14"],
     "Buttons: switch space, return to Home, recentre, quality tier, STOP (fires the warning cue and haptic), lock; comfort-score entry and notes",
     "Every command acts on the phone within 250 ms and is written to the log",
     "S V")
task("P1-D07", "D", "Flat-twin mini-map",
     ["P1-B04", "P1-D05"],
     "One renderer for the headset and the Station: Poincaré disc of the tile graph centred on the walker with the walked trail and the room outline; cube net for S4; lattice for S1; room graph for S5",
     "In replay the map shows the same tile-id sequence as the log; ≤ 0.3 ms on the phone",
     "E S V")
task("P1-D08", "D", "Forge service",
     ["P1-X03", "P1-X08"],
     "FastAPI on the Station: scan upload, object upload, job queue with one GPU worker, background removal, image-to-3D with TripoSG, job status, asset download by content hash",
     "One object photo becomes a raw mesh on the RTX 5070 in ≤ 60 s (proposed; record the measured value); a corrupt image fails its job cleanly",
     "S")
task("P1-D09", "D", "Mesh clean-up",
     ["P1-D08"],
     "Orient and scale to the scanned box, decimate to the triangle target sent with the job (1,500–5,000), regrid so no edge exceeds 0.25 m, palette colour from the photo, vertex ambient occlusion, GLB, content hash",
     "Every output passes the asset validator (triangle count, longest edge, longest side within 10 % of the box (should: 5 %), one material slot); the same input gives the same hash",
     "S")
task("P1-D10", "D", "Asset client on the phone",
     ["P1-D01", "P1-D09", "P1-C07"],
     "Uploads (RoomModel, crops), `JobStatus` and `AssetReady` handling, download with a cache, glTFast load off the main thread, material remap to the shared shader, placeholder-to-mesh swap; each job carries its object's triangle target (§4.5)",
     "Worst frame during a swap < 20 ms; a repeated photo is served from the cache; with the Station unplugged the app behaves normally",
     "H V")
task("P1-D11", "D", "Pairing and network bring-up",
     ["P1-D02", "P1-A02"],
     "Router set-up, QR page from the Hub, Local Network prompt triggered in hand with retry, link indicator",
     "A fresh install pairs by QR in ≤ 30 s (proposed); after a Wi-Fi drop the link returns within 5 s",
     "H S")
task("P1-D12", "D", "Built-in fallback set",
     ["P1-D10"],
     "One CC0 mesh per RoomPlan category, ≤ 1.5k triangles, palette-mapped, shipped in the build; placed scaled to the box after 3 failed uploads (back-off 1, 2, 4 s) or when a box has no candidate frame; a box with no RoomPlan category keeps its placeholder; tagged \"built-in\" in the log and on the Console; replaced when the Forge returns",
     "Test L19: with the Forge stopped before the scan, every box that has a RoomPlan category holds a fallback mesh within 10 s of entering Home (proposed); after a Forge restart each of them is replaced by its generated mesh; every fallback mesh has ≤ 1.5k triangles and no edge over 0.25 m after scaling",
     "V S")
task("P1-D13", "D", "On-device parameter menu",
     ["P1-D03", "P1-C10"],
     "In-headset list of the parameters of §12.2 and §12.4 (the fallback ladders and the kill switches), opened by a gamepad chord and driven by the same registry; every change written to the phone's log",
     "With `link.enabled` off, each parameter of §12.2 and §12.4 can be changed from the gamepad and the change is in the phone's log",
     "V")

# ----------------------------------------------------------------------------- gates
task("P1-G1", "G", "G1 First light",
     ["P1-A04", "P1-A13", "P1-C01", "P1-D11", "P1-D04"],
     "Stereo at 60 fps on the phone, live telemetry on the Station, the build recipe and the phone-booking rule in force",
     "Empty stereo scene at 60 fps with the HUD on; telemetry row per second on the dashboard; every engineer has installed a build; the depth image is non-null at the chosen video format",
     "V S")
task("P1-G2", "G", "G2 Curved-walk spike",
     ["P1-B02", "P1-C11", "P1-G1"],
     "Rolling update on the phone with line-drawn tiles (no shader needed); three walkers from the team. Safety for the spike: Bump Guard does not exist yet, so the floor is cleared, the lap square is marked on the real floor for the spotter (a test aid, not part of the product) and a spotter stays within arm's reach throughout",
     "Five-corner closure error < 0.25 m at loop-fit L; comfort score ≤ 3 of 20 after three minutes at loop-fit L for all three walkers; zero wall contacts and zero spotter stops. If it fails: raise L one step, or make Aligned Cells the default mode",
     "V")
task("P1-G3", "G", "G3 Spaces in the Editor",
     ["P1-A09", "P1-A11", "P1-A12", "P1-B06", "P1-B07", "P1-B08", "P1-B10"],
     "Every space module S1–S6 runs in SpaceLab in the Editor on the three sample rooms",
     "All EditMode tests green; each space selectable from the gamepad menu; shader contract unchanged since G0 or re-agreed",
     "E")
task("P1-G4", "G", "G4 Spaces on the phone at budget",
     ["P1-G1", "P1-G3", "P1-A14"],
     "Per-space metric table and the soak CSV",
     "Every space S0–S6 meets the Must column of §11.2, cold, on `budget_room`; the 15-minute soak passes",
     "V")
task("P1-G5", "G", "G5 Scan, Home and Bump Guard",
     ["P1-C07", "P1-C08", "P1-C09", "P1-C10", "P1-C11", "P1-C12", "P1-C13", "P1-C14", "P1-D04"],
     "Self-scan and pre-scan paths end in a registered Home; Bump Guard verified by someone who did not write it",
     "Both scan paths reach Home; the Bump Guard scripted test passes in the headset; a 1.4 × 1.4 m free floor selects standing mode",
     "V")
task("P1-G6", "G", "G6 Station link, all four functions",
     ["P1-D03", "P1-D04", "P1-D05", "P1-D06", "P1-D07", "P1-D10", "P1-D12", "P1-D13", "P1-C08", "P1-C09", "P1-B07"],
     "Spectator mirror, live tuning and Console, telemetry dashboard and CSV, scan and asset round-trip",
     "§7.6 table passes: state ≥ 50 Hz with < 2 % loss; mirror latency ≤ 150 ms p95; a parameter change visible ≤ 100 ms; one CSV row per second; photo to placed mesh for every object box in a test room with no frame over 20 ms; with the Forge stopped the fallback set is placed; with the Station unreachable a parameter can still be changed from the on-device menu; the app is unaffected when the Hub is unplugged",
     "V S")
task("P1-G7", "G", "G7 Phase-1 exit",
     ["P1-G2", "P1-G4", "P1-G5", "P1-G6", "P1-B09", "P1-B11", "P1-B12", "P1-A15"],
     "End-to-end run by a first-time user, watched on the Spectator",
     "Self-scan → Home with furniture arriving live → dial → 3-4-5 walk → Curved Walk → Aligned Cells (a virtual table edge stays within 5 cm of the real one) → Glued → Small Planet → Impossible Rooms → Slicer, with tuning and telemetry live; the §11.2 table is filled; every Phase-1 gate checklist in §11.3 is ticked",
     "V S")
