# Performance evidence

How the numbers for gate G4 are made, where they land and how they get into this folder
(Master Plan §8.5, §8.6, §11.2, §11.4.1; tasks `P1-A07` and `P1-A14`).

The app measures itself. Four scripted runs are built in (`ATLAS/Assets/Atlas/App/PerfTour/`):

| Run | `perf.run` | What it does | Lasts |
|---|---|---|---|
| Per-space run | `tour` (1) | Loads `budget_room`, pins the Nominal tier, enters every built space in turn (S0, S1 5 × 5, S1 3 × 3 × 3, S2 at q = 5, S3, S4, S5, S6), waits 3 s, measures 30 s each (in S2 the head stands for 5 s first, so the curvature ramp is over before a frame is measured) | about 35 s per row |
| Soak | `soak` (2) | 15 minutes alternating S1 Glued 5 × 5 and S2 Curved Walk (q = 5) every 60 s, ladder automatic, LiDAR depth on | 15 min |
| Soak, depth off | `soakNoDepth` (3) | The same with the depth image neither requested nor read, the head from a recorded walk; nobody wears the headset | 15 min |
| Ladder check | `ladder` (4) | In Home, forces Fair, Serious, Critical, High, Nominal the way the Console does and times each until it is in force | about 20 s |

S2 Curved Walk (the {4,5} tiling at ρ = 3, the room as tile zero), S3 Aligned Cells and S4 Small Planet are in the tour; a build that lacks one leaves it out and says so in its file. §11.4.1 names S2 Curved Walk and S1 Glued 5 × 5 for the soak, and the soak alternates those two (Home stands in for a space the build lacks, and the file says so). In Curved Walk the soak also stands for 5 s after entering.

## Before a run that counts

1. Release build, phone A, in the closed headset, brightness 40 %, not charging (§11.4.1, §8.6).
2. The phone is cold: thermal Nominal. The tour fails a row with `notCold` if the phone leaves Nominal while it
   is measured, and each file records `thermalAtStart`.
3. The Hub is running and the phone is paired, if the Console is to plot the run. It is not needed: every
   row is also written on the phone.
4. For the depth-off soak, record a walk first (set `pose.record` on, walk the room for a few minutes, set
   it off). The newest `walk_*.poselog` in the phone's `logs` folder is replayed. With no recording the
   built-in lap is used and the file says `pose=synthetic`.

## Starting a run

A run starts when the parameter `perf.run` leaves `off`, and the parameter goes back to `off` when the run
has ended. Setting it to `off` during a run stops the run.

| From | How |
|---|---|
| On-device menu | Group `perf`, row `perf.run`: choose `tour`, `soak`, `soakNoDepth` or `ladder` |
| Console | The same parameter in the parameter list, or `curl -X POST http://<station>:8080/api/params/perf.run -d '{"value": 1}'` |
| Command line | `-atlasPerf tour` (or `soak`, `soakNoDepth`, `ladder`). In Xcode: Product → Scheme → Edit Scheme → Arguments. Add `-atlasRoom budget_room` to skip onboarding, and `-atlasPerfQuit` to quit when the run has ended |
| Editor | Press Play, then set `perf.run` from the Console, or start the Editor with the arguments above |

Other parameters of the group, read when a run starts:

| Parameter | Default | Command line | Meaning |
|---|---|---|---|
| `perf.visitSeconds` | 30 | `-atlasPerfVisit 30` | Seconds measured per space in the tour (5 to 120) |
| `perf.soakMinutes` | 15 | `-atlasPerfSoakMinutes 15` | Length of the soak. Under 15 the result is `incomplete`: a rehearsal |
| `perf.soakIntervalSeconds` | 60 | `-atlasPerfInterval 60` | How often the soak changes space; recorded in the file as `intervalSeconds` |
| `perf.pose` | `auto` | `-atlasPerfPose auto` | `auto`, `tracked`, `replay`, `synthetic`: where the head comes from |

