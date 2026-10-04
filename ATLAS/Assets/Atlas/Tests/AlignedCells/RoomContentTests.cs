using System;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells.Tests
{
    public class RoomContentTests
    {
        static CellGrid GridOf(RoomModel r) => CellGrid.FromRoom(r);

        // Row output "Home walls on the edge of the cell grid"; Master Plan §4.3 (0.30 m inside walls)
        [TestCase(RoomFixtures.Sample3x3)]
        [TestCase(RoomFixtures.SampleL3x4)]
        [TestCase(RoomFixtures.Sample4x5Table)]
        [TestCase(RoomFixtures.BudgetRoom)]
        [TestCase(RoomFixtures.TinyStanding)]
        public void Mode2_Room_WallsBoundGrid(string name)
        {
            var room = Fixtures.Room(name);
            var grid = GridOf(room);
            double2 lo = grid.Origin, hi = grid.Origin + new double2(grid.CountX * grid.Side, grid.CountZ * grid.Side);
            Assert.LessOrEqual(hi.x - lo.x, room.FreeRect.Wf + 1e-6); Assert.LessOrEqual(hi.y - lo.y, room.FreeRect.Df + 1e-6);
            var corners = new[] { lo, new double2(hi.x, lo.y), hi, new double2(lo.x, hi.y) };
            foreach (var c in corners) Assert.IsTrue(InsideEvenOdd(room, c), "grid corner outside the wall polygon " + c);
            double min = double.MaxValue;
            foreach (var w in room.Walls)
            {
                double2 a = w.A, b = w.B;
                for (int i = 0; i < 4; i++)
                {
                    double2 p = corners[i], q = corners[(i + 1) % 4];
                    Assert.IsFalse(Crosses(a, b, p, q), "wall crosses the grid rectangle");
                    min = Math.Min(min, SegSegDist(a, b, p, q));
                }
            }
            TestContext.WriteLine($"{name}: wall clearance {min:F3} m");
            Assert.GreaterOrEqual(min, 0.30 - 1e-4);
        }

        static bool InsideEvenOdd(RoomModel r, double2 p)
        {
            bool inside = false;
            foreach (var w in r.Walls)
            {
                double ax = w.A.x, az = w.A.y, bx = w.B.x, bz = w.B.y;
                if ((az > p.y) != (bz > p.y) && p.x < (bx - ax) * (p.y - az) / (bz - az) + ax) inside = !inside;
            }
            return inside;
        }

        static double Cross(double2 o, double2 a, double2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        static bool Crosses(double2 a, double2 b, double2 p, double2 q)
            => Cross(a, b, p) * Cross(a, b, q) < 0 && Cross(p, q, a) * Cross(p, q, b) < 0;
        static double PointSeg(double2 p, double2 a, double2 b)
        {
            double2 d = b - a; double t = math.clamp(math.dot(p - a, d) / math.dot(d, d), 0, 1);
            return math.length(p - (a + t * d));
        }
        static double SegSegDist(double2 a, double2 b, double2 p, double2 q)   // for non-crossing segments
            => Math.Min(Math.Min(PointSeg(a, p, q), PointSeg(b, p, q)), Math.Min(PointSeg(p, a, b), PointSeg(q, a, b)));

        // Criterion: [P1-B12 D1] object footprint to cell range; Master Plan §4.3, §4.7
        [Test]
        public void Mode2_Room_ObjectCells()
        {
            // The table of the 4 x 5 room: base (-1.4, 0, 0), 1.0 x 0.75 x 0.6, yaw -90 deg, so its footprint is x -1.7 .. -1.1,
            // z -0.5 .. 0.5. The grid is 2 x 2 of 0.8 from (-0.8, -0.8): outside it in x (column 0 by the clamp), both rows.
            var tbl = Fixtures.Room(RoomFixtures.Sample4x5Table);
            Assert.AreEqual(1, tbl.Objects.Length);
            Assert.AreEqual(new CellRange(new int2(0, 0), new int2(0, 1)), RoomObjectCells.Of(GridOf(tbl), tbl.Objects[0]));

            // Every object of the budget room: the range is the bounding range of the cells of its footprint's corners,
            // and its base centre lies in it.
            var bud = Fixtures.Room(RoomFixtures.BudgetRoom);
            var g = GridOf(bud);
            Assert.AreEqual(12, bud.Objects.Length);
            foreach (RoomObject o in bud.Objects)
            {
                CellRange r = RoomObjectCells.Of(g, o);
                Assert.IsTrue(math.all(r.Min <= r.Max));
                Assert.IsTrue(g.Contains(r.Min) && g.Contains(r.Max));
                Assert.IsTrue(r.Contains(g.CellAt(new double2(o.Base.x, o.Base.z))), "object " + o.Id);
                bool onMinX = false, onMaxX = false, onMinZ = false, onMaxZ = false;
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        double3 p = RoomObjectCells.BoxToAnchor(o, new double3(sx * o.Size.x / 2.0, 0, sz * o.Size.z / 2.0));
                        int2 c = g.CellAt(new double2(p.x, p.z));
                        Assert.IsTrue(r.Contains(c), "object " + o.Id);
                        onMinX |= c.x == r.Min.x; onMaxX |= c.x == r.Max.x; onMinZ |= c.y == r.Min.y; onMaxZ |= c.y == r.Max.y;
                    }
                Assert.IsTrue(onMinX && onMaxX && onMinZ && onMaxZ, "tight, object " + o.Id);
            }

            // Synthetic, sample_3x3 grid (g = 0.8, origin -1.2). Footprint x 0.93..1.77, z -0.42..0.42 gives (2,0)-(2,2).
            var r3 = Fixtures.Room(RoomFixtures.Sample3x3);
            var syn = Fixtures.Obj(1, 1.35f, 0f, 0.2f, 0.7f, 1.0f, math.PI / 4);
            Assert.AreEqual(new CellRange(new int2(2, 0), new int2(2, 2)), RoomObjectCells.Of(GridOf(r3), syn));

            // The yaw sense is the room content's: RoomObject.Rotation about the base centre.
            foreach (float yaw in new[] { 0f, 0.7f, -2.1f })
            {
                var o = Fixtures.Obj(1, 0.4f, -0.3f, 0.5f, 0.8f, 0.3f, yaw);
                var local = new double3(0.21, 0.5, -0.13);
                double3 a = RoomObjectCells.BoxToAnchor(o, local);
                float3 b = math.mul(o.Rotation, (float3)local) + o.Base;
                Assert.AreEqual(a.x, b.x, 1e-5); Assert.AreEqual(a.y, b.y, 1e-5); Assert.AreEqual(a.z, b.z, 1e-5);
            }
        }
    }
}
