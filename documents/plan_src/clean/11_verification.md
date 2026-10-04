## 11. Verification: tests, metrics and gates

ATLAS is proved by automated tests (11.1), a metrics register filled with measured values (11.2) and gate checklists (11.3); 11.4 gives the measurement protocols. Rules throughout:

- Published numbers come from a Release build with Frame Timing Stats on, phone A in the closed headset (R5 §E2). "Cold" = thermal Nominal at the start; "hot" = after the 15-minute soak.
- A gate passes only when every box is ticked on one commit. A missed "must" blocks the gate; a missed "should" is recorded with its cause.
- Where a number appears both in §11 and in a task row, the §11.2 Must column and the §11.3 checklist win.
- "(proposed)" marks a value we chose with no measured or published source; it is confirmed or replaced at the gate where it is first checked.

### 11.1 Automated tests

EditMode, double precision, unless stated. All run before every merge to `main`. Two tests have a half that turns green at a later gate: the pose-jump half of `Safety_Independence` at G5, and the on-device half of `TileId_Seed` at G6.

| Test | What it proves | Pass condition | Owner lane |
|---|---|---|---|
| `Kernel_ExpLog` | exp∘log = identity for κ < 0, = 0, > 0 | Error < 1e-9 on 1,000 random points with ρ ≤ 4; < 1 mm after the float cast | B |
| `Kernel_EtaOrthonormal` | Kernel matrices stay on the group | ‖MᵀηM − η‖ < 1e-12 for generators, < 1e-9 after renormalisation (R3) | B |
| `Kernel_Inverse` | Inverse = ηMᵀη | ‖M·ηMᵀη − I‖ < 1e-12 on 1,000 random M (derived) | B |
| `Holonomy_Triangle` | Rotation round a geodesic triangle = angle defect | Error < 0.1° on 100 random triangles; sign flips with κ (R5) | B |
| `Closure_345` | The 3-4-5 walk closes | (Step·Rot 90°)ⁿ = I to 1e-12 for n = 5 at s/L = 1.0613, n = 3 at s/R = π/2, n = 4 flat; no smaller n closes (derived) | B |
| `Lorentz_Drift` | Renormalisation holds over long walks | ‖CᵀηC − η‖ (Frobenius) < 1e-9 after 100,000 random boosts; the unrenormalised value is logged only (R5) | B |
| `Float32_Upload` | The float cast is safe for the farthest tile that is drawn | \|⟨p,p⟩η + 1\| < 1e-3 for tile-relative anchors at ρ = 4.35, the farthest tile centre drawn on the high tier (3.5 + 0.8425) (R5; radius derived) | B |
| `TileId_RingInvariant` | Ids are exact | φ(X²+Y²) − T² = −1 for every tile to depth 12, evaluated in `Int128` or `BigInteger`, never in `long` (the invariant multiplies coefficients, and in `long` its products overflow from about 22 straight steps) (R3; derived) | B |
| `TileId_Growth` | The graph is {4,5} | Tiles at graph distance 0…8 = 1, 4, 12, 28, 64, 148, 340, 780, 1792 (OEIS A377322; R3 T7) | B |
| `TileId_VertexClosure` | Five squares meet at a vertex | A walk round a vertex returns the same tile after exactly 5 steps, not 4; still exact 35 tiles from home (R3) | B |
| `TileId_Unique` | No duplicates, no order dependence | Depth-6 ball by BFS, DFS and 10 random walks gives one id set; `Neighbour(Neighbour(t,k), back) == t` (R3) | B |
| `TileId_Seed` | Content is deterministic | Golden file of 64 (id, seed) pairs identical under Mono and IL2CPP (device PlayMode) (R3) | B |
| `VisibleSet_Counts` | The tile counts and the draw rule are correct | Tile centres within ρ = 2 / 3 / 3.5 / 4 of a viewer at a tile centre = 13 / 45 / 81 / 129, and 105 within 3.8425; under the draw rule of §8.5 at ρ = 3, on 500 random poses with the head pitched anywhere from 20° up to straight down, at most 71 tiles are drawn (59 when the gaze is level) and no undrawn tile has a point that is nearer than ρ and inside either eye's frustum (derived) | B |
| `TileMesh_SeamsEdges` | Tile meshes join and warp cleanly at every level of detail | Shared-edge vertices of neighbouring tiles coincide to ≤ 1 mm after the warp for every pair of levels (n = 16, 8 and 4 per side, each with the same 16 boundary segments per edge); worst screen-space chord error ≤ 0.15° from an eye height of 1.6 m; both at L = 1.4, 1.9 and 3.0 m; ≤ 1,200 / 500 / 250 triangles for the three levels at any L; five corner wedges round a post leave no gap and no overlap (A.6) | B |
| `Mode2_SightLines` | Aligned Cells instance list is complete | 3×3 cell grid: 13 / 15 / 17 instances with the user in the centre / an edge / a corner cell; ≤ 4 tiles per physical cell (R1 §A.3) | B |
| `WorldLint_All` | A generated world is usable | 100% of tiles reachable; no tile with 4 closed edges; landmark within ρ ≤ 2.5 of every tile centre; simulated draw calls ≤ 300 and triangles ≤ 500k from every tile centre × 8 headings under the draw rule of §8.5; enlarged `localBounds` on every warped renderer; every scan box has a generated mesh, a fallback mesh or a placeholder; the region of every tile to depth 6 (597 tiles) is the same on two runs, and so on the phone and on the Station; same seed → identical world (R6 §5.3) | B |
| `Glued_Handedness` | Klein copies mirror correctly | Determinant sign of copy (row, col) = (−1)^row; torus generators commute (R6 §5.3, R3 §C3) | A |
| `Portal_Graph` | Room graphs are valid | BFS from the start reaches 100% of rooms; every portal has a twin; allocated stencil references ≤ 15 per frame; every room cell inside the eroded free floor; 1,000 seeds × 5 floor shapes pass or return the fallback layout (R3) | A |
| `Slicer_Tesseract` | Mesh4D slices are correct | Corner-first slice area 0.8660 at x+y+z+w = 0.5 and 6.9282 at 2.0, to four decimals (R2 §D.10) | A |
| `Slicer_Ball` | Curved 4D bodies slice correctly | Unit 4D ball at offset c gives radius √(1 − c²) within 1% at c = 0, 0.5, 0.9 (R2 §D.10; tolerance proposed) | A |
| `Slicer_MovingSphere` | A tilted slice reproduces the scanner artefact | For a sphere moving at constant speed u, apparent length = D·v/(v − u) within 2%; at θ = 0 area within 2% of 4πR² (R2 §D.5, §D.10) | A |
| `Shader_Contract` | The look shader keeps the draw path cheap | SRP Batcher compatible; no MaterialPropertyBlock in `Atlas.*`; ≤ 8 compiled variants (proposed) | A |
| `FloorScan_Synthetic` | Floor analysis and loop-fit | Rectangle 3×3 m (free rectangle Wf × Df = 2.4 × 2.4 m) → s = 2.0 m, L = 1.885 m; 4×5 m (3.4 × 4.4 m) → s = 3.0 m, L = 2.83 m; L-shape and room-with-table: free rectangle within 0.05 m of the analytic answer, table cells not free; floor outside the confirmed outline is not free (derived) | C |
| `BumpGuard_Replay` | Warning logic on replayed poses | 20 approaches: cue fires ≥ 0.6 s before predicted contact, exactly one rising edge each (0.05 m hysteresis); on 5 replays of the lap square no STRONG cue and no WARN on the straight sides (more than 0.5 m from a lap corner, as in 11.4.5); a cue when tracking is not Normal for > 0.5 s; a 0.25 m pose jump injected in replay produces no STRONG from velocity (R1 §C.4; counts proposed) | C |
| `Safety_Independence` | Bump Guard never touches the network | Assembly half (green from G0): the `Atlas.Safety` assembly definition references neither `Atlas.Link` nor `Atlas.Telemetry`, and no source file in it uses a type from either. Pose-jump half (green from G5): an injected 0.25 m pose jump leaves the geometry frame unchanged but reaches Bump Guard (§0.5, rule 1; §6.2) | C |
| `Link_RoundTrip` | Every message survives encode → decode | Byte-exact golden round trip for every Appendix B message (the reserved `PeerState` id has no layout until `P3-D07` and is not part of this test); `State` = 202 B as laid out in Appendix B.1; unknown types and trailing bytes ignored (R4 §5.3) | D |
| `Link_ClockSync` | Offset estimate survives a lossy link | Simulated 10% loss, 15 ms one-way delay, 0–5 ms jitter (proposed): error against the injected offset ≤ 2 ms p95 (R4 §5.4) | D |
| `Link_Watchdog` | State machine is correct | DEGRADED after 500 ms silence, OFFLINE at 3 s, back-off 0.5 / 1 / 2 / 4 s, RESYNC replays the parameter snapshot (R4 §5.5) | D |

