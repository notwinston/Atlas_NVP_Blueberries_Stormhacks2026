using System;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;

namespace Atlas.Telemetry
{
    /// <summary>What the Station writes across the mirror when it cannot show the walker's world.</summary>
    public enum SpectatorBanner : byte
    {
        None = 0,
        /// <summary>The Hub does not answer.</summary>
        NoHub = 1,
        /// <summary>The Hub is there, but no State has come for two seconds.</summary>
        NoPhone = 2,
        /// <summary>The phone has no room yet, or the room could not be fetched.</summary>
        NoRoom = 3,
        FetchingRoom = 4,
        /// <summary>The room held here is not the phone's (Master Plan section 7.3.1): no space is entered.</summary>
        WorldMismatch = 5,
        /// <summary>The walker is in no space (onboarding, scan).</summary>
        NoSpace = 6,
        /// <summary>The walker's space is not in this build.</summary>
        SpaceMissing = 7,
    }

    /// <summary>
    /// The Spectator's mapping from what the link delivers to what is shown (Master Plan section 7.3.1, task
    /// P1-D05; lane D contract, section 5.8). It keeps the newest State, brings its own registry in step with
    /// the phone's, fetches the room the phone names, compares the world hash, and tells the space host which
    /// space to show. It never integrates motion and never makes up a world: without the phone's room, or with
    /// a room that does not hash to the phone's world, no space is entered. Engine-free.
    /// </summary>
    public sealed class SpectatorCore
    {
        /// <summary>With no State for this long the picture is marked "no phone".</summary>
        public const double StaleSeconds = 2.0;
        /// <summary>A room that could not be fetched, or did not match, is asked for again this often.</summary>
        public const double RefetchSeconds = 2.0;
        /// <summary>A generated world that differs from the phone's for this long is not shown. Shorter than that
        /// it is the phone's world event and its State arriving a frame apart.</summary>
        public const double DisagreeSeconds = 0.5;
        /// <summary>The world generator's seed and version in this build, as the phone hashes them (lane D contract, section 2.5).</summary>
        public const int WorldSeed = 0, GeneratorVersion = 1;

        readonly SpectatorHost _host;
        readonly ParamRegistry _registry;
        readonly IWalkerKernel _kernel;
        readonly ISpectatorRooms _rooms;
        readonly FlatTwinTrail _trail = new FlatTwinTrail();
        readonly RoomOutlineMessage _outline = new RoomOutlineMessage();

        LinkClient _client;
        StateMessage _state = StateMessage.Identity;
        bool _hasState;
        int _arrived;
        double _lastStateAt = double.NegativeInfinity;
        double _rateFrom;
        int _rateCount;

        // What the phone says it holds.
        string _wantRoom = string.Empty, _wantHash = string.Empty, _wantGeometry = string.Empty, _forgeUrl = string.Empty;
        int _wantSpace = -1;
        // The tiling, the generator and the length scale the phone hashed with: named by its world event.
        bool _wantWorld;
        int _wantQ, _wantLMm, _wantGenVer;
        long _wantSeed;
        string _wantContent = string.Empty;
        // The generated world of the entered module against the phone's.
        double _disagreeSince = double.NegativeInfinity, _refusedAt = double.NegativeInfinity;
        bool _disagrees, _worldRefused;
        bool _hasOutline;
        bool _wantDirty;

        // What is held here.
        RoomModel _room;
        string _roomGeometry = string.Empty;
        bool _fetching, _roomOk, _asked, _mismatch;
        double _lastFetchAt = double.NegativeInfinity;

        public SpectatorCore(SpectatorHost host, ParamRegistry registry, IWalkerKernel kernel, ISpectatorRooms rooms)
        {
            if (host == null) throw new ArgumentNullException("host");
            if (registry == null) throw new ArgumentNullException("registry");
            if (kernel == null) throw new ArgumentNullException("kernel");
            if (rooms == null) throw new ArgumentNullException("rooms");
            _host = host;
            _registry = registry;
            _kernel = kernel;
            _rooms = rooms;
            _state.SpaceId = SpaceIds.NoSpace;
            Banner = SpectatorBanner.NoHub;
        }

