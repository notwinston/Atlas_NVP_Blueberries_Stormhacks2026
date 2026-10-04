using System;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.SmallPlanet
{
    /// <summary>
    /// The logic of S4 Small Planet (Master Plan §3.2, §6.5, §10.1.3; tasks P1-B08 and P1-B09), engine-free:
    /// the radius, the fog, the dial, the 3-corner walk and the anchors. The walker itself is the app's
    /// kernel, configured for q = 3 on Enter; this class only reads its pose.
    /// </summary>
    /// <remarks>
    /// [B09 D2] There is no ramp in S4: the entry is a step change behind the switch fade, and the dial asks for
    /// Curved Walk only while the walker stands. On the Station there is no dial kernel to drive: the pose, the
    /// radius and the fog come from the phone's State (ReadState).
    /// </remarks>
    public sealed class SmallPlanetCore
    {
        public const string ParamFogRho = "planet.fogRho", ParamRadius = "planet.R";
        /// <summary>planet.R is a lower bound on the radius, metres; 0 leaves the radius to the room.</summary>
        public const float MaxRadiusM = 4f;

        readonly LapWalk walk = new LapWalk();
        readonly CurvatureDial dial = new CurvatureDial(3);
        ParamRegistry registry;
        ParamEntry qEntry, strongEntry, fogEntry, radiusEntry;
        RoomModel room;
        IDialKernel kernel;
        double sideM;
        bool standing, armed, requested;
        bool hasRemote;
        WalkerPose remotePose;
        double remoteRadius, remoteFog;

        public bool Configured { get; private set; }
        public bool Entered { get; private set; }
        /// <summary>R, metres: the room's loop-fit radius after the comfort floor (MP §4.4), or planet.R when that is larger.</summary>
        public double R { get; private set; } = double.NaN;
        /// <summary>The walker stands on the sphere this frame. False until a spherical pose has been seen: nothing is drawn.</summary>
        public bool Placed { get; private set; }
        public int Face { get; private set; }
        public Iso2 U { get; private set; } = new Iso2(double3x3.identity, 1);
        /// <summary>F of tile zero (face 0, the room's tile) seen from the walker's face.</summary>
        public Iso2 TileZero { get; private set; } = new Iso2(double3x3.identity, 1);
        public LapWalk Walk => walk;
        public int SelectedQ { get; private set; } = 3;
        /// <summary>The last Tick changed R: the tile mesh and the fog distances are stale.</summary>
        public bool RadiusChanged { get; private set; }
        /// <summary>The last Tick asks for Curved Walk: the dial left 3 while the walker stood. Raised once per entry,
        /// and again after <see cref="SwitchRefused"/>.</summary>
        public bool SwitchWanted { get; private set; }

        // planet.fogRho (§12.2), clamped so that opaque < discard (deviation D8)
        public double FogOpaqueRad { get; private set; } = SmallPlanetConstants.FogOpaqueRad;
        public double FogStartRad => FogOpaqueRad - SmallPlanetConstants.FogBand;

        // beacon chart -> frame of face 5: the corner of face 5 at angle pi/4 + corner*pi/2, at the circumradius (A.3)
        public static Iso2 BeaconPlacement { get; } = Iso2.Boost(
            CubeTiling.Circumradius * new double2(Math.Cos(Math.PI / 4 + SmallPlanetConstants.BeaconCorner * Math.PI / 2),
                                                  Math.Sin(Math.PI / 4 + SmallPlanetConstants.BeaconCorner * Math.PI / 2)), 1);

        /// <summary>
        /// Group "planet": planet.fogRho (the opaque end of the fog, rad) and planet.R. The dial itself, curved.q and
        /// curved.strong, is Curved Walk's too and stays in group "curved"; Enter unfolds that group. Safe to call again.
        /// </summary>
        public static void DeclareParams(ParamRegistry registry)
        {
            string group = SpaceIds.ParamGroup(SpaceId.SmallPlanet);
            registry.DeclareFloat(ParamFogRho, group, (float)SmallPlanetConstants.FogStartRad, (float)SmallPlanetConstants.FogOpaqueRad,
                                  (float)SmallPlanetConstants.FogOpaqueRad);
            registry.DeclareFloat(ParamRadius, group, 0f, MaxRadiusM, 0f, null, true);
            CurvedDialParams.RegisterQ(registry);
            CurvedDialParams.RegisterStrong(registry);
        }

        public static double ClampFog(double opaqueRad)
            => Math.Min(SmallPlanetConstants.FogOpaqueRad, Math.Max(SmallPlanetConstants.FogStartRad, opaqueRad));

        /// <summary>The radius the dial resolves for a room (MP §3.2, §4.4): R3 after the comfort floor unless strong,
        /// and never below planet.R.</summary>
        public static double RadiusFor(RoomModel room, bool strong, double atLeast = 0)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            double r = Math.Max(room.Curvature.ForDial(3, strong), atLeast);
            if (!(r > 0) || double.IsInfinity(r)) throw new ArgumentOutOfRangeException(nameof(room), "radius must be finite and positive");
            return r;
        }

        public void Configure(RoomModel room, ParamRegistry registry)
        {
            if (Entered) throw new InvalidOperationException("Configure while entered");
            if (room == null) throw new ArgumentNullException(nameof(room));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            DeclareParams(registry);
            this.registry = registry;
            this.room = room;
            qEntry = registry.Find(CurvedDialParams.ParamQ);
            strongEntry = registry.Find(CurvedDialParams.ParamStrong);
            fogEntry = registry.Find(ParamFogRho);
            radiusEntry = registry.Find(ParamRadius);
            sideM = room.LapSide;
            standing = room.Standing || !(sideM > 0);
            R = WantedRadius();
            FogOpaqueRad = ClampFog(fogEntry.Float);
            Configured = true;
        }

        /// <summary>
        /// Puts the app's kernel on the sphere: Curved(3, R), the walker on face 0 over the floor point under the
        /// head (registered until the first step, MP §4.9). With no dial kernel (the Station) nothing is driven.
        /// </summary>
        public void Enter(IDialKernel kernel, double2 headFloorMetres)
        {
            if (!Configured) throw new InvalidOperationException("Enter before Configure");
            this.kernel = kernel;
            hasRemote = false;
            Placed = false;
            RadiusChanged = false; SwitchWanted = false; requested = false; armed = false;
            walk.Disarm();
            dial.Set(3); SelectedQ = 3;
            if (kernel != null)
            {
                kernel.Configure(KernelConfig.Curved(3, R));
                kernel.ResetToFloor(headFloorMetres);
                qEntry.Set(3);   // the dial reads 3 while we are here
                Take(kernel.Pose);
            }
            registry.SetGroupActive(CurvedDialParams.Group, true);
            Entered = true;
        }

        /// <summary>The Station's side (ISpaceStateReader): the face, U, R and the fog the phone drew this frame with.</summary>
        public void ReadState(in StateMessage state)
        {
            if (state.SpaceId != (byte)SpaceId.SmallPlanet) return;
            long face = state.Tile0;
            if (face < 0 || face >= CubeTiling.FaceCount || state.P3 != 3f) return;   // not a State of this module
            remotePose.Tile = CubeTiling.FaceId((int)face);
            remotePose.Local = new Iso2(state.Xform0, state.Xform1, state.Xform2, state.Xform3, state.Xform4, state.Xform5,
                                        state.Xform6, state.Xform7, state.Xform8, 1);
            remoteRadius = state.P0;
            remoteFog = state.P5;
            hasRemote = true;
        }

        /// <summary>
        /// Step 5. pose is the kernel's after this frame's step 4 (FrameContext.Walker) and still is the kernel's
        /// verdict on that step. dialLeft and dialRight are the D-pad's two actions of this frame.
        /// </summary>
        public LapEvent Tick(in WalkerPose pose, bool still, bool dialLeft, bool dialRight)
        {
            if (!Entered) throw new InvalidOperationException("Tick before Enter");
            RadiusChanged = false;
            SwitchWanted = false;
            bool driving = kernel != null && !hasRemote;
            if (hasRemote) still = false;   // the Station cannot tell: its lap never advances

            FogOpaqueRad = ClampFog(hasRemote ? remoteFog : fogEntry.Float);
            double want = WantedRadius();
            WalkerPose now = hasRemote ? remotePose : pose;
            // Rule 4: the kernel is configured again only while the walker stands. Same face, same U: the same place on a planet of another size.
            if (want != R && (still || !driving))
            {
                R = want;
                if (driving)
                {
                    kernel.Configure(KernelConfig.Curved(3, R));
                    kernel.Reset(now);
                }
                walk.Disarm(); armed = false;
                RadiusChanged = true;
            }

            if (!Take(now)) return LapEvent.None;

            LapEvent ev = LapEvent.None;
            if (!standing)
            {
                if (!armed) { walk.Arm(3, +1, R, sideM, TileZeroPlacement.LapFlag(sideM, +1, R)); armed = true; }
                ev = walk.Update(TileZero, U, still);
            }

            if (driving)
            {
                dial.Set(Math.Min(CurvatureDial.MaxQ, Math.Max(CurvatureDial.MinQ, qEntry.Int)));
                if (still)
                {
                    if (dialLeft && dial.Prev()) qEntry.Set(dial.Q);
                    if (dialRight && dial.Next()) qEntry.Set(dial.Q);
                }
                SelectedQ = dial.Q;
                if (SelectedQ != 3 && still && !requested) { requested = true; SwitchWanted = true; }
            }
            return ev;
        }

        /// <summary>The dial is this space's to turn: the app's kernel is driven and no remote state replaces it.</summary>
        public bool Driving => kernel != null && !hasRemote;

        /// <summary>The host would not switch: the dial goes back to 3, and may ask again.</summary>
        public void SwitchRefused()
        {
            dial.Set(3); SelectedQ = 3;
            if (qEntry != null) qEntry.Set(3);
            requested = false;
        }

        public void Exit()
        {
            if (Entered && registry != null) registry.SetGroupActive(CurvedDialParams.Group, false);
            Entered = false;
            Placed = false;
            kernel = null;
            hasRemote = false;
            walk.Disarm(); armed = false;
        }

        /// <summary>
        /// kernel.md §4.5: the frame loop has written the face, U and p[0] = R from the kernel. This adds p[3] = q
        /// (always 3 here), p[4] = K = 1/R^2 in 1/m^2 and p[5] = the fog's opaque end in rad.
        /// </summary>
        public void WriteState(ref StateMessage state)
        {
            state.P3 = 3f;
            state.P4 = (float)(1.0 / (R * R));
            state.P5 = (float)FogOpaqueRad;
            state.P6 = 0f;
            state.P7 = 0f;
        }

        // §6.3 step 6; §4.6 anchor[t] = inverse(U) * F[t], in double
        public void ComputeAnchors(Iso2[] tileAnchors, out Iso2 beaconAnchor)
        {
            if (tileAnchors == null || tileAnchors.Length != CubeTiling.FaceCount) throw new ArgumentException("need 6 anchors", nameof(tileAnchors));
            Anchors(Face, U, tileAnchors, out beaconAnchor);
        }

        /// <summary>The same for any pose on the sphere.</summary>
        public static void Anchors(int face, in Iso2 u, Iso2[] tileAnchors, out Iso2 beaconAnchor)
        {
            Iso2 inv = u.Inverse();
            for (int t = 0; t < CubeTiling.FaceCount; t++) tileAnchors[t] = inv * CubeTiling.Chain(face, t);
            beaconAnchor = inv * CubeTiling.Chain(face, SmallPlanetConstants.BeaconFace) * BeaconPlacement;
        }

        double WantedRadius()
        {
            if (hasRemote && remoteRadius > 0) return remoteRadius;
            return RadiusFor(room, strongEntry.Bool, radiusEntry.Float);
        }

        // A pose of another geometry (a flat kernel on the Station before the first State) is not a place on the planet.
        bool Take(in WalkerPose pose)
        {
            int f = CubeTiling.FaceOf(pose.Tile);
            Placed = f >= 0 && pose.Local.Kappa == 1;
            if (!Placed) return false;
            Face = f;
            U = pose.Local;
            TileZero = CubeTiling.Chain(f, 0);
            return true;
        }
    }
}
