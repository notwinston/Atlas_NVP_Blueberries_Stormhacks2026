"""The Forge's rooms, jobs and its one worker (task P1-D08; Master Plan section 7.4).

One worker thread owns the generator and takes one job at a time, the largest
box first. Everything the service knows is kept under the data folder:

    rooms/<roomId>/room.json, room.meta.json, mesh.bin
    jobs/<jobId>/job.json            (and view<i>.jpg, raw_view<i>.npz only under FORGE_DEBUG_KEEP=1)
    assets/<sha256>.glb, <sha256>.json
    cache/<key>.json

Files are written to a temporary name and renamed, so a crash never leaves a
half-written record, and the folder is read back at start-up. Crops, and the
mattes made from them, live in memory only and are dropped when their job ends
(section 7.4, Privacy), so a job that was waiting or running when the service
stopped is marked failed and the phone sends it again.
"""
from __future__ import annotations

import hashlib
import io
import json
import logging
import os
import queue
import re
import shutil
import threading
import time
import urllib.request
import uuid
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Optional

import numpy as np

from . import PIPELINE_VER, imaging
from .budget import MAX_GENERATED_PER_ROOM, box_volume, plan_room
from .cleanup import CleanupError, clean
from .config import Settings
from .generators import GenerationRequest, Generator, GeneratorError, HostedGenerator, make_generator
from .imaging import ImageError, MattedView, decode_crop, dominant_colour, frame_for_generator
from .models import ObjectMeta, RoomModel
from .validator import validate_glb

log = logging.getLogger("forge")

ACTIVE_STATES = ("matting", "generating", "cleaning")
TERMINAL_STATES = ("ready", "failed", "placeholder")
SHA_PATTERN = re.compile(r"^[0-9a-f]{64}$")
_ID_PATTERN = re.compile(r"^[A-Za-z0-9_]{1,40}$")


class NotFound(KeyError):
    """An unknown room, job or box."""


class StageTimeout(RuntimeError):
    """A stage ran past its time-out."""


class _Cancelled(Exception):
    """The job's room was deleted while it ran."""


def _now() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def _write_atomic(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".part")
    with open(temporary, "wb") as handle:
        handle.write(data)
        handle.flush()
        os.fsync(handle.fileno())
    os.replace(temporary, path)


def _write_json(path: Path, value: Any) -> None:
    _write_atomic(path, json.dumps(value, indent=1).encode("utf-8"))


def _read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


@dataclass
class Job:
    """One object of one room on its way to a mesh."""

    jobId: str
    roomId: str
    boxId: int
    category: str
    dims: list[float]
    yaw: float
    triTarget: int  # the target clean-up works to: never above the one that was sent
    requestedTriTarget: int
    backend: str
    triTargetLight: int = 0  # the same for the light copy; the room's own figure when the phone sent none
    requestedTriTargetLight: int = 0  # 0: an older phone, which sends no light target
    profile: str = ""  # the object profile as it was sent (base64 of 256 bytes), or empty
    state: str = "queued"
    source: str = "local"
    error: str = ""
    generator: str = ""
    decoder: str = ""
    cached: bool = False
    seq: int = 0
    volume: float = 0.0
    cacheKey: str = ""
    views: list[dict] = field(default_factory=list)
    viewCount: int = 0
    createdUtc: str = ""
    updatedUtc: str = ""
    matting: str = ""
    timings: dict[str, float] = field(default_factory=dict)
    sha256: str = ""
    bytes: int = 0
    tris: int = 0
    maxEdge: float = 0.0
    bounds: list[float] = field(default_factory=list)
    palette: int = 0
    # The light copy and the fit (mesh design section 5.3). Empty and None until clean-up makes them.
    lightSha256: str = ""
    lightBytes: int = 0
    lightTris: int = 0
    fit: Optional[float] = None
    fitYaw: Optional[int] = None
    fitUp: str = ""
    attempts: int = 0
    report: Optional[dict] = None

    @classmethod
    def from_dict(cls, data: dict) -> "Job":
        known = {name for name in cls.__dataclass_fields__}
        return cls(**{k: v for k, v in data.items() if k in known})


