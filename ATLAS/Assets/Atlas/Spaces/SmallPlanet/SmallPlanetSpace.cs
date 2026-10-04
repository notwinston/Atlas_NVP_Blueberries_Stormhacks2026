using System;
using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.Spaces.SmallPlanet
{
    // Seam for ISpaceModule (lane A adapts it, P1-A08). Plain class; no engine types.
    public sealed class SmallPlanetSpace
    {
        double fogOpaque = SmallPlanetConstants.FogOpaqueRad;

        public double R { get; private set; } = double.NaN;
        public bool Configured { get; private set; }
        public bool Entered { get; private set; }
        public bool Registered { get; private set; } // §4.9: true until the first non-zero applied step
        public CubeTileGraph Graph { get; } = new CubeTileGraph();
        public SmallPlanetWalker Walker { get; } = new SmallPlanetWalker();
        public WalkerPose Pose => Walker.Pose;

        // planet.fogRho (§12.2), clamped so that opaque < discard (deviation D8)
        public double FogOpaqueRad
        {
            get => fogOpaque;
            set => fogOpaque = Math.Min(SmallPlanetConstants.FogOpaqueRad, Math.Max(SmallPlanetConstants.FogStartRad, value));
        }
        public double FogStartRad => FogOpaqueRad - SmallPlanetConstants.FogBand;

        // beacon chart -> frame of face 5: the corner of face 5 at angle pi/4 + corner*pi/2, at the circumradius (A.3)
        public static Iso2 BeaconPlacement { get; } = Iso2.Boost(
            CubeTiling.Circumradius * new double2(Math.Cos(Math.PI / 4 + SmallPlanetConstants.BeaconCorner * Math.PI / 2),
                                                  Math.Sin(Math.PI / 4 + SmallPlanetConstants.BeaconCorner * Math.PI / 2)), 1);

        public void Configure(in SmallPlanetRoom room)
        {
            if (Entered) throw new InvalidOperationException("Configure while entered");
            R = SmallPlanetConstants.RadiusFromLapSide(room.LapSide); // §3.2, A.4
            Configured = true;
        }

        // P1-B09 [D16]: the radius as the dial resolves it (R after the comfort floor, MP §4.4), not only 2s/pi.
        public void Configure(double radius)
        {
            if (Entered) throw new InvalidOperationException("Configure while entered");
            if (!(radius > 0) || double.IsInfinity(radius)) throw new ArgumentOutOfRangeException(nameof(radius), "radius must be finite and positive");
            R = radius;
            Configured = true;
        }

        public static WalkerPose StartPose(double2 headXZ, double radius)
            => new WalkerPose { Tile = CubeTiling.FaceId(0), Local = Iso2.Boost(headXZ / radius, 1) };

        public void Enter(in WalkerPose start)
        {
            if (!Configured) throw new InvalidOperationException("Enter before Configure");
            Walker.Reset(start, R);
            Entered = true;
            Registered = true;
        }

        public void Tick(in PlanetHead head)
        {
            if (!Entered) throw new InvalidOperationException("Tick before Enter");
            Walker.Step(head);
            if (Walker.LastApplied.x != 0 || Walker.LastApplied.y != 0) Registered = false;
        }

        public void Exit() { Entered = false; }

        // §6.3 step 6; §4.6 anchor[t] = inverse(U) * F[t], in double
        public void ComputeAnchors(Iso2[] tileAnchors, out Iso2 beaconAnchor)
        {
            if (tileAnchors == null || tileAnchors.Length != CubeTiling.FaceCount) throw new ArgumentException("need 6 anchors", nameof(tileAnchors));
            Iso2 inv = Walker.U.Inverse();
            int f = Walker.Face;
            for (int t = 0; t < CubeTiling.FaceCount; t++) tileAnchors[t] = inv * CubeTiling.Chain(f, t);
            beaconAnchor = inv * CubeTiling.Chain(f, SmallPlanetConstants.BeaconFace) * BeaconPlacement;
        }
    }
}
