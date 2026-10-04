using System;
using Unity.Mathematics;

namespace Atlas.Rendering
{
    /// <summary>
    /// The one palette of the look (Master Plan section 8.1; lane A contract, section 10.7): 16 x 16 cells of
    /// 8 x 8 pixels, a dark base, category tints, object colours, gradient strips and emissive accents.
    /// Engine-free: this class computes the texel colours and the coordinates; <see cref="AtlasPaletteTexture"/>
    /// uploads them.
    /// </summary>
    public static class AtlasPalette
    {
        public const int Size = 128, CellsPerSide = 16, CellPixels = 8;
        /// <summary>Number of cells: indices run from 0 to CellCount - 1.</summary>
        public const int CellCount = CellsPerSide * CellsPerSide;
        /// <summary>First emissive row: a palette coordinate with v at or above EmissiveV is an accent.</summary>
        public const int EmissiveRow = 12;
        public const float EmissiveV = 0.75f;

        // Row 0: the neutral ramp, the dark base.
        public const int Void = 0, FogBase = 1, SkyTop = 3, Text = 15;

        // Row 1: structure.
        public const int Floor = 16, FloorAlt = 17, Wall = 18, WallAlt = 19, Kerb = 20, Post = 21, DoorFrame = 22,
            Ceiling = 23, Opening = 24, Placeholder = 25, AvatarBody = 26, AvatarLeft = 27, AvatarRight = 28,
            Landmark = 29, Booth = 30, VirtualWall = 31;

        // The rubber duck of the S3 maze (a kit piece). Category tints exist for the 23 object categories, 0 to 22, so the
        // cells of categories 28 to 31 are free; they hold the duck's four colours instead, and Category(28 to 31) is the
        // duck's colour, not a tint (nothing asks for it). Non-emissive: row 3 is below EmissiveRow.
        // Row 3: the duck.
        public const int DuckYellow = 60, DuckOrange = 61, DuckWhite = 62, DuckBlack = 63;

        // Blocks.
        public const int CategoryBase = 32, CategoryCount = 32;
        public const int ObjectBase = 64, ObjectCount = 64;
        public const int RegionBase = 128, RegionCount = 4, RegionSteps = 4;
        public const int Cell4DBase = 208, Cell4DCount = 32;

        // Gradient strips: rows, for StripUv.
        public const int StripWarm = 9, StripCool = 10, StripDepth = 11, StripW = 15;

        // Row 12: emissive accents.
        public const int PortalFrame = 192, Beacon = 193, HomeBeacon = 194, Flag = 195, LapMarker = 196,
            CueWarn = 197, CueStrong = 198, CueRescan = 199, CueStandStill = 200, LinkOk = 201,
            LinkDegraded = 202, LinkOff = 203, Reticle = 204, SliceEdge = 205, GridLine = 206, Spare = 207;

        static readonly uint[] s_Structure =
        {
            0x1B222B, 0x222B36, 0x2A3542, 0x33404F, 0x3A4858, 0x50627A, 0x6B7F99, 0x161C24,
            0x0E1319, 0x3B4654, 0x7A8794, 0xD9534F, 0x3FA7D6, 0xC9A227, 0x4A3F6B, 0x2E2A45,
        };

        static readonly uint[] s_Accents =
        {
            0x7DF9FF, 0xFFD166, 0x9BE564, 0xFF6B6B, 0xFFE66D, 0xFFB020, 0xFF3B30, 0x5AC8FA,
            0xFFFFFF, 0x34C759, 0xFFCC00, 0x8E8E93, 0xFFFFFF, 0xFFFFFF, 0x6FA8C8, 0x000000,
        };

        static readonly float[] s_ObjectValues = { 0.28f, 0.42f, 0.58f, 0.74f };

        static readonly float[] s_RegionHues = { 200f, 150f, 35f, 320f };

        /// <summary>Palette coordinate of the centre of cell index (0..255).</summary>
        public static float2 Uv(int index)
        {
            int column = index & (CellsPerSide - 1);
            int row = (index >> 4) & (CellsPerSide - 1);
            return new float2((column + 0.5f) / CellsPerSide, (row + 0.5f) / CellsPerSide);
        }

        /// <summary>Coordinate on gradient strip row (0..15) at t in 0..1 along the strip.</summary>
        public static float2 StripUv(int row, float t)
        {
            // Half a pixel in from each end, so the two ends of the strip are exactly its first and last texel.
            float u = (0.5f + math.saturate(t) * (Size - 1)) / Size;
            return new float2(u, (row + 0.5f) / CellsPerSide);
        }

        /// <summary>The colour of a cell in linear rgb, for values that go into _BaseTint, fog or line colour.</summary>
        public static float3 Linear(int index)
        {
            // From the stored 8-bit value, so the result equals what the shader samples from the texture.
            return SrgbToLinear(math.round(Srgb(index) * 255f) / 255f);
        }

        /// <summary>The colour of a cell in sRGB, 0..1, at the cell's centre.</summary>
        public static float3 Srgb(int index)
        {
            int column = index & (CellsPerSide - 1);
            int row = (index >> 4) & (CellsPerSide - 1);
            return TexelSrgb(column * CellPixels + CellPixels / 2, row * CellPixels + CellPixels / 2);
        }

        /// <summary>Tint cell of an object category (0..31): CategoryBase + category.</summary>
        public static int Category(int category)
        {
            return CategoryBase + math.clamp(category, 0, CategoryCount - 1);
        }

