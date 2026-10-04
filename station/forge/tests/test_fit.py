"""Clean-up's orientation by the fit, the colours per face and the two copies (mesh design section 5.3)."""
import numpy as np
import pytest

from forge import attempts, cleanup, profile
from forge.cleanup import clean, finish, orient
from forge.config import Settings
from forge.generators.procedural import build_furniture
from forge.glb import read_glb
from forge.validator import validate_glb
from test_cleanup import blob
from test_colour import BLUE, BOX, RED, front_view, grid_box, halves, look_at, render
from test_colour import ColourView
from test_profile import armchair, glb_box, join, turned

CUBE = (1.0, 1.0, 1.0)


def mesh_of(glb: bytes):
    """Positions, faces and the palette cell of each face, as the file holds them."""
    file = read_glb(glb)
    primitive = file.doc["meshes"][0]["primitives"][0]
    positions = file.accessor(primitive["attributes"]["POSITION"]).astype(np.float64)
    uv = file.accessor(primitive["attributes"]["TEXCOORD_0"]).astype(np.float64)
    faces = file.accessor(primitive["indices"]).reshape(-1, 3).astype(np.int64)
    cell = (np.floor((1.0 - uv[:, 1]) * 16) * 16 + np.floor(uv[:, 0] * 16)).astype(np.int64)
    assert (cell[faces] == cell[faces[:, :1]]).all()
    return positions, faces, cell[faces[:, 0]]


# --------------------------------------------------------------------------- the turn test


@pytest.mark.parametrize("k", [0, 1, 2, 3])
def test_a_mesh_turned_by_a_quarter_turn_is_turned_back(k):
    """The turn test of the design, with a profile.

    The armchair is handed over turned by k of clean-up's quarter turns: k times a right-handed +90 degrees
    about +Y in the GLB frame, which is a yaw of -90 k degrees in Unity's box frame. Clean-up finds, by the
    fit, the (4 - k) further turns that bring it back, and reports them both ways.
    """
    v, f = armchair()
    scan = profile.of_mesh(v, f, CUBE)
    result = clean(turned(v, k), f, CUBE, 5000, scan=scan)
    report = result.report
    assert report.fit == 0.0 and report.up_axis == "Y"
    assert report.yaw_degrees == (4 - k) % 4 * 90  # clean-up's own turns, right-handed in the GLB frame
    assert report.fit_yaw == k * 90 % 360  # the same as a yaw in Unity's box frame: +90 k undoes -90 k
    assert report.to_dict()["fitYaw"] == report.fit_yaw and report.to_dict()["fitUp"] == "Y"
    assert not report.violations
    # The file holds the armchair as the scan saw it: its back on -Z, its arm on Unity's +X.
    positions, faces, _ = mesh_of(result.glb)
    assert profile.fit_score(scan, profile.of_mesh(positions, faces, CUBE)) < 0.01
    # Every orientation that was scored is in the report, the best first; the half turn is far off.
    best, second = report.candidates[0], report.candidates[1]
    assert (best["up"], best["yawDegrees"], best["fit"]) == ("Y", report.yaw_degrees, 0.0)
    assert second["fit"] > cleanup.FIT_TIE
    assert [c["fit"] for c in report.candidates] == sorted(c["fit"] for c in report.candidates)


