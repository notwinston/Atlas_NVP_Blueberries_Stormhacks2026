## 13. Demo, documentation and presentation

This section covers Phase 4 (gates G14 Feature freeze, G15 Golden build and submission) and everything a judge sees. Paths under `docs/` and `media/` are proposed.

### 13.1 Rubric-to-evidence map

| Rubric line | Shown live | Number or figure behind it | Lives in |
|---|---|---|---|
| Geometric Creativity 30% | Scanned room as tile zero; curvature dial; 3-4-5 walk; Mode 1 and Mode 2 on one tile graph; S1, S4, S5, S6 | Corners 3 / 4 / 5, closure error < 0.25 m (derived); holonomy error < 1°, Lorentz drift < 1e-9 (R5 D1) | `docs/maths.md`, `docs/evidence/correctness.json` |
| UX and Interaction 30% | Self-scan onboarding; walk + look + one button; Bump Guard cue and haptic; return to Home | FMS per minute, SEQ, time to first walk, pointing error (R5 D2) | `docs/metrics.md`, `docs/pilot/` |
| Graphics and Technical Execution 25% | 60 fps in the headset; Spectator re-render; Console telemetry | Frame time p50 / p95 / p99, draw calls ≤ 300, triangles ≤ 500k, thermal timeline (R5 D1) | `docs/metrics.md`, `docs/evidence/perf_<session>.csv` |
| Documentation and Presentation 15% | Dashboard reveal, honesty table, deck, video | Deliverables of 13.4 complete | `README.md`, `docs/`, `media/` |
| Bonus: beyond gaming | Lung Mission (hyperbolic airway walk, worldtube, breath lane, scanner artefact); FieldFit | ITV/GTV of patient 101 beside the published median 1.35 (R2) | `docs/maths.md` |
| Bonus: educational value | One rule per space; equation card, predicted against measured | One card per space S0–S6 | App, Spectator |
| Bonus: polish | Shared look; audio loop per space; distinct warning cue | 15-minute soak at "must" values on both phones | `docs/metrics.md` |
| Bonus: novel technical approach | Loop-fit rule; exact ℤ[φ] tile ids; two renderers on one kernel; live Forge furniture | Tile centres within ρ = 2 / 2.5 / 3 / 3.5: 13 / 25 / 45 / 81 (derived); Forge time per object, to be measured | `docs/architecture.md` |

### 13.2 The demo

#### Roles, space, kit

