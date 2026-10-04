using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Atlas.Link;
using Atlas.Station;

namespace Atlas.Tools
{
    /// <summary>
    /// A phone with no phone (Master Plan Appendix C.8): joins a Hub as role <c>phone</c> through the app's own
    /// <see cref="LinkClient"/> and sends <c>State</c> from a recorded <c>state.bin</c> at its original timing,
    /// or from a synthetic walk at 60 Hz, so the Console and the Spectator can be developed against it.
    /// <para>A recording is played as it was (lane D contract 2.12): the session's manifest, room outline and
    /// <c>world</c> events are sent again, only <c>seq</c> and <c>t_us</c> are rewritten, and parameters and
    /// commands from the Console are logged and otherwise ignored, because the recorded walk cannot change.</para>
    /// <para>The synthetic walk is a live stand-in: it declares a few of the app's parameters, applies what the
    /// Console sets, answers the six operator commands and sends <c>Telemetry</c> once a second.</para>
    /// </summary>
    public sealed class ReplayPlayer
    {
        public const int ExitOk = 0;
        public const int ExitNoHub = 1;
        public const int ExitBadFile = 2;

        readonly ReplayOptions _options;
        readonly ILinkClock _clock;
        readonly ParamRegistry _registry = new ParamRegistry();
        readonly LinkClient _client;
        readonly bool _replaying;
        string _statePath, _folder;
        bool _introDue;
        readonly SyntheticWalk _walk = new SyntheticWalk();
        readonly ParamHandle _cullRho;
        readonly ParamEntry _renderScale, _msaa, _stateHz, _poseSource;
        OperatorCommand _lastWalk;
        List<StateLog.Entry> _recording;
        long _statesSent, _commandsReceived;
        long _cueUntilUs;

        public ReplayPlayer(ReplayOptions options, ILinkTransport transport = null, ILinkClock clock = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _clock = clock ?? new StopwatchClock();

            // A handful of the app's own parameters (Master Plan sections 7.3.2 and 12.2), so the Console has sliders.
            _cullRho = _registry.DeclareFloat("curved.cullRho", "curved", 2f, 3.5f, 3f).Handle;
            _renderScale = _registry.DeclareFloat("render.scale", "render", 0.5f, 1f, 1f);
            _msaa = _registry.DeclareEnum("render.msaa", "render", new[] { "off", "2x", "4x" }, 2);
            _registry.DeclareInt("glued.copies", "glued", 3, 5, 5, null, true);
            _registry.DeclareBool("pose.gyroPredict", "pose", true);
            _stateHz = _registry.DeclareInt("link.stateHz", "link", 10, 60, 60);
            // walker: the Map page's keys drive the head instead of the track, as on the phone.
            _poseSource = _registry.DeclareEnum("pose.source", "pose", new[] { "tracked", "replay", "walker" }, 0);

            // A recording brings its own manifest; with no registry the client leaves every ParamSet alone.
            _replaying = options.File != null;
            _client = new LinkClient(transport ?? new LiteNetTransport(), _clock, LinkRole.Phone, _replaying ? null : _registry)
            {
                Device = "ReplayPhone",
                Build = "replay",
                Scene = "S0",
                // Every run is a session of its own, named as the phone names its sessions; the Hub logs under it.
                SessionId = NewSessionId(DateTime.UtcNow, Guid.NewGuid().GetHashCode())
            };
            // Every Welcome, the first and the one after a Hub restart, is followed by the session's introduction.
            _client.Welcomed += welcome => _introDue = true;
        }

        public LinkClient Client { get { return _client; } }
        public ParamRegistry Registry { get { return _registry; } }

        /// <summary>The session this run announces in its <c>Hello</c>, which the Hub adopts for its log folder.</summary>
        public string SessionId { get { return _client.SessionId; } }

        /// <summary>
        /// A session id as the app makes them (lane D contract 5.5): UTC time and four random hexadecimal digits,
        /// for example <c>20261004T153012Z-a3f9</c>.
        /// </summary>
        public static string NewSessionId(DateTime utc, int random)
        {
            return utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" +
                   (random & 0xFFFF).ToString("x4", CultureInfo.InvariantCulture);
        }

        /// <summary><c>State</c> packets handed to the link so far.</summary>
        public long StatesSent { get { return Interlocked.Read(ref _statesSent); } }

        /// <summary>Operator commands received so far.</summary>
        public long CommandsReceived { get { return Interlocked.Read(ref _commandsReceived); } }

