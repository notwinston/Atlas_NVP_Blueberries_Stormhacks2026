#!/usr/bin/env python3
"""Headless compile check and unit-test runner for the Atlas assemblies. No Unity Editor needed.

Every .asmdef under ATLAS/Assets/Atlas becomes an SDK-style C# project that is compiled against the real
Unity 6000.3.25f1 managed assemblies and the package assemblies Unity already built into Library/ScriptAssemblies,
so a wrong Unity or package API is a compile error here exactly as it is in the Editor.

  verify.py build [Assembly ...] [--config ios|win|editor|all] [--tag T] [--warn]
  verify.py test  [Assembly.Tests ...] [--filter EXPR] [--tag T] [--results DIR]
  verify.py rules
  verify.py all   [--tag T]
  verify.py wait  Assembly ... [--timeout SECONDS] [--tag T]

build   compiles the named assemblies and what they reference (default: every assembly) in each configuration:
          ios     the phone build   (UNITY_IOS, no UNITY_EDITOR, no UnityEditor.dll)
          win     the Station build (UNITY_STANDALONE_WIN, ATLAS_SPECTATOR)
          editor  the Editor        (UNITY_EDITOR, UnityEditor.dll; includes Atlas.Editor and the test assemblies)
test    builds each test assembly twice: once exactly as Unity compiles it (Unity's NUnit and Test Runner,
        compile only), and once against NuGet NUnit with ATLAS_HEADLESS defined, which is then run on .NET 8.
        Tests that need the engine must be wrapped in `#if !ATLAS_HEADLESS` and carry [Category("Unity")].
        Tests locate repository files through the ATLAS_PROJECT_DIR environment variable (falling back to the
        current directory, which is the project folder inside Unity).
rules   checks the assembly reference table and source rules of docs/contracts/README.md (F1, F7, rule 1).
all     rules + build (all configurations) + test (all test assemblies).
        --results DIR also writes one <Assembly>.trx per test assembly there: every test that ran, by name, with
        its outcome (tools/e2e/gates.py reads them).
wait    blocks until the named assemblies compile in the editor configuration (someone else is still writing
        them), polling every 20 s; exits 1 after --timeout (default 1500 s) and prints the remaining errors.

--tag gives every caller a private output folder, so several people or agents can run at once.

What it cannot check: shaders, scenes, prefabs, native iOS code, IL2CPP stripping, anything on a device.
Requires: .NET 8 SDK on PATH (or in ~/.dotnet), the Unity managed assemblies in ATLAS_UNITY_MANAGED
(default ~/unity-managed/Editor/Data/Managed), and ATLAS/Library/ScriptAssemblies from one Editor import.
"""
import argparse
import glob
import json
import os
import re
import subprocess
import sys
from xml.sax.saxutils import escape

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
PROJECT = os.path.join(REPO, "ATLAS")
ASSETS = os.path.join(PROJECT, "Assets", "Atlas")
SCRIPT_ASM = os.path.join(PROJECT, "Library", "ScriptAssemblies")
PACKAGE_CACHE = os.path.join(PROJECT, "Library", "PackageCache")
MANAGED = os.environ.get("ATLAS_UNITY_MANAGED", os.path.expanduser("~/unity-managed/Editor/Data/Managed"))
IOS_EXT = os.environ.get("ATLAS_UNITY_IOS_EXT", os.path.expanduser("~/unity-managed/iOSSupport"))

