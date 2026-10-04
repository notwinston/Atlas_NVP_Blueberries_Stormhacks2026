# ATLAS: Master Build Plan

**One plan, end to end: what we build, in what order, who builds which part, and how we know each part works.**

| | |
|---|---|
| Date | 3 October 2026 |
| Project | ATLAS, our entry for Huawei Custom Challenge #1, "Beyond Euclid: Interactive Experiences in Impossible Geometries" |
| Team | Four engineers, all working in Unity and C# |
| Hardware | iPhone 15 Pro Max (phone A) and iPhone 14 Pro Max (phone B) in a lens headset with the rear camera and LiDAR exposed · one MacBook Pro M3 Max (the only iOS build seat) · one Windows PC with an RTX 5070 (the Station) |
| This document | `documents/ATLAS_Master_Plan.md`, with figures in `documents/figures/` |
| Older documents | `ATLAS_Build_Pipeline_assets.md` (technology, still binding), `ATLAS_Engineering_Spec.md` and `ATLAS_Implementation_Brief.md` (background and detail; superseded where this plan differs, see Appendix D) |

---

## 0. Read me first

### 0.1 What this plan is

This is the single source of truth for building ATLAS. It merges the Engineering Spec, the Implementation Brief and the Build Pipeline, and it applies the decisions the team lead took on 3 October:

- The fixed, taped 3 × 3 m square is gone. The walker **scans their own room** and **walks freely** in it.
- The scanned room is rebuilt and becomes **tile zero** of a curved world.
- The build runs in **phases**. Phase 1 sets up all the technology, generates every non-Euclidean and 4D space, and connects the phone to the PC. The use cases follow.
- The work is split into **four parallel lanes**, one per engineer.
- There is **no calendar** in this plan. Work is ordered by dependency and released by gates. Tasks are written so they can be handed to an AI coding agent: each has inputs, outputs and a pass/fail test.

### 0.2 Order of authority

When two sources disagree, the higher one wins:

1. The team lead's decisions (recorded in §2 and used throughout this plan).
2. The Build Pipeline document (technology and implementation choices).
3. The Engineering Spec.
4. The Implementation Brief.

A few Build Pipeline lines are themselves overridden by newer decisions or by verified research (for example the taped square, and fog distance). Appendix D lists every such case.

### 0.3 How to read it

| If you want to… | Read |
|---|---|
| Understand the project in five minutes | §1 |
| See every decision and what it replaced | §2, Appendix D |
| Know what spaces exist and how the room becomes one | §3, §4 |
| Understand the 4D and why the medical use case needs it | §5 |
| Start building | §6 (architecture), §9 (build plan), Appendix C (set-up) |
| Work on the phone-to-PC connection | §7, Appendix B |
| Hit the frame rate and make it look good | §8 |
| Know when something is done | §11 |
| Build or check a use case, the demo, the risks, the open questions | §10, §13, §12, §14 |
| Look up a term | Appendix E |

### 0.4 Conventions

- **Must / should / may** have their usual engineering meaning.
- Source tags are short: (BP §4) is the Build Pipeline; (Spec §6.3) and (Brief §3.9) are the older documents; (R1) to (R6) are the research memos written for this plan (free-walk geometry, medical 4D, tile graphs, phone-to-PC link, performance, world generation); (derived) is our own mathematics. Every derived number in this plan was checked numerically; the scripts named in Appendix A recompute the geometry and worldtube numbers.
- **L** is the hyperbolic curvature radius in metres (the Spec calls it R). **R** in this plan is the sphere radius of the Small Planet. **ρ** is hyperbolic distance in units of L. **q** is the number of tiles that meet at a corner.
- Task ids look like `P1-B04`: phase, lane, number. Gates are `G0` to `G15`.

### 0.5 Ten rules

Breaking any of these is a defect.

1. **Safety runs on the phone and never touches the network.** The safety assembly cannot reference the link assembly.
2. **60 frames per second in stereo**, on both phones. Quality drops in steps before frame rate does. Never below 30.
3. **One metre walked is one metre moved**, in every space. No translation or rotation gains. The single exception is the airway's valve chamber (§10.2.3), where the world is re-aimed while the walker stands inside a closed, opaque chamber.
4. **Curvature changes only while the walker stands still.**
5. **Every action has a twin.** Each action works with the gamepad alone and with gaze dwell alone. The marker cube and any later hand input are optional extras: hand tracking is not in the Core build and no task depends on it; if the optional HoloKit spike (Appendix C.3) is ever merged, hand tracking runs only in a space that uses it (BP §0.5: it adds heat).
6. **Every curved view has a flat twin**: a map, a list or a chart of the same thing.
7. **Positions are a tile id plus local coordinates.** Never a global floating-point coordinate.
8. **One shader, one palette, fixed budgets**, checked at every gate.
9. **No clinical claims.** Every medical object on screen is labelled real, illustrative, or model-generated.
10. **No camera frames are stored.** Object photos are crops, held in memory, and deleted when the mesh is made. The one exception is the hosted fallback, which is disclosed (§7.4).

---

## 1. ATLAS on one page

### 1.1 The challenge

Build an interactive application in which non-Euclidean geometry is central and meaningful. The best entries "make the user feel it, interact with it, and discover something new about space itself".

