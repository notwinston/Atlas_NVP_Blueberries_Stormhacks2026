using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Atlas.Rendering.Room
{
    /// <summary>
    /// Triangulates a floor polygon with holes on a square grid (Master Plan sections 4.5 and 8.4). Engine-free.
    /// Each grid row is cut into trapezoids between the polygon's edges (even-odd rule), each trapezoid is cut
    /// by the grid columns, and every convex piece is fanned. Nothing is assumed about the polygon except that
    /// its rings do not cross: it may be concave, wound either way, and off the grid.
    /// </summary>
    public static class RoomFloorMesher
    {
        /// <summary>Metres: two coordinates closer than this are the same.</summary>
        const double Eps = 1e-6;
        /// <summary>Square metres: smaller pieces are dropped.</summary>
        const double MinArea = 1e-9;

        struct Edge
        {
            public double Ax, Az, Bx, Bz;

            public double XAt(double z)
            {
                // The polygon's own coordinates are kept wherever a level passes through a vertex, so that the
                // floor's rim is the scanned outline exactly.
                if (math.abs(z - Az) <= Eps) return Ax;
                if (math.abs(z - Bz) <= Eps) return Bx;
                return Ax + (Bx - Ax) * (z - Az) / (Bz - Az);
            }
        }

        sealed class Scratch
        {
            public readonly List<Edge> Edges = new List<Edge>(64);
            public readonly List<double> VertexZ = new List<double>(64);
            public readonly List<double> Levels = new List<double>(8);
            public List<double> EventsLow = new List<double>(16), EventsHigh = new List<double>(16);
            public double[] CrossKey = new double[32], CrossX0 = new double[32], CrossX1 = new double[32];
            public readonly double2[] Piece = new double2[12], Clip = new double2[12];
            public readonly List<double2> Rim = new List<double2>(24);
        }

        /// <summary>
        /// Appends the polygon at y = 0, facing up, and returns the number of triangles added.
        /// </summary>
        /// <param name="outer">Outer ring, metres, floor coordinates (x, z).</param>
        /// <param name="holes">Rings cut out of it; may be null.</param>
        /// <param name="pitch">Grid pitch, metres. The grid passes through the origin of the room frame.</param>
        /// <param name="strict">True: every cell is fanned about its centroid and long rim edges are split, so
        /// that no edge is longer than <paramref name="maxEdge"/> when the pitch is not. False: a whole cell is
        /// two triangles, whose diagonal is 1.41 pitches.</param>
        /// <param name="occlusionRing">Ring whose edges darken the floor near them (the wall line), or null.</param>
        /// <param name="uv0">Palette coordinate.</param>
        /// <param name="maxEdge">Longest edge allowed in strict mode, metres.</param>
        public static int Build(float2[] outer, float2[][] holes, float pitch, bool strict, float2[] occlusionRing,
                                float2 uv0, AtlasMeshBuilder data, float maxEdge = RoomShellBuilder.Pitch)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (!(pitch > 1e-3f)) throw new ArgumentOutOfRangeException(nameof(pitch), "The grid pitch must be positive.");
            if (outer == null || outer.Length < 3) return 0;

            var s = new Scratch();
            AddRing(s, outer);
            if (holes != null)
                for (int h = 0; h < holes.Length; h++) AddRing(s, holes[h]);
            if (s.Edges.Count < 3) return 0;

            double zMin = double.MaxValue, zMax = double.MinValue;
            for (int k = 0; k < s.VertexZ.Count; k++)
            {
                zMin = math.min(zMin, s.VertexZ[k]);
                zMax = math.max(zMax, s.VertexZ[k]);
            }
            s.VertexZ.Sort();

            int before = data.TriangleCount;
            double p = pitch;
            int j0 = (int)math.floor(zMin / p + Eps), j1 = (int)math.ceil(zMax / p - Eps);
            for (int j = j0; j < j1; j++)
            {
                double rowLow = j * p, rowHigh = (j + 1) * p;
                // Levels of this row: its two grid lines and every vertex strictly between them. Between two
                // neighbouring levels no edge starts or ends, so the edges that cross the slab keep their order.
                s.Levels.Clear();
                s.Levels.Add(rowLow);
                for (int k = 0; k < s.VertexZ.Count; k++)
                {
                    double z = s.VertexZ[k];
                    if (z > s.Levels[s.Levels.Count - 1] + Eps && z < rowHigh - Eps) s.Levels.Add(z);
                }
                s.Levels.Add(rowHigh);

                Events(s, s.Levels[0], s.EventsLow);
                for (int l = 0; l + 1 < s.Levels.Count; l++)
                {
                    double low = s.Levels[l], high = s.Levels[l + 1];
                    Events(s, high, s.EventsHigh);
                    Slab(s, low, high, p, strict, occlusionRing, uv0, maxEdge, data);
                    List<double> swap = s.EventsLow;
                    s.EventsLow = s.EventsHigh;
                    s.EventsHigh = swap;
                }
            }
            return data.TriangleCount - before;
        }

        /// <summary>Area of the polygon, square metres: the outer ring less the holes, whatever their winding.</summary>
        public static double Area(float2[] outer, float2[][] holes)
        {
            double area = math.abs(RingArea(outer));
            if (holes != null)
                for (int h = 0; h < holes.Length; h++) area -= math.abs(RingArea(holes[h]));
            return area;
        }

        /// <summary>Distance from a floor point to the nearest edge of a closed ring, metres.</summary>
        public static float DistanceToRing(float2[] ring, float2 point)
        {
            float best = float.MaxValue;
            int n = ring.Length;
            for (int k = 0, prev = n - 1; k < n; prev = k++)
            {
                float2 a = ring[prev], ab = ring[k] - a;
                float lengthSq = math.lengthsq(ab);
                float t = lengthSq > 1e-12f ? math.saturate(math.dot(point - a, ab) / lengthSq) : 0f;
                best = math.min(best, math.distancesq(point, a + ab * t));
            }
            return math.sqrt(best);
        }

        static double RingArea(float2[] ring)
        {
            if (ring == null) return 0.0;
            double total = 0.0;
            int n = ring.Length;
            for (int k = 0, prev = n - 1; k < n; prev = k++)
                total += (double)ring[prev].x * ring[k].y - (double)ring[k].x * ring[prev].y;
            return 0.5 * total;
        }

        static void AddRing(Scratch s, float2[] ring)
        {
            if (ring == null || ring.Length < 3) return;
            int n = ring.Length;
            for (int k = 0, prev = n - 1; k < n; prev = k++)
            {
                var edge = new Edge { Ax = ring[prev].x, Az = ring[prev].y, Bx = ring[k].x, Bz = ring[k].y };
                s.VertexZ.Add(edge.Bz);
                if (math.abs(edge.Ax - edge.Bx) <= Eps && math.abs(edge.Az - edge.Bz) <= Eps) continue;
                s.Edges.Add(edge);
            }
        }

        // Every x at which the polygon's boundary touches the line z = level, ascending. Pieces on both sides
        // of the line take these as vertices, so no vertex of one piece lies inside an edge of its neighbour.
        static void Events(Scratch s, double level, List<double> events)
        {
            events.Clear();
            for (int k = 0; k < s.Edges.Count; k++)
            {
                Edge e = s.Edges[k];
                bool aOn = math.abs(e.Az - level) <= Eps, bOn = math.abs(e.Bz - level) <= Eps;
                if (aOn) events.Add(e.Ax);
                if (bOn) events.Add(e.Bx);
                if (!aOn && !bOn && (e.Az < level) != (e.Bz < level)) events.Add(e.XAt(level));
            }
            events.Sort();
        }

        static void Slab(Scratch s, double low, double high, double pitch, bool strict, float2[] occlusionRing,
                         float2 uv0, float maxEdge, AtlasMeshBuilder data)
        {
            if (high - low <= Eps) return;
            double mid = 0.5 * (low + high);
            int count = 0;
            for (int k = 0; k < s.Edges.Count; k++)
            {
                Edge e = s.Edges[k];
                if ((e.Az < mid) == (e.Bz < mid)) continue;
                if (count == s.CrossKey.Length)
                {
                    Array.Resize(ref s.CrossKey, count * 2);
                    Array.Resize(ref s.CrossX0, count * 2);
                    Array.Resize(ref s.CrossX1, count * 2);
                }
                double x0 = e.XAt(low), x1 = e.XAt(high);
                s.CrossKey[count] = x0 + x1;
                s.CrossX0[count] = x0;
                s.CrossX1[count] = x1;
                count++;
            }
            // Insertion sort on the mid-slab position, carrying both ends: the lists are short.
            for (int a = 1; a < count; a++)
            {
                double key = s.CrossKey[a], x0 = s.CrossX0[a], x1 = s.CrossX1[a];
                int b = a - 1;
                while (b >= 0 && s.CrossKey[b] > key)
                {
                    s.CrossKey[b + 1] = s.CrossKey[b];
                    s.CrossX0[b + 1] = s.CrossX0[b];
                    s.CrossX1[b + 1] = s.CrossX1[b];
                    b--;
                }
                s.CrossKey[b + 1] = key;
                s.CrossX0[b + 1] = x0;
                s.CrossX1[b + 1] = x1;
            }

            // Even-odd: inside between the first and second crossing, the third and fourth, and so on.
            for (int k = 0; k + 1 < count; k += 2)
            {
                double l0 = s.CrossX0[k], l1 = s.CrossX1[k], r0 = s.CrossX0[k + 1], r1 = s.CrossX1[k + 1];
                double xMin = math.min(l0, l1), xMax = math.max(r0, r1);
                int i0 = (int)math.floor(xMin / pitch + Eps), i1 = (int)math.ceil(xMax / pitch - Eps);
                for (int i = i0; i < i1; i++)
                {
                    // Counter-clockwise in (x, z).
                    s.Piece[0] = new double2(l0, low);
                    s.Piece[1] = new double2(r0, low);
                    s.Piece[2] = new double2(r1, high);
                    s.Piece[3] = new double2(l1, high);
                    int n = ClipX(s.Piece, 4, s.Clip, i * pitch, true);
                    n = ClipX(s.Clip, n, s.Piece, (i + 1) * pitch, false);
                    n = Tidy(s.Piece, n);
                    if (n >= 3) Emit(s, n, low, high, strict, occlusionRing, uv0, maxEdge, data);
                }
            }
        }

        // Sutherland-Hodgman against one vertical line; the subject is convex.
        static int ClipX(double2[] source, int count, double2[] target, double x, bool keepGreater)
        {
            int n = 0;
            for (int k = 0, prev = count - 1; k < count; prev = k++)
            {
                double2 a = source[prev], b = source[k];
                bool aIn = keepGreater ? a.x >= x - Eps : a.x <= x + Eps;
                bool bIn = keepGreater ? b.x >= x - Eps : b.x <= x + Eps;
                if (aIn != bIn)
                    target[n++] = new double2(x, a.y + (b.y - a.y) * (x - a.x) / (b.x - a.x));
                if (bIn) target[n++] = b;
            }
            return n;
        }

        static int Tidy(double2[] points, int count)
        {
            int n = 0;
            for (int k = 0; k < count; k++)
            {
                if (n > 0 && math.distancesq(points[k], points[n - 1]) <= Eps * Eps) continue;
                points[n++] = points[k];
            }
            while (n > 1 && math.distancesq(points[0], points[n - 1]) <= Eps * Eps) n--;
            return n;
        }

        static void Emit(Scratch s, int count, double low, double high, bool strict, float2[] occlusionRing,
                         float2 uv0, float maxEdge, AtlasMeshBuilder data)
        {
            double2[] piece = s.Piece;
            double twiceArea = 0.0, cx = 0.0, cz = 0.0;
            for (int k = 0, prev = count - 1; k < count; prev = k++)
            {
                double w = piece[prev].x * piece[k].y - piece[k].x * piece[prev].y;
                twiceArea += w;
                cx += (piece[prev].x + piece[k].x) * w;
                cz += (piece[prev].y + piece[k].y) * w;
            }
            if (twiceArea <= 2.0 * MinArea) return;
            var centroid = new double2(cx / (3.0 * twiceArea), cz / (3.0 * twiceArea));

            // The rim: the piece's corners, plus the boundary's events on its two level edges and, in strict
            // mode, the points that split a long slanted edge. A slanted edge lies on the polygon's boundary and
            // has no neighbour, so splitting it opens no crack.
            List<double2> rim = s.Rim;
            rim.Clear();
            for (int k = 0; k < count; k++)
            {
                double2 a = piece[k], b = piece[(k + 1) % count];
                rim.Add(a);
                if (math.abs(a.y - b.y) <= Eps)
                {
                    List<double> events = math.abs(a.y - low) <= Eps ? s.EventsLow : s.EventsHigh;
                    double from = math.min(a.x, b.x) + Eps, to = math.max(a.x, b.x) - Eps;
                    if (a.x < b.x)
                    {
                        for (int e = 0; e < events.Count; e++)
                            if (events[e] > from && events[e] < to && events[e] > rim[rim.Count - 1].x + Eps)
                                rim.Add(new double2(events[e], a.y));
                    }
                    else
                    {
                        for (int e = events.Count - 1; e >= 0; e--)
                            if (events[e] > from && events[e] < to && events[e] < rim[rim.Count - 1].x - Eps)
                                rim.Add(new double2(events[e], a.y));
                    }
                }
                else if (strict)
                {
                    double length = math.distance(a, b);
                    int parts = (int)math.ceil(length / maxEdge - 1e-4);
                    for (int q = 1; q < parts; q++) rim.Add(math.lerp(a, b, (double)q / parts));
                }
            }

            int n = rim.Count;
            if (!strict && n == 4)
            {
                // A plain quad: two triangles. Reversed, because counter-clockwise in (x, z) is seen
                // counter-clockwise from above and Unity's front face is clockwise.
                data.Reserve(4, 6);
                int v0 = Vertex(data, rim[0], occlusionRing, uv0), v1 = Vertex(data, rim[1], occlusionRing, uv0);
                int v2 = Vertex(data, rim[2], occlusionRing, uv0), v3 = Vertex(data, rim[3], occlusionRing, uv0);
                data.AddTriangle(v0, v2, v1);
                data.AddTriangle(v0, v3, v2);
                return;
            }

            data.Reserve(n + 1, n * 3);
            int centre = Vertex(data, centroid, occlusionRing, uv0);
            int first = data.VertexCount;
            for (int k = 0; k < n; k++) Vertex(data, rim[k], occlusionRing, uv0);
            for (int k = 0; k < n; k++) data.AddTriangle(centre, first + (k + 1) % n, first + k);
        }

        static int Vertex(AtlasMeshBuilder data, double2 point, float2[] occlusionRing, float2 uv0)
        {
            var xz = new float2((float)point.x, (float)point.y);
            float occlusion = occlusionRing == null
                ? 1f
                : RoomShellBuilder.Occlusion(DistanceToRing(occlusionRing, xz));
            // Line coordinate: one unit per 0.5 m of floor, which draws the floor grid (Master Plan section 8.1).
            return data.AddVertex(new float3(xz.x, 0f, xz.y), new float3(0f, 1f, 0f), uv0,
                                  xz / AtlasMeshBuilder.GridMetres, occlusion, 255);
        }
    }
}
