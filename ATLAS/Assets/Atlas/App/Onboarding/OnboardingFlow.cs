using System;
using Atlas.Scan;
using Atlas.Telemetry;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.App.Onboarding
{
    /// <summary>
    /// Onboarding with the phone in the hand (Master Plan task P1-C12, section 4.2; lane C contract, section
    /// 10.4): from launch to Home in the headset. An engine-free state machine: the camera permission, the
    /// Station, the scan session, the saved rooms and the headset view are reached through small interfaces,
    /// and time comes in through Tick.
    /// <para>The default path of a first-time walker asks three things: Start (the safety note), Done (the
    /// sweep) and Looks right (the outline). The system's own permission prompts are raised on the way, in the
    /// hand, and the headset step counts down by itself.</para>
    /// </summary>
    public sealed class OnboardingFlow
    {
        /// <summary>Seconds "Station connected" stays on the screen before the flow moves on.</summary>
        public const float StationDwell = 1.0f;
        /// <summary>Seconds from the headset prompt to the stereo view, unless the lever is pressed first.</summary>
        public const float HeadsetCountdown = 10f;
        /// <summary>Seconds the build step waits for the walker to be placed in the room frame.</summary>
        public const float BuildTimeout = 3f;
        /// <summary>What a sweep of an ordinary room takes, seconds: the hint on the sweep screen.</summary>
        public const float SweepBudget = 30f;
        /// <summary>Seconds after which onboarding stops waiting for the saved room by itself.</summary>
        public const float RelocaliseLimit = Relocaliser.Timeout + 3f;

        readonly ICameraPermission _camera;
        readonly IStationPairing _station;
        readonly IOnboardingScan _scan;
        readonly IOnboardingRooms _rooms;
        readonly IOnboardingHeadset _headset;

        float _stepSeconds;
        float _dwell;
        bool _straightToHeadset;
        bool _headsetRefused;
        int _shownSeconds = -1;
        // The scan result that has already been activated, so that a room given from outside (the Station's
        // dev.room, a fixture) is told apart from the one in force.
        RoomModel _seenResult;

        public OnboardingFlow(ICameraPermission camera, IStationPairing station, IOnboardingScan scan,
            IOnboardingRooms rooms, IOnboardingHeadset headset)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (station == null) throw new ArgumentNullException(nameof(station));
            if (scan == null) throw new ArgumentNullException(nameof(scan));
            if (rooms == null) throw new ArgumentNullException(nameof(rooms));
            if (headset == null) throw new ArgumentNullException(nameof(headset));
            _camera = camera;
            _station = station;
            _scan = scan;
            _rooms = rooms;
            _headset = headset;
        }

        public OnboardingStep Step { get; private set; }
        public OnboardingNotice Notice { get; private set; }
        /// <summary>How many questions the walker has answered so far. Corner edits are not questions.</summary>
        public int PromptsAnswered { get; private set; }
        /// <summary>Rises whenever something the screen shows has changed.</summary>
        public int Version { get; private set; }
        /// <summary>Corners are being placed by hand: a tap on the floor adds one.</summary>
        public bool CornerMode { get; private set; }
        /// <summary>The outline under review may be dragged.</summary>
        public bool Adjusting { get; private set; }
        /// <summary>Whole seconds left on the headset prompt.</summary>
        public int CountdownSeconds { get; private set; }
        /// <summary>The room in force, from the build step on.</summary>
        public RoomModel Room { get; private set; }
        /// <summary>The Editor and development builds may take a fixture room with one click (contract README F9).</summary>
        public bool SamplesAllowed { get; set; }
        public IEventLog Events { get; set; }
        /// <summary>The TripoSG switch; null when the build has none, and the switch is not offered.</summary>
        public IOnboardingForge Forge { get; set; }

        public ICameraPermission Camera { get { return _camera; } }
        public IStationPairing Station { get { return _station; } }
        public IOnboardingScan Scan { get { return _scan; } }
        public IOnboardingRooms Rooms { get { return _rooms; } }

        /// <summary>Raised after Step has changed.</summary>
        public event Action<OnboardingStep> StepChanged;

        /// <summary>The first screen. Does nothing once the flow has started.</summary>
        public void Begin()
        {
            if (Step == OnboardingStep.Idle) Go(OnboardingStep.Welcome);
        }

        /// <summary>
        /// A button of the screen. True when the action was taken; an action that does not belong to the
        /// step on show is ignored.
        /// </summary>
        public bool Do(OnboardingAction action)
        {
            if (!Apply(action)) return false;
            if (action != OnboardingAction.AddCorner && action != OnboardingAction.UndoCorner &&
                action != OnboardingAction.Adjust && action != OnboardingAction.ToggleForge) PromptsAnswered++;
            Touch();
            return true;
        }

        /// <summary>The typed Station address ("Change IP"). A refused text leaves the stored address as it was.</summary>
        public bool SetStationAddress(string text)
        {
            if (Step != OnboardingStep.Station) return false;
            if (!_station.TrySetAddress(text))
            {
                SetNotice(OnboardingNotice.AddressRefused);
                return false;
            }
            _dwell = 0f;
            _station.Begin();
            SetNotice(OnboardingNotice.None);
            Touch();
            return true;
        }

        /// <summary>A tap on the camera picture. Adds a corner while corners are placed by hand; switches to the headset view on the headset prompt.</summary>
        public bool TapScreen(float screenX, float screenY)
        {
            if (Step == OnboardingStep.Headset) return Do(OnboardingAction.HeadsetNow);
            if (Step != OnboardingStep.Sweep || !CornerMode) return false;
            if (!_scan.TryAddCorner(screenX, screenY)) return false;
            Touch();
            return true;
        }

        /// <summary>A corner of the outline under review was dragged to a floor point of the session frame.</summary>
        public bool MoveCorner(int index, float2 floorPoint)
        {
            if (Step != OnboardingStep.Confirm || !Adjusting) return false;
            _scan.MoveCorner(index, floorPoint);
            SetNotice(_scan.Failure == ScanFailure.OutlineInvalid ? OnboardingNotice.OutlineInvalid : OnboardingNotice.None);
            Touch();
            return true;
        }

        /// <summary>
        /// True in the steps that look at the real room through the camera: the room choice, the wait for a
        /// saved room, the sweep and the outline (lane C contract, section 10.4, step 4). The virtual room of
        /// an earlier scan must not be drawn over the camera picture there, or a rescan sweeps blind.
        /// </summary>
        public static bool CameraPictureOnly(OnboardingStep step)
        {
            return step == OnboardingStep.Room || step == OnboardingStep.Relocalise ||
                   step == OnboardingStep.Sweep || step == OnboardingStep.Confirm;
        }

        /// <summary>From Home back to the hand for a new scan (lane C contract, section 14.2).</summary>
        public bool Rescan()
        {
            if (Step != OnboardingStep.Home) return false;
            _headset.Leave();
            SetNotice(OnboardingNotice.None);
            Go(OnboardingStep.Room);
            return true;
        }

        /// <summary>Once per frame, after the frame's own work.</summary>
        public void Tick(float deltaTime)
        {
            if (Step == OnboardingStep.Idle) return;
            if (deltaTime > 0f) _stepSeconds += deltaTime;
            _rooms.Tick(deltaTime);

            switch (Step)
            {
                case OnboardingStep.Camera: TickCamera(); break;
                case OnboardingStep.Station: TickStation(deltaTime); break;
                case OnboardingStep.Room:
                    if (NewRoomGiven()) EnterBuild(RoomSource.Scan);
                    break;
                case OnboardingStep.Relocalise: TickRelocalise(); break;
                case OnboardingStep.Sweep: TickSweep(deltaTime); break;
                case OnboardingStep.Confirm: TickConfirm(deltaTime); break;
                case OnboardingStep.Build:
                    if (_rooms.RoomInForce || _stepSeconds >= BuildTimeout) LeaveBuild();
                    break;
                case OnboardingStep.Headset: TickHeadset(); break;
                case OnboardingStep.Home:
                    // A room chosen from the Station or the overlay while walking: the space host reloads the space.
                    if (NewRoomGiven()) ActivateGiven();
                    break;
            }
        }

        // ---- actions ------------------------------------------------------------------------------------------

        bool Apply(OnboardingAction action)
        {
            switch (action)
            {
                case OnboardingAction.Start:
                    if (Step != OnboardingStep.Welcome) return false;
                    EnterCamera();
                    return true;

                case OnboardingAction.RetryCamera:
                    if (Step != OnboardingStep.Camera || _camera.Access == CameraAccess.Asking) return false;
                    Notice = OnboardingNotice.None;
                    _camera.Request();
                    return true;

                case OnboardingAction.OpenSettings:
                    if (Step != OnboardingStep.Camera) return false;
                    _camera.OpenSettings();
                    return true;

                case OnboardingAction.ContinueWithoutStation:
                    if (Step != OnboardingStep.Station) return false;
                    _station.Skip();
                    Notice = OnboardingNotice.None;
                    EnterRoom();
                    return true;

                case OnboardingAction.RetryStation:
                    if (Step != OnboardingStep.Station) return false;
                    Notice = OnboardingNotice.None;
                    _dwell = 0f;
                    _station.Begin();
                    return true;

                case OnboardingAction.UseSavedRoom:
                    if (Step != OnboardingStep.Room || !_rooms.HasSavedRoom) return false;
                    Notice = OnboardingNotice.None;
                    _rooms.BeginRelocalise();
                    Go(OnboardingStep.Relocalise);
                    return true;

                case OnboardingAction.ScanAutomatic:
                    if (Step != OnboardingStep.Room) return false;
                    BeginScan(false);
                    return true;

                case OnboardingAction.ScanManual:
                    if (Step == OnboardingStep.Room)
                    {
                        BeginScan(true);
                        return true;
                    }
                    // During a sweep the manual outline needs no restart: corners are accepted by every route.
                    if (Step != OnboardingStep.Sweep || CornerMode) return false;
                    CornerMode = true;
                    return true;

                case OnboardingAction.UseSampleRoom:
                    return UseSampleRoom();

                case OnboardingAction.AddCorner:
                    if (Step != OnboardingStep.Sweep) return false;
                    CornerMode = true;
                    _scan.AddCornerAtFeet();
                    return true;

                case OnboardingAction.UndoCorner:
                    if (Step != OnboardingStep.Sweep) return false;
                    _scan.RemoveLastCorner();
                    return true;

                case OnboardingAction.DoneSweeping:
                    return EndSweep();

                case OnboardingAction.Accept:
                    if (Step != OnboardingStep.Confirm || _scan.State != ScanState.AwaitingConfirm) return false;
                    Adjusting = false;
                    Notice = OnboardingNotice.None;
                    _scan.Confirm();
                    return true;

                case OnboardingAction.Adjust:
                    if (Step != OnboardingStep.Confirm) return false;
                    Adjusting = !Adjusting;
                    return true;

                case OnboardingAction.Back:
                    return Back();

                case OnboardingAction.HeadsetNow:
                    if (Step != OnboardingStep.Headset) return false;
                    EnterHeadset();
                    return true;

                case OnboardingAction.ToggleForge:
                    return ToggleForge();
            }
            return false;
        }

        // Offered on the screens before and during the sweep, while the objects' photos are still to be sent.
        bool ToggleForge()
        {
            IOnboardingForge forge = Forge;
            if (forge == null || !forge.Available) return false;
            if (Step != OnboardingStep.Welcome && Step != OnboardingStep.Room && Step != OnboardingStep.Sweep) return false;
            bool enabled = !forge.Enabled;
            forge.SetEnabled(enabled);
            IEventLog events = Events;
            if (events != null) events.Log("onboarding.forge", "triposg", enabled ? "on" : "off");
            return true;
        }

        bool EndSweep()
        {
            if (Step != OnboardingStep.Sweep || _scan.State != ScanState.Sweeping) return false;
            ScanProgress progress = _scan.Progress;
            if (!progress.FloorFound && progress.CornerCount < 3)
            {
                // Ending now would fail the scan for want of a floor: say what is missing instead.
                SetNotice(OnboardingNotice.NeedFloor);
                return false;
            }
            Notice = OnboardingNotice.None;
            _scan.EndSweep();
            return true;
        }

        bool Back()
        {
            switch (Step)
            {
                case OnboardingStep.Camera:
                    Notice = OnboardingNotice.None;
                    Go(OnboardingStep.Welcome);
                    return true;
                case OnboardingStep.Relocalise:
                    _rooms.CancelRelocalise();
                    Go(OnboardingStep.Room);
                    return true;
                case OnboardingStep.Sweep:
                case OnboardingStep.Confirm:
                    _scan.Cancel();
                    Notice = OnboardingNotice.None;
                    Go(OnboardingStep.Room);
                    return true;
                case OnboardingStep.Headset:
                    // "Scan again": the room stays in force until another one replaces it.
                    Notice = OnboardingNotice.None;
                    Go(OnboardingStep.Room);
                    return true;
                case OnboardingStep.Room:
                    // Back out of a rescan: the room in force is kept.
                    if (Room == null) return false;
                    Notice = OnboardingNotice.None;
                    LeaveBuild();
                    return true;
            }
            return false;
        }

        bool UseSampleRoom()
        {
            if (!SamplesAllowed || Step == OnboardingStep.Idle || Step >= OnboardingStep.Build) return false;
            if (Step == OnboardingStep.Relocalise) _rooms.CancelRelocalise();
            if (!_scan.UseSampleRoom())
            {
                SetNotice(OnboardingNotice.NoSampleRoom);
                return false;
            }
            if (Step == OnboardingStep.Station) _station.Skip();
            Notice = OnboardingNotice.None;
            _straightToHeadset = true;
            EnterBuild(RoomSource.Scan);
            return true;
        }

        // ---- steps --------------------------------------------------------------------------------------------

        void EnterCamera()
        {
            Notice = OnboardingNotice.None;
            CameraAccess access = _camera.Access;
            if (access == CameraAccess.Granted)
            {
                EnterStation();
                return;
            }
            Go(OnboardingStep.Camera);
            if (access == CameraAccess.NotAsked) _camera.Request();
            else if (access == CameraAccess.Denied) Notice = OnboardingNotice.CameraDenied;
        }

        void TickCamera()
        {
            CameraAccess access = _camera.Access;
            if (access == CameraAccess.Granted) EnterStation();
            else if (access == CameraAccess.Denied) SetNotice(OnboardingNotice.CameraDenied);
        }

        void EnterStation()
        {
            Notice = OnboardingNotice.None;
            if (!_station.Available)
            {
                EnterRoom();
                return;
            }
            _dwell = 0f;
            Go(OnboardingStep.Station);
            // The first connect attempt is what raises the Local Network prompt: here, in the hand.
            _station.Begin();
        }

        void TickStation(float deltaTime)
        {
            if (_station.Connected)
            {
                if (_dwell == 0f)
                {
                    Notice = OnboardingNotice.None;
                    Touch();
                }
                _dwell += math.max(deltaTime, 1e-4f);
                if (_dwell >= StationDwell) EnterRoom();
                return;
            }
            _dwell = 0f;
            PairingPhase phase = _station.Phase;
            if (phase == PairingPhase.Hint) SetNotice(OnboardingNotice.StationHint);
            else if (phase == PairingPhase.TimedOut) SetNotice(OnboardingNotice.StationTimedOut);
        }

        // After the Station: a room that is already there is used; a saved room is offered; otherwise the
        // scan starts by itself with the automatic route.
        void EnterRoom()
        {
            if (NewRoomGiven())
            {
                EnterBuild(RoomSource.Scan);
                return;
            }
            if (_rooms.HasSavedRoom)
            {
                Go(OnboardingStep.Room);
                return;
            }
            BeginScan(false);
        }

        void BeginScan(bool manualOnly)
        {
            Notice = OnboardingNotice.None;
            CornerMode = manualOnly;
            Adjusting = false;
            _scan.Begin(manualOnly);
            Go(OnboardingStep.Sweep);
        }

        void TickRelocalise()
        {
            RelocaliseOutcome outcome = _rooms.Relocalise;
            if (outcome == RelocaliseOutcome.Ready)
            {
                EnterBuild(RoomSource.Saved);
                return;
            }
            if (outcome == RelocaliseOutcome.Busy && _stepSeconds < RelocaliseLimit)
            {
                TouchEverySecond();
                return;
            }
            // Not recognised, or no answer in time: a new scan, with no further question.
            if (outcome == RelocaliseOutcome.Busy) _rooms.CancelRelocalise();
            BeginScan(false);
            Notice = OnboardingNotice.RoomNotRecognised;
        }

        void TickSweep(float deltaTime)
        {
            _scan.Tick(deltaTime);
            switch (_scan.State)
            {
                case ScanState.Failed:
                    ScanFailed();
                    return;
                case ScanState.AwaitingConfirm:
                    Notice = OnboardingNotice.None;
                    Go(OnboardingStep.Confirm);
                    return;
                case ScanState.Done:
                    EnterBuild(RoomSource.Scan);
                    return;
                case ScanState.Idle:
                    Go(OnboardingStep.Room);
                    return;
            }
            // With no LiDAR mesh, or no floor after a while, the session asks for corners: the manual
            // outline takes over without a question.
            if (!CornerMode && _scan.Progress.Hint == ScanHint.TapCorners)
            {
                CornerMode = true;
                Touch();
            }
            if (Notice == OnboardingNotice.NeedFloor)
            {
                ScanProgress progress = _scan.Progress;
                if (progress.FloorFound || progress.CornerCount >= 3) SetNotice(OnboardingNotice.None);
            }
        }

        void TickConfirm(float deltaTime)
        {
            _scan.Tick(deltaTime);
            switch (_scan.State)
            {
                case ScanState.Failed:
                    ScanFailed();
                    return;
                case ScanState.Done:
                    EnterBuild(RoomSource.Scan);
                    return;
                case ScanState.Idle:
                    Go(OnboardingStep.Room);
                    return;
            }
        }

        // The room choice comes up with the reason, and the manual outline as the way on.
        void ScanFailed()
        {
            Adjusting = false;
            switch (_scan.Failure)
            {
                case ScanFailure.TooSmall: Notice = OnboardingNotice.TooSmall; break;
                case ScanFailure.TrackingLost: Notice = OnboardingNotice.TrackingLost; break;
                case ScanFailure.OutlineInvalid: Notice = OnboardingNotice.OutlineInvalid; break;
                default: Notice = OnboardingNotice.NoFloor; break;
            }
            Go(OnboardingStep.Room);
        }

        void EnterBuild(RoomSource source)
        {
            Adjusting = false;
            RoomModel room = _rooms.Activate(source);
            _seenResult = _scan.State == ScanState.Done ? _scan.Result : _seenResult;
            if (room == null)
            {
                _straightToHeadset = false;
                Notice = OnboardingNotice.BuildFailed;
                Go(OnboardingStep.Room);
                return;
            }
            Room = room;
            Notice = OnboardingNotice.None;
            Go(OnboardingStep.Build);
        }

        void LeaveBuild()
        {
            if (_straightToHeadset)
            {
                _straightToHeadset = false;
                EnterHeadset();
                return;
            }
            _headsetRefused = false;
            CountdownSeconds = (int)math.ceil(HeadsetCountdown);
            Go(OnboardingStep.Headset);
        }

        void TickHeadset()
        {
            if (NewRoomGiven()) ActivateGiven();
            if (_headsetRefused) return;
            int left = (int)math.ceil(HeadsetCountdown - _stepSeconds);
            if (left <= 0)
            {
                EnterHeadset();
                return;
            }
            if (left != CountdownSeconds)
            {
                CountdownSeconds = left;
                Touch();
            }
        }

        void EnterHeadset()
        {
            if (!_headset.Enter())
            {
                _headsetRefused = true;
                Notice = OnboardingNotice.HeadsetRefused;
                if (Step != OnboardingStep.Headset) Go(OnboardingStep.Headset);
                else Touch();
                return;
            }
            Notice = OnboardingNotice.None;
            Go(OnboardingStep.Home);
        }

        bool NewRoomGiven()
        {
            if (_scan.State != ScanState.Done) return false;
            RoomModel result = _scan.Result;
            return result != null && !ReferenceEquals(result, _seenResult);
        }

        void ActivateGiven()
        {
            RoomModel room = _rooms.Activate(RoomSource.Scan);
            _seenResult = _scan.Result;
            if (room == null) return;
            Room = room;
            Touch();
        }

        // ---- bookkeeping --------------------------------------------------------------------------------------

        void Go(OnboardingStep step)
        {
            Step = step;
            _stepSeconds = 0f;
            _shownSeconds = -1;
            Touch();
            IEventLog events = Events;
            if (events != null) events.Log("onboarding.step", "step", OnboardingScreens.StepName(step));
            Action<OnboardingStep> changed = StepChanged;
            if (changed != null) changed(step);
        }

        void SetNotice(OnboardingNotice notice)
        {
            if (Notice == notice) return;
            Notice = notice;
            Touch();
        }

        void TouchEverySecond()
        {
            int seconds = (int)_stepSeconds;
            if (seconds == _shownSeconds) return;
            _shownSeconds = seconds;
            Touch();
        }

        /// <summary>Seconds in the step on show.</summary>
        public float StepSeconds { get { return _stepSeconds; } }

        void Touch()
        {
            Version++;
        }
    }
}
