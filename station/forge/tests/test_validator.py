import copy
import subprocess
import sys

import numpy as np
import pytest

from conftest import ROOT, repack_glb
from forge.cleanup import clean
from forge.generators.procedural import build_furniture
from forge.glb import read_glb, write_glb
from forge.palette import PALETTE, cell_uv, encode_vertex_attributes
from forge.validator import MAX_BYTES, MAX_BYTES_LIGHT, main, validate_glb

BOX = (1.0, 0.75, 0.6)


@pytest.fixture(scope="module")
def good() -> bytes:
    v, f = build_furniture("table", BOX)
    return clean(v, f, BOX, 5000).glb


def arrays(data: bytes):
    glb = read_glb(data)
    prim = glb.doc["meshes"][0]["primitives"][0]
    a = prim["attributes"]
    return (glb.accessor(a["POSITION"]).copy(), glb.accessor(a["NORMAL"]).copy(), glb.accessor(a["TEXCOORD_0"]).copy(),
            glb.accessor(a["COLOR_0"]).copy(), glb.accessor(prim["indices"]).reshape(-1, 3).copy())


def rewrite(data: bytes, positions=None, faces=None) -> bytes:
    p, n, uv, c, f = arrays(data)
    return write_glb(p if positions is None else positions, n, uv, c, f if faces is None else faces)


def edited(data: bytes, change) -> bytes:
    glb = read_glb(data)
    doc = copy.deepcopy(glb.doc)
    change(doc)
    return repack_glb(doc, glb.blob)


def grid(nx: int, nz: int, w: float, d: float, y_top: float = 0.5, split: bool = False) -> bytes:
    """A flat sheet over a base triangle, with 2 * nx * nz + 1 triangles.

    With ``split`` no two triangles share a vertex, which is the largest file a triangle count can make.
    """
    xs, zs = np.linspace(-w / 2, w / 2, nx + 1), np.linspace(-d / 2, d / 2, nz + 1)
    gx, gz = np.meshgrid(xs, zs, indexing="ij")
    top = np.stack([gx.ravel(), np.full(gx.size, y_top), gz.ravel()], axis=1)
    i, j = np.meshgrid(np.arange(nx), np.arange(nz), indexing="ij")
    a = (i * (nz + 1) + j).ravel()
    faces = np.vstack([np.stack([a, a + 1, a + nz + 2], 1), np.stack([a, a + nz + 2, a + nz + 1], 1)])
    base = np.array([[-0.05, 0, 0], [0.05, 0, 0], [0, 0, 0.05]])
    positions = np.vstack([top, base])
    faces = np.vstack([faces, [[len(top), len(top) + 1, len(top) + 2]]])
    if split:
        positions, faces = positions[faces.reshape(-1)], np.arange(faces.size).reshape(-1, 3)
    uv, colour = encode_vertex_attributes(PALETTE[0], np.ones(len(positions)))
    normals = np.tile([0.0, 1.0, 0.0], (len(positions), 1))
    return write_glb(positions, normals, uv, colour, faces)


def test_good_asset_passes(good):
    result = validate_glb(good, tri_target=5000, box=BOX)
    assert result.ok, result.violations
    assert 1500 <= result.triangles <= 5000
    assert result.longest_edge <= 0.25
    assert result.to_dict()["ok"] is True


def test_rejects_too_few_triangles():
    assert "triangles.range" in validate_glb(grid(20, 20, 1.0, 1.0)).codes


def test_rejects_too_many_triangles():
    # 7,201 triangles: inside the full copy's 15,000, over the light copy's 5,000.
    assert validate_glb(grid(60, 60, 1.0, 1.0)).ok
    assert "triangles.range" in validate_glb(grid(60, 60, 1.0, 1.0), level="light").codes
    assert "triangles.range" in validate_glb(grid(90, 90, 1.0, 1.0)).codes  # 16,201


