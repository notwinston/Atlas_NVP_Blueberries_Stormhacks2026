using System;

namespace Atlas.Blades.Lung
{
    public enum AirwayTileKind : byte { Fork, Bend }

    // A tree tile: the fork tile of an address (FrameOf) or the turn tile in front of it (MP §10.2.3 Rule S).
    public readonly struct AirwayTile : IEquatable<AirwayTile>
    {
        public readonly string Address;
        public readonly AirwayTileKind Kind;

        AirwayTile(string a, AirwayTileKind k) { Address = a; Kind = k; }

        public static AirwayTile Fork(string a) => new AirwayTile(a ?? throw new ArgumentNullException(nameof(a)), AirwayTileKind.Fork);

        public static AirwayTile Bend(string a)
        {
            if (string.IsNullOrEmpty(a)) throw new ArgumentException("the root fork has no bend tile", nameof(a));
            return new AirwayTile(a, AirwayTileKind.Bend);
        }

        public bool Equals(AirwayTile o) => Kind == o.Kind && string.Equals(Address, o.Address, StringComparison.Ordinal);
        public override bool Equals(object o) => o is AirwayTile t && Equals(t);
        public override int GetHashCode() => (Address == null ? 0 : Address.GetHashCode()) * 2 + (int)Kind;
        public static bool operator ==(AirwayTile a, AirwayTile b) => a.Equals(b);
        public static bool operator !=(AirwayTile a, AirwayTile b) => !a.Equals(b);
        public override string ToString() => $"{Kind}(\"{Address}\")";
    }
}
