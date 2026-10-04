// Direction frames of the needle sky (docs/case_contract.md sections 2 and 9.1). Work package P2-B10.
//
// The contract's direction frame is the PATIENT frame, LPS millimetre axes, right-handed:
//   +x to the patient's left, +y posterior, +z superior; a direction is a unit vector from the target outwards.
//   theta = polar angle from +z, phi = azimuth from +x towards +y,
//   direction = (sin theta cos phi, sin theta sin phi, cos theta).
// The sky_pick payload uses the same frame: az_deg = phi in degrees [0, 360), el_deg = 90 - theta in degrees
// [-90, 90] (positive towards superior).
//
// Mapping to Unity (informative in case_contract.md section 2, "lane A decides"): unity = (x, z, y), that is
// Unity x = patient left, Unity y (up) = superior, Unity z = posterior. This file only offers the swap
// (PatientToUnity / UnityToPatient). It is NOT confirmed in the Editor: whether the sky sphere's inside faces,
// the head direction and the lung model all agree under that swap has to be checked there (README.md).
using System;

namespace Atlas.Sky
{
    /// <summary>A plain 3-vector of doubles (no Unity types in this assembly).</summary>
    public readonly struct Dir3
    {
        public readonly double X, Y, Z;
        public Dir3(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    public static class SkyFrame
    {
        public const double Deg = 180.0 / Math.PI;
        public const double Rad = Math.PI / 180.0;
        const double TwoPi = 2.0 * Math.PI;

        /// <summary>(theta, phi) of a direction vector of any non-zero length; phi in [0, 2 pi). (0,0,0) gives (0, 0).</summary>
        public static void DirectionToAngles(double x, double y, double z, out double theta, out double phi)
        {
            theta = Math.Atan2(Math.Sqrt(x * x + y * y), z);
            double p = Math.Atan2(y, x);
            if (p < 0.0) p += TwoPi;
            if (p >= TwoPi) p = 0.0;
            phi = p;
        }

        public static Dir3 AnglesToDirection(double theta, double phi)
        {
            double st = Math.Sin(theta);
            return new Dir3(st * Math.Cos(phi), st * Math.Sin(phi), Math.Cos(theta));
        }

        /// <summary>sky_pick angles: az_deg in [0, 360) from +x towards +y, el_deg in [-90, 90] from the xy plane towards +z.</summary>
        public static void DirectionToAzEl(double x, double y, double z, out double azDeg, out double elDeg)
        {
            double theta, phi;
            DirectionToAngles(x, y, z, out theta, out phi);
            azDeg = phi * Deg;
            elDeg = 90.0 - theta * Deg;
        }

        public static Dir3 AzElToDirection(double azDeg, double elDeg)
        {
            return AnglesToDirection((90.0 - elDeg) * Rad, azDeg * Rad);
        }

        /// <summary>Patient (L, P, S) to Unity (x, y, z) = (L, S, P). Needs Editor confirmation, see the file header.</summary>
        public static Dir3 PatientToUnity(double x, double y, double z) { return new Dir3(x, z, y); }

        /// <summary>The inverse of <see cref="PatientToUnity"/> (the same swap).</summary>
        public static Dir3 UnityToPatient(double ux, double uy, double uz) { return new Dir3(ux, uz, uy); }

        // ----- equirectangular image (docs/case_contract.md 9.1), row 0 at +z -----

        /// <summary>The image pixel containing the direction (theta, phi): row = floor(h theta / pi), col = floor(w phi / 2 pi) mod w.</summary>
        public static void AnglesToPixel(double theta, double phi, int width, int height, out int col, out int row)
        {
            int r = (int)Math.Floor(theta / Math.PI * height);
            row = r < 0 ? 0 : (r >= height ? height - 1 : r);
            double p = phi % TwoPi;
            if (p < 0.0) p += TwoPi;
            int c = (int)Math.Floor(p / TwoPi * width);
            col = c >= width ? c - width : (c < 0 ? 0 : c);
        }

        /// <summary>theta = pi (row + 0.5) / h, phi = 2 pi (col + 0.5) / w.</summary>
        public static void PixelCenterAngles(int col, int row, int width, int height, out double theta, out double phi)
        {
            theta = Math.PI * (row + 0.5) / height;
            phi = TwoPi * (col + 0.5) / width;
        }
    }
}
