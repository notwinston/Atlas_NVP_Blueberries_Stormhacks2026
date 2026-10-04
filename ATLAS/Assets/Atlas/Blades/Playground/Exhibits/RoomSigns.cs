using System.Collections.Generic;
using Atlas.Rendering;
using Atlas.Spaces.Portal;
using Unity.Mathematics;

namespace Atlas.Blades.Playground.Exhibits
{
    /// <summary>A room-number sign on a wall of the loop corridor.</summary>
    public struct RoomSign
    {
        public int Room, Number;
        /// <summary>The middle of the sign, room frame: x and z on the wall face, y the height of its middle. Metres.</summary>
        public float3 Centre;
        /// <summary>The way the wall face looks, towards the walker who reads it.</summary>
        public float2 Normal;
        /// <summary>Metres.</summary>
        public float Height;
    }

    /// <summary>
    /// Where the room numbers of the loop corridor are signed (Master Plan task P2-C16) and the mesh of a
    /// digit. Each corridor room carries its number as a digit on the wall the walker faces when they come
    /// in, above the tally bars the room already carries (<c>PortalRoomMeshes</c>), so the number is read as
    /// a numeral and cross-checked by counting bars. Engine-free.
    /// </summary>
    public static class RoomSigns
    {
        /// <summary>Digit size, metres. It fits the narrowest corridor (0.7 m) with room to spare.</summary>
        public const float DigitHeight = 0.4f, DigitWidth = 0.24f, StrokeWidth = 0.05f;
        /// <summary>Height of the digit's middle: above the tally bars (1.2 to 1.7 m) and below the wall top (2.4 m).</summary>
        public const float CentreHeight = 2.05f;
        /// <summary>The sign stands this far in front of the wall face. Metres.</summary>
        public const float Lift = 0.008f;
        /// <summary>Most triangles one digit takes: seven strokes of two.</summary>
        public const int MaxDigitTriangles = 14;

        // Seven-segment bits: a top, b top right, c bottom right, d bottom, e bottom left, f top left, g middle.
        static readonly byte[] s_Segments =
        {
            0x3F, // 0 abcdef
            0x06, // 1 bc
            0x5B, // 2 abdeg
            0x4F, // 3 abcdg
            0x66, // 4 bcfg
            0x6D, // 5 acdfg
            0x7D, // 6 acdefg
            0x07, // 7 abc
            0x7F, // 8
            0x6F, // 9 abcdfg
        };

        /// <summary>One sign per corridor room of a loop layout, in room order. None for any other layout.</summary>
        public static List<RoomSign> Layout(PortalLayout layout)
        {
            var signs = new List<RoomSign>(PortalLayoutGenerator.MaxLoopRooms);
            if (layout == null || layout.Kind != PortalLayoutKind.Loop) return signs;
            for (int r = 0; r < layout.RoomCount; r++)
            {
                LayoutRoom room = layout.Rooms[r];
                if (room.Kind != LayoutRoomKind.Corridor || room.Number <= 0 || room.Turns <= 0) continue;
                int j = room.FirstCorner;
                float2 corner = PortalLayoutGenerator.Corner(layout.Wf, layout.Df, j);
                float2 along = PortalLayoutGenerator.LegDirection(j);
                float2 inward = PortalLayoutGenerator.LegInward(j);
                // The middle of the corner square's outer wall: the wall a walker faces when they arrive.
                float2 middle = corner + 0.5f * layout.CorridorWidth * along + Lift * inward;
                signs.Add(new RoomSign
                {
                    Room = r,
                    Number = room.Number,
                    Centre = new float3(middle.x, CentreHeight, middle.y),
                    Normal = inward,
                    Height = DigitHeight,
                });
            }
            return signs;
        }

        /// <summary>The digits of a number, most significant first. A number under 0 has none.</summary>
        public static int Digits(int number)
        {
            if (number < 0) return 0;
            int count = 1;
            while (number >= 10) { number /= 10; count++; }
            return count;
        }

        /// <summary>Adds the strokes of the sign's number to the mesh, facing the walker, left to right as they read it.</summary>
        public static void BuildSign(in RoomSign sign, AtlasMeshBuilder data, int palette)
        {
            // The face looks along the sign's normal, so the reader's right is the face's: (-n.y, n.x).
            float2 right = new float2(-sign.Normal.y, sign.Normal.x);
            int count = Digits(sign.Number);
            float pitch = DigitWidth + 1.5f * StrokeWidth;
            float2 uv = AtlasPalette.Uv(palette);
            float left = -0.5f * (count * pitch - 1.5f * StrokeWidth);
            int[] digits = new int[count];
            int rest = sign.Number;
            for (int k = count - 1; k >= 0; k--) { digits[k] = rest % 10; rest /= 10; }
            for (int k = 0; k < count; k++)
                AddDigit(data, sign, right, left + k * pitch, digits[k], uv);
        }

        static void AddDigit(AtlasMeshBuilder data, in RoomSign sign, float2 right, float offset, int digit, float2 uv)
        {
            byte bits = s_Segments[digit];
            float w = DigitWidth, h = sign.Height, t = StrokeWidth;
            float x0 = offset, x1 = offset + w;
            float y0 = sign.Centre.y - 0.5f * h, y1 = sign.Centre.y + 0.5f * h, ym = sign.Centre.y;
            // Horizontal strokes a, g, d; vertical strokes b, c, e, f.
            if ((bits & 0x01) != 0) Stroke(data, sign, right, x0, x1, y1 - t, y1, uv);
            if ((bits & 0x40) != 0) Stroke(data, sign, right, x0 + t, x1 - t, ym - 0.5f * t, ym + 0.5f * t, uv);
            if ((bits & 0x08) != 0) Stroke(data, sign, right, x0, x1, y0, y0 + t, uv);
            if ((bits & 0x02) != 0) Stroke(data, sign, right, x1 - t, x1, ym, y1, uv);
            if ((bits & 0x04) != 0) Stroke(data, sign, right, x1 - t, x1, y0, ym, uv);
            if ((bits & 0x10) != 0) Stroke(data, sign, right, x0, x0 + t, y0, ym, uv);
            if ((bits & 0x20) != 0) Stroke(data, sign, right, x0, x0 + t, ym, y1, uv);
        }

        // A rectangle on the wall: from xa to xb along the reader's right, from ya to yb up.
        static void Stroke(AtlasMeshBuilder data, in RoomSign sign, float2 right, float xa, float xb, float ya, float yb, float2 uv)
        {
            float2 p = new float2(sign.Centre.x, sign.Centre.z) + right * xa;
            float2 span = right * (xb - xa);
            data.AddQuad(new float3(p.x, ya, p.y), new float3(span.x, 0f, span.y), new float3(0f, yb - ya, 0f), uv);
        }
    }
}
