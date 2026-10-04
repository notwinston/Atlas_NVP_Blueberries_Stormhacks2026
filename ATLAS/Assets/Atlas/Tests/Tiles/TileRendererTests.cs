// [P1-B06] T4, T6, T7, T8 (brief section 4). All run under -nographics; none needs a GPU. They need the engine.
#if !ATLAS_HEADLESS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using ConstraintIs = NUnit.Framework.Is;
using Debug = UnityEngine.Debug;

namespace Atlas.Tiles.Tests
{
    [Category("Unity")]
    public class TileRendererTests
    {
        GameObject go; TileRenderer r; MaterialPool pool; TileGraph graph;

        [SetUp]
        public void SetUp()
        {
            pool = MaterialPool.CreateDefault(TileRenderer.PoolSize);
            Assert.AreEqual("Atlas/Surface", pool.Shader.name);
            graph = new TileGraph(5);
            AtlasLook.SetFog(7f, 8f);
            go = new GameObject("TileRendererTest") { hideFlags = HideFlags.DontSave };
            r = go.AddComponent<TileRenderer>();
        }

        [TearDown]
        public void TearDown()
        {
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
            Assert.AreEqual(0, pool.Rented, "every tile material went back to the pool");
            pool.Dispose();
            AtlasLook.ResetForSpace();
        }

        static FrameContext Frame(long n, float3 head, quaternion rot) => new FrameContext
        {
            FrameIndex = (int)n, Time = n / 60.0, DeltaTime = 1f / 60,
            RawHeadPosition = head, RawHeadRotation = rot, HeadPosition = head, HeadRotation = rot, Tracking = TrackingState.Normal,
        };

        // T4: row "no allocation on a tile crossing"; MP 11.2 tile-crossing frame <= 2 ms.
        [Test, Timeout(600000)]
        public void TileCrossing_NoAllocation_FrameCost()
        {
            const double L = 1.885;
            r.Configure(graph, L, pool);
            r.CullRho = 3.0;
            var pose = new WalkerPose { Tile = TileId.Zero, Local = Iso2.Boost(new double2(0.40, 0), -1) };
            double2 delta = double2.zero;
            int crossings = 0; bool crossed = false; long frameNo = 0;
            var head = new float3(0, 1.6f, 0); var rot = quaternion.identity;
            var times = new double[4096]; var isCross = new bool[4096]; int nt = 0;
            Action body = () =>
            {
                pose.Local = (pose.Local * Iso2.Boost(delta, -1)).Orthonormalised();
                crossed = graph.Reroot(ref pose);
                if (crossed) crossings++;
                var fc = Frame(++frameNo, head, rot);
                long t0 = Stopwatch.GetTimestamp();
                r.KernelStep(pose, fc);
                r.Upload(fc);
                long t1 = Stopwatch.GetTimestamp();
                if (nt < times.Length) { times[nt] = (t1 - t0) * 1000.0 / Stopwatch.Frequency; isCross[nt++] = crossed; }
            };
            TestDelegate td = () => body();
            double2 dir = new double2(Math.Cos(10 * Math.PI / 180), Math.Sin(10 * Math.PI / 180)) * 0.02;
            body();   // first frame builds the candidate list
            var pose0 = pose;
            // warm-up: 3 forward + 3 back sweeps = 6 crossings (outside the allocation checks)
            for (int k = 0; k < 6; k++) { delta = dir * ((k & 1) == 0 ? 1 : -1); for (int s = 0; s < 11; s++) body(); }
            pose = pose0;
            long gc0 = GC.GetAllocatedBytesForCurrentThread();
            nt = 0; int crossA0 = crossings;
            // phase A: back and forth across edge 0, x from 0.40 to about 0.62 L, 10 deg heading tilt, 0.02 L per step, 20 crossings
            for (int k = 0; k < 20; k++)
            {
                delta = dir * ((k & 1) == 0 ? 1 : -1);
                for (int s = 0; s < 11; s++) Assert.That(td, ConstraintIs.Not.AllocatingGCMemory(), $"phase A sweep {k} step {s}");
            }
            Assert.GreaterOrEqual(crossings - crossA0, 20, "phase A crossings");
            int guard = 0;
            int phaseB = crossings + 6; delta = new double2(0.02, 0);
            while (crossings < phaseB && guard++ < 4000)
                Assert.That(td, ConstraintIs.Not.AllocatingGCMemory(), "phase B frame " + guard);
            long gcDelta = GC.GetAllocatedBytesForCurrentThread() - gc0;
            var ct = new List<double>();
            for (int i = 0; i < nt; i++) if (isCross[i]) ct.Add(times[i]);
            ct.Sort();
            Assert.GreaterOrEqual(ct.Count, 26);
            double med = ct[ct.Count / 2], p95 = ct[(int)(ct.Count * 0.95)], max = ct[ct.Count - 1];
            string msg = $"[P1-B06 T4] crossings {ct.Count}, GC delta {gcDelta} B, crossing-frame KernelStep+Upload median {med:F3} ms p95 {p95:F3} ms max {max:F3} ms";
            Debug.Log(msg); TestContext.Out.WriteLine(msg);
            Assert.LessOrEqual(med, 2.0);
        }

