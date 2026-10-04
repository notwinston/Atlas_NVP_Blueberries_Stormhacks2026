using System;
using System.Collections.Generic;

namespace Atlas.Link
{
    /// <summary>A client finished its <c>Hello</c>.</summary>
    public delegate void LinkPeerJoinedHandler(int peerId, HelloMessage hello);

    /// <summary>A client left.</summary>
    public delegate void LinkPeerLeftHandler(int peerId, LinkRole role, string reason);

    /// <summary>
    /// The phone's <c>State</c> as decoded, with the raw 202 bytes for the binary log. Both are valid only during
    /// the call.
    /// </summary>
    public delegate void LinkHostStateHandler(int peerId, in StateMessage state, byte[] raw, int offset, int length);

    /// <summary>A <c>Telemetry</c> row from the phone. Valid only during the call.</summary>
    public delegate void LinkHostTelemetryHandler(int peerId, in TelemetryMessage telemetry);

    /// <summary>A JSON message as received: its id, its parsed body and who sent it.</summary>
    public delegate void LinkHostJsonHandler(int peerId, byte messageId, JsonValue body);

    /// <summary>
    /// A client answered one of the Hub's clock pings: the pong, and the Hub's clock when it arrived. The
    /// client's clock estimate already includes it.
    /// </summary>
    public delegate void LinkHostPongHandler(int peerId, in TPongMessage pong, long hubReceiveUs);

    /// <summary>
    /// The Hub end of the link (Master Plan section 7.1): the only listener. It answers the handshake and the
    /// clock pings, relays the phone's <c>State</c> and <c>RoomOutline</c> to Spectators and Editors, forwards
    /// parameters and the six operator commands to the phone, and keeps the last parameter snapshot so that a
    /// phone that reconnects gets its values back (RESYNC, section 7.2). The Hub process wires its web server and
    /// its logs to the events here. Call <see cref="Poll"/> in a loop; nothing blocks.
    /// <para>
    /// A Spectator is sent everything it needs to redraw the walker's view (section 7.3.1): the relayed
    /// <c>State</c>, the room outline, the phone's parameter manifest and every <c>Event</c> (parameter changes
    /// and <c>world</c> events among them), and the Forge's job messages. One that joins late is given the kept
    /// outline, the manifest with the values of that moment, the newest <c>world</c> event and the assets the
    /// phone has not acknowledged.
    /// </para>
    /// <para>
    /// The Hub pings every client four times per second. That is its heartbeat, and it gives the Hub a clock
    /// offset per client, from which the latency of a <c>State</c> is its receive time minus its send time.
    /// </para>
    /// </summary>
    public sealed class LinkHost : ILinkTransportListener
    {
        struct PendingKick
        {
            public int PeerId;
            public long AtUs;
        }

        sealed class Peer
        {
            public int Id;
            public LinkRole Role;
            public HelloMessage Hello;
            public bool AwaitingFirstManifest;
            // The client's clock against the Hub's, from the heartbeat pings.
            public readonly ClockSync Clock = new ClockSync { SteadyPeriodUs = HeartbeatMs * 1000L };
            // What the last Welcome sent to this client carried; see SendWelcomeToWatchers.
            public string WelcomeStamp;
        }

        /// <summary>
        /// Milliseconds between the clock pings the Hub sends to each client once its opening burst is over. With
        /// it the longest healthy gap on a client's watchdog is 250 ms (Master Plan section 7.2).
        /// </summary>
        public const int HeartbeatMs = 250;

        readonly ILinkTransport _transport;
        readonly ILinkClock _clock;
        readonly Dictionary<int, Peer> _peers = new Dictionary<int, Peer>();
        readonly List<Peer> _peerList = new List<Peer>();
        readonly Dictionary<string, float> _snapshot = new Dictionary<string, float>(StringComparer.Ordinal);
        readonly List<AssetReadyMessage> _unackedAssets = new List<AssetReadyMessage>();
        readonly List<PendingKick> _kicks = new List<PendingKick>();
        readonly byte[] _smallBuffer = new byte[64];
        readonly byte[] _largeBuffer = new byte[RoomOutlineMessage.MaxSize];
        readonly ParamSetMessage _paramSetIn = new ParamSetMessage();
        readonly ParamSetMessage _paramSetOut = new ParamSetMessage();
        readonly RoomOutlineMessage _outlineIn = new RoomOutlineMessage();
        readonly ILinkPeerAddresses _addresses;
        byte[] _lastOutline;
        byte[] _lastWorldEvent;
        StateMessage _stateIn;
        TelemetryMessage _telemetryIn;
        int _phone = -1;

