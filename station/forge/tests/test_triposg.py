"""The TripoSG adapter, driven by a fake pipeline: no test here imports PyTorch, and none runs TripoSG."""
import importlib.util
import sys
import threading
import time
from contextlib import contextmanager
from pathlib import Path
from types import SimpleNamespace

import numpy as np
import pytest
from fastapi.testclient import TestClient
from PIL import Image

from conftest import jpeg, make_settings, meta_for, post_object, room_model, wait_for
from forge import imaging
from forge.app import create_app
from forge.generators import GenerationRequest, GeneratorError, GeneratorUnavailable, TripoSGGenerator
from forge.generators.procedural import build_furniture

NONE_TYPE = "'NoneType' object has no attribute 'astype'"


# --------------------------------------------------------------------------- fakes


class FakeTorch:
    """The few names of `torch` the adapter touches."""

    float16, float32 = "float16", "float32"

    def __init__(self, cuda=True, total_gb=8.0):
        self.emptied = 0
        self.total_gb = total_gb
        self.cuda = SimpleNamespace(
            is_available=lambda: cuda,
            get_device_name=lambda index: "Fake RTX 5070",
            empty_cache=self._empty,
            get_device_properties=lambda index: SimpleNamespace(total_memory=int(self.total_gb * 2**30),
                                                                major=8, minor=9),
        )

    def _empty(self):
        self.emptied += 1

    @contextmanager
    def no_grad(self):
        yield

    def Generator(self, device=None):  # noqa: N802  (torch's own spelling)
        made = SimpleNamespace(device=device, seed=None)
        made.manual_seed = lambda seed: (setattr(made, "seed", seed), made)[1]
        return made


def unit_mesh():
    """What an image-to-3D model returns: a mesh of no particular scale about the origin."""
    v, f = build_furniture("storage", (0.6, 0.6, 0.6))
    lo, hi = v.min(axis=0), v.max(axis=0)
    return ((v - (lo + hi) * 0.5) / (hi - lo).max()).astype(np.float32), f


class FakePipe:
    """Stands in for `TripoSGPipeline`. ``flash`` and ``hierarchical`` say how each decoder behaves:
    "ok", "empty" (the pipeline hands back no mesh), "none" (upstream's crash on its own empty
    result) or "boom" (any other failure)."""

    device = "cuda"

    def __init__(self, flash="ok", hierarchical="ok"):
        self.behaviour = {"flash": flash, "hierarchical": hierarchical}
        self.calls = []
        self.moved = None

    def to(self, device, dtype):
        self.moved = (device, dtype)
        return self

    def __call__(self, **kwargs):
        self.calls.append(kwargs)
        decoder = "flash" if kwargs.get("use_flash_decoder", True) else "hierarchical"
        outcome = self.behaviour[decoder]
        if outcome == "none":
            raise AttributeError(NONE_TYPE)
        if outcome == "boom":
            raise RuntimeError("CUDA error: invalid argument")
        if outcome == "empty":
            return SimpleNamespace(samples=[(None, None)])
        if outcome == "no faces":
            return SimpleNamespace(samples=[(np.zeros((4, 3), np.float32), np.zeros((0, 3), np.int64))])
        return SimpleNamespace(samples=[unit_mesh()])

    @property
    def decoders(self):
        return ["flash" if c.get("use_flash_decoder", True) else "hierarchical" for c in self.calls]


# A weights folder as huggingface_hub 0.30.2 leaves one. It fetches eight files at a time, each into
# `.cache/huggingface/download/<name>.<etag>.incomplete`, and moves a file into place when it is whole: the
# small marker within seconds, the weights minutes later (seen with a real, interrupted download of a small
# repository). So a download that stopped leaves the marker and no weights.
MARKER = Path("model_index.json")
WEIGHTS = Path("transformer") / "diffusion_pytorch_model.safetensors"
PART = Path(".cache") / "huggingface" / "download" / "transformer" / "xGOKKLRSlIhH692h=.53aa5117.incomplete"


def lay_weights(folder: Path, state: str) -> None:
    """``complete``; ``partial`` (the marker, and the weights half-way); ``marker`` (the marker, and the
    weights not begun); ``absent``."""
    if state == "absent":
        return
    folder.mkdir(parents=True, exist_ok=True)
    (folder / MARKER).write_text("{}")
    for path, there in ((WEIGHTS, state == "complete"), (PART, state == "partial")):
        (folder / path).unlink(missing_ok=True)
        if there:
            (folder / path).parent.mkdir(parents=True, exist_ok=True)
            (folder / path).write_bytes(b"weights")


