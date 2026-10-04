using System;
using Unity.Mathematics;

namespace Atlas.Kernel
{
    // Master Plan §4.6 holonomy rig; frame order §6.3 step 4. Engine-free, allocation-free per step.
    public sealed class HolonomyRig
    {
        internal const double JumpLimit = 0.2;           // m, §4.6
        internal const double StillSpeed = 0.1;          // m/s, §4.6 / §0.5 rule 4
        internal const double RampSeconds = 4.0;         // §10.1.4
        internal const double CrossingHysteresis = 0.02; // units of L, R3 §A5 (deviation Dv2)
        internal const int MaxCrossingsPerFrame = 4;     // R3 §A5
        internal static readonly double Phi = (1 + Math.Sqrt(5)) / 2;
        internal static readonly double CoshR = Math.Sqrt((1 + Phi) / 2);     // A.3
        internal static readonly double SinhR = Math.Sqrt((Phi - 1) / 2);     // A.3
        internal static readonly double KleinHalfWidth = SinhR / CoshR;       // tanh r = 0.4859 (A.3)
        internal static readonly double SinhHyst = Math.Sinh(CrossingHysteresis);

        readonly TileStep _step;
        readonly TileGraph _graph;   // [B09 D9] non-null: crossings follow the graph's own in-radius (any q)
        TileId _tile;
        Iso2 _u;
        double _k, _kTarget, _kFrom, _tau;
        bool _hasPrev;
        HeadSample _prev;

        public HolonomyRig(TileStep step)
        {
            _step = step ?? throw new ArgumentNullException(nameof(step));
            _tile = TileId.Zero;
            _u = Iso2.Identity;
            LastFrame = long.MinValue;
        }

        // [B09 D9] Crossing through a TileGraph: ExitEdge, StepMatrix and Neighbour of the graph (q = 5 or 6), same loop as the TileStep form.
        public HolonomyRig(TileGraph graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _tile = TileId.Zero;
            _u = Iso2.Identity;
            LastFrame = long.MinValue;
        }

        public WalkerPose Pose => new WalkerPose { Tile = _tile, Local = _u };
        public double Curvature => _k;
        public double TargetCurvature => _kTarget;
        public double Kappa => Sign(_k);
        public double LengthScale => _k == 0 ? double.PositiveInfinity : Scale(_k);
        public long LastFrame { get; private set; }
        public long StepCount { get; private set; }
        public long RejectedCalls { get; private set; }
        public long MissedFrames { get; private set; }
        public long StalePoseFrames { get; private set; }
        public long RejectedJumps { get; private set; }
        public long UntrackedFrames { get; private set; }
        public long GuardExhausted { get; private set; }

        static double Sign(double k) => k < 0 ? -1 : k > 0 ? 1 : 0;
        static double Scale(double k) => k == 0 ? 1.0 : 1.0 / Math.Sqrt(Math.Abs(k));
        static double CurvatureOf(double kappa, double l)
        {
            if (kappa == 0) return 0;
            if (!(l > 0)) throw new ArgumentOutOfRangeException(nameof(l));
            return Sign(kappa) / (l * l);
        }

        public void Reset(in WalkerPose start, double kappa, double lengthScale)
        {
            _k = _kTarget = _kFrom = CurvatureOf(kappa, lengthScale);
            _tau = 0;
            _tile = start.Tile;
            _u = new Iso2(start.Local.M, Sign(_k));
            _hasPrev = false;
        }

        public void SetCurvatureTarget(double kappa, double lengthScale)
        {
            _kTarget = CurvatureOf(kappa, lengthScale);
            _kFrom = _k;
            _tau = 0;
        }