| Criterion | Weight | What the judges look for |
|---|---|---|
| Geometric creativity | 30 % | Original concept; geometry that is not cosmetic; correct mathematics |
| User experience and interaction | 30 % | Intuitive navigation and feedback; purposeful, not disorienting for its own sake |
| Graphics and technical execution | 25 % | Clarity and polish; smooth (30+ fps); code quality; efficient geometric computation |
| Documentation and presentation | 15 % | Clear maths and technical explanation; commented code; a compelling demo |
| Bonus | | Beyond gaming; educational value; exceptional polish; a novel technical approach |

The challenge names four families of geometry: hyperbolic, spherical, 4D projections, and impossible or Escher-like spaces. ATLAS covers all four.

### 1.2 The concept

**Your own room becomes the first tile of other universes, and you walk through them on your own feet.**

- You scan the room with the phone. ATLAS measures the free floor and rebuilds the room around you, with your furniture regenerated as 3D models while you watch.
- The size of your walkable floor sets the curvature. A dial then asks one question: *how many copies of your room meet at each corner?* Three gives a small planet. Four is ordinary flat space. Five or six gives hyperbolic space with endless rooms.
- You feel the difference with your legs. Walk a square with four left turns: in flat space you are home. On the planet you were home after three corners. In hyperbolic space you need a fifth.
- The same engine runs glued rooms (a torus, a Klein bottle), rooms that are bigger inside than outside, and a slicer for four-dimensional objects.
- Then the geometry is put to work. In the **Lung Mission**, the airway tree is walkable because hyperbolic space has room for every branch, and a tumour that moves with breathing becomes a four-dimensional object you can slice, project and tilt.

### 1.3 What a first-time walker experiences

1. **Scan** (phone in hand). Sweep the room. The free floor lights up; confirm the outline.
2. **Home**. Headset on. The room is rebuilt 1:1 in a clean diagram style. Boxes stand where the furniture is; largest first, each box turns into a generated model of that object as the Station finishes it (at most 20 per room).
3. **The dial**. Stand on the marked spot and turn the dial from 4 to 5. The walls sink to kerbs, corner posts stay, and beyond them an endless tiling of rooms appears.
4. **The 3-4-5 walk**. Follow the glowing square: walk, turn left, repeat. A counter shows the corners. It takes five to come back. Turn the dial to 3 and the same square closes after three.
5. **Two ways to be hyperbolic**. In *Curved Walk* everything curves smoothly and the world slowly turns as you lap the room. Switch to *Aligned Cells*: the floor is cut into flat cells that stay locked to the real walls, and each lap round a corner post leaves you in a different room on the same piece of floor.
6. **Exhibits**. Glued rooms (see your own back; meet your mirror image), impossible rooms (a booth that contains a hall), the small planet, and the 4D toybox (push a tesseract through your room).
7. **Lung Mission** (Phase 2). Walk the airway tree, discover that the map you navigated by was one frozen slice of a breathing, four-dimensional lung, choose where and when to place a needle, and leave with a plan card.

A teammate watches all of this on the Station: a live mirror of the walker's view, a flat map of where they are in the tiling, sliders that tune the world while it runs, and a performance dashboard.

### 1.4 How this answers the rubric

| Criterion | Our answer | Evidence we can show |
|---|---|---|
| Geometric creativity | The scanned room as tile zero; curvature derived from the room; smooth and corner-concentrated curvature on one tile graph; four geometry families on one kernel | The 3-4-5 walk closing to within 25 cm; unit tests of the mathematics (holonomy equals angle defect; tile counts match the known growth sequence); maths notes |
| User experience | Real walking with no gains; one rule and one "aha" per space; twins for every action; a flat map for every curved view; warning and haptic before any bump | Comfort scores per minute; pointing-home error; ease ratings; onboarding time |
| Graphics and technical execution | A consistent "lit architectural diagram" look; 60 fps stereo on a phone; exact integer tile ids; live AI-generated furniture | The metrics table (§11.2) and a 15-minute soak chart from the telemetry log |
| Documentation and presentation | This plan, maths notes with figures, commented code, a Spectator view that makes the demo watchable | §13 |
| Bonus | Medical education use; teaches geometry by walking it; to our knowledge a new combination (curvature fitted to the scanned room; smooth and cell-based hyperbolic walking on one tile graph) | Lung Mission; the 3-4-5 walk |

### 1.5 What exists at the end of each phase

| Phase | Ends at gate | What exists |
|---|---|---|
| **P1 Foundation** | G7 | The whole toolchain. Every space S0–S6 generated and rendered on the phone at budget, with the scanned room's walls and furniture as content: in Home, as tile zero, among the Glued copies and in Aligned Cells. The curvature dial and the 3-4-5 walk. Scan → RoomModel → Home with live AI furniture, and a built-in fallback set for when the Forge is unreachable. Bump Guard with its warning cue and haptics. The Station link with all four functions: Spectator mirror, live tuning and Console, telemetry dashboard, scan and asset round-trip. An on-device parameter menu for when the Station is unreachable |
| **P2 Use cases I** | G11 | Geometry Playground (the guided experience of §1.3, steps 1–6) and Lung Mission (step 7), built in parallel |
| **P3 Use cases II** | G12, G13 | Shared House / Post Circling and FieldFit |
| **P4 Finish** | G15 | Polish, evaluation with outside walkers, demo operations, documentation, video, golden build |
