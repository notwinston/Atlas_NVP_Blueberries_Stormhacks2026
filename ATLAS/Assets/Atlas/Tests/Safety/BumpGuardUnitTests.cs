using Atlas.Safety;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Safety.Tests
{
    /// <summary>The parts of Bump Guard on their own: the static sampler, the dynamic layer, the velocity filter, the depth kernel.</summary>
    [TestFixture]
    public unsafe class BumpGuardUnitTests
    {
        [Test]
        public void BumpLevel_IsExactlyThePlanEnum()
        {
            Assert.That(System.Enum.GetNames(typeof(BumpLevel)), Is.EqualTo(new[] { "Clear", "Warn", "Strong", "TrackingLost" }));
            Assert.That((int)BumpLevel.Clear, Is.EqualTo(0));
            Assert.That((int)BumpLevel.TrackingLost, Is.EqualTo(3));
        }

        [Test]
        public void Constants_AreThePlanValues()
        {
            Assert.That(BumpConstants.WarnNow, Is.EqualTo(0.30f));
            Assert.That(BumpConstants.StrongNow, Is.EqualTo(0.20f));
            Assert.That(BumpConstants.PathClear, Is.EqualTo(0.05f));
            Assert.That(BumpConstants.WarnHorizon, Is.EqualTo(0.8f));
            Assert.That(BumpConstants.StrongHorizon, Is.EqualTo(0.4f));
            Assert.That(BumpConstants.RelaxDistance, Is.EqualTo(0.05f));
            Assert.That(BumpConstants.RelaxSeconds, Is.EqualTo(0.3f));
            Assert.That(BumpConstants.VelocityWindow, Is.EqualTo(0.2f));
            Assert.That(BumpConstants.JumpDistance, Is.EqualTo(0.2f));
            Assert.That(BumpConstants.TrackingLostSeconds, Is.EqualTo(0.5f));
            Assert.That(BumpConstants.DynMinHeight, Is.EqualTo(0.10f));
            Assert.That(BumpConstants.DynMaxHeight, Is.EqualTo(2.0f));
            Assert.That(BumpConstants.DynStaticMargin, Is.EqualTo(0.30f));
            Assert.That(BumpConstants.DynExpirySeconds, Is.EqualTo(30.0));
            Assert.That(BumpConstants.CellSize, Is.EqualTo(0.05f));
            Assert.That(BumpConstants.RescanDisagreement, Is.EqualTo(0.15f));
            Assert.That(BumpConstants.RescanSeconds, Is.EqualTo(2.0f));
            Assert.That(BumpConstants.SectorCount, Is.EqualTo(7));
            Assert.That(BumpConstants.SectorHalfSpanDeg, Is.EqualTo(35f));
            Assert.That(BumpConstants.DepthProcessHz, Is.GreaterThanOrEqualTo(BumpConstants.DepthProcessHzMin));
            Assert.That(BumpConstants.DepthProcessHzMin, Is.EqualTo(15));
        }

        [Test]
        public void StaticClearance_IsBilinearAndBlockedOutside()
        {
            // 4 x 3 cells of 5 cm, origin (1, 2); value = 10 * i + j at the centre of cell (i, j).
            var spec = new BumpGridSpec(4, 3, 0.05f, 1f, 2f);
            var sdf = new float[12];
            for (int j = 0; j < 3; j++)
                for (int i = 0; i < 4; i++)
                    sdf[j * 4 + i] = 10 * i + j;
            fixed (float* p = sdf)
            {
                Assert.That(StaticClearance.Sample(p, spec, new float2(1.025f, 2.025f)), Is.EqualTo(0f).Within(1e-3f));
                Assert.That(StaticClearance.Sample(p, spec, new float2(1.125f, 2.075f)), Is.EqualTo(21f).Within(1e-3f), "row 0 is the lowest z");
                Assert.That(StaticClearance.Sample(p, spec, new float2(1.100f, 2.050f)), Is.EqualTo(15.5f).Within(1e-2f), "between four centres");
                Assert.That(StaticClearance.Sample(p, spec, new float2(1.005f, 2.005f)), Is.EqualTo(0f).Within(1e-3f), "rim repeats the edge");
                Assert.That(StaticClearance.Sample(p, spec, new float2(0.99f, 2.05f)), Is.EqualTo(BumpConstants.BlockedClearance));
                Assert.That(StaticClearance.Sample(p, spec, new float2(1.1f, 2.16f)), Is.EqualTo(BumpConstants.BlockedClearance));
                Assert.That(StaticClearance.Sample(p, spec, new float2(float.NaN, 2.05f)), Is.EqualTo(BumpConstants.BlockedClearance));
            }
        }

        [Test]
        public void OutsideTheGrid_IsStrong()
        {
            using (var core = new BumpGuardCore())
            {
                var sdf = new float[40 * 40];
                for (int k = 0; k < sdf.Length; k++) sdf[k] = 1f;
                core.SetRoom(sdf, 40, 40, 0.05f, -1f, -1f);
                core.Step(0.0, new float3(5f, 1.6f, 0f), quaternion.identity, true, true);
                Assert.That(core.Level, Is.EqualTo(BumpLevel.Strong));
            }
        }

        [Test]
        public void NoRoom_UsesDepthOnlyAndNeverTheMap()
        {
            using (var core = new BumpGuardCore())
            {
                for (int k = 0; k < 120; k++) core.Step(k / 60.0, new float3(0f, 1.6f, 0f), quaternion.identity, true, true);
                Assert.That(core.MapTrusted, Is.False);
                Assert.That(core.Level, Is.EqualTo(BumpLevel.Clear));
            }
        }

        [Test]
        public void VelocityFilter_AveragesOver200msAndResetsOnAJump()
        {
            var f = new HeadVelocityFilter();
            float2 p = float2.zero;
            f.Update(p, 0.0);
            Assert.That(f.Velocity, Is.EqualTo(float2.zero), "one sample: no velocity");
            for (int k = 1; k <= 60; k++)
            {
                p.y += 0.7f / 60f;
                f.Update(p, k / 60.0);
            }
            Assert.That(f.Velocity.y, Is.EqualTo(0.7f).Within(1e-3f));
            Assert.That(f.Velocity.x, Is.EqualTo(0f).Within(1e-4f));

            // Standing still, the motion leaves the window after 0.2 s: half of it is left after 0.1 s.
            for (int k = 61; k <= 66; k++) f.Update(p, k / 60.0);
            Assert.That(f.Velocity.y, Is.EqualTo(0.35f).Within(0.01f));
            for (int k = 67; k <= 73; k++) f.Update(p, k / 60.0);
            Assert.That(f.Velocity.y, Is.EqualTo(0f).Within(1e-4f));

            for (int k = 74; k <= 100; k++)
            {
                p.x += 1f / 60f;
                f.Update(p, k / 60.0);
            }
            Assert.That(f.Velocity.x, Is.EqualTo(1f).Within(1e-3f));
            f.Update(p + new float2(0f, 0.25f), 101 / 60.0);
            Assert.That(f.Jumped, Is.True);
            Assert.That(f.Velocity, Is.EqualTo(float2.zero));
            Assert.That(f.Position, Is.EqualTo(p + new float2(0f, 0.25f)), "the new position is taken at once");
            f.Update(p + new float2(0f, 0.25f), 102 / 60.0);
            Assert.That(f.Jumped, Is.False);
            Assert.That(f.Velocity, Is.EqualTo(float2.zero), "the jump itself never becomes velocity");
        }

        sealed class Layer : System.IDisposable
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

        [Test]
        public void DynamicLayer_KeepsTheHighestHitAndMeasuresToTheCell()
        {
            using (var l = new Layer())
            {
                DynamicGrid g = l.Core.Dynamic;
                Assert.That(DynamicLayer.Distance(g, new float2(0f, 0f), 0.0, out _), Is.EqualTo(float.PositiveInfinity));
                Assert.That(DynamicLayer.Stamp(g, new float2(0.52f, 0.02f), 0.4f, 1.0), Is.True);
                Assert.That(DynamicLayer.Stamp(g, new float2(0.53f, 0.03f), 0.9f, 2.0), Is.True);
                Assert.That(DynamicLayer.Stamp(g, new float2(0.51f, 0.01f), 0.2f, 3.0), Is.True);
                Assert.That(l.Core.DynamicCellCount, Is.EqualTo(1));
                Assert.That(DynamicLayer.TopHeightAt(g, new float2(0.52f, 0.02f)), Is.EqualTo(0.9f));
                Assert.That(DynamicLayer.Stamp(g, new float2(9f, 0f), 0.5f, 3.0), Is.False, "off the grid");

                // The cell covers x in [0.50, 0.55], z in [0, 0.05].
                float d = DynamicLayer.Distance(g, new float2(0.30f, 0.02f), 3.0, out float2 nearest);
                Assert.That(d, Is.EqualTo(0.20f).Within(1e-3f));
                Assert.That(nearest.x, Is.EqualTo(0.525f).Within(1e-3f));
                Assert.That(DynamicLayer.Distance(g, new float2(0.52f, 0.02f), 3.0, out _), Is.EqualTo(0f));
                Assert.That(DynamicLayer.Distance(g, new float2(0.13f, 0.02f), 3.0, out _), Is.EqualTo(0.37f).Within(1e-3f), "inside the query range");
                Assert.That(DynamicLayer.Distance(g, new float2(0.05f, 0.02f), 3.0, out _), Is.EqualTo(float.PositiveInfinity), "beyond the query range");
                Assert.That(l.Core.ClearAt(new float2(0.30f, 0.02f), 3.0), Is.EqualTo(0.20f).Within(1e-3f), "min of static and dynamic");

                // A ray passing above the stored height leaves it. Rays passing below clear it once three
                // depth images have seen it empty; several rays of one image count once.
                float2 c = new float2(0.52f, 0.02f);
                Assert.That(DynamicLayer.ClearIfSeenEmpty(g, c, 0.95f, 1, 4.0), Is.False);
                Assert.That(DynamicLayer.ClearIfSeenEmpty(g, c, 0.88f, 1, 4.0), Is.False, "within the tolerance");
                Assert.That(DynamicLayer.ClearIfSeenEmpty(g, c, 0.5f, 1, 4.0), Is.False);
                Assert.That(DynamicLayer.ClearIfSeenEmpty(g, c, 0.4f, 1, 4.0), Is.False);
                Assert.That(DynamicLayer.ClearIfSeenEmpty(g, c, 0.4f, 1, 4.0), Is.False);
                Assert.That(DynamicLayer.ClearIfSeenEmpty(g, c, 0.5f, 2, 4.1), Is.False);
                // A hit in between starts the count again, and an image that hits the cell cannot see it empty.
                DynamicLayer.Stamp(g, c, 0.9f, 4.2);
                Assert.That(DynamicLayer.ClearIfSeenEmpty(g, c, 0.5f, 3, 4.2), Is.False);
                Assert.That(DynamicLayer.ClearIfSeenEmpty(g, c, 0.5f, 4, 4.3), Is.False);
                Assert.That(DynamicLayer.ClearIfSeenEmpty(g, c, 0.5f, 5, 4.4), Is.False);
                Assert.That(l.Core.DynamicCellCount, Is.EqualTo(1));
                Assert.That(DynamicLayer.ClearIfSeenEmpty(g, c, 0.5f, 6, 4.5), Is.True);
                Assert.That(l.Core.DynamicCellCount, Is.EqualTo(0));

                l.Core.StampDynamic(c, 0.5f, 5.0);
                Assert.That(l.Core.DynamicCellCount, Is.EqualTo(1));
                l.Core.ClearDynamic();
                Assert.That(l.Core.DynamicCellCount, Is.EqualTo(0));
            }
        }

        [Test]
        public void DynamicLayer_ANewHitRenewsThe30Seconds()
        {
            using (var l = new Layer())
            {
                DynamicGrid g = l.Core.Dynamic;
                float2 p = new float2(0.52f, 0.02f), q = new float2(0.45f, 0.02f);
                DynamicLayer.Stamp(g, p, 0.5f, 0.0);
                DynamicLayer.Stamp(g, p, 0.5f, 20.0);
                Assert.That(DynamicLayer.Distance(g, q, 45.0, out _), Is.LessThan(0.1f));
                Assert.That(DynamicLayer.Distance(g, q, 50.5, out _), Is.EqualTo(float.PositiveInfinity));
                int cursor = 0;
                DynamicLayer.SweepExpired(g, 50.5, ref cursor, g.BlocksX * g.BlocksZ);
                Assert.That(l.Core.DynamicCellCount, Is.EqualTo(0));
            }
        }

        [Test]
        public void DepthKernel_FillsTheSevenSectorsLeftToRight()
        {
            // A flat depth image: every ray lands on the plane 1 m ahead. Level view from 1.6 m.
            var depth = new float[256 * 192];
            for (int k = 0; k < depth.Length; k++) depth[k] = 1f;
            float f = 96f / math.tan(math.radians(24f));
            var input = new DepthKernelInput
            {
                Width = 256, Height = 192, Stride = 4,
                Intrinsics = new DepthIntrinsics(f, f, 128f, 96f),
                SensorPosition = new float3(0f, 1.6f, 0f),
                SensorRotation = quaternion.identity,
                Time = 1.0,
                MapTrusted = false
            };
            fixed (float* d = depth)
            {
                BumpDepthKernel.Process(d, input, null, default, default, out DepthFrameResult r);
                Assert.That(r.MapTrusted, Is.False);
                // The image spans about +/-30.7 degrees, so all seven sectors see the plane; the middle one at 1 m.
                Assert.That(r.Sector(3), Is.EqualTo(1f).Within(0.005f));
                for (int k = 0; k < 7; k++) Assert.That(r.Sector(k), Is.InRange(0.99f, 1.25f), "sector " + k);
                Assert.That(r.Sector(0), Is.GreaterThan(r.Sector(1)));
                Assert.That(DepthFrameResult.SectorAngle(0), Is.EqualTo(math.radians(-30f)).Within(1e-4f));
                Assert.That(DepthFrameResult.SectorAngle(6), Is.EqualTo(math.radians(30f)).Within(1e-4f));

                // Something near on the right of the image only.
                for (int v = 0; v < 192; v++)
                    for (int u = 200; u < 256; u++)
                        depth[v * 256 + u] = 0.4f;
                BumpDepthKernel.Process(d, input, null, default, default, out r);
                Assert.That(r.Nearest(out float nearest, out int sector), Is.True);
                Assert.That(sector, Is.GreaterThanOrEqualTo(5));
                Assert.That(nearest, Is.LessThan(0.5f));
                Assert.That(r.Sector(0), Is.GreaterThan(0.99f));

                // Invalid readings are skipped.
                for (int k = 0; k < depth.Length; k++) depth[k] = k % 3 == 0 ? 0f : k % 3 == 1 ? float.NaN : 0.2f; // 0.2 m is under the sensor's range
                BumpDepthKernel.Process(d, input, null, default, default, out r);
                Assert.That(r.Nearest(out _, out _), Is.False);
            }
        }

        [Test]
        public void DepthKernel_StampsOnlyBetween10cmAnd2mAndOnlyAwayFromTheMap()
        {
            using (var sim = new BumpScenario(3.5f, 3.5f))
            {
                // A 5 cm plinth and a box close against the +Z wall: neither may be stamped.
                sim.Boxes.Add(SimBox.OnFloor(0f, 1.5f, 1f, 1f, 0.05f));
                sim.Boxes.Add(SimBox.OnFloor(0f, 3.4f, 1f, 0.2f, 1.5f));
                sim.Hold(new SimPose { Position = new float2(0f, -1f), Yaw = 0f }, 0.2f);
                Assert.That(sim.Core.DynamicCellCount, Is.EqualTo(0));

                // The same box 1 m off the wall is stamped, at its real height.
                sim.Boxes.Clear();
                sim.Boxes.Add(SimBox.OnFloor(0f, 2.4f, 1f, 0.2f, 1.5f));
                sim.Hold(new SimPose { Position = new float2(0f, -1f), Yaw = 0f }, 0.2f);
                Assert.That(sim.Core.DynamicCellCount, Is.GreaterThan(10));
                Assert.That(DynamicLayer.TopHeightAt(sim.Core.Dynamic, new float2(0f, 2.32f)), Is.InRange(1.3f, 1.5f));
            }
        }
    }
}
