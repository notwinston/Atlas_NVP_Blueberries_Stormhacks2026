using System;

namespace Atlas.Link
{
    /// <summary>Receives a decoded <c>State</c>. The struct is passed by reference and is valid only during the call.</summary>
    public delegate void LinkStateHandler(in StateMessage state);

    /// <summary>
    /// The client end of the phone-to-Station link (Master Plan section 7). The ATLAS app runs it with role
    /// phone; the Spectator and the Editors run the same class with their own role. It owns the handshake
    /// (Hello, Welcome, eight pings, ParamManifest), the clock estimate, the watchdog and the reconnect back-off.
    /// <para>
    /// Call <see cref="Poll"/> once per frame from the main thread; it never blocks, and everything received is
    /// handled inside it on that thread. With the Hub unplugged every send returns false at once and costs next
    /// to nothing (section 7.5, rule 4). In steady state nothing here allocates.
    /// </para>
    /// </summary>
    public sealed class LinkClient : ILinkTransportListener
    {
        enum Stage : byte { Idle, AwaitWelcome, Burst, Ready }

        const int CommandQueueSize = 32;

        readonly ILinkTransport _transport;
        readonly ILinkClock _clock;
        readonly ClockSync _clockSync = new ClockSync();
        readonly LinkWatchdog _watchdog = new LinkWatchdog();
        readonly ParamRegistry _registry;

        // Preallocated so that the per-frame and per-second sends allocate nothing.
        readonly byte[] _stateBuffer = new byte[StateMessage.Size];
        readonly byte[] _smallBuffer = new byte[TelemetryMessage.Size];
        readonly byte[] _largeBuffer = new byte[RoomOutlineMessage.MaxSize];
        readonly ParamSetMessage _paramSetIn = new ParamSetMessage();
        readonly ParamSetMessage _paramSetOut = new ParamSetMessage();
        readonly RoomOutlineMessage _outlineIn = new RoomOutlineMessage();
        readonly OperatorCommand[] _commands = new OperatorCommand[CommandQueueSize];
        int _commandHead, _commandCount;
        StateMessage _stateIn;
        StateMessage _stateOut;

        string _address;
        int _port = LinkProtocol.DefaultPort;
        int _peer = -1;
        Stage _stage;
        uint _stateSeq;
        uint _telemetrySeq;
        long _lastStateUs = long.MinValue;
        int _stateHz = 60;
        bool _started;
        bool _applyingRemote;

        /// <param name="transport">A <see cref="LiteNetTransport"/> in the app, a <see cref="LoopbackTransport"/> in tests.</param>
        /// <param name="clock">The monotonic clock that stamps <c>t_us</c>.</param>
        /// <param name="role">Who this client is.</param>
        /// <param name="registry">The phone's parameter registry; null for a client that holds no parameters.</param>
        public LinkClient(ILinkTransport transport, ILinkClock clock, LinkRole role, ParamRegistry registry = null)
        {
            if (transport == null) throw new ArgumentNullException("transport");
            if (clock == null) throw new ArgumentNullException("clock");
            _transport = transport;
            _clock = clock;
            _registry = registry;
            Role = role;
            _transport.Listener = this;
            if (_registry != null) _registry.Changed += OnParamChanged;
        }

        // ---- identity sent in Hello -------------------------------------------------------------------

        public LinkRole Role { get; private set; }
        public string Device = string.Empty;
        public string App = "atlas";
        public string Build = string.Empty;
        /// <summary>16 hex characters identifying the world this client holds (Master Plan section 7.3.1).</summary>
        public string WorldHash = string.Empty;
        public string RoomId = string.Empty;
        /// <summary>
        /// The session this client announces in <c>Hello</c>. The phone makes it at start; the Hub adopts it for
        /// its log folder and echoes it in <c>Welcome</c>. Empty on a Spectator. A value written here goes out
        /// with the next handshake; <see cref="SetSession"/> also tells a Hub that is already connected.
        /// </summary>
        public string SessionId = string.Empty;
        /// <summary>The scene name written into the events this client emits itself.</summary>
        public string Scene = string.Empty;

        // ---- state ------------------------------------------------------------------------------------

        public ParamRegistry Registry { get { return _registry; } }
        public ClockSync ClockSync { get { return _clockSync; } }
        public LinkWatchdog Watchdog { get { return _watchdog; } }
        public ILinkTransport Transport { get { return _transport; } }

