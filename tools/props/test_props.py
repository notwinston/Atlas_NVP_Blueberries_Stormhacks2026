"""Tests of the prop tools: check.py's rules, kitglb's edge rule, profile_tool.py and reference.py.

Run: tools/props/.venv/bin/python -m pytest tools/props -q
"""
from __future__ import annotations

import json
import shutil
from pathlib import Path

import numpy as np
import pytest

import check
import profile_tool as pt
import reference

kitglb = pt.kitglb


@pytest.fixture()
def library(tmp_path: Path) -> Path:
    """A copy of the fixture props to break."""
    folder = tmp_path / "props"
    shutil.copytree(pt.FIXTURES_DIR, folder)
    return folder


def _edit_manifest(folder: Path, change) -> None:
    manifest = pt.load_manifest(folder)
    change(manifest)
    (folder / pt.MANIFEST).write_bytes(pt.dumps_manifest(manifest))


def _replace_prop(folder: Path, pid: str, piece: kitglb.Piece) -> None:
    """Writes a new file for pid and brings the manifest's derived fields up to date, so only the mesh is wrong."""
    pt.glb_path(folder, pid).write_bytes(kitglb.write_glb(piece))

    def update(manifest):
        for entry in manifest["props"]:
            if entry["id"] == pid:
                entry.update(pt.derived_fields(folder, entry))
    _edit_manifest(folder, update)


def _errors(folder: Path) -> str:
    return "\n".join(check.check_dir(folder))


# ---- check.py ----------------------------------------------------------------------------------------------

def test_fixtures_pass():
    assert check.check_dir(pt.FIXTURES_DIR) == []
    assert check.main([str(pt.FIXTURES_DIR)]) == 0


def test_main_fails_on_a_broken_library(library):
    pt.glb_path(library, "chair_box_min").write_bytes(b"not a glb")
    assert check.main([str(library)]) == 1


def test_hash_must_match_the_file(library):
    _edit_manifest(library, lambda m: m["props"][0].update(sha256="0" * 64))
    assert "sha256 differs" in _errors(library)


def test_profile_must_match_of_mesh(library):
    def change(m):
        profile = bytearray(pt.forge_profile.decode(m["props"][0]["profile"]).tobytes())
        profile[0] ^= 1
        m["props"][0]["profile"] = pt.encode(np.frombuffer(bytes(profile), dtype=np.uint8))
    _edit_manifest(library, change)
    assert "profile differs" in _errors(library)


def test_edge_rule(library, monkeypatch):
    monkeypatch.setattr(reference, "GRID_STEP", 0.5)            # quads of up to 0.46 m: diagonals over 0.25 m
    _replace_prop(library, "chair_box_min", reference.box_piece(reference.CHAIR_BOXES))
    assert "edge rule" in _errors(library)


def test_face_cells_must_be_slot_cells(library):
    boxes = [(lo, hi, 99) if cell == reference.CHAIR_BACK else (lo, hi, cell) for lo, hi, cell in reference.CHAIR_BOXES]
    _replace_prop(library, "chair_box_min", reference.box_piece(boxes))
    errors = _errors(library)
    assert "not slot cells" in errors and "mark no face" in errors


def test_slot_cells_are_object_cells_and_distinct(library):
    _edit_manifest(library, lambda m: m["props"][0]["slots"][0].update(cell=12))
    assert "object palette cell" in _errors(library)
    _edit_manifest(library, lambda m: m["props"][0]["slots"][0].update(cell=m["props"][0]["slots"][1]["cell"]))
    assert "share a default cell" in _errors(library)


def test_origin_at_the_base_centre(library):
    lifted = [((lo[0], lo[1] + 0.1, lo[2]), (hi[0], hi[1] + 0.1, hi[2]), cell) for lo, hi, cell in reference.CHAIR_BOXES]
    _replace_prop(library, "chair_box_min", reference.box_piece(lifted))
    assert "the base is at y" in _errors(library)


def test_bounds_must_equal_size(library):
    _edit_manifest(library, lambda m: m["props"][0].update(size=[0.5, 0.9, 0.5]))
    assert "not size" in _errors(library)


def test_flipped_faces_are_found(library):
    piece = reference.box_piece(reference.CHAIR_BOXES)
    for k in range(0, 6, 3):                                        # the first two triangles turned inside out
        piece.indices[k + 1], piece.indices[k + 2] = piece.indices[k + 2], piece.indices[k + 1]
    _replace_prop(library, "chair_box_min", piece)
    assert "wind against their normals" in _errors(library)


def test_open_mesh_is_refused(library):
    piece = reference.box_piece(reference.CHAIR_BOXES)
    del piece.indices[-3:]
    _replace_prop(library, "chair_box_min", piece)
    assert "open edge" in _errors(library)


@pytest.mark.parametrize("field, value, message", [
    ("category", "unknown", "category"),
    ("category", "sofa", "category"),
    ("tags", ["a"] * 2, "tags"),
    ("tags", ["x" * 49], "tags"),
    ("licence", "CC-BY-4.0", "licence"),
    ("source", "", "source"),
    ("triangles", 1, "triangles is 1"),
])
def test_manifest_fields(library, field, value, message):
    _edit_manifest(library, lambda m: m["props"][0].update({field: value}))
    assert message in _errors(library)


def test_duplicate_ids_and_orphan_files(library):
    _edit_manifest(library, lambda m: m["props"].append(dict(m["props"][0])))
    shutil.copy(pt.glb_path(library, "chair_box_min"), pt.glb_path(library, "stray"))
    errors = _errors(library)
    assert "duplicate ids" in errors and "stray.glb.bytes" in errors


