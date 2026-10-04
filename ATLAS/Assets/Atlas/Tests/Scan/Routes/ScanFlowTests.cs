using System;
using System.Collections.Generic;
using System.Threading;
using Atlas.Scan.Grid;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Routes
{
    /// <summary>The scan's state machine: Idle, Starting, Sweeping, Processing, AwaitingConfirm, Finalising, Done, Failed.</summary>
    public class ScanFlowTests
    {
        static readonly DateTime Clock = new DateTime(2026, 10, 4, 9, 30, 15, DateTimeKind.Utc);
        static readonly float3 Head = new float3(1.5f, 1.4f, 1.5f);
        const float Frame = 1f / 60f;

        sealed class Run
        {
            public readonly ScanFlow Flow;
            public readonly List<ScanState> States = new List<ScanState>();
            public int Captures;

            public Run(Func<ScanSnapshot> capture, bool background = false)
            {
                Flow = new ScanFlow(() => { Captures++; return capture != null ? capture() : null; }, background);
                Flow.StateChanged += States.Add;
            }

            public void Tick(float seconds, TrackingState tracking = TrackingState.Normal)
            {
                for (float t = 0f; t < seconds - 1e-6f; t += Frame) Flow.Tick(Frame, tracking, Head);
            }
        }

        static ScanSnapshot Room3x3() { return ScanTestRooms.Snapshot(SyntheticRoom.Rectangle(3f, 3f).Build()); }
        static ScanSnapshot Room4x5() { return ScanTestRooms.Snapshot(SyntheticRoom.Rectangle(4f, 5f).Build()); }

        static Run Swept(Func<ScanSnapshot> capture)
        {
            var run = new Run(capture);
            run.Flow.Begin(ScanOptions.Default, Clock);
            run.Tick(Frame);
            run.Flow.EndSweep();
            run.Tick(Frame);
            return run;
        }

        [Test]
        public void FullScan_WalksEveryState()
        {
            var run = new Run(Room3x3);
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Idle));

            run.Flow.Begin(ScanOptions.Default, Clock);
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Starting));
            Assert.That(run.Flow.RoomId, Is.EqualTo("20261004_093015"));

            // The sweep does not start before the session tracks.
            run.Tick(0.5f, TrackingState.Limited);
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Starting));
            run.Tick(Frame);
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Sweeping));

            run.Tick(1.2f);
            run.Flow.EndSweep();
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Processing));
            run.Tick(Frame);
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.AwaitingConfirm));

            OutlineProposal proposal = run.Flow.Proposal;
            Assert.That(proposal.Count, Is.EqualTo(4));
            Assert.That(proposal.Route, Is.EqualTo(ScanRoute.FloorScan));
            Assert.That(proposal.Preview.FreeRect.Wf, Is.EqualTo(2.4f).Within(1e-4f));
            Assert.That(proposal.Preview.LapSide, Is.EqualTo(2.0f).Within(1e-4f));
            var rect = new float2[4];
            proposal.CopyFreeRect(rect);
            Assert.That(math.distance(rect[0], new float2(0.3f, 0.3f)), Is.LessThan(1e-3f));
            Assert.That(math.distance(rect[2], new float2(2.7f, 2.7f)), Is.LessThan(1e-3f));
            proposal.CopyLapSquare(rect);
            Assert.That(math.distance(rect[0], new float2(0.5f, 0.5f)), Is.LessThan(1e-3f));

            run.Flow.Confirm();
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Finalising));
            run.Flow.CompleteFinalise("00000000000000AB-00000000000000CD", Clock.AddSeconds(40));
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Done));

            RoomModel room = run.Flow.Result;
            Assert.That(room.Id, Is.EqualTo("20261004_093015"));
            Assert.That(room.CreatedUtc, Is.EqualTo("2026-10-04T09:30:55Z"));
            Assert.That(room.Anchor.Id, Is.EqualTo("00000000000000AB-00000000000000CD"));
            Assert.That(room.Quality.Route, Is.EqualTo(ScanRoute.FloorScan));
            var warnings = new List<string>();
            var errors = new List<string>();
            Assert.That(RoomModelValidator.Validate(room, errors, warnings), Is.True, string.Join("; ", errors));
            Assert.That(warnings, Is.Empty);
            Assert.That(run.States, Is.EqualTo(new[]
            {
                ScanState.Starting, ScanState.Sweeping, ScanState.Processing, ScanState.AwaitingConfirm,
                ScanState.Finalising, ScanState.Done,
            }));
        }

        [Test]
        public void Begin_IsAcceptedOnlyInIdleDoneAndFailed()
        {
            var run = new Run(Room3x3);
            run.Flow.Begin(ScanOptions.Default, Clock);
            run.Tick(Frame);
            var other = ScanOptions.Default;
            other.RoomId = "second";
            run.Flow.Begin(other, Clock);
            Assert.That(run.Flow.RoomId, Is.EqualTo("20261004_093015"), "Begin during a sweep is ignored");
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Sweeping));

            run.Flow.Fail(ScanFailure.TrackingLost);
            run.Flow.Begin(other, Clock);
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Starting));
            Assert.That(run.Flow.RoomId, Is.EqualTo("second"));
            Assert.That(run.Flow.Failure, Is.EqualTo(ScanFailure.None));
        }

        [Test]
        public void CallsOutOfTurn_AreIgnored()
        {
            var run = new Run(Room3x3);
            run.Flow.EndSweep();
            run.Flow.Confirm();
            run.Flow.CompleteFinalise("x", Clock);
            run.Flow.MoveCorner(0, float2.zero);
            Assert.That(run.Flow.AddCorner(float2.zero), Is.False);
            run.Tick(1f);
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Idle));
            Assert.That(run.States, Is.Empty);
            Assert.That(run.Captures, Is.EqualTo(0));
        }

        [Test]
        public void TrackingLostForTenSeconds_FailsTheSweep()
        {
            var run = new Run(Room3x3);
            run.Flow.Begin(ScanOptions.Default, Clock);
            run.Tick(Frame);
            run.Tick(9f, TrackingState.Limited);
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Sweeping));
            // Tracking comes back: the count starts again.
            run.Tick(0.1f);
            run.Tick(9.5f, TrackingState.NotTracking);
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Sweeping));
            run.Tick(0.6f, TrackingState.NotTracking);
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Failed));
            Assert.That(run.Flow.Failure, Is.EqualTo(ScanFailure.TrackingLost));
        }

        [Test]
        public void SweepThatSawNothing_FailsWithNoFloor()
        {
            Run run = Swept(null);
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Failed));
            Assert.That(run.Flow.Failure, Is.EqualTo(ScanFailure.NoFloor));
            Assert.That(run.Flow.Proposal, Is.Null);
        }

        [Test]
        public void TooSmallARoom_Fails()
        {
            Run run = Swept(() => ScanTestRooms.Snapshot(SyntheticRoom.Rectangle(0.9f, 0.9f).Build()));
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Failed));
            Assert.That(run.Flow.Failure, Is.EqualTo(ScanFailure.TooSmall));
        }

        [Test]
        public void TappedCorners_GiveARouteOneRoom()
        {
            var run = new Run(null);
            var options = ScanOptions.Default;
            options.RoomId = "by hand!";
            options.WallHeight = 2.7f;
            run.Flow.Begin(options, Clock);
            run.Tick(Frame);
            Assert.That(run.Flow.AddCorner(new float2(0f, 0f), 0.02f), Is.True);
            Assert.That(run.Flow.AddCorner(new float2(0.01f, 0.01f)), Is.False, "the same corner twice");
            run.Flow.AddCorner(new float2(4f, 0.02f), 0.01f);
            run.Flow.AddCorner(new float2(9f, 9f));
            run.Flow.RemoveLastCorner();
            run.Flow.AddCorner(new float2(4.01f, 5f));
            run.Flow.AddCorner(new float2(0f, 5.01f));
            Assert.That(run.Flow.CornerCount, Is.EqualTo(4));
            run.Tick(Frame);
            Assert.That(run.Flow.Progress.CornerCount, Is.EqualTo(4));

            run.Flow.EndSweep();
            run.Tick(Frame);
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.AwaitingConfirm));
            OutlineProposal proposal = run.Flow.Proposal;
            Assert.That(proposal.Route, Is.EqualTo(ScanRoute.ManualOutline));
            Assert.That(proposal.FloorY, Is.EqualTo(0.01f).Within(1e-5f), "the lowest tapped height is the floor");
            Assert.That(proposal.Preview.Id, Is.EqualTo("by_hand_"));
            Assert.That(proposal.Preview.FreeRect.Wf, Is.EqualTo(3.4f).Within(0.051f));
            Assert.That(proposal.Preview.FreeRect.Df, Is.EqualTo(4.4f).Within(0.051f));
            Assert.That(proposal.Preview.MaxWallHeight, Is.EqualTo(2.7f).Within(1e-5f));
            Assert.That(proposal.Preview.Standing, Is.False);
        }

        [Test]
        public void SmallTappedRoom_IsStanding()
        {
            var run = new Run(null);
            run.Flow.Begin(ScanOptions.Default, Clock);
            run.Tick(Frame);
            foreach (float2 corner in ScanTestRooms.Rectangle(2f, 2f)) run.Flow.AddCorner(corner);
            run.Flow.EndSweep();
            run.Tick(Frame);
            Assert.That(run.Flow.Proposal.Preview.Standing, Is.True);
        }

        [Test]
        public void InvalidCorrection_IsRefused_AndAValidOneRecomputes()
        {
            Run run = Swept(Room4x5);
            OutlineProposal first = run.Flow.Proposal;
            Assert.That(first.Preview.FreeRect.Wf, Is.EqualTo(3.4f).Within(1e-4f));
            int capturesAfterSweep = run.Captures;

            // Dragging a corner across the opposite wall makes the outline cross itself.
            int corner = NearestIndex(first.Outline, new float2(4f, 0f));
            run.Flow.MoveCorner(corner, new float2(-2f, 3f));
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.AwaitingConfirm));
            Assert.That(run.Flow.Failure, Is.EqualTo(ScanFailure.OutlineInvalid));
            Assert.That(run.Flow.Proposal, Is.SameAs(first));
            run.Flow.MoveCorner(99, float2.zero);
            Assert.That(run.Flow.Proposal, Is.SameAs(first));

            // Pulling two corners in by a metre makes the room 3 x 5 m.
            run.Flow.MoveCorner(corner, new float2(3f, 0f));
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Processing));
            Assert.That(run.Flow.Failure, Is.EqualTo(ScanFailure.None));
            run.Flow.MoveCorner(NearestIndex(first.Outline, new float2(4f, 5f)), new float2(3f, 5f));
            run.Tick(3 * Frame);
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.AwaitingConfirm));
            Assert.That(run.Flow.Proposal, Is.Not.SameAs(first));
            Assert.That(run.Flow.Proposal.Preview.FreeRect.Wf, Is.EqualTo(2.4f).Within(1e-4f));
            Assert.That(run.Flow.Proposal.Preview.FreeRect.Df, Is.EqualTo(4.4f).Within(1e-4f));
            Assert.That(run.Captures, Is.EqualTo(capturesAfterSweep), "a correction reuses the sweep; the sensors are not read again");

            // A correction that leaves no room to stand in is refused as well.
            OutlineProposal second = run.Flow.Proposal;
            run.Flow.SetOutline(ScanTestRooms.Rectangle(0.8f, 0.8f), 4);
            run.Tick(3 * Frame);
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.AwaitingConfirm));
            Assert.That(run.Flow.Failure, Is.EqualTo(ScanFailure.TooSmall));
            Assert.That(run.Flow.Proposal, Is.SameAs(second));
        }

        [Test]
        public void InsertAndRemoveCorner_ChangeTheOutline()
        {
            Run run = Swept(Room4x5);
            float2[] outline = run.Flow.Proposal.Outline;
            int a = NearestIndex(outline, new float2(4f, 0f));
            Assert.That(NearestIndex(outline, new float2(4f, 5f)), Is.EqualTo((a + 1) % 4), "the outline is counter-clockwise");
            // A bay pushed out of the east wall.
            run.Flow.InsertCorner(a, new float2(5f, 2.5f));
            run.Tick(3 * Frame);
            Assert.That(run.Flow.Proposal.Count, Is.EqualTo(5));
            run.Flow.RemoveCorner(NearestIndex(run.Flow.Proposal.Outline, new float2(5f, 2.5f)));
            run.Tick(3 * Frame);
            Assert.That(run.Flow.Proposal.Count, Is.EqualTo(4));
            // A triangle cannot lose a corner.
            run.Flow.SetOutline(new[] { new float2(0f, 0f), new float2(4f, 0f), new float2(0f, 5f) }, 3);
            run.Tick(3 * Frame);
            Assert.That(run.Flow.Proposal.Count, Is.EqualTo(3));
            run.Flow.RemoveCorner(0);
            Assert.That(run.Flow.Failure, Is.EqualTo(ScanFailure.OutlineInvalid));
        }

        [Test]
        public void Confirm_WaitsForTheCorrection()
        {
            Run run = Swept(Room4x5);
            run.Flow.MoveCorner(NearestIndex(run.Flow.Proposal.Outline, new float2(4f, 0f)), new float2(4.5f, 0f));
            run.Flow.Confirm();
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Processing), "Confirm is for a proposal the walker has seen");
            run.Tick(3 * Frame);
            run.Flow.Confirm();
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Finalising));
        }

        [Test]
        public void Cancel_GoesToIdle_FromAnyState()
        {
            Run run = Swept(Room3x3);
            run.Flow.Cancel();
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Idle));
            Assert.That(run.Flow.Failure, Is.EqualTo(ScanFailure.Cancelled));
            Assert.That(run.Flow.Proposal, Is.Null);

            run.Flow.Begin(ScanOptions.Default, Clock);
            run.Flow.Cancel();
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Idle));
            run.Flow.Begin(ScanOptions.Default, Clock);
            Assert.That(run.Flow.Failure, Is.EqualTo(ScanFailure.None));
        }

        [Test]
        public void Fixture_SkipsTheScan()
        {
            var run = new Run(Room3x3);
            RoomModel fixture = RoomFixtures.LoadFromProject(RoomFixtures.Sample4x5Table);
            run.Flow.UseRoom(fixture);
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Done));
            Assert.That(run.Flow.Result, Is.SameAs(fixture));
            Assert.That(run.States, Is.EqualTo(new[] { ScanState.Done }));

            // Also in the middle of a sweep, and a new scan may follow.
            run.Flow.Begin(ScanOptions.Default, Clock);
            run.Tick(0.2f);
            run.Flow.UseRoom(fixture);
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Done));
            run.Flow.Begin(ScanOptions.Default, Clock);
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.Starting));
        }

        [Test]
        public void Sweep_ReportsProgressAndAPreview()
        {
            ScanSnapshot scan = ScanTestRooms.Snapshot(SyntheticRoom.Rectangle(3f, 4f).Build());
            scan.Boxes = new[] { new ObstacleBox { Base = new float3(1.5f, 0f, 3f), Size = new float3(1f, 0.7f, 0.5f), Category = 2 } };
            var run = new Run(() => scan);
            run.Flow.KeyFrames = 12;
            run.Flow.Begin(ScanOptions.Default, Clock);
            run.Tick(Frame);
            Assert.That(run.Flow.Progress.PreviewVersion, Is.EqualTo(0));
            Assert.That(run.Flow.Progress.Hint, Is.EqualTo(ScanHint.PointAtFloor));

            run.Tick(1.1f);
            ScanProgress progress = run.Flow.Progress;
            Assert.That(progress.PreviewVersion, Is.GreaterThanOrEqualTo(1));
            Assert.That(progress.Seconds, Is.EqualTo(1.1f + Frame).Within(0.05f));
            Assert.That(progress.FloorFound, Is.True);
            Assert.That(progress.FloorArea, Is.EqualTo(12f).Within(0.5f));
            Assert.That(progress.MeshTriangles, Is.EqualTo(scan.TriangleCount));
            Assert.That(progress.BoxCount, Is.EqualTo(1));
            Assert.That(progress.RouteReached, Is.EqualTo(ScanRoute.ObjectBoxes));
            Assert.That(progress.KeyFrames, Is.EqualTo(12));
            Assert.That(progress.Tracking, Is.EqualTo(TrackingState.Normal));
            Assert.That(progress.Hint, Is.EqualTo(ScanHint.KeepSweeping));
            var outline = new float2[16];
            Assert.That(run.Flow.CopyPreviewOutline(outline), Is.EqualTo(4));
            Assert.That(run.Flow.Grid, Is.Not.Null);
            // Two previews a second, not one a frame.
            Assert.That(run.Captures, Is.InRange(2, 3));

            run.Flow.ExternalHint = ScanHint.SlowDown;
            run.Tick(Frame);
            Assert.That(run.Flow.Progress.Hint, Is.EqualTo(ScanHint.SlowDown));
        }

        [Test]
        public void Options_SwitchRoutesOff()
        {
            ScanSnapshot scan = ScanTestRooms.Snapshot(SyntheticRoom.Rectangle(3f, 4f).Build());
            scan.Boxes = new[] { new ObstacleBox { Base = new float3(1.5f, 0f, 3f), Size = new float3(1f, 0.7f, 0.5f) } };
            var run = new Run(() => ScanTestRooms.Snapshot(new FloorGridInput
            {
                Vertices = scan.Vertices, Indices = scan.Indices, TriangleClasses = scan.Classes, Walls = scan.Walls,
                FloorHeight = scan.FloorHeight, ExternalBoxes = scan.Boxes, Walker = scan.Walker,
            }));
            var options = ScanOptions.Default;
            options.UseBoxes = false;
            run.Flow.Begin(options, Clock);
            run.Tick(Frame);
            run.Flow.EndSweep();
            run.Tick(Frame);
            Assert.That(run.Flow.Proposal.Route, Is.EqualTo(ScanRoute.FloorScan));
            Assert.That(run.Flow.Proposal.Preview.Objects, Is.Empty);

            // Without the mesh and with no corners there is nothing to build from.
            run.Flow.Cancel();
            options.UseMesh = false;
            run.Flow.Begin(options, Clock);
            run.Tick(Frame);
            run.Flow.EndSweep();
            run.Tick(Frame);
            Assert.That(run.Flow.Failure, Is.EqualTo(ScanFailure.NoFloor));
        }

        [Test]
        public void Hint_AsksForCornersWhenNoFloorIsFound()
        {
            var run = new Run(null);
            run.Flow.Begin(ScanOptions.Default, Clock);
            run.Tick(Frame);
            run.Tick(19f);
            Assert.That(run.Flow.Progress.Hint, Is.EqualTo(ScanHint.PointAtFloor));
            run.Tick(1.5f);
            Assert.That(run.Flow.Progress.Hint, Is.EqualTo(ScanHint.TapCorners));
            Assert.That(run.Flow.Progress.FloorFound, Is.False);

            // A device with no LiDAR mesh asks for corners from the start.
            var plain = new Run(null);
            plain.Flow.MeshExpected = false;
            plain.Flow.Begin(ScanOptions.Default, Clock);
            plain.Tick(2 * Frame);
            Assert.That(plain.Flow.Progress.Hint, Is.EqualTo(ScanHint.TapCorners));
        }

        [Test]
        public void WalkersPath_IsRecordedAndCountsAsFloor()
        {
            // Nothing is sensed; the walker walks a lap along the walls of a 3 x 4 m room and ends the sweep.
            var flow = new ScanFlow(null, false);
            flow.Begin(ScanOptions.Default, Clock);
            float2[] lap = { new float2(0.3f, 0.3f), new float2(2.7f, 0.3f), new float2(2.7f, 3.7f), new float2(0.3f, 3.7f) };
            for (int k = 0; k < 4; k++)
            {
                for (int s = 0; s < 120; s++)
                {
                    float2 p = math.lerp(lap[k], lap[(k + 1) % 4], s / 120f);
                    flow.Tick(Frame, TrackingState.Normal, new float3(p.x, 1.4f, p.y));
                }
            }
            var path = new float2[ScanFlow.MaxPathPoints];
            int points = flow.CopyPath(path);
            Assert.That(points, Is.InRange(150, 260), "a point every 5 cm of an 11.6 m lap");
            flow.EndSweep();
            flow.Tick(Frame, TrackingState.Normal, new float3(0.3f, 1.4f, 0.3f));
            Assert.That(flow.State, Is.EqualTo(ScanState.AwaitingConfirm));
            Assert.That(flow.Proposal.Route, Is.EqualTo(ScanRoute.ManualOutline));
            Assert.That(flow.Proposal.Preview.FreeRect.Wf, Is.EqualTo(2.4f).Within(0.10f));
            Assert.That(flow.Proposal.Preview.FreeRect.Df, Is.EqualTo(3.4f).Within(0.10f));
        }

        [Test]
        public void WorkerThread_DeliversTheSameProposal()
        {
            var run = new Run(Room3x3, true);
            run.Flow.Begin(ScanOptions.Default, Clock);
            run.Tick(Frame);
            run.Flow.EndSweep();
            for (int k = 0; k < 2000 && run.Flow.State == ScanState.Processing; k++)
            {
                Thread.Sleep(5);
                run.Tick(Frame);
            }
            Assert.That(run.Flow.State, Is.EqualTo(ScanState.AwaitingConfirm));
            Assert.That(run.Flow.LastError, Is.Null);
            Assert.That(run.Flow.Proposal.Preview.FreeRect.Wf, Is.EqualTo(2.4f).Within(1e-4f));
        }

        [Test]
        public void RoomId_IsCleaned()
        {
            Assert.That(ScanFlow.CleanId(null, Clock), Is.EqualTo("20261004_093015"));
            Assert.That(ScanFlow.CleanId("lab room/2", Clock), Is.EqualTo("lab_room_2"));
            Assert.That(ScanFlow.CleanId(new string('a', 60), Clock).Length, Is.EqualTo(40));
        }

        static int NearestIndex(float2[] ring, float2 p)
        {
            int best = 0;
            for (int k = 1; k < ring.Length; k++)
                if (math.distance(ring[k], p) < math.distance(ring[best], p)) best = k;
            return best;
        }
    }
}
