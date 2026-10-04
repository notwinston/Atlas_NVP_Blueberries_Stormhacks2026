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

Every step is idempotent and cached by input hash: running it again without changes does nothing. A stamp records its
outputs relative to itself, so an `out/` tree moved or copied as a whole stays fresh.
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
(every step reports "up to date"; measured 0.6 s).

**Expected outputs** (`out/cases/<id>/`, listed by `atlas case info --case <id>`), from the run of 4 Oct 2026:

| `lung101` (24 files + `manifest.json`, 6.0 MB) | `twin` (9 files + `manifest.json`, 1.1 MB) |
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
`extra.complete = true` means "every expected file is present" and nothing more; it is not a statement that the files
pass their acceptance tests, and the fixed text `extra.note` in every manifest says so.

**Labels** (exact strings, `atlas_pipelines/labels.py`): the real case's files carry "Public research data: TCIA
4D-Lung, CC BY 3.0. Meshes derived by ATLAS."; `tube4d_rigid.bin` carries "Simplified: rigid shape"; `airway/*` carry
"Model-generated airway"; every twin file carries "Illustrative model" (no full stop, as in the plan's table). One
label per file: all of `sky/` carries the real-data label (the maps are derived from the real contours), in the
manifest and in `sky/sky.json` alike; "Teaching threshold" labels the 5 mm value, in the field `threshold_label` of
`sky/sky.json`, not a file. The footer "Education and research visualisation. Not for clinical use."
is in every manifest and every `CREDITS.txt`.

**Deferred to Phase 1** (needs the phone, the Hub or the app build; not done here): the two packages inside the app
build, the Hub route `/cases/<id>/...` that serves them, and the 2-second load test with the network off on the phone
(P2-D09 acceptance, "Phone in hand, Station"). Station-side, `out/cases/<id>/` is the delivery directory.

## Acceptance status (4 Oct 2026)

After `atlas case all --force` (both cases rebuilt, 5 min 43 s, then a second run with every step "up to date"),
`.venv/bin/python -m pytest tests -q` gives **254 passed**, none failed, none skipped (8 min 28 s, .NET present).
Nothing fails. Two acceptance numbers of Master Plan section 10.2.8 are **not met as the plan wrote them** and are
tested under a documented deviation instead; the full text, with every measured value and the plan rows to update, is
section 13 of [`docs/case_contract.md`](../../docs/case_contract.md).

**Passes as the plan wrote it**

| Work package | Plan (10.2.8) | Measured |
|---|---|---|
| P2-D01 | 2-tet fixture round-trips Python to C# byte-identical | passes |
| P2-D02 | GTV 0.52 cm3 within 3 %; ITV pinned and held within 3 %; core 0 | GTV 0.523 cm3 (table), 0.512 cm3 (shipped phase meshes); ITV 2.136 cm3; core 0 |
| P2-D03 | 20 series; centroid range 3.1 / 3.1 / 6.8 mm within 1 mm | 3.13 / 3.09 / 6.85 mm |
| P2-D04 (part) | 18,000 tets; every interior face shared by exactly 2 tets | 18,000; 35,400 faces shared by 2, none by more |
| P2-D05 (part) | core 17.2 cm3 within 5 % | 16.985 cm3 (-1.25 %) |
| P2-D06 | at most 40k triangles; lungs 4.42 L at 0 % and 3.87 L at 50 % within 3 % | 36,000 triangles; 4.3905 L (-0.67 %) and 3.8331 L (-0.95 %) |
| P2-D07 | 16 generations addressable; diameter ratio 0.794 +/- 0.08; exactly 1 Two Compasses fork on the 4-fork route | 65,536 addresses resolve; ratios 0.714 to 0.873; 1 fork (target `RRRL`, fork `""`, margin 0.142) |
| P2-D08 | `robust.r8` equals the AND of the 10 phase maps; HEALPix indices match astropy-healpix | 0 differing pixels; passes |
| P2-D09 (Station part) | hash check passes for every file | `lung101` 24 files, `twin` 9 files, 0 problems |

P2-D07 passes as written only because the generator departs from the plan's fixed angles (next table).

**Passes under a documented deviation**

| Work package | Plan | Tested instead | Measured | Contract |
|---|---|---|---|---|
| P2-D04 | slice at each phase within 1 mm Hausdorff of that phase's contour mesh | per phase and per direction, mean distance at most 0.5 mm and 95th percentile at most 1.5 mm; Hausdorff reported, not asserted | mean at most 0.313 mm, 95th percentile at most 1.011 mm; **Hausdorff 1.7 to 5.9 mm, the plan's 1 mm is not met** | 13.2 |
| P2-D04 | template reaches each phase by Newton projection | real case: demons registration, then the projection | 0 self-intersections (10 with projection alone) | 13.1 |
| P2-D05 | ITV 47.4 cm3 within 5 % | raw-mask union 47.4 cm3 within 5 %; `itv_cm3` equals the volume of `itv.glb` within 1 % | raw union 47.293 cm3 (-0.22 %); **`itv_cm3` 43.978 cm3, 7.2 % below 47.4**; `itv.glb` encloses 43.907 cm3 (-0.16 %) | 13.3 |
| P2-D07 | angle 37 degrees, plane rotation 90 degrees | angle follows the fork's diameter ratios (mean 36.3, range 11.6 to 49.2 degrees); rotation normal, mean 90, sd 41.6 degrees | with the fixed values no route has a Two Compasses fork | 13.4, 13.5 |
| P2-D06 | lungs from the contours | two right-lung contours filed under the left-lung ROI (81.4 and 37.7 cm3, phases 6 and 7) are left out of the left lung | nothing dropped at the acceptance phases beyond 0.02 cm3 | 13.7 |
| P2-D09 | "Teaching threshold" as a label | `sky/*` carry the real-data label; "Teaching threshold" labels the 5 mm value in `sky/sky.json` | label set of `lung101` has 3 strings | 13.8 |

**Still failing:** nothing in this suite.

**Not tested here:** the phone half of P2-D09 (package loads from the app build in 2 s with the network off; Hub route),
see "Deferred to Phase 1" above.

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

### Engine-free airway core (lane B: P2-B01, P2-B06, P2-B08)

`csharp/Atlas.Airway.Core/` is the C# side of `airway/tree.json`: the resolver of rule `atlas.airway/2`, the Rule S
steps, the beacon, the route cost and the Two Compasses, without Unity (see its `README.md`). Its tests are
`tests/test_p2_b_airway_core.py`. They run from a clean checkout on committed copies of `tree.json` and `greedy.json`
(`tests/fixtures/p2_b_airway/`, model-generated, no patient data); with `ATLAS_CASES_DIR=<out>/cases` the same checks run
on the built `lung101` package and the copies must be byte-equal to it.

After a deliberate change of `atlas_pipelines/airway.py` regenerate the copies and the resolver reference:
`PYTHONPATH=. .venv/bin/python tests/fixtures/make_p2_b_airway_fixture.py`, and port the change to `AirwayResolver.cs`.

## Pilot export (P2-D13)

`atlas pilot export <events.jsonl> [<events.jsonl> ...] [--out lung_summary.csv]` turns the Hub's per-session
`events.jsonl` (docs/contracts/lane-d-link.md 2.6 and 3.4) into `lung_summary.csv`, one row per input file.
`atlas pilot check <events.jsonl>` lists which of the 7 Core event types of Master Plan 10.2.6 (`m2_place`, `m4_gate`,
`m3_pick`, `fork`, `compass_choice`, `sky_pick`, `gc_check`) are present and exits 1, naming the missing ones, when any
is absent. Code: `atlas_pipelines/pilot.py`; test: `tests/test_p2_d13_pilot.py`, which runs on a SYNTHETIC session from
`tests/synthetic_pilot_session.py` (test data only, never real people).

Rules: unknown event types and bad lines are skipped (counted in `n_bad_lines`); a measure with no data is an empty
cell, never a guess; `*_n` counts are 0 when absent. Floats have 12 significant digits, booleans are 1/0, line ends
are `\n`, so the same input gives the same bytes. "last" below means the last such event in file order.

| Column(s) | Meaning |
|---|---|
| `session_id` | `session` event `sessionId`, else the file's parent folder name |
| `n_events`, `n_bad_lines`, `duration_s`, `core_types_present` | parsed lines, skipped lines, last minus first phone `t_us` in seconds (Hub events excluded), Core types seen (0..7) |
| `m2_n`, `m2_hit_last`, `m2_hit_max`, `m2_best` | M2 placements; `hit` (H = rho(p)) of the last and the largest; largest `best` |
| `m4_n`, `m4_start`, `m4_len`, `m4_duty`, `m4_itv_cm3`, `m4_best_cm3`, `m4_gap_cm3`, `m4_ratio` | M4 gates; the last gate's fields; gap = itv - best (0 is the best arc of equal duty); ratio = best / itv (1 is the best arc) |
| `m3_n`, `m3_before_n`, `m3_before_correct_n`, `m3_after_n`, `m3_after_correct_n` | M3 picks, split by `when` = before / after beat f |
| `fork_n`, `fork_correct_n`, `fork_beacon_n`, `fork_beacon_correct_n`, `fork_nobeacon_n`, `fork_nobeacon_correct_n` | forks and correct ones, overall and split by `beacon_shown` |
| `fork_t_sum_s`, `fork_t_mean_s`, `fork_t_max_s` | sum, mean, max of `t_s` |
| `compass_n`, `compass_choice`, `compass_correct` | Two Compasses: count, `choice` of the last (text), `correct` (1/0). Data members assumed: the plan names the event only |
| `sky_n`, `sky_az_deg`, `sky_el_deg`, `sky_clear_mm`, `sky_robust` | needle sky: picks; the last pick's fields |
| `gc_n`, `gc_angle_sum_deg` | Gauss-Bonnet check: count; `angle_sum_deg` of the last |
| `walked_m` | `dist_m` of the last `walked` event (event name and member assumed: the plan logs a walked distance but fixes no name) |
| `fms_n`, `fms_mean`, `fms_min`, `fms_max`, `fms_last`, `fms_per_min` | Hub `fms` scores (0..20): count, mean, min, max, last; count per minute of session |
| `seq` | Single Ease Question score (1..7) of the last `seq` event |
| `m1_n`, `m1_C`, `m1_O`, `m1_pass` | Extended tier M1: fits; last coverage C, excess O, C >= 0.95 |

Event names are matched case-insensitively with a leading `lung.` dropped and `.` read as `_`. The plan's names contain an
underscore, which the contract's name rule (`[a-z][A-Za-z0-9]*` per segment) does not allow: to be settled with lane D when
the phone code is written; the exporter accepts both spellings. A pilot claim ("in a pilot with n people, hit fraction
changed from X to Y") is made from many rows of this table by whoever runs the pilot; the exporter makes no claim.
