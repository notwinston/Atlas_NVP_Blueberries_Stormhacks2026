#!/usr/bin/env python3
"""Writes the prop test fixtures and the reference vectors for the C# port (standalone-phone plan, task T3).

    tools/props/.venv/bin/python tools/props/reference.py [--fixtures DIR] [--out FILE]

Two outputs, both deterministic (the same bytes on every run):

  ATLAS/Assets/Atlas/Tests/Objects/Fixtures/props_min/   three tiny props made of boxes (a box chair, a slab
                                                         table and a couch of three boxes) in the prop format
                                                         of plan section 6.3, with their manifest.json.
  ATLAS/Assets/Atlas/Tests/Objects/Fixtures/reference.json
      profiles     of_mesh of each fixture at its size, and coverage;
      convention   the profile of one fixture mesh as ObjectProfile.Compute must give it, with floor-class
                   triangles that must be left out, once in the box frame and once yawed and moved;
      rotate       profile.rotate of the (asymmetric) couch profile for quarter turns 0..3;
      fitScore     profile.fit_score on fixed pairs;
      scale        the Forge's scale-to-box rule (cleanup._candidate) for 5 box sizes and turns;
      matcher      a library and the PropMatcher choice for fixed queries (plan section 6.4, "Matcher rule").

Everything is computed with the Forge's own code (station/forge/forge/profile.py and cleanup.py), so the
C# port is checked against an independent implementation. `conventions` in reference.json states the
frames and the readings of the rule the vectors assume.
"""
from __future__ import annotations

import argparse
import json
import math
import re
import sys
from pathlib import Path

import numpy as np

import profile_tool as pt
from forge import cleanup as forge_cleanup  # noqa: E402  (profile_tool put station/forge on the path)
from forge import profile as forge_profile  # noqa: E402

kitglb = pt.kitglb

REFERENCE = pt.ASSETS / "Tests" / "Objects" / "Fixtures" / "reference.json"

# Grid step of the box meshes: every quad is at most 0.12 m a side, so its diagonal (0.17 m) is inside the 0.25 m rule.
GRID_STEP = 0.12

# PropMatcher's constants (plan section 6.4).
ASPECT_WEIGHT, TAG_BONUS, MAX_TAG_BONUS, TIE, MIN_COVERAGE = 0.5, 0.03, 0.06, 0.02, 0.30
MAX_ANISOTROPY = 0.15
# A vector whose decision lies closer than this to a threshold is refused: float32 on the phone could flip it.
DECISION_MARGIN = 1e-3

FACING_TURNS = {"Box": (0, 1, 2, 3), "Profile": (0,), "Table": (0, 1, 2, 3), "Clearance": (0,)}


# --------------------------------------------------------------------------- box meshes

def box_piece(boxes) -> kitglb.Piece:
    """Closed boxes in Unity's frame, flat normals, clockwise fronts, every quad at most GRID_STEP a side.

    ``boxes`` holds (lo, hi, cell) with lo and hi the (x, y, z) corners in metres. A box's grid is the same
    on all its faces, so the faces meet corner to corner and the box is closed after welding.
    """
    piece = kitglb.Piece()
    for lo, hi, cell in boxes:
        counts = [max(1, math.ceil((hi[a] - lo[a]) / GRID_STEP - 1e-9)) for a in range(3)]
        coords = [[lo[a] + (hi[a] - lo[a]) * k / counts[a] for k in range(counts[a] + 1)] for a in range(3)]
        for axis in range(3):
            for sign in (1, -1):
                n = [0.0, 0.0, 0.0]
                n[axis] = float(sign)
                u = (0.0, 1.0, 0.0) if axis != 1 else (1.0, 0.0, 0.0)
                v = _cross(u, n)                                   # cross(v, u) == n: the front faces n
                au = 1 if axis != 1 else 0
                av = next(a for a in range(3) if v[a] != 0.0)
                sv = v[av] > 0.0
                nu, nv = counts[au], counts[av]
                first = len(piece.positions)
                for t in range(nv + 1):
                    for s in range(nu + 1):
                        p = [0.0, 0.0, 0.0]
                        p[axis] = hi[axis] if sign > 0 else lo[axis]
                        p[au] = coords[au][s]
                        p[av] = coords[av][t] if sv else coords[av][nv - t]
                        piece.positions.append(tuple(p))
                        piece.normals.append(tuple(n))
                        piece.uv0.append(kitglb.palette_uv(cell))
                        piece.uv1.append((0.0, 0.0))
                        piece.shade.append(kitglb.face_shade(n))
                row = nu + 1
                for t in range(nv):
                    for s in range(nu):
                        p00 = first + t * row + s
                        p10, p01, p11 = p00 + 1, p00 + row, p00 + row + 1
                        piece.indices += [p00, p01, p11, p00, p11, p10]
    return piece


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def scaled_boxes(boxes, factor):
    return [(tuple(c * factor for c in lo), tuple(c * factor for c in hi), cell) for lo, hi, cell in boxes]


