using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Link;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace Atlas.Station
{
    /// <summary>What became of a request to the phone.</summary>
    public enum HubResult
    {
        Ok,
        /// <summary>The parameter, command or space is not known.</summary>
        Unknown,
        /// <summary>The request body is not what the route expects.</summary>
        Invalid,
        /// <summary>The demo lock is on: the phone would ignore the value (Master Plan section 7.3.2).</summary>
        Locked,
        /// <summary>No phone is connected.</summary>
        NoPhone
    }

    /// <summary>
    /// The Hub (Master Plan section 7.1, task P1-D02): the Station's only LiteNetLib listener and its web server.
    /// It relays the phone's <c>State</c> to Spectators and Editors, parameters and the six operator commands to
    /// the phone, keeps a watchdog per client, writes the session logs and feeds the Console over HTTP and a
    /// WebSocket. The link runs on one thread; HTTP handlers reach it through one lock, held for microseconds.
    /// </summary>
    public sealed class HubServer : IAsyncDisposable
    {
        /// <summary>A Spectator, Editor or Console connected over the link, with its own watchdog.</summary>
        sealed class Watcher
        {
            public int PeerId;
            public LinkRole Role;
            public string Device;
            public readonly LinkWatchdog Watch = new LinkWatchdog();
            public bool Dropped;
        }

        const long DigestIntervalUs = 100000;   // the Console's State digest, 10 Hz (Appendix B.3)
        const long SecondUs = 1000000;

        readonly HubOptions _options;
        readonly object _gate = new object();
        readonly ILinkClock _clock;
        readonly ILinkTransport _transport;
        readonly LinkHost _host;
        readonly LinkWatchdog _phoneWatch = new LinkWatchdog();
        readonly List<Watcher> _watchers = new List<Watcher>();
        readonly ConsoleFeed _feed = new ConsoleFeed();
        readonly CancellationTokenSource _stopping = new CancellationTokenSource();
        readonly DateTime _startedUtc = DateTime.UtcNow;

        volatile SessionLog _log;
        PresetStore _presets;
        ForgeProbe _forge;
        WebApplication _web;
        Thread _linkThread;
        IDisposable _timerResolution;
        volatile bool _running;
        bool _started, _stopped;

        // Phone seat. Touched only under _gate.
        int _phonePeer = -1;
        bool _phoneDropped;
        HelloMessage _phoneHello;
        StateMessage _lastState;
        bool _haveState, _stateFresh;
        uint _lastSeq;
        bool _haveSeq;
        TelemetryMessage _lastTelemetry;
        bool _haveTelemetry;
        long _statesReceived, _statesLost, _telemetryRows, _eventCount;
        int _rateCount, _lostCount;
        float _stateHz;
        long _lastDigestUs, _lastSecondUs;

        // State received and lost in each of the last five seconds: the "loss" of /api/session (contract 3.3).
        const int LossWindow = 5;
        readonly int[] _receivedPerSecond = new int[LossWindow];
        readonly int[] _lostPerSecond = new int[LossWindow];
        int _lossSlot;

        // Phone-to-Hub latency of the newest State packets, microseconds: Hub receive time minus the phone's send
        // time brought onto the Hub's clock (contract 3.2). About eight seconds at 60 Hz.
        const int LatencyWindow = 512;
        readonly int[] _latencyUs = new int[LatencyWindow];
        readonly int[] _latencySorted = new int[LatencyWindow];
        int _latencyCount, _latencyNext;

        // True when the operator named the session on the command line: the folder then keeps that name.
        bool _sessionPinned;
        // True while the session in force is one a phone named in its Hello.
        bool _sessionFromPhone;

        readonly RoomOutlineMessage _outlineScratch = new RoomOutlineMessage();
        readonly byte[] _outlineBytes = new byte[RoomOutlineMessage.MaxSize];
        int _outlineLength;
        string _outlineJson;
        int _manifestVer;
        bool _everHeard, _hintPrinted;
        long _startedUs;

        public HubServer(HubOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _clock = options.Clock ?? new StopwatchClock();
            _transport = options.Transport ?? new LiteNetTransport();
            _host = new LinkHost(_transport, _clock);

            _host.HelloReceived += OnHello;
            _host.PeerJoined += OnPeerJoined;
            _host.PeerLeft += OnPeerLeft;
            _host.StateReceived += OnState;
            _host.TelemetryReceived += OnTelemetry;
            _host.ManifestReceived += OnManifest;
            _host.JsonReceived += OnJson;
            _host.ParamSetReceived += OnParamSetFromLink;
            _host.RoomOutlineReceived += OnRoomOutline;
            _phoneWatch.StateChanged += OnPhoneLinkChanged;
        }

        // ---- what the outside may read ---------------------------------------------------------------

        public HubOptions Options { get { return _options; } }

        /// <summary>
        /// The session in force and the name of its log folder: the id the phone sent in its <c>Hello</c>, or the
        /// Hub's own until a phone names one (lane D contract 5.5).
        /// </summary>
        public string SessionId { get; private set; }

        /// <summary>The bound UDP port (the real one when the options asked for 0).</summary>
        public int UdpPort { get; private set; }

        /// <summary>The bound HTTP port (the real one when the options asked for 0).</summary>
        public int HttpPort { get; private set; }

        /// <summary>The Station's address as clients should dial it.</summary>
        public string StationAddress { get; private set; }

        public SessionLog Log { get { return _log; } }
        public PresetStore Presets { get { return _presets; } }
        public ConsoleFeed Feed { get { return _feed; } }
        public CancellationToken Stopping { get { return _stopping.Token; } }

        /// <summary><c>State</c> packets received from the phone this session.</summary>
        public long StatesReceived { get { return Interlocked.Read(ref _statesReceived); } }

        /// <summary><c>State</c> packets that never arrived, counted from gaps in the sequence numbers.</summary>
        public long StatesLost { get { return Interlocked.Read(ref _statesLost); } }

        public long TelemetryRows { get { return Interlocked.Read(ref _telemetryRows); } }
        public long EventCount { get { return Interlocked.Read(ref _eventCount); } }

        /// <summary>The phone's link as the Hub sees it: OFFLINE, RESYNC, CONNECTED or DEGRADED (section 7.2).</summary>
        public LinkState PhoneState { get { lock (_gate) return _phoneWatch.State; } }

        public bool HasPhone { get { lock (_gate) return _host.HasPhone; } }

        public int Spectators { get { lock (_gate) return _host.CountRole(LinkRole.Spectator); } }

        public bool DemoLock { get { lock (_gate) return _host.DemoLock; } }

        // ---- lifecycle --------------------------------------------------------------------------------

        /// <summary>Opens the logs, the UDP listener and the web server. Throws when a port cannot be bound.</summary>
        public async Task StartAsync()
        {
            if (_started) throw new InvalidOperationException("the Hub is already started");
            _started = true;

            HubPaths.Resolve(_options);
            _sessionPinned = _options.SessionId != null;
            SessionId = MakeSessionId(_options.LogRoot, _options.SessionId);
            _log = new SessionLog(_options.LogRoot, SessionId);
            _presets = new PresetStore(_options.PresetFolder);
            StationAddress = LanAddress.Pick(_options.AdvertiseAddress);

            _host.SessionId = SessionId;
            _host.DemoLock = _options.DemoLock;
            _host.ForgeUrl = ForgeUrlForClients();

            if (!_host.Start(_options.UdpPort))
            {
                _log.Dispose();
                throw new IOException("cannot open UDP port " + _options.UdpPort + " (is another Hub running?)");
            }
            UdpPort = _host.Port;

            try
            {
                _web = HubWeb.Build(this, _options);
                await _web.StartAsync().ConfigureAwait(false);
                HttpPort = HubWeb.BoundPort(_web, _options.HttpPort);
            }
            catch
            {
                _host.Stop("hub failed to start");
                _log.Dispose();
                throw;
            }

            long now = _clock.NowUs;
            _lastDigestUs = now;
            _lastSecondUs = now;
            _startedUs = now;
            HubLog("start", JsonValue.Object()
                .Set("sessionId", SessionId).Set("udpPort", UdpPort).Set("httpPort", HttpPort).Set("station", StationAddress));

            if (_options.ProbeForge)
            {
                _forge = new ForgeProbe(_options.ForgeUrl);
                _forge.Changed += OnForgeChanged;
                _forge.Start();
            }

            _running = true;
            if (!_options.ManualPump)
            {
                // A 1 ms loop needs a 1 ms timer; Windows gives 15.6 ms unless asked (the relay must add < 2 ms).
                _timerResolution = TimerResolution.Raise();
                _linkThread = new Thread(LinkLoop) { IsBackground = true, Name = "atlas-hub link", Priority = ThreadPriority.AboveNormal };
                _linkThread.Start();
            }
        }

        /// <summary>Says <c>Bye</c> to every client, stops the web server and closes the logs.</summary>
        public async Task StopAsync()
        {
            if (!_started || _stopped) return;
            _stopped = true;
            _running = false;
            if (_linkThread != null) _linkThread.Join(2000);
            if (_timerResolution != null) _timerResolution.Dispose();

            if (_forge != null) _forge.Dispose();
            _stopping.Cancel();   // lets the WebSocket loops end, so the web server stops at once
            if (_web != null)
            {
                try { await _web.StopAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                await _web.DisposeAsync().ConfigureAwait(false);
            }

            lock (_gate)
            {
                HubLog("stop", JsonValue.Object().Set("states", _statesReceived).Set("lost", _statesLost));
                _host.Stop("hub stopping");
            }
            _log.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            _stopping.Dispose();
        }

        void LinkLoop()
        {
            while (_running)
            {
                try { Pump(); }
                catch (Exception e)
                {
                    // One bad packet or a full disk must not end the session.
                    Console.Error.WriteLine("hub: link loop: " + e);
                }
                Thread.Sleep(1);
            }
        }

        /// <summary>
        /// One turn of the link loop: delivers and relays what arrived, runs the watchdogs, and on their
        /// schedules sends the Console its 10 Hz digest and does the once-a-second work. Allocates nothing when
        /// no Console is connected. Called by the Hub's own thread, or by the owner under
        /// <see cref="HubOptions.ManualPump"/>.
        /// </summary>
        public void Pump()
        {
            lock (_gate)
            {
                if (!_host.IsRunning) return;
                _host.Poll();
                long now = _clock.NowUs;
                TickWatchdogs(now);

                if (now - _lastDigestUs >= DigestIntervalUs)
                {
                    _lastDigestUs = now;
                    if (_stateFresh && _feed.Count > 0) _feed.Broadcast(StateDigest().ToJson());
                    _stateFresh = false;
                }
                if (now - _lastSecondUs >= SecondUs) EverySecond(now);
            }
        }

        void EverySecond(long now)
        {
            _stateHz = _rateCount * (float)SecondUs / (now - _lastSecondUs);
            _receivedPerSecond[_lossSlot] = _rateCount;
            _lostPerSecond[_lossSlot] = _lostCount;
            _lossSlot = (_lossSlot + 1) % LossWindow;
            _rateCount = 0;
            _lostCount = 0;
            _lastSecondUs = now;
            if (!_everHeard && !_hintPrinted && now - _startedUs >= 10 * SecondUs)
            {
                _hintPrinted = true;
                if (!_options.Quiet)
                    Console.WriteLine("no packets received yet: check the firewall rule for UDP " + UdpPort + " (station/setup_firewall.ps1)");
            }
            _log.Flush();
            if (_feed.Count > 0) _feed.Broadcast(LinkJson().ToJson());
            if (!_options.Quiet && _host.HasPhone)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "phone {0,-9} state {1,5:0.0} Hz  rtt {2,3} ms  loss {3,4:0.0} %  fps {4,5:0.0}  thermal {5}  spectators {6}  consoles {7}",
                    StateName(_phoneWatch.State), _stateHz, _host.GetRttMs(_phonePeer), LossPercent(),
                    _haveTelemetry ? _lastTelemetry.Fps : 0f, _haveTelemetry ? _lastTelemetry.Thermal : 0,
                    _host.CountRole(LinkRole.Spectator), _feed.Count));
            }
        }

        // ---- watchdogs (section 7.2) ------------------------------------------------------------------

        void TickWatchdogs(long now)
        {
            if (_phonePeer >= 0)
            {
                float silenceMs = _transport.GetSilenceMs(_phonePeer);
                if (silenceMs >= 0f) _phoneWatch.OnSilence(now, (long)(silenceMs * 1000f));
                if (_phoneWatch.Tick(now) == LinkState.Offline && !_phoneDropped)
                {
                    // Silent for 3 s: free the seat now, so that the phone's next Hello is not refused as a second phone.
                    _phoneDropped = true;
                    _transport.Disconnect(_phonePeer);
                }
            }

            for (int i = 0; i < _watchers.Count; i++)
            {
                Watcher w = _watchers[i];
                float silenceMs = _transport.GetSilenceMs(w.PeerId);
                if (silenceMs >= 0f) w.Watch.OnSilence(now, (long)(silenceMs * 1000f));
                if (w.Watch.Tick(now) == LinkState.Offline && !w.Dropped)
                {
                    w.Dropped = true;
                    _transport.Disconnect(w.PeerId);
                }
            }
        }

        void OnPhoneLinkChanged(LinkState previous, LinkState current)
        {
            HubLog("link", JsonValue.Object()
                .Set("role", "phone").Set("state", StateName(current)).Set("prev", StateName(previous)));
            if (_feed.Count > 0) _feed.Broadcast(LinkJson().ToJson());
            if (!_options.Quiet) Console.WriteLine("phone link: " + StateName(previous) + " -> " + StateName(current));
        }

        /// <summary>The watchdog states as <c>/api/session</c> and the <c>link</c> frame name them (contract 3.3).</summary>
        public static string StateName(LinkState state)
        {
            switch (state)
            {
                case LinkState.Connected: return "connected";
                case LinkState.Degraded: return "degraded";
                case LinkState.Resync: return "resync";
                default: return "offline";
            }
        }

        /// <summary>Share of <c>State</c> packets lost in the last five seconds, in percent, from the sequence numbers.</summary>
        float LossPercent()
        {
            int received = _rateCount, lost = _lostCount;
            for (int i = 0; i < LossWindow; i++)
            {
                received += _receivedPerSecond[i];
                lost += _lostPerSecond[i];
            }
            return received + lost == 0 ? 0f : 100f * lost / (received + lost);
        }

        // ---- link events (all inside Pump, under the gate) --------------------------------------------

        // Every Hello of the phone, before its Welcome is written: the first of a connection, and the ones it says
        // again when it starts a new session or enters another room.
        void OnHello(int peerId, HelloMessage hello)
        {
            if (hello.Role != LinkRole.Phone) return;
            bool again = peerId == _phonePeer;
            _phoneHello = hello;
            // Welcome carries the phone's world: back to the phone, and to a Spectator, which can then tell at once
            // whether it holds the same one.
            _host.WorldHash = hello.WorldHash ?? string.Empty;
            _host.RoomId = hello.RoomId ?? string.Empty;
            if (!_sessionPinned)
            {
                string named = hello.SessionId;
                if (!string.IsNullOrEmpty(named))
                {
                    if (!string.Equals(named, SessionId, StringComparison.Ordinal)) SwitchSession(named, true, again);
                }
                else if (_sessionFromPhone)
                {
                    // A phone that names no session does not continue another phone's: the Hub opens one of its own.
                    SwitchSession(MakeSessionId(_options.LogRoot, null), false, again);
                }
            }
            if (again && _feed.Count > 0) _feed.Broadcast(LinkJson().ToJson());
        }

        /// <summary>
        /// The phone makes the session id and the Hub adopts it (lane D contract 5.5), so the phone's files and
        /// the Hub's folder carry one name with no link needed at the start. A folder of that name that already
        /// exists is continued: that is a Hub restarted in mid-session. With no id in the <c>Hello</c>, or with a
        /// session named on the command line, the Hub keeps its own.
        /// </summary>
        void SwitchSession(string wanted, bool namedByPhone, bool inMidSession)
        {
            if (!Names.IsSafe(wanted))
            {
                HubLog("session", JsonValue.Object().Set("refused", wanted).Set("reason", "not a folder name"));
                return;
            }

            SessionLog previous = _log, next;
            try
            {
                // In a session the phone named, seq counts from 0 once, so a row seen twice is a duplicate.
                next = new SessionLog(_options.LogRoot, wanted) { SkipKnownRows = namedByPhone };
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // The disk says no: the session carries on in the folder it has.
                HubLog("session", JsonValue.Object().Set("refused", wanted).Set("reason", e.Message));
                return;
            }

            // What the Hub noted before the phone named the session (its start, the joins) is part of that
            // session's story. When that is all there was, the Hub's own folder, made a moment ago, holds nothing
            // else and is left out of the session list.
            bool pristine = previous.IsPristine;
            if (pristine) next.AppendHubLog(previous.ReadHubLog());
            string from = SessionId;
            previous.Dispose();
            if (pristine)
            {
                try { Directory.Delete(previous.Folder, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            _log = next;
            SessionId = wanted;
            _host.SessionId = wanted;
            _sessionFromPhone = namedByPhone;

            // The counters of /api/session are the session's.
            Interlocked.Exchange(ref _statesReceived, 0);
            Interlocked.Exchange(ref _statesLost, 0);
            Interlocked.Exchange(ref _telemetryRows, 0);
            Interlocked.Exchange(ref _eventCount, 0);
            _haveSeq = false;
            _haveTelemetry = false;
            _latencyCount = 0;
            _latencyNext = 0;

            // A session that starts in mid-connection keeps its phone's parameters and room: the new folder gets
            // them too, so that it replays on its own.
            if (inMidSession)
            {
                if (_host.Manifest != null) next.WriteManifest(_host.Manifest.ToJson().ToJson(true));
                if (_outlineLength > 0) next.WriteOutline(_outlineBytes, _outlineLength);
            }

            HubLog("session", JsonValue.Object()
                .Set("sessionId", wanted).Set("from", from).Set("namedBy", namedByPhone ? "phone" : "hub").Set("resumed", !next.CreatedFolder));
            if (!_options.Quiet)
                Console.WriteLine("session " + wanted + (namedByPhone ? " (named by the phone)" : string.Empty) + ", logs " + next.Folder);
        }

        void OnPeerJoined(int peerId, HelloMessage hello)
        {
            long now = _clock.NowUs;
            _everHeard = true;
            if (hello.Role == LinkRole.Phone)
            {
                _phonePeer = peerId;
                _phoneDropped = false;
                _phoneHello = hello;
                _haveSeq = false;
                _latencyCount = 0;
                _latencyNext = 0;
                _phoneWatch.OnTransportConnected(now);   // RESYNC until the manifest has been answered
            }
            else
            {
                var w = new Watcher { PeerId = peerId, Role = hello.Role, Device = hello.Device };
                _watchers.Add(w);
                w.Watch.OnTransportConnected(now);
                w.Watch.OnResyncComplete(now);
            }

            HubLog("join", JsonValue.Object()
                .Set("role", LinkRoles.Name(hello.Role)).Set("peer", peerId).Set("device", hello.Device ?? string.Empty)
                .Set("build", hello.Build ?? string.Empty).Set("worldHash", hello.WorldHash ?? string.Empty)
                .Set("address", _host.GetAddress(peerId)));
            if (_feed.Count > 0) _feed.Broadcast(LinkJson().ToJson());
            if (!_options.Quiet) Console.WriteLine(LinkRoles.Name(hello.Role) + " joined: " + hello.Device + " (peer " + peerId + ")");
        }

        void OnPeerLeft(int peerId, LinkRole role, string reason)
        {
            long now = _clock.NowUs;
            string roleName = LinkRoles.Name(role);
            if (peerId == _phonePeer)
            {
                roleName = LinkRoles.Name(LinkRole.Phone);
                _phonePeer = -1;
                _phoneWatch.OnTransportDisconnected(now);
            }
            else
            {
                for (int i = _watchers.Count - 1; i >= 0; i--)
                {
                    if (_watchers[i].PeerId != peerId) continue;
                    roleName = LinkRoles.Name(_watchers[i].Role);
                    _watchers.RemoveAt(i);
                }
            }

            HubLog("leave", JsonValue.Object()
                .Set("role", roleName).Set("peer", peerId).Set("reason", reason ?? string.Empty));
            if (_feed.Count > 0) _feed.Broadcast(LinkJson().ToJson());
            if (!_options.Quiet) Console.WriteLine(roleName + " left: " + reason + " (peer " + peerId + ")");
        }

        // The frame path of the relay: LinkHost has already sent the packet on to the watchers. Allocates nothing.
        void OnState(int peerId, in StateMessage state, byte[] raw, int offset, int length)
        {
            long now = _clock.NowUs;
            if (_haveSeq)
            {
                uint gap = state.Seq - _lastSeq;
                // A sequence that goes backwards is a phone that restarted, not a loss.
                if (gap > 1u && gap < 0x80000000u)
                {
                    Interlocked.Add(ref _statesLost, gap - 1u);
                    _lostCount += (int)System.Math.Min(gap - 1u, 100000u);
                }
            }
            _lastSeq = state.Seq;
            _haveSeq = true;
            _lastState = state;
            _haveState = true;
            _stateFresh = true;
            _rateCount++;
            Interlocked.Increment(ref _statesReceived);
            long offsetUs, rttUs;
            if (_host.TryGetClock(peerId, out offsetUs, out rttUs))
            {
                // offsetUs is the phone's clock minus the Hub's, from the Hub's own pings.
                long latency = now - (state.TimeUs - offsetUs);
                _latencyUs[_latencyNext] = latency < 0 ? 0 : latency > int.MaxValue ? int.MaxValue : (int)latency;
                _latencyNext = (_latencyNext + 1) % LatencyWindow;
                if (_latencyCount < LatencyWindow) _latencyCount++;
            }
            _log.WriteState(now, raw, offset);
            // A phone with no parameter registry sends no manifest; its first State ends RESYNC just as well.
            if (_phoneWatch.State == LinkState.Resync) _phoneWatch.OnResyncComplete(now);
        }

        void OnTelemetry(int peerId, in TelemetryMessage telemetry)
        {
            // A row already in perf.csv (the phone sends again what it could not see arrive) is written once.
            SessionLog log = _log;
            if (!log.WriteTelemetry(telemetry)) return;
            Interlocked.Increment(ref _telemetryRows);
            // "The newest row" is the one with the highest seq: a row from the phone's backlog does not replace it.
            if (!_haveTelemetry || !log.SkipKnownRows || telemetry.Seq >= _lastTelemetry.Seq)
            {
                _lastTelemetry = telemetry;
                _haveTelemetry = true;
            }
            if (_feed.Count > 0) _feed.Broadcast(TelemetryJson.ToJson(telemetry).Set("t", "telemetry").ToJson());
        }

        void OnManifest(int peerId, ParamManifestMessage manifest)
        {
            // LinkHost has replayed the parameter snapshot by now: the phone is back in step (RESYNC, section 7.2).
            _phoneWatch.OnResyncComplete(_clock.NowUs);
            _manifestVer++;
            _log.WriteManifest(manifest.ToJson().ToJson(true));
            if (_feed.Count > 0) _feed.Broadcast(ManifestJson().ToJson());
        }

        void OnParamSetFromLink(int peerId, ParamSetMessage set)
        {
            JsonValue values = JsonValue.Object();
            for (int i = 0; i < set.Count; i++)
                values.Set(ParamName(set.Ids[i]) ?? set.Ids[i].ToString(CultureInfo.InvariantCulture), set.Values[i]);
            HubLog("param", JsonValue.Object().Set("from", LinkRoles.Name(_host.RoleOf(peerId))).Set("values", values));
        }

        void OnRoomOutline(int peerId, RoomOutlineMessage outline)
        {
            // Kept as bytes for outline.bin (ReplayPhone sends it again) and as JSON for a Console that joins later.
            int n = outline.WriteTo(_outlineBytes, 0);
            _outlineLength = n;
            _log.WriteOutline(_outlineBytes, n);
            _outlineJson = OutlineJson(outline).ToJson();
            if (_feed.Count > 0) _feed.Broadcast(_outlineJson);
        }

        static JsonValue OutlineJson(RoomOutlineMessage o)
        {
            JsonValue points = JsonValue.Array();
            for (int i = 0; i < o.PointCount; i++) points.Add(JsonValue.Array(o.PointsXZ[i * 2], o.PointsXZ[i * 2 + 1]));
            JsonValue boxes = JsonValue.Array();
            for (int i = 0; i < o.BoxCount; i++)
            {
                RoomOutlineBox b = o.Boxes[i];
                boxes.Add(JsonValue.Object().Set("id", (int)b.Id).Set("cat", (int)b.Category).Set("cx", b.Cx).Set("cz", b.Cz)
                    .Set("w", b.W).Set("d", b.D).Set("h", b.H).Set("yaw", b.Yaw));
            }
            return JsonValue.Object().Set("t", "outline").Set("outline", points).Set("height", o.Height)
                .Set("rect", JsonValue.Object().Set("cx", o.RectCx).Set("cz", o.RectCz).Set("wf", o.RectW).Set("df", o.RectD).Set("yaw", o.RectYaw))
                .Set("boxes", boxes);
        }

        void OnJson(int peerId, byte messageId, JsonValue body)
        {
            long now = _clock.NowUs;
            switch (messageId)
            {
                case MessageId.Event:
                    if (body.GetString("ev") == LinkEvents.World) NoteWorld(body["data"]);
                    // The phone's event as it came, with the Hub's receive time added so the file sorts on one clock.
                    body.Set("hub_us", now);
                    _log.WriteEvent(body.ToJson());
                    Interlocked.Increment(ref _eventCount);
                    if (_feed.Count > 0) _feed.Broadcast(body.Set("t", "event").ToJson());
                    break;

                case MessageId.Log:
                    _log.WritePhoneLog(body.GetLong("t_us"), body.GetString("level", "info"), body.GetString("msg", string.Empty));
                    if (_feed.Count > 0) _feed.Broadcast(body.Set("t", "log").ToJson());
                    break;

                case MessageId.SceneCmd:
                case MessageId.OperatorCmd:
                    HubLog("cmd", JsonValue.Object()
                        .Set("from", LinkRoles.Name(_host.RoleOf(peerId)))
                        .Set("cmd", messageId == MessageId.SceneCmd ? "space" : body.GetString("cmd", string.Empty))
                        .Set("body", body));
                    break;

                case MessageId.AssetAck:
                    HubEvent("asset_ack", body.Clone());
                    if (_feed.Count > 0) _feed.Broadcast(body.Set("kind", "ack").Set("t", "job").ToJson());
                    break;

                case MessageId.Bye:
                    HubLog("bye", JsonValue.Object()
                        .Set("role", LinkRoles.Name(_host.RoleOf(peerId))).Set("peer", peerId)
                        .Set("reason", body.GetString("reason", string.Empty)));
                    break;

                // Hello and ParamManifest are handled through PeerJoined and ManifestReceived.
            }
        }

        // A world event that names the world hash and the room keeps Welcome true between two Hellos, so a
        // Spectator that joins next is told the world the phone is in now.
        void NoteWorld(JsonValue data)
        {
            string worldHash = data.GetString("worldHash"), roomId = data.GetString("roomId");
            bool changed = false;
            if (worldHash != null && worldHash != _host.WorldHash)
            {
                _host.WorldHash = worldHash;
                changed = true;
            }
            if (roomId != null && roomId != _host.RoomId)
            {
                _host.RoomId = roomId;
                changed = true;
            }
            if (changed) _host.SendWelcomeToWatchers();
        }

        void OnForgeChanged(ForgeStatus status)
        {
            lock (_gate)
            {
                if (_stopped) return;
                HubLog("forge", status.ToJson());
                if (_feed.Count > 0) _feed.Broadcast(LinkJson().ToJson());
            }
        }

        /// <summary>
        /// A Hub-made line in <c>events.jsonl</c> (<c>fms</c>, <c>note</c>, <c>asset_ack</c>): the phone's event
        /// shape, with scene <c>hub</c> and the Hub's clock (contract 3.4).
        /// </summary>
        void HubEvent(string ev, JsonValue data)
        {
            long now = _clock.NowUs;
            JsonValue line = new EventMessage(now, "hub", ev, data).ToJson().Set("hub_us", now);
            _log.WriteEvent(line.ToJson());
            Interlocked.Increment(ref _eventCount);
            if (_feed.Count > 0) _feed.Broadcast(line.Set("t", "event").ToJson());
        }

        /// <summary>A line in <c>hub.log</c>: connects and leaves, every command and parameter sent, preset actions.</summary>
        void HubLog(string kind, JsonValue data)
        {
            _log.WriteHubLog(_clock.NowUs, kind, data.ToJson());
        }

        /// <summary>Notes a preset action in <c>hub.log</c>.</summary>
        public void LogPreset(string action, string name)
        {
            lock (_gate) HubLog("preset", JsonValue.Object().Set("action", action).Set("name", name));
        }

        // ---- JSON views -------------------------------------------------------------------------------

        JsonValue PhoneJson()
        {
            JsonValue phone = JsonValue.Object()
                .Set("state", StateName(_phoneWatch.State))
                .Set("rttMs", _phonePeer >= 0 ? _host.GetRttMs(_phonePeer) : 0)
                .Set("loss", Math.Round(LossPercent(), 2))
                .Set("stateHz", Math.Round(_stateHz, 1))
                .Set("states", _statesReceived)
                .Set("lost", _statesLost);
            // The phone's clock minus the Hub's, from the Hub's own pings: the Console's connection strip shows it.
            long offsetUs, clockRttUs;
            if (_phonePeer >= 0 && _host.TryGetClock(_phonePeer, out offsetUs, out clockRttUs))
                phone.Set("clockOffsetMs", Math.Round(offsetUs / 1000.0, 2));
            if (_phonePeer >= 0 && _phoneHello != null)
            {
                phone.Set("device", _phoneHello.Device ?? string.Empty).Set("build", _phoneHello.Build ?? string.Empty)
                     .Set("address", _host.GetAddress(_phonePeer))
                     .Set("worldHash", _host.WorldHash ?? string.Empty).Set("roomId", _host.RoomId ?? string.Empty);
            }
            return phone;
        }

        JsonValue SessionJsonLocked()
        {
            ForgeStatus forge = _forge != null ? _forge.Status : ForgeStatus.Down;
            JsonValue sources = JsonValue.Array();
            if (_phonePeer >= 0 && _phoneHello != null)
                sources.Add(JsonValue.Object().Set("peer", _phonePeer).Set("role", "phone").Set("device", _phoneHello.Device ?? string.Empty).Set("active", true));
            return JsonValue.Object()
                .Set("sessionId", SessionId)
                .Set("phone", PhoneJson())
                .Set("sources", sources)
                .Set("spectators", _host.CountRole(LinkRole.Spectator))
                .Set("editors", _host.CountRole(LinkRole.Editor))
                .Set("consoles", _feed.Count + _host.CountRole(LinkRole.Console))
                .Set("forge", forge.ToJson())
                .Set("demoLock", _host.DemoLock)
                .Set("station", StationAddress)
                .Set("udpPort", UdpPort)
                .Set("httpPort", HttpPort)
                .Set("pair", PairUrl(StationAddress))
                .Set("uptimeS", Math.Round((DateTime.UtcNow - _startedUtc).TotalSeconds, 1))
                .Set("telemetryRows", _telemetryRows)
                .Set("events", _eventCount);
        }

        JsonValue LinkJson() { return SessionJsonLocked().Set("t", "link"); }

        /// <summary><c>GET /api/session</c> (Appendix B.3).</summary>
        public JsonValue GetSession() { lock (_gate) return SessionJsonLocked(); }

        JsonValue ParamJson(in ParamManifestEntry e)
        {
            float value;
            if (!_host.TryGetSnapshotValue(e.Name, out value)) value = e.Val;
            JsonValue p = JsonValue.Object()
                .Set("id", (int)e.Id).Set("name", e.Name ?? string.Empty).Set("group", e.Group ?? string.Empty)
                .Set("min", e.Min).Set("max", e.Max).Set("def", e.Def).Set("val", value)
                .Set("onRelease", e.OnRelease).Set("kind", ParamRegistry.KindName(e.Kind)).Set("active", e.Active);
            if (e.Live) p.Set("live", true);
            if (e.Names != null)
            {
                JsonValue names = JsonValue.Array();
                for (int i = 0; i < e.Names.Length; i++) names.Add(e.Names[i]);
                p.Set("names", names);
            }
            return p;
        }

        JsonValue ManifestJson()
        {
            JsonValue list = JsonValue.Array();
            ParamManifestMessage manifest = _host.Manifest;
            if (manifest != null)
                for (int i = 0; i < manifest.Params.Count; i++) list.Add(ParamJson(manifest.Params[i]));
            return JsonValue.Object().Set("t", "manifest").Set("ver", _manifestVer).Set("params", list).Set("locked", _host.DemoLock);
        }

        /// <summary>The phone's manifest with the current value of every parameter. Empty before the first manifest.</summary>
        public JsonValue GetParams()
        {
            lock (_gate)
            {
                JsonValue json = ManifestJson();
                json.Remove("t");
                return json;
            }
        }

        /// <summary>One parameter by name or by wire id; null when the manifest does not know it.</summary>
        public JsonValue GetParam(string key)
        {
            lock (_gate)
            {
                ParamManifestEntry entry;
                return TryFindParam(key, out entry) ? ParamJson(entry) : null;
            }
        }

        bool TryFindParam(string key, out ParamManifestEntry entry)
        {
            entry = default(ParamManifestEntry);
            ParamManifestMessage manifest = _host.Manifest;
            if (manifest == null || string.IsNullOrEmpty(key)) return false;
            int id;
            bool numeric = int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out id);
            for (int i = 0; i < manifest.Params.Count; i++)
            {
                ParamManifestEntry e = manifest.Params[i];
                if (!(numeric ? e.Id == id : string.Equals(e.Name, key, StringComparison.Ordinal))) continue;
                entry = e;
                return true;
            }
            return false;
        }

        string ParamName(ushort id)
        {
            ParamManifestMessage manifest = _host.Manifest;
            if (manifest == null) return null;
            for (int i = 0; i < manifest.Params.Count; i++)
                if (manifest.Params[i].Id == id) return manifest.Params[i].Name;
            return null;
        }

        // The 10 Hz digest of contract 3.3, from the newest State.
        JsonValue StateDigest()
        {
            StateMessage s = _lastState;
            // Tile ids are 64-bit; a JavaScript number is not, so the six words travel as text.
            var tile = new System.Text.StringBuilder(48);
            for (int i = 0; i < StateMessage.TileLength; i++)
            {
                if (i > 0) tile.Append(',');
                tile.Append(s.GetTile(i).ToString(CultureInfo.InvariantCulture));
            }
            JsonValue p = JsonValue.Array();
            for (int i = 0; i < StateMessage.ParamLength; i++) p.Add(s.GetParam(i));
            // Heading of the head's forward axis on the floor: atan2(forward.x, forward.z), forward = rot * (0, 0, 1).
            double fx = 2.0 * (s.HeadRotX * (double)s.HeadRotZ + s.HeadRotW * (double)s.HeadRotY);
            double fz = 1.0 - 2.0 * (s.HeadRotX * (double)s.HeadRotX + s.HeadRotY * (double)s.HeadRotY);
            double yawDeg = Math.Atan2(fx, fz) * (180.0 / Math.PI);
            return JsonValue.Object()
                .Set("t", "state")
                .Set("seq", (long)s.Seq).Set("t_us", s.TimeUs).Set("scene", (int)s.SceneId).Set("space", (int)s.SpaceId)
                .Set("tracking", s.Tracking).Set("jump", s.PoseJumpRejected).Set("cue", s.CueActive).Set("tier", s.QualityTier)
                .Set("lock", s.DemoLock).Set("bump", (int)s.BumpLevel)
                .Set("tile", tile.ToString()).Set("sheet", (int)s.Sheet)
                .Set("head", JsonValue.Array(s.HeadPosX, s.HeadPosY, s.HeadPosZ))
                .Set("yawDeg", Math.Round(yawDeg, 1))
                .Set("rot", JsonValue.Array(s.HeadRotX, s.HeadRotY, s.HeadRotZ, s.HeadRotW))
                .Set("p", p).Set("worldVer", (int)s.WorldVer)
                .Set("hz", Math.Round(_stateHz, 1)).Set("lossPct", Math.Round(LossPercent(), 2))
                .Set("latencyMs", LatencyJson())
                .Set("received", _statesReceived).Set("lost", _statesLost);
        }

        // Nearest-rank percentiles of the latency window, in milliseconds; zeros before the first clock exchange.
        JsonValue LatencyJson()
        {
            int n = _latencyCount;
            Array.Copy(_latencyUs, _latencySorted, n);
            Array.Sort(_latencySorted, 0, n);
            return JsonValue.Object()
                .Set("p50", LatencyMs(n, 0.50)).Set("p95", LatencyMs(n, 0.95)).Set("p99", LatencyMs(n, 0.99));
        }

        double LatencyMs(int n, double percentile)
        {
            if (n == 0) return 0.0;
            int rank = (int)Math.Ceiling(percentile * n) - 1;
            return Math.Round(_latencySorted[rank < 0 ? 0 : rank >= n ? n - 1 : rank] / 1000.0, 1);
        }

        /// <summary>The last <c>State</c> as the Console's digest; null before the first.</summary>
        public JsonValue GetState()
        {
            lock (_gate)
            {
                if (!_haveState) return null;
                JsonValue json = StateDigest();
                json.Remove("t");
                return json;
            }
        }

        /// <summary>The last <c>Telemetry</c> row; null before the first.</summary>
        public JsonValue GetTelemetry()
        {
            lock (_gate) return _haveTelemetry ? TelemetryJson.ToJson(_lastTelemetry) : null;
        }

        /// <summary>What a Console is told when it connects: the link, the manifest and the last telemetry row.</summary>
        public void Greet(ConsoleFeed.Client client)
        {
            lock (_gate)
            {
                client.Send(LinkJson().ToJson());
                if (_host.Manifest != null) client.Send(ManifestJson().ToJson());
                if (_haveTelemetry) client.Send(TelemetryJson.ToJson(_lastTelemetry).Set("t", "telemetry").ToJson());
                if (_outlineJson != null) client.Send(_outlineJson);
                if (_haveState) client.Send(StateDigest().ToJson());
            }
        }

        // ---- towards the phone ------------------------------------------------------------------------

        /// <summary>
        /// Sets one parameter on the phone, by name or wire id. The value travels as asked; the phone clamps it
        /// and echoes what it applied as an <c>Event param</c> (section 7.3.2). <paramref name="result"/> is
        /// <c>{id, name, value, sent}</c> with the value the phone will hold.
        /// </summary>
        public HubResult SetParam(string key, float value, out JsonValue result)
        {
            result = null;
            if (float.IsNaN(value) || float.IsInfinity(value)) return HubResult.Invalid;
            lock (_gate)
            {
                ParamManifestEntry e;
                if (!TryFindParam(key, out e)) return HubResult.Unknown;
                // Under the lock the phone ignores ParamSet; sending one would make the snapshot lie.
                if (_host.DemoLock) return HubResult.Locked;
                bool sent = _host.SendParam(e.Id, value);
                float held = value < e.Min ? e.Min : value > e.Max ? e.Max : value;
                result = JsonValue.Object().Set("id", (int)e.Id).Set("name", e.Name).Set("value", held).Set("sent", sent);
                HubLog("param", JsonValue.Object().Set("from", "console").Set("name", e.Name).Set("value", value).Set("sent", sent));
                return HubResult.Ok;
            }
        }

        /// <summary>
        /// Sends every known value of <c>{"values": {name: value}}</c> in one <c>ParamSet</c>, as loading a
        /// preset does. <paramref name="count"/> is how many values went out.
        /// </summary>
        public HubResult SetParams(JsonValue preset, out int count)
        {
            count = 0;
            if (preset == null || !preset.IsObject) return HubResult.Invalid;
            lock (_gate)
            {
                if (_host.DemoLock) return HubResult.Locked;
                if (_host.Manifest == null || !_host.HasPhone) return HubResult.NoPhone;
                JsonValue named = ByName(preset, out int skipped);
                count = _host.SendPreset(named);
                HubLog("param", JsonValue.Object().Set("from", "console").Set("count", count).Set("skipped", skipped));
                return HubResult.Ok;
            }
        }

        // Appendix B.3 keys preset values by id; the Hub's snapshot and LinkHost key them by name. Accept both.
        JsonValue ByName(JsonValue preset, out int skipped)
        {
            JsonValue values = preset["values"].IsObject ? preset["values"] : preset;
            JsonValue named = JsonValue.Object();
            skipped = 0;
            for (int i = 0; i < values.Count; i++)
            {
                ParamManifestEntry e;
                if (TryFindParam(values.KeyAt(i), out e)) named.Set(e.Name, values[i]);
                else skipped++;   // an id the phone does not know: left out, and counted in hub.log
            }
            return JsonValue.Object().Set("values", named);
        }

        /// <summary>The current value of every parameter as a preset, <c>{"values": {name: value}}</c>.</summary>
        public JsonValue GetSnapshot() { lock (_gate) return _host.SnapshotToJson(); }

        /// <summary>
        /// One of the five <c>OperatorCmd</c> commands: <c>home</c>, <c>recentre</c>, <c>quality</c> (args
        /// <c>{tier}</c>), <c>cue</c> (the STOP button) and <c>lock</c> (args <c>{on}</c>).
        /// </summary>
        public HubResult SendOperator(string cmd, JsonValue args)
        {
            OperatorCommand command;
            if (!OperatorCommand.TryFrom(new OperatorCmdMessage { Cmd = cmd ?? string.Empty, Args = args }, out command)) return HubResult.Unknown;
            return Send(command);
        }

        /// <summary>The sixth operator command, switch space (<c>SceneCmd</c>): <c>S0</c> to <c>S6</c>.</summary>
        public HubResult SendScene(string space, string variant, JsonValue args)
        {
            OperatorCommand command;
            var message = new SceneCmdMessage { Space = space ?? string.Empty, Variant = variant ?? string.Empty, Args = args };
            if (!OperatorCommand.TryFrom(message, out command)) return HubResult.Unknown;
            return Send(command);
        }

        HubResult Send(in OperatorCommand command)
        {
            lock (_gate)
            {
                bool sent = _host.SendCommand(command);
                JsonValue data = command.ToEvent(0, "hub").Data.Set("from", "console").Set("sent", sent);
                HubLog("cmd", data);
                if (command.Kind == OperatorCommandKind.Lock && _feed.Count > 0) _feed.Broadcast(LinkJson().ToJson());
                // The lock is the Hub's state too (it goes out in Welcome), so it succeeds with no phone connected.
                return sent || command.Kind == OperatorCommandKind.Lock ? HubResult.Ok : HubResult.NoPhone;
            }
        }

        /// <summary>
        /// <c>POST /internal/job</c>: a Forge <c>JobStatus</c> or <c>AssetReady</c> body, relayed to the phone and
        /// to every Spectator and Editor (section 7.4). A body with a <c>sha256</c> is an <c>AssetReady</c>.
        /// </summary>
        public HubResult PostJob(JsonValue body)
        {
            if (body == null || !body.IsObject || string.IsNullOrEmpty(body.GetString("jobId"))) return HubResult.Invalid;
            lock (_gate)
            {
                string kind;
                if (!string.IsNullOrEmpty(body.GetString("sha256")) && !string.IsNullOrEmpty(body.GetString("url")))
                {
                    var asset = new AssetReadyMessage();
                    asset.FromJson(body);
                    _host.SendAssetReady(asset);
                    kind = "ready";
                }
                else
                {
                    var status = new JobStatusMessage();
                    status.FromJson(body);
                    _host.SendJobStatus(status);
                    kind = "status";
                }
                HubLog("job", body);
                if (_feed.Count > 0) _feed.Broadcast(body.Clone().Set("kind", kind).Set("t", "job").ToJson());
                return HubResult.Ok;
            }
        }

        /// <summary>
        /// <c>PUT /logs/{session}/perf.csv</c> (Master Plan section 12.2, "CSV on the phone, uploaded over HTTP
        /// later"): the phone's own CSV for a session. The rows the Hub's file lacks are added in <c>seq</c>
        /// order; the rows it has are left alone. A session the Hub never saw (the phone walked with no Station)
        /// gets its folder now. <paramref name="added"/> is how many rows were new.
        /// </summary>
        public HubResult MergePerf(string session, string csv, out int added)
        {
            added = 0;
            if (!Names.IsSafe(session)) return HubResult.Unknown;
            bool hasHeader;
            int skipped;
            List<string> rows = PerfCsv.Rows(csv, out hasHeader, out skipped);
            if (!hasHeader) return HubResult.Invalid;
            // The file is rewritten under the gate, so the session cannot change folders half-way. An upload comes
            // after a session, when a few milliseconds of the link loop are not missed.
            lock (_gate)
            {
                if (session == SessionId)
                {
                    added = _log.MergePerf(rows);
                }
                else
                {
                    string folder = Path.Combine(Path.GetFullPath(_options.LogRoot), session);
                    Directory.CreateDirectory(folder);
                    added = PerfCsv.MergeFile(Path.Combine(folder, SessionLog.PerfFile), rows);
                }
                HubLog("perf", JsonValue.Object().Set("session", session).Set("rows", rows.Count).Set("added", added).Set("skipped", skipped));
            }
            return HubResult.Ok;
        }

        /// <summary>
        /// A message from a Console over the WebSocket (Appendix B.3): <c>{t: "param", id, value}</c>,
        /// <c>{t: "scene", space, variant, args}</c>, <c>{t: "op", cmd, args}</c>, <c>{t: "fms", score}</c>,
        /// <c>{t: "note", text}</c> and <c>{t: "preset", values}</c>.
        /// Returns the reply for that Console, or null when there is nothing to say.
        /// </summary>
        public JsonValue HandleConsoleMessage(JsonValue message)
        {
            if (message == null || !message.IsObject) return Error("not a JSON object");
            string type = message.GetString("t", string.Empty);
            HubResult result;
            switch (type)
            {
                case "param":
                {
                    JsonValue id = message["id"];
                    // "id" is the wire index; "name" may stand in for it.
                    string key = id.IsString ? id.AsString() : id.IsNumber ? id.AsInt().ToString(CultureInfo.InvariantCulture) : message.GetString("name");
                    JsonValue v = message["value"];
                    if (key == null || !(v.IsNumber || v.IsBool)) return Error("param needs id and value");
                    JsonValue ignored;
                    result = SetParam(key, v.IsBool ? (v.AsBool() ? 1f : 0f) : v.AsFloat(), out ignored);
                    break;
                }
                case "scene":
                    result = SendScene(SpaceName(message["space"]), message.GetString("variant", string.Empty), message["args"].IsObject ? message["args"] : null);
                    break;
                case "op":
                    result = SendOperator(message.GetString("cmd"), message["args"].IsObject ? message["args"] : null);
                    break;
                case "preset":
                {
                    int count;
                    result = SetParams(message, out count);
                    break;
                }
                case "fms":
                {
                    // The walker's comfort score, 0 to 20, as the operator types it (contract 3.3).
                    JsonValue score = message["score"];
                    if (!score.IsNumber || score.AsInt() < 0 || score.AsInt() > 20) return Error("fms needs a score from 0 to 20");
                    lock (_gate) HubEvent("fms", JsonValue.Object().Set("score", score.AsInt()));
                    return null;
                }
                case "note":
                {
                    string text = message.GetString("text");
                    if (string.IsNullOrWhiteSpace(text)) return Error("note needs a text");
                    lock (_gate) HubEvent("note", JsonValue.Object().Set("text", text));
                    return null;
                }
                case "ping":
                    return JsonValue.Object().Set("t", "pong");
                default:
                    return Error("unknown message type '" + type + "'");
            }
            return result == HubResult.Ok ? null : Error(type + ": " + Describe(result));
        }

        /// <summary>Accepts a space as <c>"S2"</c> or as the number 2.</summary>
        public static string SpaceName(JsonValue space)
        {
            if (space == null) return string.Empty;
            if (space.IsNumber) return "S" + space.AsInt().ToString(CultureInfo.InvariantCulture);
            return space.AsString(string.Empty) ?? string.Empty;
        }

        public static string Describe(HubResult result)
        {
            switch (result)
            {
                case HubResult.Ok: return "ok";
                case HubResult.Unknown: return "not known";
                case HubResult.Invalid: return "bad request";
                case HubResult.Locked: return "the demo lock is on";
                default: return "no phone connected";
            }
        }

        static JsonValue Error(string message) { return JsonValue.Object().Set("t", "error").Set("msg", message); }

        // ---- helpers ----------------------------------------------------------------------------------

        /// <summary>The deep link the pairing QR carries (section 7.1).</summary>
        public string PairUrl(string address)
        {
            return "atlas://" + address + ":" + UdpPort.ToString(CultureInfo.InvariantCulture);
        }

        string ForgeUrlForClients()
        {
            // The phone needs the Station's LAN address, not the loopback one the Hub itself uses.
            Uri uri;
            int port = Uri.TryCreate(_options.ForgeUrl, UriKind.Absolute, out uri) ? uri.Port : 8000;
            if (uri != null && !uri.IsLoopback) return _options.ForgeUrl;
            return "http://" + StationAddress + ":" + port.ToString(CultureInfo.InvariantCulture);
        }

        static string MakeSessionId(string root, string wanted)
        {
            string id = wanted ?? DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            if (!Directory.Exists(Path.Combine(root, id))) return id;
            // A restart within the same second, or a session id used before: never overwrite earlier logs.
            for (int n = 2; ; n++)
            {
                string candidate = id + "-" + n.ToString(CultureInfo.InvariantCulture);
                if (!Directory.Exists(Path.Combine(root, candidate))) return candidate;
            }
        }
    }
}
