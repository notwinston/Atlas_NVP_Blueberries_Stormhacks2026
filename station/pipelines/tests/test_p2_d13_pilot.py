"""P2-D13 Event logging and pilot export (Master Plan 10.2.8).

Acceptance (exact): all 7 Core event types present after one full run (`atlas pilot check` on the SYNTHETIC full
run passes, and fails with a clear message when one type is removed); the export is deterministic (byte-identical
on re-run); the numbers in the CSV equal an independent recomputation made here from the generator's own values.

The real acceptance (a full run on the phone through the Hub) is deferred to Phase 1 / the device: the synthetic
session proves the reader and the exporter, not that the app emits the events.
"""

from __future__ import annotations

import csv
import io
import math
import statistics
import subprocess
import sys
from pathlib import Path

import pytest

from atlas_pipelines import cli, pilot
from synthetic_pilot_session import make_run, write_run

CORE = ("m2_place", "m4_gate", "m3_pick", "fork", "compass_choice", "sky_pick", "gc_check")


def _read_csv(path: Path) -> list[dict]:
    return list(csv.DictReader(io.StringIO(path.read_text(encoding="utf-8"))))


def _atlas(*argv: str, capsys=None):
    return cli.main(list(argv))


@pytest.fixture()
def run():
    return make_run(seed=7)


@pytest.fixture()
def events_file(tmp_path, run):
    return write_run(tmp_path / "sess" / "events.jsonl", run)


# ------------------------------------------------------------------------------------ check
def test_core_constant_matches_plan():
    assert pilot.CORE_EVENT_TYPES == CORE


def test_check_passes_on_full_synthetic_run(events_file, capsys):
    rc = _atlas("pilot", "check", str(events_file))
    out = capsys.readouterr()
    assert rc == 0, out
    for t in CORE:
        assert f"present  {t}" in out.out
    assert "all 7 Core event types present" in out.out


@pytest.mark.parametrize("missing", CORE)
def test_check_fails_clearly_when_one_type_is_removed(tmp_path, run, missing, capsys):
    f = write_run(tmp_path / "events.jsonl", run, drop=(missing,))
    rc = _atlas("pilot", "check", str(f))
    out = capsys.readouterr()
    assert rc == 1
    assert f"MISSING  {missing}" in out.out
    assert f"1 of 7 Core event types missing: {missing}" in out.err
    for other in CORE:
        if other != missing:
            assert f"present  {other}" in out.out


def test_check_reports_all_missing_on_empty_and_bad_file(tmp_path, capsys):
    f = tmp_path / "events.jsonl"
    f.write_text("\n{not json\n[1,2]\n", encoding="utf-8")
    assert _atlas("pilot", "check", str(f)) == 1
    err = capsys.readouterr().err
    assert "7 of 7 Core event types missing" in err and "2 bad lines" in err


def test_check_missing_file(tmp_path, capsys):
    assert _atlas("pilot", "check", str(tmp_path / "nope.jsonl")) == 1
    assert "no such file" in capsys.readouterr().err


def test_dotted_and_prefixed_names_are_tolerated(tmp_path):
    f = tmp_path / "e.jsonl"
    f.write_text("".join('{"t_us":%d,"scene":"lung","ev":"%s","data":{}}\n' % (i, n)
                         for i, n in enumerate(["lung.m2_place", "M4.gate", "m3_pick", "fork", "compass_choice",
                                                "sky_pick", "gc_check"])), encoding="utf-8")
    events, bad = pilot.read_events(f)
    assert bad == 0 and all(pilot.core_presence(events).values())


# ------------------------------------------------------------------------------------ determinism
def test_export_is_byte_identical_on_rerun(tmp_path, events_file):
    a, b = tmp_path / "a.csv", tmp_path / "b.csv"
    assert _atlas("pilot", "export", str(events_file), "--out", str(a)) == 0
    assert _atlas("pilot", "export", str(events_file), "--out", str(b)) == 0
    assert a.read_bytes() == b.read_bytes()
    # the generator itself is deterministic too, so a second file from the same seed gives the same bytes
    again = write_run(tmp_path / "again" / "sess" / "events.jsonl", make_run(seed=7))
    assert again.read_bytes() == events_file.read_bytes()
    c = tmp_path / "c.csv"
    _atlas("pilot", "export", str(again), "--out", str(c))
    assert c.read_bytes() == a.read_bytes()
    raw = a.read_bytes()
    assert not raw.startswith(b"\xef\xbb\xbf") and b"\r" not in raw and raw.endswith(b"\n")


def test_export_via_subprocess_console_entry(tmp_path, events_file):
    out = tmp_path / "s.csv"
    env_path = str(Path(pilot.__file__).resolve().parents[1])
    r = subprocess.run([sys.executable, "-m", "atlas_pipelines.cli", "pilot", "export", str(events_file), "--out", str(out)],
                       capture_output=True, text=True, env={"PYTHONPATH": env_path, "PATH": ""}, timeout=120)
    assert r.returncode == 0, r.stderr
    assert out.is_file()


