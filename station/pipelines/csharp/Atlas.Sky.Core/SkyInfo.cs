// Reader of sky/sky.json (docs/case_contract.md section 9.4, schema atlas.sky/1). Work package P2-B10.
// Uses the generic parser of Atlas.Cases.CanonicalJson; reads only what the runtime needs.
using System.Collections.Generic;
using Atlas.Cases;

namespace Atlas.Sky
{
    /// <summary>One of the 20 test directions sky.json carries ("checks"), with the clearance the baker decoded.</summary>
    public sealed class SkyCheck
    {
        public int Col, Row, Healpix;
        public double[] Dir;            // unit vector, patient frame, 6 decimals
        public int[] Value;             // risk byte per phase
        public double[] ClearanceMm;    // decoded bytes per phase
        public double MinClearanceMm;   // min over phases
        public bool Robust;
    }

    public sealed class SkyInfo
    {
        public const string SchemaName = "atlas.sky/1";

        public string Id, Label;
        public int Width, Height, Phases;
        public int Nside, Npix;
        public string Ordering;
        public string[] RiskFiles;
        public string RobustFile;
        public double MmPerCount;       // clearance_mm = value * MmPerCount (floor-rounded, so a lower bound within one count)
        public double ClearanceMaxMm;   // a byte of 255 means this or more
        public double ThresholdMm;
        public int ThresholdValue;
        public string ThresholdLabel;
        public double[] TargetMm;       // patient frame
        public double RobustShare;      // stats.robust_share: share of the sphere (solid angle), or -1 when absent
        public SkyCheck[] Checks;

        public static SkyInfo Parse(byte[] utf8)
        {
            return FromObject(CanonicalJson.AsObject(CanonicalJson.Parse(utf8), "sky.json"));
        }

        public static SkyInfo Parse(string text)
        {
            return FromObject(CanonicalJson.AsObject(CanonicalJson.Parse(text), "sky.json"));
        }

