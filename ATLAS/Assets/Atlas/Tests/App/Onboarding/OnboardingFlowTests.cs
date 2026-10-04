using Atlas.App.Onboarding;
using Atlas.Link;
using Atlas.Scan;
using Atlas.Telemetry;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.App.Tests.Onboarding
{
    /// <summary>
    /// Onboarding in the hand (Master Plan task P1-C12): the state machine from launch to Home, driven by
    /// fakes for the camera permission, the Station, the scan session, the saved rooms and the headset view.
    /// </summary>
    public class OnboardingFlowTests
    {
        // ---- the default path ---------------------------------------------------------------------------------

        [Test]
        public void DefaultPath_ReachesHome_WithThreePrompts()
        {
            var rig = new Rig();
            rig.Flow.Begin();
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Welcome));
            Assert.That(rig.Show().PrimaryAction, Is.EqualTo(OnboardingAction.Start));

            // 1  Start. The camera prompt and the Local Network prompt are the system's own, raised in the hand.
            rig.Press(OnboardingAction.Start);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Camera));
            Assert.That(rig.Camera.Requests, Is.EqualTo(1));
            rig.Camera.AnswerPrompt();
            rig.Station.OnBegin = PairingPhase.Connected;
            rig.Frames();
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Station));
            Assert.That(rig.Station.Begins, Is.EqualTo(1), "the first connect attempt is made with the phone in the hand");
            Assert.That(rig.Show().Body, Does.Contain("192.168.0.10:9050").And.Contain("connected"));
            rig.Seconds(OnboardingFlow.StationDwell);

            // The scan starts by itself, with the automatic route.
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Sweep));
            Assert.That(rig.Scan.Calls, Is.EqualTo(new[] { "begin auto" }));
            Assert.That(rig.Flow.CornerMode, Is.False);
            rig.Scan.FindFloor(8.5f);
            rig.Scan.ProgressNow.BoxCount = 3;
            rig.Scan.ProgressNow.UnknownShare = 0.12f;
            rig.Frames();
            OnboardingScreen sweep = rig.Show();
            Assert.That(sweep.Body, Does.Contain("Floor: found, 8.5 m2").And.Contain("Objects: 3").And.Contain("Not seen yet: 12 %"));
            Assert.That(sweep.Body, Does.Contain("About 30 seconds"));
            Assert.That(sweep.Offers(OnboardingAction.ScanManual), Is.True, "the manual outline is always there");

            // 2  Done.
            rig.Press(OnboardingAction.DoneSweeping);
            rig.Frames();
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Confirm));
            Assert.That(rig.Show().Body, Does.Contain("Free floor: 3.0 x 3.0 m").And.Contain("walk a square of 2.0 m"));

            // 3  Looks right.
            rig.Press(OnboardingAction.Accept);
            rig.Frames();
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Build));
            Assert.That(rig.Rooms.Calls, Is.EqualTo(new[] { "activate Scan scan_1" }));
            rig.Frames();
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Headset));
            Assert.That(rig.Headset.In, Is.False, "the display stays mono until the countdown ends");
            Assert.That(rig.Show().Body, Does.Contain("switches in 10 s"));

            // The headset prompt counts down by itself.
            rig.Seconds(OnboardingFlow.HeadsetCountdown);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Home));
            Assert.That(rig.Headset.In, Is.True);
            Assert.That(rig.Flow.Room.Id, Is.EqualTo("scan_1"));
            Assert.That(rig.Flow.PromptsAnswered, Is.EqualTo(3), "Start, Done, Looks right");
            Assert.That(rig.Show().Hidden, Is.True, "nothing of onboarding is drawn in the headset");
            Assert.That(rig.Events.Has("onboarding.step step=home"), Is.True);
        }

        [Test]
        public void Build_WaitsForTheWalkerToBePlaced_ButNotForEver()
        {
            var rig = new Rig();
            rig.Rooms.RoomInForce = false;
            rig.ToConfirm();
            rig.Press(OnboardingAction.Accept);
            rig.Frames(10);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Build));
            rig.Rooms.RoomInForce = true;
            rig.Frames();
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Headset));

            var stuck = new Rig();
            stuck.Rooms.RoomInForce = false;
            stuck.ToConfirm();
            stuck.Press(OnboardingAction.Accept);
            stuck.Seconds(OnboardingFlow.BuildTimeout + 0.5f);
            Assert.That(stuck.Flow.Step, Is.EqualTo(OnboardingStep.Headset));
        }

        // ---- the camera permission ----------------------------------------------------------------------------

        [Test]
        public void CameraAlreadyGranted_IsNotAskedAgain()
        {
            var rig = new Rig();
            rig.Camera.Access = CameraAccess.Granted;
            rig.Flow.Begin();
            rig.Press(OnboardingAction.Start);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Station));
            Assert.That(rig.Camera.Requests, Is.EqualTo(0));
        }

        [Test]
        public void CameraDenied_Explains_AndRetries()
        {
            var rig = new Rig();
            rig.Camera.Answer = CameraAccess.Denied;
            rig.Flow.Begin();
            rig.Press(OnboardingAction.Start);
            Assert.That(rig.Show().Body, Does.Contain("Nothing the camera sees is stored"));
            rig.Camera.AnswerPrompt();
            rig.Frames(3);

            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Camera));
            Assert.That(rig.Flow.Notice, Is.EqualTo(OnboardingNotice.CameraDenied));
            OnboardingScreen screen = rig.Show();
            Assert.That(screen.Notice, Does.Contain("Settings > ATLAS > Camera"));
            Assert.That(screen.Body, Does.Contain("cannot track you"));
            Assert.That(screen.Offers(OnboardingAction.RetryCamera), Is.True);

            rig.Press(OnboardingAction.OpenSettings);
            Assert.That(rig.Camera.SettingsOpened, Is.EqualTo(1));

            // Switched on in Settings: the next try is granted and the flow goes on.
            rig.Camera.Answer = CameraAccess.Granted;
            rig.Press(OnboardingAction.RetryCamera);
            Assert.That(rig.Camera.Requests, Is.EqualTo(2));
            Assert.That(rig.Flow.Notice, Is.EqualTo(OnboardingNotice.None));
            rig.Camera.AnswerPrompt();
            rig.Frames();
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Station));
        }

        [Test]
        public void CameraDeniedEarlier_ExplainsAtOnce_AndBackReturnsToTheSafetyNote()
        {
            var rig = new Rig();
            rig.Camera.Access = CameraAccess.Denied;
            rig.Flow.Begin();
            rig.Press(OnboardingAction.Start);
            Assert.That(rig.Flow.Notice, Is.EqualTo(OnboardingNotice.CameraDenied));
            Assert.That(rig.Camera.Requests, Is.EqualTo(0));

            rig.Press(OnboardingAction.Back);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Welcome));
            Assert.That(rig.Flow.Notice, Is.EqualTo(OnboardingNotice.None));
        }

        // ---- the Station ----------------------------------------------------------------------------------------

        [Test]
        public void NoStation_ContinuesWithoutIt()
        {
            var rig = new Rig();
            rig.Camera.Access = CameraAccess.Granted;
            rig.Flow.Begin();
            rig.Press(OnboardingAction.Start);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Station));
            OnboardingScreen screen = rig.Show();
            Assert.That(screen.PrimaryAction, Is.EqualTo(OnboardingAction.ContinueWithoutStation), "offered from the first moment");
            Assert.That(screen.Body, Does.Contain("Connecting"));
            Assert.That(screen.AddressField, Is.True);

            // 15 s: where the Local Network switch is. 60 s: the attempts have stopped.
            rig.Station.Phase = PairingPhase.Hint;
            rig.Frames();
            Assert.That(rig.Show().Notice, Does.Contain("Local Network"));
            rig.Station.Phase = PairingPhase.TimedOut;
            rig.Frames();
            Assert.That(rig.Flow.Notice, Is.EqualTo(OnboardingNotice.StationTimedOut));
            Assert.That(rig.Show().Offers(OnboardingAction.RetryStation), Is.True);
            rig.Press(OnboardingAction.RetryStation);
            Assert.That(rig.Station.Begins, Is.EqualTo(2));
            Assert.That(rig.Flow.Notice, Is.EqualTo(OnboardingNotice.None));

            rig.Press(OnboardingAction.ContinueWithoutStation);
            Assert.That(rig.Station.Skips, Is.EqualTo(1));
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Sweep));
        }

        [Test]
        public void StationThatConnectsLate_MovesOnByItself()
        {
            var rig = new Rig();
            rig.Camera.Access = CameraAccess.Granted;
            rig.Flow.Begin();
            rig.Press(OnboardingAction.Start);
            rig.Seconds(5f);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Station));
            rig.Station.Phase = PairingPhase.Connected;
            rig.Seconds(OnboardingFlow.StationDwell);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Sweep));
            Assert.That(rig.Flow.PromptsAnswered, Is.EqualTo(1));
        }

        [Test]
        public void TypedAddress_IsCheckedBeforeItIsUsed()
        {
            var rig = new Rig();
            rig.Camera.Access = CameraAccess.Granted;
            rig.Flow.Begin();
            rig.Press(OnboardingAction.Start);

            Assert.That(rig.Flow.SetStationAddress("station.local"), Is.False);
            Assert.That(rig.Flow.Notice, Is.EqualTo(OnboardingNotice.AddressRefused));
            Assert.That(rig.Station.Address, Is.EqualTo("192.168.0.10:9050"));

            Assert.That(rig.Flow.SetStationAddress("192.168.1.20"), Is.True);
            Assert.That(rig.Station.Address, Is.EqualTo("192.168.1.20:9050"));
            Assert.That(rig.Station.Begins, Is.EqualTo(2), "pairing starts again with the new address");
            Assert.That(rig.Flow.Notice, Is.EqualTo(OnboardingNotice.None));
        }

        [Test]
        public void BuildWithNoLink_SkipsTheStationStep()
        {
            var rig = new Rig();
            rig.Station.Available = false;
            rig.Camera.Access = CameraAccess.Granted;
            rig.Flow.Begin();
            rig.Press(OnboardingAction.Start);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Sweep));
            Assert.That(rig.Station.Begins, Is.EqualTo(0));
        }

        [Test]
        public void StationPairing_WrapsThePairingController()
        {
            var controller = new PairingController(new MemoryPairingStore());
            var station = new StationPairing(controller);
            Assert.That(station.Available, Is.True);
            Assert.That(station.Address, Is.EqualTo("192.168.0.10:9050"));
            station.Begin();
            Assert.That(station.Phase, Is.EqualTo(PairingPhase.Trying));
            controller.Tick(16000000L, LinkState.Offline);
            Assert.That(station.Phase, Is.EqualTo(PairingPhase.Hint));
            controller.Tick(17000000L, LinkState.Connected);
            Assert.That(station.Connected, Is.True);
            Assert.That(station.TrySetAddress("nonsense"), Is.False);
            Assert.That(station.TrySetAddress("atlas://10.0.0.7:9050"), Is.True);
            Assert.That(station.Address, Is.EqualTo("10.0.0.7:9050"));

            var none = new StationPairing(null);
            Assert.That(none.Available, Is.False);
            Assert.That(none.Connected, Is.False);
            none.Begin();
            none.Skip();
            Assert.That(none.TrySetAddress("10.0.0.7"), Is.False);
        }

        // ---- a saved room ---------------------------------------------------------------------------------------

        [Test]
        public void SavedRoom_IsOffered_AndRelocalises()
        {
            var rig = new Rig();
            rig.Rooms.HasSavedRoom = true;
            rig.ToRoomStep();
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Room));
            OnboardingScreen screen = rig.Show();
            Assert.That(screen.PrimaryAction, Is.EqualTo(OnboardingAction.UseSavedRoom));
            Assert.That(screen.Offers(OnboardingAction.ScanAutomatic), Is.True);
            Assert.That(screen.Offers(OnboardingAction.ScanManual), Is.True);

            rig.Press(OnboardingAction.UseSavedRoom);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Relocalise));
            rig.Seconds(4f);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Relocalise));
            Assert.That(rig.Rooms.Seconds, Is.GreaterThan(4f), "the world map store is ticked while it works");

            rig.Rooms.Relocalise = RelocaliseOutcome.Ready;
            rig.Frames(2);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Headset));
            Assert.That(rig.Rooms.Calls, Does.Contain("activate Saved saved_1"));
            Assert.That(rig.Scan.Calls, Is.Empty, "no scan on the pre-scan path");
            rig.Press(OnboardingAction.HeadsetNow);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Home));
            Assert.That(rig.Flow.PromptsAnswered, Is.EqualTo(3), "Start, Use the saved room, Switch now");
        }

        [Test]
        public void SavedRoom_NotRecognised_FallsBackToAScan()
        {
            var rig = new Rig();
            rig.Rooms.HasSavedRoom = true;
            rig.ToRoomStep();
            rig.Press(OnboardingAction.UseSavedRoom);
            rig.Rooms.Relocalise = RelocaliseOutcome.Failed;
            rig.Frames();
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Sweep));
            Assert.That(rig.Flow.Notice, Is.EqualTo(OnboardingNotice.RoomNotRecognised));
            Assert.That(rig.Show().Notice, Does.Contain("scanned anew"));
            Assert.That(rig.Scan.Calls, Is.EqualTo(new[] { "begin auto" }));
        }

        [Test]
        public void SavedRoom_NoAnswerInTime_FallsBackToAScan()
        {
            var rig = new Rig();
            rig.Rooms.HasSavedRoom = true;
            rig.ToRoomStep();
            rig.Press(OnboardingAction.UseSavedRoom);
            rig.Seconds(OnboardingFlow.RelocaliseLimit - 1f);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Relocalise));
            rig.Seconds(1.5f);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Sweep));
            Assert.That(rig.Rooms.Calls, Does.Contain("cancel"));
            Assert.That(rig.Flow.Notice, Is.EqualTo(OnboardingNotice.RoomNotRecognised));
            Assert.That(OnboardingFlow.RelocaliseLimit, Is.GreaterThan(Relocaliser.Timeout));
        }

        [Test]
        public void SavedRoom_CanBeCancelled_AndScannedAnew()
        {
            var rig = new Rig();
            rig.Rooms.HasSavedRoom = true;
            rig.ToRoomStep();
            rig.Press(OnboardingAction.UseSavedRoom);
            rig.Press(OnboardingAction.Back);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Room));
            Assert.That(rig.Rooms.Relocalise, Is.EqualTo(RelocaliseOutcome.None));
            rig.Press(OnboardingAction.ScanAutomatic);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Sweep));
        }

        // ---- the scan -------------------------------------------------------------------------------------------

        [Test]
        public void ScanFailure_OffersTheManualOutline()
        {
            var rig = new Rig();
            rig.ToRoomStep();
            rig.Scan.FindFloor();
            rig.Scan.Found = null;
            rig.Scan.FailWith = ScanFailure.TooSmall;
            rig.Press(OnboardingAction.DoneSweeping);
            rig.Frames();

            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Room));
            Assert.That(rig.Flow.Notice, Is.EqualTo(OnboardingNotice.TooSmall));
            OnboardingScreen screen = rig.Show();
            Assert.That(screen.Notice, Does.Contain("too small"));
            Assert.That(screen.PrimaryAction, Is.EqualTo(OnboardingAction.ScanManual));
            Assert.That(screen.Offers(OnboardingAction.ScanAutomatic), Is.True);

            rig.Scan.Found = Rooms.Make("manual_1");
            rig.Press(OnboardingAction.ScanManual);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Sweep));
            Assert.That(rig.Scan.LastBeginManual, Is.True);
            Assert.That(rig.Flow.CornerMode, Is.True);
            Assert.That(rig.Flow.Notice, Is.EqualTo(OnboardingNotice.None));
            Assert.That(rig.Show().Title, Is.EqualTo("Outline the room"));

            rig.Press(OnboardingAction.AddCorner);
            rig.Press(OnboardingAction.AddCorner);
            Assert.That(rig.Flow.TapScreen(400f, 300f), Is.True);
            rig.Press(OnboardingAction.AddCorner);
            rig.Press(OnboardingAction.UndoCorner);
            Assert.That(rig.Scan.Progress.CornerCount, Is.EqualTo(3));
            Assert.That(rig.Show().Body, Does.Contain("Corners: 3"));
            rig.Press(OnboardingAction.DoneSweeping);
            rig.Frames();
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Confirm));
        }

        [Test]
        public void EveryScanFailure_HasItsOwnWords()
        {
            foreach (ScanFailure failure in new[] { ScanFailure.NoFloor, ScanFailure.TooSmall, ScanFailure.TrackingLost })
            {
                var rig = new Rig();
                rig.ToRoomStep();
                rig.Scan.Failure = failure;
                rig.Scan.State = ScanState.Failed;
                rig.Frames();
                Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Room), failure.ToString());
                Assert.That(rig.Show().Notice, Is.Not.Empty, failure.ToString());
                Assert.That(rig.Show().PrimaryAction, Is.EqualTo(OnboardingAction.ScanManual), failure.ToString());
            }
        }

        [Test]
        public void Done_BeforeAFloorOrThreeCorners_IsExplained_NotFailed()
        {
            var rig = new Rig();
            rig.ToRoomStep();
            int prompts = rig.Flow.PromptsAnswered;
            Assert.That(rig.Press(OnboardingAction.DoneSweeping), Is.False);
            Assert.That(rig.Scan.Calls, Has.No.Member("end"));
            Assert.That(rig.Flow.Notice, Is.EqualTo(OnboardingNotice.NeedFloor));
            Assert.That(rig.Flow.PromptsAnswered, Is.EqualTo(prompts));
            Assert.That(rig.Show().Body, Does.Contain("Floor: not found yet"));

            rig.Scan.FindFloor();
            rig.Frames();
            Assert.That(rig.Flow.Notice, Is.EqualTo(OnboardingNotice.None), "the notice goes when the floor is found");
        }

        [Test]
        public void TapsOnTheFloor_AddCorners_OnlyWhileOutliningByHand()
        {
            var rig = new Rig();
            rig.ToRoomStep();
            Assert.That(rig.Flow.TapScreen(100f, 100f), Is.False, "a stray touch during the automatic sweep places no corner");
            Assert.That(rig.Scan.Calls, Has.No.Member("corner tap"));

            // "Outline by hand" during a sweep needs no restart.
            rig.Press(OnboardingAction.ScanManual);
            Assert.That(rig.Flow.CornerMode, Is.True);
            Assert.That(rig.Scan.Calls, Is.EqualTo(new[] { "begin auto" }));
            Assert.That(rig.Flow.TapScreen(100f, 100f), Is.True);
            rig.Scan.CornerHits = false;
            Assert.That(rig.Flow.TapScreen(100f, 900f), Is.False, "a tap that hits no floor adds nothing");
            Assert.That(rig.Scan.Progress.CornerCount, Is.EqualTo(1));
        }

        [Test]
        public void SessionAskingForCorners_SwitchesToTheManualOutlineByItself()
        {
            var rig = new Rig();
            rig.ToRoomStep();
            rig.Scan.ProgressNow.Hint = ScanHint.TapCorners;
            rig.Frames();
            Assert.That(rig.Flow.CornerMode, Is.True);
            Assert.That(rig.Show().Offers(OnboardingAction.AddCorner), Is.True);
        }

        [Test]
        public void Sweep_TicksTheSession_AndShowsTheComputation()
        {
            var rig = new Rig();
            rig.ToRoomStep();
            int ticks = rig.Scan.Ticks;
            rig.Frames(5);
            Assert.That(rig.Scan.Ticks, Is.EqualTo(ticks + 5));
            rig.Scan.State = ScanState.Processing;
            rig.Frames();
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Sweep));
            OnboardingScreen screen = rig.Show();
            Assert.That(screen.Body, Does.Contain("Working out"));
            Assert.That(screen.ButtonCount, Is.EqualTo(0));
        }

        // ---- the outline ----------------------------------------------------------------------------------------

        [Test]
        public void SmallRoom_SaysStandingMode()
        {
            var rig = new Rig();
            rig.Scan.Found = new RoomModel { Id = "tiny", LapSide = 1.4f, Standing = true, FreeRect = new FreeRect(1.4f, 1.4f) };
            rig.ToConfirm();
            Assert.That(rig.Show().Body, Does.Contain("Free floor: 1.4 x 1.4 m").And.Contain("standing mode"));
            Assert.That(rig.Show().Body, Does.Not.Contain("walk a square"));

            rig.Press(OnboardingAction.Accept);
            rig.Frames(2);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Headset));
            Assert.That(rig.Show().Body, Does.Contain("standing mode"));
        }

        [Test]
        public void Adjust_DragsCorners_AndARefusedOutlineIsExplained()
        {
            var rig = new Rig();
            rig.ToConfirm();
            Assert.That(rig.Flow.MoveCorner(0, new float2(1f, 1f)), Is.False, "corners move only while adjusting");

            int prompts = rig.Flow.PromptsAnswered;
            rig.Press(OnboardingAction.Adjust);
            Assert.That(rig.Flow.Adjusting, Is.True);
            Assert.That(rig.Show().Body, Does.Contain("Drag a corner"));
            Assert.That(rig.Flow.MoveCorner(2, new float2(1f, 1f)), Is.True);
            Assert.That(rig.Scan.Calls, Does.Contain("move 2"));

            rig.Scan.MoveRefused = true;
            rig.Flow.MoveCorner(1, new float2(-5f, 9f));
            Assert.That(rig.Flow.Notice, Is.EqualTo(OnboardingNotice.OutlineInvalid));
            Assert.That(rig.Show().Notice, Does.Contain("not changed"));
            rig.Scan.MoveRefused = false;
            rig.Flow.MoveCorner(1, new float2(0.5f, 0.5f));
            Assert.That(rig.Flow.Notice, Is.EqualTo(OnboardingNotice.None));

            // The session recomputes after a correction: the step stays.
            rig.Scan.State = ScanState.Processing;
            rig.Frames();
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Confirm));
            Assert.That(rig.Flow.Do(OnboardingAction.Accept), Is.False, "nothing to accept while the room is recomputed");
            rig.Scan.State = ScanState.AwaitingConfirm;

            rig.Press(OnboardingAction.Adjust);
            Assert.That(rig.Flow.Adjusting, Is.False);
            Assert.That(rig.Flow.PromptsAnswered, Is.EqualTo(prompts), "adjusting is not a question");
            rig.Press(OnboardingAction.Accept);
            rig.Frames(2);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Headset));
        }

        // ---- back and cancel ------------------------------------------------------------------------------------

        [Test]
        public void Back_FromTheSweep_CancelsTheScan()
        {
            var rig = new Rig();
            rig.ToRoomStep();
            rig.Press(OnboardingAction.Back);
            Assert.That(rig.Scan.Calls, Does.Contain("cancel"));
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Room));
            OnboardingScreen screen = rig.Show();
            Assert.That(screen.PrimaryAction, Is.EqualTo(OnboardingAction.ScanAutomatic));
            Assert.That(screen.Offers(OnboardingAction.Back), Is.False, "no room to go back to");
            rig.Frames(3);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Room));
        }

        [Test]
        public void ScanAgain_FromTheOutline_CancelsIt()
        {
            var rig = new Rig();
            rig.ToConfirm();
            rig.Press(OnboardingAction.Back);
            Assert.That(rig.Scan.State, Is.EqualTo(ScanState.Idle));
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Room));
            Assert.That(rig.Rooms.Calls, Is.Empty, "no room was activated");
        }

        [Test]
        public void ScanAgain_FromTheHeadsetPrompt_AndBackToIt()
        {
            var rig = new Rig();
            rig.ToHeadsetPrompt();
            rig.Seconds(3f);
            rig.Press(OnboardingAction.Back);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Room));
            rig.Seconds(OnboardingFlow.HeadsetCountdown);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Room), "no countdown runs behind the room choice");
            Assert.That(rig.Rooms.Calls.Count, Is.EqualTo(1), "the room in force is not activated a second time");

            // The room in force is kept: back returns to the prompt with a fresh countdown.
            rig.Press(OnboardingAction.Back);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Headset));
            Assert.That(rig.Flow.CountdownSeconds, Is.EqualTo(10));
        }

        [Test]
        public void ActionsOfAnotherStep_AreIgnored()
        {
            var rig = new Rig();
            Assert.That(rig.Flow.Do(OnboardingAction.Start), Is.False, "not begun");
            rig.Flow.Tick(1f);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Idle));
            rig.Flow.Begin();
            rig.Flow.Begin();
            foreach (OnboardingAction action in new[]
                     {
                         OnboardingAction.Accept, OnboardingAction.DoneSweeping, OnboardingAction.HeadsetNow,
                         OnboardingAction.UseSavedRoom, OnboardingAction.ScanAutomatic, OnboardingAction.Back,
                         OnboardingAction.ContinueWithoutStation, OnboardingAction.AddCorner, OnboardingAction.Adjust,
                         OnboardingAction.UseSampleRoom, OnboardingAction.UseAddress, OnboardingAction.None,
                     })
                Assert.That(rig.Flow.Do(action), Is.False, action.ToString());
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Welcome));
            Assert.That(rig.Flow.PromptsAnswered, Is.EqualTo(0));
            Assert.That(rig.Flow.SetStationAddress("192.168.0.11"), Is.False);
            Assert.That(rig.Flow.Rescan(), Is.False);
        }

        // ---- the headset ----------------------------------------------------------------------------------------

        [Test]
        public void HeadsetPrompt_CountsDown_OrTakesTheLever()
        {
            var rig = new Rig();
            rig.ToHeadsetPrompt();
            Assert.That(rig.Flow.CountdownSeconds, Is.EqualTo(10));
            int version = rig.Flow.Version;
            rig.Seconds(3f);
            Assert.That(rig.Flow.CountdownSeconds, Is.EqualTo(7).Within(1));
            Assert.That(rig.Flow.Version, Is.GreaterThan(version), "the screen is told when the number changes");
            Assert.That(rig.Headset.In, Is.False);

            // The lever is a touch anywhere on the screen.
            Assert.That(rig.Flow.TapScreen(960f, 540f), Is.True);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Home));
            Assert.That(rig.Headset.Enters, Is.EqualTo(1));
            rig.Seconds(OnboardingFlow.HeadsetCountdown);
            Assert.That(rig.Headset.Enters, Is.EqualTo(1));
        }

        [Test]
        public void HeadsetRefused_IsExplained_AndNotRetriedEveryFrame()
        {
            var rig = new Rig();
            rig.Headset.Allowed = false;
            rig.ToHeadsetPrompt();
            rig.Press(OnboardingAction.HeadsetNow);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Headset));
            Assert.That(rig.Flow.Notice, Is.EqualTo(OnboardingNotice.HeadsetRefused));
            OnboardingScreen screen = rig.Show();
            Assert.That(screen.Notice, Does.Contain("Bump Guard"));
            Assert.That(screen.Offers(OnboardingAction.HeadsetNow), Is.False);
            rig.Seconds(OnboardingFlow.HeadsetCountdown + 1f);
            Assert.That(rig.Headset.Enters, Is.EqualTo(1));
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Headset));
        }

        [Test]
        public void Rescan_FromHome_ReturnsToTheHand()
        {
            var rig = new Rig();
            rig.ToHeadsetPrompt();
            rig.Press(OnboardingAction.HeadsetNow);
            Assert.That(rig.Flow.Rescan(), Is.True);
            Assert.That(rig.Headset.In, Is.False);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Room));
            rig.Scan.Found = Rooms.Make("scan_2");
            rig.Press(OnboardingAction.ScanAutomatic);
            rig.Scan.FindFloor();
            rig.Press(OnboardingAction.DoneSweeping);
            rig.Frames();
            rig.Press(OnboardingAction.Accept);
            rig.Frames(2);
            rig.Press(OnboardingAction.HeadsetNow);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Home));
            Assert.That(rig.Flow.Room.Id, Is.EqualTo("scan_2"));
            Assert.That(rig.Headset.In, Is.True);
        }

        [Test]
        public void CameraPictureOnly_WhileTheRealRoomIsLookedAt()
        {
            foreach (OnboardingStep step in new[]
                     { OnboardingStep.Room, OnboardingStep.Relocalise, OnboardingStep.Sweep, OnboardingStep.Confirm })
                Assert.That(OnboardingFlow.CameraPictureOnly(step), Is.True, step.ToString());
            foreach (OnboardingStep step in new[]
                     {
                         OnboardingStep.Idle, OnboardingStep.Welcome, OnboardingStep.Camera, OnboardingStep.Station,
                         OnboardingStep.Build, OnboardingStep.Headset, OnboardingStep.Home,
                     })
                Assert.That(OnboardingFlow.CameraPictureOnly(step), Is.False, step.ToString());
        }

        [Test]
        public void Rescan_HidesTheOldRoom_UntilTheNewOneIsBuilt()
        {
            var rig = new Rig();
            rig.ToHeadsetPrompt();
            rig.Press(OnboardingAction.HeadsetNow);
            Assert.That(OnboardingFlow.CameraPictureOnly(rig.Flow.Step), Is.False);
            Assert.That(rig.Flow.Rescan(), Is.True);
            Assert.That(OnboardingFlow.CameraPictureOnly(rig.Flow.Step), Is.True);
            rig.Scan.Found = Rooms.Make("scan_2");
            rig.Press(OnboardingAction.ScanAutomatic);
            Assert.That(OnboardingFlow.CameraPictureOnly(rig.Flow.Step), Is.True);
            rig.Scan.FindFloor();
            rig.Press(OnboardingAction.DoneSweeping);
            rig.Frames();
            Assert.That(OnboardingFlow.CameraPictureOnly(rig.Flow.Step), Is.True);
            rig.Press(OnboardingAction.Accept);
            rig.Frames(2);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Headset));
            Assert.That(OnboardingFlow.CameraPictureOnly(rig.Flow.Step), Is.False);
        }

        // ---- no phone: fixtures and rooms from outside (contract README F9) ----------------------------------

        [Test]
        public void SampleRoom_OneClick_FromTheFirstScreen_ToHome()
        {
            var rig = new Rig();
            rig.Flow.SamplesAllowed = true;
            rig.Flow.Begin();
            rig.Press(OnboardingAction.UseSampleRoom);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Build));
            rig.Frames();
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Home), "no countdown after the one click");
            Assert.That(rig.Headset.In, Is.True);
            Assert.That(rig.Flow.Room.Id, Is.EqualTo("sample_3x3"));
            Assert.That(rig.Camera.Requests, Is.EqualTo(0));
            Assert.That(rig.Flow.PromptsAnswered, Is.EqualTo(1));
        }

        [Test]
        public void SampleRoom_IsNotOfferedToAWalker()
        {
            var rig = new Rig();
            rig.Flow.Begin();
            Assert.That(rig.Show().Offers(OnboardingAction.UseSampleRoom), Is.False);
            Assert.That(rig.Flow.Do(OnboardingAction.UseSampleRoom), Is.False);

            var missing = new Rig();
            missing.Flow.SamplesAllowed = true;
            missing.Scan.Sample = null;
            missing.Flow.Begin();
            Assert.That(missing.Press(OnboardingAction.UseSampleRoom), Is.False);
            Assert.That(missing.Flow.Notice, Is.EqualTo(OnboardingNotice.NoSampleRoom));
            Assert.That(missing.Flow.Step, Is.EqualTo(OnboardingStep.Welcome));
        }

        [Test]
        public void SampleRoom_DuringAScan_DropsTheScan()
        {
            var rig = new Rig();
            rig.Flow.SamplesAllowed = true;
            rig.ToRoomStep();
            rig.Press(OnboardingAction.Back);
            rig.Press(OnboardingAction.UseSampleRoom);
            rig.Frames();
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Home));
        }

        [Test]
        public void RoomGivenBeforeTheScan_IsUsed_WithTheHeadsetPrompt()
        {
            // The Editor with no AR provider: dev.room has made a fixture the session's result at boot.
            var rig = new Rig();
            rig.Scan.Give(Rooms.Make("sample_3x3"));
            rig.ToRoomStep();
            rig.Frames();
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Headset));
            Assert.That(rig.Scan.Calls, Is.Empty, "nothing to scan");
            Assert.That(rig.Rooms.Calls, Is.EqualTo(new[] { "activate Scan sample_3x3" }));
        }

        [Test]
        public void RoomGivenWhileWalking_ReplacesTheRoom_AndIsActivatedOnce()
        {
            var rig = new Rig();
            rig.ToHeadsetPrompt();
            rig.Press(OnboardingAction.HeadsetNow);
            rig.Frames(5);
            Assert.That(rig.Rooms.Calls.Count, Is.EqualTo(1));

            rig.Scan.Give(Rooms.Make("sample_l"));
            rig.Frames(5);
            Assert.That(rig.Rooms.Calls.Count, Is.EqualTo(2));
            Assert.That(rig.Flow.Room.Id, Is.EqualTo("sample_l"));
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Home));
            Assert.That(rig.Headset.Leaves, Is.EqualTo(0));
        }

        [Test]
        public void RoomThatCannotBeActivated_ReturnsToTheRoomChoice()
        {
            var rig = new Rig();
            rig.Rooms.ActivationFails = true;
            rig.ToConfirm();
            rig.Press(OnboardingAction.Accept);
            rig.Frames(3);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Room));
            Assert.That(rig.Flow.Notice, Is.EqualTo(OnboardingNotice.BuildFailed));
            Assert.That(rig.Rooms.Calls.Count, Is.EqualTo(1), "not tried again every frame");
            Assert.That(rig.Show().PrimaryAction, Is.EqualTo(OnboardingAction.ScanAutomatic));
        }

        // ---- the words ------------------------------------------------------------------------------------------

        [Test]
        public void Welcome_CarriesTheSafetyNote()
        {
            var rig = new Rig();
            rig.Flow.Begin();
            OnboardingScreen screen = rig.Show();
            Assert.That(screen.Body, Does.Contain("Clear the floor").And.Contain("level floor").And.Contain("spotter"));
            Assert.That(screen.ButtonCount, Is.EqualTo(1));
        }

        [Test]
        public void EveryNotice_HasWords_AndEveryScreenFits()
        {
            foreach (OnboardingNotice notice in System.Enum.GetValues(typeof(OnboardingNotice)))
            {
                string words = OnboardingScreens.NoticeText(notice);
                Assert.That(words.Length > 0, Is.EqualTo(notice != OnboardingNotice.None), notice.ToString());
                // The built-in font is certain only for plain ASCII.
                foreach (char c in words) Assert.That(c, Is.LessThan((char)127), notice.ToString());
            }

            var rig = new Rig();
            rig.Flow.SamplesAllowed = true;
            rig.Rooms.HasSavedRoom = true;
            rig.Flow.Begin();
            Check(rig);
            rig.Press(OnboardingAction.Start);
            Check(rig);
            rig.Camera.AnswerPrompt();
            rig.Frames();
            Check(rig);
            rig.Press(OnboardingAction.ContinueWithoutStation);
            Check(rig);
            rig.Press(OnboardingAction.ScanManual);
            Check(rig);
            rig.Scan.FindFloor();
            rig.Press(OnboardingAction.DoneSweeping);
            rig.Frames();
            Check(rig);
            rig.Press(OnboardingAction.Accept);
            rig.Frames();
            Check(rig);
            rig.Frames();
            Check(rig);
            Assert.That(rig.Flow.Step, Is.EqualTo(OnboardingStep.Headset));
        }

        static void Check(Rig rig)
        {
            OnboardingScreen screen = rig.Show();
            string step = rig.Flow.Step.ToString();
            Assert.That(screen.Hidden, Is.False, step);
            Assert.That(screen.Title, Is.Not.Empty, step);
            Assert.That(screen.ButtonCount, Is.LessThanOrEqualTo(OnboardingScreen.MaxButtons), step);
            int primaries = 0;
            for (int i = 0; i < screen.ButtonCount; i++)
            {
                Assert.That(screen.Buttons[i].Label, Is.Not.Empty, step);
                if (screen.Buttons[i].Primary) primaries++;
            }
            Assert.That(primaries, Is.LessThanOrEqualTo(1), step + ": one way on");
            foreach (char c in screen.Title + screen.Body) Assert.That(c, Is.LessThan((char)127), step);
            Assert.That(OnboardingScreens.StepName(rig.Flow.Step), Is.Not.EqualTo("unknown"));
        }

        [Test]
        public void StepsAreLogged_InOrder()
        {
            var rig = new Rig();
            rig.ToHeadsetPrompt();
            string[] expected = { "welcome", "camera", "station", "sweep", "confirm", "build", "headset" };
            Assert.That(rig.Events.Lines.Count, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
                Assert.That(rig.Events.Lines[i], Is.EqualTo("onboarding.step step=" + expected[i]));
        }

        [Test]
        public void SteadyState_InHome_ChangesNothing()
        {
            var rig = new Rig();
            rig.ToHeadsetPrompt();
            rig.Press(OnboardingAction.HeadsetNow);
            int version = rig.Flow.Version;
            int scanTicks = rig.Scan.Ticks;
            rig.Seconds(5f);
            Assert.That(rig.Flow.Version, Is.EqualTo(version), "nothing is redrawn while walking");
            Assert.That(rig.Scan.Ticks, Is.EqualTo(scanTicks), "the scan session rests");
        }
    }
}
