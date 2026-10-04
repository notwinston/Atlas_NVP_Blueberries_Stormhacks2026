using System;
using System.Text;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;

namespace Atlas.Telemetry
{
    /// <summary>Where the once-per-second telemetry row goes (<c>telemetry.mode</c>, Master Plan section 12.2).</summary>
    public enum TelemetryMode : byte
    {
        /// <summary>To the Station and to the phone's CSV.</summary>
        Live = 0,
        /// <summary>To the phone's CSV only.</summary>
        PhoneCsv = 1,
        /// <summary>Nowhere; the HUD is forced on.</summary>
        Hud = 2
    }

    /// <summary>
    /// The link driver of the app: step 8 of the frame (Master Plan section 6.3). It owns the
    /// <see cref="LinkClient"/>, sends the frame's <c>State</c> at step 8a, and at step 8b polls the link, which
    /// applies received parameter sets to the registry, hands operator commands to <see cref="IOperatorCommands"/>
    /// and sends what was waiting. It also owns the echo of parameter changes (section 7.3.2), the world hash
    /// (section 7.3.1) and the re-sending of the manifest when the registry's shape changed.
    /// <para>
    /// Nothing here blocks and the steady frame path allocates nothing. With no Station, or with the link
    /// switched off, the app runs unchanged (section 7.5, rule 4). Operator commands leave as plain calls on an
    /// interface of <c>Atlas.World</c>, so nothing downstream, Safety least of all, sees a link type.
    /// </para>
    /// </summary>
    public sealed class LinkPump : IStateSink, IAfterFrameStep
    {
        /// <summary>Seconds between connect attempts while the walker is pairing, phone in hand (Appendix C.7).</summary>
        public const double RetrySeconds = 1.0;
        /// <summary>Telemetry rows kept while offline: ten minutes.</summary>
        public const int RowBacklog = 600;

        const long RetryUs = (long)(RetrySeconds * 1000000.0);
        const long SecondUs = 1000000;
        const int SendsPerTick = 16;
        const byte NoSpace = SpaceIds.NoSpace;

        readonly LinkClient _client;
        readonly EventLog _events;
        readonly LogFileWriter _files;
        readonly ParamRegistry _registry;
        readonly StringBuilder _csv = new StringBuilder(512);
        readonly TelemetryMessage[] _rows = new TelemetryMessage[RowBacklog];
        readonly RoomOutlineMessage _outline = new RoomOutlineMessage();
        int _rowHead, _rowCount;
        bool _hasOutline, _outlineSent;

        // Parameters whose owner writes them every frame (contract section 1.4, flag L). Their values travel in
        // State.p[3..6]; a change made by code raises no event.
        static readonly string[] s_liveNames = { "slicer.w", "slicer.xw", "slicer.yw", "slicer.zw" };

        bool[] _dirty = new bool[64];
        bool[] _live = new bool[64];
        int _dirtyCount;
        long _lastParamFlushUs;
        int _manifestSent;

        // The world this phone holds, for the world hash.
        string _roomId = string.Empty;
        string _geometryHash;

        string _host;
        int _port = LinkProtocol.DefaultPort;
        bool _started;
        long _lastKickUs;

        // What step 8a remembers of the previous State, to log the edges.
        byte _prevBump;
        int _prevTracking;
        bool _prevJump, _prevCue;
        byte _prevSpace = NoSpace;
        ushort _prevSceneId;
        int _prevWorldVer = -1;
        float _p3, _p4;

        // Gathered for the telemetry row of the running second.
        int _stateSent;
        byte _bumpMax;
        ushort _cueCount;

