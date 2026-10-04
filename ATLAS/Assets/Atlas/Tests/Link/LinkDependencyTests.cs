using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Compilation;

namespace Atlas.Link.Tests
{
    // Master Plan §6.2: dependencies between assemblies point only downwards.
    public class LinkDependencyTests
    {
        // The same sources compile into the Station Hub (.NET 8), so nothing from Unity may leak in.
        [Test]
        public void LinkHasNoEngineOrAtlasReferences()
        {
            var asmdef = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName("Atlas.Link");
            Assert.That(File.ReadAllText(asmdef), Does.Match("\"noEngineReferences\"\\s*:\\s*true"));

            var link = CompilationPipeline.GetAssemblies().Single(a => a.name == "Atlas.Link");
            Assert.That(link.assemblyReferences.Select(r => r.name), Has.None.StartsWith("Atlas."));
        }
    }
}
