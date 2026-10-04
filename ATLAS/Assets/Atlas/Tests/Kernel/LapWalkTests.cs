using System;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Kernel.Tests
{
    // P1-B09 LapWalk_Unit: the 3-4-5 walk tracker of MP §3.3, §4.4, A.4 (loop-fit values), §10.1.3 (corner counting)
    public class LapWalkTests
    {
        static double MaxDiff(in Iso2 a, in Iso2 b)
            => math.max(math.cmax(math.abs(a.M.c0 - b.M.c0)), math.max(math.cmax(math.abs(a.M.c1 - b.M.c1)), math.cmax(math.abs(a.M.c2 - b.M.c2))));

        // A.4: cosh(s/2L) = sqrt2 cos(pi/n)
        static double SideOverL(int n) => 2 * Math.Acosh(Math.Sqrt(2) * Math.Cos(Math.PI / n));

        [Test] // MP A.4 "closes after n sides": q = 5, s/L = 1.0612751
        public void Beacon5_ReturnsToFlag()
        {
            double s = SideOverL(5);
            Assert.AreEqual(1.0612751, s, 1e-7);
            var w = new LapWalk();
            Iso2 flag = TileZeroPlacement.LapFlag(s, -1, 1);
            w.Arm(5, -1, 1, s, flag);
            Assert.Less(MaxDiff(w.Beacon(5), flag), 1e-9);
            Assert.Greater(MaxDiff(w.Beacon(4), flag), 0.1);
        }

        [Test] // MP A.4 q = 3 sphere: s/R = pi/2
        public void Beacon3_ReturnsToFlag_Sphere()
        {
            var w = new LapWalk();
            Iso2 flag = TileZeroPlacement.LapFlag(Math.PI / 2, +1, 1);
            w.Arm(3, +1, 1, Math.PI / 2, flag);
            Assert.Less(MaxDiff(w.Beacon(3), flag), 1e-9);
        }

        [Test] // MP A.4 q = 4 flat: always
        public void Beacon4_ReturnsToFlag_Flat()
        {
            var w = new LapWalk();
            Iso2 flag = TileZeroPlacement.LapFlag(2.0, 0, 1);
            w.Arm(4, 0, 1, 2.0, flag);
            Assert.Less(MaxDiff(w.Beacon(4), flag), 1e-9);
            Assert.AreEqual(-1.0, w.Beacon(0).M.c2.x, 1e-12); Assert.AreEqual(-1.0, w.Beacon(0).M.c2.y, 1e-12);
            Assert.AreEqual(1.0, w.Beacon(1).M.c2.x, 1e-12); Assert.AreEqual(-1.0, w.Beacon(1).M.c2.y, 1e-12);
        }

        [Test] // MP §4.6 / §10.1.3: a corner counts only when the walker stands (D6), reach 0.3 m inclusive
        public void NoEventWithoutStill_ReachInclusive()
        {
            var w = new LapWalk();
            Iso2 flag = TileZeroPlacement.LapFlag(2.0, 0, 1);
            w.Arm(4, 0, 1, 2.0, flag);
            Iso2 id = Iso2.Identity;
            Iso2 at(double x, double z) => Iso2.Boost(new double2(x, z), 0);
            Assert.AreEqual(LapEvent.None, w.Update(id, at(-1, -1), false));
            Assert.IsFalse(w.Started);
            Assert.AreEqual(LapEvent.None, w.Update(id, at(-0.69, -1), true));    // 0.31 m: outside
            Assert.AreEqual(LapEvent.Started, w.Update(id, at(-0.7 - 1e-9, -1), true));   // 0.3 m: inside
            Assert.AreEqual(0, w.Corners); Assert.AreEqual(1, w.NextBeacon);
            Assert.AreEqual(LapEvent.None, w.Update(id, at(1, -1), false));
            Assert.AreEqual(LapEvent.Corner, w.Update(id, at(1, -1), true));
            Assert.AreEqual(1, w.Corners); Assert.AreEqual(2, w.NextBeacon);
            Assert.AreEqual(2.0, w.DistanceToFlagM, 1e-9);
            w.Update(id, at(1, 1), true); w.Update(id, at(-1, 1), true);
            Assert.AreEqual(LapEvent.Closed, w.Update(id, at(-1, -1), true));
            Assert.IsTrue(w.Closed); Assert.AreEqual(-1, w.NextBeacon);
            Assert.AreEqual(LapEvent.None, w.Update(id, at(1, -1), true));        // D19: stops after Closed
            Assert.AreEqual(4, w.Corners);
            w.Disarm(); Assert.IsFalse(w.Armed); Assert.AreEqual(-1, w.NextBeacon);
        }

        [Test] // MP §3.3: after 4 corners at q = 5 the world turned 90 degrees round you; q = 3 the sphere is opposite
        public void WorldTurn_Signs()
        {
            double s5 = SideOverL(5);
            var w = new LapWalk();
            w.Arm(5, -1, 1, s5, TileZeroPlacement.LapFlag(s5, -1, 1));
            Iso2 u = w.Beacon(0);                       // walker frame without heading change: boosts only
            Iso2 id = new Iso2(double3x3.identity, -1);
            // walk the sides as the rig does: U <- U * Boost(d), d in world axes
            double2[] dirs = { new double2(1, 0), new double2(0, 1), new double2(-1, 0), new double2(0, -1) };
            for (int k = 0; k < 4; k++) u = (u * Iso2.Boost(dirs[k] * s5, -1)).Orthonormalised();
            w.Update(id, u, false);
            Assert.AreEqual(90.0, w.WorldTurnDeg, 1.0);
            double s3 = Math.PI / 2;
            var v = new LapWalk();
            v.Arm(3, +1, 1, s3, TileZeroPlacement.LapFlag(s3, +1, 1));
            Iso2 us = v.Beacon(0);
            for (int k = 0; k < 3; k++) us = us * Iso2.Boost(dirs[k] * s3, +1);
            v.Update(new Iso2(double3x3.identity, +1), us, false);
            Assert.AreEqual(-90.0, v.WorldTurnDeg, 1.0);
        }
    }
}
