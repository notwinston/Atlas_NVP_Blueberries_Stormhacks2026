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
    /// keyboard walker its input for the next sample (contract README F9). Atlas.Input references no Atlas
    /// assembly, so the adapter lives here.
    /// </summary>
    public sealed class InputSenseStep : ISenseStep
    {
        readonly IAtlasInput _input;
        readonly IPosePipeline _pipeline;

        public InputSenseStep(IAtlasInput input, IPosePipeline pipeline)
        {
            _input = input;
            _pipeline = pipeline;
        }

        public void Sense(ref FrameContext frame)
        {
            _input.Tick(frame.RawHeadPosition, frame.RawHeadRotation, frame.DeltaTime);
            if (_pipeline == null) return;
            var walker = _pipeline.Source as KeyboardWalkerPoseSource;
            if (walker == null) return;
            WalkerInput w = _input.Walker;
            walker.SetInput(new float2(w.Move.x, w.Move.y), new float2(w.LookDegrees.x, w.LookDegrees.y), w.Speed);
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
    /// </summary>
    public sealed class BumpGuardStep : ISafetyStep, IStateWriter
    {
        readonly IBumpGuard _guard;
        readonly IPosePipeline _pipeline;

        public BumpGuardStep(IBumpGuard guard, IPosePipeline pipeline)
        {
            _guard = guard;
            _pipeline = pipeline;
        }

        public void Guard(in FrameContext frame)
        {
            _guard.Tick(in _pipeline.Sensors);
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