        public LinkHost(ILinkTransport transport, ILinkClock clock)
        {
            if (transport == null) throw new ArgumentNullException("transport");
            if (clock == null) throw new ArgumentNullException("clock");
            _transport = transport;
            _clock = clock;
            _addresses = transport as ILinkPeerAddresses;
            _transport.Listener = this;
        }

        // ---- what Welcome carries ---------------------------------------------------------------------

        public string SessionId = string.Empty;
        public string WorldHash = string.Empty;
        public string RoomId = string.Empty;
        public string ForgeUrl = string.Empty;
        /// <summary>Sent in <c>Welcome</c> and kept in step with <c>OperatorCmd lock</c> commands passing through.</summary>
        public bool DemoLock;

        // ---- state ------------------------------------------------------------------------------------

        public ILinkTransport Transport { get { return _transport; } }
        public bool IsRunning { get { return _transport.IsRunning; } }
        public int Port { get { return _transport.LocalPort; } }

        /// <summary>Clients that have said Hello.</summary>
        public int PeerCount { get { return _peerList.Count; } }

        /// <summary>The phone's peer id, or -1 when no phone is connected.</summary>
        public int PhonePeer { get { return _phone; } }

        public bool HasPhone { get { return _phone >= 0; } }

        /// <summary>The Hub's clock, microseconds: the reference for phone and spectators.</summary>
        public long NowUs { get { return _clock.NowUs; } }

        /// <summary>The phone's last manifest; null before the first.</summary>
        public ParamManifestMessage Manifest { get; private set; }

        public int UnackedAssetCount { get { return _unackedAssets.Count; } }

        public LinkRole RoleOf(int peerId)
        {
            Peer p;
            return _peers.TryGetValue(peerId, out p) ? p.Role : LinkRole.Unknown;
        }

        public int CountRole(LinkRole role)
        {
            int n = 0;
            for (int i = 0; i < _peerList.Count; i++) if (_peerList[i].Role == role) n++;
            return n;
        }

        public int GetRttMs(int peerId) { return _transport.GetRttMs(peerId); }
        public int GetLossPermille(int peerId) { return _transport.GetLossPermille(peerId); }

        /// <summary>
        /// A client's network address as text, or an empty string when the peer is unknown or the transport has
        /// no addresses. For the link panel and the log, not for the frame path.
        /// </summary>
        public string GetAddress(int peerId)
        {
            return (_addresses != null ? _addresses.GetPeerAddress(peerId) : null) ?? string.Empty;
        }

        /// <summary>
        /// The Hub's estimate of a client's clock from its own pings: <paramref name="offsetUs"/> is the client's
        /// clock minus the Hub's, <paramref name="rttUs"/> the round trip of the best recent exchange. False
        /// until the first pong of that client. Allocates nothing.
        /// </summary>
        public bool TryGetClock(int peerId, out long offsetUs, out long rttUs)
        {
            Peer p;
            if (_peers.TryGetValue(peerId, out p) && p.Clock.HasEstimate)
            {
                offsetUs = p.Clock.OffsetUs;
                rttUs = p.Clock.RttUs;
                return true;
            }
            offsetUs = 0;
            rttUs = 0;
            return false;
        }

        /// <summary>
        /// A time on a client's clock (<c>State.t_us</c>, an event's <c>t_us</c>) as a time on the Hub's clock.
        /// Returned unchanged while there is no estimate for that client. Allocates nothing.
        /// </summary>
        public long ToHubUs(int peerId, long peerUs)
        {
            Peer p;
            return _peers.TryGetValue(peerId, out p) && p.Clock.HasEstimate ? p.Clock.ToLocalUs(peerUs) : peerUs;
        }

        // ---- events (all raised inside Poll) ----------------------------------------------------------

