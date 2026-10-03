# ATLAS: Build Pipeline (how to make it look good and run at 60 fps on iPhone)

Research compiled 3 Oct 2026 from four parallel research passes. Source lists are at the bottom. **⚠ = unverified, test it on the device.**

---

## 0. TL;DR: the stack

| Job | Use | Not this |
|---|---|---|
| Engine | **Unity 6 LTS, URP, AR Foundation 6, ARKit as the only XR loader** | Cardboard XR plugin (conflicts with ARKit, see §1) |
| Stereo | **Custom two-camera rig** (±0.032 m, half-screen viewports) | Cardboard / single-pass instanced |
| Greybox | **ProBuilder** (rooms, corridors, portals, on a 0.5 m grid) | Blender for blocking out |
| Final rooms / tiles to be warped | **Blender** (you control subdivision), CC0 kits | AI generators for whole rooms (not good at modular kits yet) |
| Props | **CC0 kits first** (Kenney, Quaternius, KayKit), **then AI** (Meshy, Hunyuan3D 3.1 hosted) | Splats (not viable on iPhone in stereo) |
| Look | **"Lit architectural diagram"**: one palette texture, vertex-colour AO, edge/tile lines in the shader, colour fog, emissive accents | Post-processing, real-time shadows, screen-space outlines |
| Hyperbolic | **Port HyperEngine's H2xE shader to URP HLSL** (MIT), using the **log map** | Klein projection for H²×E (distorts elevation) |
| 4D | **CPU slicing** of a single tesseract (≈48 tets); Engine4D's vertex shader if you need more | Geometry shaders (Metal doesn't have them) |

---

## 0.5 Updates from "ATLAS Tech Landscape" (3 Oct)
- **Stereo rig:** spike the **HoloKit Unity SDK** (MIT; Unity 6, AR Foundation 6, iOS 18+, iPhone 15 Pro Max supported) in the first hour. It gives a working ARKit stereo rig plus 3D hand tracking. It is built for optical see-through, so a lens headset still needs barrel pre-distortion: use Cardboard's `DeviceParams` coefficients for the math only. If the spike fails, fall back to our own two-camera rig.
- **Hyperbolic precision:**
  - Address space **by tile id** (Holonomy / HyperRogue style), with a double-precision hyperboloid transform on the CPU, renormalised every frame and re-anchored at the camera before casting to float.
  - Metal has no float64.
  - Fog at about 5–8 units of hyperbolic distance, which is also the precision horizon.
- **Projection:**
  - For walked H²×E, keep the **log map** (correct view directions, needs 0.1–0.25 m subdivision).
  - Klein (straight geodesics, portal clipping works) is for pure H²/H³ views.
  - Team decision; the plan says Klein.
- **Portals:** stencil confirmed (render-texture portals cost about 37% fps). One level of recursion at most. Add a "portal box" for when one eye crosses before the other.
- **Comfort:**
  - No vignette during real walking (shown not to help).
  - Gains at identity.
  - Add landmarks and a mini-map in the H² wing, because people home in Euclidean terms.
  - Log the Fast Motion Sickness Scale every minute, with a stop threshold.
- **Thermal:** LiDAR depth and hand tracking add heat. Turn meshing off after the scan and use hands sparingly.
- **Airway data:**
  - TotalSegmentator only reaches airway generations 1–2.
  - Training nnU-Net on ATM'22 is impossible in 36 h.
  - For the weekend, use a **procedural (Weibel-style) airway tree labelled "illustrative"**, or an ATM'22 ground-truth label after checking the data agreement.
- **Local AI 3D on the 5070:** **TripoSG** (MIT, 8 GB VRAM). Keep Hunyuan3D off the default path: the licence excludes the EU, UK and South Korea and bans medical decision use.
- **Networking:**
  - LiteNetLib (MIT) for UDP pose and time sync.
  - Trigger the iOS Local Network prompt during onboarding, not inside the headset.
  - Use unicast to a known server IP; broadcast needs a restricted entitlement.
- **Versions:** Unity 6.3 LTS, AR Foundation 6.5.x. **ARKit XR Plugin 6.4+ needs Xcode 26** on the Mac. Remember the 6.3 thermal 30 fps cap (§1).
- **4D cube:** ARKit image tracking gives one anchor per face, so fuse the faces into one pose, smooth it (One Euro filter) and canonicalise the quaternion sign.

## 1. Platform decisions that change the plan

