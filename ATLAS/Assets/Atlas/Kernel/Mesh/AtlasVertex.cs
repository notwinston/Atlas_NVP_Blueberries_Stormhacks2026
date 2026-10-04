using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Atlas.Kernel
{
    /// <summary>
    /// The one vertex layout of the look shader (docs/contracts/lane-a-platform.md §10.2). 44 bytes, one stream.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AtlasVertex
    {
        /// <summary>Object space, metres. Offset 0.</summary>
        public float3 Position;
        /// <summary>Unit, object space. The shader does not light by it; the shade is baked from it. Offset 12.</summary>
        public float3 Normal;
        /// <summary>UNorm8 x 4 with R in the lowest byte: R shade, G line weight, B and A reserved (255). Offset 24.</summary>
        public uint Color;
        /// <summary>Palette coordinate. Offset 28.</summary>
        public float2 Uv0;
        /// <summary>Line coordinates: a line is drawn wherever either component crosses an integer. Offset 36.</summary>
        public float2 Uv1;

        /// <summary>Size of one vertex in bytes.</summary>
        public const int Stride = 44;

        /// <summary>R = shade, G = line weight, B = 255, A = 255.</summary>
        public static uint PackColor(byte shade, byte lineWeight = 255)
        {
            return shade | ((uint)lineWeight << 8) | 0xFFFF0000u;
        }
    }
}
