"""The service around clean-up: attempts on a poor fit, the light copy, the colours, the record and `reclean`."""
import hashlib
import io
import json

import numpy as np
import pytest
from fastapi.testclient import TestClient
from PIL import Image

from conftest import jpeg, make_settings, meta_for, post_object, room_model, wait_for
from forge import cleanup, profile, reclean
from forge.app import create_app
from forge.generators import GeneratorError, ProceduralGenerator, RawMesh
from forge.generators.procedural import build_furniture
from forge.glb import read_glb
from forge.imaging import dominant_colour
from forge.validator import validate_glb
from test_colour import BLUE, BOX, RED, halves, look_at, render

CHAIR = (0.5, 0.9, 0.5)
# One colour per view, so a generation can be told by the photo it was given.
COLOURS = [(150, 112, 70), (40, 54, 92), (132, 54, 48)]


def chair_profile() -> str:
    return profile.encode(profile.of_mesh(*build_furniture("chair", CHAIR), CHAIR))


class Scripted(ProceduralGenerator):
    """Answers each generation with the shape named for it: a procedural category, or "fail".

    Against the profile of a chair these shapes fit as follows (lower is better; 0.20 is the poor-fit line):
    chair 0.00, bed 0.15, couch 0.16, toilet 0.23, plant 0.45, storage 0.46, lamp 0.46.
    """

    def __init__(self, *shapes):
        self.shapes, self.photos = list(shapes), []

    def generate(self, request):
        shape = self.shapes[min(len(self.photos), len(self.shapes) - 1)]
        self.photos.append(dominant_colour(request.image))
        if shape == "fail":
            raise GeneratorError("the model produced nothing")
        v, f = build_furniture(shape, request.dims)
        lo, hi = v.min(axis=0), v.max(axis=0)
        return RawMesh(vertices=(v - (lo + hi) * 0.5) / float((hi - lo).max()), faces=f)

    def views(self):
        """Which view each generation was given."""
        return [min(range(3), key=lambda k: sum(abs(a - b) for a, b in zip(photo, COLOURS[k])))
                for photo in self.photos]


def run(tmp_path, generator, *, views=3, with_profile=True, name="attempts", **overrides):
    """One chair through a Forge with ``generator``; returns ``(status, job, app)`` once the job has ended."""
    app = create_app(make_settings(tmp_path / name, **overrides), generator)
    with TestClient(app) as client:
        model = room_model(name, [CHAIR], ["chair"])
        assert client.post("/rooms", json=model).status_code == 201
        meta = meta_for(model["objects"][0], 15000)
        meta["views"] = meta["views"] * views
        if with_profile:
            meta["profile"] = chair_profile()
        photos = [jpeg(COLOURS[k], seed=k) for k in range(views)]
        job_id = post_object(client, name, meta, photos).json()["jobId"]
        status = wait_for(client, job_id)
        return status, app.state.forge.job(job_id), app


# --------------------------------------------------------------------------- attempts


def test_a_poor_fit_runs_views_one_and_two_and_publishes_the_best(tmp_path):
    generator = Scripted("storage", "toilet", "chair")
    status, job, _ = run(tmp_path, generator)
    assert status["state"] == "ready" and status["attempts"] == 3 and generator.views() == [0, 1, 2]
    assert status["fit"] == 0.0 and status["report"]["fit"] == 0.0 and status["report"]["attempts"] == 3
    log = job.attemptLog
    assert [(e["view"], e["published"], e["error"]) for e in log] == [(0, False, ""), (1, False, ""), (2, True, "")]
    assert log[0]["fit"] > 0.4 and 0.2 < log[1]["fit"] < 0.3 and log[2]["fit"] == 0.0
    assert all(e["seconds"] >= 0 and e["generator"] == "procedural" and e["fitUp"] == "Y" for e in log)
    # What was published is the chair: its back rest, the top 3 rows of the profile's -Z side, is the tall part.
    positions, faces = glb_mesh(status_glb(tmp_path, status))
    published = profile.of_mesh(positions, faces, CHAIR).reshape(16, 16)
    assert published[:2].min() == 255 and published[4:].max() < 140