        /// <summary>The watchdog state: OFFLINE, RESYNC, CONNECTED or DEGRADED.</summary>
        public LinkState State { get { return _watchdog.State; } }

        /// <summary>True once the handshake is done and messages flow.</summary>
        public bool IsReady { get { return _stage == Stage.Ready; } }

        /// <summary>
        /// Demo lock: while true a received <c>ParamSet</c> is ignored; operator commands still work (section
        /// 7.3.2). Set by a build flag, by <c>Welcome.demoLock</c> or by <c>OperatorCmd lock</c>.
        /// </summary>
        public bool DemoLock { get; set; }

        /// <summary>
        /// <c>State</c> sends per second: 60 sends every call of <see cref="SendState"/>, 30 or 10 thin them
        /// (<c>link.stateHz</c>, Master Plan section 12.4).
        /// </summary>
        public int StateHz
        {
            get { return _stateHz; }
            set { _stateHz = value < 1 ? 1 : value > 60 ? 60 : value; }
        }

        /// <summary>When true (the default on the phone) every parameter change is echoed to the Hub as an <c>Event param</c>.</summary>
        public bool EchoParamChanges = true;

        /// <summary>The last <c>Welcome</c> received; null before the first.</summary>
        public WelcomeMessage Welcome { get; private set; }

        /// <summary>False when the Hub's world hash differs from ours: the Spectator shows "world mismatch".</summary>
        public bool WorldMatches
        {
            get { return Welcome == null || string.IsNullOrEmpty(Welcome.WorldHash) || string.Equals(Welcome.WorldHash, WorldHash, StringComparison.Ordinal); }
        }

        /// <summary>The reason of the last <c>Bye</c> from the Hub (for example a protocol mismatch); null if none.</summary>
        public string LastByeReason { get; private set; }

        public string Address { get { return _address; } }
        public int Port { get { return _port; } }

        public int RttMs { get { return _peer >= 0 ? _transport.GetRttMs(_peer) : 0; } }
        public int LossPermille { get { return _peer >= 0 ? _transport.GetLossPermille(_peer) : 0; } }

        /// <summary>This client's clock, microseconds.</summary>
        public long NowUs { get { return _clock.NowUs; } }

        /// <summary>The Hub's clock now, microseconds, from the clock estimate.</summary>
        public long HubNowUs { get { return _clockSync.ToRemoteUs(_clock.NowUs); } }

        // ---- events (all raised inside Poll, on the caller's thread) -----------------------------------

        /// <summary>The watchdog changed state: (previous, current).</summary>
        public event Action<LinkState, LinkState> StateChanged
        {
            add { _watchdog.StateChanged += value; }
            remove { _watchdog.StateChanged -= value; }
        }

        public event Action<WelcomeMessage> Welcomed;
        /// <summary>A relayed <c>State</c> arrived (Spectator and Editor roles).</summary>
        public event LinkStateHandler StateReceived;
        /// <summary>The instance is reused: copy what must be kept.</summary>
        public event Action<RoomOutlineMessage> RoomOutlineReceived;
        public event Action<JobStatusMessage> JobStatusReceived;
        public event Action<AssetReadyMessage> AssetReadyReceived;
        /// <summary>
        /// The phone's manifest, forwarded by the Hub (Spectator, Editor and Console roles): when the phone sends
        /// it, and once after this client's own handshake, then with the values the phone holds at that moment.
        /// </summary>
        public event Action<ParamManifestMessage> ManifestReceived;
        /// <summary>
        /// An <c>Event</c> of the phone, relayed by the Hub (Spectator and Editor roles): every parameter change
        /// (<see cref="LinkEvents.Param"/>, read it with <see cref="LinkEvents.TryReadParam"/>), every
        /// <see cref="LinkEvents.World"/> event and whatever else the phone logs. A client that joins late is
        /// given the newest <c>world</c> event after its handshake.
        /// </summary>
        public event Action<EventMessage> EventReceived;
        /// <summary>A <c>ParamSet</c> was applied to the registry; the argument is how many values changed.</summary>
        public event Action<int> ParamsApplied;
        /// <summary>The Hub said <c>Bye</c>; the argument is its reason.</summary>
        public event Action<string> ByeReceived;

        // ---- lifecycle --------------------------------------------------------------------------------

