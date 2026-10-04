#!/usr/bin/env python3
"""Check Markdown tables and code fences in the given files. Usage: python3 check/tables.py file.md [file.md ...]"""
import re, sys
bad = 0
for path in sys.argv[1:]:
    lines = open(path).read().split("\n")
    fence = False; i = 0
    while i < len(lines):
        l = lines[i]
        if l.strip().startswith("```"):
            fence = not fence; i += 1; continue
        if not fence and l.startswith("|"):
            j = i
            while j < len(lines) and lines[j].startswith("|"):
                j += 1
            def cols(x):
                x = re.sub(r"`[^`]*`", "", x)          # pipes inside code spans do not count
                return x.count("|") - x.count("\\|")
            n = cols(lines[i])
            if j - i < 2 or not re.match(r"^\|[\s:\-|]+\|\s*$", lines[i + 1]):
                print(f"{path}:{i+1}: table without a separator row"); bad += 1
            for k in range(i, j):
                if cols(lines[k]) != n:
                    print(f"{path}:{k+1}: {cols(lines[k])-1} cells, header has {n-1}: {lines[k][:70]}"); bad += 1
                if not lines[k].rstrip().endswith("|"):
                    print(f"{path}:{k+1}: row does not end with a pipe"); bad += 1
            i = j; continue
        i += 1
    if fence:
        print(f"{path}: unclosed code fence"); bad += 1
print("problems:", bad)
sys.exit(1 if bad else 0)