# ------------------------------------------------------------------------------------ numbers
def _approx(x: float):
    return pytest.approx(x, rel=1e-9, abs=1e-12)


def test_csv_numbers_equal_independent_recomputation(tmp_path, run, events_file):
    out = tmp_path / "lung_summary.csv"
    assert _atlas("pilot", "export", str(events_file), "--out", str(out)) == 0
    rows = _read_csv(out)
    assert list(rows[0].keys()) == list(pilot.COLUMNS) and len(rows) == 1
    r = rows[0]
    tr = run.truth

    assert r["session_id"] == run.session_id
    assert int(r["n_events"]) == len(run.events) and int(r["n_bad_lines"]) == 0
    assert float(r["duration_s"]) == _approx(tr["duration_s"])
    assert int(r["core_types_present"]) == 7

    # M2
    m2 = tr["m2"]
    assert int(r["m2_n"]) == len(m2)
    assert float(r["m2_hit_last"]) == _approx(m2[-1]["hit"])
    assert float(r["m2_hit_max"]) == _approx(max(x["hit"] for x in m2))
    assert float(r["m2_best"]) == _approx(m2[0]["best"])
    # M4
    m4 = tr["m4"]
    assert int(r["m4_n"]) == 1
    assert int(r["m4_start"]) == m4["start"] and int(r["m4_len"]) == m4["len"]
    assert float(r["m4_duty"]) == _approx(m4["len"] / 10)
    assert float(r["m4_itv_cm3"]) == _approx(m4["itv_cm3"]) and float(r["m4_best_cm3"]) == _approx(m4["best_cm3"])
    assert float(r["m4_gap_cm3"]) == _approx(m4["itv_cm3"] - m4["best_cm3"])
    assert float(r["m4_ratio"]) == _approx(m4["best_cm3"] / m4["itv_cm3"])
    # M3
    bef = [x for x in tr["m3"] if x["when"] == "before"]
    aft = [x for x in tr["m3"] if x["when"] == "after"]
    assert int(r["m3_n"]) == 2
    assert (int(r["m3_before_n"]), int(r["m3_before_correct_n"])) == (len(bef), sum(x["correct"] for x in bef))
    assert (int(r["m3_after_n"]), int(r["m3_after_correct_n"])) == (len(aft), sum(x["correct"] for x in aft))
    # forks
    fk = tr["fork"]
    assert int(r["fork_n"]) == 4 and int(r["fork_correct_n"]) == sum(f["correct"] for f in fk)
    wb = [f for f in fk if f["beacon_shown"]]
    nb = [f for f in fk if not f["beacon_shown"]]
    assert (int(r["fork_beacon_n"]), int(r["fork_beacon_correct_n"])) == (len(wb), sum(f["correct"] for f in wb))
    assert (int(r["fork_nobeacon_n"]), int(r["fork_nobeacon_correct_n"])) == (len(nb), sum(f["correct"] for f in nb))
    ts = [f["t_s"] for f in fk]
    assert float(r["fork_t_sum_s"]) == _approx(sum(ts))
    assert float(r["fork_t_mean_s"]) == _approx(statistics.fmean(ts))
    assert float(r["fork_t_max_s"]) == _approx(max(ts))
    # compass, sky, gc
    assert int(r["compass_n"]) == 1 and r["compass_choice"] == tr["compass"]["choice"] and r["compass_correct"] == "1"
    sky = tr["sky"]
    assert int(r["sky_n"]) == 1
    assert float(r["sky_az_deg"]) == _approx(sky["az_deg"]) and float(r["sky_el_deg"]) == _approx(sky["el_deg"])
    assert float(r["sky_clear_mm"]) == _approx(sky["clear_mm"])
    assert r["sky_robust"] == ("1" if sky["robust"] else "0")
    assert int(r["gc_n"]) == 1 and float(r["gc_angle_sum_deg"]) == _approx(tr["gc"]["angle_sum_deg"])
    # walked, FMS, SEQ
    assert float(r["walked_m"]) == _approx(tr["walked_m"])
    f = tr["fms"]
    assert int(r["fms_n"]) == len(f)
    assert float(r["fms_mean"]) == _approx(sum(f) / len(f))
    assert (float(r["fms_min"]), float(r["fms_max"]), float(r["fms_last"])) == (min(f), max(f), f[-1])
    assert float(r["fms_per_min"]) == _approx(len(f) / (tr["duration_s"] / 60))
    assert float(r["seq"]) == tr["seq"]
    # M1 (Extended)
    assert int(r["m1_n"]) == 1 and float(r["m1_C"]) == 0.97 and float(r["m1_O"]) == 1.4 and r["m1_pass"] == "1"