def test_generation_stops_at_the_first_fit_that_is_not_poor(tmp_path):
    generator = Scripted("storage", "couch", "chair")
    status, job, _ = run(tmp_path, generator)
    assert status["state"] == "ready" and status["attempts"] == 2 and generator.views() == [0, 1]
    assert 0.1 < status["fit"] <= 0.2 and [e["published"] for e in job.attemptLog] == [False, True]

    generator = Scripted("chair", "storage", "storage")
    status, job, _ = run(tmp_path, generator, name="first")
    assert status["attempts"] == 1 and status["fit"] == 0.0 and generator.views() == [0]


def test_a_mesh_is_published_however_poor_its_fit(tmp_path):
    generator = Scripted("storage", "plant", "lamp")  # 0.46, 0.45, 0.46: all poor
    status, job, _ = run(tmp_path, generator)
    assert status["state"] == "ready" and status["attempts"] == 3
    assert status["fit"] > 0.2 and status["fit"] == min(e["fit"] for e in job.attemptLog)
    assert [e["published"] for e in job.attemptLog] == [False, True, False]  # the plant fitted best
    assert status["report"]["fit"] == status["fit"] and status["error"] == ""


def test_never_more_views_than_there_are_and_never_more_than_three_generations(tmp_path):
    generator = Scripted("storage")
    status, _, _ = run(tmp_path, generator, views=1)
    assert status["state"] == "ready" and status["attempts"] == 1 and status["fit"] > 0.4
    generator = Scripted("storage")
    status, _, _ = run(tmp_path, generator, views=2, name="two")
    assert status["attempts"] == 2 and generator.views() == [0, 1]


def test_without_a_profile_there_is_one_generation(tmp_path):
    generator = Scripted("storage", "chair", "chair")
    status, job, _ = run(tmp_path, generator, with_profile=False)
    assert status["state"] == "ready" and status["attempts"] == 1 and generator.views() == [0]
    assert "fit" not in status and status["report"]["fit"] is None and job.attemptLog[0]["fit"] is None
    assert status["fitUp"] == "Y" and status["fitYaw"] == 0


def test_the_poor_fit_line_is_a_setting(tmp_path):
    generator = Scripted("storage", "chair")
    status, _, _ = run(tmp_path, generator, poor_fit=0.5)
    assert status["attempts"] == 1 and 0.4 < status["fit"] <= 0.5


def test_a_third_party_is_not_sent_more_photos_for_a_poor_fit(tmp_path):
    class Elsewhere(Scripted):
        source = "hosted"  # as the hosted service when it is the Forge's one generator

    generator = Elsewhere("storage", "chair", "chair")
    status, job, _ = run(tmp_path, generator)
    assert status["state"] == "ready" and status["source"] == "hosted"
    assert status["attempts"] == 1 and generator.views() == [0] and status["fit"] > 0.4
    # A failure is still tried once more with the next view, as before.
    generator = Elsewhere("fail", "storage", "chair")
    status, job, _ = run(tmp_path, generator, name="retry")
    assert status["attempts"] == 2 and generator.views() == [0, 1] and status["fit"] > 0.4


def test_a_failed_generation_counts_as_one_of_the_three(tmp_path):
    # View 0 fails, view 1 is poor, view 2 fits.
    generator = Scripted("fail", "toilet", "chair")
    status, job, _ = run(tmp_path, generator)
    assert status["state"] == "ready" and status["attempts"] == 3 and status["fit"] == 0.0
    assert [(e["view"], bool(e["error"]), e["published"]) for e in job.attemptLog] == [
        (0, True, False), (1, False, False), (2, False, True)]
    assert "produced nothing" in job.attemptLog[0]["error"] and "fit" not in job.attemptLog[0]

    # A failure after a poor mesh: the third view is still tried, and the poor mesh is there if it fails too.
    generator = Scripted("storage", "fail", "chair")
    status, job, _ = run(tmp_path, generator, name="middle")
    assert status["attempts"] == 3 and status["fit"] == 0.0 and generator.views() == [0, 1, 2]
    generator = Scripted("storage", "fail", "fail")
    status, job, _ = run(tmp_path, generator, name="alone")
    assert status["state"] == "ready" and status["attempts"] == 3 and status["fit"] > 0.4
    assert [e["published"] for e in job.attemptLog] == [True, False, False]


