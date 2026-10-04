using Atlas.World;
using System.Collections.Generic;
using NUnit.Framework;

namespace Atlas.Rendering.Tests.Quality
{
    /// <summary>Putting a tier into force (Master Plan section 8.6, task P1-A07).</summary>
    public sealed class QualityApplierTests
    {
        sealed class FakeEyes : IEyeTextures
        {
            public float RenderScale { get; private set; } = 0.8f;
            public int Msaa { get; private set; } = 4;
            public int Selects;

            public void Select(float renderScale, int msaa)
            {
                RenderScale = renderScale;
                Msaa = msaa;
                Selects++;
            }
        }

        [TearDown]
        public void RestoreFrameRate()
        {
            FramePacing.TargetFrameRate = FramePacing.DefaultFrameRate;
        }

        [Test]
        public void StartsOnNominal_WithItsRowInForce()
        {
            var eyes = new FakeEyes();
            var applier = new QualityApplier(eyes);
            Assert.That(applier.Tier, Is.EqualTo(QualityTier.Nominal));
            Assert.That(applier.RenderScale, Is.EqualTo(0.8f));
            Assert.That(applier.Msaa, Is.EqualTo(4));
            Assert.That(applier.CullRho, Is.EqualTo(3f));
            Assert.That(applier.GluedCopies, Is.EqualTo(5));
            Assert.That(applier.FrameRate, Is.EqualTo(60));
            Assert.That(FramePacing.TargetFrameRate, Is.EqualTo(60));
        }

        [Test]
        public void ForcedTier_IsInForceWithinOneSecond_AtSixtyFramesPerSecond()
        {
            for (int tier = 0; tier < QualityTable.Count; tier++)
            {
                var eyes = new FakeEyes();
                var applier = new QualityApplier(eyes);
                var seen = new List<QualityTier>();
                applier.TierChanged += seen.Add;

                // The app has been running for a while; the ladder was evaluated half a frame ago.
                double time = 50.0;
                for (int f = 0; f < 120; f++, time += 1.0 / 60.0)
                    applier.Tick(time);

                double forcedAt = time;
                applier.ForcedTier = tier;
                double inForceAt = double.NaN;
                for (int f = 0; f < 120 && double.IsNaN(inForceAt); f++, time += 1.0 / 60.0)
                {
                    applier.Tick(time);
                    if ((int)applier.Tier == tier)
                        inForceAt = time;
                }

                QualityRow row = QualityTable.Row((QualityTier)tier);
                Assert.That(inForceAt - forcedAt, Is.LessThan(1.0), "tier " + tier);
                Assert.That(eyes.RenderScale, Is.EqualTo(row.RenderScale));
                Assert.That(eyes.Msaa, Is.EqualTo(row.Msaa));
                Assert.That(applier.CullRho, Is.EqualTo(row.CullRho));
                Assert.That(applier.GluedCopies, Is.EqualTo(row.GluedCopies));
                Assert.That(FramePacing.TargetFrameRate, Is.EqualTo(row.FrameRate));
                if (tier != 0)
                    Assert.That(seen, Is.EqualTo(new[] { (QualityTier)tier }));
            }
        }

        [Test]
        public void ThermalState_IsPolledEveryTwoSeconds_AndStepsTheLadder()
        {
            var eyes = new FakeEyes();
            var applier = new QualityApplier(eyes);
            applier.Tick(0.0);
            Assert.That(applier.ThermalState, Is.EqualTo(0));

            applier.ThermalOverride = 1;
            applier.Tick(1.0);
            Assert.That(applier.ThermalState, Is.EqualTo(0), "not polled yet");
            Assert.That(applier.Tier, Is.EqualTo(QualityTier.Nominal));

            applier.Tick(2.0);
            Assert.That(applier.ThermalState, Is.EqualTo(1));
            Assert.That(applier.Tier, Is.EqualTo(QualityTier.Fair));
            Assert.That(eyes.RenderScale, Is.EqualTo(0.7f));
            Assert.That(eyes.Msaa, Is.EqualTo(4));
        }

