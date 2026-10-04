using System.Collections.Generic;
using Unity.Mathematics;

namespace Atlas.Kernel
{
    /// <summary>How the kernel is set up for one room and one dial position.</summary>
    public struct KernelConfig
    {
        /// <summary>Tiles per corner: 3, 4, 5 or 6 (the dial of section 3.2). FlatWalkerKernel takes 4;
        /// HolonomyWalkerKernel takes all four.</summary>
        public int Q;
        /// <summary>L for q = 5 and 6, R for q = 3; any positive value for q = 4. Metres per kernel unit.</summary>
        public double MetresPerUnit;
        /// <summary>q = 4 only: the tile side, which is also the centre spacing. Metres. Ignored for other q,
        /// where the spacing follows from Q and MetresPerUnit.</summary>
        public double TileSideMetres;

        /// <summary>Q = 4, the given tile side, MetresPerUnit = KernelConstants.FlatMetresPerUnit.</summary>
        public static KernelConfig Flat(double tileSideMetres)
        {
            return Flat(tileSideMetres, KernelConstants.FlatMetresPerUnit);
        }

        /// <summary>Q = 4 with the given tile side and metres per kernel unit.</summary>
        public static KernelConfig Flat(double tileSideMetres, double metresPerUnit)
        {
            KernelConfig config;
            config.Q = 4;
            config.MetresPerUnit = metresPerUnit;
            config.TileSideMetres = tileSideMetres;
            return config;
        }

        /// <summary>Q = 5 or 6 ({4,5}, {4,6}; kappa = -1) or 3 ({4,3}; kappa = +1) with L (or R) in metres.
        /// TileSideMetres is 0: the spacing follows from Q and MetresPerUnit.</summary>
        public static KernelConfig Curved(int q, double metresPerUnit)
        {
            KernelConfig config;
            config.Q = q;
            config.MetresPerUnit = metresPerUnit;
            config.TileSideMetres = 0.0;
            return config;
        }
    }

    /// <summary>What one call of Step did.</summary>
    public struct KernelStep
    {
        /// <summary>The displacement was ignored: longer than 0.2 m, a head position that is not finite,
        /// or tracking not Normal.</summary>
        public bool Rejected;
        /// <summary>Tile edges crossed in this step: 0 to KernelConstants.MaxCrossingsPerStep.</summary>
        public int Crossings;
        /// <summary>Edge index, in the tile that was left, of the last crossing; -1 when Crossings is 0.</summary>
        public int LastEdge;
        /// <summary>The displacement that was integrated. Metres. Zero when Rejected.</summary>
        public double2 AppliedMetres;
        /// <summary>Crossings > 0.</summary>
        public bool TileChanged { get { return Crossings > 0; } }
    }

    /// <summary>
    /// The walker's place in the tiling, advanced once per frame at step 4 of the frame loop
    /// (Master Plan sections 4.6 and 6.3).
    /// </summary>
    public interface IWalkerKernel
    {
        /// <summary>The tiling in use. Replaced by Configure.</summary>
        ITileGraph Graph { get; }
        /// <summary>Graph.Kappa.</summary>
        double Kappa { get; }
        /// <summary>L (or R): metres per kernel unit.</summary>
        double MetresPerUnit { get; }
        /// <summary>rho: the radius at which the fog is opaque. Kernel units. Default KernelConstants.DefaultCullRho.
        /// Setting it marks Candidates stale; the list is rebuilt inside the next Step.</summary>
        double CullRho { get; set; }

        /// <summary>The walker's pose after the last Step, Reset or Configure.</summary>
        WalkerPose Pose { get; }
        /// <summary>Candidate tiles round the walker's tile, rebuilt only when the tile, CullRho or the graph changes.
        /// Owned by the kernel: read it, never modify it, never keep it across a Configure.</summary>
        List<VisibleTile> Candidates { get; }
        /// <summary>Result of the last Step. Cleared by Reset, ResetToFloor and Configure.</summary>
        KernelStep LastStep { get; }
        /// <summary>Number of Step calls since construction. Never reset.</summary>
        long StepCount { get; }

        /// <summary>New room or new dial position. Builds the graph, then behaves as Reset(WalkerPose.Start).
        /// Not on the frame path. Rule 4: call it only while the walker stands still.</summary>
        void Configure(in KernelConfig config);
        /// <summary>Places the walker. Re-roots if the pose lies outside its tile, then rebuilds Candidates.
        /// Forgets the previous head position: the next Step integrates nothing.</summary>
        void Reset(in WalkerPose start);
        /// <summary>Places the walker at a floor point of the room frame (metres), tile zero centred on the
        /// RoomModel anchor and unturned. This is how a space becomes registered to the room.
        /// The point also becomes the kernel's "previous head position".</summary>
        void ResetToFloor(double2 floorMetres);