        /// <summary>
        /// Starts the client and begins connecting to the Hub at an IP literal. Returns false when the socket
        /// cannot be opened; the app then carries on without the link.
        /// </summary>
        public bool Start(string address, int port = LinkProtocol.DefaultPort)
        {
            _address = address;
            _port = port;
            if (!_transport.StartClient()) return false;
            _started = true;
            _watchdog.Start(_clock.NowUs);
            return true;
        }

        /// <summary>Points the client at another Hub ("Change IP") and reconnects.</summary>
        public void SetAddress(string address, int port = LinkProtocol.DefaultPort)
        {
            _address = address;
            _port = port;
            if (_started) ReconnectNow();
        }

        /// <summary>
        /// Names the session (see <see cref="SessionId"/>). When the id is new, the <c>Seq</c> that
        /// <see cref="SendTelemetry"/> stamps starts again at 0, and a Hub that is connected is told at once with
        /// another <c>Hello</c>, which it answers with a <c>Welcome</c> carrying the id back.
        /// </summary>
        public void SetSession(string sessionId)
        {
            string id = sessionId ?? string.Empty;
            if (string.Equals(id, SessionId, StringComparison.Ordinal)) return;
            SessionId = id;
            _telemetrySeq = 0;
            if (_peer >= 0) SendHello();
        }

        /// <summary>
        /// Sets the world hash and room this client holds (see <see cref="WorldHash"/> and <see cref="RoomId"/>).
        /// When either changed and a Hub is connected, it is told at once with another <c>Hello</c>, so the
        /// <c>Welcome</c> a Spectator gets names the phone's current room and world.
        /// </summary>
        public void SetWorld(string worldHash, string roomId)
        {
            string hash = worldHash ?? string.Empty, room = roomId ?? string.Empty;
            if (string.Equals(hash, WorldHash, StringComparison.Ordinal) && string.Equals(room, RoomId, StringComparison.Ordinal)) return;
            WorldHash = hash;
            RoomId = room;
            if (_peer >= 0) SendHello();
        }

        void SendHello()
        {
            Send(new HelloMessage
            {
                Role = Role, Device = Device, App = App, Build = Build, WorldHash = WorldHash, RoomId = RoomId, SessionId = SessionId
            });
            _transport.Flush();
        }

        /// <summary>Drops the current peer, if any, and tries to connect at once ("Reconnect", or after a resume).</summary>
        public void ReconnectNow()
        {
            if (!_started) return;
            DropPeer();
            _watchdog.Start(_clock.NowUs);
        }

        /// <summary>Says <c>Bye</c>, closes the socket and goes offline (<c>link.enabled</c> off).</summary>
        public void Stop(string reason = "stop")
        {
            if (!_started) return;
            if (_peer >= 0)
            {
                Send(new ByeMessage(reason));
                _transport.Flush();
            }
            _started = false;
            _peer = -1;
            _stage = Stage.Idle;
            _clockSync.Stop();
            _watchdog.Stop();
            _transport.Stop();
        }

        /// <summary>
        /// Drives the link: delivers what arrived, runs the handshake, the clock pings, the watchdog and the
        /// reconnect back-off. Once per frame, main thread, never blocks.
        /// </summary>
        public void Poll()
        {
            if (!_started) return;
            _transport.Poll();
            long now = _clock.NowUs;

            if (_peer >= 0)
            {
                // Keep-alives count as packets: the transport knows about them, the listener does not.
                float silenceMs = _transport.GetSilenceMs(_peer);
                if (silenceMs >= 0f) _watchdog.OnSilence(now, (long)(silenceMs * 1000f));

                if (_stage == Stage.Burst && _clockSync.BurstComplete(now)) FinishResync(now);

                TPingMessage ping;
                if (_stage >= Stage.Burst && _clockSync.TryNextPing(now, out ping))
                {
                    int n = ping.WriteTo(_smallBuffer, 0);
                    _transport.Send(_peer, _smallBuffer, 0, n, LinkDelivery.Unreliable);
                    // t1 is now: the ping must not wait for the transport's next tick.
                    _transport.Flush();
                }
            }

            LinkState state = _watchdog.Tick(now);
            if (state == LinkState.Offline)
            {
                // The watchdog gave up on a peer the transport still holds: let go of it.
                if (_peer >= 0) DropPeer();
                if (_watchdog.ShouldConnect(now) && !string.IsNullOrEmpty(_address)) _transport.Connect(_address, _port);
            }
        }

