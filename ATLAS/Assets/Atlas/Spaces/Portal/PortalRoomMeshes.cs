using Atlas.Rendering;
using Unity.Mathematics;

namespace Atlas.Spaces.Portal
{
    /// <summary>
    /// The geometry of Impossible Rooms as mesh data (Master Plan section 8.4: at most 2,000 triangles per
    /// room, the booth at most 800). Everything is in the room frame, so every mesh is drawn with an
    /// identity transform and every portal transform is the identity (D16). Portal rooms are never warped,
    /// so faces are single quads. Engine-free: a headless test counts the triangles.
    /// </summary>
    public static class PortalRoomMeshes
    {
        public const int RoomTriangles = AtlasMeshBudget.PortalRoom;
        public const int BoothTriangles = 800;

        /// <summary>The hall reaches this far past the free rectangle on every side, and this high. Metres.</summary>
        public const float HallMargin = 3f, HallHeight = 4.5f;
        /// <summary>The hall's floor lies this far below the platform the walker stands on. Metres.</summary>
        public const float HallDrop = 0.6f;

        const float Coarse = 1000f;             // one cell per face: nothing here bends
        const float Grid = AtlasMeshBuilder.GridMetres;
        const float FrameWidth = 0.06f;
        const float Lift = 0.004f;              // keeps a strip clear of the face it lies on

        /// <summary>
        /// What the layout adds to the real room, room 0: the booth with its door, or the frame of the loop's
        /// entrance. Nothing for a layout without doorways.
        /// </summary>
        public static void BuildExterior(PortalLayout layout, AtlasMeshBuilder data)
        {
            if (layout.Kind == PortalLayoutKind.BoothHall) BuildBooth(layout, data);
            else if (layout.Kind == PortalLayoutKind.Loop) BuildEntrance(layout, data);
        }

        /// <summary>The mesh of a virtual room: the hall, or one corridor room of the loop. Nothing for room 0.</summary>
        public static void BuildRoom(PortalLayout layout, int room, AtlasMeshBuilder data)
        {
            LayoutRoom r = layout.Rooms[room];
            if (r.Kind == LayoutRoomKind.Hall) BuildHall(layout, r, data);
            else if (r.Kind == LayoutRoomKind.Corridor) BuildCorridor(layout, r, data);
        }

        // ---- booth ----------------------------------------------------------------------------------------

        static void BuildBooth(PortalLayout layout, AtlasMeshBuilder data)
        {
            LayoutDoor door = layout.Doors[0];
            FloorRect booth = layout.Booth;
            float top = layout.BoothHeight;
            int wall = AtlasPalette.Booth;

            // Counter-clockwise seen from above, so every face looks outwards. The face that holds the
            // doorway runs the same way as the doorway's own A to B.
            var c0 = booth.Min;
            var c1 = new float2(booth.Max.x, booth.Min.y);
            var c2 = booth.Max;
            var c3 = new float2(booth.Min.x, booth.Max.y);
            bool southDoor = math.abs(door.A.y - booth.Min.y) < 1e-3f;
            if (southDoor) WallWithDoor(data, c0, c1, door, 0f, top, wall); else Wall(data, c0, c1, 0f, top, wall);
            Wall(data, c1, c2, 0f, top, wall);
            if (!southDoor) WallWithDoor(data, c2, c3, door, 0f, top, wall); else Wall(data, c2, c3, 0f, top, wall);
            Wall(data, c3, c0, 0f, top, wall);

            // Roof.
            float2 size = booth.Size;
            data.AddQuad(new float3(booth.Min.x, top, booth.Min.y), new float3(size.x, 0f, 0f), new float3(0f, 0f, size.y),
                         AtlasPalette.Uv(AtlasPalette.Ceiling));

            DoorGlow(data, door, Lift);
        }

        // ---- the loop's entrance --------------------------------------------------------------------------