def test_the_two_levels():
    full = grid(86, 87, 4.0, 4.0)  # 14,965 triangles
    light = grid(49, 50, 4.0, 4.0)  # 4,901
    assert validate_glb(full, tri_target=15000).ok and validate_glb(full, tri_target=15000, level="full").ok
    assert validate_glb(full, tri_target=14000).codes == ["triangles.target"]
    assert validate_glb(full, level="light").codes == ["triangles.range"]
    assert validate_glb(light, tri_target=5000, level="light").ok
    assert validate_glb(light, tri_target=4000, level="light").codes == ["triangles.target"]
    result = validate_glb(light, level="light")
    assert result.level == "light" and result.to_dict()["level"] == "light"
    with pytest.raises(ValueError):
        validate_glb(light, level="medium")


def test_file_size_limit_of_each_level():
    assert MAX_BYTES == 2_097_152 and MAX_BYTES_LIGHT == 1_048_576
    # The worst full copy of the design: 15,000 triangles with every vertex split is about 1.71 MB.
    worst = grid(75, 100, 4.0, 4.0, split=True)
    assert validate_glb(worst).triangles == 15001  # one over, so the size is an upper bound
    assert 1_700_000 < len(worst) < MAX_BYTES and "glb.size" not in validate_glb(worst).codes
    # A light copy has 1 MB: 5,000 split triangles fit, the full copy's bytes do not.
    small = grid(49, 50, 4.0, 4.0, split=True)
    assert MAX_BYTES_LIGHT // 2 < len(small) < MAX_BYTES_LIGHT and validate_glb(small, level="light").ok
    assert "glb.size" in validate_glb(worst, level="light").codes
    glb = read_glb(small)
    padded = repack_glb(glb.doc, glb.blob + b"\x00" * MAX_BYTES_LIGHT)
    assert validate_glb(padded).ok and validate_glb(padded, level="light").codes == ["glb.size"]
    assert validate_glb(repack_glb(glb.doc, glb.blob + b"\x00" * MAX_BYTES)).codes == ["glb.size"]


def with_cells(data: bytes, cell_of_vertex) -> bytes:
    """The same mesh with every vertex pointing at the palette cell ``cell_of_vertex`` gives it."""
    p, n, uv, c, f = arrays(data)
    for i in range(len(uv)):
        u, v = cell_uv(int(cell_of_vertex(i, p[i])))
        uv[i] = (u, 1.0 - v)
    return write_glb(p, n, uv, c, f)


