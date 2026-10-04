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
  forge/cleanup.py        mesh clean-up (P1-D09)
  forge/validator.py      the asset validator, library and command line; two levels
  forge/palette.py        palette cells and the vertex colour encoding (lane A §10.2, §10.7)
  forge/glb.py            GLB writer and reader
  forge/generators/       triposg.py · hosted.py · procedural.py
  forge/budget.py         the furniture budget (32 meshes, 120,000 triangles)
  tools/check_station.py  checks what TripoSG needs on the Station
  tools/first_run.py      sends one object through a running Forge and reads the result
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
```

One process only: it owns the GPU. Start it from `station/forge`.

| Variable | Default | Meaning |
|---|---|---|
| `FORGE_GENERATOR` | `triposg` | `triposg`, `procedural` or `hosted` |
| `FORGE_DATA_DIR` | `station/forge/data` | Rooms, jobs, assets and the cache |
| `FORGE_DEBUG_KEEP` | off | `1` keeps each job's crops and the generator's raw output on disk. Team sessions only; `GET /health` reports it |
| `FORGE_HUB_URL` | `http://127.0.0.1:8080/internal/job` | Where job states are posted; empty switches it off |
| `FORGE_PUBLIC_URL` | empty | Prefix of the asset URL in `AssetReady`, e.g. `http://192.168.8.2:8000` |
| `FORGE_HOSTED_ENABLED`, `FORGE_HOSTED_URL`, `FORGE_HOSTED_KEY` | off | The hosted fallback; see below |
| `FORGE_TRIPOSG_REPO`, `FORGE_TRIPOSG_WEIGHTS` | empty | TripoSG checkout and weights folder (`<checkout>/pretrained_weights/TripoSG` when empty) |
| `FORGE_TRIPOSG_STEPS`, `_GUIDANCE`, `_SEED`, `_FP16` | 50, 7.0, 42, on | Inference settings |
| `FORGE_TRIPOSG_FALLBACK_DECODER` | on | When TripoSG's default decoder fails, generate once more with `use_flash_decoder=False` |
| `FORGE_TRIPOSG_UP` | `Y` | Which axis of TripoSG's own frame is up: `Y`, `Z` or `X`. Measured on the Station (below) |
| `FORGE_TRIPOSG_FRONT` | `unknown` | Where the front of a TripoSG mesh is: `canonical` (on its own +Z), `view` (towards the photo's camera) or `unknown`. Measured on the Station (below) |
| `FORGE_POOR_FIT` | 0.20 | A fit to the scanned profile above this is poor, in fractions of the box height |
| `FORGE_MATTING_TIMEOUT_S`, `FORGE_GENERATE_TIMEOUT_S`, `FORGE_HOSTED_TIMEOUT_S`, `FORGE_CLEANING_TIMEOUT_S` | 10, 90, 120, 60 | Stage time-outs (§7.4) |
| `FORGE_MAX_ANISOTROPY` | 0.15 | Stretch allowed per axis when fitting a mesh to its box |

`FORGE_TRIPOSG_UP`, `FORGE_TRIPOSG_FRONT` and `FORGE_POOR_FIT` are read and checked at start-up and are not used
yet: they belong to the fit to the scan (mesh design §5.3), which clean-up does not do yet.

The port and host are uvicorn's own arguments. `forge/config.py` holds every setting.

## Routes

| Route | Request | Response |
|---|---|---|
| `GET /health` | – | `{ok, gpu, queue, pipelineVer, debugKeep}` and `gpuName`, `generator`, `generatorReady`, `generatorNote`, `hosted` (and the same flag as `hostedEnabled`), `matting`. Answers at once, also while the model loads |
| `POST /rooms` | RoomModel JSON | 201 `{roomId, roomHash, generated, placeholders, triTarget, triTargets, triTargetLight}`. `roomId` is the RoomModel's `id`. `triTargets` maps every object id to its full target (0 for a box that receives no mesh); `triTarget` is the largest of them; `triTargetLight` is the room's light target |
| `PUT /rooms/{roomId}/mesh` | binary, ≤ 2 MB | 204 |
| `GET /rooms/{roomId}` | – | The RoomModel as uploaded |
| `DELETE /rooms/{roomId}` | – | 204. Removes the room, its mesh, its jobs and the GLBs made for it |
| `POST /rooms/{roomId}/objects` | multipart: `meta` (JSON, Appendix B.4), files `view0`…`view2` | 202 `{jobId}` · 200 `{jobId, cached: true, sha256}` · 200 `{jobId, placeholder: true}` |
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
TripoSG). When ready it also holds `sha256`, `url`, `bytes`, `tris`, `maxEdge`, `bounds`, `palette` and
`report`. Its members for the light copy (`lightSha256`, `lightUrl`, `lightBytes`, `lightTris`) and for the fit
(`fit`, `fitYaw`, `fitUp`, `attempts`) are defined and absent: clean-up makes neither yet.

