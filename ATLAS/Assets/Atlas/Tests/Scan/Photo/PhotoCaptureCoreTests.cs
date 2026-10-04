using System.Collections.Generic;
using System.Threading;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Photo
{
    /// <summary>From the key frames of a sweep to the photo sink (contract lane C sections 11.2 to 11.4).</summary>
    public class PhotoCaptureCoreTests
    {
        sealed class FakeEncoder : IPhotoEncoder
        {
            public readonly List<int> Qualities = new List<int>();
            /// <summary>Qualities above this give a JPEG over the size limit.</summary>
            public int FitsAtQuality = 100;
            public bool Fail;
            public int LastWidth, LastHeight, LastBytes;

            public bool RowsBottomUp { get { return false; } }

            public byte[] EncodeJpeg(byte[] rgba, int width, int height, int quality)
            {
                lock (Qualities) Qualities.Add(quality);
                LastWidth = width;
                LastHeight = height;
                LastBytes = rgba.Length;
                if (Fail) return null;
                return new byte[quality > FitsAtQuality ? PhotoCaptureCore.MaxJpegBytes + 1 : 1000 + quality];
            }
        }

        sealed class RecordingSink : IObjectPhotoSink
        {
            public readonly List<string> Calls = new List<string>();
            public readonly List<ObjectPhotoSet> Sets = new List<ObjectPhotoSet>();
            public string RoomJson;

            public void BeginRoom(RoomModel room, string roomJson) { Calls.Add("begin"); RoomJson = roomJson; }
            public void Submit(ObjectPhotoSet photos) { Calls.Add("submit " + photos.ObjectId); Sets.Add(photos); }
            public void ReportNoPhoto(int objectId) { Calls.Add("none " + objectId); }
            public void EndRoom() { Calls.Add("end"); }
        }

        const int ChairId = 1, CouchId = 2, PlantId = 3, ShelfId = 4;

        // The chair is photographed; the couch (larger) stands where no camera looks; the plant gets no mesh.
        static RoomModel Room()
        {
            return WithObjects(new[]
                {
                    new RoomObject { Id = ChairId, Category = ObjectCategory.Chair, Base = float3.zero, Size = new float3(0.8f, 0.8f, 0.8f), Yaw = 0f, TriTarget = 3000 },
                    new RoomObject { Id = CouchId, Category = ObjectCategory.Couch, Base = new float3(0f, 0f, 40f), Size = new float3(2f, 0.9f, 1f), Yaw = 0.3f, TriTarget = 5000 },
                    new RoomObject { Id = PlantId, Category = ObjectCategory.Other, Base = new float3(0f, 0f, 0f), Size = new float3(0.8f, 0.8f, 0.8f), Yaw = 0f, TriTarget = 0 }
                });
        }

        // A complete room (the sink is handed its JSON) whose furniture is replaced.
        static RoomModel WithObjects(RoomObject[] objects)
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            room.Objects = objects;
            return room;
        }

        static PhotoBox ChairBox(float3 origin, float yaw)
        {
            return PhotoScore.ToCaptureFrame(Room().Objects[0], origin, yaw);
        }

        static KeyFrame AddFrame(PhotoCaptureCore core, in PhotoBox box, float azimuthDeg, float distance, int pattern = 4, int depthMillimetres = -1)
        {
            float3 position;
            quaternion rotation;
            PhotoScoreTests.Orbit(in box, azimuthDeg, distance, out position, out rotation);
            KeyFrame frame = core.Ring.Add(new HeadPose(core.Ring.Count, position, rotation, TrackingState.Normal));
            frame.EnsureImage(KeyFrameFormat.YCbCr420, 1280, 720);
            frame.Intrinsics = new float4(1000f, 1000f, 640f, 360f);
            for (int y = 0; y < 720; y++)
                for (int x = 0; x < 1280; x++)
                    frame.Plane0[y * 1280 + x] = ((x / pattern) + (y / pattern)) % 2 == 0 ? (byte)40 : (byte)210;
            for (int i = 0; i < frame.Plane1.Length; i++) frame.Plane1[i] = 128;
            if (depthMillimetres >= 0)
            {
                frame.EnsureDepth(256, 192);
                for (int i = 0; i < frame.Depth.Length; i++) frame.Depth[i] = (ushort)depthMillimetres;
            }
            return frame;
        }

        static PhotoCaptureCore Core(FakeEncoder encoder, RecordingSink sink, int capacity = KeyFrameRing.DefaultCapacity)
        {
            return new PhotoCaptureCore(encoder, capacity) { Sink = sink, Threaded = false };
        }

        static void TickUntilIdle(PhotoCaptureCore core, int limit = 100)
        {
            for (int i = 0; i < limit && core.Busy; i++) core.Tick();
            Assert.That(core.Busy, Is.False);
        }

        [Test]
        public void Capture_SubmitsTheSeenObject_AndReportsTheUnseenOne()
        {
            var encoder = new FakeEncoder();
            var sink = new RecordingSink();
            PhotoCaptureCore core = Core(encoder, sink);
            core.BeginSweep();
            PhotoBox chair = ChairBox(float3.zero, 0f);
            AddFrame(core, in chair, 0f, 2f);
            AddFrame(core, in chair, 10f, 2f);
            AddFrame(core, in chair, 45f, 2f);
            AddFrame(core, in chair, 90f, 2f);
            AddFrame(core, in chair, 180f, 2f);
            Assert.That(core.KeyFrameCount, Is.EqualTo(5));

            RoomModel room = Room();
            core.BeginCapture(room, float3.zero, 0f);
            Assert.That(sink.Calls, Is.EqualTo(new[] { "begin" }));
            Assert.That(sink.RoomJson, Is.EqualTo(RoomModelJson.Write(room)));
            // The key frames now belong to the capture and are gone from the ring.
            Assert.That(core.KeyFrameCount, Is.EqualTo(0));
            Assert.That(core.Busy, Is.True);

            // One object per tick, the larger first.
            core.Tick();
            Assert.That(sink.Calls, Is.EqualTo(new[] { "begin", "none " + CouchId }));
            core.Tick();
            Assert.That(sink.Calls, Is.EqualTo(new[] { "begin", "none " + CouchId, "submit " + ChairId, "end" }));
            Assert.That(core.Busy, Is.False);
            core.Tick();
            Assert.That(sink.Calls.Count, Is.EqualTo(4));
            Assert.That(core.Submitted, Is.EqualTo(1));
            Assert.That(core.NoPhoto, Is.EqualTo(1));
            Assert.That(core.FramesUsed, Is.EqualTo(5));
            Assert.That(core.Errors, Is.EqualTo(0));

            ObjectPhotoSet set = sink.Sets[0];
            Assert.That(set.ObjectId, Is.EqualTo(ChairId));
            Assert.That(set.Category, Is.EqualTo(ObjectCategory.Chair));
            Assert.That(set.TriTarget, Is.EqualTo(3000));
            Assert.That(set.Size, Is.EqualTo(new float3(0.8f, 0.8f, 0.8f)));
            Assert.That(set.Views.Length, Is.EqualTo(3));
            for (int a = 0; a < 3; a++)
            {
                ObjectView view = set.Views[a];
                Assert.That(view.Jpeg.Length, Is.LessThanOrEqualTo(PhotoCaptureCore.MaxJpegBytes));
                Assert.That(view.Width, Is.InRange(1, PhotoScore.MaxCropSide));
                Assert.That(view.Height, Is.InRange(1, PhotoScore.MaxCropSide));
                Assert.That(view.Score, Is.InRange(0.5f, 1f));
                // The camera looks at the box centre, so the principal point sits near the middle of the crop
                // (not on it: a box seen at an angle projects lopsided).
                Assert.That(view.Intrinsics.x, Is.EqualTo(1000f).Within(1e-2f));
                Assert.That(view.Intrinsics.z, Is.EqualTo(0.5f * view.Width).Within(0.1f * view.Width));
                Assert.That(view.Intrinsics.w, Is.EqualTo(0.5f * view.Height).Within(0.1f * view.Height));
                // The camera stands 2 m from the box centre, 0.4 m above its base.
                float3 eye = math.mul(view.CamToBox, new float4(0f, 0f, 0f, 1f)).xyz;
                Assert.That(math.distance(eye, new float3(0f, 0.4f, 0f)), Is.EqualTo(2f).Within(1e-3f));
                for (int b = a + 1; b < 3; b++)
                    Assert.That(PhotoScore.AzimuthGapDeg(view.AzimuthDeg, set.Views[b].AzimuthDeg), Is.GreaterThanOrEqualTo(30f));
            }
            Assert.That(encoder.LastBytes, Is.EqualTo(encoder.LastWidth * encoder.LastHeight * 4));
        }

        [Test]
        public void Capture_UsesTheAnchorToFindTheObjects()
        {
            var sink = new RecordingSink();
            PhotoCaptureCore core = Core(new FakeEncoder(), sink);
            core.BeginSweep();
            var origin = new float3(3f, -1.1f, -2f);
            const float yaw = 0.7f;
            PhotoBox chair = ChairBox(origin, yaw);
            AddFrame(core, in chair, 20f, 1.8f);
            core.BeginCapture(Room(), origin, yaw);
            TickUntilIdle(core);
            Assert.That(sink.Calls, Does.Contain("submit " + ChairId));
            Assert.That(sink.Sets[0].Views.Length, Is.EqualTo(1));
            // The camera stands at 20 degrees in the session frame; the box is turned by the anchor's yaw.
            Assert.That(sink.Sets[0].Views[0].AzimuthDeg, Is.EqualTo(20f - math.degrees(yaw)).Within(0.05f));

            // The same frames with the room placed elsewhere show nothing.
            var miss = new RecordingSink();
            core = Core(new FakeEncoder(), miss);
            core.BeginSweep();
            AddFrame(core, in chair, 20f, 1.8f);
            core.BeginCapture(Room(), float3.zero, 0f);
            TickUntilIdle(core);
            Assert.That(miss.Calls, Is.EqualTo(new[] { "begin", "none " + CouchId, "none " + ChairId, "end" }));
        }

        [Test]
        public void Capture_WithNoKeyFrames_ReportsEveryMeshedObject()
        {
            var sink = new RecordingSink();
            PhotoCaptureCore core = Core(new FakeEncoder(), sink);
            core.BeginSweep();
            core.BeginCapture(Room(), float3.zero, 0f);
            TickUntilIdle(core);
            Assert.That(sink.Calls, Is.EqualTo(new[] { "begin", "none " + CouchId, "none " + ChairId, "end" }));
            Assert.That(core.NoPhoto, Is.EqualTo(2));
        }

        [Test]
        public void Capture_WithNoSweep_DoesNotCallTheSink()
        {
            var sink = new RecordingSink();
            PhotoCaptureCore core = Core(new FakeEncoder(), sink);
            core.BeginCapture(Room(), float3.zero, 0f);       // a room loaded from a file
            Assert.That(core.Busy, Is.False);
            core.Tick();
            Assert.That(sink.Calls, Is.Empty);
        }

        [Test]
        public void Capture_ARoomWithoutMeshedObjects_BeginsAndEnds()
        {
            var sink = new RecordingSink();
            PhotoCaptureCore core = Core(new FakeEncoder(), sink);
            core.BeginSweep();
            core.BeginCapture(WithObjects(new RoomObject[0]), float3.zero, 0f);
            TickUntilIdle(core);
            Assert.That(sink.Calls, Is.EqualTo(new[] { "begin", "end" }));
        }

        [Test]
        public void Capture_WithNoSink_StillRunsAndReleases()
        {
            var core = new PhotoCaptureCore(new FakeEncoder()) { Threaded = false };
            core.BeginSweep();
            PhotoBox chair = ChairBox(float3.zero, 0f);
            AddFrame(core, in chair, 0f, 2f);
            core.BeginCapture(Room(), float3.zero, 0f);
            TickUntilIdle(core);
            Assert.That(core.Submitted, Is.EqualTo(1));
            Assert.That(core.NoPhoto, Is.EqualTo(1));
            Assert.That(core.KeyFrameCount, Is.EqualTo(0));
        }

        [Test]
        public void Capture_AnOccludedView_IsNotACandidate()
        {
            PhotoBox chair = ChairBox(float3.zero, 0f);

            // The depth image says 1.0 m where the chair's face should be 1.6 m away: something is in front.
            var blocked = new RecordingSink();
            PhotoCaptureCore core = Core(new FakeEncoder(), blocked);
            core.BeginSweep();
            AddFrame(core, in chair, 0f, 2f, 4, 1000);
            core.BeginCapture(Room(), float3.zero, 0f);
            TickUntilIdle(core);
            // It is no strict candidate, but with every tier tried the photo is still taken, depth left out.
            Assert.That(core.RejectionCount(ViewRejection.Occluded), Is.EqualTo(1));
            Assert.That(core.DepthIgnoredObjects, Is.EqualTo(1));
            Assert.That(blocked.Calls, Does.Contain("submit " + ChairId));

            var clear = new RecordingSink();
            core = Core(new FakeEncoder(), clear);
            core.BeginSweep();
            AddFrame(core, in chair, 0f, 2f, 4, 1600);
            core.BeginCapture(Room(), float3.zero, 0f);
            TickUntilIdle(core);
            Assert.That(clear.Calls, Does.Contain("submit " + ChairId));
            Assert.That(core.RejectionCount(ViewRejection.None), Is.EqualTo(1));
            Assert.That(core.DepthIgnoredObjects, Is.EqualTo(0));
        }

        [Test]
        public void Capture_PrefersTheSharperOfTwoLikeViews()
        {
            var sink = new RecordingSink();
            PhotoCaptureCore core = Core(new FakeEncoder(), sink);
            core.BeginSweep();
            PhotoBox chair = ChairBox(float3.zero, 0f);
            AddFrame(core, in chair, 0f, 2f, 64);        // coarse pattern: reads as blurred
            AddFrame(core, in chair, 5f, 2f, 4);
            AddFrame(core, in chair, -5f, 2f, 4);
            core.BeginCapture(Room(), float3.zero, 0f);
            TickUntilIdle(core);
            Assert.That(sink.Sets[0].Views.Length, Is.EqualTo(1));
            Assert.That(math.abs(sink.Sets[0].Views[0].AzimuthDeg), Is.EqualTo(5f).Within(0.05f));
        }

        [Test]
        public void Jpeg_QualityDropsUntilTheCropFits()
        {
            var encoder = new FakeEncoder { FitsAtQuality = 60 };
            var sink = new RecordingSink();
            PhotoCaptureCore core = Core(encoder, sink);
            core.BeginSweep();
            PhotoBox chair = ChairBox(float3.zero, 0f);
            AddFrame(core, in chair, 0f, 2f);
            core.BeginCapture(Room(), float3.zero, 0f);
            TickUntilIdle(core);
            Assert.That(encoder.Qualities, Is.EqualTo(new[] { 85, 75, 65, 55 }));
            Assert.That(sink.Sets[0].Views[0].Jpeg.Length, Is.EqualTo(1055));
        }

        [Test]
        public void Jpeg_ThatNeverFitsOrFails_LeavesTheObjectWithoutAPhoto()
        {
            PhotoBox chair = ChairBox(float3.zero, 0f);
            foreach (FakeEncoder encoder in new[] { new FakeEncoder { FitsAtQuality = 0 }, new FakeEncoder { Fail = true } })
            {
                var sink = new RecordingSink();
                PhotoCaptureCore core = Core(encoder, sink);
                core.BeginSweep();
                AddFrame(core, in chair, 0f, 2f);
                core.BeginCapture(Room(), float3.zero, 0f);
                TickUntilIdle(core);
                Assert.That(sink.Calls, Is.EqualTo(new[] { "begin", "none " + CouchId, "none " + ChairId, "end" }));
            }
        }

        [Test]
        public void Crop_LargerThan768_IsScaledDown()
        {
            var encoder = new FakeEncoder();
            var sink = new RecordingSink();
            PhotoCaptureCore core = Core(encoder, sink);
            core.BeginSweep();
            // A wide, low shelf fills most of the image width: its square crop is clamped to 1067 x 720.
            RoomModel room = WithObjects(new[]
            {
                new RoomObject { Id = ShelfId, Category = ObjectCategory.Storage, Base = float3.zero, Size = new float3(1.5f, 0.9f, 0.3f), Yaw = 0f, TriTarget = 4000 }
            });
            PhotoBox shelf = PhotoScore.ToCaptureFrame(room.Objects[0], float3.zero, 0f);
            AddFrame(core, in shelf, 0f, 1.9f);
            core.BeginCapture(room, float3.zero, 0f);
            TickUntilIdle(core);
            Assert.That(sink.Sets.Count, Is.EqualTo(1));
            ObjectView view = sink.Sets[0].Views[0];
            Assert.That(view.Width, Is.EqualTo(PhotoScore.MaxCropSide));
            Assert.That(view.Height, Is.LessThan(PhotoScore.MaxCropSide));
            // The focal length shrinks with the image.
            Assert.That(view.Intrinsics.x, Is.LessThan(1000f));
            Assert.That(view.Intrinsics.x, Is.EqualTo(view.Intrinsics.y).Within(2f));
        }

        [Test]
        public void Cancel_DropsEverything_AndTheSinkHearsNoMore()
        {
            var sink = new RecordingSink();
            PhotoCaptureCore core = Core(new FakeEncoder(), sink);
            core.BeginSweep();
            PhotoBox chair = ChairBox(float3.zero, 0f);
            AddFrame(core, in chair, 0f, 2f);
            core.BeginCapture(Room(), float3.zero, 0f);
            core.Tick();
            core.Cancel();
            Assert.That(core.Busy, Is.False);
            Assert.That(core.KeyFrameCount, Is.EqualTo(0));
            core.Tick();
            Assert.That(sink.Calls, Is.EqualTo(new[] { "begin", "none " + CouchId }));

            // Cancelling in the middle of a sweep empties the ring and ends the sweep.
            core.BeginSweep();
            AddFrame(core, in chair, 0f, 2f);
            core.Cancel();
            Assert.That(core.KeyFrameCount, Is.EqualTo(0));
            Assert.That(core.Sweeping, Is.False);
        }

        [Test]
        public void BeginSweep_StartsWithAnEmptyRing()
        {
            PhotoCaptureCore core = Core(new FakeEncoder(), new RecordingSink(), 4);
            core.BeginSweep();
            PhotoBox chair = ChairBox(float3.zero, 0f);
            for (int i = 0; i < 6; i++) AddFrame(core, in chair, 10f * i, 2f);
            Assert.That(core.KeyFrameCount, Is.EqualTo(4));
            core.BeginSweep();
            Assert.That(core.KeyFrameCount, Is.EqualTo(0));
            Assert.That(core.Sweeping, Is.True);
        }

        [Test]
        public void Capture_OnAWorkerThread_DeliversTheSameResult()
        {
            var sink = new RecordingSink();
            var core = new PhotoCaptureCore(new FakeEncoder()) { Sink = sink };
            core.BeginSweep();
            PhotoBox chair = ChairBox(float3.zero, 0f);
            AddFrame(core, in chair, 0f, 2f);
            AddFrame(core, in chair, 60f, 2f);
            core.BeginCapture(Room(), float3.zero, 0f);
            for (int i = 0; i < 5000 && core.Busy; i++)
            {
                core.Tick();
                Thread.Sleep(1);
            }
            Assert.That(core.Busy, Is.False);
            Assert.That(sink.Calls, Is.EqualTo(new[] { "begin", "none " + CouchId, "submit " + ChairId, "end" }));
            Assert.That(sink.Sets[0].Views.Length, Is.EqualTo(2));
        }
    }
}
