## 12. Risks and fallbacks

Wherever possible a fallback is a parameter change, not a code change. Parameters live in the registry (§7.3.2) and are set from the Console; the parameters of §12.2 and §12.4 are also in the on-device menu (task `P1-D13`), so every ladder and every kill switch works with the Station unreachable. Bump Guard is the deliberate exception: its thresholds and depth rate are constants in code (§4.10) and change only in a rebuild. Likelihood / impact: H high, M medium, L low.

### 12.1 Risk register

| # | Risk | Why it matters (rubric line or safety) | Likelihood / impact | Early signal (gate or metric) | Mitigation built into the plan | Fallback if it happens |
|---|---|---|---|---|---|---|
| **Safety and comfort** | | | | | | |
| 1 | Bump Guard is a warning cue and haptic only; the walker never sees the room (D19) | Safety | M / H | G5 protocol 11.4.5: any missed warning in 20 approaches; more than 2 STRONG cues or straight-side WARN cues on the 5-minute lap | Two sources (static scan map, live LiDAR depth kept on); thresholds set for a lead of at least 0.6 s and confirmed on a recorded lap by `BumpGuard_Replay` before G5; walker confirms the scan outline before the headset goes on | Human spotter, recommended whenever someone outside the team walks; stay in registered spaces (S0, S1, S3, S5, S6); if the test shows it is needed, raise the WARN and STRONG distance and look-ahead constants in code and rebuild (they are not registry parameters). WARN cues at the lap corners are open item 19 (§14) |
| 2 | Live depth covers a forward cone only (about ±35°, 0.3–5 m) (Spec §7), and a headset that looks level cannot see low obstacles close by (§4.10) | Safety: side and back steps, people behind, low objects | H / H | G5 protocol 11.4.5: a missed warning at a 45° approach or at the 0.45 m stool placed after the scan | Static map covers every direction; depth covers what is new ahead, and the dynamic layer keeps what it has seen (a cell is cleared only when seen empty, or after 30 s without a new hit); cables and loose objects are removed before a session | Spotter covers the rear; onboarding says "walk forwards" |
| 3 | Furniture moved after the scan | Safety: stale static map | M / H | Depth returns inside cells the map marks free | Depth overrides the map; rescan is one user action | Rescan; spotter |
| 4 | Virtual walls and furniture drift off the real ones in Curved Walk (D9, §4.6) | Safety; UX 30% | H / M | By design: one lap turns the world (§3.3) | Bump Guard works in tracking space only; onboarding states nothing in S2 is solid | `space.id` → S3 or S0, which stay registered |
| 5 | Smooth H²×E walking has no published comfort study (R1) | UX 30% | M / H | G2: FMS > 3/20 or pentagon closure error ≥ 0.25 m | G2 runs before S2 content is built; no gains, no vignette (BP §0.5) | `curved.mode` → Aligned Cells as the walked default; S2 stays a short opt-in |
| 30 | Cybersickness outside Mode 1: mirrored Klein rows, portals, the FieldFit lens | UX 30% | M / H | FMS median peak above 5, the "must" of §11.2, in any space: read at G8 and G11, on exit from Shared House (§10.3.7) and in the pilot (§13.5); a lens trial stopped for discomfort at G13 | Every step is 1:1, with no gains (rule 3 of §0.5): the Glued cover is fixed to the floor (D15) and portal transforms are the identity (D16); curvature changes only while standing (§10.1.4); lens beats are standing or seated, and the lens switches off when the head moves more than 0.5 m (§10.4.5); FMS asked each minute with a stop value (open item 6, §14) | Skip the space, or switch the lens off; `space.id` → S0 |
| **Geometry correctness** | | | | | | |
| 6 | 64-bit tile ids are exact to 42 straight tile steps and overflow at 43. The walkable lung route is 32 tile steps (16 generations, §10.2.3); 46 is only the addressable depth (23 generations), and on the airway's paths the coefficients need about 52 bits at 46 steps, so `long` suffices there (R3, derived) | Creativity 30%: wrong tiles | L / H | `checked` overflow exception in the id arithmetic; the invariant φ(X²+Y²) − T² = −1 fails. The invariant multiplies coefficients, so it is evaluated in Int128 or BigInteger: in `long` its products overflow from about 22 straight steps | `checked` arithmetic; ids behind one struct | 128-bit add-only struct (≈ 83 steps, R3); root lung ids at the lobar bronchus |
| 7 | Plain square tiles leave gaps under the log map (R6) | Graphics 25% | M / M | G3: seams at tile edges | Tile generator maps the Klein square to the chart (§3.4, A.6) | Regenerate; none needed at runtime |
| 8 | Float matrices lose precision far from the anchor (R3) | Graphics: jitter | M / M | G3: shimmer on far tiles | Double on CPU; re-anchor by tile id; no tile whose centre is farther than ρ + 0.8425 is drawn (3.84 at ρ = 3; draw rule of §8.5) | Lower `curved.cullRho` |
| 9 | Sphere antipode breaks the log map (R6) | Graphics: S4 artefacts | M / L | G3: stretched triangles behind the planet | Fragment discard near the antipode; tight fog | Lower `planet.fogRho` |
| 10 | ARKit relocalisation jump integrated as motion | Creativity: world slides | M / M | Pose-jump counter in telemetry | Jumps > 0.2 m per frame rejected for geometry; Bump Guard always uses the raw pose and resets its velocity filter on such a jump, so a relocalisation raises no false look-ahead cue (§4.10) | "Return to Home" re-anchors on tile zero |
| **Performance and heat** | | | | | | |
| 11 | Thermal "Serious": Unity caps at 30 fps unless the Player Setting is off (R5) | Graphics 25% | M / H | Thermal state in telemetry; G4 soak | Unity ≥ 6000.3.21f1; thermal ladder acts at "Fair"; dark palette; meshing off after the scan | Lower rungs (§12.2); swap to phone B |
| 12 | LiDAR depth stays on while walking; heat cost not published (R1) | Heat against safety | M / M | G5 depth on/off comparison: frame-time p95 rise > 1.0 ms; G4 soak: time-to-Fair with depth on against off | Budgets are measured with depth on; the G4 soak is run once more with depth off, on a recorded pose track with nobody in the headset, to measure the difference (`P1-A14`) | Processing fewer depth frames saves CPU only (the rate is a code constant, lowered in a rebuild and never below 15 Hz, §4.10); for heat, start the thermal ladder one rung lower. Depth is never switched off while someone walks |
| 13 | Two-camera rig is CPU-bound; the tile centres within ρ number about 5(cosh ρ − 1), and more tiles than that are drawn | Graphics: > 300 draw calls | M / H | G4: draw calls, render-thread ms | Fog fully opaque at ρ = 3; 45 tile centres lie within ρ = 3 and at most 71 tiles are drawn (draw rule of §8.5); levels of detail | ρ 2.5 (25 tile centres, at most 39 tiles drawn with a level gaze and 52 at any pitch) → 2 (13 centres, at most 26 level and 38 at any pitch); `RenderMeshInstanced` for shells. If the view-cone test is not built: cull centres at ρ = 3 and make the fog opaque at ρ = 2.15 |
| 14 | HyperEngine's warp shader uses MaterialPropertyBlocks and instanced properties: not SRP-Batcher compatible as written (R3) | Graphics: batching lost | H / M | Frame Debugger at G3 reports "not compatible" | Port moves the tile anchor into `UnityPerMaterial`; one pooled material per tile | Instanced variant of the same shader |
| **Scan and tracking** | | | | | | |
| 15 | RoomPlan through AR Foundation gives furniture boxes only (R1) | UX: scan flow | H / M | G5: no wall data from the route in use | Ranked scan routes; walls from planes and classified mesh | `scan.route` → manual corner taps |
| 16 | Tracking drift or loss inside a lens headset; iOS 26.4 LiDAR drift report, said fixed in 26.6.1 (R1; re-checked under open item 3 of §14) | Safety; closure error | M / H | G2: closure error ≥ 0.25 m. G5: static-map error after the 5-minute lap > 0.10 m, or 20 m loop drift ≥ 0.40 m (§11.2) | iOS version recorded at set-up; headset exposes cameras and LiDAR; while tracking is not Normal, Bump Guard uses live depth alone and raises "stand still" at 0.5 s; a static map that disagrees with live depth by more than 0.15 m for 2 s raises the "rescan" icon (§4.10) | Rescan; team pre-scan with saved ARWorldMap (D21); update iOS |
| 17 | Phone B has no LiDAR or is slower (open item 2, §14) | No swap phone | L / M | Set-up checklist; G4 on phone B | LiDAR on both phones verified at G1 (`P1-X04`); budgets measured on both phones | Phone B never walks; it takes object photos only |
| **Link and Station** | | | | | | |
| 18 | LiteNetLib 2.1.4 unverified on Unity 6.3 + IL2CPP + iOS 26 (R4) | Blocks G6 | M / H | G1 link smoke test | `Atlas.Link` hides the transport behind one interface | Raw UDP with the same `State` packing |
| 19 | LiteNetLib needs Wi-Fi even in development (`iproxy` forwards TCP only) (R4) | Process | H / L | First paired session | Own router; QR pairing to a known IP | Editor client on the PC for link work |
| 20 | Local Network prompt denied; Windows Firewall drops UDP (R4) | Link silent | M / M | Hub prints "no packets received" after 10 s | Prompt raised in onboarding with retry; firewall rule in the set-up script | Phone runs standalone |
| 21 | Venue radio congestion | Mirror stutters | M / M | Packet loss and RTT in telemetry | ATLAS app never depends on the link | Lower `link.stateHz`; recorded-state replay |
| **Live AI mesh pipeline** | | | | | | |
| 22 | Forge slow, down or unreachable (D20) | UX: built-in furniture in place of the walker's own | M / M | G6: Forge job start to placed mesh takes more than 60 s per object (§11.2, test L15) | Placeholder box at once; retry; results cached by photo hash | `forge.backend` → hosted API (only if the walker was told and agreed, §7.4) → cache → built-in fallback set (`P1-D12`) |
| 23 | TripoSG output is geometry-only, over budget or the wrong shape; the RTX 5070 needs PyTorch cu128 (BP §0.5) | Graphics; warp needs a ≤ 0.25 m grid | M / M | Forge post-check: triangle count, bounds against the box | Decimate to the triangle target sent with the job, regrid, palette colour, scale to the scanned box | Reject the mesh; the job fails and the box takes its built-in fallback mesh (`P1-D12`) |
| **Medical data and claims** | | | | | | |
| 24 | Real tumours barely move (median 4.3 mm SI, R2) | UX: worldtube looks static | H / M | G9: first render of patient 101 | Illustrative twin (10 mm nodule, 15 mm motion) beside it, labelled (D25) | Exaggeration slider with the factor on screen, on the illustrative twin only; never on the real case |
| 25 | Over-claiming; "4D shadow" and "tilted slice" framings are ours (R2) | Presentation 15%; ethics | M / H | A clinical claim in any string at G13 (§11.3); a hit in the phrase check (`P4-D05`) | "Not for clinical use" on screen; labels "illustrative model", "model-generated"; the never-say list of §13.4, which the phrase-check script uses | Remove the claim |
| 26 | Licences: TCIA CC BY 3.0, AeroPath CC BY 4.0, HyperEngine MIT notice; no GPL code (HyperRogue); ATM'22 labels not redistributable; Hunyuan3D off; Asset Store packs out of the repo | Submission validity | M / M | Ledger incomplete at G14 | Ledger row added with every dependency; TCIA data-use terms acknowledged and the citation recorded by G9 (open item 18, §14) | Drop the asset; procedural substitute |
| 31 | Privacy: room photos and motion logs of walkers from outside the team | Ethics; Presentation 15% | L / H | A crop file left on the Forge after its job (test L20 of §7.6, at G6); the "crops kept" banner on the Console with an outside walker; any image byte in a log (`P4-D02`) | Rule 10 of §0.5: no camera frame is stored, and crops are held in memory and deleted when the job finishes; the hosted fallback is disclosed and can be declined (§7.4); anonymous ids, motion logs kept encrypted on the Station, delete-by-id (§13.5); the data line on the booth sign | Set `forge.backend` to cache when a walker declines the hosted service; delete that walker's data by id (§13.5); run the session with `link.enabled` off, so the Station records no log |
| **Team process** | | | | | | |
| 27 | One Mac is the only iOS build seat; free-profile signing expires (Spec §15.8) | Every device gate queues behind it | H / H | Build queue length; install failure | Editor simulation in all lanes; one-command build script; lane A owns the seat | Device tests batched per gate; reinstall before each demo |
| 28 | Shared scenes: four lanes touch SpaceLab | Merge conflicts | H / M | Conflicts in `.unity` files | One additive scene per space module; only lane A edits SpaceLab | Revert to last green; re-apply as a prefab |
| 29 | Shared shader: look (lane A) and warp include (lane B) in one file set | One edit breaks every space | M / H | `Shader_Contract` fails (§11.1), or the shader contract check run at every gate fails (§9.3) | Interface frozen at G0; warp is an include with one entry point | Per-space keyword disables the new path |

