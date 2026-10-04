using System;

namespace Atlas.Kernel
{
    // Tile identity: X, Y, T in Z[phi], X = Xu + Xv*phi, etc. (Master Plan A.5, §6.5; kernel.md §5.4 word order).
    public readonly struct TileId : IEquatable<TileId>
    {
        public readonly long Xu, Xv, Yu, Yv, Tu, Tv;

        public TileId(long xu, long xv, long yu, long yv, long tu, long tv)
        {
            Xu = xu; Xv = xv; Yu = yu; Yv = yv; Tu = tu; Tv = tv;
        }

        // Tile zero's centre is the base point o = (0,0,1), so T = 1.
        public static readonly TileId Zero = new TileId(0, 0, 0, 0, 1, 0);

        internal ZPhi X => new ZPhi(Xu, Xv);
        internal ZPhi Y => new ZPhi(Yu, Yv);
        internal ZPhi T => new ZPhi(Tu, Tv);

        // {4,5} only; pure, additions only (Master Plan A.5). Resolves the frame by descent from tile zero (kernel.md §9 point 2).
        public TileId Step(int edge)
        {
            if (edge < 0 || edge > 3) throw new ArgumentOutOfRangeException(nameof(edge));
            try { return ExactTiling.CanonicalFrame(this).Step(edge).Centre; }
            catch (OverflowException) { throw new OverflowException("tile id beyond the 64-bit range (A.5: 42 straight steps)"); }
        }

        // SplitMix64 fold over the six words (kernel.md §5.7, R3 §A5).
        public ulong ContentSeed(uint worldSeed)
        {
            unchecked
            {
                ulong h = 0x9E3779B97F4A7C15UL ^ worldSeed;
                h = Mix(h, Xu); h = Mix(h, Xv); h = Mix(h, Yu); h = Mix(h, Yv); h = Mix(h, Tu); h = Mix(h, Tv);
                return h;
            }
        }

        static ulong Mix(ulong h, long w)
        {
            unchecked
            {
                h += (ulong)w + 0x9E3779B97F4A7C15UL;
                h = (h ^ (h >> 30)) * 0xBF58476D1CE4E5B9UL;
                h = (h ^ (h >> 27)) * 0x94D049BB133111EBUL;
                return h ^ (h >> 31);
            }
        }

        public bool Equals(TileId o) =>
            Xu == o.Xu && Xv == o.Xv && Yu == o.Yu && Yv == o.Yv && Tu == o.Tu && Tv == o.Tv;
        public override bool Equals(object o) => o is TileId t && Equals(t);
        // kernel.md §4.4: deterministic, not HashCode.Combine
        public override int GetHashCode() { ulong h = ContentSeed(0); return unchecked((int)h ^ (int)(h >> 32)); }
        public static bool operator ==(TileId a, TileId b) => a.Equals(b);
        public static bool operator !=(TileId a, TileId b) => !a.Equals(b);
        public override string ToString() => $"({Xu}+{Xv}φ, {Yu}+{Yv}φ, {Tu}+{Tv}φ)";
    }
}
