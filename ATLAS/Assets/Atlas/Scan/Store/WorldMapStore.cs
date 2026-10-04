using System;
using System.Threading.Tasks;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ArTrackingState = UnityEngine.XR.ARSubsystems.TrackingState;
using TrackingState = Atlas.World.TrackingState;

namespace Atlas.Scan
{
    /// <summary>How a relocalisation ended.</summary>
    public struct RelocaliseResult
    {
        /// <summary>Aligned, Failed or Unsupported.</summary>
        public RelocaliseState State;
        public RelocaliseReason Reason;
        /// <summary>The saved room, or null when there is none.</summary>
        public RoomModel Room;
        /// <summary>The room's anchor when Aligned. Null when the room stands on its saved pose (PosePipeline.Anchor takes null).</summary>
        public Transform Anchor;
        /// <summary>True: ARKit restored the saved anchor. False: re-created from the saved pose.</summary>
        public bool AnchorRestored;
        /// <summary>From the call to the end.</summary>
        public float Seconds;
        /// <summary>
        /// The room can be activated: Aligned, or a platform with no world map at all (the Editor, XR
        /// Simulation), where the room is used with an identity anchor. False: offer a new scan.
        /// </summary>
        public bool RoomUsable;
    }

    /// <summary>
    /// The pre-scan path (Master Plan section 4.2, decision D21, task P1-C13; lane C section 12): saves the
    /// session's ARWorldMap beside the RoomModel, and at launch applies it, waits for the session to
    /// recognise the room and re-finds the room anchor. A thin adapter: the decisions are Relocaliser's, the
    /// ARKit calls sit behind IArWorldMapPlatform, the files are read and written on a worker thread.
    /// Callbacks arrive on the main thread, from Tick or from the Begin call itself when the outcome is
    /// known at once.
    /// </summary>
    public sealed class WorldMapStore
    {
        const string LogPrefix = "Atlas: ";
        public const float RelocaliseTimeout = Relocaliser.Timeout;

        // The result of a worker-thread file job, picked up by Tick.
        sealed class FileJob
        {
            public volatile bool Finished;
            public byte[] Bytes;
            public bool Ok;
            public string Error;
        }

        // The answer of ARKit to a world map request; it may arrive on any thread.
        sealed class MapAnswer
        {
            public volatile bool Arrived;
            public byte[] Bytes;
        }

        enum SaveStage : byte { Idle, WaitingForMapped, Asked, Writing }

        readonly RoomStore _rooms;
        readonly ArSessionRig _rig;
        readonly Relocaliser _relocaliser = new Relocaliser();

        RoomModel _room;
        Action<RelocaliseResult> _relocaliseDone;
        FileJob _loadJob;
        TrackableId _savedAnchorId;
        bool _hasSavedAnchorId;
        GameObject _createdAnchor;

        SaveStage _saveStage;
        string _saveRoomId;
        Action<bool> _saveDone;
        float _saveSeconds;
        MapAnswer _saveAnswer;
        FileJob _saveJob;

        /// <summary>rig may be null (the Station): nothing is supported then.</summary>
        public WorldMapStore(RoomStore rooms, ArSessionRig rig)
        {
            if (rooms == null) throw new ArgumentNullException(nameof(rooms));
            _rooms = rooms;
            _rig = rig;
        }

        /// <summary>World maps can be saved and applied here: an ARKit device with a running AR session.</summary>
        public bool Supported
        {
            get { return _rig != null && _rig.HasAr && ArWorldMapPlatform.Current.WorldMapSupported && Subsystem != null; }
        }

        /// <summary>How well the session's map would relocalise if saved now. Save when Mapped.</summary>
        public WorldMappingQuality Mapping
        {
            get
            {
                XRSessionSubsystem session = Subsystem;
                return session != null ? ArWorldMapPlatform.Current.MappingQuality(session) : WorldMappingQuality.NotAvailable;
            }
        }