        public event LinkPeerJoinedHandler PeerJoined;
        /// <summary>
        /// Every <c>Hello</c> the Hub accepts, the first of a connection and the ones a client sends again when its
        /// session or world changes, raised before the <c>Welcome</c> is written. A handler that sets
        /// <see cref="SessionId"/>, <see cref="WorldHash"/> or <see cref="RoomId"/> here decides what that
        /// <c>Welcome</c> carries: this is where the Hub adopts the phone's session.
        /// </summary>
        public event LinkPeerJoinedHandler HelloReceived;
        public event LinkPeerLeftHandler PeerLeft;
        public event LinkHostStateHandler StateReceived;
        public event LinkHostTelemetryHandler TelemetryReceived;
        /// <summary>The phone sent its manifest. The Console builds its sliders from it.</summary>
        public event Action<int, ParamManifestMessage> ManifestReceived;
        /// <summary>The instance is reused: copy what must be kept.</summary>
        public event Action<int, RoomOutlineMessage> RoomOutlineReceived;
        /// <summary>
        /// Every JSON message received: Hello, ParamManifest, SceneCmd, OperatorCmd, Event, Log, AssetAck, Bye.
        /// The Hub forwards the body to the Console unchanged and writes events and logs to disk.
        /// </summary>
        public event LinkHostJsonHandler JsonReceived;
        /// <summary>A link client (Editor or Console role) asked for parameters to be set; already forwarded to the phone.</summary>
        public event Action<int, ParamSetMessage> ParamSetReceived;
        /// <summary>A client answered a heartbeat ping; <see cref="TryGetClock"/> has the estimate it fed.</summary>
        public event LinkHostPongHandler TPongReceived;

        // ---- lifecycle --------------------------------------------------------------------------------

        public bool Start(int port = LinkProtocol.DefaultPort) { return _transport.StartHost(port); }

        /// <summary>Says <c>Bye</c> to everyone and closes the socket. The parameter snapshot is kept.</summary>
        public void Stop(string reason = "hub stopping")
        {
            if (!_transport.IsRunning) return;
            byte[] bye = new ByeMessage(reason).ToBytes();
            for (int i = 0; i < _peerList.Count; i++) _transport.Send(_peerList[i].Id, bye, 0, bye.Length, LinkDelivery.Reliable);
            _transport.Flush();
            _transport.Stop();
            _peers.Clear();
            _peerList.Clear();
            _kicks.Clear();
            _phone = -1;
        }

        /// <summary>Delivers what arrived and relays it, and sends the heartbeat pings that are due. Never blocks.</summary>
        public void Poll()
        {
            _transport.Poll();
            if (_peerList.Count == 0 && _kicks.Count == 0) return;
            long now = _clock.NowUs;
            Heartbeat(now);
            for (int i = _kicks.Count - 1; i >= 0; i--)
            {
                if (now < _kicks[i].AtUs) continue;
                _transport.Disconnect(_kicks[i].PeerId);
                _kicks.RemoveAt(i);
            }
        }

        // One ping per client per heartbeat (eight in quick succession after its Hello). Allocates nothing.
        void Heartbeat(long now)
        {
            bool sent = false;
            for (int i = 0; i < _peerList.Count; i++)
            {
                Peer peer = _peerList[i];
                TPingMessage ping;
                if (peer.Role == LinkRole.Unknown || !peer.Clock.TryNextPing(now, out ping)) continue;
                int n = ping.WriteTo(_smallBuffer, 0);
                _transport.Send(peer.Id, _smallBuffer, 0, n, LinkDelivery.Unreliable);
                sent = true;
            }
            // t1 is now: the pings must not wait for the transport's next tick.
            if (sent) _transport.Flush();
        }

        // ---- towards the phone ------------------------------------------------------------------------

        /// <summary>Sets one parameter on the phone by wire id. False when no phone is connected.</summary>
        public bool SendParam(ushort id, float value)
        {
            _paramSetOut.Clear();
            _paramSetOut.Add(id, value);
            return SendParamSet(_paramSetOut);
        }

        /// <summary>Sets one parameter on the phone by name, resolved through the phone's manifest.</summary>
        public bool SendParam(string name, float value)
        {
            ushort id;
            return TryGetParamId(name, out id) && SendParam(id, value);
        }

        /// <summary>
        /// Sends a <c>ParamSet</c> to the phone and records the values in the snapshot. A preset is loaded by
        /// filling one message with all its values (section 7.3.2).
        /// </summary>
        public bool SendParamSet(ParamSetMessage set)
        {
            if (set == null) return false;
            NoteParamSet(set);
            if (_phone < 0) return false;
            int n = set.WriteTo(_largeBuffer, 0);
            return _transport.Send(_phone, _largeBuffer, 0, n, LinkDelivery.Reliable);
        }

