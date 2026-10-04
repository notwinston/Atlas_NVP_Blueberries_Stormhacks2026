using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using BigInteger = System.Numerics.BigInteger;
using Random = System.Random;

namespace Atlas.Kernel.Tests
{
    public class TileIdTests
    {
        const int Seed = 20261003;
        static readonly long[] A377322 = { 1, 4, 12, 28, 64, 148, 340, 780, 1792, 4116, 9452 };
        static readonly Iso2 Id1 = new Iso2(double3x3.identity, -1);

        // ---------- helpers ----------
        static List<List<TileId>> Levels(Func<TileId, int, TileId> nb, int depth)
        {
            var seen = new HashSet<TileId> { TileId.Zero };
            var levels = new List<List<TileId>> { new List<TileId> { TileId.Zero } };
            for (int d = 1; d <= depth; d++)
            {
                var next = new List<TileId>();
                foreach (var t in levels[d - 1])
                    for (int k = 0; k < 4; k++) { var n = nb(t, k); if (seen.Add(n)) next.Add(n); }
                levels.Add(next);
            }
            return levels;
        }

        static List<TileId> Discover(TileGraph g, int count)
        {
            var list = new List<TileId> { TileId.Zero };
            var seen = new HashSet<TileId> { TileId.Zero };
            for (int i = 0; i < list.Count && list.Count < count; i++)
                for (int k = 0; k < 4 && list.Count < count; k++) { var n = g.Neighbour(list[i], k); if (seen.Add(n)) list.Add(n); }
            return list;
        }

        static int Back(Func<TileId, int, TileId> nb, TileId t, TileId n)
        {
            int b = -1;
            for (int k = 0; k < 4; k++) if (nb(n, k) == t) { Assert.AreEqual(-1, b, "two back edges"); b = k; }
            Assert.GreaterOrEqual(b, 0, "no back edge");
            return b;
        }

        // vertex walk: from t exit through e, next exit = back + 1; returns tiles and exit edges for n steps
        static (List<TileId> tiles, List<int> exits) VertexWalk(Func<TileId, int, TileId> nb, TileId start, int e, int steps)
        {
            var tiles = new List<TileId> { start }; var exits = new List<int> { e };
            var t = start;
            for (int s = 0; s < steps; s++)
            {
                var n = nb(t, e); int b = Back(nb, t, n);
                t = n; e = (b + 1) % 4; tiles.Add(t); exits.Add(e);
            }
            return (tiles, exits);
        }

        static TileId Straight(Func<TileId, int, TileId> nb, int steps)
        {
            var t = TileId.Zero; var n = nb(t, 0); int b = Back(nb, t, n); t = n;
            for (int s = 1; s < steps; s++) { int e = (b + 2) % 4; n = nb(t, e); b = Back(nb, t, n); t = n; }
            return t;
        }

        static (BigInteger a, BigInteger b) Mul((BigInteger a, BigInteger b) x, (BigInteger a, BigInteger b) y) =>
            (x.a * y.a + x.b * y.b, x.a * y.b + x.b * y.a + x.b * y.b);

        static bool RingOk(TileId t)
        {
            var x = (new BigInteger(t.Xu), new BigInteger(t.Xv));
            var y = (new BigInteger(t.Yu), new BigInteger(t.Yv));
            var tt = (new BigInteger(t.Tu), new BigInteger(t.Tv));
            var xx = Mul(x, x); var yy = Mul(y, y);
            var s = (xx.Item1 + yy.Item1, xx.Item2 + yy.Item2);
            var phiS = (s.Item2, s.Item1 + s.Item2);
            var t2 = Mul(tt, tt);
            return phiS.Item1 - t2.Item1 == -1 && phiS.Item2 - t2.Item2 == 0;
        }

        static bool Ring6Ok(TileId t)
        {
            BigInteger x = t.Xu, y = t.Yu, w = t.Tu;
            return 3 * (x * x + y * y) - w * w == -1 && t.Xv == 0 && t.Yv == 0 && t.Tv == 0;
        }

