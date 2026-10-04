#!/usr/bin/env python3
"""One real round trip against a running Forge, with the HTTP sequence of the phone's asset client.

Start the Forge with its procedural generator, then run this with the Forge's interpreter (it needs
Pillow for the crop):

    cd station/forge
    FORGE_GENERATOR=procedural FORGE_HUB_URL= FORGE_DATA_DIR=/tmp/forge-data \\
        .venv/bin/uvicorn forge.app:app --host 127.0.0.1 --port 8011 &
    .venv/bin/python "../../ATLAS/Assets/Atlas/Tests/Telemetry/Assets/Fixtures/Tools~/forge_round_trip.py" http://127.0.0.1:8011

The sequence is the one of `AssetClientCore`: GET /health, POST /rooms (the RoomModel text), GET /health,
POST /rooms/{id}/objects (multipart: `meta`, `view0`, with the client's boundary), GET /jobs?room= every
0.2 s until the job is ready, GET the asset. The GLB is written next to the other fixtures as
`roundtrip.glb.bytes` with `roundtrip.json` (box, target, hash); the C# test `Glb_RoundTrip` then runs the
phone's validator and mesh reader on it.
"""
from __future__ import annotations

import hashlib
import io
import json
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
OUT = HERE.parent
REPO = HERE.parents[7]
ROOM = REPO / "ATLAS" / "Assets" / "Atlas" / "World" / "Resources" / "AtlasRooms" / "sample_4x5_table.json"
BOUNDARY = "atlas-forge-boundary-5f2c9a71d3e84b06"       # ForgeProtocol.Boundary


def request(method: str, url: str, body: bytes | None = None, content_type: str | None = None, timeout: float = 15.0):
    req = urllib.request.Request(url, data=body, method=method)
    if content_type:
        req.add_header("Content-Type", content_type)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as answer:
            return answer.status, answer.read()
    except urllib.error.HTTPError as error:
        return error.code, error.read()


def crop() -> bytes:
    image = Image.new("RGB", (256, 256), (96, 96, 96))
    image.paste((150, 112, 70), (40, 40, 216, 216))
    out = io.BytesIO()
    image.save(out, format="JPEG", quality=85)
    return out.getvalue()


def object_body(meta: dict, views: list[bytes]) -> bytes:
    """`ForgeProtocol.ObjectBody`, byte for byte."""
    parts = [f'--{BOUNDARY}\r\nContent-Disposition: form-data; name="meta"\r\n\r\n'.encode()
             + json.dumps(meta, separators=(",", ":")).encode() + b"\r\n"]
    for i, jpeg in enumerate(views):
        parts.append(f'--{BOUNDARY}\r\nContent-Disposition: form-data; name="view{i}"; filename="view{i}.jpg"\r\n'
                     f"Content-Type: image/jpeg\r\n\r\n".encode() + jpeg + b"\r\n")
    parts.append(f"--{BOUNDARY}--\r\n".encode())
    return b"".join(parts)


def main() -> int:
    forge = (sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:8000").rstrip("/")
    room_text = ROOM.read_bytes()
    room = json.loads(room_text)
    obj = room["objects"][0]

    status, _ = request("GET", forge + "/health", timeout=2)
    assert status == 200, f"GET /health: {status}"
    status, body = request("POST", forge + "/rooms", room_text, "application/json", 10)
    assert status in (200, 201), f"POST /rooms: {status} {body[:200]!r}"

    meta = {"boxId": obj["id"], "category": obj["category"], "dims": obj["size"], "yaw": obj["yaw"],
            "triTarget": obj["triTarget"], "backend": "local",
            "views": [{"score": 0.8, "azimuthDeg": 35.0, "intrinsics": [612.0, 612.0, 128.0, 128.0],
                       "camToBox": [1, 0, 0, 0, 0, 1, 0, 0.4, 0, 0, 1, -1.5, 0, 0, 0, 1]}]}
    status, _ = request("GET", forge + "/health", timeout=2)
    assert status == 200
    status, body = request("POST", f"{forge}/rooms/{room['id']}/objects", object_body(meta, [crop()]),
                           f"multipart/form-data; boundary={BOUNDARY}", 15)
    assert status in (200, 202), f"POST objects: {status} {body[:300]!r}"
    job_id = json.loads(body)["jobId"]

    deadline = time.monotonic() + 60
    job = None
    while time.monotonic() < deadline:
        status, body = request("GET", f"{forge}/jobs?room={room['id']}", timeout=5)
        assert status == 200, f"GET /jobs: {status}"
        jobs = json.loads(body)
        jobs = jobs if isinstance(jobs, list) else jobs["jobs"]
        job = next(j for j in jobs if j["jobId"] == job_id)
        if job["state"] in ("ready", "failed", "placeholder"):
            break
        time.sleep(0.2)
    assert job and job["state"] == "ready", f"job ended as {job}"

    url = job["url"] if job["url"].startswith("http") else forge + job["url"]
    status, glb = request("GET", url, timeout=20)
    assert status == 200, f"GET asset: {status}"
    assert hashlib.sha256(glb).hexdigest() == job["sha256"]

    (OUT / "roundtrip.glb.bytes").write_bytes(glb)
    (OUT / "roundtrip.json").write_text(json.dumps({
        "room": room["id"], "boxId": obj["id"], "category": obj["category"], "dims": obj["size"], "yaw": obj["yaw"],
        "triTarget": obj["triTarget"], "sha256": job["sha256"], "bytes": len(glb), "tris": job["tris"],
        "maxEdge": job["maxEdge"], "bounds": job["bounds"], "palette": job["palette"], "jobUrl": job["url"],
    }, indent=2) + "\n")
    print(f"round trip ok: {job['tris']} triangles, {len(glb)} bytes, sha256 {job['sha256']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