def test_numbers_hold_for_other_seeds(tmp_path):
    for seed in (1, 2, 3):
        run = make_run(seed)
        f = write_run(tmp_path / str(seed) / "events.jsonl", run)
        out = tmp_path / f"{seed}.csv"
        assert _atlas("pilot", "export", str(f), "--out", str(out)) == 0
        r = _read_csv(out)[0]
        assert float(r["m2_hit_max"]) == _approx(max(x["hit"] for x in run.truth["m2"]))
        assert int(r["fork_correct_n"]) == sum(x["correct"] for x in run.truth["fork"])
        assert float(r["fms_mean"]) == _approx(statistics.fmean(run.truth["fms"]))


# ------------------------------------------------------------------------------------ tolerance
def test_missing_optional_events_give_empty_cells_not_invented_values(tmp_path, run):
    f = write_run(tmp_path / "events.jsonl", run, drop=("fms", "seq", "walked", "m1_fit", "sky_pick", "m4_gate"))
    out = tmp_path / "o.csv"
    assert _atlas("pilot", "export", str(f), "--out", str(out)) == 0
    r = _read_csv(out)[0]
    for col in ("fms_mean", "fms_min", "fms_max", "fms_last", "fms_per_min", "seq", "walked_m", "m1_C", "m1_O", "m1_pass",
                "sky_az_deg", "sky_robust", "m4_itv_cm3", "m4_ratio", "m4_start"):
        assert r[col] == "", col
    for col in ("fms_n", "m1_n", "sky_n", "m4_n"):
        assert r[col] == "0", col
    assert int(r["core_types_present"]) == 5 and float(r["m2_hit_max"]) > 0


def test_unknown_events_bad_lines_and_wrong_types_are_skipped(tmp_path, run):
    extra = ("", "not json at all", "[1,2,3]", '{"ev":""}', '{"t_us":1,"scene":"lung","ev":"fork","data":"oops"}',
             '{"t_us":1,"scene":"lung","ev":"m2_place","data":{"hit":"high","best":true}}',
             '{"t_us":1,"scene":"x","ev":"brand.new","data":{"a":1}}')
    f = write_run(tmp_path / "events.jsonl", run, extra_lines=extra)
    out = tmp_path / "o.csv"
    assert _atlas("pilot", "export", str(f), "--out", str(out)) == 0
    r = _read_csv(out)[0]
    assert int(r["n_bad_lines"]) == 3                       # not json, [..], empty ev
    assert int(r["n_events"]) == len(run.events) + 3        # fork with bad data, m2 with bad data, brand.new
    assert int(r["fork_n"]) == 5 and int(r["fork_correct_n"]) == sum(x["correct"] for x in run.truth["fork"])
    # the bad m2 event counts as a placement but its non-numeric values are not turned into numbers
    assert int(r["m2_n"]) == 3 and r["m2_hit_last"] == ""
    assert float(r["m2_hit_max"]) == _approx(max(x["hit"] for x in run.truth["m2"]))


def test_session_id_falls_back_to_folder_name_and_is_empty_for_none(tmp_path, run):
    f = write_run(tmp_path / "20260101-abc" / "events.jsonl", run, drop=("session",))
    out = tmp_path / "o.csv"
    _atlas("pilot", "export", str(f), "--out", str(out))
    assert _read_csv(out)[0]["session_id"] == "20260101-abc"


def test_export_several_sessions_one_row_each(tmp_path):
    files = [write_run(tmp_path / f"s{i}" / "events.jsonl", make_run(i)) for i in (1, 2)]
    out = tmp_path / "all.csv"
    assert _atlas("pilot", "export", *map(str, files), "--out", str(out)) == 0
    rows = _read_csv(out)
    assert [r["session_id"] for r in rows] == ["synthetic-0001", "synthetic-0002"]


def test_empty_file_exports_a_row_of_counts_only(tmp_path):
    f = tmp_path / "s" / "events.jsonl"
    f.parent.mkdir()
    f.write_text("", encoding="utf-8")
    out = tmp_path / "o.csv"
    assert _atlas("pilot", "export", str(f), "--out", str(out)) == 0
    r = _read_csv(out)[0]
    assert r["session_id"] == "s" and r["n_events"] == "0" and r["m2_hit_max"] == "" and r["duration_s"] == ""


# ------------------------------------------------------------------------------------ the CLI did not break `atlas case`
def test_atlas_case_parser_unchanged():
    p = cli.build_parser()
    assert p.parse_args(["case", "twin"]).step == "twin"
    steps = cli.discover_steps()[0]
    assert {"twin", "pack", "verify"} <= set(steps)
    assert cli.main([]) == 2  # no group: help, 2
    assert cli.main(["pilot"]) == 2  # no command: help, 2
    assert cli.main(["case"]) == 2