        [Test]
        public void GpuTime_StepsTheLadder_OneRungPerTwentySeconds()
        {
            var eyes = new FakeEyes();
            var applier = new QualityApplier(eyes);
            applier.SetPerf(true, 13f, 16.7f);

            var tiers = new List<QualityTier>();
            var times = new List<double>();
            double now = 0.0;
            applier.TierChanged += t => { tiers.Add(t); times.Add(now); };
            for (int f = 0; f < 60 * 60; f++)
            {
                now = f / 60.0;
                applier.Tick(now);
            }

            Assert.That(tiers, Is.EqualTo(new[] { QualityTier.Fair, QualityTier.Serious }));
            Assert.That(times[1] - times[0], Is.GreaterThanOrEqualTo(20.0));
            Assert.That(eyes.RenderScale, Is.EqualTo(0.6f));
            Assert.That(eyes.Msaa, Is.EqualTo(2));
            Assert.That(applier.CullRho, Is.EqualTo(2f));
            Assert.That(applier.GluedCopies, Is.EqualTo(3));
        }

        [Test]
        public void Critical_LocksThirtyFramesPerSecond()
        {
            var applier = new QualityApplier(new FakeEyes());
            applier.ForcedTier = 3;
            applier.Tick(0.0);
            Assert.That(applier.Tier, Is.EqualTo(QualityTier.Critical));
            Assert.That(applier.FrameRate, Is.EqualTo(30));
            Assert.That(FramePacing.TargetFrameRate, Is.EqualTo(30));
            Assert.That(FramePacing.VSyncCount(30), Is.EqualTo(2));
            Assert.That(FramePacing.VSyncCount(60), Is.EqualTo(1));
        }

        [Test]
        public void ASteadyTier_DoesNotTouchTheEyeTextures()
        {
            var eyes = new FakeEyes();
            var applier = new QualityApplier(eyes);
            int selects = eyes.Selects;
            for (int f = 0; f < 600; f++)
                applier.Tick(f / 60.0);
            Assert.That(eyes.Selects, Is.EqualTo(selects));
        }

        [Test]
        public void ManualValues_StandUntilTheNextTierChange()
        {
            var applier = new QualityApplier(new FakeEyes());
            applier.CullRho = 2.5f;
            applier.GluedCopies = 3;
            for (int f = 0; f < 300; f++)
                applier.Tick(f / 60.0);
            Assert.That(applier.CullRho, Is.EqualTo(2.5f));
            Assert.That(applier.GluedCopies, Is.EqualTo(3));

            applier.ForcedTier = 4;
            applier.Tick(6.0);
            Assert.That(applier.CullRho, Is.EqualTo(3.5f));
            Assert.That(applier.GluedCopies, Is.EqualTo(7));
        }

        [Test]
        public void WorksWithoutEyeTextures()
        {
            var applier = new QualityApplier(null);
            applier.ForcedTier = 2;
            applier.Tick(0.0);
            Assert.That(applier.RenderScale, Is.EqualTo(0.6f));
            Assert.That(applier.Msaa, Is.EqualTo(2));
        }

        [Test]
        public void Reset_ReturnsToNominal()
        {
            var eyes = new FakeEyes();
            var applier = new QualityApplier(eyes);
            applier.ThermalOverride = 3;
            for (int t = 0; t <= 60; t++)
                applier.Tick(t);
            Assert.That(applier.Tier, Is.EqualTo(QualityTier.Critical));

            applier.ThermalOverride = 0;
            applier.Reset();
            Assert.That(applier.Tier, Is.EqualTo(QualityTier.Nominal));
            Assert.That(eyes.RenderScale, Is.EqualTo(0.8f));
            Assert.That(FramePacing.TargetFrameRate, Is.EqualTo(60));
        }
    }
}
