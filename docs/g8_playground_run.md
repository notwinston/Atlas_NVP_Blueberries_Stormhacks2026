# G8 Playground run: script and operator checklist (P2-C11)

Gate G8 (Master Plan section 11.3): present are lane A, lane C and two first-time users. A first-time user
completes the Geometry Playground with walk, look and one button; every space shows one predicted-against-measured
number; the comfort and usability rows hold a measured value (n >= 5 is reached in `P4-C05`).

Task row `P2-C11`: **three complete runs in two rooms with no restart; the Spectator shows every beat.**

This page is the script to read from and the sheet to fill. Every results table is empty until a phone has done it.
Nothing here has been run on a phone.

## Before the day

| # | Check | Done |
|---|---|---|
| 1 | Build with every Phase-2 package in (`P2-C01` to `P2-C17`, `P2-A01` to `P2-A04`) and lane B's S2, S3, S4, dial and 3-4-5 walk bound. Note the build id | |
| 2 | Bump Guard check of `P2-C10` done in every space (`docs/evidence/bumpguard_spaces.md`) | |
| 3 | Two rooms scanned and saved, each with a free rectangle of at least 2.4 x 2.4 m (the loop corridor and the 3-4-5 lap need it), one level floor, no glass, cables removed. Room A: the lab. Room B: a different room (a different size is better) | |
| 4 | Hub running, phone paired, Local Network prompt already answered (beat 2 exit), Console open, Spectator running and showing the phone's State | |
| 5 | Console `pg.fmsStop` read and written down: ____ (the build default is 4, provisional until `P4-C02`; open item 6) | |
| 6 | Phone cold (thermal Nominal), brightness 40 %, headset clean, spotter named | |
| 7 | Phone event log cleared or the session named, so that each run is one session. The Hub keeps `station/hub/logs/<session>/events.jsonl` | |
| 8 | The Console SEQ field exists (see "Seams to confirm"). If it does not, type SEQ into the Console note field and carry it into the sheet by hand | |
| 9 | Narrator and operator read this page once together | |

Roles (section 13): the walker; the narrator, who talks to the walker, asks FMS aloud and SEQ at the end and relays
"home" and "stop"; the operator at the Station, who types FMS and SEQ and watches the Spectator; a spotter within
reach.

## The three runs

| Run | Room | Walker | Input used alone for the whole run | Notes |
|---|---|---|---|---|
| 1 | A | first-time user 1 | Gamepad (A, D-pad, Y, Start) | |
| 2 | B | first-time user 2 | Gaze dwell (700 ms) | The second room, without restarting the app: choose the saved room B from the on-device menu |
| 3 | A or B | lane A or lane C person, or user 1 again | Headset lever (click, double click, triple click, long press) | Proposed spread: it also covers `P2-C07` (the full run once per input alone). Any input order is fine if each is used once |

"No restart" means: the app is not quit, rebuilt or reinstalled between runs, and a run starts again from beat 1
through the director. A restart, a crash or a reinstall in the middle of the day is written in the sheet and the
run counts as not complete.

## The script, beat by beat

Beat names and numbers are the director's (`pg.beat`, section 10.1.2). The prompt is the exact text of the prompt
chip (`BeatPrompts`). "Operator" lines are things only the Station does.