        static SkyInfo FromObject(Dictionary<string, object> o)
        {
            const string W = "sky.json";
            string schema = CanonicalJson.AsString(CanonicalJson.Need(o, "schema", W), "schema");
            if (schema != SchemaName) throw new ContractException("sky.json: schema is " + schema + ", expected " + SchemaName);

            var s = new SkyInfo();
            s.Id = CanonicalJson.AsString(CanonicalJson.Need(o, "id", W), "id");
            s.Label = o.ContainsKey("label") ? CanonicalJson.AsString(o["label"], "label") : "";
            s.Width = Positive(CanonicalJson.AsLong(CanonicalJson.Need(o, "width", W), "width"), "width");
            s.Height = Positive(CanonicalJson.AsLong(CanonicalJson.Need(o, "height", W), "height"), "height");
            s.Phases = Positive(CanonicalJson.AsLong(CanonicalJson.Need(o, "phases", W), "phases"), "phases");
            s.Nside = Positive(CanonicalJson.AsLong(CanonicalJson.Need(o, "nside", W), "nside"), "nside");
            s.Npix = Positive(CanonicalJson.AsLong(CanonicalJson.Need(o, "npix", W), "npix"), "npix");
            if (s.Npix != HealpixRing.NPix(s.Nside)) throw new ContractException("sky.json: npix is not 12 nside^2");
            s.Ordering = CanonicalJson.AsString(CanonicalJson.Need(o, "ordering", W), "ordering");
            if (s.Ordering != "ring") throw new ContractException("sky.json: ordering is " + s.Ordering + ", this reader implements ring");

            var files = CanonicalJson.AsObject(CanonicalJson.Need(o, "files", W), "files");
            var risk = CanonicalJson.AsArray(CanonicalJson.Need(files, "risk", "files"), "files.risk");
            if (risk.Count != s.Phases) throw new ContractException("sky.json: files.risk does not list one file per phase");
            s.RiskFiles = new string[risk.Count];
            for (int i = 0; i < risk.Count; i++) s.RiskFiles[i] = SafeName(CanonicalJson.AsString(risk[i], "files.risk"));
            s.RobustFile = SafeName(CanonicalJson.AsString(CanonicalJson.Need(files, "robust", "files"), "files.robust"));

            var enc = CanonicalJson.AsObject(CanonicalJson.Need(o, "encoding", W), "encoding");
            if (CanonicalJson.AsString(CanonicalJson.Need(enc, "type", "encoding"), "encoding.type") != "u8")
                throw new ContractException("sky.json: encoding.type must be u8");
            if (CanonicalJson.AsString(CanonicalJson.Need(enc, "rounding", "encoding"), "encoding.rounding") != "floor")
                throw new ContractException("sky.json: encoding.rounding must be floor");
            s.MmPerCount = CanonicalJson.AsDouble(CanonicalJson.Need(enc, "mm_per_count", "encoding"), "encoding.mm_per_count");
            if (!(s.MmPerCount > 0.0)) throw new ContractException("sky.json: mm_per_count must be positive");
            s.ClearanceMaxMm = CanonicalJson.AsDouble(CanonicalJson.Need(enc, "clearance_max_mm", "encoding"), "encoding.clearance_max_mm");

            s.ThresholdMm = CanonicalJson.AsDouble(CanonicalJson.Need(o, "threshold_mm", W), "threshold_mm");
            s.ThresholdValue = (int)CanonicalJson.AsLong(CanonicalJson.Need(o, "threshold_value", W), "threshold_value");
            s.ThresholdLabel = CanonicalJson.AsString(CanonicalJson.Need(o, "threshold_label", W), "threshold_label");
            s.TargetMm = CanonicalJson.AsDoubles(CanonicalJson.Need(o, "target_mm", W), 3, "target_mm");

            s.RobustShare = -1.0;
            if (o.ContainsKey("stats"))
            {
                var st = CanonicalJson.AsObject(o["stats"], "stats");
                if (st.ContainsKey("robust_share")) s.RobustShare = CanonicalJson.AsDouble(st["robust_share"], "stats.robust_share");
            }

            var checks = CanonicalJson.AsArray(CanonicalJson.Need(o, "checks", W), "checks");
            s.Checks = new SkyCheck[checks.Count];
            for (int i = 0; i < checks.Count; i++)
            {
                var c = CanonicalJson.AsObject(checks[i], "checks[" + i + "]");
                var k = new SkyCheck();
                k.Col = (int)CanonicalJson.AsLong(CanonicalJson.Need(c, "col", "check"), "col");
                k.Row = (int)CanonicalJson.AsLong(CanonicalJson.Need(c, "row", "check"), "row");
                k.Healpix = (int)CanonicalJson.AsLong(CanonicalJson.Need(c, "healpix", "check"), "healpix");
                k.Dir = CanonicalJson.AsDoubles(CanonicalJson.Need(c, "dir", "check"), 3, "dir");
                k.ClearanceMm = CanonicalJson.AsDoubles(CanonicalJson.Need(c, "clearance_mm", "check"), s.Phases, "clearance_mm");
                var vals = CanonicalJson.AsArray(CanonicalJson.Need(c, "value", "check"), "value");
                if (vals.Count != s.Phases) throw new ContractException("sky.json: check value does not have one entry per phase");
                k.Value = new int[vals.Count];
                for (int j = 0; j < vals.Count; j++) k.Value[j] = (int)CanonicalJson.AsLong(vals[j], "value");
                k.MinClearanceMm = CanonicalJson.AsDouble(CanonicalJson.Need(c, "min_clearance_mm", "check"), "min_clearance_mm");
                object rb = CanonicalJson.Need(c, "robust", "check");
                if (!(rb is bool)) throw new ContractException("sky.json: check robust must be a boolean");
                k.Robust = (bool)rb;
                s.Checks[i] = k;
            }
            return s;
        }

        static int Positive(long v, string what)
        {
            if (v < 1 || v > 1 << 20) throw new ContractException("sky.json: " + what + " out of range");
            return (int)v;
        }

        /// <summary>A file name from sky.json is joined to the sky directory, so it must be a plain name.</summary>
        static string SafeName(string n)
        {
            if (n.Length == 0 || n.IndexOf('/') >= 0 || n.IndexOf('\\') >= 0 || n == "." || n == "..")
                throw new ContractException("sky.json: file name is not a plain name: " + n);
            return n;
        }
    }
}
