using System.Collections.Generic;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Photo
{
    /// <summary>
    /// The preference for the views the generator was trained on (mesh design section 5.1): a view from the
    /// back half of the object, or from more than 30 degrees above its centre, scores half and is still a candidate.
    /// </summary>
    public class PhotoPreferenceTests
    {
        internal sealed class SizeEncoder : IPhotoEncoder
        {
            public bool RowsBottomUp { get { return false; } }
            public byte[] EncodeJpeg(byte[] rgba, int width, int height, int quality) { return new byte[1000]; }
        }

        internal sealed class Sink : IObjectPhotoSink
        {
            public readonly List<ObjectPhotoSet> Sets = new List<ObjectPhotoSet>();
            public readonly List<int> NoPhoto = new List<int>();

            public void BeginRoom(RoomModel room, string roomJson) { }
            public void Submit(ObjectPhotoSet photos) { Sets.Add(photos); }
            public void ReportNoPhoto(int objectId) { NoPhoto.Add(objectId); }
            public void EndRoom() { }
        }

        static readonly float3 s_base = new float3(0.4f, 0f, -0.3f);
        const float Yaw = 0.5f;

        // One chair, turned in the room; its front is its local +Z.
        static RoomModel Room()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            room.Objects = new[]
            {
                new RoomObject { Id = 1, Category = ObjectCategory.Chair, Base = s_base, Size = new float3(0.8f, 0.8f, 0.8f), Yaw = Yaw, TriTarget = 3000 }
            };
            return room;
        }

        static PhotoBox Chair()
        {
            return PhotoScore.ToCaptureFrame(Room().Objects[0], float3.zero, 0f);
        }

        /// <summary>A camera at an azimuth and an elevation about the box, in the box's own frame, looking at its centre.</summary>
        static void Orbit(in PhotoBox box, float azimuthDeg, float elevationDeg, float distance, out float3 position, out quaternion rotation)
        {
            float a = math.radians(azimuthDeg), e = math.radians(elevationDeg);
            var local = new float3(math.sin(a) * math.cos(e), math.sin(e), math.cos(a) * math.cos(e));
            float3 away = math.mul(box.Rotation, local);
            position = box.Centre + distance * away;
            rotation = quaternion.LookRotationSafe(-away, new float3(0f, 1f, 0f));
        }

        static PhotoCamera Camera(in PhotoBox box, float azimuthDeg, float elevationDeg, float distance)
        {
            float3 position;
            quaternion rotation;
            Orbit(in box, azimuthDeg, elevationDeg, distance, out position, out rotation);
            return PhotoScoreTests.Camera(position, rotation);
        }

        internal static void AddFrame(PhotoCaptureCore core, in PhotoBox box, float azimuthDeg, float elevationDeg, float distance)
        {
            float3 position;
            quaternion rotation;
            Orbit(in box, azimuthDeg, elevationDeg, distance, out position, out rotation);
            KeyFrame frame = core.Ring.Add(new HeadPose(core.Ring.Count, position, rotation, TrackingState.Normal));
            frame.EnsureImage(KeyFrameFormat.YCbCr420, 1280, 720);
            frame.Intrinsics = new float4(1000f, 1000f, 640f, 360f);
            // The same pattern in every frame: equal sharpness.
            for (int y = 0; y < 720; y++)
                for (int x = 0; x < 1280; x++)
                    frame.Plane0[y * 1280 + x] = ((x / 4) + (y / 4)) % 2 == 0 ? (byte)40 : (byte)210;
            for (int i = 0; i < frame.Plane1.Length; i++) frame.Plane1[i] = 128;
        }

        static ObjectPhotoSet Capture(PhotoCaptureCore core, Sink sink)
        {
            core.BeginCapture(Room(), float3.zero, 0f);
            for (int i = 0; i < 100 && core.Busy; i++) core.Tick();
            Assert.That(core.Busy, Is.False);
            return sink.Sets.Count > 0 ? sink.Sets[0] : null;
        }

        internal static PhotoCaptureCore Core(Sink sink)
        {
            var core = new PhotoCaptureCore(new SizeEncoder()) { Sink = sink, Threaded = false };
            core.BeginSweep();
            return core;
        }

        /// <summary>Where a view's camera stood, as the Forge reads it from camToBox: azimuth and elevation about the box centre.</summary>
        static void Stood(in ObjectView view, float boxHeight, out float azimuthDeg, out float elevationDeg)
        {
            float3 eye = math.mul(view.CamToBox, new float4(0f, 0f, 0f, 1f)).xyz - new float3(0f, 0.5f * boxHeight, 0f);
            azimuthDeg = math.degrees(math.atan2(eye.x, eye.z));
            elevationDeg = math.degrees(math.atan2(eye.y, math.length(eye.xz)));
        }

        [Test]
        public void ViewFactor_HalvesTheBackHalfAndTheHighViews()
        {
            Assert.That(PhotoScore.ViewFactor(0f, 0f), Is.EqualTo(1f));
            Assert.That(PhotoScore.ViewFactor(-75f, 20f), Is.EqualTo(1f));
            Assert.That(PhotoScore.ViewFactor(40f, -15f), Is.EqualTo(1f));      // from below is not penalised
            // The side of the object and 30 degrees up are still inside.
            Assert.That(PhotoScore.ViewFactor(90f, 0f), Is.EqualTo(1f));
            Assert.That(PhotoScore.ViewFactor(-90f, 30f), Is.EqualTo(1f));

            Assert.That(PhotoScore.ViewFactor(91f, 0f), Is.EqualTo(0.5f));
            Assert.That(PhotoScore.ViewFactor(-135f, 0f), Is.EqualTo(0.5f));
            Assert.That(PhotoScore.ViewFactor(180f, 10f), Is.EqualTo(0.5f));
            Assert.That(PhotoScore.ViewFactor(0f, 31f), Is.EqualTo(0.5f));
            Assert.That(PhotoScore.ViewFactor(20f, 45f), Is.EqualTo(0.5f));
            // Behind and above is halved once, not twice.
            Assert.That(PhotoScore.ViewFactor(170f, 60f), Is.EqualTo(0.5f));
            Assert.That(PhotoScore.OffViewFactor, Is.EqualTo(0.5f));
        }

        [Test]
        public void Elevation_IsTheAngleAboveTheBoxCentre()
        {
            PhotoBox box = Chair();
            foreach (float elevation in new[] { -15f, 0f, 20f, 30f, 45f, 80f })
            {
                foreach (float azimuth in new[] { 0f, 130f, -60f })
                {
                    float3 position;
                    quaternion rotation;
                    Orbit(in box, azimuth, elevation, 2f, out position, out rotation);
                    Assert.That(PhotoScore.ElevationDeg(position, in box), Is.EqualTo(elevation).Within(1e-3f));
                    Assert.That(PhotoScore.AzimuthDeg(position, in box), Is.EqualTo(azimuth).Within(1e-2f));
                }
            }
            // Straight above, and at the centre itself.
            Assert.That(PhotoScore.ElevationDeg(box.Centre + new float3(0f, 2f, 0f), in box), Is.EqualTo(90f).Within(1e-3f));
            Assert.That(PhotoScore.ElevationDeg(box.Centre, in box), Is.EqualTo(0f));
        }

        [Test]
        public void Evaluate_AViewFromBehind_ScoresHalfOfTheSameViewFromTheFront()
        {
            PhotoBox box = Chair();
            BoxProjection front, back;
            PhotoCamera before = Camera(in box, 0f, 0f, 2f), behind = Camera(in box, 180f, 0f, 2f);
            float frontScore = PhotoScore.Evaluate(in before, in box, 10f, 10f, out front);
            float backScore = PhotoScore.Evaluate(in behind, in box, 10f, 10f, out back);
            // A cube looks the same from both sides: the pictures are of equal quality.
            Assert.That(PhotoScore.Score(in back, 10f, 10f), Is.EqualTo(PhotoScore.Score(in front, 10f, 10f)).Within(1e-4f));
            Assert.That(frontScore, Is.EqualTo(PhotoScore.Score(in front, 10f, 10f)));
            Assert.That(backScore, Is.EqualTo(0.5f * PhotoScore.Score(in back, 10f, 10f)).Within(1e-6f));
            Assert.That(backScore, Is.GreaterThan(0f));

            // A camera exactly at the side of the box is not behind it.
            BoxProjection side;
            PhotoCamera beside = Camera(in box, 90f, 0f, 2f);
            Assert.That(PhotoScore.Evaluate(in beside, in box, 10f, 10f, out side), Is.EqualTo(PhotoScore.Score(in side, 10f, 10f)));
            beside = Camera(in box, -90f, 0f, 2f);
            Assert.That(PhotoScore.Evaluate(in beside, in box, 10f, 10f, out side), Is.EqualTo(PhotoScore.Score(in side, 10f, 10f)));
        }

        [Test]
        public void Evaluate_AViewFromFortyFiveDegreesAbove_LosesToOneFromTwentyDegreesAbove()
        {
            PhotoBox box = Chair();
            BoxProjection high, low;
            PhotoCamera above = Camera(in box, 0f, 45f, 2.4f), level = Camera(in box, 0f, 20f, 2.4f);
            float highScore = PhotoScore.Evaluate(in above, in box, 10f, 10f, out high);
            float lowScore = PhotoScore.Evaluate(in level, in box, 10f, 10f, out low);
            // As a picture the high one is no worse: it shows more of the box, as well centred and as sharp.
            Assert.That(PhotoScore.Score(in high, 10f, 10f), Is.GreaterThanOrEqualTo(PhotoScore.Score(in low, 10f, 10f)));
            Assert.That(highScore, Is.GreaterThan(0f));
            Assert.That(highScore, Is.EqualTo(0.5f * PhotoScore.Score(in high, 10f, 10f)).Within(1e-6f));
            Assert.That(lowScore, Is.EqualTo(PhotoScore.Score(in low, 10f, 10f)));
            Assert.That(lowScore, Is.GreaterThan(highScore));

            var picked = new int[PhotoScore.MaxViews];
            Assert.That(PhotoScore.Select(new[] { highScore, lowScore }, new[] { 0f, 0f }, 2, picked), Is.EqualTo(1));
            Assert.That(picked[0], Is.EqualTo(1));
        }

        [Test]
        public void Capture_OfAFrontAndABackView_TheFrontOneIsViewZero()
        {
            var sink = new Sink();
            PhotoCaptureCore core = Core(sink);
            PhotoBox box = Chair();
            AddFrame(core, in box, 180f, 0f, 2f);       // the back view comes first in the ring
            AddFrame(core, in box, 0f, 0f, 2f);
            ObjectPhotoSet set = Capture(core, sink);
            Assert.That(set, Is.Not.Null);
            // The back view is far enough round to be a second view, at half the score.
            Assert.That(set.Views.Length, Is.EqualTo(2));
            Assert.That(set.Views[0].AzimuthDeg, Is.EqualTo(0f).Within(0.05f));
            Assert.That(math.abs(set.Views[1].AzimuthDeg), Is.EqualTo(180f).Within(0.05f));
            Assert.That(set.Views[1].Score, Is.EqualTo(0.5f * set.Views[0].Score).Within(1e-3f));
            Assert.That(set.Views[0].Score, Is.GreaterThan(0.8f));
        }

        [Test]
        public void Capture_OfAHighAndALowerView_TakesTheLowerOne()
        {
            var sink = new Sink();
            PhotoCaptureCore core = Core(sink);
            PhotoBox box = Chair();
            AddFrame(core, in box, 0f, 45f, 2.4f);
            AddFrame(core, in box, 0f, 20f, 2.4f);
            ObjectPhotoSet set = Capture(core, sink);
            Assert.That(set, Is.Not.Null);
            // Both look from the same side, so one view is taken: the one from 20 degrees above.
            Assert.That(set.Views.Length, Is.EqualTo(1));
            float azimuth, elevation;
            Stood(in set.Views[0], set.Size.y, out azimuth, out elevation);
            Assert.That(elevation, Is.EqualTo(20f).Within(0.05f));
            Assert.That(azimuth, Is.EqualTo(0f).Within(0.05f));
        }

        [Test]
        public void Capture_WithOnlyABackView_StillTakesIt()
        {
            var sink = new Sink();
            PhotoCaptureCore core = Core(sink);
            PhotoBox box = Chair();
            AddFrame(core, in box, 160f, 0f, 2f);
            ObjectPhotoSet set = Capture(core, sink);
            Assert.That(sink.NoPhoto, Is.Empty);
            Assert.That(set, Is.Not.Null);
            Assert.That(set.Views.Length, Is.EqualTo(1));
            Assert.That(set.Views[0].AzimuthDeg, Is.EqualTo(160f).Within(0.05f));
            Assert.That(set.Views[0].Score, Is.GreaterThan(0f).And.LessThanOrEqualTo(0.5f));

            // The same for a single view from high above.
            sink = new Sink();
            core = Core(sink);
            AddFrame(core, in box, 30f, 50f, 2.4f);
            set = Capture(core, sink);
            Assert.That(sink.NoPhoto, Is.Empty);
            Assert.That(set, Is.Not.Null);
            Assert.That(set.Views.Length, Is.EqualTo(1));
            Assert.That(set.Views[0].Score, Is.GreaterThan(0f).And.LessThanOrEqualTo(0.5f));
        }

        [Test]
        public void Capture_KeepsTheSpreadBetweenViews_AndFillsUpFromBehind()
        {
            // Front views crowd within 30 degrees of each other; the views that add an angle stand behind.
            var sink = new Sink();
            PhotoCaptureCore core = Core(sink);
            PhotoBox box = Chair();
            AddFrame(core, in box, 0f, 0f, 2f);
            AddFrame(core, in box, 12f, 0f, 2f);
            AddFrame(core, in box, -14f, 0f, 2f);
            AddFrame(core, in box, 120f, 0f, 2f);
            AddFrame(core, in box, -150f, 0f, 2f);
            ObjectPhotoSet set = Capture(core, sink);
            Assert.That(set.Views.Length, Is.EqualTo(3));
            Assert.That(math.abs(set.Views[0].AzimuthDeg), Is.LessThan(15f));
            Assert.That(math.abs(set.Views[1].AzimuthDeg), Is.GreaterThan(90f));
            Assert.That(math.abs(set.Views[2].AzimuthDeg), Is.GreaterThan(90f));
            for (int a = 0; a < 3; a++)
                for (int b = a + 1; b < 3; b++)
                    Assert.That(PhotoScore.AzimuthGapDeg(set.Views[a].AzimuthDeg, set.Views[b].AzimuthDeg), Is.GreaterThanOrEqualTo(PhotoScore.MinViewSpreadDeg));
            Assert.That(set.Views[1].Score, Is.LessThan(set.Views[0].Score));
        }
    }
}