        /// <param name="client">The link client; the driver is its only caller from here on.</param>
        /// <param name="events">The event log, or null.</param>
        /// <param name="files">The writer of the perf CSV, or null to keep rows off the disk.</param>
        public LinkPump(LinkClient client, EventLog events = null, LogFileWriter files = null)
        {
            if (client == null) throw new ArgumentNullException("client");
            _client = client;
            _events = events;
            _files = files;
            _registry = client.Registry;
            _client.StateChanged += OnLinkStateChanged;
            if (_registry != null)
            {
                // The driver owns the echo: the client's own would build JSON wherever a value is written, the
                // frame's steps 1 to 7 included (rule 8 of the plan).
                _client.EchoParamChanges = false;
                _client.ParamSetIgnored += OnParamSetIgnored;
                _registry.Changed += OnParamChanged;
                _registry.Declared += OnParamDeclared;
                for (int i = 0; i < _registry.Count; i++) OnParamDeclared(_registry[i]);
                _manifestSent = _registry.ManifestVersion;
            }
            if (_events != null) _events.Link = client;
        }

        public LinkClient Client { get { return _client; } }

        /// <summary>OFFLINE, RESYNC, CONNECTED or DEGRADED.</summary>
        public LinkState State { get { return _client.State; } }

        /// <summary>The link clock, microseconds.</summary>
        public long NowUs { get { return _client.NowUs; } }

        /// <summary>True between <see cref="Start()"/> and <see cref="Stop"/>.</summary>
        public bool IsStarted { get { return _started; } }

        public string Host { get { return _host; } }
        public int Port { get { return _port; } }

        /// <summary>
        /// Where operator commands go. While null, commands wait in the link's queue (the newest 32) and can be
        /// taken with <see cref="TryDequeueCommand"/>.
        /// </summary>
        public IOperatorCommands Commands { get; set; }

        public TelemetryMode Mode { get; set; }

        /// <summary>Ticked once per frame when set; <see cref="PairingController"/> sets it itself.</summary>
        public PairingController Pairing { get; set; }

        /// <summary>
        /// The walker's kernel, for the world hash (q and metres per unit). While null the values of the flat
        /// kernel are used: q 4, one metre per unit.
        /// </summary>
        public IWalkerKernel Kernel { get; set; }

        /// <summary>
        /// The space host, for the world hash: an entered module that is an ISpaceWorld gives the generator's
        /// seed and version, and its content hash goes into the <c>world</c> event. May be null (seed 0, version 1).
        /// </summary>
        public ISpaceHost Spaces { get; set; }

        /// <summary>The world hash last computed: 16 hexadecimal digits, or empty while no space is entered.</summary>
        public string WorldHashNow { get { return _client.WorldHash; } }

        /// <summary>Raised at step 8a when <c>State.spaceId</c> changed, with (previous, current); 255 is "no space".</summary>
        public event Action<byte, byte> SpaceChanged;

        /// <summary>The Bump Guard level of the newest <c>State</c>, 0 to 3.</summary>
        public int BumpLevel { get { return _prevBump; } }

        /// <summary>Operator commands handed over since start.</summary>
        public int CommandsApplied { get; private set; }

        /// <summary>Telemetry rows waiting for the link.</summary>
        public int RowsWaiting { get { return _rowCount; } }

        // ---- lifecycle ---------------------------------------------------------------------------------

        /// <summary>Sets the Station address. A running link drops its peer and connects to the new one at once.</summary>
        public void SetAddress(string host, int port)
        {
            _host = host;
            _port = port;
            if (_started)
            {
                _client.SetAddress(host, port);
                _lastKickUs = _client.NowUs;
            }
        }

        /// <summary>Starts the link towards the address given to <see cref="SetAddress"/>. False when there is none or the socket cannot open.</summary>
        public bool Start()
        {
            if (_started) return true;
            if (string.IsNullOrEmpty(_host)) return false;
            if (!_client.Start(_host, _port)) return false;
            _started = true;
            _lastKickUs = _client.NowUs;
            return true;
        }

        public bool Start(string host, int port)
        {
            _host = host;
            _port = port;
            return Start();
        }

        /// <summary>Says <c>Bye</c> and closes the socket (<c>link.enabled</c> off). The rest of step 8 keeps running.</summary>
        public void Stop(string reason = "stop")
        {
            if (!_started) return;
            _started = false;
            _client.Stop(reason);
        }

        /// <summary>Tries to connect now: the "Reconnect" button, and the return from the background.</summary>
        public void ReconnectNow()
        {
            if (!_started) return;
            _client.ReconnectNow();
            _lastKickUs = _client.NowUs;
        }

