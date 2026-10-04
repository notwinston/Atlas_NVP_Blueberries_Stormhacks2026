// Branch address of the model airway (docs/case_contract.md section 10.1): a string over {L, R}, the trachea is "".
// Held as bits so that prefix comparison, parent, child and sibling are integer operations with no allocation.
using System;

namespace Atlas.Airway
{
    /// <summary>A letter of an address, or "none" (the trachea has no letter; a compass tie has no choice).</summary>
    public enum AirwayChoice : byte
    {
        None = 0,
        Left = 1,
        Right = 2,
    }

    /// <summary>
    /// An address over {L, R} of at most <see cref="MaxLength"/> letters. <see cref="Bits"/> is the address read as a
    /// binary number, first letter most significant, L = 0 and R = 1 (the "b" of tree.json's node index).
    /// default(AirwayAddress) is the trachea.
    /// </summary>
    public readonly struct AirwayAddress : IEquatable<AirwayAddress>
    {
        /// <summary>The plan's addressable depth (10.2.3) and tree.json's max_address_len.</summary>
        public const int MaxLength = 23;

        public readonly uint Bits;
        public readonly int Length;

        public AirwayAddress(uint bits, int length)
        {
            if (length < 0 || length > MaxLength) throw new ArgumentOutOfRangeException(nameof(length), "an address has 0 to 23 letters");
            if (length < 32 && (bits >> length) != 0) throw new ArgumentException("address bits beyond its length");
            Bits = bits; Length = length;
        }

        AirwayAddress(uint bits, int length, bool _unchecked) { Bits = bits; Length = length; }

        public static AirwayAddress Trachea { get { return default(AirwayAddress); } }

        /// <summary>Generation = number of letters.</summary>
        public int Gen { get { return Length; } }
        public bool IsTrachea { get { return Length == 0; } }

        /// <summary>Index in tree.json "nodes": (2^gen - 1) + Bits. Distinct for distinct addresses.</summary>
        public int NodeIndex { get { return ((1 << Length) - 1) + (int)Bits; } }

        /// <summary>True when letter i (0 = first after the trachea) is R.</summary>
        public bool IsRight(int i)
        {
            if ((uint)i >= (uint)Length) throw new ArgumentOutOfRangeException(nameof(i));
            return ((Bits >> (Length - 1 - i)) & 1u) != 0;
        }

        public AirwayChoice LetterAt(int i) { return IsRight(i) ? AirwayChoice.Right : AirwayChoice.Left; }

        /// <summary>The last letter; None for the trachea.</summary>
        public AirwayChoice LastLetter
        {
            get { return Length == 0 ? AirwayChoice.None : ((Bits & 1u) != 0 ? AirwayChoice.Right : AirwayChoice.Left); }
        }

        public bool CanDescend { get { return Length < MaxLength; } }

        public AirwayAddress Child(bool right)
        {
            if (Length >= MaxLength) throw new InvalidOperationException("address is at the maximum length of 23");
            return new AirwayAddress((Bits << 1) | (right ? 1u : 0u), Length + 1, true);
        }

        public AirwayAddress Child(AirwayChoice letter)
        {
            if (letter == AirwayChoice.None) throw new ArgumentException("a child needs the letter L or R");
            return Child(letter == AirwayChoice.Right);
        }

        public AirwayAddress Parent
        {
            get
            {
                if (Length == 0) throw new InvalidOperationException("the trachea has no parent");
                return new AirwayAddress(Bits >> 1, Length - 1, true);
            }
        }

        public AirwayAddress Sibling
        {
            get
            {
                if (Length == 0) throw new InvalidOperationException("the trachea has no sibling");
                return new AirwayAddress(Bits ^ 1u, Length, true);
            }
        }

        /// <summary>The first k letters.</summary>
        public AirwayAddress Prefix(int k)
        {
            if ((uint)k > (uint)Length) throw new ArgumentOutOfRangeException(nameof(k));
            return new AirwayAddress(Bits >> (Length - k), k, true);
        }

        /// <summary>Number of leading letters a and b share.</summary>
        public static int CommonPrefixLength(AirwayAddress a, AirwayAddress b)
        {
            int m = a.Length < b.Length ? a.Length : b.Length;
            uint x = (a.Bits >> (a.Length - m)) ^ (b.Bits >> (b.Length - m));
            while (x != 0) { x >>= 1; m--; }
            return m;
        }

        /// <summary>True when this address is a prefix of other (equal counts).</summary>
        public bool IsPrefixOf(AirwayAddress other)
        {
            return Length <= other.Length && (other.Bits >> (other.Length - Length)) == Bits;
        }

        public bool IsProperPrefixOf(AirwayAddress other) { return Length < other.Length && IsPrefixOf(other); }

        public bool Equals(AirwayAddress other) { return Bits == other.Bits && Length == other.Length; }
        public override bool Equals(object obj) { return obj is AirwayAddress a && Equals(a); }
        public override int GetHashCode() { return NodeIndex; }
        public static bool operator ==(AirwayAddress a, AirwayAddress b) { return a.Equals(b); }
        public static bool operator !=(AirwayAddress a, AirwayAddress b) { return !a.Equals(b); }

        /// <summary>Writes the letters as ASCII into dst; returns Length. No allocation.</summary>
        public int CopyAscii(byte[] dst, int offset)
        {
            for (int i = 0; i < Length; i++)
                dst[offset + i] = ((Bits >> (Length - 1 - i)) & 1u) != 0 ? (byte)'R' : (byte)'L';
            return Length;
        }

        /// <summary>The address as text ("" for the trachea). Allocates: for events and logs, not per frame.</summary>
        public override string ToString()
        {
            if (Length == 0) return "";
            var c = new char[Length];
            for (int i = 0; i < Length; i++) c[i] = ((Bits >> (Length - 1 - i)) & 1u) != 0 ? 'R' : 'L';
            return new string(c);
        }

        public static bool TryParse(string text, out AirwayAddress address)
        {
            address = default(AirwayAddress);
            if (text == null || text.Length > MaxLength) return false;
            uint bits = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == 'L') bits <<= 1;
                else if (ch == 'R') bits = (bits << 1) | 1u;
                else return false;
            }
            address = new AirwayAddress(bits, text.Length, true);
            return true;
        }

        /// <summary>Throws ArgumentException for anything that is not a string over {L, R} of at most 23 letters.</summary>
        public static AirwayAddress Parse(string text)
        {
            AirwayAddress a;
            if (!TryParse(text, out a))
                throw new ArgumentException("not an airway address (a string over {L, R} of at most 23 letters): " + (text ?? "null"));
            return a;
        }

        public static char Letter(AirwayChoice c)
        {
            return c == AirwayChoice.Left ? 'L' : c == AirwayChoice.Right ? 'R' : '-';
        }
    }
}