        void DropPeer()
        {
            if (_peer < 0) return;
            int peer = _peer;
            _peer = -1;
            _stage = Stage.Idle;
            _clockSync.Stop();
            _transport.Disconnect(peer);
        }

        void FinishResync(long now)
        {
            _stage = Stage.Ready;
            // The manifest goes first; the Hub answers it by replaying its parameter snapshot (section 7.2).
            if (Role == LinkRole.Phone && _registry != null) SendManifest();
            _watchdog.OnResyncComplete(now);
        }

        // ---- sending ----------------------------------------------------------------------------------

        /// <summary>
        /// Sends this frame's <c>State</c> on the sequenced channel and flushes. Stamps <c>Seq</c> and
        /// <c>TimeUs</c> into the caller's struct. Returns false, having done nothing, when the link is not ready
        /// or <see cref="StateHz"/> says this frame is skipped. Allocates nothing.
        /// </summary>
        public bool SendState(ref StateMessage state)
        {
            if (_stage != Stage.Ready) return false;
            long now = _clock.NowUs;
            if (_stateHz < 60 && _lastStateUs != long.MinValue)
            {
                // Half a 60 Hz frame of slack, so that 30 Hz on a 60 fps loop means every second frame.
                long interval = 1000000L / _stateHz - 8000L;
                if (now - _lastStateUs < interval) return false;
            }
            _lastStateUs = now;
            state.Seq = _stateSeq++;
            state.TimeUs = now;
            state.DemoLock = DemoLock;
            state.WriteTo(_stateBuffer, 0);
            bool sent = _transport.Send(_peer, _stateBuffer, 0, StateMessage.Size, LinkDelivery.Sequenced);
            _transport.Flush();
            return sent;
        }

        /// <summary>
        /// As <see cref="SendState"/>, for a caller that holds the state read-only (a frame-loop state sink). The
        /// state is copied into the link's own slot and stamped there. Allocates nothing.
        /// </summary>
        public bool SendStateCopy(in StateMessage state)
        {
            if (_stage != Stage.Ready) return false;
            _stateOut = state;
            return SendState(ref _stateOut);
        }

        /// <summary>Sends one <c>Telemetry</c> row, reliably. Stamps <c>Seq</c> and, when zero, <c>TimeUs</c>. Allocates nothing.</summary>
        public bool SendTelemetry(ref TelemetryMessage telemetry)
        {
            if (_stage != Stage.Ready) return false;
            telemetry.Seq = _telemetrySeq++;
            if (telemetry.TimeUs == 0) telemetry.TimeUs = _clock.NowUs;
            int n = telemetry.WriteTo(_smallBuffer, 0);
            return _transport.Send(_peer, _smallBuffer, 0, n, LinkDelivery.Reliable);
        }

        /// <summary>
        /// Sends one <c>Telemetry</c> row exactly as given: <c>Seq</c> and <c>TimeUs</c> are the caller's. The
        /// perf probe numbers its rows and writes the same row to the phone's CSV, so the Hub's file comes out
        /// the same bytes, and a row sent twice is recognised by its <c>Seq</c> and written once. Allocates nothing.
        /// </summary>
        public bool SendTelemetryRow(in TelemetryMessage row)
        {
            if (_stage != Stage.Ready) return false;
            int n = row.WriteTo(_smallBuffer, 0);
            return _transport.Send(_peer, _smallBuffer, 0, n, LinkDelivery.Reliable);
        }

        public bool SendRoomOutline(RoomOutlineMessage outline)
        {
            if (_stage != Stage.Ready || outline == null) return false;
            int n = outline.WriteTo(_largeBuffer, 0);
            return _transport.Send(_peer, _largeBuffer, 0, n, LinkDelivery.Reliable);
        }

        /// <summary>Sends any JSON message reliably. Available from the moment a peer exists.</summary>
        public bool Send(JsonMessage message)
        {
            if (_peer < 0 || message == null) return false;
            byte[] bytes = message.ToBytes();
            return _transport.Send(_peer, bytes, 0, bytes.Length, LinkDelivery.Reliable);
        }

        /// <summary>Sends an <c>Event</c> stamped with this client's clock.</summary>
        public bool SendEvent(string scene, string ev, JsonValue data)
        {
            return _stage == Stage.Ready && Send(new EventMessage(_clock.NowUs, scene, ev, data));
        }

