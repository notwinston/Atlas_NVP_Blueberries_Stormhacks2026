using System;
using Atlas.Link;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Telemetry
{
    /// <summary>What one object of the mirrored room shows.</summary>
    public enum SpectatorObjectShows : byte { Box = 0, BuiltIn = 1, Generated = 2 }

    /// <summary>
    /// The Spectator's furniture (Master Plan sections 7.3.1 and 7.4; lane D contract, section 5.8): the same
    /// meshes the walker sees, with nothing sent. It follows the phone's relayed <c>asset</c> events, the Hub's
    /// <c>JobStatus</c> and <c>AssetReady</c>, and, for a Station that joined after the scan, the Forge's job
    /// list. A generated mesh is downloaded by its hash, validated as on the phone and placed; a built-in mesh
    /// is placed where the phone placed one. It uploads nothing, posts no room and sends no <c>AssetAck</c>.
    /// <para>Engine-free. Everything is driven from <see cref="Tick"/> on the main thread; nothing waits.</para>
    /// </summary>
    public sealed class SpectatorAssetsCore
    {
        public const int MaxObjects = ObjectMeshRules.MaxMeshedObjects;
        public const int MaxAttempts = 3;
        public const float ListTimeout = 5f, DownloadTimeout = 20f, WorkTimeout = 10f, RetrySeconds = 2f;
        /// <summary>The job list is asked again this often while a job is still on its way.</summary>
        public const float RepollSeconds = 10f;
        /// <summary>
        /// With no news of the room for this long, an object the Forge holds no job for is taken to show its
        /// built-in mesh on the phone: a room from a file, an object with no photo, or a Forge that was away.
        /// </summary>
        public const float QuietSeconds = 6f;
        /// <summary>A mesh that could not be fetched or placed is tried again after this long.</summary>
        public const float RetryLaterSeconds = 15f;
        /// <summary>Built-in meshes built and placed per frame.</summary>
        public const int BuiltInPerTick = 2;

        enum Phase : byte { Idle, Work, Http, HttpWait }
        enum WorkOutcome : byte { Ok, NeedDownload, Rejected }

        sealed class Entry
        {
            public int Id;
            public ObjectCategory Category;
            public float3 Size;
            public float Yaw;
            /// <summary>The generated mesh the phone shows or is about to show; empty for none.</summary>
            public string Sha = string.Empty, Url = string.Empty;
            /// <summary>A hash that did not pass validation here; it is not fetched again.</summary>
            public string BadSha = string.Empty;
            public string ShownSha = string.Empty;
            public bool WantBuiltIn, HasJob, BuiltInTried;
            /// <summary>A mesh that could not be had is asked for again from this time on.</summary>
            public double RetryAt;
            public SpectatorObjectShows Shows;

            public bool Wanted { get { return Sha.Length > 0 && Sha != ShownSha && Sha != BadSha; } }
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
        }

        readonly IForgeTransport _transport;
        readonly IAssetRoom _roomView;
        readonly IAssetCache _cache;
        readonly IAssetWorker _worker;
        readonly FallbackSet _fallback;
        readonly FallbackMeshBuilder _builder = new FallbackMeshBuilder();
        readonly MeshData _builtInMesh = new MeshData(2048, 6000);
        readonly Action<object> _runWork = RunWork;

        Entry[] _entries = new Entry[0];
        string _roomId = string.Empty, _forgeUrl = string.Empty;
        bool _built, _listDone, _settled;
        double _now, _lastNews;

        int _listHandle, _listAttempts;
        double _nextListAt;

        Phase _phase;
        Entry _download;
        int _downloadHandle, _downloadAttempts;
        double _downloadNextAt, _workStartedAt;
        Work _work = new Work();

        public SpectatorAssetsCore(IForgeTransport transport, IAssetRoom room, IAssetCache cache, IAssetWorker worker, FallbackSet fallback)
        {
            if (transport == null) throw new ArgumentNullException("transport");
            if (room == null) throw new ArgumentNullException("room");
            _transport = transport;
            _roomView = room;
            _cache = cache ?? new MemoryAssetCache();
            _worker = worker ?? new InlineAssetWorker();
            _fallback = fallback;
        }

        /// <summary>Base URL of the Forge as the Hub's Welcome names it. Empty: only the cache can deliver.</summary>
        public string ForgeUrl
        {
            get { return _forgeUrl; }
            set
            {
                string url = (value ?? string.Empty).TrimEnd('/');
                if (url == _forgeUrl) return;
                _forgeUrl = url;
                // Another Forge: what could not be fetched from the last one is asked for again.
                for (int i = 0; i < _entries.Length; i++) _entries[i].RetryAt = 0.0;
                if (_built && url.Length > 0 && !RoomFixtures.IsFixture(_roomId)) AskListSoon();
            }
        }

        public string RoomId { get { return _roomId; } }
        /// <summary>Objects of the room that can take a mesh: at most twenty, the largest boxes, as on the phone.</summary>
        public int ObjectCount { get { return _entries.Length; } }
        public int GeneratedCount { get { return Count(SpectatorObjectShows.Generated); } }
        public int BuiltInCount { get { return Count(SpectatorObjectShows.BuiltIn); } }
        /// <summary>Generated meshes known of and not shown yet.</summary>
        public int PendingCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _entries.Length; i++) if (_entries[i].Wanted) n++;
                return n;
            }
        }

        public SpectatorObjectShows Shows(int objectId)
        {
            Entry e = Find(objectId);
            return e != null ? e.Shows : SpectatorObjectShows.Box;
        }

        // ---- The room content ----------------------------------------------------------------------------------

        /// <summary>
        /// The room content has built this room: every object is a placeholder box again. What is known of the
        /// same room is kept and shown again; another room starts from what its RoomModel says.
        /// </summary>
        public void RoomBuilt(RoomModel room)
        {
            if (room == null) { RoomCleared(); return; }
            CancelRequests();
            string id = room.Id ?? string.Empty;
            Entry[] before = id == _roomId ? _entries : null;
            _roomId = id;

            // The objects that take part, as the phone picks them: the triangle target of a mesh depends on
            // how many there are, and the validation below must expect what the Forge was asked for.
            RoomObject[] objects = room.Objects ?? new RoomObject[0];
            int n = 0;
            var picked = new RoomObject[objects.Length];
            for (int i = 0; i < objects.Length; i++)
                if (objects[i] != null && objects[i].TriTarget > 0 && objects[i].Mesh != MeshState.StandIn) picked[n++] = objects[i];
            Array.Sort(picked, 0, n, s_largestFirst);
            if (n > MaxObjects) n = MaxObjects;

            _entries = new Entry[n];
            for (int i = 0; i < n; i++)
            {
                RoomObject o = picked[i];
                var e = new Entry { Id = o.Id, Category = o.Category, Size = o.Size, Yaw = o.Yaw };
                if (ForgeProtocol.IsSha256(o.AssetHash)) e.Sha = o.AssetHash;
                Entry old = before != null ? FindIn(before, o.Id) : null;
                if (old != null)
                {
                    if (old.Sha.Length > 0) { e.Sha = old.Sha; e.Url = old.Url; }
                    e.BadSha = old.BadSha;
                    e.WantBuiltIn = old.WantBuiltIn;
                    e.HasJob = old.HasJob;
                }
                _entries[i] = e;
            }

            _built = true;
            _settled = false;
            _lastNews = _now;
            _listAttempts = 0;
            _nextListAt = _now;
            // A fixture room never went to the Forge: there is no job list to ask for.
            _listDone = RoomFixtures.IsFixture(_roomId);
        }

        /// <summary>The room content holds no room: requests are cancelled. What is known of the room is kept.</summary>
        public void RoomCleared()
        {
            CancelRequests();
            _built = false;
            for (int i = 0; i < _entries.Length; i++)
            {
                Entry e = _entries[i];
                e.Shows = SpectatorObjectShows.Box;
                e.ShownSha = string.Empty;
                e.BuiltInTried = false;
                e.RetryAt = 0.0;
            }
        }

        // ---- The link ------------------------------------------------------------------------------------------

        /// <summary>A relayed event of the phone. Only its <c>asset</c> events are read.</summary>
        public void OnEvent(EventMessage message)
        {
            if (message == null || message.Ev != AssetClientCore.EventName || message.Data == null || !message.Data.IsObject) return;
            JsonValue data = message.Data;
            _lastNews = _now;
            Entry e = Find(data.GetInt("boxId", -1));
            if (e == null) return;
            string step = data.GetString("step", string.Empty);
            switch (step)
            {
                case "placed":
                    // The phone shows this mesh now.
                    Generated(e, data.GetString("sha256", string.Empty), string.Empty);
                    break;
                case "builtin":
                    // `placed` is false when the phone kept the box (no template, or the mesh did not fit).
                    if (!data.GetBool("placed")) break;
                    e.WantBuiltIn = true;
                    e.BuiltInTried = false;
                    e.Sha = string.Empty;
                    if (_download == e) EndDownload();
                    break;
                case "upload":
                case "requested":
                case "cached":
                case "ready":
                    e.HasJob = true;
                    break;
            }
        }

        /// <summary>The Hub's news of a finished asset: the download starts as it does on the phone.</summary>
        public void OnAssetReady(AssetReadyMessage ready)
        {
            if (ready == null || !SameRoom(ready.RoomId)) return;
            _lastNews = _now;
            Entry e = Find(ready.BoxId);
            if (e == null) return;
            e.HasJob = true;
            Generated(e, ready.Sha256, ready.Url);
        }

        /// <summary>The Hub's news of a job. A job that is on its way keeps its box; a failed one ends in a built-in mesh.</summary>
        public void OnJobStatus(JobStatusMessage status)
        {
            if (status == null || !SameRoom(status.RoomId)) return;
            _lastNews = _now;
            Entry e = Find(status.BoxId);
            if (e == null) return;
            e.HasJob = true;
            if (status.State == "failed" && e.Sha.Length == 0) e.WantBuiltIn = true;
        }

        bool SameRoom(string roomId) { return string.IsNullOrEmpty(roomId) || roomId == _roomId; }

        void Generated(Entry e, string sha, string url)
        {
            if (!ForgeProtocol.IsSha256(sha)) return;
            e.HasJob = true;
            if (e.Sha == sha) return;
            if (_download == e) EndDownload();
            e.Sha = sha;
            e.Url = url ?? string.Empty;
            e.RetryAt = 0.0;
        }

        // ---- The frame -----------------------------------------------------------------------------------------

        /// <summary>Advances the requests and places what is ready. Once per frame, after rendering.</summary>
        /// <param name="timeSeconds">A clock that keeps running; only differences are used.</param>
        public void Tick(double timeSeconds)
        {
            _now = timeSeconds;
            if (!_built) return;
            TickList();
            TickSettle();
            TickDownload();
            TickBuiltIn();
        }

        // ---- The Forge's job list: what happened before this Station was watching ------------------------------

        void AskListSoon()
        {
            _listDone = false;
            _listAttempts = 0;
            _nextListAt = _now;
        }

        void TickList()
        {
            if (_listHandle != 0)
            {
                int status;
                byte[] body;
                if (!_transport.TryTake(_listHandle, out status, out body)) return;
                _listHandle = 0;
                JsonValue answer = status == 200 ? ForgeProtocol.Json(body) : JsonValue.Null;
                // The Forge answers with a plain list; the contract page wraps it in {"jobs": [...]}. Both are read.
                JsonValue jobs = answer.IsArray ? answer : answer["jobs"];
                if (jobs.IsArray)
                {
                    ApplyJobs(jobs);
                    _listDone = true;
                    _listAttempts = 0;
                    _nextListAt = _now + RepollSeconds;
                }
                else if (++_listAttempts >= MaxAttempts)
                {
                    // No Forge: the phone has none either, and shows its built-in set.
                    _listDone = true;
                    _listAttempts = 0;
                    _nextListAt = _now + RepollSeconds;
                }
                else _nextListAt = _now + RetrySeconds;
                return;
            }
            if (_entries.Length == 0 || _now < _nextListAt) return;
            if (_forgeUrl.Length == 0)
            {
                // No Forge was named: the cache and the phone's events are all there is.
                _listDone = true;
                return;
            }
            if (_listDone && !AwaitsJob()) return;
            _listHandle = _transport.Begin("GET", _forgeUrl + "/jobs?room=" + Uri.EscapeDataString(_roomId), null, null, ListTimeout);
        }

        // A job that neither the link nor an earlier list has seen finished.
        bool AwaitsJob()
        {
            if (RoomFixtures.IsFixture(_roomId)) return false;
            for (int i = 0; i < _entries.Length; i++)
            {
                Entry e = _entries[i];
                if (e.HasJob && e.Sha.Length == 0 && !e.WantBuiltIn) return true;
            }
            return false;
        }

        void ApplyJobs(JsonValue jobs)
        {
            for (int i = 0; i < jobs.Count; i++)
            {
                JsonValue job = jobs[i];
                if (!job.IsObject || job.GetString("roomId", _roomId) != _roomId) continue;
                Entry e = Find(job.GetInt("boxId", -1));
                if (e == null) continue;
                string state = job.GetString("state", string.Empty);
                if (state == "ready") Generated(e, job.GetString("sha256", string.Empty), job.GetString("url", string.Empty));
                else if (state == "failed") { if (e.Sha.Length == 0) e.WantBuiltIn = true; }
                else if (state != "placeholder") e.HasJob = true;
            }
        }

        void TickSettle()
        {
            if (_settled || !_listDone || _now - _lastNews < QuietSeconds) return;
            _settled = true;
            for (int i = 0; i < _entries.Length; i++)
            {
                Entry e = _entries[i];
                if (!e.HasJob && e.Sha.Length == 0) e.WantBuiltIn = true;
            }
        }

        // ---- Built-in meshes -----------------------------------------------------------------------------------

        void TickBuiltIn()
        {
            int placed = 0;
            for (int i = 0; i < _entries.Length && placed < BuiltInPerTick; i++)
            {
                Entry e = _entries[i];
                if (!e.WantBuiltIn || e.BuiltInTried || e.Shows == SpectatorObjectShows.BuiltIn) continue;
                // A generated mesh that is shown or on its way wins, unless the phone went back to its built-in one.
                if (e.Sha.Length > 0 && e.Sha != e.BadSha) continue;
                e.BuiltInTried = true;
                placed++;
                FallbackTemplate template = _fallback != null ? _fallback.Get(e.Category) : null;
                if (template == null) continue;                         // Unknown and Other keep their box
                if (!_builder.Build(template, e.Size, e.Yaw, _builtInMesh)) continue;
                if (!_roomView.PlaceBuiltIn(e.Id, _builtInMesh)) continue;
                e.Shows = SpectatorObjectShows.BuiltIn;
                e.ShownSha = string.Empty;
            }
        }

        // ---- Download, validate, load, place -------------------------------------------------------------------

        void TickDownload()
        {
            if (_download == null)
            {
                for (int i = 0; i < _entries.Length && _download == null; i++)
                    if (_entries[i].Wanted && _now >= _entries[i].RetryAt) _download = _entries[i];
                if (_download == null) return;
                _downloadAttempts = 0;
                StartWork(null);                    // the cache first: a file already there needs no Forge
            }
            Entry e = _download;
            switch (_phase)
            {
                case Phase.Work:
                    if (!_work.Done)
                    {
                        if (_now - _workStartedAt > WorkTimeout) GiveUp(e, false);
                        return;
                    }
                    if (_work.Outcome == WorkOutcome.Rejected) { GiveUp(e, true); return; }
                    if (_work.Outcome == WorkOutcome.NeedDownload)
                    {
                        if (_forgeUrl.Length == 0) { GiveUp(e, false); return; }
                        StartHttp(e);
                        return;
                    }
                    if (_roomView.PlaceGenerated(e.Id, _work.Mesh, e.Sha))
                    {
                        e.Shows = SpectatorObjectShows.Generated;
                        e.ShownSha = e.Sha;
                        e.WantBuiltIn = false;
                    }
                    else e.RetryAt = _now + RetryLaterSeconds;
                    _download = null;
                    _phase = Phase.Idle;
                    return;

                case Phase.Http:
                    int status;
                    byte[] body;
                    if (!_transport.TryTake(_downloadHandle, out status, out body)) return;
                    _downloadHandle = 0;
                    if (status == 200 && body != null && body.Length > 0) { StartWork(body); return; }
                    if (++_downloadAttempts >= MaxAttempts) { GiveUp(e, false); return; }
                    _downloadNextAt = _now + RetrySeconds;
                    _phase = Phase.HttpWait;
                    return;

                case Phase.HttpWait:
                    if (_now >= _downloadNextAt) StartHttp(e);
                    return;
            }
        }

        // rejected: the file is not a valid asset, and never will be. Otherwise it could not be had now: the
        // object keeps what it shows and the mesh is asked for again later.
        void GiveUp(Entry e, bool rejected)
        {
            if (rejected) e.BadSha = e.Sha;
            else e.RetryAt = _now + RetryLaterSeconds;
            EndDownload();
        }

        void StartHttp(Entry e)
        {
            string url = e.Url.StartsWith("http://", StringComparison.Ordinal) || e.Url.StartsWith("https://", StringComparison.Ordinal)
                ? e.Url : _forgeUrl + (e.Url.Length > 0 ? e.Url : ForgeProtocol.AssetPath(e.Sha));
            _phase = Phase.Http;
            _downloadHandle = _transport.Begin("GET", url, null, null, DownloadTimeout);
        }

        void StartWork(byte[] downloaded)
        {
            Entry e = _download;
            Work work = _work;
            work.Cache = _cache;
            work.Sha = e.Sha;
            work.Bytes = downloaded;
            work.Expect = new GlbExpectation
            {
                DimX = e.Size.x, DimY = e.Size.y, DimZ = e.Size.z,
                TriTarget = ObjectMeshRules.TriTarget(_entries.Length),
                Sha256 = e.Sha
            };
            work.Done = false;
            _workStartedAt = _now;
            _phase = Phase.Work;
            _worker.Run(_runWork, work);
        }

        // Worker thread. Touches only its Work object and the cache. The checks are the phone's.
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
                bool ok = bytes.Length <= GlbValidator.MaxBytes && GlbValidator.Validate(bytes, in work.Expect).Ok
                          && GlbMeshReader.TryRead(bytes, bytes.Length, work.Mesh);
                if (!ok)
                {
                    if (downloaded) { work.Outcome = WorkOutcome.Rejected; return; }
                    // A damaged file in the cache is not the asset's fault: fetch it again.
                    work.Cache.Delete(work.Sha);
                    work.Outcome = WorkOutcome.NeedDownload;
                    return;
                }
                if (downloaded) work.Cache.Write(work.Sha, bytes);
                work.Outcome = WorkOutcome.Ok;
            }
            catch (Exception)
            {
                work.Outcome = WorkOutcome.Rejected;
            }
            finally
            {
                work.Bytes = null;
                work.Done = true;
            }
        }

        void EndDownload()
        {
            if (_downloadHandle != 0) { _transport.Cancel(_downloadHandle); _downloadHandle = 0; }
            // A worker that is still busy keeps its buffers; the next download gets fresh ones.
            if (_phase == Phase.Work && !_work.Done) _work = new Work();
            _download = null;
            _phase = Phase.Idle;
        }

        void CancelRequests()
        {
            if (_listHandle != 0) { _transport.Cancel(_listHandle); _listHandle = 0; }
            EndDownload();
        }

        // ---- small things --------------------------------------------------------------------------------------

        Entry Find(int objectId) { return FindIn(_entries, objectId); }

        static Entry FindIn(Entry[] entries, int objectId)
        {
            for (int i = 0; i < entries.Length; i++) if (entries[i].Id == objectId) return entries[i];
            return null;
        }

        int Count(SpectatorObjectShows shows)
        {
            int n = 0;
            for (int i = 0; i < _entries.Length; i++) if (_entries[i].Shows == shows) n++;
            return n;
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
    }
}
