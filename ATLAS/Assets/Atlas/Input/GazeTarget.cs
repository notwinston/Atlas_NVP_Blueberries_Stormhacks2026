// PLACEHOLDER (lane C, P1-C10): contract surface only; replaced by lane C's implementation
using UnityEngine;

namespace Atlas.Input
{
    // Master Plan §10.1.3, §10.2.2 (placeholder choice PH-28).
    public sealed class GazeTarget : MonoBehaviour
    {
        public const float DwellSeconds = 0.7f;   // Master Plan §10.1.3 (P1-C10 range 0.6-0.8 s)
        public AtlasAction Action;                // the action this target twins
        public event System.Action Dwelled;
        public void Dwell() => Dwelled?.Invoke(); // called by lane C's reticle when the dwell completes
    }
}
