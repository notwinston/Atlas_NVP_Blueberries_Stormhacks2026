# End-to-end rehearsals

Two rehearsals that need no Unity and no phone. Together they show that what the four lanes built fits
together: the frame of the phone from the head pose to the `State` on the wire, and the Station from the wire to
its logs and its meshes. Neither renders a pixel. What only Unity or a phone can show is listed at the end.
A third command runs both, with everything else that can be checked headless, and prints the Phase 1 gates.

| Rehearsal | Command | Time |
|---|---|---|
| The phone's frame, headless | `python3 tools/headless/verify.py test Atlas.E2E.Tests --tag e2e` | about 5 s after the build |
| The Station, for real | `bash tools/e2e/run_station_rehearsal.sh` | about 45 s |
| The gates G0 to G7 | `python3 tools/e2e/gates.py` | about ten minutes |

## 1. The phone's frame (`ATLAS/Assets/Atlas/Tests/App/E2E`)

NUnit tests in an assembly of their own, `Atlas.E2E.Tests`. It is separate from `Atlas.App.Tests` because it
references both `Atlas.App` and the four `Atlas.Spaces.*` assemblies, which no other assembly may do (contract
README F1). The tests run in the Unity Test Runner and under `tools/headless/verify.py` alike.

`RehearsalRig` puts the phone together in the order of the composition root (`AtlasApp.Build`) and steps it
like `AtlasFrameLoop` does: `RunUpdate` (steps 1 to 6), `RunState` (8a), `RunAfterFrame` (8b), 60 times a second
on a manual clock. Behind it stands a Station on the in-memory network.

