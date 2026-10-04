using System;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>Event sink; scene is "lung" (MP §10.2.6).</summary>
    public interface ISkyEvents { void Emit(string ev, string dataJson); }

    /// <summary>MP §10.2.6 sky_pick {az_deg, el_deg, clear_mm, robust}.</summary>
    [Serializable] public struct SkyPickData { public float az_deg, el_deg, clear_mm; public bool robust; }

    /// <summary>MP §10.2.6 gc_check {angle_sum_deg} (B11).</summary>
    [Serializable] public struct GcCheckData { public float angle_sum_deg; }
}
