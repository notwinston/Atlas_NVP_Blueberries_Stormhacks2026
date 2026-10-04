using System;

namespace Atlas.App
{
    /// <summary>
    /// Frame times of any number of frames in fixed memory: counts per hundredth of a millisecond up to
    /// 100 ms, and the exact largest value. The perf probe's ring holds one minute; a soak holds fifteen.
    /// A time is counted in the bin at or above it, so a percentile is never reported lower than it was.
    /// Nothing is allocated after construction.
    /// </summary>
    public sealed class PerfHistogram
    {
        public const float BinMs = 0.01f;
        public const int Bins = 10000;

        readonly int[] _counts = new int[Bins + 2];     // [0] no time reported; [Bins + 1] over 100 ms
        int _n;
        float _max;

        /// <summary>Samples held.</summary>
        public int Count { get { return _n; } }
        /// <summary>The largest sample, exactly.</summary>
        public float Max { get { return _max; } }
        /// <summary>False while every sample was 0: the platform reports no such time (the Editor).</summary>
        public bool Measured { get { return _max > 0f; } }

        public void Clear()
        {
            Array.Clear(_counts, 0, _counts.Length);
            _n = 0;
            _max = 0f;
        }

        public void Add(float ms)
        {
            int bin;
            if (!(ms > 0f)) bin = 0;                                 // also NaN
            else
            {
                // 16.7f times 100 is a hair over 1670: the small bias keeps a whole hundredth in its own bin.
                double scaled = Math.Ceiling((double)ms * 100.0 - 1e-3);
                bin = scaled > Bins ? Bins + 1 : (scaled < 1.0 ? 1 : (int)scaled);
                if (ms > _max) _max = float.IsInfinity(ms) ? _max : ms;
            }
            _counts[bin]++;
            _n++;
        }

        /// <summary>Nearest rank, as the perf probe's ring: the value at index ceil(q * n) - 1 of the sorted samples. 0 when empty.</summary>
        public float Percentile(float q)
        {
            if (_n == 0) return 0f;
            int rank = (int)Math.Ceiling((double)q * _n - 1e-3);
            if (rank < 1) rank = 1;
            if (rank > _n) rank = _n;
            int seen = 0;
            for (int bin = 0; bin <= Bins; bin++)
            {
                seen += _counts[bin];
                if (seen >= rank) return bin * BinMs;
            }
            return _max;
        }
    }

    /// <summary>
    /// What one row of the tour's output says: a space over its visit, a soak as a whole, or one forced tier.
    /// Times in milliseconds unless named otherwise; -1 where a time never came.
    /// </summary>
    public struct PerfSummary
    {
        /// <summary>"tour", "soak", "soakNoDepth" or "ladder".</summary>
        public string Run;
        /// <summary>"space", "total" or "ladder".</summary>
        public string Kind;
        public string Label;
        /// <summary>"S0" to "S6"; empty on a total row.</summary>
        public string Space;
        public string Variant;
        public string Room;
        /// <summary>"tracked", "replay" or "synthetic".</summary>
        public string Pose;
        /// <summary>"on" or "off".</summary>
        public string Depth;

        /// <summary>Seconds measured.</summary>
        public float Seconds;
        public int Frames;
        public float P50, P95, P99, Max;
        /// <summary>Percent of frames over 17.2 and over 33.8 ms.</summary>
        public float Over17_2, Over33_8;
        /// <summary>Mean and lowest of the probe's frames per second, one value per second.</summary>
        public float FpsMean, FpsMin;
        /// <summary>The largest per-frame counts seen, both eyes.</summary>
        public int DrawCalls, SetPass;
        public long Triangles;
        public float CpuMainP95, CpuRenderP95, GpuP95;
        public int MemMB;
        /// <summary>Managed bytes allocated in a frame: the mean over the seconds of each second's worst frame, and the worst frame of all.</summary>
        public long GcAllocB, GcAllocMaxB;
        /// <summary>0 nominal to 3 critical: the highest thermal state while measuring.</summary>
        public int ThermalMax;
        /// <summary>Seconds from the start of the run to the first Fair, Serious and Critical; -1 when never.</summary>
        public float TimeToFair, TimeToSerious, TimeToCritical;
        /// <summary>Ladder rows: the forced tier, and milliseconds until it was in force. -1 otherwise.</summary>
        public int Tier;
        public float EffectMs;

        /// <summary>"pass", "fail" or "incomplete" (nothing failed, but something could not be measured).</summary>
        public string Result;
        /// <summary>The rows that failed, joined with '|'; empty on a pass.</summary>
        public string Failed;
        /// <summary>The rows the platform gave no value for, joined with '|'.</summary>
        public string Unmeasured;

        public static PerfSummary Empty
        {
            get
            {
                return new PerfSummary
                {
                    Run = "", Kind = "", Label = "", Space = "", Variant = "", Room = "", Pose = "", Depth = "",
                    TimeToFair = -1f, TimeToSerious = -1f, TimeToCritical = -1f, Tier = -1, EffectMs = -1f,
                    Result = "", Failed = "", Unmeasured = "",
                };
            }
        }
    }

