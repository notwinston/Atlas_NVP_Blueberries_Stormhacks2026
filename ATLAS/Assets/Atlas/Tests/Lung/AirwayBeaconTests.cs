using System;
using System.Collections.Generic;
using System.Linq;
using Atlas.Kernel;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Random = System.Random;

namespace Atlas.Blades.Lung.Tests
{
    // P2-B06 beacon tests (Master Plan §10.2.3 Aids; Build Graph row P2-B06). Each test names the criterion it checks.
    public class AirwayBeaconTests
    {
        const int Seed = 20261003;
        const double Phi = 1.618033988749895;
        static AirwayTree Tree => TreeFixture.Tree;

        static AirwayTile RandomTile(Random r, int maxGen = 16)
        {
            int g = r.Next(0, maxGen + 1);
            var s = new char[g];
            for (int k = 0; k < g; k++) s[k] = r.Next(2) == 0 ? 'L' : 'R';
            string a = new string(s);
            return g > 0 && r.Next(2) == 0 ? AirwayTile.Bend(a) : AirwayTile.Fork(a);
        }

        // The test's own prefix step, written from the brief's table, independent of AirwayBeacon.NextStep.
        static (bool arrived, AirwayTile next, int edge) Reference(AirwayTile cur, string t)
        {
            string a = cur.Address;
            bool onPath = t.Length >= a.Length && t.Substring(0, a.Length) == a;
            if (cur.Kind == AirwayTileKind.Fork)
            {
                if (a == t) return (true, default, -1);
                if (onPath) { char c = t[a.Length]; return (false, AirwayTile.Bend(a + c), c == 'L' ? 3 : 1); }
                return (false, AirwayTile.Bend(a), 0);
            }
            if (onPath) return (false, AirwayTile.Fork(a), 2);
            return (false, AirwayTile.Fork(a.Substring(0, a.Length - 1)), 0);
        }

        // N1: row P2-B06 acceptance: the beacon step equals the address-prefix step on 1,000 random targets.
        [Test]
        public void P2B06_Accept_BeaconStepEqualsPrefixStep_1000Targets()
        {
            var r = new Random(Seed);
            var g = new TileGraph(5);
            var nodes = TreeFixture.File.nodes;
            int mismatchA = 0, mismatchB = 0, mismatchC = 0, arrived = 0;
            for (int i = 0; i < 1000; i++)
            {
                string target = nodes[r.Next(nodes.Length)].address;
                var cur = RandomTile(r);
                if (i % 10 == 0 && cur.Kind == AirwayTileKind.Fork) target = cur.Address; // force arrived cases
                var rf = Reference(cur, target);
                var step = AirwayBeacon.NextStep(cur, target);
                if (step.Arrived != rf.arrived || step.TreeEdge != rf.edge || (!rf.arrived && !step.Next.Equals(rf.next))) { mismatchA++; continue; }
                if (rf.arrived) { arrived++; continue; }
                var curId = Tree.FrameOf(cur).Tile;
                int ce = Tree.FrameOf(cur).CanonicalEdge(step.TreeEdge);
                if (g.Neighbour(curId, ce) != Tree.FrameOf(rf.next).Tile) mismatchB++;
                // (c) the arrow, followed 0.6 units from the tile centre at a random heading, crosses the same edge
                var beacon = new AirwayBeacon(Tree);
                double th = r.NextDouble() * 2 * Math.PI;
                var pose = new WalkerPose { Tile = curId, Local = Iso2.Rot(th) };
                double2 d = AirwayBeacon.ArrowDirection(pose, ce);
                Assert.AreEqual(1.0, math.length(d), 1e-9);
                var moved = pose.Local * Iso2.Boost(0.6 * d, -1);
                int exit = g.ExitEdge(moved);
                if (exit != ce || g.Neighbour(curId, exit) != Tree.FrameOf(rf.next).Tile) mismatchC++;
            }
            Assert.AreEqual(0, mismatchA, "NextStep vs prefix step");
            Assert.AreEqual(0, mismatchB, "ExitCanonicalEdge vs tile graph");
            Assert.AreEqual(0, mismatchC, "ArrowDirection probe vs tile graph");
            UnityEngine.Debug.Log($"N1: 1000 targets, {arrived} arrived cases, 0 mismatches");
        }

