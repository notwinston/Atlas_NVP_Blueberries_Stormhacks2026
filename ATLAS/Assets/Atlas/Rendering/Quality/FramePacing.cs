namespace Atlas.Rendering
{
    /// <summary>
    /// The frame rate the app wants (Master Plan section 8.2: 60; 30 on the last ladder rung, section 8.6).
    /// Engine-free: <see cref="AtlasBoot"/> writes it to the engine once per frame.
    /// </summary>
    public static class FramePacing
    {
        public const int DefaultFrameRate = 60;

        static int s_target = DefaultFrameRate;

        /// <summary>Frames per second. 60 or 30; other values are clamped to 15 .. 60.</summary>
        public static int TargetFrameRate
        {
            get => s_target;
            set => s_target = value < 15 ? 15 : (value > 60 ? 60 : value);
        }

        /// <summary>VSync intervals per frame for a frame rate on a 60 Hz display: 1 at 60, 2 at 30.</summary>
        public static int VSyncCount(int frameRate) => frameRate > 30 ? 1 : 2;
    }
}
