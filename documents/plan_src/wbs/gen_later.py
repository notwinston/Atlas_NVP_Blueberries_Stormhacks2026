#!/usr/bin/env python3
"""Build the Phase 2-4 task graph from the work-package tables in the drafted sections,
check it, and write Mermaid diagrams.   Usage: python3 gen_later.py <clean_dir> <out_dir>
"""
import re
import sys
from collections import defaultdict, deque
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import areas_lib  # noqa: E402

AREAS = areas_lib.load_areas(Path(__file__).parent / "areas.md")

CLEAN = Path(sys.argv[1])
OUT = Path(sys.argv[2])
OUT.mkdir(parents=True, exist_ok=True)

CLASSDEF = """  classDef laneA fill:#e3f4ec,stroke:#2f8f6b,color:#12392b;
  classDef laneB fill:#eee7fb,stroke:#7b4fd6,color:#2c1a5c;
  classDef laneC fill:#fdeede,stroke:#d9722b,color:#5a2c0c;
  classDef laneD fill:#e3edfc,stroke:#2b6fd9,color:#11305f;
  classDef gate fill:#1d2433,stroke:#1d2433,color:#ffffff;
  classDef ext fill:#ffffff,stroke:#7a8499,color:#4a5368,stroke-dasharray: 4 3;"""

GATES = {
    "G7": "G7 · Phase-1 exit", "G8": "G8 · Playground run", "G9": "G9 · Lung 4D", "G10": "G10 · Lung airway and sky",
    "G11": "G11 · Phase-2 exit", "G12": "G12 · Shared House", "G13": "G13 · FieldFit",
    "G14": "G14 · Feature freeze", "G15": "G15 · Golden build and submission",
}
FILES = {"playground": "10a_playground.md", "lung": "10b_lung.md", "p3": "10c_house_fieldfit.md", "p4": "13_demo_docs.md"}
ID = re.compile(r"P\d-[A-D]\d\d")


def parse(fname, track, split_at=None):
    """Rows of the work-package tables. With split_at, rows after the first line that starts with it get track + '2'."""
    tasks = {}
    second = False
    for line in (CLEAN / fname).read_text().split("\n"):
        if split_at and line.startswith(split_at):
            second = True
        if not re.match(r"\|\s*P\d-[A-D]\d\d\s*\|", line):
            continue
        c = [x.strip() for x in line.strip().strip("|").split("|")]
        tasks[c[0]] = dict(id=c[0], title=c[1].replace("`", "").replace('"', "'"), lane=c[2], raw=c[3],
                           track=(track + "2") if second else track)
    return tasks


def expand(raw, me, tasks):
    deps = set()
    # ranges like P3-A02…A06
    for m in re.finditer(r"(P\d-)([A-D])(\d\d)…([A-D])?(\d\d)", raw):
        for k in range(int(m.group(3)), int(m.group(5)) + 1):
            deps.add(f"{m.group(1)}{m.group(2)}{k:02d}")
    deps.update(ID.findall(raw))
    low = raw.lower()
    same = [t for t, v in tasks.items() if v["track"] == tasks[me]["track"] and t != me]
    if "all above" in low:
        deps.update(same)
    if "all core packages" in low:
        deps.update(t for t in same if "(Extended)" not in tasks[t]["title"] and "(Stretch)" not in tasks[t]["title"])
    deps.discard(me)
    return sorted(d for d in deps if d in tasks)


def reduce_edges(edges):
    succ = defaultdict(set)
    for a, b in edges:
        succ[a].add(b)

    def reach(a, b, skip):
        seen, st = set(), [x for x in succ[a] if (a, x) != skip]
        while st:
            x = st.pop()
            if x == b:
                return True
            if x not in seen:
                seen.add(x)
                st.extend(succ[x])
        return False

    return sorted(e for e in edges if not reach(e[0], e[1], e))


