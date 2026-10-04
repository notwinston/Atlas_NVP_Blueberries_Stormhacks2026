using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Rendering;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.TileMeshes.Tests
{
    // Supporting tests (not acceptance tests).
    public class TileMeshSupportTests
    {
        TileMeshGenerator gen;

        [SetUp] public void SetUp() { gen = new TileMeshGenerator(TilePalette.Default); }

        (TileMeshData m, TileMeshLayout lay) Make(TileMeshSpec s)
        {
            var m = new TileMeshData();
            return (m, m.Build(gen, s));
        }

        [Test]
        public void VertexData()
        {
            var pal = TilePalette.Default;
            Assert.AreEqual(AtlasPalette.Uv(AtlasPalette.Floor), pal.Floor);
            Assert.AreEqual(AtlasPalette.Uv(AtlasPalette.Kerb), pal.Kerb);
            Assert.AreEqual(AtlasPalette.Uv(AtlasPalette.Post), pal.Post);
            foreach (var lod in new[] { TileLod.Near, TileLod.Mid, TileLod.Far })
            {
                var (m, lay) = Make(TileMeshSpec.Hyperbolic(5, 1.9, lod, 0xF));
                var v = m.vertices; var col = m.colors32; var uv = m.uv; var line = m.uv2; var ind = m.GetIndices(0);
                double hw = TileChart.HalfWidth(-1, 5, 1.9);
                Assert.AreEqual(lay.VertexCount, v.Length);
                Assert.AreEqual(lay.IndexCount, ind.Length);
                for (int i = 0; i < v.Length; i++)
                {
                    float ao = col[i].r / 255f;
                    Assert.GreaterOrEqual(ao, 0.6f - 1f / 255); Assert.LessOrEqual(ao, 1f);
                    Assert.AreEqual(255, col[i].b); Assert.AreEqual(255, col[i].a);
                }
                Assert.AreEqual(255, col[(int)lod / 2 * ((int)lod + 1) + (int)lod / 2].r, "tile-centre floor vertex has AO 1");
                // the look's vertex layout (lane A contract 10.2): the floor's line coordinate runs 0..1 across the tile, so lines fall on its edges
                int onEdge = 0;
                for (int i = 0; i < lay.FloorVertices.length; i++)
                {
                    Assert.AreEqual(pal.Floor.x, uv[i].x); Assert.AreEqual(pal.Floor.y, uv[i].y);
                    Assert.AreEqual(255, col[i].g, "floor line weight");
                    var k = TileChart.ChartToKlein(new double2(v[i].x, v[i].z), -1, 1.9);
                    Assert.AreEqual(0.5 + 0.5 * k.x / hw, line[i].x, 1e-5); Assert.AreEqual(0.5 + 0.5 * k.y / hw, line[i].y, 1e-5);
                    if (Math.Abs(Math.Abs(k.x) - hw) < 1e-6) { Assert.IsTrue(line[i].x == 0f || line[i].x == 1f, "edge vertex on an integer"); onEdge++; }
                    if (Math.Abs(Math.Abs(k.y) - hw) < 1e-6) { Assert.IsTrue(line[i].y == 0f || line[i].y == 1f, "edge vertex on an integer"); onEdge++; }
                }
                Assert.AreEqual(4 * 17, onEdge, "17 boundary vertices per edge");
                for (int e = 0; e < 4; e++)
                {
                    var kv = lay.KerbVertices(e);
                    for (int i = kv.start; i < kv.start + kv.length; i++)
                    {
                        Assert.AreEqual(pal.Kerb.x, uv[i].x);
                        Assert.AreEqual(0, col[i].g, "kerbs carry no lines"); Assert.AreEqual(new Vector2(0.5f, 0.5f), line[i]);
                        Assert.AreEqual(v[i].y > 0 ? 255 : 153, col[i].r);
                    }
                    var pv = lay.PostVertices(e);
                    for (int i = pv.start; i < pv.start + pv.length; i++)
                    {
                        Assert.AreEqual(pal.Post.x, uv[i].x);
                        Assert.AreEqual(0, col[i].g, "posts carry no lines"); Assert.AreEqual(new Vector2(0.5f, 0.5f), line[i]);
                        Assert.AreEqual(v[i].y > 0 ? 255 : 153, col[i].r);
                    }
                }
                // winding: Master Plan-independent check of brief 3.8 (floor +y, kerb faces along the edge normal)
                for (int t = 0; t < lay.FloorIndices.length; t += 3)
                {
                    Vector3 a = v[ind[t]], b = v[ind[t + 1]], c = v[ind[t + 2]];
                    Assert.Greater(Vector3.Cross(b - a, c - a).y, 0, "floor winding");
                }
                for (int e = 0; e < 4; e++)
                {
                    var ki = lay.KerbIndices(e);
                    var dir = new float3(1, 0, 0);
                    for (int r = 0; r < e; r++) dir = new float3(-dir.z, 0, dir.x);
                    for (int t = ki.start; t < ki.start + ki.length; t += 3)
                    {
                        Vector3 a = v[ind[t]], b = v[ind[t + 1]], c = v[ind[t + 2]];
                        var n = Vector3.Cross(b - a, c - a).normalized;
                        bool top = Mathf.Abs(n.y) > 0.5f;
                        if (top) Assert.Greater(n.y, 0);
                        else Assert.AreEqual(1f, Mathf.Abs(Vector3.Dot(n, new Vector3(dir.x, 0, dir.z))), 0.1f, "kerb face along edge normal");
                    }
                }
                for (int c = 0; c < 4; c++)
                {
                    var pi = lay.PostIndices(c); var pv = lay.PostVertices(c);
                    Vector3 corner = v[pv.start];
                    for (int t = pi.start; t < pi.start + pi.length; t += 3)
                    {
                        Vector3 a = v[ind[t]], b = v[ind[t + 1]], cc = v[ind[t + 2]];
                        var n = Vector3.Cross(b - a, cc - a);
                        if (Mathf.Abs(n.y) > 1e-5f && Mathf.Abs(n.y) > Mathf.Abs(n.x) + Mathf.Abs(n.z)) Assert.Greater(n.y, 0);
                        else
                        {
                            var mid = (a + b + cc) / 3 - corner; mid.y = 0;
                            Assert.Greater(Vector3.Dot(new Vector3(n.x, 0, n.z), mid), 0, "post faces away from corner");
                        }
                    }
                }
            }
        }

        [Test]
        public void OtherShapes()
        {
            Assert.AreEqual(0.4859, TileChart.HalfWidth(-1, 5, 1), 1e-4);
            Assert.AreEqual(0.5774, TileChart.HalfWidth(-1, 6, 1), 1e-4);
            Assert.AreEqual(1.0, TileChart.HalfWidth(1, 3, 1), 1e-4);
            Assert.AreEqual(Math.Acosh((1 + Math.Sqrt(5)) / 2), 2 * Math.Atanh(TileChart.HalfWidth(-1, 5, 1)) , 1e-9);

            // {4,6} and {4,3}: seam between Near and Far across edge 0, and q wedges covering 360 deg
            foreach (var (kappa, q, scale) in new[] { (-1, 6, 1.9), (1, 3, 1.9) })
            {
                double h = TileChart.HalfWidth(kappa, q, scale);
                TileMeshSpec Spec(TileLod l) => kappa < 0 ? TileMeshSpec.Hyperbolic(q, scale, l, 0xF) : TileMeshSpec.Spherical(scale, l, 0xF);
                var (mn, ln) = Make(Spec(TileLod.Near)); var (mf, lf) = Make(Spec(TileLod.Far));
                var vn = mn.vertices; var vf = mf.vertices;
                var G = TileTestGeometry.Step(0, kappa, q);
                double worst = 0;
                var Pn = new List<double3>(); var Pf = new List<double3>();
                for (int i = 0; i < ln.FloorVertices.length; i++)
                {
                    var k = TileChart.ChartToKlein(new double2(vn[i].x, vn[i].z), kappa, scale);
                    if (Math.Abs(k.x - h) < 1e-6) Pn.Add(TileChart.KleinToModel(k, kappa));
                }
                for (int i = 0; i < lf.FloorVertices.length; i++)
                {
                    var p = math.mul(G.M, Geometry2.Exp(new double2(vf[i].x, vf[i].z) / scale, kappa));
                    var k = TileChart.ModelToKlein(p);
                    if (Math.Abs(k.x - h) < 1e-6) Pf.Add(TileChart.KleinToModel(k, kappa));
                }
                Assert.AreEqual(17, Pn.Count); Assert.AreEqual(17, Pf.Count);
                foreach (var a in Pn)
                {
                    double best = double.MaxValue;
                    foreach (var b in Pf) best = Math.Min(best, scale * Geometry2.Distance(a, b, kappa));
                    worst = Math.Max(worst, best);
                }
                Assert.Less(worst, 1e-3);
                TestContext.WriteLine($"[S2] kappa={kappa} q={q} Near/Far seam distance {worst:E2} m");

                // angle at a corner is 2 pi / q
                double3 C = TileChart.KleinToModel(new double2(h, h), kappa);
                double3 Tan(double2 pk)
                {
                    var P = TileChart.KleinToModel(pk, kappa);
                    var t = P - kappa * TileTestGeometry.Ip(C, P, kappa) * C;
                    return t / Math.Sqrt(TileTestGeometry.Ip(t, t, kappa));
                }
                double ang = Math.Acos(TileTestGeometry.Ip(Tan(new double2(h, -h)), Tan(new double2(-h, h)), kappa));
                Assert.AreEqual(360.0, q * ang * 180 / Math.PI, 0.01);
                Assert.AreEqual(2 * Math.PI / q, ang, 1e-9);
            }

            foreach (double g in new[] { 0.8, 1.13, 1.35 })
            {
                var (m, lay) = Make(TileMeshSpec.FlatCell(g, 0xF));
                Assert.AreEqual(176, lay.TriangleCount);
                var v = m.vertices; var ind = m.GetIndices(0);
                double area = 0;
                for (int t = 0; t < lay.FloorIndices.length; t += 3)
                {
                    Vector3 a = v[ind[t]], b = v[ind[t + 1]], c = v[ind[t + 2]];
                    area += Vector3.Cross(b - a, c - a).y / 2;
                }
                Assert.AreEqual(g * g, area, 1e-6);
                for (int i = 0; i < lay.FloorVertices.length; i++)
                {
                    Assert.LessOrEqual(Mathf.Abs(v[i].x), g / 2 + 1e-6f); Assert.LessOrEqual(Mathf.Abs(v[i].z), g / 2 + 1e-6f);
                }
                // four quarter-posts round an interior corner of four cells cover 360 deg at 90 deg each
                double total = 0;
                for (int c = 0; c < 4; c++)
                {
                    int sx = c == 0 || c == 3 ? 1 : -1, sz = c < 2 ? 1 : -1;
                    var corner = new Vector3(0, 0, 0);
                    // the cell whose corner c' sits at the origin has centre (-cornerX, -cornerZ); use cell centred at (sx g/2, sz g/2) and its corner at the origin
                    int cc = -1;
                    for (int k = 0; k < 4; k++)
                    {
                        var ck = TileTestGeometry.Rot(new double2(g / 2, g / 2), k);
                        if (math.length(new double2(sx * g / 2, sz * g / 2) + ck) < 1e-9) cc = k;
                    }
                    Assert.GreaterOrEqual(cc, 0);
                    var pv = lay.PostVertices(cc);
                    var centre = new Vector3(sx * (float)g / 2, 0, sz * (float)g / 2);
                    double b0 = Math.Atan2(v[pv.start + 1].z + centre.z, v[pv.start + 1].x + centre.x);
                    double b1 = Math.Atan2(v[pv.start + 5].z + centre.z, v[pv.start + 5].x + centre.x);
                    double span = Math.Abs(Math.Atan2(Math.Sin(b1 - b0), Math.Cos(b1 - b0)));
                    Assert.AreEqual(90, span * 180 / Math.PI, 0.01);
                    total += span;
                }
                Assert.AreEqual(360, total * 180 / Math.PI, 0.01);
            }
        }

#if !ATLAS_HEADLESS
        // The upload: one stream in the look's vertex layout, the generator's counts, UInt16 indices.
        [Test, Category("Unity")]
        public void Upload_WritesTheLookLayout()
        {
            var mesh = new Mesh();
            try
            {
                var lay = gen.Build(TileMeshSpec.Hyperbolic(5, 1.9, TileLod.Near, 0xF), mesh);
                Assert.AreEqual(lay.VertexCount, mesh.vertexCount);
                Assert.AreEqual(lay.IndexCount, (int)mesh.GetIndexCount(0));
                Assert.AreEqual(UnityEngine.Rendering.IndexFormat.UInt16, mesh.indexFormat);
                Assert.AreEqual(AtlasVertex.Stride, mesh.GetVertexBufferStride(0));
                var attributes = mesh.GetVertexAttributes();
                Assert.AreEqual(AtlasMesh.Layout.Length, attributes.Length);
                for (int i = 0; i < attributes.Length; i++) Assert.AreEqual(AtlasMesh.Layout[i], attributes[i]);
                var v = mesh.vertices; var col = mesh.colors32; var line = mesh.uv2;
                for (int i = 0; i < lay.VertexCount; i++)
                {
                    Assert.AreEqual((Vector3)gen.Vertices[i].Position, v[i]);
                    Assert.AreEqual(gen.Vertices[i].Shade, col[i].r); Assert.AreEqual(gen.Vertices[i].LineWeight, col[i].g);
                    Assert.AreEqual((Vector2)gen.Vertices[i].Uv1, line[i]);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
#endif
    }
}
