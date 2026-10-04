## Appendix E. Glossary

One sentence per term. Canonical ATLAS names are in **bold** and are filed under the group they belong to.

### E.1 Geometry and mathematics

| Term | Meaning |
|---|---|
| **3-4-5 walk** | Walk s, turn left 90°, repeat: the loop closes after 3 corners on the sphere, 4 flat, 5 hyperbolic. |
| **Aligned Cells (Mode 2, S3)** | Hyperbolic mode in which square floor cells stay registered to the room and each shows one flat tile chosen by the path walked. |
| Angle defect | 360° minus the sum of the angles meeting at a vertex; five squares give −90°, which is curvature concentrated at a point. |
| Branched cover | A space wrapped several times round a point, so one circuit does not bring you back. |
| Cone point | A point where the total angle around it is not 360°. |
| **Curved Walk (Mode 1, S2)** | Smooth H²×E drawn with the log-map warp; not registered to the real room. |
| Exp map / log map | Exp turns a direction and distance into the point reached; log is its inverse and is what the vertex warp uses. |
| **Free rectangle** | The largest rectangle of walkable floor aligned to the dominant wall direction, Wf × Df, already 0.30 m inside walls and furniture; the RoomModel stores it as `freeRect`. |
| Gauss–Bonnet | The angle excess of a geodesic polygon equals curvature × its area. |
| Geodesic | The straightest possible path in a space. |
| Holonomy | The rotation a direction picks up when carried round a closed loop (curvature × enclosed area; 72° per {4,5} tile). |
| Hyperbolic plane (H²) | The surface of constant negative curvature, where area grows exponentially with radius. |
| Hyperboloid model | H² as the sheet x² + y² − t² = −1; the kernel stores poses as 3×3 matrices acting on it. |
| Hyperplane slice | The 3D cross-section of a 4D object cut by a flat 3D space. |
| Klein bottle | A square with one pair of edges glued straight and the other pair glued with a flip. |
| Klein model | Disc picture of H² in which geodesics are straight chords; used for maps and tile authoring only. |
| **L**, **ρ**, **R** | L is the hyperbolic curvature radius, in metres per hyperbolic unit (the Spec calls it R); ρ is hyperbolic distance in units of L; R in this plan is the sphere radius of the Small Planet. |
| **Lap square** | The square of side s walked in the 3-4-5 walk; it is centred on the anchor, at least 0.20 m inside the free rectangle and so at least 0.5 m from the nearest wall or scanned obstacle. |
| **Loop-fit rule** | s = clamp(min(W, D) − 1.0 m, 1.0 m, 3.2 m), with W and D measured wall to wall in an empty rectangular room (in code, from the RoomModel's `freeRect`: s = clamp(min(Wf, Df) − 0.4 m, 1.0 m, 3.2 m)); then L = s/1.0613 (hyperbolic) and R = 2s/π (spherical). |
| Lorentz boost | The hyperboloid-model matrix that translates along a geodesic; one is applied per walked step. |
| Poincaré disc | Disc picture of H² that keeps angles true; geodesics are arcs meeting the rim at 90°. |
| Projection (shadow) | Flattening a 4D object onto 3D by discarding one axis. |
| {p,q} tiling | Regular p-sided tiles, q at each corner; {4,5} is hyperbolic, {4,4} flat, {4,3} the cube on a sphere. |
| Registered | Said of a space in which the virtual walls and furniture are where the real ones are (§4.9); Curved Walk and the Small Planet are registered only until the first step. |
| Tesseract | The 4D cube: 8 cubic cells, 16 vertices. |
| **Tile zero** | The home tile of the {4,5} tiling; it carries the rebuilt room, and it is not the same thing as "the walker's current tile", the tile the walker stands on at that moment. |
| Torus (T²) | A square with opposite edges glued without flips. |
| Universal cover | The unrolled, endlessly repeating space of which a glued space is the folded form. |

### E.2 Medical

| Term | Meaning |
|---|---|
| 4D-CT | A CT sorted into (here ten) 3D volumes, one per part of the breathing cycle. |
| **Breath lane** | Breath time laid along the longest clear floor segment, one breath per 1.5 m, repeating. |
| CT-to-body divergence | The mismatch between airway and lesion positions on the planning CT and in the patient during bronchoscopy. |
| Gating, duty cycle | Treating only inside a chosen phase window; duty cycle is the fraction of the breath the beam is on. |
| GTV | Gross tumour volume: the visible tumour on one image. |
| ITV / IGTV | The volume the tumour sweeps over a breath; IGTV is the union of the GTV over all phases. |
| Mid-ventilation | The phase at which the tumour is nearest its time-averaged position. |
| MIP | Maximum intensity projection: each voxel's brightest value over all phases. |
| Respiratory phase | Position within one breath, 0–100 %; a point on the circle S¹. |
| **Worldtube** | The tumour over one breath as a single 4D object in ℝ³×S¹ (our term). |

### E.3 Engineering

| Term | Meaning |
|---|---|
| ARWorldMap | ARKit's saved tracking map, reloaded to relocalise in a pre-scanned room. |
| **ATLAS app** | The Unity iOS app on the phone; renders everything and runs all safety. |
| `budget_room` | The performance fixture frozen at G0: a 4 × 5 m room with twelve stand-in objects of exactly 5,000 triangles each and no edge over 0.25 m; budgets are measured on it, and the three sample rooms stay for functional tests. |
| **Bump Guard** | On-phone safety: the static scan map and live LiDAR depth (with its dynamic layer) predict contact and give a warning cue and haptic; its thresholds and depth rate are constants in code, with no registry parameter, no off switch and no use of the network. |
| `BumpCue` | The component in `Atlas.Safety` that produces the warning cue (indicator, sound and haptic); its one local entry point `Fire(level, direction)` is called by Bump Guard and by the Console's STOP button (`P1-C14`). |
| Burst | Unity's compiler that turns C# jobs into optimised native code. |
| **Console** | The web page served by the Hub: tuner, operator buttons, telemetry. |
| Dynamic layer | Bump Guard's memory of obstacles that were not in the scan: 5 cm cells marked where live depth lands more than 0.3 m nearer than the static map predicts, each cleared only when it is later seen empty or after 30 s without a new hit. |
| Flat twin | The flat picture that rule 6 requires beside every curved or glued view: the space's flat map with the walker's path (Poincaré disc, lattice plan, cube net, room graph), drawn as the mini-map in the headset and beside the mirror on the Station; not the same as an input twin (a second way to do an action, rule 5) or the illustrative twin (the model nodule of the Lung Mission). |
| **Forge** | The Python service that turns scans and object photos into meshes. |
| GLB | Binary single-file glTF; the format of returned furniture meshes. |
| **Home (S0)** | The scanned room rebuilt 1:1, flat, registered to reality. |
| **Hub** | `atlas-hub`, the .NET 8 process that terminates the link and serves the Console. |
| LiDAR depth | Per-frame depth image from the phone's LiDAR, range about 5 m (R1). |
| LiteNetLib | MIT reliable-UDP library for .NET, used at both ends of the link. |
| **Mesh4D** | Tetrahedral mesh in ℝ⁴ sliced by a hyperplane on the CPU. |
| MSAA | Multisample anti-aliasing; 4× to start. |
| Render scale | Fraction of the half-screen resolution (1398 × 1290) at which each eye is rendered before the final blit upscales it; 0.8 to start, which is an eye texture of 1118 × 1032, set by the texture size while URP Render Scale stays 1.0. |
| **RoomModel** | Scan output: free-floor grid, walkable polygon, free rectangle, walls, object boxes, anchor, s and L. |
| RoomPlan | Apple's room-scanning framework; through AR Foundation it yields furniture boxes and categories only. |
| **SpaceLab** | Host scene that loads one space module and exposes its parameters. |
| **Spectator** | Unity process on the Station that re-renders the walker's view from streamed state. |
| SRP Batcher | URP draw path that makes draws sharing a shader variant cheap. |
| **Station** | The Windows PC side: Hub, Spectator and Forge; the word always means this PC, and a stop of the Geometry Playground is an exhibit (E.4). |
| Stencil portal | A doorway drawn by masking pixels and drawing the far room inside the mask. |

### E.4 Scope, use cases and evaluation

| Term | Meaning |
|---|---|
| Blade | The older documents' word for a use case; in this plan it survives only in the names `Atlas.Blades.*` and `Assets/Atlas/Blades/`. |
| Core / Extended / Stretch | Scope tiers of a task or work package: Core is required by a gate; Extended is built once the Core it extends has passed, and feeds no gate; Stretch is optional and feeds no gate. |
| **Exhibit** | One stop of the Geometry Playground: a Phase-1 space wrapped with its caption, tasks, equation card and events; never called a station, because the Station is the PC. |
| FMS | Fast Motion Sickness scale: one spoken score from 0 (no sickness) to 20, asked once a minute; the plan's comfort score. |
| SEQ | Single Ease Question: one rating from 1 (very difficult) to 7 (very easy), asked once per use case. |

## Appendix F. Sources

Every URL below appears in the research memos (R1–R4), the Build Pipeline or the Spec/Brief reference lists, except the TripoSG repository, which the Build Pipeline names without a link.

### F.1 Non-Euclidean rendering and prior art

- [Holonomy (TU Delft, Web3D 2024)](https://arxiv.org/abs/2409.05460): nearest prior art; basis of Aligned Cells
- [Non-Euclidean VR II: H²×E](https://arxiv.org/abs/1702.04862): log-map drawing and relative motion for Curved Walk
- [Weeks, body coherence](https://arxiv.org/abs/2011.00510): holonomy per area; track everything from one anchor
- [HyperEngine](https://github.com/HackerPoet/HyperEngine): H2xE shader and tile mapper to port
- [HyperRogue `vr.cpp`](https://github.com/zenorogue/hyperrogue/blob/master/vr.cpp): headset holonomy modes; read for ideas only
- [Tree structures for hyperbolic tessellations](https://arxiv.org/abs/2111.12040): canonical tile words and generation rules
- [OEIS A377322](https://oeis.org/A377322): checks {4,5} tile counts per depth
- [Slotboom 2023 thesis](https://resolver.tudelft.nl/uuid:efc67ab5-16bb-4f01-9478-7cee03968257): stencil versus render-texture portal cost in VR
- [ar-portal-arfoundation-urp](https://github.com/Tongzhou-Yu/ar-portal-arfoundation-urp): closest stencil-portal repo for AR Foundation and URP
- [Engine4D](https://github.com/HackerPoet/Engine4D): vertex-shader 4D slicer above 50k tetrahedra
- [Zucconi, rendering 4D objects](https://www.alanzucconi.com/2023/07/06/rendering-4d-objects/): tetrahedron slicing explained step by step

### F.2 Walking and comfort

- [Steinicke 2010](https://www.uni-muenster.de/imperia/md/content/psyifp/ae_lappe/freie_dokumente/stein__bruder__jerald__frenz__lappe_2010.pdf): detection thresholds for rotation, distance, curvature
- [Widdowson and Wang 2022](https://par.nsf.gov/servlets/purl/10387047): people home as if Euclidean; add beacon
- [Auda, "If The Map Fits!"](https://hci.informatik.uni-due.de/en/publications/if-the-map-fits-exploring-minimaps-as-distractors-from-non-euclidean-spaces-in-virtual-reality-16030): minimaps distract; show the flat twin late
- [Suma, impossible spaces](https://doi.org/10.1109/TVCG.2012.47): overlap limits for Impossible Rooms
- [Tea For God devlog](https://void-room.itch.io/tea-for-god/devlog/235914/open-world-with-impossible-spaces): worlds generated to fit any play area
- [VRoamer](https://www.microsoft.com/en-us/research/publication/vroamer-generating-on-the-fly-vr-experiences-while-walking-inside-large-unknown-real-world-building-environments/): real obstacles become virtual objects

### F.3 Scanning and ARKit

- [ARKit bounding boxes](https://docs.unity3d.com/Packages/com.unity.xr.arkit@6.4/manual/arkit-bounding-boxes.html): RoomPlan object boxes through `ARBoundingBoxManager`
- [ARKit meshing](https://docs.unity3d.com/Packages/com.unity.xr.arkit@6.4/manual/arkit-meshing.html): classified LiDAR mesh for walls and floor
- [RoomPlan](https://developer.apple.com/documentation/roomplan): Swift plugin route for walls, doors, windows
- [WWDC22 RoomPlan session](https://developer.apple.com/videos/play/wwdc2022/10127/): room size, light and scan-length limits
- [Scene depth](https://developer.apple.com/documentation/arkit/arconfiguration/framesemantics-swift.struct/scenedepth): the LiDAR depth frames Bump Guard reads
- [Saving and loading world data](https://developer.apple.com/documentation/arkit/saving-and-loading-world-data): ARWorldMap for the pre-scan fast path
- [Forum 763400](https://developer.apple.com/forums/thread/763400): RoomPlan drops `sceneDepth` until configuration is re-run
- [Forum 827240](https://developer.apple.com/forums/thread/827240): iOS 26.4 tracking drift; check phone version

### F.4 Networking

- [LiteNetLib](https://github.com/RevenantX/LiteNetLib): transport library, wire format, channel types
- [TN3179 local network privacy](https://developer.apple.com/documentation/technotes/tn3179-understanding-local-network-privacy): iOS permission prompt and unicast rules
- [NSAllowsLocalNetworking](https://developer.apple.com/documentation/bundleresources/information-property-list/nsapptransportsecurity/nsallowslocalnetworking): plain HTTP to the Station allowed
- [libusbmuxd](https://github.com/libimobiledevice/libusbmuxd): `iproxy` is TCP only; cannot carry LiteNetLib

### F.5 Performance

- [SRP Batcher](https://docs.unity3d.com/6000.0/Documentation/Manual/SRPBatcher.html): batching rules behind the one-shader draw path
- [`Graphics.RenderMeshInstanced`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.RenderMeshInstanced.html): instanced tile shells if render thread binds
- [Meta Unity performance guide](https://developers.meta.com/vr/documentation/unity/unity-perf/): mobile-VR draw-call and triangle guidance
- [Best darn grid shader](https://bgolus.medium.com/the-best-darn-grid-shader-yet-727f9278b9d8): `fwidth` tile lines that do not alias

### F.6 Medical 4D and data

- [TCIA 4D-Lung](https://www.cancerimagingarchive.net/collection/4d-lung/): the real case, patient 101_HM10395; cited as Hugo et al. 2016, doi 10.7937/K9/TCIA.2016.ELN8YGLE, plus the TCIA citation
- [AAPM TG-76](https://www.aapm.org/pubs/reports/RPT_91.pdf): respiratory motion management; gating definitions
- [Seppenwoolde 2002](https://europepmc.org/article/MED/12095547): how far lung tumours move, and the 1 to 5 mm loop between breathing in and out
- [Liu 2007](https://mdanderson.elsevierpure.com/en/publications/assessing-respiration-induced-tumor-motion-and-internal-target-vo): share of tumours that move more than 5 and 10 mm
- [Chen 2004](https://europepmc.org/article/MED/14752730): helical-CT motion artefacts; the tilted-slice demo
- [Underberg 2004](https://europepmc.org/article/MED/15519801): a uniform 10 mm margin against an individual ITV
- [Ezhil 2009](https://doi.org/10.1186/1748-717X-4-4): IGTV from all phases versus from MIP
- [Wolthaus 2006](https://europepmc.org/article/MED/16863933): mid-ventilation and time-averaged position
- [Chen 2015](https://europepmc.org/article/MED/25357229): nodule displacement between the planning scan and tidal breathing (17.6 mm)
- [Ravikumar 2023](https://pmc.ncbi.nlm.nih.gov/articles/PMC10001114): CT-to-body divergence in navigation bronchoscopy
- [AeroPath](https://zenodo.org/records/10069289): optional real proximal airway tree
- [Kitaoka 1999](https://doi.org/10.1152/jappl.1999.87.6.2207): procedural airway rules; verify equations before quoting

### F.7 Assets and licences

- [TripoSG](https://github.com/VAST-AI-Research/TripoSG): the Forge's local image-to-3D model on the RTX 5070; MIT (BP §0.5)
- [Meshy](https://meshy.ai): a candidate for the Forge's hosted image-to-3D fallback (open item 8)
- [Hunyuan3D-2 licence](https://github.com/Tencent-Hunyuan/Hunyuan3D-2): territory and use restrictions to read
- [Kenney](https://kenney.nl/support): CC0 props and audio for the fallback set

### F.8 Licence notes

| Item | Licence | What we may do |
|---|---|---|
| HyperRogue | GPL-2.0 | Read for ideas only; no code enters the repository. |
| HyperEngine, Engine4D, LiteNetLib, TripoSG | MIT | Port or use; keep the copyright notice. |
| TCIA 4D-Lung | CC BY 3.0 | Use with credit on screen and in the README, saying that the meshes are derived; the data-use terms are acknowledged and the citation recorded in the licence ledger (open item 18). |
| AeroPath | CC BY 4.0 (Zenodo; the GitHub README says MIT) | Attribute under CC BY 4.0 (R2). |
| ATM'22 | Signed agreement; redistribution prohibited | Do not ship or commit. |
| Hunyuan3D | Excludes the EU, UK and South Korea; bans medical decision use; outputs labelled AI-generated | Off the default path (BP §0.5). |
| Meshy free tier | CC BY 4.0 | Credit Meshy for any free-tier mesh. |
| Unity Asset Store free packs | Not redistributable | Keep out of the public repository (BP §3d). |
