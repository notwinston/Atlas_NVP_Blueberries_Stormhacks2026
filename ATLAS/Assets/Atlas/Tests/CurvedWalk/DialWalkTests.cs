using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Spaces.SmallPlanet;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using static Atlas.Spaces.CurvedWalk.Tests.ReplayLogs;

namespace Atlas.Spaces.CurvedWalk.Tests
{
    // P1-B09 (MP §9.5.6 P1-B09, §10.1.3, §10.1.4, §11.2 "holonomy", §4.6 stillness). Replayed walks run through CurvedWalkSpace,
    // the plain logic of S2, with the registry and a test action source; no scene is needed.
    public class DialWalkTests
    {
        sealed class Harness
        {
            public readonly CurvedWalkSpace S = new CurvedWalkSpace();
            public readonly ParamRegistry Reg = new ParamRegistry();
            public readonly TestActions Actions = new TestActions();
            public long FrameNo;
            public ushort QId;
            public Harness(RoomModel room, int q = 5, bool strong = false)
            {
                S.Configure(room, Reg);
                Reg.TryGetId(CurvedWalkSpace.ParamQ, out QId);
                if (strong) { Reg.TryGetId(CurvedWalkSpace.ParamStrong, out ushort sid); Reg.Set(sid, 1); S.Configure(room, Reg); }
                Reg.Set(QId, q);
                S.Enter();
            }
            public void Step(in Sample s)
            {
                var f = ReplayLogs.Frame(FrameNo++, s);
                S.KernelStep(f);
                S.Tick(f, Actions);
            }
        }

        static void Run(Harness h, List<Sample> samples, Action<int, Sample> after = null)
        {
            for (int i = 0; i < samples.Count; i++) { var s = samples[i]; h.Step(s); after?.Invoke(i, s); }
        }

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

        // Closure_q5_Replay: "closes after 5 corners < 0.25 m" (MP §9.5.6 P1-B09; A.4 distances 2.000, 3.047, 3.047, 2.000)
        [Test]
        public void Closure_q5_Replay()
        {
            var room = Room("room_3x3.json");
            var log = Concat(Prefix(new double2(-1, -1)), Load("walk_lap5_3x3.poselog"));
            AssertQ5(room, log, 0.01);
            AssertQ5(room, Concat(Prefix(new double2(-1, -1)), Lap(5, 2, new double2(-1, -1), 0.03)), 0.05);   // brief: sway 0.03 m
        }

        static void AssertQ5(RoomModel room, List<Sample> log, double distTol)
        {
            var h = new Harness(room, 5);
            var tr = new CornerTrace();
            Run(h, log, (i, s) => tr.Observe(i, h.S.Walk));
            Assert.IsTrue(tr.StartedSeen, "Started at the flag");
            Assert.LessOrEqual(tr.StartDistance, 0.3);
            Assert.AreEqual(5, tr.Distances.Count);
            var expect = new[] { 2.000, 3.047, 3.047, 2.000 };
            for (int k = 0; k < 4; k++) Assert.AreEqual(expect[k], tr.Distances[k], distTol, "corner " + (k + 1));
            Assert.Greater(tr.ClosedAt, 0, "Closed at corner 5");
            Assert.Less(tr.ClosureM, 0.25);
            Assert.AreEqual(90.0, tr.TurnAtCorner4, 5.0, "world turn after one lap");   // MP §10.1.3, §11.2 holonomy
        }

        // Closure_q4_Replay: "after 4 corners" flat
        [Test]
        public void Closure_q4_Replay()
        {
            var room = Room("room_3x3.json");
            var h = new Harness(room, 4);
            var tr = new CornerTrace();
            Run(h, Concat(Prefix(new double2(-1, -1)), Lap(4, 2, new double2(-1, -1), 0.0)), (i, s) => tr.Observe(i, h.S.Walk));
            Assert.IsTrue(tr.StartedSeen);
            Assert.AreEqual(4, tr.Distances.Count);
            var e = new[] { 2.000, 2.828, 2.000 };
            for (int k = 0; k < 3; k++) Assert.AreEqual(e[k], tr.Distances[k], 0.01);
            Assert.Greater(tr.ClosedAt, 0); Assert.Less(tr.ClosureM, 0.25);
            Assert.AreEqual(0.0, h.S.Walk.WorldTurnDeg, 0.5);
            Assert.AreEqual(0.0, h.S.Rig.Curvature);
        }

