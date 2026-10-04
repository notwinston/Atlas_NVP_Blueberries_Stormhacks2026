"""The ``atlas`` console script: ``atlas case <step> [options]`` (Master Plan 10.2.4);
also ``atlas pilot export|check`` (P2-D13, see pilot.py)
and ``atlas network build|bundle`` (NET-01, see network.py).

Steps are discovered, not registered: every module ``atlas_pipelines/steps/<name>.py`` whose file
name does not start with an underscore is one step. To add a step, add a file; never edit this one.

Step module interface (see steps/README.md for an example)::

    NAME = "tube4d"                       # required; the word typed after `atlas case`
    HELP = "one line for `atlas case -h`" # optional
    DEFAULT_CASE = "lung101"              # optional; default of --case (cli default: "lung101")

    def add_arguments(parser):            # required; add the step's own options (may be empty)
        parser.add_argument("--tris", type=int, default=600)

    def run(args) -> int:                 # required; return 0 on success
        ...

Options every step receives (added here, do not redefine them)::

    --case <id>     args.case   str            case id ("lung101" or "twin")
    --out <dir>     args.out    pathlib.Path   default station/pipelines/out   (packages in <out>/cases/<id>/)
    --cache <dir>   args.cache  pathlib.Path   default station/pipelines/.cache
    --force         args.force  bool           ignore cache stamps and rebuild

A step module that fails to import does not break the others: it is listed as broken and
running it prints the import error and returns 2.
"""

from __future__ import annotations

import argparse
import importlib
import pkgutil
import sys
import traceback
from pathlib import Path
from types import ModuleType
from typing import Sequence

from . import __version__, network, paths, pilot

DEFAULT_CASE = paths.REAL_CASE


class StepError(Exception):
    """Raise from a step's run() for an expected failure: the CLI prints the message and returns 1."""


def discover_steps() -> tuple[dict[str, ModuleType], dict[str, str]]:
    """Import every step module. Returns ({NAME: module}, {module name: import error text})."""
    from . import steps as steps_pkg

    found: dict[str, ModuleType] = {}
    broken: dict[str, str] = {}
    for info in sorted(pkgutil.iter_modules(steps_pkg.__path__), key=lambda i: i.name):
        if info.name.startswith("_"):
            continue
        try:
            mod = importlib.import_module(f"{steps_pkg.__name__}.{info.name}")
        except Exception:  # a half-written or dependency-less step must not hide the others
            broken[info.name] = traceback.format_exc()
            continue
        name = getattr(mod, "NAME", None)
        if not isinstance(name, str) or not callable(getattr(mod, "run", None)) or not callable(
            getattr(mod, "add_arguments", None)
        ):
            broken[info.name] = "module must define NAME (str), add_arguments(parser) and run(args)"
            continue
        if name in found:
            broken[info.name] = f"duplicate step NAME {name!r} (also in {found[name].__name__})"
            continue
        found[name] = mod
    return found, broken


def _add_common(p: argparse.ArgumentParser, default_case: str) -> None:
    p.add_argument("--case", default=default_case, help=f"case id (default: {default_case})")
    p.add_argument("--out", type=Path, default=paths.default_out_dir(),
                   help="output root; packages go to <out>/cases/<id>/ (default: station/pipelines/out)")
    p.add_argument("--cache", type=Path, default=paths.default_cache_dir(),
                   help="download and intermediate cache (default: station/pipelines/.cache)")
    p.add_argument("--force", action="store_true", help="ignore cache stamps and rebuild")


def _broken_runner(module_name: str, error: str):
    def run(_args) -> int:
        print(f"atlas: step module steps/{module_name}.py could not be loaded:\n{error}", file=sys.stderr)
        return 2
    return run


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="atlas", description="ATLAS Station offline pipelines")
    parser.add_argument("--version", action="version", version=f"atlas-pipelines {__version__}")
    groups = parser.add_subparsers(dest="group", metavar="<group>")
    case = groups.add_parser("case", help="build case packages (Lung Mission)", description=(
        "Build case packages step by step. Each step is idempotent and cached by input hash."))
    sub = case.add_subparsers(dest="step", metavar="<step>")

    steps, broken = discover_steps()
    for name, mod in sorted(steps.items()):
        doc = getattr(mod, "HELP", None) or (mod.__doc__ or "").strip().splitlines()[0:1]
        if isinstance(doc, list):
            doc = doc[0] if doc else ""
        sp = sub.add_parser(name, help=doc, description=mod.__doc__,
                            formatter_class=argparse.RawDescriptionHelpFormatter)
        _add_common(sp, getattr(mod, "DEFAULT_CASE", DEFAULT_CASE))
        mod.add_arguments(sp)
        sp.set_defaults(_run=mod.run)
    for module_name, error in sorted(broken.items()):
        if module_name in steps:
            continue
        sp = sub.add_parser(module_name, help="(broken: failed to load)")
        _add_common(sp, DEFAULT_CASE)
        sp.set_defaults(_run=_broken_runner(module_name, error))
    parser.set_defaults(_case_parser=case)
    pilot.add_pilot_parser(groups)  # `atlas pilot export|check` (P2-D13, atlas_pipelines/pilot.py)
    network.add_network_parser(groups)  # `atlas network build|bundle` (NET-01, atlas_pipelines/network.py)
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    if args.group is None:
        parser.print_help()
        return 2
    if args.group == "pilot":
        return pilot.run_from_args(args)
    if args.group == "network":
        return network.run_from_args(args)
    if getattr(args, "step", None) is None:
        args._case_parser.print_help()
        return 2
    args.out = Path(args.out).expanduser().resolve()
    args.cache = Path(args.cache).expanduser().resolve()
    try:
        rc = args._run(args)
    except StepError as e:
        print(f"atlas case {args.step}: {e}", file=sys.stderr)
        return 1
    return int(rc or 0)


if __name__ == "__main__":
    sys.exit(main())
