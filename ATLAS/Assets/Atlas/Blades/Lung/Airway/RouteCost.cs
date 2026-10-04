using System;
using System.Globalization;
using Unity.Mathematics;

namespace Atlas.Blades.Lung
{
    // Route cost read-out (MP §10.2.3 Aids): insertion depth (sum of len_mm), sharp turns (branch angle > 45 degrees, our definition),
    // narrowest lumen against scope outer diameters 3.5 / 4.2 / 4.0 mm (Spec §11.3.1).
    public readonly struct RouteCost
    {
        public static readonly double[] ScopeOuterDiameterMm = { 3.5, 4.2, 4.0 }; // plan order
        public const double SharpTurnDeg = 45.0;                                  // strict >

        public readonly int Generation;
        public readonly double InsertionDepthMm;
        public readonly int SharpTurns;
        public readonly double NarrowestLumenMm;

        RouteCost(int g, double depth, int sharp, double lumen) { Generation = g; InsertionDepthMm = depth; SharpTurns = sharp; NarrowestLumenMm = lumen; }

        // D-G4-11: a scope fits if lumen >= its outer diameter.
        public bool Fits(int scopeIndex) => NarrowestLumenMm >= ScopeOuterDiameterMm[scopeIndex];

        public static RouteCost At(AirwayTree tree, AirwayTile tile)
        {
            string a = tile.Address;
            double depth = 0, lumen = double.MaxValue; int sharp = 0;
            var parentDir = default(double3);
            for (int len = 0; len <= a.Length; len++)
            {
                var n = tree.Node(a.Substring(0, len));
                bool isTarget = len == a.Length;
                // D-G4-10: on a bend tile the branch itself is half walked.
                depth += isTarget && tile.Kind == AirwayTileKind.Bend ? 0.5 * n.LenMm : n.LenMm;
                lumen = Math.Min(lumen, 2 * n.RadiusMm);
                if (len > 0)
                {
                    double dot = math.clamp(math.dot(parentDir, n.Dir), -1.0, 1.0);
                    if (math.degrees(Math.Acos(dot)) > SharpTurnDeg) sharp++;
                }
                parentDir = n.Dir;
            }
            return new RouteCost(a.Length, depth, sharp, lumen);
        }

        // No device brand names (D-G4-11).
        public string Format()
        {
            var ic = CultureInfo.InvariantCulture;
            var self = this;
            string S(int i) => ScopeOuterDiameterMm[i].ToString("F1", ic) + " mm " + (self.Fits(i) ? "fits" : "too wide");
            return string.Format(ic, "Generation {0} · Insertion depth {1:F0} mm · Sharp turns (>45°) {2} · Narrowest lumen {3:F1} mm · Scope {4} · {5} · {6}",
                Generation, InsertionDepthMm, SharpTurns, NarrowestLumenMm, S(0), S(1), S(2));
        }
    }
}