        // ---- step 8a -----------------------------------------------------------------------------------

        /// <summary>
        /// Step 8a: logs the edges of the frame's <c>State</c> (Bump Guard level, tracking, pose jump, space and
        /// world changes), keeps the second's figures for the telemetry row, and sends the state. Allocates
        /// nothing unless an edge is logged.
        /// </summary>
        public void Send(in StateMessage state)
        {
            byte bump = state.BumpLevel;
            bool cue = state.CueActive;
            if (bump != _prevBump)
            {
                // Every rise of the level starts a cue; so does a cue with no rise (the operator's STOP).
                if (bump > _prevBump) _cueCount++;
                if (_events != null) _events.Log("bump.level", "level", bump, "prev", _prevBump);
                _prevBump = bump;
            }
            else if (cue && !_prevCue) _cueCount++;
            _prevCue = cue;
            if (bump > _bumpMax) _bumpMax = bump;

            int tracking = state.Tracking;
            if (tracking != _prevTracking)
            {
                if (_events != null)
                {
                    if (_prevTracking == 0) _events.Log("tracking.lost", "state", tracking);
                    else if (tracking == 0) _events.Log("tracking.back");
                }
                _prevTracking = tracking;
            }

            bool jump = state.PoseJumpRejected;
            if (jump && !_prevJump && _events != null) _events.Log("pose.jump");
            _prevJump = jump;

            byte space = state.SpaceId;
            bool spaceChanged = space != _prevSpace;
            if (spaceChanged || state.WorldVer != _prevWorldVer) OnWorldChanged(in state, spaceChanged);
            _prevSceneId = state.SceneId;
            _p3 = state.P3;
            _p4 = state.P4;

            // Only the walker's app is a source of State; a Spectator or an Editor that watches sends none.
            if (_started && _client.Role == LinkRole.Phone && _client.SendStateCopy(in state)) _stateSent++;
        }

        void OnWorldChanged(in StateMessage state, bool spaceChanged)
        {
            byte previous = _prevSpace;
            byte space = state.SpaceId;
            _prevSpace = space;
            _prevWorldVer = state.WorldVer;
            string scene = SceneOf(space);
            _client.Scene = scene;

            // The world hash (section 7.3.1): into Hello, so the Hub can tell a Spectator, and into the event.
            int q, lMm, genVer;
            long seed;
            string genHash;
            string hash = ComputeWorldHash(space, out q, out lMm, out seed, out genVer, out genHash);
            _client.SetWorld(hash, _roomId);
            if (_events != null)
            {
                _events.Scene = scene;
                if (spaceChanged && space != NoSpace) _events.Log("space.entered", JsonValue.Object().Set("from", SceneOf(previous)).Set("to", scene));
                JsonValue world = JsonValue.Object()
                    .Set("worldVer", (int)state.WorldVer)
                    .Set("worldHash", hash)
                    .Set("space", (int)space)
                    .Set("roomId", _roomId)
                    .Set("geomHash", string.IsNullOrEmpty(_geometryHash) ? WorldHash.NoGeometry : _geometryHash)
                    .Set("q", q)
                    .Set("seed", seed)
                    .Set("genVer", genVer)
                    .Set("lMm", lMm);
                // The generator's own hash of what it made: the same on every runtime for the same seed and
                // room, which is what the IL2CPP check of P1-B07 compares with the golden file.
                if (genHash.Length > 0) world.Set("genHash", genHash);
                _events.Log(LinkEvents.World, world);
            }
            if (spaceChanged)
            {
                Action<byte, byte> changed = SpaceChanged;
                if (changed != null) changed(previous, space);
            }
        }

        // A space with no generator: seed 0, the first version (contract section 2.5).
        const long NoWorldSeed = 0;
        const int FirstGeneratorVersion = 1;

