using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Kernel.Tests
{
    /// <summary>The holonomy rig behind IWalkerKernel (lane-b-kernel.md section 4): the frame loop's hook on
    /// the {4,5} and {4,6} graphs, the sphere walker on the six faces at q = 3, the flat kernel unchanged at
    /// q = 4, and the dial a curved space drives through IDialKernel.</summary>
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

            Assert.Throws<ArgumentOutOfRangeException>(() => kernel.Configure(KernelConfig.Curved(7, 1.2)));
            foreach (double bad in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => kernel.Configure(KernelConfig.Curved(6, bad)));
                Assert.Throws<ArgumentOutOfRangeException>(() => kernel.Configure(KernelConfig.Curved(3, bad)));
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

        [Test]
        public void HolonomyWalker_DialRamp_OnlyWhileStanding()
        {
            // What Curved Walk does through IDialKernel: a registered flat stand on the {4,5} graph, then the
            // ramp to the dial value, which moves only in timed steps that find the walker standing.
            IDialKernel kernel = new HolonomyWalkerKernel();
            Assert.That(kernel.Q, Is.EqualTo(4));
            Assert.That(kernel.SetCurvatureTarget(Curvature.Hyperbolic, L5), Is.False, "the flat kernel has no ramp");
            Assert.That(kernel.Reaim(0.3), Is.False);
            Assert.That(kernel.ConfiguredMetresPerUnit, Is.EqualTo(KernelConstants.FlatMetresPerUnit));

            kernel.Configure(KernelConfig.Curved(5, L5));
            Assert.That(kernel.Q, Is.EqualTo(5));
            Assert.That(kernel.CurvatureNow, Is.EqualTo(-1.0 / (L5 * L5)).Within(1e-15));
            Assert.That(kernel.Ramping, Is.False);

            var head = new double2(0.4, -0.3);
            kernel.ResetToFloorFlat(head);
            Assert.That(kernel.Graph.Q, Is.EqualTo(5), "the tiling stays");
            Assert.That(kernel.Kappa, Is.EqualTo(Curvature.Flat));
            Assert.That(kernel.MetresPerUnit, Is.EqualTo(KernelConstants.FlatMetresPerUnit));
            Assert.That(kernel.ConfiguredMetresPerUnit, Is.EqualTo(L5));
            Assert.That(kernel.CurvatureNow, Is.EqualTo(0.0));
            Assert.That(kernel.CurvatureTarget, Is.EqualTo(0.0));
            Assert.That(kernel.Pose.Tile, Is.EqualTo(TileId.Zero));
            Assert.That(kernel.Pose.Local.M02, Is.EqualTo(0.4));
            Assert.That(kernel.Pose.Local.M12, Is.EqualTo(-0.3));
            Assert.That(kernel.Candidates[0].Tile, Is.EqualTo(TileId.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => kernel.ResetToFloorFlat(new double2(double.NaN, 0.0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => kernel.SetCurvatureTarget(Curvature.Spherical, 1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => kernel.SetCurvatureTarget(Curvature.Hyperbolic, 0.0));

            double target = -1.0 / (L5 * L5);
            Assert.That(kernel.SetCurvatureTarget(Curvature.Hyperbolic, L5), Is.True);
            Assert.That(kernel.CurvatureTarget, Is.EqualTo(target).Within(1e-15));
            Assert.That(kernel.Ramping, Is.True);

            // Walking at 1 m/s: the ramp waits.
            double time = 0.0;
            const double dt = 1.0 / 60.0;
            for (int n = 0; n < 60; n++)
            {
                time += dt;
                head += new double2(dt, 0.0);
                kernel.Step(head, true, time);
                Assert.That(kernel.Still, Is.False);
                Assert.That(kernel.CurvatureChanged, Is.False);
            }
            Assert.That(kernel.CurvatureNow, Is.EqualTo(0.0));
            Assert.That(kernel.Pose.Local.M02, Is.EqualTo(1.4).Within(1e-9), "flat metres while K = 0");

            // Standing for two of the four seconds: half way.
            for (int n = 0; n < 120; n++)
            {
                time += dt;
                kernel.Step(head, true, time);
                Assert.That(kernel.Still, Is.True);
                Assert.That(kernel.CurvatureChanged, Is.True);
            }
            Assert.That(kernel.CurvatureNow, Is.EqualTo(0.5 * target).Within(1e-9));
            Assert.That(kernel.Kappa, Is.EqualTo(Curvature.Hyperbolic));
            Assert.That(kernel.MetresPerUnit, Is.EqualTo(L5 * Math.Sqrt(2.0)).Within(1e-6), "the scale of this frame");
            Assert.That(kernel.ConfiguredMetresPerUnit, Is.EqualTo(L5), "the dial's target does not follow the ramp");

            // The same target again does not start the four seconds afresh; steps with no time and steps
            // while walking leave the curvature where it is.
            Assert.That(kernel.SetCurvatureTarget(Curvature.Hyperbolic, L5), Is.True);
            double held = kernel.CurvatureNow;
            for (int n = 0; n < 30; n++) kernel.Step(head, true);
            Assert.That(kernel.Still, Is.False);
            for (int n = 0; n < 30; n++)
            {
                time += dt;
                head += new double2(0.0, dt);
                kernel.Step(head, true, time);
            }
            Assert.That(kernel.CurvatureNow, Is.EqualTo(held));
            Assert.That(kernel.Ramping, Is.True);

            // Standing again: the first standing step follows a walking one; then the rest of the ramp.
            for (int n = 0; n < 130; n++)
            {
                time += dt;
                kernel.Step(head, true, time);
            }
            Assert.That(kernel.CurvatureNow, Is.EqualTo(target));
            Assert.That(kernel.Ramping, Is.False);
            Assert.That(kernel.CurvatureChanged, Is.False);
            Assert.That(kernel.MetresPerUnit, Is.EqualTo(L5).Within(1e-12));
            Assert.That(kernel.Pose.IsValid, Is.True);

            // Back to flat on the same tiling, and registered again where the head is.
            Assert.That(kernel.SetCurvatureTarget(Curvature.Flat, 0.0), Is.True);
            Assert.That(kernel.ConfiguredMetresPerUnit, Is.EqualTo(L5));
            for (int n = 0; n < 250; n++)
            {
                time += dt;
                kernel.Step(head, true, time);
            }
            Assert.That(kernel.CurvatureNow, Is.EqualTo(0.0));
            Assert.That(kernel.MetresPerUnit, Is.EqualTo(KernelConstants.FlatMetresPerUnit));
            kernel.ResetToFloorFlat(head);
            Assert.That(kernel.Pose.Tile, Is.EqualTo(TileId.Zero));
            Assert.That(kernel.Pose.Local.M02, Is.EqualTo(head.x));
            Assert.That(kernel.Pose.Local.M12, Is.EqualTo(head.y));
            Assert.That(kernel.Still, Is.False);

            // A recentre on the flat stand stays flat; with a ramp under way it registers at the dial value.
            kernel.ResetToFloor(new double2(0.2, 0.1));
            Assert.That(kernel.Kappa, Is.EqualTo(Curvature.Flat));
            Assert.That(kernel.Pose.Local.M02, Is.EqualTo(0.2));
            kernel.SetCurvatureTarget(Curvature.Hyperbolic, L5);
            kernel.ResetToFloor(new double2(0.2, 0.1));
            Assert.That(kernel.Ramping, Is.False);
            Assert.That(kernel.CurvatureNow, Is.EqualTo(target).Within(1e-15));
            Assert.That(KernelTestUtil.MaxAbsDifference(kernel.Pose.Local,
                TileZeroPlacement.Registered(new double2(0.2, 0.1), Curvature.Hyperbolic, L5)), Is.LessThan(1e-12));
        }

        [Test]
        public void HolonomyWalker_Reaim()
        {
            // U becomes U * Rot(alpha): the walker turns where it stands (Master Plan section 10.2.3).
            IDialKernel kernel = new HolonomyWalkerKernel();
            kernel.Configure(KernelConfig.Curved(5, L5));
            kernel.ResetToFloor(new double2(0.3, 0.2));
            WalkerPose before = kernel.Pose;
            Assert.That(kernel.Reaim(0.7), Is.True);
            Assert.That(kernel.Pose.Tile, Is.EqualTo(before.Tile));
            Assert.That(Geometry2.Distance(kernel.Pose.Local.Position, before.Local.Position, Curvature.Hyperbolic), Is.LessThan(1e-12));
            Assert.That(KernelTestUtil.MaxAbsDifference(kernel.Pose.Local, before.Local * Iso2.Rot(0.7, Curvature.Hyperbolic)), Is.LessThan(1e-12));
            // The previous head position is kept: the next step is integrated.
            KernelStep step = kernel.Step(new double2(0.35, 0.2), true);
            Assert.That(step.AppliedMetres.x, Is.EqualTo(0.05).Within(1e-12));

            kernel.Configure(KernelConfig.Curved(3, 1.3));
            kernel.ResetToFloor(new double2(0.3, 0.2));
            before = kernel.Pose;
            Assert.That(kernel.Reaim(-0.4), Is.True);
            Assert.That(KernelTestUtil.MaxAbsDifference(kernel.Pose.Local, before.Local * Iso2.Rot(-0.4, Curvature.Spherical)), Is.LessThan(1e-12));
        }

        [Test]
        public void HolonomyWalker_Sphere_Configure()
        {
            const double r = 2.0 * Side / Math.PI;
            var kernel = new HolonomyWalkerKernel();
            kernel.Configure(KernelConfig.Curved(3, r));

            Assert.That(kernel.Q, Is.EqualTo(3));
            Assert.That(kernel.Graph, Is.InstanceOf<CubeTileGraph>());
            Assert.That(kernel.Graph.Q, Is.EqualTo(3));
            Assert.That(kernel.Kappa, Is.EqualTo(Curvature.Spherical));
            Assert.That(kernel.MetresPerUnit, Is.EqualTo(r));
            Assert.That(kernel.ConfiguredMetresPerUnit, Is.EqualTo(r));
            Assert.That(kernel.CurvatureNow, Is.EqualTo(1.0 / (r * r)).Within(1e-15));
            Assert.That(kernel.Ramping, Is.False);
            Assert.That(kernel.Graph.CentreSpacing * kernel.MetresPerUnit, Is.EqualTo(Side).Within(1e-12), "the lap side is a quarter of the sphere");
            Assert.That(kernel.Rig, Is.Null);
            Assert.That(kernel.Pose.Tile, Is.EqualTo(CubeTiling.FaceId(0)));
            Assert.That(kernel.Pose.Local, Is.EqualTo(Iso2.IdentityFor(Curvature.Spherical)));
            Assert.That(kernel.LastStep.LastEdge, Is.EqualTo(-1));
            Assert.That(kernel.SetCurvatureTarget(Curvature.Flat, 0.0), Is.False, "the sphere has no ramp");

            // All six faces, the walker's first, with the chains of the graph.
            var expected = new List<VisibleTile>();
            new CubeTileGraph().VisibleSet(CubeTiling.FaceId(0), kernel.CullRho, expected);
            Assert.That(kernel.Candidates.Count, Is.EqualTo(CubeTiling.FaceCount));
            for (int n = 0; n < expected.Count; n++)
            {
                Assert.That(kernel.Candidates[n].Tile, Is.EqualTo(expected[n].Tile));
                Assert.That(kernel.Candidates[n].Chain, Is.EqualTo(expected[n].Chain));
            }

            // ResetToFloor is the registered placement on face 0, re-rooted when the point lies beyond it.
            var p = new double2(0.4, -0.3);
            kernel.ResetToFloor(p);
            Assert.That(kernel.Pose.Tile, Is.EqualTo(CubeTiling.FaceId(0)));
            Assert.That(KernelTestUtil.MaxAbsDifference(kernel.Pose.Local, Iso2.Boost(p / r, Curvature.Spherical)), Is.LessThan(1e-15));
            kernel.ResetToFloor(new double2(0.75 * Side, 0.0));
            Assert.That(kernel.Pose.Tile, Is.EqualTo(kernel.Graph.Neighbour(CubeTiling.FaceId(0), 0)));
            Assert.That(kernel.Candidates[0].Tile, Is.EqualTo(kernel.Pose.Tile));
            Iso2 inZero = kernel.Graph.EdgeStep(CubeTiling.FaceId(0), 0) * kernel.Pose.Local;
            Assert.That(KernelTestUtil.MaxAbsDifference(inZero, Iso2.Boost(new double2(0.75 * Side, 0.0) / r, Curvature.Spherical)), Is.LessThan(1e-12));
            Assert.Throws<ArgumentOutOfRangeException>(() => kernel.ResetToFloor(new double2(0.0, double.PositiveInfinity)));

            // Reset takes a face and a spherical frame, or a flat-tagged rotation.
            var far = new WalkerPose { Tile = CubeTiling.FaceId(4), Local = Iso2.Boost(new double2(0.2, 0.1), Curvature.Spherical) * Iso2.Rot(0.4) };
            kernel.Reset(far);
            Assert.That(kernel.Pose.Tile, Is.EqualTo(far.Tile));
            Assert.That(KernelTestUtil.MaxAbsDifference(kernel.Pose.Local, far.Local), Is.LessThan(1e-15));
            Assert.Throws<ArgumentException>(() => kernel.Reset(WalkerPose.Start), "tile zero of {4,5} is no face");
            Assert.Throws<ArgumentException>(() => kernel.Reset(
                new WalkerPose { Tile = CubeTiling.FaceId(1), Local = Iso2.IdentityFor(Curvature.Hyperbolic) }));
            Assert.Throws<ArgumentException>(() => kernel.Reset(
                new WalkerPose { Tile = CubeTiling.FaceId(1), Local = Iso2.Boost(new double2(0.5, 0.0), Curvature.Flat) }));
            Assert.That(kernel.Pose.Tile, Is.EqualTo(far.Tile));

            // Back to flat.
            kernel.Configure(KernelConfig.Flat(Side));
            Assert.That(kernel.Q, Is.EqualTo(4));
            Assert.That(kernel.Kappa, Is.EqualTo(Curvature.Flat));
            Assert.That(kernel.Pose, Is.EqualTo(WalkerPose.Start));
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void HolonomyWalker_Sphere_LapCloses(bool fromCentre, bool left)
        {
            // The q = 3 walk of Appendix A.4 through the hook: three sides of (pi / 2) R with quarter turns end
            // where they began, and the walker has turned by a quarter turn against the room.
            const double r = 2.0 * Side / Math.PI;
            var kernel = new HolonomyWalkerKernel();
            kernel.Configure(KernelConfig.Curved(3, r));
            var direct = new SphereWalker();
            double2 start = fromCentre ? double2.zero : new double2(-0.5 * Side, -0.5 * Side);
            kernel.ResetToFloor(start);
            direct.Reset(new WalkerPose { Tile = CubeTiling.FaceId(0), Local = Iso2.Boost(start / r, Curvature.Spherical) }, r);
            direct.SetPrevious(start);
            KernelTestUtil.AssertBitIdentical(direct.Pose, kernel.Pose);

            WalkerPose first = kernel.Pose;
            double3 firstPoint = (CubeTiling.Frame(CubeTiling.FaceOf(first.Tile)) * first.Local).Position;
            double2[] legs = { new double2(Side, 0.0), new double2(0.0, left ? Side : -Side), new double2(-Side, 0.0) };
            var faces = new List<int> { CubeTiling.FaceOf(first.Tile) };
            int crossings = 0;
            double2 head = start;
            double time = 0.0;
            for (int leg = 0; leg < 3; leg++)
            {
                const int n = 200;
                double2 corner = head;
                for (int i = 1; i <= n; i++)
                {
                    head = corner + legs[leg] * ((double)i / n);
                    time += 1.0 / 60.0;
                    KernelStep step = kernel.Step(head, true, time);
                    direct.Step(head, true);

                    Assert.That(step.Rejected, Is.False);
                    Assert.That(step.Crossings, Is.EqualTo(direct.LastCrossings));
                    Assert.That(step.AppliedMetres, Is.EqualTo(direct.LastApplied));
                    KernelTestUtil.AssertBitIdentical(direct.Pose, kernel.Pose);
                    int face = CubeTiling.FaceOf(kernel.Pose.Tile);
                    Assert.That(face, Is.GreaterThanOrEqualTo(0), "the tile is a face id of CubeTiling");
                    Assert.That(kernel.Candidates[0].Tile, Is.EqualTo(kernel.Pose.Tile), "the candidates follow the walker's face");
                    Assert.That(kernel.Candidates.Count, Is.EqualTo(CubeTiling.FaceCount));
                    if (step.Crossings > 0)
                    {
                        Assert.That(kernel.Graph.Neighbour(CubeTiling.FaceId(faces[faces.Count - 1]), step.LastEdge), Is.EqualTo(kernel.Pose.Tile));
                        faces.Add(face);
                    }
                    crossings += step.Crossings;
                }
                if (leg < 2)
                {
                    double3 here = (CubeTiling.Frame(CubeTiling.FaceOf(kernel.Pose.Tile)) * kernel.Pose.Local).Position;
                    Assert.That(r * Geometry2.Distance(here, firstPoint, Curvature.Spherical), Is.GreaterThan(0.5 * Side), "not closed yet");
                }
            }

            Iso2 firstWorld = CubeTiling.Frame(CubeTiling.FaceOf(first.Tile)) * first.Local;
            Iso2 lastWorld = CubeTiling.Frame(CubeTiling.FaceOf(kernel.Pose.Tile)) * kernel.Pose.Local;
            Assert.That(r * math.length(lastWorld.Position - firstPoint), Is.LessThan(1e-3), "closed to a millimetre");
            Iso2 turned = firstWorld.Inverse() * lastWorld;
            Assert.That(KernelTestUtil.MaxAbsDifference(turned, Iso2.Rot((left ? 1.0 : -1.0) * Math.PI / 2.0, Curvature.Spherical)),
                Is.LessThan(1e-9), "the holonomy of an octant");
            if (fromCentre)
            {
                Assert.That(crossings, Is.EqualTo(3));
                Assert.That(faces, Is.EqualTo(left ? new[] { 0, 1, 2, 0 } : new[] { 0, 1, 4, 0 }));
            }
            Assert.That(kernel.StepCount, Is.EqualTo(600));
        }

        [TestCase(5)]
        [TestCase(6)]
        public void HolonomyWalker_Still_IsTheHeadsSpeedInThreeDimensions_TheRampWaitsWhileTheHeadMovesVertically(int q)
        {
            // Rule 4 (Master Plan section 0.5; P1-B09): the curvature never changes while the head moves faster
            // than 0.1 m/s. The head, not the floor point under it: sitting down, standing up or bending moves
            // the head at 0.3 to 1 m/s with x and z unchanged, as HolonomyRig judged it from the full position.
            double l = q == 5 ? L5 : L6;
            IDialKernel kernel = new HolonomyWalkerKernel();
            kernel.Configure(KernelConfig.Curved(q, l));
            var head = new double2(0.4, -0.3);
            double height = 1.6;
            kernel.HeadHeightMetres = height;
            kernel.ResetToFloorFlat(head);
            Assert.That(kernel.SetCurvatureTarget(Curvature.Hyperbolic, l), Is.True);
            double target = -1.0 / (l * l);

            // The height given before the reset is the previous one: the first step already finds the walker standing.
            double time = 0.0;
            const double dt = 1.0 / 60.0;
            time += dt;
            kernel.Step(head, true, time);
            Assert.That(kernel.Still, Is.True, "standing at eye height straight after the registration");
            Assert.That(kernel.CurvatureChanged, Is.True);
            double held = kernel.CurvatureNow;
            WalkerPose pose = kernel.Pose;

            // Sitting down at 0.5 m/s for a second: x and z do not move, the head does.
            for (int n = 0; n < 60; n++)
            {
                time += dt;
                height -= 0.5 * dt;
                kernel.HeadHeightMetres = height;
                KernelStep step = kernel.Step(head, true, time);
                Assert.That(step.Rejected, Is.False);
                Assert.That(step.AppliedMetres, Is.EqualTo(double2.zero), "the height integrates nothing");
                Assert.That(kernel.Still, Is.False, "step " + n + ": the head moves at 0.5 m/s");
                Assert.That(kernel.CurvatureChanged, Is.False);
                Assert.That(kernel.CurvatureNow, Is.EqualTo(held), "step " + n + ": the ramp waits");
            }
            // The rig orthonormalises its frame in every step, so the frame is the same to rounding, not to the bit.
            Assert.That(kernel.Pose.Tile, Is.EqualTo(pose.Tile));
            Assert.That(kernel.Pose.Local.M02, Is.EqualTo(pose.Local.M02).Within(1e-12));
            Assert.That(kernel.Pose.Local.M12, Is.EqualTo(pose.Local.M12).Within(1e-12));
            Assert.That(kernel.Pose.Local.M00, Is.EqualTo(pose.Local.M00).Within(1e-12));
            Assert.That(kernel.Pose.Local.M10, Is.EqualTo(pose.Local.M10).Within(1e-12));

            // Seated and at rest: the ramp goes on. A slow nod (0.05 m/s) is standing still.
            for (int n = 0; n < 60; n++)
            {
                time += dt;
                if (n >= 30) height += 0.05 * dt;
                kernel.HeadHeightMetres = height;
                kernel.Step(head, true, time);
                Assert.That(kernel.Still, Is.True, "step " + n);
                Assert.That(kernel.CurvatureChanged, Is.True);
            }
            Assert.That(kernel.CurvatureNow, Is.EqualTo(target * 61.0 / 240.0).Within(1e-9), "61 standing steps of the 240");

            // 0.08 m/s along the floor is standing; with 0.08 m/s of height it is 0.113 m/s of head speed.
            held = kernel.CurvatureNow;
            for (int n = 0; n < 30; n++)
            {
                time += dt;
                head += new double2(0.08 * dt, 0.0);
                height += 0.08 * dt;
                kernel.HeadHeightMetres = height;
                kernel.Step(head, true, time);
                Assert.That(kernel.Still, Is.False, "step " + n);
            }
            Assert.That(kernel.CurvatureNow, Is.EqualTo(held));

            // A height that is not finite is never standing still, in its own step and in the one that leaves it.
            kernel.HeadHeightMetres = double.NaN;
            time += dt;
            kernel.Step(head, true, time);
            Assert.That(kernel.Still, Is.False);
            kernel.HeadHeightMetres = height;
            time += dt;
            kernel.Step(head, true, time);
            Assert.That(kernel.Still, Is.False);
            time += dt;
            kernel.Step(head, true, time);
            Assert.That(kernel.Still, Is.True);
            Assert.That(kernel.CurvatureNow, Is.LessThan(held));

            // A recentre takes the height of now as the previous one too.
            kernel.ResetToFloor(head);
            time += dt;
            kernel.Step(head, true, time);
            Assert.That(kernel.Still, Is.True, "standing straight after a recentre");
        }

        [TestCase(3)]
        [TestCase(4)]
        public void HolonomyWalker_Still_IsTheHeadsSpeedInThreeDimensions_OffTheRigToo(int q)
        {
            // Small Planet reads Still for its dial, its radius and its lap (q = 3): the same rule as the rig's.
            IDialKernel kernel = new HolonomyWalkerKernel();
            kernel.Configure(q == 3 ? KernelConfig.Curved(3, 1.3) : KernelConfig.Flat(2.0));
            var head = new double2(0.1, 0.2);
            double height = 1.6;
            kernel.HeadHeightMetres = height;
            kernel.ResetToFloor(head);

            double time = 0.0;
            const double dt = 1.0 / 60.0;
            time += dt;
            kernel.Step(head, true, time);
            Assert.That(kernel.Still, Is.False, "there was no step before it to time");
            time += dt;
            kernel.Step(head, true, time);
            Assert.That(kernel.Still, Is.True);

            WalkerPose pose = kernel.Pose;
            for (int n = 0; n < 60; n++)
            {
                time += dt;
                height -= 0.5 * dt;
                kernel.HeadHeightMetres = height;
                KernelStep step = kernel.Step(head, true, time);
                Assert.That(step.Rejected, Is.False);
                Assert.That(kernel.Still, Is.False, "step " + n + ": the head moves at 0.5 m/s");
            }
            KernelTestUtil.AssertBitIdentical(pose, kernel.Pose);

            for (int n = 0; n < 30; n++)
            {
                time += dt;
                height += 0.05 * dt;
                kernel.HeadHeightMetres = height;
                kernel.Step(head, true, time);
                Assert.That(kernel.Still, Is.True, "step " + n + ": 0.05 m/s");
            }

            for (int n = 0; n < 30; n++)
            {
                time += dt;
                head += new double2(0.08 * dt, 0.0);
                height += 0.08 * dt;
                kernel.HeadHeightMetres = height;
                kernel.Step(head, true, time);
                Assert.That(kernel.Still, Is.False, "step " + n + ": 0.113 m/s of head speed");
            }

            kernel.HeadHeightMetres = double.NaN;
            time += dt;
            kernel.Step(head, true, time);
            Assert.That(kernel.Still, Is.False);
        }

        [Test]
        public void HolonomyWalker_Sphere_Rejections()
        {
            const double r = 1.3;
            IDialKernel kernel = new HolonomyWalkerKernel();
            kernel.Configure(KernelConfig.Curved(3, r));
            var head = new double2(0.1, 0.2);
            kernel.ResetToFloor(head);

            // The point given to ResetToFloor is the previous head position: the first step is integrated.
            head += new double2(0.05, 0.0);
            KernelStep step = kernel.Step(head, true, 1.0);
            Assert.That(step.Rejected, Is.False);
            Assert.That(step.AppliedMetres.x, Is.EqualTo(0.05).Within(1e-12));
            Assert.That(kernel.Still, Is.False, "0.05 m in a second is slow, but there was no step before it to time");
            step = kernel.Step(head + new double2(0.05, 0.0), true, 2.0);
            head += new double2(0.05, 0.0);
            Assert.That(kernel.Still, Is.True, "0.05 m/s");
            step = kernel.Step(head + new double2(0.15, 0.0), true, 3.0);
            head += new double2(0.15, 0.0);
            Assert.That(step.Rejected, Is.False);
            Assert.That(kernel.Still, Is.False, "0.15 m/s");

            WalkerPose before = kernel.Pose;
            head += new double2(0.25, 0.0);
            step = kernel.Step(head, true, 4.0);
            Assert.That(step.Rejected, Is.True, "a jump");
            Assert.That(step.AppliedMetres, Is.EqualTo(double2.zero));
            AssertSamePose(before, kernel.Pose);
            Assert.That(kernel.Still, Is.False);

            step = kernel.Step(new double2(double.NaN, 0.0), true, 5.0);
            Assert.That(step.Rejected, Is.True);
            AssertSamePose(before, kernel.Pose);
            head += new double2(0.0, 0.05);
            step = kernel.Step(head, true, 6.0);
            Assert.That(step.Rejected, Is.False, "a broken sample is not remembered");
            Assert.That(step.AppliedMetres.y, Is.EqualTo(0.05).Within(1e-12));

            before = kernel.Pose;
            head += new double2(0.05, 0.0);
            step = kernel.Step(head, false, 7.0);
            Assert.That(step.Rejected, Is.True, "tracking lost");
            AssertSamePose(before, kernel.Pose);
            step = kernel.Step(head, true, 8.0);
            Assert.That(step.Rejected, Is.False);
            Assert.That(step.AppliedMetres, Is.EqualTo(double2.zero));

            // The first Step after Reset integrates nothing.
            kernel.Reset(kernel.Pose);
            before = kernel.Pose;
            step = kernel.Step(head + new double2(0.1, 0.1), true, 9.0);
            Assert.That(step.AppliedMetres, Is.EqualTo(double2.zero));
            AssertSamePose(before, kernel.Pose);
        }
    }
}
