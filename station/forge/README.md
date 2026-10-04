# The Forge

The Station service that turns object photos into furniture meshes (Master Plan §4.5 and §7.4, tasks `P1-D08`
and `P1-D09`; mesh design `documents/ATLAS_Mesh_Design.md`). The phone uploads a RoomModel and, for each scanned
object, one to three cropped photos with the object's box. The Forge makes a mesh from the photo, cleans it until
it meets the asset contract (§6.4) and serves it as a GLB named by its SHA-256.

```
station/forge/
  forge/app.py            HTTP interface (Appendix B.4)
  forge/service.py        rooms, jobs, the queue and its one worker, the job folder
  forge/imaging.py        crops, the matte, and the picture a generator is given
  forge/cleanup.py        mesh clean-up (P1-D09): orient, scale, regrid, colour, bake, the light copy
  forge/profile.py        the object profile of a mesh and the fit to the scanned one
  forge/colour.py         a palette cell per face, read from the photos
  forge/attempts.py       what the service and reclean share: the fit's set-up, the colour views, the order of attempts
  forge/reclean.py        clean-up again from a kept record, without the generator
  forge/validator.py      the asset validator, library and command line; two levels
  forge/palette.py        palette cells and the vertex colour encoding (lane A §10.2, §10.7)
  forge/glb.py            GLB writer and reader
  forge/generators/       triposg.py · hosted.py · procedural.py
  forge/budget.py         the furniture budget (32 meshes, 120,000 triangles)
  tools/check_station.py  checks what TripoSG needs on the Station
  tools/first_run.py      sends one object through a running Forge and reads the result
  (tools/meshcheck/ at the repository root: the numbers of the proof, from a record)
  requirements.lock       the service; -triposg.txt and -gpu.txt: the generator and matting, Station only
  tests/                  pytest suite; needs no GPU
```

## Set-up

Python 3.11. On a machine with no GPU this is all that is needed; run with `FORGE_GENERATOR=procedural`:

```
cd station/forge
python -m venv .venv
.venv\Scripts\activate                      # Linux and macOS: source .venv/bin/activate
pip install -r requirements.lock
```

The generator is installed on the Station only: PyTorch with the cu128 wheels, `requirements-triposg.txt`,
`diso` built from source, `requirements-gpu.txt` and a TripoSG checkout. "First run on the Station" below gives
the commands in order. TripoSG's own `requirements.txt` is not used: it pins a numpy that has no build for
Python 3.11, and pip cannot build its `diso` in an isolated build environment.

## Running

```
uvicorn forge.app:app --host 0.0.0.0 --port 8000
python -m pytest tests                       # the test suite
python -m forge.validator asset.glb --target 15000 --box 1.0 0.75 0.6 --level full
python -m forge.reclean <jobId>              # clean-up again from a kept record (below)
```

One process only: it owns the GPU. Start it from `station/forge`.

| Variable | Default | Meaning |
|---|---|---|
| `FORGE_GENERATOR` | `triposg` | `triposg`, `procedural` or `hosted` |
| `FORGE_DATA_DIR` | `station/forge/data` | Rooms, jobs, assets and the cache |
| `FORGE_DEBUG_KEEP` | off | `1` keeps the record of each job on disk: crops, mattes, the generator's raw output, both copies and their reports. Team sessions only; `GET /health` reports it |
| `FORGE_HUB_URL` | `http://127.0.0.1:8080/internal/job` | Where job states are posted; empty switches it off |
| `FORGE_PUBLIC_URL` | empty | Prefix of the asset URL in `AssetReady`, e.g. `http://192.168.8.2:8000` |
| `FORGE_HOSTED_ENABLED`, `FORGE_HOSTED_URL`, `FORGE_HOSTED_KEY` | off | The hosted fallback; see below |
| `FORGE_TRIPOSG_REPO`, `FORGE_TRIPOSG_WEIGHTS` | empty | TripoSG checkout and weights folder (`<checkout>/pretrained_weights/TripoSG` when empty) |
| `FORGE_TRIPOSG_STEPS`, `_GUIDANCE`, `_SEED`, `_FP16` | 50, 7.0, 42, on | Inference settings |
| `FORGE_TRIPOSG_FALLBACK_DECODER` | on | When TripoSG's default decoder fails, generate once more with `use_flash_decoder=False` |
| `FORGE_TRIPOSG_UP` | `Y` | Which axis of TripoSG's own frame is up: `Y`, `Z` or `X`. The only up axis clean-up tries for a TripoSG mesh. Measured on the Station (below) |
| `FORGE_TRIPOSG_FRONT` | `unknown` | Where the front of a TripoSG mesh is: `canonical` (on its own +Z), `view` (towards the camera of the photo it was generated from) or `unknown`. Breaks a tie of the fit, and decides alone when a job has no profile. Measured on the Station (below) |
| `FORGE_TRIPOSG_INSIDE_OUT` | off | `1` when TripoSG's faces are wound clockwise seen from outside: the adapter then reverses them. Measured on the Station (below) |
| `FORGE_POOR_FIT` | 0.20 | A fit to the scanned profile above this is poor, in fractions of the box height: the next photo is then tried |
| `FORGE_MATTING_TIMEOUT_S`, `FORGE_GENERATE_TIMEOUT_S`, `FORGE_HOSTED_TIMEOUT_S`, `FORGE_CLEANING_TIMEOUT_S` | 10, 90, 120, 60 | Stage time-outs (§7.4) |
| `FORGE_MAX_ANISOTROPY` | 0.15 | Stretch allowed per axis when fitting a mesh to its box |

