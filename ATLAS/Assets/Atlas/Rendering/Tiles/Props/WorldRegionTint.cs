// P1-B07: the four regions' colours (Master Plan §3.4 "each has its own hue", §8.1). The hues are the palette's region cells
// (lane A contract §10.7); a tile takes its region through the look's tint, the fog through AtlasLook.SetFogColor.
using Unity.Mathematics;

namespace Atlas.Rendering
{
    public static class WorldRegionTint
    {
        static readonly float3[] tints = Build();

        static float3[] Build()
        {
            var t = new float3[AtlasPalette.RegionCount];
            for (int r = 0; r < t.Length; r++)
            {
                // The tint multiplies the palette colour: keep the cell's hue and saturation and lift its brightest channel to 1,
                // so a region colours its tiles without darkening them.
                float3 c = AtlasPalette.Linear(AtlasPalette.Region(r, AtlasPalette.RegionSteps - 1));
                t[r] = c / math.cmax(c);
            }
            return t;
        }

        /// <summary>_BaseTint rgb of a tile in region 0..3, linear.</summary>
        public static float3 Linear(int region) => tints[math.clamp(region, 0, tints.Length - 1)];

        /// <summary>Fog and background colour while the walker stands in region 0..3, linear: the region's darkest cell.</summary>
        public static float3 FogLinear(int region) => AtlasPalette.Linear(AtlasPalette.Region(region, 0));
    }
}
