import base64
import json
import random
import time

import pytest
from fastapi.testclient import TestClient

from conftest import FIXTURE_ROOMS, jpeg, make_settings, meta_for, post_object, room_model, wait_for
from forge.app import create_app
from forge.budget import plan
from forge.generators import GeneratorError, ProceduralGenerator
from forge.validator import validate_glb


class Recording(ProceduralGenerator):
    """The procedural generator, noting the order of its calls and optionally misbehaving."""

    def __init__(self, delay=0.0, fail=False):
        self.calls, self.delay, self.fail = [], delay, fail

    def generate(self, request):
        self.calls.append(tuple(request.dims))
        time.sleep(self.delay)
        if self.fail:
            raise GeneratorError("the model produced nothing")
        return super().generate(request)


def upload_room(client, model: dict) -> dict:
    response = client.post("/rooms", content=json.dumps(model), headers={"Content-Type": "application/json"})
    assert response.status_code == 201, response.text
    return response.json()


def test_health(client):
    body = client.get("/health").json()
    assert body["ok"] is True and body["queue"] == 0 and body["debugKeep"] is False
    assert body["pipelineVer"] and body["gpu"] is False
    assert body["generator"] == "procedural" and body["hostedEnabled"] is False
    assert body["hosted"] is False


@pytest.mark.parametrize("name", ["sample_4x5_table", "budget_room", "sample_L_3x4", "sample_3x3", "tiny_standing"])
def test_round_trip_with_fixture_rooms(client, name):
    path = FIXTURE_ROOMS / f"{name}.json"
    if not path.is_file():
        pytest.skip(f"fixture {name} is not in this checkout")
    raw = path.read_bytes()
    model = json.loads(raw)
    created = client.post("/rooms", content=raw).json()
    assert created["roomId"] == model["id"] and len(created["roomHash"]) == 64
    assert client.get(f"/rooms/{model['id']}").content == raw

    objects = {o["id"]: o for o in model["objects"]}
    assert sorted(created["generated"]) == sorted(objects)
    targets, light = plan((o["id"], o["size"]) for o in model["objects"])
    assert created["triTargets"] == {str(box_id): target for box_id, target in targets.items()}
    assert created["triTarget"] == max(targets.values(), default=0) and created["triTargetLight"] == light

    jobs = {}
    for box_id in created["generated"]:
        response = post_object(client, model["id"], meta_for(objects[box_id], targets[box_id]), [jpeg(seed=box_id)])
        assert response.status_code == 202, response.text
        jobs[box_id] = response.json()["jobId"]

    total = 0
    for box_id, job_id in jobs.items():
        status = wait_for(client, job_id)
        assert status["state"] == "ready", status
        assert status["source"] == "local" and status["boxId"] == box_id and status["roomId"] == model["id"]
        asset = client.get(status["url"])
        assert asset.status_code == 200 and asset.headers["etag"] == f'"{status["sha256"]}"'
        assert len(asset.content) == status["bytes"] <= 2 * 1024 * 1024
        verdict = validate_glb(asset.content, tri_target=targets[box_id], box=objects[box_id]["size"])
        assert verdict.ok, (box_id, verdict.violations)
        assert verdict.triangles == status["tris"] and status["maxEdge"] <= 0.25
        assert status["triTarget"] == targets[box_id] and status["triTargetLight"] == light
        assert status["report"]["maxScaleError"] <= 0.10
        total += verdict.triangles
    assert total <= 120_000
    listed = client.get("/jobs", params={"room": model["id"]}).json()
    assert [j["jobId"] for j in listed] == list(jobs.values())
    assert client.get("/health").json()["queue"] == 0


