using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Input.Tests
{
    /// <summary>GazeDwell_Timing (lane C contract section 14.3) and the geometry of the gaze targets.</summary>
    public class GazeDwellTests
    {
        private const float Dt = 1f / 60f;
        private static readonly float3 Head = new float3(0f, 1.6f, 0f);
        private static readonly quaternion Level = quaternion.identity;

        private GazeRegistry _registry;
        private GazeDwell _dwell;

        [SetUp]
        public void SetUp()
        {
            _registry = new GazeRegistry();
            _dwell = new GazeDwell(_registry);
        }

        private static GazeTargetData Sphere(AtlasAction action, float3 centre, float radius, GazeMode mode = GazeMode.Once)
        {
            return new GazeTargetData
            {
                Action = action, Space = GazeSpace.World, Shape = GazeShape.Sphere, Centre = centre, Radius = radius,
                Mode = mode, Context = InputContext.Space
            };
        }

        private static GazeTargetData Rect(AtlasAction action, float3 centre, quaternion rotation, float2 half)
        {
            return new GazeTargetData
            {
                Action = action, Space = GazeSpace.World, Shape = GazeShape.Rect, Centre = centre, Rotation = rotation,
                HalfSize = half, Mode = GazeMode.Once, Context = InputContext.Space
            };
        }

        private static quaternion Yaw(float degrees) => quaternion.RotateY(math.radians(degrees));
        private static quaternion Pitch(float degreesUp) => quaternion.RotateX(math.radians(-degreesUp));

        /// <summary>Runs frames and returns the times (seconds since the first frame) at which something fired.</summary>
        private List<float> Run(quaternion rotation, float seconds, List<GazeFiring> firings = null, float startTime = 0f)
        {
            var times = new List<float>();
            int frames = (int)math.round(seconds / Dt);
            for (int i = 0; i < frames; i++)
            {
                if (_dwell.Tick(Head, rotation, Dt, InputContext.Space, false, out GazeFiring f))
                {
                    times.Add(startTime + i * Dt);
                    firings?.Add(f);
                }
            }
            return times;
        }

        [Test]
        public void FiresOnceAtTheDwellTime()
        {
            GazeTargetId id = _registry.Add(Sphere(AtlasAction.Primary, Head + new float3(0f, 0f, 2f), 0.1f));
            var firings = new List<GazeFiring>();
            List<float> times = Run(Level, 3f, firings);

            Assert.That(times.Count, Is.EqualTo(1), "one dwell fires once, however long the gaze stays");
            Assert.That(times[0], Is.EqualTo(0.7f).Within(Dt + 1e-4f));
            Assert.That(firings[0].Action, Is.EqualTo(AtlasAction.Primary));
            Assert.That(firings[0].Source, Is.EqualTo(InputSource.Gaze));
            Assert.That(firings[0].Id, Is.EqualTo(id));
        }

        [Test]
        public void ProgressRunsFromZeroToOne()
        {
            _registry.Add(Sphere(AtlasAction.Primary, Head + new float3(0f, 0f, 2f), 0.1f));
            Run(Level, 0.35f);
            Assert.That(_dwell.Focused.IsValid, Is.True);
            Assert.That(_dwell.Progress, Is.EqualTo(0.5f).Within(0.05f));
            Assert.That(_dwell.Point.z, Is.EqualTo(2f).Within(1e-5f));
            Run(Level, 0.5f);
            Assert.That(_dwell.Progress, Is.EqualTo(0f), "a fired Once target shows an empty ring");
        }

        [Test]
        public void DwellTimeClampsTo600And800Milliseconds()
        {
            Assert.That(_dwell.DwellSeconds, Is.EqualTo(0.7f));
            _dwell.DwellSeconds = 0.1f;
            Assert.That(_dwell.DwellSeconds, Is.EqualTo(0.6f));
            _registry.Add(Sphere(AtlasAction.Primary, Head + new float3(0f, 0f, 2f), 0.1f));
            Assert.That(Run(Level, 2f)[0], Is.EqualTo(0.6f).Within(Dt + 1e-4f));

            _dwell.Reset();
            _dwell.DwellSeconds = 5f;
            Assert.That(_dwell.DwellSeconds, Is.EqualTo(0.8f));
            Assert.That(Run(Level, 2f)[0], Is.EqualTo(0.8f).Within(Dt + 1e-4f));
        }

        [Test]
        public void LeavingCancelsTheDwell()
        {
            _registry.Add(Sphere(AtlasAction.Primary, Head + new float3(0f, 0f, 2f), 0.1f));
            Assert.That(Run(Level, 0.5f), Is.Empty);
            Assert.That(Run(Yaw(40f), 0.25f), Is.Empty);          // longer than the grace: the dwell is lost
            Assert.That(_dwell.Focused.IsValid, Is.False);
            Assert.That(_dwell.Progress, Is.EqualTo(0f));
            List<float> times = Run(Level, 1.5f);
            Assert.That(times.Count, Is.EqualTo(1));
            Assert.That(times[0], Is.EqualTo(0.7f).Within(Dt + 1e-4f), "the dwell starts again from zero");
        }

        [Test]
        public void AShortGlanceAwayKeepsTheDwell()
        {
            _registry.Add(Sphere(AtlasAction.Primary, Head + new float3(0f, 0f, 2f), 0.1f));
            Run(Level, 0.4f);
            Run(Yaw(40f), 0.1f);                                  // inside the 0.15 s grace
            Assert.That(_dwell.Focused.IsValid, Is.True);
            List<float> times = Run(Level, 1f);
            Assert.That(times.Count, Is.EqualTo(1));
            Assert.That(times[0], Is.EqualTo(0.3f).Within(2f * Dt + 1e-4f));
        }

        [Test]
        public void ReArmsOnlyAfterTheGazeHasBeenAway()
        {
            _registry.Add(Sphere(AtlasAction.Primary, Head + new float3(0f, 0f, 2f), 0.1f));
            Assert.That(Run(Level, 1f).Count, Is.EqualTo(1));

            // Off for 0.2 s: past the grace, short of the 0.3 s re-arm. Looking back does nothing.
            Run(Yaw(40f), 0.2f);
            Assert.That(Run(Level, 1.5f), Is.Empty);
            Assert.That(_dwell.Progress, Is.EqualTo(0f));

            // Off for 0.4 s: armed again.
            Run(Yaw(40f), 0.4f);
            List<float> times = Run(Level, 1.5f);
            Assert.That(times.Count, Is.EqualTo(1));
            Assert.That(times[0], Is.EqualTo(0.7f).Within(Dt + 1e-4f));
        }

        [Test]
        public void RepeatFiresAtTheDwellTimeThenAtItsInterval()
        {
            _registry.Add(Sphere(AtlasAction.NavRight, Head + new float3(0f, 0f, 2f), 0.1f, GazeMode.Repeat));
            List<float> times = Run(Level, 2.0f);
            Assert.That(times.Count, Is.EqualTo(4));               // 0.7, 1.1, 1.5, 1.9
            Assert.That(times[0], Is.EqualTo(0.7f).Within(Dt + 1e-4f));
            for (int i = 1; i < times.Count; i++)
                Assert.That(times[i] - times[i - 1], Is.EqualTo(0.4f).Within(Dt + 1e-4f));
        }

        [Test]
        public void HoldLastsFromTheDwellTimeUntilTheGazeLeaves()
        {
            GazeTargetData t = Sphere(AtlasAction.None, Head + new float3(0f, 0f, 2f), 0.1f, GazeMode.Hold);
            t.Axis = AtlasAxis.Sweep;
            t.AxisValue = -1f;
            _registry.Add(t);

            Assert.That(Run(Level, 0.5f), Is.Empty);
            Assert.That(_dwell.Holding, Is.False);
            Assert.That(Run(Level, 0.5f), Is.Empty, "an axis target fires no action");
            Assert.That(_dwell.Holding, Is.True);
            Assert.That(_dwell.TryGetHold(out AtlasAction action, out AtlasAxis axis, out float value), Is.True);
            Assert.That(action, Is.EqualTo(AtlasAction.None));
            Assert.That(axis, Is.EqualTo(AtlasAxis.Sweep));
            Assert.That(value, Is.EqualTo(-1f));
            Assert.That(_dwell.Progress, Is.EqualTo(1f));

            Run(Yaw(40f), 0.3f);
            Assert.That(_dwell.Holding, Is.False);
        }

        [Test]
        public void PicksTheNearestTargetAlongTheRay()
        {
            GazeTargetId far = _registry.Add(Sphere(AtlasAction.NextSpace, Head + new float3(0f, 0f, 4f), 0.5f));
            GazeTargetId near = _registry.Add(Sphere(AtlasAction.Primary, Head + new float3(0f, 0f, 1.5f), 0.1f));
            Run(Level, 0.1f);
            Assert.That(_dwell.Focused, Is.EqualTo(near));

            // Turned so that the ray misses the near one but still passes through the large far one.
            _dwell.Reset();
            Run(Yaw(6f), 0.1f);
            Assert.That(_dwell.Focused, Is.EqualTo(far));
        }

        [Test]
        public void BetweenTwoNearMissesTheSmallerAngleWins()
        {
            // Two small chips side by side at 2 m, 0.12 m apart: both within the 2.5 degree tolerance of a ray
            // that passes between them, nearer to the right one.
            GazeTargetId left = _registry.Add(Sphere(AtlasAction.VariantPrev, Head + new float3(-0.06f, 0f, 2f), 0.01f));
            GazeTargetId right = _registry.Add(Sphere(AtlasAction.VariantNext, Head + new float3(0.06f, 0f, 2f), 0.01f));
            Run(Yaw(0.5f), 0.1f);
            Assert.That(_dwell.Focused, Is.EqualTo(right));
            _dwell.Reset();
            Run(Yaw(-0.5f), 0.1f);
            Assert.That(_dwell.Focused, Is.EqualTo(left));
        }

        [Test]
        public void SphereGeometry()
        {
            float3 o = float3.zero;
            float3 f = new float3(0f, 0f, 1f);
            Assert.That(GazeDwell.RaySphere(o, f, new float3(0f, 0f, 3f), 0.5f, 0f, out bool exact, out float score), Is.True);
            Assert.That(exact, Is.True);
            Assert.That(score, Is.EqualTo(2.5f).Within(1e-4f));

            Assert.That(GazeDwell.RaySphere(o, f, new float3(0.6f, 0f, 3f), 0.5f, 0f, out exact, out _), Is.False);
            Assert.That(GazeDwell.RaySphere(o, f, new float3(0f, 0f, -3f), 0.5f, 0f, out exact, out _), Is.False, "behind the head");
            Assert.That(GazeDwell.RaySphere(o, f, new float3(0f, 0f, 3f), 0.5f, 2f, out exact, out _), Is.False, "past MaxDistance");

            // A 1 cm target 3 m away is still 2.5 degrees wide to the gaze: 0.131 m at that distance.
            Assert.That(GazeDwell.RaySphere(o, f, new float3(0.10f, 0f, 3f), 0.01f, 0f, out exact, out _), Is.True);
            Assert.That(exact, Is.False);
            Assert.That(GazeDwell.RaySphere(o, f, new float3(0.16f, 0f, 3f), 0.01f, 0f, out exact, out _), Is.False);
        }

        [Test]
        public void RectangleGeometry()
        {
            float3 o = float3.zero;
            float3 f = new float3(0f, 0f, 1f);
            var half = new float2(0.4f, 0.1f);

            Assert.That(GazeDwell.RayRect(o, f, new float3(0.3f, 0f, 2f), quaternion.identity, half, 0f, out bool exact, out float score), Is.True);
            Assert.That(exact, Is.True);
            Assert.That(score, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(GazeDwell.RayRect(o, f, new float3(0.6f, 0f, 2f), quaternion.identity, half, 0f, out exact, out _), Is.False);
            Assert.That(GazeDwell.RayRect(o, f, new float3(0f, 0.3f, 2f), quaternion.identity, half, 0f, out exact, out _), Is.False);
            Assert.That(GazeDwell.RayRect(o, f, new float3(0f, 0f, -2f), quaternion.identity, half, 0f, out exact, out _), Is.False, "behind the head");
            Assert.That(GazeDwell.RayRect(o, f, new float3(0f, 0f, 2f), quaternion.identity, half, 1.5f, out exact, out _), Is.False, "past MaxDistance");

            // Turned 90 degrees about its normal the long side is vertical.
            quaternion roll = quaternion.RotateZ(math.radians(90f));
            Assert.That(GazeDwell.RayRect(o, f, new float3(0f, 0.3f, 2f), roll, half, 0f, out exact, out _), Is.True);
            Assert.That(GazeDwell.RayRect(o, f, new float3(0.3f, 0f, 2f), roll, half, 0f, out exact, out _), Is.False);

            // Turned 60 degrees about the vertical its width is foreshortened: the ray meets the plane farther along.
            quaternion yaw = quaternion.RotateY(math.radians(60f));
            Assert.That(GazeDwell.RayRect(o, f, new float3(0.1f, 0f, 2f), yaw, half, 0f, out exact, out score), Is.True);
            Assert.That(exact, Is.True);
            Assert.That(GazeDwell.RayRect(o, f, new float3(0.25f, 0f, 2f), yaw, half, 0f, out exact, out _), Is.False);

            // Edge-on it cannot be looked at.
            Assert.That(GazeDwell.RayRect(o, f, new float3(0f, 0f, 2f), quaternion.RotateY(math.radians(90f)), half, 0f, out exact, out _), Is.False);

            // A thin row is widened to the 2.5 degree tolerance: 0.087 m at 2 m.
            Assert.That(GazeDwell.RayRect(o, f, new float3(0f, 0.05f, 2f), quaternion.identity, new float2(0.4f, 0.01f), 0f, out exact, out _), Is.True);
            Assert.That(exact, Is.False);
        }

        [Test]
        public void ARectangleFiresLikeASphere()
        {
            GazeTargetId sign = _registry.Add(Rect(AtlasAction.SpaceMenu, new float3(1.5f, 1.6f, 0f), Yaw(90f), new float2(0.3f, 0.2f)));
            Assert.That(Run(Level, 1f), Is.Empty, "the sign is to the right, not ahead");
            var firings = new List<GazeFiring>();
            List<float> times = Run(Yaw(90f), 1.5f, firings);
            Assert.That(times.Count, Is.EqualTo(1));
            Assert.That(firings[0].Id, Is.EqualTo(sign));
            Assert.That(firings[0].Action, Is.EqualTo(AtlasAction.SpaceMenu));
        }

        [Test]
        public void HeadSpaceTargetsFollowTheHead()
        {
            // A chip in the corner of the view is never under the gaze, wherever the head turns; one in the
            // centre of the view always is.
            GazeTargetData corner = Sphere(AtlasAction.ParamMenu, new float3(-0.55f, -0.40f, 1.0f), 0.05f);
            corner.Space = GazeSpace.Head;
            _registry.Add(corner);
            Assert.That(Run(Level, 1f), Is.Empty);
            Assert.That(Run(math.mul(Yaw(120f), Pitch(20f)), 1f), Is.Empty);

            GazeTargetData centre = Sphere(AtlasAction.Confirm, new float3(0f, 0f, 1.0f), 0.05f);
            centre.Space = GazeSpace.Head;
            GazeTargetId id = _registry.Add(centre);
            quaternion turned = math.mul(Yaw(120f), Pitch(20f));
            var firings = new List<GazeFiring>();
            Assert.That(Run(turned, 1f, firings).Count, Is.EqualTo(1));
            Assert.That(firings[0].Id, Is.EqualTo(id));
            float3 expected = Head + math.mul(turned, new float3(0f, 0f, 1f));
            Assert.That(math.distance(_dwell.Point, expected), Is.LessThan(1e-4f));
        }

        [Test]
        public void LookDownIsUnderTheGazeBelowItsPitch()
        {
            var t = new GazeTargetData
            {
                Action = AtlasAction.Secondary, Space = GazeSpace.LookDown, PitchBelowDeg = -35f,
                Mode = GazeMode.Once, Context = InputContext.Space
            };
            _registry.Add(t);
            Assert.That(Run(Pitch(-20f), 1f), Is.Empty);
            List<float> times = Run(Pitch(-50f), 1.5f);
            Assert.That(times.Count, Is.EqualTo(1));
            Assert.That(times[0], Is.EqualTo(0.7f).Within(Dt + 1e-4f));
        }

        [Test]
        public void ARealTargetBeatsLookDown()
        {
            _registry.Add(new GazeTargetData
            {
                Action = AtlasAction.Secondary, Space = GazeSpace.LookDown, PitchBelowDeg = -35f, Context = InputContext.Space
            });
            // A floor-dial detent on the floor, 1 m ahead of the feet: the head looks down 58 degrees at it.
            float3 detent = new float3(0f, 0f, 1f);
            GazeTargetId dial = _registry.Add(Sphere(AtlasAction.DialLeft, detent, 0.15f));
            quaternion down = quaternion.LookRotationSafe(math.normalize(detent - Head), new float3(0f, 1f, 0f));
            Run(down, 0.1f);
            Assert.That(_dwell.Focused, Is.EqualTo(dial));
        }

        [Test]
        public void TargetsOfAnotherContextAreIgnored()
        {
            GazeTargetData t = Sphere(AtlasAction.Confirm, Head + new float3(0f, 0f, 2f), 0.1f);
            t.Context = InputContext.Menu;
            _registry.Add(t);
            Assert.That(Run(Level, 1.5f), Is.Empty);
            Assert.That(_dwell.Focused.IsValid, Is.False);
        }

        [Test]
        public void DisabledTargetsAreIgnoredAndDisablingDropsTheFocus()
        {
            GazeTargetId id = _registry.Add(Sphere(AtlasAction.Primary, Head + new float3(0f, 0f, 2f), 0.1f));
            Run(Level, 0.5f);
            Assert.That(_dwell.Focused, Is.EqualTo(id));
            _registry.SetEnabled(id, false);
            Assert.That(Run(Level, 1.5f), Is.Empty);
            Assert.That(_dwell.Focused.IsValid, Is.False);
            _registry.SetEnabled(id, true);
            Assert.That(Run(Level, 1.5f).Count, Is.EqualTo(1));
        }

        [Test]
        public void TheLeverFiresTheFocusedTargetAtOnce()
        {
            GazeTargetId id = _registry.Add(Sphere(AtlasAction.Primary, Head + new float3(0f, 0f, 2f), 0.1f));
            Run(Level, 0.1f);
            Assert.That(_dwell.Tick(Head, Level, Dt, InputContext.Space, true, out GazeFiring f), Is.True);
            Assert.That(f.Action, Is.EqualTo(AtlasAction.Primary));
            Assert.That(f.Source, Is.EqualTo(InputSource.Lever));
            Assert.That(f.Id, Is.EqualTo(id));
            Assert.That(Run(Level, 2f), Is.Empty, "the tap spent the target: the dwell must not fire it again");
        }

        [Test]
        public void TheLeverAloneConfirms()
        {
            Assert.That(_dwell.Tick(Head, Level, Dt, InputContext.Space, true, out GazeFiring f), Is.True);
            Assert.That(f.Action, Is.EqualTo(AtlasAction.Confirm));
            Assert.That(f.Source, Is.EqualTo(InputSource.Lever));
            Assert.That(f.Id.IsValid, Is.False);
        }

        [Test]
        public void AMovedTargetIsFoundWhereItIs()
        {
            GazeTargetId id = _registry.Add(Sphere(AtlasAction.Primary, Head + new float3(0f, 0f, 2f), 0.1f));
            _registry.Move(id, Head + new float3(2f, 0f, 0f));
            Assert.That(Run(Level, 1f), Is.Empty);
            Assert.That(Run(Yaw(90f), 1f).Count, Is.EqualTo(1));
        }
    }
}
