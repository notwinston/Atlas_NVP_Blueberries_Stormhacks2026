# ATLAS: Engineering Implementation Spec

**How to build it: the tech, libraries, AR/VR pipeline, modules, interfaces, data pipelines, build and test.** Companion to the design brief.

| | |
|---|---|
| Date | 3 October 2026 (planning baseline; companion to the design brief of the same date) |
| For | the implementation agent(s) and the engineering team |
| Design documents | `ATLAS_Implementation_Brief.md` (the design brief: purpose, design space, blades, demo, evaluation; cited here as **Brief §x**) and `ATLAS_Plan.pdf` (human-readable design plan) |
| This document | `ATLAS_Engineering_Spec.md` + `ATLAS_Engineering_Spec.pdf` (human-readable version) |
| Other companions | `ATLAS_Research_Dossier.md` (evidence, ≈850 links); `illustrations/` (diagrams) |

---

## 0. How to use this spec (read first)

**What this is.** The engineering source of truth for building ATLAS: which technologies and libraries, how the AR/VR pipeline works on the phone, how the code is organised, the interfaces between modules, the data pipelines, the server, the build plan, and how it is tested. *What* the experience is and *why* lives in the design brief; where the two differ on an engineering detail, **this spec wins**.

**Reading order.**
1. §1 *Engineering summary* (what we build, decisions, hardware, readiness).
2. §2 *Development environment* (set up machines, versions, packages; first build).
3. §3 *Architecture* and §5 *AR/VR rendering pipeline*.
4. §15 *Build plan*, starting with the Phase 0 spikes and their test protocols (§15.8); signing set-up is in §2.8 and Appendix E.
5. The section for the subsystem you build.

**Conventions.**
- **MUST / SHOULD / MAY** in the RFC 2119 sense.
- Evidence tags: **[M]** measured · **[O]** observed (docs, deployments) · **[D]** derived from standard maths (verify before public use) · **[U]** unverified or estimate · **[R]** reported in a secondary source or the press (verify).
- Version numbers marked [verify] must be checked against the pinned Unity release before use.
- "Brief §x" points into the design brief (`ATLAS_Implementation_Brief.md`); "dossier → `file.md`" points into the research dossier.

