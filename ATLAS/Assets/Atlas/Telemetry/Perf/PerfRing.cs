using System;

namespace Atlas.Telemetry
{
    /// <summary>
    /// The engine-free core of the perf probe: a ring of frame timings in fixed arrays, with nearest-rank
    /// percentiles on a scratch copy (Master Plan sections 8.8 and 11.2). Nothing is allocated after
    /// construction.
    /// </summary>
    public sealed class PerfRing
    {
        public const int ChannelFrame = 0;
        public const int ChannelCpuMain = 1;
        public const int ChannelCpuRender = 2;
        public const int ChannelGpu = 3;
        public const int ChannelCount = 4;

        readonly float[][] _samples = new float[ChannelCount][];
        readonly float[] _scratch;
        readonly int _capacity;
        int _head;      // where the next sample goes
        int _count;

        // The scratch copy stays sorted for one (channel, window) until a sample arrives, so the three
        // percentiles of one row cost one sort.
        int _sortedChannel = -1;
        int _sortedCount;

        public PerfRing(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException("capacity");
            _capacity = capacity;
            for (int c = 0; c < ChannelCount; c++) _samples[c] = new float[capacity];
            _scratch = new float[capacity];
        }

        public int Capacity { get { return _capacity; } }

        /// <summary>Samples held, 0 up to the capacity.</summary>
        public int Count { get { return _count; } }

        public void Clear()
        {
            _head = 0;
            _count = 0;
            _sortedChannel = -1;
        }

        /// <summary>Adds one frame, in milliseconds. The oldest frame is overwritten when the ring is full.</summary>
        public void Add(float frameMs, float cpuMainMs, float cpuRenderMs, float gpuMs)
        {
            _samples[ChannelFrame][_head] = Clean(frameMs);
            _samples[ChannelCpuMain][_head] = Clean(cpuMainMs);
            _samples[ChannelCpuRender][_head] = Clean(cpuRenderMs);
            _samples[ChannelGpu][_head] = Clean(gpuMs);
            _head = _head + 1 == _capacity ? 0 : _head + 1;
            if (_count < _capacity) _count++;
            _sortedChannel = -1;
        }

        // A NaN would poison the sort; a negative time is a platform that reports none.
        static float Clean(float ms) { return ms > 0f && !float.IsInfinity(ms) ? ms : 0f; }

        /// <summary>The sample <paramref name="age"/> frames ago: 0 is the newest. 0 when there is none.</summary>
        public float Recent(int channel, int age)
        {
            if (age < 0 || age >= _count) return 0f;
            int i = _head - 1 - age;
            if (i < 0) i += _capacity;
            return _samples[channel][i];
        }

        /// <summary>
        /// Nearest rank over the last <paramref name="window"/> samples (0 means all): the value at index
        /// ceil(q * n) - 1 of the sorted samples. Channel: 0 frame, 1 CPU main, 2 CPU render, 3 GPU. 0 when
        /// there is no sample.
        /// </summary>
        public float Percentile(int channel, float q, int window = 0)
        {
            int n = Window(window);
            if (n == 0) return 0f;
            Sort(channel, n);
            // q arrives as a float: 0.99f is a hair above 0.99, which would push ceil(q * n) one rank up
            // whenever q * n is whole. The small bias is far below the distance between two ranks.
            int rank = (int)Math.Ceiling((double)q * n - 1e-3) - 1;
            if (rank < 0) rank = 0;
            if (rank >= n) rank = n - 1;
            return _scratch[rank];
        }

        /// <summary>Percent (0 to 100) of the frame-time samples above a threshold, over the last <paramref name="window"/> samples.</summary>
        public float ShareOver(float thresholdMs, int window = 0)
        {
            int n = Window(window);
            if (n == 0) return 0f;
            float[] frames = _samples[ChannelFrame];
            int over = 0;
            int i = _head - n;
            if (i < 0) i += _capacity;
            for (int k = 0; k < n; k++)
            {
                if (frames[i] > thresholdMs) over++;
                i = i + 1 == _capacity ? 0 : i + 1;
            }
            return 100f * over / n;
        }

        /// <summary>Arithmetic mean of a channel over the last <paramref name="window"/> samples; 0 when there is none.</summary>
        public float Mean(int channel, int window = 0)
        {
            int n = Window(window);
            if (n == 0) return 0f;
            float[] samples = _samples[channel];
            double sum = 0.0;
            int i = _head - n;
            if (i < 0) i += _capacity;
            for (int k = 0; k < n; k++)
            {
                sum += samples[i];
                i = i + 1 == _capacity ? 0 : i + 1;
            }
            return (float)(sum / n);
        }

        /// <summary>The largest sample of a channel over the last <paramref name="window"/> samples.</summary>
        public float Max(int channel, int window = 0)
        {
            int n = Window(window);
            float max = 0f;
            for (int age = 0; age < n; age++)
            {
                float v = Recent(channel, age);
                if (v > max) max = v;
            }
            return max;
        }

        int Window(int window) { return window <= 0 || window > _count ? _count : window; }

        void Sort(int channel, int n)
        {
            if (_sortedChannel == channel && _sortedCount == n) return;
            float[] samples = _samples[channel];
            int i = _head - n;
            if (i < 0) i += _capacity;
            int first = Math.Min(n, _capacity - i);
            Array.Copy(samples, i, _scratch, 0, first);
            if (first < n) Array.Copy(samples, 0, _scratch, first, n - first);
            Array.Sort(_scratch, 0, n);
            _sortedChannel = channel;
            _sortedCount = n;
        }
    }
}
