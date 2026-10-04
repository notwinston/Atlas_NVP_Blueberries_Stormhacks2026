#!/usr/bin/env python3
"""Build documents/ATLAS_Build_Graph.pdf from documents/ATLAS_Master_Plan.md.

The PDF is the build graph for people: each box is one work area (a human-sized piece
of work such as "implement hyperbolic terrain generation"), not a single task.
Everything is read from the plan at run time: the work areas (tables headed
"Area | Owner | Tasks inside | ..."), the task rows and their dependencies, the gates.
From those the script works out which area starts after which, what an area needs
from another lane, what it hands over and which gate it feeds.

Run from the repository root:

    python3 documents/tools/make_build_graph_pdf.py            # area level, for people
    python3 documents/tools/make_build_graph_pdf.py --detail   # plus the task-level appendix

Options: --plan FILE, --out FILE, --build-dir DIR (intermediate files; the default is a
folder in the system temp directory, never inside the repository).

Needs:
  - Python 3.9 or newer, standard library only.
  - Node.js with npx; diagrams are rendered by `npx -y @mermaid-js/mermaid-cli@latest`
    (written against mermaid-cli 12). Set MMDC="mmdc" to use an installed copy.
  - Google Chrome or Chromium for the HTML to PDF step. Set CHROME=/path/to/chrome
    if it is not in a standard place.
  - Optional: pdfinfo (poppler) to print page numbers in the table of contents.
"""
from __future__ import annotations

import argparse
import concurrent.futures
import datetime
import hashlib
import html
import json
import os
import re
import shlex
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
DEFAULT_PLAN = REPO / "documents" / "ATLAS_Master_Plan.md"
DEFAULT_OUT = REPO / "documents" / "ATLAS_Build_Graph.pdf"

ID_RE = re.compile(r"\bP(\d)-([A-DXG])(\d{1,2})\b")
LANES = ["A", "B", "C", "D"]
PALETTE = {  # fallbacks; the plan's classDef lines win when present
    "laneA": {"fill": "#e3f4ec", "stroke": "#2f8f6b", "color": "#12392b"},
    "laneB": {"fill": "#eee7fb", "stroke": "#7b4fd6", "color": "#2c1a5c"},
    "laneC": {"fill": "#fdeede", "stroke": "#d9722b", "color": "#5a2c0c"},
    "laneD": {"fill": "#e3edfc", "stroke": "#2b6fd9", "color": "#11305f"},
    "laneX": {"fill": "#eef1f6", "stroke": "#7a8499", "color": "#1d2433"},
    "gate": {"fill": "#1d2433", "stroke": "#1d2433", "color": "#ffffff"},
    "ext": {"fill": "#ffffff", "stroke": "#7a8499", "color": "#4a5368"},
}
PAGE_MM = {"A4": (297, 210), "A3": (420, 297), "A2": (594, 420)}
MARGIN_MM = (15, 12, 13, 12)  # top, right, bottom, left
PX_PER_MM = 96 / 25.4
MIN_LABEL_PT = 7.0   # smallest diagram label accepted before a larger page is used
MAX_LABEL_PT = 10.0  # largest diagram label; small diagrams are not blown up
FONT = '"Helvetica Neue", Helvetica, Arial, sans-serif'
MONO = 'Menlo, "SF Mono", Consolas, monospace'

NOTES: list[str] = []


def warn(msg: str) -> None:
    NOTES.append(msg)
    print("WARNING: " + msg, file=sys.stderr)


def fail(msg: str) -> None:
    print("\nERROR: " + msg, file=sys.stderr)
    sys.exit(2)


# --------------------------------------------------------------------------
# Markdown: blocks and sections
# --------------------------------------------------------------------------

HEADING_RE = re.compile(r"^(#{1,6})\s+(.*?)\s*#*\s*$")
FENCE_RE = re.compile(r"^\s{0,3}(`{3,}|~{3,})\s*([\w+-]*)\s*$")
DELIM_RE = re.compile(r"^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$")
LIST_RE = re.compile(r"^(\s*)([-*+]|\d{1,3}[.)])\s+(\S.*)$")
HR_RE = re.compile(r"^\s{0,3}([-*_])(\s*\1){2,}\s*$")


class Block:
    def __init__(self, kind, line, **kw):
        self.kind = kind
        self.line = line
        self.text = kw.get("text", "")
        self.level = kw.get("level", 0)
        self.lang = kw.get("lang", "")
        self.header = kw.get("header", [])
        self.rows = kw.get("rows", [])
        self.ordered = kw.get("ordered", False)
        self.start = kw.get("start", 1)
        self.items = kw.get("items", [])  # [(text, [Block])]
        self.sec = None


def split_row(line: str) -> list[str]:
    s = line.strip()
    if s.startswith("|"):
        s = s[1:]
    if s.endswith("|") and not s.endswith("\\|"):
        s = s[:-1]
    return [c.strip().replace("\\|", "|") for c in re.split(r"(?<!\\)\|", s)]


def _indent(line: str) -> int:
    return len(line) - len(line.lstrip(" "))


def _starts_block(lines, i, hi) -> bool:
    line = lines[i]
    if HEADING_RE.match(line) or FENCE_RE.match(line) or LIST_RE.match(line) or HR_RE.match(line):
        return True
    if line.lstrip().startswith(">"):
        return True
    if line.lstrip().startswith("|") and i + 1 < hi and DELIM_RE.match(lines[i + 1]) and "-" in lines[i + 1]:
        return True
    return False


def parse_blocks(lines: list[str], lo: int = 0, hi: int | None = None, base_line: int = 0) -> list[Block]:
    hi = len(lines) if hi is None else hi
    out: list[Block] = []
    i = lo
    while i < hi:
        line = lines[i]
        if not line.strip():
            i += 1
            continue
        m = FENCE_RE.match(line)
        if m:
            fence, lang = m.group(1)[:3], m.group(2).lower()
            j, body = i + 1, []
            while j < hi and not (lines[j].strip().startswith(fence) and not lines[j].strip().strip("`~")):
                body.append(lines[j])
                j += 1
            out.append(Block("code", base_line + i + 1, lang=lang, text="\n".join(body)))
            i = j + 1
            continue
        m = HEADING_RE.match(line)
        if m:
            out.append(Block("heading", base_line + i + 1, level=len(m.group(1)), text=m.group(2)))
            i += 1
            continue
        if line.lstrip().startswith("|") and i + 1 < hi and DELIM_RE.match(lines[i + 1]) and "-" in lines[i + 1]:
            header, j, rows = split_row(line), i + 2, []
            while j < hi and lines[j].lstrip().startswith("|"):
                rows.append(split_row(lines[j]))
                j += 1
            out.append(Block("table", base_line + i + 1, header=header, rows=rows))
            i = j
            continue
        if HR_RE.match(line):
            out.append(Block("hr", base_line + i + 1))
            i += 1
            continue
        m = LIST_RE.match(line)
        if m:
            base = len(m.group(1))
            ordered = m.group(2)[0].isdigit()
            start = int(re.match(r"\d+", m.group(2)).group()) if ordered else 1
            items, first = [], i
            while i < hi:
                m = LIST_RE.match(lines[i])
                if not m or len(m.group(1)) != base or m.group(2)[0].isdigit() != ordered:
                    break
                text, sub = [m.group(3).strip()], []
                i += 1
                while i < hi:
                    l = lines[i]
                    if not l.strip():
                        k = i + 1
                        while k < hi and not lines[k].strip():
                            k += 1
                        if k < hi and _indent(lines[k]) > base and not HEADING_RE.match(lines[k]):
                            sub.append("")
                            i += 1
                            continue
                        break
                    if _indent(l) > base:
                        sub.append(l)
                        i += 1
                        continue
                    if _starts_block(lines, i, hi):
                        break
                    text.append(l.strip())
                    i += 1
                while sub and sub[0].strip() and not LIST_RE.match(sub[0]) and not FENCE_RE.match(sub[0]):
                    text.append(sub.pop(0).strip())
                children = []
                if any(s.strip() for s in sub):
                    ded = min(_indent(s) for s in sub if s.strip())
                    children = parse_blocks([s[ded:] if s.strip() else "" for s in sub])
                items.append((" ".join(text), children))
                while i < hi and not lines[i].strip():
                    k = i
                    while k < hi and not lines[k].strip():
                        k += 1
                    m2 = LIST_RE.match(lines[k]) if k < hi else None
                    if m2 and len(m2.group(1)) == base and m2.group(2)[0].isdigit() == ordered:
                        i = k
                    break
            out.append(Block("list", base_line + first + 1, ordered=ordered, start=start, items=items))
            continue
        if line.lstrip().startswith(">"):
            j, body = i, []
            while j < hi and lines[j].lstrip().startswith(">"):
                body.append(re.sub(r"^\s*>\s?", "", lines[j]))
                j += 1
            out.append(Block("quote", base_line + i + 1, items=[("", parse_blocks(body))]))
            i = j
            continue
        j, buf = i, []
        while j < hi and lines[j].strip() and (j == i or not _starts_block(lines, j, hi)):
            buf.append(lines[j].strip())
            j += 1
        out.append(Block("para", base_line + i + 1, text=" ".join(buf)))
        i = j
    return out


class Section:
    def __init__(self, level, title, line, parent):
        self.level, self.title, self.line, self.parent = level, title, line, parent
        m = re.match(r"^(\d+(?:\.\d+)*|[A-Z]\.\d+(?:\.\d+)*)\.?\s+(.*)$", title)
        self.number, self.name = (m.group(1), m.group(2)) if m else ("", title)
        self.blocks: list[Block] = []
        self.children: list[Section] = []

    def walk(self):
        yield self
        for c in self.children:
            yield from c.walk()

    def all_blocks(self):
        for s in self.walk():
            yield from s.blocks

    def ref(self) -> str:
        return ("§" + self.number) if self.number else self.title


def build_sections(blocks: list[Block]) -> Section:
    root = Section(0, "(document)", 0, None)
    stack = [root]
    for b in blocks:
        if b.kind == "heading":
            while stack[-1].level >= b.level:
                stack.pop()
            s = Section(b.level, b.text, b.line, stack[-1])
            stack[-1].children.append(s)
            stack.append(s)
        else:
            b.sec = stack[-1]
            stack[-1].blocks.append(b)
    return root


def find_section(parent: Section, pattern: str, what: str, deep: bool = False, required: bool = True):
    pool = list(parent.walk())[1:] if deep else parent.children
    for s in pool:
        if re.search(pattern, s.name, re.I):
            return s
    if required:
        seen = "; ".join(s.title for s in parent.children) or "(none)"
        fail(f"The plan has no {what}: expected a heading matching /{pattern}/ under "
             f"'{parent.title}' (line {parent.line}). Headings found there: {seen}")
    return None


# --------------------------------------------------------------------------
# Inline Markdown to HTML
# --------------------------------------------------------------------------

def esc(s: str) -> str:
    return html.escape(s, quote=False)


