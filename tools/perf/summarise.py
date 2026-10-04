#!/usr/bin/env python3
"""Turns the files of a performance run into the metrics table of Master Plan section 11.2, as Markdown.

  summarise.py FILE [FILE ...] [--out docs/evidence/G4_metrics.md] [--strict]
  summarise.py --empty

FILE is any of the three kinds a run leaves behind; the kind is read from the content, not from the name:

  tour_<session>.csv     the tour's own file on the phone: one row per space, per soak or per forced tier
  events_<session>.jsonl the phone's event log, or the Hub's events.jsonl: the same rows as perf.summary events
  perf_<session>.csv     the probe's one row per second (on the phone or from the Hub); used only when no
                         summary row exists for that file, and then one column per stretch of one space

Every verdict is computed here again from the numbers against the "must" column, and compared with the one
the phone wrote. --strict exits 1 when a row fails. --empty prints the table with no values, for a new page.
Needs Python 3.8 or newer and nothing else.
"""
import argparse
import csv
import io
import json
import sys

MAGIC = "# atlas perf tour v1"
HEADER = ("run,kind,label,space,variant,room,pose,depth,seconds,frames,p50,p95,p99,max,over17_2,over33_8,"
          "fpsMean,fpsMin,drawCalls,setPass,tris,cpuMainP95,cpuRenderP95,gpuP95,memMB,gcAllocB,gcAllocMaxB,"
          "thermalMax,tFairS,tSeriousS,tCriticalS,tier,effectMs,result,failed,unmeasured").split(",")
TEXT_COLUMNS = {"run", "kind", "label", "space", "variant", "room", "pose", "depth", "result", "failed", "unmeasured"}
PERF_HEADER_START = "t_us,seq,sceneId,spaceId"

# The "must" column of section 11.2, and the soak's rule of section 11.4.1.
MUST = {
    "p50": 16.7, "p95": 20.0, "p99": 33.3, "over17_2": 5.0, "over33_8": 0.5,
    "drawCalls": 300, "setPass": 60, "tris": 500000,
    "cpuMainP95": 8.0, "cpuRenderP95": 6.0, "gpuCold": 9.0, "gpuHot": 14.0,
    "memMB": 1536, "gcAllocB": 1024,
}
SOAK_FPS_MEAN, SOAK_FPS_MIN, SOAK_SERIOUS_NOT_BEFORE, SOAK_SECONDS = 45.0, 30.0, 600.0, 900.0
TIER_EFFECT_MS = 1000.0
THERMAL = ["Nominal", "Fair", "Serious", "Critical"]
SPACES = ["S0 Home", "S1 Glued", "S2 Curved Walk", "S3 Aligned Cells", "S4 Small Planet", "S5 Impossible Rooms", "S6 Slicer"]
PLAN_COLUMNS = ["S0 Home", "S1 Glued 5x5", "S1 Glued 3x3x3", "S2 Curved Walk", "S3 Aligned Cells", "S4 Small Planet",
                "S5 Impossible Rooms", "S6 Slicer"]


class Run:
    def __init__(self, source):
        self.source = source
        self.facts = {}
        self.notes = []
        self.thermal = []       # (seconds, state)
        self.rows = []
        self.result = ""
        self.aborted = ""
        self.raw = False        # made from the probe's rows, with no summary row

    @property
    def kind(self):
        if self.facts.get("run"):
            return self.facts["run"]
        return self.rows[0]["run"] if self.rows else ""


def typed(row):
    out = {}
    for name in HEADER:
        value = row.get(name, "")
        if name in TEXT_COLUMNS:
            out[name] = "" if value is None else str(value)
        else:
            try:
                out[name] = float(value)
            except (TypeError, ValueError):
                out[name] = 0.0
    return out


