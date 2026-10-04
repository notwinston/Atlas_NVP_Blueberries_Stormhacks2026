# ATLAS build graph: areas for people

This file is read by `make_build_graph_pdf.py`. It cuts the task graph of `ATLAS_Master_Plan.md` into pieces of work sized for a person: one piece is about the size of "implement hyperbolic terrain generation".

Format, which the script relies on:

- `## Phase N` starts a phase, `### Lane X` (or `### All hands`) starts a lane.
- `#### <name>` is one piece of work. Name it by what it delivers.
- The first paragraph under it, starting with `Delivers:`, is the one-line description.
- The bullet list holds the plan's tasks inside the piece, one per line: task id, then the task title as written in the plan.

The script takes everything else from the plan: which piece starts after which, what a piece needs from another lane, what it hands over and which gate it feeds. A plan task that is listed nowhere here is placed with its nearest neighbours in the same lane and reported when the script runs, so this file should be edited whenever tasks are added, renamed or renumbered in the plan.

## Phase 1

### All hands

#### Set up machines, the project and the frozen contracts

Delivers: every machine and both phones ready, the Station and the network up, the Unity project and repository skeleton, and every contract between the lanes written and stubbed.

- P1-X01 Build seat, signing and Unity (lane A leads, everyone takes part)
- P1-X03 Station ready (lane D)
- P1-X04 Phones and network ready (lane C)
- P1-X05 Contract pages, lane A
- P1-X06 Contract pages, lane B
- P1-X07 Contract pages, lane C
- P1-X08 Contract pages, lane D
- P1-A01 Unity project and repository skeleton
- P1-X02 Contracts frozen (this is gate G0)

### Lane A

#### Get the app running in stereo on both phones

Delivers: the first build on both phones, URP settings and quality tiers, the two-camera stereo rig, the lens pre-distortion blit, and a one-command build.

- P1-A02 First build on both phones
- P1-A03 URP asset and quality tiers
- P1-A04 Two-camera stereo rig
- P1-A05 Lens pre-distortion as the single final blit
- P1-A13 One-command build and the build-seat rules

#### Build the shared look shader and the SpaceLab host

Delivers: the one shader and palette that every space uses, and the boot scene that loads one space at a time and runs the frame loop.

- P1-A06 Shared look shader
- P1-A08 SpaceLab host

#### Implement the flat and 4D spaces

Delivers: the Glued space (S1) with the scanned room in it, the stencil portal engine and Impossible Rooms (S5), and the Slicer (S6) with Mesh4D.

- P1-A09 Glued space S1
- P1-A10 Stencil portal engine
- P1-A11 Impossible Rooms S5
- P1-A12 Slicer S6 and Mesh4D
- P1-A15 Room content in Glued, and standing mode

#### Hold the frame rate: thermal ladder, performance runs and soak

Delivers: the thermal quality ladder, a performance run for every space on the budget fixture, and the soak.

- P1-A07 Thermal quality ladder
- P1-A14 Per-space performance runs and the soak

### Lane B

#### Implement the geometry kernel and the curved-walk rig

Delivers: the double-precision kernel for hyperbolic, flat and spherical space, and the holonomy rig that turns head movement into movement through curved space.

- P1-B01 Geometry kernel
- P1-B02 Holonomy rig

#### Implement hyperbolic terrain generation

Delivers: the warp shader, exact tile ids and the tile graph, procedural tile meshes, the tile renderer, and the world generator with WorldLint.

- P1-B03 Warp shader include
- P1-B04 Exact tile ids and the tile graph
- P1-B05 Procedural tile meshes
- P1-B06 Tile renderer
- P1-B07 World generator and WorldLint

#### Implement Small Planet and Aligned Cells

Delivers: the spherical space (S4) and the flat-cell mode of the hyperbolic world (S3, Mode 2), with the scanned room in it.

- P1-B08 Small Planet S4
- P1-B10 Aligned Cells S3 (Mode 2)
- P1-B12 Room content in Aligned Cells

#### Put the scanned room into the curved world

Delivers: the curvature dial, the 3-4-5 walk, and the scanned room as tile zero of the curved world.