        // T6: row "fog from rho - 1, opaque at rho"; MP 8.5; kernel.md 6.2; curved.cullRho range 2..3.5 (12.4).
        [Test]
        public void Fog_FromRhoMinus1_OpaqueAtRho()
        {
            r.Configure(graph, 1.885, pool);
            var pose = new WalkerPose { Tile = TileId.Zero, Local = Iso2.Identity };
            var head = new float3(1.5f, 1.6f, -2f);
            var fc = Frame(1, head, quaternion.identity);
            r.KernelStep(pose, fc); r.Upload(fc);
            AssertFog(3.77f, 5.655f);
            // _HypL and _HeadPosWS are the look step's (AtlasLook.Upload, from IWalkerKernel.MetresPerUnit and the head), not the renderer's
            AtlasLook.Upload(fc.RawHeadPosition, r.Selection.L, fc.Time, fc.DeltaTime);
            Assert.AreEqual(1.885f, Shader.GetGlobalFloat(AtlasShaderIDs.HypL), 1e-6);
            var hp = Shader.GetGlobalVector(AtlasShaderIDs.HeadPosWS);
            Assert.AreEqual(head.x, hp.x, 1e-6); Assert.AreEqual(head.y, hp.y, 1e-6); Assert.AreEqual(head.z, hp.z, 1e-6);
            AssertFog(3.77f, 5.655f);
            long before = r.Selection.CandidateRebuilds;
            r.CullRho = 2.5;
            fc = Frame(2, head, quaternion.identity);
            r.KernelStep(pose, fc); r.Upload(fc);
            AssertFog(2.8275f, 4.7125f);
            Assert.AreEqual(before + 1, r.Selection.CandidateRebuilds);
            r.CullRho = 5; Assert.AreEqual(3.5, r.CullRho);
            r.CullRho = 1; Assert.AreEqual(2.0, r.CullRho);
            r.enabled = false;
            AssertFog(7f, 8f);
        }

        // The fog as the look holds it and as the shader reads it: _AtlasFogParams = (start, 1 / (end - start)), metres.
        static void AssertFog(float start, float end)
        {
            Assert.AreEqual(start, AtlasLook.State.FogStart, 1e-4);
            Assert.AreEqual(end, AtlasLook.State.FogEnd, 1e-4);
            var p = Shader.GetGlobalVector(AtlasShaderIDs.FogParams);
            Assert.AreEqual(start, p.x, 1e-4); Assert.AreEqual(1f / (end - start), p.y, 1e-3);
        }

