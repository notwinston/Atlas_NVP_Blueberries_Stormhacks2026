using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Atlas.Kernel;
using NUnit.Framework;
using Random = System.Random;

namespace Atlas.Blades.Lung.Tests
{
    // P2-B01 AirwayTree tests (Master Plan §10.2.3; Build Graph row P2-B01). Each test names the criterion it checks.
    public class AirwayTreeTests
    {
        const int Seed = 20261003;

        static AirwayTree Tree => TreeFixture.Tree;

        // L1: row P2-B01 acceptance 1: 32,767 nodes to depth 14 map to distinct tile ids.
        [Test]
        public void P2B01_Accept_Depth14_32767DistinctIds()
        {
            var sw = Stopwatch.StartNew();
            var ids = new HashSet<TileId>();
            int n = 0;
            foreach (var node in TreeFixture.File.nodes)
            {
                if (node.gen > 14) continue;
                n++; ids.Add(Tree.FrameOf(node.address).Tile);
            }
            TestContext.WriteLine($"P2-B01 measured: tree.json load {TreeFixture.LoadMs:F0} ms, tree build {TreeFixture.BuildMs:F0} ms, depth-14 FrameOf {sw.Elapsed.TotalMilliseconds:F0} ms");
            Assert.AreEqual(32767, n);
            Assert.AreEqual(32767, ids.Count);
        }

        static int ExpectedNodes(int depth) => depth == 0 ? 15 : depth == 1 ? 23 : depth <= 13 ? 31 : depth == 14 ? 23 : depth == 15 ? 15 : 7;

        static IEnumerable<string> WindowAddresses(Random r)
        {
            var all = new List<string> { "" };
            for (int d = 1; d <= 4; d++)
                for (int i = 0; i < (1 << d); i++)
                {
                    var s = new char[d];
                    for (int k = 0; k < d; k++) s[k] = ((i >> (d - 1 - k)) & 1) == 0 ? 'L' : 'R';
                    all.Add(new string(s));
                }
            for (int i = 0; i < 2000; i++)
            {
                int d = r.Next(5, 17); var s = new char[d];
                for (int k = 0; k < d; k++) s[k] = r.Next(2) == 0 ? 'L' : 'R';
                all.Add(new string(s));
            }
            return all;
        }

        // L2: row P2-B01 acceptance 2 and MP §10.2.3 "lazy window <= 60 nodes, <= 120 tiles".
        [Test]
        public void P2B01_Accept_WindowAtMost60Nodes()
        {
            var r = new Random(Seed);
            int maxN = 0, maxT = 0;
            var times = new List<double>();
            foreach (var a in WindowAddresses(r))
            {
                foreach (var kind in new[] { AirwayTileKind.Fork, AirwayTileKind.Bend })
                {
                    if (a.Length == 0 && kind == AirwayTileKind.Bend) continue;
                    var tile = kind == AirwayTileKind.Fork ? AirwayTile.Fork(a) : AirwayTile.Bend(a);
                    var sw = Stopwatch.StartNew();
                    Tree.SetCurrent(tile);
                    times.Add(sw.Elapsed.TotalMilliseconds);
                    var w = Tree.Window;
                    Assert.AreEqual(a, w.Current);
                    Assert.LessOrEqual(w.NodeCount, AirwayWindow.MaxNodes, a);
                    Assert.LessOrEqual(w.TileCount, AirwayWindow.MaxTiles, a);
                    int en = ExpectedNodes(a.Length);
                    Assert.AreEqual(en, w.NodeCount, a);
                    Assert.AreEqual(2 * en - (a.Length <= 2 ? 1 : 0), w.TileCount, a); // forks + bends of the non-root nodes
                    maxN = Math.Max(maxN, w.NodeCount); maxT = Math.Max(maxT, w.TileCount);
                }
            }
            Assert.AreEqual(31, maxN); Assert.AreEqual(62, maxT);
            times.Sort();
            TestContext.WriteLine($"P2-B01 measured: SetCurrent p95 = {times[(int)(times.Count * 0.95)]:F3} ms over {times.Count} calls");
            Tree.SetCurrent(AirwayTile.Fork(""));
        }

