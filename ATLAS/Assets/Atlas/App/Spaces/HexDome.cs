using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Atlas.App
{
    /// <summary>
    /// The tiles of the hexagon reveal between two spaces: a Goldberg sphere (the dual of an icosahedron
    /// subdivided three times), 80 hexagons and 12 pentagons of about 20 degrees, round the head. Local space has
    /// +z where the walker looked when the switch began, and a hexagon is centred there. The tile the walker
    /// looks at when the reveal begins turns first; partway through its turn it sets off its neighbours, and
    /// they set off theirs, ring after ring, until the tiles behind the walker turn last (<see cref="Rings"/>). Engine-free, so the layout and the order are tested
    /// headless; <see cref="HexTransition"/> makes the mesh from it.
    /// <para>One vertex stream for the whole sphere, one draw: each tile is a fan round its centre, and its
    /// corners are shared with its neighbours, so the closed sphere has no gap to see through.</para>
    /// </summary>
    public sealed class HexDome
    {
        public const int Frequency = 3;
        /// <summary>10 f^2 + 2 tiles: 80 hexagons and 12 pentagons.</summary>
        public const int TileCount = 10 * Frequency * Frequency + 2;
        /// <summary>Metres from the head. The stills are painted at this distance, so it is about where the walls are.</summary>
        public const float Radius = 2.5f;
        /// <summary>Seconds one tile takes to turn over.</summary>
        public const float FlipSeconds = 0.5f;
        /// <summary>Seconds from a tile starting to turn to its neighbours starting: a third of the way over.</summary>
        public const float RingSeconds = 0.17f;

        /// <summary>Each tile's centre on the unit sphere, local space.</summary>
        public readonly float3[] Centres = new float3[TileCount];
        /// <summary>Each tile's corners, 6 or 5, on the unit sphere and in order round the centre.</summary>
        public readonly float3[][] Corners = new float3[TileCount][];
        /// <summary>The tiles that share an edge with each tile: 6 round a hexagon, 5 round a pentagon.</summary>
        public readonly int[][] Neighbours = new int[TileCount][];
        /// <summary>The line through a tile's centre it turns about: the local vertical, laid in the tile.</summary>
        public readonly float3[] Axes = new float3[TileCount];

        // The mesh, one fan per tile: the centre vertex first, then the corners.
        /// <summary>Rest position, metres, local space.</summary>
        public readonly List<float3> Positions = new List<float3>(TileCount * 7);
        /// <summary>The turning axis of the vertex's tile.</summary>
        public readonly List<float3> VertexAxes = new List<float3>(TileCount * 7);
        /// <summary>xyz: the centre of the vertex's tile, metres; w: the tile's index.</summary>
        public readonly List<float4> VertexCentres = new List<float4>(TileCount * 7);
        /// <summary>0 at a tile's centre, 1 on its edge: the shader draws the seam from it.</summary>
        public readonly List<float> Rims = new List<float>(TileCount * 7);
        public readonly List<int> Triangles = new List<int>(TileCount * 6 * 3);

        public HexDome()
        {
            List<float3> points;
            List<int3> triangles;
            Geodesic(out points, out triangles);
            if (points.Count != TileCount) throw new InvalidOperationException("HexDome: " + points.Count + " points");

            // The triangles round each point; their centres are the corners of its tile.
            var around = new List<int>[TileCount];
            for (int i = 0; i < TileCount; i++) around[i] = new List<int>(6);
            for (int t = 0; t < triangles.Count; t++)
            {
                int3 tri = triangles[t];
                around[tri.x].Add(t);
                around[tri.y].Add(t);
                around[tri.z].Add(t);
            }

            var corners = new List<float3>(6);
            var angles = new List<float>(6);
            for (int i = 0; i < TileCount; i++)
            {
                float3 n = points[i];
                float3 t1 = Perpendicular(n);
                float3 t2 = math.cross(n, t1);
                corners.Clear();
                angles.Clear();
                for (int k = 0; k < around[i].Count; k++)
                {
                    int3 tri = triangles[around[i][k]];
                    float3 c = math.normalize(points[tri.x] + points[tri.y] + points[tri.z]);
                    corners.Add(c);
                    angles.Add(math.atan2(math.dot(c, t2), math.dot(c, t1)));
                }
                SortByAngle(corners, angles);

                Centres[i] = n;
                Corners[i] = corners.ToArray();
                Axes[i] = AxisOf(n);
                AddFan(i);
            }

            // Two tiles share an edge when their points share a triangle of the subdivision.
            var neighbours = new List<int>[TileCount];
            for (int i = 0; i < TileCount; i++) neighbours[i] = new List<int>(6);
            for (int t = 0; t < triangles.Count; t++)
            {
                int3 tri = triangles[t];
                Link(neighbours, tri.x, tri.y);
                Link(neighbours, tri.y, tri.z);
                Link(neighbours, tri.z, tri.x);
            }
            for (int i = 0; i < TileCount; i++) Neighbours[i] = neighbours[i].ToArray();
        }

        static void Link(List<int>[] neighbours, int a, int b)
        {
            if (!neighbours[a].Contains(b)) neighbours[a].Add(b);
            if (!neighbours[b].Contains(a)) neighbours[b].Add(a);
        }

        /// <summary>The tile whose centre is nearest a direction (unit, local space).</summary>
        public int TileAt(float3 direction)
        {
            int best = 0;
            float bestDot = float.MinValue;
            for (int i = 0; i < TileCount; i++)
            {
                float d = math.dot(Centres[i], direction);
                if (d <= bestDot) continue;
                bestDot = d;
                best = i;
            }
            return best;
        }

        /// <summary>
        /// The ripple from one tile: each tile's ring, the number of steps across neighbours from first (0 for
        /// first itself). Returns the outermost ring. rings must hold TileCount entries.
        /// </summary>
        public int Rings(int first, int[] rings)
        {
            for (int i = 0; i < TileCount; i++) rings[i] = -1;
            var queue = new Queue<int>(TileCount);
            rings[first] = 0;
            queue.Enqueue(first);
            int outer = 0;
            while (queue.Count > 0)
            {
                int tile = queue.Dequeue();
                foreach (int next in Neighbours[tile])
                {
                    if (rings[next] >= 0) continue;
                    rings[next] = rings[tile] + 1;
                    outer = math.max(outer, rings[next]);
                    queue.Enqueue(next);
                }
            }
            return outer;
        }

        /// <summary>Seconds after the reveal begins that a tile of a ring starts to turn.</summary>
        public static float Delay(int ring) { return ring * RingSeconds; }

        /// <summary>The whole reveal for a ripple whose outermost ring is outer: every tile has turned.</summary>
        public static float RevealSeconds(int outer) { return Delay(outer) + FlipSeconds; }

        /// <summary>
        /// How far a tile has turned at a time of the reveal: 0 face on, 1 turned over. Eased in and out.
        /// Negative times (the next space still loading) are 0. The shader computes the same.
        /// </summary>
        public static float Turn(float delay, float revealSeconds)
        {
            float x = math.saturate((revealSeconds - delay) / FlipSeconds);
            return x * x * (3f - 2f * x);
        }

        void AddFan(int tile)
        {
            float3 centre = Centres[tile] * Radius;
            var packed = new float4(centre, tile);
            int first = Positions.Count;
            AddVertex(centre, tile, packed, 0f);
            float3[] corners = Corners[tile];
            for (int k = 0; k < corners.Length; k++) AddVertex(corners[k] * Radius, tile, packed, 1f);
            for (int k = 0; k < corners.Length; k++)
            {
                Triangles.Add(first);
                Triangles.Add(first + 1 + k);
                Triangles.Add(first + 1 + (k + 1) % corners.Length);
            }
        }

        void AddVertex(float3 position, int tile, float4 centre, float rim)
        {
            Positions.Add(position);
            VertexAxes.Add(Axes[tile]);
            VertexCentres.Add(centre);
            Rims.Add(rim);
        }

        // The vertical laid in the tile, so the tiles turn like revolving doors; at the poles, the local x.
        static float3 AxisOf(float3 n)
        {
            float3 up = new float3(0f, 1f, 0f);
            float3 a = up - math.dot(up, n) * n;
            if (math.lengthsq(a) < 1e-4f)
            {
                float3 x = new float3(1f, 0f, 0f);
                a = x - math.dot(x, n) * n;
            }
            return math.normalize(a);
        }

        static float3 Perpendicular(float3 n)
        {
            float3 other = math.abs(n.y) < 0.9f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f);
            return math.normalize(math.cross(other, n));
        }

        static void SortByAngle(List<float3> corners, List<float> angles)
        {
            // At most six: an insertion sort, both lists in step.
            for (int i = 1; i < corners.Count; i++)
            {
                float a = angles[i];
                float3 c = corners[i];
                int j = i - 1;
                while (j >= 0 && angles[j] > a)
                {
                    angles[j + 1] = angles[j];
                    corners[j + 1] = corners[j];
                    j--;
                }
                angles[j + 1] = a;
                corners[j + 1] = c;
            }
        }

        // The icosahedron subdivided f times on each edge and pushed onto the unit sphere, turned so that the
        // centre of one face, which is a point of the subdivision for f = 3, lies on +z.
        static void Geodesic(out List<float3> points, out List<int3> triangles)
        {
            float phi = (1f + math.sqrt(5f)) * 0.5f;
            var ico = new List<float3>(12);
            for (int s1 = -1; s1 <= 1; s1 += 2)
            for (int s2 = -1; s2 <= 1; s2 += 2)
            {
                ico.Add(math.normalize(new float3(0f, s1, s2 * phi)));
                ico.Add(math.normalize(new float3(s1, s2 * phi, 0f)));
                ico.Add(math.normalize(new float3(s2 * phi, 0f, s1)));
            }

            // The faces: every three points pairwise one edge apart, wound outwards.
            float edge = float.MaxValue;
            for (int i = 1; i < ico.Count; i++) edge = math.min(edge, math.distance(ico[0], ico[i]));
            float near = edge * 1.01f;
            var faces = new List<int3>(20);
            for (int a = 0; a < 12; a++)
            for (int b = a + 1; b < 12; b++)
            {
                if (math.distance(ico[a], ico[b]) > near) continue;
                for (int c = b + 1; c < 12; c++)
                {
                    if (math.distance(ico[a], ico[c]) > near || math.distance(ico[b], ico[c]) > near) continue;
                    bool outwards = math.dot(math.cross(ico[b] - ico[a], ico[c] - ico[a]), ico[a] + ico[b] + ico[c]) > 0f;
                    faces.Add(outwards ? new int3(a, b, c) : new int3(a, c, b));
                }
            }

            int3 f0 = faces[0];
            quaternion turn = FromTo(math.normalize(ico[f0.x] + ico[f0.y] + ico[f0.z]), new float3(0f, 0f, 1f));
            for (int i = 0; i < ico.Count; i++) ico[i] = math.mul(turn, ico[i]);

            points = new List<float3>(TileCount);
            triangles = new List<int3>(20 * Frequency * Frequency);
            var index = new Dictionary<long, int>(TileCount);
            var grid = new int[Frequency + 1, Frequency + 1];
            for (int f = 0; f < faces.Count; f++)
            {
                float3 A = ico[faces[f].x], B = ico[faces[f].y], C = ico[faces[f].z];
                for (int i = 0; i <= Frequency; i++)
                for (int j = 0; i + j <= Frequency; j++)
                {
                    int k = Frequency - i - j;
                    float3 p = math.normalize((i * A + j * B + k * C) / Frequency);
                    grid[i, j] = PointIndex(p, points, index);
                }
                for (int i = 0; i < Frequency; i++)
                for (int j = 0; i + j < Frequency; j++)
                {
                    triangles.Add(new int3(grid[i, j], grid[i + 1, j], grid[i, j + 1]));
                    if (i + j < Frequency - 1)
                        triangles.Add(new int3(grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]));
                }
            }
        }

        // Points on shared edges are made once per face: the same point is found by its rounded position.
        static int PointIndex(float3 p, List<float3> points, Dictionary<long, int> index)
        {
            long key = ((long)math.round(p.x * 1000f) + 2000) * 16000000L
                       + ((long)math.round(p.y * 1000f) + 2000) * 4000L
                       + ((long)math.round(p.z * 1000f) + 2000);
            int found;
            if (index.TryGetValue(key, out found)) return found;
            index.Add(key, points.Count);
            points.Add(p);
            return points.Count - 1;
        }

        static quaternion FromTo(float3 from, float3 to)
        {
            float3 axis = math.cross(from, to);
            float d = math.clamp(math.dot(from, to), -1f, 1f);
            if (math.lengthsq(axis) < 1e-8f)
                return d > 0f ? quaternion.identity : quaternion.AxisAngle(Perpendicular(from), math.PI);
            return quaternion.AxisAngle(math.normalize(axis), math.acos(d));
        }
    }
}