        static double Fro(double3x3 a) => math.sqrt(math.dot(a.c0, a.c0) + math.dot(a.c1, a.c1) + math.dot(a.c2, a.c2));
        static readonly double3x3 Eta = new double3x3(1, 0, 0, 0, 1, 0, 0, 0, -1);
        static double LorentzErr(double3x3 m) => Fro(math.mul(math.transpose(m), math.mul(Eta, m)) - Eta);
        static double2 Dir(int e) => new double2(Math.Cos(e * Math.PI / 2), Math.Sin(e * Math.PI / 2));
        static double Median(List<double> v) { var s = new List<double>(v); s.Sort(); return s[s.Count / 2]; }

        // ---------- T1 ----------
        // Row "per-depth counts equal OEIS A377322 to depth 10"; §11.1 TileId_Growth; R3 T7 [V].
        [Test]
        public void TileId_Growth()
        {
            var g = new TileGraph(5);
            var lv = Levels(g.Neighbour, 10);
            var counts = new long[11]; long total = 0;
            for (int i = 0; i <= 10; i++) { counts[i] = lv[i].Count; total += counts[i]; }
            TestContext.WriteLine("exact BFS counts: " + string.Join(", ", counts) + " total " + total);
            Assert.AreEqual(A377322, counts);
            Assert.AreEqual(16737, total);

            // (a) recurrence of the g.f. (1+2x+4x^2+2x^3+x^4)/(1-2x-2x^3+x^4)
            var a = new long[11]; for (int i = 0; i < 5; i++) a[i] = A377322[i];
            for (int n = 5; n <= 10; n++) a[n] = 2 * a[n - 1] + 2 * a[n - 3] - a[n - 4];
            TestContext.WriteLine("recurrence: " + string.Join(", ", a));
            Assert.AreEqual(A377322, a);
            Assert.AreEqual(4116, a[9]); Assert.AreEqual(9452, a[10]);

            // (b) float-only BFS, no Z[phi]
            double r = Math.Acosh(Math.Cos(Math.PI / 5) / Math.Sin(Math.PI / 4));
            var S = new Iso2[4];
            for (int k = 0; k < 4; k++) S[k] = Iso2.Rot(k * Math.PI / 2) * Iso2.Boost(new double2(2 * r, 0), -1) * Iso2.Rot(Math.PI);
            Func<Iso2, (long, long)> key = f => ((long)Math.Round(f.M.c2.x * 1e4), (long)Math.Round(f.M.c2.y * 1e4));
            var seen = new HashSet<(long, long)> { key(Id1) };
            var cur = new List<Iso2> { Id1 };
            var fc = new long[11]; fc[0] = 1;
            for (int d = 1; d <= 10; d++)
            {
                var nxt = new List<Iso2>();
                foreach (var f in cur)
                    for (int k = 0; k < 4; k++) { var n = f * S[k]; if (seen.Add(key(n))) nxt.Add(n); }
                fc[d] = nxt.Count; cur = nxt;
            }
            TestContext.WriteLine("float BFS counts: " + string.Join(", ", fc));
            Assert.AreEqual(A377322, fc);
        }

        // ---------- T2 ----------
        // Row "a walk round a vertex closes after exactly 5 steps"; §11.1 "not 4; still exact 35 tiles from home".
        [Test]
        public void TileId_VertexClosure()
        {
            var m = XFrame.Identity;
            for (int i = 1; i <= 5; i++)
            {
                m = m.Step(1);
                if (i < 5) Assert.IsFalse(m.Equals(XFrame.Identity), "closed after " + i);
            }
            Assert.IsTrue(m.Equals(XFrame.Identity));

            var g = new TileGraph(5);
            Func<TileId, int, TileId> viaStep = (t, k) => t.Step(k);
            var starts = new List<TileId> { TileId.Zero };
            starts.Add(Straight(g.Neighbour, 35));
            foreach (var start in starts)
                for (int e = 0; e < 4; e++)
                {
                    var (tg, eg) = VertexWalk(g.Neighbour, start, e, 5);
                    var (ts, es) = VertexWalk(viaStep, start, e, 5);
                    Assert.AreEqual(tg, ts); Assert.AreEqual(eg, es);
                    Assert.AreEqual(start, tg[5]); Assert.AreEqual(e, eg[5]);
                    var set = new HashSet<TileId>();
                    for (int i = 1; i <= 4; i++) { Assert.AreNotEqual(start, tg[i]); set.Add(tg[i]); }
                    Assert.AreEqual(4, set.Count);
                }
        }

