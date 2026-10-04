#!/usr/bin/env python3
"""The Station rehearsal's driver: talks to a running Hub and Forge the way the Console and the phone's asset
client do, and checks what comes back. Started by run_station_rehearsal.sh, which owns the processes.

    station_rehearsal.py live    --hub URL --forge URL --work DIR      parameters, commands, the Forge round trip
    station_rehearsal.py record  --walk FILE.poselog --out state.bin   a recorded walk as a Hub recording
    station_rehearsal.py replay  --hub URL --walk FILE --session ID    the Hub logged the replayed walk
    station_rehearsal.py logs    --work DIR --hub-logs DIR             the processes' own logs, after they stopped

Every check prints one line, "ok" or "FAIL". The exit code is 1 when any check failed.
Needs Pillow for the object photo, so it runs with the Forge's interpreter.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import math
import struct
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
ROOMS = REPO / "ATLAS" / "Assets" / "Atlas" / "World" / "Resources" / "AtlasRooms"
BOUNDARY = "atlas-forge-boundary-5f2c9a71d3e84b06"       # ForgeProtocol.Boundary

sys.path.insert(0, str(REPO / "tools" / "rooms"))
import roommath  # noqa: E402  (the reference of the triangle rule, which the Forge's own must agree with)

# The asset contract (Master Plan section 6.4 with mesh design section 5.4, acceptance test L16). Every mesh
# comes in two copies: the most triangles and bytes of one file, and the most triangles of a room's furniture.
MIN_TRIS, MAX_EDGE, BOX_TOLERANCE = 1500, 0.25, 0.10
LEVELS = {"full": {"tris": 15000, "bytes": 2 * 1024 * 1024, "room": 120000},
          "light": {"tris": 5000, "bytes": 1024 * 1024, "room": 60000}}

# State on the wire (Appendix B.1): 202 bytes, little-endian.
STATE = struct.Struct("<BBHIq7fBB6qh9d8fH")
STATE_ID = 0x10
RECORD = 8 + STATE.size                                    # Hub receive time, then the State as sent
HEADER = struct.Struct("<8sHHI")

failures: list[str] = []
checks = 0


def check(ok: bool, text: str) -> bool:
    global checks
    checks += 1
    print(("  ok    " if ok else "  FAIL  ") + text, flush=True)
    if not ok:
        failures.append(text)
    return ok


def request(method: str, url: str, body: bytes | None = None, content_type: str | None = None, timeout: float = 10.0):
    req = urllib.request.Request(url, data=body, method=method)
    if content_type:
        req.add_header("Content-Type", content_type)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as answer:
            return answer.status, answer.read()
    except urllib.error.HTTPError as error:
        return error.code, error.read()
    except (urllib.error.URLError, OSError) as error:
        return 0, str(error).encode()


def get_json(url: str, timeout: float = 5.0):
    status, body = request("GET", url, timeout=timeout)
    try:
        return status, json.loads(body) if body else None
    except ValueError:
        return status, None


def post_json(url: str, payload, method: str = "POST"):
    status, body = request(method, url, json.dumps(payload).encode(), "application/json")
    try:
        return status, json.loads(body) if body else None
    except ValueError:
        return status, None


def wait_for(what, seconds: float, step: float = 0.05):
    """Polls until what() returns something true. Returns (value, seconds waited) or (None, seconds)."""
    start = time.monotonic()
    while True:
        value = what()
        waited = time.monotonic() - start
        if value:
            return value, waited
        if waited >= seconds:
            return None, waited
        time.sleep(step)


# ---- the Hub: a phone, a parameter, the six commands -------------------------------------------------------


def events_of(hub: str, session: str) -> list[dict]:
    status, body = request("GET", f"{hub}/logs/{session}/events.jsonl")
    if status != 200:
        return []
    out = []
    for line in body.decode("utf-8", "replace").splitlines():
        try:
            out.append(json.loads(line))
        except ValueError:
            pass
    return out


def command(hub: str, session: str, label: str, route: str, body: dict, name: str, state_ok=None) -> None:
    """Sends one operator command over HTTP; the phone's own `cmd` event must come back, and the State must show it."""
    before = sum(1 for e in events_of(hub, session) if e.get("ev") == "cmd" and e.get("data", {}).get("cmd") == name)
    started = time.monotonic()
    status, _ = post_json(hub + route, body)
    hub_ms = (time.monotonic() - started) * 1000.0
    seen, waited = wait_for(
        lambda: sum(1 for e in events_of(hub, session) if e.get("ev") == "cmd" and e.get("data", {}).get("cmd") == name) > before, 2.0, 0.01)
    check(status == 200 and bool(seen), f"{label}: Hub {status} in {hub_ms:.0f} ms, the phone's cmd event in the Hub's log after {waited * 1000:.0f} ms")
    if state_ok is not None:
        shown, waited = wait_for(lambda: (lambda s: s[0] == 200 and s[1] and state_ok(s[1]))(get_json(hub + "/api/state")), 2.0)
        check(bool(shown), f"{label}: the State shows it after {waited * 1000:.0f} ms")


