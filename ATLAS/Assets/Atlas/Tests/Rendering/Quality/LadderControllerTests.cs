using NUnit.Framework;

namespace Atlas.Rendering.Tests.Quality
{
    /// <summary>The stepping rule of the thermal ladder (Master Plan section 8.6, task P1-A07).</summary>
    public sealed class LadderControllerTests
    {
        static QualityInputs At(double time, int thermal = 0, float gpu = 5f, float frame = 16.7f, int forced = -1, bool perf = true)
            => new QualityInputs
            {
                Time = time, ThermalState = thermal, PerfValid = perf,
                GpuP95Ms = gpu, FrameP95Ms = frame, ForcedTier = forced,
            };

        [Test]
        public void StartsOnNominal_AndStaysThereWhenCool()
        {
            var ladder = new LadderController();
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Nominal));
            for (int t = 0; t < 120; t++)
                Assert.That(ladder.Step(At(t)), Is.False);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Nominal));
        }

        [Test]
        public void ThermalFair_StepsToFair()
        {
            var ladder = new LadderController();
            Assert.That(ladder.Step(At(0, thermal: 1)), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Fair));
        }

        [Test]
        public void GpuOverNineMs_StepsToFair_AndOverTwelve_ToSerious()
        {
            var ladder = new LadderController();
            Assert.That(ladder.Step(At(0, gpu: 9f)), Is.False, "exactly 9 ms is not over 9 ms");
            Assert.That(ladder.Step(At(1, gpu: 9.1f)), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Fair));

            Assert.That(ladder.Step(At(21, gpu: 12f)), Is.False, "exactly 12 ms is not over 12 ms");
            Assert.That(ladder.Step(At(22, gpu: 12.1f)), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Serious));
        }

        [Test]
        public void Serious_StepsToCritical_OnThermalCritical_OrWhenStillMissingSixtyFps()
        {
            var a = new LadderController();
            a.Reset(QualityTier.Serious);
            Assert.That(a.Step(At(0, thermal: 2)), Is.False);
            Assert.That(a.Step(At(1, thermal: 3)), Is.True);
            Assert.That(a.Tier, Is.EqualTo(QualityTier.Critical));

            var b = new LadderController();
            b.Reset(QualityTier.Serious);
            Assert.That(b.Step(At(0, thermal: 2, gpu: 14f, frame: 16.9f)), Is.False, "60 fps is held");
            Assert.That(b.Step(At(1, thermal: 2, gpu: 14f, frame: 19f)), Is.True);
            Assert.That(b.Tier, Is.EqualTo(QualityTier.Critical));
        }

        [Test]
        public void AtMostOneStepPerTwentySeconds()
        {
            var ladder = new LadderController();
            // Everything is bad at once: critical heat, slow GPU, missed frames.
            Assert.That(ladder.Step(At(100.0, thermal: 3, gpu: 20f, frame: 30f)), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Fair));

            for (double t = 100.5; t < 120.0; t += 0.5)
            {
                Assert.That(ladder.Step(At(t, thermal: 3, gpu: 20f, frame: 30f)), Is.False, "t = " + t);
                Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Fair));
            }

            Assert.That(ladder.Step(At(120.0, thermal: 3, gpu: 20f, frame: 30f)), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Serious));
            Assert.That(ladder.Step(At(139.9, thermal: 3, gpu: 20f, frame: 30f)), Is.False);
            Assert.That(ladder.Step(At(140.0, thermal: 3, gpu: 20f, frame: 30f)), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Critical));
        }

        [Test]
        public void NeverSkipsARung_DownOrUp()
        {
            var ladder = new LadderController();
            QualityTier previous = ladder.Tier;
            int changes = 0;

            // 200 s of the worst inputs, then 200 s of the best, evaluated once per second.
            for (int t = 0; t < 400; t++)
            {
                bool hot = t < 200;
                QualityInputs inputs = hot ? At(t, thermal: 3, gpu: 25f, frame: 40f) : At(t, thermal: 0, gpu: 2f, frame: 16.6f);
                if (ladder.Step(inputs))
                {
                    changes++;
                    int distance = System.Math.Abs((int)ladder.Tier - (int)previous);
                    Assert.That(distance, Is.EqualTo(1), "moved from " + previous + " to " + ladder.Tier);
                    previous = ladder.Tier;
                }
            }

            // Down: Fair, Serious, Critical. Critical ends the session: no automatic way back.
            Assert.That(changes, Is.EqualTo(3));
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Critical));
        }

        [Test]
        public void StepsBackUp_OneRungAtATime_WithTheSpacing()
        {
            var ladder = new LadderController();
            ladder.Reset(QualityTier.Serious);
            Assert.That(ladder.Step(At(0, thermal: 0, gpu: 3f)), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Fair));
            Assert.That(ladder.Step(At(10, thermal: 0, gpu: 3f)), Is.False);
            Assert.That(ladder.Step(At(19.9, thermal: 0, gpu: 3f)), Is.False);
            Assert.That(ladder.Step(At(20, thermal: 0, gpu: 3f)), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Nominal));
        }

        [Test]
        public void Hysteresis_GpuTime()
        {
            var ladder = new LadderController();
            Assert.That(ladder.Step(At(0, gpu: 9.5f)), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Fair));

            // Just under the 9 ms that caused the step is not enough to go back: it would bounce.
            for (int t = 20; t < 200; t += 1)
                Assert.That(ladder.Step(At(t, gpu: 8.5f)), Is.False);
            for (int t = 200; t < 260; t += 1)
                Assert.That(ladder.Step(At(t, gpu: 6.5f)), Is.False, "exactly 6.5 ms is not below 6.5 ms");
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Fair));

            Assert.That(ladder.Step(At(260, gpu: 6.4f)), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Nominal));

            // And from Serious: back to Fair only below 8 ms.
            ladder.Reset(QualityTier.Serious);
            Assert.That(ladder.Step(At(300, gpu: 11f)), Is.False);
            Assert.That(ladder.Step(At(301, gpu: 8f)), Is.False);
            Assert.That(ladder.Step(At(302, gpu: 7.9f)), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Fair));
        }

        [Test]
        public void Hysteresis_ThermalStateHoldsTheRung()
        {
            var ladder = new LadderController();
            ladder.Reset(QualityTier.Fair);
            // Still Fair-hot: a fast GPU alone does not step up.
            for (int t = 0; t < 100; t++)
                Assert.That(ladder.Step(At(t, thermal: 1, gpu: 2f)), Is.False);

            ladder.Reset(QualityTier.Serious);
            for (int t = 0; t < 100; t++)
                Assert.That(ladder.Step(At(t, thermal: 2, gpu: 2f)), Is.False);
            Assert.That(ladder.Step(At(100, thermal: 1, gpu: 2f)), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Fair));
        }

        [Test]
        public void WithoutPerfData_OnlyTheThermalStateCounts()
        {
            var ladder = new LadderController();
            Assert.That(ladder.Step(At(0, gpu: 50f, frame: 50f, perf: false)), Is.False);
            Assert.That(ladder.Step(At(1, thermal: 1, perf: false)), Is.True);
            // No GPU numbers: no step back up either.
            Assert.That(ladder.Step(At(30, thermal: 0, gpu: 0f, perf: false)), Is.False);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Fair));
        }

        [Test]
        public void ForcedTier_AppliesAtOnce_IgnoringTheSpacing()
        {
            var ladder = new LadderController();
            Assert.That(ladder.Step(At(0, thermal: 1)), Is.True);   // an automatic step just happened

            Assert.That(ladder.Step(At(0.1, forced: 3)), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Critical));
            Assert.That(ladder.IsForced, Is.True);
            Assert.That(ladder.Step(At(0.2, forced: 4)), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.High));
            Assert.That(ladder.Step(At(0.3, forced: 0)), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Nominal));

            // While forced, the inputs do not move it.
            for (int t = 1; t < 100; t++)
                Assert.That(ladder.Step(At(t, thermal: 3, gpu: 30f, frame: 40f, forced: 0)), Is.False);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Nominal));
        }

        [Test]
        public void LeavingAForcedTier_ContinuesFromThatRung_AfterAFullSpacing()
        {
            var ladder = new LadderController();
            ladder.Step(At(0, forced: 2));
            Assert.That(ladder.Step(At(5, thermal: 0, gpu: 2f)), Is.False, "released: stays on Serious for now");
            Assert.That(ladder.IsForced, Is.False);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Serious));
            Assert.That(ladder.Step(At(24.9, thermal: 0, gpu: 2f)), Is.False);
            Assert.That(ladder.Step(At(25, thermal: 0, gpu: 2f)), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Fair));
        }

        [Test]
        public void LeavingForcedHigh_GoesToNominal()
        {
            var ladder = new LadderController();
            ladder.Step(At(0, forced: 4));
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.High));
            Assert.That(ladder.Step(At(1)), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Nominal));
        }

        [Test]
        public void High_IsNeverEnteredAutomatically()
        {
            var ladder = new LadderController();
            for (int t = 0; t < 600; t++)
            {
                ladder.Step(At(t, thermal: 0, gpu: 0.5f, frame: 10f));
                Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Nominal));
            }
        }
    }
}