def test_two_failures_with_no_mesh_end_the_job_as_before(tmp_path):
    generator = Scripted("fail", "fail", "chair")
    status, job, _ = run(tmp_path, generator)
    assert status["state"] == "failed" and status["error"].count("produced nothing") == 2
    assert generator.views() == [0, 1] and job.attempts == 2 and "attempts" not in status


def test_a_raw_mesh_clean_up_cannot_use_is_a_failed_generation(tmp_path):
    class Empty(Scripted):
        def generate(self, request):
            mesh = super().generate(request)
            return RawMesh(mesh.vertices, mesh.faces[:0]) if len(self.photos) == 1 else mesh

    generator = Empty("chair", "chair")
    status, job, _ = run(tmp_path, generator)
    assert status["state"] == "ready" and status["attempts"] == 2 and generator.views() == [0, 1]
    assert job.attemptLog[0]["error"].startswith("clean-up: the generator returned no usable triangles")


def test_a_mesh_that_does_not_meet_the_asset_contract_is_not_published(tmp_path):
    class Cube(Scripted):
        """Whatever the box, a cube: it cannot fill a long box within the stretch limit."""

        def generate(self, request):
            self.photos.append(dominant_colour(request.image))
            v, f = build_furniture("other", (1.0, 1.0, 1.0))
            return RawMesh(vertices=v - [0.0, 0.5, 0.0], faces=f)

    generator = Cube()
    app = create_app(make_settings(tmp_path / "data"), generator)
    with TestClient(app) as client:
        model = room_model("long", [(2.0, 0.5, 0.5)], ["couch"])
        client.post("/rooms", json=model)
        meta = meta_for(model["objects"][0], 15000)
        meta["views"] = meta["views"] * 2
        status = wait_for(client, post_object(client, "long", meta, [jpeg(seed=1), jpeg(seed=2)]).json()["jobId"])
        assert status["state"] == "failed" and status["error"].startswith("asset contract: [frame.bounds]")
        assert "sha256" not in status and len(generator.photos) == 1  # no profile: one generation, and no asset
        assert [p.name for p in (tmp_path / "data" / "assets").iterdir()] == []


def test_the_next_best_attempt_is_published_when_the_best_cannot_be_finished(tmp_path, monkeypatch):
    real = cleanup.regrid
    calls = []

    def fussy(vertices, faces, target, limit, minimum):
        calls.append(len(faces))
        if len(calls) == 1:  # the first mesh to be finished is the best fit
            raise cleanup.CleanupError("no mesh of at most 15000 triangles keeps every edge under 0.25 m for this box")
        return real(vertices, faces, target, limit, minimum)

    monkeypatch.setattr(cleanup, "regrid", fussy)
    generator = Scripted("storage", "toilet", "plant")  # 0.46, 0.23, 0.45: the toilet is the best
    status, job, _ = run(tmp_path, generator)
    assert status["state"] == "ready" and status["attempts"] == 3
    assert [e["published"] for e in job.attemptLog] == [False, False, True]  # the plant, the next best
    assert status["fit"] == job.attemptLog[2]["fit"]


# --------------------------------------------------------------------------- the light copy


def status_glb(tmp_path, status, which="sha256") -> bytes:
    (path,) = list(tmp_path.rglob(f"assets/{status[which]}.glb"))
    return path.read_bytes()


