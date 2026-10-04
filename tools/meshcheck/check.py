"""The numbers of checks M1 and M2 of the mesh design (section 5.6), read from a record.

    <Forge's python> tools/meshcheck/check.py --data station/forge/data --room <roomId>
                     [--truth truth.json] [--json numbers.json] [--pictures DIR]

A record is the Forge's data folder after a session with FORGE_DEBUG_KEEP=1: the RoomModel with its
profiles (`rooms/<roomId>/room.json`), every job with the fit and the time of each attempt
(`jobs/<jobId>/job.json`) and both copies of every mesh (`assets/`). `truth.json` is written once by hand:
for each object, which side of its box, as the RoomModel holds it, is really the front (`+Z`, `+X`, `-Z`,
`-X`), or `none` for an object without a front. With no truth the facing lines say so and are not judged.

Per object it prints the facing rule that answered and whether it was right, whether the first shape is a
plain box, the fit and the seconds of each attempt and which one was published, and both copies'
triangles and validator verdicts. Then the totals M1 and M2 ask for. The exit code is 0 when no line
that could be judged failed, 1 when one did, 2 when the record cannot be read.

Only these numbers, and pictures rendered from the meshes (`--pictures`), go into `docs/evidence/mesh/`.
No camera frame leaves the Station.

Runs with the Forge's interpreter (numpy and Pillow); it imports the Forge's own profile and validator.
"""
from __future__ import annotations

import argparse
import json
import statistics
import sys
from pathlib import Path
from typing import Optional

import numpy as np

REPO = Path(__file__).resolve().parents[2]
if str(REPO / "station" / "forge") not in sys.path:
    sys.path.insert(0, str(REPO / "station" / "forge"))

from forge import profile  # noqa: E402
from forge.glb import GlbError, read_glb  # noqa: E402
from forge.validator import validate_glb  # noqa: E402

# What the checks ask for (mesh design section 5.6).
FACING_SHARE = 0.90  # chairs and couches, and generated meshes of objects with a front, that must face right
MEDIAN_FIT = 0.12
SECONDS_PER_ATTEMPT = 60.0
POOR_FIT = 0.20
# The first shape of the phone (section 5.2): an object with no template is cut from the scan when its
# profile covers at least this much of the box, and is a plain box otherwise.
MIN_COVERAGE = 0.30
NO_TEMPLATE = ("unknown", "other")
# The objects that stand against a wall: the categories whose facing rule is the free floor.
WALL_BACKED = ("storage", "screen", "refrigerator", "oven", "dishwasher", "washerDryer", "stove", "fireplace",
               "sink", "bathtub")
SEATS = ("chair", "couch")
SIDES = ("+Z", "+X", "-Z", "-X")
# The profile rule of the phone (ObjectFacing.cs), used here to find the front of a generated mesh.
STRIP_ROWS = 3
MIN_STRIP_SEEN = 0.25
PROFILE_MARGIN = 0.15


class RecordError(ValueError):
    """The folder holds no record of that room."""


# --------------------------------------------------------------------------- reading


def strip_mean(grid: np.ndarray, side: int) -> tuple[float, int]:
    """The mean height (0 to 1) of the seen cells in the back strip of a candidate front, and how many were seen.

    ``grid`` is the profile as [j, i]. Front +Z has its back strip on rows 0 to 2, +X on columns 0 to 2,
    -Z on rows 13 to 15, -X on columns 13 to 15.
    """
    strip = (grid[:STRIP_ROWS], grid[:, :STRIP_ROWS], grid[-STRIP_ROWS:], grid[:, -STRIP_ROWS:])[side]
    seen = strip[strip > 0]
    return (float(seen.mean()) / 255.0 if len(seen) else 0.0), int(len(seen))