class FakeRuntime:
    """PyTorch, diso, the TripoSG checkout and the hub, all made of nothing.

    ``download``: "complete" fetches what is missing; "fails" cannot reach the hub; "stops" breaks off
    with the weights half-way; "offline" hands the folder back as it is, as the library does without a
    network. ``load_errors``: what the next loads fail with, for a reason that is not the weights.
    """

    def __init__(self, pipe=None, modules=("torch", "diso"), cuda=True, diso_error=None, load_errors=(),
                 download="complete", gate=None):
        self.pipe = pipe or FakePipe()
        self.modules = set(modules)
        self.fake_torch = FakeTorch(cuda)
        self.diso_error, self.load_errors = diso_error, list(load_errors)
        self.download_result, self.gate = download, gate
        self.downloads, self.loads, self.repos = [], [], []

    def installed(self, module):
        return module in self.modules

    def torch(self):
        return self.fake_torch

    def check_diso(self):
        if self.diso_error:
            raise ImportError(self.diso_error)

    def pipeline_class(self, repo):
        self.repos.append(repo)
        return self

    def from_pretrained(self, weights):
        self.loads.append(weights)
        if self.gate is not None:
            assert self.gate.wait(20), "the test never let the load finish"
        if self.load_errors:
            raise OSError(self.load_errors.pop(0))
        if not (Path(weights) / WEIGHTS).is_file():
            # What diffusers says of a folder that has the marker and not the weights.
            raise OSError(f"Error no file named {WEIGHTS.name} found in directory {Path(weights) / WEIGHTS.parent}.")
        return self.pipe

    def download(self, weights):
        self.downloads.append(weights)
        folder = Path(weights)
        if self.download_result == "fails":
            raise ConnectionError("no route to huggingface.co")
        if self.download_result == "offline":
            if not folder.is_dir() or not any(folder.iterdir()):
                raise FileNotFoundError("Cannot find an appropriate cached snapshot folder")
            return  # "Returning existing local_dir ... as remote repo cannot be accessed"
        if self.download_result == "stops":
            lay_weights(folder, "partial")
            raise ConnectionError("Connection broken: IncompleteRead")
        lay_weights(folder, "complete")


def station(tmp_path, weights="complete", **overrides):
    """Settings for a Station with a TripoSG checkout; ``weights`` is a state of `lay_weights`."""
    repo = tmp_path / "TripoSG"
    (repo / "triposg").mkdir(parents=True)
    lay_weights(repo / "pretrained_weights" / "TripoSG", weights)
    return make_settings(tmp_path / "data", generator="triposg", triposg_repo=str(repo), **overrides)


@pytest.fixture
def no_matting(monkeypatch):
    """No matting model is installed, whatever this machine has."""
    monkeypatch.setattr(imaging, "matting_method", lambda: "none")


def picture(size=64, colour=(200, 60, 40)) -> Image.Image:
    return Image.new("RGB", (size, size), colour)


def request(image=None, seed=42):
    return GenerationRequest(image=image or picture(), category="chair", dims=(0.5, 0.9, 0.5), seed=seed)


def wait_until(condition, timeout=10.0):
    deadline = time.monotonic() + timeout
    while not condition():
        assert time.monotonic() < deadline, "timed out waiting"
        time.sleep(0.01)


# --------------------------------------------------------------------------- availability


def test_on_a_machine_with_nothing_installed_everything_missing_is_named(tmp_path, no_matting):
    generator = TripoSGGenerator(make_settings(tmp_path / "data", generator="triposg"), FakeRuntime(modules=()))
    ready, note = generator.availability()
    assert ready is False and generator.gpu() is None
    for word in ("PyTorch is not installed", "FORGE_TRIPOSG_REPO", "diso is not installed", "no matting model"):
        assert word in note
    with pytest.raises(GeneratorUnavailable, match="TripoSG cannot run: PyTorch is not installed"):
        generator.warm_up()
    with pytest.raises(GeneratorUnavailable, match="TripoSG cannot run"):
        generator.generate(request())


def test_each_missing_piece_is_reported(tmp_path, matting):
    settings = station(tmp_path)
    no_torch = TripoSGGenerator(settings, FakeRuntime(modules=("diso",)))
    assert no_torch.availability() == (False, "PyTorch is not installed")
    ready, note = TripoSGGenerator(settings, FakeRuntime(modules=("torch",))).availability()
    assert ready is False and note.startswith("diso is not installed")
    nowhere = make_settings(tmp_path / "data", generator="triposg", triposg_repo=str(tmp_path / "nowhere"))
    assert "FORGE_TRIPOSG_REPO" in TripoSGGenerator(nowhere, FakeRuntime()).availability()[1]
    # Everything installed: not usable until the model is loaded.
    assert TripoSGGenerator(settings, FakeRuntime()).availability() == (False, "the model is not loaded yet")


def test_the_real_runtime_finds_what_is_installed_without_importing_it(tmp_path):
    from forge.generators.triposg import _Runtime

    runtime = _Runtime()
    assert runtime.installed("numpy") is True and runtime.installed("no_such_module_anywhere") is False
    # The adapter of a service that was only created has imported nothing.
    before = set(sys.modules)
    TripoSGGenerator(make_settings(tmp_path / "data", generator="triposg")).availability()
    assert not {"torch", "diso", "triposg"} & (set(sys.modules) - before)


