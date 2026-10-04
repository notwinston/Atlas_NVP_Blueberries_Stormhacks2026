using System;

namespace Atlas.Link
{
    /// <summary>The watchdog states of the link (Master Plan section 7.2).</summary>
    public enum LinkState : byte
    {
        /// <summary>No peer. The app is unchanged; the client reconnects with back-off.</summary>
        Offline = 0,
        /// <summary>Just reconnected: Hello, Welcome, eight pings, ParamManifest, then the Hub replays the snapshot.</summary>
        Resync = 1,
        /// <summary>A packet arrived within the last 500 ms.</summary>
        Connected = 2,
        /// <summary>Silent for 500 ms to 3 s. <c>ParamSet</c> is ignored.</summary>
        Degraded = 3
    }

    /// <summary>
    /// The link watchdog state machine (Master Plan section 7.2, R4 section 5.5): CONNECTED while packets arrive,
    /// DEGRADED after 500 ms of silence (left after 2 s healthy), OFFLINE at 3 s or when the transport drops the
    /// peer, reconnect attempts after 0.5, 1, 2, 4, 4 ... s, and RESYNC between a new connection and the end of
    /// the handshake. It owns no socket and reads no clock: the caller reports what happened and when.
    /// </summary>
    public sealed class LinkWatchdog
    {
        public const long DefaultDegradedAfterUs = 500000;
        public const long DefaultOfflineAfterUs = 3000000;
        public const long DefaultRecoverAfterUs = 2000000;
        public const long FirstBackoffUs = 500000;
        public const long MaxBackoffUs = 4000000;

        /// <summary>Silence after which the link is DEGRADED.</summary>
        public long DegradedAfterUs = DefaultDegradedAfterUs;
        /// <summary>Silence after which the link is OFFLINE.</summary>
        public long OfflineAfterUs = DefaultOfflineAfterUs;
        /// <summary>Unbroken healthy time needed to leave DEGRADED.</summary>
        public long RecoverAfterUs = DefaultRecoverAfterUs;

        long _lastPacketUs;
        long _healthySinceUs;
        long _nextAttemptUs;
        long _backoffUs = FirstBackoffUs;
        bool _attemptPending;

        public LinkState State { get; private set; }

        /// <summary>Raised on every state change with (previous, current).</summary>
        public event Action<LinkState, LinkState> StateChanged;

        /// <summary>The phone applies <c>ParamSet</c> only in this state.</summary>
        public bool AcceptsRemoteParams { get { return State == LinkState.Connected; } }

        /// <summary>True while a peer exists (any state but OFFLINE).</summary>
        public bool HasPeer { get { return State != LinkState.Offline; } }

        /// <summary>The wait that will follow the next reconnect attempt.</summary>
        public long BackoffUs { get { return _backoffUs; } }

        /// <summary>When the next reconnect attempt is due.</summary>
        public long NextAttemptUs { get { return _nextAttemptUs; } }

        /// <summary>Time since the last packet; 0 when offline.</summary>
        public long SilenceUs(long nowUs) { return State == LinkState.Offline ? 0 : nowUs - _lastPacketUs; }

        /// <summary>Starts offline with the first connect attempt due at once.</summary>
        public void Start(long nowUs)
        {
            SetState(LinkState.Offline);
            _backoffUs = FirstBackoffUs;
            _nextAttemptUs = nowUs;
            _attemptPending = true;
        }

        /// <summary>Stops asking for connect attempts and goes offline.</summary>
        public void Stop()
        {
            _attemptPending = false;
            SetState(LinkState.Offline);
        }

        /// <summary>
        /// True when a connect attempt is due. Each true answer schedules the next one, so the caller simply
        /// calls <c>Connect</c> whenever this returns true: after a drop that is at +0.5, +1, +2, +4, +4 ... s.
        /// </summary>
        public bool ShouldConnect(long nowUs)
        {
            if (State != LinkState.Offline || !_attemptPending || nowUs < _nextAttemptUs) return false;
            _nextAttemptUs = nowUs + _backoffUs;
            _backoffUs = Math.Min(_backoffUs * 2, MaxBackoffUs);
            return true;
        }

        /// <summary>The transport reports a peer: enter RESYNC and reset the back-off.</summary>
        public void OnTransportConnected(long nowUs)
        {
            _lastPacketUs = nowUs;
            _healthySinceUs = nowUs;
            _backoffUs = FirstBackoffUs;
            SetState(LinkState.Resync);
        }

        /// <summary>The handshake finished (Welcome in, burst done, manifest sent): enter CONNECTED.</summary>
        public void OnResyncComplete(long nowUs)
        {
            if (State != LinkState.Resync) return;
            _healthySinceUs = nowUs;
            SetState(nowUs - _lastPacketUs >= DegradedAfterUs ? LinkState.Degraded : LinkState.Connected);
        }

        /// <summary>The transport reports the peer gone, or an attempt failed: OFFLINE, first retry in 0.5 s.</summary>
        public void OnTransportDisconnected(long nowUs)
        {
            if (State == LinkState.Offline) return; // a failed attempt while offline keeps the running back-off
            GoOffline(nowUs);
        }

        /// <summary>Any packet from the peer arrived at <paramref name="nowUs"/>.</summary>
        public void OnPacket(long nowUs)
        {
            if (State == LinkState.Offline) return;
            // A packet that ends a silence starts a new healthy stretch; DEGRADED is left only after 2 s of it.
            if (nowUs - _lastPacketUs >= DegradedAfterUs) _healthySinceUs = nowUs;
            _lastPacketUs = nowUs;
        }

        /// <summary>
        /// The transport reports how long the peer has been silent, keep-alives included. Equivalent to
        /// <see cref="OnPacket"/> at <c>nowUs - silenceUs</c> when that is news.
        /// </summary>
        public void OnSilence(long nowUs, long silenceUs)
        {
            long heardUs = nowUs - (silenceUs < 0 ? 0 : silenceUs);
            if (heardUs > _lastPacketUs) OnPacket(heardUs);
        }

        /// <summary>
        /// Applies the timing rules and returns the state. Call once per poll, after feeding this poll's packets.
        /// When the result turns OFFLINE while the transport still holds the peer, the caller drops the peer.
        /// </summary>
        public LinkState Tick(long nowUs)
        {
            if (State == LinkState.Offline) return State;
            long silence = nowUs - _lastPacketUs;
            if (silence >= OfflineAfterUs)
            {
                GoOffline(nowUs);
                return State;
            }
            if (State == LinkState.Connected)
            {
                if (silence >= DegradedAfterUs) SetState(LinkState.Degraded);
            }
            else if (State == LinkState.Degraded)
            {
                if (silence < DegradedAfterUs && nowUs - _healthySinceUs >= RecoverAfterUs) SetState(LinkState.Connected);
            }
            return State;
        }

        void GoOffline(long nowUs)
        {
            _backoffUs = FirstBackoffUs;
            _nextAttemptUs = nowUs + _backoffUs;
            _backoffUs = Math.Min(_backoffUs * 2, MaxBackoffUs);
            _attemptPending = true;
            SetState(LinkState.Offline);
        }

        void SetState(LinkState next)
        {
            LinkState previous = State;
            if (previous == next) return;
            State = next;
            Action<LinkState, LinkState> changed = StateChanged;
            if (changed != null) changed(previous, next);
        }
    }
}