        /// <summary>
        /// Loads a preset <c>{"values": {name: value}}</c>: every value the phone's manifest knows goes out in
        /// one <c>ParamSet</c>. Returns how many values were sent.
        /// </summary>
        public int SendPreset(JsonValue preset)
        {
            if (preset == null || !preset.IsObject) return 0;
            JsonValue values = preset["values"].IsObject ? preset["values"] : preset;
            _paramSetOut.Clear();
            for (int i = 0; i < values.Count; i++)
            {
                ushort id;
                JsonValue v = values[i];
                if (!TryGetParamId(values.KeyAt(i), out id)) continue;
                if (v.IsNumber) _paramSetOut.Add(id, v.AsFloat());
                else if (v.IsBool) _paramSetOut.Add(id, v.AsBool() ? 1f : 0f);
            }
            int count = _paramSetOut.Count;
            if (count > 0) SendParamSet(_paramSetOut);
            return count;
        }

        /// <summary>Sends one of the six operator commands to the phone.</summary>
        public bool SendCommand(in OperatorCommand command)
        {
            if (command.Kind == OperatorCommandKind.Lock) DemoLock = command.On;
            return _phone >= 0 && SendTo(_phone, command.ToMessage());
        }

        /// <summary>Forwards a Forge <c>JobStatus</c> to the phone and to every Spectator and Editor.</summary>
        public void SendJobStatus(JobStatusMessage status)
        {
            if (status == null) return;
            byte[] bytes = status.ToBytes();
            SendToPhoneAndWatchers(bytes, bytes.Length);
        }

        /// <summary>
        /// Forwards a Forge <c>AssetReady</c> to the phone and to every Spectator and Editor, and remembers it
        /// until the phone acknowledges it, so that a phone that reconnects is told again (section 7.2).
        /// </summary>
        public void SendAssetReady(AssetReadyMessage asset)
        {
            if (asset == null) return;
            for (int i = _unackedAssets.Count - 1; i >= 0; i--)
                if (_unackedAssets[i].JobId == asset.JobId) _unackedAssets.RemoveAt(i);
            _unackedAssets.Add(asset);
            byte[] bytes = asset.ToBytes();
            SendToPhoneAndWatchers(bytes, bytes.Length);
        }

        /// <summary>Sends a JSON message to one client.</summary>
        public bool SendTo(int peerId, JsonMessage message)
        {
            if (message == null || !_peers.ContainsKey(peerId)) return false;
            byte[] bytes = message.ToBytes();
            return _transport.Send(peerId, bytes, 0, bytes.Length, LinkDelivery.Reliable);
        }

        /// <summary>Sends raw packet bytes to every client of a role. Allocates nothing.</summary>
        public void SendToRole(LinkRole role, byte[] data, int offset, int length, LinkDelivery delivery)
        {
            for (int i = 0; i < _peerList.Count; i++)
                if (_peerList[i].Role == role) _transport.Send(_peerList[i].Id, data, offset, length, delivery);
        }

        /// <summary>How long a kicked client is given to receive its <c>Bye</c> before the connection is closed.</summary>
        public const long KickDelayUs = 250000;

        /// <summary>
        /// Tells a client why it must go (<c>Bye</c>), stops listening to it, and closes its connection a moment
        /// later, so that the reason arrives before the disconnect does.
        /// </summary>
        public void Kick(int peerId, string reason)
        {
            byte[] bye = new ByeMessage(reason).ToBytes();
            _transport.Send(peerId, bye, 0, bye.Length, LinkDelivery.Reliable);
            _transport.Flush();
            Peer peer;
            if (_peers.TryGetValue(peerId, out peer))
            {
                if (_phone == peerId) _phone = -1;
                peer.Role = LinkRole.Unknown;
            }
            _kicks.Add(new PendingKick { PeerId = peerId, AtUs = _clock.NowUs + KickDelayUs });
        }

        // ---- Welcome ----------------------------------------------------------------------------------

        // Everything a Welcome says about the session, as one string to compare.
        string WelcomeStamp()
        {
            return string.Concat(SessionId, "\n", WorldHash, "\n", RoomId, "\n") + ForgeUrl;
        }

