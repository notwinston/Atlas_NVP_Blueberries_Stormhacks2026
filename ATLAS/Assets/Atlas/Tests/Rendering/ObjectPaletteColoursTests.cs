using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Rendering.Tests
{
    /// <summary>
    /// World's copy of the 64 object colours (docs/plans/standalone-phone/INDEX.md, section 6.1) must equal
    /// Rendering's palette, cell for cell, so that colours chosen off the engine are the colours drawn.
    /// Written from the plan, not the code.
    /// </summary>
    public class ObjectPaletteColoursTests
    {
        // A copied float literal may differ from the computed value in its last bit or so; far below one 8-bit step.
        const float Tolerance = 1e-6f;

        [Test]
        public void Range_IsThePalettesObjectBlock()
        {
            Assert.That(ObjectPaletteColours.First, Is.EqualTo(64));
            Assert.That(ObjectPaletteColours.Count, Is.EqualTo(64));
            Assert.That(ObjectPaletteColours.First, Is.EqualTo(AtlasPalette.ObjectBase));
            Assert.That(ObjectPaletteColours.Count, Is.EqualTo(AtlasPalette.ObjectCount));
        }

        [Test]
        public void EveryObjectCell_EqualsAtlasPalette()
        {
            for (int cell = ObjectPaletteColours.First; cell < ObjectPaletteColours.First + ObjectPaletteColours.Count; cell++)
            {
                float3 world = ObjectPaletteColours.Linear(cell);
                float3 rendering = AtlasPalette.Linear(cell);
                Assert.That(world.x, Is.EqualTo(rendering.x).Within(Tolerance), "cell " + cell + " r");
                Assert.That(world.y, Is.EqualTo(rendering.y).Within(Tolerance), "cell " + cell + " g");
                Assert.That(world.z, Is.EqualTo(rendering.z).Within(Tolerance), "cell " + cell + " b");
            }
        }

        [Test]
        public void EveryObjectCell_IsALinearColourInRange()
        {
            for (int cell = 64; cell < 128; cell++)
            {
                float3 c = ObjectPaletteColours.Linear(cell);
                Assert.That(math.all(c >= 0f) && math.all(c <= 1f), Is.True, "cell " + cell + ": " + c);
            }
        }
    }
}