        /// <summary>Sends a <c>Log</c> line (warnings and errors).</summary>
        public bool SendLog(string level, string msg)
        {
            return _stage == Stage.Ready && Send(new LogMessage { TimeUs = _clock.NowUs, Level = level, Msg = msg });
        }

        /// <summary>Sends the registry's <c>ParamManifest</c>. The link sends it by itself after every handshake.</summary>
        public bool SendManifest()
        {
            return _registry != null && Send(ParamManifestMessage.FromRegistry(_registry));
        }

        /// <summary>Console and Editor roles: asks the Hub to set parameters on the phone.</summary>
        public bool SendParamSet(ParamSetMessage set)
        {
            if (_stage != Stage.Ready || set == null) return false;
            int n = set.WriteTo(_largeBuffer, 0);
            return _transport.Send(_peer, _largeBuffer, 0, n, LinkDelivery.Reliable);
        }

        /// <summary>Console and Editor roles: sets one parameter on the phone. Allocates nothing.</summary>
        public bool SendParam(ushort id, float value)
        {
            _paramSetOut.Clear();
            _paramSetOut.Add(id, value);
            return SendParamSet(_paramSetOut);
        }

        /// <summary>Console and Editor roles: sends one of the six operator commands to the phone.</summary>
        public bool SendCommand(in OperatorCommand command)
        {
            return _stage == Stage.Ready && Send(command.ToMessage());
        }

        // ---- received commands ------------------------------------------------------------------------

        /// <summary>Operator commands waiting to be applied.</summary>
        public int PendingCommands { get { return _commandCount; } }

        /// <summary>
        /// Takes the oldest operator command received. The app drains this once per frame and applies each
        /// command locally, so nothing on the frame path waits for the network (section 7.5, rule 2).
        /// </summary>
        public bool TryDequeueCommand(out OperatorCommand command)
        {
            if (_commandCount == 0)
            {
                command = default(OperatorCommand);
                return false;
            }
            command = _commands[_commandHead];
            _commands[_commandHead] = default(OperatorCommand);
            _commandHead = (_commandHead + 1) % CommandQueueSize;
            _commandCount--;
            return true;
        }

        void EnqueueCommand(in OperatorCommand command)
        {
            if (_commandCount == CommandQueueSize)
            {
                // Nobody is draining. Keep the newest: an operator's latest intent matters more than their oldest.
                _commandHead = (_commandHead + 1) % CommandQueueSize;
                _commandCount--;
            }
            _commands[(_commandHead + _commandCount) % CommandQueueSize] = command;
            _commandCount++;
        }

        // ---- ILinkTransportListener -------------------------------------------------------------------

        void ILinkTransportListener.OnPeerConnected(int peerId)
        {
            if (!_started) return;
            long now = _clock.NowUs;
            _peer = peerId;
            _stage = Stage.AwaitWelcome;
            LastByeReason = null;
            _watchdog.OnTransportConnected(now);
            SendHello();
        }

        void ILinkTransportListener.OnPeerDisconnected(int peerId, string reason)
        {
            // A failed connect attempt reports an id we never adopted; the running back-off already covers it.
            if (peerId != _peer || _peer < 0) return;
            _peer = -1;
            _stage = Stage.Idle;
            _clockSync.Stop();
            _watchdog.OnTransportDisconnected(_clock.NowUs);
        }