        // Closure_q3_Replay: "after 3 corners" through the S4 module (SmallPlanetSpace + LapWalk), R3 = 1.27324
        [Test]
        public void Closure_q3_Replay()
        {
            var room = Room("room_3x3.json");
            var mod = new SmallPlanetModule { SharedMaterial = TestMaterial() };
            var reg = new ParamRegistry();
            mod.Configure(room, reg);
            Assert.AreEqual(1.27324, mod.RadiusM, 1e-6);   // RoomModel stores float32
            mod.Enter(new WalkerPose { Tile = TileId.Zero, Local = Iso2.Identity });
            try
            {
                var log = Concat(Prefix(new double2(-1, -1)), Lap(3, 2, new double2(-1, -1), 0.0));
                var tr = new CornerTrace();
                long fr = 0;
                for (int i = 0; i < log.Count; i++)
                {
                    var f = ReplayLogs.Frame(fr++, log[i]);
                    mod.KernelStep(f); mod.Tick(f);
                    tr.Observe(i, mod.Walk);
                }
                Assert.IsTrue(tr.StartedSeen);
                Assert.AreEqual(3, tr.Distances.Count);
                Assert.AreEqual(2.0, tr.Distances[0], 0.01); Assert.AreEqual(2.0, tr.Distances[1], 0.01);
                Assert.Greater(tr.ClosedAt, 0); Assert.Less(tr.ClosureM, 0.25);
                Assert.AreEqual(-90.0, mod.Walk.WorldTurnDeg, 5.0);
            }
            finally { mod.Exit(); }
        }

        // Closure_q6_Replay: {4,6}, closure after 6 corners
        [Test]
        public void Closure_q6_Replay()
        {
            var room = Room("room_3x3.json");
            var h = new Harness(room, 6);
            var tr = new CornerTrace();
            Run(h, Concat(Prefix(new double2(-1, -1)), Lap(6, 2, new double2(-1, -1), 0.0)), (i, s) => tr.Observe(i, h.S.Walk));
            Assert.AreEqual(6, h.S.Graph.Q);
            Assert.IsNull(h.S.World);
            Assert.IsTrue(tr.StartedSeen);
            Assert.AreEqual(6, tr.Distances.Count);
            Assert.AreEqual(2.0, tr.Distances[4], 0.02);
            Assert.Greater(tr.ClosedAt, 0); Assert.Less(tr.ClosureM, 0.25);
        }

        // ---- stillness ----

        // Curvature_FrozenWhileMoving: "curvature never changes while head speed exceeds 0.1 m/s" (MP §4.6, §10.1.4)
        [Test]
        public void Curvature_FrozenWhileMoving()
        {
            var room = Room("room_3x3.json");
            var h = new Harness(room, 5);
            var log = Lap(5, 2, new double2(-1, -1), 0.0);
            int requests = 0; h.S.SpaceRequested += _ => requests++;
            int stillChanges = 0, movingFrames = 0;
            TileGraph prevGraph = h.S.Graph;
            for (int i = 0; i < log.Count; i++)
            {
                if (i == 300) h.Reg.Set(h.QId, 6);
                if (i == 700) h.Reg.Set(h.QId, 4);
                double kBefore = h.S.Rig.Curvature;
                h.Step(log[i]);
                double speed = i == 0 ? 0 : math.length(new double2(log[i].X - log[i - 1].X, log[i].Z - log[i - 1].Z)) / (log[i].T - log[i - 1].T);
                if (speed > 0.1) { movingFrames++; Assert.AreEqual(kBefore, h.S.Rig.Curvature, 0.0, "frame " + i + " speed " + speed); }
                else if (h.S.Rig.Curvature != kBefore) stillChanges++;
                if (!ReferenceEquals(prevGraph, h.S.Graph)) { Assert.AreEqual(0.0, h.S.Rig.Curvature, "graph swap only at K = 0"); prevGraph = h.S.Graph; }
            }
            Assert.Greater(movingFrames, 500);
            Assert.GreaterOrEqual(stillChanges, 1, "curvature changes on at least one still frame");
            Assert.AreEqual(0, requests);
        }

