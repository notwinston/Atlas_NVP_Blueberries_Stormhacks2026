using Unity.Mathematics;

namespace Atlas.Spaces.SmallPlanet
{
    // One pose-filtered head sample (§6.3 step 3); .x and .z are the floor, .y height (unused).
    public readonly struct PlanetHead
    {
        public readonly double3 Position;
        public readonly bool TrackingNormal;
        public PlanetHead(double3 position, bool trackingNormal) { Position = position; TrackingNormal = trackingNormal; }
    }
}
