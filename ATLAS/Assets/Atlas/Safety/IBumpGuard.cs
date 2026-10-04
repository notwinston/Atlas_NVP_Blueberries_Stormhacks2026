// PLACEHOLDER (lane C, P1-C09): contract surface only; replaced by lane C's implementation
using Unity.Mathematics;

namespace Atlas.Safety
{
    // Master Plan §6.5, verbatim.
    public enum BumpLevel { Clear, Warn, Strong, TrackingLost }

    public struct SensorFrame { }   // fields are lane C's (PH-26)

    public interface IBumpGuard
    {
        BumpLevel Level { get; }
        float2 Direction { get; }
        bool RescanAdvised { get; }
        void Tick(in SensorFrame sensors);
    }
}