def plain(md: str) -> str:
    s = re.sub(r"`([^`]*)`", r"\1", md)
    s = re.sub(r"\*\*(.+?)\*\*", r"\1", s)
    s = re.sub(r"<br\s*/?>", " ", s)
    return re.sub(r"\s+", " ", s).strip()


def norm(s: str) -> str:
    return re.sub(r"[^a-z0-9]+", " ", plain(s).lower()).strip()


def lane_cls(lane: str) -> str:
    return "l" + (lane if lane in "ABCDX" and lane else "X")


def code_html(c: str) -> str:
    m = ID_RE.fullmatch(c.strip())
    if m:
        lane = m.group(2)
        cls = "gt" if lane == "G" else lane_cls(lane)
        return f'<code class="tid {cls}">{esc(c.strip())}</code>'
    return f"<code>{esc(c)}</code>"


def inline(md: str) -> str:
    codes: list[str] = []

    def stash(m):
        codes.append(m.group(2))
        return f"\x00{len(codes) - 1}\x00"

    s = re.sub(r"(`+)(.+?)\1", stash, md)
    s = re.sub(r"<br\s*/?>", "\x01", s)
    s = esc(s)
    s = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", s)
    s = re.sub(r"(?<![\w*])\*(?=\S)([^*]+?)(?<=\S)\*(?![\w*])", r"<em>\1</em>", s)
    s = re.sub(r"\[([^\]]+)\]\((https?://[^)\s]+)\)", r'<a href="\2">\1</a>', s)
    s = re.sub(r"\[([^\]]+)\]\(#[^)]*\)", r"\1", s)
    s = s.replace("\x01", "<br>")
    return re.sub(r"\x00(\d+)\x00", lambda m: code_html(codes[int(m.group(1))]), s)


# --------------------------------------------------------------------------
# Mermaid flowchart source: a small parser, enough for the plan's diagrams
# --------------------------------------------------------------------------

class MermaidError(Exception):
    pass


SHAPES = [("{{", "}}"), ("[[", "]]"), ("[(", ")]"), ("([", "])"), ("((", "))"),
          ("[/", "/]"), ("[\\", "\\]"), ("[", "]"), ("(", ")"), ("{", "}"), (">", "]")]
ARROW_RE = re.compile(r"\s*<?(--+|==+|-\.+-)([>xo])?\s*(?:\|([^|]*)\|)?\s*")
ARROW_TEXT_RE = re.compile(r"\s*(--|==|-\.)\s+([^-=|>.][^|>]*?)\s+(--+>|==+>|\.-+>|---)\s*")


class MNode:
    def __init__(self, nid):
        self.id, self.label, self.open, self.close, self.sub = nid, None, "[", "]", None

    def lines(self) -> list[str]:
        return [x.strip() for x in re.split(r"<br\s*/?>", self.label if self.label is not None else self.id)]

    def text(self) -> str:
        return " ".join(self.lines())


class MGraph:
    def __init__(self):
        self.header = "flowchart LR"
        self.nodes: dict[str, MNode] = {}
        self.edges: list[tuple[str, str]] = []
        self.subs: dict[str, dict] = {}
        self.classdefs: dict[str, str] = {}
        self.cls: dict[str, str] = {}

    def is_gate(self, nid: str) -> bool:
        n = self.nodes.get(nid)
        return self.cls.get(nid) == "gate" or (n is not None and n.open == "{{")

    def top_sub(self, nid: str):
        sid = self.nodes[nid].sub
        while sid is not None and self.subs[sid]["parent"] is not None:
            sid = self.subs[sid]["parent"]
        return sid


def parse_mermaid(src: str) -> MGraph:
    g = MGraph()
    stack: list[str] = []
    first = True

    def node_at(s, pos):
        m = re.compile(r"\s*([A-Za-z0-9_]+)").match(s, pos)
        if not m:
            raise MermaidError(f"cannot read a node in: {s!r}")
        nid, pos = m.group(1), m.end()
        n = g.nodes.get(nid)
        if n is None:
            n = g.nodes[nid] = MNode(nid)
        if stack and n.sub is None:
            n.sub = stack[-1]
            g.subs[stack[-1]]["nodes"].append(nid)
        for op, cl in SHAPES:
            if s.startswith(op, pos):
                body_start = pos + len(op)
                if s.startswith('"', body_start):
                    q = s.find('"', body_start + 1)
                    if q < 0 or not s.startswith(cl, q + 1):
                        raise MermaidError(f"unbalanced quotes in: {s!r}")
                    label, pos = s[body_start + 1:q], q + 1 + len(cl)
                else:
                    e = s.find(cl, body_start)
                    if e < 0:
                        raise MermaidError(f"unclosed shape in: {s!r}")
                    label, pos = s[body_start:e].strip(), e + len(cl)
                n.label, n.open, n.close = label, op, cl
                break
        m = re.compile(r":::([A-Za-z0-9_]+)").match(s, pos)
        if m:
            g.cls[nid] = m.group(1)
            pos = m.end()
        return nid, pos

    for raw in src.split("\n"):
        s = raw.strip()
        if not s or s.startswith("%%"):
            continue
        if first:
            first = False
            if not re.match(r"^(flowchart|graph)\b", s):
                raise MermaidError("not a flowchart")
            g.header = s
            continue
        if s.endswith(";"):
            s = s[:-1].rstrip()
        m = re.match(r"^subgraph\s+(.*)$", s)
        if m:
            rest = m.group(1).strip()
            m2 = re.match(r'^([A-Za-z0-9_]+)\s*\[\s*"?(.*?)"?\s*\]$', rest)
            sid, title = (m2.group(1), m2.group(2)) if m2 else (rest.strip('"'), rest.strip('"'))
            g.subs[sid] = {"title": title, "parent": stack[-1] if stack else None, "nodes": []}
            stack.append(sid)
            continue
        if s == "end":
            if stack:
                stack.pop()
            continue
        if re.match(r"^direction\s+\w+$", s) or re.match(r"^(style|linkStyle|click)\b", s):
            continue
        m = re.match(r"^classDef\s+(\S+)\s+(.*)$", s)
        if m:
            g.classdefs[m.group(1)] = m.group(2)
            continue
        m = re.match(r"^class\s+(.+?)\s+(\S+)$", s)
        if m:
            for nid in m.group(1).split(","):
                g.cls[nid.strip()] = m.group(2)
            continue
        pos, prev = 0, None
        while True:
            group = []
            while True:
                nid, pos = node_at(s, pos)
                group.append(nid)
                m = re.compile(r"\s*&").match(s, pos)
                if not m:
                    break
                pos = m.end()
            if prev is not None:
                g.edges.extend((a, b) for a in prev for b in group)
            prev = group
            if not s[pos:].strip():
                break
            m = ARROW_TEXT_RE.match(s, pos) or ARROW_RE.match(s, pos)
            if not m or m.end() == pos:
                raise MermaidError(f"cannot read an arrow in: {s!r}")
            pos = m.end()
    return g


def shorten_labels(src: str) -> str:
    """Long labels make a diagram too wide to read on a page: keep a node's first line and
    drop the bracketed description of a subgraph title. The full text stays in the plan."""
    def node(m):
        label = m.group(2)
        if len(label) > 70 and re.search(r"<br\s*/?>", label):
            label = re.split(r"<br\s*/?>", label)[0]
        return m.group(1) + label + m.group(3)

    def sub(m):
        title = m.group(2)
        if len(title) > 60:
            title = re.sub(r"\s*\((?:[^()]|\([^()]*\))*\)\s*$", "", title)
        return m.group(1) + title + m.group(3)

    out = []
    for line in src.split("\n"):
        if re.match(r"^\s*subgraph\b", line):
            line = re.sub(r'^(\s*subgraph\s+\w+\s*\[\s*")(.*)("\s*\]\s*)$', sub, line)
        else:
            line = re.sub(r'([\[{(]")([^"]*)("[\]})])', node, line)
        out.append(line)
    return "\n".join(out)


def mm_label(text: str) -> str:
    return text.replace('"', "#quot;")


# --------------------------------------------------------------------------
# The plan: task rows, dependencies, gates
# --------------------------------------------------------------------------

class Task:
    def __init__(self, tid, cells, table, idx):
        m = ID_RE.fullmatch(tid)
        self.id, self.phase, self.lane, self.num = tid, int(m.group(1)), m.group(2), int(m.group(3))
        self.cells, self.table, self.idx = cells, table, idx
        self.title = plain(cells.get("title", ""))
        self.deps_text = plain(cells.get("deps", ""))
        self.deps: set[str] = set()       # node names in the phase graph
        self.gate_no = None                # this row is, or doubles as, a gate
        self.area = None

    @property
    def short(self) -> str:
        return self.id.split("-", 1)[1]


ROLES = [("title", r"^(task|work package)"), ("lane", r"^lane$"), ("deps", r"^depends"),
         ("output", r"^output"), ("accept", r"^acceptance"), ("runs", r"^(runs on|needs)")]


def table_roles(header: list[str]) -> dict[str, int]:
    roles = {}
    for i, h in enumerate(header):
        hn = plain(h).lower()
        if hn == "id":
            roles["id"] = i
        for role, pat in ROLES:
            if role not in roles and re.search(pat, hn):
                roles[role] = i
    return roles


def read_tasks(root: Section) -> dict[str, Task]:
    tasks: dict[str, Task] = {}
    for b in root.all_blocks():
        if b.kind != "table":
            continue
        roles = table_roles(b.header)
        if "id" not in roles:
            continue
        b.task_rows = []
        for r in b.rows:
            tid = plain(r[roles["id"]]) if len(r) > roles["id"] else ""
            if not ID_RE.fullmatch(tid):
                continue
            if "deps" not in roles or "title" not in roles:
                fail(f"The task table at line {b.line} has task ids but no 'Depends on' or title column; "
                     f"header is: {' | '.join(b.header)}")
            cells = {k: (r[i] if i < len(r) else "") for k, i in roles.items()}
            if tid in tasks:
                warn(f"{tid} appears in two tables (lines {tasks[tid].table.line} and {b.line}); the first is used")
                continue
            tasks[tid] = Task(tid, cells, b, len(b.task_rows))
            b.task_rows.append(tid)
    return tasks


class PhaseGraph:
    """Tasks and gates of one phase with their dependencies."""

    def __init__(self, phase: int):
        self.phase = phase
        self.deps: dict[str, set[str]] = {}   # node -> nodes it depends on
        self.tasks: dict[str, Task] = {}
        self.unresolved: list[tuple[str, str]] = []
        self._anc: dict[str, set[str]] = {}

    def is_gate(self, n: str) -> bool:
        return n not in self.tasks

    def ancestors(self, n: str) -> set[str]:
        memo = self._anc
        if n in memo:
            return memo[n]
        path: list[str] = []
        onpath: set[str] = set()

        def visit(x):
            if x in memo:
                return memo[x]
            onpath.add(x)
            path.append(x)
            acc = set()
            for d in sorted(self.deps.get(x, ())):
                if d in onpath:
                    cyc = path[path.index(d):] + [d]
                    fail("The plan's dependencies contain a cycle: " + " → ".join(cyc))
                acc.add(d)
                acc |= visit(d)
            path.pop()
            onpath.discard(x)
            memo[x] = acc
            return acc

        return visit(n)


