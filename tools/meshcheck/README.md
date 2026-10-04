# meshcheck: the numbers of the mesh design's proof

`check.py` reads a **record** and prints the numbers of checks M1 and M2 of `documents/ATLAS_Mesh_Design.md`
(section 5.6). It has been run on records made with the Forge's procedural generator only
(`station/forge/tests/test_meshcheck.py`). No record of a real room exists yet, and no number it has printed
says anything about TripoSG.

## Taking a record

A record is the Forge's data folder after one scan of a real room by the team (decision M14). It stays on
the Station and never enters git (`station/forge/.gitignore` leaves `data/` out).

1. Start the Forge with a data folder of its own and with the crops kept:

   ```
   set FORGE_DATA_DIR=C:\atlas\record-<date>
   set FORGE_DEBUG_KEEP=1
   uvicorn forge.app:app --host 0.0.0.0 --port 8000
   ```

   `FORGE_DEBUG_KEEP=1` writes the photos of the room to disk. Team sessions only; `GET /health` reports it.
2. Scan the room with the phone and enter Home. The app uploads the RoomModel, with a profile and a facing
   rule per object, and the photos of each object.
3. Wait until the Forge has nothing left to do: `curl http://127.0.0.1:8000/queue` shows no `active` and an
   empty `queued`.
4. The room's id is the name of the one folder under `rooms\`.
5. Write `rooms\<roomId>\truth.json` by hand, once: for each object, which side of its box is really the front.

   ```json
   {"1": "+Z", "2": "+X", "3": "none"}
   ```

   The keys are the object ids of the RoomModel (`rooms\<roomId>\room.json`, `objects[].id`). The sides are
   those of the box as the RoomModel holds it, after the phone's facing rule: `+Z` is the side the phone took
   for the front. So stand where you face the real object's front and look at its first shape in the headset
   or on the Spectator. Write the side of the shape that you see: its front `+Z`, its own right-hand side `+X`,
   its back `-Z`, its own left-hand side `-X`. An object with no front (a table, a lamp, a plant) is `none`.
   An object left out is not judged, and it then stands between its line and a verdict: leave none out.

What the folder then holds:

| Where | What |
|---|---|
| `rooms/<roomId>/room.json`, `room.meta.json`, `truth.json` | The RoomModel with its profiles and facing rules; the Forge's plan (which objects receive meshes, their targets); the truth |
| `jobs/<jobId>/job.json` | The job: its targets, the profile, the cameras, and one line per generation with its seconds and its fit |
| `jobs/<jobId>/view<i>.jpg`, `matte<i>.png` | The crops and their mattes. **Camera frames: they never leave the Station** |
| `jobs/<jobId>/raw_view<i>.npz` | The generator's output of each attempt, before clean-up |
| `jobs/<jobId>/full.glb`, `light.glb`, `report.json` | Both published copies and their clean-up reports |
| `assets/<sha256>.glb` | The same copies under the names the phone fetches them by |

## The numbers

Run it with the Forge's interpreter, from the repository:

```
station\forge\.venv\Scripts\python tools\meshcheck\check.py --data C:\atlas\record-<date> --room <roomId> ^
    --json numbers.json --pictures pictures
```

`--truth FILE` names a truth file elsewhere; `--poor-fit` is the poor-fit line when `FORGE_POOR_FIT` was not
0.20. Per object it prints:

```
1 chair  0.5 x 0.9 x 0.5 m
  first shape: a shape (profile covers 1.00); facing by `profile`, truth +Z: right
  attempt from view 0: fit 0.312, 31.0 s
  attempt from view 1: fit 0.087, 29.4 s  <- published
  mesh: triposg (flash), fit 0.087, up Y, yaw 90; its front is on +Z: right
  full copy: 11102 triangles (target 11102), 402118 bytes, 3 cell(s): passes
  light copy: 5000 triangles (target 5000), 181240 bytes, 3 cell(s): passes
```

(The figures of that example are made up to show the shape of the output.) Then the totals:

