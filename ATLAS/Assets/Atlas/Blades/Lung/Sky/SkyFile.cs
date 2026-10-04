using System;
using System.IO;
using Atlas.Link;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>One sky.json check direction (MP §10.2.4 step 8; fields = placeholder fixture, PH-37).</summary>
    public sealed class SkyCheck { public float az_deg, el_deg, clear_mm; public float[] clear_mm_phase; }

    /// <summary>sky.json reader, schema atlas.sky/1 (MP §10.2.4 step 8; PH-30: lane B writes this reader).</summary>
    public sealed class SkyFile
    {
        public string schema; public int width, height, nside, phases; public float threshold_mm, mm_per_unit;
        public float[] target_mm; public SkyCheck[] checks;

        public static SkyFile Parse(string json)
        {
            if (!AtlasJson.TryParse(json, out JsonValue j) || !j.IsObject) throw new InvalidDataException("sky.json: not a JSON object");
            var f = new SkyFile
            {
                schema = j.GetString("schema"), width = j.GetInt("width"), height = j.GetInt("height"), nside = j.GetInt("nside"),
                phases = j.GetInt("phases"), threshold_mm = j.GetFloat("threshold_mm"), mm_per_unit = j.GetFloat("mm_per_unit"),
                target_mm = Floats(j["target_mm"]) ?? new float[0], checks = new SkyCheck[0],
            };
            JsonValue checks = j["checks"];
            if (checks.IsArray)
            {
                f.checks = new SkyCheck[checks.Count];
                for (int i = 0; i < f.checks.Length; i++)
                {
                    JsonValue c = checks[i];
                    f.checks[i] = new SkyCheck { az_deg = c.GetFloat("az_deg"), el_deg = c.GetFloat("el_deg"), clear_mm = c.GetFloat("clear_mm"), clear_mm_phase = Floats(c["clear_mm_phase"]) };
                }
            }
            f.Validate();
            return f;
        }

        static float[] Floats(JsonValue a)
        {
            if (!a.IsArray) return null;
            var v = new float[a.Count];
            for (int i = 0; i < v.Length; i++) v[i] = a[i].AsFloat();
            return v;
        }

        public void Validate()
        {
            if (schema != "atlas.sky/1") throw new InvalidDataException("sky.json schema: '" + schema + "', expected 'atlas.sky/1'");
            if (width != NeedleSkyConstants.Width) throw new InvalidDataException("sky.json width: " + width + ", expected 512");
            if (height != NeedleSkyConstants.Height) throw new InvalidDataException("sky.json height: " + height + ", expected 256");
            if (phases != NeedleSkyConstants.Phases) throw new InvalidDataException("sky.json phases: " + phases + ", expected 10");
            if (nside != NeedleSkyConstants.NsideExpected) throw new InvalidDataException("sky.json nside: " + nside + ", expected 64");
            if (Math.Abs(threshold_mm - NeedleSkyConstants.ThresholdMm) > 1e-6) throw new InvalidDataException("sky.json threshold_mm: " + threshold_mm + ", expected 5");
            if (!(mm_per_unit > 0f)) throw new InvalidDataException("sky.json mm_per_unit: " + mm_per_unit + ", expected > 0");
        }
    }
}
