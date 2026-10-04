namespace Atlas.Scan
{
    /// <summary>
    /// The rule for the ARKit video format (Master Plan Appendix C.4): of the formats that run at 60 Hz, the
    /// one with the fewest pixels. Tracking does not need more, and every pixel costs heat. Engine-free.
    /// </summary>
    public static class VideoFormat
    {
        public const int RequiredFrameRate = 60;

        /// <summary>
        /// Index of the lowest-resolution 60 Hz entry, or -1 when none runs at 60 Hz. A frame rate of 0 means
        /// "not given". The first of equal entries wins.
        /// </summary>
        public static int ChooseLowest60Hz(int[] widths, int[] heights, int[] frameRates, int count)
        {
            int best = -1;
            long bestPixels = long.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (frameRates[i] != RequiredFrameRate) continue;
                long pixels = (long)widths[i] * heights[i];
                if (pixels <= 0 || pixels >= bestPixels) continue;
                best = i;
                bestPixels = pixels;
            }
            return best;
        }

        /// <summary>"1280x720@60": the text of the start-up log line (gate G1).</summary>
        public static string Describe(int width, int height, int frameRate)
        {
            return width + "x" + height + "@" + frameRate;
        }
    }
}
