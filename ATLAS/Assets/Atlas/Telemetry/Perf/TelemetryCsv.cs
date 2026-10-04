using System.Text;
using Atlas.Link;

namespace Atlas.Telemetry
{
    /// <summary>
    /// The perf CSV (Master Plan section 7.3.3): one line per <c>Telemetry</c> row, the 34 fields in wire order.
    /// UTF-8, <c>\n</c> line ends, no quoting. Integers are decimal; every float has exactly three decimals,
    /// rounded half away from zero. The digits are written by hand, so a row costs no allocation and reads the
    /// same on every runtime and culture.
    /// <para>
    /// The row, the header and the three-decimal format are the link library's
    /// (<see cref="Atlas.Link.TelemetryCsv"/>), which the Hub writes its file with: this class forwards to it, so
    /// the phone's CSV and the Hub's are the same bytes. Only the writer with a free number of decimals, which
    /// the HUD and the menu use, lives here.
    /// </para>
    /// </summary>
    public static class TelemetryCsv
    {
        /// <summary>The header line, without its line end.</summary>
        public const string Header = Atlas.Link.TelemetryCsv.Header;

        public const int ColumnCount = Atlas.Link.TelemetryCsv.ColumnCount;

        /// <summary>Appends one row and its <c>\n</c>.</summary>
        public static void AppendRow(StringBuilder sb, in TelemetryMessage row)
        {
            Atlas.Link.TelemetryCsv.AppendRow(sb, in row);
        }

        /// <summary>Appends a whole number in decimal without allocating.</summary>
        public static void AppendInt(StringBuilder sb, long value)
        {
            Atlas.Link.TelemetryCsv.AppendInt(sb, value);
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
            if (decimals == 3)
            {
                // The format of the CSV: one writer for it, the link library's.
                Atlas.Link.TelemetryCsv.AppendFixed3(sb, value);
                return;
            }
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

        public static void AppendFixed3(StringBuilder sb, float value) { Atlas.Link.TelemetryCsv.AppendFixed3(sb, value); }
    }
}