# The three fixtures: (id, category, tags, size, slots, boxes, source). Front is +Z, so backs stand at -Z.
CHAIR_SEAT, CHAIR_BACK = 97, 70
TABLE_TOP, TABLE_LEGS = 84, 66
COUCH_SEAT, COUCH_FRAME = 105, 75

CHAIR_BOXES = [((-0.23, 0.0, -0.25), (0.23, 0.46, 0.25), CHAIR_SEAT),
               ((-0.23, 0.46, -0.25), (0.23, 0.90, -0.17), CHAIR_BACK)]
TABLE_BOXES = [((-0.60, 0.70, -0.40), (0.60, 0.74, 0.40), TABLE_TOP)] + [
    ((x0, 0.0, z0), (x0 + 0.06, 0.70, z0 + 0.06), TABLE_LEGS) for x0 in (-0.55, 0.49) for z0 in (-0.35, 0.29)]
# One arm, on +X only: the couch is not mirror-symmetric, so it shows which way x and the turns run.
COUCH_BOXES = [((-0.90, 0.0, -0.425), (0.70, 0.42, 0.425), COUCH_SEAT),
               ((-0.90, 0.42, -0.425), (0.70, 0.80, -0.22), COUCH_FRAME),
               ((0.70, 0.0, -0.425), (0.90, 0.62, 0.425), COUCH_FRAME)]

FIXTURES = [
    ("chair_box_min", "chair", ["dining"], [0.46, 0.90, 0.50],
     [("seat", CHAIR_SEAT), ("back", CHAIR_BACK)], CHAIR_BOXES),
    ("table_slab_min", "table", ["rectangular"], [1.20, 0.74, 0.80],
     [("top", TABLE_TOP), ("legs", TABLE_LEGS)], TABLE_BOXES),
    ("couch_box3_min", "couch", ["rectangular"], [1.80, 0.80, 0.85],
     [("seat", COUCH_SEAT), ("frame", COUCH_FRAME)], COUCH_BOXES),
]
FIXTURE_SOURCE = "ATLAS test fixture, generated by tools/props/reference.py"

# Library entries for the matcher vectors only (no file): (id, category, tags, size, boxes).
EXTRA = [
    # Same proportions as chair_box_min (so the same aspect term) but a lower seat and a thick back.
    ("chair_a_scaled", "chair", ["dining"], [0.552, 1.08, 0.60],
     [((-0.276, 0.0, -0.30), (0.276, 0.40, 0.30), CHAIR_SEAT),
      ((-0.276, 0.40, -0.30), (0.276, 1.08, -0.10), CHAIR_BACK)]),
    ("chair_test_stool", "chair", ["stool", "swivel"], [0.40, 0.65, 0.40],
     [((-0.20, 0.0, -0.20), (0.20, 0.65, 0.20), CHAIR_SEAT)]),
    ("couch_test_lshape", "couch", ["lShaped"], [2.00, 0.80, 1.60],
     [((-1.00, 0.0, -0.80), (1.00, 0.42, -0.10), COUCH_SEAT),
      ((0.30, 0.0, -0.10), (1.00, 0.42, 0.80), COUCH_SEAT),
      ((-1.00, 0.42, -0.80), (1.00, 0.80, -0.60), COUCH_FRAME)]),
    ("couch_test_plain", "couch", ["rectangular"], [2.00, 0.80, 0.90],
     [((-0.80, 0.0, -0.45), (0.80, 0.42, 0.45), COUCH_SEAT),
      ((-0.80, 0.42, -0.45), (0.80, 0.80, -0.25), COUCH_FRAME),
      ((-1.00, 0.0, -0.45), (-0.80, 0.62, 0.45), COUCH_FRAME),
      ((0.80, 0.0, -0.45), (1.00, 0.62, 0.45), COUCH_FRAME)]),
    ("table_test_square", "table", ["square"], [0.90, 0.75, 0.90],
     [((-0.45, 0.71, -0.45), (0.45, 0.75, 0.45), TABLE_TOP),
      ((-0.05, 0.0, -0.05), (0.05, 0.71, 0.05), TABLE_LEGS)]),
]


