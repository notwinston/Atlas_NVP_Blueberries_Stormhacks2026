using System;
using System.Collections.Generic;

namespace Atlas.Blades.FieldFit.BlindSpotFinder
{
    /// <summary>One stimulus position in the lit-eye frame. Radians in code (Brief §3.1).</summary>
    public readonly struct GridPoint
    {
        public double TemporalDeg { get; }
        public double ElDeg { get; }
        public double AzRad { get; }   // signed, +right: TemporalSign(eye) * TemporalDeg
        public double ElRad { get; }

        public GridPoint(EyeSide eye, double temporalDeg, double elDeg)
        {
            TemporalDeg = temporalDeg;
            ElDeg = elDeg;
            AzRad = BlindSpotGrid.TemporalSign(eye) * temporalDeg * Math.PI / 180.0;
            ElRad = elDeg * Math.PI / 180.0;
        }
    }

    /// <summary>Master Plan §10.4.4 beat 1 grid: 8-22 deg temporal, +-8 deg vertical; Brief §3.2-3.3 (deviation D1).</summary>
    public static class BlindSpotGrid
    {
        public const int TemporalMinDeg = 8, TemporalMaxDeg = 22, ElMinDeg = -8, ElMaxDeg = 8;   // MP §10.4.4
        public const int StepDeg = 2;                                                           // D1
        public const int Columns = 8, Rows = 9, Count = 72;
        public const double PriorityTemporalDeg = 15, PriorityElDeg = 0, PriorityRadiusDeg = 5; // D1
        public const int PriorityCount = 22;

        public static int TemporalSign(EyeSide eye) => eye == EyeSide.Right ? +1 : -1;

        /// <summary>Unit direction in the lit-eye frame (+x right, +y up, +z forward); az signed, + = right.</summary>
        public static UnityEngine.Vector3 Direction(double azRad, double elRad)
        {
            double ce = Math.Cos(elRad);
            return new UnityEngine.Vector3((float)(ce * Math.Sin(azRad)), (float)Math.Sin(elRad), (float)(ce * Math.Cos(azRad)));
        }

        public static UnityEngine.Vector3 Direction(in GridPoint p) => Direction(p.AzRad, p.ElRad);

        /// <summary>Temporal outer loop, elevation inner loop, both ascending.</summary>
        public static List<GridPoint> Build(EyeSide eye)
        {
            var list = new List<GridPoint>(Count);
            for (int t = TemporalMinDeg; t <= TemporalMaxDeg; t += StepDeg)
                for (int e = ElMinDeg; e <= ElMaxDeg; e += StepDeg)
                    list.Add(new GridPoint(eye, t, e));
            return list;
        }

        public static bool InPriorityBand(in GridPoint p) =>
            Math.Sqrt((p.TemporalDeg - PriorityTemporalDeg) * (p.TemporalDeg - PriorityTemporalDeg)
                      + (p.ElDeg - PriorityElDeg) * (p.ElDeg - PriorityElDeg)) <= PriorityRadiusDeg + 1e-9;

        /// <summary>Band 0 (priority) shuffled, then band 1 shuffled; Fisher-Yates on one SplitMix64 (Brief §3.3, D1).</summary>
        public static List<GridPoint> PresentationOrder(EyeSide eye, uint seed)
        {
            var b0 = new List<GridPoint>(PriorityCount);
            var b1 = new List<GridPoint>(Count - PriorityCount);
            foreach (var p in Build(eye)) (InPriorityBand(p) ? b0 : b1).Add(p);
            var rng = new SplitMix64(seed);
            Shuffle(b0, rng);
            Shuffle(b1, rng);
            b0.AddRange(b1);
            return b0;
        }

        static void Shuffle(List<GridPoint> l, SplitMix64 rng)
        {
            for (int i = l.Count - 1; i >= 1; i--)
            {
                int j = (int)(rng.NextULong() % (ulong)(i + 1));
                (l[i], l[j]) = (l[j], l[i]);
            }
        }
    }
}
