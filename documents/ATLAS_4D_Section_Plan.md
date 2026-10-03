# ATLAS: 4D Section Plan and Review of the "Heartbeats, Eyes and Gait" Proposal

*3 Oct 2026. This reviews the proposal to add eight medical blades (Heartbeat Torus, Visual Field Dome, Tumour Growth Corridor, Gait Torus and others) plus a controller-free input scheme. It then sets out what we build for the 4D part this weekend.*

---

## 1. Verdict

The proposal is well researched, but it is the wrong scope for this weekend. **Treat it as a menu and pick one item, not eight.**

- The hackathon has already started (3–4 Oct).
- Scope creep is a **high** risk in our own plan (ATLAS Plan p. 21).
- Tier A (5 blades) was already more than 36 hours allows.

Several pieces are worth adopting right away (§2). The rest goes on the roadmap (§6).

---

## 2. Adopt now

### 2.1 Licence filter
- **Ship:** only CC0, CC BY, BSD and CMU data.
- **Keep behind a "research demo" flag:** NC, ND, ShareAlike and no-redistribution data.
- It costs nothing and keeps ATLAS sellable later.

### 2.2 The Breath Room becomes a ring
- **Why:**
  - A breath is a loop, so a walked circle has no seam at end-of-exhale. A straight line does.
  - It matches the honest claim: *"time is a circle: flat, but it wraps."*
- **Radius correction:** the proposal says 1.2 m. That is too large.
  - The tape square is 3×3 m. After keeping 0.3–0.5 m clear inside the tape, about 2.0–2.4 m of floor is usable.
  - A 1.2 m ring (2.4 m wide) sits on the edge of that usable area.
  - **Use 0.75–0.9 m.** Our spec says 0.75 m.
  - That gives 2π·0.8 ≈ **5 m of walking per breath**.
- **Mapping:**
  ```
  φ   = atan2(z − c_z, x − c_x)            // angle around the ring centre
  bin = floor(10 · (φ mod 2π) / 2π)        // 10 breathing phases
  ```

### 2.3 Safety exit that needs no buttons
Any one of these fades the view back to camera passthrough:
1. **Automatic:** within 0.5 m of the 3×3 m boundary (ARKit position).
2. **Automatic:** ARKit tracking state drops below normal.
3. **Deliberate:** crouch below about 70% of calibrated standing eye height and hold for 1.5 s.

Tell every judge one sentence before they put the headset on: **"If anything feels off, crouch."**

### 2.4 Select with head-gaze and dwell (fallback)
- A head-gaze reticle with an **800 ms dwell** and a visible fill ring.
- This is the fallback when the gamepad isn't available, not the primary input.

---

## 3. Do not adopt this weekend

| Proposal item | Why not now |
|---|---|
| **Tap the headset shell instead of the gamepad** | No study has measured false triggers while walking, and footsteps can spike the accelerometer like a tap. Mic plus accelerometer fusion is a research project. **Keep the Bluetooth gamepad as primary.** Taps are a stretch goal. |
| **Hypernom mode (head orientation as a position on S³)** | The proposal itself rates comfort as low. Covering every position needs every head orientation, including upside down. |
| **TrueDepth (jaw or blink) inside the viewer** | Untested at this distance, and it adds compute load. |
| **Volume rendering (3D textures, ray marching)** | No iPhone stereo numbers exist. **Use meshes per phase.** |
| **Heartbeat Torus** | Needs MRI contours turned into meshes, and the torus is stitched from two datasets. Too heavy. |
| Circle of Willis, Heart Shape Slider, Colon Cylinder | Large downloads and segmentation work. Roadmap. |

---

## 4. The proposed blades, judged for the 4D part

Most proposed blades are **3D plus time**: flat 4D. They are strong medical stories, but they score less on the brief's "non-Euclidean is central" criterion than true 4D space does. The brief names *"4D projections… rotations in planes we cannot perceive"* as a category in its own right.

| Blade | Really 4D? | Non-Euclidean? | Cost this weekend | Verdict |
|---|---|---|---|---|
| **Tumour breathing on the ring** (existing story) | 3D + circle of time | Wrapped (S¹) | Medium; a synthetic tumour is cheap | ✅ **Keep as the spine** |
| **Visual Field Dome** (UWHVF, BSD-3) | Sphere + time | Sphere (S²) | **Very low**: one JSON, ~54 points on a dome | ✅ **Best new addition.** Replaces FieldFit, not the 4D part |
| **Tumour Growth Corridor** (LUMIERE, CC BY 4.0) | 3D + linear time | No | Low–medium; segmentations included, just marching cubes | ⚠️ Backup if 4D-Lung processing stalls |
| **Gait Torus** (CMU mocap) | Loop on a torus | Wrapped (T²) | Medium | ⚠️ Elegant, but adds a new story |
| **Heartbeat Torus** (Sunnybrook + PTB-XL) | Torus of two cycles | Wrapped (T²) | High | ❌ Cut |
| **Retina Tree in H²** (FIVES / HRF, CC BY 4.0) | No | Hyperbolic (H²) | Low; 2D masks to a tree | 💡 Not 4D, but a **cheaper airway fallback**: a real data tree in hours |
| Circle of Willis, Heart Shape Slider, Colon Cylinder | — | — | High | ❌ Roadmap |