def build_phase_graph(phase: int, tasks: dict[str, Task], diagrams: list[MGraph]) -> PhaseGraph:
    pg = PhaseGraph(phase)
    mine = {t.id: t for t in tasks.values() if t.phase == phase}
    alias: dict[str, str] = {}  # task id -> gate node, for rows that are gates
    for t in mine.values():
        m = re.search(r"\bgate (G\d{1,2})\b", t.title)
        if t.lane == "G":
            t.gate_no = t.num
            alias[t.id] = f"G{t.num}"
        elif m:
            t.gate_no = int(m.group(1)[1:])  # the task doubles as a gate, e.g. "Contracts frozen (this is gate G0)"
    for t in mine.values():
        if t.lane != "G":
            pg.tasks[t.id] = t
    for t in mine.values():
        text = t.deps_text
        ids: set[str] = set()
        for m in re.finditer(r"P(\d)-([A-DXG])(\d{1,2})\s*(?:…|\.{3}|–|—|\bto\b)\s*(?:P\d-)?[A-DXG]?(\d{1,2})\b", text):
            lo, hi2 = int(m.group(3)), int(m.group(4))
            for o in mine.values():
                if o.lane == m.group(2) and lo <= o.num <= hi2 and int(m.group(1)) == phase:
                    ids.add(o.id)
        for m in re.finditer(r"\bP(\d)-([A-Z])(\d{1,2})\b", text):
            tid = m.group(0)
            if int(m.group(1)) != phase:
                continue  # a task of another phase is already done when this phase runs
            if tid not in mine:
                alt = f"P{m.group(1)}-{m.group(2)}{int(m.group(3)):02d}"
                alt2 = f"P{m.group(1)}-{m.group(2)}{int(m.group(3))}"
                tid = alt if alt in mine else alt2 if alt2 in mine else tid
            if tid in mine:
                ids.add(tid)
            else:
                pg.unresolved.append((t.id, m.group(0)))
        rows = t.table.task_rows
        if re.search(r"\ball\b[^,;]*\babove\b", text, re.I):
            ids |= {r for r in rows[: t.idx] if r in mine}
        elif re.search(r"\ball\b[^,;]*\bcore\b", text, re.I):
            ids |= {r for r in rows if r != t.id and r in mine
                    and not re.search(r"\((extended|stretch)\)", mine[r].title, re.I)}
        elif re.search(r"\ball\b", text, re.I) and not ids:
            warn(f"{t.id}: 'Depends on' says \"{text}\"; read as every row above it in the same table")
            ids |= {r for r in rows[: t.idx] if r in mine}
        ids.discard(t.id)
        gates = {f"G{int(g)}" for g in re.findall(r"\bG(\d{1,2})\b", ID_RE.sub(" ", text))}
        node = alias.get(t.id, t.id)
        pg.deps[node] = {alias.get(i, i) for i in ids} | gates
        for gname in gates:
            pg.deps.setdefault(gname, set())
    # a task that doubles as a gate (X02 = G0): everything that waits for the task waits for the gate
    for t in list(pg.tasks.values()):
        if t.gate_no is not None:
            gname = f"G{t.gate_no}"
            for n, ds in pg.deps.items():
                if t.id in ds and n != gname:
                    ds.discard(t.id)
                    ds.add(gname)
            pg.deps.setdefault(gname, set()).add(t.id)
    # gates without a row (later phases): what feeds them is read from the phase diagrams
    for g in diagrams:
        for u, v in g.edges:
            if re.fullmatch(r"G\d{1,2}", u) and g.is_gate(u) and f"P{phase}-{v}" in pg.tasks \
                    and not any(t.lane == "G" for t in mine.values()):
                pg.deps[f"P{phase}-{v}"].add(u)   # a gate that opens a package, as drawn in the phase diagram
                pg.deps.setdefault(u, set())
            if not re.fullmatch(r"G\d{1,2}", v) or not g.is_gate(v):
                continue
            if any(t.lane == "G" and t.num == int(v[1:]) for t in mine.values()):
                continue  # this gate has its own row in a table
            src = f"P{phase}-{u}"
            if src in pg.tasks:
                pg.deps.setdefault(v, set()).add(src)
            elif re.fullmatch(r"G\d{1,2}", u) and g.is_gate(u) and u != v:
                pg.deps.setdefault(v, set()).add(u)   # a gate that needs another gate
                pg.deps.setdefault(u, set())
    for n in list(pg.deps):
        pg.ancestors(n)
    return pg


# --------------------------------------------------------------------------
# Areas: the human-sized pieces of work
# --------------------------------------------------------------------------

class Area:
    def __init__(self, phase, lane, name, delivers):
        self.phase, self.lane, self.name, self.delivers = phase, lane, name, delivers
        self.tasks: list[str] = []
        self.tag = ""
        self.key = ""
        self.start = False


def phase_of(sec: Section):
    while sec is not None:
        m = re.search(r"\bPhase\s+(\d+)\b", sec.name)
        if m:
            return int(m.group(1))
        sec = sec.parent
    return None


def read_plan_areas(root: Section, tasks: dict[str, Task], graphs: dict[int, "PhaseGraph"]):
    """Work areas as the plan defines them: tables headed 'Area | Owner | Tasks inside | ...'."""
    areas: dict[int, list[Area]] = {ph: [] for ph in graphs}
    sources: list[str] = []
    for sec in root.walk():
        for b in sec.blocks:
            if b.kind != "table":
                continue
            hdr = [plain(h).lower() for h in b.header]
            ti = next((i for i, h in enumerate(hdr) if h.startswith("tasks")), None)
            if not hdr or not hdr[0].startswith("area") or ti is None:
                continue
            oi = next((i for i, h in enumerate(hdr) if h.startswith("owner") or h == "lane"), None)
            phase = phase_of(sec)
            if phase not in graphs:
                warn(f"Area table at line {b.line} is not inside a 'Phase N' section with tasks; ignored")
                continue
            sources.append(sec.ref())
            for r in b.rows:
                m = re.match(r"^\*\*(.+?)\*\*\s*(?:<br\s*/?>\s*(.*))?$", r[0].strip())
                name, delivers = (m.group(1), m.group(2) or "") if m else (plain(r[0]), "")
                name = re.sub(r"^[A-DX]\d{1,2}\s*·\s*", "", plain(name))
                owner = plain(r[oi]) if oi is not None and oi < len(r) else ""
                ml = re.search(r"\bLane\s+([A-D])\b", owner)
                area = Area(phase, ml.group(1) if ml else "X", name, delivers)
                for mt in re.finditer(r"\b(?:P(\d)-)?([A-DX])(\d{1,2})\b", plain(r[ti]) if ti < len(r) else ""):
                    tid = f"P{mt.group(1) or phase}-{mt.group(2)}{mt.group(3)}"
                    t = tasks.get(tid)
                    if t is None or t.phase != phase or t.lane == "G":
                        warn(f"Area '{name}' (line {b.line}) lists {tid}, which is not a task of Phase {phase}")
                    elif t.area is not None:
                        warn(f"{tid} is listed under two areas: '{t.area.name}' and '{name}'; the first is used")
                    else:
                        t.area = area
                        area.tasks.append(tid)
                if not ml and area.tasks:
                    lanes = [tasks[x].lane for x in area.tasks]
                    area.lane = max(set(lanes), key=lanes.count)
                areas[phase].append(area)
    for phase, pg in graphs.items():
        loose = sorted((t for t in pg.tasks.values() if t.area is None), key=lambda t: (t.lane != "X", t.lane, t.num))
        if loose and not areas[phase]:
            warn(f"Phase {phase} has no area table in the plan; its tasks are shown as one area per lane")
            for lane in sorted({t.lane for t in loose}):
                a = Area(phase, lane, "All hands" if lane == "X" else f"Lane {lane} in this phase", "")
                for t in loose:
                    if t.lane == lane:
                        t.area = a
                        a.tasks.append(t.id)
                areas[phase].append(a)
        elif loose:
            # the plan keeps set-up and contract tasks outside the lane areas: they form the start
            late = [t.id for t in loose if any(d in pg.tasks and tasks[d].area is not None for d in pg.deps.get(t.id, ()))]
            if late:
                warn(f"Phase {phase}: {', '.join(late)} come after lane work but are in no area table; "
                     f"they are shown under 'Start'. Add them to an area in the plan.")
            a = Area(phase, "X", "The start: set-up and contracts", "; ".join(t.title for t in loose) + ".")
            a.start = True
            for t in loose:
                t.area = a
                a.tasks.append(t.id)
            areas[phase].append(a)
    for phase, lst in areas.items():
        lst[:] = [a for a in lst if a.tasks]
        lst.sort(key=lambda a: "XABCD".index(a.lane))
        count: dict[str, int] = {}
        for a in lst:
            count[a.lane] = count.get(a.lane, 0) + 1
            a.tag = "All" if a.lane == "X" else f"{a.lane}{count[a.lane]}"
            a.key = f"p{phase}{a.lane}{count[a.lane]}"
            a.tasks.sort(key=lambda x: (tasks[x].lane != "X", tasks[x].lane, tasks[x].num))
    return areas, sorted(set(sources))


