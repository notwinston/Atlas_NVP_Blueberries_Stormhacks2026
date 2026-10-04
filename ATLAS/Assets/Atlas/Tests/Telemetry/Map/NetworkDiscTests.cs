using System;
using System.Diagnostics;
using System.IO;
using Atlas.Link;
using NUnit.Framework;

namespace Atlas.Telemetry.Tests
{
    public sealed class NetworkDiscTests
    {
        static string Fixture(string file)
        {
            string project = Environment.GetEnvironmentVariable("ATLAS_PROJECT_DIR") ?? Directory.GetCurrentDirectory();
            return Path.GetFullPath(Path.Combine(project, "..", "station", "pipelines", "tests", "fixtures", "network", file));
        }

        static NetworkDisc Load7() { return NetworkDisc.Parse(File.ReadAllText(Fixture("network_7.json"))); }

        [Test]
        public void Project_EqualsGoldenPoincare_Within0_5Px()
        {
            NetworkDisc disc = Load7();
            JsonValue golden = AtlasJson.Parse(File.ReadAllText(Fixture("golden_poincare_7.json")));
            JsonValue points = golden["points"];
            Assert.AreEqual(disc.NodeCount, points.Count);
            double worst = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                double gx, gy, px, py;
                NetworkDisc.ToScreen(points[i][0].AsDouble(), points[i][1].AsDouble(), 1000.0, out gx, out gy);
                disc.NodeToScreen(i, 1000.0, out px, out py);
                worst = Math.Max(worst, Math.Max(Math.Abs(gx - px), Math.Abs(gy - py)));
            }
            Assert.Less(worst, 0.5);
        }

        [Test]
        public void Draw_DrawsEveryEdgeAndNode()
        {
            NetworkDisc disc = Load7();
            var twin = new FlatTwin();
            disc.Draw(twin);
            Assert.AreEqual(200, disc.NodeCount);
            Assert.AreEqual(580, disc.EdgeCount);
            Assert.AreEqual(580, twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Grid));
            Assert.AreEqual(200, twin.CountOf(FlatTwinShape.Dot, FlatTwinInk.Cell));
        }

        [Test]
        public void Draw_AllocatesNothingPerFrame()
        {
            NetworkDisc disc = Load7();
            var twin = new FlatTwin();
            disc.Draw(twin);                       // grows the list
            disc.Draw(twin);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) disc.Draw(twin);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;   // read before the assert boxes anything
            Assert.AreEqual(0L, allocated);
        }

        [Test]
        public void Panel_BuildsAndDrawsTheDisc()
        {
            NetworkDisc disc = Load7();
            var panel = new FlatTwinPanel(null, "Flat Twin", 1f, false, 0.045f);
            panel.ShowNetwork(disc);
            panel.Apply();
            Assert.AreSame(disc, panel.Network);
            Assert.AreEqual(580, panel.Twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Grid));
            Assert.AreEqual(200, panel.Twin.CountOf(FlatTwinShape.Dot, FlatTwinInk.Cell));
        }

#if !ATLAS_HEADLESS
        [Test, Category("Unity")]
        public void PanelView_BuildsAndDrawsTheDisc()
        {
            NetworkDisc disc = Load7();
            var parent = new UnityEngine.GameObject("NetworkDiscPanelTest");
            var view = new NetworkDiscPanel(parent.transform, "Network", 1f, false, 0.045f);
            try
            {
                view.Show(disc);
                view.Apply();
                Assert.AreEqual(580, view.Panel.Twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Grid));
                if (view.Panel.Transform != null) Assert.Greater(view.Panel.TriangleCount, 0);
            }
            finally
            {
                view.Dispose();
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

#endif

        [Test]
        public void Draw_CostProxy()
        {
            NetworkDisc disc = Load7();
            var twin = new FlatTwin();
            for (int i = 0; i < 20; i++) disc.Draw(twin);
            const int frames = 200;
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < frames; i++) disc.Draw(twin);
            watch.Stop();
            double ms = watch.Elapsed.TotalMilliseconds / frames;
            Assert.Less(ms, 0.3, "ms per frame (headless proxy; the Station's frame is deferred)");
        }
    }
}