def live_hub(hub: str) -> str:
    print("Hub: the phone, a parameter and the operator commands")
    link, waited = wait_for(lambda: (lambda s: s[1] if s[0] == 200 and s[1] and s[1].get("phone", {}).get("state") == "connected" else None)(
        get_json(hub + "/api/session")), 15.0, 0.1)
    if not check(bool(link), f"a phone is connected to the Hub after {waited:.1f} s"):
        return ""
    session = link["sessionId"]
    check(link["phone"].get("device") == "ReplayPhone", f"the phone is ReplayPhone, session {session}")

    state, waited = wait_for(lambda: (lambda s: s[1] if s[0] == 200 and s[1] and s[1].get("hz", 0) > 50 else None)(get_json(hub + "/api/state")), 6.0, 0.2)
    check(bool(state), "State arrives at the Hub at " + (f"{state['hz']:.1f} Hz" if state else "under 50 Hz") + " (L3: at least 50)")
    if state:
        check(isinstance(state.get("head"), list) and len(state["head"]) == 3 and state.get("lossPct", 100) < 2.0,
              f"the State digest carries the head pose; loss {state.get('lossPct')} % (L3: under 2)")
        latency = state.get("latencyMs") or {}
        check(latency.get("p95", 1e9) <= 25.0, f"phone to Hub latency p95 {latency.get('p95')} ms (L4: at most 25)")

    status, manifest = get_json(hub + "/api/params")
    names = [p["name"] for p in manifest["params"]] if status == 200 and manifest else []
    check(len(names) > 0 and "render.scale" in names, f"the phone's parameter manifest: {len(names)} parameters")

    # A parameter, set and echoed; then one out of range, clamped by the Hub and the phone.
    if link.get("demoLock"):
        post_json(hub + "/api/op", {"cmd": "lock", "args": {"on": False}})
    status, answer = post_json(hub + "/api/params/render.scale", {"value": 0.7})
    echoed, waited = wait_for(lambda: (lambda r: r[0] == 200 and r[1] and abs(r[1]["val"] - 0.7) < 1e-6)(get_json(hub + "/api/params/render.scale")), 2.0)
    check(status == 200 and answer and abs(answer["value"] - 0.7) < 1e-6 and bool(echoed),
          f"render.scale set to 0.7: Hub {status}, read back after {waited * 1000:.0f} ms")
    logged, _ = wait_for(lambda: any(e.get("ev") == "param" and e.get("data", {}).get("name") == "render.scale" for e in events_of(hub, session)), 2.0)
    check(bool(logged), "the phone's param event for render.scale is in the Hub's events.jsonl")
    _, rho = get_json(hub + "/api/params/curved.cullRho")
    status, answer = post_json(hub + "/api/params/curved.cullRho", {"value": 99})
    check(status == 200 and rho and answer and answer["value"] == rho["max"], f"curved.cullRho 99 is clamped to {rho['max'] if rho else '?'}")
    shown, _ = wait_for(lambda: (lambda s: s[0] == 200 and s[1] and abs(s[1]["p"][2] - rho["max"]) < 1e-6)(get_json(hub + "/api/state")), 2.0)
    check(bool(shown), "the clamped rho is in State.p[2]")
    status, _ = post_json(hub + "/api/params/no.such.parameter", {"value": 1})
    check(status == 404, f"an unknown parameter is refused: {status}")

    # The six operator commands (L10).
    command(hub, session, "switch to S1", "/api/scene", {"space": "S1", "variant": "", "args": {}}, "space", lambda s: s["space"] == 1)
    # The curved spaces are spaces like any other to the Hub and the Console: S2, S3 and S4 by their codes.
    for number in (2, 3, 4):
        command(hub, session, f"switch to S{number}", "/api/scene", {"space": f"S{number}", "variant": "", "args": {}}, "space",
                lambda s, number=number: s["space"] == number)
    command(hub, session, "Home", "/api/op", {"cmd": "home", "args": {}}, "home", lambda s: s["space"] == 0)
    command(hub, session, "recentre", "/api/op", {"cmd": "recentre", "args": {}}, "recentre")
    command(hub, session, "quality 2", "/api/op", {"cmd": "quality", "args": {"tier": 2}}, "quality", lambda s: s["tier"] == 2)
    command(hub, session, "quality auto", "/api/op", {"cmd": "quality", "args": {"tier": -1}}, "quality")
    command(hub, session, "STOP", "/api/op", {"cmd": "cue", "args": {}}, "cue", lambda s: s["cue"] and s["bump"] == 2)
    command(hub, session, "lock on", "/api/op", {"cmd": "lock", "args": {"on": True}}, "lock", lambda s: s["lock"])
    status, _ = post_json(hub + "/api/params/render.scale", {"value": 0.6})
    check(status == 423, f"under the lock a parameter set is refused: {status}")
    command(hub, session, "lock off", "/api/op", {"cmd": "lock", "args": {"on": False}}, "lock", lambda s: not s["lock"])
    status, _ = post_json(hub + "/api/op", {"cmd": "freeze", "args": {}})
    check(status == 404, f"an unknown command is refused: {status}")
    return session


