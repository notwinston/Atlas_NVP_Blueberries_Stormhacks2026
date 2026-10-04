using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using static Atlas.Spaces.CurvedWalk.Tests.ReplayLogs;

namespace Atlas.Spaces.CurvedWalk.Tests
{
    // P1-B09 (MP §9.5.6 P1-B09, §10.1.3, §10.1.4, §11.2 "holonomy", §4.6 stillness). Replayed walks step the app's shared
    // kernel (HolonomyWalkerKernel) as the frame loop does and run CurvedWalkCore, the plain logic of S2, with the real
    // registry, the real event log and the real pose-log reader; no scene and no engine are needed.
    public class DialWalkTests
    {
        sealed class CornerTrace
        {
            public readonly List<double> Distances = new List<double>();
            public bool StartedSeen; public double StartDistance = double.NaN;
            public int ClosedAt = -1; public double ClosureM = double.NaN; public double TurnAtCorner4 = double.NaN;
            int corners; bool started;
            public void Observe(int i, LapWalk w)
            {
                if (!started && w.Started) { started = true; StartedSeen = true; StartDistance = w.DistanceToFlagM; }
                if (w.Corners > corners)
                {
                    corners = w.Corners; Distances.Add(w.DistanceToFlagM);
                    if (corners == 4) TurnAtCorner4 = w.WorldTurnDeg;
                    if (w.Closed) { ClosedAt = i; ClosureM = w.DistanceToFlagM; }
                }
            }
        }

        // ---- closure ----

        // Closure_q5_Replay: "closes after 5 corners < 0.25 m" (MP §9.5.6 P1-B09, gate G2; A.4 distances 2.000, 3.047, 3.047, 2.000)
        [Test]
        public void Closure_q5_Replay()
        {
            var room = Room(RoomFixtures.Sample3x3);
            var log = Concat(Prefix(new double2(-1, -1)), Load("walk_lap5_3x3.poselog"));
            AssertQ5(room, log, 0.01);
            AssertQ5(room, Concat(Prefix(new double2(-1, -1)), Lap(5, 2, new double2(-1, -1), 0.03)), 0.05);   // brief: sway 0.03 m
        }

        static void AssertQ5(RoomModel room, List<HeadPose> log, double distTol)
        {
            using (var h = new Rig(room, 5))
            {
                var tr = new CornerTrace();
                h.Run(log, i => tr.Observe(i, h.S.Walk));
                Assert.AreEqual(5, h.Kernel.Q);
                Assert.AreEqual(Curvature.Hyperbolic, h.Kernel.Kappa);
                Assert.IsTrue(tr.StartedSeen, "Started at the flag");
                Assert.LessOrEqual(tr.StartDistance, 0.3);
                Assert.AreEqual(5, tr.Distances.Count);
                var expect = new[] { 2.000, 3.047, 3.047, 2.000 };
                for (int k = 0; k < 4; k++) Assert.AreEqual(expect[k], tr.Distances[k], distTol, "corner " + (k + 1));
                Assert.Greater(tr.ClosedAt, 0, "Closed at corner 5");
                Assert.Less(tr.ClosureM, 0.25);
                Assert.AreEqual(90.0, tr.TurnAtCorner4, 5.0, "world turn after one lap");   // MP §10.1.3, §11.2 holonomy
                Assert.AreEqual(TileId.Zero, h.Kernel.Pose.Tile, "back in the room's tile");
            }
        }

        // Closure_q4_Replay: "after 4 corners" flat
        [Test]
        public void Closure_q4_Replay()
        {
            using (var h = new Rig(Room(RoomFixtures.Sample3x3), 4))
            {
                var tr = new CornerTrace();
                h.Run(Concat(Prefix(new double2(-1, -1)), Lap(4, 2, new double2(-1, -1), 0.0)), i => tr.Observe(i, h.S.Walk));
                Assert.IsTrue(tr.StartedSeen);
                Assert.AreEqual(4, tr.Distances.Count);
                var e = new[] { 2.000, 2.828, 2.000 };
                for (int k = 0; k < 3; k++) Assert.AreEqual(e[k], tr.Distances[k], 0.01);
                Assert.Greater(tr.ClosedAt, 0); Assert.Less(tr.ClosureM, 0.25);
                Assert.AreEqual(0.0, h.S.Walk.WorldTurnDeg, 0.5);
                Assert.AreEqual(0.0, h.Kernel.CurvatureNow);
                Assert.AreEqual(1.0, h.Kernel.MetresPerUnit, "State.p[0] at the flat stand");
            }
        }

