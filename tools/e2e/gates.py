#!/usr/bin/env python3
"""The Phase 1 gates, G0 to G7, as far as they can be proved with no Unity Editor, no phone and no headset.

  python3 tools/e2e/gates.py                 # everything: about ten minutes
  python3 tools/e2e/gates.py --no-station    # leave out the Station's own suites and its rehearsal
  python3 tools/e2e/gates.py --trx DIR       # do not run verify.py again: read the .trx files of an earlier run
  python3 tools/e2e/gates.py --tag T         # verify.py's output folder (default: gates)

It runs, in this order, and keeps every log in ~/.cache/atlas-gates/<tag>/:

  1. tools/headless/verify.py all            the reference rules, the three builds, every headless test
  2. tools/gen_meta.py --check               every asset has its .meta
  3. tools/gen_scenes.py --check             the scenes and the build list are the generator's output
  4. a search for the header of a lane placeholder in the Phase 1 code
  5. dotnet test station/hub.tests           the Hub
  6. pytest station/forge/tests              the Forge, with the procedural generator
  7. node --test station/console-tests       the Console's modules
  8. unittest tools/perf                     the summariser of the performance tour
  9. tools/e2e/run_station_rehearsal.sh      the real Hub and Forge on the loopback interface

and then prints the checklist of each gate (Master Plan section 11.3) with, for every line, whether it is
proven here and by which tests, or what it waits for: a phone, the headset, the Editor, or a person. A line
is "proven" only when every test named for it ran in this run and passed; a test that is missing counts as
a failure, so a renamed or deleted test cannot pass a gate silently.

Exit code 0 when no headless line failed and none was left out for want of a tool (node, or a Python with
the Forge's packages: such a line is printed as "not run" with what is missing). Lines left out with
--no-station are printed as "not run" too and do not fail the run. A gate itself passes only when its device
lines are ticked as well: that is done on the phone, with docs/verification/UNITY_SIDE_BRIEF.md.

Needs what tools/headless/README.md and tools/e2e/README.md need: the .NET 8 SDK, ATLAS_UNITY_MANAGED,
node, and a Python with the Forge's packages (station/forge/.venv, FORGE_PYTHON, or this interpreter).
"""
import argparse
import glob
import os
import re
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ASSETS = os.path.join(REPO, "ATLAS", "Assets", "Atlas")
TRX = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

PROVEN, FAILED, NOT_RUN, WAITS = "proven", "FAILED", "not run", "waits"

# The header lane B's stand-ins for the other lanes carried. Phase 2 and 3 code may still hold one.
PLACEHOLDER_MARK = "PLACEHOLDER (lane"
PLACEHOLDER_EXEMPT = ("Blades" + os.sep, "StreamingAssets" + os.sep)

# Master Plan section 11.1: every named test, and the test that carries the name in this tree.
NAMED_TESTS = [
    ("Kernel_ExpLog", r"KernelMathTests\.Kernel_ExpLog\b"),
    ("Kernel_EtaOrthonormal", r"KernelMathTests\.Kernel_EtaOrthonormal\b"),
    ("Kernel_Inverse", r"KernelMathTests\.Kernel_Inverse\b"),
    ("Holonomy_Triangle", r"\.Holonomy_Triangle\b"),
    ("Closure_345", r"KernelMathTests\.Closure_345\b"),
    ("Lorentz_Drift", r"\.Lorentz_Drift\b"),
    ("Float32_Upload", r"\.Float32_Upload\b"),
    ("TileId_RingInvariant", r"\.TileId_RingInvariant\b"),
    ("TileId_Growth", r"\.TileId_Growth\b"),
    ("TileId_VertexClosure", r"\.TileId_VertexClosure\b"),
    ("TileId_Unique", r"\.TileId_Unique\b"),
    ("TileId_Seed (Mono half)", r"\.TileId_Seed\b"),
    ("VisibleSet_Counts", r"\.VisibleSet_Counts"),
    ("TileMesh_SeamsEdges", r"\.TileMesh_SeamsEdges"),
    ("Mode2_SightLines", r"\.Mode2_SightLines_"),
    ("WorldLint_All", r"\.WorldLint_All"),
    ("Glued_Handedness", r"\.Glued_Handedness"),
    ("Portal_Graph", r"\.Portal_Graph"),
    ("Slicer_Tesseract", r"\.Slicer_Tesseract"),
    ("Slicer_Ball", r"\.Slicer_Ball"),
    ("Slicer_MovingSphere", r"\.Slicer_MovingSphere"),
    ("Shader_Contract", r"\.ShaderContractTests\."),
    ("FloorScan_Synthetic", r"\.FloorScan_Synthetic"),
    ("BumpGuard_Replay", r"\.BumpGuardReplayTests\."),
    ("Safety_Independence (assembly half)", r"\.Safety_Independence_AssemblyHalf"),
    ("Safety_Independence (pose-jump half)", r"\.Safety_Independence_PoseJump"),
    ("Link_RoundTrip", r"\.Link_RoundTrip"),
    ("Link_ClockSync", r"\.Link_ClockSync"),
    ("Link_Watchdog", r"\.Link_Watchdog"),
]
LANE_AB = ("Kernel_", "Holonomy_", "Closure_", "Lorentz_", "Float32_", "TileId_", "VisibleSet_", "TileMesh_", "Mode2_",
           "WorldLint_", "Glued_", "Portal_", "Slicer_", "Shader_")


