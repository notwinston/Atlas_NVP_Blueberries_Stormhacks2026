"""Check what TripoSG needs on the Station, one line per check (README, "First run on the Station").

    python tools/check_station.py

Run it in the Forge's environment after the install and before the service is started: it loads
PyTorch and uses the graphics card, and the service wants the card to itself. Every check runs
whatever the one before it said, so one run shows everything that is missing. It ends with the
versions to write into docs/versions.md.

It does not load TripoSG's weights and does not generate anything: the service does that.

Written without a Station to run it on: on the machine it was written on every check after the
first fails, which is all that could be tried.
"""
from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path

FORGE_DIR = Path(__file__).resolve().parent.parent
if str(FORGE_DIR) not in sys.path:
    sys.path.insert(0, str(FORGE_DIR))

FAILED: list[str] = []
VERSIONS: list[tuple[str, str]] = []


class Note(Exception):
    """Not a fault, but something the person at the Station should know before starting the service."""


def check(name: str, fn) -> None:
    """Run one check: ``fn`` returns a sentence, or raises with the reason."""
    try:
        detail = fn()
        print(f"PASS  {name}: {detail}")
    except Note as note:
        print(f"NOTE  {name}: {note}")
    except Exception as exc:  # every check must be reached
        FAILED.append(name)
        print(f"FAIL  {name}: {type(exc).__name__}: {exc}")


def version_of(package: str) -> str:
    from importlib.metadata import version

    return version(package)


def python_version() -> str:
    found = "%d.%d.%d" % sys.version_info[:3]
    VERSIONS.append(("Python", found))
    if sys.version_info[:2] != (3, 11):
        raise RuntimeError(f"{found}; the Forge's lists are pinned for 3.11")
    return found


def torch_and_cuda() -> str:
    import torch

    VERSIONS.append(("PyTorch", torch.__version__))
    if not torch.cuda.is_available():
        raise RuntimeError(f"torch {torch.__version__} sees no CUDA device (a CPU build, or the driver is too old)")
    name = torch.cuda.get_device_name(0)
    total = torch.cuda.get_device_properties(0).total_memory / 2**30
    VERSIONS.append(("CUDA (PyTorch build)", str(torch.version.cuda)))
    VERSIONS.append(("GPU", f"{name}, {total:.1f} GB"))
    # A build that does not know the card's architecture imports and then fails on first use.
    value = float((torch.ones(8, device="cuda") * 2).sum())
    if value != 16.0:
        raise RuntimeError(f"a sum on the card gave {value}")
    return f"torch {torch.__version__}, CUDA {torch.version.cuda}, {name}, {total:.1f} GB"


def packages() -> str:
    import numpy

    wanted = {"transformers": "4.49.0"}
    found = {}
    for package in ("numpy", "transformers", "diffusers", "huggingface-hub", "scikit-image", "scipy", "peft"):
        found[package] = version_of(package)
        VERSIONS.append((package, found[package]))
    for package, pin in wanted.items():
        if found[package] != pin:
            raise RuntimeError(f"{package} is {found[package]}; requirements-triposg.txt pins {pin} "
                               "(4.57.0 cannot load the model)")
    if int(numpy.__version__.split(".")[0]) < 2:
        raise RuntimeError(f"numpy is {numpy.__version__}; requirements.lock pins a 2.x")
    return ", ".join(f"{k} {v}" for k, v in found.items())


def diso_decoder() -> str:
    """TripoSG's default decoder, called the way `triposg/inference_utils.py` calls it, on a sphere."""
    import torch
    from diso import DiffDMC

    VERSIONS.append(("diso", version_of("diso")))
    n = 64
    axis = torch.linspace(-1.0, 1.0, n, device="cuda")
    x, y, z = torch.meshgrid(axis, axis, axis, indexing="ij")
    sdf = (torch.sqrt(x * x + y * y + z * z) - 0.5).to(torch.float32).contiguous()
    dmc = DiffDMC(dtype=torch.float32).to("cuda")
    vertices, faces = dmc(sdf, deform=None, return_quads=False, normalize=False)
    if len(vertices) < 100 or len(faces) < 100:
        raise RuntimeError(f"a sphere gave {len(vertices)} vertices and {len(faces)} faces: the CUDA part does not work "
                           "(was CUDA_HOME set when diso was built?)")
    return f"diso {version_of('diso')}: a sphere gave {len(vertices)} vertices and {len(faces)} faces on the card"