        // Closure_q3_Replay: "after 3 corners" on the same kernel configured for the sphere (S4's position of the dial), R3 = 1.27324
        [Test]
        public void Closure_q3_Replay()
        {
            var room = Room(RoomFixtures.Sample3x3);
            double r = room.Curvature.ForDial(3), s = room.LapSide;
            Assert.AreEqual(1.27324, r, 1e-5);   // RoomModel stores float32
            var kernel = new HolonomyWalkerKernel();
            kernel.Configure(KernelConfig.Curved(3, r));
            var walk = new LapWalk();
            walk.Arm(3, Curvature.Spherical, r, s, TileZeroPlacement.LapFlag(s, Curvature.Spherical, r));
            var log = Concat(Prefix(new double2(-1, -1)), Lap(3, 2, new double2(-1, -1), 0.0));
            kernel.ResetToFloor(new double2(log[0].Position.x, log[0].Position.z));
            var tr = new CornerTrace();
            for (int i = 0; i < log.Count; i++)
            {
                StepKernel(kernel, i, log[i]);
                WalkerPose pose = kernel.Pose;
                walk.Update(CubeTiling.Chain(CubeTiling.FaceOf(pose.Tile), 0), pose.Local, kernel.Still);
                tr.Observe(i, walk);
            }
            Assert.IsTrue(tr.StartedSeen);
            Assert.AreEqual(3, tr.Distances.Count);
            Assert.AreEqual(2.0, tr.Distances[0], 0.01); Assert.AreEqual(2.0, tr.Distances[1], 0.01);
            Assert.Greater(tr.ClosedAt, 0); Assert.Less(tr.ClosureM, 0.25);
            Assert.AreEqual(-90.0, walk.WorldTurnDeg, 5.0);
        }

        // Closure_q6_Replay: {4,6}, closure after 6 corners
        [Test]
        public void Closure_q6_Replay()
        {
            using (var h = new Rig(Room(RoomFixtures.Sample3x3), 6))
            {
                var tr = new CornerTrace();
                h.Run(Concat(Prefix(new double2(-1, -1)), Lap(6, 2, new double2(-1, -1), 0.0)), i => tr.Observe(i, h.S.Walk));
                Assert.AreEqual(6, h.Kernel.Q);
                Assert.AreEqual(6, h.Kernel.Graph.Q);
                Assert.IsNull(h.S.World);
                Assert.AreEqual("", h.S.ContentHash);
                Assert.IsTrue(tr.StartedSeen);
                Assert.AreEqual(6, tr.Distances.Count);
                Assert.AreEqual(2.0, tr.Distances[4], 0.02);
                Assert.Greater(tr.ClosedAt, 0); Assert.Less(tr.ClosureM, 0.25);
            }
        }

        // ---- the shared kernel ----

        // The space keeps no walker of its own: what it reads is the pose the frame loop's kernel holds, stepped once per frame.
        [Test]
        public void Kernel_IsTheAppsAndSteppedOncePerFrame()
        {
            using (var h = new Rig(Room(RoomFixtures.Sample3x3), 5))
            {
                var log = Concat(Prefix(new double2(-1, -1)), Lap(5, 2, new double2(-1, -1), 0.03));
                long before = h.Kernel.StepCount;
                h.Run(log);
                Assert.AreSame(h.Kernel, h.S.Kernel);
                Assert.AreEqual(before + log.Count, h.Kernel.StepCount, "one kernel step per frame, none by the space");
                Assert.AreEqual(h.S.LengthScale(5), h.Kernel.MetresPerUnit, 1e-12, "_HypL and State.p[0] are the kernel's");
                Assert.AreEqual(h.S.LengthScale(5), h.Kernel.ConfiguredMetresPerUnit, 1e-12);

                var st = StateMessage.Identity;
                h.S.WriteState(ref st);
                Assert.AreEqual(5f, st.GetParam(CurvedWalkCore.StateQ));
                Assert.AreEqual((float)h.Kernel.CurvatureNow, st.GetParam(CurvedWalkCore.StateCurvature));
                for (int i = 0; i < 6; i++) Assert.AreEqual(0L, st.GetTile(i), "the tile id is the frame loop's to write");
            }
        }

