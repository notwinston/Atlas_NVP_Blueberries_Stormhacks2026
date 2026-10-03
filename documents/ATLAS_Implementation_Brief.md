# ATLAS: Implementation Brief

**A Swiss Army knife for non-Euclidean interaction on a phone VR headset.** *ATLAS is a working title; rename it freely.*

| | |
|---|---|
| Date | 3 October 2026 (planning baseline) |
| For | the implementation agent(s) and the team |
| Event | "Beyond Euclid: Interactive Experiences in Impossible Geometries" (Huawei Custom Challenge #1), presented as a live, judge-worn early version, then continued as a longer project |
| Companion files | `ATLAS_Plan.pdf` (this plan, human-readable). `ATLAS_Engineering_Spec.md` + `ATLAS_Engineering_Spec.pdf` (how to build it: stack, AR/VR pipeline, interfaces, pipelines, tests). `ATLAS_Research_Dossier.md` (research notes, about 850 unique links). `illustrations/` (15 explanatory diagrams, SVG + PNG, used in §3.7–3.9). The challenge brief (`Huawei_Custom_Challenge_1_Beyond_Euclid.docx`) |
| Name | In differential geometry a curved space is described by an **atlas**: flat *charts* glued by *transition maps*. That is literally how this engine works (flat walkable charts glued by portals, curvature, branch cuts and time), and it is the interface principle the evidence supports: "local flat charts, glued" |

---

## 0. How to use this brief (read first)

**Reading order.**
1. §1 *Project on one page* and §1.4 *Decisions*.
2. §3.7–3.9 *The spaces, the landscape and 4D, illustrated*, if you need an intuition for the geometry first, and §4.0 *Why each blade needs its geometry*.
3. §10 *Build plan*: §10.0 readiness map, the Phase 0 spikes, then §10.7–10.8 (schedule and ownership templates to fill in).
4. The section for whatever you are building; engineering detail is in `ATLAS_Engineering_Spec.md`.

The dossier holds the full evidence; this brief holds the decisions.

**Conventions.**
- **MUST / SHOULD / MAY** are used in the RFC 2119 sense.
- Evidence tags:
  - **[M]** measured (controlled study, benchmark, meta-analysis);
  - **[O]** observed (deployment, pilot, vendor documentation);
  - **[D]** derived by our research agents from standard mathematics; *verify before it goes in public docs*;
  - **[U]** unverified or estimate;
  - **[R]** reported in a secondary source or the press; verify before citing.
- Effort estimates (e.g. "2–3 weeks") come from research agents and are **[U]**.
- "Dossier → `file.md`" points into `ATLAS_Research_Dossier.md`.

**Non-negotiables. Violating any of these is a bug.**
1. **Safety never touches the network.** Pose, obstacle detection, collision warnings, passthrough escalation, gain clamping and the "Safe Harbour" exit all run on the phone, every frame. The PC can suggest; it never controls anything safety-relevant.
2. **Target a locked 60 fps on the phone, in stereo** (rubric minimum 30 fps). Never run at 120. The only sanctioned drop is a deliberate, *stable* 30 fps thermal tier (§7.12), never random frame drops.
3. **Every hand technique has a twin input** (gamepad, head-gaze dwell, marker cube or voice). The demo MUST be completable with walk + look + one button.
4. **Every curved view has a flat twin** (linked list, outline, chart or map). This is the only configuration with a measured overall win in the hyperbolic-interface literature (Risden et al. 2000, [M]).
5. **A Euclidean "Safe Harbour"** (the real scanned room, passthrough, all gains = 1) is always one gesture, button or word away.
6. **Gains start at identity.** Nothing curves, bends or rotates covertly on a first-time user.
7. **No clinical claims.** Medical blades are *education, training and research visualisation of public, de-identified data*. See the wording rules in §2.4.
8. **No camera frames are stored.** Process in RAM only. Treat motion telemetry as health data.

---

## 1. The project on one page

### 1.1 The challenge (from the brief)

Build an interactive application in which non-Euclidean geometry is **central and meaningful**. The best projects "make the user feel it, interact with it, and discover something new about space itself."

| Criterion | Weight | What it means for us |
|---|---|---|
| Geometric creativity | 30% | Originality; the geometry must not be cosmetic; **mathematical correctness** |
| UX and interaction | 30% | Intuitive, engaging; navigation, camera and feedback quality; "purposeful, not disorienting for its own sake" |
| Graphics and technical execution | 25% | Clarity, polish, **30+ FPS**, code quality, efficient geometric computation |
| Documentation and presentation | 15% | Maths and technical explanation, documented code, compelling demo |
| Bonuses | n/a | Beyond gaming (education, productivity, data viz, art); educational value; exceptional polish (visual and sound); novel technical approach |

The document names four families of space: **hyperbolic**, **spherical/elliptic**, **4D projections** and **impossible spaces / Escher-like worlds**. It lists what makes them hard: coordinate systems, rendering (projecting curved geodesics onto a flat screen) and interaction (navigation, aiming, collision). It sets **no hardware or platform requirement** ("participants are completely free in their choice of application type") and asks for **30+ FPS** in real time. It does not state the event date, slot length, judge format, whether code may be written before the event, or venue details (§13). §1.6 maps every item to the plan.

*Name collision to be aware of:* an IEEE VR 2024 3DUI Contest entry is titled **"Beyond Euclid: An Educational VR Journey into Spherical Geometry"** (Chrysanthakopoulou et al.). Cite it and say how ATLAS differs. Dossier → `interaction_design_space.md` §4.2.

### 1.2 The concept

> **ATLAS is one phone headset that walks, sees your hands and knows your room, plus a design space of non-Euclidean interactions it makes possible, plus a set of "blades": real applications where the space takes the shape of the problem inside it.**

- **The handle (the kernel).** One geometry engine with five primitives: exp/log map, parallel transport, gluing images, slice/project, conformal warp. Every geometry implements them (hyperbolic, spherical, glued/periodic, branched, portal, 4D-space, 4D-time, conformal), and every technique is written once against them.
- **The blades.** Applications where curvature is placed **where the structure is**:
  - trees are hyperbolic;
  - cycles are circles and tori;
  - directions are spheres;
  - time is the fourth axis;
  - many homes sharing one floor form a branched cover;
  - a damaged visual field is a curved region of the sphere of view.
- **The judge-legible rule:** *"Every space here has the shape of what's inside it."* Each blade then adds one rule you can learn in ten seconds, for example "every wall is a door to its opposite wall".
- **The HCI framing:** this is a CHI/UIST-style *artifact + design space + techniques + demonstrations + targeted evaluation* contribution, not a game (§8).

### 1.3 What a judge experiences (one story, ≤5 min in the headset)

The live path is **one patient story told through five geometries** (the knife's breadth inside one real task), then one blade of the judge's choice. Before the headset goes on, the judge picks a **case card** (a public CT case) and hands over a pocket object, which a teammate photographs with the Galaxy S24.

1. **Onboarding (passthrough, 40 s).** Stand on the X in the taped 3×3 m square, see the real room, raise a hand, learn A and B.
2. **Bend Your Room (30 s; E → H/S).** The judge's own scanned room bends from saddle to flat to ball under a dial while the floor stays 1:1. They walk a taped 1.5 m square carrying an arrow, and it comes back turned by K·A (≈1 rad at R = 1.5 m). Curvature is felt, then measured (§4.1).
3. **Breath Room (45 s; flat but wrapped, ℝ³ × S¹).** A 1.5 m glued cell where walking east moves you through the breath: the east wall is 100%, the west wall 0%. The tumour across the breathing cycle is laid out as a periodic tube through the copies of the room; looking along it shows its shadow, the ITV. Two flags set the gating window. They also see the back of their own head in the next copy (§4.2.1).
4. **Airway (75 s; H²).** The airway of the case they picked, with distal generations completed procedurally and labelled "model-generated" (Roll a Lung), laid out in hyperbolic space. At one fork a Euclidean compass and the hyperbolic beacon disagree, and the judge chooses using the endoluminal view. Their photographed object sits in the airway as a cairn (§4.3.1).
5. **Needle sky (30 s; S²).** Stand inside the lesion; every direction is a needle path coloured by risk; pick one by looking. The plan card is generated (§4.3.3).
6. **Post Circling (20 s; branched cover).** Walk through a free-standing door into a teammate's pre-scanned room; walk round one post and you're back home without going back through the door (§4.4).
7. **Judge's choice (45 s).** FieldFit (blind spot, then a lens; Lens-6DoF) or House Call with a live teammate.
8. **Exit and dashboard (≈45 s).** Task results first (their fork choice vs the Euclidean-compass prediction, ITV saved, needle clearance), then physical path (small loops) vs virtual path (≈10 m of airway), comfort, frame time, and the design-space cells they used.

### 1.4 Decisions (the short version)

| # | Decision | Choice | Why (evidence) | Fallback or alternative |
|---|---|---|---|---|
| D1 | What the project *is* | Platform + interaction design space + blades (Swiss Army knife) | The lead's direction; strongest HCI framing; scores breadth *and* depth | One deep blade only |
| D2 | Hackathon blades and live path | **Live story path (Tier A):** Hub + Bend Your Room → Breath Room (Glued Room engine) → Lung airway (Roll a Lung) → needle sky → Post Circling (solo, pre-scanned) → judge's choice of FieldFit or House Call. **Also Tier A:** the classic Glued Room (public loop between judges). **Tier B** (video in Q&A): Station Zero, The Branch / Deep Time, Small Planet, Manifold Lab, C-Space Room | One patient story across five geometries answers the "menu of mini-demos" risk; every slot still shows E, H, S, wrapped, branched and 4D-time; the core path needs no network | 5-min slot: skip the judge's choice; skip Post Circling if FMS ≥2 |
| D3 | Phones | **Phone A: iPhone 15 Pro Max** (LiDAR, A17 Pro) in the headset. **Phone B: iPhone 14 Pro Max** (LiDAR, A16) for hot-swap and backup only (a second judge when idle). **Galaxy S24 / S24 Ultra** takes the judge's object photos and is the live House Call partner (§6.1) | LiDAR depth, mesh, RoomPlan and Object Capture cover all sensor features on both iPhones; the 15 Pro Max is ≈10–26% faster under sustained GPU load [O] | Android (ARCore) build on the S24 as a secondary target |
| D4 | Engine | **Unity 6 LTS, URP, AR Foundation 6 (ARKit), IL2CPP**, plus Swift native plugins (Vision hands, RoomPlan, Object Capture, Core Haptics, thermal) | Fastest path to production quality; cross-platform; glTFast/AR Foundation ecosystem; all hot paths are GPU shaders or native code | Native Swift + Metal port of the kernel later if profiling demands it (§6.3) |
| D5 | Stereo | **One camera, two eyes:** AR Foundation owns tracking; one URP camera with a render-graph pass draws both eyes into a side-by-side target (one cull with a frustum enclosing both eyes; portal views as RendererLists, clipped in the shader); distortion from a Cardboard viewer profile | Unity's single-pass instanced stereo is offered for Android multiview, PSVR and Windows, not iOS ([Unity](https://docs.unity3d.com/Manual/SinglePassStereoRendering.html)); two eye cameras plus a camera per portal view would be CPU-bound | Two-camera rig; Cardboard XR plugin with native ARKit pose timeboxed to 1 day. Decide by spike 0.15 |
| D6 | Compute split | **Phone renders everything locally.** RTX 5070 PC = real-time server, operator console, spectator renderer, light AI. **H200 cluster = offline and batch** (best models, dataset processing, studies), cached before the demo | Streaming VR video from a PC measures 50–150 ms vs a ~20 ms comfort budget [M]; no maintained open-source iPhone VR streamer exists | — |
| D7 | Inputs | Must-have: real walking, head-gaze reticle, gamepad A/B (fallback: headset lever tap, then dwell), operator console. Showcase: **Vision hand pose + LiDAR depth**, **printed marker cube**, real furniture as passive haptics | The camera cone is only ≈72°×57°; hand pinch is unreliable in Cardboard-class studies [M]; the cube tracks every frame [O] | Every hand technique has a twin |
| D8 | Locomotion in 3×3 m | **Topology first** (portals, gluing, sheets, hyperbolic tiling, holonomy). Redirection gains off by default, within thresholds if used | Gain-only redirected walking needs ≈6×6 m [M]; topology gives "impossible" with zero visual-vestibular conflict | 2:1 turn resets at walls |
| D9 | Curved rendering | Hyperboloid/sphere storage → **Klein (projective) chart in the vertex shader**; **H²×E** for anything walked; geodesic fog; **stereo mode chosen per wing** by spike 0.7 (Klein per eye = no subdivision but depth compressed ≈IPD/R; equidistant = correct depth but edges subdivided offline to ≲0.2R) | In the Klein chart, straight edges stay straight; the floor stays put (dossier → `rendering_math_4d.md`) | Ray-marching only for one exotic showcase room |
| D10 | Portals | **Stencil portals** drawn as extra RendererLists of the one stereo camera, per-eye matrices, recursion ≤2, clipping at the portal plane in the shader, portal *box* for the eye straddling a portal | 2.3× (up to 3.97×) faster than render textures; lighting works [M] | Render texture at half resolution, depth 1 |
| D11 | Scanning | Rooms: **RoomPlan → parametric → rebuilt from our modular kit** (real furniture becomes walls and haptic proxies; real doors become portals). Objects: **Object Capture (Mac) for fidelity; SPAR3D or Hunyuan3D-2mini on the 5070 for speed; TRELLIS.2 on the H200 for best quality;** commercial API fallback | 5070 = 12 GB (TRELLIS.2 needs ≥24 GB); raw scans look incoherent, so re-instantiate and stylise | Pre-generated assets labelled "pre-baked" |
| D12 | Networking | WebSocket (control), UDP (pose 30–60 Hz), HTTP (assets), MessagePack; own 5/6 GHz router; `iproxy` over USB for development | Simple, works on both OSes, honours iOS Local Network rules (dossier → `phone_pc_link.md`) | Pose over WebSocket when on USB |
| D13 | Medical data | Public and de-identified only: LIDC-IDRI, TCIA 4D-Lung (CC BY 3.0), TotalSegmentator (Apache-2.0) | Licences permit derived meshes; "not for clinical use" | Synthetic cases (MAISI on H200) |
| D14 | Claims | Education, training and research visualisation; measured, not asserted (§2.4, §8) | FDA General Wellness guidance (Jan 2026) and Health Canada SaMD guidance | — |
| D15 | Geometry strategy | **Portals + curved wings:** flat charts (hub, glued rooms, house sheets) joined by isometric gluings; constant-curvature wings (H², S², H³, S²×E) only where the content is curved | Keeps the scanned room 1:1, allows every geometry type, lets R be chosen per wing for comfort (§7.13) | **"True curved space only"** (one continuous H²×E/S²×E world, Holonomy-style tile mapping) documented in §7.13 as a possible later direction; decide with spike 0.14 (run after Phase 2) |
| D16 | Latency | Predict the head pose to photon time with Core Motion (100–200 Hz) from the ARFrame timestamp, and apply a late rotational warp in the final distortion pass; warp passthrough together with the overlays | The ARKit pose is the capture-time pose at camera rate; Unity's late latching isn't available on this path [verify] | Spike 0.16: rotational motion-to-photon ≤30 ms |
| D17 | Procedural generation in the live demo | **Roll a Lung** (§4.3.1): the judge picks a pre-segmented case; the PC completes distal airway generations with a published space-filling model, lays the tree out in H² and assembles kit chambers in seconds; completed branches are labelled "model-generated" | Makes procedural non-Euclidean generation visible and honest, and fills the "segmentation stops short" gap | Curated default case cached on the phone |
| D18 | Throughline | One patient story (Bend → Breath Room → airway → sky → Post Circling), every step in a different geometry, ending in a plan card | Purpose stays clear in 5 minutes while breadth stays visible | Separate blades in the 10-min slot only |
| D19 | iOS signing | **Free Apple account in Xcode ("Personal Team")**: builds signed by one owner account on the M3 Max and installed over USB; one fixed bundle ID; reinstall ≤24 h before an event | No enrolment, fee or review wait; covers both headset iPhones (3-device limit); the 7-day build lifetime is longer than our rebuild cycle | Paid Apple Developer Program (US$99/yr; individual approval usually ≈48 h) if spike 0.26 fails or installs become a bottleneck |

### 1.5 Hardware we have, and each device's role

| Device | Role |
|---|---|
| **iPhone 15 Pro Max** in a camera-window headset | Everything the user sees and does: ARKit 6DoF, LiDAR depth and mesh, stereo rendering at 60 fps, Vision hands, image-tracked cube, RoomPlan and Object Capture *capture*, safety, local planners, logging |
| **MacBook M3 Max** | Xcode and iOS builds; Object Capture *reconstruction* (PhotogrammetrySession); AirPlay receiver as a spectator fallback; optional MPS models |
| **Windows PC, RTX 5070 (12 GB, Blackwell)** | Real-time server (FastAPI: WebSocket, UDP, HTTP); operator console; spectator renderer and "Knife Map" dashboard; logging; light AI (SPAR3D, TotalSegmentator, BioCLIP 2, small LLM); asset optimisation (gltfpack, KTX2). Needs **PyTorch built for CUDA 12.8+ (cu128 wheels, PyTorch ≥2.7)**; older repos with custom CUDA kernels may need rebuilding for sm_120 [U] |
| **H200 cluster** (141 GB HBM3e each) | Batch and offline: TRELLIS.2 asset kit and hero objects; segmentation of whole CT collections; the hyperbolic-vs-Euclidean greedy-routing study; Teichmüller lens atlas over 28,943 visual fields; Orb-v3 molecular dynamics, Earth2Studio forecasts and LLM steering for Tier B blades; OpenRDW simulation sweeps. *Live* use only if the cluster allows interactive nodes reachable from the venue, and always with a cached fallback |
| **iPhone 14 Pro Max** (phone B) | Hot-swap and backup for phone A (kept cool); second judge on short blades when idle; second developer device. Same app, same quality tier as phone A |
| **Samsung Galaxy S24 / S24 Ultra** | Photographs the judge's object for the live mesh; House Call partner client; Android build test; flat-mode citizen-science client |

### 1.6 How the plan answers the challenge

| The challenge asks for | Where ATLAS answers it | Section |
|---|---|---|
| Hyperbolic geometry | The airway tree laid out in H² (exponential room for a branching tree); the saddle end of the curvature dial | §4.3.1, §4.1 |
| Spherical / elliptic geometry | The needle sky: the sphere of straight paths, great-circle sweeps that meet, a triangle of windows whose angles sum to more than 180°; the ball end of the dial; elliptic space as the one-pass S³ option | §4.3.3, §4.1, §7.2 |
| 4D projections | The Breath Room: the tumour's path through space and time is a 4D tube, and its end-on shadow is the radiotherapy target (ITV); Slice Tilt rotates the slice in a time–space plane | §4.2.1, §3.9 |
| Impossible spaces, Escher-like worlds | The Glued Room (every wall is a door to its opposite; Klein-bottle mirror flip), Post Circling (round one post into another home), rooms that overlap out of sight; the Penrose Loop on the roadmap | §4.2, §4.4, §3.8, §3.10 |
| Coordinates, rendering and navigation rethought | Charts and gluings instead of global coordinates; the Klein chart in the vertex shader; exp-map walking; stencil portals; topology-first locomotion in 3×3 m | §7.1–7.4 |
| "Feel it, interact with it, discover something new about space" | Feet measure curvature (Bend Your Room); straight-line intuition fails at the Two Compasses fork; looking chooses a needle path | §1.3 |
| Geometric creativity (30%) | Each geometry is chosen because the data has that shape; every blade names its kind and why; live numbers on the Knife Map | §4.0, §5.5 |
| UX and interaction (30%) | Walk + look + one button; a twin for every gesture; Safe Harbour; comfort ordering; one rule per space | §3.4, §9.4 |
| Graphics and technical execution (25%) | 60 fps stereo target (30+ required); one-camera stereo; late warp; engine documented and tested | §7.2, §7.12 |
| Documentation and presentation (15%) | This brief, the engineering spec, the plan PDF, the dashboard reveal, videos and poster | §8.4, §9 |
| Bonuses | Beyond gaming (medical training, assistive vision); educational rules; the coherence recipe; novel techniques (one-camera stereo, per-wing curved stereo, time as a circle, the ITV as a 4D shadow) | §2, §7.11 |

**Main risk to the score:** UX is 30% and the document wants the experience "purposeful, not disorienting for its own sake". Five geometries in five minutes must not feel like a tour: each space gets one rule and one "aha" (§9.8), and the patient story and plan card tie them together.

---

## 2. Purpose: what the space is *for*

### 2.1 Four principles the evidence forces

These follow from the research (dossier) and recur across every research lane.

1. **Topology yes, global metric no.**
   - People navigate wormholes, portals and overlapping rooms well, and mostly don't notice them. Small rooms can overlap by up to 56% unnoticed (Suma 2012) [M]; wormhole mazes are learned as graphs (Warren 2017) [M].
   - People do **not** integrate a curved metric in their heads: homing stays Euclidean even after training in curved VR (Widdowson & Wang 2022) [M].
   - So: use curvature to create room, adjacency and wrap-around, and give people local flat charts, landmarks and minimaps.
2. **Put the curvature where the structure is.** Every blade's geometry is the geometry of its content:

   | Content | Geometry |
   |---|---|
   | Branching (airways, vessels, design trees, phylogenies, pedigrees) | Hyperbolic H² |
   | Periodic (breathing, crystals, robot joints, LLM "clocks") | Circle S¹, torus T² or T³ |
   | Directions (needles, beams, visual field, the planet) | Sphere S² |
   | Time series (4D CT, embryos, forecasts) | 4th axis |
   | Several homes on one floor | Branched cover |

   Curvature imposed on data that is not curved only adds distortion.
3. **Pair every curved view with a conventional one** (non-negotiable 4). The curved view gives overview and adjacency; the flat view gives precision and stability.
4. **Measure performance, not applause.** Users rate hyperbolic trees "cool" (7.7 vs 3.9) while performing no better [M]. Every claim we make needs a matching live measure (§8).

### 2.2 Why ATLAS is a knife and not one tool

The lead's insight: a phone headset that walks, sees hands, knows the room and can be scanned into is a **new interaction artifact**. Like an HCI paper, ATLAS contributes the *space of interactions* it enables, then shows several useful blades. The judge experiences breadth (many geometries and body channels) *and* depth (one blade that does real work end to end).

### 2.3 Blade catalogue

Tiers:
- **A** = in the hackathon demo, judge tries it.
- **B** = built if time allows, shown opt-in or on video.
- **C** = long-project roadmap.

| Blade | Tier | Geometry | What the user *does* (beyond walking) | Who it helps | Evidence anchor |
|---|---|---|---|---|---|
| **Hub: your scanned room** | A | E (Euclidean home), Curvature Dial E↔H↔S | Calibrate; learn the tools; choose a blade; return to safety | Everyone | Safe-harbour pattern |
| **Glued Room** | A | T², T³, Klein bottle, flat orbifolds | Walk through a wall and re-enter from the opposite one; reach "through the back door"; twist the gluing with a cube; carry an object around a flip and get its mirror image. *Stations:* crystal unit cell (thread CO₂ into CALF-20), robot joint torus, LLM weekday ring, universe topology | Chemistry, robotics, AI-literacy and cosmology students | Periodic boundary conditions are how all molecular dynamics and DFT codes model crystals [O]; TorusDesktop (CHI 2011) [M]; iMD-VR, *Science Advances* 2018 [M] |
| **Lung Mission** ★ deep blade | A | H² (airway tree) · S¹ (breathing) · S² (approach directions) · T² (gantry–couch chart, optional) | Plan a bronchoscopy route by walking forks; compare routes; rehearse with endoluminal views; set a radiotherapy gating arc by walking the breath; choose a needle direction by looking; print a plan card | Bronchoscopy and IR trainees; radiation-therapy students; educators; patient education | With 2D CT, path choice was correct in 14–40% of cases vs 49–96% with virtual bronchoscopy (Dolina, CHEST 2008) [M]; CT-to-body divergence ≈17 mm [M]; core biopsy pneumothorax 25.3% [M]; 4D CT standard of care (AAPM TG-76) [O] |
| **Shared House** (+ House Call) | A | Branched cover: a walkable Riemann surface | Visit another family's real room at 1:1 through a doorway; hand over a scanned object; walk round one post to change homes; clinician pins home hazards and times a chair-stand | Isolated older adults and families; occupational therapists; remote care | Loneliness is a major health risk (WHO 2025) [M]; home-hazard reduction cuts falls 26% overall, 38% in high-risk people (Cochrane 2023) [M]; telehealth home ratings AC1 0.93 [M]; flat "intersection" telepresence placement succeeds in only 13% of 4-person sessions [M] |
| **FieldFit** (+ Scotoma River) | A | Sphere of view directions; conformal and quasiconformal maps | Map your own blind spot; search the room under a real glaucoma field; fit a lens and find everything; read text that flows round a blind spot | Retinitis pigmentosa, glaucoma, hemianopia and AMD researchers and rehab; accessibility audits | Minifiers help (RP 1-lux mobility pass 0% → 88%) [M]; smartphone-VR perimetry r = 0.808 vs Humphrey [M]; DSpecs (Sayed et al.) already relocate and resize unseen targets into the remaining field, but no Schwarz-bound or quasiconformal-optimal lens has been trialled (the gap) |
| Station Zero | B | Portal charts glued by SO(3) rotations | Emergency egress where "up" changes at every hatch; point through the hull to the lifeboat; instruct a crewmate | Astronauts and commercial-station crews; submarine and ship crews | Documented Mir/ISS orientation incidents (Oman 2007) [O]; VR improves 3D spatial memory, retained a month [M] |
| The Branch / Deep Time | B | H² (huge trees) | Steer a generative design tree (doors = decisions, H200 generates variants); point the camera at a plant or pet and walk inward to its common ancestor with another organism | Designers; biology students and museums | Parallel prototyping improves outcomes (Dow, N=33) [M]; DeepTree museum study N=247 [M]; Open Tree of Life 2.39M tips, CC0 [O] |
| Small Planet | B | S²×E | Walk great circles; carry a pendulum around a loop (Foucault holonomy); nudge a storm and rerun an AI forecast ensemble | Weather and climate literacy | Globes beat flat maps for direction judgements [M]; Earth2Studio incl. Huawei Pangu-Weather [O] |
| Manifold Lab | B | S¹ rings, T² number torus | Walk the circles an LLM uses for weekdays and months; your position patches the model's hidden state and it speaks its answer | AI literacy; interpretability research | Circular features are causally used by models (Engels et al., ICLR 2025) [M] |
| C-Space Room | B | T² / T³ joint torus | The floor is a robot arm's pose space; walk round obstacles that exist only in pose space; a real arm follows | Robotics education | Configuration space is the standard formulation (Kavraki & LaValle) [O] |
| Heartbeat Torus | B | T² of time (breathing phase × cardiac phase) | Walk east through the breath and north through the heartbeat; watch tumour and heart move in one two-dimensional loop of time | Radiotherapy and cardiology education | Cardiac cine MRI is phase-binned like 4D CT (ACDC, 150 patients) [O] |
| Cortex Planet | C | S² (inflated cortex) | Inflate a brain into a walkable planet; find areas; run a spin test | Neuroscience education | VR anatomy SMD 0.53 [M] |
| Territory Surgeon | C | H² vessel tree | Cut a branch and see the tissue it feeds; check the remnant volume | HPB and thoracic surgery trainees | VR planning changed 52% of segmentectomy plans [O] |
| Geodesic Soundscape | C | Length space of the walkable floor; tanh-compressed audio distance | Navigate eyes closed by sound that follows the walkable path | Blind and low-vision travellers | Clew: <1 m error on routes up to 63 m [M] |
| Wormhole Quest | C | Wormholes, holonomy | Citizen-science game building the first norms for map-vs-graph navigation | Dementia and navigation research | Sea Hero Quest: 4.3M players; Howett 2019 AUC 0.90 for path integration in MCI [M] |
| Folded Building | C | Impossible overlap of a scanned real building | Rehearse a real building's routes in smoke at 1:1 in a small room | Firefighters, wardens, hospital staff | VR ≈ blueprint ≫ none for firefighter wayfinding [M] |
| Penrose Loop | C | A helix glued to itself by a screw motion | Climb a staircase that always rises and still returns to the start | Maths education; Escher's impossible stair made honest | Escher, *Ascending and Descending* (1960) |
| Colon Gallery, House That Never Runs Out, Unit-Cell Room (full), Embryo Room, 4D Toybox | C | various | See dossier | — | — |

### 2.4 Claims language (copy into the docs and the booth sign)

**Say:**
- "An interactive platform for exploring non-Euclidean interaction. Medical blades are educational visualisations of public, de-identified data with illustrative risk metrics. Not a medical device; not for clinical use; not intended to diagnose, treat, cure, mitigate or prevent any disease."
- "Research prototype that simulates visual-field loss and lets researchers prototype geometric remapping lenses."
- "Generates navigation performance metrics of the kind used in spatial-cognition research."

**Avoid:**
- "plans your procedure", "detects MCI", "rehabilitates", "expands your visual field", "treats …";
- normative cut-offs that flag a person as impaired;
- "first non-Euclidean VR" (Hart/Segerman, HyperRogue, Hyperbolica and Holonomy exist);
- "intuitive" without data;
- "AI-generated live" when the asset was pre-baked.

**Basis:** FDA General Wellness guidance (updated 6 Jan 2026) and Health Canada SaMD guidance. In both, *intended use* decides device status. Human-participant studies need ethics review regardless. Dossier → `purpose_medical_cognitive.md` §7.

### 2.5 Real-world roadmap (after the hackathon)

| Horizon | Steps |
|---|---|
| **Now** | Education and research instrument on public data. Every session logs the measures in §8 |
| **Next (studies)** | (a) Lung route study: H² + endoluminal vs endoluminal only vs 2D CT, with pulmonology trainees (Dolina-style). (b) FieldFit lens vs uniform minification on simulated, then real, low-vision search and mobility. (c) Shared House feasibility with older adults (UCLA-3 loneliness, social presence, adherence). (d) Glued Room pointing (TorusDesktop in 3D) |
| **Later (with clinical partners and ethics)** | Wormhole homing probe added to a Howett/Philippen-style path-integration protocol; House Call validation (head-derived chair-stand and TUG); dosed curvature for vestibular rehab (clinician-controlled) |

---

## 3. The interaction design space (the knife)

A non-Euclidean interaction technique is a choice along **six dimensions**. Each technique gets a code, e.g. `H · hand · exp · world · select · self`. Full catalogue of 63 techniques: dossier → `interaction_design_space.md`.

### 3.1 Dimensions

| Dim | Question | Values (code) |
|---|---|---|
| **G Geometry** | What space is the content in? | **E** Euclidean home · **P** portal/impossible · **T** periodic/glued quotient (T², T³, Klein bottle, orbifolds) · **H** hyperbolic (H², H²×E, H³) · **S** spherical (S², S³, S²×E, the sphere of directions) · **B** branched cover (Riemann-surface sheets, cones, helicoids) · **4s** spatial 4D · **4t** time as the 4th axis (incl. S¹ loops) |
| **B Body channel** | What drives it? | feet · head · hand · hands (bimanual) · tangible (cube, scanned object, furniture) · voice · wrist/pad (gamepad, watch) · other (collaborator, clinician, spectator) · agent (AI) |
| **M Mapping** | How does body motion become motion in the space? | **iso** isometric · **gain** amplified or warped · **exp** exponential/log map · **hol** holonomic (a loop gives a rotation) · **wrap** periodic (deck images) · **sheet** branched (crossing a cut permutes sheets) · **slice** slice/project · **conf** conformal/quasiconformal warp · **jump** discrete topological (portal crossing, gluing edit) |
| **F Frame** | Where is the geometry relative to you? | **world** (you're inside it) · **window** (portal, lens, cut) · **proxy** (handheld miniature, doll, cube) · **field** (warp of the perceived real world) · **twin** (linked flat chart) |
| **T Task** | What is the person trying to do? | navigate · select · manipulate · measure · create · communicate · learn |
| **P Perceiver** | Who perceives the effect? | self (overt) · covert · partner · audience |

*Notes on the axes:*
- **Mapping is where the maths lives**, and mappings travel between geometries. For example, `wrap` drives S¹ time and robot joints; `conf` drives both FieldFit and H³ flight.
- **Frame** turns classic VR techniques (world-in-miniature, voodoo doll, magic lens, portal) into non-Euclidean ones.
- **Generativity:** empty cells suggest new techniques (e.g. other × 4s → "Duet Rotation", where two people each hold one quaternion of an SO(4) rotation).

### 3.2 The shared kernel (what makes it one knife)

Curvature radius R; K = −1/R² (H) or +1/R² (S). Hyperbolic points live on the hyperboloid ⟨p,p⟩_L = −R² (Minkowski form x₁y₁ + x₂y₂ + x₃y₃ − x₄y₄); spherical points have |p| = R.

| Primitive | E | H | S | T (quotient by Γ) | B (sheets) | 4D |
|---|---|---|---|---|---|---|
| `exp_p(v)` (reach, step, ray) | p + v | cosh(‖v‖/R)·p + R·sinh(‖v‖/R)·v̂ | cos(‖v‖/R)·p + R·sin(‖v‖/R)·v̂ | E, then reduce mod Γ (log crossings) | E, then sheet ← σ^{±1}(sheet) on crossing a cut | ℝ⁴ step; slice basis moves with it |
| `dist / log` (beacon, depth) | ‖q − p‖ | R·arcosh(−⟨p,q⟩_L / R²) | R·arccos(p·q / R²) (cut locus = antipode) | min over images g·q (minimum image) | shortest path in sheet graph | ‖·‖ in ℝ⁴; Δt |
| `transport` (carry, loops) | identity | loop of area A rotates frames by A/R² | A/R², opposite sense | 0 for translations; orientation flips for glide | cone angle 2πk at a k-sheet post | SO(4) rotors |
| `images` (copies) | none | none | antipodal focus; elliptic q ~ −q | Γ-orbit | fibre over (x, z) | ghost slabs at w ± δ |
| `size at distance` | w/d | w / (R·sinh(d/R)) | w / (R·sin(d/R)) | per image | per sheet | perspective /(f − w) |

**Implementation:**
- A single `IGeometry` interface (`Exp`, `Log`, `Distance`, `Transport`, `Images`, `Crossings`), plus `ISlicer` (4D) and `IWarp` (conformal lookup texture).
- The selection ray (X1) and any ray-marcher share `Exp` stepping.
- For continuous K transitions (the Curvature Dial) use the κ-stereographic model, which writes E, H and S as one formula family (Bachmann, Bécigneul & Ganea, ICML 2020) [U].

### 3.3 Three exact "unifier" results (slide-worthy; all [D], verify)

1. **Pinch-the-Sky lives in PSL(2,ℂ).**
   - Take the stereographic coordinate on the view sphere from the point behind you: z = tan(θ/2)·e^{iφ}.
   - A two-hand pinch dragging z₁, z₂ → w₁, w₂ fixes the Möbius map w = a·z + b (the maps that keep the point behind you fixed), with a = (w₁ − w₂)/(z₁ − z₂) and b = w₁ − a·z₁. The full group PSL(2,ℂ) would need three point pairs.
   - The same group is: (i) FieldFit's conformal lenses; (ii) isometries of H³, i.e. flying; (iii) Lorentz boosts.
   - FieldFit's best shape-preserving 54° → 10° lens is a = 0.172 = 1/5.8. That equals relativistic aberration at rapidity ln 5.8, i.e. **v = 0.94c**.
2. **The marker cube is a 4D slicer.**
   - The cube's orientation is a unit quaternion q, which is a hyperplane normal in ℝ⁴.
   - Slice = { q·(w₀ + a·i + b·j + c·k) }.
   - Turning the cube by θ tilts the slice by θ/2. A half-turn about x swaps x for w.
   - On 4D CT this turns depth into time, so the tumour's breathing motion becomes a solid tube whose shadow is the clinical **ITV** (internal target volume).
3. **Gauss–Bonnet as a ruler.**
   - Walk any loop of area A (shoelace formula on ARKit positions); a carried frame turns by θ = |K|·A.
   - Judges can *measure the curvature with their feet*: R = 1.5 m and a 1.5 m square give 1 rad.

### 3.4 Robustness rules for every technique

- **R1.** Continuous degrees of freedom ride on head and feet; hands give clutches, commits and short relative strokes.
- **R2.** Hand deltas count only while a clutch is held, so lost frames *pause* rather than jump.
- **R3.** Every hand technique has a twin input (non-negotiable 3).
- **R4.** Camera cone: interactables 20–30° below eye level, 40–55 cm away. Draw a faint frustum ring when a hand leaves it. Keep hands ≥35 cm from the face (LiDAR depth is valid from ≈0.30 m; use 0.32 m as the software cut-off) [M].
- **R5.** Generous snapping: targets ≥3° on S²; snap to tree nodes and to pure 4D rotation planes.
- **R6.** The world moves only while the body moves or a clutch is held. Rate-limit curvature, warp and Möbius changes. Use an FOV vignette during world motion.
- **R7.** Apply a One-Euro filter (Casiez et al., CHI 2012) to hand and cube signals *before* any gain, because exponential mappings amplify jitter.
- **R8.** Safe Harbour is always one gesture, button or word away.

### 3.5 The fifteen hero techniques (design-space heroes; the live path uses a subset, see below)

Each hero can be learned in ≤30 s, has a twin input, and produces a **live number** for the spectator screen.

| # | ID · name | Code | Blade | Judge is told | Twin | Live number |
|---|---|---|---|---|---|---|
| 1 | X1 Geodesic Ray | all · hand/head · exp · world · select | Hub, all | "Point at the lamp and watch the laser obey the space" | head ray | ray length vs straight-line distance; wraps and sheet changes |
| 2 | X5 Curvature Dial | all · wrist/tangible · gain · world · learn | Hub airlock | "Turn the dial: saddle → flat → ball" | cube twist, trigger, voice | K; circumference at 1 m: 2πR·sinh(1/R) vs 2π |
| 3 | H1 Polar Tree Reach | H · hand · exp · world · select | Branch, Lung | "Each hand-width further is a whole generation" | left stick (ρ, θ) | depth; nodes in reach (bⁿ); flat-equivalent reach r_E = 2R·sinh(ρ/2R) |
| 4 | H2 Grab-the-Plane | H · hand · iso · world · navigate | Branch, Lung overview | "Pinch the floor and drag the universe" | drag on hand-held Poincaré map | accumulated twist (holonomy) |
| 5 | H4 Foucault Cube | H/S · feet + tangible · hol · world · measure | Branch floor; hub S-mode | "Aim the arrow, walk any loop, aim again" | head-aim | θ measured vs K·A (exact for the virtual loop area; the ARKit shoelace area agrees only to O((size/R)²), so use loops ≲0.5R or the virtual area) |
| 6 | S1 Look-to-Aim | S · head · iso · world · select | Lung (approach sky) | "Every way you look is a needle; find green and hold" | gamepad commit | lung traversed (mm), fissures crossed, vessel clearance |
| 7 | Q1 Breathing Ring | 4t · feet · wrap · world · select | Lung (4D CT) | "Walk the circle; you're walking through a breath. Flag the quiet part" | Phase Dial (cube yaw) | gating arc (% duty cycle), ITV volume (cm³) |
| 8 | D2 Slice Cube | 4t/4s · tangible · slice · proxy | Lung (space-time tube = ITV); 4D toybox | "Turn the cube to turn the 4D knife" | sticks | slice normal; tilt = θ_cube/2 |
| 9 | S5 Blind-Spot Finder | S · head + pad · iso · field · measure | FieldFit | "Fix the cross; tap when you see a dot" | gamepad tap | blind-spot location (≈15° temporal) and size |
| 10 | S3 Pinch-the-Sky | S · hands · conf · field · manipulate | FieldFit | "Pinch the world like a photo until the room fits" | trigger sets the scale | a, Schwarz bound, "= aberration at 0.94c" |
| 11 | B1 Post Circling | B · feet · sheet · world · navigate | Shared House | "Walk through the door; now walk round just one post" | none needed | sheet index, monodromy σ, χ and genus |
| 12 | B3 Hand-over Through the Cut | B · tangible + other · sheet · world · communicate | Shared House / House Call | "Hand Grandma the cup through the doorway" | gamepad "send" | object's sheet path; table alignment residual (cm) |
| 13 | T2 Backdoor Reach | T · hand · wrap · world · manipulate | Glued Room (crystal) | "That molecule is closer through the wall behind you: reach" | Geodesic Ray (wraps) | minimum-image vs naive distance |
| 14 | T3 Glue Die | T · tangible · jump · world · create | Glued Room (universe) | "Twist the cube against the wall, then walk through" | gamepad picks the gluing | topology name (T², Klein, orbifold), orientability |
| 15 | P1 Rotating Hatch + pointing trial | P · feet · jump · world · navigate | Station Zero | "Go through the hatch, then point to the lifeboat" | head + tap | pointing error vs true frame vs yaw-only model |

**Safe Harbour (X9)** is always on and taught first: double-tap B, two palms down (Vision), or the operator's console button when the judge says "home" (voice is heard by the narrator, not recognised on the phone).

**In the live path:** X1, X5, H4, Q1 (as the Breath Room), D2 (the Breath Room's end-on view), X15 Two Compasses, S1, B1, plus the judge's choice (S5 + S3, or B3). **X12 Geometry Lens** (frame a rectangle with both hands; inside it the scanned room renders in H, S or a 4D slice; twin: trigger) replaces H2 as the demo's hand hero, because it is coarse, two-handed, pinch-free and inside the camera cone. H1, H2, T2, T3 and P1 are shown in the public loop, the 10-min extras or on video.

### 3.6 Condensed catalogue (all 63; details in the dossier)

- **Hyperbolic:**
  - H1 Polar Tree Reach
  - H2 Grab-the-Plane
  - H3 Loop Dial (walk a loop to rotate a ring of children by A/R²)
  - H4 Foucault Cube
  - H5 Constant-Width Flashlight / Cone = Subtree
  - H6 Ariadne's Rope (pull hand-over-hand home along the geodesic)
  - H7 Curvature Pinch (curvature as zoom)
  - H8 Tangent Doll (voodoo doll = tangent-space chart)
  - H9 Hand-held Poincaré Map
  - H10 Cairn (scanned real objects as landmarks)
- **Spherical:**
  - S1 Look-to-Aim
  - S2 Great-Circle Spacing Brush (beam layouts)
  - S3 Pinch-the-Sky
  - S4 Three-Right-Angle Walk (leg = πR/2)
  - S5 Blind-Spot Finder
  - S6 Lens Slider (Pareto lens)
  - S7 Stream-around-Scotoma Reader (Joukowski)
  - S8 Needle Cube
  - S9 Antipodal Whisper (S³ for two)
- **Glued:**
  - T1 Wrap-Walk
  - T2 Backdoor Reach
  - T3 Glue Die
  - T4 Mirror Loop (carry it through a flip and get the enantiomer)
  - T5 Joint-Torus Walk
  - T6 Lattice Pull (cell ↔ universal cover)
  - T7 Circles-in-the-Sky Hunt (the cosmological matched-circles test)
  - T8 Periodic Doodle (torus knots)
- **Portal:**
  - P1 Rotating Hatch
  - P2 Point-Through-the-Hull
  - P3 Fold (impossible overlap from the RoomPlan scan)
  - P4 Pocket Portal (frame a rectangle with your fingers)
  - P5 Portal Cube (six-port peephole)
- **Branched:**
  - B1 Post Circling
  - B2 Curtain Peek (lift the branch cut to knock)
  - B3 Hand-over Through the Cut
  - B4 Lap-a-Level (helicoid: each lap round a table = one floor)
  - B5 Continuation Walk (carry √z round a post; links to MRI phase unwrapping)
  - B6 Branched Carousel (seated 720° gallery)
- **Spatial 4D:**
  - D1 Walk-the-W
  - D2 Slice Cube
  - D3 Plane Picker (mid-air 4D rolling ball)
  - D4 Two-Hand SO(4)
  - D5 Look-Down Shadow (slice ahead, projection on the floor)
  - D6 W-Push (push an occluder out of your slice)
  - D7 Duet Rotation (two people, q_L and q_R)
- **Time (4t):**
  - Q1 Breathing Ring
  - Q2 Time Corridor
  - Q3 Phase Dial
  - Q4 Freeze-Frame Pins (time portals)
  - Q5 Dwell Trail
- **Cross-geometry:**
  - X1 Geodesic Ray
  - X2 Log-Map Beacon
  - X3 Native World-in-Miniature on the cube
  - X4 Flat Twin
  - X5 Curvature Dial
  - X6 Geometry Die
  - X7 One Table, Many Worlds (haptic retargeting onto the real table)
  - X8 Glue-That-There (voice + head topology editing)
  - X9 Safe Harbour
  - X10 Ground-Control Pointer (remote pointer transported through the geometry)
  - X11 Dose Loop (clinician-only)
  - X12 Geometry Lens (hand-framed model/distortion lens)
  - X14 Scan-to-Glue Key (a real object on the threshold sets where a door leads)
  - X15 Two Compasses (a Euclidean compass and a geodesic beacon disagree at a fork; the user decides)

### 3.7 The spaces, illustrated: how a 3×3 m floor maps into each geometry

Every space reaches the body in one of **four ways**. The mapping type decides comfort, rendering path and what can go wrong.

| Mapping type | What happens underfoot | Distortion | Spaces |
|---|---|---|---|
| **1. Copies and gluing** (topology) | Every step is ordinary Euclidean walking; only crossing a wall, door or cut changes your chart | none | Glued Room (T², T³, Klein, orbifolds), impossible rooms (P), Shared House (B) |
| **2. Curved walking** | Each footstep is mapped into the curved space by the exp map (C ← C·exp(v·dt)·R_head). The flat floor cannot be laid isometrically on a curved surface (Gauss's Theorema Egregium), so the virtual position depends on the *path walked*, not only on the physical position | metric, by construction | H², H²×E wings; S²×E Small Planet |
| **3. Bent views** | You stand; view directions are remapped on the sphere of view | angular, bounded (K\*) | FieldFit |
| **4. Slicing** | You stand in a 3D slice of a 4D dataset; feet, cube or time move the slice | none (it is a cross-section) | 4D CT, 4D toybox, "4D peek" |

**Safety is always computed in physical coordinates**, whatever the mapping (non-negotiable 1).

#### Glued Room: torus and Klein bottle

![Glued room: torus and Klein bottle gluing, and how 2×2 copies tile the real floor](illustrations/fig01_glued_room.svg)

- The virtual room is a 1.5 m cell; the taped 3×3 m floor holds 2×2 copies. **Room position = floor position mod 1.5 m** (a covering map ℝ² → T²).
- Exiting east re-enters west *of the same room*; physically the user just keeps walking. Rendering shows the neighbouring copies (instanced deck translations), including the user's own avatar.
- Klein bottle: top/bottom glued with a flip (glide reflection (x, y) ↦ (L − x, y + L)), so alternate rows of copies are mirror images. A mug carried once round the flip returns mirrored (chirality demo).
- T³ adds ceiling ↔ floor; you can't walk up, but you see copies above and below.
- See it: [Torus Games](https://www.geometrygames.org/TorusGames/index.html) · [3-torus](https://en.wikipedia.org/wiki/3-torus) · [Klein bottle](https://en.wikipedia.org/wiki/Klein_bottle) · [fundamental polygons](https://en.wikipedia.org/wiki/Fundamental_polygon).

#### Impossible rooms (portals)

![Impossible rooms: virtual plan vs overlapping footprints on the real floor](illustrations/fig02_impossible_rooms.svg)

- Two rooms reuse the same floor and are never visible together (closed door or bending corridor). Overlap up to ≈50% for small rooms goes unnoticed (Suma 2012) [M].
- This is the mechanism behind the procedural landscape (§3.8) and Station Zero.
- See it: [Tea For God devlog](https://void-room.itch.io/tea-for-god/devlog/235914/open-world-with-impossible-spaces) · [Unseen Diplomacy video](https://roadtovr.com/unseen-diplomacy-uses-every-inch-of-your-room-scale-vr-space/) · [UMN overlap diagrams](https://conservancy.umn.edu/server/api/core/bitstreams/a568a570-5af2-4385-934d-0257bdb4fdf3/content).

#### Hyperbolic plane (H², H²×E)

![Hyperbolic {4,5} tiling vs the real floor's 4-squares-per-corner grid, and circumference growth](illustrations/fig03_hyperbolic.svg)

- In the {4,5} tiling five squares meet at every vertex; on the real floor four do. Map each 1 m floor cell to one hyperbolic square (Holonomy, TU Delft 2024): a loop round a real corner visits 4 of the 5 virtual squares, so the user ends in a **new** square. Repeated loops give unbounded travel in 3×3 m with no redirection gains.
- Circumference at radius r is 2πR·sinh(r/R): at r = 3R that is ≈63R vs ≈19R flat. Trees with branching factor b fit when R = ℓ/ln b.
- That tile mapping forces R ≈ 0.8–0.94 m (strong, disorienting). **Lung Mission instead uses fork chambers joined by short portal corridors, laid out in H² at R ≈ 2.2 m** (§4.3.1, §7.2). The pure tile mapping is the "true curved space only" option (§7.13).
- Rendering: H²×E (hyperbolic floor, Euclidean vertical), Klein chart in the vertex shader (§7.2).
- See it: [Order-5 square tiling](https://en.wikipedia.org/wiki/Order-5_square_tiling) · [HyperRogue](https://roguetemple.com/z/hyper/) · [Hyperbolica](https://store.steampowered.com/app/1256230/Hyperbolica/) · [Holonomy](https://arxiv.org/abs/2409.05460).

#### Sphere (S², S²×E)

![Three-right-angle triangle on a sphere vs the same footsteps on the flat floor](illustrations/fig04_spherical.svg)

- Planet radius R = 1 m: walk π/2 ≈ 1.57 m, turn 90°, three times. On the planet you return to the start (an octant triangle, angle sum 270°). On the floor the same footsteps make an open square ending 1.57 m away.
- The 90° deficit is K·A = (1/R²)·(πR²/2) = π/2 (Gauss–Bonnet). The Foucault Cube (H4) makes it measurable.
- See it: [triangle with three right angles](https://felderbooks.com/curvedland/tuttriangle.html) · [interactive spherical triangle](https://books.physics.oregonstate.edu/MNEG/sphI.html) · [Curved Spaces](https://www.geometrygames.org/CurvedSpaces/).

#### Shared House (branched cover)

![Two home sheets joined at a doorway cut; paths through, around the frame, and around one post](illustrations/fig05_branched_house.svg)

- Each home is a sheet; a free-standing door frame is a branch cut between two branch points (the posts). Through the door: change sheet. Round the whole frame: stay. Round one post: change (you crossed the cut once).
- It is the Riemann surface of √z, made walkable. Flat everywhere except the posts (cone angle 4π each). Formulas in §4.4.
- See it: [Riemann surface](https://en.wikipedia.org/wiki/Riemann_surface) · [complex logarithm "parking garage"](https://en.wikipedia.org/wiki/Complex_logarithm).

#### FieldFit (sphere of view directions)

![FieldFit lens: a 54° field squeezed into a 10° residual field with a 1:1 centre](illustrations/fig06_fieldfit_lens.svg)

- Parameters drawn: E = 54°, ρ₀ = 10°, 1:1 core e₁ = 4°, giving K\* ≈ 2.9 (power-law lens, §4.5). Rings at 10°, 20°, 30° land at ≈5.5°, 7.0°, 8.0°.
- Standing or seated only.
- See it: [AAO glaucoma simulator](https://aao.org/eye-health/drugs/glaucoma-vision-simulator) · [Apfelbaum & Peli 2015 (field expansion)](https://pelilab.partners.org/papers/Apfelbaum_Peli_2015.pdf).

#### All spaces at a glance

| Space | Rule learned in 10 s | Mapping type | Where |
|---|---|---|---|
| T² / T³ / Klein / orbifolds | Every wall is a door to its opposite wall | copies (position mod L) | Glued Room |
| Portals / impossible rooms | The building is bigger than the room | rooms re-placed out of sight | Hub, Station Zero, procedural worlds |
| H², H²×E | Five squares at every corner; branches keep their width | curved walking | Lung airway, The Branch |
| S², S²×E | Walk straight and come back | curved walking | Small Planet; the needle sky is a view |
| Branched cover | Through the door is another home | sheet switch at a cut | Shared House |
| Sphere of view | The room squeezed into what you can see | bent view | FieldFit |
| 4t / 4s | You stand in a slice | slicing | Breathing Ring, Slice Cube, 4D toybox |

### 3.8 The procedural landscape: an endless world in a small room

![Logical room graph vs per-visit placement on the floor tiles](illustrations/fig07_landscape_rooms.svg)

A generated world is **two layers** (implementation in §7.4–7.6):
1. **The map** (logical graph): rooms and connections, generated a few doors ahead from a seed by a cyclic graph grammar (loops, consistent lock-and-key). Permanent and deterministic: the same seed gives the same world on return.
2. **The placement** (physical embedding): where each room sits on the real floor *now*. Recomputed for the next room whenever the current one hides it (closed door, bend). Constraints: eroded free floor, 0.5–1.0 m tiles, every door socket opens onto ≥1 free tile, co-visible rooms never overlap, hidden rooms may overlap ≤≈50%.

Style: a cascade region → room → props (palette LUT, fog colour, trim sheets, ambient bed) so the result reads as designed. Data blades are not random: the airway tree or the tree of life *is* the map.

**Mapping a real area (larger halls, see-through mode outdoors)**

![Scan → erode → tile → place; obstacles become in-world objects](illustrations/fig08_real_area.svg)

- Keep scanning while walking (ARKit mesh + RoomPlan). Only scanned, confirmed-free floor can host virtual content; unscanned floor stays fogged and unwalkable.
- Real obstacles become diegetic objects at the same place (trees, rocks, pillars), which is also the 2 m warning tier of §7.9. Dynamic obstacles (people) still trigger the 1 m / 0.5 m tiers and passthrough.

**Continuous curved landscapes**

![Hyperbolic tiling coloured by region: whole branches become biomes](illustrations/fig09_h2_regions.svg)

- Each H² tile is generated lazily when within k tiles, from its **address** (the move sequence from the hub, HyperRogue-style), so it is identical on every visit; never store float world coordinates (§7.1).
- Branches never re-meet in H², so whole branches make coherent regions ("lands"). The tile count grows exponentially with distance, so the world never runs out.

### 3.9 4D, explained

"4D" adds a fourth direction **w** at right angles to x, y, z. ATLAS never renders a 4D world in 4D perspective: every view is a **3D slice** or a **3D shadow** (projection), and the user gets a handle to move the slice. Two kinds:
- **4s, 4D space:** shapes with four spatial directions (tesseract, 4D ball). Used in the 4D toybox and to explain impossible rooms.
- **4t, 4D time:** 3D space + time as the fourth axis (4D CT). Used in Lung Mission. The maths is the same: a 4D dataset, sliced.

**Correctness note: is the medical 4D non-Euclidean?** Not by itself. ℝ³ × ℝ (space plus linear time) is flat ℝ⁴, and slicing it is Euclidean. What is non-Euclidean is the *topology*: breathing is periodic, so phase-binned 4D CT lives on ℝ³ × S¹, which is flat but wraps (the same kind as the Glued Room). The curvature in Lung Mission comes from the H² airway and the S² needle sky (§4.0). Likewise the tesseract lives in flat ℝ⁴; only the Tesseract House ({4,3,3} in S³) is curved.

![Flatland analogy: a 3D ball through a plane, and a 4D ball through our space](illustrations/fig10_4d_flatland.svg)

A unit 4D ball centred at w = c meets our slice w = 0 in a ball of radius √(1 − c²): it appears, grows, shrinks, vanishes.

![Tesseract perspective projection and corner-first slices](illustrations/fig11_tesseract.svg)

- Projection: (x, y, z)·f/(f − w) gives the cube-in-a-cube (8 cubic cells, 16 vertices, 32 edges).
- Corner-first slices of the unit tesseract by x + y + z + w = c: tetrahedron (0 < c ≤ 1) → truncated tetrahedron (1 < c < 2) → octahedron (c = 2) → back.
- See it: [Tesseract](https://en.wikipedia.org/wiki/Tesseract) · [Banchoff 1978 hypercube film](https://thekidshouldseethis.com/post/hypercube-projections-slicing-1978-4-dimensional-cube-tour-animation) · [4D Toys](https://4dtoys.com/) · [Miegakure](https://miegakure.com/).

**The 4D that matters: breathing (4D CT)**

![4D CT phases, the tumour's space-time tube, and the ITV as its shadow](illustrations/fig14_4dct_tube_itv.svg)

- 4D CT = ten 3D volumes, one per breathing phase. Stacked along time, the tumour (GTV) traces a tube in ℝ³ × time. The ITV is that tube's projection along time; gating keeps only part of the tube, so its shadow shrinks.
- Drawn with a Lujan-type motion z(t) = −b·cos⁴(πt), b = 12 mm, 20 mm tumour: ITV height 32 mm, gated (30–70%) 21 mm. **Illustrative only;** real numbers come from the 4D-Lung RTSTRUCTs (§4.3.2).
- See it: [AAPM TG-76](https://www.aapm.org/pubs/reports/RPT_91.pdf) (Fig. 6) · [Ezhil et al. 2009, open access](https://ro-journal.biomedcentral.com/articles/10.1186/1748-717X-4-4).

![Time is a circle: the Breathing Ring](illustrations/fig15_time_ring.svg)

Breathing is periodic, so the data lives on ℝ³ × S¹ (phase 90% is adjacent to 0%). The Breath Room (§4.2.1) maps walking east to phase; the Breathing Ring (Q1) maps floor angle to phase as the alternative layout.

![Slice Cube: cube rotation θ tilts the 4D slice by θ/2](illustrations/fig12_slice_cube.svg)

- The cube's unit quaternion q is the slice normal; slice = {q·(w₀ + a·i + b·j + c·k)}. Rotating the cube by θ about u gives q = cos(θ/2) + sin(θ/2)·u, so the slice tilts θ/2; a half-turn about x gives q = i and the slice x = const, i.e. x is swapped for w (or for time on 4D CT).
- Keep the quaternion sign continuous: 360° of cube turns the normal to −q (slice faces the other way), and 720° returns to the start.
- Implementation: tetrahedral meshes in ℝ⁴; a compute shader intersects each tetrahedron with the hyperplane and appends triangles/quads to an indirect draw buffer (§7.2). Looking down past −35° cross-fades to the projection on the floor (D5 Look-Down Shadow; along t on 4D CT this is the ITV).

![Overlapping rooms separated in w](illustrations/fig13_w_peek.svg)

Impossible rooms are a 3-manifold in ℝ⁴: give each room a w-range, doorways are w-ramps, and the user lives in the slice w = w(t) (§7.1). The "4D peek" slides w₀ so adjacent rooms ghost through each other.

| Where 4D appears | Kind | Status |
|---|---|---|
| Breath Room (Lung Mission story path; Breathing Ring as alternative) | 4t | live demo |
| Slice Cube on 4D CT | 4t | live demo (optional) |
| "4D peek" through impossible rooms | 4s | demo moment |
| 4D toybox: 4D ball, tesseract slices, Two-Hand SO(4) | 4s | roadmap |
| Tesseract House: 8 cube rooms, 3 per edge ({4,3,3}, S³) | 4s | roadmap |

### 3.10 Scanning and generation: which part does what

Two different pipelines feed the worlds. They meet on the floor plan of the real room.

**Scanning: the real room becomes non-Euclidean (live).**
1. RoomPlan scans the room inside the app: walls, doors, windows and furniture as parametric boxes (§7.8).
2. The room is rebuilt from the modular kit: real doors become portal sockets, furniture becomes walls and touchable props, and the free floor is eroded and cut into 0.5–1 m tiles (§7.4).
3. The world map is saved for relocalisation; the floor marker re-anchors.

| Where the scan goes non-Euclidean | How | Section |
|---|---|---|
| Bend Your Room | The scanned room itself is placed in curved space vertex by vertex (exp map; Klein map where walls must stay flat) and bent by the curvature dial | §4.1, §7.3 |
| Post Circling and House Call | Pre-scanned rooms become the sheets of a branched space: round one post and you are in another home | §4.4 |
| The judge's object | 1–3 S24 photos → SPAR3D mesh → a cairn inside the hyperbolic airway | §4.3.1, §7.8 |
| Real obstacles | Become objects in the world at the same place (tree, pillar), which is also the 2 m safety tier | §7.9 |
| Every generated room | Must fit the scanned, eroded free floor, with one free tile beyond every door | §7.4 |

**Procedural generation: worlds bigger than the room.**
1. **The map:** a logical graph grown from a seed (cyclic graph grammar: loops and consistent lock-and-key). Same seed, same world.
2. **The placement:** where each room sits on the real floor right now, re-solved whenever a door closes; hidden rooms may overlap by up to ≈50%. This is how a building larger than 3×3 m fits (§3.8, §7.5).

| Generated world | Status |
|---|---|
| **Roll a Lung:** the patient's airway tree is the map; distal generations are grown by a space-filling model (labelled "model-generated"), laid out in H², built from kit chambers, and each next chamber is placed on the floor tiles | **Live** (D17, §4.3.1) |
| Seed-generated worlds (graph grammar + style cascade); endless H² landscapes generated tile by tile from their address; auto-dressed interiors (WFC) | Roadmap (Tier B/C) |

**Escher-like spaces.** Covered by the Glued Room (every wall is a door to its opposite wall; Klein-bottle mirror flip), the overlapping rooms of the placement layer, the Breath Room's looping time and Post Circling. Escher's gravity and staircase tricks (stairs that rise forever) are not in the live path: on a flat real floor a virtual ramp risks sickness. The **Penrose Loop** (§4.0, Tier C) builds the ever-rising stair honestly, as a spiral staircase glued to itself, for a seated view or the roadmap.

---

## 4. Blade specifications

§4.0 says why each blade needs its geometry. Each Tier A blade below then specifies: purpose → tasks → geometry and maths → data and pipeline → interactions → rendering notes → metrics → acceptance criteria → risks.

### 4.0 Why each blade needs its geometry

"Non-Euclidean" covers several different things. For mathematical correctness (30% of the score), every blade must say **which kind** it uses and **why its content has that shape**. Use these kinds consistently in docs, captions and narration:

| Kind | Meaning | Codes |
|---|---|---|
| **Curved** | Constant curvature K ≠ 0. Negative: hyperbolic (H², H³). Positive: spherical (S², S³). Triangle angle sums ≠ 180° | H, S |
| **Flat, wrapped** | K = 0 everywhere, but the space closes up (a quotient of Euclidean space): torus T², T³, Klein bottle, flat orbifolds, a circle of time S¹ | T, 4t (periodic) |
| **Branched** | Flat sheets joined along cuts; curvature is concentrated at the branch points (cone angle 2πk ⇒ curvature 2π − 2πk) | B |
| **Impossible gluing** | Flat rooms joined by isometries so they overlap or cannot embed in ℝ³ (portals) | P |
| **Directions** | The sphere of directions S² around a point, and maps of it (Möbius, quasiconformal) | S (view) |
| **Path metric** | A flat domain whose distance is measured along walkable paths around obstacles | — |
| **4D** | Flat ℝ⁴ (slices, projections) unless the 4D space is itself curved (S³) | 4s, 4t |

**Argument strength** (for prioritising the pitch): **strong** = the content's structure forces this geometry; **medium** = the geometry fits the structure and helps; **baseline** = Euclidean on purpose.

**Tier A**

| Blade | Kind · strength | Why the content has this shape | How the body meets it | What flat space can't do | Honest limit |
|---|---|---|---|---|---|
| **Hub** | Euclidean · baseline | Every curved world needs a flat reference; the real room is the home chart and Safe Harbour | Curvature Dial bends the room saddle → flat → ball at 1:1 underfoot; circumference at 1 m = 2πR·sinh(1/R) … 2π … 2πR·sin(1/R) live | n/a: it *is* the flat twin | Non-Euclidean only while the dial is turned |
| **Glued Room** | Flat, wrapped (T², T³, Klein, orbifolds) · medium–strong | Periodic content: crystals are a unit cell repeated (periodic boundary conditions in MD/DFT); joint angles wrap at 360°; LLM weekday features are circular; cosmic topology | Room = floor mod 1.5 m; Glue Die changes the gluing | In a closed box a molecule just through the wall is 1.4 m away instead of 0.1 m (minimum image). No flat room turns a mug into its mirror image by transport; a non-orientable Klein bottle does | Locally Euclidean; all effects happen at the walls |
| **Lung · airway** | Curved, negative (H²) · strong | ≈23 generations of near-binary branching (2^g branches); H² circumference grows as 2πR·sinh(r/R), so constant-width corridors fit at radius ∝ g | Chambers and corridors laid out in H² at R ≈ 2.2 m (Sarkar-style angular placement); walked distance monotone in insertion depth; greedy success toward the beacon **measured** on real trees (spike 0.19) | E² crowds after a few generations; even the star K₁,₆ has no greedy embedding in E² (Kleinberg); steering by 3D direction in the real lung misleads (Dolina 2008), shown live by X15 Two Compasses | The lung itself sits in ordinary 3D space; what lives in H² is its branching structure. Distorts 3D branch angles: pair every fork with the endoluminal view and a planar dendrogram; no 1+ε distortion claim at this scale |
| **Lung · breathing** | Flat, wrapped (ℝ³ × S¹) · medium | 4D CT is phase-binned over a periodic breath, so 90% is adjacent to 0% | Breath Room: walking east = phase (alternative: a 0.75 m floor ring); two flags bracket the gating arc; ITV and duty update live; **Slice Tilt** turns the room's slice in a time–space plane with the cube, a rotation in a plane we cannot see | A linear slider cuts the cycle at its ends; a walked circle preserves adjacency at walking pace | **No curvature.** 4D CT alone is flat ℝ⁴ and the Slice Cube slices flat 4D. Say "time is a circle: flat, but it wraps" |
| **Lung · needle sky** | Directions / curved, positive (S²) · strong | The set of straight paths to a point *is* S²; head direction is also a point of S², so looking = choosing | HEALPix risk map (nside 64, 49,152 equal-area cells); dwell on green; great-circle beam spacing; robust windows = ∩ over 10 phases; **great-circle check:** sweeps that leave the equator side by side meet at the pole, and three windows form a triangle whose angles sum to more than 180° | No flat chart of S² is faithful; az/el grids crowd at the poles and distort risk areas | "A sky is a skybox": the defence is equal-area sampling, great-circle constraints, the great-circle check and the multi-phase intersection |
| **Shared House** | Branched (walkable Riemann surface) · strong | N real homes must each stay full-size at 1:1 inside every visitor's free floor | Sheets per home; door = cut, posts = branch points; through = visit, round one post = switch | Flat telepresence must use the rooms' intersection (13% / 0% placement success at 4 / 6 clients, Choi 2024) or one merged room | Prior art MRUnion (TVCG 2025); cross-device alignment is several cm, so use snap zones |
| **FieldFit** | Directions; conformal and quasiconformal maps · strong | A visual field is a region of the sphere of directions; a lens is a map between such regions | Camera remap so E = 54° lands inside ρ₀ = 10° with a 1:1 core | Uniform minification of a flat image loses central acuity; the Schwarz lemma bounds any conformal lens to ≥5.8× central minification, which forces a quasiconformal (power-law or Teichmüller) lens | Research prototype; ≈14–17 px/deg display; standing only |

**Tier B**

| Blade | Kind | Why this shape | What flat space can't do | Honest limit |
|---|---|---|---|---|
| Station Zero | Impossible gluing (charts glued by SO(3) rotations) | Each module has its own "up"; the station is an atlas of local frames | No global flat frame keeps every module's "up"; documented Mir/ISS disorientation (Oman 2007) | Rotating "up" is the most sickness-prone; opt-in |
| The Branch / Deep Time | Curved, negative (H²) | Design trees and the Open Tree of Life (2.39M tips) grow exponentially | Flat layouts crowd after a few levels | Users rate hyperbolic trees "cool" without performing better (§2.1): always pair with a flat list |
| Small Planet | Curved, positive (S²×E) | Weather lives on S²; the Foucault pendulum's latitude-dependent turn is a holonomy of the curved Earth | Flat maps distort directions; globes beat flat maps for direction judgements [M] | Small R exaggerates curvature on purpose |
| Manifold Lab | Flat, wrapped (S¹, T²) | LLMs represent weekdays and months on circles (Engels et al., ICLR 2025) | A linear axis cuts the circle | Topology, not curvature |
| C-Space Room | Flat, wrapped (T², T³) | Revolute joints wrap at 360°; two joints form a torus (configuration space) | A square chart has a seam at ±180°; wrapped shortest paths look long | Needs continuous-rotation joints |
| Heartbeat Torus | Flat, wrapped (T² of time) | Two independent periodic motions (breathing and heartbeat) make time two-dimensional and doubly periodic: a torus | One slider or one loop cannot keep both cycles' neighbours adjacent; the torus floor does (east = breath, north = heartbeat) | No public dataset has both cycles in one patient: a labelled composite (4D-Lung breathing + ACDC cardiac cine MRI); topology, not curvature |

**Tier C**

| Blade | Kind | Why this shape | What flat space can't do | Honest limit |
|---|---|---|---|---|
| Cortex Planet | Curved, positive (S²) | The cortex is a folded sphere; neuroimaging inflates it to a sphere for registration and spin tests | Flat cortical maps need cuts | Mainly education |
| Territory Surgeon | Curved, negative (H²) | Vessel trees branch like airways | Same crowding as the airway | Segmentations stop short of fine vessels |
| Colon Gallery | Flat, wrapped (flat cylinder E²/ℤ via conformal flattening) | The colon wall is a tube | Fly-through alone sees 76.6% of mucosa (Pickhardt 2006); the unfolded wall shows all of it | Hardest pipeline; show the distortion overlay |
| Geodesic Soundscape | Path metric (log map of the walkable domain) + tanh distance compression | Sound should come from where the walkable path goes | Straight-line bearings point through walls | Flat floor; only the metric is non-Euclidean |
| Wormhole Quest | Impossible gluing + holonomy | Non-Euclidean shortcuts dissociate map-based from graph-based navigation (dementia research) | In flat worlds both strategies predict the same answer | Research instrument, not a diagnostic |
| Folded Building | Impossible gluing | Rehearse a real building at 1:1 in a small room | A 3×3 m room can't hold a building | It is impossible-space locomotion; present it as that |
| House That Never Runs Out | Curved, negative (H²) + portals | Ancestors double every generation (2ⁿ); pedigree collapse = wormholes | A flat plan runs out of room at ≈5 generations | Emotional, not clinical |
| Embryo Room | 4D time (flat) + H² lineage tree | Development is 3D + time; lineages are trees | Flat lineage trees crowd | Time is linear, so the 4D part is flat |
| 4D Toybox / Tesseract House | Flat 4D (toybox); curved S³ ({4,3,3}, House) | Slices of 4D shapes; 8 cube rooms, 3 around every edge | Cubes meet 4 per edge in flat 3D; 3 per edge (120° dihedral) needs S³ | Only the House is curved |
| Penrose Loop | Impossible gluing (a helix quotiented by a screw motion) | Escher's ever-rising stair is a spiral staircase whose top flight is glued to its bottom, a walkable picture of the universal cover of a circle | No staircase in flat space keeps rising and returns to its start | A flat real floor under a virtual ramp risks sickness; seated view or roadmap only |

**Narration rules:** name the kind in one sentence per blade ("the airway is a tree, so it lives in hyperbolic space"; "breathing repeats, so time is a circle: flat, but it wraps"; "each home is a sheet; the curvature sits in the doorposts"). Never call 4D CT non-Euclidean on its own, never call the Glued Room curved, never call the lung itself hyperbolic (its branching structure is), and back every geometric claim with a live number on the Knife Map (§5.5).

### 4.1 Hub: the scanned room (Euclidean home)

- **Purpose.** Calibration, onboarding, safe harbour and blade selection. It is the *chart at the origin* of the atlas.
- **Content.**
  - The RoomPlan scan of the demo area, rebuilt from the modular kit (walls, floor and doors as clean meshes).
  - Real furniture becomes virtual furniture in the same place: passive haptics, and collision walls in every blade.
  - The taped 3×3 m square is drawn as a soft floor ring.
- **Tasks.**
  - Fit check in passthrough.
  - "Raise your hand; touch the glowing button" (hands visible check).
  - Learn A/B.
  - **Bend Your Room** (live path, 30 s), below.
  - **X6 Geometry Die** (roll the cube to choose a world), or the operator picks (10-min Q&A only).
- **Bend Your Room (live path).**
  - **X5 Curvature Dial:** the κ-stereographic family (one formula for K < 0, 0, > 0) bends the kit-rebuilt room in the vertex shader; the floor stays fixed (H²×E / S²×E); the dial moves only while clutched, with |dK/dt| rate-limited and an FOV vignette. Range R ≥ 1.5 m, gated by spike 0.14 comfort results.
  - **H4 Foucault Cube:** the judge carries a virtual arrow around a taped 1.5 m square at R = 1.5 m; the expected turn θ = |K|·A uses the *virtual* loop area (≈1 rad) and is shown beside the measured turn on the equation card.
  - **X1 Geodesic Ray** at the real lamp: the beam bends as K changes.
  - **Twins:** trigger or cube yaw for the dial; head-aim for the arrow.
  - **Acceptance:** smooth at 60 fps on phone B; measured turn within 5° of |K|·A for the virtual loop; median FMS rise ≤1 across the segment in rehearsals.
- **Doorways.** Each blade is entered through a door in the hub. The door is a stencil portal with a "geometry airlock" caption chip, e.g. "T³ · walls glued by translation".
- **Acceptance.**
  - Passthrough → VR fade in <1 s.
  - Hand cursor visible at chin height within 0.5 s of raising a hand (until hands land in Phase 4, the onboarding check uses gaze + A).
  - X9 Safe Harbour from anywhere in <300 ms.
  - 60 fps p99 in the hub with the scan loaded.

### 4.2 Glued Room (warm-up blade; T², T³, Klein bottle, orbifolds)

- **The rule.** "Every wall is a door to its opposite wall." (Glue Die can change the rule; the caption must say so when it does.)
- **Cell size.** A **1.5 m virtual cell inside the 3×3 m floor** (2×2 cells), so boundary crossings happen on open floor. For T³ the vertical period is ≥2.6 m (above head height): a 1.5 × 1.5 × 2.6 m box is still a 3-torus. The outer edge of the taped square is a solid virtual wall; use 2:1 turn resets only for long runs [D]. Optional: four identical real stools at the same offset in each physical cell make the covering map touchable.
- **Tasks.**
  - **T1 Wrap-Walk:** walk out east, come in west. Look ahead and see the back of your own avatar in the next copy.
  - **T2 Backdoor Reach:** an object near the far wall is grabbed by your hand's image through the near wall (minimum-image convention).
  - **T3 Glue Die** (Phase 4, with the cube; Phase 1 selects gluings with the gamepad): hold the cube against a glowing wall and twist or flip it. That wall's gluing changes. Walk through and arrive rotated or mirrored; a live caption names the space (T², Klein bottle, p4 orbifold…).
  - **T4 Mirror Loop:** carry your scanned mug through the flip wall and back the plain way. It's now the mirror image of its stay-at-home twin.
  - **Stations (Tier B, optional):**
    - **Crystal:** CALF-20 unit cell; thread CO₂ to a zinc site. Live Orb-v3 on the H200 is optional; otherwise precomputed trajectories.
    - **Robot torus:** the floor is two joint angles.
    - **LLM weekday ring:** the model speaks "two days after ___".
    - **Universe finale:** pick a platycosm.
- **Maths (correctness notes).**
  - Translation gluing gives T². A glide reflection gives a **Klein bottle** (non-orientable manifold).
  - A rotation gluing about the **vertical** axis has a fixed axis, so it gives an **orbifold with a cone point** (a p4 orbifold for quarter turns), **not** a manifold. Label it as such.
  - True screw gluings roll you about the *walking* direction. A quarter-turn needs a square cross-section (room width = virtual ceiling height) [D].
- **Rendering.**
  - Draw neighbour copies by deck translations: 3×3 instanced cells plus fog (5×5 only if spikes allow). Cheaper than recursive portals. Glide-reflected (mirrored) copies need a separate instanced draw with the cull mode flipped, because mirroring reverses triangle winding.
  - Fog with distance. Matched colours on glued walls.
  - The universal-cover view (T6 Lattice Pull) opens all walls at once.
- **Metrics.** Wraps per minute; minimum-image vs naive distance on grabs; time to first successful backdoor reach; FMS.
- **Acceptance.**
  - No visible seam at a crossing in either eye.
  - Self-avatar visible in the next cell.
  - Flip gluing produces correct chirality (verified by superposition test).
- **Risks.** Lowest-sickness blade (locally Euclidean). Main risk is cube occlusion while twisting; commit with button or pinch.
- **Role in the demo.** The classic Glued Room (crystal, Klein flip, universe topology) runs as the 60 s public loop between judges and on video; the live path uses its engine as the Breath Room.

#### 4.2.1 Breath Room (live-path variant; ℝ³ × S¹)

- **Rule:** "Walking east moves you through the breath: the east wall is 100%, the west wall 0%."
- **Geometry:** the Glued Room engine with the east–west gluing reinterpreted as time: phase φ = 2π·(x mod L)/L with L = 1.5 m. Flat but wrapped (§4.0); not curved, and the narration says so.
- **Content:** a 3D slice of the 4D (x, y, z, t) data taken through the tumour centre, so the room's axes are (t, head–feet, left–right). The GTV becomes a **space-time tube** along the room's x axis; every copy of the cell repeats it, so the judge sees an endless periodic tube (the universal cover ℝ → S¹ made visible). Looking east along the tube shows it end-on: its silhouette is the ITV in that slice. This is the Slice Cube's half-turn view (§3.9) built as a room.
- **Tasks:** walk along the tube (scrub the breath); drop two floor flags to bracket the gating window, which lights that part of the tube; ITV volume and duty cycle update live; turn to face east and watch the shadow shrink. T1 self-avatar in the next copy. Optional 5 s Klein flip in the 10-min slot only.
- **Slice Tilt (10 s, cube; 10-min slot, 5-min slot if time allows):** the room shows one 3D slice of the 4D data. Tilting the cube rotates that slice in the plane spanned by time and left–right, from "time along the room" (0°) to "an ordinary 3D snapshot" (90°), and the tube morphs into the tumour's 3D shape: a rotation in a plane we cannot see, which is the challenge's 4D-projection idea. Precompute 7 tilted slices per case (0–90° in 15° steps) and blend neighbours; slicing a 4D tetrahedral mesh live with the 4D slicer (§7.2) is the stretch goal.
- **Data:** 10 phase GTV masks → per-phase SDFs on a 64³ ROI → resampled to a (t, y, z) volume of 64³ → tube mesh by marching cubes, **precomputed on the PC per case** (static, so no runtime crossfade). ITV(arc) = OR of phase masks, precomputed for all 10×10 arcs. Plus 7 tilted slice meshes for Slice Tilt.
- **Twin:** Q3 Phase Dial (cube yaw), seated.
- **Acceptance:** tube visible across ≥3 copies at 60 fps on phone B; ITV numbers equal the precomputed table; flags placeable by pad or gaze; Slice Tilt steps cleanly through 0–90° at 60 fps.

### 4.3 Lung Mission (deep blade; H² · S¹ · S² · optional T²)

**Story.** A de-identified public patient has a lung lesion. **Data choice:**
- **Case cards:** the deck is the TCIA 4D-Lung patients (≈20; Option A below), because the Breath Room needs real breathing phases. LIDC-IDRI cases can be added only as labelled composites (Option B).
- **Option A (recommended for the demo):** one TCIA 4D-Lung patient throughout: real motion and real GTV on all 10 phases. These are locally advanced NSCLC tumours, so call it a "target lesion", not a small nodule.
- **Option B:** a *composite teaching case*, with airway and peripheral nodule from LIDC-IDRI and a breathing motion field from 4D-Lung registered onto it, labelled "composite" on screen.

The trainee must:
1. route a bronchoscope to it;
2. account for breathing;
3. choose a needle path (or, if inoperable, radiotherapy beams).

Each step happens in the geometry that matches its structure. The output is a **plan card** (route forks and length, robust exit, gating arc, needle direction with clearances). Framing: *training and visualisation on public data, not for clinical use*.

#### 4.3.1 Airway wing (H², "Bronchial Labyrinth")

**Why H² is essential**
- **The airway is a near-binary tree of about 23 generations** (Weibel) [O]. Hyperbolic circumference grows as 2πR·sinh(r/R), so 2^g constant-width corridors fit at radius ∝ g; the Euclidean plane cannot hold them.
- **Walked distance tracks insertion depth.** Corridor length is monotone in real segment length, ℓ_virtual = clamp(s·ℓ_real, 1.0 m, 2.0 m), and the real insertion depth is on the HUD. **Claim limit:** Sarkar's 1+ε distortion guarantee needs edges many curvature radii long, so it does **not** apply at R ≈ 2.2 m with 1–2 m corridors. We claim constant corridor width and room for every branch, not low distortion.
- **Greedy wayfinding: measured, not assumed.** Every tree has a greedy embedding in H² (Kleinberg), while in E² even the star K₁,₆ fails [O]. Whether "walk toward the beacon" is always right *at our scale* is measured on real trees (spike 0.19 and the H200 study), and the beacon rule is chosen from that result. In the real lung, steering by 3D direction misleads, which is the error class Dolina measured.
- **Honest limit.** The H² layout distorts 3D branch orientation. **Always pair it with the true endoluminal (virtual bronchoscopy) view** at every fork (clock positions: "take the 3 o'clock branch"), and give the hand-held map a planar dendrogram as a second flat twin. Planar airway-tree layouts exist, so the H² case rests on walkable constant-width corridors and measured greedy success.

**Locomotion (resolves "curved walking" vs "portals")**
- Each fork is a **chamber**, a disc of radius ≈0.5 m (≈0.23R, nearly flat) at a node of the H² layout; each parent → child link is a **corridor** of length ℓ_virtual. The wing renders in H²×E, and inside chambers and corridors the camera moves by the exp map (gentle curved walking at this scale).
- The airway is a tree (no loops), so it can't be walked continuously in 3×3 m. Each corridor ends at a **valve door**; when it closes behind the judge, the next corridor and chamber are re-embedded on the floor tiles by the tile solver (§7.4). Topology handles the floor; curvature handles the layout and the view.
- About 2.5 m is walked per fork (corridor + chamber), so 4 forks ≈ 10 m of airway.

**Roll a Lung (procedural generation, live; D17)**
- Before the session the judge picks a **case card** (a pre-segmented 4D-Lung patient, or a labelled LIDC-IDRI composite).
- PC pipeline, target ≤10 s: load the segmented proximal tree (`tree.json`) → complete the missing distal generations toward the lesion's lobe with a published space-filling airway model (e.g. Montesantos et al., PLOS ONE 2016; Tawhai-type algorithms), constrained to the lung mask → mark completed branches `"source":"model"` (dashed on the map, labelled "model-generated") → H² layout → kit chambers and corridors → tile placements → stream to the phone (`AssetReady`).
- Fallback: a curated default case cached on the phone.

**Tasks**
1. **X15 Two Compasses** at one designed fork: a ghost *Euclidean* compass (the 3D direction to the target in the real chest) points down one branch while the hyperbolic beacon points down the other; the judge chooses using the endoluminal view, and the choice is logged. Elsewhere the beacon is a rescue aid that appears after 8 s of hesitation. The route cost so far is shown (insertion length, sharp turns, narrowest lumen vs scope OD: Ion 3.5 mm, Monarch 4.2 mm, Galaxy 4.0 mm [O]).
2. Compare 2–4 Pareto routes as coloured threads.
3. Rehearse: the H² scaffold fades and the trainee drives the endoluminal view alone; correct forks are scored.
4. Optional QA: confirm or correct automatic segment labels while walking depth-first, with a coverage meter.
5. **Pointing probe** at the nodule chamber: "point back to the trachea door and pinch" (§8.1).

**Interactions:** walking forks; X15 Two Compasses; H5 Cone = Subtree (select everything downstream); H6 Ariadne's Rope (go home); H9 hand-held Poincaré map (with planar dendrogram twin); X2 Log-Map Beacon. The judge's photographed object is placed as a cairn at the lesion chamber.

#### 4.3.2 Breathing Ring (S¹; alternative layout to the Breath Room)

**Why it is essential.** Phase-binned 4D CT is ℝ³ × S¹: flat, but wrapped in time (topology, not curvature; §4.0). Phase 90% is adjacent to 0%. A linear slider breaks that; a walked circle preserves it. The ITV is the union of the GTV over a phase arc, i.e. the shadow of a 4D swept volume. This is the real clinical 4D use (4D CT is the standard of care; tumours move 0–34 mm; manage motion above 5 mm, AAPM TG-76) [O].

**Tasks (Q1)**
- *Used if the Breath Room tests poorly, and as the seated/standing variant.* Walk a **0.75 m-radius** floor circle (it must fit the eroded play area, 2.0–2.4 m across; the 1 m boundary-grid tier is suppressed along the ring path): your angle is the phase, and the translucent chest breathes with your steps. Phase meshes of different topology are not cross-faded; interpolate per-phase SDFs on a 64³ ROI and extract on the GPU, or show discrete phases.
- **Q5 Dwell Trail** shows the tumour's closed trajectory.
- Drop two flags to bracket the quiet arc. The ITV volume (cm³) and duty cycle update live.
- Show the ≈17 mm CT-to-body divergence for the bronchoscopy tie-in, and choose the airway exit that stays closest across phases.

**Formulas**
- φ = atan2(z − c_z, x − c_x)
- bin = ⌊10·(φ mod 2π)/2π⌋ (atan2 returns (−π, π])
- ITV(arc) = ∪ GTV over the bins in the arc
- duty = arc/2π
- margins: van Herk 2.5Σ + 0.7σ, user-set [O]

**Twin.** Q3 Phase Dial: cube yaw, for seated use.

**Optional.** D2 Slice Cube: a half-turn makes depth into time, and the breathing tube is the ITV.

#### 4.3.3 Approach Sky (S², sphere of straight paths)

**Why it is essential.** The configuration space of "straight path to point p" *is* S². Head direction is also a point on S², so the body becomes the problem's coordinates.

**Data**
- Risk f(u) on a **HEALPix** grid (nside 64 = 49,152 equal-area pixels ≈0.9°).
- Computed by ray-marching distance fields of vessels, fissures, ribs, heart and other organs.
- Robust windows = ∩ over the 10 breathing phases.

**Tasks**
- **S1 Look-to-Aim:** look at green windows; dwell 600 ms in a ≥3° safe disc, or press A.
- A needle draws from skin to target with readouts: lung traversed (mm), fissures crossed, nearest-vessel clearance, entry angle.
- **Great-circle check (5 s):** the chosen window and two alternatives are joined by great-circle arcs, and the plan card shows the triangle's angle sum (always more than 180° on a sphere). Two needle sweeps that leave the equator side by side visibly meet at the pole: on a sphere, parallels meet.

**Radiotherapy option**
- **S2 Great-Circle Spacing Brush** places 5–10 beams with minimum great-circle spacing; gantry–couch collision zones are greyed out.
- The machine chart is a torus T² (gantry θ, couch φ) that **maps 2-to-1 onto the sphere** except at the poles θ = 0, π, where whole circles collapse (so it is not a covering map): (θ, φ) and (−θ, φ+π) give the same direction. Couch rotation has no effect at θ = 0 or π [D]. Walk the periodic floor to see it.

**Compute.** Sphere-trace with 20–40 steps (not 256 fixed samples) from the target along the 49,152 HEALPix directions (NESTED indexing) through **one RGBA8 3D texture per phase** (R vessels, G fissures, B bone, A other organs): a 128³ ROI at 1 mm plus a 64³ context volume at 4 mm, ≈9 MB per phase, ≈90 MB for 10 phases. Time-slice the work to ≤1.5 ms per frame; the current phase updates in <100 ms and all 10 phases in ≤300 ms after a target drag [U, spike 0.20]. Risk f(u) = weighted minimum clearance (vessels weight 1.0; fissure crossing a penalty; bone blocked); **green** = clearance ≥5 mm in every phase and no fissure crossed (teaching thresholds, illustrative). CPU parity tests use astropy-healpix (BSD), not GPL healpy.

**Honest weakness.** "A sky is just a skybox." The defence is the equal-area sampling, great-circle constraints, the great-circle check, the multi-phase set intersection and the T² ↔ S² linkage.

#### 4.3.4 Data pipeline (PC / H200)

```
TCIA DICOM (LIDC-IDRI; 4D-Lung, all CC BY 3.0)
  → TotalSegmentator (Apache-2.0): `total` (lobes, ribs, heart, aorta) + `lung_vessels` (arteries, veins, airways, airway wall)
    RISK: users report TotalSegmentator airways stopping at generations 1–2; benchmark MONAI Auto3DSeg / nnU-Net airway models (spike 0.19); complete distal generations procedurally (Roll a Lung)
  → skeleton graph (skan or VMTK; kimimaro is GPL, internal experiments only; spur pruning; radius-weighted minimum spanning tree; manual QA; freeze one curated tree per demo case) + radii (EDT) + labels
  → H² layout (Sarkar-style angular placement at R ≈ 2.2 m; store PARENT-RELATIVE isometries only, never global hyperboloid coordinates, which overflow for deep trees) → tree.json (schema below)
  → marching cubes → meshoptimizer/gltfpack (LOD0/1, KTX2, meshopt) → .glb
  → distance fields (vessels, fissures, ribs) → 3D textures (R8/R16, ≤256³ ROI) for the Approach Sky
4D-Lung RTSTRUCT: GTV + OARs on all 10 phases → per-phase meshes; precompute ITV unions per arc (or voxel OR on the phone for a 64³ ROI)
pylidc → nodule centroids from LIDC XML
```

- **Licences.** ATM'22 airway labels are **not redistributable** (train on them only under agreement; ship only meshes derived from our own model runs). MSD hepatic is CC BY-SA (share-alike).
- **Validation data (DIR-Lab 4DCT).** Ten thoracic 4DCT cases with expert-registered landmarks across the breathing cycle (request form; usage terms not posted, so keep it internal unless permitted). Use it to check the registration behind composite cases (Option B), to sanity-check the motion magnitudes the Breath Room shows, and as a golden test case for the tube pipeline. Not a case card unless its terms allow redistribution.
- **Triangle budget.**

  | Element | Triangles (LOD0, visible) |
  |---|---|
  | Airway mesh | ≈40k (split per lobe; ≤50k per chunk) |
  | Vessels | ≈30k |
  | Lobes | 5 × 3k |
  | Corridors and chambers | ≈20k |
  | Tube, overlays, cairn | ≈10k |

  ≈115k, inside the **≤150k triangles per eye including portal views** budget (§7.2).
- **`tree.json` schema (v1).** Lengths in mm unless stated; one record per branch.

  ```json
  {"schema":"atlas.tree/1","case":"C07","R_m":2.2,"scale_s":12.0,"scope_od_mm":4.2,
   "nodes":[{"id":0,"parent":-1,"gen":0,"label":"trachea","source":"segmented",
             "pos_mm":[0,0,0],"dir":[0,0,-1],"len_mm":118,"radius_mm":8.9,
             "h2":{"angle_rad":0.0,"len_m":2.0,"parent_iso":[[1,0,0],[0,1,0],[0,0,1]]}}],
   "target":{"node":57,"lesion":"L1","gap_mm":6.2},
   "phases":{"count":10,"gtv_sdf":"gtv_phase{0..9}.r8.ktx2"}}
  ```
  `source` is `"segmented"` or `"model"`; `parent_iso` is the SO⁺(2,1) isometry from the parent chamber's frame.
- **Plan card schema (v1)** (rendered on screen and as a one-page PDF on the PC):
  `{"schema":"atlas.plancard/1","case","route":{"forks","len_mm","min_lumen_mm","scope_ok"},"compass_choice":{"fork","chose","euclid_pointed","correct"},"gating":{"arc":[lo,hi],"duty","itv_cm3","itv_full_cm3"},"needle":{"az_deg","el_deg","lung_mm","vessel_clear_mm","fissures"},"probe":{"err_geodesic_deg","err_euclid_deg"},"illustrative":true,"not_for_clinical_use":true}`
- **H200 batch jobs.**
  - Segmentation of all ≈1,010 LIDC cases plus 4D-Lung.
  - **Greedy-routing study:** for thousands of random peripheral targets per tree, how often "Euclidean-greedy" branch choice (pick the child pointing most toward the target) fails vs hyperbolic-greedy *at the demo's R and corridor lengths*. That gives the Two Compasses headline number [U, not yet run].
  - Pre-segment and QA the case-card set (≈20 cases) and precompute Breath Room tubes, ITV tables and sky volumes for each.
  - Optional: MAISI synthetic CT variants (needs an 80 GB-class GPU; NVIDIA Open Model License; check the model card for use terms) [O].

#### 4.3.5 Lung Mission: metrics, acceptance, risks

- **Metrics:**
  - forks taken and wrong turns;
  - route length vs optimal;
  - time;
  - endoluminal-only rehearsal score;
  - gating arc and ITV reduction;
  - chosen needle clearance vs best robust window;
  - pointing probe errors (geodesic vs Euclidean prediction);
  - SEQ (1–7).
- **Acceptance:**
  - Four forks (including the Two Compasses fork) in ≤75 s by a naive rehearser, with no rescue in ≥90% of runs.
  - Roll a Lung builds a new case in ≤10 s on the PC; the default case loads from cache in ≤2 s.
  - Breath Room tube and ITV numbers correct at 60 fps on phone B.
  - The sky's current phase updates in <100 ms and all 10 phases in ≤300 ms after a target drag.
  - The plan card validates against `atlas.plancard/1` and prints with real numbers.
- **Risks.**
  - Commercial planners (Ion PlanPoint, Monarch) already auto-plan routes, so pitch this as training, comprehension and plan review.
  - CT segmentations stop several generations short of the lesion: Roll a Lung completes them with a published model and labels them "model-generated" (never presented as patient anatomy).
  - Hyperbolic navigation isn't learnable by everyone; provide beacons, breadcrumbs, minimap and the Euclidean home.
  - Keep the anatomy "glass"-styled (no gore).

### 4.4 Shared House (branched cover; + House Call)

**The model: a walkable Riemann surface [D]**
- Each household's scanned room is a **sheet**.
- A free-standing doorway is a **branch cut**, and its two posts are **branch points**. Crossing the opening forward applies a permutation σ to your sheet index.
- So:
  - walk *through* the door: you're in the next home;
  - walk *around* the whole frame: you stay home;
  - walk around *one* post: you change homes.
- **Riemann–Hurwitz:** χ = N − Σ_posts (N − #cycles(σ_post)).
  - K two-home doors connecting N homes: χ = N − 2K. For a connected door graph, the surface has N boundary circles (one wall loop per home), so χ = 2 − 2g − N gives genus g = K − N + 1, the cycle rank of the door graph.
  - A revolving N-cycle door gives a sphere with N holes.
- **Curvature:** the house is flat except at the doorposts. A post where k sheets meet has cone angle 2πk, so it carries **2π − 2πk curvature (−2π for a two-home door)**. Gauss–Bonnet checks: 2πN − 4πK = 2πχ ✓.
- **Personal embedding:**
  - shared state = {sheet id, pose in sheet coordinates};
  - each user's physical free space maps into a visited sheet by a rigid motion chosen to maximise overlap and align an anchor table (so both people touch "the same" table);
  - only the permutation graph is shared; the door's location in your own room is personal.

**Why flat fails**
- Flat telepresence must use the *intersection* of rooms (13.25% / 0% placement success at 4 / 6 clients, Choi 2024) [M], or one merged "union" room (MRUnion, TVCG 2025, the closest prior art; read it before claiming novelty).
- A flat floor plan holds at most 4 mutually adjacent rooms; a torus holds 7 (Szilassi map).

**Live-path version (solo, network-free).** Post Circling on pre-scanned sheets: the judge's room and one teammate's room scanned before the event, with a recorded teammate avatar labelled "recorded". Two homes only: through the door goes to the teammate's room; round one post comes back home (σ is a transposition). The live two-person House Call is the judge's-choice option and needs a 4th team member.

**Tasks**
- Knock (B2 Curtain Peek: lean through the doorway to see the other sheet before committing), visit, sit at the aligned table.
- **B3 Hand-over Through the Cut:** the object lands on the other person's real table.
- **B1 Post Circling.**
- **House Call:** the teammate (or clinician) walks the judge's scanned room, pins a trip hazard on the real rug, and asks for five sit-to-stands. Head height from ARKit times it.
- Optional space-time trail of the walk (4t).

**Tools**
- Partner device: the **Galaxy S24** running a minimal Android partner client (ARCore pose, avatar, voice; built in Phase 3, not the full Android port), with marker-based alignment: a printed floor poster relayed via the PC. The partner's room must be on the same LAN (one access point may not reach another floor; test it). ARKit collaboration is iPhone-only; Cloud Anchors need internet; Niantic Shared AR shut down May 2026 [O].
- **LiveKit** server (Apache-2.0) on the PC + LiveKit Unity SDK (iOS/Android).
- **UniVRM** (MIT) avatars + **uLipSync** (MIT).
- Steam Audio (Apache-2.0) or geodesic-distance attenuation; sound propagates only through open doorways (privacy by topology).

**Metrics:** alignment residual (cm); latency; social presence (later); chair-stand time; hazards pinned.

**Acceptance**
- The door, the post and the hand-over work with two phones on the team's LAN.
- Recorded-avatar fallback if the second person is absent.
- Visible "recorded" label.

**Risks**
- Privacy: share the parametric RoomPlan by default, not textures; doors as consent; motion is a biometric (94% re-identification from 100 s) [M].
- Network latency: buffer 60–100 ms.
- Cross-device alignment error of several cm: use snap zones, not precise shared touch.

**Covering-space co-walking (Tier B "Walk Together") [D]**
- Make the shared world a covering space of a common abstract room, so physical position is a function of virtual position and nobody needs synchronised resets (unlike Xu et al. TVCG 2024, which pauses everyone when one user resets).
- One column gives a spiral ramp; two columns give a free-group tree (485 places within 5 laps).
- Holonomy's {4,5} mapping can't be shared this way; a {4,8} order-2 branched mapping can.

### 4.5 FieldFit (sphere of view directions; + Scotoma River)

**The theorem that makes the geometry essential [D from a standard theorem]**
- **The bound.** In stereographic coordinates s = 2·tan(e/2), any *conformal* lens fitting a field of half-width E into a residual field of radius ρ₀ (fixing fixation) has central magnification ≤ **tan(ρ₀/2)/tan(E/2)** (Schwarz lemma).
- **What that means:**
  - E = 54° (ultra-wide), ρ₀ = 10°: centre must be minified ≥5.8×;
  - E = 37° (wide camera): ≥3.8×;
  - "sharp centre *and* see everything" therefore forces a **quasiconformal** lens.
- **Best radial lens (minimax distortion K\*):**
  - σ = s for s ≤ s₁; σ = s₁·(s/s₁)^{1/K\*} beyond;
  - K\* = ln(S/s₁) / ln(σ₀/s₁), where S = 2·tan(E/2), σ₀ = 2·tan(ρ₀/2), s₁ = 2·tan(e₁/2) for a 1:1 core of radius e₁;
  - K\* ≈ 1.6–4.7 across realistic settings.
- **Irregular fields:** a **Teichmüller extremal map** (Lui, Lam, Yau & Gu; "within 10 s" in their implementation).
- **Avoid the "Poincaré/tanh" lens profile:** R = ρ₀·tanh(e/ρ₀) has distortion K ≈ 34 at 30° and >2000 at 54°, turning peripheral objects into slivers. **Show it only as the cautionary example.**

**Tasks**
1. **S5 Blind-Spot Finder:** one eye, fixation cross, tap when a dot appears. The blind spot is found first and re-probed (Heijl–Krakau catch trials), which checks fixation **without eye tracking**. Smartphone-VR perimetry using this method: r = 0.808 vs Humphrey [M].
2. **Struggle:** a head-locked field loss (10° tunnel, hemianopia, or a real glaucomatous field from **UWHVF**: 28,943 Humphrey fields, BSD-3); search for 5 AI-scanned objects in the room.
3. **Fit and see:** **S6 Lens Slider** morphs uniform minification → power-law lens → Teichmüller lens; **S3 Pinch-the-Sky** sets the scale. Repeat the search: time and head-turn amplitude are logged.
4. **Inspect:** the spectator screen shows the distortion heatmap (K per region) and the Schwarz bound.
5. **Scotoma River (S7):** text parts round the blind spot along ideal-flow streamlines (Joukowski map w = z + a²/z; letters directly above shrink to 0.5×). It is compared A/B with Gupta's size-preserving Gaussian bump (+34–38% reading speed on simulated scotomas on a low-latency desktop monitor; head-mounted results were confounded by latency and foveal peeking) [M]. It needs no camera and runs on the judge's *own* physiological blind spot.

**Camera modes (constraint: ARKit locks out the ultra-wide)**
- **The live demo uses Lens-6DoF only.** The demo field is a circular tunnel (where the power-law lens is optimal within its class); real glaucoma fields use precomputed Teichmüller LUTs and are shown as an option.
- **Lens-standing (research mode, seated):** pause ARKit; AVCaptureSession on the ultra-wide (≈108° horizontal) with 3DoF from Core Motion. ARKit is paused, so LiDAR safety is off: allowed only seated, with a spotter and a Core Motion translation abort (accelerometer step detection exits the mode).
- **Lens-6DoF:** keep ARKit; wide camera (≈72–74°) for the centre; LiDAR room scan rendered for directions beyond the camera FOV (static, so flag it).

**Shader**
- One warp lookup texture per frame: camera undistortion ∘ therapeutic map φ ∘ headset pre-distortion.
- Inverse mapping per output pixel: s = σ for σ ≤ s₁, else s₁·(σ/s₁)^{K\*}; e = 2·atan(s/2).
- Irregular fields: Teichmüller LUT (RG16F, 512²).

**H200 (optional)**
- Fit Teichmüller lenses for all UWHVF fields: a population "lens atlas" of K distributions by defect type.
- Train a small field→LUT network for instant on-device fitting.
- In-silico hazard-visibility test with synthetic walkers in scanned rooms.

**Safety rules**
- Lens modes are standing or seated.
- No walking under a remapped view until a Doyon-style collision-judgement test is done (optical prisms shift the focus of expansion and can make a collision-course pedestrian look safe [M]).
- Persistent "LENS ON" indicator; one-tap off.

**Limits**
- Display acuity ≈14–17 px/deg ≈ 20/70–20/86, so patients with good central acuity would lose acuity. Present it as a research prototype.
- Camera-to-photon latency on iPhone is unmeasured: measure it, and use rotational re-projection.

**Acceptance**
- Blind spot found in ≤30 s on ≥80% of judges.
- Lens LUT applied in <1 ms per frame.
- Search time improves visibly with the lens on.

### 4.6 Tier B and C blades (short specs; details in the dossier)

| Blade | Core mechanic | Data / tools | Early version | Dossier file |
|---|---|---|---|---|
| **Station Zero** | P1 Rotating Hatch: charts glued by SO(3) transition rotations; the local floor stays the physical floor while "up" changes; egress under alarm; P2 pointing through the hull; after-action review in the true station frame (loop product = identity) | Procedural modules sized from the room scan; the scanned extinguisher as a grab prop; Android crewmate | Node + 2 modules + egress + one pointing trial + exploded review | `training_citizen_science.md` Card 1 |
| **The Branch** | H² design tree: doors = decisions; H200 generates the next variants (TRELLIS.2 objects, rooms); the scanned object is the seed; the scanned room is a measurable constraint ("must fit under the real shelf"); cairns as bookmarks | TRELLIS.2 (MIT, ≥24 GB, H200), gltfpack; precomputed fallback tree | A 3-level tree of 5 variants each, pre-generated; live generation only if H200 interactive | `science_data_ai_spaces.md` Card A |
| **Deep Time** | Camera identifies a plant or pet (BioCLIP 2, MIT; top-5) → you land on its leaf of Open Tree of Life v16.1 (2,385,875 tips, CC0); walk inward to the common ancestor of two scanned organisms | BioCLIP 2 on the 5070; OToL synthetic tree; Möbius re-centring, depth rings | One clade (e.g. mammals) | Card B |
| **Small Planet** | S²×E planet at R ≈ 0.5–3 m; great-circle walking; S4 Three-Right-Angle Walk; pendulum holonomy (Foucault); perturb-and-rerun an ensemble | Earth2Studio (Apache-2.0) with FourCastNet3 (Apache-2.0) / Pangu (CC BY-NC-SA) / GraphCast; ERA5/GFS; IBTrACS; precomputed playback | One ERA5 day + wind particles + holonomy pendulum | `wildcard_grand_concepts.md` Card 4 |
| **Manifold Lab** | Walk the weekday/month circles (Engels et al.) and number helix (Kantamneni & Tegmark) of an open LLM; your position patches the residual stream; the model speaks | Qwen3-8B (Apache-2.0) or Llama-3.1-8B; nnsight / TransformerLens (MIT); precomputed 360-angle fallback | Weekday ring with live steering + TTS + flat PCA twin | Card 2 |
| **C-Space Room** | Floor = joint torus of an arm; C-obstacles from the LiDAR scan; walk to teleoperate; wrap-around paths | SO-101 arm (≈$130–240; verify joint ranges permit wrap), LeRobot, MuJoCo | Sim arm + T² floor + one scanned box | Card 3 |
| **Cortex Planet** | fsaverage pial → inflated → sphere morph; walk HCP-MMP1 areas; spin test | FreeSurfer fsaverage (licence permits redistribution with notice); HCP-MMP terms to check | Extrinsic planet R ≈ 5–8 m | `medical_imaging_spaces.md` Card E |
| **Territory Surgeon** | Same H² tree engine on portal-vein or pulmonary vessels; cut → territory lights up; remnant % | MSD Task08 (CC BY-SA), TotalSegmentator `liver_vessels` | One MSD case | Card D |
| **Geodesic Soundscape** | Audio beacon = Riemannian log map of the walkable domain (fast marching, \|∇T\| = 1); distance tanh-compressed into 2–3 m; "two ways" at the cut locus | RoomPlan, LiDAR, AVAudioEngine HRTF; Soundscape (MIT) and Clew patterns | One room, 3–5 landmarks, eyes-closed walk round a foam partition | `therapy_assistive_remaps.md` Card 3 |
| **Wormhole Quest** | 10-min citizen-science game; shortcut, pointing and homing probes; graph-reliance index; consented open data | Same engine; flat-mode Android clients | Logging layer shipped in every Tier A blade | `training_citizen_science.md` Card 3 |
| **Folded Building** | Pre-scanned venue folded into the booth (impossible overlap) for route rehearsal in smoke; reroute when a stair is blocked | RoomPlan multi-room (StructureBuilder) | 20 s "Earth mission" add-on to Station Zero | Card 2 |
| **Unit-Cell Room (full)** | Crystal forge: CALF-20, live Orb-v3 MD (216 steps/s for 1,000 atoms on an H200 [O]), MatterGen variants, space-group orbifold mode | orb-models (Apache-2.0), ASE, pymatgen, spglib | Station inside the Glued Room | `wildcard_grand_concepts.md` Card 1 |
| **Heartbeat Torus** | Time on a torus: walking east = breathing phase, north = cardiac phase (Glued Room T² engine); tumour and heart move together in a two-dimensional loop of time | Composite: 4D-Lung breathing + ACDC cardiac cine MRI (150 patients, 3D + time; cite Bernard et al. 2018), labelled "composite" | One composite case, static meshes | — |
| **Penrose Loop** | Escher's ever-rising stair as a spiral staircase glued to itself (a helix quotiented by a screw motion): each flight is a chart and the top is glued to the bottom | Kit stairs + portals | Seated or handheld view first (a flat real floor under a virtual ramp risks sickness) | — |

---

## 5. System architecture

### 5.1 Overview

```mermaid
flowchart LR
  subgraph PHONE["iPhone 15 Pro Max in headset (all rendering + all safety)"]
    AR[ARKit 6DoF pose<br/>LiDAR depth/mesh<br/>image-tracked cube] --> SAFE[Safety monitor<br/>(every frame)]
    VIS[Vision hand pose<br/>+ LiDAR lift, 30 Hz] --> INT[Interaction layer<br/>(techniques + twins)]
    PAD[Gamepad / lever / dwell] --> INT
    SAFE --> LOC[Locomotion & topology<br/>(portals, gluing, sheets,<br/>tile embedding, resets)]
    INT --> KER[Geometry kernel<br/>exp/log/transport/images/slice/warp]
    LOC --> KER
    KER --> REN[Stereo renderer 60 fps<br/>stencil portals · Klein shader<br/>4D slicer · warp LUT · sky compute]
    SAFE --> REN
    LOG[Session logger + Knife-Map state 10 Hz]
  end
  subgraph PC["Windows PC · RTX 5070 (LAN server)"]
    RT[Realtime proc: FastAPI WS + UDP pose<br/>clock sync · planner look-ahead · logging]
    GPU[GPU workers: SPAR3D · TotalSegmentator<br/>BioCLIP 2 · gltfpack/KTX2]
    SPEC[Spectator renderer + Knife Map dashboard<br/>operator console (web)]
  end
  subgraph MAC["MacBook M3 Max"]
    OC[Object Capture reconstruction<br/>Xcode/iOS builds · AirPlay fallback]
  end
  subgraph HPC["H200 cluster (batch / optional interactive)"]
    B1[TRELLIS.2 asset kit · CT collections<br/>greedy-routing study · lens atlas<br/>Orb-v3 / Earth2Studio / LLM steering]
  end
  PHONE <-- "WS control · UDP pose · HTTP assets" --> PC
  MAC -- USDZ/GLB --> PC
  HPC -- cached GLB/JSON/LUT --> PC
  PC -- assets prefetch --> PHONE
```

### 5.2 Architecture principles: what the phone decides and what the PC may suggest

Grounded in the redirected-walking literature (dossier → `rdw_procgen_pathfinding.md` §9).

**Foundations**
- World as a graph G = (V, E) of reusable modules.
- Phone does tracking, safety, portals, room assembly and rendering.
- PC does logging and analytics.
- High-level JSON commands.
- Several virtual rooms reuse the same physical floor (impossible spaces).

**Principles**
1. **The phone controller is primary, not a fallback.** No per-frame decision ever waits on the network: latency spikes and jitter cause sickness [M].
2. **Restrict the PC's action space to discrete decisions taken while doors are closed:** which module comes next and where it goes physically. The PC never adjusts curvature gains. In 3×3 m the headroom for continuous gains is tiny anyway (gain-only RDW needs ≈6×6 m [M]).
3. **Precompute safety/continuation tables per module × physical tile pose** (Xu et al. pose-score precomputation beats S2C, APF and RL, and does relatively better in smaller spaces [M]). Ship them to the phone so the local planner is nearly as good as any PC planner.
4. **Predict door choices, not trajectories** (a categorical next-exit model from head yaw, walking direction, head-ray hits on doors and task state). An LSTM beats linear extrapolation for continuous prediction (65.7 vs 92.5 cm error at 2.5 s [M]), but in a door world the useful prediction is discrete.
5. **Use OpenRDW (Apache-2.0) on the PC only as an offline tuning simulator,** not at runtime.
6. **Store every object in room-local coordinates.** Recompute the physical embedding per visit.
7. **MPC:** not worth it for continuous gains in 3×3–4×4 m. A beam or MCTS look-ahead over the next 1–3 rooms is worth having, and cheap enough to run on the phone. The PC version is an optional "edge-assisted" upgrade.

### 5.3 Phone frame loop (order matters)

1. **Poll inputs:**
   - ARFrame: pose, sceneDepth, tracked images, tracking state;
   - gamepad;
   - latest Vision result (async, ≤30 Hz).
2. **Safety:**
   - LiDAR forward-cone sector minima;
   - mesh proximity;
   - person detection (async 5–10 Hz);
   - tracking-quality check.
   - Escalate if needed (§7.9). Safety may freeze locomotion transforms and force passthrough.
3. **Locomotion and topology:**
   - detect portal, glue or cut crossings and apply chart transitions;
   - apply clamped gains (if any);
   - run the tile embedding for the next room when its door is closed.
4. **Interaction:** active techniques read inputs (with One-Euro filters) and call kernel primitives.
5. **Simulation:** blade logic (e.g. phase from the breathing ring, sky recompute dispatch).
6. **Render (one camera, both eyes; D5):**
   - one cull with a frustum enclosing both eyes;
   - per eye into its half of the side-by-side target: home chart, stencil portals depth ≤2 (RendererLists), curved geometry via the Klein include, overlays (safety, reticle, caption chip);
   - final pass at panel resolution: distortion, chromatic aberration, passthrough composite, LUT, late rotational warp with the latest predicted pose (D16).
7. **Log:** pose 30–60 Hz, events, Knife-Map state at 10 Hz. Never block the frame on I/O; use a ring buffer and a background flush.

### 5.4 Networking (from dossier → `phone_pc_link.md`)

**Channels**
- **Control (reliable):** WebSocket over TCP. Python: FastAPI/Starlette or `websockets`. Unity: `System.Net.WebSockets.ClientWebSocket`.
- **Pose / telemetry (latest-wins):** raw UDP at 30–60 Hz, each datagram self-contained (seq, phone timestamp). Over USB tunnels (TCP only), fall back to pose over WebSocket with TCP_NODELAY.
- **Assets:** HTTP GET by content hash, triggered by `AssetReady`, so big transfers never block commands.
- **Serialisation:** MessagePack with **string-keyed maps** (MessagePack-CSharp with IL2CPP AOT; Python `msgpack`). One schema file (`specs/messages.yaml`) generates the C# and Python types; every message carries `proto` major/minor; golden round-trip tests in CI. Appendix B is the reference.

**Clock sync.** At connect, run 8–16 ping/pong exchanges and keep the minimum-RTT sample:
- offset = ((t2 − t1) + (t3 − t4))/2
- RTT = (t4 − t1) − (t3 − t2)

Re-sync at 1 Hz, low-passed. Every PC command carries `valid_until` in *phone* time.

**Watchdog (phone)**
- **CONNECTED:** heartbeat within 500 ms.
- **DEGRADED:** 500 ms–3 s, entered after 2 consecutive misses and left after 2 s healthy (hysteresis; Wi-Fi spikes of tens of ms are normal). Ignore stale plans, ease gains to identity, freeze world edits.
- **OFFLINE:** over 3 s. Local planner and cached world; subtle indicator; exponential-backoff reconnect.
- **RESYNC:** clock sync again plus a world-version delta.

**Transport**
- **Development:**
  - Android: `adb reverse tcp:8765 tcp:8765`.
  - iOS: the app *listens* on TCP and the PC runs `iproxy 8765:8765` (libusbmuxd), which avoids the Local Network prompt for incoming TCP.
- **Runtime:** own access point, PC wired to it, one wireless hop. Fixed 5 GHz non-DFS channel at 20/40 MHz (the iPhone 14 Pro Max is Wi-Fi 6, no 6 GHz); AirDrop and Handoff off on every device (AWDL causes latency spikes); measure with spike 0.21.
- **iOS:**
  - add `NSLocalNetworkUsageDescription`;
  - discovery by QR code or Bonjour (Bonjour also needs `NSBonjourServices` in Info.plist), never broadcast or multicast (that needs an Apple entitlement);
  - the first connection may fail while the permission prompt is up, so retry.
- **Android:** hold `WIFI_MODE_FULL_LOW_LATENCY`.

**Message set** (examples in Appendix B):
- `Hello` / `Welcome`
- `Ping` / `Pong`
- `Pose` (UDP)
- `Event`
- `KnifeState` (10 Hz)
- `PlanUpdate {plan_id, based_on_seq, valid_until, next_module, embedding, confidence}`
- `WorldDelta`
- `AssetReady {asset_id, url, sha256, bytes, format, lod, bounds}` / `AssetAck`
- `OperatorCmd {blade, reset, recenter, ipd_nudge, thermal_tier, fallback}`
- `Heartbeat`
- `Error`
- `Bye`

**Assets**
- GLB with meshopt (or Draco) geometry and KTX2/Basis textures (UASTC for normals, ETC1S for albedo).
- Loaded with **glTFast** (Apache-2.0) plus `com.unity.meshopt.decompress` and `com.unity.cloud.ktx`.
- Budget: 1–5 MB per chunk and ≤50k triangles per LOD0 chunk.
- Prefetch rooms behind likely doors. Never instantiate on the render thread without frame-time budgeting.
- **Gaussian splats:** avoid on the phone. The main Unity renderer (aras-p) is unmaintained and fails on iOS [O]. Convert to meshes on the PC, or use them only as PC-rendered backdrops.

### 5.5 Operator console and spectator view

**Operator console** (web app on the PC; reachable from a tablet by QR code)
- Blade buttons with a preloaded "next" queue.
- **Panic → passthrough.**
- Re-anchor ("judge on X → set origin"); recentre yaw.
- Comfort branch (standard / gentle).
- Software IPD nudge.
- Thermal tier.
- Live tiles: fps p50 and 1%-low, thermal state, tracking state, RTT, AI-service status, battery for phones A and B, hand-tracking confidence.
- Rating entry: FMS pre/mid/post, SEQ, notes.
- Fallback triggers: "play video for blade X", "use cached assets" (default ON during judging).
- Session timer and run-of-show progress bar.

**Spectator view**
- **Preferred:** a Windows build of the same Unity project (built in Phase 1) re-renders the scene from **replicated discrete state** (chart id, sheet, cell, chart-local transforms, blade state) at 10–30 Hz plus the pose stream. It is not a bit-exact replay (ARM and x64 floats differ); state replication is what keeps it in step.
- **Fallback 1:** AirPlay mirroring to the Mac (macOS 12+; crop one eye of the side-by-side image).
- **Fallback 2:** a non-interactive external-display scene from the phone (iOS 27 requires a `UISceneAccessory` registration [O]).

**"Knife Map" spectator screen** (16:9, beside the demo area)

| Panel | What it shows |
|---|---|
| First-person mirror | The judge's view, with the live technique code as a chip, e.g. `[H · hand · exp · world · select]` |
| Geometry × Body matrix | The live cell pulses; used cells fill in the blade's colour; empty cells are dotted ("open research") |
| Taxonomy ribbon | Six parallel axes; the live code is a bright polyline over faded history |
| Live equation card | The formula filled with real numbers (e.g. θ = \|K\|·A = 0.93 rad, measured 0.89) |
| Chart strip | The geometry's native flat model of the judge's path: Poincaré disk, fundamental cell, sheet graph, slice film strip, true-frame station. This doubles as the flat twin |

Glyph encoding: glyph shape = mapping type, colour = task, ring style = perceiver.

**Live vs reveal.** During the session the screen shows only the first-person mirror, the chart strip and one equation card (booth-readable); the Geometry × Body matrix and the taxonomy ribbon appear at the dashboard reveal and in the docs. Show the flat twin (minimap) *after* the "aha": minimaps can distract from noticing non-Euclidean layouts (Auda et al., CHI 2022 [R]).

- The phone sends `KnifeState` at 10 Hz: `{t, blade, technique_id, code[6], params, equation{template, values}, chart{model, x, y, path}, sheet, wraps, fps, hand_conf}`.
- A "coverage card" image is exported per judge as a souvenir and for the docs.

---

## 6. Platform and stack decisions

### 6.1 Phones

The core demo runs on **one** phone. The other devices add resilience and features.

| | Phone A: iPhone 15 Pro Max | Phone B: iPhone 14 Pro Max | Galaxy S24 / S24 Ultra |
|---|---|---|---|
| Role | In the headset for every judge | Hot-swap, backup, live object scanning, second judge, second dev device | Shared House partner; Android build |
| Chip / RAM | A17 Pro (6-core GPU) / 8 GB | A16 (5-core GPU) / 6 GB | Snapdragon 8 Gen 3 for Galaxy (S24 in US/Canada/China; S24 Ultra everywhere) or Exynos 2400 (S24 elsewhere) / 8 GB (S24), 12 GB (Ultra) |
| Display | 6.7", 2796×1290, 460 ppi, 120 Hz (we lock to 60) | same | 6.2" 2340×1080 (S24); 6.8" 3120×1440 (Ultra) |
| Depth | LiDAR | LiDAR | none (ARCore depth-from-motion, best 0.5–5 m) |
| RoomPlan / Object Capture | yes / yes | yes / yes (LiDAR + A14 or later) | no / no |
| Port, weight | USB-C (USB 3), 221 g | Lightning (USB 2), 240 g | USB-C |
| OS | iOS 26 and iOS 27 | iOS 26 and iOS 27 | Android; ARCore (confirm the model on Google's supported-devices list) |

Sources: Apple tech specs ([15 Pro Max](https://support.apple.com/en-us/111828), [14 Pro Max](https://support.apple.com/en-us/111846)); [GSMArena S24](https://www.gsmarena.com/samsung_galaxy_s24-12773.php) / [S24 Ultra](https://www.gsmarena.com/samsung_galaxy_s24_ultra-12771.php); [SamMobile chip regions](https://www.sammobile.com/news/galaxy-s24-series-exynos-snapdragon-countries/); [MacRumors on iOS 27 support](https://macrumors.com/2026/09/09/apple-announces-ios-27-release-date/); [Notebookcheck 15 vs 14 Pro Max GPU stress](https://www.notebookcheck.com/Apple-iPhone-15-Pro-Max-drosselt-in-unserem-GPU-Stresstest-bis-zu-40-ist-trotzdem-schneller-als-iPhone-14-Pro-Max.756382.0.html). There is no "iPhone 14 Max" or "14 Ultra"; the **iPhone 14 Plus has no LiDAR**, so confirm phone B is the 14 *Pro* Max.

**What phone B is for, in priority order**
1. **Heat swap.** Phones throttle to about 50% of peak GPU after 40 minutes of stress, worse inside a closed headset. Swap between judges by the single rule in §7.12 (thermal state "fair" or 30 min); in-session, drop a tier.
2. **Backup.** Crash, tracking failure, battery or a drop: back in seconds.
3. **Not live object scanning.** Object Capture heats the phone; the judge's object is photographed on the Galaxy S24 and meshed by SPAR3D on the PC instead, so phone B stays cool.
4. **Second judge** on a short blade (the 45 s classic Glued Room) while judge 1 is at the dashboard, only when phone B is cool.
5. **Second development device.** The House Call partner is the Galaxy S24, not phone B.

**Rules for mixing A and B**
- Lock both phones to the **same quality tier, set by the slower phone (14 Pro Max)**, so every judge gets the same experience. Re-run spikes 0.1 and 0.8 on phone B.
- Each phone needs its own Cardboard viewer profile check: the camera plateaus differ slightly, so confirm the headset's camera window clears the wide camera and LiDAR on **both**.
- Phone B uses Lightning: pack a Lightning cable; `iproxy` over USB works the same way.

LiDAR-only features we use (both iPhones):
- `sceneDepth` / `smoothedSceneDepth` (256×192, best at 0.3–2 m; real update rate ≈15 Hz measured on a 13 Pro [M]);
- scene-reconstruction mesh with classification;
- **RoomPlan** (parametric walls, doors, windows, openings and furniture → USDZ; StructureBuilder for multi-room);
- **Object Capture** (`ObjectCaptureSession` iOS 17+; Apple's sample targets iOS 18; needs LiDAR and an A14 or later).

Thermals:
- 15 Pro Max, 3DMark stress: ≈70% stability; settles at about **50% of peak GPU after 40 min** [O]. The 14 Pro Max stays 10–26% behind it under the same GPU stress loop [O].
- Plan thermal tiers and swap phones between judges.

Developer friction:
- **Signing (D19):** a free Apple account in Xcode (Personal Team) signs and installs the builds. They stop launching 7 days after install, which is longer than our rebuild cycle; install ≤24 h before an event. The paid Apple Developer Program (US$99/yr) is the fallback (§10.5).
- Guided Access, Do Not Disturb and `isIdleTimerDisabled` on demo day.

**Android: Galaxy S24 / S24 Ultra (secondary)**
- No rear ToF/LiDAR. ARCore depth-from-motion (best 0.5–5 m) means weaker collision warnings and no RoomPlan or Object Capture equivalent. For scanning, do server-side reconstruction from video instead (MapAnything-Apache / Depth Anything 3 Base → SpatialLM 1.1 → kit rebuild).
- Use it for the Shared House second participant and the cross-platform build; hands via MediaPipe.
- **Cardboard SDK:** v1.33.0 (Mar 2026) listed "Black artifacts might appear in VR mode on Samsung S25 and S24 devices". v1.34.0 and v1.35.0 list no known issues but give no fix note ([releases](https://github.com/googlevr/cardboard/releases)). Test on the device; our Option A stereo path uses only the SDK's optics, which may avoid it.
- The S24 (6.2") is small for most phone headsets; the S24 Ultra (6.8") fits better.

**Huawei**
- Current flagships (Mate 80, HarmonyOS 6, China market) cannot run ARCore, have only a front ToF, and Unity 6 cannot build for HarmonyOS NEXT; only Unity China's Tuanjie can, with no documented XR.
- **Not a target:** the challenge document sets no hardware requirement; confirm with the organisers.
- Nice touch for the docs: Small Planet can run Huawei's Pangu-Weather via Earth2Studio.

### 6.2 Headset requirements (buy two)

**MUST**
- A camera window leaving the wide camera **and LiDAR** unobstructed. The iPhone 15 Pro Max camera plateau is large and top-left in portrait, which ends up at one side in landscape.
- Fits a 6.7" phone, with the camera window clearing **both** iPhones' camera plateaus (15 Pro Max and 14 Pro Max).
- Head strap for walking.
- Ventilation; no case on the phone.

**SHOULD**
- Fits over glasses, or per-lens focus.
- IPD adjustment.
- Wipeable or disposable face cushion.

**Candidates**
- **Zapbox headset shell.** Designed around iPhone camera passthrough: 100° FOV, glasses-friendly per its FAQ. **Its controllers cannot run inside our ARKit app**, because Zapbox uses its own ultra-wide tracking; use the shell only and verify the 15 Pro Max fit.
- **Merge-style "AR-ready" headsets** with a camera window.
- A cut Cardboard v2 for development.

**Generate a Cardboard viewer-profile QR** (Google's Viewer Profile Generator) for the exact lenses and screen-to-lens distance, so distortion is correct.

### 6.3 Engine and rendering stack

**Unity 6 LTS (6000.x), URP, IL2CPP, Metal**
- **AR Foundation 6.x + Apple ARKit XR Plugin 6.x:** pose, depth, meshing, image tracking.
- **Stereo: one camera, two eyes (D5).** Unity offers single-pass instanced stereo only through XR display providers on Android multiview, PSVR and Windows ([Unity](https://docs.unity3d.com/Manual/SinglePassStereoRendering.html)), so on iOS we build it ourselves:
  - one URP camera; a render-graph pass renders the scene twice into the two halves of one side-by-side target (two viewports, shared culling with a frustum enclosing both eyes);
  - portal views are extra RendererLists from the room graph (no extra Camera objects), clipped at the portal plane in the shader rather than by an oblique projection;
  - per-eye projection and eye-from-head come from the Cardboard viewer profile;
  - a single **final pass** at panel resolution does lens distortion, chromatic-aberration correction, passthrough compositing (Y/CbCr), the FieldFit LUT, colour grading and the **late rotational warp** (D16);
  - fallback: two eye cameras; Option B (Cardboard XR plugin + native ARKit pose) is timeboxed to 1 day. Decide by spike 0.15.
- **Cardboard SDK as an optics library:**
  - Use the Cardboard SDK's C API (open source, iOS and Android, v1.35.0 Aug 2026) **as a library for optics only**: per-eye FOV and projection, eye-from-head, and the distortion mesh from the viewer-profile QR.
  - Do *not* use it for head tracking (rotation-only plus neck model). Verify the exact function names in `cardboard.h`.
- **Unity version:** pin the current Unity 6 LTS (6000.3 at the time of writing [verify]); don't depend on XR-only features such as on-tile post-processing. GPU Resident Drawer stays off (it needs Forward+). Foveated rendering isn't available on this iOS path (Unity's foveation covers OpenXR, Android XR and PolySpatial).
- **Frame rate:** `Application.targetFrameRate = 60`; never 120.
- **Native Swift plugins** (Unity iOS plugin, `@_cdecl` bridges):
  - `VisionHands`: Vision `VNDetectHumanHandPoseRequest` on `ARFrame.capturedImage` via `XRSessionSubsystem.nativePtr`, with LiDAR lift; run on the Neural Engine (`setComputeDevice`), latest-frame-only queue, release the pixel buffer at once.
  - `RoomPlanBridge`: pass AR Foundation's ARSession (`nativePtr`) to `RoomCaptureSession(arSession:)` (iOS 17+), stop with `pauseARSession: false`, then save the ARWorldMap from that same session; export `CapturedRoom` as JSON + USDZ (spike 0.18).
  - `ObjectCaptureBridge` (capture on the phone; reconstruct on the Mac).
  - `Thermal` (`ProcessInfo.thermalState`).
  - Core Haptics, via Apple's official Unity plugins (`apple/unityplugins`, Apache-2.0: GameController, CoreHaptics, PHASE).
- **CPU maths:** Burst + Jobs. Hyperbolic and spherical transforms are kept in **double precision on the CPU**, re-anchored to the nearest cell every frame, and sent to the GPU as float relative to the camera cell. This follows Hyperbolica's offline double-precision tile tables and Hypermine's `normalize_transform`.
- **Why not native Swift/Metal now:**
  - Unity gives editor tooling, AR Foundation, glTFast and Android.
  - The hot paths are GPU shaders and native plugins anyway.
  - Keep the kernel engine-agnostic (pure C# + HLSL with a written spec) so a Metal port is possible if profiling shows engine overhead.
- **Not chosen:**
  - **Godot 4.5+:** viable (stencil buffer, MobileVRInterface), but thinner Cardboard tooling.
  - **WebXR:** iOS Safari has WebXR disabled by default.
  - **Unreal:** no official Cardboard path.

### 6.4 Stack table (with licences)

| Layer | Component | Licence | Notes / link |
|---|---|---|---|
| AR | AR Foundation 6.x, ARKit XR Plugin 6.x | Unity package | [docs](https://docs.unity3d.com/Packages/com.unity.xr.arkit@6.3/manual/index.html) |
| Optics | Google Cardboard SDK (C API) / XR Plugin | Apache-2.0 (plugin); mixed (SDK) | [googlevr/cardboard](https://github.com/googlevr/cardboard), [cardboard-xr-plugin](https://github.com/googlevr/cardboard-xr-plugin) |
| Assets | glTFast + meshopt + KTX | Apache-2.0 | [glTFast](https://github.com/Unity-Technologies/com.unity.cloud.gltfast) |
| Serialisation | MessagePack-CSharp | MIT | [repo](https://github.com/MessagePack-CSharp/MessagePack-CSharp) |
| Apple plugins | GameController, CoreHaptics, PHASE | Apache-2.0 | [apple/unityplugins](https://github.com/apple/unityplugins) |
| Audio | Steam Audio 4.8 (iOS/Android) | Apache-2.0 | [ValveSoftware/steam-audio](https://github.com/ValveSoftware/steam-audio) (Resonance Audio is archived) |
| Hands (alt) | MediaPipe Hand Landmarker via homuler/MediaPipeUnityPlugin | MIT / Apache-2.0 | [repo](https://github.com/homuler/MediaPipeUnityPlugin) (Android route; iOS framework must be built) |
| Voice / multi-user | LiveKit server + Unity SDK | Apache-2.0 | [livekit](https://github.com/livekit/livekit), [client-sdk-unity](https://github.com/livekit/client-sdk-unity) |
| Avatars | UniVRM, uLipSync | MIT | [UniVRM](https://github.com/vrm-c/UniVRM), [uLipSync](https://github.com/hecomi/uLipSync) |
| Logging | Unity Experiment Framework (UXF) | MIT | [repo](https://github.com/immersivecognition/unity-experiment-framework) |
| Redirected walking (offline) | OpenRDW / OpenRDW2 | Apache-2.0 | [OpenRDW](https://github.com/yaoling1997/OpenRDW), [OpenRDW2](https://github.com/omegafantasy/OpenRDW2) (Unity 2019; port the logic) |
| Layout generation | Edgar (graph + room templates, 2D) | MIT | [Edgar-Unity](https://github.com/OndrejNepozitek/Edgar-Unity) |
| Server | FastAPI + uvicorn, asyncio UDP, msgpack; Ray Serve optional | MIT / BSD / Apache | — |
| Mesh tools | meshoptimizer / gltfpack, glTF-Transform, KTX-Software (toktx) | MIT / MIT / Apache-2.0 | [meshoptimizer](https://github.com/zeux/meshoptimizer) |
| Medical | TotalSegmentator; VMTK / Slicer-VMTK; pylidc; healpy (HEALPix) | Apache-2.0; BSD-2 / Apache-2.0; MIT; GPL-2.0 (healpy; *or* implement HEALPix indexing yourself) | [TotalSegmentator](https://github.com/wasserth/TotalSegmentator) |
| 3D generation | SPAR3D (fits 12 GB, ~7–10.5 GB; Stability AI Community License, free under $1M revenue) · Hunyuan3D-2mini/turbo (6 GB shape; licence excludes EU/UK/KR, Canada is fine) · TripoSG (MIT, 8 GB, geometry only) · **TRELLIS.2-4B** (MIT, ≥24 GB → H200) · SAM 3 / SAM 3D Objects (SAM licence, gated) | various | dossier → `scanning_generation_polish.md` |
| Fallback APIs | Tripo, Meshy, Rodin | commercial | ≈$0.30–0.40 per model [O, aggregator pricing] |
| Reference code | HackerPoet/HyperEngine (Hyperbolica backend; Unity; MIT; unmaintained) · SebLague/Portals (MIT) · HackerPoet/NonEuclidean (MIT) · Ralith/hypermine (Apache/Zlib) · zenorogue/hyperrogue (**GPL-2.0: read, don't copy**) · henryseg/non-euclidean_VR (GPL-3.0-or-later per README: cite, don't copy) · wxyhly/tesserxel (ISC, declared in package.json only) · aaronfranke/godot-4d (Unlicense) | — | dossier → `rendering_math_4d.md` §7 table |
| Tier B science | orb-models (Apache-2.0), MatterGen (MIT), Earth2Studio (Apache-2.0), FourCastNet3 (Apache-2.0), Pangu (CC BY-NC-SA), nnsight / TransformerLens (MIT), BioCLIP 2 (MIT), Open Tree of Life (CC0), UWHVF (BSD-3) | — | dossier → round-2 lane files |

### 6.5 Input stack and the 20-second gesture set

**Must-have**
- Real walking (ARKit 6DoF).
- Head-gaze reticle.
- **A = use / hold to carry, B = back, double-tap B (within 400 ms) = Safe Harbour**: a pre-paired Bluetooth pad (Xbox / 8BitDo / DualSense for gyro). Fallbacks: the headset lever's screen tap, then dwell on explicit targets (700 ms everywhere, 600 ms for ≥3° discs on the needle sky).
- **Voice is operator-side:** the narrator hears "home" or "exit" and presses the console button; no speech recognition runs on the phone (noisy hall).
- Operator console.
- Short Core Haptics pulses (20–50 ms) and spatial audio from the phone speakers.

**Showcase (every one has a twin)**
- **Vision hands:**
  - 21 joints, ≈19 ms inference on a 15 Pro [O]; plan 30 Hz;
  - lift with the median `sceneDepth` over wrist and knuckles (≥0.32 m), with hand-size depth as fallback;
  - One-Euro filter;
  - gestures in order of robustness: hand in zone (touch/push) > open palm vs fist (grab) > pinch (palm-normalised, hysteresis 0.25/0.40, 3-frame rule).
- **Marker cube:**
  - 8–10 cm faces, six matte high-contrast images, handle;
  - ARKit image tracking updates every frame with ≤4 images tracked at once [O], so `maximumNumberOfTrackedImages = 2–3` plus one floor marker;
  - freeze the pose when lost, never jump.
- **Real furniture as passive haptics** (X7 One Table, Many Worlds).
- **X12 Geometry Lens (demo hand hero):** both hands frame a rectangle at chin height; inside it the scanned room renders in H, S or a 4D slice; release to drop it. Detector: two open hands, thumb–index L-shapes facing each other, held 300 ms. Twin: trigger. Bench in spike 0.3.

**Avoid**
- Zapbox controllers (incompatible with ARKit).
- ARKit body tracking (it tracks *other* people).
- `.arobject` for handheld props (not updated after detection).
- Apple Watch or AirPods for judges (≈200 ms messaging, wrist-up rule, hygiene).
- Voice as the judge's primary input (noisy hall; use it for the operator).
- **ARKit `providesAudioData = true`:** it silently disables haptics [O].
- Long vibrations.
- Running at 120 fps.

**Script for judges (20 s):** *"Walk anywhere inside the tape. Look at something to highlight it. Press A to use it; hold A to carry it; B to go back. Hold the cube in front of your chin when we ask."*

---

## 7. Core technical designs (maths and algorithms)

### 7.1 World model: an atlas of charts

- **Chart (room/cell).** A locally Euclidean or locally curved region with its own coordinates, contents (in **chart-local** coordinates) and module type.
- **Gluing (edge).** A transform `T_e` from chart A's frame to chart B's frame, plus semantics (portal, wall gluing, branch cut with permutation σ, rotating hatch, time-ring seam):
  - a 4×4 affine matrix for Euclidean charts;
  - a Lorentz matrix SO⁺(3,1) for H³ (or the H²×E product: SO⁺(2,1) × ℝ);
  - an SO(4) matrix for S³.
  The same code path handles all of them. **This is the "one isometry-graph engine" story for the tech score.**
- **World** G = (V, E). It is persistent and virtual: identity, contents, door states.
- **Physical embedding** P_visit(v) ∈ SE(2), per visit, onto the play-area tile grid. It is transient and recomputed whenever a room is fully out of view (the change-blindness condition). **Never store anything in physical or world coordinates.**
- **Curved cells.** Positions are stored as **cell id + local coordinates** (HyperRogue/Hypermine style). Never accumulate a float world coordinate. Compose transforms only along the rendered chain (depth-limited).
- **The 4D view of overlapping rooms (docs framing).** Give each room a w-range; the user always occupies the slice w = w(t); doorways are w-ramps. The impossible-spaces world is then a 3-manifold embedded in ℝ⁴ over the physical floor, equivalently a covering or branched gluing of the room. A one-button "4D peek" that slides w₀ to ghost adjacent rooms through each other is a strong, honest demo moment [D].

### 7.2 Rendering

**Portals (P, T, B geometries).** Stencil-masked, re-rendering through the *same* stereo camera's per-eye view matrices multiplied by the portal transform, as extra RendererLists (no extra cameras). Per eye:
1. Draw the portal frame into stencil (ref = portal id, depth ≤2).
2. Draw the destination with view-projection × M_portal, stencil-tested, with clipping at the portal plane done in the shader (cheaper and stable in stereo; an oblique near-plane clip, Lengyel, is the alternative).
3. Clear depth inside the stencil and draw the portal "glass" depth.

Rules:
- **Teleport the rig when the head centre crosses**, but draw the portal as a **box**, so the eye that hasn't crossed still sees consistent content.
- Clone objects that straddle a portal, and transform their velocity by M.
- Transform lights through portals too.
- Measured: stencil is 2.3× faster on average (up to 3.97×) than render textures, and render textures break lighting behind portals [M] (Slotboom, TU Delft 2023).
- Glued Room copies use instanced deck translations rather than recursion.

**Curved space (H, S).** Store in the hyperboloid (or sphere) model and render through the **projective/Klein chart**. In the vertex shader: one 4×4 isometry, then the perspective divide (x/w, y/w, z/w), then the eye matrix. In the Klein chart straight edges stay straight, so no subdivision is needed for option (a) below. This is the approach of Jeff Weeks and Non-Euclidean VR I.

- **H²×E for anything walked:**
  - the floor stays the physical floor (NEVR II), avoiding the "floor falls out" problem of H³;
  - full H³ or S³ only for flying or seated rooms.
- **Camera update:**
  - C ← C · exp(translation(v·dt)) · R_head (this *is* parallel transport of the camera frame);
  - Gram–Schmidt re-orthonormalisation under the Minkowski product every few frames;
  - re-anchor to the nearest cell when leaving the home cell.
- **Stereo (chosen per wing by spike 0.7; D9):**
  - option (a) Klein per eye: straight edges, no subdivision, depth compressed (vergence ≈IPD/R);
  - option (b) **"equidistant" stereo**: map each vertex to its true direction and geodesic distance from the head centre (log map), then apply ordinary Euclidean stereo with the real IPD. Edges are then no longer straight, so meshes are **subdivided offline to ≲0.2R** (≈0.44 m at R = 2.2 m);
  - do **not** simulate true binocular rays. In H³, vergence at infinity ≈ IPD/R, so the whole world would look closer than about one curvature radius; in S³ the eyes would have to diverge beyond πR/2 [D] (HyperRogue `vr.cpp` exposes the same choice).
  - **R is chosen per blade** (smaller R = stronger effects, more disorientation). Guidance: ≈2–10 m for comfort-critical walked spaces.
    - Airway H² layout: set R from the branching, R = ℓ/ln b (b = 2, ℓ = 1.5 m → R ≈ 2.2 m) [D]. Locomotion is by fork chambers joined by portals, embedded on the tile grid (§7.4).
    - A Holonomy-style mapping of 1 m physical cells onto {4,5} squares implies R ≈ 0.8 m (edges matched) or ≈0.94 m (tile centres matched) [D]. Use it only for the Holonomy replication or The Branch, not comfort-critical blades.
  - **Coordinate convention:** internally use the *unit* hyperboloid/sphere (⟨p,p⟩ = −1 or |p| = 1) and scale lengths by R only at the metric boundary (metres). §3.2 writes R explicitly; code should not, to avoid factor-of-R bugs.
- **Growth, fog and light:**
  - ball volume in H³ ≈ (π/2)·R³·e^{2r/R}, so every extra R of view distance multiplies visible volume ≈7.4×;
  - **hard draw radius 3–4R + exponential fog in geodesic distance**, computed per vertex after the camera transform as d = R·atanh(|x′|/w′) (x′ the spatial part, w′ the time component on the unit hyperboloid), which is better conditioned near the camera than acosh(w′);
  - lights fall off as 1/sinh²(r/R) in H³ and 1/sin²(r/R) in S³ (light *refocuses* at the antipode, a visual proof of S³);
  - fog doubles as a depth cue that compensates for flattened stereo.
- **Cell lists:** precompute per "camera cell" by breadth-first search to depth k; LOD by graph distance; one draw call per module type (instancing).
- **S³:** the projective chart covers a hemisphere, so render two passes (w ≥ 0, w < 0) or use elliptic space (one pass).
- **Precision:** offline double-precision tile lookup tables (Hyperbolica) for tilings; per-frame re-anchoring.

**Approach Sky (S² compute).**
- A compute shader ray-marches from the target outward along 49,152 HEALPix directions through 3D distance-field textures (vessels, fissures, ribs).
- It writes a risk texture sampled on a 2–5k-triangle sphere.
- Robust windows are a bitwise AND across the 10 phase textures.

**4D slicer.**
- Objects are tetrahedral meshes in ℝ⁴. A compute shader intersects each tetrahedron with the hyperplane {x : n·x = w₀}, giving triangles or quads, appended to an indirect draw buffer.
- n comes from the cube's quaternion (§3.3) or a stick.
- **D5 Look-Down Shadow:** when head pitch < −35°, cross-fade to the 4D→3D projection on the floor (perspective (x, y, z)·f/(f − w), or orthographic along t for CT, which gives the ITV).
- Reference engines: Tesserxel (TypeScript/WebGPU, slicing and physics) and godot-4d. Write a minimal slicer yourself (≈200 lines of shader).

**Conformal warp (FieldFit).** One full-screen lookup-texture pass composing camera undistortion ∘ lens φ ∘ headset pre-distortion. Recompute the lookup only when parameters change.

**Passthrough and Safe Harbour.** The ARKit camera image (Y/CbCr) is composited in the final pass, undistorted with that frame's intrinsics and re-projected by the late warp together with the overlays; it is shown mono at a fixed virtual depth of 1.5–2 m (optionally offset per pixel from `smoothedSceneDepth`). It is never drawn into the MSAA eye buffers (no ARCameraBackground in VR scenes). **Safe Harbour** = passthrough + the hub's room outline and play-area ring as overlays, all gains = 1, within 300 ms of the trigger.

**Curvature Dial transitions.**
- κ-stereographic model: one formula family for K < 0, K = 0 and K > 0 via tan_κ / sin_κ [U].
- Rate-limit dK/dt and keep the floor fixed.
- Live readout: circumference at 1 m = 2πR·sinh(1/R) … 2π … 2πR·sin(1/R).

**Performance budgets (phone stereo, 60 fps)**

| Budget | Target |
|---|---|
| Visible scene | ≤150k triangles **per eye including portal views** |
| Draw calls | <100 per eye |
| Scanned or generated objects | 5–20k triangles each, one 1024² KTX2 albedo (+ ORM) |
| Full-screen passes | ≈1 ms each; **forward rendering, MSAA on, baked lighting, blob shadows, no HDR targets, no SSAO/DOF/bloom passes** (Meta mobile-VR guidance) |
| Post-processing | Unity 6.4 on-tile post (tonemap, grading, vignette, dither) or fold grading into the material shader; fake bloom with sprites |
| Render scale | Eye buffers 2 × ≈1216×1120 (≈0.87 of panel); tiers step 0.87 → 0.75 → 0.65 |
| MSAA | 4× memoryless at nominal, 2× at "serious"; verify in an Xcode GPU capture that MSAA and depth are never stored or reloaded |
| Lens mask | Hidden-area mask outside the lens circle (≈30–40% fewer fragments, headset-dependent) |

**Frame budget (16.67 ms at 60 fps; estimates to be replaced by spikes 0.15–0.17; GPU budget set at sustained, not peak, clocks)**

| Stage | Unit | A17 Pro (ms) | A16 (ms) |
|---|---|---|---|
| ARKit ingest, safety, kernel | main | 1.2 | 1.6 |
| Interaction and blade simulation | main | 1.0 | 1.3 |
| Room-graph culling, network, misc | main | 1.1 | 1.4 |
| **Main thread total** (cap 6 / 7) | | **3.3** | **4.3** |
| URP setup + ≈250 draws (SRP Batcher) | render | 2.5 | 3.2 |
| Home chart opaque + lens mask | GPU | 2.4 | 3.0 |
| Portals ≤2 (stencil) | GPU | 1.2 | 1.5 |
| Curved vertex shader, fog, overlays | GPU | 0.6 | 0.8 |
| Blade compute (sky slice *or* slicer) | GPU | 1.0 | 1.3 |
| Final pass at panel resolution (distortion, CA, passthrough, LUT, late warp) | GPU | 0.8 | 1.0 |
| **GPU total** (target ≤8 on the A16) | | **6.0** | **7.6** |
| Vision hand pose (30 Hz, async) | Neural Engine | ≈19 | ≈25 |

Fill rate does not bind (≈2.7 Mpx × 60 is a small share of the GPU); tile stores, vertex work for two eyes, and heat do. URP: HDR, depth/opaque textures, depth priming, post-processing and shadows off; Native RenderPass on. ARKit: meshing off after mapping, ≤2 tracked images, no ARCameraBackground in VR scenes.

### 7.3 Euclidean → non-Euclidean mapping of scanned meshes

Map each mesh from its local Euclidean frame into the target chart at an anchor isometry A (R = curvature radius; u = v/R; r = |u|).

- **Exponential map:** p = A·(sinh(r)·u/r, cosh(r)) in H (sin/cos in S).
  - Radial distances are exact.
  - Transverse lengths scale by sinh(r)/r ≈ 1 + r²/6.
- **Klein (projective) map:** p = A·(u, 1)/√(1 − |u|²) in H; (u, 1)/√(1 + |u|²) in S (gnomonic). Distortion is larger than the exp map's: at |u| = 0.3 it stretches radially ×1/(1−u²) ≈ +9.9% and transversely ≈ +4.8%, and it diverges as |u| → 1, so it cannot map anything as large as R.
  - **Lines map to geodesics and planes to totally geodesic planes, so flat walls stay flat with no subdivision.**
  - Radial distance becomes R·atanh(|u|).
- **Poincaré model:** edges become arcs; subdivide to edge length ≲0.1R. Use only for conformal views and minimaps.

**Distortion (exp map, transverse stretch) [D]:**

| r = size/R | 0.2 | 0.5 | 1.0 |
|---|---|---|---|
| stretch | +0.7% | +4.2% | +17.5% |

Keep scanned objects under ≈0.3R (≈1.5% exp-map distortion), or deliberately place a large one with the **exp map** (e.g. a scanned room at r ≈ R) to *show* curvature.

**Room cells in tilings (dihedral angle = 2π / cells-per-edge):**

| Honeycomb | Space | Dihedral angle |
|---|---|---|
| {4,3,4} | Euclidean | 90° |
| {4,3,5} | H³ | **72°** |
| {5,3,4} | H³ | **90°** (right-angled dodecahedra; Hypermine) |
| {4,3,6} | H³ | 60° (ideal vertices at infinity, so not usable as walkable rooms) |
| {4,3,3} | S³ | **120°**: 8 cubes = the boundary of a tesseract (the S³–4D tie-in) |

**Practical choice:** Euclidean rooms glued five around an edge by portals give {4,3,5} combinatorics with zero mesh distortion. State honestly that curvature is then concentrated on edges: a polyhedral approximation whose smooth limit is H³. Present both in the docs; this shows mathematical understanding.

**Pipeline:**
1. Clean and decimate the scan on the PC.
2. Remesh to a max edge length tied to R (≲0.1–0.2R).
3. **Place** each vertex with the exp map (exact radial distances) or, for architecture whose walls must stay totally geodesic, with the Klein map.
4. **Store** the result as hyperboloid (or Klein) coordinates in the target cell. Edges then render as geodesic segments.
5. Ship.
6. At runtime, apply only the cell isometry and camera.

### 7.4 Locomotion in a 3×3 m room

**Thresholds (for reference; recalibrate on our headset)**
- Steinicke 2010 [M]: rotation 0.67–1.24; translation 0.86–1.26; curvature radius ≥22 m (measured at 40° FOV and 60 Hz, which is close to phone VR).
- Small rooms narrow translation tolerance to 0.73–1.10 (Kim 2023) [M].
- Bending gains on curved paths reach about 4.4× [M].
- Blink and saccade redirection is impossible (no eye tracking).

**Policy**
1. **Topology does the work:** portals, gluing, branched sheets, hyperbolic tile mapping (Holonomy-style: 3×3 m of nine 1 m cells ↔ a {4,5} tiling; walking loops give holonomy and unbounded travel with no gains [O]).
2. **Authored curved corridors** use bending gains within thresholds.
3. **Covert gains off for first-time judges.** If used, fade them in and out (never step), keep covert curvature injection at or below the detection threshold (radius ≥22 m ≈ 1.8–2.6°/s at 0.7–1 m/s walking; ARC-style controllers used 3–6°/s at the edge of perceptibility), and apply rotation gain mainly during user-initiated turns.
4. **Resets:** distractor or narrative resets preferred over a bare 2:1 turn. APF-style reset direction (towards the total repulsive force).
5. **Fit by construction (Tea For God rule):**
   - rasterise the **eroded** free floor (Oasis-style erosion of 0.3–0.5 m) into 0.5–1.0 m tiles;
   - every module = occupied tiles + door sockets on tile edges;
   - a placement is valid only if all tiles lie inside the eroded area **and every door socket faces ≥1 free tile**;
   - rooms visible at the same time must not overlap;
   - rooms separated by closed doors or bends may overlap up to ≈50% for small rooms (Suma: 56% / 31%; distractor tasks raise tolerance to 68%) [M].
6. **Complexity cap:** about 3×3 to 4×4 tiles of logical maze per region (5×5 was significantly more disorienting; Koltai 2020) [M].
7. **Turn-consistency rule:** "If you choose the door on the right, you turn right on the map" (Tea For God). Avoid rotating portals except where rotation is the content (Station Zero).

### 7.5 Procedural generation (when a blade needs it)

- **Two layers:**
  1. a **logical graph** from cyclic graph grammars (Dormans / *Unexplored*: loop backbone, lock-and-key edges that stay consistent);
  2. a **physical embedding** solved per visit for the next one or two rooms by backtracking over tile placements (Edgar-style simulated annealing as a fallback).
- Wave Function Collapse for room *interiors* and corridor dressing, not the global graph.
- **Style cascade (Tea For God):** region → room → props parameters, so the result is never "a mix of random things". Per-region LUT palette, fog colour, trim sheets and ambient audio bed (§7.11).
- Medical and science blades are **data-driven, not random:** the airway graph *is* the logical graph; the embedding is Sarkar's H² layout.

### 7.6 Pathfinding and placement in non-Euclidean worlds

- **Hierarchical A\*:**
  - **Level 1:** room/portal graph. Edge cost = in-room navmesh distance from entry socket to exit socket (precomputed per module type).
  - **Level 2:** per-room navmesh.
  - Agents cross portals by applying `T_e` (`NavMeshAgent.Warp` plus a manual off-mesh traversal); Unity off-mesh links misbehave unless the cost override is 0 [O].
- **Heuristics:** Euclidean world distance is *not admissible* when portals create shortcuts. Use 0 (Dijkstra), **ALT landmarks computed on the graph**, or hop count × minimum room cost.
- **Hyperbolic trees:**
  - with a greedy embedding, "step to the neighbour closer to the beacon" is correct (Kleinberg; Sarkar with ε < 1/(D−1)) [D];
  - HyperRogue's combinatorial tessellation engine (lazy generation, no float world coordinates, tree-walkers) is the precedent for unbounded tilings (arXiv 2111.12040).
- **Airway route cost [D]:**
  - C(ℓ) = L_path + α·Σ bend(θ_fork) + β·max_phase dist(exit, nodule(phase)) + γ·needle_risk(exit → nodule)
  - subject to min lumen ≥ scope outer diameter;
  - one depth-first search is O(n); show the **Pareto front** (length vs robustness vs needle risk) as coloured corridors.
- **S² path finding:** maximum-clearance directions by distance-field ray-marching; robust windows by phase intersection; multi-beam sets by great-circle spacing; machine reachability as a mask on the T² chart.
- **Placement rules:**
  1. Objects belong to rooms (chart-local).
  2. Physical-safety checks against the *current* embedding at placement.
  3. Physically overlapping rooms are never co-visible (door state, portal culling).
  4. Anything the user must physically reach must lie in tiles that are inside the eroded area under *every* embedding the room can take (e.g. the room centre).
- **Physical vs virtual distance:** the planner scores physical walkable distance to the next door; game logic uses virtual graph distance.

### 7.7 Hands, cube and gaze pipelines

**Hands** (`VisionHands` plugin)
1. Get the `ARFrame` via `XRSessionSubsystem.nativePtr`; copy what is needed and release it at once (retained ARFrames stall the camera after ≈15–20 frames).
2. Run Vision at 30 Hz on every second frame on the **Neural Engine** (`VNRequest.setComputeDevice`), with a latest-only queue (correct landscape orientation; `maximumHandCount = 2`).
3. **Depth:** median `sceneDepth` (high confidence) over wrist + 4 knuckles. Below 0.32 m or at low confidence, use depth from hand size (≈8–8.5 cm knuckle span). Never sample at fingertips (edge noise).
4. Unproject with *that frame's* intrinsics and camera transform.
5. One-Euro filter; predict one frame ahead.

**Display:** a ghost hand plus a camera-frustum ring.

**Cube**
- Fuse the visible faces' poses using known face offsets; One-Euro filter.
- Keep the quaternion sign continuous (choose q or −q nearest the previous value), which matters for the 4D slicer: 720° periodicity is real and teachable, but image tracking returns rotations (SO(3)), so after a tracking dropout the sign is re-chosen and the 720° demo needs continuous tracking.

**Gaze:** centre reticle; hover highlight; 600–800 ms dwell for novices on explicit targets only (no Midas touch).

**Bench first (Phase 0):** camera intrinsics and FOV (expect ≈72°×57°); hand-visible fraction at chest, shoulder and chin heights with the head level vs pitched down 20°; LiDAR hand-depth validity at 25/35/50 cm; cube jitter.

### 7.8 Scanning pipelines

**Room (iPhone)**
1. RoomPlan inside the Unity app, sharing AR Foundation's ARSession (`RoomCaptureSession(arSession:)`, §6.3) → `CapturedRoom` (walls, doors, windows, openings, furniture boxes) → JSON + USDZ.
2. Rebuild the shell from the **modular kit**: extrude walls, insert door modules at detected doors. **Real doors become portal connectors.**
3. Furniture → kit props (instant) or regenerated assets.
4. Furniture becomes **virtual walls and haptic proxies** (beats random placement on presence with no performance cost; Meyer & Langbehn 2025 [M]).
5. Save an `ARWorldMap` + floor marker for relocalisation.
6. Scan the venue **the day before and the morning of** the demo.

Android route: video → MapAnything-Apache or Depth Anything 3 Base/Metric (Apache) → SpatialLM 1.1 (walls/doors/boxes; **non-commercial weights**) → kit rebuild.

**Objects**

| Need | Route | Time (est.) |
|---|---|---|
| Fidelity (the judge's mug) | Object Capture on iPhone (guided) → reconstruct on the M3 Max → USDZ → GLB | minutes |
| Speed (live, the judge's object) | 1–3 photos taken by a teammate on the **Galaxy S24** during framing → SAM 3 or background removal → **SPAR3D on the RTX 5070** (7–10.5 GB) → gltfpack (≈10k tris, 1024² KTX2, meshopt) → phone, placed as the airway cairn | ≈20–60 s [U] |
| Best quality | **TRELLIS.2-4B on the H200** (≈3 s at 512³, ≈17 s at 1024³ on H100-class [O]); lower the default 1M-face export target to 5–20k | seconds + transfer |
| Fallback | Tripo / Meshy / Rodin API, or pre-generated kit asset by label | — |

- **Show the transformation.** Photoreal scanning is now common (e.g. Meta Hyperscape, World Labs Marble [R]); stage the scan → geometry change as a visible animation and keep the stylised look, so the value is what the geometry does to the scan.
- **Coherence:** never show raw photographic lighting. De-light or keep albedo only, then re-light with the region's baked probes and LUT (§7.11).
- **Licences:** MIT/Apache (TRELLIS.2, TripoSG, Brush, meshoptimizer, MapAnything-Apache, DA3 Base/Small/Metric); gated (SAM 3, SAM 3D Objects); non-commercial (SpatialLM weights, VGGT-1B, DA3 Large/Giant); territory-restricted (Hunyuan3D 2.x: not EU/UK/KR).

**What scanned objects are *for* in ATLAS**
- **Cairns/bookmarks** in hyperbolic space (they solve the 22.7 s vs 6.5 s revisit problem).
- **Search targets** (FieldFit).
- **Hand-over objects** (Shared House).
- **Chirality demo** (Glue Die flip).
- **Scale references** (Lung Mission).
- **Seeds** for The Branch.
- **Props with passive haptics** (Station Zero extinguisher).

### 7.9 Safety system (on-phone, every frame)

**Sensing**
- LiDAR `sceneDepth` forward cone (±35°): minimum distance per sector.
- ARKit mesh as a persistent obstacle map.
- Person detection at 5–10 Hz with Apple Vision's on-device human detector (`VNDetectHumanRectanglesRequest`; no licence issue), or an Apache-licensed detector. Avoid AGPL YOLO builds.
- Tracking-state monitor.

**Escalation (design starting points, not study-derived [U])**

| Distance / event | Response |
|---|---|
| ≈2.0 m | Diegetic placeholder in the virtual world (e.g. a tree or pillar where the obstacle is; best for judging position [M]) |
| ≈1.0 m | Fading grid boundary (fewest collisions [M]) |
| ≈0.5 m, fast-approaching person, or tracking loss | Automatic passthrough "peek" or full stop |
| Always | Redundant audio + 20–50 ms haptic pulses |

Latency target: sensing → warning → photon under ≈100–200 ms (fine at walking speed because warnings start 1–2 m out).

**What the tiers measure.** Two sources, each with hysteresis (enter at the threshold, leave 0.15 m further out): (1) distance to real obstacles from LiDAR and the mesh; (2) distance to the **taped play-area outline**, registered from the floor marker at calibration. Virtual paths that run near the outline by design (Breathing Ring) suppress only the 1 m grid tier, never the 0.5 m tier. Lens-standing mode has no LiDAR and is seated only (§4.5).

**Human layer**
- Taped 3×3 m square + 0.5–1 m buffer.
- **Dedicated spotter** (never combined with another role).
- Stop word "exit".
- Operator panic → passthrough.
- Session ≤5 min.

**Public spaces (two documented modes)**
- **Mode A, see-through (recommended):** wide-camera passthrough mixed reality with overlays, 6DoF, continuous environmental awareness. In a 2024 field study, passthrough improved safety and social comfort in public without reducing presence [M].
- **Mode B, full VR with layered warnings:** only in a pre-scanned, roped-off zone with a spotter.
- **Lens-standing mode** (ultra-wide, ARKit paused) is a **seated research mode only** (§4.5); the demo uses Lens-6DoF.
- Never walking in public in full VR without a spotter.

**Privacy**
- Frames in RAM only; nothing recorded or uploaded.
- Visible "camera in use" sign.
- Depth or mesh preferred over RGB.
- Consent for room and object scans.
- Anonymous session IDs.

### 7.10 Audio and haptics

**Spatial audio**
- Steam Audio, or simple panning + attenuation, on the phone's two speakers (they sit near the temples in landscape: verify the separation by ear).
- Wired over-ear headphones only if hygienic covers are available. Bluetooth adds ≥40 ms; no in-ear buds for strangers.

**Geodesic audio (exact and cheap polish)** [D]
- Intensity follows the geometry's sphere-area law: 1/sinh²(r/R) in H³; 1/sin²(r/R) in S³ (a whisper refocuses at the antipode).
- In portal and branched worlds, sound travels along the *glued* geodesic (listener → door → speaker).
- Cue every crossing (portal, wrap, sheet change) with an earcon.

**Haptics:** transient 20–50 ms pulses for confirmations and boundary hits. Never enable ARKit audio capture (it mutes haptics). If voice is used, record through your own `AVAudioEngine` session with haptics allowed during recording [U].

### 7.11 Look and polish (coherence recipe)

1. **Non-photoreal target look:** flat or toon shading, a limited palette or painterly style, so imperfect scans read as intentional.
2. **De-light and re-light** all scanned and generated content; bake lighting per region.
3. **Per-region LUT (palette ramp) in the shared uber-shader:** colour grading at no extra cost; implements the style cascade.
4. **Rim or outline** in the same pass to unify silhouettes from different sources.
5. **Per-region geodesic fog:** hides LOD pops and portal-depth limits, and gives each blade an identity.
6. **Trim-sheet modular kit** on a 1 m or 2 m grid with standardised connector modules (doors, portals, hatches). A RoomPlan door maps to the same connector.
7. **Region ambient audio bed + reverb** matched to room size.
8. **Caption chip** naming the geometry and its rule, in a fixed screen location.

### 7.12 Thermal and performance tiers

- **Watch:** `thermalStateDidChangeNotification` *and* the GPU frame-time trend from Unity's FrameTimingManager (tier changes trigger on whichever comes first). Both phones run at phone B's tier.
- **Tiers:**

  | Thermal state | Settings |
  |---|---|
  | nominal / fair | full quality, 60 fps |
  | serious | render at 30 fps and warp to 60 (the late warp re-projects every displayed frame); keep ARKit at 60 Hz; reduce LOD and render scale; MSAA 2×; Vision ≤15 Hz |
  | critical | minimal scene and passthrough |

  "A stable 30 fps beats a choppy 60" [O].
- **Always:**
  - meshing off after the stage is mapped (keep `sceneDepth` for safety);
  - Vision only in scenes that use hands;
  - no case;
  - vented headset;
  - desk fan between judges;
  - **swap policy (single rule):** swap phones between judges when thermal state reaches "fair" or after 30 min of use, whichever comes first; in-session, drop a tier rather than swap;
  - 20–30 W USB-C PD charging between judges.

### 7.13 Geometry strategy: portals + curved wings (chosen) vs true curved space only (discussed)

**Chosen (D15): portals + curved wings.**
- Most of the world is flat, locally Euclidean charts: the scanned hub, the glued rooms, the sheets of the shared house.
- They are joined by isometric gluings: portals, wall gluings, branch cuts.
- Constant-curvature **wings** (H² airway, S² approach sky, S²×E planet, H³ rooms) are entered through doors and used only where the content is curved.
- This is the "atlas" in the name.

**Alternative: true curved space only.**
- One continuous constant-curvature world with no portals. The whole experience is walked inside H²×E (or S²×E).
- The physical room is mapped onto tiles of a regular tiling, Holonomy-style: the nine 1 m cells of the 3×3 m floor become squares of the {4,5} tiling.
- Every blade is a region of that one space.

| | Portals + curved wings (chosen) | True curved space only |
|---|---|---|
| Answer to "isn't it just portals?" | Needs the live numbers, the Gauss–Bonnet check and curved wings to answer it | Strongest answer: curvature is everywhere, continuous, and felt in every walked loop (holonomy) |
| Curvature radius in 3×3 m | Chosen per wing (R ≈ 2–10 m for walked spaces; airway R ≈ 2.2 m) | Forced small by the tile mapping: R ≈ 0.8 m (edges matched) to ≈0.94 m (centres matched) for 1 m cells [D] |
| Scanned room and objects | Shown 1:1 in flat charts; objects ≤0.3R placed with ≈1.5% distortion | Only objects ≲28 cm stay within ≈1.5%. At R ≈ 0.94 m the exp map stretches transversely ≈+5% at 0.5 m from the anchor, ≈+20% at 1 m and ≈+48% at 1.5 m [D], so the scanned room cannot be the 1:1 hub |
| Geometries available | All: E, P, T, H, S, B, 4s, 4t | One curvature at a time (H or S). Glued, branched and 4D-time blades need separate rules or are dropped, which breaks "curvature where the structure is" for non-tree content |
| Comfort | Topology first; locally Euclidean warm-up | Holonomy reported no sickness aborts (n = 12, public) [M], but frames rotate on every loop and stereo depth is compressed (≈IPD/R) |
| Rendering | Two paths: stencil portals and the Klein shader for wings | One path (Klein shader + tiling cell lists): a simpler engine but harder content |
| Prior art | Novel combination | Close to Holonomy (TU Delft, Web3D 2024), so it must be clearly differentiated |

**How to pursue it later without forking the engine**
- The kernel already supports it: `IGeometry` for H²×E and S²×E, the Klein include, cell lists, double-precision re-anchoring (spike 0.7).
- Build a **Curved-Only mode** as a hub variant:
  - the hub becomes the fundamental tile;
  - the Curvature Dial (X5) sets K globally;
  - the Lung airway and The Branch become regions of the same H² world, reached by walking instead of through doors.
- Keep Safe Harbour Euclidean regardless (non-negotiable 5).
- **Middle ground worth building anyway:** one "curved hall" wing that is a continuous H²×E region big enough to walk loops in. The Foucault Cube (H4) and the Holonomy replication live there.
- Decide with spike 0.14 (§10.1).

---

## 8. Evaluation and measurement (built in, not bolted on)

### 8.1 Live package (costs the judge under 30 s)

1. **Baseline FMS** (Fast Motion Sickness Scale: one spoken item, 0–20; r = .785 with SSQ total [M]).
2. **Pointing probe** at the end of the Lung airway walk: "point back to the trachea door and press A or pinch". Log the ray azimuth and compute three predictions:
   - (a) the **true geodesic direction** in the rendered H² (parallel-transported direction to the start);
   - (b) the **Euclidean dead-reckoning prediction** (direction to the physical start mark from the ARKit pose);
   - (c) the **graph prediction**, if a portal shortcut was used (not applicable on the airway, a tree; used in Station Zero and Wormhole Quest).

   Show which model the judge's arrow is closest to. Widdowson & Wang predict Euclidean; Warren's wormhole walkers made 37–41° constant errors toward wormhole-predicted locations [M]. *One trial is a demonstration, not a measurement; say so.*
3. **SEQ** (Single Ease Question, 1–7) for the route task, and **exit FMS**.
4. **Automatic logs:**
   - physical vs virtual path length;
   - forks and wrong turns;
   - task times;
   - frame-time histogram (p50, 1%-low);
   - thermal timeline;
   - warnings triggered;
   - hand-tracking confidence;
   - twin-input usage (a robustness metric).

**Operator rule:** FMS ≥4, or any verbal discomfort → skip to a stationary blade or end. This is a team policy, not a validated cut-off.

**Exact wording (narrator).**
- **Consent, before the headset:** "This is a research demo. We log your movement and your answers under an anonymous ID; no camera images are saved. Say 'exit' at any time and we stop. OK to go ahead?"
- **FMS, entry and exit:** "From 0, no sickness at all, to 20, frank sickness: thinking of nausea, general discomfort and stomach problems, where are you right now?"
- **Pointing probe:** "Without turning round, point back to the trachea door, the way you came in, and press A."
- **SEQ, after the airway:** "Overall, how difficult or easy was finding your way through the airway? 1 is very difficult, 7 is very easy."
- **Close:** "Thank you. Your results are on the screen; nothing identifies you."

### 8.2 Logging schema and dashboard

**Event example**
```json
{"sid":"J07","t":131.42,"blade":"lung","ev":"probe","ray_az":212.4,"pred_geodesic":148.0,"pred_euclid":205.1,"err_geo":64.4,"err_euc":7.3}
```

**Dashboard panels (30 s reveal)**
1. **Your decisions (shown first):** the Two Compasses fork choice vs where the Euclidean compass pointed, the ITV saved by gating, the needle clearance vs the best robust window.
2. **Your walk:** the taped square with the trail, beside the unrolled virtual path (Poincaré disk, tiled cover, sheet diagram). "≈10 m of airway inside 3 m of floor." The pointing-probe result appears here as a footnote.
3. **Comfort:** FMS entry and exit vs today's visitors.
4. **Performance:** frame-time trace, thermal state, tracking drops.
5. **Knife Map coverage:** the cells and mappings this judge used.

**Privacy:** anonymous IDs; no camera frames; show only this judge plus aggregates; delete on request; consent line on the sign. UXF (MIT) is a ready-made Unity trial and pose logger.

### 8.3 Technical benchmarks to publish

- fps p50 and 1%-low per blade on both phones after a 30-min thermal soak.
- Motion-to-photon latency (photodiode or 240 fps camera).
- Tracking drift per metre walked (VRoamer reported ≈1% [O]).
- Obstacle-warning latency (target <200 ms).
- Geometric numerical drift (hyperboloid renormalisation error after N steps).
- Hand-tracking visible fraction and latency.
- Cube jitter.
- Scan-to-world time (room, object).
- Asset load-to-visible time.
- Breath Room motion sanity check against the DIR-Lab expert landmarks (validation only, §4.3.4).

### 8.4 Demonstration strategy (Ledo et al., CHI 2018; Olsen, UIST 2007)

- **Novel examples:** the blades.
- **Replicated examples (cheap, very persuasive about expressiveness):**
  - Warren's wormhole maze;
  - Suma's impossible-spaces overlap;
  - Holonomy's {4,5} walk.

  Each is rebuilt as a spec in our engine, with lines of code and authoring time reported.
- **Design-space coverage:** the Knife Map.
- **"How to" walkthrough:** authoring a new blade from a dataset in six steps.
- **Avoid the usability trap:** no SUS-of-the-toolkit study.
- **Olsen's criteria as the discussion lens:**
  - situations, tasks, users;
  - importance;
  - problem not previously solved;
  - generality (N rooms tested);
  - reduced solution viscosity (blade = fundamental domain + gluing group + curvature + data binding);
  - empowering new designers;
  - power in combination;
  - scale (10⁴–10⁶-node trees, multiple users).

### 8.5 Later studies (paper-grade)

A paired within-subjects design needs N≈24 for d = 0.6 at power 0.8 (two-sided α = .05); N = 18 gives only ≈0.67.

| ID | Study |
|---|---|
| S1 | **Lung route study:** H² + endoluminal vs endoluminal only vs 2D CT; pulmonology trainees; accuracy, wrong turns, time, TLX, SSQ |
| S2 | **Glued Room pointing:** ISO 9241-411 tapping, flat vs glued, with targets whose shortest path goes through the back door; model movement time against geodesic vs visual distance |
| S3 | **Hyperbolic pointing law [D]:** target size w at geodesic distance r subtends ≈ w/sinh r, so the angular index of difficulty grows ≈ linearly in r. Test it: a falsifiable, novel HCI result |
| S4 | **Map vs graph in Shared House:** Warren's shortcut paradigm; Bayes-factor model comparison |
| S5 | **Comfort dose-response:** FMS per minute across curvature levels and gluing types |
| S6 | **Threshold calibration** for phone-VR FOV (2AFC) |
| + | FieldFit lens vs uniform minification (simulated loss, then real users with partners) |
| + | Shared House feasibility with older adults |

---

## 9. The live demo (a judge wears it)

### 9.1 Roles, space, kit

**Roles**

| Role | Job |
|---|---|
| Narrator / guide | Talks to the judge, runs the script, asks FMS and SEQ |
| **Spotter** | Safety only, inside the buffer, behind the judge's shoulder. A touch on the shoulder means stop |
| Operator | At the PC console |
| Crowd explainer | At the spectator screen and poster |

| Remote teammate | House Call option only: in a scanned room elsewhere on the LAN with the Galaxy S24 partner client |

With a team of three, merge operator and explainer and offer FieldFit as the judge's choice (House Call needs a 4th person); **never merge the spotter role.**

**Space**
- Taped 3×3 m walkable square + 0.5–1 m buffer (tape ≈4×4 to 5×5 m).
- A centre "X": every blade starts on it, which doubles as a tracking re-anchor.
- Stanchions or chairs for onlookers.
- Textured mat if the floor is plain or glossy; even, flicker-free light (ARKit and the cube need texture and light).
- Keep headsets lens-down away from windows (sunlight through the lenses can burn the screen).

**Kit**
- Phone A (iPhone 15 Pro Max) and phone B (iPhone 14 Pro Max, cool spare), same quality tier, and two headsets. Galaxy S24 for the judge's object photos and the House Call partner client.
- A deck of ≈20 printed case cards (pre-segmented public CT cases) for Roll a Lung.
- Disposable face covers; non-alcohol wipes for shells, **dry microfibre only for lenses**; sanitiser.
- Own 5 GHz router (no internet dependency); PC; Mac (AirPlay fallback, and to reinstall free-account builds) with cables for both iPhones.
- Power, PD chargers, desk fan.
- Printed cube ×2; gamepads ×2 (pre-paired).
- Foam partition (if the Soundscape is shown); chair and water.
- Poster: design-space map, situations/tasks/users per blade, architecture, "Real / Pre-baked / Mocked" table, QR codes to docs and code.
- Safety and consent sign: warnings for photosensitivity, pregnancy and vestibular conditions; minimum age 13.

### 9.2 Onboarding (≤60 s)

1. Ask: "VR before? Carsick easily? Glasses?" (carsick → gentle path).
2. **Passthrough start:** strap, glasses, focus card ("is this crisp?"), software IPD nudge if needed.
3. "Raise a hand… press A… B goes back. Double-tap B, or say 'home', to come back to your room. Say 'exit' and we stop." (The narrator relays spoken words to the console.)
4. Fade to VR on the X.

A see-through phase before full immersion was rated best for onboarding [O].

### 9.3 Run-of-show

**10-minute slot (in the headset 0:40–5:40 = 5:00)**

| Time | Segment | Judge does | Onlookers see | Key line |
|---|---|---|---|---|
| 0:00–0:40 | Frame | Picks a case card; hands over a pocket object (a teammate photographs it on the S24); baseline FMS | Poster; Roll a Lung progress bar; the object's mesh appearing | "Every space here has the shape of what's inside it. Today: one patient, five geometries." |
| 0:40–1:20 | Passthrough onboarding | Fit; raise a hand; A/B | First-person passthrough | "That's every control: walk, look, press." |
| 1:20–1:50 | **Bend Your Room** | Turns the dial; walks the 1.5 m square with the arrow | Live equation: θ = \|K\|·A predicted vs measured | "Your own room, bent. Your feet just measured curvature." |
| 1:50–2:35 | **Breath Room** | Walks east through the breath; flags the quiet part; looks along the tube; sees own back; tilts the slice (10-min slot) | The space-time tube; ITV number shrinking | "Breathing repeats, so time is a circle: flat, but it wraps. That shadow is the radiotherapy target." |
| 2:35–3:50 | **Airway** | 4 forks incl. Two Compasses; finds their object as a cairn; pointing probe | Poincaré airway lighting up; compass choice | "Airways split about 23 times; hyperbolic space keeps every branch full width. Straight-line intuition points the wrong way here." |
| 3:50–4:20 | **Needle sky** | Looks to a green window; commits; watches the great-circle check | Unrolled sphere; plan card generating | "Every straight needle path is one point on this sphere, and on a sphere even parallel paths meet." |
| 4:20–4:40 | **Post Circling** | Through the door; round one post; home | Sheet diagram | "Each home is a sheet; the curvature sits in the doorposts." |
| 4:40–5:25 | **Judge's choice** | FieldFit (blind spot + lens) or House Call with a live teammate | Lens view + Schwarz bound, or the teammate pinning a hazard | "Same engine, bending the view" / "a home visit through a doorway" |
| 5:25–5:40 | Exit | Back to X; FMS; SEQ; headset off; visible wipe | "Computing your results…" | — |
| 5:40–6:10 | **Dashboard reveal** | Turns to the screen | Decisions first, then path, comfort, frame time, coverage | "At the compass fork the straight-line compass pointed the wrong way; you chose [right]." |
| 6:10–10:00 | Q&A / deep dive | Optional: X12 Geometry Lens, the classic Glued Room, Tier B videos | Architecture; Real/Pre-baked/Mocked table | Answer "what's real?" first |

**5-minute slot (in the headset ≈2:50):** frame + case card 0:25 → onboarding 0:25 → Bend Your Room 0:25 → Breath Room 0:35 → airway (3 forks incl. the compass fork) 0:55 → needle sky *or* Post Circling 0:20 → exit 0:10 → dashboard 0:25 → Q&A 0:40.

**3-minute science-fair slot:** frame 20 s → fit 20 s → Bend Your Room + Breath Room 60 s → dashboard 20 s → Q&A 60 s; airway and sky on video.

**Panel of judges:** judge 1 does the story path. While judge 1 is at the dashboard, judge 2 does a 45 s classic Glued Room on phone B. **Ask the organisers about slot length and format.**

### 9.4 Comfort rules

- Real walking only; no joystick vection.
- World motion (Möbius grab, time scrub, Curvature Dial) only while clutched, rate-limited, with an FOV vignette.
- **Topology before gains:** zero covert gain on the first blade.
- Order: passthrough → overt, clutched curvature (Bend Your Room) → locally Euclidean topology (Breath Room) → gentle curved walking (airway) → stationary (needle sky, FieldFit).
- ≤5 min in the headset (symptoms ramp by ≈10 min [M]).
- Station Zero (rotating "up") is opt-in only: a walking wormhole study excluded 10 of 24 recruits for sickness [M].
- Stable 60 fps; report the 1%-low.

### 9.5 Fallbacks

| Failure | Detection | Immediate action | If it persists |
|---|---|---|---|
| Tracking lost or drifting | Tracking state, position jump | Fade to passthrough; back to X; re-anchor | Swap to phone B; then video |
| fps <45 sustained, or thermal "serious" | Console tile | Drop a tier ("this is our thermal safety tier") | Swap phones between judges |
| LAN or Wi-Fi down | RTT tile | Blades are standalone by design; dashboard catches up from the phone's buffer | AirPlay or video |
| AI service or H200 unreachable | Service tile | All AI assets pre-baked and cached on the phone, labelled "pre-generated" | — |
| Remote partner absent | — | Recorded avatar track, labelled "recorded" | Video |
| Hands fail (light, sleeves) | Hand-confidence tile | "Pad mode": every hero has a twin | Narrator drives via console |
| Person enters the square | LiDAR / person detection, or spotter | Auto passthrough; spotter says stop | — |
| Judge uncomfortable | FMS / verbal | Stationary blade or end; chair, water | Offer videos |
| App won't open (free-account build expired or removed) | Launch check before each session | Reinstall from the Mac over USB (≈5 min); use phone B meanwhile | — |
| Total failure | — | 3-min sizzle video + dashboard from earlier visitors | — |

**Make before the event:** 30–60 s videos per blade, a 3-min sizzle reel, a 2-page abstract and a layout schematic (the IEEE VR demo format: these double as documentation).

### 9.6 Rehearsal targets

- 15–20 dress rehearsals with naive people, stopwatch timed, FMS logged. This also seeds the "today's visitors" distribution.
- Median time in headset ≤5:00 in the 10-min slot (≈3:00 in the 5-min slot); segment overruns ≤10 s.
- ≥90% of naive rehearsers complete the Lung route without rescue.
- Median exit FMS ≤2; nobody stops for sickness.
- fps 1%-low ≥45 on both phones after 30 min of back-to-back sessions.
- Recover from a forced tracking loss in ≤15 s.
- Roll a Lung builds every case card in the deck in ≤10 s; the judge's object mesh is ready before onboarding ends in ≥80% of runs.
- Between judges, run a 60 s public Glued Room loop for visitors (it builds a crowd and grows N for the dashboard).

### 9.7 "Real / Pre-baked / Mocked" (fill in honestly before the event)

| Item | Real (live) | Pre-baked | Mocked |
|---|---|---|---|
| Room scan of the venue | morning-of RoomPlan | day-before backup | — |
| Lung segmentation, H² layout, sky fields | — | ✓ (H200/PC) | — |
| Breathing-ring phases | live crossfade | ✓ meshes | — |
| Judge's object (S24 photos → SPAR3D) | ✓ live, ≈20–60 s | kit fallback | — |
| Roll a Lung (completion, H² layout, kit, tiles) | ✓ live, ≤10 s | segmentation of the case cards | — |
| Breath Room tube, ITV tables, sky volumes | — | ✓ per case card | — |
| Shared House partner | ✓ two phones | recorded track | — |
| FieldFit lens | ✓ live warp | Teichmüller LUTs | — |
| Tier B science (MD, forecast, LLM) | only if an H200 interactive node is reachable | ✓ | — |

### 9.8 Narration script (10-minute slot, word for word)

Operator cues are in brackets. Keep lines short: the judge is busy walking.

| Time | Narrator says | Cue |
|---|---|---|
| 0:00 | "Every space here has the shape of what's inside it. Today: one patient, five geometries. Pick a case card, any one." | [Operator starts Roll a Lung for that card] |
| 0:15 | "May we borrow something from your pocket? We'll photograph it, and you'll meet it again inside the lung." | [Teammate photographs it on the S24] |
| 0:25 | Consent and FMS (§8.1). | [Operator logs FMS] |
| 0:40 | "You'll see the real room first. Raise a hand… press A… B goes back. Double-tap B or say 'home' and you're back here." | [Passthrough, then VR on the X] |
| 1:20 | "This is your own room. Turn the dial: the floor stays flat under your feet, but the space bends. Now walk the square and carry the arrow." | [Equation card on screen] |
| 1:40 | "It came back turned. That turn is the curvature times the area you walked: your feet just measured it." | — |
| 1:50 | "Breathing repeats, so here time is a circle: walk east and you move through the breath. Flat, but it wraps." | [Breath Room] |
| 2:10 | "Drop a flag where the tumour is quietest, and another where it starts to move. Now look along the tube: that shadow is the radiotherapy target." | — |
| 2:25 | "Tilt the cube. You're rotating the slice through time: a turn in a direction you can't point to." | [10-min slot only] |
| 2:35 | "These are the airways of the card you picked. They split about 23 times; hyperbolic space keeps every branch full width." | [Airway] |
| 3:00 | "Two compasses. The straight-line one points one way, the beacon another. Which do you trust?" | [Log the choice] |
| 3:30 | "Recognise this? Your object, waiting at the fork. Now point back to the trachea door and press A." | [Pointing probe] |
| 3:50 | "You're standing inside the lesion. Every straight needle path is one point on this sphere. Look at a green window to choose it." | [Needle sky] |
| 4:10 | "Watch the two sweeps: they start side by side and meet at the top. On a sphere, parallels meet." | [Great-circle check] |
| 4:20 | "Through this door is someone else's home. Walk round the post… and you're back in yours. The curvature sits in the doorpost." | [Post Circling] |
| 4:40 | "Your choice: bend the view to fit a damaged visual field, or visit a teammate's home through a doorway." | [FieldFit or House Call] |
| 5:25 | "Back to the X. Headset off when you're ready." Then FMS and SEQ. | [Visible wipe] |
| 5:40 | "At the compass fork the straight-line compass pointed the wrong way; you chose [right/left]. Here's your walk: about 10 m of airway inside 3 m of floor." | [Dashboard reveal] |
| 6:10 | "What's real? Here's the honest table." | [Real / Pre-baked / Mocked, §9.7] |

### 9.9 Safety briefing, consent and the booth sign

**Spoken safety briefing (spotter, 15 s, before the headset):** "I'm your spotter and I'll stay right behind you. If I touch your shoulder, stop. If you say 'exit', we stop straight away. The taped square is your space, and you'll see warnings well before any wall."

**Booth sign (A3, at eye height by the queue):**

> **ATLAS: walk through impossible geometry**
> A research and education demo on a phone VR headset. You walk inside a taped 3×3 m square with a spotter beside you. Sessions last about 5 minutes.
> **Please don't try it if** you are pregnant, have epilepsy or photosensitivity, a vestibular or balance condition or a heart condition, or feel unwell. Minimum age 13.
> **What we record:** your movement and answers under an anonymous ID. **No camera images are saved.** Ask us to delete your data at any time.
> **Medical content** uses public, de-identified scans for education only. It is not a medical device and not for clinical use.
> Say "exit" at any time and we stop.

---

## 10. Build plan

Effort numbers are research-agent estimates [U]; validate them in Phase 0.

### 10.0 What is planned vs the full idea (readiness)

Grades: ● build-ready (inputs, outputs, approach, formats and an acceptance test) · ◐ specified (approach and parameters; some interfaces or edge cases still to write) · ○ concept (described, not specified).

| Area | The full idea | What the build plan delivers for the event | Readiness | Phase |
|---|---|---|---|---|
| Geometry kernel | 8 kinds of space (E, P, T, H, S, B, 4s, 4t) + conformal warp under one interface | All 8 + warp; the live path uses E, P, T, H, S, B and 4t | ◐ formulas, precision rules and identity tests defined; method signatures to write | 1–4 |
| Rendering | Stereo, portals, curved space, 4D, sky, lens at 60 fps | One-camera side-by-side stereo, stencil portals, Klein H²×E, 4D slicer, sphere-traced sky, single final pass with late warp; frame budget (§7.2) | ● portals; ◐ rest (spikes 0.7, 0.15–0.17, 0.20) | 0–2 |
| Sensing and input | 6DoF walking, LiDAR, hands, cube, pad, gaze | Same; every hand technique has a twin; X12 Geometry Lens as the hand hero | ● ARKit, pad, gaze; ◐ hands, cube | 1, 4 |
| Safety | Collision warnings, including in public | Two-source tiers with hysteresis; passthrough Safe Harbour; Modes A/B; Lens-standing seated only | ◐ thresholds to tune on device | 1 |
| Scanning → world | Rooms and objects scanned in, AI meshes | RoomPlan in-app → kit rebuild; the judge's object live via S24 photos → SPAR3D; TRELLIS.2 kit in batch | ◐ kit catalogue to define | 1–3 |
| Procedural generation | Continuous non-Euclidean worlds | **Live:** Roll a Lung (airway completion, H² layout, kit, tiles). General graph-grammar worlds: Tier B | ◐ live; ○ general | 2; 5+ |
| Pathfinding and placement | Non-Euclidean routing and placement | Airway route cost and Pareto set, Two Compasses, greedy study, S² clearance search; general portal A* in Tier B | ◐; ○ general | 2; 5+ |
| 4D | 4D space and 4D time | Breath Room (ℝ³ × S¹) with the end-on ITV view; 4D toybox and Tesseract House on the roadmap | ◐; ○ | 2; 5+ |
| Purpose blades | ≈20 applications | Story path (Hub + Bend, Breath Room, Lung, Post Circling) + FieldFit + House Call + classic Glued Room; Tier B on video; Tier C in docs | ◐ Tier A; ○ Tier B/C | 1–4 |
| Design space | 6 dimensions, 63 techniques, 15 heroes | 9–11 techniques in the live path; all 63 documented; every technique logs its code | ◐ | 1–4 |
| Multi-user | Shared worlds and remote care | Solo Post Circling (pre-scanned, recorded); live House Call with the S24 partner client as an option | ◐; ○ co-walking | 3; 5+ |
| PC side | Server, console, spectator, dashboard | FastAPI server, one message schema (App. B), Windows spectator build with state replication, Knife Map live/reveal | ◐ | 1–4 |
| AI and H200 | Generation, segmentation, studies | Batch: case-card segmentation, tubes, sky volumes, greedy study, asset kit. Live: object mesh, Roll a Lung | ◐ airway depth unproven (spike 0.19) | 2–3 |
| Evaluation | FMS, pointing, SEQ, logs | Same; dashboard leads with decisions | ◐ | 1–4 |
| Engineering | Optimised and solid | Frame budget, tests, CI/CD, perf regression, memory budget, kill switches, crash reporting (§10.5) | ◐ specified | 0–1 |
| Demo operations | A live demo judges can try | Run-of-show ≤5:00, roles, fallbacks, rehearsals | ● | 4 |

**Deliberately not built for the event:** general procedural worlds, Tier B/C blades (video and docs only), H³ and S³ rooms, covering-space co-walking, full Android parity, and the studies.

### 10.1 Phase 0: spikes and benchmarks (≈2 weeks, platform only; decide D5, D9, D16, D19 and lock budgets)

Pass criteria run on **phone B** (the slower phone sets the tier), after a 20-minute soak, with "≤1 missed vsync per minute" as the frame-pacing bar.

| # | Spike | Pass criterion |
|---|---|---|
| 0.1 | Stereo pipeline on the 15 Pro Max and 14 Pro Max in the headset: empty scene, then a 150k-tri-per-eye scene | ≤1 missed vsync/min; GPU ≤8 ms on phone B |
| 0.2 | ARKit through the camera window: intrinsics, FOV (≈72°×57°), vignetting, LiDAR unobstructed, tracking while walking | Stable tracking, drift <2% over a 20 m walk |
| 0.3 | Hand reachability (5 people, three heights, head level vs −20°); X12 Geometry Lens detector | ≥80% visible frames in the recommended pose; Geometry Lens ≥90% recognised |
| 0.4 | LiDAR hand depth at 25/35/50 cm; real update rate | Valid at ≥35 cm |
| 0.5 | Cube: jitter, latency, loss under rotation; 2–3 tracked images | Usable at chin height |
| 0.6 | Stencil portal cost in stereo (depth 1 and 2) | <1.5 ms GPU at depth 2 (both eyes) |
| 0.7 | Klein vs equidistant stereo on an H²×E wing: comfort, depth, precision after 10 min | Pick a mode per wing; no visible jitter |
| 0.8 | Closed-headset thermal soak of the full stack | No tier drop before 20 min at nominal settings |
| 0.9 | Wi-Fi RTT/jitter on the own router; `iproxy` dev loop | p99 RTT <30 ms |
| 0.10 | Lens mode switch (research mode only) | Resume <2 s, relocalised |
| 0.11 | RTX 5070 model timings: SPAR3D, TotalSegmentator; PyTorch cu128 | Each runs; times recorded |
| 0.12 | H200 access: batch queue and interactive policy | Policy known |
| 0.13 | Cardboard viewer profile for the chosen headset, both phones | Straight lines straight at the edges |
| 0.15 | One camera vs two cameras on phone B | Main + render thread ≤7 ms; memoryless MSAA confirmed in a GPU capture |
| 0.16 | Motion-to-photon (240 fps video) with and without Core Motion prediction + late warp; 60 Hz pacing on ProMotion | Rotational ≤30 ms; ≤1 missed vsync/min |
| 0.17 | Vision on the Neural Engine with ARKit and rendering running | GPU delta <0.3 ms; no camera frame drops |
| 0.18 | RoomPlan through AR Foundation's ARSession; relocalisation from the saved world map | <5 s; <3 cm |
| 0.19 | Airway depth (TotalSegmentator vs MONAI Auto3DSeg / nnU-Net), skeleton robustness, H² layout, greedy success on real trees | ≥6 generations segmented or completed; R and corridor rule fixed |
| 0.20 | Sphere-traced sky, 10 phases, on phone B | Current phase <100 ms; all phases ≤300 ms; ≤100 MB |
| 0.21 | Wi-Fi with 3 devices: UDP loss and jitter; Local Network prompt after reinstall | Loss <1%; p99 <50 ms |
| 0.22 | Memory: cycle every blade 20× on phone B | No jetsam; peak ≤1.8 GB |
| 0.24 | CI: commit to installed app on both phones, signed by the free Personal Team (`xcodebuild -allowProvisioningUpdates` + `devicectl`) | ≤20 min |
| 0.25 | glTFast loading a 5 MB GLB mid-scene | Worst frame <20 ms |
| 0.26 | Free-account signing (D19): Personal Team installs on both phones; Increased Memory Limit capability accepted or refused; 7-day expiry dry run | Both phones install from the Mac; capability accepted, or spike 0.22 passes without it |

**After Phase 2:** spike 0.14 (geometry strategy, D15) needs the airway wing and the pointing probe, so it runs once those exist.

### 10.2 Phases to the hackathon build

| Phase | Deliverables | Exit criteria |
|---|---|---|
| **1. Kernel, hub, safety, tooling** | `IGeometry` (E, T, P first); chart/gluing world model; one-camera stereo + final pass (distortion, passthrough, late warp); stencil portals; hub from an in-app RoomPlan scan + modular kit v1; relocalisation (world map + floor marker); safety tiers + Safe Harbour; gamepad/gaze input; X1 Geodesic Ray; classic Glued Room T1, T4 (pad-selected gluing); logging; message schema + server; operator console v0; **Windows spectator build** with state replication; Knife Map v0; CI/CD with free-account signing (D19), kill switches, crash reporting | A naive user completes the Glued Room at 60 fps on phone B; Safe Harbour <300 ms; warnings fire at 2/1/0.5 m; commit-to-device ≤20 min |
| **2. Story path core** | **Bend Your Room** (X5 + H4); **Breath Room** (with Slice Tilt); Lung pipeline (case-card segmentation on the H200, skeleton, H² layout, tree.json, tile solver, chambers/corridors, valve doors); **Roll a Lung**; **Two Compasses**; endoluminal window; needle sky (sphere-traced, with the great-circle check); plan card; pointing probe; object pipeline (S24 photos → SPAR3D → cairn); greedy study | Four forks in ≤75 s by a naive user; Roll a Lung ≤10 s; sky per §4.3.5; plan card validates |
| **3. Post Circling, judge's choice** | Solo Post Circling on pre-scanned sheets (recorded avatar); FieldFit (blind-spot finder, circular tunnel + power-law lens, Lens-6DoF; Teichmüller LUTs precomputed); minimal S24 partner client + LiveKit for House Call | Both choices run inside the 10-min show; recorded fallbacks exist |
| **4. Hands, cube, polish, show** | Vision hands + twins; X12 Geometry Lens; cube (T3 Glue Die, D2 views); style cascade, LUTs, outlines, audio beds, earcons; dashboard reveal (decisions first); thermal tiers; videos; 15–20 rehearsals; docs (§8.4) | All §9.6 targets met |
| **5+. Long project** | Tier B blades; full Android build; Walk Together covering-space co-walking; Wormhole Quest; replications; studies S1–S6 with ethics approval; clinical partners | Papers, studies, partners |

### 10.3 Repository layout (suggested)

```
atlas/
  phone-unity/                    # Unity 6 LTS project (URP, IL2CPP, iOS + Android)
    Assets/Atlas/Kernel/          # IGeometry: Euclid, Hyperbolic(H2xE,H3), Spherical(S2xE,S3), Quotient(T2,T3,Klein,orbifold),
                                  #   Branched(sheets,permutations), Portal, Slicer4D, Warp; double-precision math (Burst)
    Assets/Atlas/World/           # charts, gluings, room graph, per-visit embedding, tile solver, style cascade
    Assets/Atlas/Rendering/       # StereoRig, DistortionPass, StencilPortalFeature (URP), CurvedSpace.hlsl (Klein include),
                                  #   GeodesicFog.hlsl, Slicer4D.compute, HealpixSky.compute, WarpLUT.shader
    Assets/Atlas/Interaction/     # technique registry (code G·B·M·F·T·P), input channels, twins, One-Euro filter
    Assets/Atlas/Blades/          # Hub, GluedRoom, LungMission, SharedHouse, FieldFit, (StationZero, Branch, ...)
    Assets/Atlas/Safety/          # depth sectors, mesh proximity, person detector, escalation, SafeHarbour
    Assets/Atlas/Link/            # WS/UDP/HTTP client, MessagePack contracts, clock sync, watchdog, glTFast loader
    Assets/Atlas/Telemetry/       # logger, KnifeState, StateSync, probes (FMS/SEQ/pointing)
    (Windows build target)        # the same project builds the PC spectator renderer
    Assets/Plugins/iOS/           # Swift: VisionHands, RoomPlanBridge, ObjectCaptureBridge, Thermal, Haptics
  server/                         # Python 3.11: FastAPI realtime, UDP pose, assets, operator console, dashboard (D3)
    workers/                      # SPAR3D, TotalSegmentator, BioCLIP, gltfpack/toktx jobs (queue)
  pipelines/
    medical/                      # tcia_fetch, segment, skeletonize, sarkar_embed, mesh_export, distance_fields, phases_itv
    objects/ rooms/ fieldfit/     # object gen + optimize; RoomPlan→kit; UWHVF→fields; Teichmüller fitting
  hpc/                            # Slurm scripts for H200 batch (TRELLIS.2, segmentation, studies, lens atlas)
  specs/                          # messages.yaml (single schema → C#/Python), tree/plancard schemas, blade configs, replications
  ci/                             # GitHub Actions (self-hosted on the M3 Max), xcodebuild/devicectl install scripts, perf-regression scripts
  docs/                           # maths notes, design space, evaluation, claims language, licences, demo runbook
  research/                       # ATLAS_Research_Dossier.md
```

### 10.4 Engineering standards

- No per-frame GC allocations in the render or safety loop. Burst/Jobs for maths. Async GPU readback only.
- Double-precision CPU geometry, re-anchored every frame; float on GPU, relative to the camera cell.
- Every technique registers its design-space code and an equation template (for the Knife Map and the docs).
- Every blade has a deterministic replay (logged inputs → identical render), for the PC spectator and debugging.
- Unit tests for kernel identities:
  - exp∘log ≈ id;
  - loop holonomy = |K|·A within tolerance;
  - Riemann–Hurwitz χ on generated houses;
  - glide gluing flips orientation;
  - the Schwarz bound holds for generated lenses.
- Licence ledger in `docs/licences.md` (MIT/Apache by default; flag NC, GPL, gated and territory-restricted items).

### 10.5 Testing, CI/CD, profiling, memory and kill switches

- **Tests.** Kernel identities in double precision; GPU-vs-CPU parity for the Klein, HEALPix (reference: astropy-healpix, BSD) and slicer kernels; slicer output compacted with a prefix sum (deterministic, unlike append buffers); per-blade golden images (Unity Graphics Test Framework); schema round-trip tests; ARKit session recordings (Xcode "Replay data") for tracking regressions. Recordings contain camera video, so they are **team-only test data, never judge sessions** (non-negotiable 8 exemption for the team's own rehearsal recordings).
- **Performance regression.** A nightly replay on phone B logs CPU/GPU p99, missed vsyncs, memory and thermal state; fail the build on a >10% regression.
- **CI/CD.** Self-hosted GitHub Actions runner on the M3 Max: Unity batch mode with a persistent Library cache → PostProcessBuild sets Swift settings, entitlements and Info.plist keys (`NSLocalNetworkUsageDescription`, `NSBonjourServices`, camera) → `xcodebuild -allowProvisioningUpdates` signs with the free Personal Team (D19) → `xcrun devicectl` installs on both phones connected to the Mac; the free account has no TestFlight, so the secondary route is Run from Xcode. Freeze a **golden demo build** and the CI pipeline before the event.
- **Profiling.** Xcode Instruments (Metal System Trace, Game Performance, Allocations); a weekly GPU frame capture per blade; CPU/GPU-ms tiles on the operator console.
- **Crash reporting.** Sentry or Crashlytics with an offline queue, plus MetricKit.
- **Memory.** Budget for phone B (6 GB): app peak ≤1.8 GB; watch `os_proc_available_memory`; request the increased-memory-limit entitlement if the free Personal Team accepts it (spike 0.26); otherwise hold the budget without it. Per blade: textures ≤300 MB, meshes ≤150 MB, volumes ≤100 MB.
- **Kill switches.** On the device (gamepad chord, works offline) and on the console: hands, cube, meshing, person detector, live vs cached sky, portal depth, glued copies, MSAA, render scale, network. Active flags are logged per session.
- **Demo-day ops.** Guided Access, orientation lock, Do Not Disturb, AirDrop/Handoff off; encrypted pseudonymous logs; videos play from the PC.

### 10.6 What to cut first if time runs short

In order: the Cardboard XR plugin spike (D5 alternative) → Lens-standing mode → Slice Tilt (keep the end-on view) → live House Call (LiveKit, avatars, S24 partner client) → Steam Audio (use simple panning) → live object meshes (use kit objects) → spike 0.14 and Curved-Only mode → the judge's-choice slot. Never cut: safety, Safe Harbour, the story path's geometry moments, the plan card, the dashboard.

### 10.7 Schedule templates (pick one when the event date is known)

Count back from the event. Gates are pass/fail: if a gate fails, take the listed cut rather than letting everything slip.

| Weeks available | Plan | Cuts built in |
|---|---|---|
| **10 or more** | W1–2 Phase 0 · W3–4 Phase 1 · W5–7 Phase 2 · W8 Phase 3 · W9 Phase 4 · W10 rehearsals | Only §10.6 if needed |
| **8** | W1–2 Phase 0 · W3–4 Phase 1 · W5–6 Phase 2 · W7 Phase 3 (FieldFit only) + Phase 4 essentials · W8 rehearsals | Live House Call, Steam Audio; Slice Tilt as stretch |
| **6** | W1 Phase 0 (make-or-break spikes only: 0.1, 0.15, 0.2, 0.7, 0.19, 0.26) · W2–3 Phase 1 (Glued Room + Bend Your Room) · W4–5 Phase 2 (Breath Room, airway on curated cases, needle sky precomputed) · W6 rehearsals | Post Circling and the judge's choice on video; hands as stretch |
| **Under 6** | The 3-minute path (Bend Your Room + Breath Room + dashboard); the rest on video | Most blades |

**Gates (every plan).**
- End of Phase 0: go/no-go per spike, each with a written plan B (protocols in the engineering spec).
- End of Phase 1: Glued Room at 60 fps on phone B; Safe Harbour <300 ms.
- Middle of Phase 2: the first full story run, end to end, by someone who has not seen it.
- 7 days before: feature freeze.
- 3 days before: golden build frozen.
- 1 day before: install on both phones (free-account builds, D19) and run the launch check offline.

### 10.8 Ownership (fill in when the team is set)

One owner and one backup per area. The owner decides inside the area; the lead decides between areas.

| Area | Covers | Owner | Backup |
|---|---|---|---|
| Rendering and AR | Stereo, final pass, late warp, portals, curved shaders, thermal tiers | ____ | ____ |
| Geometry engine | Kernel, charts and gluings, tile solver, 4D slicer, maths tests | ____ | ____ |
| Safety and input | Safety state machine, Safe Harbour, gamepad, gaze, hands, cube | ____ | ____ |
| Server and console | Link, assets, Roll a Lung jobs, console, spectator, logging | ____ | ____ |
| Medical pipeline | Case cards, segmentation, completion, H² layout, tubes, sky volumes | ____ | ____ |
| Look, sound and assets | Kit, style cascade, LUTs, audio, object pipeline | ____ | ____ |
| Demo and evaluation | Script, rehearsals, comfort measures, dashboard, videos, poster | ____ | ____ |

On the day: narrator, spotter (never doubled), operator and explainer (§9.1).

---

## 11. Risks and mitigations

| # | Risk | Likelihood / impact | Mitigation |
|---|---|---|---|
| 1 | Thermal throttling over a demo day (≈50% GPU after 40 min of stress; the enclosed headset makes it worse) | High / High | Fixed 60 fps, tiers keyed to `thermalState`, meshing off after mapping, Vision only when needed, vented headset, fan, **second phone hot-swap** |
| 2 | Hands not seen (narrow cone, edges, low light, sleeves) | High / Medium | Hands are always optional (twins); targets 20–30° below eye line at 40–55 cm; frustum ring; good lighting |
| 3 | ARKit drift or tracking loss (dark hall, plain floor, crowds) | Medium / High | Textured mat, even light, saved `ARWorldMap` + floor marker, re-anchor on X, passthrough on loss |
| 4 | Cybersickness (first-timers, curved worlds, rotating portals) | Medium / High | Topology before gains; ≤5 min; ordering; vignette; FMS stop rule; Station Zero opt-in |
| 5 | Judges read it as "just portals" / "just a skybox" | Medium / High | Live numbers (Knife Map equation card); Gauss–Bonnet check; walk-around-vs-through demo; flat twin vs curved view; one real task per blade |
| 6 | Over-claiming medically | Medium / High | §2.4 wording; public data only; "not for clinical use" on screen |
| 7 | Second phone or network failure (Shared House) | Medium / Medium | Recorded avatar track; blade order allows skipping |
| 8 | AI generation slow or unavailable live | Medium / Low | Everything pre-baked; live generation is a bonus labelled as such |
| 9 | 5070 incompatibilities (Blackwell sm_120, custom CUDA kernels) | Medium / Low | PyTorch cu128; prefer models without exotic kernels; run heavy ones on the H200 |
| 10 | Licences (NC weights, GPL code, gated models, territory limits) | Medium / Medium | Licence ledger; don't copy HyperRogue (GPL) or unlicensed repos; NC only for research and demo |
| 11 | Headset doesn't fit both iPhones' camera plateaus (15 Pro Max, 14 Pro Max) or glasses | Medium / High | Buy two candidates early; test with both phones and a glasses wearer; cut a viewer if needed |
| 12 | Scope creep (63 techniques, 15+ blades) | High / High | Tier A only until Phase 4 exit; the design space is *documented*, not all *built* |
| 13 | Challenge rules unknown (Huawei hardware? slot length? panel?) | Unknown / High | Ask the organisers now (§13) |
| 14 | Hyperbolic navigation not learnable by everyone | Medium / Medium | Beacons, breadcrumbs, cairns, hand-held Poincaré map, Ariadne's Rope, Euclidean home |
| 15 | Privacy (room scans, motion as a biometric) | Low / High | Parametric shares only, consent, anonymous IDs, no frames stored |
| 16 | No single-pass stereo on iOS; two cameras + portal cameras are CPU-bound | High / High | One-camera side-by-side stereo with portal RendererLists (D5); spike 0.15 |
| 17 | Motion-to-photon too high (capture-time pose, no prediction) | Medium / High | Core Motion prediction + late rotational warp (D16); spike 0.16 |
| 18 | Airway segmentation too shallow for a 4-fork route | High / High | Benchmark MONAI Auto3DSeg / nnU-Net (spike 0.19); Roll a Lung completes distal generations, labelled model-generated; curated default case |
| 19 | Over-claiming the hyperbolic airway (1+ε distortion; greedy guarantee) | Medium / Medium | Claims limited to constant width and room for every branch (§4.3.1); greedy success measured, not assumed |
| 20 | Free-account limits: a build expires (7 days) or Xcode refuses the memory capability | Medium / Medium | Install ≤24 h before the event and launch-check each session; Mac and cables on site; spike 0.26; paid program as fallback (D19) |
| 21 | Five geometries in five minutes feel like a tour, not a purpose (UX is 30% of the score) | Medium / High | One rule and one "aha" per space (§4.0, §9.8); the patient story and plan card tie them together; rehearsal targets (§9.6) |

---

## 12. Prior art and positioning

| System | What it does | Gap ATLAS fills | Cite |
|---|---|---|---|
| **Holonomy** (TU Delft, Web3D 2024) | Walk an infinite H² world in 3×3 m (nine cells → {4,5}); Poincaré minimap; no sickness aborts (n=12 public) | Single-user; navigation only; boundaries with no physical meaning; needs Quest/Vive. ATLAS: phone, scanned room as the base chart, manipulation/measure/create tasks, multi-user | arXiv 2409.05460 |
| HyperRogue (12.0+ VR), Hyperbolica, Non-Euclidean VR I–IV, Hypernom, 3-dimensional.space | Correct curved-space rendering and play | Little *interaction* beyond navigation; not tied to real tasks or rooms | dossier → `rendering_math_4d.md` |
| Tea For God, Unseen Diplomacy 2 (Mar 2026) | Procedural impossible spaces with real walking | Euclidean overlap only; UD2 soft-locks when levels exceed room bounds (our room scan fixes this) | dossier → `purpose_nonmedical_prior_art.md` |
| Oasis (TVCG 2017/18) | Scanned room → walkable VR; merges walkable areas | 1:1 only; no curved or branched geometry | Sra et al. |
| VRoamer, DreamWalker | Walk unknown buildings or outdoors in VR | Euclidean | MSR |
| Portal-ble (UIST 2019) | Free-hand manipulation in smartphone AR | Not VR, not non-Euclidean | Qian et al. |
| Space Bender (IEEE VR 2020) | Overt bending at boundaries | Single technique | Simeone et al. |
| **"Space-Bending Interaction … to Cheat at Golf"** (IEEE VRW 2024, 3DUI Contest) | Curvature used as an interaction technique (title-level; read it) | Check overlap with H7/X5 before claiming novelty | IEEE Xplore 10536307 |
| **"Beyond Euclid: An Educational VR Journey into Spherical Geometry"** (3DUI 2024) | Spherical-geometry education | Same name as the challenge. Check its triangle tasks against S4 | IEEE VR 2024 contest |
| MRUnion (TVCG 2025), Desk2Desk, SpaceBlender, Choi 2024 | Telepresence across dissimilar rooms | Flat intersection or union; ATLAS uses branched sheets, so every home stays full-size | dossier → `shared_worlds_remote_care.md` |
| 4D Toys, Miegakure, 4D Golf, Tesserxel | 4D slicing and play | No medical 4D-time; no tangible slice handle | — |
| Clinical minifiers, VR perimetry (Heru, VisuALL…), DSpecs (Sayed et al.) | Field expansion; field testing; DSpecs relocates and resizes unseen targets into the remaining field | No Schwarz-bound or quasiconformal-optimal lens trialled; FieldFit's claim is the optimal-within-class lens, not remapping as such | dossier → `therapy_assistive_remaps.md`; [Review of Optometry on DSpecs](https://www.reviewofoptometry.com/article/digital-specs-improve-visual-awareness) |
| Holographic virtual bronchoscopy (HoloLens 2, 18 patients; Respiration 2026 [R]); HMD bronchoscopy phantom study (Frontiers VR 2023 [R]) | XR bronchoscopy planning and guidance exists clinically | ATLAS pitches a hyperbolic layout with measured greedy routing for comprehension and training, not "XR bronchoscopy" | [Karger](https://karger.com/res/article/doi/10.1159/000551021/946259/Initial-experience-with-3D-Holographic-Virtual) · [Frontiers](https://frontiersin.org/journals/virtual-reality/articles/10.3389/frvir.2023.940536/text) |
| Immersive radiotherapy plan review (Tran et al., TVCG 2026 [R]) | Immersive RT plan review | ATLAS's distinct parts: time as a circle (gating) and the ITV as a 4D shadow | [PDF](https://ivlab.cs.umn.edu/static/data/pubs/downloads/Tran-2026-Mayo-AuthorsVersion.pdf) |
| Planar bronchial-tree layouts (patent US 8045769; geometry-aware planar tree embedding [R]) | 2D airway maps | ATLAS's H² case: walkable constant-width corridors and measured greedy success, with a planar dendrogram as a twin | [patent](https://image-ppubs.uspto.gov/dirsearch-public/print/downloadPdf/8045769) |
| Auda et al., CHI 2022, "If The Map Fits!" [R] | Minimaps as distractors from non-Euclidean layouts | Show the flat twin after the "aha" | [page](https://hci.informatik.uni-due.de/en/publications/if-the-map-fits-exploring-minimaps-as-distractors-from-non-euclidean-spaces-in-virtual-reality-16030) |
| Meta Hyperscape Capture, World Labs Marble [R] | Photoreal room capture and AI-generated worlds | Sell what the geometry does to the scan, not the scan | [TechCrunch](https://techcrunch.com/2025/09/17/meta-launches-hyperscape-technology-to-turn-real-world-spaces-into-vr) · [Marble](https://www.worldlabs.ai/blog/marble-world-model) |

**Positioning sentence:** *"To our knowledge, no prior system combines phone-only 6DoF walking, scanned real rooms, hand and tangible input, and several exact non-Euclidean geometries (hyperbolic, spherical, glued, branched, 4D) under one engine with real-task applications."* Never "first ever".

---

## 13. Open questions for the team (answer these early)

1. **Challenge rules.** The challenge document sets no hardware or platform requirement and asks for 30+ FPS. Still to ask the organisers:
   - event date and slot length;
   - whether code may be written before the event;
   - single judges or a panel; live demo only, or video too;
   - venue space, lighting, power and Wi-Fi (can we bring our own router?).
2. **Team size and roles** (the demo needs ≥3 people: narrator, spotter, operator).
3. **Phones:** confirm phone B is an iPhone 14 **Pro** Max (LiDAR), not a 14 Plus. Test the Galaxy S24 with the Cardboard optics (black-artifact issue).
4. **H200 cluster policy:** batch only (Slurm), or interactive nodes reachable from a venue network? Data-transfer limits?
5. **Which headset(s):** order two candidates now (§6.2) and test fit with the 15 Pro Max and with glasses.
6. **Medical datasets:** confirm TCIA terms acknowledgement. Choose Option A (one 4D-Lung patient throughout) or Option B (composite: LIDC case with an outer-third 8–20 mm nodule + 4D-Lung motion, labelled composite).
7. **Hackathon blade set:** Tier A as proposed, or swap Shared House for Station Zero if the second phone is a risk?
8. **Naming:** keep "ATLAS"?
9. **Geometry strategy:** stay with portals + curved wings, or also pursue a "true curved space only" mode (§7.13)? Decide after spike 0.14.
10. **Staffing:** is there a 4th person for the live House Call option? If not, the judge's choice is FieldFit only.
11. **Case cards:** which ≈20 public cases (with a lesion reachable within 4–6 generations after completion) go into the deck?
12. **Signing:** does the free Personal Team accept the Increased Memory Limit capability (spike 0.26)? If not, does the app hold 1.8 GB without it?
13. **Schedule:** once the date is known, pick a template in §10.7 and fill in the owners in §10.8.

---

## 14. Answers to the founding questions (short form)

| Question | Answer |
|---|---|
| **What is the purpose?** | A platform (knife) for non-Euclidean interaction, with blades where the space takes the shape of a real problem. The deep blade is medical (Lung Mission: hyperbolic airway routing, 4D-CT breathing, needle-direction sphere). The others: assistive vision (FieldFit), social and remote care (Shared House), science education (Glued Room stations), training (Station Zero) |
| **Euclidean → non-Euclidean mapping?** | (a) Topology: keep rooms flat and glue them with isometries (portals, gluing, sheets). (b) Curvature: place meshes with the exp map at an anchor (transverse distortion ≈ (size/R)²/6) or the Klein map when walls must stay flat (larger distortion); keep objects ≤0.3R or show curvature on purpose. (c) The scanned room is the base chart or fundamental domain; real doors become portals; furniture becomes walls and haptic proxies |
| **Procedural generation?** | **Live in the demo: Roll a Lung** (airway completion with a published space-filling model, H² layout, kit chambers, tile placement, in seconds). In general: Logical graph (cyclic grammar) + per-visit physical tile embedding (one free tile beyond every door) + modular kit + style cascade. For data blades, the data *is* the graph (airway tree, phylogeny) |
| **Scanning vs generation?** | Scanning brings the real room in (Bend Your Room, Post Circling, the judge's object as a cairn, obstacles as objects). Generation builds worlds bigger than the room (Roll a Lung live; seed-generated worlds on the roadmap). See §3.10 |
| **Non-Euclidean pathfinding and placement?** | Hierarchical A* on the chart graph with edge transforms and graph-based (ALT) heuristics; greedy routing in hyperbolic embeddings (Kleinberg/Sarkar); the airway route cost with a Pareto front; S² clearance search; chart-local placement with embedding-invariant reach rules |
| **Is 4D useful?** | Yes, where it has a job: **time** (the Breath Room: the tumour as a space-time tube whose shadow is the ITV; time is a circle, flat but wrapped), the w-model explaining overlapping rooms, the tesseract boundary as S³ {4,3,3}, and a 4D toybox. Not as the core renderer |
| **Polished and coherent?** | One legible rule per blade; stylised look with per-region LUTs, rim, geodesic fog and baked light; trim-sheet modular kit; de-lit, re-textured scans; spatial audio beds and earcons; caption chips; the Knife Map |
| **iPhone or Android?** | iPhone: the 15 Pro Max in the headset, the 14 Pro Max as phone B (both have LiDAR, RoomPlan, Object Capture). The Galaxy S24 as the Android partner. Huawei is not required by the challenge document |
| **Software?** | Unity 6 LTS URP + AR Foundation (ARKit) + custom stereo with Cardboard-profile optics + Swift plugins; Python FastAPI server; PyTorch on 5070 / H200 |
| **Phone–PC pairing?** | The phone renders locally. LAN Wi-Fi (own router): WebSocket control, UDP pose, HTTP assets. `iproxy`/`adb reverse` over USB for development. No video streaming (50–150 ms vs ≈20 ms comfort budget) |

---

## 15. Reference library (key links; ≈850 in the dossier)

**Challenge**
- *Beyond Euclid: Interactive Experiences in Impossible Geometries* (Huawei Custom Challenge #1), challenge brief (docx).

**Non-Euclidean rendering and engines**
- Hart, Hawksley, Matsumoto, Segerman. Non-Euclidean VR I (H³) and II (H²×E): [arXiv 1702.04004](https://arxiv.org/abs/1702.04004), [arXiv 1702.04862](https://arxiv.org/abs/1702.04862)
- Coulon, Matsumoto, Segerman, Trettel. Ray-marching Thurston geometries: [arXiv 2010.15801](https://arxiv.org/abs/2010.15801) · [3-dimensional.space](https://3-dimensional.space/)
- Kopczyński & Celińska-Kopczyńska. Real-time visualization in non-isotropic geometries: [arXiv 2002.09533](https://arxiv.org/abs/2002.09533) · tessellation engine [arXiv 2111.12040](https://arxiv.org/abs/2111.12040) · [HyperRogue](https://github.com/zenorogue/hyperrogue) (GPL-2.0) · [vr.cpp](https://github.com/zenorogue/hyperrogue/blob/master/vr.cpp)
- [HackerPoet/HyperEngine](https://github.com/HackerPoet/HyperEngine) (Hyperbolica backend, MIT) · [HackerPoet/NonEuclidean](https://github.com/HackerPoet/NonEuclidean) · [SebLague/Portals](https://github.com/SebLague/Portals) · [Ralith/hypermine](https://github.com/Ralith/hypermine)
- Slotboom 2023, portals in VR (stencil vs render texture): [TU Delft](https://repository.tudelft.nl/file/File_6caa1e43-0353-419f-b07b-2b598b224731)
- Weeks, Body coherence in curved-space VR: [arXiv 2011.00510](https://arxiv.org/abs/2011.00510)
- Holonomy: [arXiv 2409.05460](https://arxiv.org/abs/2409.05460) · [4TU dataset](https://data.4tu.nl/datasets/c37b581e-46fd-4a0b-800d-e2758642dc1e)
- 4D: [Tesserxel](https://github.com/wxyhly/tesserxel) · [godot-4d](https://github.com/aaronfranke/godot-4d) · [ten Bosch N-D rigid bodies](https://marctenbosch.com/ndphysics/) · Yan, Fu & Hanson, Multitouching the 4th dimension ([PDF](https://homes.luddy.indiana.edu/hansona/papers/4Dice-mco2012090080.pdf))

**Redirected walking, impossible spaces, generation**
- Steinicke 2010 thresholds ([PDF](https://sreal.ucf.edu/wp-content/uploads/2017/02/SBJFL10-optimized.pdf)) · Nilsson 2018 review ([PDF](https://Www.Gwern.net/doc/technology/2018-nilsson.pdf)) · Coles, Hmaiti & LaViola 2025 survey ([arXiv 2505.16011](https://arxiv.org/abs/2505.16011)) · Azmandian 2015 space size ([EG](https://diglib.eg.org/items/7269bd1d-0e2d-4c5e-a2cd-66fbb18bbc2c/full))
- ARC alignment ([arXiv 2101.04912](https://arxiv.org/abs/2101.04912)) · APF (Bachmann 2019, [PDF](https://www.cs.purdue.edu/cgvlab/courses/490590VR/notes/VRLocomotion/MultiuserRedirectedWalking/APFRedirectedWalking2019.pdf)) · Xu pose-score precomputation ([PDF](https://archive.ymsc.tsinghua.edu.cn/pacm_download/606/12313-Optimal_Target_Guided_Redirect_Walking_With_Pose_Score_Precomputation.pdf)) · Nescher MPC ([thesis](https://www.research-collection.ethz.ch/server/api/core/bitstreams/a734deb6-2b82-4d37-90b3-34959df14cb8/content))
- Tea For God devlogs: [impossible-space generation](https://void-room.itch.io/tea-for-god/devlog/76304/procedural-level-generation-using-impossible-spaces) · [open world](https://void-room.itch.io/tea-for-god/devlog/235914/open-world-with-impossible-spaces) · VRoamer ([PDF](https://microsoft.com/en-us/research/uploads/prod/2019/03/chen_ofek_holz_wilson-VRoamer-Generating_On-The-Fly_VR_Experiences_While_Walking_inside_Large_Unknown_Real-World_Building_Environments.pdf)) · Oasis ([PDF](https://sites.cs.ucsb.edu/~sra/publications/oasis.pdf))
- OpenRDW ([repo](https://github.com/yaoling1997/OpenRDW)) · OpenRDW2 ([repo](https://github.com/omegafantasy/OpenRDW2)) · Edgar ([repo](https://github.com/OndrejNepozitek/Edgar-Unity)) · WFC ([repo](https://github.com/mxgmn/WaveFunctionCollapse)) · Unexplored cyclic generation ([Boris the Brave](https://www.boristhebrave.com/2021/04/10/dungeon-generation-in-unexplored/))

**Cognition, health, purpose**
- Warren 2017 wormholes ([PDF](https://par.nsf.gov/servlets/purl/10214057)) · Widdowson & Wang 2022 ([PDF](https://par.nsf.gov/servlets/purl/10387047)) · Muryy & Glennerster 2021 ([PMC](https://pmc.ncbi.nlm.nih.gov/articles/PMC8057603)) · Howett 2019 ([PDF](https://www.mrc-cbu.cam.ac.uk/wp-content/uploads/www/sites/3/2019/07/HowettEtAl_B_19_VR_MCI.pdf))
- Dolina 2008 bronchoscopic path selection ([record](https://pure.psu.edu/en/publications/interbronchoscopist-variability-in-endobronchial-path-selection-a/)) · Ravikumar 2023 CT-to-body divergence ([PMC](https://pmc.ncbi.nlm.nih.gov/articles/PMC10001114)) · Heerink 2017 biopsy complications ([Springer](https://link.springer.com/doi/10.1007/s00330-016-4357-8)) · AAPM TG-76 ([PDF](https://www.aapm.org/pubs/reports/RPT_91.pdf))
- Tsapakis 2017 smartphone-VR perimetry ([Dovepress](https://www.dovepress.com/visual-field-examination-method-using-virtual-reality-glasses-compared-peer-reviewed-fulltext-article-OPTH)) · Gupta 2018 scotoma remapping ([PDF](https://legge.dl8.umn.edu/sites/legge.psych.umn.edu/files/files/media/gupta18_beneficial_effects_of_spatial_remapping_for_reading_with_simulated_central_field_loss.pdf)) · Doyon, Hwang & Jung 2024 prism hazard ([PDF](https://pelilab.partners.org/papers/boe-15-3-1393.pdf)) · Lui et al. Teichmüller maps ([arXiv 1211.2569](https://arxiv.org/abs/1211.2569))
- WHO social connection 2025 ([news](https://www.who.int/news/item/30-06-2025-social-connection-linked-to-improved-heath-and-reduced-risk-of-early-death)) · Cochrane falls/home hazards 2023 ([summary](https://www.sydney.edu.au/news-opinion/news/2023/03/10/how-to-reduce-falls-among-older-people-at-home.html)) · Choi 2024 placement ([arXiv 2408.04297](https://arxiv.org/abs/2408.04297)) · MRUnion ([TUM](https://portal.fis.tum.de/en/publications/mrunion-asymmetric-task-aware-3d-mutual-scene-generation-of-dissi-2/))
- Health Canada SaMD ([guidance](https://www.canada.ca/en/health-canada/services/drugs-health-products/medical-devices/application-information/guidance-documents/software-medical-device-guidance-document.html)) · FDA General Wellness update Jan 2026 ([summary](https://www.kslaw.com/insights/articles/fda-updates-general-wellness-and-clinical-decision-support-guidance-documents))

**Data**
- [LIDC-IDRI](https://www.cancerimagingarchive.net/collection/lidc-idri/) · [4D-Lung](https://www.cancerimagingarchive.net/collection/4d-lung/) (CC BY 3.0) · [TotalSegmentator](https://github.com/wasserth/TotalSegmentator) · [UWHVF](https://github.com/uw-biomedical-ml/uwhvf) (BSD-3) · [FreeSurfer licence](https://surfer.nmr.mgh.harvard.edu/fswiki/FreeSurferSoftwareLicense) · Open Tree of Life (CC0) · [DIR-Lab 4DCT](https://med.emory.edu/dir-lab) (validation) · [ACDC cardiac cine MRI](https://www.creatis.insa-lyon.fr/Challenge/acdc/) (Heartbeat Torus) · [C. elegans light-sheet lineages](https://zenodo.org/records/6460375) (CC BY 4.0, Embryo Room)

**Platform**
- Cardboard: [SDK](https://github.com/googlevr/cardboard), [cardboard.h](https://raw.githubusercontent.com/googlevr/cardboard/master/sdk/include/cardboard.h), [Unity plugin](https://github.com/googlevr/cardboard-xr-plugin), [viewer profiles](https://developers.google.com/cardboard/manufacturers)
- Apple: [RoomPlan](https://developer.apple.com/documentation/roomplan) · [sceneDepth](https://developer.apple.com/documentation/arkit/arconfiguration/framesemantics-swift.struct/scenedepth) · [Object Capture](https://developer.apple.com/documentation/realitykit/scanning-objects-using-object-capture) · [Vision hand pose](https://developer.apple.com/documentation/vision/vndetecthumanhandposerequest) · [TN3179 Local Network](https://developer.apple.com/documentation/technotes/tn3179-understanding-local-network-privacy) · ultra-wide lockout ([forum 831657](https://developer.apple.com/forums/thread/831657), [forum 812818](https://developer.apple.com/forums/thread/812818)) · [free vs paid developer accounts](https://developer.apple.com/support/compare-memberships/)
- [glTFast](https://docs.unity3d.com/Packages/com.unity.cloud.gltfast@6.0/manual/features.html) · [meshoptimizer](https://github.com/zeux/meshoptimizer) · [MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp) · [apple/unityplugins](https://github.com/apple/unityplugins) · [Steam Audio](https://github.com/ValveSoftware/steam-audio) · [LiveKit](https://github.com/livekit/livekit) · [UniVRM](https://github.com/vrm-c/UniVRM) · [UXF](https://github.com/immersivecognition/unity-experiment-framework)

**HCI framing**
- Ledo et al. 2018, Evaluation strategies for HCI toolkit research ([PDF](https://creativetech.mat.ucsb.edu/readings/toolkit_eval.pdf)) · Olsen 2007, Evaluating UI systems research (UIST) · Danyluk et al. CHI 2021, WIM design space ([page](https://ilab.ucalgary.ca/publications/chi-2021-danyluk/)) · Abtahi et al. CHI 2022, Beyond being real ([arXiv 2204.08566](https://arxiv.org/abs/2204.08566)) · Locomotion Vault ([site](https://locomotionvault.github.io/))

---

## Appendix A: Formula sheet (verify the [D] items before publishing)

| Quantity | Formula |
|---|---|
| Hyperbolic distance (hyperboloid, radius R) | d = R·arcosh(−⟨p,q⟩_L / R²) |
| Spherical distance | d = R·arccos(p·q / R²) |
| Exp map H / S | cosh(‖v‖/R)·p + R·sinh(‖v‖/R)·v̂ ; cos / sin |
| Circumference at radius r | H: 2πR·sinh(r/R) ; S: 2πR·sin(r/R) |
| Ball volume H³ | πR³(sinh(2r/R) − 2r/R) ≈ (π/2)R³·e^{2r/R} |
| Light falloff | H³: 1/sinh²(r/R) ; S³: 1/sin²(r/R) |
| Holonomy (Gauss–Bonnet), constant K | θ = \|K\|·A ; shoelace A = ½·Σ(x_i z_{i+1} − x_{i+1} z_i) |
| Area-matched flat reach [D] | r_E = 2R·sinh(ρ/2R) |
| Tree-matched curvature [D] | R = ℓ / ln b |
| Klein embedding of a scanned vertex | p = A·(u, 1)/√(1 − \|u\|²) (H) ; (u, 1)/√(1 + \|u\|²) (S) |
| Exp-map transverse stretch | sinh(r)/r ≈ 1 + r²/6 |
| Regular {p,q} tiling edge | cosh(a/2R) = cos(π/p)/sin(π/q) ({4,5}: a ≈ 1.254R; adjacent-tile centre spacing ≈ 1.061R) |
| Honeycomb dihedral angle | 2π / (cells per edge): {4,3,5} 72° · {5,3,4} 90° · {4,3,3} 120° |
| Stereo vergence at infinity in H³ [D] | ≈ IPD / R |
| Schwarz lens bound [D] | central magnification ≤ tan(ρ₀/2)/tan(E/2) |
| Minimax radial lens [D] | σ = s₁(s/s₁)^{1/K\*}, K\* = ln(S/s₁)/ln(σ₀/s₁) |
| Joukowski scotoma flow | w = z + a²/z ; scale \|1 − a²/z²\| |
| Pinch-the-Sky (Möbius) [D] | a = (w₁−w₂)/(z₁−z₂), b = w₁ − a·z₁ ; aberration tan(θ′/2) = e^{−η}·tan(θ/2) |
| Cube → 4D slice [D] | slice = {q·(w₀ + a·i + b·j + c·k)} ; tilt = θ_cube/2 ; SO(4): v ↦ q_L·v·q̄_R |
| Branched house [D] | χ = N − Σ_posts(N − #cycles σ) ; two-home doors, connected door graph, N boundary circles: genus = K − N + 1 ; post curvature 2π − 2πk |
| Two-column covering world [D] | places within n laps = 2·3ⁿ − 1 |
| Rotation gain as a cone [D] | gain g ⇔ cone of total angle 2πg (deficit 2π(1 − g)) |
| Clock offset (NTP) | ((t2 − t1) + (t3 − t4))/2 |
| Breathing ring | φ = atan2(z − c_z, x − c_x); bin = ⌊10·(φ mod 2π)/2π⌋; ITV = ∪ GTV over the arc |

## Appendix B: Message schema v1 (MessagePack, string-keyed; JSON shown)

**Conventions.** Poses in Unity world space (left-handed, Y up, metres); quaternions as `[x, y, z, w]`; timestamps `t_us` in phone monotonic microseconds (the PC converts with the clock offset); every message has `type` and the first message on each connection carries `proto: [major, minor]`. The single source is `specs/messages.yaml`, which generates the C# and Python types.

| Message | Direction | Fields |
|---|---|---|
| `Hello` | client → PC | `proto, device, platform, app, role` (`phone`, `partner`, `spectator`, `console`) |
| `Welcome` | PC → client | `proto, session_id, server_time_us, world_version` |
| `Ping` / `Pong` | both | `seq, t1_us` / `seq, t1_us, t2_us, t3_us` |
| `Pose` (UDP) | phone → PC | `seq, t_us, pos[3], rot[4], chart, sheet, cell, track` (`normal`, `limited`, `lost`) |
| `Event` | phone → PC | `sid, t_us, blade, ev, data{}` |
| `KnifeState` (10 Hz) | phone → PC | `t_us, blade, tech, code[6], params{}, equation{tpl, values{}}, chart{model, x, y, path_tail[]}, sheet, wraps, fps, gpu_ms, hand_conf` |
| `StateSync` (10–30 Hz) | phone → spectator | `t_us, chart, sheet, cell, blade_state{}, xforms{}` |
| `PlanUpdate` | PC → phone | `plan_id, based_on_seq, valid_until_us, at_door, next_module, embedding{tile[2], yaw}, confidence` |
| `WorldDelta` | PC → phone | `world_version, ops[]` |
| `CaseBuild` | PC → phone/console | `case, status` (`queued`, `building`, `ready`, `failed`), `progress, asset_ids[]` |
| `AssetReady` / `AssetAck` | PC → phone / phone → PC | `asset_id, url, sha256, bytes, format, lod, bounds[3]` / `asset_id, ok, load_ms` |
| `OperatorCmd` | console → phone | `cmd` (`blade`, `panic`, `reanchor`, `recenter`, `comfort`, `ipd_nudge`, `thermal_tier`, `fallback`, `kill`), `args{}` |
| `HouseState` | phone ↔ partner (via PC) | `user, sheet, pose{pos, rot}, t_us` |
| `HandOver` | phone ↔ partner | `object_id, from_sheet, to_sheet, pose` |
| `HazardPin` / `ChairStand` | partner → phone | `pos[3], note` / `reps, seconds` |
| `Heartbeat` | both | `t_us, thermal, battery, fps` |
| `Error` / `Bye` | both | `code, msg` / `reason` |

```json
{"type":"Hello","proto":[1,0],"device":"iPhone15ProMax","platform":"ios","app":"0.4.0","role":"phone"}
{"type":"Pose","seq":10231,"t_us":8812345678,"pos":[0.41,1.62,-0.88],"rot":[0.01,0.71,0.0,0.70],
 "chart":"lung.airway.n37","sheet":0,"cell":"0,0","track":"normal"}
{"type":"KnifeState","t_us":8812400000,"blade":"lung","tech":"S1","code":["S","head","iso","world","select","self"],
 "params":{},"equation":{"tpl":"lung_traversed={a}mm clearance={b}mm","values":{"a":18,"b":6}},
 "chart":{"model":"sphere","x":212.4,"y":-8.1,"path_tail":[]},"sheet":0,"wraps":0,"fps":60,"gpu_ms":7.1,"hand_conf":0.0}
{"type":"PlanUpdate","plan_id":"p88","based_on_seq":10220,"valid_until_us":8814345678,
 "at_door":"d17","next_module":"corridor_curved_R2.2_L","embedding":{"tile":[2,1],"yaw":90},"confidence":0.82}
{"type":"CaseBuild","case":"C07","status":"ready","progress":1.0,"asset_ids":["tree_C07","chambers_C07"]}
{"type":"OperatorCmd","cmd":"blade","args":{"blade":"fieldfit"}}
{"type":"Event","sid":"J07","t_us":8812420000,"blade":"lung","ev":"compass","data":{"fork":3,"chose":"geodesic","euclid_pointed":"left","correct":true}}
```

## Appendix C: Shopping and setup list

| Item | Qty | Est. cost (CAD, rough) | Note |
|---|---|---|---|
| iPhone 15 Pro Max (A), iPhone 14 Pro Max (B), Galaxy S24, MacBook M3 Max, RTX 5070 PC | have | 0 | Pack a Lightning cable for phone B |
| Phone VR headset with camera window, strap, glasses fit | 2 | 60–280 | Generic camera-window viewers ≈30–60 each; Zapbox-class ≈140. Test the 15 Pro Max camera bump and LiDAR clearance |
| Google Cardboard v2 (development) | 1 | 15–25 | Viewer-profile QR for optics tests |
| Bluetooth gamepad (8BitDo / Xbox / DualSense) | 2 | 80–180 | Pre-pair; spare batteries |
| Printed marker cube (8–10 cm, matte, handle) | 2 | 20–40 | Six distinct high-contrast faces |
| Wi-Fi 6E router + Ethernet cable to the PC | 1 | 150–300 | No internet dependency; fixed channel |
| USB-C PD chargers 20–30 W, cables, power strip | 2+ | 60–120 | Charge between judges |
| Desk fan | 1 | 25–50 | Cool phones between judges |
| Textured floor mat, gaffer tape, lamp | — | 60–150 | Tracking and safety; borrow stanchions or use chairs |
| Face covers (100), non-alcohol wipes, microfibre, sanitiser | — | 40–70 | Clean visibly |
| A1 poster, A3 safety and consent sign, ≈20 printed case cards | 1 set | 60–120 | Design-space map, architecture, Real/Pre-baked/Mocked |
| Apple account (free; Xcode Personal Team) | 1 | 0 | Paid Apple Developer Program (US$99/yr) only if spike 0.26 fails (D19) |
| Optional: SO-101 arm (C-Space Room), foam partition (Soundscape) | — | — | Tier B and C only |
| **Total, excluding optional items** | | **≈570–1,350** | Rough estimates; check current prices before buying |

