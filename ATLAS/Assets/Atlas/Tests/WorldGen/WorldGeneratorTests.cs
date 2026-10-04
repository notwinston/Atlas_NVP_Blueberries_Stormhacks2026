using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Atlas.Kernel;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.WorldGen.Tests
{
    public class WorldGeneratorTests
    {
        public static readonly uint[] Seeds = { 1, 42, 20261003 };
        public static RoomFootprint R45 => RoomFootprint.Rectangle(4, 5, 3.0 / 1.0613);
        public static RoomFootprint R33 => RoomFootprint.Rectangle(3, 3, 2.0 / 1.0613);
        const double Phi = 1.618033988749895;

        public static string GoldenPath(string name) => Path.Combine(Application.dataPath, "Atlas/Tests/WorldGen/Golden", name);

        static IEnumerable<string> GoldenLines(string name)
        {
            foreach (var line in File.ReadAllLines(GoldenPath(name)))
                if (line.Length > 0 && !line.StartsWith("#")) yield return line;
        }

        static double Cosh(TileGraph g, TileId id)
        {
            var l = new List<VisibleTile>();
            g.VisibleSet(TileId.Zero, 4.0, l);
            foreach (var v in l) if (v.Id == id) return v.F.M.c2.z;
            throw new InvalidOperationException("tile not within 4.0");
        }

        // §3.4 region and tie sentences
        [Test]
        public void Region_Rules()
        {
            var g = new WorldGenerator(1, R45);
            Assert.AreEqual(0, Regions.RegionOf(TileId.Zero));
            for (int k = 0; k < 4; k++) Assert.AreEqual(k, Regions.RegionOf(TileId.Zero.Step(k)), "edge " + k);
            // X == -Y > 0: ties edges 3 and 0 -> 0
            bool sawTie = false; var seen = new bool[4];
            foreach (var t in g.Ball(6))
            {
                int r = Regions.RegionOf(t);
                seen[r] = true;
                double x = t.Xu + t.Xv * Phi, y = t.Yu + t.Yv * Phi;
                if (t.Xu == -t.Yu && t.Xv == -t.Yv && x > 0) { sawTie = true; Assert.AreEqual(0, r, "X == -Y > 0"); }
                if (t.Xu == t.Yu && t.Xv == t.Yv && x > 0) Assert.AreEqual(0, r, "X == Y > 0 ties edges 0 and 1");
                if (Math.Abs(Math.Abs(x) - Math.Abs(y)) < 1e-9) continue;
                double[] v = { x, y, -x, -y }; int am = 0;
                for (int i = 1; i < 4; i++) if (v[i] > v[am]) am = i;
                Assert.AreEqual(am, r, t.ToString());
            }
            Assert.IsTrue(sawTie, "no X == -Y > 0 tile in the ball");
            Assert.IsTrue(seen[0] && seen[1] && seen[2] && seen[3], "all four regions occur");
        }

        // §9.5.6 "same region for every tile to depth 6 on two runs"; §11.1 "597"
        [Test]
        public void Region_Depth6_TwoRuns()
        {
            var a = new WorldGenerator(1, R45); var b = new WorldGenerator(1, R45);
            var ba = a.Ball(6);
            Assert.AreEqual(597, ba.Count);
            var rb = new Dictionary<TileId, int>();
            var bb = b.Ball(6);
            for (int i = bb.Count - 1; i >= 0; i--) rb[bb[i]] = b.Content(bb[i]).Region;
            int equal = 0;
            foreach (var t in ba) if (a.Content(t).Region == rb[t]) equal++;
            Assert.AreEqual(597, equal);
            var gold = new Dictionary<TileId, int>();
            foreach (var line in GoldenLines("Region_Depth6_golden.csv"))
            {
                var f = line.Split(',');
                var id = new TileId(long.Parse(f[0]), long.Parse(f[1]), long.Parse(f[2]), long.Parse(f[3]), long.Parse(f[4]), long.Parse(f[5]));
                gold[id] = int.Parse(f[6]);
            }
            Assert.AreEqual(597, gold.Count);
            foreach (var t in ba) Assert.AreEqual(gold[t], Regions.RegionOf(t), t.ToString());
        }

        // §3.4 Home; §4.6
        [Test]
        public void Home_Footprint([ValueSource(nameof(Rooms))] RoomFootprint room, [ValueSource(nameof(Seeds))] uint seed)
        {
            var g = new WorldGenerator(seed, room);
            Assert.AreEqual(13, g.HomeTiles.Count);
            int d0 = 0, d1 = 0, d2 = 0;
            foreach (var t in g.HomeTiles)
            {
                double w = Cosh(g.Graph, t);
                if (t == TileId.Zero) d0++;
                else if (Math.Abs(w - Phi) < 1e-9) d1++;
                else if (Math.Abs(w - Phi * Phi) < 1e-9) d2++;
                else Assert.Fail("unexpected Home tile " + t + " cosh " + w);
                var c = g.Content(t);
                Assert.AreEqual(Archetype.Home, c.Archetype); Assert.AreEqual(0, c.KerbMask); Assert.AreEqual(-1, c.FeatureEdge);
                Assert.AreNotEqual(Archetype.Tower, c.Archetype);
            }
            Assert.AreEqual((1, 4, 8), (d0, d1, d2));
            var n = new WorldGenerator(seed, null);
            Assert.AreEqual(1, n.HomeTiles.Count); Assert.AreEqual(TileId.Zero, n.HomeTiles[0]);
        }

        public static IEnumerable<RoomFootprint> Rooms() { yield return R45; yield return R33; }

        // §3.4 Tower
        [Test]
        public void TowerLattice_CoversAndClasses()
        {
            var g = new WorldGenerator(1, R45);
            var keys = new HashSet<int>();
            foreach (var t in g.Ball(3)) keys.Add(TowerLattice.ClassOf(t));
            Assert.AreEqual(15, keys.Count);
            Assert.IsFalse(keys.Contains(-1));
            Assert.AreEqual(new int3(0, 0, 1), TowerLattice.Key(TileId.Zero));
            Assert.AreEqual(11, new WorldGenerator(1, R45).TowerClass);
            Assert.AreEqual(12, new WorldGenerator(42, R45).TowerClass);
            Assert.AreEqual(11, new WorldGenerator(20261003, R45).TowerClass);
            double worst = 0; var list = new List<VisibleTile>();
            foreach (var t in g.Ball(6))
            {
                g.Graph.VisibleSet(t, 2.5, list);
                double best = double.MaxValue;
                foreach (var v in list) if (g.Content(v.Id).Archetype == Archetype.Tower) best = Math.Min(best, v.F.M.c2.z);
                worst = Math.Max(worst, best);
            }
            double target = 1 + 3 * Phi;
            TestContext.WriteLine($"worst nearest-tower cosh = {worst:R}, target {target:R}");
            Assert.AreEqual(target, worst, 1e-9 * target);
        }

        // §3.4 "pure function of the tile id"
        [Test]
        public void Content_PureAndOrderFree()
        {
            var a = new WorldGenerator(42, R45); var b = new WorldGenerator(42, R45);
            var ball = a.Ball(6);
            var ca = new List<TileContent>();
            foreach (var t in ball) ca.Add(a.Content(t));
            for (int i = ball.Count - 1; i >= 0; i--) Assert.AreEqual(ca[i], b.Content(ball[i]), ball[i].ToString());
        }

        [Test]
        public void Parent_And_Kerbs()
        {
            foreach (uint seed in Seeds)
            {
                var g = new WorldGenerator(seed, R45);
                int kerbed = 0;
                foreach (var t in g.Ball(6))
                {
                    var c = g.Content(t);
                    if (t != TileId.Zero)
                    {
                        var p = g.Parent(t);
                        Assert.Less(p.Tu + p.Tv * Phi, t.Tu + t.Tv * Phi, "T decreases");
                        Assert.AreEqual(0, c.ClosedMask & (1 << c.ParentEdge), "tree edge open");
                    }
                    else Assert.AreEqual(-1, c.ParentEdge);
                    if (c.Archetype != Archetype.Corridor) continue;
                    for (int e = 0; e < 4; e++)
                    {
                        if ((c.KerbMask & (1 << e)) == 0) continue;
                        kerbed++;
                        Assert.AreEqual((int)((c.PropSeed >> 8) & 1), e & 1, "kerb only on corridor axis edges");
                        var n = g.Graph.Neighbour(t, e);
                        Assert.AreEqual(0, g.Content(n).KerbMask & (1 << g.BackEdge(t, e)), "doubled kerb");
                    }
                }
                TestContext.WriteLine($"seed {seed}: {kerbed} kerbs on corridors");
                Assert.Greater(kerbed, 0);
            }
        }

        // §3.5; §10.1.4; D3
        [Test]
        public void StartSpan_NearStart()
        {
            foreach (uint seed in Seeds)
            {
                var g = new WorldGenerator(seed, R45);
                Assert.IsTrue(g.TryGetStartSpan(out var a, out var b));
                double wa = Cosh(g.Graph, a), wb = Cosh(g.Graph, b);
                Assert.AreEqual(Phi * Phi * Phi, wa, 1e-9);
                Assert.AreEqual(Math.Pow(Phi, 4), wb, 1e-9);
                double mid = Math.Acosh((a.Tu + a.Tv * Phi + b.Tu + b.Tv * Phi) / (2 * Math.Cosh(g.Graph.InRadius)));
                Assert.AreEqual(2.2606, mid, 1e-4);
                var ca = g.Content(a); var cb = g.Content(b);
                Assert.AreEqual(Archetype.SpanA, ca.Archetype); Assert.AreEqual(Archetype.SpanB, cb.Archetype);
                Assert.AreEqual(b, g.Graph.Neighbour(a, ca.FeatureEdge));
                Assert.AreEqual(a, g.Graph.Neighbour(b, cb.FeatureEdge));
            }
        }

        // §9.5.6 "same seed -> same world hash in two Editor runs"
        [Test]
        public void WorldHash_SameSeedTwice()
        {
            var golden = new Dictionary<uint, ulong>();
            foreach (var line in GoldenLines("WorldHash_golden.csv"))
            {
                var f = line.Split(',');
                if (f[1] == "R45") golden[uint.Parse(f[0])] = ulong.Parse(f[2].Substring(2), NumberStyles.HexNumber);
            }
            var seen = new HashSet<ulong>();
            foreach (uint seed in Seeds)
            {
                ulong h1 = new WorldGenerator(seed, R45).WorldHash(6), h2 = new WorldGenerator(seed, R45).WorldHash(6);
                TestContext.WriteLine($"seed {seed} R45 hash 0x{h1:X16}");
                Assert.AreEqual(h1, h2);
                Assert.IsTrue(seen.Add(h1), "different seeds must differ");
                Assert.AreEqual(golden[seed], h1, "golden");
            }
        }

        [Test]
        public void ZPhi_ExactSign()
        {
            Assert.AreEqual(0, ZPhiMath.Sign(0, 0));
            Assert.AreEqual(1, ZPhiMath.Sign(2, -1));   // 2 - phi > 0
            Assert.AreEqual(-1, ZPhiMath.Sign(1, -1));  // 1 - phi < 0
            Assert.AreEqual(1, ZPhiMath.Sign(-1, 1));   // phi - 1 > 0
            Assert.AreEqual(1, ZPhiMath.Sign(-3, 2));   // 2phi - 3 = 0.236
            Assert.AreEqual(-1, ZPhiMath.Compare(1, 0, 0, 1));
            long f92 = 7540113804746346429L, f91 = 4660046610375530309L; // F92 - F91 phi = (-1/phi)^91 < 0, far below double precision
            Assert.AreEqual(-1, ZPhiMath.Sign(f92, -f91));
            Assert.AreEqual(1, ZPhiMath.Sign(-f92, f91));
            Assert.AreEqual(-1, ZPhiMath.Compare(f92, 0, 0, f91));
        }
    }
}