        static List<AirwayTile> TreeNeighbours(AirwayTile t)
        {
            string a = t.Address;
            var l = new List<AirwayTile>();
            if (t.Kind == AirwayTileKind.Fork)
            {
                if (a.Length > 0) l.Add(AirwayTile.Bend(a));
                if (Tree.HasChildren(a)) { l.Add(AirwayTile.Bend(a + "L")); l.Add(AirwayTile.Bend(a + "R")); }
            }
            else { l.Add(AirwayTile.Fork(a.Substring(0, a.Length - 1))); l.Add(AirwayTile.Fork(a)); }
            return l;
        }

        // cosh of the hyperbolic distance from tile q to the fork of `target`, from the unique tree path replayed exactly from tile zero.
        static double CoshToTargetFork(AirwayTile q, string target)
        {
            var f = TileFrame.Zero; var cur = q;
            for (int guard = 0; guard < 64; guard++)
            {
                string a = cur.Address;
                bool onPath = target.StartsWith(a, StringComparison.Ordinal);
                if (cur.Kind == AirwayTileKind.Fork)
                {
                    if (a == target) { var id = f.Tile; return id.Tu + id.Tv * Phi; }
                    if (onPath) { char c = target[a.Length]; f = RuleS.ChildBend(f, c); cur = AirwayTile.Bend(a + c); }
                    else { f = RuleS.ForkToBend(f); cur = AirwayTile.Bend(a); }
                }
                else if (onPath) { f = RuleS.BendToFork(f); cur = AirwayTile.Fork(a); }
                else { f = RuleS.BendToParentFork(f, a[a.Length - 1]); cur = AirwayTile.Fork(a.Substring(0, a.Length - 1)); }
            }
            throw new InvalidOperationException("path did not terminate");
        }

        // N2: MP §10.2.3 "greedy routing is exact" (Sarkar): the neighbour closest to the target fork is the prefix step (supporting).
        [Test]
        public void P2B06_Greedy_EqualsPrefix_Sarkar()
        {
            var r = new Random(Seed);
            int n = 0;
            while (n < 1000)
            {
                var cur = RandomTile(r, 11);
                string target = RandomTile(r, 11).Address;
                var rf = Reference(cur, target);
                if (rf.arrived) continue;
                n++;
                var nbs = TreeNeighbours(cur);
                var costs = nbs.Select(q => CoshToTargetFork(q, target)).ToArray();
                int best = Array.IndexOf(costs, costs.Min());
                Assert.AreEqual(1, costs.Count(c => c == costs[best]), "tie at " + cur + " -> " + target);
                Assert.AreEqual(rf.next, nbs[best], cur + " -> " + target);
            }
        }

        // N3: MP §10.2.3 "shown after 8 s of hesitation".
        [Test]
        public void P2B06_EightSecondRule()
        {
            var b = new AirwayBeacon(Tree);
            b.Reset(AirwayRoute.Full(Tree));
            var tile = AirwayTile.Fork("R"); // not the compass fork ("" on the fixture)
            b.Tick(tile, 0.0); Assert.IsFalse(b.Visible);
            b.Tick(tile, 7.999); Assert.IsFalse(b.Visible); Assert.IsFalse(b.RescueShown);
            b.Tick(tile, 8.0); Assert.IsTrue(b.Visible); Assert.IsTrue(b.RescueShown);
            b.Tick(AirwayTile.Bend("RR"), 9.0); Assert.IsFalse(b.Visible);
            b.Tick(AirwayTile.Bend("RR"), 16.999); Assert.IsFalse(b.Visible);
            b.Tick(AirwayTile.Bend("RR"), 17.0); Assert.IsTrue(b.Visible);
            Assert.AreEqual(8.0, AirwayBeacon.HesitationSeconds);
        }

        // N4: "except at the Two Compasses fork": shown on arrival there, and not counted as rescue (D-G4-7).
        [Test]
        public void P2B06_CompassFork_ShownOnArrival()
        {
            var b = new AirwayBeacon(Tree);
            var route = AirwayRoute.Full(Tree);
            Assert.IsTrue(route.HasTwoCompasses(Tree));
            b.Reset(route);
            var fork = AirwayTile.Fork(Tree.CompassForkAddress);
            b.Tick(fork, 0.0);
            Assert.IsTrue(b.AtCompassFork); Assert.IsTrue(b.Visible);
            b.Tick(fork, 100.0);
            Assert.IsTrue(b.Visible); Assert.IsFalse(b.RescueShown);
        }