def named(prefixes=None):
    return [p for n, p in NAMED_TESTS if prefixes is None or n.startswith(prefixes)]


SCENARIO = r"E2EPhase1ScenarioTests\.E2E_Phase1_OneSession"

# Each line: (what the checklist asks, how it is settled here).
#   {"tests": [regex, ...], "steps": [step, ...]}   proven when every regex matches a test that ran, all matches passed,
#                                                    and every step passed
#   {"waits": "phone" | "headset" | "Editor" | "person" | ...}   cannot be settled without it
GATES = [
    ("G0", "Contracts frozen", [
        ("Assembly definitions exist; the reference table F1 holds and has no cycle",
         {"steps": ["rules", "build"], "tests": [r"AssemblyGraphTests\."]}),
        ("Safety_Independence, assembly half", {"steps": ["rules"], "tests": [r"\.Safety_Independence_AssemblyHalf"]}),
        ("The three sample rooms, budget_room and the standing room validate against the RoomModel schema",
         {"tests": [r"RoomModelFixtureTests\.", r"RoomModelJsonTests\."]}),
        ("The Link schema of Appendix B: every message round-trips, State is 202 bytes, the world hash vector",
         {"tests": [r"\.Link_RoundTrip", r"\.WorldHash_Vector"]}),
        ("Parameter registry ids", {"tests": [r"ParamRegistryTests\."]}),
        ("The look-shader property list", {"tests": [r"\.ShaderContractTests\."]}),
        ("Every asset has a .meta; the scenes and the build list are the generator's", {"steps": ["meta", "scenes"]}),
        ("No lane placeholder is left in the Phase 1 code", {"steps": ["placeholders"]}),
        ("The contracts are merged and tagged", {"steps": ["tag"]}),
    ]),
    ("G1", "First light", [
        ("The phone build, the Station build and the Editor compile", {"steps": ["build"]}),
        ("Pairing with the Hub", {"tests": [r"PairingTests\."]}),
        ("One telemetry row per second, and its CSV", {"tests": [r"TelemetryCsvTests\.", r"\.Telemetry_Csv"]}),
        ("State from a phone reaches the Hub's log at 50 Hz or more with under 2 % loss (ReplayPhone, loopback)",
         {"steps": ["hub", "rehearsal"]}),
        ("Stereo at 60 fps on phone A with the Metal HUD; CPU main 3 ms, GPU 2 ms", {"waits": "phone in the headset"}),
        ("Unity version, thermal frame-rate setting off, the thermal plugin returns a value", {"waits": "phone"}),
        ("The 60 Hz ARKit configuration in the start-up log; the depth image is non-null", {"waits": "phone"}),
        ("One command installs main on a phone; every engineer has installed a build", {"waits": "phone and people"}),
        ("Both phones report LiDAR; camera and LiDAR unobstructed in the headset", {"waits": "phones in the headset"}),
    ]),
    ("G2", "Curved-walk spike", [
        ("Five-corner closure under 0.25 m at the loop-fit L: the kernel alone, the recorded lap, and the lap through the app's frame loop",
         {"tests": [r"KernelMathTests\.Closure_345\b", r"\.Closure_q5_Replay", r"\.E2E_CurvedWalk_TheFiveCornerLap", SCENARIO]}),
        ("The curvature ramps only while the walker stands", {"tests": [r"\.Curvature_FrozenWhileMoving", r"\.Ramp_Duration", SCENARIO]}),
        ("Protocol 11.4.4 with three walkers and a spotter; wall contacts and spotter stops counted", {"waits": "headset and people"}),
        ("Decision recorded: Mode 1, Mode 1 at a larger L, or Mode 2; the comfort floor for L and R", {"waits": "a person, after the spike"}),
    ]),
    ("G3", "Spaces in the Editor", [
        ("Every 11.1 test of lanes A and B (TileId_Seed: the Mono half)", {"tests": named(LANE_AB)}),
        ("S1, S5 and S6 load in SpaceLab on every fixture and expose their parameters", {"tests": [r"\.E2E_Flow_EveryFixture"]}),
        ("S2, S3 and S4 load in SpaceLab on the three sample rooms and expose their parameters",
         {"tests": [r"\.G3_CurvedSpaces_LoadInSpaceLabOnEverySampleRoom"]}),
        ("Mode 1 and Mode 2 switch on one tile graph with no tile-id change",
         {"tests": [r"\.G3_Mode1ToMode2_OnOneTileGraph", r"\.E2E_CurvedSpaces_HomeToS2ToS3ToS4ToHome", SCENARIO]}),
        ("Each space can be chosen from the gamepad menu", {"tests": [r"MenuModelTests\.", r"SpaceActionsTests\."]}),
        ("The spaces under AR simulation in the Editor: scenes load, shaders compile, the tests of category Unity", {"waits": "Editor"}),
    ]),
    ("G4", "Spaces on the phone at budget", [
        ("Draw calls and triangles by simulation: the generated world, the Glued lattice, the room, Home",
         {"tests": [r"\.WorldLint_All", r"GluedBudgetTests\.", r"RoomBudgetTests\."]}),
        ("The thermal ladder's logic and what each tier sets", {"tests": [r"LadderControllerTests\.", r"QualityApplierTests\."]}),
        ("The performance tour, its soak schedule and its summariser", {"tests": [r"PerfTour"], "steps": ["perf"]}),
        ("S0 to S6 at every must row, cold, on budget_room; the register filled", {"waits": "phone in the headset"}),
        ("Fog opaque at rho = 3, no hole and no popping", {"waits": "phone in the headset"}),
        ("The 15-minute soak, with depth on and with depth off", {"waits": "phone in the headset"}),
        ("Each tier forced from the Console takes effect within 1 s", {"waits": "phone and Station"}),
    ]),
    ("G5", "Scan, Home and Bump Guard", [
        ("FloorScan_Synthetic: floor analysis and loop-fit", {"tests": [r"\.FloorScan_Synthetic"]}),
        ("A 1.4 x 1.4 m free floor selects standing mode, in the flat and in the curved spaces",
         {"tests": [r"RoomModelFixtureTests\.", r"\.G5_StandingRoom"]}),
        ("The scan flow, the outline, the object boxes and the photo capture, on synthetic input",
         {"tests": [r"ScanFlowTests\.", r"OutlineBuilderTests\.", r"PhotoCaptureCoreTests\."]}),
        ("Home loads in SpaceLab and exposes its parameters", {"tests": [r"HomeSpaceTests\.", r"\.E2E_Flow_EveryFixture"]}),
        ("The world frame is the RoomModel's anchor frame, the floor at y = 0", {"tests": [r"\.E2E_WorldFrame_"]}),
        ("BumpGuard_Replay: 0.6 s before contact, one rising edge, no STRONG on the lap, tracking lost",
         {"tests": [r"\.BumpGuardReplayTests\.", r"\.E2E_WallApproach_BumpGuardWarns", r"\.E2E_Lap_"]}),
        ("Safety_Independence, pose-jump half", {"tests": [r"\.Safety_Independence_PoseJump", r"\.E2E_JumpAndLoss"]}),
        ("Bump Guard behaves identically with the Station switched off", {"tests": [r"\.E2E_WallApproach_StationCablePulled"]}),
        ("Bump Guard is armed and cues at the real wall in S2, S3 and S4", {"tests": [r"\.E2E_WallApproach_InTheCurvedSpaces", SCENARIO]}),
        ("The cue's scheduler: WARN, STRONG and tracking lost", {"tests": [r"BumpCueSchedulerTests\."]}),
        ("Every SpaceLab action with the gamepad alone and with gaze dwell alone", {"tests": [r"InputTwinsTests\.", r"GazeDwellTests\."]}),
        ("A 0.5 m jump is absent from the geometry stream and present in the safety stream; tracking lost within 0.5 s",
         {"tests": [r"PoseFilterTests\.", r"\.E2E_JumpAndLoss"]}),
        ("Onboarding in hand, as a flow", {"tests": [r"OnboardingFlowTests\."]}),
        ("The world map's relocaliser, as logic", {"tests": [r"WorldMapRelocaliserTests\."]}),
        ("Self-scan in two rooms at must; a touched wall within 0.05 m; relocalisation within 10 s", {"waits": "phone"}),
        ("Meshing and plane detection off after the scan; LiDAR depth stays on", {"waits": "phone"}),
        ("Cue within one frame in 20 of 20 trials; protocol 11.4.5 by an engineer who did not write it", {"waits": "headset and people"}),
        ("Static-map error after the 5-minute lap; three first-time users reach Home", {"waits": "headset and people"}),
    ]),
    ("G6", "Station link, four functions", [
        ("Link_RoundTrip, Link_ClockSync, Link_Watchdog (L6 and L12 as logic)",
         {"tests": [r"\.Link_RoundTrip", r"\.Link_ClockSync", r"\.Link_Watchdog"]}),
        ("L3, L4, L9, L10 on the loopback interface with the real Hub; the Hub's own tests", {"steps": ["hub", "rehearsal"]}),
        ("L7: a closed five-corner loop in S2, tile ids equal on 100 % of samples at the Spectator, the generated world's hash equal",
         {"tests": [SCENARIO, r"\.E2E_CurvedWalk_TheFiveCornerLap"]}),
        ("L8: a world hash that differs shows \"world mismatch\" and no world", {"tests": [r"SpectatorCoreTests\."]}),
        ("L9, L10: the six operator commands within 250 ms; values clamp; ignored under lock", {"tests": [r"\.E2E_OperatorCommands"]}),
        ("L11: the telemetry CSV, 34 columns", {"tests": [r"TelemetryCsvTests\."], "steps": ["rehearsal"]}),
        ("L13, L14 as logic: the frame allocates nothing with the link on; the cue is the same with the cable pulled",
         {"tests": [r"\.E2E_SteadyState_ThePhonesFrameAllocatesNothing", r"\.E2E_WallApproach_StationCablePulled"]}),
        ("L15 to L21 with the procedural generator: jobs, GLB limits, cache hit, no crop kept, the fallback set",
         {"tests": [r"AssetClientCoreTests\.", r"GlbValidatorTests\.", r"FallbackSetTests\."], "steps": ["forge", "rehearsal"]}),
        ("L22: every kill switch from the on-device menu", {"tests": [r"MenuModelTests\."]}),
        ("The flat-twin map shows the tile-id sequence of the log", {"tests": [r"FlatTwinTests\.", SCENARIO]}),
        ("TileId_Seed, Mono half", {"tests": [r"\.TileId_Seed\b"]}),
        ("The Console's modules", {"steps": ["console"]}),
        ("L1, L2, L5, L6, L11, L12 over the Station's router; the on-device half of TileId_Seed", {"waits": "phone and Station"}),
        ("L15 to L19 with TripoSG; L17 mesh swap under 20 ms; L21 by packet capture", {"waits": "phone and the Forge's GPU"}),
        ("The Spectator's picture against the headset's, screen to screen", {"waits": "phone and Station"}),
    ]),
    ("G7", "Phase-1 exit", [
        ("Every 11.1 test, headless half, on this tree", {"tests": named(), "steps": ["verify"]}),
        ("The 3-4-5 walk closes within 0.25 m after 3, 4 and 5 corners through the app's frame loop; "
         "the curvature never changes above 0.1 m/s",
         {"tests": [SCENARIO, r"\.Closure_q3_Replay", r"\.Closure_q4_Replay", r"\.Closure_q5_Replay", r"\.Curvature_FrozenWhileMoving"]}),
        ("Mode 1 and Mode 2 are both walked from the same tile zero", {"tests": [SCENARIO, r"\.G3_Mode1ToMode2_OnOneTileGraph"]}),
        ("Room content in the registered spaces: Glued draws the room once among the copies; "
         "Aligned Cells keeps a table edge within 5 cm after ten crossings",
         {"tests": [r"\.Mode2_Room_TableEdge", r"GluedLattice"]}),
        ("One session, room to tile zero to S2, S3, S4 and Home, with the Spectator following every frame", {"tests": [SCENARIO]}),
        ("The Station: Hub, Forge, Console and the rehearsal", {"steps": ["hub", "forge", "console", "rehearsal"]}),
        ("All four Station functions in one session without a restart", {"waits": "phone and Station"}),
        ("S0 to S6 on phone A at must budget in the room scanned for the session", {"waits": "phone in the headset"}),
        ("Self-scan to Home with at least three generated objects placed", {"waits": "phone and the Forge's GPU"}),
        ("A virtual table edge within 5 cm of the real one at three touch points", {"waits": "headset"}),
        ("Bump Guard warns at a wall and at a chair moved after the scan", {"waits": "headset"}),
        ("The 15-minute soak, motion-to-photon and loop drift; every register row measured", {"waits": "phone in the headset"}),
    ]),
]


