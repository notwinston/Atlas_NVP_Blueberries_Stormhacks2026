# R5 · Performance, latency, thermal and metrics memo (ATLAS, iPhone 15 Pro Max)

Scope: Unity 6.3 LTS + URP + AR Foundation/ARKit, two-camera side-by-side stereo in a lens headset, vertex-warped H²×E geometry, stencil portals, glued-room copies, CPU 4D slicer. Written 3 Oct 2026 for the lead engineer.

Claim tags: **[V]** = verified, fetched primary source this session · **[S]** = secondary source or search snippet, not read in full · **[D]** = my derivation (arithmetic shown) · **[E]** = estimate from experience, must be measured on the device.

Note on fetches: pages were read through a summarising fetch tool, so "[V]" means the primary page was fetched and the fact was returned from it, not that I read raw HTML.

---

## 0. Headlines

1. **The Build Pipeline budgets are safe, and conservative on GPU fill.** At render scale 0.75 the phone shades about 2.0 Mpx per frame (122 Mpx/s), roughly a third of a Quest 2's default eye-buffer pixel rate, on a GPU whose *throttled* output is still about 1.6–2.3x a Quest 2-class Adreno 650 [D]. The binding limits are CPU draw submission (two cameras, no multiview), triangle count from warp subdivision, and heat.
2. **Three numbers in the Build Pipeline need correcting:**
   - **Thermal cap:** it is not "6.5+ only". The Player Setting "Adjust iOS FPS based on thermal state" is present from **6000.3.21f1** [V]. Use 6000.3.21f1 or later, or the post-build patch.
   - **Fog at 5–8 hyperbolic units conflicts with culling at d > 4.** A {4,5} tiling has about 132 tiles within d = 4, about 366 within d = 5 and about 7,400 within d = 8 [D]. Fog must be fully opaque at the cull radius, and the default cull radius should be **d = 3 (about 45 tiles)**.
   - **0.1 m subdivision everywhere is too dense** once multiplied by 45–132 tiles. Use 0.25 m, and 0.1 m only on the home tile (LOD by graph distance).
