using System.Collections.Generic;
using Atlas.App.Onboarding;
using Atlas.Scan;
using Atlas.Telemetry;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.App.Tests.Onboarding
{
    sealed class FakeCamera : ICameraPermission
    {
        public CameraAccess Access { get; set; } = CameraAccess.NotAsked;
        /// <summary>What a request ends as.</summary>
        public CameraAccess Answer = CameraAccess.Granted;
        public int Requests, SettingsOpened;

        public void Request()
        {
            Requests++;
            Access = CameraAccess.Asking;
        }

        /// <summary>The walker answers the system prompt.</summary>
        public void AnswerPrompt() { Access = Answer; }

        public void OpenSettings() { SettingsOpened++; }
    }

    sealed class FakeStation : IStationPairing
    {
        public bool Available { get; set; } = true;
        public string Address { get; set; } = "192.168.0.10:9050";
        public PairingPhase Phase { get; set; }
        public bool Connected { get { return Phase == PairingPhase.Connected; } }
        public int Begins, Skips;
        /// <summary>The phase Begin leads to.</summary>
        public PairingPhase OnBegin = PairingPhase.Trying;

        public void Begin()
        {
            Begins++;
            Phase = OnBegin;
        }

        public void Skip()
        {
            Skips++;
            if (Phase != PairingPhase.Connected) Phase = PairingPhase.Skipped;
        }

        public bool TrySetAddress(string text)
        {
            string host;
            int port;
            if (!PairingAddress.TryParse(text, out host, out port)) return false;
            Address = PairingAddress.Format(host, port);
            return true;
        }
    }

    /// <summary>A scan session that goes where the test sends it.</summary>
    sealed class FakeScan : IOnboardingScan
    {
        public readonly List<string> Calls = new List<string>();
        public ScanState State { get; set; }
        public ScanFailure Failure { get; set; }
        public ScanProgress Progress { get { return ProgressNow; } }
        public ScanProgress ProgressNow;
        public RoomModel Proposal { get; set; }
        public RoomModel Result { get; set; }
        public RoomModel Sample = Rooms.Make("sample_3x3");
        /// <summary>The room EndSweep proposes; null makes the sweep fail with FailWith.</summary>
        public RoomModel Found = Rooms.Make("scan_1");
        public ScanFailure FailWith = ScanFailure.NoFloor;
        public bool LastBeginManual;
        public bool CornerHits = true;
        public int Ticks;

        public void Begin(bool manualOnly)
        {
            Calls.Add(manualOnly ? "begin manual" : "begin auto");
            LastBeginManual = manualOnly;
            State = ScanState.Sweeping;
            Failure = ScanFailure.None;
            Proposal = null;
            Result = null;
            ProgressNow = default(ScanProgress);
            ProgressNow.Tracking = TrackingState.Normal;
        }

        public void Tick(float deltaTime)
        {
            Ticks++;
            ProgressNow.Seconds += deltaTime;
        }

        /// <summary>The sweep sees the floor.</summary>
        public void FindFloor(float area = 9f)
        {
            ProgressNow.FloorFound = true;
            ProgressNow.FloorArea = area;
            ProgressNow.PreviewVersion++;
        }

        public void EndSweep()
        {
            Calls.Add("end");
            if (Found == null)
            {
                Failure = FailWith;
                State = ScanState.Failed;
                return;
            }
            Proposal = Found;
            State = ScanState.AwaitingConfirm;
        }

        public void Confirm()
        {
            Calls.Add("confirm");
            Result = Proposal;
            Proposal = null;
            State = ScanState.Done;
        }

        public void Cancel()
        {
            Calls.Add("cancel");
            State = ScanState.Idle;
            Proposal = null;
        }

        public void AddCornerAtFeet()
        {
            Calls.Add("corner feet");
            ProgressNow.CornerCount++;
        }

        public bool TryAddCorner(float screenX, float screenY)
        {
            Calls.Add("corner tap");
            if (!CornerHits) return false;
            ProgressNow.CornerCount++;
            return true;
        }

        public void RemoveLastCorner()
        {
            Calls.Add("undo");
            if (ProgressNow.CornerCount > 0) ProgressNow.CornerCount--;
        }

        public bool MoveRefused;

        public void MoveCorner(int index, float2 floorPoint)
        {
            Calls.Add("move " + index);
            Failure = MoveRefused ? ScanFailure.OutlineInvalid : ScanFailure.None;
        }

        public bool UseSampleRoom()
        {
            Calls.Add("sample");
            if (Sample == null) return false;
            Give(Sample);
            return true;
        }

        /// <summary>A room from outside the scan: a fixture, a file, the Station's choice.</summary>
        public void Give(RoomModel room)
        {
            Proposal = null;
            Result = room;
            State = ScanState.Done;
        }
    }

    sealed class FakeRooms : IOnboardingRooms
    {
        public readonly List<string> Calls = new List<string>();
        readonly FakeScan _scan;
        public bool HasSavedRoom { get; set; }
        public RelocaliseOutcome Relocalise { get; set; }
        public RoomModel Saved = Rooms.Make("saved_1");
        public bool RoomInForce { get; set; } = true;
        public bool ActivationFails;
        public RoomModel Active;
        public float Seconds;

        public FakeRooms(FakeScan scan) { _scan = scan; }

        public void BeginRelocalise()
        {
            Calls.Add("relocalise");
            Relocalise = RelocaliseOutcome.Busy;
        }

        public void CancelRelocalise()
        {
            Calls.Add("cancel");
            Relocalise = RelocaliseOutcome.None;
        }

        public void Tick(float deltaTime) { Seconds += deltaTime; }

        public RoomModel Activate(RoomSource source)
        {
            RoomModel room = source == RoomSource.Saved ? Saved : _scan.Result;
            Calls.Add("activate " + source + " " + (room != null ? room.Id : "none"));
            if (ActivationFails || room == null) return null;
            Active = room;
            return room;
        }
    }

    sealed class FakeHeadset : IOnboardingHeadset
    {
        public bool Allowed = true;
        public bool In;
        public int Enters, Leaves;

        public bool Enter()
        {
            Enters++;
            if (!Allowed) return false;
            In = true;
            return true;
        }

        public void Leave()
        {
            Leaves++;
            In = false;
        }
    }

    sealed class FakeForge : IOnboardingForge
    {
        public bool Available { get; set; } = true;
        public bool Enabled { get; set; } = true;
        public int Sets;

        public void SetEnabled(bool enabled)
        {
            Sets++;
            Enabled = enabled;
        }
    }

    /// <summary>The flow with all five fakes, and the walker's hands.</summary>
    sealed class Rig
    {
        public const float Frame = 1f / 60f;

        public readonly FakeCamera Camera = new FakeCamera();
        public readonly FakeStation Station = new FakeStation();
        public readonly FakeScan Scan = new FakeScan();
        public readonly FakeRooms Rooms;
        public readonly FakeHeadset Headset = new FakeHeadset();
        public readonly FakeForge Forge = new FakeForge();
        public readonly FakeEvents Events = new FakeEvents();
        public readonly OnboardingFlow Flow;
        public readonly OnboardingScreen Screen = new OnboardingScreen();
        readonly System.Text.StringBuilder _text = new System.Text.StringBuilder();

        public Rig()
        {
            Rooms = new FakeRooms(Scan);
            Flow = new OnboardingFlow(Camera, Station, Scan, Rooms, Headset) { Events = Events, Forge = Forge };
        }

        public void Frames(int count = 1)
        {
            for (int i = 0; i < count; i++) Flow.Tick(Frame);
        }

        public void Seconds(float seconds)
        {
            Frames((int)math.ceil(seconds / Frame) + 1);
        }

        /// <summary>The screen as the walker sees it now.</summary>
        public OnboardingScreen Show()
        {
            OnboardingScreens.Fill(Flow, Screen, _text);
            return Screen;
        }

        /// <summary>Presses a button, which must be on the screen.</summary>
        public bool Press(OnboardingAction action)
        {
            NUnit.Framework.Assert.That(Show().Offers(action), NUnit.Framework.Is.True,
                "the " + Flow.Step + " screen has no button for " + action);
            return Flow.Do(action);
        }

        /// <summary>Welcome, camera granted, Station connected: up to the sweep (or the room choice).</summary>
        public void ToRoomStep()
        {
            Camera.Answer = CameraAccess.Granted;
            Station.OnBegin = PairingPhase.Connected;
            Flow.Begin();
            Press(OnboardingAction.Start);
            Camera.AnswerPrompt();
            Frames();
            Seconds(OnboardingFlow.StationDwell);
        }

        /// <summary>Up to the outline under review.</summary>
        public void ToConfirm()
        {
            ToRoomStep();
            Scan.FindFloor();
            Frames();
            Press(OnboardingAction.DoneSweeping);
            Frames();
        }

        /// <summary>Up to the headset prompt.</summary>
        public void ToHeadsetPrompt()
        {
            ToConfirm();
            Press(OnboardingAction.Accept);
            Frames(2);
        }
    }
}