| Role | Does |
|---|---|
| Narrator | Talks to the judge; asks FMS and SEQ; relays "home" and "stop" to the operator (the Console's return-to-Home and STOP commands, §7.3.2) |
| Operator | At the Station: Console, space switching, FMS entry, recording |
| Spotter (recommended) | Walks behind the judge; a touch on the shoulder means stop. Bump Guard is warning and haptic only (D19), depth sees only forwards, and Mode 1 is not registered to the room (§4.6). Never merged with another role (Spec §14.1) |
| Explainer | At the Spectator screen for onlookers |

Space: free rectangle with min(Wf, Df) ≥ 1.5 m (about 2.1 m wall to wall), the smallest floor that is not standing mode (§4.4). One level floor, with no stairs or step-down in or beside the walked area; glass doors and mirrors are left outside the confirmed outline; cables and loose objects are removed (§4.10). Onlookers stay outside the scanned floor. Even, flicker-free light (Spec §14.1).

Kit: phones A and B; the lens headset (a second one only if open item 14 provides it), stored lens-down away from windows; Station; Mac with cables; own 5 GHz router; chargers; desk fan; face covers, wipes, dry microfibre for lenses; booth sign; chair and water (Spec §14.1).

#### Onboarding (phone in hand until step 5)

1. Narrator asks: VR before, carsick easily, glasses. Consent line and baseline FMS (Brief §8.1). The operator tells a walker from outside the team that, if the Station's own generator fails, crops of the object photos go to a hosted service whose retention we do not control; if the walker declines, the operator sets `forge.backend` to cache before the scan (§7.4).
2. App launch: camera and Local Network prompts are answered in hand, never in the headset (BP §0.5).
3. Pairing: the judge scans the Hub's QR code; the Console shows "phone connected" (R4).
4. Scan: 30–60 s self-scan, the judge confirms the floor outline (D21). Fast path: team pre-scan and saved ARWorldMap relocalise instead.
5. Headset on, focus check, one button press. Spotter: "I stay behind you. If I touch your shoulder, stop. A sound and a buzz mean something real is close."

Target: first walk ≤ 90 s after launch (R5 D2).

#### Run-of-show

The organisers' slot length is unknown (Brief §13); both scripts are rehearsed.

Long slot (10 min, self-scan):

| Time | Segment | Judge does | Spectator shows |
|---|---|---|---|
| 0:00–0:40 | Frame | Consent, FMS, prompts, pairing | "Connected" |
| 0:40–1:40 | Scan | Scans, confirms outline, headset on | RoomModel outline; s and L |
| 1:40–2:40 | Home and dial | Sees the room 1:1, bends it, walks the five-corner lap | 3-4-5 card: corners, closure error |
| 2:40–3:40 | Curved Walk | Walks to a landmark; furniture meshes arrive; switches to Aligned Cells | Forge queue; flat-twin mini-map after the lap (Brief §12) |
| 3:40–4:20 | Airway | Walks one fork of the hyperbolic airway (two tile steps, one valve chamber) on the one-fork demo route, ending in the lesion chamber | Poincaré map and dendrogram with the walker's address; "2^16 branches, same corridor width" |
| 4:20–5:00 | Slicer | Slices and rotates the tesseract | Slice and shadow |
| 5:00–6:10 | Lung 4D | Unroll time; worldtube slice and shadow; breath lane; tilted slice | ITV number; artefact view |
| 6:10–6:25 | Exit | Headset off; exit FMS; SEQ | "Preparing your results" |
| 6:25–7:10 | Dashboard reveal | Turns to the screen | Path, comfort, performance, mathematics |
| 7:10–10:00 | Questions | Judge's choice: S1, S4, S5, Post Circling or FieldFit | Honesty table; architecture |

The demo uses a one-fork route whose end tile is the lesion chamber (a Console switch); the four-fork route of §10.2.3 stays the G10 test.

Headset time 4:45, under the 5 min limit (Brief §9.4).

Short slot (5 min, pre-scan fast path): frame 0:25 → relocalise 0:20 → Home, dial, lap 0:45 → Curved Walk 0:45 → worldtube and breath lane 0:45 → exit 0:10 → dashboard 0:30 → questions 1:20. Headset time 2:45. Slicer, Aligned Cells, the airway fork and the artefact move to video.

#### Comfort rules

- Real walking only; identity gains; no vignette while walking (BP §0.5).
- L changes only while the judge stands still (R1).
- FMS (0–20) is spoken once a minute; the operator types it into the Console.
- Stop threshold: **to be confirmed by the team** (open item 6, §14). The older documents offer ≥ 4 (Brief §8.1); the performance research offers ≥ 10 (R5 D2). The registry key is `pg.fmsStop` (§10.1.4). At the threshold, or on any verbal discomfort: headset off, chair, water. FMS > 3 switches Mode 1 to Mode 2 (G2 rule).
- Order: flat and registered (Home) → curved (Mode 1) → stationary (Slicer, worldtube).

#### Fallbacks

| Failure | Detected by | Immediate action | If it persists |
|---|---|---|---|
| No link | Console RTT tile; phone link icon | Continue: the app is standalone, Bump Guard never uses the network, the CSV is written on the phone | Dashboard from the phone's file; video for onlookers |
| Hot phone | Thermal state on the Console | Thermal ladder drops a rung; the narrator names it | Swap phones between judges |
| Bad scan | Outline wrong at confirmation; or the Bump Guard "rescan" icon during the walk (§4.10) | Manual corner taps; on the "rescan" icon the judge stands still, the headset comes off and the room is rescanned | Team pre-scan and ARWorldMap |
| Forge down | Forge tile on the Console | Hosted API if the walker was told and agreed, otherwise built-in set | Built-in set, labelled "built-in" |
| Tracking lost | Tracking state; pose jump > 0.2 m | Judge stands still; spotter steps in; relocalise, target ≤ 15 s (Brief §9.6) | Home; phone B; video |
| App will not open | Launch check before each session | Phone B; reinstall from the Mac | Video and a saved dashboard |

Phone swap rule: never start a session on a phone reporting Serious. Between judges, swap if the Console shows Fair (amber). During a session, swap only when the ladder reaches Serious. The resting phone is out of the headset, case off, at the fan, screen off (BP §5). Charge it only once it is back at Nominal, and unplug it before it goes back into the headset (charging adds heat, R5).

#### Honesty table (on the sign; shown in questions)

| Item | Real | Generated live | Pre-computed | Illustrative |
|---|---|---|---|---|
| Room, walls, object boxes | Scanned in the session, or team pre-scan (said aloud) | | | |
| s, L, tiling size | | Loop-fit from the scan | | |
| Furniture meshes | | Forge, per object, for at most 20 objects (largest first) | Built-in set, labelled "built-in", when the Forge is unreachable or an object has no usable photo; objects beyond the twentieth stay placeholder boxes| |
| Curved world | | Tile graph and world generator, by seed | | |
| Tumour, 10 phases | TCIA 4D-Lung patient 101 contours | Slices by Mesh4D | Tetrahedral worldtube | |
| Mobile nodule (10 mm, 15 mm motion) | | | | "illustrative model" |
| Airway tree | | | | "model-generated" |
| Proximal airway (if shown) | AeroPath (CC BY 4.0), a different patient from the tumour case | | Meshes | |
| Second home and visitor (Shared House) | A team member's room, used with their consent; the visitor is labelled "recorded" or "live" | | Pre-scanned and bundled; the recorded visitor track | A synthetic room, if one is used, is said aloud |
| Performance numbers | Measured on the phone | | Soak chart from an earlier run | |

### 13.3 Narration: one rule per space

| Space | Rule in ten seconds |
|---|---|
| S0 Home | "This is your room, rebuilt from your scan." |
| 3-4-5 walk | "Same steps, same left turns: four corners close on a flat floor, five here." |
| S2 Curved Walk | "Your room is tile zero of a floor with five squares at every corner." |
| S3 Aligned Cells | "The floor stays put; which room you enter depends on the way you came." |
| S4 Small Planet | "Three right-angle turns bring you home." |
| S1 Glued | "Every wall is a door to the opposite wall." |
| S5 Impossible Rooms | "More rooms than the floor has space for." |
| S6 Slicer | "You see one slice of a four-dimensional object." |
| Airway | "Every branch gets a corridor of the same width, because this floor has room for all of them." |
| Worldtube | "One breath, one object: a slice is an instant, the shadow is the volume to treat." |
| Breath lane | "Walk the lane and you walk through the breath." |
| Tilted slice | "A slow scanner cuts space-time at a slant; that is the artefact." |

Closing line: "Every space you walked was built from your room and from a rule you can check with your feet."

### 13.4 Documentation deliverables

| Deliverable | Content | Source |
|---|---|---|
| `README.md` | What ATLAS is; how to run the app, Hub, Spectator and Forge; hardware | Appendix C |
| `docs/CREDITS.md` | 4D-Lung CC BY 3.0 (Hugo et al. 2016 plus the TCIA citation); AeroPath CC BY 4.0 if used; LiteNetLib, TripoSG, HyperEngine and Engine4D MIT (copyright notice kept in every ported file); rembg and its model weights; CC0 kits (Kenney, Quaternius, KayKit); Meshy CC BY 4.0 if any hosted free-tier mesh ships; audio; AI-generated assets labelled; HyperRogue (GPL-2.0) read only, no code copied | Licence ledger |
| `docs/maths.md` | 1 Coordinates and kernel (hyperboloid; tile id + local matrix). 2 Rendering: the log-map vertex warp, why not Klein or Poincaré for the walked view, chart-authored tile meshes, fog and cull. 3 {4,5} tiling and ℤ[φ] ids. 4 Loop-fit and the 3-4-5 proof, including the sphere. 5 Mode 2 as the polyhedral model with the same curvature per tile. 6 Flat but not simple: glued covers and portals. 7 4D slicing and the worldtube. 8 The lens mathematics (FieldFit). Each formula names its unit test and figure | Appendix A |
| `docs/architecture.md` | Two layers, assemblies, message table, Station processes | Sections 6, 7 |
| `docs/metrics.md` | R5 D1 and D2 rows with must, should and measured; one soak chart | `perf_<session>.csv` |
| `docs/evidence/test_report.md` | One line per §11.1 test: what it proves, pass condition, result on the golden commit; linked from the top of the README | CI output |
| `docs/prior_art.md` | Positioning table below | Brief §12 |
| Video | Reel ≤ 3 min in this order: scan → Home → dial → 3-4-5 walk with the corner counter → Mode 1 / Mode 2 → airway fork → worldtube, breath lane, tilted slice → dashboard. One 30–60 s clip per space S0–S6, plus one clip each for the airway, the lung 4D (with the scanner artefact), Post Circling and FieldFit (Spec §14.2) | Spectator capture |
| Deck | Rule per space, architecture, metrics table, honesty table | `docs/` |

Code-comment standard, checked at G15 (audit: `P4-A05`): every public type and method in `Atlas.*` has an XML summary; every formula carries its source tag and units; every shader property states its space and unit; each assembly has a `README.md`; no numeric literals for tunables outside the parameter registry. The one exception is Bump Guard: its thresholds and depth rate are named constants in `Atlas.Safety` and are never in the registry (§4.10).

Claims language (Brief §2.4). Say: "to our knowledge, no prior system combines…"; "education and research visualisation; not for clinical use". Never say: "first"; "intuitive" without pilot data; "generated live" for a built-in asset; "plans your procedure", "treats", "detects" a condition, "rehabilitates", "expands your visual field"; any cut-off that labels a person as impaired. The phrase-check script (`P4-D05`) uses the full Avoid list of Brief §2.4.

| Prior work | What it does | How ATLAS differs |
|---|---|---|
| Holonomy (TU Delft) | Walks {4,5} as flat cells in a fixed 3×3 m area | Cell size from the scanned room; the same tile graph also drives a smooth mode. Its comfort evidence covers Mode 2 only (R1) |
| Hart–Segerman, HyperRogue VR, Hyperbolica | Correct curved rendering; no real room | Scanned room as tile zero; room-derived curvature; the 3-4-5 walk |
| Tea for God | Euclidean overlapping rooms fitted to the play area | Curved and glued spaces; live AI furniture |
| 4D Toys, Miegakure | 4D slicing as play | Breathing worldtube and scanner artefact on real data |

Before submission, lane B reads the two 3DUI 2024 contest entries ("Beyond Euclid … Spherical Geometry", "Space-Bending Interaction…") and records any overlap (Brief §12).

### 13.5 Evaluation we can run

Pilot: n ≥ 5 walkers from outside the team, reached in the rehearsals (`P4-C05`); anonymous ids; no camera frames stored; motion logs are treated as health data and kept encrypted on the Station (Brief §0, Spec §15.5); the dashboard shows only this walker plus aggregates; data deleted on request (Spec §13.1).

What is kept per session: the RoomModel (floor outline, object boxes), furniture meshes, pose and event logs, and FMS scores; no images. A self-scan by a walker from outside the team is not saved as an ARWorldMap, and its RoomModel, cached GLBs and Forge room record are deleted at session end unless the walker agrees to keep them. Delete-by-id (`P4-D02`) removes the Hub logs (`perf.csv`, `state.bin`, `events.jsonl`, `phone.log`), the Forge room, mesh and assets (`DELETE /rooms/{roomId}`, Appendix B.4), and the phone's RoomModel, world map, cached GLBs and copy of the performance CSV.

| Measure | Capture | Must (R5 D2) |
|---|---|---|
| FMS per minute: peak and rise | Console entry | Median peak ≤ 5 |
| SEQ (1–7), once per use case | Console entry (`P2-C17`) | Median ≥ 5 |
| Time to first walk | Event log | ≤ 90 s |
| Pointing back to the start after three legs, Home against Curved Walk | Head ray at button press (`P2-C17`) | Measured in both |
| Sessions ended early | Operator flag | ≤ 1 in 5 |
| Bump Guard warnings per minute; wall contacts | Event log; spotter count | Reported |

May claim: descriptive values with n stated; that a pointing gap between flat and curved shows the geometry is not cosmetic. May not claim: statistical significance, clinical benefit, comfort of Mode 1 beyond our sample, or general usability. One trial per judge is a demonstration, not a measurement (Brief §8.1).

### 13.6 Work packages for Phase 4

| ID | Work package | Lane | Depends on | Output (files, components, interfaces) | Acceptance test (binary, with numbers) | Needs (Editor / phone in hand / phone in headset / Station) |
|---|---|---|---|---|---|---|
| P4-A01 | Look pass per space | A | G12, G13 | Palette and fog presets | In S0–S6, a 360° capture shows, in either eye, 0 surfaces beyond the distance at which the fog is fully opaque and 0 undrawn tiles inside it; draw calls ≤ 300; triangles ≤ 500k | Phone in headset |
| P4-A02 | Audio loops and cues | A | G14 Feature freeze | One loop per space; final mix of the Bump Guard tones (built in `P1-C14`) | 7 of 7 spaces have a loop; cue starts ≤ 1 frame after the event in 20 of 20 trials; allocation ≤ 1 kB per frame | Phone in hand |
| P4-A03 | Performance hardening | A | P4-A01 | `perf_<session>.csv` per phone | 15-minute soak on A and B: p95 ≤ 20 ms, p99 ≤ 33.3 ms, never < 30 fps, no Serious before 10 min | Phone in headset, Station |
| P4-A04 | Golden build and launch check | A | P4-A03, P4-C04 | Tagged build on both phones | Wi-Fi off: 7 of 7 spaces load on both phones; signing valid past the event date; 1 reinstall from the Mac succeeds; Guided Access, orientation lock and Do Not Disturb on, on both phones (Spec §15.5) | Phone in hand |
| P4-A05 | Comment audit | A | G14 Feature freeze | Audit script; assembly READMEs | 100% of public `Atlas.*` types have a summary; 0 compiler warnings | Editor |
| P4-B01 | Maths notes | B | G11 Phase-2 exit | `docs/maths.md` | 8 chapters present; every formula names a passing test | Editor |
| P4-B02 | Correctness report | B | G12, G13, P1-B04, P1-A12 | `docs/evidence/correctness.json` | Lorentz drift < 1e-9; holonomy error < 1°; tesseract slice volume error < 1e-4; tile counts 13, 25, 45, 81 | Editor |
| P4-B03 | Equation-card audit and Spectator mirror | B | P4-B01, P2-C06, P2-A03, P1-D05 | Each of the 7 cards checked against `docs/maths.md` (`docs/evidence/cards.md`); the same card drawn on the Spectator from `Event` data | 7 of 7 cards show a predicted and a measured value that agree with the log to 3 significant figures, on the phone and on the Spectator | Phone in headset, Station |
| P4-B04 | Figure data | B | P4-B02 | Scripts and data in `figures/` | `python figures/make_figures.py` exits with code 0 from a fresh clone and writes fig01–fig06 | Editor |
| P4-B05 | Prior-art note | B | G7 Phase-1 exit | `docs/prior_art.md` | 4 table rows and 2 contest entries each have an overlap note | Editor |
| P4-C01 | Onboarding polish with outside walkers | C | G12, G13 | Revised prompt, pairing, scan and fit screens (built in `P1-C12`) | Local Network prompt precedes the headset step in 5 of 5 fresh installs; median first walk ≤ 90 s, n ≥ 5 | Phone in hand |
| P4-C02 | Comfort protocol | C | P4-D02 | `pg.fmsStop` set from pilot data; the comfort protocol page in `docs/demo/` (the prompt and the alert are built in `P2-C09`) | Prompt every 60 s ± 5 s; a value ≥ `pg.fmsStop` raises the operator alert in 10 of 10 trials | Station |
| P4-C03 | Pre-scan fast-path drill | C | G12, G13 | The `P1-C13` world-map path wired into the demo flow and the runbook | Relocalises within 10 s in 5 of 5 trials | Phone in hand |
| P4-C04 | Failure drills | C | P4-C01 | `docs/demo/runbook.md` | 6 of 6 fallbacks exercised; forced tracking loss recovers in ≤ 15 s in 3 of 3 trials | Phone in headset, Station |
| P4-C05 | Rehearsals | C | P4-C04, P4-D01 | `docs/demo/rehearsal_log.md` | ≥ 5 outside walkers; headset time ≤ 5:00 in every run; overruns ≤ 10 s per segment | Phone in headset, Station |
| P4-C06 | Sign and briefings | C | G14 Feature freeze | Booth sign, spotter briefing (stairs, glass and mirrors, cables, loose objects), honesty table | Sign carries minimum age 13, exclusions, the data line (with the hosted-fallback sentence of §7.4) and "not for clinical use": 4 of 4 | Editor |
| P4-D01 | Judges' dashboard | D | P4-D02 | Console page `/session/<id>` | 5 panels (path, comfort, performance, spaces visited, mathematics) render from log files with the phone disconnected; the mathematics panel shows this session's predicted against measured values (corners 3 / 4 / 5, closure error in m, world turn per lap against 90°) and the test summary from `correctness.json` (tests passed / total, Lorentz drift, holonomy error, tile counts 13 / 25 / 45 / 81); the page shows no other session's id, path or scores, only aggregates | Station |
| P4-D02 | Session log and pilot data | D | G12, G13, P1-D01 | `events.jsonl` per session; delete-by-id; the Hub's `logs/` folder on an encrypted volume | 0 image bytes in any log; delete-by-id removes every file for the id on the Hub, the Forge and the phone; 100% of FMS entries timestamped; `logs/` is on an encrypted volume | Station, phone in hand |
| P4-D03 | Metrics report | D | P4-A03, P4-D02 | `docs/metrics.md`; chart script | Every R5 D1 and D2 "must" row holds a measured value or "unmeasured"; chart regenerates from the CSV | Station |
| P4-D04 | Spectator capture | D | G12, G13, P1-D05 | `media/` clips and reel | 11 clips of 30–60 s (S0–S6, airway, lung 4D, Post Circling, FieldFit); reel ≤ 3 min containing the 3-4-5 counter and the artefact view | Station |
| P4-D05 | Deck and claims check | D | P4-D03 | Deck; phrase-check script | 0 hits for the phrases of the Avoid list of Brief §2.4 (§13.4) in `README.md`, `docs/` and UI strings | Editor |
| P4-D06 | Offline pack | D | P4-D04, P4-D01 | Reel and a saved dashboard on the Station | Both open with the router unplugged | Station |
