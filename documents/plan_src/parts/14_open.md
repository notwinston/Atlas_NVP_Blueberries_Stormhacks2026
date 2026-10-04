---

## 14. Open items

Things only the team can decide or check. None of them blocks the start; each names the gate by which it must be settled.

| # | Item | Why it matters | Settle by |
|---|---|---|---|
| 1 | **Headset model and its lens numbers** (distortion coefficients, lens separation, screen-to-lens distance); check the fit with both phones and with a glasses wearer | The lens blit (`P1-A05`) needs them. Without them straight lines bow | G4 |
| 2 | **Both phones really have LiDAR** (15 Pro Max and 14 Pro Max, not Plus models), and the headset leaves the rear camera and LiDAR unobstructed | Scanning and Bump Guard depend on it | G1 |
| 3 | **iOS version on both phones.** A LiDAR tracking-drift regression was reported for one recent iOS release and reported fixed in a later one (R1; re-read the thread). Run the 20 m loop drift test on the installed version | Drift moves the static map under Bump Guard | G5 |
| 4 | **Unity patch and Xcode**: Unity ≥ 6000.3.21f1, Xcode 26 on the Mac | Thermal frame-rate setting; ARKit plugin | G0 |
| 5 | **How outside walkers are supervised.** Bump Guard is a warning and a haptic by decision. This plan recommends a cleared floor, a confirmed scan and a human spotter whenever someone outside the team wears the headset | Safety of people who do not know the room | G7 |
| 6 | **Comfort stop value.** The comfort score (0–20, spoken each minute) at which a session ends. The older documents offer 4 (Brief §8.1); the performance research offers 10 (R5 D2) | Run-of-show, Console alert | G2 |
| 7 | **Comfort floor for L and R** | Set from the G2 result; provisional values are L ≥ 1.4 m and R ≥ 1.2 m | G2 |
| 8 | **Hosted image-to-3D service** for when the local generator fails: which one, what budget, what licence, what data-retention terms. And the measured time per object on the RTX 5070 | Forge fallback (`P1-D08`); what a walker is told before the scan and what the booth sign's data line says (§7.4) | G6 |
| 9 | **Gamepad model**, and whether its rumble is reachable from Unity on iOS | Second haptic channel for Bump Guard; twins | G5 |
| 10 | **Headset lever**: a single button that produces a screen touch? Are double and triple clicks acceptable? | Twin mapping in the Playground | G8 |
| 11 | **Marker cube**: in scope for the Slicer and the Lung 4D, or gamepad and gaze only? | Extended input in Phase 2 | G8 |
| 12 | **AeroPath licence wording** differs between its two hosting pages (CC BY 4.0 and MIT). Attribute under CC BY 4.0, and confirm before shipping the optional real proximal airway | Credits | G10 |
| 13 | **Shared House "guest rug"** (§10.3): a rug on the second sheet that marks where the walker's own floor is free. Is that acceptable under "no ghost of the room"? Without it only Bump Guard protects the walker on the second sheet. Default until the lead answers: no rug. Sheet 1 is drawn in full, with no marking derived from the real room; `P3-A04` is not built; the door fit still maximises the overlap of the two free floors; and the spotter recommendation of §4.10 applies to every walker on sheet 1 | Phase 3 design | G11 (before `P3-A04` starts) |
| 14 | **A second headset** for a live House Call on phone B | Extended scope of Phase 3 | G12 |
| 15 | **The organisers' rules**: whether code and assets may be prepared before the event and what must be declared as prior work; slot length; one judge or a panel; live demo or video; the space, light and power at the venue; whether our own Wi-Fi router is allowed | Eligibility of everything built under this plan; run-of-show; the Station link needs the router | G0 (prior-work rule); G14 (the rest) |
| 16 | **H200 cluster.** Not assumed anywhere in this plan. If it is available it can run batch jobs, never anything live | Optional | — |
| 17 | **Phase 2 lane split.** Lane A works on both the Playground (4 packages) and the Lung Mission (13 packages), so the Lung Mission has three lanes and not two. Accept, or move `P2-A01`–`P2-A04` to lane C | Balance of the lanes in Phase 2 (§9.6) | G7 |
| 18 | **TCIA data-use terms** acknowledged for 4D-Lung, and the required citation recorded in the licence ledger (Spec §17 Q6) | Credits; submission validity | G9 |
| 19 | **Bump Guard at the lap corners.** The lap square is 0.5 m inside the walls, so a walker faster than 0.5 m/s at a corner gets a WARN there (§4.10). Accept that, or widen the inset to 0.7 m: s = min(W, D) − 1.4 m, so a 3 × 3 m room gives s = 1.6 m and L = 1.51 m, and a corner stays quiet up to 0.75 m/s (derived) | A cue at every corner of the 3-4-5 walk, or a smaller lap and stronger curvature in every room | G5 |

