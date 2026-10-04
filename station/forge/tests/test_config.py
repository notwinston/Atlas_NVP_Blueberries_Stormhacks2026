import pytest

from forge.config import Settings

NEW = ("FORGE_CLEANING_TIMEOUT_S", "FORGE_POOR_FIT", "FORGE_TRIPOSG_UP", "FORGE_TRIPOSG_FRONT",
       "FORGE_TRIPOSG_FALLBACK_DECODER")


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
        assert settings.triposg_fallback_decoder is True


def test_each_setting_has_its_variable(env):
    env.setenv("FORGE_CLEANING_TIMEOUT_S", "45")
    env.setenv("FORGE_POOR_FIT", "0.3")
    env.setenv("FORGE_TRIPOSG_UP", "z")  # case does not matter
    env.setenv("FORGE_TRIPOSG_FRONT", "Canonical")
    env.setenv("FORGE_TRIPOSG_FALLBACK_DECODER", "0")
    settings = Settings.from_env()
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
