using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Atlas.Kernel;
using Atlas.Rendering;
using NUnit.Framework;
using Unity.Mathematics;
#if !ATLAS_HEADLESS
using UnityEngine;
#endif
using static Atlas.Spaces.SmallPlanet.Tests.SmallPlanetTestUtil;
using Random = System.Random;

namespace Atlas.Spaces.SmallPlanet.Tests
{
    public class SmallPlanetRenderTests
    {
        static readonly double3 O = new double3(0, 0, 1);
        static readonly float3 Head = new float3(0, 1.6f, 0);

        static Iso2 PoseTowards(double3 world, double rotation)
        {
            double2 t = Geometry2.Log(world, 1);
            return new Iso2((Iso2.Boost(t, 1) * Iso2.Rot(rotation)).M, 1);
        }

        // T4. P1-B05 seam criterion on the {4,3} frames; §3.4, §9.5.6 P1-B05
        [Test]
        public void SmallPlanet_TileSeams()
        {
            var rng = new Random(20261003);
            double worst = 0; int checkedVerts = 0;
            foreach (double s in new[] { 1.0, 2.0, 3.2 })
            {
                double R = 2 * s / Math.PI;
                var tile = BuildTile(R);
                var edgeVerts = new List<int>[4];
                for (int k = 0; k < 4; k++)
                {
                    edgeVerts[k] = new List<int>();
                    var e = new double2(k == 0 ? 1 : k == 2 ? -1 : 0, k == 1 ? 1 : k == 3 ? -1 : 0);
                    for (int i = tile.Lay.FloorVertices.start; i < tile.Lay.FloorVertices.start + tile.Lay.FloorVertices.length; i++)
                    {
                        var kl = TileChart.ChartToKlein(new double2(tile.V[i].x, tile.V[i].z), 1, R);
                        if (Math.Abs(math.dot(kl, e) - 1) < 1e-5) edgeVerts[k].Add(i);
                    }
                    Assert.AreEqual(17, edgeVerts[k].Count, $"edge {k}");
                }
                var anchors = new Iso2[6];
                for (int n = 0; n < 100; n++)
                {
                    int face = rng.Next(6);
                    double r = CubeTiling.Circumradius * Math.Sqrt(rng.NextDouble()), a = rng.NextDouble() * 2 * Math.PI, th = rng.NextDouble() * 2 * Math.PI;
                    var u = new Iso2((Iso2.Boost(r * new double2(Math.Cos(a), Math.Sin(a)), 1) * Iso2.Rot(th)).M, 1);
                    Anchors(Place(s, face, u), anchors, out _);
                    var mats = new float4x4[6];
                    for (int f = 0; f < 6; f++) mats[f] = ToF4(WarpAnchor.ToMatrix(anchors[f]));
                    for (int f = 0; f < 6; f++)
                        for (int k = 0; k < 4; k++)
                        {
                            int g = CubeTiling.Neighbour(f, k), be = CubeTiling.BackEdge(f, k);
                            foreach (int vi in edgeVerts[k])
                            {
                                float3 w = AtlasWarpCpu.AtlasWarp(tile.V[vi], mats[f], 2f, (float)R, Head, float4x4.identity);
                                if (SmallPlanetShading.VertexGeodesic(w, Head, (float)R) > 2.6f) continue;
                                float best = float.MaxValue;
                                foreach (int vj in edgeVerts[be])
                                {
                                    float3 w2 = AtlasWarpCpu.AtlasWarp(tile.V[vj], mats[g], 2f, (float)R, Head, float4x4.identity);
                                    best = Math.Min(best, math.length(w2.xz - w.xz));
                                }
                                worst = Math.Max(worst, best); checkedVerts++;
                                Assert.Less(best, 1e-3f, $"s={s} face {f} edge {k}");
                            }
                        }
                }
            }
            TestContext.WriteLine($"T4 worst seam gap {worst * 1000:F4} mm over {checkedVerts} vertices");
        }

