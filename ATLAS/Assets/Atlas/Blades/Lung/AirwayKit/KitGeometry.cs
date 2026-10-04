using System;
using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.AirwayKit
{
    // Brief G5 2.1 (D-5). Kit edge 0 is the entry edge (toward the parent); R3 D3: edge 3 is the walker's left, edge 1 the right.
    public enum KitKind { Fork = 0, Straight = 1, BendRight = 2, BendLeft = 3, End = 4 }

    public enum WallRunType { Line, Arc }

    // One run of the wall path (brief 2.3). Line: Fermi arm Edge, Side (+1: u = +w, -1: u = -w), T0..T1 along the arm.
    // Arc: chart radius ChamberRadius from Phi0 to Phi1 counter-clockwise (radians, chart angle).
    public readonly struct WallRun
    {
        public readonly WallRunType Type;
        public readonly int Edge, Side;
        public readonly double T0, T1, Phi0, Phi1;
        public WallRun(WallRunType type, int edge, int side, double t0, double t1, double phi0, double phi1)
        { Type = type; Edge = edge; Side = side; T0 = t0; T1 = t1; Phi0 = phi0; Phi1 = phi1; }
    }

    // Airway corridor and chamber geometry in double precision (brief G5 2.1-2.3). kappa is -1 (scale = L) or 0 (flat cell, scale = g, D-10).
    public static class KitGeometry
    {
        public const double CorridorHalfWidth = 0.4;     // w: MP 10.2.3 "corridor 0.8 m wide"
        public const double KerbHeight = 0.4;            // MP 10.2.3 "0.4 m kerbs" (D-1)
        public const double MembraneTop = 2.6;           // TileChart.PostHeight (D-3)
        public const double ChamberRadius = 0.5;         // c: MP 10.2.3 chamber disc
        public const double GlyphRadius = 0.15;          // MP 10.2.3 standing glyph
        public const double StripHalfWidth = 0.1;        // MP 10.2.3 strip 0.2 m wide
        public const double DecalHeight = 0.005;         // [min] D-8
        public const double ValveShellRadius = 0.49;     // [min] D-6
        public const double ValveCapRadius = 0.5;        // [min]
        public const double ValveFloorHeight = 0.01;     // [min]
        public const double ValveGlyphHeight = 0.015;    // [min]
        public const double MaxKitEdge = 0.25;           // P2-B02 acceptance; MP 8.4
        public const double ColumnStep = 0.14;           // [derived] sqrt(0.14^2 + 0.2^2) = 0.2441 <= 0.25
        public const double RowStep = 0.2;
        public const int Levels = 14;                    // heights 0, 0.2, ..., 2.6
        public const int KerbRows = 2;                   // cell rows 0-1; membrane rows 2..12
        public const int LastRow = 12;
        public const int GlyphSegments = 16, CapSegments = 24, CapRings = 3;   // [derived]
        const double InRadiusUnits = 0.5306375309525181; // acosh(cos(pi/5)/sin(pi/4)), MP A.3

        public static int KitMask(KitKind kind) => kind switch
        {
            KitKind.Fork => 0b1011,
            KitKind.Straight => 0b0101,
            KitKind.BendRight => 0b0011,
            KitKind.BendLeft => 0b1001,
            KitKind.End => 0b0001,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        // Brief 2.1 Classification.
        public static KitKind Classify(int canonicalOpenMask, int canonicalEntryEdge, out int quarterTurns)
        {
            if (canonicalEntryEdge < 0 || canonicalEntryEdge > 3) throw new ArgumentException("entry edge must be 0..3", nameof(canonicalEntryEdge));
            if (canonicalOpenMask < 0 || canonicalOpenMask > 15) throw new ArgumentException("mask must be 0..15", nameof(canonicalOpenMask));
            if ((canonicalOpenMask & (1 << canonicalEntryEdge)) == 0) throw new ArgumentException("entry edge is not in the open mask");
            int kit = 0;
            for (int j = 0; j < 4; j++) if ((canonicalOpenMask & (1 << ((j + canonicalEntryEdge) & 3))) != 0) kit |= 1 << j;
            quarterTurns = canonicalEntryEdge;
            switch (kit)
            {
                case 0b1011: return KitKind.Fork;
                case 0b0101: return KitKind.Straight;
                case 0b0011: return KitKind.BendRight;
                case 0b1001: return KitKind.BendLeft;
                case 0b0001: return KitKind.End;
                default: throw new ArgumentException("open mask is not a kit kind: " + canonicalOpenMask);
            }
        }

        // Kit frame to tile frame: Rot(k pi/2). Anchor = inverse(U) F[t] KitToTile(k).
        public static Iso2 KitToTile(int k) => Iso2.Rot((k & 3) * Math.PI / 2);

        static void Check(int kappa, double scale)
        {
            if (kappa != -1 && kappa != 0) throw new ArgumentOutOfRangeException(nameof(kappa), "kit kappa must be -1 or 0 (D-10)");
            if (kappa == 0 && scale < 1.0) throw new ArgumentOutOfRangeException(nameof(scale), "flat cell g must be at least 1.0 m (D-10)");
            if (kappa == -1 && InRadiusUnits * scale < ChamberRadius - 1e-6) throw new ArgumentOutOfRangeException(nameof(scale), "L too small for the 0.5 m chamber");
        }

        static double Lam(int kappa, double scale) => kappa < 0 ? scale : 1.0;

        // Tile in-radius H in metres (s/2): MP App. A.3.
        public static double InRadiusMetres(int kappa, double scale)
        {
            if (kappa != -1 && kappa != 0) throw new ArgumentOutOfRangeException(nameof(kappa));
            Check(kappa, scale);
            return kappa < 0 ? InRadiusUnits * scale : scale * 0.5;
        }

        public static double CornerT(int kappa, double scale)
        {
            Check(kappa, scale);
            double l = Lam(kappa, scale);
            return kappa < 0 ? l * Math.Asinh(Math.Tanh(CorridorHalfWidth / l)) : CorridorHalfWidth;
        }

        public static double ChamberT(int kappa, double scale)
        {
            Check(kappa, scale);
            double l = Lam(kappa, scale);
            return kappa < 0 ? l * Math.Acosh(Math.Cosh(ChamberRadius / l) / Math.Cosh(CorridorHalfWidth / l))
                             : Math.Sqrt(ChamberRadius * ChamberRadius - CorridorHalfWidth * CorridorHalfWidth);
        }

        // Radians. Chart angle (from the arm axis) of the point (t_i, w) on the chamber circle.
        public static double ChamberAlpha(int kappa, double scale)
        {
            Check(kappa, scale);
            double l = Lam(kappa, scale), ti = ChamberT(kappa, scale);
            return kappa < 0 ? Math.Atan2(Math.Tanh(CorridorHalfWidth / l), Math.Sinh(ti / l)) : Math.Atan2(CorridorHalfWidth, ti);
        }

        static double2 Dir(int e)
        {
            switch (e & 3) { case 0: return new double2(1, 0); case 1: return new double2(0, 1); case 2: return new double2(-1, 0); default: return new double2(0, -1); }
        }

        // Brief 2.2 Fermi coordinates about arm e: t along the axis from the tile centre, u perpendicular (+u toward edge e+1).
        public static double2 FermiToChart(int kitEdge, double t, double u, int kappa, double scale)
        {
            Check(kappa, scale);
            double2 a = Dir(kitEdge), n = new double2(-a.y, a.x);
            if (kappa == 0) return t * a + u * n;
            double L = scale, cu = Math.Cosh(u / L);
            var P = new double3(cu * Math.Sinh(t / L) * a + Math.Sinh(u / L) * n, cu * Math.Cosh(t / L));
            return L * Geometry2.Log(P, -1);
        }

        public static void ChartToFermi(int kitEdge, double2 chart, int kappa, double scale, out double t, out double u)
        {
            Check(kappa, scale);
            double2 a = Dir(kitEdge);
            if (kappa == 0) { t = math.dot(chart, a); u = a.x * chart.y - a.y * chart.x; return; }
            double L = scale;
            double3 P = Geometry2.Exp(chart / L, -1);
            // Rot(-theta_e) P: x' = P . a, y' = P . n
            double qx = P.x * a.x + P.y * a.y, qy = -P.x * a.y + P.y * a.x, qz = P.z;
            u = L * Math.Asinh(qy);
            t = L * Math.Atanh(qx / qz);
        }

        static void Neighbours(int mask, int e, out int plusOpen, out int minusOpen)
        {
            plusOpen = (mask >> ((e + 1) & 3)) & 1; minusOpen = (mask >> ((e + 3) & 3)) & 1;
        }

        // Brief 2.3: wall path = boundary of (union of open arms 0 <= t <= H, |u| <= w) U disc(c) (D-4). Returns the run count.
        public static int WallPath(KitKind kind, int kappa, double scale, WallRun[] into)
        {
            Check(kappa, scale);
            int mask = KitMask(kind), n = 0;
            double H = InRadiusMetres(kappa, scale), tc = CornerT(kappa, scale), ti = ChamberT(kappa, scale), al = ChamberAlpha(kappa, scale);
            if (into == null || into.Length < 7) throw new ArgumentException("into must hold 7 runs");
            for (int e = 0; e < 4; e++)
            {
                if ((mask & (1 << e)) == 0) continue;
                Neighbours(mask, e, out int p, out int m);
                into[n++] = new WallRun(WallRunType.Line, e, +1, p == 1 ? tc : ti, H, 0, 0);
                into[n++] = new WallRun(WallRunType.Line, e, -1, m == 1 ? tc : ti, H, 0, 0);
                int f = e; int g = 0;
                do { f = (f + 1) & 3; g++; } while ((mask & (1 << f)) == 0 && g < 4);
                if (g != 1)
                    into[n++] = new WallRun(WallRunType.Arc, e, 0, 0, 0, e * Math.PI / 2 + al, e * Math.PI / 2 + g * Math.PI / 2 - al);
            }
            return n;
        }

        // Point of a run at parameter s in [0, 1] (chart metres).
        public static double2 RunPoint(in WallRun run, double s, int kappa, double scale)
        {
            if (run.Type == WallRunType.Line)
                return FermiToChart(run.Edge, run.T0 + (run.T1 - run.T0) * s, run.Side * CorridorHalfWidth, kappa, scale);
            double phi = run.Phi0 + (run.Phi1 - run.Phi0) * s;
            return ChamberRadius * new double2(Math.Cos(phi), Math.Sin(phi));
        }

        public static double TrueLength(in WallRun run, int kappa, double scale)
        {
            Check(kappa, scale);
            double l = Lam(kappa, scale);
            if (run.Type == WallRunType.Line) return (kappa < 0 ? Math.Cosh(CorridorHalfWidth / l) : 1.0) * (run.T1 - run.T0);
            return ArcLength(ChamberRadius, run.Phi1 - run.Phi0, kappa, scale);
        }

        // True length of a circular arc of radius r (in the chart, r is the true radius) and angle dphi.
        public static double ArcLength(double r, double dphi, int kappa, double scale)
            => (kappa < 0 ? scale * Math.Sinh(r / scale) : r) * dphi;

        public static int ColumnCount(in WallRun run, int kappa, double scale)
            => Math.Max(1, (int)Math.Ceiling(TrueLength(run, kappa, scale) / ColumnStep - 1e-9));

        // Valve shell columns for a sector at radius ValveShellRadius.
        public static int IrisColumns(double phi0, double phi1, int kappa, double scale)
            => Math.Max(1, (int)Math.Ceiling(ArcLength(ValveShellRadius, phi1 - phi0, kappa, scale) / ColumnStep - 1e-9));

        // Brief 2.3 Iris sectors: [theta_e - beta-, theta_e + beta+], beta = pi/4 beside an open neighbour, else alpha.
        public static void IrisSector(KitKind kind, int kitEdge, int kappa, double scale, out double phi0, out double phi1)
        {
            Check(kappa, scale);
            int mask = KitMask(kind);
            if (kitEdge < 0 || kitEdge > 3 || (mask & (1 << kitEdge)) == 0) throw new ArgumentException("edge is not open in this kind", nameof(kitEdge));
            Neighbours(mask, kitEdge, out int p, out int m);
            double al = ChamberAlpha(kappa, scale), th = kitEdge * Math.PI / 2;
            phi0 = th - (m == 1 ? Math.PI / 4 : al);
            phi1 = th + (p == 1 ? Math.PI / 4 : al);
        }
    }
}