        // ---- what the view reads -------------------------------------------------------------------------------

        public SpectatorHost Host { get { return _host; } }
        /// <summary>True once a State has arrived.</summary>
        public bool HasState { get { return _hasState; } }
        /// <summary>The newest State.</summary>
        public ref readonly StateMessage State { get { return ref _state; } }
        public SpectatorBanner Banner { get; private set; }
        /// <summary>The walked path and tile sequence, from every State received.</summary>
        public FlatTwinTrail Trail { get { return _trail; } }
        /// <summary>The phone's room outline message, or null before the first.</summary>
        public RoomOutlineMessage Outline { get { return _hasOutline ? _outline : null; } }
        /// <summary>+1 on every outline message received.</summary>
        public int OutlineRevision { get; private set; }
        /// <summary>The room shown, or null.</summary>
        public RoomModel Room { get { return _roomOk ? _room : null; } }
        /// <summary>The Forge's base URL as the Hub's Welcome named it; empty before the first.</summary>
        public string ForgeUrl { get { return _forgeUrl; } }
        /// <summary>The room id the phone names; empty when it names none.</summary>
        public string RoomId { get { return _wantRoom; } }
        /// <summary>True when the phone gave a hash and the room held here hashes to it. False for a room taken on its id or outline alone.</summary>
        public bool WorldVerified { get; private set; }
        /// <summary>States received per second, over the last second.</summary>
        public float StateHz { get; private set; }
        public long StatesReceived { get; private set; }
        /// <summary>Parameter values changed here by the phone's manifest and events.</summary>
        public int ParamsApplied { get; private set; }
        /// <summary>Room searches begun.</summary>
        public int RoomRequests { get; private set; }

        /// <summary>
        /// The tiling and the length scale the phone named for its world in a space: q and the dial's target L
        /// (or R) in metres. False when its last world event was for another space or named neither.
        /// </summary>
        public bool TryGetWorld(SpaceId space, out int q, out double metresPerUnit)
        {
            q = _wantQ;
            metresPerUnit = _wantLMm / 1000.0;
            return _wantWorld && _wantSpace == (int)space;
        }

        // ---- the link ------------------------------------------------------------------------------------------

        /// <summary>Subscribes to a link client of role spectator. Its events are raised inside its Poll.</summary>
        public void Attach(LinkClient client)
        {
            if (client == null) throw new ArgumentNullException("client");
            Detach();
            _client = client;
            client.Welcomed += OnWelcome;
            client.StateReceived += OnState;
            client.ManifestReceived += OnManifest;
            client.EventReceived += OnEvent;
            client.RoomOutlineReceived += OnRoomOutline;
            if (client.Welcome != null) OnWelcome(client.Welcome);
        }

        public void Detach()
        {
            LinkClient client = _client;
            _client = null;
            if (client == null) return;
            client.Welcomed -= OnWelcome;
            client.StateReceived -= OnState;
            client.ManifestReceived -= OnManifest;
            client.EventReceived -= OnEvent;
            client.RoomOutlineReceived -= OnRoomOutline;
        }

        /// <summary>The Hub's Welcome: the Forge, and the room and world of the phone when it has said them.</summary>
        public void OnWelcome(WelcomeMessage welcome)
        {
            if (welcome == null) return;
            if (!string.IsNullOrEmpty(welcome.ForgeUrl)) _forgeUrl = welcome.ForgeUrl;
            // A Welcome sent before the phone arrived names nothing; it does not unsay what a world event said.
            if (string.IsNullOrEmpty(welcome.RoomId) && string.IsNullOrEmpty(welcome.WorldHash)) return;
            // A Welcome names the hash alone: what a world event said of it stands only while the hash is the same.
            if (!string.Equals(welcome.WorldHash ?? string.Empty, _wantHash, StringComparison.Ordinal)) _wantWorld = false;
            Want(welcome.RoomId ?? string.Empty, welcome.WorldHash ?? string.Empty, null, -1);
        }