def front_by_profile(cells: np.ndarray) -> Optional[str]:
    """The front of a shape by the phone's profile rule: opposite the side whose edge strip is highest.

    None when the rule gives no answer: the profile covers too little, the highest strip is hardly seen,
    or it does not stand out from the next by 0.15 of the height (a table, a cupboard).
    """
    if profile.coverage(cells) < MIN_COVERAGE:
        return None
    grid = np.asarray(cells).reshape(16, 16)
    means = [strip_mean(grid, side) for side in range(4)]
    order = sorted(range(4), key=lambda side: -means[side][0])  # stable: the lower side on a tie, as the phone
    best, second = order[0], order[1]
    if means[best][1] < MIN_STRIP_SEEN * STRIP_ROWS * 16 or means[best][0] - means[second][0] < PROFILE_MARGIN:
        return None
    return SIDES[best]


def glb_profile(glb: bytes, dims) -> np.ndarray:
    file = read_glb(glb)
    primitive = file.doc["meshes"][0]["primitives"][0]
    positions = np.asarray(file.accessor(primitive["attributes"]["POSITION"]), dtype=np.float64)
    faces = np.asarray(file.accessor(primitive["indices"]), dtype=np.int64).reshape(-1, 3)
    return profile.of_mesh(positions, faces, dims)


def read_copy(data: Path, sha: str, level: str, target: int, dims) -> Optional[dict]:
    """One published copy as the validator sees it; None when the job has none."""
    if not sha:
        return None
    path = data / "assets" / f"{sha}.glb"
    if not path.is_file():
        return {"sha256": sha, "ok": False, "triangles": 0, "bytes": 0, "violations": ["the file is not in assets/"]}
    glb = path.read_bytes()
    verdict = validate_glb(glb, tri_target=target or None, box=dims, level=level)
    return {"sha256": sha, "ok": verdict.ok, "triangles": verdict.triangles, "bytes": len(glb), "target": target,
            "cells": verdict.palette_cells, "violations": [f"[{v.code}] {v.message}" for v in verdict.violations]}


def read_record(data: Path, room_id: str, truth: Optional[dict]) -> dict:
    """Everything the checks need of one room, one entry per object."""
    room_dir = data / "rooms" / room_id
    if not (room_dir / "room.json").is_file():
        raise RecordError(f"{room_dir} holds no room.json: {room_id} is not a room of {data}")
    room = json.loads((room_dir / "room.json").read_text(encoding="utf-8"))
    plan = json.loads((room_dir / "room.meta.json").read_text(encoding="utf-8"))
    meshed = set(plan.get("generated", []))

    jobs: dict[int, dict] = {}  # the last job of each box that ran to an end
    for path in sorted((data / "jobs").glob("*/job.json")):
        job = json.loads(path.read_text(encoding="utf-8"))
        if job.get("roomId") != room_id or job.get("state") not in ("ready", "failed"):
            continue
        if job.get("cached") and job["boxId"] in jobs:
            continue  # an answer from the cache says nothing new about the mesh
        if job["boxId"] not in jobs or job.get("seq", 0) > jobs[job["boxId"]].get("seq", 0):
            jobs[job["boxId"]] = job

    objects = []
    for entry in room.get("objects", []):
        box_id, category, dims = entry["id"], entry.get("category", "unknown"), entry["size"]
        scan = profile.decode(entry["profile"]) if entry.get("profile") else None
        coverage = profile.coverage(scan) if scan is not None else 0.0
        side = None if truth is None else truth.get(str(box_id))
        if side is not None and side not in SIDES + ("none",):
            raise RecordError(f"truth for object {box_id} is {side!r}; it must be one of {SIDES + ('none',)}")
        has_front = side in SIDES
        item = {
            "id": box_id,
            "category": category,
            "name": f"{box_id} {category}",
            "size": dims,
            "profile": scan is not None,
            "coverage": round(coverage, 3),
            "facingRule": entry.get("facing", "box"),
            "truth": side,
            # After the phone's facing rule the front of every box is its +Z: the rule was right when it is.
            "facingRight": (side == "+Z") if has_front else None,
            "plainBox": category in NO_TEMPLATE and coverage < MIN_COVERAGE,
            "meshed": box_id in meshed,
            "job": None,
        }
        job = jobs.get(box_id)
        if job is not None:
            full = read_copy(data, job.get("sha256", ""), "full", job.get("triTarget", 0), dims)
            light = read_copy(data, job.get("lightSha256", ""), "light", job.get("triTargetLight", 0), dims)
            mesh_front = None
            if full is not None and full["ok"]:
                try:
                    mesh_front = front_by_profile(glb_profile((data / "assets" / f"{job['sha256']}.glb").read_bytes(), dims))
                except GlbError:
                    mesh_front = None
            item["job"] = {
                "jobId": job["jobId"],
                "state": job["state"],
                "error": job.get("error", ""),
                "generator": job.get("generator", ""),
                "decoder": job.get("decoder", ""),
                "fit": job.get("fit"),
                "fitYaw": job.get("fitYaw"),
                "fitUp": job.get("fitUp", ""),
                "attempts": [{"view": a["view"], "seconds": a.get("seconds"), "fit": a.get("fit"),
                              "error": a.get("error", ""), "published": bool(a.get("published"))}
                             for a in job.get("attemptLog", [])],
                "full": full,
                "light": light,
                "meshFront": mesh_front,
                # The mesh stands in the box: it faces right when its own front is on the box's true front.
                "meshFacingRight": (mesh_front == side) if has_front and mesh_front is not None else None,
            }
        objects.append(item)
    return {"room": room_id, "truth": truth is not None, "meshedObjects": len(meshed), "objects": objects}


