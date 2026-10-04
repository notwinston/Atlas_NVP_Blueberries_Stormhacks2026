using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Safety.Tests
{
    /// <summary>
    /// BumpGuard as the clearance query of Master Plan section 4.10: what lane B's valve chambers and spaces ask
    /// for at a floor point. Driven with SensorFrames over the fixture room's distance field. No engine.
    /// </summary>
    public class ClearanceQueryTests
    {
        const float Tolerance = 1e-4f;

        RoomModel _room;
        BumpGuard _guard;

        [SetUp]
        public void SetUp()
        {
            _room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            _guard = new BumpGuard(false);
            _guard.SetRoom(_room);
        }

        [TearDown]
        public void TearDown()
        {
            _guard.Dispose();
        }

        void Tick(double time, bool roomActive = true)
        {
            var sensors = new SensorFrame
            {
                Time = time,
                DeltaTime = 1f / 60f,
                Head = new HeadPose(time, new float3(0f, 1.6f, 0f), quaternion.identity, TrackingState.Normal),
                RoomActive = roomActive,
                AnchorTracked = true
            };
            _guard.Tick(in sensors);
        }

        [Test]
        public void TheGuard_IsAClearanceQuery()
        {
            Assert.That(_guard, Is.InstanceOf<IClearanceQuery>());
            Assert.That(_guard, Is.InstanceOf<IBumpGuard>());
        }

        [Test]
        public void Clearance_IsTheRoomsDistanceField_WhenNothingIsDynamic()
        {
            Tick(0.0);
            var points = new[]
            {
                float2.zero, new float2(0.5f, -0.4f), new float2(-1.0f, 0.9f), new float2(1.3f, 0f), new float2(1.6f, 0f)
            };
            IClearanceQuery query = _guard;
            foreach (float2 p in points)
                Assert.That(query.Clearance(p), Is.EqualTo(_room.Sdf.Sample(p)).Within(Tolerance), p.ToString());
            Assert.That(query.Clearance(float2.zero), Is.GreaterThan(1.0f));
        }

        [Test]
        public void Clearance_TakesTheNearerOfTheWallAndADynamicCell()
        {
            Tick(1.0);
            float2 cell = new float2(0.525f, 0.525f);
            float before = _guard.Clearance(cell);
            _guard.Core.StampDynamic(cell, 0.5f, 1.0);

            Assert.That(_guard.Clearance(cell), Is.EqualTo(0f).Within(Tolerance), "at the cell centre");
            float2 beside = cell + new float2(0.3f, 0f);
            Assert.That(_guard.Clearance(beside), Is.LessThan(0.3f).And.GreaterThan(0.2f), "beside it");
            Assert.That(before, Is.GreaterThan(0.5f));
            Assert.That(_guard.Clearance(new float2(-1.0f, -1.0f)),
                Is.EqualTo(_room.Sdf.Sample(new float2(-1.0f, -1.0f))).Within(Tolerance), "far from it, only the wall counts");
        }

        [Test]
        public void ADynamicCell_IsForgottenAfterItsThirtySeconds()
        {
            _guard.Core.StampDynamic(new float2(0.525f, 0.525f), 0.5f, 0.0);
            Tick(1.0);
            Assert.That(_guard.Clearance(new float2(0.525f, 0.525f)), Is.EqualTo(0f).Within(Tolerance));
            Tick(BumpConstants.DynExpirySeconds + 1.0);
            Assert.That(_guard.Clearance(new float2(0.525f, 0.525f)), Is.GreaterThan(0.5f));
        }

        [Test]
        public void OutsideTheGrid_OrNotANumber_IsBlocked()
        {
            Tick(0.0);
            Assert.That(_guard.Clearance(new float2(40f, 0f)), Is.EqualTo(0f));
            Assert.That(_guard.Clearance(new float2(float.NaN, 0f)), Is.EqualTo(0f));
        }

        [Test]
        public void WithNoRoom_TheClearanceIsInfinite()
        {
            var bare = new BumpGuard(false);
            try
            {
                Assert.That(bare.Clearance(float2.zero), Is.EqualTo(float.PositiveInfinity));
                bare.SetRoom(_room);
                var sensors = new SensorFrame { Time = 0.0, Head = HeadPose.Identity, RoomActive = false, AnchorTracked = true };
                bare.Tick(in sensors);   // the room frame is not active: the map is put away
                Assert.That(bare.Clearance(float2.zero), Is.EqualTo(float.PositiveInfinity));
            }
            finally
            {
                bare.Dispose();
            }
            Assert.That(bare.Clearance(float2.zero), Is.EqualTo(float.PositiveInfinity), "after Dispose");
        }
    }
}
