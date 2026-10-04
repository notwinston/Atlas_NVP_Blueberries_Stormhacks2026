using System;
using Unity.Mathematics;

namespace Atlas.Kernel
{
    public enum LapEvent : byte { None, Started, Corner, Closed }

    // P1-B09: the 3-4-5 walk tracker (Master Plan §3.3, §4.4, A.4, §10.1.3). Pure; allocation-free after construction.
    // The walk is "walk s, turn left 90 degrees" q times: W = Boost((s / l, 0), kappa) * Rot(pi / 2), Beacon_k = flag * W^k (A.4).
    // Corners are counted by reach (0.3 m) plus stillness [B09 D6].
    public sealed class LapWalk
    {
        public const double ReachRadiusM = 0.3;   // [B09 D6]
        public const int MaxQ = 6;

        readonly Iso2[] beacons = new Iso2[MaxQ + 1];
        double kappa, scale;

        public bool Armed { get; private set; }
        public bool Started { get; private set; }
        public bool Closed { get; private set; }
        public int Q { get; private set; }
        public int Corners { get; private set; }
        public double DistanceToFlagM { get; private set; } = double.NaN;
        public double WorldTurnDeg { get; private set; }

        /// <summary>0 (the flag) until Started, then Corners + 1; -1 when Closed or disarmed.</summary>
        public int NextBeacon => !Armed || Closed ? -1 : (Started ? Corners + 1 : 0);

        /// <summary>k = 0..Q in tile zero's frame; Beacon(0) is the flag.</summary>
        public Iso2 Beacon(int k)
        {
            if (k < 0 || k > Q) throw new ArgumentOutOfRangeException(nameof(k));
            return beacons[k];
        }

        public void Arm(int q, double kappa, double lengthScaleM, double sideM, in Iso2 flagInTileZero)
        {
            if (q < 3 || q > MaxQ) throw new ArgumentOutOfRangeException(nameof(q));
            if (!(sideM > 0) || !(lengthScaleM > 0)) throw new ArgumentOutOfRangeException(nameof(sideM));
            Q = q;
            this.kappa = kappa < 0 ? -1 : kappa > 0 ? 1 : 0;
            scale = this.kappa == 0 ? 1.0 : lengthScaleM;
            Iso2 w = Iso2.Boost(new double2(sideM / scale, 0), this.kappa) * Iso2.Rot(Math.PI / 2);
            beacons[0] = flagInTileZero;
            for (int k = 1; k <= q; k++) beacons[k] = beacons[k - 1] * w;
            Armed = true; Started = false; Closed = false; Corners = 0;
            DistanceToFlagM = double.NaN; WorldTurnDeg = 0;
        }

        public void Disarm()
        {
            Armed = false; Started = false; Closed = false; Corners = 0;
            DistanceToFlagM = double.NaN; WorldTurnDeg = 0;
        }

        /// <summary>tileZeroInCurrent is F of tile zero seen from the walker's tile; walker is U in that tile (kernel.md §4.6).</summary>
        public LapEvent Update(in Iso2 tileZeroInCurrent, in Iso2 walker, bool still)
        {
            if (!Armed) return LapEvent.None;
            Iso2 flag = tileZeroInCurrent * beacons[0];
            double3 p = walker.M.c2;
            DistanceToFlagM = Geometry2.Distance(p, flag.M.c2, kappa) * scale;
            // world turn: rotation of the walker's frame relative to the flag's frame carried along the geodesic (A.2 holonomy)
            Iso2 rel = flag.Inverse() * walker;
            double2 t = Geometry2.Log(rel.M.c2, kappa);
            Iso2 rm = Iso2.Boost(t, kappa).Inverse() * rel;
            double theta = Math.Atan2(rm.M.c0.y, rm.M.c0.x);
            WorldTurnDeg = Wrap(-theta * 180.0 / Math.PI);
            if (Closed) return LapEvent.None;

            if (!Started)
            {
                if (still && DistanceToFlagM <= ReachRadiusM) { Started = true; Corners = 0; return LapEvent.Started; }
                return LapEvent.None;
            }
            Iso2 next = tileZeroInCurrent * beacons[Corners + 1];
            double d = Geometry2.Distance(p, next.M.c2, kappa) * scale;
            if (still && d <= ReachRadiusM)
            {
                Corners++;
                if (Corners == Q) { Closed = true; return LapEvent.Closed; }
                return LapEvent.Corner;
            }
            return LapEvent.None;
        }

        static double Wrap(double deg)
        {
            deg = Math.IEEERemainder(deg, 360.0);   // (-180, 180]
            return deg <= -180.0 ? deg + 360.0 : deg;
        }
    }
}
