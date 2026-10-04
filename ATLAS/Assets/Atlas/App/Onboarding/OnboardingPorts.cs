using Atlas.Scan;
using Atlas.Telemetry;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.App.Onboarding
{
    /// <summary>Where onboarding stands (lane C contract, section 10.4). Phone in the hand until Home.</summary>
    public enum OnboardingStep : byte
    {
        Idle = 0,
        /// <summary>The safety note.</summary>
        Welcome = 1,
        /// <summary>The camera permission, asked for in the hand.</summary>
        Camera = 2,
        /// <summary>The Station: the address, the Local Network prompt, connect or continue without.</summary>
        Station = 3,
        /// <summary>The room choice: shown when a saved room exists, after a failed scan and after a back.</summary>
        Room = 4,
        /// <summary>The saved room is being recognised.</summary>
        Relocalise = 5,
        /// <summary>The sweep, and the short computation that follows it.</summary>
        Sweep = 6,
        /// <summary>The outline on the floor: accept or adjust.</summary>
        Confirm = 7,
        /// <summary>The room is activated; the walker is placed in the room frame.</summary>
        Build = 8,
        /// <summary>"Put the phone in the headset", with a countdown.</summary>
        Headset = 9,
        /// <summary>The display is stereo and Home is entered. Onboarding rests.</summary>
        Home = 10,
    }

    /// <summary>What the screen has to explain on top of the step's own text.</summary>
    public enum OnboardingNotice : byte
    {
        None = 0,
        CameraDenied = 1,
        StationHint = 2,
        StationTimedOut = 3,
        AddressRefused = 4,
        RoomNotRecognised = 5,
        NoFloor = 6,
        TooSmall = 7,
        TrackingLost = 8,
        OutlineInvalid = 9,
        /// <summary>"Done" was pressed before a floor or three corners were there.</summary>
        NeedFloor = 10,
        BuildFailed = 11,
        HeadsetRefused = 12,
        NoSampleRoom = 13,
    }

    /// <summary>Everything a button of the onboarding screen can ask for.</summary>
    public enum OnboardingAction : byte
    {
        None = 0,
        Start = 1,
        RetryCamera = 2,
        OpenSettings = 3,
        ContinueWithoutStation = 4,
        RetryStation = 5,
        /// <summary>Handled by the view, which reads the typed text and calls SetStationAddress.</summary>
        UseAddress = 6,
        UseSavedRoom = 7,
        ScanAutomatic = 8,
        ScanManual = 9,
        /// <summary>The Editor's one click: a fixture room, straight to the headset view (contract README F9).</summary>
        UseSampleRoom = 10,
        AddCorner = 11,
        UndoCorner = 12,
        DoneSweeping = 13,
        Accept = 14,
        Adjust = 15,
        Back = 16,
        HeadsetNow = 17,
        /// <summary>Switches the Station's TripoSG models of the scanned objects on or off (forge.backend).</summary>
        ToggleForge = 18,
    }

    public enum CameraAccess : byte { NotAsked = 0, Asking = 1, Granted = 2, Denied = 3 }

    /// <summary>Where the room that is being activated comes from.</summary>
    public enum RoomSource : byte
    {
        /// <summary>The scan session's result: a confirmed scan, or a fixture or file given to the session.</summary>
        Scan = 0,
        /// <summary>The saved room that was just relocalised.</summary>
        Saved = 1,
    }

    public enum RelocaliseOutcome : byte { None = 0, Busy = 1, Ready = 2, Failed = 3 }

    /// <summary>The camera permission (Master Plan Appendix C.6: accepted in hand, not in the headset).</summary>
    public interface ICameraPermission
    {
        CameraAccess Access { get; }
        /// <summary>Raises the system prompt the first time; afterwards the system answers at once.</summary>
        void Request();
        /// <summary>Opens the system settings of the app, where a refused permission is switched on.</summary>
        void OpenSettings();
    }

    /// <summary>Pairing with the Station as onboarding uses it (lane D contract, section 5.6).</summary>
    public interface IStationPairing
    {
        /// <summary>False when the build has no link at all: the step is skipped.</summary>
        bool Available { get; }
        string Address { get; }
        PairingPhase Phase { get; }
        bool Connected { get; }
        /// <summary>Starts the one-second connect attempts. The first one raises the Local Network prompt.</summary>
        void Begin();
        void Skip();
        bool TrySetAddress(string text);
    }

    /// <summary>The scan session as onboarding drives it (lane C contract, section 10.3).</summary>
    public interface IOnboardingScan
    {
        ScanState State { get; }
        ScanFailure Failure { get; }
        ScanProgress Progress { get; }
        /// <summary>The room the outline under review gives; null outside AwaitingConfirm.</summary>
        RoomModel Proposal { get; }
        /// <summary>The finished room; null unless State is Done.</summary>
        RoomModel Result { get; }

        /// <summary>manualOnly: route 1 alone. Otherwise the routes the scan parameters allow, FloorScan and up.</summary>
        void Begin(bool manualOnly);
        void Tick(float deltaTime);
        void EndSweep();
        void Confirm();
        void Cancel();

        void AddCornerAtFeet();
        /// <summary>A corner at the floor point under a screen point, pixels.</summary>
        bool TryAddCorner(float screenX, float screenY);
        void RemoveLastCorner();
        /// <summary>Moves a corner of the outline under review; floorPoint is in the session frame.</summary>
        void MoveCorner(int index, float2 floorPoint);

        /// <summary>Makes a fixture room the result with no scan. False when there is none.</summary>
        bool UseSampleRoom();
    }

    /// <summary>Saved rooms, relocalising, and making a room the active one (lane C contract, sections 12 and 14.2).</summary>
    public interface IOnboardingRooms
    {
        /// <summary>A saved room this phone can return to.</summary>
        bool HasSavedRoom { get; }
        void BeginRelocalise();
        RelocaliseOutcome Relocalise { get; }
        void CancelRelocalise();
        /// <summary>Every frame: relocalising and the world map save advance here.</summary>
        void Tick(float deltaTime);

        /// <summary>Rows 1 to 4 of section 14.2. Returns the room now in force, or null when there was none to activate.</summary>
        RoomModel Activate(RoomSource source);
        /// <summary>The walker has been placed in the room frame: Home may be entered.</summary>
        bool RoomInForce { get; }
    }

    /// <summary>
    /// Whether the scanned objects are sent to the Forge, where the Station's TripoSG makes a model of each
    /// (the parameter forge.backend). Off, every object keeps its built-in mesh.
    /// </summary>
    public interface IOnboardingForge
    {
        /// <summary>False when the build has no asset client to send photos with: the switch is not offered.</summary>
        bool Available { get; }
        bool Enabled { get; }
        void SetEnabled(bool enabled);
    }

    /// <summary>Row 5 of section 14.2: the stereo view, Bump Guard armed, gaze input, Home.</summary>
    public interface IOnboardingHeadset
    {
        /// <summary>False when the headset view is refused (a phone build with no Bump Guard).</summary>
        bool Enter();
        /// <summary>The phone is back in the hand, for a rescan.</summary>
        void Leave();
    }
}
