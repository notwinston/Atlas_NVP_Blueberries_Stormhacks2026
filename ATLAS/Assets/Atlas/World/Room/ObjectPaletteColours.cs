using System;
using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>
    /// The 64 object colours of the palette (cells 64..127: four value rows of 16 hues) as linear rgb, for
    /// engine-free code that picks a cell by colour (the on-phone prop colouring). A copy of the rule in
    /// Atlas.Rendering's AtlasPalette (rows 4 to 7 of TexelSrgb, then Linear), which this assembly cannot
    /// reference; a Rendering test holds the two equal for every cell. Thread-safe.
    /// </summary>
    public static class ObjectPaletteColours
    {
        public const int First = 64, Count = 64;

        static readonly float[] s_Values = { 0.28f, 0.42f, 0.58f, 0.74f };
        static readonly float3[] s_Linear = Build();

        /// <summary>The colour of cell First..First + Count - 1 in linear rgb, as the shader samples it from the
        /// palette texture. ArgumentOutOfRangeException for any other cell.</summary>
        public static float3 Linear(int cell)
        {
            if (cell < First || cell >= First + Count)
                throw new ArgumentOutOfRangeException(nameof(cell), "Object colours are cells 64 to 127.");
            return s_Linear[cell - First];
        }

        static float3[] Build()
        {
            var colours = new float3[Count];
            for (int k = 0; k < Count; k++)
            {
                float3 srgb = Srgb(k & 15, k >> 4);
                // From the stored 8-bit value, as AtlasPalette.Linear does.
                colours[k] = SrgbToLinear(math.round(srgb * 255f) / 255f);
            }
            return colours;
        }

        // AtlasPalette.TexelSrgb, rows 4 to 7.
        static float3 Srgb(int column, int valueStep)
        {
            float value = s_Values[valueStep];
            if (column < 12) return HsvToRgb(column * 30f, 0.50f, value);
            if (column == 12) return HsvToRgb(30f, 0.12f, value);
            if (column == 13) return HsvToRgb(210f, 0.12f, value);
            if (column == 14) return HsvToRgb(28f, 0.35f, value);
            return HsvToRgb(0f, 0f, value);
        }

        static float3 SrgbToLinear(float3 srgb)
        {
            return new float3(SrgbToLinear(srgb.x), SrgbToLinear(srgb.y), SrgbToLinear(srgb.z));
        }

        static float SrgbToLinear(float c)
        {
            return c <= 0.04045f ? c / 12.92f : math.pow((c + 0.055f) / 1.055f, 2.4f);
        }

        // AtlasPalette.HsvToRgb, operation for operation.
        static float3 HsvToRgb(float hueDegrees, float saturation, float value)
        {
            float h = hueDegrees - 360f * math.floor(hueDegrees / 360f);
            float sector = h / 60f;
            float chroma = value * saturation;
            float x = chroma * (1f - math.abs(sector - 2f * math.floor(sector / 2f) - 1f));
            float m = value - chroma;
            float3 rgb;
            if (sector < 1f) rgb = new float3(chroma, x, 0f);
            else if (sector < 2f) rgb = new float3(x, chroma, 0f);
            else if (sector < 3f) rgb = new float3(0f, chroma, x);
            else if (sector < 4f) rgb = new float3(0f, x, chroma);
            else if (sector < 5f) rgb = new float3(x, 0f, chroma);
            else rgb = new float3(chroma, 0f, x);
            return rgb + m;
        }
    }
}
