import json

from fastapi.testclient import TestClient

from conftest import jpeg, make_settings, meta_for, post_object, room_model, wait_for
from forge.app import create_app
from forge.validator import validate_glb


def start_and_stop_mid_queue(settings):
    """Finish one job, leave a second one waiting, then stop the service."""
    model = room_model("restart", [(1.0, 0.75, 0.6), (0.5, 0.9, 0.5)], ["table", "chair"])
    app = create_app(settings)
    with TestClient(app) as client:
        assert client.post("/rooms", content=json.dumps(model)).status_code == 201
        done = post_object(client, "restart", meta_for(model["objects"][0], 5000), [jpeg(seed=1)]).json()["jobId"]
        ready = wait_for(client, done)
        app.state.forge.pause()
        waiting = post_object(client, "restart", meta_for(model["objects"][1], 5000), [jpeg(seed=2)]).json()["jobId"]
        assert client.get(f"/jobs/{waiting}").json()["state"] == "queued"
    return model, ready, waiting


def test_restart_keeps_rooms_finished_jobs_and_assets(tmp_path):
    settings = make_settings(tmp_path / "data")
    model, ready, waiting = start_and_stop_mid_queue(settings)

    with TestClient(create_app(settings)) as client:
        assert client.get("/rooms/restart").json() == model
        again = client.get(f"/jobs/{ready['jobId']}").json()
        assert again == ready
        asset = client.get(ready["url"])
        assert asset.status_code == 200 and validate_glb(asset.content, 5000, model["objects"][0]["size"]).ok

        # The crops of the waiting job were only in memory: it is failed, not lost and not stuck.
        lost = client.get(f"/jobs/{waiting}").json()
        assert lost["state"] == "failed" and "restart" in lost["error"]
        assert client.get("/health").json()["queue"] == 0

        # The phone sends the object again and it goes through; the finished photo comes from the cache.
        retry = post_object(client, "restart", meta_for(model["objects"][1], 5000), [jpeg(seed=2)])
        assert retry.status_code == 202 and wait_for(client, retry.json()["jobId"])["state"] == "ready"
        cached = post_object(client, "restart", meta_for(model["objects"][0], 5000), [jpeg(seed=1)])
        assert cached.status_code == 200 and cached.json()["sha256"] == ready["sha256"]
        assert len(client.get("/jobs", params={"room": "restart"}).json()) == 4


def test_restart_resumes_waiting_jobs_when_crops_are_kept(tmp_path):
    settings = make_settings(tmp_path / "data", debug_keep=True)
    _, _, waiting = start_and_stop_mid_queue(settings)
    with TestClient(create_app(settings)) as client:
        assert wait_for(client, waiting)["state"] == "ready"


def test_restart_survives_damaged_records(tmp_path):
    settings = make_settings(tmp_path / "data")
    _, ready, _ = start_and_stop_mid_queue(settings)
    jobs = settings.data_dir / "jobs"
    (jobs / "deadbeefdeadbeef").mkdir()
    (jobs / "deadbeefdeadbeef" / "job.json").write_text("{ half a record")
    (settings.data_dir / "assets" / f"{ready['sha256']}.glb").unlink()
    with TestClient(create_app(settings)) as client:
        status = client.get(f"/jobs/{ready['jobId']}").json()
        assert status["state"] == "failed" and "missing" in status["error"]
        assert client.get("/jobs/deadbeefdeadbeef").status_code == 404
