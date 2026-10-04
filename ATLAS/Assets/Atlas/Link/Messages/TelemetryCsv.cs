using System;
using System.Text;

namespace Atlas.Link
{
    /// <summary>
    /// The perf CSV (Master Plan section 7.3.3): the header line and one line per <c>Telemetry</c> row, the 34
    /// fields in wire order under their wire names. UTF-8, <c>\n</c> line ends, no quoting, no trailing comma.
    /// Integers are written in decimal; every float with exactly three decimals, rounded half away from zero
    /// from its value as a double.
    /// <para>
    /// The phone and the Hub both write their file through this class, and the digits are produced here rather
    /// than by the runtime's formatter, so the two files are the same bytes on Mono, IL2CPP and .NET 8 under any
    /// culture (test L11). Appending a row to a builder that has the room allocates nothing.
    /// </para>
    /// </summary>
    public static class TelemetryCsv
    {
        /// <summary>The header line, without its line end.</summary>
        public const string Header = TelemetryMessage.CsvHeader;

        /// <summary>Columns of the header and of every row.</summary>
        public const int ColumnCount = 34;

        /// <summary>Longest row there can be, line end included: a builder of this capacity never grows.</summary>
        public const int MaxRowLength = 384;

        // Above this a float no longer has three meaningful decimals and its thousandths would not fit a long.
        const double LargestMagnitude = 9.0e12;

        /// <summary>Appends the header line with its <c>\n</c>.</summary>
        public static void AppendHeader(StringBuilder sb)
        {
            if (sb == null) throw new ArgumentNullException("sb");
            sb.Append(Header).Append('\n');
        }

        /// <summary>Appends one row with its <c>\n</c>.</summary>
        public static void AppendRow(StringBuilder sb, in TelemetryMessage row)
        {
            if (sb == null) throw new ArgumentNullException("sb");
            Int(sb, row.TimeUs);
            Int(sb, row.Seq);
            Int(sb, row.SceneId);
            Int(sb, row.SpaceId);
            Int(sb, row.Tier);
            Fixed(sb, row.Fps);
            Fixed(sb, row.P50);
            Fixed(sb, row.P95);
            Fixed(sb, row.P99);
            Fixed(sb, row.Max);
            Fixed(sb, row.Over17_2);
            Fixed(sb, row.Over33_8);
            Fixed(sb, row.CpuMainP95);
            Fixed(sb, row.CpuRenderP95);
            Fixed(sb, row.GpuP95);
            Fixed(sb, row.RenderScale);
            Int(sb, row.DrawCalls);
            Int(sb, row.SetPass);
            Int(sb, row.TrisK);
            Int(sb, row.MemMB);
            Int(sb, row.VisibleTiles);
            Int(sb, row.Copies);
            Int(sb, row.RttMs);
            Int(sb, row.LossPermille);
            Int(sb, row.CueCount);
            Int(sb, row.GcAllocB);
            Int(sb, row.Thermal);
            Int(sb, row.Battery);
            Int(sb, row.Tracking);
            Int(sb, row.Msaa);
            Int(sb, row.StateHz);
            Int(sb, row.BumpMax);
            Int(sb, row.AssetsPlaced);
            AppendInt(sb, row.AssetsPending);
            sb.Append('\n');
        }

        static void Int(StringBuilder sb, long value)
        {
            AppendInt(sb, value);
            sb.Append(',');
        }

        static void Fixed(StringBuilder sb, float value)
        {
            AppendFixed3(sb, value);
            sb.Append(',');
        }

        /// <summary>Appends a whole number in decimal.</summary>
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

        /// <summary>
        /// Appends a number with exactly three decimals, rounded half away from zero from the float's value as a
        /// double: <c>60.000</c>, <c>16.667</c>, <c>0.800</c>. A value that is not a number is written as
        /// <c>0.000</c>, and a value that rounds to zero carries no sign.
        /// </summary>
        public static void AppendFixed3(StringBuilder sb, float value)
        {
            double v = value;
            if (double.IsNaN(v) || double.IsInfinity(v)) v = 0.0;
            bool negative = v < 0.0;
            double magnitude = negative ? -v : v;
            if (magnitude > LargestMagnitude) magnitude = LargestMagnitude;
            long thousandths = (long)Math.Floor(magnitude * 1000.0 + 0.5);
            if (negative && thousandths != 0) sb.Append('-');
            AppendDigits(sb, (ulong)(thousandths / 1000));
            sb.Append('.');
            int fraction = (int)(thousandths % 1000);
            sb.Append((char)('0' + fraction / 100));
            sb.Append((char)('0' + fraction / 10 % 10));
            sb.Append((char)('0' + fraction % 10));
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
    }
}
