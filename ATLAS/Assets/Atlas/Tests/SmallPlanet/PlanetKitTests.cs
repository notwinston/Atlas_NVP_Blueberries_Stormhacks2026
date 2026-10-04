using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
#if !ATLAS_HEADLESS
using UnityEngine;
using UnityEngine.TestTools;
#endif
using static Atlas.Spaces.SmallPlanet.Tests.SmallPlanetTestUtil;

namespace Atlas.Spaces.SmallPlanet.Tests
{
    /// <summary>
    /// The Small Planet tiles modelled in Blender (PlanetKit.md): loaded through KitSet, scaled by R, refused as a
    /// whole when a file is missing or altered, and drawn by the view in place of the procedural tiles and beacon.
    /// </summary>
    public class PlanetKitTests
    {
        static string KitDirectory =>
            Path.Combine(ProjectDir, "Assets", "Atlas", "Spaces", "SmallPlanet", "Resources", PlanetKitTiles.ResourceFolder);

        // The reader SmallPlanetView uses, with the disk in place of Resources; edit may replace a file's bytes.
        static Func<string, byte[]> DiskReader(Func<string, byte[], byte[]> edit = null)
        {
            return name =>
            {
                string path = Path.Combine(KitDirectory, name == "manifest" ? "manifest.json" : name + ".bytes");
                byte[] bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
                return edit == null ? bytes : edit(name, bytes);
            };
        }

        static JsonValue Manifest() => AtlasJson.ParseUtf8(File.ReadAllBytes(Path.Combine(KitDirectory, "manifest.json")));

        internal static PlanetKitTiles Shipped()
        {
            PlanetKitTiles kit = PlanetKitTiles.Load(DiskReader(), out string error);
            Assert.IsNotNull(kit, error);
            return kit;
        }

        [Test]
        public void PlanetKit_LoadsSixTiles_WithTheManifestsTriangleCounts()
        {
            PlanetKitTiles kit = Shipped();
            JsonValue manifest = Manifest();
            JsonValue pieces = manifest["pieces"];
            Assert.AreEqual(CubeTiling.FaceCount, pieces.Count);
            int total = 0;
            for (int f = 0; f < CubeTiling.FaceCount; f++)
            {
                string name = PlanetKitTiles.TileName(f);
                Assert.AreEqual(pieces[name].GetInt("triangles", -1), kit.Triangles(f), name);
                Assert.That(kit.Triangles(f), Is.InRange(1, PlanetKitTiles.MaxTileTriangles), name);
                total += kit.Triangles(f);
                TestContext.WriteLine($"{name} ({manifest["planet"]["tiles"][f].AsString()}): {kit.Triangles(f)} triangles, {kit.Tile(f).VertexCount} vertices");
            }
            Assert.AreEqual(total, kit.TotalTriangles);
            Assert.AreEqual(manifest["planet"].GetDouble("R0"), kit.AuthoredRadius, 0.0);
            Assert.AreEqual(SmallPlanetConstants.KitRadius, kit.AuthoredRadius, 1e-6, "planet.R's default is the kit's R0");
            Assert.AreEqual(PlanetKitTiles.HalfWidthR, manifest["planet"].GetDouble("half_width_R"), 1e-6);
            Assert.AreEqual("Home", manifest["planet"]["tiles"][0].AsString(), "tile0 is CubeTiling face 0");
            Assert.AreEqual("Beacon", manifest["planet"]["tiles"][SmallPlanetConstants.BeaconFace].AsString(), "the cairn's tile is the beacon face");
        }

