using System;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Blades.Playground.Tests
{
    /// <summary>The log map lane C keeps for the home bearing: it must invert Exp in all three geometries (Appendix A.2).</summary>
    [TestFixture]
    public class HomeBearingLogTests
    {
        [Test]
        public void FlatLogIsThePointInTheChart()
        {
            double2 v = KernelHomeBearing.Log(new double3(1.5, -2.0, 1.0), 0.0);
            Assert.That(v.x, Is.EqualTo(1.5).Within(1e-12));
            Assert.That(v.y, Is.EqualTo(-2.0).Within(1e-12));
        }

        [TestCase(-1.0)]
        [TestCase(1.0)]
        public void CurvedLogInvertsExp(double kappa)
        {
            double2 t = new double2(0.3, -0.4);
            double r = math.length(t);
            double2 dir = t / r;
            double3 p = kappa < 0
                ? new double3(Math.Sinh(r) * dir.x, Math.Sinh(r) * dir.y, Math.Cosh(r))
                : new double3(Math.Sin(r) * dir.x, Math.Sin(r) * dir.y, Math.Cos(r));
            double2 v = KernelHomeBearing.Log(p, kappa);
            Assert.That(v.x, Is.EqualTo(t.x).Within(1e-12));
            Assert.That(v.y, Is.EqualTo(t.y).Within(1e-12));
        }

        [Test]
        public void LogOfTheBasePointIsZero()
        {
            double2 v = KernelHomeBearing.Log(new double3(0, 0, 1), -1.0);
            Assert.That(math.length(v), Is.EqualTo(0.0).Within(1e-12));
        }
    }
}
