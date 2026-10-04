using System;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Spaces.AlignedCells.Tests
{
    public class RoomContentTests
    {
        static CellGrid GridOf(RoomModel r) => CellGrid.FromFreeRectangle(new FreeRectangle(r.freeRect.Wf, r.freeRect.Df));

        // Criterion: Master Plan §4.3 cells.aligned; §4.7 grid rule (PH-22 corner demo) [P1-B12 D10]
        [TestCase("room_3x3", 3, 3, 0.8)]
        [TestCase("room_3x4_L", 3, 3, 0.8)]
        [TestCase("room_4x5_table", 2, 2, 0.75)]
        [TestCase("budget_room", 3, 4, 0.933333)]
        public void Mode2_Room_GridMatchesRoomModel(string name, int cx, int cz, double g)
        {
            var room = Fixtures.Room(name);
            var grid = GridOf(room);
            Assert.AreEqual(cx, grid.CountX); Assert.AreEqual(cz, grid.CountZ);
            Assert.AreEqual(g, grid.Side, 1e-5);
            var a = room.cells.aligned;
            bool xShort = room.freeRect.Wf <= room.freeRect.Df;
            Assert.AreEqual(xShort ? a.n : a.m, grid.CountX); Assert.AreEqual(xShort ? a.m : a.n, grid.CountZ);
            Assert.AreEqual(a.g, grid.Side, 1e-5);
        }

        // Row output "Home walls on the edge of the cell grid"; Master Plan §4.3 (0.30 m inside walls)
        [TestCase("room_3x3", 0.30)]
        [TestCase("room_3x4_L", 0.30)]
        [TestCase("room_4x5_table", 0.30)]
        [TestCase("budget_room", 0.60)]
        public void Mode2_Room_WallsBoundGrid(string name, double expectedClearance)
        {
            var room = Fixtures.Room(name);
            var grid = GridOf(room);
            double2 lo = grid.Origin, hi = grid.Origin + new double2(grid.CountX * grid.Side, grid.CountZ * grid.Side);
            var corners = new[] { lo, new double2(hi.x, lo.y), hi, new double2(lo.x, hi.y) };
            foreach (var c in corners) Assert.IsTrue(InsideEvenOdd(room, c), "grid corner outside the wall polygon " + c);
            double min = double.MaxValue;
            foreach (var w in room.walls)
            {
                double2 a = new double2(w.a.x, w.a.z), b = new double2(w.b.x, w.b.z);
                for (int i = 0; i < 4; i++)
                {
                    double2 p = corners[i], q = corners[(i + 1) % 4];
                    Assert.IsFalse(Crosses(a, b, p, q), "wall crosses the grid rectangle");
                    min = Math.Min(min, SegSegDist(a, b, p, q));
                }
            }
            TestContext.WriteLine($"{name}: wall clearance {min:F3} m (expected {expectedClearance:F2})");
            Assert.GreaterOrEqual(min, 0.30 - 1e-4);
            Assert.AreEqual(expectedClearance, min, 1e-3);
        }

        static bool InsideEvenOdd(RoomModel r, double2 p)
        {
            bool inside = false;
            foreach (var w in r.walls)
            {
                double ax = w.a.x, az = w.a.z, bx = w.b.x, bz = w.b.z;
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
            var bud = Fixtures.Room("budget_room");
            var g = GridOf(bud);
            int[][] exp = {
                new[]{0,0,0,0}, new[]{0,1,0,2}, new[]{0,3,0,3}, new[]{2,0,2,0}, new[]{2,1,2,2}, new[]{2,3,2,3},
                new[]{0,0,0,0}, new[]{1,0,1,0}, new[]{2,0,2,0}, new[]{0,3,0,3}, new[]{1,3,1,3}, new[]{2,3,2,3} };
            Assert.AreEqual(12, bud.objects.Length);
            for (int i = 0; i < 12; i++)
                Assert.AreEqual(new CellRange(new int2(exp[i][0], exp[i][1]), new int2(exp[i][2], exp[i][3])), RoomObjectCells.Of(g, bud.objects[i]), "object " + i);

            var tbl = Fixtures.Room("room_4x5_table");
            Assert.AreEqual(new CellRange(new int2(0, 1), new int2(1, 1)), RoomObjectCells.Of(GridOf(tbl), tbl.objects[0]));

            // Synthetic, room_3x3 grid (g = 0.8, origin -1.2). Footprint x 0.93..1.77, z -0.42..0.42 gives (2,0)-(2,2).
            // (The brief's z = 0.3 would give (2,1)-(2,2); z = 0 is used so that the three rows are crossed.)
            var r3 = Fixtures.Room("room_3x3");
            var syn = RoomEdit.Obj(0, 1.35f, 0f, 0.2f, 0.7f, 1.0f, Mathf.PI / 4);
            Assert.AreEqual(new CellRange(new int2(2, 0), new int2(2, 2)), RoomObjectCells.Of(GridOf(r3), syn));

            foreach (float yaw in new[] { 0f, 0.7f, -2.1f })
            {
                var o = RoomEdit.Obj(0, 0.4f, -0.3f, 0.5f, 0.8f, 0.3f, yaw);
                var local = new double3(0.21, 0.5, -0.13);
                double3 a = RoomObjectCells.BoxToAnchor(o, local);
                Vector3 b = RoomObjectCells.Rotation(o) * new Vector3((float)local.x, (float)local.y, (float)local.z) + o.position;
                Assert.AreEqual(a.x, b.x, 1e-5); Assert.AreEqual(a.y, b.y, 1e-5); Assert.AreEqual(a.z, b.z, 1e-5);
            }
        }
    }
}
