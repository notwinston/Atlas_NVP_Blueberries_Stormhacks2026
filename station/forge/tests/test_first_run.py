"""The first-run tool (tools/first_run.py): its reading of a chair mesh, and the round trip against a real server."""
import importlib.util
import io
import json
import socket
import threading
import time

import numpy as np
import pytest
from PIL import Image

from conftest import ROOT, jpeg, make_settings
from forge.app import create_app
from forge.cleanup import _UP, clean
from forge.generators.procedural import build_furniture

spec = importlib.util.spec_from_file_location("first_run", ROOT / "tools" / "first_run.py")
first_run = importlib.util.module_from_spec(spec)
spec.loader.exec_module(first_run)

DIMS = (0.45, 0.9, 0.5)


def turn(points, degrees):
    """About +Y, in the sense that moves Unity's +Z towards its +X (the points are right-handed: Unity's x is -x)."""
    a = np.radians(degrees)
    x, z = -points[:, 0], points[:, 2]
    return np.stack([-(x * np.cos(a) + z * np.sin(a)), points[:, 1], -x * np.sin(a) + z * np.cos(a)], axis=1)


def chair(front_azimuth=0.0):
    """A chair, upright and right-handed, whose front points at ``front_azimuth`` of Unity's box frame."""
    v, f = build_furniture("chair", DIMS)  # as built its back is on -Z: the front is at 0
    return turn(v, front_azimuth), f


def test_a_chair_with_its_front_on_plus_z_reads_canonical():
    v, f = chair(0)
    reading = first_run.read_frame(v, f)
    assert (reading["up"], reading["front"]) == ("Y", "canonical") and abs(reading["frontAzimuth"]) < 1.0
    # The map is seen from above with the front at the top: the back rest is the bottom line.
    lines = reading["map"].splitlines()
    assert len(lines) == 19 and "front" in lines[0] and "back" in lines[17]
    assert set(lines[16].strip()) == {"9"} and "9" not in lines[1]


@pytest.mark.parametrize("azimuth", [-45, 45, -30, 60])
def test_a_chair_turned_as_the_camera_saw_it_reads_view(azimuth):
    v, f = chair(azimuth)
    reading = first_run.read_frame(v, f)
    assert reading["up"] == "Y" and reading["front"] == "view"
    assert abs(reading["frontAzimuth"] - azimuth) < 12  # the two photos, left and right, give the two signs


@pytest.mark.parametrize("azimuth", [90, -90, 180, 135, -135])
def test_any_other_front_reads_unknown_and_gives_the_azimuth(azimuth):
    v, f = chair(azimuth)
    reading = first_run.read_frame(v, f)
    assert reading["up"] == "Y" and reading["front"] == "unknown"
    assert abs(abs(reading["frontAzimuth"]) - abs(azimuth)) < 12


@pytest.mark.parametrize("axis", ["Y", "Z", "X"])
def test_up_is_read_as_the_axis_clean_up_would_turn_upright(axis):
    v, f = chair(0)
    rotation = dict(_UP)[axis]
    raw = v @ rotation  # the frame from which clean-up's rotation for this axis gives the upright chair
    assert np.allclose(raw @ rotation.T, v)
    reading = first_run.read_frame(raw * 3.7 + 11.0, f)  # scale and place do not matter
    assert (reading["up"], reading["front"]) == (axis, "canonical")


def test_a_chair_on_its_head_or_an_object_that_is_not_tall_has_no_setting():
    v, f = chair(0)
    upside_down = v * [1, -1, -1]
    reading = first_run.read_frame(upside_down, f)
    assert reading["up"] is None and reading["front"] is None and "stands on its head" in " ".join(reading["notes"])
    cupboard = build_furniture("storage", (0.6, 0.6, 0.6))
    reading = first_run.read_frame(*cupboard)
    assert reading["up"] is None and "no axis is clearly the longest" in " ".join(reading["notes"])


def test_the_frame_is_read_from_a_glb_through_its_report(tmp_path):
    # The generator's frame is Z up. Clean-up rights the mesh, and for a box whose width and depth are the
    # other way round it also turns it by a quarter; the reading takes that turn out again, so the GLB and
    # the raw output say the same about the generator.
    cases = [(0, DIMS, 0, "canonical"), (0, (DIMS[2], DIMS[1], DIMS[0]), 90, "canonical"),
             (180, DIMS, 0, "unknown"), (-45, (0.67, 0.9, 0.67), 0, "view")]
    for k, (front, box, yaw, expected) in enumerate(cases):
        v, f = chair(front)
        raw = v @ dict(_UP)["Z"]
        result = clean(raw, f, box, 5000)
        assert (result.report.up_axis, result.report.yaw_degrees) == ("Z", yaw)
        path = tmp_path / f"{k}.glb"
        path.write_bytes(result.glb)
        reading = first_run.analyse_file(path, result.report.up_axis, result.report.yaw_degrees)
        assert reading["source"] == "glb" and (reading["up"], reading["front"]) == ("Z", expected)
        # The same mesh as the generator gave it, read with no report.
        np.savez(tmp_path / "raw.npz", vertices=raw, faces=f)
        raw_reading = first_run.analyse_file(tmp_path / "raw.npz")
        assert raw_reading["source"] == "raw" and (raw_reading["up"], raw_reading["front"]) == ("Z", expected)
        assert abs(abs(raw_reading["frontAzimuth"]) - abs(front)) < 12
        assert abs(abs(reading["frontAzimuth"]) - abs(front)) < 12


