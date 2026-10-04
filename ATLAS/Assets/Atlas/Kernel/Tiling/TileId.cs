using System;
using System.Globalization;

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

        /// <summary>Tile zero under the name of the frame-loop contract (lane-b-kernel.md §2.4): the same value as Zero.</summary>
        public static TileId Origin => Zero;

        /// <summary>True for default(TileId), all six words zero: "no tile". It is the value Appendix B.1 calls 0.</summary>
        public bool IsNone => (Xu | Xv | Yu | Yv | Tu | Tv) == 0;

        internal ZPhi X => new ZPhi(Xu, Xv);
        internal ZPhi Y => new ZPhi(Yu, Yv);
        internal ZPhi T => new ZPhi(Tu, Tv);

        /// <summary>Word 0..5 in wire order: Xu, Xv, Yu, Yv, Tu, Tv. Other index: ArgumentOutOfRangeException.</summary>
        public long this[int word]
        {
            get
            {
                switch (word)
                {
                    case 0: return Xu;
                    case 1: return Xv;
                    case 2: return Yu;
                    case 3: return Yv;
                    case 4: return Tu;
                    case 5: return Tv;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(word), word,
                            "Atlas.Kernel: a tile id has words 0 to 5.");
                }
            }
        }

        /// <summary>Writes the six words in wire order into the first six elements.</summary>
        public void CopyTo(Span<long> words6)
        {
            if (words6.Length < 6)
            {
                throw new ArgumentException("Atlas.Kernel: six elements are needed.", nameof(words6));
            }
            words6[0] = Xu; words6[1] = Xv; words6[2] = Yu;
            words6[3] = Yv; words6[4] = Tu; words6[5] = Tv;
        }

        /// <summary>Reads six words in wire order (Appendix B.1, State.tileId).</summary>
        public static TileId FromWords(ReadOnlySpan<long> words6)
        {
            if (words6.Length < 6)
            {
                throw new ArgumentException("Atlas.Kernel: six elements are needed.", nameof(words6));
            }
            return new TileId(words6[0], words6[1], words6[2], words6[3], words6[4], words6[5]);
        }

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

        /// <summary>The six words in wire order, decimal, comma-separated, invariant culture: "0,0,0,0,1,0".
        /// This is the form a tile id takes in JSON (events, logs).</summary>
        public override string ToString()
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            return Xu.ToString(inv) + "," + Xv.ToString(inv) + "," + Yu.ToString(inv) + ","
                 + Yv.ToString(inv) + "," + Tu.ToString(inv) + "," + Tv.ToString(inv);
        }

        /// <summary>Parses the text form written by ToString. False, and "no tile", for anything else.</summary>
        public static bool TryParse(string text, out TileId id)
        {
            id = default;
            if (string.IsNullOrEmpty(text)) return false;

            string[] parts = text.Split(',');
            if (parts.Length != 6) return false;

            long w0, w1, w2, w3, w4, w5;
            if (!TryWord(parts[0], out w0) || !TryWord(parts[1], out w1) || !TryWord(parts[2], out w2) ||
                !TryWord(parts[3], out w3) || !TryWord(parts[4], out w4) || !TryWord(parts[5], out w5))
            {
                return false;
            }
            id = new TileId(w0, w1, w2, w3, w4, w5);
            return true;
        }

        static bool TryWord(string text, out long word)
        {
            return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out word);
        }
    }
}