        void SendWelcome(Peer peer, string stamp)
        {
            peer.WelcomeStamp = stamp;
            SendTo(peer.Id, new WelcomeMessage
            {
                SessionId = SessionId, WorldHash = WorldHash, RoomId = RoomId, DemoLock = DemoLock, ForgeUrl = ForgeUrl
            });
        }

        /// <summary>
        /// Sends <c>Welcome</c> again to every Spectator and Editor whose last one no longer says what
        /// <see cref="SessionId"/>, <see cref="WorldHash"/>, <see cref="RoomId"/> and <see cref="ForgeUrl"/> hold
        /// now, so a Spectator that joined before the phone, or before the phone entered a room, learns the
        /// source's session and world. The Hub calls it by itself after every <c>Hello</c> of the phone; an owner
        /// that changes those fields at another time calls it then. Returns how many were sent.
        /// </summary>
        public int SendWelcomeToWatchers()
        {
            string stamp = null;
            int sent = 0;
            for (int i = 0; i < _peerList.Count; i++)
            {
                Peer peer = _peerList[i];
                if (!LinkRoles.Watches(peer.Role)) continue;
                if (stamp == null) stamp = WelcomeStamp();
                if (string.Equals(peer.WelcomeStamp, stamp, StringComparison.Ordinal)) continue;
                SendWelcome(peer, stamp);
                sent++;
            }
            if (sent > 0) _transport.Flush();
            return sent;
        }

        // ---- parameter snapshot -----------------------------------------------------------------------

        /// <summary>The last known value of every parameter, as a preset <c>{"values": {name: value}}</c>.</summary>
        public JsonValue SnapshotToJson()
        {
            JsonValue values = JsonValue.Object();
            foreach (KeyValuePair<string, float> kv in _snapshot) values.Set(kv.Key, kv.Value);
            return JsonValue.Object().Set("values", values);
        }

        /// <summary>Last known value of a parameter by name.</summary>
        public bool TryGetSnapshotValue(string name, out float value) { return _snapshot.TryGetValue(name, out value); }

        /// <summary>Forgets the snapshot (a new session): the next phone keeps the values it arrives with.</summary>
        public void ClearSnapshot() { _snapshot.Clear(); }

        // The phone's manifest with the value each parameter holds now, for a client that joins in mid-session.
        ParamManifestMessage CurrentManifest()
        {
            var current = new ParamManifestMessage();
            for (int i = 0; i < Manifest.Params.Count; i++)
            {
                ParamManifestEntry e = Manifest.Params[i];
                float known;
                if (e.Name != null && _snapshot.TryGetValue(e.Name, out known)) e.Val = known;
                current.Params.Add(e);
            }
            return current;
        }

        bool TryGetParamId(string name, out ushort id)
        {
            id = 0;
            if (Manifest == null || name == null) return false;
            for (int i = 0; i < Manifest.Params.Count; i++)
            {
                if (!string.Equals(Manifest.Params[i].Name, name, StringComparison.Ordinal)) continue;
                id = Manifest.Params[i].Id;
                return true;
            }
            return false;
        }

        void NoteParamSet(ParamSetMessage set)
        {
            if (Manifest == null) return;
            for (int i = 0; i < set.Count; i++)
            {
                for (int k = 0; k < Manifest.Params.Count; k++)
                {
                    ParamManifestEntry e = Manifest.Params[k];
                    if (e.Id != set.Ids[i]) continue;
                    // Record what the phone will hold after its clamp, not what was asked for.
                    float v = set.Values[i];
                    if (!float.IsNaN(v)) _snapshot[e.Name] = v < e.Min ? e.Min : v > e.Max ? e.Max : v;
                    break;
                }
            }
        }