def glb_mesh(glb: bytes):
    file = read_glb(glb)
    primitive = file.doc["meshes"][0]["primitives"][0]
    return (file.accessor(primitive["attributes"]["POSITION"]).astype(np.float64),
            file.accessor(primitive["indices"]).reshape(-1, 3).astype(np.int64))


def test_the_light_copy_is_published_as_its_own_asset(tmp_path):
    app = create_app(make_settings(tmp_path / "data", public_url="http://station:8000"))
    with TestClient(app) as client:
        model = room_model("levels", [(2.0, 0.6, 1.6)], ["bed"])
        client.post("/rooms", json=model)
        meta = {**meta_for(model["objects"][0], 15000), "triTargetLight": 1600}
        status = wait_for(client, post_object(client, "levels", meta, [jpeg()]).json()["jobId"])
        assert status["state"] == "ready" and status["tris"] > 1600
        assert status["lightSha256"] != status["sha256"] and 1500 <= status["lightTris"] <= 1600
        assert status["lightUrl"] == f"http://station:8000/assets/{status['lightSha256']}.glb"
        full = client.get(f"/assets/{status['sha256']}.glb")
        light = client.get(f"/assets/{status['lightSha256']}.glb")
        assert light.status_code == 200 and light.headers["etag"] == f'"{status["lightSha256"]}"'
        assert hashlib.sha256(light.content).hexdigest() == status["lightSha256"]
        assert len(light.content) == status["lightBytes"] < len(full.content)
        dims = model["objects"][0]["size"]
        assert validate_glb(full.content, tri_target=15000, box=dims, level="full").ok
        verdict = validate_glb(light.content, tri_target=1600, box=dims, level="light")
        assert verdict.ok and verdict.triangles == status["lightTris"]
        assert status["report"]["light"] == {"sha256": status["lightSha256"], "bytes": status["lightBytes"],
                                             "triangles": status["lightTris"]}
        # The message for the Hub carries the same four members.
        ready = app.state.forge._asset_fields(app.state.forge.job(status["jobId"]))
        assert {k: ready[k] for k in ("lightSha256", "lightUrl", "lightBytes", "lightTris")} == {
            k: status[k] for k in ("lightSha256", "lightUrl", "lightBytes", "lightTris")}

        # The same photo again is answered from the cache, light copy included.
        again = post_object(client, "levels", meta, [jpeg()]).json()
        assert again["cached"] is True
        cached = client.get(f"/jobs/{again['jobId']}").json()
        assert cached["lightSha256"] == status["lightSha256"] and "attempts" not in cached
        # Another light target is another job: the same full copy, and a light copy of its own.
        other = {**meta, "triTargetLight": 2000}
        answer = post_object(client, "levels", other, [jpeg()])
        assert answer.status_code == 202
        second = wait_for(client, answer.json()["jobId"])
        assert second["sha256"] == status["sha256"] and second["lightSha256"] != status["lightSha256"]
        assert 1600 < second["lightTris"] <= 2000
        # Each is answered from the cache with its own light copy, though the full copy is one file.
        for sent, made in ((meta, status), (other, second)):
            hit = client.get(f"/jobs/{post_object(client, 'levels', sent, [jpeg()]).json()['jobId']}").json()
            assert hit["cached"] is True and hit["lightSha256"] == made["lightSha256"]
            assert hit["lightTris"] == made["lightTris"] and hit["report"]["light"] == made["report"]["light"]

        # Deleting the room removes both copies.
        assert client.delete("/rooms/levels").status_code == 204
        assert client.get(f"/assets/{status['lightSha256']}.glb").status_code == 404
        assert client.get(f"/assets/{status['sha256']}.glb").status_code == 404
    assert [p for p in (tmp_path / "data").rglob("*") if p.is_file()] == []


