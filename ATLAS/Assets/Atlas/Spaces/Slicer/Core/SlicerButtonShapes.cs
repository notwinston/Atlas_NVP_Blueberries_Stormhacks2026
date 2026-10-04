using Atlas.Rendering;
using Unity.Mathematics;

namespace Atlas.Spaces.Slicer
{
    /// <summary>The icon drawn on a button.</summary>
    public enum SlicerIcon { None, Up, Down, TurnPlus, TurnMinus }

    /// <summary>
    /// The flat pieces of the Slicer's buttons (task P2-A01): a tile with a rim, the ring that shows the gaze is on it, the
    /// bar that fills while the gaze dwells, and the icons (an arrow up or down for the cut, a turning arrow for a turn).
    /// Everything lies in the button's own XY plane about its centre and faces -z, the side a TextMeshPro text reads from,
    /// so one turn of the button's transform shows it to the walker. Engine-free; built once per room.
    /// </summary>
    public static class SlicerButtonShapes
    {
        /// <summary>Half the edge of a handle's square tile, and half the size of a chip's pill, metres.</summary>
        public static readonly float2 TileHalf = new float2(0.05f, 0.05f), PillHalf = new float2(0.105f, 0.036f);
        /// <summary>Width of the rim, of the gaze ring outside it, and the cut corners, metres.</summary>
        public const float Rim = 0.007f, Ring = 0.009f, Chamfer = 0.016f;
        /// <summary>Height of the dwell bar and its gap above the tile's bottom edge, metres.</summary>
        public const float BarHeight = 0.009f, BarInset = 0.012f;

        /// <summary>Depth of each layer: the ring behind the rim, the face in front of it, the icon and the bar in front.</summary>
        public const float RingZ = 0.002f, RimZ = 0f, FaceZ = -0.001f, IconZ = -0.002f, BarZ = -0.003f;

        static readonly float2[] s_points = new float2[64];

        /// <summary>The tile: a rim of colour <paramref name="rim"/> around a dark face.</summary>
        public static void AddTile(AtlasMeshBuilder into, float2 half, int rim)
        {
            AddChamfered(into, half, Chamfer, RimZ, AtlasPalette.Uv(rim));
            AddChamfered(into, half - Rim, Chamfer - 0.4f * Rim, FaceZ, AtlasPalette.Uv(AtlasPalette.Void));
        }

        /// <summary>The ring shown while the gaze is on the button: a lit border just outside the tile.</summary>
        public static void AddRing(AtlasMeshBuilder into, float2 half, int colour)
        {
            AddChamfered(into, half + Ring, Chamfer + 0.4f * Ring, RingZ, AtlasPalette.Uv(colour));
        }

        /// <summary>The dwell bar at full length: from x = 0 to x = 1, one unit high about y = 0. The button scales it.</summary>
        public static void AddUnitBar(AtlasMeshBuilder into, int colour)
        {
            s_points[0] = new float2(0f, -0.5f);
            s_points[1] = new float2(1f, -0.5f);
            s_points[2] = new float2(1f, 0.5f);
            s_points[3] = new float2(0f, 0.5f);
            AddConvex(into, s_points, 4, 0f, AtlasPalette.Uv(colour));
        }

        /// <summary>Where the dwell bar starts on a tile of <paramref name="half"/>, and how long it is when full.</summary>
        public static float3 BarOrigin(float2 half) { return new float3(-half.x + BarInset, -half.y + BarInset, BarZ); }
        public static float BarLength(float2 half) { return 2f * (half.x - BarInset); }

        /// <summary>An icon about the centre, <paramref name="size"/> metres across, in colour cell <paramref name="colour"/>.</summary>
        public static void AddIcon(AtlasMeshBuilder into, SlicerIcon icon, float size, int colour)
        {
            AddIcon(into, icon, size, AtlasPalette.Uv(colour));
        }

