import pytest

from forge.config import Settings

NEW = ("FORGE_CLEANING_TIMEOUT_S", "FORGE_POOR_FIT", "FORGE_TRIPOSG_UP", "FORGE_TRIPOSG_FRONT",
       "FORGE_TRIPOSG_FALLBACK_DECODER", "FORGE_TRIPOSG_INSIDE_OUT",
       "FORGE_MAX_GENERATED", "FORGE_TRIPOSG_OCTREE_DEPTH")


@pytest.fixture
def env(monkeypatch):
    for name in NEW + ("FORGE_GENERATOR",):
        monkeypatch.delenv(name, raising=False)
    return monkeypatch


def test_defaults(env):
    for settings in (Settings(), Settings.from_env()):
        assert settings.cleaning_timeout_s == 60.0  # was 20: clean-up now casts rays and makes two copies
        assert settings.matting_timeout_s == 10.0 and settings.generate_timeout_s == 90.0
        assert settings.poor_fit == 0.20
        assert settings.triposg_up == "Y" and settings.triposg_front == "unknown"
        assert settings.triposg_fallback_decoder is True and settings.triposg_inside_out is False
        assert settings.max_generated == 32  # the rule of the mesh design (M9)
        assert settings.triposg_octree_depth == 0  # 0: TripoSG's own call, no depth named


def test_each_setting_has_its_variable(env):
    env.setenv("FORGE_CLEANING_TIMEOUT_S", "45")
    env.setenv("FORGE_POOR_FIT", "0.3")
    env.setenv("FORGE_TRIPOSG_UP", "z")  # case does not matter
    env.setenv("FORGE_TRIPOSG_FRONT", "Canonical")
    env.setenv("FORGE_TRIPOSG_FALLBACK_DECODER", "0")
    env.setenv("FORGE_TRIPOSG_INSIDE_OUT", "1")
    settings = Settings.from_env()
    assert settings.triposg_inside_out is True
    assert settings.cleaning_timeout_s == 45.0 and settings.poor_fit == 0.3
    assert settings.triposg_up == "Z" and settings.triposg_front == "canonical"
    assert settings.triposg_fallback_decoder is False
    env.setenv("FORGE_TRIPOSG_UP", "X")
    env.setenv("FORGE_TRIPOSG_FRONT", "view")
    env.setenv("FORGE_TRIPOSG_FALLBACK_DECODER", "on")
    settings = Settings.from_env()
    assert (settings.triposg_up, settings.triposg_front, settings.triposg_fallback_decoder) == ("X", "view", True)


@pytest.mark.parametrize("name, value", [("FORGE_TRIPOSG_UP", "W"), ("FORGE_TRIPOSG_UP", "-Y"),
                                         ("FORGE_TRIPOSG_FRONT", "back"), ("FORGE_TRIPOSG_FRONT", "yes")])
def test_a_value_outside_the_choices_stops_the_service(env, name, value):
    env.setenv(name, value)
    with pytest.raises(ValueError, match=name):
        Settings.from_env()


def test_the_generation_cap_and_the_decoder_depth_have_their_variables(env):
    env.setenv("FORGE_MAX_GENERATED", "8")
    env.setenv("FORGE_TRIPOSG_OCTREE_DEPTH", "8")
    settings = Settings.from_env()
    assert settings.max_generated == 8 and settings.triposg_octree_depth == 8


@pytest.mark.parametrize("name, value", [("FORGE_MAX_GENERATED", "0"), ("FORGE_MAX_GENERATED", "33"),
                                         ("FORGE_TRIPOSG_OCTREE_DEPTH", "5"), ("FORGE_TRIPOSG_OCTREE_DEPTH", "11")])
def test_a_cap_or_depth_out_of_range_stops_the_service(env, name, value):
    env.setenv(name, value)
    with pytest.raises(ValueError, match=name):
        Settings.from_env()
