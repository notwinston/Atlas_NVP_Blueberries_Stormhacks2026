using System;
using System.Collections.Generic;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Hands
{
    /// <summary>
    /// The surfaces of the scanned room that a fingertip can touch (documents/ATLAS_Hands_Design.md section 6.5):
    /// the walls with their openings left out, the floor, and the furniture boxes. They are where the real things
    /// are, so a finger really stops there, and their places are known without reading the picture back.
    /// Everything is analytic: a wall is a few flat rectangles, the floor is the outline, a box is an oriented
    /// box. The room is turned into those shapes once, in <see cref="SetRoom"/>; a query allocates nothing and
    /// costs a few dozen small tests. Engine-free. Main thread only.
    ///
    /// The shapes are the ones the room is drawn with (RoomShellBuilder): a wall is cut at the edges of its
    /// openings, a door leaves only the wall above it, a window the wall below its sill and above its head.
    /// </summary>
    public sealed class RoomSurfaces : IHandsSurface
    {
        /// <summary>A box whose base is no higher than this above the floor stands on the floor: its underside is not a surface, metres.</summary>
        internal const float FloorTolerance = 0.02f;

        // The renderer's own limit: spans and heights shorter than this are not there.
        const float Tiny = 1e-4f;

        /// <summary>One flat piece of a wall: the plane it lies in, the span along the wall and the heights.</summary>
        struct WallPanel
        {
            /// <summary>The floor point at which u = 0, room frame (x, z).</summary>
            public float2 Origin;
            /// <summary>Unit, along the wall from A to B.</summary>
            public float2 Along;
            /// <summary>Unit, towards the room: the left of A to B.</summary>
            public float2 Normal;
            public float U0, U1, Y0, Y1;
        }

        /// <summary>One piece of furniture as an oriented box.</summary>
        struct BoxShape
        {
            public float3 Centre, Half;
            public quaternion ToWorld, ToLocal;
            public float Radius;
            public bool OnFloor;
        }

        float2[] _outline = Array.Empty<float2>();
        WallPanel[] _panels = Array.Empty<WallPanel>();
        BoxShape[] _boxes = Array.Empty<BoxShape>();

        /// <summary>
        /// False turns every query into "nothing there". The caller turns it off in a space that is not
        /// registered to the real room, where a wall's place on screen is not the wall's place in the room. On
        /// by default.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Takes the room's surfaces: its walls and their openings, the outline of the floor and the objects'
        /// boxes. Null clears them. The shapes are worked out here, once; call it again when the room changes.
        /// </summary>
        public void SetRoom(RoomModel room)
        {
            _outline = Array.Empty<float2>();
            _panels = Array.Empty<WallPanel>();
            _boxes = Array.Empty<BoxShape>();
            if (room == null) return;

            if (room.Outline != null && room.Outline.Length >= 3) _outline = (float2[])room.Outline.Clone();

            var panels = new List<WallPanel>();
            RoomWall[] walls = room.Walls ?? Array.Empty<RoomWall>();
            for (int w = 0; w < walls.Length; w++) AddWall(walls[w], panels);
            _panels = panels.ToArray();

            RoomObject[] objects = room.Objects ?? Array.Empty<RoomObject>();
            var boxes = new List<BoxShape>(objects.Length);
            for (int k = 0; k < objects.Length; k++)
            {
                RoomObject o = objects[k];
                if (o == null) continue;
                float3 half = 0.5f * math.max(o.Size, float3.zero);
                quaternion turn = o.Rotation;
                boxes.Add(new BoxShape
                {
                    Centre = o.Base + new float3(0f, half.y, 0f),
                    Half = half,
                    ToWorld = turn,
                    ToLocal = math.conjugate(turn),
                    Radius = math.length(half),
                    OnFloor = o.Base.y <= FloorTolerance
                });
            }
            _boxes = boxes.ToArray();
        }

        /// <summary>
        /// The surface point closest to a world point and the unit normal there, pointing to the open side: into
        /// the room from a wall (whichever side of the wall the point is on), up from the floor, out of a box.
        /// On a box's edge or corner the normal is the way from the surface to the point, which is what the
        /// distance field's slope is. A point inside a box is answered with its nearest face. False, with
        /// zeroes, when no surface lies within maxDistance metres, when the surfaces are off, or with no room.
        /// </summary>
        public bool TryClosest(float3 world, float maxDistance, out float3 point, out float3 normal)
        {
            point = float3.zero;
            normal = float3.zero;
            if (!Enabled || !(maxDistance >= 0f) || !math.all(math.isfinite(world))) return false;

            // Squared distances throughout. A candidate counts when it is within reach and nearer than the best.
            float reach2 = maxDistance * maxDistance;
            float best = float.MaxValue;
            bool found = false;
            float3 bestPoint = float3.zero, bestNormal = float3.zero;

            // The floor: the outline at height 0, so the floor ends where the walls begin.
            float height2 = world.y * world.y;
            if (_outline.Length >= 3 && height2 <= reach2)
            {
                float2 flat = world.xz;
                float2 on = flat;
                float across2 = 0f;
                if (!WalkablePolygon.RingContains(_outline, flat)) across2 = ClosestOnOutline(flat, out on);
                float d2 = across2 + height2;
                if (d2 <= reach2 && d2 < best)
                {
                    best = d2;
                    bestPoint = new float3(on.x, 0f, on.y);
                    bestNormal = new float3(0f, 1f, 0f);
                    found = true;
                }
            }

            // The walls: rectangles in their planes, so the gap of an opening is simply not among them.
            WallPanel[] panels = _panels;
            for (int i = 0; i < panels.Length; i++)
            {
                ref readonly WallPanel p = ref panels[i];
                float2 q = world.xz - p.Origin;
                float s = math.dot(q, p.Normal);
                float s2 = s * s;
                if (s2 > reach2 || s2 >= best) continue;
                float u = math.dot(q, p.Along);
                float cu = math.clamp(u, p.U0, p.U1);
                float cy = math.clamp(world.y, p.Y0, p.Y1);
                float du = u - cu, dy = world.y - cy;
                float d2 = s2 + du * du + dy * dy;
                if (d2 <= reach2 && d2 < best)
                {
                    best = d2;
                    bestPoint = new float3(p.Origin.x + p.Along.x * cu, cy, p.Origin.y + p.Along.y * cu);
                    bestNormal = new float3(p.Normal.x, 0f, p.Normal.y);
                    found = true;
                }
            }

            // The boxes.
            BoxShape[] boxes = _boxes;
            for (int i = 0; i < boxes.Length; i++)
            {
                ref readonly BoxShape b = ref boxes[i];
                float3 offset = world - b.Centre;
                float outside = math.length(offset) - b.Radius;       // a box is never nearer than its bounding sphere
                if (outside > 0f && (outside * outside > reach2 || outside * outside >= best)) continue;
                float3 local = math.mul(b.ToLocal, offset);
                // Under a box that stands on the floor is under the floor: the floor is there, at least as near.
                if (b.OnFloor && local.y < -b.Half.y) continue;

                float3 delta = local - math.clamp(local, -b.Half, b.Half);
                float d2 = math.lengthsq(delta);
                float3 onBox, outward;
                if (d2 > 0f)
                {
                    if (d2 > reach2 || d2 >= best) continue;
                    onBox = local - delta;
                    outward = delta * math.rsqrt(d2);
                }
                else
                {
                    // On the surface or inside it: the nearest face. A box on the floor has no underside.
                    float3 over = b.Half - local, under = b.Half + local;
                    float gap = over.x;
                    outward = new float3(1f, 0f, 0f);
                    if (under.x < gap) { gap = under.x; outward = new float3(-1f, 0f, 0f); }
                    if (over.y < gap) { gap = over.y; outward = new float3(0f, 1f, 0f); }
                    if (!b.OnFloor && under.y < gap) { gap = under.y; outward = new float3(0f, -1f, 0f); }
                    if (over.z < gap) { gap = over.z; outward = new float3(0f, 0f, 1f); }
                    if (under.z < gap) { gap = under.z; outward = new float3(0f, 0f, -1f); }
                    d2 = gap * gap;
                    if (d2 > reach2 || d2 >= best) continue;
                    onBox = local + outward * gap;
                }
                best = d2;
                bestPoint = b.Centre + math.mul(b.ToWorld, onBox);
                bestNormal = math.mul(b.ToWorld, outward);
                found = true;
            }

            if (!found) return false;
            point = bestPoint;
            normal = bestNormal;
            return true;
        }

        // ---- Making the shapes -----------------------------------------------------------------------------

        // The wall's rectangles: cut at 0, at the edges of every opening and at the wall's length; in an opening's
        // span only what is below its sill and above its head is left. The renderer cuts the walls the same way.
        static void AddWall(RoomWall wall, List<WallPanel> panels)
        {
            if (wall == null) return;
            float length = wall.Length;
            if (length < Tiny || wall.Height < Tiny) return;
            float2 along = (wall.B - wall.A) / length;
            float2 normal = new float2(-along.y, along.x);
            WallOpening[] openings = wall.Openings ?? Array.Empty<WallOpening>();

            float[] cuts = new float[2 + 2 * openings.Length];
            int count = 0;
            cuts[count++] = 0f;
            cuts[count++] = length;
            for (int k = 0; k < openings.Length; k++)
            {
                cuts[count++] = math.clamp(openings[k].Offset, 0f, length);
                cuts[count++] = math.clamp(openings[k].Offset + openings[k].Width, 0f, length);
            }
            Array.Sort(cuts, 0, count);

            for (int c = 0; c + 1 < count; c++)
            {
                float u0 = cuts[c], u1 = cuts[c + 1];
                if (u1 - u0 < Tiny) continue;
                int opening = OpeningAt(openings, 0.5f * (u0 + u1));
                if (opening < 0)
                {
                    AddPanel(panels, wall, along, normal, u0, u1, 0f, wall.Height);
                    continue;
                }
                WallOpening o = openings[opening];
                if (o.Bottom > Tiny) AddPanel(panels, wall, along, normal, u0, u1, 0f, math.min(o.Bottom, wall.Height));
                if (o.Top < wall.Height - Tiny) AddPanel(panels, wall, along, normal, u0, u1, math.max(o.Top, 0f), wall.Height);
            }
        }

        static void AddPanel(List<WallPanel> panels, RoomWall wall, float2 along, float2 normal, float u0, float u1, float y0, float y1)
        {
            if (y1 - y0 < Tiny) return;
            panels.Add(new WallPanel { Origin = wall.A, Along = along, Normal = normal, U0 = u0, U1 = u1, Y0 = y0, Y1 = y1 });
        }

        static int OpeningAt(WallOpening[] openings, float u)
        {
            for (int k = 0; k < openings.Length; k++)
                if (u >= openings[k].Offset && u <= openings[k].Offset + openings[k].Width) return k;
            return -1;
        }

        // ---- The floor's rim ---------------------------------------------------------------------------------

        // The point of the outline nearest to a point outside it, and the squared distance to it.
        float ClosestOnOutline(float2 p, out float2 on)
        {
            float2[] ring = _outline;
            float best = float.MaxValue;
            on = p;
            for (int k = 0, prev = ring.Length - 1; k < ring.Length; prev = k++)
            {
                float2 a = ring[prev];
                float2 edge = ring[k] - a;
                float length2 = math.lengthsq(edge);
                float t = length2 > 1e-12f ? math.saturate(math.dot(p - a, edge) / length2) : 0f;
                float2 c = a + edge * t;
                float d2 = math.lengthsq(p - c);
                if (d2 < best)
                {
                    best = d2;
                    on = c;
                }
            }
            return best;
        }
    }
}
