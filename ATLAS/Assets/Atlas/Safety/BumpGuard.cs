// PLACEHOLDER (lane C, P1-C09): contract surface only; replaced by lane C's implementation
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Safety
{
    // Master Plan §4.10. Level is always Clear, Tick does nothing, the dynamic layer is empty.
    public sealed class BumpGuard : IBumpGuard, IClearanceQuery
    {
        readonly RoomModel _room;

        public BumpGuard(RoomModel room) { _room = room; }

        public BumpLevel Level => BumpLevel.Clear;
        public float2 Direction => float2.zero;
        public bool RescanAdvised => false;
        public void Tick(in SensorFrame sensors) { }
        public float Clearance(float2 p) => _room.sdf.Sample(p.x, p.y);
    }
}
