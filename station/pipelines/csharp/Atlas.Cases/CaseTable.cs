// ATLAS case contract (docs/case_contract.md, section 5): table.json reader and writer.
// Plain C#, System only, no Unity references.
using System.Collections.Generic;

namespace Atlas.Cases
{
    /// <summary>
    /// table.json, schema atlas.table/1. Volumes in cm3, lengths in patient LPS millimetres, rho in [0, 1].
    /// GatingCm3[s][l] is the volume of the union of the l + 1 consecutive phases s, s + 1, ..., s + l (mod 10).
    /// </summary>
    public sealed class CaseTable
    {
        public const string Schema = "atlas.table/1";
        public const int PhaseCount = 10;

        public string Id;
        public long Phases = PhaseCount;
        public long Substeps;
        public double[] GtvCm3;
        public double ItvCm3;
        public double CoreCm3;
        public double[][] GatingCm3;
        public double[][] CentroidMm;
        public double[] CentroidRangeMm;
        public double[] ArgmaxRhoMm;
        public double MaxRho;
        public double[] OriginMm;
        public long[] OccDims;
        public double[] OccBboxMinMm;
        public double[] OccBboxMaxMm;
        /// <summary>Optional free-form object, preserved as parsed. Null when absent.</summary>
        public Dictionary<string, object> Extra;

        static readonly string[] Required = {
            "schema", "id", "phases", "substeps", "gtv_cm3", "itv_cm3", "core_cm3", "gating_cm3", "centroid_mm",
            "centroid_range_mm", "argmax_rho_mm", "max_rho", "origin_mm", "occ" };
        static readonly string[] Optional = { "extra" };
        static readonly string[] OccRequired = { "dims", "bbox_min_mm", "bbox_max_mm" };
        static readonly string[] None = { };

        public static CaseTable Read(byte[] utf8) { return FromJson(CanonicalJson.Parse(utf8)); }

        public byte[] Write() { return CanonicalJson.Write(ToJson()); }

        public static CaseTable FromJson(object json)
        {
            const string W = "table.json";
            var o = CanonicalJson.AsObject(json, W);
            CanonicalJson.CheckKeys(o, Required, Optional, W);
            if (CanonicalJson.AsString(o["schema"], "schema") != Schema)
                throw new ContractException(W + ": schema must be " + Schema);
            var t = new CaseTable();
            t.Id = CanonicalJson.AsString(o["id"], "id");
            t.Phases = CanonicalJson.AsLong(o["phases"], "phases");
            if (t.Phases != PhaseCount) throw new ContractException(W + ": phases must be 10");
            t.Substeps = CanonicalJson.AsLong(o["substeps"], "substeps");
            t.GtvCm3 = CanonicalJson.AsDoubles(o["gtv_cm3"], PhaseCount, "gtv_cm3");
            t.ItvCm3 = CanonicalJson.AsDouble(o["itv_cm3"], "itv_cm3");
            t.CoreCm3 = CanonicalJson.AsDouble(o["core_cm3"], "core_cm3");
            t.GatingCm3 = CanonicalJson.AsDoubleRows(o["gating_cm3"], PhaseCount, PhaseCount, "gating_cm3");
            t.CentroidMm = CanonicalJson.AsDoubleRows(o["centroid_mm"], PhaseCount, 3, "centroid_mm");
            t.CentroidRangeMm = CanonicalJson.AsDoubles(o["centroid_range_mm"], 3, "centroid_range_mm");
            t.ArgmaxRhoMm = CanonicalJson.AsDoubles(o["argmax_rho_mm"], 3, "argmax_rho_mm");
            t.MaxRho = CanonicalJson.AsDouble(o["max_rho"], "max_rho");
            t.OriginMm = CanonicalJson.AsDoubles(o["origin_mm"], 3, "origin_mm");
            var occ = CanonicalJson.AsObject(o["occ"], "occ");
            CanonicalJson.CheckKeys(occ, OccRequired, None, W + " occ");
            var dims = CanonicalJson.AsArray(occ["dims"], "occ.dims");
            if (dims.Count != 3) throw new ContractException(W + ": occ.dims must be 3 positive integers");
            t.OccDims = new long[3];
            for (int i = 0; i < 3; i++) t.OccDims[i] = CanonicalJson.AsLong(dims[i], "occ.dims");
            t.OccBboxMinMm = CanonicalJson.AsDoubles(occ["bbox_min_mm"], 3, "occ.bbox_min_mm");
            t.OccBboxMaxMm = CanonicalJson.AsDoubles(occ["bbox_max_mm"], 3, "occ.bbox_max_mm");
            object extra;
            if (o.TryGetValue("extra", out extra)) t.Extra = CanonicalJson.AsObject(extra, "extra");
            t.Validate();
            return t;
        }