def matting() -> str:
    from PIL import Image

    import rembg

    VERSIONS.append(("rembg", version_of("rembg")))
    try:
        import onnxruntime

        VERSIONS.append(("onnxruntime", f"{onnxruntime.__version__}, providers {onnxruntime.get_available_providers()}"))
    except ImportError:
        pass
    session = rembg.new_session()  # downloads the matting model on its first use
    picture = Image.new("RGB", (256, 256), (96, 96, 96))
    picture.paste((150, 112, 70), (60, 60, 196, 196))
    cut = rembg.remove(picture, session=session)
    if cut.mode != "RGBA" or cut.size != picture.size:
        raise RuntimeError(f"rembg returned a {cut.mode} picture of {cut.size}")
    return f"rembg {version_of('rembg')} cut a test picture"


def checkout() -> str:
    repo = os.environ.get("FORGE_TRIPOSG_REPO", "")
    if not repo:
        raise RuntimeError("FORGE_TRIPOSG_REPO is not set")
    if not (Path(repo) / "triposg").is_dir():
        raise RuntimeError(f"{repo} holds no `triposg` folder")
    commit = "unknown"
    try:
        commit = subprocess.run(["git", "-C", repo, "rev-parse", "HEAD"], capture_output=True, text=True,
                                timeout=20).stdout.strip() or commit
    except (OSError, subprocess.SubprocessError):
        pass
    VERSIONS.append(("TripoSG commit", commit))
    sys.path.insert(0, str(Path(repo).resolve()))
    from triposg.pipelines.pipeline_triposg import TripoSGPipeline  # noqa: F401  (this imports diffusers and diso)

    return f"{repo} at {commit[:12]}; the pipeline imports"


def weights() -> str:
    from forge.config import Settings
    from forge.generators import TripoSGGenerator

    folder = TripoSGGenerator(Settings.from_env()).weights_dir()
    if not (folder / "model_index.json").is_file():
        raise Note(f"no model_index.json in {folder}: the service downloads the weights (7.95 GB) when it "
                   "starts, and is not ready until they are there")
    size = sum(p.stat().st_size for p in folder.rglob("*") if p.is_file()) / 2**30
    VERSIONS.append(("TripoSG weights", f"{folder}, {size:.2f} GB"))
    if size < 7.0:
        raise RuntimeError(f"{folder} holds {size:.2f} GB; the whole model is 7.95 GB: the download is not complete")
    return f"{folder}, {size:.2f} GB"


def the_forge() -> str:
    from forge.config import Settings
    from forge.generators import TripoSGGenerator

    settings = Settings.from_env()
    ready, note = TripoSGGenerator(settings).availability()
    VERSIONS.append(("FORGE_TRIPOSG_UP / _FRONT", f"{settings.triposg_up} / {settings.triposg_front}"))
    # The adapter is ready only once the service has loaded the model; here it must have nothing to miss.
    if not ready and not note.startswith("the model is not loaded yet"):
        raise RuntimeError(note)
    return "the adapter finds everything it needs; the model is loaded when the service starts"


def main() -> int:
    check("Python", python_version)
    check("PyTorch and the graphics card", torch_and_cuda)
    check("the pinned packages", packages)
    check("diso, TripoSG's decoder", diso_decoder)
    check("rembg, the matting model", matting)
    check("the TripoSG checkout", checkout)
    check("the weights", weights)
    check("the Forge's adapter", the_forge)

    print("\nFor docs/versions.md:")
    for name, value in VERSIONS:
        print(f"  {name}: {value}")
    if FAILED:
        print(f"\n{len(FAILED)} of 8 checks failed: " + "; ".join(FAILED))
        return 1
    print("\nNo check failed. Start the service and send it an object (tools/first_run.py).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