COMMON_DEFINES = [
    "UNITY_6000_3_25", "UNITY_6000_3", "UNITY_6000", "UNITY_5_3_OR_NEWER", "UNITY_2017_1_OR_NEWER",
    "UNITY_2018_1_OR_NEWER", "UNITY_2018_3_OR_NEWER", "UNITY_2019_1_OR_NEWER", "UNITY_2019_4_OR_NEWER",
    "UNITY_2020_1_OR_NEWER", "UNITY_2020_3_OR_NEWER", "UNITY_2021_1_OR_NEWER", "UNITY_2021_3_OR_NEWER",
    "UNITY_2022_1_OR_NEWER", "UNITY_2022_3_OR_NEWER", "UNITY_2023_1_OR_NEWER", "UNITY_2023_3_OR_NEWER",
    "UNITY_6000_0_OR_NEWER", "UNITY_6000_1_OR_NEWER", "UNITY_6000_2_OR_NEWER", "UNITY_6000_3_OR_NEWER",
    "ENABLE_INPUT_SYSTEM", "NET_STANDARD_2_1", "NET_STANDARD", "NET_STANDARD_2_0", "CSHARP_7_3_OR_NEWER",
]
CONFIGS = {
    "ios": {"platform": "iOS", "editor": False,
            "defines": ["UNITY_IOS", "UNITY_IPHONE", "ENABLE_IL2CPP", "UNITY_XR_ARKIT_LOADER_ENABLED"]},
    "win": {"platform": "WindowsStandalone64", "editor": False,
            "defines": ["UNITY_STANDALONE", "UNITY_STANDALONE_WIN", "ENABLE_MONO", "ATLAS_SPECTATOR"]},
    "editor": {"platform": "Editor", "editor": True,
               "defines": ["UNITY_EDITOR", "UNITY_EDITOR_64", "UNITY_EDITOR_OSX", "UNITY_IOS", "UNITY_IPHONE",
                           "UNITY_INCLUDE_TESTS", "ENABLE_MONO", "UNITY_XR_ARKIT_LOADER_ENABLED"]},
}
NOWARN = "CS0436;CS1701;CS1702;CS1591;CS0649;CS0169;CS0414;NU1701;NU1702;NETSDK1188;MSB3277;MSB3243;CS8021"
TEST_RUNNER_REFS = ("UnityEngine.TestRunner", "UnityEditor.TestRunner")

# docs/contracts/README.md, table F1: which Atlas assemblies each assembly may reference.
SPACES = "Atlas.Spaces.*"
BLADES = "Atlas.Blades.*"
ALLOWED = {
    "Atlas.Kernel": [],
    "Atlas.Link": [],
    "Atlas.World": ["Atlas.Kernel", "Atlas.Link"],
    "Atlas.Rendering": ["Atlas.Kernel", "Atlas.World"],
    "Atlas.Scan": ["Atlas.Kernel", "Atlas.World"],
    "Atlas.Scan.ARKit": ["Atlas.Kernel", "Atlas.World", "Atlas.Scan"],
    "Atlas.Safety": ["Atlas.World"],
    "Atlas.Input": [],
    "Atlas.Telemetry": ["Atlas.Kernel", "Atlas.Link", "Atlas.World"],
    # Tracked hands (documents/ATLAS_Hands_Design.md 3.2). Link for parameter types only, as a space.
    "Atlas.Hands": ["Atlas.Kernel", "Atlas.Link", "Atlas.World", "Atlas.Rendering", "Atlas.Input"],
    "Atlas.Hands.ARKit": ["Atlas.Kernel", "Atlas.World", "Atlas.Hands"],
    "Atlas.App": ["Atlas.Kernel", "Atlas.Link", "Atlas.World", "Atlas.Rendering", "Atlas.Scan", "Atlas.Safety",
                  "Atlas.Input", "Atlas.Telemetry", "Atlas.Hands"],
    SPACES: ["Atlas.Kernel", "Atlas.Link", "Atlas.World", "Atlas.Rendering", "Atlas.Input", "Atlas.Telemetry"],
    "Atlas.Spectator": ["Atlas.Kernel", "Atlas.Link", "Atlas.World", "Atlas.Rendering", "Atlas.Telemetry", SPACES],
    # World lint (lane B): the generator and the real tile meshes, in the Editor only.
    "Atlas.World.Lint.Editor": ["Atlas.Kernel", "Atlas.World", "Atlas.Rendering"],
    # A use case (Master Plan 6.2): the layers below the app and the spaces, never the app or another use case.
    BLADES: ["Atlas.Kernel", "Atlas.Link", "Atlas.World", "Atlas.Rendering", "Atlas.Scan", "Atlas.Safety", "Atlas.Input",
             "Atlas.Telemetry", SPACES],
}


