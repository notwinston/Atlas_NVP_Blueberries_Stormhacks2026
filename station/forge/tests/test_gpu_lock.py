"""The generation time-out counts the generation, not the wait for the graphics card."""
import json
import time

import pytest
from fastapi.testclient import TestClient

from conftest import jpeg, make_settings, meta_for, post_object, room_model, wait_for
from forge.app import create_app
from forge.generators import ProceduralGenerator
from forge.service import StageTimeout


class Slow(ProceduralGenerator):
    def __init__(self, delay=0.0):
        self.delay = delay

    def generate(self, request):
        time.sleep(self.delay)
        return super().generate(request)


def one_job(tmp_path, generator, **overrides):
    app = create_app(make_settings(tmp_path / "data", **overrides), generator)
    model = room_model("lock", [(1.2, 0.75, 0.7)], ["table"])
    return app, model


def test_a_job_that_waits_for_the_card_is_not_timed_out_by_the_wait(tmp_path):
    app, model = one_job(tmp_path, Slow(), generate_timeout_s=1.0)
    forge = app.state.forge
    with TestClient(app) as client:
        client.post("/rooms", content=json.dumps(model))
        forge._gpu_lock.acquire()  # another job is on the card
        try:
            posted = post_object(client, "lock", meta_for(model["objects"][0], 5000), [jpeg(seed=1)])
            assert posted.status_code == 202
            time.sleep(1.6)  # longer than the generation time-out
        finally:
            forge._gpu_lock.release()
        assert wait_for(client, posted.json()["jobId"])["state"] == "ready"


def test_a_generation_that_is_too_slow_still_times_out(tmp_path):
    app, model = one_job(tmp_path, Slow(delay=1.5), generate_timeout_s=0.3)
    with TestClient(app) as client:
        client.post("/rooms", content=json.dumps(model))
        posted = post_object(client, "lock", meta_for(model["objects"][0], 5000), [jpeg(seed=1)])
        status = wait_for(client, posted.json()["jobId"])
        assert status["state"] == "failed" and "timed out after 0.3 s" in status["error"]


def test_the_wait_for_the_card_has_a_cap_of_twice_the_time_out(tmp_path):
    app, model = one_job(tmp_path, Slow(), generate_timeout_s=0.2)
    forge = app.state.forge
    with TestClient(app):
        view = object()
        job = type("J", (), {"category": "table", "dims": (1.0, 1.0, 1.0)})()
        forge._gpu_lock.acquire()
        try:
            started = time.monotonic()
            with pytest.raises(StageTimeout, match="timed out after 0.4 s of waiting for the graphics card"):
                forge._generate(forge.generator, view, 0, job, 0.2)
            assert 0.35 < time.monotonic() - started < 2.0
        finally:
            forge._gpu_lock.release()
