// PLACEHOLDER (lane C, P1-C09): contract surface only; replaced by lane C's implementation
using Unity.Mathematics;

namespace Atlas.Safety
{
    // clear(p) = min(static SDF at p, distance to the nearest dynamic cell), m; p = anchor-frame xz
    // (Master Plan §4.10; placeholder choice PH-27).
    public interface IClearanceQuery
    {
        float Clearance(float2 p);
    }
}
