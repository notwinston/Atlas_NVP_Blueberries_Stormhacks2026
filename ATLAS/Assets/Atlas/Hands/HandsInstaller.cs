using System;
using System.Diagnostics;
using Atlas.Input;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Hands
{
    /// <summary>
    /// What the composition root hands over (documents/ATLAS_Hands_Design.md section 3.7, E3). Atlas.Hands may
    /// reference neither the app, the scan nor Bump Guard, so what it needs from them comes as plain references
    /// and delegates. Things built after the hands (Bump Guard, the event log, the space host) are reached lazily.
    /// </summary>
    public sealed class HandsPorts
    {
        /// <summary>Parent of the hands' objects. Never room content: its subtree is re-layered and switched off as rooms change.</summary>
        public Transform Parent;
        /// <summary>Where the sounds play: the head. May be null (no sound).</summary>
        public Transform Head;
        /// <summary>The material pool. Null: nothing is drawn (the hands still track, touch and mask).</summary>
        public MaterialPool Pool;
        public IStereoRig Rig;
        public IAtlasInput Input;
        public IPosePipeline Pipeline;
        /// <summary>The rear camera's transform.</summary>
        public Transform TrackedCamera;
        /// <summary>The AR session's space in the scene (XROrigin.TrackablesParent), asked every frame; may return null.</summary>
        public Func<Transform> SessionSpace;
        /// <summary>XRSessionSubsystem.nativePtr of the running AR session, or zero while there is none.</summary>
        public Func<IntPtr> NativeSession;
        /// <summary>Bump Guard's level is not Clear.</summary>
        public Func<bool> Warning;
        /// <summary>A phone, not the Editor.</summary>
        public bool IsDevice;
        /// <summary>Folder for hand logs (hands.record). Null: no recording.</summary>
        public string RecordFolder;
        /// <summary>Times the App's Bump Guard step caught the mask source throwing. May be null.</summary>
        public Func<int> MaskFaults;
    }

    /// <summary>The installed hands: the three frame hooks, the mask source, and the parameters.</summary>
    public sealed class HandsSystem
    {
        public const string Group = "hands";
        public const string ParamEnabled = "hands.enabled", ParamRate = "hands.rate", ParamReach = "hands.reach",
                            ParamSound = "hands.sound", ParamDraw = "hands.draw", ParamEditor = "hands.editor",
                            ParamBench = "hands.bench", ParamRecord = "hands.record", ParamIgnoreHeat = "hands.ignoreHeat";
        static readonly string[] s_rates = { "auto", "30", "15" };

        public HandsRuntime Runtime { get; internal set; }
        public HandsView View { get; internal set; }
        public HandsSounds Sounds { get; internal set; }
        internal EditorHandsSource Editor;

        /// <summary>Step 1, to be added between the pose pipeline's sense step and input's.</summary>
        public ISenseStep Sense { get; internal set; }
        /// <summary>Step 6, to be added after the rig's upload step.</summary>
        public IUploadStep Upload { get; internal set; }
        /// <summary>Step 8b.</summary>
        public IAfterFrameStep AfterFrame { get; internal set; }
        /// <summary>For the App's Bump Guard step.</summary>
        public IDepthMaskSource Mask { get { return Runtime; } }

        /// <summary>
        /// Declares the hands.* parameters. Called by the composition root after its own declarations, so that no
        /// existing parameter changes its wire id.
        /// </summary>
        public void DeclareParams(ParamRegistry registry)
        {
            if (registry == null) return;
            HandsRuntime runtime = Runtime;
            EditorHandsSource editor = Editor;
            // On by default; the menu's Hands row (Spaces page) and the kill switch turn it off.
            ParamEntry enabled = registry.DeclareBool(ParamEnabled, Group, true, on => runtime.Enabled = on);
            // Declaring does not call the callback: the runtime takes the value in force now, or it would stay
            // off until the switch was flipped.
            if (runtime != null) runtime.Enabled = enabled.Bool;
            registry.DeclareEnum(ParamRate, Group, s_rates, 0, index => runtime.RateOverride = index == 1 ? 30 : index == 2 ? 15 : 0);
            registry.DeclareFloat(ParamReach, Group, HandsConstants.ReachMinMetres, HandsConstants.ReachMaxMetres,
                HandsConstants.ReachMetres, metres => runtime.Reach = metres);
            registry.DeclareBool(ParamSound, Group, true, on => runtime.Sound = on);
            registry.DeclareBool(ParamDraw, Group, true, on => runtime.Draw = on);
            registry.DeclareBool(ParamEditor, Group, false, on => { if (editor != null) editor.Enabled = on; });
            registry.DeclareBool(ParamBench, Group, false, on => runtime.Bench = on);
            registry.DeclareBool(ParamRecord, Group, false, on => runtime.Record = on);
            // On by default: the hands keep running on a hot phone, at the reduced rate. The menu's "Hands in heat" row turns it off.
            ParamEntry ignoreHeat = registry.DeclareBool(ParamIgnoreHeat, Group, true, on => runtime.IgnoreHeat = on);
            if (runtime != null) runtime.IgnoreHeat = ignoreHeat.Bool;
        }

        /// <summary>Stops the detector, returns the materials, destroys the objects. Safe to call twice.</summary>
        public void Shutdown()
        {
            if (Runtime != null) Runtime.Shutdown(Time.unscaledTimeAsDouble);
            if (View != null) View.Dispose();
            if (Sounds != null) Sounds.Dispose();
            View = null;
            Sounds = null;
            if (Runtime != null)
            {
                AtlasServices.Unregister<IHands>(Runtime);
                AtlasServices.Unregister<IReachLayout>(Runtime);
            }
        }
    }

    /// <summary>
    /// The phone's source is made by a platform assembly that needs the native AR session, which does not exist
    /// until some frames after start. This stands in until it does.
    /// </summary>
    sealed class DeferredHandsSource : IHandsSource
    {
        readonly Func<IntPtr> _session;
        IHandsSource _inner;
        IntPtr _madeFor;
        int _rate;

        public DeferredHandsSource(Func<IntPtr> session) { _session = session; }

        IHandsSource Inner
        {
            get
            {
                if (!HandsPlatform.HasPlatformSource || _session == null) return _inner;
                IntPtr session = _session();
                // One source for each AR session: the plugin keeps the pointer it was started with, so a session
                // subsystem that was made again gets a new source and the old one is stopped first.
                if (_inner != null && (session == _madeFor || session == IntPtr.Zero)) return _inner;
                if (session == IntPtr.Zero) return null;
                if (_inner != null) _inner.Stop();
                _inner = null;
                IHandsSource made = HandsPlatform.Create(session);
                if (made == null || !made.Available) return null;
                _inner = made;
                _madeFor = session;
                if (_rate > 0) _inner.SetRate(_rate);
                return _inner;
            }
        }

        public bool Available { get { IHandsSource inner = Inner; return inner != null && inner.Available; } }
        public string Name { get { return _inner != null ? _inner.Name : "none"; } }
        public int Fault { get { return _inner != null ? _inner.Fault : 0; } }

        public void SetRate(int hz)
        {
            _rate = hz;
            IHandsSource inner = Inner;
            if (inner != null) inner.SetRate(hz);
        }

        public bool TryRead(double time, ref HandsDetection detection)
        {
            IHandsSource inner = Inner;
            return inner != null && inner.TryRead(time, ref detection);
        }

        public void Stop()
        {
            _rate = 0;
            if (_inner != null) _inner.Stop();
        }
    }

    /// <summary>Step 1: the hands' sense hook.</summary>
    public sealed class HandsSenseStep : ISenseStep
    {
        readonly HandsRuntime _runtime;
        public HandsSenseStep(HandsRuntime runtime) { _runtime = runtime; }
        public void Sense(ref FrameContext frame) { _runtime.Sense(ref frame); }
    }

    /// <summary>
    /// Step 6: skins and uploads. The frame loop has no net under its upload hooks, so this one has its own: an
    /// exception switches the hands off for the session and the frame carries on (decision R9).
    /// </summary>
    public sealed class HandsUploadStep : IUploadStep
    {
        readonly HandsSystem _system;
        public HandsUploadStep(HandsSystem system) { _system = system; }

        public void Upload(in FrameContext frame)
        {
            HandsRuntime runtime = _system.Runtime;
            if (runtime.Faulted)
            {
                // A fault in any step (sense, upload or after-frame) takes the view down with it, so the last
                // drawn hands and marks never stay standing in the room for the rest of the session.
                DropView();
                return;
            }
            long start = Stopwatch.GetTimestamp();
            try
            {
                HandsView view = _system.View;
                if (view != null) view.Upload(in frame);
            }
            catch (Exception e)
            {
                runtime.ReportFault("upload", e);
                DropView();
            }
            runtime.AddUploadTicks(Stopwatch.GetTimestamp() - start);
        }

        void DropView()
        {
            HandsView view = _system.View;
            if (view == null) return;
            _system.View = null;
            try { view.Dispose(); } catch (Exception) { }
        }
    }

    /// <summary>Step 8b: edge events and sounds, behind the same kind of net as the upload step.</summary>
    public sealed class HandsAfterFrameStep : IAfterFrameStep
    {
        readonly HandsSystem _system;
        public HandsAfterFrameStep(HandsSystem system) { _system = system; }

        public void AfterFrame(in FrameContext frame)
        {
            HandsRuntime runtime = _system.Runtime;
            if (runtime.Faulted) return;
            try
            {
                HandsSounds sounds = _system.Sounds;
                if (sounds != null && runtime.Sound)
                {
                    HandsTouchState touch = runtime.InputTouch;
                    if (touch.Left.Fired || touch.Right.Fired) sounds.Press();
                    if (touch.PinchFired) sounds.Pinch();
                    if (runtime.Touch(HandSide.Left).ContactBegan || runtime.Touch(HandSide.Right).ContactBegan) sounds.Contact();
                }
                runtime.AfterFrame(in frame);
            }
            catch (Exception e)
            {
                runtime.ReportFault("after", e);
            }
        }
    }

    /// <summary>
    /// The one entry point the composition root calls (section 3.7, E3): builds the source, the runtime and the
    /// view, publishes IHands and IReachLayout, and returns the three frame hooks for the root to add in order.
    /// </summary>
    public static class HandsInstaller
    {
        public static HandsSystem Install(HandsPorts ports)
        {
            if (ports == null) throw new ArgumentNullException("ports");
            var system = new HandsSystem();

            Transform camera = ports.TrackedCamera;
            Func<Transform> sessionSpace = ports.SessionSpace;
            Func<float4x4> worldFromSession = () =>
            {
                Transform space = sessionSpace != null ? sessionSpace() : null;
                return space != null ? (float4x4)space.localToWorldMatrix : float4x4.identity;
            };

            // The source: the plugin on a phone, mouse and keys in the Editor, nothing anywhere else.
            IHandsSource source;
            if (ports.IsDevice)
            {
                source = new DeferredHandsSource(ports.NativeSession);
            }
            else if (Application.isEditor && camera != null)
            {
                system.Editor = new EditorHandsSource(() =>
                {
                    Transform space = sessionSpace != null ? sessionSpace() : null;
                    float4x4 world = camera.localToWorldMatrix;
                    return space != null ? math.mul((float4x4)space.worldToLocalMatrix, world) : world;
                });
                source = system.Editor;
            }
            else
            {
                source = new NullHandsSource();
            }

            IStereoRig rig = ports.Rig;
            IPosePipeline pipeline = ports.Pipeline;
            EditorHandsSource editor = system.Editor;
            string recordFolder = ports.RecordFolder;
            var deps = new HandsDeps
            {
                Source = source,
                Input = ports.Input,
                Pipeline = pipeline,
                Headset = () => rig == null || rig.Mode == DisplayMode.Headset,
                Warning = ports.Warning,
                // The physical camera must be what moves the view; the Editor's stand-in rides on the Editor's own head.
                HeadFromCamera = () => (editor != null && editor.Available) ||
                                       (pipeline != null && pipeline.Source != null && pipeline.Source.DrivesRig),
                WorldFromSession = worldFromSession,
                CameraPosition = () => camera != null ? (float3)camera.position : float3.zero,
                Events = () => { IEventLog log; return AtlasServices.TryGet(out log) ? log : null; },
                Host = () => { ISpaceHost host; return AtlasServices.TryGet(out host) ? host : null; },
                RecordPath = () => string.IsNullOrEmpty(recordFolder) ? null
                    : System.IO.Path.Combine(recordFolder, "hands_" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ") + ".handlog"),
                MaskFaults = ports.MaskFaults,
            };
            if (camera == null) deps.CameraPosition = null;

            system.Runtime = new HandsRuntime(deps);
            if (ports.Pool != null && ports.Parent != null)
                system.View = HandsView.Create(ports.Parent, ports.Pool, system.Runtime);
            if (ports.Head != null) system.Sounds = new HandsSounds(ports.Head);

            system.Sense = new HandsSenseStep(system.Runtime);
            system.Upload = new HandsUploadStep(system);
            system.AfterFrame = new HandsAfterFrameStep(system);

            AtlasServices.Register<IHands>(system.Runtime);
            AtlasServices.Register<IReachLayout>(system.Runtime);
            return system;
        }
    }
}
