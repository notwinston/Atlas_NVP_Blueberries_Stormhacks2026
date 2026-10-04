using System.Text;

namespace Atlas.App
{
    /// <summary>
    /// The "must" column of the performance rows of Master Plan section 11.2, and the soak's pass rule of
    /// section 11.4.1, as code. The gate tests exactly these numbers. Engine-free.
    /// </summary>
    public static class PerfMust
    {
        public const float FrameP50Ms = 16.7f, FrameP95Ms = 20f, FrameP99Ms = 33.3f;
        /// <summary>Percent of frames over one and over two vsync intervals plus 0.5 ms.</summary>
        public const float Over17_2Percent = 5f, Over33_8Percent = 0.5f;
        public const int DrawCalls = 300, SetPass = 60;
        public const long Triangles = 500000;
        public const float CpuMainP95Ms = 8f, CpuRenderP95Ms = 6f;
        public const float GpuP95ColdMs = 9f, GpuP95HotMs = 14f;
        /// <summary>1.5 GB, counted in the mebibytes the probe reports.</summary>
        public const int MemoryMB = 1536;
        public const long GcAllocBytes = 1024;

        /// <summary>The soak: mean frame rate, lowest second, and the earliest Serious may come.</summary>
        public const float SoakFpsMean = 45f, SoakFpsMin = 30f, SoakSeriousNotBeforeSeconds = 600f;
        /// <summary>A soak shorter than this cannot show "Serious not before 10 min" and is a rehearsal.</summary>
        public const float SoakSeconds = 900f;
        /// <summary>A forced tier must be in force within this (task P1-A07).</summary>
        public const float TierEffectMs = 1000f;

        public const string Pass = "pass", Fail = "fail", Incomplete = "incomplete";

        /// <summary>
        /// Judges one space's row and writes Result, Failed and Unmeasured. A value the platform did not report
        /// (the thread times and the counters read 0 in the Editor) cannot pass: with no failure the row is
        /// then "incomplete".
        /// </summary>
        /// <param name="cold">True for the per-space run: the phone must stay at thermal Nominal, and the cold
        /// GPU limit applies. False for a soak row: the hot limit applies once the phone has left Nominal.</param>
        public static void JudgeSpace(ref PerfSummary s, bool cold, StringBuilder scratch)
        {
            scratch.Length = 0;
            int mark = 0;
            if (s.Frames <= 0)
            {
                s.Failed = "noFrames";
                s.Unmeasured = "";
                s.Result = Fail;
                return;
            }
            if (s.P50 > FrameP50Ms) Add(scratch, "p50");
            if (s.P95 > FrameP95Ms) Add(scratch, "p95");
            if (s.P99 > FrameP99Ms) Add(scratch, "p99");
            if (s.Over17_2 > Over17_2Percent) Add(scratch, "over17_2");
            if (s.Over33_8 > Over33_8Percent) Add(scratch, "over33_8");
            if (s.DrawCalls > DrawCalls) Add(scratch, "drawCalls");
            if (s.SetPass > SetPass) Add(scratch, "setPass");
            if (s.Triangles > Triangles) Add(scratch, "tris");
            if (s.CpuMainP95 > CpuMainP95Ms) Add(scratch, "cpuMain");
            if (s.CpuRenderP95 > CpuRenderP95Ms) Add(scratch, "cpuRender");
            bool hot = !cold && s.ThermalMax >= 1;
            if (s.GpuP95 > (hot ? GpuP95HotMs : GpuP95ColdMs)) Add(scratch, "gpu");
            if (s.MemMB > MemoryMB) Add(scratch, "mem");
            if (s.GcAllocB > GcAllocBytes) Add(scratch, "gcAlloc");
            if (cold && s.ThermalMax >= 1) Add(scratch, "notCold");
            s.Failed = scratch.ToString();
            mark = scratch.Length;

            scratch.Length = 0;
            if (!(s.CpuMainP95 > 0f)) Add(scratch, "cpuMain");
            if (!(s.CpuRenderP95 > 0f)) Add(scratch, "cpuRender");
            if (!(s.GpuP95 > 0f)) Add(scratch, "gpu");
            if (s.DrawCalls <= 0) Add(scratch, "drawCalls");
            if (s.SetPass <= 0) Add(scratch, "setPass");
            if (s.Triangles <= 0) Add(scratch, "tris");
            if (s.MemMB <= 0) Add(scratch, "mem");
            s.Unmeasured = scratch.ToString();

            s.Result = mark > 0 ? Fail : (scratch.Length > 0 ? Incomplete : Pass);
            scratch.Length = 0;
        }

        /// <summary>
        /// Judges the soak's total row (section 11.4.1, step 4, and the P1-A14 row): no Critical, Serious not
        /// before 10 minutes, at least 45 frames per second in the mean and never under 30.
        /// </summary>
        /// <param name="plannedSeconds">What the run was scheduled to last: under 15 minutes it is a rehearsal
        /// and cannot pass, only fail or be "incomplete".</param>
        public static void JudgeSoak(ref PerfSummary s, float plannedSeconds, StringBuilder scratch)
        {
            scratch.Length = 0;
            if (s.Frames <= 0) Add(scratch, "noFrames");
            if (s.TimeToCritical >= 0f || s.ThermalMax >= 3) Add(scratch, "critical");
            if (s.TimeToSerious >= 0f && s.TimeToSerious < SoakSeriousNotBeforeSeconds) Add(scratch, "seriousEarly");
            if (s.Frames > 0 && s.FpsMean < SoakFpsMean) Add(scratch, "fpsMean");
            if (s.Frames > 0 && s.FpsMin < SoakFpsMin) Add(scratch, "fpsMin");
            s.Failed = scratch.ToString();
            bool failed = scratch.Length > 0;
            bool rehearsal = plannedSeconds < SoakSeconds - 0.5f;
            s.Unmeasured = rehearsal ? "short" : "";
            s.Result = failed ? Fail : (rehearsal ? Incomplete : Pass);
            scratch.Length = 0;
        }

        /// <summary>Judges one forced tier: in force within a second.</summary>
        public static void JudgeTier(ref PerfSummary s)
        {
            s.Unmeasured = "";
            if (s.EffectMs < 0f) { s.Failed = "noEffect"; s.Result = Fail; }
            else if (s.EffectMs > TierEffectMs) { s.Failed = "slow"; s.Result = Fail; }
            else { s.Failed = ""; s.Result = Pass; }
        }

        static void Add(StringBuilder sb, string name)
        {
            if (sb.Length > 0) sb.Append('|');
            sb.Append(name);
        }
    }
}
