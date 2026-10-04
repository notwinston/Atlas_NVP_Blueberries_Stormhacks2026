using System.Linq;
using NUnit.Framework;
using UnityEditor.Compilation;

namespace Atlas.Scan.Tests
{
    // Master Plan §6.2: dependencies between assemblies point only downwards.
    public class ScanDependencyTests
    {
        static string[] AtlasReferences(string assembly) =>
            CompilationPipeline.GetAssemblies().Single(a => a.name == assembly)
                .assemblyReferences.Select(r => r.name).Where(n => n.StartsWith("Atlas.")).ToArray();

        [Test]
        public void ScanReferencesOnlyKernelAndWorld() =>
            Assert.That(AtlasReferences("Atlas.Scan"), Is.SubsetOf(new[] { "Atlas.Kernel", "Atlas.World" }));
    }
}