        // The world the Station must agree on: seed, generator version and the generator's own hash (P1-B07 path).
        [Test]
        public void World_HashIsTheGeneratorsAndFollowsTheTiling()
        {
            using (var h = new Rig(Room(RoomFixtures.Sample3x3), 5))
            {
                Assert.IsNotNull(h.S.World);
                Assert.AreEqual(CurvedWalkCore.WorldSeed, h.S.World.Seed);
                Assert.AreEqual(h.S.World.WorldHash(6).ToString("x16"), h.S.ContentHash);
                Assert.AreEqual(16, h.S.ContentHash.Length);
                Assert.AreEqual(h.S.LengthScale(5), h.S.Footprint.L, 0.0, "the world uses the target L, never the ramping L");

                var log = Load("walk_still_60s.poselog");
                int revision = h.S.WorldRevision;
                for (int i = 0; i < log.Count; i++)
                {
                    if (i == 330) h.Q.Set(6);
                    h.Step(log[i]);
                }
                Assert.AreEqual(6, h.Kernel.Q);
                Assert.AreEqual(1, h.WorldBumps, "the tiling swap bumps the world version once");
                Assert.Greater(h.S.WorldRevision, revision);
                double l6 = h.S.LengthScale(6);
                Assert.AreEqual(-1.0 / (l6 * l6), h.Kernel.CurvatureNow, 1e-12);
            }
        }

        // ---- stillness ----

        // Curvature_FrozenWhileMoving: "curvature never changes while head speed exceeds 0.1 m/s" (MP §4.6, §10.1.4)
        [Test]
        public void Curvature_FrozenWhileMoving()
        {
            using (var h = new Rig(Room(RoomFixtures.Sample3x3), 5))
            {
                var log = Lap(5, 2, new double2(-1, -1), 0.0);
                int stillChanges = 0, movingFrames = 0, prevQ = h.Kernel.Q;
                for (int i = 0; i < log.Count; i++)
                {
                    if (i == 300) h.Q.Set(6);
                    if (i == 700) h.Q.Set(4);
                    double kBefore = h.Kernel.CurvatureNow;
                    h.Step(log[i]);
                    double speed = i == 0 ? 0 : math.length(log[i].Position.xz - log[i - 1].Position.xz) / (log[i].Time - log[i - 1].Time);
                    if (speed > 0.1) { movingFrames++; Assert.AreEqual(kBefore, h.Kernel.CurvatureNow, 0.0, "frame " + i + " speed " + speed); }
                    else if (h.Kernel.CurvatureNow != kBefore) stillChanges++;
                    if (prevQ != h.Kernel.Q) { Assert.AreEqual(0.0, h.Kernel.CurvatureNow, "graph swap only at K = 0"); prevQ = h.Kernel.Q; }
                }
                Assert.Greater(movingFrames, 500);
                Assert.GreaterOrEqual(stillChanges, 1, "curvature changes on at least one still frame");
                Assert.AreEqual(0, h.Requests.Count);
            }
        }

