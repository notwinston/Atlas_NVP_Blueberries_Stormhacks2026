using System;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Walking
{
    // MP §4.4: lap square of side s, centred on the anchor, sides along x and z.
    public sealed class LapCorners
    {
        readonly double _h;
        public LapCorners(double s)
        {
            if (!(s > 0)) throw new ArgumentOutOfRangeException(nameof(s), "lap side must be > 0");
            Side = s; _h = s / 2;
        }
        public double Side { get; }

        // h = s/2: K0 = (-h,-h), K1 = (h,-h), K2 = (h,h), K3 = (-h,h) (G6 §3.3); .y is the floor z.
        public double2 Corner(int i)
        {
            switch (i & 3)
            {
                case 0: return new double2(-_h, -_h);
                case 1: return new double2(_h, -_h);
                case 2: return new double2(_h, _h);
                default: return new double2(-_h, _h);
            }
        }

        // Ties go to the lowest index.
        public int Nearest(double2 p)
        {
            int best = 0; double bd = math.lengthsq(p - Corner(0));
            for (int i = 1; i < 4; i++)
            {
                double d = math.lengthsq(p - Corner(i));
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }
    }
}
