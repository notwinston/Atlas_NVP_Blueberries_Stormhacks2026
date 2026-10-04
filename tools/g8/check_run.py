#!/usr/bin/env python3
"""Reads the events.jsonl of one Playground run (G8, task P2-C11) and prints what the operator checklist asks.

    python3 tools/g8/check_run.py events.jsonl [--strict]
    python3 tools/g8/check_run.py --selftest

One event per line: {"t_us", "scene", "ev", "data"} (Master Plan Appendix B.2). Checks: the ten pg.beat beats
in order, an fms entry every 60 s +- 5 s, two probe events (S0 then S2), the seq entries, every pg.fmsAlert,
and the walk.* events. It judges only what the log can show; the Spectator and the walker's experience are
on the checklist by eye. Exit code 1 with --strict when a line fails.
"""
import json
import sys

BEATS = ["scan", "home", "headset", "centre", "dial", "walk345", "free", "compare", "menu", "return"]
FMS_INTERVAL, FMS_TOLERANCE = 60.0, 5.0


def load(path):
    out = []
    with open(path, encoding="utf-8") as f:
        for n, line in enumerate(f, 1):
            line = line.strip()
            if not line:
                continue
            try:
                out.append(json.loads(line))
            except ValueError:
                print("line %d: not JSON, skipped" % n)
    return out


def beat_of(e):
    d = e.get("data", {})
    b = d.get("beat", d.get("name"))
    if isinstance(b, int) and 1 <= b <= 10:
        return BEATS[b - 1]
    return b


def check(events):
    rows = []

    def row(name, ok, detail):
        rows.append((name, ok, detail))

    beats = [beat_of(e) for e in events if e.get("ev") == "pg.beat"]
    # the run may be walked again from a beat by the operator: the first ten distinct beats must appear in order
    pos = 0
    for b in beats:
        if pos < 10 and b == BEATS[pos]:
            pos += 1
    row("pg.beat: all ten beats in order", pos == 10, "%d beat events, reached %s" % (len(beats), BEATS[pos - 1] if pos else "none"))

    fms = [e for e in events if e.get("ev") == "fms"]
    gaps = [(b["t_us"] - a["t_us"]) / 1e6 for a, b in zip(fms, fms[1:])]
    bad = [g for g in gaps if abs(g - FMS_INTERVAL) > FMS_TOLERANCE]
    row("fms: entered every 60 s +- 5 s", bool(fms) and not bad,
        "%d entries, gaps %s s" % (len(fms), ", ".join("%.0f" % g for g in gaps) or "-"))
    row("fms: scores 0 to 20", all(0 <= e.get("data", {}).get("score", -1) <= 20 for e in fms), "max %s" % max([e["data"].get("score", 0) for e in fms] or [0]))
    alerts = [e for e in events if e.get("ev") == "pg.fmsAlert"]
    row("pg.fmsAlert: operator alerts", True, "%d alert(s)" % len(alerts))

    probes = [e for e in events if e.get("ev") == "probe"]
    keys = {"ray_az", "pred_geodesic", "pred_euclid", "err_geo", "err_euc"}
    row("probe: two events with all five fields", len(probes) >= 2 and all(keys <= set(p.get("data", {})) for p in probes),
        "%d probe(s), scenes %s" % (len(probes), ", ".join(p.get("scene", "?") for p in probes) or "-"))
    scenes = [p.get("scene") for p in probes]
    row("probe: one in S0 and one in S2", "S0" in scenes and "S2" in scenes, ", ".join(scenes) or "-")

    seq = [e for e in events if e.get("ev") == "seq"]
    row("seq: score 1 to 7 logged", bool(seq) and all(1 <= e.get("data", {}).get("score", 0) <= 7 for e in seq),
        "%d entries: %s" % (len(seq), ", ".join(str(e.get("data", {}).get("score")) for e in seq) or "-"))

    walk = {}
    for e in events:
        if e.get("ev", "").startswith("walk."):
            walk[e["ev"]] = walk.get(e["ev"], 0) + 1
    row("walk.*: corners, laps, closures seen", all(k in walk for k in ("walk.corner", "walk.lap", "walk.closed")),
        ", ".join("%s x%d" % kv for kv in sorted(walk.items())) or "-")
    return rows


def selftest():
    t = lambda s: int(s * 1e6)
    ev = [{"t_us": t(i), "scene": "S0", "ev": "pg.beat", "data": {"beat": i}} for i in range(1, 11)]
    ev += [{"t_us": t(60 * k), "scene": "S0", "ev": "fms", "data": {"score": 2}} for k in range(1, 4)]
    ev += [{"t_us": t(5), "scene": "S0", "ev": "probe", "data": dict.fromkeys(["ray_az", "pred_geodesic", "pred_euclid", "err_geo", "err_euc"], 1.0)},
           {"t_us": t(9), "scene": "S2", "ev": "probe", "data": dict.fromkeys(["ray_az", "pred_geodesic", "pred_euclid", "err_geo", "err_euc"], 1.0)},
           {"t_us": t(11), "scene": "S2", "ev": "seq", "data": {"score": 6}}]
    ev += [{"t_us": t(7), "scene": "S2", "ev": e, "data": {}} for e in ("walk.corner", "walk.lap", "walk.closed")]
    assert all(ok for _, ok, _ in check(ev)), check(ev)
    ev2 = [e for e in ev if not (e["ev"] == "pg.beat" and e["data"]["beat"] == 7)]
    assert not dict((n, ok) for n, ok, _ in check(ev2))["pg.beat: all ten beats in order"]
    ev3 = [dict(e, t_us=e["t_us"] + (30_000_000 if e["ev"] == "fms" and e["t_us"] > t(60) else 0)) for e in ev]
    assert not dict((n, ok) for n, ok, _ in check(ev3))["fms: entered every 60 s +- 5 s"]
    print("selftest ok")


def main(argv):
    if "--selftest" in argv:
        return selftest()
    paths = [a for a in argv if not a.startswith("--")]
    if len(paths) != 1:
        print(__doc__)
        return 2
    rows = check(load(paths[0]))
    for name, ok, detail in rows:
        print("%-44s %-4s %s" % (name, "ok" if ok else "FAIL", detail))
    return 1 if "--strict" in argv and not all(ok for _, ok, _ in rows) else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
