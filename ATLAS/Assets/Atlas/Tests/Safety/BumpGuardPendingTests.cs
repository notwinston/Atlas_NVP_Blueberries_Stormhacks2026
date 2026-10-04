using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using Scene = Atlas.Safety.Tests.BumpGuardMaskIdentityTests.Scene;

namespace Atlas.Safety.Tests
{
    /// <summary>
    /// BumpGuard_Pending (decision R13): in a hands session a near return must be seen twice. A dynamic cell
    /// first stamped by a return nearer than 0.80 m is pending: stored, and not counted by anything that reads
    /// the layer, until a hit that comes 0.15 s or more after its first hit; a pending cell with no hit for
    /// 0.10 s starts again. So a hand that comes into view, and a hand whose mask is missing for a few images,
    /// raise no cue, and what they leave behind never counts. What it costs is measured here too: something
    /// real that is first seen close by is warned of 0.15 to 0.17 s later. Outside a session nothing changes,
    /// bit for bit. The scenes are those of the mask tests: a walker in a scanned room with a level 256 x 192
    /// depth camera, <see cref="SimHand"/> plates that only the synthetic depth sees, masks built the way the
    /// hands build them.
    /// </summary>
    [TestFixture]
    public unsafe class BumpGuardPendingTests
    {
        const float RoomHalf = 3.5f;
        const float FrameSeconds = 1f / BumpScenario.FrameRate;
        const int W = BumpScenario.DepthW, H = BumpScenario.DepthH;

        static string F(double value) { return value.ToString("F2", CultureInfo.InvariantCulture); }

        static string Show(string format, params object[] args) { return string.Format(CultureInfo.InvariantCulture, format, args); }

        // Runs every case on a thread of its own and hands the results back in order. The cases share nothing:
        // each has its own Bump Guard. Asserting and printing stay with the caller, on the test's thread.
        static TResult[] InParallel<TCase, TResult>(IReadOnlyList<TCase> cases, Func<TCase, TResult> run)
        {
            var results = new TResult[cases.Count];
            Parallel.For(0, cases.Count, n => results[n] = run(cases[n]));
            return results;
        }

        // The cells of the layer: those that count for clear(p), and those that are stored and do not.
        static void CountCells(BumpGuardCore core, out int counting, out int pending)
        {
            counting = pending = 0;
            DynamicGrid g = core.Dynamic;
            if (!g.IsCreated || *g.ActiveCount == 0) return;
            int cells = g.Spec.Width * g.Spec.Height;
            for (int k = 0; k < cells; k++)
            {
                if (!(g.TopHeight[k] > 0f)) continue;
                if (g.Pending[k] != 0) pending++;
                else counting++;
            }
        }

        // ---- 1. BumpGuard_Pending_OutsideASessionNothingChanges ----

        static readonly SimHand LeftHand = new SimHand(-0.11f, -0.05f, 0.45f, true);
        static readonly SimHand RightHand = new SimHand(0.11f, -0.05f, 0.45f, true);
        // The hand the furnished walks of the identity scenes raise themselves.
        static readonly SimHand FurnishedHand = new SimHand(0.02f, -0.05f, 0.45f, true);

        static bool Furnished(Scene scene) { return scene == Scene.LapFurnished || scene == Scene.WallApproachFurnished; }

        // A mask as the builder of commit 515d14c made it: one gate for the hand's rectangle and its strip.
        static DepthMask WithOneGate(DepthMask mask)
        {
            mask.Region0.ArmMaxDepth = 0f;
            mask.Region1.ArmMaxDepth = 0f;
            return mask;
        }

        // The hands of the masked runs, and the mask that comes with this frame's image. The furnished walks mask
        // the hand they already carry; the others raise two hands a second in. The mask comes six images after
        // the hands, is missing for three images in every fifty, and lingers nine images after they are lowered:
        // so the kernel masks, refuses, forgets and is left without a mask, all outside a session.
        static DepthMask HandsAt(Scene scene, BumpScenario real, BumpScenario before, int frame, int frames)
        {
            int up = Furnished(scene) ? frames / 8 : 60;
            bool lowered = Furnished(scene) && frame >= 3 * frames / 4 + 9;
            if (!Furnished(scene) && frame == up)
            {
                foreach (BumpScenario sim in new[] { real, before })
                {
                    LeftHand.AddTo(sim.Body);
                    RightHand.AddTo(sim.Body);
                }
            }
            if (frame < up + 6 || lowered) return default(DepthMask);
            int beat = (frame - up) % 50;
            if (beat >= 25 && beat < 28) return default(DepthMask);
            return WithOneGate(Furnished(scene) ? real.MaskFor(FurnishedHand) : real.MaskFor(LeftHand, RightHand));
        }

        struct Totals
        {
            public ulong Fingerprint;
            public int Frames, Stamped, Cleared, Masked, Forgotten, Refused, LevelChanges;
        }

        // Runs the scene twice in step, outside a session: the real depth step beside the one from before the
        // pending rule, both with the same mask, and compares them after every frame.
        static Totals RunOutsideASession(Scene scene, bool hands)
        {
            List<BumpGuardMaskIdentityTests.Pose> poses = BumpGuardMaskIdentityTests.PosesOf(scene);
            float half = BumpGuardMaskIdentityTests.RoomHalfOf(scene);
            string name = scene + (hands ? " with masked hands" : "");
            var totals = new Totals { Frames = poses.Count };
            var print = new BumpGuardMaskIdentityTests.Fingerprint();
            using (var real = new BumpScenario(half, half))
            using (var before = new BumpScenario(half, half) { KernelBeforePending = true })
            {
                for (int k = 0; k < poses.Count; k++)
                {
                    BumpGuardMaskIdentityTests.Stage(scene, real, k, poses.Count);
                    BumpGuardMaskIdentityTests.Stage(scene, before, k, poses.Count);
                    if (hands) real.Mask = before.Mask = HandsAt(scene, real, before, k, poses.Count);
                    real.TrackingNormal = before.TrackingNormal = poses[k].TrackingNormal;
                    real.StepFrame(poses[k].Position, poses[k].Rotation);
                    before.StepFrame(poses[k].Position, poses[k].Rotation);

                    // Every output and every cell; a cell that is pending in one and not in the other differs.
                    BumpCompare.Same(real.Core, before.Core, name, k);
                    DepthFrameResult a = real.Core.LastDepth, b = before.Core.LastDepth;
                    if (a.Masked != b.Masked || a.Forgotten != b.Forgotten || a.MaskRefused != b.MaskRefused)
                        Assert.Fail(name + ", frame " + k + ": masked " + a.Masked + " against " + b.Masked + ", forgotten " + a.Forgotten +
                                    " against " + b.Forgotten + ", refused " + a.MaskRefused + " against " + b.MaskRefused);

                    BumpGuardMaskIdentityTests.AddFrame(ref print, real.Core);
                    if (hands)
                    {
                        print.Add(a.Masked);
                        print.Add(a.Forgotten);
                        print.Add(a.MaskRefused);
                    }
                    totals.Stamped += a.Stamped;
                    totals.Cleared += a.Cleared;
                    totals.Masked += a.Masked;
                    totals.Forgotten += a.Forgotten;
                    if (a.MaskRefused) totals.Refused++;
                }
                BumpGuardMaskIdentityTests.AddLayer(ref print, real.Core);
                CountCells(real.Core, out _, out int pending);
                Assert.That(pending, Is.EqualTo(0), name + ": a cell is pending outside a session");
                totals.LevelChanges = real.Changes.Count;
                Assert.That(before.Changes.Count, Is.EqualTo(real.Changes.Count), name + ": level changes");
                for (int c = 0; c < real.Changes.Count; c++)
                {
                    Assert.That(real.Changes[c].Time, Is.EqualTo(before.Changes[c].Time), name + ": time of level change " + c);
                    Assert.That(real.Changes[c].To, Is.EqualTo(before.Changes[c].To), name + ": level change " + c);
                }
            }
            totals.Fingerprint = print.Value;
            return totals;
        }

        // The masked runs, recorded with this file on the kernel, the layer and the core of commit 515d14c, before
        // the pending rule was written, on .NET 8 (arm64). With no hands a run is the one of
        // BumpGuard_Mask_EmptyIsIdentity and its fingerprint is the one recorded there, on commit d69e186.
        static ulong RecordedWithHands(Scene scene)
        {
            switch (scene)
            {
                case Scene.Lap: return 0x14AEF35456CDEB9EUL;
                case Scene.LapFurnished: return 0x531F919CB741D9EBUL;
                case Scene.WallApproach: return 0xFC47D30F3AC0F95EUL;
                case Scene.WallApproachFurnished: return 0x609CD46A965C3721UL;
                case Scene.ChairAtOneMetrePerSecond: return 0xB460518F5B116342UL;
                default: return 0x372787D8D676A7EDUL;
            }
        }

        // With masked hands: the six scenes of BumpGuard_Mask_EmptyIsIdentity. With no mask that test holds the same
        // six against the kernel of before the mask, and still does; here two of them are held against the kernel
        // of before the pending rule as well, the recorded lap among them.
        [TestCase(Scene.Lap, false)]
        [TestCase(Scene.ChairAtOneMetrePerSecond, false)]
        [TestCase(Scene.Lap, true)]
        [TestCase(Scene.LapFurnished, true)]
        [TestCase(Scene.WallApproach, true)]
        [TestCase(Scene.WallApproachFurnished, true)]
        [TestCase(Scene.ChairAtOneMetrePerSecond, true)]
        [TestCase(Scene.StoolAtHalfAMetrePerSecond, true)]
        public void BumpGuard_Pending_OutsideASessionNothingChanges(Scene scene, bool hands)
        {
            Totals t = RunOutsideASession(scene, hands);
            TestContext.WriteLine("{0}{1}: {2} frames, {3} stamps, {4} cells cleared, {5} samples masked, {6} cells forgotten, mask refused in {7} frames, {8} level changes, fingerprint 0x{9:X16}",
                scene, hands ? " with masked hands" : "", t.Frames, t.Stamped, t.Cleared, t.Masked, t.Forgotten, t.Refused, t.LevelChanges, t.Fingerprint);
            if (hands)
            {
                // The mask must really be at work, or the run proves nothing about it.
                Assert.That(t.Masked, Is.GreaterThan(10000), "samples masked");
                Assert.That(t.Forgotten, Is.GreaterThan(0), "cells forgotten when the mask came");
                Assert.That(t.Stamped, Is.GreaterThan(1000), "stamps");
            }
#if ATLAS_HEADLESS
            // Floating-point library calls may differ in the last bit between runtimes, so the recorded values are
            // held against the headless run only; the comparison above holds everywhere.
            ulong recorded = hands ? RecordedWithHands(scene) : BumpGuardMaskIdentityTests.Recorded(scene);
            Assert.That(t.Fingerprint, Is.EqualTo(recorded), scene + (hands ? " with masked hands" : "") + ": not what the commit before the pending rule produced");
#endif
        }

