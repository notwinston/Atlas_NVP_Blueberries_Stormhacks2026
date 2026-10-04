namespace Atlas.App
{
    /// <summary>
    /// Where the development overlay sits on the screen, with no engine type so that it runs in headless
    /// tests. The overlay is laid out on a screen 540 units tall and scaled to the real height; its box is
    /// in the top left corner. A touch layer under it asks <see cref="Contains"/> before it takes a
    /// pointer-down as its own, so a press on an overlay button is not also the headset lever.
    /// </summary>
    public static class DevOverlayLayout
    {
        /// <summary>Height of the layout the overlay is drawn in, in its own units.</summary>
        public const float ReferenceHeight = 540f;
        public const float Left = 8f;
        public const float Top = 8f;
        public const float Width = 250f;
        public const float Height = 524f;
        /// <summary>Height of the box that only says "Bump Guard missing".</summary>
        public const float WarningHeight = 64f;

        /// <summary>
        /// True when a point in screen pixels, measured from the bottom left corner as the Input System and
        /// the scan session do, lies inside the overlay's box.
        /// </summary>
        public static bool Contains(float screenX, float screenY, float screenHeight, bool warningOnly)
        {
            if (!(screenHeight > 0f)) return false;
            float scale = screenHeight / ReferenceHeight;
            float x = screenX / scale;
            float fromTop = (screenHeight - screenY) / scale;
            float height = warningOnly ? WarningHeight : Height;
            return x >= Left && x <= Left + Width && fromTop >= Top && fromTop <= Top + height;
        }
    }
}