        public void Validate()
        {
            const string W = "table.json";
            if (Id == null) throw new ContractException(W + ": id missing");
            if (Phases != PhaseCount) throw new ContractException(W + ": phases must be 10");
            if (Substeps < 0) throw new ContractException(W + ": substeps must be >= 0");
            if (MaxRho < 0.0 || MaxRho > 1.0) throw new ContractException(W + ": max_rho must be in [0, 1]");
            if (GtvCm3 == null || GtvCm3.Length != PhaseCount) throw new ContractException(W + ": gtv_cm3 needs 10 values");
            if (GatingCm3 == null || GatingCm3.Length != PhaseCount) throw new ContractException(W + ": gating_cm3 needs 10 rows");
            foreach (var row in GatingCm3)
                if (row == null || row.Length != PhaseCount) throw new ContractException(W + ": gating_cm3 rows need 10 values");
            if (CentroidMm == null || CentroidMm.Length != PhaseCount) throw new ContractException(W + ": centroid_mm needs 10 rows");
            foreach (var row in CentroidMm)
                if (row == null || row.Length != 3) throw new ContractException(W + ": centroid_mm rows need 3 values");
            Need3(CentroidRangeMm, "centroid_range_mm"); Need3(ArgmaxRhoMm, "argmax_rho_mm"); Need3(OriginMm, "origin_mm");
            Need3(OccBboxMinMm, "occ.bbox_min_mm"); Need3(OccBboxMaxMm, "occ.bbox_max_mm");
            if (OccDims == null || OccDims.Length != 3 || OccDims[0] <= 0 || OccDims[1] <= 0 || OccDims[2] <= 0)
                throw new ContractException(W + ": occ.dims must be 3 positive integers");
        }

        static void Need3(double[] v, string what)
        {
            if (v == null || v.Length != 3) throw new ContractException("table.json: " + what + " needs 3 values");
        }

        public Dictionary<string, object> ToJson()
        {
            Validate();
            var occ = new Dictionary<string, object>();
            occ["dims"] = new List<object> { OccDims[0], OccDims[1], OccDims[2] };
            occ["bbox_min_mm"] = CanonicalJson.FromDoubles(OccBboxMinMm);
            occ["bbox_max_mm"] = CanonicalJson.FromDoubles(OccBboxMaxMm);
            var o = new Dictionary<string, object>();
            o["schema"] = Schema;
            o["id"] = Id;
            o["phases"] = Phases;
            o["substeps"] = Substeps;
            o["gtv_cm3"] = CanonicalJson.FromDoubles(GtvCm3);
            o["itv_cm3"] = ItvCm3;
            o["core_cm3"] = CoreCm3;
            o["gating_cm3"] = CanonicalJson.FromDoubleRows(GatingCm3);
            o["centroid_mm"] = CanonicalJson.FromDoubleRows(CentroidMm);
            o["centroid_range_mm"] = CanonicalJson.FromDoubles(CentroidRangeMm);
            o["argmax_rho_mm"] = CanonicalJson.FromDoubles(ArgmaxRhoMm);
            o["max_rho"] = MaxRho;
            o["origin_mm"] = CanonicalJson.FromDoubles(OriginMm);
            o["occ"] = occ;
            if (Extra != null) o["extra"] = Extra;
            return o;
        }
    }
}
