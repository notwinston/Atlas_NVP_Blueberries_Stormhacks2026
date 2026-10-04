using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Scan;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using static Atlas.Spaces.CurvedWalk.Tests.ReplayLogs;

namespace Atlas.Spaces.CurvedWalk.Tests
{
    // P1-B11: the room is tile zero (MP §4.6, §3.5, A.4, §9.5.6 P1-B11). Replays run through CurvedWalkSpace / CurvedWalkModule.
    public class TileZeroTests
    {
        sealed class Harness
        {
            public readonly CurvedWalkSpace S = new CurvedWalkSpace();
            public readonly ParamRegistry Reg = new ParamRegistry();
            public ushort QId; public long N;
            public Harness(RoomModel room, int q = 5)
            {
                S.Configure(room, Reg);
                Reg.TryGetId(CurvedWalkSpace.ParamQ, out QId);
                Reg.Set(QId, q);
                S.Enter();
            }
            public FrameContext Step(in Sample s)
            {
                var f = ReplayLogs.Frame(N++, s);
                S.KernelStep(f); S.Tick(f, null);
                return f;
            }
        }

        // Ramp_Restores_2cm: "a ramp to loop-fit L and back restores registration to 2 cm" (MP §9.5.6 P1-B11)
        [TestCase(0f, 0f)]
        [TestCase(0.6f, -0.4f)]
        public void Ramp_Restores_2cm(float dx, float dz)
        {
            var h = new Harness(Room("room_3x3.json"), 5);
            var log = Shift(Load("walk_still_60s.poselog"), dx, dz);
            double l5 = h.S.Resolve(5).LengthScaleM;
            bool reachedTarget = false;
            for (int i = 0; i < log.Count; i++)
            {
                if (i == 1200) h.Reg.Set(h.QId, 4);   // t = 20 s
                h.Step(log[i]);
                if (Math.Abs(h.S.Rig.Curvature + 1.0 / (l5 * l5)) < 1e-12) reachedTarget = true;
            }
            Assert.IsTrue(reachedTarget, "ramped to loop-fit L");
            Assert.AreEqual(0.0, h.S.Rig.Curvature);
            Assert.AreEqual(1, h.S.Reregistrations);
            Assert.LessOrEqual(h.S.LastRegistrationErrorM, 0.02);
            Assert.Less(h.S.LastRegistrationErrorM, 0.002, "expected well under the 2 cm limit");
            Assert.AreEqual(TileId.Zero, h.S.Rig.Pose.Tile);
        }

        // ReturnHome_Reregisters: return-to-Home re-registers (MP §4.6): next frame tile zero, K = 0, flat registration
        [Test]
        public void ReturnHome_Reregisters()
        {
            var room = Room("room_3x3.json");
            var home = HomeRebuild.Build(room);
            var mod = new CurvedWalkModule { SharedMaterial = DialWalkTests.TestMaterial(), Home = home };
            var reg = new ParamRegistry();
            mod.Configure(room, reg);
            mod.Enter(new WalkerPose { Tile = TileId.Zero, Local = Iso2.Identity });
            try
            {
                var log = Concat(Prefix(new double2(-1, -1)), Lap(5, 2, new double2(-1, -1), 0.03));
                long n = 0;
                for (int i = 0; i < log.Count; i++) { var f = Frame(n++, log[i]); mod.KernelStep(f); mod.Tick(f); mod.Upload(f); }
                var sp = mod.Space;
                Assert.Less(sp.Rig.Curvature, 0.0, "curved before Return Home");
                mod.Space.ReturnHome();
                var last = log[log.Count - 1];
                var fr = Frame(n++, new Sample(last.T + Dt, last.X, last.Z));
                mod.KernelStep(fr); mod.Tick(fr); mod.Upload(fr);
                Assert.AreEqual(TileId.Zero, sp.Rig.Pose.Tile);
                Assert.AreEqual(0.0, sp.Rig.Curvature); Assert.AreEqual(0.0, sp.Rig.TargetCurvature);
                Assert.AreEqual(4, sp.SelectedQ);
                Iso2 u = sp.Rig.Pose.Local;
                Assert.AreEqual(last.X, u.M.c2.x, 1e-6); Assert.AreEqual(last.Z, u.M.c2.y, 1e-6);
                Assert.AreEqual(1.0, u.M.c0.x, 1e-6); Assert.AreEqual(0.0, u.M.c0.y, 1e-6);
                Assert.AreEqual(0f, mod.RoomView.WallsRenderer.sharedMaterial.GetFloat(AtlasShaderIDs.WarpMode));
                Assert.AreEqual(0f, mod.RoomView.ObjectCount > 0 ? mod.RoomView.ObjectRenderer(0).sharedMaterial.GetFloat(AtlasShaderIDs.WarpMode) : 0f);
                Assert.IsTrue(mod.RoomView.FloorRenderer.enabled);
                Assert.IsFalse(mod.Tiles.gameObject.activeSelf, "tile renderer hidden at K = 0");
                Assert.AreEqual(0, mod.Space.Reregistrations, "Return Home is not a landing snap");
            }
            finally { mod.Exit(); }
        }