        // Dial_LockedWhileMoving: D-pad ignored above 0.1 m/s, honoured on a still frame (MP §10.1.4 "locked while moving")
        [Test]
        public void Dial_LockedWhileMoving()
        {
            var h = new Harness(Room("room_3x3.json"), 5);
            var log = Lap(5, 2, new double2(-1, -1), 0.0);
            for (int i = 0; i < 60; i++) h.Step(log[i]);   // moving
            h.Actions.Next = true; h.Step(log[60]); h.Actions.Next = false;
            Assert.IsFalse(h.S.LastStill);
            Assert.AreEqual(5, h.S.SelectedQ);
            int stop = -1;
            for (int i = 61; i < log.Count; i++) { h.Step(log[i]); if (h.S.LastStill) { stop = i; break; } }
            Assert.Greater(stop, 0);
            h.Actions.Next = true; h.Step(log[stop + 1]); h.Actions.Next = false;
            Assert.IsTrue(h.S.LastStill);
            Assert.AreEqual(6, h.S.SelectedQ);
            Assert.AreEqual(6f, h.Reg.Get(h.QId));
        }

        // Ramp_Duration: 4 s of accumulated still time, linear in K (MP §10.1.4)
        [Test]
        public void Ramp_Duration()
        {
            var room = Room("room_3x3.json");
            var h = new Harness(room, 5);
            var log = Load("walk_still_60s.poselog");
            double l5 = h.S.Resolve(5).LengthScaleM, target = -1.0 / (l5 * l5);
            Assert.AreEqual(1.884481, l5, 1e-6);
            double stillTime = 0, reached = -1; bool reachedSet = false;
            for (int i = 0; i < log.Count; i++)
            {
                double kBefore = h.S.Rig.Curvature;
                h.Step(log[i]);
                double k = h.S.Rig.Curvature;
                if (!reachedSet && h.S.LastStill) stillTime += i > 0 ? log[i].T - log[i - 1].T : 0;
                if (!reachedSet && Math.Abs(k - target) < 1e-12) { reachedSet = true; reached = stillTime; }
                if (!reachedSet && i > 0 && k == kBefore) Assert.IsFalse(h.S.LastStill, "ramp stalled on a still frame " + i);
                if (reachedSet) Assert.AreEqual(target, k, 1e-12, "stays at the target");
            }
            Assert.IsTrue(reachedSet);
            Assert.GreaterOrEqual(reached, 4.0 - 1e-6);
            Assert.Less(reached, 4.1);
        }

        // Dial_Q3_RequestsS4: q = 3 ramps K to 0 on still frames, then asks for S4 exactly once (D2)
        [Test]
        public void Dial_Q3_RequestsS4()
        {
            var h = new Harness(Room("room_3x3.json"), 5);
            var log = Load("walk_still_60s.poselog");
            var req = new List<(int frame, bool still, double k)>();
            h.S.SpaceRequested += id => req.Add(((int)h.FrameNo, h.S.LastStill, h.S.Rig.Curvature));
            bool stillOnlyChange = true;
            for (int i = 0; i < log.Count; i++)
            {
                if (i == 330) { double l5 = h.S.Resolve(5).LengthScaleM; Assert.AreEqual(-1.0 / (l5 * l5), h.S.Rig.Curvature, 1e-12); h.Reg.Set(h.QId, 3); }
                double kBefore = h.S.Rig.Curvature;
                h.Step(log[i]);
                if (h.S.Rig.Curvature != kBefore && !h.S.LastStill) stillOnlyChange = false;
            }
            Assert.IsTrue(stillOnlyChange);
            Assert.AreEqual(0.0, h.S.Rig.Curvature);
            Assert.AreEqual(1, req.Count);
            Assert.IsTrue(req[0].still); Assert.AreEqual(0.0, req[0].k);
            Assert.Greater(req[0].frame, 330 + 235);
        }