**Non-negotiables. Violating any of these is a bug.**
1. **Safety never touches the network.** Pose, obstacle detection, warnings, passthrough escalation, gain clamping and Safe Harbour run on the phone, every frame. The `Atlas.Safety` assembly MUST NOT reference `Atlas.Link`.
2. **Locked 60 fps in stereo** on the slower phone (iPhone 14 Pro Max). The only sanctioned drop is the deliberate 30 fps render / 60 Hz warp thermal tier.
3. **Every hand technique has a twin input** (gamepad, gaze dwell, cube or console). The demo MUST be completable with walk + look + one button.
4. **Every curved view has a flat twin** (map, list, chart).
5. **Safe Harbour** (passthrough + room outline, all gains = 1) is always ≤300 ms away.
6. **Gains start at identity.** Nothing curves or rotates covertly on a first-time user.
7. **No clinical claims** in UI strings or logs (Brief §2.4).
8. **No camera frames are stored** from judges or visitors. Process in RAM only (the team's own ARKit test recordings are the only exception, §15.5).

## 1. Engineering summary

### 1.1 What we are building

ATLAS ships as **five deliverables**:

| Deliverable | Platform | Built with | Role |
|---|---|---|---|
| **Phone app** | iOS 17+ (iPhone 15 Pro Max = phone A, 14 Pro Max = phone B) | Unity 6.3 LTS, URP, AR Foundation (ARKit), Swift plugins | Everything the judge sees and does: tracking, safety, stereo rendering, interaction, blades, logging |
| **Partner client** | Android (Galaxy S24) | Same Unity project, AR Foundation (ARCore), minimal scene | House Call option: partner pose, avatar, voice |
| **PC server + web console** | Windows (RTX 5070) | Python 3.11, FastAPI, msgpack; web app (TypeScript + D3) | Real-time link, asset service, Roll a Lung builds, object meshes, operator console, dashboard, logging |
| **Spectator renderer** | Windows | Same Unity project, `ATLAS_SPECTATOR` build | Re-renders the judge's view from replicated state for the Knife Map screen |
| **Pipelines + batch jobs** | PC and H200 cluster | Python package `atlas-pipelines`, Apptainer, Slurm | Case cards (segmentation, completion, H² layout, meshes, tubes, sky volumes), greedy study, asset kit |

The live experience these must deliver (one patient story through five geometries, then the judge's choice) is specified in Brief §1.3 and Brief §9.3; this spec is how to build it.

### 1.2 Decisions (shared with the design brief)

| # | Decision | Choice | Why (evidence) | Fallback or alternative |
|---|---|---|---|---|
| D1 | What the project *is* | Platform + interaction design space + blades (Swiss Army knife) | The lead's direction; strongest HCI framing; scores breadth *and* depth | One deep blade only |
| D2 | Hackathon blades and live path | **Live story path (Tier A):** Hub + Bend Your Room → Breath Room (Glued Room engine) → Lung airway (Roll a Lung) → needle sky → Post Circling (solo, pre-scanned) → judge's choice of FieldFit or House Call. **Also Tier A:** the classic Glued Room (public loop between judges). **Tier B** (video in Q&A): Station Zero, The Branch / Deep Time, Small Planet, Manifold Lab, C-Space Room | One patient story across five geometries answers the "menu of mini-demos" risk; every slot still shows E, H, S, wrapped, branched and 4D-time; the core path needs no network | 5-min slot: skip the judge's choice; skip Post Circling if FMS ≥2 |
| D3 | Phones | **Phone A: iPhone 15 Pro Max** (LiDAR, A17 Pro) in the headset. **Phone B: iPhone 14 Pro Max** (LiDAR, A16) for hot-swap and backup only (a second judge when idle). **Galaxy S24 / S24 Ultra** takes the judge's object photos and is the live House Call partner (§4.1) | LiDAR depth, mesh, RoomPlan and Object Capture cover all sensor features on both iPhones; the 15 Pro Max is ≈10–26% faster under sustained GPU load [O] | Android (ARCore) build on the S24 as a secondary target |
| D4 | Engine | **Unity 6 LTS, URP, AR Foundation 6 (ARKit), IL2CPP**, plus Swift native plugins (Vision hands, RoomPlan, Object Capture, Core Haptics, thermal) | Fastest path to production quality; cross-platform; glTFast/AR Foundation ecosystem; all hot paths are GPU shaders or native code | Native Swift + Metal port of the kernel later if profiling demands it (§4.3) |
| D5 | Stereo | **One camera, two eyes:** AR Foundation owns tracking; one URP camera with a render-graph pass draws both eyes into a side-by-side target (one cull with a frustum enclosing both eyes; portal views as RendererLists, clipped in the shader); distortion from a Cardboard viewer profile | Unity's single-pass instanced stereo is offered for Android multiview, PSVR and Windows, not iOS ([Unity](https://docs.unity3d.com/Manual/SinglePassStereoRendering.html)); two eye cameras plus a camera per portal view would be CPU-bound | Two-camera rig; Cardboard XR plugin with native ARKit pose timeboxed to 1 day. Decide by spike 0.15 |
| D6 | Compute split | **Phone renders everything locally.** RTX 5070 PC = real-time server, operator console, spectator renderer, light AI. **H200 cluster = offline and batch** (best models, dataset processing, studies), cached before the demo | Streaming VR video from a PC measures 50–150 ms vs a ~20 ms comfort budget [M]; no maintained open-source iPhone VR streamer exists | — |
| D7 | Inputs | Must-have: real walking, head-gaze reticle, gamepad A/B (fallback: headset lever tap, then dwell), operator console. Showcase: **Vision hand pose + LiDAR depth**, **printed marker cube**, real furniture as passive haptics | The camera cone is only ≈72°×57°; hand pinch is unreliable in Cardboard-class studies [M]; the cube tracks every frame [O] | Every hand technique has a twin |
| D8 | Locomotion in 3×3 m | **Topology first** (portals, gluing, sheets, hyperbolic tiling, holonomy). Redirection gains off by default, within thresholds if used | Gain-only redirected walking needs ≈6×6 m [M]; topology gives "impossible" with zero visual-vestibular conflict | 2:1 turn resets at walls |
| D9 | Curved rendering | Hyperboloid/sphere storage → **Klein (projective) chart in the vertex shader**; **H²×E** for anything walked; geodesic fog; **stereo mode chosen per wing** by spike 0.7 (Klein per eye = no subdivision but depth compressed ≈IPD/R; equidistant = correct depth but edges subdivided offline to ≲0.2R) | In the Klein chart, straight edges stay straight; the floor stays put (dossier → `rendering_math_4d.md`) | Ray-marching only for one exotic showcase room |
| D10 | Portals | **Stencil portals** drawn as extra RendererLists of the one stereo camera, per-eye matrices, recursion ≤2, clipping at the portal plane in the shader, portal *box* for the eye straddling a portal | 2.3× (up to 3.97×) faster than render textures; lighting works [M] | Render texture at half resolution, depth 1 |
| D11 | Scanning | Rooms: **RoomPlan → parametric → rebuilt from our modular kit** (real furniture becomes walls and haptic proxies; real doors become portals). Objects: **Object Capture (Mac) for fidelity; SPAR3D or Hunyuan3D-2mini on the 5070 for speed; TRELLIS.2 on the H200 for best quality;** commercial API fallback | 5070 = 12 GB (TRELLIS.2 needs ≥24 GB); raw scans look incoherent, so re-instantiate and stylise | Pre-generated assets labelled "pre-baked" |
| D12 | Networking | WebSocket (control), UDP (pose 30–60 Hz), HTTP (assets), MessagePack; own 5/6 GHz router; `iproxy` over USB for development | Simple, works on both OSes, honours iOS Local Network rules (dossier → `phone_pc_link.md`) | Pose over WebSocket when on USB |
| D13 | Medical data | Public and de-identified only: LIDC-IDRI, TCIA 4D-Lung (CC BY 3.0), TotalSegmentator (Apache-2.0) | Licences permit derived meshes; "not for clinical use" | Synthetic cases (MAISI on H200) |
| D14 | Claims | Education, training and research visualisation; measured, not asserted (Brief §2.4, Brief §8) | FDA General Wellness guidance (Jan 2026) and Health Canada SaMD guidance | — |
| D15 | Geometry strategy | **Portals + curved wings:** flat charts (hub, glued rooms, house sheets) joined by isometric gluings; constant-curvature wings (H², S², H³, S²×E) only where the content is curved | Keeps the scanned room 1:1, allows every geometry type, lets R be chosen per wing for comfort (Brief §7.13) | **"True curved space only"** (one continuous H²×E/S²×E world, Holonomy-style tile mapping) documented in Brief §7.13 as a possible later direction; decide with spike 0.14 (run after Phase 2) |
| D16 | Latency | Predict the head pose to photon time with Core Motion (100–200 Hz) from the ARFrame timestamp, and apply a late rotational warp in the final distortion pass; warp passthrough together with the overlays | The ARKit pose is the capture-time pose at camera rate; Unity's late latching isn't available on this path [verify] | Spike 0.16: rotational motion-to-photon ≤30 ms |
| D17 | Procedural generation in the live demo | **Roll a Lung** (§11.3.1): the judge picks a pre-segmented case; the PC completes distal airway generations with a published space-filling model, lays the tree out in H² and assembles kit chambers in seconds; completed branches are labelled "model-generated" | Makes procedural non-Euclidean generation visible and honest, and fills the "segmentation stops short" gap | Curated default case cached on the phone |
| D18 | Throughline | One patient story (Bend → Breath Room → airway → sky → Post Circling), every step in a different geometry, ending in a plan card | Purpose stays clear in 5 minutes while breadth stays visible | Separate blades in the 10-min slot only |
| D19 | iOS signing | **Free Apple account in Xcode ("Personal Team")**: builds signed by one owner account on the M3 Max and installed over USB; one fixed bundle ID; reinstall ≤24 h before an event | No enrolment, fee or review wait; covers both headset iPhones (3-device limit); the 7-day build lifetime is longer than our rebuild cycle | Paid Apple Developer Program (US$99/yr; individual approval usually ≈48 h) if spike 0.26 fails or installs become a bottleneck |

### 1.3 Hardware and each device's role

| Device | Role |
|---|---|
| **iPhone 15 Pro Max** in a camera-window headset | Everything the user sees and does: ARKit 6DoF, LiDAR depth and mesh, stereo rendering at 60 fps, Vision hands, image-tracked cube, RoomPlan and Object Capture *capture*, safety, local planners, logging |
| **MacBook M3 Max** | Xcode and iOS builds; Object Capture *reconstruction* (PhotogrammetrySession); AirPlay receiver as a spectator fallback; optional MPS models |
| **Windows PC, RTX 5070 (12 GB, Blackwell)** | Real-time server (FastAPI: WebSocket, UDP, HTTP); operator console; spectator renderer and "Knife Map" dashboard; logging; light AI (SPAR3D, TotalSegmentator, BioCLIP 2, small LLM); asset optimisation (gltfpack, KTX2). Needs **PyTorch built for CUDA 12.8+ (cu128 wheels, PyTorch ≥2.7)**; older repos with custom CUDA kernels may need rebuilding for sm_120 [U] |
| **H200 cluster** (141 GB HBM3e each) | Batch and offline: TRELLIS.2 asset kit and hero objects; segmentation of whole CT collections; the hyperbolic-vs-Euclidean greedy-routing study; Teichmüller lens atlas over 28,943 visual fields; Orb-v3 molecular dynamics, Earth2Studio forecasts and LLM steering for Tier B blades; OpenRDW simulation sweeps. *Live* use only if the cluster allows interactive nodes reachable from the venue, and always with a cached fallback |
| **iPhone 14 Pro Max** (phone B) | Hot-swap and backup for phone A (kept cool); second judge on short blades when idle; second developer device. Same app, same quality tier as phone A |
| **Samsung Galaxy S24 / S24 Ultra** | Photographs the judge's object for the live mesh; House Call partner client; Android build test; flat-mode citizen-science client |

### 1.4 Readiness: what is planned vs the full idea

Grades: ● build-ready (inputs, outputs, approach, formats and an acceptance test) · ◐ specified (approach and parameters; some interfaces or edge cases still to write) · ○ concept (described, not specified).

| Area | The full idea | What the build plan delivers for the event | Readiness | Phase |
|---|---|---|---|---|
| Geometry kernel | 8 kinds of space (E, P, T, H, S, B, 4s, 4t) + conformal warp under one interface | All 8 + warp; the live path uses E, P, T, H, S, B and 4t | ◐ formulas, precision rules and identity tests defined; method signatures to write | 1–4 |
| Rendering | Stereo, portals, curved space, 4D, sky, lens at 60 fps | One-camera side-by-side stereo, stencil portals, Klein H²×E, 4D slicer, sphere-traced sky, single final pass with late warp; frame budget (§5.3) | ● portals; ◐ rest (spikes 0.7, 0.15–0.17, 0.20) | 0–2 |
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
| Engineering | Optimised and solid | Frame budget, tests, CI/CD, perf regression, memory budget, kill switches, crash reporting (§15.5) | ◐ specified | 0–1 |
| Demo operations | A live demo judges can try | Run-of-show ≤5:00, roles, fallbacks, rehearsals | ● | 4 |

**Deliberately not built for the event:** general procedural worlds, Tier B/C blades (video and docs only), H³ and S³ rooms, covering-space co-walking, full Android parity, and the studies.

## 2. Development environment and setup

### 2.1 Accounts, machines and roles

| Item | Use |
|---|---|
| **Apple account (free)**, signed in to Xcode on the M3 Max | Signs and installs the iOS builds as an Xcode **Personal Team** (D19, §2.8). No enrolment, fee or review wait. The paid Apple Developer Program (US$99/yr) is a fallback only |
| **GitHub** org + repo (Git LFS for binaries) | Source, issues, CI; self-hosted runner on the M3 Max |
| **Unity account** | Editor licence per Unity's current terms [verify] |
| **H200 cluster account** | Slurm queue, project storage, container runtime |
| **MacBook M3 Max** | Unity Editor, Xcode, iOS builds, CI runner, Object Capture reconstruction (optional) |
| **Windows PC, RTX 5070** | Server, GPU workers, web console host, spectator build (Unity Editor for Windows) |
| **Devices** | iPhone 15 Pro Max (A), iPhone 14 Pro Max (B), Galaxy S24 (partner, object photos), 2 headsets + Cardboard v2, 2 Bluetooth gamepads, printed cubes, Wi-Fi 6E router |

### 2.2 Versions to pin

| Component | Version | Notes |
|---|---|---|
| Unity | **6000.3 LTS** (Unity 6.3) | Pin the exact patch in `ProjectSettings/ProjectVersion.txt` ([Unity 6.3 LTS](https://unity.com/blog/unity-6-3-lts-is-now-available)) |
| URP | the 17.x release bundled with 6000.3 [verify] | Render Graph on (compatibility mode off) |
| AR Foundation, Apple ARKit XR Plugin, Google ARCore XR Plugin | the 6.x line verified for 6000.3 (6.3 docs) [verify] | No XR display provider: we render stereo ourselves (§5.2) |
| Xcode / iOS SDK | the Xcode release Unity 6000.3 supports for the iOS 26/27 SDK [verify] | Built on the M3 Max |
| iOS deployment target | **17.0** | Needed for `RoomCaptureSession(arSession:)` and Vision compute-device selection |
| Android | API level of the Galaxy S24 (ARCore supported) | Partner client only |
| Cardboard SDK | **v1.35.0** (C API, optics only) | [googlevr/cardboard](https://github.com/googlevr/cardboard) |
| Python | **3.11** | Server and pipelines |
| PyTorch | **≥2.7, CUDA 12.8 (cu128)** on the RTX 5070 | Blackwell (sm_120) needs cu128 wheels |
| Node | current LTS | Builds the web console only (Vite) |

### 2.3 Unity packages

| Package (ID or repo) | Licence | Purpose |
|---|---|---|
| `com.unity.render-pipelines.universal` | Unity | URP, Render Graph, renderer features |
| `com.unity.xr.arfoundation`, `com.unity.xr.arkit`, `com.unity.xr.arcore` | Unity | Tracking, depth, meshing, image tracking, camera frames |
| `com.unity.xr.management` | Unity | Loads ARKit/ARCore only (no stereo display provider) |
| `com.unity.inputsystem` | Unity | Gamepad, touch (headset lever) |
| `com.unity.burst`, `com.unity.collections`, `com.unity.mathematics` | Unity | Kernel maths (double precision), safety jobs |
| `com.unity.cloud.gltfast`, `com.unity.meshopt.decompress`, `com.unity.cloud.ktx` | Apache-2.0 / Unity | GLB + meshopt + KTX2 loading ([glTFast](https://github.com/Unity-Technologies/com.unity.cloud.gltfast)) |
| `com.unity.test-framework`, `com.unity.testframework.graphics` | Unity | Edit/Play-mode tests, golden images |
| `com.unity.profiling.core` | Unity | ProfilerMarkers/Counters per pipeline stage |
| [Cysharp/UniTask](https://github.com/Cysharp/UniTask) | MIT | Allocation-free async (asset loads, networking) |
| [MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp) v3 | MIT | Wire format; AOT-safe source-generated formatters for IL2CPP (install per its Unity section, e.g. NuGetForUnity) |
| [apple/unityplugins](https://github.com/apple/unityplugins) | Apache-2.0 | Apple.Core, GameController, CoreHaptics (PHASE optional) |
| [livekit/client-sdk-unity](https://github.com/livekit/client-sdk-unity) | Apache-2.0 | Voice for the House Call option |
| [vrm-c/UniVRM](https://github.com/vrm-c/UniVRM), [hecomi/uLipSync](https://github.com/hecomi/uLipSync) | MIT | Partner avatar and lip-sync (House Call option) |
| [ValveSoftware/steam-audio](https://github.com/ValveSoftware/steam-audio) | Apache-2.0 | Spatial audio (optional; simple panning first) |
| [UXF](https://github.com/immersivecognition/unity-experiment-framework) | MIT | Optional trial/pose logging helpers |
| Sentry Unity SDK (or Crashlytics) | MIT | Crash reporting with offline queue |

### 2.4 Native iOS plugin (Swift)

- **Location:** `Assets/Plugins/iOS/AtlasNative/` (Swift sources); C entry points exported with `@_cdecl`, called from C# with `[DllImport("__Internal")]`. One `AtlasNative` façade class per feature.
- **Getting the ARKit objects:** `XRSessionSubsystem.nativePtr` and `XRCameraFrame.nativePtr` point to small versioned structs holding the `ARSession` and `ARFrame` pointers (see AR Foundation's ARKit native-pointer docs) [verify layout for the pinned version].

| Entry point (C) | Does | Notes |
|---|---|---|
| `atlas_vision_start(session, hz)` / `atlas_vision_poll(out HandFrame*)` | `VNDetectHumanHandPoseRequest` on `ARFrame.capturedImage`, LiDAR lift, latest-only | Neural Engine via `setComputeDevice`; release the pixel buffer immediately |
| `atlas_humans_poll(out HumanBoxes*)` | `VNDetectHumanRectanglesRequest` at 5–10 Hz | Safety input |
| `atlas_motion_start(hz)` / `atlas_motion_predict(t_target, out float4 q)` | `CMMotionManager` device motion at 200 Hz; integrates gyro from the ARFrame timestamp to the predicted photon time | Same clock as `ARFrame.timestamp` (system uptime) |
| `atlas_roomplan_begin(session)` / `atlas_roomplan_end(path)` | `RoomCaptureSession(arSession:)`, `stop(pauseARSession: false)`, export JSON + USDZ, save `ARWorldMap` | Spike 0.18 |
| `atlas_worldmap_save(path)` / `atlas_worldmap_load(path)` | Relocalisation | Plus floor-marker re-anchor |
| `atlas_thermal_state()` | `ProcessInfo.thermalState` | Polled + notification |
| Haptics | Apple.CoreHaptics plugin | Transient 20–50 ms pulses |

- **Info.plist (set by a PostProcessBuild script):** `NSCameraUsageDescription`, `NSLocalNetworkUsageDescription`, `NSBonjourServices` (`_atlas._tcp`), `NSMicrophoneUsageDescription` (House Call voice), `UIRequiresFullScreen`, landscape-only orientation.
- **Entitlements:** `com.apple.developer.kernel.increased-memory-limit`, only if the free Personal Team accepts it (spike 0.26); otherwise none, and the 1.8 GB budget is held without it.
- **Build settings:** IL2CPP, ARM64, Metal only, Swift version set, `ENABLE_BITCODE = NO`.

### 2.5 PC server and pipelines (Python)

- **Layout:** `server/` (FastAPI app) and `pipelines/` (CLI `atlas`), each with a `pyproject.toml`; lock files committed (uv or pip-tools).
- **Server packages:** `fastapi`, `uvicorn[standard]`, `msgpack`, `pydantic` v2, `numpy`, `qrcode`, `zeroconf` (mDNS).
- **Pipeline packages:** `numpy`, `scipy`, `scikit-image`, `SimpleITK`, `pydicom`, `nibabel`, `rt-utils` (MIT, RTSTRUCT → masks), `TotalSegmentator` (Apache-2.0), `monai` (Apache-2.0), `skan` (BSD-3) for skeleton graphs, `networkx`, `trimesh` (MIT), `pygltflib`, `astropy-healpix` (BSD-3), background removal (`rembg` or SAM 3, gated licence), SPAR3D (Stability AI Community License).
- **Avoid in shipped code:** kimimaro (GPL-3.0) and healpy (GPL-2.0); internal experiments only, recorded in the licence ledger.
- **CLI tools on PATH:** `gltfpack` ([meshoptimizer](https://github.com/zeux/meshoptimizer)), `toktx` ([KTX-Software](https://github.com/KhronosGroup/KTX-Software)), `dcm2niix`; Blender (optional, kit authoring).
- **Windows GPU:** current NVIDIA driver with CUDA 12.8; PyTorch cu128; one GPU worker process owns the card.

### 2.6 H200 cluster

- **Containers:** an Apptainer image `atlas-pipelines.sif` built from the same `pipelines/pyproject.toml` (plus TRELLIS.2 and MONAI weights).
- **Jobs:** Slurm scripts in `hpc/` (array jobs over cases): segment, complete + layout, tube + ITV, sky volumes, greedy study, TRELLIS.2 asset kit (§12.2).
- **Data:** inputs and outputs in project storage under `/project/atlas/`; results synced to the PC's `cache/cases/` with rsync after each run.
- **Policy:** confirm batch-only vs interactive access early (spike 0.12); the live demo never depends on the cluster.

### 2.7 First-build checklist (day 1)

1. Create the Unity 6000.3 project from the URP template; add the packages in §2.3; commit with Git LFS rules for `*.glb`, `*.ktx2`, `*.png`, `*.fbx`.
2. **Player settings (iOS):** IL2CPP, ARM64, Metal, target iOS 17.0, landscape left only, **Frame Timing Stats on**, `Application.targetFrameRate = 60`, VSync count 1, Managed Stripping Level Low (raise later with link.xml).
3. **XR Plug-in Management:** ARKit on for iOS, ARCore on for Android; **no** XR display provider.
4. **Signing (free account, §2.8):** sign in to Xcode with the owner's Apple account (Xcode › Settings › Accounts); in Unity's Player Settings set **Signing Team ID** to that Personal Team and tick **Automatically Sign**; fix the bundle ID as `com.<owner>.atlas`. On each iPhone: pair with the Mac, turn on Developer Mode, and after the first install trust the developer (Settings › General › VPN & Device Management).
5. Build an empty AR scene (ARSession + XR Origin) to both iPhones from the M3 Max; confirm camera permission, tracking and the depth image. Try the Increased Memory Limit capability now (spike 0.26).
6. Add the Cardboard C API static library; scan the headset's viewer-profile QR; render a straight-line test pattern through the distortion meshes (spike 0.13).
7. Start the FastAPI server on the PC; connect over USB with `iproxy 8765:8765`, then over Wi-Fi; see `Pose` packets in the console.
8. Wire CI (§15.5): commit → Unity batch build → `xcodebuild` signed by the Personal Team → `devicectl` install on both phones in ≤20 min.

**Fast inner loop.** Work that doesn't need tracking runs in the Editor with AR Foundation's XR Simulation environments; kernel maths runs as EditMode tests and in the PC tools; on the phone, stay on USB with `iproxy` so the console shows frame times live, and replay recorded ARKit walks (team-only) to repeat tracking bugs.

**Things that bite on day 1.** The Local Network prompt must be accepted again after a reinstall (spike 0.21). Free-account builds stop launching 7 days after install (rebuild or reinstall; §2.8). Retained `ARFrame`s stall the camera after ≈15–20 frames. PyTorch on the RTX 5070 needs cu128 wheels; older repos with custom CUDA kernels may need rebuilding for sm_120.

### 2.8 iOS signing with a free Apple account (Xcode Personal Team)

We sign and install the iPhone builds with a **free Apple account** in Xcode, which Xcode calls a **Personal Team** (D19). There is no enrolment, fee or review wait. The paid Apple Developer Program is a fallback, not the plan.

**Owner.** One teammate's Apple account (two-factor on) is signed in to Xcode on the M3 Max (Xcode › Settings › Accounts) and signs every build for phone A and phone B. Teammates who test on their own iPhones use their own free accounts.

**Free-account limits and our rules**

| Limit | Rule |
|---|---|
| Builds stop launching **7 days** after install (the provisioning profile expires) | Every rebuild renews it. Before an event, install on both phones ≤24 h before day 1; the pre-session launch check (§14.2) catches an expired build |
| 3 registered devices | Only phone A and phone B on the owner's account |
| 3 apps per device | ATLAS plus at most one test app |
| 10 new App IDs per 7 days | One fixed bundle ID, `com.<owner>.atlas`, for the life of the project; no throwaway projects on this account |
| No TestFlight, App Store Connect or developer portal | Installs go from the shared Mac over USB (or network pairing); Xcode manages certificates and profiles automatically |
| Some capabilities aren't offered to Personal Teams | The only one we request is Increased Memory Limit (`com.apple.developer.kernel.increased-memory-limit`). Spike 0.26 checks it; if refused, drop it and pass spike 0.22 (peak ≤1.8 GB on phone B) without it |

**Build → phone.**
1. Unity batch build for iOS with **Signing Team ID** = the Personal Team and **Automatically Sign** on, so the generated Xcode project needs no manual signing.
2. On the M3 Max: `xcodebuild -project Unity-iPhone.xcodeproj -scheme Unity-iPhone -configuration Release -destination 'generic/platform=iOS' -derivedDataPath build -allowProvisioningUpdates build` (uses the Apple account signed in to Xcode) [verify in spike 0.24].
3. `xcrun devicectl device install app --device <udid> build/Build/Products/Release-iphoneos/<Product>.app` for each phone connected to the Mac (`xcrun devicectl list devices` shows the IDs).
4. Fallback: open the project in Xcode and press Run.

**One-time set-up per phone.** Pair with the Mac (Trust This Computer) → Settings › Privacy & Security › Developer Mode → restart → Enable → after the first install, Settings › General › VPN & Device Management › the owner's account › Trust.

**Event procedure.** Freeze the golden build. The evening before day 1, install it on both phones and launch each once with Wi-Fi off. Bring the Mac and both cables (a reinstall takes ≈5 min). A two-day event needs no reinstall in between.

**When to switch to paid** (US$99/yr; individual approval usually ≈48 h, not guaranteed):
- spike 0.26 fails *and* the app can't hold 1.8 GB without the capability;
- more devices or testers are needed than the free limits allow;
- manual installs become a bottleneck.

Enrol as an individual ≥2 weeks before the event. Switching takes 15–30 min: change the team in Unity and Xcode, delete the free-signed copy from each phone (a different team can't install over it), add the capability, rebuild. Step by step: Appendix E.

## 3. System architecture

### 3.1 Overview

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

### 3.2 Module map (Unity assemblies)

| Assembly (`.asmdef`) | Contents | May reference |
|---|---|---|
| `Atlas.Kernel` | `IGeometry` and implementations (E, H, S, T, B, P, 4D slicer, warp), double-precision maths, identity checks | Unity.Mathematics, Burst, Collections (no scene types, so EditMode-testable) |
| `Atlas.World` | Charts, gluings, room graph, per-visit embedding and tile solver, style cascade, case-package loader | Kernel |
| `Atlas.Rendering` | Stereo renderer feature, eye passes, portal pass, `CurvedSpace.hlsl`, fog, `Slicer4D.compute`, `HealpixSky.compute`, final composite (distortion, CA, passthrough, LUT, late warp) | Kernel, World, URP |
| `Atlas.Interaction` | Technique registry (G·B·M·F·T·P code), input channels and twins, One-Euro filter, gesture detectors (incl. Geometry Lens) | Kernel, World, Platform |
| `Atlas.Safety` | Depth sectors, mesh proximity grid, play-area outline, human detection client, escalation state machine, Safe Harbour | Platform, Kernel (**never Link**) |
| `Atlas.Link` | WebSocket/UDP/HTTP client, generated message types, clock sync, watchdog, asset cache | — |
| `Atlas.Telemetry` | Ring-buffer logger, `KnifeState`, `StateSync`, probes (FMS, SEQ, pointing) | Link |
| `Atlas.Blades.*` | Hub (+ Bend Your Room), GluedRoom, BreathRoom, Lung, SharedHouse, FieldFit | all of the above |
| `Atlas.Platform.iOS` / `.Android` | Native bridges (Vision, Core Motion, RoomPlan, world map, thermal, haptics) | — |
| `Atlas.Spectator` | Windows only: `StateSync` consumer, mirror camera, chart strip | Rendering, World, Link |

**Rules.** Dependencies only point down this table (Spectator aside). `Atlas.Safety` cannot see the network by construction. `Atlas.Kernel` has no `UnityEngine.Object` dependencies, so its identities run in EditMode tests and in the PC tools.

### 3.3 Phone frame loop (order matters)

1. **Poll inputs:**
   - ARFrame: pose, sceneDepth, tracked images, tracking state;
   - gamepad;
   - latest Vision result (async, ≤30 Hz).
2. **Safety:**
   - LiDAR forward-cone sector minima;
   - mesh proximity;
   - person detection (async 5–10 Hz);
   - tracking-quality check.
   - Escalate if needed (§7.2). Safety may freeze locomotion transforms and force passthrough.
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

### 3.4 Architecture principles: what the phone decides and what the PC may suggest

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

### 3.5 Core interfaces

These are the seams between modules. Names are binding; signatures may be refined in Phase 1.

```csharp
// Atlas.Kernel
public enum GeometryKind { Euclid, Hyperbolic, Spherical, Quotient, Branched, Portal, Space4, Time4 }

public readonly struct ChartPoint {            // position = chart id + local coords (never a global float)
    public readonly ChartId Chart;
    public readonly double4 Local;             // hyperboloid/sphere/affine coords in the chart
    public readonly int Sheet;                 // branched covers; 0 otherwise
}

public interface IGeometry {
    GeometryKind Kind { get; }
    double R { get; }                          // curvature radius; +inf when flat
    ChartPoint Exp(in ChartPoint p, in double3 v);           // step, reach, ray
    double3   Log(in ChartPoint p, in ChartPoint q);         // direction × geodesic distance
    double    Distance(in ChartPoint p, in ChartPoint q);
    double3x3 Transport(in ChartPoint p, in double3 v);      // frame rotation along v (holonomy)
    int       Images(in ChartPoint p, Span<ChartPoint> dst); // copies in quotients / covers
    bool      Crosses(in ChartPoint a, in ChartPoint b, out Gluing g); // wall, portal or cut crossed
}

public sealed class Gluing {                   // edge of the world graph
    public ChartId From, To;
    public double4x4 Transform;                // affine / SO+(3,1) / SO(4) as a 4x4
    public GluingKind Kind;                    // Portal, Wall, BranchCut, Hatch, TimeSeam
    public int[] SheetPermutation;             // branched covers (sigma)
}

public interface ISlicer4D { void SetSlice(quaternion normal, float w0); }   // normal = cube's q

// Atlas.Interaction
public interface ITechnique {
    string Id { get; }                          // e.g. "X15"
    TechniqueCode Code { get; }                 // G·B·M·F·T·P, logged in KnifeState
    void Tick(in InputFrame input, IWorldContext world);
    EquationCard Equation { get; }              // template + live values for the Knife Map
}

// Atlas.Blades
public interface IBlade {
    string Id { get; }
    UniTask Load(CasePackage pkg, CancellationToken ct);  // assets off the render thread
    void Enter(ChartPoint start);
    void Tick(in FrameContext frame);
    void Exit();
    BladeState Snapshot();                      // replicated to the spectator (StateSync)
}

// Atlas.Safety
public enum SafetyLevel { Clear, Notice, Warn, Stop, Harbour }
public interface ISafetyMonitor {
    SafetyLevel Level { get; }
    void Tick(in SensorFrame s);                // called first in the frame, never awaits
    event Action<SafetyLevel, float3> Changed;  // level + nearest obstacle vector
}
```

## 4. Platform and stack

### 4.1 Phones

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
1. **Heat swap.** Phones throttle to about 50% of peak GPU after 40 minutes of stress, worse inside a closed headset. Swap between judges by the single rule in §5.4 (thermal state "fair" or 30 min); in-session, drop a tier.
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
- **Signing (D19):** a free Apple account in Xcode (Personal Team) signs and installs the builds. They stop launching 7 days after install, which is longer than our rebuild cycle; install ≤24 h before an event. The paid Apple Developer Program (US$99/yr) is the fallback (§15.5).
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

### 4.2 Headset requirements (buy two)

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

### 4.3 Engine and rendering stack

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

### 4.4 Stack table (with licences)

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

### 4.5 Input stack and the 20-second gesture set

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

## 5. AR/VR rendering pipeline

### 5.1 AR session configuration

| AR Foundation component | Setting | Why |
|---|---|---|
| `ARSession` + `XROrigin` | World tracking, 6DoF | Pose for walking |
| `ARCameraManager` | Highest 60 fps configuration (e.g. 1920×1440 @ 60); autofocus on; **no `ARCameraBackground` in VR scenes** | Camera is composited in the final pass (§5.2) |
| `AROcclusionManager` | Environment depth on, temporal smoothing on (`smoothedSceneDepth`), confidence on | Safety and hand lift; real update ≈15 Hz |
| `ARMeshManager` | **On during mapping only** (first 30–60 s), classification on; then off | Obstacle grid; heat |
| `ARPlaneManager` | Horizontal, during calibration only | Floor height |
| `ARTrackedImageManager` | Library: 6 cube faces + floor marker; ≤2–3 moving images | Cube input; re-anchor |
| World map | Save after the venue scan; load at app start; re-anchor on the floor X | Relocalisation (spike 0.18) |

**Per-frame pose path:** ARKit pose (capture time) → Core Motion prediction to photon time (`atlas_motion_predict`) → render pose for both eyes; the latest prediction is applied again as the late warp in the final pass (§5.2).

### 5.2 Stereo, final pass, late warp and passthrough (implementation)

**Why custom.** Unity's single-pass instanced stereo is only offered through XR display providers on Android multiview, PSVR and Windows ([Unity](https://docs.unity3d.com/Manual/SinglePassStereoRendering.html)), and the Cardboard display provider is rotation-only. So ATLAS renders stereo itself with **one camera** and a URP renderer feature.

**Render targets.**
- One side-by-side colour target, 2 × (1216 × 1120) at the nominal tier (≈0.87 of panel resolution per eye), **4× MSAA memoryless** colour and depth, resolved to a non-MSAA texture for the final pass.
- Tiers scale the eye size 0.87 → 0.75 → 0.65 and MSAA 4× → 2× (§5.4).

**Render-graph passes (URP `ScriptableRendererFeature`, `RecordRenderGraph`).**

| # | Pass | Does |
|---|---|---|
| 1 | `AtlasCull` | One cull with a frustum enclosing both eyes; visible portals and glued copies collected from the room graph |
| 2 | `AtlasEye` (left, right viewports) | Opaque home chart → portal passes (RendererLists per visible portal, stencil ref per portal, depth ≤2, clip plane in the shader) → curved-space materials (`CurvedSpace.hlsl`) → fog/transparent → overlays (reticle, safety, caption chip) |
| 3 | `AtlasCompute` | Blade compute: needle-sky slice (≤1.5 ms) or 4D slicer (prefix-sum compaction, indirect draw) |
| 4 | `AtlasFinalComposite` | To the backbuffer at panel resolution: two Cardboard distortion meshes; vertex shader applies the late warp; fragment samples the eye texture with per-channel radial chromatic-aberration scale; composites passthrough and the FieldFit LUT; colour LUT; lens mask |

**Cardboard optics (C API, [cardboard.h](https://raw.githubusercontent.com/googlevr/cardboard/master/sdk/include/cardboard.h)).** Saved viewer parameters (QR) → lens-distortion object for the panel size → per eye: eye-from-head matrix, projection/FOV, distortion mesh. Meshes are uploaded once.

| Step | Call (names checked against `cardboard.h`, master) |
|---|---|
| Pair the headset (once, in settings) | `CardboardQrCode_scanQrCodeAndSaveDeviceParams()`; re-read when `CardboardQrCode_getDeviceParamsChangedCount()` changes |
| Load the profile at start | `CardboardQrCode_getSavedDeviceParams(&params, &size)` (fallback `CardboardQrCode_getCardboardV1DeviceParams`), then `CardboardQrCode_destroy(params)` |
| Create the lens model | `CardboardLensDistortion_create(params, size, display_width_px, display_height_px)` |
| Per eye (`kLeft`, `kRight`) | `CardboardLensDistortion_getEyeFromHeadMatrix`, `CardboardLensDistortion_getProjectionMatrix(z_near, z_far)` or `CardboardLensDistortion_getFieldOfView`, `CardboardLensDistortion_getDistortionMesh(&mesh)` → copied into a Unity `Mesh` once |
| Late warp input | Each mesh vertex's UV (into the undistorted eye image) plus that eye's field of view gives its tan-angle direction; precompute it into a vertex attribute |
| Not used | `CardboardHeadTracker_*` (ARKit gives 6DoF) and `CardboardDistortionRenderer_*` / `CardboardMetalDistortionRenderer_create` (our final pass draws the meshes so it can add the late warp, passthrough and LUTs) |

**Late rotational warp (D16).**
1. Render both eyes with the pose predicted for the expected photon time.
2. Just before the final pass, read the newest prediction; ΔR = R_render⁻¹ · R_latest.
3. In the distortion-mesh vertex shader, take the vertex's undistorted eye-space direction d (from its tan-angle coordinates), rotate it by ΔR⁻¹ (sign confirmed in spike 0.16), and project with the eye's render projection to get the UV into that eye's half of the colour target.
4. Rotation only (no positional reprojection); this removes most rotational latency at negligible cost.

**Passthrough and Safe Harbour.** ARKit's camera image (Y and CbCr textures, native Metal textures) is undistorted with that frame's intrinsics, mapped onto a plane at 1.5–2 m in eye space (optionally offset per pixel from `smoothedSceneDepth`) and warped by the same ΔR. Safe Harbour draws passthrough plus the room outline and play-area ring, with all gains forced to 1.

**Shader and kernel files (`Assets/Atlas/Rendering/`).**

| File | Kind | Does |
|---|---|---|
| `AtlasUber.shader` | Lit material | Kit and scanned objects: baked light probes, colour grading folded in, outlines; SRP Batcher compatible; target of the glTFast material generator |
| `CurvedSpace.hlsl` | Include | One 4×4 isometry, the Klein divide (x/w, y/w, z/w), then the eye matrix; equidistant option per wing (§5.3) |
| `GeodesicFog.hlsl` | Include | Fog by geodesic distance d = R·atanh(\|x′\|/w′); hard draw radius 3–4R |
| `PortalStencil.shader` + `StencilPortalFeature` | Stencil + renderer feature | Portal frame writes the stencil value; far side drawn stencil-tested and clipped at the portal plane |
| `Slicer4D.compute` | Compute | Tetrahedron ∩ hyperplane → triangles; prefix-sum compaction; indirect draw arguments |
| `HealpixSky.compute` | Compute | 49,152 HEALPix directions marched through distance-field volumes → risk texture; AND across 10 phases |
| `WarpLUT.compute` | Compute | Builds the FieldFit 512² warp lookup when lens settings change |
| `FinalComposite.shader` | Full screen | Distortion-mesh vertex shader with the late warp; fragment: chromatic-aberration fix, passthrough (Y/CbCr → RGB), FieldFit and colour LUTs, lens mask |

**URP switches.** HDR, depth texture, opaque texture, depth priming, post-processing and shadows off; Native RenderPass and SRP Batcher on; Render Graph on (compatibility mode off).

**Profiling markers.** Every pass above has a `ProfilerMarker`; CPU and GPU times per pass go to the console tiles and the nightly regression replay (§15.5).

### 5.3 Rendering details: portals, curved space, sky, slicer, budgets

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
    - Airway H² layout: set R from the branching, R = ℓ/ln b (b = 2, ℓ = 1.5 m → R ≈ 2.2 m) [D]. Locomotion is by fork chambers joined by portals, embedded on the tile grid (§6.4).
    - A Holonomy-style mapping of 1 m physical cells onto {4,5} squares implies R ≈ 0.8 m (edges matched) or ≈0.94 m (tile centres matched) [D]. Use it only for the Holonomy replication or The Branch, not comfort-critical blades.
  - **Coordinate convention:** internally use the *unit* hyperboloid/sphere (⟨p,p⟩ = −1 or |p| = 1) and scale lengths by R only at the metric boundary (metres). §6.1 writes R explicitly; code should not, to avoid factor-of-R bugs.
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
- n comes from the cube's quaternion (Brief §3.3) or a stick.
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

### 5.4 Thermal and performance tiers

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

## 6. Non-Euclidean engine

### 6.1 The shared kernel (formulas)

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

### 6.2 World model: an atlas of charts

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

### 6.3 Euclidean → non-Euclidean mapping of scanned meshes

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

### 6.4 Locomotion in a 3×3 m room

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

### 6.5 Procedural generation

- **Two layers:**
  1. a **logical graph** from cyclic graph grammars (Dormans / *Unexplored*: loop backbone, lock-and-key edges that stay consistent);
  2. a **physical embedding** solved per visit for the next one or two rooms by backtracking over tile placements (Edgar-style simulated annealing as a fallback).
- Wave Function Collapse for room *interiors* and corridor dressing, not the global graph.
- **Style cascade (Tea For God):** region → room → props parameters, so the result is never "a mix of random things". Per-region LUT palette, fog colour, trim sheets and ambient audio bed (§9.2).
- Medical and science blades are **data-driven, not random:** the airway graph *is* the logical graph; the embedding is Sarkar's H² layout.

### 6.6 Pathfinding and placement

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

### 6.7 Algorithms in pseudo-code

**Tile solver (per-visit physical placement; §6.4).**
```text
solve(module, entry_socket_world, free_tiles, visible_rooms, hidden_rooms):
  best = None
  for rot in {0, 90, 180, 270}:
    place = align(module.entry_socket, entry_socket_world, rot)
    tiles = footprint(module, place)
    if not tiles ⊆ free_tiles: continue                 # eroded floor only
    if tiles ∩ footprint(visible_rooms): continue        # co-visible rooms never overlap
    if any(no free tile in front of s) for s in module.exit_sockets: continue
    if |tiles ∩ footprint(hidden_rooms)| > 0.5·|tiles|: continue
    score = Σ free tiles beyond exits − overlap penalty
    best = argmax(best, score)
  return best   # None → alternate module or distractor reset
```

**H² layout for the airway (Roll a Lung; §11.3.1).**
```text
layout(node, iso_from_parent):
  node.parent_iso = iso_from_parent                     # SO+(2,1); never store global coords
  k = len(node.children)
  for i, child in enumerate(node.children):
    theta = node.dir - node.wedge/2 + node.wedge*(i + 0.5)/k
    child.wedge = min(node.wedge/k * SHRINK, pi/2)
    child.len_m = clamp(s * child.len_mm / 1000, 1.0, 2.0)
    layout(child, Rot(theta) @ Boost(child.len_m / R))  # R ≈ 2.2 m
```
Compose isometries only along the rendered chain from the current chamber (depth ≤ draw radius).

**Distal airway completion (space-filling, Tawhai/Montesantos type).**
```text
complete(terminals, lobe_mask):
  P = sample points in lobe_mask                        # density per published model
  assign each point to its nearest terminal branch
  queue = terminals
  while queue:
    b = pop(queue); pts = points(b)
    if len(pts) < 1 or b.length < L_min: mark terminal; continue
    split pts by the plane through b's axis and centroid(pts)
    for half in halves:
      d = centroid(half) - b.end
      child = Branch(start=b.end, dir=normalize(d), length=0.4*|d|,
                     radius=b.radius*0.79, source="model")   # 0.79 ≈ 2^(-1/3), Murray
      clamp branching angle to ≤60°; push(child)
```
Parameters (point density, L_min, length fraction) follow the published model (e.g. Montesantos et al., PLOS ONE 2016) [verify]; every generated branch is tagged `source: "model"` and drawn dashed.

**Greedy-routing study (H200; spike 0.19).** For each tree and 1,000 random peripheral targets: Euclidean-greedy picks the child whose 3D direction points most toward the target; H²-greedy picks the child closer to the target in the layout's hyperbolic distance. Record failure rates by generation and corridor rule; choose the beacon rule from the result.

### 6.8 Robustness rules every technique implements

- **R1.** Continuous degrees of freedom ride on head and feet; hands give clutches, commits and short relative strokes.
- **R2.** Hand deltas count only while a clutch is held, so lost frames *pause* rather than jump.
- **R3.** Every hand technique has a twin input (non-negotiable 3).
- **R4.** Camera cone: interactables 20–30° below eye level, 40–55 cm away. Draw a faint frustum ring when a hand leaves it. Keep hands ≥35 cm from the face (LiDAR depth is valid from ≈0.30 m; use 0.32 m as the software cut-off) [M].
- **R5.** Generous snapping: targets ≥3° on S²; snap to tree nodes and to pure 4D rotation planes.
- **R6.** The world moves only while the body moves or a clutch is held. Rate-limit curvature, warp and Möbius changes. Use an FOV vignette during world motion.
- **R7.** Apply a One-Euro filter (Casiez et al., CHI 2012) to hand and cube signals *before* any gain, because exponential mappings amplify jitter.
- **R8.** Safe Harbour is always one gesture, button or word away.

## 7. Sensing, input and safety

### 7.1 Hands, cube and gaze pipelines

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

### 7.2 Safety system (on-phone, every frame)

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

**What the tiers measure.** Two sources, each with hysteresis (enter at the threshold, leave 0.15 m further out): (1) distance to real obstacles from LiDAR and the mesh; (2) distance to the **taped play-area outline**, registered from the floor marker at calibration. Virtual paths that run near the outline by design (Breathing Ring) suppress only the 1 m grid tier, never the 0.5 m tier. Lens-standing mode has no LiDAR and is seated only (§11.5).

**Human layer**
- Taped 3×3 m square + 0.5–1 m buffer.
- **Dedicated spotter** (never combined with another role).
- Stop word "exit".
- Operator panic → passthrough.
- Session ≤5 min.

**Public spaces (two documented modes)**
- **Mode A, see-through (recommended):** wide-camera passthrough mixed reality with overlays, 6DoF, continuous environmental awareness. In a 2024 field study, passthrough improved safety and social comfort in public without reducing presence [M].
- **Mode B, full VR with layered warnings:** only in a pre-scanned, roped-off zone with a spotter.
- **Lens-standing mode** (ultra-wide, ARKit paused) is a **seated research mode only** (§11.5); the demo uses Lens-6DoF.
- Never walking in public in full VR without a spotter.

**Privacy**
- Frames in RAM only; nothing recorded or uploaded.
- Visible "camera in use" sign.
- Depth or mesh preferred over RGB.
- Consent for room and object scans.
- Anonymous session IDs.

### 7.3 Safety state machine (implementation)

**State machine (runs first every frame; no network).**

| State | Enter when | Response | Leave when |
|---|---|---|---|
| `Clear` | default | none | — |
| `Notice` | obstacle ≤2.0 m | diegetic placeholder at the obstacle (tree, pillar) | >2.15 m for 0.5 s |
| `Warn` | obstacle ≤1.0 m, or ≤1.0 m to the taped outline | fading grid boundary + soft audio | >1.15 m for 0.5 s |
| `Stop` | obstacle ≤0.5 m, fast-approaching person, or tracking lost/limited | automatic passthrough, locomotion frozen, haptic pulse | >0.8 m for 1 s and tracking normal, or operator clears |
| `Harbour` | double-tap B, palms down, or console | Safe Harbour (§5.2) | user/operator resumes |

**Inputs.**
- **Depth sectors:** a Burst job over the 256×192 depth + confidence images: 7 sectors across ±35° of the forward direction, minimum valid depth (confidence ≥ medium, 0.3–5 m) per sector.
- **Mesh proximity:** a 10 cm occupancy grid baked from `ARMeshManager` meshes during mapping; nearest occupied cell to the head.
- **Play-area outline:** the taped square registered from the floor marker at calibration; distance to its edge.
- **People:** Vision human rectangles at 5–10 Hz, combined with depth for range and closing speed.
- **Tracking:** ARKit tracking state; `Limited` for >0.5 s counts as lost.

**Rules.** Hysteresis on every threshold (+0.15 m). Virtual paths that run near the outline by design (Breathing Ring) may suppress only the `Warn` grid, never `Stop`. Lens-standing mode (ARKit paused) is seated only. Outputs (level + nearest obstacle vector) go to Rendering, Audio and Haptics in the same frame.

## 8. Scanning and assets

### 8.1 Scanning pipelines

**Room (iPhone)**
1. RoomPlan inside the Unity app, sharing AR Foundation's ARSession (`RoomCaptureSession(arSession:)`, §4.3) → `CapturedRoom` (walls, doors, windows, openings, furniture boxes) → JSON + USDZ.
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
- **Coherence:** never show raw photographic lighting. De-light or keep albedo only, then re-light with the region's baked probes and LUT (§9.2).
- **Licences:** MIT/Apache (TRELLIS.2, TripoSG, Brush, meshoptimizer, MapAnything-Apache, DA3 Base/Small/Metric); gated (SAM 3, SAM 3D Objects); non-commercial (SpatialLM weights, VGGT-1B, DA3 Large/Giant); territory-restricted (Hunyuan3D 2.x: not EU/UK/KR).

**What scanned objects are *for* in ATLAS**
- **Cairns/bookmarks** in hyperbolic space (they solve the 22.7 s vs 6.5 s revisit problem).
- **Search targets** (FieldFit).
- **Hand-over objects** (Shared House).
- **Chirality demo** (Glue Die flip).
- **Scale references** (Lung Mission).
- **Seeds** for The Branch.
- **Props with passive haptics** (Station Zero extinguisher).

### 8.2 Asset formats, budgets and the case package

| Asset | Format | Budget |
|---|---|---|
| Room kit modules | GLB, meshopt geometry, KTX2 (ETC1S albedo, UASTC normals); sockets in glTF `extras`; 0.5 m grid | Trim sheets 2048²; ≤50k tris per chunk |
| Scanned / generated object | GLB ≤10k tris, one 1024² KTX2 albedo (+ORM) | 1–5 MB |
| Airway, vessels, lobes | GLB LOD0/LOD1, split per lobe | ≤50k tris per chunk; ≈115k total |
| Breath Room tube | GLB (precomputed per case) | ≤10k tris |
| Needle-sky volumes | KTX2 3D textures, RGBA8 (vessels, fissures, bone, other), 128³ @1 mm + 64³ @4 mm per phase | ≈9 MB per phase |
| GTV phase SDFs | KTX2 3D, R8, 64³ | 10 per case |

**Case package (one folder per case card, content-hashed, served by `/assets/{sha}`):**
```text
case_<id>/
  manifest.json            # schema versions, hashes, units, licences
  tree.json                # atlas.tree/1 (§11.3.4)
  airway_lod0.glb  airway_lod1.glb  vessels.glb  lobes.glb
  chambers.glb             # kit-assembled corridors and chambers
  tube.glb                 # Breath Room space-time tube
  itv_table.json           # 10×10 phase arcs → ITV cm³
  sky_phase0..9.ktx2       # needle-sky volumes
  gtv_phase0..9.ktx2       # GTV SDFs
  plancard_template.json   # atlas.plancard/1
```
**Compression.** KTX2 textures use Basis Universal (ETC1S for colour, UASTC for normal maps) and are transcoded to ASTC on the iPhones at load; geometry is meshopt-compressed by gltfpack.

Loading: glTFast with a per-frame time budget (`TimeBudgetPerFrameDeferAgent`), a custom material generator that maps to the ATLAS uber-shader, and a pre-warmed shader-variant collection (worst frame <20 ms, spike 0.25).

### 8.3 Room kit catalogue (first version)

All modules sit on a 0.5 m grid with sockets in glTF `extras`, one 2048² trim sheet per region, KTX2 textures and meshopt geometry (§8.2). Triangle counts are LOD0 budgets.

| Module | Variants | Sockets / metadata | Tris | Used by |
|---|---|---|---|---|
| Wall panel | 0.5 m, 1.0 m; plain, window, trim | `wall` (height 2.6 m) | ≤600 | Hub rebuild, chambers |
| Corner | inner, outer | `corner` | ≤400 | Hub rebuild |
| Door frame (portal socket) | single, double, free-standing | `portal` (stencil plane, clip plane, transform) | ≤800 | Hub, Post Circling, chambers |
| Floor tile | 0.5 m, 1.0 m; plain, marked X | `tile` (walkable flag) | ≤50 | Every room |
| Furniture proxy | box, table, chair, couch, shelf (scaled from RoomPlan boxes) | `prop`, `haptic` (touchable) | ≤1,000 each | Hub rebuild |
| Fork chamber | 1 in / 2 out, 1 in / 3 out | `valve_door` ×N, `beacon` | ≤3,000 | Airway |
| Corridor | straight 1.0–2.0 m; curved for R = 2.2 m (left, right) | `corridor_in`, `corridor_out` | ≤1,500 | Airway |
| Valve door | open/close animation | `valve` | ≤500 | Airway |
| Cairn plinth | — | `cairn` (object anchor) | ≤300 | Airway |
| Glued cell shell | 1.5 m T² cell; T³ variant 2.6 m high | `deck` (instanced copies) | ≤4,000 | Glued Room, Breath Room |
| Gating flag | pair | `flag` | ≤200 | Breath Room |
| Branch post | post with ring marker | `branch_post` (sheet permutation) | ≤400 | Post Circling |
| Beacon, signage, caption chip | — | `ui_anchor` | ≤200 | All |
| Safety placeholder | tree, pillar, rock | `obstacle` | ≤1,500 | Safety `Notice` tier |

**Rules.** Every room is occupied tiles plus door sockets on tile edges. A placement is valid only if all its tiles lie inside the eroded free floor and every door socket faces at least one free tile (§6.4). Each region gets its LUT, fog colour, trim sheet and audio bed from the style cascade (§9.2).

**From a RoomPlan scan to kit.**
1. RoomPlan JSON: walls, doors, windows, openings, and objects with category and size.
2. Walls snapped to the 0.5 m grid where they fall within tolerance.
3. Doors become portal sockets; windows become window panels.
4. Each object box becomes the proxy of its category, scaled to fit.
5. Free floor eroded by 0.3–0.5 m and cut into tiles (§6.4).
6. Region LUT, trim sheet and audio bed applied.

## 9. Audio, haptics and look

### 9.1 Audio and haptics

**Spatial audio**
- Steam Audio, or simple panning + attenuation, on the phone's two speakers (they sit near the temples in landscape: verify the separation by ear).
- Wired over-ear headphones only if hygienic covers are available. Bluetooth adds ≥40 ms; no in-ear buds for strangers.

**Geodesic audio (exact and cheap polish)** [D]
- Intensity follows the geometry's sphere-area law: 1/sinh²(r/R) in H³; 1/sin²(r/R) in S³ (a whisper refocuses at the antipode).
- In portal and branched worlds, sound travels along the *glued* geodesic (listener → door → speaker).
- Cue every crossing (portal, wrap, sheet change) with an earcon.

**Haptics:** transient 20–50 ms pulses for confirmations and boundary hits. Never enable ARKit audio capture (it mutes haptics). If voice is used, record through your own `AVAudioEngine` session with haptics allowed during recording [U].

### 9.2 Look and polish (coherence recipe)

1. **Non-photoreal target look:** flat or toon shading, a limited palette or painterly style, so imperfect scans read as intentional.
2. **De-light and re-light** all scanned and generated content; bake lighting per region.
3. **Per-region LUT (palette ramp) in the shared uber-shader:** colour grading at no extra cost; implements the style cascade.
4. **Rim or outline** in the same pass to unify silhouettes from different sources.
5. **Per-region geodesic fog:** hides LOD pops and portal-depth limits, and gives each blade an identity.
6. **Trim-sheet modular kit** on a 1 m or 2 m grid with standardised connector modules (doors, portals, hatches). A RoomPlan door maps to the same connector.
7. **Region ambient audio bed + reverb** matched to room size.
8. **Caption chip** naming the geometry and its rule, in a fixed screen location.

## 10. Networking and the PC side

### 10.1 Networking

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

### 10.2 Operator console and spectator view

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

### 10.3 Server implementation

**Process and endpoints (`server/`, one asyncio process + a GPU worker pool).**

| Endpoint / port | Protocol | Purpose |
|---|---|---|
| `:8765 /ws` | WebSocket (MessagePack) | Control channel for phone, partner, console, spectator (`Hello.role`) |
| `:8766` | UDP | `Pose` at 30–60 Hz (latest-wins) |
| `:8765 /assets/{sha256}` | HTTP GET | Content-addressed assets from `cache/` |
| `:8765 /api/case/{id}/build` | HTTP POST | Roll a Lung: complete → layout → kit → tiles; progress via `CaseBuild` |
| `:8765 /api/object` | HTTP POST (multipart) | The judge's photos → background removal → SPAR3D → gltfpack → `AssetReady` |
| `:8765 /api/logs` | HTTP POST | Session log upload at exit |
| `:8765 /console`, `/dashboard` | static web | Operator console and Knife Map dashboard |
| mDNS `_atlas._tcp` + QR code | discovery | QR payload `{"ws":"ws://<ip>:8765/ws","udp":8766}` |

**Modules.** `link.py` (sessions, roles, clock sync, routing), `udp.py` (asyncio datagram protocol), `assets.py` (hash store), `cases.py` (Roll a Lung jobs), `objects.py` (object jobs), `console.py` (operator commands), `logs.py` (SQLite + Parquet export), `workers/` (GPU process pool: one process owns the RTX 5070).

**Web console and dashboard.** Vite + TypeScript + D3 (MIT/ISC), served statically; connects as role `console`; tiles for fps (p50, 1%-low), CPU/GPU ms, thermal, tracking, RTT, battery, hand confidence, kill switches, blade queue, panic → passthrough, re-anchor, comfort branch, IPD nudge.

**Spectator.** The same Unity project built for Windows with `ATLAS_SPECTATOR` defined (ARKit disabled); connects as role `spectator`, consumes `StateSync` (10–30 Hz) + `Pose`, renders the mirror view and the chart strip; the dashboard reveal is the web page.

## 11. Blade implementation specs

### 11.0 Scope

What each blade *is* for the judge is in Brief §1.3, Brief §4.0 and Brief §9.3. This section is the build spec: data, algorithms, rendering notes and acceptance tests. Tier B and C blades are roadmap only (Brief §4.6).

### 11.1 Hub: the scanned room (Euclidean home)

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

### 11.2 Glued Room (warm-up blade; T², T³, Klein bottle, orbifolds)

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

#### 11.2.1 Breath Room (live-path variant; ℝ³ × S¹)

- **Rule:** "Walking east moves you through the breath: the east wall is 100%, the west wall 0%."
- **Geometry:** the Glued Room engine with the east–west gluing reinterpreted as time: phase φ = 2π·(x mod L)/L with L = 1.5 m. Flat but wrapped (Brief §4.0); not curved, and the narration says so.
- **Content:** a 3D slice of the 4D (x, y, z, t) data taken through the tumour centre, so the room's axes are (t, head–feet, left–right). The GTV becomes a **space-time tube** along the room's x axis; every copy of the cell repeats it, so the judge sees an endless periodic tube (the universal cover ℝ → S¹ made visible). Looking east along the tube shows it end-on: its silhouette is the ITV in that slice. This is the Slice Cube's half-turn view (Brief §3.9) built as a room.
- **Tasks:** walk along the tube (scrub the breath); drop two floor flags to bracket the gating window, which lights that part of the tube; ITV volume and duty cycle update live; turn to face east and watch the shadow shrink. T1 self-avatar in the next copy. Optional 5 s Klein flip in the 10-min slot only.
- **Slice Tilt (10 s, cube; 10-min slot, 5-min slot if time allows):** the room shows one 3D slice of the 4D data. Tilting the cube rotates that slice in the plane spanned by time and left–right, from "time along the room" (0°) to "an ordinary 3D snapshot" (90°), and the tube morphs into the tumour's 3D shape: a rotation in a plane we cannot see, which is the challenge's 4D-projection idea. Precompute 7 tilted slices per case (0–90° in 15° steps) and blend neighbours; slicing a 4D tetrahedral mesh live with the 4D slicer (§5.3) is the stretch goal.
- **Data:** 10 phase GTV masks → per-phase SDFs on a 64³ ROI → resampled to a (t, y, z) volume of 64³ → tube mesh by marching cubes, **precomputed on the PC per case** (static, so no runtime crossfade). ITV(arc) = OR of phase masks, precomputed for all 10×10 arcs. Plus 7 tilted slice meshes for Slice Tilt.
- **Twin:** Q3 Phase Dial (cube yaw), seated.
- **Acceptance:** tube visible across ≥3 copies at 60 fps on phone B; ITV numbers equal the precomputed table; flags placeable by pad or gaze; Slice Tilt steps cleanly through 0–90° at 60 fps.

### 11.3 Lung Mission (deep blade; H² · S¹ · S² · optional T²)

**Story.** A de-identified public patient has a lung lesion. **Data choice:**
- **Case cards:** the deck is the TCIA 4D-Lung patients (≈20; Option A below), because the Breath Room needs real breathing phases. LIDC-IDRI cases can be added only as labelled composites (Option B).
- **Option A (recommended for the demo):** one TCIA 4D-Lung patient throughout: real motion and real GTV on all 10 phases. These are locally advanced NSCLC tumours, so call it a "target lesion", not a small nodule.
- **Option B:** a *composite teaching case*, with airway and peripheral nodule from LIDC-IDRI and a breathing motion field from 4D-Lung registered onto it, labelled "composite" on screen.

The trainee must:
1. route a bronchoscope to it;
2. account for breathing;
3. choose a needle path (or, if inoperable, radiotherapy beams).

Each step happens in the geometry that matches its structure. The output is a **plan card** (route forks and length, robust exit, gating arc, needle direction with clearances). Framing: *training and visualisation on public data, not for clinical use*.

#### 11.3.1 Airway wing (H², "Bronchial Labyrinth")

**Why H² is essential**
- **The airway is a near-binary tree of about 23 generations** (Weibel) [O]. Hyperbolic circumference grows as 2πR·sinh(r/R), so 2^g constant-width corridors fit at radius ∝ g; the Euclidean plane cannot hold them.
- **Walked distance tracks insertion depth.** Corridor length is monotone in real segment length, ℓ_virtual = clamp(s·ℓ_real, 1.0 m, 2.0 m), and the real insertion depth is on the HUD. **Claim limit:** Sarkar's 1+ε distortion guarantee needs edges many curvature radii long, so it does **not** apply at R ≈ 2.2 m with 1–2 m corridors. We claim constant corridor width and room for every branch, not low distortion.
- **Greedy wayfinding: measured, not assumed.** Every tree has a greedy embedding in H² (Kleinberg), while in E² even the star K₁,₆ fails [O]. Whether "walk toward the beacon" is always right *at our scale* is measured on real trees (spike 0.19 and the H200 study), and the beacon rule is chosen from that result. In the real lung, steering by 3D direction misleads, which is the error class Dolina measured.
- **Honest limit.** The H² layout distorts 3D branch orientation. **Always pair it with the true endoluminal (virtual bronchoscopy) view** at every fork (clock positions: "take the 3 o'clock branch"), and give the hand-held map a planar dendrogram as a second flat twin. Planar airway-tree layouts exist, so the H² case rests on walkable constant-width corridors and measured greedy success.

**Locomotion (resolves "curved walking" vs "portals")**
- Each fork is a **chamber**, a disc of radius ≈0.5 m (≈0.23R, nearly flat) at a node of the H² layout; each parent → child link is a **corridor** of length ℓ_virtual. The wing renders in H²×E, and inside chambers and corridors the camera moves by the exp map (gentle curved walking at this scale).
- The airway is a tree (no loops), so it can't be walked continuously in 3×3 m. Each corridor ends at a **valve door**; when it closes behind the judge, the next corridor and chamber are re-embedded on the floor tiles by the tile solver (§6.4). Topology handles the floor; curvature handles the layout and the view.
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
5. **Pointing probe** at the nodule chamber: "point back to the trachea door and pinch" (Brief §8.1).

**Interactions:** walking forks; X15 Two Compasses; H5 Cone = Subtree (select everything downstream); H6 Ariadne's Rope (go home); H9 hand-held Poincaré map (with planar dendrogram twin); X2 Log-Map Beacon. The judge's photographed object is placed as a cairn at the lesion chamber.

#### 11.3.2 Breathing Ring (S¹; alternative layout to the Breath Room)

**Why it is essential.** Phase-binned 4D CT is ℝ³ × S¹: flat, but wrapped in time (topology, not curvature; Brief §4.0). Phase 90% is adjacent to 0%. A linear slider breaks that; a walked circle preserves it. The ITV is the union of the GTV over a phase arc, i.e. the shadow of a 4D swept volume. This is the real clinical 4D use (4D CT is the standard of care; tumours move 0–34 mm; manage motion above 5 mm, AAPM TG-76) [O].

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

#### 11.3.3 Approach Sky (S², sphere of straight paths)

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

#### 11.3.4 Data pipeline (PC / H200)

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

  ≈115k, inside the **≤150k triangles per eye including portal views** budget (§5.3).
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

#### 11.3.5 Lung Mission: metrics, acceptance, risks

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

### 11.4 Shared House (branched cover; + House Call)

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

### 11.5 FieldFit (sphere of view directions; + Scotoma River)

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

## 12. Data pipelines and batch compute

### 12.1 Case-card pipeline (`pipelines/medical`, CLI `atlas case …`)

| Step | Command | Libraries | Output |
|---|---|---|---|
| Fetch | `atlas case fetch --collection 4D-Lung --patient <id>` | TCIA REST API / NBIA Data Retriever | DICOM (CT phases, RTSTRUCT) |
| Convert | `atlas case convert` | pydicom, SimpleITK, dcm2niix | NIfTI per phase |
| Segment | `atlas case segment` | TotalSegmentator (`total`, `lung_vessels`); airway model benchmarked in spike 0.19 (MONAI Auto3DSeg / nnU-Net) | Lobe, vessel, airway, bone masks |
| GTV | `atlas case gtv` | rt-utils, SimpleITK | GTV masks for 10 phases |
| Skeleton | `atlas case skeleton` | skan, networkx; spur pruning; radius-weighted MST | Branch graph + radii |
| Complete | `atlas case complete` | numpy, scipy (§6.7) | Graph with `source: "model"` branches |
| Layout | `atlas case layout --R 2.2` | numpy (SO⁺(2,1)) | `tree.json` |
| Meshes | `atlas case mesh` | scikit-image marching cubes, trimesh, gltfpack | Airway/vessel/lobe GLBs |
| Chambers | `atlas case chambers` | kit modules + tile metadata | `chambers.glb` |
| Breath Room | `atlas case tube` | SimpleITK SDFs, marching cubes | `tube.glb`, `itv_table.json` |
| Needle sky | `atlas case sky` | scipy EDT, RGBA8 packing, toktx | `sky_phase*.ktx2` |
| Package | `atlas case pack` | — | Case package + manifest (§8.2) |

Every step is idempotent, cached by input hash, and covered by a golden-output test on one reference case.

### 12.2 H200 batch jobs

| Job (`hpc/*.sbatch`) | Input | Output | Notes |
|---|---|---|---|
| `segment_all` (array) | 4D-Lung cases (+ LIDC for composites) | masks | 1 H200 per task |
| `complete_layout` | masks | `tree.json`, chambers | CPU-heavy; GPU optional |
| `tube_sky` | phase CTs, GTV masks | tubes, ITV tables, sky volumes | |
| `greedy_study` | all trees | failure rates (CSV + plots) | Feeds Two Compasses and the docs |
| `trellis_kit` | prompts / reference photos | stylised kit props (GLB) | TRELLIS.2 needs ≥24 GB; export at 5–20k faces |

Containers are run with Apptainer; outputs land in `/project/atlas/cases/` and sync to the PC nightly.

### 12.3 Live object pipeline

1. A teammate photographs the judge's object on the Galaxy S24 during framing (1–3 photos) and uploads to `/api/object`.
2. Background removal (rembg or SAM 3) → SPAR3D on the RTX 5070 (≈7–10.5 GB VRAM) → decimate to ≤10k tris → `gltfpack -cc -tc` → `AssetReady` to the phone.
3. The Lung blade places it as the cairn at the lesion chamber; if not ready in time, a kit object by label is used and labelled.

## 13. Telemetry and benchmarks

### 13.1 Logging schema and dashboard

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

### 13.2 Technical benchmarks to publish

- fps p50 and 1%-low per blade on both phones after a 30-min thermal soak.
- Motion-to-photon latency (photodiode or 240 fps camera).
- Tracking drift per metre walked (VRoamer reported ≈1% [O]).
- Obstacle-warning latency (target <200 ms).
- Geometric numerical drift (hyperboloid renormalisation error after N steps).
- Hand-tracking visible fraction and latency.
- Cube jitter.
- Scan-to-world time (room, object).
- Asset load-to-visible time.
- Breath Room motion sanity check against the DIR-Lab expert landmarks (validation only, §11.3.4).

## 14. Demo-day engineering

### 14.1 Roles, space, kit

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

### 14.2 Fallbacks

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

### 14.3 "Real / Pre-baked / Mocked" (fill in honestly before the event)

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

### 14.4 Run sheet from the engine's side

Each row: what the engine runs or loads in that segment of the 10-minute slot (timings from Brief §9.3), and what replaces it if it fails.

| Time | Segment | Space | What runs or loads | If it fails |
|---|---|---|---|---|
| 0:00–0:40 | Frame | — | Roll a Lung build on the PC (`/api/case/{id}/build`); S24 photos → SPAR3D; baseline FMS | Curated default case; kit object |
| 0:40–1:20 | Onboarding | passthrough | Fit; floor-marker re-anchor on the X; gamepad and hands check | Pad mode |
| 1:20–1:50 | Bend Your Room | E → H / S | Hub from the venue scan; curvature dial; carried arrow | Day-before scan |
| 1:50–2:35 | Breath Room | ℝ³ × S¹ | Glued cells with x as time; `tube.glb`, `itv_table.json`, 7 tilted slices for Slice Tilt | Classic Glued Room |
| 2:35–3:50 | Airway | H² | `tree.json`, `chambers.glb`, tile solver, Two Compasses, cairn, pointing probe | Cached default tree |
| 3:50–4:20 | Needle sky | S² | Sky volumes, `HealpixSky.compute`, great-circle check arcs, plan card | Precomputed sky |
| 4:20–4:40 | Post Circling | branched | Two pre-scanned sheets, recorded avatar | Skip (FMS ≥2) |
| 4:40–5:25 | Judge's choice | view sphere / E | FieldFit warp lookup, or House Call (S24 + LiveKit) | Recorded partner |
| 5:25–6:10 | Exit + reveal | passthrough | FMS, SEQ; logs to `/api/logs`; dashboard | Phone's log buffer |

**Preload rule.** The next segment's package loads while the current one runs (glTFast with a frame-time budget) and the previous one is unloaded, so peak memory stays ≤1.8 GB on phone B. Nothing on this path waits on the network once the case is built.

## 15. Build plan

### 15.1 Phase 0: spikes and benchmarks (≈2 weeks, platform only; decide D5, D9, D16, D19 and lock budgets)

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

### 15.2 Phases to the hackathon build

| Phase | Deliverables | Exit criteria |
|---|---|---|
| **1. Kernel, hub, safety, tooling** | `IGeometry` (E, T, P first); chart/gluing world model; one-camera stereo + final pass (distortion, passthrough, late warp); stencil portals; hub from an in-app RoomPlan scan + modular kit v1; relocalisation (world map + floor marker); safety tiers + Safe Harbour; gamepad/gaze input; X1 Geodesic Ray; classic Glued Room T1, T4 (pad-selected gluing); logging; message schema + server; operator console v0; **Windows spectator build** with state replication; Knife Map v0; CI/CD with free-account signing (D19), kill switches, crash reporting | A naive user completes the Glued Room at 60 fps on phone B; Safe Harbour <300 ms; warnings fire at 2/1/0.5 m; commit-to-device ≤20 min |
| **2. Story path core** | **Bend Your Room** (X5 + H4); **Breath Room** (with Slice Tilt); Lung pipeline (case-card segmentation on the H200, skeleton, H² layout, tree.json, tile solver, chambers/corridors, valve doors); **Roll a Lung**; **Two Compasses**; endoluminal window; needle sky (sphere-traced, with the great-circle check); plan card; pointing probe; object pipeline (S24 photos → SPAR3D → cairn); greedy study | Four forks in ≤75 s by a naive user; Roll a Lung ≤10 s; sky per §11.3.5; plan card validates |
| **3. Post Circling, judge's choice** | Solo Post Circling on pre-scanned sheets (recorded avatar); FieldFit (blind-spot finder, circular tunnel + power-law lens, Lens-6DoF; Teichmüller LUTs precomputed); minimal S24 partner client + LiveKit for House Call | Both choices run inside the 10-min show; recorded fallbacks exist |
| **4. Hands, cube, polish, show** | Vision hands + twins; X12 Geometry Lens; cube (T3 Glue Die, D2 views); style cascade, LUTs, outlines, audio beds, earcons; dashboard reveal (decisions first); thermal tiers; videos; 15–20 rehearsals; docs (Brief §8.4) | All Brief §9.6 targets met |
| **5+. Long project** | Tier B blades; full Android build; Walk Together covering-space co-walking; Wormhole Quest; replications; studies S1–S6 with ethics approval; clinical partners | Papers, studies, partners |

### 15.3 Repository layout (suggested)

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

### 15.4 Engineering standards

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

### 15.5 Testing, CI/CD, profiling, memory and kill switches

- **Tests.** Kernel identities in double precision; GPU-vs-CPU parity for the Klein, HEALPix (reference: astropy-healpix, BSD) and slicer kernels; slicer output compacted with a prefix sum (deterministic, unlike append buffers); per-blade golden images (Unity Graphics Test Framework); schema round-trip tests; ARKit session recordings (Xcode "Replay data") for tracking regressions. Recordings contain camera video, so they are **team-only test data, never judge sessions** (non-negotiable 8 exemption for the team's own rehearsal recordings).
- **Performance regression.** A nightly replay on phone B logs CPU/GPU p99, missed vsyncs, memory and thermal state; fail the build on a >10% regression.
- **CI/CD.** Self-hosted GitHub Actions runner on the M3 Max: Unity batch mode with a persistent Library cache → PostProcessBuild sets Swift settings, entitlements and Info.plist keys (`NSLocalNetworkUsageDescription`, `NSBonjourServices`, camera) → `xcodebuild -allowProvisioningUpdates` signs with the free Personal Team (D19) → `xcrun devicectl` installs on both phones connected to the Mac; the free account has no TestFlight, so the secondary route is Run from Xcode. Freeze a **golden demo build** and the CI pipeline before the event.
- **Profiling.** Xcode Instruments (Metal System Trace, Game Performance, Allocations); a weekly GPU frame capture per blade; CPU/GPU-ms tiles on the operator console.
- **Crash reporting.** Sentry or Crashlytics with an offline queue, plus MetricKit.
- **Memory.** Budget for phone B (6 GB): app peak ≤1.8 GB; watch `os_proc_available_memory`; request the increased-memory-limit entitlement if the free Personal Team accepts it (spike 0.26); otherwise hold the budget without it. Per blade: textures ≤300 MB, meshes ≤150 MB, volumes ≤100 MB.
- **Kill switches.** On the device (gamepad chord, works offline) and on the console: hands, cube, meshing, person detector, live vs cached sky, portal depth, glued copies, MSAA, render scale, network. Active flags are logged per session.
- **Demo-day ops.** Guided Access, orientation lock, Do Not Disturb, AirDrop/Handoff off; encrypted pseudonymous logs; videos play from the PC.

### 15.6 What to cut first if time runs short

In order: the Cardboard XR plugin spike (D5 alternative) → Lens-standing mode → Slice Tilt (keep the end-on view) → live House Call (LiveKit, avatars, S24 partner client) → Steam Audio (use simple panning) → live object meshes (use kit objects) → spike 0.14 and Curved-Only mode → the judge's-choice slot. Never cut: safety, Safe Harbour, the story path's geometry moments, the plan card, the dashboard.

### 15.7 Which spike settles which decision

| Decision | Spikes | Default until measured |
|---|---|---|
| D5 stereo path | 0.1, 0.15 | One camera, side-by-side; two-camera rig as fallback |
| D9 curved stereo per wing | 0.7 | Klein per eye; equidistant where depth feels wrong |
| D16 latency method | 0.16 | Core Motion prediction + late rotational warp |
| Airway model, R and corridor rule | 0.19 | TotalSegmentator + model completion; R ≈ 2.2 m |
| Needle sky on the phone | 0.20 | Live current phase, other phases precomputed |
| Budgets (GPU, memory, heat) | 0.1, 0.8, 0.22 | 8 ms GPU, 1.8 GB, 20 min before a tier drop |
| D15 geometry strategy | 0.14 (after Phase 2) | Portals + curved wings |
| D19 iOS signing | 0.26, 0.24 | Free Apple account (Xcode Personal Team); paid program only if 0.26 fails |

### 15.8 Phase 0 test protocols (the make-or-break spikes)

For each spike: set-up, steps, what to record, the pass mark, and plan B if it fails. Record every result in `docs/phase0.md` with the date, phone, build and numbers.

**0.1 + 0.15 · Stereo performance on phone B**
- *Set-up:* phone B in the closed headset at room temperature; Frame Timing Stats on; scene A = empty AR scene; scene B = 150k triangles per eye with two portals.
- *Steps:* run each scene for 20 min, logging main-thread, render-thread and GPU ms every second from FrameTimingManager; run once with the one-camera path and once with two cameras.
- *Record:* p50/p99 GPU and CPU ms, missed vsyncs per minute, thermal-state timeline.
- *Pass:* GPU ≤8 ms and main + render ≤7 ms at p99 after 20 min; ≤1 missed vsync per minute.
- *If it fails:* eye scale 0.75, MSAA 2×, portal depth 1; if the one-camera path loses, use two cameras (D5 fallback).

**0.2 · ARKit through the camera window**
- *Set-up:* closed headset, textured mat, even light; a taped 20 m loop (or ten laps of a 2 m square).
- *Steps:* walk the loop at normal pace and stop on the X; repeat in dimmer light.
- *Record:* end-point error against the X, tracking-state drops, relocalisation time.
- *Pass:* drift <2% of distance walked; no tracking loss in normal light.
- *If it fails:* try the other headset (camera window, vignetting), add floor texture, re-anchor on the X at the start of every blade.

**0.7 · Comfort in curved space (Klein vs equidistant stereo)**
- *Set-up:* an H²×E corridor wing at R = 2.2 m in both stereo modes; 5 people who have not used it.
- *Steps:* FMS at entry; 2 min of walking per mode, order counterbalanced; FMS after each; ask "Did the depth look right?"
- *Record:* FMS change, the depth answer, any stop.
- *Pass:* median FMS rise ≤2; nobody stops; one mode judged "depth looked right" by at least 4 of 5.
- *If it fails:* raise R to 3–5 m, shorten the airway segment, add a vignette.

**0.19 · Airway depth on the case cards**
- *Set-up:* 5 4D-Lung cases; TotalSegmentator vs a MONAI / nnU-Net airway model on the H200.
- *Steps:* segment, skeletonise, count generations along the route to each target lesion; run model completion; run the greedy-routing study on each tree.
- *Record:* segmented generations, completed generations, greedy success (hyperbolic vs Euclidean).
- *Pass:* ≥6 generations segmented or completed for a 4-fork route; greedy numbers recorded.
- *If it fails:* curate a default case, lean on labelled model completion, shorten the route to 3 forks.

**0.26 · Free-account signing (D19)**
- *Set-up:* the owner's free Apple account in Xcode; both phones paired; fixed bundle ID.
- *Steps:* add Increased Memory Limit; build and install on both phones with `xcodebuild` + `devicectl` (§2.8); note the profile expiry date; reinstall after 6 days to confirm renewal.
- *Record:* capability accepted or refused; install time per phone; expiry date.
- *Pass:* both phones install from the Mac; the capability is accepted, or spike 0.22 passes without it.
- *If it fails:* enrol in the paid program (Appendix E) at least 2 weeks before the event.

**Order for week 1.** 0.1 + 0.15 and 0.2 first (if the phone can't hold 60 fps in the headset, nothing else matters), then 0.26 and 0.7, with 0.19 running on the H200 in parallel. Every other spike follows in week 2.

**Results log template (`docs/phase0.md`).**

| Spike | Date | Phone / build | Key numbers | Pass? | Decision or plan B taken |
|---|---|---|---|---|---|
| 0.1 + 0.15 | | B / | GPU p99 ms · CPU p99 ms · missed vsyncs per min | | |
| 0.2 | | B / | drift % over distance · tracking drops | | |
| 0.7 | | B / | median FMS rise · "depth right" count of 5 · stops | | |
| 0.19 | | H200 / | segmented generations · completed generations · greedy success H vs E | | |
| 0.26 | | A + B / | capability accepted? · install time · expiry date | | |

### 15.9 Schedule templates (pick one when the event date is known)

Count back from the event. Gates are pass/fail: if a gate fails, take the listed cut rather than letting everything slip.

| Weeks available | Plan | Cuts built in |
|---|---|---|
| **10 or more** | W1–2 Phase 0 · W3–4 Phase 1 · W5–7 Phase 2 · W8 Phase 3 · W9 Phase 4 · W10 rehearsals | Only §15.6 if needed |
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

### 15.10 Ownership (fill in when the team is set)

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

On the day: narrator, spotter (never doubled), operator and explainer (§14.1).

## 16. Engineering risks and mitigations

| # | Risk | Likelihood / impact | Mitigation |
|---|---|---|---|
| 1 | Thermal throttling over a demo day (≈50% GPU after 40 min of stress; the enclosed headset makes it worse) | High / High | Fixed 60 fps, tiers keyed to `thermalState`, meshing off after mapping, Vision only when needed, vented headset, fan, **second phone hot-swap** |
| 2 | Hands not seen (narrow cone, edges, low light, sleeves) | High / Medium | Hands are always optional (twins); targets 20–30° below eye line at 40–55 cm; frustum ring; good lighting |
| 3 | ARKit drift or tracking loss (dark hall, plain floor, crowds) | Medium / High | Textured mat, even light, saved `ARWorldMap` + floor marker, re-anchor on X, passthrough on loss |
| 4 | Cybersickness (first-timers, curved worlds, rotating portals) | Medium / High | Topology before gains; ≤5 min; ordering; vignette; FMS stop rule; Station Zero opt-in |
| 5 | Judges read it as "just portals" / "just a skybox" | Medium / High | Live numbers (Knife Map equation card); Gauss–Bonnet check; walk-around-vs-through demo; flat twin vs curved view; one real task per blade |
| 6 | Over-claiming medically | Medium / High | Brief §2.4 wording; public data only; "not for clinical use" on screen |
| 7 | Second phone or network failure (Shared House) | Medium / Medium | Recorded avatar track; blade order allows skipping |
| 8 | AI generation slow or unavailable live | Medium / Low | Everything pre-baked; live generation is a bonus labelled as such |
| 9 | 5070 incompatibilities (Blackwell sm_120, custom CUDA kernels) | Medium / Low | PyTorch cu128; prefer models without exotic kernels; run heavy ones on the H200 |
| 10 | Licences (NC weights, GPL code, gated models, territory limits) | Medium / Medium | Licence ledger; don't copy HyperRogue (GPL) or unlicensed repos; NC only for research and demo |
| 11 | Headset doesn't fit both iPhones' camera plateaus (15 Pro Max, 14 Pro Max) or glasses | Medium / High | Buy two candidates early; test with both phones and a glasses wearer; cut a viewer if needed |
| 12 | Scope creep (63 techniques, 15+ blades) | High / High | Tier A only until Phase 4 exit; the design space is *documented*, not all *built* |
| 13 | Challenge rules unknown (Huawei hardware? slot length? panel?) | Unknown / High | Ask the organisers now (§17) |
| 14 | Hyperbolic navigation not learnable by everyone | Medium / Medium | Beacons, breadcrumbs, cairns, hand-held Poincaré map, Ariadne's Rope, Euclidean home |
| 15 | Privacy (room scans, motion as a biometric) | Low / High | Parametric shares only, consent, anonymous IDs, no frames stored |
| 16 | No single-pass stereo on iOS; two cameras + portal cameras are CPU-bound | High / High | One-camera side-by-side stereo with portal RendererLists (D5); spike 0.15 |
| 17 | Motion-to-photon too high (capture-time pose, no prediction) | Medium / High | Core Motion prediction + late rotational warp (D16); spike 0.16 |
| 18 | Airway segmentation too shallow for a 4-fork route | High / High | Benchmark MONAI Auto3DSeg / nnU-Net (spike 0.19); Roll a Lung completes distal generations, labelled model-generated; curated default case |
| 19 | Over-claiming the hyperbolic airway (1+ε distortion; greedy guarantee) | Medium / Medium | Claims limited to constant width and room for every branch (§11.3.1); greedy success measured, not assumed |
| 20 | Free-account limits: a build expires (7 days) or Xcode refuses the memory capability | Medium / Medium | Install ≤24 h before the event and launch-check each session; Mac and cables on site; spike 0.26; paid program as fallback (D19) |
| 21 | Five geometries in five minutes feel like a tour, not a purpose (UX is 30% of the score) | Medium / High | One rule and one "aha" per space (Brief §4.0, Brief §9.8); the patient story and plan card tie them together; rehearsal targets (Brief §9.6) |

## 17. Open questions

1. **Challenge rules.** The challenge document sets no hardware or platform requirement and asks for 30+ FPS. Still to ask the organisers:
   - event date and slot length;
   - whether code may be written before the event;
   - single judges or a panel; live demo only, or video too;
   - venue space, lighting, power and Wi-Fi (can we bring our own router?).
2. **Team size and roles** (the demo needs ≥3 people: narrator, spotter, operator).
3. **Phones:** confirm phone B is an iPhone 14 **Pro** Max (LiDAR), not a 14 Plus. Test the Galaxy S24 with the Cardboard optics (black-artifact issue).
4. **H200 cluster policy:** batch only (Slurm), or interactive nodes reachable from a venue network? Data-transfer limits?
5. **Which headset(s):** order two candidates now (§4.2) and test fit with the 15 Pro Max and with glasses.
6. **Medical datasets:** confirm TCIA terms acknowledgement. Choose Option A (one 4D-Lung patient throughout) or Option B (composite: LIDC case with an outer-third 8–20 mm nodule + 4D-Lung motion, labelled composite).
7. **Hackathon blade set:** Tier A as proposed, or swap Shared House for Station Zero if the second phone is a risk?
8. **Naming:** keep "ATLAS"?
9. **Geometry strategy:** stay with portals + curved wings, or also pursue a "true curved space only" mode (Brief §7.13)? Decide after spike 0.14.
10. **Staffing:** is there a 4th person for the live House Call option? If not, the judge's choice is FieldFit only.
11. **Case cards:** which ≈20 public cases (with a lesion reachable within 4–6 generations after completion) go into the deck?
12. **Signing:** does the free Personal Team accept the Increased Memory Limit capability (spike 0.26)? If not, does the app hold 1.8 GB without it?
13. **Schedule:** once the date is known, pick a template in §15.9 and fill in the owners in §15.10.

## 18. Engineering references

**Unity and XR**
- Unity 6.3 LTS: [announcement](https://unity.com/blog/unity-6-3-lts-is-now-available) · AR Foundation: [manual](https://docs.unity3d.com/Manual/com.unity.xr.arfoundation.html) · ARKit XR Plugin: [docs](https://docs.unity3d.com/Packages/com.unity.xr.arkit@6.3/manual/index.html) · Single-pass stereo platforms: [docs](https://docs.unity3d.com/Manual/SinglePassStereoRendering.html)
- glTFast: [repo](https://github.com/Unity-Technologies/com.unity.cloud.gltfast) · MessagePack-CSharp: [repo](https://github.com/MessagePack-CSharp/MessagePack-CSharp) · UniTask: [repo](https://github.com/Cysharp/UniTask) · Apple Unity plugins: [repo](https://github.com/apple/unityplugins)
- Cardboard SDK: [repo](https://github.com/googlevr/cardboard) · [cardboard.h](https://raw.githubusercontent.com/googlevr/cardboard/master/sdk/include/cardboard.h) · [releases](https://github.com/googlevr/cardboard/releases) · [viewer profiles](https://developers.google.com/cardboard/manufacturers)

**Apple platform**
- ARKit scene depth: [docs](https://developer.apple.com/documentation/arkit/arconfiguration/framesemantics-swift.struct/scenedepth) · World maps: [docs](https://developer.apple.com/documentation/arkit/saving-and-loading-world-data) · Image tracking limits: [docs](https://developer.apple.com/documentation/arkit/arworldtrackingconfiguration/maximumnumberoftrackedimages)
- RoomPlan: [docs](https://developer.apple.com/documentation/roomplan) · [WWDC23 RoomPlan enhancements](https://developer.apple.com/wwdc23/10192) · Object Capture: [docs](https://developer.apple.com/documentation/realitykit/scanning-objects-using-object-capture)
- Vision hand pose: [docs](https://developer.apple.com/documentation/vision/vndetecthumanhandposerequest) · Local network privacy (TN3179): [docs](https://developer.apple.com/documentation/technotes/tn3179-understanding-local-network-privacy) · ARKit session recording/replay: [docs](https://developer.apple.com/documentation/arkit/recording-and-replaying-ar-session-data)
- Signing: free vs paid memberships: [Apple](https://developer.apple.com/support/compare-memberships/) · Developer Mode: [docs](https://developer.apple.com/documentation/xcode/enabling-developer-mode-on-a-device) · Enrolment (fallback): [Apple](https://developer.apple.com/programs/enroll/) · D-U-N-S timelines: [Apple](https://developer.apple.com/support/D-U-N-S/)

**Assets, audio, multi-user**
- meshoptimizer / gltfpack: [repo](https://github.com/zeux/meshoptimizer) · KTX-Software: [repo](https://github.com/KhronosGroup/KTX-Software) · Steam Audio: [repo](https://github.com/ValveSoftware/steam-audio) · LiveKit: [server](https://github.com/livekit/livekit), [Unity SDK](https://github.com/livekit/client-sdk-unity) · UniVRM: [repo](https://github.com/vrm-c/UniVRM) · uLipSync: [repo](https://github.com/hecomi/uLipSync) · UXF: [repo](https://github.com/immersivecognition/unity-experiment-framework)

**Medical and AI**
- TotalSegmentator: [repo](https://github.com/wasserth/TotalSegmentator) · TCIA 4D-Lung: [collection](https://www.cancerimagingarchive.net/collection/4d-lung/) · LIDC-IDRI: [collection](https://www.cancerimagingarchive.net/collection/lidc-idri/) · Airway depth report: [Slicer forum](https://discourse.slicer.org/t/slicer3d-through-totalsegmentator-airway-segmentation-limited-to-1-2-generations-how-to-extend-to-distal-bronchi-g4-g5/45943)
- SPAR3D: [repo](https://github.com/Stability-AI/stable-point-aware-3d) · TRELLIS.2: [repo](https://github.com/microsoft/TRELLIS.2) · SAM 3: [repo](https://github.com/facebookresearch/sam3) · UWHVF visual fields: [repo](https://github.com/uw-biomedical-ml/uwhvf)

**Non-Euclidean rendering references**
- Non-Euclidean VR I/II: [arXiv 1702.04004](https://arxiv.org/abs/1702.04004), [arXiv 1702.04862](https://arxiv.org/abs/1702.04862) · HyperEngine: [repo](https://github.com/HackerPoet/HyperEngine) · SebLague/Portals: [repo](https://github.com/SebLague/Portals) · Hypermine: [repo](https://github.com/Ralith/hypermine) · Portals in VR (stencil vs RT): [TU Delft](https://repository.tudelft.nl/file/File_6caa1e43-0353-419f-b07b-2b598b224731)

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

## Appendix D: Glossary

| Term | Meaning |
|---|---|
| Chart | A room or cell with its own local coordinates |
| Gluing | The rule joining two charts (portal, glued wall, branch cut, hatch, time seam) |
| Exp / log map | Take a straight step in curved space / the direction and distance to a point |
| Holonomy | How far a carried arrow turns after a loop (curvature × area) |
| Klein chart | A way to draw hyperbolic or spherical space where straight lines stay straight |
| HEALPix | An equal-area grid on the sphere (49,152 directions here) |
| ITV | Internal target volume: the space a tumour sweeps through over the breathing cycle |
| Late warp | Rotating the finished image by the newest head motion just before display |
| Memoryless MSAA | Anti-aliasing kept in on-chip tile memory, never written to RAM |
| Stencil portal | A doorway drawn by masking pixels and re-drawing the far side inside them |
| Render Graph | URP's way of declaring passes so Unity schedules them and their memory |
| asmdef | A Unity assembly definition; ours enforce which modules may reference which |
| IL2CPP | Unity's C# → C++ build path, required on iOS |
| Motion-to-photon | Time from a head movement to the matching light on screen |

## Appendix E: Apple account set-up (free first, paid as fallback)

**Free account (the plan, D19).**
1. Install Xcode; Xcode › Settings › Accounts › + › Apple Account › sign in. It shows as "Name (Personal Team)".
2. Unity: File › Build Profiles › iOS › Build. Open `Unity-iPhone.xcodeproj`; target Unity-iPhone › Signing & Capabilities › tick Automatically manage signing › Team = the Personal Team; bundle ID `com.<owner>.atlas`, kept for the whole project.
3. Plug in each iPhone, unlock it, tap Trust This Computer. Settings › Privacy & Security › Developer Mode › on › Restart › Enable (the switch appears only after pairing).
4. Run (⌘R) or use the CI commands (§2.8). First launch on each phone: Settings › General › VPN & Device Management › the owner's account › Trust.
5. Limits: builds last 7 days; 3 devices; 3 apps per device; 10 new app IDs per 7 days; no TestFlight; some capabilities unavailable (spike 0.26).

**Paid fallback (US$99/yr).**
- *Before you start:* two-factor authentication on; legal name on the Apple account exactly as on your ID; a street address; the age of majority (19 in British Columbia); a photo ID (app route) or your own credit card (web route).
- *App route:* Apple Developer app › Account › sign in › Enroll Now › legal name and phone › photograph the front and back of the ID › Individual › agree › Subscribe (renews yearly).
- *Web route:* developer.apple.com/programs/enroll › Start your enrollment › confirm legal name, address and phone › Individual / Sole Proprietor › agree › pay with your own card › keep the Enrollment ID.
- *Timing:* approval usually takes about 48 h but is not guaranteed; if no confirmation arrives within 24 h of paying, contact Apple with the Enrollment ID.
- *Organisation route* (only if a school or company should own the account): a legal entity, a D-U-N-S number (up to 5 business days, plus up to 2 before Apple sees it), a public website and someone with signing authority. Fee waivers exist for accredited schools, non-profits and government bodies, not for individuals.

**Switching to paid (15–30 min).** Xcode › Settings › Accounts shows the paid team → change the Team in Signing & Capabilities (add a suffix such as `.dev` if the free team still holds the bundle ID) → delete the free-signed app from each phone → add Increased Memory Limit → put the Team ID (developer.apple.com/account › Membership details) into Unity's Signing Team ID → rebuild and install. Development builds then last about a year.

| Symptom | Fix |
|---|---|
| Developer Mode missing in Settings | Pair the phone with Xcode first |
| "Untrusted Developer" on launch | Settings › General › VPN & Device Management › Trust |
| "Failed to register bundle identifier" | Use a unique bundle ID and keep it |
| App ID limit exceeded | Reuse the bundle ID; wait out the 7 days; or go paid |
| App won't open after about a week | The free build expired: run from Xcode again |
| Install fails: identifier "does not match the installed application" | Delete the copy signed by the other team, then install |
| "Personal Team doesn't support…" a capability | Remove it, or switch to the paid team |
