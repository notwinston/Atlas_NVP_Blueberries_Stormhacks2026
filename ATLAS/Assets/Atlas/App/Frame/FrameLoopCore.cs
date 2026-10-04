using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.App
{
    /// <summary>The space host as the frame loop drives it: steps 5, 6 and 8a of the frame.</summary>
    public interface ISpaceFrame
    {
        /// <summary>Step 5: the entered module's Tick; nothing when no space is entered.</summary>
        void TickSpace(in FrameContext frame);
        /// <summary>Step 6: the entered module's own upload, before the registered hooks.</summary>
        void UploadSpace(in FrameContext frame);
        /// <summary>Step 8a: the entered module's part of the State.</summary>
        void WriteState(ref StateMessage state);
    }

    /// <summary>The walker resets the space host needs from the frame loop.</summary>
    public interface IWalkerReset
    {
        /// <summary>kernel.ResetToFloor at the geometry head's floor point of the frame in progress.</summary>
        void ResetWalkerToFloor();
        /// <summary>
        /// A room was made active: the walker is reset straight after step 3 of the first frame whose head
        /// pose is in the room frame, before that frame's kernel step.
        /// </summary>
        void LatchRoomReset();
        /// <summary>True from LatchRoomReset until that reset has been applied.</summary>
        bool RoomResetPending { get; }
    }

    /// <summary>A frame hook that also owns fields of the State (Bump Guard: level and cue flag).</summary>
    public interface IStateWriter
    {
        void WriteState(ref StateMessage state);
    }

    /// <summary>Where the two eyes are this frame. Implemented over the stereo rig.</summary>
    public interface IEyePositions
    {
        void GetEyes(out float3 left, out float3 right);
    }

    /// <summary>
    /// The phone's frame in order (Master Plan section 6.3, contract README F5). Engine-free: the component
    /// AtlasFrameLoop calls RunUpdate, RunState and RunAfterFrame with the engine's clock, and tests call them
    /// with their own. Steps 1 to 6 are plain calls made in order from RunUpdate; nothing here waits for the
    /// network or a file, and nothing allocates.
    /// </summary>
    public sealed class FrameLoopCore : IFrameLoop, IWalkerReset
    {
        /// <summary>The frame time is clamped to this range. Seconds.</summary>
        public const float MinDeltaTime = 0.004f, MaxDeltaTime = 0.1f;

        readonly List<ISenseStep> _sense = new List<ISenseStep>(4);
        // One flag per sense hook: its first exception has been reported.
        readonly List<bool> _senseFaulted = new List<bool>(4);
        readonly List<IUploadStep> _upload = new List<IUploadStep>(8);
        readonly List<IAfterFrameStep> _after = new List<IAfterFrameStep>(8);
        readonly IWalkerKernel _kernel;

        ISafetyStep _guard;
        IStateWriter _guardState;
        IPoseFilterStep _filter;
        IStateSink _sink;

        FrameContext _frame;
        StateMessage _state = StateMessage.Identity;
        int _nextFrameIndex;
        float2 _previousFloor;
        bool _havePreviousFloor;
        bool _roomResetLatched;
        bool _statePending;
        bool _afterPending;
        bool _sealed;
        bool _eyesFaulted;
        bool _tailFaulted;

        public FrameLoopCore(IWalkerKernel kernel)
        {
            if (kernel == null) throw new ArgumentNullException(nameof(kernel));
            _kernel = kernel;
            _frame.RawHeadRotation = quaternion.identity;
            _frame.HeadRotation = quaternion.identity;
            _frame.Tracking = TrackingState.Normal;
            _frame.Walker = kernel.Pose;
        }

        // ---- what the composition root wires --------------------------------------------------------------

        public IWalkerKernel Kernel { get { return _kernel; } }
        /// <summary>The space host's frame calls. May be null (no space is ever ticked).</summary>
        public ISpaceFrame Space { get; set; }
        /// <summary>For the common fields of the State. May be null.</summary>
        public ISpaceHost Host { get; set; }
        /// <summary>The pose pipeline: tells when the head is in the room frame, and is reset with the walker. May be null.</summary>
        public IPosePipeline Pipeline { get; set; }
        /// <summary>The quality tier in force. May be null (Nominal).</summary>
        public IQualityState Quality { get; set; }
        /// <summary>The stereo rig's eyes. May be null (both eyes at the head).</summary>
        public IEyePositions Eyes { get; set; }
        /// <summary>
        /// Receives one line for each misuse (a second guard, a hook added after boot) and one for the first
        /// exception of each step-1 hook. May be null.
        /// </summary>
        public Action<string> Warning { get; set; }

        /// <summary>Step-1 hooks (and the eye fill, and the late tail) that have thrown at least once.</summary>
        public int FaultedSenseCount
        {
            get
            {
                int count = (_eyesFaulted ? 1 : 0) + (_tailFaulted ? 1 : 0);
                for (int i = 0; i < _senseFaulted.Count; i++) if (_senseFaulted[i]) count++;
                return count;
            }
        }

        /// <summary>Frames run so far.</summary>
        public int FrameCount { get { return _nextFrameIndex; } }
        public bool HasGuard { get { return _guard != null; } }
        /// <summary>The State as last filled at step 8a.</summary>
        public ref readonly StateMessage LastState { get { return ref _state; } }

        /// <summary>Called by the composition root after AtlasServices.Booted: a hook added later is reported.</summary>
        public void Seal() { _sealed = true; }

        // ---- IFrameLoop -----------------------------------------------------------------------------------

        public ref readonly FrameContext Current { get { return ref _frame; } }

        public void AddSense(ISenseStep step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            NoteLate("sense");
            _sense.Add(step);
            _senseFaulted.Add(false);
        }

        public void SetGuard(ISafetyStep step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            if (_guard != null) Warn("frame loop: a second Bump Guard step replaces the first");
            NoteLate("guard");
            _guard = step;
            _guardState = step as IStateWriter;
        }

        public void SetPoseFilter(IPoseFilterStep step)
        {
            NoteLate("pose filter");
            _filter = step;
        }

        public void AddUpload(IUploadStep step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            NoteLate("upload");
            _upload.Add(step);
        }

        public void SetStateSink(IStateSink sink)
        {
            NoteLate("state sink");
            _sink = sink;
        }

        public void AddAfterFrame(IAfterFrameStep step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            NoteLate("after-frame");
            _after.Add(step);
        }

        public void ResetWalkerToFloor()
        {
            float3 head = _frame.HeadPosition;
            // The kernel refuses a point that is not finite; a broken head pose resets to the anchor.
            bool finite = math.all(math.isfinite(head));
            _kernel.ResetToFloor(finite ? new double2(head.x, head.z) : double2.zero);
            _frame.Walker = _kernel.Pose;
            _frame.FloorDelta = float2.zero;
            _havePreviousFloor = false;
        }

        public void ResetWalker(in WalkerPose pose)
        {
            _kernel.Reset(in pose);
            _frame.Walker = _kernel.Pose;
            _frame.FloorDelta = float2.zero;
            _havePreviousFloor = false;
        }

        // ---- IWalkerReset ---------------------------------------------------------------------------------

        public void LatchRoomReset() { _roomResetLatched = true; }

        public bool RoomResetPending { get { return _roomResetLatched; } }

        // ---- the frame ------------------------------------------------------------------------------------

        /// <summary>Steps 1 to 6. time is the app clock in seconds; deltaTime the time since the last frame.</summary>
        public void RunUpdate(double time, float deltaTime)
        {
            // A frame that was never rendered (no camera, a paused Editor view) still gets its tail, so a
            // space switch and the link keep moving.
            // A fault in that late tail must not cost this frame its Bump Guard step either.
            if (_statePending || _afterPending)
            {
                try
                {
                    RunState();
                    RunAfterFrame();
                }
                catch (Exception e)
                {
                    if (!_tailFaulted) ReportFault("the late tail of the last frame", e);
                    _tailFaulted = true;
                }
            }

            // BeginFrame
            _frame.FrameIndex = _nextFrameIndex++;
            _frame.Time = time;
            _frame.DeltaTime = math.clamp(deltaTime, MinDeltaTime, MaxDeltaTime);
            IQualityState quality = Quality;
            _frame.Tier = quality != null ? quality.Tier : QualityTier.Nominal;

            // 1  Read: the pose pipeline first, then input (the order they were added in). Bump Guard runs
            //    whatever happens here (Master Plan section 6.3, step 2; lane A contract, section 7.2): a hook
            //    that throws is reported once, keeps being called, and the frame goes on with what it has.
            int senseCount = _sense.Count;
            for (int i = 0; i < senseCount; i++)
            {
                try
                {
                    _sense[i].Sense(ref _frame);
                }
                catch (Exception e)
                {
                    if (!_senseFaulted[i]) ReportFault("sense hook " + i + " (" + _sense[i].GetType().Name + ")", e);
                    _senseFaulted[i] = true;
                }
            }
            if (senseCount == 0) _frame.Tracking = TrackingState.Normal;   // neutral: the pose stays as it was set

            IEyePositions eyes = Eyes;
            bool haveEyes = false;
            if (eyes != null)
            {
                try
                {
                    eyes.GetEyes(out _frame.LeftEyePosition, out _frame.RightEyePosition);
                    haveEyes = true;
                }
                catch (Exception e)
                {
                    if (!_eyesFaulted) ReportFault("the eye positions", e);
                    _eyesFaulted = true;
                }
            }
            if (!haveEyes)
            {
                _frame.LeftEyePosition = _frame.RawHeadPosition;
                _frame.RightEyePosition = _frame.RawHeadPosition;
            }

            // 2  Bump Guard: before anything can be skipped, with the raw pose and the depth of step 1.
            if (_guard != null) _guard.Guard(in _frame);

            // 3  Pose filter.
            if (_filter != null) _filter.Filter(ref _frame);
            else
            {
                _frame.HeadPosition = _frame.RawHeadPosition;
                _frame.HeadRotation = _frame.RawHeadRotation;
                _frame.PoseJumpRejected = false;
                float2 floor = _frame.HeadPosition.xz;
                _frame.FloorDelta = _havePreviousFloor ? floor - _previousFloor : float2.zero;
                _previousFloor = floor;
                _havePreviousFloor = true;
            }

            // A room became active: this is the first frame whose head is in the room frame, so the walker is
            // placed now, before the kernel sees the change of frame as a step.
            if (_roomResetLatched)
            {
                IPosePipeline pipeline = Pipeline;
                if (pipeline == null || pipeline.Sensors.RoomActive)
                {
                    _roomResetLatched = false;
                    ResetWalkerToFloor();
                    if (pipeline != null) pipeline.ResetGeometry();
                }
            }

            // 4  Kernel: exactly once per frame, with the head's floor point (the kernel forms the step itself)
            //    and the frame's time, by which a curved kernel tells standing from walking for its ramp.
            _frame.Kernel = _kernel.Step(new double2(_frame.HeadPosition.x, _frame.HeadPosition.z),
                _frame.Tracking == TrackingState.Normal, _frame.Time);
            _frame.Walker = _kernel.Pose;

            // 5  Space module.
            ISpaceFrame space = Space;
            if (space != null)
            {
                space.TickSpace(in _frame);
                // 6  Upload: the module first, then the registered hooks.
                space.UploadSpace(in _frame);
            }
            int uploadCount = _upload.Count;
            for (int i = 0; i < uploadCount; i++) _upload[i].Upload(in _frame);

            _statePending = true;
            _afterPending = true;
        }

        /// <summary>Step 8a: fills the State and hands it to the sink. Once per frame; a second call does nothing.</summary>
        public void RunState()
        {
            if (!_statePending) return;
            _statePending = false;

            FillState(ref _state);
            ISpaceFrame space = Space;
            if (space != null) space.WriteState(ref _state);
            if (_sink != null) _sink.Send(in _state);
        }

        /// <summary>Step 8b: the after-frame hooks in order. Once per frame; a second call does nothing.</summary>
        public void RunAfterFrame()
        {
            if (_statePending) RunState();
            if (!_afterPending) return;
            _afterPending = false;
            int count = _after.Count;
            for (int i = 0; i < count; i++) _after[i].AfterFrame(in _frame);
        }

        // The common fields of the State (lane A contract, section 2). The link writes seq, t_us and the lock.
        void FillState(ref StateMessage state)
        {
            state.Flags = 0;
            state.Tracking = TrackingStates.ToWire(_frame.Tracking);
            state.PoseJumpRejected = _frame.PoseJumpRejected || _frame.Kernel.Rejected && _frame.Tracking == TrackingState.Normal;
            state.QualityTier = QualityTable.ToWire(_frame.Tier);

            ISpaceHost host = Host;
            bool hasSpace = host != null && host.HasSpace;
            state.SpaceId = hasSpace ? (byte)host.Current : SpaceIds.NoSpace;
            state.SceneId = hasSpace ? SpaceIds.SceneId(host.Current) : SpaceIds.NoScene;
            state.WorldVer = host != null ? host.WorldVersion : (ushort)0;

            state.HeadPosX = _frame.RawHeadPosition.x;
            state.HeadPosY = _frame.RawHeadPosition.y;
            state.HeadPosZ = _frame.RawHeadPosition.z;
            float4 q = _frame.RawHeadRotation.value;
            state.HeadRotX = q.x;
            state.HeadRotY = q.y;
            state.HeadRotZ = q.z;
            state.HeadRotW = q.w;

            state.BumpLevel = 0;
            if (_guardState != null) _guardState.WriteState(ref state);

            state.Sheet = 0;
            ITileGraph graph = _kernel.Graph;
            if (graph == null || graph.Q == 4)
            {
                state.SetTile(0, 0, 0, 0, 0, 0);
                state.SetXformIdentity();
            }
            else
            {
                // A curved space walks the one kernel, so its tile and frame are the kernel's: the words of
                // kernel.md section 5.4 in order, and Local row-major (section 4.5). Read now, not from the
                // frame context: the module may have placed the walker in its Tick.
                WalkerPose pose = _kernel.Pose;
                TileId tile = pose.Tile;
                state.SetTile(tile.Xu, tile.Xv, tile.Yu, tile.Yv, tile.Tu, tile.Tv);
                Iso2 local = pose.Local;
                state.Xform0 = local.M00; state.Xform1 = local.M01; state.Xform2 = local.M02;
                state.Xform3 = local.M10; state.Xform4 = local.M11; state.Xform5 = local.M12;
                state.Xform6 = local.M20; state.Xform7 = local.M21; state.Xform8 = local.M22;
            }
            float rho = (float)_kernel.CullRho;
            state.P0 = (float)_kernel.MetresPerUnit;
            state.P1 = rho - 1f;
            state.P2 = rho;
            state.P3 = 0f;
            state.P4 = 0f;
            state.P5 = 0f;
            state.P6 = 0f;
            state.P7 = 0f;
        }

        void NoteLate(string what)
        {
            if (_sealed) Warn("frame loop: a " + what + " hook was added after boot");
        }

        // Only on the first exception of a hook, so the strings cost nothing in a healthy frame. A warning
        // sink that throws is not allowed to stop the frame either.
        void ReportFault(string what, Exception e)
        {
            try
            {
                Warn("frame loop: " + what + " threw " + e.GetType().Name + ": " + e.Message +
                     ". Bump Guard still runs; the hook is called again next frame.");
            }
            catch (Exception)
            {
            }
        }

        void Warn(string message)
        {
            Action<string> warning = Warning;
            if (warning != null) warning(message);
        }
    }
}