def test_matting_is_required(tmp_path, no_matting):
    # No matting model on this machine: TripoSG says so and never loads.
    runtime = FakeRuntime()
    generator = TripoSGGenerator(station(tmp_path), runtime)
    assert generator.needs_matting is True
    ready, note = generator.availability()
    assert ready is False and note == imaging.MATTING_MISSING and "rembg" in note
    with pytest.raises(GeneratorUnavailable, match="TripoSG cannot run: no matting model is installed"):
        generator.generate(request())
    assert runtime.loads == [] and runtime.pipe.calls == []


def test_no_cuda_is_found_at_the_load(tmp_path, matting):
    generator = TripoSGGenerator(station(tmp_path), FakeRuntime(cuda=False))
    with pytest.raises(GeneratorUnavailable, match="CUDA is not available to PyTorch"):
        generator.warm_up()
    assert generator.availability() == (False, "CUDA is not available to PyTorch") and generator.gpu() is None


def test_diso_that_is_installed_but_does_not_load(tmp_path, matting):
    runtime = FakeRuntime(diso_error="DLL load failed while importing _C")
    generator = TripoSGGenerator(station(tmp_path), runtime)
    assert generator.availability() == (False, "the model is not loaded yet")  # finding it is not enough
    with pytest.raises(GeneratorUnavailable, match="diso cannot be imported: DLL load failed"):
        generator.warm_up()
    assert generator.availability() == (False, "diso cannot be imported: DLL load failed while importing _C")
    assert generator.gpu() == "Fake RTX 5070"  # the card is there, the generator is not
    assert runtime.loads == []


def test_the_card_sizes_the_generation_cap_unless_it_was_set(tmp_path, matting):
    settings = station(tmp_path)
    assert settings.max_generated == 32 and settings.max_generated_from_env is False
    TripoSGGenerator(settings, FakeRuntime()).warm_up()  # the fake card has 8 GB
    assert settings.max_generated == 16

    kept = station(tmp_path / "kept")
    kept.max_generated, kept.max_generated_from_env = 5, True
    TripoSGGenerator(kept, FakeRuntime()).warm_up()
    assert kept.max_generated == 5


# --------------------------------------------------------------------------- the weights


def test_complete_weights_are_loaded_without_a_download(tmp_path, matting):
    settings = station(tmp_path)
    runtime = FakeRuntime()
    generator = TripoSGGenerator(settings, runtime)
    assert generator.weights_gap() == ""
    generator.warm_up()
    weights = str(Path(settings.triposg_repo) / "pretrained_weights" / "TripoSG")
    assert runtime.downloads == [] and runtime.loads == [weights]
    assert runtime.repos == [str(Path(settings.triposg_repo).resolve())]
    assert runtime.pipe.moved == ("cuda", "float16")
    assert generator.availability() == (True, "") and generator.gpu() == "Fake RTX 5070"
    generator.warm_up()  # loaded once
    assert runtime.loads == [weights]


def weights_folder(settings) -> str:
    return str(Path(settings.triposg_repo) / "pretrained_weights" / "TripoSG")


def test_a_folder_that_shows_its_gap_is_downloaded_before_the_load(tmp_path, matting):
    # No folder at all: the marker is missing.
    settings = station(tmp_path / "absent", weights="absent")
    runtime = FakeRuntime()
    generator = TripoSGGenerator(settings, runtime)
    ready, note = generator.availability()
    assert ready is False and "no model_index.json in" in note and "downloaded first" in note
    generator.warm_up()
    assert runtime.downloads == [weights_folder(settings)] and runtime.loads == [weights_folder(settings)]
    assert generator.availability() == (True, "") and generator.weights_gap() == ""

    # A download that stopped with the weights half-way: the marker is there, and so is the unfinished file.
    settings = station(tmp_path / "partial", weights="partial")
    runtime = FakeRuntime()
    generator = TripoSGGenerator(settings, runtime)
    ready, note = generator.availability()
    assert ready is False and "stopped before it was whole" in note and "downloaded first" in note
    generator.warm_up()
    assert runtime.downloads == [weights_folder(settings)] and runtime.loads == [weights_folder(settings)]
    assert generator.availability() == (True, "")


def test_a_marker_without_the_weights_is_fetched_when_the_load_fails(tmp_path, matting):
    """A download that stopped before a weight file was begun leaves the marker and nothing that shows a gap.

    The load then fails; the adapter fetches what is missing once and loads again, instead of failing every job.
    """
    settings = station(tmp_path, weights="marker")
    runtime = FakeRuntime()
    generator = TripoSGGenerator(settings, runtime)
    assert generator.weights_gap() == "" and generator.availability() == (False, "the model is not loaded yet")
    generator.warm_up()
    folder = weights_folder(settings)
    assert runtime.loads == [folder, folder] and runtime.downloads == [folder]
    assert generator.availability() == (True, "")
    assert generator.generate(request()).decoder == "flash" and runtime.downloads == [folder]


