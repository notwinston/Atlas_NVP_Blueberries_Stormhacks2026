using System;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Rendering.Room
{
    /// <summary>
    /// The Home shell as mesh data: floor, walls with their openings, and the kerb variant with corner posts
    /// (Master Plan sections 4.5, 4.6 and 8.4). Engine-free: a headless test counts its triangles and measures
    /// its edges. Everything is in the room frame, so the meshes are drawn with an identity transform.
    /// </summary>
    /// <remarks>
    /// The 0.25 m rule exists so that the shell can bend in a curved space, and the warp moves vertices in the
    /// floor plane only. The floor therefore has no edge over 0.25 m at all, and the walls and kerbs have no edge
    /// whose length along the floor is over 0.25 m; a wall's rows may be taller.
    /// When a room is too large for 3,000 triangles the detail is lowered in this order: wall rows double in
    /// height (0.5, 1, 2, 4 m); then the floor's cells become two triangles each (diagonal 0.354 m); then the
    /// floor's pitch doubles (0.5, 1, 2, 4 m).
    /// </remarks>
    public static class RoomShellBuilder
    {
        /// <summary>Grid pitch and longest edge, metres.</summary>
        public const float Pitch = 0.25f;
        public const int MaxTriangles = 3000;
        /// <summary>Metres, outwards from the wall line.</summary>
        public const float KerbThickness = 0.10f;
        /// <summary>Widest kerb column, metres: the diagonal of a cell of the kerb's top, which lies in the floor
        /// plane, is then 0.25 m.</summary>
        public const float KerbColumn = 0.2291f;
        public const float DefaultKerbHeight = 0.10f;
        public const float MinKerbHeight = 0.05f, MaxKerbHeight = 0.9f;
        /// <summary>Corner posts of the kerb variant, metres. No higher than a wall may be in a curved space.</summary>
        public const float PostRadius = 0.06f, PostHeight = 0.9f;
        public const int PostSides = 8;
        /// <summary>Tallest wall row the budget rule may choose, metres.</summary>
        public const float MaxBand = 4f;
        /// <summary>Ambient occlusion at a floor-wall edge, and the distance over which it fades to none.</summary>
        public const float OcclusionAtEdge = 0.6f, OcclusionReach = 0.25f;

        const float Tiny = 1e-4f;

        /// <summary>Ambient occlusion 0.6..1 at a distance from a floor-wall edge, metres.</summary>
        public static float Occlusion(float distance)
        {
            return math.lerp(OcclusionAtEdge, 1f, math.saturate(distance / OcclusionReach));
        }

        /// <summary>Appends the floor: the room's outline, wall to wall, with no holes under furniture.</summary>
        public static void Floor(RoomModel room, AtlasMeshBuilder data)
        {
            Floor(room, data, out _, out _);
        }

        /// <summary>Appends the floor and reports the detail the budget rule chose: the grid pitch, and whether
        /// no edge is over 0.25 m (strict) or whole cells are two triangles.</summary>
        public static void Floor(RoomModel room, AtlasMeshBuilder data, out float pitch, out bool strict)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            if (data == null) throw new ArgumentNullException(nameof(data));
            float2 uv0 = AtlasPalette.Uv(AtlasPalette.Floor);
            // What the walls cost at their coarsest: the floor may use the rest.
            int floorBudget = MaxTriangles - WallTriangles(room, MaxBand);
            var trial = new AtlasMeshBuilder(2048, 8192);
            pitch = Pitch;
            strict = true;
            while (true)
            {
                trial.Clear();
                RoomFloorMesher.Build(room.Outline, null, pitch, strict, room.Outline, uv0, trial);
                if (trial.TriangleCount <= floorBudget || pitch >= MaxBand) break;
                if (strict) strict = false;
                else pitch *= 2f;
            }
            data.Append(trial);
        }

        /// <summary>Appends a floor cut from a polygon with holes (the walkable floor, for a space that draws
        /// only where the walker may go). No ambient occlusion: its rim is not a wall.</summary>
        public static void Floor(WalkablePolygon polygon, AtlasMeshBuilder data, float pitch = Pitch, bool strict = true)
        {
            if (polygon == null) throw new ArgumentNullException(nameof(polygon));
            if (data == null) throw new ArgumentNullException(nameof(data));
            RoomFloorMesher.Build(polygon.Outer, polygon.Holes, pitch, strict, null, AtlasPalette.Uv(AtlasPalette.Floor), data);
        }

        /// <summary>Appends the walls, facing the room, one-sided. band is the row height used after the budget
        /// rule: 0.25, 0.5, 1, 2 or 4 metres.</summary>
        public static void Walls(RoomModel room, int floorTriangles, AtlasMeshBuilder data, out float band)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            if (data == null) throw new ArgumentNullException(nameof(data));
            band = Pitch;
            while (floorTriangles + WallTriangles(room, band) > MaxTriangles && band < MaxBand) band *= 2f;
            EmitWalls(room, band, data);
        }

        /// <summary>Triangles the walls take with rows of the given height.</summary>
        public static int WallTriangles(RoomModel room, float band)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            return EmitWalls(room, band, null);
        }

        /// <summary>
        /// Appends the kerb variant: along every wall a strip kerbHeight high and 0.10 m thick, outwards from the
        /// wall line (inner face, top, outer face), left open under doors and plain openings, and a post at
        /// every wall corner. Drawn instead of the walls when a space is curved or wrapped.
        /// </summary>
        public static void Kerbs(RoomModel room, float kerbHeight, AtlasMeshBuilder data, bool cornerPosts = true)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            if (data == null) throw new ArgumentNullException(nameof(data));
            kerbHeight = math.clamp(kerbHeight, MinKerbHeight, MaxKerbHeight);
            float2 uv0 = AtlasPalette.Uv(AtlasPalette.Kerb);
            RoomWall[] walls = room.Walls;
            float[] cuts = new float[2 + 2 * MaxOpenings(walls)];
            float[] rows = { 0f, kerbHeight };
            for (int w = 0; w < walls.Length; w++)
            {
                RoomWall wall = walls[w];
                float length = wall.Length;
                if (length < Tiny) continue;
                float2 along = (wall.B - wall.A) / length;
                float2 outward = new float2(along.y, -along.x) * KerbThickness;   // the room is on the left
                int cutCount = Cuts(wall, length, cuts);
                for (int c = 0; c + 1 < cutCount; c++)
                {
                    float u0 = cuts[c], u1 = cuts[c + 1];
                    if (u1 - u0 < Tiny) continue;
                    int opening = OpeningAt(wall, 0.5f * (u0 + u1));
                    if (opening >= 0 && wall.Openings[opening].Kind != OpeningKind.Window) continue;
                    int columns = Columns(u1 - u0, KerbColumn);
                    float2 a = PointOn(wall, length, along, u0), b = PointOn(wall, length, along, u1);
                    Panel(data, a, b, columns, rows, 2, uv0);                                   // inner face
                    Panel(data, b + outward, a + outward, columns, rows, 2, uv0);               // outer face
                    KerbTop(data, a, b, outward, columns, kerbHeight, uv0);
                }
            }
            if (cornerPosts) Posts(room, data);
        }

        /// <summary>Triangles of the kerb variant. They do not depend on the kerb's height.</summary>
        public static int KerbTriangles(RoomModel room, bool cornerPosts = true)
        {
            var scratch = new AtlasMeshBuilder(1024, 4096);
            Kerbs(room, DefaultKerbHeight, scratch, cornerPosts);
            return scratch.TriangleCount;
        }

        /// <summary>
        /// Length of the longest triangle edge measured along the floor (its height difference ignored), metres.
        /// This is the length the warp of a curved space bends.
        /// </summary>
        public static float LongestFloorPlaneEdge(AtlasMeshBuilder data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            float longest = 0f;
            for (int t = 0; t + 2 < data.IndexCount; t += 3)
            {
                float2 p0 = data.Vertices[data.Indices[t]].Position.xz;
                float2 p1 = data.Vertices[data.Indices[t + 1]].Position.xz;
                float2 p2 = data.Vertices[data.Indices[t + 2]].Position.xz;
                longest = math.max(longest, math.max(math.distancesq(p0, p1), math.max(math.distancesq(p1, p2), math.distancesq(p2, p0))));
            }
            return math.sqrt(longest);
        }

        // ---- Walls ---------------------------------------------------------------------------------------------

        // Builds the walls, or only counts them when data is null. One code path, so the count is the mesh's.
        static int EmitWalls(RoomModel room, float band, AtlasMeshBuilder data)
        {
            RoomWall[] walls = room.Walls;
            float2 uv0 = AtlasPalette.Uv(AtlasPalette.Wall);
            float[] cuts = new float[2 + 2 * MaxOpenings(walls)];
            float[] rows = new float[RowCapacity(room.MaxWallHeight)];
            int triangles = 0;
            for (int w = 0; w < walls.Length; w++)
            {
                RoomWall wall = walls[w];
                float length = wall.Length;
                if (length < Tiny || wall.Height < Tiny) continue;
                float2 along = (wall.B - wall.A) / length;
                int cutCount = Cuts(wall, length, cuts);
                for (int c = 0; c + 1 < cutCount; c++)
                {
                    float u0 = cuts[c], u1 = cuts[c + 1];
                    if (u1 - u0 < Tiny) continue;
                    int columns = Columns(u1 - u0);
                    float2 a = PointOn(wall, length, along, u0), b = PointOn(wall, length, along, u1);
                    int opening = OpeningAt(wall, 0.5f * (u0 + u1));
                    if (opening < 0)
                    {
                        triangles += WallPanel(data, a, b, columns, 0f, wall.Height, band, rows, uv0);
                        continue;
                    }
                    WallOpening o = wall.Openings[opening];
                    if (o.Bottom > Tiny)
                        triangles += WallPanel(data, a, b, columns, 0f, math.min(o.Bottom, wall.Height), band, rows, uv0);
                    if (o.Top < wall.Height - Tiny)
                        triangles += WallPanel(data, a, b, columns, math.max(o.Top, 0f), wall.Height, band, rows, uv0);
                }
            }
            return triangles;
        }

        static int WallPanel(AtlasMeshBuilder data, float2 a, float2 b, int columns, float y0, float y1, float band,
                             float[] rows, float2 uv0)
        {
            if (y1 - y0 < Tiny) return 0;
            int n = math.max(1, (int)math.ceil((y1 - y0) / band - Tiny));
            int count = 0;
            for (int k = 0; k <= n; k++) rows[count++] = k == n ? y1 : y0 + (y1 - y0) * k / n;
            if (data != null) Panel(data, a, b, columns, rows, count, uv0);
            return 2 * columns * (count - 1);
        }

        static int RowCapacity(float height)
        {
            return 2 + (int)math.ceil(math.max(height, 0f) / Pitch);
        }

        // A vertical panel from floor point a to floor point b, facing the left of a to b, with the given row
        // heights. Occlusion follows the height above the floor.
        static void Panel(AtlasMeshBuilder data, float2 a, float2 b, int columns, float[] rows, int rowCount, float2 uv0)
        {
            float2 direction = math.normalize(b - a);
            var normal = new float3(-direction.y, 0f, direction.x);
            data.Reserve((columns + 1) * rowCount, columns * (rowCount - 1) * 6);
            int first = data.VertexCount;
            for (int r = 0; r < rowCount; r++)
            {
                float y = rows[r];
                float occlusion = Occlusion(y);
                for (int i = 0; i <= columns; i++)
                {
                    // The ends are the given points themselves: a virtual wall is the scanned wall.
                    float2 xz = i == 0 ? a : i == columns ? b : math.lerp(a, b, (float)i / columns);
                    data.AddVertex(new float3(xz.x, y, xz.y), normal, uv0, float2.zero, occlusion, 0);
                }
            }
            int stride = columns + 1;
            for (int r = 0; r + 1 < rowCount; r++)
            {
                for (int i = 0; i < columns; i++)
                {
                    int p00 = first + r * stride + i, p10 = p00 + 1, p01 = p00 + stride, p11 = p01 + 1;
                    data.AddTriangle(p00, p10, p11);
                    data.AddTriangle(p00, p11, p01);
                }
            }
        }

        static void KerbTop(AtlasMeshBuilder data, float2 a, float2 b, float2 outward, int columns, float height, float2 uv0)
        {
            var up = new float3(0f, 1f, 0f);
            float occlusion = Occlusion(height);
            data.Reserve((columns + 1) * 2, columns * 6);
            int first = data.VertexCount;
            for (int i = 0; i <= columns; i++)
            {
                float2 inner = i == 0 ? a : i == columns ? b : math.lerp(a, b, (float)i / columns);
                float2 outer = inner + outward;
                data.AddVertex(new float3(inner.x, height, inner.y), up, uv0, float2.zero, occlusion, 0);
                data.AddVertex(new float3(outer.x, height, outer.y), up, uv0, float2.zero, occlusion, 0);
            }
            for (int i = 0; i < columns; i++)
            {
                int inner0 = first + 2 * i, outer0 = inner0 + 1, inner1 = inner0 + 2, outer1 = inner0 + 3;
                data.AddTriangle(inner0, inner1, outer1);
                data.AddTriangle(inner0, outer1, outer0);
            }
        }

        // One post at every distinct wall end.
        static void Posts(RoomModel room, AtlasMeshBuilder data)
        {
            RoomWall[] walls = room.Walls;
            float2 uv0 = AtlasPalette.Uv(AtlasPalette.Post);
            var placed = new float2[walls.Length * 2];
            int count = 0;
            for (int w = 0; w < walls.Length; w++)
            {
                for (int end = 0; end < 2; end++)
                {
                    float2 corner = end == 0 ? walls[w].A : walls[w].B;
                    bool seen = false;
                    for (int k = 0; k < count && !seen; k++) seen = math.distancesq(placed[k], corner) < 1e-4f;
                    if (seen) continue;
                    placed[count++] = corner;
                    data.AddPost(new float3(corner.x, 0f, corner.y), PostRadius, PostHeight, uv0, PostSides, true, 0,
                                 Pitch, OcclusionAtEdge);
                }
            }
        }

        // ---- Spans along a wall --------------------------------------------------------------------------------

        static int MaxOpenings(RoomWall[] walls)
        {
            int most = 0;
            for (int w = 0; w < walls.Length; w++) most = math.max(most, walls[w].Openings.Length);
            return most;
        }

        // 0, both edges of every opening and the wall's length, ascending.
        static int Cuts(RoomWall wall, float length, float[] cuts)
        {
            int count = 0;
            cuts[count++] = 0f;
            cuts[count++] = length;
            WallOpening[] openings = wall.Openings;
            for (int k = 0; k < openings.Length; k++)
            {
                cuts[count++] = math.clamp(openings[k].Offset, 0f, length);
                cuts[count++] = math.clamp(openings[k].Offset + openings[k].Width, 0f, length);
            }
            Array.Sort(cuts, 0, count);
            return count;
        }

        static int OpeningAt(RoomWall wall, float u)
        {
            WallOpening[] openings = wall.Openings;
            for (int k = 0; k < openings.Length; k++)
                if (u >= openings[k].Offset && u <= openings[k].Offset + openings[k].Width) return k;
            return -1;
        }

        static int Columns(float span, float widest = Pitch)
        {
            return math.max(1, (int)math.ceil(span / widest - Tiny));
        }

        static float2 PointOn(RoomWall wall, float length, float2 along, float u)
        {
            if (u <= 0f) return wall.A;
            if (u >= length) return wall.B;
            return wall.A + along * u;
        }
    }
}