def test_the_fit_also_chooses_the_up_axis_and_respects_the_box():
    # A box with three different sides: the bounds leave one up axis and two yaws, and the fit takes the right yaw.
    v, f = join(glb_box((-0.5, 0.0, -0.25), (0.5, 0.25, 0.25)), glb_box((-0.5, 0.0, -0.25), (0.5, 0.8, -0.1)),
                glb_box((0.3, 0.0, -0.1), (0.5, 0.6, 0.25)))
    dims = (1.0, 0.8, 0.5)
    scan = profile.of_mesh(v, f, dims)
    for k in range(4):
        # Handed over lying on its side (Z up in the generator's frame) and turned by k quarter turns.
        delivered = turned(v, k) @ dict(cleanup._UP)["Z"]
        report = clean(delivered, f, dims, 5000, scan=scan).report
        assert (report.up_axis, report.yaw_degrees, report.fit) == ("Z", (4 - k) % 4 * 90, 0.0), k
        assert [(c["up"], c["yawDegrees"]) for c in report.candidates] == [
            ("Z", report.yaw_degrees), ("Z", (report.yaw_degrees + 180) % 360)]
    # With the up axis known, as for TripoSG once it is measured, only that axis is tried.
    assert clean(v @ dict(cleanup._UP)["Z"], f, dims, 5000, scan=scan, up_axes=("Z",)).report.fit == 0.0
    wrong = clean(v @ dict(cleanup._UP)["Z"], f, dims, 5000, scan=scan, up_axes=("Y",)).report
    assert wrong.up_axis == "Y" and wrong.fit > 0.2 and wrong.violations  # it lies on its side and says so
    with pytest.raises(cleanup.CleanupError):
        clean(v, f, dims, 5000, scan=scan, up_axes=("W",))


# --------------------------------------------------------------------------- ties and no profile


def table():
    """A square table: every quarter turn fits a scan of it equally well."""
    return build_furniture("table", (1.0, 0.75, 1.0)), (1.0, 0.75, 1.0)


@pytest.mark.parametrize("prefer, yaw", [(None, 0), (0, 0), (1, 90), (2, 180), (3, 270)])
def test_a_tie_is_broken_by_the_generators_known_front(prefer, yaw):
    (v, f), dims = table()
    scan = profile.of_mesh(v, f, dims)
    report = clean(v, f, dims, 5000, scan=scan, prefer_turns=prefer).report
    assert report.fit == 0.0 and report.yaw_degrees == yaw
    assert [c["fit"] for c in report.candidates[:4]] == [0.0] * 4  # the four yaws tie


def test_a_mesh_that_fits_the_box_no_way_is_still_turned_the_better_way_round():
    # The armchair fills a cube; the box is half as deep. No orientation is within the stretch limit, so the
    # bounds choose as before, and the fit only chooses between that orientation and the same turned by a half.
    v, f = armchair()
    dims = (1.0, 1.0, 0.5)
    scan = profile.of_mesh(v * [1.0, 1.0, 0.5], f, dims)
    for half_turns in (0, 1):
        report = clean(turned(v, 2 * half_turns), f, dims, 5000, scan=scan).report
        assert [(c["up"], c["yawDegrees"]) for c in sorted(report.candidates, key=lambda c: c["yawDegrees"])] == [
            ("Y", 0), ("Y", 180)]
        assert all(c["stretch"] > 0.15 for c in report.candidates)
        assert report.yaw_degrees == 180 * half_turns
        assert report.fit == pytest.approx(report.candidates[0]["fit"], abs=1e-4) and report.fit < 0.15
        assert report.candidates[1]["fit"] > report.fit + cleanup.FIT_TIE
        assert any(fault.startswith("[frame.bounds]") for fault in report.violations)  # and the report says so


def test_another_up_axis_must_fit_clearly_better_to_be_taken():
    # A cube with a plinth: on its side it fits a scan of its flat top a hair better, which means nothing.
    dims = (0.5, 0.5, 0.5)
    v, f = build_furniture("other", dims)
    scan = np.full(256, 100, dtype=np.uint8)
    report = clean(v, f, dims, 5000, scan=scan).report
    best = report.candidates[0]
    assert best["up"] != "Y" and 0.0 < report.fit - best["fit"] < cleanup.FIT_TIE
    assert (report.up_axis, report.yaw_degrees) == ("Y", 0)  # the generator's own frame, within the tie
    # A chair on its side is far from the scan of the chair: the right axis is found whatever the order.
    v, f = build_furniture("chair", (0.6, 0.6, 0.6))
    scan = profile.of_mesh(v, f, (0.6, 0.6, 0.6))
    lying = v @ dict(cleanup._UP)["X"]
    report = clean(lying, f, (0.6, 0.6, 0.6), 5000, scan=scan).report
    assert (report.up_axis, report.fit) == ("X", 0.0)


