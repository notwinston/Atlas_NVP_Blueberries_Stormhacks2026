"""tools/meshcheck/check.py against a record made here with the procedural generator."""
import importlib.util
import json

import numpy as np
import pytest
from fastapi.testclient import TestClient
from PIL import Image

from conftest import ROOT, jpeg, make_settings, meta_for, post_object, room_model, wait_for
from forge import profile
from forge.app import create_app
from forge.generators.procedural import build_furniture

spec = importlib.util.spec_from_file_location("meshcheck", ROOT.parent.parent / "tools" / "meshcheck" / "check.py")
check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check)


def scanned(category, dims) -> np.ndarray:
    """The profile the phone would have measured of the real object: here, of the procedural shape."""
    return profile.of_mesh(*build_furniture(category, dims), dims)


def corner_only() -> np.ndarray:
    """A scan that saw one corner of the box: 16 of 256 cells."""
    cells = np.zeros((16, 16), dtype=np.uint8)
    cells[:4, :4] = 200
    return cells.reshape(-1)


# id, category, box, the scanned profile (or None), the facing rule that answered, and the true front.
OBJECTS = [
    (1, "chair", (0.5, 0.9, 0.5), "own", "profile", "+Z"),
    (2, "couch", (2.0, 0.8, 0.9), "own", "profile", "+X"),  # the phone's rule was a quarter turn off
    (3, "storage", (1.0, 1.8, 0.5), "own", "clearance", "+Z"),
    (4, "table", (1.2, 0.75, 0.8), "own", None, "none"),
    (5, "unknown", (0.4, 0.4, 0.4), "corner", None, "none"),  # too little seen: a plain box
    (6, "other", (0.5, 0.5, 0.5), "low", None, "none"),  # seen whole, and lower than the box: a poor fit
    (7, "unknown", (0.3, 0.3, 0.3), None, None, None),  # no profile, and not in truth.json
]


@pytest.fixture(scope="module")
def record(tmp_path_factory):
    """A Forge data folder after a session with FORGE_DEBUG_KEEP=1, and its truth.json."""
    root = tmp_path_factory.mktemp("record")
    settings = make_settings(root / "data", debug_keep=True)
    model = room_model("lab", [o[2] for o in OBJECTS], [o[1] for o in OBJECTS])
    profiles = {}
    for entry, (box_id, category, dims, kind, rule, _) in zip(model["objects"], OBJECTS):
        if kind is not None:
            cells = {"own": lambda: scanned(category, dims), "corner": corner_only,
                     "low": lambda: np.full(256, 100, dtype=np.uint8)}[kind]()
            profiles[box_id] = entry["profile"] = profile.encode(cells)
        if rule is not None:
            entry["facing"] = rule
    with TestClient(create_app(settings)) as client:
        created = client.post("/rooms", json=model)
        assert created.status_code == 201
        jobs = []
        for entry in model["objects"]:
            meta = meta_for(entry, created.json()["triTargets"][str(entry["id"])])
            if entry["id"] in profiles:
                meta["profile"] = profiles[entry["id"]]
            jobs.append(post_object(client, "lab", meta, [jpeg(seed=entry["id"])]).json()["jobId"])
        assert [wait_for(client, job)["state"] for job in jobs] == ["ready"] * len(jobs)
    truth = root / "truth.json"
    truth.write_text(json.dumps({str(o[0]): o[5] for o in OBJECTS if o[5] is not None}))
    return settings.data_dir, truth


def numbers(record, tmp_path, *extra):
    data, truth = record
    out = tmp_path / "numbers.json"
    code = check.main(["--data", str(data), "--room", "lab", "--truth", str(truth), "--json", str(out), *extra])
    return code, json.loads(out.read_text())


