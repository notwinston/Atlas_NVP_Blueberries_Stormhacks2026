using System.Collections.Generic;
using Unity.Mathematics;

namespace Atlas.Kernel
{
    /// <summary>How the kernel is set up for one room and one dial position.</summary>
    public struct KernelConfig
    {
        /// <summary>Tiles per corner: 3, 4, 5 or 6 (the dial of section 3.2). Only 4 in this build.</summary>
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
    }
}
