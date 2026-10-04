### 10.3 Shared House and Post Circling

#### 10.3.1 Purpose and claim limits

Two scanned homes become the two sheets of one walkable surface, joined at a free-standing door frame. Core: **Post Circling**, solo, no network, second home pre-scanned, a recorded visitor labelled "recorded". Extended: **House Call**, a live second walker on phone B. Stretch: voice, hand-over, more than two homes.

Claims we make: the surface is an exact two-sheet branched cover; walking is 1:1 and flat. Claims we do not make: novelty over MRUnion (TVCG 2025) until it has been read (Spec §11.4); any clinical value of House Call; social-presence results.

#### 10.3.2 Why the geometry is necessary

Flat telepresence must use the intersection of the rooms or one merged room (Spec §11.4). The cover gives each walker the whole of the other home through a door that occupies 0 m² of wall. The three door behaviours are not scripted: they follow from one rule, "crossing the cut swaps the sheet".

#### 10.3.3 Maths

| Item | Formula | Tag |
|---|---|---|
| Sheets, door | N = 2 homes, K = 1 door, σ = transposition (σ = σ⁻¹, so crossing direction does not matter) | Spec §11.4 |
| Topology | χ = N − 2K = 0; genus g = K − N + 1 = 0; two boundary circles: an annulus | Spec §11.4 |
| Curvature | Flat except at the posts; cone angle 2πk = 4π, curvature 2π − 2πk = −2π per post; check 2πN − 4πK = 2πχ = 0 | Spec §11.4 |
| Cut | Open segment P₁P₂ between the post centres on the floor | derived |
| Crossing test | With orient(a,b,c) = (b−a)×(c−a), a step p→q crosses when orient(P₁,P₂,p)·orient(P₁,P₂,q) < 0 and orient(p,q,P₁)·orient(p,q,P₂) < 0; then sheet ← 1 − sheet | derived |
| Winding | θ₁ += atan2((p−P₁)×(q−P₁), (p−P₁)·(q−P₁)); a closed loop round P₁ alone returns to its sheet only when θ₁ is a multiple of 4π | derived |
| Embedding | M = T_A · Flip^f · T_B⁻¹, where T_X is the door pose in RoomModel X and f ∈ {0,1} turns the door 180° about its centre | derived |

Three consequences (derived): through the door crosses the cut once (switch); round the whole frame crosses it zero times (stay); round one post crosses it once (switch), and a second lap returns.

A straight sight line meets the cut at most once, so a depth-1 stencil portal renders the cover exactly; no recursion is needed (derived; matches §2).

#### 10.3.4 What is registered and what is not

| Thing | Registered to reality? |
|---|---|
| Sheet 0: the walker's own Home (walls, live AI furniture) | Yes |
| Door frame | Position yes; the frame itself is virtual (no real prop) |
| Sheet 1: the other home's walls and furniture | No. Virtual only |
| Walker position | 1:1 on both sheets; no drift, no holonomy |

Keeping the walker inside their own free floor F_A while on sheet 1:

1. **Door fit.** M is fixed by the door poses, so the search runs over the door pose in home B (x, z on a 0.1 m grid, yaw in 5° steps, f) and maximises area(F_A ∩ M·F_B) on the RoomModel free-floor grids. Virtual furniture standing in real free floor is harmless; virtual free floor over real obstacles is the hazard to minimise.
2. **Guest rug (not built unless the lead approves open item 13 of §14; the answer is needed by G11, before `P3-A04` starts).** Default: no rug. Sheet 1 is drawn in full, with its own floor only and no marking derived from the real room. The door fit (item 1) keeps virtual free floor over real obstacles to a minimum, Bump Guard (item 3) is the only protection, and the spotter recommendation of §4.10 applies to every walker on sheet 1.
3. **Bump Guard** runs unchanged in tracking space on both sheets (warning cue + haptic only, D19).

Door placement in the walker's room: centre at the free-floor point of maximum signed distance; door width 0.8 m (design choice); the circling post needs ≥ 0.9 m clear radius (Spec §11.4), the other post ≥ 0.5 m (design choice). If no pose qualifies, the use case reports "room too small" and does not start.

#### 10.3.5 Flow

