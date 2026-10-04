using System;
using Atlas.Kernel;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Blades.Playground
{
    /// <summary>
    /// Lane C's port to lane B's curvature dial (P1-B11), shaped like its real API: the dial's kappa, and a ramp of
    /// kappa to zero. The Playground never reaches into the kernel's dial: at integration an adapter over the
    /// real dial binds to this. In the flat build <see cref="FlatCurvatureRamp"/> stands in.
    /// </summary>
    public interface ICurvatureRamp
    {
        /// <summary>Kappa now: +1, 0 or -1 at rest, between while it ramps.</summary>
        double Kappa { get; }
        /// <summary>Starts the ramp of kappa to zero over this many seconds. A flat dial does nothing.</summary>
        void RampToFlat(float seconds);
        /// <summary>Kappa is exactly zero and no ramp is running.</summary>
        bool IsFlat { get; }
    }

    /// <summary>The flat build's ramp: kappa is zero and stays there.</summary>
    public sealed class FlatCurvatureRamp : ICurvatureRamp
    {
        public double Kappa { get { return 0.0; } }
        public void RampToFlat(float seconds) { }
        public bool IsFlat { get { return true; } }
    }

    /// <summary>Where a kernel pose puts the walker on the floor of the room frame, assuming tile zero is the room.</summary>
    public interface IFloorProjection
    {
        /// <summary>False when the pose has no such place (a tile that is not tile zero's neighbour in this tiling).</summary>
        bool TryProject(in WalkerPose pose, out double2 floorMetres, out double yawRadians);
    }

    /// <summary>The flat kernel's projection: the cell's centre plus the offset inside it, in metres. Lane B's
    /// kernel binds its own at integration.</summary>
    public sealed class FlatFloorProjection : IFloorProjection
    {
        readonly IWalkerKernel _kernel;

        public FlatFloorProjection(IWalkerKernel kernel)
        {
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        }

        public bool TryProject(in WalkerPose pose, out double2 floorMetres, out double yawRadians)
        {
            floorMetres = default;
            yawRadians = 0.0;
            if (!FlatTileGraph.TryGetCell(pose.Tile, out long i, out long j)) return false;
            double l = _kernel.MetresPerUnit;
            double a = _kernel.Graph.CentreSpacing;
            double3 p = pose.Local.Position;
            floorMetres = new double2((i * a + p.x) * l, (j * a + p.y) * l);
            yawRadians = Math.Atan2(pose.Local.M10, pose.Local.M00);
            return true;
        }
    }

    /// <summary>How far Home's walls stand from the RoomModel after the walker came back.</summary>
    public static class HomeCheck
    {
        /// <summary>The acceptance bound of P2-C08, metres.</summary>
        public const double WallToleranceMetres = 0.05;

        /// <summary>
        /// Home draws the RoomModel in tile zero, so the kernel places the room on the floor wherever it believes
        /// the walker stands. headFloor is where the head really is (room frame); the pose is where the kernel
        /// puts the walker. The drawn wall corner p lands at R(yaw)(p - head) + head + (kernel - head): the
        /// largest distance from p over the corners of every wall. Zero when the kernel and the head agree.
        /// </summary>
        public static double MaxWallDeviation(RoomModel room, double2 headFloor, double2 kernelFloor, double yaw)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            double2 offset = kernelFloor - headFloor;
            double c = Math.Cos(yaw), s = Math.Sin(yaw);
            double worst = 0.0;
            RoomWall[] walls = room.Walls;
            if (walls.Length == 0) return math.length(offset);
            for (int k = 0; k < walls.Length; k++)
            {
                worst = Math.Max(worst, Deviation(walls[k].A, headFloor, offset, c, s));
                worst = Math.Max(worst, Deviation(walls[k].B, headFloor, offset, c, s));
            }
            return worst;
        }

        static double Deviation(float2 corner, double2 head, double2 offset, double c, double s)
        {
            double2 d = new double2(corner.x, corner.y) - head;
            // Where the corner is drawn, minus where it stands: (R - I)(p - head) + offset.
            double2 moved = new double2(c * d.x - s * d.y - d.x, s * d.x + c * d.y - d.y) + offset;
            return math.length(moved);
        }
    }

    /// <summary>Where the return to Home is.</summary>
    public enum HomeReturnState : byte
    {
        Idle = 0,
        /// <summary>Kappa is ramping to zero.</summary>
        Ramping = 1,
        /// <summary>SpaceLab is switching to Home and resetting the walker.</summary>
        Returning = 2,
        Done = 3,
        Failed = 4
    }

    /// <summary>
    /// Beat 10 and the Return Home twin (Master Plan sections 10.1.2 and 10.1.3, task P2-C08): "Kernel reset to tile
    /// zero, kappa ramps to 0, S0 registered again". The sequence is the ramp of kappa to zero over 4 s (R1 E.3), then
    /// SpaceLab's return to Home (fade, walker put back on the room's floor, Home registered), then a check that
    /// Home's walls stand within 0.05 m of the RoomModel, logged as <c>pg.home</c>. Engine-free; the host and the
    /// ramp are ports. Main thread only.
    /// </summary>
    public sealed class HomeReturn
    {
        /// <summary>kappa ramps over 4 s (Master Plan section 10.1.4).</summary>
        public const float RampSeconds = 4f;
        /// <summary>A SpaceLab that has not answered in this long fails the return, seconds (derived).</summary>
        public const float TimeoutSeconds = 10f;

        readonly ISpaceHost _host;
        readonly ICurvatureRamp _ramp;
        readonly IFloorProjection _projection;
        readonly IEventLog _log;
        float _elapsed;
        float _sinceStage;
        bool _requested;

        public HomeReturn(ISpaceHost host, ICurvatureRamp ramp, IFloorProjection projection, IEventLog log = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _ramp = ramp ?? throw new ArgumentNullException(nameof(ramp));
            _projection = projection ?? throw new ArgumentNullException(nameof(projection));
            _log = log;
        }

        public HomeReturnState State { get; private set; }
        /// <summary>The wall deviation of the last completed return, metres; NaN before one.</summary>
        public double WallDeviationM { get; private set; } = double.NaN;
        /// <summary>The last completed return met the 0.05 m bound.</summary>
        public bool WithinTolerance { get { return !double.IsNaN(WallDeviationM) && WallDeviationM <= HomeCheck.WallToleranceMetres; } }
        public bool Active { get { return State == HomeReturnState.Ramping || State == HomeReturnState.Returning; } }
        /// <summary>Raised once when the return completes (Done) or fails.</summary>
        public event Action<HomeReturnState> Finished;

        /// <summary>The Return Home twin: B held, the home beacon, the triple click.</summary>
        public void OnAction(PgActionEvent e)
        {
            if (e.Action == PgAction.ReturnHome) Begin();
        }

        /// <summary>Starts the sequence. Ignored while one runs.</summary>
        public void Begin()
        {
            if (Active) return;
            _elapsed = 0f;
            _sinceStage = 0f;
            _requested = false;
            if (_ramp.IsFlat)
            {
                State = HomeReturnState.Returning;
                return;
            }
            _ramp.RampToFlat(RampSeconds);
            State = HomeReturnState.Ramping;
        }

        /// <summary>
        /// Once a frame. pose is the kernel's pose now, headFloor the head's floor point in the room frame (metres,
        /// (x, z) in (.x, .y)).
        /// </summary>
        public void Tick(float deltaTime, in WalkerPose pose, double2 headFloor)
        {
            if (!Active) return;
            float dt = deltaTime > 0f ? deltaTime : 0f;
            _elapsed += dt;
            _sinceStage += dt;
            if (_elapsed > TimeoutSeconds + RampSeconds)
            {
                Finish(HomeReturnState.Failed);
                return;
            }

            if (State == HomeReturnState.Ramping)
            {
                if (!_ramp.IsFlat) return;
                State = HomeReturnState.Returning;
                _sinceStage = 0f;
            }

            // Returning: ask once, then wait for Home to be running with the walker reset.
            if (!_requested)
            {
                _requested = _host.ReturnHome();
                return;
            }
            if (_host.State != SpaceHostState.Running || !_host.HasSpace || _host.Current != SpaceId.Home) return;
            Verify(in pose, headFloor);
        }

        void Verify(in WalkerPose pose, double2 headFloor)
        {
            RoomModel room = _host.Room;
            if (room == null || !_projection.TryProject(in pose, out double2 kernelFloor, out double yaw))
            {
                // Nothing to compare against: the return itself worked, the registration could not be measured.
                WallDeviationM = double.NaN;
                Finish(HomeReturnState.Failed);
                return;
            }
            WallDeviationM = HomeCheck.MaxWallDeviation(room, headFloor, kernelFloor, yaw);
            if (_log != null) _log.Log("pg.home", "wall_dev_m", WallDeviationM);
            Finish(WithinTolerance ? HomeReturnState.Done : HomeReturnState.Failed);
        }

        void Finish(HomeReturnState state)
        {
            State = state;
            Action<HomeReturnState> handler = Finished;
            if (handler != null) handler(state);
        }
    }
}