        /// <summary>A relayed State: the newest replaces the one held. No interpolation, no prediction.</summary>
        public void OnState(in StateMessage state)
        {
            _state = state;
            _hasState = true;
            _arrived++;
            StatesReceived++;
            _trail.Add(in state);
        }

        /// <summary>
        /// The phone's manifest: every value this registry knows by name, and its group's state. The Station's
        /// own link settings are left alone.
        /// </summary>
        public void OnManifest(ParamManifestMessage manifest)
        {
            if (manifest == null) return;
            for (int i = 0; i < manifest.Params.Count; i++)
            {
                ParamManifestEntry e = manifest.Params[i];
                ParamEntry entry;
                if (IsOwn(e.Name) || !_registry.TryGet(e.Name, out entry)) continue;
                if (_registry.IsGroupActive(entry.Group) != e.Active) _registry.SetGroupActive(entry.Group, e.Active);
                if (_registry.Set(entry, e.Val, ParamSource.Remote)) ParamsApplied++;
            }
        }

        // The phone's link and telemetry switches describe the phone's side of the link, not this one.
        static bool IsOwn(string name)
        {
            return name == null || name.StartsWith("link.", StringComparison.Ordinal) || name.StartsWith("telemetry.", StringComparison.Ordinal);
        }

        /// <summary>A relayed event of the phone: a parameter change or a world change. Others are ignored.</summary>
        public void OnEvent(EventMessage message)
        {
            if (message == null) return;
            ushort id;
            string name;
            float value;
            if (LinkEvents.TryReadParam(message, out id, out name, out value))
            {
                ParamEntry entry;
                if (!IsOwn(name) && _registry.TryGet(name, out entry) && _registry.Set(entry, value, ParamSource.Remote)) ParamsApplied++;
                return;
            }
            if (message.Ev != LinkEvents.World || message.Data == null || !message.Data.IsObject) return;
            JsonValue data = message.Data;
            // A world event of an older build carries only the version and the space.
            string room = data.GetString("roomId", null);
            Want(room ?? _wantRoom, data.GetString("worldHash", string.Empty), data.GetString("geomHash", null), data.GetInt("space", -1));
            // A curved space hashes with its own tiling, generator seed and length scale (section 2.5). They are the
            // phone's to choose and are taken as told; the room they are hashed with stays the one held here.
            _wantWorld = data.Has("q") && data.Has("seed") && data.Has("genVer") && data.Has("lMm");
            if (_wantWorld)
            {
                _wantQ = data.GetInt("q", 4);
                _wantSeed = data.GetLong("seed", WorldSeed);
                _wantGenVer = data.GetInt("genVer", GeneratorVersion);
                _wantLMm = data.GetInt("lMm", 0);
            }
            // What the phone's generator made (WorldGenerator.WorldHash); the same module makes it again here.
            _wantContent = data.GetString("genHash", null) ?? string.Empty;
            _worldRefused = false;
        }

        /// <summary>The phone's room outline. The message object is the link's own: it is copied.</summary>
        public void OnRoomOutline(RoomOutlineMessage outline)
        {
            if (outline == null) return;
            _outline.Clear();
            for (int i = 0; i < outline.PointCount; i++) _outline.AddPoint(outline.PointsXZ[2 * i], outline.PointsXZ[2 * i + 1]);
            for (int i = 0; i < outline.BoxCount; i++) _outline.AddBox(in outline.Boxes[i]);
            _outline.Height = outline.Height;
            _outline.RectCx = outline.RectCx;
            _outline.RectCz = outline.RectCz;
            _outline.RectW = outline.RectW;
            _outline.RectD = outline.RectD;
            _outline.RectYaw = outline.RectYaw;
            _hasOutline = true;
            OutlineRevision++;
            _wantDirty = true;
        }