class Step:
    def __init__(self, key, title):
        self.key, self.title = key, title
        self.status, self.detail = NOT_RUN, "not run"

    def set(self, ok, detail):
        self.status, self.detail = (PROVEN if ok else FAILED), detail


def dotnet():
    local = os.path.expanduser("~/.dotnet/dotnet")
    return local if os.path.exists(local) else (shutil.which("dotnet") or "dotnet")


def run(cmd, log, cwd=REPO, env=None, echo=False):
    """Runs a command with its output in a log file (and on the screen when asked). Returns (exit code, output)."""
    e = dict(os.environ)
    e.update({"DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1", "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT": "1"})
    if env:
        e.update(env)
    out = []
    try:
        p = subprocess.Popen(cmd, cwd=cwd, env=e, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, errors="replace")
    except OSError as error:
        return 127, str(error)
    with open(log, "w") as f:
        for line in p.stdout:
            f.write(line)
            out.append(line)
            if echo:
                sys.stdout.write("    " + line)
                sys.stdout.flush()
    return p.wait(), "".join(out)


def last_line(text, pattern):
    hits = [l.strip() for l in text.splitlines() if re.search(pattern, l)]
    return hits[-1] if hits else ""


# What the Forge's server needs to start with the procedural generator, and what its test suite needs on top.
FORGE_SERVER = "uvicorn, fastapi, PIL, numpy"
FORGE_SUITE = FORGE_SERVER + ", pytest, trimesh"


