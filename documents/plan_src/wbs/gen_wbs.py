#!/usr/bin/env python3
"""Validate the ATLAS work breakdown and generate Markdown tables and Mermaid diagrams from it.

Usage: python3 gen_wbs.py <out_dir>
Checks: every dependency exists, the graph is acyclic, no task depends on a later phase.
"""
import re
import sys
from collections import defaultdict, deque
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import wbs_p1  # noqa: E402

OUT = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).parent / "out"
OUT.mkdir(parents=True, exist_ok=True)

LANE_NAME = {
    "X": "All hands",
    "A": "Lane A · Platform, Look and Flat Spaces",
    "B": "Lane B · Kernel and Curved Worlds",
    "C": "Lane C · Scan, Room and Safety",
    "D": "Lane D · Link, Station and Forge",
    "G": "Gates",
}
RUNS = {"E": "Editor", "H": "phone in hand", "V": "phone in headset", "S": "Station", "M": "Mac", "D": "no device"}

SHORT = {  # short labels for diagrams
    "P1-X01": "Build seat, signing, Unity", "P1-X02": "G0 · Contracts frozen",
    "P1-X03": "Station ready", "P1-X04": "Phones and network ready", "P1-X05": "Contract pages, lane A",
    "P1-X06": "Contract pages, lane B", "P1-X07": "Contract pages, lane C", "P1-X08": "Contract pages, lane D",
    "P1-A01": "Project skeleton", "P1-A02": "First build on phones", "P1-A03": "URP settings and tiers",
    "P1-A04": "Stereo rig", "P1-A05": "Lens distortion blit", "P1-A06": "Shared look shader",
    "P1-A07": "Thermal ladder", "P1-A08": "SpaceLab host", "P1-A09": "Glued space S1",
    "P1-A10": "Portal engine", "P1-A11": "Impossible Rooms S5", "P1-A12": "Slicer S6 and Mesh4D",
    "P1-A13": "One-command build", "P1-A14": "Perf runs and soak", "P1-A15": "Room in Glued, standing mode",
    "P1-B01": "Geometry kernel", "P1-B02": "Holonomy rig", "P1-B03": "Warp shader include",
    "P1-B04": "Exact tile ids and graph", "P1-B05": "Tile meshes", "P1-B06": "Tile renderer",
    "P1-B07": "World generator, WorldLint", "P1-B08": "Small Planet S4", "P1-B09": "Dial and 3-4-5 walk",
    "P1-B10": "Aligned Cells S3", "P1-B11": "Room becomes tile zero", "P1-B12": "Room in Aligned Cells",
    "P1-C01": "AR session, simulation", "P1-C02": "Scan route 1 manual", "P1-C03": "Scan route 2 FloorScan",
    "P1-C04": "Object boxes", "P1-C05": "RoomPlan walls (Extended)", "P1-C06": "RoomModel and loop-fit",
    "P1-C07": "Home rebuild S0", "P1-C08": "Object photo capture", "P1-C09": "Bump Guard",
    "P1-C10": "Input and twins", "P1-C11": "Pose pipeline", "P1-C12": "Onboarding in hand",
    "P1-C13": "Pre-scan and relocalise", "P1-C14": "Warning cue and haptics", "P1-C15": "FloorScan core (Editor)",
    "P1-D01": "Atlas.Link library", "P1-D02": "Hub", "P1-D03": "Registry and tuner", "P1-D04": "Perf probe, telemetry",
    "P1-D05": "Spectator", "P1-D06": "Operator Console", "P1-D07": "Flat-twin mini-map", "P1-D08": "Forge service",
    "P1-D09": "Mesh clean-up", "P1-D10": "Asset client", "P1-D11": "Pairing and network",
    "P1-D12": "Built-in fallback set", "P1-D13": "On-device menu",
    "P1-G1": "G1 · First light", "P1-G2": "G2 · Curved-walk spike", "P1-G3": "G3 · Spaces in the Editor",
    "P1-G4": "G4 · Spaces on the phone at budget", "P1-G5": "G5 · Scan, Home, Bump Guard",
    "P1-G6": "G6 · Station link, four functions", "P1-G7": "G7 · Phase-1 exit",
}