## The queue and the furniture budget

- One worker owns the generator and runs one job at a time, the largest box first.
- When a room is uploaded the Forge ranks its boxes by volume. The 32 largest receive meshes. A job for any
  other box is answered with `placeholder: true` and is never generated.
- The room's 120,000 furniture triangles are shared between those boxes by surface area, 3,000 to 15,000 each
  (mesh design §5.4; `forge/budget.py`, in whole numbers so that the phone and `tools/rooms/roommath.py` give
  the same figures). The light target is clamp(60,000 ÷ n, 1,500, 5,000), the same for every mesh of the room.
  Clean-up works to the smaller of the box's target and the `triTarget` sent with the job.
- A second upload for a box whose job is still waiting or running returns that job.
- A generation that fails or times out is tried once more with view 1, then with the hosted service if it is
  switched on. A job whose mesh cannot meet the asset contract fails; the phone then keeps its first shape.
- Crops, and the mattes made from them, are held in memory and dropped when the job ends. After a restart
  finished jobs, rooms and assets are still there; a job that was waiting or running is marked failed (its
  crops are gone) and the phone sends it again. With `FORGE_DEBUG_KEEP=1` the crops are on disk and such a job
  runs again by itself; the generator's raw output is kept beside them as `raw_view<i>.npz`.

## Generators

| `FORGE_GENERATOR` | What it is |
|---|---|
| `triposg` | TripoSG on the Station's GPU. The default. **It has never run**, see below |
| `procedural` | Builds a plausible mesh for the category from the box size alone (table, chair, couch, bed, storage, lamp, plant, screen, stairs, basin, toilet, plain box). Deterministic, no GPU. For tests and for running the whole round trip without the Station's graphics card. It does not look at the photo, except for the colour |
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
- **The weights** are complete when their folder holds `model_index.json`. A folder without it is what a
  download that stopped leaves behind, and is downloaded again.
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

Clean-up welds the mesh, drops detached fragments, picks the up axis and the quarter-turn yaw whose bounds best
match the box, scales to the box (at most ±15 % of stretch per axis), puts the origin at the centre of the base,
decimates to the triangle target, splits edges until none is longer than 0.25 m, picks the nearest object colour
of the palette (cells 64–127) to the photo's dominant colour, and bakes face shade × ambient occlusion into the
vertex colour. The same input gives the same bytes. It does not yet orient by the scan, colour per face or make
the light copy (mesh design §5.3).

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
cells. Every job's output is validated at the full level before it is published.

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
  starts, and is not ready until they are there.

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
| `the model could not be loaded from …: …` | The rest of the line is PyTorch's or diffusers' own error. Out of memory: close whatever else uses the card |
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
| `clean-up: …`, `asset contract: [frame.bounds] …` | The mesh could not be brought to the box. Check the measures you typed; a mesh that stands askew in its box also ends here, and step 6 still reads it |

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
`FORGE_DEBUG_KEEP=1`. Without the file the tool reads the GLB and takes clean-up's quarter turn out again,
which works only when the job ended `ready`. To look at the mesh yourself, open `first_run\asset.glb` in any
glTF viewer that shows the axes: +Y is up, and the GLB's z is the box's z (its x is the box's −x).

Set the two variables in the prompt that starts the service, restart it, and then make them the defaults in
`forge/config.py` (`triposg_up`, `triposg_front`) and in the table of "Running" above, in a commit.

### 7 · Write into `docs/versions.md`

`check_station.py` prints most of it; `pip freeze > forge-station-freeze.txt` has the rest.

- The date of the run and who ran it.
- Windows version; the NVIDIA driver version (`nvidia-smi`); the Visual Studio Build Tools and CUDA Toolkit versions.
- Python; PyTorch and its CUDA build; `diso`, and whether it was built with `CUDA_HOME` set.
- `transformers`, `diffusers`, `huggingface-hub`, numpy; `rembg` and `onnxruntime` with its providers.
- The TripoSG commit; the weights folder and its size.
- Every pin that had to change in `requirements-triposg.txt` or `requirements-gpu.txt`, and why.
- `FORGE_TRIPOSG_UP` and `FORGE_TRIPOSG_FRONT` as measured, with the front azimuth of both photos.
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
- **Up and front.** Bounds cannot tell a mesh from the same mesh turned by 180° or standing on its head, so the
  generator's own up direction is trusted unless another axis fits the box clearly better. The fit to the scan
  that settles it is not built yet.
- **On the phone**: glTFast loading the file with no material, and the palette cell and shade arriving as lane A's
  shader expects, can only be checked in Unity.