        // N5: at the target fork there is no arrow.
        [Test]
        public void P2B06_Arrived_NoArrow()
        {
            var b = new AirwayBeacon(Tree);
            b.Reset(AirwayRoute.Full(Tree));
            var end = AirwayTile.Fork(Tree.TargetAddress);
            b.Tick(end, 0.0); Assert.IsFalse(b.Visible);
            b.Tick(end, 100.0); Assert.IsFalse(b.Visible); Assert.IsFalse(b.RescueShown);
            Assert.IsTrue(b.Step.Arrived); Assert.AreEqual(-1, b.Step.TreeEdge); Assert.AreEqual(-1, b.ExitCanonicalEdge);
        }

        // N9: BeaconArrowView places the arrow 0.6 m ahead of the head, 0.01 m above the floor (D-G4-12; supporting).
        [Test]
        public void BeaconArrowView_Places()
        {
            var b = new AirwayBeacon(Tree);
            b.Reset(AirwayRoute.Full(Tree));
            // find a state whose exit is canonical edge 0 (so the arrow points along the walker's +x)
            AirwayTile? pick = null;
            var cand = new List<AirwayTile>();
            foreach (var n in TreeFixture.File.nodes.Where(n => n.gen <= 5))
            {
                cand.Add(AirwayTile.Fork(n.address));
                if (n.gen > 0) cand.Add(AirwayTile.Bend(n.address));
            }
            foreach (var t in cand)
            {
                var step = AirwayBeacon.NextStep(t, Tree.TargetAddress);
                if (!step.Arrived && Tree.FrameOf(t).CanonicalEdge(step.TreeEdge) == 0) { pick = t; break; }
            }
            Assert.IsTrue(pick.HasValue, "a tile exiting through canonical edge 0");
            b.Tick(pick.Value, 0.0); b.Tick(pick.Value, 9.0);
            Assert.IsTrue(b.Visible); Assert.AreEqual(0, b.ExitCanonicalEdge);
            var go = new GameObject("arrow");
            try
            {
                var v = go.AddComponent<BeaconArrowView>();
                var pose = new WalkerPose { Tile = TileId.Zero, Local = new Iso2(double3x3.identity, -1) };
                var frame = new FrameContext(1, 9.0, 0.01f, new float3(1, 1.6f, 2), quaternion.identity, true, false);
                v.Apply(frame, pose, b);
                Assert.IsTrue(go.activeSelf);
                Assert.AreEqual(1.6f, go.transform.position.x, 1e-5f);
                Assert.AreEqual(0.01f, go.transform.position.y, 1e-5f);
                Assert.AreEqual(2f, go.transform.position.z, 1e-5f);
                var fwd = go.transform.forward;
                Assert.AreEqual(1f, fwd.x, 1e-5f); Assert.AreEqual(0f, fwd.y, 1e-5f); Assert.AreEqual(0f, fwd.z, 1e-5f);
                b.Tick(AirwayTile.Fork("RRL"), 10.0); // tile change: hidden again
                v.Apply(frame, pose, b);
                Assert.IsFalse(go.activeSelf);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // Supporting: the arrow mesh is a 0.20 x 0.20 m floor triangle, every edge within 0.25 m (MP §8.4 A.6), face up.
        [Test]
        public void ArrowMesh_Geometry()
        {
            var m = ArrowMesh.Build(3);
            try
            {
                var v = m.vertices; var t = m.triangles;
                Assert.AreEqual(3, v.Length); CollectionAssert.AreEqual(new[] { 0, 1, 2 }, t);
                for (int i = 0; i < 3; i++) Assert.LessOrEqual(Vector3.Distance(v[i], v[(i + 1) % 3]), 0.25f);
                Assert.Greater(Vector3.Cross(v[1] - v[0], v[2] - v[0]).y, 0f);
                Assert.AreEqual(Atlas.Rendering.AtlasPalette.CellUV(3), m.uv[0]);
            }
            finally { UnityEngine.Object.DestroyImmediate(m); }
        }
    }
}