CLASSDEF = """  classDef laneA fill:#e3f4ec,stroke:#2f8f6b,color:#12392b;
  classDef laneB fill:#eee7fb,stroke:#7b4fd6,color:#2c1a5c;
  classDef laneC fill:#fdeede,stroke:#d9722b,color:#5a2c0c;
  classDef laneD fill:#e3edfc,stroke:#2b6fd9,color:#11305f;
  classDef laneX fill:#eef1f6,stroke:#7a8499,color:#1d2433;
  classDef gate fill:#1d2433,stroke:#1d2433,color:#ffffff;
  classDef ext fill:#ffffff,stroke:#7a8499,color:#4a5368,stroke-dasharray: 4 3;"""


def nid(tid):
    return tid.replace("P1-", "").replace("-", "")


def topo(tasks):
    indeg = {t: 0 for t in tasks}
    out = defaultdict(list)
    for t, d in tasks.items():
        for p in d["deps"]:
            if p not in tasks:
                raise SystemExit(f"{t}: unknown dependency {p}")
            out[p].append(t)
            indeg[t] += 1
    q = deque(sorted(t for t, n in indeg.items() if n == 0))
    order = []
    while q:
        t = q.popleft()
        order.append(t)
        for c in sorted(out[t]):
            indeg[c] -= 1
            if indeg[c] == 0:
                q.append(c)
    if len(order) != len(tasks):
        raise SystemExit("cycle in the task graph: " + ", ".join(sorted(set(tasks) - set(order))))
    return order, out


def depth(tasks, order):
    d = {}
    for t in order:
        d[t] = 1 + max((d[p] for p in tasks[t]["deps"]), default=0)
    return d


def table(tasks, lane):
    rows = ["| ID | Task | Depends on | Output | Acceptance test | Runs on |", "|---|---|---|---|---|---|"]
    for t in sorted(k for k, v in tasks.items() if v["lane"] == lane):
        v = tasks[t]
        deps = ", ".join(f"`{p}`" for p in v["deps"]) or "none"
        runs = ", ".join(RUNS[c] for c in v["runs"].split())
        rows.append(f"| `{t}` | **{v['title']}** | {deps} | {v['output']} | {v['accept']} | {runs} |")
    return "\n".join(rows)


def node(tid, tasks, ext=False):
    v = tasks[tid]
    lab = f"{nid(tid)} · {SHORT[tid]}" if v["lane"] != "G" and tid != "P1-X02" else SHORT[tid]
    if v["lane"] == "G" or tid == "P1-X02":
        s = f'  {nid(tid)}{{{{"{lab}"}}}}'
    else:
        s = f'  {nid(tid)}["{lab}"]'
    return s


def klass(tid, tasks, ext=False):
    v = tasks[tid]
    if ext:
        return "ext"
    if v["lane"] == "G" or tid == "P1-X02":
        return "gate"
    return "lane" + v["lane"]


def lane_diagram(tasks, lane):
    mine = sorted(k for k, v in tasks.items() if v["lane"] == lane)
    ext_in, ext_out, edges = set(), set(), []
    for t in mine:
        for p in tasks[t]["deps"]:
            edges.append((p, t))
            if tasks[p]["lane"] != lane:
                ext_in.add(p)
    for t, v in tasks.items():
        if v["lane"] != lane:
            for p in v["deps"]:
                if p in mine and v["lane"] == "G":
                    edges.append((p, t))
                    ext_out.add(t)
    lines = ["flowchart LR"]
    for t in sorted(ext_in):
        lines.append(node(t, tasks))
    for t in mine:
        lines.append(node(t, tasks))
    for t in sorted(ext_out):
        lines.append(node(t, tasks))
    for a, b in sorted(set(edges)):
        lines.append(f"  {nid(a)} --> {nid(b)}")
    groups = defaultdict(list)
    for t in mine:
        groups[klass(t, tasks)].append(nid(t))
    for t in ext_in:
        groups["gate" if (tasks[t]["lane"] == "G" or t == "P1-X02") else "ext"].append(nid(t))
    for t in ext_out:
        groups["gate"].append(nid(t))
    lines.append(CLASSDEF)
    for k, ids in sorted(groups.items()):
        lines.append(f"  class {','.join(sorted(set(ids)))} {k};")
    return "\n".join(lines)


import areas_lib  # noqa: E402

AREAS_FILE = Path(__file__).parent / "areas.md"