def write_fixtures(folder: Path) -> dict[str, pt.PropGlb]:
    """Writes the three fixture props and their manifest; returns each prop's mesh as read back from its file."""
    folder.mkdir(parents=True, exist_ok=True)
    meshes: dict[str, pt.PropGlb] = {}
    entries = []
    for pid, category, tags, size, slots, boxes in FIXTURES:
        piece = box_piece(boxes)
        kitglb.check_piece(pid, piece, 2500, check_edges=True)
        data = kitglb.write_glb(piece)
        pt.glb_path(folder, pid).write_bytes(data)
        mesh = pt.read_prop_glb(data)
        meshes[pid] = mesh
        entries.append({
            "id": pid, "category": category, "tags": tags, "size": size,
            "triangles": mesh.triangles, "sha256": pt.sha256(data),
            "slots": [{"name": name, "cell": cell} for name, cell in slots],
            "profile": pt.encode(pt.profile_of(mesh, size)),
            "source": FIXTURE_SOURCE, "licence": "CC0-1.0",
        })
    (folder / pt.MANIFEST).write_bytes(pt.dumps_manifest({"schema": pt.SCHEMA, "props": entries}))
    return meshes


def mesh_of(boxes) -> pt.PropGlb:
    """A box mesh as it would be stored and read back: the same float32 rounding as a fixture file."""
    return pt.read_prop_glb(kitglb.write_glb(box_piece(boxes)))


# --------------------------------------------------------------------------- the matcher rule (plan 6.4)

def match(query: dict, library: list[dict]):
    """(found, chosen, candidates) for one query, exactly as the plan's matcher rule reads.

    Ties: every candidate whose score is less than TIE above the lowest is tied; among those the smaller
    quarter turn wins, then the ordinal-smaller id. Tags count distinct strings both sides hold.
    """
    turns = FACING_TURNS[query["facing"]]
    q_profile = query["profileBytes"]
    use_profile = q_profile is not None and forge_profile.coverage(q_profile) >= MIN_COVERAGE
    q_tags = set(query["tags"])
    size = np.asarray(query["size"], dtype=np.float64)
    candidates = []
    for index, prop in enumerate(library):
        if prop["category"] != query["category"]:
            continue
        for turn in turns:
            w, h, d = prop["size"]
            e = np.array([d, h, w] if turn % 2 else [w, h, d], dtype=np.float64)
            lam = np.log(size / e)
            aspect = float(np.abs(lam - lam.mean()).mean())
            fit = float(forge_profile.fit_score(q_profile, forge_profile.rotate(prop["profileBytes"], turn))) \
                if use_profile else 0.0
            tags = min(MAX_TAG_BONUS, TAG_BONUS * len(q_tags & set(prop["tags"])))
            score = fit + ASPECT_WEIGHT * aspect - tags
            candidates.append({"index": index, "id": prop["id"], "quarterTurns": turn, "fit": fit,
                               "aspect": aspect, "tagBonus": tags, "score": score})
    if not candidates:
        return False, None, candidates
    best = min(c["score"] for c in candidates)
    tied = [c for c in candidates if c["score"] - best < TIE]
    chosen = min(tied, key=lambda c: (c["quarterTurns"], c["id"]))
    # Refuse a vector that float32 could decide the other way.
    for c in candidates:
        gap = c["score"] - best - TIE
        if abs(gap) < DECISION_MARGIN:
            raise RuntimeError(f"query {query['name']}: candidate {c['id']} turn {c['quarterTurns']} is "
                               f"{gap:+.2e} from the tie threshold")
    if use_profile and abs(forge_profile.coverage(q_profile) - MIN_COVERAGE) < 1.0 / 256:
        raise RuntimeError(f"query {query['name']}: coverage too close to {MIN_COVERAGE}")
    return True, chosen, candidates