        // Dial_LockedWhileMoving: the dial action is ignored above 0.1 m/s, honoured on a still frame (MP §10.1.4 "locked while moving")
        [Test]
        public void Dial_LockedWhileMoving()
        {
            using (var h = new Rig(Room(RoomFixtures.Sample3x3), 5))
            {
                var log = Lap(5, 2, new double2(-1, -1), 0.0);
                for (int i = 0; i < 60; i++) h.Step(log[i]);   // moving
                h.Input.DialRight = true; h.Step(log[60]); h.Input.DialRight = false;
                Assert.IsFalse(h.S.LastStill);
                Assert.AreEqual(5, h.S.SelectedQ);
                int stop = -1;
                for (int i = 61; i < log.Count; i++) { h.Step(log[i]); if (h.S.LastStill) { stop = i; break; } }
                Assert.Greater(stop, 0);
                h.Input.DialRight = true; h.Step(log[stop + 1]); h.Input.DialRight = false;
                Assert.IsTrue(h.S.LastStill);
                Assert.AreEqual(6, h.S.SelectedQ);
                Assert.AreEqual(6, h.Q.Int);
            }
        }

        // The Console's parameter is the same dial: a remote curved.q moves the target, and the curvature still only
        // moves while the walker stands.
        [Test]
        public void Dial_ConsoleParameter_MovesTheTargetOnlyWhileStill()
        {
            using (var h = new Rig(Room(RoomFixtures.Sample3x3), 5))
            {
                Assert.AreEqual("curved", h.Q.Group);
                var still = Load("walk_still_60s.poselog");
                for (int i = 0; i < 330; i++) h.Step(still[i]);
                double l5 = h.S.LengthScale(5);
                Assert.AreEqual(-1.0 / (l5 * l5), h.Kernel.CurvatureNow, 1e-12);

                // walking: the remote value is taken as the dial's snap, the curvature does not move
                var lap = Lap(1, 2, new double2(still[329].Position.x, still[329].Position.z), 0.0, 0.7, 0, still[329].Time);
                Assert.IsTrue(h.Reg.Set(CurvedWalkCore.ParamQ, 4, ParamSource.Remote));
                for (int i = 0; i < lap.Count; i++)
                {
                    h.Step(lap[i]);
                    Assert.AreEqual(-1.0 / (l5 * l5), h.Kernel.CurvatureNow, 0.0, "moving frame " + i);
                }
                Assert.AreEqual(4, h.S.SelectedQ);
                Assert.AreEqual(0.0, h.Kernel.CurvatureTarget);

                // standing again: four seconds to flat
                var last = lap[lap.Count - 1];
                var rest = Still(new double2(last.Position.x, last.Position.z), 300, last.Time + Dt);
                h.Run(rest);
                Assert.AreEqual(0.0, h.Kernel.CurvatureNow);
            }
        }

        // Ramp_Duration: 4 s of accumulated still time, linear in K (MP §10.1.4)
        [Test]
        public void Ramp_Duration()
        {
            using (var h = new Rig(Room(RoomFixtures.Sample3x3), 5))
            {
                var log = Load("walk_still_60s.poselog");
                double l5 = h.S.LengthScale(5), target = -1.0 / (l5 * l5);
                Assert.AreEqual(1.884526, l5, 1e-5);
                double stillTime = 0, reached = -1; bool reachedSet = false;
                for (int i = 0; i < log.Count; i++)
                {
                    double kBefore = h.Kernel.CurvatureNow;
                    h.Step(log[i]);
                    double k = h.Kernel.CurvatureNow;
                    if (!reachedSet && h.S.LastStill) stillTime += i > 0 ? log[i].Time - log[i - 1].Time : 0;
                    if (!reachedSet && Math.Abs(k - target) < 1e-12) { reachedSet = true; reached = stillTime; }
                    if (!reachedSet && i > 0 && k == kBefore) Assert.IsFalse(h.S.LastStill, "ramp stalled on a still frame " + i);
                    if (reachedSet) Assert.AreEqual(target, k, 1e-12, "stays at the target");
                }
                Assert.IsTrue(reachedSet);
                Assert.GreaterOrEqual(reached, 4.0 - 1e-6);
                Assert.Less(reached, 4.1);
            }
        }