def check_acyclic(nodes, edges):
    indeg = {n: 0 for n in nodes}
    out = defaultdict(list)
    for a, b in edges:
        out[a].append(b)
        indeg[b] += 1
    q = deque(n for n, d in indeg.items() if d == 0)
    seen = 0
    while q:
        n = q.popleft()
        seen += 1
        for c in out[n]:
            indeg[c] -= 1
            if indeg[c] == 0:
                q.append(c)
    if seen != len(nodes):
        raise SystemExit("cycle among: " + ", ".join(n for n, d in indeg.items() if d > 0))


def short(tid):
    return tid.split("-", 1)[1]


def diagram(title_groups, tasks, edges, gates_used, ext=()):
    """title_groups: list of (subgraph label, [task ids]); ext: task ids drawn dashed, outside the subgraphs."""
    lines = ["flowchart LR"]
    for t in ext:
        lines.append(f'  {short(t)}["{short(t)} · {tasks[t]["title"]}"]')
    for i, (lab, ids) in enumerate(title_groups):
        lines.append(f'  subgraph SG{i}["{lab}"]')
        for t in ids:
            lines.append(f'    {short(t)}["{short(t)} · {tasks[t]["title"]}"]')
        lines.append("  end")
    for g in gates_used:
        lines.append(f'  {g}{{{{"{GATES[g]}"}}}}')
    for a, b in edges:
        lines.append(f"  {short(a) if a in tasks else a} --> {short(b) if b in tasks else b}")
    lines.append(CLASSDEF)
    by = defaultdict(list)
    for _, ids in title_groups:
        for t in ids:
            by["lane" + tasks[t]["lane"]].append(short(t))
    by["gate"] = list(gates_used)
    if ext:
        by["ext"] = [short(t) for t in ext]
    for k, ids in sorted(by.items()):
        lines.append(f"  class {','.join(ids)} {k};")
    return "\n".join(lines) + "\n"


def build(track_tasks, entry, exits, extra_gate_edges=()):
    """entry: default entry gate; exits: {gate: [task ids]}. Returns reduced edges including gates."""
    edges = set()
    for t, v in track_tasks.items():
        deps = expand(v["raw"], t, track_tasks)
        m = re.search(r"\bG(\d+)\b", v["raw"])
        if deps:
            edges.update((d, t) for d in deps)
        else:
            edges.add((("G" + m.group(1)) if m else entry, t))
    for g, ids in exits.items():
        edges.update((t, g) for t in ids)
    edges.update(extra_gate_edges)
    nodes = set(track_tasks) | {a for a, _ in edges} | {b for _, b in edges}
    check_acyclic(nodes, edges)
    return reduce_edges(edges)


def subview(edges, inside, gates):
    """Edges to draw for a diagram that shows only `inside` (task ids) and `gates`.
    Tasks outside it that feed it directly are returned as external inputs (drawn dashed).
    Indirect dependencies through hidden tasks are kept, then the result is reduced again."""
    inside, gates = set(inside), set(gates)
    succ = defaultdict(set)
    for a, b in edges:
        succ[a].add(b)
    ext = sorted({a for a, b in edges if a not in inside and a not in gates and (b in inside or b in gates)})
    keep = inside | gates | set(ext)
    closure = set()
    for a in keep:
        seen, st = set(), list(succ[a])
        while st:
            x = st.pop()
            if x in seen:
                continue
            seen.add(x)
            st.extend(succ[x])
        closure.update((a, x) for x in seen if x in keep)
    red = reduce_edges(closure)
    xs = set(ext)
    drawn = [(a, b) for a, b in red
             if b in inside or (a in inside and b in gates) or (a in xs and b in gates) or (a in inside and b in xs)]
    return drawn, ext


def optional(v):
    return any(k in v["title"] for k in ("(Extended)", "(Stretch)", "(Blocked"))


def sinks(tasks, edges):
    has_out = {a for a, _ in edges}
    return [t for t in tasks if t not in has_out and not optional(tasks[t])]


