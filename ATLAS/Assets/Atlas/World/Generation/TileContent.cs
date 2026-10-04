using System;
using Atlas.Kernel;

namespace Atlas.World
{
    // Master Plan §3.4: what one tile contains. Pure function of (tile id, seed, footprint).
    public readonly struct TileContent : IEquatable<TileContent>
    {
        public readonly TileId Id;
        public readonly Archetype Archetype;
        public readonly byte Region;       // 0..3
        public readonly byte KerbMask;     // kerbs this tile builds
        public readonly byte ClosedMask;   // edges closed by a kerb from either side
        public readonly byte PostMask;     // always 0xF (§3.5)
        public readonly sbyte FeatureEdge; // Doorway: door edge; SpanA/SpanB: shared edge; else -1
        public readonly sbyte ParentEdge;  // edge towards tile zero; -1 on tile zero
        public readonly ulong PropSeed;

        public TileContent(TileId id, Archetype archetype, byte region, byte kerbMask, byte closedMask, byte postMask,
            sbyte featureEdge, sbyte parentEdge, ulong propSeed)
        {
            Id = id; Archetype = archetype; Region = region; KerbMask = kerbMask; ClosedMask = closedMask; PostMask = postMask;
            FeatureEdge = featureEdge; ParentEdge = parentEdge; PropSeed = propSeed;
        }

        public bool Equals(TileContent o) =>
            Id == o.Id && Archetype == o.Archetype && Region == o.Region && KerbMask == o.KerbMask && ClosedMask == o.ClosedMask &&
            PostMask == o.PostMask && FeatureEdge == o.FeatureEdge && ParentEdge == o.ParentEdge && PropSeed == o.PropSeed;
        public override bool Equals(object o) => o is TileContent c && Equals(c);
        public override int GetHashCode() => unchecked((int)PropSeed ^ (int)(PropSeed >> 32) ^ (int)Archetype);
        public static bool operator ==(TileContent a, TileContent b) => a.Equals(b);
        public static bool operator !=(TileContent a, TileContent b) => !a.Equals(b);
        public override string ToString() => $"{Id} {Archetype} r{Region} kerb={KerbMask:X} closed={ClosedMask:X} feat={FeatureEdge} parent={ParentEdge}";
    }
}
