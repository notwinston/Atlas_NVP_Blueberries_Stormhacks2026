using System;

namespace Atlas.Blades.Playground
{
    /// <summary>Bearings of the pointing probe. Azimuth is in degrees, 0 along +z of the room frame, increasing towards +x (the order of the kernel's floor point, x then z). Engine-free.</summary>
    public static class ProbeGeometry
    {
        /// <summary>Azimuth of the vector (dx, dz), in [0, 360).</summary>
        public static double AzimuthDeg(double dx, double dz)
        {
            return Normalise360(Math.Atan2(dx, dz) * (180.0 / Math.PI));
        }

        /// <summary>Head yaw of a forward vector (fx, fz), the same convention.</summary>
        public static double YawDeg(double forwardX, double forwardZ)
        {
            return AzimuthDeg(forwardX, forwardZ);
        }

        public static double Normalise360(double deg)
        {
            double a = deg % 360.0;
            return a < 0.0 ? a + 360.0 : a;
        }

        /// <summary>Smallest angle between two azimuths, 0 to 180.</summary>
        public static double AngleBetweenDeg(double a, double b)
        {
            double d = Math.Abs(Normalise360(a) - Normalise360(b));
            return d > 180.0 ? 360.0 - d : d;
        }
    }
}
