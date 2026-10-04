"""Show the pipeline's directories, the discovered steps and the files of a case package."""

from __future__ import annotations

import json

from .. import __version__, contract, paths

NAME = "info"
HELP = "show directories, discovered steps and the state of a case package"


def add_arguments(parser) -> None:
    parser.add_argument("--json", action="store_true", help="print one JSON object instead of text")


def run(args) -> int:
    from ..cli import discover_steps

    steps, broken = discover_steps()
    case = paths.case_dir(args.case, args.out)
    files = []
    if case.is_dir():
        files = [
            {"path": p.relative_to(case).as_posix(), "size": p.stat().st_size}
            for p in sorted(case.rglob("*"))
            if p.is_file()
        ]
    info = {
        "version": __version__,
        "case": args.case,
        "out": str(args.out),
        "cache": str(args.cache),
        "case_dir": str(case),
        "steps": sorted(steps),
        "broken_steps": sorted(broken),
        "files": files,
        "M": contract.M_DISPLAY,
        "Lambda": contract.LAMBDA_M,
    }
    if args.json:
        print(json.dumps(info, indent=1, sort_keys=True))
        return 0
    print(f"atlas-pipelines {__version__}")
    print(f"out    {args.out}")
    print(f"cache  {args.cache}")
    print(f"steps  {' '.join(sorted(steps))}")
    if broken:
        print(f"broken {' '.join(sorted(broken))}")
    print(f"case   {args.case}  ({case})")
    if not files:
        print("  (no files yet)")
    for f in files:
        print(f"  {f['size']:>10d}  {f['path']}")
    return 0