`FORGE_TRIPOSG_UP` and `FORGE_TRIPOSG_FRONT` describe TripoSG's meshes only: a mesh of the procedural or the
hosted generator is oriented with nothing assumed about its frame.

The port and host are uvicorn's own arguments. `forge/config.py` holds every setting.

## Routes

| Route | Request | Response |
|---|---|---|
| `GET /health` | – | `{ok, gpu, queue, pipelineVer, debugKeep}` and `gpuName`, `generator`, `generatorReady`, `generatorNote`, `hosted` (and the same flag as `hostedEnabled`), `matting`. Answers at once, also while the model loads |
| `POST /rooms` | RoomModel JSON | 201 `{roomId, roomHash, generated, placeholders, triTarget, triTargets, triTargetLight}`. `roomId` is the RoomModel's `id`. `triTargets` maps every object id to its full target (0 for a box that receives no mesh); `triTarget` is the largest of them; `triTargetLight` is the room's light target. 422 for an object whose `size` has a side that is not above zero |
| `PUT /rooms/{roomId}/mesh` | binary, ≤ 2 MB | 204 |
| `GET /rooms/{roomId}` | – | The RoomModel as uploaded |
| `DELETE /rooms/{roomId}` | – | 204. Removes the room, its mesh, its jobs and the GLBs made for it |
| `POST /rooms/{roomId}/objects` | multipart: `meta` (JSON, Appendix B.4), files `view0`…`view2` | 202 `{jobId}` · 200 `{jobId, cached: true, sha256}`, with `lightSha256`, `lightUrl`, `lightBytes`, `lightTris` when the cached mesh has a light copy · 200 `{jobId, placeholder: true}` |
| `GET /jobs/{jobId}` | – | `JobStatus`, with the `AssetReady` fields and the clean-up report once ready |
| `GET /jobs?room={roomId}` | – | A list of the same |
| `GET /queue` | – | `{active, queued, placeholders, generator}`; `queued` is in the order the worker takes it |
| `GET /assets/{sha256}.glb` | – | The GLB; `ETag` is the hash; 404 if unknown |

Job states: `queued` → `matting` → `generating` → `cleaning` → `ready` or `failed`, and `placeholder` for a
box that receives no mesh.

`meta` holds `boxId`, `category`, `dims`, `yaw`, `triTarget` (1,500 to 15,000), `backend`, `views`, and two
optional members: `triTargetLight` (1,500 to 5,000) and `profile`, the object profile of the scan as the
RoomModel holds it (standard base64 of exactly 256 bytes; anything else is a 422). The job keeps both, and each
view's `intrinsics` and `camToBox`. An older phone sends neither; its job gets the room's own light target.

`JobStatus` holds `jobId`, `roomId`, `boxId`, `state`, `source`, `error`, `triTarget`, `triTargetLight`,
`cached`, `generator`, and `decoder` when the generator has more than one (`flash` or `hierarchical` for
TripoSG). When ready it also holds:

| Members | Meaning |
|---|---|
| `sha256`, `url`, `bytes`, `tris`, `maxEdge`, `bounds`, `palette` | The full copy, as `AssetReady` carries it. `palette` is the cell that covers the most surface |
| `lightSha256`, `lightUrl`, `lightBytes`, `lightTris` | The light copy, an asset of its own under its own hash. Absent when none could be made (the report says why). The same file as the full copy when that is already within the light target |
| `fitUp`, `fitYaw` | How clean-up turned the mesh. `fitUp`: the axis of the generator's frame taken as up; clean-up first turns the mesh so that this axis is +Y. `fitYaw`: the turn it then gave the mesh about +Y, as a yaw in Unity's box frame (0, 90, 180 or 270; positive turns +Z towards +X): the +Z side of the uprighted mesh stands at that azimuth of the box. That side is the generator's own +Z for `fitUp` `Y` and `X`, and its −Y for `Z` |
| `fit` | How far the turned mesh is from the scanned profile: the mean difference of heights over the cells the scan saw, in fractions of the box height; 0 is a perfect match. Absent for a job with no profile, or with one that saw under a tenth of the box |
| `attempts` | The generations made for the object, failed ones included. Absent for an answer from the cache |
| `report` | The clean-up report of the full copy |