        void OnManifest(Peer peer, ParamManifestMessage manifest)
        {
            Manifest = manifest;
            if (peer.AwaitingFirstManifest)
            {
                // RESYNC: the phone has just (re)connected. Whatever it holds that differs from the snapshot is
                // put back, so a phone that restarted with defaults returns to the tuned values (L12).
                peer.AwaitingFirstManifest = false;
                _paramSetOut.Clear();
                for (int i = 0; i < manifest.Params.Count; i++)
                {
                    ParamManifestEntry e = manifest.Params[i];
                    float known;
                    if (!_snapshot.TryGetValue(e.Name, out known) || known == e.Val) continue;
                    if (!_paramSetOut.Add(e.Id, known))
                    {
                        FlushReplay(peer.Id);
                        _paramSetOut.Add(e.Id, known);
                    }
                }
                FlushReplay(peer.Id);
                for (int i = 0; i < manifest.Params.Count; i++)
                    if (!_snapshot.ContainsKey(manifest.Params[i].Name)) _snapshot[manifest.Params[i].Name] = manifest.Params[i].Val;

                for (int i = 0; i < _unackedAssets.Count; i++)
                {
                    byte[] bytes = _unackedAssets[i].ToBytes();
                    _transport.Send(peer.Id, bytes, 0, bytes.Length, LinkDelivery.Reliable);
                }
            }
            else
            {
                // A manifest in mid-session (new declarations): the phone is the authority on its own values.
                for (int i = 0; i < manifest.Params.Count; i++) _snapshot[manifest.Params[i].Name] = manifest.Params[i].Val;
            }
        }

        void FlushReplay(int peerId)
        {
            if (_paramSetOut.Count == 0) return;
            int n = _paramSetOut.WriteTo(_largeBuffer, 0);
            _transport.Send(peerId, _largeBuffer, 0, n, LinkDelivery.Reliable);
            _paramSetOut.Clear();
        }

        // ---- relaying ---------------------------------------------------------------------------------

        void SendToWatchers(byte[] data, int offset, int length, LinkDelivery delivery)
        {
            for (int i = 0; i < _peerList.Count; i++)
                if (LinkRoles.Watches(_peerList[i].Role)) _transport.Send(_peerList[i].Id, data, offset, length, delivery);
        }

        // The manifest goes to everyone who shows or mirrors the phone's parameters: Consoles and Editors build
        // sliders from it, Spectators bring their own registry in step with it. An Editor is both and gets it once.
        void SendToOperatorsAndWatchers(byte[] data, int offset, int length)
        {
            for (int i = 0; i < _peerList.Count; i++)
                if (LinkRoles.Operates(_peerList[i].Role) || LinkRoles.Watches(_peerList[i].Role))
                    _transport.Send(_peerList[i].Id, data, offset, length, LinkDelivery.Reliable);
        }

        void SendToPhoneAndWatchers(byte[] data, int length)
        {
            if (_phone >= 0) _transport.Send(_phone, data, 0, length, LinkDelivery.Reliable);
            SendToWatchers(data, 0, length, LinkDelivery.Reliable);
        }

        // ---- ILinkTransportListener -------------------------------------------------------------------

        void ILinkTransportListener.OnPeerConnected(int peerId)
        {
            // Nothing yet: a client exists for the Hub once it has said Hello.
        }

        void ILinkTransportListener.OnPeerDisconnected(int peerId, string reason)
        {
            Peer peer;
            if (!_peers.TryGetValue(peerId, out peer)) return;
            _peers.Remove(peerId);
            _peerList.Remove(peer);
            if (_phone == peerId) _phone = -1;
            for (int i = _kicks.Count - 1; i >= 0; i--)
                if (_kicks[i].PeerId == peerId) _kicks.RemoveAt(i);
            LinkPeerLeftHandler handler = PeerLeft;
            if (handler != null) handler(peerId, peer.Role, reason);
        }