        [Test]
        public void BumpGuard_Pending_OutsideASessionNothingChanges_WhenTheSessionEnds()
        {
            // A session that ends leaves Bump Guard as it is outside one: the next near return counts at once,
            // also where a pending cell was waiting, and a pending cell that is hit no more never counts.
            var pose = new SimPose { Position = new float2(0f, -2.48f), Yaw = 0f };
            using (var sim = new BumpScenario(RoomHalf, RoomHalf) { HandsSession = true })
            {
                sim.Hold(pose, 0.5f);
                // A post 0.31 m ahead, and a tray beside the line that will be taken away with the session.
                sim.Boxes.Add(SimBox.OnFloor(0f, -2.12f, 0.10f, 0.10f, 1.6f));
                var tray = new SimPlate(new float3(0.25f, -0.05f, 0.50f), 0.10f, 0.10f);
                sim.Body.Add(tray);
                for (int k = 0; k < 4; k++) sim.StepFrame(pose);
                CountCells(sim.Core, out int counting, out int pending);
                Assert.That(counting, Is.EqualTo(0), "four images into a session nothing near counts yet");
                Assert.That(pending, Is.GreaterThan(2));
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Clear));

                sim.HandsSession = false;
                sim.Body.Clear();
                sim.StepFrame(pose);
                CountCells(sim.Core, out counting, out pending);
                Assert.That(counting, Is.GreaterThan(0), "the post counts with the first image outside the session");
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Warn));
                Assert.That(DynamicLayer.PendingAt(sim.Core.Dynamic, new float2(0.02f, -2.16f)), Is.False);
                Assert.That(sim.Core.ClearAt(new float2(0.25f, -2.02f), sim.Time), Is.GreaterThan(0.20f), "the tray's cells never counted");

                sim.Hold(pose, 2f);
                CountCells(sim.Core, out counting, out pending);
                Assert.That(pending, Is.EqualTo(0), "what was pending and is hit no more is gone");
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Warn));
                Assert.That(sim.RisingEdges(), Is.EqualTo(1));
            }
        }

        // ---- 2. BumpGuard_Pending_HandEntryRaisesNoCue ----

        struct Entry
        {
            public BumpLevel Worst;
            public int Cues;
            public double NotClearSeconds, FirstCueAfterEntry;
            public int CellsBeforeTheMask, PendingBeforeTheMask, MostCounting, CellsAtTheEnd, CountingAtTheEnd;
        }

        // A hand comes into view half a second into the walk and its mask arrives lagImages later.
        static Entry HandEntry(bool session, float speed, float pitchDownDegrees, float depth, int lagImages, bool forearm, int tailFrames = 120)
        {
            const int raise = 30;
            using (var sim = new BumpScenario(RoomHalf, RoomHalf) { HandsSession = session })
            {
                var hand = new SimHand(0.03f, -0.05f, depth, forearm);
                quaternion rotation = quaternion.RotateX(math.radians(pitchDownDegrees));
                var e = new Entry { Worst = BumpLevel.Clear, FirstCueAfterEntry = -1.0 };
                int maskFrame = raise + lagImages;
                for (int k = 0; k < maskFrame + tailFrames; k++)
                {
                    if (k == raise) hand.AddTo(sim.Body);
                    if (k == maskFrame) sim.Mask = sim.MaskFor(hand);
                    BumpLevel before = sim.Core.Level;
                    sim.StepFrame(new float3(0f, BumpScenario.HeadHeight, -2.5f + speed * k * FrameSeconds), rotation);
                    CountCells(sim.Core, out int counting, out int pending);
                    if (k == maskFrame - 1)
                    {
                        e.CellsBeforeTheMask = counting + pending;
                        e.PendingBeforeTheMask = pending;
                    }
                    if (counting > e.MostCounting) e.MostCounting = counting;
                    if (sim.Core.Level > e.Worst) e.Worst = sim.Core.Level;
                    if (before == BumpLevel.Clear && sim.Core.Level != BumpLevel.Clear)
                    {
                        e.Cues++;
                        if (e.FirstCueAfterEntry < 0.0) e.FirstCueAfterEntry = (k - raise) * (double)FrameSeconds;
                    }
                    if (sim.Core.Level != BumpLevel.Clear) e.NotClearSeconds += FrameSeconds;
                }
                CountCells(sim.Core, out e.CountingAtTheEnd, out int left);
                e.CellsAtTheEnd = e.CountingAtTheEnd + left;
                return e;
            }
        }

        struct Motion
        {
            public string Name;
            public float Speed, PitchDown, Depth;
        }

        // The hand enters 0.45 m ahead of a walker who stands with a level head or walks. One more row: a walker
        // who stands and looks down 25 degrees at a hand 0.35 m ahead, as when pressing a near panel; on the
        // floor that hand is nearer than it is deep, and it is the standing case that warns outside a session.
        static readonly Motion[] Motions =
        {
            new Motion { Name = "standing", Speed = 0f, PitchDown = 0f, Depth = 0.45f },
            new Motion { Name = "standing, looking down 25 degrees at a hand 0.35 m ahead", Speed = 0f, PitchDown = 25f, Depth = 0.35f },
            new Motion { Name = "0.5 m/s", Speed = 0.5f, PitchDown = 0f, Depth = 0.45f },
            new Motion { Name = "0.7 m/s", Speed = 0.7f, PitchDown = 0f, Depth = 0.45f },
            new Motion { Name = "1.0 m/s", Speed = 1.0f, PitchDown = 0f, Depth = 0.45f },
        };

        struct EntryCase
        {
            public bool Forearm, AlsoOutside;
            public Motion Motion;
            public int Lag, Tail;
        }

        struct EntryPair
        {
            public Entry InSession, Outside;
        }

        // The entry in a session and, where asked, the same entry outside one.
        static EntryPair Enter(EntryCase c)
        {
            var pair = new EntryPair { InSession = HandEntry(true, c.Motion.Speed, c.Motion.PitchDown, c.Motion.Depth, c.Lag, c.Forearm, c.Tail) };
            if (c.AlsoOutside) pair.Outside = HandEntry(false, c.Motion.Speed, c.Motion.PitchDown, c.Motion.Depth, c.Lag, c.Forearm, c.Tail);
            return pair;
        }

        static string EntryRow(string what, in Motion motion, int lag, in Entry e)
        {
            return Show("{0}, {1}, mask {2,2} images late: {3,-6} {4} cue(s), not Clear for {5} s; {6} cells before the mask ({7} pending), most that counted at once {8}, left at the end {9}",
                what, motion.Name, lag, e.Worst.ToString().ToUpperInvariant(), e.Cues, F(e.NotClearSeconds), e.CellsBeforeTheMask,
                e.PendingBeforeTheMask, e.MostCounting, e.CellsAtTheEnd);
        }

        [Test]
        public void BumpGuard_Pending_HandEntryRaisesNoCue()
        {
            // The images before a hand is detected. In a session what it stamps is pending; its mask comes before
            // 0.15 s have passed, the stamping stops, and nothing ever counted: no cue, standing or walking, with
            // a forearm or without. The same entry outside a session, at the longest of the three lags, shows
            // what the session is for.
            var cases = new List<EntryCase>();
            foreach (bool forearm in new[] { false, true })
                foreach (Motion motion in Motions)
                    foreach (int lag in new[] { 3, 6, 9 })
                        cases.Add(new EntryCase { Forearm = forearm, Motion = motion, Lag = lag, Tail = 120, AlsoOutside = lag == 9 });
            EntryPair[] entries = InParallel<EntryCase, EntryPair>(cases, Enter);

            int needed = 0;
            for (int n = 0; n < cases.Count; n++)
            {
                EntryCase c = cases[n];
                Entry e = entries[n].InSession;
                string what = c.Forearm ? "hand and forearm" : "hand";
                string row = EntryRow(what, c.Motion, c.Lag, e);
                TestContext.WriteLine(row);
                Assert.That(e.Worst, Is.EqualTo(BumpLevel.Clear), row);
                Assert.That(e.Cues, Is.EqualTo(0), row);
                Assert.That(e.MostCounting, Is.EqualTo(0), "a cell of the hand counted: " + row);
                Assert.That(e.CellsBeforeTheMask, Is.GreaterThan(0), "the hand was not in the picture: " + row);
                Assert.That(e.PendingBeforeTheMask, Is.EqualTo(e.CellsBeforeTheMask), row);
                Assert.That(e.CellsAtTheEnd, Is.EqualTo(0), "something was left behind: " + row);

                if (!c.AlsoOutside) continue;
                Entry plain = entries[n].Outside;
                TestContext.WriteLine("    outside a session: " + EntryRow(what, c.Motion, c.Lag, plain));
                if (plain.Worst != BumpLevel.Clear) needed++;
                // Only standing with a level head is a hand 0.45 m ahead beyond every threshold.
                if (c.Motion.Speed > 0f || c.Motion.PitchDown > 0f)
                    Assert.That(plain.Worst, Is.GreaterThanOrEqualTo(BumpLevel.Warn), "outside a session this entry warns: " + row);
            }
            Assert.That(needed, Is.EqualTo(8), "entries that raise a cue outside a session");
        }

        [Test]
        public void BumpGuard_Pending_HandEntryRaisesNoCue_UnlessTheMaskIsLaterThanThePendingTime()
        {
            // Fifteen images, a quarter of a second: longer than a cell stays pending. Measured, and held as it is.
            // What stays over a cell for 0.15 s comes to count: the hand of a walker who stands, and at 0.5 m/s
            // the top of the forearm, which follows the hand over the same floor. A walking walker's hand alone
            // has moved on before any of its cells has been hit for 0.15 s, and so has the forearm at 0.7 m/s
            // and faster. Where a cue is raised it starts no sooner than 0.15 s after the hand came into view and
            // is shorter and no stronger than outside a session.
            var cases = new List<EntryCase>();
            foreach (bool forearm in new[] { false, true })
                foreach (Motion motion in Motions)
                    cases.Add(new EntryCase { Forearm = forearm, Motion = motion, Lag = 15, Tail = 120, AlsoOutside = true });
            EntryPair[] entries = InParallel<EntryCase, EntryPair>(cases, Enter);

            for (int n = 0; n < cases.Count; n++)
            {
                EntryCase c = cases[n];
                Entry e = entries[n].InSession, plain = entries[n].Outside;
                string what = c.Forearm ? "hand and forearm" : "hand";
                string row = EntryRow(what, c.Motion, 15, e);
                TestContext.WriteLine(row + (e.Cues > 0 ? Show("; the cue began {0} s after the hand came into view", F(e.FirstCueAfterEntry)) : ""));
                TestContext.WriteLine("    outside a session: " + EntryRow(what, c.Motion, 15, plain));

                Assert.That(e.Worst, Is.LessThanOrEqualTo(plain.Worst), row);
                Assert.That(e.NotClearSeconds, Is.LessThanOrEqualTo(plain.NotClearSeconds + 1e-9), row);
                if (e.Cues > 0) Assert.That(e.FirstCueAfterEntry, Is.GreaterThanOrEqualTo(BumpConstants.PendingSeconds - 1e-9), row);
                Assert.That(e.CellsAtTheEnd, Is.LessThanOrEqualTo(plain.CellsAtTheEnd), row);

                if (c.Motion.Speed == 0f)
                {
                    // Standing, the hand's cells have been hit for 0.15 s before the mask comes: they count.
                    Assert.That(e.MostCounting, Is.GreaterThan(0), row);
                    Assert.That(e.Worst, Is.EqualTo(plain.Worst), row);
                }
                else if (!c.Forearm || c.Motion.Speed >= 0.7f)
                {
                    Assert.That(e.Worst, Is.EqualTo(BumpLevel.Clear), row);
                    Assert.That(e.MostCounting, Is.EqualTo(0), row);
                    Assert.That(e.CellsAtTheEnd, Is.EqualTo(0), row);
                }
                else
                {
                    // The forearm at 0.5 m/s: a short WARN where there was a long STRONG, and no trail.
                    Assert.That(plain.Worst, Is.EqualTo(BumpLevel.Strong), row);
                    Assert.That(e.Worst, Is.EqualTo(BumpLevel.Warn), row);
                    Assert.That(e.Cues, Is.EqualTo(1), row);
                    Assert.That(e.NotClearSeconds, Is.LessThan(0.5), row);
                    Assert.That(e.CellsAtTheEnd, Is.EqualTo(0), row);
                }
            }
        }

        // ---- 3. BumpGuard_Pending_GapsInTheMaskRaiseNoCue ----

        struct Blink
        {
            public int CuesOut, CuesBack, MostCounting, PendingWhenLowered, CellsAtTheEnd, MaskedFrames, UnmaskedFrames;
            public BumpLevel Worst;
        }

        // Walks out for two seconds with a hand and forearm 0.45 m ahead, masked exactly from the first image,
        // except that the mask is absent for gapImages images once a second. Then the hand is lowered and the
        // walker turns round and walks back along the same line, through whatever was left there.
        static Blink WalkWithABlinkingMask(bool session, float speed, int gapImages)
        {
            const float half = 5.0f;
            const int walkFrames = 120;
            using (var sim = new BumpScenario(half, half) { HandsSession = session })
            {
                var hand = new SimHand(0.05f, -0.05f, 0.45f, true);
                hand.AddTo(sim.Body);
                DepthMask mask = sim.MaskFor(hand);
                var b = new Blink { Worst = BumpLevel.Clear };
                float z = -4.0f;
                for (int k = 0; k < walkFrames; k++)
                {
                    z = -4.0f + speed * k * FrameSeconds;
                    bool absent = k % 60 >= 30 && k % 60 < 30 + gapImages;
                    sim.Mask = absent ? default(DepthMask) : mask;
                    sim.StepFrame(new float3(0f, BumpScenario.HeadHeight, z), quaternion.identity);
                    if (absent) b.UnmaskedFrames++;
                    else if (sim.Core.LastDepth.Masked > 0) b.MaskedFrames++;
                    Note(sim, ref b);
                }
                b.CuesOut = sim.RisingEdges();
                CountCells(sim.Core, out _, out b.PendingWhenLowered);

                // The hand is lowered; the session goes on.
                sim.Body.Clear();
                sim.Mask = default(DepthMask);
                var stand = new float3(0f, BumpScenario.HeadHeight, z);
                quaternion back = quaternion.RotateY(math.PI);
                for (int k = 0; k < 20; k++)
                {
                    sim.StepFrame(stand, quaternion.identity);
                    Note(sim, ref b);
                }
                for (int k = 0; k < 10; k++)
                {
                    sim.StepFrame(stand, back);
                    Note(sim, ref b);
                }
                for (int k = 0; k < walkFrames; k++)
                {
                    sim.StepFrame(new float3(0f, BumpScenario.HeadHeight, z - speed * k * FrameSeconds), back);
                    Note(sim, ref b);
                }
                b.CuesBack = sim.RisingEdges() - b.CuesOut;
                CountCells(sim.Core, out int counting, out int pending);
                b.CellsAtTheEnd = counting + pending;
                return b;
            }
        }

        static void Note(BumpScenario sim, ref Blink b)
        {
            CountCells(sim.Core, out int counting, out _);
            if (counting > b.MostCounting) b.MostCounting = counting;
            if (sim.Core.Level > b.Worst) b.Worst = sim.Core.Level;
        }

        struct BlinkCase
        {
            public bool Session;
            public float Speed;
            public int Gap;
        }

        [Test]
        public void BumpGuard_Pending_GapsInTheMaskRaiseNoCue()
        {
            // A detection missed, a confidence that dips: the mask is gone for a few images and the hand is an
            // obstacle again. In a session what it stamps in those images is pending and is hit no more once the
            // mask is back: no cue on the way out, and nothing on the way back through the place. The same walks
            // outside a session, for the shortest and the longest gap, show what the session is for.
            var cases = new List<BlinkCase>();
            foreach (float speed in new[] { 0.5f, 1.0f })
                for (int gap = 1; gap <= 8; gap++)
                    cases.Add(new BlinkCase { Session = true, Speed = speed, Gap = gap });
            foreach (float speed in new[] { 0.5f, 1.0f })
                foreach (int gap in new[] { 1, 8 })
                    cases.Add(new BlinkCase { Session = false, Speed = speed, Gap = gap });
            Blink[] walks = InParallel<BlinkCase, Blink>(cases, c => WalkWithABlinkingMask(c.Session, c.Speed, c.Gap));

            int cuedOutside = 0;
            for (int n = 0; n < cases.Count; n++)
            {
                BlinkCase c = cases[n];
                Blink b = walks[n];
                string row = Show("{0}{1} m/s, mask absent for {2} image(s) once a second: {3} cue(s) on the way out, {4} on the way back, worst level {5}; most cells that counted at once {6}, pending when the hand was lowered {7}, left at the end {8}",
                    c.Session ? "" : "outside a session, ", F(c.Speed), c.Gap, b.CuesOut, b.CuesBack, b.Worst, b.MostCounting, b.PendingWhenLowered, b.CellsAtTheEnd);
                TestContext.WriteLine(row);
                Assert.That(b.UnmaskedFrames, Is.EqualTo(2 * c.Gap), row);
                if (!c.Session)
                {
                    if (b.CuesOut > 0) cuedOutside++;
                    continue;
                }
                Assert.That(b.MaskedFrames, Is.EqualTo(120 - 2 * c.Gap), "the mask was at work between the gaps: " + row);
                Assert.That(b.CuesOut, Is.EqualTo(0), row);
                Assert.That(b.CuesBack, Is.EqualTo(0), row);
                Assert.That(b.Worst, Is.EqualTo(BumpLevel.Clear), row);
                Assert.That(b.MostCounting, Is.EqualTo(0), "a cell left in a gap counted: " + row);
                Assert.That(b.CellsAtTheEnd, Is.EqualTo(0), row);
            }
            Assert.That(cuedOutside, Is.EqualTo(4), "outside a session a gap in the mask raises a cue");
        }

        // ---- 4. BumpGuard_Pending_ApproachesAreUnchanged ----

        public enum Thing { Chair, Stool, Post, Figure, Wall }

        static SimBox BoxOf(Thing thing)
        {
            switch (thing)
            {
                case Thing.Chair: return SimBox.OnFloor(0f, 2.0f, 0.45f, 0.45f, 0.90f);
                case Thing.Stool: return SimBox.OnFloor(0f, 2.0f, 0.35f, 0.35f, 0.45f);
                case Thing.Post: return SimBox.OnFloor(0f, 2.0f, 0.10f, 0.10f, 1.60f);
                default: return SimBox.OnFloor(0f, 2.0f, 0.45f, 0.30f, 1.75f);
            }
        }

        struct Approach
        {
            public Thing Thing;
            public float Speed;
            public int Frames, CueFrame, StrongFrame, MostPending, NearStamps;
            /// <summary>The first difference between the walk in a session and the walk outside one; null when there is none.</summary>
            public string Difference;
        }

        // The same walk at a thing in a session and outside one, frame by frame.
        static Approach WalkAtInAndOutsideASession(Thing thing, float speed)
        {
            var a = new Approach { Thing = thing, Speed = speed, CueFrame = -1, StrongFrame = -1 };
            using (var session = new BumpScenario(RoomHalf, RoomHalf) { HandsSession = true })
            using (var plain = new BumpScenario(RoomHalf, RoomHalf))
            {
                float startZ, contactZ;
                if (thing == Thing.Wall)
                {
                    // The scanned wall at +Z: contact where the path reaches clear() = 0, 5 cm before it.
                    startZ = RoomHalf - 1.8f;
                    contactZ = RoomHalf - BumpConstants.StaticInset;
                }
                else
                {
                    SimBox box = BoxOf(thing);
                    session.Boxes.Add(box);
                    plain.Boxes.Add(box);
                    startZ = box.Min.z - (thing == Thing.Stool ? 3.0f : thing == Thing.Chair ? 2.2f : 1.8f);
                    contactZ = box.Min.z;
                }

                for (int k = 0; ; k++)
                {
                    float z = startZ + speed * k * FrameSeconds;
                    if (z >= contactZ) break;
                    var pose = new SimPose { Position = new float2(0f, z), Yaw = 0f };
                    session.StepFrame(pose);
                    plain.StepFrame(pose);
                    a.Frames++;

                    if (a.Difference == null &&
                        (session.Core.Level != plain.Core.Level || session.Core.RawLevel != plain.Core.RawLevel ||
                         session.Core.RescanAdvised != plain.Core.RescanAdvised))
                        a.Difference = "frame " + k + ", " + F(contactZ - z) + " m before contact: level " + session.Core.Level + " (raw " +
                                       session.Core.RawLevel + ") in a session, " + plain.Core.Level + " (raw " + plain.Core.RawLevel + ") outside one";
                    if (a.CueFrame < 0 && plain.Core.Level != BumpLevel.Clear) a.CueFrame = k;
                    if (a.StrongFrame < 0 && plain.Core.Level == BumpLevel.Strong) a.StrongFrame = k;
                    CountCells(session.Core, out _, out int pending);
                    if (pending > a.MostPending) a.MostPending = pending;
                    if (contactZ - z < BumpConstants.PendingNearMetres) a.NearStamps += session.Core.LastDepth.Stamped;
                }
                if (a.Difference == null && session.Changes.Count != plain.Changes.Count) a.Difference = "the number of level changes";
            }
            return a;
        }

        [Test]
        public void BumpGuard_Pending_ApproachesAreUnchanged()
        {
            // The approach protocol with no hand in the picture, in a session beside the same walk outside one:
            // things placed after the scan and the scanned wall. Each walk starts where the thing is in a level
            // view from 1.6 m (a 0.45 m stool only beyond 2.6 m, a 0.90 m chair beyond 1.6 m), well outside
            // 0.80 m. Whatever is first seen at 0.80 m or beyond counts at once, and the static map is never
            // pending, so the levels are the same in every frame.
            var walks = new List<Approach>();
            foreach (Thing thing in new[] { Thing.Chair, Thing.Stool, Thing.Post, Thing.Figure, Thing.Wall })
                foreach (float speed in new[] { 0.3f, 0.5f, 1.0f })
                    walks.Add(new Approach { Thing = thing, Speed = speed });
            var done = new Approach[walks.Count];
            // The walks share nothing; each has its own two Bump Guards.
            Parallel.For(0, walks.Count, n => done[n] = WalkAtInAndOutsideASession(walks[n].Thing, walks[n].Speed));

            foreach (Approach a in done)
            {
                string walk = a.Thing + " at " + F(a.Speed) + " m/s";
                TestContext.WriteLine("{0}: WARN {1} s and STRONG {2} s before contact, in a session and outside one ({3} frames; in the session {4} stamps within 0.80 m, at most {5} cells pending at once)",
                    walk, F((a.Frames - a.CueFrame) * FrameSeconds), F((a.Frames - a.StrongFrame) * FrameSeconds), a.Frames, a.NearStamps, a.MostPending);
                Assert.That(a.Difference, Is.Null, walk);
                Assert.That(a.CueFrame, Is.GreaterThanOrEqualTo(0), walk + ": no cue");
                Assert.That(a.StrongFrame, Is.GreaterThanOrEqualTo(a.CueFrame), walk + ": no STRONG");
                Assert.That((a.Frames - a.CueFrame) * FrameSeconds, Is.GreaterThanOrEqualTo(0.6f), walk + ": the lead of the protocol");
            }
        }

        // ---- 5. BumpGuard_Pending_SomeoneSteppingInIsCuedWithinAFifthOfASecond ----

        // Someone (0.5 x 0.3 x 1.8 m) steps into the path, their front `ahead` metres from a walker's head, one
        // second into the walk. Seconds from the first image that shows them to the frame of the cue; -1 for none.
        static double CueDelay(bool session, float ahead, float speed, out BumpLevel level)
        {
            const int appear = 60;
            level = BumpLevel.Clear;
            using (var sim = new BumpScenario(RoomHalf, RoomHalf) { HandsSession = session })
            {
                for (int k = 0; k < appear + 60; k++)
                {
                    float z = -2.5f + speed * k * FrameSeconds;
                    if (k == appear) sim.Boxes.Add(SimBox.OnFloor(0f, z + ahead + 0.15f, 0.5f, 0.3f, 1.8f));
                    if (k >= appear && sim.Boxes[0].ContainsFloorPoint(new float2(0f, z))) break;
                    sim.StepFrame(new SimPose { Position = new float2(0f, z), Yaw = 0f });
                    if (k < appear) Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Clear), "before anyone stepped in");
                    else if (sim.Core.Level != BumpLevel.Clear)
                    {
                        level = sim.Core.Level;
                        return (k - appear) / (double)BumpScenario.FrameRate;
                    }
                }
            }
            return -1.0;
        }

        [Test]
        public void BumpGuard_Pending_SomeoneSteppingInIsCuedWithinAFifthOfASecond()
        {
            // The price of the rule: something real that is first seen nearer than 0.80 m waits its 0.15 s too.
            foreach (float speed in new[] { 1.0f, 0.5f })
                foreach (float ahead in new[] { 0.50f, 0.60f, 0.70f, 0.79f })
                {
                    double inSession = CueDelay(true, ahead, speed, out BumpLevel levelInSession);
                    double outside = CueDelay(false, ahead, speed, out BumpLevel levelOutside);
                    string row = Show("someone steps in {0} m ahead of a walker at {1} m/s: cued after {2} s in a session ({3}), after {4} s outside one ({5})",
                        F(ahead), F(speed), inSession < 0.0 ? "no time" : inSession.ToString("F3", CultureInfo.InvariantCulture), levelInSession.ToString().ToUpperInvariant(),
                        outside < 0.0 ? "no time" : outside.ToString("F3", CultureInfo.InvariantCulture), levelOutside.ToString().ToUpperInvariant());
                    TestContext.WriteLine(row);
                    if (speed < 1.0f) continue;   // the slower walker is measured, not held: the cue there is due later
                    Assert.That(outside, Is.InRange(0.0, FrameSeconds + 1e-9), row);
                    Assert.That(inSession, Is.GreaterThanOrEqualTo(BumpConstants.PendingSeconds - 1e-9), "cued before the pending time had passed: " + row);
                    Assert.That(inSession, Is.LessThanOrEqualTo(0.20), row);
                }

            // Nearer than this the rule cannot cue at all: the walker is inside LiDAR's 0.30 m before 0.15 s have
            // passed, so no image hits the cells again. Measured, and held as it is.
            foreach (float ahead in new[] { 0.40f, 0.45f })
            {
                double inSession = CueDelay(true, ahead, 1.0f, out _);
                double outside = CueDelay(false, ahead, 1.0f, out _);
                TestContext.WriteLine("someone steps in {0} m ahead of a walker at 1.00 m/s: {1} in a session, cued after {2} s outside one, {3} s before contact",
                    F(ahead), inSession < 0.0 ? "no cue before contact" : "cued after " + inSession.ToString("F3", CultureInfo.InvariantCulture) + " s",
                    outside.ToString("F3", CultureInfo.InvariantCulture), F(ahead - outside));
                Assert.That(outside, Is.EqualTo(0.0));
                Assert.That(inSession, Is.LessThan(0.0), "cued " + F(ahead) + " m ahead after all");
            }

            // First seen at 0.80 m or beyond, nothing is held back.
            foreach (float ahead in new[] { 0.81f, 0.85f, 1.20f })
            {
                double inSession = CueDelay(true, ahead, 1.0f, out _);
                double outside = CueDelay(false, ahead, 1.0f, out _);
                TestContext.WriteLine("someone steps in {0} m ahead of a walker at 1.00 m/s: cued after {1} s in a session, after {2} s outside one",
                    F(ahead), inSession.ToString("F3", CultureInfo.InvariantCulture), outside.ToString("F3", CultureInfo.InvariantCulture));
                Assert.That(inSession, Is.EqualTo(outside), "first seen " + F(ahead) + " m ahead");
            }
        }

        // ---- 6. BumpGuard_Pending_AThingThatStaysCounts ----

        [Test]
        public void BumpGuard_Pending_AThingThatStaysCounts()
        {
            // The walker stands; a post appears 0.31 m ahead (clear(head) 0.28 m: WARN once it counts) and stays.
            // In a session it is pending until an image comes 0.15 s or more after its first, counts from that
            // image on, and goes on counting for as long as it is seen, and after.
            var pose = new SimPose { Position = new float2(0f, -2.48f), Yaw = 0f };
            float2 atThePost = new float2(0.02f, -2.16f);
            using (var sim = new BumpScenario(RoomHalf, RoomHalf) { HandsSession = true })
            using (var plain = new BumpScenario(RoomHalf, RoomHalf))
            {
                sim.Hold(pose, 0.5f);
                plain.Hold(pose, 0.5f);
                float clearBefore = sim.Core.ClearAt(atThePost, sim.Time);
                SimBox post = SimBox.OnFloor(0f, -2.12f, 0.10f, 0.10f, 1.6f);
                sim.Boxes.Add(post);
                plain.Boxes.Add(post);
                double first = sim.Time;
                int countedFrom = -1;
                for (int k = 0; k < 150; k++)
                {
                    double now = sim.Time;
                    sim.StepFrame(pose);
                    plain.StepFrame(pose);
                    bool due = now - first >= BumpConstants.PendingSeconds;
                    string where = "image " + k + ", " + (now - first).ToString("F4", CultureInfo.InvariantCulture) + " s after the first";
                    CountCells(sim.Core, out int counting, out int pending);
                    Assert.That(plain.Core.Level, Is.EqualTo(BumpLevel.Warn), "outside a session the post counts with its first image");
                    if (!due)
                    {
                        Assert.That(counting, Is.EqualTo(0), where);
                        Assert.That(pending, Is.GreaterThan(0), where);
                        Assert.That(DynamicLayer.PendingAt(sim.Core.Dynamic, atThePost), Is.True, where);
                        Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Clear), where);
                        Assert.That(sim.Core.ClearAt(atThePost, now), Is.EqualTo(clearBefore), "clear(p) at the post is the static map's: " + where);
                        Assert.That(sim.Core.Now, Is.GreaterThan(0.9f), "clear(head) is the static map's: " + where);
                    }
                    else
                    {
                        if (countedFrom < 0) countedFrom = k;
                        Assert.That(pending, Is.EqualTo(0), where);
                        Assert.That(counting, Is.GreaterThan(0), where);
                        Assert.That(DynamicLayer.PendingAt(sim.Core.Dynamic, atThePost), Is.False, where);
                        Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Warn), where);
                        Assert.That(sim.Core.Now, Is.EqualTo(plain.Core.Now), where);
                        Assert.That(sim.Core.ClearAt(atThePost, now), Is.EqualTo(plain.Core.ClearAt(atThePost, now)), where);
                        Assert.That(sim.Core.DynamicCellCount, Is.EqualTo(plain.Core.DynamicCellCount), where);
                    }
                }
                TestContext.WriteLine("a post 0.31 m ahead of a standing walker, in a session: pending for {0} images, counted from the image {1} s after its first",
                    countedFrom, (countedFrom * (double)FrameSeconds).ToString("F3", CultureInfo.InvariantCulture));
                Assert.That(countedFrom, Is.InRange(9, 10), "0.15 s at 60 images a second");
                Assert.That(sim.RisingEdges(), Is.EqualTo(1));

                // Out of view it is remembered, as any obstacle is; back in view its near returns do not make it
                // pending again.
                var away = new SimPose { Position = pose.Position, Yaw = math.PI };
                sim.Hold(away, 1f);
                CountCells(sim.Core, out int kept, out int none);
                Assert.That(none, Is.EqualTo(0));
                Assert.That(kept, Is.GreaterThan(0));
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Warn));
                for (int k = 0; k < 30; k++)
                {
                    sim.StepFrame(pose);
                    CountCells(sim.Core, out _, out int pending);
                    Assert.That(pending, Is.EqualTo(0), "back in view, image " + k);
                    Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Warn), "back in view, image " + k);
                }
                Assert.That(sim.RisingEdges(), Is.EqualTo(1));
            }
        }

        // ---- 7. BumpGuard_Pending_TheForearmsTrail ----

        [Test]
        public void BumpGuard_Pending_TheForearmsTrail()
        {
            // Wave 1's limit, measured again (BumpGuard_Mask_ForgetsTheHandsOwnCells_ButNotAForearmsTrailBelowTheView):
            // a walking walker's forearm crosses the lower border of the picture and stamps cells there before its
            // mask exists; no masked sample points at them afterwards and no ray passes under them. Outside a
            // session 2 to 4 of them stay for 30 s and the walker walks through them: STRONG. In a session they
            // are pending when the mask comes, are hit no more, never count, and are swept away.
            var cases = new List<EntryCase>();
            foreach (float speed in new[] { 0.5f, 1.0f })
                foreach (float depth in new[] { 0.35f, 0.45f, 0.60f })
                    foreach (int lag in new[] { 6, 15 })
                        cases.Add(new EntryCase
                        {
                            Forearm = true, Lag = lag, Tail = 135,
                            Motion = new Motion { Name = F(speed) + " m/s, hand " + F(depth) + " m ahead", Speed = speed, Depth = depth },
                            // The limit outside a session, as wave 1 measured it: held on the rows at 0.45 m.
                            AlsoOutside = depth == 0.45f && lag == 6
                        });
            EntryPair[] entries = InParallel<EntryCase, EntryPair>(cases, Enter);

            for (int n = 0; n < cases.Count; n++)
            {
                EntryCase c = cases[n];
                Entry e = entries[n].InSession;
                string row = EntryRow("hand and forearm", c.Motion, c.Lag, e);
                TestContext.WriteLine(row);
                // Nothing is left, however late the mask.
                Assert.That(e.CellsAtTheEnd, Is.EqualTo(0), "something was left behind: " + row);
                Assert.That(e.Worst, Is.LessThanOrEqualTo(BumpLevel.Warn), row);
                Assert.That(e.NotClearSeconds, Is.LessThan(0.5), row);
                if (c.Lag <= 6)
                {
                    Assert.That(e.Worst, Is.EqualTo(BumpLevel.Clear), row);
                    Assert.That(e.MostCounting, Is.EqualTo(0), "a cell of the trail counted: " + row);
                }

                if (!c.AlsoOutside) continue;
                Entry plain = entries[n].Outside;
                TestContext.WriteLine("    outside a session: " + EntryRow("hand and forearm", c.Motion, c.Lag, plain));
                Assert.That(plain.CellsAtTheEnd, Is.InRange(2, 4), "outside a session the trail stays");
                Assert.That(plain.Worst, Is.EqualTo(BumpLevel.Strong));
                Assert.That(plain.NotClearSeconds, Is.GreaterThan(0.9));
            }
        }

        // ---- The session flag ----

        [Test]
        public void BumpGuard_Pending_TheSessionReachesTheKernelWithOrWithoutARegion()
        {
            var k = new DepthIntrinsics(215f, 215f, 128f, 96f);
            DepthMask withHand = SimHand.Mask(7, k, H, new SimHand(0.03f, -0.05f, 0.45f, true));
            withHand.Session = 1;

            DepthKernelMask same = BumpGuard.KernelMask(withHand, 7);
            Assert.That(same.Count, Is.EqualTo(1));
            Assert.That(same.Session, Is.EqualTo(1));
            // Built for another image: its regions are dropped, as before; the session is the frame's, and stays.
            foreach (int sequence in new[] { 6, 8 })
            {
                DepthKernelMask other = BumpGuard.KernelMask(withHand, sequence);
                Assert.That(other.IsEmpty, Is.True);
                Assert.That(other.Covers(128f, 120f, 0.45f), Is.False);
                Assert.That(other.Session, Is.EqualTo(1));
            }
            // No hand in view: no region, and still a session.
            DepthKernelMask noHand = BumpGuard.KernelMask(new DepthMask { Session = 1, DepthSequence = 3 }, 3);
            Assert.That(noHand.IsEmpty, Is.True);
            Assert.That(noHand.Session, Is.EqualTo(1));
            Assert.That(BumpGuard.KernelMask(new DepthMask { Session = -4 }, 0).Session, Is.Not.EqualTo(0), "any number but 0 is a session");
            // No session.
            withHand.Session = 0;
            Assert.That(BumpGuard.KernelMask(withHand, 7).Session, Is.EqualTo(0));
            Assert.That(BumpGuard.KernelMask(withHand, 8).Session, Is.EqualTo(0));
            Assert.That(BumpGuard.KernelMask(default(DepthMask), 0).Session, Is.EqualTo(0));
        }

        [Test]
        public void BumpGuard_Pending_TheSessionReachesTheKernelThroughTheAdapter()
        {
            // The app's Bump Guard, ticked with sensor frames: the fixture room, a walker who stands in its middle,
            // a post that appears 0.31 m ahead. Four guards get the same frames with a different mask each.
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            using (var eye = new BumpScenario(BumpRecordings.RoomHalf, BumpRecordings.RoomHalf))
            using (var none = new BumpGuard(false))
            using (var noHand = new BumpGuard(false))
            using (var stale = new BumpGuard(false))
            using (var zero = new BumpGuard(false))
            {
                foreach (BumpGuard guard in new[] { none, noHand, stale, zero }) guard.SetRoom(room);
                var head = new float3(0f, BumpScenario.HeadHeight, -0.48f);
                float2 atThePost = new float2(0.02f, -0.16f);
                DepthMask handElsewhere = SimHand.Mask(0, eye.Intrinsics, H, new SimHand(0f, -0.05f, 0.45f, true));
                int countedFrom = -1;
                for (int k = 0; k < 40; k++)
                {
                    if (k == 10) eye.Boxes.Add(SimBox.OnFloor(0f, -0.12f, 0.10f, 0.10f, 1.6f));
                    int sequence = 500 + k;
                    eye.Render(head, quaternion.identity);
                    SensorFrame frame = eye.SensorsAt(head, quaternion.identity, k, sequence, default(DepthMask));
                    none.Tick(in frame);
                    // A session with no hand in view: no region.
                    frame.Mask = new DepthMask { Session = 1, DepthSequence = sequence };
                    noHand.Tick(in frame);
                    // A session whose regions were built for the image before: they would take the post out; they
                    // are ignored, and the session is not.
                    frame.Mask = handElsewhere;
                    frame.Mask.DepthSequence = sequence - 1;
                    frame.Mask.Session = 1;
                    stale.Tick(in frame);
                    // The same stale regions and no session: the frame of today.
                    frame.Mask.Session = 0;
                    zero.Tick(in frame);

                    string where = "frame " + k;
                    BumpCompare.Same(zero.Core, none.Core, "a stale mask outside a session", k);
                    BumpCompare.Same(stale.Core, noHand.Core, "a stale mask in a session", k);
                    Assert.That(stale.Core.LastDepth.Masked, Is.EqualTo(0), where);
                    if (k < 10) continue;

                    // The clearance query at the post: the static map's while the post is pending.
                    float outside = none.Clearance(atThePost), inSession = noHand.Clearance(atThePost);
                    Assert.That(outside, Is.LessThan(0.10f), "outside a session the post counts with its first image: " + where);
                    if (inSession < 0.10f && countedFrom < 0) countedFrom = k - 10;
                    if (k < 10 + 9)
                    {
                        Assert.That(inSession, Is.GreaterThan(1.0f), "the clearance query does not count a pending cell: " + where);
                        Assert.That(noHand.Level, Is.EqualTo(BumpLevel.Clear), where);
                    }
                    if (k > 10 + 10)
                    {
                        Assert.That(inSession, Is.EqualTo(outside), where);
                        Assert.That(noHand.Level, Is.EqualTo(none.Level), where);
                    }
                }
                Assert.That(countedFrom, Is.InRange(9, 10));
            }
        }

        // ---- What the rule costs elsewhere: measured, so that it is known ----

        struct LineCase
        {
            public bool Session, Chair;
            public float Speed;
        }

        struct LineWalk
        {
            public BumpLevel Worst;
            public int CueFrame, Frames;
        }

        // Walks at a chair (or at nothing) with a hand and forearm 0.45 m ahead on the walking line that nothing masks.
        static LineWalk WalkWithAHandNothingMasks(LineCase c)
        {
            var walk = new LineWalk { Worst = BumpLevel.Clear, CueFrame = -1 };
            using (var sim = new BumpScenario(RoomHalf, RoomHalf) { HandsSession = c.Session })
            {
                new SimHand(0f, -0.08f, 0.45f, true).AddTo(sim.Body);
                SimBox box = BoxOf(Thing.Chair);
                if (c.Chair) sim.Boxes.Add(box);
                for (int k = 0; ; k++)
                {
                    float z = box.Min.z - 2.2f + c.Speed * k * FrameSeconds;
                    if (c.Chair ? z >= box.Min.z : k >= 120) break;
                    sim.StepFrame(new SimPose { Position = new float2(0f, z), Yaw = 0f });
                    walk.Frames++;
                    if (sim.Core.Level > walk.Worst) walk.Worst = sim.Core.Level;
                    if (walk.CueFrame < 0 && sim.Core.Level != BumpLevel.Clear) walk.CueFrame = k;
                }
            }
            return walk;
        }

        [Test]
        public void BumpGuard_Pending_Characterisation_AHandNothingMasksOnTheWalkingLine()
        {
            // Measured, not wished for (the review's S13). A hand the detector never finds is never masked. Outside
            // a session it is an obstacle from the first step and the walker is warned, of the hand. In a session
            // its cells are pending and the hand has moved on before they have been hit for 0.15 s: it never
            // counts, and what it hides is seen as late as behind a masked hand (H18).
            float[] speeds = { 0.5f, 0.7f, 1.0f };
            var cases = new List<LineCase>();
            foreach (float speed in speeds)
                foreach (bool chair in new[] { false, true })
                    foreach (bool session in new[] { true, false })
                        cases.Add(new LineCase { Session = session, Chair = chair, Speed = speed });
            LineWalk[] walks = InParallel<LineCase, LineWalk>(cases, WalkWithAHandNothingMasks);

            for (int n = 0; n < speeds.Length; n++)
            {
                LineWalk openInSession = walks[4 * n], openOutside = walks[4 * n + 1], chairInSession = walks[4 * n + 2], chairOutside = walks[4 * n + 3];
                string row = Show("an unmasked hand and forearm on the walking line at {0} m/s: on open floor {1} in a session, {2} outside one; with a chair ahead the first cue {3} before contact in a session, {4} outside one",
                    F(speeds[n]), openInSession.Worst.ToString().ToUpperInvariant(), openOutside.Worst.ToString().ToUpperInvariant(),
                    chairInSession.CueFrame < 0 ? "never comes" : "comes " + F((chairInSession.Frames - chairInSession.CueFrame) * FrameSeconds) + " s",
                    chairOutside.CueFrame < 0 ? "never comes" : "comes " + F((chairOutside.Frames - chairOutside.CueFrame) * FrameSeconds) + " s");
                TestContext.WriteLine(row);

                // Outside a session the hand itself warns from the first steps.
                Assert.That(openOutside.Worst, Is.GreaterThanOrEqualTo(BumpLevel.Warn), row);
                Assert.That(openOutside.CueFrame, Is.InRange(0, 3), row);
                Assert.That(chairOutside.CueFrame, Is.InRange(0, 3), row);
                // In a session the hand raises nothing, and the chair is warned of late, as behind a masked hand.
                Assert.That(openInSession.Worst, Is.EqualTo(BumpLevel.Clear), "the hand does not count: " + row);
                Assert.That(chairInSession.CueFrame, Is.GreaterThanOrEqualTo(0), "the chair was never warned of: " + row);
                Assert.That((chairInSession.Frames - chairInSession.CueFrame) * FrameSeconds, Is.InRange(0.2f, 0.65f), "the chair is seen late, behind the hand: " + row);
            }
        }

        [Test]
        public void BumpGuard_Pending_Characterisation_AtTheLowestDepthRate()
        {
            // Depth may be processed as rarely as 15 times a second (BumpConstants.DepthProcessHzMin). The first
            // image 0.15 s or more after a cell's first is then the fourth, 0.20 s after it: the rule costs a
            // fifth of a second there, and someone who steps in half a metre ahead of a walker at 1 m/s is at
            // LiDAR's near limit by the time their cells would count.
            const int appear = 60, every = BumpConstants.DepthProcessHz / BumpConstants.DepthProcessHzMin;
            foreach (float ahead in new[] { 0.50f, 0.60f, 0.70f, 0.79f })
            {
                var delay = new double[2];
                for (int run = 0; run < 2; run++)
                {
                    delay[run] = -1.0;
                    using (var sim = new BumpScenario(RoomHalf, RoomHalf) { HandsSession = run == 0 })
                    {
                        for (int k = 0; k < appear + 60; k++)
                        {
                            float z = -2.5f + 1.0f * k * FrameSeconds;
                            if (k == appear) sim.Boxes.Add(SimBox.OnFloor(0f, z + ahead + 0.15f, 0.5f, 0.3f, 1.8f));
                            if (k >= appear && sim.Boxes[0].ContainsFloorPoint(new float2(0f, z))) break;
                            sim.DepthEnabled = k % every == 0;
                            sim.StepFrame(new SimPose { Position = new float2(0f, z), Yaw = 0f });
                            if (k >= appear && sim.Core.Level != BumpLevel.Clear)
                            {
                                delay[run] = (k - appear) / (double)BumpScenario.FrameRate;
                                break;
                            }
                        }
                    }
                }
                string row = Show("15 depth images a second, someone steps in {0} m ahead of a walker at 1.00 m/s: {1} in a session, {2} outside one",
                    F(ahead), delay[0] < 0.0 ? "no cue before contact" : "cued after " + delay[0].ToString("F3", CultureInfo.InvariantCulture) + " s",
                    delay[1] < 0.0 ? "no cue before contact" : "cued after " + delay[1].ToString("F3", CultureInfo.InvariantCulture) + " s");
                TestContext.WriteLine(row);
                Assert.That(delay[1], Is.EqualTo(0.0), row);
                if (ahead >= 0.60f) Assert.That(delay[0], Is.EqualTo(0.20).Within(1e-9), row);
            }
        }

        // ---- The head-frame fallback: measured, and left as it is ----

        [Test]
        public void BumpGuard_Pending_Characterisation_AHandsEntryInTheHeadFrameFallback()
        {
            // No room is loaded (a rescan is running): live depth alone warns, from the seven sectors, in the head
            // frame. Nothing is stamped there, so nothing can be pending: in a session too the hand is the nearest
            // return of its sector from its first image until its mask takes it out, and the level falls 0.3 s
            // later. Measured here so that it is known; decision R13 leaves the fallback as it is.
            const int raise = 30;
            foreach (bool forearm in new[] { false, true })
                foreach (float speed in new[] { 0f, 0.5f, 1.0f })
                    foreach (int lag in new[] { 3, 9, 15 })
                    {
                        using (var sim = new BumpScenario(RoomHalf, RoomHalf) { HandsSession = true })
                        using (var plain = new BumpScenario(RoomHalf, RoomHalf))
                        {
                            sim.Core.ClearRoom();
                            plain.Core.ClearRoom();
                            var hand = new SimHand(0.03f, -0.05f, 0.45f, forearm);
                            int maskFrame = raise + lag, firstCue = -1, notClear = 0, lastNotClear = -1;
                            BumpLevel worst = BumpLevel.Clear;
                            float nearest = float.PositiveInfinity;
                            for (int k = 0; k < maskFrame + 60; k++)
                            {
                                if (k == raise)
                                {
                                    hand.AddTo(sim.Body);
                                    hand.AddTo(plain.Body);
                                }
                                if (k == maskFrame) sim.Mask = plain.Mask = sim.MaskFor(hand);
                                var head = new float3(0f, BumpScenario.HeadHeight, -2.5f + speed * k * FrameSeconds);
                                sim.StepFrame(head, quaternion.identity);
                                plain.StepFrame(head, quaternion.identity);
                                Assert.That(sim.Core.MapTrusted, Is.False);
                                Assert.That(sim.Core.Diagnostics.HeadFrameFallback, Is.True);
                                // The session changes nothing in the fallback.
                                Assert.That(sim.Core.Level, Is.EqualTo(plain.Core.Level), "frame " + k);
                                Assert.That(sim.Core.Now, Is.EqualTo(plain.Core.Now), "frame " + k);
                                Assert.That(sim.Core.DynamicCellCount, Is.EqualTo(0));
                                if (k >= raise && k < maskFrame && sim.Core.Now < nearest) nearest = sim.Core.Now;
                                if (sim.Core.Level != BumpLevel.Clear)
                                {
                                    if (firstCue < 0) firstCue = k;
                                    notClear++;
                                    lastNotClear = k;
                                }
                                if (sim.Core.Level > worst) worst = sim.Core.Level;
                            }
                            string row = Show("no room, {0}, {1} m/s, mask {2,2} images late: nearest sector return {3} m; {4,-6} {5}, not Clear for {6} s{7}",
                                forearm ? "hand and forearm" : "hand", F(speed), lag, F(nearest), worst.ToString().ToUpperInvariant(),
                                firstCue < 0 ? "no cue" : "cue in the hand's image " + (firstCue - raise + 1), F(notClear * FrameSeconds),
                                lastNotClear < 0 ? "" : ", Clear again " + F((lastNotClear + 1 - maskFrame) * FrameSeconds) + " s after the mask");
                            TestContext.WriteLine(row);

                            Assert.That(nearest, Is.LessThan(0.50f), "the unmasked hand is the nearest return: " + row);
                            if (speed == 0f) Assert.That(worst, Is.EqualTo(BumpLevel.Clear), row);
                            if (speed > 0f && forearm)
                            {
                                Assert.That(firstCue, Is.EqualTo(raise), "the cue starts with the hand's first image: " + row);
                                Assert.That(notClear * FrameSeconds, Is.EqualTo(lag * FrameSeconds + BumpConstants.RelaxSeconds).Within(0.05), row);
                            }
                            Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Clear), "one second after the mask: " + row);
                        }
                    }
        }

        // ---- The dynamic layer's part ----

        sealed class Layer : IDisposable
        {
            public readonly BumpGuardCore Core = new BumpGuardCore();
            public Layer()
            {
                var sdf = new float[80 * 80];
                for (int k = 0; k < sdf.Length; k++) sdf[k] = 2f;
                Core.SetRoom(sdf, 80, 80, 0.05f, -2f, -2f);
            }
            public void Dispose() => Core.Dispose();
        }

        static bool Counts(in DynamicGrid g, float2 p, double time)
        {
            return DynamicLayer.Distance(g, p, time, out _) < 0.01f;
        }

        [Test]
        public void DynamicLayer_Pending_ANearStampIsStoredAndCountsFromAHitTheSecondsAfterItsFirst()
        {
            using (var l = new Layer())
            {
                DynamicGrid g = l.Core.Dynamic;
                float2 c = new float2(0.52f, 0.02f);
                Assert.That(DynamicLayer.PendingAt(g, c), Is.False, "an empty cell is not pending");
                Assert.That(DynamicLayer.PendingAt(g, new float2(9f, 0f)), Is.False, "nor is a point off the grid");

                Assert.That(DynamicLayer.Stamp(g, c, 1.4f, 10.0, true), Is.True);
                Assert.That(l.Core.DynamicCellCount, Is.EqualTo(1), "stored");
                Assert.That(DynamicLayer.PendingAt(g, c), Is.True);
                Assert.That(DynamicLayer.TopHeightAt(g, c), Is.EqualTo(1.4f));
                Assert.That(DynamicLayer.FirstHitAt(g, c), Is.EqualTo(10.0));
                Assert.That(DynamicLayer.Distance(g, c, 10.0, out _), Is.EqualTo(float.PositiveInfinity), "and not counted");
                Assert.That(l.Core.ClearAt(c, 10.0), Is.EqualTo(2f), "clear(p) is the static map's");

                // Hit again and again, less than 0.15 s after its first hit: pending still.
                foreach (double later in new[] { 0.02, 0.05, 0.10, 0.149 })
                {
                    DynamicLayer.Stamp(g, c, 1.4f, 10.0 + later, true);
                    Assert.That(DynamicLayer.PendingAt(g, c), Is.True, later + " s after its first hit");
                    Assert.That(Counts(g, c, 10.0 + later), Is.False);
                }
                // Time alone does not make it count: it takes a hit.
                Assert.That(Counts(g, c, 10.2), Is.False);
                Assert.That(DynamicLayer.PendingAt(g, c), Is.True);
                // A hit 0.15 s after the first: it counts from this hit on, with the first stamp it always had.
                DynamicLayer.Stamp(g, c, 1.4f, 10.0 + BumpConstants.PendingSeconds, true);
                Assert.That(DynamicLayer.PendingAt(g, c), Is.False);
                Assert.That(Counts(g, c, 10.15), Is.True);
                Assert.That(DynamicLayer.FirstHitAt(g, c), Is.EqualTo(10.0));

                // A cell that counts never becomes pending again, whatever hits it and however late.
                DynamicLayer.Stamp(g, c, 1.4f, 10.2, true);
                DynamicLayer.Stamp(g, c, 1.4f, 15.0, true);
                DynamicLayer.Stamp(g, c, 1.4f, 15.0 + 0.5 * BumpConstants.DynExpirySeconds, true);
                Assert.That(DynamicLayer.PendingAt(g, c), Is.False);
                Assert.That(Counts(g, c, 30.0), Is.True);
                Assert.That(DynamicLayer.FirstHitAt(g, c), Is.EqualTo(10.0));
            }
        }

        [Test]
        public void DynamicLayer_Pending_ACellWithNoHitForATenthOfASecondStartsAgain()
        {
            using (var l = new Layer())
            {
                DynamicGrid g = l.Core.Dynamic;
                float2 c = new float2(0.52f, 0.02f);
                DynamicLayer.Stamp(g, c, 1.4f, 10.0, true);
                DynamicLayer.Stamp(g, c, 1.4f, 10.05, true);
                // No hit for 0.11 s: the next hit is its first. 0.16 s after the very first stamp it does not count.
                DynamicLayer.Stamp(g, c, 0.9f, 10.16, true);
                Assert.That(DynamicLayer.PendingAt(g, c), Is.True);
                Assert.That(DynamicLayer.FirstHitAt(g, c), Is.EqualTo(10.16), "the cell started again");
                Assert.That(DynamicLayer.TopHeightAt(g, c), Is.EqualTo(0.9f), "as a new cell: what stood there before left nothing");
                Assert.That(l.Core.DynamicCellCount, Is.EqualTo(1));
                // Hits less than 0.10 s apart keep the run going; it counts 0.15 s after the run began.
                DynamicLayer.Stamp(g, c, 0.9f, 10.25, true);
                Assert.That(DynamicLayer.PendingAt(g, c), Is.True);
                Assert.That(DynamicLayer.FirstHitAt(g, c), Is.EqualTo(10.16));
                DynamicLayer.Stamp(g, c, 0.9f, 10.30, true);
                Assert.That(DynamicLayer.PendingAt(g, c), Is.True, "0.14 s after the run began");
                DynamicLayer.Stamp(g, c, 0.9f, 10.32, true);
                Assert.That(DynamicLayer.PendingAt(g, c), Is.False, "0.16 s after the run began");
                Assert.That(DynamicLayer.FirstHitAt(g, c), Is.EqualTo(10.16));

                // A cell that is hit once every 0.12 s never counts.
                float2 d = new float2(-0.52f, 0.02f);
                for (int k = 0; k < 40; k++)
                {
                    DynamicLayer.Stamp(g, d, 1.0f, 20.0 + 0.12 * k, true);
                    Assert.That(DynamicLayer.PendingAt(g, d), Is.True, "hit " + k);
                    Assert.That(Counts(g, d, 20.0 + 0.12 * k), Is.False);
                }

                // A clock that went back: a pending cell whose last hit lies after now has run out too. It starts
                // again at this hit and counts 0.15 s later, and is not left pending until the clock catches up.
                float2 e = new float2(0.02f, 0.92f);
                DynamicLayer.Stamp(g, e, 1.0f, 100.0, true);
                DynamicLayer.Stamp(g, e, 1.0f, 50.0, true);
                Assert.That(DynamicLayer.FirstHitAt(g, e), Is.EqualTo(50.0));
                DynamicLayer.Stamp(g, e, 1.0f, 50.08, true);
                DynamicLayer.Stamp(g, e, 1.0f, 50.16, true);
                Assert.That(DynamicLayer.PendingAt(g, e), Is.False);
                Assert.That(Counts(g, e, 50.16), Is.True);
            }
        }

        [Test]
        public void DynamicLayer_Pending_FarReturnsAndStampsOutsideASessionCountAtOnce()
        {
            using (var l = new Layer())
            {
                DynamicGrid g = l.Core.Dynamic;
                float2 c = new float2(0.52f, 0.02f), d = new float2(-0.52f, 0.02f), e = new float2(0.02f, 0.92f);
                // First stamped by a return that is not near: never pending.
                DynamicLayer.Stamp(g, c, 1.4f, 10.0, false);
                Assert.That(DynamicLayer.PendingAt(g, c), Is.False);
                Assert.That(Counts(g, c, 10.0), Is.True);
                DynamicLayer.Stamp(g, c, 1.4f, 10.02, true);
                Assert.That(DynamicLayer.PendingAt(g, c), Is.False, "near returns do not make a counting cell pending");

                // A pending cell that a far return hits counts from that hit.
                DynamicLayer.Stamp(g, d, 1.4f, 10.0, true);
                Assert.That(Counts(g, d, 10.0), Is.False);
                DynamicLayer.Stamp(g, d, 0.6f, 10.02, false);
                Assert.That(DynamicLayer.PendingAt(g, d), Is.False);
                Assert.That(Counts(g, d, 10.02), Is.True);
                Assert.That(DynamicLayer.TopHeightAt(g, d), Is.EqualTo(1.4f), "the cell it always was: the higher of its hits");
                Assert.That(DynamicLayer.FirstHitAt(g, d), Is.EqualTo(10.0));

                // The stamp of before the rule is a stamp outside a session.
                DynamicLayer.Stamp(g, e, 1.0f, 10.0);
                Assert.That(DynamicLayer.PendingAt(g, e), Is.False);
                Assert.That(Counts(g, e, 10.0), Is.True);
                l.Core.StampDynamic(new float2(0.02f, -0.92f), 1.0f, 10.0);
                Assert.That(Counts(g, new float2(0.02f, -0.92f), 10.0), Is.True);
            }
        }

        [Test]
        public void DynamicLayer_Pending_APendingCellIsClearedForgottenAndSweptLikeAnyOther_AndSoonerWhenItHasRunOut()
        {
            using (var l = new Layer())
            {
                DynamicGrid g = l.Core.Dynamic;
                float2 c = new float2(0.52f, 0.02f);

                // Seen empty in three images it goes, and what is stamped there next is a new cell.
                DynamicLayer.Stamp(g, c, 1.4f, 10.0, true);
                for (int image = 1; image <= 3; image++) DynamicLayer.ClearIfSeenEmpty(g, c, 0.2f, image, 10.0 + 0.01 * image);
                Assert.That(l.Core.DynamicCellCount, Is.EqualTo(0));
                Assert.That(DynamicLayer.PendingAt(g, c), Is.False);
                DynamicLayer.Stamp(g, c, 1.4f, 10.05);
                Assert.That(DynamicLayer.PendingAt(g, c), Is.False, "a cell that was pending leaves no mark on the next");
                Assert.That(Counts(g, c, 10.05), Is.True);
                l.Core.ClearDynamic();

                // The young-cell rule reaches a pending cell as it reaches any cell under a second old.
                DynamicLayer.Stamp(g, c, 1.4f, 20.0, true);
                Assert.That(DynamicLayer.ForgetIfYoung(g, c, 1.4f, 20.0), Is.False, "hit in this very image");
                Assert.That(DynamicLayer.ForgetIfYoung(g, c, 0.9f, 20.05), Is.False, "not at the sample's height");
                Assert.That(DynamicLayer.ForgetIfYoung(g, c, 1.4f, 20.05), Is.True);
                Assert.That(l.Core.DynamicCellCount, Is.EqualTo(0));
                // A counting cell the mask forgot is gone like any other; stamped again in a session it is a new
                // cell and waits again: forgetting never lets anything count sooner.
                DynamicLayer.Stamp(g, c, 1.4f, 30.0, false);
                Assert.That(DynamicLayer.ForgetIfYoung(g, c, 1.4f, 30.5), Is.True);
                DynamicLayer.Stamp(g, c, 1.4f, 30.52, true);
                Assert.That(DynamicLayer.PendingAt(g, c), Is.True);
                Assert.That(DynamicLayer.FirstHitAt(g, c), Is.EqualTo(30.52));
                l.Core.ClearDynamic();
                Assert.That(DynamicLayer.PendingAt(g, c), Is.False, "an emptied layer holds nothing pending");

                // The first stamp is one clock for both rules. A cell that became counting 0.15 s into its run is
                // young for the mask until a second after the run began, not after it began to count.
                DynamicLayer.Stamp(g, c, 1.4f, 40.0, true);
                DynamicLayer.Stamp(g, c, 1.4f, 40.08, true);
                DynamicLayer.Stamp(g, c, 1.4f, 40.16, true);
                Assert.That(DynamicLayer.PendingAt(g, c), Is.False);
                Assert.That(DynamicLayer.ForgetIfYoung(g, c, 1.4f, 41.0), Is.False, "a second after its first stamp it is kept");
                Assert.That(DynamicLayer.ForgetIfYoung(g, c, 1.4f, 40.99), Is.True, "just under a second after it, it is the hand's");
                l.Core.ClearDynamic();

                // The sweep drops a pending cell that has run out (no hit for 0.10 s), keeps one that is being hit,
                // and keeps every cell that counts for its 30 s.
                float2 running = new float2(-0.52f, 0.02f), counting = new float2(0.02f, 0.92f);
                DynamicLayer.Stamp(g, c, 1.4f, 50.0, true);
                DynamicLayer.Stamp(g, running, 1.4f, 50.0, true);
                DynamicLayer.Stamp(g, counting, 1.4f, 50.0, false);
                DynamicLayer.Stamp(g, running, 1.4f, 50.08, true);
                int cursor = 0;
                DynamicLayer.SweepExpired(g, 50.09, ref cursor, g.BlocksX * g.BlocksZ);
                Assert.That(l.Core.DynamicCellCount, Is.EqualTo(3), "0.09 s without a hit: not run out yet");
                DynamicLayer.SweepExpired(g, 50.12, ref cursor, g.BlocksX * g.BlocksZ);
                Assert.That(l.Core.DynamicCellCount, Is.EqualTo(2));
                Assert.That(DynamicLayer.TopHeightAt(g, c), Is.EqualTo(0f), "run out and swept");
                Assert.That(DynamicLayer.PendingAt(g, running), Is.True, "hit 0.04 s ago: kept");
                DynamicLayer.SweepExpired(g, 50.0 + BumpConstants.DynExpirySeconds - 0.5, ref cursor, g.BlocksX * g.BlocksZ);
                Assert.That(l.Core.DynamicCellCount, Is.EqualTo(1));
                Assert.That(Counts(g, counting, 79.5), Is.True, "a cell that counts keeps its 30 s");
            }
        }

        [Test]
        public void DynamicLayer_Pending_ACellPastItsThirtySecondsIsANewCell()
        {
            using (var l = new Layer())
            {
                DynamicGrid g = l.Core.Dynamic;
                float2 c = new float2(0.52f, 0.02f);
                DynamicLayer.Stamp(g, c, 1.4f, 10.0, false);
                Assert.That(Counts(g, c, 10.0), Is.True);
                // Past its 30 s and not swept yet, it counts for nothing: a near hit in a session starts a new,
                // pending cell there, exactly as it would had the sweep come first.
                double later = 10.0 + BumpConstants.DynExpirySeconds + 5.0;
                Assert.That(Counts(g, c, later), Is.False);
                DynamicLayer.Stamp(g, c, 1.0f, later, true);
                Assert.That(DynamicLayer.PendingAt(g, c), Is.True);
                Assert.That(DynamicLayer.FirstHitAt(g, c), Is.EqualTo(later));
                Assert.That(Counts(g, c, later), Is.False);
                DynamicLayer.Stamp(g, c, 1.0f, later + 0.08, true);
                Assert.That(Counts(g, c, later + 0.08), Is.False);
                DynamicLayer.Stamp(g, c, 1.0f, later + 0.16, true);
                Assert.That(Counts(g, c, later + 0.16), Is.True);
            }
        }

        [Test]
        public void PendingConstants_AreTheDecidedValues()
        {
            Assert.That(BumpConstants.PendingNearMetres, Is.EqualTo(0.80f));
            Assert.That(BumpConstants.PendingSeconds, Is.EqualTo(0.15));
            Assert.That(BumpConstants.PendingRestartSeconds, Is.EqualTo(0.10));
            // Whatever a mask may take out is near: a hand that is masked late was pending until then.
            Assert.That(BumpConstants.PendingNearMetres, Is.GreaterThanOrEqualTo(BumpConstants.MaskMaxDepth));
            // A cell must be able to live through the gap between two images at the lowest depth rate and still count.
            Assert.That(1.0 / BumpConstants.DepthProcessHzMin, Is.LessThan(BumpConstants.PendingRestartSeconds));
            // The mask's young-cell rule reaches a cell for longer than it can be pending.
            Assert.That(BumpConstants.PendingSeconds, Is.LessThan(BumpConstants.MaskYoungSeconds));
        }

        // ---- 8. The frame path ----

