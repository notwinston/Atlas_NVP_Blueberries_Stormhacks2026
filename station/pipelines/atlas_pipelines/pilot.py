"""P2-D13 Event logging and pilot export (Master Plan 10.2.6, 10.2.8): ``events.jsonl`` -> ``lung_summary.csv``.

CLI (group ``pilot``, next to ``atlas case``)::

    atlas pilot export <events.jsonl> [<events.jsonl> ...] [--out lung_summary.csv]
    atlas pilot check  <events.jsonl>

``export`` writes one CSV row per input file (one Hub session folder = one ``events.jsonl`` = one session).
``check`` reports which of the 7 Core event types of 10.2.6 are present and returns 1 with a message naming
the missing ones. Nothing here touches the Hub or the phone; it only reads the file the Hub writes.

Input format (docs/contracts/lane-d-link.md 2.6 and 3.4, Appendix B.2): one JSON object per line,
``{"t_us": int, "scene": str, "ev": str, "data": {...}}``, to which the Hub adds ``hub_us``. Hub-made events
(``fms``, ``note``, ``asset_ack``, ``source``) carry ``"scene": "hub"`` and the Hub clock in ``t_us``.
Event names are matched case-insensitively, a leading ``lung.`` is dropped and a ``.`` is read as ``_``
(the contract's segment rule ``[a-z][A-Za-z0-9]*`` does not allow the underscore of ``m2_place``; the plan's
names are the ones used, the dotted spelling is tolerated). Unknown event types, blank lines, unparsable
lines and members of the wrong type are skipped, never guessed; a measure with no data is an EMPTY cell.
Counts (``*_n``) are 0, not empty, when no event of that type was seen.

lung_summary.csv columns (one row per session; "last" = the last such event in file order)
---------------------------------------------------------------------------------------------------
session_id            ``session`` event ``data.sessionId``; else the name of the file's parent folder
n_events              parsed event lines;  n_bad_lines: lines skipped (not JSON / not an object / no ``ev``)
duration_s            (last - first ``t_us``) / 1e6 over the phone's events (``scene`` != ``hub``)
core_types_present    how many of the 7 Core types are present (0..7)
M2 needle dwell       m2_n; m2_hit_last = ``hit`` of the last placement; m2_hit_max = max ``hit``;
                      m2_best = max ``best`` (best possible rho of the case)
M4 gating window      m4_n; of the LAST gate: m4_start, m4_len, m4_duty, m4_itv_cm3, m4_best_cm3 (best arc of equal
                      duty); m4_gap_cm3 = itv - best (0 = the best arc); m4_ratio = best / itv (1 = the best arc)
M3 spot the artefact  m3_n; m3_before_n, m3_before_correct_n, m3_after_n, m3_after_correct_n (``when`` is
                      "before" or "after" beat f; any other value counts in m3_n only)
fork                  fork_n, fork_correct_n; fork_beacon_n / fork_beacon_correct_n (``beacon_shown`` true);
                      fork_nobeacon_n / fork_nobeacon_correct_n (``beacon_shown`` false);
                      fork_t_sum_s, fork_t_mean_s, fork_t_max_s over ``t_s``
compass_choice        compass_n; compass_choice (``choice`` of the last, text); compass_correct (``correct``,
                      1/0). The plan fixes no data members for this event: these two are the assumed ones
sky_pick              sky_n; of the last: sky_az_deg, sky_el_deg, sky_clear_mm, sky_robust (1/0)
gc_check              gc_n; gc_angle_sum_deg (``angle_sum_deg`` of the last)
walked distance       walked_m: ``dist_m`` of the last ``walked`` event (the plan logs a walked distance but fixes
                      no event name; ``walked {dist_m}`` is the assumed one)
FMS                   fms_n; fms_mean, fms_min, fms_max, fms_last over the ``fms`` events (Hub, ``score`` 0..20);
                      fms_per_min = fms_n / duration in minutes
SEQ                   seq: ``score`` (1..7) of the last ``seq`` event
M1 margin fit         (Extended tier) m1_n; of the last: m1_C, m1_O, m1_pass (C >= 0.95, 1/0)

Floats are written with 12 significant digits, ints as ints, booleans as 1/0, line ends ``\\n``, UTF-8, no BOM;
the same input gives the same bytes.

This module makes no clinical claim. The pilot claim 10.2.6 allows is "in a pilot with n people, hit fraction
changed from X to Y": it is made from many rows of this table, by whoever runs the pilot, not here.
"""

