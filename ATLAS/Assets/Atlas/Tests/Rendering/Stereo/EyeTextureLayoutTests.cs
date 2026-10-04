using Atlas.World;
using NUnit.Framework;

namespace Atlas.Rendering.Tests.Stereo
{
    /// <summary>Eye-texture sizes and sets (Master Plan sections 8.2 and 8.6).</summary>
    public sealed class EyeTextureLayoutTests
    {
        // Both phones have the same panel: iPhone 15 Pro Max and iPhone 14 Pro Max, 2796 x 1290 in landscape.
        static readonly object[] Phones =
        {
            new object[] { "iPhone 15 Pro Max", 2796, 1290 },
            new object[] { "iPhone 14 Pro Max", 2796, 1290 },
        };

        [TestCaseSource(nameof(Phones))]
        public void HalfScreen_IsHalfTheLongSide(string phone, int width, int height)
        {
            EyeTextureLayout.HalfScreen(width, height, out int halfWidth, out int halfHeight);
            Assert.That(halfWidth, Is.EqualTo(1398), phone);
            Assert.That(halfHeight, Is.EqualTo(1290), phone);

            // The same before the landscape orientation has settled.
            EyeTextureLayout.HalfScreen(height, width, out halfWidth, out halfHeight);
            Assert.That(halfWidth, Is.EqualTo(1398), phone);
            Assert.That(halfHeight, Is.EqualTo(1290), phone);
        }

        [TestCaseSource(nameof(Phones))]
        public void Sizes_ForTheThreeScales(string phone, int width, int height)
        {
            EyeTextureLayout.HalfScreen(width, height, out int halfWidth, out int halfHeight);

            EyeTextureLayout.Size(halfWidth, halfHeight, 0.8f, out int w, out int h);
            Assert.That(w, Is.EqualTo(1118), phone);   // Master Plan section 8.2: 1118 x 1032
            Assert.That(h, Is.EqualTo(1032), phone);

            EyeTextureLayout.Size(halfWidth, halfHeight, 0.7f, out w, out h);
            Assert.That(w, Is.EqualTo(978), phone);
            Assert.That(h, Is.EqualTo(904), phone);

            EyeTextureLayout.Size(halfWidth, halfHeight, 0.6f, out w, out h);
            Assert.That(w, Is.EqualTo(838), phone);
            Assert.That(h, Is.EqualTo(774), phone);
        }

        [Test]
        public void Sizes_AreEven_ForAnyScreen()
        {
            for (int width = 640; width <= 3000; width += 37)
            {
                EyeTextureLayout.HalfScreen(width, width / 2 + 3, out int halfWidth, out int halfHeight);
                for (int set = 0; set < EyeTextureLayout.SetCount; set++)
                {
                    EyeTextureLayout.Size(halfWidth, halfHeight, EyeTextureLayout.SetScale(set), out int w, out int h);
                    Assert.That(w % 2, Is.EqualTo(0));
                    Assert.That(h % 2, Is.EqualTo(0));
                    Assert.That(w, Is.GreaterThan(0));
                    Assert.That(h, Is.GreaterThan(0));
                }
            }
        }

        [Test]
        public void HalfScreen_FallsBackToThePhone_WhenTheSizeIsUnknown()
        {
            EyeTextureLayout.HalfScreen(0, 0, out int halfWidth, out int halfHeight);
            Assert.That(halfWidth, Is.EqualTo(EyeTextureLayout.DefaultHalfWidth));
            Assert.That(halfHeight, Is.EqualTo(EyeTextureLayout.DefaultHalfHeight));
        }

        [Test]
        public void Sets_CarryTheMsaaOfTheirRung()
        {
            Assert.That(EyeTextureLayout.SetCount, Is.EqualTo(4));
            Assert.That(EyeTextureLayout.SetScale(0), Is.EqualTo(0.8f));
            Assert.That(EyeTextureLayout.SetMsaa(0), Is.EqualTo(4));
            Assert.That(EyeTextureLayout.SetScale(1), Is.EqualTo(0.7f));
            Assert.That(EyeTextureLayout.SetMsaa(1), Is.EqualTo(4));
            Assert.That(EyeTextureLayout.SetScale(2), Is.EqualTo(0.6f));
            Assert.That(EyeTextureLayout.SetMsaa(2), Is.EqualTo(2));
            Assert.That(EyeTextureLayout.SetScale(3), Is.EqualTo(0.6f));
            Assert.That(EyeTextureLayout.SetMsaa(3), Is.EqualTo(1));
        }

        [Test]
        public void EveryLadderRung_HasAPreallocatedSet()
        {
            for (int tier = 0; tier < QualityTable.Count; tier++)
            {
                QualityRow row = QualityTable.Row((QualityTier)tier);
                int set = EyeTextureLayout.FindSet(row.RenderScale, row.Msaa);
                Assert.That(set, Is.GreaterThanOrEqualTo(0), "no set for " + row.Tier);
                Assert.That(EyeTextureLayout.SetScale(set), Is.EqualTo(row.RenderScale));
                Assert.That(EyeTextureLayout.SetMsaa(set), Is.EqualTo(row.Msaa));
            }
        }

        [Test]
        public void APairOutsideTheLadder_HasNoPreallocatedSet()
        {
            Assert.That(EyeTextureLayout.FindSet(0.8f, 2), Is.EqualTo(-1));
            Assert.That(EyeTextureLayout.FindSet(0.7f, 1), Is.EqualTo(-1));
        }

        [Test]
        public void Snapping()
        {
            Assert.That(EyeTextureLayout.SnapScale(0.9f), Is.EqualTo(0.8f));
            Assert.That(EyeTextureLayout.SnapScale(0.74f), Is.EqualTo(0.7f));
            Assert.That(EyeTextureLayout.SnapScale(0.1f), Is.EqualTo(0.6f));
            Assert.That(EyeTextureLayout.SnapMsaa(8), Is.EqualTo(4));
            Assert.That(EyeTextureLayout.SnapMsaa(3), Is.EqualTo(2));
            Assert.That(EyeTextureLayout.SnapMsaa(0), Is.EqualTo(1));
        }
    }
}
