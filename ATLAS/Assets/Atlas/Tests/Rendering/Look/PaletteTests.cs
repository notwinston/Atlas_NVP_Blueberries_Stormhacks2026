using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Rendering.Tests.Look
{
    /// <summary>The palette layout and texel values of lane A's contract, section 10.7.</summary>
    public class PaletteTests
    {
        static byte[] Pixels()
        {
            var pixels = new byte[AtlasPalette.Size * AtlasPalette.Size * 4];
            AtlasPalette.WritePixels(pixels);
            return pixels;
        }

        static int Rgb(byte[] pixels, int x, int y)
        {
            int o = (y * AtlasPalette.Size + x) * 4;
            return (pixels[o] << 16) | (pixels[o + 1] << 8) | pixels[o + 2];
        }

        static int CellRgb(byte[] pixels, int index)
        {
            return Rgb(pixels, (index % 16) * 8 + 4, (index / 16) * 8 + 4);
        }

        [Test]
        public void Layout_Is16By16CellsOf8Pixels()
        {
            Assert.That(AtlasPalette.Size, Is.EqualTo(128));
            Assert.That(AtlasPalette.CellsPerSide * AtlasPalette.CellPixels, Is.EqualTo(AtlasPalette.Size));
            Assert.That(AtlasPalette.CellCount, Is.EqualTo(256));
        }

        [Test]
        public void NamedIndices_AreFrozen()
        {
            Assert.That(AtlasPalette.Void, Is.EqualTo(0));
            Assert.That(AtlasPalette.FogBase, Is.EqualTo(1));
            Assert.That(AtlasPalette.SkyTop, Is.EqualTo(3));
            Assert.That(AtlasPalette.Text, Is.EqualTo(15));
            Assert.That(AtlasPalette.Floor, Is.EqualTo(16));
            Assert.That(AtlasPalette.Wall, Is.EqualTo(18));
            Assert.That(AtlasPalette.Placeholder, Is.EqualTo(25));
            Assert.That(AtlasPalette.VirtualWall, Is.EqualTo(31));
            Assert.That(AtlasPalette.CategoryBase, Is.EqualTo(32));
            Assert.That(AtlasPalette.ObjectBase, Is.EqualTo(64));
            Assert.That(AtlasPalette.RegionBase, Is.EqualTo(128));
            Assert.That(AtlasPalette.PortalFrame, Is.EqualTo(192));
            Assert.That(AtlasPalette.CueWarn, Is.EqualTo(197));
            Assert.That(AtlasPalette.CueStrong, Is.EqualTo(198));
            Assert.That(AtlasPalette.GridLine, Is.EqualTo(206));
            Assert.That(AtlasPalette.Cell4DBase, Is.EqualTo(208));
        }

        [Test]
        public void Uv_IsTheCellCentre_RowZeroAtTheBottom()
        {
            Assert.That(AtlasPalette.Uv(0).x, Is.EqualTo(0.5f / 16f).Within(1e-6f));
            Assert.That(AtlasPalette.Uv(0).y, Is.EqualTo(0.5f / 16f).Within(1e-6f));
            float2 wall = AtlasPalette.Uv(AtlasPalette.Wall);
            Assert.That(wall.x, Is.EqualTo(2.5f / 16f).Within(1e-6f));
            Assert.That(wall.y, Is.EqualTo(1.5f / 16f).Within(1e-6f));
            float2 last = AtlasPalette.Uv(255);
            Assert.That(last.x, Is.EqualTo(15.5f / 16f).Within(1e-6f));
            Assert.That(last.y, Is.EqualTo(15.5f / 16f).Within(1e-6f));
        }

        [Test]
        public void StructureAndAccentCells_HoldTheContractColours()
        {
            byte[] pixels = Pixels();
            Assert.That(CellRgb(pixels, AtlasPalette.Floor), Is.EqualTo(0x1B222B));
            Assert.That(CellRgb(pixels, AtlasPalette.Wall), Is.EqualTo(0x2A3542));
            Assert.That(CellRgb(pixels, AtlasPalette.DoorFrame), Is.EqualTo(0x6B7F99));
            Assert.That(CellRgb(pixels, AtlasPalette.AvatarLeft), Is.EqualTo(0xD9534F));
            Assert.That(CellRgb(pixels, AtlasPalette.VirtualWall), Is.EqualTo(0x2E2A45));
            Assert.That(CellRgb(pixels, AtlasPalette.PortalFrame), Is.EqualTo(0x7DF9FF));
            Assert.That(CellRgb(pixels, AtlasPalette.CueWarn), Is.EqualTo(0xFFB020));
            Assert.That(CellRgb(pixels, AtlasPalette.CueStrong), Is.EqualTo(0xFF3B30));
            Assert.That(CellRgb(pixels, AtlasPalette.LinkOk), Is.EqualTo(0x34C759));
            Assert.That(CellRgb(pixels, AtlasPalette.GridLine), Is.EqualTo(0x6FA8C8));
        }

        [Test]
        public void EveryFlatCell_IsOneColourEdgeToEdge()
        {
            byte[] pixels = Pixels();
            for (int index = 0; index < 256; index++)
            {
                int row = index / 16;
                if (row == 9 || row == 10 || row == 11 || row == 15) continue;   // strips
                int x0 = (index % 16) * 8, y0 = row * 8;
                int centre = CellRgb(pixels, index);
                Assert.That(Rgb(pixels, x0, y0), Is.EqualTo(centre), "cell " + index);
                Assert.That(Rgb(pixels, x0 + 7, y0 + 7), Is.EqualTo(centre), "cell " + index);
            }
        }

        [Test]
        public void AlphaIsOpaqueEverywhere()
        {
            byte[] pixels = Pixels();
            for (int i = 3; i < pixels.Length; i += 4) Assert.That(pixels[i], Is.EqualTo(255));
        }

        [Test]
        public void NeutralRamp_IsDarkAndRises()
        {
            // HSV(215, 0.25 - 0.01 i, 0.03 + 0.05 i): the brightest channel is the value.
            for (int i = 0; i < 16; i++)
            {
                float3 c = AtlasPalette.Srgb(i);
                Assert.That(math.cmax(c), Is.EqualTo(0.03f + 0.05f * i).Within(1e-4f));
                Assert.That(c.z, Is.GreaterThanOrEqualTo(c.x), "the ramp is blue-grey");
            }
            Assert.That(math.cmax(AtlasPalette.Srgb(AtlasPalette.FogBase)), Is.LessThan(0.1f));
        }

        [Test]
        public void TheBase_IsDark()
        {
            // OLED: structure cells that fill the view stay dark (Master Plan section 8.1).
            foreach (int index in new[] { AtlasPalette.Floor, AtlasPalette.FloorAlt, AtlasPalette.Wall, AtlasPalette.WallAlt, AtlasPalette.Ceiling, AtlasPalette.Opening })
                Assert.That(math.cmax(AtlasPalette.Srgb(index)), Is.LessThan(0.35f), "cell " + index);
        }

        [Test]
        public void CategoryTints_FollowTheGoldenAngle()
        {
            Assert.That(AtlasPalette.Category(0), Is.EqualTo(32));
            Assert.That(AtlasPalette.Category(31), Is.EqualTo(63));
            Assert.That(AtlasPalette.Category(99), Is.EqualTo(63));
            float3 unknown = AtlasPalette.Srgb(AtlasPalette.Category(0));
            Assert.That(unknown.x, Is.EqualTo(0.45f).Within(1e-5f));
            Assert.That(unknown.y, Is.EqualTo(0.45f).Within(1e-5f));
            float3 expected = AtlasPalette.HsvToRgb(137.5f * 5f % 360f, 0.40f, 0.50f);
            float3 actual = AtlasPalette.Srgb(AtlasPalette.Category(5));
            Assert.That(math.cmax(math.abs(expected - actual)), Is.LessThan(1e-4f));
            // Row 3 continues the series.
            expected = AtlasPalette.HsvToRgb(137.5f * 20f % 360f, 0.40f, 0.50f);
            actual = AtlasPalette.Srgb(AtlasPalette.Category(20));
            Assert.That(math.cmax(math.abs(expected - actual)), Is.LessThan(1e-4f));
        }

        // Cells 60 to 63 (category tints 28 to 31) hold the S3 duck's colours: a 29th object category would get a duck colour
        // as its tint, so it must fail here first.
        [Test]
        public void ObjectCategories_StayBelowTheDuckCells()
        {
            Assert.That(AtlasPalette.CategoryTintCount, Is.EqualTo(28));
            Assert.That(AtlasPalette.Category(AtlasPalette.CategoryTintCount), Is.EqualTo(AtlasPalette.DuckYellow));
            Assert.That(ObjectCategories.Count, Is.LessThanOrEqualTo(AtlasPalette.CategoryTintCount),
                "more object categories than category tints: the duck's cells 60..63 would be read as tints");
            foreach (ObjectCategory c in System.Enum.GetValues(typeof(ObjectCategory)))
            {
                Assert.That((int)c, Is.LessThan(AtlasPalette.CategoryTintCount), c.ToString());
                Assert.That(AtlasPalette.Category(ObjectCategories.TintIndex(c)), Is.LessThan(AtlasPalette.DuckYellow), c.ToString());
            }
        }

        [Test]
        public void ObjectColours_HaveFourValueSteps()
        {
            float[] values = { 0.28f, 0.42f, 0.58f, 0.74f };
            for (int k = 0; k < 4; k++)
            {
                Assert.That(AtlasPalette.Object(0, k), Is.EqualTo(64 + 16 * k));
                for (int column = 0; column < 16; column++)
                    Assert.That(math.cmax(AtlasPalette.Srgb(AtlasPalette.Object(column, k))), Is.EqualTo(values[k]).Within(1e-4f));
            }
            // Column 15 is a neutral grey.
            float3 grey = AtlasPalette.Srgb(AtlasPalette.Object(15, 2));
            Assert.That(grey.x, Is.EqualTo(grey.z).Within(1e-6f));
        }

        [Test]
        public void Regions_StepUpInLightness()
        {
            Assert.That(AtlasPalette.Region(0, 0), Is.EqualTo(128));
            Assert.That(AtlasPalette.Region(3, 3), Is.EqualTo(143));
            Assert.That(AtlasPalette.Region(2, 1), Is.EqualTo(128 + 8 + 1));
            for (int r = 0; r < 4; r++)
                for (int t = 0; t < 4; t++)
                    Assert.That(math.cmax(AtlasPalette.Srgb(AtlasPalette.Region(r, t))), Is.EqualTo(0.22f + 0.12f * t).Within(1e-4f));
        }

        [Test]
        public void Cell4D_WrapsIntoItsBlock()
        {
            Assert.That(AtlasPalette.Cell4D(0), Is.EqualTo(208));
            Assert.That(AtlasPalette.Cell4D(31), Is.EqualTo(239));
            Assert.That(AtlasPalette.Cell4D(32), Is.EqualTo(208));
            Assert.That(AtlasPalette.Cell4D(-1), Is.EqualTo(239));
            Assert.That(AtlasPalette.IsEmissive(AtlasPalette.Cell4D(7)), Is.True);
        }

        [Test]
        public void EmissiveRows_AreTwelveToFifteen()
        {
            Assert.That(AtlasPalette.IsEmissive(191), Is.False);
            Assert.That(AtlasPalette.IsEmissive(192), Is.True);
            Assert.That(AtlasPalette.IsEmissive(255), Is.True);
            Assert.That(AtlasPalette.Uv(191).y, Is.LessThan(AtlasPalette.EmissiveV));
            Assert.That(AtlasPalette.Uv(192).y, Is.GreaterThanOrEqualTo(AtlasPalette.EmissiveV));
            Assert.That(AtlasPalette.StripUv(AtlasPalette.StripDepth, 1f).y, Is.LessThan(AtlasPalette.EmissiveV));
            Assert.That(AtlasPalette.StripUv(AtlasPalette.StripW, 0f).y, Is.GreaterThanOrEqualTo(AtlasPalette.EmissiveV));
        }

        [Test]
        public void Strips_RunContinuouslyAlongU()
        {
            byte[] pixels = Pixels();
            // Warm and cool strips brighten, the depth strip darkens, monotonically texel by texel.
            for (int x = 1; x < 128; x++)
            {
                Assert.That(Rgb(pixels, x, 9 * 8 + 4) >> 16, Is.GreaterThanOrEqualTo(Rgb(pixels, x - 1, 9 * 8 + 4) >> 16));
                Assert.That(Rgb(pixels, x, 10 * 8 + 4) & 255, Is.GreaterThanOrEqualTo(Rgb(pixels, x - 1, 10 * 8 + 4) & 255));
                Assert.That(Rgb(pixels, x, 11 * 8 + 4) & 255, Is.LessThanOrEqualTo(Rgb(pixels, x - 1, 11 * 8 + 4) & 255));
            }
            Assert.That(Rgb(pixels, 0, 15 * 8 + 4), Is.EqualTo(0xFF7F6A));
            Assert.That(Rgb(pixels, 127, 15 * 8 + 4), Is.EqualTo(0x8E6BFF));
            // The ends of a strip are the centres of its first and last texel.
            Assert.That(AtlasPalette.StripUv(9, 0f).x, Is.EqualTo(0.5f / 128f).Within(1e-6f));
            Assert.That(AtlasPalette.StripUv(9, 1f).x, Is.EqualTo(127.5f / 128f).Within(1e-6f));
            Assert.That(AtlasPalette.StripUv(9, 2f).x, Is.EqualTo(127.5f / 128f).Within(1e-6f));
        }

        [Test]
        public void Linear_MatchesTheShaderDefaultOfTheLineColour()
        {
            // _LineColor's default in AtlasSurface.shader is palette GridLine in linear rgb.
            float3 line = AtlasPalette.Linear(AtlasPalette.GridLine);
            Assert.That(line.x, Is.EqualTo(0.159f).Within(1e-3f));
            Assert.That(line.y, Is.EqualTo(0.392f).Within(1e-3f));
            Assert.That(line.z, Is.EqualTo(0.578f).Within(1e-3f));
            float3 white = AtlasPalette.Linear(AtlasPalette.Reticle);
            Assert.That(white.x, Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void HsvToRgb_KnownValues()
        {
            Assert.That(math.cmax(math.abs(AtlasPalette.HsvToRgb(0f, 1f, 1f) - new float3(1f, 0f, 0f))), Is.LessThan(1e-6f));
            Assert.That(math.cmax(math.abs(AtlasPalette.HsvToRgb(120f, 1f, 1f) - new float3(0f, 1f, 0f))), Is.LessThan(1e-6f));
            Assert.That(math.cmax(math.abs(AtlasPalette.HsvToRgb(240f, 1f, 0.5f) - new float3(0f, 0f, 0.5f))), Is.LessThan(1e-6f));
            Assert.That(math.cmax(math.abs(AtlasPalette.HsvToRgb(360f, 1f, 1f) - new float3(1f, 0f, 0f))), Is.LessThan(1e-5f));
            Assert.That(math.cmax(math.abs(AtlasPalette.HsvToRgb(60f, 0f, 0.3f) - new float3(0.3f))), Is.LessThan(1e-6f));
        }

        [Test]
        public void WritePixels_RejectsAShortBuffer()
        {
            Assert.Throws<System.ArgumentException>(() => AtlasPalette.WritePixels(new byte[16]));
        }
    }
}