        public RelocaliseState State { get { return _relocaliser.State; } }
        public RelocaliseReason Reason { get { return _relocaliser.Reason; } }
        public float SecondsRelocalising { get { return _relocaliser.Seconds; } }
        /// <summary>0 to 1 towards the time-out, for the progress display.</summary>
        public float Progress { get { return _relocaliser.Progress; } }
        /// <summary>A save is in progress.</summary>
        public bool Saving { get { return _saveStage != SaveStage.Idle; } }
        /// <summary>A save or a relocalisation is in progress: Tick must be called.</summary>
        public bool Busy { get { return Saving || _relocaliser.Busy; } }

        /// <summary>
        /// Saves the session's world map as room_id.worldmap. done(false) when unsupported, when ARKit has no
        /// map to give, or on a file error. Waits up to 5 s for Mapping to reach Mapped, then asks anyway.
        /// </summary>
        public void BeginSave(string roomId, Action<bool> done)
        {
            if (_saveStage != SaveStage.Idle) EndSave(false);      // a newer request replaces the older one
            if (string.IsNullOrEmpty(roomId) || !Supported)
            {
                if (done != null) done(false);
                return;
            }
            _saveRoomId = roomId;
            _saveDone = done;
            _saveSeconds = 0f;
            _saveAnswer = null;
            _saveJob = null;
            _saveStage = SaveStage.WaitingForMapped;
        }

        /// <summary>
        /// Launch: picks the room to return to (the remembered one, else the newest), loads it and
        /// relocalises. With no saved room the result is Unsupported with reason NoSavedRoom.
        /// </summary>
        public void BeginRelocaliseLast(Action<RelocaliseResult> done)
        {
            RoomModel room;
            string error;
            if (!RoomIndex.TryLoadMostRecent(_rooms, _rooms.LastRoomId, out room, out error)) room = null;
            BeginRelocalise(room, done);
        }

        /// <summary>
        /// Loads room_id.worldmap, applies it and waits for the room's anchor. Where there is no world map
        /// at all (the Editor, XR Simulation) the room is handed back at once with an identity anchor, so
        /// the same flow can be rehearsed with no phone (contract README F9).
        /// </summary>
        public void BeginRelocalise(RoomModel room, Action<RelocaliseResult> done)
        {
            CancelRelocalise();
            ReleaseCreatedAnchor();
            _room = room;
            _relocaliseDone = done;
            _relocaliser.Begin(room != null, Supported);
            if (!_relocaliser.Busy)
            {
                bool neutral = room != null && (_rig == null || _rig.Backend != XrBackend.ARKit);
                // A saved anchor pose belongs to the session that made it. With no map to return to that
                // session, the room frame is laid on the session frame as it is.
                if (neutral) room.Anchor = new RoomAnchor { Id = "", Position = float3.zero, Yaw = 0f };
                FinishRelocalise(null, neutral);
                return;
            }

            _hasSavedAnchorId = TryParseId(room.Anchor.Id, out _savedAnchorId);
            var job = new FileJob();
            _loadJob = job;
            RoomStore rooms = _rooms;
            string id = room.Id;
            Task.Run(() =>
            {
                job.Ok = RoomIndex.TryReadWorldMap(rooms, id, out job.Bytes, out job.Error);
                job.Finished = true;
            });
        }

        /// <summary>Advances a save and a relocalisation. Call every frame while Busy; cheap otherwise.</summary>
        public void Tick(float deltaTime)
        {
            if (_saveStage != SaveStage.Idle) TickSave(deltaTime);
            if (_relocaliser.Busy) TickRelocalise(deltaTime);
        }

        /// <summary>
        /// Drops a relocalisation and a save in progress. No callback is made for either. A world map
        /// that was applied and not yet recognised is taken out of the session and tracking restarts.
        /// </summary>
        public void Cancel()
        {
            CancelRelocalise();
            _saveStage = SaveStage.Idle;
            _saveDone = null;
            _saveAnswer = null;
            _saveJob = null;
        }

        XRSessionSubsystem Subsystem
        {
            get { return _rig != null && _rig.Session != null ? _rig.Session.subsystem : null; }
        }

