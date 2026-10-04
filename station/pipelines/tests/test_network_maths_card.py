"""NET-11: the Network Atlas section of docs/maths.md.

Every formula row of section 3 of the design doc must be on the card and name, by full name, the tests the contract
(docs/contracts/network.md section 8) fixes for it, and each named test must exist. The card must make no claim
beyond section 8 of the design doc.
"""

from __future__ import annotations

import re
from pathlib import Path

import pytest

DESIGN = "documents/network_atlas/ATLAS_Hyperbolic_Network_Design.md"
CONTRACT = "docs/contracts/network.md"
CARD = "docs/maths.md"


def _read(repo_root: Path, rel: str) -> str:
    return (repo_root / rel).read_text(encoding="utf-8")


def _table_rows(text: str, start: str, end: str) -> list[list[str]]:
    a = text.index(start)
    b = text.index(end, a + len(start))
    lines = [ln for ln in text[a:b].splitlines() if ln.startswith("|")][2:]
    return [[c.strip() for c in ln.strip().strip("|").split("|")] for ln in lines]


def _section3_rows(repo_root: Path) -> list[str]:
    """First column (the quantity name) of each row of the design doc's section 3 table."""
    text = _read(repo_root, DESIGN)
    a = text.index("## 3. The model")
    b = text.index("**Default parameters", a)
    lines = [ln for ln in text[a:b].splitlines() if ln.startswith("|")][2:]
    return [ln.strip().strip("|").split(" | ")[0].strip() for ln in lines]


def _contract_rows(repo_root: Path) -> dict[str, list[str]]:
    text = _read(repo_root, CONTRACT)
    a = text.index("## 8. §3 formulas")
    b = text.index("## 9.", a)
    out: dict[str, list[str]] = {}
    for ln in [x for x in text[a:b].splitlines() if x.startswith("|")][2:]:
        label, tests = ln.strip().strip("|").split("|", 1)
        out[label.strip()] = re.findall(r"`([^`]+)`", tests)
    return out


def _test_exists(repo_root: Path, name: str) -> bool:
    if "::" in name:  # pytest node id
        path, func = name.split("::")
        f = repo_root / path
        return f.is_file() and re.search(rf"^def {re.escape(func)}\(", f.read_text(encoding="utf-8"), re.M) is not None
    if " › " in name:  # console test: <file> › <title>
        path, title = name.split(" › ", 1)
        f = repo_root / path
        return f.is_file() and title in f.read_text(encoding="utf-8")
    parts = name.split(".")  # NUnit full name
    cls, method = parts[-2], parts[-1]
    for f in (repo_root / "ATLAS/Assets/Atlas/Tests/Network").rglob("*.cs"):
        src = f.read_text(encoding="utf-8")
        if re.search(rf"\bclass {cls}\b", src) and re.search(rf"\b{method}\s*\(", src):
            return True
    return False


def test_every_section3_formula_names_an_existing_test(repo_root: Path) -> None:
    card = _read(repo_root, CARD)
    assert "## Network Atlas" in card
    section = card[card.index("## Network Atlas"):]
    card_rows = {r[0]: r for r in _table_rows(section, "| Quantity", "Default parameters")}
    contract = _contract_rows(repo_root)
    design_rows = _section3_rows(repo_root)
    assert len(design_rows) == 13
    assert len(contract) == len(design_rows)
    for quantity in design_rows:
        assert quantity in card_rows, f"formula row missing from the card: {quantity}"
        proved_by = card_rows[quantity][-1]
        named = re.findall(r"`([^`]+)`", proved_by)
        assert named, f"{quantity}: no test named"
        # the contract's section 8 row with the same position fixes the tests
        expected = list(contract.values())[design_rows.index(quantity)]
        assert named == expected, f"{quantity}: tests differ from contract section 8"
        for name in named:
            assert _test_exists(repo_root, name), f"{quantity}: named test does not exist: {name}"


def test_no_claim_beyond_section8(repo_root: Path) -> None:
    design = _read(repo_root, DESIGN)
    a = design.index("## 8. Claims we make")
    b = design.index("This follows the Master Plan", a)
    rows = [ln.strip().strip("|").split(" | ") for ln in design[a:b].splitlines() if ln.startswith("|")][2:]
    say = [r[0].strip() for r in rows]
    avoid = [r[1].strip() for r in rows]

    card = _read(repo_root, CARD)
    section = card[card.index("### Claims we make"):]
    say_block = section[section.index("We say:"):section.index("We do not say:")]
    avoid_block = section[section.index("We do not say:"):]
    # the card's claims are exactly the doc's: nothing added, nothing dropped
    say_card = re.findall(r"^- (.+)$", say_block, re.M)
    avoid_card = re.findall(r"^- (.+)$", avoid_block.split("Known gap")[0], re.M)
    assert say_card == say
    assert avoid_card == avoid
    # a forbidden claim never appears outside the "do not say" list
    outside = card.replace(avoid_block, "")
    for forbidden in ('"This is the real Internet"', '"Greedy routing always works"', '"Euclidean geometry cannot route"'):
        assert forbidden not in outside