        // T7: row "pool of tile objects and material instances; per-tile anchor each frame"; MP 8.3 (no MaterialPropertyBlock).
        [Test]
        public void Pool_MaterialInstances_AnchorEachFrame()
        {
            r.Configure(graph, 1.885, pool);
            Assert.AreEqual(128, r.Slots.Count);
            Assert.AreEqual(128, pool.Rented);
            var seen = new HashSet<Material>();
            for (int i = 0; i < r.Slots.Count; i++)
            {
                var m = r.Slots[i].sharedMaterial;
                Assert.IsTrue(pool.Owns(m), "pooled Atlas/Surface instance " + i); Assert.IsTrue(seen.Add(m), "distinct material instance " + i);
                Assert.AreEqual("Atlas/Surface", m.shader.name);
                Assert.AreEqual(1f, m.GetFloat(AtlasShaderIDs.WarpMode));
                Assert.AreEqual(TileRenderer.EdgeLineStrength, m.GetVector(AtlasShaderIDs.LineParams).z, "tile edges are drawn by the look's lines");
                Assert.AreEqual(1000f, r.Slots[i].localBounds.size.x); Assert.AreEqual(1000f, r.Slots[i].localBounds.size.y); Assert.AreEqual(1000f, r.Slots[i].localBounds.size.z);
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, r.Slots[i].shadowCastingMode);
                Assert.IsFalse(r.Slots[i].receiveShadows);
            }
            var rng = new System.Random(TileTestPoses.Seed);
            var head = new float3(0, 1.6f, 0);
            for (int frame = 1; frame <= 2; frame++)
            {
                var U = TileTestPoses.RandomWalker(rng, 5);
                var fc = Frame(frame, head, TileTestPoses.Rot(rng.NextDouble() * 360, 20));
                r.KernelStep(new WalkerPose { Tile = TileId.Zero, Local = U }, fc);
                r.Upload(fc);
                var sel = r.Selection;
                Assert.Greater(sel.DrawnCount, 0);
                int enabled = 0;
                for (int i = 0; i < r.Slots.Count; i++) if (r.Slots[i].enabled) enabled++;
                Assert.AreEqual(sel.DrawnCount, enabled); Assert.AreEqual(sel.DrawnCount, r.DrawnCount);
                for (int i = 0; i < r.Slots.Count; i++)
                {
                    if (i >= sel.DrawnCount) { Assert.IsFalse(r.Slots[i].enabled); continue; }
                    Assert.IsTrue(r.Slots[i].enabled);
                    var want = WarpAnchor.ToMatrix(sel.Drawn[i].Anchor);
                    var got = r.Slots[i].sharedMaterial.GetMatrix(AtlasShaderIDs.Anchor);
                    for (int a = 0; a < 4; a++) for (int b = 0; b < 4; b++) Assert.AreEqual(BitConverter.SingleToInt32Bits(want[a, b]), BitConverter.SingleToInt32Bits(got[a, b]), $"slot {i} [{a},{b}] frame {frame}");
                    Assert.AreSame(r.Shells.Get(sel.Drawn[i].Lod), r.Slots[i].GetComponent<MeshFilter>().sharedMesh);
                }
            }
            Assert.AreEqual(2, r.Selection.UpdateCount, "the rule ran once per frame for both eyes");

            string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Assets", "Atlas");
            var files = new List<string>(Directory.GetFiles(Path.Combine(root, "Rendering", "Tiles"), "*.cs"));
            files.AddRange(Directory.GetFiles(Path.Combine(root, "Spaces", "CurvedWalk"), "*.cs", SearchOption.AllDirectories));
            string[] banned = { "MaterialPropertyBlock", "beginCameraRendering", "OnWillRenderObject", "OnPreCull", "Shader.Find" };
            foreach (var f in files)
            {
                var text = File.ReadAllText(f);
                foreach (var b in banned)
                {
                    foreach (var line in text.Split('\n'))
                        if (line.Contains(b) && !line.TrimStart().StartsWith("//")) Assert.Fail($"{Path.GetFileName(f)} contains {b}: {line.Trim()}");
                }
            }
        }

        // T8: P1-B05 hand-over (rebuild shells when L changes, in place); MP 8.4 caps; A.6 edge midpoint 0.5306375 L.
        [Test]
        public void ShellCache_RebuildOnL_InPlace()
        {
            foreach (int q in new[] { 5, 6 })
            {
                using var cache = new TileShellCache(q, 1.885);
                foreach (var lod in new[] { TileLod.Near, TileLod.Mid, TileLod.Far })
                    Assert.LessOrEqual(cache.Get(lod).GetIndexCount(0) / 3, TileChart.TriangleCap(lod), $"q={q} {lod}");
            }
            using var c5 = new TileShellCache(5, 1.885);
            Assert.LessOrEqual(c5.Get(TileLod.Near).GetIndexCount(0) / 3, 1200);
            Assert.LessOrEqual(c5.Get(TileLod.Mid).GetIndexCount(0) / 3, 500);
            Assert.LessOrEqual(c5.Get(TileLod.Far).GetIndexCount(0) / 3, 250);
            var n0 = c5.Get(TileLod.Near); var m0 = c5.Get(TileLod.Mid); var f0 = c5.Get(TileLod.Far);
            c5.Rebuild(2.83);
            Assert.AreSame(n0, c5.Get(TileLod.Near)); Assert.AreSame(m0, c5.Get(TileLod.Mid)); Assert.AreSame(f0, c5.Get(TileLod.Far));
            Assert.AreEqual(2.83, c5.L);
            var v = new List<Vector3>(); n0.GetVertices(v);
            double best = double.MaxValue;
            foreach (var p in v) best = Math.Min(best, math.length(new double3(p.x, p.y, p.z) - new double3(2.83 * 0.5306375, 0, 0)));
            Assert.Less(best, 1e-4);
            double l = 2.83;
            TestDelegate td = () => { l = l == 2.83 ? 1.885 : 2.83; c5.Rebuild(l); };
            td(); td();
            Assert.That(td, ConstraintIs.Not.AllocatingGCMemory(), "Rebuild allocates 0 B");
        }
    }
}
#endif
