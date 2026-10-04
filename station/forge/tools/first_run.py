"""Send one object through a running Forge and read what comes back (README, "First run on the Station").

    python tools/first_run.py chair.jpg --dims 0.45 0.90 0.50
    python tools/first_run.py --analyse first_run/asset.glb

It posts a room of one object and one photo, follows the job, saves the answer and the GLB,
runs the asset validator on it, and reads the generator's frame from the mesh: which axis is
up, and where the back of the chair is. The reading is what `FORGE_TRIPOSG_UP` and
`FORGE_TRIPOSG_FRONT` are set from, so the photo must be of a chair, clearly taller than it is
wide, seen from the front and from one side at once, at about 45 degrees.

With FORGE_DEBUG_KEEP=1 on the service, and this script on the same machine, the frame is read
from the generator's raw output (`data/jobs/<jobId>/raw_view0.npz`), which also exists when
clean-up rejected the mesh. Otherwise it is read from the GLB and the clean-up report.

Runs in the Forge's environment (numpy and Pillow); talks to the service over HTTP only.
Its reading was tested on built meshes. It has never read a mesh that TripoSG made.
"""
from __future__ import annotations

import argparse
import io
import json
import math
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path
from typing import Optional

import numpy as np

FORGE_DIR = Path(__file__).resolve().parent.parent
if str(FORGE_DIR) not in sys.path:
    sys.path.insert(0, str(FORGE_DIR))

from forge.cleanup import _UP  # noqa: E402  (the rotations FORGE_TRIPOSG_UP names)
from forge.glb import read_glb  # noqa: E402
from forge.validator import validate_glb  # noqa: E402

MAX_CROP_BYTES = 1024 * 1024  # the Forge's limit for one view
BOUNDARY = "atlas-forge-first-run-7c1e42a9"
GRID = 16  # the map is drawn like an object profile: 16 x 16 cells over the footprint

# How the reading is taken (all of it is a starting point until a real mesh has been read).
TALL_ENOUGH = 1.15  # the test object must be this much taller than it is wide or deep, also when it stands askew
END_SHARE = 0.30  # the top and the bottom of the object: this share of its height each
BACK_REST = 0.6  # upright when the top's footprint is under this share of the bottom's
OFF_CENTRE = 0.30  # a back rest sits at least this far from the middle, in half-depths
CANONICAL_WITHIN = 20.0  # degrees between the front and +Z
VIEW_FROM, VIEW_TO = 25.0, 65.0  # a front this far from +Z is the photo's own 45 degrees


# --------------------------------------------------------------------------- the reading


