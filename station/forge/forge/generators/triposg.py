"""TripoSG on the Station's GPU (Master Plan section 7.4, state `generating`; mesh design section 5.1).

NEVER RUN. This adapter was written from the public TripoSG repository
(github.com/VAST-AI-Research/TripoSG, MIT; commit fc5c409 of 18 April 2025),
its `scripts/inference_triposg.py` and `triposg/pipelines/pipeline_triposg.py`.
The machines it was written on have no GPU and no PyTorch, so its tests drive
it with a fake pipeline. Check it on the RTX 5070 before relying on it
(README, "First run on the Station").

Nothing here imports PyTorch until the model is loaded, and the model is loaded
by the worker when the service starts, so GET /health answers at once on any
machine and the first object does not pay for the load.

What it expects on the Station:
  FORGE_TRIPOSG_REPO     the cloned TripoSG repository (holds `triposg/` and `scripts/`)
  FORGE_TRIPOSG_WEIGHTS  the VAST-AI/TripoSG weights folder; `<repo>/pretrained_weights/TripoSG` when empty.
                         Downloaded when the service starts if it does not hold `model_index.json`
  diso                   TripoSG's decoder, built from source against the installed PyTorch
  rembg                  the matting model: TripoSG is never given a picture that is not matted
"""
from __future__ import annotations

import importlib.util
import logging
import sys
import threading
from pathlib import Path
from typing import Any, Optional

import numpy as np
from PIL import Image

from .. import imaging
from ..config import Settings
from .base import GenerationRequest, GeneratorError, GeneratorUnavailable, RawMesh

log = logging.getLogger("forge.triposg")

_HF_REPO = "VAST-AI/TripoSG"
# A weights folder is complete when it holds this file: a download that stopped half-way
# leaves the folder and not the file, and loading from it fails.
_WEIGHTS_MARKER = "model_index.json"

# TripoSG's two ways from latents to a mesh. "flash" is its default and needs diso; one open
# report has it fail on Windows with a CUDA error, after which the pipeline crashes on the empty
# result (TripoSG issue 63). "hierarchical" is the older marching-cubes decoder
# (`use_flash_decoder=False`).
DECODER_FLASH = "flash"
DECODER_HIERARCHICAL = "hierarchical"
# After the default decoder has failed on this many objects in a row while the other one
# produced their meshes, the other one is tried first: a decoder that is broken on this
# machine would otherwise cost every object a whole generation.
_SWITCH_AFTER_FAILURES = 2


class _NotReady(Exception):
    """Why the model cannot be loaded; a sentence for GET /health and for the job."""


class _Runtime:
    """What the adapter takes from outside the Forge: PyTorch, diso, the TripoSG checkout, the Hugging Face hub.

    Each method imports on first use. The tests pass their own, so they need none of the four.
    """

    def installed(self, module: str) -> bool:
        return importlib.util.find_spec(module) is not None

    def torch(self) -> Any:
        import torch

        return torch

    def check_diso(self) -> None:
        # diso is a compiled extension: finding it is not enough, it has to load.
        import diso  # noqa: F401

    def pipeline_class(self, repo: str) -> Any:
        if repo not in sys.path:
            sys.path.insert(0, repo)
        from triposg.pipelines.pipeline_triposg import TripoSGPipeline

        return TripoSGPipeline

    def download(self, weights: str) -> None:
        from huggingface_hub import snapshot_download

        snapshot_download(repo_id=_HF_REPO, local_dir=weights)


def _on_white(image: Image.Image) -> Image.Image:
    """RGB for the pipeline. The service sends the framed picture, which is RGB on white already."""
    if image.mode == "RGB":
        return image
    rgba = image.convert("RGBA")
    canvas = Image.new("RGBA", rgba.size, (255, 255, 255, 255))
    return Image.alpha_composite(canvas, rgba).convert("RGB")


