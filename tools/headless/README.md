# Headless verification

`verify.py` compiles every Atlas assembly and runs the engine-free unit tests with no Unity Editor. It exists
because only one Mac can open the Editor for a device build, while most of the code can be proved without it.

```
python3 tools/headless/verify.py all          # rules, build in three configurations, run the tests
python3 tools/headless/verify.py build Atlas.Scan --config ios
python3 tools/headless/verify.py test Atlas.Scan.Tests --filter "FullyQualifiedName~FloorScan"
python3 tools/headless/verify.py rules
python3 tools/headless/verify.py all --results out/trx    # also one .trx per test assembly: every test by name
python3 tools/headless/verify.py wait Atlas.Telemetry --timeout 1200
python3 tools/headless/verify.py --help
```

| Option | Meaning |
|---|---|
| `--config ios\|win\|editor\|all` | Which build configuration `build` compiles: the phone, the Station, the Editor. Default `all` |
| `--tag <name>` | A private output folder (`~/.cache/atlas-headless/<name>/`), so several people or agents can run at once without overwriting each other's projects. Pass one whenever others work in the same tree |
| `--filter <expr>` | `dotnet test` filter for `test`, for example `FullyQualifiedName~PerfTour` |
| `--warn` | Print compiler warnings |
| `--results <dir>` | Write one `.trx` per test assembly (`test`, `all`) |
| `wait <Assembly ...> [--timeout s]` | Blocks until the named assemblies compile in the Editor configuration (somebody else is still writing them), polling every 20 s; exits 1 after the timeout (default 1500 s) and prints the remaining errors |

`rules` checks the assembly reference table of `docs/contracts/README.md` (F1; the `ALLOWED` dictionary of `verify.py`, which
`Tests/Architecture/AssemblyGraphTests.cs` repeats), the file rules (no `MaterialPropertyBlock`, no scene search, no
`NotImplementedException`, `Atlas.Link` engine-free, `Atlas.Safety` free of Link and Telemetry) and reference cycles. Each
`build` line and each `test` line ends `OK` or `FAIL` with the first errors. A test assembly is built twice (as Unity compiles it, then
against NuGet NUnit with `ATLAS_HEADLESS`), so a test that does not compile in the Editor fails here too.

How it works: each `.asmdef` under `ATLAS/Assets/Atlas` becomes a generated C# project (in
`~/.cache/atlas-headless/<tag>/`) that references the real Unity 6000.3.25f1 managed assemblies and the package
assemblies in `ATLAS/Library/ScriptAssemblies`, with the same scripting defines Unity sets for the phone build,
the Station build and the Editor. A wrong Unity or package API is therefore a compile error here too.

Tests are written once and run twice: in the Unity Test Runner, and here on .NET 8 against NuGet NUnit. Tests
that need the engine are wrapped in `#if !ATLAS_HEADLESS` and marked `[Category("Unity")]`; they are compiled
here and run only in Unity. A test finds repository files like this:

```csharp
static string ProjectDir =>
    Environment.GetEnvironmentVariable("ATLAS_PROJECT_DIR") ?? Directory.GetCurrentDirectory();
```

Set-up, once per machine:

1. .NET 8 SDK on `PATH`, or in `~/.dotnet` (set `DOTNET_ROOT` if it lives elsewhere).
2. The Unity managed assemblies: point `ATLAS_UNITY_MANAGED` at the folder that holds `UnityEngine/`: on macOS
   `<Unity>/Unity.app/Contents/Resources/Scripting/Managed` (Unity 6000.3), on Windows and Linux `<Unity>/Editor/Data/Managed`.
   Point `ATLAS_UNITY_IOS_EXT` at the folder that holds `UnityEditor.iOS.Extensions.Xcode.dll` (macOS:
   `<Unity>/PlaybackEngines/iOSSupport`). The defaults are `~/unity-managed/Editor/Data/Managed` and `~/unity-managed/iOSSupport`.
3. Open the project in the Editor once so `ATLAS/Library/ScriptAssemblies` exists.

It cannot check shaders, scenes, native iOS code, IL2CPP stripping or anything on a device. Those are checked in
the Editor and on the phone.
