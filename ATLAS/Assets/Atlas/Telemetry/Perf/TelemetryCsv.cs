using System.Text;
using Atlas.Link;

namespace Atlas.Telemetry
{
    /// <summary>
    /// The perf CSV (Master Plan section 7.3.3): one line per <c>Telemetry</c> row, the 34 fields in wire order.
    /// UTF-8, <c>\n</c> line ends, no quoting. Integers are decimal; every float has exactly three decimals,
    /// rounded half away from zero. The digits are written by hand, so a row costs no allocation and reads the
    /// same on every runtime and culture.
    /// </summary>
    public static class TelemetryCsv
    {
        /// <summary>The header line, without its line end.</summary>
        public const string Header = TelemetryMessage.CsvHeader;

        public const int ColumnCount = 34;

        /// <summary>Appends one row and its <c>\n</c>.</summary>
        public static void AppendRow(StringBuilder sb, in TelemetryMessage row)
        {
            AppendInt(sb, row.TimeUs); sb.Append(',');
            AppendInt(sb, row.Seq); sb.Append(',');
            AppendInt(sb, row.SceneId); sb.Append(',');
            AppendInt(sb, row.SpaceId); sb.Append(',');
            AppendInt(sb, row.Tier); sb.Append(',');
            AppendFixed3(sb, row.Fps); sb.Append(',');
            AppendFixed3(sb, row.P50); sb.Append(',');
            AppendFixed3(sb, row.P95); sb.Append(',');
            AppendFixed3(sb, row.P99); sb.Append(',');
            AppendFixed3(sb, row.Max); sb.Append(',');
            AppendFixed3(sb, row.Over17_2); sb.Append(',');
            AppendFixed3(sb, row.Over33_8); sb.Append(',');
            AppendFixed3(sb, row.CpuMainP95); sb.Append(',');
            AppendFixed3(sb, row.CpuRenderP95); sb.Append(',');
            AppendFixed3(sb, row.GpuP95); sb.Append(',');
            AppendFixed3(sb, row.RenderScale); sb.Append(',');
            AppendInt(sb, row.DrawCalls); sb.Append(',');
            AppendInt(sb, row.SetPass); sb.Append(',');
            AppendInt(sb, row.TrisK); sb.Append(',');
            AppendInt(sb, row.MemMB); sb.Append(',');
            AppendInt(sb, row.VisibleTiles); sb.Append(',');
            AppendInt(sb, row.Copies); sb.Append(',');
            AppendInt(sb, row.RttMs); sb.Append(',');
            AppendInt(sb, row.LossPermille); sb.Append(',');
            AppendInt(sb, row.CueCount); sb.Append(',');
            AppendInt(sb, row.GcAllocB); sb.Append(',');
            AppendInt(sb, row.Thermal); sb.Append(',');
            AppendInt(sb, row.Battery); sb.Append(',');
            AppendInt(sb, row.Tracking); sb.Append(',');
            AppendInt(sb, row.Msaa); sb.Append(',');
            AppendInt(sb, row.StateHz); sb.Append(',');
            AppendInt(sb, row.BumpMax); sb.Append(',');
            AppendInt(sb, row.AssetsPlaced); sb.Append(',');
            AppendInt(sb, row.AssetsPending);
            sb.Append('\n');
        }

        /// <summary>Appends a whole number in decimal without allocating.</summary>
        public static void AppendInt(StringBuilder sb, long value)
        {
            if (value < 0)
            {
                sb.Append('-');
                // long.MinValue has no positive counterpart; its magnitude fits a ulong.
                AppendDigits(sb, (ulong)(-(value + 1)) + 1UL);
                return;
            }
            AppendDigits(sb, (ulong)value);
        }

        static void AppendDigits(StringBuilder sb, ulong value)
        {
            ulong divisor = 1;
            while (value / divisor >= 10) divisor *= 10;
            while (divisor > 0)
            {
                sb.Append((char)('0' + (int)(value / divisor % 10)));
                divisor /= 10;
            }
        }

        /// <summary>Appends a number with <paramref name="decimals"/> digits after the point, rounded half away from zero.</summary>
        public static void AppendFixed(StringBuilder sb, float value, int decimals)
        {
            double v = value;
            if (double.IsNaN(v) || double.IsInfinity(v)) v = 0.0;
            long scale = 1;
            for (int i = 0; i < decimals; i++) scale *= 10;
            bool negative = v < 0.0;
            double magnitude = negative ? -v : v;
            if (magnitude > 9.0e15 / scale) magnitude = 9.0e15 / scale;
            long scaled = (long)System.Math.Floor(magnitude * scale + 0.5);
            if (negative && scaled != 0) sb.Append('-');
            AppendDigits(sb, (ulong)(scaled / scale));
            if (decimals <= 0) return;
            sb.Append('.');
            long fraction = scaled % scale;
            for (long d = scale / 10; d > 0; d /= 10) sb.Append((char)('0' + (int)(fraction / d % 10)));
        }

        public static void AppendFixed3(StringBuilder sb, float value) { AppendFixed(sb, value, 3); }
    }
}