### 11.2 Metrics register

Captured by `AtlasPerfProbe` (3,600-frame ring buffer, one CSV row per second, stored on the phone and sent to the Hub) unless stated. "Per space" = each of S0–S6 alone, with the room's content, on the `budget_room` fixture (S1 in both its 5 × 5 and its 3 × 3 × 3 layout). The per-space planning figures of §8.5 are measured there at G4 and replaced by the measured values; the gate itself tests the "must" column below.

| Metric | Must | Should | Stretch | How captured | Gate where first checked |
|---|---|---|---|---|---|
| **Performance** (R5 §D1) | | | | | |
| Frame time p50 / p95 / p99 | ≤ 16.7 / 20 / 33.3 ms | ≤ 16.7 / 17.5 / 25 ms | ≤ 16.7 / 16.9 / 18 ms | `FrameTimingManager` | G1; per space G4 |
| Frames over 17.2 / 33.8 ms (one and two vsync intervals + 0.5 ms) | ≤ 5% / 0.5% | ≤ 1% / 0.1% | ≤ 0.2% / 0 | same | G4 |
| Frame rate, hot | ≥ 45 mean, never < 30 | 60 locked | 60 locked with full-size eye textures (1398 × 1290, scale 1.0) | same + Metal HUD | G7 |
| Draw calls, both eyes | ≤ 300 | ≤ 200 | ≤ 120 | `ProfilerRecorder` "Draw Calls Count" | G4 |
| Set-pass calls | ≤ 60 | ≤ 30 | ≤ 15 | "SetPass Calls Count" | G4 |
| Triangles, both eyes | ≤ 500k | ≤ 300k | ≤ 200k | "Triangles Count" | G4 |
| CPU main / render p95 | ≤ 8 / 6 ms | ≤ 6 / 5 ms | ≤ 4 / 3 ms | `cpuMainThreadFrameTime`, `cpuRenderThreadFrameTime` | G1, G4 |
| GPU p95, cold / hot | ≤ 9 / 14 ms | ≤ 7 / 11 ms | ≤ 5 / 8 ms | `gpuFrameTime`, checked against the Metal HUD | G4, G7 |
| Resident memory | ≤ 1.5 GB | ≤ 1.0 GB | ≤ 700 MB | "System Used Memory" | G4 |
| Managed allocation per frame | ≤ 1 kB | 0 | 0 | "GC Allocated In Frame" | G4 |
| Thermal timeline, 15-minute soak | No Critical; Serious not before 10 min | No Serious | Nominal or Fair only | Native `thermalState` plugin, 2 s poll | G4, G7 |
| Time-to-Fair in the soak, LiDAR depth on against off | reported | — | — | 11.4.1 (the two soaks) | G4 |
| Slicer p95: tesseract / tumour tube | ≤ 0.5 / 1.0 ms | ≤ 0.2 ms / — | ≤ 0.05 ms / — | `ProfilerMarker("Atlas.Slicer4D")` (R5, R2 §D.10) | G4, G9 |
| Motion-to-photon, rotation | Measured; ≤ 100 ms | ≤ 50 ms | ≤ 30 ms | 11.4.2 | G7 |
| **Geometric correctness** | | | | | |
| 3-4-5 walk closure error | < 0.25 m at loop-fit L | < 0.15 m (proposed) | < 0.10 m (proposed) | Virtual pose logged at start and end (R1 §D.8) | G2, G7 |
| Measured holonomy against prediction | Within 5° | Within 2° (proposed) | — | World-turn read-out of the 3-4-5 walk (`P1-B09`), logged; the prediction is 90° for one lap at loop-fit L | G7 |
| In-app Lorentz drift | < 1e-9 always | < 1e-12 | — | Computed every 60 frames (R5) | G3 |
| Holonomy update / tile-crossing frame | ≤ 0.1 / 2 ms | ≤ 0.02 / 0.5 ms | — / ≤ 0.1 ms | `ProfilerMarker` (R5) | G3 |
| Phone and Spectator tile-id agreement | 100% of samples on a closed loop | — | — | Hub log (R4) | G6 |
| **Scan** | | | | | |
| Scan end → walkable Home | ≤ 90 s | ≤ 60 s | ≤ 30 s (proposed) | Event log (R5 §E3) | G5 |
| Free rectangle Wf, Df against a tape measure | ≤ 0.10 m error | ≤ 0.05 m (proposed) | — | Tape, two rooms: the clear span between walls or furniture, less 0.6 m (§4.4) | G5 |
| Loop-closure drift of raw ARKit pose, 20 m | < 2% (0.40 m) | < 1% | < 0.5% | 11.4.3 (R5) | G5 |
| **Bump Guard** (thresholds of §4.10; pass marks proposed) | | | | | |
| Warning lead distance at 1.0 m/s | ≥ 0.60 m | ≥ 0.80 m | — | 11.4.5 | G5 |
| Warning lead time to predicted contact | ≥ 0.6 s | ≥ 0.8 s | — | 11.4.5 | G5 |
| Missed warnings in 20 scripted approaches | 0 | 0 | 0 | 11.4.5 | G5 |
| STRONG cues, and WARN cues on straight sides, on a 5-minute lap of the lap square (0.5 m from the walls) | ≤ 2 | 0 | 0 | 11.4.5 | G5 |
| Corner WARN cues on that lap | reported | — | — | 11.4.5 | G5 |
| Static-map error after the 5-minute lap (touched real wall against the SDF edge, less the 0.05 m offset of §4.2, three points) | ≤ 0.10 m | ≤ 0.05 m | — | 11.4.5 | G5 |
| Frame-time cost of live LiDAR depth, p95 | ≤ 1.0 ms | ≤ 0.5 ms | — | Probe, depth on against off; the depth-off run uses a recorded pose track with nobody wearing the headset | G5 |
| **Link** (R4 §5.10) | | | | | |
| Connect time, launch → CONNECTED | ≤ 3 s p95 of 10 launches | — | — | Hub log | G6 |
| `State` rate and loss | ≥ 50 Hz mean, < 2% | ≥ 58 Hz mean, < 0.5% | — | Sequence numbers at the Hub | G6 |
| Phone → Hub latency, clock-corrected | p95 ≤ 25 ms, p99 ≤ 50 ms | p50 ≤ 10 ms | — | `t_us` against receive time | G6 |
| Phone → Spectator, screen to screen | ≤ 150 ms | ≤ 100 ms | ≤ 60 ms | 11.4.6 (R5, R4) | G6 |
| Parameter round trip, slider → phone, p95 | ≤ 100 ms on a clean link; ≤ 250 ms at 10% simulated loss | ≤ 100 ms at 10% simulated loss | — | `ParamSet` echo (11.4.6) | G6 |
| Operator command, Console → effect on the phone | ≤ 250 ms for each of the six commands | — | — | Hub send time against the phone's log entry, clock-corrected (test L10) | G6 |
| Clock offset jitter | ≤ 2 ms p95 | — | — | Hub log | G6 |
| Main-thread p95, link on against off | Differs ≤ 0.2 ms; 0 B allocated by `Atlas.Link` | — | — | Two probe runs (R4) | G6 |
| **Asset round trip** | | | | | |
| Forge job start → placed mesh (`AssetAck`), per object | ≤ 60 s | ≤ 30 s (proposed) | ≤ 20 s | `Event` timestamps (test L15); the wait in the queue is reported separately | G6 |
| Triangles after clean-up | 1,500–5,000 per object and no more than the target sent with the job; ≤ 60,000 for all generated meshes in the room; edges ≤ 0.25 m | — | — | Forge report (BP §5; target rule in §4.5) | G6 |
| Scale error against the scanned box | ≤ 10% of the longest side (proposed) | ≤ 5% | — | Forge report against RoomModel | G6 |
| Worst frame while a GLB loads | < 20 ms | ≤ 17.2 ms (no frame over one vsync interval + 0.5 ms) | — | Frame log | G6 |
| **Comfort and usability** (R5 §D2) | | | | | |
| FMS (Fast Motion Sickness scale, 0–20, spoken each minute), median peak | ≤ 5 | ≤ 3 | ≤ 2 | Operator types into the Console | G2, G7 |
| Pointing-to-origin error after a 3-leg walk | Measured in S0 and S2, n ≥ 5 | S2 within 1.5× of S0 | Within 1.2× | `probe` event of `P2-C17`: head ray at a button press against the geodesic and the Euclidean prediction | G8 (first values); n ≥ 5 in `P4-C05` |
| Single Ease Question (1–7), median | ≥ 5 | ≥ 5.5 | ≥ 6 | Console SEQ field of `P2-C17`, one question per use case | G8 (first values); n ≥ 5 in `P4-C05` |
| Onboarding: launch → first completed walk | ≤ 90 s | ≤ 60 s | ≤ 30 s | Event log | G7 |

