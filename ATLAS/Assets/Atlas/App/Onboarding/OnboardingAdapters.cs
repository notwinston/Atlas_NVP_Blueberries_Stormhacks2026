using System;
using System.Collections.Generic;
using Atlas.Link;
using Atlas.Scan;
using Atlas.Telemetry;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Atlas.App.Onboarding
{
    /// <summary>
    /// The camera permission on the phone (Master Plan Appendix C.6). The AR session owns the camera, so its
    /// manager's answer counts as well as the system's. Where there is no camera to ask for (no AR provider,
    /// the Editor) the permission reads as granted.
    /// </summary>
    sealed class UnityCameraPermission : ICameraPermission
    {
        readonly ARCameraManager _camera;
        readonly bool _needed;
        AsyncOperation _request;
        bool _asked;

        public UnityCameraPermission(ArSessionRig rig)
        {
            _camera = rig != null ? rig.Camera : null;
            _needed = rig != null && rig.HasAr && !Application.isEditor;
        }

        public CameraAccess Access
        {
            get
            {
                if (!_needed) return CameraAccess.Granted;
                if (Application.HasUserAuthorization(UserAuthorization.WebCam)) return CameraAccess.Granted;
                if (_camera != null && _camera.permissionGranted) return CameraAccess.Granted;
                if (_request != null && !_request.isDone) return CameraAccess.Asking;
                return _asked ? CameraAccess.Denied : CameraAccess.NotAsked;
            }
        }

        public void Request()
        {
            _asked = true;
            _request = Application.RequestUserAuthorization(UserAuthorization.WebCam);
        }

        public void OpenSettings()
        {
            // The app's own page of the iOS Settings.
            Application.OpenURL("app-settings:");
        }
    }

    /// <summary>Lane D's pairing controller behind onboarding's interface. Engine-free. A null controller is "no Station in this build".</summary>
    public sealed class StationPairing : IStationPairing
    {
        readonly PairingController _pairing;

        public StationPairing(PairingController pairing)
        {
            _pairing = pairing;
        }

        public bool Available { get { return _pairing != null; } }
        public string Address { get { return _pairing != null ? _pairing.Address : ""; } }
        public PairingPhase Phase { get { return _pairing != null ? _pairing.Phase : PairingPhase.Skipped; } }
        public bool Connected { get { return _pairing != null && _pairing.Phase == PairingPhase.Connected; } }

        public void Begin()
        {
            if (_pairing != null) _pairing.BeginPairing();
        }

        public void Skip()
        {
            if (_pairing != null) _pairing.Skip();
        }

        public bool TrySetAddress(string text)
        {
            return _pairing != null && _pairing.TrySetAddress(text);
        }
    }

    /// <summary>The scan session behind onboarding's interface, with the routes the scan parameters allow.</summary>
    sealed class SessionScan : IOnboardingScan
    {
        readonly ScanSession _session;
        readonly ScanSettings _settings;

        public SessionScan(ScanSession session, ScanSettings settings)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            _session = session;
            _settings = settings ?? new ScanSettings();
        }

        public ScanState State { get { return _session.State; } }
        public ScanFailure Failure { get { return _session.Failure; } }
        public ScanProgress Progress { get { return _session.Progress; } }

        public RoomModel Proposal
        {
            get
            {
                OutlineProposal proposal = _session.State == ScanState.AwaitingConfirm ? _session.Proposal : null;
                return proposal != null ? proposal.Preview : null;
            }
        }

        public RoomModel Result { get { return _session.State == ScanState.Done ? _session.Result : null; } }

        public void Begin(bool manualOnly)
        {
            ScanOptions options = _settings.Options;
            if (manualOnly)
            {
                options.UseMesh = false;
                options.UseBoxes = false;
            }
            _session.Begin(in options);
        }

        public void Tick(float deltaTime) { _session.Tick(deltaTime); }
        public void EndSweep() { _session.EndSweep(); }
        public void Confirm() { _session.Confirm(); }
        public void Cancel() { _session.Cancel(); }
        public void AddCornerAtFeet() { _session.AddCornerAtFeet(); }
        public bool TryAddCorner(float screenX, float screenY) { return _session.TryAddCorner(new Vector2(screenX, screenY)); }
        public void RemoveLastCorner() { _session.RemoveLastCorner(); }
        public void MoveCorner(int index, float2 floorPoint) { _session.MoveCorner(index, floorPoint); }

        public bool UseSampleRoom()
        {
            try
            {
                return _session.UseFixture(RoomFixtures.Sample3x3);
            }
            catch (Exception e) when (e is ArgumentException || e is FormatException)
            {
                Debug.LogWarning("Atlas: the sample room could not be loaded: " + e.Message);
                return false;
            }
        }
    }

    /// <summary>
    /// Saved rooms, relocalising and the room hand-over on the phone (lane C contract, sections 12 and 14.2).
    /// A room is written to the phone only for a team pre-scan (scan.teamPrescan): the scan of any other
    /// walker stays in memory and is gone when the app closes (Master Plan section 13.5).
    /// </summary>
    sealed class AppRooms : IOnboardingRooms
    {
        readonly ScanSystems _scan;
        readonly WorldMapStore _worldMaps;
        readonly RoomHandOver _handOver;
        readonly IWalkerReset _walker;
        readonly ScanSettings _settings;
        readonly List<RoomSummary> _list = new List<RoomSummary>(8);

        bool _looked, _hasSaved;
        RelocaliseOutcome _outcome;
        RelocaliseResult _result;

        public AppRooms(ScanSystems scan, WorldMapStore worldMaps, RoomHandOver handOver, IWalkerReset walker, ScanSettings settings)
        {
            _scan = scan;
            _worldMaps = worldMaps;
            _handOver = handOver;
            _walker = walker;
            _settings = settings ?? new ScanSettings();
        }

        public bool HasSavedRoom
        {
            get
            {
                // The folder is read once, in the hand, and again after a room has been saved.
                if (_looked) return _hasSaved;
                _looked = true;
                _hasSaved = false;
                try
                {
                    RoomSummary summary;
                    if (RoomIndex.TryMostRecent(_scan.Rooms, _scan.Rooms.LastRoomId, _list, out summary))
                        // On the phone a saved room is of use only with its world map; elsewhere it is used as it is.
                        _hasSaved = summary.HasWorldMap || !_worldMaps.Supported;
                }
                catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
                {
                    Debug.LogWarning("Atlas: the saved rooms could not be read: " + e.Message);
                }
                return _hasSaved;
            }
        }

        public RelocaliseOutcome Relocalise { get { return _outcome; } }

        public void BeginRelocalise()
        {
            _outcome = RelocaliseOutcome.Busy;
            _worldMaps.BeginRelocaliseLast(OnRelocalised);
        }

        void OnRelocalised(RelocaliseResult result)
        {
            _result = result;
            _outcome = result.RoomUsable && result.Room != null ? RelocaliseOutcome.Ready : RelocaliseOutcome.Failed;
        }

        public void CancelRelocalise()
        {
            if (_outcome == RelocaliseOutcome.Busy) _worldMaps.Cancel();
            _outcome = RelocaliseOutcome.None;
        }

        public void Tick(float deltaTime)
        {
            if (_worldMaps.Busy) _worldMaps.Tick(deltaTime);
        }

        public RoomModel Activate(RoomSource source)
        {
            RoomModel room;
            object anchor;
            if (source == RoomSource.Saved)
            {
                room = _outcome == RelocaliseOutcome.Ready ? _result.Room : null;
                anchor = _result.Anchor;
                _outcome = RelocaliseOutcome.None;
            }
            else
            {
                ScanSession session = _scan.Session;
                room = session.State == ScanState.Done ? session.Result : null;
                anchor = session.ResultAnchor;
            }
            if (room == null) return null;

            try
            {
                // Rows 1 and 2: the anchor, then the room to the space host and everything that listens to it.
                _handOver.Activate(room, anchor);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return null;
            }

            // Row 3, the pre-scan path: only a team pre-scan is kept, with its world map.
            if (source == RoomSource.Scan && _settings.TeamPrescan && room.Quality.Route != ScanRoute.Fixture)
            {
                _scan.Rooms.Save(room);
                _scan.Rooms.LastRoomId = room.Id;
                _worldMaps.BeginSave(room.Id, null);
                _looked = false;
            }
            // Row 4, the object photos, is the scan session's own: it hands them to the asset client.
            return room;
        }

        public bool RoomInForce { get { return !_walker.RoomResetPending; } }
    }

    /// <summary>The headset view through the app's one place that switches it (lane C contract, section 14.2, row 5).</summary>
    sealed class AppHeadset : IOnboardingHeadset
    {
        readonly RehearsalFlow _flow;
        readonly RoomHandOver _handOver;

        public AppHeadset(RehearsalFlow flow, RoomHandOver handOver)
        {
            _flow = flow;
            _handOver = handOver;
        }

        public bool Enter()
        {
            // Stereo view, Bump Guard armed, gaze input: refused on a phone with no Bump Guard.
            if (!_flow.EnterHeadset()) return false;
            // Home. When the walker is not placed yet the hand-over asks again at the end of the next frame.
            _handOver.EnterNow();
            return true;
        }

        public void Leave()
        {
            _flow.LeaveHeadset();
        }
    }
}