Crosswalk to the Spec's open questions (Spec §17; Brief §13 holds the same list): Q1 is item 15; Q2 is settled by D5; Q3 is item 2 (no Android client, Appendix C.3); Q4 is item 16; Q5 is item 1; Q6 is D25 and item 18; Q7 is settled by D3; Q9 by D9; Q10 by D5 and item 14; Q11 by D25; Q12 by Appendix C.1 (no entitlements; the 1.5 GB memory budget is measured on both phones at G4); Q13 by D4. Q8 (the name) stays ATLAS.

**Assumptions made in this plan**

- The plan is one Markdown document with figures; the six research memos it cites are kept beside it in `documents/research/`.
- Mode 1 (Curved Walk) is the default hyperbolic mode, subject to gate G2. If G2 fails, Aligned Cells becomes the default and Curved Walk a switchable demonstration.
- Phase 2 places the Glued and Impossible Rooms exhibits with lane C, and the whole medical 4D runtime, including the Lung director, labels and credits (`P2-A32`), with lane A, to balance the lanes.
- Work that sits between two lanes goes to the lane that owns the code it touches: room content in Glued to lane A (`P1-A15`) and in Aligned Cells to lane B (`P1-B12`); the warning cue and haptics to lane C (`P1-C14`); the built-in fallback set and the on-device parameter menu to lane D (`P1-D12`, `P1-D13`); the pointing probe and SEQ to lane C (`P2-C17`); the peer relay and the two-plan flat twin of the Shared House to lane D (`P3-D07`, `P3-D08`), so that use case has two lane-D packages beside those of lanes A and C.
- Phase 3 starts after G11 although its packages depend technically only on Phase 1.
- Tile ids use 64-bit integers. Their coefficients are exact for 42 straight tile steps from tile zero and overflow at step 43. The longest route in the plan, the walkable airway, is 32 tile steps (16 generations); addressing all 23 generations (46 steps) along the airway's own paths needs about 52 bits, so 64 bits are enough there too. A wider integer type is a later option.
- Bump Guard's constants are starting values, held in code (§4.10): WARN when the clearance at the head is under 0.30 m, or when the straight path of the next 0.8 s comes within 0.05 m of a hazard; STRONG likewise at 0.20 m and 0.4 s; a cue relaxes only after 0.05 m and 0.3 s of margin; an obstacle seen by live depth is remembered until it is seen empty, or for 30 s without a new hit; live depth is processed at every frame the phone delivers and never below 15 Hz. `BumpGuard_Replay` on a recorded lap confirms or changes them before G5; open item 19 covers the lap corners.
- At most 20 scanned objects receive generated meshes, largest boxes first; further objects stay placeholder boxes. The triangle target per object is clamp(60,000 ÷ n, 3,000, 5,000) for n meshed objects, so the furniture of a room never exceeds 60,000 triangles (§4.5, §8.5).
- The Glued space (S1) is a lattice of open-frame cells (corner posts, a floor grid, one chiral landmark; at most 1,500 triangles each), 5 × 5 for the torus and the Klein bottle and 3 × 3 × 3 for T³. The real room is not repeated: its walls sink to kerbs and every scanned object is drawn once, at its true position, among the copies (§4.9).
- In Curved Walk a tile is drawn when its centre is within ρ + 0.8425 of the walker and its bounding disc on the floor is not outside the view frustum widened by 12° to each side and 4° up and down. At ρ = 3 at most 59 tiles are drawn with a level gaze and 71 at any pitch (§8.5). If the view test is not built, tile centres are culled at ρ = 3 and the fog is made fully opaque at ρ = 2.15.
