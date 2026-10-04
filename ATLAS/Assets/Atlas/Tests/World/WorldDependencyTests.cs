using System.Linq;
using NUnit.Framework;
using UnityEditor.Compilation;

namespace Atlas.World.Tests
{
    // Master Plan §6.2: dependencies between assemblies point only downwards.
    public class WorldDependencyTests
    {
        static string[] AtlasReferences(string assembly) =>
            CompilationPipeline.GetAssemblies().Single(a => a.name == assembly)
                .assemblyReferences.Select(r => r.name).Where(n => n.StartsWith("Atlas.")).ToArray();

        [Test]
        public void WorldReferencesOnlyKernel() =>
            Assert.That(AtlasReferences("Atlas.World"), Is.SubsetOf(new[] { "Atlas.Kernel" }));
    }
}
