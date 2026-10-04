using Unity.Mathematics;

namespace Atlas.Kernel
{
    // Lane-B plain input type (P1-B02 deviation Dv1): one filtered head pose read in frame `Frame` (Master Plan §6.3 step 1).
    public readonly struct HeadSample
    {
        public readonly long Frame;
        public readonly double Time;
        public readonly double3 Position; // Unity world, metres: .x and .z = floor, .y = height
        public readonly bool TrackingNormal;

        public HeadSample(long frame, double time, double3 position, bool trackingNormal)
        {
            Frame = frame; Time = time; Position = position; TrackingNormal = trackingNormal;
        }
    }
}
