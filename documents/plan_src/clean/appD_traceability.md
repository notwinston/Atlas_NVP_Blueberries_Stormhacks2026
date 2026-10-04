## Appendix D. What changed against the older documents

Order of authority: the team lead's decisions (§2 of this plan) > Build Pipeline (BP) > Spec > Brief. The research memos R1–R6 correct BP where they verified or derived a different value. In the "older position" column, "old D-n" is a row of the decision table shared by Spec §1.2 and Brief §1.4, and "NN-n" is a Spec §0 non-negotiable. In the "decided by" column, "D-n" is a row of §2 of this plan. "Blade" is the older documents' word for a use case.

### D.1 Superseded

| Topic | Older position (Spec/Brief ref) | Now | Decided by |
|---|---|---|---|
| Stereo | One camera, side-by-side pass; Cardboard XR plugin as fallback (old D5, Spec §5.2) | Own two-camera rig, ±0.032 m, one render texture per eye (1118 × 1032); ARKit the only XR loader | BP §0, §1.1; R5 for the eye textures (D.2) |
| Optics | Cardboard C API, distortion meshes, chromatic-aberration fix (Spec §5.2) | Barrel pre-distortion as the one final blit, which samples both eye textures and upscales them; hard-coded coefficients | BP §0.5 |
| Latency | Core Motion prediction + late rotational warp, ≤ 30 ms (old D16) | `TrackedPoseDriver` "Update only"; optional gyro prediction; no late warp | BP §4, R5 |
| Thermal tier | 30 fps render warped to 60 Hz (NN2, Spec §5.4) | No warp tier; quality ladder; plan never to reach Serious | R5 (overrides BP §1.3; see D.2) |
| Projection | Klein chart for walked H²×E (old D9) | Log-map warp for anything walked; Klein/Poincaré for maps only | BP §0 |
| Subdivision | None (Klein), or ≲ 0.2R (Spec §6.3) | Tile levels by grid count: n = 16 per side on the walker's current tile and the 12 tiles that touch it, n = 8 on every other drawn tile, n = 4 only on the high tier beyond ρ = 3. A longest edge of 0.25 m for the Home shell, furniture and authored props | BP §3a, R3, R5 |
| Shader source | Own `CurvedSpace.hlsl`, 4×4 isometry, ⟨p,p⟩ = −R² (Spec §5.3, §6.1) | HyperEngine H²×E port (MIT); unit hyperboloid scaled by L; double 3×3 on the CPU; κ-unified kernel | BP §4, §6 step 5; this plan §6.2 |
| Tile addressing | Offline double-precision lookup tables (Spec §5.3) | Exact integer ids in ℤ[φ]; pose = (tile id, local double 3×3) | BP §0.5, R3 |
| Fog and culling | Draw radius 3–4R, exponential fog (Spec §5.3) | Fog starts at ρ = 2 and is opaque at ρ = 3 by default; every tile that reaches inside that radius and can be in view is drawn, by the CPU draw rule of §8.5 | D13, R3, R5 |
| Instancing | One instanced draw per module type (Spec §5.3) | SRP Batcher; one pooled material instance per tile or room; no property blocks | BP §4 |
| Display mode | Mode A see-through recommended (Spec §7.2) | VR, no camera background | BP §1.2, D19 |
| Safety | Safe Harbour (passthrough + outline), Stop state, operator panic (NN5, Spec §7.3) | Bump Guard: warning cue and haptic only. Its thresholds and depth rate are constants in code, with no registry parameter and no off switch. The Console's STOP button fires the same cue and nothing freezes; return to Home is a user action | D19 |
| Depth after set-up | Depth sectors drive the Stop state machine; person detection (Spec §7.2–7.3) | LiDAR environment depth stays on and feeds Bump Guard only (the seven sectors and the dynamic layer); human stencil and human depth off; meshing and planes off | D19 |
| Scanning | Team pre-scan of the venue; kit rebuild on the PC (Spec §8.1, §8.3) | Walker self-scan is the headline; pre-scan + ARWorldMap the fast path; RoomModel built on the phone | D2, D21 |
| Room role | Hub is a neutral Euclidean lobby (Spec §11.1) | Home is tile zero of the generated world; L from the loop-fit rule | D18 |
| Hyperbolic walking | Holonomy mapping of nine 1 m cells; portals + curved wings (old D15, Brief §7.13) | Mode 1 Curved Walk and Mode 2 Aligned Cells; one kernel, one tile graph | D9 |
| Glued edges | 1.5 m cell, 3×3 instanced copies, cull-flipped mirror draw (Spec §11.2) | Universal cover fixed to the floor. The cell is an open frame cut from the free rectangle (n = max(1, round(Wf / 1.5 m)) cells along a side, likewise for Df, which gives cells of 1.12 to 2.25 m), repeated as 5 × 5 GameObject copies (3 × 3 × 3 for T³) with negative scale for Klein rows. The room itself is not repeated: its walls (as kerbs) and furniture are drawn once, where they really are | BP §4, D15 |
| Portals | Custom `StencilPortalFeature`, recursion ≤ 2 (old D10) | Render Objects features, stencil refs 1–15, depth 1 ceiling, shader plane clip | BP §4 |
| 4D slicer | `Slicer4D.compute` (Spec §5.3, Brief §3.9) | `Mesh4D` on the CPU with Burst; Engine4D vertex slicer only above ~50k tetrahedra | BP §4, R2 |
| What 4D is | Phase-binned CT only; spatial 4D on the roadmap (Spec §11.3.2) | Four pieces on one slicer: toybox, worldtube, breath lane, scanner-artefact demo | D25 |
| Look and materials | `AtlasUber` lit shader, baked probes, per-region LUT (Spec §9.2) | One material, one 128² palette, vertex-colour AO, `fwidth` lines | BP §2 |
| Post-processing | On-tile post, grading, sprite bloom (Spec §5.3) | None; no bloom, no real-time shadows | BP §4 |
| Budgets | < 100 draws, ≤ 150k triangles per eye; scale 0.87 (Spec §5.2–5.3) | ≤ 300 draws, ≤ 500k triangles per frame (both eyes); render scale 0.8 (set by the eye-texture size), 4× MSAA | BP §5, R5 |
| Asset formats | GLB + meshopt + KTX2 throughout (Spec §8.2) | FBX at edit time for authored content; GLB only for Forge output | BP §3a, D20 |
| AI generation | SPAR3D or Hunyuan3D-2mini; TRELLIS.2 on the H200 (old D11) | TripoSG on the RTX 5070, hosted API fallback; H200 never on the critical path | BP §0.5, §0, D20 |
| Networking | Python FastAPI server, WebSocket + raw UDP, MessagePack (old D12, Spec §10) | `Atlas.Link` on LiteNetLib both ends; .NET 8 Hub; binary `State` at 60 Hz, JSON control, HTTP for files; Python only in the Forge | BP §0.5, R4 |
| Discovery | Bonjour or QR (Spec §10.1) | Unicast to a known IP by QR pairing; own router | BP §0.5 |
| PC role | Console and spectator in a later phase (Spec §15.2) | All four Station functions gate Phase 1 (G6) | D22 |
| Knife Map | Spectator screen with the equation card, fed by a `KnifeState` message (Spec §10.2, §13.1, Appendix B) | Name retired. Its parts are the Spectator (`P1-D05`), the flat-twin mini-map (`P1-D07`), the equation cards (`P2-C06`, `P2-A03`; audited and mirrored on the Spectator in `P4-B03`) and the judges' dashboard (`P4-D01`). `State` and `Event` carry the data (Appendix B) | this plan |
| Crash reporting | Sentry or Crashlytics with MetricKit (Spec §15.5) | Dropped. `Log` messages and the telemetry CSV take its place (Appendix C.3) | this plan |
| Medical data | One real patient throughout; segmented airway with distal completion (old D13, old D17, old D18) | 4D-Lung patient 101 beside a labelled illustrative twin; procedural airway, optional AeroPath proximal tree | BP §0.5, D25 |
| Play area | Taped 3×3 m square, centre X, stanchions (old D8, Spec §6.4) | Free walk on the scanned free floor. The product marks nothing on the real floor; the lap square marked for the spotter at gate G2 is a test aid | D2 |
| Comfort | Vignette during world motion (Spec §6.8) | No vignette while walking; gains at identity; gate G2 on Mode 1 | BP §0.5, R1 |
| Hand input | Vision hand pose, pinch, the gesture set and the hand "hero" techniques (old D7; Spec §4.5, §7.1; Brief §3.5) | Not built. Inputs are the gamepad, gaze dwell and the headset lever (`P1-C10`); the marker cube is Extended (open item 11); hand tracking exists only as an optional HoloKit spike (Appendix C.3) | BP §0.5, this plan |
| Use cases | Shared House and FieldFit late and first to be cut (Spec §15.2, §15.6) | All carried: P2 Playground + Lung Mission; P3 Shared House + FieldFit | D3 |
| Tier B and C blades | "Station Zero", Small Planet, Wormhole Quest, the 4D Toybox and the other Tier B and C blades; "Station Zero" as the swap for Shared House (Brief §2.3, §4.6; Spec §17 Q7) | Not built as use cases. The geometry of Small Planet is space S4 and the 4D Toybox is the toybox of the Slicer (S6); the rest stay as documented ideas in the Brief | D1, D3 |
| House Call partner | Galaxy S24 Android client, LiveKit voice, avatar (old D3; Spec §11.4) | No Android client and no LiveKit (Appendix C.3). Core is Post Circling, solo, with a recorded visitor. A live House Call is Extended, on phone B (`P3-C04`, `P3-D07`), in a second headset if there is one (open item 14); voice is Stretch (§10.3.1) | D3, this plan |
| Process and timescale | Calendar-sized phases, spike phase, cut-first list, unowned areas (Spec §15) | Gate-driven (G0–G15); four lanes A–D; tiers Core / Extended / Stretch | D3, D5, D4 |