class TripoSGGenerator:
    name = "triposg"
    source = "local"
    # TripoSG was trained on a matted object that fills 90 % of the picture.
    needs_matting = True

    def __init__(self, settings: Settings, runtime: Optional[_Runtime] = None) -> None:
        self._settings = settings
        self._runtime = runtime or _Runtime()
        self._pipe = None
        self._torch = None
        self._gpu_name: Optional[str] = None
        self._load_lock = threading.Lock()
        self._loading = ""  # what the load is doing right now; empty when none is running
        self._load_error = ""  # why the last load failed
        self._flash_failures = 0

    # -- status: never imports, never blocks -------------------------------------------

    def weights_dir(self) -> Path:
        if self._settings.triposg_weights:
            return Path(self._settings.triposg_weights)
        return Path(self._settings.triposg_repo or ".") / "pretrained_weights" / "TripoSG"

    def _weights_complete(self) -> bool:
        return (self.weights_dir() / _WEIGHTS_MARKER).is_file()

    def _missing(self) -> list[str]:
        """Everything that is not installed, found without importing any of it."""
        missing = []
        if not self._runtime.installed("torch"):
            missing.append("PyTorch is not installed")
        repo = self._settings.triposg_repo
        if not repo or not (Path(repo) / "triposg").is_dir():
            missing.append("FORGE_TRIPOSG_REPO does not point at a TripoSG checkout")
        if not self._runtime.installed("diso"):
            missing.append("diso is not installed (TripoSG's decoder; it is built from source, see the README)")
        if imaging.matting_method() == "none":
            missing.append(imaging.MATTING_MISSING)
        return missing

    def availability(self) -> tuple[bool, str]:
        """Usable only once the model is loaded; until then the reason says what is missing or going on."""
        if self._pipe is not None:
            return True, ""
        missing = self._missing()
        if missing:
            return False, "; ".join(missing)
        if self._loading:
            return False, self._loading
        if self._load_error:
            return False, self._load_error
        if not self._weights_complete():
            return False, (f"the model is not loaded yet, and the weights are not complete (no {_WEIGHTS_MARKER} "
                           f"in {self.weights_dir()}): they are downloaded first")
        return False, "the model is not loaded yet"

    def gpu(self) -> Optional[str]:
        """The CUDA device's name, known once the load has imported PyTorch."""
        return self._gpu_name

    # -- the model ---------------------------------------------------------------------

    def warm_up(self) -> None:
        self._load()

    def _load(self):
        if self._pipe is not None:
            return self._pipe
        with self._load_lock:
            if self._pipe is not None:
                return self._pipe
            try:
                self._pipe = self._load_locked()
                self._load_error = ""
            except _NotReady as exc:
                self._load_error = str(exc)
                raise GeneratorUnavailable(f"TripoSG cannot run: {exc}") from exc.__cause__
            finally:
                self._loading = ""
            return self._pipe

    def _load_locked(self):
        runtime, settings = self._runtime, self._settings
        torch = None
        missing = self._missing()
        if runtime.installed("torch"):
            self._loading = "PyTorch is being imported"
            try:
                torch = runtime.torch()
                cuda = bool(torch.cuda.is_available())
                self._gpu_name = torch.cuda.get_device_name(0) if cuda else None
            except Exception as exc:  # a broken install raises anything
                raise _NotReady(f"PyTorch cannot be imported: {exc}") from exc
            if not cuda:
                missing.append("CUDA is not available to PyTorch")
        if missing:
            raise _NotReady("; ".join(missing))

        self._loading = "diso is being imported"
        try:
            runtime.check_diso()
        except Exception as exc:
            raise _NotReady(f"diso cannot be imported: {exc}") from exc
        repo = str(Path(settings.triposg_repo).resolve())
        try:
            pipeline_class = runtime.pipeline_class(repo)
        except Exception as exc:
            raise _NotReady(f"the TripoSG package cannot be imported from {repo}: {exc}") from exc

        weights = self.weights_dir()
        if not self._weights_complete():
            # No folder, or a folder a stopped download left behind: fetch what is missing.
            self._loading = f"the weights are being downloaded to {weights}"
            log.info("downloading the TripoSG weights to %s", weights)
            try:
                runtime.download(str(weights))
            except Exception as exc:
                raise _NotReady(f"the weights could not be downloaded to {weights}: {exc}") from exc
            if not self._weights_complete():
                raise _NotReady(f"the download left no {_WEIGHTS_MARKER} in {weights}")

        self._loading = "the model is being loaded"
        dtype = torch.float16 if settings.triposg_half_precision else torch.float32
        try:
            pipe = pipeline_class.from_pretrained(str(weights)).to("cuda", dtype)
        except Exception as exc:
            raise _NotReady(f"the model could not be loaded from {weights}: {exc}") from exc
        self._torch = torch
        log.info("TripoSG loaded on %s", self._gpu_name)
        return pipe

    # -- one object --------------------------------------------------------------------

    def _decoders(self) -> list[str]:
        if not self._settings.triposg_fallback_decoder:
            return [DECODER_FLASH]
        if self._flash_failures >= _SWITCH_AFTER_FAILURES:
            return [DECODER_HIERARCHICAL, DECODER_FLASH]
        return [DECODER_FLASH, DECODER_HIERARCHICAL]

    def _run(self, pipe, torch, image: Image.Image, seed: int, decoder: str) -> tuple[np.ndarray, np.ndarray]:
        # The default call is upstream's own, word for word; only the fallback names the decoder.
        extra = {} if decoder == DECODER_FLASH else {"use_flash_decoder": False}
        with torch.no_grad():
            sample = pipe(
                image=image,
                generator=torch.Generator(device=pipe.device).manual_seed(int(seed)),
                num_inference_steps=self._settings.triposg_steps,
                guidance_scale=self._settings.triposg_guidance,
                **extra,
            ).samples[0]
        if sample is None or sample[0] is None or sample[1] is None:
            raise GeneratorError("it returned no mesh")
        vertices = np.asarray(sample[0], dtype=np.float64)
        faces = np.ascontiguousarray(sample[1]).astype(np.int64)
        if vertices.ndim != 2 or vertices.shape[1] != 3 or faces.ndim != 2 or faces.shape[1] != 3 or not len(faces):
            raise GeneratorError("it returned no mesh")
        return vertices, faces

    def generate(self, request: GenerationRequest) -> RawMesh:
        pipe = self._load()
        torch = self._torch
        image = _on_white(request.image)

        errors: list[str] = []
        try:
            for decoder in self._decoders():
                try:
                    vertices, faces = self._run(pipe, torch, image, request.seed, decoder)
                except Exception as exc:  # CUDA, diffusers and diso raise many types; the job must fail cleanly
                    errors.append(f"{decoder} decoder: {exc}")
                    log.warning("TripoSG's %s decoder failed: %s", decoder, exc)
                    continue
                if decoder == DECODER_FLASH:
                    self._flash_failures = 0
                elif errors:
                    self._flash_failures += 1
                    if self._flash_failures == _SWITCH_AFTER_FAILURES:
                        log.warning("TripoSG's default decoder failed on %d objects in a row; the %s decoder "
                                    "is tried first from now on", _SWITCH_AFTER_FAILURES, DECODER_HIERARCHICAL)
                return RawMesh(vertices=vertices, faces=faces, decoder=decoder)
        finally:
            torch.cuda.empty_cache()
        raise GeneratorError("TripoSG inference failed: " + "; ".join(errors))