        // ---------- T3 ----------
        // Row "phi(X^2+Y^2) - T^2 = -1 for every generated tile (Int128)"; §11.1 "to depth 12, BigInteger".
        // BigInteger stands in for Int128 (not in Unity's profile, kernel.md §5.6).
        [Test]
        public void TileId_RingInvariant()
        {
            var g = new TileGraph(5);
            var lv = Levels(g.Neighbour, 12);
            int n = 0;
            foreach (var l in lv) foreach (var t in l) { n++; Assert.IsTrue(RingOk(t), t.ToString()); }
            TestContext.WriteLine("tiles checked: " + n);
            Assert.AreEqual(88301, n);
        }

        // ---------- T4 ----------
        // Row "13/45/81/129 and 105 within 3.8425"; §11.1 VisibleSet_Counts (counts half); A.3.
        [Test]
        public void VisibleSet_Counts()
        {
            var g = new TileGraph(5);
            var lv = Levels(g.Neighbour, 10);
            var centres = new List<TileId> { TileId.Zero, lv[7][123], Straight(g.Neighbour, 20) };
            double[] rhos = { 2, 3, 3.5, 4, 3.8425, 2.5, 0 };
            int[] want = { 13, 45, 81, 129, 105, 25, 1 };
            var list = new List<VisibleTile>(1024);
            foreach (var c in centres)
            {
                // unpruned BFS from c with chained doubles
                var dist = new Dictionary<TileId, (int depth, double w)>();
                var frames = new Dictionary<TileId, double3x3> { { c, double3x3.identity } };
                dist[c] = (0, 1);
                var q = new Queue<TileId>(); q.Enqueue(c);
                while (q.Count > 0)
                {
                    var t = q.Dequeue(); var dt = dist[t].depth; if (dt >= 10) continue;
                    for (int k = 0; k < 4; k++)
                    {
                        var n = g.Neighbour(t, k);
                        if (dist.ContainsKey(n)) continue;
                        var f = math.mul(frames[t], g.StepMatrix(t, k).M);
                        frames[n] = f; dist[n] = (dt + 1, f.c2.z); q.Enqueue(n);
                    }
                }
                for (int i = 0; i < rhos.Length; i++)
                {
                    g.VisibleSet(c, rhos[i], list);
                    TestContext.WriteLine($"centre {c} rho {rhos[i]}: {list.Count}");
                    Assert.AreEqual(want[i], list.Count, $"rho {rhos[i]}");
                    double ch = Math.Cosh(rhos[i]);
                    Assert.AreEqual(c, list[0].Id); Assert.AreEqual(0, list[0].GraphDistance);
                    Assert.Less(Fro(list[0].F.M - double3x3.identity), 1e-12);
                    int prev = 0, brute = 0;
                    foreach (var kv in dist) if (kv.Value.w <= ch + 1e-12) brute++;
                    Assert.AreEqual(list.Count, brute, "brute force");
                    foreach (var e in list)
                    {
                        Assert.LessOrEqual(e.F.M.c2.z, ch + 1e-12);
                        Assert.Less(LorentzErr(e.F.M), 1e-12);
                        Assert.GreaterOrEqual(e.GraphDistance, prev); prev = e.GraphDistance;
                        Assert.AreEqual(dist[e.Id].depth, e.GraphDistance);
                        Assert.AreEqual(-1, e.F.Kappa);
                    }
                    if (c == TileId.Zero)
                    {
                        double phi = (1 + Math.Sqrt(5)) / 2, sp = Math.Sqrt(phi);
                        foreach (var e in list)
                        {
                            double X = e.Id.Xu + e.Id.Xv * phi, Y = e.Id.Yu + e.Id.Yv * phi, T = e.Id.Tu + e.Id.Tv * phi;
                            var p = e.F.M.c2;
                            Assert.Less(math.cmax(math.abs(p - new double3(sp * X, sp * Y, T))), 1e-12 * T);
                        }
                    }
                }
            }
        }

        // ---------- T5 ----------
        // Row "a crossing (re-root + rebuild at rho = 3 + 0.8425) costs <= 0.5 ms in the Editor".
        static List<double> CrossingTimes(TileGraph g, List<VisibleTile> list, double rho, int warm, int n,
            List<TileId> cycle, List<int> exits)
        {
            var times = new List<double>();
            var sw = new Stopwatch();
            for (int i = 0; i < warm + n; i++)
            {
                int j = i % 5;
                var pose = new WalkerPose { Tile = cycle[j], Local = Iso2.Boost(Dir(exits[j]) * (g.InRadius + 0.05), -1) };
                sw.Restart();
                bool moved = g.Reroot(ref pose);
                g.VisibleSet(pose.Tile, rho, list);
                sw.Stop();
                Assert.IsTrue(moved); Assert.AreEqual(cycle[(j + 1) % 5], pose.Tile);
                if (i >= warm) times.Add(sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency);
            }
            return times;
        }