        public HolonomyRigResult Step(long frame, in HeadSample head)
        {
            if (frame <= LastFrame)
            {
                RejectedCalls++;
                return new HolonomyRigResult(frame, false, double2.zero, double.NaN, false, 0, -1, false);
            }
            if (LastFrame != long.MinValue && frame > LastFrame + 1) MissedFrames += frame - LastFrame - 1;
            LastFrame = frame;
            StepCount++;

            double2 d = double2.zero;
            double speed = double.NaN, dt = 0;
            bool still = false;
            if (head.Frame != frame) StalePoseFrames++;
            else
            {
                if (_hasPrev)
                {
                    double2 raw = new double2(head.Position.x - _prev.Position.x, head.Position.z - _prev.Position.z);
                    dt = head.Time - _prev.Time;
                    speed = dt > 0 ? math.length(head.Position - _prev.Position) / dt : double.NaN;
                    bool jump = math.length(raw) > JumpLimit;
                    if (!head.TrackingNormal) UntrackedFrames++;
                    else if (jump) RejectedJumps++;
                    else d = raw;
                    still = head.TrackingNormal && !jump && dt > 0 && speed <= StillSpeed;
                }
                _prev = head;
                _hasPrev = true;
            }

            // §4.6 / A.2: U <- U*Boost(d/L), orthonormalised every frame.
            double kappa = Sign(_k);
            _u = new Iso2(math.mul(_u.M, Iso2.Boost(d / Scale(_k), kappa).M), kappa).Orthonormalised();
            _u = new Iso2(_u.M, kappa);

            bool changed = false;
            if (_k != _kTarget && still)
            {
                _tau += dt;
                double alpha = _tau >= RampSeconds - 1e-9 ? 1 : _tau / RampSeconds;
                double kNew = alpha == 1 ? _kTarget : _kFrom + (_kTarget - _kFrom) * alpha;
                if (kNew != _k) { Rescale(_k, kNew); _k = kNew; changed = true; }
            }

            int crossings = 0, lastEdge = -1;
            kappa = Sign(_k);
            if (kappa < 0)
            {
                for (int n = 0; n < MaxCrossingsPerFrame; n++)
                {
                    int best = ExitEdge();
                    if (best < 0) break;
                    Iso2 g;
                    if (_graph != null) { g = _graph.StepMatrix(_tile, best); _tile = _graph.Neighbour(_tile, best); }   // g before the tile changes
                    else _tile = _step(_tile, best, out g);
                    _u = new Iso2(math.mul(g.Inverse().M, _u.M), -1); // §4.6 U = inverse(G[i])*U
                    crossings++; lastEdge = best;
                }
                if (ExitEdge() >= 0) GuardExhausted++;
            }
            return new HolonomyRigResult(frame, true, d, speed, still, crossings, lastEdge, changed);
        }

        // sigma_k = sinh(signed distance past edge k) (A.3 Klein half-width); edges face k*90 deg (kernel.md §5.3).
        internal static double Sigma(double3 p, int k)
        {
            double ex = k == 0 ? 1 : k == 2 ? -1 : 0, ey = k == 1 ? 1 : k == 3 ? -1 : 0;
            return (p.x * ex + p.y * ey) * CoshR - p.z * SinhR;
        }

        int ExitEdge() => _graph != null ? _graph.ExitEdge(_u) : Exceeds(_u.M.c2);

        static int Exceeds(double3 p)
        {
            int best = -1; double bestSigma = SinhHyst;
            for (int k = 0; k < 4; k++)
            {
                double s = Sigma(p, k);
                if (s > bestSigma) { best = k; bestSigma = s; }
            }
            return best;
        }

        // Deviation Dv3: keep the log-chart position (metres) and heading about the current tile centre.
        void Rescale(double kOld, double kNew)
        {
            double ko = Sign(kOld), so = Scale(kOld), kn = Sign(kNew), sn = Scale(kNew);
            double2 t = Geometry2.Log(_u.M.c2, ko);
            Iso2 rm = Iso2.Boost(t, ko).Inverse() * new Iso2(_u.M, ko);
            double theta = Math.Atan2(rm.M.c0.y, rm.M.c0.x);
            Iso2 n = Iso2.Boost(t * so / sn, kn) * Iso2.Rot(theta);
            _u = new Iso2(n.M, kn);
        }
    }
}
