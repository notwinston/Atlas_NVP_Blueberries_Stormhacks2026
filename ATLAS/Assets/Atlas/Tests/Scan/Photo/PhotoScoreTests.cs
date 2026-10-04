using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Photo
{
    /// <summary>The scoring core of object photo capture (contract lane C sections 11.2 and 11.3).</summary>
    public class PhotoScoreTests
    {
        internal static PhotoCamera Camera(float3 position, quaternion rotation)
        {
            return new PhotoCamera { Position = position, Rotation = rotation, Intrinsics = new float4(1000f, 1000f, 640f, 360f), Width = 1280, Height = 720 };
        }

        /// <summary>A camera at an azimuth about a box (in the box's frame for yaw 0), level with its centre, looking at it.</summary>
        internal static void Orbit(in PhotoBox box, float azimuthDeg, float distance, out float3 position, out quaternion rotation)
        {
            float a = math.radians(azimuthDeg);
            var away = new float3(math.sin(a), 0f, math.cos(a));
            position = box.Centre + distance * away;
            rotation = quaternion.LookRotationSafe(-away, new float3(0f, 1f, 0f));
        }

        static PhotoBox Cube(float side)
        {
            return new PhotoBox { Base = float3.zero, Size = new float3(side, side, side), Yaw = 0f };
        }

        static PhotoCamera Facing(in PhotoBox box, float distance)
        {
            float3 p;
            quaternion r;
            Orbit(in box, 180f, distance, out p, out r);
            return Camera(p, r);
        }

        [Test]
        public void Project_AnAxisAlignedBox()
        {
            var box = new PhotoBox { Base = float3.zero, Size = new float3(2f, 1f, 0.5f), Yaw = 0f };
            PhotoCamera camera = Camera(new float3(0f, 0.5f, -4f), quaternion.identity);
            BoxProjection p;
            Assert.That(PhotoScore.Project(in camera, in box, out p), Is.True);
            // The near face is 3.75 m away and 2 m wide, 1 m tall.
            Assert.That(p.Min.x, Is.EqualTo(640f - 266.667f).Within(0.01f));
            Assert.That(p.Max.x, Is.EqualTo(640f + 266.667f).Within(0.01f));
            Assert.That(p.Min.y, Is.EqualTo(360f - 133.333f).Within(0.01f));
            Assert.That(p.Max.y, Is.EqualTo(360f + 133.333f).Within(0.01f));
            Assert.That(p.Centre.x, Is.EqualTo(640f).Within(1e-3f));
            Assert.That(p.Centre.y, Is.EqualTo(360f).Within(1e-3f));
            Assert.That(p.Distance, Is.EqualTo(4f).Within(1e-4f));
            Assert.That(p.FaceDepth, Is.EqualTo(3.75f).Within(1e-4f));
            Assert.That(p.Centredness, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Project_AnOrientedBox_TurnsWithItsYaw()
        {
            // Yaw 90 degrees carries the 2 m side onto the view axis: the near face is 3 m away and 0.5 m wide.
            var box = new PhotoBox { Base = float3.zero, Size = new float3(2f, 1f, 0.5f), Yaw = math.PI / 2f };
            PhotoCamera camera = Camera(new float3(0f, 0.5f, -4f), quaternion.identity);
            BoxProjection p;
            Assert.That(PhotoScore.Project(in camera, in box, out p), Is.True);
            Assert.That(p.Min.x, Is.EqualTo(640f - 83.333f).Within(0.01f));
            Assert.That(p.Max.x, Is.EqualTo(640f + 83.333f).Within(0.01f));
            Assert.That(p.Min.y, Is.EqualTo(360f - 166.667f).Within(0.01f));
            Assert.That(p.Max.y, Is.EqualTo(360f + 166.667f).Within(0.01f));
            Assert.That(p.FaceDepth, Is.EqualTo(3f).Within(1e-4f));
            Assert.That(p.Area, Is.EqualTo(166.667f * 333.333f / (1280f * 720f)).Within(1e-4f));
        }

        [Test]
        public void Project_APointUpAndRight_LandsUpAndRightInTheImage()
        {
            PhotoCamera camera = Camera(float3.zero, quaternion.identity);
            float2 pixel = PhotoScore.ToPixel(in camera, new float3(0.5f, 0.25f, 2f));
            Assert.That(pixel.x, Is.EqualTo(890f).Within(1e-3f));
            Assert.That(pixel.y, Is.EqualTo(235f).Within(1e-3f));     // rows count down from the top
        }

        [Test]
        public void Evaluate_ABoxBehindTheCamera_ScoresZero()
        {
            PhotoBox box = Cube(0.8f);
            // Two metres in front of the box, looking away from it.
            PhotoCamera camera = Camera(new float3(0f, 0.4f, -2f), quaternion.RotateY(math.PI));
            BoxProjection p;
            Assert.That(PhotoScore.Project(in camera, in box, out p), Is.False);
            Assert.That(PhotoScore.Evaluate(in camera, in box, 10f, 10f, out p), Is.EqualTo(0f));
        }

        [Test]
        public void Evaluate_ABoxThatStraddlesTheCameraPlane_ScoresZero()
        {
            var box = new PhotoBox { Base = float3.zero, Size = new float3(1f, 1f, 4f), Yaw = 0f };
            PhotoCamera camera = Camera(new float3(0f, 0.5f, -1.5f), quaternion.identity);
            BoxProjection p;
            Assert.That(PhotoScore.Evaluate(in camera, in box, 10f, 10f, out p), Is.EqualTo(0f));
        }

        [Test]
        public void Evaluate_ABoxOutsideTheImage_ScoresZero()
        {
            PhotoBox box = Cube(0.8f);
            // In front of the camera, but turned 40 degrees away: the box leaves the image.
            PhotoCamera camera = Camera(new float3(0f, 0.4f, -2f), quaternion.RotateY(math.radians(40f)));
            BoxProjection p;
            Assert.That(PhotoScore.Project(in camera, in box, out p), Is.True);
            Assert.That(PhotoScore.Evaluate(in camera, in box, 10f, 10f, out p), Is.EqualTo(0f));
        }

        [Test]
        public void Evaluate_ABoxCutByTheMargin_ScoresZero()
        {
            PhotoBox box = Cube(0.8f);
            // Turned just enough that the box's edge enters the 5 % margin while staying inside the image.
            PhotoCamera camera = Camera(new float3(0f, 0.4f, -2f), quaternion.RotateY(math.radians(16f)));
            BoxProjection p;
            PhotoScore.Project(in camera, in box, out p);
            Assert.That(p.Min.x, Is.GreaterThan(0f));
            Assert.That(p.Max.x, Is.LessThan(1280f));
            Assert.That(p.Min.x < 64f || p.Max.x > 1216f, Is.True);
            Assert.That(PhotoScore.Evaluate(in camera, in box, 10f, 10f, out p), Is.EqualTo(0f));
        }

        [TestCase(0.5f)]     // too near: the centre is under 0.6 m away
        [TestCase(3.4f)]     // too far
        public void Evaluate_OutsideTheDistanceRange_ScoresZero(float distance)
        {
            var box = new PhotoBox { Base = float3.zero, Size = new float3(0.1f, 0.1f, 0.1f) * (distance < 1f ? 1f : 12f), Yaw = 0f };
            PhotoCamera camera = Facing(in box, distance);
            BoxProjection p;
            Assert.That(PhotoScore.Evaluate(in camera, in box, 10f, 10f, out p), Is.EqualTo(0f));
        }

        [Test]
        public void Evaluate_TooSmallOrTooLargeInTheImage_ScoresZero()
        {
            BoxProjection p;
            PhotoBox small = Cube(0.3f);
            PhotoCamera far = Facing(in small, 2.5f);
            Assert.That(PhotoScore.Evaluate(in far, in small, 10f, 10f, out p), Is.EqualTo(0f));
            Assert.That(p.Area, Is.LessThan(PhotoScore.MinArea));

            var wide = new PhotoBox { Base = float3.zero, Size = new float3(1.6f, 0.9f, 0.2f), Yaw = 0f };
            PhotoCamera near = Facing(in wide, 1.55f);
            PhotoScore.Project(in near, in wide, out p);
            Assert.That(p.Area, Is.GreaterThan(PhotoScore.MaxArea));
            Assert.That(PhotoScore.Evaluate(in near, in wide, 10f, 10f, out p), Is.EqualTo(0f));
        }

        [Test]
        public void Evaluate_ACentredLargeSharpView_BeatsTheOthers()
        {
            PhotoBox box = Cube(0.8f);
            BoxProjection p;
            PhotoCamera good = Facing(in box, 2f);
            float best = PhotoScore.Evaluate(in good, in box, 10f, 10f, out p);
            Assert.That(best, Is.GreaterThan(0.8f));
            Assert.That(best, Is.LessThanOrEqualTo(1f));

            float blurred = PhotoScore.Evaluate(in good, in box, 2f, 10f, out p);
            Assert.That(blurred, Is.EqualTo(best - 0.3f * 0.8f).Within(1e-4f));

            PhotoCamera farther = Facing(in box, 2.9f);
            float small = PhotoScore.Evaluate(in farther, in box, 10f, 10f, out p);
            Assert.That(small, Is.GreaterThan(0f));
            Assert.That(small, Is.LessThan(best));

            PhotoCamera aside = Camera(good.Position, math.mul(good.Rotation, quaternion.RotateY(math.radians(8f))));
            float offCentre = PhotoScore.Evaluate(in aside, in box, 10f, 10f, out p);
            Assert.That(offCentre, Is.GreaterThan(0f));
            Assert.That(offCentre, Is.LessThan(best));
        }

        [Test]
        public void Score_IsTheWeightedSum()
        {
            var p = new BoxProjection { Area = 0.175f, Centredness = 0.5f };
            Assert.That(PhotoScore.Score(in p, 5f, 10f), Is.EqualTo(0.4f * 0.5f + 0.3f * 0.5f + 0.3f * 0.5f).Within(1e-6f));
            // Area and sharpness saturate.
            p.Area = 0.6f;
            p.Centredness = 1f;
            Assert.That(PhotoScore.Score(in p, 50f, 10f), Is.EqualTo(1f).Within(1e-6f));
            // A ring of flat images has no median to compare with.
            Assert.That(PhotoScore.Score(in p, 0f, 0f), Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void Occlusion_NeedsAReturnThatDisagrees()
        {
            Assert.That(PhotoScore.IsOccluded(1.0f, 1.6f), Is.True);       // something in front
            Assert.That(PhotoScore.IsOccluded(2.4f, 1.6f), Is.True);       // the box is not there
            Assert.That(PhotoScore.IsOccluded(1.7f, 1.6f), Is.False);
            Assert.That(PhotoScore.IsOccluded(0f, 1.6f), Is.False);        // no depth: cannot tell
        }

        [Test]
        public void Azimuth_IsMeasuredInTheBoxFrame()
        {
            PhotoBox box = Cube(1f);
            Assert.That(PhotoScore.AzimuthDeg(new float3(0f, 0.5f, 2f), in box), Is.EqualTo(0f).Within(1e-3f));
            Assert.That(PhotoScore.AzimuthDeg(new float3(2f, 0.5f, 0f), in box), Is.EqualTo(90f).Within(1e-3f));
            Assert.That(PhotoScore.AzimuthDeg(new float3(-2f, 3f, 0f), in box), Is.EqualTo(-90f).Within(1e-3f));
            // Yaw 90 degrees carries the box's +X onto the room's -Z.
            box.Yaw = math.PI / 2f;
            Assert.That(PhotoScore.AzimuthDeg(new float3(0f, 0.5f, -2f), in box), Is.EqualTo(90f).Within(1e-3f));

            Assert.That(PhotoScore.AzimuthGapDeg(170f, -170f), Is.EqualTo(20f).Within(1e-4f));
            Assert.That(PhotoScore.AzimuthGapDeg(-30f, 60f), Is.EqualTo(90f).Within(1e-4f));
        }

        [Test]
        public void Select_TakesTheBestThenSpreadsTheAngle()
        {
            float[] scores = { 0.70f, 0.90f, 0.85f, 0f, 0.60f, 0.80f };
            float[] azimuths = { 100f, 0f, 10f, 50f, 40f, -170f };
            var picked = new int[PhotoScore.MaxViews];
            int n = PhotoScore.Select(scores, azimuths, scores.Length, picked);
            Assert.That(n, Is.EqualTo(3));
            Assert.That(picked[0], Is.EqualTo(1));      // the best
            Assert.That(picked[1], Is.EqualTo(5));      // 0.85 is only 10 degrees away; 0.80 is next
            Assert.That(picked[2], Is.EqualTo(0));      // 100 degrees: clear of 0 and of -170
        }

        [Test]
        public void Select_NeverMoreThanThree()
        {
            var scores = new float[12];
            var azimuths = new float[12];
            for (int i = 0; i < 12; i++)
            {
                scores[i] = 0.5f + 0.01f * i;
                azimuths[i] = -180f + 30f * i + 15f;
            }
            var picked = new int[PhotoScore.MaxViews];
            Assert.That(PhotoScore.Select(scores, azimuths, 12, picked), Is.EqualTo(3));
            Assert.That(picked[0], Is.EqualTo(11));
            for (int a = 0; a < 3; a++)
                for (int b = a + 1; b < 3; b++)
                    Assert.That(PhotoScore.AzimuthGapDeg(azimuths[picked[a]], azimuths[picked[b]]), Is.GreaterThanOrEqualTo(30f));
        }

        [Test]
        public void Select_OneViewWhenAllLookFromTheSameSide_NoneWithoutACandidate()
        {
            var picked = new int[PhotoScore.MaxViews];
            Assert.That(PhotoScore.Select(new[] { 0.5f, 0.6f, 0.7f }, new[] { 0f, 10f, 25f }, 3, picked), Is.EqualTo(1));
            Assert.That(picked[0], Is.EqualTo(2));
            Assert.That(PhotoScore.Select(new[] { 0f, 0f }, new[] { 0f, 90f }, 2, picked), Is.EqualTo(0));
            Assert.That(PhotoScore.Select(new float[0], new float[0], 0, picked), Is.EqualTo(0));
        }

        [Test]
        public void Crop_GrowsAndSquares()
        {
            CropRect crop = PhotoScore.Crop(new float2(500f, 310f), new float2(700f, 410f), 1280, 720);
            // 200 wide grown by 12 % on each side is 248; the square takes the longer side.
            Assert.That(crop.X, Is.EqualTo(476));
            Assert.That(crop.Y, Is.EqualTo(236));
            Assert.That(crop.Width, Is.EqualTo(248));
            Assert.That(crop.Height, Is.EqualTo(248));
        }

        [Test]
        public void Crop_IsClampedToTheImage()
        {
            CropRect crop = PhotoScore.Crop(new float2(10f, 100f), new float2(400f, 300f), 1280, 720);
            Assert.That(crop.X, Is.EqualTo(0));
            Assert.That(crop.Y, Is.EqualTo(0));
            Assert.That(crop.Width, Is.EqualTo(447));
            Assert.That(crop.Height, Is.EqualTo(442));

            crop = PhotoScore.Crop(new float2(900f, 300f), new float2(1270f, 700f), 1280, 720);
            Assert.That(crop.X + crop.Width, Is.EqualTo(1280));
            Assert.That(crop.Y + crop.Height, Is.EqualTo(720));
            Assert.That(crop.X, Is.GreaterThanOrEqualTo(0));

            // Even a box wholly outside gives a rectangle inside the image.
            crop = PhotoScore.Crop(new float2(2000f, 900f), new float2(2100f, 950f), 1280, 720);
            Assert.That(crop.Width, Is.GreaterThanOrEqualTo(1));
            Assert.That(crop.Height, Is.GreaterThanOrEqualTo(1));
            Assert.That(crop.X + crop.Width, Is.LessThanOrEqualTo(1280));
            Assert.That(crop.Y + crop.Height, Is.LessThanOrEqualTo(720));
        }

        [Test]
        public void OutputSize_ScalesDownNeverUp()
        {
            int w, h;
            var small = new CropRect { Width = 248, Height = 200 };
            PhotoScore.OutputSize(in small, out w, out h);
            Assert.That(w, Is.EqualTo(248));
            Assert.That(h, Is.EqualTo(200));

            var large = new CropRect { Width = 1000, Height = 900 };
            PhotoScore.OutputSize(in large, out w, out h);
            Assert.That(w, Is.EqualTo(768));
            Assert.That(h, Is.EqualTo(691));
        }

        [Test]
        public void CropIntrinsics_FollowTheCropAndItsScale()
        {
            var crop = new CropRect { X = 400, Y = 100, Width = 400, Height = 400 };
            float4 k = PhotoScore.CropIntrinsics(new float4(1000f, 1000f, 640f, 360f), in crop, 200, 200);
            Assert.That(k.x, Is.EqualTo(500f).Within(1e-3f));
            Assert.That(k.y, Is.EqualTo(500f).Within(1e-3f));
            Assert.That(k.z, Is.EqualTo(120f).Within(1e-3f));
            Assert.That(k.w, Is.EqualTo(130f).Within(1e-3f));
        }

        [Test]
        public void CamToBox_CarriesCameraPointsIntoTheBoxFrame()
        {
            var box = new PhotoBox { Base = new float3(1f, 0f, 2f), Size = new float3(1f, 1f, 1f), Yaw = math.PI / 2f };
            var cameraPosition = new float3(1f, 0.5f, -1f);
            float4x4 m = PhotoScore.CamToBox(cameraPosition, quaternion.identity, in box);
            // The camera is 3 m along the room's -Z from the base, which is the box's +X.
            float3 origin = math.mul(m, new float4(0f, 0f, 0f, 1f)).xyz;
            Assert.That(math.distance(origin, new float3(3f, 0.5f, 0f)), Is.LessThan(1e-4f));
            // One metre ahead of the camera is one metre nearer the box.
            float3 ahead = math.mul(m, new float4(0f, 0f, 1f, 1f)).xyz;
            Assert.That(math.distance(ahead, new float3(2f, 0.5f, 0f)), Is.LessThan(1e-4f));
        }

        [Test]
        public void ToCaptureFrame_PlacesTheObjectByTheAnchor()
        {
            var obj = new RoomObject { Id = 3, Base = new float3(1f, 0f, 0f), Size = new float3(1f, 2f, 3f), Yaw = 0.25f };
            PhotoBox box = PhotoScore.ToCaptureFrame(obj, new float3(10f, -1.2f, 5f), math.PI / 2f);
            // The room's +X is the session's -Z after a yaw of 90 degrees.
            Assert.That(math.distance(box.Base, new float3(10f, -1.2f, 4f)), Is.LessThan(1e-4f));
            Assert.That(box.Yaw, Is.EqualTo(0.25f + math.PI / 2f).Within(1e-5f));
            Assert.That(box.Size, Is.EqualTo(obj.Size));
        }

        [Test]
        public void Median_OfOddAndEvenCounts()
        {
            var scratch = new float[8];
            Assert.That(PhotoScore.Median(new[] { 5f, 1f, 3f }, 3, scratch), Is.EqualTo(3f));
            Assert.That(PhotoScore.Median(new[] { 5f, 1f, 3f, 9f }, 4, scratch), Is.EqualTo(4f));
            Assert.That(PhotoScore.Median(new float[0], 0, scratch), Is.EqualTo(0f));
        }
    }
}
