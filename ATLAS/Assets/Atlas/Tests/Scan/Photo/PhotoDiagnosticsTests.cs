using NUnit.Framework;

namespace Atlas.Scan.Tests.Photo
{
    /// <summary>The two summary lines of the object photo capture.</summary>
    public class PhotoDiagnosticsTests
    {
        [Test]
        public void KeyFrameSummary_NamesEveryReason()
        {
            string line = PhotoDiagnostics.KeyFrameSummary(12, 5, new[] { 1, 2, 3, 4, 5, 6, 7 }, 9);
            Assert.That(line, Is.EqualTo("object photos: choosing from 12 key frames for 5 objects; refused: not tracking 1, turning fast 2, " +
                "no camera 3, no intrinsics 4, no image 5, format 6, image size 7; ring full dropped 9"));
        }

        [Test]
        public void KeyFrameSummary_NullOrShortArrayCountsAsZeros()
        {
            const string zeros = "object photos: choosing from 0 key frames for 3 objects; refused: not tracking 0, turning fast 0, " +
                "no camera 0, no intrinsics 0, no image 0, format 0, image size 0; ring full dropped 0";
            Assert.That(PhotoDiagnostics.KeyFrameSummary(0, 3, null, 0), Is.EqualTo(zeros));
            Assert.That(PhotoDiagnostics.KeyFrameSummary(0, 3, new int[2], 0), Is.EqualTo(zeros));
        }

        [Test]
        public void ResultSummary_NamesEveryReason()
        {
            string line = PhotoDiagnostics.ResultSummary(2, 3, 1, 1, 40, new[] { 4, 5, 6, 7, 8, 9, 10, 11 }, 2);
            Assert.That(line, Is.EqualTo("object photos: 2 objects sent (1 from partial views, 1 ignoring depth), 3 take the built-in mesh; " +
                "40 views checked: 4 strict candidates, behind camera 5, outside image 6, too small 7, too large 8, too near 9, " +
                "too far 10, occluded 11; JPEG failures 2; key frames released"));
        }

        [Test]
        public void ResultSummary_NullOrShortArrayCountsAsZeros()
        {
            const string zeros = "object photos: 0 objects sent (0 from partial views, 0 ignoring depth), 4 take the built-in mesh; " +
                "0 views checked: 0 strict candidates, behind camera 0, outside image 0, too small 0, too large 0, too near 0, " +
                "too far 0, occluded 0; JPEG failures 0; key frames released";
            Assert.That(PhotoDiagnostics.ResultSummary(0, 4, 0, 0, 0, null, 0), Is.EqualTo(zeros));
            Assert.That(PhotoDiagnostics.ResultSummary(0, 4, 0, 0, 0, new int[3], 0), Is.EqualTo(zeros));
        }
    }
}