def test_the_answer_from_the_cache_names_the_light_copy(tmp_path):
    """The phone learns of a cached mesh from the answer to its upload alone: that answer names both copies."""
    settings = make_settings(tmp_path / "data", public_url="http://station:8000")
    light_keys = {"lightSha256", "lightUrl", "lightBytes", "lightTris"}
    with TestClient(create_app(settings)) as client:
        model = room_model("warm", [(2.0, 0.6, 1.6), (0.5, 0.9, 0.5)], ["bed", "chair"])
        client.post("/rooms", json=model)
        bed = {**meta_for(model["objects"][0], 15000), "triTargetLight": 1600}
        first = wait_for(client, post_object(client, "warm", bed, [jpeg(seed=1)]).json()["jobId"])
        assert first["state"] == "ready" and first["lightSha256"] != first["sha256"]

        # With a light copy: the four members are in the answer itself, the same as the job's.
        answer = post_object(client, "warm", bed, [jpeg(seed=1)])
        assert answer.status_code == 200
        body = answer.json()
        assert body["cached"] is True and body["sha256"] == first["sha256"]
        assert {k: body[k] for k in light_keys} == {k: first[k] for k in light_keys}
        assert body["lightUrl"] == f"http://station:8000/assets/{first['lightSha256']}.glb"
        assert set(body) == {"jobId", "cached", "sha256"} | light_keys
        assert client.get(f"/assets/{body['lightSha256']}.glb").status_code == 200
        # GET /jobs/{jobId} and GET /jobs?room= say the same of the cached job.
        one = client.get(f"/jobs/{body['jobId']}").json()
        listed = {j["jobId"]: j for j in client.get("/jobs", params={"room": "warm"}).json()}
        for status in (one, listed[body["jobId"]], listed[first["jobId"]]):
            assert {k: status[k] for k in light_keys} == {k: first[k] for k in light_keys}
        assert one["cached"] is True and one["source"] == "cache"

        # A light copy that is the full copy's own file is named too: the phone need not know they are one.
        chair = meta_for(model["objects"][1], 15000)
        made = wait_for(client, post_object(client, "warm", chair, [jpeg(seed=2)]).json()["jobId"])
        same = post_object(client, "warm", chair, [jpeg(seed=2)]).json()
        assert made["lightSha256"] == made["sha256"] == same["sha256"] == same["lightSha256"]

    # A cached mesh with no light copy recorded: an entry from before the light copy existed. The answer
    # is what it was then, and the job's status has no light member.
    cache = settings.data_dir / "cache"
    for entry in cache.glob("*.json"):
        record = json.loads(entry.read_text())
        if record["sha256"] != first["sha256"]:
            continue
        old_info = {k: v for k, v in record.items() if k not in ("lightSha256", "lightBytes", "lightTris",
                                                                  "fit", "fitYaw", "fitUp")}
        old_info["report"] = {k: v for k, v in record["report"].items() if k != "light"}
        (settings.data_dir / "assets" / f"{first['sha256']}.json").write_text(json.dumps(old_info))
        entry.write_text(json.dumps({"sha256": first["sha256"]}))  # the entry named only the asset
    with TestClient(create_app(settings)) as client:
        answer = post_object(client, "warm", bed, [jpeg(seed=1)])
        assert answer.status_code == 200 and answer.json() == {
            "jobId": answer.json()["jobId"], "cached": True, "sha256": first["sha256"]}
        status = client.get(f"/jobs/{answer.json()['jobId']}").json()
        assert status["state"] == "ready" and status["sha256"] == first["sha256"] and not light_keys & set(status)
        assert "fit" not in status and "fitUp" not in status and client.get(status["url"]).status_code == 200
        listed = client.get("/jobs", params={"room": "warm"}).json()
        assert not light_keys & set(listed[-1]) and listed[-1]["jobId"] == status["jobId"]


