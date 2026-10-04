using System;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Kernel.Tests
{
    /// <summary>Shared helpers of the kernel fixtures. Everything takes kappa or a graph, so the curved
    /// cases can reuse them unchanged.</summary>
    internal static class KernelTestUtil
    {
        /// <summary>The seed every random kernel test uses (lane B contract, section 7).</summary>
        public const int Seed = 20261004;

        public static double MaxAbsDifference(Iso2 a, Iso2 b)
        {
            double worst = Math.Abs(a.M00 - b.M00);
            worst = Math.Max(worst, Math.Abs(a.M01 - b.M01));
            worst = Math.Max(worst, Math.Abs(a.M02 - b.M02));
            worst = Math.Max(worst, Math.Abs(a.M10 - b.M10));
            worst = Math.Max(worst, Math.Abs(a.M11 - b.M11));
            worst = Math.Max(worst, Math.Abs(a.M12 - b.M12));
            worst = Math.Max(worst, Math.Abs(a.M20 - b.M20));
            worst = Math.Max(worst, Math.Abs(a.M21 - b.M21));
            worst = Math.Max(worst, Math.Abs(a.M22 - b.M22));
            return worst;
        }

        public static double MaxAbsDifference(double3 a, double3 b)
        {
            return math.cmax(math.abs(a - b));
        }

        /// <summary>The generator of the 3-4-5 walk: one side of length step, then a left turn (Appendix A.4).</summary>
        public static Iso2 LapGenerator(double step, double turn, double kappa)
        {
            return Iso2.Boost(new double2(step, 0.0), kappa) * Iso2.Rot(turn, kappa);
        }

        public static Iso2 Power(Iso2 g, int n)
        {
            Iso2 result = Iso2.IdentityFor(g.Kappa);
            for (int i = 0; i < n; i++) result = result * g;
            return result;
        }

        public static double Range(System.Random random, double min, double max)
        {
            return min + (max - min) * random.NextDouble();
        }

        /// <summary>A random vector of length at most maxLength, uniform in direction.</summary>
        public static double2 RandomStep(System.Random random, double maxLength)
        {
            double angle = Range(random, -Math.PI, Math.PI);
            double length = maxLength * random.NextDouble();
            return new double2(length * Math.Cos(angle), length * Math.Sin(angle));
        }

        public static bool BitIdentical(Iso2 a, Iso2 b)
        {
            Span<double> x = stackalloc double[9];
            Span<double> y = stackalloc double[9];
            a.CopyTo(x);
            b.CopyTo(y);
            for (int i = 0; i < 9; i++)
            {
                if (BitConverter.DoubleToInt64Bits(x[i]) != BitConverter.DoubleToInt64Bits(y[i])) return false;
            }
            return BitConverter.DoubleToInt64Bits(a.Kappa) == BitConverter.DoubleToInt64Bits(b.Kappa);
        }

        public static void AssertBitIdentical(WalkerPose expected, WalkerPose actual)
        {
            Assert.That(actual.Tile, Is.EqualTo(expected.Tile));
            Assert.That(BitIdentical(expected.Local, actual.Local), Is.True, "the local frame changed");
        }
    }
}
