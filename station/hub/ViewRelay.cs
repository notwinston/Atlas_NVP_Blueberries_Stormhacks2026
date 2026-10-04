using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Link;

namespace Atlas.Station
{
    /// <summary>
    /// The phone's view for the Console's View page: one eye's image, before the lens pass, as JPEG frames the
    /// phone posts while anyone watches. Every viewer is sent the newest frame; a slow viewer skips frames
    /// instead of queueing them. <see cref="WatchingChanged"/> tells the Hub when to ask the phone to start or
    /// stop. Thread-safe.
    /// </summary>
    public sealed class ViewRelay
    {
        /// <summary>A frame larger than this is refused: a 640-pixel JPEG is a few tens of kilobytes.</summary>
        public const int MaxFrameBytes = 1 << 20;

        readonly object _gate = new object();
        readonly Stopwatch _clock = Stopwatch.StartNew();
        byte[] _frame;
        string _mode = string.Empty;
        long _seq;
        long _receivedMs = -1;
        int _viewers;
        // Frames in the last whole second, for the page's rate.
        long _windowStartMs;
        int _windowFrames;
        float _fps;
        TaskCompletionSource<bool> _next = NewSignal();

        /// <summary>Raised outside the lock when the first viewer arrives (true) and when the last one leaves (false).</summary>
        public event Action<bool> WatchingChanged;

        public bool Watching { get { lock (_gate) return _viewers > 0; } }
        public int Viewers { get { lock (_gate) return _viewers; } }
        /// <summary>Frames received since the Hub started.</summary>
        public long Frames { get { lock (_gate) return _seq; } }

        /// <summary>A frame from the phone. mode is what the phone was showing: "headset" or "hand". False when it is not a JPEG.</summary>
        public bool Post(byte[] jpeg, string mode)
        {
            if (!IsJpeg(jpeg)) return false;
            TaskCompletionSource<bool> waiting;
            lock (_gate)
            {
                long now = _clock.ElapsedMilliseconds;
                _frame = jpeg;
                _mode = mode ?? string.Empty;
                _seq++;
                _receivedMs = now;
                if (now - _windowStartMs >= 1000)
                {
                    _fps = _windowFrames * 1000f / Math.Max(1, now - _windowStartMs);
                    _windowStartMs = now;
                    _windowFrames = 0;
                }
                _windowFrames++;
                waiting = _next;
                _next = NewSignal();
            }
            waiting.TrySetResult(true);
            return true;
        }

        /// <summary>The newest frame and its number; false before the first one.</summary>
        public bool TryGetLatest(out byte[] jpeg, out long seq)
        {
            lock (_gate)
            {
                jpeg = _frame;
                seq = _seq;
                return _frame != null;
            }
        }

        /// <summary>Completes when a frame newer than <paramref name="seq"/> is there (at once if it already is).</summary>
        public Task WaitNewer(long seq, CancellationToken cancel)
        {
            Task signal;
            lock (_gate)
            {
                if (_frame != null && _seq > seq) return Task.CompletedTask;
                signal = _next.Task;
            }
            return signal.WaitAsync(cancel);
        }

        /// <summary>A viewer for as long as the returned object is not disposed.</summary>
        public IDisposable Watch()
        {
            bool first;
            lock (_gate) first = ++_viewers == 1;
            if (first) Raise(true);
            return new Viewer(this);
        }

        /// <summary>For the page: viewers, frames, rate, the age of the newest frame and what the phone was showing.</summary>
        public JsonValue Status()
        {
            lock (_gate)
            {
                long age = _receivedMs < 0 ? -1 : _clock.ElapsedMilliseconds - _receivedMs;
                // No frame for two seconds: the rate is zero, not the last second the phone sent.
                float fps = age < 0 || age > 2000 ? 0f : _fps;
                return JsonValue.Object()
                    .Set("viewers", _viewers).Set("frames", _seq).Set("ageMs", age)
                    .Set("fps", (float)Math.Round(fps, 1)).Set("bytes", _frame != null ? _frame.Length : 0).Set("mode", _mode);
            }
        }

        void Leave()
        {
            bool last;
            lock (_gate) last = --_viewers == 0;
            if (last) Raise(false);
        }

        void Raise(bool watching)
        {
            Action<bool> handler = WatchingChanged;
            if (handler != null) handler(watching);
        }

        /// <summary>Starts with the JPEG start-of-image marker and is no larger than <see cref="MaxFrameBytes"/>.</summary>
        public static bool IsJpeg(byte[] bytes)
        {
            return bytes != null && bytes.Length >= 4 && bytes.Length <= MaxFrameBytes && bytes[0] == 0xFF && bytes[1] == 0xD8;
        }

        static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        sealed class Viewer : IDisposable
        {
            ViewRelay _relay;

            public Viewer(ViewRelay relay) { _relay = relay; }

            public void Dispose()
            {
                ViewRelay relay = Interlocked.Exchange(ref _relay, null);
                if (relay != null) relay.Leave();
            }
        }
    }
}
