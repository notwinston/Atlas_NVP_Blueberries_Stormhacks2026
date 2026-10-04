#!/usr/bin/env python3
"""Assemble ATLAS_Master_Plan.md from the lead's parts, the cleaned drafts and the generated tables/diagrams."""
import re, sys
from pathlib import Path
S = Path(__file__).parent
P, C, W = S / "parts", S / "clean", S / "wbs" / "out"
OUT = Path(sys.argv[1])

def rd(p): return Path(p).read_text().rstrip() + "\n"

front = rd(P / "00_front.md")
arch = rd(P / "06_arch.md")

# ---- build plan with generated content
build = rd(P / "09_build.md")
rep = {
    "{{P1_OVERVIEW_MMD}}": rd(W / "p1_overview.mmd").rstrip(),
    "{{P1_HANDOVERS}}": rd(W / "p1_handovers.md").rstrip(),
    "{{P2_PLAYGROUND_MMD}}": rd(W / "p2_playground.mmd").rstrip(),
    "{{P2_LUNG_4D_MMD}}": rd(W / "p2_lung_4d.mmd").rstrip(),
    "{{P2_LUNG_AIRWAY_MMD}}": rd(W / "p2_lung_airway.mmd").rstrip(),
    "{{P3_MMD}}": rd(W / "p3.mmd").rstrip(),
    "{{P4_MMD}}": rd(W / "p4.mmd").rstrip(),
}
for lane in "XABCDG":
    rep["{{P1_TABLE_%s}}" % lane] = rd(W / f"p1_table_{lane}.md").rstrip()
for lane in "ABCD":
    rep["{{P1_LANE_%s_MMD}}" % lane] = rd(W / f"p1_lane_{lane}.mmd").rstrip()
nt = (W / "p1_counts.txt").read_text().split()
rep["{{P1_NTASKS}}"], rep["{{P1_NEDGES}}"] = nt[0], nt[1]
rep["{{COUNTS_P1}}"] = f"A {nt[3]} · B {nt[4]} · C {nt[5]} · D {nt[6]} (+ {nt[2]} shared, {nt[7]} gates)"
for line in (W / "later_report.txt").read_text().split("\n"):
    m = re.match(r"(P[234]) per lane: A=(\d+), B=(\d+), C=(\d+), D=(\d+)", line)
    if m:
        rep["{{COUNTS_%s}}" % m.group(1)] = f"A {m.group(2)} · B {m.group(3)} · C {m.group(4)} · D {m.group(5)}"
rep["{{P1_GATES_TREE}}"] = (W / "p1_gates_tree.txt").read_text().rstrip()
rep["{{P1_WAVES}}"] = (W / "p1_waves.md").read_text().rstrip()
rep["{{P1_LANES_TREE}}"] = (W / "p1_lanes_tree.txt").read_text().rstrip()
_n = sum(1 for l in (W / "p1_areas.md").read_text().split("\n") if l.startswith("| **"))
_words = {12: "Twelve", 13: "Thirteen", 14: "Fourteen", 15: "Fifteen", 16: "Sixteen", 17: "Seventeen", 18: "Eighteen", 19: "Nineteen", 20: "Twenty"}
rep["{{P1_NAREAS}}"] = _words.get(_n, str(_n))
rep["{{P1_NAREAS_LC}}"] = _words.get(_n, str(_n)).lower()
rep["{{P1_AREAS}}"] = (W / "p1_areas.md").read_text().rstrip()
for ph in ("P2", "P3", "P4"):
    rep["{{%s_AREAS_MMD}}" % ph] = (W / f"{ph.lower()}_areas.mmd").read_text().rstrip()
    rep["{{%s_AREAS}}" % ph] = (W / f"{ph.lower()}_areas.md").read_text().rstrip()
rep["{{P1_AREA_STEPS}}"] = (W / "p1_area_steps.md").read_text().rstrip()
for k, v in rep.items():
    assert k in build, k
    build = build.replace(k, v)
assert not re.search(r"\{\{[A-Z0-9_]+\}\}", build)

use_cases_head = """---

## 10. Use cases

Four use cases are built on the Phase-1 platform. Each is specified here with its work packages; §9.6–9.8 show the order in which the packages are built.

| Use case | Phase | Lanes | Geometry it needs, and why | Exit gate |
|---|---|---|---|---|
| **10.1 Geometry Playground** | 2 | C, A | All of S0–S6: the room bent by the dial, walked, and compared | G8 |
| **10.2 Lung Mission** | 2 | B, D, A | Hyperbolic space for a tree that doubles at every generation; ℝ³ × S¹ for a tumour that moves with breathing; a sphere for the directions a needle can take | G9, G10 |
| **10.3 Shared House / Post Circling** | 3 | A, C (two packages from D) | A two-sheet branched cover: two homes joined at one door, flat everywhere except two posts | G12 |
| **10.4 FieldFit** | 3 | B, D | The sphere of view directions: a lens that trades sharpness for field | G13 |
"""

def sec(text): return "---\n\n" + text.rstrip() + "\n"

i0 = front.index("## 0. Read me first")
order = [
    front[:i0].rstrip().rstrip("-").rstrip() + "\n",
    "{{TOC}}",
    "---\n\n" + front[i0:],
    rd(P / "02_decisions.md"),
    rd(P / "03_spaces.md"),
    rd(P / "04_scan.md"),
    rd(P / "05_4d.md"),
    arch,
    sec(rd(C / "07_link.md")),
    rd(P / "08_graphics.md"),
    build,
    use_cases_head,
    rd(C / "10a_playground.md"),
    rd(C / "10b_lung.md"),
    rd(C / "10c_house_fieldfit.md"),
    sec(rd(C / "11_verification.md")),
    sec(rd(C / "12_risks.md")),
    sec(rd(C / "13_demo_docs.md")),
    rd(P / "14_open.md"),
    rd(P / "appA_formulas.md"),
    sec(rd(C / "appB_schema.md")),
    sec(rd(C / "appC_setup.md")),
    sec(rd(C / "appD_traceability.md")),
    sec(rd(C / "appE_glossary_sources.md")),
]
doc = "\n".join(x.rstrip() + "\n" for x in order)

# ---- table of contents from the level-2 headings
def anchor(h):
    s = h.strip().lower()
    s = re.sub(r"[^\w\s\-]", "", s, flags=re.UNICODE)
    return re.sub(r"\s", "-", s)
heads = [m.group(1) for m in re.finditer(r"(?m)^## (.+)$", doc)]
toc = ["## Contents", ""]
for h in heads:
    toc.append(f"- [{h}](#{anchor(h)})")
doc = doc.replace("{{TOC}}", "---\n\n" + "\n".join(toc) + "\n")
doc = re.sub(r"\n{3,}", "\n\n", doc)
OUT.write_text(doc)
print("lines", doc.count("\n"), "words", len(doc.split()), "chars", len(doc))
print("level-2 sections:", len(heads))
