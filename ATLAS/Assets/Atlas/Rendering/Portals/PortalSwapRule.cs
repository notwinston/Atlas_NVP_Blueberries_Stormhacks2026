using System;
using Unity.Mathematics;

namespace Atlas.Rendering
{
    /// <summary>
    /// When a room that is no longer shown may be switched off (Master Plan task P1-A10): only when its
    /// bounds fail the frustum test for both eyes and the head is outside the volume the room shares with
    /// the walker's room, and only after that has held for three consecutive frames. Engine-free.
    /// </summary>
    public sealed class PortalSwapRule
    {
        /// <summary>Consecutive frames the condition must hold.</summary>
        public const int HoldFrames = 3;

        int[] m_Held;

        public PortalSwapRule(int capacity = 16)
        {
            m_Held = new int[math.max(capacity, 1)];
        }

        /// <summary>Frames in a row the condition has held for a room.</summary>
        public int HeldFrames(int room)
        {
            return room >= 0 && room < m_Held.Length ? m_Held[room] : 0;
        }

        /// <summary>Forgets every room.</summary>
        public void Reset()
        {
            Array.Clear(m_Held, 0, m_Held.Length);
        }

        /// <summary>Forgets one room: it is shown again, or it has been switched off.</summary>
        public void Reset(int room)
        {
            if (room >= 0 && room < m_Held.Length) m_Held[room] = 0;
        }

        /// <summary>Makes room for this many rooms. Called when a room is added, never on the frame path.</summary>
        public void EnsureCapacity(int rooms)
        {
            if (rooms > m_Held.Length) Array.Resize(ref m_Held, math.max(rooms, m_Held.Length * 2));
        }

        /// <summary>
        /// One frame of one room that no doorway shows. True when the room may be switched off now: neither
        /// eye's frustum touches its bounds, the head is outside the overlap volume, and that has been so
        /// for three frames in a row, this one included.
        /// </summary>
        public bool Step(int room, bool leftEyeSees, bool rightEyeSees, bool headInOverlap)
        {
            if (room < 0 || room >= m_Held.Length) return false;
            if (leftEyeSees || rightEyeSees || headInOverlap)
            {
                m_Held[room] = 0;
                return false;
            }
            if (m_Held[room] < HoldFrames) m_Held[room]++;
            return m_Held[room] >= HoldFrames;
        }
    }

    /// <summary>
    /// The geometry behind the portal renderer: doorway planes, the head crossing a doorway, how much of an
    /// eye's view a doorway fills, and the frustum test of a room's bounds. Planes are (n.xyz, d) with
    /// dot(n, p) + d positive on the kept side. Engine-free; nothing here allocates.
    /// </summary>
    public static class PortalMath
    {
        /// <summary>An eye this close to a doorway's plane gets the portal box. Metres.</summary>
        public const float BoxDistance = 0.10f;
        /// <summary>How far behind a doorway the head may be and still be shown it. Metres.</summary>
        public const float BehindTolerance = 0.05f;
        /// <summary>Depth of the portal box behind the doorway. Metres. Deeper than the corner of the near plane.</summary>
        public const float BoxDepth = 0.5f;
        /// <summary>Area given to a doorway whose box is on: larger than any doorway seen from outside.</summary>
        public const float FullViewArea = 1e6f;

        /// <summary>
        /// Plane of the doorway from floor point a to floor point b, a on the left as seen by the viewer:
        /// the normal points at the viewer.
        /// </summary>
        public static float4 DoorPlane(float2 a, float2 b)
        {
            float2 along = math.normalizesafe(b - a, new float2(1f, 0f));
            var normal = new float3(along.y, 0f, -along.x);
            return new float4(normal, -(normal.x * a.x + normal.z * a.y));
        }

        /// <summary>The same plane seen from the other side.</summary>
        public static float4 Flip(float4 plane)
        {
            return -plane;
        }

        /// <summary>Signed distance of a point from a plane: positive on the side the normal points to. Metres.</summary>
        public static float Side(float4 plane, float3 point)
        {
            return math.dot(plane.xyz, point) + plane.w;
        }

        /// <summary>
        /// True when the point, dropped onto the doorway's plane, lies inside the doorway widened by margin
        /// on both sides and at the top.
        /// </summary>
        public static bool InsideDoor(float2 a, float2 b, float height, float3 point, float margin)
        {
            float2 span = b - a;
            float width = math.length(span);
            if (!(width > 1e-6f)) return false;
            float along = math.dot(new float2(point.x, point.z) - a, span / width);
            return along >= -margin && along <= width + margin && point.y >= -margin && point.y <= height + margin;
        }

