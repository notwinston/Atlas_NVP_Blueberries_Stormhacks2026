using System;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Hands.Tests
{
    /// <summary>The One-Euro filter (Casiez, Roussel and Vogel, CHI 2012) that the hands filter is made of.</summary>
    public class HandsFilterOneEuroTests
    {
        const double Dt = 1.0 / 30.0;

        [Test]
        public void Alpha_IsTheSmoothingFactorOfAFirstOrderLowPass()
        {
            // alpha = dt / (dt + tau), tau = 1 / (2 pi cut-off).
            float cutoff = 1.5f, dt = 1f / 30f;
            float tau = 1f / (2f * math.PI * cutoff);
            Assert.That(OneEuro.Alpha(cutoff, dt), Is.EqualTo(dt / (dt + tau)).Within(1e-6f));
            Assert.That(OneEuro.Alpha(1e6f, dt), Is.GreaterThan(0.9999f), "a very high cut-off passes everything");
            Assert.That(OneEuro.Alpha(0.01f, dt), Is.LessThan(0.01f), "a very low one almost nothing");
            // A longer time between samples trusts the new one more; no time at all, not at all.
            Assert.That(OneEuro.Alpha(2f, 0.1f), Is.GreaterThan(OneEuro.Alpha(2f, 0.05f)));
            Assert.That(OneEuro.Alpha(2f, 0f), Is.EqualTo(0f));
            // A faster signal gets a higher cut-off, so a larger alpha.
            Assert.That(OneEuro.Alpha(6f, dt), Is.GreaterThan(OneEuro.Alpha(1.5f, dt)));
        }

        [Test]
        public void TheFirstSample_PassesThrough_WithNoVelocity()
        {
            var filter = new OneEuro(1.5f, 12f, 1f);
            Assert.That(filter.Started, Is.False);
            float3 value = filter.Filter(new float3(1f, 2f, 3f), 10.0);
            Assert.That(value, Is.EqualTo(new float3(1f, 2f, 3f)));
            Assert.That(filter.Started, Is.True);
            Assert.That(filter.Derivative, Is.EqualTo(float3.zero));
            Assert.That(filter.Time, Is.EqualTo(10.0));
        }

        [Test]
        public void ConstantInput_StaysWhereItIs()
        {
            var filter = new OneEuro(1.5f, 12f, 1f);
            float3 value = default(float3);
            for (int i = 0; i < 200; i++) value = filter.Filter(new float3(0.3f, -0.2f, 0.45f), i * Dt);
            Assert.That(math.distance(value, new float3(0.3f, -0.2f, 0.45f)), Is.LessThan(1e-6f));
            Assert.That(math.length(filter.Derivative), Is.LessThan(1e-6f));
        }

        [Test]
        public void NoiseAtRest_IsCutDownAsTheLowPassSays()
        {
            // With no speed term the filter is a plain low-pass: white noise comes out with a standard deviation
            // of sqrt(alpha / (2 - alpha)) times what went in.
            var filter = new OneEuro(1.5f, 0f, 1f);
            var rng = new HandsFilterRng(42u);
            const int n = 30000;
            double sumIn = 0.0, sumOut = 0.0;
            for (int i = 0; i < n; i++)
            {
                float x = rng.Gaussian();
                float y = filter.Filter(x, i * Dt);
                if (i < 100) continue;
                sumIn += x * x;
                sumOut += y * y;
            }
            float alpha = OneEuro.Alpha(1.5f, (float)Dt);
            double expected = Math.Sqrt(alpha / (2f - alpha));
            double measured = Math.Sqrt(sumOut / sumIn);
            Assert.That(measured, Is.EqualTo(expected).Within(0.04 * expected));
        }

        [Test]
        public void ConstantVelocity_IsFollowedWithTheLowPassLag_AndTheDerivativeIsTheVelocity()
        {
            // No speed term: a ramp comes out late by v * dt * (1 - alpha) / alpha, and the derivative is v.
            var filter = new OneEuro(2f, 0f, 1f);
            float3 velocity = new float3(0.4f, -0.1f, 0.2f);
            float3 value = default(float3);
            double time = 0.0;
            for (int i = 0; i < 150; i++)
            {
                time = i * Dt;
                value = filter.Filter(velocity * (float)time, time);
            }
            float alpha = OneEuro.Alpha(2f, (float)Dt);
            float3 lag = velocity * ((float)Dt * (1f - alpha) / alpha);
            Assert.That(math.distance(velocity * (float)time - value, lag), Is.LessThan(0.03f * math.length(lag)));
            Assert.That(math.distance(filter.Derivative, velocity), Is.LessThan(1e-3f), "the velocity is not biased by the lag");
        }

        [Test]
        public void TheCutoffRisesWithSpeed_SoAFastHandLagsLess()
        {
            // The same ramp through the same minimum cut-off: the speed term shortens the lag.
            var plain = new OneEuro(1.5f, 0f, 1f);
            var adaptive = new OneEuro(1.5f, 12f, 1f);
            float3 velocity = new float3(0.8f, 0f, 0f);
            float3 a = default(float3), b = default(float3);
            double time = 0.0;
            for (int i = 0; i < 150; i++)
            {
                time = i * Dt;
                float3 x = velocity * (float)time;
                a = plain.Filter(x, time);
                b = adaptive.Filter(x, time);
            }
            float lagPlain = velocity.x * (float)time - a.x;
            float lagAdaptive = velocity.x * (float)time - b.x;
            Assert.That(lagAdaptive, Is.LessThan(0.35f * lagPlain), "lag with the speed term " + lagAdaptive + " m, without " + lagPlain + " m");
            // At speed the lag settles near 1 / (2 pi beta) metres, the One-Euro filter's own figure.
            Assert.That(lagAdaptive, Is.InRange(0.008f, 0.02f));
        }

        [Test]
        public void ScalarAndVectorFilters_Agree()
        {
            var scalar = new OneEuro(1.5f, 12f, 1f);
            var vector = new OneEuro(1.5f, 12f, 1f);
            var rng = new HandsFilterRng(7u);
            for (int i = 0; i < 300; i++)
            {
                float x = 0.3f * (float)Math.Sin(i * 0.1) + 0.01f * rng.Gaussian();
                float s = scalar.Filter(x, i * Dt);
                float3 v = vector.Filter(new float3(x, 0f, 0f), i * Dt);
                Assert.That(s, Is.EqualTo(v.x).Within(1e-6f));
            }
        }

        [Test]
        public void TimeThatDoesNotAdvance_AndBadValues_ChangeNothing()
        {
            var filter = new OneEuro(1.5f, 12f, 1f);
            filter.Filter(new float3(0f, 0f, 0f), 1.0);
            filter.Filter(new float3(1f, 0f, 0f), 1.0 + Dt);
            float3 value = filter.Value, derivative = filter.Derivative;
            Assert.That(filter.Filter(new float3(5f, 5f, 5f), 1.0 + Dt), Is.EqualTo(value), "the same time again");
            Assert.That(filter.Filter(new float3(5f, 5f, 5f), 0.5), Is.EqualTo(value), "an earlier time");
            Assert.That(filter.Filter(new float3(float.NaN, 0f, 0f), 2.0), Is.EqualTo(value), "a value that is not a number");
            Assert.That(filter.Filter(new float3(0f, 0f, float.PositiveInfinity), 2.0), Is.EqualTo(value));
            Assert.That(filter.Filter(new float3(1f, 1f, 1f), double.NaN), Is.EqualTo(value), "a time that is not a number");
            Assert.That(filter.Derivative, Is.EqualTo(derivative));
            Assert.That(filter.Time, Is.EqualTo(1.0 + Dt));
        }

        [Test]
        public void Reset_ForgetsTheSamples_AndKeepsTheSettings()
        {
            var filter = new OneEuro(1.5f, 12f, 1f);
            for (int i = 0; i < 10; i++) filter.Filter(new float3(i, 0f, 0f), i * Dt);
            filter.Reset();
            Assert.That(filter.Started, Is.False);
            Assert.That(filter.Derivative, Is.EqualTo(float3.zero));
            Assert.That(filter.MinCutoff, Is.EqualTo(1.5f));
            Assert.That(filter.Beta, Is.EqualTo(12f));
            Assert.That(filter.DerivativeCutoff, Is.EqualTo(1f));
            Assert.That(filter.Filter(new float3(7f, 7f, 7f), 0.0), Is.EqualTo(new float3(7f, 7f, 7f)), "the next sample passes through");
        }

        [Test]
        public void ARotation_FirstSamplePassesThrough_AndSpeedIsTheAngularSpeed()
        {
            var filter = new OneEuroRotation { MinCutoff = 1.5f, Beta = 0f, DerivativeCutoff = 1f };
            quaternion first = quaternion.AxisAngle(math.normalize(new float3(1f, 2f, 3f)), 0.7f);
            Assert.That(Angle(filter.Filter(first, 0.0), first), Is.LessThan(1e-5f));
            Assert.That(filter.AngularSpeed, Is.EqualTo(0f));

            // Turning at 2 rad/s about a fixed axis: the speed estimate converges to 2.
            float3 axis = math.normalize(new float3(0.3f, 1f, -0.2f));
            var turning = new OneEuroRotation { MinCutoff = 2f, Beta = 0f, DerivativeCutoff = 1f };
            quaternion value = quaternion.identity;
            double time = 0.0;
            for (int i = 0; i < 150; i++)
            {
                time = i * Dt;
                value = turning.Filter(quaternion.AxisAngle(axis, 2f * (float)time), time);
            }
            Assert.That(turning.AngularSpeed, Is.EqualTo(2f).Within(0.02f));
            // It lags by the low-pass: omega * dt * (1 - alpha) / alpha, in radians.
            float alpha = OneEuro.Alpha(2f, (float)Dt);
            float expectedLag = 2f * (float)Dt * (1f - alpha) / alpha;
            quaternion truth = quaternion.AxisAngle(axis, 2f * (float)time);
            float lag = Angle(truth, value);
            Assert.That(lag, Is.EqualTo(expectedLag).Within(0.05f * expectedLag));
        }

        [Test]
        public void ARotation_TakesTheShortWayRound_AndSignMeansNothing()
        {
            var filter = new OneEuroRotation { MinCutoff = 1.5f, Beta = 12f, DerivativeCutoff = 1f };
            quaternion q = quaternion.AxisAngle(new float3(0f, 1f, 0f), 0.4f);
            filter.Filter(q, 0.0);
            // The same rotation written with the opposite sign is the same rotation: nothing moves, no speed.
            quaternion negated = new quaternion(-q.value);
            quaternion value = filter.Filter(negated, Dt);
            Assert.That(Angle(q, value), Is.LessThan(1e-4f));
            Assert.That(filter.AngularSpeed, Is.LessThan(1e-3f));

            // A turn across the point where the sign of a quaternion flips is a smooth turn.
            var across = new OneEuroRotation { MinCutoff = 1.5f, Beta = 0f, DerivativeCutoff = 1f };
            float3 axis = new float3(0f, 0f, 1f);
            float previous = -1f;
            for (int i = 0; i < 120; i++)
            {
                float angle = 6.0f + 0.05f * i;      // through 2 pi, where the quaternion's w goes through zero
                quaternion sample = quaternion.AxisAngle(axis, angle);
                quaternion filtered = across.Filter(sample, i * Dt);
                float away = Angle(sample, filtered);
                Assert.That(away, Is.LessThan(0.3f), "step " + i);
                if (previous >= 0f) Assert.That(math.abs(away - previous), Is.LessThan(0.05f), "no jump at step " + i);
                previous = away;
            }
        }

        static float Angle(quaternion a, quaternion b)
        {
            float d = math.abs(math.dot(a.value, b.value));
            return 2f * math.acos(math.clamp(d, -1f, 1f));
        }
    }
}
