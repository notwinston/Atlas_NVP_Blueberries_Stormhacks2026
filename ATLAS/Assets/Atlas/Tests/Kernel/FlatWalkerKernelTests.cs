using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Kernel.Tests
{
    /// <summary>The kernel step hook at kappa = 0: integration, tile crossing, jump rejection and set-up
    /// (Master Plan sections 4.6 and 6.3).</summary>
    [TestFixture]
    public class FlatWalkerKernelTests
    {
        private static FlatWalkerKernel Kernel(double tileSideMetres, double metresPerUnit = 1.0)
        {
            var kernel = new FlatWalkerKernel();
            kernel.Configure(KernelConfig.Flat(tileSideMetres, metresPerUnit));
            return kernel;
        }

        /// <summary>The floor point the pose stands for, in metres. Only meaningful on the flat grid.</summary>
        private static double2 Floor(IWalkerKernel kernel)
        {
            WalkerPose pose = kernel.Pose;
            Assert.That(FlatTileGraph.TryGetCell(pose.Tile, out long i, out long j), Is.True);
            double a = kernel.Graph.CentreSpacing;
            return kernel.MetresPerUnit * new double2(a * i + pose.Local.M02, a * j + pose.Local.M12);
        }

        private static double Error(double2 a, double2 b)
        {
            return math.cmax(math.abs(a - b));
        }

        /// <summary>Walks a straight line of the given length in equal steps; returns the tile edges crossed.</summary>
        private static int Walk(IWalkerKernel kernel, ref double2 head, double2 direction, double length, int steps)
        {
            double2 from = head;
            int crossings = 0;
            for (int n = 1; n <= steps; n++)
            {
                head = from + direction * (length * n / steps);
                KernelStep step = kernel.Step(head, true);
                Assert.That(step.Rejected, Is.False);
                crossings += step.Crossings;
            }
            return crossings;
        }

        [Test]
        public void FlatWalker_Integrates()
        {
            FlatWalkerKernel kernel = Kernel(2.0);
            var random = new System.Random(KernelTestUtil.Seed);
            var head = new double2(0.3, -0.4);
            kernel.ResetToFloor(head);
            Assert.That(Error(Floor(kernel), head), Is.LessThan(1e-12));

            double bound = 0.5 * kernel.Graph.CentreSpacing + KernelConstants.CrossHysteresis;
            double worst = 0.0, reach = 0.0;
            int crossings = 0;
            for (int n = 0; n < 20000; n++)
            {
                double2 d = KernelTestUtil.RandomStep(random, 0.19);
                head += d;
                KernelStep step = kernel.Step(head, true);
                Iso2 local = kernel.Pose.Local;
                crossings += step.Crossings;
                worst = Math.Max(worst, Error(Floor(kernel), head));
                reach = Math.Max(reach, Math.Max(Math.Abs(local.M02), Math.Abs(local.M12)));

                if (step.Rejected || Error(step.AppliedMetres, d) > 1e-12 || !kernel.Pose.IsValid)
                {
                    Assert.Fail("step " + n + " was not integrated");
                }
                // No rotation ever appears at kappa = 0.
                if (local.M00 != 1.0 || local.M11 != 1.0 || local.M01 != 0.0 || local.M10 != 0.0)
                {
                    Assert.Fail("step " + n + " turned the frame");
                }
            }
            Assert.That(worst, Is.LessThan(1e-9));
            Assert.That(reach, Is.LessThanOrEqualTo(bound + 1e-12));
            Assert.That(crossings, Is.GreaterThan(100), "the walk must cross tiles to mean anything");
            Assert.That(kernel.StepCount, Is.EqualTo(20000));
        }

        [Test]
        public void FlatWalker_Crossing()
        {
            FlatWalkerKernel kernel = Kernel(1.0);
            kernel.ResetToFloor(new double2(0.45, 0.45));
            KernelStep step = kernel.Step(new double2(0.57, 0.57), true);
            Assert.That(step.Crossings, Is.EqualTo(2));
            Assert.That(step.TileChanged, Is.True);
            Assert.That(step.LastEdge, Is.EqualTo(1));
            Assert.That(kernel.Pose.Tile, Is.EqualTo(FlatTileGraph.Cell(1, 1)));
            Assert.That(kernel.Pose.Local.M02, Is.EqualTo(-0.43).Within(1e-12));
            Assert.That(kernel.Pose.Local.M12, Is.EqualTo(-0.43).Within(1e-12));
            // The world position is continuous across the edge.
            Assert.That(Error(Floor(kernel), new double2(0.57, 0.57)), Is.LessThan(1e-12));
            Assert.That(kernel.LastStep.Crossings, Is.EqualTo(2));

            kernel.ResetToFloor(new double2(0.49, 0.0));
            step = kernel.Step(new double2(0.51, 0.0), true);
            Assert.That(step.Crossings, Is.EqualTo(0), "0.51 is inside the hysteresis band");
            Assert.That(step.TileChanged, Is.False);
            Assert.That(step.LastEdge, Is.EqualTo(-1));
            Assert.That(kernel.Pose.Tile, Is.EqualTo(TileId.Origin));
            Assert.That(Error(Floor(kernel), new double2(0.51, 0.0)), Is.LessThan(1e-12));

            step = kernel.Step(new double2(0.53, 0.0), true);
            Assert.That(step.Crossings, Is.EqualTo(1));
            Assert.That(step.LastEdge, Is.EqualTo(0));
            Assert.That(kernel.Pose.Tile, Is.EqualTo(FlatTileGraph.Cell(1, 0)));
            Assert.That(Error(Floor(kernel), new double2(0.53, 0.0)), Is.LessThan(1e-12));

            // Each of the four edges, and the position stays continuous through every one.
            double2[] directions = { new double2(1, 0), new double2(0, 1), new double2(-1, 0), new double2(0, -1) };
            for (int k = 0; k < 4; k++)
            {
                var head = directions[k] * 0.45;
                kernel.ResetToFloor(head);
                int lastEdge = -1;
                for (int n = 0; n < 4; n++)
                {
                    head += directions[k] * 0.05;
                    step = kernel.Step(head, true);
                    if (step.TileChanged) lastEdge = step.LastEdge;
                    Assert.That(Error(Floor(kernel), head), Is.LessThan(1e-12));
                }
                Assert.That(lastEdge, Is.EqualTo(k));
                Assert.That(kernel.Pose.Tile, Is.EqualTo(kernel.Graph.Neighbour(TileId.Origin, k)));
            }
        }

        [Test]
        public void FlatWalker_NoFlicker()
        {
            FlatWalkerKernel kernel = Kernel(2.0);
            var head = new double2(0.99, 0.0);
            kernel.ResetToFloor(head);
            int crossings = 0;
            for (int n = 0; n < 1000; n++)
            {
                head.x += (n % 2 == 0) ? 0.05 : -0.05;
                crossings += kernel.Step(head, true).Crossings;
            }
            Assert.That(crossings, Is.EqualTo(1));
            Assert.That(kernel.Pose.Tile, Is.EqualTo(FlatTileGraph.Cell(1, 0)));

            head = new double2(0.99, 0.0);
            kernel.ResetToFloor(head);
            crossings = 0;
            for (int n = 0; n < 1000; n++)
            {
                head.x += (n % 2 == 0) ? 0.02 : -0.02;
                crossings += kernel.Step(head, true).Crossings;
            }
            Assert.That(crossings, Is.EqualTo(0));
            Assert.That(kernel.Pose.Tile, Is.EqualTo(TileId.Origin));
        }

        [Test]
        public void FlatWalker_JumpRejected()
        {
            FlatWalkerKernel kernel = Kernel(2.0);
            kernel.ResetToFloor(new double2(0.1, 0.2));
            long count = kernel.StepCount;

            // A jump is ignored, once.
            WalkerPose before = kernel.Pose;
            KernelStep step = kernel.Step(new double2(0.35, 0.2), true);
            Assert.That(step.Rejected, Is.True);
            Assert.That(step.AppliedMetres, Is.EqualTo(double2.zero));
            Assert.That(step.Crossings, Is.EqualTo(0));
            Assert.That(step.LastEdge, Is.EqualTo(-1));
            KernelTestUtil.AssertBitIdentical(before, kernel.Pose);
            Assert.That(kernel.LastStep.Rejected, Is.True);
            Assert.That(kernel.StepCount, Is.EqualTo(++count));

            step = kernel.Step(new double2(0.40, 0.2), true);
            Assert.That(step.Rejected, Is.False);
            Assert.That(Error(step.AppliedMetres, new double2(0.05, 0.0)), Is.LessThan(1e-15));
            Assert.That(Error(Floor(kernel), new double2(0.15, 0.2)), Is.LessThan(1e-12), "the jump itself is never integrated");
            Assert.That(kernel.StepCount, Is.EqualTo(++count));

            // A head position that is not finite leaves everything alone, including the previous position.
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                before = kernel.Pose;
                step = kernel.Step(new double2(bad, 0.2), true);
                Assert.That(step.Rejected, Is.True);
                Assert.That(step.AppliedMetres, Is.EqualTo(double2.zero));
                KernelTestUtil.AssertBitIdentical(before, kernel.Pose);
                Assert.That(kernel.StepCount, Is.EqualTo(++count));

                before = kernel.Pose;
                step = kernel.Step(new double2(0.2, bad), true);
                Assert.That(step.Rejected, Is.True);
                KernelTestUtil.AssertBitIdentical(before, kernel.Pose);
                Assert.That(kernel.StepCount, Is.EqualTo(++count));
            }
            step = kernel.Step(new double2(0.45, 0.2), true);
            Assert.That(step.Rejected, Is.False);
            Assert.That(Error(Floor(kernel), new double2(0.20, 0.2)), Is.LessThan(1e-12));
            count++;

            // Movement while tracking is not Normal is rejected and is not caught up afterwards.
            before = kernel.Pose;
            step = kernel.Step(new double2(0.50, 0.2), false);
            Assert.That(step.Rejected, Is.True);
            Assert.That(step.AppliedMetres, Is.EqualTo(double2.zero));
            KernelTestUtil.AssertBitIdentical(before, kernel.Pose);
            Assert.That(kernel.StepCount, Is.EqualTo(++count));
            step = kernel.Step(new double2(0.50, 0.2), true);
            Assert.That(step.Rejected, Is.False);
            Assert.That(step.AppliedMetres, Is.EqualTo(double2.zero));
            Assert.That(Error(Floor(kernel), new double2(0.20, 0.2)), Is.LessThan(1e-12));
            Assert.That(kernel.StepCount, Is.EqualTo(++count));

            // The limit is 0.2 m, in any direction.
            var head = new double2(0.0, 0.0);
            kernel.ResetToFloor(head);
            foreach (double angle in new[] { 0.0, 0.7, 2.0, -2.5 })
            {
                var direction = new double2(Math.Cos(angle), Math.Sin(angle));
                head += direction * 0.19;
                Assert.That(kernel.Step(head, true).Rejected, Is.False, "0.19 m at angle " + angle);
                head += direction * 0.21;
                Assert.That(kernel.Step(head, true).Rejected, Is.True, "0.21 m at angle " + angle);
            }

            // The first Step after Reset or Configure only records the head position.
            kernel.Reset(WalkerPose.Start);
            Assert.That(kernel.LastStep.Rejected, Is.False);
            Assert.That(kernel.LastStep.LastEdge, Is.EqualTo(-1));
            count = kernel.StepCount;
            before = kernel.Pose;
            step = kernel.Step(new double2(5.0, 5.0), true);
            Assert.That(step.Rejected, Is.False);
            Assert.That(step.AppliedMetres, Is.EqualTo(double2.zero));
            Assert.That(kernel.Pose.Tile, Is.EqualTo(before.Tile));
            Assert.That(kernel.Pose.Local, Is.EqualTo(before.Local));
            step = kernel.Step(new double2(5.1, 5.0), true);
            Assert.That(step.Rejected, Is.False);
            Assert.That(kernel.Pose.Local.M02, Is.EqualTo(0.1).Within(1e-12));
            Assert.That(kernel.StepCount, Is.EqualTo(count + 2));

            kernel.Configure(KernelConfig.Flat(2.0));
            step = kernel.Step(new double2(-3.0, 9.0), true);
            Assert.That(step.AppliedMetres, Is.EqualTo(double2.zero));
            Assert.That(kernel.Pose.Local, Is.EqualTo(Iso2.Identity));
            Assert.That(kernel.StepCount, Is.EqualTo(count + 3), "StepCount is never reset");

            // Extreme input never throws.
            Assert.DoesNotThrow(() => kernel.Step(new double2(double.MaxValue, double.MaxValue), true));
            Assert.DoesNotThrow(() => kernel.Step(new double2(-double.MaxValue, -double.MaxValue), true));
            Assert.That(kernel.LastStep.Rejected, Is.True);
            Assert.That(kernel.Pose.Local, Is.EqualTo(Iso2.Identity));
        }

        [Test]
        public void FlatWalker_LapSquare()
        {
            // Lane B extends this with the {4,5} and {4,3} laps: the same four-sided walk, a different
            // number of laps to come home.
            FlatWalkerKernel kernel = Kernel(2.0);
            double2[] sides = { new double2(1, 0), new double2(0, 1), new double2(-1, 0), new double2(0, -1) };

            var start = new double2(-1.0, -1.0);
            var head = start;
            kernel.ResetToFloor(head);
            WalkerPose first = kernel.Pose;
            int crossings = 0;
            foreach (double2 side in sides) crossings += Walk(kernel, ref head, side, 2.0, 40);
            Assert.That(crossings, Is.EqualTo(0), "the lap runs along the edges of tile zero");
            Assert.That(Error(head, start), Is.LessThan(1e-9));
            Assert.That(Error(Floor(kernel), start), Is.LessThan(1e-9));
            Assert.That(kernel.Pose.Tile, Is.EqualTo(first.Tile));
            Assert.That(KernelTestUtil.MaxAbsDifference(kernel.Pose.Local, first.Local), Is.LessThan(1e-9));

            start = new double2(-0.7, -0.8);
            head = start;
            kernel.ResetToFloor(head);
            first = kernel.Pose;
            crossings = 0;
            foreach (double2 side in sides) crossings += Walk(kernel, ref head, side, 2.0, 40);
            Assert.That(crossings, Is.EqualTo(4));
            Assert.That(Error(Floor(kernel), start), Is.LessThan(1e-9));
            Assert.That(kernel.Pose.Tile, Is.EqualTo(first.Tile));
            Assert.That(KernelTestUtil.MaxAbsDifference(kernel.Pose.Local, first.Local), Is.LessThan(1e-9));
        }

        [Test]
        public void FlatWalker_UnitFree()
        {
            const double side = 2.0;
            FlatWalkerKernel one = Kernel(side, 1.0);
            FlatWalkerKernel other = Kernel(side, 1.885);
            Assert.That(other.MetresPerUnit, Is.EqualTo(1.885));
            Assert.That(other.Graph.CentreSpacing * other.MetresPerUnit, Is.EqualTo(side).Within(1e-12));

            var random = new System.Random(KernelTestUtil.Seed);
            var head = new double2(0.3, -0.4);
            one.ResetToFloor(head);
            other.ResetToFloor(head);
            int compared = 0;
            for (int n = 0; n < 2000; n++)
            {
                head += KernelTestUtil.RandomStep(random, 0.19);
                one.Step(head, true);
                other.Step(head, true);
                Assert.That(Error(Floor(one), head), Is.LessThan(1e-9));
                Assert.That(Error(Floor(other), head), Is.LessThan(1e-9));

                // The hysteresis is in kernel units, so inside that band the moment of crossing may differ.
                if (EdgeDistance(head.x, side) > 0.04 && EdgeDistance(head.y, side) > 0.04)
                {
                    Assert.That(other.Pose.Tile, Is.EqualTo(one.Pose.Tile));
                    compared++;
                }
            }
            Assert.That(compared, Is.GreaterThan(1500));
        }

        /// <summary>Distance in metres from a coordinate to the nearest tile edge line.</summary>
        private static double EdgeDistance(double coordinate, double side)
        {
            double f = coordinate / side + 0.5;
            f -= Math.Floor(f);
            return Math.Min(f, 1.0 - f) * side;
        }

        private static void AssertSameList(List<VisibleTile> actual, List<VisibleTile> expected)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            for (int n = 0; n < expected.Count; n++)
            {
                Assert.That(actual[n].Tile, Is.EqualTo(expected[n].Tile));
                Assert.That(actual[n].Chain, Is.EqualTo(expected[n].Chain));
                Assert.That(actual[n].GraphDistance, Is.EqualTo(expected[n].GraphDistance));
            }
        }

        private static List<VisibleTile> Expected(IWalkerKernel kernel)
        {
            var list = new List<VisibleTile>();
            double radius = kernel.CullRho + 2.0 * kernel.Graph.CircumRadius + KernelConstants.CrossHysteresis;
            kernel.Graph.VisibleSet(kernel.Pose.Tile, radius, list);
            return list;
        }

        [Test]
        public void FlatWalker_Candidates()
        {
            var kernel = new FlatWalkerKernel();
            List<VisibleTile> candidates = kernel.Candidates;
            Assert.That(candidates.Capacity, Is.GreaterThanOrEqualTo(1024));
            Assert.That(kernel.CullRho, Is.EqualTo(KernelConstants.DefaultCullRho));
            Assert.That(kernel.MetresPerUnit, Is.EqualTo(KernelConstants.FlatMetresPerUnit));
            Assert.That(kernel.Graph.CentreSpacing, Is.EqualTo(KernelConstants.DefaultFlatTileSideMetres));
            Assert.That(kernel.Kappa, Is.EqualTo(Curvature.Flat));
            Assert.That(kernel.Pose.Tile, Is.EqualTo(TileId.Origin));
            Assert.That(kernel.Pose.Local, Is.EqualTo(Iso2.Identity));
            Assert.That(candidates[0].Tile, Is.EqualTo(TileId.Origin));
            AssertSameList(candidates, Expected(kernel));
            int atStart = candidates.Count;

            // A tile change rebuilds the list round the new tile.
            var head = new double2(0.9, 0.0);
            kernel.ResetToFloor(head);
            head.x += 0.19;
            Assert.That(kernel.Step(head, true).TileChanged, Is.True);
            Assert.That(kernel.Candidates, Is.SameAs(candidates));
            Assert.That(candidates[0].Tile, Is.EqualTo(FlatTileGraph.Cell(1, 0)));
            Assert.That(candidates.Count, Is.EqualTo(atStart));
            AssertSameList(candidates, Expected(kernel));

            // A new rho is taken up inside the next Step, not before.
            kernel.CullRho = 6.0;
            Assert.That(candidates.Count, Is.EqualTo(atStart));
            kernel.Step(head, true);
            Assert.That(candidates.Count, Is.GreaterThan(atStart));
            AssertSameList(candidates, Expected(kernel));
            Assert.That(kernel.Candidates, Is.SameAs(candidates));

            // Also when that Step is a rejected one.
            kernel.CullRho = 1.0;
            kernel.Step(head, false);
            Assert.That(candidates.Count, Is.LessThan(atStart));
            AssertSameList(candidates, Expected(kernel));

            kernel.Configure(KernelConfig.Flat(1.2));
            Assert.That(kernel.Candidates, Is.SameAs(candidates));
            Assert.That(kernel.CullRho, Is.EqualTo(1.0), "Configure keeps rho");
            AssertSameList(candidates, Expected(kernel));

            Assert.Throws<ArgumentOutOfRangeException>(() => kernel.CullRho = -1.0);
            Assert.Throws<ArgumentOutOfRangeException>(() => kernel.CullRho = double.NaN);
        }

        [Test]
        public void FlatWalker_StepAllocatesNothing()
        {
            FlatWalkerKernel kernel = Kernel(1.0);
            var head = new double2(0.0, 0.0);
            kernel.ResetToFloor(head);
            kernel.Step(head, true);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int n = 0; n < 5000; n++)
            {
                head += new double2(0.15, 0.11);      // a tile change every few steps
                kernel.Step(head, true);
            }
            kernel.Step(new double2(double.NaN, 0.0), true);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.EqualTo(0));
            Assert.That(FlatTileGraph.TryGetCell(kernel.Pose.Tile, out long i, out long j), Is.True);
            Assert.That(i, Is.GreaterThan(700));
        }

        [Test]
        public void FlatWalker_ConfigureAndReset()
        {
            var kernel = new FlatWalkerKernel();

            KernelConfig curved = KernelConfig.Flat(2.0);
            curved.Q = 5;
            Assert.Throws<NotSupportedException>(() => kernel.Configure(curved));
            KernelConfig noTiling = KernelConfig.Flat(2.0);
            noTiling.Q = 7;
            Assert.Throws<ArgumentOutOfRangeException>(() => kernel.Configure(noTiling));
            foreach (double bad in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => kernel.Configure(KernelConfig.Flat(bad)));
                Assert.Throws<ArgumentOutOfRangeException>(() => kernel.Configure(KernelConfig.Flat(2.0, bad)));
            }

            KernelConfig flat = KernelConfig.Flat(2.0);
            Assert.That(flat.Q, Is.EqualTo(4));
            Assert.That(flat.MetresPerUnit, Is.EqualTo(KernelConstants.FlatMetresPerUnit));
            Assert.That(flat.TileSideMetres, Is.EqualTo(2.0));
            kernel.Configure(flat);

            kernel.ResetToFloor(new double2(3.1, -2.2));
            Assert.That(kernel.Pose.Tile, Is.EqualTo(FlatTileGraph.Cell(2, -1)));
            Assert.That(kernel.Pose.Local.M02, Is.EqualTo(-0.9).Within(1e-12));
            Assert.That(kernel.Pose.Local.M12, Is.EqualTo(-0.2).Within(1e-12));
            Assert.That(kernel.Pose.IsValid, Is.True);
            Assert.That(kernel.Candidates[0].Tile, Is.EqualTo(FlatTileGraph.Cell(2, -1)));

            // A tie goes to the higher index.
            kernel.ResetToFloor(new double2(1.0, -1.0));
            Assert.That(kernel.Pose.Tile, Is.EqualTo(FlatTileGraph.Cell(1, 0)));
            Assert.That(kernel.Pose.Local.Position, Is.EqualTo(new double3(-1.0, -1.0, 1.0)));

            // ResetToFloor records the head position, so the next Step integrates from it.
            kernel.ResetToFloor(new double2(0.2, 0.2));
            Assert.That(kernel.LastStep.Crossings, Is.EqualTo(0));
            Assert.That(kernel.LastStep.LastEdge, Is.EqualTo(-1));
            KernelStep step = kernel.Step(new double2(0.3, 0.2), true);
            Assert.That(Error(step.AppliedMetres, new double2(0.1, 0.0)), Is.LessThan(1e-15));
            Assert.Throws<ArgumentOutOfRangeException>(() => kernel.ResetToFloor(new double2(double.NaN, 0.0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => kernel.ResetToFloor(new double2(0.0, double.NegativeInfinity)));

            // Reset re-roots a pose that lies outside the tile it names, without hysteresis.
            var outside = new WalkerPose
            {
                Tile = FlatTileGraph.Cell(3, 3),
                Local = Iso2.Boost(new double2(5.3, -1.01), Curvature.Flat),
            };
            kernel.Reset(outside);
            Assert.That(kernel.Pose.Tile, Is.EqualTo(FlatTileGraph.Cell(6, 2)));
            Assert.That(kernel.Pose.Local.M02, Is.EqualTo(-0.7).Within(1e-12));
            Assert.That(kernel.Pose.Local.M12, Is.EqualTo(0.99).Within(1e-12));
            Assert.That(kernel.Candidates[0].Tile, Is.EqualTo(FlatTileGraph.Cell(6, 2)));

            // A point exactly on an edge stays in the tile it was given with.
            var onEdge = new WalkerPose { Tile = TileId.Origin, Local = Iso2.Boost(new double2(1.0, -1.0), Curvature.Flat) };
            kernel.Reset(onEdge);
            Assert.That(kernel.Pose.Tile, Is.EqualTo(TileId.Origin));
            Assert.That(kernel.Pose.Local, Is.EqualTo(onEdge.Local));

            Assert.Throws<ArgumentException>(() => kernel.Reset(default(WalkerPose)));
            Assert.Throws<ArgumentException>(() => kernel.Reset(
                new WalkerPose { Tile = new TileId(1, 0, 0, 0, 0, 1), Local = Iso2.Identity }));
            Assert.Throws<ArgumentException>(() => kernel.Reset(
                new WalkerPose { Tile = TileId.Origin, Local = default(Iso2) }));
            // A refused Reset leaves the pose as it was.
            Assert.That(kernel.Pose.Tile, Is.EqualTo(TileId.Origin));

            Assert.That(WalkerPose.Start.IsValid, Is.True);
            Assert.That(WalkerPose.Start.Tile, Is.EqualTo(TileId.Origin));
            Assert.That(WalkerPose.Start.Local, Is.EqualTo(Iso2.Identity));
            Assert.That(default(WalkerPose).IsValid, Is.False);
        }
    }
}
