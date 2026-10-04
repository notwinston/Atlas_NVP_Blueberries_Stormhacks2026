using System;
using System.Collections.Generic;
using Atlas.Scan.Grid;
using Atlas.Scan.Tests.Routes;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Photo
{
    /// <summary>
    /// The scan drives object photo capture (contract lane C sections 10.2 and 11): the ring is emptied when a
    /// scan starts, key frames are taken only during the sweep, a confirmed room starts selection and the
    /// hand-over, and a scan that ends any other way drops what it collected.
    /// </summary>
    public class ScanPhotoDriverTests
    {
        static readonly DateTime Clock = new DateTime(2026, 10, 4, 9, 30, 15, DateTimeKind.Utc);
        static readonly float3 Head = new float3(1.5f, 1.4f, 1.5f);
        const float Dt = 1f / 60f;
        const string AnchorId = "00000000000000AB-00000000000000CD";

        /// <summary>Records what the driver asks, in order. A capture hands over two objects, one per Tick.</summary>
        sealed class FakeCapture : IObjectPhotoCapture
        {
            public readonly List<string> Calls = new List<string>();
            public int Frames, FramesWithDepth, Ticks;
            public RoomModel Room;
            public string RoomAnchorAtCapture, RoomTimeAtCapture;
            int _objectsLeft;

            public int KeyFrameCount { get; private set; }
            public bool Busy { get; private set; }

            public void BeginSweep()
            {
                Calls.Add("BeginSweep");
                KeyFrameCount = 0;
                Busy = false;
            }

            public void OnFrame(in HeadPose raw, in DepthFrame depth)
            {
                // One entry for the whole sweep keeps the order readable.
                if (Calls[Calls.Count - 1] != "OnFrame") Calls.Add("OnFrame");
                Frames++;
                if (depth.IsValid) FramesWithDepth++;
                KeyFrameCount++;
            }

            public void BeginCapture(RoomModel room)
            {
                Calls.Add("BeginCapture");
                Room = room;
                RoomAnchorAtCapture = room.Anchor.Id;
                RoomTimeAtCapture = room.CreatedUtc;
                KeyFrameCount = 0;
                _objectsLeft = 2;
                Busy = true;
            }

            public void Tick()
            {
                Calls.Add("Tick");
                Ticks++;
                if (--_objectsLeft <= 0) Busy = false;
            }

            public void Cancel()
            {
                Calls.Add("Cancel");
                KeyFrameCount = 0;
                Busy = false;
            }
        }

        /// <summary>The scan flow and the driver, connected and stepped the way ScanSession and the frame loop do it.</summary>
        sealed class Scan
        {
            public readonly FakeCapture Photos = new FakeCapture();
            public readonly ScanPhotoDriver Driver;
            public readonly ScanFlow Flow;
            public SensorFrame Sensors;

            public Scan()
            {
                Driver = new ScanPhotoDriver(Photos);
                Flow = new ScanFlow(() => ScanTestRooms.Snapshot(SyntheticRoom.Rectangle(3f, 3f).Build()), false);
                Flow.StateChanged += state => Driver.OnState(state, Flow.Result);
                Sensors.Head = new HeadPose(0.0, Head, quaternion.identity, TrackingState.Normal);
            }

            public void Frame(float deltaTime = Dt)
            {
                // ScanSession.Tick: whoever drives the flow calls it only while a scan is running.
                ScanState state = Flow.State;
                if (state != ScanState.Idle && state != ScanState.Done && state != ScanState.Failed)
                {
                    Driver.OnFrame(state, in Sensors);
                    Flow.KeyFrames = Driver.KeyFrameCount;
                    Flow.Tick(deltaTime, Sensors.Head.Tracking, Sensors.Head.Position);
                    if (Flow.State == ScanState.Finalising) Flow.CompleteFinalise(AnchorId, Clock.AddSeconds(40));
                }
                // Step 8b of the frame loop, every frame of the app.
                FrameContext frame = default(FrameContext);
                Driver.AfterFrame(in frame);
            }

            public void Frames(int count)
            {
                for (int i = 0; i < count; i++) Frame();
            }

            /// <summary>Begin, five frames of sweep, EndSweep, and the proposal is waiting.</summary>
            public void SweepToProposal()
            {
                Flow.Begin(ScanOptions.Default, Clock);
                Frame();
                Assert.That(Flow.State, Is.EqualTo(ScanState.Sweeping));
                Frames(5);
                Flow.EndSweep();
                Frame();
                Assert.That(Flow.State, Is.EqualTo(ScanState.AwaitingConfirm));
            }
        }

        [Test]
        public void A_confirmed_scan_drives_the_capture_in_order()
        {
            var scan = new Scan();
            scan.Flow.Begin(ScanOptions.Default, Clock);
            Assert.That(scan.Photos.Calls, Is.EqualTo(new[] { "BeginSweep" }), "the ring is emptied when the scan starts");

            // Starting: no key frame before the session tracks and the sweep begins.
            scan.Frame();
            Assert.That(scan.Flow.State, Is.EqualTo(ScanState.Sweeping));
            Assert.That(scan.Photos.Frames, Is.EqualTo(0));

            scan.Frames(5);
            Assert.That(scan.Photos.Frames, Is.EqualTo(5), "once per frame of the sweep");
            Assert.That(scan.Flow.Progress.KeyFrames, Is.EqualTo(5), "the progress shows the ring");

            // The sweep is over: the walker looks at the outline, and no frame is taken any more.
            scan.Flow.EndSweep();
            scan.Frames(4);
            Assert.That(scan.Flow.State, Is.EqualTo(ScanState.AwaitingConfirm));
            Assert.That(scan.Photos.Frames, Is.EqualTo(5));

            scan.Flow.Confirm();
            Assert.That(scan.Photos.Calls, Is.EqualTo(new[] { "BeginSweep", "OnFrame" }), "nothing starts before the room is final");

            scan.Frame();
            Assert.That(scan.Flow.State, Is.EqualTo(ScanState.Done));
            Assert.That(scan.Photos.Room, Is.SameAs(scan.Flow.Result));
            Assert.That(scan.Photos.RoomAnchorAtCapture, Is.EqualTo(AnchorId), "the anchor was written before the sink saw the room");
            Assert.That(scan.Photos.RoomTimeAtCapture, Is.EqualTo("2026-10-04T09:30:55Z"));
            Assert.That(scan.Driver.Delivering, Is.True);

            scan.Frames(5);
            Assert.That(scan.Photos.Calls, Is.EqualTo(new[] { "BeginSweep", "OnFrame", "BeginCapture", "Tick", "Tick" }),
                "one object per frame, and no call once the last one is handed over");
            Assert.That(scan.Driver.Delivering, Is.False);
        }

        [Test]
        public void Nothing_is_asked_of_the_capture_outside_a_scan()
        {
            var scan = new Scan();
            scan.Frames(20);
            Assert.That(scan.Photos.Calls, Is.Empty);

            scan.SweepToProposal();
            scan.Flow.Confirm();
            scan.Frames(10);
            int calls = scan.Photos.Calls.Count;
            scan.Frames(100);
            Assert.That(scan.Photos.Calls.Count, Is.EqualTo(calls), "walking after the scan costs the capture nothing");
            Assert.That(scan.Photos.Frames, Is.EqualTo(5));
        }

        [Test]
        public void A_cancelled_scan_drops_its_key_frames()
        {
            var scan = new Scan();
            scan.SweepToProposal();
            scan.Flow.Cancel();
            Assert.That(scan.Flow.State, Is.EqualTo(ScanState.Idle));
            Assert.That(scan.Photos.Calls, Is.EqualTo(new[] { "BeginSweep", "OnFrame", "Cancel" }));
            scan.Frames(5);
            Assert.That(scan.Photos.Calls.Count, Is.EqualTo(3));
        }

        [Test]
        public void A_failed_scan_drops_its_key_frames()
        {
            var scan = new Scan();
            scan.Flow.Begin(ScanOptions.Default, Clock);
            scan.Frames(3);
            scan.Sensors.Head = new HeadPose(0.0, Head, quaternion.identity, TrackingState.Limited);
            for (int i = 0; i < 11; i++) scan.Frame(1f);
            Assert.That(scan.Flow.State, Is.EqualTo(ScanState.Failed));
            Assert.That(scan.Photos.Calls[scan.Photos.Calls.Count - 1], Is.EqualTo("Cancel"));
            Assert.That(scan.Photos.Calls, Has.No.Member("BeginCapture"));
        }

        [Test]
        public void A_room_that_was_not_scanned_takes_no_photos()
        {
            // From a file with no scan at all.
            var scan = new Scan();
            scan.Flow.UseRoom(new RoomModel { Id = "saved" });
            scan.Frames(5);
            Assert.That(scan.Flow.State, Is.EqualTo(ScanState.Done));
            Assert.That(scan.Photos.Calls, Has.No.Member("BeginCapture"));
            Assert.That(scan.Photos.Calls, Has.No.Member("Tick"));

            // In the middle of a scan, the confirmation already given: the sweep's frames are not this room's.
            scan = new Scan();
            scan.SweepToProposal();
            scan.Flow.Confirm();
            scan.Driver.Drop();                                     // as ScanSession.UseRoom does
            scan.Flow.UseRoom(new RoomModel { Id = "saved" });
            scan.Frames(5);
            Assert.That(scan.Photos.Calls, Has.No.Member("BeginCapture"));
            Assert.That(scan.Photos.Calls[scan.Photos.Calls.Count - 1], Is.EqualTo("Cancel"));
        }

        [Test]
        public void A_rescan_drops_the_crops_still_waiting()
        {
            var scan = new Scan();
            scan.SweepToProposal();
            scan.Flow.Confirm();
            scan.Frame();
            Assert.That(scan.Photos.Ticks, Is.EqualTo(1));
            Assert.That(scan.Driver.Delivering, Is.True);

            scan.Flow.Begin(ScanOptions.Default, Clock.AddMinutes(5));
            Assert.That(scan.Photos.Calls[scan.Photos.Calls.Count - 1], Is.EqualTo("BeginSweep"));
            Assert.That(scan.Driver.Delivering, Is.False);
            scan.Frames(3);
            Assert.That(scan.Photos.Ticks, Is.EqualTo(1), "the first scan's crops are not handed over any more");
        }

        [Test]
        public void Leaving_the_session_idle_after_a_confirmed_room_keeps_its_hand_over()
        {
            var scan = new Scan();
            scan.SweepToProposal();
            scan.Flow.Confirm();
            scan.Frame();
            scan.Flow.Cancel();
            Assert.That(scan.Flow.State, Is.EqualTo(ScanState.Idle));
            Assert.That(scan.Photos.Calls, Has.No.Member("Cancel"));
            scan.Frames(3);
            Assert.That(scan.Photos.Ticks, Is.EqualTo(2));
            Assert.That(scan.Driver.Delivering, Is.False);
        }

        [Test]
        public void A_key_frame_gets_this_frames_depth_or_none()
        {
            var scan = new Scan();
            scan.Flow.Begin(ScanOptions.Default, Clock);
            scan.Frame();

            // A depth image kept from an earlier frame: it is not this frame's.
            scan.Sensors.Depth = new DepthFrame { Depth = new float[4], Width = 2, Height = 2, Fx = 1f, Fy = 1f };
            scan.Sensors.HasDepth = false;
            scan.Frames(3);
            Assert.That(scan.Photos.Frames, Is.EqualTo(3));
            Assert.That(scan.Photos.FramesWithDepth, Is.EqualTo(0));

            scan.Sensors.HasDepth = true;
            scan.Frames(2);
            Assert.That(scan.Photos.FramesWithDepth, Is.EqualTo(2));
        }

        [Test]
        public void A_session_with_no_capture_runs_unchanged()
        {
            var driver = new ScanPhotoDriver(null);
            var sensors = new SensorFrame();
            Assert.DoesNotThrow(() =>
            {
                driver.OnState(ScanState.Starting, null);
                driver.OnFrame(ScanState.Sweeping, in sensors);
                driver.OnState(ScanState.Finalising, null);
                driver.OnState(ScanState.Done, new RoomModel { Id = "r" });
                driver.Deliver();
                driver.Drop();
            });
            Assert.That(driver.KeyFrameCount, Is.EqualTo(0));
            Assert.That(driver.Delivering, Is.False);
        }
    }
}
