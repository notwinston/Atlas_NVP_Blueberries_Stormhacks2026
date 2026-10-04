using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Telemetry.Tests
{
    /// <summary>
    /// The built-in fallback set (Master Plan section 4.5, task P1-D12; docs/contracts/lane-d-link.md
    /// section 4.4): the files tools/gen_fallback_set.py writes, and the meshes FallbackMeshBuilder makes of them.
    /// </summary>
    public class FallbackSetTests
    {
        static IEnumerable<ObjectCategory> Categories()
        {
            for (int i = 0; i < ObjectCategories.Count; i++)
                if (FallbackSet.HasTemplate((ObjectCategory)i)) yield return (ObjectCategory)i;
        }

        // A box of each kind an object can be: a thing on a desk, furniture, the largest furniture of a room.
        static readonly float3[] s_sizes =
        {
            new float3(0.06f, 0.03f, 0.11f), new float3(0.3f, 0.2f, 0.25f), new float3(0.5f, 0.9f, 0.5f),
            new float3(1.2f, 0.75f, 0.8f), new float3(2.2f, 0.9f, 0.95f), new float3(2.0f, 0.5f, 1.6f), new float3(1.0f, 2.0f, 0.4f)
        };

        [Test]
        public void Fallback_TheSetHasOneTemplatePerCategory()
        {
            FallbackSet set = AssetFixtures.LoadFallback();
            Assert.That(set.LastError, Is.Null);
            Assert.That(set.Count, Is.EqualTo(21));
            foreach (ObjectCategory category in Categories())
            {
                FallbackTemplate template = set.Get(category);
                Assert.That(template, Is.Not.Null, category.ToString());
                Assert.That(template.Name, Is.EqualTo(ObjectCategories.Name(category)));
                Assert.That(template.PaletteCell, Is.InRange(64, 127), "an object colour, not the placeholder's tint");
            }
            Assert.That(set.Get(ObjectCategory.Unknown), Is.Null);
            Assert.That(set.Get(ObjectCategory.Other), Is.Null);
        }

        [TestCaseSource(nameof(Categories))]
        public void Fallback_Template_MeetsTheTemplateRow(ObjectCategory category)
        {
            string name = ObjectCategories.Name(category);
            byte[] glb = AssetFixtures.ReadFallback(name + ".glb");
            Assert.That(glb, Is.Not.Null, name + ".glb.bytes is missing");

            GlbReport report = GlbValidator.ValidateTemplate(glb);
            Assert.That(report.Ok, Is.True, "rejected with " + report.FailedCheck);
            Assert.That(report.Triangles, Is.InRange(1, 300));
            Assert.That(report.BoundsX, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(report.BoundsY, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(report.BoundsZ, Is.EqualTo(1f).Within(1e-5f));

            // A template is not a Forge asset: it has no colour and far too few triangles for the asset contract.
            GlbExpectation unit = AssetFixtures.Expect(new float3(1f, 1f, 1f), 5000);
            Assert.That(GlbValidator.Validate(glb, in unit).FailedCheck, Is.EqualTo("attributes"));

            JsonValue manifest = AtlasJson.ParseUtf8(AssetFixtures.ReadFallback(FallbackSet.ManifestName));
            JsonValue entry = manifest["templates"][name];
            Assert.That(entry.GetInt("triangles"), Is.EqualTo(report.Triangles));
            Assert.That(entry.GetString("resource"), Is.EqualTo("AtlasFallback/" + name + ".glb"));
            Assert.That(manifest.GetString("license"), Is.EqualTo("CC0-1.0"));

            FallbackTemplate template;
            Assert.That(FallbackSet.TryReadTemplate(glb, out template), Is.True);
            for (int k = 0; k < template.Indices.Length; k += 3)
            {
                float3 a = template.Positions[template.Indices[k]], b = template.Positions[template.Indices[k + 1]], c = template.Positions[template.Indices[k + 2]];
                Assert.That(math.dot(math.cross(b - a, c - a), template.Normals[template.Indices[k]]), Is.GreaterThan(0f), "winding");
            }
        }

        [TestCaseSource(nameof(Categories))]
        public void Fallback_Builder_KeepsTheLimitsForSmallAndLargeBoxes(ObjectCategory category)
        {
            FallbackTemplate template = AssetFixtures.LoadFallback().Get(category);
            var builder = new FallbackMeshBuilder();
            var mesh = new MeshData();
            foreach (float3 size in s_sizes)
            {
                string what = category + " " + size;
                Assert.That(builder.Build(template, size, 0.7f, mesh), Is.True, what);
                Assert.That(mesh.TriangleCount, Is.InRange(template.TriangleCount, FallbackMeshBuilder.MaxTriangles), what);
                Assert.That(mesh.LongestEdge(), Is.LessThanOrEqualTo(0.25f + 1e-6f), what);

                // The mesh is the box: same size, standing on its base centre.
                Assert.That(mesh.BoundsMin.x, Is.EqualTo(-0.5f * size.x).Within(1e-5f), what);
                Assert.That(mesh.BoundsMax.x, Is.EqualTo(0.5f * size.x).Within(1e-5f), what);
                Assert.That(mesh.BoundsMin.y, Is.EqualTo(0f).Within(1e-6f), what);
                Assert.That(mesh.BoundsMax.y, Is.EqualTo(size.y).Within(1e-5f), what);
                Assert.That(mesh.BoundsMin.z, Is.EqualTo(-0.5f * size.z).Within(1e-5f), what);
                Assert.That(mesh.BoundsMax.z, Is.EqualTo(0.5f * size.z).Within(1e-5f), what);

                float2 uv = FallbackMeshBuilder.PaletteUv(template.PaletteCell);
                for (int v = 0; v < mesh.VertexCount; v++)
                {
                    AtlasVertex vertex = mesh.Vertices[v];
                    Assert.That(vertex.Uv0.x, Is.EqualTo(uv.x));
                    Assert.That(vertex.Uv0.y, Is.EqualTo(uv.y));
                    Assert.That(vertex.Uv1.x, Is.EqualTo(0f));
                    Assert.That(vertex.Uv1.y, Is.EqualTo(0f));
                    Assert.That(math.length(vertex.Normal), Is.EqualTo(1f).Within(1e-4f));
                    Assert.That(vertex.Color >> 8, Is.EqualTo(0xFFFFFFu), "line weight 255, reserved 255");
                    // Shade floor 0.55 times the floor's occlusion 0.7 is the darkest a vertex can be.
                    Assert.That(vertex.Color & 255u, Is.InRange(98u, 255u));
                }
                for (int k = 0; k < mesh.IndexCount; k += 3)
                {
                    AtlasVertex a = mesh.Vertices[mesh.Indices[k]], b = mesh.Vertices[mesh.Indices[k + 1]], c = mesh.Vertices[mesh.Indices[k + 2]];
                    float3 face = math.cross(b.Position - a.Position, c.Position - a.Position);
                    Assert.That(math.dot(face, a.Normal), Is.GreaterThan(0f), what + ": winding");
                }
            }
        }

        [Test]
        public void Fallback_Builder_SplitsWithoutCracks()
        {
            // One closed surface: the faces of a box share their edges, and each shared edge must be cut alike
            // from both sides.
            FallbackTemplate storage = AssetFixtures.LoadFallback().Get(ObjectCategory.Storage);
            var builder = new FallbackMeshBuilder();
            var mesh = new MeshData();
            foreach (float3 size in new[] { new float3(1.9f, 0.85f, 0.9f), new float3(0.61f, 1.37f, 0.33f), new float3(2.5f, 2.4f, 0.6f) })
            {
                Assert.That(builder.Build(storage, size, 0f, mesh), Is.True, size.ToString());
                Assert.That(mesh.TriangleCount, Is.GreaterThan(storage.TriangleCount), "the box is large enough to need splits");
                Assert.That(CountJunctions(mesh), Is.EqualTo(0), size.ToString());
            }
        }

        [Test]
        public void Fallback_Builder_BisectsWhatIsNotARectangle()
        {
            // Two triangles of a kite, sharing the edge 0-2: a mesh from a kit rather than the team's boxes.
            var kite = new FallbackTemplate
            {
                Name = "kite", PaletteCell = 100,
                Positions = new[] { new float3(-0.5f, 0f, -0.5f), new float3(0.5f, 0f, -0.2f), new float3(0.5f, 1f, 0.5f), new float3(-0.3f, 1f, 0.1f) },
                Normals = new[] { new float3(0f, 0f, -1f), new float3(0f, 0f, -1f), new float3(0f, 0f, -1f), new float3(0f, 0f, -1f) },
                Indices = new[] { 0, 2, 1, 0, 3, 2 }
            };
            var builder = new FallbackMeshBuilder();
            var mesh = new MeshData();
            Assert.That(builder.Build(kite, new float3(1.3f, 1.1f, 0.9f), 0f, mesh), Is.True);
            Assert.That(mesh.TriangleCount, Is.InRange(20, FallbackMeshBuilder.MaxTriangles));
            Assert.That(mesh.LongestEdge(), Is.LessThanOrEqualTo(0.25f + 1e-6f));
            Assert.That(CountJunctions(mesh), Is.EqualTo(0));

            // The area is kept: nothing is lost or doubled by the split.
            double area = 0.0;
            for (int k = 0; k < mesh.IndexCount; k += 3)
            {
                float3 a = mesh.Vertices[mesh.Indices[k]].Position, b = mesh.Vertices[mesh.Indices[k + 1]].Position, c = mesh.Vertices[mesh.Indices[k + 2]].Position;
                area += 0.5 * math.length(math.cross(b - a, c - a));
            }
            var s = new float3(1.3f, 1.1f, 0.9f);
            float3 p0 = kite.Positions[0] * s, p1 = kite.Positions[1] * s, p2 = kite.Positions[2] * s, p3 = kite.Positions[3] * s;
            double want = 0.5 * math.length(math.cross(p2 - p0, p1 - p0)) + 0.5 * math.length(math.cross(p3 - p0, p2 - p0));
            Assert.That(area, Is.EqualTo(want).Within(1e-4));
        }

        // Vertices that lie inside another triangle's edge: where one side was cut and the other was not.
        static int CountJunctions(MeshData mesh)
        {
            int junctions = 0;
            for (int k = 0; k < mesh.IndexCount; k += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    float3 p = mesh.Vertices[mesh.Indices[k + e]].Position, q = mesh.Vertices[mesh.Indices[k + (e + 1) % 3]].Position;
                    float3 d = q - p;
                    float lengthSq = math.lengthsq(d);
                    for (int v = 0; v < mesh.VertexCount; v++)
                    {
                        float3 r = mesh.Vertices[v].Position - p;
                        float t = math.dot(r, d) / lengthSq;
                        if (t > 0.01f && t < 0.99f && math.lengthsq(r - t * d) < 1e-10f) junctions++;
                    }
                }
            }
            return junctions;
        }

        [Test]
        public void Fallback_Builder_ShadesByTheRoomsLightAndTheFloor()
        {
            FallbackTemplate storage = AssetFixtures.LoadFallback().Get(ObjectCategory.Storage);
            var builder = new FallbackMeshBuilder();
            var mesh = new MeshData();
            Assert.That(builder.Build(storage, new float3(0.2f, 0.6f, 0.2f), 0f, mesh), Is.True);
            foreach (float yaw in new[] { 0f, 1.2f })
            {
                Assert.That(builder.Build(storage, new float3(0.2f, 0.6f, 0.2f), yaw, mesh), Is.True);
                float cos = math.cos(yaw), sin = math.sin(yaw);
                for (int v = 0; v < mesh.VertexCount; v++)
                {
                    AtlasVertex vertex = mesh.Vertices[v];
                    float3 n = vertex.Normal;
                    var inRoom = new float3(n.x * cos + n.z * sin, n.y, -n.x * sin + n.z * cos);
                    float occlusion = math.lerp(0.7f, 1f, math.saturate(vertex.Position.y / 0.3f));
                    Assert.That(vertex.Color & 255u, Is.EqualTo((uint)AtlasShading.ToByte(AtlasShading.FaceShade(inRoom), occlusion)));
                }
            }
        }

        [Test]
        public void Fallback_Builder_RefusesABoxThatNeedsTooManyTriangles()
        {
            FallbackSet set = AssetFixtures.LoadFallback();
            var builder = new FallbackMeshBuilder();
            var mesh = new MeshData();
            Assert.That(builder.Build(set.Get(ObjectCategory.Storage), new float3(6f, 3f, 6f), 0f, mesh), Is.False);
            Assert.That(mesh.TriangleCount, Is.EqualTo(0), "nothing is left behind: the object keeps its placeholder box");
            Assert.That(mesh.VertexCount, Is.EqualTo(0));
            // The builder is ready for the next object.
            Assert.That(builder.Build(set.Get(ObjectCategory.Chair), new float3(0.5f, 0.9f, 0.5f), 0f, mesh), Is.True);
            Assert.That(mesh.TriangleCount, Is.InRange(1, FallbackMeshBuilder.MaxTriangles));
        }

        [Test]
        public void Fallback_Builder_GivesTheSameMeshTwice()
        {
            FallbackTemplate couch = AssetFixtures.LoadFallback().Get(ObjectCategory.Couch);
            var first = new MeshData();
            var second = new MeshData();
            Assert.That(new FallbackMeshBuilder().Build(couch, new float3(2f, 0.85f, 0.9f), 0.3f, first), Is.True);
            var reused = new FallbackMeshBuilder();
            reused.Build(couch, new float3(0.4f, 0.4f, 0.4f), 1f, second);
            Assert.That(reused.Build(couch, new float3(2f, 0.85f, 0.9f), 0.3f, second), Is.True);
            Assert.That(second.VertexCount, Is.EqualTo(first.VertexCount));
            Assert.That(second.IndexCount, Is.EqualTo(first.IndexCount));
            for (int v = 0; v < first.VertexCount; v++)
            {
                Assert.That(second.Vertices[v].Position.Equals(first.Vertices[v].Position), Is.True);
                Assert.That(second.Vertices[v].Color, Is.EqualTo(first.Vertices[v].Color));
            }
            for (int k = 0; k < first.IndexCount; k++) Assert.That(second.Indices[k], Is.EqualTo(first.Indices[k]));
        }

        [Test]
        public void Fallback_Set_SurvivesMissingFiles()
        {
            FallbackSet none = FallbackSet.Load(name => null);
            Assert.That(none.Count, Is.EqualTo(0));
            Assert.That(none.LastError, Is.Not.Null);
            Assert.That(none.Get(ObjectCategory.Chair), Is.Null);

            FallbackSet partial = FallbackSet.Load(name => name == "chair.glb" ? null : AssetFixtures.ReadFallback(name));
            Assert.That(partial.Count, Is.EqualTo(20));
            Assert.That(partial.Get(ObjectCategory.Chair), Is.Null);
            Assert.That(partial.Get(ObjectCategory.Table), Is.Not.Null);
        }
    }
}