- P1-B09 Curvature dial and the 3-4-5 walk
- P1-B11 The room becomes tile zero

### Lane C

#### Bring up AR tracking, input and the pose pipeline

Delivers: the AR session with Editor simulation and replay, gamepad and gaze input with twins, and the pose pipeline.

- P1-C01 AR session and Editor simulation
- P1-C10 Input and twins
- P1-C11 Pose pipeline

#### Implement room scanning and the RoomModel

Delivers: the FloorScan core, the manual and automatic scan routes, object boxes, the RoomPlan wall bridge (Extended), and the RoomModel with the loop-fit.

- P1-C15 FloorScan and loop-fit core in the Editor
- P1-C02 Scan route 1: manual outline
- P1-C03 Scan route 2: automatic FloorScan on the phone
- P1-C04 Object boxes
- P1-C05 Swift RoomPlan bridge for walls, doors and windows (Extended)
- P1-C06 RoomModel and the loop-fit

#### Rebuild the room as Home and add Bump Guard

Delivers: the Home rebuild (S0), object photo capture for the Forge, the warning cue and haptics, and Bump Guard.

- P1-C07 Home rebuild S0
- P1-C08 Object photo capture
- P1-C14 Warning cue and haptics
- P1-C09 Bump Guard

#### Build onboarding and the pre-scan path

Delivers: onboarding with the phone in the hand, and a saved world map that relocalises in a room scanned earlier.

- P1-C12 Onboarding in hand
- P1-C13 Pre-scan path: world map save and relocalise

### Lane D

#### Build the phone-to-Station link

Delivers: the Atlas.Link library, the Hub, and pairing and network bring-up.

- P1-D01 Atlas.Link library
- P1-D02 Hub
- P1-D11 Pairing and network bring-up

#### Build live tuning, the operator Console and telemetry

Delivers: the parameter registry and Console tuner, the operator Console, the on-device menu, and the perf probe, HUD and telemetry.

- P1-D03 Parameter registry and Console tuner
- P1-D04 Perf probe, HUD and telemetry
- P1-D06 Operator Console
- P1-D13 On-device parameter menu

#### Build the Spectator mirror and the flat-twin map

Delivers: the Spectator build that re-renders the walker's view on the Station, and the flat-twin mini-map.

- P1-D05 Spectator
- P1-D07 Flat-twin mini-map

#### Build the Forge: object photos to furniture meshes

Delivers: the Forge service on the Station, mesh clean-up, the asset client on the phone, and the built-in fallback set.

- P1-D08 Forge service
- P1-D09 Mesh clean-up
- P1-D10 Asset client on the phone
- P1-D12 Built-in fallback set

## Phase 2

### Lane A

#### Build the Slicer toybox exhibit

Delivers: the Slicer toybox with section names and shadow.

- P2-A01 Slicer toybox
- P2-A02 Section names and shadow

#### Wire the equation sources and finish the Playground look

Delivers: the live numbers behind the equation cards of the exhibits, and the look polish of the Playground.

- P2-A03 Exhibit equation sources
- P2-A04 Look polish

#### Implement the breathing-tumour 4D views

Delivers: the 4D tube loaded into Mesh4D, live slices with phase colouring, shells and the look-down shadow, the breathing chest, and the ring fallback.

- P2-A20 `TubeLoader`
- P2-A21 Live slicer views
- P2-A22 Phase colouring
- P2-A23 Shells and look-down shadow
- P2-A24 Breathing chest
- P2-A30 Ring fallback

#### Build the breath lane, gating and scanner tilt

Delivers: the breath lane with the unroll animation and "now" plane, the gating flags, and the scanner tilt.

- P2-A25 Breath lane
- P2-A26 Unroll animation and "now" plane
- P2-A27 Gating flags
- P2-A28 Scanner tilt

#### Build the Lung tasks and director, and pass the 4D soak

Delivers: tasks M2, M3 and M4 with scoring and events, the Lung director with labels and credits, and the Lung 4D soak report.