        // PlanetKit.md: xz times R, y as authored; the ground reaches the tile edge at pi/4 R on both chart axes,
        // and props overhang it by at most 0.062 R (0.07 R allowed).
        [Test]
        public void PlanetKit_ScaledByR_TilesSpanPiOver4R_HeightsStayMetres()
        {
            PlanetKitTiles kit = Shipped();
            var into = new AtlasMeshBuilder(kit.MaxVertices, kit.MaxIndices);
            AtlasVertex[] vertices = into.Vertices;
            int[] indices = into.Indices;
            foreach (double radius in new[] { 1.2, SmallPlanetConstants.KitRadius, SmallPlanetCore.MaxRadiusM })
            {
                float r = (float)radius;
                double half = PlanetKitTiles.HalfWidthR * radius;
                for (int f = 0; f < CubeTiling.FaceCount; f++)
                {
                    string at = $"tile{f} at R = {radius}";
                    MeshData source = kit.Tile(f);
                    kit.Scale(f, radius, into);
                    Assert.AreSame(vertices, into.Vertices, "no allocation, " + at);
                    Assert.AreSame(indices, into.Indices, "no allocation, " + at);
                    Assert.AreEqual(source.VertexCount, into.VertexCount, at);
                    Assert.AreEqual(source.TriangleCount, into.TriangleCount, at);

                    float3 min = new float3(float.MaxValue), max = new float3(float.MinValue);
                    for (int v = 0; v < into.VertexCount; v++)
                    {
                        AtlasVertex a = source.Vertices[v], b = into.Vertices[v];
                        Assert.AreEqual(a.Position.x * r, b.Position.x, at);
                        Assert.AreEqual(a.Position.y, b.Position.y, "heights are metres, " + at);
                        Assert.AreEqual(a.Position.z * r, b.Position.z, at);
                        Assert.AreEqual(1f, math.length(b.Normal), 1e-4f, at);
                        if (radius == SmallPlanetConstants.KitRadius) Assert.Less(math.distance(a.Normal, b.Normal), 1e-4f, at);
                        Assert.AreEqual(a.Uv0, b.Uv0, at);
                        Assert.AreEqual(a.Uv1, b.Uv1, at);
                        Assert.AreEqual(a.Color, b.Color, at);
                        min = math.min(min, b.Position);
                        max = math.max(max, b.Position);
                    }
                    for (int i = 0; i < into.IndexCount; i++) Assert.AreEqual(source.Indices[i], into.Indices[i], at);
                    Assert.Less(math.distance(min, into.BoundsMin), 1e-4f, at);
                    Assert.Less(math.distance(max, into.BoundsMax), 1e-4f, at);

                    double lo = half - 1e-4 * radius, hi = half + 0.07 * radius;
                    Assert.That((double)max.x, Is.InRange(lo, hi), "+x edge, " + at);
                    Assert.That((double)-min.x, Is.InRange(lo, hi), "-x edge, " + at);
                    Assert.That((double)max.z, Is.InRange(lo, hi), "+z edge, " + at);
                    Assert.That((double)-min.z, Is.InRange(lo, hi), "-z edge, " + at);
                    Assert.That((double)min.y, Is.GreaterThan(-0.2), at);
                    Assert.That((double)max.y, Is.LessThan(1.5), at);
                }
            }
        }

        [Test]
        public void PlanetKit_IsRefusedAsAWhole_WhenAFileIsMissingAlteredOrShort()
        {
            AssertRefused(null, "no reader");
            AssertRefused(_ => null, "nothing in the folder");
            AssertRefused(DiskReader((name, bytes) => name == "tile3.glb" ? null : bytes), "tile3 missing");
            AssertRefused(DiskReader((name, bytes) =>
            {
                if (name != "tile3.glb" || bytes == null) return bytes;
                var copy = (byte[])bytes.Clone();
                copy[copy.Length / 2] ^= 0x5A;
                return copy;
            }), "tile3 altered");
            AssertRefused(DiskReader((name, bytes) => name == "manifest" ? Edit(bytes, "\"R0\"", "\"R_0\"") : bytes), "no planet.R0");
            AssertRefused(DiskReader((name, bytes) => name == "manifest" ? Edit(bytes, ",\\s*\"tile5\"\\s*:\\s*\\{[^}]*\\}", "") : bytes), "five tiles");
            AssertRefused(DiskReader((name, bytes) => name == "manifest" ? Encoding.UTF8.GetBytes("{\"pieces\": ") : bytes), "manifest cut short");
        }

        static byte[] Edit(byte[] manifest, string pattern, string replacement)
        {
            string text = Encoding.UTF8.GetString(manifest);
            string edited = Regex.Replace(text, pattern, replacement);
            Assert.AreNotEqual(text, edited, "the edit " + pattern + " applies");
            return Encoding.UTF8.GetBytes(edited);
        }

        static void AssertRefused(Func<string, byte[]> read, string why)
        {
            PlanetKitTiles kit = PlanetKitTiles.Load(read, out string error);
            Assert.IsNull(kit, why);
            Assert.That(error, Is.Not.Null.And.Not.Empty, why);
            TestContext.WriteLine(why + ": " + error);
        }

        // T10 (kit). §8.5: S4 <= 70 draw calls and <= 190k triangles, both eyes; six tile draws an eye, no beacon.
        [Test]
        public void PlanetKit_Budget()
        {
            PlanetKitTiles kit = Shipped();
            for (int f = 0; f < CubeTiling.FaceCount; f++) Assert.LessOrEqual(kit.Triangles(f), PlanetKitTiles.MaxTileTriangles);
            int draws = 2 * CubeTiling.FaceCount, tris = 2 * kit.TotalTriangles;
            TestContext.WriteLine($"T10 kit: planet draws {draws}, planet triangles {tris}");
            Assert.LessOrEqual(draws, 70);
            Assert.LessOrEqual(tris, 190000);
        }

#if !ATLAS_HEADLESS
        [Test, Category("Unity")]
        public void PlanetKit_LoadsFromResources_AsTheViewLoadsIt()
        {
            PlanetKitTiles kit = PlanetKitTiles.Load(SmallPlanetView.ReadKitResource, out string error);
            Assert.IsNotNull(kit, error);
            PlanetKitTiles disk = Shipped();
            for (int f = 0; f < CubeTiling.FaceCount; f++) Assert.AreEqual(disk.Triangles(f), kit.Triangles(f));
            Assert.AreEqual(disk.AuthoredRadius, kit.AuthoredRadius, 0.0);
            Assert.IsNotNull(SmallPlanetView.SharedKit(), "the view finds the kit in Resources");
        }

