using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.Portal
{
    /// <summary>What a layout is generated from: the free rectangle of the scan and the walker's choices.</summary>
    public struct PortalLayoutRequest
    {
        /// <summary>The free rectangle, metres: Wf along X, Df along Z, centred on the origin.</summary>
        public float Wf, Df;
        /// <summary>Standing mode: the walker does not walk, so the loop is never built.</summary>
        public bool Standing;
        /// <summary>Height of every doorway, metres.</summary>
        public float DoorHeight;
        public PortalLayoutKind Layout;
        /// <summary>Rooms in the loop, 2 to 6.</summary>
        public int LoopRooms;
        /// <summary>Chooses the booth's corner, the loop's entrance and the rooms' colours.</summary>
        public uint Seed;
        /// <summary>The eroded free floor, or null to trust the free rectangle.</summary>
        public IPortalFloor Floor;

        /// <summary>The request of a scanned room: its free rectangle, its floor, and doors below its lowest wall.</summary>
        public static PortalLayoutRequest FromRoom(RoomModel room, PortalLayoutKind layout, int loopRooms, uint seed)
        {
            float lowest = RoomModel.DefaultWallHeight;
            for (int k = 0; k < room.Walls.Length; k++)
                if (room.Walls[k].Height > 0f) lowest = math.min(lowest, room.Walls[k].Height);
            return new PortalLayoutRequest
            {
                Wf = room.FreeRect.Wf,
                Df = room.FreeRect.Df,
                Standing = room.Standing,
                DoorHeight = math.clamp(lowest - 0.1f, PortalLayoutGenerator.MinDoorHeight, PortalLayoutGenerator.MaxDoorHeight),
                Layout = layout,
                LoopRooms = loopRooms,
                Seed = seed,
                Floor = new RoomModelFloor(room),
            };
        }
    }

    /// <summary>
    /// Generates the layouts of Impossible Rooms from the free rectangle (Master Plan section 3 and task
    /// P1-A11). Every room is placed in the room frame and checked against the eroded free floor. A floor
    /// that cannot hold the loop gets the booth; one that cannot hold the booth gets the real room alone.
    /// Engine-free and deterministic: the same request gives the same layout.
    /// </summary>
    public static class PortalLayoutGenerator
    {
        public const int MinLoopRooms = 2, MaxLoopRooms = 6, DefaultLoopRooms = 6;
        /// <summary>The loop is built only when the short side of the free rectangle is at least this (Master Plan section 3). Metres.</summary>
        public const float MinLoopSide = 2.4f;
        /// <summary>Corridor width: a third of the short side, between these. Metres.</summary>
        public const float MinCorridor = 0.7f, MaxCorridor = 1.0f;
        /// <summary>The booth: 1.0 m square where the floor allows, never under 0.7 m.</summary>
        public const float BoothSide = 1.0f, MinBoothSide = 0.7f;
        /// <summary>Floor left in front of the booth's door on the short side. Metres.</summary>
        public const float BoothApproach = 0.4f;
        public const float DoorWidth = 0.9f;
        public const float MinDoorHeight = 1.8f, MaxDoorHeight = 2.1f;
        public const float LoopWallHeight = 2.4f;
        /// <summary>Corners the loop's rooms cover between them: two laps of the free rectangle.</summary>
        public const int LoopCorners = 8;

        /// <summary>The layout asked for, or the best one the floor can hold. Never null, always valid.</summary>
        public static PortalLayout Generate(in PortalLayoutRequest request)
        {
            PortalLayoutKind wanted = request.Layout == PortalLayoutKind.Loop ? PortalLayoutKind.Loop : PortalLayoutKind.BoothHall;
            float doorHeight = math.clamp(request.DoorHeight > 0f ? request.DoorHeight : MaxDoorHeight, MinDoorHeight, MaxDoorHeight);
            bool sane = request.Wf > 0f && request.Df > 0f && !float.IsNaN(request.Wf) && !float.IsNaN(request.Df);

            if (sane && wanted == PortalLayoutKind.Loop && !request.Standing && math.min(request.Wf, request.Df) >= MinLoopSide - 1e-4f)
            {
                // The entrance may stand on either long side.
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    int leg = ((request.Seed + (uint)attempt) & 1u) == 0u ? 3 : 1;
                    PortalLayout loop = BuildLoop(in request, doorHeight, leg);
                    if (loop.IsValid(request.Floor)) return loop;
                }
            }

            if (sane && math.min(request.Wf, request.Df) >= MinBoothSide + BoothApproach - 1e-4f)
            {
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    int corner = (int)((request.Seed + (uint)attempt) & 3u);
                    PortalLayout booth = BuildBooth(in request, doorHeight, corner);
                    booth.IsFallback = wanted != PortalLayoutKind.BoothHall;
                    if (booth.IsValid(request.Floor)) return booth;
                }
            }

            var alone = NewLayout(in request, PortalLayoutKind.RealRoomOnly, doorHeight);
            alone.IsFallback = true;
            alone.AddRoom(LayoutRoomKind.Real);
            return alone;
        }

        /// <summary>Corridor width for a free rectangle: clamp(short side / 3, 0.7 m, 1.0 m).</summary>
        public static float CorridorWidth(float wf, float df)
        {
            return math.clamp(math.min(wf, df) / 3f, MinCorridor, MaxCorridor);
        }

        /// <summary>How many corners loop room k of n covers: 8 corners shared as evenly as they go, the first rooms taking the rest.</summary>
        public static int LoopSpan(int k, int rooms)
        {
            rooms = math.clamp(rooms, MinLoopRooms, MaxLoopRooms);
            return LoopCorners / rooms + (k < LoopCorners % rooms ? 1 : 0);
        }

        // ---- the loop -------------------------------------------------------------------------------------
        //
        // Seen from above, +Z up the page: corner 0 is NW, 1 NE, 2 SE, 3 SW. Leg j runs from corner j to corner
        // j + 1, so a walker who follows the legs turns right at every corner. A corridor room is, per corner it
        // covers, the corner square and the leg that follows it. Its entrance is the cross-section in front of
        // its first corner square and its exit the one in front of the next room's.

        /// <summary>Outer corner j of the free rectangle.</summary>
        public static float2 Corner(float wf, float df, int j)
        {
            float hx = 0.5f * wf, hz = 0.5f * df;
            switch (j & 3)
            {
                case 0: return new float2(-hx, hz);
                case 1: return new float2(hx, hz);
                case 2: return new float2(hx, -hz);
                default: return new float2(-hx, -hz);
            }
        }

        /// <summary>The direction a walker walks along leg j.</summary>
        public static float2 LegDirection(int j)
        {
            switch (j & 3)
            {
                case 0: return new float2(1f, 0f);
                case 1: return new float2(0f, -1f);
                case 2: return new float2(-1f, 0f);
                default: return new float2(0f, 1f);
            }
        }

        /// <summary>From the outer wall of leg j towards the middle of the room: the walker's right.</summary>
        public static float2 LegInward(int j)
        {
            float2 d = LegDirection(j);
            return new float2(d.y, -d.x);
        }

        public static float LegLength(float wf, float df, int j)
        {
            return (j & 1) == 0 ? wf : df;
        }

        /// <summary>The corner square at corner j.</summary>
        public static FloorRect CornerCell(float wf, float df, float width, int j)
        {
            float2 p = Corner(wf, df, j);
            return new FloorRect(p, p + width * (LegDirection(j) + LegInward(j)));
        }

        /// <summary>Leg j without the corner squares at its ends.</summary>
        public static FloorRect LegCell(float wf, float df, float width, int j)
        {
            float2 p = Corner(wf, df, j), d = LegDirection(j);
            return new FloorRect(p + width * d, p + (LegLength(wf, df, j) - width) * d + width * LegInward(j));
        }

        static PortalLayout BuildLoop(in PortalLayoutRequest request, float doorHeight, int entranceLeg)
        {
            float wf = request.Wf, df = request.Df;
            float width = CorridorWidth(wf, df);
            int count = math.clamp(request.LoopRooms, MinLoopRooms, MaxLoopRooms);

            PortalLayout layout = NewLayout(in request, PortalLayoutKind.Loop, doorHeight);
            layout.CorridorWidth = width;
            layout.WallHeight = math.max(LoopWallHeight, doorHeight + 0.3f);
            layout.EntranceLeg = entranceLeg;
            layout.AddRoom(LayoutRoomKind.Real);

            // The first corridor room starts at the corner in front of the entrance leg, so it owns that leg.
            int corner = entranceLeg;
            uint hueShift = request.Seed >> 1;
            for (int k = 0; k < count; k++)
            {
                int span = LoopSpan(k, count);
                LayoutRoom room = layout.AddRoom(LayoutRoomKind.Corridor);
                room.FirstCorner = corner & 3;
                room.Turns = span;
                room.Number = k + 1;
                room.Hue = (int)((hueShift + (uint)k) & 3u);
                room.Cells = new FloorRect[2 * span];
                for (int i = 0; i < span; i++)
                {
                    room.Cells[2 * i] = CornerCell(wf, df, width, corner + i);
                    room.Cells[2 * i + 1] = LegCell(wf, df, width, corner + i);
                }
                corner += span;
            }

            // Exits: the cross-section at the end of a room's last leg, the outer wall on the walker's left.
            for (int k = 0; k < count; k++)
            {
                LayoutRoom room = layout.Rooms[1 + k];
                int lastLeg = room.FirstCorner + room.Turns - 1;
                float2 p = Corner(wf, df, lastLeg), d = LegDirection(lastLeg);
                float2 a = p + (LegLength(wf, df, lastLeg) - width) * d;
                float2 b = a + width * LegInward(lastLeg);
                int next = 1 + (k + 1) % count;
                room.ExitPortal = layout.AddDoor(room.Id, next, a, b, doorHeight);
            }

            // The entrance from the real room: in the inner wall of the entrance leg, seen from the middle of the room.
            {
                float2 p = Corner(wf, df, entranceLeg), d = LegDirection(entranceLeg);
                float length = LegLength(wf, df, entranceLeg);
                float doorWidth = math.min(DoorWidth, length - 2f * width);
                float2 mid = p + 0.5f * length * d + width * LegInward(entranceLeg);
                layout.AddDoor(0, 1, mid - 0.5f * doorWidth * d, mid + 0.5f * doorWidth * d, doorHeight);
            }
            return layout;
        }

        // ---- booth to hall --------------------------------------------------------------------------------

        static PortalLayout BuildBooth(in PortalLayoutRequest request, float doorHeight, int corner)
        {
            float wf = request.Wf, df = request.Df;
            float hx = 0.5f * wf, hz = 0.5f * df;
            // The corner the booth stands in: bit 0 picks the side in x, bit 1 the side in z.
            float sx = (corner & 1) == 0 ? -1f : 1f, sz = (corner & 2) == 0 ? -1f : 1f;
            float side = math.min(BoothSide, math.min(wf - BoothApproach, 0.5f * df));
            side = math.max(side, MinBoothSide);
            float doorWidth = math.min(DoorWidth, side - 0.1f);

            PortalLayout layout = NewLayout(in request, PortalLayoutKind.BoothHall, doorHeight);
            layout.Booth = new FloorRect(new float2(sx * hx, sz * hz), new float2(sx * (hx - side), sz * (hz - side)));
            layout.BoothHeight = doorHeight + 0.2f;
            layout.BoothCorner = new int2((int)sx, (int)sz);
            layout.AddRoom(LayoutRoomKind.Real);

            LayoutRoom hall = layout.AddRoom(LayoutRoomKind.Hall);
            hall.Cells = new[] { new FloorRect(new float2(-hx, -hz), new float2(hx, hz)) };
            hall.Hue = (int)((request.Seed >> 2) & 3u);

            // The door faces the middle of the room along the long side. A walker in front of it looks
            // along sz, so the left end lies towards -sz in x.
            float face = sz * (hz - side);
            float centre = sx * (hx - 0.5f * side);
            var a = new float2(centre - sz * 0.5f * doorWidth, face);
            var b = new float2(centre + sz * 0.5f * doorWidth, face);
            layout.AddDoor(0, hall.Id, a, b, doorHeight);
            return layout;
        }

        static PortalLayout NewLayout(in PortalLayoutRequest request, PortalLayoutKind kind, float doorHeight)
        {
            return new PortalLayout
            {
                Kind = kind,
                Requested = request.Layout == PortalLayoutKind.Loop ? PortalLayoutKind.Loop : PortalLayoutKind.BoothHall,
                IsFallback = false,
                Seed = request.Seed,
                Wf = request.Wf,
                Df = request.Df,
                DoorHeight = doorHeight,
                StartRoom = 0,
            };
        }
    }
}
