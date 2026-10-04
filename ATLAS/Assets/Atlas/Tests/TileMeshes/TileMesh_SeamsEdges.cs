using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Atlas.Kernel;
using Atlas.Rendering;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.TileMeshes.Tests
{
    // Master Plan §11.1 TileMesh_SeamsEdges (acceptance tests of P1-B05)
    public class TileMesh_SeamsEdges
    {
        static readonly double[] Ls = { 1.4, 1.9, 3.0 };
        static readonly TileLod[] Lods = { TileLod.Near, TileLod.Mid, TileLod.Far };

        TileMeshGenerator gen;
        readonly List<Mesh> made = new List<Mesh>();

        [SetUp] public void SetUp() { gen = new TileMeshGenerator(TilePalette.Provisional); }

        [TearDown]
        public void TearDown()
        {
            foreach (var m in made) if (m != null) UnityEngine.Object.DestroyImmediate(m);
            made.Clear();
        }

        (Mesh mesh, TileMeshLayout lay, Vector3[] v) Make(TileMeshSpec spec)
        {
            var m = new Mesh();
            made.Add(m);
            var lay = gen.Build(spec, m);
            return (m, lay, m.vertices);
        }

        static double2 Klein(Vector3 p, double L) => TileChart.ChartToKlein(new double2(p.x, p.z), -1, L);

        // Floor boundary vertices of one edge as (along-edge coordinate, vertex), sorted.
        static List<(double t, int i)> EdgeVerts(Vector3[] v, TileMeshLayout lay, int e, double L, double h)
        {
            var list = new List<(double, int)>();
            for (int i = lay.FloorVertices.start; i < lay.FloorVertices.start + lay.FloorVertices.length; i++)
            {
                var r = TileTestGeometry.Rot(Klein(v[i], L), -e);
                if (Math.Abs(r.x - h) < 1e-6) list.Add((r.y, i));
            }
            list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return list;
        }

        // T1: "edges of neighbouring tiles coincide to 1 mm for every pair of levels" (Master Plan §9.5.6 P1-B05, §3.4)
        [Test]
        public void Seams_NeighbourEdgesCoincide()
        {
            double h = TileChart.HalfWidth(-1, 5, 1);
            double worst = 0;
            var ws = new[] { new double2(0, 0), new double2(0.9 * h, 0.9 * h), new double2(0.95 * h, 0) };
            foreach (double L in Ls)
            {
                var meshes = new Dictionary<TileLod, (Mesh, TileMeshLayout, Vector3[])>();
                foreach (var lod in Lods)
                {
                    var r = Make(TileMeshSpec.Hyperbolic(5, L, lod, 0xF));
                    meshes[lod] = (r.mesh, r.lay, r.v);
                    int boundary = 0;
                    for (int i = 0; i < r.lay.FloorVertices.length; i++)
                    {
                        var k = Klein(r.v[i], L);
                        if (Math.Abs(Math.Abs(k.x) - h) < 1e-6 || Math.Abs(Math.Abs(k.y) - h) < 1e-6) boundary++;
                    }
                    Assert.AreEqual(64, boundary, $"boundary vertices L={L} {lod}");
                    for (int e = 0; e < 4; e++)
                    {
                        var ev = EdgeVerts(r.v, r.lay, e, L, h);
                        Assert.AreEqual(17, ev.Count, $"edge {e} {lod}");
                        for (int j = 0; j <= 16; j++)
                            Assert.AreEqual(-h + 2 * h * j / 16, ev[j].t, 1e-6, $"edge {e} node {j} {lod}");
                    }
                }
                foreach (var la in Lods)
                    foreach (var lb in Lods)
                        for (int k = 0; k < 4; k++)
                            for (int j = 0; j < 4; j++)
                            {
                                var (_, layA, vA) = meshes[la];
                                var (_, layB, vB) = meshes[lb];
                                var G = TileTestGeometry.Step(k, -1, 5) * Iso2.Rot(j * Math.PI / 2);
                                var zero = new List<(double t, int i)>();
                                zero.AddRange(EdgeVerts(vA, layA, k, L, h));
                                var nb = new List<(double t, int i)>();
                                for (int i = 0; i < layB.FloorVertices.length; i++)
                                {
                                    var q = math.mul(G.M, Geometry2.Exp(new double2(vB[i].x, vB[i].z) / L, -1));
                                    var r = TileTestGeometry.Rot(TileChart.ModelToKlein(q), -k);
                                    if (Math.Abs(r.x - h) < 1e-6) nb.Add((r.y, i));
                                }
                                nb.Sort((a, b) => a.t.CompareTo(b.t));
                                Assert.AreEqual(17, nb.Count, $"neighbour vertices on shared edge L={L} {la}/{lb} k={k} j={j}");
                                Assert.AreEqual(17, zero.Count);
                                foreach (var w in ws)
                                {
                                    var U = TileTestGeometry.Walker(w);
                                    var A0 = TileTestGeometry.ToFloat(U.Inverse().M);
                                    var A1 = TileTestGeometry.ToFloat((U.Inverse() * G).M);
                                    for (int p = 0; p < 17; p++)
                                    {
                                        Assert.AreEqual(zero[p].t, nb[p].t, 1e-6);
                                        var a = TileTestGeometry.Warp(vA[zero[p].i], A0, L);
                                        var b = TileTestGeometry.Warp(vB[nb[p].i], A1, L);
                                        double d = math.length(a - b);
                                        worst = Math.Max(worst, d);
                                        Assert.LessOrEqual(d, 1e-3, $"seam L={L} {la}/{lb} k={k} j={j}");
                                    }
                                }
                            }
            }
            TestContext.WriteLine($"[T1] maximum seam distance after warp: {worst:E3} m (limit 1e-3)");
        }

        // T2: "five wedges round a post leave no gap and no overlap" (Master Plan A.6)
        [Test]
        public void PostWedges_FiveCloseWithoutGapOrOverlap()
        {
            double h = TileChart.HalfWidth(-1, 5, 1);
            var S1 = TileTestGeometry.Step(1, -1, 5);
            var pw = Iso2.Identity;
            for (int i = 1; i <= 5; i++)
            {
                pw = pw * S1;
                if (i < 5) Assert.Greater(math.cmax(math.abs(pw.M.c0 - new double3(1, 0, 0))) + math.cmax(math.abs(pw.M.c1 - new double3(0, 1, 0))) + math.cmax(math.abs(pw.M.c2 - new double3(0, 0, 1))), 1e-3);
            }
            double id = math.cmax(math.abs(pw.M.c0 - new double3(1, 0, 0))) + math.cmax(math.abs(pw.M.c1 - new double3(0, 1, 0))) + math.cmax(math.abs(pw.M.c2 - new double3(0, 0, 1)));
            Assert.Less(id, 1e-12, "S1^5 = I");

            double worstGap = 0, worstSpanErr = 0, worstCt = 0, worstRad = 0, worstJoin = 0;
            foreach (double L in Ls)
                foreach (var lod in Lods)
                {
                    var r = Make(TileMeshSpec.Hyperbolic(5, L, lod, 0xF));
                    for (int c = 0; c < 4; c++)
                    {
                        double3 P = TileChart.KleinToModel(TileTestGeometry.Rot(new double2(h, h), c), -1);
                        var Binv = Iso2.Boost(Geometry2.Log(P, -1), -1).Inverse();
                        var Rc = Iso2.Rot(c * Math.PI / 2);
                        var Rci = Iso2.Rot(-c * Math.PI / 2);
                        var power = Iso2.Identity;
                        var starts = new List<double>(); var ends = new List<double>(); var spans = new List<double>();
                        var startPos = new List<double3>(); var endPos = new List<double3>();
                        for (int i = 0; i < 5; i++)
                        {
                            var M = Rc * power * Rci;
                            power = power * S1;
                            int ci = -1;
                            for (int cc = 0; cc < 4; cc++)
                            {
                                var Cc = TileChart.KleinToModel(TileTestGeometry.Rot(new double2(h, h), cc), -1);
                                if (math.length(math.mul(M.M, Cc) - P) < 1e-9) ci = cc;
                            }
                            Assert.GreaterOrEqual(ci, 0, $"wedge {i}: no corner maps onto the post (c={c})");
                            int p0 = r.lay.PostVertices(ci).start;
                            double3 Map(int vi) => math.mul(Binv.M, math.mul(M.M, Geometry2.Exp(new double2(r.v[vi].x, r.v[vi].z) / L, -1)));
                            var ct = Map(p0);
                            double ctOff = L * Geometry2.Distance(ct, TileTestGeometry.O, -1);
                            worstCt = Math.Max(worstCt, ctOff);
                            Assert.Less(ctOff, 1e-3, "CT at the post");
                            double prev = 0, span = 0, b0 = 0;
                            double3 first = default, last = default;
                            for (int j = 0; j <= 4; j++)
                            {
                                var Y = Map(p0 + 1 + j);
                                double rad = L * Geometry2.Distance(Y, TileTestGeometry.O, -1);
                                worstRad = Math.Max(worstRad, Math.Abs(rad - TileChart.PostRadius));
                                Assert.AreEqual(TileChart.PostRadius, rad, 1e-3);
                                double beta = Math.Atan2(-Y.y, Y.x); // clockwise in (x,z): arcs run from edge c to edge c+1
                                if (j == 0) { b0 = beta; first = Y; }
                                else
                                {
                                    double d = math.fmod(beta - prev + 3 * Math.PI, 2 * Math.PI) - Math.PI;
                                    Assert.Greater(d, 0, "monotone bearing");
                                    span += d;
                                }
                                prev = beta; last = Y;
                            }
                            starts.Add(b0); ends.Add(b0 + span); spans.Add(span);
                            startPos.Add(first); endPos.Add(last);
                            double se = Math.Abs(span - 72 * Math.PI / 180) * 180 / Math.PI;
                            worstSpanErr = Math.Max(worstSpanErr, se);
                            Assert.AreEqual(72, span * 180 / Math.PI, 0.01);
                        }
                        var order = new List<int> { 0, 1, 2, 3, 4 };
                        order.Sort((a, b) => Mod2Pi(starts[a]).CompareTo(Mod2Pi(starts[b])));
                        double total = 0;
                        for (int i = 0; i < 5; i++)
                        {
                            int a = order[i], b = order[(i + 1) % 5];
                            double gap = Math.Abs(Wrap(ends[a] - starts[b])) * 180 / Math.PI;
                            worstGap = Math.Max(worstGap, gap);
                            Assert.Less(gap, 0.01, "gap or overlap between wedges");
                            double join = L * Geometry2.Distance(endPos[a], startPos[b], -1);
                            worstJoin = Math.Max(worstJoin, join);
                            Assert.Less(join, 1e-3);
                            total += spans[a];
                        }
                        Assert.AreEqual(360, total * 180 / Math.PI, 0.01);
                    }
                }

            // corner angles at (h, h)
            double3 Cm = TileChart.KleinToModel(new double2(h, h), -1);
            double3 Tan(double3 Pm)
            {
                var t = Pm + TileTestGeometry.Ip(Cm, Pm, -1) * Cm;
                return t / Math.Sqrt(TileTestGeometry.Ip(t, t, -1));
            }
            var ta = Tan(TileChart.KleinToModel(new double2(h, -h), -1));
            var tb = Tan(TileChart.KleinToModel(new double2(-h, h), -1));
            double curved = Math.Acos(TileTestGeometry.Ip(ta, tb, -1));
            Assert.AreEqual(2 * Math.PI / 5, curved, 1e-6);
            const double eps = 1e-7;
            var c0 = TileChart.KleinToChart(new double2(h, h), -1, 1);
            var da = TileChart.KleinToChart(new double2(h, h - eps), -1, 1) - c0;
            var db = TileChart.KleinToChart(new double2(h - eps, h), -1, 1) - c0;
            double mesh = Math.Acos(math.dot(da, db) / (math.length(da) * math.length(db))) * 180 / Math.PI;
            Assert.AreEqual(65.823, mesh, 0.01);
            TestContext.WriteLine($"[T2] curved corner {curved * 180 / Math.PI:F6} deg, mesh-coordinate corner {mesh:F4} deg; worst wedge gap/overlap {worstGap:E2} deg; worst span error {worstSpanErr:E2} deg; worst CT offset {worstCt:E2} m; worst arc radius error {worstRad:E2} m; worst arc join {worstJoin:E2} m");
        }

        static double Mod2Pi(double a) { a %= 2 * Math.PI; return a < 0 ? a + 2 * Math.PI : a; }
        static double Wrap(double a) { a = Mod2Pi(a); return a > Math.PI ? a - 2 * Math.PI : a; }

        sealed class EdgeMesh
        {
            public float3[] P;
            public int[] Ea, Eb;
        }

        EdgeMesh ReadEdges(Mesh m)
        {
            var v = m.vertices;
            var ind = m.GetIndices(0);
            var set = new HashSet<long>();
            var ea = new List<int>(); var eb = new List<int>();
            void Add(int a, int b)
            {
                if (v[a].y != v[b].y) return;
                if (v[a].x == v[b].x && v[a].z == v[b].z) return;
                int lo = Math.Min(a, b), hi = Math.Max(a, b);
                if (set.Add(((long)lo << 32) | (uint)hi)) { ea.Add(lo); eb.Add(hi); }
            }
            for (int t = 0; t < ind.Length; t += 3) { Add(ind[t], ind[t + 1]); Add(ind[t + 1], ind[t + 2]); Add(ind[t + 2], ind[t]); }
            var p = new float3[v.Length];
            for (int i = 0; i < v.Length; i++) p[i] = v[i];
            return new EdgeMesh { P = p, Ea = ea.ToArray(), Eb = eb.ToArray() };
        }

        static double TileWorst(EdgeMesh em, double3x3 A, double L)
        {
            var e = new double3(0, 1.6, 0);
            int nv = em.P.Length;
            var K = new double2[nv]; var W = new double3[nv];
            for (int i = 0; i < nv; i++)
            {
                var q = math.mul(A, Geometry2.Exp(new double2(em.P[i].x, em.P[i].z) / L, -1));
                K[i] = q.xy / q.z;
                var t = Geometry2.Log(q, -1) * L;
                W[i] = new double3(t.x, em.P[i].y, t.y);
            }
            double worst = 0;
            for (int ei = 0; ei < em.Ea.Length; ei++)
            {
                int a = em.Ea[ei], b = em.Eb[ei];
                double3 Aa = W[a] - e, Bb = W[b] - e;
                double3 n = math.normalize(math.cross(Aa, Bb));
                double y = em.P[a].y;
                for (int s = 1; s <= 5; s++)
                {
                    double u = s / 6.0;
                    double2 k = (1 - u) * K[a] + u * K[b];
                    double3 X = new double3(k, 1) / Math.Sqrt(1 - math.lengthsq(k));
                    var lg = Geometry2.Log(X, -1) * L;
                    double3 v = math.normalize(new double3(lg.x, y, lg.y) - e);
                    double3 p = v - math.dot(n, v) * n;
                    double err;
                    if (math.dot(math.cross(Aa, p), n) >= 0 && math.dot(math.cross(p, Bb), n) >= 0) err = Math.Asin(Math.Min(1, Math.Abs(math.dot(n, v))));
                    else err = Math.Min(Ang(v, Aa), Ang(v, Bb));
                    worst = Math.Max(worst, err);
                }
            }
            return worst * 180 / Math.PI;
        }

        static double Ang(double3 a, double3 b) => Math.Atan2(math.length(math.cross(a, b)), math.dot(a, b));

        // T3: "worst screen-space chord error <= 0.15 deg from eye height 1.6 m at L = 1.4, 1.9, 3.0" (Master Plan §3.4, §8.4)
        [Test, Timeout(600000)]
        public void ChordError_AtMost015Deg()
        {
            double h = TileChart.HalfWidth(-1, 5, 1);
            var tiles = TileTestGeometry.Tiles(5.2);
            var d0 = new double[tiles.Count];
            int near = 0;
            for (int i = 0; i < tiles.Count; i++) { d0[i] = TileTestGeometry.Dist(tiles[i]); if (d0[i] <= 1.7) near++; }
            Assert.AreEqual(13, near, "tile zero plus the 12 that touch it");
            TestContext.WriteLine($"[T3] {tiles.Count} tiles within 5.2 L");
            const double midR = 3.5 + 0.8425;
            var walkers = new List<double2>();
            for (int a = -3; a <= 3; a++) for (int b = -3; b <= 3; b++) walkers.Add(0.999 * h * new double2(a / 3.0, b / 3.0));
            foreach (double L in Ls)
            {
                var em = new EdgeMesh[3];
                for (int li = 0; li < 3; li++) em[li] = ReadEdges(Make(TileMeshSpec.Hyperbolic(5, L, Lods[li], 0xF, 0xF)).mesh);
                var worst = new double[walkers.Count, 3];
                Parallel.For(0, walkers.Count, wi =>
                {
                    var U = TileTestGeometry.Walker(walkers[wi]);
                    var Ui = U.Inverse();
                    var uo = math.mul(U.M, TileTestGeometry.O);
                    for (int ti = 0; ti < tiles.Count; ti++)
                    {
                        var F = tiles[ti];
                        double dw = Geometry2.Distance(math.mul(F.M, TileTestGeometry.O), uo, -1);
                        var A = (Ui * F).M;
                        if (d0[ti] <= 1.7) worst[wi, 0] = Math.Max(worst[wi, 0], TileWorst(em[0], A, L));
                        if (d0[ti] > 1.7 && dw <= midR) worst[wi, 1] = Math.Max(worst[wi, 1], TileWorst(em[1], A, L));
                        if (dw > 3.0 && dw <= midR) worst[wi, 2] = Math.Max(worst[wi, 2], TileWorst(em[2], A, L));
                    }
                });
                for (int li = 0; li < 3; li++)
                {
                    double w = 0;
                    for (int wi = 0; wi < walkers.Count; wi++) w = Math.Max(w, worst[wi, li]);
                    TestContext.WriteLine($"[T3] L={L} {Lods[li]}: worst chord error {w:F4} deg (limit 0.15)");
                    Assert.LessOrEqual(w, 0.15, $"chord error L={L} {Lods[li]}");
                }
            }
        }

        // T4: "<= 1,200 / 500 / 250 triangles for the three levels at any L" (Master Plan §8.4)
        [Test]
        public void Triangles_WithinCaps()
        {
            var sw = new Stopwatch();
            foreach (var lod in Lods)
            {
                int max = 0, first = -1;
                foreach (double L in new[] { 0.9422, 1.4, 1.9, 3.0, 3.0152 })
                    for (int km = 0; km < 16; km++)
                    {
                        var m = new Mesh(); made.Add(m);
                        gen.Build(TileMeshSpec.Hyperbolic(5, L, lod, km), m);
                        int tri = (int)m.GetIndexCount(0) / 3;
                        Assert.LessOrEqual(tri, TileChart.TriangleCap(lod));
                        if (km == 15) { if (first < 0) first = tri; Assert.AreEqual(first, tri, "count independent of L"); }
                        max = Math.Max(max, tri);
                    }
                var mm = new Mesh(); made.Add(mm);
                gen.Build(TileMeshSpec.Hyperbolic(5, 1.9, lod, 0xF), mm);
                sw.Restart();
                const int reps = 200;
                for (int i = 0; i < reps; i++) gen.Build(TileMeshSpec.Hyperbolic(5, 1.9, lod, 0xF), mm);
                sw.Stop();
                TestContext.WriteLine($"[T4] {lod}: max triangles {max} (cap {TileChart.TriangleCap(lod)}); Build time {sw.Elapsed.TotalMilliseconds / reps:F3} ms per mesh");
            }
        }
    }
}
