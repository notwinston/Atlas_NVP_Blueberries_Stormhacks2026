using System.Text;
using Atlas.Link;

namespace Atlas.Telemetry
{
    /// <summary>
    /// The text of the in-headset HUD (Master Plan section 8.8, task P1-D04): five short lines built from the
    /// last telemetry row and a few plain values. Engine-free, and written into a caller's
    /// <see cref="StringBuilder"/> digit by digit, so building it allocates nothing.
    /// </summary>
    public static class HudText
    {
        public const string SessionOver = "Session over";

        /// <summary>Replaces the builder's content with the five lines.</summary>
        /// <param name="row">The newest telemetry row.</param>
        /// <param name="link">The link state.</param>
        /// <param name="bumpLevel">The Bump Guard level now, 0 to 3.</param>
        /// <param name="sessionOver">The last rung of the thermal ladder was reached: a sixth line says so.</param>
        public static void Build(StringBuilder sb, in TelemetryMessage row, LinkState link, int bumpLevel, bool sessionOver = false)
        {
            sb.Length = 0;

            // 60 fps  p95 16.9  tier 0  therm 0
            TelemetryCsv.AppendFixed(sb, row.Fps, 0);
            sb.Append(" fps  p95 ");
            TelemetryCsv.AppendFixed(sb, row.P95, 1);
            sb.Append("  tier ");
            TelemetryCsv.AppendInt(sb, row.Tier);
            sb.Append("  therm ");
            TelemetryCsv.AppendInt(sb, row.Thermal);
            sb.Append('\n');

            // cpu 4.1 / 2.9  gpu 4.6 ms
            sb.Append("cpu ");
            TelemetryCsv.AppendFixed(sb, row.CpuMainP95, 1);
            sb.Append(" / ");
            TelemetryCsv.AppendFixed(sb, row.CpuRenderP95, 1);
            sb.Append("  gpu ");
            TelemetryCsv.AppendFixed(sb, row.GpuP95, 1);
            sb.Append(" ms\n");

            // draws 142  sp 22  tris 231k
            sb.Append("draws ");
            TelemetryCsv.AppendInt(sb, row.DrawCalls);
            sb.Append("  sp ");
            TelemetryCsv.AppendInt(sb, row.SetPass);
            sb.Append("  tris ");
            TelemetryCsv.AppendInt(sb, row.TrisK);
            sb.Append("k\n");

            // mem 612 MB  bat 87  gc 0
            sb.Append("mem ");
            TelemetryCsv.AppendInt(sb, row.MemMB);
            sb.Append(" MB  bat ");
            if (row.Battery == 255) sb.Append('-');
            else TelemetryCsv.AppendInt(sb, row.Battery);
            sb.Append("  gc ");
            TelemetryCsv.AppendInt(sb, row.GcAllocB);
            sb.Append('\n');

            // S1  link 3 ms 0.0%  bump 0  assets 3/1
            sb.Append('S');
            TelemetryCsv.AppendInt(sb, row.SpaceId);
            sb.Append("  link ");
            if (link == LinkState.Offline) sb.Append("offline");
            else
            {
                if (link == LinkState.Resync) sb.Append("resync ");
                else if (link == LinkState.Degraded) sb.Append("degraded ");
                TelemetryCsv.AppendInt(sb, row.RttMs);
                sb.Append(" ms ");
                TelemetryCsv.AppendFixed(sb, row.LossPermille * 0.1f, 1);
                sb.Append('%');
            }
            sb.Append("  bump ");
            TelemetryCsv.AppendInt(sb, bumpLevel);
            sb.Append("  assets ");
            TelemetryCsv.AppendInt(sb, row.AssetsPlaced);
            sb.Append('/');
            TelemetryCsv.AppendInt(sb, row.AssetsPending);

            if (sessionOver) sb.Append('\n').Append(SessionOver);
        }
    }
}
