import copy
import subprocess
import sys

import numpy as np
import pytest

from conftest import ROOT, repack_glb
from forge.cleanup import clean
from forge.generators.procedural import build_furniture
from forge.glb import read_glb, write_glb
from forge.palette import PALETTE, encode_vertex_attributes
from forge.validator import main, validate_glb

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


def grid(nx: int, nz: int, w: float, d: float, y_top: float = 0.5) -> bytes:
    """A flat sheet over a base triangle, with 2 * nx * nz + 1 triangles."""
    xs, zs = np.linspace(-w / 2, w / 2, nx + 1), np.linspace(-d / 2, d / 2, nz + 1)
    gx, gz = np.meshgrid(xs, zs, indexing="ij")
    top = np.stack([gx.ravel(), np.full(gx.size, y_top), gz.ravel()], axis=1)
    i, j = np.meshgrid(np.arange(nx), np.arange(nz), indexing="ij")
    a = (i * (nz + 1) + j).ravel()
    faces = np.vstack([np.stack([a, a + 1, a + nz + 2], 1), np.stack([a, a + nz + 2, a + nz + 1], 1)])
    base = np.array([[-0.05, 0, 0], [0.05, 0, 0], [0, 0, 0.05]])
    positions = np.vstack([top, base])
    faces = np.vstack([faces, [[len(top), len(top) + 1, len(top) + 2]]])
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
    assert "triangles.range" in validate_glb(grid(60, 60, 1.0, 1.0)).codes


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