# ---- the Forge: a room, its objects, the meshes ------------------------------------------------------------


def crop(colour) -> bytes:
    from PIL import Image
    image = Image.new("RGB", (256, 256), (96, 96, 96))
    image.paste(colour, (40, 40, 216, 216))
    out = io.BytesIO()
    image.save(out, format="JPEG", quality=85)
    return out.getvalue()


def object_body(meta: dict, views: list[bytes]) -> bytes:
    """ForgeProtocol.ObjectBody of the phone's asset client, byte for byte."""
    parts = [f'--{BOUNDARY}\r\nContent-Disposition: form-data; name="meta"\r\n\r\n'.encode()
             + json.dumps(meta, separators=(",", ":")).encode() + b"\r\n"]
    for i, jpeg in enumerate(views):
        parts.append(f'--{BOUNDARY}\r\nContent-Disposition: form-data; name="view{i}"; filename="view{i}.jpg"\r\n'
                     f"Content-Type: image/jpeg\r\n\r\n".encode() + jpeg + b"\r\n")
    parts.append(f"--{BOUNDARY}--\r\n".encode())
    return b"".join(parts)


def measure_glb(glb: bytes) -> dict:
    """Reads a GLB with nothing but struct: triangle count, longest edge, bounds, and what must not be there."""
    magic, version, length = struct.unpack_from("<4sII", glb, 0)
    if magic != b"glTF" or version != 2 or length != len(glb):
        raise ValueError("not a glTF 2 binary")
    offset, doc, binary = 12, None, b""
    while offset < len(glb):
        size, kind = struct.unpack_from("<I4s", glb, offset)
        chunk = glb[offset + 8: offset + 8 + size]
        if kind == b"JSON":
            doc = json.loads(chunk)
        elif kind == b"BIN\x00":
            binary = chunk
        offset += 8 + size
    meshes = doc.get("meshes", [])
    if len(meshes) != 1 or len(meshes[0]["primitives"]) != 1:
        raise ValueError("more than one mesh or primitive")
    primitive = meshes[0]["primitives"][0]

    def view(accessor_index: int):
        accessor = doc["accessors"][accessor_index]
        buffer_view = doc["bufferViews"][accessor["bufferView"]]
        start = buffer_view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
        return accessor, start, buffer_view.get("byteStride", 0)

    accessor, start, stride = view(primitive["attributes"]["POSITION"])
    stride = stride or 12
    positions = [struct.unpack_from("<3f", binary, start + i * stride) for i in range(accessor["count"])]
    accessor, start, _ = view(primitive["indices"])
    code = {5123: "H", 5125: "I"}[accessor["componentType"]]
    indices = struct.unpack_from(f"<{accessor['count']}{code}", binary, start)

    longest = 0.0
    for t in range(0, len(indices), 3):
        a, b, c = positions[indices[t]], positions[indices[t + 1]], positions[indices[t + 2]]
        longest = max(longest, math.dist(a, b), math.dist(b, c), math.dist(c, a))
    lo = [min(p[k] for p in positions) for k in range(3)]
    hi = [max(p[k] for p in positions) for k in range(3)]
    return {
        "tris": len(indices) // 3, "maxEdge": longest, "min": lo, "max": hi,
        "extras": [k for k in ("materials", "textures", "images", "samplers") if doc.get(k)],
        "material": "material" in primitive,
    }