        // Independent tree-edge neighbour of a window tile (R3 §D3 layout), as an AirwayTile or null.
        static AirwayTile? TreeNeighbour(AirwayTile t, int treeEdge, AirwayTree tree)
        {
            string a = t.Address;
            if (t.Kind == AirwayTileKind.Fork)
            {
                if (treeEdge == 0) return a.Length == 0 ? (AirwayTile?)null : AirwayTile.Bend(a);
                if (treeEdge == 2) return null;
                if (!tree.HasChildren(a)) return null;
                return AirwayTile.Bend(a + (treeEdge == 3 ? 'L' : 'R'));
            }
            if (treeEdge == 0) return AirwayTile.Fork(a.Substring(0, a.Length - 1));
            if (treeEdge == 2) return AirwayTile.Fork(a);
            return null;
        }

        // L3: MP §10.2.3 "tiles not in the tree are solid tissue": TryLocate agrees with the independent tile graph.
        [Test]
        public void P2B01_Window_TryLocate()
        {
            var r = new Random(Seed);
            var g = new TileGraph(5);
            for (int i = 0; i < 200; i++)
            {
                string a = new string(Enumerable.Range(0, r.Next(0, 17)).Select(_ => r.Next(2) == 0 ? 'L' : 'R').ToArray());
                var cur = a.Length > 0 && r.Next(2) == 0 ? AirwayTile.Bend(a) : AirwayTile.Fork(a);
                Tree.SetCurrent(cur);
                var w = Tree.Window;
                var set = new HashSet<AirwayTile>(w.Tiles);
                foreach (var t in w.Tiles)
                {
                    var id = Tree.FrameOf(t).Tile;
                    Assert.IsTrue(w.TryLocate(id, out var back), t.Address);
                    Assert.AreEqual(t, back);
                    var fr = Tree.FrameOf(t);
                    int mask = Tree.OpenEdgesCanonical(t);
                    for (int treeEdge = 0; treeEdge < 4; treeEdge++)
                    {
                        int ce = fr.CanonicalEdge(treeEdge);
                        var nb = g.Neighbour(id, ce);
                        bool open = (mask & (1 << ce)) != 0;
                        var want = TreeNeighbour(t, treeEdge, Tree);
                        Assert.AreEqual(want.HasValue, open, $"{t.Kind} {t.Address} edge {treeEdge}");
                        bool located = w.TryLocate(nb, out var nt);
                        if (!open) Assert.IsFalse(located, $"closed edge {treeEdge} of {t.Kind} {t.Address} is tissue");
                        else
                        {
                            Assert.AreEqual(set.Contains(want.Value), located, $"open edge {treeEdge} of {t.Kind} {t.Address}");
                            if (located) Assert.AreEqual(want.Value, nt);
                            if (t.Equals(cur)) Assert.IsTrue(located, "the current tile's open neighbours are in the window");
                        }
                    }
                }
            }
            Tree.SetCurrent(AirwayTile.Fork(""));
            Assert.IsFalse(Tree.Window.TryLocate(TileId.Zero.Step(0), out _));
        }

        // L4: PH-30 / MP §10.2.4: invariants of tree.json (atlas.tree/2); no fixture-specific target.
        [Test]
        public void P2B01_TreeJson_Fixture()
        {
            Assert.AreEqual("atlas.tree/2", TreeFixture.File.schema);
            Assert.GreaterOrEqual(Tree.MaxGeneration, AirwayTree.WalkableGenerations); // P2-D07
            Assert.AreEqual((1L << (Tree.MaxGeneration + 1)) - 1, Tree.NodeCount);
            foreach (var n in TreeFixture.File.nodes) Assert.AreEqual("model", n.source, n.address);
            Assert.IsTrue(AirwayAddress.IsProperPrefixOf(Tree.CompassForkAddress, Tree.TargetAddress));
            Assert.IsTrue(Tree.TryGetNode(Tree.TargetAddress, out _));
        }

        static void Rejects(string json, string why) =>
            Assert.Throws<FormatException>(() => AirwayTreeFile.Parse(json), why);