from __future__ import annotations

import csv
import json
import math
import sys
from pathlib import Path
from typing import Any, Iterable, Sequence

CORE_EVENT_TYPES = ("m2_place", "m4_gate", "m3_pick", "fork", "compass_choice", "sky_pick", "gc_check")

COLUMNS = (
    "session_id", "n_events", "n_bad_lines", "duration_s", "core_types_present",
    "m2_n", "m2_hit_last", "m2_hit_max", "m2_best",
    "m4_n", "m4_start", "m4_len", "m4_duty", "m4_itv_cm3", "m4_best_cm3", "m4_gap_cm3", "m4_ratio",
    "m3_n", "m3_before_n", "m3_before_correct_n", "m3_after_n", "m3_after_correct_n",
    "fork_n", "fork_correct_n", "fork_beacon_n", "fork_beacon_correct_n",
    "fork_nobeacon_n", "fork_nobeacon_correct_n", "fork_t_sum_s", "fork_t_mean_s", "fork_t_max_s",
    "compass_n", "compass_choice", "compass_correct",
    "sky_n", "sky_az_deg", "sky_el_deg", "sky_clear_mm", "sky_robust",
    "gc_n", "gc_angle_sum_deg",
    "walked_m",
    "fms_n", "fms_mean", "fms_min", "fms_max", "fms_last", "fms_per_min",
    "seq",
    "m1_n", "m1_C", "m1_O", "m1_pass",
)

M1_PASS_COVERAGE = 0.95  # 10.2.6: pass if C >= 0.95


# --------------------------------------------------------------------------- reading
def canon_event_name(ev: str) -> str:
    """'Lung.M2.place' -> 'm2_place'; 'm2_place' -> 'm2_place'."""
    name = ev.strip().lower()
    if name.startswith("lung."):
        name = name[5:]
    return name.replace(".", "_")


class Event:
    """One parsed line: ``name`` (canonical), ``scene``, ``t_us`` (int or None), ``data`` (dict)."""

    __slots__ = ("name", "scene", "t_us", "data")

    def __init__(self, name: str, scene: str, t_us: int | None, data: dict) -> None:
        self.name, self.scene, self.t_us, self.data = name, scene, t_us, data


