using System;
using System.Numerics;
using Atlas.Kernel;

namespace Atlas.World
{
    // Exact order on Z[phi] (Master Plan A.5: phi = (1 + sqrt5)/2). No Int128 in .NET Standard 2.1, so BigInteger beyond 2^29.
    public static class ZPhiMath
    {
        const long Fast = 1L << 29;

        // u + v phi = (a + b sqrt5)/2 with a = 2u + v, b = v; for opposite signs compare a^2 with 5 b^2.
        public static int Sign(long u, long v)
        {
            if (v == 0) return Math.Sign(u);
            if (u == 0) return Math.Sign(v);
            if ((u > 0) == (v > 0)) return u > 0 ? 1 : -1;
            if (u > -Fast && u < Fast && v > -Fast && v < Fast)
            {
                long a = 2 * u + v;
                return a * a > 5 * v * v ? Math.Sign(a) : Math.Sign(v);
            }
            return Sign((BigInteger)u, (BigInteger)v);
        }

        static int Sign(BigInteger u, BigInteger v)
        {
            if (v.IsZero) return u.Sign;
            if (u.IsZero) return v.Sign;
            if (u.Sign == v.Sign) return u.Sign;
            BigInteger a = 2 * u + v;
            return a * a > 5 * v * v ? a.Sign : v.Sign;
        }

        public static int Compare(long u1, long v1, long u2, long v2)
        {
            const long F = 1L << 40;
            if (u1 > -F && u1 < F && v1 > -F && v1 < F && u2 > -F && u2 < F && v2 > -F && v2 < F)
                return Sign(u1 - u2, v1 - v2);
            return Sign((BigInteger)u1 - u2, (BigInteger)v1 - v2);
        }
    }

    public static class TileOrder
    {
        // Lexicographic on (Xu, Xv, Yu, Yv, Tu, Tv).
        public static int Compare(TileId a, TileId b)
        {
            int c = a.Xu.CompareTo(b.Xu); if (c != 0) return c;
            c = a.Xv.CompareTo(b.Xv); if (c != 0) return c;
            c = a.Yu.CompareTo(b.Yu); if (c != 0) return c;
            c = a.Yv.CompareTo(b.Yv); if (c != 0) return c;
            c = a.Tu.CompareTo(b.Tu); if (c != 0) return c;
            return a.Tv.CompareTo(b.Tv);
        }
    }
}
