using System;
using Atlas.Kernel.FourD;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Kernel.Tests
{
    /// <summary>
    /// Acceptance tests of the 4D slicer (Master Plan section 11.1: Slicer_Tesseract, Slicer_Ball,
    /// Slicer_MovingSphere; R2 sections D.5 and D.10).
    /// </summary>
    public class SlicerTests
    {
        // The plan states the corner-first cut of the unit tesseract [0, 1]^4 as x + y + z + w = c. Our
        // tesseract is that cube centred on the origin, and CornerFirst() turns (1, 1, 1, 1) / 2 onto +w, so
        // the same cut is w = (c - 2) / 2.
        private static void CornerFirstCut(SliceHarness slice, float c)
        {
            slice.Slice(Rotation4D.CornerFirst(), (c - 2f) / 2f);
        }

        [Test]
        public void Slicer_Tesseract()
        {
            var slice = new SliceHarness(Polytopes4D.Tesseract());

            // A regular tetrahedron of edge sqrt(2) / 2: area sqrt(3) / 2.
            CornerFirstCut(slice, 0.5f);
            Assert.That(slice.Area(), Is.EqualTo(0.8660).Within(0.00005));
            Assert.That(slice.Area(), Is.EqualTo(Math.Sqrt(3.0) / 2.0).Within(1e-5));

            // A regular octahedron of edge sqrt(2): area 4 sqrt(3).
            CornerFirstCut(slice, 2.0f);
            Assert.That(slice.Area(), Is.EqualTo(6.9282).Within(0.00005));
            Assert.That(slice.Area(), Is.EqualTo(4.0 * Math.Sqrt(3.0)).Within(1e-5));

            // In between, a truncated tetrahedron: the tetrahedron of area 2 sqrt(3) c^2 with four corners
            // cut off, each removing sqrt(3) (c - 1)^2.
            CornerFirstCut(slice, 1.5f);
            Assert.That(slice.Area(), Is.EqualTo(3.5 * Math.Sqrt(3.0)).Within(1e-4));

            // The slice is symmetric about the centre and empty outside the body.
            CornerFirstCut(slice, 3.5f);
            Assert.That(slice.Area(), Is.EqualTo(Math.Sqrt(3.0) / 2.0).Within(1e-5));
            CornerFirstCut(slice, -0.1f);
            Assert.That(slice.TriangleCount, Is.EqualTo(0));
            CornerFirstCut(slice, 4.1f);
            Assert.That(slice.TriangleCount, Is.EqualTo(0));

            // The same cut stated the other way: an untouched tesseract and the normal (1, 1, 1, 1) / 2.
            // The picture drops w, so it is foreshortened, but it is cut from the same tetrahedra.
            CornerFirstCut(slice, 2.0f);
            int cutByRotation = slice.TetsCut;
            slice.Slice(float4x4.identity, float4.zero, new float4(0.5f), 0f);
            Assert.That(slice.TetsCut, Is.EqualTo(cutByRotation));
        }

        [Test]
        public void Slicer_Tesseract_CellFirstSliceIsTheUnitCube()
        {
            var slice = new SliceHarness(Polytopes4D.Tesseract());
            slice.Slice(double4x4.identity, 0.13f);
            Assert.That(slice.Area(), Is.EqualTo(6f).Within(1e-5f));
            Assert.That(slice.Extent(new float3(1f, 0f, 0f)), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(slice.Extent(new float3(0f, 1f, 1f)), Is.EqualTo(math.SQRT2).Within(1e-6f));
            Assert.That(slice.TriangleCount, Is.EqualTo(slice.VertexCount / 3));

            // Each of the six side cells gives one face of the cube; the two cells at w = +-0.5 are not cut.
            for (int i = 0; i < slice.VertexCount; i++)
                Assert.That(slice.Cells[i], Is.InRange(0, 5));
        }

        [Test]
        public void Slicer_Ball()
        {
            var slice = new SliceHarness(Polytopes4D.Ball(24));
            double4x4[] turns =
            {
                double4x4.identity,
                Rotation4D.CornerFirst(),
                Rotation4D.FromPlaneAngles(0.3, -0.8, 0.7, 1.1, 0.45, -1.3),
            };

            foreach (float c in new[] { 0f, 0.5f, 0.9f })
            {
                float radius = math.sqrt(1f - c * c);
                foreach (double4x4 turn in turns)
                {
                    slice.Slice(turn, c);
                    Assert.That(slice.TriangleCount, Is.GreaterThan(0));

                    double fromArea = Math.Sqrt(slice.Area() / (4.0 * Math.PI));
                    Assert.That(fromArea, Is.EqualTo(radius).Within(0.01 * radius), $"radius from area at c = {c}");

                    for (int axis = 0; axis < 3; axis++)
                    {
                        float3 direction = float3.zero;
                        direction[axis] = 1f;
                        Assert.That(0.5f * slice.Extent(direction), Is.EqualTo(radius).Within(0.01f * radius),
                                    $"radius from extent at c = {c}");
                    }

                    float worst = 0f;
                    for (int i = 0; i < slice.VertexCount; i++)
                        worst = math.max(worst, math.abs(math.length(slice.Positions[i]) - radius));
                    Assert.That(worst, Is.LessThan(0.01f * radius), $"every vertex radius at c = {c}");
                }
            }
        }

        [Test]
        public void Slicer_MovingSphere()
        {
            // One unit of w per second (c = 1). The sphere moves along the table axis z at speed u for the
            // first nine tenths of the period; the last slab only closes the period and is not used here.
            const float D = 2f, Period = 4f, V = 2f;
            float sphereArea = math.PI * D * D;

            foreach (float u in new[] { 0.5f, -0.5f, 0f, 1f })
            {
                var slice = new SliceHarness(Polytopes4D.MovingSphere(D, Polytopes4D.OffsetsConstantSpeed(10, u, Period), Period));

                // Untilted (theta = 0) the cut is a snapshot: the sphere itself.
                foreach (float w in new[] { 0.1f, 1.37f, 2f, 3.9f })
                {
                    slice.Slice(double4x4.identity, w);
                    Assert.That(slice.Area(), Is.EqualTo(sphereArea).Within(0.02f * sphereArea));
                    Assert.That(slice.Extent(new float3(0f, 0f, 1f)), Is.EqualTo(D).Within(0.02f * D));
                    Assert.That(slice.OpenEdgeCount(out _), Is.EqualTo(0));
                    Assert.That(slice.InwardTriangleCount(slice.Centroid()), Is.EqualTo(0));
                }

                // A table moving at speed v is the cut tilted by theta, tan(theta) = c / v, here placed to
                // meet the sphere's centre at t = 2.
                float theta = math.atan(1f / V);
                float4 normal = new float4(0f, 0f, -math.sin(theta), math.cos(theta));
                float4 meeting = new float4(0f, 0f, u * 2f, 2f);
                slice.Slice(float4x4.identity, float4.zero, normal, math.dot(normal, meeting));

                float expected = D * V / (V - u);
                Assert.That(slice.Extent(new float3(0f, 0f, 1f)), Is.EqualTo(expected).Within(0.02f * expected),
                            $"apparent length at u = {u}");

                // Across the table the sphere keeps its true width, and the recorded phase spans the scan time.
                Assert.That(slice.Extent(new float3(1f, 0f, 0f)), Is.EqualTo(D).Within(0.02f * D));
                float first = float.MaxValue, last = float.MinValue;
                for (int i = 0; i < slice.VertexCount; i++)
                {
                    first = math.min(first, slice.Data[i]);
                    last = math.max(last, slice.Data[i]);
                }
                Assert.That((last - first) * Period, Is.EqualTo(D / (V - u)).Within(0.02f * D / (V - u)));
                Assert.That(slice.OpenEdgeCount(out _), Is.EqualTo(0));
                Assert.That(slice.InwardTriangleCount(slice.Centroid()), Is.EqualTo(0));
            }
        }

        private static readonly Func<Mesh4D>[] ConvexBodies =
        {
            Polytopes4D.Cell5, Polytopes4D.Tesseract, Polytopes4D.Cell16, Polytopes4D.Cell24, () => Polytopes4D.Ball(4),
        };

        [Test]
        public void Slicer_ConvexBodies_SlicesAreClosedAndWoundOutward()
        {
            double4x4[] turns =
            {
                double4x4.identity,
                Rotation4D.Plane(0, 3, 0.6),
                Rotation4D.CornerFirst(),
                Rotation4D.FromPlaneAngles(0.3, -0.8, 0.7, 1.1, 0.45, -1.3),
            };

            foreach (Func<Mesh4D> build in ConvexBodies)
            {
                Mesh4D mesh = build();
                var slice = new SliceHarness(mesh);
                int nonEmpty = 0;

                foreach (double4x4 turn in turns)
                {
                    for (int step = -10; step <= 10; step++)
                    {
                        slice.Slice(turn, step * 0.0931f);
                        if (slice.TriangleCount == 0) continue;
                        nonEmpty++;

                        string where = $"{mesh.TetCount} tetrahedra, step {step}";
                        Assert.That(slice.VertexCount, Is.EqualTo(3 * slice.TriangleCount));
                        Assert.That(slice.TriangleCount, Is.LessThanOrEqualTo(mesh.MaxSliceTriangles));
                        Assert.That(slice.TetsCut, Is.InRange((slice.TriangleCount + 1) / 2, slice.TriangleCount));
                        Assert.That(slice.OpenEdgeCount(out int solid), Is.EqualTo(0), where);
                        Assert.That(solid, Is.GreaterThanOrEqualTo(4), where);
                        Assert.That(slice.InwardTriangleCount(slice.Centroid()), Is.EqualTo(0), where);
                        for (int i = 0; i < slice.VertexCount; i++)
                            Assert.That(math.length(slice.Normals[i]), Is.EqualTo(1f).Within(1e-4f));
                    }
                }
                Assert.That(nonEmpty, Is.GreaterThan(20));
            }
        }

        [Test]
        public void Slicer_Tesseract_SlicesThroughVerticesStayClosed()
        {
            // At c = 1, 2 and 3 the corner-first cut passes exactly through vertices of the tesseract.
            var slice = new SliceHarness(Polytopes4D.Tesseract());
            foreach (float c in new[] { 0.5f, 1f, 2f, 3f })
            {
                CornerFirstCut(slice, c);
                Assert.That(slice.OpenEdgeCount(out int solid), Is.EqualTo(0), $"c = {c}");
                Assert.That(solid, Is.GreaterThanOrEqualTo(4));
                Assert.That(slice.InwardTriangleCount(slice.Centroid()), Is.EqualTo(0), $"c = {c}");
            }
        }

        [Test]
        public void Slicer_Transform_EqualsSlicingTheMovedMesh()
        {
            float4x4 turn = (float4x4)Rotation4D.FromPlaneAngles(0.2, 0.5, 1.0, -0.4, -0.3, 0.8);
            float4 shift = new float4(0.3f, -0.2f, 0.1f, 0.15f);
            float4 normal = math.normalize(new float4(0.1f, 0.4f, -0.2f, 0.9f));

            Mesh4D original = Polytopes4D.Cell24();
            var moved = new float4[original.VertexCount];
            for (int i = 0; i < moved.Length; i++) moved[i] = math.mul(turn, original.Vertices[i]) + shift;
            var direct = new SliceHarness(new Mesh4D(moved, original.Tetrahedra, original.TetCell, original.Edges, original.CellCount));
            direct.Slice(float4x4.identity, float4.zero, normal, 0.2f);

            var viaTransform = new SliceHarness(original);
            viaTransform.Slice(turn, shift, normal, 0.2f);

            Assert.That(viaTransform.TriangleCount, Is.GreaterThan(0));
            Assert.That(viaTransform.TriangleCount, Is.EqualTo(direct.TriangleCount));
            Assert.That(viaTransform.Area(), Is.EqualTo(direct.Area()).Within(1e-4f));
            for (int i = 0; i < viaTransform.VertexCount; i++)
            {
                Assert.That(math.distance(viaTransform.Positions[i], direct.Positions[i]), Is.LessThan(1e-4f));
                Assert.That(math.distance(viaTransform.Normals[i], direct.Normals[i]), Is.LessThan(1e-3f));
            }
            Assert.That(viaTransform.InwardTriangleCount(viaTransform.Centroid()), Is.EqualTo(0));
        }

        [Test]
        public void Slicer_ReportsCellsAndInterpolatedData()
        {
            var slice = new SliceHarness(Polytopes4D.MovingSphere(1f, Polytopes4D.OffsetsBreathing(10, 1.5f), 1.5f));

            // A phase slice at 34% of the period touches only slab 3, and every vertex carries that phase.
            slice.Slice(double4x4.identity, 0.34f * 1.5f);
            Assert.That(slice.TriangleCount, Is.GreaterThan(0));
            Assert.That(slice.TetsCut, Is.LessThanOrEqualTo(1800), "one slab in ten is touched");
            for (int i = 0; i < slice.VertexCount; i++)
            {
                Assert.That(slice.Cells[i], Is.EqualTo(3));
                Assert.That(slice.Data[i], Is.EqualTo(0.34f).Within(1e-5f));
            }
        }

        [Test]
        public unsafe void Slicer_NeverWritesPastTheCapacityAndAcceptsMissingCells()
        {
            Mesh4D mesh = Polytopes4D.Tesseract();
            var positions = new float3[mesh.MaxSliceVertices + 1];
            var normals = new float3[mesh.MaxSliceVertices + 1];
            var cells = new int[mesh.MaxSliceVertices + 1];
            float4x4 turn = (float4x4)Rotation4D.CornerFirst();
            float4 none = float4.zero;
            float4 normal = SliceHarness.PlusW;

            fixed (float4* v = mesh.Vertices)
            fixed (int* t = mesh.Tetrahedra)
            fixed (float3* p = positions)
            fixed (float3* n = normals)
            fixed (int* oc = cells)
            {
                Slicer4D.Slice(v, t, mesh.TetCount, null, null, in turn, in none, in normal, 0f,
                               p, n, oc, null, mesh.MaxSliceVertices, out int fullVertices, out int fullTriangles, out int fullTets);
                Assert.That(fullVertices, Is.GreaterThan(12));
                Assert.That(fullTriangles * 3, Is.EqualTo(fullVertices));
                for (int i = 0; i < fullVertices; i++) Assert.That(cells[i], Is.EqualTo(0), "no cell ids given");

                // A tetrahedron is written whole or not at all.
                for (int capacity = 0; capacity <= 12; capacity++)
                {
                    for (int i = 0; i < positions.Length; i++) positions[i] = new float3(123f);
                    Slicer4D.Slice(v, t, mesh.TetCount, null, null, in turn, in none, in normal, 0f,
                                   p, n, oc, null, capacity, out int vertices, out int triangles, out int tets);
                    Assert.That(vertices, Is.LessThanOrEqualTo(capacity));
                    Assert.That(vertices, Is.EqualTo(3 * triangles));
                    Assert.That(tets, Is.LessThan(fullTets));
                    for (int i = vertices; i < positions.Length; i++) Assert.That(positions[i].x, Is.EqualTo(123f));
                }
            }
        }

        [Test]
        public unsafe void Slicer_Kernel_DoesNotAllocate()
        {
            Mesh4D[] meshes =
            {
                Polytopes4D.Tesseract(),
                Polytopes4D.MovingSphere(1f, Polytopes4D.OffsetsBreathing(10, 1.5f), 1.5f),
            };

            foreach (Mesh4D mesh in meshes)
            {
                var positions = new float3[mesh.MaxSliceVertices];
                var normals = new float3[mesh.MaxSliceVertices];
                var cells = new int[mesh.MaxSliceVertices];
                var data = new float[mesh.MaxSliceVertices];
                var shadow = new float3[mesh.VertexCount];
                float4x4 turn = (float4x4)Rotation4D.Plane(2, 3, 0.3);
                float4 shift = new float4(0f, 0f, 0f, 0.1f);
                float4 normal = SliceHarness.PlusW;
                int total = 0;
                float area = 0f;

                fixed (float4* v = mesh.Vertices)
                fixed (int* t = mesh.Tetrahedra)
                fixed (int* c = mesh.TetCell)
                fixed (float* vd = mesh.VertexData)
                fixed (float3* p = positions)
                fixed (float3* n = normals)
                fixed (int* oc = cells)
                fixed (float* od = data)
                fixed (float3* sh = shadow)
                {
                    // One pass first so that the measured pass runs compiled code.
                    for (int pass = 0; pass < 2; pass++)
                    {
                        long before = GC.GetAllocatedBytesForCurrentThread();
                        for (int frame = 0; frame < 50; frame++)
                        {
                            Slicer4D.Slice(v, t, mesh.TetCount, c, vd, in turn, in shift, in normal, 0.2f + 0.01f * frame,
                                           p, n, oc, od, positions.Length, out int vertices, out int triangles, out _);
                            total += triangles;
                            area += Slicer4D.Area(p, triangles);
                            area += Slicer4D.Extent(p, vertices, new float3(0f, 0f, 1f));
                            Projector4D.ProjectVertices(v, mesh.VertexCount, in turn, in shift, true, 3f, sh, null);
                        }
                        long after = GC.GetAllocatedBytesForCurrentThread();
                        if (pass == 1) Assert.That(after - before, Is.EqualTo(0L), "managed bytes allocated by 50 slices");
                    }
                }

                Assert.That(total, Is.GreaterThan(0));
                Assert.That(area, Is.GreaterThan(0f));
            }
        }
    }
}
