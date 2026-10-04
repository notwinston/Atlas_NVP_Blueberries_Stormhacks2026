# The Forge

The Station service that turns object photos into furniture meshes (Master Plan §4.5 and §7.4, tasks `P1-D08`
and `P1-D09`). The phone uploads a RoomModel and, for each scanned object, one to three cropped photos with the
object's box. The Forge makes a mesh from the photo, cleans it until it meets the asset contract (§6.4) and
serves it as a GLB named by its SHA-256.

```
station/forge/
  forge/app.py            HTTP interface (Appendix B.4)
  forge/service.py        rooms, jobs, the queue and its one worker, the job folder
  forge/cleanup.py        mesh clean-up (P1-D09)
  forge/validator.py      the asset validator, library and command line
  forge/palette.py        palette cells and the vertex colour encoding (lane A §10.2, §10.7)
  forge/glb.py            GLB writer and reader
  forge/generators/       triposg.py · hosted.py · procedural.py
  forge/budget.py         the furniture budget (20 meshes, 60,000 triangles)
  tests/                  pytest suite; needs no GPU
```

## Set-up on the Station

Python 3.11, NVIDIA driver with CUDA 12.8 (Appendix C.2, C.7).

```
cd station/forge
python -m venv .venv
.venv\Scripts\activate                      # Linux and macOS: source .venv/bin/activate
pip install -r requirements.lock

# The generator, Station only
pip install torch torchvision --index-url https://download.pytorch.org/whl/cu128
python -c "import torch; print(torch.__version__, torch.cuda.is_available())"     # must print True
git clone https://github.com/VAST-AI-Research/TripoSG  C:\atlas\TripoSG
pip install -r C:\atlas\TripoSG\requirements.txt
pip install -r requirements-gpu.txt
set FORGE_TRIPOSG_REPO=C:\atlas\TripoSG
```

Write the installed PyTorch version and the TripoSG commit hash into `docs/versions.md`. The TripoSG weights
(`VAST-AI/TripoSG` on Hugging Face) download on the first job unless `FORGE_TRIPOSG_WEIGHTS` points at a copy.

On a machine with no GPU, `pip install -r requirements.lock` is all that is needed; run with
`FORGE_GENERATOR=procedural`.

## Running

```
uvicorn forge.app:app --host 0.0.0.0 --port 8000
python -m pytest tests                       # the test suite
python -m forge.validator asset.glb --target 5000 --box 1.0 0.75 0.6
```

One process only: it owns the GPU. Start it from `station/forge`.

| Variable | Default | Meaning |
|---|---|---|
| `FORGE_GENERATOR` | `triposg` | `triposg`, `procedural` or `hosted` |
| `FORGE_DATA_DIR` | `station/forge/data` | Rooms, jobs, assets and the cache |
| `FORGE_DEBUG_KEEP` | off | `1` keeps the crops on disk. Team sessions only; `GET /health` reports it |
| `FORGE_HUB_URL` | `http://127.0.0.1:8080/internal/job` | Where job states are posted; empty switches it off |
| `FORGE_PUBLIC_URL` | empty | Prefix of the asset URL in `AssetReady`, e.g. `http://192.168.8.2:8000` |
| `FORGE_HOSTED_ENABLED`, `FORGE_HOSTED_URL`, `FORGE_HOSTED_KEY` | off | The hosted fallback; see below |
| `FORGE_TRIPOSG_REPO`, `FORGE_TRIPOSG_WEIGHTS` | empty | TripoSG checkout and weights folder |
| `FORGE_TRIPOSG_STEPS`, `_GUIDANCE`, `_SEED`, `_FP16` | 50, 7.0, 42, on | Inference settings |
| `FORGE_MATTING_TIMEOUT_S`, `FORGE_GENERATE_TIMEOUT_S`, `FORGE_HOSTED_TIMEOUT_S`, `FORGE_CLEANING_TIMEOUT_S` | 10, 90, 120, 20 | Stage time-outs (§7.4) |
| `FORGE_MAX_ANISOTROPY` | 0.15 | Stretch allowed per axis when fitting a mesh to its box |

The port and host are uvicorn's own arguments. `forge/config.py` holds every setting.

## Routes

