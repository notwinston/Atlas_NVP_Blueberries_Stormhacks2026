"""pytest set-up for tools/kit.

test_kitglb.py is kept byte-identical to the kit-hero-assets branch. Two of its tests read
ATLAS/Assets/Atlas/Rendering/Look/AtlasPalette.cs, which only branches with the look (hub-phone-view,
kit-hero-assets, phase1-integration) carry; on a branch without it they are skipped instead of failing.
"""
from pathlib import Path

import pytest

PALETTE_CS = Path(__file__).resolve().parents[2] / "ATLAS" / "Assets" / "Atlas" / "Rendering" / "Look" / "AtlasPalette.cs"
NEEDS_PALETTE_CS = {"test_palette_slots_come_from_atlas_palette_cs", "test_material_names_map_to_slots"}


def pytest_collection_modifyitems(config, items):
    if PALETTE_CS.is_file():
        return
    skip = pytest.mark.skip(reason=f"{PALETTE_CS.name} is not on this branch")
    for item in items:
        if item.name in NEEDS_PALETTE_CS:
            item.add_marker(skip)
