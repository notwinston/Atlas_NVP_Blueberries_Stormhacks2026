using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Atlas.Link
{
    /// <summary>
    /// The link over LiteNetLib 2.1.4 with the settings of Master Plan section 7.2: two channels, ping every
    /// 500 ms, disconnect after 3 s, 5 ms logic tick, IPv4 only, unicast to a known address, connection key
    /// <c>atlas-v1</c>. Sockets run on LiteNetLib's own threads; events are handed over inside <see cref="Poll"/>.
    /// A packet is stamped and copied on the receive thread the moment it arrives (<see cref="ILinkReceiveStamps"/>)
    /// and waits in a queue for the poll, so the listener still hears everything on its own thread.
    /// </summary>
    public sealed class LiteNetTransport : ILinkTransport, ILinkPeerAddresses, ILinkReceiveStamps, INetEventListener
    {
        /// <summary>Milliseconds between the two connect requests of one attempt, and from the second to giving up.</summary>
        public const int ClientReconnectDelayMs = 250;

        // The connection key as LiteNetLib puts it on the wire, made once: connecting with the key as a string
        // would build a writer and its buffer for every attempt.
        static readonly byte[] s_keyBytes = NetDataWriter.FromString(LinkProtocol.ConnectionKey).CopyData();

        // One State with LiteNetLib's header to spare; a larger packet (a fragmented manifest) gets its own size.
        const int ArrivalBufferSize = 2048;
        const int ArrivalPoolMax = 256;

        // A packet between the receive thread and the poll.
        struct Arrival
        {
            public NetPeer Peer;            // null once delivered
            public byte[] Data;
            public int Length;
            public LinkDelivery Delivery;
            public long StampUs;
        }

        readonly NetManager _net;
        readonly Dictionary<int, NetPeer> _peers = new Dictionary<int, NetPeer>();
        bool _isHost;

        // Guards _arrived and _arrivalPool, the only state the receive thread shares with the poll. Held for a
        // copy or a swap, never across a call into LiteNetLib or the listener.
        readonly object _arrivalLock = new object();
        readonly List<Arrival> _arrived = new List<Arrival>(64);
        readonly Stack<byte[]> _arrivalPool = new Stack<byte[]>(64);
        readonly List<Arrival> _delivering = new List<Arrival>(64);     // poll thread only
        ILinkClock _stampClock = new StopwatchClock();

        // The Hub's end point, parsed once per (address, port). With the Station away the client asks to connect
        // again and again for the whole session; none of those calls may allocate (section 7.5, rule 6).
        string _connectAddress;
        int _connectPort;
        IPEndPoint _connectEndPoint;    // null when the address is not an IP literal

        public LiteNetTransport()
        {
            _net = new NetManager(this)
            {
                ChannelsCount = LinkProtocol.ChannelsCount,
                PingInterval = 500,
                DisconnectTimeout = 3000,
                UpdateTime = 5,
                AutoRecycle = true,
                EnableStatistics = true,
                IPv6Enabled = false,
                UnconnectedMessagesEnabled = false,
                BroadcastReceiveEnabled = false,
                // OnNetworkReceive runs on the receive thread, where the arrival is stamped. Every other event
                // still waits for PollEvents.
                UnsyncedReceiveEvent = true
            };
        }

        public long LastReceiveUs { get; private set; }

        public void StampWith(ILinkClock clock)
        {
            if (clock != null) _stampClock = clock;
        }

        public ILinkTransportListener Listener { get; set; }

        public bool IsRunning { get { return _net.IsRunning; } }

        public int LocalPort { get { return _net.IsRunning ? _net.LocalPort : 0; } }

        /// <summary>The LiteNetLib manager, for settings a tool wants to change before starting (loss simulation on the Hub).</summary>
        public NetManager Manager { get { return _net; } }

        public bool StartClient()
        {
            _isHost = false;
            _net.AllowPeerAddressChange = false;
            // One attempt is two requests 250 ms apart and has failed after 500 ms. The link's own back-off decides
            // when the next one starts, so the library's retries must not run underneath it for seconds.
            _net.ReconnectDelay = ClientReconnectDelayMs;
            _net.MaxConnectAttempts = 1;
            return _net.IsRunning || _net.Start();
        }

        public bool StartHost(int port)
        {
            _isHost = true;
            // A phone that roams between access points keeps its session (Master Plan section 7.2).
            _net.AllowPeerAddressChange = true;
            return _net.IsRunning || _net.Start(port);
        }

        public void Connect(string address, int port)
        {
            if (!_net.IsRunning || string.IsNullOrEmpty(address)) return;
            if (port != _connectPort || !string.Equals(address, _connectAddress, System.StringComparison.Ordinal))
            {
                // An IP literal only: a host name would resolve synchronously on this thread. Refusing one here
                // keeps the frame loop free of DNS (Master Plan section 7.5, rule 2).
                IPAddress ip;
                _connectEndPoint = IPAddress.TryParse(address, out ip) ? new IPEndPoint(ip, port) : null;
                _connectAddress = address;
                _connectPort = port;
            }
            if (_connectEndPoint == null)
            {
                ILinkTransportListener listener = Listener;
                if (listener != null) listener.OnPeerDisconnected(-1, "address is not an IP literal");
                return;
            }
            // While an attempt to this end point is still running, LiteNetLib hands back the peer it has.
            _net.Connect(_connectEndPoint, new System.ReadOnlySpan<byte>(s_keyBytes));
        }

        public void Poll()
        {
            if (!_net.IsRunning) return;
            // The arrivals are taken before the events: a peer's connect is raised before its first packet can
            // arrive, so every packet taken here belongs to a peer that PollEvents has announced by the time
            // it is delivered. A disconnect hands over its peer's packets first (OnPeerDisconnected).
            TakeArrivals(null);
            _net.PollEvents();
            DeliverArrivals(null);
            if (_delivering.Count == 0) return;
            lock (_arrivalLock)
            {
                for (int i = 0; i < _delivering.Count; i++)
                    if (_arrivalPool.Count < ArrivalPoolMax) _arrivalPool.Push(_delivering[i].Data);
            }
            _delivering.Clear();
        }

        // Moves what the receive thread has queued to the poll's list, in order: everything, or one peer's.
        void TakeArrivals(NetPeer only)
        {
            lock (_arrivalLock)
            {
                int kept = 0;
                for (int i = 0; i < _arrived.Count; i++)
                {
                    Arrival a = _arrived[i];
                    if (only == null || ReferenceEquals(a.Peer, only)) _delivering.Add(a);
                    else _arrived[kept++] = a;
                }
                _arrived.RemoveRange(kept, _arrived.Count - kept);
            }
        }

        // Hands the taken packets to the listener, oldest first: all of them, or one peer's. A listener may
        // stop the transport from inside the call, which empties the list under this loop.
        void DeliverArrivals(NetPeer only)
        {
            for (int i = 0; i < _delivering.Count; i++)
            {
                Arrival a = _delivering[i];
                if (a.Peer == null || (only != null && !ReferenceEquals(a.Peer, only))) continue;
                NetPeer peer = a.Peer;
                a.Peer = null;
                _delivering[i] = a;
                // A packet of a peer that has gone since (its id may be in use again) is dropped.
                NetPeer known;
                ILinkTransportListener listener = Listener;
                if (listener == null || !_peers.TryGetValue(peer.Id, out known) || !ReferenceEquals(known, peer)) continue;
                LastReceiveUs = a.StampUs;
                listener.OnReceive(peer.Id, a.Data, 0, a.Length, a.Delivery);
            }
        }

        public bool Send(int peerId, byte[] data, int offset, int length, LinkDelivery delivery)
        {
            NetPeer peer;
            if (!_peers.TryGetValue(peerId, out peer)) return false;
            switch (delivery)
            {
                case LinkDelivery.Sequenced:
                    if (length > peer.GetMaxSinglePacketSize(DeliveryMethod.Sequenced)) return false;
                    peer.Send(data, offset, length, LinkProtocol.ChannelState, DeliveryMethod.Sequenced);
                    return true;
                case LinkDelivery.Unreliable:
                    if (length > peer.GetMaxSinglePacketSize(DeliveryMethod.Unreliable)) return false;
                    peer.Send(data, offset, length, LinkProtocol.ChannelControl, DeliveryMethod.Unreliable);
                    return true;
                default:
                    peer.Send(data, offset, length, LinkProtocol.ChannelControl, DeliveryMethod.ReliableOrdered);
                    return true;
            }
        }

        public void Flush()
        {
            if (_net.IsRunning) _net.TriggerUpdate();
        }

        public void Disconnect(int peerId)
        {
            NetPeer peer;
            if (_peers.TryGetValue(peerId, out peer)) _net.DisconnectPeer(peer);
        }

        // LiteNetLib writes the bytes into the disconnect packet and puts it on the wire before this returns,
        // so the packet is out even when the socket is closed straight afterwards.
        public void Disconnect(int peerId, byte[] data, int offset, int length)
        {
            NetPeer peer;
            if (!_peers.TryGetValue(peerId, out peer)) return;
            if (Wire.InRange(data, offset, length) && length > 0) _net.DisconnectPeer(peer, data, offset, length);
            else _net.DisconnectPeer(peer);
        }

        public void Stop()
        {
            if (_net.IsRunning) _net.Stop(true);
            _peers.Clear();
            lock (_arrivalLock) _arrived.Clear();
            _delivering.Clear();
        }

        public int GetRttMs(int peerId)
        {
            NetPeer peer;
            return _peers.TryGetValue(peerId, out peer) ? peer.RoundTripTime : 0;
        }

        public float GetSilenceMs(int peerId)
        {
            NetPeer peer;
            return _peers.TryGetValue(peerId, out peer) ? peer.TimeSinceLastPacket : -1f;
        }

        public int GetLossPermille(int peerId)
        {
            NetPeer peer;
            if (!_peers.TryGetValue(peerId, out peer) || peer.Statistics == null) return 0;
            long sent = peer.Statistics.PacketsSent;
            return sent > 0 ? (int)(peer.Statistics.PacketLoss * 1000 / sent) : 0;
        }

        /// <summary>The peer's IP address as LiteNetLib holds it now (a roaming phone's changes with it).</summary>
        public string GetPeerAddress(int peerId)
        {
            NetPeer peer;
            return _peers.TryGetValue(peerId, out peer) && peer.Address != null ? peer.Address.ToString() : null;
        }

        // ---- INetEventListener ---------------------------------------------------------------------------

        void INetEventListener.OnPeerConnected(NetPeer peer)
        {
            _peers[peer.Id] = peer;
            ILinkTransportListener listener = Listener;
            if (listener != null) listener.OnPeerConnected(peer.Id);
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            // What the peer sent before it left is still in the queue: that first, in order.
            TakeArrivals(peer);
            DeliverArrivals(peer);
            ILinkTransportListener listener = Listener;
            // A peer that closed with a last packet (its Bye): that packet first, then the disconnect.
            NetPacketReader last = disconnectInfo.AdditionalData;
            if (listener != null && last != null && !last.IsNull && last.AvailableBytes > 0 && _peers.ContainsKey(peer.Id))
            {
                LastReceiveUs = _stampClock.NowUs;
                listener.OnReceive(peer.Id, last.RawData, last.Position, last.AvailableBytes, LinkDelivery.Reliable);
            }
            _peers.Remove(peer.Id);
            if (listener != null) listener.OnPeerDisconnected(peer.Id, ReasonText(disconnectInfo.Reason));
        }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError) { }

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            // On LiteNetLib's receive thread (UnsyncedReceiveEvent). The stamp comes first; the reader is
            // recycled when this returns, so the bytes are copied into a pooled buffer for the poll.
            long stampUs = _stampClock.NowUs;
            int length = reader.AvailableBytes;
            Arrival a;
            a.Peer = peer;
            a.Length = length;
            a.Delivery = deliveryMethod == DeliveryMethod.Unreliable ? LinkDelivery.Unreliable
                : deliveryMethod == DeliveryMethod.Sequenced ? LinkDelivery.Sequenced
                : LinkDelivery.Reliable;
            a.StampUs = stampUs;
            lock (_arrivalLock)
            {
                a.Data = _arrivalPool.Count > 0 ? _arrivalPool.Pop() : new byte[ArrivalBufferSize];
                if (a.Data.Length < length)
                {
                    _arrivalPool.Push(a.Data);
                    a.Data = new byte[length];
                }
                System.Buffer.BlockCopy(reader.RawData, reader.Position, a.Data, 0, length);
                _arrived.Add(a);
            }
        }

        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }

        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) { }

        void INetEventListener.OnConnectionRequest(ConnectionRequest request)
        {
            // Only the Hub listens. The phone, the Spectator and the Editors reject every request (section 7.1).
            if (_isHost) request.AcceptIfKey(LinkProtocol.ConnectionKey);
            else request.Reject();
        }

        // Constant strings, so a disconnect does not build one.
        static string ReasonText(DisconnectReason reason)
        {
            switch (reason)
            {
                case DisconnectReason.ConnectionFailed: return "ConnectionFailed";
                case DisconnectReason.Timeout: return "Timeout";
                case DisconnectReason.HostUnreachable: return "HostUnreachable";
                case DisconnectReason.NetworkUnreachable: return "NetworkUnreachable";
                case DisconnectReason.RemoteConnectionClose: return "RemoteConnectionClose";
                case DisconnectReason.DisconnectPeerCalled: return "DisconnectPeerCalled";
                case DisconnectReason.ConnectionRejected: return "ConnectionRejected";
                default: return "Disconnected";
            }
        }
    }
}
