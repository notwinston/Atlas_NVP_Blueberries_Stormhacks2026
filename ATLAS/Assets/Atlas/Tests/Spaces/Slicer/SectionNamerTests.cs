using System;
using Atlas.Kernel.FourD;
using Atlas.Spaces.Slicer;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Spaces.Tests
{
    /// <summary>
    /// Names of the Slicer's cross-sections (Master Plan section 10.1.3 and task P2-A02: "correct name at five test offsets
    /// per polytope"). The bodies have circumradius 1; the tesseract rests on a corner, so pushing it through the cut gives
    /// tetrahedron, truncated tetrahedron, octahedron, truncated tetrahedron, tetrahedron (section 5.1).
    /// </summary>
    public class SectionNamerTests
    {
        static readonly float4 PlusW = new float4(0f, 0f, 0f, 1f);

        static SlicerSection Bound(SlicerObject body)
        {
            var s = new SlicerSection();
            s.Bind(body);
            return s;
        }

        // w of every vertex of a body at rest (no turn), sorted, distinct values only.
        static float[] RestW(SlicerObject body)
        {
            Mesh4D mesh = SlicerObjects.Build(body);
            SlicerController.GetTransform(body, 0f, 0f, 0f, out float4x4 m, out float4 t);
            var w = new System.Collections.Generic.List<float>();
            foreach (float4 v in mesh.Vertices)
            {
                float x = math.mul(m, v).w + t.w;
                if (!w.Exists(o => math.abs(o - x) < 1e-4f)) w.Add(x);
            }
            w.Sort();
            return w.ToArray();
        }

        // Acceptance P2-A02: the corner-first tesseract through the cut, five offsets
        [TestCase(-0.75f, SectionShape.Tetrahedron)]
        [TestCase(-0.25f, SectionShape.TruncatedTetrahedron)]
        [TestCase(0.0f, SectionShape.Octahedron)]
        [TestCase(0.25f, SectionShape.TruncatedTetrahedron)]
        [TestCase(0.75f, SectionShape.Tetrahedron)]
        public void Section_TesseractCornerFirst(float offset, SectionShape expected)
        {
            SectionInfo s = Bound(SlicerObject.Tesseract).Name(0f, 0f, 0f, offset);
            Assert.AreEqual(expected, s.Shape, $"w = {offset}: {s.Faces} faces, {s.Vertices} vertices");
            Assert.AreEqual(2, s.Vertices - s.Edges + s.Faces, "Euler: V - E + F = 2");
        }

        // The octahedron at the middle: 8 triangles, 6 vertices, 12 edges
        [Test]
        public void Section_TesseractMiddleIsTheOctahedron()
        {
            SectionInfo s = Bound(SlicerObject.Tesseract).Name(0f, 0f, 0f, 0f);
            Assert.AreEqual(8, s.Faces); Assert.AreEqual(8, s.Triangles);
            Assert.AreEqual(6, s.Vertices); Assert.AreEqual(12, s.Edges);
            Assert.AreEqual("octahedron", s.Name);
        }

        // Cell-first, the tesseract cuts into a cube at every offset inside it
        [TestCase(0.0f)]
        [TestCase(0.3f)]
        [TestCase(-0.45f)]
        public void Section_TesseractCellFirstIsACube(float offset)
        {
            SlicerSection s = Bound(SlicerObject.Tesseract);
            SectionInfo info = s.Name(float4x4.identity, float4.zero, PlusW, offset);
            Assert.AreEqual(SectionShape.Cube, info.Shape, $"{info.Faces} faces");
            Assert.AreEqual(6, info.Quads); Assert.AreEqual(8, info.Vertices);
        }

        // A turned tesseract cut cell-first is a box, not a cube, once its faces are no longer squares of one size
        [Test]
        public void Section_TurnedTesseractIsNoLongerACube()
        {
            SlicerSection s = Bound(SlicerObject.Tesseract);
            var m = (float4x4)Rotation4D.Plane(0, 3, math.radians(20.0));   // XW turn of the cube cell-first
            SectionInfo info = s.Name(m, float4.zero, PlusW, 0f);
            Assert.AreEqual(SectionShape.Box, info.Shape, $"{info.Faces} faces, {info.Quads} quads");
        }

        // The 5-cell rests on a cell (four vertices at one w, the fifth above): every cut at rest is a tetrahedron. Turned so
        // that its vertices spread along w, the cut that leaves one vertex below is a tetrahedron and the one that leaves two
        // below and three above is a triangular prism.
        [Test]
        public void Section_Cell5()
        {
            SlicerSection s = Bound(SlicerObject.Cell5);
            foreach (float w in new[] { -0.2f, 0f, 0.6f })   // the bottom cell lies at w = -0.25, the top vertex at +1
                Assert.AreEqual(SectionShape.Tetrahedron, s.Name(0f, 0f, 0f, w).Shape, $"at rest, w = {w}");

            const float xw = 0.9f, yw = 0.4f;
            Mesh4D mesh = SlicerObjects.Build(SlicerObject.Cell5);
            SlicerController.GetTransform(SlicerObject.Cell5, xw, yw, 0f, out float4x4 m, out float4 t);
            var ws = new float[mesh.VertexCount];
            for (int i = 0; i < ws.Length; i++) ws[i] = math.mul(m, mesh.Vertices[i]).w + t.w;
            Array.Sort(ws);
            Assert.That(ws[1] - ws[0], Is.GreaterThan(0.05f), "the turn spreads the vertices along w");
            Assert.That(ws[2] - ws[1], Is.GreaterThan(0.05f));
            Assert.AreEqual(SectionShape.Tetrahedron, s.Name(xw, yw, 0f, 0.5f * (ws[0] + ws[1])).Shape, "one vertex below");
            Assert.AreEqual(SectionShape.TriangularPrism, s.Name(xw, yw, 0f, 0.5f * (ws[1] + ws[2])).Shape, "two below, three above");
        }

        // The 16-cell stands on a vertex: every cut between it and the equator is an octahedron
        [Test]
        public void Section_Cell16NearAVertexIsAnOctahedron()
        {
            float[] w = RestW(SlicerObject.Cell16);
            SlicerSection s = Bound(SlicerObject.Cell16);
            SectionInfo info = s.Name(0f, 0f, 0f, 0.5f * (w[0] + w[1]));
            Assert.AreEqual(SectionShape.Octahedron, info.Shape, $"{info.Faces} faces, {info.Vertices} vertices");
        }

        // The 24-cell rests on an octahedral cell (vertices at w = -1/sqrt2, 0, +1/sqrt2): between that cell and the middle
        // the cut is a truncated octahedron (6 squares, 8 hexagons), and the middle cut through its 12 equator vertices is a
        // cuboctahedron
        [Test]
        public void Section_Cell24CellFirst()
        {
            float[] w = RestW(SlicerObject.Cell24);
            Assert.AreEqual(3, w.Length, "three layers of vertices along w");
            SlicerSection s = Bound(SlicerObject.Cell24);
            SectionInfo low = s.Name(0f, 0f, 0f, 0.5f * (w[0] + w[1]));
            Assert.AreEqual(SectionShape.TruncatedOctahedron, low.Shape, $"{low.Faces} faces, {low.Quads} quads, {low.Hexagons} hexagons");
            Assert.AreEqual(24, low.Vertices);
            SectionInfo middle = s.Name(0f, 0f, 0f, 0f);
            Assert.AreEqual(SectionShape.Cuboctahedron, middle.Shape, $"{middle.Faces} faces, {middle.Vertices} vertices");
        }

        // Every section of every polytope, under random turns and cuts, is a closed polyhedron: V - E + F = 2
        [Test]
        public void Section_EveryCutIsAClosedPolyhedron()
        {
            var rng = new Unity.Mathematics.Random(1234);
            foreach (SlicerObject body in new[] { SlicerObject.Tesseract, SlicerObject.Cell5, SlicerObject.Cell16, SlicerObject.Cell24 })
            {
                SlicerSection s = Bound(body);
                int named = 0;
                for (int i = 0; i < 400; i++)
                {
                    float3 turn = rng.NextFloat3(-math.PI, math.PI);
                    float offset = rng.NextFloat(-0.9f, 0.9f);
                    SectionInfo info = s.Name(turn.x, turn.y, turn.z, offset);
                    if (info.Faces == 0) continue;
                    Assert.AreEqual(2, info.Vertices - info.Edges + info.Faces,
                        $"{body} turn {turn} w {offset}: V {info.Vertices} E {info.Edges} F {info.Faces}");
                    Assert.GreaterOrEqual(info.Faces, 4);
                    if (info.Shape != SectionShape.Polyhedron) named++;
                }
                Assert.Greater(named, 0, $"{body}: some section has a name");
            }
        }

        // Beyond the body there is nothing to name
        [Test]
        public void Section_MissIsNothing()
        {
            Assert.AreEqual(SectionShape.None, Bound(SlicerObject.Tesseract).Name(0f, 0f, 0f, 1.08f).Shape);
            Assert.AreEqual("nothing", SectionNamer.Name(SectionShape.None));
        }

        // The ball and the tube cut into spheres
        [Test]
        public void Section_SmoothBodiesAreSpheres()
        {
            Assert.AreEqual(SectionShape.Sphere, Bound(SlicerObject.Ball).Name(0f, 0f, 0f, 0.2f).Shape);
            Assert.AreEqual(SectionShape.Sphere, Bound(SlicerObject.Tube).Name(0f, 0f, 0f, 0f).Shape);
            Assert.IsFalse(SlicerSection.IsPolytope(SlicerObject.Ball));
            Assert.IsTrue(SlicerSection.IsPolytope(SlicerObject.Cell24));
        }

        // The face histogram of each named shape
        [Test]
        public void Classify_FaceHistograms()
        {
            SectionShape C(int t, int q, int p, int h, int v = 0, bool eq = true)
                => SectionNamer.Classify(new SectionInfo { Triangles = t, Quads = q, Pentagons = p, Hexagons = h, Vertices = v }, eq);
            Assert.AreEqual(SectionShape.Tetrahedron, C(4, 0, 0, 0));
            Assert.AreEqual(SectionShape.TriangularPrism, C(2, 3, 0, 0));
            Assert.AreEqual(SectionShape.SquarePyramid, C(4, 1, 0, 0));
            Assert.AreEqual(SectionShape.Cube, C(0, 6, 0, 0));
            Assert.AreEqual(SectionShape.Box, C(0, 6, 0, 0, eq: false));
            Assert.AreEqual(SectionShape.Octahedron, C(8, 0, 0, 0));
            Assert.AreEqual(SectionShape.PentagonalPrism, C(0, 5, 2, 0));
            Assert.AreEqual(SectionShape.HexagonalPrism, C(0, 6, 0, 2));
            Assert.AreEqual(SectionShape.TruncatedTetrahedron, C(4, 0, 0, 4));
            Assert.AreEqual(SectionShape.Cuboctahedron, C(8, 6, 0, 0));
            Assert.AreEqual(SectionShape.TruncatedOctahedron, C(0, 6, 0, 8));
            Assert.AreEqual(SectionShape.RhombicDodecahedron, C(0, 12, 0, 0, 14));
            Assert.AreEqual(SectionShape.Polyhedron, C(6, 2, 1, 0));
        }

#if ATLAS_HEADLESS
        // A frame that names the slice allocates nothing (the buffers are sized when the body is bound). Measured in three
        // rounds, the best counts: the runtime's tiered compiler may allocate once while it recompiles the test's own loop.
        [Test]
        public void Section_NamingAllocatesNothing()
        {
            SlicerSection s = Bound(SlicerObject.Cell24);
            long best = long.MaxValue;
            for (int round = 0; round < 3; round++) best = Math.Min(best, Round(s));
            Assert.AreEqual(0, best);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static long Round(SlicerSection s)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) s.Name(0.1f * (i % 20), 0.2f, 0.3f, -0.5f + 0.05f * (i % 20));
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
#endif
    }
}
