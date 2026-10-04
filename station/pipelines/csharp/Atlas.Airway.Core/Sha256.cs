// SHA-256 (FIPS 180-4) with its working memory held by the instance, so that hashing a node allocates nothing.
// System.Security.Cryptography would do the same job but returns a new byte[] per call on the API level Unity offers.
// The test runner checks this class against System.Security.Cryptography.SHA256.
using System;

namespace Atlas.Airway
{
    /// <summary>Not thread-safe: one instance per thread.</summary>
    public sealed class Sha256
    {
        static readonly uint[] K =
        {
            0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
            0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
            0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
            0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
            0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
            0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
            0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
            0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
        };

        readonly uint[] w = new uint[64];
        readonly byte[] block = new byte[64];
        readonly uint[] h = new uint[8];

        /// <summary>Hashes data[0 .. length). The digest stays in this instance until the next call.</summary>
        public void Compute(byte[] data, int length)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (length < 0 || length > data.Length) throw new ArgumentOutOfRangeException(nameof(length));
            h[0] = 0x6a09e667; h[1] = 0xbb67ae85; h[2] = 0x3c6ef372; h[3] = 0xa54ff53a;
            h[4] = 0x510e527f; h[5] = 0x9b05688c; h[6] = 0x1f83d9ab; h[7] = 0x5be0cd19;

            int off = 0;
            for (; off + 64 <= length; off += 64) Round(data, off);

            int rest = length - off;
            for (int i = 0; i < rest; i++) block[i] = data[off + i];
            block[rest] = 0x80;
            for (int i = rest + 1; i < 64; i++) block[i] = 0;
            if (rest >= 56)
            {
                Round(block, 0);
                for (int i = 0; i < 64; i++) block[i] = 0;
            }
            ulong bits = (ulong)length * 8UL;
            for (int i = 0; i < 8; i++) block[63 - i] = (byte)(bits >> (8 * i));
            Round(block, 0);
        }

        /// <summary>Bytes 8k .. 8k+7 of the digest as a big-endian unsigned 64-bit integer, k = 0..3.</summary>
        public ulong Word64(int k)
        {
            if ((uint)k > 3u) throw new ArgumentOutOfRangeException(nameof(k));
            return ((ulong)h[2 * k] << 32) | h[2 * k + 1];
        }

        /// <summary>Byte i of the digest, i = 0..31.</summary>
        public byte DigestByte(int i)
        {
            if ((uint)i > 31u) throw new ArgumentOutOfRangeException(nameof(i));
            return (byte)(h[i >> 2] >> (24 - 8 * (i & 3)));
        }

        static uint Rotr(uint x, int n) { return (x >> n) | (x << (32 - n)); }

        void Round(byte[] d, int off)
        {
            for (int i = 0; i < 16; i++)
                w[i] = ((uint)d[off + 4 * i] << 24) | ((uint)d[off + 4 * i + 1] << 16) | ((uint)d[off + 4 * i + 2] << 8) | d[off + 4 * i + 3];
            for (int i = 16; i < 64; i++)
            {
                uint s0 = Rotr(w[i - 15], 7) ^ Rotr(w[i - 15], 18) ^ (w[i - 15] >> 3);
                uint s1 = Rotr(w[i - 2], 17) ^ Rotr(w[i - 2], 19) ^ (w[i - 2] >> 10);
                w[i] = unchecked(w[i - 16] + s0 + w[i - 7] + s1);
            }
            uint a = h[0], b = h[1], c = h[2], dd = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
            for (int i = 0; i < 64; i++)
            {
                uint S1 = Rotr(e, 6) ^ Rotr(e, 11) ^ Rotr(e, 25);
                uint ch = (e & f) ^ (~e & g);
                uint t1 = unchecked(hh + S1 + ch + K[i] + w[i]);
                uint S0 = Rotr(a, 2) ^ Rotr(a, 13) ^ Rotr(a, 22);
                uint maj = (a & b) ^ (a & c) ^ (b & c);
                uint t2 = unchecked(S0 + maj);
                hh = g; g = f; f = e; e = unchecked(dd + t1); dd = c; c = b; b = a; a = unchecked(t1 + t2);
            }
            unchecked
            {
                h[0] += a; h[1] += b; h[2] += c; h[3] += dd; h[4] += e; h[5] += f; h[6] += g; h[7] += hh;
            }
        }
    }
}
