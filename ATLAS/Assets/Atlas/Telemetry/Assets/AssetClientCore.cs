using System;
using Atlas.Link;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Telemetry
{
    /// <summary>Where one object stands in the round trip (docs/contracts/lane-d-link.md section 4.3).</summary>
    public enum ObjectAssetState : byte
    { Placeholder = 0, Uploading = 1, Requested = 2, Downloading = 3, Loading = 4, Ready = 5, BuiltIn = 6 }

    /// <summary>
    /// The asset round trip as a state machine with no engine in it (Master Plan sections 4.5 and 7.4, tasks
    /// P1-D10 and P1-D12): one state per object, one upload, one poll and one download in flight per room.
    /// It posts the RoomModel and the crops to the Forge, follows the jobs, downloads and validates each GLB
    /// and hands the mesh over; whenever the Forge cannot deliver, the object takes its built-in mesh, and the
    /// generated one replaces it when the Forge returns.
    /// <para>Everything is driven from <see cref="Tick"/> on the main thread; nothing here waits. Time comes in
    /// as an argument. Hashing, validation and mesh reading go through <see cref="IAssetWorker"/>.</para>
    /// </summary>
    public sealed class AssetClientCore
    {
        public const int MaxObjects = ObjectMeshRules.MaxMeshedObjects;
        public const int MaxAttempts = 3;
        /// <summary>A room for which no scan announces itself this long after it was built came from a file.</summary>
        public const float LoadedRoomGraceSeconds = 2f;
        public const float HealthTimeout = 2f, RoomTimeout = 10f, ObjectTimeout = 15f, PollTimeout = 5f, DownloadTimeout = 20f;
        public const float PollInterval = 2f, ProbeInterval = 4f, JobSilenceSeconds = 30f, WorkTimeout = 10f;
        /// <summary>The worst frame is watched this long after a mesh was handed over (test L17).</summary>
        public const float AckWindowSeconds = 0.5f;
        /// <summary>Built-in meshes built and placed per frame.</summary>
        public const int BuiltInPerTick = 2;

        public const string BackendLocal = "local", BackendHosted = "hosted", BackendCache = "cache", BackendOff = "off";
        public const string EventName = "asset";
        /// <summary>The reason logged with the built-in mesh an object gets as soon as the room content is built.</summary>
        public const string FirstShapeReason = "first_shape";

        static readonly float[] s_retryWaits = { 1f, 2f };

        enum Reach : byte { Unknown, Reachable, Unreachable }
        enum UploadStep : byte { Idle, Health, Room, Object, Probe }
        enum DownloadPhase : byte { Idle, Work, Http, HttpWait, Place }
        enum WorkOutcome : byte { Ok, NeedDownload, Rejected }

        sealed class Entry
        {
            public int Id;
            public ObjectCategory Category;
            public float3 Size;
            public float Yaw;
            /// <summary>The object's profile (RoomObject.Profile), or null: it shapes the first shape.</summary>
            public byte[] Profile;
            /// <summary>The full target of the object's job; 0 for an object that receives no mesh.</summary>
            public int TriTarget;
            public ObjectAssetState State;
            /// <summary>Built-in for good: no photo, a failed job or a rejected asset is never retried.</summary>
            public bool Sticky;
            public bool ShowsBuiltIn, NeedsBuiltIn, BuiltInTried;
            public string BuiltInReason;
            public ObjectPhotoSet Photos;
            public string JobId = string.Empty, Sha = string.Empty, Url = string.Empty;
            /// <summary>The light copy the Forge announced (or a saved room names); "" for none.</summary>
            public string LightSha = string.Empty, LightUrl = string.Empty;
            /// <summary>A light copy is known and not yet handed over, dropped or on its way.</summary>
            public bool LightPending;
            public bool AckPending;
            public double AckAt;
            public float LoadMs, WorstFrameMs;
        }

        sealed class Work
        {
            public IAssetCache Cache;
            public string Sha;
            public byte[] Bytes;
            public GlbExpectation Expect;
            public readonly MeshData Mesh = new MeshData(4096, 16384);
            public volatile bool Done;
            public WorkOutcome Outcome;
            public string Reason;
        }

        readonly IForgeTransport _transport;
        readonly IAssetRoom _roomView;
        readonly IAssetCache _cache;
        readonly IAssetWorker _worker;
        readonly FallbackSet _fallback;
        readonly IEventLog _events;
        readonly FallbackMeshBuilder _builder = new FallbackMeshBuilder();
        readonly MeshData _builtInMesh = new MeshData(2048, 6000);
        readonly Action<object> _runWork = RunWork;

        Entry[] _entries = new Entry[0];
        // Objects that receive no mesh (no triangle target, or beyond the largest): they keep their first shape.
        Entry[] _extras = new Entry[0];
        RoomModel _room;
        string _roomId = string.Empty, _roomJson;
        bool _built, _beginSeen, _graceDone, _roomPosted;
        double _now, _builtAt;
        string _forgeUrl = string.Empty, _backend = BackendLocal;

        Reach _reach = Reach.Unknown;
        UploadStep _uploadStep;
        int _uploadHandle, _attempts;
        Entry _uploadEntry;
        double _nextAttemptAt, _nextProbeAt;

        int _pollHandle;
        double _nextPollAt, _lastJobNews;

        DownloadPhase _downloadPhase;
        Entry _download;
        // The download in flight is the light copy of _download (the full copy of the object is in place).
        bool _downloadLight;
        int _downloadHandle, _downloadAttempts;
        double _downloadNextAt, _downloadDoneAt, _workStartedAt;
        Work _work = new Work();

        int _deleteHandle;

        public AssetClientCore(IForgeTransport transport, IAssetRoom room, IAssetCache cache, IAssetWorker worker,
                               FallbackSet fallback, IEventLog events)
        {
            if (transport == null) throw new ArgumentNullException("transport");
            if (room == null) throw new ArgumentNullException("room");
            _transport = transport;
            _roomView = room;
            _cache = cache ?? new MemoryAssetCache();
            _worker = worker ?? new InlineAssetWorker();
            _fallback = fallback;
            _events = events;
        }

        // ---- What the app sets ---------------------------------------------------------------------------------

        /// <summary>Base URL of the Forge with no trailing slash, for example http://192.168.0.10:8000. Empty: no Forge.</summary>
        public string ForgeUrl
        {
            get { return _forgeUrl; }
            set
            {
                string url = (value ?? string.Empty).TrimEnd('/');
                if (url == _forgeUrl) return;
                _forgeUrl = url;
                // Another address is another Forge: ask it at once.
                if (_reach == Reach.Unreachable) _nextProbeAt = _now;
                else _reach = Reach.Unknown;
                _attempts = 0;
                _nextAttemptAt = _now;
            }
        }

        /// <summary><c>local</c>, <c>hosted</c>, <c>cache</c> or <c>off</c>: the parameter <c>forge.backend</c>.</summary>
        public string Backend
        {
            get { return _backend; }
            set
            {
                string backend = value == BackendHosted || value == BackendCache || value == BackendOff ? value : BackendLocal;
                if (backend == _backend) return;
                _backend = backend;
                if (backend == BackendOff) AllToBuiltIn("backend_off", false);
            }
        }

        /// <summary>Receives every <c>AssetAck</c>; the app sends it over the link.</summary>
        public Action<AssetAckMessage> AckSink { get; set; }

        // ---- What the app reads --------------------------------------------------------------------------------

        /// <summary>The last GET /health answered 200.</summary>
        public bool ForgeReachable { get { return _reach == Reach.Reachable; } }
        public string RoomId { get { return _roomId; } }
        /// <summary>Objects of the room that receive meshes: at most twenty, the largest boxes.</summary>
        public int ObjectCount { get { return _entries.Length; } }
        /// <summary>Objects showing their generated mesh.</summary>
        public int PlacedCount { get { return Count(ObjectAssetState.Ready, ObjectAssetState.Ready); } }
        /// <summary>Objects on their way: uploading, requested, downloading or loading.</summary>
        public int PendingCount { get { return Count(ObjectAssetState.Uploading, ObjectAssetState.Loading); } }
        /// <summary>Objects showing their built-in mesh: their first shape or the one they fell back to.</summary>
        public int BuiltInCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _entries.Length; i++) if (_entries[i].ShowsBuiltIn) n++;
                for (int i = 0; i < _extras.Length; i++) if (_extras[i].ShowsBuiltIn) n++;
                return n;
            }
        }

        public ObjectAssetState GetState(int objectId)
        {
            Entry e = Find(objectId);
            return e != null ? e.State : ObjectAssetState.Placeholder;
        }

        /// <summary>True while the object shows its built-in mesh, whatever is on the way for it.</summary>
        public bool ShowsBuiltIn(int objectId)
        {
            Entry e = Find(objectId);
            return e != null && e.ShowsBuiltIn;
        }

        /// <summary>The triangle target sent with the object's job; 0 when it receives no mesh.</summary>
        public int TriTarget(int objectId)
        {
            Entry e = Find(objectId);
            return e != null ? e.TriTarget : 0;
        }

        /// <summary>The id of the k-th object in the order the client serves them: largest box first.</summary>
        public int ObjectIdAt(int rank) { return _entries[rank].Id; }

        // ---- Room content --------------------------------------------------------------------------------------

        /// <summary>
        /// The room content has built this room: its placeholder boxes exist. A room the client already follows
        /// keeps its state and is shown again; another room ends the previous one.
        /// </summary>
        public void RoomBuilt(RoomModel room)
        {
            if (room == null) { RoomCleared(); return; }
            if (_room == null || _roomId != room.Id) StartRoom(room);
            _room = room;
            _built = true;
            _builtAt = _now;
            _graceDone = false;
            // A light mesh on its way was meant for the views that are gone.
            if (_downloadLight) EndDownload();
            for (int i = 0; i < _extras.Length; i++) FirstShapeDue(_extras[i]);
            for (int i = 0; i < _entries.Length; i++)
            {
                Entry e = _entries[i];
                // The views are new: whatever was shown is a placeholder box again, and the first shape is due.
                FirstShapeDue(e);
                // The views are new, so the light mesh is gone with them; it follows the full one again.
                e.LightPending = e.LightSha.Length > 0;
                if (e.State == ObjectAssetState.Ready || (e.State == ObjectAssetState.Placeholder && e.Sha.Length > 0))
                {
                    // From the cache: the pre-scan path needs no Station, and no job waits for an answer.
                    e.State = ObjectAssetState.Downloading;
                    e.JobId = string.Empty;
                }
                else if (e.State == ObjectAssetState.Requested) _roomView.SetRequested(e.Id);
            }
            if (_backend == BackendOff) AllToBuiltIn("backend_off", false);
            else if (_reach == Reach.Unreachable) AllToBuiltIn("unreachable", false);
        }

        // The views are new: whatever was shown is a box again. Every object gets its first shape, whatever the
        // Forge's state; the generated mesh replaces it when it arrives.
        static void FirstShapeDue(Entry e)
        {
            e.ShowsBuiltIn = false;
            e.BuiltInTried = false;
            e.NeedsBuiltIn = true;
            if (e.State != ObjectAssetState.BuiltIn || string.IsNullOrEmpty(e.BuiltInReason)) e.BuiltInReason = FirstShapeReason;
        }

        /// <summary>The room is gone: requests are cancelled and late answers dropped.</summary>
        public void RoomCleared()
        {
            CancelRequests();
            _room = null;
            _roomId = string.Empty;
            _roomJson = null;
            _entries = new Entry[0];
            _extras = new Entry[0];
            _built = false;
        }

        // ---- Photo capture (lane C section 11.4) ---------------------------------------------------------------

        public void BeginRoom(RoomModel room, string roomJson)
        {
            if (room == null) return;
            if (_room == null || _roomId != room.Id) StartRoom(room);
            _beginSeen = true;
            _roomJson = roomJson;
            _roomPosted = false;
            if (_backend == BackendOff) AllToBuiltIn("backend_off", false);
            else if (_reach == Reach.Unreachable) AllToBuiltIn("unreachable", false);
        }

        public void Submit(ObjectPhotoSet photos)
        {
            if (photos == null) return;
            Entry e = Find(photos.ObjectId);
            if (e == null || e.Sticky) return;                          // not one of the twenty, or settled
            if (e.State != ObjectAssetState.Placeholder && e.State != ObjectAssetState.BuiltIn) return;   // already on its way
            if (ForgeProtocol.ViewCount(photos) == 0) { ToBuiltIn(e, "no_photo", true); return; }
            if (_backend == BackendOff) { ToBuiltIn(e, "backend_off", false); return; }
            e.Photos = photos;
            // While the Forge is away the crops wait in memory and the built-in mesh stands in.
            if (_reach == Reach.Unreachable) ToBuiltIn(e, "unreachable", false);
            else e.State = ObjectAssetState.Uploading;
        }

        public void ReportNoPhoto(int objectId)
        {
            Entry e = Find(objectId);
            if (e != null && e.State != ObjectAssetState.Ready) ToBuiltIn(e, "no_photo", true);
        }

        public void EndRoom()
        {
            // Whatever was neither submitted nor reported has no photo.
            for (int i = 0; i < _entries.Length; i++)
            {
                Entry e = _entries[i];
                if (e.State == ObjectAssetState.Placeholder && e.Photos == null && e.Sha.Length == 0) ToBuiltIn(e, "no_photo", true);
            }
        }

        // ---- The link ------------------------------------------------------------------------------------------

        public void OnJobStatus(JobStatusMessage status)
        {
            if (status == null || status.RoomId != _roomId) return;
            _lastJobNews = _now;
            if (status.State == "failed") ApplyJob(status.JobId, status.BoxId, status.State, status.Error, null, null, null, null);
        }

        public void OnAssetReady(AssetReadyMessage ready)
        {
            if (ready == null || ready.RoomId != _roomId) return;
            _lastJobNews = _now;
            ApplyJob(ready.JobId, ready.BoxId, "ready", string.Empty, ready.Sha256, ready.Url, ready.LightSha256, ready.LightUrl);
        }

        /// <summary>DELETE /rooms/{roomId} and removal of this room's cached GLBs (Master Plan section 13.5).</summary>
        public void ForgetRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return;
            if (_forgeUrl.Length > 0 && _backend != BackendOff && _deleteHandle == 0)
                _deleteHandle = _transport.Begin("DELETE", _forgeUrl + "/rooms/" + roomId, null, null, RoomTimeout);
            if (roomId != _roomId) return;
            for (int i = 0; i < _entries.Length; i++)
                if (_entries[i].Sha.Length > 0) _cache.Delete(_entries[i].Sha);
            RoomCleared();
        }

        // ---- The frame -----------------------------------------------------------------------------------------

        /// <summary>
        /// Advances every request and places what is ready. Call once per frame after rendering (step 8b).
        /// </summary>
        /// <param name="timeSeconds">A clock that keeps running; only differences are used.</param>
        /// <param name="frameMs">How long the frame that just ended took, for the AssetAck's worst frame.</param>
        public void Tick(double timeSeconds, float frameMs)
        {
            _now = timeSeconds;
            if (_deleteHandle != 0)
            {
                int status;
                byte[] body;
                if (_transport.TryTake(_deleteHandle, out status, out body)) _deleteHandle = 0;
            }
            if (_room == null) return;

            if (_built && !_beginSeen && !_graceDone && _now - _builtAt >= LoadedRoomGraceSeconds)
            {
                // No scan announced itself: the room came from a file or a fixture and no photo will follow.
                _graceDone = true;
                for (int i = 0; i < _entries.Length; i++)
                    if (_entries[i].State == ObjectAssetState.Placeholder) ToBuiltIn(_entries[i], "no_scan", false);
            }

            TickUpload();
            TickPoll();
            TickDownload();
            TickBuiltIn();
            TickAcks(frameMs);
        }

        // ---- Rooms ---------------------------------------------------------------------------------------------

        void StartRoom(RoomModel room)
        {
            CancelRequests();
            _room = room;
            _roomId = room.Id ?? string.Empty;
            _roomJson = null;
            _roomPosted = false;
            _beginSeen = false;
            _graceDone = false;
            _built = false;
            _attempts = 0;
            _nextAttemptAt = _now;
            _lastJobNews = _now;

            // The objects that take part: those lane C gave a triangle target, largest box first, twenty at most.
            RoomObject[] objects = room.Objects ?? new RoomObject[0];
            int n = 0, rest = 0;
            var picked = new RoomObject[objects.Length];
            var others = new RoomObject[objects.Length];
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] == null || objects[i].Mesh == MeshState.StandIn) continue;
                if (objects[i].TriTarget > 0) picked[n++] = objects[i];
                else others[rest++] = objects[i];
            }
            Array.Sort(picked, 0, n, s_largestFirst);
            // An object beyond the largest keeps its first shape, like one the room gave no target.
            for (int i = MaxObjects; i < n; i++) others[rest++] = picked[i];
            if (n > MaxObjects) n = MaxObjects;

            _entries = new Entry[n];
            for (int i = 0; i < n; i++)
            {
                RoomObject o = picked[i];
                var e = new Entry { Id = o.Id, Category = o.Category, Size = o.Size, Yaw = o.Yaw, Profile = o.Profile, TriTarget = o.TriTarget };
                // A saved room names the mesh it had; it comes back from the cache.
                if (ForgeProtocol.IsSha256(o.AssetHash)) e.Sha = o.AssetHash;
                if (e.Sha.Length > 0 && ForgeProtocol.IsSha256(o.AssetHashLight)) { e.LightSha = o.AssetHashLight; e.LightPending = true; }
                _entries[i] = e;
            }
            _extras = new Entry[rest];
            for (int i = 0; i < rest; i++)
            {
                RoomObject o = others[i];
                _extras[i] = new Entry
                {
                    Id = o.Id, Category = o.Category, Size = o.Size, Yaw = o.Yaw, Profile = o.Profile,
                    State = ObjectAssetState.BuiltIn, Sticky = true
                };
            }
        }

        static readonly System.Collections.Generic.IComparer<RoomObject> s_largestFirst = new LargestFirst();

        sealed class LargestFirst : System.Collections.Generic.IComparer<RoomObject>
        {
            public int Compare(RoomObject a, RoomObject b)
            {
                int byVolume = b.Volume.CompareTo(a.Volume);
                return byVolume != 0 ? byVolume : a.Id.CompareTo(b.Id);
            }
        }

        void CancelRequests()
        {
            if (_uploadHandle != 0) _transport.Cancel(_uploadHandle);
            if (_pollHandle != 0) _transport.Cancel(_pollHandle);
            if (_downloadHandle != 0) _transport.Cancel(_downloadHandle);
            _uploadHandle = _pollHandle = _downloadHandle = 0;
            _uploadStep = UploadStep.Idle;
            _uploadEntry = null;
            EndDownload();
        }

        Entry Find(int objectId)
        {
            for (int i = 0; i < _entries.Length; i++) if (_entries[i].Id == objectId) return _entries[i];
            for (int i = 0; i < _extras.Length; i++) if (_extras[i].Id == objectId) return _extras[i];
            return null;
        }

        int Count(ObjectAssetState from, ObjectAssetState to)
        {
            int n = 0;
            for (int i = 0; i < _entries.Length; i++) if (_entries[i].State >= from && _entries[i].State <= to) n++;
            return n;
        }

        // ---- Built-in meshes -----------------------------------------------------------------------------------

        void AllToBuiltIn(string reason, bool sticky)
        {
            for (int i = 0; i < _entries.Length; i++)
            {
                Entry e = _entries[i];
                if (e.State == ObjectAssetState.Ready || e.State == ObjectAssetState.BuiltIn) continue;
                if (e.State == ObjectAssetState.Downloading || e.State == ObjectAssetState.Loading)
                {
                    // A mesh that may be in the cache needs no Forge; only one on the wire is given up.
                    bool onTheWire = _download == e && (_downloadPhase == DownloadPhase.Http || _downloadPhase == DownloadPhase.HttpWait);
                    if (!onTheWire) continue;
                }
                ToBuiltIn(e, reason, sticky);
            }
        }

        void ToBuiltIn(Entry e, string reason, bool sticky)
        {
            if (_download == e) EndDownload();
            if (_uploadEntry == e && sticky) _uploadEntry = null;
            bool changed = e.State != ObjectAssetState.BuiltIn;
            e.State = ObjectAssetState.BuiltIn;
            if (sticky)
            {
                e.Sticky = true;
                e.Photos = null;                    // nothing will be sent for it again: the crops go
            }
            if (!e.ShowsBuiltIn && !e.BuiltInTried)
            {
                e.NeedsBuiltIn = true;
                e.BuiltInReason = reason;
            }
            else if (changed && e.ShowsBuiltIn)
            {
                // The first shape is already there and stays: say why the object will have nothing else.
                LogStep("builtin", e, JsonValue.Object().Set("reason", reason ?? string.Empty).Set("placed", true)
                    .Set("kept", string.Empty).Set("shape", "kept").Set("tris", 0));
            }
        }

        void TickBuiltIn()
        {
            if (!_built) return;
            int placed = 0;
            for (int i = 0; i < _entries.Length && placed < BuiltInPerTick; i++) PlaceFirstShape(_entries[i], ref placed);
            for (int i = 0; i < _extras.Length && placed < BuiltInPerTick; i++) PlaceFirstShape(_extras[i], ref placed);
        }

        // The built-in mesh of one object: the fitted template, the template, or the shape cut from the scan.
        void PlaceFirstShape(Entry e, ref int placed)
        {
            if (!e.NeedsBuiltIn) return;
            e.NeedsBuiltIn = false;
            e.BuiltInTried = true;
            if (e.State == ObjectAssetState.Ready) return;              // the generated mesh got there first
            placed++;

            FirstShapeKind kind = FittedShapes.Build(_builder, _fallback, e.Category, e.Size, e.Yaw, e.Profile, _builtInMesh);
            string kept = null;
            if (kind == FirstShapeKind.None)
                kept = _fallback != null && _fallback.Get(e.Category) != null ? "too_large" : "no_template";    // the box stays
            else if (!_roomView.PlaceBuiltIn(e.Id, _builtInMesh)) kept = "no_view";
            if (kept == null) e.ShowsBuiltIn = true;
            LogStep("builtin", e, JsonValue.Object().Set("reason", e.BuiltInReason ?? string.Empty)
                .Set("placed", kept == null).Set("kept", kept ?? string.Empty)
                .Set("shape", kept == null ? ShapeName(kind) : string.Empty)
                .Set("tris", kept == null ? _builtInMesh.TriangleCount : 0));
        }

        static string ShapeName(FirstShapeKind kind)
        {
            switch (kind)
            {
                case FirstShapeKind.Template: return "template";
                case FirstShapeKind.Fitted: return "fitted";
                case FirstShapeKind.ScanCut: return "scancut";
                default: return string.Empty;
            }
        }

        // ---- Uploads -------------------------------------------------------------------------------------------

        void TickUpload()
        {
            if (_uploadHandle != 0)
            {
                int status;
                byte[] body;
                if (!_transport.TryTake(_uploadHandle, out status, out body)) return;
                _uploadHandle = 0;
                UploadStep step = _uploadStep;
                _uploadStep = UploadStep.Idle;
                OnUploadAnswer(step, status, body);
                if (_uploadHandle != 0) return;
            }
            if (_backend == BackendOff || _forgeUrl.Length == 0) return;

            // With nothing to send, the Forge is still asked now and then: a saved room may be waiting for a
            // mesh that is not in the cache, and the menu shows whether the Forge is there.
            if (_reach == Reach.Unreachable || (_reach == Reach.Unknown && !HasUpload()))
            {
                if (_now < _nextProbeAt) return;
                _uploadStep = UploadStep.Probe;
                _uploadHandle = _transport.Begin("GET", _forgeUrl + "/health", null, null, HealthTimeout);
                return;
            }
            if (_now < _nextAttemptAt || !HasUpload()) return;
            // Reachability first, before every attempt: a Forge that is away is noticed in 2 s, not in 15.
            _uploadStep = UploadStep.Health;
            _uploadHandle = _transport.Begin("GET", _forgeUrl + "/health", null, null, HealthTimeout);
        }

        bool HasUpload()
        {
            if (_roomJson == null) return false;
            if (!_roomPosted) return _entries.Length > 0;
            return NextUpload() != null;
        }

        Entry NextUpload()
        {
            for (int i = 0; i < _entries.Length; i++)
                if (_entries[i].State == ObjectAssetState.Uploading && _entries[i].Photos != null) return _entries[i];
            return null;
        }

        void OnUploadAnswer(UploadStep step, int status, byte[] body)
        {
            switch (step)
            {
                case UploadStep.Probe:
                    if (status == 200) ForgeReturned();
                    else if (_reach == Reach.Unknown) DeclareUnreachable();
                    else _nextProbeAt = _now + ProbeInterval;
                    break;

                case UploadStep.Health:
                    if (status != 200) { AttemptFailed(); break; }
                    _reach = Reach.Reachable;
                    if (!_roomPosted)
                    {
                        _uploadStep = UploadStep.Room;
                        _uploadHandle = _transport.Begin("POST", _forgeUrl + "/rooms", ForgeProtocol.Utf8(_roomJson),
                            ForgeProtocol.JsonContentType, RoomTimeout);
                        LogStep("room", null, JsonValue.Object().Set("roomId", _roomId).Set("attempt", _attempts + 1));
                        break;
                    }
                    Entry next = NextUpload();
                    if (next == null) break;
                    _uploadEntry = next;
                    _uploadStep = UploadStep.Object;
                    int target = next.TriTarget;
                    _uploadHandle = _transport.Begin("POST", _forgeUrl + "/rooms/" + _roomId + "/objects",
                        ForgeProtocol.ObjectBody(next.Photos, target, _backend, ObjectMeshRules.LightTarget(_entries.Length)), ForgeProtocol.MultipartContentType, ObjectTimeout);
                    LogStep("upload", next, JsonValue.Object().Set("attempt", _attempts + 1).Set("triTarget", target)
                        .Set("views", ForgeProtocol.ViewCount(next.Photos)));
                    break;

                case UploadStep.Room:
                    if (status == 200 || status == 201)
                    {
                        _roomPosted = true;
                        _attempts = 0;
                    }
                    else if (status >= 400 && status < 500)
                    {
                        // The Forge answers and refuses the room: sending it again changes nothing.
                        _roomJson = null;
                        for (int i = 0; i < _entries.Length; i++)
                            if (_entries[i].State != ObjectAssetState.Ready) ToBuiltIn(_entries[i], "room_rejected", true);
                    }
                    else AttemptFailed();
                    break;

                case UploadStep.Object:
                    Entry e = _uploadEntry;
                    _uploadEntry = null;
                    if (e == null || e.State != ObjectAssetState.Uploading) break;
                    if (status == 200 || status == 202) OnObjectAccepted(e, ForgeProtocol.Json(body));
                    else if (status == 404)
                    {
                        // The Forge no longer knows the room (its data was cleared): post it again first.
                        _roomPosted = false;
                        AttemptFailed();
                    }
                    else if (status >= 400 && status < 500)
                    {
                        LogStep("failed", e, JsonValue.Object().Set("reason", "upload_rejected").Set("status", status));
                        ToBuiltIn(e, "upload_rejected", true);
                    }
                    else AttemptFailed();
                    break;
            }
        }

        void OnObjectAccepted(Entry e, JsonValue answer)
        {
            _attempts = 0;
            e.Photos = null;                        // the upload that carried the crops has ended
            e.JobId = answer.GetString("jobId", string.Empty);
            string sha = answer.GetString("sha256", string.Empty);
            if (answer.GetBool("placeholder"))
            {
                // Beyond the Forge's own twenty: the box stays.
                e.State = ObjectAssetState.Placeholder;
                e.Sticky = true;
                return;
            }
            if (answer.GetBool("cached") && ForgeProtocol.IsSha256(sha))
            {
                LogStep("cached", e, JsonValue.Object().Set("jobId", e.JobId).Set("sha256", sha));
                BeginDownload(e, sha, ForgeProtocol.AssetPath(sha));
                NoteLight(e, answer.GetString("lightSha256", null), answer.GetString("lightUrl", null));
                return;
            }
            if (e.JobId.Length == 0)
            {
                LogStep("failed", e, JsonValue.Object().Set("reason", "no_job"));
                ToBuiltIn(e, "no_job", true);
                return;
            }
            if (Count(ObjectAssetState.Requested, ObjectAssetState.Requested) == 0)
            {
                _lastJobNews = _now;
                _nextPollAt = _now + PollInterval;
            }
            e.State = ObjectAssetState.Requested;
            if (_built) _roomView.SetRequested(e.Id);
            LogStep("requested", e, JsonValue.Object().Set("jobId", e.JobId));
        }

        void AttemptFailed()
        {
            _attempts++;
            if (_attempts >= MaxAttempts) DeclareUnreachable();
            else _nextAttemptAt = _now + s_retryWaits[_attempts - 1];
        }

        void DeclareUnreachable()
        {
            _reach = Reach.Unreachable;
            _attempts = 0;
            _nextProbeAt = _now + ProbeInterval;
            if (_pollHandle != 0) { _transport.Cancel(_pollHandle); _pollHandle = 0; }
            LogStep("unreachable", null, JsonValue.Object().Set("roomId", _roomId));
            // Every object that is not there yet takes its built-in mesh; crops and job ids are kept for the return.
            AllToBuiltIn("unreachable", false);
        }

        void ForgeReturned()
        {
            _reach = Reach.Reachable;
            _attempts = 0;
            _nextAttemptAt = _now;
            _roomPosted = false;                    // the room and the crops still held are posted again
            _lastJobNews = _now;
            _nextPollAt = _now;
            LogStep("reachable", null, JsonValue.Object().Set("roomId", _roomId));
            for (int i = 0; i < _entries.Length; i++)
            {
                Entry e = _entries[i];
                if (e.State != ObjectAssetState.BuiltIn || e.Sticky) continue;
                if (e.Sha.Length > 0) e.State = ObjectAssetState.Downloading;
                else if (e.Photos != null && _roomJson != null) e.State = ObjectAssetState.Uploading;
                else if (e.JobId.Length > 0) e.State = ObjectAssetState.Requested;
            }
            // With nothing to post, the room on the Forge is as it was.
            if (_roomJson == null) _roomPosted = true;
        }

        // ---- Jobs ----------------------------------------------------------------------------------------------

        void TickPoll()
        {
            if (_pollHandle != 0)
            {
                int status;
                byte[] body;
                if (!_transport.TryTake(_pollHandle, out status, out body)) return;
                _pollHandle = 0;
                if (status == 200) OnJobList(ForgeProtocol.Json(body));
            }
            if (_reach == Reach.Unreachable || _forgeUrl.Length == 0 || _backend == BackendOff) return;
            if (Count(ObjectAssetState.Requested, ObjectAssetState.Requested) == 0) return;
            if (_now - _lastJobNews > JobSilenceSeconds)
            {
                // Neither the Hub nor a poll has said anything about the jobs: the Forge is away.
                DeclareUnreachable();
                return;
            }
            if (_now < _nextPollAt) return;
            _nextPollAt = _now + PollInterval;
            _pollHandle = _transport.Begin("GET", _forgeUrl + "/jobs?room=" + _roomId, null, null, PollTimeout);
        }

        void OnJobList(JsonValue answer)
        {
            // The Forge answers with a plain list; the contract page wraps it in {"jobs": [...]}. Both are read.
            JsonValue jobs = answer.IsArray ? answer : answer["jobs"];
            if (!jobs.IsArray) return;
            _lastJobNews = _now;
            for (int i = 0; i < jobs.Count; i++)
            {
                JsonValue job = jobs[i];
                if (!job.IsObject || job.GetString("roomId", _roomId) != _roomId) continue;
                ApplyJob(job.GetString("jobId", string.Empty), job.GetInt("boxId"), job.GetString("state", string.Empty),
                    job.GetString("error", string.Empty), job.GetString("sha256", null), job.GetString("url", null),
                    job.GetString("lightSha256", null), job.GetString("lightUrl", null));
            }
            // A job the Forge no longer lists will never finish.
            for (int i = 0; i < _entries.Length; i++)
            {
                Entry e = _entries[i];
                if (e.State != ObjectAssetState.Requested) continue;
                bool listed = false;
                for (int k = 0; k < jobs.Count && !listed; k++) listed = jobs[k].GetString("jobId", string.Empty) == e.JobId;
                if (listed) continue;
                LogStep("failed", e, JsonValue.Object().Set("jobId", e.JobId).Set("reason", "job_lost"));
                ToBuiltIn(e, "job_lost", true);
            }
        }

        void ApplyJob(string jobId, int boxId, string state, string error, string sha, string url, string lightSha, string lightUrl)
        {
            Entry e = Find(boxId);
            if (e == null || e.State != ObjectAssetState.Requested) return;
            if (e.JobId.Length > 0 && !string.IsNullOrEmpty(jobId) && jobId != e.JobId) return;     // an older job of the same box
            if (state == "ready")
            {
                if (!ForgeProtocol.IsSha256(sha))
                {
                    LogStep("failed", e, JsonValue.Object().Set("jobId", e.JobId).Set("reason", "no_hash"));
                    ToBuiltIn(e, "no_hash", true);
                    return;
                }
                LogStep("ready", e, JsonValue.Object().Set("jobId", e.JobId).Set("sha256", sha));
                BeginDownload(e, sha, string.IsNullOrEmpty(url) ? ForgeProtocol.AssetPath(sha) : url);
                NoteLight(e, lightSha, lightUrl);
            }
            else if (state == "failed")
            {
                LogStep("failed", e, JsonValue.Object().Set("jobId", e.JobId).Set("reason", error ?? string.Empty));
                ToBuiltIn(e, "job_failed", true);
            }
            else if (state == "placeholder")
            {
                e.State = ObjectAssetState.Placeholder;
                e.Sticky = true;
            }
        }

        // ---- Download, validate, load, place -------------------------------------------------------------------

        // The Forge announced a light copy: it is fetched after the full one is in place. Anything else is no light copy.
        void NoteLight(Entry e, string lightSha, string lightUrl)
        {
            if (!ForgeProtocol.IsSha256(lightSha)) return;
            e.LightSha = lightSha;
            e.LightUrl = string.IsNullOrEmpty(lightUrl) ? ForgeProtocol.AssetPath(lightSha) : lightUrl;
            e.LightPending = true;
        }

        void BeginDownload(Entry e, string sha, string url)
        {
            e.Sha = sha;
            e.Url = url;
            e.State = ObjectAssetState.Downloading;
        }

        void EndDownload()
        {
            if (_downloadHandle != 0) { _transport.Cancel(_downloadHandle); _downloadHandle = 0; }
            // A worker that is still busy keeps its buffers; the next download gets fresh ones.
            if (_downloadPhase == DownloadPhase.Work && !_work.Done) _work = new Work();
            _download = null;
            _downloadLight = false;
            _downloadPhase = DownloadPhase.Idle;
        }

        void TickDownload()
        {
            if (_download == null)
            {
                for (int i = 0; i < _entries.Length && _download == null; i++)
                    if (_entries[i].State == ObjectAssetState.Downloading) _download = _entries[i];
                _downloadLight = false;
                // Only when no full copy waits: the walker is in Home, which shows the full ones.
                for (int i = 0; _download == null && _built && i < _entries.Length; i++)
                {
                    if (_entries[i].State != ObjectAssetState.Ready || !_entries[i].LightPending) continue;
                    _download = _entries[i];
                    _downloadLight = true;
                }
                if (_download == null) return;
                _downloadAttempts = 0;
                _downloadDoneAt = _now;
                StartWork(null);                    // the cache first: a file already there skips the download
            }
            Entry e = _download;
            switch (_downloadPhase)
            {
                case DownloadPhase.Work:
                    if (!_work.Done)
                    {
                        if (_now - _workStartedAt > WorkTimeout) Fail(e, "load_failed");
                        return;
                    }
                    if (_work.Outcome == WorkOutcome.Rejected) { Fail(e, _work.Reason); return; }
                    if (_work.Outcome == WorkOutcome.NeedDownload)
                    {
                        if (_reach == Reach.Unreachable || _backend == BackendOff || _forgeUrl.Length == 0)
                        {
                            // A saved room whose mesh is not in the cache, and no Forge to ask.
                            if (_downloadLight) DropLight(e, "no_cache");
                            else ToBuiltIn(e, "no_cache", false);
                            return;
                        }
                        StartHttp(e);
                        return;
                    }
                    if (!_downloadLight) e.State = ObjectAssetState.Loading;
                    _downloadPhase = DownloadPhase.Place;
                    goto case DownloadPhase.Place;

                case DownloadPhase.Http:
                    int status;
                    byte[] body;
                    if (!_transport.TryTake(_downloadHandle, out status, out body)) return;
                    _downloadHandle = 0;
                    if (status == 200 && body != null && body.Length > 0)
                    {
                        _downloadDoneAt = _now;
                        StartWork(body);
                        return;
                    }
                    _downloadAttempts++;
                    if (_downloadAttempts >= MaxAttempts) { Fail(e, "download_failed"); return; }
                    _downloadNextAt = _now + s_retryWaits[_downloadAttempts - 1];
                    _downloadPhase = DownloadPhase.HttpWait;
                    return;

                case DownloadPhase.HttpWait:
                    if (_now >= _downloadNextAt) StartHttp(e);
                    return;

                case DownloadPhase.Place:
                    if (!_built) return;            // the boxes are not there yet
                    if (_downloadLight)
                    {
                        PlaceLight(e);
                        return;
                    }
                    if (!_roomView.PlaceGenerated(e.Id, _work.Mesh, e.Sha)) { Reject(e, "load_failed"); return; }
                    e.State = ObjectAssetState.Ready;
                    e.ShowsBuiltIn = false;
                    e.NeedsBuiltIn = false;
                    e.Photos = null;
                    e.LoadMs = (float)((_now - _downloadDoneAt) * 1000.0);
                    e.WorstFrameMs = 0f;
                    e.AckPending = true;
                    e.AckAt = _now + AckWindowSeconds;
                    LogStep("placed", e, JsonValue.Object().Set("sha256", e.Sha).Set("tris", _work.Mesh.TriangleCount)
                        .Set("loadMs", e.LoadMs));
                    _download = null;
                    _downloadPhase = DownloadPhase.Idle;
                    return;
            }
        }

        void StartHttp(Entry e)
        {
            string sha = _downloadLight ? e.LightSha : e.Sha, path = _downloadLight ? e.LightUrl : e.Url;
            string url = path.StartsWith("http://", StringComparison.Ordinal) || path.StartsWith("https://", StringComparison.Ordinal)
                ? path : _forgeUrl + (path.Length > 0 ? path : ForgeProtocol.AssetPath(sha));
            _downloadPhase = DownloadPhase.Http;
            _downloadHandle = _transport.Begin("GET", url, null, null, DownloadTimeout);
        }

        void StartWork(byte[] downloaded)
        {
            Entry e = _download;
            Work work = _work;
            string sha = _downloadLight ? e.LightSha : e.Sha;
            work.Cache = _cache;
            work.Sha = sha;
            work.Bytes = downloaded;
            work.Expect = new GlbExpectation
            {
                DimX = e.Size.x, DimY = e.Size.y, DimZ = e.Size.z,
                // The full copy meets the object's own target; the light copy the room's light target.
                TriTarget = _downloadLight ? ObjectMeshRules.LightTarget(_entries.Length) : e.TriTarget,
                Level = _downloadLight ? GlbLevel.Light : GlbLevel.Full,
                Sha256 = sha
            };
            work.Done = false;
            _workStartedAt = _now;
            _downloadPhase = DownloadPhase.Work;
            _worker.Run(_runWork, work);
        }

        // Worker thread. Touches only its Work object and the cache.
        static void RunWork(object state)
        {
            var work = (Work)state;
            try
            {
                byte[] bytes = work.Bytes;
                bool downloaded = bytes != null;
                if (!downloaded)
                {
                    bytes = work.Cache.TryRead(work.Sha);
                    if (bytes == null) { work.Outcome = WorkOutcome.NeedDownload; return; }
                }
                GlbReport report = bytes.Length > GlbValidator.MaxBytesOf(work.Expect.Level)
                    ? new GlbReport { FailedCheck = GlbValidator.CodeSize }
                    : GlbValidator.Validate(bytes, in work.Expect);
                if (!report.Ok)
                {
                    if (!downloaded)
                    {
                        // A damaged file in the cache is not the asset's fault: fetch it again.
                        work.Cache.Delete(work.Sha);
                        work.Outcome = WorkOutcome.NeedDownload;
                        return;
                    }
                    work.Reason = report.FailedCheck == GlbValidator.CodeHash ? "hash_mismatch"
                        : report.FailedCheck == GlbValidator.CodeSize ? "too_large" : "validate:" + report.FailedCheck;
                    work.Outcome = WorkOutcome.Rejected;
                    return;
                }
                if (!GlbMeshReader.TryRead(bytes, bytes.Length, work.Mesh))
                {
                    work.Reason = "load_failed";
                    work.Outcome = WorkOutcome.Rejected;
                    return;
                }
                if (downloaded) work.Cache.Write(work.Sha, bytes);
                work.Outcome = WorkOutcome.Ok;
            }
            catch (Exception)
            {
                work.Reason = "load_failed";
                work.Outcome = WorkOutcome.Rejected;
            }
            finally
            {
                work.Bytes = null;
                work.Done = true;
            }
        }

        // A copy that fails ends the object's full copy (Reject) or, for a light copy, only itself.
        void Fail(Entry e, string reason)
        {
            if (_downloadLight) DropLight(e, reason);
            else Reject(e, reason);
        }

        // The light copy is given up: logged, never counted against the object, which keeps its full mesh.
        void DropLight(Entry e, string reason)
        {
            LogStep("light_dropped", e, JsonValue.Object().Set("sha256", e.LightSha).Set("reason", reason ?? string.Empty));
            e.LightPending = false;
            EndDownload();
        }

        void PlaceLight(Entry e)
        {
            if (!_roomView.PlaceGeneratedLight(e.Id, _work.Mesh, e.LightSha)) { DropLight(e, "load_failed"); return; }
            e.LightPending = false;
            LogStep("light_placed", e, JsonValue.Object().Set("sha256", e.LightSha).Set("tris", _work.Mesh.TriangleCount));
            _download = null;
            _downloadLight = false;
            _downloadPhase = DownloadPhase.Idle;
        }

        void Reject(Entry e, string reason)
        {
            LogStep("rejected", e, JsonValue.Object().Set("sha256", e.Sha).Set("reason", reason ?? string.Empty));
            SendAck(e, false, reason);
            e.Sha = string.Empty;
            e.LightPending = false;
            ToBuiltIn(e, "rejected", true);
        }

        // ---- Acks and events -----------------------------------------------------------------------------------

        void TickAcks(float frameMs)
        {
            for (int i = 0; i < _entries.Length; i++)
            {
                Entry e = _entries[i];
                if (!e.AckPending) continue;
                if (frameMs > e.WorstFrameMs) e.WorstFrameMs = frameMs;
                if (_now < e.AckAt) continue;
                e.AckPending = false;
                SendAck(e, true, string.Empty);
            }
        }

        void SendAck(Entry e, bool ok, string reason)
        {
            Action<AssetAckMessage> sink = AckSink;
            // A mesh that came from the cache of a saved room belongs to no job: there is nobody to tell.
            if (sink == null || e.JobId.Length == 0) return;
            sink(new AssetAckMessage
            {
                JobId = e.JobId, BoxId = e.Id, Sha256 = e.Sha, Ok = ok,
                LoadMs = ok ? e.LoadMs : 0f, WorstFrameMs = ok ? e.WorstFrameMs : 0f, Reason = reason ?? string.Empty
            });
        }

        void LogStep(string step, Entry e, JsonValue data)
        {
            if (_events == null) return;
            data.Set("step", step);
            if (e != null) data.Set("boxId", e.Id);
            _events.LogJson(EventName, data.ToJson());
        }
    }
}
