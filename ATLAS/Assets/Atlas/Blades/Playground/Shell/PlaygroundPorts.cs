using Atlas.Kernel;
using Atlas.Telemetry;

namespace Atlas.Blades.Playground.Shell
{
    // The ports of the Playground shell (Master Plan section 10.1.5: "a change needed inside a space is requested
    // from the lane that owns it"). Lane C reaches lane B's dial, 3-4-5 walk, mode switch and home beacon, and lane
    // D's mini-map, only through these interfaces. Each is shaped like the real lane B or lane D member it stands
    // for, named in its comment, and bound at integration by a small adapter; headless tests bind fakes.

    /// <summary>
    /// The curvature dial. Shaped like lane B's <c>CurvatureDial</c> (P1-B09): q is 3, 4, 5 or 6, and a change
    /// starts the 4 s curvature ramp, which the holonomy rig runs only while the walker stands (Master Plan
    /// sections 4.6 and 10.1.4).
    /// </summary>
    public interface IDialPort
    {
        /// <summary>Tiles per corner now selected, 3 to 6.</summary>
        int Q { get; }
        /// <summary>Selects q. False when it is already selected; ArgumentOutOfRangeException outside 3 to 6.</summary>
        bool Set(int q);
        /// <summary>D-pad right: q + 1, clamped at 6. True when q changed.</summary>
        bool Next();
        /// <summary>D-pad left: q - 1, clamped at 3. True when q changed.</summary>
        bool Prev();
        /// <summary>Headset lever: 3 to 4 to 5 to 6 to 3. True when q changed.</summary>
        bool Cycle();
    }

    /// <summary>What one update of the 3-4-5 walk saw. Same values as lane B's <c>LapEvent</c>.</summary>
    public enum WalkEvent : byte { None = 0, Started = 1, Corner = 2, Closed = 3 }

    /// <summary>
    /// The 3-4-5 walk tracker and its beacons. Shaped like lane B's <c>LapWalk</c> (P1-B09): "walk s, turn left
    /// 90 degrees" q times from the flag, corners counted by reach (0.3 m) plus stillness. The adapter feeds the
    /// kernel's frames to <c>LapWalk.Update</c> and draws the markers with <c>LapMarkerView</c>; this shell only
    /// reads the numbers and arms the walk.
    /// </summary>
    public interface IWalkPort
    {
        bool Armed { get; }
        bool Started { get; }
        bool Closed { get; }
        /// <summary>Corners of the loop, 3 to 6.</summary>
        int Q { get; }
        /// <summary>Corners reached since the start.</summary>
        int Corners { get; }
        /// <summary>Metres from the walker to the flag, along the geodesic. NaN before the first update.</summary>
        double DistanceToFlagM { get; }
        /// <summary>Degrees, (-180, 180]: how far the world has turned relative to the walker (the "world turned
        /// by N" read-out).</summary>
        double WorldTurnDeg { get; }
        /// <summary>0 (the flag) until started, then Corners + 1; -1 when closed or disarmed.</summary>
        int NextBeacon { get; }
        /// <summary>Metres to the next beacon along the geodesic. NaN when there is none. Not a member of
        /// <c>LapWalk</c>: the adapter computes it from <c>Beacon(NextBeacon)</c>, the same way as the distance.</summary>
        double NextBeaconDistanceM { get; }
        /// <summary>Radians, clockwise from the walker's heading as seen from above: where the next beacon lies.
        /// NaN when there is none. Adapter-computed, like <see cref="NextBeaconDistanceM"/>.</summary>
        double NextBeaconBearingRad { get; }

        /// <summary>Arms the walk with the loop side, from the flag at c0 = (-s/2, -s/2). kappa is -1, 0 or +1;
        /// lengthScaleM is R, 1 or L as the dial resolved it.</summary>
        void Arm(int q, double kappa, double lengthScaleM, double sideM);
        void Disarm();
        /// <summary>Advances the tracker with the kernel's frame of this tick. still is the stillness gate.</summary>
        WalkEvent Update(bool still);
    }

    /// <summary>Which of the two ways of living in curved space is shown (Master Plan section 10.1.2, beats 7 and 8).</summary>
    public enum CurvedMode : byte
    {
        /// <summary>Mode 1, Curved Walk (S2): smooth curvature.</summary>
        CurvedWalk = 1,
        /// <summary>Mode 2, Aligned Cells (S3): curvature at the corners.</summary>
        AlignedCells = 2
    }

    /// <summary>
    /// The mode switch between S2 and S3. Shaped like the <c>curved.mode</c> parameter lane B registers
    /// (1 = Mode 1, 2 = Mode 2) and the kernel's walker pose. The switch must keep the walker's tile.
    /// </summary>
    public interface IModePort
    {
        CurvedMode Mode { get; }
        /// <summary>The tile the walker stands on now (<c>IWalkerKernel.Pose.Tile</c>).</summary>
        TileId Tile { get; }
        /// <summary>Asks for a mode. False when it is the mode already shown.</summary>
        bool Request(CurvedMode mode);
    }

    /// <summary>
    /// The home beacon (P1-B06, P1-B10): a marker that points back to tile zero. Both distances are in metres.
    /// </summary>
    public interface IHomeBeaconPort
    {
        /// <summary>Whether the beacon is drawn.</summary>
        bool Enabled { get; set; }
        /// <summary>The distance the beacon shows (its read-out).</summary>
        double ShownDistanceM { get; }
        /// <summary>The distance from the walker to tile zero as the kernel has it (<c>Geometry2.Distance</c>
        /// times L).</summary>
        double KernelDistanceM { get; }
    }

    /// <summary>The flat-twin mini-map of lane D (P1-D07). <c>FlatTwinHud.Visible</c> is the real member.</summary>
    public interface IMiniMapPort
    {
        bool Visible { get; set; }
    }

    /// <summary>Binds <see cref="IMiniMapPort"/> to lane D's <see cref="FlatTwinHud"/>.</summary>
    public sealed class FlatTwinMiniMap : IMiniMapPort
    {
        readonly FlatTwinHud m_Hud;

        public FlatTwinMiniMap(FlatTwinHud hud)
        {
            m_Hud = hud;
        }

        public bool Visible
        {
            get { return m_Hud != null && m_Hud.Visible; }
            set { if (m_Hud != null) m_Hud.Visible = value; }
        }
    }
}