def test_a_near_tie_is_broken_and_a_clear_fit_is_not():
    # A table with a low block near one edge: the half turn scores a little worse, within the tie.
    (v, f), dims = table()
    v, f = join((v, f), glb_box((-0.2, 0.75, 0.3), (0.2, 0.80, 0.45)))
    dims = (1.0, 0.80, 1.0)
    scan = profile.of_mesh(v, f, dims)
    plain = clean(v, f, dims, 5000, scan=scan).report
    assert plain.yaw_degrees == 0 and plain.fit == 0.0
    half_turn = next(c for c in plain.candidates if c["up"] == "Y" and c["yawDegrees"] == 180)
    assert 0.0 < half_turn["fit"] < cleanup.FIT_TIE
    assert clean(v, f, dims, 5000, scan=scan, prefer_turns=2).report.yaw_degrees == 180  # the known front wins
    assert clean(v, f, dims, 5000, scan=scan, prefer_turns=2).report.fit == pytest.approx(half_turn["fit"], abs=1e-4)
    # The armchair fits one way only: what is known of the generator's front does not turn it away from the scan.
    v, f = armchair()
    scan = profile.of_mesh(v, f, CUBE)
    for prefer in (None, 0, 1, 2, 3):
        assert clean(v, f, CUBE, 5000, scan=scan, prefer_turns=prefer).report.yaw_degrees == 0


def test_without_a_profile_the_known_front_decides_and_without_either_the_bounds_do():
    v, f = armchair()
    # Nothing known: the rule of before, the generator's own frame unless the bounds say otherwise.
    plain = clean(v, f, CUBE, 5000).report
    assert (plain.up_axis, plain.yaw_degrees, plain.fit, plain.candidates) == ("Y", 0, None, [])
    assert plain.to_dict()["fit"] is None and plain.to_dict()["fitYaw"] == 0
    # The front is known to be on the mesh's own +Z: no turn. Or towards a camera that stood elsewhere.
    for prefer in range(4):
        report = clean(v, f, CUBE, 5000, prefer_turns=prefer).report
        assert (report.yaw_degrees, report.fit) == (prefer * 90, None)
    # A scan that saw nothing is no profile.
    assert clean(v, f, CUBE, 5000, scan=np.zeros(256, np.uint8)).report.fit is None
    # In a box that is not square the bounds allow two yaws; a preferred turn outside them is not taken.
    dims = (1.4, 0.75, 0.8)
    v, f = build_furniture("table", dims)
    assert clean(v, f, dims, 5000, prefer_turns=2).report.yaw_degrees == 180
    assert clean(v, f, dims, 5000, prefer_turns=1).report.yaw_degrees == 0
    sideways = turned(v, 1)
    assert clean(sideways, f, dims, 5000, prefer_turns=0).report.yaw_degrees == 90  # the bounds, as before
    assert clean(sideways, f, dims, 5000, prefer_turns=3).report.yaw_degrees == 270


