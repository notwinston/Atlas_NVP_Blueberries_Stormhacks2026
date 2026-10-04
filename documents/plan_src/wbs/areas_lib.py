"""Work areas for people: one editable file shared by the plan's generators and the PDF handout.

File format (Markdown):
    ## Phase 1
    ### Lane A
    #### Build seat and stereo rig
    Delivers: first build on both phones, URP settings, ...
    - `P1-A02` First build on both phones
    - `P1-A03` URP asset and quality tiers
A piece may carry a use case in its name before a colon or a middle dot; that is free text.
"""
import re
from pathlib import Path


def load_areas(path):
    """Returns {phase number: [dict(lane, name, delivers, ids)]} in file order."""
    out, phase, lane, cur = {}, None, None, None
    for raw in Path(path).read_text().split("\n"):
        line = raw.rstrip()
        m = re.match(r"^## Phase (\d)", line)
        if m:
            phase, lane, cur = int(m.group(1)), None, None
            out.setdefault(phase, [])
            continue
        m = re.match(r"^### Lane ([A-D])", line)
        if m:
            lane, cur = m.group(1), None
            continue
        m = re.match(r"^#### (.+)$", line)
        if m and phase and lane:
            cur = dict(lane=lane, name=m.group(1).strip(), delivers="", ids=[])
            out[phase].append(cur)
            continue
        m = re.match(r"^Delivers:\s*(.+)$", line)
        if m and cur is not None:
            cur["delivers"] = m.group(1).strip().rstrip(".")
            continue
        m = re.match(r"^- `?(P\d-[A-DX]\d\d)`?", line)
        if m and cur is not None:
            cur["ids"].append(m.group(1))
    return out


def check_cover(areas, task_ids, phase):
    """Every lane task of the phase must be in exactly one area. Returns a list of problems."""
    seen, probs = {}, []
    for a in areas.get(phase, []):
        for i in a["ids"]:
            if i in seen:
                probs.append(f"{i} is in two areas: {seen[i]} and {a['name']}")
            seen[i] = a["name"]
            if i not in task_ids:
                probs.append(f"{i} (in {a['name']}) is not a task of the plan")
    for i in sorted(task_ids):
        if i not in seen:
            probs.append(f"{i} is in no area")
    return probs
