# Bump Guard in every space (P2-C10)

The scripted approach test of Master Plan section 11.4.5, repeated in S0 to S6 with the same fixed thresholds
(section 4.10: WARN at `now < 0.30 m` or `path(0.8 s) < 0.05 m`, STRONG at `now < 0.20 m` or `path(0.4 s) < 0.05 m`,
relax after 0.05 m and 0.3 s). The thresholds are constants in code and are not changed for any space. A space
that fails a row is a finding for that space's lane or for Bump Guard, never a reason to tune a threshold for it.

Acceptance (task row `P2-C10`), verbatim: in each space the warning starts at least 0.6 s before predicted contact
in 10 of 10 wall approaches; no STRONG cue and no WARN on the straight sides in one clear-floor 3-4-5 walk. WARN
cues at the corners are counted and reported, not judged (open item 19, section 14; as in `P1-C09`).

## What the headless run proves, and what it cannot

Bump Guard reads the physical head pose and the physical scan only. No space feeds it anything, and a space cannot
move the wall it warns about. So the headless test below, run once per space, proves that the shared logic meets
the rows on the walk each space asks of the walker, with the space's name on the row. It cannot show what a space
changes on the phone: frame time while the cue plays, the cue drawn over that space's look, the haptic at the
space's frame rate, or the room registration the space leaves behind. That is the device check, and only the
device check fills the "Phone" columns.

| Space | Built on this branch | Headless row | Phone row |
|---|---|---|---|
| S0 Home | yes | run | to do |
| S1 Glued | yes | run | to do |
| S2 Curved Walk | no (lane B) | pending | pending |
| S3 Aligned Cells | no (lane B) | pending | pending |
| S4 Small Planet | no (lane B) | pending | pending |
| S5 Impossible Rooms | yes | run | to do |
| S6 Slicer | yes | run | to do |

## Headless harness

