using System;

namespace Atlas.Spaces.SmallPlanet
{
    public static class SmallPlanetConstants
    {
        public const double DiscardRad = 2.6;                          // §9.5.6 P1-B08; R6 §2.5
        public const double FogOpaqueRad = 0.8 * Math.PI;              // §9.5.6, §10.1.4
        public const double FogStartRad = 0.5 * Math.PI;               // §10.2 S4 row
        public const double FogBand = FogOpaqueRad - FogStartRad;      // 0.3 pi
        /// <summary>The face of the PlanetKit's "Beacon" tile (tile5): its cairn and lantern are the planet's landmark.</summary>
        public const int BeaconFace = 5;

        /// <summary>
        /// R0 of the PlanetKit tiles (PlanetKit.md, manifest "planet.R0"): the radius, metres, they were modelled at
        /// (PLANET_SCALE 1.5, tile side 3.6 m). The default lower bound of planet.R, so the props keep the proportions
        /// they were authored with. PlanetKitTests checks it against the shipped manifest.
        /// </summary>
        public const double KitRadius = 2.291831;

        // §3.2, §4.4, A.4: R = 2s/pi
        public static double RadiusFromLapSide(double s)
        {
            if (!(s > 0) || double.IsInfinity(s)) throw new ArgumentOutOfRangeException(nameof(s), "lap side must be finite and positive");
            return 2.0 * s / Math.PI;
        }
    }
}