## The queue and the furniture budget

- One worker owns the generator and runs one job at a time, the largest box first.
- When a room is uploaded the Forge ranks its boxes by volume. The 32 largest receive meshes. A job for any
  other box is answered with `placeholder: true` and is never generated.
- The room's 120,000 furniture triangles are shared between those boxes by surface area, 3,000 to 15,000 each
  (mesh design §5.4; `forge/budget.py`, in whole numbers so that the phone and `tools/rooms/roommath.py` give
  the same figures). The light target is clamp(60,000 ÷ n, 1,500, 5,000), the same for every mesh of the room.
  Clean-up works to the smaller of the box's target and the `triTarget` sent with the job.
- A second upload for a box whose job is still waiting or running returns that job.
- **Attempts** (decision M7). Each generated mesh is turned and scaled to its box at once, which gives its fit
  to the scanned profile. A fit above `FORGE_POOR_FIT` is poor: the next view is then generated, view 1 and then
  view 2, and the attempt that fits best is finished and published. A mesh is always published, however poor
  its fit; the fit is in the answer. At most three generations are made for an object, and a failed one counts
  as one of the three. A job with no profile has no fit and so one generation.
- A generation that fails or times out is tried again with the next view; after two failures with no mesh the
  generator is given up for that object, and the hosted service is tried if it is switched on. The hosted
  service is never sent a second photo because of a poor fit.
- A job fails when no attempt gives a mesh that meets the asset contract (the best fit is tried first, then the
  next); the phone then keeps its first shape.
- Crops, and the mattes made from them, are held in memory and dropped when the job ends. After a restart
  finished jobs, rooms and assets are still there; a job that was waiting or running is marked failed (its
  crops are gone) and the phone sends it again. With `FORGE_DEBUG_KEEP=1` the crops are on disk and such a job
  runs again by itself.

## Generators

| `FORGE_GENERATOR` | What it is |
|---|---|
| `triposg` | TripoSG on the Station's GPU. The default. **It has never run**, see below |
| `procedural` | Builds a plausible mesh for the category from the box size alone (table, chair, couch, bed, storage, lamp, plant, screen, stairs, basin, toilet, plain box), facing local +Z as the box does: the back of a chair, a couch, a bed and a toilet is on −Z. Deterministic, no GPU. For tests and for running the whole round trip without the Station's graphics card. It does not look at the photo, except for the colours |
| `hosted` | A third-party service only. The interface exists; **no vendor is wired** (Master Plan §14, open item 8) |

**The hosted fallback sends the walker's crop to a third party** whose retention the team does not control. It is
off unless `FORGE_HOSTED_ENABLED=1`. `GET /health` shows `hostedEnabled`, every job that used it reports
`source: "hosted"`, and each use is logged. The walker must be told before the scan (§7.4, Privacy). A job sent
with `backend: "cache"` never reaches any generator.

What the `triposg` generator does around the model (mesh design §5.1):

- **The model is loaded when the service starts**, by the worker, so the first object does not pay for it.
  The matting model is loaded then too: fetching it the first time takes longer than the matting time-out.
  `generatorReady` is true only once that load has succeeded; until then `generatorNote` says what is missing
  or going on (`the model is being loaded`, `the weights are being downloaded to …`). Jobs wait in the queue
  meanwhile. A load that failed is tried again by the next job.
- **The weights.** The download puts each file in place when it is whole: `model_index.json` within seconds,
  the weights minutes later. So a download that stopped leaves the marker and no weights, and the marker
  alone says nothing. A folder is downloaded (the download resumes what it has) when it has no
  `model_index.json`, or when `.cache/huggingface/download` under it holds a file that was left half-way. A
  folder that shows neither and still does not load is fetched once and read again, before the load is given
  up: that is the folder of a download that stopped before one of its files was begun. A download that came
  back without an error is followed by the load whatever lies in that folder: a half-way file can be the
  leftover of an older revision, which no download finishes, and must not keep whole weights from loading.
- **Matting is required.** TripoSG is never given a picture that is not matted. With no matting model
  installed, `generatorReady` is false and a job fails with `TripoSG cannot run: no matting model is installed`.
  A view whose matting fails, or whose matte is empty, is not sent; the next view is tried.
- **The picture** is the crop cut to the bounding box of its matte, put on white, and padded to a square with
  a margin of 10 % of the long side on every side, as TripoSG's own script frames it. Specks of matte under
  0.5 % of it are left out.
