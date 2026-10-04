using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Telemetry
{
    /// <summary>
    /// Step 1 of the Spectator's frame (lane D contract, section 5.8): the raw head pose, the tracking state and
    /// the quality tier come from the newest State instead of from sensors. With no State yet the frame stays
    /// at the origin.
    /// </summary>
    public sealed class SpectatorSense : ISenseStep
    {
        readonly SpectatorCore _core;

        public SpectatorSense(SpectatorCore core)
        {
            if (core == null) throw new ArgumentNullException("core");
            _core = core;
        }

        public void Sense(ref FrameContext frame)
        {
            if (!_core.HasState)
            {
                frame.Tracking = TrackingState.Normal;
                return;
            }
            ref readonly StateMessage s = ref _core.State;
            var position = new float3(s.HeadPosX, s.HeadPosY, s.HeadPosZ);
            var rotation = new float4(s.HeadRotX, s.HeadRotY, s.HeadRotZ, s.HeadRotW);
            float norm = math.lengthsq(rotation);
            // A broken sample leaves the frame where it was.
            if (!math.all(math.isfinite(position)) || !(norm > 1e-6f) || float.IsInfinity(norm)) return;
            frame.RawHeadPosition = position;
            frame.RawHeadRotation = new quaternion(rotation / math.sqrt(norm));
            frame.Tracking = TrackingStates.FromWire((byte)s.Tracking);
            frame.Tier = (QualityTier)s.QualityTier;
            frame.PoseJumpRejected = s.PoseJumpRejected;
        }
    }

    /// <summary>
    /// The Spectator's frame (contract README F5; lane D contract, section 5.8): the same steps as the phone's,
    /// with the State in place of the sensors and of the kernel's integration. Step 2 (Bump Guard), the pose
    /// filter and the state sink have nothing to do here and their hooks are accepted and left unused. The
    /// walker is placed from the State on every frame and the kernel is never stepped, so nothing is
    /// integrated. Engine-free: the Station's component calls <see cref="Run"/> and <see cref="RunAfterFrame"/>.
    /// </summary>
    public sealed class SpectatorLoop : IFrameLoop
    {
        public const float MinDeltaTime = 0.004f, MaxDeltaTime = 0.1f;

        readonly List<ISenseStep> _sense = new List<ISenseStep>(2);
        readonly List<IUploadStep> _upload = new List<IUploadStep>(4);
        readonly List<IAfterFrameStep> _after = new List<IAfterFrameStep>(8);
        readonly SpectatorCore _core;
        readonly IWalkerKernel _kernel;
        readonly double[] _xform = new double[9];
        FrameContext _frame;
        int _nextFrameIndex;
        bool _afterPending;

        public SpectatorLoop(SpectatorCore core, IWalkerKernel kernel)
        {
            if (core == null) throw new ArgumentNullException("core");
            if (kernel == null) throw new ArgumentNullException("kernel");
            _core = core;
            _kernel = kernel;
            _frame.RawHeadRotation = quaternion.identity;
            _frame.HeadRotation = quaternion.identity;
            _frame.Tracking = TrackingState.Normal;
            _frame.Walker = kernel.Pose;
        }

        /// <summary>The link's watchdog is not offline; set by the Station before each <see cref="Run"/>.</summary>
        public bool LinkUp { get; set; }
        public int FrameCount { get { return _nextFrameIndex; } }

        // ---- IFrameLoop ----------------------------------------------------------------------------------------

        public ref readonly FrameContext Current { get { return ref _frame; } }

        public void AddSense(ISenseStep step)
        {
            if (step == null) throw new ArgumentNullException("step");
            _sense.Add(step);
        }

        /// <summary>Nothing: the Spectator has no Bump Guard. It shows the phone's level.</summary>
        public void SetGuard(ISafetyStep step) { }

        /// <summary>Nothing: the geometry pose is the raw pose of the State.</summary>
        public void SetPoseFilter(IPoseFilterStep step) { }

        public void AddUpload(IUploadStep step)
        {
            if (step == null) throw new ArgumentNullException("step");
            _upload.Add(step);
        }

        /// <summary>Nothing: a Spectator sends no State.</summary>
        public void SetStateSink(IStateSink sink) { }

        public void AddAfterFrame(IAfterFrameStep step)
        {
            if (step == null) throw new ArgumentNullException("step");
            _after.Add(step);
        }

        public void ResetWalkerToFloor() { PlaceWalker(); }

        public void ResetWalker(in WalkerPose pose)
        {
            _kernel.Reset(in pose);
            _frame.Walker = _kernel.Pose;
        }

        // ---- the frame -----------------------------------------------------------------------------------------

        /// <summary>Steps 1 to 6.</summary>
        public void Run(double time, float deltaTime)
        {
            if (_afterPending) RunAfterFrame();

            _frame.FrameIndex = _nextFrameIndex++;
            _frame.Time = time;
            _frame.DeltaTime = math.clamp(deltaTime, MinDeltaTime, MaxDeltaTime);
            _frame.Tier = QualityTier.Nominal;
            _frame.PoseJumpRejected = false;

            // 1  The State stands in for the sensors.
            for (int i = 0; i < _sense.Count; i++) _sense[i].Sense(ref _frame);
            _frame.LeftEyePosition = _frame.RawHeadPosition;
            _frame.RightEyePosition = _frame.RawHeadPosition;

            // 3  No pose filter: the geometry pose is the raw pose, and no step is formed from it.
            _frame.HeadPosition = _frame.RawHeadPosition;
            _frame.HeadRotation = _frame.RawHeadRotation;
            _frame.FloorDelta = float2.zero;

            // 4  The walker is where the State says.
            PlaceWalker();
            _frame.Kernel = default(KernelStep);
            _frame.Kernel.LastEdge = -1;

            // Room and space follow the phone; then 5 and 6 for the entered module.
            _core.Step(time, LinkUp);
            if (_core.HasState) _core.Host.Tick(in _core.State, in _frame);
            for (int i = 0; i < _upload.Count; i++) _upload[i].Upload(in _frame);
            _afterPending = true;
        }

        /// <summary>Step 8b: the after-frame hooks in order. Once per frame.</summary>
        public void RunAfterFrame()
        {
            if (!_afterPending) return;
            _afterPending = false;
            for (int i = 0; i < _after.Count; i++) _after[i].AfterFrame(in _frame);
        }

        // A curved space sends its tile and tile-local transform, and the kernel takes them as they are. A flat
        // space sends no tile: the walker then stands on the floor point under the head, as on the phone.
        void PlaceWalker()
        {
            bool placed = false;
            if (_core.HasState)
            {
                ref readonly StateMessage s = ref _core.State;
                var tile = new TileId(s.Tile0, s.Tile1, s.Tile2, s.Tile3, s.Tile4, s.Tile5);
                long i, j;
                if (IsCurved(s.SpaceId) && FlatTileGraph.TryGetCell(tile, out i, out j) && _kernel.Graph != null && _kernel.Graph.Q == 4)
                {
                    _xform[0] = s.Xform0; _xform[1] = s.Xform1; _xform[2] = s.Xform2;
                    _xform[3] = s.Xform3; _xform[4] = s.Xform4; _xform[5] = s.Xform5;
                    _xform[6] = s.Xform6; _xform[7] = s.Xform7; _xform[8] = s.Xform8;
                    WalkerPose pose;
                    pose.Tile = tile;
                    pose.Local = Iso2.FromRowMajor(_xform, _kernel.Kappa);
                    if (pose.IsValid)
                    {
                        try
                        {
                            _kernel.Reset(in pose);
                            placed = true;
                        }
                        catch (ArgumentException) { /* a pose the kernel refuses: the floor point below */ }
                    }
                }
            }
            if (!placed)
            {
                float3 head = _frame.HeadPosition;
                bool finite = math.all(math.isfinite(head)) && math.abs(head.x) < 1e6f && math.abs(head.z) < 1e6f;
                _kernel.ResetToFloor(finite ? new double2(head.x, head.z) : double2.zero);
            }
            _frame.Walker = _kernel.Pose;
        }

        static bool IsCurved(byte space)
        {
            return space == (byte)SpaceId.CurvedWalk || space == (byte)SpaceId.AlignedCells || space == (byte)SpaceId.SmallPlanet;
        }
    }
}
