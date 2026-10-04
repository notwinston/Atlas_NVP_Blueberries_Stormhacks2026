from __future__ import annotations

import io
import json
import struct
import sys
import time
from pathlib import Path

import numpy as np
import pytest
from fastapi.testclient import TestClient
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from forge.app import create_app  # noqa: E402
from forge.config import Settings  # noqa: E402

FIXTURE_ROOMS = ROOT.parent.parent / "ATLAS" / "Assets" / "Atlas" / "World" / "Resources" / "AtlasRooms"


def make_settings(data_dir: Path, **overrides) -> Settings:
    settings = Settings()
    settings.data_dir = Path(data_dir)
    settings.generator = "procedural"
    settings.hub_url = ""  # no Hub in tests
    for key, value in overrides.items():
        setattr(settings, key, value)
    return settings


def jpeg(colour=(150, 112, 70), size=256, seed=0) -> bytes:
    """A synthetic crop: an object-coloured block on a grey background."""
    rng = np.random.default_rng(seed)
    pixels = np.full((size, size, 3), 96, dtype=np.uint8)
    pixels[size // 6 : -size // 6, size // 6 : -size // 6] = colour
    pixels = np.clip(pixels.astype(np.int16) + rng.integers(-3, 4, pixels.shape), 0, 255).astype(np.uint8)
    out = io.BytesIO()
    Image.fromarray(pixels).save(out, format="JPEG", quality=85)
    return out.getvalue()


def room_model(room_id: str, sizes, categories=None) -> dict:
    """A minimal RoomModel holding the fields the Forge reads."""
    return {
        "version": 1,
        "id": room_id,
        "createdUtc": "2026-10-01T00:00:00Z",
        "objects": [
            {
                "id": i + 1,
                "category": (categories[i] if categories else "storage"),
                "base": [0.0, 0.0, 0.0],
                "size": list(size),
                "yaw": 0.0,
                "mesh": "placeholder",
                "assetHash": "",
                "triTarget": 0,
            }
            for i, size in enumerate(sizes)
        ],
    }


def meta_for(obj: dict, tri_target: int, backend: str = "local") -> dict:
    return {
        "boxId": obj["id"],
        "category": obj["category"],
        "dims": obj["size"],
        "yaw": obj.get("yaw", 0.0),
        "triTarget": tri_target,
        "backend": backend,
        "views": [{"score": 0.9, "azimuthDeg": 0.0, "intrinsics": [600, 600, 384, 384],
                   "camToBox": [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 1.5, 1]}],
    }


def post_object(client, room_id: str, meta: dict, views):
    files = {f"view{i}": (f"view{i}.jpg", data, "image/jpeg") for i, data in enumerate(views)}
    return client.post(f"/rooms/{room_id}/objects", data={"meta": json.dumps(meta)}, files=files)


def wait_for(client, job_id: str, timeout: float = 60.0) -> dict:
    deadline = time.monotonic() + timeout
    while True:
        status = client.get(f"/jobs/{job_id}").json()
        if status["state"] in ("ready", "failed", "placeholder"):
            return status
        assert time.monotonic() < deadline, f"job {job_id} still {status['state']}"
        time.sleep(0.02)


def repack_glb(doc: dict, blob: bytes) -> bytes:
    """Write a GLB from an edited JSON document, for building contract violations."""
    text = json.dumps(doc).encode()
    text += b" " * ((4 - len(text) % 4) % 4)
    blob += b"\x00" * ((4 - len(blob) % 4) % 4)
    total = 12 + 8 + len(text) + 8 + len(blob)
    return (struct.pack("<III", 0x46546C67, 2, total) + struct.pack("<II", len(text), 0x4E4F534A) + text
            + struct.pack("<II", len(blob), 0x004E4942) + blob)


@pytest.fixture
def settings(tmp_path):
    return make_settings(tmp_path / "data")


@pytest.fixture
def client(settings):
    with TestClient(create_app(settings)) as c:
        yield c