def test_a_cached_light_copy_whose_file_is_gone_is_not_promised(tmp_path):
    settings = make_settings(tmp_path / "data")
    with TestClient(create_app(settings)) as client:
        model = room_model("lost", [(2.0, 0.6, 1.6)], ["bed"])
        client.post("/rooms", json=model)
        bed = {**meta_for(model["objects"][0], 15000), "triTargetLight": 1600}
        first = wait_for(client, post_object(client, "lost", bed, [jpeg()]).json()["jobId"])
        (settings.data_dir / "assets" / f"{first['lightSha256']}.glb").unlink()
        answer = post_object(client, "lost", bed, [jpeg()])
        assert answer.status_code == 200 and set(answer.json()) == {"jobId", "cached", "sha256"}
        assert "lightSha256" not in client.get(f"/jobs/{answer.json()['jobId']}").json()


def test_an_old_style_job_still_works(tmp_path):
    # A phone from before the mesh design: one view, no profile, no light target, a target of 5,000.
    with TestClient(create_app(make_settings(tmp_path / "data"))) as client:
        model = room_model("old", [(2.0, 0.8, 0.9), (0.5, 0.9, 0.5)], ["couch", "chair"])
        client.post("/rooms", json=model)
        meta = meta_for(model["objects"][0], 5000)
        assert "profile" not in meta and "triTargetLight" not in meta and len(meta["views"]) == 1
        status = wait_for(client, post_object(client, "old", meta, [jpeg()]).json()["jobId"])
        assert status["state"] == "ready" and status["triTarget"] == 5000 and status["tris"] <= 5000
        # No fit, since there was no profile; one generation; and a light copy at the room's own light target.
        assert "fit" not in status and status["attempts"] == 1
        assert status["triTargetLight"] == 5000 and len(status["lightSha256"]) == 64
        # A reader that knows nothing of the new members finds everything it had before.
        for key in ("sha256", "url", "bytes", "tris", "maxEdge", "bounds", "palette", "report"):
            assert key in status
        assert status["report"]["upAxis"] == "Y" and status["report"]["yawDegrees"] == 0
        asset = client.get(status["url"])
        assert validate_glb(asset.content, tri_target=5000, box=model["objects"][0]["size"]).ok
        # Without a camera in the meta at all (`views` left out) it works the same.
        bare = {k: v for k, v in meta_for(model["objects"][1], 5000).items() if k != "views"}
        assert wait_for(client, post_object(client, "old", bare, [jpeg()]).json()["jobId"])["state"] == "ready"


def test_a_light_copy_that_fails_leaves_the_full_one_published(tmp_path, monkeypatch):
    def stuck(*args, **kwargs):
        raise cleanup.CleanupError("no mesh of at most 1600 triangles keeps every edge under 0.25 m for this box")

    monkeypatch.setattr(cleanup, "_lighter", stuck)
    with TestClient(create_app(make_settings(tmp_path / "data"))) as client:
        model = room_model("heavy", [(2.0, 0.6, 1.6)], ["bed"])
        client.post("/rooms", json=model)
        meta = {**meta_for(model["objects"][0], 15000), "triTargetLight": 1600}
        status = wait_for(client, post_object(client, "heavy", meta, [jpeg()]).json()["jobId"])
        assert status["state"] == "ready" and client.get(status["url"]).status_code == 200
        assert not {"lightSha256", "lightUrl", "lightBytes", "lightTris"} & set(status)
        assert "keeps every edge" in status["report"]["light"]["error"]
        assert any(note.startswith("no light copy") for note in status["report"]["notes"])


def test_a_light_copy_whose_file_is_gone_is_dropped_at_restart(tmp_path):
    settings = make_settings(tmp_path / "data")
    with TestClient(create_app(settings)) as client:
        model = room_model("gone", [(2.0, 0.6, 1.6)], ["bed"])
        client.post("/rooms", json=model)
        meta = {**meta_for(model["objects"][0], 15000), "triTargetLight": 1600}
        status = wait_for(client, post_object(client, "gone", meta, [jpeg()]).json()["jobId"])
    (settings.data_dir / "assets" / f"{status['lightSha256']}.glb").unlink()
    with TestClient(create_app(settings)) as client:
        again = client.get(f"/jobs/{status['jobId']}").json()
        assert again["state"] == "ready" and again["sha256"] == status["sha256"] and "lightSha256" not in again


