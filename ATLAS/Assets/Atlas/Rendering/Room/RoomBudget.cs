using System;
using Atlas.World;

namespace Atlas.Rendering.Room
{
    /// <summary>What the room costs to draw. Both eyes draw the same content, so a frame is twice an eye.</summary>
    public struct RoomDrawCost
    {
        public int DrawCallsPerEye;
        public int TrianglesPerEye;
        /// <summary>Of TrianglesPerEye: floor and walls (or kerbs).</summary>
        public int ShellTriangles;
        /// <summary>Of TrianglesPerEye: the objects.</summary>
        public int ObjectTriangles;

        public int DrawCallsPerFrame { get { return 2 * DrawCallsPerEye; } }
        public int TrianglesPerFrame { get { return 2 * TrianglesPerEye; } }
    }

    /// <summary>
    /// Draw-call and triangle accounting for the room (Master Plan section 8.5). Engine-free: it counts what
    /// RoomContent builds from a RoomModel, with the same builders.
    /// </summary>
    /// <remarks>
    /// The batching: the floor is one mesh and one draw call, the walls (or the kerbs with their posts) are
    /// one, and each object is one. All of them use pooled instances of the one surface shader, so they sit
    /// in one SRP batch and cost one set-pass. On budget_room that is 14 draw calls per eye, 28 per frame.
    /// </remarks>
    public static class RoomBudget
    {
        /// <summary>Home, per frame, both eyes (Master Plan section 8.5).</summary>
        public const int HomeDrawCallsPerFrame = 60, HomeTrianglesPerFrame = 130000;

        /// <summary>The cost of the room as RoomContent draws it in the given presentation.</summary>
        public static RoomDrawCost Estimate(RoomModel room, WallMode walls = WallMode.Full, bool floorVisible = true,
                                            bool objectsVisible = true)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            var cost = new RoomDrawCost();
            var scratch = new AtlasMeshBuilder(2048, 8192);
            RoomShellBuilder.Floor(room, scratch);
            int floor = scratch.TriangleCount;
            if (floorVisible)
            {
                cost.ShellTriangles += floor;
                cost.DrawCallsPerEye++;
            }
            if (walls != WallMode.Hidden && room.Walls.Length > 0)
            {
                // The wall rows are chosen against the floor whether or not the floor is shown.
                scratch.Clear();
                if (walls == WallMode.Full) RoomShellBuilder.Walls(room, floor, scratch, out _);
                else RoomShellBuilder.Kerbs(room, RoomShellBuilder.DefaultKerbHeight, scratch);
                cost.ShellTriangles += scratch.TriangleCount;
                cost.DrawCallsPerEye++;
            }
            if (objectsVisible)
            {
                RoomObject[] objects = room.Objects;
                for (int k = 0; k < objects.Length; k++) cost.ObjectTriangles += RoomObjectMeshes.Triangles(objects[k]);
                cost.DrawCallsPerEye += objects.Length;
            }
            cost.TrianglesPerEye = cost.ShellTriangles + cost.ObjectTriangles;
            return cost;
        }

        /// <summary>True when the cost is inside Home's row of the budget table.</summary>
        public static bool FitsHome(in RoomDrawCost cost)
        {
            return cost.DrawCallsPerFrame <= HomeDrawCallsPerFrame && cost.TrianglesPerFrame <= HomeTrianglesPerFrame;
        }
    }
}
