using System.Diagnostics;

namespace Atlas.Link
{
    /// <summary>A monotonic microsecond clock. The link never reads time any other way, so tests can inject one.</summary>
    public interface ILinkClock
    {
        /// <summary>Monotonic microseconds since an arbitrary start (<c>t_us</c> of Appendix B).</summary>
        long NowUs { get; }
    }

    /// <summary>The production clock: <see cref="Stopwatch"/> ticks converted to microseconds. No wall-clock steps.</summary>
    public sealed class StopwatchClock : ILinkClock
    {
        static readonly double s_usPerTick = 1000000.0 / Stopwatch.Frequency;

        public long NowUs { get { return (long)(Stopwatch.GetTimestamp() * s_usPerTick); } }
    }

    /// <summary>A clock that moves only when told to. Used by tests and by replay tools.</summary>
    public sealed class ManualClock : ILinkClock
    {
        public long NowUs { get; set; }

        public ManualClock(long startUs = 0) { NowUs = startUs; }

        public void AdvanceUs(long us) { NowUs += us; }
        public void AdvanceMs(double ms) { NowUs += (long)(ms * 1000.0); }
    }

    /// <summary>
    /// Estimates the offset between this clock and the Hub's (Master Plan section 7.2, R4 section 5.4).
    /// Four-timestamp exchange: <c>offset = ((t2 - t1) + (t3 - t4)) / 2</c>, <c>rtt = (t4 - t1) - (t3 - t2)</c>.
    /// Eight exchanges 50 ms apart after a restart, then one per second. Of the last 16 samples the one with the
    /// smallest round trip is the least disturbed by jitter; its offset is taken directly during the opening
    /// burst and low-passed (alpha 0.1) afterwards. Engine-free and driven by the caller's clock.
    /// </summary>
    public sealed class ClockSync
    {
        public const int BurstCount = 8;
        public const long BurstIntervalUs = 50000;
        public const long SteadyIntervalUs = 1000000;
        public const int WindowSize = 16;
        public const double Alpha = 0.1;

        /// <summary>How long after the last burst ping the burst counts as finished even if pongs were lost.</summary>
        public const long BurstGraceUs = 250000;

        /// <summary>
        /// Time between pings once the opening burst is over. A client keeps one per second; the Hub pings every
        /// client four times per second, which is also its heartbeat (<see cref="LinkHost.HeartbeatMs"/>).
        /// </summary>
        public long SteadyPeriodUs = SteadyIntervalUs;

        readonly long[] _sentT1 = new long[WindowSize];     // t1 of the ping with seq s, at s % WindowSize
        readonly uint[] _sentSeq = new uint[WindowSize];
        readonly bool[] _sentOpen = new bool[WindowSize];   // still waiting for its pong
        readonly long[] _offsets = new long[WindowSize];
        readonly long[] _rtts = new long[WindowSize];
        int _sampleCount;       // samples in the window, at most WindowSize
        int _sampleNext;
        int _accepted;          // pongs accepted since the restart
        uint _nextSeq;
        int _pingsSent;
        long _nextPingUs;
        long _lastBurstPingUs;
        double _offset;
        bool _started;

        /// <summary>True once at least one pong has been accepted since the last restart.</summary>
        public bool HasEstimate { get { return _accepted > 0; } }

        /// <summary>Remote clock minus local clock, microseconds. Add it to a local time to get Hub time.</summary>
        public long OffsetUs { get { return (long)System.Math.Round(_offset); } }

        /// <summary>Round trip of the best sample in the window, microseconds.</summary>
        public long RttUs { get; private set; }

        /// <summary>Round trip of the most recent sample, microseconds.</summary>
        public long LastRttUs { get; private set; }

        /// <summary>Pongs accepted since the last restart.</summary>
        public int SampleCount { get { return _accepted; } }

        public int PingsSent { get { return _pingsSent; } }