        /// <summary>Cell of region r (0..3) at lightness step t (0..3): RegionBase + 4 * r + t.</summary>
        public static int Region(int region, int step)
        {
            return RegionBase + RegionSteps * math.clamp(region, 0, RegionCount - 1) + math.clamp(step, 0, RegionSteps - 1);
        }

        /// <summary>Emissive cell of a 4D cell: Cell4DBase + (cell mod 32).</summary>
        public static int Cell4D(int cell)
        {
            return Cell4DBase + (((cell % Cell4DCount) + Cell4DCount) % Cell4DCount);
        }

        /// <summary>Object colour cell (lane D's AssetReady.palette): ObjectBase + 16 * valueStep + column.</summary>
        public static int Object(int column, int valueStep)
        {
            return ObjectBase + CellsPerSide * math.clamp(valueStep, 0, 3) + math.clamp(column, 0, CellsPerSide - 1);
        }

        /// <summary>True when the cell lies in the emissive rows 12 to 15.</summary>
        public static bool IsEmissive(int index)
        {
            return ((index >> 4) & (CellsPerSide - 1)) >= EmissiveRow;
        }

        /// <summary>
        /// The sRGB colour, 0..1, of the texel at pixel (x, y); y = 0 is the bottom row. Cells are flat; the strips
        /// of rows 9, 10, 11 and 15 run continuously along x.
        /// </summary>
        public static float3 TexelSrgb(int x, int y)
        {
            int column = math.clamp(x, 0, Size - 1) / CellPixels;
            int row = math.clamp(y, 0, Size - 1) / CellPixels;
            float along = math.clamp(x, 0, Size - 1) / (float)(Size - 1);

            switch (row)
            {
                case 0:
                    return HsvToRgb(215f, 0.25f - 0.01f * column, 0.03f + 0.05f * column);
                case 1:
                    return Hex(s_Structure[column]);
                case 2:
                case 3:
                {
                    int category = (row - 2) * CellsPerSide + column;
                    int cell = CategoryBase + category;
                    if (cell == DuckYellow) return Hex(0xFFC21A);
                    if (cell == DuckOrange) return Hex(0xFF4A12);
                    if (cell == DuckWhite) return Hex(0xE8E8E8);
                    if (cell == DuckBlack) return Hex(0x1A1A1A);
                    return category == 0
                        ? HsvToRgb(0f, 0f, 0.45f)
                        : HsvToRgb(GoldenHue(category), 0.40f, 0.50f);
                }
                case 4:
                case 5:
                case 6:
                case 7:
                {
                    float value = s_ObjectValues[row - 4];
                    if (column < 12) return HsvToRgb(column * 30f, 0.50f, value);
                    if (column == 12) return HsvToRgb(30f, 0.12f, value);
                    if (column == 13) return HsvToRgb(210f, 0.12f, value);
                    if (column == 14) return HsvToRgb(28f, 0.35f, value);
                    return HsvToRgb(0f, 0f, value);
                }
                case 8:
                    return HsvToRgb(s_RegionHues[column / RegionSteps], 0.45f, 0.22f + 0.12f * (column % RegionSteps));
                case 9:
                    return HsvToRgb(15f, 0.55f, math.lerp(0.15f, 0.75f, along));
                case 10:
                    return HsvToRgb(205f, 0.55f, math.lerp(0.15f, 0.75f, along));
                case 11:
                    return HsvToRgb(215f, 0.20f, math.lerp(0.40f, 0.05f, along));
                case 12:
                    return Hex(s_Accents[column]);
                case 13:
                case 14:
                    return HsvToRgb(GoldenHue((row - 13) * CellsPerSide + column), 0.65f, 0.95f);
                default:
                    return math.lerp(Hex(0xFF7F6A), Hex(0x8E6BFF), along);
            }
        }

        /// <summary>
        /// Writes the whole palette as RGBA8, sRGB encoded, row 0 (the bottom row) first: Size * Size * 4 bytes.
        /// Alpha is 255. Allocates nothing.
        /// </summary>
        public static void WritePixels(byte[] rgba)
        {
            if (rgba == null) throw new ArgumentNullException(nameof(rgba));
            if (rgba.Length < Size * Size * 4)
                throw new ArgumentException("The palette needs Size * Size * 4 bytes.", nameof(rgba));

            int o = 0;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float3 c = TexelSrgb(x, y);
                    rgba[o++] = ToByte(c.x);
                    rgba[o++] = ToByte(c.y);
                    rgba[o++] = ToByte(c.z);
                    rgba[o++] = 255;
                }
            }
        }

        /// <summary>sRGB 0..1 to linear, the exact piecewise curve the GPU applies to an sRGB texture.</summary>
        public static float3 SrgbToLinear(float3 srgb)
        {
            return new float3(SrgbToLinear(srgb.x), SrgbToLinear(srgb.y), SrgbToLinear(srgb.z));
        }

        static float SrgbToLinear(float c)
        {
            return c <= 0.04045f ? c / 12.92f : math.pow((c + 0.055f) / 1.055f, 2.4f);
        }

        /// <summary>Hue in degrees, saturation and value 0..1, to sRGB 0..1.</summary>
        public static float3 HsvToRgb(float hueDegrees, float saturation, float value)
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

        // Successive hues 137.5 degrees apart never cluster, however many are used.
        static float GoldenHue(int k)
        {
            float hue = k * 137.5f;
            return hue - 360f * math.floor(hue / 360f);
        }

        static float3 Hex(uint rgb)
        {
            return new float3(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
        }

        static byte ToByte(float c)
        {
            return (byte)math.clamp((int)math.round(c * 255f), 0, 255);
        }
    }
}
