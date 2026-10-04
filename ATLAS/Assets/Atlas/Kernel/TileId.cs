using System;
using System.Globalization;

namespace Atlas.Kernel
{
    /// <summary>
    /// The exact identity of one tile: six 64-bit integers. In {4,5} they are X, Y, T in Z[phi]
    /// (X = Xu + Xv·phi and so on) and the tile centre is (sqrt(phi)·X, sqrt(phi)·Y, T)
    /// (Master Plan section 6.5, Appendix A.5). Other tilings define their own use of the six words.
    /// </summary>
    public readonly struct TileId : IEquatable<TileId>
    {
        public readonly long Xu, Xv, Yu, Yv, Tu, Tv;

        public TileId(long xu, long xv, long yu, long yv, long tu, long tv)
        {
            Xu = xu; Xv = xv; Yu = yu; Yv = yv; Tu = tu; Tv = tv;
        }

        /// <summary>Tile zero, the home tile, in every tiling: (0, 0, 0, 0, 1, 0), that is X = 0, Y = 0, T = 1.</summary>
        public static TileId Origin
        {
            get { return new TileId(0, 0, 0, 0, 1, 0); }
        }

        /// <summary>True for default(TileId), all six words zero: "no tile". It is the value Appendix B.1 calls 0.</summary>
        public bool IsNone
        {
            get { return (Xu | Xv | Yu | Yv | Tu | Tv) == 0; }
        }

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

        /// <summary>The {4,5} neighbour across edge 0..3 of the tile's canonical frame; additions only.
        /// NotSupportedException in this build. Other lanes call ITileGraph.Neighbour.</summary>
        public TileId Step(int edge)
        {
            throw new NotSupportedException(Curvature.NotAvailable("the {4,5} tile step", "P1-B04"));
        }

        /// <summary>Deterministic content seed: the same on every device, runtime and exploration order.</summary>
        public ulong ContentSeed(uint worldSeed)
        {
            // SplitMix64 folded over the six words in wire order (R3 section A5). Integers only, so
            // Mono, IL2CPP and the Station's .NET agree bit for bit.
            unchecked
            {
                ulong h = 0x9E3779B97F4A7C15UL ^ worldSeed;
                h = Mix(h, Xu); h = Mix(h, Xv);
                h = Mix(h, Yu); h = Mix(h, Yv);
                h = Mix(h, Tu); h = Mix(h, Tv);
                return h;
            }
        }

        private static ulong Mix(ulong h, long word)
        {
            unchecked
            {
                h += (ulong)word + 0x9E3779B97F4A7C15UL;
                h = (h ^ (h >> 30)) * 0xBF58476D1CE4E5B9UL;
                h = (h ^ (h >> 27)) * 0x94D049BB133111EBUL;
                return h ^ (h >> 31);
            }
        }

        /// <summary>All six words equal.</summary>
        public bool Equals(TileId other)
        {
            return Xu == other.Xu && Xv == other.Xv && Yu == other.Yu
                && Yv == other.Yv && Tu == other.Tu && Tv == other.Tv;
        }

        public override bool Equals(object obj)
        {
            return obj is TileId other && Equals(other);
        }

        public override int GetHashCode()
        {
            ulong s = ContentSeed(0);
            return unchecked((int)(s ^ (s >> 32)));
        }

        public static bool operator ==(TileId a, TileId b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(TileId a, TileId b)
        {
            return !a.Equals(b);
        }

        /// <summary>The six words in wire order, decimal, comma-separated, invariant culture: "0,0,0,0,1,0".</summary>
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

        private static bool TryWord(string text, out long word)
        {
            return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out word);
        }
    }
}
