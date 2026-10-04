using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Rendering
{
    // Master Plan §3.4, §8.4: the value is the grid count n.
    public enum TileLod { Near = 16, Mid = 8, Far = 4 }

    public readonly struct TileMeshSpec
    {
        public readonly int Kappa;     // -1 hyperbolic {4,q}, +1 spherical {4,3}, 0 flat cell
        public readonly int Q;
        public readonly double Scale;  // L, R or cell side g, metres
        public readonly TileLod Lod;
        public readonly int KerbMask;  // bit e = kerb on edge e
        public readonly int PostMask;  // bit c = wedge at corner c (between edges c and c+1)

        TileMeshSpec(int kappa, int q, double scale, TileLod lod, int kerbMask, int postMask)
        {
            if (scale < 0.4) throw new ArgumentOutOfRangeException(nameof(scale), "Scale must be at least 0.4 m");
            if (kerbMask < 0 || kerbMask > 15) throw new ArgumentOutOfRangeException(nameof(kerbMask));
            if (postMask < 0 || postMask > 15) throw new ArgumentOutOfRangeException(nameof(postMask));
            Kappa = kappa; Q = q; Scale = scale; Lod = lod; KerbMask = kerbMask; PostMask = postMask;
        }

        public int GridCount => Kappa != 0 ? (int)Lod : 4;
        public int BoundarySegments => Kappa != 0 ? TileChart.BoundarySegments : 4;

        public static TileMeshSpec Hyperbolic(int q, double L, TileLod lod, int kerbMask, int postMask = 0xF)
        {
            if (q != 5 && q != 6) throw new ArgumentOutOfRangeException(nameof(q));
            return new TileMeshSpec(-1, q, L, lod, kerbMask, postMask);
        }

        public static TileMeshSpec Spherical(double R, TileLod lod, int kerbMask, int postMask = 0xF)
            => new TileMeshSpec(1, 3, R, lod, kerbMask, postMask);

        public static TileMeshSpec FlatCell(double cellSide, int kerbMask, int postMask = 0xF)
            => new TileMeshSpec(0, 4, cellSide, TileLod.Near, kerbMask, postMask);
    }

    public struct TilePalette
    {
        public float2 Floor, Kerb, Post;

        // Texel centres of 16-px blocks in the 128^2 palette of Master Plan §8.1; provisional until P1-A06.
        public static readonly TilePalette Provisional = new TilePalette
        {
            Floor = new float2(0.0625f, 0.0625f),
            Kerb = new float2(0.1875f, 0.0625f),
            Post = new float2(0.3125f, 0.0625f),
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TileVertex
    {
        public float3 Position;  // xz = chart position in metres, y = height
        public Color32 Color;    // rgb = AO, a = 255
        public float2 Uv;        // palette UV

        public static readonly VertexAttributeDescriptor[] Descriptors =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
        };
    }

    public struct TileMeshLayout
    {
        public RangeInt FloorVertices, FloorIndices;
        public RangeInt Kerb0V, Kerb1V, Kerb2V, Kerb3V, Kerb0I, Kerb1I, Kerb2I, Kerb3I;
        public RangeInt Post0V, Post1V, Post2V, Post3V, Post0I, Post1I, Post2I, Post3I;
        public int VertexCount, IndexCount;
        public int TriangleCount => IndexCount / 3;

        public RangeInt KerbVertices(int edge) => edge switch { 0 => Kerb0V, 1 => Kerb1V, 2 => Kerb2V, _ => Kerb3V };
        public RangeInt KerbIndices(int edge) => edge switch { 0 => Kerb0I, 1 => Kerb1I, 2 => Kerb2I, _ => Kerb3I };
        public RangeInt PostVertices(int corner) => corner switch { 0 => Post0V, 1 => Post1V, 2 => Post2V, _ => Post3V };
        public RangeInt PostIndices(int corner) => corner switch { 0 => Post0I, 1 => Post1I, 2 => Post2I, _ => Post3I };

        internal void SetKerb(int e, RangeInt v, RangeInt i)
        {
            switch (e) { case 0: Kerb0V = v; Kerb0I = i; break; case 1: Kerb1V = v; Kerb1I = i; break; case 2: Kerb2V = v; Kerb2I = i; break; default: Kerb3V = v; Kerb3I = i; break; }
        }

        internal void SetPost(int c, RangeInt v, RangeInt i)
        {
            switch (c) { case 0: Post0V = v; Post0I = i; break; case 1: Post1V = v; Post1I = i; break; case 2: Post2V = v; Post2I = i; break; default: Post3V = v; Post3I = i; break; }
        }
    }
}
