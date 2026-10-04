using System;
using System.IO;
using NUnit.Framework;

namespace Atlas.Telemetry.Tests
{
    public sealed class NetworkMapSwitchTests
    {
        static string Fixture(string file)
        {
            string project = Environment.GetEnvironmentVariable("ATLAS_PROJECT_DIR") ?? Directory.GetCurrentDirectory();
            return Path.GetFullPath(Path.Combine(project, "..", "station", "pipelines", "tests", "fixtures", "network", file));
        }

        [Test]
        public void Inactive_ShowsNothing_ActiveShowsTheChosenFile_LoadedOnce()
        {
            var asked = new System.Collections.Generic.List<string>();
            var sw = new NetworkMapSwitch(name => { asked.Add(name); return File.ReadAllText(Fixture(name)); });
            Assert.IsNull(sw.Update(false, 0, -1));
            Assert.AreEqual(0, asked.Count);
            NetworkDisc a = sw.Update(true, 0, -1);
            Assert.IsNotNull(a);
            Assert.AreEqual(7, a.Seed);
            Assert.AreSame(a, sw.Update(true, 0, -1));
            NetworkDisc b = sw.Update(true, 1, -1);
            Assert.AreEqual(83, b.Seed);
            Assert.AreSame(a, sw.Update(true, 0, -1));
            CollectionAssert.AreEqual(new[] { "network_7.json", "network_83.json" }, asked);
            Assert.IsNull(sw.Update(false, 1, -1));
        }

        [Test]
        public void MissingOrBadFile_FallsBackToTheFlatTwin_AndIsNotRetried()
        {
            int reads = 0;
            var sw = new NetworkMapSwitch(name => { reads++; return null; });
            Assert.IsNull(sw.Update(true, 0, -1));
            Assert.IsNull(sw.Update(true, 0, -1));
            Assert.AreEqual(1, reads);
            var bad = new NetworkMapSwitch(name => "{not json");
            Assert.IsNull(bad.Update(true, 1, -1));
        }

        [Test]
        public void Centre_RecentresOnTheNodeAndBackToTheOrigin()
        {
            var sw = new NetworkMapSwitch(name => File.ReadAllText(Fixture(name)));
            NetworkDisc d = sw.Update(true, 0, 3);
            double sx, sy;
            d.NodeToScreen(3, 1000.0, out sx, out sy);
            Assert.AreEqual(500.0, sx, 1e-6);
            Assert.AreEqual(500.0, sy, 1e-6);
            sw.Update(true, 0, -1);
            Assert.AreEqual(0.0, d.Centre.x);
        }
    }
}