- **Two decoders.** If the default one (`flash`, which needs `diso`) raises or returns nothing, the object is
  generated once more with `use_flash_decoder=False` (`hierarchical`), and the job reports which decoder made
  its mesh. After two objects in a row on which only the second decoder worked, it is tried first. Both
  generations share the one generation time-out.

The `procedural` generator needs no matting: the tests run without `rembg`.

## Clean-up and the asset contract

Clean-up (`forge/cleanup.py`) follows mesh design §5.3. The same input gives the same bytes.

| # | Step | What it does |
|---:|---|---|
| 1 | Weld, reduce | Merge coincident vertices, decimate to the full target, drop detached fragments |
| 2 | Orient | **By the fit** when the job has a profile (below); otherwise by what is known of the generator's front, and failing that by the bounds alone, as before |
| 3 | Scale | To the box, at most ±15 % of stretch per axis; the origin at the centre of the base |
| 4 | Regrid | Split edges until none is longer than 0.25 m, with the count at or under the target |
| 5 | Colour per face | A palette cell per face, read from the matted photos (below) |
| 6 | Bake | Face shade × ambient occlusion into the vertex colour |
| 7 | The light copy | From the full copy: decimated to the light target, put back on the full copy's bounds, then steps 4 to 6 |
| 8 | Validate | Both copies, each at its level |

**The fit.** The candidates are the up axes (Y, Z, X; for a TripoSG mesh only `FORGE_TRIPOSG_UP`) times the
four quarter turns, keeping those whose bounds fill the box within the stretch limit. Each is scaled to the box
and its own profile is made with the phone's rays (`forge/profile.py`); the score is the mean difference from
the scanned profile. The lowest score wins. Scores within 0.02 of the best are a tie: `FORGE_TRIPOSG_FRONT`
breaks it (`canonical`: no turn; `view`: the turn that puts the mesh's own +Z side where the camera stood of
the view that mesh was generated from, which for a second or third attempt is not view 0), then the
generator's own up axis is kept, then the lower score. Without a profile, `FORGE_TRIPOSG_FRONT` alone decides
among the turns the bounds allow, and with `unknown` it is the bounds alone. A profile that saw under a tenth
of the box (26 of its 256 cells) is no profile for the fit: over a handful of cells one cell that differs
would make a fit poor and outvote the setting. Every candidate and its score is in the report (`candidates`).

Turns, in both frames: one quarter turn of clean-up is a right-handed +90° about +Y in the GLB frame
(x′ = z, z′ = −x), which is a yaw of −90° in Unity's box frame. The report's `yawDegrees` counts the turn the
first way (0, 90, 180, 270), `fitYaw` the second: `fitYaw` = (360 − `yawDegrees`) mod 360.

**Colour per face** (`forge/colour.py`). A photo is used when it is matted and the phone sent its camera
(`intrinsics` and `camToBox`). A face takes the colour of the photos that see it, then the nearest object cell
of the palette; the cells are smoothed into at most six regions, and a face no photo saw takes the cell of the
nearest seen face. Vertices are split where two cells meet, so every vertex of a face points at that face's
cell. With no usable photo the object keeps one cell from the dominant colour of view 0, as before; that is
the case on a machine with no matting model. `report.colour` names the cells and the share each covers.

**The light copy** is published as an asset of its own. When it cannot be made, or does not meet its level,
the full copy is still published and `report.light.error` says why.

The GLB holds one mesh, one primitive, no material and no texture: `POSITION`, `NORMAL`, `TEXCOORD_0` (the
palette cell, written as (u, 1 − v)), `COLOR_0` (R = shade, G = B = A = 255), 16-bit indices. It is a
right-handed glTF; the importer on the phone inverts X.

The validator (`forge/validator.py`) knows the two copies of a mesh (mesh design §5.4):

| `--level` | Triangles | And at most | File |
|---|---|---|---|
| `full` (the default) | 1,500 to 15,000 | the job's `triTarget` | 2 MB (2,097,152 bytes) |
| `light` | 1,500 to 5,000 | the job's `triTargetLight` | 1 MB (1,048,576 bytes) |

At either level it rejects: more than one mesh, primitive or node; any material, texture, image or sampler; a
node transform; an edge over 0.25 m; a vertex whose palette cell is not an object colour (`palette.cell`); a
face whose three vertices are not in the same cell (`palette.face`); a base that is not on y = 0 or not centred;
with `--box`, any side more than 10 % off the box (wrong scale, units or up axis). A mesh may use several object
cells. Both copies of every job are validated, each at its level, before they are published.

## The record, and clean-up without the generator

With `FORGE_DEBUG_KEEP=1` a job's folder holds what clean-up was given and what it made (mesh design §5.6):

