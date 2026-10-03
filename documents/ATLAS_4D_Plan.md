# ATLAS: 4D Plan (combined)

*3 Oct 2026. This merges the 4D Plan (projection and slicing engine) with the 4D Section Plan (Breath Room ring, safety, and the review of the "Heartbeats, Eyes and Gait" proposal) into one plan for the 36-hour build. It fits the Engineering Spec (§5.3 4D slicer, `ISlicer4D`, §11.2.1 Breath Room, D5 Look-Down Shadow).*

A working prototype of the engine already exists: **[demo/4d-ar/](../demo/4d-ar/)** (WebXR AR + desktop preview). The maths core is `four.js`, with 7 passing tests, and it ports one-to-one to the Unity classes in §6.

---

## 1. Decisions

| # | Decision | Source |
|---|---|---|
| 1 | **One slicing engine, one story, three steps:** walk through 4D space → the same slicer on the breathing tumour → look down for the projection | Section plan §5 |
| 2 | **The 4D toybox (tesseract + hypersphere) is in the demo, not the roadmap.** It is the brief's own category ("4D projections… rotations in planes we cannot perceive"), cheap, and needs no data | Both |
| 3 | **Walking through w is the hero interaction.** One taped floor lane is the w axis. The gamepad trigger is its twin | Section plan §5 Step 1; 4D Plan w-lane |
| 4 | **The Breath Room is a ring** of radius **0.75–0.9 m**, not 1.2 m: a breath is a loop, so a ring has no seam at end-of-exhale | Section plan §2.2 |
| 5 | **Synthetic tumour first** (2 cm sphere on the Lujan cos⁴ curve, labelled "illustrative"); one real TCIA 4D-Lung patient only if that pipeline is ready | Both |
| 6 | **Of the eight proposed medical blades, adopt none this weekend except the Visual Field Dome** (optional, replaces FieldFit) | Section plan §1, §4 |
| 7 | **Gamepad is the primary input**; head-gaze dwell (800 ms) is the fallback; crouch-to-exit plus automatic passthrough for safety | Section plan §2.3–2.4 |
| 8 | Ship only CC0, CC BY, BSD and CMU data; NC, ND, ShareAlike and no-redistribution data stay behind a "research demo" flag | Section plan §2.1 |

---

## 2. What the judge experiences (about 2.5 min)

### Step 1: Walk through 4D space (60 s), code 4s
1. **Hypersphere warm-up.** The judge stands at the west end of a taped 2 m **w-lane**. Beside the lane, at chest height, a 4D ball's slice appears as a dot, grows into a ball, then shrinks and vanishes as they walk east. Slice radius = √(1 − w²). Narrator: *"This is Flatland's sphere, one dimension up."*
2. **Tesseract shadow.** A button swaps in the tesseract's 3D shadow (the cube-in-a-cube), coloured by w. The judge pushes a stick and it rotates in the **XW plane** and turns inside out. *"Nothing is bending. It's turning in a direction you can't point to."*
3. **Tesseract slice, corner-first.** Walking the lane again, the cross-section runs tetrahedron → truncated tetrahedron → octahedron → back, with live corner counts. Each of the 8 cubic cells has its own colour, so you can see which cubes the knife is cutting.

