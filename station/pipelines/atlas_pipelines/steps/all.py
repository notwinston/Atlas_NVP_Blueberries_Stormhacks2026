"""Run the whole pipeline: every step of both cases, then pack and verify.

    atlas case all [--case lung101|twin|all] [--stop-on-error]

Order, per case (each step is idempotent and cached, so a second run does nothing):

    twin     twin, pack, verify
    lung101  fetch, masks, tube4d, shells, chest, airway, sky, pack, verify

By default a failing step does not stop the run: the later steps still run, and pack records what is
missing, so a package with a stopped upstream step is still packed and its gaps are listed. The exit
code is 1 when any step failed. ``--force`` is passed to every step except ``fetch`` (a forced fetch
would download 0.8 GB again; use ``atlas case fetch --force`` for that).
"""

from __future__ import annotations

import time

from .. import paths

NAME = "all"
HELP = "run every step of both cases, then pack and verify"
DEFAULT_CASE = "all"

SEQUENCE = {
    paths.TWIN_CASE: ("twin", "pack", "verify"),
    paths.REAL_CASE: ("fetch", "masks", "tube4d", "shells", "chest", "airway", "sky", "pack", "verify"),
}
ORDER = (paths.TWIN_CASE, paths.REAL_CASE)


def add_arguments(parser) -> None:
    parser.add_argument("--stop-on-error", action="store_true",
                        help="stop at the first failing step instead of continuing")


def run(args) -> int:
    from ..cli import StepError, main

    if args.case == "all":
        cases = ORDER
    elif args.case in SEQUENCE:
        cases = (args.case,)
    else:
        raise StepError(f"all: unknown case {args.case!r} (all, {', '.join(ORDER)})")
    results: list[tuple[str, str, int, float]] = []
    for case in cases:
        for step in SEQUENCE[case]:
            argv = ["case", step, "--case", case, "--out", str(args.out), "--cache", str(args.cache)]
            if args.force and step != "fetch":
                argv.append("--force")
            print(f"\n=== atlas {' '.join(argv)}", flush=True)
            t0 = time.time()
            try:
                rc = main(argv)
            except SystemExit as e:  # argparse errors
                rc = int(e.code or 0)
            dt = time.time() - t0
            results.append((case, step, rc, dt))
            if rc != 0 and args.stop_on_error:
                break
        else:
            continue
        break
    print("\n=== summary")
    for case, step, rc, dt in results:
        print(f"  {case:8s} {step:8s} {'ok' if rc == 0 else f'FAILED (exit {rc})':16s} {dt:8.1f} s")
    failed = [(c, s) for c, s, rc, _ in results if rc != 0]
    if failed:
        raise StepError("failed steps: " + ", ".join(f"{s}({c})" for c, s in failed))
    return 0