```
data/jobs/<jobId>/
  job.json            the box, the targets, the profile, the cameras, one line per generation (attemptLog)
  view<i>.jpg         the crops as uploaded            (camera frames: they never leave the Station)
  matte<i>.png        the matte of each crop
  raw_view<i>.npz     the generator's output of the attempt from view i: vertices, faces, decoder
  full.glb light.glb  both published copies
  report.json         both clean-up reports
```

`python -m forge.reclean <jobId or folder> [--data DIR] [--out DIR] [--json]` reads such a folder and runs
clean-up again as the service did: every kept attempt is turned and scored, the best is finished. It prints
the fit of each attempt, the new report, and whether the copies are the bytes that were published. With the
settings the job ran under they are; change clean-up, or `FORGE_TRIPOSG_UP`, `FORGE_TRIPOSG_FRONT`,
`FORGE_MAX_ANISOTROPY`, and it shows what the same generated meshes would have become. It writes nothing
into the record. `tools/meshcheck/check.py` at the repository root reads a whole room's record and prints the
numbers of checks M1 and M2.

## First run on the Station

**None of this has been run.** It was written on a Mac with no GPU, from TripoSG's public repository (commit
`fc5c409`, 18 April 2025) and from `diso`'s build script. Nothing below was tried on Windows, on a GPU, or with
TripoSG's weights, and TripoSG has not generated a mesh anywhere. Expect to correct it, and correct this
section when you do. What was checked, all on 4 October 2026:

- The three requirement lists resolve together for Python 3.11 on Windows (`uv pip compile`), and every pin has
  a Windows wheel except `antlr4-python3-runtime`, which is pure Python.
- `pip install diso` fails in pip's isolated build environment, because its build script imports PyTorch (seen
  with pip 26.2.1).
- In a throw-away Python 3.11 environment on the Mac, with `requirements.lock`, `requirements-triposg.txt`,
  PyTorch 2.7.1 for the processor and `rembg[cpu]==2.0.69`: TripoSG's package imports through the adapter's own
  import, with `diso` replaced by a stub; the mesh extraction of the second decoder (scikit-image's marching
  cubes) runs with the pinned numpy; `rembg` cuts TripoSG's example pictures and the Forge frames them; the
  Forge's tests pass.
- The service, `tools/first_run.py` and the `curl` commands below work against the Forge with the procedural
  generator. The adapter passes its tests with a fake pipeline.

The Station is the Windows PC with the RTX 5070. Use the **x64 Native Tools Command Prompt for VS 2022** for
step 2, so the compiler is on the path; any Command Prompt will do afterwards. In PowerShell, write
`$env:NAME = "value"` for `set NAME=value` and `curl.exe` for `curl`.

### 1 · Before installing

| Needed | Check | Why |
|---|---|---|
| Python 3.11, 64-bit | `py -3.11 --version` | The lists are pinned for 3.11 |
| NVIDIA driver for the RTX 50 series | `nvidia-smi` shows the card | PyTorch's cu128 build needs it |
| Build Tools for Visual Studio 2022, workload "Desktop development with C++" | `cl` prints the compiler's version | `diso` is compiled here |
| CUDA Toolkit 12.8 | `nvcc --version`, and `echo %CUDA_PATH%` shows its folder | `diso`'s CUDA part |
| Git, and about 20 GB of free disk | `git --version` | The checkout, the weights (7.95 GB), the wheels |

### 2 · Install

```
cd station\forge
py -3.11 -m venv .venv
.venv\Scripts\activate
python -m pip install --upgrade pip
pip install -r requirements.lock

pip install torch==2.7.1 torchvision==0.22.1 --index-url https://download.pytorch.org/whl/cu128
python -c "import torch; print(torch.__version__, torch.cuda.is_available(), torch.cuda.get_device_name(0))"

pip install -r requirements-triposg.txt

pip install setuptools wheel
set CUDA_HOME=%CUDA_PATH%
pip install --no-build-isolation diso==0.1.4

pip install -r requirements-gpu.txt

git clone https://github.com/VAST-AI-Research/TripoSG C:\atlas\TripoSG
git -C C:\atlas\TripoSG checkout fc5c40990181e2a756c4e0b1c2f4d6b5202faf8c
huggingface-cli download VAST-AI/TripoSG --local-dir C:\atlas\TripoSG\pretrained_weights\TripoSG
```

- The `python -c` line must print `2.7.1+cu128 True` and the card's name. If it prints `False`, stop: the
  wheel is a CPU build or the driver is too old.
- `diso` is TripoSG's default decoder. PyPI has only its source, and its build script imports PyTorch, so it is
  installed after PyTorch and with `--no-build-isolation`. **`CUDA_HOME` must be set when it is built**:
  without it the build leaves out the CUDA part and reports no error, and the decoder fails later.