# --------------------------------------------------------------------------- vectors

def b64(profile) -> str:
    return forge_profile.encode(profile)


def num(x) -> float:
    """A float as the vectors hold it: rounded to 1e-12 so the text is short, still far inside the 1e-4 tolerance."""
    value = round(float(x), 12)
    return 0.0 if value == 0.0 else value


def unity_vertices(mesh: pt.PropGlb) -> np.ndarray:
    """The stored positions in Unity's frame (x negated back), exactly the float32 values."""
    v = mesh.positions.copy()
    v[:, 0] = -v[:, 0] + 0.0
    return v


def convention_vectors(couch: pt.PropGlb, size) -> list[dict]:
    """ObjectProfile.Compute on the couch fixture must give of_mesh's bytes, floor-class triangles left out."""
    expected = pt.profile_of(couch, size)
    w, h, d = size
    local = unity_vertices(couch)
    faces = couch.faces[:, [0, 2, 1]]            # Unity's winding; the profile does not depend on it
    # Two Floor-class triangles across the whole footprint at 0.7 m, above the seat (0.42 m): counted, they
    # would raise every seat cell, so the vector fails unless Floor-class triangles are left out.
    floor = np.array([[-w, 0.7, -d], [w, 0.7, -d], [w, 0.7, d], [-w, 0.7, d]], dtype=np.float64)
    base_index = len(local)
    local = np.vstack([local, floor])
    faces = np.vstack([faces, [[base_index, base_index + 2, base_index + 1],
                               [base_index, base_index + 3, base_index + 2]]])
    classes = [0] * len(couch.faces) + [2, 2]

    out = []
    for name, yaw, base in (("boxFrame", 0.0, (0.0, 0.0, 0.0)), ("yawedAndMoved", 0.7, (1.25, 0.1, -2.5))):
        yaw32 = float(np.float32(yaw))
        c, s = math.cos(yaw32), math.sin(yaw32)
        # ObjectProfile: yaw psi carries local +Z to (sin psi, cos psi) and local +X to (cos psi, -sin psi).
        room = np.empty_like(local)
        room[:, 0] = local[:, 0] * c + local[:, 2] * s + base[0]
        room[:, 1] = local[:, 1] + base[1]
        room[:, 2] = -local[:, 0] * s + local[:, 2] * c + base[2]
        room = room.astype(np.float32).astype(np.float64)
        # The independent side: back into the box frame from the float32 room positions, then of_mesh.
        back = np.empty_like(room)
        dx, dz = room[:, 0] - np.float32(base[0]), room[:, 2] - np.float32(base[2])
        back[:, 0] = -(dx * c - dz * s)                # GLB frame: x negated
        back[:, 1] = room[:, 1] - np.float32(base[1])
        back[:, 2] = dx * s + dz * c
        keep = np.array(classes) != 2
        recomputed = forge_profile.of_mesh(back, faces[keep], size)
        if not np.array_equal(recomputed, expected):
            raise RuntimeError(f"convention vector {name}: of_mesh differs after the room transform "
                               f"({int((recomputed != expected).sum())} cells); choose another yaw")
        out.append({
            "name": name,
            "mesh": "couch_box3_min plus two Floor-class triangles at y = 0.7 over the whole footprint",
            "vertices": [num(x) for x in room.reshape(-1)],
            "indices": [int(i) for i in faces.reshape(-1)],
            "classes": classes,
            "boxBase": [num(x) for x in np.float32(base)],
            "boxSize": [num(x) for x in size],
            "boxYaw": num(yaw32),
            "profile": b64(expected),
        })
    return out