### 11.3 Gate checklists

**G0 Contracts frozen** (present: all four lanes)
- [ ] RoomModel fields, the `Atlas.Link` schema (Appendix B), parameter registry ids and the look-shader property list are merged and tagged; the three sample rooms and the performance fixture `budget_room` are in the repository and validate against the RoomModel schema.
- [ ] Assembly definitions exist; the assembly-reference half of `Safety_Independence` is green (the pose-jump half is checked at G5).

**G1 First light** (present: A, C, D)
- [ ] Stereo rig, empty scene, phone A: 60 fps on the Metal HUD; CPU main ≤ 3 ms, GPU ≤ 2 ms (R5 §E3).
- [ ] Unity ≥ 6000.3.21f1, thermal frame-rate setting off; the thermal plugin (delivered by `P1-A02`) returns a value.
- [ ] Lowest-resolution 60 Hz ARKit configuration shown in the start-up log; the environment depth image is non-null at the chosen video format.
- [ ] The phone pairs with the Hub; `State` packets from the phone appear in the Hub log, and the dashboard shows one telemetry row per second.
- [ ] One command installs the current `main` on a phone, and every engineer has installed a build.
- [ ] Both phones report LiDAR (`P1-X04`); rear camera and LiDAR are unobstructed in the headset.

**G2 Curved-walk spike** (present: B, C, three walkers from the team, a spotter)
- [ ] Safety for the spike is in place, because Bump Guard does not exist yet: the walkers are team members only, the floor is cleared, the lap square is marked on the real floor for the spotter (a test aid, not part of the product), and a spotter stays within arm's reach throughout.
- [ ] Protocol 11.4.4 run; pass or fail recorded per walker, with wall contacts and spotter stops counted.
- [ ] Decision recorded: Mode 1 default, Mode 1 at a larger L, or Mode 2 default; the comfort floor for L and R is recorded with it.