        void ILinkTransportListener.OnReceive(int peerId, byte[] data, int offset, int length, LinkDelivery delivery)
        {
            if (peerId != _peer || length < 1) return;
            long now = _clock.NowUs;
            _watchdog.OnPacket(now);

            switch (data[offset])
            {
                case MessageId.TPong:
                {
                    var pong = new TPongMessage();
                    if (pong.ReadFrom(data, offset, length)) _clockSync.OnPong(pong, now);
                    break;
                }
                case MessageId.TPing:
                {
                    var ping = new TPingMessage();
                    if (!ping.ReadFrom(data, offset, length)) break;
                    TPongMessage pong = ClockSync.Answer(ping, now, _clock.NowUs);
                    int n = pong.WriteTo(_smallBuffer, 0);
                    _transport.Send(_peer, _smallBuffer, 0, n, LinkDelivery.Unreliable);
                    _transport.Flush();
                    break;
                }
                case MessageId.State:
                {
                    LinkStateHandler handler = StateReceived;
                    if (handler != null && _stateIn.ReadFrom(data, offset, length)) handler(in _stateIn);
                    break;
                }
                case MessageId.ParamSet:
                    // Clamped by the registry; ignored under demo lock and while the link is not CONNECTED.
                    if (_registry == null || DemoLock || !_watchdog.AcceptsRemoteParams) break;
                    if (_paramSetIn.ReadFrom(data, offset, length))
                    {
                        _applyingRemote = true;
                        int changed;
                        try { changed = _registry.Apply(_paramSetIn, ParamSource.Remote); }
                        finally { _applyingRemote = false; }
                        Action<int> applied = ParamsApplied;
                        if (applied != null) applied(changed);
                    }
                    break;
                case MessageId.RoomOutline:
                {
                    Action<RoomOutlineMessage> handler = RoomOutlineReceived;
                    if (handler != null && _outlineIn.ReadFrom(data, offset, length)) handler(_outlineIn);
                    break;
                }
                case MessageId.Welcome: OnWelcome(data, offset, length, now); break;
                case MessageId.SceneCmd:
                {
                    var message = new SceneCmdMessage();
                    OperatorCommand command;
                    if (message.ReadFrom(data, offset, length) && OperatorCommand.TryFrom(message, out command)) EnqueueCommand(command);
                    break;
                }
                case MessageId.OperatorCmd:
                {
                    var message = new OperatorCmdMessage();
                    OperatorCommand command;
                    if (!message.ReadFrom(data, offset, length) || !OperatorCommand.TryFrom(message, out command)) break;
                    // The lock takes effect here and now, so a ParamSet queued behind it is already ignored.
                    if (command.Kind == OperatorCommandKind.Lock) DemoLock = command.On;
                    EnqueueCommand(command);
                    break;
                }
                case MessageId.JobStatus:
                {
                    Action<JobStatusMessage> handler = JobStatusReceived;
                    var message = new JobStatusMessage();
                    if (handler != null && message.ReadFrom(data, offset, length)) handler(message);
                    break;
                }
                case MessageId.AssetReady:
                {
                    Action<AssetReadyMessage> handler = AssetReadyReceived;
                    var message = new AssetReadyMessage();
                    if (handler != null && message.ReadFrom(data, offset, length)) handler(message);
                    break;
                }
                case MessageId.ParamManifest:
                {
                    Action<ParamManifestMessage> handler = ManifestReceived;
                    var message = new ParamManifestMessage();
                    if (handler != null && message.ReadFrom(data, offset, length)) handler(message);
                    break;
                }
                case MessageId.Event:
                {
                    Action<EventMessage> handler = EventReceived;
                    var message = new EventMessage();
                    if (handler != null && message.ReadFrom(data, offset, length)) handler(message);
                    break;
                }
                case MessageId.Bye:
                {
                    var bye = new ByeMessage();
                    if (!bye.ReadFrom(data, offset, length)) break;
                    LastByeReason = bye.Reason;
                    Action<string> handler = ByeReceived;
                    if (handler != null) handler(bye.Reason);
                    break;
                }
                // Every other id, the reserved PeerState among them, is ignored (section 7.5, rule 5).
            }
        }

        void OnWelcome(byte[] data, int offset, int length, long now)
        {
            var welcome = new WelcomeMessage();
            if (!welcome.ReadFrom(data, offset, length)) return;
            Welcome = welcome;
            if (welcome.ProtoMajor != LinkProtocol.ProtoMajor)
            {
                // The Hub refuses a mismatch with a Bye; a Hub that welcomes one anyway is not one we can talk to.
                LastByeReason = "protocol mismatch: hub " + welcome.ProtoMajor + ", client " + LinkProtocol.ProtoMajor;
                return;
            }
            if (welcome.DemoLock) DemoLock = true;
            if (_stage == Stage.AwaitWelcome)
            {
                _stage = Stage.Burst;
                _clockSync.Restart(now);
            }
            Action<WelcomeMessage> handler = Welcomed;
            if (handler != null) handler(welcome);
        }

        void OnParamChanged(ParamEntry entry, ParamSource source)
        {
            if (!EchoParamChanges || _stage != Stage.Ready || Role != LinkRole.Phone) return;
            // The applied (clamped) value goes back to the Console, whoever changed it (section 7.3.2).
            Send(LinkEvents.ParamChanged(_clock.NowUs, Scene, entry, _applyingRemote ? ParamSource.Remote : source));
        }
    }
}
