using System;
using System.Collections.Generic;
using Atlas.Scan.Grid;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Routes
{
    /// <summary>
    /// Object photo capture follows the scan (contract lane C sections 10.3 and 11): the flow and the photo
    /// driver connected the way ScanSession connects them, with a recording capture in place of the camera.
    /// </summary>
    public class ScanPhotoWiringTests
    {
        static readonly DateTime Clock = new DateTime(2026, 10, 4, 9, 30, 15, DateTimeKind.Utc);
        static readonly float3 Head = new float3(1.5f, 1.4f, 1.5f);
        const float Frame = 1f / 60f;

        sealed class RecordingCapture : IObjectPhotoCapture
        {
            public readonly List<string> Calls = new List<string>();
            public int Frames, Ticks, PendingObjects;
            public bool LastDepthValid;
            public RoomModel Room;

            public int KeyFrameCount { get { return Frames; } }
            public bool Busy { get { return PendingObjects > 0; } }
            public void BeginSweep() { Calls.Add("sweep"); Frames = 0; PendingObjects = 0; }
            public void OnFrame(in HeadPose raw, in DepthFrame depth) { Frames++; LastDepthValid = depth.IsValid; }
            public void BeginCapture(RoomModel room) { Calls.Add("capture"); Room = room; PendingObjects = 2; }
            public void Tick() { Ticks++; if (PendingObjects > 0) PendingObjects--; }
            public void Cancel() { Calls.Add("cancel"); Frames = 0; PendingObjects = 0; }
        }

        // What ScanSession does with its flow and its driver, without the AR session.
        sealed class Rig
        {
            public readonly RecordingCapture Capture = new RecordingCapture();
            public readonly ScanPhotoDriver Driver;
            public readonly ScanFlow Flow;
            SensorFrame _sensors;

            public Rig()
            {
                Driver = new ScanPhotoDriver(Capture);
                Flow = new ScanFlow(() => ScanTestRooms.Snapshot(SyntheticRoom.Rectangle(3f, 3f).Build()), false);
                Flow.StateChanged += state => Driver.OnState(state, Flow.Result);
            }

            public void Tick(int frames = 1)
            {
                for (int i = 0; i < frames; i++)
                {
                    Driver.OnFrame(Flow.State, in _sensors);
                    Flow.KeyFrames = Driver.KeyFrameCount;
                    Flow.Tick(Frame, TrackingState.Normal, Head);
                    Driver.Deliver();
                }
            }

            public void SweepToConfirm()
            {
                Flow.Begin(ScanOptions.Default, Clock);
                Tick();                                 // Starting -> Sweeping
                Tick(5);
                Flow.EndSweep();
                Tick();                                 // Processing -> AwaitingConfirm
            }
        }

        [Test]
        public void ConfirmedScan_TakesKeyFramesDuringTheSweep_ThenHandsTheFinalRoomOver()
        {
            var rig = new Rig();
            rig.Tick(3);
            Assert.That(rig.Capture.Frames, Is.EqualTo(0), "no key frame before a scan");

            rig.SweepToConfirm();
            Assert.That(rig.Flow.State, Is.EqualTo(ScanState.AwaitingConfirm));
            Assert.That(rig.Capture.Calls, Is.EqualTo(new[] { "sweep" }));
            Assert.That(rig.Capture.Frames, Is.EqualTo(5), "one offer per Sweeping frame, none in Starting or Processing");
            Assert.That(rig.Capture.LastDepthValid, Is.False, "no depth image this frame: none is passed on");

            rig.Tick();
            Assert.That(rig.Flow.Progress.KeyFrames, Is.EqualTo(5), "the progress shows the ring's count");
            Assert.That(rig.Capture.Frames, Is.EqualTo(5), "no key frame while the outline is reviewed");

            rig.Flow.Confirm();
            Assert.That(rig.Flow.State, Is.EqualTo(ScanState.Finalising));
            Assert.That(rig.Capture.Room, Is.Null, "the room is not final yet");
            rig.Flow.CompleteFinalise("anchor", Clock.AddSeconds(30));
            Assert.That(rig.Flow.State, Is.EqualTo(ScanState.Done));
            Assert.That(rig.Capture.Calls, Is.EqualTo(new[] { "sweep", "capture" }));
            Assert.That(rig.Capture.Room, Is.SameAs(rig.Flow.Result));
            Assert.That(rig.Driver.Delivering, Is.True);

            // The hand-over runs on after Done, one object per frame, and then costs nothing.
            rig.Driver.Deliver();
            rig.Driver.Deliver();
            Assert.That(rig.Driver.Delivering, Is.False);
            rig.Driver.Deliver();
            Assert.That(rig.Capture.Ticks, Is.EqualTo(2));
        }

        [Test]
        public void CancelledScan_DropsItsKeyFrames()
        {
            var rig = new Rig();
            rig.SweepToConfirm();
            rig.Flow.Cancel();
            Assert.That(rig.Flow.State, Is.EqualTo(ScanState.Idle));
            Assert.That(rig.Capture.Calls, Is.EqualTo(new[] { "sweep", "cancel" }));
            Assert.That(rig.Capture.Room, Is.Null);
            rig.Tick(3);
            Assert.That(rig.Capture.Frames, Is.EqualTo(0));
        }

        [Test]
        public void RoomFromAFile_DuringAScan_IsNotPhotographed()
        {
            var rig = new Rig();
            rig.SweepToConfirm();
            RoomModel saved = rig.Flow.Proposal.Preview;
            rig.Flow.UseRoom(saved);
            Assert.That(rig.Flow.State, Is.EqualTo(ScanState.Done));
            Assert.That(rig.Capture.Room, Is.Null, "there are no photos of a room that was not scanned now");
            Assert.That(rig.Capture.Calls[rig.Capture.Calls.Count - 1], Is.EqualTo("cancel"));
            Assert.That(rig.Driver.Delivering, Is.False);
        }

        [Test]
        public void NewScan_AfterAConfirmedOne_StartsAFreshSweep()
        {
            var rig = new Rig();
            rig.SweepToConfirm();
            rig.Flow.Confirm();
            rig.Flow.CompleteFinalise("anchor", Clock.AddSeconds(30));
            Assert.That(rig.Driver.Delivering, Is.True);

            rig.Flow.Begin(ScanOptions.Default, Clock.AddSeconds(60));
            Assert.That(rig.Capture.Calls, Is.EqualTo(new[] { "sweep", "capture", "sweep" }));
            Assert.That(rig.Driver.Delivering, Is.False, "crops of the earlier room are dropped with its key frames");
        }

        [Test]
        public void SessionWithoutPhotoCapture_DoesNothing()
        {
            var driver = new ScanPhotoDriver(null);
            SensorFrame sensors = default(SensorFrame);
            driver.OnState(ScanState.Starting, null);
            driver.OnFrame(ScanState.Sweeping, in sensors);
            driver.OnState(ScanState.Finalising, null);
            driver.OnState(ScanState.Done, null);
            driver.Deliver();
            driver.Drop();
            Assert.That(driver.KeyFrameCount, Is.EqualTo(0));
            Assert.That(driver.Delivering, Is.False);
        }
    }
}