def load_stages():
    """Phase 1 work areas from the shared areas file: (area id, "Name<br/>what it delivers", lane, tasks)."""
    out, n = [], defaultdict(int)
    for a in areas_lib.load_areas(AREAS_FILE)[1]:
        n[a["lane"]] += 1
        lab = f"{a['name']}<br/>{a['delivers']}".replace('"', "'").replace("`", "")
        out.append((f"{a['lane']}{n[a['lane']]}", lab, a["lane"], a["ids"]))
    order = {"A": 0, "B": 1, "C": 2, "D": 3}
    return sorted(out, key=lambda s: (order[s[2]], int(s[0][1:])))


STAGES = load_stages()
CHAINS = []     # filled from the task data: area a -> area b (same lane) when a task of b depends on a task of a
GATE_LABEL = {"G1": "G1<br/>First light", "G2": "G2<br/>Curved-walk spike", "G3": "G3<br/>Spaces in the Editor",
              "G4": "G4<br/>Spaces on the phone<br/>at budget", "G5": "G5<br/>Scan, Home,<br/>Bump Guard",
              "G6": "G6<br/>Station link,<br/>four functions", "G7": "G7<br/>Phase-1 exit"}


def overview(tasks):
    of = {}
    for sid, _, _, ts in STAGES:
        for t in ts:
            of[t] = sid
    of["P1-X01"] = of["P1-A01"] = of["P1-X03"] = of["P1-X04"] = "S0"
    for k in ("P1-X05", "P1-X06", "P1-X07", "P1-X08"):
        of[k] = "S1"
    of["P1-X02"] = "G0"
    for t, v in tasks.items():
        if v["lane"] == "G":
            of[t] = t.replace("P1-", "")
    missing = set(tasks) - set(of)
    if missing:
        raise SystemExit("tasks not in any stage: " + ", ".join(sorted(missing)))
    lane_of = {sid: lane for sid, _, lane, _ in STAGES}
    gate_edges, hand = set(), defaultdict(list)
    intra, area_edges = set(), set()
    for t, v in tasks.items():
        for p in v["deps"]:
            a, b = of[p], of[t]
            if a == b:
                continue
            if b.startswith("G") and b != "G0":
                gate_edges.add((a, b))
            elif a in lane_of and b in lane_of:
                area_edges.add((a, b))
                if lane_of[a] != lane_of[b]:
                    hand[(a, b)].append((p, t))
                else:
                    intra.add((a, b))
    # the area graph must be acyclic too, or the areas are badly cut
    asucc = defaultdict(set)
    for a, b in area_edges:
        asucc[a].add(b)

    def areach(a, b, skip=None):
        st, seen = [x for x in asucc[a] if (a, x) != skip], set()
        while st:
            x = st.pop()
            if x == b:
                return True
            if x not in seen:
                seen.add(x)
                st.extend(asucc[x])
        return False

    for sid in lane_of:
        if areach(sid, sid):
            raise SystemExit(f"area {sid} is in a cycle of areas")
    # edges inside a lane, transitively reduced
    isucc = defaultdict(set)
    for a, b in intra:
        isucc[a].add(b)

    def ireach(a, b, skip=None):
        st, seen = [x for x in isucc[a] if (a, x) != skip], set()
        while st:
            x = st.pop()
            if x == b:
                return True
            if x not in seen:
                seen.add(x)
                st.extend(isucc[x])
        return False

    CHAINS[:] = sorted(e for e in intra if not ireach(e[0], e[1], e))
    lines = ["flowchart LR", '  S0["Set-up, in parallel<br/>build seat, Station, phones and network<br/>then the project skeleton"] --> G0{{"G0<br/>Contracts frozen"}}',
             '  S1["Contract pages, in parallel<br/>one set per lane, written from this plan"] --> G0']
    for lane in "ABCD":
        lines.append(f'  subgraph L{lane}["{LANE_NAME[lane]}"]')
        lines.append("    direction LR")
        for sid, lab, ln, _ in STAGES:
            if ln == lane:
                lines.append(f'    {sid}["{lab}"]')
        lines.append("  end")
    # how each area starts: from the set-up (S0), from its own contract pages (S1) or from the frozen contracts (G0)
    preds = defaultdict(set)
    for a, b in intra:
        preds[b].add(a)

    def ancestors(sid):
        st, seen = list(preds[sid]), set()
        while st:
            x = st.pop()
            if x not in seen:
                seen.add(x)
                st.extend(preds[x])
        return seen

    s0 = {k for k, v in of.items() if v == "S0"}
    s1 = {k for k, v in of.items() if v == "S1"}
    has_g0, has_s1 = set(), set()
    for sid, _, _, ts in STAGES:
        if any("P1-X02" in tasks[x]["deps"] for x in ts) and not (ancestors(sid) & has_g0):
            lines.append(f"  G0 --> {sid}")
            has_g0.add(sid)
    for sid, _, _, ts in STAGES:
        covered = sid in has_g0 or bool(ancestors(sid) & has_g0)
        deps = {dd for x in ts for dd in tasks[x]["deps"]}
        if deps & s1 and not covered and not (ancestors(sid) & has_s1):
            lines.append(f"  S1 --> {sid}")
            has_s1.add(sid)
        if deps & s0 and not covered and not preds[sid]:
            lines.append(f"  S0 --> {sid}")
    for a, b in CHAINS:
        lines.append(f"  {a} --> {b}")
    for gid, lab in GATE_LABEL.items():
        lines.append(f'  {gid}{{{{"{lab}"}}}}')
    # gate edges: every area a gate needs, except an area that already leads to another needed area of its lane
    def reaches(a, b):
        return ireach(a, b)

    need = defaultdict(set)
    for a, b in gate_edges:
        need[b].add(a)
    for gid in sorted(need, key=lambda g: int(g[1:])):
        for a in sorted(need[gid]):
            if a in ("S0", "S1"):
                continue
            if a in lane_of and any(o != a and o in lane_of and reaches(a, o) for o in need[gid]):
                continue
            lines.append(f"  {a} --> {gid}")
    lines.append(CLASSDEF)
    by = defaultdict(list)
    for sid, _, ln, _ in STAGES:
        by["lane" + ln].append(sid)
    by["laneX"] += ["S0", "S1"]
    by["gate"] += ["G0"] + list(GATE_LABEL)
    for k, ids in sorted(by.items()):
        lines.append(f"  class {','.join(ids)} {k};")
    # ---- area table and area steps (the people-level view)
    name = {sid: lab.split("<br/>")[0] for sid, lab, _, _ in STAGES}
    needs, gives, feeds = defaultdict(set), defaultdict(set), defaultdict(set)
    for (a, b) in hand:
        needs[b].add(a)
        gives[a].add(b)
    for a, b in gate_edges:
        if a in lane_of:
            feeds[a].add(b)
    arows = ["| Area | Owner | Tasks inside | Needs from other lanes | Hands over to | Gates it feeds |", "|---|---|---|---|---|---|"]
    for sid, lab, ln, ts in STAGES:
        what = lab.split("<br/>")[1]
        arows.append(f"| **{sid} · {name[sid]}**<br/>{what} | Lane {ln} | " + ", ".join(f"`{x.replace('P1-', '')}`" for x in sorted(ts)) + " | "
                     + ("; ".join(f"{x} {name[x]}" for x in sorted(needs[sid])) or "nothing") + " | "
                     + ("; ".join(f"{x} {name[x]}" for x in sorted(gives[sid])) or "nothing") + " | "
                     + (", ".join(sorted(feeds[sid], key=lambda g: int(g[1:]))) or "none directly") + " |")
    (OUT / "p1_areas.md").write_text("\n".join(arows) + "\n")
    apred = defaultdict(set)
    for a, b in area_edges:
        apred[b].add(a)
    level = {}

    def lev(sid):
        if sid not in level:
            level[sid] = 1 + max((lev(p) for p in apred[sid]), default=0)
        return level[sid]

    for sid in lane_of:
        lev(sid)
    srows = ["| Step | Lane A | Lane B | Lane C | Lane D |", "|---|---|---|---|---|",
             "| Start | Build seat, signing, Unity (`X01`)<br/>contract pages (`X05`)<br/>then the project skeleton (`A01`) | Contract pages (`X06`) | Phones and network (`X04`)<br/>contract pages (`X07`) | Station (`X03`)<br/>contract pages (`X08`) |"]
    for k in range(1, max(level.values()) + 1):
        cells = []
        for ln in "ABCD":
            cells.append("<br/>".join(f"{sid} {name[sid]}" for sid, _, l2, _ in STAGES if l2 == ln and level[sid] == k))
        srows.append(f"| {k} | " + " | ".join(cells) + " |")
    (OUT / "p1_area_steps.md").write_text("\n".join(srows) + "\n")
    # handover table
    rows = ["| From | To | What is handed over (task → task) |", "|---|---|---|"]
    for (a, b), pairs in sorted(hand.items()):
        what = "; ".join(f"`{p}` {SHORT[p]} → `{t}` {SHORT[t]}" for p, t in sorted(pairs))
        rows.append(f"| {lane_of[a]} · {name[a]} | {lane_of[b]} · {name[b]} | {what} |")
    return "\n".join(lines), "\n".join(rows)


