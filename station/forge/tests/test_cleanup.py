import numpy as np
import pytest
import trimesh

from forge import cleanup
from forge.cleanup import CleanupError, clean
from forge.generators.procedural import build_furniture
from forge.glb import read_glb
from forge.validator import validate_glb


def check(result, dims, target):
    verdict = validate_glb(result.glb, tri_target=target, box=dims)
    assert verdict.ok, verdict.violations
    report = result.report
    assert report.triangles == verdict.triangles <= target
    assert report.longest_edge <= 0.25
    assert report.max_scale_error <= 0.10
    assert report.palette_name
    return verdict


def blob(subdivisions=7):
    sphere = trimesh.creation.icosphere(subdivisions=subdivisions)
    v = np.asarray(sphere.vertices)
    bumps = 1.0 + 0.15 * np.sin(7 * v[:, [0]]) * np.cos(5 * v[:, [1]])
    return v * bumps * [1.0, 0.7, 0.8], np.asarray(sphere.faces)


def test_blob_of_over_200k_triangles():
    v, f = blob()
    assert len(f) > 200_000
    dims = (0.9, 0.63, 0.72)
    result = clean(v * 12.0 + 5.0, f, dims, 4000, (60, 120, 60))
    verdict = check(result, dims, 4000)
    assert verdict.triangles > 3000  # the budget is used, not thrown away
    assert result.report.source_triangles == len(f)
    assert result.report.palette_name.startswith("green") and 64 <= result.report.palette_index < 128


def test_the_full_target_goes_to_fifteen_thousand():
    v, f = blob()
    dims = (0.9, 0.63, 0.72)
    result = clean(v, f, dims, 15000, (60, 120, 60))
    verdict = check(result, dims, 15000)
    assert 12000 < verdict.triangles <= 15000 and result.report.tri_target == 15000
    assert len(result.glb) < 2 * 1024 * 1024
    # The light level does not take it; a target above the contract's largest is held to it.
    assert "triangles.range" in validate_glb(result.glb, box=dims, level="light").codes
    assert clean(v, f, dims, 20000, (60, 120, 60)).report.tri_target == 15000
    # And a light target: what the light copy of a crowded room will be held to.
    light = clean(v, f, dims, 1875, (60, 120, 60))
    assert validate_glb(light.glb, tri_target=1875, box=dims, level="light").ok and light.report.triangles >= 1500


def test_long_thin_triangles():
    # A 3 m plank of twelve triangles: every one is a sliver far over the edge limit.
    plank = trimesh.creation.box((3.0, 0.04, 0.3))
    dims = (3.0, 0.04, 0.3)
    result = clean(plank.vertices, plank.faces, dims, 3000)
    check(result, dims, 3000)
    # A fan of needles around one vertex.
    angles = np.linspace(0, 2 * np.pi, 400, endpoint=False)
    rim = np.stack([np.cos(angles) * 1.5, np.zeros(400), np.sin(angles) * 1.5], axis=1)
    v = np.vstack([[0, 0, 0], [0, 0.3, 0], rim])
    i = np.arange(400)
    j = (i + 1) % 400
    f = np.vstack([np.stack([np.zeros(400, int), i + 2, j + 2], 1), np.stack([np.ones(400, int), j + 2, i + 2], 1)])
    check(clean(v, f, (3.0, 0.3, 3.0), 5000), (3.0, 0.3, 3.0), 5000)


def test_non_manifold_mesh():
    a = trimesh.creation.box((0.5, 0.5, 0.5))
    b = trimesh.creation.box((0.5, 0.5, 0.5))
    b.apply_translation((0.5, 0.5, 0.0))  # the two boxes touch along one edge
    v = np.vstack([a.vertices, b.vertices, [[0, -0.25, 0], [0, 0.25, 0], [0.9, 0, 0]]])
    fin = [[16, 17, 18]]  # a loose fin cutting through both
    f = np.vstack([a.faces, b.faces + 8, fin, a.faces[:4], [[0, 0, 1]]])  # duplicates and a degenerate face
    v = np.vstack([v, [[np.nan, 0, 0]]])  # and an unusable vertex
    dims = (1.15, 1.0, 0.5)
    result = clean(v, f, dims, 3000)
    check(result, dims, 3000)


def test_wrong_scale_and_orientation():
    dims = (1.4, 0.75, 0.8)
    v, f = build_furniture("table", dims)
    # Lying on its side (Z up), turned a quarter about the up axis, in centimetres, far from the origin.
    z_up = np.stack([-v[:, 2], v[:, 0], v[:, 1]], axis=1) * 100.0 + [300.0, -120.0, 55.0]
    result = clean(z_up, f, dims, 5000)
    verdict = check(result, dims, 5000)
    assert result.report.up_axis == "Z" and result.report.yaw_degrees == 90
    assert np.allclose(verdict.size, dims, atol=1e-4)
    assert abs(verdict.bounds_min[1]) < 1e-5
    # The table top is still at the top: most of the surface area sits in the upper tenth.
    glb = read_glb(result.glb)
    prim = glb.doc["meshes"][0]["primitives"][0]
    p = glb.accessor(prim["attributes"]["POSITION"]).astype(np.float64)
    t = glb.accessor(prim["indices"]).reshape(-1, 3)
    area = 0.5 * np.linalg.norm(np.cross(p[t[:, 1]] - p[t[:, 0]], p[t[:, 2]] - p[t[:, 0]]), axis=1)
    high = p[t].mean(axis=1)[:, 1] > 0.9 * dims[1]
    assert area[high].sum() > 0.5 * area.sum()