        static void BuildEntrance(PortalLayout layout, AtlasMeshBuilder data)
        {
            LayoutDoor door = layout.Doors[layout.Doors.Count - 1];
            DoorFrame(data, door, DoorNormal(door));
            DoorGlow(data, door, FrameWidth + 0.005f + Lift);
        }

        // ---- hall -----------------------------------------------------------------------------------------

        static void BuildHall(PortalLayout layout, LayoutRoom room, AtlasMeshBuilder data)
        {
            float hx = 0.5f * layout.Wf, hz = 0.5f * layout.Df;
            var min = new float2(-hx, -hz);
            var max = new float2(hx, hz);
            var outerMin = min - HallMargin;
            var outerMax = max + HallMargin;
            float low = -HallDrop;

            // The platform the walker stands on: the free rectangle, 1:1 over the real floor.
            data.AddFloorGrid(min, max, 0f, AtlasPalette.Uv(AtlasPalette.Floor), Grid, 1f, 255, Coarse);
            // Its rim glows, so the walker sees where the floor ends.
            Rim(data, min, max);
            // Its sides, looking outwards.
            int side = AtlasPalette.Kerb;
            Wall(data, min, new float2(max.x, min.y), low, 0f, side);
            Wall(data, new float2(max.x, min.y), max, low, 0f, side);
            Wall(data, max, new float2(min.x, max.y), low, 0f, side);
            Wall(data, new float2(min.x, max.y), min, low, 0f, side);

            // The hall itself: a floor below, far walls and a ceiling. It is larger than the room it stands in.
            data.AddFloorGrid(outerMin, outerMax, low, AtlasPalette.Uv(AtlasPalette.FloorAlt), Grid, 1f, 255, Coarse);
            int wall = AtlasPalette.Region(room.Hue, 1);
            Wall(data, outerMin, new float2(outerMin.x, outerMax.y), low, HallHeight, wall);
            Wall(data, new float2(outerMin.x, outerMax.y), outerMax, low, HallHeight, wall);
            Wall(data, outerMax, new float2(outerMax.x, outerMin.y), low, HallHeight, wall);
            Wall(data, new float2(outerMax.x, outerMin.y), outerMin, low, HallHeight, wall);
            float2 outerSize = outerMax - outerMin;
            data.AddQuad(new float3(outerMin.x, HallHeight, outerMin.y), new float3(0f, 0f, outerSize.y),
                         new float3(outerSize.x, 0f, 0f), AtlasPalette.Uv(AtlasPalette.Ceiling));

            // Columns round the platform, half-way to the walls.
            float gap = 0.5f * HallMargin;
            var columnSize = new float3(0.35f, HallHeight + HallDrop, 0.35f);
            float2 columnUv = AtlasPalette.Uv(AtlasPalette.Region(room.Hue, 3));
            for (int ix = -1; ix <= 1; ix++)
            {
                for (int iz = -1; iz <= 1; iz++)
                {
                    if (ix == 0 && iz == 0) continue;
                    var at = new float3(ix * (hx + gap), low, iz * (hz + gap));
                    data.AddBox(at, columnSize, 0f, columnUv, false, 0, Coarse);
                }
            }

            // The way back: a frame standing free on the platform. From the hall's side it is a closed panel.
            LayoutDoor door = layout.Doors[0];
            float2 normal = DoorNormal(door);
            DoorFrame(data, door, normal);
            float2 back = normal * 0.02f;
            Wall(data, door.A + back, door.B + back, 0f, door.Height, AtlasPalette.Booth);
        }

        static void Rim(AtlasMeshBuilder data, float2 min, float2 max)
        {
            const float width = 0.04f;
            float2 uv = AtlasPalette.Uv(AtlasPalette.LapMarker);
            float y = Lift;
            float2 size = max - min;
            data.AddQuad(new float3(min.x, y, min.y), new float3(size.x, 0f, 0f), new float3(0f, 0f, width), uv);
            data.AddQuad(new float3(min.x, y, max.y - width), new float3(size.x, 0f, 0f), new float3(0f, 0f, width), uv);
            data.AddQuad(new float3(min.x, y, min.y), new float3(width, 0f, 0f), new float3(0f, 0f, size.y), uv);
            data.AddQuad(new float3(max.x - width, y, min.y), new float3(width, 0f, 0f), new float3(0f, 0f, size.y), uv);
        }