- The checkout is pinned to the commit the adapter was read against.
- The last line fetches the weights now, with a progress bar. Without it the service fetches them when it
  starts, and is not ready until they are there. If the download stops, run the line again or just start the
  service: either resumes it.

### 3 · Check the install

```
set FORGE_TRIPOSG_REPO=C:\atlas\TripoSG
python tools\check_station.py
```

Eight lines, `PASS`, `NOTE` or `FAIL`, each with its reason, then the versions for `docs/versions.md`. It runs
`diso` on the card with a small sphere, the way TripoSG calls it, and cuts a test picture with `rembg` (which
downloads the matting model, about 170 MB, the first time).

| A line that fails | Likely cause |
|---|---|
| PyTorch and the graphics card | A CPU wheel (the index URL was left out), or the driver |
| the pinned packages | Something replaced `transformers` 4.49.0: install `requirements-triposg.txt` again |
| diso, TripoSG's decoder | It was built without `CUDA_HOME`, or without the compiler on the path: `pip uninstall diso`, then the three `diso` lines of step 2 again. If it still fails on the card, this is the fault of TripoSG issue 63; the Forge then uses the other decoder for every object (step 5) |
| rembg, the matting model | `requirements-gpu.txt` is not installed, or the model could not be downloaded |
| the weights (a `NOTE`, not a failure) | No `model_index.json`, a file left half-way, or under 7 GB in place: the download did not finish. The service fetches the rest when it starts |
| the TripoSG checkout | `FORGE_TRIPOSG_REPO` is not set in this prompt, or the package does not import: the message names the module that is missing |

### 4 · Start the service

```
set FORGE_TRIPOSG_REPO=C:\atlas\TripoSG
set FORGE_DEBUG_KEEP=1
uvicorn forge.app:app --host 0.0.0.0 --port 8000
```

`FORGE_DEBUG_KEEP=1` keeps the test photo and the generator's raw output on disk, which step 6 reads. Leave it
out when walkers from outside the team scan. In a second prompt:

```
curl http://127.0.0.1:8000/health
```

`generatorReady` is false while the model loads and `generatorNote` says what is happening. It must become
`true`, with `gpuName` the card and `matting` `rembg`. If it stays false, the note is the reason:

| `generatorNote` | Meaning |
|---|---|
| `the model is being loaded`, `the weights are being downloaded to …` | Wait |
| `PyTorch is not installed`, `diso is not installed …`, `no matting model is installed …`, `FORGE_TRIPOSG_REPO does not point at a TripoSG checkout` | That step of the install is missing in this environment, or the variable is not set in this prompt |
| `CUDA is not available to PyTorch` | Step 2's check line |
| `diso cannot be imported: …` | The build of `diso`; see step 3 |
| `the model is not loaded yet, and the weights are not complete (…): they are downloaded first` | No job has come yet. The first one downloads what is missing, then loads |
| `the weights could not be downloaded to …: …` | The network, or the disk. The next job tries again and resumes what is there |
| `the model could not be loaded from …: …` | The rest of the line is PyTorch's or diffusers' own error. Out of memory: close whatever else uses the card. `no file named …`, with `a download into … stopped before it was whole` behind it: the weights are not all there and the Station could not reach the hub (without a network the library hands the folder back as it is); give it a network and send a job |
| `the matting model could not be loaded: …` | `rembg` could not fetch or start its model |

### 5 · Send one object and read the job report

Take one photo of a chair that is clearly taller than it is wide: **from its front-left, at about 45°** (stand
in front of it and step to your left until you see the front and one side equally), the whole chair in the
picture, the camera at about the height of the back rest. Measure its width, height and depth with a tape.
Then, in the second prompt (`.venv\Scripts\activate` first):

```
python tools\first_run.py C:\atlas\chair_front_left.jpg --dims 0.45 0.90 0.50 --wait 900
```

It waits for the generator to be ready, posts a room of one object and the photo, prints each state of the job
with the time, the clean-up report and the validator's verdict, and saves `first_run\first_run.json`,
`first_run\asset.glb` and `first_run\raw_view0.npz`. The same by hand, with `room.json` holding
`{"version": 1, "id": "first", "objects": [{"id": 1, "category": "chair", "size": [0.45, 0.90, 0.50]}]}` and
`meta.json` holding `{"boxId": 1, "category": "chair", "dims": [0.45, 0.90, 0.50], "yaw": 0.0, "triTarget": 15000}`:

```
curl -X POST http://127.0.0.1:8000/rooms -H "Content-Type: application/json" --data-binary @room.json
curl -X POST http://127.0.0.1:8000/rooms/first/objects -F "meta=<meta.json" -F "view0=@chair.jpg;type=image/jpeg"
curl http://127.0.0.1:8000/jobs/<the jobId of the answer>
curl -o asset.glb http://127.0.0.1:8000/assets/<the sha256 of the job>.glb
python -m forge.validator asset.glb --target 15000 --box 0.45 0.90 0.50 --level full
```

