using Unity.Mathematics;

namespace Atlas.Safety
{
    /// <summary>A flat indicator shape: points in metres on the plane 1 m ahead (x right, y up), and triangles.</summary>
    public sealed class CueShape
    {
        public readonly float2[] Points;
        public readonly int[] Triangles;
        public int PointCount { get; private set; }
        public int IndexCount { get; private set; }

        public CueShape(int pointCapacity, int indexCapacity)
        {
            Points = new float2[pointCapacity];
            Triangles = new int[indexCapacity];
        }

        public int AddPoint(float2 p)
        {
            Points[PointCount] = p;
            return PointCount++;
        }

        /// <summary>Adds a triangle wound clockwise for a viewer who sees x to the right and y up: Unity's front face.</summary>
        public void AddTriangle(int a, int b, int c)
        {
            float2 ab = Points[b] - Points[a];
            float2 ac = Points[c] - Points[a];
            if (ab.x * ac.y - ab.y * ac.x > 0f)
            {
                int swap = b;
                b = c;
                c = swap;
            }
            Triangles[IndexCount++] = a;
            Triangles[IndexCount++] = b;
            Triangles[IndexCount++] = c;
        }

        public void AddQuad(float2 a, float2 b, float2 c, float2 d)
        {
            int i = AddPoint(a);
            AddPoint(b);
            AddPoint(c);
            AddPoint(d);
            AddTriangle(i, i + 1, i + 2);
            AddTriangle(i, i + 2, i + 3);
        }

        /// <summary>A straight stroke from p to q with square caps that reach halfWidth past both ends.</summary>
        public void AddStroke(float2 p, float2 q, float halfWidth)
        {
            float2 along = math.normalizesafe(q - p, new float2(0f, 1f)) * halfWidth;
            float2 across = new float2(-along.y, along.x);
            AddQuad(p - along - across, p - along + across, q + along + across, q + along - across);
        }

        /// <summary>
        /// A ring segment between two radii from angle a0 to a1, radians, measured clockwise from straight up.
        /// </summary>
        public void AddArc(float inner, float outer, float a0, float a1, int segments)
        {
            int first = PointCount;
            for (int k = 0; k <= segments; k++)
            {
                float a = math.lerp(a0, a1, k / (float)segments);
                math.sincos(a, out float s, out float c);
                AddPoint(new float2(s * inner, c * inner));
                AddPoint(new float2(s * outer, c * outer));
            }
            for (int k = 0; k < segments; k++)
            {
                int i = first + k * 2;
                AddTriangle(i, i + 1, i + 3);
                AddTriangle(i, i + 3, i + 2);
            }
        }
    }

    /// <summary>
    /// The three indicators of the warning cue as flat shapes (Master Plan section 4.10, outputs table). Built
    /// once at start-up, in code (contracts F3). Sizes are metres on the plane 1 m ahead of the head, so 0.1 m is
    /// about 6 degrees of view.
    /// </summary>
    public static class BumpCueShapes
    {
        /// <summary>A chevron that points up (+y): two strokes that meet at the tip. 0.14 m wide.</summary>
        public static CueShape Chevron()
        {
            var shape = new CueShape(8, 12);
            float2 tip = new float2(0f, 0.030f);
            shape.AddStroke(new float2(-0.060f, -0.030f), tip, 0.011f);
            shape.AddStroke(new float2(0.060f, -0.030f), tip, 0.011f);
            return shape;
        }

        /// <summary>"Stand still": a full ring with two upright bars, the pause sign. 0.17 m across.</summary>
        public static CueShape StandStill()
        {
            const int segments = 32;
            var shape = new CueShape((segments + 1) * 2 + 8, segments * 6 + 12);
            shape.AddArc(0.070f, 0.085f, 0f, 2f * math.PI, segments);
            shape.AddStroke(new float2(-0.022f, -0.026f), new float2(-0.022f, 0.026f), 0.010f);
            shape.AddStroke(new float2(0.022f, -0.026f), new float2(0.022f, 0.026f), 0.010f);
            return shape;
        }

        /// <summary>"Rescan": a ring open at the top that ends in an arrowhead, the reload sign. 0.12 m across.</summary>
        public static CueShape Rescan()
        {
            const int segments = 24;
            const float inner = 0.040f, outer = 0.054f;
            float start = math.radians(50f), end = math.radians(330f);
            var shape = new CueShape((segments + 1) * 2 + 3, segments * 6 + 3);
            shape.AddArc(inner, outer, start, end, segments);

            // The arrowhead continues the ring clockwise: its base straddles the ring's end, its tip lies ahead.
            math.sincos(end, out float s, out float c);
            float2 radial = new float2(s, c);
            float2 tangent = new float2(c, -s);
            float mid = 0.5f * (inner + outer);
            int a = shape.AddPoint(radial * (inner - 0.014f));
            int b = shape.AddPoint(radial * (outer + 0.014f));
            int tip = shape.AddPoint(radial * mid + tangent * 0.038f);
            shape.AddTriangle(a, b, tip);
            return shape;
        }
    }

    /// <summary>
    /// The two sounds of the warning cue as samples (Master Plan section 4.10: "a soft tick", "a clear tone").
    /// Generated once at start-up; the Unity side wraps them in AudioClips.
    /// </summary>
    public static class BumpCueTones
    {
        public const int SampleRate = 48000;
        public const float TickHz = 1800f;
        public const float TickSeconds = 0.010f;
        public const float ToneHz = 660f;
        public const float ToneSeconds = 0.080f;

        /// <summary>The WARN tick: a 1,800 Hz burst of 10 ms that dies away, at a soft level.</summary>
        public static float[] Tick()
        {
            int n = (int)(SampleRate * TickSeconds);
            var samples = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float decay = math.exp(-5f * i / n) * (1f - i / (float)n);
                samples[i] = 0.5f * decay * math.sin(2f * math.PI * TickHz * t);
            }
            return samples;
        }

        /// <summary>The STRONG tone: 660 Hz for 80 ms with 5 ms ramps, so six a second stay apart and do not click.</summary>
        public static float[] Tone()
        {
            int n = (int)(SampleRate * ToneSeconds);
            int ramp = (int)(SampleRate * 0.005f);
            var samples = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = math.min(1f, math.min(i, n - 1 - i) / (float)ramp);
                samples[i] = 0.9f * envelope * math.sin(2f * math.PI * ToneHz * t);
            }
            return samples;
        }
    }
}