**G3 Spaces in the Editor** (present: A, B)
- [ ] Every 11.1 test owned by lanes A and B is green (the on-device half of `TileId_Seed` is checked at G6).
- [ ] S1–S6 each load in SpaceLab under AR simulation, on the three sample rooms, and expose their parameters (S0 is checked at G5).
- [ ] Mode 1 and Mode 2 switch on one tile graph with no tile-id change.

**G4 Spaces on the phone at budget** (present: A, B, D)
- [ ] S0–S6 each meet every "must" performance row, cold, on `budget_room` with the room's content loaded (the Home shell and all twelve stand-in objects; S1 in both its 5 × 5 and its 3 × 3 × 3 layout); the measured values are written into the register and replace the planning figures of §8.5.
- [ ] Fog is fully opaque at ρ = 3 in both eyes, and every tile that reaches inside that radius and can be in view is drawn: no hole in the floor and no popping while walking or turning, and the edge of the drawn world cannot be told from the background.
- [ ] The 15-minute soak (11.4.1), alternating S2 Curved Walk (ρ = 3, the room as tile zero) and S1 Glued 5 × 5 on `budget_room`, passes the thermal row.
- [ ] The soak is repeated once with depth off, driven by a recorded pose track with nobody wearing the headset, and the difference in time-to-Fair is recorded.
- [ ] Each thermal-ladder tier forced from the Console takes effect within 1 s and sets the eye-texture size (render scale), MSAA, ρ and Glued copies of its row in §8.6.

