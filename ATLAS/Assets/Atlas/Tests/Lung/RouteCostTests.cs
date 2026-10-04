using System;
using System.Globalization;
using System.Threading;
using NUnit.Framework;

namespace Atlas.Blades.Lung.Tests
{
    // P2-B06 route-cost read-out tests (Master Plan §10.2.3 Aids "Route cost read-out"). Each test names the criterion it checks.
    public class RouteCostTests
    {
        static string DirAt(double deg) =>
            "[" + Math.Sin(deg * Math.PI / 180).ToString("R", CultureInfo.InvariantCulture) + ",0," +
            (-Math.Cos(deg * Math.PI / 180)).ToString("R", CultureInfo.InvariantCulture) + "]";

        static AirwayTree Synthetic()
        {
            string nodes = string.Join(",",
                TreeFixture.Node("", 0, 100, 9, "[0,0,0]", "[0,0,-1]"),
                TreeFixture.Node("L", 1, 50, 7, "[0,0,-100]", DirAt(30)),
                TreeFixture.Node("LR", 2, 30, 2.0, "[0,0,-150]", DirAt(76)),   // 46.0 degrees from L
                TreeFixture.Node("LL", 2, 20, 2.1, "[0,0,-150]", DirAt(74.9))); // 44.9 degrees from L
            return new AirwayTree(AirwayTreeFile.Parse(TreeFixture.Json(nodes, "\"LR\"", "\"\"")), Atlas.Kernel.TileFrame.Zero);
        }

        // N6: MP §10.2.3 read-out: insertion depth = sum of len_mm, sharp turns > 45 degrees (strict), narrowest lumen vs 3.5 / 4.2 / 4.0 mm.
        [Test]
        public void RouteCost_Synthetic()
        {
            var t = Synthetic();
            var lr = RouteCost.At(t, AirwayTile.Fork("LR"));
            Assert.AreEqual(180.0, lr.InsertionDepthMm, 1e-9);
            Assert.AreEqual(1, lr.SharpTurns);
            Assert.AreEqual(4.0, lr.NarrowestLumenMm, 1e-9);
            Assert.AreEqual(2, lr.Generation);
            Assert.IsTrue(lr.Fits(0)); Assert.IsFalse(lr.Fits(1)); Assert.IsTrue(lr.Fits(2));
            Assert.AreEqual(165.0, RouteCost.At(t, AirwayTile.Bend("LR")).InsertionDepthMm, 1e-9); // D-G4-10
            var ll = RouteCost.At(t, AirwayTile.Fork("LL"));
            Assert.AreEqual(0, ll.SharpTurns);
            Assert.AreEqual(4.2, ll.NarrowestLumenMm, 1e-9);
            Assert.IsTrue(ll.Fits(1));
            CollectionAssert.AreEqual(new[] { 3.5, 4.2, 4.0 }, RouteCost.ScopeOuterDiameterMm);
        }

        // N7: MP §10.2.3 "in the symmetric model the lumen falls below 4.2 mm at generation 7" (fixture-derived).
        [Test]
        public void RouteCost_Fixture_LumenBelow4_2AtGen7()
        {
            var tree = TreeFixture.Tree;
            int first = -1;
            for (int g = 0; g <= AirwayTree.WalkableGenerations; g++)
            {
                var c = RouteCost.At(tree, AirwayTile.Fork(new string('L', g)));
                if (!c.Fits(1)) { first = g; Assert.AreEqual(3.581, c.NarrowestLumenMm, 1e-6); Assert.IsTrue(c.Fits(0)); break; }
            }
            Assert.AreEqual(7, first);
            Assert.AreEqual(4.51, RouteCost.At(tree, AirwayTile.Fork("LLLLLL")).NarrowestLumenMm, 1e-6);
            Assert.IsTrue(RouteCost.At(tree, AirwayTile.Fork("LLLLLL")).Fits(1));
        }

        // N8: the read-out text is culture invariant and carries no device brand names (D-G4-11).
        [Test]
        public void RouteCost_Format_Invariant()
        {
            var old = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                string s = RouteCost.At(Synthetic(), AirwayTile.Fork("LR")).Format();
                StringAssert.Contains("Insertion depth 180 mm", s);
                StringAssert.Contains("4.0 mm", s);
                StringAssert.Contains("Generation 2", s);
                StringAssert.Contains("Sharp turns (>45°) 1", s);
                StringAssert.Contains("Scope 3.5 mm fits", s);
                StringAssert.Contains("4.2 mm too wide", s);
                StringAssert.Contains("4.0 mm fits", s);
            }
            finally { Thread.CurrentThread.CurrentCulture = old; }
        }
    }
}