        [Test]
        public void TileId_CrossingCost()
        {
            var g = new TileGraph(5); var list = new List<VisibleTile>(1024);
            var (cycle, exits) = VertexWalk(g.Neighbour, TileId.Zero, 1, 4);
            const double Rho = 3.0 + 0.8425;
            var times = CrossingTimes(g, list, Rho, 100, 100, cycle, exits);
            var s = new List<double>(times); s.Sort();
            double med = s[50], p95 = s[94], max = s[99];
            TestContext.WriteLine($"crossing rho={Rho}: median {med:F4} ms, p95 {p95:F4} ms, max {max:F4} ms; codeOptimization={UnityEditor.Compilation.CompilationPipeline.codeOptimization}; visible={list.Count}");
            var t2 = CrossingTimes(g, list, 3.0 + 2 * 0.8425, 100, 100, cycle, exits);
            TestContext.WriteLine($"info rho=4.685: median {Median(t2):F4} ms; visible={list.Count}");
            var fresh = new TileGraph(5); var tm = new List<double>(); var sw = new Stopwatch();
            var t = TileId.Zero; int b = 0;
            var pose = new WalkerPose { Tile = t, Local = Id1 };
            for (int i = 0; i < 20; i++)
            {
                int e = i == 0 ? 0 : (b + 2) % 4;
                pose.Local = Iso2.Boost(Dir(e) * (fresh.InRadius + 0.05), -1);
                sw.Restart(); fresh.Reroot(ref pose); fresh.VisibleSet(pose.Tile, Rho, list); sw.Stop();
                tm.Add(sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency);
                var n = fresh.Neighbour(t, e); b = Back(fresh.Neighbour, t, n); t = n;
                Assert.AreEqual(t, pose.Tile);
            }
            TestContext.WriteLine("cold straight crossings (ms): " + string.Join(" ", tm.ConvertAll(x => x.ToString("F3"))));
            Assert.LessOrEqual(med, 0.5);
        }

        // ---------- T6 ----------
        // §11.1 TileId_Unique: BFS, DFS and 10 random walks give one id set; Neighbour(Neighbour(t,k), back) == t.
        [Test]
        public void TileId_Unique()
        {
            var g = new TileGraph(5);
            var lv = Levels(g.Neighbour, 6);
            var depth = new Dictionary<TileId, int>();
            for (int d = 0; d < lv.Count; d++) foreach (var t in lv[d]) depth[t] = d;
            Assert.AreEqual(597, depth.Count);

            var gd = new TileGraph(5);
            var seen = new HashSet<TileId> { TileId.Zero };
            var st = new Stack<TileId>(); st.Push(TileId.Zero);
            while (st.Count > 0)
            {
                var t = st.Pop();
                for (int k = 0; k < 4; k++) { var n = gd.Neighbour(t, k); if (depth.TryGetValue(n, out var dn) && dn <= 6 && seen.Add(n)) st.Push(n); }
            }
            CollectionAssert.AreEquivalent(depth.Keys, seen);

            var graphs = new List<TileGraph> { gd };
            var rnd = new Random(Seed);
            for (int w = 0; w < 10; w++)
            {
                var gw = new TileGraph(5); var vis = new HashSet<TileId> { TileId.Zero }; var cur = TileId.Zero; long steps = 0;
                while (vis.Count < depth.Count && steps++ < 2000000)
                {
                    var n = gw.Neighbour(cur, rnd.Next(4));
                    if (depth.ContainsKey(n)) { cur = n; vis.Add(n); }
                }
                Assert.AreEqual(depth.Count, vis.Count, "walk " + w);
                graphs.Add(gw);
            }
            foreach (var t in depth.Keys)
                for (int k = 0; k < 4; k++)
                {
                    var n = g.Neighbour(t, k);
                    Back(g.Neighbour, t, n);
                    g.TryGetFrame(t, out var f0);
                    foreach (var gx in graphs)
                    {
                        Assert.AreEqual(n, gx.Neighbour(t, k));
                        Assert.IsTrue(gx.TryGetFrame(t, out var f1)); Assert.IsTrue(f0.Equals(f1));
                    }
                }
            for (int k = 0; k < 4; k++)
            {
                var m = XFrame.Identity.Step(k).Step(0); var r = XFrame.Identity;
                for (int i = 0; i < k; i++) r = r.Rot1();
                Assert.IsTrue(m.Equals(r), "S_k S_0 = R^k, k=" + k);
            }
        }