def test_triangle_floor():
    piece = reference.box_piece([((-0.1, 0.0, -0.1), (0.1, 0.2, 0.1), 70)])   # 12 triangles
    mesh = pt.read_prop_glb(kitglb.write_glb(piece))
    assert mesh.triangles < check.MIN_TRIANGLES


def test_categories_come_from_object_category_cs():
    names = check.category_names()
    assert names[:3] == ["unknown", "couch", "table"] and "washerDryer" in names and names[-1] == "other"


# ---- kitglb ------------------------------------------------------------------------------------------------

def test_kitglb_edge_check_is_opt_in(monkeypatch):
    monkeypatch.setattr(reference, "GRID_STEP", 1.0)
    piece = reference.box_piece([((-0.25, 0.0, -0.25), (0.25, 0.5, 0.25), 70)])
    kitglb.check_piece("cube", piece, 2500)                          # the kit pieces' rules: passes
    with pytest.raises(ValueError, match="edge rule"):
        kitglb.check_piece("cube", piece, 2500, check_edges=True)


def test_kitglb_edge_check_passes_short_edges():
    kitglb.check_piece("chair", reference.box_piece(reference.CHAIR_BOXES), 2500, check_edges=True)


# ---- profile_tool.py ---------------------------------------------------------------------------------------

def test_reader_inverts_the_writer():
    piece = reference.box_piece(reference.CHAIR_BOXES)
    back = pt.read_prop_glb(kitglb.write_glb(piece)).to_piece()
    assert back.indices == piece.indices
    assert np.allclose(back.positions, piece.positions, atol=1e-6)
    assert np.allclose(back.normals, piece.normals, atol=1e-6)
    assert np.allclose(back.uv0, piece.uv0, atol=1e-6)


def test_cells_decode_the_palette_coordinate():
    piece = reference.box_piece([((-0.1, 0.0, -0.1), (0.1, 0.2, 0.1), 97)])
    assert set(pt.read_prop_glb(kitglb.write_glb(piece)).cells().tolist()) == {97}


def test_profile_tool_write_is_idempotent(library):
    before = (library / pt.MANIFEST).read_bytes()
    assert pt.main(["--dir", str(library)]) == 0                    # nothing differs
    assert pt.main(["--dir", str(library), "--write"]) == 0
    assert (library / pt.MANIFEST).read_bytes() == before


def test_profile_tool_reports_stale_fields(library):
    _edit_manifest(library, lambda m: m["props"][1].update(profile="", triangles=0))
    assert pt.main(["--dir", str(library)]) == 1
    pt.main(["--dir", str(library), "--write"])
    assert check.check_dir(library) == []


def test_couch_profile_is_indexed_in_unitys_frame():
    """The arm stands on Unity's +X: high i. Row 0 (lowest z) is the back of the couch, j high is its front."""
    manifest = pt.load_manifest(pt.FIXTURES_DIR)
    entry = next(e for e in manifest["props"] if e["id"] == "couch_box3_min")
    grid = pt.forge_profile.decode(entry["profile"]).reshape(16, 16)     # [j, i]
    seat, arm, back = round(255 * 0.42 / 0.80), round(255 * 0.62 / 0.80), 255
    assert grid[8, 15] == arm and grid[8, 0] == seat
    assert grid[0, 5] == back and grid[15, 5] == seat


# ---- reference.py ------------------------------------------------------------------------------------------

def test_reference_is_reproducible_and_current(tmp_path):
    outputs = []
    for run in ("a", "b"):
        assert reference.main(["--fixtures", str(tmp_path / run), "--out", str(tmp_path / run / "reference.json")]) == 0
        outputs.append({p.name: p.read_bytes() for p in sorted((tmp_path / run).iterdir())})
    assert outputs[0] == outputs[1]
    committed = {p.name: p.read_bytes() for p in sorted(pt.FIXTURES_DIR.iterdir())}
    committed["reference.json"] = reference.REFERENCE.read_bytes()
    assert outputs[0] == committed, "the committed fixtures are stale: run tools/props/reference.py"


def test_reference_matcher_choices():
    doc = json.loads(reference.REFERENCE.read_bytes())
    chosen = {q["name"]: (q["expected"].get("id"), q["expected"].get("quarterTurns"))
              for q in doc["matcher"]["queries"]}
    assert chosen == {
        "profile_picks_shape": ("chair_box_min", 0),
        "no_profile_tie_breaks_by_id": ("chair_a_scaled", 0),
        "tags_pick_stool": ("chair_test_stool", 0),
        "profile_rule_keeps_turn_0": ("chair_box_min", 0),
        "box_rule_turns_chair": ("chair_box_min", 2),
        "box_rule_turns_couch": ("couch_box3_min", 1),
        "table_rule_low_coverage": ("table_slab_min", 1),
        "clearance_with_tags": ("couch_test_lshape", 0),
        "no_prop_of_category": (None, None),
    }
    facings = {q["facing"] for q in doc["matcher"]["queries"]}
    assert facings == {"Box", "Profile", "Table", "Clearance"}
    library = doc["matcher"]["library"]
    for q in doc["matcher"]["queries"]:
        if q["expected"]["found"]:
            assert library[q["expected"]["index"]]["id"] == q["expected"]["id"]


def test_reference_scale_matches_the_plan_formula():
    doc = json.loads(reference.REFERENCE.read_bytes())
    assert len(doc["scale"]) >= 5
    for case in doc["scale"]:
        lam = np.log(np.array(case["box"]) / np.array(case["extentIn"]))
        u = np.exp(lam.mean())
        s = u * np.clip(np.exp(lam) / u, 0.85, 1.15)
        assert np.allclose(s, case["scale"], atol=1e-9)
        assert np.allclose(np.array(case["extentIn"]) * s, case["extentOut"], atol=1e-9)
        assert case["boundsMin"][1] == 0.0
