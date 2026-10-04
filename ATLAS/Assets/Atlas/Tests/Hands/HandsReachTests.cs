using System;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Hands.Tests
{
    /// <summary>
    /// The near anchor (documents/ATLAS_Hands_Design.md section 6.1, with decision R3 and the review): where it
    /// is placed from the camera, that it stays put until the camera has moved or the heading has turned, that
    /// it follows a walker, that it holds still under a fingertip, and that it keeps out of the room's walls.
    /// Time is passed in, so every test steps a clock at 60 frames a second.
    /// </summary>
    public class HandsReachTests
    {
        const float Dt = 1f / 60f;
        static readonly float3 Up = new float3(0f, 1f, 0f);
        static readonly float3 Forward = new float3(0f, 0f, 1f);

        /// <summary>A head turned to a heading (degrees; 0 faces +Z, positive turns towards +X), pitched down and rolled.</summary>
        static quaternion Head(float yawDegrees, float pitchDegrees = 0f, float rollDegrees = 0f)
        {
            return math.mul(math.mul(quaternion.RotateY(math.radians(yawDegrees)), quaternion.RotateX(math.radians(pitchDegrees))),
                quaternion.RotateZ(math.radians(rollDegrees)));
        }

        /// <summary>The direction of the anchor's line from the camera: along a heading, a pitch below the horizontal.</summary>
        static float3 Line(float yawDegrees, float pitchDegrees)
        {
            float yaw = math.radians(yawDegrees), pitch = math.radians(pitchDegrees);
            return new float3(math.sin(yaw) * math.cos(pitch), -math.sin(pitch), math.cos(yaw) * math.cos(pitch));
        }

        /// <summary>Where the anchor belongs in free space: ReachMetres from the camera, ReachPitchDegrees down.</summary>
        static float3 Placed(float3 camera, float yawDegrees, float reach = HandsConstants.ReachMetres)
        {
            return camera + reach * Line(yawDegrees, HandsConstants.ReachPitchDegrees);
        }

        static void AssertNear(float3 expected, float3 actual, float tolerance, string what)
        {
            Assert.That(math.distance(expected, actual), Is.LessThanOrEqualTo(tolerance), what + ": expected " + expected + ", got " + actual);
        }

        static float PitchBelowHorizontal(float3 offset)
        {
            return math.degrees(math.asin(-offset.y / math.length(offset)));
        }

        /// <summary>The anchor, a clock and everything Update is given, with a walker standing 1.5 m up, facing +Z.</summary>
        sealed class Rig
        {
            public readonly ReachAnchor Anchor = new ReachAnchor();
            public RoomSurfaces Surfaces;
            public double Now;
            public bool Near = true;
            public float3 Camera = new float3(0f, 1.5f, 0f);
            public float Yaw, Pitch = 10f, Roll;
            public float Speed;
            public bool LeftValid, RightValid, Pressing;
            public float3 Left, Right;

            public void Step(int frames = 1)
            {
                for (int i = 0; i < frames; i++)
                {
                    Anchor.Update(Now, Dt, Near, Camera, Head(Yaw, Pitch, Roll), Speed, LeftValid, Left, RightValid, Right, Pressing, Surfaces);
                    Now += Dt;
                }
            }

            public void Run(float seconds) { Step((int)math.round(seconds / Dt)); }

            /// <summary>The walker walks along +Z at a speed for some seconds, reporting that speed.</summary>
            public void Walk(float speed, float seconds, Action<int> afterEachFrame = null)
            {
                Speed = speed;
                int frames = (int)math.round(seconds / Dt);
                for (int i = 0; i < frames; i++)
                {
                    Camera += new float3(0f, 0f, speed * Dt);
                    Step();
                    if (afterEachFrame != null) afterEachFrame(i);
                }
            }
        }

        static RoomSurfaces Surfaces(string fixture)
        {
            var surfaces = new RoomSurfaces();
            surfaces.SetRoom(RoomFixtures.LoadFromProject(fixture));
            return surfaces;
        }

        static void AssertClear(RoomSurfaces surfaces, float3 position, string what)
        {
            float3 point, normal;
            Assert.That(surfaces.TryClosest(position, 0.049f, out point, out normal), Is.False,
                what + ": " + position + " is within 5 cm of a surface at " + point);
        }

        // ---- The object ----------------------------------------------------------------------------------------

        [Test]
        public void Hands_Reach_Anchor_BeginsNotNear()
        {
            var anchor = new ReachAnchor();
            Assert.That(anchor.Near, Is.False);
            Assert.That(anchor.Scale, Is.EqualTo(1f));
            Assert.That(anchor.ReachMetres, Is.EqualTo(HandsConstants.ReachMetres));
            Assert.That(anchor.Position, Is.EqualTo(float3.zero));
            Assert.That(anchor.Rotation, Is.EqualTo(quaternion.identity));
            IReachLayout layout = anchor;   // what the panels are given
            Assert.That(layout.Near, Is.False);
            Assert.That(layout.Scale, Is.EqualTo(1f));
        }

        [Test]
        public void Hands_Reach_Anchor_ReachMetresIsClamped()
        {
            var anchor = new ReachAnchor();
            anchor.ReachMetres = 0.2f;
            Assert.That(anchor.ReachMetres, Is.EqualTo(HandsConstants.ReachMinMetres));
            anchor.ReachMetres = 0.9f;
            Assert.That(anchor.ReachMetres, Is.EqualTo(HandsConstants.ReachMaxMetres));
            anchor.ReachMetres = 0.55f;
            Assert.That(anchor.ReachMetres, Is.EqualTo(0.55f));
            anchor.ReachMetres = float.NaN;
            Assert.That(anchor.ReachMetres, Is.EqualTo(0.55f), "NaN is ignored");
        }

        // ---- Placement -----------------------------------------------------------------------------------------

        [Test]
        public void Hands_Reach_Anchor_PlacedFromTheCamera()
        {
            float3[] cameras = { new float3(0f, 1.6f, 0f), new float3(0.3f, 1.4f, -0.5f), new float3(-2f, 1.2f, 3f) };
            float[] yaws = { 0f, 30f, -135f };
            for (int i = 0; i < cameras.Length; i++)
            {
                var rig = new Rig { Camera = cameras[i], Yaw = yaws[i] };
                rig.Step();
                ReachAnchor a = rig.Anchor;
                Assert.That(a.Near, Is.True);
                Assert.That(a.Scale, Is.EqualTo(HandsConstants.ReachPanelScale));
                AssertNear(Placed(cameras[i], yaws[i]), a.Position, 1e-4f, "position " + i);
                Assert.That(math.distance(a.Position, cameras[i]), Is.EqualTo(HandsConstants.ReachMetres).Within(1e-4f), "measured from the camera");

                float3 forward = math.mul(a.Rotation, Forward), up = math.mul(a.Rotation, Up), right = math.mul(a.Rotation, new float3(1f, 0f, 0f));
                AssertNear(math.normalize(a.Position - cameras[i]), forward, 1e-4f, "+Z points away from the walker");
                Assert.That(forward.y, Is.EqualTo(-math.sin(math.radians(HandsConstants.ReachPitchDegrees))).Within(1e-4f), "on a line 25 degrees down");
                Assert.That(up.y, Is.GreaterThan(0.9f), "+Y is up along the panel");
                Assert.That(math.dot(up, forward), Is.EqualTo(0f).Within(1e-4f));
                Assert.That(math.dot(-forward, math.normalize(cameras[i] - a.Position)), Is.GreaterThan(0.9999f), "the panel faces the camera");
                float yaw = math.radians(yaws[i]);
                Assert.That(math.dot(right, new float3(math.cos(yaw), 0f, -math.sin(yaw))), Is.GreaterThan(0.9999f), "+X is the walker's right");
            }
        }

        [Test]
        public void Hands_Reach_Anchor_UsesTheHeadingNotThePitchOrTheRoll()
        {
            var level = new Rig { Yaw = 70f, Pitch = 0f };
            level.Step();
            foreach (float pitch in new[] { 10f, 40f, 75f, -40f })
            {
                foreach (float roll in new[] { 0f, 30f, -45f })
                {
                    var rig = new Rig { Yaw = 70f, Pitch = pitch, Roll = roll };
                    rig.Step();
                    AssertNear(level.Anchor.Position, rig.Anchor.Position, 1e-4f, "pitch " + pitch + ", roll " + roll);
                }
            }
        }

        [Test]
        public void Hands_Reach_Anchor_ReachMetresSetsTheDistance()
        {
            foreach (float reach in new[] { 0.40f, 0.52f, 0.60f })
            {
                var rig = new Rig { Yaw = 15f };
                rig.Anchor.ReachMetres = reach;
                rig.Step();
                Assert.That(math.distance(rig.Anchor.Position, rig.Camera), Is.EqualTo(reach).Within(1e-4f));
                AssertNear(Placed(rig.Camera, 15f, reach), rig.Anchor.Position, 1e-4f, "reach " + reach);
            }
        }

        [Test]
        public void Hands_Reach_Anchor_LookingStraightDown_TheHeadingIsTheHeadsUpVectors()
        {
            // Within 6 degrees of straight down or up the forward vector has no heading; HeadPose.Heading uses the
            // head's up vector then, which points the way the walker faces, so the panel stays in front of them.
            foreach (float yaw in new[] { 0f, 90f, 200f, -50f })
            {
                foreach (float pitch in new[] { 80f, 85f, 89.9f, 90f, -80f, -90f })
                {
                    var rig = new Rig { Yaw = yaw, Pitch = pitch };
                    rig.Step();
                    float3 offset = rig.Anchor.Position - rig.Camera;
                    float2 flat = math.normalize(offset.xz);
                    float2 facing = new float2(math.sin(math.radians(yaw)), math.cos(math.radians(yaw)));
                    string at = "yaw " + yaw + ", pitch " + pitch;
                    Assert.That(math.dot(flat, facing), Is.GreaterThan(0.999f), at);
                    Assert.That(offset.y, Is.EqualTo(-HandsConstants.ReachMetres * math.sin(math.radians(HandsConstants.ReachPitchDegrees))).Within(1e-4f), at);
                    float2 heading = new HeadPose(0.0, rig.Camera, Head(yaw, pitch), TrackingState.Normal).Heading;
                    Assert.That(math.dot(flat, heading), Is.GreaterThan(0.9999f), at + ": as HeadPose.Heading");
                }
            }
        }

        [Test]
        public void Hands_Reach_Anchor_LookingDownAtItDoesNotMoveIt()
        {
            var rig = new Rig { Yaw = 30f, Pitch = 20f };
            rig.Step();
            float3 start = rig.Anchor.Position;
            for (float pitch = 20f; pitch <= 90f; pitch += 1f)
            {
                rig.Pitch = pitch;
                rig.Step();
                Assert.That(rig.Anchor.Position, Is.EqualTo(start), "pitch " + pitch);
            }
            for (float pitch = 90f; pitch >= -90f; pitch -= 2f)
            {
                rig.Pitch = pitch;
                rig.Step();
                Assert.That(rig.Anchor.Position, Is.EqualTo(start), "pitch " + pitch);
            }
        }

        // ---- Near ----------------------------------------------------------------------------------------------

        [Test]
        public void Hands_Reach_Anchor_WhenNotNear_KeepsItsPoseAndTheScaleIsOne()
        {
            var rig = new Rig();
            rig.Step();
            float3 position = rig.Anchor.Position;
            quaternion rotation = rig.Anchor.Rotation;
            Assert.That(rig.Anchor.Scale, Is.EqualTo(HandsConstants.ReachPanelScale));

            rig.Near = false;
            rig.Camera = new float3(2f, 1.5f, 1f);
            rig.Yaw = 90f;
            rig.Step(30);
            Assert.That(rig.Anchor.Near, Is.False);
            Assert.That(rig.Anchor.Scale, Is.EqualTo(1f));
            Assert.That(rig.Anchor.Position, Is.EqualTo(position));
            Assert.That(rig.Anchor.Rotation, Is.EqualTo(rotation));
        }

        [Test]
        public void Hands_Reach_Anchor_IsSetAtOnce_WhenNearBecomesTrue()
        {
            var rig = new Rig();
            rig.Step();
            rig.Near = false;
            rig.Camera = new float3(2f, 1.5f, 1f);
            rig.Yaw = 90f;
            rig.Step(30);

            rig.Near = true;
            rig.Step();
            Assert.That(rig.Anchor.Near, Is.True);
            Assert.That(rig.Anchor.Scale, Is.EqualTo(HandsConstants.ReachPanelScale));
            AssertNear(Placed(rig.Camera, 90f), rig.Anchor.Position, 1e-4f, "set where the walker is now, not eased from the old place");

            // Also when the old place is still within a metre of the camera, where only "near became true" can
            // explain it: a step to the side and a quarter turn, and the very first frame has the anchor in front.
            var near = new Rig();
            near.Step();
            near.Near = false;
            near.Camera = new float3(0.4f, 1.5f, 0f);
            near.Yaw = 90f;
            near.Step(30);
            Assert.That(math.distance(near.Anchor.Position, near.Camera), Is.LessThan(1f), "the old place is not far");
            near.Near = true;
            near.Step();
            AssertNear(Placed(near.Camera, 90f), near.Anchor.Position, 1e-4f, "set at once");
        }

        [Test]
        public void Hands_Reach_Anchor_NearDoesNotChange_WhileAPressIsUnderWay()
        {
            // Panels register their targets anew when Near changes, which they must not do under a finger.
            var rig = new Rig();
            rig.Step();
            float3 placed = rig.Anchor.Position;
            rig.Pressing = true;
            rig.Near = false;                      // the gate drops in mid-press
            rig.Step(10);
            Assert.That(rig.Anchor.Near, Is.True, "it waits for the press to end");
            Assert.That(rig.Anchor.Scale, Is.EqualTo(HandsConstants.ReachPanelScale));
            Assert.That(rig.Anchor.Position, Is.EqualTo(placed));
            rig.Pressing = false;
            rig.Step();
            Assert.That(rig.Anchor.Near, Is.False, "and then it follows the gate");
            Assert.That(rig.Anchor.Scale, Is.EqualTo(1f));
            Assert.That(rig.Anchor.Position, Is.EqualTo(placed), "keeping the pose it had");

            // The other way: a press on something else as the gate comes up.
            var coming = new Rig { Near = false };
            coming.Step();
            Assert.That(coming.Anchor.Near, Is.False);
            coming.Pressing = true;
            coming.Near = true;
            coming.Step(10);
            Assert.That(coming.Anchor.Near, Is.False, "not while a press is under way");
            coming.Pressing = false;
            coming.Step();
            Assert.That(coming.Anchor.Near, Is.True);
            AssertNear(Placed(coming.Camera, 0f), coming.Anchor.Position, 1e-4f, "set at once when it comes");
        }

        [Test]
        public void Hands_Reach_Anchor_IsSetAgain_WhenMoreThanAMetreFromTheCamera()
        {
            var rig = new Rig();
            rig.Step();
            rig.Camera = new float3(3f, 1.5f, 2f);    // a jump: the anchor is now far away
            rig.Step();
            AssertNear(Placed(rig.Camera, 0f), rig.Anchor.Position, 1e-4f, "set at once");
        }

        [Test]
        public void Hands_Reach_Anchor_Reset_ForgetsThePlacement()
        {
            var rig = new Rig();
            rig.Anchor.ReachMetres = 0.55f;
            rig.Step();
            rig.Anchor.Reset();
            Assert.That(rig.Anchor.Near, Is.False);
            Assert.That(rig.Anchor.Scale, Is.EqualTo(1f));
            Assert.That(rig.Anchor.Position, Is.EqualTo(float3.zero));
            Assert.That(rig.Anchor.Rotation, Is.EqualTo(quaternion.identity));
            Assert.That(rig.Anchor.ReachMetres, Is.EqualTo(0.55f), "the setting is not state");

            rig.Camera = new float3(0.2f, 1.5f, 0.1f);    // near the old place: only a fresh set puts it right
            rig.Step();
            AssertNear(Placed(rig.Camera, 0f, 0.55f), rig.Anchor.Position, 1e-4f, "set afresh");
        }

        // ---- Staying put and moving ----------------------------------------------------------------------------

        [Test]
        public void Hands_Reach_Anchor_StaysPut_UnderSmallHeadMotion()
        {
            var rig = new Rig();
            rig.Step();
            float3 start = rig.Anchor.Position;
            quaternion startRotation = rig.Anchor.Rotation;
            for (int i = 0; i < 360; i++)
            {
                float t = i * Dt;
                rig.Camera = new float3(0.05f * math.sin(t * 5f), 1.5f + 0.04f * math.sin(t * 7f), 0.05f * math.cos(t * 3f));
                rig.Yaw = 12f * math.sin(t * 4f);
                rig.Pitch = 10f + 30f * math.sin(t * 2f);
                rig.Step();
                Assert.That(rig.Anchor.Position, Is.EqualTo(start), "frame " + i);
                Assert.That(rig.Anchor.Rotation, Is.EqualTo(startRotation), "frame " + i);
            }
        }

        [Test]
        public void Hands_Reach_Anchor_Moves_OnlyPastTheCameraDistance()
        {
            var rig = new Rig();
            rig.Step();
            float3 start = rig.Anchor.Position;

            rig.Camera = new float3(0f, 1.5f, 0.24f);
            rig.Run(2f);
            Assert.That(rig.Anchor.Position, Is.EqualTo(start), "0.24 m is not enough");

            rig.Camera = new float3(0f, 1.5f, 0.26f);
            rig.Run(0.05f);
            Assert.That(rig.Anchor.Position, Is.Not.EqualTo(start), "0.26 m moves it");
            Assert.That(math.distance(rig.Anchor.Position, Placed(rig.Camera, 0f)), Is.GreaterThan(0.01f), "and it eases: it is not there yet");
            rig.Run(0.4f);
            AssertNear(Placed(rig.Camera, 0f), rig.Anchor.Position, 1e-3f, "it arrives where the walker is now");

            // From its new place the distance counts afresh.
            float3 arrived = rig.Anchor.Position;
            rig.Camera += new float3(0f, 0f, 0.2f);
            rig.Run(1f);
            Assert.That(rig.Anchor.Position, Is.EqualTo(arrived), "0.20 m from the new place is not enough");
        }

        [Test]
        public void Hands_Reach_Anchor_Moves_WhenTheWalkerCrouches()
        {
            var rig = new Rig();
            rig.Step();
            float3 start = rig.Anchor.Position;
            rig.Camera = new float3(0f, 1.3f, 0f);     // 0.2 m down: not enough
            rig.Run(1f);
            Assert.That(rig.Anchor.Position, Is.EqualTo(start));
            rig.Camera = new float3(0f, 1.1f, 0f);     // 0.4 m down: the height counts like any other distance
            rig.Run(0.6f);
            AssertNear(Placed(rig.Camera, 0f), rig.Anchor.Position, 1e-3f, "crouched");
        }

        [Test]
        public void Hands_Reach_Anchor_Moves_OnlyAfterTheHeadingHasBeenPastFortyDegreesForHalfASecond()
        {
            // 39 degrees, held for good: nothing.
            var rig = new Rig();
            rig.Step();
            float3 start = rig.Anchor.Position;
            rig.Yaw = 39f;
            rig.Run(3f);
            Assert.That(rig.Anchor.Position, Is.EqualTo(start), "39 degrees");

            // 45 degrees for under half a second and back: nothing.
            rig = new Rig();
            rig.Step();
            start = rig.Anchor.Position;
            rig.Yaw = 45f;
            rig.Run(0.45f);
            rig.Yaw = 0f;
            rig.Run(2f);
            Assert.That(rig.Anchor.Position, Is.EqualTo(start), "45 degrees for 0.45 s");

            // Past half a second it moves, eased, to the place that faces the new heading.
            rig = new Rig();
            rig.Step();
            start = rig.Anchor.Position;
            rig.Yaw = 45f;
            rig.Run(0.55f);
            Assert.That(rig.Anchor.Position, Is.Not.EqualTo(start), "45 degrees for half a second");
            rig.Run(0.4f);
            AssertNear(Placed(rig.Camera, 45f), rig.Anchor.Position, 1e-3f, "now in front of the walker");

            // A dip under the limit starts the half second again.
            rig = new Rig();
            rig.Step();
            start = rig.Anchor.Position;
            rig.Yaw = 45f;
            rig.Run(0.4f);
            rig.Yaw = 20f;
            rig.Run(0.1f);
            rig.Yaw = 45f;
            rig.Run(0.4f);
            Assert.That(rig.Anchor.Position, Is.EqualTo(start), "0.4 s, a dip, 0.4 s");
            rig.Run(0.2f);
            Assert.That(rig.Anchor.Position, Is.Not.EqualTo(start), "0.6 s in a row");
        }

        [Test]
        public void Hands_Reach_Anchor_TheHeadingDifferenceIsAnAngleNotADifferenceOfYaws()
        {
            // 170 degrees to -170 degrees is a turn of 20 degrees, not 340.
            var rig = new Rig { Yaw = 170f };
            rig.Step();
            float3 start = rig.Anchor.Position;
            rig.Yaw = -170f;
            rig.Run(3f);
            Assert.That(rig.Anchor.Position, Is.EqualTo(start));
            // And the same side's 50 degrees still moves it.
            rig.Yaw = -140f;
            rig.Run(1f);
            Assert.That(rig.Anchor.Position, Is.Not.EqualTo(start));
        }

        [Test]
        public void Hands_Reach_Anchor_EasesOverTheEaseTime()
        {
            var rig = new Rig();
            rig.Step();
            float3 start = rig.Anchor.Position;
            rig.Camera = new float3(0f, 1.5f, 0.5f);
            float3 target = Placed(rig.Camera, 0f);
            float total = math.distance(start, target);
            int frames = (int)math.round(HandsConstants.ReachEaseSeconds / Dt);

            float previous = 0f;
            for (int i = 1; i <= frames + 6; i++)
            {
                rig.Step();
                float done = math.distance(start, rig.Anchor.Position) / total;
                Assert.That(done, Is.GreaterThanOrEqualTo(previous - 1e-4f), "it never goes back, frame " + i);
                Assert.That(done, Is.LessThanOrEqualTo(1f + 1e-3f), "it never overshoots, frame " + i);
                if (i == frames / 2) Assert.That(done, Is.EqualTo(0.5f).Within(0.03f), "half way at half the time");
                // Gentle at both ends: a quarter of the way through it has done under a fifth, and at three quarters over three quarters.
                if (i == frames / 4) Assert.That(done, Is.LessThan(0.20f), "gentle at the start");
                if (i == frames * 3 / 4) Assert.That(done, Is.GreaterThan(0.77f), "gentle at the end");
                if (i < frames - 1) Assert.That(done, Is.LessThan(0.999f), "not there before its time, frame " + i);
                previous = done;
            }
            AssertNear(target, rig.Anchor.Position, 1e-3f, "arrived");
        }

        [Test]
        public void Hands_Reach_Anchor_ToleratesAZeroNegativeOrNaNFrameTime()
        {
            var rig = new Rig();
            rig.Step();
            float3 start = rig.Anchor.Position;
            rig.Camera = new float3(0f, 1.5f, 0.5f);       // far enough to start an ease
            foreach (float dt in new[] { 0f, -1f, float.NaN })
            {
                rig.Anchor.Update(rig.Now, dt, true, rig.Camera, Head(0f, 10f), 0f, false, float3.zero, false, float3.zero, false, null);
                float3 p = rig.Anchor.Position;
                Assert.That(float.IsNaN(p.x + p.y + p.z), Is.False, "dt " + dt);
                Assert.That(p, Is.EqualTo(start), "an ease does not advance on no time, dt " + dt);
            }
            rig.Run(0.5f);
            AssertNear(Placed(rig.Camera, 0f), rig.Anchor.Position, 1e-3f, "and then it arrives");
        }

        [Test]
        public void Hands_Reach_Anchor_ChangingReachMetresPlacesItAgain()
        {
            var rig = new Rig();
            rig.Step();
            float3 start = rig.Anchor.Position;
            rig.Anchor.ReachMetres = 0.6f;
            rig.Step();
            Assert.That(rig.Anchor.Position, Is.Not.EqualTo(start), "it starts to move");
            rig.Run(0.5f);
            AssertNear(Placed(rig.Camera, 0f, 0.6f), rig.Anchor.Position, 1e-3f, "the new reach");

            float3 held = rig.Anchor.Position;
            rig.Anchor.ReachMetres = 0.6f;    // the same value again
            rig.Run(1f);
            Assert.That(rig.Anchor.Position, Is.EqualTo(held), "nothing changed, nothing moved");
        }

        // ---- Walking -------------------------------------------------------------------------------------------

        [Test]
        public void Hands_Reach_Anchor_FollowsEveryFrame_WhileWalking()
        {
            var rig = new Rig();
            rig.Step();
            float ahead = HandsConstants.ReachMetres * math.cos(math.radians(HandsConstants.ReachPitchDegrees));
            float3 first = rig.Anchor.Position;
            rig.Walk(1f, 4f, i =>
            {
                float3 offset = rig.Anchor.Position - rig.Camera;
                Assert.That(math.length(offset), Is.EqualTo(HandsConstants.ReachMetres).Within(0.01f), "frame " + i);
                Assert.That(offset.z, Is.EqualTo(ahead).Within(0.01f), "never left behind and never walked through, frame " + i);
            });
            Assert.That(rig.Anchor.Position.z - first.z, Is.EqualTo(4f).Within(0.05f), "four metres on");
        }

        [Test]
        public void Hands_Reach_Anchor_DoesNotFollowEveryFrame_BelowWalkingSpeed()
        {
            var rig = new Rig();
            rig.Step();
            float3 start = rig.Anchor.Position;
            rig.Walk(0.29f, 0.5f);    // the reported speed is under 0.3 m/s: the distance rule decides, and 0.145 m is short of it
            Assert.That(rig.Anchor.Position, Is.EqualTo(start));
        }

        [Test]
        public void Hands_Reach_Anchor_Walking_StartsAboveAndEndsWellBelowTheThreshold()
        {
            float reach = HandsConstants.ReachMetres;
            var rig = new Rig();
            rig.Step();
            Action<int> following = i =>
                Assert.That(math.length(rig.Anchor.Position - rig.Camera), Is.EqualTo(reach).Within(0.01f), "still following, frame " + i);

            rig.Walk(0.35f, 1f, following);                 // above 0.3: it follows
            rig.Walk(0.20f, 1f, following);                 // between 0.15 and 0.3: it carries on
            rig.Walk(0.10f, 0.25f, following);              // under 0.15, for less than 0.3 s: it carries on
            rig.Walk(0.20f, 0.1f, following);               // back over 0.15: the 0.3 s starts again
            rig.Walk(0.10f, 0.25f, following);
            rig.Walk(0.10f, 0.2f);                          // 0.45 s under 0.15: it stops following
            float3 held = rig.Anchor.Position;
            rig.Walk(0.10f, 0.6f);                          // and holds still while the camera moves 6 cm
            Assert.That(rig.Anchor.Position, Is.EqualTo(held));

            // Standing, the camera-distance rule applies again, from where it stopped.
            rig.Camera += new float3(0f, 0f, 0.3f);         // 30 cm in one step: past 0.25 m from where it was set
            rig.Run(1f);
            AssertNear(Placed(rig.Camera, 0f), rig.Anchor.Position, 1e-3f, "re-placed after the walker had moved 0.25 m");
        }

        [Test]
        public void Hands_Reach_Anchor_WalkingBeginsWithoutAJump()
        {
            var rig = new Rig();
            rig.Step();
            rig.Yaw = 35f;       // under 40 degrees: it waits
            rig.Run(1f);
            float3 before = rig.Anchor.Position;
            float3 ideal = Placed(rig.Camera, 35f);
            Assert.That(math.distance(before, ideal), Is.GreaterThan(0.2f), "there is a gap to close");

            rig.Speed = 1f;      // the walker sets off; the camera stays, so the first step is the anchor's own
            rig.Step();
            Assert.That(math.distance(rig.Anchor.Position, before), Is.InRange(0.001f, 0.06f), "the first walking frame does not jump");
            rig.Run(0.6f);
            AssertNear(ideal, rig.Anchor.Position, 0.01f, "caught up with the heading");
        }

        [Test]
        public void Hands_Reach_Anchor_AfterAWalk_TheLimitsCountFromWhereItStopped()
        {
            var rig = new Rig();
            rig.Step();
            rig.Walk(1f, 0.5f);
            rig.Yaw = 80f;               // a sharp turn as the walk ends: past 40 degrees from where it was set before the walk
            rig.Speed = 0f;              // and the walker stops
            rig.Run(0.35f);              // it follows for 0.3 s more, then holds
            float3 held = rig.Anchor.Position;
            rig.Run(2f);
            Assert.That(rig.Anchor.Position, Is.EqualTo(held), "it was set for this heading while it followed, so nothing is owed");
        }

        // ---- Held still ----------------------------------------------------------------------------------------

        [Test]
        public void Hands_Reach_Anchor_Frozen_WhileAFingertipIsNear()
        {
            foreach (bool leftHand in new[] { true, false })
            {
                var rig = new Rig();
                rig.Step();
                float3 start = rig.Anchor.Position;
                float3 tip = start + new float3(0.10f, 0f, 0f);       // within 15 cm of it
                if (leftHand) { rig.LeftValid = true; rig.Left = tip; } else { rig.RightValid = true; rig.Right = tip; }

                rig.Camera = new float3(0.3f, 1.5f, 0.5f);            // past the camera-distance rule
                rig.Yaw = 90f;                                        // and long past the heading rule
                rig.Run(2f);
                Assert.That(rig.Anchor.Position, Is.EqualTo(start), (leftHand ? "left" : "right") + " fingertip near");
                Assert.That(rig.Anchor.Scale, Is.EqualTo(HandsConstants.ReachPanelScale));

                rig.LeftValid = rig.RightValid = false;               // the finger leaves
                rig.Run(1f);
                AssertNear(Placed(rig.Camera, 90f), rig.Anchor.Position, 1e-3f, "free again");
            }
        }

        [Test]
        public void Hands_Reach_Anchor_Frozen_AtFifteenCentimetres_AndNotBeyond()
        {
            var rig = new Rig();
            rig.Step();
            float3 start = rig.Anchor.Position;
            rig.RightValid = true;
            rig.Right = start + new float3(0f, 0f, HandsConstants.ReachFreezeMetres - 0.001f);
            rig.Camera = new float3(0f, 1.5f, 0.5f);
            rig.Run(1f);
            Assert.That(rig.Anchor.Position, Is.EqualTo(start), "14.9 cm");

            rig.Right = start + new float3(0f, 0f, HandsConstants.ReachFreezeMetres + 0.001f);
            rig.Run(0.1f);
            Assert.That(rig.Anchor.Position, Is.Not.EqualTo(start), "15.1 cm");
        }

        [Test]
        public void Hands_Reach_Anchor_AFingertipThatIsNotValidDoesNotFreezeIt()
        {
            var rig = new Rig();
            rig.Step();
            float3 start = rig.Anchor.Position;
            rig.LeftValid = false;
            rig.Left = start;                  // on the anchor, but not a tip anyone can trust
            rig.RightValid = false;
            rig.Right = start + new float3(0.02f, 0f, 0f);
            rig.Camera = new float3(0f, 1.5f, 0.5f);
            rig.Run(0.1f);
            Assert.That(rig.Anchor.Position, Is.Not.EqualTo(start));
        }

        [Test]
        public void Hands_Reach_Anchor_Frozen_WhilePressing()
        {
            var rig = new Rig();
            rig.Step();
            float3 start = rig.Anchor.Position;
            rig.Pressing = true;
            rig.Camera = new float3(0.3f, 1.5f, 0.5f);
            rig.Yaw = 90f;
            rig.Run(2f);
            Assert.That(rig.Anchor.Position, Is.EqualTo(start), "pressing");

            rig.Pressing = false;
            rig.Run(1f);
            AssertNear(Placed(rig.Camera, 90f), rig.Anchor.Position, 1e-3f, "released");
        }

        [Test]
        public void Hands_Reach_Anchor_Frozen_EvenFarFromTheCamera()
        {
            // "Never moves": the rule that sets it again beyond a metre does not override a press.
            var rig = new Rig();
            rig.Step();
            float3 start = rig.Anchor.Position;
            rig.Pressing = true;
            rig.Camera = new float3(4f, 1.5f, 4f);
            rig.Run(1f);
            Assert.That(rig.Anchor.Position, Is.EqualTo(start));
            rig.Pressing = false;
            rig.Step();
            AssertNear(Placed(rig.Camera, 0f), rig.Anchor.Position, 1e-4f, "set at once when the press ends");
        }

        [Test]
        public void Hands_Reach_Anchor_Frozen_WhileWalkingToo()
        {
            var rig = new Rig();
            rig.Step();
            float3 start = rig.Anchor.Position;
            rig.RightValid = true;
            rig.Right = start + new float3(0.05f, 0.05f, 0f);
            rig.Walk(1f, 0.5f);
            Assert.That(rig.Anchor.Position, Is.EqualTo(start), "walking with a finger on it");

            rig.RightValid = false;
            rig.Walk(1f, 1f);
            Assert.That(math.length(rig.Anchor.Position - rig.Camera), Is.EqualTo(HandsConstants.ReachMetres).Within(0.01f), "it follows again");
        }

        [Test]
        public void Hands_Reach_Anchor_Frozen_PausesAnEaseAndResumesIt()
        {
            var rig = new Rig();
            rig.Step();
            float3 start = rig.Anchor.Position;
            rig.Camera = new float3(0f, 1.5f, 0.5f);
            float3 target = Placed(rig.Camera, 0f);
            rig.Step(6);
            float3 held = rig.Anchor.Position;
            Assert.That(math.distance(held, start), Is.InRange(0.01f, math.distance(start, target) - 0.01f), "part of the way");

            rig.RightValid = true;             // a fingertip arrives on it
            rig.Right = held;
            rig.Run(1f);
            Assert.That(rig.Anchor.Position, Is.EqualTo(held), "it waits where it was");

            rig.RightValid = false;
            rig.Step();
            float step = math.distance(held, rig.Anchor.Position);
            Assert.That(step, Is.InRange(1e-4f, 0.08f), "it carries on from there, with no jump");
            rig.Run(0.5f);
            AssertNear(target, rig.Anchor.Position, 1e-3f, "and gets where it was going");
        }

        // ---- The room -------------------------------------------------------------------------------------------

        [Test]
        public void Hands_Reach_Anchor_PulledBack_FromAWall()
        {
            RoomSurfaces surfaces = Surfaces(RoomFixtures.Sample3x3);       // walls at +-1.5 m
            float3 camera = new float3(0f, 1.5f, 1.1f);

            // Facing the wall z = 1.5 from 0.4 m: the unconstrained anchor would be behind it, at z = 1.535.
            var free = new Rig { Camera = camera };
            free.Step();
            Assert.That(free.Anchor.Position.z, Is.GreaterThan(1.5f), "without the room it is behind the wall");

            var rig = new Rig { Camera = camera, Surfaces = surfaces };
            rig.Step();
            float3 line = Line(0f, HandsConstants.ReachPitchDegrees);
            float t = (1.45f - camera.z) / line.z;
            AssertNear(camera + line * t, rig.Anchor.Position, 3e-3f, "5 cm in front of the wall, along the same line");
            Assert.That(rig.Anchor.Position.z, Is.EqualTo(1.45f).Within(3e-3f));
            Assert.That(math.length(rig.Anchor.Position - camera), Is.GreaterThanOrEqualTo(0.30f), "still within reach of LiDAR");
            AssertClear(surfaces, rig.Anchor.Position, "wall");
            Assert.That(PitchBelowHorizontal(rig.Anchor.Position - camera), Is.EqualTo(HandsConstants.ReachPitchDegrees).Within(0.1f), "the pitch is as it was");
            Assert.That(rig.Anchor.Rotation, Is.EqualTo(free.Anchor.Rotation), "and so is the tilt");
        }

        [Test]
        public void Hands_Reach_Anchor_PulledBack_FromAWallSeenSideways()
        {
            RoomSurfaces surfaces = Surfaces(RoomFixtures.Sample3x3);
            float3 camera = new float3(1.1f, 1.5f, 0f);
            var rig = new Rig { Camera = camera, Yaw = 60f, Surfaces = surfaces };    // towards the wall x = 1.5, at an angle
            rig.Step();
            float3 line = Line(60f, HandsConstants.ReachPitchDegrees);
            float t = (1.45f - camera.x) / line.x;
            AssertNear(camera + line * t, rig.Anchor.Position, 3e-3f, "5 cm in front of the wall, along the same line");
            AssertClear(surfaces, rig.Anchor.Position, "oblique wall");
        }

        [Test]
        public void Hands_Reach_Anchor_NotPulledBack_WhenThereIsNoRoomOrItIsOff()
        {
            float3 camera = new float3(0f, 1.5f, 1.1f);
            var none = new Rig { Camera = camera };
            none.Step();

            RoomSurfaces off = Surfaces(RoomFixtures.Sample3x3);
            off.Enabled = false;
            var disabled = new Rig { Camera = camera, Surfaces = off };
            disabled.Step();

            var noRoom = new Rig { Camera = camera, Surfaces = new RoomSurfaces() };
            noRoom.Step();

            AssertNear(Placed(camera, 0f), none.Anchor.Position, 1e-4f, "no surfaces");
            AssertNear(Placed(camera, 0f), disabled.Anchor.Position, 1e-4f, "surfaces off");
            AssertNear(Placed(camera, 0f), noRoom.Anchor.Position, 1e-4f, "surfaces with no room");
        }

        [Test]
        public void Hands_Reach_Anchor_PulledBack_FromTheFloor_ItMovesUpAndNearerTheHorizontal()
        {
            RoomSurfaces surfaces = Surfaces(RoomFixtures.Sample3x3);

            // Crouched 15 cm off the floor, the 25 degree line would end 5 cm in front of the floor at 0.24 m, under
            // 0.30 m: so the line is raised, a step at a time, only as far as it takes.
            float3 camera = new float3(0f, 0.15f, 0f);
            var rig = new Rig { Camera = camera, Surfaces = surfaces };
            rig.Step();
            float3 offset = rig.Anchor.Position - camera;
            Assert.That(PitchBelowHorizontal(offset), Is.EqualTo(15f).Within(0.5f), "25, 20, then 15 degrees");
            Assert.That(math.length(offset), Is.GreaterThanOrEqualTo(0.30f));
            Assert.That(math.length(offset), Is.EqualTo(0.10f / math.sin(math.radians(15f))).Within(3e-3f), "5 cm above the floor");
            Assert.That(rig.Anchor.Position.y, Is.EqualTo(0.05f).Within(2e-3f));
            AssertClear(surfaces, rig.Anchor.Position, "floor");
            float3 forward = math.mul(rig.Anchor.Rotation, Forward);
            Assert.That(forward.y, Is.EqualTo(-math.sin(math.radians(15f))).Within(0.01f), "the panel tilts with its line");

            // Lower still, the line is nearly level.
            var low = new Rig { Camera = new float3(0f, 0.08f, 0f), Surfaces = surfaces };
            low.Step();
            Assert.That(PitchBelowHorizontal(low.Anchor.Position - low.Camera), Is.EqualTo(5f).Within(0.5f));
            Assert.That(math.length(low.Anchor.Position - low.Camera), Is.GreaterThanOrEqualTo(0.30f));
            AssertClear(surfaces, low.Anchor.Position, "lower");

            // Standing, nothing of this happens.
            var standing = new Rig { Camera = new float3(0f, 1.5f, 0f), Surfaces = surfaces };
            standing.Step();
            AssertNear(Placed(standing.Camera, 0f), standing.Anchor.Position, 1e-4f, "standing");
        }

        [Test]
        public void Hands_Reach_Anchor_PulledBack_FromATable()
        {
            // sample_4x5_table: the table's top is at 0.75 m over z -0.5 to 0.5, x -1.7 to -1.1. Leaning over its
            // edge, the 25 degree line would end inside its top.
            RoomSurfaces surfaces = Surfaces(RoomFixtures.Sample4x5Table);
            float3 camera = new float3(-1.4f, 0.9f, -0.8f);
            var free = new Rig { Camera = camera };
            free.Step();
            Assert.That(free.Anchor.Position.y, Is.LessThan(0.75f + 0.05f), "unconstrained it is in the table");

            var rig = new Rig { Camera = camera, Surfaces = surfaces };
            rig.Step();
            AssertClear(surfaces, rig.Anchor.Position, "table");
            float3 offset = rig.Anchor.Position - camera;
            Assert.That(math.length(offset), Is.GreaterThanOrEqualTo(0.30f));
            Assert.That(PitchBelowHorizontal(offset), Is.LessThan(HandsConstants.ReachPitchDegrees - 1f), "nearer the horizontal");
            Assert.That(rig.Anchor.Position.y, Is.GreaterThan(0.75f + 0.045f), "above the table's top");
        }

        [Test]
        public void Hands_Reach_Anchor_PulledBack_WithTheWalkersFaceAtAWall_ItIsNeverInsideTheHead()
        {
            RoomSurfaces surfaces = Surfaces(RoomFixtures.Sample3x3);
            foreach (float z in new[] { 1.48f, 1.43f, 1.40f, 1.30f })
            {
                var rig = new Rig { Camera = new float3(0f, 1.5f, z), Surfaces = surfaces };
                rig.Step();
                float3 offset = rig.Anchor.Position - rig.Camera;
                Assert.That(float.IsNaN(offset.x + offset.y + offset.z), Is.False, "z " + z);
                Assert.That(math.length(offset), Is.GreaterThanOrEqualTo(0.10f - 1e-4f), "never nearer than 10 cm, z " + z);
                float3 forward = math.mul(rig.Anchor.Rotation, Forward);
                Assert.That(math.length(forward), Is.EqualTo(1f).Within(1e-4f), "a proper rotation, z " + z);
            }
            // With room to spare it is pulled back as far as the wall asks and no further.
            var near = new Rig { Camera = new float3(0f, 1.5f, 1.30f), Surfaces = surfaces };
            near.Step();
            float t = (1.45f - 1.30f) / Line(0f, 25f).z;
            Assert.That(math.length(near.Anchor.Position - near.Camera), Is.EqualTo(math.max(t, 0.10f)).Within(3e-3f));
        }

        [Test]
        public void Hands_Reach_Anchor_PulledBack_WhileWalkingUpToAWall_NeverEntersIt()
        {
            RoomSurfaces surfaces = Surfaces(RoomFixtures.Sample3x3);
            var rig = new Rig { Camera = new float3(0f, 1.5f, -0.5f), Surfaces = surfaces };
            rig.Step();
            // Walk at 1 m/s up to 0.2 m from the wall z = 1.5, and stop there.
            rig.Walk(1f, 1.8f, i =>
            {
                float3 p = rig.Anchor.Position;
                Assert.That(p.z, Is.LessThanOrEqualTo(1.45f + 3e-3f), "never nearer than 5 cm to the wall, frame " + i);
            });
            Assert.That(rig.Camera.z, Is.EqualTo(1.3f).Within(0.02f));
            rig.Walk(0f, 1f);
            Assert.That(rig.Anchor.Position.z, Is.EqualTo(1.45f).Within(3e-3f));
        }

        // ---- Allocation ------------------------------------------------------------------------------------------

        static void Drive(Rig rig, int i)
        {
            float t = i * Dt;
            rig.Camera = new float3(-0.5f + 1.2f * math.sin(t * 0.7f), 1.5f, -0.5f + 1.2f * math.sin(t * 0.5f));
            rig.Yaw = 80f * math.sin(t * 0.9f);
            rig.Pitch = 20f + 25f * math.sin(t * 1.3f);
            rig.Speed = (i / 90) % 2 == 0 ? 1f : 0f;
            rig.Near = i % 400 < 380;
            rig.RightValid = (i / 70) % 3 == 0;
            rig.Right = rig.Anchor.Position + new float3(0.05f, 0.02f, 0f);
            rig.Pressing = (i / 130) % 5 == 4;
            rig.Step();
        }

        [Test]
        public void Hands_Reach_Anchor_AllocatesNothing()
        {
            var rig = new Rig { Surfaces = Surfaces(RoomFixtures.Sample4x5Table) };
            for (int i = 0; i < 800; i++) Drive(rig, i);   // warm up

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 800; i < 2800; i++) Drive(rig, i);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0), "bytes allocated by 2000 frames of walking, turning, pressing and flickering");
            Assert.That(float.IsNaN(rig.Anchor.Position.x), Is.False);
        }
    }
}
