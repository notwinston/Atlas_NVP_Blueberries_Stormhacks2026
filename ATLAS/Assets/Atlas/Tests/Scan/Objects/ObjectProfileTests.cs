using System;
using Atlas.Scan.Grid;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Objects
{
    /// <summary>The object profile (mesh design section 5.2, brief S3): the bytes, the coverage and the quarter turns.</summary>
    public class ObjectProfileTests
    {
        const int N = ObjectProfile.Side;

        static byte[] Profile(ScannedShapes scan, float3 basePoint, float3 size, float yaw, bool withClasses = true)
        {
            return ObjectProfile.Compute(scan.Vertices, scan.Indices, withClasses ? scan.Classes : null, basePoint, size, yaw);
        }

        static byte[] Filled(byte value)
        {
            var profile = new byte[ObjectProfile.Length];
            for (int c = 0; c < profile.Length; c++) profile[c] = value;
            return profile;
        }

        [Test]
        public void VectorA_TheChair_GivesTheExpectedBytes()
        {
            byte[] profile = Profile(ScannedShapes.VectorA(), float3.zero, ScannedShapes.VectorASize, 0f);
            Assert.That(profile.Length, Is.EqualTo(256));
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                    Assert.That(profile[j * 16 + i], Is.EqualTo(j <= 1 ? 255 : 128), "cell (" + i + ", " + j + ")");
            Assert.That(ObjectProfile.Coverage(profile), Is.EqualTo(1f));
        }

        [Test]
        public void Compute_ARayThroughAnEdgeOrAVertex_HitsTheTriangle()
        {
            // The first ray of column 0 and of row 0 stands at -0.25 + 0.25 * 0.5 / 16.
            const float first = -0.2421875f;

            // A level triangle that lies outside the first column of rays and touches it with one edge: only
            // the rays on that edge can hit it.
            var vertices = new[] { new float3(first, 0.5f, -0.25f), new float3(first, 0.5f, 0.25f), new float3(first - 1f, 0.5f, 0f) };
            byte[] edge = ObjectProfile.Compute(vertices, new[] { 0, 1, 2 }, null, float3.zero, ScannedShapes.VectorASize, 0f);
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                    Assert.That(edge[j * 16 + i], Is.EqualTo(i == 0 ? 128 : 0), "edge, cell (" + i + ", " + j + ")");

            // One that touches the first ray of cell (0, 0) with a corner and lies wholly outside the others.
            vertices = new[] { new float3(first, 0.5f, first), new float3(first - 1f, 0.5f, first - 0.5f), new float3(first - 0.5f, 0.5f, first - 1f) };
            byte[] corner = ObjectProfile.Compute(vertices, new[] { 0, 1, 2 }, null, float3.zero, ScannedShapes.VectorASize, 0f);
            for (int c = 0; c < 256; c++) Assert.That(corner[c], Is.EqualTo(c == 0 ? 128 : 0), "corner, cell " + c);

            // The top of the seat alone, as its two triangles: the rays on their shared diagonal are hit once, not lost between them.
            ScannedShapes seat = new ScannedShapes().Level(-0.25f, -0.25f, 0.25f, 0.25f, 0.5f);
            Assert.That(Profile(seat, float3.zero, ScannedShapes.VectorASize, 0f), Is.EqualTo(Filled(128)));
        }

        [Test]
        public void VectorB_NoTriangles_GivesZerosAndNoCoverage()
        {
            byte[] profile = ObjectProfile.Compute(new float3[0], new int[0], new SurfaceClass[0], float3.zero, ScannedShapes.VectorASize, 0f);
            Assert.That(profile, Is.EqualTo(new byte[256]));
            Assert.That(ObjectProfile.Coverage(profile), Is.EqualTo(0f));

            Assert.That(ObjectProfile.Compute(null, null, null, float3.zero, ScannedShapes.VectorASize, 0f), Is.EqualTo(new byte[256]));
            Assert.That(ObjectProfile.Coverage(null), Is.EqualTo(0f));
        }

        [Test]
        public void VectorC_QuarterTurnsOfTheChair()
        {
            byte[] a = ScannedShapes.VectorAProfile();

            byte[] half = ObjectProfile.Rotate(a, 2);
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                    Assert.That(half[j * 16 + i], Is.EqualTo(j >= 14 ? 255 : 128), "two turns, cell (" + i + ", " + j + ")");

            byte[] quarter = ObjectProfile.Rotate(a, 1);
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                    Assert.That(quarter[j * 16 + i], Is.EqualTo(i >= 14 ? 255 : 128), "one turn, cell (" + i + ", " + j + ")");

            byte[] three = ObjectProfile.Rotate(a, 3);
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                    Assert.That(three[j * 16 + i], Is.EqualTo(i <= 1 ? 255 : 128), "three turns, cell (" + i + ", " + j + ")");
        }

        [Test]
        public void Rotate_FollowsTheCellRule_AndComesBackAfterFourTurns()
        {
            // Every cell different, so a wrong mapping cannot hide.
            var profile = new byte[256];
            for (int c = 0; c < 256; c++) profile[c] = (byte)c;

            byte[] one = ObjectProfile.Rotate(profile, 1), two = ObjectProfile.Rotate(profile, 2), three = ObjectProfile.Rotate(profile, 3);
            for (int j = 0; j < N; j++)
            {
                for (int i = 0; i < N; i++)
                {
                    Assert.That(one[ObjectProfile.Index(i, j)], Is.EqualTo(profile[ObjectProfile.Index(j, 15 - i)]));
                    Assert.That(two[ObjectProfile.Index(i, j)], Is.EqualTo(profile[ObjectProfile.Index(15 - i, 15 - j)]));
                    Assert.That(three[ObjectProfile.Index(i, j)], Is.EqualTo(profile[ObjectProfile.Index(15 - j, i)]));
                }
            }
            Assert.That(ObjectProfile.Rotate(one, 1), Is.EqualTo(two));
            Assert.That(ObjectProfile.Rotate(two, 1), Is.EqualTo(three));
            Assert.That(ObjectProfile.Rotate(three, 1), Is.EqualTo(profile));
            Assert.That(ObjectProfile.Rotate(profile, -1), Is.EqualTo(three));
            Assert.That(ObjectProfile.Rotate(profile, 6), Is.EqualTo(two));

            // No turn is a copy, never the same array.
            byte[] same = ObjectProfile.Rotate(profile, 0);
            Assert.That(same, Is.EqualTo(profile));
            Assert.That(same, Is.Not.SameAs(profile));
            Assert.That(ObjectProfile.Rotate(null, 1), Is.Null);
            Assert.Throws<ArgumentException>(() => ObjectProfile.Rotate(new byte[255], 1));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void Rotate_IsTheProfileOfTheSameScanInATurnedBox(int turns)
        {
            // A chair that is wider than deep, so an odd turn swaps the sides of the box.
            var size = new float3(0.75f, 1f, 0.5f);
            var basePoint = new float3(1.5f, 0f, -0.75f);
            const float yaw = 0.4f;
            ScannedShapes chair = ScannedShapes.Chair(size.x, size.y, size.z, 0.4375f, 0.0625f).Place(basePoint, yaw);
            byte[] straight = Profile(chair, basePoint, size, yaw);
            Assert.That(ObjectProfile.Coverage(straight), Is.EqualTo(1f));

            float3 turnedSize = (turns & 1) != 0 ? new float3(size.z, size.y, size.x) : size;
            byte[] turned = Profile(chair, basePoint, turnedSize, yaw + turns * 0.5f * math.PI);
            Assert.That(turned, Is.EqualTo(ObjectProfile.Rotate(straight, turns)));
        }

        [TestCase(0.7f, 3.2f, 0.75f, -1.7f)]
        [TestCase(-2.5f, -0.4f, -1.125f, 6.3f)]
        [TestCase(1.5707964f, 10f, 0f, 10f)]
        public void Compute_ABoxTurnedAndMovedOffTheOrigin_GivesTheSameBytes(float yaw, float x, float y, float z)
        {
            var basePoint = new float3(x, y, z);
            ScannedShapes chair = ScannedShapes.VectorA().Place(basePoint, yaw);
            Assert.That(Profile(chair, basePoint, ScannedShapes.VectorASize, yaw), Is.EqualTo(ScannedShapes.VectorAProfile()));
        }

        [Test]
        public void Compute_AFloorClassTriangle_IsIgnored()
        {
            var size = new float3(1f, 1f, 1f);
            ScannedShapes floor = new ScannedShapes().Level(-0.5f, -0.5f, 0.5f, 0.5f, 0.25f, SurfaceClass.Floor);
            Assert.That(Profile(floor, float3.zero, size, 0f), Is.EqualTo(new byte[256]));

            // The same surface under any other class counts, and so it does when the mesh has no classes.
            ScannedShapes seat = new ScannedShapes().Level(-0.5f, -0.5f, 0.5f, 0.5f, 0.25f, SurfaceClass.Seat);
            Assert.That(Profile(seat, float3.zero, size, 0f), Is.EqualTo(Filled(64)));
            Assert.That(Profile(floor, float3.zero, size, 0f, false), Is.EqualTo(Filled(64)));

            // Floor under furniture does not hide the furniture.
            floor.Level(-0.5f, -0.5f, 0f, 0.5f, 0.5f, SurfaceClass.Table);
            byte[] both = Profile(floor, float3.zero, size, 0f);
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                    Assert.That(both[j * 16 + i], Is.EqualTo(i < 8 ? 128 : 0), "cell (" + i + ", " + j + ")");
            Assert.That(ObjectProfile.Coverage(both), Is.EqualTo(0.5f));
        }

        [Test]
        public void Compute_AHitBelowTwoCentimetres_IsIgnored()
        {
            var size = new float3(1f, 1f, 1f);
            ScannedShapes low = new ScannedShapes().Level(-0.5f, -0.5f, 0.5f, 0.5f, 0.01f);
            Assert.That(Profile(low, float3.zero, size, 0f), Is.EqualTo(new byte[256]));

            // Three centimetres up counts: floor(255 * 0.03 + 0.5) = 8.
            ScannedShapes higher = new ScannedShapes().Level(-0.5f, -0.5f, 0.5f, 0.5f, 0.03f);
            Assert.That(Profile(higher, float3.zero, size, 0f), Is.EqualTo(Filled(8)));

            // The height is measured from the base of the box, not from the floor of the room.
            Assert.That(Profile(higher, new float3(0f, 0.02f, 0f), size, 0f), Is.EqualTo(new byte[256]));
        }

        [Test]
        public void Compute_AHitAboveTheRayStart_IsIgnored_AndOneJustAboveTheBoxIsFull()
        {
            var size = new float3(1f, 1f, 1f);
            ScannedShapes shelf = new ScannedShapes().Level(-0.5f, -0.5f, 0.5f, 0.5f, 1.1f);
            Assert.That(Profile(shelf, float3.zero, size, 0f), Is.EqualTo(new byte[256]));

            ScannedShapes top = new ScannedShapes().Level(-0.5f, -0.5f, 0.5f, 0.5f, 1.04f);
            Assert.That(Profile(top, float3.zero, size, 0f), Is.EqualTo(Filled(255)));
        }

        [Test]
        public void Compute_TakesTheHighestHitOfACell()
        {
            var size = new float3(1f, 2f, 1f);
            ScannedShapes shapes = new ScannedShapes()
                .Level(-0.5f, -0.5f, 0.5f, 0.5f, 0.5f)
                .Level(-0.5f, -0.5f, 0.5f, 0.5f, 1.5f)
                .Level(-0.5f, -0.5f, 0.5f, 0.5f, 1f);
            Assert.That(Profile(shapes, float3.zero, size, 0f), Is.EqualTo(Filled(191)));      // floor(255 * 0.75 + 0.5)

            // One ray of the four is enough: a strip a sixty-fourth of the box wide over the first quarter point of column 5.
            float x = -0.5f + (5 + 0.25f) / 16f;
            ScannedShapes strip = new ScannedShapes().Level(-0.5f, -0.5f, 0.5f, 0.5f, 0.5f).Level(x - 1f / 128f, -0.5f, x + 1f / 128f, 0.5f, 1.5f);
            byte[] profile = Profile(strip, float3.zero, size, 0f);
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                    Assert.That(profile[j * 16 + i], Is.EqualTo(i == 5 ? 191 : 64), "cell (" + i + ", " + j + ")");
        }

        [Test]
        public void Compute_ASlope_IsReadAtTheHighestRayOfEachRow()
        {
            // A ramp from a quarter of the height at the low-z edge to three quarters at the other.
            var size = new float3(1f, 1f, 1f);
            ScannedShapes ramp = new ScannedShapes().Quad(new float3(-0.5f, 0.25f, -0.5f), new float3(0.5f, 0.25f, -0.5f),
                new float3(0.5f, 0.75f, 0.5f), new float3(-0.5f, 0.75f, 0.5f));
            byte[] profile = Profile(ramp, float3.zero, size, 0f);
            for (int j = 0; j < N; j++)
            {
                double height = 0.25 + 0.5 * (j + 0.75) / 16.0;
                int expected = (int)Math.Floor(255.0 * height + 0.5);
                for (int i = 0; i < N; i++) Assert.That(profile[j * 16 + i], Is.EqualTo(expected), "cell (" + i + ", " + j + ")");
            }
        }

        [Test]
        public void Compute_OnlyTheScanInsideTheBoxCounts()
        {
            // A table top that runs on beyond the box, and a second piece of furniture beside it.
            var size = new float3(0.5f, 1f, 0.5f);
            var basePoint = new float3(2f, 0f, 2f);
            ScannedShapes scan = new ScannedShapes()
                .Level(1.5f, 1.5f, 2f, 3f, 0.5f)
                .AddBox(new float3(2.3f, 0f, 1f), new float3(3f, 1f, 1.74f));
            byte[] profile = Profile(scan, basePoint, size, 0f);
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                    Assert.That(profile[j * 16 + i], Is.EqualTo(i < 8 ? 128 : 0), "cell (" + i + ", " + j + ")");
        }

        [Test]
        public void Compute_ATriangleThatIsNoNumber_IsLeftOut()
        {
            // A broken triangle in the scan costs its own hits, not the profile, also beside a box with no size.
            ScannedShapes scan = ScannedShapes.VectorA();
            scan.Triangle(new float3(0f, 0.7f, 0f), new float3(float.NaN, 0.7f, 0.1f), new float3(0.1f, float.NaN, float.NaN));
            scan.Triangle(new float3(float.NaN, float.NaN, float.NaN), new float3(0.1f, 0.7f, 0f), new float3(0f, 0.7f, 0.1f));
            scan.Triangle(new float3(0f, 0.7f, 0f), new float3(float.PositiveInfinity, 0.7f, 0f), new float3(0f, 0.7f, float.NegativeInfinity));
            PhotoBox[] boxes =
            {
                new PhotoBox { Base = float3.zero, Size = new float3(0.5f, 0f, 0.5f), Yaw = 0f },
                new PhotoBox { Base = float3.zero, Size = ScannedShapes.VectorASize, Yaw = 0f },
            };
            byte[][] profiles = ObjectProfile.Compute(scan.Vertices, scan.Indices, scan.Classes, boxes);
            Assert.That(profiles[0], Is.EqualTo(new byte[256]));
            Assert.That(profiles[1], Is.EqualTo(ScannedShapes.VectorAProfile()));
        }

        [Test]
        public void Compute_ABoxWithNoSize_GivesZeros()
        {
            ScannedShapes chair = ScannedShapes.VectorA();
            Assert.That(Profile(chair, float3.zero, new float3(0.5f, 0f, 0.5f), 0f), Is.EqualTo(new byte[256]));
            Assert.That(Profile(chair, float3.zero, new float3(0f, 1f, 0.5f), 0f), Is.EqualTo(new byte[256]));
            Assert.That(Profile(chair, float3.zero, new float3(0.5f, 1f, float.NaN), 0f), Is.EqualTo(new byte[256]));
        }

        [Test]
        public void Compute_ManyBoxesInOnePass_GivesEachBoxItsOwnProfile()
        {
            // Two chairs, a low table between them and the floor under all three.
            var size = new float3(0.75f, 1f, 0.5f);
            var first = new PhotoBox { Base = new float3(1.5f, 0f, -0.75f), Size = size, Yaw = 0.4f };
            var second = new PhotoBox { Base = new float3(-2f, 0.25f, 3f), Size = size, Yaw = -2.2f };
            var table = new PhotoBox { Base = new float3(0f, 0f, 1f), Size = new float3(1.2f, 0.5f, 0.8f), Yaw = 1f };
            var nothing = new PhotoBox { Base = new float3(5f, 0f, 5f), Size = new float3(0.5f, 0.5f, 0.5f), Yaw = 0f };
            var flat = new PhotoBox { Base = first.Base, Size = new float3(0.75f, 0f, 0.5f), Yaw = 0.4f };

            var scan = new ScannedShapes();
            scan.Level(-6f, -6f, 6f, 6f, 0f, SurfaceClass.Floor);
            AddPlaced(scan, ScannedShapes.Chair(size.x, size.y, size.z, 0.4375f, 0.0625f).Place(first.Base, first.Yaw));
            AddPlaced(scan, ScannedShapes.Chair(size.x, size.y, size.z, 0.4375f, 0.0625f).Place(second.Base, second.Yaw));
            AddPlaced(scan, new ScannedShapes().AddBox(new float3(-0.6f, 0f, -0.4f), new float3(0.6f, 0.5f, 0.4f), SurfaceClass.Table).Place(table.Base, table.Yaw));

            PhotoBox[] boxes = { first, nothing, table, flat, second };
            byte[][] profiles = ObjectProfile.Compute(scan.Vertices, scan.Indices, scan.Classes, boxes);
            Assert.That(profiles.Length, Is.EqualTo(5));
            for (int k = 0; k < boxes.Length; k++)
                Assert.That(profiles[k], Is.EqualTo(Profile(scan, boxes[k].Base, boxes[k].Size, boxes[k].Yaw)), "box " + k);

            // The two chairs read alike wherever they stand; the table is one level top; the others saw nothing.
            Assert.That(profiles[4], Is.EqualTo(profiles[0]));
            Assert.That(profiles[0][ObjectProfile.Index(7, 0)], Is.EqualTo(255));
            Assert.That(profiles[0][ObjectProfile.Index(7, 9)], Is.EqualTo(112));
            Assert.That(profiles[2], Is.EqualTo(Filled(255)));
            Assert.That(profiles[1], Is.EqualTo(new byte[256]));
            Assert.That(profiles[3], Is.EqualTo(new byte[256]));

            Assert.That(ObjectProfile.Compute(scan.Vertices, scan.Indices, scan.Classes, null), Is.Empty);
            Assert.That(ObjectProfile.Compute(null, null, null, boxes)[0], Is.EqualTo(new byte[256]));
        }

        static void AddPlaced(ScannedShapes into, ScannedShapes placed)
        {
            float3[] v = placed.Vertices;
            int[] t = placed.Indices;
            SurfaceClass[] c = placed.Classes;
            for (int k = 0; k < c.Length; k++)
                into.Triangle(v[t[3 * k]], v[t[3 * k + 1]], v[t[3 * k + 2]], c[k]);
        }

        [Test]
        public void Quantise_RoundsToTheNearestByte_AndNeverToZero()
        {
            Assert.That(ObjectProfile.Quantise(0.5, 1.0), Is.EqualTo(128));
            Assert.That(ObjectProfile.Quantise(1.0, 1.0), Is.EqualTo(255));
            Assert.That(ObjectProfile.Quantise(1.05, 1.0), Is.EqualTo(255));
            Assert.That(ObjectProfile.Quantise(0.75, 1.5), Is.EqualTo(128));
            Assert.That(ObjectProfile.Quantise(0.02, 6.0), Is.EqualTo(1));
            // A height that rounds to 0 is still a seen cell: 0 means unseen.
            Assert.That(ObjectProfile.Quantise(0.001, 1.0), Is.EqualTo(1));

            Assert.That(ObjectProfile.HeightFraction(255), Is.EqualTo(1f));
            Assert.That(ObjectProfile.HeightFraction(128), Is.EqualTo(128f / 255f));
            Assert.That(ObjectProfile.HeightFraction(0), Is.EqualTo(0f));
        }

        [Test]
        public void Coverage_CountsTheSeenCells()
        {
            var profile = new byte[256];
            Assert.That(ObjectProfile.Coverage(profile), Is.EqualTo(0f));
            for (int c = 0; c < 64; c++) profile[c * 3] = 1;
            Assert.That(ObjectProfile.Coverage(profile), Is.EqualTo(0.25f));
            Assert.That(ObjectProfile.Coverage(new byte[10]), Is.EqualTo(0f));
        }

        [Test]
        public void Compute_AFineSheetUnderSixtyFourBoxes_LeavesNoGap()
        {
            // A 6 m x 6 m sheet at 0.5 m in 5 cm cells (28,800 triangles), read by 64 boxes at as many yaws:
            // every one of their 65,536 rays lands on it, also those that pass through an edge of the sheet.
            const int cells = 120;
            var vertices = new float3[(cells + 1) * (cells + 1)];
            for (int j = 0; j <= cells; j++)
                for (int i = 0; i <= cells; i++)
                    vertices[j * (cells + 1) + i] = new float3(i * 0.05f, 0.5f, j * 0.05f);
            var indices = new int[cells * cells * 6];
            for (int j = 0, at = 0; j < cells; j++)
            {
                for (int i = 0; i < cells; i++)
                {
                    int a = j * (cells + 1) + i, b = a + 1, c = a + cells + 2, d = a + cells + 1;
                    indices[at++] = a; indices[at++] = b; indices[at++] = c;
                    indices[at++] = a; indices[at++] = c; indices[at++] = d;
                }
            }
            for (int k = 0; k < 64; k++)
            {
                var basePoint = new float3(0.6f + 0.6f * (k % 8) + 0.013f * k, 0f, 0.6f + 0.6f * (k / 8) + 0.007f * k);
                byte[] profile = ObjectProfile.Compute(vertices, indices, null, basePoint, new float3(0.5f, 1f, 0.4f), 0.1f * k);
                Assert.That(profile, Is.EqualTo(Filled(128)), "box " + k);
            }
        }
    }
}
