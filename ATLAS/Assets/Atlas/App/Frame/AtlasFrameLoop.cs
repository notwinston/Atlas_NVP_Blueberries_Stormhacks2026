using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.App
{
    /// <summary>
    /// The one driver of the phone's frame (Master Plan section 6.3, contract README F5). It runs after the
    /// tracked pose driver and after every other Update, makes steps 1 to 6 as plain calls from one Update,
    /// step 8a in LateUpdate and step 8b once the frame has been submitted for rendering. The order itself
    /// lives in <see cref="FrameLoopCore"/>; this component only brings the engine's clock and callbacks.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class AtlasFrameLoop : MonoBehaviour, IFrameLoop
    {
        FrameLoopCore _core;

        /// <summary>The engine-free loop. Set once by the composition root, before the component is enabled.</summary>
        public FrameLoopCore Core { get { return _core; } }

        /// <summary>Creates the component, disabled, on a new object "FrameLoop" under parent.</summary>
        public static AtlasFrameLoop Create(Transform parent, FrameLoopCore core)
        {
            var host = new GameObject("FrameLoop");
            host.transform.SetParent(parent, false);
            var loop = host.AddComponent<AtlasFrameLoop>();
            loop._core = core;
            loop.enabled = false;
            return loop;
        }

        void OnEnable()
        {
            RenderPipelineManager.endContextRendering += OnEndContextRendering;
        }

        void OnDisable()
        {
            RenderPipelineManager.endContextRendering -= OnEndContextRendering;
        }

        void Update()
        {
            if (_core != null) _core.RunUpdate(Time.unscaledTimeAsDouble, Time.unscaledDeltaTime);
        }

        void LateUpdate()
        {
            if (_core != null) _core.RunState();
        }

        // Raised once per rendered view (the Editor has several); the core runs its tail for the first only.
        void OnEndContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (_core != null) _core.RunAfterFrame();
        }

        // ---- IFrameLoop: the installers' view ---------------------------------------------------------------

        public ref readonly FrameContext Current { get { return ref _core.Current; } }
        public void AddSense(ISenseStep step) { _core.AddSense(step); }
        public void SetGuard(ISafetyStep step) { _core.SetGuard(step); }
        public void SetPoseFilter(IPoseFilterStep step) { _core.SetPoseFilter(step); }
        public void AddUpload(IUploadStep step) { _core.AddUpload(step); }
        public void SetStateSink(IStateSink sink) { _core.SetStateSink(sink); }
        public void AddAfterFrame(IAfterFrameStep step) { _core.AddAfterFrame(step); }
        public void ResetWalkerToFloor() { _core.ResetWalkerToFloor(); }
        public void ResetWalker(in WalkerPose pose) { _core.ResetWalker(in pose); }
    }
}