- P2-A29 Tasks M2, M3, M4
- P2-A32 Lung director, labels and credits
- P2-A31 Lung 4D soak

### Lane B

#### Generate the airway wing in hyperbolic space

Delivers: the airway tree mapped onto the tile graph, and the corridor and chamber kit.

- P2-B01 `AirwayTree`
- P2-B02 Corridor and chamber kit

#### Implement walking the airway: valve chambers and mode switch

Delivers: the valve solver for Mode 1, the valve walk for Mode 2, the mode switch, and the lesion chamber hand-over.

- P2-B03 `ValveSolver`, Mode 1
- P2-B04 Valve walk, Mode 2
- P2-B05 Mode switch
- P2-B09 Lesion chamber hand-over

#### Build airway navigation: beacon, endoluminal window and Two Compasses

Delivers: the beacon and route cost, the endoluminal window, Two Compasses, and the airway run test.

- P2-B06 Beacon and route cost
- P2-B07 Endoluminal window
- P2-B08 Two Compasses
- P2-B12 Airway run test

#### Build the needle sky

Delivers: the needle sky with its phase and robust layers, and the great-circle check.

- P2-B10 Needle sky
- P2-B11 Great-circle check

### Lane C

#### Build the Geometry Playground shell and guided walk

Delivers: the director and beats, scan-to-Home, the centre mark and dial hand-off, the guided 3-4-5 walk, the mode compare, tutorial prompts with the comfort question, and the pointing probe.

- P2-C01 Director and beats
- P2-C02 Scan-to-Home
- P2-C03 Centre mark and dial hand-off
- P2-C04 Guided 3-4-5 walk
- P2-C05 Mode compare
- P2-C09 Tutorial prompts and FMS
- P2-C17 Pointing probe and SEQ

#### Build captions, equation cards, action twins and the exhibit menu

Delivers: caption chips and equation cards, the action twins, and the exhibit menu with return to Home.

- P2-C06 Caption chips and equation cards
- P2-C07 Action twins
- P2-C08 Exhibit menu and return Home

#### Build the Glued and Impossible Rooms exhibits

Delivers: the Glued exhibit with the chiral kit and the gluing switch, and the Impossible Rooms exhibit with the loop corridor.

- P2-C12 Glued exhibit: wrap-walk
- P2-C13 Chiral kit
- P2-C14 Gluing switch and T³
- P2-C15 Impossible Rooms exhibit
- P2-C16 Loop corridor and room numbers

#### Check Bump Guard in every space and run the Playground

Delivers: the Bump Guard check repeated in every space, and the complete Playground run.

- P2-C10 Bump Guard check in every space
- P2-C11 Playground run (G8)

### Lane D

#### Define the case contract and the illustrative case

Delivers: the case contract with its readers, and the illustrative case package that lanes A and B build against.

- P2-D01 Case contract
- P2-D02 `atlas case twin`

#### Build the real-case pipeline

Delivers: the `atlas case` steps from fetch and masks to the 4D tube, shells, chest, airway and sky, packed and delivered to the phone.

- P2-D03 `atlas case fetch`, `masks`
- P2-D04 `atlas case tube4d`
- P2-D05 `atlas case shells`
- P2-D06 `atlas case chest`
- P2-D07 `atlas case airway`
- P2-D08 `atlas case sky`
- P2-D09 `atlas case pack` and delivery

#### Build the Lung panels, plan card and event logging

Delivers: the Lung Console panel, the Lung twins on Spectator and Console, the plan card, and event logging with the pilot export.

- P2-D10 Lung Console panel
- P2-D11 Lung twins on Spectator and Console
- P2-D12 Plan card
- P2-D13 Event logging and pilot export

## Phase 3

### Lane A

#### Implement the two-sheet house: sheets, door and flat-twin data

Delivers: the sheet state machine, the two-sheet door rendering, and the winding counter with the flat-twin data.

- P3-A01 Sheet state machine
- P3-A02 Two-sheet door rendering
- P3-A05 Winding counter and flat-twin data

#### Load the second home and the visitor

Delivers: the second home loader, the visitor avatar with its recorded track, the phone performance pass, and the guest rug if the lead approves it.