#if ATLAS_HEADLESS
        [Test]
        public void BumpGuard_Pending_AllocatesNothing()
        {
            using (var sim = new BumpScenario(RoomHalf, RoomHalf) { HandsSession = true })
            {
                // Two unmasked hands with their forearms in a session, a walker at 0.5 m/s and a chair ahead: cells
                // are made pending, run out, start again, are swept, and under the forearms come to count.
                LeftHand.AddTo(sim.Body);
                RightHand.AddTo(sim.Body);
                sim.Boxes.Add(SimBox.OnFloor(0f, 1.5f, 0.45f, 0.45f, 0.90f));
                // Warm up: first calls may JIT.
                sim.Walk(new float2(0f, -2.5f), new float2(0f, 1f), 0.5f, 1f);

                BumpGuardCore core = sim.Core;
                DepthKernelMask session = BumpGuard.KernelMask(new DepthMask { Session = 1 }, 0);
                Assert.That(session.Session, Is.EqualTo(1));
                double t = sim.Time;
                int mostPending = 0, mostCounting = 0;
                fixed (float* depth = sim.Depth)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int k = 0; k < 100; k++)
                    {
                        t += FrameSeconds;
                        var head = new float3(0f, BumpScenario.HeadHeight, -2.0f + 0.5f * k * FrameSeconds);
                        sim.Render(head, quaternion.identity);
                        DepthKernelInput input = core.MakeDepthInput(W, H, sim.Intrinsics, head, quaternion.identity, t, true, true);
                        input.Mask = session;
                        core.ProcessDepth(depth, input);
                        core.Step(t, head, quaternion.identity, true, true);
                    }
                    long after = GC.GetAllocatedBytesForCurrentThread();
                    CountCells(core, out mostCounting, out mostPending);
                    Assert.That(after - before, Is.EqualTo(0L));
                }
                Assert.That(mostPending, Is.GreaterThan(0), "cells were pending");
                Assert.That(mostCounting, Is.GreaterThan(0), "and cells counted");
            }
        }

        [Test]
        public void BumpGuard_Pending_TheAdapterAllocatesNothing()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            using (var eye = new BumpScenario(BumpRecordings.RoomHalf, BumpRecordings.RoomHalf))
            using (var guard = new BumpGuard(false))
            {
                guard.SetRoom(room);
                new SimHand(0.03f, -0.05f, 0.45f, true).AddTo(eye.Body);
                var head = new float3(0f, BumpScenario.HeadHeight, 0f);
                eye.Render(head, quaternion.identity);
                for (int k = 0; k < 5; k++)
                {
                    SensorFrame warm = eye.SensorsAt(head, quaternion.identity, k, k, new DepthMask { Session = 1, DepthSequence = k });
                    guard.Tick(in warm);
                }
                CountCells(guard.Core, out int counting, out int pending);
                Assert.That(pending, Is.GreaterThan(0));
                Assert.That(counting, Is.EqualTo(0));
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int k = 5; k < 105; k++)
                {
                    SensorFrame frame = eye.SensorsAt(head, quaternion.identity, k, k, new DepthMask { Session = 1, DepthSequence = k });
                    guard.Tick(in frame);
                }
                long after = GC.GetAllocatedBytesForCurrentThread();
                Assert.That(after - before, Is.EqualTo(0L));
                CountCells(guard.Core, out counting, out pending);
                Assert.That(counting, Is.GreaterThan(0), "the hand that nothing masked for 0.15 s counts");
            }
        }

        [Test]
        public void BumpGuard_Pending_CostPerDepthImage()
        {
            // The price of the rule on one 256 x 192 image (3,072 samples), on this machine's .NET in an unoptimised
            // test build and not under Burst. In a session a stamp reads the cell's pending flag and may write it;
            // the clearance query reads the flag of every occupied cell it meets, in a session and outside one;
            // the sweep reads it too.
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                var head = new float3(0f, BumpScenario.HeadHeight, -2.5f);
                DepthKernelMask none = default(DepthKernelMask);
                DepthKernelMask session = BumpGuard.KernelMask(new DepthMask { Session = 1 }, 0);

                sim.Render(head, quaternion.identity);
                Microseconds(sim, head, none, false, out double bare, out double bareStep);
                Microseconds(sim, head, session, false, out double bareSession, out double bareSessionStep);
                Microseconds(sim, head, none, true, out double cells, out double cellsStep);
                Microseconds(sim, head, session, true, out double cellsSession, out double cellsSessionStep);
                TestContext.WriteLine("no hands in the picture, empty layer: the image {0:F1} us outside a session, {1:F1} us in one; the step after it {2:F2} us, {3:F2} us", bare, bareSession, bareStep, bareSessionStep);
                TestContext.WriteLine("no hands in the picture, cells in the layer: the image {0:F1} us outside a session, {1:F1} us in one; the step after it {2:F2} us, {3:F2} us", cells, cellsSession, cellsStep, cellsSessionStep);

                // Two unmasked hands with their forearms: every one of their samples is a near return that stamps.
                LeftHand.AddTo(sim.Body);
                RightHand.AddTo(sim.Body);
                sim.Render(head, quaternion.identity);
                Microseconds(sim, head, none, false, out double hands, out double handsStep);
                Microseconds(sim, head, session, false, out double handsSession, out double handsSessionStep);
                TestContext.WriteLine("two unmasked hands in the picture: the image {0:F1} us outside a session, {1:F1} us in one; the step after it {2:F2} us, {3:F2} us", hands, handsSession, handsStep, handsSessionStep);
                Assert.That(bare, Is.GreaterThan(0.0));
            }
        }

        // Best of four rounds of a hundred images: microseconds for the depth image, and for the step after it.
        static void Microseconds(BumpScenario sim, float3 head, DepthKernelMask mask, bool cells, out double image, out double step)
        {
            const int images = 100;
            BumpGuardCore core = sim.Core;
            image = step = double.MaxValue;
            var imageWatch = new System.Diagnostics.Stopwatch();
            var stepWatch = new System.Diagnostics.Stopwatch();
            fixed (float* depth = sim.Depth)
            {
                for (int round = 0; round < 4; round++)
                {
                    core.Reset();
                    // Cells well away from the hands, as high as the ceiling: every ray checks the layer and none clears them.
                    if (cells)
                        for (int n = 0; n < 12; n++) core.StampDynamic(new float2(-1.5f + 0.05f * n, 1.0f), 2.4f, 0.0);
                    imageWatch.Reset();
                    stepWatch.Reset();
                    for (int k = 0; k < images; k++)
                    {
                        double t = 1.0 + k * (double)FrameSeconds;
                        DepthKernelInput input = core.MakeDepthInput(W, H, sim.Intrinsics, head, quaternion.identity, t, true, true);
                        input.Mask = mask;
                        imageWatch.Start();
                        core.ProcessDepth(depth, input);
                        imageWatch.Stop();
                        stepWatch.Start();
                        core.Step(t, head, quaternion.identity, true, true);
                        stepWatch.Stop();
                    }
                    image = Math.Min(image, imageWatch.Elapsed.TotalMilliseconds * 1000.0 / images);
                    step = Math.Min(step, stepWatch.Elapsed.TotalMilliseconds * 1000.0 / images);
                }
            }
        }
