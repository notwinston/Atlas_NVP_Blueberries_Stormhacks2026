using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Atlas.Scan
{
    /// <summary>
    /// Scan route 1 (Master Plan section 4.2, task P1-C02): from the floor corners a walker tapped, or the
    /// path walked along the walls, to a clean floor outline. It closes the ring, puts the corners in order,
    /// makes it counter-clockwise and squares it up to the dominant wall direction. Engine-free; floor points
    /// are (x, z) in any one flat frame.
    /// </summary>
    public static class OutlineBuilder
    {
        /// <summary>No edge of an outline is shorter, metres (RoomModel rule R1).</summary>
        public const float MinEdge = 0.05f;
        /// <summary>Most points an outline may have (RoomModel schema).</summary>
        public const int MaxPoints = 255;
        /// <summary>An edge within this of the dominant wall direction, or of its normal, is squared up. Degrees.</summary>
        public const float SquareTolerance = 15f;
        /// <summary>A corner nearer than this to the line through its neighbours is no corner, metres.</summary>
        public const float CollinearTolerance = 0.03f;
        /// <summary>How far a walker's path along the walls lies inside them, metres.</summary>
        public const float PathWallOffset = 0.30f;
        /// <summary>A walked path is simplified to within this, metres.</summary>
        public const float PathTolerance = 0.10f;

        /// <summary>Signed area: positive when the ring is counter-clockwise seen from above (+X right, +Z up).</summary>
        public static float SignedArea(float2[] ring, int count)
        {
            double total = 0.0;
            for (int k = 0; k < count; k++)
            {
                float2 p = ring[k], q = ring[(k + 1) % count];
                total += (double)p.x * q.y - (double)q.x * p.y;
            }
            return (float)(0.5 * total);
        }

        /// <summary>True when no two edges that are not neighbours touch or cross.</summary>
        public static bool IsSimple(float2[] ring, int count)
        {
            if (count < 3) return false;
            for (int a = 0; a < count; a++)
            {
                float2 p = ring[a], q = ring[(a + 1) % count];
                for (int b = a + 1; b < count; b++)
                {
                    if (b == a + 1 || (a == 0 && b == count - 1)) continue;      // neighbours share a corner
                    if (SegmentsTouch(p, q, ring[b], ring[(b + 1) % count])) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The test a corrected outline must pass (contract section 10.3): 3 to 255 finite points, no edge
        /// under 0.05 m, simple. Either winding.
        /// </summary>
        public static bool IsValid(float2[] ring, int count)
        {
            if (ring == null || count < 3 || count > MaxPoints || count > ring.Length) return false;
            for (int k = 0; k < count; k++)
            {
                if (!math.all(math.isfinite(ring[k]))) return false;
                if (math.distance(ring[k], ring[(k + 1) % count]) < MinEdge) return false;
            }
            return IsSimple(ring, count) && math.abs(SignedArea(ring, count)) > 1e-4f;
        }

        /// <summary>A copy of the first count points, counter-clockwise.</summary>
        public static float2[] CounterClockwise(float2[] ring, int count)
        {
            var copy = new float2[count];
            Array.Copy(ring, copy, count);
            if (SignedArea(copy, count) < 0f) Array.Reverse(copy);
            return copy;
        }

        /// <summary>
        /// Cleans a ring: a last point that repeats the first is dropped (the ring is never closed by
        /// repetition), points nearer than 0.05 m to their predecessor are dropped, corners that lie on the
        /// line through their neighbours are dropped, and the result is counter-clockwise. Null when fewer
        /// than three corners remain or the ring crosses itself.
        /// </summary>
        public static float2[] Normalise(float2[] points, int count)
        {
            if (points == null || count < 3) return null;
            var ring = new List<float2>(count);
            for (int k = 0; k < count; k++)
            {
                if (!math.all(math.isfinite(points[k]))) continue;
                if (ring.Count > 0 && math.distance(ring[ring.Count - 1], points[k]) < MinEdge) continue;
                ring.Add(points[k]);
            }
            while (ring.Count > 1 && math.distance(ring[ring.Count - 1], ring[0]) < MinEdge) ring.RemoveAt(ring.Count - 1);
            DropCollinear(ring);
            if (ring.Count < 3 || ring.Count > MaxPoints) return null;
            float2[] result = ring.ToArray();
            if (!IsValid(result, result.Length)) return null;
            if (SignedArea(result, result.Length) < 0f) Array.Reverse(result);
            return result;
        }

        /// <summary>
        /// The outline from tapped floor corners, in the order tapped. When that order makes the ring cross
        /// itself the corners are put in order round their centre instead. Null with fewer than three usable
        /// corners.
        /// </summary>
        public static float2[] FromCorners(float2[] corners, int count, bool squareUp = true)
        {
            if (corners == null || count < 3) return null;
            float2[] ring = Normalise(corners, count);
            if (ring == null)
            {
                // Tapped out of order: sort by direction from the centre. Right for every room that can be
                // seen whole from one point, which is the room a walker can tap.
                var sorted = new float2[count];
                Array.Copy(corners, sorted, count);
                float2 centre = float2.zero;
                for (int k = 0; k < count; k++) centre += sorted[k];
                centre /= count;
                Array.Sort(sorted, (a, b) =>
                    math.atan2(a.y - centre.y, a.x - centre.x).CompareTo(math.atan2(b.y - centre.y, b.x - centre.x)));
                ring = Normalise(sorted, count);
                if (ring == null) return null;
            }
            return squareUp ? SquareUp(ring) : ring;
        }

        /// <summary>
        /// The outline from the path walked along the walls: the path is closed and simplified, then pushed
        /// outwards by wallOffset, because a walker's body keeps that far from the wall. Null when the path
        /// encloses less than a quarter of a square metre.
        /// </summary>
        public static float2[] FromPath(float2[] path, int count, float wallOffset = PathWallOffset, bool squareUp = true)
        {
            if (path == null || count < 3) return null;
            float2[] simplified = Simplify(path, count, PathTolerance);
            float2[] ring = Normalise(simplified, simplified.Length);
            if (ring == null || SignedArea(ring, ring.Length) < 0.25f) return null;
            if (squareUp) ring = SquareUp(ring);
            if (wallOffset > 0f)
            {
                float2[] grown = Offset(ring, wallOffset);
                if (IsValid(grown, grown.Length)) ring = grown;
            }
            return ring;
        }

        /// <summary>
        /// Douglas-Peucker on a closed ring: no dropped point lies further than tolerance from the result.
        /// The ring is split at its first point and at the point furthest from it.
        /// </summary>
        public static float2[] Simplify(float2[] ring, int count, float tolerance)
        {
            if (count > ring.Length) count = ring.Length;
            if (count < 4) { var same = new float2[count]; Array.Copy(ring, same, count); return same; }
            int far = 0;
            float farDistance = -1f;
            for (int k = 1; k < count; k++)
            {
                float d = math.distancesq(ring[k], ring[0]);
                if (d > farDistance) { farDistance = d; far = k; }
            }
            var keep = new bool[count + 1];
            keep[0] = keep[far] = true;
            SimplifyRange(ring, count, 0, far, tolerance, keep);
            SimplifyRange(ring, count, far, count, tolerance, keep);      // index count is the first point again
            var result = new List<float2>();
            for (int k = 0; k < count; k++) if (keep[k]) result.Add(ring[k]);
            return result.ToArray();
        }

        /// <summary>
        /// Squares a counter-clockwise ring up: the dominant wall direction is found from its edges (weighted
        /// by length, modulo 90 degrees), every edge within 15 degrees of that direction or of its normal is
        /// laid exactly along it, and the corners are moved to where those lines meet. Edges at other angles
        /// keep their direction. Returns the input when the result would not be a valid outline.
        /// </summary>
        public static float2[] SquareUp(float2[] ring, float toleranceDegrees = SquareTolerance)
        {
            if (ring == null || ring.Length < 3) return ring;
            int n = ring.Length;
            double sin4 = 0.0, cos4 = 0.0;
            for (int k = 0; k < n; k++)
            {
                double dx = (double)ring[(k + 1) % n].x - ring[k].x, dz = (double)ring[(k + 1) % n].y - ring[k].y;
                double length = Math.Sqrt(dx * dx + dz * dz);
                if (length <= 0.0) continue;
                double a = 4.0 * Math.Atan2(dz, dx);
                sin4 += length * Math.Sin(a);
                cos4 += length * Math.Cos(a);
            }
            if (Math.Abs(sin4) < 1e-9 && Math.Abs(cos4) < 1e-9) return ring;
            double theta = 0.25 * Math.Atan2(sin4, cos4);
            double ct = Math.Cos(theta), st = Math.Sin(theta);

            // The ring in the wall-aligned frame.
            var x = new double[n];
            var z = new double[n];
            for (int k = 0; k < n; k++)
            {
                x[k] = ct * ring[k].x + st * ring[k].y;
                z[k] = -st * ring[k].x + ct * ring[k].y;
            }
            // Edge k runs from corner k to corner k + 1. Kind 1: along x (its line is a z value); kind 2:
            // along z (its line is an x value); kind 0: neither.
            double slope = Math.Tan(toleranceDegrees * Math.PI / 180.0);
            var kind = new int[n];
            var line = new double[n];
            var weight = new double[n];
            for (int k = 0; k < n; k++)
            {
                int next = (k + 1) % n;
                double dx = x[next] - x[k], dz = z[next] - z[k];
                weight[k] = Math.Sqrt(dx * dx + dz * dz);
                if (Math.Abs(dz) <= slope * Math.Abs(dx)) { kind[k] = 1; line[k] = 0.5 * (z[k] + z[next]); }
                else if (Math.Abs(dx) <= slope * Math.Abs(dz)) { kind[k] = 2; line[k] = 0.5 * (x[k] + x[next]); }
            }
            // Neighbouring edges of one kind whose lines nearly coincide are one wall.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int k = 0; k < n; k++)
                {
                    int next = (k + 1) % n;
                    if (kind[k] == 0 || kind[k] != kind[next] || Math.Abs(line[k] - line[next]) >= MinEdge) continue;
                    double total = weight[k] + weight[next];
                    double shared = total > 0.0 ? (line[k] * weight[k] + line[next] * weight[next]) / total : line[k];
                    line[k] = line[next] = shared;
                }
            }
            var squared = new List<float2>(n + 4);
            for (int k = 0; k < n; k++)
            {
                int before = (k + n - 1) % n;      // the edge that arrives at corner k; edge k leaves it
                int a = kind[before], b = kind[k];
                if (a == 1 && b == 2) Add(squared, line[k], line[before], ct, st);
                else if (a == 2 && b == 1) Add(squared, line[before], line[k], ct, st);
                else if (a == 1 && b == 1)
                {
                    // Two walls along x at different depths: the step between them was not tapped.
                    Add(squared, x[k], line[before], ct, st);
                    if (line[before] != line[k]) Add(squared, x[k], line[k], ct, st);
                }
                else if (a == 2 && b == 2)
                {
                    Add(squared, line[before], z[k], ct, st);
                    if (line[before] != line[k]) Add(squared, line[k], z[k], ct, st);
                }
                else if (a == 1 || b == 1) Add(squared, x[k], a == 1 ? line[before] : line[k], ct, st);
                else if (a == 2 || b == 2) Add(squared, a == 2 ? line[before] : line[k], z[k], ct, st);
                else Add(squared, x[k], z[k], ct, st);
            }
            float2[] result = Normalise(squared.ToArray(), squared.Count);
            return result ?? ring;
        }

        /// <summary>
        /// A counter-clockwise ring pushed outwards by distance: every edge moves along its outward normal
        /// and the corners are where the moved edges meet.
        /// </summary>
        public static float2[] Offset(float2[] ring, float distance)
        {
            int n = ring.Length;
            var result = new float2[n];
            for (int k = 0; k < n; k++)
            {
                float2 before = ring[(k + n - 1) % n], here = ring[k], after = ring[(k + 1) % n];
                float2 n1 = OutwardNormal(before, here), n2 = OutwardNormal(here, after);
                float along = 1f + math.dot(n1, n2);
                // A corner that turns back on itself has no meeting point: it moves along one normal.
                result[k] = along > 0.2f ? here + distance * (n1 + n2) / along : here + distance * n1;
            }
            return result;
        }

        static float2 OutwardNormal(float2 a, float2 b)
        {
            float2 d = math.normalizesafe(b - a);
            return new float2(d.y, -d.x);      // to the right of the direction of travel: outside a counter-clockwise ring
        }

        static void Add(List<float2> ring, double x, double z, double ct, double st)
        {
            ring.Add(new float2((float)(ct * x - st * z), (float)(st * x + ct * z)));
        }

        static void SimplifyRange(float2[] ring, int count, int first, int last, float tolerance, bool[] keep)
        {
            if (last - first < 2) return;
            float2 a = ring[first], b = ring[last % count];
            int worst = -1;
            float worstDistance = tolerance;
            for (int k = first + 1; k < last; k++)
            {
                float d = PointSegmentDistance(ring[k], a, b);
                if (d > worstDistance) { worstDistance = d; worst = k; }
            }
            if (worst < 0) return;
            keep[worst] = true;
            SimplifyRange(ring, count, first, worst, tolerance, keep);
            SimplifyRange(ring, count, worst, last, tolerance, keep);
        }

        static void DropCollinear(List<float2> ring)
        {
            bool dropped = true;
            while (dropped && ring.Count > 3)
            {
                dropped = false;
                for (int k = 0; k < ring.Count && ring.Count > 3; k++)
                {
                    float2 before = ring[(k + ring.Count - 1) % ring.Count], after = ring[(k + 1) % ring.Count];
                    if (PointSegmentDistance(ring[k], before, after) >= CollinearTolerance) continue;
                    ring.RemoveAt(k);
                    k--;
                    dropped = true;
                }
            }
        }

        internal static float PointSegmentDistance(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float lengthSq = math.lengthsq(ab);
            if (lengthSq < 1e-12f) return math.distance(p, a);
            float t = math.saturate(math.dot(p - a, ab) / lengthSq);
            return math.distance(p, a + t * ab);
        }

        static bool SegmentsTouch(float2 p, float2 q, float2 r, float2 s)
        {
            double d1 = Cross(r, s, p), d2 = Cross(r, s, q), d3 = Cross(p, q, r), d4 = Cross(p, q, s);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
            return (d1 == 0 && OnSegment(r, s, p)) || (d2 == 0 && OnSegment(r, s, q)) ||
                   (d3 == 0 && OnSegment(p, q, r)) || (d4 == 0 && OnSegment(p, q, s));
        }

        static double Cross(float2 a, float2 b, float2 c)
        {
            return ((double)b.x - a.x) * ((double)c.y - a.y) - ((double)b.y - a.y) * ((double)c.x - a.x);
        }

        static bool OnSegment(float2 a, float2 b, float2 p)
        {
            return p.x >= math.min(a.x, b.x) && p.x <= math.max(a.x, b.x) && p.y >= math.min(a.y, b.y) && p.y <= math.max(a.y, b.y);
        }
    }
}