### 12.2 Fallback ladders

| Item | Full | Step 1 | Step 2 | Last resort | Switch |
|---|---|---|---|---|---|
| S0 Home | Swift RoomPlan plugin, Forge meshes | ARKit mesh, planes, RoomPlan boxes | Mesh and planes only; with no RoomPlan boxes there is no live furniture (§4.2), and the obstacles stay placeholder boxes | Manual corner taps; built-in fallback set | `scan.route`, `forge.backend` |
| S1 Glued | T²/Klein 5 × 5 or T³ 3 × 3 × 3 | T² and Klein only (no T³), 5 × 5 | 3 × 3 copies | T² only, 3 × 3 | `glued.mode`, `glued.copies` |
| S2 Curved Walk | ρ = 3, scale 0.8, MSAA 4× | ρ = 2.5, scale 0.7 | ρ = 2, scale 0.6, MSAA 2× | Switch to S3 | `curved.cullRho`, `render.scale`, `render.msaa`, `curved.mode` |
| S3 Aligned Cells | Per-instance clip gates | Fewer neighbour cells visible | The 2 × 2 corner demo, five tiles round one post (§4.7) | Home with the curvature dial, standing | `cells.viewDepth` |
| S4 Small Planet | Six tiles, loop-fit R | Tighter fog | Larger R | Standing view, no walking | `planet.fogRho`, `planet.R` |
| S5 Impossible Rooms | Portal depth 1 | Depth 0 | Fewer rooms | One room pair | `portal.depth`, `rooms.count` |
| S6 Slicer | Live worldtube (≈ 18k tets) and polytopes | Stepped precomputed slices, no blending (R2) | Tesseract only (48 tets) | Static slice meshes | `slicer.source` |
| Station: mirror | `State` at 60 Hz | 30 Hz | 10 Hz | Recorded-state replay | `link.stateHz` |
| Station: tuner | Console | Console under `demoLock`: the phone ignores `ParamSet`, the six operator commands still work | On-device menu (`P1-D13`) for the parameters of §12.4 | Compiled defaults | `demoLock` |
| Station: telemetry | Live dashboard and CSV | CSV on the phone, uploaded over HTTP later | In-headset HUD | — | `telemetry.mode` |
| Station: round-trip | TripoSG on the 5070 | Hosted API, only if the walker was told and agreed (§7.4) | Cache by photo hash | Built-in fallback set (`P1-D12`) | `forge.backend` |

