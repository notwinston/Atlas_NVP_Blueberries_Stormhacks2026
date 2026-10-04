using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Telemetry.Tests
{
    /// <summary>
    /// First shapes made from the object profile (design section 5.2): the fitted templates of chair, couch and
    /// bed, and the shape cut from the scan for objects without a template.
    /// </summary>
    public class FittedShapesTests
    {
        static IEnumerable<ObjectCategory> AllCategories()
        {
            for (int i = 0; i < ObjectCategories.Count; i++) yield return (ObjectCategory)i;
        }

        static byte[] Profile(System.Func<int, int, int> value)
        {
            var profile = new byte[256];
            for (int j = 0; j < 16; j++)
                for (int i = 0; i < 16; i++) profile[j * 16 + i] = (byte)value(i, j);
            return profile;
        }

        // Test vector A of the brief (S3): a chair-like box, high at the back (rows 0 and 1) and half height elsewhere.
        static byte[] VectorA() { return Profile((i, j) => j < 2 ? 255 : 128); }

        static byte[] Full(int seed) { return Profile((i, j) => 60 + (i * 31 + j * 17 + seed * 13) % 190); }

        static void AssertInsideTheBox(MeshData mesh, float3 size, string what)
        {
            Assert.That(mesh.BoundsMin.x, Is.GreaterThanOrEqualTo(-0.5f * size.x - 1e-5f), what);
            Assert.That(mesh.BoundsMax.x, Is.LessThanOrEqualTo(0.5f * size.x + 1e-5f), what);
            Assert.That(mesh.BoundsMin.y, Is.GreaterThanOrEqualTo(-1e-6f), what);
            Assert.That(mesh.BoundsMax.y, Is.LessThanOrEqualTo(size.y + 1e-5f), what);
            Assert.That(mesh.BoundsMin.z, Is.GreaterThanOrEqualTo(-0.5f * size.z - 1e-5f), what);
            Assert.That(mesh.BoundsMax.z, Is.LessThanOrEqualTo(0.5f * size.z + 1e-5f), what);
        }

        // The highest upward-facing vertex with z in [zMin, zMax] and |x| at most xMax; -1 when there is none.
        static float TopAt(MeshData mesh, float zMin, float zMax, float xMax = float.MaxValue)
        {
            float top = -1f;
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                AtlasVertex vertex = mesh.Vertices[v];
                if (vertex.Normal.y > 0.99f && vertex.Position.z >= zMin && vertex.Position.z <= zMax && math.abs(vertex.Position.x) <= xMax)
                    top = math.max(top, vertex.Position.y);
            }
            return top;
        }

        [TestCaseSource(nameof(AllCategories))]
        public void Shapes_EveryCategoryWithAFullProfile_BuildsWithinTheLimits(ObjectCategory category)
        {
            FallbackSet set = AssetFixtures.LoadFallback();
            var builder = new FallbackMeshBuilder();
            var mesh = new MeshData();
            var sizes = new[] { new float3(0.5f, 0.9f, 0.5f), new float3(1.2f, 0.75f, 0.8f), new float3(2.0f, 0.85f, 0.9f), new float3(0.06f, 0.03f, 0.11f) };
            for (int k = 0; k < sizes.Length; k++)
            {
                float3 size = sizes[k];
                string what = category + " " + size;
                FirstShapeKind kind = FittedShapes.Build(builder, set, category, size, 0.4f, Full(k), mesh);
                Assert.That(kind, Is.Not.EqualTo(FirstShapeKind.None), what);
                Assert.That(mesh.TriangleCount, Is.InRange(1, FallbackMeshBuilder.MaxTriangles), what);
                Assert.That(mesh.LongestEdge(), Is.LessThanOrEqualTo(0.25f + 1e-6f), what);
                AssertInsideTheBox(mesh, size, what);
                bool hasTemplate = FallbackSet.HasTemplate(category);
                Assert.That(kind == FirstShapeKind.ScanCut, Is.EqualTo(!hasTemplate), what);
                for (int v = 0; v < mesh.VertexCount; v++)
                    Assert.That(math.length(mesh.Vertices[v].Normal), Is.EqualTo(1f).Within(1e-4f), what);
                for (int i = 0; i < mesh.IndexCount; i += 3)
                {
                    AtlasVertex a = mesh.Vertices[mesh.Indices[i]], b = mesh.Vertices[mesh.Indices[i + 1]], c = mesh.Vertices[mesh.Indices[i + 2]];
                    Assert.That(math.dot(math.cross(b.Position - a.Position, c.Position - a.Position), a.Normal), Is.GreaterThan(0f), what + ": winding");
                }
            }
        }

        [Test]
        public void Shapes_NoProfile_IsExactlyTodaysTemplateMesh()
        {
            FallbackSet set = AssetFixtures.LoadFallback();
            var builder = new FallbackMeshBuilder();
            var size = new float3(0.5f, 0.9f, 0.5f);
            foreach (ObjectCategory category in new[] { ObjectCategory.Chair, ObjectCategory.Couch, ObjectCategory.Bed, ObjectCategory.Table })
            {
                var today = new MeshData();
                Assert.That(builder.Build(set.Get(category), size, 0.3f, today), Is.True);
                var thin = Profile((i, j) => i == 0 && j < 16 ? 100 : 0);       // coverage 16 / 256: under 0.30
                foreach (byte[] profile in new byte[][] { null, new byte[256], new byte[10], thin })
                {
                    var mesh = new MeshData();
                    Assert.That(FittedShapes.Build(builder, set, category, size, 0.3f, profile, mesh), Is.EqualTo(FirstShapeKind.Template), category.ToString());
                    AssertSameMesh(today, mesh, category.ToString());
                }
            }
        }

        static void AssertSameMesh(MeshData a, MeshData b, string what)
        {
            Assert.That(b.VertexCount, Is.EqualTo(a.VertexCount), what);
            Assert.That(b.IndexCount, Is.EqualTo(a.IndexCount), what);
            for (int v = 0; v < a.VertexCount; v++)
            {
                Assert.That(b.Vertices[v].Position.Equals(a.Vertices[v].Position), Is.True, what);
                Assert.That(b.Vertices[v].Color, Is.EqualTo(a.Vertices[v].Color), what);
            }
            for (int k = 0; k < a.IndexCount; k++) Assert.That(b.Indices[k], Is.EqualTo(a.Indices[k]), what);
        }

        [Test]
        public void Shapes_AFittedChair_HasItsSeatWhereTheProfileSaysAndItsBackAtTheFullHeight()
        {
            FallbackSet set = AssetFixtures.LoadFallback();
            var builder = new FallbackMeshBuilder();
            var size = new float3(0.5f, 1.0f, 0.5f);
            var mesh = new MeshData();

            // The back half is the tall back, the front half (rows 8 to 15) is the seat at 0.36 of the height.
            byte[] low = Profile((i, j) => j >= 8 ? 92 : 255);
            Assert.That(FittedShapes.Build(builder, set, ObjectCategory.Chair, size, 0f, low, mesh), Is.EqualTo(FirstShapeKind.Fitted));
            Assert.That(TopAt(mesh, 0.05f, 0.25f), Is.EqualTo(92f / 255f).Within(1e-4f), "the seat top follows the profile");
            Assert.That(mesh.BoundsMax.y, Is.EqualTo(size.y).Within(1e-5f), "the back keeps the full height");
            Assert.That(TopAt(mesh, -0.25f, -0.2f), Is.EqualTo(size.y).Within(1e-5f), "and stands on local -Z");
            Assert.That(mesh.LongestEdge(), Is.LessThanOrEqualTo(0.25f + 1e-6f));

            // A higher seat gives a higher seat top.
            byte[] high = Profile((i, j) => j >= 8 ? 153 : 255);
            Assert.That(FittedShapes.Build(builder, set, ObjectCategory.Chair, size, 0f, high, mesh), Is.EqualTo(FirstShapeKind.Fitted));
            Assert.That(TopAt(mesh, 0.05f, 0.25f), Is.EqualTo(0.6f).Within(1e-4f));

            // The median is taken over seen cells only, so unseen cells in the front half do not pull it down.
            byte[] gaps = Profile((i, j) => j >= 8 ? (i % 2 == 0 ? 0 : 153) : 255);
            Assert.That(FittedShapes.Build(builder, set, ObjectCategory.Chair, size, 0f, gaps, mesh), Is.EqualTo(FirstShapeKind.Fitted));
            Assert.That(TopAt(mesh, 0.05f, 0.25f), Is.EqualTo(0.6f).Within(1e-4f));
        }

        [Test]
        public void Shapes_TheSeatIsKeptWithinASensibleRange()
        {
            FallbackSet set = AssetFixtures.LoadFallback();
            FallbackTemplate chair = set.Get(ObjectCategory.Chair);
            var builder = new FallbackMeshBuilder();
            var size = new float3(0.5f, 1.0f, 0.5f);
            var mesh = new MeshData();
            float median;
            Assert.That(FittedShapes.FrontMedian(Profile((i, j) => j >= 8 ? 10 : 255), out median), Is.True);
            Assert.That(median, Is.EqualTo(10f / 255f).Within(1e-6f));
            // A very low (0.04) and a very high (0.98) front are held to 0.25 and 0.70 of the height for a chair.
            Assert.That(FittedShapes.Build(builder, set, ObjectCategory.Chair, size, 0f, Profile((i, j) => j >= 8 ? 10 : 255), mesh), Is.EqualTo(FirstShapeKind.Fitted));
            Assert.That(TopAt(mesh, 0.05f, 0.25f), Is.EqualTo(0.25f).Within(1e-5f));
            Assert.That(FittedShapes.Build(builder, set, ObjectCategory.Chair, size, 0f, Profile((i, j) => j >= 8 ? 250 : 255), mesh), Is.EqualTo(FirstShapeKind.Fitted));
            Assert.That(TopAt(mesh, 0.05f, 0.25f), Is.EqualTo(0.70f).Within(1e-5f));
            // Another category is never fitted.
            Assert.That(FittedShapes.Fit(set.Get(ObjectCategory.Table), ObjectCategory.Table, Full(0)), Is.SameAs(set.Get(ObjectCategory.Table)));
            // Neither is an object whose front half nobody saw.
            Assert.That(FittedShapes.Fit(chair, ObjectCategory.Chair, Profile((i, j) => j < 8 ? 200 : 0)), Is.SameAs(chair));
        }

        [TestCase(ObjectCategory.Couch)]
        [TestCase(ObjectCategory.Bed)]
        public void Shapes_ACouchAndABed_AreFittedToo(ObjectCategory category)
        {
            FallbackSet set = AssetFixtures.LoadFallback();
            var builder = new FallbackMeshBuilder();
            var size = new float3(1.8f, 0.9f, 0.9f);
            var plain = new MeshData();
            var fitted = new MeshData();
            Assert.That(builder.Build(set.Get(category), size, 0f, plain), Is.True);
            Assert.That(FittedShapes.Build(builder, set, category, size, 0f, Profile((i, j) => j >= 8 ? 80 : 255), fitted), Is.EqualTo(FirstShapeKind.Fitted));
            Assert.That(fitted.BoundsMax.y, Is.EqualTo(size.y).Within(1e-5f));
            Assert.That(fitted.LongestEdge(), Is.LessThanOrEqualTo(0.25f + 1e-6f));
            float seat = 80f / 255f * size.y;
            Assert.That(TopAt(fitted, 0.1f, 0.4f, 0.5f), Is.EqualTo(seat).Within(1e-4f), "the seat follows the profile");
            Assert.That(TopAt(plain, 0.1f, 0.4f, 0.5f), Is.GreaterThan(seat + 0.05f), "the template's own seat is higher");
        }

        [Test]
        public void Shapes_ScanCut_OfTestVectorA_IsHigherAtTheBackThanAtTheFront()
        {
            var builder = new FallbackMeshBuilder();
            var size = new float3(0.5f, 1.0f, 0.5f);
            var mesh = new MeshData();
            Assert.That(FittedShapes.Build(builder, null, ObjectCategory.Unknown, size, 0f, VectorA(), mesh), Is.EqualTo(FirstShapeKind.ScanCut));
            Assert.That(mesh.TriangleCount, Is.InRange(1, FallbackMeshBuilder.MaxTriangles));
            Assert.That(mesh.LongestEdge(), Is.LessThanOrEqualTo(0.25f + 1e-6f));
            AssertInsideTheBox(mesh, size, "vector A");

            float backTop = TopAt(mesh, -0.25f, -0.25f + 0.0625f + 1e-5f), frontTop = TopAt(mesh, 0.2f, 0.25f);
            Assert.That(backTop, Is.EqualTo(1.0f).Within(1e-5f));
            Assert.That(frontTop, Is.EqualTo(128f / 255f).Within(1e-4f));
            Assert.That(backTop, Is.GreaterThan(frontTop + 0.3f));
            Assert.That(mesh.BoundsMin.y, Is.EqualTo(0f).Within(1e-6f), "the columns stand on the floor");

            // Tinted with the cell of the category's placeholder box.
            float2 uv = FallbackMeshBuilder.PaletteUv(FittedShapes.CategoryPaletteBase + ObjectCategories.TintIndex(ObjectCategory.Unknown));
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                Assert.That(mesh.Vertices[v].Uv0.x, Is.EqualTo(uv.x));
                Assert.That(mesh.Vertices[v].Uv0.y, Is.EqualTo(uv.y));
            }
            Assert.That(FittedShapes.CategoryPaletteBase + ObjectCategories.TintIndex(ObjectCategory.Other), Is.EqualTo(54));
        }

        [Test]
        public void Shapes_ScanCut_NeedsAProfileOfEnoughCoverage()
        {
            var builder = new FallbackMeshBuilder();
            var size = new float3(0.5f, 1.0f, 0.5f);
            var mesh = new MeshData();
            var seen = Profile((i, j) => (i + j) % 4 == 0 ? 200 : 0);                  // 64 of 256 cells: under 0.30
            Assert.That(FittedShapes.Coverage(seen), Is.EqualTo(0.25f));
            Assert.That(FittedShapes.Build(builder, null, ObjectCategory.Other, size, 0f, seen, mesh), Is.EqualTo(FirstShapeKind.None));
            Assert.That(mesh.TriangleCount, Is.EqualTo(0), "the placeholder box stays");
            Assert.That(FittedShapes.Build(builder, null, ObjectCategory.Other, size, 0f, null, mesh), Is.EqualTo(FirstShapeKind.None));
            var enough = Profile((i, j) => (i + j) % 3 == 0 ? 200 : 0);               // 86 of 256
            Assert.That(FittedShapes.Coverage(enough), Is.GreaterThanOrEqualTo(0.30f));
            Assert.That(FittedShapes.Build(builder, null, ObjectCategory.Other, size, 0f, enough, mesh), Is.EqualTo(FirstShapeKind.ScanCut));
        }

        [Test]
        public void Shapes_ScanCut_FillsUnseenCellsFromTheirSeenNeighbours()
        {
            // The highest of each 2 x 2 is kept.
            byte[] profile = Profile((i, j) => (i == 0 && j == 0) ? 40 : (i == 1 && j == 1) ? 90 : 60);
            int[] grid = FittedShapes.Reduce(profile, 8);
            Assert.That(grid[0], Is.EqualTo(90));
            Assert.That(grid[1], Is.EqualTo(60));

            // One block seen, the rest filled from it, pass by pass: the whole grid has its value.
            byte[] one = new byte[256];
            one[0] = 100;
            one[1] = 100; one[16] = 100; one[17] = 100;
            grid = FittedShapes.Reduce(one, 8);
            Assert.That(grid, Has.All.EqualTo(100));
            Assert.That(FittedShapes.Reduce(new byte[256], 8), Is.Null, "nothing seen: no shape");
            Assert.That(FittedShapes.Reduce(null, 8), Is.Null);

            // A gap between two heights takes the mean of the seen neighbours it has.
            byte[] gap = Profile((i, j) => i < 6 ? 100 : i >= 8 ? 200 : 0);
            grid = FittedShapes.Reduce(gap, 8);
            Assert.That(grid[0 * 8 + 3], Is.EqualTo(150), "between 100 on the left and 200 on the right");
            Assert.That(grid, Has.None.EqualTo(0));
        }

        [Test]
        public void Shapes_ScanCut_FallsBackToFourByFourWhenEightByEightIsTooLarge()
        {
            var builder = new FallbackMeshBuilder();
            byte[] profile = Full(3);
            var scratch = new MeshData();
            bool found = false;
            for (float side = 1.0f; side <= 3.0f && !found; side += 0.05f)
            {
                var size = new float3(side, 0.9f, side);
                bool eight = builder.Build(FittedShapes.ColumnTemplate(profile, 8, 50), size, 0f, scratch);
                bool four = builder.Build(FittedShapes.ColumnTemplate(profile, 4, 50), size, 0f, scratch);
                if (eight || !four) continue;
                found = true;
                var mesh = new MeshData();
                Assert.That(FittedShapes.BuildScanCut(builder, ObjectCategory.Unknown, size, 0f, profile, mesh), Is.True, size.ToString());
                Assert.That(mesh.TriangleCount, Is.InRange(1, FallbackMeshBuilder.MaxTriangles));
                var four4 = new MeshData();
                Assert.That(builder.Build(FittedShapes.ColumnTemplate(profile, 4, 32), size, 0f, four4), Is.True);
                Assert.That(mesh.TriangleCount, Is.EqualTo(four4.TriangleCount), "built again at 4 x 4");
            }
            Assert.That(found, Is.True, "some size is too large for 8 x 8 and fits at 4 x 4");

            // A box too large for either keeps its placeholder.
            var none = new MeshData();
            Assert.That(FittedShapes.BuildScanCut(builder, ObjectCategory.Unknown, new float3(12f, 3f, 12f), 0f, profile, none), Is.False);
            Assert.That(none.TriangleCount, Is.EqualTo(0));
        }

        [Test]
        public void Shapes_ATemplateThatIsTooLarge_FallsBackToTheScanCutOrTheBox()
        {
            FallbackSet set = AssetFixtures.LoadFallback();
            var builder = new FallbackMeshBuilder();
            var mesh = new MeshData();
            var huge = new float3(6f, 3f, 6f);
            Assert.That(FittedShapes.Build(builder, set, ObjectCategory.Storage, huge, 0f, null, mesh), Is.EqualTo(FirstShapeKind.None));
            Assert.That(mesh.TriangleCount, Is.EqualTo(0));

            // Where a template cannot be built the scan decides: the shape cut from it, or the box.
            byte[] profile = Full(1);
            int refused = 0;
            for (int c = 0; c < ObjectCategories.Count; c++)
            {
                var category = (ObjectCategory)c;
                FallbackTemplate template = set.Get(category);
                if (template == null) continue;
                for (float side = 1.0f; side <= 6.0f; side += 0.5f)
                {
                    var size = new float3(side, 1.2f, side);
                    if (builder.Build(template, size, 0f, mesh)) continue;
                    refused++;
                    bool cut = FittedShapes.BuildScanCut(builder, category, size, 0f, profile, mesh);
                    FirstShapeKind kind = FittedShapes.Build(builder, set, category, size, 0f, profile, mesh);
                    if (kind == FirstShapeKind.Fitted) continue;            // a lower seat can be built where the template cannot
                    Assert.That(kind, Is.EqualTo(cut ? FirstShapeKind.ScanCut : FirstShapeKind.None), category + " " + size);
                    if (cut) Assert.That(mesh.TriangleCount, Is.InRange(1, FallbackMeshBuilder.MaxTriangles));
                    else Assert.That(mesh.TriangleCount, Is.EqualTo(0));
                    Assert.That(FittedShapes.Build(builder, set, category, size, 0f, null, mesh), Is.EqualTo(FirstShapeKind.None), "no profile: the box");
                }
            }
            Assert.That(refused, Is.GreaterThan(0));
        }
    }
}
