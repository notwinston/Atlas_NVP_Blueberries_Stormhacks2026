using System;
using Atlas.Rendering;
using Unity.Mathematics;

namespace Atlas.Blades.Playground.Shell
{
    /// <summary>One floor arrow: where it lies and which way it points, room frame.</summary>
    public struct ArrowPlacement
    {
        /// <summary>Metres, floor point (x, z).</summary>
        public float2 Position;
        /// <summary>Unit vector (x, z): the way the arrow points.</summary>
        public float2 Direction;
    }

    /// <summary>
    /// The floor arrows of the guided walk (Master Plan section 10.1.2, beat 6: "beacons lead the loop"): a few
    /// chevrons on the floor between the walker and the next beacon. They lie in the room frame at the
    /// walker's floor point and are never warped (section 10.1.4: UI is anchored to the floor, never warped), so
    /// in curved space they show the bearing at the walker, not the curved path. Engine-free.
    /// </summary>
    public static class FloorArrows
    {
        /// <summary>The most chevrons shown at once.</summary>
        public const int MaxCount = 4;
        /// <summary>Metres from the head to the first chevron (derived: beyond the feet, inside the view).</summary>
        public const float FirstOffset = 0.8f;
        /// <summary>Metres between chevrons (derived).</summary>
        public const float Spacing = 0.5f;
        /// <summary>Metres: chevrons stop this short of the beacon, the reach radius of the loop walk.</summary>
        public const float StopShort = 0.3f;
        /// <summary>Metres above the floor.</summary>
        public const float Height = 0.006f;
        /// <summary>Metres: the chevron's tip lies this far ahead of its centre, and its base this far behind.</summary>
        public const float Tip = 0.12f, Back = 0.08f, HalfWidth = 0.09f;
        /// <summary>Triangles of one chevron.</summary>
        public const int TrianglesEach = 1;

        /// <summary>
        /// Places up to MaxCount chevrons from the head towards the next beacon. heading is the head's direction
        /// on the floor (x, z; need not be unit); bearingRad is clockwise from it seen from above (as
        /// <see cref="IWalkPort.NextBeaconBearingRad"/>); distanceM is the way to the beacon. Returns how many
        /// were written to into; 0 for a NaN bearing or distance, a zero heading, or a beacon within reach.
        /// </summary>
        public static int Layout(float2 headFloor, float2 heading, double bearingRad, double distanceM, ArrowPlacement[] into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            if (double.IsNaN(bearingRad) || double.IsNaN(distanceM) || double.IsInfinity(distanceM)) return 0;
            float length = math.length(heading);
            if (!(length > 1e-6f)) return 0;

            float2 h = heading / length;
            float s = (float)math.sin(bearingRad), c = (float)math.cos(bearingRad);
            // Clockwise as seen from above with +X to the right and +Z up the page.
            float2 direction = new float2(h.x * c + h.y * s, -h.x * s + h.y * c);

            int count = 0;
            float reach = (float)distanceM - StopShort;
            for (float d = FirstOffset; d <= reach && count < MaxCount && count < into.Length; d += Spacing)
            {
                into[count].Position = headFloor + direction * d;
                into[count].Direction = direction;
                count++;
            }
            return count;
        }

        /// <summary>Adds one flat chevron triangle per placement.</summary>
        public static void Build(ArrowPlacement[] placements, int count, AtlasMeshBuilder into)
        {
            if (placements == null) throw new ArgumentNullException(nameof(placements));
            if (into == null) throw new ArgumentNullException(nameof(into));
            float2 uv = AtlasPalette.Uv(AtlasPalette.Beacon);
            float3 up = new float3(0f, 1f, 0f);
            into.Reserve(3 * count, 3 * TrianglesEach * count);
            for (int k = 0; k < count; k++)
            {
                float2 d = placements[k].Direction;
                float2 side = new float2(d.y, -d.x);   // to the walker's right
                float2 p = placements[k].Position;
                float2 tip = p + d * Tip;
                float2 left = p - d * Back - side * HalfWidth;
                float2 right = p - d * Back + side * HalfWidth;
                int a = into.AddVertex(new float3(tip.x, Height, tip.y), up, uv, float2.zero, 1f, 0);
                int b = into.AddVertex(new float3(right.x, Height, right.y), up, uv, float2.zero, 1f, 0);
                int e = into.AddVertex(new float3(left.x, Height, left.y), up, uv, float2.zero, 1f, 0);
                // Seen from above (+X right, +Z up): tip, right, left runs clockwise, which faces up in Unity.
                into.AddTriangle(a, b, e);
            }
        }
    }
}