def test_a_leftover_of_another_download_does_not_keep_the_model_from_loading(tmp_path, matting):
    # The weights are whole, and beside them lies an unfinished file of another revision, which no download
    # will ever finish. It costs a look at the hub at every start, and nothing else.
    settings = station(tmp_path)
    stale = Path(weights_folder(settings)) / PART.parent / "old=.0000aaaa.incomplete"
    stale.parent.mkdir(parents=True, exist_ok=True)
    stale.write_bytes(b"")
    runtime = FakeRuntime()
    generator = TripoSGGenerator(settings, runtime)
    assert "stopped before it was whole" in generator.weights_gap()
    generator.warm_up()
    assert runtime.downloads == [weights_folder(settings)] and runtime.loads == [weights_folder(settings)]
    assert generator.availability() == (True, "") and stale.is_file()


def test_weights_setting_and_full_precision(tmp_path, matting):
    elsewhere = tmp_path / "weights"
    settings = station(tmp_path, triposg_weights=str(elsewhere), triposg_half_precision=False)
    runtime = FakeRuntime()
    TripoSGGenerator(settings, runtime).warm_up()
    assert runtime.downloads == [str(elsewhere)] and runtime.loads == [str(elsewhere)]
    assert runtime.pipe.moved == ("cuda", "float32")


def test_a_download_that_fails_stops_or_has_no_network(tmp_path, matting):
    # The hub cannot be reached and the folder shows its gap: no load is tried.
    runtime = FakeRuntime(download="fails")
    generator = TripoSGGenerator(station(tmp_path / "a", weights="partial"), runtime)
    with pytest.raises(GeneratorUnavailable, match="the weights could not be downloaded"):
        generator.warm_up()
    assert "no route to huggingface.co" in generator.availability()[1] and runtime.loads == []

    # The download breaks off again: the same.
    runtime = FakeRuntime(download="stops")
    generator = TripoSGGenerator(station(tmp_path / "b", weights="absent"), runtime)
    with pytest.raises(GeneratorUnavailable, match="the weights could not be downloaded.*Connection broken"):
        generator.warm_up()
    assert runtime.loads == [] and "stopped before it was whole" in generator.weights_gap()

    # No network: the library hands the folder back as it is. The load is tried, once, and says what is missing.
    settings = station(tmp_path / "c", weights="partial")
    runtime = FakeRuntime(download="offline")
    generator = TripoSGGenerator(settings, runtime)
    with pytest.raises(GeneratorUnavailable, match="could not be loaded from .*no file named diffusion_pytorch_model"
                                                   ".*stopped before it was whole"):
        generator.warm_up()
    assert runtime.loads == [weights_folder(settings)] and runtime.downloads == [weights_folder(settings)]
    # No network and a folder with no marker at all: nothing to load from.
    settings = station(tmp_path / "c2", weights="absent")
    (Path(weights_folder(settings)) / "vae").mkdir(parents=True)
    (Path(weights_folder(settings)) / "vae" / "config.json").write_text("{}")
    runtime = FakeRuntime(download="offline")
    with pytest.raises(GeneratorUnavailable, match="the download left no model_index.json"):
        TripoSGGenerator(settings, runtime).warm_up()
    assert runtime.loads == []

    # No network and a marker without the weights: both loads fail, and the reason is the load's own.
    settings = station(tmp_path / "d", weights="marker")
    runtime = FakeRuntime(download="offline")
    generator = TripoSGGenerator(settings, runtime)
    with pytest.raises(GeneratorUnavailable, match="could not be loaded from .*no file named diffusion_pytorch_model"):
        generator.warm_up()
    assert len(runtime.loads) == 2 and len(runtime.downloads) == 1
    # The network comes back: the next job's load fetches the rest by itself.
    runtime.download_result = "complete"
    assert generator.generate(request()).decoder == "flash" and generator.availability() == (True, "")
    assert len(runtime.downloads) == 2

    # The load fails and the hub cannot be reached: both reasons are given.
    runtime = FakeRuntime(download="fails")
    generator = TripoSGGenerator(station(tmp_path / "e", weights="marker"), runtime)
    with pytest.raises(GeneratorUnavailable) as raised:
        generator.warm_up()
    assert "no file named diffusion_pytorch_model" in str(raised.value)
    assert "the weights could not be downloaded" in str(raised.value)


def test_a_failed_load_is_reported_and_tried_again_by_the_next_job(tmp_path, matting):
    # A reason that is not the weights: the folder is whole, and fetching changes nothing.
    runtime = FakeRuntime(load_errors=["CUDA out of memory", "CUDA out of memory"])
    settings = station(tmp_path)
    generator = TripoSGGenerator(settings, runtime)
    with pytest.raises(GeneratorUnavailable, match="the model could not be loaded"):
        generator.warm_up()
    ready, note = generator.availability()
    assert ready is False and "CUDA out of memory" in note
    assert len(runtime.loads) == 2 and runtime.downloads == [weights_folder(settings)]
    assert generator.generate(request()).decoder == "flash"
    assert generator.availability() == (True, "") and len(runtime.loads) == 3


