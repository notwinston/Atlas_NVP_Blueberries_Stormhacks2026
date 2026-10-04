using System;
using NUnit.Framework;

namespace Atlas.Kernel.Tests
{
    // P1-B09. Row Output "snaps q = 3,4,5,6"; MP §3.2, §4.4 (comfort floors), §10.1.3 (D-pad, lever).
    public class CurvatureDialTests
    {
        // Dial_Snaps: Output "snaps q = 3,4,5,6" (MP §9.5.6 P1-B09)
        [Test]
        public void Dial_Snaps()
        {
            var d = new CurvatureDial(5);
            Assert.IsTrue(d.Next()); Assert.AreEqual(6, d.Q);
            Assert.IsFalse(d.Next()); Assert.AreEqual(6, d.Q);          // clamps at 6
            Assert.IsTrue(d.Prev()); Assert.IsTrue(d.Prev()); Assert.IsTrue(d.Prev()); Assert.AreEqual(3, d.Q);
            Assert.IsFalse(d.Prev()); Assert.AreEqual(3, d.Q);          // clamps at 3
            var seq = new System.Collections.Generic.List<int>();
            for (int i = 0; i < 4; i++) { d.Cycle(); seq.Add(d.Q); }
            CollectionAssert.AreEqual(new[] { 4, 5, 6, 3 }, seq);       // lever twin 3 to 4 to 5 to 6 to 3
            Assert.Throws<ArgumentOutOfRangeException>(() => d.Set(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => d.Set(7));
            Assert.IsFalse(d.Set(3)); Assert.IsTrue(d.Set(4));
        }

        // Dial_Target_room_3x3: loop-fit values read from RoomModel.curvature, no floor applies (MP §4.3, §4.4)
        [Test]
        public void Target_Room3x3_NoFloor()
        {
            AssertTarget(CurvatureDial.Target(5, 1.884481, 1.518603, 1.27324, false), -1, 1.884481, false, DialGeometry.Hyperbolic);
            AssertTarget(CurvatureDial.Target(6, 1.884481, 1.518603, 1.27324, false), -1, 1.518603, false, DialGeometry.Hyperbolic);
            AssertTarget(CurvatureDial.Target(3, 1.884481, 1.518603, 1.27324, false), +1, 1.27324, false, DialGeometry.Spherical);
            AssertTarget(CurvatureDial.Target(4, 1.884481, 1.518603, 1.27324, false), 0, 1.0, false, DialGeometry.Flat);
        }

        // Dial_Target_room_4x5_table: MP §4.4 comfort floors L >= 1.4, R >= 1.2 unless strong
        [Test]
        public void Target_Room4x5_FloorsAndStrong()
        {
            var a = CurvatureDial.Target(5, 1.036465, 0.835232, 0.700282, false);
            var b = CurvatureDial.Target(6, 1.036465, 0.835232, 0.700282, false);
            var c = CurvatureDial.Target(3, 1.036465, 0.835232, 0.700282, false);
            Assert.AreEqual(1.4, a.LengthScaleM, 1e-12); Assert.IsTrue(a.FloorApplied);
            Assert.AreEqual(1.4, b.LengthScaleM, 1e-12); Assert.IsTrue(b.FloorApplied);
            Assert.AreEqual(1.2, c.LengthScaleM, 1e-12); Assert.IsTrue(c.FloorApplied);
            var s5 = CurvatureDial.Target(5, 1.036465, 0.835232, 0.700282, true);
            var s6 = CurvatureDial.Target(6, 1.036465, 0.835232, 0.700282, true);
            var s3 = CurvatureDial.Target(3, 1.036465, 0.835232, 0.700282, true);
            Assert.AreEqual(1.036465, s5.LengthScaleM, 1e-12); Assert.IsFalse(s5.FloorApplied);
            Assert.AreEqual(0.835232, s6.LengthScaleM, 1e-12);
            Assert.AreEqual(0.700282, s3.LengthScaleM, 1e-12);
        }

        static void AssertTarget(DialTarget t, double kappa, double len, bool floor, DialGeometry g)
        {
            Assert.AreEqual(kappa, t.Kappa); Assert.AreEqual(len, t.LengthScaleM, 1e-12);
            Assert.AreEqual(floor, t.FloorApplied); Assert.AreEqual(g, t.Geometry);
        }
    }
}