        static double TriangleDiameter(TileData d, int a, int b, int c)
        {
            var p = new[] { a, b, c };
            var q = new double3[3];
            for (int i = 0; i < 3; i++) q[i] = Geometry2.Exp(new double2(d.V[p[i]].x, d.V[p[i]].z) / d.R, 1);
            return math.max(Geometry2.Distance(q[0], q[1], 1), math.max(Geometry2.Distance(q[1], q[2], 1), Geometry2.Distance(q[0], q[2], 1)));
        }

        // returns number of drawn triangles that contain the cut point; fills worst float/double g error
        static int Scan(TileData d, Iso2 anchor, float4x4 mat, ref double worstG)
        {
            int n = d.V.Length;
            var g = new float[n]; var q = new double3[n]; var gd = new double[n];
            for (int i = 0; i < n; i++)
            {
                float3 w = AtlasWarpCpu.AtlasWarp(d.V[i], mat, 2f, (float)d.R, Head, float4x4.identity);
                g[i] = SmallPlanetShading.VertexGeodesic(w, Head, (float)d.R);
                q[i] = math.mul(anchor.M, Geometry2.Exp(new double2(d.V[i].x, d.V[i].z) / d.R, 1));
                gd[i] = Geometry2.Distance(q[i], O, 1);
                if (g[i] <= 2.6f) worstG = Math.Max(worstG, Math.Abs(g[i] - gd[i]) * d.R);
            }
            var cut = new double3(0, 0, -1);
            int bad = 0;
            for (int t = 0; t < d.T.Length; t += 3)
            {
                int a = d.T[t], b = d.T[t + 1], c = d.T[t + 2];
                if (Math.Min(g[a], Math.Min(g[b], g[c])) > 2.6f) continue;
                bool contains = false;
                if (math.length(q[a] - cut) < 1e-6 || math.length(q[b] - cut) < 1e-6 || math.length(q[c] - cut) < 1e-6) contains = true;
                double d0 = math.determinant(new double3x3(q[a], q[b], cut)), d1 = math.determinant(new double3x3(q[b], q[c], cut)), d2 = math.determinant(new double3x3(q[c], q[a], cut));
                bool sameHemisphere = math.dot(q[a] + q[b] + q[c], cut) > 0; // the sign test alone also accepts -cut, the head
                if (sameHemisphere && ((d0 > 1e-12 && d1 > 1e-12 && d2 > 1e-12) || (d0 < -1e-12 && d1 < -1e-12 && d2 < -1e-12))) contains = true;
                if (contains) bad++;
            }
            return bad;
        }

        static IEnumerable<(string, double3)> HeadPoints()
        {
            for (int f = 0; f < 6; f++) yield return ($"centre{f}", CubeTiling.Frame(f).M.c2);
            for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2) for (int sz = -1; sz <= 1; sz += 2)
                        yield return ("vertex", math.normalize(new double3(sx, sy, sz)));
            for (int i = 0; i < 3; i++)
                for (int a = -1; a <= 1; a += 2) for (int b = -1; b <= 1; b += 2)
                    {
                        var v = double3.zero; v[(i + 1) % 3] = a; v[(i + 2) % 3] = b;
                        yield return ("edge", math.normalize(v));
                    }
            var rng = new Random(11);
            for (int i = 0; i < 500; i++)
            {
                var v = new double3(rng.NextDouble() - .5, rng.NextDouble() - .5, rng.NextDouble() - .5);
                if (math.length(v) < 1e-3) continue;
                yield return ("random", math.normalize(v));
            }
        }

