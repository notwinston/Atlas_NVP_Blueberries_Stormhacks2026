using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Compilation;

namespace Atlas.Safety.Tests
{
    // Master Plan §6.2: dependencies between assemblies point only downwards.
    public class SafetyDependencyTests
    {
        // Rule 1: safety runs on the phone and never touches the network.
        [Test]
        public void SafetyNeverReachesLinkOrTelemetry()
        {
            var safety = CompilationPipeline.GetAssemblies().Single(a => a.name == "Atlas.Safety");
            var reachable = new HashSet<string>();
            var pending = new Stack<Assembly>(safety.assemblyReferences);
            while (pending.Count > 0)
            {
                var next = pending.Pop();
                if (reachable.Add(next.name))
                    foreach (var reference in next.assemblyReferences)
                        pending.Push(reference);
            }

            Assert.That(reachable, Has.None.EqualTo("Atlas.Link").And.None.EqualTo("Atlas.Telemetry"));
        }
    }
}
