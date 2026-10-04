// ATLAS case contract (docs/case_contract.md, section 4): tube4d.bin reader and writer.
// Plain C#, System only, no Unity references.
using System;

namespace Atlas.Cases
{
    /// <summary>
    /// A tetrahedral mesh in (x, y, z, tau). Little-endian file:
    /// header 24 bytes: "ATL4", version u16, nVerts u32, nTets u32, layers u16, M f32, Lambda f32;
    /// then P float32 nVerts x 4, T int32 nTets x 4, slab u8 x nTets. No padding anywhere.
    /// </summary>
    public sealed class Tube4D
    {
        public const int HeaderSize = 24;
        public const ushort CurrentVersion = 1;
        public const int MaxLayers = 256;

        public ushort Version = CurrentVersion;
        /// <summary>Number of phase layers (10). The tube has Layers + 1 vertex rings.</summary>
        public ushort Layers;
        /// <summary>Display magnification stored in the header (15).</summary>
        public float M;
        /// <summary>Lane length of one breath in metres (1.5).</summary>
        public float Lambda;
        /// <summary>4 floats per vertex: x, y, z, tau in metres (display frame).</summary>
        public float[] P;
        /// <summary>4 vertex indices per tetrahedron.</summary>
        public int[] T;
        /// <summary>Per tetrahedron: the phase layer k of its prism, 0 &lt;= k &lt; Layers.</summary>
        public byte[] Slab;

        public int VertexCount { get { return P.Length / 4; } }
        public int TetCount { get { return T.Length / 4; } }

        public void Validate()
        {
            if (Version != CurrentVersion) throw new ContractException("tube4d: unsupported version " + Version);
            if (P == null || T == null || Slab == null) throw new ContractException("tube4d: missing array");
            if (P.Length % 4 != 0) throw new ContractException("tube4d: P must be n x 4");
            if (T.Length % 4 != 0) throw new ContractException("tube4d: T must be m x 4");
            if (Slab.Length != TetCount) throw new ContractException("tube4d: slab must have one entry per tet");
            if (Layers < 1 || Layers > MaxLayers) throw new ContractException("tube4d: layers must be 1..256");
            if (!IsFinite(M) || !IsFinite(Lambda) || M <= 0f || Lambda <= 0f)
                throw new ContractException("tube4d: M and Lambda must be finite and positive");
            for (int i = 0; i < P.Length; i++)
                if (!IsFinite(P[i])) throw new ContractException("tube4d: P holds NaN or infinity");
            int n = VertexCount;
            for (int i = 0; i < T.Length; i++)
                if (T[i] < 0 || T[i] >= n) throw new ContractException("tube4d: tet index out of range");
            for (int i = 0; i < Slab.Length; i++)
                if (Slab[i] >= Layers) throw new ContractException("tube4d: slab id >= layers");
        }

        static bool IsFinite(float f) { return !float.IsNaN(f) && !float.IsInfinity(f); }

        public static Tube4D Read(byte[] data)
        {
            if (data.Length < HeaderSize) throw new ContractException("tube4d: file shorter than the 24-byte header");
            if (data[0] != (byte)'A' || data[1] != (byte)'T' || data[2] != (byte)'L' || data[3] != (byte)'4')
                throw new ContractException("tube4d: bad magic");
            var t = new Tube4D();
            t.Version = (ushort)(data[4] | (data[5] << 8));
            if (t.Version != CurrentVersion) throw new ContractException("tube4d: unsupported version " + t.Version);
            uint n = ReadU32(data, 6);
            uint m = ReadU32(data, 10);
            t.Layers = (ushort)(data[14] | (data[15] << 8));
            t.M = ReadF32(data, 16);
            t.Lambda = ReadF32(data, 20);
            long expect = HeaderSize + 16L * n + 17L * m;
            if (data.Length != expect)
                throw new ContractException("tube4d: file is " + data.Length + " bytes, header implies " + expect);
            int o = HeaderSize;
            t.P = new float[4 * (int)n];
            for (int i = 0; i < t.P.Length; i++, o += 4) t.P[i] = ReadF32(data, o);
            t.T = new int[4 * (int)m];
            for (int i = 0; i < t.T.Length; i++, o += 4) t.T[i] = (int)ReadU32(data, o);
            t.Slab = new byte[(int)m];
            Buffer.BlockCopy(data, o, t.Slab, 0, (int)m);
            t.Validate();
            return t;
        }

        public byte[] Write()
        {
            Validate();
            var data = new byte[HeaderSize + 4 * P.Length + 4 * T.Length + Slab.Length];
            data[0] = (byte)'A'; data[1] = (byte)'T'; data[2] = (byte)'L'; data[3] = (byte)'4';
            data[4] = (byte)(Version & 0xFF); data[5] = (byte)(Version >> 8);
            WriteU32(data, 6, (uint)VertexCount);
            WriteU32(data, 10, (uint)TetCount);
            data[14] = (byte)(Layers & 0xFF); data[15] = (byte)(Layers >> 8);
            WriteF32(data, 16, M);
            WriteF32(data, 20, Lambda);
            int o = HeaderSize;
            for (int i = 0; i < P.Length; i++, o += 4) WriteF32(data, o, P[i]);
            for (int i = 0; i < T.Length; i++, o += 4) WriteU32(data, o, (uint)T[i]);
            Buffer.BlockCopy(Slab, 0, data, o, Slab.Length);
            return data;
        }

        // Explicit little-endian, independent of the host byte order.
        static uint ReadU32(byte[] d, int o)
        {
            return (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));
        }

        static void WriteU32(byte[] d, int o, uint v)
        {
            d[o] = (byte)v; d[o + 1] = (byte)(v >> 8); d[o + 2] = (byte)(v >> 16); d[o + 3] = (byte)(v >> 24);
        }

        static float ReadF32(byte[] d, int o) { return BitConverter.Int32BitsToSingle((int)ReadU32(d, o)); }

        static void WriteF32(byte[] d, int o, float v) { WriteU32(d, o, (uint)BitConverter.SingleToInt32Bits(v)); }
    }
}