def live_forge(forge: str, hub: str, work: Path, forge_dir: Path) -> None:
    print("Forge: fixture rooms, their object jobs, the meshes")
    status, health = get_json(forge + "/health")
    check(status == 200 and health and health.get("generator") == "procedural" and health.get("generatorReady", True),
          f"GET /health: {status}, generator {health.get('generator') if health else '?'}")
    check(bool(health) and not health.get("debugKeep") and not health.get("hostedEnabled"),
          "no crops are kept and the hosted service is off (L20, L21)")
    _, session = get_json(hub + "/api/session")
    forge_seen, _ = wait_for(lambda: (lambda s: s[0] == 200 and s[1] and s[1].get("forge", {}).get("up"))(get_json(hub + "/api/session")), 6.0, 0.2)
    check(bool(forge_seen), "the Hub's probe sees the Forge")

    out = work / "assets"
    out.mkdir(parents=True, exist_ok=True)
    palette = [(150, 112, 70), (70, 110, 150), (120, 140, 90), (160, 80, 80)]
    for room_name in ("sample_4x5_table", "budget_room"):
        text = (ROOMS / (room_name + ".json")).read_bytes()
        room = json.loads(text)
        objects = room["objects"]
        status, body = request("POST", forge + "/rooms", text, "application/json")
        answer = json.loads(body) if status in (200, 201) else {}
        generated = answer.get("generated") or []
        if not check(status in (200, 201) and answer.get("roomId") == room["id"],
                     f"{room_name}: POST /rooms {status}, boxes {generated} receive meshes"):
            continue
        # The Forge's plan against the reference rule: the room's 120,000 triangles shared by surface area.
        targets, light_target = roommath.plan_tri_targets(objects)
        check(sorted(generated) == sorted(o["id"] for o in objects) and not answer.get("placeholders"),
              f"{room_name}: every one of the {len(objects)} boxes receives a mesh")
        check(answer.get("triTargets") == {str(box): t for box, t in targets.items()}
              and answer.get("triTarget") == max(targets.values(), default=0)
              and answer.get("triTargetLight") == light_target,
              f"{room_name}: the Forge's targets are those of tools/rooms/roommath.py: full "
              f"{min(targets.values())} to {max(targets.values())}, light {light_target}")
        status, back = get_json(f"{forge}/rooms/{room['id']}")
        check(status == 200 and back == room, f"{room_name}: GET /rooms/{room['id']} returns the RoomModel as uploaded")

        def meta_of(obj: dict) -> dict:
            # What the phone sends: the RoomModel's own full target for the object and the room's light target.
            return {"boxId": obj["id"], "category": obj["category"], "dims": obj["size"], "yaw": obj["yaw"],
                    "triTarget": obj["triTarget"], "triTargetLight": light_target, "backend": "local",
                    "views": [{"score": 0.8, "azimuthDeg": 35.0, "intrinsics": [612.0, 612.0, 128.0, 128.0],
                               "camToBox": [1, 0, 0, 0, 0, 1, 0, 0.4, 0, 0, 1, -1.5, 0, 0, 0, 1]}]}

        jobs = {}
        for k, obj in enumerate(objects):
            status, body = request("POST", f"{forge}/rooms/{room['id']}/objects",
                                   object_body(meta_of(obj), [crop(palette[k % len(palette)])]),
                                   f"multipart/form-data; boundary={BOUNDARY}", 15)
            if status in (200, 202):
                jobs[json.loads(body)["jobId"]] = obj
            else:
                check(False, f"{room_name} box {obj['id']}: POST objects {status} {body[:200]!r}")
        check(len(jobs) == len(objects), f"{room_name}: {len(jobs)} object jobs accepted")

        def finished():
            status, listed = get_json(f"{forge}/jobs?room={room['id']}")
            listed = listed if isinstance(listed, list) else (listed or {}).get("jobs", [])
            mine = [j for j in listed if j["jobId"] in jobs]
            return mine if status == 200 and len(mine) == len(jobs) and all(j["state"] in ("ready", "failed", "placeholder") for j in mine) else None

        done, waited = wait_for(finished, 60.0 * max(1, len(jobs)), 0.2)
        if not check(bool(done), f"{room_name}: every job ended, {waited:.1f} s after the first upload (L15: at most 60 s each)"):
            continue
        check(all(j["state"] == "ready" for j in done), f"{room_name}: every job is ready: " + ", ".join(sorted({j['state'] for j in done})))
        check(all(j.get("attempts") == 1 and j.get("fitUp") == "Y" and "fit" not in j for j in done if j["state"] == "ready"),
              f"{room_name}: one generation per object, and no fit, since these fixtures carry no profile")

        import subprocess
        total = {"full": 0, "light": 0}
        for job in sorted(done, key=lambda j: jobs[j["jobId"]]["id"]):
            obj = jobs[job["jobId"]]
            if job["state"] != "ready":
                continue
            # Both copies: the full one to the smaller of the two targets, the light one to the room's.
            limits = {"full": min(targets[obj["id"]], obj["triTarget"]), "light": light_target}
            for level, sha_key, url_key, count_key in (("full", "sha256", "url", "tris"),
                                                       ("light", "lightSha256", "lightUrl", "lightTris")):
                name = f"{room_name} box {obj['id']} ({obj['category']}), {level} copy"
                if not check(bool(job.get(sha_key)) and bool(job.get(url_key)), f"{name}: the job names it"):
                    continue
                url = job[url_key] if job[url_key].startswith("http") else forge + job[url_key]
                status, glb = request("GET", url, timeout=20)
                if not check(status == 200 and hashlib.sha256(glb).hexdigest() == job[sha_key],
                             f"{name}: asset downloaded, its SHA-256 is its name"):
                    continue
                path = out / f"{room['id']}_{obj['id']}_{level}.glb"
                path.write_bytes(glb)
                try:
                    m = measure_glb(glb)
                except Exception as error:      # a malformed file is a finding, not a crash
                    check(False, f"{name}: the GLB cannot be read: {error}")
                    continue
                size = [m["max"][k] - m["min"][k] for k in range(3)]
                worst = max(abs(size[k] - obj["size"][k]) / obj["size"][k] for k in range(3))
                centred = abs(m["min"][1]) < 1e-3 and abs(m["min"][0] + m["max"][0]) < 0.02 and abs(m["min"][2] + m["max"][2]) < 0.02
                limit = min(limits[level], LEVELS[level]["tris"])
                ok = (MIN_TRIS <= m["tris"] <= limit and m["maxEdge"] <= MAX_EDGE + 1e-4 and worst <= BOX_TOLERANCE
                      and centred and not m["extras"] and not m["material"] and len(glb) <= LEVELS[level]["bytes"])
                check(ok, f"{name}: {m['tris']} triangles (1500 to {limit}), longest edge {m['maxEdge']:.3f} m, "
                          f"{worst * 100:.1f} % off the box, base centred {centred}, {len(glb)} bytes (L16)")
                check(m["tris"] == job[count_key], f"{name}: the job reports the triangle count the file holds")
                total[level] += m["tris"]

                # The Forge's own validator, as the command line the team uses.
                run = subprocess.run([sys.executable, "-m", "forge.validator", str(path), "--target", str(limit),
                                      "--level", level, "--box", *[str(v) for v in obj["size"]]],
                                     cwd=forge_dir, capture_output=True, text=True)
                check(run.returncode == 0, f"{name}: forge.validator accepts it" +
                      ("" if run.returncode == 0 else ": " + (run.stdout + run.stderr).strip()[-300:]))
        for level in ("full", "light"):
            check(0 < total[level] <= LEVELS[level]["room"],
                  f"{room_name}: the {level} copies hold {total[level]} triangles in all (L16: at most {LEVELS[level]['room']:,})")

        # The same crop again with the same meta: the cache answers, no job runs (L18).
        obj = objects[0]
        status, body = request("POST", f"{forge}/rooms/{room['id']}/objects", object_body(meta_of(obj), [crop(palette[0])]),
                               f"multipart/form-data; boundary={BOUNDARY}", 15)
        again = json.loads(body) if status in (200, 202) else {}
        check(status == 200 and again.get("cached") is True, f"{room_name}: the same crop uploaded twice is a cache hit: {status} {again}")

    kept = [p for p in (work / "forge-data").rglob("*") if p.is_file() and p.suffix.lower() in (".jpg", ".jpeg", ".png")]
    check(not kept, f"after all jobs the Forge holds {len(kept)} crop files (L20)")
    status, queue = get_json(forge + "/queue")
    check(status == 200 and queue and not queue.get("active") and not queue.get("queued"), "the Forge's queue is empty")
    check(bool(session), "the Hub answered throughout")