        // Runs on a change of space, world version or room, never per frame. Empty while no space is entered:
        // there is no world to compare.
        string ComputeWorldHash(byte space, out int q, out int lMm, out long seed, out int genVer, out string genHash)
        {
            IWalkerKernel kernel = Kernel;
            q = kernel != null && kernel.Graph != null ? kernel.Graph.Q : 4;
            // The dial's target, not the scale of this frame: L moves through a curvature ramp and is one metre
            // at K = 0, and the phone and the Spectator must agree whenever the event happens to fire.
            IDialKernel dial = kernel as IDialKernel;
            lMm = WorldHash.Millimetres(dial != null ? dial.ConfiguredMetresPerUnit : (kernel != null ? kernel.MetresPerUnit : 1.0));

            seed = NoWorldSeed;
            genVer = FirstGeneratorVersion;
            genHash = string.Empty;
            ISpaceHost host = Spaces;
            ISpaceWorld generated = host != null && host.HasSpace && space != NoSpace ? host.Module as ISpaceWorld : null;
            if (generated != null)
            {
                seed = generated.WorldSeed;
                genVer = generated.GeneratorVersion;
                genHash = generated.ContentHash ?? string.Empty;
            }
            if (space == NoSpace) return string.Empty;
            return WorldHash.Compute(space, q, seed, genVer, _geometryHash, lMm);
        }

        static string SceneOf(byte space)
        {
            return space < SpaceIds.Count ? SpaceIds.Code((SpaceId)space) : EventLog.NoScene;
        }

        // ---- step 8b -----------------------------------------------------------------------------------

        /// <summary>Step 8b. See <see cref="Tick"/>.</summary>
        public void AfterFrame(in FrameContext frame) { Tick(); }

        /// <summary>
        /// The frame tail: polls the link (received parameter sets are clamped and applied by the registry inside
        /// the poll), retries the connection, hands over operator commands, and sends waiting events and rows.
        /// Never blocks; a quiet tick allocates nothing.
        /// </summary>
        public void Tick()
        {
            long now = _client.NowUs;
            if (_started)
            {
                // While the walker is pairing, phone in hand, the Station gets one attempt per second (Appendix
                // C.7). At every other time the client's own back-off (0.5, 1, 2, 4, 4 ... s) decides: each
                // attempt costs the transport an end point, and a session with no Station must stay free of
                // that (section 7.5, rule 4).
                if (_client.State == LinkState.Offline && now - _lastKickUs >= RetryUs && IsPairing())
                {
                    _lastKickUs = now;
                    _client.ReconnectNow();
                }
                _client.Poll();

                IOperatorCommands commands = Commands;
                if (commands != null)
                {
                    OperatorCommand command;
                    while (_client.TryDequeueCommand(out command)) Apply(commands, in command, now);
                }

                if (_client.IsReady)
                {
                    // The manifest again when a group was switched or a range changed, so the Console and the
                    // Spectator fold and grey the right sliders. A version compare on every other frame.
                    if (_registry != null && _registry.ManifestVersion != _manifestSent && _client.Role == LinkRole.Phone)
                    {
                        int version = _registry.ManifestVersion;
                        if (_client.SendManifest()) _manifestSent = version;
                    }
                    if (_hasOutline && !_outlineSent) _outlineSent = _client.SendRoomOutline(_outline);
                    if (_events != null) _events.SendQueued(SendsPerTick);
                    SendWaitingRows(SendsPerTick);
                }
            }

            if (_dirtyCount > 0 && now - _lastParamFlushUs >= SecondUs) FlushLocalParams(now);

            PairingController pairing = Pairing;
            if (pairing != null) pairing.Tick(now, _client.State);
        }

        bool IsPairing()
        {
            PairingController pairing = Pairing;
            return pairing != null && (pairing.Phase == PairingPhase.Trying || pairing.Phase == PairingPhase.Hint);
        }

        // ---- operator commands -------------------------------------------------------------------------

        /// <summary>
        /// Takes the oldest operator command, for a composition root that applies commands itself instead of
        /// setting <see cref="Commands"/>. The command is written to the event log here.
        /// </summary>
        public bool TryDequeueCommand(out OperatorCommand command)
        {
            if (!_client.TryDequeueCommand(out command)) return false;
            CommandsApplied++;
            if (_events != null) _events.Log(command.ToEvent(_client.NowUs, _client.Scene));
            return true;
        }