(`view0` is a JPEG of at most 1 MB; `first_run.py` scales a larger photo itself.)

What to read in the answer, and in `data\jobs\<jobId>\job.json`, which also has the time of each stage:

| Member | Expect | If not |
|---|---|---|
| `state` | `ready` | `error` says why; the table below |
| `generator`, `decoder` | `triposg`, `flash` | `hierarchical`: the default decoder failed and the object was generated twice. Look for `TripoSG's flash decoder failed: …` in the service's output, and see step 3 on `diso` |
| `timings.generating` | 60 s or less (check M2 of the design) | Fewer steps: `FORGE_TRIPOSG_STEPS` (the official demo allows 8 to 50) |
| `timings.cleaning` | Well under its 60 s time-out | Write the figure down: the raw mesh may be larger than assumed |
| `report.sourceTriangles` | Not known: several hundred thousand is the guess | Write it down |
| `report.triangles` | 1,500 to 15,000 | – |
| `report.maxScaleError` | 0.10 or less | The mesh does not fill the box you measured; step 6 may show why |
| the validator | accepts it | Its codes say what is wrong |

While it generates, `nvidia-smi --query-gpu=memory.used --format=csv -l 1` in a third prompt shows the memory
in use; write down the peak.

| `error` | Meaning |
|---|---|
| `TripoSG cannot run: …` | The same reasons as `generatorNote` in step 4 |
| `generation (triposg) timed out after 90 s` | Too slow, or both decoders ran inside one time-out. Lower `FORGE_TRIPOSG_STEPS`, or raise `FORGE_GENERATE_TIMEOUT_S` to see how long it really takes |
| `TripoSG inference failed: flash decoder: …; hierarchical decoder: …` | Both decoders failed; the two reasons are TripoSG's own. `'NoneType' object has no attribute 'astype'` is what TripoSG raises when a decoder returned nothing |
| `view 0: the matte is empty …`, `view 0: background removal failed: …` | The matting model found no object in the photo, or failed on it |
| `clean-up: …`, `asset contract: [frame.bounds] …` | The mesh could not be brought to the box. Check the measures you typed. A mesh that stands askew in its box also ends here, and so does one whose up axis is not `FORGE_TRIPOSG_UP` (only that axis is tried for TripoSG, and it is `Y` until measured); step 6 still reads it |

### 6 · Measure the output frame

TripoSG does not document which way its meshes face. `first_run.py` reads it from the chair: it finds the
vertical axis (the longest), takes the end with the narrow footprint as the top (the back rest; the legs span
the seat), and looks where the back rest is. It prints a map of the mesh seen from above, like an object
profile: heights in tenths, the box's front (+Z) at the top, and then the two settings. The map is what to look
at; the settings are only as good as the chair was clear.

```
The generator's frame, read from the raw output:
        front of the box (+Z)
      4444444444444444
      4444444444444444
            …
      9999999999999999
      9999999999999999
        back of the box (-Z)
  FORGE_TRIPOSG_UP=Y
  FORGE_TRIPOSG_FRONT=canonical
```

**Up** (`FORGE_TRIPOSG_UP`):

| The tool says | Set |
|---|---|
| `its +Y end is the narrow one (the back rest)` | `Y` (the default) |
| the same with `+Z` or `+X` | `Z` or `X` |
| `the mesh stands on its head`, or it cannot tell the ends apart | Nothing: no value describes it. Leave `Y`, keep `raw_view0.npz`, and report it: clean-up needs another candidate |

**Front** (`FORGE_TRIPOSG_FRONT`), from where the 9s of the back rest are on the map:

| The map shows | Means | Set |
|---|---|---|
| The back rest straight along the bottom edge (front at about 0°) | The front of the mesh is on its own +Z, whatever side the photo was taken from | `canonical` |
| The chair standing askew, its back rest across a bottom corner (front at about ±45°) | The mesh is turned as the camera saw it | `view` |
| Anything else: the back rest along the top or a side edge, or across a top corner | A frame the Forge has no setting for | `unknown` (the default), and write down the azimuth the tool printed |

Then take a second photo **from the front-right** and run it again with `--out first_run_right`.
`canonical` must give the same map. `view` must give the mirror image: the same angle with the other sign. If
the two runs do not agree in one of these two ways, the setting is `unknown`.