# --------------------------------------------------------------------------- colours through the service


def photo_of_the_box(position=(0.0, 0.4, 2.5), size=384, focal=540):
    """A JPEG of the two-coloured box on the grey of the test crops, with the camera that took it."""
    m, k = look_at(position, (0.0, 0.4, 0.0), focal, size, size)
    rgb, _ = render([BOX], halves, m, k, size, size, background=(96, 96, 96))
    out = io.BytesIO()
    Image.fromarray(rgb).save(out, format="JPEG", quality=95)
    return out.getvalue(), {"score": 0.9, "azimuthDeg": 0.0, "intrinsics": list(k), "camToBox": m.reshape(-1).tolist()}


def test_the_photos_colour_the_faces_when_they_are_matted(tmp_path, matting):
    dims = [0.6, 0.8, 0.4]
    settings = make_settings(tmp_path / "data", debug_keep=True)
    with TestClient(create_app(settings)) as client:
        client.post("/rooms", json=room_model("colours", [dims]))  # a cupboard: near enough to the box of the photo
        front, front_meta = photo_of_the_box()
        side, side_meta = photo_of_the_box(position=(2.0, 0.4, 1.5))
        meta = {"boxId": 1, "category": "storage", "dims": dims, "yaw": 0.0, "triTarget": 15000,
                "views": [front_meta, side_meta]}
        status = wait_for(client, post_object(client, "colours", meta, [front, side]).json()["jobId"])
        assert status["state"] == "ready", status
        colour = status["report"]["colour"]
        assert colour["views"] == 2  # both photos were used, though one generation was made
        assert sorted(c["index"] for c in colour["cells"]) == sorted([RED.index, BLUE.index])
        assert status["palette"] == colour["cells"][0]["index"] == colour["paletteIndex"]
        for which, level in (("sha256", "full"), ("lightSha256", "light")):
            glb = client.get(f"/assets/{status[which]}.glb").content
            verdict = validate_glb(glb, box=dims, level=level)
            assert verdict.ok and verdict.palette_cells == sorted([RED.index, BLUE.index])
        # On the front, which both cameras saw: red on Unity's +X, which the file stores as -x. And the side
        # the second camera saw is red. (The back and the top, which neither saw, take the nearest seen colour.)
        file = read_glb(client.get(status["url"]).content)
        primitive = file.doc["meshes"][0]["primitives"][0]
        positions = file.accessor(primitive["attributes"]["POSITION"])
        uv = file.accessor(primitive["attributes"]["TEXCOORD_0"])
        cell = (np.floor((1.0 - uv[:, 1]) * 16) * 16 + np.floor(uv[:, 0] * 16)).astype(int)
        front = positions[:, 2] > 0.19
        assert (cell[front & (positions[:, 0] < -0.1)] == RED.index).all()
        assert (cell[front & (positions[:, 0] > 0.1)] == BLUE.index).all()
        assert (cell[positions[:, 0] < -0.29] == RED.index).all()
        # The record keeps the mattes beside the crops.
        folder = settings.data_dir / "jobs" / status["jobId"]
        assert sorted(p.name for p in folder.iterdir()) == [
            "full.glb", "job.json", "light.glb", "matte0.png", "matte1.png", "raw_view0.npz", "report.json",
            "view0.jpg", "view1.jpg"]
        with Image.open(folder / "matte0.png") as kept:
            assert kept.mode == "L" and kept.size == (384, 384) and 0.1 < np.asarray(kept).mean() / 255 < 0.6


