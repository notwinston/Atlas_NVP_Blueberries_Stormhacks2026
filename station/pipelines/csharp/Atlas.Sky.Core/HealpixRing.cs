// HEALPix, ring ordering: direction -> pixel index (Gorski et al. 2005). Work package P2-B10.
// Plain C#, System only. A port of ang2pix_ring in station/pipelines/atlas_pipelines/sky.py (whose
// docstring has the formula); both are tested against astropy-healpix.
using System;

namespace Atlas.Sky
{
    public static class HealpixRing
    {
        public static int NPix(int nside) { return 12 * nside * nside; }

        /// <summary>
        /// Pixel index (ring ordering, 0 .. 12 nside^2 - 1) of the direction with polar angle
        /// <paramref name="theta"/> in [0, pi] from +z and azimuth <paramref name="phi"/> (any real, radians).
        /// theta is the HEALPix colatitude and phi its longitude. No allocation.
        /// </summary>
        public static int Ang2PixRing(int nside, double theta, double phi)
        {
            if (nside < 1 || nside > 8192) throw new ArgumentOutOfRangeException("nside");
            if (!(theta >= 0.0 && theta <= Math.PI)) throw new ArgumentOutOfRangeException("theta", "theta must lie in [0, pi]");
            double z = Math.Cos(theta);
            double za = Math.Abs(z);
            double tt = PosMod(phi, 2.0 * Math.PI) / (0.5 * Math.PI);
            if (tt >= 4.0) tt = 0.0; // PosMod can return 2 pi itself for a tiny negative phi

            if (za <= 2.0 / 3.0)
            {
                // equatorial belt
                double t1 = nside * (0.5 + tt);
                double t2 = nside * z * 0.75;
                int jp = (int)Math.Floor(t1 - t2);
                int jm = (int)Math.Floor(t1 + t2);
                int ir = nside + 1 + jp - jm;
                int kshift = 1 - (ir & 1);
                int ip = PosMod(FloorDiv(jp + jm - nside + kshift + 1, 2), 4 * nside);
                return 2 * nside * (nside - 1) + (ir - 1) * 4 * nside + ip;
            }
            else
            {
                // polar caps
                double tp = tt - Math.Floor(tt);
                double tmp = nside * Math.Sin(theta) / Math.Sqrt((1.0 + za) / 3.0);
                int jp = (int)Math.Floor(tp * tmp);
                int jm = (int)Math.Floor((1.0 - tp) * tmp);
                int ir = jp + jm + 1;
                int ip = PosMod((int)Math.Floor(tt * ir), 4 * ir);
                return z > 0.0 ? 2 * ir * (ir - 1) + ip : 12 * nside * nside - 2 * ir * (ir + 1) + ip;
            }
        }

        /// <summary>Pixel index of a direction vector (any non-zero length) in the patient frame of the contract.</summary>
        public static int Vec2PixRing(int nside, double x, double y, double z)
        {
            double theta, phi;
            SkyFrame.DirectionToAngles(x, y, z, out theta, out phi);
            return Ang2PixRing(nside, theta, phi);
        }

        static double PosMod(double a, double m)
        {
            double r = a % m;
            return r < 0.0 ? r + m : r;
        }

        static int PosMod(int a, int m)
        {
            int r = a % m;
            return r < 0 ? r + m : r;
        }

        static int FloorDiv(int a, int b)
        {
            int q = a / b;
            return (a % b != 0 && ((a < 0) != (b < 0))) ? q - 1 : q;
        }
    }
}
