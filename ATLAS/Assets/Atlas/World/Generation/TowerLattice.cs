using System.Collections.Generic;
using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.World
{
    // Master Plan §3.4 "a tower within rho <= 2.5 of every tile": residue classes of (X, Y, T) modulo the prime sqrt5 of Z[phi]
    // (phi = 3 mod sqrt5), normalised under negation. 15 classes occur, each with covering radius cosh = 1 + 3 phi (2.4529).
    public static class TowerLattice
    {
        public const int ClassCount = 15;

        static readonly int3[] classes =
        {
            new int3(0, 0, 1), new int3(0, 1, 2), new int3(0, 1, 3), new int3(1, 0, 2), new int3(1, 0, 3),
            new int3(1, 2, 1), new int3(1, 2, 4), new int3(1, 3, 1), new int3(1, 3, 4), new int3(2, 1, 1),
            new int3(2, 1, 4), new int3(2, 2, 0), new int3(2, 3, 0), new int3(2, 4, 1), new int3(2, 4, 4),
        };

        public static IReadOnlyList<int3> Classes => classes;

        static int Mod5(long a) { int r = (int)(a % 5); return r < 0 ? r + 5 : r; }
        static int Res(long u, long v) => (Mod5(u) + 3 * Mod5(v)) % 5;

        public static int3 Key(TileId id)
        {
            var k = new int3(Res(id.Xu, id.Xv), Res(id.Yu, id.Yv), Res(id.Tu, id.Tv));
            var n = new int3((5 - k.x) % 5, (5 - k.y) % 5, (5 - k.z) % 5);
            return Less(n, k) ? n : k;
        }

        static bool Less(int3 a, int3 b) => a.x != b.x ? a.x < b.x : a.y != b.y ? a.y < b.y : a.z < b.z;

        public static int ClassOf(TileId id)
        {
            var k = Key(id);
            for (int i = 0; i < classes.Length; i++) if (classes[i].Equals(k)) return i;
            return -1;
        }
    }
}