### Step 2: The same slicer on breathing (60 s), code 4t
4. **Breath Room ring.** The judge walks a 0.75–0.9 m ring. Their angle around it is the breathing phase, and the chest breathes with their steps. Two floor flags bracket the quiet part of the breath. The **ITV** (the tumour's shadow over the gated arc) and the duty cycle update live. *"Breathing repeats, so time is a circle: flat, but it wraps."*

### Step 3: Look down for the projection (15 s)
5. **Look-Down Shadow (D5).** When head pitch is below −35°, the slice cross-fades to the 4D→3D projection on the floor. This is the "ghost image" idiom from 4D Golf. Knife Map card: `slice: n·x = w₀`, `shadow: (x,y,z)·f/(f − w)`, live w₀ and corner count.

**Short slot (5 min):** Steps 1.2–1.3 and Step 2 only. **Optional, if time allows:** the Visual Field Dome (§5).

---

## 3. Scope tiers

| Tier | Contents | Est. dev-hours |
|---|---|---|
| **MVP (must)** | Tesseract and hypersphere, 4D rotation, shadow and slice modes, gamepad controls, palette/emissive look, edit-mode tests | **5–6 h** (maths already prototyped in `four.js`) |
| **Target** | w-lane walking, corner-first preset and sweep, per-cell colours, Breath Room ring with synthetic tumour, ITV and duty cycle, D5 Look-Down Shadow, Knife Map card, crouch-to-exit | +5–6 h |
| **Stretch** | Real TCIA 4D-Lung patient, Visual Field Dome, 24-cell and 5-cell, marker-cube slice normal (`ISlicer4D` with a quaternion), compute-shader slicer | +3–8 h each |

**Rule:** nothing from Stretch starts until the MVP runs at 60 fps in stereo on the headset phone through a 10-minute soak.

---

## 4. Floor layout (one taped 3 × 3 m square, used in turn)

```
 +---------------------------------------+   tape (3 x 3 m)
 |  0.3-0.5 m clear margin inside tape   |
 |   +-------------------------------+   |
 |   |  W ====== w-lane (2 m) ====== E|   |   Step 1: walk east = move slice +w
 |   |        [4D object, chest height]  |   object beside the lane, so it stays in view
 |   |                               |   |
 |   |          .-~~~~~-.            |   |   Step 2: Breath ring, r = 0.75-0.9 m
 |   |        /  flags   \           |   |   about 5 m of walking per breath
 |   |        \  tumour  /           |   |
 |   |          '-.___.-'            |   |
 |   +-------------------------------+   |
 +---------------------------------------+
 Exit: within 0.5 m of the tape, tracking loss, or crouch below 70% eye height for 1.5 s → passthrough
```

- **w-lane mapping:** `w₀ = lerp(−2.2, +2.2, (x − x_west) / 2 m)`, clamped. The trigger does the same when walking isn't possible.
- **Ring mapping:** `φ = atan2(z − c_z, x − c_x)`, then `bin = floor(10·(φ mod 2π)/2π)` for 10 breathing phases. Interpolate between phases for smooth motion.
- Both features use the same square at different times, so they never compete for floor.

---

## 5. Data and content

### 5.1 Synthetic tumour (default)
- A 20 mm ball whose centre moves head–feet as `z(t) = z₀ − A·cos⁴(πt)`, with A = 12 mm (Lujan model), t ∈ [0, 1) per breath.
- **No meshing pipeline is needed.** Each phase is the same sphere moved to `z(t)`. The ITV over a gated arc is the union of those spheres: a capsule, with an analytic volume. Duty cycle = arc length / 2π.
- The 4D "tube" in (t, y, z) is a swept sphere, generated analytically.
- On-screen label: "Illustrative data, not for clinical use."

### 5.2 Real-data upgrade (stretch)
- One TCIA 4D-Lung patient (CC BY 3.0): 10 phase masks, then marching cubes on the PC, then one GLB per phase (≤10k tris each). This is the only place marching cubes is needed.
- Fallback if 4D-Lung stalls: LUMIERE growth series (CC BY 4.0) as a linear-time corridor. It is flat 3D + time and not wrapped, so say so.

### 5.3 Visual Field Dome (optional, replaces FieldFit; not part of 4D)
- UWHVF (BSD-3): one JSON, about 54 test points per exam.
- Show the points on a dome around the head. Where you look is the data coordinate, so it is a sphere (S²). Walk forward to scrub years of exams.

---

## 6. Architecture

```
Assets/Atlas/Kernel/FourD/
  Mesh4D.cs            Vector4[] verts, int[] tets (4 per tet), int[] tetCell, int2[] edges, int4[] squares
  Polytopes4D.cs       BuildTesseract() (48 Kuhn tets, 6 per cell), Hypersphere (analytic slice), later 5-cell / 24-cell
  Rotation4D.cs        double4x4 from six plane angles (XY XZ XW YZ YW ZW); re-orthonormalise
  Slicer4D.cs          implements ISlicer4D; tet ∩ hyperplane → triangles (+ cell id) and wire segments; no per-frame alloc
  Projector4D.cs       perspective (x,y,z)·f/(f − w), f = 4; orthographic option (drop w, used for the ITV along t)
Assets/Atlas/Blades/FourD/
  FourDToybox.cs       hypersphere → tesseract shadow → slice; mode, presets, Knife Map state
  WLane.cs             floor position → w₀ (trigger twin)
  BreathRing.cs        ring angle → phase; flags → gated arc → ITV capsule + duty cycle
  LookDownShadow.cs    head pitch < −35° → cross-fade slice ↔ projection
Assets/Atlas/Safety/
  CrouchExit.cs        < 70% of calibrated eye height for 1.5 s → passthrough (plus boundary and tracking triggers)
Assets/Atlas/Rendering/
  FourDEdge.shader     emissive unlit, colour by w or by cell id, Cull Off, SRP Batcher compatible
Tests/EditMode/FourD/  ported from demo/4d-ar/four.test.mjs (§9)
```

The prototype maps directly onto these classes: `buildTesseract` → `Polytopes4D`, `rotation4` → `Rotation4D`, `sliceFaces/sliceEdges/sliceCorners` → `Slicer4D`, `project` → `Projector4D`.

Per-frame data flow (on the phone, about 0.2 ms of CPU for a tesseract):
```
input (stick angles, w-lane or trigger, mode)
  → R = Rotation4D(angles)                           double, renormalised
  → P = R · verts                                    16 Vector4
  → Shadow: Projector4D(P) → 32 edge rods, coloured by w
    Slice:  Slicer4D(P, n = +w, w₀) → faces coloured by cell id + wire segments
  → Mesh.SetVertices / SetIndices                    preallocated, DontRecalculateBounds
  → the normal stereo rig renders it                 real stereo depth, no special shader maths
```

Interface kept for the marker cube (stretch):
```csharp
public interface ISlicer4D { void SetSlice(quaternion normal, float w0); }
// MVP: normal fixed to +w and the object rotates instead (equivalent, simpler).
// Stretch: normal = cube quaternion q as a unit 4-vector. Fuse face anchors, One-Euro smooth,
// keep the sign continuous (a 360° cube turn tilts the slice 180°; 720° returns to start).
```

---

## 7. Maths reference

- **Tesseract:** 16 corners (±1, ±1, ±1, ±1); 32 edges (corners differing in one coordinate); 24 squares (fix two coordinates); 8 cubic cells × 6 Kuhn tetrahedra = **48 boundary tetrahedra**. Using the same Kuhn split everywhere makes shared faces match.
- **Hypersphere:** at w₀ the slice is a ball of radius √(1 − w₀²), or nothing if |w₀| > 1.
- **Plane rotation (i, j) by θ:** `p_i' = p_i cosθ − p_j sinθ`, `p_j' = p_i sinθ + p_j cosθ`. Apply in the fixed order XY, XZ, XW, YZ, YW, ZW.
- **Corner-first preset:** XW = 45°, YW = 35.26° (atan 1/√2), ZW = 30°. This sends (1,1,1,1) onto +w, length 2, so w₀ ∈ [−2, 2].
- **Slicing a tetrahedron:** `d_k = n·p_k − w₀`, with one side test (`d ≥ 0`) shared by faces, edges and corner counts.
  - 1–3 split → triangle.
  - 2–2 split → quad. With positive corners a₀, a₁ and negative b₀, b₁, the cycle (a₀b₀, a₀b₁, a₁b₁, a₁b₀) never self-intersects, so no sorting is needed.
  - 4–0 → nothing.
  - Edge point: `t = d₁/(d₁ − d₂)`, `x = p₁ + t(p₂ − p₁)`.
- **Slice wireframe:** cut each of the 24 squares, giving at most one segment each.
- **Shadow:** `k = f/(f − w)`, f = 4 (≥ 2·max|w|). High w is nearer the 4D eye: coral 400 = near, purple 400 = far. The small inner cube is the far cell.
- **Test values:** axis-aligned at w₀ = 0 gives 8 corners and face area 24; corner-first gives 4 / 12 / 6 corners at w₀ = −1.5 / −0.75 / 0.

---

## 8. Task list (one 4D owner, in order)

| # | Task | Est. | Done when |
|---|---|---|---|
| 1 | Port `four.js` → `Mesh4D`, `Polytopes4D`, `Rotation4D` + tests | 1.0 h | Tests in §9 pass in Unity edit mode |
| 2 | `Slicer4D` (faces with cell id + wire), preallocated buffers | 1.0 h | 0 B GC per frame; < 0.3 ms on device |
| 3 | `Projector4D` + rod/edge rendering + `FourDEdge.shader` | 1.0 h | Cube-in-a-cube on device; XW turns it inside out |
| 4 | `FourDToybox`: hypersphere → shadow → slice, gamepad mapping, Hub placement | 1.0 h | Usable with gamepad only; Safe Harbour still works |
| 5 | Look: palette, emissive edges, per-cell face colours, dithered translucency | 1.0 h | Readable in stereo, no z-fighting |
| 6 | **MVP gate:** 10-minute soak on the headset phone | 0.5 h | 60 fps, < 16.6 ms frame time, no thermal drop |
| 7 | `WLane` + corner-first preset + sweep | 1.0 h | Walking the lane shows 4 → 12 → 6 → 12 → 4 |
| 8 | `CrouchExit` + boundary / tracking passthrough | 0.5 h | All three triggers reach passthrough < 300 ms |
| 9 | `BreathRing` with synthetic tumour, flags, ITV capsule, duty cycle | 2.0 h | Phase follows the walk smoothly; ITV equals the analytic value |
| 10 | `LookDownShadow` (D5) | 1.0 h | Smooth cross-fade, no pop |
| 11 | Knife Map card + technique codes (4s, 4t) | 0.5 h | Live w₀, corner count, ITV and duty cycle on the dashboard |

**Total:** MVP ≈ 5.5 h, plus target ≈ 5 h.

### Controls (each has a twin input)

| Action | Primary (gamepad) | Twin |
|---|---|---|
| Move slice w₀ | Triggers | Walk the w-lane |
| Rotate XW / YW | Left stick x / y | Gaze-dwell chips (800 ms) |
| Rotate ZW | Right stick x | Gaze-dwell |
| Shadow ↔ slice | A | Look down (D5) |
| Next object / corner-first preset | Y | Gaze-dwell |
| Breath flags | X (drop at current phase) | Gaze-dwell on the ring |
| Exit / Safe Harbour | B, or say "home" | Crouch 1.5 s (automatic triggers always on) |

---

## 9. Tests

Run before every device build. They already pass in `node demo/4d-ar/four.test.mjs`.
- Tesseract: 16 / 32 / 24 / 48; boundary Euler characteristic 16 − 32 + 24 − 8 = 0; every tet has volume 8/6 (6 tets fill each 2³ cell).
- Rotation: R·Rᵀ = I to 1e-9; four 90° XW turns give the identity.
- Axis-aligned slice at w₀ = 0: 8 corners, face area 24, 12 wire edges.
- Corner-first: 4 / 12 / 6 corners at w₀ = −1.5 / −0.75 / 0; nothing beyond |w₀| > 2.
- Projection: w = 0 is unchanged; w < 0 shrinks.
- To add in Unity: hypersphere radius √(1 − w²); ring phase wraps at 2π with no seam; ITV capsule volume against the analytic formula.

### Spikes to run today

| Spike | Time | Pass if |
|---|---|---|
| Ring vs line Breath Room, 3 people | 1 h | People can say which phase they're in |
| Dwell sweep 500 / 800 / 1,000 ms, 3 people | 30 min | Fastest with no accidental selects |
| w-lane comprehension, 3 people | 30 min | They can predict "walk east → slice moves" after one pass |
| Licence audit of every shipped file | 30 min | All CC0, CC BY, BSD or CMU |

---

## 10. Risks and fallbacks

| Risk | Mitigation |
|---|---|
| Judges think it's "just a rotating wireframe" | Walking the lane with live corner counts and the Flatland line make it a cross-section, not an animation |
| Quad triangulation crosses itself | Use the (a₀b₀, a₀b₁, a₁b₁, a₁b₀) cycle (done in `four.js`) |
| Faces and edges z-fight | Edge depth offset (`Offset -1,-1`); dithered face translucency |
| Inconsistent face winding from the slice | `Cull Off`; flat normals per triangle |
| Lane walking and the stick fight over w₀ | The last-touched input owns w₀ for 2 s; the lane wins while the user is moving |
| Breath ring too big for the safe area | Radius 0.75–0.9 m, checked against the scanned free floor at setup |
| Disorientation | The 4D object moves, never the world; the ring is ordinary walking |
| Scope creep (eight proposed blades, marker cube) | Only the Visual Field Dome is allowed, and only after the MVP gate; `ISlicer4D` keeps the cube hook |

---

## 11. Claims

**Say**
- "You see 4D as 3D slices and shadows. Walking and head tracking let you explore them with your body."
- "Breathing repeats, so time is a circle: flat, but it wraps."
- "Embodied exploration of medical data. Educational, not for clinical use."

**Avoid**
- "Users learn 4D." Studies show no significant learning effect.
- "4D CT is non-Euclidean." It is flat 4D; only the wrap is non-Euclidean.
- Calling the 4D toybox curved. It is flat 4D, under the brief's "4D projections" item.
- Any clinical or diagnostic claim.

---

## Appendix A: Review of the "Heartbeats, Eyes and Gait" proposal

**Verdict:** well researched, but the wrong scope for this weekend. Treat it as a menu and pick one item.

| Proposal item | Really 4D? | Non-Euclidean? | Cost | Verdict |
|---|---|---|---|---|
| Tumour breathing on the ring (existing story) | 3D + circle of time | Wrapped (S¹) | Medium; synthetic is cheap | ✅ Keep as the spine |
| Visual Field Dome (UWHVF, BSD-3) | Sphere + time | Sphere (S²) | Very low | ✅ Optional; replaces FieldFit |
| Tumour Growth Corridor (LUMIERE, CC BY 4.0) | 3D + linear time | No | Low–medium | ⚠️ Backup only |
| Gait Torus (CMU mocap) | Loop on a torus | Wrapped (T²) | Medium | ⚠️ Roadmap |
| Retina Tree in H² (FIVES / HRF, CC BY 4.0) | No | Hyperbolic (H²) | Low | 💡 Cheaper airway fallback, not 4D |
| Heartbeat Torus (Sunnybrook + PTB-XL) | Torus of two cycles | Wrapped (T²) | High | ❌ Cut |
| Circle of Willis, Heart Shape Slider, Colon Cylinder | — | — | High | ❌ Roadmap |

**Not adopted this weekend:**
- **Shell taps instead of the gamepad:** footsteps can spike the accelerometer like a tap. Stretch at most.
- **Hypernom mode (head orientation as a position on S³):** rated low-comfort by the proposal itself, and it needs upside-down head poses.
- **TrueDepth jaw or blink input:** untested at this distance.
- **Volume rendering:** no stereo performance numbers on iPhone; use meshes per phase.

## Appendix B: Roadmap (after the hackathon)
- Heartbeat Torus.
- Gait Torus.
- Tumour Growth Corridor.
- Retina and Circle of Willis trees in H².
- Colon Cylinder.
- Tesseract House (S³).
- Compute-shader slicer for many 4D objects.
- Controller-free input: shell taps, head-roll dial, Hypernom mode.

## Appendix C: References
- **Code:** HackerPoet/Engine4D (MIT; vertex-shader slicer, no geometry shaders); wxyhly/tesserxel (ISC; WebGPU 4D); romanesco/Slice4D (MIT). Our own prototype is `demo/4d-ar`.
- **Background:** Abbott, *Flatland* (1884); Banchoff, *Beyond the Third Dimension* (1990) and the 1978 hypercube film; Carl Sagan's Flatland clip; Bailey Snyder's Interactive 4D Handbook; Alan Zucconi, "Rendering 4D objects" (2023).
- **Prior art to differ from:** Marc ten Bosch's 4D Toys / Miegakure; CodeParade's 4D Golf. **Our angle:** walking a physical w-lane in a headset, plus the same slicer giving a breathing tumour's ITV a real clinical meaning.
- **Data:** TCIA 4D-Lung (CC BY 3.0), UWHVF (BSD-3), LUMIERE (CC BY 4.0); Lujan breathing model; AAPM TG-76.
