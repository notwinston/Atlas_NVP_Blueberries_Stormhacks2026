using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using static Atlas.Objects.Tests.ObjectsTestData;

namespace Atlas.Objects.Tests
{
    /// <summary>
    /// MeshOps (docs/plans/standalone-phone/INDEX.md section 6.4 and T5): crack-free edge splitting with shared
    /// midpoints, deterministic vertex clustering, small-part removal by area share, and the shade bake of
    /// FallbackMeshBuilder (room light turned by yaw, floor occlusion 0.7 to 1 over 0.3 m).
    /// </summary>
    public class MeshOpsTests
    {
        static AtlasVertex Vertex(float3 p, float3 n, int cell)
        {
            return new AtlasVertex { Position = p, Normal = n, Color = AtlasVertex.PackColor(200), Uv0 = PaletteUv(cell), Uv1 = float2.zero };
        }

        /// <summary>A w x d square on the floor plane at height y, two triangles sharing one diagonal, facing up.</summary>
        static MeshData Square(float w, float d, float y, int cell)
        {
            var m = new MeshData();
            var up = new float3(0f, 1f, 0f);
            int a = m.AddVertex(Vertex(new float3(0f, y, 0f), up, cell));
            int b = m.AddVertex(Vertex(new float3(w, y, 0f), up, cell));
            int c = m.AddVertex(Vertex(new float3(w, y, d), up, cell));
            int e = m.AddVertex(Vertex(new float3(0f, y, d), up, cell));
            // Clockwise seen from above (+Y): a, e, c and a, c, b.
            m.AddTriangle(a, e, c);
            m.AddTriangle(a, c, b);
            return m;
        }

        // ------------------------------------------------------------------ SplitLongEdges