**G5 Scan, Home and Bump Guard** (present: C, B, D)
- [ ] Self-scan produces a RoomModel, the walker confirms the outline, and all scan rows are at "must" in two different rooms.
- [ ] A RoomModel with a 1.4 × 1.4 m free floor selects standing mode.
- [ ] Home (S0) loads in SpaceLab and exposes its parameters; a touched real wall lies within 0.05 m of the virtual wall at three points (should: 0.03 m).
- [ ] Pre-scan path: a saved ARWorldMap relocalises within 10 s to ≤ 5 cm (should: 5 s, 3 cm).
- [ ] Meshing and plane detection are off after the scan; LiDAR depth stays on.
- [ ] Warning cue (`P1-C14`): for WARN, STRONG and tracking-lost the indicator, sound and haptic all start within one frame of the call in 20 of 20 trials. The cue is indicator, sound and haptic only, and the world does not change.
- [ ] Protocol 11.4.5, run by an engineer who did not write Bump Guard, passes every Bump Guard row at the depth rate that ships (at least 15 Hz), with the constants confirmed by `BumpGuard_Replay`.
- [ ] Static-map error after the 5-minute lap is ≤ 0.10 m at three points.
- [ ] The pose-jump half of `Safety_Independence` is green.
- [ ] Bump Guard behaves identically with the Station switched off.
- [ ] Input and pose (`P1-C10`, `P1-C11`): every SpaceLab action can be done with the gamepad alone and with gaze dwell alone; a 0.5 m jump injected in replay is absent from the geometry pose stream and present in the safety pose stream; loss of tracking raises the tracking-lost event within 0.5 s.
- [ ] Onboarding in hand (`P1-C12`): three first-time users reach Home in the headset with at most three prompts each.