    /// <summary>
    /// The statistics of one space, or of a whole soak: every frame's four times and, once per second, the
    /// probe's counters (Master Plan section 11.2). Engine-free; nothing is allocated after construction.
    /// </summary>
    public sealed class PerfStats
    {
        public const float OneVsyncMs = 17.2f;
        public const float TwoVsyncMs = 33.8f;

        readonly PerfHistogram _frame = new PerfHistogram();
        readonly PerfHistogram _cpuMain = new PerfHistogram();
        readonly PerfHistogram _cpuRender = new PerfHistogram();
        readonly PerfHistogram _gpu = new PerfHistogram();

        int _over17, _over33;
        double _seconds;
        int _rows;
        double _fpsSum;
        float _fpsMin;
        int _draws, _setPass, _memMB;
        long _tris;
        double _gcSum;
        long _gcMax;
        int _thermalMax;

        public int Frames { get { return _frame.Count; } }
        /// <summary>Per-second rows taken.</summary>
        public int Rows { get { return _rows; } }
        public double Seconds { get { return _seconds; } }

        public void Clear()
        {
            _frame.Clear();
            _cpuMain.Clear();
            _cpuRender.Clear();
            _gpu.Clear();
            _over17 = _over33 = 0;
            _seconds = 0.0;
            _rows = 0;
            _fpsSum = 0.0;
            _fpsMin = 0f;
            _draws = _setPass = _memMB = 0;
            _tris = 0;
            _gcSum = 0.0;
            _gcMax = 0;
            _thermalMax = 0;
        }

        /// <summary>One measured frame. deltaTime is what the frame lasted on the app clock, seconds.</summary>
        public void AddFrame(float frameMs, float cpuMainMs, float cpuRenderMs, float gpuMs, float deltaTime)
        {
            _frame.Add(frameMs);
            _cpuMain.Add(cpuMainMs);
            _cpuRender.Add(cpuRenderMs);
            _gpu.Add(gpuMs);
            if (frameMs > OneVsyncMs) _over17++;
            if (frameMs > TwoVsyncMs) _over33++;
            if (deltaTime > 0f) _seconds += deltaTime;
        }

        /// <summary>One second of the probe: frames per second, the second's largest counters, memory, allocation.</summary>
        public void AddSecond(float fps, int drawCalls, int setPass, long triangles, int memMB, long gcAllocBytes)
        {
            _fpsSum += fps;
            if (_rows == 0 || fps < _fpsMin) _fpsMin = fps;
            _rows++;
            if (drawCalls > _draws) _draws = drawCalls;
            if (setPass > _setPass) _setPass = setPass;
            if (triangles > _tris) _tris = triangles;
            if (memMB > _memMB) _memMB = memMB;
            _gcSum += gcAllocBytes;
            if (gcAllocBytes > _gcMax) _gcMax = gcAllocBytes;
        }

        /// <summary>The thermal state while measuring, 0 to 3; the highest is kept.</summary>
        public void AddThermal(int state)
        {
            if (state > _thermalMax) _thermalMax = state;
        }

        /// <summary>Fills the measured fields of a row. Names, timeline and verdict are the caller's.</summary>
        public void Fill(ref PerfSummary s)
        {
            int n = _frame.Count;
            s.Seconds = (float)_seconds;
            s.Frames = n;
            s.P50 = _frame.Percentile(0.50f);
            s.P95 = _frame.Percentile(0.95f);
            s.P99 = _frame.Percentile(0.99f);
            s.Max = _frame.Max;
            s.Over17_2 = n > 0 ? 100f * _over17 / n : 0f;
            s.Over33_8 = n > 0 ? 100f * _over33 / n : 0f;
            // A visit too short for a row of the probe still has a frame rate: frames over time.
            s.FpsMean = _rows > 0 ? (float)(_fpsSum / _rows) : (_seconds > 0.0 ? (float)(n / _seconds) : 0f);
            s.FpsMin = _rows > 0 ? _fpsMin : s.FpsMean;
            s.DrawCalls = _draws;
            s.SetPass = _setPass;
            s.Triangles = _tris;
            s.CpuMainP95 = _cpuMain.Percentile(0.95f);
            s.CpuRenderP95 = _cpuRender.Percentile(0.95f);
            s.GpuP95 = _gpu.Percentile(0.95f);
            s.MemMB = _memMB;
            s.GcAllocB = _rows > 0 ? (long)Math.Round(_gcSum / _rows) : 0;
            s.GcAllocMaxB = _gcMax;
            s.ThermalMax = _thermalMax;
        }

        /// <summary>True when the platform reported the three thread times (a phone); false in the Editor.</summary>
        public bool ThreadTimesMeasured { get { return _cpuMain.Measured && _cpuRender.Measured && _gpu.Measured; } }
        public bool CpuMainMeasured { get { return _cpuMain.Measured; } }
        public bool CpuRenderMeasured { get { return _cpuRender.Measured; } }
        public bool GpuMeasured { get { return _gpu.Measured; } }
    }
}