        // Dial_Q3_RequestsS4: q = 3 ramps K to 0 on still frames, then asks the host for S4 exactly once (D2)
        [Test]
        public void Dial_Q3_RequestsS4()
        {
            using (var h = new Rig(Room(RoomFixtures.Sample3x3), 5))
            {
                var log = Load("walk_still_60s.poselog");
                var req = new List<(int frame, bool still, double k)>();
                h.S.RequestSwitch = id => { h.Requests.Add(id); req.Add((h.FrameNo, h.S.LastStill, h.Kernel.CurvatureNow)); return true; };
                int movedWhileWalking = -1;
                for (int i = 0; i < log.Count; i++)
                {
                    if (i == 330) { double l5 = h.S.LengthScale(5); Assert.AreEqual(-1.0 / (l5 * l5), h.Kernel.CurvatureNow, 1e-12); h.Q.Set(3); }
                    double kBefore = h.Kernel.CurvatureNow;
                    h.Step(log[i]);
                    if (h.Kernel.CurvatureNow != kBefore && !h.S.LastStill && movedWhileWalking < 0) movedWhileWalking = i;
                }
                Assert.AreEqual(-1, movedWhileWalking, "the curvature only moves on still frames");
                Assert.AreEqual(0.0, h.Kernel.CurvatureNow);
                Assert.AreEqual(1, req.Count);
                Assert.AreEqual(SpaceId.SmallPlanet, h.Requests[0]);
                Assert.IsTrue(req[0].still); Assert.AreEqual(0.0, req[0].k);
                Assert.Greater(req[0].frame, 330 + 235);
            }
        }

        // Mode 1 to Mode 2 (MP §4.8): the walker's button while standing, or the operator's kill switch, asks the host for S3;
        // the kernel still stands in the {4,5} world, which is what lets SpaceLab keep the tile id and the frame.
        [Test]
        public void Mode2_IsRequestedFromTheHost()
        {
            var log = Load("walk_still_60s.poselog");
            using (var h = new Rig(Room(RoomFixtures.Sample3x3), 5))
            {
                for (int i = 0; i < 330; i++) h.Step(log[i]);
                Assert.AreEqual(0, h.Requests.Count);
                h.Input.ModeToggle = true; h.Step(log[330]); h.Input.ModeToggle = false;
                CollectionAssert.AreEqual(new[] { SpaceId.AlignedCells }, h.Requests);
                Assert.AreEqual(5, h.Kernel.Graph.Q);
                Assert.AreEqual(Curvature.Hyperbolic, h.Kernel.Kappa);
            }
            using (var h = new Rig(Room(RoomFixtures.Sample3x3), 5))
            {
                for (int i = 0; i < 30; i++) h.Step(log[i]);
                h.Mode.Set(1, ParamSource.Remote);
                h.Step(log[30]); h.Step(log[31]);
                CollectionAssert.AreEqual(new[] { SpaceId.AlignedCells }, h.Requests, "asked once, on the change");
            }
        }

        // Standing_DialNoMarkers: "with the standing flag set the dial still snaps q = 3 to 6 and no lap markers are shown" (MP §4.4, §9.5.6).
        // The markers and the read-out are shown exactly while the walk is armed.
        [Test]
        public void Standing_DialNoMarkers()
        {
            var room = Room(RoomFixtures.TinyStanding);
            Assert.IsTrue(room.Standing);
            using (var h = new Rig(room, 5))
            {
                var log = Load("walk_still_60s.poselog");
                double l = h.S.LengthScale(5), kTarget = -1.0 / (l * l);
                Assert.AreEqual(1.4, l, 1e-6, "the comfort floor");
                var seen = new List<int>();
                var text = new StringBuilder();
                bool reachedK = false;
                for (int i = 0; i < log.Count; i++)
                {
                    h.Input = default;
                    if (i == 300) h.Input.DialLeft = true;       // 5 -> 4
                    if (i == 600) h.Input.DialLeft = true;       // 4 -> 3 (K has landed at 0 by now)
                    if (i == 610) h.Input.DialLeft = true;       // clamps at 3
                    if (i == 620 || i == 630 || i == 640) h.Input.DialRight = true;   // 4, 5, 6
                    if (i == 650) h.Input.DialRight = true;      // clamps at 6
                    h.Step(log[i]);
                    Assert.IsFalse(h.S.Walk.Armed, "no walk on a standing room, frame " + i);
                    Assert.IsFalse(WalkReadout.Build(text, h.S.Walk), "no read-out, frame " + i);
                    if (i == 299 || i == 301 || i == 601 || i == 611 || i == 621 || i == 631 || i == 641 || i == 651) seen.Add(h.S.SelectedQ);
                    if (Math.Abs(h.Kernel.CurvatureNow - kTarget) < 1e-12) reachedK = true;
                }
                Assert.IsTrue(reachedK, "q = 5 reaches K = -1/1.4^2");
                CollectionAssert.AreEqual(new[] { 5, 4, 3, 3, 4, 5, 6, 6 }, seen);
                Assert.IsTrue(h.Requests.Contains(SpaceId.SmallPlanet), "at q = 3 S4 is still requested");
                Assert.AreEqual(0, h.Count("walk.corner"));
            }
        }