def read_tour(text, source):
    run = Run(source)
    header = None
    for line in text.splitlines():
        if not line.strip():
            continue
        if line.startswith("#"):
            body = line[1:].strip()
            if "=" not in body:
                continue
            key, value = body.split("=", 1)
            if key == "note":
                run.notes.append(value)
            elif key == "thermal":
                parts = value.split()
                if len(parts) == 2:
                    run.thermal.append((float(parts[0]), int(parts[1])))
            elif key == "result":
                run.result = value
            elif key == "aborted":
                run.aborted = value
            else:
                run.facts[key] = value
            continue
        cells = line.split(",")
        if header is None:
            header = cells
            missing = [c for c in HEADER if c not in header]
            if missing:
                raise ValueError("%s: columns missing from the header: %s" % (source, ", ".join(missing)))
            continue
        if len(cells) != len(header):
            raise ValueError("%s: a row has %d cells, the header %d" % (source, len(cells), len(header)))
        run.rows.append(typed(dict(zip(header, cells))))
    return [run]


def read_events(text, source):
    runs, run = [], None
    for line in text.splitlines():
        line = line.strip()
        if not line.startswith("{"):
            continue
        try:
            event = json.loads(line)
        except ValueError:
            continue
        name, data = event.get("ev", ""), event.get("data") or {}
        if not isinstance(data, dict):
            continue
        if name == "perf.runBegin":
            run = Run(source)
            run.facts["run"] = data.get("run", "")
            if "plannedSeconds" in data:
                run.facts["plannedSeconds"] = data["plannedSeconds"]
            runs.append(run)
        elif name == "perf.note" and run is not None:
            run.notes.append(data.get("text", ""))
        elif name == "perf.summary":
            if run is None:
                run = Run(source)
                runs.append(run)
            row = typed(data)
            for key in ("room", "pose", "depth"):
                run.facts.setdefault(key, row[key])
            run.rows.append(row)
        elif name == "perf.runEnd" and run is not None:
            run.result = data.get("result", "")
            if run.result == "aborted":
                run.aborted = "see the phone's file"
            run = None
    return runs


def read_perf(text, source):
    """The probe's rows with no summary: one column per stretch of one space, from the last row of the stretch."""
    run = Run(source)
    run.raw = True
    run.facts["run"] = "tour"
    run.notes.append("made from the probe's rows: no settle time was left out, and the stretches are those of the session")
    reader = csv.DictReader(io.StringIO(text))
    stretch = []

    def close():
        if len(stretch) < 2:
            return
        last = stretch[-1]
        space = int(float(last["spaceId"]))
        if space < 0 or space >= len(SPACES):
            return
        copies = int(float(last.get("copies", 0) or 0))
        label = SPACES[space] + (" %d copies" % copies if space == 1 and copies else "")
        number = lambda name: [float(r.get(name, 0) or 0) for r in stretch[1:]]
        row = typed({
            "run": "tour", "kind": "space", "label": label, "space": "S%d" % space,
            "seconds": len(stretch) - 1, "frames": sum(number("fps")),
            "p50": last["p50"], "p95": last["p95"], "p99": last["p99"], "max": max(number("max")),
            "over17_2": last["over17_2"], "over33_8": last["over33_8"],
            "fpsMean": sum(number("fps")) / len(number("fps")), "fpsMin": min(number("fps")),
            "drawCalls": max(number("drawCalls")), "setPass": max(number("setPass")), "tris": 1000 * max(number("trisK")),
            "cpuMainP95": last["cpuMainP95"], "cpuRenderP95": last["cpuRenderP95"], "gpuP95": last["gpuP95"],
            "memMB": max(number("memMB")), "gcAllocB": sum(number("gcAllocB")) / len(number("gcAllocB")),
            "gcAllocMaxB": max(number("gcAllocB")), "thermalMax": max(number("thermal")),
            "tFairS": -1, "tSeriousS": -1, "tCriticalS": -1, "tier": -1, "effectMs": -1,
        })
        run.rows.append(row)

    for record in reader:
        if stretch and record.get("spaceId") != stretch[-1].get("spaceId"):
            close()
            stretch = []
        stretch.append(record)
    close()
    return [run] if run.rows else []