def test_what_the_setting_says_of_the_front():
    def camera(x, z):
        return [{"camToBox": [1, 0, 0, x, 0, 1, 0, 1.0, 0, 0, 1, z, 0, 0, 0, 1]}]

    assert attempts.preferred_turns("canonical", camera(2.0, 0.0)) == 0
    assert attempts.preferred_turns("unknown", camera(2.0, 0.0)) is None
    # `view`: the mesh's own +Z side is the one the camera saw. Camera on side k of the box: (4 - k) % 4 turns.
    assert attempts.preferred_turns("view", camera(0.0, 2.0)) == 0  # in front (+Z)
    assert attempts.preferred_turns("view", camera(2.0, 0.1)) == 3  # on Unity's +X
    assert attempts.preferred_turns("view", camera(0.0, -2.0)) == 2  # behind
    assert attempts.preferred_turns("view", camera(-2.0, 0.1)) == 1  # on Unity's -X
    assert attempts.preferred_turns("view", []) is None and attempts.preferred_turns("view", [{}]) is None
    # And that turn does put the mesh's +Z side where the camera stood: a post on the mesh's +Z.
    post = glb_box((-0.1, 0.0, 0.3), (0.1, 1.0, 0.5))
    for x, z in ((0.0, 2.0), (2.0, 0.1), (0.0, -2.0), (-2.0, 0.1)):
        turns = attempts.preferred_turns("view", camera(x, z))
        centre = turned(post[0], turns).mean(axis=0) * [-1.0, 1.0, 1.0]  # in Unity's box frame
        assert centre[0] * x + centre[2] * z > 0.7

    # A camera that cannot be one gives no colour view; a matted view with a sound camera does.
    from PIL import Image

    from forge.imaging import MattedView

    crop = Image.new("RGB", (64, 64), (200, 60, 40))
    matted = {0: MattedView(crop, Image.new("L", (64, 64), 255), "rembg"),
              1: MattedView(crop, Image.new("L", (64, 64), 255), "none")}
    sound = {"intrinsics": [60.0, 60.0, 32.0, 32.0], "camToBox": camera(0.0, 2.0)[0]["camToBox"]}
    assert len(attempts.colour_views(matted, [sound, sound])) == 1  # view 1 is not matted
    for broken in ({**sound, "intrinsics": [0.0, 60.0, 32.0, 32.0]}, {**sound, "camToBox": [0.0] * 16},
                   {**sound, "intrinsics": None}, {"intrinsics": sound["intrinsics"]}, {}):
        assert attempts.colour_views(matted, [broken]) == []
    assert attempts.colour_views(matted, []) == []

    settings = Settings()
    settings.triposg_up, settings.triposg_front = "Z", "canonical"
    assert attempts.up_axes_for("triposg", settings) == ("Z",)
    assert attempts.front_for("triposg", settings) == "canonical"
    # The two settings describe TripoSG's meshes; another generator's are oriented with nothing assumed.
    assert attempts.up_axes_for("procedural", settings) is None and attempts.front_for("hosted", settings) == "unknown"
    assert attempts.in_order_of_fit([0.3, 0.1, 0.1]) == [1, 2, 0] and attempts.in_order_of_fit([None, None]) == [0, 1]
    assert attempts.scan_of("") is None and attempts.scan_of(profile.encode(np.zeros(256, np.uint8))) is None


# --------------------------------------------------------------------------- colour per face


def two_colour_box(step=0.05):
    """The box of the colour tests with the photo of it: red where Unity's x is positive, blue where negative."""
    v, f = grid_box(*BOX, step=step)
    dims = tuple(BOX[1][k] - BOX[0][k] for k in range(3))
    return v, f, dims, front_view(halves)