### D.2 Build Pipeline items overridden by newer decisions or by verified research

| BP item | Replaced by | Reason |
|---|---|---|
| Taped, roped square with a spotter (§1.2, §4) | Free walk in the scanned room | D2 |
| "Turn off … occlusion" after set-up (§5) | Environment depth stays on; meshing, planes, probes, light estimation off | Bump Guard needs live depth (D19) |
| "Don't let the user cross a glued edge" (§4) | The walker crosses freely | The cover is fixed to the floor: nothing is re-centred, so no global inverted culling (D15) |
| Glued Room: one combined mesh per copy, under 5k triangles each (§5) | Each copy is an open-frame cell of at most 1,500 triangles, with a self-avatar in every copy but the walker's own. The room's walls (as kerbs) and furniture are drawn once, where they really are | A copy of the room with its furniture cannot be held under 5k: the furniture alone may reach 60,000 triangles (§4.5), and 25 copies of that for two eyes would be 3 million triangles a frame against a budget of 500k (derived). Drawn once, everything that looks solid is where the real thing is |
| Fog at 5–8 units; cull at d > ~4 (§0.5, §4) | Fog opaque at ρ = 3; every tile that reaches inside that radius and can be in view is drawn (§8.5) | 5–8 is the float-precision horizon; 45 tile centres lie within ρ = 3 (at most 71 tiles drawn) but 129 within ρ = 4, over the draw budget (R3, R6) |
| Thermal Player Setting "on 6.5+" (§1.3) | Pin Unity ≥ 6000.3.21f1 | The setting exists from that patch (R5) |
| Accept Unity's 30 fps cap at Serious (§1.3, §5) | Cap switched off; the ladder keeps 60 fps at Serious and locks to 30 fps only on its last rung | A silent drop to 30 fps with no reprojection is the worst outcome in a headset; a controlled quality drop is better (R5) |
| CC0 kits first for props (§0, §3b) | Scanned objects are generated live by the Forge, at most 20 per room and the largest first; further objects stay placeholder boxes. A built-in fallback set (one CC0 mesh per RoomPlan category, `P1-D12`) replaces the boxes when the Forge is unreachable | D20 |
| Tile pivot at a floor corner (§3a) | Curved tiles pivot at the tile centre; flat rooms keep the corner | The centre is the chart origin (§3.4, R6) |
| 0.1–0.25 m subdivision, undifferentiated (§3a) | Tile levels by grid count: n = 16 per side on the walker's current tile and the 12 tiles that touch it, n = 8 on every other drawn tile. The 0.25 m longest-edge rule stays for the Home shell, furniture and authored props | A fine grid on every tile is too dense when up to 57 tiles are drawn. A count per side holds the triangle caps for any L and lets tiles of different level share their edge vertices exactly (R3, R5) |
| MSAA 2× as the start (§4) | 4× at scale 0.8; 2× on the thermal ladder | Fill rate is not the constraint (R5) |
| Half-screen viewports for the two eye cameras; render scale 0.75–0.8 set in the URP asset (§0, §4) | One render texture per eye, 1118 × 1032 (0.8 × the half-screen), each eye camera with a full viewport. URP Render Scale stays 1.0 and the texture size carries the 0.8. One final blit samples both textures | Two eye cameras are two render passes. Drawn into one multisampled target, the second pass would have to load what the first stored, so the multisampled colour could not be memoryless or resolved on-tile (R5) |
| HoloKit SDK spike first (§0.5) | Own rig is the baseline; HoloKit an optional hand-tracking spike | HoloKit is built for optical see-through, needs iOS 18+ (the target is iOS 17.0) and still needs our own barrel pre-distortion blit. For a lens headset it adds only hand tracking, which is optional (rule 5). BP §0.5 itself names the own rig as the fallback, and the rig is on the path to G1, so it is built first (D7) |
| LiteNetLib with no PC peer named (§0.5) | The .NET Hub terminates it | Own wire protocol: both ends must be .NET (R4) |
| Warped tiles and rooms rebuilt in Blender, with the Blender MCP (§0, §3a) | Tile shells and the Home shell generated in C# at run time (`P1-B05`, §3.4); Blender only for landmarks and props | L and the tile size come from the scanned room and q from the dial, so a tile cannot be authored for one fixed L with a 0.25 m grid; the Home shell comes from the walker's own scan. The MCP was available only to the session that wrote BP |
| Timed order-of-work list (§6) | Gates | D4 |