def _samples(vertices: np.ndarray, faces: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Points spread over the surface, and the area each stands for.

    A generator's mesh is dense and its faces' middles are enough. A mesh of a few large faces is
    sampled inside each face, finely enough for the 16 x 16 map.
    """
    v = np.asarray(vertices, dtype=np.float64)
    f = np.asarray(faces, dtype=np.int64).reshape(-1, 3)
    a, b, c = v[f[:, 0]], v[f[:, 1]], v[f[:, 2]]
    area = 0.5 * np.linalg.norm(np.cross(b - a, c - a), axis=1)
    longest = max(np.linalg.norm(b - a, axis=1).max(), np.linalg.norm(c - b, axis=1).max(),
                  np.linalg.norm(a - c, axis=1).max())
    cell = float((v[f].reshape(-1, 3).max(axis=0) - v[f].reshape(-1, 3).min(axis=0)).min()) / (2 * GRID)
    steps = int(np.clip(np.ceil(longest / max(cell, 1e-9)), 1, 32))
    while steps > 1 and len(f) * (steps + 1) * (steps + 2) // 2 > 2_000_000:
        steps -= 1
    if steps == 1:
        return (a + b + c) / 3.0, area
    i, j = np.meshgrid(np.arange(steps + 1), np.arange(steps + 1), indexing="ij")
    inside = (i + j) <= steps
    u, w = i[inside] / steps, j[inside] / steps
    points = a[:, None, :] * (1.0 - u - w)[None, :, None] + b[:, None, :] * u[None, :, None] + c[:, None, :] * w[None, :, None]
    return points.reshape(-1, 3), np.repeat(area / len(u), len(u))


def _footprint_area(points: np.ndarray, axes: tuple[int, int]) -> float:
    if len(points) == 0:
        return 0.0
    span = points[:, list(axes)].max(axis=0) - points[:, list(axes)].min(axis=0)
    return float(span[0] * span[1])


def find_up(points: np.ndarray) -> tuple[str, int, str]:
    """``(axis, sign, why)``: the axis of the mesh's own frame that is vertical, and which end is the top.

    The vertical axis is the longest one. The top is the end with the smaller footprint: a chair's
    back rest is a slab, its legs span the whole seat. ``sign`` is 0 when the two ends do not differ
    enough to say.
    """
    extent = points.max(axis=0) - points.min(axis=0)
    axis = int(np.argmax(extent))
    others = tuple(k for k in range(3) if k != axis)
    name = "XYZ"[axis]
    if extent[axis] < TALL_ENOUGH * max(extent[others[0]], extent[others[1]]):
        return name, 0, (f"the mesh is {extent[0]:.2f} x {extent[1]:.2f} x {extent[2]:.2f}: no axis is clearly "
                         "the longest, so up cannot be read from it. Use a taller object")
    t = (points[:, axis] - points[:, axis].min()) / extent[axis]
    high = _footprint_area(points[t > 1.0 - END_SHARE], others)
    low = _footprint_area(points[t < END_SHARE], others)
    if high <= BACK_REST * low:
        return name, 1, f"its +{name} end is the narrow one (the back rest), its -{name} end the wide one (the legs)"
    if low <= BACK_REST * high:
        return name, -1, f"its -{name} end is the narrow one (the back rest): the mesh stands on its head"
    return name, 0, (f"the two ends along {name} are about as wide as each other ({high:.3f} and {low:.3f}): "
                     "which is the top cannot be read. Look at the mesh")


def upright(points: np.ndarray, axis: str) -> np.ndarray:
    """The mesh turned so that ``axis`` of its own frame is +Y, by the rotation clean-up uses for it."""
    rotation = dict(_UP)[axis]
    return points @ rotation.T


def height_map(points: np.ndarray) -> np.ndarray:
    """16 x 16 heights (0 to 1, -1 where nothing is) over the footprint, in Unity's box frame.

    ``points`` are upright and right-handed, as a GLB is. Unity's local x is the negative of the
    GLB's (brief S8), so the columns run along -x of the points. Row 0 is the lowest z.
    """
    lo, hi = points.min(axis=0), points.max(axis=0)
    size = np.maximum(hi - lo, 1e-9)
    column = np.minimum(((hi[0] - points[:, 0]) / size[0] * GRID).astype(int), GRID - 1)
    row = np.minimum(((points[:, 2] - lo[2]) / size[2] * GRID).astype(int), GRID - 1)
    grid = np.full((GRID, GRID), -1.0)
    np.maximum.at(grid, (row, column), (points[:, 1] - lo[1]) / size[1])
    return grid


def draw(grid: np.ndarray) -> str:
    """The height map as text, seen from above: the box's front (+Z) at the top, its +X to the right."""
    lines = ["        front of the box (+Z)"]
    for row in range(GRID - 1, -1, -1):
        cells = "".join("." if h < 0 else str(min(9, int(h * 10))) for h in grid[row])
        lines.append(("  -X  " if row == GRID // 2 else "      ") + cells + ("  +X" if row == GRID // 2 else ""))
    lines.append("        back of the box (-Z)")
    lines.append("      (heights in tenths of the object's height; 9 is the top, . is empty)")
    return "\n".join(lines)


def find_front(points: np.ndarray, weights: np.ndarray) -> tuple[Optional[float], str]:
    """``(azimuth of the front in degrees, why)`` of an upright mesh, in Unity's box frame.

    Azimuth 0 is +Z, 90 is +X (atan2(x, z), as the views' `azimuthDeg`). The front is opposite the
    back rest, which is where the top of the mesh is. None when the top sits over the middle.
    """
    lo, hi = points.min(axis=0), points.max(axis=0)
    size = np.maximum(hi - lo, 1e-9)
    high = points[:, 1] > lo[1] + (1.0 - END_SHARE) * size[1]
    top = np.average(points[high], axis=0, weights=weights[high])
    centre = (lo + hi) / 2.0
    # In half-widths and half-depths, so a deep seat and a shallow one read alike. Unity's x is -x.
    x = -(top[0] - centre[0]) / (size[0] / 2.0)
    z = (top[2] - centre[2]) / (size[2] / 2.0)
    if math.hypot(x, z) < OFF_CENTRE:
        return None, f"the top of the mesh sits over its middle ({x:+.2f}, {z:+.2f}): no back rest was found"
    back = math.degrees(math.atan2(x, z))
    front = (back + 360.0) % 360.0 - 180.0
    return front, f"the back rest is towards azimuth {back:+.0f} (x {x:+.2f}, z {z:+.2f}), so the front is at {front:+.0f}"


def read_frame(vertices: np.ndarray, faces: np.ndarray, up_axis: Optional[str] = None, yaw_degrees: int = 0) -> dict:
    """Read the generator's frame from a chair mesh.

    For the generator's raw output leave ``up_axis`` None: up is found from the mesh. For a GLB
    pass the clean-up report's `upAxis` and `yawDegrees`: the file is upright already, and the
    quarter turn clean-up gave it is taken out again before the front is read.
    """
    points, weights = _samples(vertices, faces)
    reading: dict = {"source": "raw" if up_axis is None else "glb", "notes": []}
    if up_axis is None:
        axis, sign, why = find_up(points)
        reading["notes"].append(f"up: {why}")
        if sign <= 0:
            reading.update(up=None, front=None, frontAzimuth=None)
            reading["notes"].append("no value of FORGE_TRIPOSG_UP describes this; leave it at Y and write down what you see")
            return reading
        points = upright(points, axis)
    else:
        axis = up_axis
        vertical, sign, why = find_up(points)
        reading["notes"].append(f"up: clean-up took {axis} of the generator's frame as up; in the file {why}")
        if sign <= 0 or vertical != "Y":
            reading.update(up=None, front=None, frontAzimuth=None)
            reading["notes"].append("the file is not an upright chair; no value of FORGE_TRIPOSG_UP describes this")
            return reading
        if yaw_degrees == 90:
            # Clean-up turned the mesh by a quarter about +Y (x' = z, z' = -x): turn it back.
            points = np.stack([-points[:, 2], points[:, 1], points[:, 0]], axis=1)
    reading["up"] = axis
    reading["map"] = draw(height_map(points))
    front, why = find_front(points, weights)
    reading["notes"].append(f"front: {why}")
    reading["frontAzimuth"] = None if front is None else round(front, 1)
    if front is None:
        reading["front"] = "unknown"
    elif abs(front) <= CANONICAL_WITHIN:
        reading["front"] = "canonical"
        reading["notes"].append("the front is on the mesh's own +Z, whatever side the photo was taken from")
    elif VIEW_FROM <= abs(front) <= VIEW_TO:
        reading["front"] = "view"
        reading["notes"].append("the front is about 45 degrees off +Z, as in the photo: the mesh is turned as the camera "
                                "saw it. A second photo from the front-right must give the same angle with the other sign")
    else:
        reading["front"] = "unknown"
        reading["notes"].append("the front is neither on +Z nor where the photo's camera stood: the Forge has no setting "
                                "for this; leave FORGE_TRIPOSG_FRONT at unknown and write the azimuth down")
    return reading


def print_reading(reading: dict) -> None:
    print(f"\nThe generator's frame, read from the {'raw output' if reading['source'] == 'raw' else 'GLB'}:")
    if reading.get("map"):
        print(reading["map"])
    for note in reading["notes"]:
        print("  - " + note)
    if reading.get("up"):
        print(f"\n  FORGE_TRIPOSG_UP={reading['up']}")
        print(f"  FORGE_TRIPOSG_FRONT={reading['front']}")


def analyse_file(path: Path, up_axis: Optional[str] = None, yaw_degrees: int = 0) -> dict:
    """Read the frame from a raw `.npz` of the Forge's job folder, or from a `.glb` (with the report's values)."""
    if path.suffix == ".npz":
        with np.load(path) as raw:
            return read_frame(raw["vertices"], raw["faces"])
    glb = read_glb(path.read_bytes())
    primitive = glb.doc["meshes"][0]["primitives"][0]
    positions = np.asarray(glb.accessor(primitive["attributes"]["POSITION"]), dtype=np.float64)
    faces = np.asarray(glb.accessor(primitive["indices"]), dtype=np.int64).reshape(-1, 3)
    return read_frame(positions, faces, up_axis or "Y", yaw_degrees)


# --------------------------------------------------------------------------- the round trip


def call(method: str, url: str, body: Optional[bytes] = None, content_type: str = "", timeout: float = 30.0):
    """``(status, bytes)``; a refused connection is status 0."""
    request = urllib.request.Request(url, data=body, method=method)
    if content_type:
        request.add_header("Content-Type", content_type)
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.read()
    except (urllib.error.URLError, OSError) as error:
        return 0, str(error).encode()


def get_json(url: str):
    status, body = call("GET", url)
    try:
        return status, json.loads(body)
    except ValueError:
        return status, {"detail": body.decode("utf-8", "replace")[:300]}


def prepare_photo(path: Path) -> bytes:
    """The photo as a JPEG the Forge takes: at most 1 MB. A larger or other file is scaled and encoded again."""
    data = path.read_bytes()
    if len(data) <= MAX_CROP_BYTES and data[:2] == b"\xff\xd8":
        return data
    from PIL import Image, ImageOps

    image = ImageOps.exif_transpose(Image.open(io.BytesIO(data))).convert("RGB")
    image.thumbnail((1024, 1024))
    for quality in (90, 80, 70, 60):
        out = io.BytesIO()
        image.save(out, format="JPEG", quality=quality)
        if out.tell() <= MAX_CROP_BYTES:
            break
    print(f"the photo was scaled to {image.size[0]} x {image.size[1]} and encoded again: {out.tell()} bytes")
    return out.getvalue()


def multipart(meta: dict, photo: bytes) -> bytes:
    def part(name: str, head: str, body: bytes) -> bytes:
        return (f"--{BOUNDARY}\r\nContent-Disposition: form-data; name=\"{name}\"{head}\r\n\r\n").encode() + body + b"\r\n"

    return (part("meta", "", json.dumps(meta).encode())
            + part("view0", '; filename="view0.jpg"\r\nContent-Type: image/jpeg', photo)
            + f"--{BOUNDARY}--\r\n".encode())


def wait_until_ready(url: str, seconds: float) -> Optional[dict]:
    """The health answer once the generator is ready; None when it is not within ``seconds``."""
    deadline, last = time.monotonic() + seconds, None
    while True:
        status, health = get_json(url + "/health")
        if status != 200:
            print(f"GET /health: no answer from {url} ({health.get('detail', status)}). Is the service running?")
            return None
        note = health.get("generatorNote", "")
        if health.get("generatorReady"):
            return health
        if note != last:
            print(f"the generator is not ready: {note}")
            last = note
        if time.monotonic() >= deadline:
            return None
        time.sleep(2.0)


def run(args: argparse.Namespace) -> int:
    url = args.url.rstrip("/")
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    record: dict = {"url": url, "photo": str(args.photo), "dims": args.dims, "category": args.category}

    health = wait_until_ready(url, args.wait)
    if health is None:
        print("stopped: the generator is not ready. GET /health says why; the README's run-book lists what each note means.")
        return 2
    record["health"] = health
    print(f"health: generator {health['generator']} ready on {health.get('gpuName') or 'no GPU'}, "
          f"matting {health['matting']}, pipeline {health['pipelineVer']}, debugKeep {health['debugKeep']}")

    w, h, d = args.dims
    room_id = f"firstrun_{int(time.time())}"
    room = {"version": 1, "id": room_id, "createdUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "objects": [{"id": 1, "category": args.category, "base": [0.0, 0.0, 0.0], "size": [w, h, d], "yaw": 0.0,
                         "mesh": "placeholder", "assetHash": "", "triTarget": 0}]}
    status, body = call("POST", url + "/rooms", json.dumps(room).encode(), "application/json")
    if status != 201:
        print(f"POST /rooms: {status} {body[:300]!r}")
        return 1
    created = json.loads(body)
    target = min(args.target, created["triTargets"]["1"])
    print(f"room {room_id}: full target {created['triTargets']['1']}, light target {created['triTargetLight']}")

    # No profile and no camera pose: clean-up then orients by the bounds alone, which is what is being read.
    meta = {"boxId": 1, "category": args.category, "dims": [w, h, d], "yaw": 0.0, "triTarget": target,
            "backend": "local"}
    started = time.monotonic()
    status, body = call("POST", f"{url}/rooms/{room_id}/objects", multipart(meta, prepare_photo(Path(args.photo))),
                        f"multipart/form-data; boundary={BOUNDARY}")
    if status not in (200, 202):
        print(f"POST /rooms/{room_id}/objects: {status} {body[:300]!r}")
        return 1
    job_id = json.loads(body)["jobId"]
    print(f"job {job_id}")

    seen, since, stages = None, started, {}
    while True:
        status, job = get_json(f"{url}/jobs/{job_id}")
        if status != 200:
            print(f"GET /jobs/{job_id}: {status} {job}")
            return 1
        now = time.monotonic()
        if job["state"] != seen:
            if seen is not None:
                stages[seen] = round(now - since, 1)
            print(f"  {now - started:6.1f} s  {job['state']}")
            seen, since = job["state"], now
        if job["state"] in ("ready", "failed", "placeholder"):
            break
        if now - started > args.timeout:
            print(f"stopped waiting after {args.timeout:g} s; the job is still {job['state']}")
            return 1
        time.sleep(0.25)
    record.update(job=job, seconds=round(time.monotonic() - started, 1), stages=stages)
    print(f"the job took {record['seconds']} s as seen from here: {stages}")
    print("its own record, with the time of each stage: " + str(Path(args.data) / "jobs" / job_id / "job.json"))

    result = 0
    if job["state"] == "ready":
        print(f"generator {job['generator']}" + (f", decoder {job['decoder']}" if job.get("decoder") else ""))
        print("report: " + json.dumps(job["report"], indent=1))
        asset_url = job["url"] if job["url"].startswith("http") else url + job["url"]
        status, glb = call("GET", asset_url, timeout=60)
        if status != 200:
            print(f"GET {asset_url}: {status}")
            return 1
        (out / "asset.glb").write_bytes(glb)
        verdict = validate_glb(glb, tri_target=job["triTarget"], box=args.dims, level="full")
        record["validator"] = verdict.to_dict()
        print(f"asset.glb: {verdict.triangles} triangles, {len(glb)} bytes; the validator "
              + ("accepts it" if verdict.ok else "rejects it: " + "; ".join(f"[{v.code}] {v.message}" for v in verdict.violations)))
        result = 0 if verdict.ok else 1
    else:
        print(f"the job ended as {job['state']}: {job.get('error')}")
        result = 1

    raw = Path(args.data) / "jobs" / job_id / "raw_view0.npz"
    reading = None
    if raw.is_file():
        (out / "raw_view0.npz").write_bytes(raw.read_bytes())
        reading = analyse_file(raw)
    elif job["state"] == "ready":
        print(f"\n{raw} is not there (the service runs without FORGE_DEBUG_KEEP=1, or on another machine): "
              "reading the frame from the GLB and the clean-up report instead")
        reading = analyse_file(out / "asset.glb", job["report"]["upAxis"], job["report"]["yawDegrees"])
    if reading is not None:
        record["reading"] = {k: v for k, v in reading.items() if k != "map"}
        print_reading(reading)
    (out / "first_run.json").write_text(json.dumps(record, indent=1))
    print(f"\nwritten to {out}: first_run.json" + (", asset.glb" if job["state"] == "ready" else "")
          + (", raw_view0.npz" if raw.is_file() else ""))
    return result


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Send one object through a running Forge and read the generator's frame.")
    parser.add_argument("photo", nargs="?", help="a photo of the object: a chair seen from the front and one side, at about 45 degrees")
    parser.add_argument("--dims", type=float, nargs=3, metavar=("W", "H", "D"),
                        help="the object's box in metres: width, height, depth, measured with a tape")
    parser.add_argument("--category", default="chair")
    parser.add_argument("--url", default="http://127.0.0.1:8000", help="the Forge")
    parser.add_argument("--target", type=int, default=15000, help="triangle target of the full copy")
    parser.add_argument("--out", default="first_run", help="folder for the answer, the GLB and the reading")
    parser.add_argument("--data", default=str(FORGE_DIR / "data"), help="the Forge's data folder, when it is on this machine")
    parser.add_argument("--wait", type=float, default=0.0, help="seconds to wait for the generator to become ready")
    parser.add_argument("--timeout", type=float, default=600.0, help="seconds to wait for the job")
    parser.add_argument("--analyse", metavar="FILE", help="only read the frame from a raw_view0.npz or a .glb; no service is called")
    parser.add_argument("--up-axis", choices=("Y", "Z", "X"), help="with --analyse of a .glb: `upAxis` of its clean-up report")
    parser.add_argument("--yaw-degrees", type=int, choices=(0, 90), default=0, help="with --analyse of a .glb: `yawDegrees` of its report")
    args = parser.parse_args(argv)

    if args.analyse:
        print_reading(analyse_file(Path(args.analyse), args.up_axis, args.yaw_degrees))
        return 0
    if not args.photo or not args.dims:
        parser.error("a photo and --dims are required")
    if args.dims[1] < TALL_ENOUGH * math.hypot(args.dims[0], args.dims[2]):
        print(f"warning: the box {args.dims} is not clearly taller than its footprint is across; "
              "up may not be readable from this object")
    return run(args)


if __name__ == "__main__":
    sys.exit(main())
