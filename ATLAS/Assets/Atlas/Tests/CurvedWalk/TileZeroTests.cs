using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using static Atlas.Spaces.CurvedWalk.Tests.ReplayLogs;

namespace Atlas.Spaces.CurvedWalk.Tests
{
    // P1-B11: the room is tile zero (MP §4.6, §3.5, A.4, §9.5.6 P1-B11). Replays step the app's shared kernel and run CurvedWalkCore.
    public class TileZeroTests
    {
        // Ramp_Restores_2cm: "a ramp to loop-fit L and back restores registration to 2 cm" (MP §9.5.6 P1-B11)
        [TestCase(0f, 0f)]
        [TestCase(0.6f, -0.4f)]
        public void Ramp_Restores_2cm(float dx, float dz)
        {
            using (var h = new Rig(Room(RoomFixtures.Sample3x3), 5))
            {
                var log = Shift(Load("walk_still_60s.poselog"), dx, dz);
                double l5 = h.S.LengthScale(5);
                bool reachedTarget = false;
                for (int i = 0; i < log.Count; i++)
                {
                    if (i == 1200) h.Q.Set(4);   // t = 20 s
                    h.Step(log[i]);
                    if (Math.Abs(h.Kernel.CurvatureNow + 1.0 / (l5 * l5)) < 1e-12) reachedTarget = true;
                }
                Assert.IsTrue(reachedTarget, "ramped to loop-fit L");
                Assert.AreEqual(0.0, h.Kernel.CurvatureNow);
                Assert.AreEqual(1, h.S.Reregistrations);
                Assert.LessOrEqual(h.S.LastRegistrationErrorM, 0.02);
                Assert.Less(h.S.LastRegistrationErrorM, 0.002, "expected well under the 2 cm limit");
                WalkerPose pose = h.Kernel.Pose;
                Assert.AreEqual(TileId.Zero, pose.Tile);
                // registered flat again: the frame is the translation by the head's floor point, in metres
                var last = log[log.Count - 1];
                Assert.AreEqual(last.Position.x, pose.Local.M.c2.x, 1e-6); Assert.AreEqual(last.Position.z, pose.Local.M.c2.y, 1e-6);
                Assert.AreEqual(1.0, pose.Local.M.c0.x, 1e-9); Assert.AreEqual(0.0, pose.Local.M.c0.y, 1e-9);
                Assert.IsTrue(h.S.TileZeroFound);
            }
        }

        // ReturnHome_Reregisters: return-to-Home re-registers (MP §4.6). The space keeps no walker, so leaving it from deep
        // in the curved world leaves the app's kernel for SpaceLab to put back: flat for the room, the walker on the floor
        // point under the head, tile zero.
        [Test]
        public void ReturnHome_Reregisters()
        {
            var room = Room(RoomFixtures.Sample3x3);
            using (var h = new Rig(room, 5))
            {
                var log = Concat(Prefix(new double2(-1, -1)), Lap(3, 2, new double2(-1, -1), 0.03));
                h.Run(log);
                Assert.Less(h.Kernel.CurvatureNow, 0.0, "curved before Return Home");
                Assert.AreNotEqual(TileId.Zero, h.Kernel.Pose.Tile, "three sides of the lap end outside the room's tile");
                h.S.Exit();
                Assert.IsNull(h.S.Kernel);
                Assert.IsFalse(h.S.Walk.Armed);

                // what SpaceLab does after Exit while the kernel is not flat (App/Spaces/SpaceLab.cs)
                var last = log[log.Count - 1];
                h.Kernel.Configure(KernelConfig.Flat(room.LapSide));
                h.Kernel.ResetToFloor(new double2(last.Position.x, last.Position.z));
                Assert.AreEqual(4, h.Kernel.Q);
                Assert.AreEqual(0.0, h.Kernel.CurvatureNow); Assert.AreEqual(0.0, h.Kernel.CurvatureTarget);
                Assert.AreEqual(Curvature.Flat, h.Kernel.Kappa);
                Assert.IsNull(h.Kernel.Rig, "no rig is left behind");
                Iso2 u = h.Kernel.Pose.Local;
                // a flat chart again: an unturned translation that lies within one tile of its centre
                Assert.LessOrEqual(math.abs(u.M.c2.x), room.LapSide); Assert.LessOrEqual(math.abs(u.M.c2.y), room.LapSide);
                Assert.AreEqual(1.0, u.M.c0.x, 1e-9); Assert.AreEqual(0.0, u.M.c0.y, 1e-9);

                // and S2 entered again starts at the room's registration: tile zero, K = 0, the frame a translation by the head
                h.S.Enter(h.Kernel);
                h.S.Register(new double2(last.Position.x, last.Position.z));
                WalkerPose pose = h.Kernel.Pose;
                Assert.AreEqual(TileId.Zero, pose.Tile);
                Assert.AreEqual(0.0, h.Kernel.CurvatureNow);
                Assert.AreEqual(last.Position.x, pose.Local.M.c2.x, 1e-6); Assert.AreEqual(last.Position.z, pose.Local.M.c2.y, 1e-6);
                Assert.AreEqual(0, h.S.Reregistrations, "entering is not a landing snap");
            }
        }