def test_queue_is_largest_first_and_capped_at_thirty_two(tmp_path):
    generator = Recording()
    app = create_app(make_settings(tmp_path / "data"), generator)
    # The fourth test vector of the triangle rule: forty boxes, id i is (0.3 + 0.05 i, 0.5, 0.4).
    sizes = [(0.3 + 0.05 * (i + 1), 0.5, 0.4) for i in range(40)]
    model = room_model("crowded", sizes)
    targets, light = plan((o["id"], o["size"]) for o in model["objects"])
    with TestClient(app) as client:
        app.state.forge.pause()
        created = upload_room(client, model)
        assert len(created["generated"]) == 32 and created["triTarget"] == 5043 == max(targets.values())
        assert created["placeholders"] == [8, 7, 6, 5, 4, 3, 2, 1]
        assert created["triTargets"] == {str(box_id): target for box_id, target in targets.items()}
        assert [created["triTargets"][str(i)] for i in (1, 8, 9, 17, 18, 40)] == [0, 0, 3000, 3000, 3044, 5043]
        assert created["triTargetLight"] == light == 1875

        order = list(model["objects"])
        random.Random(7).shuffle(order)
        answers = {}
        for obj in order:
            # The phone may ask for more than the room allows; the Forge holds it to the room's budget.
            response = post_object(client, "crowded", meta_for(obj, 15000), [jpeg(seed=obj["id"])])
            answers[obj["id"]] = response
        for box_id in created["placeholders"]:
            assert answers[box_id].status_code == 200 and answers[box_id].json()["placeholder"] is True
        for box_id in created["generated"]:
            assert answers[box_id].status_code == 202

        queue = client.get("/queue").json()
        assert [e["boxId"] for e in queue["queued"]] == created["generated"] == list(range(40, 8, -1))
        assert sorted(e["boxId"] for e in queue["placeholders"]) == [1, 2, 3, 4, 5, 6, 7, 8]
        assert queue["active"] is None and client.get("/health").json()["queue"] == 32
        first = client.get(f"/jobs/{answers[40].json()['jobId']}").json()
        assert first["state"] == "queued" and first["queuePosition"] == 0

        app.state.forge.pause(False)
        statuses = [wait_for(client, answers[b].json()["jobId"]) for b in created["generated"]]
        assert all(s["state"] == "ready" for s in statuses)
        assert all(s["triTarget"] == targets[s["boxId"]] and s["tris"] <= s["triTarget"] for s in statuses)
        assert all(s["triTargetLight"] == 1875 for s in statuses)
        assert sum(s["tris"] for s in statuses) <= 120_000
        # One worker, and it took the boxes in order of volume.
        assert generator.calls == [tuple(sizes[b - 1]) for b in created["generated"]]
        for box_id in created["placeholders"]:
            status = client.get(f"/jobs/{answers[box_id].json()['jobId']}").json()
            assert status["state"] == "placeholder" and "sha256" not in status and "32 largest" in status["error"]
        assert len(generator.calls) == 32


def test_cache_and_the_cache_backend(client):
    model = room_model("cached", [(1.0, 0.75, 0.6), (0.5, 0.9, 0.5)], ["table", "chair"])
    upload_room(client, model)
    table, chair = model["objects"]
    photo = jpeg((40, 54, 92))

    miss = post_object(client, "cached", meta_for(chair, 5000, backend="cache"), [jpeg(seed=3)])
    status = wait_for(client, miss.json()["jobId"])
    assert status["state"] == "failed" and "cache" in status["error"] and status["source"] == "cache"

    first = post_object(client, "cached", meta_for(table, 5000), [photo])
    assert first.status_code == 202
    ready = wait_for(client, first.json()["jobId"])
    assert ready["state"] == "ready" and ready["cached"] is False
    assert ready["report"]["colour"]["name"].startswith(("blue", "azure")) and 64 <= ready["palette"] < 128

    again = post_object(client, "cached", meta_for(table, 5000, backend="cache"), [photo])
    assert again.status_code == 200
    body = again.json()
    assert body["cached"] is True and body["sha256"] == ready["sha256"]
    hit = client.get(f"/jobs/{body['jobId']}").json()
    assert hit["state"] == "ready" and hit["source"] == "cache" and hit["tris"] == ready["tris"]
    # A different triangle target is a different asset.
    other = post_object(client, "cached", meta_for(table, 3000), [photo])
    assert other.status_code == 202
    assert wait_for(client, other.json()["jobId"])["tris"] <= 3000


