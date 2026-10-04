"""The generator interface: one image in, one raw triangle mesh out."""
from __future__ import annotations

from dataclasses import dataclass
from typing import Optional, Protocol, Sequence

import numpy as np
from PIL import Image


class GeneratorError(RuntimeError):
    """Generation failed for this input."""


class GeneratorUnavailable(GeneratorError):
    """The generator cannot run on this machine or is switched off."""


@dataclass
class GenerationRequest:
    """One object to generate.

    ``image`` is the matted crop (RGBA, alpha is the object). A generator that
    sets ``needs_matting`` receives instead the picture framed for it: the
    object cut to its matte, on white, padded to a square (RGB;
    `imaging.frame_for_generator`).
    """

    image: Image.Image
    category: str
    dims: Sequence[float]  # scanned box [w, h, d], metres
    seed: int = 42


@dataclass
class RawMesh:
    """Generator output in the generator's own frame and scale; clean-up fits it to the box."""

    vertices: np.ndarray  # (N, 3)
    faces: np.ndarray  # (M, 3)
    decoder: str = ""  # which of the generator's decoders produced it, when it has more than one


class Generator(Protocol):
    """An image-to-3D backend. One worker thread owns it and calls it one job at a time."""

    #: Short name reported by GET /health and stored with each job.
    name: str
    #: What a job that used it reports as its source: "local" or "hosted".
    source: str
    #: True when it must never be given a picture that is not matted (mesh design section 5.1).
    needs_matting: bool

    def availability(self) -> tuple[bool, str]:
        """``(usable, reason)`` without loading any model. Called by GET /health: it must not block."""
        ...

    def warm_up(self) -> None:
        """Load whatever `generate` needs. Called when the service starts and again before each
        generation; does nothing once loaded, and raises GeneratorUnavailable when it cannot."""
        ...

    def gpu(self) -> Optional[str]:
        """Name of the GPU this generator runs on, or None."""
        ...

    def generate(self, request: GenerationRequest) -> RawMesh:
        ...