| Beat | Walker does | Sees | Flat twin (mini-map and Spectator) |
|---|---|---|---|
| 1 Door appears | Stands in Home | Frame rises from the floor; other home visible through it | Two floor plans side by side, cut drawn on both |
| 2 Peek | Leans head into the opening | Other home, correct per eye | Dot at the cut |
| 3 Through | Walks through | Whole surround is the other home; own room visible back through the frame | Dot jumps to plan B |
| 4 Round the frame | Walks round both posts | Stays on the same sheet | Loop encloses both posts; counter 0 |
| 5 Round one post | Circles the marked post once | Sheet switches without using the opening | Winding dial about P₁ reads 2π |
| 6 Second lap | Circles again | Back home | Dial reads 4π, "cone angle 4π" caption |
| 7 Visitor | Meets recorded or live visitor | Capsule-and-head avatar; label "recorded" or "live" | Second dot with sheet colour |

#### 10.3.6 Data, Station and link

| Need | Detail |
|---|---|
| Second RoomModel | `roommodel_B.json` plus Forge GLBs of a team member's room, used with their consent, or of a synthetic room, never a pilot walker's room; pre-scanned and bundled, or scanned by phone B and fetched over HTTP from the Hub |
| Recorded visitor | `housecall_recorded.bytes`: pose and sheet at 30 Hz in sheet-B coordinates |
| Shared state | {sheet id, pose in sheet coordinates}; sheet k coordinates = RoomModel k frame; each client applies its own M (Spec §11.4) |
| Link additions | `State.sheet` (already in Appendix B.1) carries the sheet id; a new message `PeerState` (id 0x11, reserved in Appendix B.2; the Hub relays a peer's `State`); JSON `house.door` carries the door pose. Peer playback buffer 80 ms (Spec: 60–100 ms). The message, the relay and `house.door` are `P3-D07` (lane D); the playback buffer is `P3-C04`; both packages are Extended |
| Spectator | Renders the active sheet and both plans |
| Privacy | Only RoomModel geometry and Forge meshes are shared; no camera images (Spec §11.4) |

Post Circling must run with the Station unreachable.

#### 10.3.7 Measures logged

Sheet switches with cause (opening or post); θ₁ and θ₂; time on each sheet; Bump Guard warnings per sheet; overlap area of the door fit (m²); peer latency (ms); FMS on exit.

#### 10.3.8 Work packages

Lanes A and C build the use case. Lane D adds two packages, because `Atlas.Link`, the Hub, the mini-map and the Spectator are its code (§9.2): the peer relay (`P3-D07`, Extended) and the two-plan flat twin (`P3-D08`).

| ID | Work package | Lane | Depends on | Output (files, components, interfaces) | Acceptance test (binary, with numbers) | Needs (Editor / phone in hand / phone in headset / Station) |
|---|---|---|---|---|---|---|
| P3-A01 | Sheet state machine | A | G7 Phase-1 exit | `SheetState.cs` (crossing test, winding, per-eye sheet index) | Unit tests: 1,000 random segments match brute force; scripted paths give through = 1 switch, frame loop = 0, one-post loop = 1, two laps = 0 net | Editor |
| P3-A02 | Two-sheet door rendering | A | P3-A01, P1-A10 | `SheetDoor.cs`, door volume 0.1 m deep, stencil mask + depth reset + plane clip | Head moved through the opening at 1.5 m/s in simulation: 0 frames show the wrong sheet in either eye; ≤ 300 draw calls | Editor |
| P3-A03 | Second home loader | A | P3-A02, P1-C06, P1-D10 | `SheetHome.cs` builds sheet 1 from `roommodel_B.json` under M, own pooled material | Home B wall corners land within 0.01 m of M·(RoomModel B corners) | Editor |
| P3-A04 | (Blocked on open item 13; do not start) Guest rug and unlit floor | A | P3-A03, P3-C02, and the lead's answer to open item 13 | `GuestRug.cs` mesh from the fit mask | Rug area equals the mask area within 2 %; no rug vertex nearer than 0.3 m to a real obstacle | Editor |
| P3-A05 | Winding counter and flat-twin data | A | P3-A01 | Winding counter and the flat-twin data source (two plans, the cut, the winding angle) exposed through `ISpaceModule` | The counter reads 4π ± 0.1 rad after two laps of one post on a scripted path | Editor |
| P3-A06 | Visitor avatar, recorded track, labels | A | P3-A03 | `Visitor.cs`, recorder, "recorded"/"live" label | Avatar drawn only on its own sheet in 100 % of 200 sampled frames; label always visible | Editor |
| P3-A07 | Phone performance pass | A | P3-A02, P3-A03, P3-A05, P3-A06 | Soak log | 60 fps median, never below 30, ≤ 300 draw calls, ≤ 500k triangles over a 10-crossing run | Phone in headset |
| P3-C01 | Second scan path | C | G7, P1-C06, P1-D01 | Export and import of `roommodel_B.json`, bundled and phone-B routes | Phone B scan appears on phone A with identical free-floor grid (0 differing cells) | Phone in hand, Station |
| P3-C02 | Door placement and door fit | C | P3-C01 | `DoorFit.cs` returning door poses, f, overlap mask | Post clearances met (0.9 m, 0.5 m) on 3 test rooms; result equals exhaustive search optimum | Editor |
| P3-C03 | Bump Guard on sheet 1 | C | P3-A03, P1-C09 | Test protocol and log | In 10 of 10 approaches to a real obstacle on sheet 1 the cue and the haptic start ≥ 0.6 s before predicted contact, as on sheet 0 (the lead-time row of §11.2) | Phone in headset |
| P3-C04 | Peer link (Extended) | C | P3-A06, P3-D07 | Peer playback buffer (80 ms), avatar driven from `PeerState`, latency log | Two phones: peer avatar error ≤ 0.1 m at rest; latency logged; the use case is unaffected by a Hub shutdown | Phone in hand, Station |
| P3-C05 | G12 walk-through | C | P3-A07, P3-C02, P3-C03, P3-D08 | Run log and capture (with `P3-C04` when the Extended House Call is built) | All seven beats completed by 3 walkers with 0 contacts | Phone in headset, Station |
| P3-D07 | Peer relay (Extended) | D | G7, P1-D02 | `PeerState` message id and codec in `Atlas.Link`; the Hub relays a peer's `State` as `PeerState`; `house.door` JSON; Appendix B updated | Golden round trip for `PeerState`; the relay adds < 2 ms | Station |
| P3-D08 | Two-plan flat twin | D | P3-A05, P1-D07 | Two floor plans, the cut and the winding dial in the mini-map and on the Spectator | The dial reads 4π ± 0.1 rad after two laps of one post | Editor, Station |

G12 = P3-C05 passed, with the door placed by the door fit of P3-C02. Packages marked Extended or Blocked feed no gate: the House Call (P3-C04, P3-D07) is checked at G12 only if it is built, and the guest rug (P3-A04) is not part of G12.

#### 10.3.9 Open risks

| Risk | Handling |
|---|---|
| Rooms too small for 0.9 m post clearance | Report and do not start; measure on real rooms |
| Guest rug read as a ghost of the real room | Not built by default (open item 13); Bump Guard alone protects on sheet 1, tested by `P3-C03` |
| Phone B has no headset | Phone B runs a mono handheld view; open item 14 (§14) |
| Two walkers in one physical room | Not supported; walkers use separate rooms |

### 10.4 FieldFit

#### 10.4.1 Purpose and claim limits

FieldFit shows what a restricted visual field is like and how a field-remapping lens trades sharpness for coverage. It is a research prototype and a teaching tool, not a low-vision aid (Spec §11.5). Display acuity is about 14–17 px/deg (Spec §11.5), so we claim nothing about acuity.

What changes under VR only: there is no camera image. The lens remaps the **rendered Home** (the walker's scanned room with its live AI furniture). The source field half-width E is therefore our own render parameter, not a camera limit, and camera-to-photon latency no longer applies. The old Lens-standing and Lens-6DoF camera modes are dropped.

#### 10.4.2 Why the geometry is necessary

On the sphere of view directions, with s = 2·tan(e/2), any conformal lens that fits half-width E into a residual radius ρ₀ has central magnification ≤ tan(ρ₀/2)/tan(E/2) (Spec §11.5, Schwarz lemma). For E = 54°, ρ₀ = 10° that is 0.172: the centre shrinks 5.8×. A sharp centre plus the whole field forces a quasiconformal lens, and the distortion K is the price shown to the walker.

#### 10.4.3 Maths

| Item | Formula | Tag |
|---|---|---|
| Constants | S = 2·tan(E/2), σ₀ = 2·tan(ρ₀/2), s₁ = 2·tan(e₁/2) | Spec §11.5 |
| Uniform lens | σ = s·σ₀/S; conformal; attains the bound | derived |
| Power-law lens | σ = s for s ≤ s₁; σ = s₁·(s/s₁)^(1/K*) beyond; K* = ln(S/s₁)/ln(σ₀/s₁) | Spec §11.5 |
| Inverse, per output pixel | s = σ for σ ≤ s₁, else s₁·(σ/s₁)^K*; e = 2·atan(s/2) | Spec §11.5 |
| Default | E = 54°, ρ₀ = 10°, e₁ = 4° gives K* = 2.92; rings at 10°, 20°, 30° land at 5.5°, 7.0°, 8.0° | Brief §3.7, re-derived |
| Slider | σ_t = (1 − t)·σ_uniform + t·σ_power, monotone, inverted through a 1,024-sample radial table | derived |
| Cautionary lens | R = ρ₀·tanh(e/ρ₀); K ≈ 34 at 30° | Spec §11.5 |
| Source lookup | uv = 0.5 + 0.5·(tan e / tan E)·(cos α, sin α), α = azimuth | derived |
| Centre density | Source target of N pixels gives N/157.7 px/deg at the centre for E = 54°: 9.7 at N = 1,536 (the default) and 13.0 at N = 2,048 (the high tier) | derived |
| Scotoma River | Lines follow Im(w) = const, w = z + a²/z; letter scale 1/\|1 − a²/z²\| (0.5 directly above), clamped to ≤ 2 | Spec §11.5, scale derived |

**Render path.** Each eye renders Home to a square target of half-width E: 1,536² with 4× MSAA by default (9.7 px/deg), 2,048² only on the high tier; multisampled colour and depth are memoryless. The source size follows the thermal ladder (1,536 → 1,280 → 1,024). A lookup texture per eye (RG16F, 512²) stores, for each screen pixel, the source uv after: inverse headset pre-distortion → view direction (e_out, α) → inverse lens → source uv. Pixels with e_out > ρ₀ store uv = −1 and are drawn black: this is the head-locked field loss. The lookup takes the place of the barrel-distortion step in the ordinary final blit (`P1-A05`), and that blit samples the two source targets in place of the two eye textures, so the frame still ends in one blit. The texture is rebuilt only when a parameter changes, not every frame.

#### 10.4.4 Flow

| Beat | Walker does | Sees | Twin on the Spectator |
|---|---|---|---|
| 1 Blind-Spot Finder | One eye lit, fixates a cross, taps when a dot is seen | Dots on a grid 8°–22° temporal, ±8° vertical (design choice); found spot re-probed as catch trials | Field map filling in |
| 2 Struggle | Finds 5 marked objects in Home through a 10° tunnel | Black beyond 10° | Undistorted view with the tunnel outlined |
| 3 Fit and see | Moves the slider, repeats the search | Uniform → power-law; tanh shown once as the warning | Same view with rings at 10°, 20°, 30° |
| 4 Inspect | Looks around | "LENS ON" badge | K heatmap and the Schwarz bound |
| 5 Scotoma River (Extended) | Reads text with one eye | Text parting round their own blind spot | Streamline plot |

#### 10.4.5 Registration and safety

Home stays registered; the lens bends only directions. Lens beats are standing or seated (Spec §11.5). The lens switches off when the head moves more than 0.5 m from its start point (design choice), on one tap, or from the Console. Bump Guard stays on. The badge "LENS ON" is always visible while a lens is active.

#### 10.4.6 Data and Station

| Need | Detail |
|---|---|
| Field presets | `fieldloss/*.png` masks, 512²: 10° tunnel, hemianopia, one UWHVF field (BSD-3); Core uses the tunnel only |
| Console | Parameters `lens.E`, `lens.rho0`, `lens.e1`, `lens.t`, `lens.profile`, `lens.on` in the registry |
| Teichmüller lookup (Stretch) | Offline solve on the Station for one UWHVF field, exported as RG16F 512² |
| Search targets | 5 tokens placed on RoomModel object boxes, positions seeded per trial |

#### 10.4.7 Measures logged

Blind-spot centre and time to find (s); catch-trial false positives; search time per target (s); head-turn amplitude (degrees, sum of absolute yaw); lens profile, K*, t; blit cost (ms); lens auto-off events; FMS on exit.

#### 10.4.8 Work packages

| ID | Work package | Lane | Depends on | Output (files, components, interfaces) | Acceptance test (binary, with numbers) | Needs (Editor / phone in hand / phone in headset / Station) |
|---|---|---|---|---|---|---|
| P3-B01 | Lens maths library | B | G7 Phase-1 exit | `LensMath.cs`: profiles, inverses, K(e) | K* = 2.918 ± 0.001; rings at 5.5°, 7.0°, 8.0° ± 0.05°; 54° maps to 10.00°; tanh K at 30° between 30 and 40 | Editor |
| P3-B02 | Lookup builder | B | P3-B01, P1-A05 | `LensLut.cs` (Burst), two RG16F 512² textures | Rebuild < 16 ms; round trip through forward lens within 0.1° at 1,000 samples | Editor |
| P3-B03 | Wide source render and composed final blit | B | P3-B02, P1-C07 | `FieldFitFeature.cs`, shader `FieldFitBlit` | With lens off, output differs from the ordinary blit by < 1 pixel; exactly one final blit per frame | Editor |
| P3-B04 | Field-loss mask and slider | B | P3-B03, P3-D01 | Mask sampling, `lens.t` morph | Tunnel edge at 10° ± 0.2° measured on a ring test scene | Editor |
| P3-B05 | Lens guard | B | P3-B03 | Badge, tap-off, 0.5 m auto-off | Auto-off within 1 frame of 0.5 m displacement in 10 of 10 trials | Phone in headset |
| P3-B06 | Blind-Spot Finder | B | G7, P1-A04 | `BlindSpot.cs`, one-eye stimulus, catch trials | Blind spot found in ≤ 30 s by ≥ 4 of 5 testers (Spec §11.5) | Phone in headset |
| P3-B07 | Scotoma River (Extended) | B | P3-B06 | `RiverText.cs` | No glyph centre inside radius a; scale directly above = 0.5 ± 0.02 | Phone in headset |
| P3-B08 | Phone performance pass | B | P3-B03, P3-B04, P3-B05 | Soak log | Blit < 1 ms (Spec §11.5); with the lens on, GPU p95 ≤ 9 ms cold and every "must" frame-time row of §11.2 holds over a full search trial | Phone in headset, Station |
| P3-D01 | Field presets | D | G7, P1-D08 | `fieldloss/` masks and generator script | Tunnel mask area equals a 10° disc within 1 % | Station |
| P3-D02 | Console lens panel | D | G7, P1-D03 | Six `lens.*` parameters, stop-lens button | Each change reaches the phone in < 100 ms on the LAN | Station, phone in hand |
| P3-D03 | Spectator heatmap and bound | D | P3-B01, P1-D05 | K heatmap, bound readout | Heatmap reads K = 1 inside e₁ and 2.92 ± 0.05 outside; bound reads 0.172 | Station |
| P3-D04 | Search task and logging | D | P3-B04, P1-D04 | `SearchTask.cs`, CSV columns above | 5 targets logged per trial with 0 missing rows over 10 trials | Phone in headset, Station |
| P3-D05 | Teichmüller lookup (Stretch) | D | P3-B02 | Offline solver output for one UWHVF field | Loads and renders; maximum K reported | Station |
| P3-D06 | G13 measurement run | D | P3-B06, P3-B08, P3-D02, P3-D03, P3-D04 | Result table | Median search time with the power-law lens is lower than with the tunnel alone for ≥ 4 of 5 testers | Phone in headset, Station |

G13 = P3-D06 passed and, with the lens on, FieldFit meets the "must" performance rows of §11.2 (the soak of P3-B08 supplies the numbers). Packages marked Extended or Stretch feed no gate.

#### 10.4.9 Open risks

| Risk | Handling |
|---|---|
| Per-eye radial remap changes binocular disparity; fusion may fail | Test in `P3-B03`; fallback renders both eyes from the centre eye while the lens is on |
| 1,536² source targets exceed the frame budget | Step to 1,280² or 2× MSAA; measured in `P3-B08` |
| Fewer than 5 scanned objects in Home | Tokens also placed on wall positions |
| No collision-judgement evidence for walking under a lens | Walking stays excluded (Spec §11.5) |