        void TickSave(float deltaTime)
        {
            if (deltaTime > 0f) _saveSeconds += deltaTime;
            switch (_saveStage)
            {
                case SaveStage.WaitingForMapped:
                {
                    XRSessionSubsystem session = Subsystem;
                    if (session == null) { EndSave(false); return; }
                    if (!WorldMapSaveRule.AskNow(Mapping, _saveSeconds)) return;
                    var answer = new MapAnswer();
                    _saveAnswer = answer;
                    _saveSeconds = 0f;
                    _saveStage = SaveStage.Asked;
                    ArWorldMapPlatform.Current.BeginGetWorldMap(session, bytes =>
                    {
                        answer.Bytes = bytes;
                        answer.Arrived = true;
                    });
                    return;
                }
                case SaveStage.Asked:
                {
                    MapAnswer answer = _saveAnswer;
                    if (!answer.Arrived)
                    {
                        if (_saveSeconds >= WorldMapSaveRule.AnswerWait) EndSave(false);
                        return;
                    }
                    byte[] bytes = answer.Bytes;
                    _saveAnswer = null;
                    if (bytes == null || bytes.Length == 0)
                    {
                        Debug.LogWarning(LogPrefix + "ARKit gave no world map for room " + _saveRoomId);
                        EndSave(false);
                        return;
                    }
                    var job = new FileJob();
                    _saveJob = job;
                    _saveStage = SaveStage.Writing;
                    RoomStore rooms = _rooms;
                    string id = _saveRoomId;
                    Task.Run(() =>
                    {
                        job.Ok = RoomIndex.TryWriteWorldMap(rooms, id, bytes, out job.Error);
                        job.Finished = true;
                    });
                    return;
                }
                case SaveStage.Writing:
                {
                    FileJob job = _saveJob;
                    if (!job.Finished) return;
                    if (job.Ok) Debug.Log(LogPrefix + "world map saved for room " + _saveRoomId);
                    else Debug.LogWarning(LogPrefix + "world map not saved: " + job.Error);
                    EndSave(job.Ok);
                    return;
                }
            }
        }

        void EndSave(bool ok)
        {
            Action<bool> done = _saveDone;
            _saveStage = SaveStage.Idle;
            _saveDone = null;
            _saveAnswer = null;
            _saveJob = null;
            if (done != null) done(ok);
        }

        void TickRelocalise(float deltaTime)
        {
            XRSessionSubsystem session = Subsystem;
            if (_relocaliser.State == RelocaliseState.Loading && _loadJob != null && _loadJob.Finished)
            {
                FileJob job = _loadJob;
                _loadJob = null;
                WorldMapLoad load = WorldMapLoad.Missing;
                if (job.Ok)
                    load = session != null && ArWorldMapPlatform.Current.ApplyWorldMap(session, job.Bytes)
                        ? WorldMapLoad.Applied : WorldMapLoad.Bad;
                _relocaliser.MapLoaded(load);
                if (load == WorldMapLoad.Applied) Debug.Log(LogPrefix + "world map applied for room " + _room.Id + ": relocalising");
            }

            TrackingState tracking = session != null && session.running
                ? ArCameraPoseSource.Map(session.trackingState) : TrackingState.NotTracking;
            ARAnchor saved = null;
            if (_relocaliser.State == RelocaliseState.Relocalising && _hasSavedAnchorId && _rig.Anchors != null)
                saved = _rig.Anchors.GetAnchor(_savedAnchorId);
            bool savedTracked = saved != null && saved.trackingState == ArTrackingState.Tracking;

            RelocaliseStep step = _relocaliser.Tick(deltaTime, tracking, savedTracked);
            if (_relocaliser.Busy) return;
            // Before the callback: the walker is offered a new scan, and that needs a session that tracks.
            DropUnrecognisedMap();

            Transform anchor = null;
            if (step == RelocaliseStep.UseRestoredAnchor) anchor = saved.transform;
            else if (step == RelocaliseStep.CreateAnchor) anchor = CreateAnchor(_room);
            FinishRelocalise(anchor, false);
        }