        /// <summary>
        /// True when the opening burst is over: eight pongs are in, or all eight pings left and the grace time
        /// for the stragglers has passed.
        /// </summary>
        public bool BurstComplete(long nowUs)
        {
            if (!_started) return false;
            if (_accepted >= BurstCount) return true;
            return _pingsSent >= BurstCount && nowUs - _lastBurstPingUs >= BurstGraceUs;
        }

        /// <summary>Forgets every sample and starts a new burst at <paramref name="nowUs"/>.</summary>
        public void Restart(long nowUs)
        {
            _started = true;
            _sampleCount = 0;
            _sampleNext = 0;
            _accepted = 0;
            _pingsSent = 0;
            _nextPingUs = nowUs;
            _lastBurstPingUs = nowUs;
            _offset = 0.0;
            RttUs = 0;
            LastRttUs = 0;
            for (int i = 0; i < WindowSize; i++) _sentOpen[i] = false;
        }

        /// <summary>Stops pinging until the next <see cref="Restart"/>. The last estimate is kept.</summary>
        public void Stop() { _started = false; }

        /// <summary>
        /// Call once per poll. Returns true when a ping is due and fills it; the caller sends it unreliably.
        /// </summary>
        public bool TryNextPing(long nowUs, out TPingMessage ping)
        {
            ping = default(TPingMessage);
            if (!_started || nowUs < _nextPingUs) return false;

            ping.Seq = _nextSeq++;
            ping.T1Us = nowUs;
            int slot = (int)(ping.Seq % WindowSize);
            _sentSeq[slot] = ping.Seq;
            _sentT1[slot] = nowUs;
            _sentOpen[slot] = true;

            _pingsSent++;
            if (_pingsSent <= BurstCount) _lastBurstPingUs = nowUs;
            _nextPingUs = nowUs + (_pingsSent < BurstCount ? BurstIntervalUs : SteadyPeriodUs);
            return true;
        }

        /// <summary>
        /// Feeds a received pong and the local time it arrived. Returns false for a pong that does not answer a
        /// recent ping of this instance (a duplicate, or one from before a restart).
        /// </summary>
        public bool OnPong(in TPongMessage pong, long t4Us)
        {
            int slot = (int)(pong.Seq % WindowSize);
            if (!_started || !_sentOpen[slot] || _sentSeq[slot] != pong.Seq || _sentT1[slot] != pong.T1Us) return false;
            _sentOpen[slot] = false;

            long rtt = (t4Us - pong.T1Us) - (pong.T3Us - pong.T2Us);
            if (rtt < 0) return false; // not physically possible: a corrupt or forged pong
            long offset = ((pong.T2Us - pong.T1Us) + (pong.T3Us - t4Us)) / 2;

            _offsets[_sampleNext] = offset;
            _rtts[_sampleNext] = rtt;
            _sampleNext = (_sampleNext + 1) % WindowSize;
            if (_sampleCount < WindowSize) _sampleCount++;
            _accepted++;
            LastRttUs = rtt;

            int best = 0;
            for (int i = 1; i < _sampleCount; i++)
                if (_rtts[i] < _rtts[best]) best = i;
            RttUs = _rtts[best];

            // During the burst there is nothing worth smoothing towards, so take the best sample as it is.
            if (_accepted <= BurstCount) _offset = _offsets[best];
            else _offset += Alpha * (_offsets[best] - _offset);
            return true;
        }

        /// <summary>The pong that answers a ping received at <paramref name="t2Us"/> and answered at <paramref name="t3Us"/>.</summary>
        public static TPongMessage Answer(in TPingMessage ping, long t2Us, long t3Us)
        {
            TPongMessage pong;
            pong.Seq = ping.Seq;
            pong.T1Us = ping.T1Us;
            pong.T2Us = t2Us;
            pong.T3Us = t3Us;
            return pong;
        }

        /// <summary>A local time on the remote (Hub) clock.</summary>
        public long ToRemoteUs(long localUs) { return localUs + OffsetUs; }

        /// <summary>A remote (Hub) time on the local clock.</summary>
        public long ToLocalUs(long remoteUs) { return remoteUs - OffsetUs; }
    }
}
