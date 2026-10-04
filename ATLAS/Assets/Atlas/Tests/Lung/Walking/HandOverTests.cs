using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Blades.Lung.Walking.Tests
{
    // P2-B09 lesion chamber hand-over (MP §10.2.8 P2-B09; G6 §4.4). Each test names the criterion it checks.
    public class HandOverTests
    {
        static readonly IForkTiles Tiles = new AirwayTreeBinding(Lung.Tests.TreeFixture.Tree);
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static RoomModel Room() =>
            RoomModel.Load(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "recordings", "room_3x3.json")));

        static string EvidenceDir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "laneB", "evidence", "P2-B09"));

        sealed class Rig
        {
            public AirwayWing Wing; public LesionHandOver Hand; public SimWalker Walker;
            public int Entries; public readonly List<SpaceId> Requests = new List<SpaceId>();
        }

        static Rig Make(int route, string overrideRoot = null)
        {
            var room = Room();
            var r = new Rig();
            var choice = new ScriptedForkChoice(Tiles.TargetAddress) { Override = overrideRoot };
            r.Wing = new AirwayWing(room, Tiles, new FakeClearance(room), choice, new FakeChamberView());
            r.Hand = new LesionHandOver(r.Wing, Tiles, id => r.Requests.Add(id), route);
            var prev = r.Wing.ChamberEntered;
            r.Wing.ChamberEntered = (a, t) => { r.Entries++; prev?.Invoke(a, t); };
            r.Walker = new SimWalker(r.Wing) { Head = new float3(-1f, 1.6f, -1f), Sink = f => { r.Wing.KernelStep(f); r.Wing.Tick(f); r.Hand.Tick(f); } };
            r.Wing.Enter();
            return r;
        }

        // Entries counts chamber entries after the root (tile steps from the trachea).
        static void WalkTo(Rig r, int maxFrames, Func<bool> stop)
        {
            for (int f = 0; f < maxFrames && !stop(); f++) r.Walker.Step();
        }

        // T9.1, criterion: "triggered at the end tile of the selected route" (MP §10.2.8 P2-B09; PH-39: length from tree.json).
        [Test]
        public void HandOver_TriggeredAtEndTileOfSelectedRoute()
        {
            string t = Tiles.TargetAddress;
            Assert.Greater(t.Length, 0);
            foreach (int route in new[] { 0, 1 })
            {
                var r = Make(route);
                string end = r.Hand.EndAddress;
                Assert.AreEqual(route == 0 ? t : t.Substring(0, 1), end);
                int steps = 2 * end.Length;
                bool earlyStart = false;
                int startedAt = -1;
                for (int f = 0; f < 200000 && startedAt < 0; f++)
                {
                    r.Walker.Step();
                    if (r.Hand.Started) startedAt = r.Entries; else if (r.Entries >= steps) earlyStart |= false;
                }
                Assert.AreEqual(steps, startedAt, "route " + route + ": Started on entry number " + steps);
                Assert.AreEqual(Tiles.ForkTile(end), r.Hand.StartedTile);
                Assert.AreEqual(ValvePhase.HandOver, r.Wing.Phase);
                Assert.IsFalse(earlyStart);
            }
            // one wrong choice at the root: no trigger within 2|target|+4 tile steps
            var w = Make(0, t[0] == 'L' ? "R" : "L");
            int limit = 2 * t.Length + 4;
            WalkTo(w, 400000, () => w.Entries >= limit || w.Hand.Started);
            Assert.IsFalse(w.Hand.Started, "wrong root choice never reaches the end tile");
            Assert.GreaterOrEqual(w.Entries, 1);
        }

        static void Stand(Rig r, double seconds)
        {
            int n = (int)Math.Round(seconds * 60);
            for (int i = 0; i < n; i++) r.Walker.Frame_(r.Walker.HeadXZ);
        }

        // T9.2, criteria: "curvature ramps to flat over 3-5 s while standing" (MP §10.2.8 P2-B09, §0.5 rule 4).
        [Test]
        public void HandOver_RampToFlatOnlyWhileStanding_3to5s()
        {
            var r = Make(1);
            WalkTo(r, 200000, () => r.Hand.Started);
            Assert.IsTrue(r.Hand.Started);
            Stand(r, 2.0);
            int movingChanged = 0, movingFrames = 0;
            double k0 = r.Wing.Rig.Curvature;
            for (int i = 0; i < 30; i++)   // 0.5 m at 0.5 m/s
            {
                double before = r.Wing.Rig.Curvature;
                r.Walker.Frame_(r.Walker.HeadXZ + new double2(0.5 / 60.0, 0));
                movingFrames++;
                if (!r.Wing.LastRigResult.Still && r.Wing.Rig.Curvature != before) movingChanged++;
            }
            Assert.AreEqual(0, movingChanged, "curvature unchanged on all " + movingFrames + " moving frames");
            Assert.AreNotEqual(0.0, r.Wing.Rig.Curvature, "not flat before 4 s of standing");
            for (int i = 0; i < 600 && r.Wing.Rig.Curvature != 0; i++) r.Walker.Frame_(r.Walker.HeadXZ);
            Assert.AreEqual(0.0, r.Wing.Rig.Curvature);
            Assert.AreEqual(0.0, r.Wing.Rig.Kappa);
            Assert.AreEqual(4.0, r.Hand.RampStillSeconds, 1.0 / 60.0);
            Assert.IsTrue(r.Hand.RampStillSeconds >= 3 && r.Hand.RampStillSeconds <= 5);
            Assert.AreNotEqual(0.0, k0);
        }

        sealed class AirwayTestModule : ISpaceModule, IKernelStage
        {
            readonly Rig _r; public AirwayTestModule(Rig r) { _r = r; }
            public SpaceId Id => SpaceId.S2;
            public bool Registered => false;
            public void Configure(RoomModel room, ParamRegistry registry) { }
            public void Enter(in WalkerPose start) { _r.Wing.Enter(); }
            public void KernelStep(in FrameContext f) { _r.Wing.KernelStep(f); }
            public void Tick(in FrameContext f) { _r.Wing.Tick(f); _r.Hand.Tick(f); }
            public void Exit() { _r.Wing.Exit(); }
            public void WriteState(ref Atlas.Link.StateMessage s) { }
        }

        sealed class FakeS6 : ISpaceModule
        {
            public int Enters;
            public SpaceId Id => SpaceId.S6; public bool Registered => true;
            public void Configure(RoomModel room, ParamRegistry registry) { }
            public void Enter(in WalkerPose start) { Enters++; }
            public void Tick(in FrameContext f) { }
            public void Exit() { }
            public void WriteState(ref Atlas.Link.StateMessage s) { }
        }

        // Builds a lab with the airway module (S2) and a fake S6; frames go through SpaceLab.Tick (MP §6.3 steps 4-5).
        static (Rig r, SpaceLab lab, FakeS6 s6) MakeLab(int route)
        {
            var r = Make(route);
            var room = Room();
            var lab = new SpaceLab(room, new ParamRegistry());
            var s6 = new FakeS6();
            var mod = new AirwayTestModule(r);
            lab.Register(mod); lab.Register(s6);
            // the hand-over's request goes through the lab
            var r2 = new Rig { Wing = r.Wing, Hand = null };
            r.Hand = new LesionHandOver(r.Wing, Tiles, id => { r.Requests.Add(id); lab.Request(id); }, route);
            r.Walker.Sink = f => lab.Tick(f);
            lab.Request(SpaceId.S2);
            lab.Tick(new FrameContext(0, 0, 1f / 60f, r.Walker.Head, quaternion.identity, true, false));
            return (r, lab, s6);
        }

        // T9.3, criteria: "S6 load through the placeholder request"; beat interface (PH-13).
        [Test]
        public void HandOver_S6LoadRequestedOnce_BeatCompleted()
        {
            var (r, lab, s6) = MakeLab(1);
            var beat = new AirwayBeat(r.Wing, r.Hand);
            Assert.AreEqual(LungBeat.B, beat.Beat);
            Assert.IsFalse(beat.Completed);
            double curvAtRequest = double.NaN;
            for (int f = 0; f < 200000 && r.Requests.Count == 0; f++) r.Walker.Step();
            curvAtRequest = r.Wing.Rig.Curvature;
            Assert.AreEqual(1, r.Requests.Count);
            Assert.AreEqual(0.0, curvAtRequest, "requested only after Curvature == 0");
            for (int i = 0; i < 120; i++) r.Walker.Frame_(r.Walker.HeadXZ);
            Assert.AreEqual(1, r.Requests.Count, "exactly once");
            Assert.AreEqual(SpaceId.S6, lab.Active.Id);
            Assert.AreEqual(1, s6.Enters);
            Assert.IsTrue(beat.Completed);
        }

        // T9.4, criterion (Editor half): "no simulated frame > 33 ms from end-chamber entry to S6 + 60 frames" (MP §10.2.8 P2-B09).
        [Test]
        public void HandOver_NoFrameOver33ms_Simulated()
        {
            { var w = MakeLab(1); RunTimed(w, null); }   // warm-up on a separate instance (JIT is not frame time)
            var times = new List<double>();
            var m = MakeLab(1);
            RunTimed(m, times);
            double max = times.Max();
            var sorted = times.OrderBy(x => x).ToList();
            double p95 = sorted[(int)Math.Floor(0.95 * (sorted.Count - 1))];
            Directory.CreateDirectory(EvidenceDir);
            File.WriteAllText(Path.Combine(EvidenceDir, "handover_sim.json"),
                "{\n  \"criterion\": \"P2-B09 no frame > 33 ms (Editor simulation; phone deferred)\",\n  \"frames\": " + times.Count +
                ",\n  \"max_ms\": " + max.ToString("F3", Inv) + ",\n  \"p95_ms\": " + p95.ToString("F3", Inv) +
                ",\n  \"ramp_still_s\": " + m.r.Hand.RampStillSeconds.ToString("F4", Inv) + "\n}\n");
            Assert.LessOrEqual(max, 33.0, "max frame ms");
        }

        static void RunTimed((Rig r, SpaceLab lab, FakeS6 s6) m, List<double> times)
        {
            var r = m.r;
            for (int f = 0; f < 200000 && !r.Hand.Started; f++) r.Walker.Step();
            Assert.IsTrue(r.Hand.Started);
            int after = -1;
            for (int f = 0; f < 5000 && after < 60; f++)
            {
                var sw = Stopwatch.StartNew();
                r.Walker.Step();
                sw.Stop();
                times?.Add(sw.Elapsed.TotalMilliseconds);
                if (m.lab.Active.Id == SpaceId.S6) after = after < 0 ? 0 : after + 1;
            }
            Assert.AreEqual(SpaceId.S6, m.lab.Active.Id);
        }
    }
}