**G6 Station link, four functions** (present: D plus one engineer from each other lane)
- [ ] Every test in the table of §7.6 passes.
- [ ] Spectator mirror: tile-id agreement and screen-to-screen latency at "must"; the on-device half of `TileId_Seed` is green and the world hash is the same on the phone and the Station.
- [ ] Live tuning: curvature, fog, space id and 4D slice change from the Console; values clamp; all six operator commands (switch space, return to Home, recentre, quality tier, STOP (warning cue), lock) act on the phone within 250 ms.
- [ ] Telemetry: a 15-minute run plotted with no gap > 2 s; the CSV exists on the Station and the phone.
- [ ] Scan and asset round trip, in a room with at least four object boxes: every box with a candidate frame is photographed, generated by the Forge, returned and placed (at most 20 generated meshes per room, largest boxes first); asset rows at "must"; a placeholder is shown until each mesh arrives.
- [ ] Fallback set (`P1-D12`): with the Forge stopped before the scan, every box holds a built-in fallback mesh within 10 s of entering Home (proposed); after a Forge restart each is replaced by its generated mesh.
- [ ] With the Station unreachable, a parameter from the kill-switch list of §12.4 is changed from the on-device menu and the change is in the phone's log.
- [ ] Flat-twin mini-map (`P1-D07`): in replay the map shows the same tile-id sequence as the log; it costs ≤ 0.3 ms on the phone.
- [ ] Router unplugged for 10 s: OFFLINE within 3.5 s, CONNECTED within 5 s of return, parameters restored, frame time unchanged (R4).

**G7 Phase-1 exit** (present: all four lanes)
- [ ] G0–G6 and every 11.1 test green on one commit.
- [ ] All four Station functions shown in one session without a restart.
- [ ] S0, S1, S2, S3, S4, S5 and S6 each run on phone A at "must" budget in the room scanned for the session; the per-space table is measured again on that scan and recorded beside the G4 values.
- [ ] Self-scan → Home with live AI furniture: at least three objects generated and placed in the session.
- [ ] The 3-4-5 walk closes within 0.25 m after 3, 4 and 5 corners; curvature never changes while head speed exceeds 0.1 m/s.
- [ ] Mode 1 and Mode 2 are both walked from the same tile zero.
- [ ] Room content in the registered spaces: in Glued (S1) the room's walls (as kerbs) and furniture are drawn once, where they really are, among the copies (`P1-A15`); in Aligned Cells (S3) each object is drawn in its own cell (`P1-B12`). In both, a virtual table edge is within 5 cm of the real one at three touch points.
- [ ] Bump Guard warns at a wall and at a chair moved after the scan.
- [ ] The 15-minute in-headset soak passes; motion-to-photon and loop drift are measured.
- [ ] Every register row first checked at G1–G7 holds a measured value.

**G8 Playground run** (present: A, C, two first-time users)
- [ ] A first-time user completes the Geometry Playground with walk, look and one button.
- [ ] Every space shows one predicted-against-measured number.
- [ ] Comfort and usability rows hold a measured value; n ≥ 5 is reached in `P4-C05`.

**G9 Lung 4D** (present: A, B, D)
- [ ] Toybox, breathing worldtube, breath lane and scanner-artefact demo all run on the phone, on both case packages (real and illustrative).
- [ ] ITV, core and gated volumes on screen equal `table.json` within 1% (R2 §D.10).
- [ ] `P2-A31`: slicer p95 ≤ 1.0 ms and frame p95 ≤ 17.5 ms through a soak with the slice moving, on both packages; thermal state never Serious.
- [ ] `P2-A32`: beats a and c–f advance both ways on both packages. The footer "Education and research visualisation. Not for clinical use." is on every screen, every asset shows exactly its label of §10.2.1 ("Illustrative model" on the twin; "Public research data: TCIA 4D-Lung, CC BY 3.0. Meshes derived by ATLAS." on the real case), and the credits show the TCIA citation.

