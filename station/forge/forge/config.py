"""Forge settings. Everything is read from the environment once, at start-up."""
from __future__ import annotations

import os
from dataclasses import dataclass, field
from pathlib import Path

_PACKAGE_ROOT = Path(__file__).resolve().parent.parent

GENERATORS = ("triposg", "procedural", "hosted")
# The axis of TripoSG's own frame that points up, and where the front of its meshes is:
# "canonical" (on the mesh's own +Z), "view" (towards the camera of the photo) or "unknown".
# Both are measured on the Station (README, "First run on the Station").
TRIPOSG_UP = ("Y", "Z", "X")
TRIPOSG_FRONT = ("canonical", "view", "unknown")


def _flag(name: str, default: bool = False) -> bool:
    value = os.environ.get(name)
    if value is None:
        return default
    return value.strip().lower() in ("1", "true", "yes", "on")


def _number(name: str, default: float) -> float:
    value = os.environ.get(name)
    return float(value) if value not in (None, "") else default


def _choice(name: str, default: str, allowed: tuple[str, ...]) -> str:
    """One of ``allowed``, compared without regard to case; the spelling of ``allowed`` is returned."""
    value = os.environ.get(name)
    if value in (None, ""):
        return default
    for option in allowed:
        if option.lower() == value.strip().lower():
            return option
    raise ValueError(f"{name} must be one of {allowed}, not {value!r}")


@dataclass
class Settings:
    """Ports, paths, the generator choice and the stage time-outs (section 7.4)."""

    host: str = "0.0.0.0"
    port: int = 8000
    data_dir: Path = field(default_factory=lambda: _PACKAGE_ROOT / "data")

    # Which generator the one worker owns: "triposg" (the Station's RTX 5070),
    # "procedural" (no GPU; tests and dry runs) or "hosted" (third party only).
    generator: str = "triposg"
    # The hosted fallback sends a crop to a third party, so it is off unless the
    # operator switches it on and the walker has been told (section 7.4, Privacy).
    hosted_enabled: bool = False
    hosted_url: str = ""
    hosted_key: str = ""

    # FORGE_DEBUG_KEEP=1 keeps the crops on disk; reported by GET /health.
    debug_keep: bool = False

    # Job states are posted to the Hub on the loopback interface (Appendix B.3).
    # An empty value switches the notifications off.
    hub_url: str = "http://127.0.0.1:8080/internal/job"
    # Prefix of the asset URL announced in AssetReady, e.g. "http://192.168.8.2:8000".
    public_url: str = ""

    matting_timeout_s: float = 10.0
    generate_timeout_s: float = 90.0
    hosted_timeout_s: float = 120.0
    # Clean-up casts rays and makes two copies of every mesh (mesh design section 5.3).
    cleaning_timeout_s: float = 60.0

    # Non-uniform stretch allowed when fitting a mesh to its box (section 7.4: +-15 %).
    max_anisotropy: float = 0.15
    # A fit to the scanned profile above this is poor: the other photos are then tried
    # (mesh design section 5.3, decision M7). In fractions of the box height.
    poor_fit: float = 0.20

    max_room_bytes: int = 2 * 1024 * 1024
    max_room_mesh_bytes: int = 2 * 1024 * 1024
    max_crop_bytes: int = 1024 * 1024

    # TripoSG (unverified on the build machine; see README).
    triposg_repo: str = ""
    triposg_weights: str = ""
    rmbg_weights: str = ""
    triposg_steps: int = 50
    triposg_guidance: float = 7.0
    triposg_seed: int = 42
    triposg_half_precision: bool = True
    # When the default decoder fails, generate once more with `use_flash_decoder=False`.
    triposg_fallback_decoder: bool = True
    # TripoSG's output frame; "Y" and "unknown" until it has been measured on the Station.
    triposg_up: str = "Y"
    triposg_front: str = "unknown"
    # Set when TripoSG's faces turn out to be wound clockwise seen from outside: the adapter then
    # reverses them. Clean-up, the colours and the baked shade all take the winding on trust.
    triposg_inside_out: bool = False

    @classmethod
    def from_env(cls) -> "Settings":
        s = cls()
        s.host = os.environ.get("FORGE_HOST", s.host)
        s.port = int(_number("FORGE_PORT", s.port))
        s.data_dir = Path(os.environ.get("FORGE_DATA_DIR", str(s.data_dir))).expanduser()
        s.generator = os.environ.get("FORGE_GENERATOR", s.generator).strip().lower()
        if s.generator not in GENERATORS:
            raise ValueError(f"FORGE_GENERATOR must be one of {GENERATORS}, not {s.generator!r}")
        s.hosted_enabled = _flag("FORGE_HOSTED_ENABLED")
        s.hosted_url = os.environ.get("FORGE_HOSTED_URL", "")
        s.hosted_key = os.environ.get("FORGE_HOSTED_KEY", "")
        s.debug_keep = _flag("FORGE_DEBUG_KEEP")
        s.hub_url = os.environ.get("FORGE_HUB_URL", s.hub_url)
        s.public_url = os.environ.get("FORGE_PUBLIC_URL", "").rstrip("/")
        s.matting_timeout_s = _number("FORGE_MATTING_TIMEOUT_S", s.matting_timeout_s)
        s.generate_timeout_s = _number("FORGE_GENERATE_TIMEOUT_S", s.generate_timeout_s)
        s.hosted_timeout_s = _number("FORGE_HOSTED_TIMEOUT_S", s.hosted_timeout_s)
        s.cleaning_timeout_s = _number("FORGE_CLEANING_TIMEOUT_S", s.cleaning_timeout_s)
        s.max_anisotropy = _number("FORGE_MAX_ANISOTROPY", s.max_anisotropy)
        s.poor_fit = _number("FORGE_POOR_FIT", s.poor_fit)
        s.triposg_repo = os.environ.get("FORGE_TRIPOSG_REPO", "")
        s.triposg_weights = os.environ.get("FORGE_TRIPOSG_WEIGHTS", "")
        s.rmbg_weights = os.environ.get("FORGE_RMBG_WEIGHTS", "")
        s.triposg_steps = int(_number("FORGE_TRIPOSG_STEPS", s.triposg_steps))
        s.triposg_guidance = _number("FORGE_TRIPOSG_GUIDANCE", s.triposg_guidance)
        s.triposg_seed = int(_number("FORGE_TRIPOSG_SEED", s.triposg_seed))
        s.triposg_half_precision = _flag("FORGE_TRIPOSG_FP16", True)
        s.triposg_fallback_decoder = _flag("FORGE_TRIPOSG_FALLBACK_DECODER", True)
        s.triposg_up = _choice("FORGE_TRIPOSG_UP", s.triposg_up, TRIPOSG_UP)
        s.triposg_front = _choice("FORGE_TRIPOSG_FRONT", s.triposg_front, TRIPOSG_FRONT)
        s.triposg_inside_out = _flag("FORGE_TRIPOSG_INSIDE_OUT")
        return s