# --------------------------------------------------------------------------- the totals


def share(right: int, total: int) -> Optional[float]:
    return None if total == 0 else right / total


def line(label: str, verdict: Optional[bool], text: str) -> dict:
    return {"check": label, "pass": verdict, "text": text}


def totals(record: dict, poor_fit: float = POOR_FIT) -> dict:
    """The lines of M1 and M2; ``pass`` is None for a line that cannot be judged from this record."""
    objects = record["objects"]
    judged = [o for o in objects if o["facingRight"] is not None]

    def facing(group) -> tuple[int, int]:
        chosen = [o for o in judged if o["category"] in group]
        return sum(o["facingRight"] for o in chosen), len(chosen)

    # M1: the first shapes, as far as the record shows them.
    boxes = [o["name"] for o in objects if o["profile"] and o["plainBox"]]
    seats_right, seats = facing(SEATS)
    walls_right, walls = facing(WALL_BACKED)
    by_rule: dict[str, list[int]] = {}
    for o in judged:
        tally = by_rule.setdefault(o["facingRule"], [0, 0])
        tally[0] += int(o["facingRight"])
        tally[1] += 1
    m1 = [
        line("no object with a profile is a plain box", not boxes,
             f"{sum(o['profile'] for o in objects)} objects have a profile; plain boxes among them: "
             + (", ".join(boxes) or "none")),
        line("chairs and couches face right", None if seats == 0 else seats_right / seats >= FACING_SHARE,
             f"{seats_right} of {seats}" + ("" if seats == 0 else f" ({100 * seats_right / seats:.0f} %; "
                                                                  f"{100 * FACING_SHARE:.0f} % asked)")
             + ("" if record["truth"] else "; no truth.json")),
        line("every wall-backed object faces right", None if walls == 0 else walls_right == walls,
             f"{walls_right} of {walls}" + ("" if record["truth"] else "; no truth.json")),
        line("facing by the rule that answered", None,
             ", ".join(f"{rule}: {right} of {total}" for rule, (right, total) in sorted(by_rule.items())) or "nothing to judge"),
        line("plain boxes with no profile", None,
             ", ".join(o["name"] for o in objects if o["plainBox"] and not o["profile"]) or "none"),
    ]

    # M2: the generated meshes.
    meshed = [o for o in objects if o["meshed"]]
    both = [o for o in meshed if o["job"] and o["job"]["full"] and o["job"]["full"]["ok"]
            and o["job"]["light"] and o["job"]["light"]["ok"]]
    missing = [o["name"] for o in meshed if o not in both]
    slow = [f"{o['name']} view {a['view']} ({a['seconds']:.0f} s)" for o in objects if o["job"]
            for a in o["job"]["attempts"] if a["seconds"] is not None and a["seconds"] > SECONDS_PER_ATTEMPT]
    timed = sum(1 for o in objects if o["job"] for a in o["job"]["attempts"] if a["seconds"] is not None)
    fits = [o["job"]["fit"] for o in objects if o["job"] and o["job"]["state"] == "ready" and o["job"]["fit"] is not None]
    median = statistics.median(fits) if fits else None
    fronted = [o for o in objects if o["job"] and o["job"]["meshFacingRight"] is not None]
    undecided = [o["name"] for o in objects if o["job"] and o["truth"] in SIDES and o["job"]["meshFront"] is None
                 and o["job"]["state"] == "ready"]
    mesh_right = sum(o["job"]["meshFacingRight"] for o in fronted)
    poor = [f"{o['name']} ({o['job']['fit']:.2f})" for o in objects
            if o["job"] and o["job"]["state"] == "ready" and o["job"]["fit"] is not None and o["job"]["fit"] > poor_fit]
    m2 = [
        line("each meshed object has a full and a light copy that pass the validator",
             None if not meshed else not missing,
             f"{len(both)} of {len(meshed)}" + (f"; without: {', '.join(missing)}" if missing else "")),
        line(f"generation takes {SECONDS_PER_ATTEMPT:.0f} s or less per attempt", None if timed == 0 else not slow,
             f"{timed - len(slow)} of {timed} attempts" + (f"; over: {', '.join(slow)}" if slow else "")),
        line(f"the median fit is {MEDIAN_FIT} or better", None if median is None else median <= MEDIAN_FIT,
             "no object has a fit" if median is None else f"{median:.3f} over {len(fits)} objects"),
        line("generated meshes of objects with a front face right",
             None if not fronted else mesh_right / len(fronted) >= FACING_SHARE,
             f"{mesh_right} of {len(fronted)}"
             + ("" if not fronted else f" ({100 * mesh_right / len(fronted):.0f} %; {100 * FACING_SHARE:.0f} % asked)")
             + (f"; the mesh's front could not be read for: {', '.join(undecided)}" if undecided else "")
             + ("" if record["truth"] else "; no truth.json")),
        line(f"objects above the poor-fit line of {poor_fit:g} after their attempts", None, ", ".join(poor) or "none"),
    ]
    return {"M1": m1, "M2": m2}


