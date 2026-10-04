using Atlas.Kernel.FourD;
using Atlas.Spaces.Slicer;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Spaces.Tests
{
    /// <summary>The Slicer's layout and controller (Master Plan section 5.1; lane A contract, section 14.3).</summary>
    public class SlicerControllerTests
    {
        static SlicerInput At(float z, float dt = 1f / 60f)
        {
            return new SlicerInput { Floor = new float2(-0.75f, z), DeltaTime = dt };
        }

        // Walks the controller onto the lane: the lane takes the cut once the walker has moved along it.
        static SlicerController OnLane(SlicerLayout layout, float z)
        {
            var controller = new SlicerController(in layout);
            controller.Step(At(z - 0.2f));
            controller.Step(At(z));
            Assert.That(controller.Owner, Is.EqualTo(SlicerCutOwner.Lane));
            return controller;
        }

        [Test]
        public void Layout_FollowsTheFreeRectangle()
        {
            SlicerLayout layout = SlicerLayout.FromRoom(3f, 4f, false);
            Assert.That(layout.Centre.x, Is.EqualTo(0.75f).Within(1e-6f));
            Assert.That(layout.Centre.y, Is.EqualTo(1.25f).Within(1e-6f));
            Assert.That(layout.Centre.z, Is.EqualTo(0f));
            Assert.That(layout.Radius, Is.EqualTo(0.5f).Within(1e-6f), "0.2 * wf, held at 0.5 m");
            Assert.That(layout.HasLane, Is.True);
            Assert.That(layout.LaneX, Is.EqualTo(-0.75f).Within(1e-6f));
            Assert.That(layout.LaneLength, Is.EqualTo(3f).Within(1e-6f), "df - 0.4 m, at most 3 m");

            SlicerLayout small = SlicerLayout.FromRoom(1.6f, 2.0f, false);
            Assert.That(small.Radius, Is.EqualTo(0.32f).Within(1e-6f));
            Assert.That(small.LaneLength, Is.EqualTo(1.6f).Within(1e-6f));

            SlicerLayout tiny = SlicerLayout.FromRoom(1.0f, 1.0f, false);
            Assert.That(tiny.Radius, Is.EqualTo(0.25f).Within(1e-6f), "never smaller than 0.25 m");
            Assert.That(tiny.HasLane, Is.False, "0.6 m is no lane");
        }

        [Test]
        public void Layout_StandingMode_HasNoLane()
        {
            SlicerLayout layout = SlicerLayout.FromRoom(1.4f, 1.4f, true);
            Assert.That(layout.HasLane, Is.False);
            Assert.That(layout.CutPerMetre, Is.EqualTo(0f));
            Assert.That(layout.CutAt(0.5f), Is.EqualTo(0f));

            // A big room with the standing flag set still has no lane: the walker is never asked to walk.
            Assert.That(SlicerLayout.FromRoom(4f, 4f, true).HasLane, Is.False);
        }

        [Test]
        public void Layout_HandlesStandClearOfTheBodyAndOfEachOther()
        {
            foreach (float wf in new[] { 1.2f, 2f, 3f, 5f })
            {
                SlicerLayout layout = SlicerLayout.FromRoom(wf, 3f, false);
                var points = new[]
                {
                    layout.CutHandle(+1), layout.CutHandle(-1), layout.NextObjectChip, layout.ViewChip,
                    layout.TurnHandle(0, +1), layout.TurnHandle(0, -1), layout.TurnHandle(1, +1),
                    layout.TurnHandle(1, -1), layout.TurnHandle(2, +1), layout.TurnHandle(2, -1),
                };
                for (int i = 0; i < points.Length; i++)
                {
                    // The shadow of a polytope reaches at most 4/3 of the radius from the centre... in x, y, z
                    // together; a handle beyond that sphere is never inside the body or its shadow.
                    Assert.That(math.distance(points[i], layout.Centre), Is.GreaterThan(layout.Radius * 4f / 3f), "handle " + i);
                    Assert.That(points[i].y, Is.InRange(0.4f, 2.1f), "within reach of the gaze, handle " + i);
                    for (int j = i + 1; j < points.Length; j++)
                        Assert.That(math.distance(points[i], points[j]), Is.GreaterThan(2f * SlicerLayout.HandleRadius),
                                    "handles " + i + " and " + j + " do not overlap");
                }
                Assert.That(layout.BarPoint(SlicerObjects.CutRange).y - layout.BarPoint(-SlicerObjects.CutRange).y,
                            Is.EqualTo(2f * layout.Radius).Within(1e-5f));
            }
        }

        [Test]
        public void WalkingOneMetre_MovesTheCutByTheLanesRate()
        {
            SlicerLayout layout = SlicerLayout.FromRoom(3f, 4f, false);
            Assert.That(layout.CutPerMetre, Is.EqualTo(2.2f / 3f).Within(1e-6f));

            SlicerController controller = OnLane(layout, -0.5f);
            float before = controller.W;
            controller.Step(At(0.5f));
            Assert.That(controller.W - before, Is.EqualTo(2.2f / 3f).Within(1e-5f), "1 m along a 3 m lane");

            // The middle of the lane is the middle of the body, whatever the walker's x.
            var off = new SlicerInput { Floor = new float2(1.3f, 0f), DeltaTime = 1f / 60f };
            controller.Step(in off);
            Assert.That(controller.W, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(layout.LaneZ(layout.CutAt(0.9f)), Is.EqualTo(0.9f).Within(1e-5f));
        }

        [Test]
        public void TheCut_IsHeldAtTheEndsOfTheBody()
        {
            SlicerLayout layout = SlicerLayout.FromRoom(3f, 4f, false);
            SlicerController controller = OnLane(layout, 0f);
            controller.Step(At(1.5f));
            Assert.That(controller.W, Is.EqualTo(SlicerObjects.CutRange).Within(1e-6f));
            controller.Step(At(9f));
            Assert.That(controller.W, Is.EqualTo(SlicerObjects.CutRange).Within(1e-6f), "beyond the lane's end");
            controller.Step(At(-9f));
            Assert.That(controller.W, Is.EqualTo(-SlicerObjects.CutRange).Within(1e-6f));

            controller.SetCut(5f);
            Assert.That(controller.W, Is.EqualTo(SlicerObjects.CutRange));
            controller.SetCut(float.NaN);
            Assert.That(controller.W, Is.EqualTo(0f));
        }

        [Test]
        public void TheLane_DoesNotTakeTheCutUntilTheWalkerMoves()
        {
            SlicerLayout layout = SlicerLayout.FromRoom(3f, 4f, false);
            var controller = new SlicerController(in layout);
            controller.Reset(0.4f, 0f, 0f, 0f);
            for (int i = 0; i < 300; i++) controller.Step(At(1.0f));
            Assert.That(controller.W, Is.EqualTo(0.4f), "a stored cut stays while the walker stands");
            Assert.That(controller.Owner, Is.EqualTo(SlicerCutOwner.Parameter));

            controller.Step(At(1.0f + SlicerController.LaneReclaimMetres + 0.01f));
            Assert.That(controller.Owner, Is.EqualTo(SlicerCutOwner.Lane));
            Assert.That(controller.W, Is.EqualTo(layout.CutAt(1.06f)).Within(1e-5f));
        }

        [Test]
        public void TheInputTouchedLast_OwnsTheCutForTwoSeconds()
        {
            SlicerLayout layout = SlicerLayout.FromRoom(3f, 4f, false);
            SlicerController controller = OnLane(layout, 0f);

            // The Station sets the cut; the walker keeps walking, and the lane stays out of it for 2 s.
            controller.SetCut(-0.8f);
            float z = 0f;
            for (int i = 0; i < 60; i++)
            {
                z += 0.01f;
                controller.Step(At(z));
            }
            Assert.That(controller.W, Is.EqualTo(-0.8f), "1 s later the value from outside still holds");
            Assert.That(controller.Owner, Is.EqualTo(SlicerCutOwner.Parameter));
            for (int i = 0; i < 70; i++)
            {
                z += 0.01f;
                controller.Step(At(z));
            }
            Assert.That(controller.Owner, Is.EqualTo(SlicerCutOwner.Lane), "after 2 s the walking walker has the cut back");
            Assert.That(controller.W, Is.EqualTo(layout.CutAt(z)).Within(1e-5f));

            // The stick takes it at once.
            var push = At(z);
            push.AxisW = -1f;
            float before = controller.W;
            controller.Step(in push);
            Assert.That(controller.Owner, Is.EqualTo(SlicerCutOwner.Stick));
            Assert.That(controller.W, Is.EqualTo(before - SlicerController.CutRatePerSecond / 60f).Within(1e-5f));
        }

        [Test]
        public void StandingMode_TheStickMovesTheCut_AndWalkingDoesNot()
        {
            SlicerLayout layout = SlicerLayout.FromRoom(1.4f, 1.4f, true);
            var controller = new SlicerController(in layout);
            for (int i = 0; i < 120; i++) controller.Step(At(-0.5f + i * 0.01f));
            Assert.That(controller.W, Is.EqualTo(0f), "leaning about moves nothing");

            var push = At(0f);
            push.AxisW = 1f;
            for (int i = 0; i < 60; i++) controller.Step(in push);
            Assert.That(controller.W, Is.EqualTo(SlicerController.CutRatePerSecond).Within(1e-4f), "one second of full stick");
            for (int i = 0; i < 600; i++) controller.Step(in push);
            Assert.That(controller.W, Is.EqualTo(SlicerObjects.CutRange), "held at the end of the range");

            // Released, the cut stays where it was put.
            for (int i = 0; i < 300; i++) controller.Step(At(0.3f));
            Assert.That(controller.W, Is.EqualTo(SlicerObjects.CutRange));
        }

        [Test]
        public void TheDeadZone_IgnoresAStickAtRest()
        {
            Assert.That(SlicerController.DeadZone(0.1f), Is.EqualTo(0f));
            Assert.That(SlicerController.DeadZone(-0.15f), Is.EqualTo(0f));
            Assert.That(SlicerController.DeadZone(1f), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(SlicerController.DeadZone(-1f), Is.EqualTo(-1f).Within(1e-6f));
            Assert.That(SlicerController.DeadZone(float.NaN), Is.EqualTo(0f));
            Assert.That(SlicerController.DeadZone(0.575f), Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void TheSticks_TurnTheBodyInThePlanesThatContainW()
        {
            SlicerLayout layout = SlicerLayout.FromRoom(3f, 4f, false);
            var controller = new SlicerController(in layout);
            var turn = At(0f);
            turn.AxisXw = 1f;
            turn.AxisZw = -1f;
            for (int i = 0; i < 60; i++) controller.Step(in turn);
            Assert.That(controller.Xw, Is.EqualTo(SlicerController.TurnRatePerSecond).Within(1e-4f));
            Assert.That(controller.Yw, Is.EqualTo(0f));
            Assert.That(controller.Zw, Is.EqualTo(-SlicerController.TurnRatePerSecond).Within(1e-4f));

            // Angles stay within -pi .. pi however long the stick is held.
            for (int i = 0; i < 2000; i++)
            {
                controller.Step(in turn);
                Assert.That(math.abs(controller.Xw), Is.LessThanOrEqualTo(math.PI));
                Assert.That(math.abs(controller.Zw), Is.LessThanOrEqualTo(math.PI));
            }
            Assert.That(SlicerController.WrapAngle(math.PI + 0.25f), Is.EqualTo(-math.PI + 0.25f).Within(1e-5f));
            Assert.That(SlicerController.WrapAngle(-7f * math.PI / 2f), Is.EqualTo(math.PI / 2f).Within(1e-5f));
            Assert.That(SlicerController.WrapAngle(float.NaN), Is.EqualTo(0f));
        }

        [Test]
        public void Rotation_ComposesTheRestPoseThenXwYwZw()
        {
            const float xw = 0.4f, yw = -1.1f, zw = 2.3f;
            SlicerController.GetTransform(SlicerObject.Cell16, xw, yw, zw, out float4x4 m, out float4 t);
            double4x4 expected = math.mul(Rotation4D.Plane(2, 3, zw), math.mul(Rotation4D.Plane(1, 3, yw), Rotation4D.Plane(0, 3, xw)));
            AssertEqual(m, new float4x4(expected), 1e-6f);
            Assert.That(math.length(t), Is.EqualTo(0f));

            // A rotation: M * M^T = I.
            AssertEqual(math.mul(m, math.transpose(m)), float4x4.identity, 1e-5f);

            // A turn in XW leaves y and z alone and moves x into w.
            SlicerController.GetTransform(SlicerObject.Cell16, math.PI / 2f, 0f, 0f, out m, out t);
            float4 x = math.mul(m, new float4(1f, 0f, 0f, 0f));
            Assert.That(math.distance(x, new float4(0f, 0f, 0f, 1f)), Is.LessThan(1e-6f));
            Assert.That(math.distance(math.mul(m, new float4(0f, 1f, 0f, 0f)), new float4(0f, 1f, 0f, 0f)), Is.LessThan(1e-6f));

            // With no turn the tesseract stands on a corner: (1, 1, 1, 1) / 2 is the top of the w axis.
            SlicerController.GetTransform(SlicerObject.Tesseract, 0f, 0f, 0f, out m, out t);
            Assert.That(math.distance(math.mul(m, new float4(0.5f)), new float4(0f, 0f, 0f, 1f)), Is.LessThan(1e-6f));
            AssertEqual(m, new float4x4(Rotation4D.CornerFirst()), 1e-7f);
        }

        [Test]
        public void TheTube_TurnsAboutItsOwnCentre()
        {
            Mesh4D tube = SlicerObjects.Build(SlicerObject.Tube);
            Assert.That(tube.TetCount, Is.EqualTo(18000));
            foreach (var angles in new[] { new float3(0f), new float3(0.7f, -0.3f, 1.9f) })
            {
                SlicerController.GetTransform(SlicerObject.Tube, angles.x, angles.y, angles.z, out float4x4 m, out float4 t);
                float4 lo = new float4(float.MaxValue), hi = new float4(float.MinValue);
                float far = 0f;
                foreach (float4 v in tube.Vertices)
                {
                    float4 p = math.mul(m, v) + t;
                    lo = math.min(lo, p);
                    hi = math.max(hi, p);
                    far = math.max(far, math.length(p));
                }
                Assert.That(far, Is.LessThan(1.45f), "the tube stays about the origin when turned");
                if (angles.x == 0f)
                {
                    Assert.That(lo.w, Is.EqualTo(-SlicerObjects.CutRange).Within(1e-5f), "one breath spans the cut range");
                    Assert.That(hi.w, Is.EqualTo(SlicerObjects.CutRange).Within(1e-5f));
                    Assert.That(lo.z + hi.z, Is.EqualTo(0f).Within(1e-4f), "centred along its travel");
                }
            }
        }

        [Test]
        public void TheCutNormal_TiltsOnlyForTheTube()
        {
            Assert.That(SlicerController.CutNormal(SlicerObject.Tesseract, 0.6f), Is.EqualTo(new float4(0f, 0f, 0f, 1f)));
            Assert.That(SlicerController.CutNormal(SlicerObject.Tube, 0f), Is.EqualTo(new float4(0f, 0f, 0f, 1f)));
            float4 n = SlicerController.CutNormal(SlicerObject.Tube, math.radians(30f));
            Assert.That(n.z, Is.EqualTo(-0.5f).Within(1e-6f));
            Assert.That(n.w, Is.EqualTo(math.sqrt(3f) / 2f).Within(1e-6f));
            Assert.That(math.length(n), Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void TheSteppedSource_MovesTheCutInSteps()
        {
            SlicerLayout layout = SlicerLayout.FromRoom(3f, 4f, false);
            var controller = new SlicerController(in layout);
            float step = 2.2f / SlicerController.SteppedSteps;
            controller.SetCut(0.26f);
            Assert.That(controller.CutOffset(SlicerSource.Live), Is.EqualTo(0.26f));
            Assert.That(controller.CutOffset(SlicerSource.TesseractOnly), Is.EqualTo(0.26f));
            Assert.That(controller.CutOffset(SlicerSource.Stepped), Is.EqualTo(4f * step).Within(1e-6f));
            controller.SetCut(1.1f);
            Assert.That(controller.CutOffset(SlicerSource.Stepped), Is.EqualTo(1.1f).Within(1e-6f));
        }

        static void AssertEqual(float4x4 a, float4x4 b, float tolerance)
        {
            for (int c = 0; c < 4; c++)
                Assert.That(math.cmax(math.abs(a[c] - b[c])), Is.LessThan(tolerance), "column " + c);
        }
    }
}