### D.3 Still valid: read the older documents for detail

- **Interaction design space, technique catalogue, unifier results:** Brief §3.1–3.6.
- **Claims language:** Brief §2.4 (the phrase check of `P4-D05` uses its full Avoid list); Real / Pre-baked / Mocked table Spec §14.3.
- **Why each use case needs its geometry:** Brief §4.0.
- **Kernel identities and formula sheet:** Spec §6.1, Appendix A ([D] items still need checking).
- **Mapping scanned meshes into curved space:** Spec §6.3, for the distortion table and the rule "keep objects under about 0.3 L" only. Its pipeline (steps 1–6), Klein placement and H³ honeycomb table are superseded by §3.4 and §4.6 of this plan (D.1, rows Projection and Subdivision).
- **Glued-room maths notes:** Spec §6.2, §11.2; Brief §3.7.
- **Branched-cover maths (Shared House):** Spec §11.4, Appendix A.
- **FieldFit lens maths:** Spec §11.5; Brief §4.5.
- **4D maths:** Brief §3.9 (tetrahedron ∩ hyperplane, quaternion double cover).
- **Robustness rules:** Spec §6.8 and Brief §3.4, rules R1–R5 and R7 only. They apply to the marker cube (Spec §7.1) and to any later hand input. R6 (vignette during world motion) and R8 (Safe Harbour) are superseded (D.1, rows Comfort and Safety).
- **Architecture rules:** Spec §3.4, for the principle that the phone decides everything within a frame and never waits for the Station; and the rule that safety never references the link (Spec §3.2). The Spec's assembly map (§3.2), frame loop (§3.3) and interfaces (§3.5) are superseded by §6.2, §6.3 and §6.5 of this plan.
- **Depth-sector job:** Spec §7.3 (seven sectors across ±35° of the view direction, on the 256 × 192 depth image), as a Bump Guard input only. The Spec's 10 cm occupancy grid, its distances and hysteresis, person detection and the state machine are superseded by §4.2 and §4.10 of this plan (D.1, rows Safety and Depth after set-up).
- **Router and Wi-Fi set-up:** Spec §10.1. **Signing:** Spec §2.8 and Spec App. E.
- **Evaluation instruments:** Brief §8, Spec §13. **Prior art:** Brief §12.
- **Consent, FMS and SEQ wording; booth-sign text and exclusion list:** Brief §8.1, §9.9. On the sign, "inside a taped 3×3 m square" becomes "in the room you scanned", and the data line also carries the hosted-fallback disclosure of §7.4. The spotter briefing is the one in §13.2 of this plan.