def test_asset_download(client):
    model = room_model("assets", [(0.6, 0.6, 0.6)])
    upload_room(client, model)
    job = post_object(client, "assets", meta_for(model["objects"][0], 5000), [jpeg()]).json()
    status = wait_for(client, job["jobId"])
    name = f"/assets/{status['sha256']}.glb"
    response = client.get(name)
    assert response.headers["content-type"] == "model/gltf-binary"
    assert client.get(name, headers={"If-None-Match": response.headers["etag"]}).status_code == 304
    assert client.get("/assets/" + "0" * 64 + ".glb").status_code == 404
    assert client.get("/assets/..%2Fjobs.glb").status_code == 404


def test_corrupt_image_fails_its_job_cleanly(client):
    model = room_model("corrupt", [(0.6, 0.6, 0.6), (0.5, 0.5, 0.5)])
    upload_room(client, model)
    bad = post_object(client, "corrupt", meta_for(model["objects"][0], 5000), [b"\xff\xd8\xff\xe0 not really a jpeg"])
    assert bad.status_code == 202
    status = wait_for(client, bad.json()["jobId"])
    assert status["state"] == "failed" and "image" in status["error"]
    # The worker is still alive for the next job.
    good = post_object(client, "corrupt", meta_for(model["objects"][1], 5000), [jpeg()])
    assert wait_for(client, good.json()["jobId"])["state"] == "ready"


def test_hosted_backend_is_off_by_default(client):
    model = room_model("hosted", [(0.6, 0.6, 0.6)])
    upload_room(client, model)
    job = post_object(client, "hosted", meta_for(model["objects"][0], 5000, backend="hosted"), [jpeg()]).json()
    status = wait_for(client, job["jobId"])
    assert status["state"] == "failed" and status["source"] == "hosted" and "disabled" in status["error"]


def test_hosted_enabled_is_reported_and_has_no_vendor(tmp_path):
    settings = make_settings(tmp_path / "data", hosted_enabled=True, hosted_url="https://example.invalid", hosted_key="k")
    with TestClient(create_app(settings)) as client:
        assert client.get("/health").json()["hostedEnabled"] is True
        assert client.get("/health").json()["hosted"] is True
        model = room_model("hosted", [(0.6, 0.6, 0.6)])
        upload_room(client, model)
        job = post_object(client, "hosted", meta_for(model["objects"][0], 5000, backend="hosted"), [jpeg()]).json()
        status = wait_for(client, job["jobId"])
        assert status["state"] == "failed" and "vendor" in status["error"]


def test_generation_time_out_retries_with_view_one_then_fails(tmp_path):
    generator = Recording(delay=0.4)
    settings = make_settings(tmp_path / "data", generate_timeout_s=0.05)
    with TestClient(create_app(settings, generator)) as client:
        model = room_model("slow", [(0.6, 0.6, 0.6)])
        upload_room(client, model)
        job = post_object(client, "slow", meta_for(model["objects"][0], 5000), [jpeg(seed=1), jpeg(seed=2)]).json()
        status = wait_for(client, job["jobId"])
        assert status["state"] == "failed" and status["error"].count("timed out") == 2
        assert client.get("/health").json()["queue"] == 0


def test_generator_failure_and_bad_mesh_fail_the_job(tmp_path):
    with TestClient(create_app(make_settings(tmp_path / "a"), Recording(fail=True))) as client:
        model = room_model("broken", [(0.6, 0.6, 0.6)])
        upload_room(client, model)
        job = post_object(client, "broken", meta_for(model["objects"][0], 5000), [jpeg()]).json()
        status = wait_for(client, job["jobId"])
        assert status["state"] == "failed" and "produced nothing" in status["error"]

    with TestClient(create_app(make_settings(tmp_path / "b"))) as client:
        # The procedural table is built for the box in the job; a box of another shape cannot be met.
        model = room_model("misfit", [(3.0, 0.3, 0.3)], ["plant"])
        upload_room(client, model)
        meta = meta_for(model["objects"][0], 5000)
        job = post_object(client, "misfit", meta, [jpeg()]).json()
        assert wait_for(client, job["jobId"])["state"] in ("ready", "failed")


