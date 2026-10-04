"""SYNTHETIC TEST DATA ONLY. Not a recording of any person, not a measurement, not a result.

A deterministic generator of one full Lung Mission run as the Hub would write it to ``events.jsonl``
(contract docs/contracts/lane-d-link.md 2.6 / 3.4: ``{"t_us","scene","ev","data"}`` plus ``hub_us``; Hub-made
``fms`` events carry ``"scene":"hub"``). Every number is drawn from ``random.Random(seed)``; the same seed gives
the same bytes. Used by test_p2_d13_pilot.py; the pilot code never imports it.

``make_run(seed)`` returns a ``Run`` whose ``events`` are the dicts as written and whose ``truth`` holds the
values the generator chose, so a test can recompute the CSV without calling the code under test.
"""

from __future__ import annotations

import json
import random
from dataclasses import dataclass, field
from pathlib import Path

SYNTHETIC_NOTICE = "SYNTHETIC TEST DATA"
T0_US = 1_700_000_000_000_000  # arbitrary phone clock origin
HUB_OFFSET_US = 12_345  # Hub receipt is later than the phone's event by a fixed lag


@dataclass
class Run:
    session_id: str
    events: list[dict] = field(default_factory=list)
    truth: dict = field(default_factory=dict)

    def lines(self, drop: tuple[str, ...] = ()) -> list[str]:
        return [json.dumps(e, separators=(",", ":")) for e in self.events if e["ev"] not in drop]


def make_run(seed: int = 7, session_id: str | None = None) -> Run:
    rng = random.Random(seed)
    sid = session_id or f"synthetic-{seed:04d}"
    run = Run(sid)
    t = T0_US

    def emit(ev: str, data: dict, scene: str = "lung", dt_s: float = 5.0, hub_clock: bool = False) -> None:
        nonlocal t
        if not hub_clock:
            t += int(dt_s * 1_000_000)
            run.events.append({"t_us": t, "scene": scene, "ev": ev, "data": data, "hub_us": t + HUB_OFFSET_US})
        else:  # Hub-made: Hub clock in t_us, never sent to the phone
            run.events.append({"t_us": t + HUB_OFFSET_US + int(dt_s * 1_000_000), "scene": "hub", "ev": ev, "data": data})

    tr: dict = {}
    emit("boot", {"build": "synthetic", "device": "synthetic", "os": "synthetic"}, scene="none", dt_s=0)
    emit("session", {"sessionId": sid}, scene="none", dt_s=0.1)
    emit("space.entered", {"from": "S0", "to": "lung"}, scene="none", dt_s=1)
    for beat in "abcdefgh":  # P2-A32: one lung.beat per beat; unknown to the exporter
        emit("lung.beat", {"beat": beat}, dt_s=10)
        if beat == "c":
            emit("param.set", {"src": "link", "values": {"lung.phase": rng.randint(0, 9)}}, dt_s=1)
        if beat == "d":  # M2 (two placements) and M4
            tr["m2"] = []
            best = round(rng.uniform(0.9, 1.0), 4)
            for _ in range(2):
                hit = round(rng.uniform(0.1, best), 4)
                p = [round(rng.uniform(-20, 20), 2) for _ in range(3)]
                emit("m2_place", {"p_mm": p, "hit": hit, "best": best}, dt_s=8)
                tr["m2"].append({"hit": hit, "best": best})
            start, ln = rng.randint(0, 9), rng.randint(3, 6)
            best_cm3 = round(rng.uniform(5, 15), 3)
            itv = round(best_cm3 + rng.uniform(0, 10), 3)
            emit("m4_gate", {"start": start, "len": ln, "itv_cm3": itv, "duty": ln / 10, "best_cm3": best_cm3}, dt_s=9)
            tr["m4"] = {"start": start, "len": ln, "itv_cm3": itv, "duty": ln / 10, "best_cm3": best_cm3}
        if beat == "e":  # M3 before beat f ...
            tr["m3"] = []
            for when, ok in (("before", rng.random() < 0.5), ("after", True)):
                emit("m3_pick", {"choice": rng.randint(0, 2), "correct": ok, "when": when}, dt_s=6)
                tr["m3"].append({"when": when, "correct": ok})
            emit("m1_fit", {"radii_mm": [12.0, 11.5, 14.2], "C": 0.97, "O": 1.4}, dt_s=4)  # Extended tier
            tr["m1"] = {"C": 0.97, "O": 1.4}
        if beat == "g":  # airway: four forks, one with the Two Compasses
            tr["fork"] = []
            for i in range(4):
                beacon = i != 1
                ok = rng.random() < 0.75
                t_s = round(rng.uniform(4, 25), 2)
                emit("fork", {"address": "0" * i + "1", "choice": rng.randint(0, 1), "correct": ok,
                              "beacon_shown": beacon, "t_s": t_s}, dt_s=t_s)
                tr["fork"].append({"correct": ok, "beacon_shown": beacon, "t_s": t_s})
                if i == 2:
                    cc = {"choice": "beacon", "correct": True}
                    emit("compass_choice", cc, dt_s=3)
                    tr["compass"] = cc
        if beat == "h":  # lung end: sky, great-circle check
            sky = {"az_deg": round(rng.uniform(0, 360), 1), "el_deg": round(rng.uniform(-90, 90), 1),
                   "clear_mm": round(rng.uniform(2, 30), 2), "robust": rng.random() < 0.6}
            emit("sky_pick", sky, dt_s=7)
            tr["sky"] = sky
            gc = {"angle_sum_deg": round(rng.uniform(300, 420), 2)}
            emit("gc_check", gc, dt_s=7)
            tr["gc"] = gc
        # FMS each minute, written by the Hub
        tr.setdefault("fms", [])
        score = rng.randint(0, 6)
        emit("fms", {"score": score}, hub_clock=True, dt_s=0)
        tr["fms"].append(score)
    walked = round(rng.uniform(40, 120), 2)
    emit("walked", {"dist_m": walked}, dt_s=2)
    tr["walked_m"] = walked
    seq = rng.randint(1, 7)
    emit("seq", {"score": seq}, dt_s=5)
    tr["seq"] = seq
    emit("zz.unknownFutureEvent", {"x": 1}, dt_s=0.5)  # an event type the exporter does not know
    tr["duration_s"] = (max(e["t_us"] for e in run.events if e["scene"] != "hub")
                        - min(e["t_us"] for e in run.events if e["scene"] != "hub")) / 1e6
    run.truth = tr
    return run


def write_run(path: Path, run: Run, drop: tuple[str, ...] = (), extra_lines: tuple[str, ...] = ()) -> Path:
    """Write the run's events.jsonl (``\\n`` line ends); ``drop`` removes event types, ``extra_lines`` appends raw lines."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("".join(l + "\n" for l in run.lines(drop) + list(extra_lines)), encoding="utf-8", newline="")
    return path