        /// <summary>An icon about the centre in any palette coordinate (the cut's arrows take the w-depth colours).</summary>
        public static void AddIcon(AtlasMeshBuilder into, SlicerIcon icon, float size, float2 uv)
        {
            float s = 0.5f * size;
            switch (icon)
            {
                case SlicerIcon.Up:
                case SlicerIcon.Down:
                {
                    float d = icon == SlicerIcon.Up ? 1f : -1f;
                    // The head, then the stem.
                    s_points[0] = new float2(0f, d * s);
                    s_points[1] = new float2(-0.8f * s, d * 0.05f * s);
                    s_points[2] = new float2(0.8f * s, d * 0.05f * s);
                    AddConvex(into, s_points, 3, IconZ, uv);
                    s_points[0] = new float2(-0.28f * s, d * 0.1f * s);
                    s_points[1] = new float2(0.28f * s, d * 0.1f * s);
                    s_points[2] = new float2(0.28f * s, -d * s);
                    s_points[3] = new float2(-0.28f * s, -d * s);
                    AddConvex(into, s_points, 4, IconZ, uv);
                    break;
                }
                case SlicerIcon.TurnPlus:
                case SlicerIcon.TurnMinus:
                    AddTurnArrow(into, s, icon == SlicerIcon.TurnPlus ? 1f : -1f, uv);
                    break;
            }
        }

        // Three quarters of a circle with an arrowhead at its end: anticlockwise for plus, clockwise for minus (mirrored).
        static void AddTurnArrow(AtlasMeshBuilder into, float s, float direction, float2 uv)
        {
            const int segments = 14;
            float r = 0.62f * s, band = 0.2f * s;
            float start = math.radians(-60f), sweep = math.radians(250f);
            for (int i = 0; i < segments; i++)
            {
                float a0 = start + sweep * i / segments, a1 = start + sweep * (i + 1) / segments;
                s_points[0] = Polar(r - 0.5f * band, a0, direction);
                s_points[1] = Polar(r + 0.5f * band, a0, direction);
                s_points[2] = Polar(r + 0.5f * band, a1, direction);
                s_points[3] = Polar(r - 0.5f * band, a1, direction);
                AddConvex(into, s_points, 4, IconZ, uv);
            }
            float end = start + sweep;
            float2 radial = Polar(1f, end, direction), tangent = Polar(1f, end + 0.5f * math.PI, direction);
            float2 at = r * radial;
            s_points[0] = at + 0.42f * s * radial;
            s_points[1] = at - 0.42f * s * radial;
            s_points[2] = at + 0.5f * s * tangent;
            AddConvex(into, s_points, 3, IconZ, uv);
        }

        // A point at radius r and angle a; direction -1 mirrors it in x, which turns anticlockwise into clockwise.
        static float2 Polar(float r, float a, float direction)
        {
            math.sincos(a, out float sin, out float cos);
            return new float2(direction * r * cos, r * sin);
        }

        // A rectangle of half size h with its corners cut by c.
        static void AddChamfered(AtlasMeshBuilder into, float2 h, float c, float z, float2 uv)
        {
            c = math.min(c, 0.45f * math.cmin(2f * h));
            s_points[0] = new float2(-h.x + c, -h.y);
            s_points[1] = new float2(h.x - c, -h.y);
            s_points[2] = new float2(h.x, -h.y + c);
            s_points[3] = new float2(h.x, h.y - c);
            s_points[4] = new float2(h.x - c, h.y);
            s_points[5] = new float2(-h.x + c, h.y);
            s_points[6] = new float2(-h.x, h.y - c);
            s_points[7] = new float2(-h.x, -h.y + c);
            AddConvex(into, s_points, 8, z, uv);
        }

        /// <summary>A convex polygon at depth z facing -z, whatever the order of its points: a fan, clockwise as the
        /// walker sees it (Unity's front face).</summary>
        static void AddConvex(AtlasMeshBuilder into, float2[] p, int n, float z, float2 uv)
        {
            float area = 0f;
            for (int i = 0; i < n; i++)
            {
                float2 a = p[i], b = p[(i + 1) % n];
                area += a.x * b.y - b.x * a.y;
            }
            var normal = new float3(0f, 0f, -1f);
            int first = into.VertexCount;
            for (int i = 0; i < n; i++)
                into.AddVertex(new float3(p[i].x, p[i].y, z), normal, uv, float2.zero, 1f, 0);
            // Seen from -z, x runs right and y up, so anticlockwise points (area > 0) are walked backwards.
            for (int i = 1; i < n - 1; i++)
            {
                if (area > 0f) into.AddTriangle(first, first + i + 1, first + i);
                else into.AddTriangle(first, first + i, first + i + 1);
            }
        }
    }
}