        // The comfort floor (MP §4.4) is the RoomModel's; curved.strong lifts it, and the change is taken up at K = 0.
        [Test]
        public void Strong_LiftsTheComfortFloor_AtFlat()
        {
            var room = Room(RoomFixtures.Sample4x5Table);
            using (var h = new Rig(room, 5))
            {
                var log = Load("walk_still_60s.poselog");
                Assert.AreEqual(room.Curvature.FloorL, h.S.LengthScale(5), 1e-6);
                string hash = h.S.ContentHash;
                bool crossedFlat = false;
                for (int i = 0; i < log.Count; i++)
                {
                    if (i == 330) h.Strong.Set(true);
                    h.Step(log[i]);
                    if (i > 330 && h.Kernel.CurvatureNow == 0) crossedFlat = true;
                }
                Assert.IsTrue(crossedFlat, "the new L is taken up through K = 0");
                Assert.AreEqual(room.Curvature.L5, h.S.LengthScale(5), 1e-6);
                Assert.AreEqual(-1.0 / ((double)room.Curvature.L5 * room.Curvature.L5), h.Kernel.CurvatureNow, 1e-9);
                Assert.AreEqual(h.S.LengthScale(5), h.S.Footprint.L, 0.0);
                Assert.AreEqual(h.S.World.WorldHash(6).ToString("x16"), h.S.ContentHash);
                Assert.AreEqual(1, h.WorldBumps);
                Assert.IsNotNull(hash);
            }
        }

        // ---- read-outs ----

        // WalkEvents_Logged (MP §11.2 "logged"): the walk.* events of MP §10.1.3 through the real event log
        [Test]
        public void WalkEvents_Logged()
        {
            using (var h = new Rig(Room(RoomFixtures.Sample3x3), 5))
            {
                h.Run(Concat(Prefix(new double2(-1, -1)), Load("walk_lap5_3x3.poselog")));
                Assert.AreEqual(1, h.Count("walk.lap"));
                Assert.AreEqual(5, h.Count("walk.corner"));
                Assert.AreEqual(1, h.Count("walk.closed"));
                foreach (EventMessage e in h.Events)
                    if (e.Ev == "walk.closed")
                    {
                        var m = Regex.Match(e.Data.ToJson(false), "\"closure_m\":([-0-9.]+)");
                        Assert.IsTrue(m.Success);
                        Assert.Less(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), 0.25);
                        Assert.AreEqual("S2", e.Scene);
                    }
            }
        }