        // ---------- T7 ----------
        // Row "content seed = hash of the id"; §11.1 TileId_Seed (Editor/Mono half); kernel.md §5.7.
        // The IL2CPP half runs on the phone at G6 and is deferred.
        [Test]
        public void TileId_Seed()
        {
            var z = TileId.Zero;
            Assert.AreEqual(0x4194AB7A26ABAF68UL, z.ContentSeed(0));
            Assert.AreEqual(0x1339BA6A0D8B35C0UL, z.ContentSeed(12345));
            var g = new TileGraph(5);
            var e0 = z.Step(0); var e1 = z.Step(1);
            Assert.AreEqual(0xB27898E099B829B2UL, e0.ContentSeed(0));
            Assert.AreEqual(0x1A402E1FD39B2A54UL, e0.ContentSeed(12345));
            Assert.AreEqual(0x486B6F16E8179850UL, e1.ContentSeed(0));
            Assert.AreEqual(0x7EE67468759C7B41UL, e1.ContentSeed(12345));

            var path = Path.Combine(Application.dataPath, "Atlas/Tests/Kernel/Golden/TileId_Seed_golden.csv");
            var rows = new List<string[]>();
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim('\r', ' ');
                if (line.Length == 0 || line[0] == '#') continue;
                rows.Add(line.Split(','));
            }
            Assert.AreEqual(64, rows.Count);
            var order = Discover(g, 32);
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var id = new TileId(long.Parse(r[0]), long.Parse(r[1]), long.Parse(r[2]), long.Parse(r[3]), long.Parse(r[4]), long.Parse(r[5]));
                Assert.AreEqual(order[i / 2], id, "row " + i);
                ulong want = ulong.Parse(r[7].Substring(2), NumberStyles.HexNumber);
                Assert.AreEqual(want, id.ContentSeed(uint.Parse(r[6])), "row " + i);
            }
            Assert.AreEqual(z.GetHashCode(), TileId.Zero.GetHashCode());
            Assert.AreEqual(unchecked((int)0x4194AB7A26ABAF68UL ^ (int)(0x4194AB7A26ABAF68UL >> 32)), z.GetHashCode());
        }

        // ---------- T8 ----------
        // Row "re-root when the walker crosses an edge"; §4.6, §6.3 step 4.
        [Test]
        public void TileId_Reroot()
        {
            var g = new TileGraph(5);
            double r = g.InRadius;
            var pose = new WalkerPose { Tile = TileId.Zero, Local = Iso2.Boost(Dir(0) * (r + 0.01), -1) };
            Assert.IsFalse(g.Reroot(ref pose));
            pose.Local = Iso2.Boost(Dir(0) * (r + 0.03), -1);
            Assert.IsTrue(g.Reroot(ref pose));
            var n = TileId.Zero.Step(0);
            Assert.AreEqual(n, pose.Tile);
            int back = Back(g.Neighbour, TileId.Zero, n);
            pose.Local = Iso2.Boost(Dir(back) * (r + 0.019), -1);
            Assert.IsFalse(g.Reroot(ref pose));
            pose.Local = Iso2.Boost(Dir(back) * (r + 0.06), -1);
            Assert.IsTrue(g.Reroot(ref pose));
            Assert.AreEqual(TileId.Zero, pose.Tile);

            // random walk, with a gentle pull home so exact ids stay in 64-bit range (deviation D14)
            double phi = (1 + Math.Sqrt(5)) / 2, sp = Math.Sqrt(phi);
            var rnd = new Random(Seed);
            var P = Id1; var U = Id1; pose = new WalkerPose { Tile = TileId.Zero, Local = U };
            int crossings = 0, singles = 0;
            for (int f = 0; f < 20000; f++)
            {
                double2 d;
                var W = math.mul(P.M, pose.Local.M).c2;
                if (W.z > Math.Cosh(6))
                {
                    var inv = (P * pose.Local).Inverse().M.c2; // origin as seen from the walker
                    d = math.normalize(inv.xy) * 0.1;
                }
                else { double th = rnd.NextDouble() * 2 * Math.PI; d = 0.1 * new double2(Math.Cos(th), Math.Sin(th)); }
                pose.Local = (pose.Local * Iso2.Boost(d, -1)).Orthonormalised();
                var prevTile = pose.Tile; var Wb = math.mul(P.M, pose.Local.M).c2;
                var copy = pose; var Pn = P; int nk = 0, first = -1; var curTile = pose.Tile;
                int k;
                while ((k = g.ExitEdge(copy.Local)) >= 0)
                {
                    var G = g.StepMatrix(curTile, k);
                    Pn = Pn * G; copy.Local = G.Inverse() * copy.Local; curTile = g.Neighbour(curTile, k);
                    if (nk++ == 0) first = k;
                    Assert.Less(nk, 5);
                }
                bool moved = g.Reroot(ref pose);
                Assert.AreEqual(nk > 0, moved);
                Assert.AreEqual(curTile, pose.Tile);
                Assert.AreEqual(-1, g.ExitEdge(pose.Local));
                if (moved)
                {
                    crossings++; P = Pn;
                    if (nk == 1) { singles++; Assert.AreEqual(prevTile.Step(first), pose.Tile); }
                    var Wa = math.mul(P.M, pose.Local.M).c2;
                    Assert.Less(math.cmax(math.abs(Wa - Wb)), 1e-12 * Wb.z * 100 + 1e-12);
                    if (f % 97 == 0)
                    {
                        double X = pose.Tile.Xu + pose.Tile.Xv * phi, Y = pose.Tile.Yu + pose.Tile.Yv * phi, T = pose.Tile.Tu + pose.Tile.Tv * phi;
                        Assert.Less(math.cmax(math.abs(P.M.c2 - new double3(sp * X, sp * Y, T))), 1e-9 * T);
                    }
                }
                else Assert.AreEqual(prevTile, pose.Tile);
            }
            TestContext.WriteLine($"random walk crossings: {crossings} (single {singles})");
            Assert.Greater(crossings, 100);
        }

        // ---------- T9 ----------
        // R3 T3: exact frames vs double products; A.5.
        [Test]
        public void TileId_ExactVsDouble()
        {
            var Gk = new Iso2[4];
            for (int k = 0; k < 4; k++) Gk[k] = XFrame.Identity.Step(k).ToIso2();
            var rnd = new Random(Seed);
            double worst = 0;
            for (int i = 0; i < 1000; i++)
            {
                int len = 1 + rnd.Next(25);
                var f = XFrame.Identity; var m = Id1;
                for (int s = 0; s < len; s++) { int k = rnd.Next(4); f = f.Step(k); m = m * Gk[k]; }
                var e = f.ToIso2();
                double err = math.cmax(math.abs(e.M.c0 - m.M.c0)); err = math.max(err, math.cmax(math.abs(e.M.c1 - m.M.c1))); err = math.max(err, math.cmax(math.abs(e.M.c2 - m.M.c2)));
                worst = math.max(worst, err / m.M.c2.z);
                Assert.Less(err, 1e-9 * m.M.c2.z);
            }
            TestContext.WriteLine($"worst relative exact-vs-double error {worst:E2}");
        }

        // ---------- T10 ----------
        // A.5: "exact to 42 straight steps, overflow at 43".
        [Test]
        public void TileId_Overflow()
        {
            var m = XFrame.Identity.Step(0);
            for (int i = 0; i < 41; i++) m = m.Step(2);
            var mm = m;
            Assert.Throws<OverflowException>(() => mm.Step(2));
        }

        // ---------- T11 ----------
        // kernel.md §9 point 2: id resolution without a graph.
        [Test]
        public void TileId_Resolve()
        {
            var g = new TileGraph(5); var list = new List<VisibleTile>(); var list2 = new List<VisibleTile>();
            var far = Straight(g.Neighbour, 20);
            var fresh = new TileGraph(5);
            fresh.VisibleSet(far, 3.0, list);
            Assert.AreEqual(45, list.Count);
            g.VisibleSet(far, 3.0, list2);
            Assert.AreEqual(list.Count, list2.Count);
            for (int i = 0; i < list.Count; i++)
            {
                Assert.AreEqual(list[i].Id, list2[i].Id);
                Assert.Less(math.cmax(math.abs(list[i].F.M.c0 - list2[i].F.M.c0)), 1e-12);
                Assert.Less(math.cmax(math.abs(list[i].F.M.c2 - list2[i].F.M.c2)), 1e-12);
            }
            var lv = Levels(g.Neighbour, 8);
            foreach (var l in lv) foreach (var t in l)
            {
                Assert.IsTrue(g.TryGetFrame(t, out var f));
                Assert.IsTrue(f.Equals(ExactTiling.CanonicalFrame(t)), t.ToString());
            }
            Assert.Throws<ArgumentException>(() => new TileId(1, 0, 0, 0, 1, 0).Step(0));
            Assert.Throws<ArgumentException>(() => default(TileId).Step(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => TileId.Zero.Step(4));
        }

        // ---------- T12 ----------
        // Row "tolerance-hash variant for {4,6}"; R3 T7.
        [Test]
        public void Tiling46_ToleranceVariant()
        {
            var g = new TileGraph(6);
            var lv = Levels(g.Neighbour, 10);
            var want = new long[] { 1, 4, 12, 32, 84, 220, 576, 1508, 3948, 10336, 27060 };
            var got = new long[11];
            for (int i = 0; i <= 10; i++) got[i] = lv[i].Count;
            TestContext.WriteLine("{4,6} counts: " + string.Join(", ", got));
            Assert.AreEqual(want, got);
            foreach (var l in lv) foreach (var t in l) Assert.IsTrue(Ring6Ok(t), t.ToString());

            foreach (var e in new[] { 0, 1, 2, 3 })
            {
                var (tiles, exits) = VertexWalk(g.Neighbour, TileId.Zero, e, 6);
                Assert.AreEqual(TileId.Zero, tiles[6]); Assert.AreEqual(e, exits[6]);
                for (int i = 1; i <= 5; i++) Assert.AreNotEqual(TileId.Zero, tiles[i]);
            }

            var depth = new Dictionary<TileId, int>();
            for (int d = 0; d <= 5; d++) foreach (var t in lv[d]) depth[t] = d;
            var rnd = new Random(Seed);
            for (int w = 0; w < 10; w++)
            {
                var gw = new TileGraph(6); var vis = new HashSet<TileId> { TileId.Zero }; var cur = TileId.Zero; long steps = 0;
                while (vis.Count < depth.Count && steps++ < 3000000)
                {
                    var n = gw.Neighbour(cur, rnd.Next(4));
                    if (depth.ContainsKey(n)) { cur = n; vis.Add(n); }
                }
                Assert.AreEqual(depth.Count, vis.Count);
                foreach (var t in depth.Keys) for (int k = 0; k < 4; k++) Assert.AreEqual(g.Neighbour(t, k), gw.Neighbour(t, k));
            }
            var g2 = new TileGraph(6);
            var id8 = lv[8][1000];
            for (int k = 0; k < 4; k++) Assert.AreEqual(g.Neighbour(id8, k), g2.Neighbour(id8, k));

            var list = new List<VisibleTile>();
            g.VisibleSet(TileId.Zero, 3.0, list);
            TestContext.WriteLine("{4,6} visible at rho 3: " + list.Count);
            Assert.Greater(list.Count, 0);
            for (int i = 1; i < list.Count; i++) Assert.GreaterOrEqual(list[i].GraphDistance, list[i - 1].GraphDistance);
        }

        // ---------- T13 ----------
        [Test]
        public void TileGraph_Guards()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TileGraph(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TileGraph(4));
            var g = new TileGraph(5);
            Assert.AreEqual(5, g.Q);
            Assert.Throws<ArgumentNullException>(() => g.VisibleSet(TileId.Zero, 3, null));
            var list = new List<VisibleTile>();
            Assert.Throws<ArgumentOutOfRangeException>(() => g.VisibleSet(TileId.Zero, -1, list));
            Assert.Throws<ArgumentOutOfRangeException>(() => g.VisibleSet(TileId.Zero, double.NaN, list));
            g.VisibleSet(TileId.Zero, 3, list); Assert.AreEqual(45, list.Count);
            g.VisibleSet(TileId.Zero, 2, list); Assert.AreEqual(13, list.Count);
        }
    }
}