        // LapLine_PostDistance: "the lap line starts 0.17 m from a corner post of tile zero (at L = 1.88 m)" (MP A.4: 0.0920553 L at loop-fit L)
        [TestCase(RoomFixtures.Sample3x3, 0.1735)]
        [TestCase(RoomFixtures.BudgetRoom, 0.1735)]
        [TestCase(RoomFixtures.SampleL3x4, 0.1301)]
        public void LapLine_PostDistance(string name, double expectM)
        {
            var room = Room(name);
            double L = room.Curvature.ForDial(5, true);   // loop-fit
            var flag = TileZeroPlacement.LapFlag(room.LapSide, -1, L);
            double h = TileChart.HalfWidth(-1, 5, L);
            double3 post2 = TileChart.KleinToModel(new double2(-h, -h), -1);   // corner 2: between edges 2 (-x) and 3 (-y)
            double d = Geometry2.Distance(flag.M.c2, post2, -1) * L;
            Assert.AreEqual(0.0920553 * L, d, 0.001);
            Assert.AreEqual(expectM, d, 0.001);
        }

        sealed class TileTrace
        {
            public readonly List<TileId> Seq = new List<TileId>();
            public readonly List<int> RunFrames = new List<int>();
            int run;
            public void Observe(TileId t)
            {
                if (Seq.Count == 0) { Seq.Add(t); run = 1; return; }
                if (t == Seq[Seq.Count - 1]) { run++; return; }
                RunFrames.Add(run); Seq.Add(t); run = 1;
            }
            public int Changes => Seq.Count - 1;
            public int MinInterior() { int m = int.MaxValue; for (int i = 1; i < RunFrames.Count; i++) m = Math.Min(m, RunFrames[i]); return m; }
        }

        // Lap_NoTileFlicker: "a replayed lap of the lap square shows no flicker of the tile id along the edges" (MP §9.5.6 P1-B11, R3 §A5)
        [TestCase(RoomFixtures.Sample3x3)]
        [TestCase(RoomFixtures.BudgetRoom)]
        public void Lap_NoTileFlicker(string name)
        {
            var room = Room(name);
            double s = room.LapSide, a = -s / 2;
            var log = Concat(Prefix(new double2(a, a)), Lap(5, s, new double2(a, a), 0.03));
            using (var h = new Rig(room, 5))
            {
                var tr = new TileTrace();
                for (int i = 0; i < log.Count; i++) { h.Step(log[i]); tr.Observe(h.Kernel.Pose.Tile); }
                ITileGraph g = h.Kernel.Graph;
                Assert.AreEqual(8, tr.Changes, "tile id changes over the lap");
                var z = TileId.Zero;
                Assert.AreEqual(new[] { z, g.Neighbour(z, 3), z, g.Neighbour(z, 0), z, g.Neighbour(z, 1), z, g.Neighbour(z, 2), z }, tr.Seq.ToArray());
                Assert.GreaterOrEqual(tr.MinInterior(), 15, "every interior run is at least 15 frames (0.25 s)");

                // control: the same log with no hysteresis flickers (test-local classifier, a copy of the rig's crossing without the margin)
                if (name == RoomFixtures.Sample3x3)
                {
                    var ctl = new TileTrace();
                    var graph = new TileGraph(5);
                    double L = h.S.LengthScale(5);
                    var tile = TileId.Zero;
                    double2 prev = new double2(log[0].Position.x, log[0].Position.z);
                    Iso2 u = TileZeroPlacement.Registered(prev, -1, L);
                    double ch = Math.Cosh(graph.InRadius), sh = Math.Sinh(graph.InRadius);
                    for (int i = 1; i < log.Count; i++)
                    {
                        double2 cur = new double2(log[i].Position.x, log[i].Position.z);
                        u = (u * Iso2.Boost((cur - prev) / L, -1)).Orthonormalised(); prev = cur;
                        for (int guard = 0; guard < 4; guard++)
                        {
                            double3 p = u.M.c2;
                            double4 sg = new double4(p.x * ch - p.z * sh, p.y * ch - p.z * sh, -p.x * ch - p.z * sh, -p.y * ch - p.z * sh);
                            int k = 0; double best = sg.x;
                            if (sg.y > best) { best = sg.y; k = 1; }
                            if (sg.z > best) { best = sg.z; k = 2; }
                            if (sg.w > best) { best = sg.w; k = 3; }
                            if (best <= 0) break;
                            Iso2 gm = graph.StepMatrix(tile, k); tile = graph.Neighbour(tile, k);
                            u = new Iso2(math.mul(gm.Inverse().M, u.M), -1);
                        }
                        ctl.Observe(tile);
                    }
                    Assert.Less(ctl.MinInterior(), 15, "control: without hysteresis at least one interior run is shorter than 15 frames");
                }
            }
        }

