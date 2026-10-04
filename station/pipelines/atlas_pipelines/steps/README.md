# Pipeline steps

`atlas case <step> [options]` runs one step. A step is a module in this directory; the CLI
(`atlas_pipelines/cli.py`) discovers it by file name. **To add a step, add `steps/<name>.py`. Do not
edit `cli.py`.** Files whose name starts with `_` are ignored (use them for helpers).

## Interface

| Name | Required | Meaning |
|---|---|---|
| `NAME: str` | yes | The word typed after `atlas case` (normally the file name) |
| `add_arguments(parser)` | yes | Add the step's own `argparse` options; may do nothing |
| `run(args) -> int` | yes | Do the work; return 0 on success. Raise `atlas_pipelines.cli.StepError("message")` for an expected failure (exit code 1) |
| `HELP: str` | no | One line for `atlas case -h` (default: first line of the module docstring) |
| `DEFAULT_CASE: str` | no | Default of `--case` for this step (CLI default: `lung101`; the twin step sets `twin`) |

Options every step receives (the CLI adds them; do not redefine them):

| Option | In `run` | Default |
|---|---|---|
| `--case <id>` | `args.case` (str) | `lung101` |
| `--out <dir>` | `args.out` (absolute `pathlib.Path`) | `station/pipelines/out` |
| `--cache <dir>` | `args.cache` (absolute `pathlib.Path`) | `station/pipelines/.cache` |
| `--force` | `args.force` (bool) | off: use cache stamps |

Where things go (`atlas_pipelines/paths.py`):

- `paths.case_dir(args.case, args.out, create=True)` is `<out>/cases/<id>/`: package files only (docs/case_contract.md).
- `paths.work_dir(args.case, args.out, create=True)` is `<out>/work/<id>/`: scratch that is not shipped.
- `paths.stamp_path(args.case, NAME, args.out)` is the cache stamp of this step.
- `paths.frame_path(args.case, args.out)` is `frame.json`, written by `tube4d` / `twin`, read by later steps (`contract.Frame`).
- Downloads and reusable intermediates go under `args.cache`.

## Example

```python
"""Write the occupancy texture of a case."""          # first line doubles as the help text
from atlas_pipelines import cache, paths
from atlas_pipelines.cli import StepError

NAME = "occ"
CODE_VERSION = "occ/1"            # bump when the code changes what it writes


def add_arguments(parser):
    parser.add_argument("--size", type=int, default=64)


def run(args) -> int:
    case = paths.case_dir(args.case, args.out, create=True)
    tube = case / "tube4d.bin"
    if not tube.exists():
        raise StepError(f"{tube} is missing: run `atlas case tube4d --case {args.case}` first")
    out = case / "occ.r8"

    def work():
        out.write_bytes(bytes(args.size ** 3))

    ran = cache.run_cached(
        stamp=paths.stamp_path(args.case, NAME, args.out),
        code_version=CODE_VERSION,
        params={"case": args.case, "size": args.size},
        inputs=[tube],
        outputs=[out],
        work=work,
        force=args.force,
    )
    print(f"occ: {'built' if ran else 'up to date (cached)'} {out}")
    return 0
```

Run it with `atlas case occ --case twin --size 64`; run it again and it prints "up to date".

## Rules

- Idempotent: running a step twice gives the same bytes. No timestamps, no random seeds left unset.
- Cached by input hash: use `cache.run_cached` or `cache.StepCache` (documented in `cache.py`).
- Write package files with `atlas_pipelines.contract` (`write_tube4d`, `write_table`, `write_manifest`,
  `write_json`) and `atlas_pipelines.glb` (`write_glb`), so the bytes follow docs/case_contract.md.
- Take every label, licence and credit string from `atlas_pipelines.labels`.
- A step may import another step's module for its functions (the twin step reuses tube4d and shells).
- The built-in step `info` lists the discovered steps and the files of a case: `atlas case info --case twin`.