# ---- a recorded walk through ReplayPhone -------------------------------------------------------------------


def read_poselog(path: Path) -> list[tuple]:
    """t x y z qx qy qz qw s per line, after the header (lane C contract, section 7.6)."""
    samples = []
    for line in path.read_text().splitlines():
        parts = line.split()
        if len(parts) != 9:
            continue
        try:
            samples.append(tuple(float(v) for v in parts[:8]) + (int(parts[8]),))
        except ValueError:
            continue
    return samples


def record(walk: Path, out: Path) -> int:
    """Writes the walk as a Hub recording (state.bin): one State per sample, Home entered, the flat kernel's values."""
    samples = read_poselog(walk)
    if not samples:
        print(f"{walk}: no samples", file=sys.stderr)
        return 1
    with open(out, "wb") as f:
        f.write(HEADER.pack(b"ATLSTATE", 1, RECORD, 0))
        for seq, (t, x, y, z, qx, qy, qz, qw, tracking) in enumerate(samples):
            t_us = int(round(t * 1e6))
            flags = tracking & 3
            f.write(struct.pack("<q", t_us))
            f.write(STATE.pack(STATE_ID, flags, 0, seq, t_us, x, y, z, qx, qy, qz, qw, 0, 0,
                               0, 0, 0, 0, 0, 0, 0, 1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0,
                               1.0, 2.0, 3.0, 0.0, 0.0, 0.0, 0.0, 0.0, 1))
    print(f"recorded {len(samples)} States of {walk.name} into {out}")
    return 0