class AreaGraph:
    """Areas and gates of one phase: who starts after whom, who needs what.

    solid edge  x -> y: nothing in y can begin before x delivers ("starts after")
    dashed edge x -> y: y can begin, but part of it waits for x ("also needs")
    """

    def __init__(self, pg: PhaseGraph, areas: list[Area], tasks: dict[str, Task]):
        self.pg, self.areas, self.tasks = pg, areas, tasks
        self.by_key = {a.key: a for a in areas}
        gates = sorted((n for n in pg.deps if pg.is_gate(n)), key=lambda g: int(g[1:]))
        self.entry = [g for g in gates if not pg.deps[g]]      # passed before this phase starts
        self.gates = [g for g in gates if pg.deps[g]]          # passed inside this phase
        self.nodes = [a.key for a in areas] + self.gates
        self.items: dict[tuple[str, str], list[tuple[str, str]]] = {}
        owner = lambda n: tasks[n].area.key if n in pg.tasks else n
        for n, ds in pg.deps.items():
            y = owner(n)
            for d in sorted(ds):
                x = owner(d)
                if x != y and x not in self.entry:
                    self.items.setdefault((x, y), []).append((d, n))
        self.solid: set[tuple[str, str]] = set()
        self.dashed: set[tuple[str, str]] = set()
        for (x, y) in self.items:
            members_y = self.by_key[y].tasks if y in self.by_key else [y]
            src = set(self.by_key[x].tasks) if x in self.by_key else {x}
            blocks = all((pg.ancestors(m) & src) for m in members_y)
            (self.solid if blocks else self.dashed).add((x, y))
        self.solid_r = {(x, y) for (x, y) in self.solid if not self._path(x, y, self.solid, skip=(x, y))}
        # a need is listed unless the area already starts after that source anyway
        self.dashed_r = {(x, y) for (x, y) in self.dashed if not self._path(x, y, self.solid)}
        # step of an area: the first step in which at least one of its tasks can begin.
        # A task sits one step after the latest area or gate it waits for.
        entry, depth = set(self.entry), {}

        def step(n: str) -> int:
            if n not in depth:
                depth[n] = 0
                for d in pg.deps.get(n, ()):
                    if d in entry:
                        continue
                    crossing = n in pg.tasks and owner(d) != owner(n)
                    depth[n] = max(depth[n], step(d) + (1 if crossing else 0))
            return depth[n]

        raw = {a.key: min(step(t) for t in a.tasks) for a in areas}
        ranks = {v: i for i, v in enumerate(sorted(set(raw.values())))}
        self.level = {k: ranks[v] for k, v in raw.items()}
        self.blocking = [g for g in self.gates if any(x == g and y in self.by_key for (x, y) in self.solid | self.dashed)]

    @staticmethod
    def _path(a, b, edges, skip=None) -> bool:
        seen, stack = set(), [a]
        while stack:
            x = stack.pop()
            for (u, v) in edges:
                if u != x or (u, v) == skip or v in seen:
                    continue
                if v == b:
                    return True
                seen.add(v)
                stack.append(v)
        return False

    def source_titles(self, x: str, y: str) -> list[str]:
        out = []
        for d, _ in self.items.get((x, y), []):
            t = self.tasks[d].title if d in self.pg.tasks else ""
            if t and t not in out:
                out.append(t)
        return out

    def feeds(self, key: str) -> list[str]:
        return [y for (x, y) in sorted(self.solid | self.dashed, key=lambda e: int(e[1][1:]) if e[1] not in self.by_key else 0)
                if x == key and y not in self.by_key]


# --------------------------------------------------------------------------
# Diagrams: Mermaid CLI
# --------------------------------------------------------------------------

def mermaid_config(font_px: int, wrap_px: int) -> dict:
    return {
        "theme": "redux", "look": "neo", "layout": "elk",
        "fontFamily": FONT, "fontSize": font_px,
        "themeVariables": {
            "fontFamily": FONT, "fontSize": f"{font_px}px", "nodeShadow": False, "dropShadow": "none",
            "lineColor": "#3a4256", "clusterBkg": "#f7f8fa", "clusterBorder": "#b9bfcc", "strokeWidth": 1.5,
        },
        "flowchart": {"theme": "redux", "look": "neo", "wrappingWidth": wrap_px, "minNodeWidth": 120,
                      "padding": 14, "useMaxWidth": False},
        "elk": {"mergeEdges": False},
        "deterministicIds": True, "deterministicIDSeed": "atlas",
    }


class Figure:
    def __init__(self, key, src, font_px=20, wrap_px=230, line=0):
        self.key, self.src, self.font_px, self.wrap_px, self.line = key, src, font_px, wrap_px, line
        self.path, self.w, self.h = None, 0.0, 0.0


def render_figures(figs: list[Figure], build: Path) -> None:
    mdir = build / "mmd"
    mdir.mkdir(parents=True, exist_ok=True)
    cmd = shlex.split(os.environ.get("MMDC", "")) or ["npx", "-y", "@mermaid-js/mermaid-cli@latest"]
    if shutil.which(cmd[0]) is None:
        fail(f"'{cmd[0]}' was not found. Install Node.js (for npx) or set MMDC to a mermaid-cli command.")

    def one(f: Figure) -> str:
        cfg = mermaid_config(f.font_px, f.wrap_px)
        digest = hashlib.sha1((f.src + json.dumps(cfg, sort_keys=True) + " ".join(cmd)).encode()).hexdigest()[:12]
        svg = mdir / f"{f.key}-{digest}.svg"
        if not svg.exists():
            src, cfile = mdir / f"{f.key}-{digest}.mmd", mdir / f"{f.key}-{digest}.json"
            src.write_text(f.src, encoding="utf-8")
            cfile.write_text(json.dumps(cfg), encoding="utf-8")
            base = cmd + ["-i", str(src), "-o", str(svg), "-b", "transparent", "-c", str(cfile), "-I", "m" + digest, "-q"]
            r = subprocess.run(base + ["--no-font-embed"], capture_output=True, text=True, timeout=600)
            if r.returncode != 0 and "no-font-embed" in (r.stderr + r.stdout):
                r = subprocess.run(base, capture_output=True, text=True, timeout=600)
            if r.returncode != 0 or not svg.exists():
                where = f" (Mermaid block at line {f.line} of the plan)" if f.line else ""
                return f"Diagram '{f.key}'{where} did not render:\n{(r.stderr or r.stdout).strip()[-1500:]}"
        text = svg.read_text(encoding="utf-8")
        m = re.search(r'viewBox="([^"]+)"', text)
        if not m:
            return f"Diagram '{f.key}': the SVG from Mermaid has no viewBox"
        _, _, w, h = (float(v) for v in m.group(1).split())
        f.path, f.w, f.h = svg, w, h
        return ""

    if not figs:
        return
    errors = [one(figs[0])]  # the first call may download the package; run it alone
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as ex:
        errors += list(ex.map(one, figs[1:]))
    errors = [e for e in errors if e]
    if errors:
        fail("\n\n".join(errors))


def content_mm(size: str) -> tuple[float, float]:
    w, h = PAGE_MM[size]
    return w - MARGIN_MM[1] - MARGIN_MM[3], h - MARGIN_MM[0] - MARGIN_MM[2]


def place(fig: Figure, reserve_mm: float = 14.0, sizes=("A4", "A3")):
    """Smallest page on which the figure's labels stay readable: (size, width mm, height mm, label pt)."""
    best = None
    for size in sizes:
        cw, ch = content_mm(size)
        s = min(cw * PX_PER_MM / fig.w, (ch - reserve_mm) * PX_PER_MM / fig.h, MAX_LABEL_PT / (fig.font_px * 0.75))
        best = (size, fig.w * s / PX_PER_MM, fig.h * s / PX_PER_MM, fig.font_px * 0.75 * s)
        if best[3] >= MIN_LABEL_PT:
            break
    else:
        warn(f"Diagram '{fig.key}' is {fig.w:.0f} px wide: its labels are only {best[3]:.1f} pt on an "
             f"{sizes[-1]} landscape page. Zoom in, or split the diagram.")
    return best


# --------------------------------------------------------------------------
# HTML
# --------------------------------------------------------------------------

class Doc:
    def __init__(self, footer: str):
        self.parts: list[str] = []
        self.pages: dict[str, tuple[str, str]] = {}
        self.toc: list[tuple[int, str, str]] = []
        self.footer = footer
        self.figs: list[tuple[Figure, tuple]] = []

    def page(self, key: str, header: str, size: str = "A4") -> str:
        name = f"{key}{size}"
        self.pages[name] = (header, size)
        return name

    def add(self, page: str, body: str, new_page: bool = False) -> None:
        cls = "pg np" if new_page else "pg"
        self.parts.append(f'<div class="{cls}" style="page:{page}">{body}</div>')

    def heading(self, level: int, anchor: str, number: str, title: str, src: str = "") -> str:
        self.toc.append((level, anchor, f"{number} {title}".strip()))
        tag = f'<span class="src">plan {esc(src)}</span>' if src else ""
        num = f'<span class="num">{esc(number)}</span>' if number else ""
        return f'<h{level} id="{anchor}">{num}{esc(title)}{tag}</h{level}>'


def css_str(s: str) -> str:
    return s.replace("\\", "\\\\").replace('"', '\\"')


def figure_html(doc: Doc, fig: Figure, size: str, w: float, h: float, pt: float, caption: str = "") -> str:
    doc.figs.append((fig, (size, w, h, pt)))
    cap = f'<div class="cap">{caption}</div>' if caption else ""
    return (f'<div class="fig"><img src="mmd/{fig.path.name}" style="width:{w:.1f}mm;height:{h:.1f}mm" '
            f'alt="{esc(fig.key)}">{cap}</div>')


def table_html(b: Block, cls: str = "", widths: list[float] | None = None) -> str:
    ncol = len(b.header)
    out = [f'<table class="t {cls}">']
    if widths:
        tot = sum(widths)
        out.append("<colgroup>" + "".join(f'<col style="width:{100 * w / tot:.2f}%">' for w in widths) + "</colgroup>")
    out.append("<thead><tr>" + "".join(f"<th>{inline(h)}</th>" for h in b.header) + "</tr></thead><tbody>")
    for r in b.rows:
        r = (r + [""] * ncol)[:ncol]
        tds = []
        for c in r:
            p = plain(c)
            m = re.match(r"^([A-D])\s*·\s", p)
            k = ""
            if m:
                k = f' class="lane {lane_cls(m.group(1))}"'
            elif re.match(r"^G\d{1,2}\b", p) and c.startswith("**"):
                k = ' class="gatecell"'
            tds.append(f"<td{k}>{inline(c)}</td>")
        out.append("<tr>" + "".join(tds) + "</tr>")
    out.append("</tbody></table>")
    return "".join(out)


def blocks_html(blocks: list[Block], table_cls: str = "", on_mermaid=None) -> str:
    out = []
    for b in blocks:
        if b.kind == "para":
            out.append(f"<p>{inline(b.text)}</p>")
        elif b.kind == "list":
            tag = "ol" if b.ordered else "ul"
            attr = f' start="{b.start}"' if b.ordered and b.start != 1 else ""
            lis = []
            for text, children in b.items:
                m = re.match(r"^\[( |x|X)\]\s+(.*)$", text)
                if m:
                    box = '<span class="box">' + ("✓" if m.group(1) != " " else "") + "</span>"
                    lis.append(f'<li class="chk">{box}{inline(m.group(2))}{blocks_html(children)}</li>')
                else:
                    lis.append(f"<li>{inline(text)}{blocks_html(children)}</li>")
            out.append(f"<{tag}{attr}>" + "".join(lis) + f"</{tag}>")
        elif b.kind == "table":
            out.append(table_html(b, table_cls))
        elif b.kind == "code":
            if b.lang == "mermaid" and on_mermaid is not None:
                out.append(on_mermaid(b))
            else:
                width = max((len(l) for l in b.text.split("\n")), default=0)
                size = min(8.2, 262 / max(width, 1) / 0.2124)  # Menlo is 0.602 em wide: keep the widest line on the page
                out.append(f'<pre style="font-size:{max(size, 6.5):.2f}pt">{esc(b.text)}</pre>')
        elif b.kind == "quote":
            out.append("<blockquote>" + blocks_html(b.items[0][1]) + "</blockquote>")
    return "".join(out)