class Asm:
    def __init__(self, path):
        with open(path, encoding="utf-8-sig") as f:
            d = json.load(f)
        self.path = path
        self.dir = os.path.dirname(path)
        self.name = d["name"]
        self.root_namespace = d.get("rootNamespace", "")
        self.references = d.get("references", [])
        self.include = d.get("includePlatforms", [])
        self.exclude = d.get("excludePlatforms", [])
        self.unsafe = bool(d.get("allowUnsafeCode", False))
        self.no_engine = bool(d.get("noEngineReferences", False))
        self.constraints = d.get("defineConstraints", [])
        self.precompiled = d.get("precompiledReferences", []) if d.get("overrideReferences") else []
        self.version_defines = d.get("versionDefines", [])
        self.is_test = any(r in TEST_RUNNER_REFS for r in self.references) or "UNITY_INCLUDE_TESTS" in self.constraints

    def sources(self, all_dirs):
        nested = [d for d in all_dirs if d != self.dir and d.startswith(self.dir + os.sep)]
        out = []
        for root, dirs, files in os.walk(self.dir):
            dirs[:] = [d for d in dirs if os.path.join(root, d) not in nested and not d.endswith("~") and not d.startswith(".")]
            out += [os.path.join(root, f) for f in files if f.endswith(".cs")]
        return sorted(out)

    def in_config(self, cfg, defines):
        platform = CONFIGS[cfg]["platform"]
        if self.include and platform not in self.include:
            return False
        if platform in self.exclude:
            return False
        for c in self.constraints:
            want = not c.startswith("!")
            if (c.lstrip("!") in defines) != want:
                return False
        return True


def load():
    paths = glob.glob(os.path.join(ASSETS, "**", "*.asmdef"), recursive=True)
    asms = {a.name: a for a in (Asm(p) for p in paths)}
    return asms


def lock_packages():
    try:
        with open(os.path.join(PROJECT, "Packages", "packages-lock.json")) as f:
            return json.load(f)["dependencies"]
    except OSError:
        return {}


def defines_for(asm, cfg, extra=()):
    d = list(COMMON_DEFINES) + CONFIGS[cfg]["defines"] + list(extra)
    packages = lock_packages()
    for vd in asm.version_defines:
        if vd.get("name") in packages or vd.get("name") == "Unity":
            d.append(vd["define"])
    return d


def engine_refs(cfg):
    mod = os.path.join(MANAGED, "UnityEngine")
    refs = sorted(glob.glob(os.path.join(mod, "UnityEngine*.dll")))
    if CONFIGS[cfg]["editor"]:
        refs += sorted(glob.glob(os.path.join(mod, "UnityEditor*.dll")))
        refs += sorted(glob.glob(os.path.join(IOS_EXT, "UnityEditor.iOS.Extensions*.dll")))
    return [r for r in refs if os.path.basename(r) not in ("UnityEngine.dll", "UnityEditor.dll") or True]


def precompiled_path(name):
    hits = glob.glob(os.path.join(PACKAGE_CACHE, "**", name), recursive=True)
    hits += glob.glob(os.path.join(PROJECT, "Assets", "**", name), recursive=True)
    return hits[0] if hits else None


def closure(asms, names):
    seen, stack = [], list(names)
    while stack:
        n = stack.pop()
        if n in seen or n not in asms:
            continue
        seen.append(n)
        stack += [r for r in asms[n].references if r in asms]
    return seen