        // ---- corridor -------------------------------------------------------------------------------------

        static void BuildCorridor(PortalLayout layout, LayoutRoom room, AtlasMeshBuilder data)
        {
            float wf = layout.Wf, df = layout.Df, width = layout.CorridorWidth, top = layout.WallHeight;
            // The second lap is told from the first by a lighter wall, and every room by its number.
            int wall = AtlasPalette.Region(room.Hue, room.Number > 4 ? 3 : 1);
            float2 floorUv = AtlasPalette.Uv(AtlasPalette.Floor);
            LayoutDoor entrance = layout.Doors[layout.Doors.Count - 1];

            for (int i = 0; i < room.Turns; i++)
            {
                int j = room.FirstCorner + i;
                float2 p = PortalLayoutGenerator.Corner(wf, df, j);
                float2 d = PortalLayoutGenerator.LegDirection(j);
                float2 inward = PortalLayoutGenerator.LegInward(j);
                float length = PortalLayoutGenerator.LegLength(wf, df, j);

                FloorRect corner = PortalLayoutGenerator.CornerCell(wf, df, width, j);
                FloorRect leg = PortalLayoutGenerator.LegCell(wf, df, width, j);
                data.AddFloorGrid(corner.Min, corner.Max, 0f, floorUv, Grid, 1f, 255, Coarse);
                data.AddFloorGrid(leg.Min, leg.Max, 0f, floorUv, Grid, 1f, 255, Coarse);

                // Walls are walked with the corridor on their right, which is the side they face.
                Wall(data, p + width * inward, p, 0f, top, wall);
                Wall(data, p, p + width * d, 0f, top, wall);
                Wall(data, p + width * d, p + (length - width) * d, 0f, top, wall);
                float2 innerFrom = p + (length - width) * d + width * inward;
                float2 innerTo = p + width * d + width * inward;
                bool holdsEntrance = entrance.To == room.Id && i == 0;
                if (holdsEntrance) WallWithDoor(data, innerFrom, innerTo, entrance, 0f, top, wall);
                else Wall(data, innerFrom, innerTo, 0f, top, wall);

                // The room's number, as bars on the wall the walker faces when coming in.
                if (i == 0) Tally(data, p, d, inward, width, room.Number);
            }
        }

        static void Tally(AtlasMeshBuilder data, float2 corner, float2 along, float2 inward, float width, int number)
        {
            if (number <= 0) return;
            const float bar = 0.05f;
            float pitch = width / (number + 1);
            float2 uv = AtlasPalette.Uv(AtlasPalette.Beacon);
            for (int k = 1; k <= number; k++)
            {
                float2 from = corner + (k * pitch - 0.5f * bar) * along + Lift * inward;
                float2 to = from + bar * along;
                var origin = new float3(from.x, 1.2f, from.y);
                data.AddQuad(origin, new float3(to.x - from.x, 0f, to.y - from.y), new float3(0f, 0.5f, 0f), uv);
            }
        }

        // ---- pieces ---------------------------------------------------------------------------------------

        /// <summary>The unit normal of a doorway on the floor, towards a walker who sees A on the left.</summary>
        public static float2 DoorNormal(in LayoutDoor door)
        {
            float2 along = math.normalizesafe(door.B - door.A, new float2(1f, 0f));
            return new float2(along.y, -along.x);
        }