CSS = r"""
:root{--ink:#1d2433;--muted:#4a5368;--rule:#d3d7e0;--panel:#f3f4f7;}
.lA{--f:@Af;--s:@As;--i:@Ai}.lB{--f:@Bf;--s:@Bs;--i:@Bi}.lC{--f:@Cf;--s:@Cs;--i:@Ci}
.lD{--f:@Df;--s:@Ds;--i:@Di}.lX{--f:@Xf;--s:@Xs;--i:@Xi}
@page{size:297mm 210mm;margin:15mm 12mm 13mm 12mm;
 @top-left{content:"ATLAS Build Graph";font:700 7.5pt @FONT;color:#1d2433;vertical-align:bottom;padding-bottom:2mm;border-bottom:.4pt solid #c5cad6}
 @top-right{content:"";font:7.5pt @FONT;color:#4a5368;vertical-align:bottom;padding-bottom:2mm;border-bottom:.4pt solid #c5cad6}
 @bottom-left{content:"@FOOTER";font:7pt @FONT;color:#4a5368;vertical-align:top;padding-top:2mm}
 @bottom-right{content:"Page " counter(page) " of " counter(pages);font:7pt @FONT;color:#4a5368;vertical-align:top;padding-top:2mm}}
@PAGES
html{-webkit-print-color-adjust:exact;print-color-adjust:exact}
body{margin:0;font:9.2pt/1.42 @FONT;color:var(--ink)}
.np{break-before:page}
h1{font-size:19pt;line-height:1.15;margin:0 0 4mm;padding-top:2.5mm;border-top:2.2pt solid var(--ink);letter-spacing:-.01em}
h2{font-size:12.5pt;line-height:1.2;margin:4mm 0 2mm;break-after:avoid}
h3{font-size:10pt;margin:3.5mm 0 1.5mm;break-after:avoid}
h1 .num,h2 .num{color:var(--muted);font-weight:400;margin-right:2.2mm}
.src{float:right;font-size:7.5pt;font-weight:400;color:var(--muted);letter-spacing:0;padding-top:1.2mm}
h1 .src{padding-top:3.4mm}
p{margin:0 0 2mm}ul,ol{margin:0 0 2mm;padding-left:5mm}li{margin-bottom:.8mm}
code{font:.9em @MONO;background:var(--panel);padding:0 .9mm;border-radius:1.2pt;white-space:nowrap}
code.tid{background:var(--f,#eef1f6);color:var(--i,#1d2433);box-shadow:inset 0 0 0 .4pt var(--s,#7a8499)}
code.tid.gt{background:#1d2433;color:#fff;box-shadow:none}
pre{font-family:@MONO;line-height:1.36;background:var(--panel);padding:3mm 3.5mm;margin:0 0 3mm;border-radius:2pt;white-space:pre;break-inside:avoid}
.lead{font-size:10.5pt;line-height:1.45;max-width:190mm}
.note{font-size:8pt;color:var(--muted);margin:1mm 0 2.5mm}
.intro{font-size:8.6pt;margin:0 0 2.5mm}
table.t{border-collapse:collapse;width:100%;font-size:8.2pt;line-height:1.36;margin:0 0 3mm}
table.t thead{display:table-header-group}
table.t th{background:var(--panel);text-align:left;font-weight:700;padding:1.5mm 2mm;border-bottom:.7pt solid var(--ink);vertical-align:bottom}
table.t td{padding:1.5mm 2mm;border-bottom:.4pt solid var(--rule);vertical-align:top}
table.t tr{break-inside:avoid}
table.t td.lane{background:var(--f);color:var(--i);border-left:2.2pt solid var(--s)}
table.t td.gatecell b{white-space:normal}
table.fixed{table-layout:fixed}
.fig{margin:1mm 0 2.5mm;break-inside:avoid}.fig img{display:block;margin:0 auto}
.cap{font-size:8pt;color:var(--muted);margin-top:1.5mm}
.tag{display:inline-block;font:700 7.3pt @FONT;padding:.1mm 1.4mm;border-radius:1.4pt;background:var(--f);color:var(--i);box-shadow:inset 0 0 0 .5pt var(--s);white-space:nowrap;margin-right:1mm}
.gate{display:inline-block;font:700 7.3pt @FONT;padding:.25mm 2.6mm;background:#1d2433;color:#fff;white-space:nowrap;margin-right:1mm;clip-path:polygon(1.6mm 0,calc(100% - 1.6mm) 0,100% 50%,calc(100% - 1.6mm) 100%,1.6mm 100%,0 50%)}
.it{color:var(--muted)}
table.board{border-collapse:separate;border-spacing:1.2mm 1mm;width:100%;table-layout:fixed;margin:0 -1.2mm 2mm}
table.board th{font-size:8.2pt;text-align:left;padding:1.4mm 2mm;background:var(--f,#fff);color:var(--i,#1d2433);border-top:2.2pt solid var(--s,#1d2433);vertical-align:bottom}
table.board th.lv,table.board td.lv{width:9mm;background:none;border:0;color:var(--muted);font-size:7.5pt;text-align:center;vertical-align:middle}
table.board th.gcol{background:#1d2433;color:#fff;border-top-color:#1d2433}
table.board td{vertical-align:top;padding:0}
table.board tr{break-inside:avoid}
.card{background:var(--f);color:var(--i);border-left:2.2pt solid var(--s);padding:1.1mm 1.6mm;margin-bottom:1mm;font-size:8pt;line-height:1.24;break-inside:avoid}
.card .cn{font-weight:700}.card .cw{font-size:7.5pt;line-height:1.45;margin-top:.4mm;color:var(--ink)}
.seg{white-space:nowrap;margin-right:1.6mm}.seg .tag,.seg .gate{margin-right:.4mm}
.gcard{font-size:8pt;line-height:1.3;margin-bottom:1.2mm;padding:1.2mm 0}
.an{font-weight:700;display:block;margin-bottom:.6mm}
td.ac{border-left:2.2pt solid var(--s)}
.ids{font:7.2pt/1.4 @MONO;color:var(--muted)}
.cover{display:grid;grid-template-columns:1.25fr 1fr;gap:12mm;margin-top:6mm}
.cover h1{font-size:34pt;border:0;padding:0;margin:0 0 2mm;letter-spacing:-.02em}
.cover .sub{font-size:13pt;color:var(--muted);margin:0 0 7mm}
.kicker{font:700 7.5pt @FONT;letter-spacing:.09em;text-transform:uppercase;color:var(--muted);margin:0 0 2mm}
.legend{display:grid;grid-template-columns:auto 1fr;gap:1.8mm 3mm;align-items:center;font-size:8.6pt;margin-bottom:6mm}
.sw{display:inline-block;width:11mm;height:5mm;background:var(--f);box-shadow:inset 0 0 0 .9pt var(--s)}
.sw.hex{background:#1d2433;box-shadow:none;clip-path:polygon(1.8mm 0,calc(100% - 1.8mm) 0,100% 50%,calc(100% - 1.8mm) 100%,1.8mm 100%,0 50%)}
.ln{display:inline-block;width:11mm;border-top:1.3pt solid #3a4256;vertical-align:middle}.ln.dash{border-top-style:dashed}
.toc{list-style:none;padding:0;margin:0;font-size:9pt}.toc li{display:flex;gap:2mm;margin:0 0 1.1mm}
.toc li.l2{padding-left:5mm;font-size:8.4pt;color:var(--muted)}
.toc a{color:inherit;text-decoration:none;flex:1;border-bottom:.4pt dotted #aab0bf}.toc .pn{min-width:6mm;text-align:right}
.keep{break-inside:avoid}
.quoted{border-left:1.5pt solid var(--rule);padding-left:3.5mm;margin:0 0 3mm;font-size:8.6pt}
.quoted .kicker{margin-bottom:1mm}
.cols2{columns:2;column-gap:9mm}.cols2 table.t{break-inside:avoid}
.chk{list-style:none;position:relative}.chk .box{position:absolute;left:-4.6mm;top:.5mm;width:2.6mm;height:2.6mm;border:.6pt solid var(--ink);font-size:6pt;line-height:2.6mm;text-align:center}
.gatecard{break-inside:avoid;margin:0 0 3mm}.gatecard p{margin:0 0 1mm}
blockquote{margin:0 0 2mm;padding-left:3mm;border-left:1.5pt solid var(--rule);color:var(--muted)}
"""


def build_css(doc: Doc, palette: dict) -> str:
    css = CSS.replace("@FONT", FONT).replace("@MONO", MONO).replace("@FOOTER", css_str(doc.footer))
    for l in "ABCDX":
        p = palette.get("lane" + l, PALETTE["lane" + l])
        css = css.replace(f"@{l}f", p["fill"]).replace(f"@{l}s", p["stroke"]).replace(f"@{l}i", p["color"])
    rules = []
    for name, (header, size) in doc.pages.items():
        w, h = PAGE_MM[size]
        sz = f"size:{w}mm {h}mm;" if size != "A4" else ""
        if header is None:
            rules.append(f'@page {name}{{{sz}@top-left{{content:"";border:0}}@top-right{{content:"";border:0}}}}')
        else:
            rules.append(f'@page {name}{{{sz}@top-right{{content:"{css_str(header)}"}}}}')
    return css.replace("@PAGES", "\n".join(rules))


# --------------------------------------------------------------------------
# Content
# --------------------------------------------------------------------------

def area_ref(ag: AreaGraph, key: str, with_name: bool = False) -> str:
    if key in ag.by_key:
        a = ag.by_key[key]
        name = f" {esc(a.name)}" if with_name else ""
        return f'<span class="tag {lane_cls(a.lane)}">{esc(a.tag)}</span>{name}'
    return f'<span class="gate">{esc(key)}</span>'


def phase_graph_source(ag: AreaGraph, gate_names: dict[int, str], palette: dict) -> str:
    out = ["flowchart LR"]
    shown = {a.key for a in ag.areas} | set(ag.blocking)
    for a in ag.areas:
        fed = ag.feeds(a.key)
        extra = ("<br/>feeds " + ", ".join(fed)) if fed else ""
        out.append(f'  {a.key}["{mm_label(a.tag + "  " + a.name)}{extra}"]')
    for gname in ag.blocking:
        nm = gate_names.get(int(gname[1:]))
        out.append(f'  {gname}{{{{"{mm_label(gname + ("<br/>" + nm if nm else ""))}"}}}}')
    order = {n: i for i, n in enumerate(ag.nodes)}
    key = lambda e: (order[e[0]], order[e[1]])
    for (x, y) in sorted(ag.solid_r, key=key):
        if x in shown and y in shown:
            out.append(f"  {x} --> {y}")
    for (x, y) in sorted(ag.dashed_r, key=key):
        if x in shown and y in shown:
            out.append(f"  {x} -.-> {y}")
    for name in ["laneA", "laneB", "laneC", "laneD", "laneX", "gate"]:
        p = palette.get(name, PALETTE[name])
        out.append(f'  classDef {name} fill:{p["fill"]},stroke:{p["stroke"]},color:{p["color"]};')
    for l in "XABCD":
        keys = [a.key for a in ag.areas if a.lane == l]
        if keys:
            out.append(f'  class {",".join(keys)} lane{l};')
    if ag.blocking:
        out.append(f'  class {",".join(ag.blocking)} gate;')
    return "\n".join(out) + "\n"