def test_small_stretch_is_absorbed_and_a_wrong_shape_is_reported():
    cube = trimesh.creation.box((1, 1, 1))
    near = clean(cube.vertices, cube.faces, (0.5, 0.55, 0.45), 3000)
    assert near.report.max_scale_error < 1e-4
    # A cube cannot become a 2 x 0.5 x 0.5 bench within +-15 % per axis.
    far = clean(cube.vertices, cube.faces, (2.0, 0.5, 0.5), 3000)
    assert far.report.max_scale_error > 0.10
    assert "frame.bounds" in validate_glb(far.glb, 3000, (2.0, 0.5, 0.5)).codes


def test_same_input_gives_the_same_hash():
    v, f = blob(5)
    first = clean(v, f, (0.6, 0.5, 0.5), 3000, (150, 112, 70))
    second = clean(v.copy(), f.copy(), (0.6, 0.5, 0.5), 3000, (150, 112, 70))
    assert first.glb == second.glb and first.report.sha256 == second.report.sha256
    other = clean(v, f, (0.6, 0.5, 0.5), 3000, (40, 54, 92))
    assert other.report.sha256 != first.report.sha256


def test_output_has_one_colour_and_baked_shading():
    dims = (2.0, 0.8, 0.9)
    v, f = build_furniture("couch", dims)
    result = clean(v, f, dims, 5000, (132, 54, 48))
    glb = read_glb(result.glb)
    doc = glb.doc
    assert len(doc["meshes"]) == 1 and len(doc["meshes"][0]["primitives"]) == 1
    assert not any(k in doc for k in ("materials", "textures", "images", "samplers"))
    a = doc["meshes"][0]["primitives"][0]["attributes"]
    uv, colour = glb.accessor(a["TEXCOORD_0"]), glb.accessor(a["COLOR_0"])
    assert len(np.unique(uv, axis=0)) == 1  # one palette cell
    assert result.report.palette_name.startswith("red")
    column, row = result.report.palette_index % 16, result.report.palette_index // 16
    assert np.allclose(uv[0], [(column + 0.5) / 16, 1 - (row + 0.5) / 16]) and 4 <= row <= 7
    # R is the shade; G (line weight), B and A are 255 (lane A, mesh vertex format).
    assert (colour[:, 1:] == 255).all()
    normals = glb.accessor(a["NORMAL"])
    assert np.allclose(np.linalg.norm(normals, axis=1), 1.0, atol=1e-3)
    shade = colour[:, 0].astype(float)
    up, down = normals[:, 1] > 0.99, normals[:, 1] < -0.99
    assert shade[up].max() > 225 and shade[down].max() < 160  # lit from above
    assert shade[up].min() < shade[up].max() - 20  # and darker in the inner corners


def test_shade_follows_the_room_light_and_the_yaw():
    from forge.palette import LIGHT_DIRECTION, SHADE_FLOOR, face_shade

    assert np.isclose(face_shade(np.array([LIGHT_DIRECTION * [-1, 1, 1]]))[0], 1.0)  # X is inverted on import
    assert np.isclose(face_shade(np.array([-LIGHT_DIRECTION * [-1, 1, 1]]))[0], SHADE_FLOOR)
    # Box-local +Z turned by +90 degrees faces the room's +X.
    turned = face_shade(np.array([[0.0, 0.0, 1.0]]), yaw=np.pi / 2)[0]
    assert np.isclose(turned, SHADE_FLOOR + (1 - SHADE_FLOOR) * (0.5 + 0.5 * LIGHT_DIRECTION[0]))
    cube = trimesh.creation.box((1, 1, 1))
    a = clean(cube.vertices, cube.faces, (0.5, 0.5, 0.5), 3000, yaw=0.0)
    b = clean(cube.vertices, cube.faces, (0.5, 0.5, 0.5), 3000, yaw=1.0)
    assert a.report.sha256 != b.report.sha256


def test_clustering_fallback_reaches_the_target(monkeypatch):
    v, f = blob(5)
    cv, cf = cleanup.decimate_by_clustering(*cleanup.weld(v, f), 2000)
    assert 0 < len(cf) <= 2000
    # Without the quadric decimator the whole clean-up still meets the contract.
    import builtins

    real_import = builtins.__import__

    def no_decimator(name, *args, **kwargs):
        if name == "fast_simplification":
            raise ImportError(name)
        return real_import(name, *args, **kwargs)

    monkeypatch.setattr(builtins, "__import__", no_decimator)
    check(clean(v, f, (0.6, 0.5, 0.5), 3000), (0.6, 0.5, 0.5), 3000)


def test_unusable_input_fails_cleanly():
    with pytest.raises(CleanupError):
        clean(np.zeros((0, 3)), np.zeros((0, 3), int), (1, 1, 1), 3000)
    with pytest.raises(CleanupError):
        clean(np.full((3, 3), np.nan), [[0, 1, 2]], (1, 1, 1), 3000)
    with pytest.raises(CleanupError):
        clean([[0, 0, 0], [1, 0, 0], [0, 1, 0]], [[0, 1, 7]], (1, 1, 1), 3000)
    cube = trimesh.creation.box((1, 1, 1))
    with pytest.raises(CleanupError):  # a 6 m cube cannot be gridded at 0.25 m in 3,000 triangles
        clean(cube.vertices, cube.faces, (6, 6, 6), 3000)
    with pytest.raises(CleanupError):
        clean(cube.vertices, cube.faces, (1, 1, 1), 900)
