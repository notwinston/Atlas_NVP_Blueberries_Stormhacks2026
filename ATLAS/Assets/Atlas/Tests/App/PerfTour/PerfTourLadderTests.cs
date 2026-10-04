using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.World;
using NUnit.Framework;
using WorldTier = Atlas.World.QualityTier;

namespace Atlas.App.Tests
{
    /// <summary>
    /// The ladder check against the real ladder, registry and operator commands (Master Plan task P1-A07):
    /// what the tour does on the phone, with the frame loop's step 8b played by hand.
    /// </summary>
    [TestFixture]
    public sealed class PerfTourLadderTests
    {
        const float Dt = 1f / 60f;

        sealed class Perf : IPerfSource
        {
            public bool HasData { get; set; }
            public float GpuP95Ms10s { get; set; }
            public float FrameP95Ms10s { get; set; }
        }

        sealed class Rig : IPerfTourDriver
        {
            public readonly ParamRegistry Registry = new ParamRegistry();
            public readonly QualityLadder Ladder;
            public readonly CommandRouter Router;
            public readonly Perf Perf = new Perf();
            public readonly List<string> Lines = new List<string>();

            public Rig()
            {
                Ladder = new QualityLadder(new QualityApplier(null)) { Perf = Perf };
                // Off the device there is no thermal plugin: the rehearsal value stands in.
                Ladder.Applier.ThermalOverride = 0;
                AppParams.DeclareRender(Registry, Ladder, new FlatWalkerKernel(), null, null, null, false, null, null);
                Router = new CommandRouter(new FakeHost(), new FakeLoop(), Registry);
                Registry.PushAll();
            }

            public bool PrepareRoom() { return true; }
            public void ApplyVariant(PerfVariant variant) { }
            public void Stand(float seconds) { }
            public bool InSpace(SpaceId space) { return true; }
            public bool RequestSpace(SpaceId space) { return true; }
            public void ForceTier(int tier) { Router.SetQuality(tier); }
            public void WriteLines(string text) { Lines.Add(text); }
            public void LogEvent(string ev, string dataJson) { }

            /// <summary>Step 8b of one frame in the composition root's order: the tour, then the ladder.</summary>
            public double Frame(PerfTourCore core, double time)
            {
                time += Dt;
                if (core != null) core.Tick(time, Ladder.ThermalState, (int)Ladder.Tier);
                var frame = new FrameContext { Time = time, DeltaTime = Dt };
                Ladder.AfterFrame(in frame);
                return time;
            }
        }

        [Test]
        public void PerfTour_Ladder_EveryTierForcedTheConsolesWayIsInForceWithinASecond()
        {
            var rig = new Rig();
            var core = new PerfTourCore(rig);
            core.Begin(PerfSchedule.Ladder(1f), new PerfRunInfo { Session = "s", Room = "budget_room" }, 0.0);

            double time = 0.0;
            var scales = new List<float>();
            rig.Ladder.TierChanged += tier => scales.Add(rig.Registry.GetFloat(AppParams.RenderScale));
            for (int i = 0; i < 60 * 30 && core.Running; i++) time = rig.Frame(core, time);

            Assert.That(core.Running, Is.False);
            Assert.That(core.Result, Is.EqualTo(PerfMust.Pass));
            Assert.That(core.Rows.Count, Is.EqualTo(5));
            foreach (PerfSummary row in core.Rows)
            {
                Assert.That(row.EffectMs, Is.GreaterThanOrEqualTo(0f).And.LessThan(50f), row.Label + ": in force on the next frame");
                Assert.That(row.Result, Is.EqualTo(PerfMust.Pass));
            }
            // Fair, Serious, Critical, High, Nominal: the rung's render scale was written back each time.
            Assert.That(scales, Is.EqualTo(new[] { 0.7f, 0.6f, 0.6f, 0.8f, 0.8f }));
            Assert.That(rig.Registry.GetInt(AppParams.PinTier), Is.EqualTo(-1), "the ladder is automatic again");
            Assert.That(rig.Ladder.PinnedTier, Is.EqualTo(-1));
        }

        [Test]
        public void PerfTour_Ladder_FollowsTheThermalStateAndTheProbeEveryFrame()
        {
            var rig = new Rig();
            double time = 0.0;
            for (int i = 0; i < 120; i++) time = rig.Frame(null, time);
            Assert.That(rig.Ladder.Tier, Is.EqualTo(WorldTier.Nominal));

            // The phone turns Fair: polled within 2 s, stepped at the next evaluation.
            rig.Ladder.Applier.ThermalOverride = 1;
            for (int i = 0; i < 60 * 4; i++) time = rig.Frame(null, time);
            Assert.That(rig.Ladder.ThermalState, Is.EqualTo(1));
            Assert.That(rig.Ladder.Tier, Is.EqualTo(WorldTier.Fair));

            // The probe reports a GPU p95 over 12 ms: one more rung, but not before 20 s have passed.
            rig.Perf.HasData = true;
            rig.Perf.GpuP95Ms10s = 13f;
            rig.Perf.FrameP95Ms10s = 16.9f;
            for (int i = 0; i < 60 * 5; i++) time = rig.Frame(null, time);
            Assert.That(rig.Ladder.Tier, Is.EqualTo(WorldTier.Fair), "at most one step per 20 s");
            for (int i = 0; i < 60 * 20; i++) time = rig.Frame(null, time);
            Assert.That(rig.Ladder.Tier, Is.EqualTo(WorldTier.Serious));
            Assert.That(rig.Registry.GetInt(AppParams.GluedCopies), Is.EqualTo(3));
        }
    }
}