        // The Map page repeats a held walk input several times a second; only a change of input is logged.
        OperatorCommand _lastWalk;
        bool _walkLogged;
        // The Hub repeats "view on" every second while anyone watches; only a change is logged.
        bool _viewOn;

        void Apply(IOperatorCommands commands, in OperatorCommand command, long now)
        {
            bool log = true;
            try
            {
                switch (command.Kind)
                {
                    case OperatorCommandKind.Walk:
                        commands.Walk(command.MoveX, command.MoveY, command.Turn, command.Speed);
                        log = !_walkLogged || command.MoveX != _lastWalk.MoveX || command.MoveY != _lastWalk.MoveY ||
                              command.Turn != _lastWalk.Turn || command.Speed != _lastWalk.Speed;
                        _lastWalk = command;
                        _walkLogged = true;
                        break;
                    case OperatorCommandKind.View:
                        commands.View(command.On, command.Port);
                        log = command.On != _viewOn;
                        _viewOn = command.On;
                        break;
                    case OperatorCommandKind.SwitchSpace:
                        ApplyArgs(command.Args);
                        if (command.Space >= 0 && command.Space < SpaceIds.Count)
                            commands.SwitchSpace((SpaceId)command.Space, string.IsNullOrEmpty(command.Variant) ? null : command.Variant);
                        break;
                    case OperatorCommandKind.Home: commands.Home(); break;
                    case OperatorCommandKind.Recentre: commands.Recentre(); break;
                    case OperatorCommandKind.Quality: commands.SetQuality(command.Tier); break;
                    case OperatorCommandKind.Cue: commands.Cue(); break;
                    case OperatorCommandKind.Lock: commands.SetLock(command.On); break;
                    // Hands on or off sets the parameter itself, as a SceneCmd's args do, so the demo lock does not stop it.
                    case OperatorCommandKind.Hands:
                        if (_registry != null) _registry.Set("hands.enabled", command.On ? 1f : 0f, ParamSource.Remote);
                        break;
                    default: return;
                }
            }
            catch (Exception e)
            {
                // A handler that throws must not stop the frame tail or the commands behind it.
                if (_events != null) _events.LogLine("error", "operator command " + command.Name + ": " + e.Message);
            }
            CommandsApplied++;
            if (log && _events != null) _events.Log(command.ToEvent(now, _client.Scene));
        }

        // A SceneCmd may carry parameter values for the space it switches to; they are part of the command, so
        // they apply under demo lock as the command itself does.
        void ApplyArgs(JsonValue args)
        {
            if (_registry == null || args == null || !args.IsObject) return;
            for (int i = 0; i < args.Count; i++)
            {
                JsonValue v = args[i];
                if (v.IsNumber) _registry.Set(args.KeyAt(i), v.AsFloat(), ParamSource.Remote);
                else if (v.IsBool) _registry.Set(args.KeyAt(i), v.AsBool() ? 1f : 0f, ParamSource.Remote);
            }
        }

        // ---- parameters --------------------------------------------------------------------------------

        // The echo of section 7.3.2 and the phone's own log. A change made by the Station, a preset or a person
        // goes out at once: it arrives at step 8b or from a menu, never from the frame's steps. A value that
        // code writes (Local) only marks a flag here, which costs nothing, and is reported at step 8b at most
        // once per second; a live parameter written by code is not reported at all.
        void OnParamChanged(ParamEntry entry, ParamSource source)
        {
            if (source != ParamSource.Local)
            {
                Report(LinkEvents.ParamChanged(_client.NowUs, _client.Scene, entry, source));
                return;
            }
            int id = entry.Id;
            if (id < _live.Length && _live[id]) return;
            if (id >= _dirty.Length) Array.Resize(ref _dirty, Math.Max(id + 1, _dirty.Length * 2));
            if (_dirty[id]) return;
            _dirty[id] = true;
            _dirtyCount++;
        }

