"""The Forge HTTP interface (Master Plan Appendix B.4).

    uvicorn forge.app:app --host 0.0.0.0 --port 8000
"""
from __future__ import annotations

import json
import logging
import re
from contextlib import asynccontextmanager
from typing import Optional

from fastapi import FastAPI, File, Form, HTTPException, Query, Request, Response, UploadFile
from fastapi.responses import JSONResponse
from pydantic import ValidationError

from . import PIPELINE_VER, imaging
from .config import Settings
from .generators import Generator
from .models import Health, JobCreated, JobStatus, ObjectMeta, QueueEntry, QueueView, RoomCreated, RoomModel
from .service import Forge, Job, NotFound

_ASSET_NAME = re.compile(r"^([0-9a-f]{64})\.glb$")
_ROOM_ID = re.compile(r"^[A-Za-z0-9_]{1,40}$")
_JOB_ID = re.compile(r"^[0-9a-f]{16}$")


def _entry(job: Job) -> QueueEntry:
    return QueueEntry(jobId=job.jobId, roomId=job.roomId, boxId=job.boxId, volume=round(job.volume, 6), state=job.state)


def create_app(settings: Optional[Settings] = None, generator: Optional[Generator] = None) -> FastAPI:
    """Build the application. Tests pass their own settings and generator."""
    settings = settings or Settings.from_env()
    forge = Forge(settings, generator)

    @asynccontextmanager
    async def lifespan(_: FastAPI):
        logging.getLogger("forge").setLevel(logging.INFO)
        if settings.debug_keep:
            logging.getLogger("forge").warning("FORGE_DEBUG_KEEP is set: crops are kept on disk")
        forge.start()
        yield
        forge.stop()

    app = FastAPI(title="ATLAS Forge", version=PIPELINE_VER, lifespan=lifespan)
    app.state.forge = forge

    def room_or_404(room_id: str) -> dict:
        if not _ROOM_ID.match(room_id):
            raise HTTPException(404, "unknown room")
        try:
            return forge.room_meta(room_id)
        except NotFound:
            raise HTTPException(404, "unknown room") from None

    @app.get("/health", response_model=Health)
    def health() -> Health:
        ready, note = forge.generator_status()
        name = forge.generator.gpu()
        hosted = forge.hosted.availability()[0]
        return Health(
            ok=True,
            gpu=name is not None,
            gpuName=name,
            queue=forge.pending_count(),
            pipelineVer=PIPELINE_VER,
            debugKeep=settings.debug_keep,
            generator=forge.generator.name,
            generatorReady=ready,
            generatorNote=note,
            hosted=hosted,
            hostedEnabled=hosted,
            matting=imaging.matting_method(),
        )

    @app.post("/rooms", status_code=201, response_model=RoomCreated)
    async def post_room(request: Request) -> RoomCreated:
        raw = await request.body()
        if len(raw) > settings.max_room_bytes:
            raise HTTPException(413, f"RoomModel larger than {settings.max_room_bytes} bytes")
        try:
            room = RoomModel.model_validate_json(raw)
        except ValidationError as exc:
            raise HTTPException(422, json.loads(exc.json(include_url=False, include_input=False))) from None
        if len({o.id for o in room.objects}) != len(room.objects):
            raise HTTPException(422, "object ids are not unique")
        return RoomCreated(**{k: v for k, v in forge.put_room(room, raw).items() if k != "createdUtc"})

    @app.put("/rooms/{room_id}/mesh", status_code=204)
    async def put_room_mesh(room_id: str, request: Request) -> Response:
        room_or_404(room_id)
        data = await request.body()
        if len(data) > settings.max_room_mesh_bytes:
            raise HTTPException(413, f"room mesh larger than {settings.max_room_mesh_bytes} bytes")
        forge.put_room_mesh(room_id, data)
        return Response(status_code=204)

    @app.get("/rooms/{room_id}")
    def get_room(room_id: str) -> Response:
        room_or_404(room_id)
        return Response(forge.room_json(room_id), media_type="application/json")

    @app.delete("/rooms/{room_id}", status_code=204)
    def delete_room(room_id: str) -> Response:
        room_or_404(room_id)
        forge.delete_room(room_id)
        return Response(status_code=204)

    @app.post("/rooms/{room_id}/objects", response_model=JobCreated, response_model_exclude_none=True)
    def post_object(
        room_id: str,
        meta: str = Form(...),
        view0: Optional[UploadFile] = File(None),
        view1: Optional[UploadFile] = File(None),
        view2: Optional[UploadFile] = File(None),
    ):
        room_or_404(room_id)
        try:
            parsed = ObjectMeta.model_validate_json(meta)
        except ValidationError as exc:
            raise HTTPException(422, json.loads(exc.json(include_url=False, include_input=False))) from None
        crops: list[bytes] = []
        for name, upload in (("view0", view0), ("view1", view1), ("view2", view2)):
            if upload is None:
                break
            data = upload.file.read(settings.max_crop_bytes + 1)
            if len(data) > settings.max_crop_bytes:
                raise HTTPException(413, f"{name} larger than {settings.max_crop_bytes} bytes")
            if not data:
                raise HTTPException(422, f"{name} is empty")
            crops.append(data)
        if not crops:
            raise HTTPException(422, "view0 is required")
        try:
            job, created = forge.submit(room_id, parsed, crops)
        except NotFound:
            raise HTTPException(404, f"box {parsed.boxId} is not in room {room_id}") from None
        if job.state == "placeholder":
            return JSONResponse({"jobId": job.jobId, "placeholder": True}, status_code=200)
        if job.cached and job.state == "ready":
            return JSONResponse({"jobId": job.jobId, "cached": True, "sha256": job.sha256}, status_code=200)
        return JSONResponse({"jobId": job.jobId}, status_code=202)

    @app.get("/jobs/{job_id}", response_model=JobStatus, response_model_exclude_none=True)
    def get_job(job_id: str):
        if not _JOB_ID.match(job_id):
            raise HTTPException(404, "unknown job")
        try:
            return forge.status(forge.job(job_id))
        except NotFound:
            raise HTTPException(404, "unknown job") from None

    @app.get("/jobs", response_model=list[JobStatus], response_model_exclude_none=True)
    def list_jobs(room: Optional[str] = Query(default=None)):
        if room is not None:
            room_or_404(room)
        return [forge.status(job) for job in forge.jobs(room)]

    @app.get("/queue", response_model=QueueView)
    def get_queue() -> QueueView:
        active = forge.active()
        return QueueView(
            active=_entry(active) if active else None,
            queued=[_entry(j) for j in forge.queued()],
            placeholders=[_entry(j) for j in forge.jobs() if j.state == "placeholder"],
            generator=forge.generator.name,
        )

    @app.get("/assets/{name}")
    def get_asset(name: str, request: Request) -> Response:
        match = _ASSET_NAME.match(name)
        data = forge.asset(match.group(1)) if match else None
        if data is None:
            raise HTTPException(404, "unknown asset")
        etag = f'"{match.group(1)}"'
        headers = {"ETag": etag, "Cache-Control": "public, max-age=31536000, immutable"}
        if request.headers.get("if-none-match") == etag:
            return Response(status_code=304, headers=headers)
        return Response(data, media_type="model/gltf-binary", headers=headers)

    return app


def __getattr__(name: str):
    # `uvicorn forge.app:app` reads this attribute. Building the application on first use,
    # instead of at import, keeps importing this module free of side effects (no data folder
    # is created and nothing is read from the environment until the service really starts).
    if name == "app":
        instance = create_app()
        globals()["app"] = instance
        return instance
    raise AttributeError(f"module {__name__!r} has no attribute {name!r}")