        /// <summary>
        /// True when the head went through the doorway between two frames: it was in front of the plane, it
        /// is now on it or behind it, and the point where it met the plane lies inside the doorway.
        /// </summary>
        public static bool Crossed(float4 plane, float2 a, float2 b, float height, float3 previousHead, float3 head)
        {
            float before = Side(plane, previousHead);
            float after = Side(plane, head);
            if (!(before > 0f) || after > 0f) return false;
            float t = before / (before - after);
            float3 meet = math.lerp(previousHead, head, t);
            // A head is wider than a point: a shoulder's width of slack keeps a walker who brushes the jamb.
            return InsideDoor(a, b, height, meet, 0.15f);
        }

        /// <summary>
        /// True when the box touches the frustum: the test of GeometryUtility.TestPlanesAABB. planes holds
        /// six planes with their normals pointing inwards.
        /// </summary>
        public static bool BoundsInFrustum(float4[] planes, float3 min, float3 max)
        {
            for (int k = 0; k < 6; k++)
            {
                float4 plane = planes[k];
                // The corner of the box that lies furthest along the plane's normal.
                float3 far = math.select(min, max, plane.xyz >= 0f);
                if (math.dot(plane.xyz, far) + plane.w < 0f) return false;
            }
            return true;
        }

        /// <summary>True when the point lies inside both boxes.</summary>
        public static bool InOverlap(float3 point, float3 minA, float3 maxA, float3 minB, float3 maxB)
        {
            float3 min = math.max(minA, minB), max = math.min(maxA, maxB);
            return math.all(point >= min) && math.all(point <= max);
        }

        /// <summary>
        /// How much of an eye's view a doorway fills: the area of the doorway, cut by the eye's frustum, on
        /// the plane one metre in front of the eye. 0 when no part of it is in view. planes are the six
        /// frustum planes (left, right, down, up, near, far), normals inwards.
        /// </summary>
        public static float DoorViewArea(float2 a, float2 b, float height, float3 eye, float4[] planes)
        {
            Span<float3> polygon = stackalloc float3[12];
            Span<float3> scratch = stackalloc float3[12];
            polygon[0] = new float3(a.x, 0f, a.y);
            polygon[1] = new float3(b.x, 0f, b.y);
            polygon[2] = new float3(b.x, height, b.y);
            polygon[3] = new float3(a.x, height, a.y);
            int count = 4;
            for (int k = 0; k < 6 && count >= 3; k++)
            {
                count = Clip(polygon, count, planes[k], scratch);
                Span<float3> swap = polygon;
                polygon = scratch;
                scratch = swap;
            }
            if (count < 3) return 0f;

            // The near plane's normal is the view direction; any pair of axes across it measures the same area.
            float3 forward = math.normalizesafe(planes[4].xyz, new float3(0f, 0f, 1f));
            float3 right = math.normalizesafe(math.cross(new float3(0f, 1f, 0f), forward), new float3(1f, 0f, 0f));
            float3 up = math.cross(forward, right);
            float2 first = Project(polygon[0] - eye, right, up, forward);
            float2 previous = Project(polygon[1] - eye, right, up, forward);
            float twice = 0f;
            for (int k = 2; k < count; k++)
            {
                float2 current = Project(polygon[k] - eye, right, up, forward);
                float2 u = previous - first, v = current - first;
                twice += u.x * v.y - u.y * v.x;
                previous = current;
            }
            return 0.5f * math.abs(twice);
        }

        static float2 Project(float3 fromEye, float3 right, float3 up, float3 forward)
        {
            float depth = math.max(math.dot(fromEye, forward), 1e-4f);
            return new float2(math.dot(fromEye, right), math.dot(fromEye, up)) / depth;
        }

        // Sutherland-Hodgman against one plane. A convex polygon gains at most one vertex per plane.
        static int Clip(Span<float3> source, int count, float4 plane, Span<float3> target)
        {
            int written = 0;
            float3 previous = source[count - 1];
            float previousSide = Side(plane, previous);
            for (int k = 0; k < count; k++)
            {
                float3 current = source[k];
                float currentSide = Side(plane, current);
                if ((previousSide >= 0f) != (currentSide >= 0f))
                {
                    float t = previousSide / (previousSide - currentSide);
                    if (written < target.Length) target[written++] = math.lerp(previous, current, t);
                }
                if (currentSide >= 0f && written < target.Length) target[written++] = current;
                previous = current;
                previousSide = currentSide;
            }
            return written;
        }
    }
}
