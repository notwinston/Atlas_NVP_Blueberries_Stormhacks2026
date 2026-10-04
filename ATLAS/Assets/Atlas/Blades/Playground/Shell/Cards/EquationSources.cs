using System;
using System.Collections.Generic;
using Atlas.World;

namespace Atlas.Blades.Playground
{
    /// <summary>
    /// The relations on the cards of S0, S2, S3 and S4 (Master Plan sections 3.2, 3.3 and 10.1.3), written once so
    /// the sources and the tests share them. Pure functions of s, L, R, q and g.
    /// </summary>
    public static class CardMath
    {
        /// <summary>Angle sum of a triangle on the flat floor, degrees.</summary>
        public const double FlatAngleSumDeg = 180.0;
        /// <summary>The octant triangle of Small Planet: three right angles, degrees.</summary>
        public const double PlanetAngleSumDeg = 270.0;
        /// <summary>The number of rooms around one corner of S3 (R1 section D.4): five, so the post is -90 degrees.</summary>
        public const int CellsCornerQ = 5;

        /// <summary>Right-hand side of the loop relation: sqrt(2)*cos(pi/q). Valid for q = 3, 5, 6 (q = 4 gives 1).</summary>
        public static double LoopRhs(int q)
        {
            return Math.Sqrt(2.0) * Math.Cos(Math.PI / q);
        }

        /// <summary>
        /// Left-hand side with the live lengths: cosh(s/2L) for q = 5 and 6, cos(s/2R) for q = 3, and 1 for the flat
        /// q = 4 (where the relation reads s = s). NaN when a length is not positive.
        /// </summary>
        public static double LoopLhs(int q, double s, double lengthScale)
        {
            if (q == 4) return 1.0;
            if (!(s > 0) || !(lengthScale > 0)) return double.NaN;
            double x = s / (2.0 * lengthScale);
            return q == 3 ? Math.Cos(x) : Math.Cosh(x);
        }

        /// <summary>R of Small Planet: 2s/pi.</summary>
        public static double PlanetRadius(double s)
        {
            return 2.0 * s / Math.PI;
        }

        /// <summary>Where straight ahead ends up on Small Planet: one great circle, 2*pi*R = 4s, metres.</summary>
        public static double PlanetHomeDistance(double s)
        {
            return 4.0 * s;
        }

        /// <summary>Curvature at an S3 corner post: 2*pi - q*pi/2, degrees (-90 for q = 5).</summary>
        public static double CornerCurvatureDeg(int q)
        {
            return (2.0 * Math.PI - q * Math.PI / 2.0) * 180.0 / Math.PI;
        }

        /// <summary>Equivalent radius of an S3 cell: g / sqrt(2*pi/q), the "0.89 g" of the card, metres.</summary>
        public static double CellsEffectiveL(double g, int q)
        {
            return g / Math.Sqrt(2.0 * Math.PI / q);
        }
    }

    /// <summary>The cards of S0, S2, S3 and S4 in one registry that lane A adds S1, S5 and S6 to.</summary>
    public static class EquationSources
    {
        static readonly Dictionary<SpaceId, IEquationSource> s_sources = new Dictionary<SpaceId, IEquationSource>();

        /// <summary>Puts a source in the registry, replacing the one of the same space.</summary>
        public static void Register(IEquationSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            s_sources[source.Space] = source;
        }

        public static bool TryGet(SpaceId space, out IEquationSource source)
        {
            return s_sources.TryGetValue(space, out source);
        }

        /// <summary>For a test or a new room: forgets everything.</summary>
        public static void Clear()
        {
            s_sources.Clear();
        }
    }

    /// <summary>S0 Home: the flat loop. A flat space is never called curved, so the card shows what stays true:
    /// four right angles, a lap that closes, a world that does not turn.</summary>
    public sealed class HomeEquationSource : IEquationSource
    {
        readonly IWalkReadout _walk;

        public HomeEquationSource(IWalkReadout walk)
        {
            _walk = walk ?? throw new ArgumentNullException(nameof(walk));
        }

        public SpaceId Space { get { return SpaceId.Home; } }
        public string Template { get { return "4 x 90 deg = 360 deg: the loop closes"; } }
        public int MaxRows { get { return 4; } }

        public int Read(EquationRow[] rows)
        {
            rows[0] = EquationRow.Number("side s", _walk.SideM, "m");
            rows[1] = EquationRow.Number("triangle angle sum", CardMath.FlatAngleSumDeg, "deg");
            rows[2] = EquationRow.Number("from the start flag", _walk.Armed ? _walk.DistanceToFlagM : double.NaN, "m");
            rows[3] = EquationRow.Number("world turned", _walk.Armed ? _walk.WorldTurnDeg : double.NaN, "deg");
            return 4;
        }
    }

    /// <summary>S2 Curved Walk: cosh(s/2L) = sqrt(2)*cos(pi/q) with the live s, L, q, the laps and the world turn.
    /// At q = 4 the dial is flat and the card says so; at q = 3 the relation is the sphere's.</summary>
    public sealed class CurvedWalkEquationSource : IEquationSource
    {
        readonly IWalkReadout _walk;

