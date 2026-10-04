using System;
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

        // Master Plan A.7
        [Test]
        public void Mode2_EquivalentRadius()
        {
            var g = G(2.4, 2.4);
            Assert.AreEqual(0.892, g.EquivalentRadius / g.Side, 1e-3);
        }
    }
}
