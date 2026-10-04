using System;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells.Tests
{
    public class CellGridTests
    {
        static CellGrid G(double w, double d) => CellGrid.FromFreeRectangle(new FreeRectangle(w, d));

        // Master Plan §4.7 grid formula and worked examples
        [Test]
        public void Mode2_GridRule()
        {
            void Check(double w, double d, int cx, int cz, double g, bool demo)
            {
                var grid = G(w, d);
                Assert.AreEqual(cx, grid.CountX, $"{w}x{d} CountX");
                Assert.AreEqual(cz, grid.CountZ, $"{w}x{d} CountZ");
                Assert.AreEqual(g, grid.Side, 1e-12, $"{w}x{d} g");
                Assert.AreEqual(demo, grid.CornerDemo);
            }
            Check(2.4, 2.4, 3, 3, 0.8, false);
            Check(3.4, 4.4, 3, 3, 3.4 / 3, false);
            Check(5.4, 7.4, 4, 5, 1.35, false);
            Check(7.4, 5.4, 5, 4, 1.35, false);
            Check(1.2, 1.4, 2, 2, 0.6, true);
            Check(2.25, 3, 3, 4, 0.75, false);
            Assert.AreEqual(4, G(3.0, 4.0 - 1e-9).CountZ, "m guard 1e-6");
            Assert.Throws<ArgumentOutOfRangeException>(() => G(0.7, 3.0));
            var g33 = G(2.4, 2.4);
            Assert.AreEqual(0, g33.CellAt(new double2(-1.2 - 1, 0)).x);
            Assert.AreEqual(2, g33.CellAt(new double2(5, 0)).x);
            Assert.AreEqual(4, g33.InteriorCornerCount);
            Assert.AreEqual(1, G(1.2, 1.4).InteriorCornerCount);
            Assert.AreEqual(12, G(5.4, 7.4).InteriorCornerCount);
        }

        // Decision: one grid rule. The grid of a RoomModel is its cells.aligned, which is CellGridRules.Aligned of the free rectangle.
        [TestCase(RoomFixtures.Sample3x3, 3, 3, 0.8)]
        [TestCase(RoomFixtures.SampleL3x4, 2, 2, 0.95)]
        [TestCase(RoomFixtures.Sample4x5Table, 2, 2, 0.8)]
        [TestCase(RoomFixtures.BudgetRoom, 3, 4, 0.8)]
        [TestCase(RoomFixtures.TinyStanding, 2, 2, 0.7)]
        public void Mode2_GridIsTheRoomModels(string name, int cx, int cz, double g)
        {
            RoomModel room = Fixtures.Room(name);
            CellGrid grid = CellGrid.FromRoom(room);
            Assert.AreEqual(cx, grid.CountX); Assert.AreEqual(cz, grid.CountZ);
            Assert.AreEqual(g, grid.Side, 1e-6);
            bool xShort = room.FreeRect.Wf <= room.FreeRect.Df;
            Assert.AreEqual(xShort ? room.Aligned.N : room.Aligned.M, grid.CountX);
            Assert.AreEqual(xShort ? room.Aligned.M : room.Aligned.N, grid.CountZ);
            Assert.AreEqual(room.Aligned.G, grid.Side, 0);
            Assert.AreEqual(room.Aligned.N == 2, grid.CornerDemo);

            AlignedGrid rule = CellGridRules.Aligned(Math.Round(room.FreeRect.Wf, 6), Math.Round(room.FreeRect.Df, 6));
            CellGrid free = G(Math.Round(room.FreeRect.Wf, 6), Math.Round(room.FreeRect.Df, 6));
            Assert.AreEqual(rule.N, room.Aligned.N); Assert.AreEqual(rule.M, room.Aligned.M);
            Assert.AreEqual(grid.CountX, free.CountX); Assert.AreEqual(grid.CountZ, free.CountZ);
            Assert.AreEqual(grid.Side, free.Side, 1e-6);

            // A model that was never fitted gets the same grid from its free rectangle.
            var bare = new RoomModel { FreeRect = room.FreeRect };
            CellGrid again = CellGrid.FromRoom(bare);
            Assert.AreEqual(grid.CountX, again.CountX); Assert.AreEqual(grid.CountZ, again.CountZ);
            Assert.AreEqual(grid.Side, again.Side, 1e-6);
        }

        // Master Plan A.7
        [Test]
        public void Mode2_EquivalentRadius()
        {
            var g = G(2.4, 2.4);
            Assert.AreEqual(0.892, g.EquivalentRadius / g.Side, 1e-3);
        }
    }
}