def areas(tasks, spec, rest_label=None):
    """spec: list of (label, [ids]). Ids not named fall into rest_label (if given). Missing ids are ignored."""
    used, out = set(), []
    for label, ids in spec:
        ids = [i for i in ids if i in tasks]
        used.update(ids)
        if ids:
            out.append((label, sorted(ids)))
    rest = sorted(set(tasks) - used)
    if rest:
        if rest_label is None:
            raise SystemExit("packages without an area: " + ", ".join(rest))
        out.append((rest_label, rest))
    return out


def ids(prefix, nums):
    return [f"{prefix}{n:02d}" for n in nums]


AREA_HEAD = "| Area | Owner | Tasks inside | Needs from other lanes | Hands over to | Gates it feeds |"


def spec_groups(tasks, spec, prefix=""):
    """spec: list of (lane, name, covers, ids). Returns [(subgraph label, ids)] and the area records."""
    used, groups, recs = set(), [], []
    for lane, name, covers, want in spec:
        got = sorted(i for i in want if i in tasks)
        if not got:
            continue
        used.update(got)
        clean = lambda x: x.replace('"', "'").replace("`", "")
        groups.append((f"Lane {lane} · {clean(name)} ({clean(covers)})", got))
        recs.append(dict(lane=lane, name=name, covers=covers, ids=got))
    rest = sorted(set(tasks) - used)
    if rest:
        raise SystemExit("areas file: packages without an area: " + ", ".join(rest))
    return groups, recs


def spec_for(phase, tasks):
    """Areas of the shared file whose packages belong to this set of tasks (a use case or a whole phase)."""
    out = []
    for a in AREAS.get(phase, []):
        mine = [i for i in a["ids"] if i in tasks]
        if mine and len(mine) != len([i for i in a["ids"]]):
            other = [i for i in a["ids"] if i not in tasks]
            raise SystemExit(f"areas file: the area {a['name']!r} mixes two use cases ({mine[0]} and {other[0]}); split it")
        if mine:
            out.append((a["lane"], a["name"], a["delivers"], mine))
    return out


def area_view(tag, recs, tasks, gate_in, gate_out, gate_labels, gate_chain=()):
    """People-level view of one phase: a Mermaid diagram with one node per area and a table.
    gate_in: {gate: [task ids that wait for it]}; gate_out: {gate: [task ids that feed it]}."""
    of = {}
    for k, r in enumerate(recs):
        r["id"] = f"{tag}{r['lane']}{k + 1}"
        for i in r["ids"]:
            of[i] = r["id"]
    by_id = {r["id"]: r for r in recs}
    edges = set()
    for tid, v in tasks.items():
        if optional(v):
            continue
        for d in expand(v["raw"], tid, tasks):
            if d in of and not optional(tasks[d]) and of[d] != of[tid]:
                edges.add((of[d], of[tid]))
    entry = set()
    for gname, tids in gate_in.items():
        entry.update((gname, of[x]) for x in tids if x in of)
    edges |= entry
    for gname, tids in gate_out.items():
        edges.update((of[x], gname) for x in tids if x in of)
    edges.update(gate_chain)
    nodes = set(by_id) | {a for a, _ in edges} | {b for _, b in edges}
    check_acyclic(nodes, edges)                     # the areas must form a DAG, or they are badly cut
    red = sorted(set(reduce_edges(edges - entry)) | entry)
    lines = ["flowchart LR"]
    for lane in "ABCD":
        mine = [r for r in recs if r["lane"] == lane]
        if not mine:
            continue
        lines.append(f'  subgraph {tag}L{lane}["Lane {lane}"]')
        lines.append("    direction LR")
        for r in mine:
            lab = (r["name"] + "<br/>" + r["covers"]).replace('"', "'").replace("`", "")
            lines.append(f'    {r["id"]}["{lab}"]')
        lines.append("  end")
    for gname in sorted({n for n in nodes if n not in by_id}, key=lambda x: int(x[1:])):
        lines.append(f'  {gname}{{{{"{gate_labels[gname]}"}}}}')
    for a, b in red:
        lines.append(f"  {a} --> {b}")
    lines.append(CLASSDEF)
    for lane in "ABCD":
        mine = [r["id"] for r in recs if r["lane"] == lane]
        if mine:
            lines.append(f"  class {','.join(mine)} lane{lane};")
    lines.append("  class " + ",".join(sorted(n for n in nodes if n not in by_id)) + " gate;")
    rows = [AREA_HEAD, "|---|---|---|---|---|---|"]
    for r in recs:
        needs = sorted({a for a, b in edges if b == r["id"] and a in by_id and by_id[a]["lane"] != r["lane"]})
        gives = sorted({b for a, b in edges if a == r["id"] and b in by_id and by_id[b]["lane"] != r["lane"]})
        feeds = sorted({b for a, b in edges if a == r["id"] and b not in by_id}, key=lambda x: int(x[1:]))
        rows.append(f"| **{r['name']}**<br/>{r['covers']} | Lane {r['lane']} | " + ", ".join(f"`{i}`" for i in r["ids"]) + " | "
                    + ("; ".join(by_id[x]["name"] for x in needs) or "nothing") + " | "
                    + ("; ".join(by_id[x]["name"] for x in gives) or "nothing") + " | "
                    + (", ".join(feeds) or "none directly") + " |")
    return "\n".join(lines) + "\n", "\n".join(rows) + "\n"