def forge_python(modules):
    """An interpreter that can import the modules: FORGE_PYTHON, the Forge's own environment, or this one."""
    for candidate in (os.environ.get("FORGE_PYTHON"), os.path.join(REPO, "station", "forge", ".venv", "bin", "python"), sys.executable):
        if not candidate or not os.path.exists(candidate):
            continue
        try:
            if subprocess.run([candidate, "-c", "import " + modules], capture_output=True).returncode == 0:
                return candidate
        except OSError:
            pass
    return None


def read_trx(folder):
    """{full test name: outcome} of every .trx in a folder. A test with cases has one entry per case."""
    results = {}
    for path in sorted(glob.glob(os.path.join(folder, "*.trx"))):
        root = ET.parse(path).getroot()
        classes = {}
        for unit in root.findall(".//t:UnitTest", TRX):
            method = unit.find("t:TestMethod", TRX)
            classes[unit.get("id")] = method.get("className") if method is not None else ""
        for result in root.findall(".//t:UnitTestResult", TRX):
            results[classes.get(result.get("testId"), "") + "." + result.get("testName")] = result.get("outcome")
    return results


def placeholder_files():
    hits = []
    for root, dirs, files in os.walk(ASSETS):
        dirs[:] = [d for d in dirs if not d.endswith("~")]
        for name in files:
            if not name.endswith((".cs", ".shader", ".hlsl", ".cginc", ".md", ".json", ".asmdef")):
                continue
            path = os.path.join(root, name)
            rel = os.path.relpath(path, ASSETS)
            if rel.startswith(PLACEHOLDER_EXEMPT):
                continue
            with open(path, encoding="utf-8-sig", errors="replace") as f:
                if PLACEHOLDER_MARK in f.read():
                    hits.append(rel)
    return sorted(hits)


