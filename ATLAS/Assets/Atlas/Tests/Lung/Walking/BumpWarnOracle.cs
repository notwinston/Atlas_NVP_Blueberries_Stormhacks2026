using System;
using System.Collections.Generic;
using Atlas.Safety;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Walking.Tests
{
    // Test-only reference of the MP §4.10 static-map logic (G6 §4.5; DV-G6-19: the placeholder BumpGuard.Level is always Clear).
    //   v = mean head-floor velocity over the last 0.2 s (12 frames); now = clear(head); path(T) = min clear along head -> head + v T
    //   (every 0.05 m, both ends); STRONG if now < 0.20 or path(0.4) < 0.05; WARN if now < 0.30 or path(0.8) < 0.05;
    //   relax only after now >= threshold + 0.05 and path >= 0.10, held 0.3 s. WARN start = rising edge into WARN or STRONG.
    sealed class BumpWarnOracle
    {
        public const int Clear = 0, Warn = 1, Strong = 2;
        const int Window = 12; const double Dt = 1.0 / 60.0;
        readonly IClearanceQuery _clear; readonly LapCorners _corners;
        readonly List<double2> _hist = new List<double2>();
        double _hold;
        public int Level { get; private set; }
        public int StraightWarnStarts, CornerCues, StrongStarts;
        public double MinClearance = double.PositiveInfinity;

        public BumpWarnOracle(IClearanceQuery clear, LapCorners corners) { _clear = clear; _corners = corners; }

        double Clr(double2 p) => _clear.Clearance(new float2((float)p.x, (float)p.y));

        double Path(double2 head, double2 v, double T)
        {
            double2 end = head + v * T; double len = math.length(end - head);
            int n = Math.Max(1, (int)Math.Ceiling(len / 0.05));
            double best = double.PositiveInfinity;
            for (int i = 0; i <= n; i++) best = Math.Min(best, Clr(head + (end - head) * ((double)i / n)));
            return best;
        }

        public void Sample(double2 head)
        {
            _hist.Add(head); if (_hist.Count > Window + 1) _hist.RemoveAt(0);
            double2 v = _hist.Count > 1 ? (_hist[_hist.Count - 1] - _hist[0]) / ((_hist.Count - 1) * Dt) : double2.zero;
            double now = Clr(head); MinClearance = Math.Min(MinClearance, now);
            double p4 = Path(head, v, 0.4), p8 = Path(head, v, 0.8);
            int raw = (now < 0.20 || p4 < 0.05) ? Strong : (now < 0.30 || p8 < 0.05) ? Warn : Clear;
            int prev = Level;
            if (raw >= Level) { Level = raw; _hold = 0; }
            else
            {
                double th = Level == Strong ? 0.20 : 0.30; double p = Level == Strong ? p4 : p8;
                if (now >= th + 0.05 && p >= 0.10) { _hold += Dt; if (_hold >= 0.3) { Level = raw; _hold = 0; } } else _hold = 0;
            }
            if (Level > prev)   // rising edge into WARN or STRONG
            {
                if (Level == Strong) StrongStarts++;
                double d = math.length(head - _corners.Corner(_corners.Nearest(head)));
                if (d <= 0.5) CornerCues++; else StraightWarnStarts++;   // MP §11.4.5 corner cue
            }
        }
    }
}