def test_the_numbers_of_m1_and_m2(record, tmp_path, capsys):
    code, result = numbers(record, tmp_path)
    printed = capsys.readouterr().out
    assert code == 1  # the couch faces wrong, and one object with a profile is a plain box
    by_id = {o["id"]: o for o in result["objects"]}
    assert result["room"] == "lab" and result["truth"] is True and result["meshedObjects"] == 7

    # M1, per object: the rule that answered, whether it was right, and whether the first shape is a plain box.
    assert [(o["facingRule"], o["facingRight"]) for o in result["objects"]] == [
        ("profile", True), ("profile", False), ("clearance", True), ("box", None), ("box", None), ("box", None),
        ("box", None)]
    assert [o["plainBox"] for o in result["objects"]] == [False, False, False, False, True, False, True]
    assert by_id[5]["coverage"] == 0.062 and by_id[6]["coverage"] == 1.0 and by_id[7]["profile"] is False

    # M2, per object: the attempts, the published fit, both copies.
    for o in result["objects"]:
        job = o["job"]
        assert job["state"] == "ready" and job["generator"] == "procedural" and job["fitUp"] == "Y"
        assert len(job["attempts"]) == 1 and job["attempts"][0]["published"] and job["attempts"][0]["seconds"] < 60
        assert job["full"]["ok"] and job["light"]["ok"] and 1500 <= job["light"]["triangles"] <= job["light"]["target"]
        assert job["full"]["triangles"] <= job["full"]["target"] and job["full"]["violations"] == []
    assert [by_id[k]["job"]["fit"] for k in (1, 2, 3, 4)] == [0.0] * 4 and by_id[7]["job"]["fit"] is None
    assert by_id[6]["job"]["fit"] > 0.5 and by_id[6]["job"]["attempts"][0]["fit"] == by_id[6]["job"]["fit"]
    # The front of each mesh, read from the mesh by the phone's own profile rule.
    assert by_id[1]["job"]["meshFront"] == "+Z" and by_id[1]["job"]["meshFacingRight"] is True
    assert by_id[2]["job"]["meshFront"] == "+Z" and by_id[2]["job"]["meshFacingRight"] is False  # the truth is +X
    # A cupboard is flat on top: the rule cannot read its front, and it is not judged.
    assert by_id[3]["job"]["meshFront"] is None and by_id[3]["job"]["meshFacingRight"] is None
    assert by_id[4]["job"]["meshFacingRight"] is None

    m1 = {item["check"]: item for item in result["checks"]["M1"]}
    m2 = {item["check"]: item for item in result["checks"]["M2"]}
    box = m1["no object with a profile is a plain box"]
    assert box["pass"] is False and box["text"] == "6 objects have a profile; plain boxes among them: 5 unknown"
    seats = m1["chairs and couches face right"]
    assert seats["pass"] is False and seats["text"] == "1 of 2 (50 %; 90 % asked)"
    assert m1["every wall-backed object faces right"]["pass"] is True
    assert m1["facing by the rule that answered"]["text"] == "clearance: 1 of 1, profile: 1 of 2"
    assert m1["plain boxes with no profile"]["text"] == "7 unknown"

    copies = m2["each meshed object has a full and a light copy that pass the validator"]
    assert copies["pass"] is True and copies["text"] == "7 of 7"
    assert m2["generation takes 60 s or less per attempt"]["pass"] is True
    median = m2["the median fit is 0.12 or better"]
    assert median["pass"] is True and median["text"].endswith("over 6 objects")
    faces = m2["generated meshes of objects with a front face right"]
    assert faces["pass"] is False and faces["text"].startswith("1 of 2 (50 %; 90 % asked)")
    assert "could not be read for: 3 storage" in faces["text"]
    poor = m2["objects above the poor-fit line of 0.2 after their attempts"]
    assert poor["pass"] is None and "6 other (0.6" in poor["text"] and "1 chair" not in poor["text"]

    # The same in words.
    assert "1 chair  0.5 x 0.9 x 0.5 m" in printed and "facing by `profile`, truth +Z: right" in printed
    assert "truth +X: WRONG" in printed and "first shape: a plain box (profile covers 0.06)" in printed
    assert "attempt from view 0: fit 0.000" in printed and "<- published" in printed
    assert "full copy: 1500 triangles (target 15000)" in printed and "light copy:" in printed
    assert "FAIL  chairs and couches face right: 1 of 2" in printed
    assert "PASS  every wall-backed object faces right: 1 of 1" in printed