The last-resort column is for a failure during a live demo only. It never satisfies a gate: G4 and G7 need every space S0 to S6, and both Mode 1 and Mode 2, at Step 2 or better.

The thermal ladder (§8.6; R5) drives `render.scale`, `render.msaa`, `curved.cullRho` and `glued.copies` by itself; the Console can pin any rung.

### 12.3 If a gate fails

| Gate | Blocked | Can proceed | Lead decides |
|---|---|---|---|
| G1 First light | G2, G4 (all device work in lanes A, B and D) | Kernel, tile graph, Hub process, Forge, all Editor work | Fix the rig first; no lane starts device tests |
| G2 Curved-walk spike | S2 as the walked default | Everything else; S3 shares the kernel and tile graph | Raise L one step and repeat the protocol once (§11.4.4); if it still fails, make S3 the default and walk the five-corner leg of the 3-4-5 walk in S3 |
| G3 Spaces in the Editor | G4 for the failing space only | Other spaces; scan; link | Fix the space. It may pass at ladder Step 1 or Step 2, never at the last-resort rung; no space is held past G7 |
| G4 Spaces on the phone at budget | G7 | G5, G6 | Accept Step 1 or Step 2 as that space's default. A last-resort rung does not pass G4 |
| G5 Scan, Home, Bump Guard | All walking by anyone outside the team, and all walking without a spotter within reach; G7 | Walking by team members with a spotter within reach, as at G2; S6 seated; link; Forge | Nothing is traded for this; no public walking until it passes |
| G6 Station link | Function 4 blocks live furniture; functions 1–3 block operator tooling | All on-phone spaces | Name the failed function; D22 requires all four for G7 |
| G7 Phase-1 exit | P2 | Fixes only | No P2 work until G1–G6 are green |

### 12.4 Kill switches

Each is one registry parameter, settable from the Console and from the on-device menu (`P1-D13`); every change is logged with the session. Bump Guard has no off switch and no registry parameter: its thresholds and depth rate are constants in code (§4.10).

- `space.id`: force any space; S0 is the safe default.
- `curved.mode`: Mode 1 or Mode 2.
- `curved.cullRho`: 2–3.5; the radius ρ at which the fog is fully opaque (draw rule of §8.5).
- `render.scale`: 0.8, 0.7 or 0.6; selects the eye-texture set (§8.2).
- `render.msaa`: 2 or 4.
- `glued.copies`: 3 or 5 per side.
- `portal.depth`: 0 or 1.
- `slicer.source`: live, stepped, tesseract.
- `pose.gyroPredict`: on or off.
- `forge.backend`: local, hosted, cache, off (§7.4). Set it to cache for a walker who declines the hosted service.
- `scan.route`: 1–4.
- `link.enabled`: off puts the ATLAS app in standalone mode.
- `link.stateHz`: 10, 30 or 60.
- `demoLock`: the phone ignores `ParamSet`; the six operator commands still work (§7.3.2).
- `thermal.pinTier`: hold one ladder rung.
