using System;
using System.Collections.Generic;
using Atlas.Blades.Lung.AirwayKit;
using Atlas.Kernel;
using Atlas.Rendering;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.AirwayViews.Tests
{
    public class KitGeometryTests
    {
        const double Tol = KitTestUtil.FloatTol;

        // Criterion: P2-B02 "no edge longer than 0.25 m" for kerbs, membranes, iris (floor exempt, MP 8.4).
        [Test]
        public void Kit_EdgeLength_KerbMembraneIris()
        {
            var b = KitTestUtil.NewBuilder();
            double mx = 0;
            {
                foreach (var (kappa, scale) in KitTestUtil.Geoms())
                    foreach (var kind in KitTestUtil.Kinds)
                    {
                        var lay = b.BuildTile(kind, KitTestUtil.Spec(kappa, scale, TileLod.Near));
                        var v = KitTestUtil.Verts(b, lay.VertexCount); var idx = KitTestUtil.Idx(b, lay.IndexCount);
                        foreach (var r in new[] { lay.KerbIndices, lay.MembraneIndices, lay.StripIndices, lay.GlyphIndices })
                            mx = Math.Max(mx, KitTestUtil.MaxEdge(v, idx, r));
                        int mask = KitGeometry.KitMask(kind);
                        for (int sub = 0; sub < 16; sub++)
                        {
                            if ((sub & ~mask) != 0) continue;
                            foreach (bool opaque in new[] { true, false })
                            {
                                var sl = b.BuildValveShell(kind, sub, opaque, kappa, scale);
                                var sv = KitTestUtil.Verts(b, sl.VertexCount); var si = KitTestUtil.Idx(b, sl.IndexCount);
                                foreach (var r in new[] { sl.ShellIndices, sl.IrisIndices, sl.RoofIndices, sl.FloorIndices, sl.GlyphIndices })
                                    mx = Math.Max(mx, KitTestUtil.MaxEdge(sv, si, r));
                            }
                        }
                    }
                TestContext.WriteLine($"[P2-B02] max kit edge = {mx:F4} m (limit {KitGeometry.MaxKitEdge})");
                Assert.LessOrEqual(mx, KitGeometry.MaxKitEdge);
            }
        }

        // Criterion: MP 10.2.3 Chamber and Corridor rows (0.8 m corridor between 0.4 m kerbs, chamber disc 0.5 m, strip 0.2 m, glyph 0.15 m).
        [Test]
        public void Kit_CorridorGeometry()
        {
            var b = KitTestUtil.NewBuilder();
            {
                foreach (var (kappa, scale) in new[] { (-1, KitTestUtil.LFromS(2.0)), (0, 1.5) })
                    foreach (var kind in KitTestUtil.Kinds)
                    {
                        var lay = b.BuildTile(kind, KitTestUtil.Spec(kappa, scale, TileLod.Near));
                        var v = KitTestUtil.Verts(b, lay.VertexCount); var idx = KitTestUtil.Idx(b, lay.IndexCount);
                        var arms = KitTestUtil.OpenArms(kind);
                        double H = KitGeometry.InRadiusMetres(kappa, scale), w = KitGeometry.CorridorHalfWidth;
                        string ctx = $"kappa {kappa} {kind}";

                        // (a) and (b): wall vertices
                        var kerb = KitTestUtil.Used(idx, lay.KerbIndices); var mem = KitTestUtil.Used(idx, lay.MembraneIndices);
                        var wall = new HashSet<int>(kerb); wall.UnionWith(mem);
                        foreach (int i in wall)
                        {
                            double2 c = KitTestUtil.Chart(v[i]);
                            bool line = false;
                            foreach (int e in arms)
                            {
                                KitGeometry.ChartToFermi(e, c, kappa, scale, out double t, out double u);
                                if (Math.Abs(Math.Abs(u) - w) <= Tol && t >= -Tol && t <= H + Tol) line = true;
                                Assert.IsFalse(t >= 0 && t <= H && Math.Abs(u) < w - 4e-6, $"{ctx}: wall vertex inside arm {e}");
                            }
                            double rad = math.length(c);
                            bool arc = Math.Abs(rad - KitGeometry.ChamberRadius) <= Tol;
                            Assert.IsTrue(line || arc, $"{ctx}: wall vertex {c} neither on a kerb line nor on the chamber arc");
                            Assert.GreaterOrEqual(rad, KitGeometry.ChamberRadius - 4e-6, ctx + " radius");
                        }

                        // (c) joins in the double path
                        var runs = new WallRun[8];
                        int n = KitGeometry.WallPath(kind, kappa, scale, runs);
                        Assert.AreEqual(kind == KitKind.Fork ? 7 : kind == KitKind.Straight ? 6 : kind == KitKind.End ? 3 : 5, n, ctx + " run count");
                        int mask = KitGeometry.KitMask(kind);
                        WallRun Find(WallRunType ty, int e, int side)
                        {
                            for (int i = 0; i < n; i++) if (runs[i].Type == ty && runs[i].Edge == e && (ty == WallRunType.Arc || runs[i].Side == side)) return runs[i];
                            throw new Exception("run not found");
                        }
                        foreach (int e in arms)
                        {
                            var lp = Find(WallRunType.Line, e, +1);
                            double2 p0 = KitGeometry.RunPoint(lp, 0, kappa, scale);
                            int e1 = (e + 1) & 3;
                            if ((mask & (1 << e1)) != 0)
                                Assert.AreEqual(0, math.length(p0 - KitGeometry.RunPoint(Find(WallRunType.Line, e1, -1), 0, kappa, scale)), 1e-9, ctx + " corner join");
                            else
                            {
                                var arc = Find(WallRunType.Arc, e, 0);
                                Assert.AreEqual(0, math.length(p0 - KitGeometry.RunPoint(arc, 0, kappa, scale)), 1e-9, ctx + " line-arc join");
                                int f = e; do { f = (f + 1) & 3; } while ((mask & (1 << f)) == 0);
                                var next = Find(WallRunType.Line, f, -1);
                                Assert.AreEqual(0, math.length(KitGeometry.RunPoint(arc, 1, kappa, scale) - KitGeometry.RunPoint(next, 0, kappa, scale)), 1e-9, ctx + " arc-line join");
                            }
                            foreach (int side in new[] { 1, -1 })
                            {
                                var r = Find(WallRunType.Line, e, side);
                                KitGeometry.ChartToFermi(e, KitGeometry.RunPoint(r, 1, kappa, scale), kappa, scale, out double t, out _);
                                Assert.AreEqual(H, t, 1e-9, ctx + " line end on tile edge");
                            }
                        }

                        // (d) heights
                        double kmax = 0, mmin = 9, mmax = 0;
                        foreach (int i in kerb) { Assert.That(v[i].Position.y, Is.InRange(-1e-6f, 0.4f + 1e-6f)); kmax = Math.Max(kmax, v[i].Position.y); }
                        foreach (int i in mem) { Assert.That(v[i].Position.y, Is.InRange(0.4f - 1e-6f, 2.6f + 1e-6f)); mmin = Math.Min(mmin, v[i].Position.y); mmax = Math.Max(mmax, v[i].Position.y); }
                        Assert.AreEqual(0.4, kmax, 1e-6); Assert.AreEqual(2.6, mmax, 1e-6); Assert.AreEqual(0.4, mmin, 1e-6);

                        // (e) strip
                        double umax = 0;
                        foreach (int i in KitTestUtil.Used(idx, lay.StripIndices))
                        {
                            bool ok = false;
                            foreach (int e in arms)
                            {
                                KitGeometry.ChartToFermi(e, KitTestUtil.Chart(v[i]), kappa, scale, out double t, out double u);
                                if (Math.Abs(u) <= 0.1 + Tol && t >= KitGeometry.GlyphRadius - Tol && t <= H + Tol) { ok = true; umax = Math.Max(umax, Math.Abs(u)); }
                            }
                            Assert.IsTrue(ok, ctx + " strip vertex out of range");
                            Assert.AreEqual(0.005, v[i].Position.y, 1e-7);
                        }
                        Assert.AreEqual(0.1, umax, Tol);

                        // (f) glyph
                        int rim = 0;
                        foreach (int i in KitTestUtil.Used(idx, lay.GlyphIndices))
                        {
                            double rad = math.length(KitTestUtil.Chart(v[i]));
                            if (rad > 1e-9) { Assert.AreEqual(0.15, rad, Tol); rim++; }
                            Assert.AreEqual(0.005, v[i].Position.y, 1e-7);
                        }
                        Assert.AreEqual(16, rim);
                    }
            }
        }

        // Criterion: MP 10.2.3 "dithered membrane"; MP 8.1 (50% geometric dither in the shared shader).
        [Test]
        public void Kit_Dither()
        {
            var b = KitTestUtil.NewBuilder();
            {
                foreach (var (kappa, scale) in KitTestUtil.Geoms())
                    foreach (var kind in KitTestUtil.Kinds)
                    {
                        var lay = b.BuildTile(kind, KitTestUtil.Spec(kappa, scale, TileLod.Near));
                        var runs = new WallRun[8]; int n = KitGeometry.WallPath(kind, kappa, scale, runs);
                        int total = 0; for (int i = 0; i < n; i++) total += KitGeometry.ColumnCount(runs[i], kappa, scale) * 11;
                        double ratio = (lay.MembraneIndices.length / 12) / (double)total;
                        Assert.That(ratio, Is.InRange(0.45, 0.55), $"kappa {kappa} scale {scale} {kind}");
                    }
            }
        }

#if !ATLAS_HEADLESS
        // Criterion: MP 8.5 "a tile shell is one draw call per eye".
        [Test, Category("Unity")]
        public void Kit_OneDrawPerTile()
        {
            var b = KitTestUtil.NewBuilder();
            {
                foreach (var (kappa, scale) in new[] { (-1, KitTestUtil.LFromS(2.0)), (0, 1.5) })
                {
                    using var cache = new AirwayKitMeshes(b);
                    cache.Rebuild(kappa, scale);
                    foreach (var kind in KitTestUtil.Kinds)
                        foreach (var lod in KitTestUtil.Lods)
                        {
                            var mesh = cache.Get(kind, lod);
                            Assert.AreEqual(1, mesh.subMeshCount); Assert.AreEqual(UnityEngine.Rendering.IndexFormat.UInt16, mesh.indexFormat);
                        }
                    Assert.AreEqual(kappa, cache.Kappa); Assert.AreEqual(scale, cache.Scale);
                    if (kappa == 0) Assert.AreSame(cache.Get(KitKind.Fork, TileLod.Near), cache.Get(KitKind.Fork, TileLod.Far));
                }
            }
        }

#endif

        // Criterion: MP 10.2.3 steps 1, 4, 5 (shell opaque / iris opens / dithered); MP 0.5 rule 3.
        [Test]
        public void Kit_ValveShell()
        {
            var b = KitTestUtil.NewBuilder();
            {
                foreach (var (kappa, scale) in KitTestUtil.Geoms())
                    foreach (var kind in KitTestUtil.Kinds)
                    {
                        int mask = KitGeometry.KitMask(kind);
                        string ctx = $"kappa {kappa} scale {scale} {kind}";
                        // opaque, nothing open
                        var lay = b.BuildValveShell(kind, 0, true, kappa, scale);
                        var v = KitTestUtil.Verts(b, lay.VertexCount); var idx = KitTestUtil.Idx(b, lay.IndexCount);
                        var runs = new WallRun[8]; int n = KitGeometry.WallPath(kind, kappa, scale, runs);
                        double sum = 0;
                        for (int i = 0; i < n; i++) if (runs[i].Type == WallRunType.Arc) sum += runs[i].Phi1 - runs[i].Phi0;
                        foreach (int e in KitTestUtil.OpenArms(kind)) { KitGeometry.IrisSector(kind, e, kappa, scale, out double p0, out double p1); sum += p1 - p0; }
                        Assert.AreEqual(2 * Math.PI, sum, 1e-9, ctx);
                        var wallV = KitTestUtil.Used(idx, lay.ShellIndices); wallV.UnionWith(KitTestUtil.Used(idx, lay.IrisIndices));
                        foreach (int i in wallV) Assert.AreEqual(KitGeometry.ValveShellRadius, math.length(KitTestUtil.Chart(v[i])), Tol, ctx);
                        Assert.Greater(lay.RoofIndices.length, 0); Assert.Greater(lay.FloorIndices.length, 0); Assert.Greater(lay.GlyphIndices.length, 0);
                        Assert.Greater(lay.ShellIndices.length, 0); Assert.Greater(lay.IrisIndices.length, 0);
                        float ymax = 0; foreach (var vv in v) ymax = Math.Max(ymax, vv.Position.y);
                        Assert.AreEqual(2.6, ymax, 1e-6, ctx);

                        // opaque, one exit iris open
                        foreach (int e in KitTestUtil.OpenArms(kind))
                        {
                            lay = b.BuildValveShell(kind, 1 << e, true, kappa, scale);
                            v = KitTestUtil.Verts(b, lay.VertexCount); idx = KitTestUtil.Idx(b, lay.IndexCount);
                            AssertNoIrisIn(v, idx, lay.IrisIndices, kind, e, kappa, scale, ctx);
                            // dithered
                            lay = b.BuildValveShell(kind, 1 << e, false, kappa, scale);
                            v = KitTestUtil.Verts(b, lay.VertexCount); idx = KitTestUtil.Idx(b, lay.IndexCount);
                            Assert.AreEqual(0, lay.ShellIndices.length); Assert.AreEqual(0, lay.RoofIndices.length);
                            Assert.AreEqual(0, lay.FloorIndices.length); Assert.AreEqual(0, lay.GlyphIndices.length);
                            AssertNoIrisIn(v, idx, lay.IrisIndices, kind, e, kappa, scale, ctx + " dithered");
                            int closedCols = 0;
                            foreach (int f in KitTestUtil.OpenArms(kind))
                            {
                                if (f == e) continue;
                                KitGeometry.IrisSector(kind, f, kappa, scale, out double p0, out double p1);
                                closedCols += KitGeometry.IrisColumns(p0, p1, kappa, scale);
                            }
                            if (closedCols > 0)
                            {
                                double ratio = (lay.IrisIndices.length / 12) / (double)(closedCols * 13);
                                Assert.That(ratio, Is.InRange(0.45, 0.55), ctx + " iris dither");
                            }
                            else Assert.AreEqual(0, lay.IrisIndices.length);
                        }
                        int closedBit = 0; while ((mask & (1 << closedBit)) != 0) closedBit++;
                        int outside = 1 << closedBit;
                        Assert.Throws<ArgumentException>(() => b.BuildValveShell(kind, outside, true, kappa, scale), ctx + " mask outside kind");
                    }
            }
        }

        static void AssertNoIrisIn(AtlasVertex[] v, ushort[] idx, UnityEngine.RangeInt r, KitKind kind, int e, int kappa, double scale, string ctx)
        {
            KitGeometry.IrisSector(kind, e, kappa, scale, out double p0, out double p1);
            for (int i = r.start; i < r.start + r.length; i += 3)
            {
                double2 c = (KitTestUtil.Chart(v[idx[i]]) + KitTestUtil.Chart(v[idx[i + 1]]) + KitTestUtil.Chart(v[idx[i + 2]])) / 3;
                double a = Math.Atan2(c.y, c.x);
                double d = a - p0; d -= 2 * Math.PI * Math.Floor(d / (2 * Math.PI));   // 0..2pi from the sector start
                Assert.IsFalse(d > 1e-6 && d < (p1 - p0) - 1e-6, ctx + ": iris triangle inside the open sector");
            }
        }

        // Criterion: MP 8.5 "1 kB of managed allocation per frame": the building of the valve shell, before any upload.
        [Test]
        public void Kit_ValveShell_Alloc()
        {
            var b = KitTestUtil.NewBuilder();
            double L = KitTestUtil.LFromS(2.0);
            b.BuildValveShell(KitKind.Fork, 0b0010, true, -1, L);
            long before = GC.GetAllocatedBytesForCurrentThread();
            b.BuildValveShell(KitKind.Fork, 0b0010, true, -1, L);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            TestContext.WriteLine($"[P2-B02] BuildValveShell managed allocation = {bytes} B");
            Assert.LessOrEqual(bytes, 1024);
        }

#if !ATLAS_HEADLESS
        // The same with the upload to a Mesh.
        [Test, Category("Unity")]
        public void Kit_ValveShell_Alloc_WithUpload()
        {
            var b = KitTestUtil.NewBuilder(); var m = new UnityEngine.Mesh();
            try
            {
                double L = KitTestUtil.LFromS(2.0);
                b.BuildValveShell(KitKind.Fork, 0b0010, true, -1, L, m);
                long before = GC.GetAllocatedBytesForCurrentThread();
                b.BuildValveShell(KitKind.Fork, 0b0010, true, -1, L, m);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                TestContext.WriteLine($"[P2-B02] BuildValveShell with upload, managed allocation = {bytes} B");
                Assert.LessOrEqual(bytes, 1024);
            }
            finally { UnityEngine.Object.DestroyImmediate(m); }
        }
#endif
    }
}