        // The headset read-out (corner counter, closure error, world turn) follows the LapWalk.
        [Test]
        public void Readout_FollowsTheWalk()
        {
            using (var h = new Rig(Room(RoomFixtures.Sample3x3), 5))
            {
                var text = new StringBuilder();
                var log = Concat(Prefix(new double2(-1, -1)), Load("walk_lap5_3x3.poselog"));
                bool sawPrompt = false; int lastCorners = 0;
                for (int i = 0; i < log.Count; i++)
                {
                    h.Step(log[i]);
                    LapWalk w = h.S.Walk;
                    bool shown = WalkReadout.Build(text, w);
                    Assert.AreEqual(w.Armed, shown, "frame " + i);
                    if (!shown) { Assert.AreEqual(0, text.Length); continue; }
                    string t = text.ToString();
                    if (!w.Started) { sawPrompt = true; Assert.AreEqual("walk to the flag", t); continue; }
                    string[] lines = t.Split('\n');
                    Assert.AreEqual(3, lines.Length);
                    Assert.AreEqual("corners " + w.Corners + " / 5", lines[0]);
                    Assert.AreEqual((w.Closed ? "closure " : "flag ") + w.DistanceToFlagM.ToString("F2", CultureInfo.InvariantCulture) + " m", lines[1]);
                    Assert.AreEqual("turn " + w.WorldTurnDeg.ToString("F0", CultureInfo.InvariantCulture) + " deg", lines[2]);
                    lastCorners = w.Corners;
                }
                Assert.IsTrue(sawPrompt);
                Assert.AreEqual(5, lastCorners);
                StringAssert.StartsWith("corners 5 / 5\nclosure 0.", text.ToString());
            }
        }

        // The dial's gaze twins (MP §0.5 rule 5) stay where they are while the walker looks at them, and follow a turn.
        [Test]
        public void Chips_StayPutUnderTheGaze_AndFollowATurn()
        {
            var chips = new CurvedWalkChips();
            float3 head = new float3(0.4f, 1.6f, -0.2f);
            Assert.IsTrue(chips.Follow(quaternion.identity));
            float3 left = chips.Centre(head, CurvedWalkChips.DialLeft), right = chips.Centre(head, CurvedWalkChips.DialRight);
            Assert.AreEqual(head.x - 0.55f, left.x, 1e-6f); Assert.AreEqual(head.z + 1f, left.z, 1e-6f);
            Assert.AreEqual(head.x + 0.55f, right.x, 1e-6f);

            // looking straight at either dial chip turns the head by atan(0.55): the frame does not move
            float toChip = math.atan2(0.55f, 1f);
            Assert.Less(toChip, CurvedWalkChips.FollowAngle);
            Assert.IsFalse(chips.Follow(quaternion.RotateY(toChip)));
            Assert.IsFalse(chips.Follow(quaternion.RotateY(-toChip)));
            Assert.AreEqual(0f, chips.Yaw);

            // a quarter turn: the frame follows, and the chips are to the walker's left and right again
            Assert.IsTrue(chips.Follow(quaternion.RotateY(math.PI / 2)));
            Assert.AreEqual(math.PI / 2, chips.Yaw, 1e-6f);
            left = chips.Centre(head, CurvedWalkChips.DialLeft);
            Assert.AreEqual(head.x + 1f, left.x, 1e-5f); Assert.AreEqual(head.z + 0.55f, left.z, 1e-5f);
            Assert.IsFalse(chips.Follow(math.mul(quaternion.RotateY(math.PI / 2), quaternion.RotateX(-math.PI / 2))), "looking straight up has no heading");
        }

        [Test]
        public void Params_AreDeclaredOnce_InTheRealRegistry()
        {
            var reg = new ParamRegistry();
            CurvedWalkCore.DeclareParams(reg);
            ParamEntry q = reg.Find(CurvedWalkCore.ParamQ), strong = reg.Find(CurvedWalkCore.ParamStrong);
            Assert.IsNotNull(q); Assert.IsNotNull(strong);
            Assert.AreEqual(SpaceIds.ParamGroup(SpaceId.CurvedWalk), q.Group);
            Assert.AreEqual(5, q.Int);
            q.Set(6);
            CurvedWalkCore.DeclareParams(reg);   // every Configure declares again: the same entry, the value kept
            Assert.AreSame(q, reg.Find(CurvedWalkCore.ParamQ));
            Assert.AreEqual(6, q.Int);
            Assert.IsNull(reg.Find("curved.cullRho"), "curved.cullRho is the app's declaration (AppParams), bound to the kernel");
        }
    }
}
