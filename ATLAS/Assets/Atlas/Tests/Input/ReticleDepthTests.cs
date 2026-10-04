using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Input.Tests
{
    /// <summary>
    /// The reticle's distance (documents/ATLAS_Hands_Design.md, decision R10): it comes forward to a focused
    /// target that is nearer than 2 m, so that it is not seen double over a near panel, and keeps its size in the
    /// view.
    /// </summary>
    public class ReticleDepthTests
    {
        private const float Dt = 1f / 60f;
        private static readonly float3 Head = new float3(0f, 1.6f, 0f);

        private static float After(ref ReticleDepth depth, float focusMetres, float seconds)
        {
            int frames = (int)math.round(seconds / Dt);
            float metres = depth.Metres;
            for (int i = 0; i < frames; i++) metres = depth.Step(focusMetres, Dt);
            return metres;
        }

        [Test]
        public void Input_Reticle_DistanceRule_StandsAtTwoMetresWithNothingNearInFocus()
        {
            var depth = new ReticleDepth();
            Assert.That(depth.Metres, Is.EqualTo(2.0f), "before the first frame");
            Assert.That(After(ref depth, 0f, 1f), Is.EqualTo(2.0f), "nothing in focus");
            Assert.That(After(ref depth, 3.5f, 1f), Is.EqualTo(2.0f), "a target beyond 2 m");
            Assert.That(After(ref depth, 2.0f, 1f), Is.EqualTo(2.0f), "a target at 2 m");
            Assert.That(ReticleDepth.Wanted(0f), Is.EqualTo(2.0f));
            Assert.That(ReticleDepth.Wanted(float.NaN), Is.EqualTo(2.0f));
            Assert.That(ReticleDepth.Wanted(1.2f), Is.EqualTo(1.2f));
            Assert.That(GazeReticle.DistanceMetres, Is.EqualTo(ReticleDepth.FarMetres), "one number for the far distance");
        }

        [Test]
        public void Input_Reticle_DistanceRule_ComesToANearTargetWithin150Milliseconds()
        {
            var depth = new ReticleDepth();
            float last = depth.Metres;
            float first = depth.Step(0.45f, Dt);
            Assert.That(first, Is.GreaterThan(0.45f).And.LessThan(2.0f), "eased, not jumped");
            for (int i = 1; i < 9; i++)
            {
                float metres = depth.Step(0.45f, Dt);
                Assert.That(metres, Is.LessThanOrEqualTo(last), "it only comes nearer");
                last = metres;
            }
            Assert.That(last, Is.EqualTo(0.45f).Within(1e-5f), "there after 0.15 s");
            Assert.That(After(ref depth, 0.45f, 1f), Is.EqualTo(0.45f).Within(1e-5f), "and it stays");

            // It follows a target that moves, and one at arm's length further out.
            Assert.That(After(ref depth, 1.2f, 0.15f), Is.EqualTo(1.2f).Within(1e-4f));
        }

        [Test]
        public void Input_Reticle_DistanceRule_NeverComesNearerThan35Centimetres()
        {
            var depth = new ReticleDepth();
            for (int i = 0; i < 60; i++) Assert.That(depth.Step(0.10f, Dt), Is.GreaterThanOrEqualTo(0.35f - 1e-6f));
            Assert.That(depth.Metres, Is.EqualTo(0.35f).Within(1e-5f));
            Assert.That(ReticleDepth.Wanted(0.10f), Is.EqualTo(0.35f));
        }

        [Test]
        public void Input_Reticle_DistanceRule_GoesBackWhenTheFocusLeaves()
        {
            var depth = new ReticleDepth();
            After(ref depth, 0.45f, 0.5f);
            float first = depth.Step(0f, Dt);
            Assert.That(first, Is.GreaterThan(0.45f).And.LessThan(2.0f));
            Assert.That(After(ref depth, 0f, 0.15f), Is.EqualTo(2.0f), "exactly where it stands today");

            // In the hand the reticle is hidden: it starts from far when it is shown again.
            After(ref depth, 0.45f, 0.5f);
            depth.Reset();
            Assert.That(depth.Metres, Is.EqualTo(2.0f));
        }

        [Test]
        public void Input_Reticle_DistanceRule_KeepsItsSizeInTheView()
        {
            // The mesh is drawn for 2 m: at any other distance it is scaled by the ratio, so the angle it fills is the same.
            Assert.That(ReticleDepth.Scale(2.0f), Is.EqualTo(1f));
            Assert.That(ReticleDepth.Scale(0.45f), Is.EqualTo(0.225f).Within(1e-6f));
            foreach (float metres in new[] { 0.35f, 0.45f, 1.2f, 2.0f })
                Assert.That(0.027f * ReticleDepth.Scale(metres) / metres, Is.EqualTo(0.027f / 2.0f).Within(1e-6f), "the ring's angle at " + metres + " m");
        }

        [Test]
        public void Input_Reticle_DistanceRule_TheDistanceIsWhereTheGazeMeetsTheTarget()
        {
            var registry = new GazeRegistry();
            var dwell = new GazeDwell(registry);
            Assert.That(dwell.PointDistance, Is.EqualTo(0f), "nothing in focus");

            // A wide row 0.45 m ahead, looked at 20 cm from its centre: the reticle belongs where the gaze meets the
            // row, not at the distance of the row's centre (0.49 m).
            GazeTargetId row = registry.Add(new GazeTargetData
            {
                Action = AtlasAction.NavDown, Space = GazeSpace.World, Shape = GazeShape.Rect,
                Centre = Head + new float3(0.20f, 0f, 0.45f), Rotation = quaternion.identity, HalfSize = new float2(0.25f, 0.02f),
                Context = InputContext.Space
            });
            dwell.Tick(Head, quaternion.identity, Dt, InputContext.Space, false, out _);
            Assert.That(dwell.Focused, Is.EqualTo(row));
            Assert.That(dwell.PointDistance, Is.EqualTo(0.45f).Within(1e-5f));
            registry.Remove(row);

            // A sphere: the distance of its centre.
            registry.Add(new GazeTargetData
            {
                Action = AtlasAction.Primary, Space = GazeSpace.World, Centre = Head + new float3(0f, 0f, 1.2f), Radius = 0.1f,
                Context = InputContext.Space
            });
            dwell.Tick(Head, quaternion.identity, Dt, InputContext.Space, false, out _);
            Assert.That(dwell.PointDistance, Is.EqualTo(1.2f).Within(1e-5f));
            // Looked away from: nothing in focus, no distance.
            for (int i = 0; i < 30; i++) dwell.Tick(Head, quaternion.RotateY(math.radians(90f)), Dt, InputContext.Space, false, out _);
            Assert.That(dwell.Focused.IsValid, Is.False);
            Assert.That(dwell.PointDistance, Is.EqualTo(0f));

            // A LookDown target has no place: the reticle stays far.
            var down = new GazeRegistry();
            var downDwell = new GazeDwell(down);
            down.Add(new GazeTargetData { Action = AtlasAction.Secondary, Space = GazeSpace.LookDown, PitchBelowDeg = -35f, Context = InputContext.Space });
            downDwell.Tick(Head, quaternion.RotateX(math.radians(50f)), Dt, InputContext.Space, false, out _);
            Assert.That(downDwell.Focused.IsValid, Is.True);
            Assert.That(downDwell.PointDistance, Is.EqualTo(0f));

            // The service passes it on.
            var input = new AtlasInputCore();
            input.PushContext(InputContext.Space);
            input.Targets.Add(new GazeTargetData
            {
                Action = AtlasAction.Primary, Space = GazeSpace.World, Centre = Head + new float3(0f, 0f, 0.45f), Radius = 0.05f,
                Context = InputContext.Space
            });
            input.Tick(default, Head, quaternion.identity, Dt);
            Assert.That(input.GazeDistance, Is.EqualTo(0.45f).Within(1e-5f));
        }
    }
}