def read_file(path):
    with open(path, encoding="utf-8") as handle:
        text = handle.read()
    first = text.lstrip().split("\n", 1)[0].strip()
    if first.startswith(MAGIC) or first.startswith("run,kind,"):
        return read_tour(text, path)
    if first.startswith(PERF_HEADER_START):
        return read_perf(text, path)
    if first.startswith("{"):
        return read_events(text, path)
    raise ValueError("%s: not a tour file, an event log or a perf CSV" % path)


# ---- verdicts ---------------------------------------------------------------------------------------------

def judge_space(row, cold):
    """The must column for one space. Returns (failed, unmeasured)."""
    failed, unmeasured = [], []
    if row["frames"] <= 0:
        return ["noFrames"], []
    hot = (not cold) and row["thermalMax"] >= 1
    checks = [
        ("p50", row["p50"], MUST["p50"]), ("p95", row["p95"], MUST["p95"]), ("p99", row["p99"], MUST["p99"]),
        ("over17_2", row["over17_2"], MUST["over17_2"]), ("over33_8", row["over33_8"], MUST["over33_8"]),
        ("drawCalls", row["drawCalls"], MUST["drawCalls"]), ("setPass", row["setPass"], MUST["setPass"]),
        ("tris", row["tris"], MUST["tris"]),
        ("cpuMain", row["cpuMainP95"], MUST["cpuMainP95"]), ("cpuRender", row["cpuRenderP95"], MUST["cpuRenderP95"]),
        ("gpu", row["gpuP95"], MUST["gpuHot"] if hot else MUST["gpuCold"]),
        ("mem", row["memMB"], MUST["memMB"]), ("gcAlloc", row["gcAllocB"], MUST["gcAllocB"]),
    ]
    for name, value, limit in checks:
        # The file carries three decimals; a hair over the limit from rounding is not a failure.
        if value > limit + 5e-4:
            failed.append(name)
    if cold and row["thermalMax"] >= 1:
        failed.append("notCold")
    for name, value in (("cpuMain", row["cpuMainP95"]), ("cpuRender", row["cpuRenderP95"]), ("gpu", row["gpuP95"]),
                        ("drawCalls", row["drawCalls"]), ("setPass", row["setPass"]), ("tris", row["tris"]),
                        ("mem", row["memMB"])):
        if value <= 0:
            unmeasured.append(name)
    return failed, unmeasured


def judge_soak(row, planned):
    failed, unmeasured = [], []
    if row["frames"] <= 0:
        failed.append("noFrames")
    if row["tCriticalS"] >= 0 or row["thermalMax"] >= 3:
        failed.append("critical")
    if 0 <= row["tSeriousS"] < SOAK_SERIOUS_NOT_BEFORE:
        failed.append("seriousEarly")
    if row["frames"] > 0 and row["fpsMean"] < SOAK_FPS_MEAN:
        failed.append("fpsMean")
    if row["frames"] > 0 and row["fpsMin"] < SOAK_FPS_MIN:
        failed.append("fpsMin")
    if planned < SOAK_SECONDS - 0.5:
        unmeasured.append("short")
    return failed, unmeasured


def judge_tier(row):
    if row["effectMs"] < 0:
        return ["noEffect"], []
    if row["effectMs"] > TIER_EFFECT_MS:
        return ["slow"], []
    return [], []


def verdict(failed, unmeasured):
    return "fail" if failed else ("incomplete" if unmeasured else "pass")