1. **ARKit and the Cardboard plugin can't run together.** XR Plug-in Management activates only the first loader that succeeds (googlevr/cardboard #291), so you'd get either 3DoF Cardboard or ARKit. → Use **ARKit only, plus your own two-camera stereo rig.** Lens pre-distortion becomes optional: skip it, or add a blit later.
2. **Passthrough in stereo is awkward.** AR Foundation allows only one `ARCameraBackground`. A second eye needs a full-screen quad using `ARCameraBackground.material`, and the split viewport stretches it, so you have to fix the UVs.
   - **Recommendation:** render the blades in **VR mode with no camera background** (it's cheaper), inside the taped, roped-off square with a spotter (the plan's Mode B).
   - Keep passthrough for onboarding / Hub only if time allows.
   - *This contradicts the plan's "Mode A recommended". Team decision.*
3. **Thermal 30 fps cap.**
   - Unity 6.3's generated `UnityAppController+Thermal.mm` forces **30 fps at "Serious" thermal state** and overrides `targetFrameRate`.
   - On 6.5+ it's a Player Setting.
   - The plan already allows a 30 fps thermal tier, so accept it, but delay it with the measures in §5.
4. **Adaptive Performance doesn't report thermal state on iOS.** Write a roughly 10-line native `.mm` plugin that returns `NSProcessInfo.thermalState` and poll it every 2–5 s.
5. **Your vertex warp breaks culling and batching.**
   - Set `renderer.localBounds` to something huge (e.g. 1000 m), or copies will pop.
   - Turn off baked occlusion culling.
   - Static batching is only safe if the warp is computed from **world** position.

---

## 2. Art direction: the "lit architectural diagram"

Why: Manifold Garden, Antichamber and Monument Valley all read clearly because **colour and line define form, not textures**. This is also the cheapest thing to render in stereo on a phone.

