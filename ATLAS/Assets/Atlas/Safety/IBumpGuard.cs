using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Safety
{
    /// <summary>Bump Guard as the frame loop sees it (Master Plan section 6.5).</summary>
    public interface IBumpGuard
    {
        /// <summary>The level to show. Rises at once, relaxes after 0.05 m and 0.3 s of margin.</summary>
        BumpLevel Level { get; }
        /// <summary>
        /// Towards the hazard, in head space: x to the walker's right, y ahead along the head's heading (pitch
        /// and roll left out). Unit length, or zero when the level is Clear or no direction is known.
        /// </summary>
        float2 Direction { get; }
        /// <summary>Live depth and the static map disagree (section 4.10).</summary>
        bool RescanAdvised { get; }
        /// <summary>The first call of the frame, with the raw pose. Never awaits.</summary>
        void Tick(in SensorFrame sensors);
    }
}