        void Want(string roomId, string worldHash, string geometryHash, int space)
        {
            if (!string.Equals(roomId, _wantRoom, StringComparison.Ordinal))
            {
                _wantRoom = roomId;
                _asked = false;                 // another room: fetch it now
            }
            string hash = worldHash ?? string.Empty, geometry = geometryHash ?? string.Empty;
            if (string.Equals(geometry, WorldHash.NoGeometry, StringComparison.Ordinal)) geometry = string.Empty;
            // The phone says something new about its world: a room that did not match is fetched again now.
            if (!string.Equals(hash, _wantHash, StringComparison.Ordinal) || !string.Equals(geometry, _wantGeometry, StringComparison.Ordinal))
                _asked = false;
            _wantHash = hash;
            _wantGeometry = geometry;
            _wantSpace = space;
            _wantDirty = true;
        }

        // ---- once per frame ------------------------------------------------------------------------------------

        /// <summary>
        /// Brings the room and the space in step with the phone and decides the banner. Called once per frame,
        /// before the entered module is ticked. Nothing here waits.
        /// </summary>
        /// <param name="now">Seconds on the Station's clock.</param>
        /// <param name="linkUp">The link's watchdog is not offline.</param>
        public void Step(double now, bool linkUp)
        {
            if (_arrived > 0)
            {
                _lastStateAt = now;
                _rateCount += _arrived;
                _arrived = 0;
            }
            if (now - _rateFrom >= 1.0)
            {
                StateHz = (float)(_rateCount / (now - _rateFrom));
                _rateCount = 0;
                _rateFrom = now;
            }
            _host.WorldVersion = _state.WorldVer;

            StepRoom(now);
            bool live = _hasState && now - _lastStateAt <= StaleSeconds;
            SpectatorBanner space = StepSpace(now);

            // The first thing that is wrong is the one worth reading.
            if (!linkUp) Banner = SpectatorBanner.NoHub;
            else if (!live) Banner = SpectatorBanner.NoPhone;
            else Banner = space;
        }

        void StepRoom(double now)
        {
            bool haveLead = !string.IsNullOrEmpty(_wantRoom) || _hasOutline;

            // A held room is checked again whenever the phone says something new about its world.
            if (_wantDirty && _roomOk && !Verify(_room))
            {
                _roomOk = false;
                _mismatch = true;
                _host.SetRoom(null);
                _asked = false;
            }
            _wantDirty = false;

            if (!_fetching && !_roomOk && haveLead && (!_asked || now - _lastFetchAt >= RefetchSeconds))
            {
                _asked = true;
                _lastFetchAt = now;
                _fetching = true;
                RoomRequests++;
                _rooms.Begin(_wantRoom, _forgeUrl, _hasOutline ? _outline : null);
            }
            // A fixture is there at once; the Forge answers on a later frame.
            if (_fetching)
            {
                RoomModel room;
                if (!_rooms.TryTake(out room)) return;
                _fetching = false;
                Take(room);
            }
        }

        void Take(RoomModel room)
        {
            _room = room;
            _roomGeometry = string.Empty;
            _mismatch = false;
            if (room == null) return;
            try { _roomGeometry = room.Sdf != null ? RoomModelJson.GeometryHash(room) : string.Empty; }
            catch (Exception) { _roomGeometry = string.Empty; }
            if (!Verify(room))
            {
                _mismatch = true;
                return;
            }
            _roomOk = true;
            if (!ReferenceEquals(_host.Room, room)) _host.SetRoom(room);
        }