        // A ParamSet dropped under demo lock or on a degraded link is said out loud (lane D contract 2.6 and
        // 2.10): the Hub puts its record of those values back, and the phone's own log shows the refusal.
        void OnParamSetIgnored(string reason, int count)
        {
            Report(LinkEvents.ParamIgnored(_client.NowUs, _client.Scene, reason, _client.IgnoredParamSet));
        }

        void OnParamDeclared(ParamEntry entry)
        {
            if (entry == null || Array.IndexOf(s_liveNames, entry.Name) < 0) return;
            int id = entry.Id;
            if (id >= _live.Length) Array.Resize(ref _live, Math.Max(id + 1, _live.Length * 2));
            _live[id] = true;
        }

        void FlushLocalParams(long now)
        {
            _lastParamFlushUs = now;
            for (int id = 0; id < _dirty.Length && _dirtyCount > 0; id++)
            {
                if (!_dirty[id]) continue;
                _dirty[id] = false;
                _dirtyCount--;
                ParamEntry entry;
                if (_registry.TryGet((ushort)id, out entry))
                    Report(LinkEvents.ParamChanged(now, _client.Scene, entry, ParamSource.Local));
            }
        }

        // To the phone's file always; to the Station while the link is up. A change made while the Station is
        // away is not kept for it: the manifest of the next handshake carries the values of that moment.
        void Report(EventMessage message)
        {
            bool toLink = _client.IsReady && _client.Role == LinkRole.Phone;
            if (_events != null)
            {
                if (toLink) _events.Log(message);
                else _events.LogLocal(message);
            }
            else if (toLink) _client.Send(message);
        }

        // ---- link state --------------------------------------------------------------------------------

        void OnLinkStateChanged(LinkState previous, LinkState current)
        {
            // A new handshake: the Station has forgotten the room.
            if (current == LinkState.Resync || current == LinkState.Offline) _outlineSent = false;
            // The client has just sent the manifest as the last step of its handshake.
            else if (previous == LinkState.Resync && _registry != null) _manifestSent = _registry.ManifestVersion;
            if (_events != null)
                _events.Log(LinkEvents.Link, JsonValue.Object().Set("state", StateName(current)).Set("prev", StateName(previous)));
        }

        public static string StateName(LinkState state)
        {
            switch (state)
            {
                case LinkState.Resync: return "resync";
                case LinkState.Connected: return "connected";
                case LinkState.Degraded: return "degraded";
                default: return "offline";
            }
        }

        // ---- telemetry rows ----------------------------------------------------------------------------

        /// <summary>
        /// Fills the row fields the driver knows (scene, space, link figures, <c>State</c> rate, the second's
        /// highest Bump Guard level, the cue count, Glued copies) and starts a new second.
        /// </summary>
        public void TakeSecond(ref PerfRowContext context)
        {
            context.SceneId = _prevSceneId;
            context.SpaceId = _prevSpace == NoSpace ? (byte)0 : _prevSpace;
            context.RttMs = (ushort)Clamp(_client.RttMs, 0, ushort.MaxValue);
            context.LossPermille = (ushort)Clamp(_client.LossPermille, 0, 1000);
            context.StateHz = (byte)Clamp(_stateSent, 0, 255);
            context.BumpMax = _bumpMax;
            context.CueCount = _cueCount;
            context.Copies = GluedCopies();
            _stateSent = 0;
            _bumpMax = _prevBump;
        }

        // S1 writes its mode to p[3] and its copies per side to p[4]: n * n cells for the torus and the Klein
        // bottle, 27 for the three-torus.
        ushort GluedCopies()
        {
            if (_prevSpace != (byte)SpaceId.Glued) return 0;
            int n = (int)_p4;
            if ((int)_p3 == 2) return 27;
            return n > 0 && n < 256 ? (ushort)(n * n) : (ushort)0;
        }

        static int Clamp(int v, int min, int max) { return v < min ? min : v > max ? max : v; }

