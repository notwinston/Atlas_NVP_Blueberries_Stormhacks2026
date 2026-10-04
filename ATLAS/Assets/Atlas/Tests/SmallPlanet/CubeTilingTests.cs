using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Rendering;
using NUnit.Framework;
using Unity.Mathematics;
using static Atlas.Spaces.SmallPlanet.Tests.SmallPlanetTestUtil;

namespace Atlas.Spaces.SmallPlanet.Tests
{
    public class CubeTilingTests
    {
        static readonly int[][] GoldenPhi =
        {
            new[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 },
            new[] { 0, 0, 1, 0, -1, 0, 1, 0, 0 },
            new[] { 0, 1, 0, 0, 0, 1, 1, 0, 0 },
            new[] { 0, 0, -1, 0, 1, 0, 1, 0, 0 },
            new[] { 0, -1, 0, 0, 0, -1, 1, 0, 0 },
            new[] { 1, 0, 0, 0, -1, 0, 0, 0, -1 },
        };
        static readonly int[][] GoldenCentre = { new[] { 0, 0, 1 }, new[] { 1, 0, 0 }, new[] { 0, 1, 0 }, new[] { -1, 0, 0 }, new[] { 0, -1, 0 }, new[] { 0, 0, -1 } };
        static readonly int[][] N = { new[] { 1, 2, 3, 4 }, new[] { 0, 4, 5, 2 }, new[] { 0, 1, 5, 3 }, new[] { 0, 2, 5, 4 }, new[] { 0, 3, 5, 1 }, new[] { 1, 4, 3, 2 } };
        static readonly int[][] J = { new[] { 0, 0, 0, 0 }, new[] { 0, 1, 0, 3 }, new[] { 3, 1, 1, 3 }, new[] { 2, 1, 2, 3 }, new[] { 1, 1, 3, 3 }, new[] { 2, 2, 2, 2 } };
        static readonly int[] Anti = { 5, 3, 4, 1, 2, 0 };

        // T2. §9.5.6 P1-B08 "{4,3} tiling: six tiles"; task "6 tiles, 4 neighbours each, 3 per corner"; A.3, A.5; kernel.md §5.8
        [Test]
        public void CubeTiling_SixTilesAdjacency()
        {
            Assert.AreEqual(6, CubeTiling.FaceCount);
            var seen = new HashSet<TileId>();
            for (int f = 0; f < 6; f++)
            {
                Assert.IsTrue(seen.Add(CubeTiling.FaceId(f)));
                var id = CubeTiling.FaceId(f);
                Assert.AreEqual(f, CubeTiling.FaceOf(id));
            }
            Assert.AreEqual(-1, CubeTiling.FaceOf(TileId.Zero));
            Assert.AreEqual(Math.Atan(Math.Sqrt(2)), CubeTiling.Circumradius, 1e-15);
            Assert.AreEqual(0.9553166, CubeTiling.Circumradius, 1e-7);

            for (int f = 0; f < 6; f++)
            {
                var m = CubeTiling.Frame(f).M;
                for (int c = 0; c < 3; c++)
                    for (int r = 0; r < 3; r++)
                    {
                        Assert.AreEqual(GoldenPhi[f][r * 3 + c], m[c][r], $"Phi{f}[{r},{c}]");
                        Assert.AreEqual(Math.Round(m[c][r]), m[c][r]);
                    }
                Assert.IsTrue(math.mul(math.transpose(m), m).Equals(double3x3.identity), "orthonormal exact");
                Assert.AreEqual(1.0, math.determinant(m));
                Assert.AreEqual(new double3(GoldenCentre[f][0], GoldenCentre[f][1], GoldenCentre[f][2]), m.c2);
                Assert.AreEqual(Anti[f], CubeTiling.Antipode(f));
                Assert.AreEqual(f, CubeTiling.Antipode(CubeTiling.Antipode(f)));

                var nb = new HashSet<int>();
                for (int k = 0; k < 4; k++)
                {
                    int g = CubeTiling.Neighbour(f, k);
                    Assert.AreEqual(N[f][k], g);
                    Assert.AreNotEqual(f, g);
                    Assert.AreNotEqual(CubeTiling.Antipode(f), g);
                    Assert.IsTrue(nb.Add(g));
                    int be = CubeTiling.BackEdge(f, k);
                    Assert.AreEqual(f, CubeTiling.Neighbour(g, be));
                    Assert.IsTrue(ExactEq(CubeTiling.Step(g, be), CubeTiling.Step(f, k).Inverse()), $"Step({g},{be}) == Step({f},{k})^-1");
                    int matches = 0;
                    for (int j = 0; j < 4; j++)
                        if (ExactEq(CubeTiling.Step(f, k), CubeTiling.StepMatrix(k) * ExactRot(j))) { matches++; Assert.AreEqual(J[f][k], j); }
                    Assert.AreEqual(1, matches, $"J unique {f},{k}");
                    Assert.AreEqual((4 - J[f][k]) % 4, be);
                    double3 c = CubeTiling.Step(f, k).M.c2;
                    Assert.AreEqual(Math.PI / 2, Geometry2.Distance(c, Geometry2.BasePoint, 1), 1e-12);
                    double2 e = new double2(k == 0 ? 1 : k == 2 ? -1 : 0, k == 1 ? 1 : k == 3 ? -1 : 0);
                    Assert.AreEqual(e, c.xy, "gnomonic direction is e_k");
                    Assert.AreEqual(1, CubeTiling.GraphDistance(f, g));
                }
                Assert.AreEqual(4, nb.Count);
                Assert.AreEqual(2, CubeTiling.GraphDistance(f, Anti[f]));
                Assert.AreEqual(0, CubeTiling.GraphDistance(f, f));
            }

            // (a) A.5: (S_1)^q = I and no smaller power
            var s1 = CubeTiling.StepMatrix(1);
            var id3 = new Iso2(double3x3.identity, 1);
            Assert.IsFalse(ExactEq(s1, id3));
            Assert.IsFalse(ExactEq(s1 * s1, id3));
            Assert.IsTrue(ExactEq(s1 * s1 * s1, id3));

            // (b) walk round a vertex
            for (int f = 0; f < 6; f++)
                for (int j = 0; j < 4; j++)
                {
                    var start = CubeTiling.Frame(f) * ExactRot(j);
                    var m = start;
                    var faces = new List<int>();
                    for (int step = 1; step <= 3; step++)
                    {
                        m = m * s1;
                        int g = FaceAt(m.M.c2);
                        faces.Add(g);
                        if (step < 3) Assert.AreNotEqual(f, g, $"f={f} j={j} step={step}");
                        bool found = false;
                        for (int q = 0; q < 4; q++) found |= ExactEq(m, CubeTiling.Frame(g) * ExactRot(q));
                        Assert.IsTrue(found, "frame is canonical in its face");
                    }
                    Assert.AreEqual(f, faces[2]);
                    Assert.IsTrue(ExactEq(m, start), $"returns to start f={f} j={j}");
                    Assert.AreNotEqual(faces[0], faces[1]);
                }

            // (c) 8 vertices, 12 edges, V - E + F = 2
            var verts = new List<double3>();
            var vcount = new List<int>();
            int[,] vid = new int[6, 4];
            for (int f = 0; f < 6; f++)
                for (int c = 0; c < 4; c++)
                {
                    double3 v = math.mul(CubeTiling.Frame(f).M, RotXY(new double3(1, 1, 1), c)) / Math.Sqrt(3);
                    int idx = verts.FindIndex(w => math.length(w - v) < 1e-9);
                    if (idx < 0) { verts.Add(v); vcount.Add(0); idx = verts.Count - 1; }
                    vcount[idx]++; vid[f, c] = idx;
                }
            Assert.AreEqual(8, verts.Count);
            foreach (int n in vcount) Assert.AreEqual(3, n);
            var edges = new Dictionary<(int, int), int>();
            for (int f = 0; f < 6; f++)
                for (int k = 0; k < 4; k++)
                {
                    int a = vid[f, k], b = vid[f, (k + 3) % 4]; // edge k joins corners k-1 and k
                    var key = (Math.Min(a, b), Math.Max(a, b));
                    edges[key] = edges.TryGetValue(key, out int c) ? c + 1 : 1;
                }
            Assert.AreEqual(12, edges.Count);
            foreach (var kv in edges) Assert.AreEqual(2, kv.Value);
            Assert.AreEqual(2, verts.Count - edges.Count + 6);

            // (d) A.3 corner angle
            Assert.AreEqual(2 * Math.PI / 3, TileChart.CornerAngle(1, 3), 1e-12);
            Assert.AreEqual(2 * Math.PI, 3 * TileChart.CornerAngle(1, 3), 1e-12);
        }

