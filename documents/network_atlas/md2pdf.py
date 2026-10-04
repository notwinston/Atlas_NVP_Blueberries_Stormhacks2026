"""Minimal Markdown -> HTML -> PDF (headless Edge or Chrome). No third-party packages.

    python md2pdf.py ATLAS_Hyperbolic_Network_Design.md          # writes .html and .pdf beside it

Supports: headings, paragraphs, **bold**, *italic*, `code`, [links](url), images, pipe tables, flat and
one-level nested lists, numbered lists, fenced code, horizontal rules.
"""
import html
import os
import re
import subprocess
import sys

CSS = """
@page { size: A4; margin: 16mm 15mm 18mm 15mm; }
body { font-family: "Segoe UI", Helvetica, Arial, sans-serif; font-size: 10pt; line-height: 1.45; color: #1f2933; }
h1 { font-size: 21pt; margin: 0 0 4pt; color: #102a43; }
h2 { font-size: 14pt; margin: 18pt 0 6pt; color: #102a43; border-bottom: 1.5px solid #d9e2ec; padding-bottom: 3pt; }
h3 { font-size: 11.5pt; margin: 13pt 0 4pt; color: #243b53; }
p { margin: 5pt 0; }
code { font-family: Consolas, "Courier New", monospace; font-size: 8.8pt; background: #f0f4f8; padding: 0 2pt; border-radius: 2pt; }
pre { background: #f0f4f8; padding: 7pt 9pt; border-radius: 4pt; font-size: 8.3pt; line-height: 1.3; overflow: hidden;
      white-space: pre-wrap; page-break-inside: avoid; }
pre code { background: none; padding: 0; font-size: inherit; }
table { border-collapse: collapse; width: 100%; margin: 6pt 0 9pt; font-size: 8.8pt; page-break-inside: auto; }
tr { page-break-inside: avoid; }
th { background: #e6eef6; text-align: left; font-weight: 600; }
th, td { border: 1px solid #cbd5e1; padding: 3pt 5pt; vertical-align: top; }
img { display: block; max-width: 72%; margin: 8pt auto 2pt; page-break-inside: avoid; }
hr { border: none; border-top: 1px solid #d9e2ec; margin: 12pt 0; }
ul, ol { margin: 4pt 0 6pt 16pt; padding: 0; }
li { margin: 1.5pt 0; }
em { color: #334e68; }
a { color: #1d5fa8; text-decoration: none; }
"""


def inline(text):
    parts = re.split(r"(`[^`]+`)", text)
    out = []
    for p in parts:
        if p.startswith("`") and p.endswith("`") and len(p) > 1:
            out.append("<code>" + html.escape(p[1:-1]) + "</code>")
            continue
        p = html.escape(p, quote=False)
        p = re.sub(r"!\[([^\]]*)\]\(([^)]+)\)", r'<img alt="\1" src="\2">', p)
        p = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", r'<a href="\2">\1</a>', p)
        p = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", p)
        p = re.sub(r"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])", r"<em>\1</em>", p)
        out.append(p)
    return "".join(out)


def convert(md):
    lines = md.split("\n")
    out, i = [], 0
    while i < len(lines):
        line = lines[i]
        s = line.strip()
        if s.startswith("```"):
            i += 1
            buf = []
            while i < len(lines) and not lines[i].strip().startswith("```"):
                buf.append(lines[i]); i += 1
            out.append("<pre><code>" + html.escape("\n".join(buf)) + "</code></pre>")
            i += 1
            continue
        if not s:
            i += 1; continue
        if re.fullmatch(r"-{3,}", s):
            out.append("<hr>"); i += 1; continue
        m = re.match(r"(#{1,6})\s+(.*)", s)
        if m:
            n = len(m.group(1)); out.append(f"<h{n}>{inline(m.group(2))}</h{n}>"); i += 1; continue
        if s.startswith("|"):
            rows = []
            while i < len(lines) and lines[i].strip().startswith("|"):
                rows.append(lines[i].strip()); i += 1
            cells = [[c.strip() for c in r.strip("|").split("|")] for r in rows]
            head, body = cells[0], [r for r in cells[1:] if not all(re.fullmatch(r":?-{3,}:?", c) for c in r)]
            t = ["<table><thead><tr>" + "".join(f"<th>{inline(c)}</th>" for c in head) + "</tr></thead><tbody>"]
            for r in body:
                t.append("<tr>" + "".join(f"<td>{inline(c)}</td>" for c in r) + "</tr>")
            out.append("".join(t) + "</tbody></table>")
            continue
        if re.match(r"(\s*)([-*]|\d+\.)\s+", line):
            out.append(list_block(lines, i));
            while i < len(lines) and (re.match(r"\s*([-*]|\d+\.)\s+", lines[i]) or
                                      (lines[i].startswith("  ") and lines[i].strip())):
                i += 1
            continue
        if s.startswith("!["):
            out.append(inline(s)); i += 1; continue
        buf = [s]; i += 1
        while i < len(lines) and lines[i].strip() and not re.match(r"\s*(#|\||```|[-*]\s|\d+\.\s|!\[)", lines[i]):
            buf.append(lines[i].strip()); i += 1
        out.append("<p>" + inline(" ".join(buf)) + "</p>")
    return "\n".join(out)


def list_block(lines, i):
    """One list starting at line i; nested items are indented by two or more spaces."""
    items = []  # (indent, ordered, text)
    while i < len(lines):
        m = re.match(r"(\s*)([-*]|\d+\.)\s+(.*)", lines[i])
        if m:
            items.append([len(m.group(1)), m.group(2)[-1] == ".", m.group(3)]); i += 1
        elif lines[i].startswith("  ") and lines[i].strip() and items:
            items[-1][2] += " " + lines[i].strip(); i += 1
        else:
            break
    html_out, stack = [], []
    for indent, ordered, text in items:
        level = 0 if indent < 2 else 1
        while len(stack) > level + 1:
            html_out.append("</li></%s>" % stack.pop())
        if len(stack) == level:
            tag = "ol" if ordered else "ul"; stack.append(tag); html_out.append(f"<{tag}>")
        else:
            html_out.append("</li>")
        html_out.append("<li>" + inline(text))
    while stack:
        html_out.append("</li></%s>" % stack.pop())
    return "".join(html_out)


def main():
    src = os.path.abspath(sys.argv[1])
    base = os.path.splitext(src)[0]
    with open(src, encoding="utf-8") as f:
        body = convert(f.read())
    title = os.path.basename(base).replace("_", " ")
    page = f'<!doctype html><html><head><meta charset="utf-8"><title>{title}</title><style>{CSS}</style></head><body>{body}</body></html>'
    with open(base + ".html", "w", encoding="utf-8") as f:
        f.write(page)
    browsers = [r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                r"C:\Program Files\Google\Chrome\Application\chrome.exe"]
    exe = next(b for b in browsers if os.path.exists(b))
    url = "file:///" + (base + ".html").replace("\\", "/")
    subprocess.run([exe, "--headless=new", "--disable-gpu", "--no-pdf-header-footer",
                    "--print-to-pdf=" + base + ".pdf", url], check=True, capture_output=True, timeout=120)
    print(base + ".pdf")


if __name__ == "__main__":
    main()