def waiting(tasks, entry):
    """Task ids that have no dependency inside their own track: they start at the entry gate."""
    return [t for t, v in tasks.items() if not expand(v["raw"], t, tasks)]


def main():
    pg = parse(FILES["playground"], "playground")
    lung = parse(FILES["lung"], "lung")
    p3 = parse(FILES["p3"], "p3", split_at="### 10.4")
    for t, v in p3.items():
        v["track"] = "house" if v["track"] == "p3" else "fieldfit"
    p4 = parse(FILES["p4"], "p4")
    report = []

    # ---- Phase 2, Playground
    e = build(pg, "G7", {"G8": ["P2-C11"]})
    pg_groups, pg_recs = spec_groups(pg, spec_for(2, pg))
    (OUT / "p2_playground.mmd").write_text(diagram(pg_groups, pg, e, ["G7", "G8"]))
    report.append(f"P2 playground: {len(pg)} packages, {len(e)} drawn edges")

    # ---- Phase 2, Lung: two diagrams (data and 4D -> G9; airway and sky -> G10)
    pre = build(lung, "G7", {})
    loose = sinks(lung, pre)
    exits = {"G9": [t for t in loose if lung[t]["lane"] == "A"], "G10": [t for t in loose if lung[t]["lane"] != "A"]}
    e = build(lung, "G7", exits, extra_gate_edges={("P2-A32", "G10")} if "P2-A32" in lung else ())
    lung_groups, lung_recs = spec_groups(lung, spec_for(2, lung))
    part1 = sorted(t for t, v in lung.items() if v["lane"] == "A" or t in ("P2-D01", "P2-D02", "P2-D03", "P2-D04", "P2-D05", "P2-D06"))
    part2 = sorted(t for t in lung if t not in part1)
    d1, x1 = subview(e, part1, ["G7", "G9"])
    g1 = [(lab, [i for i in its if i in part1]) for lab, its in lung_groups]
    (OUT / "p2_lung_4d.mmd").write_text(diagram([x for x in g1 if x[1]], lung, d1, ["G7", "G9"], x1))
    d2, x2 = subview(e, part2, ["G7", "G10"])
    g2 = [(lab, [i for i in its if i in part2]) for lab, its in lung_groups]
    (OUT / "p2_lung_airway.mmd").write_text(diagram([x for x in g2 if x[1]], lung, d2, ["G7", "G10"], x2))
    report.append(f"P2 lung: {len(lung)} packages; 4D diagram {len(part1)} nodes + {len(x1)} inputs, {len(d1)} edges; "
                  f"airway diagram {len(part2)} nodes + {len(x2)} inputs, {len(d2)} edges; exits {exits}")
    # people-level view of Phase 2
    p2 = {**pg, **lung}
    out = {"G8": ["P2-C11"], "G9": exits["G9"], "G10": exits["G10"] + (["P2-A32"] if "P2-A32" in lung else [])}
    mmd, tab = area_view("P2", pg_recs + lung_recs, p2, {"G7": waiting(pg, "G7") + waiting(lung, "G7")}, out, GATES,
                         gate_chain={("G8", "G11"), ("G9", "G11"), ("G10", "G11")})
    (OUT / "p2_areas.mmd").write_text(mmd)
    (OUT / "p2_areas.md").write_text(tab)

    # ---- Phase 3
    house = {t: v for t, v in p3.items() if v["track"] == "house"}
    ff = {t: v for t, v in p3.items() if v["track"] == "fieldfit"}
    for v in p3.values():
        v["raw"] = re.sub(r"\bG7\b( Phase-1 exit)?", "G11", v["raw"])   # Phase 3 starts when the lanes are free (§9.4)
    x12, x13 = sinks(house, build(house, "G11", {})), sinks(ff, build(ff, "G11", {}))
    e1 = build(house, "G11", {"G12": x12})
    e2 = build(ff, "G11", {"G13": x13})
    h_groups, h_recs = spec_groups(house, spec_for(3, house))
    f_groups, f_recs = spec_groups(ff, spec_for(3, ff))
    (OUT / "p3.mmd").write_text(diagram(h_groups + f_groups, p3, sorted(set(e1) | set(e2)), ["G11", "G12", "G13"]))
    report.append(f"P3: {len(p3)} packages ({len(house)} Shared House, {len(ff)} FieldFit)")
    mmd, tab = area_view("P3", h_recs + f_recs, p3, {"G11": [t for t in waiting(house, "G11") + waiting(ff, "G11") if not optional(p3[t])]},
                         {"G12": x12, "G13": x13}, GATES)
    (OUT / "p3_areas.mmd").write_text(mmd)
    (OUT / "p3_areas.md").write_text(tab)

    # ---- Phase 4
    freeze_wait = [t for t, v in p4.items() if "G14" in v["raw"]]
    for v in p4.values():   # every Phase-4 package starts after the use cases are done
        v["raw"] = v["raw"].replace("G12, G13", "G13 FieldFit").replace("G11 Phase-2 exit", "G13 FieldFit").replace("G7 Phase-1 exit", "G13 FieldFit")
    FREEZE = {("P4-A01", "G14"), ("P4-A03", "G14"), ("P4-C04", "G14"), ("G13", "G14")}
    pre = build(p4, "G13", {}, extra_gate_edges=FREEZE)
    x15 = sinks(p4, pre)
    e = build(p4, "G13", {"G15": x15}, extra_gate_edges=FREEZE)
    p4_groups, p4_recs = spec_groups(p4, spec_for(4, p4))
    glab = dict(GATES)
    glab["G13"] = "G12 and G13 · Phase-3 use cases done"
    txt = diagram(p4_groups, p4, e, ["G13", "G14", "G15"]).replace('G13{{"G13 · FieldFit"}}', 'G13{{"G12 and G13 · Phase-3 use cases done"}}')
    (OUT / "p4.mmd").write_text(txt)
    report.append(f"P4: {len(p4)} packages")
    start = [t for t in waiting(p4, "G13") if t not in freeze_wait]
    mmd, tab = area_view("P4", p4_recs, p4, {"G13": start, "G14": freeze_wait},
                         {"G14": ["P4-A01", "P4-A03", "P4-C04"], "G15": x15}, glab, gate_chain={("G14", "G15")})
    (OUT / "p4_areas.mmd").write_text(mmd)
    (OUT / "p4_areas.md").write_text(tab)

    counts = defaultdict(lambda: defaultdict(int))
    for name, d in (("P2", {**pg, **lung}), ("P3", p3), ("P4", p4)):
        for v in d.values():
            counts[name][v["lane"]] += 1
    for name in ("P2", "P3", "P4"):
        report.append(f"{name} per lane: " + ", ".join(f"{l}={counts[name][l]}" for l in "ABCD"))
    (OUT / "later_report.txt").write_text("\n".join(report) + "\n")
    print("\n".join(report))


if __name__ == "__main__":
    main()
