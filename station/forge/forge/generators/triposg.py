"""TripoSG on the Station's GPU (Master Plan section 7.4, state `generating`).

UNVERIFIED ON THE BUILD MACHINE. This adapter was written from the public
TripoSG repository (github.com/VAST-AI-Research/TripoSG, MIT) and its
`scripts/inference_triposg.py`; it has never been run, because the machine it
was written on has no GPU and no PyTorch. Check it on the RTX 5070 with the
cu128 PyTorch wheels before relying on it (README, "What is unverified").

Nothing here is imported until the first job, so the service starts, answers
GET /health and serves cached assets on a machine without torch.

What it expects on the Station:
  FORGE_TRIPOSG_REPO     the cloned TripoSG repository (holds `triposg/` and `scripts/`)
  FORGE_TRIPOSG_WEIGHTS  the VAST-AI/TripoSG weights folder (downloaded on first use if empty)
"""
from __future__ import annotations

import importlib.util
import logging
import sys
from pathlib import Path
from typing import Optional

import numpy as np
from PIL import Image

from ..config import Settings
from .base import GenerationRequest, GeneratorError, GeneratorUnavailable, RawMesh

log = logging.getLogger("forge.triposg")

_HF_REPO = "VAST-AI/TripoSG"


class TripoSGGenerator:
    name = "triposg"
    source = "local"

    def __init__(self, settings: Settings) -> None:
        self._settings = settings
        self._pipe = None
        self._torch = None

    # -- status, cheap and import-free where possible ---------------------------------

    def availability(self) -> tuple[bool, str]:
        if importlib.util.find_spec("torch") is None:
            return False, "PyTorch is not installed"
        repo = self._settings.triposg_repo
        if not repo or not (Path(repo) / "triposg").is_dir():
            return False, "FORGE_TRIPOSG_REPO does not point at a TripoSG checkout"
        import torch

        if not torch.cuda.is_available():
            return False, "CUDA is not available to PyTorch"
        return True, ""

    def gpu(self) -> Optional[str]:
        if importlib.util.find_spec("torch") is None:
            return None
        import torch

        return torch.cuda.get_device_name(0) if torch.cuda.is_available() else None

    # -- the model ---------------------------------------------------------------------

    def _load(self):
        if self._pipe is not None:
            return self._pipe
        ok, reason = self.availability()
        if not ok:
            raise GeneratorUnavailable(f"TripoSG cannot run: {reason}")
        import torch

        repo = str(Path(self._settings.triposg_repo).resolve())
        if repo not in sys.path:
            sys.path.insert(0, repo)
        try:
            from triposg.pipelines.pipeline_triposg import TripoSGPipeline
        except ImportError as exc:
            raise GeneratorUnavailable(f"the TripoSG package cannot be imported from {repo}: {exc}") from exc

        weights = self._settings.triposg_weights or str(Path(repo) / "pretrained_weights" / "TripoSG")
        if not Path(weights).is_dir():
            from huggingface_hub import snapshot_download

            log.info("downloading the TripoSG weights to %s", weights)
            snapshot_download(repo_id=_HF_REPO, local_dir=weights)

        dtype = torch.float16 if self._settings.triposg_half_precision else torch.float32
        self._pipe = TripoSGPipeline.from_pretrained(weights).to("cuda", dtype)
        self._torch = torch
        log.info("TripoSG loaded on %s", torch.cuda.get_device_name(0))
        return self._pipe

    def generate(self, request: GenerationRequest) -> RawMesh:
        pipe = self._load()
        torch = self._torch

        # TripoSG is trained on an object over a plain background: flatten the matte onto white.
        rgba = request.image.convert("RGBA")
        canvas = Image.new("RGBA", rgba.size, (255, 255, 255, 255))
        image = Image.alpha_composite(canvas, rgba).convert("RGB")

        try:
            with torch.no_grad():
                outputs = pipe(
                    image=image,
                    generator=torch.Generator(device=pipe.device).manual_seed(int(request.seed)),
                    num_inference_steps=self._settings.triposg_steps,
                    guidance_scale=self._settings.triposg_guidance,
                ).samples[0]
        except Exception as exc:  # CUDA and diffusers raise many types; the job must fail cleanly
            raise GeneratorError(f"TripoSG inference failed: {exc}") from exc
        finally:
            torch.cuda.empty_cache()

        vertices = np.asarray(outputs[0], dtype=np.float64)
        faces = np.ascontiguousarray(outputs[1]).astype(np.int64)
        if vertices.ndim != 2 or vertices.shape[1] != 3 or faces.ndim != 2 or faces.shape[1] != 3 or not len(faces):
            raise GeneratorError("TripoSG returned no mesh")
        return RawMesh(vertices=vertices, faces=faces)
