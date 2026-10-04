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
    /// kernel takes the tiling, the curvature and the pose of each State and is never stepped, so nothing is
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
        readonly IDialKernel _dial;         // null on a kernel that is flat only
        readonly double[] _xform = new double[9];
        long _placedState = -1;
        int _placedSpace = -1;
        FrameContext _frame;
        int _nextFrameIndex;
        bool _afterPending;

        public SpectatorLoop(SpectatorCore core, IWalkerKernel kernel)
        {
            if (core == null) throw new ArgumentNullException("core");
            if (kernel == null) throw new ArgumentNullException("kernel");
            _core = core;
            _kernel = kernel;
            _dial = kernel as IDialKernel;
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

        public void ResetWalkerToFloor()
        {
            _placedState = -1;
            PlaceWalker();
        }

        public void ResetWalker(in WalkerPose pose)
        {
            _placedState = -1;
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
            int entered = _core.Host.EnterCount;
            _core.Step(time, LinkUp);
            // A module may set the kernel up in its Enter, as it must on the phone: the State places it again.
            if (_core.Host.EnterCount != entered)
            {
                _placedState = -1;
                PlaceWalker();
            }
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

        // The kernel is the phone's, one State late (lane D contract, section 5.8): its tiling, its length scale,
        // its curvature and its pose are set from the State, and nothing is integrated. Curved Walk and Small
        // Planet walk the kernel, so their tile and transform are its pose. Every other space leaves it flat with
        // the walker on the floor point under the head, as on the phone; Aligned Cells is one of them, its tile
        // being the module's own.
        void PlaceWalker()
        {
            bool placed = false;
            if (_core.HasState && _dial != null)
            {
                ref readonly StateMessage s = ref _core.State;
                // rho is the kernel's on the phone, sent in every State (p[2]): the same tiles are candidates here.
                double rho = s.P2;
                if (rho > 0.0 && !double.IsInfinity(rho) && rho != _kernel.CullRho)
                {
                    try { _kernel.CullRho = rho; }
                    catch (ArgumentOutOfRangeException) { /* a rho this kernel does not take: its own stays */ }
                }
                bool walks = s.SpaceId == (byte)SpaceId.CurvedWalk || s.SpaceId == (byte)SpaceId.SmallPlanet;
                if (walks)
                {
                    // A State that was placed already is not placed again: Reset rebuilds the candidate tiles.
                    long seen = _core.StatesReceived;
                    if (seen == _placedState && s.SpaceId == _placedSpace) placed = true;
                    else
                    {
                        try
                        {
                            placed = s.SpaceId == (byte)SpaceId.CurvedWalk ? PlaceOnTiling(in s) : PlaceOnSphere(in s);
                        }
                        catch (ArgumentException) { /* a tile or a frame the kernel refuses */ }
                        catch (OverflowException) { /* a tile beyond the range of the ids */ }
                        catch (InvalidOperationException) { /* q = 6 beyond its precision */ }
                        if (placed)
                        {
                            _placedState = seen;
                            _placedSpace = s.SpaceId;
                        }
                        // A State that cannot be placed leaves the walker on the last good one, on the same tiling.
                        else placed = _placedSpace == s.SpaceId;
                    }
                }
            }
            if (!placed)
            {
                _placedSpace = -1;
                if (_dial != null && _dial.Q != 4) _kernel.Configure(KernelConfig.Flat(FlatSide()));
                float3 head = _frame.HeadPosition;
                bool finite = math.all(math.isfinite(head)) && math.abs(head.x) < 1e6f && math.abs(head.z) < 1e6f;
                _kernel.ResetToFloor(finite ? new double2(head.x, head.z) : double2.zero);
            }
            _frame.Walker = _kernel.Pose;
        }

        // S2 (kernel.md section 4.5; lane B contract): p[0] is the metres per unit of this frame, p[3] the dial,
        // p[4] the signed curvature of this frame in 1/m^2, which is zero while the walker stands in the flat
        // room on tile zero and moves through a ramp.
        bool PlaceOnTiling(in StateMessage s)
        {
            int q = TilingOf(in s);
            ReadXform(in s);
            if (!(s.P4 < 0f))
            {
                // K = 0: tile zero's chart is the room frame, and the frame is a translation in metres.
                var floor = new double2(_xform[2], _xform[5]);
                if (!math.all(math.isfinite(floor)) || math.abs(floor.x) > 1e6 || math.abs(floor.y) > 1e6) return false;
                // The length the ramp will end at is not in a State; the world event names it.
                if (_dial.Q != q) _kernel.Configure(KernelConfig.Curved(q, TargetMetres()));
                _dial.ResetToFloorFlat(floor);
                return true;
            }

            double l = s.P0;
            if (!(l > 0.0) || double.IsInfinity(l)) return false;
            WalkerPose pose;
            pose.Tile = new TileId(s.Tile0, s.Tile1, s.Tile2, s.Tile3, s.Tile4, s.Tile5);
            pose.Local = Iso2.FromRowMajor(_xform, Curvature.Hyperbolic);
            if (!pose.IsValid) return false;
            if (_dial.Q != q) _kernel.Configure(KernelConfig.Curved(q, l));
            // The scale of this frame, which moves through a ramp: Reset places the walker at it.
            if (_dial.ConfiguredMetresPerUnit != l) _dial.SetCurvatureTarget(Curvature.Hyperbolic, l);
            _kernel.Reset(in pose);
            return true;
        }

        // S4: the face is the tile, p[0] the radius, and there is no ramp.
        bool PlaceOnSphere(in StateMessage s)
        {
            double r = s.P0;
            if (!(r > 0.0) || double.IsInfinity(r)) return false;
            ReadXform(in s);
            WalkerPose pose;
            pose.Tile = new TileId(s.Tile0, s.Tile1, s.Tile2, s.Tile3, s.Tile4, s.Tile5);
            pose.Local = Iso2.FromRowMajor(_xform, Curvature.Spherical);
            // Face 0 is six zeros, the words of "no tile" elsewhere: the face is asked for, not the pose's validity.
            if (CubeTiling.FaceOf(pose.Tile) < 0 || !(pose.Local.Defect() < 1e-6)) return false;
            if (_dial.Q != 3 || _dial.ConfiguredMetresPerUnit != r) _kernel.Configure(KernelConfig.Curved(3, r));
            _kernel.Reset(in pose);
            return true;
        }

        // The tiling the phone walks: the one its world event names; without one, the dial of the State; while
        // the dial passes through 3 or 4 on its way out, the tiling it leaves.
        int TilingOf(in StateMessage s)
        {
            int q;
            double metres;
            if (_core.TryGetWorld(SpaceId.CurvedWalk, out q, out metres) && (q == 5 || q == 6)) return q;
            if (s.P3 == 5f || s.P3 == 6f) return (int)s.P3;
            return _dial.Q == 6 ? 6 : 5;
        }

        double TargetMetres()
        {
            int q;
            double metres;
            if (_core.TryGetWorld(SpaceId.CurvedWalk, out q, out metres) && metres > 0.0) return metres;
            return _dial.Q == 5 || _dial.Q == 6 ? _dial.ConfiguredMetresPerUnit : KernelConstants.FlatMetresPerUnit;
        }

        // The flat tiling of the room held here, as SpaceLab configures it on the phone.
        double FlatSide()
        {
            RoomModel room = _core.Room;
            return room != null && room.LapSide > 0f ? room.LapSide : KernelConstants.DefaultFlatTileSideMetres;
        }

        void ReadXform(in StateMessage s)
        {
            _xform[0] = s.Xform0; _xform[1] = s.Xform1; _xform[2] = s.Xform2;
            _xform[3] = s.Xform3; _xform[4] = s.Xform4; _xform[5] = s.Xform5;
            _xform[6] = s.Xform6; _xform[7] = s.Xform7; _xform[8] = s.Xform8;
        }
    }
}
