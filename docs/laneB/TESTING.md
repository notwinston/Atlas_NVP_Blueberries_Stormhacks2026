# Lane B phone and headset testing (deferred acceptance)

One section per task: scene, steps, expected numbers. A human fills in the measured values.

## P1-B12 Room content in Aligned Cells (DEFERRED: phone A in the headset)

- **Scene:** `Assets/Atlas/Spaces/AlignedCells/Scenes/Space_AlignedCells.unity`, loaded additively by the boot scene.
- **Preconditions:** real P1-A04 (stereo rig), P1-A06 (AtlasCore declares `_Gates` and calls `AtlasGatesClip`), P1-A08 (AtlasFrameLoop; boot calls `AlignedCellsSpaceHost.Register(spaceLab)`), P1-C06 (RoomModel from a scan) and P1-C07 (`SpaceLab.Home` set). Release build; note the thermal state at the start.

### Test 1, table edge (G7 item; criterion "within 5 cm at three touch points after ten cell crossings")

1. Scan a room with a table, enter S3 and stand in the cell next to the table.
2. Mark three points on the real table's near top edge: left corner, middle, right corner.
3. Walk ten cell-centre-to-cell-centre moves round the nearest interior post. Check `Crossings == 10` on the Spectator or in the log.
4. Touch the virtual edge at each of the three points. A helper measures the gap between fingertip and real edge with a ruler.
5. **Pass:** all three gaps <= 0.05 m. Record the three values: ____ / ____ / ____ m.

### Test 2, budget (G4; criterion "S3 on budget_room <= 170 draw calls and <= 200k triangles per frame at 60 fps")

- Load `budget_room.json` through lane A's replay or A14 tour path. Walk all 12 cells.
- Read ProfilerRecorder "Draw Calls Count" and "Triangles Count", and FrameTimingManager.
- **Expected:** <= 170 and <= 200,000 (Editor proxy worst: 76 draw calls, about 128k triangles); 60 fps; frame time p50/p95/p99 <= 16.7 / 20 / 33.3 ms (Master Plan §11.2 Must).
- Overrun: report the measured value, change no number (§9.9 rule 7).

## P1-B10 Aligned Cells S3 (DEFERRED: phone in the headset)

Same scene and preconditions as above.

- Four quarter-turns round an interior corner end in a fifth tile, and a fifth returns to the first (read the tile id on the Spectator).
- Tile instances alone cost <= 120 draw calls per frame on the 4 x 5 m sample room (Editor worst: 34).
