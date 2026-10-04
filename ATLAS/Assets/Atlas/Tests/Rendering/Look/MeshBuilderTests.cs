using System.Runtime.InteropServices;
using System;
using Atlas.Kernel;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Rendering.Tests.Look
{
    /// <summary>The mesh builder: counts, normals, winding, bounds and the 0.25 m rule (lane A contract, 10.2).</summary>
    public class MeshBuilderTests
    {
        const float Limit = AtlasMeshBuilder.MaxEdge * 1.0002f;

        // Every triangle must be clockwise seen from the side its vertex normals point to, with no index out of range.
        static void AssertWindingAgreesWithNormals(AtlasMeshBuilder mesh)
        {
            Assert.That(mesh.IndexCount % 3, Is.EqualTo(0));
            for (int t = 0; t < mesh.IndexCount; t += 3)
            {
                int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
                Assert.That(a >= 0 && a < mesh.VertexCount && b >= 0 && b < mesh.VertexCount && c >= 0 && c < mesh.VertexCount, Is.True);
                float3 pa = mesh.Vertices[a].Position, pb = mesh.Vertices[b].Position, pc = mesh.Vertices[c].Position;
                float3 face = math.cross(pb - pa, pc - pa);
                Assert.That(math.length(face), Is.GreaterThan(1e-9f), "degenerate triangle " + t / 3);
                float3 normal = math.normalize(mesh.Vertices[a].Normal + mesh.Vertices[b].Normal + mesh.Vertices[c].Normal);
                Assert.That(math.dot(math.normalize(face), normal), Is.GreaterThan(0.5f), "triangle " + t / 3);
            }
        }

        static void AssertUnitNormalsAndBounds(AtlasMeshBuilder mesh)
        {
            float3 min = new float3(float.MaxValue), max = new float3(float.MinValue);
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                Assert.That(math.length(mesh.Vertices[i].Normal), Is.EqualTo(1f).Within(1e-4f));
                min = math.min(min, mesh.Vertices[i].Position);
                max = math.max(max, mesh.Vertices[i].Position);
                Assert.That(mesh.Vertices[i].Color >> 16, Is.EqualTo(0xFFFFu), "reserved channels are 255");
            }
            Assert.That(math.cmax(math.abs(min - mesh.BoundsMin)), Is.LessThan(1e-5f));
            Assert.That(math.cmax(math.abs(max - mesh.BoundsMax)), Is.LessThan(1e-5f));
        }

        static float Area(AtlasMeshBuilder mesh, int firstIndex = 0)
        {
            float area = 0f;
            for (int t = firstIndex; t < mesh.IndexCount; t += 3)
            {
                float3 pa = mesh.Vertices[mesh.Indices[t]].Position, pb = mesh.Vertices[mesh.Indices[t + 1]].Position, pc = mesh.Vertices[mesh.Indices[t + 2]].Position;
                area += 0.5f * math.length(math.cross(pb - pa, pc - pa));
            }
            return area;
        }

        [Test]
        public void Vertex_Is44BytesInContractOrder()
        {
            Assert.That(Marshal.SizeOf<AtlasVertex>(), Is.EqualTo(AtlasVertex.Stride));
            Assert.That((int)Marshal.OffsetOf<AtlasVertex>("Position"), Is.EqualTo(0));
            Assert.That((int)Marshal.OffsetOf<AtlasVertex>("Normal"), Is.EqualTo(12));
            Assert.That((int)Marshal.OffsetOf<AtlasVertex>("Color"), Is.EqualTo(24));
            Assert.That((int)Marshal.OffsetOf<AtlasVertex>("Uv0"), Is.EqualTo(28));
            Assert.That((int)Marshal.OffsetOf<AtlasVertex>("Uv1"), Is.EqualTo(36));
        }

        [Test]
        public void PackColor_PutsShadeInTheLowestByte()
        {
            uint color = AtlasVertex.PackColor(0x12, 0x34);
            Assert.That(color, Is.EqualTo(0xFFFF3412u));
            var vertex = new AtlasVertex { Color = color };
            Assert.That(vertex.Shade, Is.EqualTo(0x12));
            Assert.That(vertex.LineWeight, Is.EqualTo(0x34));
            Assert.That(AtlasVertex.PackColor(200) >> 8 & 255u, Is.EqualTo(255u));
        }

        [Test]
        public void Shading_OneFixedLight()
        {
            Assert.That(math.length(AtlasShading.LightDirection), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(AtlasShading.FaceShade(AtlasShading.LightDirection), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(AtlasShading.FaceShade(-AtlasShading.LightDirection), Is.EqualTo(AtlasShading.ShadeFloor).Within(1e-6f));
            Assert.That(AtlasShading.FaceShadeTwoSided(-AtlasShading.LightDirection), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(AtlasShading.FaceShade(new float3(0f, 1f, 0f)), Is.GreaterThan(AtlasShading.FaceShade(new float3(1f, 0f, 0f))));
            Assert.That(AtlasShading.ToByte(1f), Is.EqualTo(255));
            Assert.That(AtlasShading.ToByte(1f, 0.5f), Is.EqualTo(128));
            Assert.That(AtlasShading.ToByte(3f), Is.EqualTo(255));
            Assert.That(AtlasShading.ToByte(-1f), Is.EqualTo(0));
        }

        [Test]
        public void QuadGrid_FloorOneMetre_Is4By4Cells()
        {
            var mesh = new AtlasMeshBuilder();
            mesh.AddQuadGrid(float3.zero, new float3(1f, 0f, 0f), new float3(0f, 0f, 1f), AtlasPalette.Uv(AtlasPalette.Floor),
                             float2.zero, new float2(2f, 0f), new float2(0f, 2f));
            Assert.That(mesh.VertexCount, Is.EqualTo(25));
            Assert.That(mesh.TriangleCount, Is.EqualTo(32));
            Assert.That(mesh.TriangleCount, Is.EqualTo(AtlasMeshBudget.QuadGridTriangles(1f, 1f)));
            Assert.That(mesh.LongestSide(), Is.LessThanOrEqualTo(Limit));
            Assert.That(mesh.Vertices[0].Normal.y, Is.EqualTo(1f).Within(1e-6f), "front face towards cross(v, u)");
            AssertWindingAgreesWithNormals(mesh);
            AssertUnitNormalsAndBounds(mesh);
            Assert.That(Area(mesh), Is.EqualTo(1f).Within(1e-4f));

            // Line coordinates run 0..2 along each side: a line every 0.5 m.
            AtlasVertex far = mesh.Vertices[24];
            Assert.That(far.Uv1.x, Is.EqualTo(2f).Within(1e-6f));
            Assert.That(far.Uv1.y, Is.EqualTo(2f).Within(1e-6f));
            Assert.That(far.Uv0.x, Is.EqualTo(AtlasPalette.Uv(AtlasPalette.Floor).x));
            Assert.That(far.Shade, Is.EqualTo(AtlasShading.ToByte(AtlasShading.FaceShade(new float3(0f, 1f, 0f)))));
            Assert.That(far.LineWeight, Is.EqualTo(255));
        }

        [Test]
        public void QuadGrid_AwkwardLengths_KeepEveryCellSideWithinTheLimit()
        {
            var lengths = new[] { 0.01f, 0.2499f, 0.25f, 0.2501f, 0.5f, 0.7f, 1.003f, 3.37f, 4.999f };
            foreach (float lu in lengths)
            {
                foreach (float lv in lengths)
                {
                    var mesh = new AtlasMeshBuilder();
                    // A slanted quad: neither side lies on an axis.
                    float3 u = math.normalize(new float3(1f, 0.3f, -0.2f)) * lu;
                    float3 v = math.normalize(math.cross(u, new float3(0.1f, 1f, 0.4f))) * lv;
                    mesh.AddQuadGrid(new float3(1f, 2f, 3f), u, v, float2.zero, float2.zero, float2.zero, float2.zero);
                    Assert.That(mesh.LongestSide(), Is.LessThanOrEqualTo(Limit), lu + " x " + lv);
                    Assert.That(mesh.TriangleCount, Is.EqualTo(AtlasMeshBudget.QuadGridTriangles(lu, lv)));
                    Assert.That(Area(mesh), Is.EqualTo(lu * lv).Within(1e-3f * math.max(1f, lu * lv)));
                    AssertWindingAgreesWithNormals(mesh);
                }
            }
        }

        [Test]
        public void Segments_ExactMultiplesDoNotGainACell()
        {
            Assert.That(AtlasMeshBudget.Segments(0.25f, 0.25f), Is.EqualTo(1));
            Assert.That(AtlasMeshBudget.Segments(1f, 0.25f), Is.EqualTo(4));
            Assert.That(AtlasMeshBudget.Segments(5f, 0.25f), Is.EqualTo(20));
            Assert.That(AtlasMeshBudget.Segments(0.26f, 0.25f), Is.EqualTo(2));
            Assert.That(AtlasMeshBudget.Segments(0f, 0.25f), Is.EqualTo(1));
            Assert.That(AtlasMeshBudget.Segments(1f, 0f), Is.EqualTo(1));
            Assert.That(AtlasMeshBudget.DyadicSegments(1f, 0.25f), Is.EqualTo(4));
            Assert.That(AtlasMeshBudget.DyadicSegments(1.2f, 0.25f), Is.EqualTo(8));
            Assert.That(AtlasMeshBudget.DyadicSegments(0.1f, 0.25f), Is.EqualTo(1));
        }

        [Test]
        public void Quad_IsTwoTriangles()
        {
            var mesh = new AtlasMeshBuilder();
            mesh.AddQuad(float3.zero, new float3(3f, 0f, 0f), new float3(0f, 2f, 0f), float2.zero);
            Assert.That(mesh.VertexCount, Is.EqualTo(4));
            Assert.That(mesh.TriangleCount, Is.EqualTo(2));
            // cross(v, u) = cross(+y, +x) = -z.
            Assert.That(mesh.Vertices[0].Normal.z, Is.EqualTo(-1f).Within(1e-6f));
            AssertWindingAgreesWithNormals(mesh);
        }

        [Test]
        public void FloorGrid_LineCoordinatesFollowTheRoomFrame()
        {
            var mesh = new AtlasMeshBuilder();
            mesh.AddFloorGrid(new float2(-2f, -2.5f), new float2(2f, 2.5f), 0f, AtlasPalette.Uv(AtlasPalette.Floor));
            Assert.That(mesh.TriangleCount, Is.EqualTo(16 * 20 * 2));
            Assert.That(mesh.TriangleCount, Is.LessThanOrEqualTo(AtlasMeshBudget.HomeShell));
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                AtlasVertex vertex = mesh.Vertices[i];
                Assert.That(vertex.Uv1.x, Is.EqualTo(vertex.Position.x / 0.5f).Within(1e-4f));
                Assert.That(vertex.Uv1.y, Is.EqualTo(vertex.Position.z / 0.5f).Within(1e-4f));
                Assert.That(vertex.Normal.y, Is.EqualTo(1f));
            }
            AssertWindingAgreesWithNormals(mesh);
            AssertUnitNormalsAndBounds(mesh);
        }

        [Test]
        public void Box_CountsNormalsAndBounds()
        {
            var mesh = new AtlasMeshBuilder();
            var size = new float3(1f, 0.5f, 0.75f);
            mesh.AddBox(new float3(2f, 0f, -1f), size, 0f, AtlasPalette.Uv(AtlasPalette.Placeholder));
            // 4 x 3 top, sides 4 x 2 twice and 3 x 2 twice.
            Assert.That(mesh.TriangleCount, Is.EqualTo(2 * (12 + 8 + 8 + 6 + 6)));
            Assert.That(mesh.TriangleCount, Is.EqualTo(AtlasMeshBudget.BoxTriangles(size)));
            Assert.That(math.cmax(math.abs(mesh.BoundsMin - new float3(1.5f, 0f, -1.375f))), Is.LessThan(1e-5f));
            Assert.That(math.cmax(math.abs(mesh.BoundsMax - new float3(2.5f, 0.5f, -0.625f))), Is.LessThan(1e-5f));
            Assert.That(mesh.LongestSide(), Is.LessThanOrEqualTo(Limit));
            AssertWindingAgreesWithNormals(mesh);
            AssertUnitNormalsAndBounds(mesh);

            // Normals point away from the centre: the box is seen from outside.
            float3 centre = new float3(2f, 0.25f, -1f);
            for (int i = 0; i < mesh.VertexCount; i++)
                Assert.That(math.dot(mesh.Vertices[i].Normal, mesh.Vertices[i].Position - centre), Is.GreaterThan(0f));
            Assert.That(Area(mesh), Is.EqualTo(1f * 0.75f + 2f * (1f * 0.5f) + 2f * (0.75f * 0.5f)).Within(1e-4f));
        }

        [Test]
        public void Box_WithBottom_AddsTheSixthFace()
        {
            var open = new AtlasMeshBuilder();
            var closed = new AtlasMeshBuilder();
            var size = new float3(0.6f, 0.9f, 0.4f);
            open.AddBox(float3.zero, size, 0.3f, float2.zero);
            closed.AddBox(float3.zero, size, 0.3f, float2.zero, true);
            Assert.That(closed.TriangleCount, Is.EqualTo(AtlasMeshBudget.BoxTriangles(size, true)));
            Assert.That(closed.TriangleCount - open.TriangleCount, Is.EqualTo(2 * 3 * 2));
            AssertWindingAgreesWithNormals(closed);
            bool down = false;
            for (int i = 0; i < closed.VertexCount; i++) down |= closed.Vertices[i].Normal.y < -0.99f;
            Assert.That(down, Is.True);
        }

        [Test]
        public void Box_YawTurnsClockwiseSeenFromAbove()
        {
            var mesh = new AtlasMeshBuilder();
            // A long box along x, turned a quarter turn about +y, lies along z (Unity's sense: +x goes to -z).
            mesh.AddBox(float3.zero, new float3(2f, 0.2f, 0.2f), math.PI / 2f, float2.zero);
            Assert.That(mesh.BoundsMax.z, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(mesh.BoundsMax.x, Is.EqualTo(0.1f).Within(1e-5f));
            AssertWindingAgreesWithNormals(mesh);
            AssertUnitNormalsAndBounds(mesh);

            mesh.Clear();
            mesh.AddBox(float3.zero, new float3(1f, 1f, 1f), 0.7f, float2.zero);
            for (int i = 0; i < mesh.VertexCount; i++)
                Assert.That(math.dot(mesh.Vertices[i].Normal, mesh.Vertices[i].Position - new float3(0f, 0.5f, 0f)), Is.GreaterThan(0f));
        }

        [Test]
        public void Box_BaseOcclusionDarkensOnlyTheFloorRow()
        {
            var mesh = new AtlasMeshBuilder();
            mesh.AddBox(float3.zero, new float3(0.5f, 0.5f, 0.5f), 0f, float2.zero, false, 0, 0.25f, 0.6f);
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                AtlasVertex vertex = mesh.Vertices[i];
                byte lit = AtlasShading.ToByte(AtlasShading.FaceShade(vertex.Normal));
                bool sideAtFloor = vertex.Position.y < 1e-6f && math.abs(vertex.Normal.y) < 0.5f;
                Assert.That(vertex.Shade, Is.EqualTo(sideAtFloor ? AtlasShading.ToByte(AtlasShading.FaceShade(vertex.Normal), 0.6f) : lit));
                Assert.That(vertex.LineWeight, Is.EqualTo(0));
            }
        }

        [Test]
        public void Post_IsARoundPrismSeenFromOutside()
        {
            var mesh = new AtlasMeshBuilder();
            mesh.AddPost(new float3(1f, 0f, 1f), 0.04f, 1f, AtlasPalette.Uv(AtlasPalette.Post));
            // 8 sides x 4 rows, plus an 8-triangle top.
            Assert.That(mesh.TriangleCount, Is.EqualTo(8 * 4 * 2 + 8));
            Assert.That(mesh.LongestSide(), Is.LessThanOrEqualTo(Limit));
            AssertWindingAgreesWithNormals(mesh);
            AssertUnitNormalsAndBounds(mesh);
            Assert.That(mesh.BoundsMax.y, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(mesh.BoundsMax.x - mesh.BoundsMin.x, Is.EqualTo(0.08f).Within(1e-4f));

            mesh.Clear();
            mesh.AddPost(float3.zero, 0.04f, 1f, float2.zero, 6, false);
            Assert.That(mesh.TriangleCount, Is.EqualTo(6 * 4 * 2));
        }

        [Test]
        public void Post_ThickOne_RaisesItsSideCountAndCutsItsTop()
        {
            var mesh = new AtlasMeshBuilder();
            mesh.AddPost(float3.zero, 0.6f, 0.9f, float2.zero, 4);
            Assert.That(mesh.LongestEdge(), Is.LessThanOrEqualTo(AtlasMeshBuilder.MaxEdge * 1.4143f));
            Assert.That(mesh.LongestSide(), Is.LessThanOrEqualTo(Limit));
            AssertWindingAgreesWithNormals(mesh);
        }

        static float2[] LShape()
        {
            // An L, anticlockwise seen from above (x right, z up).
            return new[]
            {
                new float2(0f, 0f), new float2(2f, 0f), new float2(2f, 1f),
                new float2(1f, 1f), new float2(1f, 2.3f), new float2(0f, 2.3f),
            };
        }

        [Test]
        public void ExtrudedOutline_ConcaveShape_WallsFaceOutAndCapCoversTheArea()
        {
            float2[] outline = LShape();
            var mesh = new AtlasMeshBuilder();
            mesh.AddExtrudedOutline(outline, outline.Length, 0f, 0.4f, AtlasPalette.Uv(AtlasPalette.Kerb), true, false, false);
            int wallIndices = mesh.IndexCount;
            float perimeter = 2f + 1f + 1f + 1.3f + 1f + 2.3f;
            Assert.That(Area(mesh), Is.EqualTo(perimeter * 0.4f).Within(1e-3f));
            AssertWindingAgreesWithNormals(mesh);
            // The wall of the edge (0,0)-(2,0) faces -z: outwards.
            Assert.That(mesh.Vertices[0].Normal.z, Is.EqualTo(-1f).Within(1e-6f));

            mesh.Clear();
            mesh.AddExtrudedOutline(outline, outline.Length, 0f, 0.4f, AtlasPalette.Uv(AtlasPalette.Kerb));
            AssertWindingAgreesWithNormals(mesh);
            AssertUnitNormalsAndBounds(mesh);
            float capArea = 0f;
            for (int t = 0; t < mesh.IndexCount; t += 3)
            {
                AtlasVertex a = mesh.Vertices[mesh.Indices[t]];
                if (a.Normal.y < 0.5f) continue;
                Assert.That(a.Position.y, Is.EqualTo(0.4f).Within(1e-6f));
                float3 pb = mesh.Vertices[mesh.Indices[t + 1]].Position, pc = mesh.Vertices[mesh.Indices[t + 2]].Position;
                capArea += 0.5f * math.length(math.cross(pb - a.Position, pc - a.Position));
            }
            Assert.That(capArea, Is.EqualTo(2f * 1f + 1f * 1.3f).Within(1e-3f));
            Assert.That(mesh.LongestSide(), Is.LessThanOrEqualTo(Limit));
            Assert.That(wallIndices, Is.GreaterThan(0));
        }

        [Test]
        public void ExtrudedOutline_EitherDirection_GivesTheSameFacing()
        {
            float2[] outline = LShape();
            var reversed = new float2[outline.Length];
            for (int i = 0; i < outline.Length; i++) reversed[i] = outline[outline.Length - 1 - i];
            float2 inside = new float2(0.5f, 0.5f);

            foreach (float2[] ring in new[] { outline, reversed })
            {
                foreach (bool inward in new[] { false, true })
                {
                    var mesh = new AtlasMeshBuilder();
                    mesh.AddExtrudedOutline(ring, ring.Length, 0f, 2.4f, float2.zero, true, inward, false);
                    AssertWindingAgreesWithNormals(mesh);
                    // The wall on x = 0 is seen from inside exactly when the room is built inwards.
                    for (int i = 0; i < mesh.VertexCount; i++)
                    {
                        AtlasVertex vertex = mesh.Vertices[i];
                        if (math.abs(vertex.Position.x) > 1e-6f || math.abs(vertex.Normal.x) < 0.5f) continue;
                        Assert.That(vertex.Normal.x > 0f, Is.EqualTo(inward));
                    }
                    Assert.That(inside.x, Is.GreaterThan(0f));
                }
            }
        }

        [Test]
        public void ExtrudedOutline_OpenRun_HasNoCapAndFacesLeftOfTravel()
        {
            var run = new[] { new float2(0f, 0f), new float2(0f, 3f) };   // travelling along +z
            var mesh = new AtlasMeshBuilder();
            mesh.AddExtrudedOutline(run, 2, 0f, 0.1f, float2.zero, false);
            Assert.That(mesh.TriangleCount, Is.EqualTo(12 * 1 * 2));
            for (int i = 0; i < mesh.VertexCount; i++) Assert.That(mesh.Vertices[i].Normal.x, Is.EqualTo(-1f).Within(1e-6f));
            AssertWindingAgreesWithNormals(mesh);

            mesh.Clear();
            mesh.AddExtrudedOutline(run, 2, 0f, 0.1f, float2.zero, false, true);
            for (int i = 0; i < mesh.VertexCount; i++) Assert.That(mesh.Vertices[i].Normal.x, Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void ExtrudedOutline_CapAndWallMeetAtTheSamePoints()
        {
            // 1.3 m is cut into 8 by halving and into 6 by equal steps: the wall must follow the cap.
            float2[] outline = LShape();
            var mesh = new AtlasMeshBuilder();
            mesh.AddExtrudedOutline(outline, outline.Length, 0f, 0.25f, float2.zero);
            int checkedPoints = 0;
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                AtlasVertex cap = mesh.Vertices[i];
                if (cap.Normal.y < 0.5f) continue;
                // A cap vertex on the edge x = 1, z in 1..2.3.
                if (math.abs(cap.Position.x - 1f) > 1e-6f || cap.Position.z < 1f + 1e-4f || cap.Position.z > 2.3f - 1e-4f) continue;
                bool found = false;
                for (int k = 0; k < mesh.VertexCount && !found; k++)
                {
                    AtlasVertex wall = mesh.Vertices[k];
                    found = wall.Normal.y < 0.5f && math.distance(wall.Position, cap.Position) < 1e-5f;
                }
                Assert.That(found, Is.True, "cap vertex at z = " + cap.Position.z + " has no wall vertex");
                checkedPoints++;
            }
            Assert.That(checkedPoints, Is.EqualTo(7));
        }

        [Test]
        public void OutlineCap_FacesDownWhenAsked()
        {
            float2[] outline = LShape();
            var mesh = new AtlasMeshBuilder();
            mesh.AddOutlineCap(outline, outline.Length, 2.4f, AtlasPalette.Uv(AtlasPalette.Ceiling), false);
            AssertWindingAgreesWithNormals(mesh);
            Assert.That(mesh.Vertices[0].Normal.y, Is.EqualTo(-1f));
            Assert.That(Area(mesh), Is.EqualTo(3.3f).Within(1e-3f));
            Assert.That(mesh.LongestEdge(), Is.LessThanOrEqualTo(Limit));
        }

        [Test]
        public void Subdivide_GuaranteesNoEdgeOverTheLimit_OnAwkwardTriangles()
        {
            var random = new System.Random(20261004);
            for (int round = 0; round < 60; round++)
            {
                var mesh = new AtlasMeshBuilder(8, 12);
                float3 normal = new float3(0f, 1f, 0f);
                // Slivers, needles and big triangles, in any orientation.
                float scale = round % 3 == 0 ? 6f : (round % 3 == 1 ? 1.1f : 0.3f);
                float3 a = Next(random) * scale, b = Next(random) * scale, c = Next(random) * scale;
                if (round % 5 == 0) c = math.lerp(a, b, 0.5f) + Next(random) * 0.01f;   // a sliver
                if (math.length(math.cross(b - a, c - a)) < 1e-6f) continue;
                mesh.AddTriangle(mesh.AddVertex(a, normal, float2.zero, new float2(0f, 0f)),
                                 mesh.AddVertex(b, normal, float2.zero, new float2(1f, 0f)),
                                 mesh.AddVertex(c, normal, float2.zero, new float2(0f, 1f)));
                float area = Area(mesh);
                float3 face = math.normalize(math.cross(b - a, c - a));

                int added = mesh.Subdivide();

                Assert.That(mesh.LongestEdge(), Is.LessThanOrEqualTo(Limit), "round " + round);
                Assert.That(mesh.TriangleCount, Is.EqualTo(1 + added));
                Assert.That(Area(mesh), Is.EqualTo(area).Within(1e-3f * math.max(area, 1f)), "the surface is unchanged");
                for (int t = 0; t < mesh.IndexCount; t += 3)
                {
                    float3 pa = mesh.Vertices[mesh.Indices[t]].Position, pb = mesh.Vertices[mesh.Indices[t + 1]].Position, pc = mesh.Vertices[mesh.Indices[t + 2]].Position;
                    float3 n = math.cross(pb - pa, pc - pa);
                    if (math.length(n) > 1e-7f) Assert.That(math.dot(math.normalize(n), face), Is.GreaterThan(0.9f), "winding kept");
                }
            }
        }

        static float3 Next(System.Random random)
        {
            return new float3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f);
        }

        [Test]
        public void Subdivide_LeavesShortTrianglesAlone()
        {
            var mesh = new AtlasMeshBuilder();
            mesh.AddQuadGrid(float3.zero, new float3(0.25f, 0f, 0f), new float3(0f, 0f, 0.1f), float2.zero, float2.zero, float2.zero, float2.zero);
            int before = mesh.TriangleCount;
            Assert.That(mesh.Subdivide(0.5f), Is.EqualTo(0));
            Assert.That(mesh.TriangleCount, Is.EqualTo(before));
        }

        [Test]
        public void Subdivide_SharedEdgeIsCutAtTheSamePoints_NoCrack()
        {
            // Two triangles that share the edge (0,0,0)-(3,0,0.7) but not its vertices, as two hard-edged faces do.
            var mesh = new AtlasMeshBuilder();
            float3 p = float3.zero, q = new float3(3f, 0f, 0.7f);
            float3 up = new float3(0f, 1f, 0f);
            mesh.AddTriangle(mesh.AddVertex(p, up, float2.zero, float2.zero), mesh.AddVertex(new float3(0.4f, 0f, 2f), up, float2.zero, float2.zero), mesh.AddVertex(q, up, float2.zero, float2.zero));
            int secondStart = mesh.VertexCount;
            mesh.AddTriangle(mesh.AddVertex(q, up, float2.zero, float2.zero), mesh.AddVertex(new float3(2.2f, 0f, -0.3f), up, float2.zero, float2.zero), mesh.AddVertex(p, up, float2.zero, float2.zero));
            mesh.Subdivide();
            Assert.That(mesh.LongestEdge(), Is.LessThanOrEqualTo(Limit));

            // Collect the points each side put on the shared edge: the sets must be equal.
            float3 direction = math.normalize(q - p);
            int onEdge = 0;
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                float3 position = mesh.Vertices[i].Position;
                float along = math.dot(position - p, direction);
                if (math.length(position - (p + direction * along)) > 1e-5f) continue;
                onEdge++;
                bool twin = false;
                for (int k = 0; k < mesh.VertexCount && !twin; k++)
                    twin = k != i && math.all(mesh.Vertices[k].Position == position);
                Assert.That(twin, Is.True, "a vertex on the shared edge has no twin on the other face");
            }
            Assert.That(onEdge, Is.GreaterThanOrEqualTo(2 * 17), "3.08 m is halved four times: 17 points a side");
            Assert.That(secondStart, Is.EqualTo(3));
        }

        [Test]
        public void Subdivide_SharedIndicesShareNewVertices_AndInterpolateAttributes()
        {
            var mesh = new AtlasMeshBuilder();
            mesh.AddQuad(float3.zero, new float3(1f, 0f, 0f), new float3(0f, 0f, 1f), new float2(0.25f, 0.5f));
            mesh.Vertices[0].Uv1 = new float2(0f, 0f);
            mesh.Vertices[3].Uv1 = new float2(4f, 4f);
            mesh.Subdivide(1.2f);   // only the diagonal is too long: one new vertex, shared by both triangles
            Assert.That(mesh.VertexCount, Is.EqualTo(5));
            Assert.That(mesh.TriangleCount, Is.EqualTo(4));
            AtlasVertex mid = mesh.Vertices[4];
            Assert.That(mid.Position.x, Is.EqualTo(0.5f));
            Assert.That(mid.Position.z, Is.EqualTo(0.5f));
            Assert.That(mid.Uv1.x, Is.EqualTo(2f));
            Assert.That(mid.Uv0.y, Is.EqualTo(0.5f));
            Assert.That(mid.Normal.y, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(mid.Shade, Is.EqualTo(mesh.Vertices[0].Shade));
            AssertWindingAgreesWithNormals(mesh);
        }

        [Test]
        public void Subdivide_Box_MeetsTheStrictRuleWithinTheFurnitureBudget()
        {
            var mesh = new AtlasMeshBuilder();
            mesh.AddBox(float3.zero, new float3(1.6f, 0.75f, 0.8f), 0.4f, float2.zero);
            Assert.That(mesh.LongestEdge(), Is.GreaterThan(Limit), "the grid's diagonals are longer than its sides");
            mesh.Subdivide();
            Assert.That(mesh.LongestEdge(), Is.LessThanOrEqualTo(Limit));
            Assert.That(mesh.WithinBudget(AtlasMeshBudget.BuiltInFurniture), Is.True);
            AssertWindingAgreesWithNormals(mesh);
            AssertUnitNormalsAndBounds(mesh);
        }

        [Test]
        public void Subdivide_RangeLeavesEarlierTrianglesUntouched()
        {
            var mesh = new AtlasMeshBuilder();
            mesh.AddQuad(float3.zero, new float3(2f, 0f, 0f), new float3(0f, 0f, 2f), float2.zero);
            int first = mesh.IndexCount;
            mesh.AddQuad(new float3(5f, 0f, 0f), new float3(2f, 0f, 0f), new float3(0f, 0f, 2f), float2.zero);
            mesh.Subdivide(0.25f, first);
            Assert.That(mesh.Indices[0], Is.EqualTo(0));
            Assert.That(mesh.Indices[1], Is.EqualTo(2));
            Assert.That(mesh.Indices[2], Is.EqualTo(3));
            Assert.That(mesh.LongestEdge(), Is.GreaterThan(2f));
            Assert.Throws<ArgumentOutOfRangeException>(() => mesh.Subdivide(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => mesh.Subdivide(0.25f, 4));
        }

        [Test]
        public void Clear_KeepsTheArrays_AndARebuildAllocatesNothing()
        {
            var mesh = new AtlasMeshBuilder();
            float2[] outline = LShape();
            Build(mesh, outline);
            int triangles = mesh.TriangleCount;
            AtlasVertex[] vertices = mesh.Vertices;
            Build(mesh, outline);   // second pass: the arrays and the scratch have their final size
            long before = GC.GetAllocatedBytesForCurrentThread();
            Build(mesh, outline);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0));
            Assert.That(mesh.TriangleCount, Is.EqualTo(triangles));
            Assert.That(mesh.Vertices.Length, Is.GreaterThanOrEqualTo(vertices.Length));
        }

        static void Build(AtlasMeshBuilder mesh, float2[] outline)
        {
            mesh.Clear();
            mesh.AddFloorGrid(new float2(-2f, -2f), new float2(2f, 2f), 0f, float2.zero);
            mesh.AddBox(new float3(1f, 0f, 1f), new float3(0.8f, 0.7f, 0.5f), 0.2f, float2.zero);
            mesh.AddPost(float3.zero, 0.05f, 1.2f, float2.zero);
            mesh.AddExtrudedOutline(outline, outline.Length, 0f, 0.3f, float2.zero);
            mesh.Subdivide();
        }

        [Test]
        public void Empty_HasZeroBoundsAndCounts()
        {
            var mesh = new AtlasMeshBuilder();
            Assert.That(mesh.IsEmpty, Is.True);
            Assert.That(mesh.TriangleCount, Is.EqualTo(0));
            Assert.That(mesh.LongestEdge(), Is.EqualTo(0f));
            Assert.That(math.all(mesh.BoundsSize == float3.zero), Is.True);
            Assert.That(math.all(mesh.BoundsCentre == float3.zero), Is.True);
        }

        [Test]
        public void Append_OffsetsTheIndices()
        {
            var a = new AtlasMeshBuilder();
            var b = new AtlasMeshBuilder();
            a.AddQuad(float3.zero, new float3(1f, 0f, 0f), new float3(0f, 0f, 1f), float2.zero);
            b.AddQuad(new float3(5f, 1f, 0f), new float3(1f, 0f, 0f), new float3(0f, 0f, 1f), float2.zero);
            a.Append(b);
            Assert.That(a.VertexCount, Is.EqualTo(8));
            Assert.That(a.TriangleCount, Is.EqualTo(4));
            Assert.That(a.Indices[6], Is.EqualTo(4));
            Assert.That(a.BoundsMax.x, Is.EqualTo(6f));
            AssertWindingAgreesWithNormals(a);
            AssertUnitNormalsAndBounds(a);
        }

        [Test]
        public void Append_MeshData_MovesItByTheOffset()
        {
            var source = new MeshData(8, 8);
            source.AddQuadGrid(float3.zero, new float3(1f, 0f, 0f), new float3(0f, 0f, 1f), new float2(0.25f, 0.5f),
                               new float2(3f, 4f), new float2(1f, 0f), new float2(0f, 1f));
            var a = new AtlasMeshBuilder();
            a.AddQuad(float3.zero, new float3(1f, 0f, 0f), new float3(0f, 0f, 1f), float2.zero);
            var offset = new float3(2f, 0.5f, -1f);
            a.Append(source, offset);

            Assert.That(a.VertexCount, Is.EqualTo(4 + source.VertexCount));
            Assert.That(a.TriangleCount, Is.EqualTo(2 + source.TriangleCount));
            Assert.That(a.Indices[6], Is.EqualTo(4 + source.Indices[0]));
            for (int v = 0; v < source.VertexCount; v++)
            {
                AtlasVertex copied = a.Vertices[4 + v], original = source.Vertices[v];
                Assert.That(copied.Position, Is.EqualTo(original.Position + offset));
                Assert.That(copied.Normal, Is.EqualTo(original.Normal));
                Assert.That(copied.Color, Is.EqualTo(original.Color));
                Assert.That(copied.Uv0, Is.EqualTo(original.Uv0));
                Assert.That(copied.Uv1, Is.EqualTo(original.Uv1), "the line coordinates do not move with the piece");
            }
            Assert.That(a.BoundsMin, Is.EqualTo(new float3(0f, 0f, -1f)));
            Assert.That(a.BoundsMax, Is.EqualTo(new float3(3f, 0.5f, 1f)));
            AssertWindingAgreesWithNormals(a);

            // An empty source adds nothing and leaves the bounds alone.
            var empty = new MeshData(4, 6);
            a.Append(empty, new float3(100f, 100f, 100f));
            Assert.That(a.VertexCount, Is.EqualTo(4 + source.VertexCount));
            Assert.That(a.BoundsMax, Is.EqualTo(new float3(3f, 0.5f, 1f)));
        }

        [Test]
        public void Growth_FromATinyCapacity_KeepsTheData()
        {
            var mesh = new AtlasMeshBuilder(1, 1);
            mesh.AddQuadGrid(float3.zero, new float3(4f, 0f, 0f), new float3(0f, 0f, 5f), float2.zero, float2.zero, float2.zero, float2.zero);
            Assert.That(mesh.TriangleCount, Is.EqualTo(640));
            Assert.That(mesh.VertexCount, Is.EqualTo(17 * 21));
            AssertWindingAgreesWithNormals(mesh);
        }

        [Test]
        public void Budgets_FollowThePlan()
        {
            Assert.That(AtlasMeshBudget.FurnitureTarget(1), Is.EqualTo(5000));
            Assert.That(AtlasMeshBudget.FurnitureTarget(12), Is.EqualTo(5000));
            Assert.That(AtlasMeshBudget.FurnitureTarget(15), Is.EqualTo(4000));
            Assert.That(AtlasMeshBudget.FurnitureTarget(20), Is.EqualTo(3000));
            Assert.That(AtlasMeshBudget.FurnitureTarget(40), Is.EqualTo(3000));
            Assert.That(AtlasMeshBudget.FurnitureTarget(0), Is.EqualTo(5000));
            Assert.That(AtlasMeshBudget.FurnitureTarget(20) * AtlasMeshBudget.FurnitureObjects, Is.LessThanOrEqualTo(AtlasMeshBudget.FurnitureTotal));
            Assert.That(AtlasMeshBudget.Fits(3000, AtlasMeshBudget.HomeShell), Is.True);
            Assert.That(AtlasMeshBudget.Fits(3001, AtlasMeshBudget.HomeShell), Is.False);

            // The Home shell of a 4 x 5 m room on the 0.25 m grid: floor plus 2.4 m walls stays in budget.
            int shell = AtlasMeshBudget.QuadGridTriangles(4f, 5f)
                        + 2 * AtlasMeshBudget.QuadGridTriangles(4f, 2.4f) + 2 * AtlasMeshBudget.QuadGridTriangles(5f, 2.4f);
            Assert.That(shell, Is.EqualTo(640 + 2 * 320 + 2 * 400));
            Assert.That(AtlasMeshBudget.Fits(shell, AtlasMeshBudget.HomeShell), Is.True);
        }
    }
}