        static double3 RotXY(double3 v, int c)
        {
            for (int i = 0; i < c; i++) v = new double3(-v.y, v.x, v.z);
            return v;
        }

        static int FaceAt(double3 p)
        {
            for (int g = 0; g < 6; g++) if (CubeTiling.Frame(g).M.c2.Equals(p)) return g;
            Assert.Fail("point is not a face centre"); return -1;
        }

        // T2 (VisibleSet part). kernel.md §4.6 "On q = 3 all six tiles are always returned"; §8.3
        [Test]
        public void CubeTiling_VisibleSet()
        {
            var g = new CubeTileGraph();
            Assert.AreEqual(3, g.Q);
            var list = new List<VisibleTile>(6);
            foreach (double rho in new[] { 0.0, 1.0, 100.0 })
                for (int c = 0; c < 6; c++)
                {
                    g.VisibleSet(CubeTiling.FaceId(c), rho, list);
                    Assert.AreEqual(6, list.Count);
                    var seen = new HashSet<int>();
                    for (int i = 0; i < 6; i++)
                    {
                        int t = CubeTiling.FaceOf(list[i].Id);
                        Assert.IsTrue(seen.Add(t));
                        Assert.IsTrue(ExactEq(CubeTiling.Chain(c, t), list[i].Chain));
                        int expT = i == 0 ? c : i <= 4 ? CubeTiling.Neighbour(c, i - 1) : CubeTiling.Antipode(c);
                        Assert.AreEqual(expT, t);
                        Assert.AreEqual(i == 0 ? 0 : i <= 4 ? 1 : 2, list[i].GraphDistance);
                    }
                }
            Assert.Throws<ArgumentException>(() => g.VisibleSet(TileId.Zero, 1, list));
            list.Add(default);
            g.VisibleSet(CubeTiling.FaceId(2), 1, list);
            Assert.AreEqual(6, list.Count, "list cleared on reuse");
            var id = CubeTiling.FaceId(3);
            for (int i = 0; i < 10; i++) g.VisibleSet(id, 1, list);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) g.VisibleSet(id, 1, list);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            TestContext.WriteLine($"T2 VisibleSet allocated {bytes} bytes in 1000 calls");
            Assert.AreEqual(0, bytes);
        }
    }
}
