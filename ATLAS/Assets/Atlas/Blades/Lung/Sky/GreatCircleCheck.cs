using Unity.Mathematics;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>The three arcs and the angle sum of the marked triangle (MP §10.2.2 beat g; P2-B11).</summary>
    public sealed class GreatCircleCheck
    {
        GreatCircleCheck(GreatCircleArc[] arcs, double sum, double area) { Arcs = arcs; AngleSumDeg = sum; AreaSr = area; }

        public static GreatCircleCheck From(double3 a, double3 b, double3 c) => new GreatCircleCheck(
            new[] { new GreatCircleArc(a, b), new GreatCircleArc(b, c), new GreatCircleArc(c, a) },
            SphericalTriangle.AngleSumDeg(a, b, c), SphericalTriangle.AreaSr(a, b, c));

        public GreatCircleArc[] Arcs { get; }   // a->b, b->c, c->a
        public double AngleSumDeg { get; }
        public double AreaSr { get; }
    }
}