def test_availability_answers_while_the_model_loads(tmp_path, matting):
    gate = threading.Event()
    generator = TripoSGGenerator(station(tmp_path), FakeRuntime(gate=gate))
    loader = threading.Thread(target=generator.warm_up, daemon=True)
    loader.start()
    try:
        wait_until(lambda: generator.availability()[1] == "the model is being loaded")
        started = time.monotonic()
        assert generator.availability() == (False, "the model is being loaded")
        assert generator.gpu() == "Fake RTX 5070"
        assert time.monotonic() - started < 0.5  # neither call waits for the load
        assert loader.is_alive()
    finally:
        gate.set()
    loader.join(10)
    assert generator.availability() == (True, "")


# --------------------------------------------------------------------------- the decoders


def loaded(tmp_path, pipe, **overrides):
    runtime = FakeRuntime(pipe=pipe)
    generator = TripoSGGenerator(station(tmp_path, **overrides), runtime)
    generator.warm_up()
    return generator, runtime


def test_the_default_call_is_upstreams_own(tmp_path, matting):
    pipe = FakePipe()
    generator, runtime = loaded(tmp_path, pipe, triposg_steps=30, triposg_guidance=6.5)
    framed = picture(120)
    mesh = generator.generate(request(framed, seed=7))
    assert mesh.decoder == "flash" and mesh.vertices.shape[1] == 3 and mesh.faces.dtype == np.int64
    assert mesh.vertices.dtype == np.float64 and len(mesh.faces) > 0
    (call,) = pipe.calls
    assert set(call) == {"image", "generator", "num_inference_steps", "guidance_scale"}  # no decoder is named
    assert call["image"] is framed  # the framed picture goes in as it is
    assert call["num_inference_steps"] == 30 and call["guidance_scale"] == 6.5
    assert call["generator"].seed == 7 and call["generator"].device == "cuda"
    assert runtime.fake_torch.emptied == 1


def test_an_inside_out_generator_has_its_faces_turned_round(tmp_path, matting):
    expected = unit_mesh()[1]
    as_made = loaded(tmp_path / "a", FakePipe())[0].generate(request())
    assert np.array_equal(as_made.faces, expected)
    turned = loaded(tmp_path / "b", FakePipe(), triposg_inside_out=True)[0].generate(request())
    assert np.array_equal(turned.faces, expected[:, ::-1]) and np.array_equal(turned.vertices, as_made.vertices)
    assert turned.faces.flags["C_CONTIGUOUS"] and turned.faces.dtype == np.int64


def test_a_matted_crop_is_put_on_white(tmp_path, matting):
    pipe = FakePipe()
    generator, _ = loaded(tmp_path, pipe)
    rgba = Image.new("RGBA", (40, 40), (10, 20, 30, 0))
    rgba.paste((200, 60, 40, 255), (10, 10, 30, 30))
    generator.generate(request(rgba))
    sent = pipe.calls[0]["image"]
    assert sent.mode == "RGB" and sent.getpixel((0, 0)) == (255, 255, 255) and sent.getpixel((20, 20)) == (200, 60, 40)


@pytest.mark.parametrize("failure", ["none", "boom", "empty", "no faces"])
def test_a_failed_default_decoder_is_tried_once_more_without_it(tmp_path, matting, failure):
    pipe = FakePipe(flash=failure)
    generator, runtime = loaded(tmp_path, pipe)
    mesh = generator.generate(request())
    assert mesh.decoder == "hierarchical" and len(mesh.faces) > 0
    assert pipe.decoders == ["flash", "hierarchical"]
    assert pipe.calls[1]["use_flash_decoder"] is False and "use_flash_decoder" not in pipe.calls[0]
    assert runtime.fake_torch.emptied == 1


def test_the_fallback_can_be_switched_off(tmp_path, matting):
    pipe = FakePipe(flash="none")
    generator, runtime = loaded(tmp_path, pipe, triposg_fallback_decoder=False)
    with pytest.raises(GeneratorError) as raised:
        generator.generate(request())
    assert "TripoSG inference failed: flash decoder: " + NONE_TYPE == str(raised.value)
    assert pipe.decoders == ["flash"] and runtime.fake_torch.emptied == 1

    # A pipeline that hands back nothing, instead of crashing on it, is named for what it is.
    for k, empty in enumerate(("empty", "no faces")):
        generator, _ = loaded(tmp_path / str(k), FakePipe(flash=empty), triposg_fallback_decoder=False)
        with pytest.raises(GeneratorError) as raised:
            generator.generate(request())
        assert str(raised.value) == "TripoSG inference failed: flash decoder: it returned no mesh"


def test_both_decoders_failing_fails_the_generation_with_both_reasons(tmp_path, matting):
    pipe = FakePipe(flash="none", hierarchical="boom")
    generator, _ = loaded(tmp_path, pipe)
    with pytest.raises(GeneratorError) as raised:
        generator.generate(request())
    assert not isinstance(raised.value, GeneratorUnavailable)
    assert str(raised.value) == (f"TripoSG inference failed: flash decoder: {NONE_TYPE}; "
                                 "hierarchical decoder: CUDA error: invalid argument")
    assert pipe.decoders == ["flash", "hierarchical"]


