"""Image-to-3D backends. The choice is made once, from the settings."""
from __future__ import annotations

from ..config import Settings
from .base import GenerationRequest, Generator, GeneratorError, GeneratorUnavailable, RawMesh
from .hosted import HostedGenerator
from .procedural import ProceduralGenerator
from .triposg import TripoSGGenerator

__all__ = [
    "GenerationRequest",
    "Generator",
    "GeneratorError",
    "GeneratorUnavailable",
    "HostedGenerator",
    "ProceduralGenerator",
    "RawMesh",
    "TripoSGGenerator",
    "make_generator",
]


def make_generator(settings: Settings) -> Generator:
    """The generator the worker owns (`FORGE_GENERATOR`)."""
    if settings.generator == "procedural":
        return ProceduralGenerator()
    if settings.generator == "hosted":
        return HostedGenerator(settings)
    return TripoSGGenerator(settings)