# --------------------------------------------------------------------------- printing


def print_record(record: dict, checks: dict) -> None:
    print(f"room {record['room']}: {len(record['objects'])} objects, {record['meshedObjects']} receive meshes"
          + ("" if record["truth"] else "; no truth.json, so facing is not judged"))
    for o in record["objects"]:
        right = {True: "right", False: "WRONG", None: "not judged"}[o["facingRight"]]
        first = "a plain box" if o["plainBox"] else "a shape"
        cover = f"profile covers {o['coverage']:.2f}" if o["profile"] else "no profile"
        print(f"\n{o['name']}  {o['size'][0]:g} x {o['size'][1]:g} x {o['size'][2]:g} m")
        print(f"  first shape: {first} ({cover}); facing by `{o['facingRule']}`, truth {o['truth'] or '?'}: {right}")
        job = o["job"]
        if job is None:
            print("  mesh: " + ("no job ended for this object" if o["meshed"] else "none (beyond the room's 32)"))
            continue
        for a in job["attempts"]:
            fit = "no fit" if a["fit"] is None else f"fit {a['fit']:.3f}"
            seconds = "" if a["seconds"] is None else f", {a['seconds']:.1f} s"
            print(f"  attempt from view {a['view']}: " + (a["error"] or fit) + seconds
                  + ("  <- published" if a["published"] else ""))
        if job["state"] != "ready":
            print(f"  mesh: {job['state']}: {job['error']}")
            continue
        fit = "no fit (no profile)" if job["fit"] is None else f"fit {job['fit']:.3f}"
        front = f"its front is on {job['meshFront']}" if job["meshFront"] else "its front cannot be read from it"
        facing = {True: "right", False: "WRONG", None: "not judged"}[job["meshFacingRight"]]
        print(f"  mesh: {job['generator']}" + (f" ({job['decoder']})" if job["decoder"] else "")
              + f", {fit}, up {job['fitUp']}, yaw {job['fitYaw']}; {front}: {facing}")
        for level in ("full", "light"):
            copy = job[level]
            if copy is None:
                print(f"  {level} copy: none")
            else:
                verdict = "passes" if copy["ok"] else "FAILS " + "; ".join(copy["violations"])
                print(f"  {level} copy: {copy['triangles']} triangles (target {copy.get('target', 0)}), "
                      f"{copy['bytes']} bytes, {len(copy.get('cells', []))} cell(s): {verdict}")
    for name in ("M1", "M2"):
        print(f"\n{name}")
        for item in checks[name]:
            mark = {True: "PASS", False: "FAIL", None: "    "}[item["pass"]]
            print(f"  {mark}  {item['check']}: {item['text']}")