- **One 128² palette texture** (flat colour blocks plus a few gradient strips) and **one shared material** everywhere. That lets the SRP Batcher and batching work and keeps draw calls tiny. Import it with **compression off and mipmaps off** so the gradients don't band.
- **Bake AO into vertex colours in Blender.** Cycles can bake to the colour attribute, or use the free VertexOven add-on. This beats lightmaps here because lightmaps don't follow warped or copied geometry.
- **Draw edges and tile lines in the surface shader**, anti-aliased with `fwidth` (Ben Golus' grid shader). It's nearly free, stable in both eyes, and shows the tiling directly. Use inverted-hull outlines **only** on hero objects (portal frames, the 4D slicer).
- **Fog in each room's own hue.** Lightness steps down with copy or recursion depth, so the 3rd and 4th copies fade into fog. This makes repetition readable and hides the edge of the grid.
- **Emissive accents** (portals, 4D cross-sections, beacons) with **no bloom**.
- **Lung:** a warm two-tone palette for chambers and a cool one for corridors, with gradient UVs along the airway.
- Glass/holograms: use sparingly (overdraw and sorting). Fake them with an unlit fresnel/rim term or dithered transparency.
- Holonomy's trick: put **landmarks at tile corners** and run objects (logs, creeks) across several tiles, so the distortion is *visible*.

---

## 3. Asset pipeline

### 3a. Rooms and tiles
1. **ProBuilder greybox** on a 0.5 m grid. Lock the dimensions in the headset.
2. Plain boxy rooms can **stay as ProBuilder meshes** with the palette material. Use "Merge Objects" to combine each room into one mesh.
3. Anything **warped by the hyperbolic shader** gets rebuilt in **Blender** with a **regular grid of 0.1–0.25 m edges**. In the log map straight edges bend, and too few vertices makes them look wrong.
4. Blender export (FBX):
   - Metric, scale 1.0, transforms applied
   - Apply Scalings = FBX Units Scale, Forward −Z, Up Y
   - Pivot at a floor corner
5. **Blender MCP is already connected in this Claude session.** It can generate parametric tiles ({4,5}: squares, five per corner; n-gon tiles, corridor segments) with exact subdivision, auto-assign palette UVs, and batch-export. Use it.

### 3b. Props (furniture, mugs, plants, signage)
In priority order:
1. **CC0 kits:**
   - **Kenney** and **Quaternius** (CC0, no credit needed)
   - **KayKit** (CC0, and it uses a single gradient atlas, so it matches our palette workflow)
   - Poly Pizza: **check each model's licence** (mix of CC0, CC-BY and NC)
2. **AI generation** when nothing fits:
   - **Meshy 6:** best overall. Retopology to a target polycount (100–300k, quad or tri). Exports GLB/FBX/USDZ. Official Unity plugin (Pro). Free tier is about 3 generations/month and is CC BY 4.0 (credit it). **One $20 Pro month is the best purchase.**
   - **Hunyuan3D 3.1 hosted** (3d.hunyuanglobal.com): **20 free generations/day**, top-tier geometry. Reduce polycount yourself in Blender. ⚠ The licence excludes the EU, UK and South Korea, and outputs must be labelled AI-generated.
   - **Tripo 3.x:** fast backup with Smart Low Poly. ⚠ The free tier is reportedly public and non-commercial, so pay $19.90 if you use it.
   - **Sloyd:** parametric, clean low-poly furniture, Unity plugin.
   - **TRELLIS.2 (MIT)** for bulk generation **only on the H200**. Officially it needs 24 GB and Linux; on the RTX 5070 it's a community workaround (⚠).
   - Skip: Luma Genie (shut down Jan 2026), Kaedim (enterprise only), and local Hunyuan texturing on the 5070 (needs a Blackwell rasteriser build).
3. **AI-to-Unity cleanup:**
   - Generate from a **clean reference image** (front-on, flat even lighting, plain background), not from text.
   - Retopologise to budget, then Smart UV Project.
   - Bake to the shared atlas or palette.
   - Fix the **scale** (AI output has arbitrary scale) and the **baked-in shadows**.
   - Use **TextMeshPro for signage**; AI text is garbled.
   - Use alpha-cutout cards for leaves.
4. **Real objects:** Apple Object Capture via Reality Composer, or Scaniverse (free, exports mesh), for 1–2 hero props. **Not Gaussian splats**: aras-p's Unity splats are "not tested on mobile" and break on iOS Metal.

### 3c. Textures, sky and sound
- Tileable PBR (if needed): **Polycam AI Texture Generator** (free).
- Sky: a **gradient skybox** fits the look and is free. Blockade Skybox AI needs a paid plan to export; Unity AI's Cubemap Generator has a 14-day trial.
- Sound (cheap polish):
  - One ambient loop per space (lung: low-pass airflow; hyperbolic: airy drone)
  - A portal chime pitched by depth, a tick at tile edges, a rising tone for the 4D slice
  - Sources: Kenney audio (CC0), Sonniss GDC bundles

### 3d. Licensing rules
- **Unity Asset Store free packs can't be redistributed**, so keep them **out of a public GitHub repo**.
- Credit CC-BY assets and Meshy free-tier output on Devpost.

---

## 4. Rendering implementation

### URP asset (iPhone)
- **Forward** path, Render Graph on (the default)
- **HDR off**, Depth/Opaque Texture off, Depth Priming off
- **MSAA 2× (or 4×)**, **render scale 0.75–0.8**
- **No post-processing** (URP's on-tile post-processing is Vulkan-only, so not available on Metal)
- **No FXAA, no STP** (STP smears under head motion)
- Shadows off, additional lights off or per-vertex, **SRP Batcher on**
- Skip GPU Resident Drawer (porting the warp shader to it is risky)

### Custom shaders
Make them **SRP Batcher compatible**:
- Per-room data (e.g. the room's Lorentz anchor) in `CBUFFER_START(UnityPerMaterial)`, one material instance per room
- Camera and holonomy data as globals via `Shader.SetGlobalMatrix`
- **No MaterialPropertyBlocks**

### Glued Room (torus / Klein)
- Copy (i, j) = translate(i·W, 0, j·D), plus a mirror on odd rows if Klein mode.
- Use a 5×5 to 7×7 grid of copies, with fog hiding the edge.
- **Use plain GameObject copies with negative scale**: Unity flips the winding automatically and it stays batcher-friendly. If you use `RenderMeshInstanced`, make two calls split by parity with `Cull Front` / `Cull Back`, and use no normal maps on mirrored copies.
- **Self-avatar:** a head/torso mesh at the tracked head pose in every copy *except* (0,0).
- **Treat the physical room as the fundamental domain.** Don't let the user cross a glued edge, especially the Klein flip (it would need global inverted culling).

### Impossible rooms and stencil portals
- Use **Render Objects renderer features**, which already run on Render Graph:
  1. The doorway mask quad writes `Stencil Ref N`, with `ColorMask 0` and `ZWrite Off`.
  2. Room N's layer is removed from the default opaque mask.
  3. A Render Objects feature draws layer RoomN with `Stencil Equal N`.
- Only use stencil values **0–15**.
- Clip room-B geometry that sits in front of the doorway in the shader:
  ```hlsl
  clip(dot(_PortalPlane.xyz, posWS) + _PortalPlane.w);
  ```
- **Skip recursion.**
- **Room swap:** only when the room's bounds fail `TestPlanesAABB` for **both** eye frustums *and* the user is outside the overlap, plus a few frames of hysteresis.
- **Closest repo:** Tongzhou-Yu/ar-portal-arfoundation-urp (MIT, AR Foundation + Render Objects stencil portals).

### Hyperbolic floor (H²×E)
- **Reference:** HackerPoet/HyperEngine (MIT, Hyperbolica's engine), especially `H2xEShader.shader`, `HyperCore.cginc` and `DisableCameraCull.cs`. Port it from Built-in RP cginc to URP HLSL. Also see mmagdics/noneuclideanunity (MIT).
- **Shader (log map, correct view directions for H²×E):**
  ```hlsl
  float4x4 _HypCamInv; float _HypL; float3 _HeadPosWS;            // globals
  CBUFFER_START(UnityPerMaterial) float4x4 _RoomAnchor; CBUFFER_END
  float3 ExpH2(float2 t){ float r=length(t); float2 u=r>1e-6?t/r:0; return float3(sinh(r)*u, cosh(r)); }
  float2 LogH2(float3 p){ float s=length(p.xy); return s>1e-6? p.xy/s*asinh(s) : 0; }
  float3 WarpH2xE(float3 posOS){                                   // room-local metres, y = height
    float3 p = mul((float3x3)_HypCamInv, mul((float3x3)_RoomAnchor, ExpH2(posOS.xz/_HypL)));
    float2 t = LogH2(p)*_HypL;
    return float3(_HeadPosWS.x+t.x, posOS.y, _HeadPosWS.z+t.y);    // world space; normal VP does per-eye
  }
  ```
- **CPU side** (`double3x3`, metric diag(1, 1, −1)):
  - Each frame, take Δ = head xz displacement / L and set `C = C · Boost(Δ)`. Holonomy accumulates automatically.
  - Re-orthonormalise C with η-Gram–Schmidt.
  - Upload `C⁻¹ = η Cᵀ η` as float.
  - **Re-anchor** when far from the origin.
  - CPU-cull rooms at hyperbolic distance d > ~4, where `cosh d = −⟨p,q⟩`.
- **Pitfalls:**
  - Set `TrackedPoseDriver` to **Update only**, not "Update and Before Render", and run the holonomy script late (`[DefaultExecutionOrder(100)]`). Otherwise the deltas jitter.
  - **Reject pose jumps over 0.2 m per frame**, e.g. from ARKit relocalisation.
  - Compute the boost **once per head**, not per eye. The eye offset is applied in Euclidean space, and the error is negligible.
  - Huge `localBounds` (see §1).
- **Holonomy** (TU Delft 2024) is the closest prior art: Unity, 3×3 m, {4,5} tiling (five squares per corner), 7 stencil views. ⚠ No public code was found, and it stores state in a discrete tree to avoid float drift.

### 4D slicer
- One tesseract: **slice on the CPU every frame** (≈48 tets, each giving a triangle or a quad) with `Mesh.SetVertices`.
- Many 4D objects: port **HackerPoet/Engine4D**'s vertex-shader slicer (MIT, uses an `SV_VertexID` lookup table, no geometry shader).

---

## 5. Performance budgets and thermal plan

**Budgets** (extrapolated from Meta's Quest numbers; ⚠ not Apple figures):
- **< 300 draw calls total** (both eyes), **< 300–500k triangles per frame**
- Per prop: small 300–1.5k tris, furniture 1.5–5k, hero 5–15k, room shell < 2k
- Glued Room with 49 copies: one combined mesh per copy and < 5k tris each ≈ 49 draws per eye. Trim the grid to 5×5 if needed.
- Textures: **ASTC** (6×6 albedo, 4×4 UI/normals), mipmaps on, max 1024, shared atlas

**Frame pacing**
- `Application.targetFrameRate = 60` (iOS defaults to 30), ProMotion off.
- Pick the **lowest-resolution 60 Hz ARKit camera configuration** (`ARCameraManager.GetConfigurations()`), never 4K.
- After setup, **turn off** plane detection, ARMeshManager (LiDAR meshing), environment probes, light estimation and occlusion.

**Thermal ladder** (from the native thermal-state plugin):
- Fair → render scale 0.7
- Serious → 0.6, MSAA 2×, fewer copies, shorter fog (Unity will cap at 30 fps here anyway)

**Demo day**
- Low screen brightness
- Phone out of the headset between judges, case off
- A fan, and swap to phone B

**Profiling**
- Unity Profiler (Development Build), Frame Debugger, Render Graph Viewer attached to the device
- Xcode Metal frame capture
- **Metal Performance HUD** (Settings → Developer → Graphics HUD)
- A **15-minute soak test** in the headset before calling anything done

---

## 6. Order of work (first 6 hours)
1. AR Mobile template project → ARKit-only loader → URP settings above → `targetFrameRate` → build to iPhone.
2. Two-camera stereo rig (VR mode, no background) → Metal HUD shows 60 fps on an empty scene.
3. Palette texture + one shared unlit shader with `fwidth` grid lines + fog → ProBuilder 1.5 m room.
4. Glued Room copies (torus) → self-avatar → Klein toggle.
5. *In parallel (second dev):* port the HyperEngine H2xE shader → one subdivided Blender floor tile → boost/holonomy script.
6. *In parallel (artist):* Blender MCP tiles, CC0 props, palette UVs, vertex AO.

---

## Sources

**AI 3D tools**
- meshy.ai (credits guide, AI retopology, Unity plugin docs)
- tripo3d.ai/blog/tripo-dcc-bridge-for-unity
- hyper3d.ai/pricing
- x.com/TencentHunyuan/status/2016449283428659599
- github.com/microsoft/TRELLIS.2
- github.com/visualbruno/ComfyUI-Trellis2
- github.com/Tencent-Hunyuan/Hunyuan3D-2 (LICENSE, issue #329)
- github.com/Stability-AI/stable-point-aware-3d
- unity.com/blog/unity-ai-3d-object-generator
- poly.cam/tools/ai-texture-generator
- github.com/aras-p/UnityGaussianSplatting (#226, #72)
- developers.meta.com/vr/documentation/unity/unity-perf/

**Art and pipeline**
- Manifold Garden SIGGRAPH 2020 talk (history.siggraph.org)
- bgolus.medium.com/the-best-darn-grid-shader-yet-727f9278b9d8
- arxiv.org/html/2409.05460 (Holonomy)
- void-room.itch.io/tea-for-god devlog
- kenney.nl/support · quaternius.com · github.com/KayKit-Game-Assets · polyhaven.com/license
- github.com/mrven/Blender-Asset-Creation-Toolset
- github.com/ForestKatsch/VertexOven
- docs.unity3d.com (importing LOD meshes, ProBuilder export, FBX exporter)
- blendermcp.org · sonniss.com/gameaudiogdc

**Unity performance**
- github.com/googlevr/cardboard-xr-plugin/releases · googlevr/cardboard #291, #350, #283
- docs.unity3d.com: XR Management end-user docs, AR Foundation camera components, `Application.targetFrameRate`
- docs.unity3d.com: xr-untethered-device-optimization, xr-graphics-on-tile-post-processing, URP configure-for-better-performance
- docs.unity3d.com: SRPBatcher-Materials, GPUInstancing, Renderer.localBounds, adaptive-performance supported-features
- discussions.unity.com: "iOS locked at 30 fps after upgrading to Unity 6.3" (thermal cap)
- developer.apple.com/forums/thread/696078
- developer.android.com/games/optimize/lighting-for-mobile-games-with-unity

**Non-Euclidean rendering**
- Hyperbolic and portal engines: github.com/HackerPoet/HyperEngine · github.com/HackerPoet/Engine4D · github.com/HackerPoet/NonEuclidean
- Portal repos: github.com/SebLague/Portals · github.com/daniel-ilett/portals-urp · github.com/WearableComputerLab/VRPortalToolkit · github.com/Tongzhou-Yu/ar-portal-arfoundation-urp
- Other non-Euclidean code: github.com/mmagdics/noneuclideanunity · github.com/henryseg/non-euclidean_VR · github.com/Ralith/hypermine
- 4D: alanzucconi.com/2023/07/06/rendering-4d-objects/
- Stencil impossible geometry: danielilett.com/2022-01-05-tut5-22-impossible-geom-stencils/
- Unity: RenderObjectsPass.cs (Unity-Technologies/Graphics 6000.0), Graphics.RenderMeshInstanced docs
- Slotboom 2023 thesis: resolver.tudelft.nl/uuid:efc67ab5-16bb-4f01-9478-7cee03968257