def scale_vectors(meshes: dict[str, pt.PropGlb], sizes: dict[str, list]) -> list[dict]:
    """cleanup._candidate (up axis Y) for fixed boxes: the Forge's scale formula with a = 0.15."""
    cases = [
        ("chair_box_min", 0, [0.46, 0.90, 0.50]),      # the reference size: scale 1
        ("chair_box_min", 0, [0.92, 1.80, 1.00]),      # uniform: scale 2
        ("chair_box_min", 0, [0.55, 0.85, 0.50]),      # uneven, inside the stretch limit
        ("chair_box_min", 0, [1.20, 0.50, 0.30]),      # uneven past the limit: clipped
        ("couch_box3_min", 1, [0.90, 0.75, 1.70]),     # a quarter turn: w and d swap
        ("couch_box3_min", 3, [0.85, 0.80, 1.80]),     # three quarter turns
    ]
    out = []
    for pid, turns, box in cases:
        mesh = meshes[pid]
        cand = forge_cleanup._candidate(mesh.positions, np.asarray(box, dtype=np.float64), "Y", np.eye(3), turns,
                                        MAX_ANISOTROPY)
        rotated = mesh.positions @ forge_cleanup._TURNS[turns].T
        extent = rotated.max(axis=0) - rotated.min(axis=0)
        lam = np.log(np.asarray(box) / extent)
        uniform = math.exp(float(lam.mean()))
        factor = uniform * np.clip(np.exp(lam) / uniform, 1.0 - MAX_ANISOTROPY, 1.0 + MAX_ANISOTROPY)
        scaled = cand.vertices
        mine = rotated * factor
        lo, hi = mine.min(axis=0), mine.max(axis=0)
        mine = mine - [(lo[0] + hi[0]) * 0.5, lo[1], (lo[2] + hi[2]) * 0.5]
        if not np.allclose(mine, scaled, rtol=0.0, atol=1e-12):
            raise RuntimeError(f"scale vector {pid} {box}: the formula here and cleanup._candidate differ")
        lo, hi = scaled.min(axis=0), scaled.max(axis=0)
        fitted = forge_profile.of_mesh(scaled, mesh.faces, hi - lo)
        turned = forge_profile.rotate(pt.profile_of(mesh, sizes[pid]), turns)
        if not np.array_equal(fitted, turned):
            # The quarter-turn convention (CONVENTIONS["quarterTurn"]) would be wrong.
            raise RuntimeError(f"scale vector {pid} turn {turns}: the fitted mesh's profile is not rotate(profile, "
                               f"{turns}) ({int((fitted != turned).sum())} cells differ)")
        out.append({
            "prop": pid, "quarterTurns": turns, "box": box, "maxAnisotropy": MAX_ANISOTROPY,
            "extentIn": [num(x) for x in extent],
            "lambda": [num(x) for x in lam],
            "uniform": num(uniform),
            "scale": [num(x) for x in factor],
            "extentOut": [num(x) for x in hi - lo],
            # Unity's frame (x negated back): base-centred, so lo.y = 0 and x, z symmetric.
            "boundsMin": [num(-hi[0]), num(lo[1]), num(lo[2])],
            "boundsMax": [num(-lo[0]), num(hi[1]), num(hi[2])],
            "fittedProfile": b64(fitted),
        })
    return out