| Part | What runs |
|---|---|
| Frame order | the real `FrameLoopCore` |
| Head pose | the real `PoseLogReplaySource` playing `recordings/walk_*.poselog`, one sample per frame |
| Step 1 and 3 | `RehearsalPosePipeline`: the order of `Atlas.Scan.PosePipeline` without its transforms, over the real `RoomFrame` and `PoseFilter` |
| Depth | the real `SyntheticDepthSource` (the Editor's ray-cast image, 64 x 48 at 15 Hz) against the fixture's walls and objects |
| Step 2 | the real `BumpGuard` and `BumpGuardStep`, with the fixture's distance field as its map, armed as after "headset on" |
| Step 4 | the real `HolonomyWalkerKernel`, the app's one walker kernel: flat with the room's lap side as SpaceLab configures it, {4,5} or {4,6} while Curved Walk is entered, the sphere (q = 3) in Small Planet |
| Step 5 and 6 | the real `SpaceLab`; its modules are the spaces' own cores (`HomeSpaceCore`; `GluedLattice`, `GluedRules`, `GluedTierFollower`; `PortalSpaceCore`, `PortalMath`, `PortalSwapRule`; `SlicerParams`, `SlicerLayout`, `SlicerController`; `CurvedWalkCore`; `AlignedCellsCore`; `SmallPlanetCore`) behind `ISpaceModule` |
| Room | the real `RoomHandOver` with each fixture of `RoomFixtures.All` |
| Parameters and commands | the real `ParamRegistry`, `AppParams`, `QualityLadder` and `CommandRouter` |
| Step 8 | the real `LinkPump` and `EventLog` on a real `LinkClient` |
| Station | a real `LinkHost` (the Hub's link) and a Spectator `LinkClient` behind it, on `LoopbackNetwork` |

What each test proves:

| Test | Proves |
|---|---|
| `E2E_Flow_EveryFixture_…` (five fixtures) | Hand-over to Home in every room: room frame on, walker placed under the head, Bump Guard holds the map, the Station holds the outline. A lap of the room's own lap square stays inside the free rectangle and the walkable floor, for the head and for the kernel's walker. Each flat space is entered from a Station command through black, with a new world version and `space.id` written back. A space no build has is refused cleanly. Every frame's `State` decodes at the Hub and at the Spectator to the pose, space and flags the frame loop filled |
| `E2E_CurvedWalk_TheFiveCornerLap…` | Curved Walk through the real frame loop on `sample_3x3`: the kernel goes to {4,5}, the curvature ramps while the head stands, the recorded five-sided lap leaves tile zero and closes after five corners under 0.25 m (gate G2, headless half). Every frame's `State.tile` and `xform` are the kernel's pose, `p[0]` is L, `p[3]` the dial and `p[4]` the curvature. The `world` event carries the generator's hash. Home puts the kernel back to flat with the walker under the head |
| `E2E_CurvedSpaces_HomeToS2ToS3ToS4ToHome_…` | All seven spaces are available and next-space walks them in order. Home, S2, a walk into another tile, S3 with the same tile id (Mode 1 to Mode 2, sections 4.8 and 4.9) and a flat kernel, S4 with the kernel on the sphere at the room's R, then Home flat and re-registered. `State` decodes at the Hub and the Spectator in every frame |
| `E2E_Lap_…` | The recorded lap: path length 4 x the lap side in the geometry stream, a closed loop, no jump. Bump Guard gets a depth image at 15 Hz, never reaches STRONG, and rises to WARN only within 0.5 m of a corner |
| `E2E_WallApproach_BumpGuardWarns…` | The recorded approach at 1 m/s: the cue rises 0.83 s before predicted contact (must: 0.6 s), the Station sees the level in the same frame, the cue is asked for and logged |
| `E2E_WallApproach_StationCablePulled_…` | The same walk with the Station up, with its network cut after half a second, and with no Station at all: Bump Guard's level changes in the same frames (Master Plan rule 1; L14 in logic, not in milliseconds) |
| `E2E_WallApproach_InTheCurvedSpaces_…` | The same approach in Home, in Curved Walk at full curvature, in Aligned Cells and on the Small Planet: Bump Guard's level changes in the same frames to the same levels, and the Station sees each in its frame (Master Plan rule 1: the guard works in the room frame, before the kernel) |
| `E2E_JumpAndLoss_…` | The injected 0.5 m jump reaches Bump Guard's input in full and is absent from the geometry stream: zero `FloorDelta`, the kernel does not move, `State.flags` bit 2 is set at the Hub for that one frame. One second of limited tracking: no geometry step, `tracking` on the wire, TRACKING LOST after half a second. The operator's recentre brings the walker back under the head within 250 ms |
| `E2E_Glued_…` | On the recorded lap the cell in `State.tile` changes in exactly the frames in which the head passes a cell edge of the lattice: four times, to a neighbour each time. `glued.mode` from the Station reaches the `State` within 250 ms and raises the world version |
| `E2E_Portal_…` | The room graph of a 3 x 3 m room is connected, twinned and inside the walkable floor. Walking through a doorway and back changes `State.sheet` in the frame the head passes the door plane, never earlier; the walker's room is never switched off, and no room is switched off under the walker |
| `E2E_Slicer_…` | Walking the lane moves the cut: `State.p[3]` equals the layout's cut for the head's z from the frame the lane takes over. A slider value from the Station is in the `State` within 250 ms and holds |
| `E2E_OperatorCommands_…` | Switch (with a variant), Home, recentre, quality, STOP and lock, each sent by the Hub, each in effect within 250 ms (the tier within a second). STOP shows the cue at STRONG without changing Bump Guard's level or the world. Under lock a slider is ignored; without it an out-of-range value is clamped by the phone |
| `E2E_SteadyState_ThePhonesFrameAllocatesNothing` (seven spaces) | 100 consecutive frames of the whole chain, link included, allocate 0 managed bytes |
| `E2E_Phase1_OneSession_…` | Phase 1 in one session on `sample_3x3`, with a Spectator behind the Hub (`RehearsalSpectator`: the real `SpectatorCore`, `SpectatorHost` and `SpectatorLoop` on their own kernel and their own copy of the room). The room's loop-fit (s = 2.0 m, L = 1.885 m); Home; S2 with the room as tile zero and the walker over the head's floor point; the recorded five-sided lap closes after five corners; the dial to q = 4 ramps the curvature to zero and four sides close flat; back to q = 5 and the walk to the real wall crosses into another tile with Bump Guard's cue 0.83 s before contact; S3 keeps that tile id, and Bump Guard stays at its level through the fade; the cue again in S3; S4 at the room's R, three sides close on the sphere, the cue again; Home. In every frame: the curvature does not change while the head moves faster than 0.1 m/s; the Spectator's `State` is the phone's; the Station's trail holds the phone's tile sequence; the Station's kernel has the phone's tiling, tile and frame in S2 and S4, and its cell in S3; the Station generated the phone's world (same content hash) and never showed "world mismatch" |
| `E2E_WorldFrame_…` | A room whose anchor is neither at the session's origin nor level with it nor aligned with it: after the hand-over the head over the anchor is at (0, eye height, 0), the floor is y = 0 along a walk, the kernel's walker and Bump Guard's map are in that frame, and S2 registers tile zero there (kernel.md section 9.1) |
| `G3_CurvedSpaces_Load…` (three sample rooms) | S2, S3 and S4 load in SpaceLab on each sample room, configure the one kernel from the room's loop-fit ({4,5} at L, flat, the sphere at R) and expose their parameters; Home is flat again |
| `G3_Mode1ToMode2_…` (three sample rooms) | The walker leaves tile zero through one edge in Mode 1; Mode 2 is entered with that tile id, on the {4,5} graph, and it is the tile in `State` |
| `G5_StandingRoom_…` | In the 1.4 x 1.4 m room the three curved spaces load, offer no lap, and Bump Guard stays armed with the room's map |

What it cannot prove, because these parts are stand-ins written in this folder:

- `RehearsalPosePipeline` is not `PosePipeline`. The XR Origin move, the tracked camera, the ARAnchor and the
  session-frame branch (a scanned room on a phone) are not exercised: every walk here is already in the room frame.
- `HomeModule`, `GluedModule`, `PortalModule`, `SlicerModule`, `CurvedWalkModule`, `AlignedCellsModule` and
  `SmallPlanetModule`, and the Station's `StationModule`, are not the MonoBehaviour adapters. They call
  the same cores in the same order, but the meshes, materials, renderers, the Burst jobs of the Slicer, the
  stencil rooms of `PortalRenderer` and the input of the spaces are not run.
- No eye camera exists, so the portal hold rule is only checked for "never under the walker", not for what an
  eye sees through a doorway.
- The quality ladder has no performance figures here (no probe), so only pinned tiers are exercised.

## 2. The Station (`tools/e2e/run_station_rehearsal.sh`)

Starts the real programs on free ports of the loopback interface, in a work folder under the temporary
directory, and stops them again. `KEEP=1` keeps the work folder (logs and downloaded GLBs); a failed run keeps
it too. The exit code is 0 only when every check passed.

1. Builds `station/hub` and `tools/ReplayPhone` into `~/.cache/atlas-station/e2e`.
2. Starts the Forge (`uvicorn forge.app:app`, `FORGE_GENERATOR=procedural`, reporting to the Hub) and the Hub
   (`atlas-hub`), and waits for both to answer.
3. Starts ReplayPhone with its synthetic walk, then `station_rehearsal.py live`:
   - the phone connects; `State` arrives at about 60 Hz with no loss, p95 latency a few milliseconds;
   - a parameter is set over HTTP and read back, an out-of-range value is clamped, an unknown one refused;
   - switch space (S1, then the curved S2, S3 and S4), Home, recentre, quality (pinned and automatic), STOP,
     lock on, lock off: each answers 200,
     the phone's own `cmd` event appears in the Hub's `events.jsonl`, and the `State` digest shows the effect;
     a parameter set under lock answers 423;
   - `sample_4x5_table` and `budget_room` are posted to the Forge with one photo per object (the phone's own
     multipart body); all 13 jobs become ready; each GLB is downloaded, its SHA-256 compared with its name,
     and measured by a reader written here with nothing but `struct` (1,500 to 5,000 triangles and no more
     than the target, no edge over 0.25 m, within 10 % of the box, base centred, no material or texture, under
     1 MB), then by the Forge's own `forge.validator`; the room total is under 60,000 triangles; the same
     crop uploaded again is a cache hit; the Forge keeps no crop file.
4. Turns each walked recording of `recordings/` (the two laps, the wall approach, the jump) into a Hub recording (`station_rehearsal.py record`), plays it through
   ReplayPhone, and reads the session's `state.bin` back from the Hub: every recorded pose is there, bit for
   bit and in order.
5. Stops the Hub and the Forge and reads their output and the five session folders: no exception, traceback
   or 500; whole records in `state.bin`; the six commands, the parameter and the Forge's job states in the
   logs; `perf.csv` with its 34 columns.

What it cannot prove: the phone is ReplayPhone, which shares the link sources with the app but none of its
frame. The link runs over UDP on the loopback interface, so nothing is learnt about Wi-Fi. The meshes come from
the procedural generator; TripoSG has never run. The Console is only fetched, not operated (see
`station/console-tests` for that).

## 3. The gates (`tools/e2e/gates.py`)

One command for "the gates as a whole". It runs `verify.py all` (keeping one `.trx` per test assembly),
`gen_meta.py --check`, `gen_scenes.py --check`, a search for a lane placeholder header in the Phase 1 code, the
Hub's, the Forge's and the Console's own suites, the performance summariser's tests and the Station rehearsal.
Then it prints the checklist of G0 to G7 (Master Plan section 11.3): each line is `proven` with the tests
and steps that prove it, `FAILED`, `not run`, or `waits` for a phone, the headset, the Editor or a person. A
line is proven only when every test named for it ran in that run and passed, so a test that was renamed or
removed fails its line. Section 11.1 is listed test by test at the end. The exit code is 0 when no headless
line failed; no gate is passed by it alone, because every gate has lines that wait.

```
python3 tools/e2e/gates.py                 # everything
python3 tools/e2e/gates.py --no-station    # the Unity assemblies and the repository checks only
python3 tools/e2e/gates.py --trx DIR       # reuse the results of `verify.py all --results DIR`
```

Logs are kept in `~/.cache/atlas-gates/<tag>/`. The Forge's suite and the rehearsal use `FORGE_PYTHON`,
`station/forge/.venv`, or the interpreter the script runs under, whichever has the Forge's packages.

## 4. What only the Unity-side check and the phone can prove

Unity Editor, no phone (F9):

- `AtlasBootstrap` boots from `Atlas_Boot.unity` and reaches Home on a fixture with the keyboard walker; the
  real `PosePipeline` moves the XR Origin and the tracked camera.
- The real space modules build their meshes and materials from the pool; `Atlas/Surface`, the portal mask
  shader and the flat-twin shader compile; the stencil rooms of S5 and the slice of S6 are on screen.
- `ArPoseSystems.StartReplay` with the three walks of `recordings/` gives the figures of section 1 again
  (cue lead, corner cues, cell changes), now with the real adapters.
- The Editor as the phone of the link against a running Hub: the rehearsal of section 2 with the real app
  where ReplayPhone stood, and the Spectator following it.
- Draw calls, triangles and frame time of each space on `budget_room` (the perf tour).

The phone:

- Camera permission, the Local Network prompt, pairing from the QR code.
- The self-scan: ARKit tracking, LiDAR depth at 256 x 192, FloorScan, the outline confirmed by the walker,
  the world map saved and found again.
- Home rebuilt 1:1: a wall on the screen is where the wall is. Nothing headless can show registration.
- Bump Guard against real walls and real furniture with real depth, the haptic and the sound of the cue, and
  its lead time measured with a tape (section 11.4.5).
- Object photos taken during the scan, sent to a Forge with TripoSG, and the meshes swapped in (L15 to L19).
- 60 frames a second in the headset, the thermal ladder on a warm phone, the lens correction, comfort.
- The link over the Station's own router: L1 to L6, L11 to L14 with the cable really pulled.
