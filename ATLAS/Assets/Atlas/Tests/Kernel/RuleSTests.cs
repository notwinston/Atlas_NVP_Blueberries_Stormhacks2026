using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Unity.Mathematics;
using BigInteger = System.Numerics.BigInteger;
using Random = System.Random;

namespace Atlas.Kernel.Tests
{
    // P2-B01 Rule S tests (Master Plan §10.2.3; kernel.md §4.8). Each test names the criterion it checks.
    public class RuleSTests
    {
        const int Seed = 20261003;
        const double Phi = 1.618033988749895;

        static string RandomAddress(Random r, int len)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < len; i++) sb.Append(r.Next(2) == 0 ? 'L' : 'R');
            return sb.ToString();
        }

        static TileId Id(long xu, long xv, long yu, long yv, long tu, long tv) => new TileId(xu, xv, yu, yv, tu, tv);

        // K1: MP A.5, R3 §D3 golden words (Xu, Xv, Yu, Yv, Tu, Tv).
        [Test]
        public void RuleS_Golden_FirstForks()
        {
            var z = TileFrame.Zero;
            Assert.AreEqual(Id(0, 0, 0, -2, 1, 2), RuleS.FrameOf("L", z).Tile);
            Assert.AreEqual(Id(0, 0, -1, 0, 0, 1), RuleS.BendFrameOf("L", z).Tile);
            Assert.AreEqual(Id(0, 0, 0, 2, 1, 2), RuleS.FrameOf("R", z).Tile);
            Assert.AreEqual(Id(0, 0, 1, 0, 0, 1), RuleS.BendFrameOf("R", z).Tile);
            Assert.AreEqual(Id(0, -2, -4, -6, 5, 8), RuleS.FrameOf("LR", z).Tile);
            Assert.AreEqual(Id(-1, 0, -2, -2, 2, 3), RuleS.BendFrameOf("LR", z).Tile);
            Assert.AreEqual(Id(0, -2, 4, 6, 5, 8), RuleS.FrameOf("RL", z).Tile);
            Assert.AreEqual(Id(0, 2, -4, -6, 5, 8), RuleS.FrameOf("LL", z).Tile);
            Assert.AreEqual(Id(372, 602, 1272, 2058, 1685, 2728), RuleS.FrameOf("RRRLLR", z).Tile);
            Assert.AreEqual(Id(137, 220, 466, 754, 618, 999), RuleS.BendFrameOf("RRRLLR", z).Tile);
            Assert.IsTrue(RuleS.FrameOf("", z).Equals(TileFrame.Zero));
            // the bend of "L" is the tile across edge 3 of tile zero (kernel.md §5.3)
            Assert.AreEqual(TileId.Zero.Step(3), RuleS.BendFrameOf("L", z).Tile);
        }

        // K2: MP §10.2.3 "fork spacing 2.1226 units".
        [Test]
        public void RuleS_ForkSpacing_2_1226()
        {
            var r = new Random(Seed);
            double want = Math.Acosh(1 + 2 * Phi);
            Assert.AreEqual(2.1226, Math.Round(want, 4));
            Assert.AreEqual(want, RuleS.ForkSpacing, 1e-6);
            for (int i = 0; i < 200; i++)
            {
                var a = RandomAddress(r, r.Next(0, 7));
                char c = r.Next(2) == 0 ? 'L' : 'R';
                var f0 = RuleS.FrameOf(a, TileFrame.Zero); var f1 = RuleS.FrameOf(a + c, TileFrame.Zero);
                var o = Geometry2.BasePoint;
                var p0 = math.mul(f0.ToIso2().M, o); var p1 = math.mul(f1.ToIso2().M, o);
                Assert.AreEqual(want, Geometry2.Distance(p0, p1, -1), 1e-6, a + c); // 1e-9 is below double precision of absolute positions at depth 7 (measured error 8e-9)
            }
            // relative T of the child fork seen from Zero: Tu = 1, Tv = 2
            var child = RuleS.FrameOf("L", TileFrame.Zero).Tile;
            Assert.AreEqual(1, child.Tu); Assert.AreEqual(2, child.Tv);
        }

        // K3: MP §10.2.3 / A.5 "long tile ids suffice to 23 generations" (53 bits at the worst path).
        [Test]
        public void RuleS_Depth23_ExactInLong()
        {
            var r = new Random(Seed);
            var addrs = new List<string> { "LRLRLRLRLRLRLRLRLRLRLRL", "RLRLRLRLRLRLRLRLRLRLRLR" };
            for (int i = 0; i < 2000; i++) addrs.Add(RandomAddress(r, 23));
            int maxBitsRandom = 0;
            foreach (var a in addrs)
            {
                var t = RuleS.FrameOf(a, TileFrame.Zero).Tile;
                // phi-arithmetic in BigInteger pairs: (u + v phi)
                var X = (new BigInteger(t.Xu), new BigInteger(t.Xv)); var Y = (new BigInteger(t.Yu), new BigInteger(t.Yv));
                var T = (new BigInteger(t.Tu), new BigInteger(t.Tv));
                var xx = Mul(X, X); var yy = Mul(Y, Y); var tq = Mul(T, T);
                var s = Mul((BigInteger.Zero, BigInteger.One), (xx.Item1 + yy.Item1, xx.Item2 + yy.Item2)); // phi (X^2 + Y^2)
                Assert.AreEqual(BigInteger.MinusOne, s.Item1 - tq.Item1, a);
                Assert.AreEqual(BigInteger.Zero, s.Item2 - tq.Item2, a);
                if (a != addrs[0] && a != addrs[1]) maxBitsRandom = Math.Max(maxBitsRandom, Bits(t));
            }
            Assert.AreEqual(53, Bits(RuleS.FrameOf(addrs[0], TileFrame.Zero).Tile));
            UnityEngine.Debug.Log($"K3 random max bit length = {maxBitsRandom}; alternating path = {Bits(RuleS.FrameOf(addrs[0], TileFrame.Zero).Tile)}");
        }

        static (BigInteger, BigInteger) Mul((BigInteger, BigInteger) a, (BigInteger, BigInteger) b) =>
            (a.Item1 * b.Item1 + a.Item2 * b.Item2, a.Item1 * b.Item2 + a.Item2 * b.Item1 + a.Item2 * b.Item2);

        static int Bits(TileId t)
        {
            int m = 0;
            foreach (var w in new[] { t.Xu, t.Xv, t.Yu, t.Yv, t.Tu, t.Tv })
                { ulong u = (ulong)Math.Abs(w); int b = 0; while (u != 0) { b++; u >>= 1; } m = Math.Max(m, b); }
            return m;
        }

        // K4: R3 §E1 round trip: down then up gives the trachea exactly.
        [Test]
        public void RuleS_RoundTrip()
        {
            var r = new Random(Seed);
            for (int i = 0; i < 500; i++)
            {
                var a = RandomAddress(r, r.Next(0, 24));
                var m = TileFrame.Zero;
                foreach (char c in a) m = RuleS.BendToFork(RuleS.ChildBend(m, c));
                Assert.IsTrue(m.Equals(RuleS.FrameOf(a, TileFrame.Zero)));
                for (int k = a.Length - 1; k >= 0; k--)
                {
                    var bend = RuleS.ForkToBend(m);
                    Assert.IsTrue(bend.Equals(RuleS.BendFrameOf(a.Substring(0, k + 1), TileFrame.Zero)), a);
                    m = RuleS.BendToParentFork(bend, a[k]);
                }
                Assert.IsTrue(m.Equals(TileFrame.Zero), a);
            }
        }

        // K5: kernel.md §5.3: TileFrame.Step agrees with the TileGraph through CanonicalEdge.
        [Test]
        public void TileFrame_MatchesTileGraph()
        {
            var r = new Random(Seed);
            var g = new TileGraph(5);
            var f = TileFrame.Zero;
            for (int i = 0; i < 2000; i++)
            {
                int e = r.Next(4);
                var n = f.Step(e);
                Assert.AreEqual(g.Neighbour(f.Tile, f.CanonicalEdge(e)), n.Tile, "step " + i);
                Assert.That(n.Spin, Is.InRange(0, 3));
                Assert.IsTrue(f.Rotate(4).Equals(f));
                f = n;
                if (i % 50 == 49) f = TileFrame.Zero; // keep the 64-bit range (A.5)
            }
            for (int n = -5; n <= 5; n++) Assert.AreEqual(((n % 4) + 4) % 4, TileFrame.Zero.Rotate(n).Spin, "n=" + n);
        }

        // K6: G10 checklist "16 generations addressable, no overlap" (supporting): 262,141 distinct tile ids.
        [Test]
        public void RuleS_NoOverlap_Depth16()
        {
            var seen = new HashSet<TileId>();
            var level = new List<TileFrame> { TileFrame.Zero };
            seen.Add(TileFrame.Zero.Tile);
            int forks = 1, bends = 0;
            for (int d = 0; d < 16; d++)
            {
                var next = new List<TileFrame>(level.Count * 2);
                foreach (var fk in level)
                    foreach (char c in "LR")
                    {
                        var b = RuleS.ChildBend(fk, c); var ch = RuleS.BendToFork(b);
                        Assert.IsTrue(seen.Add(b.Tile)); Assert.IsTrue(seen.Add(ch.Tile));
                        bends++; forks++; next.Add(ch);
                    }
                level = next;
            }
            Assert.AreEqual(131071, forks); Assert.AreEqual(131070, bends); Assert.AreEqual(262141, seen.Count);
        }

        // K7: invalid addresses are rejected.
        [Test]
        public void RuleS_InvalidAddress_Throws()
        {
            Assert.Throws<ArgumentException>(() => RuleS.FrameOf("LX", TileFrame.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => RuleS.FrameOf(new string('L', 24), TileFrame.Zero));
            Assert.Throws<ArgumentNullException>(() => RuleS.FrameOf(null, TileFrame.Zero));
        }
    }
}
