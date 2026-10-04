"""Tests of kitglb: the GLB writer of the kit pieces, its checks and its manifest.

Run: python -m pytest tools/kit -q
"""
from __future__ import annotations

import hashlib
import json
import math
import struct
from pathlib import Path

import pytest

import kitglb

REPO = Path(__file__).resolve().parents[2]
PALETTE_CS = REPO / "ATLAS" / "Assets" / "Atlas" / "Rendering" / "Look" / "AtlasPalette.cs"


def cube(size=0.5, cell=29, lift=0.0) -> kitglb.Piece:
    """A closed box standing on the floor, Unity's frame, flat normals, clockwise fronts."""
    h = 0.5 * size
    piece = kitglb.Piece()
    axes = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)]
    for n in axes:
        u = (0, 1, 0) if n[1] == 0 else (1, 0, 0)
        v = _cross(u, n)                                    # cross(v, u) == n: the front faces n
        centre = (n[0] * h, h + n[1] * h + lift, n[2] * h)
        o = tuple(centre[i] - h * u[i] - h * v[i] for i in range(3))
        first = len(piece.positions)
        for s, t in ((0, 0), (1, 0), (0, 1), (1, 1)):
            piece.positions.append(tuple(o[i] + size * (s * u[i] + t * v[i]) for i in range(3)))
            piece.normals.append(tuple(float(c) for c in n))
            piece.uv0.append(kitglb.palette_uv(cell))
            piece.uv1.append((2.0 * s, 3.0 * t))
            piece.shade.append(0.25 + 0.5 * s)
        p00, p10, p01, p11 = first, first + 1, first + 2, first + 3
        piece.indices += [p00, p01, p11, p00, p11, p10]
    return piece


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _sub(a, b):
    return tuple(a[i] - b[i] for i in range(3))


def _dot(a, b):
    return sum(a[i] * b[i] for i in range(3))


# ---- An independent reader: what GlbFile.TryParse and GlbMeshReader.TryRead look at --------------------------

def parse(glb: bytes):
    magic, version, total = struct.unpack_from("<III", glb, 0)
    assert magic == 0x46546C67 and version == 2 and total == len(glb)
    json_length, json_type = struct.unpack_from("<II", glb, 12)
    assert json_type == 0x4E4F534A and json_length % 4 == 0
    doc = json.loads(glb[20:20 + json_length].decode("utf-8"))
    bin_header = 20 + json_length
    bin_length, bin_type = struct.unpack_from("<II", glb, bin_header)
    assert bin_type == 0x004E4942 and bin_length % 4 == 0
    assert bin_header + 8 + bin_length == len(glb), "no data after the BIN chunk"
    return doc, glb[bin_header + 8:]


def accessor(doc, blob, index):
    a = doc["accessors"][index]
    view = doc["bufferViews"][a["bufferView"]]
    fmt = {5121: "B", 5123: "H", 5125: "I", 5126: "f"}[a["componentType"]]
    width = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[a["type"]]
    size = struct.calcsize("<" + fmt) * width
    stride = view.get("byteStride", size)
    start = view.get("byteOffset", 0) + a.get("byteOffset", 0)
    assert start % struct.calcsize("<" + fmt) == 0, "aligned"
    assert start + (a["count"] - 1) * stride + size <= view.get("byteOffset", 0) + view["byteLength"]
    out = [struct.unpack_from("<%d%s" % (width, fmt), blob, start + k * stride) for k in range(a["count"])]
    return a, (out if width > 1 else [e[0] for e in out])


# ---- Round trip --------------------------------------------------------------------------------------------

def test_round_trip_uses_exactly_the_accessor_types_the_phone_reads():
    piece = cube()
    doc, blob = parse(kitglb.write_glb(piece))
    assert len(doc["meshes"]) == 1 and len(doc["meshes"][0]["primitives"]) == 1
    primitive = doc["meshes"][0]["primitives"][0]
    attributes = primitive["attributes"]
    expected = {"POSITION": (5126, "VEC3"), "NORMAL": (5126, "VEC3"), "TEXCOORD_0": (5126, "VEC2"),
                "TEXCOORD_1": (5126, "VEC2"), "COLOR_0": (5121, "VEC4")}
    assert set(attributes) == set(expected)
    for name, (component, kind) in expected.items():
        a = doc["accessors"][attributes[name]]
        assert (a["componentType"], a["type"], a["count"]) == (component, kind, 24), name
        assert a.get("normalized", False) == (name == "COLOR_0"), name
    indices = doc["accessors"][primitive["indices"]]
    assert (indices["componentType"], indices["type"], indices["count"]) == (5123, "SCALAR", 36)
    assert "materials" not in doc and "textures" not in doc and "images" not in doc
    assert doc["nodes"] == [{"mesh": 0}]

    a, positions = accessor(doc, blob, attributes["POSITION"])
    assert a["min"] == [min(p[i] for p in positions) for i in range(3)]
    assert a["max"] == [max(p[i] for p in positions) for i in range(3)]