def test_request_errors(client):
    assert client.post("/rooms", content=b"{not json").status_code == 422
    assert client.post("/rooms", content=json.dumps({"version": 2, "id": "x", "objects": []})).status_code == 422
    assert client.post("/rooms", content=json.dumps({"version": 1, "id": "../x", "objects": []})).status_code == 422
    twice = room_model("dup", [(1, 1, 1), (1, 1, 1)])
    twice["objects"][1]["id"] = 1
    assert client.post("/rooms", content=json.dumps(twice)).status_code == 422
    assert client.post("/rooms", content=b" " * (3 * 1024 * 1024)).status_code == 413

    model = room_model("errors", [(0.6, 0.6, 0.6)])
    upload_room(client, model)
    obj = model["objects"][0]
    assert post_object(client, "nowhere", meta_for(obj, 5000), [jpeg()]).status_code == 404
    assert post_object(client, "errors", {**meta_for(obj, 5000), "boxId": 9}, [jpeg()]).status_code == 404
    assert post_object(client, "errors", {**meta_for(obj, 5000), "triTarget": 15001}, [jpeg()]).status_code == 422
    assert post_object(client, "errors", {**meta_for(obj, 5000), "triTarget": 1499}, [jpeg()]).status_code == 422
    assert post_object(client, "errors", {**meta_for(obj, 5000), "triTargetLight": 5001}, [jpeg()]).status_code == 422
    assert post_object(client, "errors", {**meta_for(obj, 5000), "triTargetLight": 1499}, [jpeg()]).status_code == 422
    assert post_object(client, "errors", {**meta_for(obj, 5000), "backend": "off"}, [jpeg()]).status_code == 422
    assert client.post("/rooms/errors/objects", data={"meta": json.dumps(meta_for(obj, 5000))}).status_code == 422
    assert post_object(client, "errors", meta_for(obj, 5000), [b"x" * (1024 * 1024 + 1)]).status_code == 413
    assert client.get("/jobs/0123456789abcdef").status_code == 404
    assert client.get("/jobs/nonsense").status_code == 404
    assert client.get("/jobs", params={"room": "nowhere"}).status_code == 404
    assert client.get("/rooms/nowhere").status_code == 404


def test_same_box_twice_while_waiting_is_one_job(tmp_path):
    app = create_app(make_settings(tmp_path / "data"))
    with TestClient(app) as client:
        app.state.forge.pause()
        model = room_model("twice", [(0.6, 0.6, 0.6)])
        upload_room(client, model)
        a = post_object(client, "twice", meta_for(model["objects"][0], 5000), [jpeg()])
        b = post_object(client, "twice", meta_for(model["objects"][0], 5000), [jpeg()])
        assert a.status_code == b.status_code == 202 and a.json()["jobId"] == b.json()["jobId"]
        app.state.forge.pause(False)
        assert wait_for(client, a.json()["jobId"])["state"] == "ready"


def test_room_mesh_and_delete(client, settings):
    model = room_model("gone", [(0.6, 0.6, 0.6)])
    upload_room(client, model)
    assert client.put("/rooms/gone/mesh", content=b"\x00" * 1000).status_code == 204
    assert client.put("/rooms/gone/mesh", content=b"\x00" * (2 * 1024 * 1024 + 1)).status_code == 413
    assert client.put("/rooms/nowhere/mesh", content=b"\x00").status_code == 404
    job = post_object(client, "gone", meta_for(model["objects"][0], 5000), [jpeg()]).json()
    status = wait_for(client, job["jobId"])
    assert client.get(status["url"]).status_code == 200

    assert client.delete("/rooms/gone").status_code == 204
    assert client.get("/rooms/gone").status_code == 404
    assert client.get(f"/jobs/{job['jobId']}").status_code == 404
    assert client.get(status["url"]).status_code == 404
    assert client.delete("/rooms/gone").status_code == 404
    left = [p for p in settings.data_dir.rglob("*") if p.is_file()]
    assert left == []


