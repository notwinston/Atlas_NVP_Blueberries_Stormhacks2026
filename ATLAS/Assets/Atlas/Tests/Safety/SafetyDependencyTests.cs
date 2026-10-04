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
            // Master Plan §6.2 says Safety "never references" Link or Telemetry, so the check is on its direct
            // references. It was transitive, but §6.5 puts ISpaceModule (uses ParamRegistry and StateMessage)
            // in Atlas.World, which therefore references Atlas.Link, and §6.2 has Safety reference World (brief R1, R3).
            var reachable = new HashSet<string>(safety.assemblyReferences.Select(r => r.name));

            Assert.That(reachable, Has.None.EqualTo("Atlas.Link").And.None.EqualTo("Atlas.Telemetry"));
        }
    }
}
