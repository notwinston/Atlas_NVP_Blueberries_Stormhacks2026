using System;
using Atlas.Link;

namespace Atlas.Telemetry
{
    /// <summary>What the probe measured in one frame. Times in milliseconds; a source that is not built reads 0.</summary>
    public struct PerfSample
    {
        public float FrameMs, CpuMainMs, CpuRenderMs, GpuMs;
        public long DrawCalls, SetPassCalls, Triangles;
        /// <summary>Managed bytes allocated in the frame.</summary>
        public long GcAllocBytes;
        /// <summary>Resident memory in bytes ("System Used Memory").</summary>
        public long UsedMemoryBytes;
    }

    /// <summary>The fields of a <c>Telemetry</c> row that the probe does not measure itself.</summary>
    public struct PerfRowContext
    {
        public ushort SceneId;
        public byte SpaceId;
        public byte Tier;
        public float RenderScale;
        public byte Thermal, Battery, Tracking, Msaa;
        public ushort VisibleTiles, Copies, RttMs, LossPermille, CueCount;
        public byte StateHz, BumpMax, AssetsPlaced, AssetsPending;
    }

    /// <summary>
    /// The engine-free half of <see cref="AtlasPerfProbe"/>: the ring, the per-second accumulators and the row
    /// (Master Plan sections 7.3.3 and 11.2). It reads no clock; the frame time stamps come in as arguments.
    /// </summary>
    public sealed class PerfProbeCore
    {
        public const float OneVsyncMs = 17.2f;
        public const float TwoVsyncMs = 33.8f;

        readonly PerfRing _ring;
        readonly int _ladderFrames;
        readonly int _minFrames;

        double _secondStart = double.NaN;
        int _secondFrames;
        float _secondMaxMs;
        long _secondDraws, _secondSetPass, _secondTris, _secondGc;
        long _memoryBytes;
        uint _seq;

        float _fps, _maxMs;
        long _draws, _setPass, _tris, _gc;

        public PerfProbeCore(int ringFrames = 3600, int ladderFrames = 600, int minFramesForData = 120)
        {
            _ring = new PerfRing(ringFrames);
            _ladderFrames = ladderFrames;
            _minFrames = minFramesForData;
        }

        public PerfRing Ring { get { return _ring; } }

        /// <summary>False until enough frames have been sampled for the ladder to trust the percentiles.</summary>
        public bool HasData { get { return _ring.Count >= _minFrames; } }

        /// <summary>GPU frame time, p95 over the ladder window. Refreshed when a second completes.</summary>
        public float GpuP95Ms10s { get; private set; }

        /// <summary>Frame time, p95 over the ladder window. Refreshed when a second completes.</summary>
        public float FrameP95Ms10s { get; private set; }

        /// <summary>Rows made since the session started.</summary>
        public uint RowCount { get { return _seq; } }

        /// <summary>
        /// Adds one frame. Returns true when this frame completes a second: the caller then builds the row with
        /// <see cref="BuildRow"/>.
        /// </summary>
        public bool AddFrame(double timeSeconds, in PerfSample sample)
        {
            _ring.Add(sample.FrameMs, sample.CpuMainMs, sample.CpuRenderMs, sample.GpuMs);
            if (double.IsNaN(_secondStart) || timeSeconds < _secondStart) _secondStart = timeSeconds;

            _secondFrames++;
            if (sample.FrameMs > _secondMaxMs) _secondMaxMs = sample.FrameMs;
            if (sample.DrawCalls > _secondDraws) _secondDraws = sample.DrawCalls;
            if (sample.SetPassCalls > _secondSetPass) _secondSetPass = sample.SetPassCalls;
            if (sample.Triangles > _secondTris) _secondTris = sample.Triangles;
            if (sample.GcAllocBytes > _secondGc) _secondGc = sample.GcAllocBytes;
            _memoryBytes = sample.UsedMemoryBytes;

            double elapsed = timeSeconds - _secondStart;
            if (elapsed < 1.0) return false;

            _fps = (float)(_secondFrames / elapsed);
            _maxMs = _secondMaxMs;
            _draws = _secondDraws;
            _setPass = _secondSetPass;
            _tris = _secondTris;
            _gc = _secondGc;
            _secondStart = timeSeconds;
            _secondFrames = 0;
            _secondMaxMs = 0f;
            _secondDraws = _secondSetPass = _secondTris = _secondGc = 0;

            GpuP95Ms10s = _ring.Percentile(PerfRing.ChannelGpu, 0.95f, _ladderFrames);
            FrameP95Ms10s = _ring.Percentile(PerfRing.ChannelFrame, 0.95f, _ladderFrames);
            return true;
        }

