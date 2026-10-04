using System;
using System.Diagnostics;
using System.IO;
using Atlas.Blades.Network.Core;
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
#if !ATLAS_HEADLESS
using UnityEngine;
#endif

namespace Atlas.Blades.Network.Tests
{
    // NET-06: the V2 floor map. The geometry and its budgets run on the engine-free mesher and focus; what needs the engine
    // (renderers, materials, the pool) is at the end. Device draw counts and timings are deferred to the phone.
    public class NetworkFloorTests
    {
        const float Dt = 1f / 60f;

        static NetworkFloorMesher Mesher(int seed = 7) { return new NetworkFloorMesher(NetworkTestEnv.Model(seed)); }

        static void Run(NetworkFloorMesher m, NetworkFocus f)
        {
            int guard = 0;
            while (f.Active && guard++ < 1000) m.Step(f, Dt);
        }

        [Test]
        public void ThreeMeshes_ThreeDrawsPerEye()
        {
            NetworkFloorMesher m = Mesher();
            var builders = new[] { m.Nodes, m.Edges, m.Highlight };
            Assert.AreNotSame(builders[0], builders[1]);
            Assert.AreNotSame(builders[1], builders[2]);
            Assert.AreNotSame(builders[0], builders[2]);
            Assert.Greater(m.Nodes.TriangleCount, 0);
            Assert.Greater(m.Edges.TriangleCount, 0);
            // One merged mesh each, one material each: three draws per eye. The highlight is empty until a route is lit.
            m.SetHighlight(new[] { 0, 1 }, 2);
            m.Step(new NetworkFocus(m.Model), Dt);
            Assert.Greater(m.Highlight.TriangleCount, 0);
#if !ATLAS_HEADLESS
            MaterialPool pool = MaterialPool.CreateDefault(8);
            NetworkFloorView view = NetworkFloorView.Create(NetworkTestEnv.Model(7), pool);
            try
            {
                Assert.AreEqual(3, view.Renderers.Length);
                var materials = new System.Collections.Generic.HashSet<Material>(view.Materials);
                Assert.AreEqual(3, materials.Count, "one material each");
                foreach (Material mat in view.Materials) Assert.IsTrue(pool.Owns(mat));
                Assert.AreEqual(3, view.GetComponentsInChildren<MeshRenderer>(true).Length);
                Assert.IsFalse(view.Visible, "built hidden");
            }
            finally { UnityEngine.Object.DestroyImmediate(view.gameObject); }
#endif
        }

        [Test]
        public void Triangles_AtMost25k()
        {
            NetworkFloorMesher m = Mesher();
            NetworkModel model = m.Model;
            Assert.AreEqual(10 * model.NodeCount, m.Nodes.TriangleCount, "a post is a five-face box");
            // The longest route the router can produce (both rules, sampled pairs of the giant component) is lit.
            var router = new GreedyRouter(model);
            var path = new int[GreedyRouter.Limit];
            var longest = new int[GreedyRouter.Limit];
            int longestCount = 0;
            for (int a = 0; a < router.GiantCount; a += 5)
                for (int b = 0; b < router.GiantCount; b += 7)
                    foreach (RoutingRule rule in new[] { RoutingRule.Hyperbolic, RoutingRule.Flat })
                    {
                        int c = router.Route(router.GiantNode(a), router.GiantNode(b), rule, path, out _);
                        if (c > longestCount) { longestCount = c; Array.Copy(path, longest, c); }
                    }
            TestContext.Out.WriteLine("longest sampled greedy route: " + longestCount + " nodes");
            m.SetHighlight(longest, longestCount);
            var focus = new NetworkFocus(model);
            focus.Begin(3);
            m.Step(focus, Dt);
            Assert.LessOrEqual(m.TriangleCount, 25000, "nodes " + m.Nodes.TriangleCount + " edges " + m.Edges.TriangleCount + " highlight " + m.Highlight.TriangleCount);
            Run(m, focus);
            Assert.LessOrEqual(m.TriangleCount, 25000);
            Assert.LessOrEqual(Mesher(83).TriangleCount, 25000);

            // No silent cut and no overrun: a synthetic route of the router's full limit has every hop and post built,
            // and the whole map still fits the budget.
            var full = new int[GreedyRouter.Limit];
            for (int i = 0; i < full.Length; i++) full[i] = i % model.NodeCount;
            m.SetHighlight(full, full.Length);
            m.Step(focus, Dt);
            Assert.AreEqual(full.Length - 1, NetworkFloorMesher.HighlightMaxEdges, "capacity for every edge of the longest route");
            int segs = (m.Highlight.TriangleCount - full.Length * 10) / 2 / (full.Length - 1);
            Assert.GreaterOrEqual(segs, 1, "every edge of a full route is built");
            Assert.AreEqual((full.Length - 1) * segs * 2 + full.Length * 10, m.Highlight.TriangleCount, "equal segments on every hop");
            Assert.LessOrEqual(m.TriangleCount, 25000, "full 200-node route: nodes " + m.Nodes.TriangleCount + " edges " + m.Edges.TriangleCount + " highlight " + m.Highlight.TriangleCount);
            Run(m, focus);
            Assert.LessOrEqual(m.TriangleCount, 25000, "full route while the focus animates");
        }

