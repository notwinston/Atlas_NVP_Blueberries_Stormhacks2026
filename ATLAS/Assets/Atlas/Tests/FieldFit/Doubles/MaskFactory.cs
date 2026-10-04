using UnityEngine;

namespace Atlas.Blades.FieldFit.Tests
{
    // Master Plan §10.4.6, Brief §7.4 / §9 B04: generated masks (512^2, half-width M deg, a pixel is seen iff its centre is within radiusDeg).
    internal static class MaskFactory
    {
        public static Texture2D Disc(int size, double halfWidthDeg, double radiusDeg)
        {
            var t = new Texture2D(size, size, TextureFormat.R8, false, true);
            var px = new Color32[size * size];
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    double x = ((i + 0.5) / size * 2 - 1) * halfWidthDeg, y = ((j + 0.5) / size * 2 - 1) * halfWidthDeg;
                    byte v = (byte)(System.Math.Sqrt(x * x + y * y) <= radiusDeg ? 255 : 0);
                    px[j * size + i] = new Color32(v, v, v, 255);
                }
            t.SetPixels32(px); t.Apply(false, false);
            return t;
        }

        public static Texture2D Solid(int size, bool white, int leftWhiteColumns = -1)
        {
            var t = new Texture2D(size, size, TextureFormat.R8, false, true);
            var px = new Color32[size * size];
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    byte v = (byte)((leftWhiteColumns >= 0 ? i < leftWhiteColumns : white) ? 255 : 0);
                    px[j * size + i] = new Color32(v, v, v, 255);
                }
            t.SetPixels32(px); t.Apply(false, false);
            return t;
        }
    }
}
