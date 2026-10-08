using System;
using Atlas.Link;
using Atlas.Objects;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Telemetry
{
    /// <summary>
    /// Turns the JPEG crops of an object into an sRGB picture. Main thread only (the app's adapter uses Unity's
    /// ImageConversion); tests use a fake.
    /// </summary>
    public interface ICropDecoder
    {
        /// <summary>
        /// False when the bytes are no picture. <paramref name="rgb"/> holds width * height * 3 bytes (r, g, b),
        /// row 0 at the top, and belongs to the caller.
        /// </summary>
        bool TryDecode(byte[] jpeg, out int width, out int height, out byte[] rgb);
    }

    /// <summary>
    /// The on-phone object path (standalone-phone plan section 6.5, task T8): the runtime replacement for the Forge.
    /// Every object first gets its built-in first shape, as with <see cref="AssetClientCore"/>; once its photos
    /// arrive (or it is reported to have none), it is matched to a library prop, fitted, coloured from its crops
    /// and placed through <see cref="IAssetRoom.PlaceOnPhone"/>. Unknown and Other objects, and objects whose best
    /// prop fits poorly, are cut out of the scanned LiDAR mesh instead (D8). Nothing goes over the network.
    /// <para>Everything is driven from <see cref="Tick"/> on the main thread; nothing here waits. JPEGs are decoded
    /// on the main thread, at most <see cref="DecodesPerTick"/> objects per frame and at most
    /// <see cref="MaxInFlight"/> objects at a time; matching, fitting, colouring and cutting run on
    /// <see cref="IAssetWorker"/>. Crops are dropped as soon as the object is coloured (rule 10). The scanned mesh
    /// is kept in memory only, until every object of its room is built.</para>
    /// <para>Engine-free. A saved room whose objects are <c>matched</c> with a prop and its cells is rebuilt without
    /// photos, by the same choice; <c>scanCut</c> objects of a saved room keep their first shape.</para>
    /// </summary>
    public sealed class OnPhoneObjectsCore : IObjectPhotoSink, IScanMeshSink
    {
        public const int MaxObjects = ObjectMeshRules.MaxMeshedObjects;
        /// <summary>A room for which no scan announces itself this long after it was built came from a file.</summary>
        public const float LoadedRoomGraceSeconds = AssetClientCore.LoadedRoomGraceSeconds;
        /// <summary>Built-in meshes built and placed per frame.</summary>
        public const int BuiltInPerTick = AssetClientCore.BuiltInPerTick;
        /// <summary>Objects whose crops are decoded per frame (plan section 6.5).</summary>
        public const int DecodesPerTick = 2;
        /// <summary>Objects being built at once: their decoded crops are what the path holds in memory.</summary>
        public const int MaxInFlight = 2;
        /// <summary>The floor of an object's triangle cap: max(TriTarget, this).</summary>
        public const int MinTriangleCap = ObjectMeshRules.TriTargetMin;
        /// <summary>A build that has not ended after this long is given up and the first shape stays.</summary>
        public const float WorkTimeout = 10f;
        /// <summary>Crops used for colour, as the Forge reads them.</summary>
        public const int MaxViews = 3;

        public const string EventName = AssetClientCore.EventName;

        enum Step : byte { Waiting, Queued, Working, Placed, Kept }
        enum Outcome : byte { None, Matched, ScanCut }

        sealed class Entry
        {
            public int Id;
            /// <summary>The room's own object: read and written on the main thread only.</summary>
            public RoomObject Object;
            /// <summary>A copy of the box for the worker: id, category, base, size, yaw.</summary>
            public RoomObject Box;
            public ObjectCategory Category;
            public float3 Size;
            public float Yaw;
            public byte[] Profile;
            public string[] Tags;
            public ObjectFacingRule Facing;
            public int TriTarget;
            public Step Step;
            public ObjectPhotoSet Photos;
            /// <summary>A saved prop and its cells: the object is rebuilt from them without photos.</summary>
            public string SavedProp;
            public byte[] SavedCells;
            /// <summary>The cell a cut-out was coloured with, kept to cut it again for new views; -1 for none.</summary>
            public int CutCell = -1;
            /// <summary>What is placed: Matched or ScanCut, with the prop and cells of a match.</summary>
            public MeshState PlacedState;
            public string PlacedProp;
            public byte[] PlacedCells;
            public bool ShowsOnPhone, ShowsBuiltIn, NeedsBuiltIn, BuiltInTried;
            public string BuiltInReason;
            public bool Extra;
        }

        sealed class Work
        {
            public Entry Entry;
            public double StartedAt;
            // In: copies of what the worker needs; the entry is not touched off the main thread.
            public PropEntry[] Library;
            public ScanMesh Scan;
            public RoomObject Box;
            public ObjectQuery Query;
            public float Yaw;
            public int MaxTriangles;
            public ObjectView[] Views;
            public RgbImage[] Images;
            public string SavedProp;
            public byte[] SavedCells;
            public int SavedCutCell;
            // Out.
            public readonly MeshData Mesh = new MeshData(4096, 16384);
            public Outcome Outcome;
            public string Prop;
            public byte[] Cells;
            public int CutCell;
            public float Fit;
            public int Turns;
            public string Reason;
            public volatile bool Done;
        }

        readonly IAssetRoom _roomView;
        readonly PropSet _props;
        readonly IAssetWorker _worker;
        readonly FallbackSet _fallback;
        readonly IEventLog _events;
        readonly ICropDecoder _decoder;
        readonly FallbackMeshBuilder _builder = new FallbackMeshBuilder();
        readonly MeshData _builtInMesh = new MeshData(2048, 6000);
        readonly Action<object> _runWork = RunWork;
        readonly Work[] _slots = new Work[MaxInFlight];

        Entry[] _entries = new Entry[0];
        Entry[] _extras = new Entry[0];
        RoomModel _room;
        string _roomId = string.Empty;
        bool _built, _beginSeen, _graceDone, _settledRaised;
        double _now, _builtAt;
        // The scanned mesh of one room (D8): in memory only, dropped once that room's objects are built.
        ScanMesh _scanMesh;
        string _scanRoomId;

        public OnPhoneObjectsCore(IAssetRoom room, PropSet props, IAssetWorker worker, FallbackSet fallback, IEventLog events,
                                  ICropDecoder decoder)
        {
            if (room == null) throw new ArgumentNullException("room");
            _roomView = room;
            _props = props ?? PropSet.Empty();
            _worker = worker ?? new InlineAssetWorker();
            _fallback = fallback;
            _events = events;
            _decoder = decoder;
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new Work();
        }

        // ---- What the app reads --------------------------------------------------------------------------------

        public string RoomId { get { return _roomId; } }
        /// <summary>Objects of the room that receive an on-phone mesh: at most thirty-two, the largest boxes.</summary>
        public int ObjectCount { get { return _entries.Length; } }
        /// <summary>Objects showing a matched prop or a cut-out.</summary>
        public int PlacedCount { get { return Count(Step.Placed); } }
        /// <summary>Objects whose mesh is being built or waits its turn.</summary>
        public int PendingCount { get { return Count(Step.Queued) + Count(Step.Working); } }
        /// <summary>Objects showing their built-in mesh: their first shape.</summary>
        public int BuiltInCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _entries.Length; i++) if (_entries[i].ShowsBuiltIn && !_entries[i].ShowsOnPhone) n++;
                for (int i = 0; i < _extras.Length; i++) if (_extras[i].ShowsBuiltIn) n++;
                return n;
            }
        }
        /// <summary>True while a scanned mesh is held for the room's cut-outs.</summary>
        public bool HoldsScanMesh { get { return _scanMesh != null; } }
        /// <summary>The library the objects are matched against.</summary>
        public PropSet Props { get { return _props; } }

        /// <summary>
        /// Raised once per scanned room, on the main thread, when every object has its final mesh and the room's
        /// RoomObjects say so (Mesh, Prop, PropCells): the app saves a kept room again (plan section 6.5, saved room).
        /// </summary>
        public Action<RoomModel> Settled { get; set; }

        // ---- The scan mesh -------------------------------------------------------------------------------------

        public void SetScanMesh(string roomId, ScanMesh mesh)
        {
            _scanRoomId = roomId ?? string.Empty;
            _scanMesh = mesh;
        }

        // ---- Room content --------------------------------------------------------------------------------------

        /// <summary>
        /// The room content has built this room: its placeholder boxes exist. A room the core already follows keeps
        /// its state, and what was placed is built again for the new views; another room ends the previous one.
        /// </summary>
        public void RoomBuilt(RoomModel room)
        {
            if (room == null) { RoomCleared(); return; }
            if (_room == null || _roomId != (room.Id ?? string.Empty)) StartRoom(room);
            else if (!ReferenceEquals(_room, room)) Rebind(room);
            _room = room;
            _built = true;
            _builtAt = _now;
            _graceDone = false;
            for (int i = 0; i < _extras.Length; i++) FirstShapeDue(_extras[i]);
            for (int i = 0; i < _entries.Length; i++)
            {
                Entry e = _entries[i];
                FirstShapeDue(e);
                e.ShowsOnPhone = false;
                if (e.Step != Step.Placed) continue;
                // The views are new: what was placed is built again, the same way, without photos.
                if (e.PlacedState == MeshState.Matched)
                {
                    e.SavedProp = e.PlacedProp;
                    e.SavedCells = e.PlacedCells;
                    e.Step = Step.Queued;
                }
                else if (e.PlacedState == MeshState.ScanCut && e.CutCell >= 0 && ScanFor(_roomId) != null) e.Step = Step.Queued;
                else Keep(e, "scan_mesh_gone");
            }
        }

        /// <summary>The room is gone: work in flight is dropped and its results never placed.</summary>
        public void RoomCleared()
        {
            DropWork();
            _room = null;
            _roomId = string.Empty;
            _entries = new Entry[0];
            _extras = new Entry[0];
            _built = false;
            _scanMesh = null;
            _scanRoomId = null;
        }

        // ---- Photo capture (lane C section 11.4) ---------------------------------------------------------------

        public void BeginRoom(RoomModel room, string roomJson)
        {
            if (room == null) return;
            if (_room == null || _roomId != (room.Id ?? string.Empty)) StartRoom(room);
            _beginSeen = true;
        }

        public void Submit(ObjectPhotoSet photos)
        {
            if (photos == null) return;
            Entry e = FindEntry(photos.ObjectId);
            if (e == null || e.Step != Step.Waiting) return;    // not one of the thirty-two, or already on its way
            e.Photos = ForgeProtocol.ViewCount(photos) > 0 ? photos : null;
            e.Step = Step.Queued;
        }

        public void ReportNoPhoto(int objectId)
        {
            Entry e = FindEntry(objectId);
            if (e == null || e.Step != Step.Waiting) return;
            // No photo means default colours, not no mesh.
            e.Photos = null;
            e.Step = Step.Queued;
        }

        public void EndRoom()
        {
            // Whatever was neither submitted nor reported has no photo.
            for (int i = 0; i < _entries.Length; i++)
                if (_entries[i].Step == Step.Waiting) _entries[i].Step = Step.Queued;
        }

        // ---- The frame -----------------------------------------------------------------------------------------

        /// <summary>
        /// Places what is built, starts the next builds and places first shapes. Call once per frame after
        /// rendering (step 8b). Allocates nothing once the room is settled.
        /// </summary>
        /// <param name="timeSeconds">A clock that keeps running; only differences are used.</param>
        public void Tick(double timeSeconds)
        {
            _now = timeSeconds;
            if (_room == null) return;

            if (_built && !_beginSeen && !_graceDone && _now - _builtAt >= LoadedRoomGraceSeconds)
            {
                // No scan announced itself: the room came from a file or a fixture and no photo will follow. Its
                // objects are matched with their default colours.
                _graceDone = true;
                for (int i = 0; i < _entries.Length; i++)
                    if (_entries[i].Step == Step.Waiting) _entries[i].Step = Step.Queued;
            }

            TickWork();
            TickStart();
            TickBuiltIn();
            TickSettled();
        }

        // ---- Rooms ---------------------------------------------------------------------------------------------

        void StartRoom(RoomModel room)
        {
            DropWork();
            _room = room;
            _roomId = room.Id ?? string.Empty;
            _beginSeen = false;
            _graceDone = false;
            _built = false;
            _settledRaised = false;
            // A scanned mesh of another room is of no use any more.
            if (_scanMesh != null && _scanRoomId != _roomId) { _scanMesh = null; _scanRoomId = null; }

            // The objects that take part: those with a triangle target, largest box first, thirty-two at most.
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
            for (int i = MaxObjects; i < n; i++) others[rest++] = picked[i];
            if (n > MaxObjects) n = MaxObjects;

            _entries = new Entry[n];
            for (int i = 0; i < n; i++)
            {
                Entry e = NewEntry(picked[i]);
                RoomObject o = picked[i];
                // A saved room names the prop it had: rebuilt without photos, by the same choice.
                if (o.Mesh == MeshState.Matched && !string.IsNullOrEmpty(o.Prop))
                {
                    e.SavedProp = o.Prop;
                    e.SavedCells = o.PropCells;
                    e.Step = Step.Queued;
                }
                _entries[i] = e;
            }
            _extras = new Entry[rest];
            for (int i = 0; i < rest; i++)
            {
                Entry e = NewEntry(others[i]);
                e.Extra = true;
                e.Step = Step.Kept;
                _extras[i] = e;
            }
        }

        static Entry NewEntry(RoomObject o)
        {
            return new Entry
            {
                Id = o.Id, Object = o, Category = o.Category, Size = o.Size, Yaw = o.Yaw, Profile = o.Profile,
                Tags = o.Tags ?? Array.Empty<string>(), Facing = o.Facing, TriTarget = o.TriTarget,
                Box = new RoomObject { Id = o.Id, Category = o.Category, Base = o.Base, Size = o.Size, Yaw = o.Yaw, Facing = o.Facing },
            };
        }

        // The same room as another instance (read again from its file): the entries follow the new objects.
        void Rebind(RoomModel room)
        {
            RoomObject[] objects = room.Objects ?? new RoomObject[0];
            for (int k = 0; k < 2; k++)
            {
                Entry[] list = k == 0 ? _entries : _extras;
                for (int i = 0; i < list.Length; i++)
                    for (int j = 0; j < objects.Length; j++)
                        if (objects[j] != null && objects[j].Id == list[i].Id) { list[i].Object = objects[j]; break; }
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

        Entry FindEntry(int objectId)
        {
            for (int i = 0; i < _entries.Length; i++) if (_entries[i].Id == objectId) return _entries[i];
            return null;
        }

        int Count(Step step)
        {
            int n = 0;
            for (int i = 0; i < _entries.Length; i++) if (_entries[i].Step == step) n++;
            return n;
        }

        ScanMesh ScanFor(string roomId)
        {
            return _scanMesh != null && _scanRoomId == roomId ? _scanMesh : null;
        }

        // ---- First shapes (as AssetClientCore) -----------------------------------------------------------------

        static void FirstShapeDue(Entry e)
        {
            e.ShowsBuiltIn = false;
            e.BuiltInTried = false;
            e.NeedsBuiltIn = true;
            e.BuiltInReason = AssetClientCore.FirstShapeReason;
        }

        void TickBuiltIn()
        {
            if (!_built) return;
            int placed = 0;
            for (int i = 0; i < _entries.Length && placed < BuiltInPerTick; i++) PlaceFirstShape(_entries[i], ref placed);
            for (int i = 0; i < _extras.Length && placed < BuiltInPerTick; i++) PlaceFirstShape(_extras[i], ref placed);
        }

        void PlaceFirstShape(Entry e, ref int placed)
        {
            if (!e.NeedsBuiltIn) return;
            e.NeedsBuiltIn = false;
            e.BuiltInTried = true;
            if (e.ShowsOnPhone) return;                         // the on-phone mesh got there first
            placed++;

            FirstShapeKind kind = FittedShapes.Build(_builder, _fallback, e.Category, e.Size, e.Yaw, e.Profile, _builtInMesh);
            string kept = null;
            if (kind == FirstShapeKind.None)
                kept = _fallback != null && _fallback.Get(e.Category) != null ? "too_large" : "no_template";
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

        // The object has nothing better than its first shape, for good.
        void Keep(Entry e, string reason)
        {
            e.Step = Step.Kept;
            e.Photos = null;
            if (e.SavedProp != null && e.Object != null && e.Object.Prop == e.SavedProp)
            {
                // A saved prop that cannot be built again is no longer what the object shows.
                e.Object.Prop = string.Empty;
                e.Object.PropCells = null;
            }
            e.SavedProp = null;
            e.SavedCells = null;
            if (!e.ShowsBuiltIn && !e.BuiltInTried && _built) e.NeedsBuiltIn = true;
            LogStep("kept", e, JsonValue.Object().Set("reason", reason ?? string.Empty));
        }

        // ---- Builds --------------------------------------------------------------------------------------------

        void TickStart()
        {
            int started = 0;
            for (int s = 0; s < _slots.Length && started < DecodesPerTick; s++)
            {
                if (_slots[s].Entry != null) continue;
                Entry next = null;
                for (int i = 0; i < _entries.Length && next == null; i++) if (_entries[i].Step == Step.Queued) next = _entries[i];
                if (next == null) return;
                Start(_slots[s], next);
                started++;
            }
        }

        void Start(Work work, Entry e)
        {
            work.Entry = e;
            work.StartedAt = _now;
            work.Library = _props.Props;
            work.Scan = ScanFor(_roomId);
            work.Box = e.Box;
            work.Query = new ObjectQuery { Category = e.Category, Tags = e.Tags, Size = e.Size, Profile = e.Profile, Facing = e.Facing };
            work.Yaw = e.Yaw;
            work.MaxTriangles = Math.Max(e.TriTarget, MinTriangleCap);
            work.SavedProp = e.SavedProp;
            work.SavedCells = e.SavedCells;
            work.SavedCutCell = e.SavedProp == null && e.Step == Step.Queued && e.PlacedState == MeshState.ScanCut ? e.CutCell : -1;
            work.Outcome = Outcome.None;
            work.Prop = null;
            work.Cells = null;
            work.CutCell = -1;
            work.Fit = 0f;
            work.Turns = 0;
            work.Reason = null;
            Decode(e, work);
            e.Photos = null;                                    // the JPEGs go once decoded (rule 10)
            e.Step = Step.Working;
            work.Done = false;
            _worker.Run(_runWork, work);
        }

        // The crops of the object as pictures, main thread. A view that cannot be decoded is left out.
        void Decode(Entry e, Work work)
        {
            work.Views = null;
            work.Images = null;
            ObjectPhotoSet photos = e.Photos;
            int count = Math.Min(ForgeProtocol.ViewCount(photos), MaxViews);
            if (count == 0 || _decoder == null) return;
            var views = new ObjectView[count];
            var images = new RgbImage[count];
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                ObjectView view = photos.Views[i];
                int width, height;
                byte[] rgb;
                if (!_decoder.TryDecode(view.Jpeg, out width, out height, out rgb)) continue;
                if (rgb == null || width <= 0 || height <= 0 || rgb.Length < width * height * 3) continue;
                // The intrinsics are in crop pixels: a picture of another size would be sampled in the wrong place.
                if (view.Width > 0 && view.Height > 0 && (view.Width != width || view.Height != height)) continue;
                view.Jpeg = null;
                views[n] = view;
                images[n] = new RgbImage { Width = width, Height = height, Rgb = rgb };
                n++;
            }
            if (n == 0) return;
            if (n < count)
            {
                Array.Resize(ref views, n);
                Array.Resize(ref images, n);
            }
            work.Views = views;
            work.Images = images;
        }

        void TickWork()
        {
            for (int s = 0; s < _slots.Length; s++)
            {
                Work work = _slots[s];
                Entry e = work.Entry;
                if (e == null) continue;
                if (!work.Done)
                {
                    if (_now - work.StartedAt > WorkTimeout)
                    {
                        // The worker keeps its buffers; this slot gets fresh ones.
                        _slots[s] = new Work();
                        Keep(e, "work_timeout");
                    }
                    continue;
                }
                if (!_built) continue;                          // the boxes are not there yet
                Finish(work, e);
                work.Entry = null;
                work.Library = null;
                work.Scan = null;
                work.Box = null;
                work.Query = default(ObjectQuery);
                work.SavedProp = null;
                work.SavedCells = null;
            }
        }

        void Finish(Work work, Entry e)
        {
            if (work.Outcome == Outcome.None) { Keep(e, work.Reason ?? "none"); return; }
            MeshState state = work.Outcome == Outcome.Matched ? MeshState.Matched : MeshState.ScanCut;
            string prop = state == MeshState.Matched ? work.Prop : string.Empty;
            byte[] cells = state == MeshState.Matched ? work.Cells : null;
            if (!_roomView.PlaceOnPhone(e.Id, work.Mesh, state, prop, cells)) { Keep(e, "no_view"); return; }
            e.Step = Step.Placed;
            e.PlacedState = state;
            e.PlacedProp = prop;
            e.PlacedCells = cells;
            e.CutCell = state == MeshState.ScanCut ? work.CutCell : -1;
            e.SavedProp = null;
            e.SavedCells = null;
            e.ShowsOnPhone = true;
            e.NeedsBuiltIn = false;
            JsonValue data = JsonValue.Object().Set("shape", state == MeshState.Matched ? "matched" : "scanCut")
                .Set("tris", work.Mesh.TriangleCount).Set("fit", work.Fit);
            if (state == MeshState.Matched) data.Set("prop", prop).Set("turns", work.Turns);
            LogStep("onphone", e, data);
        }

        void DropWork()
        {
            for (int s = 0; s < _slots.Length; s++)
            {
                if (_slots[s].Entry == null) continue;
                // A worker that is still busy keeps its buffers; the next build gets fresh ones.
                if (!_slots[s].Done) _slots[s] = new Work();
                else
                {
                    _slots[s].Entry = null;
                    _slots[s].Views = null;
                    _slots[s].Images = null;
                }
            }
        }

        // ---- Settling ------------------------------------------------------------------------------------------

        void TickSettled()
        {
            // A scanned mesh of the next room (it comes before that room's BeginRoom) is not this room's to drop.
            bool holdsOwn = ScanFor(_roomId) != null;
            if (_settledRaised && !holdsOwn) return;
            for (int s = 0; s < _slots.Length; s++) if (_slots[s].Entry != null) return;
            for (int i = 0; i < _entries.Length; i++)
            {
                Step step = _entries[i].Step;
                if (step != Step.Placed && step != Step.Kept) return;
            }
            if (holdsOwn && _built)
            {
                // Every object is built: the scanned mesh has done its work (D8).
                _scanMesh = null;
                _scanRoomId = null;
            }
            if (_settledRaised || !_beginSeen || !_built) return;
            int placed = 0;
            for (int i = 0; i < _entries.Length; i++)
            {
                Entry e = _entries[i];
                if (e.Step != Step.Placed) continue;
                // The room content swaps one mesh per frame: the room says what it shows only once it is drawn.
                if (e.Object == null || e.Object.Mesh != e.PlacedState) return;
                placed++;
            }
            _settledRaised = true;
            if (placed == 0) return;
            Action<RoomModel> settled = Settled;
            if (settled != null) settled(_room);
        }

        // ---- The build, on the worker ----------------------------------------------------------------------------

        // Worker thread. Touches only its Work object and the read-only library and scan mesh.
        static void RunWork(object state)
        {
            var work = (Work)state;
            try
            {
                Build(work);
            }
            catch (Exception ex)
            {
                work.Mesh.Clear();
                work.Outcome = Outcome.None;
                work.Reason = "error:" + ex.GetType().Name;
            }
            finally
            {
                // The decoded crops go as soon as the object is coloured (rule 10).
                work.Views = null;
                work.Images = null;
                work.Done = true;
            }
        }

        static void Build(Work work)
        {
            ObjectCategory category = work.Query.Category;
            if (work.SavedProp != null) { Rebuild(work); return; }
            if (work.SavedCutCell >= 0)
            {
                if (!TryScanCut(work, (byte)work.SavedCutCell)) work.Reason = "scan_cut_failed";
                return;
            }
            if (category == ObjectCategory.Unknown || category == ObjectCategory.Other)
            {
                if (!TryScanCut(work, 0)) work.Reason = work.Scan == null ? "no_scan_mesh" : "scan_cut_failed";
                return;
            }
            PropMatch match;
            if (!PropMatcher.Choose(in work.Query, work.Library, out match)) { work.Reason = "no_prop"; return; }
            work.Fit = match.Fit;
            // A poor fit: the object's own shape beats a wrong prop, when the scan has enough of it (D8).
            if (match.Fit > PropMatcher.PoorFit && TryScanCut(work, 0)) return;
            if (TryFit(work, in match, null)) return;
            // Over the triangle cap: the next-best candidate, then the first shape.
            var rest = (PropEntry[])work.Library.Clone();
            rest[match.Index] = null;
            PropMatch second;
            if (PropMatcher.Choose(in work.Query, rest, out second))
            {
                work.Fit = second.Fit;
                if (TryFit(work, in second, null)) return;
            }
            work.Reason = "over_cap";
        }

        // A saved matched object: the same choice as when it was made (best, else the next best, as Build does),
        // so the same quarter turn; the saved cells colour it.
        static void Rebuild(Work work)
        {
            PropEntry[] library = work.Library;
            int saved = -1;
            for (int i = 0; i < library.Length && saved < 0; i++)
                if (library[i] != null && string.Equals(library[i].Id, work.SavedProp, StringComparison.Ordinal)) saved = i;
            if (saved < 0) { work.Reason = "prop_missing"; return; }

            PropMatch match;
            bool found = PropMatcher.Choose(in work.Query, library, out match) && match.Index == saved;
            if (!found && match.Index >= 0)
            {
                var rest = (PropEntry[])library.Clone();
                rest[match.Index] = null;
                found = PropMatcher.Choose(in work.Query, rest, out match) && match.Index == saved;
            }
            if (!found)
            {
                // The library or the box changed since: the saved prop alone decides its turn.
                found = PropMatcher.Choose(in work.Query, new[] { library[saved] }, out match);
                match.Index = saved;
            }
            if (!found) { work.Reason = "prop_category"; return; }
            work.Fit = match.Fit;
            if (!TryFit(work, in match, ValidCells(work.SavedCells, library[saved].SlotCells))) work.Reason = "over_cap";
        }

        // The saved cells when they suit the prop's slots; null (default colours) otherwise.
        static byte[] ValidCells(byte[] cells, byte[] slots)
        {
            if (cells == null || slots == null || cells.Length != slots.Length) return null;
            for (int i = 0; i < cells.Length; i++)
                if (cells[i] < RoomObject.FirstPropCell || cells[i] > RoomObject.LastPropCell) return null;
            return cells;
        }

        // Fits the matched prop, bakes its shade for the object's yaw and colours it: from the crops, or with
        // the given cells.
        static bool TryFit(Work work, in PropMatch match, byte[] cells)
        {
            PropEntry prop = work.Library[match.Index];
            if (!PropFitter.Build(prop, in match, work.Query.Size, work.MaxTriangles, work.Mesh)) return false;
            MeshOps.BakeShade(work.Mesh, work.Yaw);
            if (cells == null) cells = PropColour.Choose(prop, work.Mesh, work.Views, work.Images);
            else cells = (byte[])cells.Clone();
            PropColour.Apply(work.Mesh, prop.SlotCells, cells);
            work.Outcome = Outcome.Matched;
            work.Prop = prop.Id;
            work.Cells = cells;
            work.Turns = match.QuarterTurns;
            return true;
        }

        // The object cut out of the scanned mesh, coloured from the crops (or with the given cell when it is one).
        static bool TryScanCut(Work work, byte cell)
        {
            if (work.Scan == null || work.Box == null) return false;
            if (!ScanCut.Build(work.Scan, work.Box, work.MaxTriangles, work.Mesh)) return false;
            byte fallback = ScanCut.DefaultCell(work.Query.Category);
            if (cell < RoomObject.FirstPropCell || cell > RoomObject.LastPropCell)
                cell = PropColour.ChooseOne(work.Mesh, work.Views, work.Images, fallback);
            if (cell != fallback) PropColour.Apply(work.Mesh, new[] { fallback }, new[] { cell });
            work.Outcome = Outcome.ScanCut;
            work.CutCell = cell;
            return true;
        }

        // ---- Events --------------------------------------------------------------------------------------------

        void LogStep(string step, Entry e, JsonValue data)
        {
            if (_events == null) return;
            data.Set("step", step);
            if (e != null) data.Set("boxId", e.Id);
            _events.LogJson(EventName, data.ToJson());
        }
    }
}
