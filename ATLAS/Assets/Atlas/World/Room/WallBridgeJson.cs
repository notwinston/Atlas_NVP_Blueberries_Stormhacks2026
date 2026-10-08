using Unity.Mathematics;

namespace Atlas.World
{
    // The atlas.walls/1 reader that lived here (WallBridgeJson) is retired: the RoomPlan plugin now writes
    // atlas.room/1, read by RoomPlanJson (standalone-phone plan section 6.2). The surface types stay, under their
    // names, because RoomPlanJson and WallBridgeMerge use them.

    /// <summary>What RoomPlan calls a surface of the room.</summary>
    public enum BridgeSurfaceKind : byte { Wall = 0, Door = 1, Window = 2, Opening = 3 }

    /// <summary>
    /// One upright rectangle of a RoomPlan capture (a wall, or a door, window or plain opening in one), in the
    /// AR session frame as Unity has it: metres, +Y up, left-handed.
    /// </summary>
    public struct BridgeSurface
    {
        public BridgeSurfaceKind Kind;
        /// <summary>Centre of the rectangle.</summary>
        public float3 Centre;
        /// <summary>Unit floor direction (x, z) the width runs along. Its sign carries no meaning.</summary>
        public float2 Along;
        /// <summary>Metres along the floor.</summary>
        public float Width;
        /// <summary>Metres, bottom edge to top edge.</summary>
        public float Height;
    }
}