def replay(hub: str, walk: Path, session: str) -> None:
    print(f"Hub: the recorded walk {walk.name}, as the Hub logged it")
    samples = read_poselog(walk)
    status, body = request("GET", f"{hub}/logs/{session}/state.bin", timeout=20)
    if not check(status == 200 and len(body) >= HEADER.size, f"GET /logs/{session}/state.bin: {status}, {len(body)} bytes"):
        return
    magic, version, size, _ = HEADER.unpack_from(body, 0)
    check(magic == b"ATLSTATE" and version == 1 and size == RECORD and (len(body) - HEADER.size) % RECORD == 0,
          "the log is a state.bin of whole 210-byte records")
    logged = []
    for offset in range(HEADER.size, len(body) - RECORD + 1, RECORD):
        fields = STATE.unpack_from(body, offset + 8)
        logged.append(fields)
    check(len(logged) >= 0.98 * len(samples), f"{len(logged)} of {len(samples)} States reached the Hub's log (L3: loss under 2 %)")

    # Every logged State is one sample of the recording, bit for bit in its pose, and they are in order.
    wanted = [struct.unpack("<7f", struct.pack("<7f", *s[1:8])) for s in samples]
    cursor, matched, ordered = 0, 0, True
    last_seq = -1
    for fields in logged:
        pose = fields[5:12]
        while cursor < len(wanted) and wanted[cursor] != pose:
            cursor += 1
        if cursor == len(wanted):
            break
        matched += 1
        cursor += 1
        ordered = ordered and fields[3] > last_seq
        last_seq = fields[3]
    check(matched == len(logged), f"{matched} of {len(logged)} logged States carry a pose of the recording, in the recording's order")
    check(ordered, "the sequence numbers in the log only rise")
    limited = [f for f in logged if (f[1] & 3) != 2]
    check(len(limited) == sum(1 for s in samples if s[8] != 2) or len(logged) < len(samples),
          f"{len(limited)} logged States carry tracking that is not normal, as in the recording")


# ---- the logs, after the processes stopped -----------------------------------------------------------------


