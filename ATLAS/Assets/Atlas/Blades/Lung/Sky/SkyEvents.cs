using Atlas.Link;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>Event sink; scene is "lung" (MP §10.2.6). dataJson is one JSON object as text.</summary>
    public interface ISkyEvents { void Emit(string ev, string dataJson); }

    /// <summary>MP §10.2.6 sky_pick {az_deg, el_deg, clear_mm, robust}.</summary>
    public struct SkyPickData
    {
        public float az_deg, el_deg, clear_mm; public bool robust;

        public JsonValue ToJson() => JsonValue.Object().Set("az_deg", az_deg).Set("el_deg", el_deg).Set("clear_mm", clear_mm).Set("robust", robust);

        public static SkyPickData FromJson(JsonValue j) => new SkyPickData
        {
            az_deg = j.GetFloat("az_deg"), el_deg = j.GetFloat("el_deg"), clear_mm = j.GetFloat("clear_mm"), robust = j.GetBool("robust"),
        };
    }

    /// <summary>MP §10.2.6 gc_check {angle_sum_deg} (B11).</summary>
    public struct GcCheckData
    {
        public float angle_sum_deg;

        public JsonValue ToJson() => JsonValue.Object().Set("angle_sum_deg", angle_sum_deg);

        public static GcCheckData FromJson(JsonValue j) => new GcCheckData { angle_sum_deg = j.GetFloat("angle_sum_deg") };
    }
}
