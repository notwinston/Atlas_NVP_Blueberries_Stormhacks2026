using System;
using Atlas.Rendering;
using Unity.Mathematics;

namespace Atlas.Spaces.SmallPlanet
{
    public static class SmallPlanetConstants
    {
        public const double DiscardRad = 2.6;                          // §9.5.6 P1-B08; R6 §2.5
        public const double FogOpaqueRad = 0.8 * Math.PI;              // §9.5.6, §10.1.4
        public const double FogStartRad = 0.5 * Math.PI;               // §10.2 S4 row
        public const double FogBand = FogOpaqueRad - FogStartRad;      // 0.3 pi
        public const int BeaconFace = 5, BeaconCorner = 0;
        public const float BeaconHeight = 4.0f;                        // R6 §2.6 landmark tower
        public const float BeaconRadius = 0.18f;                       // circumradius of the octagon
        public const int BeaconSides = 8;
        public static readonly float2 BeaconUv = AtlasPalette.Uv(AtlasPalette.Beacon);   // emissive accent of the palette

        // §3.2, §4.4, A.4: R = 2s/pi
        public static double RadiusFromLapSide(double s)
        {
            if (!(s > 0) || double.IsInfinity(s)) throw new ArgumentOutOfRangeException(nameof(s), "lap side must be finite and positive");
            return 2.0 * s / Math.PI;
        }
    }
}