        /// <summary>
        /// Step 4 of the frame loop. headFloorMetres is the floor point under the head in the room frame,
        /// from the geometry pose (after the pose filter): (x, z) in (.x, .y). The kernel forms the
        /// displacement since its previous call itself. Exactly one call per frame.
        /// </summary>
        KernelStep Step(double2 headFloorMetres, bool trackingNormal);

        /// <summary>
        /// Step 4 with the frame's time in seconds: what the frame loop calls. The time tells standing from
        /// walking (head speed at most 0.1 m/s), which is the only state a curvature ramp advances in (Master
        /// Plan section 4.6). A flat kernel ignores it. The form without a time integrates the same
        /// displacement and never advances a ramp. Exactly one of the two per frame.
        /// </summary>
        KernelStep Step(double2 headFloorMetres, bool trackingNormal, double timeSeconds);
    }

    /// <summary>
    /// The frame loop's kernel as a curved space drives it: the dial position, the curvature ramp and the
    /// re-aim (Master Plan sections 3.2, 4.6 and 10.1.4). Published in AtlasServices next to IWalkerKernel;
    /// it is the same object. Main thread only.
    /// </summary>
    /// <remarks>
    /// How a space uses it. In Enter: Configure(KernelConfig.Curved(q, L)) for q = 5 or 6, or
    /// Curved(3, R) for the sphere; then, for a start that matches the room, ResetToFloorFlat(head) and
    /// SetCurvatureTarget(Curvature.Hyperbolic, L): the walker stands in tile zero at K = 0 and the ramp runs
    /// while it stands. In Tick: read Pose, Candidates, Still, CurvatureNow. The space never calls Step and
    /// never restores the flat kernel: SpaceLab does that after Exit.
    /// </remarks>
    public interface IDialKernel : IWalkerKernel
    {
        /// <summary>Q of the last Configure: 3, 4, 5 or 6. Graph.Q.</summary>
        int Q { get; }
        /// <summary>L (or R) of the last Configure or SetCurvatureTarget: the dial's target, constant through
        /// a ramp. MetresPerUnit is the value of this frame, which moves while the ramp runs and is 1 at K = 0.</summary>
        double ConfiguredMetresPerUnit { get; }
        /// <summary>The signed curvature of this frame, 1/m^2: 0 flat, -1/L^2 hyperbolic, +1/R^2 on the sphere.</summary>
        double CurvatureNow { get; }
        /// <summary>The curvature the ramp ends at. Equal to CurvatureNow when no ramp is under way.</summary>
        double CurvatureTarget { get; }
        /// <summary>CurvatureNow differs from CurvatureTarget.</summary>
        bool Ramping { get; }
        /// <summary>The last timed Step found the walker standing: tracking Normal, no jump, floor speed at most
        /// 0.1 m/s. False after a Step without a time, a Reset or a Configure.</summary>
        bool Still { get; }
        /// <summary>The last Step moved the curvature. The scale of every anchor changed with it.</summary>
        bool CurvatureChanged { get; }

        /// <summary>
        /// Starts a ramp of four seconds from the curvature of now to kappa / metresPerUnit^2
        /// (kappa = 0: to flat, metresPerUnit ignored). The ramp advances only in a timed Step that finds the
        /// walker standing. False, and nothing changes, when Q is not 5 or 6: only the holonomy rig ramps.
        /// </summary>
        bool SetCurvatureTarget(double kappa, double metresPerUnit);
        /// <summary>
        /// Registers the walker to the room at K = 0 without leaving the configured tiling: tile zero, the frame
        /// a translation by floorMetres in metres, no ramp under way. The point becomes the kernel's previous
        /// head position. At Q = 3 and 4, where there is no flat stand on the tiling, it is ResetToFloor.
        /// While the walker stands like this, ResetToFloor keeps it flat too; once a ramp has begun or ended,
        /// ResetToFloor registers at the configured curvature and ends the ramp.
        /// </summary>
        void ResetToFloorFlat(double2 floorMetres);
        /// <summary>
        /// Turns the walker's frame about the walker: U becomes U * Rot(alpha). The position, the previous head
        /// position and a ramp are untouched (Master Plan section 10.2.3, step 4). False at Q = 4.
        /// </summary>
        bool Reaim(double alpha);
    }
}
