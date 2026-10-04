using System.Text;
using Atlas.Telemetry;

namespace Atlas.App
{
    /// <summary>
    /// The tour's own file, <c>tour_&lt;session&gt;.csv</c>, next to the probe's <c>perf_&lt;session&gt;.csv</c>:
    /// lines that start with '#' carry the run's facts and notes, then one header and one row per space, per
    /// soak or per forced tier. UTF-8, <c>\n</c> line ends, no quoting; numbers are written as the perf CSV
    /// writes them, so the file reads the same on every culture. The same row goes out as the data of a
    /// <c>perf.summary</c> event, which is how it reaches the Hub. Engine-free.
    /// </summary>
    public static class PerfTourCsv
    {
        /// <summary>The first line of every file.</summary>
        public const string Magic = "# atlas perf tour v1";

        public const string Header =
            "run,kind,label,space,variant,room,pose,depth,seconds,frames,p50,p95,p99,max,over17_2,over33_8," +
            "fpsMean,fpsMin,drawCalls,setPass,tris,cpuMainP95,cpuRenderP95,gpuP95,memMB,gcAllocB,gcAllocMaxB," +
            "thermalMax,tFairS,tSeriousS,tCriticalS,tier,effectMs,result,failed,unmeasured";

        public const int ColumnCount = 36;

        public const string SummaryEvent = "perf.summary";
        public const string NoteEvent = "perf.note";
        public const string BeginEvent = "perf.runBegin";
        public const string EndEvent = "perf.runEnd";

        /// <summary>One comment line, with its line end: "# key=value".</summary>
        public static void AppendFact(StringBuilder sb, string key, string value)
        {
            sb.Append("# ").Append(key).Append('=');
            AppendText(sb, value);
            sb.Append('\n');
        }

        /// <summary>One row, with its line end.</summary>
        public static void AppendRow(StringBuilder sb, in PerfSummary s)
        {
            Write(sb, in s, false);
            sb.Append('\n');
        }

        /// <summary>The row as one JSON object, member names as the header's columns.</summary>
        public static void AppendJson(StringBuilder sb, in PerfSummary s)
        {
            sb.Append('{');
            Write(sb, in s, true);
            sb.Append('}');
        }

        static void Write(StringBuilder sb, in PerfSummary s, bool json)
        {
            Text(sb, json, "run", s.Run, true);
            Text(sb, json, "kind", s.Kind);
            Text(sb, json, "label", s.Label);
            Text(sb, json, "space", s.Space);
            Text(sb, json, "variant", s.Variant);
            Text(sb, json, "room", s.Room);
            Text(sb, json, "pose", s.Pose);
            Text(sb, json, "depth", s.Depth);
            Fixed(sb, json, "seconds", s.Seconds, 1);
            Whole(sb, json, "frames", s.Frames);
            Fixed(sb, json, "p50", s.P50, 3);
            Fixed(sb, json, "p95", s.P95, 3);
            Fixed(sb, json, "p99", s.P99, 3);
            Fixed(sb, json, "max", s.Max, 3);
            Fixed(sb, json, "over17_2", s.Over17_2, 3);
            Fixed(sb, json, "over33_8", s.Over33_8, 3);
            Fixed(sb, json, "fpsMean", s.FpsMean, 3);
            Fixed(sb, json, "fpsMin", s.FpsMin, 3);
            Whole(sb, json, "drawCalls", s.DrawCalls);
            Whole(sb, json, "setPass", s.SetPass);
            Whole(sb, json, "tris", s.Triangles);
            Fixed(sb, json, "cpuMainP95", s.CpuMainP95, 3);
            Fixed(sb, json, "cpuRenderP95", s.CpuRenderP95, 3);
            Fixed(sb, json, "gpuP95", s.GpuP95, 3);
            Whole(sb, json, "memMB", s.MemMB);
            Whole(sb, json, "gcAllocB", s.GcAllocB);
            Whole(sb, json, "gcAllocMaxB", s.GcAllocMaxB);
            Whole(sb, json, "thermalMax", s.ThermalMax);
            Fixed(sb, json, "tFairS", s.TimeToFair, 1);
            Fixed(sb, json, "tSeriousS", s.TimeToSerious, 1);
            Fixed(sb, json, "tCriticalS", s.TimeToCritical, 1);
            Whole(sb, json, "tier", s.Tier);
            Fixed(sb, json, "effectMs", s.EffectMs, 1);
            Text(sb, json, "result", s.Result);
            Text(sb, json, "failed", s.Failed);
            Text(sb, json, "unmeasured", s.Unmeasured);
        }

        static void Name(StringBuilder sb, bool json, string name, bool first)
        {
            if (!first) sb.Append(',');
            if (json) sb.Append('"').Append(name).Append("\":");
        }

        static void Text(StringBuilder sb, bool json, string name, string value, bool first = false)
        {
            Name(sb, json, name, first);
            if (json) sb.Append('"');
            AppendText(sb, value);
            if (json) sb.Append('"');
        }

        static void Whole(StringBuilder sb, bool json, string name, long value)
        {
            Name(sb, json, name, false);
            TelemetryCsv.AppendInt(sb, value);
        }

        static void Fixed(StringBuilder sb, bool json, string name, float value, int decimals)
        {
            Name(sb, json, name, false);
            TelemetryCsv.AppendFixed(sb, value, decimals);
        }

        // The file has no quoting: a comma, a quote, a backslash or a line end in a name becomes a space.
        static void AppendText(StringBuilder sb, string value)
        {
            if (value == null) return;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                sb.Append(c == ',' || c == '"' || c == '\\' || c < ' ' ? ' ' : c);
            }
        }
    }
}
