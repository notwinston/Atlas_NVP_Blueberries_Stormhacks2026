using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Atlas.Rendering
{
    /// <summary>
    /// The one vertex layout of the look shader (lane A contract, section 10.2). 44 bytes, one stream:
    /// position, normal, colour (UNorm8 x 4), palette coordinate, line coordinates.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AtlasVertex
    {
        /// <summary>Object space, metres.</summary>
        public float3 Position;
        /// <summary>Unit, object space. The shader does not light by it; the shade is baked from it.</summary>
        public float3 Normal;
        /// <summary>R = shade, G = line weight, B and A reserved (255). R is the lowest byte.</summary>
        public uint Color;
        /// <summary>Palette coordinate.</summary>
        public float2 Uv0;
        /// <summary>Line coordinates: a line is drawn wherever a component crosses an integer.</summary>
        public float2 Uv1;

        /// <summary>Size of one vertex in bytes.</summary>
        public const int Stride = 44;

        /// <summary>R = shade, G = line weight, B = 255, A = 255.</summary>
        public static uint PackColor(byte shade, byte lineWeight = 255)
        {
            return shade | ((uint)lineWeight << 8) | 0xFFFF0000u;
        }

        /// <summary>The baked shade, 0..255.</summary>
        public byte Shade => (byte)(Color & 255u);
        /// <summary>The line weight, 0..255.</summary>
        public byte LineWeight => (byte)((Color >> 8) & 255u);
    }
}