| # | Beat (`pg.beat`) | Prompt on the chip | Walker does | Exit condition | Operator does and watches | Events expected |
|---|---|---|---|---|---|---|
| 1 | Scan (`scan`) | Sweep the phone slowly over the floor, then confirm the outline. | Phone in hand, sweeps the floor, confirms the outline (A, chip dwell, click). In run 2, picks the saved room B instead | RoomModel valid | Console shows the outline arrive. Skip to beat 2 with a `SceneCmd` only if the scan fails twice | `pg.beat`, scan and room events |
| 2 | Home appears (`home`) | This is your room, drawn one to one. Confirm when it looks right. | Looks at the boxes, watches Forge meshes replace them (or the fallback set) | Walker confirms; Local Network prompt already answered | Spectator shows the same room; a box that stays a placeholder is noted | `pg.beat`, `space.entered` S0 |
| 3 | Headset on (`headset`) | Put the phone in the headset and stand still. | Phone into the headset, stands still | Head pose stable | Spotter steps in. Start the FMS clock here (the first question comes one minute after the headset is on) | `pg.beat` |
| 4 | Centre (`centre`) | Walk to the glowing disc on the floor. | Walks to the disc | Head within 0.3 m of c0 | Note the time from beat 3 | `pg.beat` |
| 5 | Dial (`dial`) | Stand still and turn the dial to five rooms per corner. | Stands still, turns the dial q to 5 with the input of the run (D-pad, floor dial, lever click cycle 3, 4, 5, 6) | q = 5 selected | Try the dial while the walker moves: it must be ignored above 0.1 m/s. Console shows q. Caption chip of S2 appears at the entrance | `pg.beat`, `param` changes |
| 6 | 3-4-5 walk (`walk345`) | Follow the beacons round the loop and count the corners. | Walks the loop of side s, counts the corners; repeats at q = 3 and q = 4 when asked. After the third leg at q = 4 (S0) and again at q = 5 (S2) looks at the start and presses A or dwells | Loop closed at q = 5 | Watch corner counter, closure error, "world turned by N degrees" on the Spectator. The probe prompt is "Look at where you started and press A." | `pg.beat`, `walk.corner`, `walk.lap`, `walk.closed`, `tile.cross`, `probe` x2 |
| 7 | Free Curved Walk (`free`) | Walk freely. Open the mode chip when you are ready. | S2, Mode 1: walks, sees the mini-map after the first lap, uses the home beacon | Walker opens the mode chip | Spectator shows the mini-map reveal | `pg.beat`, `walk.*`, `tile.cross` |
| 8 | Compare (`compare`) | Same walk, other mode. Open the menu when you are ready. | S3, Mode 2: caption "smooth curvature" against "curvature at the corners". Same tile id on the switch | Walker opens the menu | Read the tile id before and after the switch | `pg.beat`, `cells.cross`, `cells.postLoop` |
| 9 | Exhibit menu (`menu`) | Look at a sign to choose an exhibit, or confirm that you are done. | Looks at one sign (Glued, Impossible Rooms, Slicer), does its one task, or confirms done | Exhibit chosen, or done | Spectator follows the walker into the exhibit and back. Watch the caption chip, the card values and the exhibit's events (`glued.wrap`, `glued.mode`, `portal.cross`, `rooms.loop`, `slicer.*`) | `pg.beat`, exhibit events |
| 10 | Return Home (`return`) | Walking home. Take the headset off when your room is back. | Returns Home (B held, home beacon, triple click), waits for the room | End of run | Kernel back at tile zero, kappa 0, S0 again. Read `pg.home wall_dev_m` (must be 0.05 m or less). Then ask SEQ | `pg.beat`, `pg.home`, `space.entered` S0 |

Every beat can also be advanced from the Console by `SceneCmd`. The run must be completed by condition, with at
most one `SceneCmd` advance per run, which is written in the sheet.

### The three measures taken during every run

**FMS (comfort).** The narrator asks aloud once per minute, the prompt chip reminds with "Comfort check: say your
score, 0 to 20." The operator types the walker's number (0 to 20) into the Console comfort field, which the Hub logs as `fms` (the phone does not log it).
The interval is 60 s, plus or minus 5 s. At or above `pg.fmsStop` the Console raises its red operator alert and the Hub logs `pg.fmsAlert`; the chip reads
"Stop. Headset off, sit down, water." and the narrator stops the run. A stopped run is recorded as stopped, with the
score; it is not repeated by the same walker that day.

**Pointing probe.** Twice per run: after the third leg at q = 4 in S0 (flat), and after the third leg at q = 5 in
S2. The walker looks at where they started and presses A (gaze twin: hold the gaze still within 3 degrees for
700 ms). Logged as `probe {ray_az, pred_geodesic, pred_euclid, err_geo, err_euc}`. Both predictions are checked in
replay against the kernel to 2 degrees (`P2-C17`); on the phone only the five numbers are read.

**SEQ (usability).** At the end of the run the narrator asks "How easy was that? 1 is very difficult, 7 is very
easy." once for the flat walk (S0) and once for the curved walk (S2), the plan asks one question per use case
(section 11.2). The operator types each number (1 to 7) into the Console SEQ field, which logs `seq`.

## Operator checklist (tick during the run; one sheet per run)

| # | Check | Run 1 | Run 2 | Run 3 |
|---|---|---|---|---|
| 1 | Beat 1 Scan completed by condition | | | |
| 2 | Beat 2 Home appears: every box a placeholder or a mesh; no box without one | | | |
| 3 | Beat 3 Headset on completed by condition | | | |
| 4 | Beat 4 Centre completed by condition | | | |
| 5 | Beat 5 Dial: moving walker could not turn it; q = 5 reached standing | | | |
| 6 | Beat 6 3-4-5 walk: loop closed at q = 5; closure error under 0.25 m; world turned 90 deg +- 5 after one lap | | | |
| 7 | Probe 1 (S0, q = 4) taken and logged | | | |
| 8 | Probe 2 (S2, q = 5) taken and logged | | | |
| 9 | Beat 7 Free Curved Walk: mini-map shown after lap 1; home beacon works | | | |
| 10 | Beat 8 Compare: tile id unchanged by the switch | | | |
| 11 | Beat 9 Exhibit menu: one exhibit entered and left, or done | | | |
| 12 | Beat 10 Return Home: Home walls within 0.05 m (`pg.home wall_dev_m`) | | | |
| 13 | FMS asked every 60 s +- 5 s; each entry in the log | | | |
| 14 | SEQ typed twice (S0, S2) and in the log | | | |
| 15 | Spectator showed beat 1 | | | |
| 16 | Spectator showed beat 2 | | | |
| 17 | Spectator showed beat 3 | | | |
| 18 | Spectator showed beat 4 | | | |
| 19 | Spectator showed beat 5 | | | |
| 20 | Spectator showed beat 6 | | | |
| 21 | Spectator showed beat 7 | | | |
| 22 | Spectator showed beat 8 | | | |
| 23 | Spectator showed beat 9 | | | |
| 24 | Spectator showed beat 10 | | | |
| 25 | No restart, crash, reinstall or relaunch during the run | | | |
| 26 | No Bump Guard STRONG cue; WARN cues noted (count) | | | |
| 27 | Walker used only the run's one input | | | |
| 28 | Walker needed no help from the narrator beyond the script | | | |