        public CurvedWalkEquationSource(IWalkReadout walk)
        {
            _walk = walk ?? throw new ArgumentNullException(nameof(walk));
        }

        public SpaceId Space { get { return SpaceId.CurvedWalk; } }

        public string Template
        {
            get
            {
                switch (_walk.Q)
                {
                    case 3: return "cos(s / 2R) = sqrt(2) cos(pi / q)";
                    case 4: return "flat: four right turns close the loop";
                    default: return "cosh(s / 2L) = sqrt(2) cos(pi / q)";
                }
            }
        }

        public int MaxRows { get { return 8; } }

        public int Read(EquationRow[] rows)
        {
            int q = _walk.Q;
            double s = _walk.SideM;
            double l = _walk.LengthScaleM;
            int n = 0;
            rows[n++] = EquationRow.Number("q", q, "");
            rows[n++] = EquationRow.Number("s", s, "m");
            if (q == 4)
            {
                rows[n++] = EquationRow.Words("geometry", "flat");
            }
            else
            {
                rows[n++] = EquationRow.Number(q == 3 ? "R" : "L", l, "m");
                rows[n++] = EquationRow.Number(q == 3 ? "cos(s/2R)" : "cosh(s/2L)", CardMath.LoopLhs(q, s, l), "");
                rows[n++] = EquationRow.Number("sqrt(2) cos(pi/q)", CardMath.LoopRhs(q), "");
            }
            // A lap closes at Q corners (lane B's LapWalk), so four right turns at q = 5 are not yet a lap.
            rows[n++] = EquationRow.Number("laps", _walk.Armed ? _walk.Corners / Math.Max(1, q) : double.NaN, "");
            rows[n++] = EquationRow.Number("world turned", _walk.Armed ? _walk.WorldTurnDeg : double.NaN, "deg");
            rows[n++] = EquationRow.Number("from the start flag", _walk.Armed ? _walk.DistanceToFlagM : double.NaN, "m");
            return n;
        }
    }

    /// <summary>S3 Aligned Cells: the curvature sits at the corners, 2*pi - q*pi/2 = -90 degrees, and each cell
    /// behaves like L = 0.89 g.</summary>
    public sealed class AlignedCellsEquationSource : IEquationSource
    {
        readonly ICellsReadout _cells;

        public AlignedCellsEquationSource(ICellsReadout cells)
        {
            _cells = cells ?? throw new ArgumentNullException(nameof(cells));
        }

        public SpaceId Space { get { return SpaceId.AlignedCells; } }
        public string Template { get { return "corner curvature = 2 pi - q pi / 2"; } }
        public int MaxRows { get { return 5; } }

        public int Read(EquationRow[] rows)
        {
            double g = _cells.CellSideM;
            rows[0] = EquationRow.Number("q", CardMath.CellsCornerQ, "");
            rows[1] = EquationRow.Number("corner curvature", CardMath.CornerCurvatureDeg(CardMath.CellsCornerQ), "deg");
            rows[2] = EquationRow.Number("g", g, "m");
            rows[3] = EquationRow.Number("L_eff = 0.89 g", g > 0 ? CardMath.CellsEffectiveL(g, CardMath.CellsCornerQ) : double.NaN, "m");
            rows[4] = EquationRow.Number("cells crossed", _cells.Crossings, "");
            return 5;
        }
    }

    /// <summary>S4 Small Planet: R = 2s/pi, a triangle of three right angles sums to 270 degrees, and straight
    /// ahead comes home after 2*pi*R = 4s.</summary>
    public sealed class SmallPlanetEquationSource : IEquationSource
    {
        readonly IWalkReadout _walk;

        public SmallPlanetEquationSource(IWalkReadout walk)
        {
            _walk = walk ?? throw new ArgumentNullException(nameof(walk));
        }

        public SpaceId Space { get { return SpaceId.SmallPlanet; } }
        public string Template { get { return "R = 2 s / pi"; } }
        public int MaxRows { get { return 6; } }

        public int Read(EquationRow[] rows)
        {
            double s = _walk.SideM;
            rows[0] = EquationRow.Number("s", s, "m");
            rows[1] = EquationRow.Number("R", s > 0 ? CardMath.PlanetRadius(s) : double.NaN, "m");
            rows[2] = EquationRow.Number("triangle angle sum", CardMath.PlanetAngleSumDeg, "deg");
            rows[3] = EquationRow.Number("straight ahead, home after", s > 0 ? CardMath.PlanetHomeDistance(s) : double.NaN, "m");
            rows[4] = EquationRow.Number("corners", _walk.Armed ? _walk.Corners : double.NaN, "");
            rows[5] = EquationRow.Number("from the start flag", _walk.Armed ? _walk.DistanceToFlagM : double.NaN, "m");
            return 6;
        }
    }
}
