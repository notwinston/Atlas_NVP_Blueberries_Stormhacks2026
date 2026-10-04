using System;
using System.Collections.Generic;

namespace Atlas.Link
{
    /// <summary>
    /// An in-memory network for tests and offline tools: any number of <see cref="LoopbackTransport"/> endpoints,
    /// with configurable loss, delay and jitter, driven by an injected clock. It mimics what the link relies on
    /// from LiteNetLib: reliable packets always arrive and in order, sequenced packets may be lost but never
    /// arrive out of order, unreliable packets may be lost or reordered, and a silent peer is dropped after the
    /// disconnect timeout. Single-threaded; steady-state sends allocate nothing.
    /// </summary>
    public sealed class LoopbackNetwork
    {
        enum PacketKind : byte { Data, ConnectRequest, ConnectAccept, ConnectFailed, Disconnect }

        sealed class Packet
        {
            public PacketKind Kind;
            public LoopbackTransport To;
            public int ToPeerId;            // the peer id under which the receiver knows the sender
            public Connection Link;
            public bool FromA;
            public long DeliverAtUs;
            public long Order;
            public LinkDelivery Delivery;
            public uint Sequence;
            public byte[] Data = new byte[256];
            public int Length;
            public string Reason;
        }

        internal sealed class Connection
        {
            public LoopbackTransport A, B;  // A dialled, B listens
            public int IdAtA, IdAtB;        // A knows B as IdAtA; B knows A as IdAtB
            public bool Open;
            public long LastReliableAtoB, LastReliableBtoA;
            public uint SeqAtoB, SeqBtoA, SeenAtoB, SeenBtoA;
            public bool AnySeenAtoB, AnySeenBtoA;
        }

        readonly ILinkClock _clock;
        readonly List<Packet> _inFlight = new List<Packet>(64);     // sorted by (DeliverAtUs, Order)
        readonly Stack<Packet> _pool = new Stack<Packet>();
        readonly List<LoopbackTransport> _endpoints = new List<LoopbackTransport>();
        readonly List<Connection> _connections = new List<Connection>();
        uint _rng;
        long _order;
        bool _cut;
        long _cutSinceUs;

        /// <summary>Chance 0..1 that a sequenced or unreliable packet is lost. Reliable packets are never lost.</summary>
        public double Loss;
        /// <summary>One-way delay in milliseconds.</summary>
        public double DelayMs;
        /// <summary>Extra one-way delay, uniform in 0..JitterMs, drawn per packet.</summary>
        public double JitterMs;
        /// <summary>Silence after which both ends of a connection are told the peer timed out.</summary>
        public double DisconnectTimeoutMs = 3000.0;
        /// <summary>How long a connect attempt to nobody takes to fail.</summary>
        public double ConnectFailMs = 500.0;

        public long PacketsSent { get; private set; }
        public long PacketsLost { get; private set; }

        public LoopbackNetwork(ILinkClock clock, int seed = 1)
        {
            if (clock == null) throw new ArgumentNullException("clock");
            _clock = clock;
            _rng = (uint)seed * 2654435761u + 1u;
            // Enough packets for any steady exchange, so that a measured run never grows the pool.
            for (int i = 0; i < 64; i++) _pool.Push(new Packet());
        }

        internal ILinkClock Clock { get { return _clock; } }

        /// <summary>
        /// While true the cable is pulled: nothing is delivered in either direction, and after
        /// <see cref="DisconnectTimeoutMs"/> every connection times out.
        /// </summary>
        public bool Cut
        {
            get { return _cut; }
            set
            {
                if (_cut == value) return;
                _cut = value;
                _cutSinceUs = _clock.NowUs;
            }
        }

        /// <summary>A new endpoint on this network.</summary>
        public LoopbackTransport CreateEndpoint()
        {
            var endpoint = new LoopbackTransport(this);
            _endpoints.Add(endpoint);
            return endpoint;
        }

        // xorshift32: small, deterministic and the same on every runtime.
        double NextUnit()
        {
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 16777216.0;
        }

        long TransitUs() { return (long)((DelayMs + JitterMs * NextUnit()) * 1000.0); }

        Packet Rent()
        {
            Packet p = _pool.Count > 0 ? _pool.Pop() : new Packet();
            p.Order = _order++;
            p.Reason = null;
            p.Link = null;
            p.Length = 0;
            return p;
        }