def write_pictures(record: dict, data: Path, folder: Path) -> int:
    """One picture per meshed object: the scan's profile beside the published mesh's, seen from above.

    Heights as greys (white is the top of the box, black is empty), the box's front (+Z) at the top.
    Made from the meshes and the profiles alone; no photo is read.
    """
    from PIL import Image

    room = json.loads((data / "rooms" / record["room"] / "room.json").read_text(encoding="utf-8"))
    scans = {entry["id"]: entry.get("profile") for entry in room.get("objects", [])}
    folder.mkdir(parents=True, exist_ok=True)
    written = 0
    for o in record["objects"]:
        job = o["job"]
        if job is None or job["full"] is None or not (data / "assets" / f"{job['full']['sha256']}.glb").is_file():
            continue
        mesh = glb_profile((data / "assets" / f"{job['full']['sha256']}.glb").read_bytes(), o["size"])
        scan = profile.decode(scans[o["id"]]) if scans.get(o["id"]) else np.zeros(256, dtype=np.uint8)
        tiles = [np.flipud(np.asarray(p).reshape(16, 16)) for p in (scan, mesh)]  # row 15 (+Z) at the top
        strip = np.concatenate([tiles[0], np.full((16, 2), 64, dtype=np.uint8), tiles[1]], axis=1)
        Image.fromarray(np.kron(strip, np.ones((12, 12), dtype=np.uint8))).save(
            folder / f"{o['id']:03d}_{o['category']}.png")
        written += 1
    return written


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="The numbers of checks M1 and M2 of the mesh design, from a record.")
    parser.add_argument("--data", default=str(REPO / "station" / "forge" / "data"), help="the Forge's data folder")
    parser.add_argument("--room", required=True, help="the room's id")
    parser.add_argument("--truth", default=None,
                        help="truth.json: object id to its true front (+Z, +X, -Z, -X or none); "
                             "rooms/<roomId>/truth.json of the data folder when not given")
    parser.add_argument("--poor-fit", type=float, default=POOR_FIT, help="the poor-fit line (FORGE_POOR_FIT)")
    parser.add_argument("--json", default=None, metavar="FILE", help="also write the numbers as JSON")
    parser.add_argument("--pictures", default=None, metavar="DIR",
                        help="also write one picture per mesh: the scan's profile beside the mesh's")
    args = parser.parse_args(argv)

    data = Path(args.data)
    truth_path = Path(args.truth) if args.truth else data / "rooms" / args.room / "truth.json"
    try:
        truth = json.loads(truth_path.read_text(encoding="utf-8")) if truth_path.is_file() else None
        if args.truth and truth is None:
            raise RecordError(f"{truth_path} is not there")
        record = read_record(data, args.room, truth)
    except (RecordError, OSError, ValueError, KeyError) as exc:
        print(f"FAIL {exc}")
        return 2
    checks = totals(record, args.poor_fit)
    print_record(record, checks)
    if args.json:
        Path(args.json).write_text(json.dumps({**record, "checks": checks}, indent=1), encoding="utf-8")
        print(f"\nwritten: {args.json}")
    if args.pictures:
        print(f"written: {write_pictures(record, data, Path(args.pictures))} pictures in {args.pictures}")
    return 1 if any(item["pass"] is False for name in checks for item in checks[name]) else 0


if __name__ == "__main__":
    sys.exit(main())
