using System;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>Great-circle arc From to To (shorter way). D7 keeps picks off antipodes so the arc is defined (brief §11.12).</summary>
    public readonly struct GreatCircleArc
    {
        public readonly double3 From, To, Normal; public readonly double LengthRad;

        public GreatCircleArc(double3 from, double3 to)
        {
            From = from; To = to;
            double3 c = math.cross(from, to);
            double len = math.length(c);
            Normal = len > 1e-15 ? c / len : double3.zero;
            LengthRad = SkyDirection.AngleRad(from, to);
        }

        /// <summary>Angular distance from d to the arc: to the circle inside the arc's lune, else to the nearer endpoint (shader mirror).</summary>
        public double DistanceRad(double3 d)
        {
            bool inLune = math.dot(d, math.cross(Normal, From)) >= 0 && math.dot(d, math.cross(To, Normal)) >= 0;
            if (inLune && !Normal.Equals(double3.zero)) return Math.Asin(Math.Min(1.0, Math.Abs(math.dot(d, Normal))));
            return Math.Min(SkyDirection.AngleRad(d, From), SkyDirection.AngleRad(d, To));
        }
    }
}