The reading comes from `raw_view0.npz`, the generator's output before clean-up; that is why step 4 sets
`FORGE_DEBUG_KEEP=1`. Without the file the tool reads the GLB and takes clean-up's turn out again, which works
only when the job ended `ready`. To look at the mesh yourself, open `first_run\asset.glb` in any glTF viewer
that shows the axes: +Y is up, and the GLB's z is the box's z (its x is the box's −x). The tool sends no
profile and no camera, so clean-up orients this one mesh by its bounds and by the two settings as they stand.

**Winding** (`FORGE_TRIPOSG_INSIDE_OUT`). Clean-up, the colours and the baked shade all take it on trust
that a generated mesh's faces are wound counter-clockwise seen from outside. The tool reads it from the same
mesh, by the sign of the volume the faces enclose:

| The tool says | Means | Do |
|---|---|---|
| `the faces are wound outwards` | As clean-up expects | Nothing |
| `the faces are wound INWARDS` | The mesh is inside out. On the phone it would have one colour (no face turns to any camera) and its shade the wrong way round, dark above and light below | `set FORGE_TRIPOSG_INSIDE_OUT=1`, restart the service, run the tool again: it must now say outwards. Note which decoder made the mesh (`decoder` in the answer): the two decoders are different code, and if a mesh of the other one reads the other way, one flag cannot serve both. Report that |
| `it cannot be read from this mesh` | The mesh is not closed, or its faces are wound both ways | Look at `first_run\asset.glb` in a viewer with back faces hidden; keep `raw_view0.npz` and report it |

Set the variables in the prompt that starts the service, restart it, and then make them the defaults in
`forge/config.py` (`triposg_up`, `triposg_front`, `triposg_inside_out`) and in the table of "Running" above,
in a commit.

### 7 · Write into `docs/versions.md`

`check_station.py` prints most of it; `pip freeze > forge-station-freeze.txt` has the rest.

- The date of the run and who ran it.
- Windows version; the NVIDIA driver version (`nvidia-smi`); the Visual Studio Build Tools and CUDA Toolkit versions.
- Python; PyTorch and its CUDA build; `diso`, and whether it was built with `CUDA_HOME` set.
- `transformers`, `diffusers`, `huggingface-hub`, numpy; `rembg` and `onnxruntime` with its providers.
- The TripoSG commit; the weights folder and its size.
- Every pin that had to change in `requirements-triposg.txt` or `requirements-gpu.txt`, and why.
- `FORGE_TRIPOSG_UP` and `FORGE_TRIPOSG_FRONT` as measured, with the front azimuth of both photos; the
  winding the tool read, and for which decoder.
- The decoder that made the mesh; seconds for `matting`, `generating` and `cleaning`; the peak memory on the card;
  `sourceTriangles`.
- Anything in this section that was wrong.

Then move "TripoSG on the RTX 5070" out of that page's "Not yet verified" list.

## What is unverified

- **TripoSG** (`forge/generators/triposg.py`) has never run: the machines it was written on have no GPU and no
  PyTorch. Its calls were read against the public repository; its tests use a fake pipeline. Not known until
  the first run: whether the install works as written; whether `diso` builds and runs on the Station; the time
  and memory per object; the size of the raw mesh; and TripoSG's output frame.
- **The fallback decoder** is inferred from TripoSG's source and one open report (issue 63). That
  `use_flash_decoder=False` works where the default fails is untested.
- **rembg** was tried once on the Mac's processor, on TripoSG's example pictures put on a plain background: it
  cut them in 0.2 s each and the framing was right. It was not tried on the Station, with `onnxruntime-gpu`, or on
  a photo of a room. The tests use synthetic pictures and mattes.
- **The requirement lists** were resolved for Windows and installed once on macOS, never on Windows and never
  with the CUDA wheels. `diso` was never built.
- **The fit, the colours and the light copy** have been run on built shapes only: the procedural furniture,
  boxes and a lumpy sphere, with profiles made from those same shapes and photos rendered here. On such
  input a turned mesh is turned back, two colours land on the right faces and both copies validate. How the
  fit behaves on a real generated mesh against a real scan (thin coverage, a profile from a noisy LiDAR mesh,
  a generated shape that only resembles the object) is not known; the thresholds 0.02 and 0.20 are starting
  values.
- **Winding.** Whether TripoSG's faces are wound outwards is not known, for either of its decoders. The first
  run reads it; `FORGE_TRIPOSG_INSIDE_OUT` is there in case they are not.
- **The weights download.** How the pinned `huggingface_hub` leaves a folder when a download stops was read in
  its source and seen once with a small public repository. The adapter's handling of it is tested with a fake;
  the 7.95 GB download itself was never made.
- **Up.** A scan seen from above cannot tell an object from the same object standing on its head, and no
  negative up axis is among the candidates. For TripoSG only `FORGE_TRIPOSG_UP` is tried, `Y` until measured.
- **On the phone**: glTFast loading the file with no material, and the palette cell and shade arriving as lane A's
  shader expects, can only be checked in Unity.
