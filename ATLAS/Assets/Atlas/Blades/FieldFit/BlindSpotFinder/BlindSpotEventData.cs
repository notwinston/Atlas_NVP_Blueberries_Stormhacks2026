using System;

namespace Atlas.Blades.FieldFit.BlindSpotFinder
{
    // App. B Event data payloads (Brief §4, D8). Public snake_case fields for JsonUtility; never NaN or Infinity.
    [Serializable] public class BlindSpotStartData { public string eye; public uint seed; }

    [Serializable]
    public class BlindSpotProbeData
    {
        public int index; public string kind;   // grid, reprobe, catch
        public double az_deg, el_deg, onset_s; public bool seen; public double rt_ms;   // rt_ms -1 when not seen
    }

    [Serializable] public class BlindSpotFoundData { public double az_deg, el_deg, t_find_s; }

    [Serializable]
    public class BlindSpotDoneData
    {
        public string eye; public bool found, aborted;
        public double centre_az_deg, centre_el_deg, radius_deg, t_find_s;
        public int missed, catch_trials, catch_fp, trials;
        public double duration_s;
    }

    [Serializable]
    public class RiverStartData
    {
        public string eye; public double a_deg, centre_az_deg, centre_el_deg; public int glyphs;
        public double min_r_over_a, scale_above;
    }
}
