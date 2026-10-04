using System;
using Atlas.Input;
using Atlas.Rendering;
using Atlas.Safety;
using Atlas.Scan;
using Atlas.World;

namespace Atlas.App
{
    /// <summary>
    /// The flow while no onboarding is installed, and the Editor-first path at all times (contract README
    /// F9): a room comes from a fixture, a file or a scan through the one scan session, is handed over, and
    /// the walker is put in the headset view. With onboarding installed this class only serves the dev.room
    /// choice and leaves the scan, the hand-over and the headset prompt to it.
    /// </summary>
    public sealed class RehearsalFlow : IAfterFrameStep
    {
        readonly ScanSession _session;
        readonly RoomHandOver _handOver;
        readonly IStereoRig _rig;
        readonly BumpGuard _guard;
        readonly IAtlasInput _input;
        readonly ScanSettings _scan;

        int _queuedRoom = -1;
        string _queuedFile;
        bool _resultPending;
        bool _inHeadset;

        public RehearsalFlow(ScanSession session, RoomHandOver handOver, IStereoRig rig, BumpGuard guard,
            IAtlasInput input, ScanSettings scan)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (handOver == null) throw new ArgumentNullException(nameof(handOver));
            _session = session;
            _handOver = handOver;
            _rig = rig;
            _guard = guard;
            _input = input;
            _scan = scan ?? new ScanSettings();
            _session.StateChanged += OnScanState;
        }

        /// <summary>False when onboarding owns the scan and the hand-over.</summary>
        public bool OwnsFlow { get; set; } = true;
        /// <summary>Go to the headset view as soon as a room is active (the Editor); on a phone the walker asks for it.</summary>
        public bool AutoHeadset { get; set; }
        /// <summary>False refuses the headset view: a phone build with no Bump Guard (lane A contract, section 7.2).</summary>
        public bool HeadsetAllowed { get; set; } = true;
        public bool InHeadset { get { return _inHeadset; } }
        public IEventLog Events { get; set; }
        /// <summary>The last thing that went wrong, for the overlay; null when nothing did.</summary>
        public string LastError { get; private set; }

        /// <summary>dev.room: 0 is "none"; 1 and up pick a fixture. Applied at the next step 8b.</summary>
        public void SetRoomChoice(int index)
        {
            _queuedRoom = index;
        }

        /// <summary>A RoomModel file to use as the room. Applied at the next step 8b.</summary>
        public void UseRoomFile(string path)
        {
            _queuedFile = path;
        }

        /// <summary>Starts a scan with the options of scan.route. The phone goes back to the hand.</summary>
        public void BeginScan()
        {
            LeaveHeadset();
            _session.Begin(_scan.Options);
        }

        /// <summary>The walker has the headset on: Bump Guard is armed, the eyes draw, gaze input is live.</summary>
        public bool EnterHeadset()
        {
            if (_inHeadset) return true;
            if (!HeadsetAllowed)
            {
                LastError = "Bump Guard missing";
                return false;
            }
            if (_guard != null) _guard.Armed = true;
            if (_rig != null)
            {
                _rig.SetMode(DisplayMode.Headset);
                // The rig itself refuses the headset view while it is locked (GuardedStereoRig).
                if (_rig.Mode != DisplayMode.Headset)
                {
                    if (_guard != null) _guard.Armed = false;
                    LastError = "Bump Guard missing";
                    return false;
                }
            }
            if (_input != null) _input.PushContext(InputContext.Space);
            _inHeadset = true;
            Log("headset.on");
            return true;
        }

        /// <summary>The phone is back in the hand.</summary>
        public void LeaveHeadset()
        {
            if (!_inHeadset) return;
            _inHeadset = false;
            if (_guard != null) _guard.Armed = false;
            if (_rig != null) _rig.SetMode(DisplayMode.Hand);
            if (_input != null) _input.PopContext(InputContext.Space);
            Log("headset.off");
        }

        public void AfterFrame(in FrameContext frame)
        {
            if (_queuedFile != null)
            {
                string path = _queuedFile;
                _queuedFile = null;
                UseFile(path);
            }
            if (_queuedRoom >= 0)
            {
                int index = _queuedRoom;
                _queuedRoom = -1;
                UseFixture(index);
            }

            if (!OwnsFlow) return;

            ScanState state = _session.State;
            if (state != ScanState.Idle && state != ScanState.Done && state != ScanState.Failed)
                _session.Tick(frame.DeltaTime);

            if (_resultPending)
            {
                _resultPending = false;
                RoomModel room = _session.Result;
                if (room != null)
                {
                    LastError = null;
                    _handOver.Activate(room, _session.ResultAnchor);
                    if (AutoHeadset) EnterHeadset();
                }
            }
        }

        void UseFixture(int index)
        {
            string[] names = RoomFixtures.All;
            if (index <= 0 || index > names.Length) return;
            try
            {
                if (!_session.UseFixture(names[index - 1])) LastError = "no fixture " + names[index - 1];
                // Choosing a room again while the session is already Done raises no state change.
                else if (OwnsFlow) _resultPending = true;
            }
            catch (Exception e) when (e is ArgumentException || e is FormatException)
            {
                LastError = e.Message;
                Log("room.loadFailed", e.Message);
            }
        }

        void UseFile(string path)
        {
            try
            {
                _session.UseRoom(RoomFixtures.LoadFromFile(path));
                if (OwnsFlow) _resultPending = true;
            }
            catch (Exception e) when (e is ArgumentException || e is FormatException || e is System.IO.IOException ||
                                      e is UnauthorizedAccessException)
            {
                LastError = e.Message;
                Log("room.loadFailed", e.Message);
            }
        }

        // Raised inside the session's own calls; the hand-over waits for step 8b of the frame.
        void OnScanState(ScanState state)
        {
            if (state == ScanState.Done && OwnsFlow) _resultPending = true;
        }

        void Log(string ev, string detail = null)
        {
            IEventLog events = Events;
            if (events == null) return;
            if (detail == null) events.Log(ev);
            else events.Log(ev, "detail", detail);
        }
    }
}
