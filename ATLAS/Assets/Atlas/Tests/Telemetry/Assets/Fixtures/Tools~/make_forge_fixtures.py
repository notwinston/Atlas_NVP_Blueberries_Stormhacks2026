#!/usr/bin/env python3
"""Writes the GLB fixtures of the asset-client tests with the Forge's own code.

Run with the Forge's interpreter, from anywhere:

    station/forge/.venv/bin/python "ATLAS/Assets/Atlas/Tests/Telemetry/Assets/Fixtures/Tools~/make_forge_fixtures.py"

Good files come straight from the procedural generator and the clean-up (the
same path a job takes). Bad files are a good file with one thing edited, so each
breaks exactly one rule of the asset contract (lane-d-link.md section 4.1).
`fixtures.json` lists every file with the box, the triangle target, the hash and
the code the phone's validator must answer.
"""
from __future__ import annotations

import hashlib
import json
import struct
import sys
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
OUT = HERE.parent
REPO = HERE.parents[7]
sys.path.insert(0, str(REPO / "station" / "forge"))

from forge.cleanup import clean  # noqa: E402
from forge.generators.procedural import build_furniture  # noqa: E402
from forge.glb import read_glb  # noqa: E402
from forge.validator import validate_glb  # noqa: E402

GOOD = [
    # name, category, dims [w, h, d], yaw, triangle target
    ("forge_table", "table", [1.2, 0.75, 0.8], 0.0, 5000),
    ("forge_chair", "chair", [0.5, 0.9, 0.5], 1.570796, 3000),
    ("forge_couch", "couch", [2.0, 0.85, 0.9], -0.6, 4615),
    ("forge_plant", "plant", [0.4, 1.2, 0.4], 0.0, 3000),
]


def repack(doc: dict, blob: bytes) -> bytes:
    text = json.dumps(doc, separators=(",", ":")).encode()
    text += b" " * ((4 - len(text) % 4) % 4)
    blob += b"\x00" * ((4 - len(blob) % 4) % 4)
    total = 12 + 8 + len(text) + 8 + len(blob)
    return (struct.pack("<III", 0x46546C67, 2, total) + struct.pack("<II", len(text), 0x4E4F534A) + text
            + struct.pack("<II", len(blob), 0x004E4942) + blob)


def edit(data: bytes, change) -> bytes:
    glb = read_glb(data)
    doc = json.loads(json.dumps(glb.doc))
    blob = bytearray(glb.blob)
    blob = change(doc, blob) or blob
    doc["buffers"][0]["byteLength"] = len(blob)
    return repack(doc, bytes(blob))


def too_many_triangles(doc, blob):
    # The index buffer twelve times over: the same surface, 12 x the triangles, past the 15,000 of the full copy.
    view = doc["bufferViews"][doc["accessors"][0]["bufferView"]]
    start, length = view["byteOffset"], view["byteLength"]
    indices = bytes(blob[start:start + length])
    while len(blob) % 4:
        blob.append(0)
    doc["bufferViews"].append({"buffer": 0, "byteOffset": len(blob), "byteLength": 12 * length, "target": 34963})
    doc["accessors"][0]["bufferView"] = len(doc["bufferViews"]) - 1
    doc["accessors"][0]["count"] *= 12
    blob.extend(indices * 12)
    return blob


def long_edge(doc, blob):
    # One vertex pulled 0.4 m along x, inside the bounds, so only the edge rule breaks.
    acc = doc["accessors"][doc["meshes"][0]["primitives"][0]["attributes"]["POSITION"]]
    start = doc["bufferViews"][acc["bufferView"]]["byteOffset"]
    positions = np.frombuffer(bytes(blob), dtype="<f4", count=acc["count"] * 3, offset=start).reshape(-1, 3).copy()
    k = int(np.argmin(positions[:, 0]))
    positions[k, 0] += 0.4
    blob[start:start + positions.nbytes] = positions.tobytes()


def with_texture(doc, blob):
    doc["images"] = [{"uri": "wood.png"}]
    doc["samplers"] = [{}]
    doc["textures"] = [{"source": 0, "sampler": 0}]
    doc["materials"] = [{"pbrMetallicRoughness": {"baseColorTexture": {"index": 0}}}]
    doc["meshes"][0]["primitives"][0]["material"] = 0


def moved_node(doc, blob):
    doc["nodes"][0]["translation"] = [0.0, 0.5, 0.0]


def lifted(doc, blob):
    acc = doc["accessors"][doc["meshes"][0]["primitives"][0]["attributes"]["POSITION"]]
    start = doc["bufferViews"][acc["bufferView"]]["byteOffset"]
    positions = np.frombuffer(bytes(blob), dtype="<f4", count=acc["count"] * 3, offset=start).reshape(-1, 3).copy()
    positions[:, 1] += 0.3
    blob[start:start + positions.nbytes] = positions.tobytes()


BAD = [
    # name, source, edit, code of the phone's validator
    ("bad_too_many_triangles", "forge_chair", too_many_triangles, "tris_max"),
    ("bad_long_edge", "forge_table", long_edge, "edge"),
    ("bad_texture", "forge_table", with_texture, "no_material"),
    ("bad_node_transform", "forge_table", moved_node, "structure"),
    ("bad_off_the_floor", "forge_table", lifted, "origin"),
]


def main() -> int:
    index, good = [], {}
    for name, category, dims, yaw, target in GOOD:
        vertices, faces = build_furniture(category, dims)
        result = clean(vertices, faces, dims, target, yaw=yaw)
        check = validate_glb(result.glb, tri_target=target, box=dims)
        assert check.ok, (name, check.codes)
        good[name] = (result.glb, dims, yaw, target, category)
        (OUT / f"{name}.glb.bytes").write_bytes(result.glb)
        index.append({"file": f"{name}.glb.bytes", "category": category, "dims": dims, "yaw": yaw,
                      "triTarget": target, "sha256": hashlib.sha256(result.glb).hexdigest(),
                      "triangles": check.triangles, "vertices": check.vertices, "expect": "ok"})
    for name, source, change, code in BAD:
        data, dims, yaw, target, category = good[source]
        bad = edit(data, change)
        (OUT / f"{name}.glb.bytes").write_bytes(bad)
        index.append({"file": f"{name}.glb.bytes", "category": category, "dims": dims, "yaw": yaw,
                      "triTarget": target, "sha256": hashlib.sha256(bad).hexdigest(), "expect": code})
    (OUT / "fixtures.json").write_text(json.dumps({"fixtures": index}, indent=2) + "\n")
    print(f"{len(index)} fixtures in {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