### D.4 Errors in the older documents that the plan corrects

| Error | Correction | Source |
|---|---|---|
| Slice Tilt rotates in the time–left-right plane (Spec §11.2.1); both axes lie in the slice, so nothing changes | Tilt in the z–τ plane | R2 |
| "Precompute 7 tilted slices and blend neighbours" (Spec §11.2.1) | Tilted slices change topology: slice live, never cross-fade | R2 |
| Airway layout (Spec §6.7): ℓ = 1.5 m, R = 2.2 m, 120° forks | Overlaps (372 edge crossings by depth 8); 120° forks need ℓ ≥ L·ln 3; tree snapped to the {4,5} tile graph | R3 |
| "About 23 generations" (Brief §4.3.1) | About 16 conducting, 23 in all | R2 |
| ≈ 17 mm quoted as CT-to-body divergence (Spec §11.3.2) | It is nodule displacement between breath states; not shown as divergence | R2 |
| "GTV + OARs on all 10 phases" (Spec §11.3.4) | Tumour contours on all phases for 20/20 patients; lungs for 5/20 | R2 |
| ITV as the OR of phase masks (Spec §11.2.1) | Under-covers small fast nodules; interpolate sub-steps | R2 |
| Illustrative 12 mm motion implied for the real case (Brief) | Real median 4.3 mm; subtle real tube beside the labelled twin | R2 |
| Real obstacles become props at the same place in every space (Brief §3.8) | Holds only in registered spaces (S0, S1, S3, S5, S6) | R1 |
| Holonomy comfort evidence applied to smooth curved walking | Applies to Mode 2 only; Mode 1 has gate G2 | R1 |
| Post-processing both off and on (Spec §5.2 vs §5.3) | Off | this plan |
| Kit grid 0.5 m vs 1–2 m (Spec §8.2 vs §9.2) | 0.5 m | this plan |
| Draw calls < 100 per eye vs ≈ 250 (Spec §5.3) | ≤ 300 per frame, both eyes | this plan |
| Hyperboloid −R² (Spec §6.1) vs unit form (Spec §6.3) | Unit, scaled by L | this plan |
| Three hyperbolic constructions with no priority; no R for a scanned room | Modes 1 and 2, portals as S5; L from the loop-fit rule | this plan |
| `OperatorCmd` fields differ between Spec §10.1 and its Appendix B | Appendix B of this plan is the only schema | this plan |
| Swap phones at "fair" although "fair" is a full-quality tier (Spec §5.4) | One thermal ladder, defined in §8 | this plan |
| Phone B both scans and does not scan objects; "≤ 2–3" vs "≤ 2" tracked images; 63 vs 64 techniques; "Shared House" vs "House Call" | Each stated once in this plan; the name is Shared House | this plan |