class Forge:
    """Rooms, jobs, assets and the worker. Thread-safe; the HTTP layer only calls into it."""

    def __init__(self, settings: Settings, generator: Optional[Generator] = None) -> None:
        self.settings = settings
        self.generator: Generator = generator or make_generator(settings)
        self.hosted = HostedGenerator(settings)
        self._root = Path(settings.data_dir)
        self._lock = threading.RLock()
        self._wake = threading.Condition(self._lock)
        self._jobs: dict[str, Job] = {}
        self._rooms: dict[str, dict] = {}
        self._crops: dict[str, list[bytes]] = {}
        # Each view of the running job, decoded and matted: the crop and its matte (section 5.1).
        self._views: dict[str, dict[int, MattedView]] = {}
        # Why the matting model could not be loaded at start-up, when the generator needs one.
        self._matting_error = ""
        self._cancelled: set[str] = set()
        self._active: Optional[str] = None
        self._seq = 0
        self._stop = False
        self._paused = False
        self._worker: Optional[threading.Thread] = None
        # The generator is used by one call at a time, even when an earlier call timed out
        # and is still running in the background.
        self._gpu_lock = threading.Lock()
        self._hub_queue: "queue.Queue[Optional[dict]]" = queue.Queue(maxsize=256)
        self._hub_thread: Optional[threading.Thread] = None
        for name in ("rooms", "jobs", "assets", "cache"):
            (self._root / name).mkdir(parents=True, exist_ok=True)
        self._recover()

    # ------------------------------------------------------------------ life cycle

    def start(self) -> None:
        with self._lock:
            if self._worker is not None:
                return
            self._stop = False
            self._worker = threading.Thread(target=self._work, name="forge-worker", daemon=True)
            self._worker.start()
            if self.settings.hub_url:
                self._hub_thread = threading.Thread(target=self._notify_loop, name="forge-hub", daemon=True)
                self._hub_thread.start()

    def stop(self, timeout: float = 5.0) -> None:
        with self._wake:
            self._stop = True
            self._wake.notify_all()
        worker, self._worker = self._worker, None
        if worker is not None:
            worker.join(timeout)
        if self._hub_thread is not None:
            self._hub_queue.put(None)
            self._hub_thread.join(timeout)
            self._hub_thread = None

    def pause(self, paused: bool = True) -> None:
        """Hold the worker before it takes its next job (used by operators and tests)."""
        with self._wake:
            self._paused = paused
            self._wake.notify_all()

    def _recover(self) -> None:
        """Read the data folder back after a restart."""
        for path in sorted((self._root / "rooms").glob("*/room.meta.json")):
            try:
                meta = _read_json(path)
                if (path.parent / "room.json").is_file():
                    if "triTargets" not in meta or "triTargetLight" not in meta:
                        # Written before the furniture budget of the mesh design: plan the room again.
                        room = RoomModel.model_validate_json((path.parent / "room.json").read_bytes())
                        meta.update(self._plan(room))
                        _write_json(path, meta)
                    # JSON has no integer keys.
                    meta["triTargets"] = {int(k): int(v) for k, v in meta["triTargets"].items()}
                    self._rooms[meta["roomId"]] = meta
            except (OSError, ValueError, KeyError) as exc:
                log.warning("ignoring unreadable room record %s: %s", path, exc)
        for path in sorted((self._root / "jobs").glob("*/job.json")):
            try:
                job = Job.from_dict(_read_json(path))
            except (OSError, ValueError, TypeError) as exc:
                log.warning("ignoring unreadable job record %s: %s", path, exc)
                continue
            self._seq = max(self._seq, job.seq + 1)
            if job.state == "ready" and not self._asset_path(job.sha256).is_file():
                job.state, job.error = "failed", "the asset file is missing after a restart"
                self._save(job)
            elif job.state not in TERMINAL_STATES:
                crops = self._kept_crops(job)
                if crops:
                    # Crops were kept on disk for debugging, so the job can simply run again.
                    self._crops[job.jobId] = crops
                    job.state, job.error = "queued", ""
                else:
                    job.state = "failed"
                    job.error = "interrupted by a Forge restart; crops are not kept, send the object again"
                self._save(job)
            self._jobs[job.jobId] = job
        if self._jobs:
            log.info("recovered %d rooms and %d jobs from %s", len(self._rooms), len(self._jobs), self._root)

    # ------------------------------------------------------------------ paths

    def _job_dir(self, job_id: str) -> Path:
        return self._root / "jobs" / job_id

    def _room_dir(self, room_id: str) -> Path:
        return self._root / "rooms" / room_id

    def _asset_path(self, sha: str) -> Path:
        return self._root / "assets" / f"{sha}.glb"

    def _save(self, job: Job) -> None:
        job.updatedUtc = _now()
        _write_json(self._job_dir(job.jobId) / "job.json", asdict(job))

    def _kept_crops(self, job: Job) -> list[bytes]:
        crops = []
        for i in range(3):
            path = self._job_dir(job.jobId) / f"view{i}.jpg"
            if not path.is_file():
                break
            crops.append(path.read_bytes())
        return crops

    # ------------------------------------------------------------------ rooms

    @staticmethod
    def _plan(room: RoomModel) -> dict:
        """Which boxes of the room receive meshes and their triangle targets (mesh design section 5.4)."""
        plan = plan_room((o.id, o.size) for o in room.objects)
        return {
            "generated": plan.generated,
            "placeholders": plan.placeholders,
            # The largest full target, for a reader that knows one figure per room.
            "triTarget": plan.largest,
            "triTargets": plan.targets,
            "triTargetLight": plan.light,
        }

    def put_room(self, room: RoomModel, raw: bytes) -> dict:
        """Store a RoomModel and work out which of its boxes receive meshes (section 4.5)."""
        meta = {
            "roomId": room.id,
            "roomHash": hashlib.sha256(raw).hexdigest(),
            **self._plan(room),
            "createdUtc": _now(),
        }
        with self._lock:
            _write_atomic(self._room_dir(room.id) / "room.json", raw)
            _write_json(self._room_dir(room.id) / "room.meta.json", meta)
            self._rooms[room.id] = meta
        return meta

    def room_meta(self, room_id: str) -> dict:
        with self._lock:
            if room_id not in self._rooms:
                raise NotFound(room_id)
            return dict(self._rooms[room_id])

    def room_json(self, room_id: str) -> bytes:
        self.room_meta(room_id)
        return (self._room_dir(room_id) / "room.json").read_bytes()

    def put_room_mesh(self, room_id: str, data: bytes) -> None:
        self.room_meta(room_id)
        _write_atomic(self._room_dir(room_id) / "mesh.bin", data)

    def delete_room(self, room_id: str) -> None:
        """Remove the room record, its mesh, its jobs and the GLBs made for it (section 7.4)."""
        with self._wake:
            if room_id not in self._rooms:
                raise NotFound(room_id)
            del self._rooms[room_id]
            mine = [j for j in self._jobs.values() if j.roomId == room_id]
            others = {j.sha256 for j in self._jobs.values() if j.roomId != room_id and j.sha256}
            for job in mine:
                del self._jobs[job.jobId]
                self._crops.pop(job.jobId, None)
                self._views.pop(job.jobId, None)
                if job.jobId == self._active:
                    self._cancelled.add(job.jobId)
                shutil.rmtree(self._job_dir(job.jobId), ignore_errors=True)
            doomed = {j.sha256 for j in mine if j.sha256} - others
            for sha in doomed:
                self._asset_path(sha).unlink(missing_ok=True)
                self._asset_path(sha).with_suffix(".json").unlink(missing_ok=True)
            for entry in (self._root / "cache").glob("*.json"):
                try:
                    if _read_json(entry).get("sha256") in doomed:
                        entry.unlink(missing_ok=True)
                except (OSError, ValueError):
                    entry.unlink(missing_ok=True)
            shutil.rmtree(self._room_dir(room_id), ignore_errors=True)

    # ------------------------------------------------------------------ jobs

    def _cache_key(self, view0: bytes, job: Job) -> str:
        # Section 7.4 keys the cache on the photo, the triangle target and the pipeline version.
        # The box, its yaw (the baked shade depends on it) and the generator are added because
        # each of them changes the mesh that comes out.
        dims = ",".join(f"{d:.3f}" for d in [*job.dims, job.yaw])
        text = "|".join(
            [hashlib.sha256(view0).hexdigest(), str(job.triTarget), PIPELINE_VER, dims, job.category,
             self.generator.name]
        )
        return hashlib.sha256(text.encode("ascii", "replace")).hexdigest()

    def _cache_lookup(self, key: str) -> Optional[dict]:
        path = self._root / "cache" / f"{key}.json"
        if not path.is_file():
            return None
        try:
            sha = _read_json(path)["sha256"]
            info = _read_json(self._asset_path(sha).with_suffix(".json"))
        except (OSError, ValueError, KeyError):
            return None
        return info if self._asset_path(sha).is_file() else None

    def submit(self, room_id: str, meta: ObjectMeta, crops: list[bytes]) -> tuple[Job, bool]:
        """Create the job for one object. Returns ``(job, created)``.

        ``created`` is False when the answer is already known: a cache hit, a box
        beyond the room's 32 that receives no mesh, or a job for the same box
        that is still waiting or running.
        """
        with self._wake:
            room = self._rooms.get(room_id)
            if room is None:
                raise NotFound(room_id)
            if meta.boxId not in room["generated"] and meta.boxId not in room["placeholders"]:
                raise NotFound(f"box {meta.boxId}")
            for existing in self._jobs.values():
                if existing.roomId == room_id and existing.boxId == meta.boxId and existing.state not in TERMINAL_STATES:
                    return existing, False

            # The room's own budget wins when the phone asks for more than it allows; a smaller
            # target from the phone wins over the room's. The same for the light copy.
            own = room["triTargets"].get(meta.boxId, 0)
            light = room["triTargetLight"]
            job = Job(
                jobId=uuid.uuid4().hex[:16],
                roomId=room_id,
                boxId=meta.boxId,
                category=meta.category,
                dims=[float(d) for d in meta.dims],
                yaw=meta.yaw,
                triTarget=min(meta.triTarget, own or meta.triTarget),
                requestedTriTarget=meta.triTarget,
                triTargetLight=min(meta.triTargetLight, light) if meta.triTargetLight else light,
                requestedTriTargetLight=meta.triTargetLight or 0,
                profile=meta.profile or "",
                backend=meta.backend,
                source="hosted" if meta.backend == "hosted" else ("cache" if meta.backend == "cache" else "local"),
                seq=self._seq,
                volume=box_volume(meta.dims),
                views=[v.model_dump() for v in meta.views],
                viewCount=len(crops),
                createdUtc=_now(),
            )
            self._seq += 1
            self._jobs[job.jobId] = job

            if meta.boxId in room["placeholders"]:
                job.state = "placeholder"
                job.error = (f"beyond the {MAX_GENERATED_PER_ROOM} largest boxes of the room; "
                             "it receives no mesh and keeps its first shape")
                self._save(job)
                return job, False

            job.cacheKey = self._cache_key(crops[0], job)
            hit = self._cache_lookup(job.cacheKey)
            if hit is not None:
                self._fill_from_asset(job, hit)
                job.state, job.cached, job.source = "ready", True, "cache"
                self._save(job)
                self._announce(job)
                return job, False
            if meta.backend == "cache":
                job.state, job.error = "failed", "not in the cache, and the backend is `cache`"
                self._save(job)
                self._announce(job)
                return job, True

            self._crops[job.jobId] = list(crops)
            if self.settings.debug_keep:
                for i, crop in enumerate(crops):
                    _write_atomic(self._job_dir(job.jobId) / f"view{i}.jpg", crop)
            self._save(job)
            self._announce(job)
            self._wake.notify_all()
            return job, True

    def job(self, job_id: str) -> Job:
        with self._lock:
            if job_id not in self._jobs:
                raise NotFound(job_id)
            return self._jobs[job_id]

    def jobs(self, room_id: Optional[str] = None) -> list[Job]:
        with self._lock:
            found = [j for j in self._jobs.values() if room_id is None or j.roomId == room_id]
            return sorted(found, key=lambda j: j.seq)

    def queued(self) -> list[Job]:
        """Waiting jobs in the order the worker takes them: largest box first."""
        with self._lock:
            waiting = [j for j in self._jobs.values() if j.state == "queued"]
            return sorted(waiting, key=lambda j: (-j.volume, j.seq))

    def active(self) -> Optional[Job]:
        with self._lock:
            return self._jobs.get(self._active) if self._active else None

    def pending_count(self) -> int:
        with self._lock:
            return sum(1 for j in self._jobs.values() if j.state not in TERMINAL_STATES)

    def status(self, job: Job) -> dict:
        """The `JobStatus` body, with the `AssetReady` fields once the job is ready."""
        with self._lock:
            out: dict[str, Any] = {
                "jobId": job.jobId,
                "roomId": job.roomId,
                "boxId": job.boxId,
                "state": job.state,
                "source": job.source,
                "error": job.error,
                "triTarget": job.triTarget,
                "cached": job.cached,
                "generator": job.generator,
            }
            if job.triTargetLight:
                out["triTargetLight"] = job.triTargetLight
            if job.decoder:
                out["decoder"] = job.decoder
            if job.state == "queued":
                out["queuePosition"] = [j.jobId for j in self.queued()].index(job.jobId)
            if job.state == "ready":
                out.update(self._asset_fields(job))
                if job.fit is not None:
                    out.update({"fit": job.fit, "fitYaw": job.fitYaw, "fitUp": job.fitUp})
                if job.attempts:
                    out["attempts"] = job.attempts
                out["report"] = job.report
            return out

    def _asset_fields(self, job: Job) -> dict:
        """The members of `AssetReady`; those of the light copy only when the job has one."""
        fields = {
            "sha256": job.sha256,
            "url": f"{self.settings.public_url}/assets/{job.sha256}.glb",
            "bytes": job.bytes,
            "tris": job.tris,
            "maxEdge": job.maxEdge,
            "bounds": list(job.bounds),
            "palette": job.palette,
        }
        if job.lightSha256:
            fields.update({
                "lightSha256": job.lightSha256,
                "lightUrl": f"{self.settings.public_url}/assets/{job.lightSha256}.glb",
                "lightBytes": job.lightBytes,
                "lightTris": job.lightTris,
            })
        return fields

    def _fill_from_asset(self, job: Job, info: dict) -> None:
        job.sha256, job.bytes, job.tris = info["sha256"], info["bytes"], info["tris"]
        job.maxEdge, job.bounds, job.palette = info["maxEdge"], info["bounds"], info["palette"]
        job.report, job.generator = info.get("report"), info.get("generator", "")
        job.decoder = info.get("decoder", "")

    def asset(self, sha: str) -> Optional[bytes]:
        if not SHA_PATTERN.match(sha):
            return None
        path = self._asset_path(sha)
        return path.read_bytes() if path.is_file() else None

    # ------------------------------------------------------------------ Hub notifications

    def _announce(self, job: Job) -> None:
        """Queue the job's state for the Hub; never blocks and never fails the job."""
        if not self.settings.hub_url:
            return
        base = {"jobId": job.jobId, "roomId": job.roomId, "boxId": job.boxId}
        messages = [{"type": "JobStatus", **base, "state": job.state, "source": job.source, "error": job.error}]
        if job.state == "ready":
            messages.append({"type": "AssetReady", **base, **self._asset_fields(job)})
        for message in messages:
            try:
                self._hub_queue.put_nowait(message)
            except queue.Full:
                log.debug("Hub notification dropped: the queue is full")

    def _notify_loop(self) -> None:
        while True:
            message = self._hub_queue.get()
            if message is None:
                return
            request = urllib.request.Request(
                self.settings.hub_url,
                data=json.dumps(message).encode("utf-8"),
                headers={"Content-Type": "application/json"},
                method="POST",
            )
            try:
                urllib.request.urlopen(request, timeout=1.0).close()
            except Exception as exc:  # the Hub being away must never matter to the Forge
                log.debug("Hub not reached: %s", exc)

    # ------------------------------------------------------------------ the worker

    def generator_status(self) -> tuple[bool, str]:
        """`generatorReady` and `generatorNote` of GET /health; never blocks."""
        ready, note = self.generator.availability()
        if ready and self._matting_error:
            return False, self._matting_error
        return ready, note

    def _warm_up(self) -> None:
        """Load the matting model and the generator's model before the first job.

        The first job would otherwise pay for both inside its own time-outs. A model
        that cannot be loaded is no reason to stop: GET /health reports why, cached
        assets are still served, and each job tries the load again.
        """
        if imaging.matting_method() != "none":
            try:
                imaging.load_matting()
            except Exception as exc:
                log.warning("the matting model could not be loaded: %s", exc)
                # Without a matte the colour is read from the plain crop; only a generator
                # that takes matted pictures alone cannot work.
                if getattr(self.generator, "needs_matting", False):
                    self._matting_error = f"the matting model could not be loaded: {exc}"
        try:
            self.generator.warm_up()
        except GeneratorError as exc:
            log.warning("the %s generator is not ready: %s", self.generator.name, exc)
        except Exception as exc:  # model code raises anything; the worker must reach its queue
            log.exception("the %s generator failed to load: %s", self.generator.name, exc)

    def _work(self) -> None:
        self._warm_up()
        while True:
            with self._wake:
                while not self._stop and (self._paused or not self.queued()):
                    self._wake.wait(timeout=1.0)
                if self._stop:
                    return
                job = self.queued()[0]
                self._active = job.jobId
            try:
                self._process(job)
            except _Cancelled:
                log.info("job %s dropped: its room was deleted", job.jobId)
            except Exception as exc:  # a job must never take the worker down
                log.exception("job %s failed unexpectedly", job.jobId)
                self._fail(job, f"internal error: {exc}")
            finally:
                with self._lock:
                    self._active = None
                    self._cancelled.discard(job.jobId)
                    self._crops.pop(job.jobId, None)
                    self._views.pop(job.jobId, None)

    def _enter(self, job: Job, state: str) -> None:
        with self._lock:
            if job.jobId in self._cancelled or job.jobId not in self._jobs:
                raise _Cancelled()
            job.state = state
            self._save(job)
        self._announce(job)

    def _fail(self, job: Job, error: str) -> None:
        with self._lock:
            if job.jobId not in self._jobs:
                return
            job.state, job.error = "failed", error
            self._save(job)
        log.warning("job %s (room %s, box %d) failed: %s", job.jobId, job.roomId, job.boxId, error)
        self._announce(job)

    def _timed(self, what: str, seconds: float, fn: Callable[[], Any]) -> Any:
        """Run ``fn`` on a helper thread and give up on it after ``seconds``."""
        box: dict[str, Any] = {}

        def run() -> None:
            try:
                box["value"] = fn()
            except BaseException as exc:
                box["error"] = exc

        thread = threading.Thread(target=run, name=f"forge-{what}", daemon=True)
        thread.start()
        thread.join(seconds)
        if thread.is_alive():
            raise StageTimeout(f"{what} timed out after {seconds:g} s")
        if "error" in box:
            raise box["error"]
        return box["value"]

    def _view(self, job: Job, crops: list[bytes], index: int) -> MattedView:
        """View ``index`` of the job, decoded and matted. Kept in memory until the job ends."""
        views = self._views.setdefault(job.jobId, {})
        if index not in views:
            crop = decode_crop(crops[index])
            note = ""
            try:
                cutout, method = self._timed(
                    "matting", self.settings.matting_timeout_s, lambda: imaging.remove_background(crop)
                )
                if method != "none":
                    self._matting_error = ""
            except StageTimeout:
                raise
            except Exception as exc:
                # The colour can still be read from the plain crop; a generator that needs
                # a matted picture is not given this view (`_picture`).
                log.warning("background removal failed on view %d, the crop stays as it is: %s", index, exc)
                cutout, method, note = crop.convert("RGBA"), "none", f"background removal failed: {exc}"
            views[index] = MattedView(crop=crop, matte=cutout.getchannel("A"), method=method, note=note)
        return views[index]

    @staticmethod
    def _picture(generator: Generator, view: MattedView, index: int):
        """What ``generator`` is given of a view: the matted crop, or the picture framed for it."""
        if not getattr(generator, "needs_matting", False):
            return view.cutout
        if view.method == "none":
            # Never an unmatted room photo (mesh design section 5.1).
            raise ImageError(f"view {index}: {view.note or imaging.MATTING_MISSING}; "
                             f"{generator.name} is given matted pictures only")
        try:
            return frame_for_generator(view.crop, view.matte)
        except ImageError as exc:
            raise ImageError(f"view {index}: {exc}") from exc

    def _generate(self, generator: Generator, view: MattedView, index: int, job: Job, seconds: float):
        def run():
            with self._gpu_lock:
                # The model, when the load at start-up did not get it; a generator that cannot run says so here.
                generator.warm_up()
                request = GenerationRequest(image=self._picture(generator, view, index), category=job.category,
                                            dims=job.dims, seed=self.settings.triposg_seed)
                return generator.generate(request)

        return self._timed(f"generation ({generator.name})", seconds, run)

    def _keep_raw(self, job: Job, index: int, raw) -> None:
        """Under FORGE_DEBUG_KEEP=1: the generator's output as it came, before clean-up touches it.

        With it clean-up can be run again without the GPU, and the generator's own frame can be
        read (mesh design sections 5.1 and 5.6).
        """
        packed = io.BytesIO()
        np.savez_compressed(packed, vertices=np.asarray(raw.vertices, dtype=np.float32),
                            faces=np.asarray(raw.faces, dtype=np.int32),
                            decoder=np.array(getattr(raw, "decoder", "")))
        _write_atomic(self._job_dir(job.jobId) / f"raw_view{index}.npz", packed.getvalue())

    def _process(self, job: Job) -> None:
        settings = self.settings
        crops = self._crops.get(job.jobId) or []
        started = time.monotonic()

        hit = self._cache_lookup(job.cacheKey)
        if hit is not None:
            with self._lock:
                self._fill_from_asset(job, hit)
                job.cached, job.source = True, "cache"
            self._enter(job, "ready")
            return

        # matting ---------------------------------------------------------------
        self._enter(job, "matting")
        try:
            first = self._view(job, crops, 0)
        except (ImageError, StageTimeout, IndexError) as exc:
            self._fail(job, f"view 0: {exc}")
            return
        job.matting = first.method
        job.timings["matting"] = round(time.monotonic() - started, 3)

        # generating ------------------------------------------------------------
        self._enter(job, "generating")
        mark = time.monotonic()
        raw, errors, used, used_view = None, [], None, 0
        attempts: list[tuple[Generator, int, float]] = []  # the generator, the view it is given, the time-out
        if job.backend == "hosted":
            attempts.append((self.hosted, 0, settings.hosted_timeout_s))
        else:
            attempts.append((self.generator, 0, settings.generate_timeout_s))
            if len(crops) > 1:
                attempts.append((self.generator, 1, settings.generate_timeout_s))  # one retry with view 1
            if self.generator.source != "hosted" and self.hosted.availability()[0]:
                attempts.append((self.hosted, 0, settings.hosted_timeout_s))
        for generator, index, seconds in attempts:
            try:
                raw = self._generate(generator, self._view(job, crops, index), index, job, seconds)
                used, used_view = generator, index
                break
            except (GeneratorError, StageTimeout, ImageError) as exc:
                errors.append(str(exc))
            except Exception as exc:  # model code raises anything; keep the worker alive
                errors.append(f"{generator.name}: {exc}")
            with self._lock:
                if job.jobId in self._cancelled:
                    raise _Cancelled()
        if raw is None or used is None:
            self._fail(job, "; ".join(errors) or "no generator produced a mesh")
            return
        job.generator, job.source = used.name, used.source
        job.decoder = getattr(raw, "decoder", "")
        job.timings["generating"] = round(time.monotonic() - mark, 3)
        if settings.debug_keep:
            with self._lock:
                if job.jobId in self._jobs:
                    self._keep_raw(job, used_view, raw)

        # cleaning --------------------------------------------------------------
        self._enter(job, "cleaning")
        mark = time.monotonic()
        try:
            colour = dominant_colour(first.cutout)
            result = self._timed(
                "clean-up",
                settings.cleaning_timeout_s,
                lambda: clean(raw.vertices, raw.faces, job.dims, job.triTarget, colour,
                              yaw=job.yaw, max_anisotropy=settings.max_anisotropy),
            )
        except (CleanupError, StageTimeout) as exc:
            self._fail(job, f"clean-up: {exc}")
            return
        verdict = validate_glb(result.glb, tri_target=job.triTarget, box=job.dims, level="full")
        if not verdict.ok:
            self._fail(job, "asset contract: " + "; ".join(f"[{v.code}] {v.message}" for v in verdict.violations))
            return
        job.timings["cleaning"] = round(time.monotonic() - mark, 3)

        report = result.report
        info = {
            "sha256": report.sha256,
            "bytes": report.bytes,
            "tris": report.triangles,
            "maxEdge": round(report.longest_edge, 5),
            "bounds": [round(v, 5) for v in report.size],
            "palette": report.palette_index,
            "report": report.to_dict(),
            "generator": used.name,
            "decoder": job.decoder,
        }
        with self._lock:
            if job.jobId in self._cancelled or job.jobId not in self._jobs:
                raise _Cancelled()
            _write_atomic(self._asset_path(report.sha256), result.glb)
            _write_json(self._asset_path(report.sha256).with_suffix(".json"), info)
            _write_json(self._root / "cache" / f"{job.cacheKey}.json", {"sha256": report.sha256})
            self._fill_from_asset(job, info)
            if not settings.debug_keep:
                self._crops.pop(job.jobId, None)
        self._enter(job, "ready")
        log.info("job %s ready: box %d, %d triangles, %s", job.jobId, job.boxId, job.tris, job.sha256[:12])
