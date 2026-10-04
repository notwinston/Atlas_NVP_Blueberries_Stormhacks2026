using System.Collections.Generic;
using Atlas.Blades.Lung.AirwayKit;
using Atlas.Kernel;
using Atlas.Rendering;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.AirwayViews.Tests
{
    public class KitFloorTests
    {
        // Criterion: P2-B02 "come from the P1-B05 generator ... keep its LODs ... triangle caps".
        [Test]
        public void Kit_FloorFromGenerator()
        {
            var b = KitTestUtil.NewBuilder();
            var gen = new TileMeshGenerator(TilePalette.Default);
            {
                foreach (var (kappa, scale) in KitTestUtil.Geoms())
                    foreach (var lod in KitTestUtil.Lods)
                    {
                        if (kappa == 0 && lod != TileLod.Near) continue;
                        var spec = KitTestUtil.Spec(kappa, scale, lod);
                        var rl = gen.Build(spec);
                        var rv = KitTestUtil.Copy(gen.Vertices, rl.VertexCount); var ri = KitTestUtil.Copy(gen.Indices, rl.IndexCount);
                        foreach (var kind in KitTestUtil.Kinds)
                        {
                            var lay = b.BuildTile(kind, spec);
                            var v = KitTestUtil.Verts(b, lay.VertexCount); var idx = KitTestUtil.Idx(b, lay.IndexCount);
                            Assert.AreEqual(rv.Length, lay.FloorVertices.length);
                            Assert.AreEqual(ri.Length, lay.FloorIndices.length);
                            for (int i = 0; i < rv.Length; i++)
                            {
                                Assert.IsTrue(math.all(v[i].Position == rv[i].Position), "pos");
                                Assert.AreEqual(rv[i].Color, v[i].Color, "colour");
                                Assert.IsTrue(math.all(v[i].Uv0 == rv[i].Uv0) && math.all(v[i].Uv1 == rv[i].Uv1), "uv");
                                Assert.IsTrue(math.all(v[i].Normal == rv[i].Normal), "normal");
                            }
                            for (int i = 0; i < ri.Length; i++) Assert.AreEqual(ri[i], idx[i]);
                            Assert.LessOrEqual(lay.FloorIndices.length / 3, TileChart.TriangleCap(lod));
                        }
                    }
            }
        }

        // Criterion: "16 boundary segments per edge".
        [Test]
        public void Kit_FloorBoundary16()
        {
            var b = KitTestUtil.NewBuilder();
            {
                foreach (var s in KitTestUtil.SideS)
                {
                    double L = KitTestUtil.LFromS(s);
                    foreach (var lod in KitTestUtil.Lods)
                    {
                        var lay = b.BuildTile(KitKind.Fork, KitTestUtil.Spec(-1, L, lod));
                        var v = KitTestUtil.Verts(b, lay.VertexCount);
                        for (int e = 0; e < 4; e++)
                        {
                            var pts = new List<double2>();
                            for (int i = 0; i < lay.FloorVertices.length; i++)
                            {
                                var c = KitTestUtil.Chart(v[i]);
                                if (KitTestUtil.EdgeResidual(c, e, L) > 1e-6) continue;
                                bool dup = false; foreach (var p in pts) if (math.length(p - c) < 1e-6) { dup = true; break; }
                                if (!dup) pts.Add(c);
                            }
                            Assert.AreEqual(17, pts.Count, $"s {s} lod {lod} edge {e}");
                        }
                    }
                }
            }
        }

        // Criterion: MP 8.4 "neighbours share their edge vertices exactly" (across the open edge, walls meet too).
        [Test]
        public void Kit_SeamsAcrossOpenEdge()
        {
            var b = KitTestUtil.NewBuilder();
            {
                foreach (var (kappa, scale) in new[] { (-1, KitTestUtil.LFromS(2.0)), (-1, KitTestUtil.LFromS(3.2)), (0, 1.5) })
                    foreach (var lodS in kappa < 0 ? new[] { TileLod.Mid, TileLod.Far } : new[] { TileLod.Near })
                    {
                        double H = KitGeometry.InRadiusMetres(kappa, scale);
                        Iso2 S1 = kappa < 0 ? Iso2.Rot(System.Math.PI / 2) * Iso2.Boost(new double2(2 * KitTestUtil.RIn, 0), -1) * Iso2.Rot(System.Math.PI)
                                       : Iso2.Rot(System.Math.PI / 2) * Iso2.Boost(new double2(scale, 0), 0) * Iso2.Rot(System.Math.PI);
                        var lf = b.BuildTile(KitKind.Fork, KitTestUtil.Spec(kappa, scale, TileLod.Near));
                        var vf = KitTestUtil.Verts(b, lf.VertexCount);
                        var ls = b.BuildTile(KitKind.Straight, KitTestUtil.Spec(kappa, scale, lodS));
                        var vs = KitTestUtil.Verts(b, ls.VertexCount);
                        var sInFork = new List<double2>();
                        foreach (var v in vs) sInFork.Add(KitTestUtil.Apply(S1, KitTestUtil.Chart(v), kappa, scale));
                        var forkChart = new List<double2>(); foreach (var v in vf) forkChart.Add(KitTestUtil.Chart(v));

                        bool Has(List<double2> pts, AtlasVertex[] vv, double2 p, float y)
                        {
                            for (int i = 0; i < pts.Count; i++) if (math.length(pts[i] - p) < 1e-3 && System.Math.Abs(vv[i].Position.y - y) < 1e-3f) return true;
                            return false;
                        }

                        int n = 0;
                        for (int i = 0; i < vf.Length; i++)
                        {
                            if (vf[i].Position.y != 0) continue;
                            bool onEdge1 = kappa < 0 ? KitTestUtil.EdgeResidual(forkChart[i], 1, scale) <= 1e-6
                                                    : System.Math.Abs(forkChart[i].y - H) <= 1e-6;
                            if (!onEdge1) continue;
                            n++;
                            Assert.IsTrue(Has(sInFork, vs, forkChart[i], 0), $"no straight vertex near fork edge vertex {forkChart[i]} (kappa {kappa}, {lodS})");
                        }
                        Assert.GreaterOrEqual(n, kappa < 0 ? 17 : 5);   // flat cell: 4 boundary segments (TileMeshSpec.BoundarySegments)
                        foreach (double sign in new[] { 1.0, -1.0 })
                        {
                            double2 p = KitGeometry.FermiToChart(1, H, sign * KitGeometry.CorridorHalfWidth, kappa, scale);
                            Assert.IsTrue(Has(forkChart, vf, p, 0), "fork arm-1 wall end");
                            Assert.IsTrue(Has(sInFork, vs, p, 0), "straight arm-0 wall end");
                        }
                    }
            }
        }
    }
}