def settle(how, steps, tests):
    """(status, evidence) of one checklist line."""
    if "waits" in how:
        return WAITS, "for " + how["waits"]
    status, notes = PROVEN, []
    for key in how.get("steps", []):
        step = steps[key]
        if step.status == FAILED:
            status = FAILED
        elif step.status == NOT_RUN and status != FAILED:
            status = NOT_RUN
        elif step.status == WAITS and status == PROVEN:
            status = WAITS
        notes.append("%s: %s" % (step.title, step.detail))
    patterns = how.get("tests", [])
    if patterns and tests is None:
        if status != FAILED:
            status = NOT_RUN
        notes.append("the headless tests did not run")
    elif patterns:
        passed, bad = 0, []
        for pattern in patterns:
            hits = [(n, o) for n, o in tests.items() if re.search(pattern, n)]
            if not hits:
                bad.append("no test matches " + pattern)
            for n, o in hits:
                if o == "Passed":
                    passed += 1
                else:
                    bad.append("%s: %s" % (n.split(".", 3)[-1], o))
        if bad:
            status = FAILED
            notes += bad
        notes.append("%d tests passed" % passed)
    return status, "; ".join(notes)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--tag", default="gates", help="verify.py's output folder, and this run's log folder")
    ap.add_argument("--trx", default="", help="read the .trx files of an earlier `verify.py all --results DIR` and skip verify.py")
    ap.add_argument("--no-station", action="store_true", help="leave out the Hub, Forge and Console suites and the Station rehearsal")
    ap.add_argument("--quiet", action="store_true", help="do not echo verify.py's output")
    args = ap.parse_args()

    tag = re.sub(r"[^A-Za-z0-9_.-]", "_", args.tag)
    logs = os.path.join(os.path.expanduser("~/.cache/atlas-gates"), tag)
    os.makedirs(logs, exist_ok=True)
    started = time.time()
    steps = {k: Step(k, t) for k, t in [
        ("verify", "verify.py all"), ("rules", "rules"), ("build", "three builds"), ("meta", "gen_meta --check"),
        ("scenes", "gen_scenes --check"), ("placeholders", "placeholder search"), ("tag", "git tag"),
        ("hub", "Hub tests"), ("forge", "Forge tests"), ("console", "Console tests"), ("perf", "perf summariser tests"),
        ("rehearsal", "Station rehearsal")]}
    skipped = set()

    def begin(key):
        print("== %s" % steps[key].title)
        sys.stdout.flush()

    def end(key):
        print("   %s  %s" % (steps[key].status, steps[key].detail))
        sys.stdout.flush()

    # 1  The Unity assemblies: rules, three builds, every headless test, one .trx per test assembly.
    tests = None
    if args.trx:
        trx = os.path.abspath(args.trx)
        tests = read_trx(trx)
        for key in ("verify", "rules", "build"):
            skipped.add(key)
            steps[key].detail = "not run again (--trx)"
        print("== verify.py all: not run again; %d test results read from %s" % (len(tests), trx))
        if not tests:
            sys.exit("no .trx file in " + trx)
    else:
        begin("verify")
        trx = os.path.join(logs, "trx")
        shutil.rmtree(trx, ignore_errors=True)
        code, out = run([sys.executable, os.path.join(REPO, "tools", "headless", "verify.py"), "all", "--tag", tag, "--results", trx],
                        os.path.join(logs, "verify.log"), echo=not args.quiet)
        rules = last_line(out, r"^\[rules\]")
        builds = [l.strip() for l in out.splitlines() if l.startswith("[build ")]
        suites = [l for l in out.splitlines() if l.startswith("[test ")]
        steps["rules"].set(rules.startswith("[rules] OK"), rules or "no rules line")
        steps["build"].set(len(builds) == 3 and all("] OK" in b for b in builds), "; ".join(b.split("  ")[0] + (" OK" if "] OK" in b else " FAIL") for b in builds) or "no build line")
        tests = read_trx(trx) if os.path.isdir(trx) else {}
        passed = sum(1 for o in tests.values() if o == "Passed")
        failing = sum(1 for l in suites if "] FAIL" in l)
        steps["verify"].set(code == 0, "exit %d; %d test assemblies, %d failing; %d of %d headless tests passed"
                            % (code, len(suites), failing, passed, len(tests)))
        end("verify")
        if not tests:
            tests = None

    # 2, 3  Assets and scenes.
    begin("meta")
    code, out = run([sys.executable, os.path.join(REPO, "tools", "gen_meta.py"), "--check"], os.path.join(logs, "gen_meta.log"))
    steps["meta"].set(code == 0, (out.strip().splitlines() or ["no output"])[0])
    end("meta")
    begin("scenes")
    code, out = run([sys.executable, os.path.join(REPO, "tools", "gen_scenes.py"), "--check"], os.path.join(logs, "gen_scenes.log"))
    steps["scenes"].set(code == 0, (out.strip().splitlines() or ["no output"])[-1])
    end("scenes")

    # 4  Placeholders.
    begin("placeholders")
    hits = placeholder_files()
    steps["placeholders"].set(not hits, "none under ATLAS/Assets/Atlas outside Blades and StreamingAssets" if not hits
                              else "%d file(s): %s" % (len(hits), ", ".join(hits[:8])))
    end("placeholders")

    # G0's tag is a person's to make; here it is only looked for.
    try:
        tags = subprocess.run(["git", "tag", "--list", "[gG]0*"], cwd=REPO, capture_output=True, text=True).stdout.split()
    except OSError:
        tags = []
    steps["tag"].status = PROVEN if tags else WAITS
    steps["tag"].detail = ", ".join(tags) if tags else "none named g0: a person tags the commit the contracts were agreed on"

    # 5 to 9  The Station.
    station = ("hub", "forge", "console", "perf", "rehearsal")
    if args.no_station:
        for key in station:
            skipped.add(key)
            steps[key].detail = "left out (--no-station)"
        print("== the Station's suites and its rehearsal: left out (--no-station)")
    else:
        begin("hub")
        code, out = run([dotnet(), "test", os.path.join(REPO, "station", "hub.tests"), "-nologo"], os.path.join(logs, "hub.log"))
        steps["hub"].set(code == 0, last_line(out, r"(Passed!|Failed!|error)") or "exit %d" % code)
        end("hub")

        begin("forge")
        python = forge_python(FORGE_SUITE)
        if python is None:
            # A tool that is missing is not a test that failed: the line stays "not run", and the run does not exit 0.
            steps["forge"].detail = ("no Python here can import " + FORGE_SUITE + ": make station/forge/.venv on this machine "
                                     "(station/forge/README.md) or set FORGE_PYTHON")
        else:
            code, out = run([python, "-m", "pytest", "tests", "-q"], os.path.join(logs, "forge.log"), cwd=os.path.join(REPO, "station", "forge"))
            steps["forge"].set(code == 0, last_line(out, r"\d+ (passed|failed|error)") or "exit %d" % code)
        end("forge")

        begin("console")
        node = shutil.which("node")
        if node is None:
            steps["console"].detail = "node is not installed"
        else:
            code, out = run([node, "--test"], os.path.join(logs, "console.log"), cwd=os.path.join(REPO, "station", "console-tests"))
            counts = [last_line(out, r"^\D*%s \d+" % word) for word in ("pass", "fail")]
            steps["console"].set(code == 0, ", ".join(c.lstrip("# ").strip() for c in counts if c) or "exit %d" % code)
        end("console")

        begin("perf")
        code, out = run([sys.executable, "-m", "unittest", "test_summarise"], os.path.join(logs, "perf.log"), cwd=os.path.join(REPO, "tools", "perf"))
        steps["perf"].set(code == 0, last_line(out, r"^(Ran \d+|OK|FAILED)") or "exit %d" % code)
        end("perf")

        begin("rehearsal")
        python = forge_python(FORGE_SERVER)
        if python is None:
            steps["rehearsal"].detail = "no Python here can import " + FORGE_SERVER + ": make station/forge/.venv or set FORGE_PYTHON"
        else:
            code, out = run(["bash", os.path.join(REPO, "tools", "e2e", "run_station_rehearsal.sh")], os.path.join(logs, "rehearsal.log"),
                            env={"FORGE_PYTHON": python})
            oks = sum(1 for l in out.splitlines() if l.startswith("  ok "))
            fails = [l.strip() for l in out.splitlines() if l.startswith("  FAIL")]
            steps["rehearsal"].set(code == 0, "%d checks passed, %d failed%s" % (oks, len(fails), (": " + fails[0]) if fails else ""))
        end("rehearsal")

    # ---- the gates -----------------------------------------------------------------------------------------
    print()
    print("Phase 1 gates (Master Plan section 11.3), as far as they go with no phone, no headset and no Editor")
    try:
        head = subprocess.run(["git", "log", "-1", "--format=%h %s"], cwd=REPO, capture_output=True, text=True).stdout.strip()
        dirty = subprocess.run(["git", "status", "--porcelain"], cwd=REPO, capture_output=True, text=True).stdout.strip()
        print("commit %s%s" % (head, " + uncommitted changes" if dirty else ""))
    except OSError:
        pass
    failed = missing = 0
    summary = []
    for gate, title, lines in GATES:
        print()
        print("%s  %s" % (gate, title))
        count = {PROVEN: 0, FAILED: 0, NOT_RUN: 0, WAITS: 0}
        for what, how in lines:
            status, evidence = settle(how, steps, tests)
            count[status] += 1
            if status == FAILED:
                failed += 1
            elif status == NOT_RUN and (tests is None or any(steps[k].status == NOT_RUN and k not in skipped for k in how.get("steps", []))):
                missing += 1
            label = {PROVEN: "proven ", FAILED: "FAILED ", NOT_RUN: "not run", WAITS: "waits  "}[status]
            print("  [%s] %s" % (label, what))
            print("            %s" % evidence)
        headless = count[PROVEN] + count[FAILED] + count[NOT_RUN]
        summary.append("%s  %-30s headless %d of %d proven%s%s; %d wait for a device or a person"
                       % (gate, title, count[PROVEN], headless, (", %d FAILED" % count[FAILED]) if count[FAILED] else "",
                          (", %d not run" % count[NOT_RUN]) if count[NOT_RUN] else "", count[WAITS]))

    print()
    print("Section 11.1, test by test")
    for name, pattern in NAMED_TESTS:
        status, evidence = settle({"tests": [pattern]}, steps, tests)
        print("  [%s] %-38s %s" % ({PROVEN: "proven ", FAILED: "FAILED ", NOT_RUN: "not run"}[status], name, evidence))
        if status == FAILED:
            failed += 1
    print("  TileId_Seed's on-device half (IL2CPP) is checked at G6 on the phone.")

    print()
    print("Summary (%d s; logs in %s)" % (time.time() - started, logs))
    for line in summary:
        print("  " + line)
    if steps["verify"].status == FAILED:
        failed += 1
        print("  verify.py all did not exit 0: see verify.log")
    if failed:
        print("  %d headless line(s) FAILED" % failed)
    if missing:
        print("  %d headless line(s) could not be run" % missing)
    if not failed and not missing:
        print("  Every headless line that was run is proven. No gate is passed by this alone: each has lines that wait.")
    sys.exit(1 if failed or missing else 0)


if __name__ == "__main__":
    main()