        void Enqueue(Packet p)
        {
            int i = _inFlight.Count;
            while (i > 0 && _inFlight[i - 1].DeliverAtUs > p.DeliverAtUs) i--;
            _inFlight.Insert(i, p);
        }

        internal float SilenceMs(LoopbackTransport endpoint, int peerId)
        {
            if (FindConnection(endpoint, peerId) == null) return -1f;
            // Stands in for LiteNetLib's keep-alives: an intact link is never silent, a cut one is silent since the cut.
            return _cut ? (_clock.NowUs - _cutSinceUs) / 1000f : 0f;
        }

        Connection FindConnection(LoopbackTransport endpoint, int peerId)
        {
            for (int i = 0; i < _connections.Count; i++)
            {
                Connection c = _connections[i];
                if (!c.Open) continue;
                if ((c.A == endpoint && c.IdAtA == peerId) || (c.B == endpoint && c.IdAtB == peerId)) return c;
            }
            return null;
        }

        internal void Connect(LoopbackTransport from, int port)
        {
            long now = _clock.NowUs;
            LoopbackTransport host = null;
            for (int i = 0; i < _endpoints.Count; i++)
                if (_endpoints[i].IsRunning && _endpoints[i].IsHost && _endpoints[i].LocalPort == port) host = _endpoints[i];

            if (host == null || _cut)
            {
                Packet fail = Rent();
                fail.Kind = PacketKind.ConnectFailed;
                fail.To = from;
                fail.ToPeerId = from.NextPeerId();
                fail.DeliverAtUs = now + (long)(ConnectFailMs * 1000.0);
                fail.Reason = "ConnectionFailed";
                Enqueue(fail);
                return;
            }

            var c = new Connection { A = from, B = host, IdAtA = from.NextPeerId(), IdAtB = host.NextPeerId() };
            _connections.Add(c);
            Packet request = Rent();
            request.Kind = PacketKind.ConnectRequest;
            request.To = host;
            request.ToPeerId = c.IdAtB;
            request.Link = c;
            request.DeliverAtUs = now + TransitUs();
            Enqueue(request);
        }

        internal bool Send(LoopbackTransport from, int peerId, byte[] data, int offset, int length, LinkDelivery delivery)
        {
            Connection c = FindConnection(from, peerId);
            if (c == null || !Wire.InRange(data, offset, length)) return false;
            PacketsSent++;
            bool fromA = c.A == from;

            // The sequence number is taken even for a packet that is then lost, as on a real wire.
            uint sequence = 0;
            if (delivery == LinkDelivery.Sequenced) sequence = fromA ? ++c.SeqAtoB : ++c.SeqBtoA;

            if (_cut || (delivery != LinkDelivery.Reliable && Loss > 0.0 && NextUnit() < Loss))
            {
                PacketsLost++;
                return true; // lost on the wire; the sender cannot tell
            }

            Packet p = Rent();
            p.Kind = PacketKind.Data;
            p.To = fromA ? c.B : c.A;
            p.ToPeerId = fromA ? c.IdAtB : c.IdAtA;
            p.Link = c;
            p.FromA = fromA;
            p.Delivery = delivery;
            p.Sequence = sequence;
            if (p.Data.Length < length) p.Data = new byte[Math.Max(length, p.Data.Length * 2)];
            Buffer.BlockCopy(data, offset, p.Data, 0, length);
            p.Length = length;

            long at = _clock.NowUs + TransitUs();
            if (delivery == LinkDelivery.Reliable)
            {
                // Reliable-ordered: a packet never overtakes the one sent before it.
                if (fromA) { if (at < c.LastReliableAtoB) at = c.LastReliableAtoB; c.LastReliableAtoB = at; }
                else { if (at < c.LastReliableBtoA) at = c.LastReliableBtoA; c.LastReliableBtoA = at; }
            }
            p.DeliverAtUs = at;
            Enqueue(p);
            return true;
        }

        // A close that carries a last packet: the packet travels as a reliable one and the disconnect never
        // overtakes it.
        internal void Disconnect(LoopbackTransport from, int peerId, byte[] data, int offset, int length)
        {
            Connection c = FindConnection(from, peerId);
            if (c == null) return;
            Send(from, peerId, data, offset, length, LinkDelivery.Reliable);
            Disconnect(from, peerId, "DisconnectPeerCalled", "RemoteConnectionClose", c.A == from ? c.LastReliableAtoB : c.LastReliableBtoA);
        }

