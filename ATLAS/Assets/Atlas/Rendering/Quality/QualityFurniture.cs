using System;
using Atlas.World;

namespace Atlas.Rendering
{
    /// <summary>
    /// The furniture-level column of the quality table (mesh design section 5.4): the highest level a tier
    /// allows. Nominal and High allow Full, so a space keeps its own level. The first step below nominal and
    /// every lower step allow Light only, in every space, before any step lowers the render scale. It sits
    /// here rather than in <see cref="QualityRow"/>, which lives in Atlas.World.
    /// </summary>
    public static class QualityFurniture
    {
        static readonly FurnitureLevel[] s_cap =
        {
            FurnitureLevel.Full,    // Nominal
            FurnitureLevel.Light,   // Fair
            FurnitureLevel.Light,   // Serious
            FurnitureLevel.Light,   // Critical
            FurnitureLevel.Full,    // High
        };

        /// <summary>The highest level the tier allows.</summary>
        public static FurnitureLevel Cap(QualityTier tier)
        {
            int index = (int)tier;
            if (index < 0 || index >= QualityTable.Count)
                throw new ArgumentOutOfRangeException(nameof(tier));
            return s_cap[index];
        }

        /// <summary>The level a space gets under a tier: Light when the tier allows only Light, else the space's own.</summary>
        public static FurnitureLevel Limit(FurnitureLevel spaceLevel, QualityTier tier)
        {
            return Cap(tier) == FurnitureLevel.Light ? FurnitureLevel.Light : spaceLevel;
        }
    }
}
