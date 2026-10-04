using Atlas.Input;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Safety;
using Atlas.Scan;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.App
{
    /// <summary>
    /// Step 1, after the pose pipeline: freezes the input snapshot with this frame's raw head, then hands the
    /// keyboard walker its input for the next sample (contract README F9): the device's own, plus the walk
    /// input the Station sends from its Map page. Atlas.Input references no Atlas assembly, so the adapter
    /// lives here.
    /// </summary>
    public sealed class InputSenseStep : ISenseStep
    {
        readonly IAtlasInput _input;
        readonly IPosePipeline _pipeline;
        readonly RemoteWalkInput _remote;

        /// <param name="remote">The Station's walk input; may be null.</param>
        public InputSenseStep(IAtlasInput input, IPosePipeline pipeline, RemoteWalkInput remote = null)
        {
            _input = input;
            _pipeline = pipeline;
            _remote = remote;
        }

        public void Sense(ref FrameContext frame)
        {
            _input.Tick(frame.RawHeadPosition, frame.RawHeadRotation, frame.DeltaTime);
            // The remote hold runs down whatever the source, so an old input never moves a walker chosen later.
            float2 remoteMove = float2.zero;
            float remoteYaw = 0f, remoteSpeed = 0f;
            bool remote = _remote != null && _remote.Take(frame.DeltaTime, out remoteMove, out remoteYaw, out remoteSpeed);
            if (_pipeline == null) return;
            var walker = _pipeline.Source as KeyboardWalkerPoseSource;
            if (walker == null) return;
            WalkerInput w = _input.Walker;
            float2 move = new float2(w.Move.x, w.Move.y);
            float2 look = new float2(w.LookDegrees.x, w.LookDegrees.y);
            float speed = w.Speed;
            if (remote)
            {
                move = math.clamp(move + remoteMove, -1f, 1f);
                look.x += remoteYaw;
                speed = remoteSpeed;
            }
            walker.SetInput(move, look, speed);
        }
    }

    /// <summary>
    /// Step 1 where there is no pose pipeline: the raw head is read from the head transform.
    /// </summary>
    public sealed class HeadTransformSenseStep : ISenseStep
    {
        readonly Transform _head;

        public HeadTransformSenseStep(Transform head) { _head = head; }

        public void Sense(ref FrameContext frame)
        {
            if (_head != null)
            {
                _head.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
                frame.RawHeadPosition = position;
                frame.RawHeadRotation = rotation;
            }
            frame.Tracking = TrackingState.Normal;
        }
    }

    /// <summary>
    /// Step 2: Bump Guard with the sensor frame the pose pipeline built in step 1 (raw pose, depth). It builds
    /// nothing itself. Also writes Bump Guard's fields of the State, so the frame loop names no safety type.
    /// <para>With a mask source (the tracked hands), a frame that holds a new depth image is copied and the copy
    /// carries the source's mask for that image: where the walker's own hands are in it
    /// (documents/ATLAS_Hands_Design.md section 7.1). Bump Guard learns nothing of where the mask came from and
    /// holds it to its own limits. Without a source the step is what it always was.</para>
    /// </summary>
    public sealed class BumpGuardStep : ISafetyStep, IStateWriter
    {
        readonly IBumpGuard _guard;
        readonly IPosePipeline _pipeline;
        readonly IDepthMaskSource _mask;

        /// <param name="mask">Who can say where the walker's hands are in a depth image; may be null.</param>
        public BumpGuardStep(IBumpGuard guard, IPosePipeline pipeline, IDepthMaskSource mask = null)
        {
            _guard = guard;
            _pipeline = pipeline;
            _mask = mask;
        }

        /// <summary>
        /// Times the mask source threw. Bump Guard ran on the unmasked frame each time. The step stays silent,
        /// so that a source that fails every frame costs nothing: whoever wired the source reads this and reports.
        /// </summary>
        public int MaskFaults { get; private set; }

        public void Guard(in FrameContext frame)
        {
            ref readonly SensorFrame sensors = ref _pipeline.Sensors;
            if (_mask != null && sensors.HasDepth)
            {
                SensorFrame masked = sensors;
                bool asked = false;
                try
                {
                    if (!_mask.TryGetMask(sensors.Depth.Sequence, out masked.Mask)) masked.Mask = default(DepthMask);
                    asked = true;
                }
                catch (System.Exception)
                {
                    // Nothing outside Bump Guard may stop it: the frame goes in as the pipeline built it.
                    MaskFaults++;
                }
                if (asked)
                {
                    _guard.Tick(in masked);
                    return;
                }
            }
            _guard.Tick(in sensors);
        }

        public void WriteState(ref StateMessage state)
        {
            state.BumpLevel = (byte)_guard.Level;
            state.CueActive = BumpCue.Shown != BumpLevel.Clear;
        }
    }

    /// <summary>
    /// Step 6: the look's shader globals (head position, metres per kernel unit, time, fog cross-fade). It runs
    /// after the entered module's own upload and is the only writer of _HypL and _HeadPosWS: L (or R) is the
    /// kernel's of this frame, and the head is the geometry head that stepped the kernel, so the warp is
    /// centred on the sample the anchors were built for.
    /// </summary>
    public sealed class LookUploadStep : IUploadStep
    {
        readonly IWalkerKernel _kernel;

        public LookUploadStep(IWalkerKernel kernel) { _kernel = kernel; }

        public void Upload(in FrameContext frame)
        {
            AtlasLook.Upload(frame.HeadPosition, _kernel.MetresPerUnit, frame.Time, frame.DeltaTime);
        }
    }

    /// <summary>
    /// Step 6, after the entered module's own upload: the tiles the curved world drew this frame go to the
    /// telemetry row's visibleTiles (Master Plan section 7.3.3). 0 while no tile renderer is configured.
    /// Atlas.Telemetry does not reference Atlas.Rendering, so the copy is made here.
    /// </summary>
    public sealed class TileStatsUploadStep : IUploadStep
    {
        readonly System.Action<ushort> _write;

        public TileStatsUploadStep(System.Action<ushort> write) { _write = write; }

        public void Upload(in FrameContext frame)
        {
            int drawn = AtlasServices.TryGet(out ITileDrawStats stats) ? stats.TilesDrawn : 0;
            _write((ushort)(drawn < 0 ? 0 : (drawn > ushort.MaxValue ? ushort.MaxValue : drawn)));
        }
    }

    /// <summary>Step 6: the pose pipeline's rotation prediction goes to the eyes (identity when it is off).</summary>
    public sealed class RigUploadStep : IUploadStep
    {
        readonly IStereoRig _rig;
        readonly IPosePipeline _pipeline;

        public RigUploadStep(IStereoRig rig, IPosePipeline pipeline)
        {
            _rig = rig;
            _pipeline = pipeline;
        }

        public void Upload(in FrameContext frame)
        {
            _rig.PredictionOffset = _pipeline.PredictionRotation;
        }
    }

    /// <summary>The two eye positions from the stereo rig.</summary>
    public sealed class RigEyePositions : IEyePositions
    {
        readonly IStereoRig _rig;

        public RigEyePositions(IStereoRig rig) { _rig = rig; }

        public void GetEyes(out float3 left, out float3 right)
        {
            left = _rig.GetEyePosition(Eye.Left);
            right = _rig.GetEyePosition(Eye.Right);
        }
    }
}