        void ILinkTransportListener.OnReceive(int peerId, byte[] data, int offset, int length, LinkDelivery delivery)
        {
            if (length < 1) return;
            byte id = data[offset];

            // The clock exchange needs no Hello, so a client can measure the link at once.
            if (id == MessageId.TPing)
            {
                var ping = new TPingMessage();
                if (!ping.ReadFrom(data, offset, length)) return;
                long t2 = _clock.NowUs;
                TPongMessage pong = ClockSync.Answer(ping, t2, _clock.NowUs);
                int n = pong.WriteTo(_smallBuffer, 0);
                _transport.Send(peerId, _smallBuffer, 0, n, LinkDelivery.Unreliable);
                _transport.Flush();
                return;
            }
            if (id == MessageId.Hello)
            {
                OnHello(peerId, data, offset, length);
                return;
            }

            Peer peer;
            if (!_peers.TryGetValue(peerId, out peer)) return; // no Hello yet: say nothing, relay nothing

            switch (id)
            {
                case MessageId.State:
                    if (peer.Role != LinkRole.Phone || !_stateIn.ReadFrom(data, offset, length)) break;
                    // The same 202 bytes go on to the watchers, so seq and t_us stay the phone's.
                    SendToWatchers(data, offset, StateMessage.Size, LinkDelivery.Sequenced);
                    _transport.Flush();
                    {
                        LinkHostStateHandler handler = StateReceived;
                        if (handler != null) handler(peerId, in _stateIn, data, offset, StateMessage.Size);
                    }
                    break;

                case MessageId.Telemetry:
                    if (peer.Role == LinkRole.Phone && _telemetryIn.ReadFrom(data, offset, length))
                    {
                        LinkHostTelemetryHandler handler = TelemetryReceived;
                        if (handler != null) handler(peerId, in _telemetryIn);
                    }
                    break;

                case MessageId.RoomOutline:
                    if (peer.Role != LinkRole.Phone || !_outlineIn.ReadFrom(data, offset, length)) break;
                    {
                        // Kept so that a Spectator that joins later can still draw the room.
                        int size = _outlineIn.Size;
                        if (_lastOutline == null || _lastOutline.Length != size) _lastOutline = new byte[size];
                        Buffer.BlockCopy(data, offset, _lastOutline, 0, size);
                        SendToWatchers(_lastOutline, 0, size, LinkDelivery.Reliable);
                        Action<int, RoomOutlineMessage> handler = RoomOutlineReceived;
                        if (handler != null) handler(peerId, _outlineIn);
                    }
                    break;

                case MessageId.ParamSet:
                    if (!LinkRoles.Operates(peer.Role) || !_paramSetIn.ReadFrom(data, offset, length)) break;
                    SendParamSet(_paramSetIn);
                    {
                        Action<int, ParamSetMessage> handler = ParamSetReceived;
                        if (handler != null) handler(peerId, _paramSetIn);
                    }
                    break;

                case MessageId.ParamManifest:
                    if (peer.Role != LinkRole.Phone) break;
                    {
                        var manifest = new ParamManifestMessage();
                        if (!manifest.ReadFrom(data, offset, length)) break;
                        OnManifest(peer, manifest);
                        SendToOperatorsAndWatchers(data, offset, length);
                        Action<int, ParamManifestMessage> handler = ManifestReceived;
                        if (handler != null) handler(peerId, manifest);
                        RaiseJson(peerId, data, offset, length);
                    }
                    break;

                case MessageId.SceneCmd:
                case MessageId.OperatorCmd:
                    if (!LinkRoles.Operates(peer.Role)) break;
                    if (id == MessageId.OperatorCmd)
                    {
                        var cmd = new OperatorCmdMessage();
                        OperatorCommand command;
                        if (cmd.ReadFrom(data, offset, length) && OperatorCommand.TryFrom(cmd, out command) &&
                            command.Kind == OperatorCommandKind.Lock) DemoLock = command.On;
                    }
                    if (_phone >= 0) _transport.Send(_phone, data, offset, length, LinkDelivery.Reliable);
                    RaiseJson(peerId, data, offset, length);
                    break;

                case MessageId.Event:
                    if (peer.Role != LinkRole.Phone) break;
                    {
                        byte jsonId;
                        JsonValue body;
                        if (!JsonMessage.TryDecode(data, offset, length, out jsonId, out body)) break;
                        // The phone's own changes (on-device menu, clamped remote values) keep the snapshot true.
                        if (body.GetString("ev") == LinkEvents.Param)
                        {
                            JsonValue d = body["data"];
                            string name = d.GetString("name");
                            if (name != null && d["value"].IsNumber) _snapshot[name] = d["value"].AsFloat();
                        }
                        // Spectators mirror the phone from its events: parameter changes, world, objects, assets.
                        SendToWatchers(data, offset, length, LinkDelivery.Reliable);
                        if (body.GetString("ev") == LinkEvents.World)
                        {
                            if (_lastWorldEvent == null || _lastWorldEvent.Length != length) _lastWorldEvent = new byte[length];
                            Buffer.BlockCopy(data, offset, _lastWorldEvent, 0, length);
                        }
                        LinkHostJsonHandler handler = JsonReceived;
                        if (handler != null) handler(peerId, jsonId, body);
                    }
                    break;

                case MessageId.AssetAck:
                    if (peer.Role != LinkRole.Phone) break;
                    {
                        var ack = new AssetAckMessage();
                        if (ack.ReadFrom(data, offset, length))
                        {
                            for (int i = _unackedAssets.Count - 1; i >= 0; i--)
                                if (_unackedAssets[i].JobId == ack.JobId) _unackedAssets.RemoveAt(i);
                        }
                        RaiseJson(peerId, data, offset, length);
                    }
                    break;

                case MessageId.Log:
                case MessageId.Bye:
                    RaiseJson(peerId, data, offset, length);
                    break;

                case MessageId.TPong:
                {
                    var pong = new TPongMessage();
                    if (!pong.ReadFrom(data, offset, length)) break;
                    long t4 = _clock.NowUs;
                    if (!peer.Clock.OnPong(pong, t4)) break;
                    LinkHostPongHandler handler = TPongReceived;
                    if (handler != null) handler(peerId, in pong, t4);
                    break;
                }

                // Anything else, the reserved PeerState among it, is ignored (section 7.5, rule 5).
            }
        }