        /// <summary>Plays until the recording ends, the time is up or <paramref name="stop"/> is set. Returns an exit code.</summary>
        public int Run(CancellationToken stop)
        {
            if (_replaying)
            {
                bool isFolder = Directory.Exists(_options.File);
                _statePath = isFolder ? Path.Combine(_options.File, "state.bin") : _options.File;
                _folder = isFolder ? _options.File : Path.GetDirectoryName(Path.GetFullPath(_options.File));
                try
                {
                    _recording = StateLog.ReadAll(_statePath);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    Console.Error.WriteLine("ReplayPhone: " + e.Message);
                    return ExitBadFile;
                }
                if (_recording.Count == 0)
                {
                    Console.Error.WriteLine("ReplayPhone: " + _statePath + " holds no State records");
                    return ExitBadFile;
                }
                Say("recording: " + _recording.Count + " states, " +
                    ((_recording[_recording.Count - 1].HubUs - _recording[0].HubUs) / 1e6).ToString("0.0") + " s");
            }

            if (!_client.Start(_options.Address, _options.Port))
            {
                Console.Error.WriteLine("ReplayPhone: cannot open a UDP socket");
                return ExitNoHub;
            }
            Say("connecting to " + _options.Address + ":" + _options.Port + " ...");

            try
            {
                return Play(stop);
            }
            finally
            {
                _client.Stop("replay finished");
            }
        }

        int Play(CancellationToken stop)
        {
            long startUs = _clock.NowUs;
            long playStartUs = -1;       // when the Hub first welcomed us
            long nextTelemetryUs = 0;
            long frame = 0;              // synthetic walk: frames sent
            int index = 0;               // recording: next entry
            long loopBaseUs = 0;         // recording: playback time at which the current pass began
            var state = new StateMessage();
            var telemetry = new TelemetryMessage();
            int sentThisSecond = 0;

            while (!stop.IsCancellationRequested)
            {
                _client.Poll();
                long now = _clock.NowUs;

                if (!_client.IsReady)
                {
                    if (playStartUs < 0 && now - startUs > (long)(_options.ConnectTimeoutSeconds * 1e6))
                    {
                        Console.Error.WriteLine("ReplayPhone: no Hub answered at " + _options.Address + ":" + _options.Port +
                                                (_client.LastByeReason != null ? " (" + _client.LastByeReason + ")" : string.Empty));
                        return ExitNoHub;
                    }
                    // Offline in mid-play: the client reconnects by itself and the walk carries on, as on the phone.
                    Thread.Sleep(1);
                    continue;
                }

                if (playStartUs < 0)
                {
                    playStartUs = now;
                    nextTelemetryUs = now + 1000000;
                    Say("connected, session " + _client.Welcome.SessionId + (_recording != null ? ", playing the recording" : ", walking"));
                }
                if (_introDue)
                {
                    _introDue = false;
                    SendIntroduction();
                }

                HandleCommands(now);
                _client.StateHz = _stateHz.Int;

                long elapsed = now - playStartUs;
                if (_options.Seconds > 0 && elapsed >= (long)(_options.Seconds * 1e6)) break;

                if (_recording != null)
                {
                    long firstUs = _recording[0].HubUs;
                    while (index < _recording.Count &&
                           (long)((_recording[index].HubUs - firstUs) / _options.Speed) <= elapsed - loopBaseUs)
                    {
                        state = _recording[index].State;
                        if (_client.SendState(ref state))
                        {
                            Interlocked.Increment(ref _statesSent);
                            sentThisSecond++;
                        }
                        index++;
                    }
                    if (index >= _recording.Count)
                    {
                        if (!_options.Loop) break;
                        index = 0;
                        loopBaseUs = elapsed + 16667;
                    }
                }
                else if (elapsed >= frame * 1000000L / _options.Hz)
                {
                    _walk.Driven = _poseSource.Int == 2;
                    _walk.Fill(ref state, frame / (double)_options.Hz, _registry, _cullRho);
                    if (now < _cueUntilUs)
                    {
                        // The STOP cue: indicator, tone and haptic at STRONG level; the world keeps rendering (section 7.3.2).
                        state.CueActive = true;
                        state.BumpLevel = 2;
                    }
                    if (_client.SendState(ref state))
                    {
                        Interlocked.Increment(ref _statesSent);
                        sentThisSecond++;
                    }
                    frame++;
                    // After a stall, skip the frames that are already history instead of sending a burst.
                    long due = elapsed * _options.Hz / 1000000L;
                    if (due > frame) frame = due;
                }

                if (_options.Telemetry && !_replaying && now >= nextTelemetryUs)
                {
                    nextTelemetryUs += 1000000;
                    FillTelemetry(ref telemetry, sentThisSecond);
                    _client.SendTelemetry(ref telemetry);
                    sentThisSecond = 0;
                }

                Thread.Sleep(1);
            }

            // The last packets are still in the transport's hands: give them a moment before the socket closes.
            long lingerUntilUs = _clock.NowUs + 100000;
            while (_clock.NowUs < lingerUntilUs)
            {
                _client.Poll();
                Thread.Sleep(1);
            }

            Say("done: " + StatesSent + " states sent, " + CommandsReceived + " commands received");
            return ExitOk;
        }