        // L5: every validation rule of the reader (PH-30) throws FormatException.
        [Test]
        public void P2B01_TreeJson_Rejects()
        {
            var N = (Func<string, int, string>)((a, g) => TreeFixture.Node(a, g));
            string root = N("", 0), l = N("L", 1);
            Assert.DoesNotThrow(() => AirwayTreeFile.Parse(TreeFixture.Json(root + "," + l, "\"L\"", "\"\"")));
            Rejects(TreeFixture.Json(root, schema: "atlas.tree/1"), "schema");
            Rejects(TreeFixture.Json(""), "empty nodes");
            Rejects(TreeFixture.Json(root + "," + N("X", 1), "\"\""), "invalid address");
            Rejects(TreeFixture.Json(root + "," + N(new string('L', 24), 24)), "address too long");
            Rejects(TreeFixture.Json(root + "," + N("L", 2), "\"\""), "gen != length");
            Rejects(TreeFixture.Json(root + "," + root), "duplicate");
            Rejects(TreeFixture.Json(l, "\"L\"", "\"\""), "no root");
            Rejects(TreeFixture.Json(root + "," + N("LL", 2), "\"LL\"", "\"\""), "parent missing");
            Rejects(TreeFixture.Json(root + "," + TreeFixture.Node("L", 1, pos: "[0,0]"), "\"L\"", "\"\""), "pos_mm length");
            Rejects(TreeFixture.Json(root + "," + TreeFixture.Node("L", 1, dir: "[0,0,0,1]"), "\"L\"", "\"\""), "dir length");
            Rejects(TreeFixture.Json(root + "," + TreeFixture.Node("L", 1, len: 0), "\"L\"", "\"\""), "len_mm > 0");
            Rejects(TreeFixture.Json(root + "," + TreeFixture.Node("L", 1, r: 0), "\"L\"", "\"\""), "radius_mm > 0");
            Rejects(TreeFixture.Json(root + "," + l, "\"R\"", "\"\""), "target missing");
            Rejects(TreeFixture.Json(root + "," + l, "\"L\"", "\"R\""), "compass fork missing");
            Rejects(TreeFixture.Json(root + "," + l, "\"L\"", "\"L\""), "compass fork not a proper prefix");
            Assert.Throws<InvalidDataException>(() =>
            {
                var dir = Path.Combine(Path.GetTempPath(), "atlas_lfs_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(dir, "airway"));
                File.WriteAllText(Path.Combine(dir, "airway", "tree.json"), "version https://git-lfs.github.com/spec/v1\noid sha256:00\nsize 1\n");
                try { AirwayTreeFile.Load(dir); } finally { Directory.Delete(dir, true); }
            });
        }

        // L6: open edges in canonical numbering (MP §10.2.3 layout; kernel.md §4.8).
        [Test]
        public void P2B01_OpenEdges()
        {
            Assert.AreEqual(0b1010, Tree.OpenEdgesCanonical(AirwayTile.Fork("")));
            string deep = new string('L', AirwayTree.WalkableGenerations);
            string some = "RLRL";
            foreach (var a in new[] { "L", "RLR", some, deep })
                Assert.AreEqual(2, PopCount(Tree.OpenEdgesCanonical(AirwayTile.Bend(a))), "bend " + a);
            Assert.AreEqual(3, PopCount(Tree.OpenEdgesCanonical(AirwayTile.Fork(some))));
            Assert.AreEqual(1, PopCount(Tree.OpenEdgesCanonical(AirwayTile.Fork(deep))));
        }

        static int PopCount(int v) { int c = 0; while (v != 0) { c += v & 1; v >>= 1; } return c; }

        // Supporting: FrameOf matches the Kernel Rule S and is data independent.
        [Test]
        public void P2B01_FrameOf_IsRuleS()
        {
            Assert.IsTrue(Tree.FrameOf("RRRLLR").Equals(RuleS.FrameOf("RRRLLR", Tree.Trachea)));
            Assert.IsTrue(Tree.BendFrameOf("RRRLLR").Equals(RuleS.BendFrameOf("RRRLLR", Tree.Trachea)));
            Assert.IsTrue(Tree.FrameOf(AirwayTile.Bend("LR")).Equals(RuleS.BendFrameOf("LR", Tree.Trachea)));
            Assert.AreEqual(Tree.FrameOf("LRLRLRLRLRLRLRLRLRLRLRL").Tile, RuleS.FrameOf("LRLRLRLRLRLRLRLRLRLRLRL", TileFrame.Zero).Tile); // beyond the data (D-G4-4)
        }

        // Supporting: route sizes come from tree.json, never a constant 4 (PH-39).
        [Test]
        public void P2B01_Route_FromTreeJson()
        {
            var full = AirwayRoute.Full(Tree);
            Assert.AreEqual(Tree.TargetAddress, full.TargetAddress);
            Assert.AreEqual(Tree.TargetAddress.Length, full.Forks);
            Assert.AreEqual(2 * full.Forks, full.TileSteps);
            Assert.AreEqual(1, AirwayRoute.OneFork(Tree).Forks);
        }
    }
}