"Spectator showed beat N" means the operator saw, on the Spectator monitor while the beat was current, the
picture of that beat (the space, the card or chip, the beacons or signs). The `pg.beat` event in `events.jsonl` is
checked separately by `tools/g8/check_run.py`.

### After each run

```sh
curl http://<station>:8080/api/sessions                         # newest first
curl -O http://<station>:8080/logs/<session>/events.jsonl
python3 tools/g8/check_run.py events.jsonl --strict             # beats, FMS cadence, probes, SEQ, walk events
```

Keep `events.jsonl` and `perf_<session>.csv` of each run beside the sheet (`docs/evidence/`). A recorded walk is
motion data of that person: commit one only with their agreement.

## Results (empty: to be filled on the phone)

Build ______   Phone ______   Date ______   Operator ______   Narrator ______

| Run | Room | Walker | Input | Complete, no restart | Beats shown on the Spectator (of 10) | Time, beat 3 to 10 (min) | Session id | Result |
|---|---|---|---|---|---|---|---|---|
| 1 | A | | Gamepad | | | | | |
| 2 | B | | Gaze dwell | | | | | |
| 3 | | | Headset lever | | | | | |

Gate rows:

| Row | Must | Run 1 | Run 2 | Run 3 |
|---|---|---|---|---|
| Three complete runs in two rooms, no restart | 3 of 3, rooms A and B both used | | | |
| Spectator shows every beat | 10 of 10 in each run | | | |
| First-time user completes with walk, look and one button | yes | | | |
| FMS per minute (scores in order) | median peak <= 5 (section 11.2); stop at `pg.fmsStop` | | | |
| FMS peak | | | | |
| Probe error, S0: `err_geo` / `err_euc` (deg) | measured in S0 and S2 | | | |
| Probe error, S2: `err_geo` / `err_euc` (deg) | S2 within 1.5 x of S0 (should) | | | |
| SEQ, S0 / S2 | median >= 5 (n >= 5 in `P4-C05`) | | | |
| Every space shows one predicted-against-measured number | all spaces shown | | | |

One predicted-against-measured number per space, as the walker met it (the equation card, section 10.1.3):

| Space | Predicted | Measured |
|---|---|---|
| S0 Home | closes after 4 right turns | closure error ____ m |
| S1 Glued | wrap at the cell edge, mod a | seam seen: ____ |
| S2 Curved Walk | 90 deg turned per lap at loop-fit L | ____ deg |
| S3 Aligned Cells | tile id differs after 4 crossings, matches after 5 | ____ |
| S4 Small Planet | closes after 3 corners, 6 rooms | closure error ____ m |
| S5 Impossible Rooms | room number after 4 turns differs from start | start ____ after ____ |
| S6 Slicer | section "octahedron", area 6.9282 | ____ |

Problems met (what, when, which beat, fixed how):

(none yet)

## Seams to confirm before the day (not claimed built)

| Seam | State on `laneC/phase2` | Who |
|---|---|---|
| The Console SEQ field | Built (P2-C17): the Console's Ease score field sends `{t:"seq", score}` and the Hub logs `seq {score}`. The phone does not log it | none |
| FMS and SEQ back to the phone | The Hub logs `fms`, `pg.fmsAlert` and `seq` itself; the phone only reacts (stop line, closes the SEQ question) if the link glue feeds `IOperatorEntryFeed`, and never logs them | Lane D (link glue), optional |
| The director that runs the beats (`PlaygroundDirector`, `Beats.asset`, `pg.beat`) | Not on this branch; the beat enum, prompts and the shell parts are | `P2-C01` package |
| Spectator display of beats | The Spectator re-renders from `State`; showing the beat number or name on it is lane D's. If the beat is not visible, tick "Spectator showed beat N" only for what the walker's world shows, and report the gap | Lane D |
| Lane B spaces and the dial | Not on this branch (only contract types and the flat stub) | Lane B, bound at integration |
| Restarting the director for the next run without relaunching the app | Not defined in the plan. If the build has no control for it, write it in "Problems met" | `P2-C01` / integrator |
