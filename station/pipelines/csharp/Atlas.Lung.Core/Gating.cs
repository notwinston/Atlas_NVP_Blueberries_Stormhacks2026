// Lane A engine-free core, P2-A27 (gating flags). Plain C#, System only. Nothing here allocates.
using System;
using Atlas.Cases;

namespace Atlas.Lung.Core
{
    /// <summary>
    /// A gating arc: <c>Length</c> consecutive phase bins starting at <c>Start</c>, wrapping 9 -> 0
    /// (bins Start, Start + 1, ..., Start + Length - 1 mod 10). Its gated volume is
    /// <c>table.json</c> <c>gating_cm3[Start][Length - 1]</c>; duty = Length / 10.
    /// </summary>
    public static class Gating
    {
        public const int Phases = CaseTable.PhaseCount;

        public static void Check(int start, int length)
        {
            if (start < 0 || start >= Phases) throw new ArgumentOutOfRangeException("start", "start bin must be 0..9");
            if (length < 1 || length > Phases) throw new ArgumentOutOfRangeException("length", "arc length must be 1..10");
        }

        /// <summary>Gated volume of the arc in cm3: the table entry.</summary>
        public static double VolumeCm3(CaseTable table, int start, int length)
        {
            Check(start, length);
            return table.GatingCm3[start][length - 1];
        }

        /// <summary>Duty cycle = arc length / 10.</summary>
        public static double Duty(int length) { return length / (double)Phases; }

        /// <summary>The arc of the same length with the smallest volume (lowest start bin on a tie).</summary>
        public static void BestArc(CaseTable table, int length, out int bestStart, out double bestCm3)
        {
            Check(0, length);
            bestStart = 0;
            bestCm3 = table.GatingCm3[0][length - 1];
            for (int s = 1; s < Phases; s++)
            {
                double v = table.GatingCm3[s][length - 1];
                if (v < bestCm3) { bestCm3 = v; bestStart = s; }
            }
        }

        /// <summary>ITV reduction of an arc: the share of the full ITV that gating removes, 1 - V(arc) / ITV.</summary>
        public static double ItvReduction(CaseTable table, int start, int length)
        {
            return 1.0 - VolumeCm3(table, start, length) / table.ItvCm3;
        }

        /// <summary>True when phase bin k is inside the arc (the phases drawn opaque in the gated shadow).</summary>
        public static bool Contains(int start, int length, int bin)
        {
            int d = ((bin - start) % Phases + Phases) % Phases;
            return d < length;
        }

        /// <summary>The phase bin (0..9) of a lane position: floor(10 frac(tau / Lambda)).</summary>
        public static int BinOf(double tau, double lambda)
        {
            int k = (int)Math.Floor(PhaseColour.U(tau, lambda) * Phases);
            return k >= Phases ? Phases - 1 : k;
        }

        /// <summary>
        /// The arc between two flags on the breath lane: from the bin of the first flag forward (in the
        /// direction of time) to the bin of the second, both included. Two flags in one bin give length 1.
        /// </summary>
        public static void ArcFromFlags(double tauFirst, double tauSecond, double lambda, out int start, out int length)
        {
            start = BinOf(tauFirst, lambda);
            int end = BinOf(tauSecond, lambda);
            length = ((end - start) % Phases + Phases) % Phases + 1;
        }
    }
}