def write_if_changed(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    try:
        with open(path) as f:
            if f.read() == text:
                return
    except OSError:
        pass
    with open(path, "w") as f:
        f.write(text)


def ref_item(path, private):
    name = os.path.splitext(os.path.basename(path))[0]
    return ('    <Reference Include="%s"><HintPath>%s</HintPath><Private>%s</Private></Reference>'
            % (escape(name), escape(path), "true" if private else "false"))


def package_refs(asms, asm, problems):
    out = []
    for r in asm.references:
        if r in asms or r in TEST_RUNNER_REFS:
            continue
        if r.startswith("GUID:"):
            problems.append("%s: GUID reference %s is not supported; use the assembly name" % (asm.name, r))
            continue
        dll = os.path.join(SCRIPT_ASM, r + ".dll")
        if os.path.exists(dll):
            out.append(dll)
        else:
            problems.append("%s: reference '%s' has no assembly in Library/ScriptAssemblies" % (asm.name, r))
    for p in asm.precompiled:
        if p == "nunit.framework.dll":
            continue
        hit = precompiled_path(p)
        if hit:
            out.append(hit)
        else:
            problems.append("%s: precompiled reference '%s' not found" % (asm.name, p))
    return out


def gen_project(asms, asm, cfg, out_root, problems, variant="lib"):
    """variant: lib (as Unity compiles it), run (headless NUnit host, test assemblies only)."""
    all_dirs = [a.dir for a in asms.values()]
    run = variant == "run"
    defines = defines_for(asm, cfg, ["ATLAS_HEADLESS"] if run else [])
    proj_dir = os.path.join(out_root, cfg, asm.name + (".Run" if run else ""))
    lines = ['<Project Sdk="Microsoft.NET.Sdk">', "  <PropertyGroup>",
             "    <TargetFramework>%s</TargetFramework>" % ("net8.0" if run else "netstandard2.1"),
             "    <LangVersion>9.0</LangVersion>",
             "    <AssemblyName>%s</AssemblyName>" % asm.name,
             "    <RootNamespace>%s</RootNamespace>" % (asm.root_namespace or asm.name),
             "    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>",
             "    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>",
             "    <Nullable>disable</Nullable>",
             "    <ImplicitUsings>disable</ImplicitUsings>",
             "    <Deterministic>true</Deterministic>",
             "    <DebugType>portable</DebugType>",
             "    <InvariantGlobalization>true</InvariantGlobalization>",
             "    <CheckForOverflowUnderflow>false</CheckForOverflowUnderflow>",
             "    <AllowUnsafeBlocks>%s</AllowUnsafeBlocks>" % ("true" if asm.unsafe else "false"),
             "    <DefineConstants>%s</DefineConstants>" % ";".join(defines),
             "    <NoWarn>%s</NoWarn>" % NOWARN,
             "    <IsPackable>false</IsPackable>"]
    if run:
        lines += ["    <IsTestProject>true</IsTestProject>", "    <RollForward>Major</RollForward>"]
    lines += ["  </PropertyGroup>", "  <ItemGroup>"]
    for s in asm.sources(all_dirs):
        lines.append('    <Compile Include="%s" />' % escape(s))
    lines += ["  </ItemGroup>", "  <ItemGroup>"]
    for r in asm.references:
        if r in asms:
            lines.append('    <ProjectReference Include="%s" />'
                         % escape(os.path.join(out_root, cfg, r, r + ".csproj")))
    # The headless test host needs every assembly of the closure next to it at run time.
    dlls = package_refs(asms, asm, problems)
    if run:
        for dep in closure(asms, [asm.name]):
            dlls += package_refs(asms, asms[dep], [])
    engine = [] if asm.no_engine else engine_refs(cfg)
    if run:
        engine = engine_refs(cfg)
    for dll in sorted(set(dlls)):
        lines.append(ref_item(dll, run))
    for dll in engine:
        lines.append(ref_item(dll, run))
    if asm.is_test:
        if run:
            lines += ['    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />',
                      '    <PackageReference Include="NUnit" Version="3.14.0" />',
                      '    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />']
        else:
            nunit = precompiled_path("nunit.framework.dll")
            for dll in [nunit] + [os.path.join(SCRIPT_ASM, r + ".dll") for r in TEST_RUNNER_REFS]:
                if dll and os.path.exists(dll):
                    lines.append(ref_item(dll, False))
    lines += ["  </ItemGroup>", "</Project>", ""]
    path = os.path.join(proj_dir, asm.name + ".csproj")
    write_if_changed(path, "\n".join(lines))
    return path


def generate(asms, out_root):
    problems = []
    projects = {}
    for cfg in CONFIGS:
        for asm in asms.values():
            if not asm.in_config(cfg, defines_for(asm, cfg)):
                continue
            projects[(cfg, asm.name, "lib")] = gen_project(asms, asm, cfg, out_root, problems)
            if asm.is_test and cfg == "editor":
                projects[(cfg, asm.name, "run")] = gen_project(asms, asm, cfg, out_root, problems, "run")
    return projects, sorted(set(problems))


def dotnet():
    exe = os.path.expanduser("~/.dotnet/dotnet")
    return exe if os.path.exists(exe) else "dotnet"


def env():
    e = dict(os.environ)
    e.setdefault("DOTNET_ROOT", os.path.expanduser("~/.dotnet"))
    e["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1"
    e["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    e["DOTNET_NOLOGO"] = "1"
    e["MSBUILDDISABLENODEREUSE"] = "1"
    e["ATLAS_PROJECT_DIR"] = PROJECT
    e["ATLAS_REPO_DIR"] = REPO
    return e


DIAG = re.compile(r"^(?P<file>.*?)\((?P<line>\d+),(?P<col>\d+)\): (?P<kind>error|warning) (?P<code>\w+): (?P<msg>.*?)( \[.*\])?$")


def run_build(csproj, show_warnings):
    p = subprocess.run([dotnet(), "build", csproj, "-nologo", "-v:q", "-nodeReuse:false", "-m:4",
                        "-clp:NoSummary", "-p:TreatWarningsAsErrors=false"],
                       capture_output=True, text=True, env=env())
    errors, warnings = [], []
    for line in (p.stdout + p.stderr).splitlines():
        line = line.strip()
        m = DIAG.match(line)
        if m:
            rel = os.path.relpath(m.group("file"), REPO) if os.path.isabs(m.group("file")) else m.group("file")
            text = "%s(%s): %s %s: %s" % (rel, m.group("line"), m.group("kind"), m.group("code"), m.group("msg"))
            (errors if m.group("kind") == "error" else warnings).append(text)
        elif " error " in line or line.startswith("error"):
            errors.append(line)
    errors = list(dict.fromkeys(errors))
    warnings = list(dict.fromkeys(warnings))
    if p.returncode != 0 and not errors:
        errors.append("dotnet build failed:\n" + (p.stdout + p.stderr)[-2000:])
    return errors, warnings


def cmd_build(asms, projects, names, configs, show_warnings):
    ok = True
    for cfg in configs:
        targets = [n for n in (names or sorted(asms)) if (cfg, n, "lib") in projects]
        skipped = [n for n in (names or []) if n in asms and (cfg, n, "lib") not in projects]
        # Build leaves only: a project build covers everything it references.
        covered = set()
        for n in targets:
            covered |= set(closure(asms, [n])) - {n}
        roots = [n for n in targets if n not in covered] or targets
        seen_err = []
        nwarn = 0
        for n in roots:
            errors, warnings = run_build(projects[(cfg, n, "lib")], show_warnings)
            seen_err += [e for e in errors if e not in seen_err]
            nwarn += len(warnings)
            if show_warnings:
                for w in warnings:
                    print("    " + w)
        status = "OK" if not seen_err else "FAIL"
        print("[build %-6s] %s  %d assemblies, %d errors, %d warnings%s"
              % (cfg, status, len(set(closure(asms, targets)) & {k[1] for k in projects if k[0] == cfg}),
                 len(seen_err), nwarn, ("  (not in this configuration: %s)" % ", ".join(skipped)) if skipped else ""))
        for e in seen_err[:60]:
            print("    " + e)
        if len(seen_err) > 60:
            print("    ... %d more" % (len(seen_err) - 60))
        ok = ok and not seen_err
    return ok


def cmd_test(asms, projects, names, test_filter, results_dir=None):
    ok = True
    tests = [n for n in (names or sorted(asms)) if (("editor", n, "run") in projects)]
    for n in names or []:
        if n not in tests:
            print("[test] %s is not a test assembly" % n)
            ok = False
    for n in tests:
        errors, _ = run_build(projects[("editor", n, "lib")], False)
        if errors:
            print("[test %s] FAIL: does not compile as Unity compiles it" % n)
            for e in errors[:40]:
                print("    " + e)
            ok = False
            continue
        errors, _ = run_build(projects[("editor", n, "run")], False)
        if errors:
            print("[test %s] FAIL: headless test host does not compile" % n)
            for e in errors[:40]:
                print("    " + e)
            ok = False
            continue
        flt = "TestCategory!=Unity"
        if test_filter:
            flt = "(%s)&(%s)" % (flt, test_filter)
        cmd = [dotnet(), "test", projects[("editor", n, "run")], "-nologo", "--no-build",
               "--filter", flt, "--logger", "console;verbosity=normal"]
        if results_dir:
            cmd += ["--logger", "trx;LogFileName=%s.trx" % n, "--results-directory", results_dir]
        p = subprocess.run(cmd, capture_output=True, text=True, env=env())
        out = p.stdout + p.stderr
        failed = []
        block = None
        for line in out.splitlines():
            s = line.rstrip()
            if re.match(r"^\s+Failed \S", s):
                block = [s.strip()]
                failed.append(block)
            elif block is not None:
                if re.match(r"^\s+(Passed|Skipped|Failed) \S", s) or s.startswith("Test Run") or s.startswith("Total tests"):
                    block = None
                elif s.strip() and len(block) < 14:
                    block.append("    " + s.strip())
        summary = [l.strip() for l in out.splitlines() if re.search(r"(Total tests|Passed:|Failed:|Skipped:|Passed!|Failed!|No test)", l)]
        good = p.returncode == 0
        if "No test matches" in out or "No test is available" in out:
            summary.append("no headless tests in this assembly")
            good = True
        print("[test %s] %s  %s" % (n, "OK" if good else "FAIL", " | ".join(dict.fromkeys(summary))))
        for b in failed[:25]:
            print("    " + "\n    ".join(b))
        if not good and not failed:
            print(out[-3000:])
        ok = ok and good
    return ok


def cmd_wait(asms, projects, names, timeout):
    import time
    deadline = time.time() + timeout
    while True:
        errors = []
        for n in names:
            if ("editor", n, "lib") in projects:
                errors += run_build(projects[("editor", n, "lib")], False)[0]
            else:
                errors.append("%s is not built in the editor configuration" % n)
        if not errors:
            print("[wait] OK  %s compile" % ", ".join(names))
            return True
        if time.time() > deadline:
            print("[wait] TIMEOUT  %s still do not compile:" % ", ".join(names))
            for e in list(dict.fromkeys(errors))[:40]:
                print("    " + e)
            return False
        time.sleep(20)
        asms2 = load()
        projects.clear()
        projects.update(generate(asms2, WAIT_OUT[0])[0])


WAIT_OUT = [""]


def allowed_for(name):
    if name in ALLOWED:
        return ALLOWED[name]
    if name.startswith("Atlas.Spaces."):
        return ALLOWED[SPACES]
    if name.startswith("Atlas.Blades."):
        return ALLOWED[BLADES]
    return None  # Editor, tests: unrestricted


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//.*", "", text)


def cmd_rules(asms):
    problems = []
    all_dirs = [a.dir for a in asms.values()]
    for a in asms.values():
        allowed = allowed_for(a.name)
        if allowed is None or a.is_test:
            continue
        for r in a.references:
            if not r.startswith("Atlas."):
                continue
            if r not in asms:
                problems.append("F1: %s references %s, which does not exist" % (a.name, r))
            elif r not in allowed and not (SPACES in allowed and r.startswith("Atlas.Spaces.")):
                problems.append("F1: %s may not reference %s" % (a.name, r))
    for name in asms:
        if name in closure(asms, [r for r in asms[name].references if r in asms]):
            problems.append("F1: %s is part of a reference cycle" % name)
    for a in asms.values():
        for src in a.sources(all_dirs):
            with open(src, encoding="utf-8-sig", errors="replace") as f:
                text = strip_comments(f.read())
            rel = os.path.relpath(src, REPO)
            if not a.is_test and a.name != "Atlas.Editor" and "MaterialPropertyBlock" in text:
                problems.append("F7: MaterialPropertyBlock used in %s" % rel)
            if a.name == "Atlas.Safety" and re.search(r"\bAtlas\.(Link|Telemetry|Hands)\b", text):
                problems.append("Rule 1: Atlas.Safety source uses Atlas.Link, Atlas.Telemetry or Atlas.Hands: %s" % rel)
            if a.name == "Atlas.Link" and re.search(r"\bUnity(Engine|Editor)?\b\s*[.;]", text):
                problems.append("F1: Atlas.Link must stay engine-free: %s" % rel)
            if not a.is_test and re.search(r"\bNotImplementedException\b", text):
                problems.append("Unfinished: NotImplementedException in %s" % rel)
            if not a.is_test and a.name != "Atlas.Editor" and re.search(
                    r"\bFindObjects?(OfType|ByType)\b|\bFindFirstObjectByType\b|\bFindAnyObjectByType\b|\bGameObject\.Find\b", text):
                problems.append("F3: scene search in %s (use AtlasServices)" % rel)
    safety = asms.get("Atlas.Safety")
    if safety:
        for r in safety.references:
            if r in ("Atlas.Link", "Atlas.Telemetry", "Atlas.Hands"):
                problems.append("Rule 1: Atlas.Safety references %s" % r)
    link = asms.get("Atlas.Link")
    if link and not link.no_engine:
        problems.append("F1: Atlas.Link must set noEngineReferences")
    print("[rules] %s  %d assemblies, %d problems" % ("OK" if not problems else "FAIL", len(asms), len(problems)))
    for p in sorted(set(problems)):
        print("    " + p)
    return not problems


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", choices=["build", "test", "rules", "all", "wait"])
    ap.add_argument("assemblies", nargs="*")
    ap.add_argument("--config", default="all", choices=["ios", "win", "editor", "all"])
    ap.add_argument("--tag", default="default")
    ap.add_argument("--filter", default="")
    ap.add_argument("--warn", action="store_true", help="print compiler warnings")
    ap.add_argument("--timeout", type=int, default=1500)
    ap.add_argument("--results", default="", help="folder for one .trx per test assembly (test, all)")
    args = ap.parse_args()

    if not os.path.isdir(os.path.join(MANAGED, "UnityEngine")):
        sys.exit("Unity managed assemblies not found at %s (set ATLAS_UNITY_MANAGED)" % MANAGED)
    if not os.path.isdir(SCRIPT_ASM):
        sys.exit("ATLAS/Library/ScriptAssemblies not found: open the project in Unity once")

    asms = load()
    unknown = [n for n in args.assemblies if n not in asms]
    if unknown and args.command != "wait":
        sys.exit("unknown assemblies: %s\nknown: %s" % (", ".join(unknown), ", ".join(sorted(asms))))
    tag = re.sub(r"[^A-Za-z0-9_.-]", "_", args.tag)
    out_root = os.path.join(os.environ.get("ATLAS_HEADLESS_OUT", os.path.expanduser("~/.cache/atlas-headless")), tag)
    projects, problems = generate(asms, out_root)
    for p in problems:
        print("[generate] " + p)
    if args.command == "wait":
        WAIT_OUT[0] = out_root
        sys.exit(0 if cmd_wait(asms, projects, args.assemblies, args.timeout) else 1)
    configs = list(CONFIGS) if args.config == "all" else [args.config]

    ok = not problems
    if args.command in ("rules", "all"):
        ok = cmd_rules(asms) and ok
    if args.command in ("build", "all"):
        ok = cmd_build(asms, projects, args.assemblies if args.command == "build" else [], configs, args.warn) and ok
    if args.command in ("test", "all"):
        results = os.path.abspath(args.results) if args.results else None
        ok = cmd_test(asms, projects, args.assemblies if args.command == "test" else [], args.filter, results) and ok
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
