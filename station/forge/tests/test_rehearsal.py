"""The Station rehearsal's own rules (tools/e2e/station_rehearsal.py), where they can be wrong without the Station."""
import importlib.util

from conftest import ROOT

spec = importlib.util.spec_from_file_location("station_rehearsal",
                                              ROOT.parent.parent / "tools" / "e2e" / "station_rehearsal.py")
rehearsal = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rehearsal)


def test_the_rehearsal_accepts_the_up_axis_that_was_measured():
    ready = {"state": "ready", "attempts": 1, "fitUp": "Y", "fitYaw": 0}
    assert rehearsal.plain_generation(ready, "procedural") and rehearsal.plain_generation(ready, "triposg")
    # On the Station, once FORGE_TRIPOSG_UP is measured as Z or X, every TripoSG mesh says so: nothing is wrong.
    for axis in ("Z", "X"):
        assert rehearsal.plain_generation({**ready, "fitUp": axis}, "triposg")
        assert not rehearsal.plain_generation({**ready, "fitUp": axis}, "procedural")  # those are built upright
    # What it still refuses: an axis that is none, a second generation, a fit where there was no profile.
    assert not rehearsal.plain_generation({**ready, "fitUp": "W"}, "triposg")
    assert not rehearsal.plain_generation({k: v for k, v in ready.items() if k != "fitUp"}, "triposg")
    assert not rehearsal.plain_generation({**ready, "attempts": 2}, "procedural")
    assert not rehearsal.plain_generation({**ready, "fit": 0.1}, "procedural")


def test_the_rehearsal_holds_the_contract_of_both_copies():
    assert rehearsal.LEVELS == {"full": {"tris": 15000, "bytes": 2 * 1024 * 1024, "room": 120000},
                                "light": {"tris": 5000, "bytes": 1024 * 1024, "room": 60000}}
    assert rehearsal.MIN_TRIS == 1500 and rehearsal.MAX_EDGE == 0.25