        void FinishRelocalise(Transform anchor, bool neutral)
        {
            var result = new RelocaliseResult
            {
                State = _relocaliser.State,
                Reason = _relocaliser.Reason,
                Room = _room,
                Anchor = anchor,
                AnchorRestored = _relocaliser.AnchorRestored,
                Seconds = _relocaliser.Seconds,
                RoomUsable = neutral || _relocaliser.State == RelocaliseState.Aligned,
            };
            if (result.State == RelocaliseState.Aligned)
            {
                _rooms.LastRoomId = _room.Id;
                Debug.Log(LogPrefix + "relocalised in room " + _room.Id + " after " + result.Seconds.ToString("0.0") + " s, anchor " +
                          (result.AnchorRestored ? "restored" : (anchor != null ? "re-created" : "at the saved pose")));
            }
            else if (_room != null)
            {
                Debug.Log(LogPrefix + "room " + _room.Id + " not relocalised: " + result.Reason +
                          (neutral ? " (used with an identity anchor)" : ""));
            }
            Action<RelocaliseResult> done = _relocaliseDone;
            _relocaliseDone = null;
            _loadJob = null;
            if (done != null) done(result);
        }

        void CancelRelocalise()
        {
            _relocaliser.Cancel();
            _relocaliseDone = null;
            _loadJob = null;        // a read still running finishes into a job nobody looks at
            DropUnrecognisedMap();
        }

        // ARKit keeps a session with an initial world map in Limited (Relocalizing) until it recognises
        // that room: in another room, or a changed one, tracking would never return to Normal and a new
        // scan could not start. So an attempt that ends without the room takes the map out again and
        // restarts tracking (Master Plan section 4.2; lane C section 12, step 4).
        void DropUnrecognisedMap()
        {
            if (!_relocaliser.TakeMapToDrop()) return;
            XRSessionSubsystem session = Subsystem;
            if (session != null) ArWorldMapPlatform.Current.ClearWorldMap(session);
            if (_rig != null && _rig.Session != null) _rig.Session.Reset();
            Debug.Log(LogPrefix + "world map dropped and tracking restarted: the room was not recognised");
        }

        // A relocalised session uses the map's own origin, so the room's saved pose holds in it: the new
        // anchor goes there and the room takes its id (lane C section 12, step 3).
        Transform CreateAnchor(RoomModel room)
        {
            ARAnchorManager anchors = _rig.Anchors;
            Transform space = _rig.Origin != null ? _rig.Origin.TrackablesParent : null;
            if (anchors == null || !anchors.enabled || anchors.subsystem == null || space == null) return null;
            var holder = new GameObject("Atlas Room Anchor");
            holder.SetActive(false);
            holder.transform.SetParent(space, false);
            holder.transform.localPosition = room.Anchor.Position;
            holder.transform.localRotation = Quaternion.AngleAxis(math.degrees(room.Anchor.Yaw), Vector3.up);
            ARAnchor anchor = holder.AddComponent<ARAnchor>();
            // The component registers itself with the anchor subsystem, at its world pose, when it is enabled.
            holder.SetActive(true);
            if (!anchor.enabled || anchor.trackableId == TrackableId.invalidId)
            {
                UnityEngine.Object.Destroy(holder);
                Debug.LogWarning(LogPrefix + "the room anchor could not be re-created: the room keeps its saved pose");
                return null;
            }
            _createdAnchor = holder;
            RoomAnchor saved = room.Anchor;
            saved.Id = anchor.trackableId.ToString();
            room.Anchor = saved;
            return holder.transform;
        }

        void ReleaseCreatedAnchor()
        {
            if (_createdAnchor != null) UnityEngine.Object.Destroy(_createdAnchor);
            _createdAnchor = null;
        }

        static bool TryParseId(string text, out TrackableId id)
        {
            id = TrackableId.invalidId;
            if (string.IsNullOrEmpty(text)) return false;
            try
            {
                id = new TrackableId(text);
            }
            catch (FormatException)
            {
                return false;
            }
            return id != TrackableId.invalidId;
        }
    }
}
