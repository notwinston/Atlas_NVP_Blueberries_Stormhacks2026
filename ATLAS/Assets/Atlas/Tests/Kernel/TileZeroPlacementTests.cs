using System;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Kernel.Tests
{
    // P1-B11 (MP §4.6 placement about the room centre; kernel.md §2.4 yaw sense)
    public class TileZeroPlacementTests
    {
        [Test] // MP §4.6: room metres are tile zero's chart metres
        public void Registered_IsExpOfHeadOverL()
        {
            var h = new double2(0.3, -0.2);
            var r = TileZeroPlacement.Registered(h, -1, 1.884481);
            var e = Geometry2.Exp(h / 1.884481, -1);
            Assert.Less(math.length(r.M.c2 - e), 1e-12);
            var f = TileZeroPlacement.Registered(h, 0, 5.0);   // flat: metres, scale ignored
            Assert.AreEqual(0.3, f.M.c2.x, 1e-15); Assert.AreEqual(-0.2, f.M.c2.y, 1e-15); Assert.AreEqual(0, f.Kappa);
        }

        [Test] // kernel.md §2.4: Rot(theta) is a Unity yaw of -theta; a point on the object's +z axis lands towards +x for positive yaw
        public void Object_YawSense()
        {
            double yaw = 0.7;
            Iso2 p = TileZeroPlacement.Object(new double2(1, 2), yaw, 0, 1);
            double3 pt = math.mul(p.M, new double3(0, 1, 1));   // local +z, 1 m ahead
            Assert.AreEqual(1 + Math.Sin(yaw), pt.x, 1e-12);
            Assert.AreEqual(2 + Math.Cos(yaw), pt.y, 1e-12);
            Iso2 h = TileZeroPlacement.Object(new double2(0.5, 0.25), yaw, -1, 1.4);
            double3 hp = math.mul(h.M, Geometry2.Exp(new double2(0, 1) / 1.4, -1));
            double2 lg = Geometry2.Log(hp, -1) * 1.4;
            double2 origin = Geometry2.Log(h.M.c2, -1) * 1.4;
            Assert.Greater(lg.x - origin.x, 0.0);   // yaw > 0 turns +z towards +x
        }

        [Test] // MP A.4: flag at c0 = (-s/2, -s/2); Boost(c0 / l)
        public void LapFlag_IsCorner0()
        {
            var f = TileZeroPlacement.LapFlag(2.0, -1, 1.884481);
            double2 back = Geometry2.Log(f.M.c2, -1) * 1.884481;
            Assert.AreEqual(-1.0, back.x, 1e-9); Assert.AreEqual(-1.0, back.y, 1e-9);
            var flat = TileZeroPlacement.LapFlag(2.0, 0, 1);
            Assert.AreEqual(-1.0, flat.M.c2.x, 1e-15);
        }
    }
}