| Line | From | Passes when |
|---|---|---|
| M1 · no object with a profile is a plain box | The RoomModel: a plain box is an `unknown` or `other` object whose profile covers under 0.30 of its box | None is, and at least one object has a profile |
| M1 · chairs and couches face right | `truth.json`: the phone's facing is right when the truth is `+Z` | At least 90 % of the room's chairs and couches are in the truth and right |
| M1 · every wall-backed object faces right | The same, for the categories whose rule is the free floor (storage, screen, refrigerator, oven, dishwasher, washerDryer, stove, fireplace, sink, bathtub) | All are in the truth and right |
| M1 · facing by the rule that answered | Right and total per `objects[].facing` | Not judged: for tuning the rule lists |
| M2 · each meshed object has a full and a light copy that pass the validator | `forge.validator` on both files, at their levels and targets | All of the room's meshed objects (at most 32) |
| M2 · generation takes 60 s or less per attempt | `attemptLog[].seconds` of each meshed object's job | Every attempt, and every meshed object has a timed one that made a mesh |
| M2 · the median fit is 0.12 or better | The published fit of every meshed object that has one (none without a profile, or with one that saw under a tenth of the box) | Median ≤ 0.12 whatever the others would have scored |
| M2 · generated meshes of objects with a front face right | The front of each published mesh, read from the mesh by the phone's own profile rule (its back is the side whose edge strip is highest), against the truth | At least 90 % of the meshes are read, in the truth and right |
| M2 · objects above the poor-fit line after their attempts | Published fit above 0.20 | Not judged: listed by name |

A line has a verdict only when the record settles it. An object that could not be judged counts against a
pass and against a fail alike, and is named in the line: one that `truth.json` leaves out, a mesh whose front
cannot be read, an object with no fit, one with no timed generation. So "chairs and couches face right" passes
only if at least 90 % of all the room's chairs and couches are known to be right, fails only if under 90 %
would be right even with every unjudged one right, and has no mark otherwise. The median passes or fails only
if it does so whatever the objects without a fit would have scored. A room in which no object has a profile
has no verdict on plain boxes.

A box is judged by the last job that generated its mesh. An answer from the cache (the phone sends a box
again whenever the app is started on the same room) holds no generation and stands in only when the box has
no other job; the line on seconds then says how many meshed objects have no timed attempt that made a mesh.
An attempt that failed counts when it ran over the time, and never towards a pass: one that broke off early
does not show how long the object's generation takes.

The last line counts the lines that pass, fail and are not settled. The exit code is 0 only when every line
that asks for something passes, 1 when one fails, 3 when none fails but one is not settled (not a pass: the
record does not show it), 2 when the record cannot be read.

What the script cannot see, and a person must:

- **The first shapes on the phone** (the rest of M1): that they are there within 10 s of entering Home, and
  that a named object's fitted template looks like it. The script knows only which objects end as plain boxes.
- **The front of a mesh the profile rule cannot read**: a cupboard, a refrigerator, anything flat on top.
  They are named in the line ("the mesh's front could not be read for …"); look at the picture or at the mesh.
- **Whether the mesh looks like the object.** The fit measures heights seen from above, nothing else.
- M3 (the budget on the phone) and M4 (doors and windows) are not in the record.

## Where the numbers go

`docs/evidence/mesh/`: `numbers.json`, the printed text, and the pictures of `--pictures`. Each picture shows
the scan's profile on the left and the published mesh's on the right, seen from above with the box's front at
the top, heights as greys. It is made from the profile and the mesh alone. **Nothing else of the record goes
into git: no crop, no matte, no photo of the room.**

## Tuning clean-up without the card

Because the record keeps the generator's raw output, clean-up can be changed and run again on the same
objects (`station/forge`):

```
python -m forge.reclean <jobId> --data C:\atlas\record-<date>
set FORGE_TRIPOSG_FRONT=canonical
python -m forge.reclean <jobId> --data C:\atlas\record-<date> --out again
```

It prints the fit of every kept attempt, the new report, and whether the copies are the bytes that were
published. It writes nothing into the record.