        void RaiseJson(int peerId, byte[] data, int offset, int length)
        {
            LinkHostJsonHandler handler = JsonReceived;
            if (handler == null) return;
            byte id;
            JsonValue body;
            if (JsonMessage.TryDecode(data, offset, length, out id, out body)) handler(peerId, id, body);
        }

        void OnHello(int peerId, byte[] data, int offset, int length)
        {
            var hello = new HelloMessage();
            if (!hello.ReadFrom(data, offset, length)) return;

            if (hello.ProtoMajor != LinkProtocol.ProtoMajor)
            {
                // Refused with a readable reason (section 7.5, rule 5).
                Kick(peerId, "protocol mismatch: the Hub speaks " + LinkProtocol.ProtoMajor + "." + LinkProtocol.ProtoMinor +
                             ", the client sent " + hello.ProtoMajor + "." + hello.ProtoMinor);
                return;
            }

            if (hello.Role == LinkRole.Phone && _phone >= 0 && _phone != peerId)
            {
                // One phone at a time, and the one in the seat keeps it: two phones that each replaced the other
                // would reconnect in turns for ever. The newcomer retries with back-off and gets in once the seat
                // is free (a phone that changed address keeps its peer, so this does not lock out a roamer).
                Kick(peerId, "another phone is already connected");
                return;
            }

            Peer peer;
            bool isNew = !_peers.TryGetValue(peerId, out peer);
            if (isNew)
            {
                peer = new Peer { Id = peerId };
                _peers.Add(peerId, peer);
                _peerList.Add(peer);
                peer.Clock.Restart(_clock.NowUs);
            }
            peer.Role = hello.Role;
            peer.Hello = hello;

            if (hello.Role == LinkRole.Phone)
            {
                _phone = peerId;
                // RESYNC belongs to a new connection. A Hello said again in mid-session (a new session id, a new
                // room) changes what Welcome carries and nothing else.
                if (isNew) peer.AwaitingFirstManifest = true;
            }

            // The owner decides what the Welcome carries: the Hub adopts the phone's session and world here.
            LinkPeerJoinedHandler said = HelloReceived;
            if (said != null) said(peerId, hello);

            SendWelcome(peer, WelcomeStamp());
            if (hello.Role == LinkRole.Phone) SendWelcomeToWatchers();

            if (LinkRoles.Watches(hello.Role) && _lastOutline != null)
                _transport.Send(peerId, _lastOutline, 0, _lastOutline.Length, LinkDelivery.Reliable);
            // With the values of this moment, not those of the phone's handshake: a late joiner starts in step.
            if ((LinkRoles.Operates(hello.Role) || LinkRoles.Watches(hello.Role)) && Manifest != null) SendTo(peerId, CurrentManifest());
            if (LinkRoles.Watches(hello.Role))
            {
                if (_lastWorldEvent != null) _transport.Send(peerId, _lastWorldEvent, 0, _lastWorldEvent.Length, LinkDelivery.Reliable);
                for (int i = 0; i < _unackedAssets.Count; i++)
                {
                    byte[] bytes = _unackedAssets[i].ToBytes();
                    _transport.Send(peerId, bytes, 0, bytes.Length, LinkDelivery.Reliable);
                }
            }
            _transport.Flush();

            if (isNew)
            {
                LinkPeerJoinedHandler handler = PeerJoined;
                if (handler != null) handler(peerId, hello);
            }
            RaiseJson(peerId, data, offset, length);
        }
    }
}