def test_a_decoder_that_keeps_failing_is_no_longer_tried_first(tmp_path, matting):
    pipe = FakePipe(flash="none")
    generator, _ = loaded(tmp_path, pipe)
    assert [generator.generate(request()).decoder for _ in range(3)] == ["hierarchical"] * 3
    # Two objects paid for both decoders; the third went to the working one at once.
    assert pipe.decoders == ["flash", "hierarchical", "flash", "hierarchical", "hierarchical"]

    # One failure followed by a success does not switch.
    pipe = FakePipe(flash="none")
    generator, _ = loaded(tmp_path / "second", pipe)
    generator.generate(request())
    pipe.behaviour["flash"] = "ok"
    assert generator.generate(request()).decoder == "flash"
    pipe.behaviour["flash"] = "none"
    generator.generate(request())
    assert pipe.decoders == ["flash", "hierarchical", "flash", "flash", "hierarchical"]


# --------------------------------------------------------------------------- inside the service


def served(tmp_path, runtime, **overrides):
    settings = station(tmp_path, **overrides)
    return create_app(settings, TripoSGGenerator(settings, runtime)), settings


def one_job(client, room="triposg", views=None):
    model = room_model(room, [(0.6, 0.6, 0.6)])
    assert client.post("/rooms", json=model).status_code == 201
    return post_object(client, room, meta_for(model["objects"][0], 15000), views or [jpeg()]).json()["jobId"]


def test_the_model_is_loaded_when_the_service_starts_and_a_job_gets_a_framed_picture(tmp_path, matting):
    runtime = FakeRuntime()
    app, _ = served(tmp_path, runtime)
    with TestClient(app) as client:
        wait_until(lambda: client.get("/health").json()["generatorReady"])  # no job was needed
        health = client.get("/health").json()
        assert health["generator"] == "triposg" and health["generatorNote"] == "" and health["matting"] == "rembg"
        assert health["gpu"] is True and health["gpuName"] == "Fake RTX 5070"
        assert len(runtime.loads) == 1 and runtime.pipe.calls == []

        status = wait_for(client, one_job(client))
        assert status["state"] == "ready", status
        assert status["generator"] == "triposg" and status["decoder"] == "flash" and status["source"] == "local"
        assert len(runtime.loads) == 1

        # What the model saw: the object of the crop (a block of 172 pixels in a 256-pixel crop) cut to
        # its matte, on white, in a square with a tenth of margin; nothing of the grey room is left.
        (call,) = runtime.pipe.calls
        sent = call["image"]
        assert sent.mode == "RGB" and sent.size[0] == sent.size[1]
        pixels = np.asarray(sent).astype(np.int16)
        block = np.abs(pixels - [150, 112, 70]).max(axis=2) < 40
        rows, columns = np.flatnonzero(block.any(axis=1)), np.flatnonzero(block.any(axis=0))
        long_side = max(len(rows), len(columns))
        assert 165 <= long_side <= 180 and abs(sent.size[0] - (long_side + 2 * int(long_side * 0.1))) <= 4
        assert abs(rows[0] - (sent.size[1] - 1 - rows[-1])) <= 2  # centred, top to bottom
        assert abs(columns[0] - (sent.size[0] - 1 - columns[-1])) <= 2  # and left to right
        assert (pixels[0] == 255).all() and (pixels[-1] == 255).all() and (pixels[:, 0] == 255).all()
        assert not (np.abs(pixels - 96).max(axis=2) < 12).any()  # no grey background anywhere

        # The crop and its matte were held for the job and are dropped with it.
        assert app.state.forge._views == {} and app.state.forge._crops == {}


def test_health_answers_and_jobs_wait_while_the_model_loads(tmp_path, matting):
    gate = threading.Event()
    runtime = FakeRuntime(gate=gate)
    app, _ = served(tmp_path, runtime)
    with TestClient(app) as client:
        try:
            wait_until(lambda: client.get("/health").json()["generatorNote"] == "the model is being loaded")
            started = time.monotonic()
            health = client.get("/health").json()
            assert time.monotonic() - started < 1.0 and health["ok"] is True and health["generatorReady"] is False
            job_id = one_job(client)
            time.sleep(0.2)
            assert client.get(f"/jobs/{job_id}").json()["state"] == "queued" and runtime.pipe.calls == []
        finally:
            gate.set()
        assert wait_for(client, job_id)["state"] == "ready"
        assert client.get("/health").json()["generatorReady"] is True


def test_without_a_matting_model_the_service_says_so_and_no_photo_reaches_the_model(tmp_path, no_matting):
    runtime = FakeRuntime()  # PyTorch, CUDA, diso, the checkout and the weights are all there
    app, _ = served(tmp_path, runtime)
    with TestClient(app) as client:
        health = client.get("/health").json()
        assert health["generatorReady"] is False and health["matting"] == "none"
        assert health["generatorNote"] == "no matting model is installed (rembg; pip install -r requirements-gpu.txt)"
        status = wait_for(client, one_job(client, views=[jpeg(seed=1), jpeg(seed=2)]))
        assert status["state"] == "failed"
        assert "TripoSG cannot run: no matting model is installed" in status["error"]
        assert runtime.loads == [] and runtime.pipe.calls == []
        assert client.get("/health").json()["queue"] == 0  # the worker goes on