def logs(work: Path, hub_logs: Path, sessions: list[str]) -> None:
    print("Logs: what the Hub and the Forge wrote")
    hub_out = (work / "hub.out").read_text(errors="replace") if (work / "hub.out").exists() else ""
    forge_out = (work / "forge.out").read_text(errors="replace") if (work / "forge.out").exists() else ""
    bad_hub = [l for l in hub_out.splitlines() if "Unhandled" in l or "Exception" in l or l.lstrip().startswith("at ")]
    check(not bad_hub, "the Hub's output holds no exception" + ("" if not bad_hub else ": " + bad_hub[0][:200]))
    bad_forge = [l for l in forge_out.splitlines() if "Traceback" in l or " 500 " in l or "ERROR" in l]
    check(not bad_forge, "the Forge's output holds no traceback, error or 500" + ("" if not bad_forge else ": " + bad_forge[0][:200]))
    check("Application startup complete" in forge_out, "the Forge started")

    for session in sessions:
        folder = hub_logs / session
        if not check(folder.is_dir(), f"session folder {session} exists"):
            continue
        state = folder / "state.bin"
        records = (state.stat().st_size - HEADER.size) // RECORD if state.exists() else 0
        check(records > 0 and (state.stat().st_size - HEADER.size) % RECORD == 0, f"{session}: state.bin holds {records} whole records")
        hub_log = (folder / "hub.log").read_text(errors="replace") if (folder / "hub.log").exists() else ""
        kinds = {}
        for line in hub_log.splitlines():
            parts = line.split(" ", 2)
            if len(parts) >= 2:
                kinds[parts[1]] = kinds.get(parts[1], 0) + 1
        print("        hub.log: " + ", ".join(f"{k} {v}" for k, v in sorted(kinds.items())))
        check(bool(hub_log) and not any(k in ("error", "exception") for k in kinds), f"{session}: hub.log has entries and no error")

    if sessions:
        folder = hub_logs / sessions[0]
        events = [json.loads(l) for l in (folder / "events.jsonl").read_text().splitlines() if l.strip()] if (folder / "events.jsonl").exists() else []
        commands = [e["data"].get("cmd") for e in events if e.get("ev") == "cmd"]
        check(all(c in commands for c in ("space", "home", "recentre", "quality", "cue", "lock")),
              "events.jsonl of the live session holds a cmd event for each of the six commands: " + ", ".join(commands))
        hub_log = (folder / "hub.log").read_text(errors="replace") if (folder / "hub.log").exists() else ""
        check("render.scale" in hub_log or "param" in hub_log, "hub.log records the parameter that was sent")
        jobs = sum(1 for l in hub_log.splitlines() if l.split(" ", 2)[1:2] == ["job"])
        check(jobs >= 13, f"hub.log records the Forge's job states relayed through /internal/job: {jobs} lines for 13 jobs")
        perf = (folder / "perf.csv").read_text().splitlines() if (folder / "perf.csv").exists() else []
        check(len(perf) >= 3 and len(perf[0].split(",")) == 34, f"perf.csv: the 34-column header and {max(0, len(perf) - 1)} rows, one a second")
        check((folder / "manifest.json").exists() and (folder / "outline.bin").exists(), "manifest.json and outline.bin were kept")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("phase", choices=("live", "record", "replay", "logs"))
    parser.add_argument("--hub", default="http://127.0.0.1:8080")
    parser.add_argument("--forge", default="http://127.0.0.1:8000")
    parser.add_argument("--work", type=Path)
    parser.add_argument("--hub-logs", type=Path)
    parser.add_argument("--walk", type=Path)
    parser.add_argument("--out", type=Path)
    parser.add_argument("--session", action="append", default=[])
    args = parser.parse_args()
    hub, forge = args.hub.rstrip("/"), args.forge.rstrip("/")

    if args.phase == "record":
        return record(args.walk, args.out)
    if args.phase == "live":
        session = live_hub(hub)
        live_forge(forge, hub, args.work, REPO / "station" / "forge")
        if session:
            (args.work / "live.session").write_text(session)
    elif args.phase == "replay":
        replay(hub, args.walk, args.session[0])
    else:
        logs(args.work, args.hub_logs, args.session)

    print(f"{args.phase}: {checks - len(failures)} of {checks} checks passed")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