        [TestCase(0.25f)]
        [TestCase(0.1f)]
        public void SplitLongEdges_Square_EdgesShort_NoTJunctions_AreaAndAttributesKept(float maxEdge)
        {
            MeshData m = Square(1f, 1f, 0.4f, 81);
            MeshOps.SplitLongEdges(m, maxEdge);
            AssertIndicesValid(m, "split square");
            Assert.That(m.LongestEdge(), Is.LessThanOrEqualTo(maxEdge + 1e-5f));
            Assert.That(TJunctions(m), Is.EqualTo(0));
            Assert.That(Area(m), Is.EqualTo(1f).Within(1e-4f));
            for (int k = 0; k < m.VertexCount; k++)
            {
                Assert.That(Cell(m.Vertices[k].Uv0), Is.EqualTo(81));
                Assert.That(m.Vertices[k].Normal.y, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(m.Vertices[k].Position.y, Is.EqualTo(0.4f).Within(1e-6f));
            }
            // Winding kept: every triangle still faces up (clockwise seen from +Y).
            for (int k = 0; k < m.IndexCount; k += 3)
            {
                float3 a = m.Vertices[m.Indices[k]].Position, b = m.Vertices[m.Indices[k + 1]].Position,
                    c = m.Vertices[m.Indices[k + 2]].Position;
                Assert.That(math.cross(b - a, c - a).y, Is.GreaterThan(0f), "triangle " + k / 3 + " flipped");
            }
        }

        [Test]
        public void SplitLongEdges_UnweldedClosedBox_StaysClosed()
        {
            // AddBox with a huge maxEdge: six faces of two triangles each, every face with its own four vertices
            // (as a flat-shaded prop is stored). Each shared edge must be cut alike from both sides.
            var m = new MeshData();
            m.AddBox(new float3(0.1f, 0f, -0.2f), new float3(1.1f, 0.7f, 0.9f), 0.4f, PaletteUv(70), withBottom: true, maxEdge: 10f);
            Assume.That(m.TriangleCount, Is.EqualTo(12));
            Assume.That(OddEdges(m), Is.EqualTo(0));
            MeshOps.SplitLongEdges(m, 0.25f);
            Assert.That(m.LongestEdge(), Is.LessThanOrEqualTo(0.25f + 1e-5f));
            Assert.That(OddEdges(m), Is.EqualTo(0), "a crack: an edge split on one side only");
            Assert.That(TJunctions(m), Is.EqualTo(0));
        }

        [Test]
        public void SplitLongEdges_ShortEdges_Unchanged()
        {
            MeshData m = Square(0.1f, 0.15f, 0f, 64);
            MeshData before = Copy(m);
            MeshOps.SplitLongEdges(m, 0.25f);
            AssertMeshEqual(before, m, "nothing to split");
        }

        [Test]
        public void SplitLongEdges_IsDeterministic()
        {
            MeshData a = Square(1.3f, 0.7f, 0f, 64), b = Square(1.3f, 0.7f, 0f, 64);
            MeshOps.SplitLongEdges(a, 0.2f);
            MeshOps.SplitLongEdges(b, 0.2f);
            AssertMeshEqual(a, b, "split twice");
        }

        [Test]
        public void SplitLongEdges_Empty_NoException()
        {
            var m = new MeshData();
            Assert.DoesNotThrow(() => MeshOps.SplitLongEdges(m, 0.25f));
            Assert.That(m.TriangleCount, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ ClusterSimplify

        static MeshData FineSquare()
        {
            var m = new MeshData();
            // 1 m square, 2 cm cells: 50 x 50 x 2 = 5000 triangles, facing up.
            m.AddQuadGrid(float3.zero, new float3(1f, 0f, 0f), new float3(0f, 0f, 1f), PaletteUv(90),
                          float2.zero, float2.zero, float2.zero, 1f, 255, 0.02f);
            return m;
        }

        [Test]
        public void ClusterSimplify_FewerTriangles_NoDegenerates_InsideTheOriginalBounds()
        {
            MeshData m = FineSquare();
            int before = m.TriangleCount;
            Assume.That(before, Is.EqualTo(5000));
            MeshOps.ClusterSimplify(m, 0.1f);
            AssertIndicesValid(m, "simplified");
            Assert.That(m.TriangleCount, Is.GreaterThan(0));
            // About 10 x 10 clusters: at most 2 triangles per pair of neighbouring cells.
            Assert.That(m.TriangleCount, Is.LessThan(400));
            for (int k = 0; k < m.IndexCount; k += 3)
            {
                int a = m.Indices[k], b = m.Indices[k + 1], c = m.Indices[k + 2];
                Assert.That(a != b && b != c && a != c, Is.True, "degenerate triangle " + k / 3);
            }
            UsedBounds(m, out float3 min, out float3 max);
            Assert.That(min.x, Is.GreaterThanOrEqualTo(-1e-5f));
            Assert.That(min.z, Is.GreaterThanOrEqualTo(-1e-5f));
            Assert.That(max.x, Is.LessThanOrEqualTo(1f + 1e-5f));
            Assert.That(max.z, Is.LessThanOrEqualTo(1f + 1e-5f));
            Assert.That(min.y, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(max.y, Is.EqualTo(0f).Within(1e-5f));
            // Most of the square is still covered.
            Assert.That(Area(m), Is.GreaterThan(0.6f));
        }

        [Test]
        public void ClusterSimplify_IsDeterministic()
        {
            MeshData a = FineSquare(), b = FineSquare();
            MeshOps.ClusterSimplify(a, 0.07f);
            MeshOps.ClusterSimplify(b, 0.07f);
            AssertMeshEqual(a, b, "clustered twice");
        }

        [Test]
        public void ClusterSimplify_Empty_NoException()
        {
            var m = new MeshData();
            Assert.DoesNotThrow(() => MeshOps.ClusterSimplify(m, 0.03f));
            Assert.That(m.TriangleCount, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ DropSmallParts

        [Test]
        public void DropSmallParts_RemovesPartsUnderTheShare_KeepsTheRest()
        {
            var m = new MeshData();
            // Big: a 1 m box, 6 m2. Medium: 0.2 m box, 0.24 m2 (3.8 %, each face 0.6 %). Tiny: 3 cm box, 0.0054 m2 (0.09 %).
            m.AddBox(new float3(0f, 0f, 0f), new float3(1f), 0f, PaletteUv(64), withBottom: true);
            int bigTriangles = m.TriangleCount;
            m.AddBox(new float3(2f, 0f, 0f), new float3(0.2f), 0f, PaletteUv(64), withBottom: true);
            int mediumTriangles = m.TriangleCount - bigTriangles;
            m.AddBox(new float3(-2f, 0f, 0f), new float3(0.03f), 0f, PaletteUv(64), withBottom: true);
            int tinyTriangles = m.TriangleCount - bigTriangles - mediumTriangles;
            Assume.That(tinyTriangles, Is.GreaterThan(0));

            MeshOps.DropSmallParts(m, 0.005f);
            AssertIndicesValid(m, "after DropSmallParts");
            Assert.That(m.TriangleCount, Is.EqualTo(bigTriangles + mediumTriangles));
            for (int k = 0; k < m.IndexCount; k++)
                Assert.That(m.Vertices[m.Indices[k]].Position.x, Is.GreaterThan(-1.5f), "a tiny-box triangle survived");
        }

        [Test]
        public void DropSmallParts_NothingSmall_Unchanged()
        {
            var m = new MeshData();
            m.AddBox(new float3(0f), new float3(1f), 0f, PaletteUv(64), withBottom: true);
            m.AddBox(new float3(3f, 0f, 0f), new float3(0.8f), 0f, PaletteUv(64), withBottom: true);
            int before = m.TriangleCount;
            float area = Area(m);
            MeshOps.DropSmallParts(m, 0.005f);
            Assert.That(m.TriangleCount, Is.EqualTo(before));
            Assert.That(Area(m), Is.EqualTo(area).Within(1e-4f));
        }

        [Test]
        public void DropSmallParts_Empty_NoException()
        {
            var m = new MeshData();
            Assert.DoesNotThrow(() => MeshOps.DropSmallParts(m, 0.005f));
            Assert.That(m.TriangleCount, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ BakeShade

        static IEnumerable<TestCaseData> ShadeCases()
        {
            float[] yaws = { 0f, 0.9f, -2.2f };
            foreach (float yaw in yaws)
                yield return new TestCaseData(yaw);
        }

        /// <summary>
        /// COLOR.r of each vertex is round(255 * saturate(FaceShade(n') * occlusion)), with n' the vertex normal
        /// turned by the object's yaw into the room (Unity's sense: local +X goes to (cos, 0, -sin)) and
        /// occlusion = lerp(0.7, 1, saturate(y / 0.3)). One byte of slack for float order.
        /// </summary>
        [TestCaseSource(nameof(ShadeCases))]
        public void BakeShade_RoomLightTurnedByYaw_FloorOcclusion(float yaw)
        {
            var m = new MeshData();
            float2 none = float2.zero;
            // Faces with normals +Y, +X, -X, +Z, -Z, each spanning heights 0..0.6 so occlusion varies.
            // AddQuadGrid's front is cross(v, u).
            m.AddQuadGrid(new float3(-0.5f, 0.6f, -0.5f), new float3(1f, 0f, 0f), new float3(0f, 0f, 1f), PaletteUv(64), none, none, none, 1f, 255, 0.1f);   // +Y
            m.AddQuadGrid(new float3(0.5f, 0f, -0.5f), new float3(0f, 0f, 1f), new float3(0f, 0.6f, 0f), PaletteUv(64), none, none, none, 1f, 255, 0.1f);   // +X
            m.AddQuadGrid(new float3(-0.5f, 0f, -0.5f), new float3(0f, 0.6f, 0f), new float3(0f, 0f, 1f), PaletteUv(64), none, none, none, 1f, 255, 0.1f);  // -X
            m.AddQuadGrid(new float3(-0.5f, 0f, 0.5f), new float3(0f, 0.6f, 0f), new float3(1f, 0f, 0f), PaletteUv(64), none, none, none, 1f, 255, 0.1f);   // +Z
            m.AddQuadGrid(new float3(-0.5f, 0f, -0.5f), new float3(1f, 0f, 0f), new float3(0f, 0.6f, 0f), PaletteUv(64), none, none, none, 1f, 255, 0.1f);  // -Z
            var seen = new HashSet<int>();
            for (int k = 0; k < m.VertexCount; k++)
            {
                float3 n = m.Vertices[k].Normal;
                seen.Add((int)math.round(n.x) * 9 + (int)math.round(n.y) * 3 + (int)math.round(n.z));
                m.Vertices[k].Color = AtlasVertex.PackColor(0, 255);   // so the bake must write it
            }
            Assume.That(seen.Count, Is.EqualTo(5), "five face directions");

            MeshOps.BakeShade(m, yaw);

            quaternion turn = quaternion.RotateY(yaw);
            for (int k = 0; k < m.VertexCount; k++)
            {
                AtlasVertex v = m.Vertices[k];
                float3 inRoom = math.mul(turn, v.Normal);
                float occlusion = math.lerp(0.7f, 1f, math.saturate(v.Position.y / 0.3f));
                int expected = AtlasShading.ToByte(AtlasShading.FaceShade(inRoom), occlusion);
                Assert.That((int)v.Shade, Is.EqualTo(expected).Within(1),
                            "vertex " + k + " normal " + v.Normal + " y " + v.Position.y);
            }
        }

        [Test]
        public void BakeShade_Empty_NoException()
        {
            var m = new MeshData();
            Assert.DoesNotThrow(() => MeshOps.BakeShade(m, 0.7f));
            Assert.That(m.VertexCount, Is.EqualTo(0));
        }

        [Test]
        public void BakeShade_KeepsPositionsNormalsAndCells()
        {
            MeshData m = Square(0.6f, 0.4f, 0.1f, 99);
            MeshData before = Copy(m);
            MeshOps.BakeShade(m, 0.3f);
            Assert.That(m.VertexCount, Is.EqualTo(before.VertexCount));
            Assert.That(m.IndexCount, Is.EqualTo(before.IndexCount));
            for (int k = 0; k < m.VertexCount; k++)
            {
                Assert.That(m.Vertices[k].Position, Is.EqualTo(before.Vertices[k].Position));
                Assert.That(m.Vertices[k].Normal, Is.EqualTo(before.Vertices[k].Normal));
                Assert.That(m.Vertices[k].Uv0, Is.EqualTo(before.Vertices[k].Uv0));
            }
        }
    }
}