        // The world check of section 7.3.1. Every statement the phone has made about its room must hold for
        // the room held here: the geometry hash, the world hash, and the floor of its outline message.
        bool Verify(RoomModel room)
        {
            WorldVerified = false;
            if (room == null) return false;
            if (_wantGeometry.Length > 0 && !string.Equals(_wantGeometry, _roomGeometry, StringComparison.Ordinal)) return false;
            if (_hasOutline && !SpectatorRoomMatch.Agrees(room, _outline)) return false;
            if (_wantHash.Length == 0)
            {
                WorldVerified = _wantGeometry.Length > 0;
                return true;
            }
            ITileGraph graph = _kernel.Graph;
            int q = graph != null ? graph.Q : 4;
            int lMm = WorldHash.Millimetres(_kernel.MetresPerUnit);
            long seed = WorldSeed;
            int genVer = GeneratorVersion;
            if (_wantWorld)
            {
                q = _wantQ;
                lMm = _wantLMm;
                seed = _wantSeed;
                genVer = _wantGenVer;
            }
            if (_wantSpace >= 0)
            {
                if (!HashIs(_wantSpace, q, lMm, seed, genVer)) return false;
            }
            else
            {
                // A Welcome names the hash but not the space it was made for: it must be one of them.
                bool any = false;
                for (int s = 0; s < SpaceIds.Count && !any; s++) any = HashIs(s, q, lMm, seed, genVer);
                if (!any) return false;
            }
            WorldVerified = true;
            return true;
        }

        bool HashIs(int space, int q, int lMm, long seed, int genVer)
        {
            string own = WorldHash.Compute(space, q, seed, genVer, _roomGeometry, lMm);
            return string.Equals(own, _wantHash, StringComparison.Ordinal);
        }

        SpectatorBanner StepSpace(double now)
        {
            if (!_roomOk)
            {
                if (_host.HasSpace) _host.Leave();
                if (_mismatch) return SpectatorBanner.WorldMismatch;
                return _fetching ? SpectatorBanner.FetchingRoom : SpectatorBanner.NoRoom;
            }
            if (!_hasState) return SpectatorBanner.NoPhone;
            byte id = _state.SpaceId;
            if (id >= SpaceIds.Count)
            {
                if (_host.HasSpace) _host.Leave();
                return SpectatorBanner.NoSpace;
            }
            var target = (SpaceId)id;
            // A world that was generated differently here is tried again as a room is: every two seconds.
            if (_worldRefused && now - _refusedAt < RefetchSeconds) return SpectatorBanner.WorldMismatch;
            _worldRefused = false;
            if (!_host.HasSpace || _host.Current != target)
            {
                _disagrees = false;
                if (!_host.RequestSwitch(target))
                    return _host.HasSpace && _host.Current == target ? SpectatorBanner.None : SpectatorBanner.SpaceMissing;
            }
            if (GeneratorAgrees())
            {
                _disagrees = false;
                return SpectatorBanner.None;
            }
            if (!_disagrees)
            {
                _disagrees = true;
                _disagreeSince = now;
            }
            if (now - _disagreeSince < DisagreeSeconds) return SpectatorBanner.None;
            // The same room and another generated world: not the walker's world, so it is not shown.
            _disagrees = false;
            _worldRefused = true;
            _refusedAt = now;
            _host.Leave();
            return SpectatorBanner.WorldMismatch;
        }

        // The second half of the world check (section 7.3.1): the entered module generates its world itself, from
        // the room held here and the parameters it was sent, and what it made must be what the phone named: the
        // seed, the generator's version and, where both report one, the hash of the generated content.
        bool GeneratorAgrees()
        {
            var own = _host.Module as ISpaceWorld;
            if (own == null || !_wantWorld || !_host.HasSpace || _wantSpace != (int)_host.Current) return true;
            if (own.WorldSeed != _wantSeed || own.GeneratorVersion != _wantGenVer) return false;
            string content = own.ContentHash;
            return string.IsNullOrEmpty(content) || _wantContent.Length == 0
                || string.Equals(content, _wantContent, StringComparison.Ordinal);
        }

        /// <summary>The words of a banner, for the Station's panel.</summary>
        public static string BannerText(SpectatorBanner banner)
        {
            switch (banner)
            {
                case SpectatorBanner.NoHub: return "no hub";
                case SpectatorBanner.NoPhone: return "no phone";
                case SpectatorBanner.NoRoom: return "no room";
                case SpectatorBanner.FetchingRoom: return "fetching room";
                case SpectatorBanner.WorldMismatch: return "world mismatch";
                case SpectatorBanner.NoSpace: return "no space";
                case SpectatorBanner.SpaceMissing: return "space not in this build";
                default: return string.Empty;
            }
        }
    }
}
