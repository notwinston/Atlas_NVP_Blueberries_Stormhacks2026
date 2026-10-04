namespace Atlas.Scan
{
    /// <summary>
    /// The two summary lines of the object photo capture. Only warnings reach the Hub's phone.log, so these
    /// say why a scan sent the Forge no photos: how many key frames were refused and why, and how many views
    /// were rejected and why. Engine-free.
    /// </summary>
    public static class PhotoDiagnostics
    {
        /// <summary>
        /// The line printed when the capture starts. refused is indexed by KeyFrameRefusal; null counts as all zero.
        /// </summary>
        public static string KeyFrameSummary(int frames, int objects, int[] refused, int evicted)
        {
            return "object photos: choosing from " + frames + " key frames for " + objects + " objects; refused: not tracking " +
                   At(refused, (int)KeyFrameRefusal.NotTracking) + ", turning fast " + At(refused, (int)KeyFrameRefusal.TurningFast) +
                   ", no camera " + At(refused, (int)KeyFrameRefusal.NoCamera) + ", no intrinsics " + At(refused, (int)KeyFrameRefusal.NoIntrinsics) +
                   ", no image " + At(refused, (int)KeyFrameRefusal.NoImage) + ", format " + At(refused, (int)KeyFrameRefusal.Format) +
                   ", image size " + At(refused, (int)KeyFrameRefusal.ImageSize) + "; ring full dropped " + evicted;
        }

        /// <summary>
        /// The line printed when delivery has ended. rejections is indexed by ViewRejection (0 is the strict
        /// candidates accepted); null or a short array counts as zeros.
        /// </summary>
        public static string ResultSummary(int sent, int noPhoto, int partial, int depthIgnored, int viewsChecked, int[] rejections, int encodeFailures)
        {
            return "object photos: " + sent + " objects sent (" + partial + " from partial views, " + depthIgnored + " ignoring depth), " +
                   noPhoto + " take the built-in mesh; " + viewsChecked + " views checked: " + At(rejections, 0) + " strict candidates, behind camera " +
                   At(rejections, 1) + ", outside image " + At(rejections, 2) + ", too small " + At(rejections, 3) + ", too large " +
                   At(rejections, 4) + ", too near " + At(rejections, 5) + ", too far " + At(rejections, 6) + ", occluded " + At(rejections, 7) +
                   "; JPEG failures " + encodeFailures + "; key frames released";
        }

        static int At(int[] counts, int index)
        {
            return counts != null && index < counts.Length ? counts[index] : 0;
        }
    }
}