def board_html(ag: AreaGraph, lane_names: dict[str, str]) -> str:
    lanes = [l for l in "ABCD" if any(a.lane == l for a in ag.areas)]
    head = ['<th class="lv">Step</th>']
    for l in lanes:
        head.append(f'<th class="{lane_cls(l)}">{esc(f"Lane {l} · {lane_names.get(l, chr(32))}".rstrip(" ·"))}</th>')

    def card(a: Area) -> str:
        after = [x for (x, y) in sorted(ag.solid_r) if y == a.key]
        needs = [x for (x, y) in sorted(ag.dashed_r) if y == a.key]
        segs = [f'<span class="seg">{word} {" ".join(area_ref(ag, x) for x in lst)}</span>'
                for word, lst in (("after", after), ("needs", needs), ("feeds", ag.feeds(a.key))) if lst]
        extra = f'<div class="cw">{" ".join(segs)}</div>' if segs else ""
        return (f'<div class="card {lane_cls(a.lane)}"><div class="cn"><span class="tag {lane_cls(a.lane)}">'
                f'{esc(a.tag)}</span>{esc(a.name)}</div>{extra}</div>')

    rows = []
    for lv in range(max(ag.level.values(), default=0) + 1):
        here = [a for a in ag.areas if ag.level[a.key] == lv]
        shared = [a for a in here if a.lane == "X"]
        if shared:   # work for all hands runs across every lane
            rows.append(f'<tr><td class="lv">{lv + 1}</td><td colspan="{len(lanes)}">'
                        + "".join(card(a) for a in shared) + "</td></tr>")
        if any(a.lane != "X" for a in here):
            cells = "".join("<td>" + "".join(card(a) for a in here if a.lane == l) + "</td>" for l in lanes)
            rows.append(f'<tr><td class="lv">{"" if shared else lv + 1}</td>{cells}</tr>')
    return ('<table class="board"><thead><tr>' + "".join(head) + "</tr></thead><tbody>"
            + "".join(rows) + "</tbody></table>")


def area_table_html(ag: AreaGraph, lane: str) -> str:
    rows = []
    for a in ag.areas:
        if a.lane != lane:
            continue
        after = [x for (x, y) in sorted(ag.solid_r) if y == a.key]
        needs = [x for (x, y) in sorted(ag.dashed_r) if y == a.key]
        gives = [y for (x, y) in sorted(ag.solid | ag.dashed)
                 if x == a.key and y in ag.by_key and ag.by_key[y].lane != a.lane]
        feeds = ag.feeds(a.key)

        def with_items(x, y):
            its = "; ".join(esc(t) for t in ag.source_titles(x, y))
            return f'<div>{area_ref(ag, x if y == a.key else y)}<span class="it">{its}</span></div>'

        ids = [a.tasks[0]] + [ag.tasks[t].short for t in a.tasks[1:]]
        n = len(a.tasks)
        rows.append(
            f'<tr class="{lane_cls(a.lane)}"><td class="ac"><span class="an"><span class="tag {lane_cls(a.lane)}">'
            f'{esc(a.tag)}</span>{esc(a.name)}</span>{inline(a.delivers)}</td>'
            f'<td>{" ".join(area_ref(ag, x) for x in after) or "<span class=it>nothing in this phase</span>"}</td>'
            f'<td>{"".join(with_items(x, a.key) for x in needs) or "<span class=it>–</span>"}</td>'
            f'<td>{"".join(with_items(a.key, y) for y in gives) or "<span class=it>–</span>"}</td>'
            f'<td>{" ".join(area_ref(ag, g) for g in feeds) or "<span class=it>–</span>"}</td>'
            f'<td><span class="ids">{n} task{"s" if n != 1 else ""}: {esc(", ".join(ids))}</span></td></tr>')
    head = "".join(f"<th>{h}</th>" for h in ["Area and what it delivers", "Starts after", "Also needs, before it can finish",
                                              "Hands over to other lanes", "Feeds gate", "Inside, in the plan"])
    cols = "".join(f'<col style="width:{w}%">' for w in [30, 11, 20, 20, 7, 12])
    return f'<table class="t fixed"><colgroup>{cols}</colgroup><thead><tr>{head}</tr></thead><tbody>{"".join(rows)}</tbody></table>'


