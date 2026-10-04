using System;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Routes
{
    /// <summary>Route 1: from tapped corners or a walked path to a closed, ordered, squared-up outline.</summary>
    public class OutlineBuilderTests
    {
        static void AssertRightAngles(float2[] ring)
        {
            for (int k = 0; k < ring.Length; k++)
            {
                float2 a = math.normalize(ring[k] - ring[(k + ring.Length - 1) % ring.Length]);
                float2 b = math.normalize(ring[(k + 1) % ring.Length] - ring[k]);
                Assert.That(math.dot(a, b), Is.EqualTo(0f).Within(1e-4f), "corner " + k + " is not square");
            }
        }

        [Test]
        public void NoisyRectangle_IsSquaredUp()
        {
            var random = new System.Random(7);
            float2[] exact = ScanTestRooms.Moved(ScanTestRooms.Rectangle(3f, 4f), math.radians(20f), new float2(1.3f, -0.7f));
            var tapped = new float2[4];
            for (int k = 0; k < 4; k++) tapped[k] = exact[k] + ScanTestRooms.Jitter(random, 0.02f);

            float2[] outline = OutlineBuilder.FromCorners(tapped, 4);

            Assert.That(outline, Is.Not.Null);
            Assert.That(outline.Length, Is.EqualTo(4));
            AssertRightAngles(outline);
            Assert.That(OutlineBuilder.SignedArea(outline, 4), Is.EqualTo(12f).Within(0.3f));
            for (int k = 0; k < 4; k++) Assert.That(ScanTestRooms.NearestCorner(exact[k], outline), Is.LessThan(0.05f));
        }

        [Test]
        public void ClosingPoint_IsDropped_AndRingIsCounterClockwise()
        {
            // Tapped clockwise, and the first corner tapped again to close the ring.
            float2[] tapped = { new float2(0f, 0f), new float2(0f, 3f), new float2(3f, 3f), new float2(3f, 0f), new float2(0.01f, 0.01f) };
            float2[] outline = OutlineBuilder.FromCorners(tapped, tapped.Length);
            Assert.That(outline.Length, Is.EqualTo(4));
            Assert.That(OutlineBuilder.SignedArea(outline, 4), Is.EqualTo(9f).Within(1e-3f));
        }

        [Test]
        public void CornersTappedOutOfOrder_ArePutInOrder()
        {
            // A bow tie as tapped: the diagonals cross.
            float2[] tapped = { new float2(0f, 0f), new float2(3f, 3f), new float2(3f, 0f), new float2(0f, 3f) };
            Assert.That(OutlineBuilder.IsSimple(tapped, 4), Is.False);
            float2[] outline = OutlineBuilder.FromCorners(tapped, 4);
            Assert.That(outline, Is.Not.Null);
            Assert.That(OutlineBuilder.IsValid(outline, outline.Length), Is.True);
            Assert.That(OutlineBuilder.SignedArea(outline, outline.Length), Is.EqualTo(9f).Within(1e-3f));
        }

        [Test]
        public void LShape_KeepsItsSixCorners()
        {
            var random = new System.Random(3);
            float2[] exact = ScanTestRooms.Moved(ScanTestRooms.LOutline, math.radians(-33f), new float2(-2f, 5f));
            var tapped = new float2[exact.Length];
            for (int k = 0; k < exact.Length; k++) tapped[k] = exact[k] + ScanTestRooms.Jitter(random, 0.02f);
            float2[] outline = OutlineBuilder.FromCorners(tapped, tapped.Length);
            Assert.That(outline.Length, Is.EqualTo(6));
            AssertRightAngles(outline);
            for (int k = 0; k < exact.Length; k++) Assert.That(ScanTestRooms.NearestCorner(exact[k], outline), Is.LessThan(0.05f));
        }

        [Test]
        public void SlantedWall_KeepsItsDirection()
        {
            // A room with one corner cut off at 45 degrees.
            float2[] tapped = { new float2(0f, 0f), new float2(4f, 0.02f), new float2(4.01f, 2f), new float2(3f, 3f), new float2(0.02f, 3f) };
            float2[] outline = OutlineBuilder.FromCorners(tapped, tapped.Length);
            Assert.That(outline.Length, Is.EqualTo(5));
            bool slanted = false;
            for (int k = 0; k < 5; k++)
            {
                float2 d = math.normalize(outline[(k + 1) % 5] - outline[k]);
                if (math.abs(math.abs(d.x) - math.abs(d.y)) < 0.05f) slanted = true;
            }
            Assert.That(slanted, Is.True, "the 45 degree wall was squared away");
        }

        [Test]
        public void CollinearCorner_IsDropped()
        {
            float2[] tapped = { new float2(0f, 0f), new float2(1.5f, 0.01f), new float2(3f, 0f), new float2(3f, 3f), new float2(0f, 3f) };
            Assert.That(OutlineBuilder.FromCorners(tapped, tapped.Length).Length, Is.EqualTo(4));
        }

        [Test]
        public void TooFewCorners_GiveNoOutline()
        {
            float2[] tapped = { new float2(0f, 0f), new float2(3f, 0f), new float2(3.01f, 0.01f) };
            Assert.That(OutlineBuilder.FromCorners(tapped, 2), Is.Null);
            Assert.That(OutlineBuilder.FromCorners(tapped, 3), Is.Null);
        }

        [Test]
        public void IsValid_RefusesCrossingAndShortEdges()
        {
            Assert.That(OutlineBuilder.IsValid(ScanTestRooms.Rectangle(3f, 3f), 4), Is.True);
            float2[] crossing = { new float2(0f, 0f), new float2(3f, 3f), new float2(3f, 0f), new float2(0f, 3f) };
            Assert.That(OutlineBuilder.IsValid(crossing, 4), Is.False);
            float2[] shortEdge = { new float2(0f, 0f), new float2(3f, 0f), new float2(3f, 0.03f), new float2(3f, 3f), new float2(0f, 3f) };
            Assert.That(OutlineBuilder.IsValid(shortEdge, 5), Is.False);
            float2[] notFinite = { new float2(0f, 0f), new float2(float.NaN, 0f), new float2(3f, 3f) };
            Assert.That(OutlineBuilder.IsValid(notFinite, 3), Is.False);
        }

        [Test]
        public void WalkedPath_GivesTheWalls()
        {
            // A lap 0.30 m inside the walls of a 3 x 4 m room, a sample every 5 cm, with a wobble.
            var random = new System.Random(11);
            float2[] lap = { new float2(0.3f, 0.3f), new float2(2.7f, 0.3f), new float2(2.7f, 3.7f), new float2(0.3f, 3.7f) };
            var path = new System.Collections.Generic.List<float2>();
            for (int k = 0; k < 4; k++)
            {
                float2 a = lap[k], b = lap[(k + 1) % 4];
                int steps = (int)(math.distance(a, b) / 0.05f);
                for (int s = 0; s < steps; s++) path.Add(math.lerp(a, b, (float)s / steps) + ScanTestRooms.Jitter(random, 0.02f));
            }
            float2[] outline = OutlineBuilder.FromPath(path.ToArray(), path.Count);
            Assert.That(outline, Is.Not.Null);
            Assert.That(outline.Length, Is.EqualTo(4));
            foreach (float2 corner in ScanTestRooms.Rectangle(3f, 4f))
                Assert.That(ScanTestRooms.NearestCorner(corner, outline), Is.LessThan(0.08f));
        }

        [Test]
        public void StraightWalk_IsNoOutline()
        {
            var path = new float2[40];
            for (int k = 0; k < path.Length; k++) path[k] = new float2(0.05f * k, 0f);
            Assert.That(OutlineBuilder.FromPath(path, path.Length), Is.Null);
        }

        [Test]
        public void Simplify_KeepsCornersWithinTolerance()
        {
            var ring = new System.Collections.Generic.List<float2>();
            float2[] square = ScanTestRooms.Rectangle(2f, 2f);
            for (int k = 0; k < 4; k++)
                for (int s = 0; s < 20; s++) ring.Add(math.lerp(square[k], square[(k + 1) % 4], s / 20f));
            float2[] simple = OutlineBuilder.Simplify(ring.ToArray(), ring.Count, 0.02f);
            Assert.That(simple.Length, Is.EqualTo(4));
        }

        [Test]
        public void Offset_GrowsARectangle()
        {
            float2[] grown = OutlineBuilder.Offset(ScanTestRooms.Rectangle(2f, 3f), 0.3f);
            Assert.That(OutlineBuilder.SignedArea(grown, 4), Is.EqualTo(2.6f * 3.6f).Within(1e-3f));
        }
    }
}