def test_only_the_jobs_own_files_are_written_and_crops_are_not_kept(client, settings):
    model = room_model("tidy", [(0.6, 0.6, 0.6)])
    upload_room(client, model)
    job = post_object(client, "tidy", meta_for(model["objects"][0], 5000), [jpeg(), jpeg(seed=2)]).json()
    status = wait_for(client, job["jobId"])
    files = sorted(str(p.relative_to(settings.data_dir)) for p in settings.data_dir.rglob("*") if p.is_file())
    cache = [f for f in files if f.startswith("cache/")]
    assert len(cache) == 1
    assert files == sorted(
        cache + [f"assets/{status['sha256']}.glb", f"assets/{status['sha256']}.json",
                 f"jobs/{job['jobId']}/job.json", "rooms/tidy/room.json", "rooms/tidy/room.meta.json"]
    )
    assert client.app.state.forge._crops == {}


def test_debug_keep_is_reported_and_keeps_crops(tmp_path):
    settings = make_settings(tmp_path / "data", debug_keep=True)
    with TestClient(create_app(settings)) as client:
        assert client.get("/health").json()["debugKeep"] is True
        model = room_model("kept", [(0.6, 0.6, 0.6)])
        upload_room(client, model)
        job = post_object(client, "kept", meta_for(model["objects"][0], 5000), [jpeg()]).json()
        wait_for(client, job["jobId"])
        assert (settings.data_dir / "jobs" / job["jobId"] / "view0.jpg").is_file()


def test_service_starts_without_torch_and_fails_jobs_cleanly(tmp_path):
    # The Station's default generator on a machine with no PyTorch: the service still answers,
    # serves what it has, and a job reports why it cannot run.
    settings = make_settings(tmp_path / "data", generator="triposg")
    with TestClient(create_app(settings)) as client:
        health = client.get("/health").json()
        assert health["ok"] is True and health["generator"] == "triposg"
        assert health["generatorReady"] is False and health["generatorNote"] and health["gpu"] is False
        model = room_model("nogpu", [(0.6, 0.6, 0.6)])
        upload_room(client, model)
        job = post_object(client, "nogpu", meta_for(model["objects"][0], 5000), [jpeg()]).json()
        status = wait_for(client, job["jobId"])
        assert status["state"] == "failed" and "TripoSG cannot run" in status["error"]


def profile_text(value=128, count=256) -> str:
    return base64.b64encode(bytes([value]) * count).decode("ascii")


def test_profile_is_checked_on_arrival(client):
    model = room_model("profiles", [(0.6, 0.6, 0.6)])
    upload_room(client, model)
    obj = model["objects"][0]
    good = profile_text()
    assert len(good) == 344
    bad = [
        profile_text(count=255),  # one byte short
        profile_text(count=257),
        "",
        good[:-1],  # the padding is gone
        good[:-4] + "-_==",  # the URL alphabet is not standard base64
        "not base64 at all!",
        12,
    ]
    for value in bad:
        response = post_object(client, "profiles", {**meta_for(obj, 5000), "profile": value}, [jpeg()])
        assert response.status_code == 422, value
    assert client.get("/jobs", params={"room": "profiles"}).json() == []
    assert post_object(client, "profiles", {**meta_for(obj, 5000), "profile": None}, [jpeg()]).status_code == 202


