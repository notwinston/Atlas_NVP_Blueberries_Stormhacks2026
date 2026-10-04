# Recordings

Recorded scans and walks for replay in the Editor and in headless tests (contract README F8):
`room_*.json` are RoomModel files, `walk_*.poselog` are pose logs. No camera frame is ever stored here.

## Pose logs

The format is in `docs/contracts/lane-c-room.md` section 7.6: a text file, one raw head pose per line,
`t x y z qx qy qz qw s`, with `s` = 0 not tracking, 1 limited, 2 normal. The codec is `Atlas.World.PoseLog`;
`Atlas.Scan.PoseLogReplaySource` plays a log as the head, and `Atlas.Scan.PoseLogRecorder` writes one on the
phone when `pose.record` is on (to `persistentDataPath/logs/`).

A pose log recorded from a person is motion data of that person. Commit one only with their agreement.

## Sample walks

The three logs below are synthetic. They are in the room frame of the fixture `sample_3x3` (a 3 x 3 m room,
free rectangle 2.4 x 2.4 m, lap square 2.0 m), at 60 samples a second and an eye height of 1.6 m.

| File | Walk | Length |
|---|---|---|
| `walk_lap_sample_3x3.poselog` | One lap of the lap square at 0.5 m/s, from the corner (-1, -1) along +Z, then +X, -Z and -X, looking along the path and turning over the last 0.2 m of each side | 16 s, 961 samples |
| `walk_wall_approach.poselog` | Straight from (0, -1) towards the wall at z = 1.5 at 1.0 m/s, stopping 0.3 m short of it, then standing for one second | 3.2 s, 193 samples |
| `walk_jump_and_loss.poselog` | Along +X from (-1, 0) at 0.5 m/s. At sample 90 (1.5 s) the pose jumps 0.5 m along +Z and stays there, as after a relocalisation. From sample 150 (2.5 s) tracking is limited for one second while the pose keeps moving | 4 s, 241 samples |

They are the output of `SampleWalks` in `ATLAS/Assets/Atlas/Tests/Scan/Pose/`, and the test
`PoseLogTests.RecordingsFile_IsTheGeneratorsOutput` compares the files with it. To regenerate them after a change
to the generator:

```sh
ATLAS_WRITE_RECORDINGS=1 python3 tools/headless/verify.py test Atlas.Scan.Tests --filter "FullyQualifiedName~PoseLogTests"
```

## Replaying a walk in the Editor

With the room `sample_3x3` active (`RoomFixtures.Load`), hand the log to the pose systems:

```csharp
var pose = AtlasServices.Get<Atlas.Scan.ArPoseSystems>();
pose.StartReplay("../recordings/walk_lap_sample_3x3.poselog", loop: true, out string error);
// ... and pose.StopReplay() returns to the keyboard walker or the AR camera.
```
