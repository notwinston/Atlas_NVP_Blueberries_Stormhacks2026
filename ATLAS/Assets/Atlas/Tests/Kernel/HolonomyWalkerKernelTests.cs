using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Kernel.Tests
{
    /// <summary>The holonomy rig behind IWalkerKernel (lane-b-kernel.md section 4): the frame loop's hook on
    /// the {4,5} and {4,6} graphs, and the flat kernel unchanged at q = 4.</summary>
    [TestFixture]
    public class HolonomyWalkerKernelTests
    {
        private const double Side = 2.0;
        private const double L5 = Side / 1.0612750619050357;
        private const double L6 = Side / 1.3169578969248166;

        private static readonly double2[] Directions =
            { new double2(1, 0), new double2(0, 1), new double2(-1, 0), new double2(0, -1) };

        /// <summary>The head positions of a walk of the given number of sides, turning left, in 5 cm steps.</summary>
        private static List<double2> Lap(int sides, double side, double2 start)
        {
            var points = new List<double2>();
            double2 corner = start;
            int n = (int)Math.Round(side / 0.05);
            for (int k = 0; k < sides; k++)
            {
                for (int i = 1; i <= n; i++) points.Add(corner + Directions[k & 3] * (side * i / n));
                corner += Directions[k & 3] * side;
            }
            return points;
        }

        private static void AssertSamePose(WalkerPose expected, WalkerPose actual)
        {
            Assert.That(actual.Tile, Is.EqualTo(expected.Tile));
            Assert.That(KernelTestUtil.MaxAbsDifference(expected.Local, actual.Local), Is.LessThan(1e-12));
        }

        [Test]
        public void HolonomyWalker_FlatByDefault()
        {
            IWalkerKernel kernel = new HolonomyWalkerKernel();
            var flat = new FlatWalkerKernel();
            Assert.That(kernel.Graph.Q, Is.EqualTo(4));
            Assert.That(kernel.Kappa, Is.EqualTo(Curvature.Flat));
            Assert.That(kernel.MetresPerUnit, Is.EqualTo(KernelConstants.FlatMetresPerUnit));
            Assert.That(kernel.CullRho, Is.EqualTo(KernelConstants.DefaultCullRho));
            Assert.That(((HolonomyWalkerKernel)kernel).Rig, Is.Null);

            kernel.Configure(KernelConfig.Flat(1.5));
            flat.Configure(KernelConfig.Flat(1.5));
            kernel.CullRho = 2.5;
            flat.CullRho = 2.5;
            var random = new System.Random(KernelTestUtil.Seed);
            var head = new double2(0.3, -0.4);
            kernel.ResetToFloor(head);
            flat.ResetToFloor(head);
            for (int n = 0; n < 2000; n++)
            {
                head += KernelTestUtil.RandomStep(random, n % 97 == 0 ? 0.3 : 0.19);
                bool tracking = n % 53 != 0;
                KernelStep a = kernel.Step(head, tracking);
                KernelStep b = flat.Step(head, tracking);
                Assert.That(a.Rejected, Is.EqualTo(b.Rejected));
                Assert.That(a.Crossings, Is.EqualTo(b.Crossings));
                Assert.That(a.LastEdge, Is.EqualTo(b.LastEdge));
                KernelTestUtil.AssertBitIdentical(flat.Pose, kernel.Pose);
                Assert.That(kernel.Candidates.Count, Is.EqualTo(flat.Candidates.Count));
            }
            Assert.That(kernel.StepCount, Is.EqualTo(flat.StepCount));
            Assert.That(kernel.LastStep.AppliedMetres, Is.EqualTo(flat.LastStep.AppliedMetres));
        }

        [TestCase(5, L5)]
        [TestCase(6, L6)]
        public void HolonomyWalker_Configure(int q, double l)
        {
            var kernel = new HolonomyWalkerKernel();
            kernel.Configure(KernelConfig.Curved(q, l));

            Assert.That(kernel.Graph, Is.InstanceOf<TileGraph>());
            Assert.That(kernel.Graph.Q, Is.EqualTo(q));
            Assert.That(kernel.Kappa, Is.EqualTo(Curvature.Hyperbolic));
            Assert.That(kernel.MetresPerUnit, Is.EqualTo(l).Within(1e-12));
            Assert.That(kernel.Graph.CentreSpacing * kernel.MetresPerUnit, Is.EqualTo(Side).Within(1e-9), "the lap square is the tile spacing");
            Assert.That(kernel.Pose.Tile, Is.EqualTo(TileId.Origin));
            Assert.That(kernel.Pose.Local, Is.EqualTo(Iso2.IdentityFor(Curvature.Hyperbolic)));
            Assert.That(kernel.Pose.IsValid, Is.True);
            Assert.That(kernel.LastStep.LastEdge, Is.EqualTo(-1));
            Assert.That(kernel.Rig, Is.Not.Null);

            var expected = new List<VisibleTile>(1024);
            double radius = kernel.CullRho + 2.0 * kernel.Graph.CircumRadius + KernelConstants.CrossHysteresis;
            new TileGraph(q).VisibleSet(TileId.Origin, radius, expected);
            List<VisibleTile> candidates = kernel.Candidates;
            Assert.That(candidates.Count, Is.EqualTo(expected.Count));
            for (int n = 0; n < expected.Count; n++) Assert.That(candidates[n].Tile, Is.EqualTo(expected[n].Tile));

            // Setting rho marks the list stale; it is rebuilt inside the next Step, in the same List.
            kernel.CullRho = 2.0;
            Assert.That(kernel.Candidates.Count, Is.EqualTo(expected.Count));
            kernel.Step(double2.zero, true);
            Assert.That(kernel.Candidates, Is.SameAs(candidates));
            Assert.That(kernel.Candidates.Count, Is.LessThan(expected.Count));
            Assert.Throws<ArgumentOutOfRangeException>(() => kernel.CullRho = -1.0);
            Assert.Throws<ArgumentOutOfRangeException>(() => kernel.CullRho = double.NaN);

            // Back to the flat dial position.
            kernel.Configure(KernelConfig.Flat(Side));
            Assert.That(kernel.Graph.Q, Is.EqualTo(4));
            Assert.That(kernel.Kappa, Is.EqualTo(Curvature.Flat));
            Assert.That(kernel.Rig, Is.Null);
            Assert.That(kernel.CullRho, Is.EqualTo(2.0));
        }

        [TestCase(5, L5)]
        [TestCase(6, L6)]
        public void HolonomyWalker_MatchesRig(int q, double l)
        {
            // The hook is the rig: the same head positions give the same tile and the same U, bit for bit.
            var kernel = new HolonomyWalkerKernel();
            kernel.Configure(KernelConfig.Curved(q, l));
            var rig = new HolonomyRig(new TileGraph(q));
            var start = new double2(-0.7, -0.8);
            var pose = new WalkerPose { Tile = TileId.Origin, Local = TileZeroPlacement.Registered(start, Curvature.Hyperbolic, l) };
            rig.Reset(pose, Curvature.Hyperbolic, l);
            kernel.ResetToFloor(start);

            // ResetToFloor has shown the rig the floor point, as its first sample.
            long frame = 0;
            rig.Step(frame, new HeadSample(frame, 0.0, new double3(start.x, 1.6, start.y), true));
            KernelTestUtil.AssertBitIdentical(rig.Pose, kernel.Pose);
            int crossings = 0;
            foreach (double2 p in Lap(q, Side, start))
            {
                frame++;
                HolonomyRigResult r = rig.Step(frame, new HeadSample(frame, frame / 60.0, new double3(p.x, 1.6, p.y), true));
                KernelStep step = kernel.Step(p, true);

                Assert.That(step.Rejected, Is.False);
                Assert.That(step.Crossings, Is.EqualTo(r.Crossings));
                Assert.That(step.LastEdge, Is.EqualTo(r.LastEdge));
                Assert.That(step.AppliedMetres, Is.EqualTo(r.Applied));
                Assert.That(step.TileChanged, Is.EqualTo(r.Crossings > 0));
                KernelTestUtil.AssertBitIdentical(rig.Pose, kernel.Pose);
                Assert.That(kernel.Pose.IsValid, Is.True);
                Assert.That(kernel.Candidates[0].Tile, Is.EqualTo(kernel.Pose.Tile), "the candidates follow the walker's tile");
                crossings += step.Crossings;
            }
            Assert.That(crossings, Is.GreaterThan(0));
            Assert.That(kernel.StepCount, Is.EqualTo(frame));
            Assert.That(kernel.LastStep.Crossings, Is.EqualTo(0));
        }

        [TestCase(5, L5)]
        [TestCase(6, L6)]
        public void HolonomyWalker_LapCloses(int q, double l)
        {
            // The 3-4-5 walk through the hook: q sides of the lap square, turning left, end where they began
            // (Appendix A.4), although the room was walked round q / 4 times.
            var kernel = new HolonomyWalkerKernel();
            kernel.Configure(KernelConfig.Curved(q, l));
            var start = new double2(-0.7, -0.8);
            kernel.ResetToFloor(start);
            WalkerPose first = kernel.Pose;

            List<double2> walk = Lap(q, Side, start);
            for (int n = 0; n < walk.Count; n++)
            {
                kernel.Step(walk[n], true);
                if (n + 1 == walk.Count / q * 4 && q != 4)
                {
                    // One lap of the room is not a lap of the world.
                    double apart = Geometry2.Distance(kernel.Pose.Local.Position, first.Local.Position, Curvature.Hyperbolic);
                    Assert.That(kernel.Pose.Tile != first.Tile || apart > 0.5, Is.True);
                }
            }

            Assert.That(kernel.Pose.Tile, Is.EqualTo(first.Tile));
            Assert.That(Geometry2.Distance(kernel.Pose.Local.Position, first.Local.Position, Curvature.Hyperbolic) * l,
                Is.LessThan(1e-9), "back at the start");
            // The world has turned against the room by the q quarter turns of the walk, less the whole turn of the loop.
            double turn = WalkerHeading.Of(kernel.Pose.Local) - WalkerHeading.Of(first.Local);
            double expected = -(q - 4) * Math.PI / 2.0;
            Assert.That(Math.Abs(Math.IEEERemainder(turn - expected, 2.0 * Math.PI)), Is.LessThan(1e-9));
        }

        [Test]
        public void HolonomyWalker_JumpRejected()
        {
            var kernel = new HolonomyWalkerKernel();
            kernel.Configure(KernelConfig.Curved(5, L5));
            var head = new double2(0.1, 0.2);
            kernel.ResetToFloor(head);
            long steps = kernel.StepCount;

            // The point given to ResetToFloor is the previous head position: the first step is integrated.
            head += new double2(0.05, 0.0);
            KernelStep step = kernel.Step(head, true);
            Assert.That(step.Rejected, Is.False);
            Assert.That(step.AppliedMetres.x, Is.EqualTo(0.05).Within(1e-12));

            // A jump is rejected once and leaves the pose where it was (the rig still renormalises U every frame).
            WalkerPose before = kernel.Pose;
            head += new double2(0.25, 0.0);
            step = kernel.Step(head, true);
            Assert.That(step.Rejected, Is.True);
            Assert.That(step.AppliedMetres, Is.EqualTo(double2.zero));
            AssertSamePose(before, kernel.Pose);
            head += new double2(0.05, 0.0);
            step = kernel.Step(head, true);
            Assert.That(step.Rejected, Is.False);
            Assert.That(step.AppliedMetres.x, Is.EqualTo(0.05).Within(1e-12));

            // A head position that is not finite is rejected and is not remembered.
            before = kernel.Pose;
            foreach (double broken in new[] { double.NaN, double.PositiveInfinity })
            {
                step = kernel.Step(new double2(broken, 0.0), true);
                Assert.That(step.Rejected, Is.True);
                AssertSamePose(before, kernel.Pose);
            }
            head += new double2(0.0, 0.05);
            step = kernel.Step(head, true);
            Assert.That(step.Rejected, Is.False);
            Assert.That(step.AppliedMetres.y, Is.EqualTo(0.05).Within(1e-12));

            // Movement made while tracking is lost is never integrated.
            before = kernel.Pose;
            head += new double2(0.05, 0.0);
            step = kernel.Step(head, false);
            Assert.That(step.Rejected, Is.True);
            AssertSamePose(before, kernel.Pose);
            step = kernel.Step(head, true);
            Assert.That(step.Rejected, Is.False);
            Assert.That(step.AppliedMetres, Is.EqualTo(double2.zero));

            Assert.That(kernel.StepCount - steps, Is.EqualTo(8));
            Assert.That(kernel.Rig.RejectedJumps, Is.EqualTo(1));
            Assert.That(kernel.Rig.RejectedCalls, Is.EqualTo(0));
            Assert.That(kernel.Rig.MissedFrames, Is.EqualTo(0));

            // The first Step after Reset integrates nothing: it only records the head position.
            kernel.Reset(kernel.Pose);
            before = kernel.Pose;
            step = kernel.Step(head + new double2(0.1, 0.1), true);
            Assert.That(step.Rejected, Is.False);
            Assert.That(step.AppliedMetres, Is.EqualTo(double2.zero));
            Assert.That(Geometry2.Distance(kernel.Pose.Local.Position, before.Local.Position, Curvature.Hyperbolic), Is.LessThan(1e-12));
        }

        [Test]
        public void HolonomyWalker_ConfigureAndReset()
        {
            var kernel = new HolonomyWalkerKernel();
            kernel.Configure(KernelConfig.Curved(5, L5));

            Assert.Throws<NotSupportedException>(() => kernel.Configure(KernelConfig.Curved(3, 1.2)));
            Assert.Throws<ArgumentOutOfRangeException>(() => kernel.Configure(KernelConfig.Curved(7, 1.2)));
            foreach (double bad in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => kernel.Configure(KernelConfig.Curved(6, bad)));
                Assert.Throws<ArgumentOutOfRangeException>(() => kernel.Configure(KernelConfig.Flat(bad)));
            }
            // A refused Configure changes nothing.
            Assert.That(kernel.Graph.Q, Is.EqualTo(5));
            Assert.That(kernel.MetresPerUnit, Is.EqualTo(L5).Within(1e-12));

            // ResetToFloor is the registered placement in tile zero (Master Plan section 4.6), re-rooted.
            var p = new double2(0.4, -0.3);
            kernel.ResetToFloor(p);
            Assert.That(kernel.Pose.Tile, Is.EqualTo(TileId.Origin));
            Assert.That(KernelTestUtil.MaxAbsDifference(kernel.Pose.Local, TileZeroPlacement.Registered(p, Curvature.Hyperbolic, L5)),
                Is.LessThan(1e-12));
            Assert.That(math.length(Geometry2.Log(kernel.Pose.Local.Position, kernel.Kappa)) * kernel.MetresPerUnit,
                Is.EqualTo(0.5).Within(1e-12));
            Assert.Throws<ArgumentOutOfRangeException>(() => kernel.ResetToFloor(new double2(double.NaN, 0.0)));

            // A floor point outside tile zero is re-rooted into the tile it lies in.
            kernel.ResetToFloor(new double2(1.5, 0.0));
            Assert.That(kernel.Pose.Tile, Is.EqualTo(kernel.Graph.Neighbour(TileId.Origin, 0)));
            Assert.That(kernel.Pose.IsValid, Is.True);
            Assert.That(kernel.Candidates[0].Tile, Is.EqualTo(kernel.Pose.Tile));
            Iso2 inZero = kernel.Graph.EdgeStep(TileId.Origin, 0) * kernel.Pose.Local;
            Assert.That(KernelTestUtil.MaxAbsDifference(inZero, TileZeroPlacement.Registered(new double2(1.5, 0.0), Curvature.Hyperbolic, L5)),
                Is.LessThan(1e-12));

            // Reset takes the frame-loop start pose (flat-tagged identity) and any hyperbolic pose.
            kernel.Reset(WalkerPose.Start);
            Assert.That(kernel.Pose.Tile, Is.EqualTo(TileId.Origin));
            Assert.That(kernel.Pose.Local, Is.EqualTo(Iso2.IdentityFor(Curvature.Hyperbolic)));
            var far = new WalkerPose { Tile = TileId.Origin.Step(1), Local = Iso2.Boost(new double2(0.2, 0.1), Curvature.Hyperbolic) * Iso2.Rot(0.4) };
            kernel.Reset(far);
            KernelTestUtil.AssertBitIdentical(far, kernel.Pose);

            Assert.Throws<ArgumentException>(() => kernel.Reset(default(WalkerPose)));
            Assert.Throws<ArgumentException>(() => kernel.Reset(
                new WalkerPose { Tile = FlatTileGraph.Cell(1, 0), Local = Iso2.Identity }));
            Assert.Throws<ArgumentException>(() => kernel.Reset(
                new WalkerPose { Tile = TileId.Origin, Local = Iso2.Boost(new double2(0.5, 0.0), Curvature.Flat) }));
            Assert.Throws<ArgumentException>(() => kernel.Reset(
                new WalkerPose { Tile = TileId.Origin, Local = Iso2.IdentityFor(Curvature.Spherical) }));
            KernelTestUtil.AssertBitIdentical(far, kernel.Pose);
        }

        [Test]
        public void HolonomyWalker_RampNeedsTheClock()
        {
            // The dial's ramp is the rig's and runs only while the walker stands (Master Plan section 4.6):
            // the timed Step advances it, the untimed one of the interface does not.
            var kernel = new HolonomyWalkerKernel();
            kernel.Configure(KernelConfig.Curved(5, L5));
            var head = new double2(0.1, 0.0);
            kernel.ResetToFloor(head);
            kernel.Rig.SetCurvatureTarget(Curvature.Flat, 1.0);

            for (int n = 0; n < 600; n++) kernel.Step(head, true);
            Assert.That(kernel.Kappa, Is.EqualTo(Curvature.Hyperbolic));
            Assert.That(kernel.MetresPerUnit, Is.EqualTo(L5).Within(1e-12));

            for (int n = 1; n <= 300; n++) kernel.Step(head, true, n / 60.0);
            Assert.That(kernel.Kappa, Is.EqualTo(Curvature.Flat));
            Assert.That(kernel.MetresPerUnit, Is.EqualTo(KernelConstants.FlatMetresPerUnit));
            // The log-chart position in metres is kept across the ramp.
            Assert.That(kernel.Pose.Local.M02, Is.EqualTo(0.1).Within(1e-9));
        }
    }
}