def test_a_two_colour_object_gets_two_cells_in_the_right_places_through_clean_and_the_validator():
    v, f, dims, view = two_colour_box()
    result = clean(v, f, dims, 5000, RED.rgb, views=[view], tri_target_light=1500)
    for glb, level, target, report in ((result.glb, "full", 5000, result.report),
                                       (result.light_glb, "light", 1500, result.light_report)):
        verdict = validate_glb(glb, tri_target=target, box=dims, level=level)
        assert verdict.ok, verdict.violations
        assert verdict.palette_cells == sorted([RED.index, BLUE.index])
        positions, faces, cells = mesh_of(glb)
        centre = positions[faces].mean(axis=1)
        unity_x = -centre[:, 0]  # the file is in the GLB frame
        area = 0.5 * np.linalg.norm(np.cross(positions[faces[:, 1]] - positions[faces[:, 0]],
                                             positions[faces[:, 2]] - positions[faces[:, 0]]), axis=1)
        # On the front, which the camera saw, each half has its colour; the back, which it did not see,
        # took the colour of the nearest seen face, so it is split the same way.
        for side in (centre[:, 2] > 0.19, centre[:, 2] < -0.19):
            assert (cells[side & (unity_x > 0.06)] == RED.index).all()
            assert (cells[side & (unity_x < -0.06)] == BLUE.index).all()
        right = (cells == RED.index) == (unity_x > 0)
        assert area[right].sum() > 0.97 * area.sum()
        # The report names both cells with the share each covers, and the job's one cell is the larger.
        assert sorted(c for c, _ in report.cells) == sorted([RED.index, BLUE.index]) and report.colour_views == 1
        assert abs(sum(share for _, share in report.cells) - 1.0) < 1e-9
        assert all(0.4 < share < 0.6 for _, share in report.cells)
        assert report.palette_index == report.cells[0][0] and report.to_dict()["colour"]["views"] == 1
        assert [c["index"] for c in report.to_dict()["colour"]["cells"]] == [c for c, _ in report.cells]
    assert result.light_report.triangles <= 1500 < result.report.triangles


def test_no_usable_photo_leaves_one_cell_from_the_dominant_colour():
    v, f, dims, view = two_colour_box()
    plain = clean(v, f, dims, 5000, BLUE.rgb)
    assert plain.report.cells == [(BLUE.index, 1.0)] and plain.report.colour_views == 0
    assert validate_glb(plain.glb, box=dims).palette_cells == [BLUE.index]
    # A camera that sees nothing of the object (it looks away) gives the same file.
    m, k = look_at((0.0, 0.4, 2.5), (0.0, 0.4, 5.0), 900, 640, 640)
    rgb, alpha = render([BOX], halves, m, k, 640, 640)
    away = clean(v, f, dims, 5000, BLUE.rgb, views=[ColourView(rgb, alpha, k, m)])
    assert away.glb == plain.glb
    # The same input gives the same bytes, colours included.
    again = clean(v.copy(), f.copy(), dims, 5000, RED.rgb, views=[view])
    assert clean(v, f, dims, 5000, RED.rgb, views=[view]).glb == again.glb


def test_the_colours_follow_the_mesh_when_the_fit_turns_it():
    # The mesh is handed over turned by a quarter; the photo shows the object as it stands in the room.
    v, f, dims, view = two_colour_box()
    scan = profile.of_mesh(*join((v, f), glb_box((0.1, 0.8, -0.2), (0.3, 0.9, 0.2))), (0.6, 0.9, 0.4))
    v, f = join((v, f), grid_box((0.1, 0.8, -0.2), (0.3, 0.9, 0.2)))  # a block on top, on Unity's +X: no symmetry
    dims = (0.6, 0.9, 0.4)
    result = clean(turned(v, 2), f, dims, 5000, RED.rgb, scan=scan, views=[view])
    assert result.report.yaw_degrees == 180 and result.report.fit < 0.01
    positions, faces, cells = mesh_of(result.glb)
    centre = positions[faces].mean(axis=1)
    front = (centre[:, 2] > 0.19) & (centre[:, 1] < 0.75)
    assert (cells[front & (-centre[:, 0] > 0.06)] == RED.index).all()
    assert (cells[front & (-centre[:, 0] < -0.06)] == BLUE.index).all()


# --------------------------------------------------------------------------- the two copies