def judge(run, row):
    if row["kind"] == "ladder":
        return judge_tier(row)
    if row["kind"] == "total":
        if run.aborted:
            planned = 0.0
        elif "plannedSeconds" in run.facts:
            planned = float(run.facts["plannedSeconds"] or 0)
        else:
            # The length is not in this file: the phone's own word on it stands.
            planned = 0.0 if "short" in row["unmeasured"].split("|") else SOAK_SECONDS
        return judge_soak(row, planned)
    failed, unmeasured = judge_space(row, run.kind == "tour")
    # What only the phone can know (the space was never entered, or was left) is taken from its verdict.
    for extra in ("notEntered", "leftSpace"):
        if extra in row["failed"].split("|"):
            failed.append(extra)
    return failed, unmeasured


# ---- Markdown ---------------------------------------------------------------------------------------------

def num(value, digits=2):
    text = ("%." + str(digits) + "f") % value
    return text


def cell(value, text, name, failed, unmeasured):
    if name in unmeasured:
        return "n/a"
    return "**%s**" % text if name in failed else text


def metrics_table(columns, rows_by_label, judged, cold=True):
    """The table of section 11.2: a row per metric, a column per space. cold is the per-space run."""
    def line(cells):
        return "| " + " | ".join(cells) + " |"

    out = [line(["Metric", "Must"] + columns), line(["---", "---"] + ["---"] * len(columns))]

    def metric(title, must, render):
        cells = []
        for label in columns:
            row = rows_by_label.get(label)
            cells.append("" if row is None else render(row, *judged[label]))
        out.append(line([title, must] + cells))

    def triple(row, failed, unmeasured):
        return " / ".join(cell(row[n], num(row[n]), n, failed, unmeasured) for n in ("p50", "p95", "p99"))

    def over(row, failed, unmeasured):
        return " / ".join(cell(row[n], num(row[n]), n, failed, unmeasured) for n in ("over17_2", "over33_8"))

    def cpu(row, failed, unmeasured):
        return (cell(row["cpuMainP95"], num(row["cpuMainP95"]), "cpuMain", failed, unmeasured) + " / " +
                cell(row["cpuRenderP95"], num(row["cpuRenderP95"]), "cpuRender", failed, unmeasured))

    def whole(column, name):
        return lambda row, failed, unmeasured: cell(row[column], "%d" % round(row[column]), name, failed, unmeasured)

    def thermal(row, failed, unmeasured):
        state = THERMAL[min(3, max(0, int(row["thermalMax"])))]
        return "**%s**" % state if "notCold" in failed else state

    def result(row, failed, unmeasured):
        if failed:
            return "**FAIL** (" + ", ".join(failed) + ")"
        return "incomplete" if unmeasured else "pass"

    metric("Frame time p50 / p95 / p99 (ms)", "≤ 16.7 / 20 / 33.3", triple)
    metric("Frames over 17.2 / 33.8 ms (%)", "≤ 5 / 0.5", over)
    metric("Draw calls, both eyes", "≤ 300", whole("drawCalls", "drawCalls"))
    metric("Set-pass calls", "≤ 60", whole("setPass", "setPass"))
    metric("Triangles, both eyes", "≤ 500k", whole("tris", "tris"))
    metric("CPU main / render p95 (ms)", "≤ 8 / 6", cpu)
    metric("GPU p95, cold (ms)" if cold else "GPU p95, cold / hot (ms)", "≤ 9" if cold else "≤ 9 / 14",
           lambda r, f, u: cell(r["gpuP95"], num(r["gpuP95"]), "gpu", f, u))
    metric("Resident memory (MB)", "≤ 1536 (1.5 GB)", whole("memMB", "mem"))
    metric("Managed allocation per frame (B)", "≤ 1024", whole("gcAllocB", "gcAlloc"))
    metric("Thermal state, highest", "Nominal (cold)" if cold else "see the soak's rule", thermal)
    metric("Frames measured", "", lambda r, f, u: "%d" % r["frames"])
    metric("Must column", "", result)
    return out