        // A vertical face from floor point p0 to p1 between two heights. It faces the right of p0 to p1.
        static void Wall(AtlasMeshBuilder data, float2 p0, float2 p1, float y0, float y1, int palette)
        {
            float2 span = p1 - p0;
            float length = math.length(span);
            if (!(length > 1e-4f) || !(y1 - y0 > 1e-4f)) return;
            data.AddQuadGrid(new float3(p0.x, y0, p0.y), new float3(span.x, 0f, span.y), new float3(0f, y1 - y0, 0f),
                             AtlasPalette.Uv(palette),
                             new float2(0f, y0 / Grid), new float2(length / Grid, 0f), new float2(0f, (y1 - y0) / Grid),
                             1f, 255, Coarse);
        }

        // The same face with the doorway cut out of it. The doorway lies on the line from p0 to p1.
        static void WallWithDoor(AtlasMeshBuilder data, float2 p0, float2 p1, in LayoutDoor door, float y0, float y1, int palette)
        {
            float2 span = p1 - p0;
            float length = math.length(span);
            if (!(length > 1e-4f)) return;
            float2 along = span / length;
            float s0 = math.dot(door.A - p0, along), s1 = math.dot(door.B - p0, along);
            float from = math.clamp(math.min(s0, s1), 0f, length), to = math.clamp(math.max(s0, s1), 0f, length);
            Wall(data, p0, p0 + from * along, y0, y1, palette);
            Wall(data, p0 + to * along, p1, y0, y1, palette);
            Wall(data, p0 + from * along, p0 + to * along, math.max(y0, door.Height), y1, palette);
        }

        // Two posts and a lintel on the viewer's side of the doorway's plane.
        static void DoorFrame(AtlasMeshBuilder data, in LayoutDoor door, float2 normal)
        {
            float2 along = math.normalizesafe(door.B - door.A, new float2(1f, 0f));
            float2 uv = AtlasPalette.Uv(AtlasPalette.DoorFrame);
            float2 forward = normal * (0.5f * FrameWidth + 0.005f);
            var post = new float3(FrameWidth, door.Height + FrameWidth, FrameWidth);
            float2 left = door.A - along * (0.5f * FrameWidth) + forward;
            float2 right = door.B + along * (0.5f * FrameWidth) + forward;
            data.AddBox(new float3(left.x, 0f, left.y), post, 0f, uv, false, 0, Coarse);
            data.AddBox(new float3(right.x, 0f, right.y), post, 0f, uv, false, 0, Coarse);
            float width = math.distance(door.A, door.B);
            float2 mid = 0.5f * (door.A + door.B) + forward;
            // Doorways run along X or along Z, so the lintel is a box along that axis.
            var lintel = new float3(math.abs(along.x) * width + FrameWidth * math.abs(along.y), FrameWidth,
                                    math.abs(along.y) * width + FrameWidth * math.abs(along.x));
            data.AddBox(new float3(mid.x, door.Height, mid.y), lintel, 0f, uv, true, 0, Coarse);
        }

        // An emissive outline round the doorway, on the viewer's side, this far in front of the doorway's plane.
        static void DoorGlow(AtlasMeshBuilder data, in LayoutDoor door, float inFront)
        {
            const float width = 0.04f;
            float2 along = math.normalizesafe(door.B - door.A, new float2(1f, 0f));
            float2 lift = DoorNormal(door) * inFront;
            int glow = AtlasPalette.PortalFrame;
            float2 a = door.A + lift, b = door.B + lift;
            GlowStrip(data, a - along * width, a, 0f, door.Height + width, glow);
            GlowStrip(data, b, b + along * width, 0f, door.Height + width, glow);
            GlowStrip(data, a, b, door.Height, door.Height + width, glow);
        }

        static void GlowStrip(AtlasMeshBuilder data, float2 p0, float2 p1, float y0, float y1, int palette)
        {
            float2 span = p1 - p0;
            data.AddQuad(new float3(p0.x, y0, p0.y), new float3(span.x, 0f, span.y), new float3(0f, y1 - y0, 0f),
                         AtlasPalette.Uv(palette));
        }
    }
}