| Route | Request | Response |
|---|---|---|
| `GET /health` | – | `{ok, gpu, queue, pipelineVer, debugKeep}` and `gpuName`, `generator`, `generatorReady`, `generatorNote`, `hostedEnabled`, `matting` |
| `POST /rooms` | RoomModel JSON | 201 `{roomId, roomHash, generated, placeholders, triTarget}`. `roomId` is the RoomModel's `id` |
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

## The queue and the furniture budget

- One worker owns the generator and runs one job at a time, the largest box first.
- When a room is uploaded the Forge ranks its boxes by volume. The 20 largest receive meshes. A job for any
  other box is answered with `placeholder: true` and is never generated.
- The triangle target is clamp(60,000 ÷ n, 3,000, 5,000) for the n boxes that receive meshes. Clean-up works to
  the smaller of this and the `triTarget` sent with the job.
- A second upload for a box whose job is still waiting or running returns that job.
- A generation that fails or times out is tried once more with view 1, then with the hosted service if it is
  switched on. A job whose mesh cannot meet the asset contract fails; the phone then places the built-in mesh.
- Crops are held in memory and dropped when the job ends. After a restart finished jobs, rooms and assets are
  still there; a job that was waiting or running is marked failed (its crops are gone) and the phone sends it
  again. With `FORGE_DEBUG_KEEP=1` the crops are on disk and such a job runs again by itself.

## Generators

| `FORGE_GENERATOR` | What it is |
|---|---|
| `triposg` | TripoSG on the Station's GPU. The default. **Unverified**, see below |
| `procedural` | Builds a plausible mesh for the category from the box size alone (table, chair, couch, bed, storage, lamp, plant, screen, stairs, basin, toilet, plain box). Deterministic, no GPU. For tests and for running the whole round trip without the Station's graphics card. It does not look at the photo, except for the colour |
| `hosted` | A third-party service only. The interface exists; **no vendor is wired** (Master Plan §14, open item 8) |

**The hosted fallback sends the walker's crop to a third party** whose retention the team does not control. It is
off unless `FORGE_HOSTED_ENABLED=1`. `GET /health` shows `hostedEnabled`, every job that used it reports
`source: "hosted"`, and each use is logged. The walker must be told before the scan (§7.4, Privacy). A job sent
with `backend: "cache"` never reaches any generator.

## Clean-up and the asset contract

Clean-up welds the mesh, drops detached fragments, picks the up axis and the quarter-turn yaw whose bounds best
match the box, scales to the box (at most ±15 % of stretch per axis), puts the origin at the centre of the base,
decimates to the triangle target, splits edges until none is longer than 0.25 m, picks the nearest object colour
of the palette (cells 64–127) to the photo's dominant colour, and bakes face shade × ambient occlusion into the
vertex colour. The same input gives the same bytes.

The GLB holds one mesh, one primitive, no material and no texture: `POSITION`, `NORMAL`, `TEXCOORD_0` (the
palette cell, written as (u, 1 − v)), `COLOR_0` (R = shade, G = B = A = 255), 16-bit indices. It is a
right-handed glTF; the importer on the phone inverts X.

The validator (`forge/validator.py`) rejects: more than one mesh, primitive or node; any material, texture,
image or sampler; a node transform; fewer than 1,500 or more than 5,000 triangles; more triangles than the
job's target; an edge over 0.25 m; a base that is not on y = 0 or not centred; with `--box`, any side more than
10 % off the box (wrong scale, units or up axis); a file over 1 MB. Every job's output is validated before it is
published.

## What is unverified

- **TripoSG** (`forge/generators/triposg.py`) was written from the public repository's inference script and has
  never run: the machine it was written on has no GPU and no PyTorch. On the Station check, in this order: the
  import of `triposg.pipelines.pipeline_triposg.TripoSGPipeline`; `from_pretrained(...).to("cuda", float16)`;
  the call `pipe(image=..., generator=..., num_inference_steps=..., guidance_scale=...).samples[0]` returning
  `(vertices, faces)`; the time per object (≤ 60 s proposed, `P1-D08`); the up axis TripoSG uses.
- **rembg** is an optional import. Without it crops go to the generator unmatted and `GET /health` shows
  `matting: "none"`. It was not installed here.
- **Up and front.** Bounds cannot tell a mesh from the same mesh turned by 180° or standing on its head, so the
  generator's own up direction is trusted unless another axis fits the box clearly better.
- **On the phone**: glTFast loading the file with no material, and the palette cell and shade arriving as lane A's
  shader expects, can only be checked in Unity.