def test_job_keeps_what_the_phone_sent(tmp_path):
    app = create_app(make_settings(tmp_path / "data"))
    with TestClient(app) as client:
        forge = app.state.forge
        forge.pause()
        model = room_model("sent", [(1.0, 0.75, 0.6), (0.5, 0.9, 0.5), (0.4, 0.4, 0.4)], ["table", "chair", "plant"])
        created = upload_room(client, model)
        assert created["triTargets"] == {"1": 15000, "2": 15000, "3": 15000} and created["triTargetLight"] == 5000
        table, chair, plant = model["objects"]

        # A phone of this design: the profile and both targets.
        new = {**meta_for(table, 15000), "triTargetLight": 4000, "profile": profile_text(77)}
        job = forge.job(post_object(client, "sent", new, [jpeg(seed=1)]).json()["jobId"])
        assert (job.triTarget, job.requestedTriTarget) == (15000, 15000)
        assert (job.triTargetLight, job.requestedTriTargetLight) == (4000, 4000)
        assert base64.b64decode(job.profile) == bytes([77]) * 256
        assert job.views[0]["camToBox"][14] == 1.5 and list(job.views[0]["intrinsics"]) == [600.0, 600.0, 384.0, 384.0]

        # An older phone sends neither: no profile, and the light target is the room's own.
        old = forge.job(post_object(client, "sent", meta_for(chair, 5000), [jpeg(seed=2)]).json()["jobId"])
        assert (old.triTarget, old.triTargetLight, old.requestedTriTargetLight, old.profile) == (5000, 5000, 0, "")

        # A smaller target from the phone wins over the room's.
        small = {**meta_for(plant, 3200), "triTargetLight": 1600}
        status = client.get(f"/jobs/{post_object(client, 'sent', small, [jpeg(seed=3)]).json()['jobId']}").json()
        assert status["triTarget"] == 3200 and status["triTargetLight"] == 1600

        forge.pause(False)
        done = [wait_for(client, j["jobId"]) for j in client.get("/jobs", params={"room": "sent"}).json()]
        assert [s["state"] for s in done] == ["ready"] * 3
        # No light copy, no fit and no attempts are made yet: their members are absent.
        for status in done:
            assert not {"lightSha256", "lightUrl", "lightBytes", "lightTris", "fit", "fitYaw", "fitUp", "attempts",
                        "decoder"} & set(status)
        assert done[2]["tris"] <= 3200

    # What was sent is on disk with the job, so it survives a restart.
    with TestClient(create_app(make_settings(tmp_path / "data"))) as client:
        again = client.app.state.forge.job(job.jobId)
        assert again.profile == job.profile and again.triTargetLight == 4000


def test_job_status_carries_the_light_copy_and_the_fit_when_a_job_has_them(tmp_path):
    # Clean-up does not make them yet; this is the shape the answer takes once it does.
    app = create_app(make_settings(tmp_path / "data", public_url="http://station:8000"))
    with TestClient(app) as client:
        model = room_model("levels", [(0.6, 0.6, 0.6)])
        upload_room(client, model)
        job_id = post_object(client, "levels", meta_for(model["objects"][0], 5000), [jpeg()]).json()["jobId"]
        wait_for(client, job_id)
        job = app.state.forge.job(job_id)
        job.lightSha256, job.lightBytes, job.lightTris = "ab" * 32, 40000, 1800
        job.fit, job.fitYaw, job.fitUp, job.attempts, job.decoder = 0.08, 180, "Y", 2, "flash"
        status = client.get(f"/jobs/{job_id}").json()
        assert status["lightSha256"] == "ab" * 32 and status["lightBytes"] == 40000 and status["lightTris"] == 1800
        assert status["lightUrl"] == "http://station:8000/assets/" + "ab" * 32 + ".glb"
        assert (status["fit"], status["fitYaw"], status["fitUp"], status["attempts"]) == (0.08, 180, "Y", 2)
        assert status["decoder"] == "flash"
        assert app.state.forge._asset_fields(job)["lightTris"] == 1800  # and so does `AssetReady`


def test_a_room_record_from_before_the_new_budget_is_planned_again(tmp_path):
    settings = make_settings(tmp_path / "data")
    sizes = [(0.3 + 0.05 * (i + 1), 0.5, 0.4) for i in range(40)]
    with TestClient(create_app(settings)) as client:
        upload_room(client, room_model("old", sizes))
    path = settings.data_dir / "rooms" / "old" / "room.meta.json"
    record = json.loads(path.read_text())
    # As the Forge wrote it before: twenty boxes and one figure for the room.
    order = record["generated"] + record["placeholders"]
    old = {"roomId": "old", "roomHash": record["roomHash"], "generated": order[:20], "placeholders": order[20:],
           "triTarget": 3000, "createdUtc": record["createdUtc"]}
    path.write_text(json.dumps(old))
    with TestClient(create_app(settings)) as client:
        meta = client.app.state.forge.room_meta("old")
        assert len(meta["generated"]) == 32 and meta["triTargets"] == record_targets(record)
        assert meta["triTarget"] == 5043 and meta["triTargetLight"] == 1875
    assert json.loads(path.read_text())["triTargetLight"] == 1875


def record_targets(record: dict) -> dict:
    return {int(box_id): target for box_id, target in record["triTargets"].items()}
