using NUnit.Framework;

namespace Atlas.Rendering.Tests.Quality
{
    /// <summary>The ladder table must equal Master Plan section 8.6.</summary>
    public sealed class QualityTableTests
    {
        //            tier                    scale  MSAA  rho   copies  fps
        [TestCase(QualityTier.Nominal,  0.8f, 4, 3.0f, 5, 60)]
        [TestCase(QualityTier.Fair,     0.7f, 4, 3.0f, 5, 60)]
        [TestCase(QualityTier.Serious,  0.6f, 2, 2.0f, 3, 60)]
        [TestCase(QualityTier.Critical, 0.6f, 1, 2.0f, 3, 30)]   // MSAA off, locked to 30 fps
        [TestCase(QualityTier.High,     0.8f, 4, 3.5f, 7, 60)]   // "high" tier: rho 3.5, 7 x 7 copies
        public void Row_EqualsThePlan(QualityTier tier, float scale, int msaa, float rho, int copies, int frameRate)
        {
            QualityRow row = QualityTable.Row(tier);
            Assert.That(row.Tier, Is.EqualTo(tier));
            Assert.That(row.RenderScale, Is.EqualTo(scale));
            Assert.That(row.Msaa, Is.EqualTo(msaa));
            Assert.That(row.CullRho, Is.EqualTo(rho));
            Assert.That(row.GluedCopies, Is.EqualTo(copies));
            Assert.That(row.FrameRate, Is.EqualTo(frameRate));
        }

        [Test]
        public void TierValues_AreTheConsoleTiers()
        {
            Assert.That((int)QualityTier.Nominal, Is.EqualTo(0));
            Assert.That((int)QualityTier.Fair, Is.EqualTo(1));
            Assert.That((int)QualityTier.Serious, Is.EqualTo(2));
            Assert.That((int)QualityTier.Critical, Is.EqualTo(3));
            Assert.That((int)QualityTier.High, Is.EqualTo(4));
            Assert.That(QualityTable.Count, Is.EqualTo(5));
        }

        [Test]
        public void Thresholds_AreThePlans()
        {
            Assert.That(LadderController.DwellSeconds, Is.EqualTo(20.0));
            Assert.That(LadderController.GpuToFairMs, Is.EqualTo(9f));
            Assert.That(LadderController.GpuToSeriousMs, Is.EqualTo(12f));
            Assert.That(QualityApplier.ThermalPollSeconds, Is.EqualTo(2.0));
        }

        [Test]
        public void Row_OutsideTheTable_Throws()
        {
            Assert.That(() => QualityTable.Row((QualityTier)5), Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }
    }
}