        void HandleCommands(long now)
        {
            OperatorCommand command;
            while (_client.TryDequeueCommand(out command))
            {
                Interlocked.Increment(ref _commandsReceived);
                if (_replaying)
                {
                    Say("command ignored by the recording: " + command.Name);
                    continue;
                }
                switch (command.Kind)
                {
                    case OperatorCommandKind.SwitchSpace:
                        _walk.Space = command.Space;
                        _walk.WorldVer++;
                        break;
                    case OperatorCommandKind.Home:
                        _walk.Space = 0;
                        _walk.WorldVer++;
                        break;
                    case OperatorCommandKind.Quality:
                        // -1 is "automatic": the governor chooses, and with no load it sits at Nominal (0). A pinned
                        // tier is reported as pinned, High (4) included; Critical (3) is the lowest rung.
                        _walk.QualityTier = command.Tier < 0 ? 0 : Math.Min(command.Tier, 4);
                        break;
                    case OperatorCommandKind.Cue:
                        _cueUntilUs = now + 1000000;
                        break;
                    case OperatorCommandKind.Walk:
                    {
                        _walk.Walk(command);
                        // A held key is repeated several times a second: as the app does, only a change is logged.
                        bool same = _lastWalk.Kind == OperatorCommandKind.Walk && command.MoveX == _lastWalk.MoveX &&
                                    command.MoveY == _lastWalk.MoveY && command.Turn == _lastWalk.Turn && command.Speed == _lastWalk.Speed;
                        _lastWalk = command;
                        if (same) continue;
                        break;
                    }
                    // Recentre has nothing to move here, and the lock is applied by LinkClient itself.
                }
                _client.Scene = "S" + _walk.Space;
                _client.Send(command.ToEvent(_client.NowUs, _client.Scene));
                Say("command: " + command.Name);
            }
        }

        /// <summary>
        /// What a phone sends once it is welcomed. The synthetic walk sends its room (its manifest goes out with
        /// the handshake). A recording sends what its session folder kept: <c>manifest.json</c>,
        /// <c>outline.bin</c> and the <c>world</c> events of <c>events.jsonl</c>.
        /// </summary>
        void SendIntroduction()
        {
            if (!_replaying)
            {
                _client.SendRoomOutline(SyntheticWalk.Outline());
                return;
            }

            try
            {
                string manifestPath = Path.Combine(_folder, "manifest.json");
                JsonValue json;
                if (File.Exists(manifestPath) && AtlasJson.TryParse(File.ReadAllText(manifestPath), out json))
                {
                    var manifest = new ParamManifestMessage();
                    manifest.FromJson(json);
                    _client.Send(manifest);
                    Say("sent manifest.json: " + manifest.Params.Count + " parameters");
                }

                string outlinePath = Path.Combine(_folder, "outline.bin");
                if (File.Exists(outlinePath))
                {
                    byte[] bytes = File.ReadAllBytes(outlinePath);
                    var outline = new RoomOutlineMessage();
                    if (outline.ReadFrom(bytes, 0, bytes.Length) && _client.SendRoomOutline(outline)) Say("sent outline.bin");
                }

                string eventsPath = Path.Combine(_folder, "events.jsonl");
                if (File.Exists(eventsPath))
                {
                    int worlds = 0;
                    using (var stream = new FileStream(eventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            EventMessage e;
                            if (!EventMessage.TryParseLine(line, out e) || e.Ev != "world") continue;
                            e.TimeUs = _client.NowUs;
                            if (_client.Send(e)) worlds++;
                        }
                    }
                    if (worlds > 0) Say("sent " + worlds + " world events");
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // The recording itself is what matters; its companions are a convenience.
                Console.Error.WriteLine("ReplayPhone: " + e.Message);
            }
        }

        void FillTelemetry(ref TelemetryMessage t, int statesThisSecond)
        {
            t = new TelemetryMessage();
            t.SceneId = (ushort)_walk.Space;
            t.SpaceId = (byte)_walk.Space;
            t.Tier = (byte)_walk.QualityTier;
            t.Fps = statesThisSecond;
            t.P50 = 16.7f;
            t.P95 = 16.9f;
            t.P99 = 17.1f;
            t.Max = 17.4f;
            t.CpuMainP95 = 6.0f;
            t.CpuRenderP95 = 3.0f;
            t.GpuP95 = 7.5f;
            t.RenderScale = _renderScale.Float;
            t.DrawCalls = 42;
            t.SetPass = 6;
            t.TrisK = 180;
            t.MemMB = 640;
            t.VisibleTiles = 61;
            t.Copies = 1;
            t.RttMs = (ushort)Math.Min(_client.RttMs, ushort.MaxValue);
            t.LossPermille = (ushort)Math.Min(_client.LossPermille, ushort.MaxValue);
            t.Battery = 100;
            t.Tracking = 2;
            t.Msaa = (byte)(_msaa.Int == 0 ? 1 : _msaa.Int * 2);
            t.StateHz = (byte)_client.StateHz;
        }

        void Say(string text)
        {
            if (!_options.Quiet) Console.WriteLine("ReplayPhone: " + text);
        }
    }
}