def matcher_vectors(library: list[dict], profiles: dict[str, np.ndarray]) -> dict:
    couch = profiles["couch_box3_min"]
    low = couch.copy()
    low[np.flatnonzero(low)[60:]] = 0                     # 60 of 256 cells seen: coverage 0.234 < 0.30
    s = lambda pid, f=1.0: [num(x * f) for x in next(p["size"] for p in library if p["id"] == pid)]  # noqa: E731
    sw = lambda pid, f=1.0: (lambda v: [v[2], v[1], v[0]])(s(pid, f))  # noqa: E731  (w and d swapped)
    queries = [
        ("profile_picks_shape", "chair", "Profile", [], s("chair_box_min", 1.05), profiles["chair_box_min"]),
        ("no_profile_tie_breaks_by_id", "chair", "Profile", [], s("chair_box_min", 1.05), None),
        ("tags_pick_stool", "chair", "Profile", ["swivel", "stool", "notATag"], [0.42, 0.60, 0.42], None),
        ("profile_rule_keeps_turn_0", "chair", "Profile", [], s("chair_box_min"),
         forge_profile.rotate(profiles["chair_box_min"], 2)),
        ("box_rule_turns_chair", "chair", "Box", [], s("chair_box_min"),
         forge_profile.rotate(profiles["chair_box_min"], 2)),
        ("box_rule_turns_couch", "couch", "Box", [], sw("couch_box3_min", 1.1),
         forge_profile.rotate(couch, 1)),
        ("table_rule_low_coverage", "table", "Table", [], sw("table_slab_min"), low),
        ("clearance_with_tags", "couch", "Clearance", ["lShaped"], s("couch_test_lshape"),
         profiles["couch_test_lshape"]),
        ("no_prop_of_category", "bed", "Box", [], [1.6, 0.5, 2.0], profiles["couch_box3_min"]),
    ]
    out = []
    for name, category, facing, tags, size, profile in queries:
        q = {"name": name, "category": category, "facing": facing, "tags": tags, "size": size,
             "profileBytes": profile}
        found, chosen, candidates = match(q, library)
        out.append({
            "name": name, "category": category, "facing": facing, "tags": tags, "size": size,
            "profile": b64(profile) if profile is not None else None,
            "coverage": num(forge_profile.coverage(profile)) if profile is not None else 0.0,
            "expected": ({"found": True, "index": chosen["index"], "id": chosen["id"],
                          "quarterTurns": chosen["quarterTurns"], "fit": num(chosen["fit"]),
                          "score": num(chosen["score"])} if found else {"found": False}),
            "candidates": [{k: (num(v) if isinstance(v, float) else v) for k, v in c.items()} for c in candidates],
        })
    return {
        "library": [{k: v for k, v in p.items() if k != "profileBytes"} for p in library],
        "queries": out,
    }


CONVENTIONS = {
    "profile": "256 bytes, Unity box frame: origin at the centre of the base, +Y up, i along local +X, j along "
               "local +Z, index j * 16 + i, row 0 at the lowest z, 0 = no hit, else clamp(floor(255 * y / h + 0.5), "
               "1, 255); hits below 0.02 m or above h + 0.05 m and Floor-class triangles do not count "
               "(ObjectProfile.cs, profile.py of_mesh). Profiles are standard base64 with padding.",
    "rotate": "rotate(p, k) is ObjectProfile.Rotate(p, k): new cell (i, j) takes old cell (j, 15 - i) for k = 1, "
              "(15 - i, 15 - j) for k = 2, (15 - j, i) for k = 3.",
    "quarterTurn": "A quarter turn k of a prop mesh is the turn whose profile is rotate(profile, k): in Unity's "
                   "frame (x, y, z) -> (-z, y, x), applied k times (cleanup._YAW90 in the GLB frame). The prop's "
                   "size turns with it: odd k gives (d, h, w). scale[*].fittedProfile, of_mesh of the fitted "
                   "mesh at its own extent (extentOut), equals rotate(profile, k) in every scale vector.",
    "scale": "lambda_k = ln(box_k / e_k) with e the turned mesh's extent, u = exp(mean lambda), "
             "s_k = u * clip(exp(lambda_k) / u, 1 - a, 1 + a), a = 0.15; then the origin moves to the centre of "
             "the base. The result's extent is extentOut, not box, when the clip acts.",
    "fitScore": "fit_score(scan, generated): mean over the cells the scan saw (scan != 0) of |generated - scan| / 255.",
    "matcher": "Candidates: props of the query's category. Turns: {0} for Profile and Clearance, {0,1,2,3} for Box "
               "and Table. e = prop size turned (odd turns swap w and d); lambda_k = ln(size_k / e_k); "
               "A = mean_k |lambda_k - mean(lambda)|. F = fit_score(q.profile, rotate(prop.profile, turn)) when "
               "coverage(q.profile) >= 0.30, else 0 (a null profile has coverage 0). tags = min(0.06, 0.03 * the "
               "number of distinct strings in both tag lists, ordinal). Score = F + 0.5 A - tags. Tie: every "
               "candidate with score - lowest < 0.02 is tied; among them the smaller turn wins, then the ordinal-"
               "smaller id. expected.index is the index in matcher.library. found = false when no prop has the "
               "category.",
    "tolerance": "Floats are compared within 1e-4; choices (found, index, id, quarterTurns) and profile bytes exactly.",
}


