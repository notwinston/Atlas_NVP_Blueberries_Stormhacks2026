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

        // 17 points along edge k of a tile chart (Klein coordinate 1 along the edge's normal), corners included.
        static float3[] EdgePoints(int k, double R)
        {
            var e = new double2(k == 0 ? 1 : k == 2 ? -1 : 0, k == 1 ? 1 : k == 3 ? -1 : 0);
            var along = new double2(-e.y, e.x);
            var points = new float3[17];
            for (int i = 0; i < points.Length; i++)
            {
                double2 x = TileChart.KleinToChart(e + (i / 8.0 - 1) * along, 1, R);
                points[i] = new float3((float)x.x, 0f, (float)x.y);
            }
            return points;
        }

        // T4. P1-B05 seam criterion on the {4,3} frames (§3.4, §9.5.6 P1-B05): the warp of edge k of every face meets the warp
        // of the neighbour's back edge, the chain and the shader's spherical map agreeing to a millimetre.
        [Test]
        public void SmallPlanet_TileSeams()
        {
            var rng = new Random(20261003);
            double worst = 0; int checkedVerts = 0;
            foreach (double s in new[] { 1.0, 2.0, 3.2 })
            {
                double R = 2 * s / Math.PI;
                var edgeVerts = new float3[4][];
                for (int k = 0; k < 4; k++) edgeVerts[k] = EdgePoints(k, R);
                var anchors = new Iso2[6];
                for (int n = 0; n < 100; n++)
                {
                    int face = rng.Next(6);
                    double r = CubeTiling.Circumradius * Math.Sqrt(rng.NextDouble()), a = rng.NextDouble() * 2 * Math.PI, th = rng.NextDouble() * 2 * Math.PI;
                    var u = new Iso2((Iso2.Boost(r * new double2(Math.Cos(a), Math.Sin(a)), 1) * Iso2.Rot(th)).M, 1);
                    Anchors(Place(s, face, u), anchors);
                    var mats = new float4x4[6];
                    for (int f = 0; f < 6; f++) mats[f] = ToF4(WarpAnchor.ToMatrix(anchors[f]));
                    for (int f = 0; f < 6; f++)
                        for (int k = 0; k < 4; k++)
                        {
                            int g = CubeTiling.Neighbour(f, k), be = CubeTiling.BackEdge(f, k);
                            foreach (float3 vi in edgeVerts[k])
                            {
                                float3 w = AtlasWarpCpu.AtlasWarp(vi, mats[f], 2f, (float)R, Head, float4x4.identity);
                                if (SmallPlanetShading.VertexGeodesic(w, Head, (float)R) > 2.6f) continue;
                                float best = float.MaxValue;
                                foreach (float3 vj in edgeVerts[be])
                                {
                                    float3 w2 = AtlasWarpCpu.AtlasWarp(vj, mats[g], 2f, (float)R, Head, float4x4.identity);
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

        static IEnumerable<(string, double3)> HeadPoints(int randomPoints)
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
            for (int i = 0; i < randomPoints; i++)
            {
                var v = new double3(rng.NextDouble() - .5, rng.NextDouble() - .5, rng.NextDouble() - .5);
                if (math.length(v) < 1e-3) continue;
                yield return ("random", math.normalize(v));
            }
        }

        // T5. Editor proxy for "no smeared triangle in a 360 deg look-around" (§9.5.6), §10.2, R6 §2.5, kernel.md §6.3, on the
        // PlanetKit tiles: xz scales with R, so a triangle's geodesic diameter (xz / R) is the same at every radius.
        [Test]
        public void SmallPlanet_NoSmearBeyondDiscard()
        {
            PlanetKitTiles kit = PlanetKitTests.Shipped();
            double limit = Math.PI - SmallPlanetConstants.DiscardRad;
            double worstDiam = 0;
            var tiles = new TileData[6];
            double R = SmallPlanetConstants.KitRadius;
            for (int f = 0; f < 6; f++)
            {
                tiles[f] = BuildKitTile(kit, f, R);
                TileData d = tiles[f];
                for (int t = 0; t < d.T.Length; t += 3)
                    worstDiam = Math.Max(worstDiam, TriangleDiameter(d, d.T[t], d.T[t + 1], d.T[t + 2]));
            }
            TestContext.WriteLine($"T5a worst triangle geodesic diameter {worstDiam:F4} rad (limit {limit:F4})");
            Assert.Less(worstDiam, limit);

            double worstG = 0; int poses = 0, drawnBad = 0;
            var rng = new Random(5);
            var anchors = new Iso2[6];
            double s = R * Math.PI / 2;
            foreach (var (name, w) in HeadPoints(60))
            {
                var u = PoseTowards(w, name.StartsWith("centre") ? 0 : rng.NextDouble() * 2 * Math.PI);
                Anchors(Place(s, 0, u), anchors);
                for (int f = 0; f < 6; f++) drawnBad += Scan(tiles[f], anchors[f], ToF4(WarpAnchor.ToMatrix(anchors[f])), ref worstG);
                poses++;
            }
            TestContext.WriteLine($"T5b {poses} poses: drawn triangles containing the cut point {drawnBad}; worst |g_float - g_double| {worstG * 1000:F4} mm");
            Assert.AreEqual(0, drawnBad);
            Assert.LessOrEqual(worstG, 1e-3);

            // (c) regression for the LogS2 antipode branch (§3.6): the centre of face 5, seen from the centre of face 0
            foreach (double side in new[] { 1.0, 2.0, 3.2 })
            {
                double r = 2 * side / Math.PI;
                Anchors(Place(side, 0, new Iso2(double3x3.identity, 1)), anchors);
                float3 wc = AtlasWarpCpu.AtlasWarp(float3.zero, ToF4(WarpAnchor.ToMatrix(anchors[5])), 2f, (float)r, Head, float4x4.identity);
                float g = SmallPlanetShading.VertexGeodesic(wc, Head, (float)r);
                TestContext.WriteLine($"T5c s={side} centre of face 5 g = {g:F5}");
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

        // The sky (SmallPlanetSkyMesh): beyond the fogged planet and inside the eye cameras' far plane, above the horizon,
        // palette colours only (the sun emissive), no lines; one sun and the clouds, under two thousand triangles.
        [Test]
        public void SmallPlanet_Sky()
        {
            var sun = new AtlasMeshBuilder(64, 192);
            int sunTris = SmallPlanetSkyMesh.BuildSun(sun);
            Assert.AreEqual(3 * SmallPlanetSkyMesh.SunSegments, sunTris);
            var clouds = new AtlasMeshBuilder(4096, 4096);
            int cloudTris = SmallPlanetSkyMesh.BuildClouds(clouds);
            int puffs = 0;
            foreach (var c in SmallPlanetSkyMesh.Clouds) puffs += c.Puffs;
            Assert.AreEqual(puffs * SmallPlanetSkyMesh.PuffTriangles, cloudTris);
            Assert.LessOrEqual(sunTris + cloudTris, 2000);

            double fogged = SmallPlanetConstants.FogOpaqueRad * SmallPlanetCore.MaxRadiusM;
            var sunCells = new[] { AtlasPalette.Uv(SmallPlanetSkyMesh.SunCell), AtlasPalette.Uv(SmallPlanetSkyMesh.SunHaloCell) };
            var cloudCells = new[] { AtlasPalette.Uv(SmallPlanetSkyMesh.CloudCell), AtlasPalette.Uv(SmallPlanetSkyMesh.CloudBaseCell) };
            foreach (var mesh in new[] { sun, clouds })
            {
                var cells = mesh == sun ? sunCells : cloudCells;
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    AtlasVertex v = mesh.Vertices[i];
                    float d = math.length(v.Position);
                    Assert.Greater(d, fogged + 10, "beyond the planet");
                    Assert.Less(d, StereoRig.FarPlane - 2, "inside the far plane");
                    Assert.Greater(v.Position.y, 5f, "above the horizon");
                    Assert.AreEqual(0, v.LineWeight, "no lines");
                    Assert.IsTrue(v.Uv0.Equals(cells[0]) || v.Uv0.Equals(cells[1]), "palette cell, vertex " + i);
                }
            }
            Assert.IsTrue(AtlasPalette.IsEmissive(SmallPlanetSkyMesh.SunCell) && AtlasPalette.IsEmissive(SmallPlanetSkyMesh.SunHaloCell));
            Assert.IsTrue(AtlasPalette.IsEmissive(SmallPlanetSkyMesh.CloudCell));
            Assert.IsFalse(AtlasPalette.IsEmissive(SmallPlanetSkyMesh.CloudBaseCell), "the base takes the baked shade");

            // The horizon is the Blender preview's sky (art/blender/smallplanet_build.py, SKY = "BFE3F5"); the zenith is deeper.
            string build = File.ReadAllText(Path.Combine(ProjectDir, "..", "art", "blender", "smallplanet_build.py"));
            Match sky = Regex.Match(build, "^SKY\\s*=\\s*\"([0-9A-Fa-f]{6})\"", RegexOptions.Multiline);
            Assert.IsTrue(sky.Success, "SKY in smallplanet_build.py");
            int hex = Convert.ToInt32(sky.Groups[1].Value, 16);
            float3 want = new float3((hex >> 16) & 255, (hex >> 8) & 255, hex & 255) / 255f;
            Assert.Less(math.cmax(math.abs(SmallPlanetSkyMesh.HorizonSrgb - want)), 1e-6f);
            float3 lum = new float3(0.2126f, 0.7152f, 0.0722f);
            Assert.Less(math.dot(SmallPlanetSkyMesh.ZenithLinear, lum), math.dot(SmallPlanetSkyMesh.HorizonLinear, lum));
            Assert.Greater(SmallPlanetSkyMesh.SunDirection.y, 0.3f);
        }
    }
}
