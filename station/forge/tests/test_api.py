import json
import random
import time

import pytest
from fastapi.testclient import TestClient

from conftest import FIXTURE_ROOMS, jpeg, make_settings, meta_for, post_object, room_model, wait_for
from forge.app import create_app
from forge.budget import triangle_target
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
    target = triangle_target(len(objects)) if objects else 0
    assert created["triTarget"] == target

    jobs = {}
    for box_id in created["generated"]:
        response = post_object(client, model["id"], meta_for(objects[box_id], target), [jpeg(seed=box_id)])
        assert response.status_code == 202, response.text
        jobs[box_id] = response.json()["jobId"]

    total = 0
    for box_id, job_id in jobs.items():
        status = wait_for(client, job_id)
        assert status["state"] == "ready", status
        assert status["source"] == "local" and status["boxId"] == box_id and status["roomId"] == model["id"]
        asset = client.get(status["url"])
        assert asset.status_code == 200 and asset.headers["etag"] == f'"{status["sha256"]}"'
        assert len(asset.content) == status["bytes"] <= 1024 * 1024
        verdict = validate_glb(asset.content, tri_target=target, box=objects[box_id]["size"])
        assert verdict.ok, (box_id, verdict.violations)
        assert verdict.triangles == status["tris"] and status["maxEdge"] <= 0.25
        assert status["report"]["maxScaleError"] <= 0.10
        total += verdict.triangles
    assert total <= 60_000
    listed = client.get("/jobs", params={"room": model["id"]}).json()
    assert [j["jobId"] for j in listed] == list(jobs.values())
    assert client.get("/health").json()["queue"] == 0


def test_queue_is_largest_first_and_capped_at_twenty(tmp_path):
    generator = Recording()
    app = create_app(make_settings(tmp_path / "data"), generator)
    sizes = [(0.3 + 0.05 * i, 0.5, 0.4) for i in range(25)]
    model = room_model("crowded", sizes)
    with TestClient(app) as client:
        app.state.forge.pause()
        created = upload_room(client, model)
        assert len(created["generated"]) == 20 and created["triTarget"] == 3000
        assert created["placeholders"] == [5, 4, 3, 2, 1]

        order = list(model["objects"])
        random.Random(7).shuffle(order)
        answers = {}
        for obj in order:
            # The phone may ask for more than the room allows; the Forge holds it to the room's budget.
            response = post_object(client, "crowded", meta_for(obj, 5000), [jpeg(seed=obj["id"])])
            answers[obj["id"]] = response
        for box_id in created["placeholders"]:
            assert answers[box_id].status_code == 200 and answers[box_id].json()["placeholder"] is True
        for box_id in created["generated"]:
            assert answers[box_id].status_code == 202

        queue = client.get("/queue").json()
        assert [e["boxId"] for e in queue["queued"]] == created["generated"] == list(range(25, 5, -1))
        assert sorted(e["boxId"] for e in queue["placeholders"]) == [1, 2, 3, 4, 5]
        assert queue["active"] is None and client.get("/health").json()["queue"] == 20
        first = client.get(f"/jobs/{answers[25].json()['jobId']}").json()
        assert first["state"] == "queued" and first["queuePosition"] == 0

        app.state.forge.pause(False)
        statuses = [wait_for(client, answers[b].json()["jobId"]) for b in created["generated"]]
        assert all(s["state"] == "ready" for s in statuses)
        assert all(s["triTarget"] == 3000 and s["tris"] <= 3000 for s in statuses)
        assert sum(s["tris"] for s in statuses) <= 60_000
        # One worker, and it took the boxes in order of volume.
        assert generator.calls == [tuple(sizes[b - 1]) for b in created["generated"]]
        for box_id in created["placeholders"]:
            status = client.get(f"/jobs/{answers[box_id].json()['jobId']}").json()
            assert status["state"] == "placeholder" and "sha256" not in status
        assert len(generator.calls) == 20


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
    assert post_object(client, "errors", {**meta_for(obj, 5000), "triTarget": 9000}, [jpeg()]).status_code == 422
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