        // T5. Editor proxy for "no smeared triangle in a 360 deg look-around" (§9.5.6), §10.2, R6 §2.5, kernel.md §6.3
        [Test]
        public void SmallPlanet_NoSmearBeyondDiscard()
        {
            double limit = Math.PI - SmallPlanetConstants.DiscardRad;
            double worstDiam = 0;
            foreach (double s in LapSides)
            {
                double R = 2 * s / Math.PI;
                foreach (var d in new[] { BuildTile(R), BuildBeacon(R) })
                    for (int t = 0; t < d.T.Length; t += 3)
                        worstDiam = Math.Max(worstDiam, TriangleDiameter(d, d.T[t], d.T[t + 1], d.T[t + 2]));
            }
            TestContext.WriteLine($"T5a worst triangle geodesic diameter {worstDiam:F4} rad (limit {limit:F4})");
            Assert.Less(worstDiam, limit);

            double worstG = 0; int poses = 0, drawnBad = 0;
            var rng = new Random(5);
            var anchors = new Iso2[6];
            foreach (double s in new[] { 1.0, 2.0, 3.2 })
            {
                double R = 2 * s / Math.PI;
                var tile = BuildTile(R); var beacon = BuildBeacon(R);
                foreach (var (name, w) in HeadPoints())
                {
                    var u = PoseTowards(w, name.StartsWith("centre") ? 0 : rng.NextDouble() * 2 * Math.PI);
                    Anchors(Place(s, 0, u), anchors, out var ba);
                    for (int f = 0; f < 6; f++) drawnBad += Scan(tile, anchors[f], ToF4(WarpAnchor.ToMatrix(anchors[f])), ref worstG);
                    drawnBad += Scan(beacon, ba, ToF4(WarpAnchor.ToMatrix(ba)), ref worstG);
                    poses++;
                }
            }
            TestContext.WriteLine($"T5b {poses} poses: drawn triangles containing the cut point {drawnBad}; worst |g_float - g_double| {worstG * 1000:F4} mm");
            Assert.AreEqual(0, drawnBad);
            Assert.LessOrEqual(worstG, 1e-3);

            // (c) regression for the LogS2 antipode branch (§3.6)
            foreach (double s in new[] { 1.0, 2.0, 3.2 })
            {
                double R = 2 * s / Math.PI;
                var tile = BuildTile(R);
                Anchors(Place(s, 0, new Iso2(double3x3.identity, 1)), anchors, out _);
                int vi = -1;
                for (int i = tile.Lay.FloorVertices.start; i < tile.Lay.FloorVertices.start + tile.Lay.FloorVertices.length; i++)
                    if (math.length(tile.V[i].xz) < 1e-6f) vi = i;
                Assert.GreaterOrEqual(vi, 0);
                float3 w = AtlasWarpCpu.AtlasWarp(tile.V[vi], ToF4(WarpAnchor.ToMatrix(anchors[5])), 2f, (float)R, Head, float4x4.identity);
                float g = SmallPlanetShading.VertexGeodesic(w, Head, (float)R);
                TestContext.WriteLine($"T5c s={s} centre vertex of face 5 g = {g:F5}");
                Assert.GreaterOrEqual(g, Math.PI - 1e-3);
            }
        }

        // T6. §3.6; kernel.md §4.3
        [Test]
        public void Warp_LogS2Antipode()
        {
            Assert.AreEqual(Math.PI, math.length(AtlasWarpCpu.LogS2(new float3(0, 0, -1))), 1e-6);
            Assert.AreEqual(Math.PI, math.length(AtlasWarpCpu.LogS2(new float3(1e-7f, 0, -1))), 1e-6);
            var p = new float3(0.3f, 0.2f, (float)Math.Sqrt(1 - 0.13));
            double2 want = Geometry2.Log(new double3(p), 1);
            Assert.Less(math.length((double2)AtlasWarpCpu.LogS2(p) - want), 1e-6);
            string src = Hlsl("AtlasWarp.hlsl");
            StringAssert.IsMatch(@"LogS2[\s\S]*p\.z\s*<\s*0", src);
        }