3. **Latency: accept TrackedPoseDriver "Update only".** On this stack it costs about 1 ms on average against a pipeline of roughly 55–95 ms [D/E]. The only thing that materially cuts latency is gyro prediction of rotation (about 30 lines of C#, no native plugin). The Spec's late warp depends on a custom final pass the Build Pipeline dropped; treat it as out of scope.
4. **Thermal: plan never to reach "Serious".** A 30 fps render without reprojection is poor in a headset, and 30-render/60-reproject is a 4–6 h stretch item with visible positional judder at room scale. Cheaper levers come first: dark palette on OLED, brightness about 40%, 720p ARKit config, meshing off after the scan, no charging, phone swap.
5. **"Scan your room" is the new performance risk.** A raw LiDAR mesh (order 50–200k triangles [E]) replicated into every visible tile would exceed the budget by 10x or more. It must be drawn once, or decimated per copy.

---

## A. Reality check of the Build Pipeline budgets

### A1. A17 Pro versus XR2 Gen 1 / Gen 2

| Item | Value | Tag |
|---|---|---|
| iPhone 15 Pro Max, 3DMark Wild Life Extreme (peak) | 3,570 (UL database), 3,746 (GSMArena) | [V] |
| Same, 20-min stress test stability | about 61% (Notebookcheck, 26 °C, 50% brightness) to 70% (GSMArena) | [S] / [V] |
| Same, floor after about 40 min of consecutive GPU stress | about 50% of peak | [V] GSMArena |
| Solar Bay stability / Steel Nomad stability (UL) | 72% / 69% | [V] |
| CPU sustained (30-min stress) | 78% of peak | [V] GSMArena |
| Snapdragon 8 Gen 2 phone (same Adreno 740 as XR2 Gen 2), WLE peak | about 3,700 (ROG Phone 7) | [S] |
| Quest 3 vs Quest 2 GPU | "about 2.5x" (Meta), 2.3x GPU and 1.34x CPU in neutral mode (UploadVR) | [V] / [S] |
| Snapdragon 865 / Adreno 650 (XR2 Gen 1 base), WLE peak | about 1,100–1,200 | [E] from memory |

Derived ratios [D]:

- A17 Pro peak / XR2 Gen 1 class: 3,650 / 1,150 ≈ **3.2x**.
- A17 Pro sustained (50–70% of peak = 1,800–2,550) / XR2 Gen 1 class: **1.6–2.2x**.
- A17 Pro peak / Quest 3 (2.5 × 1,150 ≈ 2,900): ≈ 1.25x. Sustained: **0.6–0.9x** of a Quest 3.

So: budget as if the phone were "a Quest 2 with 2x the GPU and 2x the single-thread CPU, but no multiview and worse cooling", not as a Quest 3.

Meta's own budgets for reference [V, developers.meta.com device-optimization-comparison]: Quest 2 < 100 draw calls and < 750k triangles per frame; Quest 3 < 200 draw calls and < 1.5M triangles; 13.8 ms frame at 72 Hz. The Build Pipeline's "< 300 draws total (both eyes), 300–500k triangles" equals 150 draws per eye, which is Quest 2-level per-eye CPU work on a CPU about 2x faster [E], and is below Quest 2's triangle budget. **Verdict: keep the Build Pipeline budgets as the "must" line; they have about 2x margin when cold and roughly 1x when throttled.**

### A2. Pixel counts (side-by-side, 2796 × 1290 panel)

Each eye viewport is 1398 × 1290. URP render scale s is linear, so pixels scale with s² [D].

| Render scale | Per-eye target | Mpx per eye | Mpx per frame | Mpx/s at 60 fps | vs scale 1.0 |
|---|---|---|---|---|---|
| 0.60 | 839 × 774 | 0.649 | 1.30 | 78 | 36% |
| 0.70 | 979 × 903 | 0.884 | 1.77 | 106 | 49% |
| 0.75 | 1049 × 968 | 1.014 | 2.03 | 122 | 56% |
| 0.80 | 1118 × 1032 | 1.154 | 2.31 | 138 | 64% |
| 0.87 (Spec) | 1216 × 1120 | 1.362 | 2.72 | 163 | 76% |
| 1.00 | 1398 × 1290 | 1.803 | 3.61 | 216 | 100% |

Comparison [E, from memory of Meta defaults]: Quest 2 default eye buffer 1440 × 1584 ×2 at 72 Hz ≈ 328 Mpx/s; Quest 3 1680 × 1760 ×2 at 72 Hz ≈ 426 Mpx/s. ATLAS at 0.75 shades about 37% of a Quest 2's pixel rate.

Consequences:
- Fill rate is not the constraint. Start at **0.8 with 4x MSAA** and only drop if the soak test demands it.
- Any render scale other than 1.0 forces an intermediate target and a final upscale blit per camera (two blits of 1.8 Mpx each, about 0.2–0.4 ms GPU total [E]).
- Through a lens headset only part of each half-screen is visible. A hidden-area mask (a depth-only mesh drawn first at the near plane outside the lens circle) removes 20–40% of fragments [E]; it is a stretch item because fill is not binding.
- Angular resolution matters more than fps here: at about 90° horizontal FOV per eye, 1049 px gives about 11.7 px/° and 1398 px gives about 15.5 px/° [D]. Line-based art (fwidth grid lines) benefits visibly from 0.8–1.0 plus MSAA.

### A3. MSAA on Apple TBDR GPUs

- Apple: on tile-based deferred GPUs a memoryless multisample texture is resolved on-tile; "the multisample texture doesn't need to be stored, saving memory space, and it doesn't require a second render or compute pass, saving compute time and memory bandwidth" [V, Apple Metal MSAA article]. Apple gives no numeric cost.
- Expected cost when configured correctly: 2x about 3–8%, 4x about 8–15% of GPU time, mostly per-sample depth/stencil and edge-pixel shading [E].
- The failure mode is a **stored** MSAA surface. If URP cannot merge the Render Objects portal passes into the main native render pass, the MSAA colour and depth are stored and reloaded. At scale 0.8, 4x: 1.154 Mpx × 4 samples × 8 bytes (colour + depth/stencil) ≈ 37 MB written and read back per eye, per break [D]. That turns a 10% cost into several ms.
- Check: Render Graph Viewer shows the opaque, stencil-mask and Render Objects passes merged into one native pass per camera; Xcode GPU capture shows store action "MultisampleResolve" for colour and "DontCare" for depth/stencil.
- 4x MSAA with fwidth lines and no post-processing is the right AA for this look. Agree with the Build Pipeline: no FXAA, no STP.

### A4. Vertex-shader cost of the sinh/cosh/asinh warp

Per vertex: two 3×3 multiplies, one length, sinh + cosh (one exp and one reciprocal if written as e = exp(r); sinh = (e − 1/e)/2; cosh = (e + 1/e)/2), asinh = log(s + sqrt(s² + 1)), two normalisations, then the usual view-projection. About 120–180 scalar operations [D].

| Vertices per frame (both eyes) | Ops per frame at 150/vertex | Share of A17 Pro GPU (about 2 TFLOPS peak [S], assume 1 TFLOPS sustained usable) | Time |
|---|---|---|---|
| 300k | 45 M | 0.3% | < 0.1 ms |
| 500k | 75 M | 0.5% | < 0.1 ms |
| 1.5 M | 225 M | 1.4% | about 0.2 ms |

Even with a 5x pessimism factor the transcendental math is under 0.5 ms [D/E]. **The warp arithmetic is not the cost; the triangle count that the subdivision creates is.** Three cheap wins anyway:
- Compute exp once and derive sinh and cosh from it; do not call both intrinsics.
- Use `half` only for colour; keep positions `float` (precision at d ≈ 4 needs it).
- Upload `_HypCamInv · _RoomAnchor` as a single per-material matrix when the room anchor is static per frame (one multiply instead of two); set it with `Material.SetMatrix`, which stays SRP Batcher compatible.

### A5. Triangle and draw-call arithmetic for the hyperbolic wing

{4,5} tiling: tile area = 2π − 4·(2π/5) = 2π/5 ≈ 1.257 (curvature −1). Disc area = 2π(cosh r − 1). Tile side = 2·acosh(cos(π/4)/sin(π/5)) ≈ 1.254 [D].

| Cull radius d | Disc area | Tiles visible (≈ area / 1.257) | Draws (2 eyes, 1 mesh per tile) |
|---|---|---|---|
| 2 | 17.4 | 14 | 28 |
| 3 | 56.9 | 45 | 90 |
| 4 | 165 | 132 | 264 |
| 5 | 460 | 366 | 732 |
| 8 | 9,360 | 7,400 | 14,900 |

Triangles per tile (floor + ceiling + walls ≈ 12–28 m² of surface depending on L and wall openness) [D]:

| Subdivision | Triangles per m² | Triangles per tile (12–28 m²) | d = 3 (45 tiles), per eye | d = 4 (132 tiles), per eye |
|---|---|---|---|---|
| 0.25 m | 32 | 380–900 | 17–40k | 50–119k |
| 0.15 m | 89 | 1.1–2.5k | 48–112k | 141–329k |
| 0.10 m | 200 | 2.4–5.6k | 108–252k | 317–739k |

Conclusions:
- **Default: d = 3, 0.25 m subdivision, 0.1 m on the home tile only.** About 90 draws and under 100k triangles per frame for the shell, leaving 200 draws for props, portals, avatar and UI.
- d = 4 at 0.25 m is the "should" tier (264 draws, needs prop merging).
- The Build Pipeline's fog distance of 5–8 units must be changed to "fully fogged by the cull radius" (3–4).
- One material instance per tile with a different `_RoomAnchor` is fine: the SRP Batcher batches by shader variant, and "you can still use as many different materials with the same shader as you want" [V, Unity SRP Batcher manual]. It lowers CPU set-up between draws; it does **not** reduce the draw count. Pool the material instances (allocate 150 at load, reassign anchors) so tile streaming creates no garbage.
- Alternative that halves draws further (stretch): one mesh per tile *type*, `Graphics.RenderMeshInstanced` with the anchor in an instanced property. This leaves the SRP Batcher path, so only do it if the render thread is the measured bottleneck.

### A6. Glued room copies

| Grid | Copies | Draws (2 eyes, 1 merged mesh per copy) | Triangles per frame at 5k per copy |
|---|---|---|---|
| 3 × 3 | 9 | 18 | 90k |
| 5 × 5 | 25 | 50 | 250k |
| 7 × 7 | 49 | 98 | 490k |

[D] 7×7 at 5k sits exactly on the upper triangle budget, and only if every copy is one merged mesh. Ten unmerged props per copy would be 980 draws. **Default 5×5; 7×7 is a quality tier for a cool phone.** Negative-scale copies are drawn in a separate batch from positive-scale ones (winding flip), which costs one extra state change, not more.

**Room scan interaction:** if the scanned room becomes the fundamental domain, 25 copies × a raw 100k-triangle LiDAR mesh = 2.5M triangles per eye. Decimate the scan to ≤ 5k triangles for copies (full mesh only in copy (0,0)), or show copies as the decimated shell plus edge lines.

### A7. Two cameras versus one camera drawing both eyes

- Unity's single-pass instanced stereo is not available without an XR display provider, so both options issue every draw twice. The single-camera custom feature saves only the fixed per-camera work: one extra cull, one extra URP camera set-up, one extra set of per-camera constants and one extra final blit.
- Fixed per-camera overhead in URP on this class of CPU: about 0.4–0.8 ms main thread plus 0.2–0.5 ms render thread [E]. With huge `localBounds` everything passes the frustum test, so culling is O(renderers) at well under 1 µs each: under 0.3 ms for 300 renderers [E].
- Draw submission dominates: about 6–10 µs per SRP-batched draw on the render thread [E], so 300 draws ≈ 1.8–3 ms.
- **Verdict: two cameras. Expected penalty about 0.6–1.3 ms CPU, not worth custom render-feature work in 36 h.** Measure it once (empty scene, one camera vs two) and move on.
- Rules that keep the two-camera rig cheap: both eye cameras share one URP renderer and one set of renderer features; no camera stacking; no per-camera post; CPU tile culling and holonomy run once per frame, not per camera; disable the AR camera's own rendering (the tracked camera only carries the pose).

### A8. CPU work specific to ATLAS

| Task | Cost | Tag |
|---|---|---|
| Holonomy update (3×3 double multiply, η-Gram–Schmidt, inverse, cast) | < 5 µs | [D] |
| Visible-tile set (hyperbolic distance test on ≤ 400 tiles, cosh d = −⟨p,q⟩) | < 50 µs | [D] |
| Tile-graph expansion on a tile crossing (adds ≤ 50 tiles) | 0.2–1 ms on that frame; do it incrementally or in a job | [E] |
| Tesseract slice, 48 tetrahedra, plain C# | about 10–20 µs + `Mesh.SetVertices` about 20–50 µs | [E] |
| Slicer at 5,000 tetrahedra | about 1 ms plain C#, about 0.1 ms with Burst | [E] |
| Pose packet (LiteNetLib, 60 Hz, about 64 bytes) | < 30 µs | [E] |

None of this needs a compute shader for Phase 1. Agree with the Build Pipeline over the Spec (compute slicer, prefix-sum compaction): CPU slicing is at least 50x under budget for one tesseract. Use preallocated `NativeArray`/`List` buffers and `Mesh.MarkDynamic()` so the slicer allocates nothing per frame.

### A9. Frame-time budget at 60 fps (16.67 ms)

Main thread, render thread and GPU run in parallel; each must fit, with headroom for throttling. GPU frequency can fall to 50–70% of peak under heat (A1), so a GPU frame that takes 8 ms cold can take 11–16 ms hot [D]. That sets the cold targets.

| Stage | Thread | Target (ms) | Cap (ms) | Notes |
|---|---|---|---|---|
| AR Foundation / ARKit session update, pose, input | main | 1.0 | 1.5 | [E] No camera background, planes, meshing or occlusion after set-up |
| Scripts: holonomy + visible tiles + slicer + blade logic | main | 0.5 | 1.0 | [D] from A8 |
| Networking + telemetry + HUD | main | 0.2 | 0.5 | HUD text at 2 Hz |
| URP set-up and culling, 2 cameras | main | 1.2 | 2.0 | [E] |
| Engine overhead (player loop, transforms, TextMeshPro, GC) | main | 0.6 | 1.0 | zero per-frame allocations |
| **Main thread total** | | **3.5** | **6.0** | headroom ≥ 10 ms |
| Draw submission, ≤ 300 draws | render | 2.5 | 4.0 | [E] |
| Metal encode, present | render | 0.5 | 1.0 | |
| **Render thread total** | | **3.0** | **5.0** | |
| Opaque + warp + fog, per eye (scale 0.8, 4x MSAA) | GPU | 1.6 | 2.5 | [E] |
| Stencil portals (≤ 2, no recursion), per eye | GPU | 0.5 | 1.0 | [E] |
| Resolve + upscale blit, per eye | GPU | 0.2 | 0.3 | |
| **GPU per eye** | | **2.3** | **3.8** | |
| **GPU total, both eyes (cold)** | | **4.6** | **7.6** | hot = cold ÷ 0.6 → 7.7 to 12.7 |
| Headroom at the cap, hot | | — | ≥ 4 ms | |

These are estimates; replace them with measured values after gate G0 (section E).

---

## B. Latency

### B1. Where the time goes

`ARFrame.timestamp` is "the time at which the frame was captured", and the frame's camera carries the pose for that image [V, Apple ARFrame docs via search]. ARKit does not predict to display time.

| Stage | Time (ms) | Tag |
|---|---|---|
| Camera exposure mid-point → ARKit delivers the frame (sensor readout, ISP, VIO) | 20–35 | [E] |
| Wait for the next Unity `Update` | 0–16.7, mean 8 | [D] |
| `Update` → frame presented (main + render thread, GPU, vsync) | 16.7–33 | [E] |
| Scan-out to the middle of the panel at 60 Hz | about 8 | [D] |
| **Total motion-to-photon** | **about 55–95, centre about 70** | [D/E] |

Cross-check [S]: a robotics paper reports about 140 ms "movement to pose update" for ARKit on an iPhone 15 Pro (search snippet, arXiv 2407.10353; it includes their own transport, could not be read in full). Treat 70 ms as optimistic and 140 ms as the pessimistic bound until measured.

What it means: at a 100°/s head turn, 70 ms is a 7° world lag [D]. VR practice aims for 20 ms. This will read as "swim" on fast turns. It is tolerable for slow walking demos, and it is the main reason to keep head turns slow in the demo script and keep landmarks far rather than near the face.

### B2. "Update only" versus "Update and Before Render"

- Unity's own guidance: Update And Before Render is the default and recommended setting for a camera, "to ensure user comfort and minimize latency" [S, Input System TrackedPoseDriver docs via search].
- On this stack the before-render sample runs about 3–5 ms after `Update`. ARKit produces a new pose only every 16.7 ms, so the second sample differs in roughly 3.5/16.7 ≈ 20–25% of frames and by one whole ARKit step when it does. Mean latency gain ≈ 0.22 × 16.7 × (fraction usable) ≈ **1 ms at best**, and it turns a steady latency into one that alternates by 16.7 ms on a fifth of frames, which is visible judder [D]. Headsets benefit from before-render because their IMU runs at 500–1000 Hz; ARKit's pose does not.
- **Recommendation: keep the Build Pipeline setting (Update only).** One pose sample per frame feeds both holonomy and the cameras, so the virtual and physical motion stay consistent.

### B3. What a 36-h team should do

| Level | Action | Cost | Expected effect |
|---|---|---|---|
| Minimal | Update only; `targetFrameRate = 60`; 60 Hz ARKit configuration; ProMotion off; measure once (B4) and publish the number | 30 min | none; honest number |
| Target | **Gyro rotation prediction in C#.** Read `Input.gyro.rotationRateUnbiased` (Core Motion through Unity, no plugin), compute `q_render = q_arkit · exp(½ · ω_body · τ)` with τ tuned between 30 and 60 ms, apply to the rig's rotation in `Application.onBeforeRender` **after** holonomy has consumed the unpredicted pose. Position is not predicted. | 1.5–2.5 h including the device-to-camera axis mapping in landscape | removes roughly τ of rotational lag: 70 → about 20–30 ms for rotation [E]. Over-prediction shows as overshoot; pick τ from the slow-motion test, and clamp ω above 300°/s |
| Stretch | Late rotational warp in a custom final pass (Spec D16) | 5–8 h and requires the custom distortion pass | not recommended this weekend |

Guard rails for the Target level: a kill switch (prediction off) on the operator console; holonomy, networking and telemetry always use the unpredicted ARKit pose; only keep it if the measurement improves and three people see no jitter.

### B4. Measurement

1. **Slow-motion video (primary).** A second phone films at 240 fps (4.17 ms per frame) with both the test phone's body and its screen in shot. The app shows a world-locked vertical line. Tap or twist the phone sharply against a stop. Count frames from the first physical movement to the first on-screen movement. Ten trials, report mean and range. Resolution ±4 ms.
2. **Gyro-step split (in-app, no camera).** Log the timestamp when |ω| first exceeds 0.5 rad/s and the timestamp of the first frame whose ARKit rotation changes by more than 0.2°. The difference is sensor-to-`Update` latency. Add a full-screen flash driven directly by the gyro threshold: in the slow-motion video, movement → flash is the render path alone; movement → line shift is the ARKit path. The difference isolates ARKit's share.
3. **Publish:** motion-to-photon (rotation) with prediction off and on.

---

## C. Thermal

### C1. The Unity cap

- Unity 6.3 generates `Classes/UnityAppController+Thermal.mm`; `adjustFrameRateForThermalState:` returns 30 fps at `NSProcessInfoThermalStateSerious` and 15 fps at Critical regardless of `targetFrameRate` [V, Unity Discussions thread 1731379, code quoted there, verified on 6000.3.18f1].
- Unity staff said the UI was missing from the backport and "should be fixed in next release (6000.3.21f1)"; a user confirmed the "Adjust iOS FPS based on thermal state" setting appears in 6000.3.21f1 [V, same thread]. Unity 6.5 exposes the setting plus "Serious Thermal State FPS" and "Critical Thermal State FPS" [V, 6000.5.0a4 release notes].
- **Action: pin Unity ≥ 6000.3.21f1.** If the team is on an older 6.3 patch, add the `[PostProcessBuild]` script from that thread, which rewrites the method to `return targetFPS;`.
- Whether to disable it: yes, and own the policy in the ladder below. A silent drop to 30 fps mid-demo with no reprojection is the worst outcome; a controlled quality drop at "Fair" is better. Apple's state definitions: Fair "slightly elevated", Serious "high", Critical "significantly impacting the performance of the system and the device needs to cool down" [V].

### C2. How long before "Serious"

No primary measurement exists for this workload. Evidence and estimate:

- At 100% GPU load the 15 Pro Max loses 30–40% within a 20-minute stress run and reaches a 50% floor by about 40 minutes [V/S, A1].
- ATLAS runs the GPU at roughly 30–45% (A9), plus ARKit world tracking (camera, ISP and about one CPU core of VIO), plus the display, inside a headset with poor airflow.
- Reports: when hot, ARKit's own frame rate can halve until the phone cools (Apple developer forum 689458) [S]; AR apps commonly overheat phones in 15–30 minutes [S, low quality].
- **Estimate at 22–24 °C ambient, closed headset, 60 fps, brightness about 50%: Fair in 3–8 min, Serious in 12–25 min** [E]. With LiDAR meshing or hand tracking left on, expect the lower end. The Spec's working assumption was 20 minutes before a tier drop.
- This is exactly what the 15-minute soak measures. Do it in Phase 1, not on demo day.

### C3. Mitigation ladder (ordered by benefit per effort)

| # | Lever | Why it helps | Cost |
|---|---|---|---|
| 1 | Dark palette and dark fog | OLED power scales with emitted light; a dark "diagram" look cuts display heat directly | art decision |
| 2 | Brightness fixed at about 40% (`Screen.brightness`), auto-brightness off | lenses and a dark surround need little light | 5 min |
| 3 | ARKit: lowest-resolution 60 fps configuration; planes, meshing, occlusion, light estimation, environment probes off after set-up | less ISP and CPU work | 30 min |
| 4 | Room scan as a bounded phase (≤ 60 s of LiDAR meshing), then `ARMeshManager` off | LiDAR meshing is a major heat source | design rule |
| 5 | No charging while running; set the iOS 80% charge limit and keep the battery above it during USB profiling | charging heat adds to SoC heat [E, verify the limit stops charging over USB] | 2 min |
| 6 | Case off; headset with an open back; phone out of the headset and in front of a desk fan between users | passive cooling | — |
| 7 | Quality ladder driven by the thermal plugin and by GPU-time trend | see below | 2 h |
| 8 | Two phones, swap at "Fair" or every 15–20 min | resets the clock | ops |
| 9 | MagSafe Peltier cooler on the phone's back if the headset leaves it exposed and the cooler clears the cameras and LiDAR | reviewers report much higher stress-test stability with active coolers [S, not verified]; adds about 70–100 g and a power cable | about 30–50 USD |

Avoid frozen gel packs (condensation).

Quality ladder (replace the Build Pipeline's with this; change at most one step every 20 s, with hysteresis):

| Trigger | Render scale | MSAA | Hyperbolic cull d | Glued grid | Other |
|---|---|---|---|---|---|
| Nominal | 0.8 | 4x | 3 (4 on "high") | 5×5 (7×7 on "high") | — |
| Fair, or GPU p95 > 9 ms | 0.7 | 4x | 3 | 5×5 | hands off; console shows amber |
| Serious, or GPU p95 > 12 ms | 0.6 | 2x | 2 | 3×3 | console shows "swap phone now"; stay at 60 |
| Still missing 60 for 10 s, or Critical | 0.6 | off | 2 | 3×3 | drop to a clean locked 30 and end the session |

### C4. Is 30 fps render with 60 Hz reprojection feasible here?

Technically yes, without native code: run Unity at 60; render the two scene cameras into persistent eye textures on alternate frames (toggle `camera.enabled`), with about 15% extra FOV; every frame a final camera draws two quads that sample those textures through a rotation-only homography built from the newest pose.

Costs and limits [D/E]:
- 4–6 h for someone who has written a reprojection shader before, plus tuning.
- The eye textures must be stored (resolved, non-memoryless), plus overscan: about 30% more pixels.
- Rotation only. At 1 m/s walking and 33 ms between renders, an object 1.5 m away shifts by 3.3 cm ≈ 1.3° between real frames and is shown twice: near geometry judders. ATLAS rooms are near geometry.
- It only saves about half the scene cost; C3 steps 1–8 are cheaper.

**Verdict: stretch only.** Plain 30 fps without reprojection doubles each image; at a 100°/s turn the two copies are 1.7° apart [D]. Use it only as the last ladder rung.

### C5. Display notes

- Run at 60 Hz. ProMotion (120 Hz) needs its own Player Setting and doubles the work for no tracking benefit, since ARKit delivers 60 Hz.
- The OLED is full-persistence at 60 Hz, so fast head turns smear regardless of frame rate. This cannot be fixed in software; it is another reason for a calm demo script.

---

## D. Metric set, targets and capture

### D1. Performance and correctness metrics

"Hot" = after the 15-minute in-headset soak. All frame statistics are taken from a Release build (see E2).

| # | Metric | Must | Should | Stretch | Capture |
|---|---|---|---|---|---|
| 1 | Frame time p50 | ≤ 16.7 ms | ≤ 16.7 | ≤ 16.7 | `FrameTimingManager` `cpuFrameTime`, ring buffer of 3,600 frames |
| 2 | Frame time p95 | ≤ 20 ms | ≤ 17.5 | ≤ 16.9 | same |
| 3 | Frame time p99 | ≤ 33.3 ms | ≤ 25 | ≤ 18 | same |
| 4 | Frames over 16.7 ms (+0.5 tolerance) | ≤ 5% | ≤ 1% | ≤ 0.2% | same |
| 5 | Frames over 33.3 ms | ≤ 0.5% | ≤ 0.1% | 0 | same |
| 6 | Sustained fps, hot (rubric line is 30+) | ≥ 45 mean, never < 30 | 60 locked | 60 locked at scale 1.0 | same + Metal HUD cross-check |
| 7 | CPU main thread p95 | ≤ 8 ms | ≤ 6 | ≤ 4 | `cpuMainThreadFrameTime` |
| 8 | CPU render thread p95 | ≤ 6 ms | ≤ 5 | ≤ 3 | `cpuRenderThreadFrameTime` |
| 9 | GPU time p95, cold / hot | ≤ 9 / ≤ 14 ms | ≤ 7 / ≤ 11 | ≤ 5 / ≤ 8 | `gpuFrameTime` (on Metal it "may exceed total frame time" due to TBDR [V]; cross-check with the Metal HUD) |
| 10 | Draw calls per frame (both eyes) | ≤ 300 | ≤ 200 | ≤ 120 | `ProfilerRecorder(ProfilerCategory.Render, "Draw Calls Count")` |
| 11 | Set-pass calls | ≤ 60 | ≤ 30 | ≤ 15 | `"SetPass Calls Count"` |
| 12 | Triangles per frame (both eyes) | ≤ 500k | ≤ 300k | ≤ 200k | `"Triangles Count"` |
| 13 | Memory (resident) | ≤ 1.5 GB | ≤ 1.0 GB | ≤ 700 MB | `ProfilerRecorder(ProfilerCategory.Memory, "System Used Memory")` |
| 14 | Managed allocation per frame in steady state | ≤ 1 kB | 0 | 0 | `"GC Allocated In Frame"` |
| 15 | Thermal timeline, 15-min soak | no Critical; Serious not before 10 min | no Serious in 15 min | Nominal/Fair only at 20 min | 10-line `.mm` plugin returning `NSProcessInfo.thermalState`, polled every 2 s, logged with time |
| 16 | Tile-graph generation at load (≥ 1,000 tiles) | ≤ 200 ms | ≤ 50 ms | ≤ 10 ms | `Stopwatch` + `ProfilerMarker("Atlas.TileGraph")` |
| 17 | Tile-graph work on a tile-crossing frame | ≤ 2 ms | ≤ 0.5 ms | ≤ 0.1 ms | `ProfilerMarker` read through `ProfilerRecorder` |
| 18 | Holonomy update | ≤ 0.1 ms | ≤ 0.02 | — | `ProfilerMarker("Atlas.Holonomy")` |
| 19 | 4D slicer per frame (tesseract) | ≤ 0.5 ms | ≤ 0.2 | ≤ 0.05 (Burst) | `ProfilerMarker("Atlas.Slicer4D")` |
| 20 | Motion-to-photon, rotation | measured and reported; ≤ 100 ms | ≤ 50 ms with gyro prediction | ≤ 30 ms | B4 slow-motion video |
| 21 | Pose → spectator view latency, p95 | ≤ 150 ms | ≤ 60 ms | ≤ 35 ms | send timestamp in each packet; clock offset from an NTP-style ping exchange over LiteNetLib; PC logs receive and display time |
| 22 | Pose link rate and loss | ≥ 50 Hz, < 2% loss | 60 Hz, < 0.5% | — | sequence numbers, PC side |
| 23 | Tracking drift, 20 m closed loop (physical, raw ARKit pose) | < 2% (40 cm) | < 1% (20 cm) | < 0.5% | start and end on a floor mark; app logs pose at two button presses; three runs, in the headset |
| 24 | Lorentz drift ‖Cᵀ·η·C − η‖ (Frobenius, double) | < 1e-9 at all times | < 1e-12 | — | computed every 60 frames in-app; editor test of 100,000 random boosts (about 28 min at 60 fps) with and without renormalisation |
| 25 | Float32 upload error (‖p‖²_η + 1 for tile anchors at the cull radius) | < 1e-3 at d = 4 | < 1e-4 | — | editor test |
| 26 | Holonomy correctness: walk a geodesic triangle, measured rotation vs angle defect (= enclosed area, Gauss–Bonnet) | error < 1° | < 0.1° | — | editor test with a synthetic path; this is the publishable "mathematical correctness" number |
| 27 | Slicer correctness: slice of a tesseract at w = 0 is a cube; volume error | < 1e-4 | < 1e-6 | — | editor test |
| 28 | Portal seam: pixel difference between through-portal and direct view of the same room | no visible seam | < 1 px offset | — | golden screenshot pair |

Notes on capture:
- `FrameTimingManager` results arrive four frames late and are on in development builds; release builds need Player Settings → "Frame Timing Stats" [V]. Using a `ProfilerRecorder` enables it automatically [V].
- Render counters ("Draw Calls Count", "SetPass Calls Count", "Triangles Count") through `ProfilerRecorder` are reported by Unity as available in release players [S, from memory of the Rendering Profiler module page; confirm on the first build].

### D2. UX metrics (rubric: UX and Interaction 30%)

| Metric | Must | Should | Stretch | Capture |
|---|---|---|---|---|
| Fast Motion Sickness scale (0–20, one spoken number per minute; Keshavarz and Hecht 2011 [S]) | median peak ≤ 5; stop the session at ≥ 10 [E, team to confirm threshold] | median peak ≤ 3; rise ≤ 2 (Spec spike 0.7) | ≤ 2 | operator types the number on the PC console; logged with session time |
| Pointing-to-origin error (after a 3-leg walk, point back to the start) | measured in Euclidean and hyperbolic rooms, n ≥ 5 | hyperbolic with landmarks + mini-map within 1.5x of the Euclidean error | within 1.2x | head-forward ray at a button press vs true bearing; logged automatically. The gap between conditions is itself a result: it shows the geometry is not cosmetic |
| Single Ease Question (1–7) per task | median ≥ 5 | ≥ 5.5 (about the published cross-study average [S]) | ≥ 6 | one question on the PC after each blade |
| Time to first successful walk-through (onboarding) | ≤ 90 s | ≤ 60 s | ≤ 30 s | auto from event log |
| Sessions ended early for discomfort | ≤ 1 in 5 | 0 | 0 | operator flag |

### D3. Capture architecture (about 3 h of work, do it in Phase 1)

1. **`AtlasPerfProbe` (one MonoBehaviour).** Holds the `ProfilerRecorder`s above, calls `FrameTimingManager.CaptureFrameTimings()` each frame, keeps a 3,600-frame ring buffer, and once per second computes p50/p95/p99, percentage over 16.7 and 33.3 ms, counters, memory, thermal state, render scale, MSAA, scene id, visible tiles and copies.
2. **CSV over the link.** One row per second (about 250 bytes) to the PC on a reliable LiteNetLib channel; the PC appends to `perf_<session>.csv`. Also write the file locally under `Application.persistentDataPath` (enable `UIFileSharingEnabled`) so a lost link loses nothing.
3. **In-headset HUD.** One TextMeshPro quad, toggled by a triple tap or from the PC: fps, p95, CPU main / render / GPU ms, draws, triangles, thermal state, ladder tier. Refresh at 2 Hz with a cached `StringBuilder` so it allocates nothing.
4. **PC console.** Live line chart of frame time and a thermal colour bar; this doubles as the judges' "Performance" dashboard panel (Spec §13.1).
5. **Soak script.** A scripted 15-minute auto-tour (camera on a recorded pose track or a volunteer walking) that cycles the heaviest scenes, then prints the D1 table. One CSV per soak goes in the repository as evidence for the documentation score.

---

## E. Optimisation checklist, profiling workflow and gates

### E1. Checklist, ordered by expected gain per hour

| # | Action | Expected gain | Effort |
|---|---|---|---|
| 1 | `Application.targetFrameRate = 60`; Unity ≥ 6000.3.21f1 with the thermal FPS setting off (or the post-build patch) | 30 → 60 fps | 10 min |
| 2 | Xcode scheme: Release configuration, debugger detached, Metal API Validation off for any measurement | 1–3 ms CPU [E] | 5 min |
| 3 | URP asset per Build Pipeline §4: HDR off, depth/opaque texture off, depth priming off, no post, shadows off, additional lights off, SRP Batcher on | 2–4 ms GPU, avoids extra passes [E] | 15 min |
| 4 | ARKit: lowest 60 fps video format; managers off after set-up; no `ARCameraBackground` in VR scenes | about 1 ms CPU plus heat | 30 min |
| 5 | Merge each room or copy into one mesh, one shared shader, palette texture; no MaterialPropertyBlocks | draws ÷ 5–10 | 1 h, mostly authoring discipline |
| 6 | CPU tile culling at d = 3 with fog opaque at the cull radius | bounds the worst case | 1 h |
| 7 | Subdivision LOD: 0.1 m on the home tile, 0.25 m elsewhere | triangles ÷ 3–6 | 1 h in Blender |
| 8 | Confirm MSAA is resolved on-tile and passes are merged (Render Graph Viewer + one Xcode capture) | avoids a 2–5 ms trap | 30 min |
| 9 | Zero per-frame allocations (pooled materials, preallocated slicer buffers, no LINQ or string building in `Update`) | removes GC spikes: p99 | 1 h |
| 10 | Dark palette, brightness 40% | several minutes more before Fair [E] | 10 min |
| 11 | Thermal plugin + quality ladder (C3) | graceful instead of abrupt | 2 h |
| 12 | Decimate the scanned room mesh (≤ 60k triangles drawn once; ≤ 5k per copy) | makes room scan viable | 1–2 h |
| 13 | Front-to-back order: draw the home tile first, far tiles last (set `renderQueue` or sorting by graph distance) | small on TBDR (hidden surface removal is automatic); skip unless measured | — |
| 14 | Gyro rotation prediction (B3) | comfort, not fps | 2 h |
| 15 | Hidden-area mask; `RenderMeshInstanced` for tiles; Burst slicer | each < 1 ms | stretch |

Do not spend time on: baked occlusion culling (broken by the warp), static batching (SRP Batcher path is enough), GPU Resident Drawer, compute-shader slicer, single-camera stereo.

### E2. Profiling workflow

1. **Always-on:** the in-app HUD and the 1 Hz CSV (D3). This is the first thing to look at.
2. **Metal Performance HUD** for an independent fps and GPU-time reading: Settings → Developer → Graphics HUD, or the `MetalHudEnabled` key in Info.plist, or the `MTL_HUD_ENABLED=1` environment variable in the Xcode scheme [V, Apple "Monitoring your Metal app's graphics performance"].
3. **CPU-bound** (main or render thread over target): Unity Profiler attached over USB to a Development Build; Timeline view; look for per-camera URP cost, script markers, `GC.Alloc`. Deep Profile only in the editor.
4. **Too many draws or set-passes:** Frame Debugger on the device; read the "SRP Batch" break reasons.
5. **Pass structure:** Render Graph Viewer; confirm one native pass per eye and no unexpected intermediate textures.
6. **GPU-bound:** Xcode GPU frame capture (Metal debugger): per-pass time, load/store actions, overdraw, vertex count per draw. Instruments "Game Performance" template for thermal state against GPU time over a soak.
7. **Rule:** numbers that get published come from a Release build with Frame Timing Stats on, in the headset, not on a desk and not from a Development Build.

### E3. Performance gates

**G0 — end of hour 2 (empty stereo scene on the phone)**
- Two-camera rig, empty scene: 60 fps on the Metal HUD; CPU main ≤ 3 ms, GPU ≤ 2 ms.
- HUD and thermal plugin working; Unity version and thermal setting confirmed.
- If this fails, nothing else starts.

**G1 — end of Phase 1 (tech + spaces + phone linked to PC)**
- Each space alone (Euclidean room, glued room 5×5, H²×E at d = 3, portal pair, tesseract slicer): metrics 1–12 at "must" cold.
- Worst-case combined scene: frame p95 ≤ 20 ms cold.
- One 15-minute in-headset soak on the heaviest scene: no Critical, ≥ 45 fps mean, CSV saved. Time-to-Fair and time-to-Serious recorded.
- Editor tests 24–27 green.
- Phone ↔ PC: pose at ≥ 50 Hz, p95 latency ≤ 150 ms, perf CSV arriving at 1 Hz, clock offset estimated.
- Motion-to-photon measured once and written down.
- 20 m loop drift measured once in the real free-walk space.
- Room scan (if in Phase 1): scan-to-walkable ≤ 90 s; meshing confirmed off afterwards; scanned mesh triangle count logged.

**G2 — end of each blade / use-case phase**
- Blade scene at "must" on all of D1 rows 1–14, hot.
- No new shader variants beyond the agreed set (set-pass ≤ 60).
- FMS run with at least 3 people who did not build it: median peak ≤ 5.
- Kill switches for anything the blade added (hands, extra portals, extra copies).

**G3 — demo freeze (at least 4 h before judging)**
- Full demo path, two consecutive runs on one phone without cooling in between: no Serious, frame p99 ≤ 33 ms.
- "Should" column reached on rows 1–9, or the miss is documented with the cause.
- Final metric table and one soak chart exported for the README and slides.
- Ladder tested by forcing each state from the console.

---

## F. Scope options (4 people, about 36 h)

| Level | Performance work included | Person-hours |
|---|---|---|
| **Minimal** | Settings (E1 #1–4), draw and triangle budgets enforced by convention, Metal HUD only, one manual 15-min soak, numbers typed into the README. Update-only pose. Phone swap as the only thermal plan. | about 3 |
| **Target** | Minimal + `AtlasPerfProbe`, HUD and CSV to PC (D3), thermal plugin and ladder, editor correctness tests 24–27, gates G0–G3, slow-motion latency number, loop-drift number, FMS and SEQ logging, gyro rotation prediction behind a kill switch. | about 12–14 (one person half-time plus help at gates) |
| **Stretch** | Target + hidden-area mask, instanced tiles, Burst slicer, 30/60 reprojection rung, automated soak replay, MagSafe cooler trial, pointing-error study with n ≥ 8. | + 12–16 |

Recommendation: **Target**. It is the level that produces evidence for three rubric lines at once (30+ fps, efficiency of geometric computation, mathematical correctness) and it makes the Phase 1 "phone connected to PC" goal useful from the first hour, because the link's first payload is the perf CSV.

Suggested ownership: one person owns the probe, the HUD, the thermal plugin and the gates (sequential dependency: needs the stereo rig and the LiteNetLib link, both Phase 1). Editor correctness tests are written by whoever writes the holonomy and slicer code, in parallel. Soaks need a second phone or a 15-minute pause for one phone; schedule them at meal breaks.

---

## G. Conflicts found between documents

| Topic | Spec | Build Pipeline | Recommendation |
|---|---|---|---|
| Stereo path | one camera + custom render feature (D5) | two cameras | two cameras (A7) |
| Eye buffer | 0.87, 4x MSAA | 0.75–0.8, 2x or 4x | 0.8, 4x; ladder down |
| Triangles | ≤ 150k per eye | 300–500k per frame | same thing; keep 500k per frame as the must line |
| Draws | < 100 per eye | < 300 total | ≤ 300 total must, ≤ 200 should |
| Latency | Core Motion prediction + late warp (D16) | Update only | Update only + optional gyro prediction in C# |
| Serious tier | 30 fps render, warp to 60 | accept Unity's 30 cap | disable the cap; ladder; plain 30 only as last rung |
| Slicer | compute shader | CPU | CPU |
| Fog / cull radius | hard draw radius 3–4 R | cull d > 4, fog 5–8 | cull d = 3 default (4 high), fog opaque at the cull radius |
| Subdivision | ≤ 0.2 R (about 0.44 m at R = 2.2) | 0.1–0.25 m | 0.25 m, 0.1 m home tile |
| Play space | taped 3×3 m | taped square, VR mode | superseded by free walk + room scan; drift and scan-mesh budgets added here |
| Thermal cap version | — | setting only on 6.5+ | setting exists from 6000.3.21f1 |

---

## Could not verify

1. Minutes to Fair and Serious for this exact workload (C2). No primary data; needs the soak.
2. Motion-to-photon on this stack (B1). The 55–95 ms figure is derived; the 140 ms figure is from a search snippet of a paper I could not read.
3. Whether AR Foundation's ARKit provider returns a newer pose in the before-render input update than in `Update` within the same frame (B2). The recommendation holds either way.
4. Wild Life Extreme score for Adreno 650 / Snapdragon 865 (about 1,100–1,200, from memory) and any direct benchmark of a Quest headset.
5. Notebookcheck's loop-by-loop throttling numbers (the page returned 403; figures are from the search snippet).
6. MSAA percentage cost on A17 Pro; Apple publishes no number.
7. Per-camera URP overhead and per-draw microseconds on A17 Pro (A7); the URP camera page gives no cost figures.
8. A17 Pro GPU at about 2 TFLOPS; Quest default eye-buffer sizes (from memory).
9. Which render counters `ProfilerRecorder` exposes in a release iOS player.
10. Effect of Peltier MagSafe coolers on sustained performance; whether the iOS 80% charge limit stops charging heat while on USB.
11. The exact list of 60 fps ARKit video formats on the 15 Pro Max and whether the 1280×720 format remains available with LiDAR scene reconstruction enabled.
12. Whether Unity on iOS can pace at 40 fps on the 120 Hz panel (a possible intermediate rung instead of 30).
13. Meta's device-comparison table was read through a summariser; confirm the Quest 2 "< 100 draw calls" line before quoting it.
14. FMS stop threshold (≥ 10) and the SEQ average (about 5.5) are from memory of the literature.

## Questions for the team

1. Which exact Unity patch is installed? If it is below 6000.3.21f1, can you update before the event, or do we ship the post-build patch?
2. Which headset is it, and is the phone's back exposed? This decides the MagSafe cooler option, airflow, and how much of each half-screen the lenses actually show (hidden-area mask, usable FOV).
3. Is there a second iPhone 15 Pro-class phone for swapping and for the 240 fps latency video, or only one?
4. Free walk: what is the largest area you expect to walk (venue floor), and is passthrough needed for safety there? Passthrough brings back `ARCameraBackground` and its cost in stereo.
5. Room scan: is the scanned mesh shown once (home tile) or repeated in every hyperbolic tile and glued copy? Repetition forces decimation to about 5k triangles per copy.
6. Does LiDAR meshing stay on during walking (for obstacle safety), or only during a scan phase? This is the biggest single thermal decision.
7. What curvature scale L (metres per hyperbolic unit) will the walked H²×E wing use? It sets tile size, the number of visible tiles and the subdivision needed.
8. Is a locked 60 fps the goal, or is a stable 45–60 with a 30 fps floor acceptable for judging, given the rubric only asks for 30+? This decides how aggressive the ladder is and whether any stretch work is justified.
