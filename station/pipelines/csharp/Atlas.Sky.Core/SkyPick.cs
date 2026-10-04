// Event payloads of the needle sky (Master Plan 10.2.6): sky_pick {az_deg, el_deg, clear_mm, robust}
// and gc_check {angle_sum_deg}. Plain structs; the adapter hands them to the Link's event writer.
using System;
using System.Globalization;
using System.Text;

namespace Atlas.Sky
{
    /// <summary>
    /// What the walker marked: the direction (patient frame, az/el as in <see cref="SkyFrame.DirectionToAzEl"/>), the
    /// minimum clearance over the ten phases at the pixel containing it, and whether that pixel is in the robust window.
    /// </summary>
    public readonly struct SkyPick
    {
        public readonly double AzDeg, ElDeg, ClearMm;
        public readonly bool Robust;
        public SkyPick(double azDeg, double elDeg, double clearMm, bool robust)
        {
            AzDeg = azDeg; ElDeg = elDeg; ClearMm = clearMm; Robust = robust;
        }

        /// <summary>{"az_deg":a,"el_deg":e,"clear_mm":c,"robust":b}: az, el with 2 decimals, clear_mm with 1 (the map's resolution is 0.1 mm).</summary>
        public string ToJson()
        {
            return EventPayload.SkyPickJson(this);
        }
    }

    /// <summary>The great-circle check result the walker sees and the event logs.</summary>
    public readonly struct GcCheck
    {
        public readonly double AngleSumDeg;
        public GcCheck(double angleSumDeg) { AngleSumDeg = angleSumDeg; }
        public string ToJson() { return EventPayload.GcCheckJson(this); }
    }

    public static class EventPayload
    {
        /// <summary>
        /// The names as the plan writes them. NOTE: docs/contracts/lane-d-link.md section 2.6 says an event name is
        /// "&lt;area&gt;.&lt;name&gt; or one word" with segments matching [a-z][A-Za-z0-9]*, which an underscore breaks. The plan's
        /// seven lung names (m2_place, ..., sky_pick, gc_check) all have one. Lane D has to settle that; the constants are here
        /// so that a rename is one edit.
        /// </summary>
        public const string SkyPickEvent = "sky_pick";
        public const string GcCheckEvent = "gc_check";

        public static string SkyPickJson(SkyPick p)
        {
            var sb = new StringBuilder(80);
            sb.Append("{\"az_deg\":").Append(F(p.AzDeg, "F2"))
              .Append(",\"el_deg\":").Append(F(p.ElDeg, "F2"))
              .Append(",\"clear_mm\":").Append(F(p.ClearMm, "F1"))
              .Append(",\"robust\":").Append(p.Robust ? "true" : "false").Append('}');
            return sb.ToString();
        }

        public static string GcCheckJson(GcCheck g)
        {
            return "{\"angle_sum_deg\":" + F(g.AngleSumDeg, "F2") + "}";
        }

        static string F(double v, string fmt)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException("event numbers must be finite");
            string s = v.ToString(fmt, CultureInfo.InvariantCulture);
            if (s.StartsWith("-0.") && IsZeros(s)) s = s.Substring(1); // no "-0.00"
            return s;
        }

        static bool IsZeros(string s)
        {
            for (int i = 1; i < s.Length; i++) if (s[i] != '0' && s[i] != '.') return false;
            return true;
        }
    }
}