        static string Hlsl(string file)
        {
            string src = File.ReadAllText(Path.Combine(ProjectDir, "Assets/Atlas/Rendering/Shaders/Resources", file));
            src = Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline);
            return Regex.Replace(src, @"//[^\n]*", "");
        }

        // T6b. §9.5.6 "fragments beyond 2.6 rad discarded" in the shared shader: spherical mode only, in units of _HypL = R, in the fragment stage
        [Test]
        public void Warp_SphericalDiscardIsInTheShader()
        {
            string warp = Hlsl("AtlasWarp.hlsl");
            StringAssert.IsMatch(@"void\s+AtlasWarpClip\s*\(\s*float3\s+positionWS\s*\)[\s\S]*_WarpMode\s*>\s*1\.5[\s\S]*clip\s*\(\s*2\.6\s*-\s*length\s*\(\s*positionWS\.xz\s*-\s*_HeadPosWS\.xz\s*\)\s*/\s*_HypL\s*\)", warp);
            Assert.AreEqual(2.6, SmallPlanetConstants.DiscardRad, "the CPU reference and the shader agree");
            StringAssert.IsMatch(@"AtlasWarpClip\s*\(\s*input\.positionWS\s*\)", Hlsl("AtlasSurface.shader"));
        }

        // T7. §3.5 pole beacon; R6 §2.6, §5.3; Spec §6.3
        [Test]
        public void SmallPlanet_PoleBeacon()
        {
            var mesh = new AtlasMeshBuilder(32, 96);
            int tris = PoleBeaconMesh.Build(mesh);
            Assert.AreEqual(24, tris); Assert.LessOrEqual(tris, 500);
            Assert.AreEqual(24, mesh.TriangleCount);
            Assert.AreEqual(17, mesh.VertexCount);
            Assert.AreEqual(SmallPlanetConstants.BeaconHeight, mesh.BoundsSize.y, 1e-6);
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                Assert.AreEqual(AtlasPalette.Uv(AtlasPalette.Beacon), mesh.Vertices[i].Uv0);
                Assert.AreEqual(0, mesh.Vertices[i].LineWeight);
            }
            var v = mesh.Vertices; var t = mesh.Indices;
            for (int i = 0; i < mesh.IndexCount; i += 3)
            {
                float3 n = math.cross(v[t[i + 1]].Position - v[t[i]].Position, v[t[i + 2]].Position - v[t[i]].Position);
                float3 c = (v[t[i]].Position + v[t[i + 1]].Position + v[t[i + 2]].Position) / 3f;
                var outward = new float3(c.x, 0, c.z);
                Assert.IsTrue(math.dot(n, outward) > 0 || n.y > 0, $"triangle {i / 3} faces outward");
            }
            double inscribed = SmallPlanetConstants.BeaconRadius * Math.Cos(Math.PI / 8);
            Assert.Greater(inscribed, TileChart.PostRadius);
            Assert.LessOrEqual(SmallPlanetConstants.BeaconRadius, 0.3 * (2 * 1.0 / Math.PI));

            double3 bw = math.mul((CubeTiling.Frame(5) * SmallPlanetCore.BeaconPlacement).M, O);
            Assert.Less(math.length(bw - new double3(1, -1, -1) / Math.Sqrt(3)), 1e-12);
            var d = new List<double>();
            for (int f = 0; f < 6; f++)
            {
                double dist = Geometry2.Distance(bw, CubeTiling.Frame(f).M.c2, 1);
                d.Add(dist);
                Assert.LessOrEqual(dist, 0.8 * Math.PI);
                Assert.GreaterOrEqual(Geometry2.Distance(bw, CubeTiling.Frame(CubeTiling.Antipode(f)).M.c2, 1), 0.3);
            }
            d.Sort();
            TestContext.WriteLine("T7 beacon distances to face centres: " + string.Join(", ", d.ConvertAll(x => x.ToString("F4"))));
            for (int i = 0; i < 3; i++) { Assert.AreEqual(0.9553, d[i], 1e-4); Assert.AreEqual(2.1863, d[i + 3], 1e-4); }

            Anchors(Place(2.0, 0, new Iso2(double3x3.identity, 1)), new Iso2[6], out var ba);
            Assert.Less(math.length(math.mul(ba.M, O) - new double3(1, -1, -1) / Math.Sqrt(3)), 1e-12);
        }

        // T10. §8.3, §8.5 (S4 <= 70 draw calls, <= 190k triangles), §8.4 Near cap 1,200
        [Test]
        public void SmallPlanet_Budget()
        {
            foreach (double s in LapSides)
            {
                int T = BuildTile(2 * s / Math.PI).Lay.TriangleCount;
                Assert.LessOrEqual(T, 1200);
                int draws = 2 * 7, tris = 2 * (6 * T + 24);
                TestContext.WriteLine($"T10 s={s}: tile triangles {T}, planet draws {draws}, planet triangles {tris}");
                Assert.LessOrEqual(draws, 70); Assert.LessOrEqual(tris, 190000);
            }
        }

