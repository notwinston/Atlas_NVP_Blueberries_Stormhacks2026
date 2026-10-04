"""Re-hash every file of a package against its manifest.json; exit non-zero on any mismatch.

Master Plan 10.2.8 row P2-D09: "Hash check passes for every file".

    atlas case verify [--case lung101|twin|all]

Checks, for each package: manifest.json parses (strict atlas.case/1); every listed file exists with
the recorded size and SHA-256; no unlisted file is present (dot-files aside). Prints one line per
file and exits 1 if any check fails. A package whose manifest records missing expected files
(``extra.complete = false``) still verifies if its hashes match; it is reported as INCOMPLETE.

What OK means: the files are the files that were packed. It does not mean that the package is complete
(see ``extra.complete``, which means "every expected file is present") or that its files pass their
acceptance tests; those are the tests in ``tests/``.
"""

from __future__ import annotations

from .. import contract, paths
from ..cache import sha256_file

NAME = "verify"
HELP = "re-hash a package against manifest.json (non-zero exit on any mismatch)"
DEFAULT_CASE = "all"


def add_arguments(parser) -> None:
    parser.add_argument("-q", "--quiet", action="store_true", help="print only problems and the summary")


def verify_case(case: str, out, quiet: bool = False) -> list[str]:
    """Verify one case; print the report; return the list of problems."""
    case_dir = paths.case_dir(case, out, create=False)
    mpath = case_dir / contract.MANIFEST_NAME
    if not mpath.is_file():
        problems = [f"{contract.MANIFEST_NAME}: missing (run `atlas case pack --case {case}`)"]
        for p in problems:
            print(f"verify {case}: FAIL {p}")
        return problems
    problems = contract.verify_manifest(case_dir)
    bad = {p.split(":", 1)[0] for p in problems}
    try:
        m = contract.read_manifest(mpath)
    except (OSError, contract.ContractError):
        m = None
    if m is not None and not quiet:
        for f in m.files:
            status = "FAIL" if f.path in bad else "ok  "
            print(f"verify {case}: {status} {f.size:>10d}  {f.sha256[:12]}  {f.path}")
    for p in problems:
        print(f"verify {case}: FAIL {p}")
    if m is not None:
        extra = m.extra or {}
        if extra.get("complete") is False:
            print(f"verify {case}: INCOMPLETE package, missing at pack time: {', '.join(extra.get('missing', []))}")
    n = len(m.files) if m is not None else 0
    print(f"verify {case}: {'FAILED' if problems else 'OK'}: {n} files hashed, {len(problems)} problem(s)")
    return problems


def run(args) -> int:
    from ..cli import StepError
    cases = [paths.REAL_CASE, paths.TWIN_CASE] if args.case == "all" else [args.case]
    total = 0
    for c in cases:
        total += len(verify_case(c, args.out, args.quiet))
    if total:
        raise StepError(f"{total} problem(s): the package does not match its manifest")
    return 0