#endif

#if !ATLAS_HEADLESS
        [Test]
        [Category("Unity")]
        public void BumpGuard_Pending_TheBurstJobGivesWhatTheInlineKernelGives()
        {
            // On the phone a 256 x 192 image goes through the Burst job; headless tests run the kernel inline. The
            // same sensor frames of a session must make the two hold back, count and forget the same cells and
            // raise the same level. (Counts and levels are compared, not the bits of every float: the Editor's two
            // compilers need not round alike. The walker stands a little off the grid, so that no ray runs along
            // the edge of a cell.)
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            using (var eye = new BumpScenario(BumpRecordings.RoomHalf, BumpRecordings.RoomHalf))
            using (var job = new BumpGuard(true))
            using (var inline = new BumpGuard(false))
            {
                job.SetRoom(room);
                inline.SetRoom(room);
                var hand = new SimHand(0.03f, -0.05f, 0.45f, true);
                var head = new float3(0.013f, BumpScenario.HeadHeight, -0.317f);
                int mostPending = 0, promoted = 0;
                for (int k = 0; k < 120; k++)
                {
                    // The walker stands. A hand comes up after half a second and is masked six images later; a post
                    // appears half a metre ahead and to the left of the hand's region a second in, and stays.
                    if (k == 30) hand.AddTo(eye.Body);
                    if (k == 60) eye.Boxes.Add(SimBox.OnFloor(-0.187f, 0.233f, 0.10f, 0.10f, 1.6f));
                    eye.Render(head, quaternion.identity);
                    DepthMask mask = k >= 36 ? SimHand.Mask(k, eye.Intrinsics, H, hand) : new DepthMask { DepthSequence = k };
                    mask.Session = 1;
                    SensorFrame frame = eye.SensorsAt(head, quaternion.identity, k, k, mask);
                    job.Tick(in frame);
                    inline.Tick(in frame);

                    string where = "frame " + k;
                    CountCells(job.Core, out int counting, out int pending);
                    CountCells(inline.Core, out int countingInline, out int pendingInline);
                    Assert.That(pending, Is.EqualTo(pendingInline), where);
                    Assert.That(counting, Is.EqualTo(countingInline), where);
                    Assert.That(job.Core.LastDepth.Masked, Is.EqualTo(inline.Core.LastDepth.Masked), where);
                    Assert.That(job.Core.LastDepth.Forgotten, Is.EqualTo(inline.Core.LastDepth.Forgotten), where);
                    Assert.That(job.Core.Level, Is.EqualTo(inline.Core.Level), where);
                    if (pending > mostPending) mostPending = pending;
                    promoted = counting;
                }
                Assert.That(mostPending, Is.GreaterThan(0), "cells were pending");
                Assert.That(promoted, Is.GreaterThan(0), "and the post came to count");
            }
        }
#endif
    }
}