`perf.pose = auto` means: the tour walks the built-in lap (an ellipse inside the free rectangle at 0.8 m/s
with a sweeping gaze, the same on every run); the soak follows the walker's tracked head on a phone and the
built-in lap elsewhere; the depth-off soak replays the newest recorded walk. The depth-off soak never follows
a tracked head, whatever is asked: depth is never off while someone walks (§8.6).

A run puts the phone in the headset view, starts a new session (so its files have their own name), and when
it ends it gives back the pinned tier, the Glued layout, the pose source and the depth request, and returns
to Home. `budget_room` stays the active room: scan or choose a room again before a walker enters.

## What a run writes

On the phone, in `Documents/logs/` (`Application.persistentDataPath/logs`; in the Editor on macOS
`~/Library/Application Support/<company>/<product>/logs`):

| File | Content |
|---|---|
| `tour_<session>.csv` | The run's summary: facts and notes as `# key=value` lines, then one row per space (`kind=space`), one for a whole soak (`kind=total`) or one per forced tier (`kind=ladder`), each with `result` = `pass`, `fail` or `incomplete` and the rows that failed |
| `perf_<session>.csv` | The probe's row per second for the whole run, 34 columns (lane D contract §5.3). The thermal state of every second is in its `thermal` column: this is the soak's thermal timeline |
| `events_<session>.jsonl` | The event log. Every summary row is also a `perf.summary` event; `perf.runBegin`, `perf.note` and `perf.runEnd` frame the run |

`incomplete` means nothing failed but a value could not be measured: the Editor reports no CPU-thread or GPU
times, and a soak shorter than 15 minutes cannot show "Serious not before 10 min". Only a phone gives `pass`.

Columns of `tour_<session>.csv`:

```text
run,kind,label,space,variant,room,pose,depth,seconds,frames,p50,p95,p99,max,over17_2,over33_8,fpsMean,fpsMin,drawCalls,setPass,tris,cpuMainP95,cpuRenderP95,gpuP95,memMB,gcAllocB,gcAllocMaxB,thermalMax,tFairS,tSeriousS,tCriticalS,tier,effectMs,result,failed,unmeasured
```

- Times are milliseconds; `over17_2` and `over33_8` are percent of frames; `tFairS`, `tSeriousS` and
  `tCriticalS` are seconds from the start of the run to the first Fair, Serious and Critical, `-1` when never.
- Frame, CPU and GPU times are taken from every measured frame. Draw calls, set-pass calls, triangles and
  memory are the largest value of any second. `gcAllocB` is the mean over the seconds of each second's worst
  frame, and `gcAllocMaxB` the worst frame of all.
- The 3 s after a space is entered are not measured, and neither is the second of the probe that straddles
  them, so a switch never reaches a row.
- `depth` is `on` (LiDAR), `off` (the depth-off soak) or `simulated` (no LiDAR on this platform).

## Pulling the files

**From the phone (file sharing).** The build sets `UIFileSharingEnabled`, so the app's `Documents` folder is
open to the Mac.

- Finder: select the iPhone in the sidebar → Files → ATLAS → drag the `logs` folder out.
- Or on the command line:
  `xcrun devicectl device copy from --device <udid> --domain-type appDataContainer --domain-identifier com.winstonthov.atlas --source Documents/logs --destination ./logs`

**From the Hub.** While the phone is paired the Hub keeps the same session under `station/hub/logs/<session>/`
(or the folder given with `--logs`):

```sh
curl http://<station>:8080/api/sessions                              # newest first
curl -O http://<station>:8080/logs/<session>/perf.csv
curl -O http://<station>:8080/logs/<session>/events.jsonl
```

`events.jsonl` holds the summary rows as `perf.summary` events, which is all `summarise.py` needs. If the
link was down for part of the run, send the phone's own CSV afterwards and the Hub merges the missing rows:
`curl -X PUT --data-binary @perf_<session>.csv http://<station>:8080/logs/<session>/perf.csv`.

## Making the table