        // Lap_PostFade: "post fade checked on a replayed lap" (MP §9.5.6 P1-B11 Output; §3.5 near-head fade 0.3 m, post radius 0.15 m)
        [Test]
        public void Lap_PostFade()
        {
            var room = Room(RoomFixtures.Sample3x3);
            var log = Concat(Prefix(new double2(-1, -1)), Lap(5, 2, new double2(-1, -1), 0.0));
            using (var h = new Rig(room, 5))
            {
                double L = h.S.LengthScale(5);
                double hw = TileChart.HalfWidth(-1, 5, L);
                var corner = new double3[4];
                corner[0] = TileChart.KleinToModel(new double2(hw, hw), -1); corner[1] = TileChart.KleinToModel(new double2(-hw, hw), -1);
                corner[2] = TileChart.KleinToModel(new double2(-hw, -hw), -1); corner[3] = TileChart.KleinToModel(new double2(hw, -hw), -1);
                var vis = new List<VisibleTile>();
                int startFrame = -1, side = 0; int prevCorners = 0;
                var engagedPerSide = new int[6];
                double minAxis = double.MaxValue, firstAxis = double.NaN, firstToZero2 = double.NaN;
                for (int i = 0; i < log.Count; i++)
                {
                    h.Step(log[i]);
                    var w = h.S.Walk;
                    if (startFrame < 0 && w.Started) startFrame = i;
                    if (startFrame < 0) continue;
                    if (w.Corners > prevCorners) { prevCorners = w.Corners; side = Math.Min(5, w.Corners); }
                    if (w.Closed && w.Corners == 5) break;
                    WalkerPose pose = h.Kernel.Pose;
                    h.Kernel.Graph.VisibleSet(pose.Tile, 2.0, vis);
                    double3 wp = pose.Local.M.c2;
                    double best = double.MaxValue;
                    foreach (var vt in vis)
                        for (int c = 0; c < 4; c++)
                            best = Math.Min(best, Geometry2.Distance(wp, math.mul(vt.F.M, corner[c]), -1) * L);
                    if (i == startFrame)
                    {
                        firstAxis = best;
                        firstToZero2 = Geometry2.Distance(wp, corner[2], -1) * L;   // tile zero's own corner 2 (F of tile zero is the identity here)
                    }
                    minAxis = Math.Min(minAxis, best);
                    if (CurvedRoomConstants.NearFadeEngaged(best - TileChart.PostRadius)) engagedPerSide[side]++;
                }
                Assert.GreaterOrEqual(startFrame, 0);
                // five tiles share a post: the nearest one is tile zero's corner 2 whichever tile reports it
                Assert.AreEqual(firstAxis, firstToZero2, 1e-6, "the nearest post is tile zero's corner 2");
                Assert.AreEqual(0.1735, firstAxis, 0.002);
                Assert.IsTrue(CurvedRoomConstants.NearFadeEngaged(firstAxis - TileChart.PostRadius));
                for (int k = 0; k < 5; k++) Assert.GreaterOrEqual(engagedPerSide[k], 1, "side " + (k + 1) + " has an engaged frame");
                Assert.GreaterOrEqual(minAxis, 0.15, "the lap line never touches a post");
            }
        }
    }
}