def test_round_trip_negates_x_and_flips_v():
    piece = cube(lift=0.125)
    doc, blob = parse(kitglb.write_glb(piece))
    attributes = doc["meshes"][0]["primitives"][0]["attributes"]
    _, positions = accessor(doc, blob, attributes["POSITION"])
    _, normals = accessor(doc, blob, attributes["NORMAL"])
    _, uv0 = accessor(doc, blob, attributes["TEXCOORD_0"])
    _, uv1 = accessor(doc, blob, attributes["TEXCOORD_1"])
    _, colours = accessor(doc, blob, attributes["COLOR_0"])
    for k in range(len(piece.positions)):
        x, y, z = piece.positions[k]
        assert positions[k] == pytest.approx((-x, y, z), abs=1e-7)
        nx, ny, nz = piece.normals[k]
        assert normals[k] == pytest.approx((-nx, ny, nz), abs=1e-7)
        assert not (normals[k][0] == 0.0 and math.copysign(1.0, normals[k][0]) < 0), "no negative zero"
        # GlbMeshReader reads v back as 1 - v; the palette coordinates come back exactly.
        assert (uv0[k][0], 1.0 - uv0[k][1]) == piece.uv0[k]
        assert uv1[k] == pytest.approx((piece.uv1[k][0], 1.0 - piece.uv1[k][1]), abs=1e-6)
        assert colours[k] == (round(255 * piece.shade[k]), 255, 255, 255)


def test_round_trip_reverses_the_winding():
    piece = cube()
    doc, blob = parse(kitglb.write_glb(piece))
    primitive = doc["meshes"][0]["primitives"][0]
    _, indices = accessor(doc, blob, primitive["indices"])
    _, positions = accessor(doc, blob, primitive["attributes"]["POSITION"])
    _, normals = accessor(doc, blob, primitive["attributes"]["NORMAL"])
    for k in range(0, len(indices), 3):
        a, b, c = piece.indices[k:k + 3]
        assert tuple(indices[k:k + 3]) == (a, c, b)
        # Clockwise front in Unity's frame, counter-clockwise front in glTF's: both have cross . n > 0.
        unity = _cross(_sub(piece.positions[b], piece.positions[a]), _sub(piece.positions[c], piece.positions[a]))
        assert _dot(unity, piece.normals[a]) > 0
        p0, p1, p2 = (positions[i] for i in indices[k:k + 3])
        assert _dot(_cross(_sub(p1, p0), _sub(p2, p0)), normals[indices[k]]) > 0


def test_write_glb_is_deterministic():
    assert kitglb.write_glb(cube()) == kitglb.write_glb(cube())


def test_triangle_count_reads_the_index_accessor():
    assert kitglb.triangle_count(kitglb.write_glb(cube())) == 12


# ---- Palette -----------------------------------------------------------------------------------------------

def test_palette_uv_matches_atlas_palette_uv():
    assert kitglb.palette_uv(0) == (0.5 / 16, 0.5 / 16)
    assert kitglb.palette_uv(29) == (13.5 / 16, 1.5 / 16)       # Landmark: column 13, row 1
    assert kitglb.palette_uv(255) == (15.5 / 16, 15.5 / 16)


def test_palette_slots_come_from_atlas_palette_cs():
    slots = kitglb.palette_slots(PALETTE_CS)
    assert slots["Landmark"] == 29
    assert slots["Post"] == 21
    assert slots["AvatarBody"] == 26
    assert slots["AvatarLeft"] == 27
    assert slots["AvatarRight"] == 28
    assert slots["Text"] == 15
    assert slots["Flag"] == 195
    for not_a_slot in ("Size", "CellsPerSide", "CellCount", "EmissiveRow", "CategoryBase", "ObjectBase", "StripW"):
        assert not_a_slot not in slots