```sh
python3 tools/perf/summarise.py logs/tour_*.csv --out docs/evidence/G4_metrics.md
python3 tools/perf/summarise.py events.jsonl                 # the same from the Hub's file
python3 tools/perf/summarise.py logs/tour_*.csv --strict     # exit 1 when a row fails
python3 tools/perf/test_summarise.py                         # the tool's own check, on tools/perf/sample/
```

Give it every file of the day at once: it writes the per-space table, both soaks with their thermal
timeline, the difference in time-to-Fair with depth on and off, and the ladder check. It computes every
verdict again from the numbers and lists any row where it disagrees with the phone. Given only a
`perf_<session>.csv` it still makes a table, one column per stretch of one space, without the settle time
left out: good for a look, not for the gate.

Commit, for each run: `perf_<session>.csv` (§11.4.1 step 6), `tour_<session>.csv`, and the Markdown.

## The register, to fill at G4

Per space, cold, on `budget_room` with the room's content (the Home shell and all twelve stand-in objects).
The measured values replace the planning figures of §8.5.

| Metric | Must | S0 Home | S1 Glued 5x5 | S1 Glued 3x3x3 | S2 Curved Walk | S3 Aligned Cells | S4 Small Planet | S5 Impossible Rooms | S6 Slicer |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Frame time p50 / p95 / p99 (ms) | ≤ 16.7 / 20 / 33.3 |  |  |  |  |  |  |  |  |
| Frames over 17.2 / 33.8 ms (%) | ≤ 5 / 0.5 |  |  |  |  |  |  |  |  |
| Draw calls, both eyes | ≤ 300 |  |  |  |  |  |  |  |  |
| Set-pass calls | ≤ 60 |  |  |  |  |  |  |  |  |
| Triangles, both eyes | ≤ 500k |  |  |  |  |  |  |  |  |
| CPU main / render p95 (ms) | ≤ 8 / 6 |  |  |  |  |  |  |  |  |
| GPU p95, cold (ms) | ≤ 9 |  |  |  |  |  |  |  |  |
| Resident memory (MB) | ≤ 1536 (1.5 GB) |  |  |  |  |  |  |  |  |
| Managed allocation per frame (B) | ≤ 1024 |  |  |  |  |  |  |  |  |
| Thermal state, highest | Nominal (cold) |  |  |  |  |  |  |  |  |
| Frames measured |  |  |  |  |  |  |  |  |  |
| Must column |  |  |  |  |  |  |  |  |  |

The soak (§11.4.1), phone A, `budget_room`:

| Soak | Must | Depth on | Depth off |
| --- | --- | --- | --- |
| Thermal timeline | No Critical; Serious not before 10 min |  |  |
| Time-to-Fair | reported |  |  |
| Time-to-Serious | not before 600 s |  |  |
| Frame rate, mean | ≥ 45 |  |  |
| Frame rate, lowest second | ≥ 30 |  |  |
| Space changes every | recorded | 60 s | 60 s |
| Result |  |  |  |
| Time-to-Fair, off minus on | reported |  |  |

The ladder (`P1-A07`): each tier forced from the Console is in force within 1 s.

| Forced tier | Must | Time until in force (ms) | Result |
| --- | --- | --- | --- |
| 1 Fair | ≤ 1000 ms |  |  |
| 2 Serious | ≤ 1000 ms |  |  |
| 3 Critical | ≤ 1000 ms |  |  |
| 4 High | ≤ 1000 ms |  |  |
| 0 Nominal | ≤ 1000 ms |  |  |

Rows of §11.2 that these runs do not fill: frame rate hot and GPU p95 hot (G7, from the soak of the session's
own room), the Slicer's p95 by object (`ProfilerMarker("Atlas.Slicer4D")`, read in the Unity Profiler), and
motion-to-photon (§11.4.2).

## Rehearsing with no phone

In the Editor, press Play and set `perf.run`. The whole run plays against the fixture with the built-in lap;
frame time comes from the frame's delta, the thread and GPU times read 0, and every space row ends
`incomplete` unless something really fails. That checks the script, the files and this tool; it is not
evidence. Headless: `python3 tools/headless/verify.py test Atlas.App.Tests --filter "FullyQualifiedName~PerfTour"`.