def test_photo_and_form():
    small = jpeg()
    path_bytes = io.BytesIO(small).getvalue()
    assert first_run.MAX_CROP_BYTES == 1024 * 1024
    body = first_run.multipart({"boxId": 1}, path_bytes)
    assert body.startswith(b"--" + first_run.BOUNDARY.encode()) and body.endswith(b"--\r\n")
    assert b'name="meta"\r\n\r\n{"boxId": 1}\r\n' in body and b'name="view0"; filename="view0.jpg"' in body


def test_a_large_photo_is_scaled_under_the_limit(tmp_path, capsys):
    rng = np.random.default_rng(1)
    big = tmp_path / "big.png"
    Image.fromarray(rng.integers(0, 255, (1500, 2000, 3), dtype=np.uint8)).save(big)
    assert big.stat().st_size > first_run.MAX_CROP_BYTES
    sent = first_run.prepare_photo(big)
    assert sent[:2] == b"\xff\xd8" and len(sent) <= first_run.MAX_CROP_BYTES
    assert max(Image.open(io.BytesIO(sent)).size) == 1024 and "scaled" in capsys.readouterr().out
    ok = tmp_path / "ok.jpg"
    ok.write_bytes(jpeg())
    assert first_run.prepare_photo(ok) == jpeg()


@pytest.fixture
def served(tmp_path):
    """The Forge behind a real HTTP port, with the procedural generator and the crops kept."""
    import uvicorn

    settings = make_settings(tmp_path / "data", debug_keep=True)
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        port = probe.getsockname()[1]
    server = uvicorn.Server(uvicorn.Config(create_app(settings), host="127.0.0.1", port=port, log_level="error",
                                           loop="asyncio"))
    thread = threading.Thread(target=server.run, daemon=True)
    thread.start()
    deadline = time.monotonic() + 10
    while not server.started:
        assert time.monotonic() < deadline, "the server did not start"
        time.sleep(0.02)
    yield f"http://127.0.0.1:{port}", settings
    server.should_exit = True
    thread.join(10)


def test_the_round_trip_against_a_running_forge(served, tmp_path, capsys):
    url, settings = served
    photo = tmp_path / "chair.jpg"
    photo.write_bytes(jpeg())
    out = tmp_path / "out"
    code = first_run.main([str(photo), "--dims", *map(str, DIMS), "--url", url, "--out", str(out),
                           "--data", str(settings.data_dir)])
    printed = capsys.readouterr().out
    assert code == 0, printed
    assert "health: generator procedural ready" in printed and "the validator accepts it" in printed
    assert "full target 15000, light target 5000" in printed
    assert " s  ready" in printed and "the job took" in printed
    record = json.loads((out / "first_run.json").read_text())
    assert record["job"]["state"] == "ready" and record["validator"]["ok"] is True
    assert (out / "asset.glb").read_bytes()[:4] == b"glTF" and (out / "raw_view0.npz").is_file()
    # The procedural chair is built upright with its front on +Z, and that is what the tool reads.
    assert record["reading"]["source"] == "raw" and record["reading"]["up"] == "Y"
    assert record["reading"]["front"] == "canonical" and abs(record["reading"]["frontAzimuth"]) < 1
    assert "FORGE_TRIPOSG_UP=Y" in printed and "FORGE_TRIPOSG_FRONT=canonical" in printed

    # Reading the saved files again, without the service.
    assert first_run.main(["--analyse", str(out / "raw_view0.npz")]) == 0
    assert first_run.main(["--analyse", str(out / "asset.glb"), "--up-axis", "Y"]) == 0
    assert capsys.readouterr().out.count("FORGE_TRIPOSG_FRONT=canonical") == 2


def test_a_forge_that_is_not_there_or_not_ready_stops_the_tool(tmp_path, capsys):
    photo = tmp_path / "chair.jpg"
    photo.write_bytes(jpeg())
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        port = probe.getsockname()[1]
    code = first_run.main([str(photo), "--dims", "0.45", "0.9", "0.5", "--url", f"http://127.0.0.1:{port}",
                           "--out", str(tmp_path / "out")])
    assert code == 2 and "Is the service running?" in capsys.readouterr().out
