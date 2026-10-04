using UnityEngine;

namespace Atlas.Blades.FieldFit
{
    // Master Plan §10.4.5; Brief §5. P3-B03 stub that never fires: P3-B05 fills it (so B04 and B05 never edit FieldFitFeature.cs).
    internal sealed class LensGuard
    {
        public const float AutoOffDistanceM = 0.5f;          // §10.4.5 (design choice in the plan)
        public void Arm(Vector3 head) { }
        public bool Exceeded(Vector3 head, out float d) { d = 0f; return false; }
        public void RequestTapOff() { }
        public bool ConsumeTapOff() { return false; }
    }
}
