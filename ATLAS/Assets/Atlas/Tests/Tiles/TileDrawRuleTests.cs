// [P1-B06] T1, T2, T9. Each test name states its criterion (brief section 4).
using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Rendering;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Tiles.Tests
{
    public class TileDrawRuleTests
    {
        // T1: MP 8.5 "wholly outside a plane with inward normal n when n.c + r.|n_floor| < 0"; A.3 circumradius.
        [Test]
        public void TileDrawRule_PlaneTest_IsPlanFormula()
        {
            Assert.IsTrue(TileDrawRule.ShellOutside(new double3(0, 0, 1), new double3(0, -1.6, -2), 1.5, 0));
            Assert.IsFalse(TileDrawRule.ShellOutside(new double3(0, 0, 1), new double3(0, -1.6, -2), 2.5, 0));
            Assert.IsTrue(TileDrawRule.ShellOutside(new double3(0, 1, 0), new double3(0, -1.6, 3), 1, 0));
            Assert.IsFalse(TileDrawRule.ShellOutside(new double3(0, 1, 0), new double3(0, -1.6, 3), 1, 2.6));
            Assert.IsFalse(TileDrawRule.ShellOutside(new double3(0, -1, 0), new double3(0, -1.6, 3), 1, 2.6));

            var cone = TileViewCone.FromHead(new float3(0, 1.6f, 0), quaternion.identity, 50, 48);
            foreach (var n in new[] { cone.NLeft, cone.NRight, cone.NTop, cone.NBottom }) Assert.AreEqual(1.0, math.length(n), 1e-12);
            var eye = cone.Eye;
            Assert.That(Inside(cone, eye + new double3(0, 0, 10)), Is.True);
            Assert.That(Inside(cone, eye + new double3(0, 0, -10)), Is.False);
            double a = 62 * Math.PI / 180, b = 52 * Math.PI / 180;
            Assert.AreEqual(0.0, math.dot(cone.NRight, new double3(Math.Sin(a), 0, Math.Cos(a))), 1e-9);
            Assert.AreEqual(0.0, math.dot(cone.NBottom, new double3(0, -Math.Sin(b), Math.Cos(b))), 1e-9);

            Assert.AreEqual(0.8424821, TileDrawRule.CircumRadius(5), 1e-7);
            Assert.Less(Math.Abs(TileDrawRule.CircumRadius(5) - 0.8425), 1e-4);
            Assert.AreEqual(1.1462158, TileDrawRule.CircumRadius(6), 1e-7);
        }

        static bool Inside(in TileViewCone c, double3 p)
        {
            double3 v = p - c.Eye;
            return math.dot(c.NLeft, v) >= 0 && math.dot(c.NRight, v) >= 0 && math.dot(c.NTop, v) >= 0 && math.dot(c.NBottom, v) >= 0;
        }

        // T2: MP 11.1 VisibleSet_Counts (draw-rule half), EditMode stand-in for "<= 150 draw calls per frame at any head pitch"; 8.5 counts.
        [Test, Timeout(600000)]
        public void VisibleSet_Counts_DrawRuleHalf()
        {
            const int q = 5; const double rho = 3.0;
            double rc = TileDrawRule.CircumRadius(q);
            var rng = new System.Random(TileTestPoses.Seed);
            var log = new System.Text.StringBuilder();
            foreach (double L in new[] { 1.41, 1.885, 2.83 })
            {
                var pts = TileTestPoses.MidMeshPoints(q, L);
                var graph = new TileGraph(q);
                var sel = new TileSelection(graph, L) { Rho = rho };
                int maxAny = 0, maxLevel = 0, viol = 0, litFloor = 0, litPost = 0;
                var flags = new int[TileSelection.CandidateCapacity];
                for (int pass = 0; pass < 2; pass++)
                    for (int it = 0; it < 500; it++)
                    {
                        var U = TileTestPoses.RandomWalker(rng, q);
                        var head = TileTestPoses.RandomHead(rng);
                        double yaw = rng.NextDouble() * 360;
                        double pitchDown = pass == 0 ? -20 + rng.NextDouble() * 110 : 0;
                        var rot = TileTestPoses.Rot(yaw, pitchDown);
                        var cone = TileViewCone.FromHead(head, rot, TileDrawRule.DefaultHalfSideDeg, TileDrawRule.DefaultHalfUpDownDeg);
                        var pose = new WalkerPose { Tile = TileId.Zero, Local = U };
                        int n = sel.Update(pose, cone);
                        maxAny = Math.Max(maxAny, n);
                        if (pass == 1) maxLevel = Math.Max(maxLevel, n);
                        var fr = new TileTestPoses.EyeFrusta(head, rot, 50, 48);
                        var uInv = U.Inverse();
                        Array.Fill(flags, -1);
                        int j = 0;
                        for (int i = 0; i < sel.Candidates.Count; i++)
                        {
                            var t = sel.Candidates[i];
                            bool drawn = j < sel.DrawnCount && sel.Drawn[j].Id.Equals(t.Id);
                            if (drawn) j++;
                            var A = uInv * t.F;
                            double3 p = A.M.c2;
                            double d = Math.Acosh(Math.Max(1, p.z));
                            if (d > rho + rc) continue;
                            // plan's literal rule: (a) + floor-disc plane test with height 0, no rule (c)
                            double2 lg = Geometry2.Log(p, -1);
                            var cw = new double3(cone.Eye.x + L * lg.x, TileDrawRule.FloorY, cone.Eye.z + L * lg.y);
                            bool literal = !LiteralOutside(cone, cw, rc * L);
                            if (!drawn || !literal)
                            {
                                int f = TileTestPoses.ViolationFlags(pts, A, L, rho, fr);
                                if (!drawn && f != 0) viol++;
                                if (!literal)
                                {
                                    if ((f & TileTestPoses.FloorViolation) != 0) litFloor++;
                                    if ((f & TileTestPoses.ElevatedViolation) != 0) litPost++;
                                }
                            }
                        }
                        Assert.AreEqual(sel.DrawnCount, j, "Drawn must be a subsequence of Candidates in BFS order");
                    }
                log.AppendLine($"L={L}: max drawn any pitch {maxAny}, level {maxLevel}, violations {viol}, literal-rule floor {litFloor} post {litPost}");
                Assert.LessOrEqual(maxAny, 71, "any-pitch drawn at L=" + L);
                Assert.LessOrEqual(2 * maxAny, 150, "draw calls at L=" + L);
                Assert.LessOrEqual(maxLevel, 59, "level gaze drawn at L=" + L);
                Assert.AreEqual(0, viol, "undrawn tile with a point nearer than rho in the true frustum, L=" + L);
                Assert.AreEqual(0, litFloor, "plan's literal floor rule, floor points, L=" + L);
            }
            Debug.Log("[P1-B06 T2] " + log);
            TestContext.Out.WriteLine("[P1-B06 T2] " + log);
        }

        static bool LiteralOutside(in TileViewCone c, double3 cw, double r)
        {
            double3 v = cw - c.Eye;
            return TileDrawRule.ShellOutside(c.NLeft, v, r, 0) || TileDrawRule.ShellOutside(c.NRight, v, r, 0)
                || TileDrawRule.ShellOutside(c.NTop, v, r, 0) || TileDrawRule.ShellOutside(c.NBottom, v, r, 0);
        }

        // T9: MP 8.5 (a) completeness: every tile with d(walker, centre) <= rho + r_c is a candidate; kernel.md 4.6 guide [P1-B06 D5].
        [Test, Timeout(600000)]
        public void Candidates_CoverEveryTileWithinReach()
        {
            const int q = 5;
            double rc = TileDrawRule.CircumRadius(q);
            var rng = new System.Random(TileTestPoses.Seed);
            var brute = new List<VisibleTile>();
            new TileGraph(q).VisibleSet(TileId.Zero, 7.0, brute);
            var graph = new TileGraph(q);
            var sel = new TileSelection(graph, 1.885);
            var never = TileTestPoses.NeverCull();
            int missing = 0;
            foreach (double rho in new[] { 2.0, 2.5, 3.0, 3.5 })
            {
                sel.Rho = rho;
                for (int it = 0; it < 300; it++)
                {
                    var U = TileTestPoses.RandomWalker(rng, q, 0.02);
                    sel.Update(new WalkerPose { Tile = TileId.Zero, Local = U }, never);
                    var ids = new HashSet<TileId>();
                    for (int i = 0; i < sel.Candidates.Count; i++) ids.Add(sel.Candidates[i].Id);
                    var uInv = U.Inverse();
                    for (int i = 0; i < brute.Count; i++)
                    {
                        double d = Math.Acosh(Math.Max(1, (uInv * brute[i].F).M.c2.z));
                        if (d <= rho + rc && !ids.Contains(brute[i].Id)) missing++;
                    }
                }
            }
            Assert.AreEqual(0, missing);
        }
    }
}