### D.5 Where the risks of Spec §16 went

Spec §16 lists 21 engineering risks, and Brief §11 carries the same list. Each one is now a row of the risk register (§12.1), an open item (§14), or settled by a decision or a section of this plan.

| Spec risk | Now |
|---|---|
| 1 Thermal throttling | Risk 11; risk 12 for the heat of live depth |
| 2 Hands not seen | Retired: hand input is not built (D.1, row Hand input) |
| 3 Drift or tracking loss | Risk 16 |
| 4 Cybersickness | Risk 5 for Mode 1; risk 30 for the other spaces |
| 5 Read as "just portals" | §13.1 and §13.3: the equation cards and the 3-4-5 walk |
| 6 Medical over-claiming | Risk 25 |
| 7 Second phone or network failure | Post Circling is Core, solo and needs no network (§10.3) |
| 8 AI generation slow or unavailable | Risk 22 |
| 9 RTX 5070 incompatibilities | Risk 23 |
| 10 Licences | Risk 26 |
| 11 Headset fit | Open item 1 |
| 12 Scope creep | D3 fixes the four use cases; inside them the tiers are Core / Extended / Stretch; the rest of the design space stays in the Brief (D.3) |
| 13 Challenge rules unknown | Open item 15 |
| 14 Hyperbolic navigation | §3.5 |
| 15 Privacy | Risk 31 |
| 16 No single-pass stereo | Risk 13 |
| 17 Motion-to-photon | D8 and §8.7; measured by the motion-to-photon row of §11.2 |
| 18 Airway segmentation too shallow | Retired by D25: the airway is procedural |
| 19 Over-claiming the hyperbolic airway | §10.2.1 (what is not claimed) and §10.2.3 (honest limits) |
| 20 Free-account limits | Risk 27; the app ships with no entitlements (Appendix C.1) |
| 21 Feels like a tour | §13.3: one rule per space |
