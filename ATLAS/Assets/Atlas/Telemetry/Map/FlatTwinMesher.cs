using System;
using Atlas.Kernel;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Telemetry
{
    /// <summary>
    /// Turns a <see cref="FlatTwin"/> into triangles for a square panel (Master Plan task P1-D07): lines become
    /// thin quads clipped to the view, dots octagons, the walker a dart. Engine-free. The mesh lies in the
    /// panel's XY plane, map +X to the right and map +Z up, in the AtlasVertex layout with the ink's colour in
    /// the vertex colour. Writing into a MeshData that has reached its working size allocates nothing.
    /// </summary>
    public static class FlatTwinMesher
    {
        /// <summary>Line width as a share of the panel's half side; the stronger inks are half as wide again.</summary>
        public const float LineWidth = 0.014f;
        /// <summary>Length of the walker's dart as a share of the panel's half side.</summary>
        public const float ArrowLength = 0.11f;
        /// <summary>No dot is drawn smaller than this share of the half side.</summary>
        public const float MinDot = 0.012f;
        /// <summary>The mesh uses 16-bit indices: entries past this many vertices are left out.</summary>
        public const int MaxVertices = 60000;

        const int DotSides = 8;

        // sRGB, R in the lowest byte, A = 255: one colour per FlatTwinInk.
        static readonly uint[] s_ink =
        {
            Rgb(230, 235, 230),     // Outline
            Rgb(110, 120, 125),     // Grid
            Rgb(70, 110, 150),      // Cell
            Rgb(255, 200, 60),      // Highlight
            Rgb(200, 120, 80),      // Object
            Rgb(80, 230, 120),      // Walker
            Rgb(60, 140, 90),       // Trail
            Rgb(190, 110, 230),     // Portal
            Rgb(60, 215, 100),      // Good
            Rgb(240, 180, 50),      // Warn
            Rgb(235, 70, 60),       // Alert
            Rgb(140, 140, 140),     // Muted
        };
        static readonly uint s_background = Rgb(14, 16, 20);
        static readonly float2[] s_dot = BuildDot();

        /// <summary>The packed vertex colour of an ink.</summary>
        public static uint Colour(FlatTwinInk ink)
        {
            int i = (int)ink;
            return s_ink[i < s_ink.Length ? i : 0];
        }

        /// <summary>
        /// Clears <paramref name="into"/> and writes the twin. The view square maps onto a square of half side
        /// <paramref name="halfSize"/> round the origin. With <paramref name="background"/> a dark square is
        /// laid under the map, for a panel that floats in front of a world.
        /// </summary>
        public static void Write(FlatTwin twin, MeshData into, float halfSize, bool background)
        {
            if (twin == null) throw new ArgumentNullException("twin");
            if (into == null) throw new ArgumentNullException("into");
            into.Clear();
            if (background) Quad(into, -halfSize, -halfSize, halfSize, -halfSize, halfSize, halfSize, -halfSize, halfSize, s_background);

            float cx = twin.CentreX, cz = twin.CentreZ, inv = 1f / twin.HalfExtent;
            bool disc = twin.ClipToDisc;
            int count = twin.Count;
            for (int i = 0; i < count && into.VertexCount + DotSides + 1 <= MaxVertices; i++)
            {
                ref readonly FlatTwinItem item = ref twin[i];
                uint colour = Colour(item.Ink);
                switch (item.Shape)
                {
                    case FlatTwinShape.Line:
                    {
                        float ax = (item.A - cx) * inv, az = (item.B - cz) * inv;
                        float bx = (item.C - cx) * inv, bz = (item.D - cz) * inv;
                        if (!(disc ? ClipDisc(ref ax, ref az, ref bx, ref bz) : ClipSquare(ref ax, ref az, ref bx, ref bz))) break;
                        float dx = bx - ax, dz = bz - az;
                        float length = (float)Math.Sqrt(dx * dx + dz * dz);
                        if (!(length > 1e-6f)) break;
                        float w = 0.5f * LineWidth * (Strong(item.Ink) ? 1.5f : 1f) / length;
                        float nx = -dz * w, nz = dx * w;
                        Quad(into, (ax - nx) * halfSize, (az - nz) * halfSize, (bx - nx) * halfSize, (bz - nz) * halfSize,
                             (bx + nx) * halfSize, (bz + nz) * halfSize, (ax + nx) * halfSize, (az + nz) * halfSize, colour);
                        break;
                    }
                    case FlatTwinShape.Bar:
                    {
                        float x0 = math.clamp((Math.Min(item.A, item.C) - cx) * inv, -1f, 1f);
                        float x1 = math.clamp((Math.Max(item.A, item.C) - cx) * inv, -1f, 1f);
                        float z0 = math.clamp((Math.Min(item.B, item.D) - cz) * inv, -1f, 1f);
                        float z1 = math.clamp((Math.Max(item.B, item.D) - cz) * inv, -1f, 1f);
                        if (!(x1 > x0) || !(z1 > z0)) break;
                        Quad(into, x0 * halfSize, z0 * halfSize, x1 * halfSize, z0 * halfSize,
                             x1 * halfSize, z1 * halfSize, x0 * halfSize, z1 * halfSize, colour);
                        break;
                    }
                    case FlatTwinShape.Dot:
                    {
                        float x = (item.A - cx) * inv, z = (item.B - cz) * inv;
                        if (!Inside(x, z, disc)) break;
                        float r = Math.Max(item.C * inv, MinDot) * halfSize;
                        int centre = Vertex(into, x * halfSize, z * halfSize, colour);
                        for (int k = 0; k < DotSides; k++)
                            Vertex(into, x * halfSize + s_dot[k].x * r, z * halfSize + s_dot[k].y * r, colour);
                        for (int k = 0; k < DotSides; k++)
                            into.AddTriangle(centre, centre + 1 + k, centre + 1 + (k + 1) % DotSides);
                        break;
                    }
                    case FlatTwinShape.Arrow:
                    {
                        float x = (item.A - cx) * inv, z = (item.B - cz) * inv;
                        // A walker outside the view is held on its edge, so the eye can still find it.
                        x = math.clamp(x, -1f, 1f);
                        z = math.clamp(z, -1f, 1f);
                        float fx = (float)Math.Sin(item.C), fz = (float)Math.Cos(item.C);
                        float rx = fz, rz = -fx;
                        float l = ArrowLength * halfSize;
                        x *= halfSize;
                        z *= halfSize;
                        int tip = Vertex(into, x + fx * 0.6f * l, z + fz * 0.6f * l, colour);
                        Vertex(into, x - fx * 0.4f * l + rx * 0.4f * l, z - fz * 0.4f * l + rz * 0.4f * l, colour);
                        Vertex(into, x - fx * 0.15f * l, z - fz * 0.15f * l, colour);
                        Vertex(into, x - fx * 0.4f * l - rx * 0.4f * l, z - fz * 0.4f * l - rz * 0.4f * l, colour);
                        into.AddTriangle(tip, tip + 1, tip + 2);
                        into.AddTriangle(tip, tip + 2, tip + 3);
                        break;
                    }
                }
            }
        }

        /// <summary>Where a map point lands on a panel of this half side.</summary>
        public static float2 ToPanel(FlatTwin twin, float x, float z, float halfSize)
        {
            float inv = halfSize / twin.HalfExtent;
            return new float2((x - twin.CentreX) * inv, (z - twin.CentreZ) * inv);
        }

        static bool Strong(FlatTwinInk ink)
        {
            return ink == FlatTwinInk.Outline || ink == FlatTwinInk.Highlight || ink == FlatTwinInk.Walker;
        }

        static bool Inside(float x, float z, bool disc)
        {
            return disc ? x * x + z * z <= 1f : Math.Abs(x) <= 1f && Math.Abs(z) <= 1f;
        }

        // Liang-Barsky against the square of half side one.
        static bool ClipSquare(ref float ax, ref float az, ref float bx, ref float bz)
        {
            float t0 = 0f, t1 = 1f, dx = bx - ax, dz = bz - az;
            if (!ClipEdge(-dx, ax + 1f, ref t0, ref t1) || !ClipEdge(dx, 1f - ax, ref t0, ref t1) ||
                !ClipEdge(-dz, az + 1f, ref t0, ref t1) || !ClipEdge(dz, 1f - az, ref t0, ref t1)) return false;
            float x0 = ax + t0 * dx, z0 = az + t0 * dz;
            bx = ax + t1 * dx;
            bz = az + t1 * dz;
            ax = x0;
            az = z0;
            return true;
        }

        static bool ClipEdge(float p, float q, ref float t0, ref float t1)
        {
            if (p == 0f) return q >= 0f;
            float r = q / p;
            if (p < 0f)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }
            return true;
        }

        // The part of a segment inside the unit circle.
        static bool ClipDisc(ref float ax, ref float az, ref float bx, ref float bz)
        {
            float dx = bx - ax, dz = bz - az;
            float a = dx * dx + dz * dz;
            if (!(a > 1e-12f)) return false;
            float b = 2f * (ax * dx + az * dz);
            float c = ax * ax + az * az - 1f;
            float discriminant = b * b - 4f * a * c;
            if (discriminant <= 0f) return false;
            float root = (float)Math.Sqrt(discriminant);
            float t0 = Math.Max(0f, (-b - root) / (2f * a)), t1 = Math.Min(1f, (-b + root) / (2f * a));
            if (!(t1 > t0)) return false;
            float x0 = ax + t0 * dx, z0 = az + t0 * dz;
            bx = ax + t1 * dx;
            bz = az + t1 * dz;
            ax = x0;
            az = z0;
            return true;
        }

        static void Quad(MeshData into, float ax, float ay, float bx, float by, float cx, float cy, float dx, float dy, uint colour)
        {
            int first = Vertex(into, ax, ay, colour);
            Vertex(into, bx, by, colour);
            Vertex(into, cx, cy, colour);
            Vertex(into, dx, dy, colour);
            into.AddTriangle(first, first + 1, first + 2);
            into.AddTriangle(first, first + 2, first + 3);
        }

        static int Vertex(MeshData into, float x, float y, uint colour)
        {
            AtlasVertex vertex;
            vertex.Position = new float3(x, y, 0f);
            vertex.Normal = new float3(0f, 0f, -1f);
            vertex.Color = colour;
            vertex.Uv0 = float2.zero;
            vertex.Uv1 = float2.zero;
            return into.AddVertex(in vertex);
        }

        static uint Rgb(uint r, uint g, uint b) { return r | (g << 8) | (b << 16) | 0xFF000000u; }

        static float2[] BuildDot()
        {
            var ring = new float2[DotSides];
            for (int k = 0; k < DotSides; k++)
            {
                double a = 2.0 * Math.PI * k / DotSides;
                ring[k] = new float2((float)Math.Cos(a), (float)Math.Sin(a));
            }
            return ring;
        }
    }
}