def test_material_names_map_to_slots():
    slots = kitglb.palette_slots(PALETTE_CS)
    assert kitglb.slot_for_material("pal_Landmark", slots, "Hero_Landmark") == 29
    with pytest.raises(ValueError, match="Hero_Landmark.*pal_Nope"):
        kitglb.slot_for_material("pal_Nope", slots, "Hero_Landmark")
    with pytest.raises(ValueError, match="Hero_Avatar.*Material"):
        kitglb.slot_for_material("Material", slots, "Hero_Avatar")
    with pytest.raises(ValueError, match="Hero_Avatar.*no material"):
        kitglb.slot_for_material(None, slots, "Hero_Avatar")


# ---- Blender frame -----------------------------------------------------------------------------------------

def test_blender_frame_maps_z_up_to_y_up_and_turns_the_winding():
    assert kitglb.blender_to_unity((1.0, 2.0, 3.0)) == (1.0, 3.0, 2.0)
    # A Blender face seen from +Z is counter-clockwise; in Unity's frame it is seen from +Y and must be clockwise.
    a, b, c = (0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 1.0, 0.0)
    assert _dot(_cross(_sub(b, a), _sub(c, a)), (0, 0, 1)) > 0
    ua, ub, uc = kitglb.blender_triangle(*(kitglb.blender_to_unity(p) for p in (a, b, c)))
    assert _dot(_cross(_sub(ub, ua), _sub(uc, ua)), kitglb.blender_to_unity((0, 0, 1))) > 0


def test_face_shade_matches_atlas_shading():
    light = kitglb.LIGHT_DIRECTION
    assert kitglb.face_shade(light) == pytest.approx(1.0)
    assert kitglb.face_shade(tuple(-c for c in light)) == pytest.approx(kitglb.SHADE_FLOOR)
    assert kitglb.face_shade((0.0, 1.0, 0.0)) == pytest.approx(0.55 + 0.45 * (0.5 + 0.5 * light[1]))


# ---- Checks ------------------------------------------------------------------------------------------------

def test_check_piece_accepts_a_closed_piece():
    kitglb.check_piece("landmark", cube(), kitglb.BUDGETS["landmark"])
    assert kitglb.BUDGETS == {"landmark": 1500, "avatar": 600}


def test_check_piece_refuses_a_piece_over_budget():
    with pytest.raises(ValueError, match="landmark.*12 triangles.*11"):
        kitglb.check_piece("landmark", cube(), 11)


def test_check_piece_refuses_an_open_edge():
    piece = cube()
    del piece.indices[-6:]                                  # one face missing
    with pytest.raises(ValueError, match="avatar.*open edge"):
        kitglb.check_piece("avatar", piece, 600)


def test_check_piece_welds_split_corners_before_looking_for_open_edges():
    piece = cube()
    piece.positions[0] = tuple(c + 4e-6 for c in piece.positions[0])
    kitglb.check_piece("avatar", piece, 600)


def test_check_piece_refuses_an_index_out_of_range():
    piece = cube()
    piece.indices[4] = 24
    with pytest.raises(ValueError, match="landmark.*index 24"):
        kitglb.check_piece("landmark", piece, 1500)


def test_check_piece_refuses_a_non_unit_normal():
    piece = cube()
    piece.normals[3] = (0.0, 2.0, 0.0)
    with pytest.raises(ValueError, match="landmark.*normal"):
        kitglb.check_piece("landmark", piece, 1500)


def test_check_piece_refuses_arrays_of_different_lengths():
    piece = cube()
    piece.shade.pop()
    with pytest.raises(ValueError, match="landmark.*shade"):
        kitglb.check_piece("landmark", piece, 1500)


# ---- Manifest ----------------------------------------------------------------------------------------------

def test_manifest_lists_triangles_and_hashes():
    landmark, avatar = kitglb.write_glb(cube()), kitglb.write_glb(cube(0.2, 27))
    manifest = json.loads(kitglb.write_manifest({"landmark": landmark, "avatar": avatar}).decode("utf-8"))
    assert manifest == {"pieces": {
        "avatar": {"triangles": 12, "sha256": hashlib.sha256(avatar).hexdigest()},
        "landmark": {"triangles": 12, "sha256": hashlib.sha256(landmark).hexdigest()},
    }}
