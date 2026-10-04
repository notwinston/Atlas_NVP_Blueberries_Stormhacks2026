#!/usr/bin/env python3
"""Validate the assembled plan. Usage: python3 check/validate.py <plan.md> <figures_dir> [--mermaid]"""
import re, sys, subprocess, os, tempfile, concurrent.futures as cf
from pathlib import Path
plan = Path(sys.argv[1]); figs = Path(sys.argv[2]); do_mmd = "--mermaid" in sys.argv
txt = plan.read_text(); lines = txt.split("\n")
problems = []
def P(kind, msg): problems.append((kind, msg))

# ---- fences and tables
fence = False; blocks = []; cur = None
for i, l in enumerate(lines, 1):
    if l.strip().startswith("```"):
        if not fence:
            fence = True; cur = (l.strip()[3:], i, [])
        else:
            fence = False; blocks.append(cur); cur = None
        continue
    if fence: cur[2].append(l)
if fence: P("fence", "unclosed code fence")
def cols(x):
    x = re.sub(r"`[^`]*`", "", x); return x.count("|") - x.count("\\|")
i = 0; infence = False
while i < len(lines):
    l = lines[i]
    if l.strip().startswith("```"): infence = not infence; i += 1; continue
    if not infence and l.startswith("|"):
        j = i
        while j < len(lines) and lines[j].startswith("|"): j += 1
        n = cols(lines[i])
        if j - i < 2 or not re.match(r"^\|[\s:\-|]+\|\s*$", lines[i + 1]): P("table", f"line {i+1}: no separator row")
        for k in range(i, j):
            if cols(lines[k]) != n: P("table", f"line {k+1}: {cols(lines[k])-1} cells vs {n-1}: {lines[k][:60]}")
        i = j; continue
    i += 1

# ---- task ids: defined rows vs mentions
body = "\n".join(l for l in lines)
defined = set(re.findall(r"(?m)^\| `?(P\d-[A-DXG]\d{1,2})`? \|", body))
mentioned = set(re.findall(r"\bP\d-[A-DXG]\d{1,2}\b", body))
for t in sorted(mentioned - defined): P("task-id", f"{t} is mentioned but has no row")
# ranges like P2-A20–P2-A32 are fine; shorthand ids in the text tree (A04, B11 ...) are not checked
gates = set(re.findall(r"\bG(\d{1,2})\b", body))
for g in sorted(gates, key=int):
    if int(g) > 15: P("gate", f"G{g} does not exist")

# ---- images
for m in re.finditer(r"!\[[^\]]*\]\(([^)]+)\)", body):
    if not (plan.parent / m.group(1)).exists() and not (figs.parent / m.group(1)).exists(): P("image", m.group(1))

# ---- section references
heads = set()
for l in lines:
    m = re.match(r"^#{2,5} (?:Appendix )?((?:\d+|[A-F])(?:\.\d+)*)[\. ]", l)
    if m: heads.add(m.group(1))
EXT = re.compile(r"(Spec|Brief|BP|R[1-6]|Pipeline)\b(?:[\s,;]|and|§[\d\.]+|[A-Z]\.\d+)*$")
for m in re.finditer(r"§(\d+(?:\.\d+)*)", body):
    ref = m.group(1).rstrip(".")
    ctx = body[max(0, m.start() - 80):m.start()]
    if EXT.search(ctx): continue                     # a reference into another document
    if ref not in heads:
        ln = body.count("\n", 0, m.start()) + 1
        P("section-ref", f"line {ln}: §{ref} …{body[max(0,m.start()-40):m.end()+12]!r}")

# ---- words that must not appear
BAN = [r"bump\.warnDist", r"bump\.depthHz", r"\bU\d{1,2}\b(?! ←)", r"\bcanon\b", r"\bdirectives?\b", r"\breviewers?\b", r"\bRD\d+\b", r"\blens L\d",
       r"157 B", r"\b366\b", r"\bpillar", r"Station menu", r"StationMenu", r"\bcorpus\b", r"\(decision \d", r"field-expanding",
       r"boxes simply stay", r"45 tiles drawn", r"Hands are optional", r"X5\b", r"\bT\d\d\b(?! )"]
for pat in BAN:
    for m in re.finditer(pat, body):
        ln = body.count("\n", 0, m.start()) + 1
        P("banned", f"line {ln}: /{pat}/ …{body[max(0,m.start()-45):m.end()+35]!r}")
# time estimates
for m in re.finditer(r"\b(\d+(?:\.\d+)?)\s*(hours?|days?|weeks?|person-days?)\b", body):
    ln = body.count("\n", 0, m.start()) + 1
    P("time?", f"line {ln}: …{body[max(0,m.start()-50):m.end()+30]!r}")

# ---- mermaid
mm = [(lang, ln, "\n".join(src)) for lang, ln, src in blocks if lang.strip() == "mermaid"]
if do_mmd:
    tmp = Path(tempfile.mkdtemp(prefix="mmd"))
    def render(k):
        lang, ln, src = mm[k]
        f = tmp / f"m{k:02d}.mmd"; f.write_text(src)
        r = subprocess.run(["npx", "-y", "@mermaid-js/mermaid-cli@latest", "-i", str(f), "-o", str(tmp / f"m{k:02d}.svg"), "-b", "white"],
                           capture_output=True, text=True)
        ok = r.returncode == 0 and (tmp / f"m{k:02d}.svg").exists()
        return k, ln, ok, (r.stderr or r.stdout)[-300:]
    with cf.ThreadPoolExecutor(4) as ex:
        for k, ln, ok, err in ex.map(render, range(len(mm))):
            if not ok: P("mermaid", f"block {k} at line {ln}: {err}")
print(f"lines {len(lines)}  words {len(txt.split())}  chars {len(txt)}  tables ok  mermaid blocks {len(mm)}{' (rendered)' if do_mmd else ''}")
print(f"task rows defined {len(defined)}  ids mentioned {len(mentioned)}  level-2 sections {sum(1 for l in lines if l.startswith('## '))}")
kinds = {}
for k, m in problems: kinds[k] = kinds.get(k, 0) + 1
print("problems:", kinds or "none")
for k, m in problems: print(f"  [{k}] {m}")