def test_without_truth_nothing_about_facing_is_judged(record, tmp_path, capsys):
    data, _ = record
    code = check.main(["--data", str(data), "--room", "lab", "--json", str(tmp_path / "n.json")])
    result = json.loads((tmp_path / "n.json").read_text())
    assert code == 1  # the plain box with a profile is still a failed line
    assert result["truth"] is False and all(o["facingRight"] is None for o in result["objects"])
    lines = {item["check"]: item for name in ("M1", "M2") for item in result["checks"][name]}
    assert lines["chairs and couches face right"]["pass"] is None
    assert "no truth.json" in lines["chairs and couches face right"]["text"]
    assert lines["generated meshes of objects with a front face right"]["pass"] is None
    assert "no truth.json, so facing is not judged" in capsys.readouterr().out
    # A truth file beside the room is found by itself.
    (data / "rooms" / "lab" / "truth.json").write_text(json.dumps({"1": "+Z"}))
    try:
        check.main(["--data", str(data), "--room", "lab", "--json", str(tmp_path / "n.json")])
        assert json.loads((tmp_path / "n.json").read_text())["objects"][0]["facingRight"] is True
    finally:
        (data / "rooms" / "lab" / "truth.json").unlink()
    capsys.readouterr()


def test_the_poor_fit_line_and_the_pictures(record, tmp_path, capsys):
    code, result = numbers(record, tmp_path, "--poor-fit", "0.9", "--pictures", str(tmp_path / "pictures"))
    poor = [item for item in result["checks"]["M2"] if item["check"].startswith("objects above the poor-fit line")]
    assert poor[0]["text"] == "none" and "0.9" in poor[0]["check"]
    pictures = sorted(p.name for p in (tmp_path / "pictures").iterdir())
    assert pictures == ["001_chair.png", "002_couch.png", "003_storage.png", "004_table.png", "005_unknown.png",
                        "006_other.png", "007_unknown.png"]
    with Image.open(tmp_path / "pictures" / "001_chair.png") as picture:
        pixels = np.asarray(picture)
    # The scan on the left and the mesh on the right, the front (+Z) at the top: the back rest is the bottom band.
    assert pixels.shape == (16 * 12, 34 * 12) and pixels[-6, 6] == 255 and pixels[-6, -6] == 255
    assert pixels[6, 6] < 140 and np.array_equal(pixels[:, :192], pixels[:, -192:])
    capsys.readouterr()


def test_a_record_that_cannot_be_read(record, tmp_path, capsys):
    data, truth = record
    assert check.main(["--data", str(data), "--room", "nowhere"]) == 2
    assert "holds no room.json" in capsys.readouterr().out
    assert check.main(["--data", str(data), "--room", "lab", "--truth", str(tmp_path / "missing.json")]) == 2
    bad = tmp_path / "bad.json"
    bad.write_text(json.dumps({"1": "front"}))
    assert check.main(["--data", str(data), "--room", "lab", "--truth", str(bad)]) == 2
    assert "must be one of" in capsys.readouterr().out


def test_the_front_of_a_shape_by_the_phones_profile_rule():
    chair = scanned("chair", (0.5, 0.9, 0.5))
    assert check.front_by_profile(chair) == "+Z"
    # `rotate(profile, k)` is the profile after the box's yaw grew by k quarter turns: the front goes round with it.
    assert [check.front_by_profile(profile.rotate(chair, k)) for k in range(4)] == ["+Z", "-X", "-Z", "+X"]
    grid = chair.reshape(16, 16)
    mean, seen = check.strip_mean(grid, 0)
    assert seen == 48 and mean == pytest.approx((2 * 255 + 122) / 3 / 255, abs=0.01)
    # No answer: a flat top, a scan that saw too little, a back that is hardly higher than the rest.
    assert check.front_by_profile(scanned("table", (1.2, 0.75, 0.8))) is None
    assert check.front_by_profile(corner_only()) is None
    low_back = np.full((16, 16), 200, dtype=np.uint8)
    low_back[:3] = 230  # 0.12 of the height above the rest: under the margin of 0.15
    assert check.front_by_profile(low_back.reshape(-1)) is None
    low_back[:3] = 255  # 0.22 above the rest, and 0.18 above the side strips, which hold three of its rows
    assert check.front_by_profile(low_back.reshape(-1)) == "+Z"