---

## 5. What we build: the 4D section

One story and one slicing engine, in three steps.

### Step 1: Explain 4D by walking through it
- One floor direction is the 4th axis, **w**. Walking east moves the 3D slice through a 4D object.
- **Hypersphere:** appears as a dot, grows into a ball, shrinks and vanishes. Slice radius = √(1 − w²).
- **Tesseract, cut corner-first:** tetrahedron → truncated tetrahedron → octahedron → back.
- This directly covers the brief's "4D projections" category.
- References: Carl Sagan's Flatland clip, 4D Toys, Miegakure, and Bailey Snyder's Interactive 4D Handbook.

### Step 2: Apply the same slicer to the breathing tumour on the ring
- Your angle around the 0.75–0.9 m ring is the breathing phase, so the chest breathes as you walk.
- Two flags on the floor bracket the quiet part of the breath. The ITV (the tumour's "shadow" over the gated arc) and the duty cycle update live.
- **Data, synthetic first:**
  - A 2 cm sphere moving along the Lujan cos⁴ breathing curve, about 12 mm head-to-feet.
  - 10 phases, generated analytically on the PC and turned into meshes with marching cubes.
  - Labelled "illustrative".
- **Data upgrade:** swap in one real TCIA 4D-Lung patient (CC BY 3.0) if that pipeline is ready in time.

### Step 3: Look down to see the projection
- The Look-Down Shadow shows the 4D projection of what is off-slice.
- 4D Golf uses the same "ghost image" convention, so it is an accepted idiom.

### Optional: Visual Field Dome in place of FieldFit
- Where you look **is** the data coordinate: the strongest "body matches data" case.
- UWHVF is one BSD-3 JSON. Show each test point as a dot on a dome around the head, and walk forward to scrub years of tests.

### Implementation notes
- **Slicing:**
  - A tesseract is 8 cubic cells, split into about 40–48 tetrahedra in 4D.
  - Each frame, intersect every tetrahedron's 6 edges with the slicing hyperplane, giving a triangle or a quad per tetrahedron.
  - Run it on the CPU and upload with `Mesh.SetVertices`. No geometry shaders are needed (Metal has none).
- **4D rotation:**
  - One 4×4 matrix per plane. For example, the xw plane: `x' = x cos θ − w sin θ`, `w' = x sin θ + w cos θ`.
  - Gamepad stick for the xw, yw and zw planes; triggers move the slice along w.
- **Projection:**
  - Perspective `(x, y, z) / (d − w)`, drawn as emissive lines and coloured by w.
- **Look:**
  - Colour each of the 8 cells differently so a slice shows which cubes it cuts.
  - Emissive edges and palette fill, per the art direction in `ATLAS_Build_Pipeline_assets.md`.
- **Reference code:** HackerPoet/Engine4D (MIT).
- **Marker cube (Slice Cube):** stretch goal only. Fuse the face anchors into one pose, smooth it, and keep the quaternion sign consistent. The gamepad stays as the fallback.

---

## 6. Roadmap (after the hackathon)
- Heartbeat Torus (cardiac × breathing).
- Gait Torus (hip × knee over one stride).
- Tumour Growth Corridor (LUMIERE).
- Retina and Circle of Willis trees in H².
- Colon Cylinder.
- Controller-free input: shell taps, head-roll dial, Hypernom mode.

---

## 7. Spikes to run today

| Spike | Time | Pass if |
|---|---|---|
| Ring vs line Breath Room, 3 people | 1 h | People can say which phase they're in |
| Dwell sweep: 500 / 800 / 1,000 ms, 3 people | 30 min | Fastest setting with no accidental selects |
| Licence audit of every shipped file | 30 min | All CC0, CC BY, BSD or CMU |

Skipped: the volume-rendering budget (we use meshes) and TrueDepth (nothing depends on it).

---

## 8. Claims

**Say**
- "You see 4D as 3D slices and shadows. Walking and head tracking let you explore them with your body."
- "Breathing repeats, so time is a circle: flat, but it wraps."
- "Embodied exploration of medical data. Educational, not for clinical use."

**Avoid**
- "Users learn 4D." Studies show no significant learning effect.
- "4D CT is non-Euclidean." It is flat 4D. The wrap is what is non-Euclidean.
- Any clinical or diagnostic claim.
