using UnityEngine;

namespace Atlas.Blades.FieldFit
{
    /// <summary>
    /// Auto-off after 0.5 m of head displacement and tap-off. Master Plan §10.4.5 (the 0.5 m is a design choice in the plan);
    /// Brief §5, DV10: 3D distance from the head position at lens-on, strictly greater than 0.5 m.
    /// </summary>
    internal sealed class LensGuard
    {
        public const float AutoOffDistanceM = 0.5f;          // §10.4.5
        Vector3 start;
        bool tapRequested;

        public void Arm(Vector3 head) { start = head; tapRequested = false; }

        public bool Exceeded(Vector3 head, out float d)
        {
            d = (head - start).magnitude;
            return d > AutoOffDistanceM;
        }

        public void RequestTapOff() { tapRequested = true; }

        public bool ConsumeTapOff() { bool t = tapRequested; tapRequested = false; return t; }
    }
}
