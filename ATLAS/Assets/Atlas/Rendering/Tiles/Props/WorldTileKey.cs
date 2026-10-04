// P1-B07: the part of a tile's generated content that decides its mesh. Two tiles with the same key share one mesh, so the
// number of meshes is bounded by the key, not by the number of tiles in the world.
using System;
using Atlas.World;

namespace Atlas.Rendering
{
    public readonly struct WorldTileKey : IEquatable<WorldTileKey>
    {
        public const int GardenVariants = 8;
        /// <summary>Code is below this.</summary>
        public const int CodeCount = 1 << 13;

        public readonly Archetype Archetype;
        public readonly byte KerbMask;       // kerbs this tile builds (TileContent.KerbMask)
        public readonly sbyte FeatureEdge;   // Doorway: door edge; SpanA/SpanB: shared edge; else -1
        public readonly byte Variant;        // Garden: 0..7 from the tile's content seed; else 0

        public WorldTileKey(Archetype archetype, int kerbMask, int featureEdge = -1, int variant = 0)
        {
            if (kerbMask < 0 || kerbMask > 15) throw new ArgumentOutOfRangeException(nameof(kerbMask));
            if (featureEdge < -1 || featureEdge > 3) throw new ArgumentOutOfRangeException(nameof(featureEdge));
            if (variant < 0 || variant >= GardenVariants) throw new ArgumentOutOfRangeException(nameof(variant));
            bool feature = archetype == Archetype.Doorway || archetype == Archetype.SpanA || archetype == Archetype.SpanB;
            Archetype = archetype; KerbMask = (byte)kerbMask;
            FeatureEdge = (sbyte)(feature ? featureEdge : -1);
            Variant = (byte)(archetype == Archetype.Garden ? variant : 0);
        }

        /// <summary>Deterministic from the generator's content: the Garden variant is bits 16..18 of the tile's ContentSeed.</summary>
        public static WorldTileKey From(in TileContent content) =>
            new WorldTileKey(content.Archetype, content.KerbMask & 15, content.FeatureEdge, (int)((content.PropSeed >> 16) & (GardenVariants - 1)));

        /// <summary>A tile with no generated content: the bare shell with its four wedges.</summary>
        public static WorldTileKey Plain => new WorldTileKey(Archetype.Plaza, 0);

        /// <summary>True when the archetype carries props (a Doorway or Span without its edge carries none).</summary>
        public bool HasProps =>
            Archetype == Archetype.Tower || Archetype == Archetype.Garden ||
            ((Archetype == Archetype.Doorway || Archetype == Archetype.SpanA || Archetype == Archetype.SpanB) && FeatureEdge >= 0);

        public int Code => (int)Archetype | (KerbMask << 3) | ((FeatureEdge + 1) << 7) | (Variant << 10);

        public bool Equals(WorldTileKey o) => Code == o.Code;
        public override bool Equals(object o) => o is WorldTileKey k && Equals(k);
        public override int GetHashCode() => Code;
        public override string ToString() => $"{Archetype} kerb={KerbMask:X} feat={FeatureEdge} var={Variant}";
    }
}