**G10 Lung airway and sky** (present: B, D)
- [ ] Procedural airway snapped to the {4,5} tile graph, 16 generations addressable, no overlap, labelled "model-generated".
- [ ] `P2-B12`: a first-time user walks four forks without the rescue beacon in ≥ 9 of 10 runs, comfort score ≤ 3; walking the centre strip for 8 tile steps raises no WARN on the straight sides.
- [ ] `P2-B10`: the needle sky reads precomputed maps (no per-frame compute); clearance shown equals `sky.json` within 0.1 mm at 20 directions.
- [ ] `P2-B09`, `P2-B11`, `P2-D09`, `P2-D11` and `P2-D13` are accepted, and all eight beats of `P2-A32` (a to h) run end to end on the phone; the airway run ends in the lesion-chamber hand-over.
- [ ] `P2-D12`: the plan card validates and prints from a real run.

**G11 Phase-2 exit** (present: all four lanes)
- [ ] G8–G10 green; performance rows at "must", hot, in both use cases.
- [ ] FMS median peak ≤ 5 with three people who did not build the use case.
- [ ] Set-pass calls ≤ 60; a kill switch exists for everything Phase 2 added.

**G12 Shared House** (present: A, C, both phones)
- [ ] `P3-C05`: all seven beats of Post Circling completed by three walkers with no contact: through the door switches sheet, round the frame does not, round one post does, a second lap returns.
- [ ] (Extended) House Call: a live second walker on phone B appears on the right sheet within 0.1 m.
- [ ] The recorded visitor carries a visible "recorded" label.
- [ ] `P3-C03`: on sheet 1 Bump Guard gives the cue and the haptic ≥ 0.6 s before predicted contact in 10 of 10 approaches to a real obstacle, as on sheet 0.

**G13 FieldFit** (present: B, D)
- [ ] Blind spot found within 30 s by ≥ 4 of 5 testers; the composed final blit costs < 1 ms.
- [ ] `P3-D06`: median search time with the power-law lens is lower than with the tunnel alone for ≥ 4 of 5 testers.
- [ ] FieldFit meets the "must" performance rows of §11.2 with the lens on.
- [ ] No clinical claim in any string.

**G14 Feature freeze** (present: all four lanes)
- [ ] Full demo path twice in a row on one phone without cooling: no Serious, p99 ≤ 33.3 ms (R5 §E3).
- [ ] "Should" reached on the frame-time, CPU and GPU rows, or each miss documented.
- [ ] Recovery from a forced tracking loss within 15 s.

**G15 Golden build and submission** (present: all four lanes)
- [ ] The golden build launches with Wi-Fi off and runs the full demo with the Station unplugged.
- [ ] The filled register, one soak chart and its CSV are in the repository.
- [ ] A fresh clone builds from the README; architecture diagram, maths note, licences and AI-asset credits are present; the comment audit (`P4-A05`) finds a summary on 100% of public `Atlas.*` types.
- [ ] No camera frame from any visitor is stored, and `FORGE_DEBUG_KEEP` is off; the booth sign's data line discloses the hosted fallback (§7.4); delete-by-id removes every file for a session id on the Hub, the Forge and the phone (`P4-D02`).

### 11.4 Measurement protocols

#### 11.4.1 15-minute soak
1. Release build, phone A in the closed headset, brightness 40%, start at thermal Nominal.
2. A walker, or a recorded pose track, alternates S2 Curved Walk (ρ = 3, the room as tile zero) and S1 Glued 5 × 5 for 15 minutes with LiDAR depth on. At G4 the room is `budget_room`; at G7 it is the room scanned for the session. The tour script of `P1-A14` fixes how often the space changes, and that interval is recorded with the result.
3. `AtlasPerfProbe` writes one CSV row per second; the Console plots frame time and thermal state.
4. Pass: every "must" performance row holds; no Critical; Serious not before 10 min. Record time-to-Fair and time-to-Serious.
5. At G4, repeat the soak once with depth off, driven by a recorded pose track with nobody wearing the headset, starting again from thermal Nominal, and record the difference in time-to-Fair.
6. Commit each run's file as `perf_<session>.csv`.

