# P1-B06 tile renderer: phone A checklist (deferred; MP 9.9 rule 5, a human runs it)

Scene `CurvedWalk_TileProbe` (see the P1-B06 section of `docs/laneB/TESTING.md`). Fill the measured column; report a miss with its value.

| # | Step | Expected | Measured |
|---|---|---|---|
| 1 | Install the build; record the device, iOS version, thermal state and commit. | recorded | |
| 2 | Stand at the room centre. Do two slow 360° yaw turns at level gaze. Then, at 4 headings, pitch from 20° up to straight down. Record the probe's maximum 2×drawn, total draw calls, set-pass calls and triangles. (If a recorder is not Valid in Release, repeat in a Development build and note it.) | 2×drawn ≤ 150; draw calls ≤ 300 | |
| 3 | Read the Metal HUD over the same 2 minutes: fps, and frame time p50/p95 from Frame Timing Stats. | 60 fps; p50 ≤ 16.7 ms; p95 ≤ 20 ms | |
| 4 | Walk two laps of the lap square, crossing at least 10 tile edges. Record the crossing count, the maximum GC bytes on crossing frames and the crossing-frame p95 from the markers `Atlas.Tiles.Select` + `Atlas.Tiles.Upload`. | crossings ≥ 10; GC 0 B; p95 ≤ 2 ms | |
| 5 | Screenshots at level gaze at 4 headings, both eyes. A judge answers: can the edge of the drawn world be told from the background? Blocked until P1-A06; with the placeholder shader record "not judgeable". | cannot be told apart | |
| 6 | Look down at the feet next to a corner post, then look up 20°. Record whether any post wedge or floor patch disappears. | none disappears | |