def seconds_text(value):
    if value < 0:
        return "not reached"
    return "%d s (%d min %02d s)" % (round(value), int(value) // 60, int(value) % 60)


def describe(run):
    facts = run.facts
    parts = []
    for key in ("session", "device", "build", "room", "pose", "depth"):
        if facts.get(key):
            parts.append("%s `%s`" % (key, facts[key]))
    parts.append("file `%s`" % run.source)
    return ", ".join(parts)


def render_run(run, out, problems):
    judged = {}
    for row in run.rows:
        failed, unmeasured = judge(run, row)
        judged[id(row)] = (failed, unmeasured)
        mine = verdict(failed, unmeasured)
        if row["result"] and row["result"] != mine:
            problems.append("%s, %s: the phone wrote %s, the numbers say %s" % (run.source, row["label"], row["result"], mine))
        if failed:
            problems.append("%s, %s: FAIL (%s)" % (run.source, row["label"], ", ".join(failed)))

    spaces = [r for r in run.rows if r["kind"] == "space"]
    totals = [r for r in run.rows if r["kind"] == "total"]
    tiers = [r for r in run.rows if r["kind"] == "ladder"]
    kind = run.kind

    title = {"tour": "Per-space run", "soak": "Soak, LiDAR depth on", "soakNoDepth": "Soak, LiDAR depth off",
             "ladder": "Ladder check"}.get(kind, "Run")
    out.append("## %s" % title)
    out.append("")
    out.append(describe(run) + ".")
    if run.aborted:
        out.append("")
        out.append("**This run ended early** (%s): its numbers are not evidence." % run.aborted)
    if run.notes:
        out.append("")
        out.extend("- " + note for note in run.notes)
    out.append("")

    if spaces:
        columns = [r["label"] for r in spaces]
        by_label = dict((r["label"], r) for r in spaces)
        by_judged = dict((r["label"], judged[id(r)]) for r in spaces)
        out.extend(metrics_table(columns, by_label, by_judged, kind == "tour"))
        if kind != "tour":
            out.append("")
            out.append("In a soak the GPU row is judged against the hot limit once the phone has left Nominal.")
        out.append("")

    for row in totals:
        failed, unmeasured = judged[id(row)]
        out.append("| Soak | Must | Measured |")
        out.append("| --- | --- | --- |")
        out.append("| Thermal timeline | No Critical; Serious not before 10 min | highest %s |" % THERMAL[min(3, int(row["thermalMax"]))])
        out.append("| Time-to-Fair | reported | %s |" % seconds_text(row["tFairS"]))
        out.append("| Time-to-Serious | not before 600 s | %s |" % cell(row["tSeriousS"], seconds_text(row["tSeriousS"]), "seriousEarly", failed, []))
        out.append("| Time-to-Critical | never | %s |" % cell(row["tCriticalS"], seconds_text(row["tCriticalS"]), "critical", failed, []))
        out.append("| Frame rate, mean | ≥ 45 | %s |" % cell(row["fpsMean"], num(row["fpsMean"], 1), "fpsMean", failed, []))
        out.append("| Frame rate, lowest second | ≥ 30 | %s |" % cell(row["fpsMin"], num(row["fpsMin"], 1), "fpsMin", failed, []))
        out.append("| Measured | 900 s | %d s, %d frames |" % (round(row["seconds"]), row["frames"]))
        result = "**FAIL** (" + ", ".join(failed) + ")" if failed else ("incomplete (" + ", ".join(unmeasured) + ")" if unmeasured else "pass")
        out.append("| Result | | %s |" % result)
        out.append("")
        if run.thermal:
            out.append("Thermal changes: " + ", ".join("%s at %d s" % (THERMAL[min(3, s)], round(t)) for t, s in run.thermal) + ".")
            out.append("")

    if tiers:
        out.append("| Forced tier | Must | Time until in force (ms) | Result |")
        out.append("| --- | --- | --- | --- |")
        for row in tiers:
            failed, _ = judged[id(row)]
            took = "never" if row["effectMs"] < 0 else num(row["effectMs"], 1)
            out.append("| %d %s | ≤ 1000 ms | %s | %s |" % (row["tier"], row["label"], "**%s**" % took if failed else took,
                                                           "**FAIL**" if failed else "pass"))
        out.append("")


def render_depth_difference(runs, out):
    on = [r for r in runs if r.kind == "soak" and not r.aborted]
    off = [r for r in runs if r.kind == "soakNoDepth" and not r.aborted]

    def fair(run):
        totals = [r for r in run.rows if r["kind"] == "total"]
        return totals[-1]["tFairS"] if totals else None

    if not on or not off:
        return
    a, b = fair(on[-1]), fair(off[-1])
    if a is None or b is None:
        return
    out.append("## Time-to-Fair, LiDAR depth on against off")
    out.append("")
    out.append("| | Depth on | Depth off | Difference |")
    out.append("| --- | --- | --- | --- |")
    if a >= 0 and b >= 0:
        difference = "%+d s (off minus on)" % round(b - a)
    elif a < 0 and b < 0:
        difference = "neither run reached Fair"
    elif a < 0:
        difference = "only the depth-off run reached Fair"
    else:
        difference = "only the depth-on run reached Fair"
    out.append("| Time-to-Fair | %s | %s | %s |" % (seconds_text(a), seconds_text(b), difference))
    out.append("")


def empty_table():
    judged = dict((label, ([], [])) for label in PLAN_COLUMNS)
    lines = metrics_table(PLAN_COLUMNS, {}, judged)
    return "\n".join(lines) + "\n"


def summarise(paths):
    runs, problems = [], []
    for path in paths:
        runs.extend(read_file(path))
    # A summary row beats the probe's rows, and a run given twice (the phone's file and its events) counts once.
    if any(r.rows and not r.raw for r in runs):
        runs = [r for r in runs if not r.raw]
    seen, unique = set(), []
    for run in runs:
        signature = (run.kind, tuple((r["label"], r["frames"], r["p95"], r["effectMs"]) for r in run.rows))
        if run.rows and signature in seen:
            continue
        seen.add(signature)
        unique.append(run)
    runs = unique
    out = ["# ATLAS performance evidence", ""]
    out.append("Made by `tools/perf/summarise.py` from %d file%s. A value in bold is over its \"must\" limit of "
               "Master Plan section 11.2; n/a is a value the platform did not report (the Editor has no thread times)."
               % (len(paths), "" if len(paths) == 1 else "s"))
    out.append("")
    order = {"tour": 0, "soak": 1, "soakNoDepth": 2, "ladder": 3}
    shown = [r for r in runs if r.rows or r.aborted]
    for run in sorted(shown, key=lambda r: order.get(r.kind, 9)):
        render_run(run, out, problems)
    render_depth_difference(shown, out)
    if not shown:
        out.append("No performance run was found in these files.")
        out.append("")
    if problems:
        out.append("## To look at")
        out.append("")
        for problem in problems:
            out.append("- " + problem)
        out.append("")
    failed = any("FAIL" in p for p in problems)
    return "\n".join(out), failed


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("files", nargs="*", help="tour_*.csv, events*.jsonl or perf*.csv")
    parser.add_argument("--out", help="write the Markdown here instead of printing it")
    parser.add_argument("--strict", action="store_true", help="exit 1 when a row fails its must value")
    parser.add_argument("--empty", action="store_true", help="print the empty metrics table and stop")
    args = parser.parse_args(argv)

    if args.empty:
        sys.stdout.write(empty_table())
        return 0
    if not args.files:
        parser.error("give at least one file, or --empty")
    try:
        text, failed = summarise(args.files)
    except (OSError, ValueError) as error:
        sys.stderr.write("summarise.py: %s\n" % error)
        return 2
    if args.out:
        with open(args.out, "w", encoding="utf-8") as handle:
            handle.write(text)
        sys.stdout.write("wrote %s\n" % args.out)
    else:
        sys.stdout.write(text)
    return 1 if failed and args.strict else 0


if __name__ == "__main__":
    sys.exit(main())