#if !ATLAS_HEADLESS
        // T10 (view). §8.3: seven renderers, one pooled instance each, one shared tile mesh, huge bounds; everything goes back to the pool
        [Test, Category("Unity")]
        public void SmallPlanet_View()
        {
            var pool = MaterialPool.CreateDefault(16);
            var go = new GameObject("view");
            try
            {
                var view = go.AddComponent<SmallPlanetView>();
                var anchors = new Iso2[6];
                foreach (double s in LapSides)
                {
                    double R = 2 * s / Math.PI;
                    view.Build(R, pool);
                    Assert.AreEqual(7, pool.Rented);
                    Assert.AreEqual(6, view.TileRenderers.Count);
                    Assert.IsNotNull(view.BeaconRenderer);
                    var mats = new HashSet<Material>();
                    var all = new List<MeshRenderer>(view.TileRenderers) { view.BeaconRenderer };
                    foreach (var r in all)
                    {
                        Assert.IsTrue(r.enabled);
                        Assert.AreEqual(new Vector3(1000, 1000, 1000), r.localBounds.size);
                        Assert.IsTrue(pool.Owns(r.sharedMaterial));
                        Assert.AreEqual(AtlasWarpCpu.ModeSpherical, r.sharedMaterial.GetFloat(AtlasShaderIDs.WarpMode));
                        mats.Add(r.sharedMaterial);
                    }
                    Assert.AreEqual(7, mats.Count);
                    var m0 = view.TileRenderers[0].GetComponent<MeshFilter>().sharedMesh;
                    for (int f = 1; f < 6; f++) Assert.AreSame(m0, view.TileRenderers[f].GetComponent<MeshFilter>().sharedMesh);
                    Assert.AreEqual(view.TileTriangles, (int)m0.GetIndexCount(0) / 3);
                    Assert.AreEqual(24, (int)view.BeaconRenderer.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0) / 3);
                    Anchors(Place(s, 0, new Iso2(double3x3.identity, 1)), anchors, out Iso2 beacon);
                    view.Upload(anchors, beacon);
                    Assert.AreEqual(WarpAnchor.ToMatrix(anchors[3]), view.TileRenderers[3].sharedMaterial.GetMatrix(AtlasShaderIDs.Anchor));
                    view.Shown = false;
                    Assert.IsFalse(view.BeaconRenderer.gameObject.activeSelf);
                    view.SetRadius(R * 1.5);
                    Assert.AreSame(m0, view.TileRenderers[0].GetComponent<MeshFilter>().sharedMesh, "rebuilt in place");
                    view.Release();
                    Assert.AreEqual(0, go.transform.childCount);
                    Assert.AreEqual(0, pool.Rented);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                pool.Dispose();
            }
        }
#endif
    }
}
