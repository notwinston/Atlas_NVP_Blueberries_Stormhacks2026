using System;
using Atlas.Rendering.Room;
using Atlas.World;

namespace Atlas.Spaces.Home
{
    /// <summary>What Home costs to draw: the room content and Home's own floor marks.</summary>
    public struct HomeDrawCost
    {
        /// <summary>The room content in Home's presentation: full walls, floor and objects shown.</summary>
        public RoomDrawCost Room;
        /// <summary>The floor marks: one mesh, one draw call per eye.</summary>
        public int MarkTriangles;
        /// <summary>0 or 1.</summary>
        public int MarkDrawCalls;

        public int DrawCallsPerEye { get { return Room.DrawCallsPerEye + MarkDrawCalls; } }
        public int TrianglesPerEye { get { return Room.TrianglesPerEye + MarkTriangles; } }
        /// <summary>Both eyes draw the same content.</summary>
        public int DrawCallsPerFrame { get { return 2 * DrawCallsPerEye; } }
        public int TrianglesPerFrame { get { return 2 * TrianglesPerEye; } }
    }

    /// <summary>
    /// Budget accounting for Home (Master Plan section 8.5 and task P1-C07): at most 60 draw calls and
    /// 130,000 triangles per frame for both eyes on budget_room. Engine-free: it counts with the builders
    /// that draw the room and the marks.
    /// </summary>
    public static class HomeBudget
    {
        public const int DrawCallsPerFrame = RoomBudget.HomeDrawCallsPerFrame;
        public const int TrianglesPerFrame = RoomBudget.HomeTrianglesPerFrame;

        /// <summary>The cost of Home on this room, with or without its floor marks.</summary>
        public static HomeDrawCost Estimate(RoomModel room, bool marks = true)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            var cost = new HomeDrawCost { Room = RoomBudget.Estimate(room, WallMode.Full, true, true) };
            if (marks)
            {
                cost.MarkTriangles = HomeMarks.Triangles(room);
                cost.MarkDrawCalls = 1;
            }
            return cost;
        }

        /// <summary>True when the cost is inside Home's row of the budget table.</summary>
        public static bool Fits(in HomeDrawCost cost)
        {
            return cost.DrawCallsPerFrame <= DrawCallsPerFrame && cost.TrianglesPerFrame <= TrianglesPerFrame;
        }
    }
}