        internal void Disconnect(LoopbackTransport from, int peerId, string localReason, string remoteReason, long notBeforeUs = long.MinValue)
        {
            Connection c = FindConnection(from, peerId);
            if (c == null) return;
            c.Open = false;
            _connections.Remove(c);
            long now = _clock.NowUs;
            bool fromA = c.A == from;

            if (from.IsRunning)
            {
                Packet local = Rent();
                local.Kind = PacketKind.Disconnect;
                local.To = from;
                local.ToPeerId = peerId;
                local.DeliverAtUs = now;
                local.Reason = localReason;
                Enqueue(local);
            }

            Packet remote = Rent();
            remote.Kind = PacketKind.Disconnect;
            remote.To = fromA ? c.B : c.A;
            remote.ToPeerId = fromA ? c.IdAtB : c.IdAtA;
            // A pulled cable carries no goodbye: the far end notices at its own timeout.
            remote.DeliverAtUs = now + (_cut ? (long)(DisconnectTimeoutMs * 1000.0) : TransitUs());
            if (remote.DeliverAtUs < notBeforeUs) remote.DeliverAtUs = notBeforeUs;
            remote.Reason = _cut ? "Timeout" : remoteReason;
            Enqueue(remote);
        }

        internal void DisconnectAll(LoopbackTransport endpoint)
        {
            for (int i = _connections.Count - 1; i >= 0; i--)
            {
                Connection c = _connections[i];
                if (c.A == endpoint) Disconnect(endpoint, c.IdAtA, "Stopped", "RemoteConnectionClose");
                else if (c.B == endpoint) Disconnect(endpoint, c.IdAtB, "Stopped", "RemoteConnectionClose");
            }
        }

        internal void Poll(LoopbackTransport endpoint)
        {
            long now = _clock.NowUs;

            if (_cut && now - _cutSinceUs >= (long)(DisconnectTimeoutMs * 1000.0))
            {
                for (int i = _connections.Count - 1; i >= 0; i--)
                {
                    Connection c = _connections[i];
                    bool wasOpen = c.Open;
                    c.Open = false;
                    _connections.RemoveAt(i);
                    QueueTimeout(c.A, c.IdAtA, now);
                    if (wasOpen) QueueTimeout(c.B, c.IdAtB, now); // the listener never saw a half-made connection
                }
            }

            // One packet at a time: a listener may send from inside its callback, which edits the queue.
            while (true)
            {
                Packet p = null;
                for (int i = 0; i < _inFlight.Count; i++)
                {
                    if (_inFlight[i].DeliverAtUs > now) break;
                    if (_inFlight[i].To != endpoint) continue;
                    p = _inFlight[i];
                    _inFlight.RemoveAt(i);
                    break;
                }
                if (p == null) break;
                Deliver(endpoint, p);
                _pool.Push(p);
            }
        }

        void QueueTimeout(LoopbackTransport to, int peerId, long now)
        {
            Packet p = Rent();
            p.Kind = PacketKind.Disconnect;
            p.To = to;
            p.ToPeerId = peerId;
            p.DeliverAtUs = now;
            p.Reason = "Timeout";
            Enqueue(p);
        }

