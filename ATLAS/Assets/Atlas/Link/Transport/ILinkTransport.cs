namespace Atlas.Link
{
    /// <summary>How a packet travels (Master Plan section 7.2). The delivery fixes the channel.</summary>
    public enum LinkDelivery : byte
    {
        /// <summary>Channel 0, reliable and ordered: every control message and <c>Telemetry</c>.</summary>
        Reliable = 0,
        /// <summary>Channel 1, sequenced: <c>State</c>. A late packet is dropped, never delivered out of order.</summary>
        Sequenced = 1,
        /// <summary>No channel, unreliable: <c>TPing</c> and <c>TPong</c>.</summary>
        Unreliable = 2
    }

    /// <summary>
    /// What a transport tells its owner. Every call arrives inside <see cref="ILinkTransport.Poll"/>, on the
    /// thread that called it, so nothing here needs a lock.
    /// </summary>
    public interface ILinkTransportListener
    {
        void OnPeerConnected(int peerId);

        /// <summary>A peer left, or an outgoing connect attempt failed (the id was then never connected).</summary>
        void OnPeerDisconnected(int peerId, string reason);

        /// <summary>
        /// A packet arrived. The bytes are valid only during the call: copy what must be kept.
        /// </summary>
        void OnReceive(int peerId, byte[] data, int offset, int length, LinkDelivery delivery);
    }

    /// <summary>
    /// Optional on a transport: where a peer is on the network. The Hub shows it in its link panel, so an
    /// operator can tell which phone is connected. A transport that has no addresses does not implement it.
    /// </summary>
    public interface ILinkPeerAddresses
    {
        /// <summary>
        /// The peer's address as text (an IPv4 literal on the real link), or null for an unknown peer. Builds a
        /// string: for status displays and logs, never the frame path.
        /// </summary>
        string GetPeerAddress(int peerId);
    }

    /// <summary>
    /// Optional on a transport: when each packet arrived. A packet waits for the next <see cref="ILinkTransport.Poll"/>
    /// before the listener sees it, up to a whole frame on the phone; a clock exchange that read its receive
    /// times at the poll would carry that wait (Master Plan section 7.2: 2 ms at p95). A transport that
    /// implements this stamps a packet the moment it arrives, on its receive thread.
    /// </summary>
    public interface ILinkReceiveStamps
    {
        /// <summary>
        /// The clock the arrivals are stamped with: the listener's own, so that the stamps compare with its
        /// other times. Its <see cref="ILinkClock.NowUs"/> is then read on the transport's receive thread.
        /// </summary>
        void StampWith(ILinkClock clock);

        /// <summary>
        /// Inside <see cref="ILinkTransportListener.OnReceive"/>: when the packet being delivered arrived, in
        /// microseconds of the clock given to <see cref="StampWith"/>. Never later than that clock is now.
        /// </summary>
        long LastReceiveUs { get; }
    }

    /// <summary>
    /// The few things the link needs from a network: connect, send three ways, and a non-blocking poll. The real
    /// one is <see cref="LiteNetTransport"/>; tests use <see cref="LoopbackTransport"/>.
    /// </summary>
    public interface ILinkTransport
    {
        ILinkTransportListener Listener { get; set; }

        bool IsRunning { get; }

        /// <summary>The bound local port, or 0 when not running.</summary>
        int LocalPort { get; }

        /// <summary>Starts as a client on any free port. A client rejects every incoming connection request.</summary>
        bool StartClient();

        /// <summary>Starts as the listener on <paramref name="port"/> (0 picks a free port).</summary>
        bool StartHost(int port);

        /// <summary>Begins connecting to a host. Returns at once; the outcome arrives through the listener.</summary>
        void Connect(string address, int port);

        /// <summary>Delivers everything that has arrived to the listener. Never blocks.</summary>
        void Poll();

        /// <summary>Queues a packet. The bytes are copied before this returns. False when the peer is unknown or the packet too large.</summary>
        bool Send(int peerId, byte[] data, int offset, int length, LinkDelivery delivery);

        /// <summary>Asks the transport to put queued packets on the wire now instead of at its next tick.</summary>
        void Flush();

        void Disconnect(int peerId);

        /// <summary>
        /// Closes the connection to a peer and hands it one last packet with the close itself, so a goodbye
        /// cannot be overtaken by the disconnect or lost with the socket. The far end receives the packet as a
        /// reliable one, and then the disconnect. The packet must fit in one datagram.
        /// </summary>
        void Disconnect(int peerId, byte[] data, int offset, int length);

        void Stop();

        /// <summary>Smoothed round trip to a peer in milliseconds; 0 when unknown.</summary>
        int GetRttMs(int peerId);

        /// <summary>
        /// Milliseconds since anything at all arrived from a peer, keep-alives included; negative when the
        /// transport cannot tell.
        /// </summary>
        float GetSilenceMs(int peerId);

        /// <summary>Lost packets per thousand sent to a peer; 0 when unknown.</summary>
        int GetLossPermille(int peerId);
    }
}