def test_a_view_whose_matting_fails_is_not_sent_and_the_next_view_is(tmp_path, matting):
    runtime = FakeRuntime()
    app, _ = served(tmp_path, runtime)
    with TestClient(app) as client:
        wait_until(lambda: client.get("/health").json()["generatorReady"])
        matting.fail_on = {1}
        status = wait_for(client, one_job(client, "second", [jpeg(seed=1), jpeg((40, 54, 92), seed=2)]))
        assert status["state"] == "ready", status
        (call,) = runtime.pipe.calls  # view 0 never reached the model
        centre = call["image"].getpixel((call["image"].size[0] // 2,) * 2)
        assert max(abs(a - b) for a, b in zip(centre, (40, 54, 92))) < 30  # the picture is view 1's

        matting.fail_on = {matting.calls + 1}
        status = wait_for(client, one_job(client, "only"))
        assert status["state"] == "failed" and len(runtime.pipe.calls) == 1
        assert status["error"] == ("view 0: background removal failed: onnxruntime ran out of memory; "
                                   "triposg is given matted pictures only")


def test_views_that_cannot_be_matted_are_passed_over_and_are_no_failed_generations(tmp_path, matting):
    runtime = FakeRuntime()
    app, _ = served(tmp_path, runtime)
    with TestClient(app) as client:
        wait_until(lambda: client.get("/health").json()["generatorReady"])
        matting.fail_on = {1, 2}  # the first two photos
        model = room_model("third", [(0.6, 0.6, 0.6)])
        client.post("/rooms", json=model)
        meta = meta_for(model["objects"][0], 15000)
        meta["views"] = meta["views"] * 3
        photos = [jpeg(seed=1), jpeg(seed=2), jpeg((40, 54, 92), seed=3)]
        status = wait_for(client, post_object(client, "third", meta, photos).json()["jobId"])
        # Two views gave no picture; the third was generated, and it is the only generation.
        assert status["state"] == "ready" and status["attempts"] == 1 and len(runtime.pipe.calls) == 1
        assert [e["view"] for e in app.state.forge.job(status["jobId"]).attemptLog] == [2]


def test_a_matte_of_specks_is_a_view_that_cannot_be_used_not_a_failed_generation(tmp_path, matting):
    runtime = FakeRuntime()
    app, _ = served(tmp_path, runtime)
    with TestClient(app) as client:
        wait_until(lambda: client.get("/health").json()["generatorReady"])
        model = room_model("dots", [(0.6, 0.6, 0.6)])
        client.post("/rooms", json=model)
        meta = meta_for(model["objects"][0], 15000)
        meta["views"] = meta["views"] * 3
        photos = [jpeg(seed=1), jpeg(seed=2), jpeg(seed=3)]

        # The first two photos give a matte of dots: they are passed over, and the third is generated.
        matting.specks_on = {matting.calls + 1, matting.calls + 2}
        status = wait_for(client, post_object(client, "dots", meta, photos).json()["jobId"])
        assert status["state"] == "ready" and status["attempts"] == 1 and len(runtime.pipe.calls) == 1
        assert [e["view"] for e in app.state.forge.job(status["jobId"]).attemptLog] == [2]

        # All three: no generation was made, and the job says why in the words of the matte.
        matting.specks_on = {matting.calls + k for k in (1, 2, 3)}
        photos = [jpeg(seed=4), jpeg(seed=5), jpeg(seed=6)]
        status = wait_for(client, post_object(client, "dots", meta, photos).json()["jobId"])
        job = app.state.forge.job(status["jobId"])
        assert status["state"] == "failed" and status["error"].count("the matte holds only specks") == 3
        assert "index 0 is out of bounds" not in status["error"]
        assert job.attempts == 0 and job.attemptLog == [] and len(runtime.pipe.calls) == 1


def test_an_empty_matte_is_not_sent(tmp_path, matting):
    runtime = FakeRuntime()
    app, _ = served(tmp_path, runtime)
    with TestClient(app) as client:
        matting.empty = True
        status = wait_for(client, one_job(client))
        assert status["state"] == "failed" and "view 0: the matte is empty" in status["error"]
        assert runtime.pipe.calls == []


def test_a_matting_model_that_cannot_be_loaded_is_reported(tmp_path, matting):
    matting.load_error = "u2net.onnx could not be downloaded"
    app, _ = served(tmp_path, FakeRuntime())
    with TestClient(app) as client:
        wait_until(lambda: "matting model could not be loaded" in client.get("/health").json()["generatorNote"])
        health = client.get("/health").json()
        assert health["generatorReady"] is False and "u2net.onnx" in health["generatorNote"]
        # A picture that mattes after all clears it.
        assert wait_for(client, one_job(client))["state"] == "ready"
        assert client.get("/health").json()["generatorReady"] is True


def test_the_matting_model_is_loaded_at_the_start_for_any_generator(tmp_path, matting):
    loads = []
    real_load = imaging.load_matting
    imaging.load_matting = lambda: loads.append(1) or real_load()  # the fixture restores it
    try:
        with TestClient(create_app(make_settings(tmp_path / "a"))) as client:  # the procedural generator
            wait_until(lambda: loads)
            assert client.get("/health").json()["generatorReady"] is True
        # A matting model that cannot be loaded stops only a generator that needs it.
        matting.load_error = "u2net.onnx could not be downloaded"
        with TestClient(create_app(make_settings(tmp_path / "b"))) as client:
            wait_until(lambda: len(loads) == 2)
            health = client.get("/health").json()
            assert health["generatorReady"] is True and health["generatorNote"] == ""
    finally:
        imaging.load_matting = real_load


def test_the_job_reports_the_decoder_that_made_its_mesh(tmp_path, matting):
    runtime = FakeRuntime(pipe=FakePipe(flash="none"))
    app, settings = served(tmp_path, runtime)
    with TestClient(app) as client:
        photo = jpeg(seed=5)
        status = wait_for(client, one_job(client, views=[photo]))
        assert status["state"] == "ready" and status["decoder"] == "hierarchical"
        assert runtime.pipe.decoders == ["flash", "hierarchical"]
        # The asset remembers it, so an answer from the cache says the same.
        model = room_model("triposg", [(0.6, 0.6, 0.6)])
        again = post_object(client, "triposg", meta_for(model["objects"][0], 15000), [photo]).json()
        assert again["cached"] is True
        assert client.get(f"/jobs/{again['jobId']}").json()["decoder"] == "hierarchical"

    # Both decoders failing fails the job with both reasons, and the worker goes on.
    runtime = FakeRuntime(pipe=FakePipe(flash="none", hierarchical="boom"))
    app, _ = served(tmp_path / "second", runtime)
    with TestClient(app) as client:
        status = wait_for(client, one_job(client))
        assert status["state"] == "failed" and "flash decoder" in status["error"]
        assert "hierarchical decoder: CUDA error" in status["error"] and "decoder" not in status


def test_what_was_measured_of_triposgs_frame_steers_the_fit(tmp_path, matting):
    """`FORGE_TRIPOSG_FRONT` breaks a tie of the fit, and `FORGE_TRIPOSG_UP` names the one up axis that is tried."""
    from forge import profile

    flat = profile.encode(np.full(256, 255, dtype=np.uint8))  # a cupboard's flat top: every yaw fits it equally

    def job(front, up="Y", camera=(2.0, 0.4, 0.1), name="frame"):
        runtime = FakeRuntime()
        app, _ = served(tmp_path / f"{name}_{front}_{up}", runtime, triposg_front=front, triposg_up=up)
        with TestClient(app) as client:
            model = room_model(name, [(0.6, 0.6, 0.6)])
            assert client.post("/rooms", json=model).status_code == 201
            meta = meta_for(model["objects"][0], 15000)
            x, y, z = camera
            meta["views"][0]["camToBox"] = [1, 0, 0, x, 0, 1, 0, y, 0, 0, 1, z, 0, 0, 0, 1]
            meta["profile"] = flat
            return wait_for(client, post_object(client, name, meta, [jpeg()]).json()["jobId"])

    # The camera stood on Unity's +X side of the box.
    unknown, canonical, view = job("unknown"), job("canonical"), job("view")
    for status in (unknown, canonical, view):
        assert status["state"] == "ready" and status["fit"] == 0.0 and status["fitUp"] == "Y"
    assert unknown["fitYaw"] == 0 and canonical["fitYaw"] == 0
    # `view`: the mesh's own +Z side is put where the camera stood, at azimuth 90.
    assert view["fitYaw"] == 90 and view["report"]["yawDegrees"] == 270
    assert job("view", camera=(0.1, 0.4, -2.0), name="behind")["fitYaw"] == 180
    assert job("view", camera=(-2.0, 0.4, 0.1), name="left")["fitYaw"] == 270
    # Only the measured up axis is tried: every candidate of the fit has it.
    lying = job("unknown", up="Z", name="up")
    assert lying["fitUp"] == "Z" and {c["up"] for c in lying["report"]["candidates"]} == {"Z"}
    assert {c["up"] for c in unknown["report"]["candidates"]} == {"Y"}


@pytest.mark.skipif(importlib.util.find_spec("torch") is not None,
                    reason="PyTorch is installed here: the service's own start-up may load it")
def test_no_test_imported_pytorch():
    assert "torch" not in sys.modules and "diso" not in sys.modules and "rembg" not in sys.modules


def test_a_decoder_depth_is_given_to_both_decoders(tmp_path, matting):
    pipe = FakePipe(flash="boom")
    generator, _ = loaded(tmp_path, pipe, triposg_octree_depth=8)
    mesh = generator.generate(request())
    assert mesh.decoder == "hierarchical"
    flash, hierarchical = pipe.calls
    assert flash["flash_octree_depth"] == 8 and "hierarchical_octree_depth" not in flash
    assert hierarchical["hierarchical_octree_depth"] == 8 and "flash_octree_depth" not in hierarchical