        // Standing_DialNoMarkers: "with the standing flag set the dial still snaps q = 3 to 6 and no lap markers are shown" (MP §4.4, §9.5.6)
        [Test]
        public void Standing_DialNoMarkers()
        {
            var room = StandingRoom();
            var mod = new CurvedWalkModule { SharedMaterial = TestMaterial(), Actions = new TestActions() };
            var act = (TestActions)mod.Actions;
            var reg = new ParamRegistry();
            mod.Configure(room, reg);
            mod.Enter(new WalkerPose { Tile = TileId.Zero, Local = Iso2.Identity });
            var requested = new List<SpaceId>(); mod.SpaceRequested += requested.Add;
            try
            {
                reg.TryGetId(CurvedWalkSpace.ParamQ, out ushort qid);
                var log = Load("walk_still_60s.poselog");
                double l14 = 1.4, kTarget = -1.0 / (l14 * l14);
                var seen = new List<int>();
                bool reachedK = false;
                for (int i = 0; i < log.Count; i++)
                {
                    act.Prev = act.Next = false;
                    if (i == 300) act.Prev = true;       // 5 -> 4
                    if (i == 600) act.Prev = true;       // 4 -> 3 (K has landed at 0 by now)
                    if (i == 610) act.Prev = true;       // clamps at 3
                    if (i == 620 || i == 630 || i == 640) act.Next = true;   // 4, 5, 6
                    if (i == 650) act.Next = true;       // clamps at 6
                    var f = ReplayLogs.Frame(i, log[i]);
                    mod.KernelStep(f); mod.Tick(f); mod.Upload(f);
                    Assert.IsFalse(mod.Space.Walk.Armed, "no walk on a standing room, frame " + i);
                    Assert.IsFalse(mod.Markers.Visible, "no lap markers, frame " + i);
                    if (i > 0 && (i == 299 || i == 301 || i == 601 || i == 611 || i == 621 || i == 631 || i == 641 || i == 651)) seen.Add(mod.Space.SelectedQ);
                    if (Math.Abs(mod.Space.Rig.Curvature - kTarget) < 1e-12) reachedK = true;
                }
                Assert.IsTrue(reachedK, "q = 5 reaches K = -1/1.4^2");
                CollectionAssert.AreEqual(new[] { 5, 4, 3, 3, 4, 5, 6, 6 }, seen);
                Assert.IsTrue(requested.Contains(SpaceId.S4), "at q = 3 S4 is still requested");
            }
            finally { mod.Exit(); }
        }

        static RoomModel StandingRoom()
        {
            var room = Room("room_3x3.json");
            room.freeRect = new FreeRect { Wf = 1.4f, Df = 1.4f };
            room.standing = true;
            room.lapSquare = new LapSquare { s = 1.0f };
            room.curvature = new Curvature { L5 = 0.94226f, L6 = 0.75930f, R3 = 0.63662f };
            return room;
        }

        // WalkEvents_Logged (MP §11.2 "logged"): the walk.* events of MP §10.1.3
        [Test]
        public void WalkEvents_Logged()
        {
            using (var ev = new Events())
            {
                var h = new Harness(Room("room_3x3.json"), 5);
                Run(h, Concat(Prefix(new double2(-1, -1)), Load("walk_lap5_3x3.poselog")));
                Assert.AreEqual(1, ev.Count("walk.lap"));
                Assert.AreEqual(5, ev.Count("walk.corner"));
                Assert.AreEqual(1, ev.Count("walk.closed"));
                foreach (var e in ev.List)
                    if (e.ev == "walk.closed")
                    {
                        var m = Regex.Match(e.data, "\"closure_m\":([-0-9.]+)");
                        Assert.IsTrue(m.Success);
                        Assert.Less(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), 0.25);
                        Assert.AreEqual("S2", e.scene);
                    }
            }
        }

        public static Material TestMaterial()
        {
            var sh = Shader.Find("Atlas/AtlasSurface");
            Assert.IsNotNull(sh, "Atlas/AtlasSurface shader");
            return new Material(sh) { hideFlags = HideFlags.DontSave };
        }
    }
}
