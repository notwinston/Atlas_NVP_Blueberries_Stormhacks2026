using System;
using System.Collections.Generic;
using System.Linq;
using Atlas.Blades.FieldFit.BlindSpotFinder;
using NUnit.Framework;
using UnityEngine;

namespace Atlas.Blades.FieldFit.BlindSpotTests
{
    public class BlindSpotGridTests
    {
        [Test]
        public void SplitMix64_Golden()
        {
            // checks: Brief §3.3 PRNG golden values
            var r0 = new SplitMix64(0);
            Assert.AreEqual(0xE220A8397B1DCDAFUL, r0.NextULong());
            Assert.AreEqual(0x6E789E6AA1B965F4UL, r0.NextULong());
            Assert.AreEqual(0x06C45D188009454FUL, r0.NextULong());
            var r1 = new SplitMix64(1);
            Assert.AreEqual(0x910A2DEC89025CC1UL, r1.NextULong());
            Assert.AreEqual(0xBEEB8DA1658EEC67UL, r1.NextULong());
        }

        [Test]
        public void Grid_Count()
        {
            // checks: beat 1 grid, 8-22 deg temporal and +-8 deg vertical (MP §10.4.4), 72 points (D1)
            var g = BlindSpotGrid.Build(EyeSide.Right);
            Assert.AreEqual(72, g.Count);
            Assert.AreEqual(72, g.Select(p => (p.TemporalDeg, p.ElDeg)).Distinct().Count());
            for (int t = 8; t <= 22; t += 2) Assert.AreEqual(9, g.Count(p => p.TemporalDeg == t));
            for (int e = -8; e <= 8; e += 2) Assert.AreEqual(8, g.Count(p => p.ElDeg == e));
        }

        [Test]
        public void Grid_TemporalSign()
        {
            // checks: temporal side: right eye +az, left eye -az (Brief §3.1)
            foreach (var p in BlindSpotGrid.Build(EyeSide.Right)) Assert.AreEqual(p.TemporalDeg * Math.PI / 180, p.AzRad, 1e-12);
            foreach (var p in BlindSpotGrid.Build(EyeSide.Left)) Assert.AreEqual(-p.TemporalDeg * Math.PI / 180, p.AzRad, 1e-12);
        }

        static HashSet<(double, double)> Band()
        {
            var s = new HashSet<(double, double)>();
            foreach (int t in new[] { 12, 14, 16, 18 }) foreach (int e in new[] { -4, -2, 0, 2, 4 }) s.Add((t, e));
            s.Add((10, 0)); s.Add((20, 0));
            return s;
        }

        [Test]
        public void Grid_PriorityBand()
        {
            // checks: priority band of 22 points (D1)
            var got = new HashSet<(double, double)>(BlindSpotGrid.Build(EyeSide.Right).Where(p => BlindSpotGrid.InPriorityBand(p)).Select(p => (p.TemporalDeg, p.ElDeg)));
            Assert.AreEqual(22, got.Count);
            Assert.IsTrue(got.SetEquals(Band()));
        }

        static string Head(List<GridPoint> o, int n) => string.Join(" ", o.Take(n).Select(p => $"({p.TemporalDeg:0},{p.ElDeg:0})"));

        [Test]
        public void Grid_Order()
        {
            // checks: band-first deterministic presentation order (Brief §3.3, D1)
            foreach (uint seed in new uint[] { 0, 1, 2 })
            {
                var o = BlindSpotGrid.PresentationOrder(EyeSide.Right, seed);
                Assert.AreEqual(72, o.Count);
                Assert.AreEqual(72, o.Select(p => (p.TemporalDeg, p.ElDeg)).Distinct().Count());
                Assert.IsTrue(new HashSet<(double, double)>(o.Take(22).Select(p => (p.TemporalDeg, p.ElDeg))).SetEquals(Band()));
            }
            var a = BlindSpotGrid.PresentationOrder(EyeSide.Right, 0);
            var b = BlindSpotGrid.PresentationOrder(EyeSide.Right, 0);
            for (int i = 0; i < 72; i++) Assert.AreEqual(a[i].TemporalDeg * 100 + a[i].ElDeg, b[i].TemporalDeg * 100 + b[i].ElDeg);
            Assert.AreNotEqual(Head(a, 72), Head(BlindSpotGrid.PresentationOrder(EyeSide.Right, 1), 72));
            Assert.AreEqual("(16,2) (18,0) (18,-4) (16,-2) (12,-2)", Head(a, 5));
            Assert.AreEqual("(14,-4) (18,2) (18,4) (12,-2) (18,0)", Head(BlindSpotGrid.PresentationOrder(EyeSide.Right, 1), 5));
        }

        [Test]
        public void Direction_Axes()
        {
            // checks: lit-eye frame +x right, +y up, +z forward; Direction formula (Brief §3.1)
            Assert.AreEqual(0, (BlindSpotGrid.Direction(0, 0) - new Vector3(0, 0, 1)).magnitude, 1e-6);
            Assert.AreEqual(0, (BlindSpotGrid.Direction(Math.PI / 2, 0) - new Vector3(1, 0, 0)).magnitude, 1e-6);
            Assert.AreEqual(0, (BlindSpotGrid.Direction(0, Math.PI / 2) - new Vector3(0, 1, 0)).magnitude, 1e-6);
            double az = 0.31, el = -0.17;
            double ang = Vector3.Angle(Vector3.forward, BlindSpotGrid.Direction(az, el)) * Math.PI / 180;
            Assert.AreEqual(Math.Acos(Math.Cos(az) * Math.Cos(el)), ang, 1e-5);
        }
    }
}