def build(fixtures_dir: Path) -> dict:
    meshes = write_fixtures(fixtures_dir)
    manifest = pt.load_manifest(fixtures_dir)
    sizes = {e["id"]: e["size"] for e in manifest["props"]}
    profiles = {pid: forge_profile.decode(e["profile"]) for pid, e in ((e["id"], e) for e in manifest["props"])}

    library = [{"id": e["id"], "category": e["category"], "tags": e["tags"], "size": e["size"],
                "profile": e["profile"], "profileBytes": profiles[e["id"]]} for e in manifest["props"]]
    for pid, category, tags, size, boxes in EXTRA:
        mesh = mesh_of(boxes)
        extent = unity_vertices(mesh).max(axis=0) - unity_vertices(mesh).min(axis=0)
        if np.abs(extent - np.asarray(size)).max() > 1e-6:
            raise RuntimeError(f"{pid}: the boxes span {extent}, not {size}")
        profiles[pid] = pt.profile_of(mesh, size)
        library.append({"id": pid, "category": category, "tags": tags, "size": size,
                        "profile": b64(profiles[pid]), "profileBytes": profiles[pid]})

    couch = profiles["couch_box3_min"]
    fit_pairs = [("chair_box_min", "chair_box_min"), ("chair_box_min", "chair_a_scaled"),
                 ("chair_box_min", "chair_test_stool"), ("chair_box_min", "couch_box3_min"),
                 ("couch_box3_min", "couch_test_plain"), ("couch_box3_min", "table_slab_min")]
    fit = [{"scan": b64(profiles[a]), "generated": b64(profiles[b]), "label": f"{a} vs {b}",
            "fit": num(forge_profile.fit_score(profiles[a], profiles[b]))} for a, b in fit_pairs]
    fit += [{"scan": b64(couch), "generated": b64(forge_profile.rotate(couch, k)), "label": f"couch vs rotate {k}",
             "fit": num(forge_profile.fit_score(couch, forge_profile.rotate(couch, k)))} for k in (1, 2, 3)]

    return {
        "schema": "atlas.props.reference/1",
        "generator": "tools/props/reference.py (Forge profile.py and cleanup.py)",
        "conventions": CONVENTIONS,
        "constants": {"aspectWeight": ASPECT_WEIGHT, "tagBonus": TAG_BONUS, "maxTagBonus": MAX_TAG_BONUS,
                      "tie": TIE, "minCoverage": MIN_COVERAGE, "maxAnisotropy": MAX_ANISOTROPY},
        "profiles": [{"prop": e["id"], "size": e["size"], "profile": e["profile"],
                      "coverage": num(forge_profile.coverage(profiles[e["id"]]))} for e in manifest["props"]],
        "convention": convention_vectors(meshes["couch_box3_min"], sizes["couch_box3_min"]),
        "rotate": [{"turns": k, "profile": b64(forge_profile.rotate(couch, k))} for k in range(4)],
        "rotateSource": "couch_box3_min",
        "fitScore": fit,
        "scale": scale_vectors(meshes, sizes),
        "matcher": matcher_vectors(library, profiles),
    }


def dumps(doc: dict) -> bytes:
    """Two-space JSON with every list of plain numbers on one line, so the vectors stay readable."""
    text = json.dumps(doc, indent=2, ensure_ascii=True, allow_nan=False)
    text = re.sub(r"\[\s+((?:-?[0-9][0-9.e+-]*,\s+)*-?[0-9][0-9.e+-]*)\s+\]",
                  lambda m: "[" + re.sub(r",\s+", ", ", m.group(1)) + "]", text)
    return (text + "\n").encode("utf-8")


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--fixtures", type=Path, default=pt.FIXTURES_DIR, help="where the fixture props go")
    ap.add_argument("--out", type=Path, default=REFERENCE, help="where reference.json goes")
    args = ap.parse_args(argv)
    doc = build(args.fixtures)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_bytes(dumps(doc))
    print(f"wrote {args.fixtures} and {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