def test_both_copies_validate_at_their_levels_and_the_light_one_meets_its_target():
    v, f = blob()
    dims = (0.9, 0.63, 0.72)
    result = clean(v * 12.0 + 5.0, f, dims, 15000, (60, 120, 60), tri_target_light=1875)
    full, light = result.report, result.light_report
    assert full.level == "full" and light.level == "light" and not full.violations and not light.violations
    assert validate_glb(result.glb, tri_target=15000, box=dims, level="full").ok
    assert validate_glb(result.light_glb, tri_target=1875, box=dims, level="light").ok
    assert 12000 < full.triangles <= 15000 and 1500 <= light.triangles <= 1875 == light.tri_target
    assert light.bytes < full.bytes and light.sha256 != full.sha256
    assert full.light == {"sha256": light.sha256, "bytes": light.bytes, "triangles": light.triangles}
    assert full.to_dict()["light"]["triangles"] == light.triangles and "light" not in light.to_dict()
    # The two copies fill the same box, are turned the same way and have the same colour.
    assert np.allclose(light.size, full.size, atol=1e-4)
    assert (light.up_axis, light.yaw_degrees) == (full.up_axis, full.yaw_degrees)
    assert light.palette_index == full.palette_index
    assert any("reduced from the full copy" in note for note in light.notes)
    # A light target above the contract's 5,000 is held to it.
    assert clean(v, f, dims, 15000, tri_target_light=9000).light_report.triangles <= 5000


def test_a_full_copy_already_under_the_light_target_is_its_own_light_copy():
    dims = (0.5, 0.9, 0.5)
    v, f = build_furniture("chair", dims)
    result = clean(v, f, dims, 15000, tri_target_light=5000)
    assert result.light_glb == result.glb and result.light_report.sha256 == result.report.sha256
    assert result.light_report.level == "light" and not result.light_report.violations
    # Nothing is asked for, nothing is made.
    plain = clean(v, f, dims, 15000)
    assert plain.light_glb is None and plain.light_report is None and plain.report.light is None
    assert plain.glb == result.glb


def test_a_light_copy_that_cannot_be_made_leaves_the_full_one(monkeypatch):
    v, f = blob(5)
    dims = (0.6, 0.5, 0.5)
    full = clean(v, f, dims, 5000).glb
    low = clean(v, f, dims, 5000, tri_target_light=900)  # under the contract's 1,500
    assert low.glb == full and low.light_glb is None and low.light_report is None
    assert "below the contract minimum" in low.report.light["error"]
    assert any(note.startswith("no light copy: ") for note in low.report.notes) and not low.report.violations

    def stuck(*args, **kwargs):
        raise cleanup.CleanupError("no mesh of at most 1500 triangles keeps every edge under 0.25 m for this box")

    monkeypatch.setattr(cleanup, "_lighter", stuck)
    failed = clean(v, f, dims, 5000, tri_target_light=1500)
    assert failed.glb == full and failed.light_glb is None and "keeps every edge" in failed.report.light["error"]

    # A light copy that is made but does not meet its level is not published either: here it was not reduced.
    monkeypatch.setattr(cleanup, "_lighter", lambda v, f, target, limit: (v, f))
    over = clean(v, f, dims, 5000, tri_target_light=1500)
    assert over.glb == full and over.light_glb is None and over.light_report is None
    assert over.report.light["error"].startswith("asset contract: [triangles.target]") and not over.report.violations


def test_orient_then_finish_is_clean():
    v, f = armchair()
    scan = profile.of_mesh(v, f, CUBE)
    oriented = orient(turned(v, 1), f, CUBE, 5000, scan=scan)
    assert (oriented.up_axis, oriented.turns, oriented.fit, oriented.tri_target) == ("Y", 3, 0.0, 5000)
    assert np.allclose(oriented.vertices.min(axis=0), [-0.5, 0.0, -0.5]) and len(oriented.candidates) == 12
    a = finish(oriented, (150, 112, 70), yaw=0.3, tri_target_light=1500)
    b = clean(turned(v, 1), f, CUBE, 5000, (150, 112, 70), yaw=0.3, scan=scan, tri_target_light=1500)
    assert a.glb == b.glb and a.light_glb == b.light_glb and a.report.to_dict() == b.report.to_dict()
