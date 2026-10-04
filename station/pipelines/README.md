# ATLAS Station pipelines

Offline data pipeline of the Phase 2 "Lung Mission" (Master Plan §10.2.4, work packages P2-D01 to P2-D09).
It builds two case packages, `lung101` (TCIA 4D-Lung patient 101_HM10395) and `twin` (an illustrative model),
in the format of [`docs/case_contract.md`](../../docs/case_contract.md).

Education and research visualisation. Not for clinical use.

## Setup

```sh
cd station/pipelines
python3.12 -m venv .venv            # if venv lacks ensurepip: python3.12 -m venv --without-pip .venv
                                    #   then: curl -sSL https://bootstrap.pypa.io/get-pip.py | .venv/bin/python
.venv/bin/pip install -r requirements.txt
.venv/bin/pip install -e .
.venv/bin/atlas case info
```

`requirements.txt` pins the exact versions installed and tested (Linux aarch64, Python 3.12).

The C# half of the contract test needs the .NET SDK (10.0 LTS). The test looks for `dotnet` on `PATH`, then in
`$DOTNET_ROOT`, then in `~/.dotnet`:

```sh
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel LTS --install-dir ~/.dotnet
```

On a machine without ICU libraries, `dotnet` needs `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`; the test sets it.

## Running

```sh
.venv/bin/atlas case <step> [--case lung101|twin] [--out DIR] [--cache DIR] [--force] [step options]
.venv/bin/atlas case -h
```

| Directory | Holds | In Git |
|---|---|---|
| `.cache/` | downloads (DICOM) and reusable intermediates | no |
| `out/cases/<id>/` | the case package | no |
| `out/work/<id>/` | scratch: `frame.json`, cache stamps | no |

Every step is idempotent and cached by input hash: running it again without changes does nothing.
DICOM is never committed.

## Running the whole pipeline

From a clean checkout (Linux, Python 3.12, 16 GB RAM is plenty; about 1 GB of disk for `.cache/` and `out/`):

```sh
cd station/pipelines
python3.12 -m venv .venv                      # see Setup if venv lacks ensurepip
.venv/bin/pip install -r requirements.txt && .venv/bin/pip install -e .
.venv/bin/atlas case all                      # both cases, every step, then pack and verify
```

`atlas case all [--case lung101|twin|all] [--force] [--stop-on-error]` runs these steps, in this order:

| Case | Steps |
|---|---|
| `twin` | `twin`, `pack`, `verify` |
| `lung101` | `fetch`, `masks`, `tube4d`, `shells`, `chest`, `airway`, `sky`, `pack`, `verify` |

A failing step does not stop the run (unless `--stop-on-error`): the later steps still run, `pack` records which
expected files are missing, and the exit code is 1 at the end with a per-step summary. `--force` goes to every step
except `fetch` (use `atlas case fetch --force` to download again). The same thing by hand, one step at a time:

```sh
.venv/bin/atlas case twin    --case twin
.venv/bin/atlas case fetch   && .venv/bin/atlas case masks && .venv/bin/atlas case tube4d \
 && .venv/bin/atlas case shells && .venv/bin/atlas case chest && .venv/bin/atlas case airway && .venv/bin/atlas case sky
.venv/bin/atlas case pack    --case twin    && .venv/bin/atlas case pack   --case lung101
.venv/bin/atlas case verify                                  # both cases; exit 1 on any mismatch
```

**Download.** `fetch` downloads the TCIA 4D-Lung study of patient 101_HM10395 (10 CT + 10 RTSTRUCT series, about
0.8 GB of DICOM on disk, about 0.3 GB over the wire) into `.cache/tcia/`, 4 parallel downloads, resumable; the whole
`.cache/` is about 0.8 GB after `masks`. The download time depends on the TCIA server and the line and was not
measured on a cold cache for this README. Once the data and `out/` are built, `atlas case all` takes under one second
(every step reports "up to date"; measured 0.4 s).

**Expected outputs** (`out/cases/<id>/`, listed by `atlas case info --case <id>`), from the run of 4 Oct 2026:

| `lung101` (24 files + `manifest.json`, 6.3 MB) | `twin` (9 files + `manifest.json`, 1.1 MB) |
|---|---|
| `tube4d.bin`, `tube4d_rigid.bin`, `phases.glb` | `tube4d.bin`, `tube4d_rigid.bin`, `phases.glb` |
| `itv.glb`, `core.glb`, `rho50.glb`, `occ.r8`, `table.json` | `itv.glb`, `core.glb`, `rho50.glb`, `occ.r8`, `table.json` |
| `chest.glb` | |
| `airway/tree.json`, `airway/greedy.json` | |
| `sky/risk_p0.r8` ... `sky/risk_p9.r8`, `sky/robust.r8`, `sky/sky.json` | |
| `CREDITS.txt`, `manifest.json` | `CREDITS.txt`, `manifest.json` |

`pack` hashes every file of the package (SHA-256, size), writes `CREDITS.txt` and `manifest.json` (`atlas.case/1`: id,
kind, label, per-file labels, licence, credit, citations, M = 15, Lambda = 1.5, origin_mm, files). It never completes
a package silently: expected files that are absent are printed (`pack: INCOMPLETE ... missing <file>`) and recorded in
`manifest.json` under `extra.missing` with `extra.complete = false`; `--strict` turns that into exit 1. A DICOM file in a
package stops `pack`. `verify` re-hashes every file against the manifest (and refuses an unlisted file) and exits 1 on
any mismatch; an incomplete package that matches its hashes still verifies and is reported as INCOMPLETE.

