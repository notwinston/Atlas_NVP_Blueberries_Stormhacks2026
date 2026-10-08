using System;
using Atlas.Kernel;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Objects
{
    /// <summary>
    /// Fits a chosen prop to an object's box (standalone-phone plan D6 and section 6.4): turns it by the match's
    /// quarter turns, scales it with the Forge's formula (cleanup._candidate: as uniform as possible, each axis
    /// stretched by at most <see cref="MaxAnisotropy"/> beyond the uniform scale), puts the origin at the centre of
    /// the base, then splits every edge longer than <see cref="PropMaxEdge"/> and bakes the shade. Engine-free and
    /// deterministic; allocation-free once the target mesh and the thread's scratch buffers have grown.
    /// </summary>
    public static class PropFitter
    {
        public const float MaxAnisotropy = 0.15f, PropMaxEdge = 0.25f;

        // The Forge's floor under an extent before its logarithm is taken.
        const double MinExtent = 1e-9;

        /// <summary>
        /// Replaces the contents of <paramref name="into"/> with <paramref name="prop"/>'s mesh turned by
        /// match.QuarterTurns (each turn carries (x, y, z) to (-z, y, x) in Unity's frame), scaled to
        /// <paramref name="size"/> (w, h, d) by the Forge formula, base-centred, split so that no edge is longer than
        /// <see cref="PropMaxEdge"/>, with the shade baked into COLOR.r for a yaw of 0 and COLOR.b = 255.
        /// The shade depends on the object's yaw in the room, which this call does not know: a caller that places
        /// the mesh at another yaw calls <see cref="MeshOps.BakeShade"/>(into, yaw) afterwards, which rewrites
        /// COLOR.r only. False, with <paramref name="into"/> empty, when the prop has no usable mesh, the size is
        /// not positive, or the result has more than <paramref name="maxTriangles"/> triangles.
        /// </summary>
        public static bool Build(PropEntry prop, in PropMatch match, float3 size, int maxTriangles, MeshData into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            if (prop == null || prop.Mesh == null) return false;
            MeshData source = prop.Mesh;
            if (source.VertexCount == 0 || source.IndexCount < 3) return false;
            if (!(size.x > 0f) || !(size.y > 0f) || !(size.z > 0f)) return false;
            if (float.IsInfinity(size.x) || float.IsInfinity(size.y) || float.IsInfinity(size.z)) return false;
            if (source.IndexCount / 3 > maxTriangles) return false;
            int turns = ((match.QuarterTurns % 4) + 4) % 4;

            // The turned mesh's extent (all vertices, as the Forge measures it).
            double3 lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            for (int v = 0; v < source.VertexCount; v++)
            {
                double3 p = Turn(source.Vertices[v].Position, turns);
                lo = math.min(lo, p);
                hi = math.max(hi, p);
            }
            double3 extent = math.max(hi - lo, new double3(MinExtent));

            // lambda_k = ln(b_k / e_k), u = exp(mean lambda), s_k = u * clip(exp(lambda_k) / u, 1 - a, 1 + a).
            double3 lambda = new double3(Math.Log(size.x / extent.x), Math.Log(size.y / extent.y), Math.Log(size.z / extent.z));
            double uniform = Math.Exp((lambda.x + lambda.y + lambda.z) / 3.0);
            double3 scale = uniform * math.clamp(math.exp(lambda) / uniform, 1.0 - MaxAnisotropy, 1.0 + MaxAnisotropy);
            if (!math.all(math.isfinite(scale)) || !math.all(scale > 0.0)) return false;
            // The base centre of the scaled mesh: a positive scale keeps the minimum and the maximum where they were.
            double3 low = lo * scale, high = hi * scale;
            var origin = new double3((low.x + high.x) * 0.5, low.y, (low.z + high.z) * 0.5);
            float3 inverse = (float3)(1.0 / scale);

            into.Reserve(source.VertexCount, source.IndexCount);
            AtlasVertex vertex;
            for (int v = 0; v < source.VertexCount; v++)
            {
                vertex = source.Vertices[v];
                vertex.Position = (float3)(Turn(vertex.Position, turns) * scale - origin);
                // A normal of a scaled surface is the old one divided by the scale, made unit again.
                float3 normal = (float3)Turn(vertex.Normal, turns);
                vertex.Normal = math.normalizesafe(normal * inverse, normal);
                into.AddVertex(in vertex);
            }
            int count = source.VertexCount;
            for (int k = 0; k + 2 < source.IndexCount; k += 3)
            {
                int a = source.Indices[k], b = source.Indices[k + 1], c = source.Indices[k + 2];
                if ((uint)a >= (uint)count || (uint)b >= (uint)count || (uint)c >= (uint)count)
                {
                    into.Clear();
                    return false;
                }
                into.AddTriangle(a, b, c);
            }

            if (!MeshOps.SplitLongEdges(into, PropMaxEdge, maxTriangles) || into.TriangleCount > maxTriangles)
            {
                into.Clear();
                return false;
            }
            MeshOps.BakeShade(into, 0f);
            return true;
        }

        // k quarter turns of (x, y, z) -> (-z, y, x), in double so the scale is the Forge's to rounding.
        static double3 Turn(float3 p, int k)
        {
            switch (k)
            {
                case 1: return new double3(-(double)p.z, p.y, p.x);
                case 2: return new double3(-(double)p.x, p.y, -(double)p.z);
                case 3: return new double3(p.z, p.y, -(double)p.x);
                default: return new double3(p.x, p.y, p.z);
            }
        }
    }
}