        /// <summary>
        /// Takes the row of a completed second: one CSV line for the phone's file and, in live mode, one
        /// <c>Telemetry</c> message with the same <c>Seq</c> and time. A row that cannot be sent waits (up to 600) and goes out after the next
        /// handshake, so the Station's CSV has no hole. Allocates nothing.
        /// </summary>
        public void SubmitRow(in TelemetryMessage row)
        {
            if (Mode == TelemetryMode.Hud) return;
            if (_files != null)
            {
                _csv.Length = 0;
                TelemetryCsv.AppendRow(_csv, in row);
                _files.Append(LogFileWriter.Perf, _csv);
            }
            if (Mode != TelemetryMode.Live || !_started || _client.Role != LinkRole.Phone) return;
            // The row goes out as the probe numbered it, so the Station's CSV is the phone's, byte for byte.
            if (_rowCount == 0 && _client.IsReady && _client.SendTelemetryRow(in row)) return;
            if (_rowCount == RowBacklog)
            {
                _rowHead = (_rowHead + 1) % RowBacklog;
                _rowCount--;
            }
            _rows[(_rowHead + _rowCount) % RowBacklog] = row;
            _rowCount++;
        }

        void SendWaitingRows(int max)
        {
            for (int i = 0; i < max && _rowCount > 0; i++)
            {
                if (!_client.SendTelemetryRow(in _rows[_rowHead])) return;
                _rowHead = (_rowHead + 1) % RowBacklog;
                _rowCount--;
            }
        }

        /// <summary>A new session: forgets the rows and events of the old one's second and restarts the cue count.</summary>
        public void ResetSession()
        {
            _rowHead = 0;
            _rowCount = 0;
            _cueCount = 0;
            _stateSent = 0;
            _bumpMax = _prevBump;
        }

        // A Hub that is connected hears of the new room at once (another Hello); the world event follows at
        // step 8a when the host has raised the world version.
        void TellWorld()
        {
            int q, lMm, genVer;
            long seed;
            string genHash;
            _client.SetWorld(ComputeWorldHash(_prevSpace, out q, out lMm, out seed, out genVer, out genHash), _roomId);
        }

        // A room that is not complete (no distance field yet) has no geometry hash; it counts as no geometry
        // rather than stopping whoever set the room.
        static string GeometryHashOf(RoomModel room)
        {
            if (room == null) return null;
            try { return RoomModelJson.GeometryHash(room); }
            catch (ArgumentException) { return null; }
        }

        // ---- room outline ------------------------------------------------------------------------------

        /// <summary>
        /// Gives the Station the scanned room (floor polygon, wall height, free rectangle, object boxes) so the
        /// Spectator can draw it. Sent now when the link is up, and again after every handshake. Null clears it.
        /// </summary>
        public void SetRoom(RoomModel room)
        {
            _outline.Clear();
            _hasOutline = room != null;
            _outlineSent = false;
            _roomId = room != null && room.Id != null ? room.Id : string.Empty;
            _geometryHash = GeometryHashOf(room);
            if (room == null)
            {
                TellWorld();
                return;
            }

            for (int i = 0; i < room.Outline.Length; i++)
                if (!_outline.AddPoint(room.Outline[i].x, room.Outline[i].y)) break;
            float height = 0f;
            for (int i = 0; i < room.Walls.Length; i++)
                if (room.Walls[i] != null && room.Walls[i].Height > height) height = room.Walls[i].Height;
            _outline.Height = height > 0f ? height : RoomModel.DefaultWallHeight;
            // The room frame has the free rectangle at its origin, unrotated (contracts F4).
            _outline.RectCx = 0f;
            _outline.RectCz = 0f;
            _outline.RectW = room.FreeRect.Wf;
            _outline.RectD = room.FreeRect.Df;
            _outline.RectYaw = 0f;
            for (int i = 0; i < room.Objects.Length; i++)
            {
                RoomObject o = room.Objects[i];
                if (o == null) continue;
                var box = new RoomOutlineBox
                {
                    Id = (byte)o.Id, Category = (byte)o.Category,
                    Cx = o.Base.x, Cz = o.Base.z, W = o.Size.x, D = o.Size.z, H = o.Size.y, Yaw = o.Yaw
                };
                if (!_outline.AddBox(in box)) break;
            }
            TellWorld();
            if (_started && _client.IsReady) _outlineSent = _client.SendRoomOutline(_outline);
        }
    }
}