#### 11.4.2 Motion-to-photon by slow-motion video
1. The app shows a world-locked vertical line. Phone B films at 240 fps (4.17 ms per frame) with the test phone's body and screen in shot.
2. Twist the test phone sharply against a stop.
3. Count frames from first physical movement to first on-screen movement.
4. Ten trials, gyro prediction off and on; report mean and range (resolution ±4 ms) (R5 §B4).

#### 11.4.3 20 m loop drift
1. Mark a start point on the floor; in the headset, press the button on the mark.
2. Walk a 20 m closed path (for example two laps of a 2 m × 3 m rectangle, or five laps of a 1 m square), return to the mark, press again.
3. Drift = distance between the two logged raw ARKit positions.
4. Three runs; report the worst. Pass: < 0.40 m.

#### 11.4.4 Curved-walk spike
1. Safety for the spike: Bump Guard does not exist yet. The walkers are team members only; the floor is cleared; the lap square is marked on the real floor for the spotter (a test aid, not part of the product); a spotter stays within arm's reach of the walker throughout. Warnings are not counted; wall contacts and spotter stops are.
2. Apply the loop-fit rule to the test room, measured wall to wall with a tape (the scan does not exist yet); record s and L.
3. Three walkers each walk the pentagon: five sides of s, a 90° left turn at each corner. Closure error = distance between virtual start and end.
4. Each walker then walks freely for three minutes at each of 0.5·L, L and 1.5·L (0.9, 1.9 and 2.8 m in a 3×3 m room; R1 §D.8), speaking an FMS score each minute. Wall contacts and spotter stops are counted.
5. Pass rule: closure < 0.25 m, every FMS ≤ 3 at loop-fit L, zero wall contacts and zero spotter stops, for all three walkers → Mode 1 stays the default.
6. Otherwise raise L and repeat once; if that fails, Mode 2 becomes the default and Mode 1 a switchable demonstration.

#### 11.4.5 Bump Guard scripted approach
Set-up: the build under test processes depth at the rate that ships (never below 15 Hz), and that rate is recorded. The vertical field of view of the depth image, with the phone in the headset, is measured and recorded.

1. Scanned room with one wall and one scanned table, plus one chair and one stool 0.45 m high placed after the scan (visible only to live LiDAR).
2. A walker in the headset, looking level and not at the floor, with a spotter, makes 20 approaches, five to each of the four obstacles: head-on and at 45°, each at about 0.5 and 1.0 m/s, and a second head-on approach at 1.0 m/s. Every approach starts at least 3 m from the obstacle and reaches it within 30 s, because a level view from 1.6 m first sees the top of a 0.45 m stool from about 2.6 m.
3. The walker stops at the cue. The log gives the lead distance (from the head to the obstacle when the cue starts; the obstacle's position is taken with a tape measure) and the lead time to predicted contact. The distance at which the walker came to rest is measured with the tape and recorded beside them.
4. One run of five minutes lapping the lap square (0.5 m from the walls) counts STRONG cues anywhere and WARN cues on the straight sides, and separately the WARN cues at the corners. A WARN is a corner cue when the head is within 0.5 m of a lap corner as the cue starts (at 1.0 m/s a corner WARN starts 0.40 m before the corner; derived); every other WARN is on a straight side. Corner cues are reported, not judged (open item 19 in §14).
5. After the lap, repeat the three-point wall touch of the G5 checklist and record the static-map error: how far the edge of the distance field lies from each touched point on the real wall, after allowing for the one-cell (5 cm) inset of that edge.
6. Pass: all Bump Guard rows at "must".

#### 11.4.6 Link latency
1. Own router; phone in the headset; Hub and Spectator running.
2. Hub latency: the Hub logs clock-corrected `t_us` → receive time over five minutes of walking; report p50, p95, p99.
3. Screen to screen: a 240 fps camera films the phone screen and the Spectator monitor together during a sharp head turn; count frames between the two; ten trials.
4. Parameter round trip: the Console stamps each `ParamSet`, the phone echoes the applied value in an `Event`; report p95 with and without 10% simulated loss.

#### 11.4.7 Asset round trip
1. Scan a room holding a tape-measured box and three ordinary objects.
2. For each object the log records: photo sent, Forge start, GLB ready, mesh placed.
3. Report per object: Forge start → placed time (the register row), photo sent → placed time (which includes the wait in the queue), triangle count against the target sent with the job, longest edge, and longest-side scale against the scanned box.
4. Read the frame log during each GLB load.
5. Pass: all asset rows at "must" for four objects out of four.
