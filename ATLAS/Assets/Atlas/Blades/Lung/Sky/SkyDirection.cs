using System;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>Sky direction convention (D3): Unity world axes, az = atan2(x, z), el = asin(y); equirectangular, file row 0 at the top (PH-37).</summary>
    public static class SkyDirection
    {
        const double Deg = Math.PI / 180.0;

        public static double3 FromAzEl(double azDeg, double elDeg)
        {
            double az = azDeg * Deg, el = elDeg * Deg, c = Math.Cos(el);
            return new double3(c * Math.Sin(az), Math.Sin(el), c * Math.Cos(az));
        }

        public static void ToAzEl(double3 d, out double azDeg, out double elDeg)
        {
            azDeg = Math.Atan2(d.x, d.z) / Deg;
            elDeg = Math.Asin(Math.Max(-1.0, Math.Min(1.0, d.y))) / Deg;
        }

        /// <summary>Stable angle, MP App. A: atan2(|a x b|, a . b).</summary>
        public static double AngleRad(double3 a, double3 b) => Math.Atan2(math.length(math.cross(a, b)), math.dot(a, b));

        public static void Pixel(double azDeg, double elDeg, out int i, out int j)
        {
            double az = azDeg - 360.0 * Math.Floor((azDeg + 180.0) / 360.0);   // [-180, 180)
            i = Clamp((int)Math.Floor((az + 180.0) / 360.0 * NeedleSkyConstants.Width), NeedleSkyConstants.Width - 1);
            j = Clamp((int)Math.Floor((90.0 - elDeg) / 180.0 * NeedleSkyConstants.Height), NeedleSkyConstants.Height - 1);
        }

        static int Clamp(int v, int max) => v < 0 ? 0 : (v > max ? max : v);
    }
}