def test_without_a_matte_the_object_keeps_one_cell(tmp_path):
    dims = [0.6, 0.8, 0.4]
    with TestClient(create_app(make_settings(tmp_path / "data"))) as client:
        client.post("/rooms", json=room_model("plain", [dims]))
        front, front_meta = photo_of_the_box()
        meta = {"boxId": 1, "category": "storage", "dims": dims, "yaw": 0.0, "triTarget": 15000, "views": [front_meta]}
        status = wait_for(client, post_object(client, "plain", meta, [front]).json()["jobId"])
        assert status["state"] == "ready" and status["report"]["colour"]["views"] == 0
        assert len(status["report"]["colour"]["cells"]) == 1


# --------------------------------------------------------------------------- the record and reclean


def test_reclean_reproduces_the_published_bytes_from_a_kept_record(tmp_path, capsys, matting):
    generator = Scripted("storage", "toilet", "chair")
    status, job, app = run(tmp_path, generator, debug_keep=True)
    settings = app.state.forge.settings
    folder = settings.data_dir / "jobs" / job.jobId
    assert sorted(p.name for p in folder.iterdir()) == [
        "full.glb", "job.json", "light.glb", "matte0.png", "matte1.png", "matte2.png", "raw_view0.npz",
        "raw_view1.npz", "raw_view2.npz", "report.json", "view0.jpg", "view1.jpg", "view2.jpg"]
    assert hashlib.sha256((folder / "full.glb").read_bytes()).hexdigest() == status["sha256"]
    assert hashlib.sha256((folder / "light.glb").read_bytes()).hexdigest() == status["lightSha256"]
    kept = json.loads((folder / "report.json").read_text())
    assert kept["full"] == status["report"] and kept["light"]["level"] == "light"

    done = reclean.reclean(folder, settings)
    assert done.same_full and done.same_light
    assert done.result.glb == (folder / "full.glb").read_bytes()
    assert done.result.light_glb == (folder / "light.glb").read_bytes()
    assert [(a["view"], a["fit"], a["published"]) for a in done.attempts] == [
        (e["view"], e["fit"], e["published"]) for e in job.attemptLog]
    assert done.result.report.to_dict() == {k: v for k, v in status["report"].items() if k != "attempts"}

    # The command line: by folder, and by job id with the data folder; it prints the report and the verdict.
    assert reclean.main([str(folder)]) == 0
    printed = capsys.readouterr().out
    assert "view 2: fit 0.0000, up Y, yaw 0  <- published" in printed and "view 0: fit 0.45" in printed
    assert printed.count("the same bytes as the published one") == 2
    out = tmp_path / "again"
    assert reclean.main([job.jobId, "--data", str(settings.data_dir), "--out", str(out), "--json"]) == 0
    answer = json.loads(capsys.readouterr().out)
    assert answer["sameBytesAsPublished"] == {"full": True, "light": True} and answer["report"]["fit"] == 0.0
    assert (out / "full.glb").read_bytes() == (folder / "full.glb").read_bytes()
    assert sorted(p.name for p in out.iterdir()) == ["full.glb", "light.glb", "report.json"]

    # Clean-up tuned: with another stretch limit the answer may differ, and the tool says whether it does.
    tuned = make_settings(settings.data_dir, max_anisotropy=0.0)
    changed = reclean.reclean(folder, tuned)
    assert changed.result is None or isinstance(changed.same_full, bool)
    # The record itself was not touched.
    assert hashlib.sha256((folder / "full.glb").read_bytes()).hexdigest() == status["sha256"]


def test_reclean_needs_a_kept_record(tmp_path, capsys):
    status, job, app = run(tmp_path, Scripted("chair"), views=1)  # FORGE_DEBUG_KEEP was not set
    folder = app.state.forge.settings.data_dir / "jobs" / job.jobId
    with pytest.raises(reclean.RecordError, match="no raw_view"):
        reclean.reclean(folder)
    assert reclean.main([str(folder)]) == 1 and "FAIL" in capsys.readouterr().out
    assert reclean.main([str(tmp_path / "nowhere")]) == 1
    assert "holds no job.json" in capsys.readouterr().out
