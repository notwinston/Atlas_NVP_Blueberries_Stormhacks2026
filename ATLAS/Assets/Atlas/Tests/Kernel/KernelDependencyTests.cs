using System.Linq;
using NUnit.Framework;
using UnityEditor.Compilation;

namespace Atlas.Kernel.Tests
{
    // Master Plan §6.2: dependencies between assemblies point only downwards.
    public class KernelDependencyTests
    {
        static string[] AtlasReferences(string assembly) =>
            CompilationPipeline.GetAssemblies().Single(a => a.name == assembly)
                .assemblyReferences.Select(r => r.name).Where(n => n.StartsWith("Atlas.")).ToArray();

        [Test]
        public void KernelReferencesNoOtherAtlasAssembly() =>
            Assert.That(AtlasReferences("Atlas.Kernel"), Is.Empty);
    }
}