def full(tasks):
    lines = ["flowchart LR"]
    for lane in "XABCD":
        lines.append(f'  subgraph L{lane}["{LANE_NAME[lane]}"]')
        for t in sorted(k for k, v in tasks.items() if v["lane"] == lane):
            lines.append("  " + node(t, tasks))
        lines.append("  end")
    for t in sorted(k for k, v in tasks.items() if v["lane"] == "G"):
        lines.append(node(t, tasks))
    for t in sorted(tasks):
        for p in tasks[t]["deps"]:
            lines.append(f"  {nid(p)} --> {nid(t)}")
    lines.append(CLASSDEF)
    by = defaultdict(list)
    for t in tasks:
        by[klass(t, tasks)].append(nid(t))
    for k, ids in sorted(by.items()):
        lines.append(f"  class {','.join(sorted(ids))} {k};")
    return "\n".join(lines)


def main():
    tasks = wbs_p1.T
    order, out = topo(tasks)
    d = depth(tasks, order)
    crit_end = max(d, key=d.get)
    # one longest chain
    chain, cur = [crit_end], crit_end
    while tasks[cur]["deps"]:
        cur = max(tasks[cur]["deps"], key=lambda p: d[p])
        chain.append(cur)
    chain.reverse()

    for lane in "XABCDG":
        (OUT / f"p1_table_{lane}.md").write_text(table(tasks, lane) + "\n")
    for lane in "ABCD":
        (OUT / f"p1_lane_{lane}.mmd").write_text(lane_diagram(tasks, lane) + "\n")
    ov, hand = overview(tasks)
    (OUT / "p1_overview.mmd").write_text(ov + "\n")
    (OUT / "p1_handovers.md").write_text(hand + "\n")
    (OUT / "p1_full.mmd").write_text(full(tasks) + "\n")

    counts = defaultdict(int)
    dev = defaultdict(int)
    for t, v in tasks.items():
        counts[v["lane"]] += 1
        if "V" in v["runs"].split() or "H" in v["runs"].split():
            dev[v["lane"]] += 1
    nedges = sum(len(v["deps"]) for v in tasks.values())
    rep = [f"tasks: {len(tasks)}  edges: {nedges}  acyclic: yes",
           "per lane: " + ", ".join(f"{k}={counts[k]}" for k in "XABCDG"),
           "need a phone: " + ", ".join(f"{k}={dev[k]}" for k in "XABCDG"),
           "editor-only tasks: " + ", ".join(sorted(t for t, v in tasks.items() if set(v['runs'].split()) <= {'E', 'S', 'M'} and v['lane'] not in 'G')),
           f"longest dependency chain ({len(chain)} steps): " + " → ".join(chain),
           "cross-lane handovers: " + str(hand.count("\n") - 1)]
    # every longest chain (there can be ties)
    longest = max(d.values())
    chains = []

    def walk(cur, acc):
        if not tasks[cur]["deps"]:
            chains.append(list(reversed(acc)))
            return
        for p in sorted(tasks[cur]["deps"]):
            if d[p] == d[cur] - 1:
                walk(p, acc + [p])

    for end in sorted(t for t in tasks if d[t] == longest):
        walk(end, [end])
    rep.append(f"longest chains ({longest} tasks, {len(chains)} ties):")
    rep += ["   " + " → ".join(c) for c in chains]
    # tasks that need neither a phone nor the build seat, and whether any ancestor does
    def needs_device(t):
        return bool(set(tasks[t]["runs"].split()) & {"H", "V", "M"})

    anc_dev = {}
    for t in order:
        anc_dev[t] = any(needs_device(p) or anc_dev[p] for p in tasks[t]["deps"] if p not in ("P1-X01", "P1-A01", "P1-X02"))
    free = sorted(t for t in tasks if not needs_device(t) and tasks[t]["lane"] not in "GX" and t != "P1-A01")
    rep.append("no phone and no build seat: " + ", ".join(free))
    rep.append("  of these, wait for a device task upstream: " + (", ".join(t for t in free if anc_dev[t]) or "none"))
    (OUT / "p1_report.txt").write_text("\n".join(rep) + "\n")
    print("\n".join(rep))
    # the gate lines of the text tree in the plan
    glab = {"P1-G1": "G1 First light", "P1-G2": "G2 Curved-walk spike", "P1-G3": "G3 Spaces in the Editor",
            "P1-G4": "G4 Spaces on the phone", "P1-G5": "G5 Scan, Home, Bump Guard", "P1-G6": "G6 Station link",
            "P1-G7": "G7 Phase-1 exit"}
    tree = []
    for gid in sorted(glab):
        deps = sorted(tasks[gid]["deps"], key=lambda x: (0 if "-G" in x else 1, x))
        tree.append(f"   {glab[gid]} ".ljust(33, ".") + " " + ", ".join(x.replace("P1-", "") for x in deps))
    (OUT / "p1_gates_tree.txt").write_text("\n".join(tree) + "\n")
    # waves: a task's wave is one more than the highest wave among its dependencies
    waves = defaultdict(lambda: defaultdict(list))
    for tid in order:
        lane = tasks[tid]["lane"]
        col = "G" if (lane == "G" or tid == "P1-X02") else lane
        waves[d[tid]][col].append(tid)
    head = "| Wave | Shared | Lane A | Lane B | Lane C | Lane D | Gate reached |"
    wrows = [head, "|---|---|---|---|---|---|---|"]
    for w in sorted(waves):
        cells = []
        for col in "XABCDG":
            items = []
            for tid in sorted(waves[w][col]):
                lab = SHORT[tid] if (col == "G") else f"`{tid.replace('P1-', '')}` {SHORT[tid]}"
                items.append(lab.replace(" · ", " "))
            cells.append("<br/>".join(items) if items else "")
        wrows.append(f"| {w} | " + " | ".join(cells) + " |")
    (OUT / "p1_waves.md").write_text("\n".join(wrows) + "\n")
    # lane rows of the text tree: one work area per entry, its tasks grouped by wave, and what it waits for
    probs = areas_lib.check_cover(areas_lib.load_areas(AREAS_FILE), {k for k, v in tasks.items() if v["lane"] in "ABCD" and k != "P1-A01"}, 1)
    if probs:
        raise SystemExit("areas file: " + "; ".join(probs))
    sh = lambda x: x.replace("P1-", "")
    tree_lanes = []
    for lane in "ABCD":
        tree_lanes.append(f" LANE {lane}  {LANE_NAME[lane].split(' · ', 1)[1]}")
        for sid, lab, ln, ts in STAGES:
            if ln != lane:
                continue
            by_wave = defaultdict(list)
            for x in ts:
                by_wave[d[x]].append(sh(x))
            seq = "  →  ".join(" ‖ ".join(sorted(by_wave[w])) for w in sorted(by_wave))
            ext = sorted({p for x in ts for p in tasks[x]["deps"]
                          if p not in ts and (tasks[p]["lane"] != lane or p == "P1-A01") and p != "P1-X02"})
            groups = defaultdict(list)
            for p in ext:
                kind = "gates" if tasks[p]["lane"] == "G" else ("start" if (tasks[p]["lane"] == "X" or p == "P1-A01") else "lane " + tasks[p]["lane"])
                groups[kind].append(sh(p))
            waits = " · ".join(f"{', '.join(v)} ({k})" for k, v in sorted(groups.items()))
            tree_lanes.append(f"   {sid} {lab.split('<br/>')[0]}")
            tree_lanes.append(f"        {seq}")
            if waits:
                tree_lanes.append(f"        waits for {waits}")
        tree_lanes.append("")
    (OUT / "p1_lanes_tree.txt").write_text("\n".join(tree_lanes).rstrip() + "\n")
    (OUT / "p1_counts.txt").write_text(f"{len(tasks)} {nedges} " + " ".join(str(counts[k]) for k in "XABCDG") + "\n")


if __name__ == "__main__":
    main()
