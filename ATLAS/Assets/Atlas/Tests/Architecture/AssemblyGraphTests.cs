using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Atlas.Architecture.Tests
{
    /// <summary>
    /// The assembly table of docs/contracts/README.md (F1) and the source rules that go with it.
    /// Reads the .asmdef and .cs files from disk, so it runs in the Unity Test Runner and headless alike.
    /// </summary>
    public class AssemblyGraphTests
    {
        const string Spaces = "Atlas.Spaces.*";
        const string Blades = "Atlas.Blades.*";

        // Which Atlas assemblies each assembly may reference. The Editor assembly and tests are unrestricted.
        static readonly Dictionary<string, string[]> Allowed = new Dictionary<string, string[]>
        {
            { "Atlas.Kernel", new string[0] },
            { "Atlas.Link", new string[0] },
            { "Atlas.World", new[] { "Atlas.Kernel", "Atlas.Link" } },
            { "Atlas.Rendering", new[] { "Atlas.Kernel", "Atlas.World" } },
            { "Atlas.Scan", new[] { "Atlas.Kernel", "Atlas.World" } },
            { "Atlas.Scan.ARKit", new[] { "Atlas.Kernel", "Atlas.World", "Atlas.Scan" } },
            { "Atlas.Safety", new[] { "Atlas.World" } },
            { "Atlas.Input", new string[0] },
            { "Atlas.Telemetry", new[] { "Atlas.Kernel", "Atlas.Link", "Atlas.World" } },
            { "Atlas.App", new[] { "Atlas.Kernel", "Atlas.Link", "Atlas.World", "Atlas.Rendering", "Atlas.Scan", "Atlas.Safety", "Atlas.Input", "Atlas.Telemetry" } },
            { Spaces, new[] { "Atlas.Kernel", "Atlas.Link", "Atlas.World", "Atlas.Rendering", "Atlas.Input", "Atlas.Telemetry" } },
            { "Atlas.Spectator", new[] { "Atlas.Kernel", "Atlas.Link", "Atlas.World", "Atlas.Rendering", "Atlas.Telemetry", Spaces } },
            { "Atlas.World.Lint.Editor", new[] { "Atlas.Kernel", "Atlas.World", "Atlas.Rendering" } },
            { Blades, new[] { "Atlas.Kernel", "Atlas.Link", "Atlas.World", "Atlas.Rendering", "Atlas.Scan", "Atlas.Safety", "Atlas.Input", "Atlas.Telemetry", Spaces } },
        };

        sealed class Asm
        {
            public string Name, Dir, Text;
            public string[] References;
            public bool IsTest;
        }

        static string AtlasDir
        {
            get
            {
                var project = Environment.GetEnvironmentVariable("ATLAS_PROJECT_DIR");
                if (string.IsNullOrEmpty(project))
                    project = Directory.GetCurrentDirectory();
                return Path.Combine(project, "Assets", "Atlas");
            }
        }

        static List<Asm> Load()
        {
            var result = new List<Asm>();
            foreach (var path in Directory.GetFiles(AtlasDir, "*.asmdef", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(path);
                var refs = Regex.Match(text, "\"references\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
                result.Add(new Asm
                {
                    Name = Regex.Match(text, "\"name\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value,
                    Dir = Path.GetDirectoryName(path),
                    Text = text,
                    References = Regex.Matches(refs.Groups[1].Value, "\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToArray(),
                    IsTest = text.Contains("UNITY_INCLUDE_TESTS"),
                });
            }
            return result;
        }

        static IEnumerable<string> Sources(Asm asm, List<Asm> all)
        {
            var nested = all.Where(a => a != asm && a.Dir.StartsWith(asm.Dir + Path.DirectorySeparatorChar)).Select(a => a.Dir).ToArray();
            return Directory.GetFiles(asm.Dir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !nested.Any(n => f.StartsWith(n + Path.DirectorySeparatorChar)));
        }

        static string Code(string file)
        {
            var text = File.ReadAllText(file);
            text = Regex.Replace(text, "/\\*.*?\\*/", "", RegexOptions.Singleline);
            return Regex.Replace(text, "//.*", "");
        }

        static string[] AllowedFor(string name)
        {
            if (Allowed.TryGetValue(name, out var allowed))
                return allowed;
            if (name.StartsWith("Atlas.Spaces.")) return Allowed[Spaces];
            return name.StartsWith("Atlas.Blades.") ? Allowed[Blades] : null;
        }

        [Test]
        public void AssembliesReferenceOnlyWhatTheTableAllows()
        {
            var all = Load();
            var names = new HashSet<string>(all.Select(a => a.Name));
            var problems = new List<string>();
            foreach (var asm in all.Where(a => !a.IsTest))
            {
                var allowed = AllowedFor(asm.Name);
                if (allowed == null)
                    continue;
                foreach (var reference in asm.References.Where(r => r.StartsWith("Atlas.")))
                {
                    if (!names.Contains(reference))
                        problems.Add(asm.Name + " references " + reference + ", which does not exist");
                    else if (!allowed.Contains(reference) && !(allowed.Contains(Spaces) && reference.StartsWith("Atlas.Spaces.")))
                        problems.Add(asm.Name + " may not reference " + reference);
                }
            }
            Assert.That(problems, Is.Empty);
        }

        [Test]
        public void AssemblyGraphHasNoCycle()
        {
            var all = Load().ToDictionary(a => a.Name);
            foreach (var start in all.Values)
            {
                var seen = new HashSet<string>();
                var pending = new Stack<string>(start.References.Where(all.ContainsKey));
                while (pending.Count > 0)
                {
                    var next = pending.Pop();
                    if (!seen.Add(next))
                        continue;
                    foreach (var reference in all[next].References.Where(all.ContainsKey))
                        pending.Push(reference);
                }
                Assert.That(seen, Has.None.EqualTo(start.Name), start.Name + " is part of a reference cycle");
            }
        }

        // Rule 1 (Master Plan §0.5, §11.1): safety runs on the phone and never touches the network.
        // This is the assembly half; the pose-jump half lives with the Bump Guard tests.
        [Test]
        public void Safety_Independence_AssemblyHalf()
        {
            var all = Load();
            var safety = all.Single(a => a.Name == "Atlas.Safety");
            Assert.That(safety.References, Has.None.EqualTo("Atlas.Link").And.None.EqualTo("Atlas.Telemetry"));
            var offenders = Sources(safety, all).Where(f => Regex.IsMatch(Code(f), "\\bAtlas\\.(Link|Telemetry)\\b")).Select(Path.GetFileName);
            Assert.That(offenders, Is.Empty, "Atlas.Safety sources use a type from Atlas.Link or Atlas.Telemetry");
        }

        // The Hub compiles the same sources, so the link library may not see the engine.
        [Test]
        public void LinkIsEngineFree()
        {
            var all = Load();
            var link = all.Single(a => a.Name == "Atlas.Link");
            Assert.That(link.Text, Does.Match("\"noEngineReferences\"\\s*:\\s*true"));
            Assert.That(link.References, Has.None.StartsWith("Atlas."));
            var offenders = Sources(link, all).Where(f => Regex.IsMatch(Code(f), "\\bUnity(Engine|Editor)?\\b\\s*[.;]")).Select(Path.GetFileName);
            Assert.That(offenders, Is.Empty, "Atlas.Link sources use a Unity namespace");
        }

        // Shader_Contract (§11.1): per-tile and per-room data lives in pooled material instances, never in a property block.
        [Test]
        public void NoMaterialPropertyBlockAnywhere()
        {
            var all = Load();
            var offenders = all.Where(a => !a.IsTest && a.Name != "Atlas.Editor")
                .SelectMany(a => Sources(a, all))
                .Where(f => Code(f).Contains("MaterialPropertyBlock"))
                .Select(Path.GetFileName);
            Assert.That(offenders, Is.Empty);
        }
    }
}
