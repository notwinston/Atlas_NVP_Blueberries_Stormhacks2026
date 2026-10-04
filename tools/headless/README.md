# Headless verification

`verify.py` compiles every Atlas assembly and runs the engine-free unit tests with no Unity Editor. It exists
because only one Mac can open the Editor for a device build, while most of the code can be proved without it.

```
python3 tools/headless/verify.py all          # rules, build in three configurations, run the tests
python3 tools/headless/verify.py build Atlas.Scan --config ios
python3 tools/headless/verify.py test Atlas.Scan.Tests --filter "FullyQualifiedName~FloorScan"
python3 tools/headless/verify.py rules
python3 tools/headless/verify.py --help
```

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

1. .NET 8 SDK on `PATH`, or in `~/.dotnet`.
2. The Unity managed assemblies: point `ATLAS_UNITY_MANAGED` at `<Unity>/Unity.app/Contents/Managed` (macOS) or
   `<Unity>/Editor/Data/Managed` (Windows, Linux), and `ATLAS_UNITY_IOS_EXT` at the folder that holds
   `UnityEditor.iOS.Extensions.Xcode.dll`.
3. Open the project in the Editor once so `ATLAS/Library/ScriptAssemblies` exists.

It cannot check shaders, scenes, native iOS code, IL2CPP stripping or anything on a device. Those are checked in
the Editor and on the phone.