**Labels** (exact strings, `atlas_pipelines/labels.py`): the real case's files carry "Public research data: TCIA
4D-Lung, CC BY 3.0. Meshes derived by ATLAS."; `tube4d_rigid.bin` carries "Simplified: rigid shape"; `airway/*` carry
"Model-generated airway"; `sky/robust.r8` and `sky/sky.json` (the 5 mm clearance threshold) carry "Teaching threshold";
every twin file carries "Illustrative model.". The footer "Education and research visualisation. Not for clinical use."
is in every manifest and every `CREDITS.txt`.

**Deferred to Phase 1** (needs the phone, the Hub or the app build; not done here): the two packages inside the app
build, the Hub route `/cases/<id>/...` that serves them, and the 2-second load test with the network off on the phone
(P2-D09 acceptance, "Phone in hand, Station"). Station-side, `out/cases/<id>/` is the delivery directory.

## Known failing acceptance tests (4 Oct 2026)

Three acceptance numbers of Master Plan section 10.2.8 are not met on the real case. The tests assert the plan's
numbers unchanged and fail; `atlas case all` still builds and packs the files. These need a decision from the plan owner.

| Work package | Plan | Measured | Why |
|---|---|---|---|
| P2-D04 | slice at each phase within 1 mm Hausdorff of that phase's contour mesh | 2.9 to 7.2 mm (mean distance 0.22 to 0.34 mm) | phase shapes differ by more than 1 mm; one 600-triangle mean-shape template cannot reach lobes only one phase has |
| P2-D05 | ITV 47.4 cm3 within 5 % | 43.98 cm3 (-7.2 %); raw-mask union 47.29 cm3 | 47.4 was measured on raw masks; the plan's sigma = 1 mm smoothing of 3 mm slices shrinks the union. Core passes (16.99 against 17.2) |
| P2-D07 | exactly 1 Two Compasses fork on the 4-fork route | 0 | with 37 degrees / 90 degrees the Euclidean compass is never wrong on a route that ends at generation 4 |

## Running the whole test suite

```sh
cd station/pipelines
.venv/bin/python -m pytest tests          # one file per work package; downloads the TCIA data first if .cache/ is empty
```

Prerequisites: the venv above; the .NET SDK 10.0 for the C# tests (`dotnet` on `PATH`, in `$DOTNET_ROOT` or in
`~/.dotnet`; without it the C# tests are reported as skipped with the reason "NOT RUN"); network access to TCIA for the
first run (about 0.3 GB, see Download above). The session fixture `tcia_101` fetches and rasterises the data when it is
missing. The P2-D09 tests run `atlas case all` on the real `out/` first, so every package is built (or found cached)
before it is hashed, tampered with (on copies in a temporary directory) and checked; one P2-D09 test also checks both
manifests with the C# reader (`Atlas.Cases.RoundTrip verify`). Run one work package alone with
`.venv/bin/python -m pytest tests/test_p2_d09_pack.py`.

## How steps are discovered

`atlas case <step>` looks for a module `atlas_pipelines/steps/<name>.py` that defines `NAME`,
`add_arguments(parser)` and `run(args) -> int`. Adding a step is adding a file; `cli.py` is not edited.
The interface, the common options and an example are in [`atlas_pipelines/steps/README.md`](atlas_pipelines/steps/README.md).

## Layout

| Path | Content |
|---|---|
| `atlas_pipelines/cli.py` | the `atlas` command and step discovery |
| `atlas_pipelines/paths.py` | default directories, case and work directory helpers |
| `atlas_pipelines/cache.py` | input-hash caching (`run_cached`, `StepCache`) |
| `atlas_pipelines/contract.py` | canonical JSON; readers and writers of `tube4d.bin`, `table.json`, `manifest.json`; hash check |
| `atlas_pipelines/glb.py` | GLB 2.0 writer and reader with morph targets, no dependencies beyond numpy |
| `atlas_pipelines/labels.py` | the exact label, footer, licence and credit strings |
| `atlas_pipelines/steps/` | one module per step (`pack`, `verify`, `all` are the delivery steps) |
| `csharp/Atlas.Cases/` | plain C# readers and writers (System only, no Unity references), ready to move into the app |
| `csharp/Atlas.Cases.Tests/` | their tests, a console program without a test framework |
| `csharp/Atlas.Cases.RoundTrip/` | console tool: read a file, write it back (`tube4d`, `table`, `manifest`, `json`, `verify`) |
| `tests/` | `test_p2_dNN_<name>.py`, one file per work package; `fixtures/` holds committed inputs |

## Tests

```sh
.venv/bin/python -m pytest tests/test_p2_d01_contract.py     # one work package
.venv/bin/python -m pytest                                   # everything
```

Tests that need the TCIA data download it themselves into `.cache/` when it is missing (about 0.8 GB).
The C# tests build `csharp/` with `dotnet build`; without a .NET SDK they are reported as skipped, with the reason.

Regenerate the P2-D01 fixture after a deliberate contract change: `.venv/bin/python tests/fixtures/make_p2_d01_fixture.py`.