def test_palette_cells():
    one = grid(30, 30, 4.0, 4.0, split=True)
    result = validate_glb(one)
    assert result.ok and result.palette_cells == [64] and result.to_dict()["paletteCells"] == [64]
    # Two colours, split along x = 0 between faces: every face keeps one cell.
    p, _, _, _, f = arrays(one)
    left = p[f].mean(axis=1)[:, 0] < 0
    two = with_cells(one, lambda i, _: 110 if left[i // 3] else 75)
    result = validate_glb(two)
    assert result.ok, result.violations
    assert result.palette_cells == [75, 110]
    # One vertex of one face in another cell.
    mixed = with_cells(one, lambda i, _: 127 if i == 4 else 64)
    assert validate_glb(mixed).codes == ["palette.face"]
    # A cell that is not an object colour (cells 64 to 127), and a coordinate off the texture.
    assert validate_glb(with_cells(one, lambda i, _: 3)).codes == ["palette.cell"]
    assert validate_glb(with_cells(one, lambda i, _: 128)).codes == ["palette.cell"]
    p, n, uv, c, f = arrays(one)
    uv[:3] = (1.5, 0.5)  # the three vertices of the first face
    assert validate_glb(write_glb(p, n, uv, c, f)).codes == ["palette.cell"]


def test_rejects_more_triangles_than_the_job_target(good):
    count = validate_glb(good).triangles
    assert validate_glb(good, tri_target=count).ok
    assert validate_glb(good, tri_target=count - 1).codes == ["triangles.target"]


def test_rejects_a_long_edge():
    # 40 x 40 cells over 12 m x 4 m: every cell is 0.3 m long.
    result = validate_glb(grid(40, 40, 12.0, 4.0))
    assert "edge.long" in result.codes
    assert result.longest_edge > 0.25
    assert "edge.long" not in validate_glb(grid(40, 40, 4.0, 4.0)).codes


def test_rejects_two_meshes(good):
    def two(doc):
        doc["meshes"].append(copy.deepcopy(doc["meshes"][0]))
        doc["nodes"].append({"mesh": 1})
        doc["scenes"][0]["nodes"].append(1)

    assert "mesh.count" in validate_glb(edited(good, two)).codes


def test_rejects_two_primitives(good):
    def two(doc):
        doc["meshes"][0]["primitives"].append(copy.deepcopy(doc["meshes"][0]["primitives"][0]))

    assert "mesh.count" in validate_glb(edited(good, two)).codes


def test_rejects_a_material(good):
    def material(doc):
        doc["materials"] = [{"pbrMetallicRoughness": {"baseColorFactor": [1, 0, 0, 1]}}]
        doc["meshes"][0]["primitives"][0]["material"] = 0

    assert "material.present" in validate_glb(edited(good, material)).codes


def test_rejects_a_texture(good):
    def texture(doc):
        doc["images"] = [{"uri": "wood.png"}]
        doc["samplers"] = [{}]
        doc["textures"] = [{"source": 0, "sampler": 0}]

    assert "texture.present" in validate_glb(edited(good, texture)).codes


def test_rejects_wrong_units(good):
    p = arrays(good)[0]
    centimetres = rewrite(good, positions=p * 100.0)
    assert "frame.bounds" in validate_glb(centimetres, box=BOX).codes
    assert "frame.units" in validate_glb(centimetres).codes


def test_rejects_z_up(good):
    p = arrays(good)[0]
    z_up = p[:, [0, 2, 1]] * [1, -1, 1]
    z_up[:, 1] -= z_up[:, 1].min()
    assert "frame.bounds" in validate_glb(rewrite(good, positions=z_up), box=BOX).codes


def test_rejects_an_origin_that_is_not_the_base_centre(good):
    p = arrays(good)[0]
    centred = rewrite(good, positions=p - [0, BOX[1] / 2, 0])  # origin at the box centre
    assert validate_glb(centred, box=BOX).codes == ["frame.origin"]
    shifted = rewrite(good, positions=p + [0.2, 0, 0])  # origin at a corner-ish point
    assert validate_glb(shifted, box=BOX).codes == ["frame.origin"]


def test_rejects_a_node_transform(good):
    def move(doc):
        doc["nodes"][0]["scale"] = [0.01, 0.01, 0.01]

    assert "frame.transform" in validate_glb(edited(good, move)).codes


def test_rejects_missing_attributes(good):
    def strip(doc):
        del doc["meshes"][0]["primitives"][0]["attributes"]["COLOR_0"]

    assert "attributes.missing" in validate_glb(edited(good, strip)).codes


def test_rejects_garbage(good):
    assert validate_glb(b"not a glb at all, just bytes").codes == ["glb.malformed"]
    assert validate_glb(good[:-40]).codes == ["glb.malformed"]


def test_cli(tmp_path, good, capsys):
    ok, bad = tmp_path / "ok.glb", tmp_path / "bad.glb"
    ok.write_bytes(good)
    bad.write_bytes(rewrite(good, positions=arrays(good)[0] * 100.0))
    assert main([str(ok), "--target", "5000", "--box", "1.0", "0.75", "0.6"]) == 0
    assert "PASS" in capsys.readouterr().out
    assert main([str(ok), str(bad), "--box", "1.0", "0.75", "0.6"]) == 1
    out = capsys.readouterr().out
    assert "FAIL" in out and "frame.bounds" in out
    run = subprocess.run([sys.executable, "-m", "forge.validator", str(bad), "--json"], cwd=ROOT,
                         capture_output=True, text=True)
    assert run.returncode == 1 and '"ok": false' in run.stdout


def test_cli_level(tmp_path, capsys):
    full, light = tmp_path / "full.glb", tmp_path / "light.glb"
    full.write_bytes(grid(60, 60, 4.0, 4.0))
    light.write_bytes(grid(40, 40, 4.0, 4.0))
    assert main([str(full), "--target", "15000"]) == 0
    assert "(full)" in capsys.readouterr().out
    assert main([str(full), "--level", "light"]) == 1
    assert "triangles.range" in capsys.readouterr().out
    assert main([str(light), "--level", "light", "--target", "5000"]) == 0
    assert "(light)" in capsys.readouterr().out
    with pytest.raises(SystemExit):
        main([str(light), "--level", "medium"])