        // §8.3 with the kit: six renderers, one pooled instance and one mesh each, no beacon; rescaled in place.
        [Test, Category("Unity")]
        public void SmallPlanet_View_WithTheKit_DrawsSixTiles_AndNoPoleBeacon()
        {
            PlanetKitTiles kit = Shipped();
            var pool = MaterialPool.CreateDefault(16);
            var go = new GameObject("view");
            try
            {
                var view = go.AddComponent<SmallPlanetView>();
                double radius = SmallPlanetConstants.KitRadius;
                view.Build(radius, pool, kit);
                Assert.IsTrue(view.UsesKit);
                Assert.IsNull(view.BeaconRenderer, "the kit's cairn is the beacon");
                Assert.AreEqual(6, pool.Rented);
                Assert.AreEqual(6, view.TileRenderers.Count);
                Assert.AreEqual(6, go.transform.childCount);
                Assert.AreEqual(kit.TotalTriangles, view.PlanetTriangles);
                var meshes = new HashSet<Mesh>();
                var mats = new HashSet<Material>();
                for (int f = 0; f < 6; f++)
                {
                    MeshRenderer r = view.TileRenderers[f];
                    Mesh mesh = r.GetComponent<MeshFilter>().sharedMesh;
                    meshes.Add(mesh);
                    mats.Add(r.sharedMaterial);
                    Assert.AreEqual(kit.Triangles(f), (int)mesh.GetIndexCount(0) / 3, "tile " + f);
                    Assert.AreEqual(kit.Tile(f).VertexCount, mesh.vertexCount, "tile " + f);
                    Assert.AreEqual(new Vector3(1000, 1000, 1000), r.localBounds.size);
                    Assert.AreEqual(AtlasWarpCpu.ModeSpherical, r.sharedMaterial.GetFloat(AtlasShaderIDs.WarpMode));
                    Assert.AreEqual(kit.Tile(f).BoundsMax.x * (float)radius, mesh.bounds.max.x, 1e-4f, "tile " + f);
                    Assert.AreEqual(kit.Tile(f).BoundsMax.y, mesh.bounds.max.y, 1e-4f, "tile " + f);
                }
                Assert.AreEqual(6, meshes.Count, "one mesh a face");
                Assert.AreEqual(6, mats.Count);

                var anchors = new Iso2[6];
                Anchors(Place(radius * Math.PI / 2, 0, new Iso2(double3x3.identity, 1)), anchors, out Iso2 beacon);
                view.Upload(anchors, beacon);
                Assert.AreEqual(WarpAnchor.ToMatrix(anchors[5]), view.TileRenderers[5].sharedMaterial.GetMatrix(AtlasShaderIDs.Anchor));

                view.SetRadius(radius * 1.5);
                for (int f = 0; f < 6; f++)
                {
                    Mesh mesh = view.TileRenderers[f].GetComponent<MeshFilter>().sharedMesh;
                    Assert.IsTrue(meshes.Contains(mesh), "rebuilt in place");
                    Assert.AreEqual(kit.Tile(f).BoundsMax.x * (float)(radius * 1.5), mesh.bounds.max.x, 1e-4f, "tile " + f);
                    Assert.AreEqual(kit.Tile(f).BoundsMax.y, mesh.bounds.max.y, 1e-4f, "heights stay, tile " + f);
                }

                view.Shown = false;
                for (int f = 0; f < 6; f++) Assert.IsFalse(view.TileRenderers[f].gameObject.activeSelf);
                view.Shown = true;
                Assert.IsNull(view.BeaconRenderer, "showing the planet brings no beacon back");

                view.Release();
                Assert.AreEqual(0, go.transform.childCount);
                Assert.AreEqual(0, pool.Rented);
                foreach (Mesh mesh in meshes) Assert.IsTrue(mesh == null, "the tile meshes are destroyed");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                pool.Dispose();
            }
        }

        // A kit that is refused: one warning, then the procedural planet exactly as before (shared tile mesh and the pole beacon).
        [Test, Category("Unity")]
        public void SmallPlanet_View_FallsBackToProceduralTiles_WhenTheKitIsRefused()
        {
            LogAssert.Expect(LogType.Warning, new Regex("Small Planet kit tiles are missing or invalid"));
            PlanetKitTiles kit = SmallPlanetView.LoadKit(DiskReader((name, bytes) => name == "tile2.glb" ? null : bytes));
            Assert.IsNull(kit);

            var pool = MaterialPool.CreateDefault(16);
            var go = new GameObject("view");
            try
            {
                var view = go.AddComponent<SmallPlanetView>();
                view.Build(1.5, pool, kit);
                Assert.IsFalse(view.UsesKit);
                Assert.AreEqual(7, pool.Rented);
                Assert.IsNotNull(view.BeaconRenderer);
                Mesh m0 = view.TileRenderers[0].GetComponent<MeshFilter>().sharedMesh;
                for (int f = 1; f < 6; f++) Assert.AreSame(m0, view.TileRenderers[f].GetComponent<MeshFilter>().sharedMesh);
                Assert.LessOrEqual(view.TileTriangles, TileChart.TriangleCap(TileLod.Near));
                view.Release();
                Assert.AreEqual(0, pool.Rented);
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