- P3-A03 Second home loader
- P3-A04 (Blocked on open item 13; do not start) Guest rug and unlit floor
- P3-A06 Visitor avatar, recorded track, labels
- P3-A07 Phone performance pass

### Lane B

#### Implement the FieldFit lens

Delivers: the lens mathematics, the lookup builder, the wide source render with the composed final blit, the field-loss mask and slider, the lens guard, and the phone performance pass.

- P3-B01 Lens maths library
- P3-B02 Lookup builder
- P3-B03 Wide source render and composed final blit
- P3-B04 Field-loss mask and slider
- P3-B05 Lens guard
- P3-B08 Phone performance pass

#### Build the Blind-Spot Finder

Delivers: the Blind-Spot Finder, and Scotoma River (Extended).

- P3-B06 Blind-Spot Finder
- P3-B07 Scotoma River (Extended)

### Lane C

#### Scan the second room and fit the door

Delivers: the second scan path, and door placement with the door fit.

- P3-C01 Second scan path
- P3-C02 Door placement and door fit

#### Make the Shared House safe and shared, and walk it

Delivers: Bump Guard on the second sheet, the peer link (Extended), and the G12 walk-through.

- P3-C03 Bump Guard on sheet 1
- P3-C04 Peer link (Extended)
- P3-C05 G12 walk-through

### Lane D

#### Relay the peer and show the two-plan flat twin

Delivers: the peer relay (Extended) and the two floor plans with the winding dial on the Station.

- P3-D07 Peer relay (Extended)
- P3-D08 Two-plan flat twin

#### Build the FieldFit presets and lens panel

Delivers: the field presets, the Console lens panel, and the Teichmüller lookup (Stretch).

- P3-D01 Field presets
- P3-D02 Console lens panel
- P3-D05 Teichmüller lookup (Stretch)

#### Build the heatmap and the search task, and run the FieldFit measurement

Delivers: the Spectator heatmap and bound, the search task with logging, and the G13 measurement run.

- P3-D03 Spectator heatmap and bound
- P3-D04 Search task and logging
- P3-D06 G13 measurement run

## Phase 4

### Lane A

#### Finish the look of every space and harden performance

Delivers: the look pass for every space and the soak on both phones, which is what the feature freeze waits for.

- P4-A01 Look pass per space
- P4-A03 Performance hardening

#### Add audio, audit the comments and ship the golden build

Delivers: the audio loops and cues, the comment audit, and the golden build with its launch check.

- P4-A02 Audio loops and cues
- P4-A05 Comment audit
- P4-A04 Golden build and launch check

### Lane B

#### Prove and document the mathematics

Delivers: the maths notes, the correctness report, the equation-card audit, the figure data, and the prior-art note.

- P4-B01 Maths notes
- P4-B02 Correctness report
- P4-B03 Equation-card audit and Spectator mirror
- P4-B04 Figure data
- P4-B05 Prior-art note

### Lane C

#### Polish onboarding and the pre-scan fast path

Delivers: onboarding polished with outside walkers, and the pre-scan fast-path drill.

- P4-C01 Onboarding polish with outside walkers
- P4-C03 Pre-scan fast-path drill

#### Run the failure drills and the rehearsals

Delivers: the failure drills and the rehearsals with outside walkers.

- P4-C04 Failure drills
- P4-C05 Rehearsals

#### Write the comfort protocol, the sign and the briefings

Delivers: the comfort protocol, the booth sign and the spotter briefing.

- P4-C02 Comfort protocol
- P4-C06 Sign and briefings

### Lane D

#### Build the judges' dashboard and the session logs

Delivers: the judges' dashboard, and the session log with the pilot data.

- P4-D01 Judges' dashboard
- P4-D02 Session log and pilot data

#### Produce the metrics report, the video, the deck and the offline pack

Delivers: the metrics report, the Spectator capture, the deck with its claims check, and the offline pack.

- P4-D03 Metrics report
- P4-D04 Spectator capture
- P4-D05 Deck and claims check
- P4-D06 Offline pack
