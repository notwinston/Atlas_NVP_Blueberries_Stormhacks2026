"""Request and response bodies of the Forge HTTP interface (Master Plan Appendix B.4)."""
from __future__ import annotations

import base64
import binascii
from typing import Any, Literal, Optional

from pydantic import BaseModel, ConfigDict, Field, field_validator

from .budget import VALIDATOR_MAX_TRIANGLES, VALIDATOR_MAX_TRIANGLES_LIGHT, VALIDATOR_MIN_TRIANGLES

Vec3 = tuple[float, float, float]

ROOM_ID_PATTERN = r"^[A-Za-z0-9_]{1,40}$"
# The object profile of lane C: 16 x 16 heights of the scanned surface inside the box, one byte each.
PROFILE_BYTES = 256


def decode_profile(text: str) -> bytes:
    """The 256 bytes of a profile sent as standard base64 with padding; ValueError for anything else."""
    try:
        raw = base64.b64decode(text, validate=True)
    except (binascii.Error, ValueError) as exc:
        raise ValueError(f"profile is not standard base64: {exc}") from None
    if len(raw) != PROFILE_BYTES:
        raise ValueError(f"profile decodes to {len(raw)} bytes; exactly {PROFILE_BYTES} are required")
    return raw


class RoomObject(BaseModel):
    """The fields of a RoomModel object the Forge reads; the rest is kept untouched."""

    model_config = ConfigDict(extra="allow")

    id: int = Field(ge=1, le=255)
    category: str = "unknown"
    size: Vec3


class RoomModel(BaseModel):
    """A RoomModel as uploaded. The Forge stores the document whole and reads only these fields."""

    model_config = ConfigDict(extra="allow")

    version: Literal[1]
    id: str = Field(pattern=ROOM_ID_PATTERN)
    objects: list[RoomObject] = Field(default_factory=list, max_length=64)


class ViewMeta(BaseModel):
    model_config = ConfigDict(extra="ignore")

    score: float = 0.0
    azimuthDeg: float = 0.0
    intrinsics: Optional[tuple[float, float, float, float]] = None
    camToBox: Optional[list[float]] = Field(default=None, min_length=16, max_length=16)


class ObjectMeta(BaseModel):
    """The `meta` part of POST /rooms/{roomId}/objects."""

    model_config = ConfigDict(extra="ignore")

    boxId: int = Field(ge=1, le=255)
    category: str = Field(default="unknown", max_length=32)
    dims: Vec3
    yaw: float = 0.0
    triTarget: int = Field(ge=VALIDATOR_MIN_TRIANGLES, le=VALIDATOR_MAX_TRIANGLES)
    # The target of the light copy. An older phone does not send it; the Forge then uses its own figure for the room.
    triTargetLight: Optional[int] = Field(default=None, ge=VALIDATOR_MIN_TRIANGLES, le=VALIDATOR_MAX_TRIANGLES_LIGHT)
    # The object profile as the RoomModel holds it (`objects[k].profile`). Absent when the scan gave none.
    profile: Optional[str] = None
    backend: Literal["local", "hosted", "cache"] = "local"
    views: list[ViewMeta] = Field(default_factory=list, max_length=3)

    @field_validator("profile")
    @classmethod
    def _profile_is_256_bytes(cls, value: Optional[str]) -> Optional[str]:
        if value is not None:
            decode_profile(value)
        return value


class Health(BaseModel):
    ok: bool
    gpu: bool
    gpuName: Optional[str] = None
    queue: int
    pipelineVer: str
    debugKeep: bool
    generator: str
    generatorReady: bool
    generatorNote: str = ""
    # The hosted fallback is armed. "hosted" is the name the Hub and the Console read; "hostedEnabled" is the
    # same flag under its older name.
    hosted: bool
    hostedEnabled: bool
    matting: str


class RoomCreated(BaseModel):
    roomId: str
    roomHash: str
    generated: list[int]
    placeholders: list[int]
    # The largest full target of the room: what a reader that knows one figure per room may ask for.
    triTarget: int
    # The full target of every box, 0 for a box that receives no mesh; and the room's light target.
    triTargets: dict[int, int]
    triTargetLight: int


class JobCreated(BaseModel):
    jobId: str
    cached: Optional[bool] = None
    sha256: Optional[str] = None
    # With `cached`: the light copy of the cached mesh, when it has one.
    lightSha256: Optional[str] = None
    lightUrl: Optional[str] = None
    lightBytes: Optional[int] = None
    lightTris: Optional[int] = None
    placeholder: Optional[bool] = None


class JobStatus(BaseModel):
    """`JobStatus` of Appendix B.2, with the `AssetReady` fields once the job is ready."""

    jobId: str
    roomId: str
    boxId: int
    state: Literal["queued", "matting", "generating", "cleaning", "ready", "failed", "placeholder"]
    source: Literal["local", "hosted", "cache"]
    error: str = ""
    triTarget: int
    triTargetLight: Optional[int] = None
    cached: bool = False
    generator: str = ""
    # Which of TripoSG's decoders produced the mesh: "flash" or "hierarchical".
    decoder: Optional[str] = None
    queuePosition: Optional[int] = None
    sha256: Optional[str] = None
    url: Optional[str] = None
    bytes: Optional[int] = None
    tris: Optional[int] = None
    maxEdge: Optional[float] = None
    bounds: Optional[Vec3] = None
    palette: Optional[int] = None
    # The light copy (mesh design section 5.3); absent when the job has none.
    lightSha256: Optional[str] = None
    lightUrl: Optional[str] = None
    lightBytes: Optional[int] = None
    lightTris: Optional[int] = None
    # How clean-up turned the generated mesh. `fitUp`: the axis of the generator's own frame that was taken as
    # up (Y, Z or X). `fitYaw`: the turn about that axis as a yaw in Unity's box frame, 0, 90, 180 or 270 degrees
    # (positive turns +Z towards +X): the azimuth of the box at which the mesh's own +Z side stands.
    # `fit`: how far the turned mesh is from the scanned profile, 0 a perfect match; absent for a job with no
    # profile. `attempts`: the generations made for the object, failed ones included; absent for a cached answer.
    fit: Optional[float] = None
    fitYaw: Optional[int] = None
    fitUp: Optional[str] = None
    attempts: Optional[int] = None
    report: Optional[dict[str, Any]] = None


class QueueEntry(BaseModel):
    jobId: str
    roomId: str
    boxId: int
    volume: float
    state: str


class QueueView(BaseModel):
    """What the worker is doing and what waits, in the order it will be taken."""

    active: Optional[QueueEntry] = None
    queued: list[QueueEntry]
    placeholders: list[QueueEntry]
    generator: str