        // LapLine_PostDistance: "the lap line starts 0.17 m from a corner post of tile zero (at L = 1.88 m)" (MP A.4: 0.0920553 L)
        [TestCase("room_3x3.json", 0.1735)]
        [TestCase("budget_room.json", 0.2082)]
        public void LapLine_PostDistance(string name, double expectM)
        {
            var room = Room(name);
            double L = CurvatureDial.Target(5, room.curvature.L5, room.curvature.L6, room.curvature.R3, false).LengthScaleM;
            var flag = TileZeroPlacement.LapFlag(room.lapSquare.s, -1, L);
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
        [TestCase("room_3x3.json", 2.0)]
        [TestCase("budget_room.json", 2.4)]
        public void Lap_NoTileFlicker(string name, double s)
        {
            var room = Room(name);
            double a = -s / 2;
            var log = Concat(Prefix(new double2(a, a)), Lap(5, s, new double2(a, a), 0.03));
            var h = new Harness(room, 5);
            var tr = new TileTrace();
            for (int i = 0; i < log.Count; i++) { h.Step(log[i]); tr.Observe(h.S.Rig.Pose.Tile); }
            var g = h.S.Graph;
            Assert.AreEqual(8, tr.Changes, "tile id changes over the lap");
            var z = TileId.Zero;
            Assert.AreEqual(new[] { z, g.Neighbour(z, 3), z, g.Neighbour(z, 0), z, g.Neighbour(z, 1), z, g.Neighbour(z, 2), z }, tr.Seq.ToArray());
            Assert.GreaterOrEqual(tr.MinInterior(), 15, "every interior run is at least 15 frames (0.25 s)");

            // control: the same log with no hysteresis flickers (test-local classifier, a copy of the rig's crossing without the margin)
            if (name == "room_3x3.json")
            {
                var ctl = new TileTrace();
                var graph = new TileGraph(5);
                double L = h.S.Resolve(5).LengthScaleM;
                var tile = TileId.Zero;
                Iso2 u = TileZeroPlacement.Registered(new double2(0, 0), -1, L);
                double2 prev = new double2(log[0].X, log[0].Z); u = TileZeroPlacement.Registered(prev, -1, L);
                double ch = Math.Cosh(graph.InRadius), sh = Math.Sinh(graph.InRadius);
                for (int i = 1; i < log.Count; i++)
                {
                    double2 cur = new double2(log[i].X, log[i].Z);
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

        // Lap_PostFade: "post fade checked on a replayed lap" (MP §9.5.6 P1-B11 Output; §3.5 near-head fade 0.3 m, post radius 0.15 m)
        [Test]
        public void Lap_PostFade()
        {
            var room = Room("room_3x3.json");
            var log = Concat(Prefix(new double2(-1, -1)), Lap(5, 2, new double2(-1, -1), 0.0));
            var h = new Harness(room, 5);
            double L = h.S.Resolve(5).LengthScaleM;
            double hw = TileChart.HalfWidth(-1, 5, L);
            var corner = new double3[4];
            corner[0] = TileChart.KleinToModel(new double2(hw, hw), -1); corner[1] = TileChart.KleinToModel(new double2(-hw, hw), -1);
            corner[2] = TileChart.KleinToModel(new double2(-hw, -hw), -1); corner[3] = TileChart.KleinToModel(new double2(hw, -hw), -1);
            var vis = new List<VisibleTile>();
            int startFrame = -1, side = 0; int prevCorners = 0;
            var engagedPerSide = new int[6];
            double minAxis = double.MaxValue, firstAxis = double.NaN, firstToZero2 = double.NaN; int firstTileCorner = -1; TileId firstTile = default;
            for (int i = 0; i < log.Count; i++)
            {
                h.Step(log[i]);
                var w = h.S.Walk;
                if (startFrame < 0 && w.Started) startFrame = i;
                if (startFrame < 0) continue;
                if (w.Corners > prevCorners) { prevCorners = w.Corners; side = Math.Min(5, w.Corners); }
                if (w.Closed && w.Corners == 5) break;
                h.S.Graph.VisibleSet(h.S.Rig.Pose.Tile, 2.0, vis);
                double3 wp = h.S.Rig.Pose.Local.M.c2;
                double best = double.MaxValue; int bc = -1; TileId bt = default;
                foreach (var vt in vis)
                    for (int c = 0; c < 4; c++)
                    {
                        double d = Geometry2.Distance(wp, math.mul(vt.F.M, corner[c]), -1) * L;
                        if (d < best) { best = d; bc = c; bt = vt.Id; }
                    }
                if (i == startFrame)
                {
                    firstAxis = best; firstTileCorner = bc; firstTile = bt;
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
