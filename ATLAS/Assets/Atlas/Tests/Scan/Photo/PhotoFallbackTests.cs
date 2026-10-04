using System.Collections.Generic;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Photo
{
    /// <summary>
    /// The partial-view tiers of object photo capture and the counts of why views are refused: furniture seen
    /// by a phone held at head height rarely fits the image, and the strict test alone gave no photo at all.
    /// </summary>
    public class PhotoFallbackTests
    {
        sealed class Encoder : IPhotoEncoder
        {
            public bool Fail;
            public bool RowsBottomUp { get { return false; } }
            public byte[] EncodeJpeg(byte[] rgba, int width, int height, int quality) { return Fail ? null : new byte[1000]; }
        }

        sealed class Sink : IObjectPhotoSink
        {
            public readonly List<string> Calls = new List<string>();
            public readonly List<ObjectPhotoSet> Sets = new List<ObjectPhotoSet>();
            public void BeginRoom(RoomModel room, string roomJson) { Calls.Add("begin"); }
            public void Submit(ObjectPhotoSet photos) { Calls.Add("submit " + photos.ObjectId); Sets.Add(photos); }
            public void ReportNoPhoto(int objectId) { Calls.Add("none " + objectId); }
            public void EndRoom() { Calls.Add("end"); }
        }

        const int ObjectId = 7;
        const int Strict = 0, Partial = 1;

        static readonly float3 Eye = new float3(0f, 1.3f, 0f);
        static readonly PhotoBox Cube = new PhotoBox { Base = new float3(0f, 0.9f, 2f), Size = new float3(0.8f, 0.8f, 0.8f), Yaw = 0f };

        static PhotoCamera Level(float3 position)
        {
            return PhotoScoreTests.Camera(position, quaternion.identity);
        }

        static PhotoBox Sofa()
        {
            return new PhotoBox { Base = new float3(0f, 0f, 1.8f), Size = new float3(2f, 0.85f, 0.9f), Yaw = math.PI };
        }

        // The phone looks down by this many degrees.
        static quaternion Pitched(float degrees)
        {
            return quaternion.RotateX(math.radians(degrees));
        }

        static RoomModel RoomOf(PhotoBox box)
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            room.Objects = new[]
            {
                new RoomObject { Id = ObjectId, Category = ObjectCategory.Couch, Base = box.Base, Size = box.Size, Yaw = box.Yaw, TriTarget = 3000 }
            };
            return room;
        }

        static KeyFrame AddFrame(PhotoCaptureCore core, float3 position, quaternion rotation, int depthMillimetres = -1)
        {
            KeyFrame frame = core.Ring.Add(new HeadPose(core.Ring.Count, position, rotation, TrackingState.Normal));
            frame.EnsureImage(KeyFrameFormat.YCbCr420, 1280, 720);
            frame.Intrinsics = new float4(1000f, 1000f, 640f, 360f);
            for (int y = 0; y < 720; y++)
                for (int x = 0; x < 1280; x++)
                    frame.Plane0[y * 1280 + x] = ((x / 4) + (y / 4)) % 2 == 0 ? (byte)40 : (byte)210;
            for (int i = 0; i < frame.Plane1.Length; i++) frame.Plane1[i] = 128;
            if (depthMillimetres >= 0)
            {
                frame.EnsureDepth(256, 192);
                for (int i = 0; i < frame.Depth.Length; i++) frame.Depth[i] = (ushort)depthMillimetres;
            }
            return frame;
        }

        // A camera that looks at the cube from the front at azimuthDeg, then turns right or left so that the
        // cube is cut by the edge of the image: a partial view.
        static void PartialPose(float azimuthDeg, out float3 position, out quaternion rotation)
        {
            PhotoBox cube = Cube;
            PhotoScoreTests.Orbit(in cube, azimuthDeg, 2f, out position, out rotation);
            rotation = math.mul(rotation, quaternion.RotateY(math.radians(28f)));
        }

        static void StrictPose(float azimuthDeg, out float3 position, out quaternion rotation)
        {
            PhotoBox cube = Cube;
            PhotoScoreTests.Orbit(in cube, azimuthDeg, 2f, out position, out rotation);
        }

        static PhotoCaptureCore Run(Encoder encoder, Sink sink, RoomModel room, System.Action<PhotoCaptureCore> frames)
        {
            var core = new PhotoCaptureCore(encoder) { Sink = sink, Threaded = false };
            core.BeginSweep();
            frames(core);
            core.BeginCapture(room, float3.zero, 0f);
            for (int i = 0; i < 100 && core.Busy; i++) core.Tick();
            Assert.That(core.Busy, Is.False);
            return core;
        }

        static void AddPartial(PhotoCaptureCore core, float azimuthDeg, int depthMillimetres = -1)
        {
            float3 p;
            quaternion r;
            PartialPose(azimuthDeg, out p, out r);
            AddFrame(core, p, r, depthMillimetres);
        }

        static void AddStrict(PhotoCaptureCore core, float azimuthDeg, int depthMillimetres = -1)
        {
            float3 p;
            quaternion r;
            StrictPose(azimuthDeg, out p, out r);
            AddFrame(core, p, r, depthMillimetres);
        }

        [Test]
        public void Check_NamesTheFirstReasonAViewFails()
        {
            BoxProjection p;
            PhotoBox behind = new PhotoBox { Base = new float3(0f, 0.9f, -3f), Size = new float3(0.8f, 0.8f, 0.8f) };
            PhotoBox outside = new PhotoBox { Base = new float3(5f, 0.9f, 3f), Size = new float3(1f, 1f, 1f) };
            PhotoBox small = new PhotoBox { Base = new float3(0f, 1.1f, 3f), Size = new float3(0.4f, 0.4f, 0.4f) };
            PhotoBox large = new PhotoBox { Base = new float3(0f, 0.7f, 2f), Size = new float3(2.2f, 1.2f, 0.01f) };
            PhotoBox near = new PhotoBox { Base = new float3(0f, 1.2f, 0.5f), Size = new float3(0.2f, 0.2f, 0.2f) };
            PhotoBox far = new PhotoBox { Base = new float3(0f, 0.55f, 4.5f), Size = new float3(1.5f, 1.5f, 1.5f) };
            PhotoBox good = new PhotoBox { Base = new float3(0f, 0.9f, 2.5f), Size = new float3(0.8f, 0.8f, 0.8f) };
            PhotoCamera camera = Level(Eye);
            Assert.That(PhotoScore.Check(in camera, in behind, out p), Is.EqualTo(ViewRejection.BehindCamera));
            Assert.That(PhotoScore.Check(in camera, in outside, out p), Is.EqualTo(ViewRejection.OutsideImage));
            Assert.That(PhotoScore.Check(in camera, in small, out p), Is.EqualTo(ViewRejection.TooSmall));
            Assert.That(PhotoScore.Check(in camera, in large, out p), Is.EqualTo(ViewRejection.TooLarge));
            Assert.That(PhotoScore.Check(in camera, in near, out p), Is.EqualTo(ViewRejection.TooNear));
            Assert.That(PhotoScore.Check(in camera, in far, out p), Is.EqualTo(ViewRejection.TooFar));
            Assert.That(PhotoScore.Check(in camera, in good, out p), Is.EqualTo(ViewRejection.None));
            Assert.That(PhotoScore.IsCandidate(in camera, in p), Is.True);

            // An unusable camera has no projection either.
            camera.Width = 0;
            Assert.That(PhotoScore.Check(in camera, in good, out p), Is.EqualTo(ViewRejection.BehindCamera));
        }

        [Test]
        public void Sofa_SeenFromHeadHeight_IsOnlyAPartialView()
        {
            PhotoBox sofa = Sofa();
            var camera = PhotoScoreTests.Camera(Eye, Pitched(20f));
            BoxProjection p;
            Assert.That(PhotoScore.Check(in camera, in sofa, out p), Is.EqualTo(ViewRejection.OutsideImage));
            float strict = PhotoScore.Evaluate(in camera, in sofa, 1f, 1f, out p);
            float partial = PhotoScore.EvaluatePartial(in camera, in sofa, 1f, 1f, out p);
            Assert.That(strict, Is.EqualTo(0f));
            Assert.That(partial, Is.GreaterThan(0f));
            Assert.That(partial, Is.LessThanOrEqualTo(PhotoScore.RelaxedFactor));
            Assert.That(PhotoScore.VisibleShare(in camera, in p), Is.InRange(PhotoScore.MinVisibleShare, 1f));
            float2 lo, hi;
            PhotoScore.Clip(in camera, in p, out lo, out hi);
            Assert.That(lo.x, Is.GreaterThanOrEqualTo(0f));
            Assert.That(hi.x, Is.LessThanOrEqualTo(1280f));
            Assert.That(hi.y, Is.LessThanOrEqualTo(720f));
        }

        [Test]
        public void Capture_OfOnlyPartialViews_StillGivesAPhoto()
        {
            PhotoBox sofa = Sofa();
            var sink = new Sink();
            PhotoCaptureCore core = Run(new Encoder(), sink, RoomOf(sofa), c => AddFrame(c, Eye, Pitched(20f)));
            Assert.That(sink.Calls, Is.EqualTo(new[] { "begin", "submit " + ObjectId, "end" }));
            Assert.That(sink.Sets[0].Views.Length, Is.EqualTo(1));
            Assert.That(core.PartialViewObjects, Is.EqualTo(1));
            Assert.That(core.DepthIgnoredObjects, Is.EqualTo(0));
            Assert.That(core.RejectionCount(ViewRejection.OutsideImage), Is.GreaterThan(0));
            Assert.That(core.RejectionCount(ViewRejection.None), Is.EqualTo(0));
            Assert.That(core.ViewsChecked, Is.EqualTo(1));
            Assert.That(core.Submitted, Is.EqualTo(1));
        }

        [Test]
        public void Capture_WithAStrictFrame_DoesNotRunThePartialTier()
        {
            // The partial frames would be accepted by the partial tier, were it run.
            PhotoBox cube = Cube;
            float3 p;
            quaternion r;
            PartialPose(20f, out p, out r);
            var partialCamera = PhotoScoreTests.Camera(p, r);
            BoxProjection projection;
            Assert.That(PhotoScore.Check(in partialCamera, in cube, out projection), Is.EqualTo(ViewRejection.OutsideImage));
            Assert.That(PhotoScore.EvaluatePartial(in partialCamera, in cube, 1f, 1f, out projection), Is.GreaterThan(0f));
            var sink = new Sink();
            PhotoCaptureCore core = Run(new Encoder(), sink, RoomOf(Cube), c =>
            {
                AddStrict(c, 0f);
                AddPartial(c, 20f);
                AddPartial(c, -20f);
                AddPartial(c, 30f);
            });
            Assert.That(sink.Sets.Count, Is.EqualTo(1));
            Assert.That(sink.Sets[0].Views.Length, Is.EqualTo(1));
            Assert.That(sink.Sets[0].Views[0].AzimuthDeg, Is.EqualTo(0f).Within(0.05f));
            Assert.That(core.PartialViewObjects, Is.EqualTo(0));
            Assert.That(core.RejectionCount(ViewRejection.None), Is.EqualTo(1));
            Assert.That(core.ViewsChecked, Is.EqualTo(4));
        }

        [Test]
        public void Capture_OfABoxOutOfSightOrBehind_GivesNoPhotoInAnyTier()
        {
            // Turned well away: the box centre is outside the image.
            var away = new Sink();
            PhotoCaptureCore core = Run(new Encoder(), away, RoomOf(Cube), c =>
            {
                float3 p;
                quaternion r;
                StrictPose(0f, out p, out r);
                AddFrame(c, p, math.mul(r, quaternion.RotateY(math.radians(50f))));
            });
            Assert.That(away.Calls, Is.EqualTo(new[] { "begin", "none " + ObjectId, "end" }));
            Assert.That(core.PartialViewObjects + core.DepthIgnoredObjects, Is.EqualTo(0));
            Assert.That(core.RejectionCount(ViewRejection.OutsideImage), Is.EqualTo(1));

            // Facing the other way: the box is behind the camera.
            var behind = new Sink();
            core = Run(new Encoder(), behind, RoomOf(Cube), c =>
            {
                float3 p;
                quaternion r;
                StrictPose(0f, out p, out r);
                AddFrame(c, p, math.mul(r, quaternion.RotateY(math.PI)));
            });
            Assert.That(behind.Calls, Is.EqualTo(new[] { "begin", "none " + ObjectId, "end" }));
            Assert.That(core.RejectionCount(ViewRejection.BehindCamera), Is.EqualTo(1));
        }

        [Test]
        public void Capture_OfOccludedViews_TakesThePhotoWithDepthLeftOut()
        {
            // The depth image says 1.0 m everywhere: the box face (1.6 m) is hidden in every frame.
            var sink = new Sink();
            PhotoCaptureCore core = Run(new Encoder(), sink, RoomOf(Cube), c =>
            {
                AddStrict(c, 0f, 1000);
                AddPartial(c, 20f, 1000);
            });
            Assert.That(sink.Calls, Is.EqualTo(new[] { "begin", "submit " + ObjectId, "end" }));
            Assert.That(core.DepthIgnoredObjects, Is.EqualTo(1));
            Assert.That(core.PartialViewObjects, Is.EqualTo(0));
            Assert.That(core.RejectionCount(ViewRejection.Occluded), Is.EqualTo(1));
        }

        [Test]
        public void Capture_PartialViewWhoseDepthAgrees_IsTakenInTheSecondTier()
        {
            float3 p;
            quaternion r;
            PartialPose(20f, out p, out r);
            PhotoBox cube = Cube;
            var camera = PhotoScoreTests.Camera(p, r);
            BoxProjection projection;
            Assert.That(PhotoScore.Project(in camera, in cube, out projection), Is.True);
            int face = (int)math.round(projection.FaceDepth * 1000f);

            var sink = new Sink();
            PhotoCaptureCore core = Run(new Encoder(), sink, RoomOf(Cube), c =>
            {
                AddStrict(c, 0f, 1000);         // hidden
                AddFrame(c, p, r, face);        // the depth agrees with the face
            });
            Assert.That(sink.Sets.Count, Is.EqualTo(1));
            Assert.That(sink.Sets[0].Views.Length, Is.EqualTo(1));
            Assert.That(sink.Sets[0].Views[0].AzimuthDeg, Is.EqualTo(20f).Within(0.05f));
            Assert.That(core.PartialViewObjects, Is.EqualTo(1));
            Assert.That(core.DepthIgnoredObjects, Is.EqualTo(0));
        }

        [Test]
        public void Capture_WithAnEncoderThatReturnsNothing_CountsTheFailures()
        {
            var sink = new Sink();
            PhotoCaptureCore core = Run(new Encoder { Fail = true }, sink, RoomOf(Cube), c => AddStrict(c, 0f));
            Assert.That(sink.Calls, Is.EqualTo(new[] { "begin", "none " + ObjectId, "end" }));
            Assert.That(core.EncodeFailures, Is.GreaterThan(0));
            Assert.That(core.Errors, Is.EqualTo(0));
            Assert.That(core.FirstError, Does.StartWith("JPEG encoder returned nothing ("));
        }

        [Test]
        public void Counts_StartAgainAtTheNextCapture()
        {
            var core = new PhotoCaptureCore(new Encoder { Fail = true }) { Sink = new Sink(), Threaded = false };
            core.BeginSweep();
            AddStrict(core, 0f);
            core.BeginCapture(RoomOf(Cube), float3.zero, 0f);
            for (int i = 0; i < 100 && core.Busy; i++) core.Tick();
            Assert.That(core.ViewsChecked, Is.EqualTo(1));
            Assert.That(core.EncodeFailures, Is.GreaterThan(0));
            Assert.That(core.RejectionCount(ViewRejection.None), Is.EqualTo(1));

            core.BeginSweep();
            core.BeginCapture(RoomOf(Cube), float3.zero, 0f);
            Assert.That(core.ViewsChecked, Is.EqualTo(0));
            Assert.That(core.EncodeFailures, Is.EqualTo(0));
            Assert.That(core.PartialViewObjects, Is.EqualTo(0));
            Assert.That(core.DepthIgnoredObjects, Is.EqualTo(0));
            Assert.That(core.FirstError, Is.Null);
            for (int kind = 0; kind < PhotoScore.RejectionKinds; kind++) Assert.That(core.RejectionCount((ViewRejection)kind), Is.EqualTo(0));
        }
    }
}