`ATLAS/Assets/Atlas/Tests/Safety/BumpGuardSpacesTests.cs` (assembly `Atlas.Safety.Tests`; runs in the Unity Test
Runner and headless). It reuses `BumpScenario` (the real `BumpGuardCore` on a synthetic depth image, 60 Hz head,
the scan's distance field with the one-cell inset) and the fixtures in `recordings/`. Per space:

| Test | Walk | Pass |
|---|---|---|
| `WallApproaches_TenOfTen_WarnAtLeast600msBeforeContact` | Ten approaches to the wall of a 7 x 7 m room, each starting 3.2 m out: head-on at 0.5, 0.6, 0.7, 0.8, 0.9, 1.0 m/s; 45 degrees at 0.5, 0.75, 1.0 m/s; a second head-on at 1.0 m/s | 10 of 10 have a cue, exactly one rising edge, and a lead time to predicted contact of at least 0.6 s. Predicted contact is where the straight path reaches `clear() = 0`, 5 cm before the wall |
| `RecordedWallApproach_OneCueAtLeast600msBeforePredictedContact` | `recordings/walk_wall_approach.poselog` (1.0 m/s at the wall of `sample_3x3`, stops 0.3 m short) | One cue; lead at least 0.6 s |
| `RecordedLap_NoStrongAndNoWarnOnTheStraightSides_CornerCuesCounted` | `recordings/walk_lap_sample_3x3.poselog`, one lap of the lap square at 0.5 m/s | No STRONG, no WARN more than 0.5 m from a lap corner; corner WARNs counted and written to the test output |

S2, S3 and S4 report as ignored with the reason "Pending, lane B". The scripted five-phase lap of
`BumpGuardReplayTests.LapSquare_NoStrongAndNoWarnOnTheStraightSides` and the 20 approaches of
`BumpGuardReplayTests.Approach_CueFiresOnceAtLeast600msBeforeContact` (the chair and the stool included) stay as
they are and cover the room, not a space.

```sh
python3 tools/headless/verify.py test Atlas.Safety.Tests --tag c10 --filter "FullyQualifiedName~BumpGuardSpaces"
```

### Headless results (this branch, phone-independent)

| Row | Value |
|---|---|
| Lead time of the ten approaches | 0.850 to 0.933 s, 10 of 10 at or above 0.6 s, one rising edge each |
| Recorded wall approach | WARN at z = 0.617 m, lead to predicted contact 0.833 s |
| Recorded lap, STRONG | 0 |
| Recorded lap, WARN on straight sides | 0 |
| Recorded lap, corner WARNs | 2 (reported, not judged) |

These numbers are the same for each built space by construction (see above).

## Device check (phone in headset)

Fill one block per space. Set-up as section 11.4.5: the build under test processes depth at the rate that ships
(never below 15 Hz), and that rate is recorded; the depth image's vertical field of view is recorded; one level
floor, no glass, cables removed, a spotter within reach for anyone outside the team.

Room: the lap-square room of the session (the free rectangle at least 2.4 x 2.4 m so that a 3-4-5 lap fits), one
wall that is clear of furniture, at least 3 m of floor in front of it.

For each space S0, S1, S5, S6, and for S2, S3, S4 once lane B is bound (do not run them before: the 3-4-5
walk and the dial are lane B's):

1. Enter the space from the Console (`SceneCmd`, or the exhibit menu in the Playground). In S2 and S4 the walker
   starts at the room centre and enters with the dial at q = 5 (S2) or q = 3 (S4); in S3 at q = 5.
2. Wall approaches. Ten approaches, each starting at least 3 m from the wall and reaching it within 30 s, the walker
   looking level and not at the floor: head-on at about 0.5 m/s three times, head-on at about 1.0 m/s three times,
   at 45 degrees at about 0.5 m/s twice and at about 1.0 m/s twice. The walker stops at the cue. Read the lead
   distance (tape, head to wall when the cue starts) and the lead time from `events.jsonl` and `perf_<session>.csv`.
   Record the distance at which the walker came to rest.
3. The 3-4-5 walk. One clear-floor lap of the lap square at 0.5 m/s, the lap markers on where the space has them
   (S0 and S1 walk the square without a dial). Count STRONG cues anywhere, WARN cues on the straight sides, and
   separately WARN cues within 0.5 m of a lap corner as the cue starts.
4. Note anything the space does to the cue: a late or missing haptic, the cue hidden by the space's look, a
   frame-time spike while it plays (`perf_<session>.csv`, `thermal` and frame columns), and the space's own events
   at the same moment (`walk.corner`, `tile.cross`, `portal.cross`).
5. Pass for the space: 10 of 10 with a lead time of at least 0.6 s; STRONG = 0; straight-side WARN = 0.

Space-specific notes for the walker and the operator:

| Space | What to watch |
|---|---|
| S0 Home | Baseline. The walls are drawn where they are |
| S1 Glued | Walls are kerbs among 24 copies: the wall in front is also a doorway in the picture. Warn on the real wall, not on the picture |
| S2 Curved Walk (pending) | Tile zero is the room only at entry, then it drifts: Bump Guard is the only protection (section 10.1.3). The approach must be made on the real floor with the kernel far from tile zero as well as at entry |
| S3 Aligned Cells (pending) | The cell grid is registered, so walls and kerbs coincide with the real walls |
| S4 Small Planet (pending) | Not registered; the world is six rooms. Approach the real wall while the picture shows open sky |
| S5 Impossible Rooms | Booth and loop corridor: approach a real wall from inside the hall and from inside the corridor |
| S6 Slicer | The pedestal stands at the centre c0 (registered): approach the real wall with the pedestal in view and then looking away |

### Results (empty: to be filled on the phone)

Build ______   Phone ______   Depth rate (Hz) ______   Depth vertical field of view (deg) ______   Date ______   Walker ______   Spotter ______

Approaches (lead distance m / lead time s / rest distance m), one row per approach:

| # | Approach | S0 | S1 | S2 | S3 | S4 | S5 | S6 |
|---|---|---|---|---|---|---|---|---|
| 1 | head-on 0.5 m/s | | | pending | pending | pending | | |
| 2 | head-on 0.5 m/s | | | pending | pending | pending | | |
| 3 | head-on 0.5 m/s | | | pending | pending | pending | | |
| 4 | head-on 1.0 m/s | | | pending | pending | pending | | |
| 5 | head-on 1.0 m/s | | | pending | pending | pending | | |
| 6 | head-on 1.0 m/s | | | pending | pending | pending | | |
| 7 | 45 deg 0.5 m/s | | | pending | pending | pending | | |
| 8 | 45 deg 0.5 m/s | | | pending | pending | pending | | |
| 9 | 45 deg 1.0 m/s | | | pending | pending | pending | | |
| 10 | 45 deg 1.0 m/s | | | pending | pending | pending | | |
| Warned at least 0.6 s early, of 10 | must be 10 | | | pending | pending | pending | | |

The 3-4-5 walk:

| Count | Must | S0 | S1 | S2 | S3 | S4 | S5 | S6 |
|---|---|---|---|---|---|---|---|---|
| STRONG cues anywhere | 0 | | | pending | pending | pending | | |
| WARN cues on the straight sides | 0 | | | pending | pending | pending | | |
| WARN cues at the corners | reported | | | pending | pending | pending | | |
| Haptic felt at every cue | yes | | | pending | pending | pending | | |
| Frame p95 while a cue plays (ms) | reported | | | pending | pending | pending | | |
| Result | | | | pending | pending | pending | | |

Findings and any space that failed a row:

(none yet)
