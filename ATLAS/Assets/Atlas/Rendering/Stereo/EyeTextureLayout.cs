using System;

namespace Atlas.Rendering
{
    /// <summary>
    /// Sizes and sample counts of the eye-texture sets (Master Plan sections 8.2 and 8.6). Engine-free.
    /// A set is the pair of eye textures of one ladder rung; its size is the half-screen times the render scale.
    /// </summary>
    public static class EyeTextureLayout
    {
        /// <summary>Half of 2796 px: iPhone 14 Pro Max and 15 Pro Max in landscape.</summary>
        public const int DefaultHalfWidth = 1398;
        public const int DefaultHalfHeight = 1290;

        /// <summary>Sets allocated once at start: 0.8 with 4x, 0.7 with 4x, 0.6 with 2x, 0.6 with no MSAA.</summary>
        public const int SetCount = 4;

        public const float ScaleHigh = 0.8f;
        public const float ScaleMid = 0.7f;
        public const float ScaleLow = 0.6f;

        /// <summary>Render scale of a preallocated set.</summary>
        public static float SetScale(int set)
        {
            switch (set)
            {
                case 0: return ScaleHigh;
                case 1: return ScaleMid;
                case 2: return ScaleLow;
                case 3: return ScaleLow;
                default: throw new ArgumentOutOfRangeException(nameof(set));
            }
        }

        /// <summary>MSAA sample count of a preallocated set: that of the ladder rung that uses it.</summary>
        public static int SetMsaa(int set)
        {
            switch (set)
            {
                case 0: return 4;
                case 1: return 4;
                case 2: return 2;
                case 3: return 1;
                default: throw new ArgumentOutOfRangeException(nameof(set));
            }
        }

        /// <summary>
        /// The half-screen an eye covers, from the screen's pixel size. The longer side is taken as the width,
        /// so a size read before the landscape orientation has settled still gives the right answer. A size
        /// that is not positive gives the default 1398 x 1290.
        /// </summary>
        public static void HalfScreen(int screenWidth, int screenHeight, out int halfWidth, out int halfHeight)
        {
            int longSide = Math.Max(screenWidth, screenHeight);
            int shortSide = Math.Min(screenWidth, screenHeight);
            if (shortSide <= 0 || longSide < 2)
            {
                halfWidth = DefaultHalfWidth;
                halfHeight = DefaultHalfHeight;
                return;
            }
            halfWidth = longSide / 2;
            halfHeight = shortSide;
        }

        /// <summary>Snaps a render scale to 0.6, 0.7 or 0.8.</summary>
        public static float SnapScale(float renderScale)
        {
            if (renderScale < 0.65f) return ScaleLow;
            if (renderScale < 0.75f) return ScaleMid;
            return ScaleHigh;
        }

        /// <summary>Snaps a sample count to 1, 2 or 4.</summary>
        public static int SnapMsaa(int msaa)
        {
            if (msaa >= 4) return 4;
            if (msaa >= 2) return 2;
            return 1;
        }

        /// <summary>One side of an eye texture: the nearest even number to pixels x scale (halves round up).</summary>
        public static int Scaled(int pixels, float renderScale)
        {
            // Whole hundredths keep 1290 x 0.7 at exactly 903 instead of a float just below it.
            int hundredths = (int)Math.Round(renderScale * 100.0);
            int scaled = 2 * ((pixels * hundredths + 100) / 200);
            return Math.Max(scaled, 2);
        }

        /// <summary>Size of an eye texture for a half-screen and a render scale. 1118 x 1032 at 0.8 on both phones.</summary>
        public static void Size(int halfWidth, int halfHeight, float renderScale, out int width, out int height)
        {
            width = Scaled(halfWidth, renderScale);
            height = Scaled(halfHeight, renderScale);
        }

        /// <summary>The preallocated set for a snapped (scale, MSAA) pair, or -1 when no rung uses that pair.</summary>
        public static int FindSet(float renderScale, int msaa)
        {
            float scale = SnapScale(renderScale);
            int samples = SnapMsaa(msaa);
            for (int i = 0; i < SetCount; i++)
            {
                if (SetScale(i) == scale && SetMsaa(i) == samples)
                    return i;
            }
            return -1;
        }
    }
}