        [Test]
        public void AtRest_ZeroManagedAllocationPerFrame()
        {
            NetworkFloorMesher m = Mesher();
            var focus = new NetworkFocus(m.Model);
            focus.Begin(5);
            Run(m, focus);
            for (int i = 0; i < 20; i++) m.Step(focus, Dt);   // warm
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 600; i++) m.Step(focus, Dt);
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(0, after - before, "bytes allocated over 600 frames at rest");
        }

        [Test]
        public void Recentre_AtMost2msCpuPerFrame()
        {
            // The same deterministic sequence of re-centres is timed in several passes; a frame's cost is its minimum over
            // the passes, so a scheduler preemption or a collection landing in one pass does not count as the code's cost.
            // The bound stays 2.0 ms on the worst such frame.
            const int Passes = 5, First = 10, Last = 20;
            NetworkFloorMesher m = Mesher();
            var focus = new NetworkFocus(m.Model);
            for (int w = 0; w < 3; w++)                        // warm: JIT, caches, tiered code
            {
                focus.Reset();
                for (int target = First; target < Last; target++) { focus.Begin(target); Run(m, focus); }
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var best = new System.Collections.Generic.List<double>();
            long allocated = 0;
            for (int pass = 0; pass < Passes; pass++)
            {
                focus.Reset();
                focus.Begin(7);
                Run(m, focus);
                int index = 0;
                for (int target = First; target < Last; target++)
                {
                    Assert.IsTrue(focus.Begin(target));
                    int frames = 0;
                    while (focus.Active)
                    {
                        long a0 = GC.GetAllocatedBytesForCurrentThread();
                        long t0 = Stopwatch.GetTimestamp();
                        m.Step(focus, Dt);
                        long t1 = Stopwatch.GetTimestamp();
                        allocated += GC.GetAllocatedBytesForCurrentThread() - a0;
                        double ms = (t1 - t0) * 1000.0 / Stopwatch.Frequency;
                        if (pass == 0) best.Add(ms); else if (ms < best[index]) best[index] = ms;
                        index++;
                        frames++;
                    }
                    Assert.AreEqual((int)Math.Ceiling(focus.RecentreSeconds / Dt - 1e-4), frames, 1, "0.6 s of frames");
                }
            }
            Assert.AreEqual(0, allocated, "a re-centre allocates nothing");
            double worst = 0;
            foreach (double ms in best) if (ms > worst) worst = ms;
            var sorted = new System.Collections.Generic.List<double>(best);
            sorted.Sort();
            TestContext.Out.WriteLine("recentre frame ms: median " + sorted[sorted.Count / 2] + " p90 " + sorted[sorted.Count * 9 / 10] + " worst " + worst);
            Assert.LessOrEqual(worst, 2.0, "worst frame, ms (headless proxy; device timing deferred: phone)");
        }

        [Test]
        public void Meshes_RebuiltOnlyDuringRecentre_IntoPreallocatedBuffers()
        {
            NetworkFloorMesher m = Mesher();
            var focus = new NetworkFocus(m.Model);
            AtlasVertex[][] vertices = { m.Nodes.Vertices, m.Edges.Vertices, m.Highlight.Vertices };
            int[][] indices = { m.Nodes.Indices, m.Edges.Indices, m.Highlight.Indices };

            int builds = m.GeometryBuilds;
            for (int i = 0; i < 100; i++) Assert.IsFalse(m.Step(focus, Dt));
            Assert.AreEqual(builds, m.GeometryBuilds, "no rebuild at rest");

            Assert.IsTrue(focus.Begin(42));
            int frames = 0;
            while (focus.Active)
            {
                Assert.IsTrue(m.Step(focus, Dt));
                Assert.IsTrue(m.GeometryRebuilt);
                frames++;
            }
            Assert.AreEqual(builds + frames, m.GeometryBuilds, "one build per frame of the re-centre");
            Assert.AreEqual(0.6f, focus.RecentreSeconds);
            builds = m.GeometryBuilds;
            for (int i = 0; i < 100; i++) Assert.IsFalse(m.Step(focus, Dt));
            Assert.AreEqual(builds, m.GeometryBuilds, "none after it");

            // A lit route rebuilds the highlight mesh alone.
            int hb = m.HighlightBuilds;
            m.SetHighlight(new[] { 1, 2, 3 }, 3);
            Assert.IsTrue(m.Step(focus, Dt));
            Assert.IsFalse(m.GeometryRebuilt);
            Assert.AreEqual(builds, m.GeometryBuilds);
            Assert.AreEqual(hb + 1, m.HighlightBuilds);

            Assert.AreSame(vertices[0], m.Nodes.Vertices);
            Assert.AreSame(vertices[1], m.Edges.Vertices);
            Assert.AreSame(vertices[2], m.Highlight.Vertices);
            Assert.AreSame(indices[0], m.Nodes.Indices);
            Assert.AreSame(indices[1], m.Edges.Indices);
            Assert.AreSame(indices[2], m.Highlight.Indices);
        }

        [Test]
        public void Recentre_PutsTheNodeAtTheMiddle()
        {
            NetworkFloorMesher m = Mesher();
            var focus = new NetworkFocus(m.Model);
            focus.Begin(17);
            Run(m, focus);
            float3 p = m.NodePosition(17);
            Assert.Less(math.length(new float2(p.x, p.z)), 1e-5f);
            Assert.AreEqual(17, focus.FocusNode);
        }

        [Test]
        public void UsesOnlyAtlasLook()
        {
            string dir = Path.Combine(NetworkTestEnv.ProjectDir, "Assets", "Atlas", "Blades", "Network");
            foreach (string name in new[] { "NetworkFloorMesher.cs", "NetworkFocus.cs", "NetworkFloorView.cs", "NetworkInstaller.cs" })
            {
                string text = StripComments(File.ReadAllText(Path.Combine(dir, name)));
                Assert.IsFalse(text.Contains("MaterialPropertyBlock"), name);
                Assert.IsFalse(text.Contains("new Material("), name);
                Assert.IsFalse(text.Contains("Shader.Find"), name);
            }
            string mesher = File.ReadAllText(Path.Combine(dir, "NetworkFloorMesher.cs"));
            string view = File.ReadAllText(Path.Combine(dir, "NetworkFloorView.cs"));
            Assert.IsTrue(mesher.Contains("AtlasMeshBuilder") && mesher.Contains("AtlasPalette"));
            Assert.IsTrue(view.Contains("MaterialPool") && view.Contains("AtlasMesh"));
        }

        static string StripComments(string text)
        {
            text = System.Text.RegularExpressions.Regex.Replace(text, @"/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline);
            return System.Text.RegularExpressions.Regex.Replace(text, @"//.*", "");
        }

        [Test]
        public void Disc_InsideFreeRectangle_AllRoomFixtures()
        {
            NetworkFloorMesher m = Mesher();
            var focus = new NetworkFocus(m.Model);
            focus.Begin(30);
            m.Step(focus, 0.2f);                                // a mid-glide picture as well as the rest picture
            m.SetHighlight(new[] { 1, 2, 3, 4 }, 4);
            m.Step(focus, Dt);
            foreach (string name in RoomFixtures.All)
            {
                RoomModel room = RoomFixtures.LoadFromProject(name);
                FreeRect free = room.FreeRect;
                float r = NetworkFloorMesher.DiscRadius(free);
                Assert.LessOrEqual(r, 1.2f + 1e-6f, name);
                Assert.LessOrEqual(r, 0.5f * math.min(free.Wf, free.Df) + 1e-6f, name);
                if (0.5f * math.min(free.Wf, free.Df) >= 1.2f) Assert.AreEqual(1.2f, r, 1e-6f, name + ": 2.4 m across when the rectangle allows");
                float s = r / NetworkFloorMesher.DiscRadiusMetres;
                foreach (AtlasMeshBuilder b in new[] { m.Nodes, m.Edges, m.Highlight })
                    for (int i = 0; i < b.VertexCount; i++)
                    {
                        float3 v = b.Vertices[i].Position;
                        Assert.LessOrEqual(math.abs(v.x * s), 0.5f * free.Wf + 1e-5f, name);
                        Assert.LessOrEqual(math.abs(v.z * s), 0.5f * free.Df + 1e-5f, name);
                        Assert.LessOrEqual(math.length(new float2(v.x, v.z)) * s, r + 1e-5f, name + ": inside the disc");
                    }
            }
        }
    }
}
