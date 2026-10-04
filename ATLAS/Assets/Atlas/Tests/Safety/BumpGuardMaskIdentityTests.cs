using System;
using System.Collections.Generic;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Safety.Tests
{
    /// <summary>
    /// BumpGuard_Mask_EmptyIsIdentity (hands design sections 7.1 and 9.1): teaching Bump Guard about the walker's
    /// hands may not change anything it does when there are none. With an empty mask, every output of the depth
    /// step, every cell of the dynamic layer and the level sequence are bit for bit what they were before, on the
    /// recorded walks the suite already replays and on two of its scripted approaches.
    /// <para>"Before" is held two ways. <see cref="BumpDepthKernelBeforeMask"/> is the depth step copied from the
    /// commit before the mask; it runs beside the real one on the same machine, frame by frame, so the
    /// comparison is exact wherever the test runs. And the fingerprints below were recorded by running this file
    /// on that commit: they also cover the code the two runs share (the step, the clearance query, the sweep).</para>
    /// </summary>
    [TestFixture]
    public unsafe class BumpGuardMaskIdentityTests
    {
        /// <summary>The walks: the two recordings as the suite replays them, the same with things the scan never
        /// saw and an unmasked hand in front of the camera, and two approaches of BumpGuard_Replay.</summary>
        public enum Scene { Lap, LapFurnished, WallApproach, WallApproachFurnished, ChairAtOneMetrePerSecond, StoolAtHalfAMetrePerSecond }

        struct Pose
        {
            public float3 Position;
            public quaternion Rotation;
            public bool TrackingNormal;
        }

        // Every bit that goes in changes the value: 64-bit FNV-1a over 32-bit words.
        struct Fingerprint
        {
            ulong _hash;
            bool _started;

            public ulong Value { get { return _started ? _hash : 14695981039346656037UL; } }

            public void Add(int word)
            {
                if (!_started)
                {
                    _hash = 14695981039346656037UL;
                    _started = true;
                }
                _hash = (_hash ^ (uint)word) * 1099511628211UL;
            }

            public void Add(float value) { Add(BitConverter.SingleToInt32Bits(value)); }
            public void Add(bool value) { Add(value ? 1 : 0); }

            public void Add(double value)
            {
                long bits = BitConverter.DoubleToInt64Bits(value);
                Add((int)bits);
                Add((int)(bits >> 32));
            }
        }

        static float RoomHalfOf(Scene scene)
        {
            return scene == Scene.ChairAtOneMetrePerSecond || scene == Scene.StoolAtHalfAMetrePerSecond ? 3.5f : BumpRecordings.RoomHalf;
        }

        static List<Pose> PosesOf(Scene scene)
        {
            var poses = new List<Pose>(1024);
            if (scene == Scene.ChairAtOneMetrePerSecond || scene == Scene.StoolAtHalfAMetrePerSecond)
            {
                // Straight at a box 2 m into a 7 x 7 m room, from 3.5 m away, until the head reaches it.
                float speed = scene == Scene.ChairAtOneMetrePerSecond ? 1.0f : 0.5f;
                float nearFace = scene == Scene.ChairAtOneMetrePerSecond ? 2.0f - 0.225f : 2.0f - 0.175f;
                for (int k = 0; ; k++)
                {
                    float z = -1.5f + speed * k / BumpScenario.FrameRate;
                    if (z >= nearFace) break;
                    poses.Add(new Pose { Position = new float3(0f, BumpScenario.HeadHeight, z), Rotation = quaternion.identity, TrackingNormal = true });
                }
                return poses;
            }

            bool lap = scene == Scene.Lap || scene == Scene.LapFurnished;
            PoseLog log = BumpRecordings.Load(lap ? BumpRecordings.Lap : BumpRecordings.WallApproach);
            foreach (HeadPose s in log.Samples)
                poses.Add(new Pose { Position = s.Position, Rotation = s.Rotation, TrackingNormal = s.Tracking == TrackingState.Normal });
            return poses;
        }

        // What the scan never saw, by frame. The furnished walks keep the dynamic layer busy: a figure and a post
        // that are stamped and remembered, the figure taken away so that its cells are seen empty, and a hand
        // nobody has masked that stamps a trail in front of the walker and is lowered again.
        static void Stage(Scene scene, BumpScenario sim, int frame, int frames)
        {
            switch (scene)
            {
                case Scene.ChairAtOneMetrePerSecond:
                    if (frame == 0) sim.Boxes.Add(SimBox.OnFloor(0f, 2.0f, 0.45f, 0.45f, 0.90f));
                    break;
                case Scene.StoolAtHalfAMetrePerSecond:
                    if (frame == 0) sim.Boxes.Add(SimBox.OnFloor(0f, 2.0f, 0.35f, 0.35f, 0.45f));
                    break;
                case Scene.LapFurnished:
                case Scene.WallApproachFurnished:
                    if (frame == 0)
                    {
                        sim.Boxes.Add(SimBox.OnFloor(0.2f, -0.1f, 0.40f, 0.30f, 1.70f));
                        sim.Boxes.Add(SimBox.OnFloor(-0.4f, 0.5f, 0.10f, 0.10f, 1.50f));
                    }
                    if (frame == frames / 8) new SimHand(0.02f, -0.05f, 0.45f, true).AddTo(sim.Body);
                    if (frame == frames / 2) sim.Boxes.RemoveAt(0);
                    if (frame == 3 * frames / 4) sim.Body.Clear();
                    break;
            }
        }

        static void AddFrame(ref Fingerprint f, BumpGuardCore core)
        {
            DepthFrameResult r = core.LastDepth;
            for (int k = 0; k < BumpConstants.SectorCount; k++) f.Add(r.Sector(k));
            f.Add(r.WallDisagreement);
            f.Add(r.WallSamples);
            f.Add(r.Stamped);
            f.Add(r.Cleared);
            f.Add(r.Time);
            f.Add(r.MapTrusted);
            f.Add((int)core.Level);
            f.Add((int)core.RawLevel);
            f.Add(core.Direction.x);
            f.Add(core.Direction.y);
            f.Add(core.Now);
            f.Add(core.Path04);
            f.Add(core.Path08);
            f.Add(core.Speed);
            f.Add(core.DynamicCellCount);
            f.Add(core.RescanAdvised);
            f.Add(core.Diagnostics.RescanMedian);
            f.Add(core.Diagnostics.RisingEdges);
        }

        static void AddLayer(ref Fingerprint f, BumpGuardCore core)
        {
            DynamicGrid g = core.Dynamic;
            int cells = g.Spec.Width * g.Spec.Height;
            for (int k = 0; k < cells; k++)
            {
                f.Add(g.TopHeight[k]);
                f.Add(g.EmptyState[k]);
                // The time of an empty cell is whatever its last tenant left: not an output.
                if (g.TopHeight[k] > 0f) f.Add(g.LastHit[k]);
            }
            int blocks = g.BlocksX * g.BlocksZ;
            for (int k = 0; k < blocks; k++) f.Add(g.BlockCount[k]);
            f.Add(*g.ActiveCount);
        }

        static void AssertSameBits(float actual, float expected, string what)
        {
            if (BitConverter.SingleToInt32Bits(actual) != BitConverter.SingleToInt32Bits(expected))
                Assert.Fail(what + ": " + actual.ToString("R") + " is not " + expected.ToString("R"));
        }

        static void AssertSameFrame(BumpGuardCore real, BumpGuardCore before, string where)
        {
            DepthFrameResult a = real.LastDepth, b = before.LastDepth;
            for (int k = 0; k < BumpConstants.SectorCount; k++) AssertSameBits(a.Sector(k), b.Sector(k), where + " sector " + k);
            AssertSameBits(a.WallDisagreement, b.WallDisagreement, where + " wall disagreement");
            Assert.That(a.WallSamples, Is.EqualTo(b.WallSamples), where + " wall samples");
            Assert.That(a.Stamped, Is.EqualTo(b.Stamped), where + " stamped");
            Assert.That(a.Cleared, Is.EqualTo(b.Cleared), where + " cleared");
            Assert.That(a.Time, Is.EqualTo(b.Time), where + " time");
            Assert.That(a.MapTrusted, Is.EqualTo(b.MapTrusted), where + " map trusted");

            Assert.That(real.Level, Is.EqualTo(before.Level), where + " level");
            Assert.That(real.RawLevel, Is.EqualTo(before.RawLevel), where + " raw level");
            AssertSameBits(real.Direction.x, before.Direction.x, where + " direction x");
            AssertSameBits(real.Direction.y, before.Direction.y, where + " direction y");
            AssertSameBits(real.Now, before.Now, where + " now");
            AssertSameBits(real.Path04, before.Path04, where + " path(0.4 s)");
            AssertSameBits(real.Path08, before.Path08, where + " path(0.8 s)");
            AssertSameBits(real.Speed, before.Speed, where + " speed");
            Assert.That(real.RescanAdvised, Is.EqualTo(before.RescanAdvised), where + " rescan advised");
            AssertSameBits(real.Diagnostics.RescanMedian, before.Diagnostics.RescanMedian, where + " rescan median");
            Assert.That(real.Diagnostics.RisingEdges, Is.EqualTo(before.Diagnostics.RisingEdges), where + " rising edges");
        }

        static void AssertSameLayer(BumpGuardCore real, BumpGuardCore before, string where)
        {
            DynamicGrid a = real.Dynamic, b = before.Dynamic;
            Assert.That(*a.ActiveCount, Is.EqualTo(*b.ActiveCount), where + " occupied cells");
            int cells = a.Spec.Width * a.Spec.Height;
            for (int k = 0; k < cells; k++)
            {
                if (BitConverter.SingleToInt32Bits(a.TopHeight[k]) != BitConverter.SingleToInt32Bits(b.TopHeight[k]) ||
                    a.EmptyState[k] != b.EmptyState[k] || (a.TopHeight[k] > 0f && a.LastHit[k] != b.LastHit[k]))
                    Assert.Fail(where + " dynamic cell " + k + " differs");
            }
            int blocks = a.BlocksX * a.BlocksZ;
            for (int k = 0; k < blocks; k++)
                if (a.BlockCount[k] != b.BlockCount[k]) Assert.Fail(where + " block " + k + " differs");
        }

        struct Totals
        {
            public ulong Fingerprint;
            public int Frames, Stamped, Cleared, LevelChanges, CellsAtTheEnd;
        }

        // Runs the scene twice in step, the real depth step with no mask beside the one from before the mask,
        // and compares them after every frame.
        static Totals Run(Scene scene)
        {
            List<Pose> poses = PosesOf(scene);
            float half = RoomHalfOf(scene);
            var totals = new Totals { Frames = poses.Count };
            var print = new Fingerprint();
            using (var real = new BumpScenario(half, half))
            using (var before = new BumpScenario(half, half) { KernelBeforeMask = true })
            {
                for (int k = 0; k < poses.Count; k++)
                {
                    Stage(scene, real, k, poses.Count);
                    Stage(scene, before, k, poses.Count);
                    real.TrackingNormal = before.TrackingNormal = poses[k].TrackingNormal;
                    real.StepFrame(poses[k].Position, poses[k].Rotation);
                    before.StepFrame(poses[k].Position, poses[k].Rotation);

                    string where = scene + ", frame " + k + ":";
                    AssertSameFrame(real.Core, before.Core, where);
                    AssertSameLayer(real.Core, before.Core, where);
                    AddFrame(ref print, real.Core);
                    totals.Stamped += real.Core.LastDepth.Stamped;
                    totals.Cleared += real.Core.LastDepth.Cleared;
                }
                AddLayer(ref print, real.Core);
                totals.LevelChanges = real.Changes.Count;
                totals.CellsAtTheEnd = real.Core.DynamicCellCount;
                Assert.That(before.Changes.Count, Is.EqualTo(real.Changes.Count), scene + ": level changes");
                for (int c = 0; c < real.Changes.Count; c++)
                {
                    Assert.That(real.Changes[c].Time, Is.EqualTo(before.Changes[c].Time), scene + ": time of level change " + c);
                    Assert.That(real.Changes[c].To, Is.EqualTo(before.Changes[c].To), scene + ": level change " + c);
                }
            }
            totals.Fingerprint = print.Value;
            return totals;
        }

        // Recorded on commit d69e186, before Atlas.Safety knew of any mask, by this same code on .NET 8 (arm64).
        static ulong Recorded(Scene scene)
        {
            switch (scene)
            {
                case Scene.Lap: return 0x7C40B7E6766B91F2UL;
                case Scene.LapFurnished: return 0x30823AB27C50557EUL;
                case Scene.WallApproach: return 0x403715975E1E8EA8UL;
                case Scene.WallApproachFurnished: return 0xD8B111F5AC45160FUL;
                case Scene.ChairAtOneMetrePerSecond: return 0xF0B4F7101498D2D7UL;
                default: return 0x98F71060218F596CUL;
            }
        }

        [TestCase(Scene.Lap)]
        [TestCase(Scene.LapFurnished)]
        [TestCase(Scene.WallApproach)]
        [TestCase(Scene.WallApproachFurnished)]
        [TestCase(Scene.ChairAtOneMetrePerSecond)]
        [TestCase(Scene.StoolAtHalfAMetrePerSecond)]
        public void BumpGuard_Mask_EmptyIsIdentity_OnTheRecordedLaps(Scene scene)
        {
            Totals t = Run(scene);
            TestContext.WriteLine("{0}: {1} frames, {2} stamps, {3} cells cleared, {4} level changes, {5} cells at the end, fingerprint 0x{6:X16}",
                scene, t.Frames, t.Stamped, t.Cleared, t.LevelChanges, t.CellsAtTheEnd, t.Fingerprint);
            // The furnished walks and the approaches must really use the dynamic layer, or they prove little.
            if (scene != Scene.Lap && scene != Scene.WallApproach)
            {
                Assert.That(t.Stamped, Is.GreaterThan(1000), "stamps");
                Assert.That(t.LevelChanges, Is.GreaterThan(0), "level changes");
            }
            if (scene == Scene.LapFurnished || scene == Scene.WallApproachFurnished)
                Assert.That(t.Cleared, Is.GreaterThan(10), "cells seen empty");
#if ATLAS_HEADLESS
            // Floating-point library calls may differ in the last bit between runtimes, so the recorded values are
            // held against the headless run only; the comparison above holds everywhere.
            Assert.That(t.Fingerprint, Is.EqualTo(Recorded(scene)), scene + ": not what the commit before the mask produced");
#endif
        }
    }
}
