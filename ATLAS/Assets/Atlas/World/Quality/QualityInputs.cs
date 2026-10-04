namespace Atlas.World
{
    /// <summary>What the ladder rule looks at, sampled once per second.</summary>
    public struct QualityInputs
    {
        /// <summary>Seconds, FrameContext.Time.</summary>
        public double Time;
        /// <summary>0 to 3, polled every 2 s.</summary>
        public int ThermalState;
        /// <summary>A perf source is present and has data.</summary>
        public bool PerfValid;
        /// <summary>IPerfSource.GpuP95Ms10s.</summary>
        public float GpuP95Ms;
        /// <summary>IPerfSource.FrameP95Ms10s.</summary>
        public float FrameP95Ms;
        /// <summary>thermal.pinTier: -1 automatic, 0 to 4.</summary>
        public int PinTier;
    }
}
