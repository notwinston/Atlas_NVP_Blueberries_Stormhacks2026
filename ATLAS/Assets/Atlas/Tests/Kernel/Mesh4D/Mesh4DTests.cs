using System;
using System.Linq;
using Atlas.Kernel.FourD;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Kernel.Tests
{
    /// <summary>Generators, rotations and the shadow of the 4D bodies (Master Plan section 5.5, task P1-A12).</summary>
    public class Mesh4DTests
    {
        [Test]
        public void Polytopes_Counts()
        {
            Mesh4D tesseract = Polytopes4D.Tesseract();
            Assert.That(tesseract.VertexCount, Is.EqualTo(16));
            Assert.That(tesseract.TetCount, Is.EqualTo(48));
            Assert.That(tesseract.EdgeCount, Is.EqualTo(32));
            Assert.That(tesseract.CellCount, Is.EqualTo(8));
            for (int cell = 0; cell < 8; cell++)
                Assert.That(tesseract.TetCell.Count(id => id == cell), Is.EqualTo(6), "six tetrahedra per cubic cell");
            foreach (float4 v in tesseract.Vertices)
                Assert.That(math.all(math.abs(v) == 0.5f), Is.True);

            // Six Kuhn tetrahedra fill a unit cube: volume 1/6 each, so the normal has length 1.
            for (int t = 0; t < tesseract.TetCount; t++)
                Assert.That(math.length(tesseract.TetNormal(t)) / 6f, Is.EqualTo(1f / 6f).Within(1e-6f));

            Mesh4D cell5 = Polytopes4D.Cell5();
            Assert.That(cell5.VertexCount, Is.EqualTo(5));
            Assert.That(cell5.TetCount, Is.EqualTo(5));
            Assert.That(cell5.EdgeCount, Is.EqualTo(10));
            Assert.That(cell5.CellCount, Is.EqualTo(5));

            Mesh4D cell16 = Polytopes4D.Cell16();
            Assert.That(cell16.VertexCount, Is.EqualTo(8));
            Assert.That(cell16.TetCount, Is.EqualTo(16));
            Assert.That(cell16.EdgeCount, Is.EqualTo(24));
            Assert.That(cell16.CellCount, Is.EqualTo(16));

            Mesh4D cell24 = Polytopes4D.Cell24();
            Assert.That(cell24.VertexCount, Is.EqualTo(24));
            Assert.That(cell24.TetCount, Is.EqualTo(96));
            Assert.That(cell24.EdgeCount, Is.EqualTo(96));
            Assert.That(cell24.CellCount, Is.EqualTo(24));
            for (int cell = 0; cell < 24; cell++)
                Assert.That(cell24.TetCell.Count(id => id == cell), Is.EqualTo(4), "four tetrahedra per octahedron");

            foreach (Mesh4D polytope in new[] { tesseract, cell5, cell16, cell24 })
            {
                foreach (float4 v in polytope.Vertices)
                    Assert.That(math.length(v), Is.EqualTo(1f).Within(1e-6f), "circumradius 1");
            }

            Mesh4D ball = Polytopes4D.Ball(4);
            Assert.That(ball.TetCount, Is.EqualTo(48 * 64));
            Assert.That(ball.CellCount, Is.EqualTo(8));
            Assert.That(ball.EdgeCount, Is.EqualTo(0));
            foreach (float4 v in ball.Vertices) Assert.That(math.length(v), Is.EqualTo(1f).Within(1e-5f));

            Mesh4D tube = Polytopes4D.MovingSphere(0.01f, Polytopes4D.OffsetsBreathing(10, 0.015f), 1.5f);
            Assert.That(tube.TetCount, Is.EqualTo(18000));
            Assert.That(tube.VertexCount, Is.EqualTo(11 * 302));
            Assert.That(tube.CellCount, Is.EqualTo(10));
            Assert.That(tube.EdgeCount, Is.EqualTo(0));
            Assert.That(tube.WPeriod, Is.EqualTo(1.5f));
            Assert.That(tube.VertexData, Is.Not.Null);
            Assert.That(tube.MaxSliceTriangles, Is.EqualTo(36000));
        }

        [Test]
        public void Mesh4D_Polytopes_HaveClosedBoundaries()
        {
            Mesh4D[] closed =
            {
                Polytopes4D.Cell5(), Polytopes4D.Tesseract(), Polytopes4D.Cell16(), Polytopes4D.Cell24(), Polytopes4D.Ball(3),
            };
            foreach (Mesh4D mesh in closed)
            {
                var faces = SliceHarness.FaceUses(mesh);
                Assert.That(faces.Values.All(uses => uses == 2), Is.True,
                            $"every face of the {mesh.TetCount}-tetrahedron mesh is shared by exactly two");
            }
        }

        [Test]
        public void Mesh4D_Polytopes_AreOrientedOutward()
        {
            Mesh4D[] centred =
            {
                Polytopes4D.Cell5(), Polytopes4D.Tesseract(), Polytopes4D.Cell16(), Polytopes4D.Cell24(), Polytopes4D.Ball(3),
            };
            foreach (Mesh4D mesh in centred)
            {
                for (int t = 0; t < mesh.TetCount; t++)
                    Assert.That(math.dot(mesh.TetNormal(t), mesh.TetCentroid(t)), Is.GreaterThan(0f));
            }
        }

        [Test]
        public void Mesh4D_Tube_ClosesWhenItsEndsAreIdentified()
        {
            Mesh4D tube = Polytopes4D.MovingSphere(1f, Polytopes4D.OffsetsBreathing(10, 1.5f), 1.5f);
            int ring = tube.VertexCount / 11;

            // As a mesh in R^4 the tube is open at w = 0 and w = period: 600 faces at each end.
            var open = SliceHarness.FaceUses(tube);
            Assert.That(open.Values.Count(uses => uses == 1), Is.EqualTo(1200));
            Assert.That(open.Values.All(uses => uses == 1 || uses == 2), Is.True);

            // In R^3 x S^1 the last ring is the first ring, and nothing is left open.
            var wrapped = SliceHarness.FaceUses(tube, index => index % (ring * 10));
            Assert.That(wrapped.Values.All(uses => uses == 2), Is.True);

            for (int i = 0; i < ring; i++)
            {
                Assert.That(math.distance(tube.Vertices[i].xyz, tube.Vertices[10 * ring + i].xyz), Is.LessThan(1e-6f));
                Assert.That(tube.Vertices[10 * ring + i].w, Is.EqualTo(1.5f).Within(1e-6f));
            }
        }

        [Test]
        public void Mesh4D_Offsets_FollowThePlanCurves()
        {
            float3[] constant = Polytopes4D.OffsetsConstantSpeed(10, 0.5f, 4f);
            Assert.That(constant.Length, Is.EqualTo(10));
            Assert.That(constant[3].z, Is.EqualTo(0.5f * 3f * 0.4f).Within(1e-6f));
            Assert.That(constant[3].x, Is.EqualTo(0f));

            // z = -b cos^4(pi t / tau): fully down at t = 0, back at zero mid-cycle, mean -0.375 b (Appendix A.8).
            float3[] breathing = Polytopes4D.OffsetsBreathing(1000, 2f);
            Assert.That(breathing[0].z, Is.EqualTo(-2f).Within(1e-6f));
            Assert.That(breathing[500].z, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(breathing.Average(o => o.z), Is.EqualTo(-0.375f * 2f).Within(1e-4f));
        }

        [Test]
        public void Mesh4D_CellBoundaryTriangles_AreThePolytopeFaces()
        {
            // 24 squares of a tesseract, two triangles each; 10 triangles of a 5-cell; 32 of a 16-cell;
            // 96 of a 24-cell.
            Assert.That(Polytopes4D.Tesseract().BuildCellBoundaryTriangles().Length, Is.EqualTo(48 * 3));
            Assert.That(Polytopes4D.Cell5().BuildCellBoundaryTriangles().Length, Is.EqualTo(10 * 3));
            Assert.That(Polytopes4D.Cell16().BuildCellBoundaryTriangles().Length, Is.EqualTo(32 * 3));
            Assert.That(Polytopes4D.Cell24().BuildCellBoundaryTriangles().Length, Is.EqualTo(96 * 3));
        }

        [Test]
        public void Mesh4D_Constructor_RejectsBadInput()
        {
            var vertices = new float4[4];
            Assert.Throws<ArgumentException>(() => new Mesh4D(vertices, new[] { 0, 1, 2, 4 }, new[] { 0 }, null, 1));
            Assert.Throws<ArgumentException>(() => new Mesh4D(vertices, new[] { 0, 1, 2 }, new[] { 0 }, null, 1));
            Assert.Throws<ArgumentException>(() => new Mesh4D(vertices, new[] { 0, 1, 2, 3 }, new[] { 1 }, null, 1));
            Assert.Throws<ArgumentException>(() => new Mesh4D(vertices, new[] { 0, 1, 2, 3 }, new[] { 0 }, new[] { 0, 9 }, 1));
            Assert.DoesNotThrow(() => new Mesh4D(vertices, new[] { 0, 1, 2, 3 }, new[] { 0 }, null, 1));
        }

        [Test]
        public void Rotation4D_Basics()
        {
            int[,] planes = { { 0, 1 }, { 0, 2 }, { 0, 3 }, { 1, 2 }, { 1, 3 }, { 2, 3 } };
            for (int plane = 0; plane < 6; plane++)
            {
                double4x4 r = Rotation4D.Plane(planes[plane, 0], planes[plane, 1], 0.7);
                AssertIdentity(math.mul(r, math.transpose(r)), 1e-9);
                Assert.That(math.determinant(r), Is.EqualTo(1.0).Within(1e-9));
            }

            // Four quarter turns in XW give the identity; one carries x into w and leaves y and z alone.
            double4x4 quarter = Rotation4D.Plane(0, 3, math.PI_DBL / 2.0);
            AssertIdentity(math.mul(math.mul(quarter, quarter), math.mul(quarter, quarter)), 1e-9);
            Assert.That(math.distance(math.mul(quarter, new double4(1, 2, 3, 0)), new double4(0, 2, 3, 1)), Is.LessThan(1e-12));

            // R2 section D.5: the ZW turn takes e_w to (0, 0, -sin, cos) and e_z to (0, 0, cos, sin).
            double4x4 tilt = Rotation4D.Plane(2, 3, 0.4);
            Assert.That(math.distance(tilt.c3, new double4(0, 0, -math.sin(0.4), math.cos(0.4))), Is.LessThan(1e-12));
            Assert.That(math.distance(tilt.c2, new double4(0, 0, math.cos(0.4), math.sin(0.4))), Is.LessThan(1e-12));

            double4x4 corner = Rotation4D.CornerFirst();
            Assert.That(math.distance(math.mul(corner, new double4(0.5, 0.5, 0.5, 0.5)), new double4(0, 0, 0, 1)), Is.LessThan(1e-12));
            AssertIdentity(math.mul(corner, math.transpose(corner)), 1e-12);
            Assert.That(math.determinant(corner), Is.EqualTo(1.0).Within(1e-12));

            double4x4 general = Rotation4D.FromPlaneAngles(0.1, 0.2, 0.3, 0.4, 0.5, 0.6);
            AssertIdentity(math.mul(general, math.transpose(general)), 1e-9);
            Assert.That(math.determinant(general), Is.EqualTo(1.0).Within(1e-9));
            AssertIdentity(math.mul(Rotation4D.FromPlaneAngles(0, 0, 0.3, 0, 0, 0), Rotation4D.Plane(0, 3, -0.3)), 1e-12);

            // A matrix that has drifted comes back to a rotation.
            double4x4 drifted = general;
            drifted.c0 *= 1.01;
            drifted.c2 += 0.02 * drifted.c1;
            double4x4 repaired = Rotation4D.Orthonormalised(drifted);
            AssertIdentity(math.mul(repaired, math.transpose(repaired)), 1e-12);
            AssertIdentity(math.mul(Rotation4D.Orthonormalised(general), math.transpose(general)), 1e-9);
        }

        private static void AssertIdentity(double4x4 m, double tolerance)
        {
            for (int c = 0; c < 4; c++)
            for (int r = 0; r < 4; r++)
                Assert.That(m[c][r], Is.EqualTo(c == r ? 1.0 : 0.0).Within(tolerance));
        }

        [Test]
        public unsafe void Mesh4D_Shadow_ProjectsOrthographicallyAndInPerspective()
        {
            Assert.That(Projector4D.Orthographic(new float4(1f, 2f, 3f, 4f)), Is.EqualTo(new float3(1f, 2f, 3f)));
            Assert.That(Projector4D.Perspective(new float4(1f, 2f, 3f, 1f)), Is.EqualTo(new float3(2f, 4f, 6f)));
            Assert.That(Projector4D.Perspective(new float4(1f, 2f, 3f, 0f), 5f), Is.EqualTo(new float3(1f, 2f, 3f)));
            Assert.That(math.all(math.isfinite(Projector4D.Perspective(new float4(1f, 2f, 3f, 2f)))), Is.True);

            Mesh4D tesseract = Polytopes4D.Tesseract();
            var shadow = new float3[tesseract.VertexCount];
            var depth = new float[tesseract.VertexCount];
            float4x4 identity = float4x4.identity;
            float4 none = float4.zero;

            fixed (float4* vertices = tesseract.Vertices)
            fixed (float3* positions = shadow)
            fixed (float* w = depth)
            {
                Projector4D.ProjectVertices(vertices, tesseract.VertexCount, in identity, in none, false, 2f, positions, w);
                for (int i = 0; i < shadow.Length; i++)
                {
                    Assert.That(math.distance(shadow[i], tesseract.Vertices[i].xyz), Is.LessThan(1e-6f));
                    Assert.That(depth[i], Is.EqualTo(tesseract.Vertices[i].w));
                }

                // Cube within a cube: the cell nearer the eye (w = +0.5) is drawn larger than the far one.
                Projector4D.ProjectVertices(vertices, tesseract.VertexCount, in identity, in none, true, 2f, positions, null);
                for (int i = 0; i < shadow.Length; i++)
                {
                    float expected = tesseract.Vertices[i].w > 0f ? 0.5f * 2f / 1.5f : 0.5f * 2f / 2.5f;
                    Assert.That(math.abs(shadow[i].x), Is.EqualTo(expected).Within(1e-5f));
                    Assert.That(math.abs(shadow[i].y), Is.EqualTo(expected).Within(1e-5f));
                    Assert.That(math.abs(shadow[i].z), Is.EqualTo(expected).Within(1e-5f));
                }
            }
        }
    }
}