def read_events(path: str | Path) -> tuple[list[Event], int]:
    """Parse an ``events.jsonl``. Returns (events in file order, number of skipped lines)."""
    events: list[Event] = []
    bad = 0
    with open(path, "r", encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                obj = json.loads(line)
            except ValueError:
                bad += 1
                continue
            if not isinstance(obj, dict) or not isinstance(obj.get("ev"), str) or not obj["ev"].strip():
                bad += 1
                continue
            data = obj.get("data")
            t_us = obj.get("t_us")
            if isinstance(t_us, bool) or not isinstance(t_us, (int, float)) or not math.isfinite(t_us):
                t_us = None
            scene = obj.get("scene")
            events.append(Event(canon_event_name(obj["ev"]), scene if isinstance(scene, str) else "",
                                None if t_us is None else int(t_us), data if isinstance(data, dict) else {}))
    return events, bad


# --------------------------------------------------------------------------- value coercion
def _num(v: Any) -> float | None:
    if isinstance(v, bool) or not isinstance(v, (int, float)) or not math.isfinite(v):
        return None
    return float(v)


def _bool(v: Any) -> bool | None:
    if isinstance(v, bool):
        return v
    if isinstance(v, (int, float)) and v in (0, 1):
        return bool(v)
    if isinstance(v, str) and v.strip().lower() in ("true", "false"):
        return v.strip().lower() == "true"
    return None


def _text(v: Any) -> str | None:
    if isinstance(v, bool) or v is None:
        return None
    if isinstance(v, (str, int)):
        return str(v)
    return None


def _cell(v: Any) -> str:
    """A CSV cell: '' for None, 1/0 for bool, ints as ints, floats with 12 significant digits."""
    if v is None:
        return ""
    if isinstance(v, bool):
        return "1" if v else "0"
    if isinstance(v, int):
        return str(v)
    if isinstance(v, float):
        if v == int(v) and abs(v) < 1e15:
            return str(int(v))
        return format(v, ".12g")
    return str(v)


def _mean(xs: Sequence[float]) -> float | None:
    return math.fsum(xs) / len(xs) if xs else None


def _last_of(values: Iterable[Any]) -> Any:
    out = None
    for v in values:
        out = v
    return out


# --------------------------------------------------------------------------- the summary
def summarize(events: Sequence[Event], bad_lines: int = 0, fallback_session_id: str = "") -> dict[str, Any]:
    """The values of one lung_summary.csv row, keyed by COLUMNS. None means an empty cell."""
    by: dict[str, list[dict]] = {}
    for e in events:
        by.setdefault(e.name, []).append(e.data)
    row: dict[str, Any] = {c: None for c in COLUMNS}

    sid = _last_of(_text(d.get("sessionId")) for d in by.get("session", []))
    row["session_id"] = sid or fallback_session_id or None
    row["n_events"] = len(events)
    row["n_bad_lines"] = bad_lines
    phone_t = [e.t_us for e in events if e.t_us is not None and e.scene != "hub"]
    duration_s = (max(phone_t) - min(phone_t)) / 1e6 if len(phone_t) >= 2 else None
    row["duration_s"] = duration_s
    row["core_types_present"] = sum(1 for t in CORE_EVENT_TYPES if t in by)

    # M2
    m2 = by.get("m2_place", [])
    row["m2_n"] = len(m2)
    hits = [h for h in (_num(d.get("hit")) for d in m2) if h is not None]
    bests = [b for b in (_num(d.get("best")) for d in m2) if b is not None]
    row["m2_hit_last"] = _last_of(_num(d.get("hit")) for d in m2)
    row["m2_hit_max"] = max(hits) if hits else None
    row["m2_best"] = max(bests) if bests else None

    # M4 (the last gate is the walker's final answer)
    m4 = by.get("m4_gate", [])
    row["m4_n"] = len(m4)
    if m4:
        d = m4[-1]
        row["m4_start"], row["m4_len"], row["m4_duty"] = _num(d.get("start")), _num(d.get("len")), _num(d.get("duty"))
        itv, best = _num(d.get("itv_cm3")), _num(d.get("best_cm3"))
        row["m4_itv_cm3"], row["m4_best_cm3"] = itv, best
        if itv is not None and best is not None:
            row["m4_gap_cm3"] = itv - best
            row["m4_ratio"] = best / itv if itv > 0 else None

    # M3
    m3 = by.get("m3_pick", [])
    row["m3_n"] = len(m3)
    for when in ("before", "after"):
        sel = [d for d in m3 if isinstance(d.get("when"), str) and d["when"].strip().lower() == when]
        row[f"m3_{when}_n"] = len(sel)
        row[f"m3_{when}_correct_n"] = sum(1 for d in sel if _bool(d.get("correct")) is True)

    # fork
    fk = by.get("fork", [])
    row["fork_n"] = len(fk)
    row["fork_correct_n"] = sum(1 for d in fk if _bool(d.get("correct")) is True)
    for key, want in (("beacon", True), ("nobeacon", False)):
        sel = [d for d in fk if _bool(d.get("beacon_shown")) is want]
        row[f"fork_{key}_n"] = len(sel)
        row[f"fork_{key}_correct_n"] = sum(1 for d in sel if _bool(d.get("correct")) is True)
    ts = [t for t in (_num(d.get("t_s")) for d in fk) if t is not None]
    row["fork_t_sum_s"] = math.fsum(ts) if ts else None
    row["fork_t_mean_s"] = _mean(ts)
    row["fork_t_max_s"] = max(ts) if ts else None

    # compass_choice
    cc = by.get("compass_choice", [])
    row["compass_n"] = len(cc)
    if cc:
        row["compass_choice"] = _text(cc[-1].get("choice"))
        row["compass_correct"] = _bool(cc[-1].get("correct"))

    # sky_pick
    sk = by.get("sky_pick", [])
    row["sky_n"] = len(sk)
    if sk:
        d = sk[-1]
        row["sky_az_deg"], row["sky_el_deg"], row["sky_clear_mm"] = _num(d.get("az_deg")), _num(d.get("el_deg")), _num(d.get("clear_mm"))
        row["sky_robust"] = _bool(d.get("robust"))

    # gc_check
    gc = by.get("gc_check", [])
    row["gc_n"] = len(gc)
    row["gc_angle_sum_deg"] = _last_of(_num(d.get("angle_sum_deg")) for d in gc)

    # walked distance, FMS, SEQ
    row["walked_m"] = _last_of(_num(d.get("dist_m")) for d in by.get("walked", []))
    fms = [s for s in (_num(d.get("score")) for d in by.get("fms", [])) if s is not None]
    row["fms_n"] = len(fms)
    row["fms_mean"] = _mean(fms)
    row["fms_min"] = min(fms) if fms else None
    row["fms_max"] = max(fms) if fms else None
    row["fms_last"] = fms[-1] if fms else None
    row["fms_per_min"] = len(fms) / (duration_s / 60.0) if fms and duration_s else None
    row["seq"] = _last_of(_num(d.get("score")) for d in by.get("seq", []))

    # M1 (Extended)
    m1 = by.get("m1_fit", [])
    row["m1_n"] = len(m1)
    if m1:
        c, o = _num(m1[-1].get("C")), _num(m1[-1].get("O"))
        row["m1_C"], row["m1_O"] = c, o
        row["m1_pass"] = None if c is None else c >= M1_PASS_COVERAGE
    return row


def summarize_file(path: str | Path) -> dict[str, Any]:
    p = Path(path)
    events, bad = read_events(p)
    return summarize(events, bad, fallback_session_id=p.resolve().parent.name)


def write_summary_csv(rows: Sequence[dict[str, Any]], out: str | Path) -> None:
    """Write the header and one line per row; ``\\n`` line ends, UTF-8, no BOM, deterministic."""
    out = Path(out)
    if out.parent and not out.parent.exists():
        out.parent.mkdir(parents=True, exist_ok=True)
    with open(out, "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, lineterminator="\n")
        w.writerow(COLUMNS)
        for r in rows:
            w.writerow([_cell(r.get(c)) for c in COLUMNS])


def core_presence(events: Sequence[Event]) -> dict[str, int]:
    """{core type: number of events seen} for the 7 Core types, in plan order."""
    counts = {t: 0 for t in CORE_EVENT_TYPES}
    for e in events:
        if e.name in counts:
            counts[e.name] += 1
    return counts


# --------------------------------------------------------------------------- CLI
def add_pilot_parser(groups) -> None:
    """Attach the ``pilot`` group (``export``, ``check``) to the top-level subparsers."""
    pilot = groups.add_parser("pilot", help="pilot logs: events.jsonl -> lung_summary.csv (P2-D13)",
                              description="Event logging and pilot export for the Lung Mission (Master Plan 10.2.6).")
    sub = pilot.add_subparsers(dest="pilot_cmd", metavar="<command>")
    ex = sub.add_parser("export", help="events.jsonl -> lung_summary.csv, one row per session file",
                        description=(__doc__ or "").split("CLI", 1)[0])
    ex.add_argument("events", nargs="+", type=Path, help="events.jsonl of one session (one row each)")
    ex.add_argument("--out", type=Path, default=Path("lung_summary.csv"), help="CSV to write (default: lung_summary.csv)")
    ex.set_defaults(_pilot_run=_run_export)
    ck = sub.add_parser("check", help="report which of the 7 Core event types are present")
    ck.add_argument("events", type=Path, help="events.jsonl of one session")
    ck.set_defaults(_pilot_run=_run_check)
    pilot.set_defaults(_pilot_parser=pilot)


def _run_export(args) -> int:
    rows = []
    for p in args.events:
        if not Path(p).is_file():
            print(f"atlas pilot export: no such file: {p}", file=sys.stderr)
            return 1
        rows.append(summarize_file(p))
    write_summary_csv(rows, args.out)
    print(f"atlas pilot export: wrote {len(rows)} row(s) to {args.out}")
    return 0


def _run_check(args) -> int:
    if not Path(args.events).is_file():
        print(f"atlas pilot check: no such file: {args.events}", file=sys.stderr)
        return 1
    events, bad = read_events(args.events)
    counts = core_presence(events)
    for t, n in counts.items():
        print(f"  {'present' if n else 'MISSING':8s} {t}  ({n} event{'s' if n != 1 else ''})")
    missing = [t for t, n in counts.items() if not n]
    if missing:
        print(f"atlas pilot check: FAIL, {len(missing)} of 7 Core event types missing: {', '.join(missing)} "
              f"({len(events)} events read, {bad} bad lines)", file=sys.stderr)
        return 1
    print(f"atlas pilot check: OK, all 7 Core event types present ({len(events)} events read, {bad} bad lines)")
    return 0


def run_from_args(args) -> int:
    """Called by cli.main for the ``pilot`` group."""
    if getattr(args, "pilot_cmd", None) is None:
        args._pilot_parser.print_help()
        return 2
    return int(args._pilot_run(args) or 0)
