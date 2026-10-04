using System;
using System.IO;
using UnityEngine;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>One sky.json check direction (MP §10.2.4 step 8; fields = placeholder fixture, PH-37).</summary>
    [Serializable] public sealed class SkyCheck { public float az_deg, el_deg, clear_mm; public float[] clear_mm_phase; }

    /// <summary>sky.json reader, schema atlas.sky/1 (MP §10.2.4 step 8; PH-30: lane B writes this reader).</summary>
    [Serializable]
    public sealed class SkyFile
    {
        public string schema; public int width, height, nside, phases; public float threshold_mm, mm_per_unit;
        public float[] target_mm; public SkyCheck[] checks;

        public static SkyFile Parse(string json)
        {
            var f = JsonUtility.FromJson<SkyFile>(json);
            if (f == null) throw new InvalidDataException("sky.json: not a JSON object");
            f.Validate();
            return f;
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
