using System;
using Atlas.Link;
using Atlas.World;
using Unity.Profiling;
using UnityEngine;

namespace Atlas.Telemetry
{
    /// <summary>
    /// Frame timings and render counters: a 3,600-frame ring and one <c>Telemetry</c> row per second (Master
    /// Plan sections 7.3.3, 8.8 and 11.2, task P1-D04). This class only reads the engine
    /// (<c>FrameTimingManager</c>, five <c>ProfilerRecorder</c>s, the battery); the arithmetic is in
    /// <see cref="PerfProbeCore"/>. It is also the <see cref="IPerfSource"/> the quality ladder reads. Nothing
    /// is allocated after construction.
    /// </summary>
    public sealed class AtlasPerfProbe : IPerfSource, IAfterFrameStep, IDisposable
    {
        /// <summary>One minute at 60 fps.</summary>
        public const int RingFrames = 3600;
        /// <summary>The window of <see cref="IPerfSource"/>: ten seconds.</summary>
        public const int LadderFrames = 600;
        public const int MinFramesForData = 120;

        readonly PerfProbeCore _core = new PerfProbeCore(RingFrames, LadderFrames, MinFramesForData);
        readonly FrameTiming[] _timings = new FrameTiming[1];
        readonly LinkPump _pump;
        readonly bool _timingEnabled;
        ProfilerRecorder _draws, _setPass, _triangles, _memory, _gcAlloc;
        IQualityState _quality;
        TelemetryMessage _lastRow;
        bool _hasRow;
        double _time;

        /// <param name="pump">The link driver that takes each row and supplies the link's share of it; may be null.</param>
        public AtlasPerfProbe(LinkPump pump = null)
        {
            _pump = pump;
            if (pump != null) pump.SpaceChanged += OnSpaceChanged;
            _timingEnabled = FrameTimingManager.IsFeatureEnabled();
            // A counter the player does not have reads 0: Valid is checked at every read.
            _draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            _memory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            _gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        }

        public PerfProbeCore Core { get { return _core; } }

        /// <summary>Tier, thermal state, render scale and MSAA of the row come from here. Found through the services when null.</summary>
        public IQualityState Quality
        {
            get { return _quality; }
            set { _quality = value; }
        }

        /// <summary>Objects showing a generated mesh, and objects waiting for one; written by the asset client.</summary>
        public byte AssetsPlaced { get; set; }
        public byte AssetsPending { get; set; }

        /// <summary>Tiles drawn, written by the tile renderer; 0 until there is one.</summary>
        public ushort VisibleTiles { get; set; }

        // ---- IPerfSource -------------------------------------------------------------------------------

        public bool HasData { get { return _core.HasData; } }
        public float GpuP95Ms10s { get { return _core.GpuP95Ms10s; } }
        public float FrameP95Ms10s { get { return _core.FrameP95Ms10s; } }

        /// <summary>The thermal state the ladder last published, 0 nominal to 3 critical.</summary>
        public int ThermalState { get { return _quality != null ? _quality.ThermalState : 0; } }

        /// <summary>Frames in the ring, 0 to 3600.</summary>
        public int SampleCount { get { return _core.Ring.Count; } }

        /// <summary>The newest row; false before the first second has passed.</summary>
        public bool TryGetLastRow(out TelemetryMessage row)
        {
            row = _lastRow;
            return _hasRow;
        }

        /// <summary>Raised once per second at step 8b with the new row.</summary>
        public event Action<TelemetryMessage> RowReady;

        /// <summary>Step 8b: samples this frame's timings and counters; once per second makes the row and hands it on.</summary>
        public void AfterFrame(in FrameContext frame)
        {
            var sample = new PerfSample();
            bool timed = false;
            if (_timingEnabled)
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _timings) == 1 && _timings[0].cpuFrameTime > 0.0)
                {
                    sample.FrameMs = (float)_timings[0].cpuFrameTime;
                    sample.CpuMainMs = (float)_timings[0].cpuMainThreadFrameTime;
                    sample.CpuRenderMs = (float)_timings[0].cpuRenderThreadFrameTime;
                    sample.GpuMs = (float)_timings[0].gpuFrameTime;
                    timed = true;
                }
            }
            // No timing from the platform (the Editor, the feature off): the frame's own delta stands in.
            if (!timed) sample.FrameMs = frame.DeltaTime * 1000f;

            sample.DrawCalls = _draws.Valid ? _draws.LastValue : 0;
            sample.SetPassCalls = _setPass.Valid ? _setPass.LastValue : 0;
            sample.Triangles = _triangles.Valid ? _triangles.LastValue : 0;
            sample.UsedMemoryBytes = _memory.Valid ? _memory.LastValue : 0;
            sample.GcAllocBytes = _gcAlloc.Valid ? _gcAlloc.LastValue : 0;

            // The probe keeps its own time, so a second is a second of frames whatever the loop's clock does.
            _time += frame.DeltaTime > 0f ? frame.DeltaTime : sample.FrameMs * 0.001f;
            if (!_core.AddFrame(_time, in sample)) return;

            var context = new PerfRowContext();
            if (_quality == null) AtlasServices.TryGet(out _quality);
            if (_quality != null)
            {
                context.Tier = (byte)_quality.Tier;
                context.Thermal = (byte)Mathf.Clamp(_quality.ThermalState, 0, 3);
                context.RenderScale = _quality.RenderScale;
                context.Msaa = (byte)Mathf.Clamp(_quality.Msaa, 0, 255);
            }
            else context.Tier = (byte)frame.Tier;
            context.Tracking = TrackingStates.ToWire(frame.Tracking);
            float battery = SystemInfo.batteryLevel;
            context.Battery = battery < 0f ? (byte)255 : (byte)Mathf.Clamp(Mathf.RoundToInt(battery * 100f), 0, 100);
            context.VisibleTiles = VisibleTiles;
            context.AssetsPlaced = AssetsPlaced;
            context.AssetsPending = AssetsPending;

            long nowUs;
            if (_pump != null)
            {
                _pump.TakeSecond(ref context);
                nowUs = _pump.NowUs;
            }
            else nowUs = (long)(_time * 1000000.0);

            _core.BuildRow(nowUs, in context, out _lastRow);
            _hasRow = true;
            if (_pump != null) _pump.SubmitRow(in _lastRow);
            Action<TelemetryMessage> ready = RowReady;
            if (ready != null) ready(_lastRow);
        }

        void OnSpaceChanged(byte previous, byte current) { ClearRing(); }

        /// <summary>Empties the ring. Done by itself when the space changes, so a row never mixes two spaces.</summary>
        public void ClearRing() { _core.ClearRing(); }

        /// <summary>A new session: empties the ring and restarts the row counter.</summary>
        public void ResetSession()
        {
            _core.ResetSession();
            _hasRow = false;
        }

        public void Dispose()
        {
            if (_pump != null) _pump.SpaceChanged -= OnSpaceChanged;
            _draws.Dispose();
            _setPass.Dispose();
            _triangles.Dispose();
            _memory.Dispose();
            _gcAlloc.Dispose();
        }
    }
}
