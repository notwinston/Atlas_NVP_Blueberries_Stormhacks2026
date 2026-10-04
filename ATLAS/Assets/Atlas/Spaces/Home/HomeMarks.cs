using System;
using Atlas.Rendering;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.Home
{
    /// <summary>
    /// The floor marks Home owns (Master Plan sections 4.4 and 4.5): the centre mark the walker stands on, at
    /// the room origin, and the outline of the lap square. Engine-free: it fills an AtlasMeshBuilder in the
    /// room frame, and a headless test counts what it built.
    /// </summary>
    /// <remarks>
    /// Both marks go into one mesh, so they cost one draw call per eye. In standing mode the lap outline is
    /// left out: the walker is never invited to walk (Master Plan section 4.4, small rooms).
    /// </remarks>
    public static class HomeMarks
    {
        /// <summary>Metres above the floor: enough to stay clear of the floor in the depth buffer at 5 m.</summary>
        public const float Height = 0.004f;

        /// <summary>The centre mark: a ring one stands in and a dot at the origin. Metres.</summary>
        public const float RingOuterRadius = 0.15f, RingInnerRadius = 0.11f, DotRadius = 0.03f;
        /// <summary>Segments around the ring; the dot uses half as many.</summary>
        public const int RingSegments = 24;

        /// <summary>Width of the lap outline, metres. The line is centred on the lap square's edge.</summary>
        public const float LapLineWidth = 0.03f;

        /// <summary>Triangles of the centre mark: a constant.</summary>
        public const int CentreTriangles = 2 * RingSegments + RingSegments / 2;

        /// <summary>True when the lap outline is part of the marks: there is a lap square and the room is not in standing mode.</summary>
        public static bool HasLapOutline(RoomModel room)
        {
            return room != null && !room.Standing && room.LapSide > LapLineWidth;
        }

        /// <summary>Adds the centre mark and, unless the room is in standing mode, the lap outline.</summary>
        public static void Build(RoomModel room, AtlasMeshBuilder into)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            if (into == null) throw new ArgumentNullException(nameof(into));
            AddCentre(into);
            if (HasLapOutline(room)) AddLapOutline(room.LapSide, into);
        }

        /// <summary>Triangles Build adds for this room.</summary>
        public static int Triangles(RoomModel room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            return CentreTriangles + (HasLapOutline(room) ? LapTriangles(room.LapSide) : 0);
        }

        /// <summary>Triangles of the outline of a lap square of the given side.</summary>
        public static int LapTriangles(float side)
        {
            int along = AtlasMeshBudget.Segments(side + LapLineWidth, AtlasMeshBuilder.MaxEdge);
            int between = AtlasMeshBudget.Segments(side - LapLineWidth, AtlasMeshBuilder.MaxEdge);
            return 4 * (along + between);
        }

        /// <summary>The ring and the dot at the room origin.</summary>
        public static void AddCentre(AtlasMeshBuilder into)
        {
            float2 uv = AtlasPalette.Uv(AtlasPalette.HomeBeacon);
            float3 up = new float3(0f, 1f, 0f);
            into.Reserve(2 * RingSegments + RingSegments / 2 + 1, 3 * CentreTriangles);

            // The angle runs from +X towards +Z, so a face seen from above lists its corners against it.
            int first = into.VertexCount;
            for (int k = 0; k < RingSegments; k++)
            {
                float angle = 2f * math.PI * k / RingSegments;
                float3 direction = new float3(math.cos(angle), 0f, math.sin(angle));
                float3 lift = new float3(0f, Height, 0f);
                into.AddVertex(direction * RingInnerRadius + lift, up, uv, float2.zero, 1f, 0);
                into.AddVertex(direction * RingOuterRadius + lift, up, uv, float2.zero, 1f, 0);
            }
            for (int k = 0; k < RingSegments; k++)
            {
                int next = (k + 1) % RingSegments;
                int inner = first + 2 * k, outer = inner + 1;
                int innerNext = first + 2 * next, outerNext = innerNext + 1;
                into.AddTriangle(inner, outerNext, outer);
                into.AddTriangle(inner, innerNext, outerNext);
            }

            int dotSegments = RingSegments / 2;
            int centre = into.AddVertex(new float3(0f, Height, 0f), up, uv, float2.zero, 1f, 0);
            int rim = into.VertexCount;
            for (int k = 0; k < dotSegments; k++)
            {
                float angle = 2f * math.PI * k / dotSegments;
                into.AddVertex(new float3(DotRadius * math.cos(angle), Height, DotRadius * math.sin(angle)),
                               up, uv, float2.zero, 1f, 0);
            }
            for (int k = 0; k < dotSegments; k++) into.AddTriangle(centre, rim + (k + 1) % dotSegments, rim + k);
        }

        /// <summary>Four strips along the edges of the lap square, side s, centred on the origin.</summary>
        public static void AddLapOutline(float side, AtlasMeshBuilder into)
        {
            float2 uv = AtlasPalette.Uv(AtlasPalette.LapMarker);
            float half = 0.5f * side, w = 0.5f * LapLineWidth;
            // The two strips along X carry the corners; the two along Z fit between them, so nothing overlaps.
            Strip(into, uv, new float2(-half - w, -half - w), new float2(half + w, -half + w));
            Strip(into, uv, new float2(-half - w, half - w), new float2(half + w, half + w));
            Strip(into, uv, new float2(-half - w, -half + w), new float2(-half + w, half - w));
            Strip(into, uv, new float2(half - w, -half + w), new float2(half + w, half - w));
        }

        static void Strip(AtlasMeshBuilder into, float2 uv, float2 min, float2 max)
        {
            into.AddFloorGrid(min, max, Height, uv, AtlasMeshBuilder.GridMetres, 1f, 0, AtlasMeshBuilder.MaxEdge);
        }
    }
}
