using System;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Walking.Tests
{
    // G6 §4 SimWalker: 60 Hz, head y = 1.6, straight to the target by min(v dt, remaining); target = NextChamberWorld while
    // Walking, else CurrentChamberWorld (stands on the glyph). Optional lateral offset 0.1 sin(pi u/len) across the segment.
    sealed class SimWalker
    {
        public const double Dt = 1.0 / 60.0;
        public readonly AirwayWing Wing;
        public double Speed = 0.7, Lateral;
        public float3 Head = new float3(0, 1.6f, 0);
        public long Frame;
        bool _walking = true, _haveSeg; double _u, _len; double2 _dirN;

        public SimWalker(AirwayWing wing) { Wing = wing; }
        public double2 HeadXZ => new double2(Head.x, Head.z);

        public void Step()
        {
            bool walking = Wing.Phase == ValvePhase.Walking;
            double2 target = walking ? Wing.NextChamberWorld : Wing.CurrentChamberWorld;
            double2 r = target - HeadXZ; double rem = math.length(r);
            if (!_haveSeg || walking != _walking) { _haveSeg = true; _walking = walking; _u = 0; _len = rem; _dirN = rem > 0 ? new double2(-r.y, r.x) / rem : double2.zero; }
            double2 pos = HeadXZ;
            if (rem > 0)
            {
                double step = Math.Min(Speed * Dt, rem);
                double2 dir = r / rem;
                double offOld = Offset(_u); _u += step; double offNew = Offset(_u);
                pos += dir * step + _dirN * (offNew - offOld);
                if (step >= rem && _u < _len + 1e-12) { /* landed: drop any residual offset only through the sine reaching 0 */ }
            }
            Frame_(pos);
        }

        double Offset(double u) => Lateral == 0 || _len <= 0 ? 0 : Lateral * Math.Sin(Math.PI * Math.Min(u, _len) / _len);

        public void Frame_(double2 pos)
        {
            Head = new float3((float)pos.x, 1.6f, (float)pos.y);
            Frame++;
            var f = new FrameContext(Frame, Frame * Dt, (float)Dt, Head, quaternion.identity, true, false);
            Wing.KernelStep(f);
            Wing.Tick(f);
        }
    }
}
