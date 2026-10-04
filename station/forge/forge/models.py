"""Request and response bodies of the Forge HTTP interface (Master Plan Appendix B.4)."""
from __future__ import annotations

from typing import Any, Literal, Optional

from pydantic import BaseModel, ConfigDict, Field

from .budget import VALIDATOR_MAX_TRIANGLES, VALIDATOR_MIN_TRIANGLES

Vec3 = tuple[float, float, float]

ROOM_ID_PATTERN = r"^[A-Za-z0-9_]{1,40}$"


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
    backend: Literal["local", "hosted", "cache"] = "local"
    views: list[ViewMeta] = Field(default_factory=list, max_length=3)


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
    hostedEnabled: bool
    matting: str


class RoomCreated(BaseModel):
    roomId: str
    roomHash: str
    generated: list[int]
    placeholders: list[int]
    triTarget: int


class JobCreated(BaseModel):
    jobId: str
    cached: Optional[bool] = None
    sha256: Optional[str] = None
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
    cached: bool = False
    generator: str = ""
    queuePosition: Optional[int] = None
    sha256: Optional[str] = None
    url: Optional[str] = None
    bytes: Optional[int] = None
    tris: Optional[int] = None
    maxEdge: Optional[float] = None
    bounds: Optional[Vec3] = None
    palette: Optional[int] = None
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