        void Deliver(LoopbackTransport endpoint, Packet p)
        {
            ILinkTransportListener listener = endpoint.Listener;
            if (!endpoint.IsRunning) return;
            switch (p.Kind)
            {
                case PacketKind.ConnectRequest:
                {
                    Connection c = p.Link;
                    if (_cut || !_connections.Contains(c) || !c.A.IsRunning)
                    {
                        _connections.Remove(c);
                        break;
                    }
                    c.Open = true;
                    Packet accept = Rent();
                    accept.Kind = PacketKind.ConnectAccept;
                    accept.To = c.A;
                    accept.ToPeerId = c.IdAtA;
                    accept.Link = c;
                    accept.DeliverAtUs = _clock.NowUs + TransitUs();
                    Enqueue(accept);
                    if (listener != null) listener.OnPeerConnected(c.IdAtB);
                    break;
                }
                case PacketKind.ConnectAccept:
                    if (p.Link.Open && listener != null) listener.OnPeerConnected(p.ToPeerId);
                    break;
                case PacketKind.ConnectFailed:
                case PacketKind.Disconnect:
                    if (listener != null) listener.OnPeerDisconnected(p.ToPeerId, p.Reason);
                    break;
                default:
                {
                    // A packet sent before a disconnect still arrives, as a goodbye does on a real link.
                    Connection c = p.Link;
                    if (_cut || c == null) break;
                    if (p.Delivery == LinkDelivery.Sequenced)
                    {
                        // A sequenced packet older than one already delivered is dropped.
                        if (p.FromA)
                        {
                            if (c.AnySeenAtoB && p.Sequence <= c.SeenAtoB) break;
                            c.SeenAtoB = p.Sequence;
                            c.AnySeenAtoB = true;
                        }
                        else
                        {
                            if (c.AnySeenBtoA && p.Sequence <= c.SeenBtoA) break;
                            c.SeenBtoA = p.Sequence;
                            c.AnySeenBtoA = true;
                        }
                    }
                    if (listener != null)
                    {
                        // The stamp a receive thread would have taken: the packet arrived at DeliverAtUs and has
                        // waited for this poll since.
                        long waitedUs = _clock.NowUs - p.DeliverAtUs;
                        endpoint.LastReceiveUs = endpoint.StampClock.NowUs - (waitedUs > 0 ? waitedUs : 0);
                        listener.OnReceive(p.ToPeerId, p.Data, 0, p.Length, p.Delivery);
                    }
                    break;
                }
            }
        }
    }

    /// <summary>One endpoint of a <see cref="LoopbackNetwork"/>. Create it with <see cref="LoopbackNetwork.CreateEndpoint"/>.</summary>
    public sealed class LoopbackTransport : ILinkTransport, ILinkPeerAddresses, ILinkReceiveStamps
    {
        /// <summary>What <see cref="GetPeerAddress"/> answers for every connected peer: the network is in memory.</summary>
        public const string PeerAddress = "loopback";

        static int s_nextFreePort = 50000;

        readonly LoopbackNetwork _network;
        int _nextPeerId;

        internal LoopbackTransport(LoopbackNetwork network)
        {
            _network = network;
            StampClock = network.Clock;
        }

        // The listener's clock, which a test may have shifted against the network's.
        internal ILinkClock StampClock { get; private set; }

        public long LastReceiveUs { get; internal set; }

        public void StampWith(ILinkClock clock) { StampClock = clock ?? _network.Clock; }

        public ILinkTransportListener Listener { get; set; }
        public bool IsRunning { get; private set; }
        public int LocalPort { get; private set; }
        internal bool IsHost { get; private set; }

        internal int NextPeerId() { return _nextPeerId++; }

        public bool StartClient()
        {
            if (IsRunning) return !IsHost;
            IsRunning = true;
            IsHost = false;
            LocalPort = System.Threading.Interlocked.Increment(ref s_nextFreePort);
            return true;
        }

        public bool StartHost(int port)
        {
            if (IsRunning) return IsHost;
            IsRunning = true;
            IsHost = true;
            LocalPort = port > 0 ? port : System.Threading.Interlocked.Increment(ref s_nextFreePort);
            return true;
        }

        public void Connect(string address, int port)
        {
            if (IsRunning) _network.Connect(this, port);
        }

        public void Poll()
        {
            if (IsRunning) _network.Poll(this);
        }

        public bool Send(int peerId, byte[] data, int offset, int length, LinkDelivery delivery)
        {
            return IsRunning && _network.Send(this, peerId, data, offset, length, delivery);
        }

        public void Flush() { }

        public void Disconnect(int peerId)
        {
            if (IsRunning) _network.Disconnect(this, peerId, "DisconnectPeerCalled", "RemoteConnectionClose");
        }

        public void Disconnect(int peerId, byte[] data, int offset, int length)
        {
            if (IsRunning) _network.Disconnect(this, peerId, data, offset, length);
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;
            _network.DisconnectAll(this);
            LocalPort = 0;
        }

        public int GetRttMs(int peerId) { return (int)(2.0 * _network.DelayMs + _network.JitterMs); }

        public float GetSilenceMs(int peerId) { return IsRunning ? _network.SilenceMs(this, peerId) : -1f; }

        public int GetLossPermille(int peerId) { return (int)(_network.Loss * 1000.0); }

        public string GetPeerAddress(int peerId) { return GetSilenceMs(peerId) >= 0f ? PeerAddress : null; }
    }
}