def main() -> None:
    ap = argparse.ArgumentParser(description="Build the ATLAS build graph PDF from the master plan.")
    ap.add_argument("--plan", type=Path, default=DEFAULT_PLAN)
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    ap.add_argument("--build-dir", type=Path,
                    default=Path(os.environ.get("ATLAS_PDF_BUILD", Path(tempfile.gettempdir()) / "atlas_build_graph_pdf")))
    ap.add_argument("--detail", action="store_true", help="append the task-level tables and diagrams")
    args = ap.parse_args()
    build: Path = args.build_dir
    build.mkdir(parents=True, exist_ok=True)
    if not args.plan.exists():
        fail(f"Plan not found: {args.plan}")

    lines = args.plan.read_text(encoding="utf-8").split("\n")
    root = build_sections(parse_blocks(lines))
    bp = find_section(root, r"^Build plan\b", "build plan section ('## 9. Build plan')", deep=True)
    s_lanes = find_section(bp, r"four lanes", "'The four lanes' section")
    s_shared = find_section(bp, r"shared resources", "'Shared resources' section")
    s_gates = find_section(bp, r"phases and gates", "'Phases and gates' section")
    s_org = find_section(bp, r"how the plan is organised", "", required=False)
    s_agents = find_section(bp, r"AI coding agents", "'Working with AI coding agents' section")
    s_check = find_section(root, r"gate checklists", "'Gate checklists' section", deep=True)
    phases: dict[int, Section] = {}
    for c in bp.children:
        m = re.match(r"^Phase\s+(\d+)\b", c.name)
        if m:
            phases[int(m.group(1))] = c
    if not phases:
        fail(f"No 'Phase N' sections found under '{bp.title}' (line {bp.line}).")

    # palette from the plan's own diagrams
    palette = {k: dict(v) for k, v in PALETTE.items()}
    seen_defs = set()
    for b in bp.all_blocks():
        if b.kind == "code" and b.lang == "mermaid":
            for m in re.finditer(r"classDef\s+(\w+)\s+([^;\n]+)", b.text):
                if m.group(1) in seen_defs:
                    continue
                seen_defs.add(m.group(1))
                props = dict(p.split(":", 1) for p in m.group(2).split(",") if ":" in p)
                palette.setdefault(m.group(1), {}).update({k.strip(): v.strip() for k, v in props.items()
                                                           if k.strip() in ("fill", "stroke", "color")})

    # tasks and checks
    tasks = read_tasks(root)
    raw_counts = {}
    for l in lines:
        m = re.match(r"^\|\s*`?P(\d)-[A-DXG]\d{1,2}`?\s*\|", l)
        if m:
            raw_counts[int(m.group(1))] = raw_counts.get(int(m.group(1)), 0) + 1
    for ph, n in sorted(raw_counts.items()):
        got = sum(1 for t in tasks.values() if t.phase == ph)
        if got != n:
            fail(f"Phase {ph}: {n} lines in the plan start with a task id but {got} task rows were parsed. "
                 f"A task table is malformed or an id is repeated.")
    for ph in sorted(raw_counts):
        if ph not in phases:
            warn(f"The plan has P{ph} task rows but no 'Phase {ph}' section in the build plan")

    # gates: names, summaries, who is present, which phase
    gate_names: dict[int, str] = {}
    gate_pass: dict[int, str] = {}
    for b in s_gates.blocks:
        if b.kind == "table":
            for r in b.rows:
                m = re.match(r"^G(\d{1,2})\s+(.*)$", plain(r[0]))
                if m:
                    gate_names[int(m.group(1))] = m.group(2)
                    gate_pass[int(m.group(1))] = r[1] if len(r) > 1 else ""
    gate_present: dict[int, str] = {}
    checklists: list[tuple[int, list[Block]]] = []
    for b in s_check.blocks:
        m = re.match(r"^\*\*G(\d{1,2})\s+(.*?)\*\*\s*(?:\((.*)\))?", b.text) if b.kind == "para" else None
        if m:
            g = int(m.group(1))
            gate_names.setdefault(g, m.group(2))
            gate_present[g] = re.sub(r"^present:\s*", "", m.group(3) or "")
            checklists.append((g, [b]))
        elif checklists:
            checklists[-1][1].append(b)
    gate_phase: dict[int, int] = {}
    gate_pred: dict[int, set[int]] = {}
    gates_mmd = next((b for b in s_gates.blocks if b.kind == "code" and b.lang == "mermaid"), None)
    if gates_mmd is not None:
        try:
            gg = parse_mermaid(gates_mmd.text)
            for nid in gg.nodes:
                sid = gg.top_sub(nid)
                m = re.search(r"Phase\s+(\d+)", gg.subs[sid]["title"]) if sid else None
                if m and re.fullmatch(r"G\d{1,2}", nid):
                    gate_phase[int(nid[1:])] = int(m.group(1))
            for u, v in gg.edges:
                if re.fullmatch(r"G\d{1,2}", u) and re.fullmatch(r"G\d{1,2}", v):
                    gate_pred.setdefault(int(v[1:]), set()).add(int(u[1:]))
        except MermaidError as e:
            warn(f"Phases-and-gates diagram (line {gates_mmd.line}) could not be read for gate order: {e}")

    # lane names
    lane_names: dict[str, str] = {}
    lanes_table = next((b for b in s_lanes.blocks if b.kind == "table"), None)
    if lanes_table is None:
        fail(f"'{s_lanes.title}' (line {s_lanes.line}) has no table.")
    for r in lanes_table.rows:
        m = re.match(r"^([A-D])\s*·\s*(.*)$", plain(r[0]))
        if m:
            lane_names[m.group(1)] = m.group(2)

    # phase diagrams, phase graphs, areas
    phase_diagrams: dict[int, list[MGraph]] = {}
    plan_area_diagram: dict[int, tuple[Block, str, str]] = {}   # block, sentence before it, legend after it
    for ph, sec in phases.items():
        phase_diagrams[ph] = []
        for sub in sec.walk():
            for i, b in enumerate(sub.blocks):
                if b.kind != "code" or b.lang != "mermaid":
                    continue
                try:
                    g = parse_mermaid(b.text)
                except MermaidError as e:
                    warn(f"Diagram at line {b.line} could not be read for gate inputs: {e}")
                    continue
                phase_diagrams[ph].append(g)
                # a diagram whose boxes are not task ids is the plan's picture of the work areas
                boxes = [n for n in g.nodes if not g.is_gate(n)]
                if ph not in plan_area_diagram and len(boxes) >= 2 and not any(re.fullmatch(r"[A-DX]\d{2}", n) for n in boxes):
                    is_text = lambda x: x is not None and x.kind == "para" and not re.fullmatch(r"\*\*[^*]+\*\*", x.text)
                    before = sub.blocks[i - 1] if i > 0 else None
                    after = sub.blocks[i + 1] if i + 1 < len(sub.blocks) else None
                    plan_area_diagram[ph] = (b, before.text if is_text(before) else "",
                                             after.text if is_text(after) and len(after.text) < 300 else "")
    graphs = {ph: build_phase_graph(ph, tasks, phase_diagrams.get(ph, [])) for ph in sorted(raw_counts)}
    unresolved = [(a, b) for pg in graphs.values() for (a, b) in pg.unresolved]
    for a, b in unresolved:
        warn(f"{a} depends on {b}, which is not a task row in the plan")

    areas, area_refs = read_plan_areas(root, tasks, graphs)
    area_source = ", ".join(area_refs) if area_refs else "no area tables: one area per lane"
    agraphs = {ph: AreaGraph(graphs[ph], areas.get(ph, []), tasks) for ph in graphs}

    # ---- figures
    figs: list[Figure] = []
    f_gates = None
    if gates_mmd is not None:
        f_gates = Figure("gates", gates_mmd.text, line=gates_mmd.line)
        figs.append(f_gates)
    f_phase = {}
    for ph, ag in agraphs.items():
        if ph in plan_area_diagram:   # the plan draws the areas itself: use its diagram
            blk = plan_area_diagram[ph][0]
            f_phase[ph] = Figure(f"plan-areas-phase{ph}", shorten_labels(blk.text), font_px=24, wrap_px=200, line=blk.line)
        else:
            f_phase[ph] = Figure(f"areas-phase{ph}", phase_graph_source(ag, gate_names, palette))
        figs.append(f_phase[ph])
    detail_figs: dict[int, Figure] = {}
    if args.detail:
        for b in bp.all_blocks():
            if b.kind == "code" and b.lang == "mermaid" and b is not gates_mmd:
                detail_figs[id(b)] = Figure(f"plan-l{b.line}", shorten_labels(b.text), font_px=24, wrap_px=200, line=b.line)
                figs.append(detail_figs[id(b)])
    print(f"Rendering {len(figs)} diagrams with Mermaid ...", flush=True)
    render_figures(figs, build)

    # ---- document
    mtime = datetime.datetime.fromtimestamp(args.plan.stat().st_mtime)
    stamp = f"{mtime.day} {mtime:%B %Y}, {mtime:%H:%M}"
    doc = Doc(f"Generated from {args.plan.name} · plan last modified {stamp}")
    today = datetime.date.today()
    gmax = max(gate_names, default=0)

    def phase_title(ph: int) -> str:
        return phases[ph].name if ph in phases else f"Phase {ph}"

    # cover
    pcover = doc.page("cover", None)
    lane_rows = "".join(
        f'<span class="sw {lane_cls(l)}"></span><span><b>Lane {l}</b> · {esc(lane_names.get(l, ""))}</span>' for l in LANES)
    toc_slot = "@@TOC@@"
    cover = f"""
<div class="cover"><div>
<p class="kicker">StormHacks 2026 · team handout</p>
<h1 id="top">ATLAS Build Graph</h1>
<p class="sub">Who builds what, what can run side by side, what has to wait, and the gates in between.</p>
<p class="lead">This is the build plan cut for people. Each box is one area of work that one person owns,
about the size of “implement hyperbolic terrain generation”, not a single task. For every phase it shows the
areas of each lane, which of them can start at the same time, which one waits for another, what is handed
from one person to the next, and the gate each area feeds.</p>
<p>Everything on these pages is read from <code>documents/{esc(args.plan.name)}</code> when the PDF is built.
The work areas are the ones the plan defines ({esc(area_source)}); who waits for whom is worked out from the
dependencies of the tasks inside each area. The tasks themselves, with their outputs and acceptance tests,
stay in the plan (§9 and §10) for the coding agents. Work is ordered by dependencies and gates only.</p>
<p class="kicker" style="margin-top:6mm">How to read an area</p>
<ul>
<li><b>Starts after</b> (on the boards: <b>after</b>): nothing inside the area can begin until the named area or gate is done.</li>
<li><b>Also needs</b> (<b>needs</b>): the area can start, but one part of it waits for something from another area.</li>
<li><b>Feeds</b>: the gates at which the area's work is checked.</li>
<li><b>Hands over to</b>: what another lane is waiting for from this area. These are the moments to talk.</li>
<li>A tag such as <span class="tag lB">B2</span> is lane B, second area of that phase. Task ids such as
<code>P1-B04</code> (phase 1, lane B, number 04) only point into the plan. Gates are G0 to G{gmax}.</li>
</ul>
<p class="note">Generated {today.day} {today:%B %Y} from {esc(args.plan.name)} (plan last modified {stamp}).</p>
</div><div>
<p class="kicker">Legend</p>
<div class="legend">{lane_rows}
<span class="sw lX"></span><span><b>All hands</b> · shared work</span>
<span class="sw hex"></span><span><b>Gate</b> · where the lanes meet and a check is passed</span>
<span class="ln"></span><span><b>Arrow</b> · the area at its head needs something from the area at its tail</span>
</div>
<p class="kicker">Contents</p>
{toc_slot}
</div></div>"""
    doc.add(pcover, cover)

    # 1 big picture
    sec_no = 1
    p1 = doc.page("s1", "1 · The big picture")
    body = doc.heading(1, "s1", "1", "The big picture")
    gate_blocks = [b for b in s_gates.blocks if b is not gates_mmd]
    sub = doc.heading(2, "s1-1", "1.1", "Phases and gates", s_gates.ref())
    if f_gates is not None:
        size, w, h, pt = place(f_gates, reserve_mm=30)
        fill = h / content_mm(size)[1]
        gp = doc.page("s1", "1 · The big picture", size)
        gate_html = sub + figure_html(doc, f_gates, size, w, h, pt)
        if fill < 0.5:
            doc.add(gp, body + gate_html + blocks_html(gate_blocks), new_page=True)
        else:
            doc.add(gp, body + gate_html, new_page=True)
            doc.add(p1, blocks_html(gate_blocks), new_page=True)
    else:
        doc.add(p1, body + sub + blocks_html(gate_blocks), new_page=True)
    lanes_html = doc.heading(2, "s1-2", "1.2", "The four lanes: who owns what", s_lanes.ref()) + blocks_html(s_lanes.blocks)
    if s_org is not None:
        lanes_html += doc.heading(2, "s1-3", "1.3", "How the plan is organised", s_org.ref()) + blocks_html(s_org.blocks)
    doc.add(p1, lanes_html, new_page=True)
    doc.add(p1, doc.heading(2, "s1-4", "1.4", "Shared resources and the rules that protect them", s_shared.ref())
            + '<div class="cols2">' + blocks_html(s_shared.blocks) + "</div>", new_page=True)

    # phases
    for ph in sorted(agraphs):
        ag = agraphs[ph]
        if not ag.areas:
            continue
        sec_no += 1
        sid = f"s{sec_no}"
        title = phase_title(ph)
        header = f"{sec_no} · {title}"
        pp = doc.page(sid, header)
        sec = phases.get(ph)
        html_parts = [doc.heading(1, sid, str(sec_no), title, sec.ref() if sec else "")]
        plan_notes = ""
        if sec is not None:
            intro = []
            for b in sec.blocks:
                if (b.kind == "code" and b.lang == "mermaid") or (
                        b.kind == "para" and re.fullmatch(r"\*\*[^*]+\*\*", b.text)):
                    break
                intro.append(b)
            if intro:
                plan_notes = f'<div class="quoted">{blocks_html(intro)}</div>'
        n_tasks = len(ag.pg.tasks)
        mine_g = [g for g, p in gate_phase.items() if p == ph]
        before = sorted({p for g in mine_g for p in gate_pred.get(g, ()) if gate_phase.get(p) != ph})
        if not before:
            before = sorted(int(g[1:]) for g in ag.entry
                            if not any(int(g[1:]) in gate_pred.get(int(o[1:]), ()) for o in ag.entry))
        opens = ""
        if before:
            pills = " and ".join(f'<span class="gate">G{g}</span>{esc(gate_names.get(g, ""))}' for g in before)
            opens = f'This phase opens when {pills} {"has" if len(before) == 1 else "have"} passed. '
        elif ph == min(agraphs):
            opens = "This phase opens the build. "
        html_parts.append(doc.heading(2, sid + "-1", f"{sec_no}.1", "Who can work side by side"))
        html_parts.append(
            f'<p class="intro">{opens}{len(ag.areas)} areas cover the {n_tasks} tasks of this phase. Across a row: areas that '
            f'can be worked on at the same point. Down a column: one person’s order. An area sits in the first step in '
            f'which part of it can begin; it is finished only when everything under “after” and “needs” has arrived.</p>')
        html_parts.append(board_html(ag, lane_names))
        doc.add(pp, "".join(html_parts), new_page=True)

        fig = f_phase[ph]
        if ph in plan_area_diagram:
            blk, before, after = plan_area_diagram[ph]
            lead_in = f"<p>{inline(before)}</p>" if before else ""
            size, w, h, pt = place(fig, reserve_mm=26 + (8 if before else 0))
            cap = (inline(after) + " " if after else "") + ("Drawn from the plan’s diagram; each box shows the name of the "
                                                             "area, and what it covers is in the tables that follow.")
            graph_html = (doc.heading(2, sid + "-2", f"{sec_no}.2", "The work areas, as the plan draws them", blk.sec.ref())
                          + lead_in + figure_html(doc, fig, size, w, h, pt, cap))
        else:
            size, w, h, pt = place(fig, reserve_mm=24)
            cap = ("Solid arrow: starts after. Dashed arrow: also needs, before it can finish. Colours are the lanes. "
                   "A dark hexagon is a gate that areas wait for; the other gates are named in “feeds”. "
                   "An arrow that is implied by two others is left out.")
            graph_html = doc.heading(2, sid + "-2", f"{sec_no}.2", "The area graph") + figure_html(doc, fig, size, w, h, pt, cap)
        doc.add(doc.page(sid, header, size), graph_html, new_page=True)

        parts = [doc.heading(2, sid + "-3", f"{sec_no}.3", "The areas, lane by lane")]
        for l in "XABCD":
            if not any(a.lane == l for a in ag.areas):
                continue
            name = "All hands" if l == "X" else f"Lane {l} · {lane_names.get(l, '')}"
            parts.append(f"<h3>{esc(name)}</h3>" + area_table_html(ag, l))
        doc.add(pp, "".join(parts), new_page=True)

        gate_list = sorted({int(g[1:]) for g in ag.gates} | {g for g, p in gate_phase.items() if p == ph})
        gate_list = [g for g in gate_list if gate_phase.get(g, ph) == ph]
        rows = []
        for g in gate_list:
            gname = f"G{g}"
            preds = sorted((x for (x, y) in ag.solid if y == gname),
                           key=lambda x: (x not in ag.by_key, ag.nodes.index(x) if x in ag.nodes else 0))
            extra = [f"G{p}" for p in sorted(gate_pred.get(g, ())) if f"G{p}" not in preds]
            if True:
                needs = "".join(f"<div>{area_ref(ag, x, with_name=True)}</div>" for x in preds if x in ag.by_key)
                needs += " ".join(area_ref(ag, x) for x in preds if x not in ag.by_key)
                needs += " ".join(f'<span class="gate">{x}</span>' for x in extra)
            rows.append(f'<tr><td class="gatecell"><span class="gate">{gname}</span><b>{esc(gate_names.get(g, ""))}</b></td>'
                        f'<td>{needs or "<span class=it>–</span>"}</td><td>{inline(gate_pass.get(g, ""))}</td>'
                        f'<td>{esc(gate_present.get(g, ""))}</td></tr>')
        if rows:
            cols = "".join(f'<col style="width:{w}%">' for w in [17, 33, 37, 13])
            doc.add(pp, '<div class="keep">' + doc.heading(2, sid + "-4", f"{sec_no}.4", "Gates of this phase", s_gates.ref())
                    + f'<table class="t fixed"><colgroup>{cols}</colgroup><thead><tr><th>Gate</th><th>Needs</th>'
                      f'<th>Passes when (summary)</th><th>Present</th></tr></thead><tbody>{"".join(rows)}</tbody></table></div>')
        if plan_notes:
            doc.add(pp, '<div class="keep">' + doc.heading(2, sid + "-5", f"{sec_no}.5", "What the plan says about this phase",
                                                           sec.ref()) + plan_notes + "</div>")

    # agents
    sec_no += 1
    sid = f"s{sec_no}"
    pa = doc.page(sid, f"{sec_no} · {s_agents.name}")
    doc.add(pa, doc.heading(1, sid, str(sec_no), s_agents.name, s_agents.ref()) + blocks_html(s_agents.blocks),
            new_page=True)

    # appendix: task-level detail
    if args.detail:
        sid = "sA"
        header = "Appendix · Task-level detail for agents"
        pd = doc.page(sid, header)
        doc.add(pd, doc.heading(1, sid, "A", "Task-level detail for agents")
                + '<p class="lead">The pages that follow are the plan’s own task diagrams and task tables. The tables '
                  'are unchanged: Output and Acceptance test are binding as written. In the diagrams a long label is cut '
                  'to its first line so that the graph fits a page; the full text is in the plan.</p>', new_page=True)

        TASK_W = {"id": 7, "title": 12, "lane": 4, "deps": 9, "output": 29, "accept": 29, "runs": 9}

        def task_table(b: Block) -> str:
            inv = {i: r for r, i in table_roles(b.header).items()}
            return table_html(b, "fixed", [TASK_W.get(inv.get(i, ""), 10) for i in range(len(b.header))])

        def fig_page(b: Block, lead_in: list[str], caption: str) -> None:
            fig = detail_figs[id(b)]
            size, w, h, pt = place(fig, reserve_mm=14 + 10 * len(lead_in) + (8 if caption else 0), sizes=("A4", "A3", "A2"))
            fp = doc.page(sid, header, size)
            doc.add(fp, "".join(lead_in) + figure_html(doc, fig, size, w, h, pt, caption), new_page=True)

        def emit(sec: Section, level: int) -> None:
            buf = [doc.heading(2, f"sA-{sec.line}", "", sec.title) if level <= 2 else f"<h3>{esc(sec.title)}</h3>"]
            fresh = level <= 2          # a phase starts on a new page
            blocks, i = sec.blocks, 0
            while i < len(blocks):
                b = blocks[i]
                if b.kind == "code" and b.lang == "mermaid" and b is not gates_mmd:
                    lead_in = []   # a heading, a label or a short paragraph stays with its diagram
                    while buf and len(lead_in) < 3 and (buf[-1].startswith("<h") or (
                            buf[-1].startswith("<p>") and len(buf[-1]) < 400)):
                        lead_in.insert(0, buf.pop())
                    if buf:
                        doc.add(pd, "".join(buf), new_page=fresh)
                        buf = []
                    nxt = blocks[i + 1] if i + 1 < len(blocks) else None
                    caption = ""
                    if nxt is not None and nxt.kind == "para" and len(nxt.text) < 300 \
                            and not re.fullmatch(r"\*\*[^*]+\*\*", nxt.text):
                        caption, i = inline(nxt.text), i + 1   # the legend line under a diagram
                    fig_page(b, lead_in, caption)
                    fresh = True
                elif b.kind == "table" and getattr(b, "task_rows", None):
                    buf.append(task_table(b))
                else:
                    buf.append(blocks_html([b]))
                i += 1
            if buf:
                doc.add(pd, "".join(buf), new_page=fresh)
            for c in sec.children:
                emit(c, level + 1)

        for ph in sorted(phases):
            emit(phases[ph], 2)
        seen = {id(x) for sec in phases.values() for x in sec.walk()}
        for tb in sorted({id(t.table): t.table for t in tasks.values()}.values(), key=lambda b: b.line):
            top = tb.sec
            if id(top) in seen:
                continue
            seen.add(id(top))
            label = top.parent.name if re.match(r"^work packages$", top.name, re.I) and top.parent is not None else top.name
            title = label if re.match(r"^work packages", label, re.I) else f"Work packages · {label}"
            body = "".join(task_table(b) if b.kind == "table" and getattr(b, "task_rows", None) else blocks_html([b])
                           for b in top.blocks)
            doc.add(pd, doc.heading(2, f"sA-{top.line}", "", title, top.ref()) + body, new_page=True)
        cards = "".join('<div class="gatecard">' + blocks_html(bl) + "</div>" for _, bl in checklists)
        doc.add(pd, doc.heading(2, "sA-chk", "", "Gate checklists", s_check.ref()) + f'<div class="cols2">{cards}</div>',
                new_page=True)

    # ---- write HTML, print, fill in page numbers
    def html_text(pages: dict[str, int]) -> str:
        items = []
        for level, anchor, text in doc.toc:
            if level > 1:
                continue
            pn = pages.get(anchor, "")
            items.append(f'<li class="l{level}"><a href="#{anchor}">{esc(text)}</a><span class="pn">{pn}</span></li>')
        body = "".join(doc.parts).replace(toc_slot, '<ul class="toc">' + "".join(items) + "</ul>")
        return (f'<!doctype html><html lang="en-GB"><head><meta charset="utf-8"><title>ATLAS Build Graph</title>'
                f"<style>{build_css(doc, palette)}</style></head><body>{body}</body></html>")

    chrome = os.environ.get("CHROME") or next((c for c in [
        "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
        "/Applications/Chromium.app/Contents/MacOS/Chromium",
        shutil.which("google-chrome") or "", shutil.which("chromium") or "", shutil.which("chromium-browser") or "",
        shutil.which("chrome") or ""] if c and Path(c).exists()), None)
    if not chrome:
        fail("Google Chrome was not found. Install it or set CHROME=/path/to/chrome.")
    html_file, pdf_file = build / "build_graph.html", build / "build_graph.pdf"

    def print_pdf() -> None:
        if pdf_file.exists():
            pdf_file.unlink()
        log = build / "chrome.log"
        with open(log, "w") as lf:
            proc = subprocess.Popen([chrome, "--headless=new", "--disable-gpu", "--no-first-run",
                                     "--no-default-browser-check", "--no-pdf-header-footer",
                                     "--generate-pdf-document-outline", f"--print-to-pdf={pdf_file}",
                                     html_file.as_uri()], stdout=lf, stderr=subprocess.STDOUT)
            # Chrome sometimes writes the file and then lingers; stop waiting once the file is complete.
            deadline, last, stable = time.time() + 240, -1, 0
            while proc.poll() is None and time.time() < deadline:
                time.sleep(0.5)
                size = pdf_file.stat().st_size if pdf_file.exists() else -1
                stable = stable + 1 if size > 0 and size == last else 0
                last = size
                if stable >= 8:
                    break
            if proc.poll() is None:
                proc.terminate()
                try:
                    proc.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    proc.kill()
        if not pdf_file.exists() or pdf_file.stat().st_size == 0:
            fail("Chrome did not write the PDF.\n" + log.read_text(errors="replace")[-2000:])

    html_file.write_text(html_text({}), encoding="utf-8")
    print("Printing with Chrome ...", flush=True)
    print_pdf()
    pages: dict[str, int] = {}
    if shutil.which("pdfinfo"):
        r = subprocess.run(["pdfinfo", "-dests", str(pdf_file)], capture_output=True, text=True)
        for m in re.finditer(r'^\s*(\d+)\s+\[.*?\]\s+"([^"]+)"', r.stdout, re.M):
            pages[m.group(2)] = int(m.group(1))
    if pages:
        html_file.write_text(html_text(pages), encoding="utf-8")
        print_pdf()
    args.out.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(pdf_file, args.out)

    # ---- report
    npages = ""
    if shutil.which("pdfinfo"):
        r = subprocess.run(["pdfinfo", str(args.out)], capture_output=True, text=True)
        m = re.search(r"^Pages:\s+(\d+)", r.stdout, re.M)
        npages = f"{m.group(1)} pages, " if m else ""
    print(f"\nWrote {args.out} ({npages}{args.out.stat().st_size / 1024:.0f} kB)")
    print(f"Plan: {args.plan} (modified {stamp}); work areas from: {area_source}")
    for ph in sorted(graphs):
        pg, ag = graphs[ph], agraphs[ph]
        edges = sum(len(d) for d in pg.deps.values())
        print(f"Phase {ph}: {raw_counts[ph]} task rows = {len(pg.tasks)} tasks + {raw_counts[ph] - len(pg.tasks)} gate rows; "
              f"{edges} dependency edges; {len(ag.areas)} areas; gates {', '.join(ag.gates) or 'none'}"
              f"{' (opens at ' + ', '.join(ag.entry) + ')' if ag.entry else ''}")
        for lv in range(max(ag.level.values(), default=0) + 1):
            row = [f"{a.tag} {a.name}" for a in ag.areas if ag.level[a.key] == lv]
            print(f"   step {lv + 1}: " + " | ".join(row))
    for fig, (size, w, h, pt) in doc.figs:
        print(f"Diagram {fig.key}: {fig.w:.0f}x{fig.h:.0f} px on {size} landscape, labels {pt:.1f} pt")
    print(f"Dependency ids that do not resolve: {len(unresolved)}")
    if NOTES:
        print(f"\n{len(NOTES)} warning(s):")
        for n in NOTES:
            print("  - " + n)


if __name__ == "__main__":
    main()