        /// <summary>
        /// Fills the row of the second that just completed. Percentiles are over the whole ring; the frame rate,
        /// the worst frame, the counters and the allocation are over the last second.
        /// </summary>
        public void BuildRow(long timeUs, in PerfRowContext context, out TelemetryMessage row)
        {
            row = new TelemetryMessage();
            row.TimeUs = timeUs;
            row.Seq = _seq++;
            row.SceneId = context.SceneId;
            row.SpaceId = context.SpaceId;
            row.Tier = context.Tier;
            row.Fps = _fps;
            row.P50 = _ring.Percentile(PerfRing.ChannelFrame, 0.50f);
            row.P95 = _ring.Percentile(PerfRing.ChannelFrame, 0.95f);
            row.P99 = _ring.Percentile(PerfRing.ChannelFrame, 0.99f);
            row.Max = _maxMs;
            row.Over17_2 = _ring.ShareOver(OneVsyncMs);
            row.Over33_8 = _ring.ShareOver(TwoVsyncMs);
            row.CpuMainP95 = _ring.Percentile(PerfRing.ChannelCpuMain, 0.95f);
            row.CpuRenderP95 = _ring.Percentile(PerfRing.ChannelCpuRender, 0.95f);
            row.GpuP95 = _ring.Percentile(PerfRing.ChannelGpu, 0.95f);
            row.RenderScale = context.RenderScale;
            row.DrawCalls = ToU16(_draws);
            row.SetPass = ToU16(_setPass);
            row.TrisK = ToU16((_tris + 500) / 1000);
            row.MemMB = ToU16(_memoryBytes >> 20);
            row.VisibleTiles = context.VisibleTiles;
            row.Copies = context.Copies;
            row.RttMs = context.RttMs;
            row.LossPermille = context.LossPermille;
            row.CueCount = context.CueCount;
            row.GcAllocB = _gc < 0 ? 0u : _gc > uint.MaxValue ? uint.MaxValue : (uint)_gc;
            row.Thermal = context.Thermal;
            row.Battery = context.Battery;
            row.Tracking = context.Tracking;
            row.Msaa = context.Msaa;
            row.StateHz = context.StateHz;
            row.BumpMax = context.BumpMax;
            row.AssetsPlaced = context.AssetsPlaced;
            row.AssetsPending = context.AssetsPending;
        }

        /// <summary>Empties the ring, so a row never mixes two spaces. The row counter keeps running.</summary>
        public void ClearRing()
        {
            _ring.Clear();
            GpuP95Ms10s = 0f;
            FrameP95Ms10s = 0f;
        }

        /// <summary>A new session: empties the ring and restarts the row counter at 0.</summary>
        public void ResetSession()
        {
            ClearRing();
            _seq = 0;
            _secondStart = double.NaN;
            _secondFrames = 0;
            _secondMaxMs = 0f;
            _secondDraws = _secondSetPass = _secondTris = _secondGc = 0;
        }

        static ushort ToU16(long value) { return value < 0 ? (ushort)0 : value > ushort.MaxValue ? ushort.MaxValue : (ushort)value; }
    }
}
