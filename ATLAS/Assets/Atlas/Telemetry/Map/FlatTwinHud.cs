using System;
using System.Diagnostics;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using UnityEngine;

namespace Atlas.Telemetry
{
    /// <summary>
    /// The in-headset mini-map (Master Plan rule 6, task P1-D07; lane D contract, section 5.9): the flat twin of
    /// the entered space on a small head-locked panel, lower right, 0.25 m across at 1.0 m. It is off by default,
    /// because a mini-map distracts, and is switched by the parameter <c>hud.map</c>. The map is rebuilt ten
    /// times per second at step 8b of the frame, never every frame; a hidden map costs one ring write.
    /// </summary>
    public sealed class FlatTwinHud : IAfterFrameStep, IDisposable
    {
        public const string ParamMap = "hud.map";
        public const string ParamGroup = "hud";
        /// <summary>Side of the panel and its distance from the eyes, metres.</summary>
        public const float PanelSize = 0.25f, Distance = 1.0f;
        /// <summary>Where the panel sits: this far right of and below the view axis, degrees.</summary>
        public const float RightDegrees = 15f, BelowDegrees = 13f;
        public const float RebuildSeconds = 0.1f;
        /// <summary>The map's budget on the phone, milliseconds per rebuild (Master Plan gate G6).</summary>
        public const float BudgetMs = 0.3f;

        readonly FlatTwinPanel _panel;
        readonly FlatTwinTrail _trail = new FlatTwinTrail();
        ISpaceHost _host;
        IWalkerKernel _kernel;
        float _sinceRebuild = RebuildSeconds;
        bool _visible;

        FlatTwinHud(Transform hud)
        {
            _panel = new FlatTwinPanel(hud, "AtlasFlatTwin", PanelSize, true, 0.014f);
            Transform t = _panel.Transform;
            if (t != null)
            {
                float right = RightDegrees * Mathf.Deg2Rad, below = BelowDegrees * Mathf.Deg2Rad;
                var direction = new Vector3(Mathf.Sin(right) * Mathf.Cos(below), -Mathf.Sin(below), Mathf.Cos(right) * Mathf.Cos(below));
                t.localPosition = direction * Distance;
                // Turned to face the eyes.
                t.localRotation = Quaternion.LookRotation(direction, Vector3.up);
            }
            _panel.Visible = false;
        }

        /// <summary>
        /// Creates the panel under <c>boot.Hud</c>, declares <c>hud.map</c> (off) in the registry and, when the
        /// frame loop is given or already published, adds the map to step 8b. Lane D's installer calls it once,
        /// after the HUD; a second call returns the map that exists.
        /// </summary>
        public static FlatTwinHud Install(AtlasBootContext boot, IFrameLoop loop = null)
        {
            if (boot == null) throw new ArgumentNullException("boot");
            FlatTwinHud existing;
            if (AtlasServices.TryGet(out existing)) return existing;

            var map = new FlatTwinHud(boot.Hud);
            ParamRegistry registry = boot.Params;
            ParamEntry entry;
            if (registry != null && !registry.TryGet(ParamMap, out entry))
                registry.DeclareBool(ParamMap, ParamGroup, false, on => map.Visible = on);
            if (loop == null) AtlasServices.TryGet(out loop);
            if (loop != null) loop.AddAfterFrame(map);
            AtlasServices.Register(map);
            return map;
        }

        /// <summary><c>hud.map</c>.</summary>
        public bool Visible
        {
            get { return _visible; }
            set
            {
                if (value == _visible) return;
                _visible = value;
                _panel.Visible = value;
                _sinceRebuild = RebuildSeconds;
            }
        }

        public FlatTwin Twin { get { return _panel.Twin; } }
        public FlatTwinTrail Trail { get { return _trail; } }

        /// <summary>Cost of the last rebuild and of the worst one since boot, milliseconds, for the perf gate.</summary>
        public float LastRebuildMs { get; private set; }
        public float WorstRebuildMs { get; private set; }
        public int Rebuilds { get; private set; }

        /// <summary>Step 8b: every tenth of a second the trail takes the frame, and a visible map is redrawn.</summary>
        public void AfterFrame(in FrameContext frame)
        {
            _sinceRebuild += frame.DeltaTime;
            if (_sinceRebuild < RebuildSeconds) return;
            _sinceRebuild = 0f;

            long started = _visible ? Stopwatch.GetTimestamp() : 0;
            if (_host == null) AtlasServices.TryGet(out _host);
            if (_kernel == null) AtlasServices.TryGet(out _kernel);

            StateMessage state = StateMessage.Identity;
            ISpaceModule module = FillState(ref state, in frame);
            _trail.Add(in state);
            if (!_visible) return;

            var source = module as IFlatTwinSource;
            if (source != null) FlatTwinBuilder.Build(_panel.Twin, source);
            else FlatTwinBuilder.Build(_panel.Twin, _host != null ? _host.Room : null, _kernel, in state, _trail);
            _panel.Apply();

            LastRebuildMs = (float)((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
            // The first rebuilds size the buffers; they are not the steady cost.
            if (++Rebuilds > 3 && LastRebuildMs > WorstRebuildMs) WorstRebuildMs = LastRebuildMs;
        }

        // The State of this frame as the map needs it: the head, the space and the space's own fields. The
        // module writes what it drew with, as it does for the link.
        ISpaceModule FillState(ref StateMessage state, in FrameContext frame)
        {
            state.TimeUs = (long)(frame.Time * 1e6);
            state.HeadPosX = frame.RawHeadPosition.x;
            state.HeadPosY = frame.RawHeadPosition.y;
            state.HeadPosZ = frame.RawHeadPosition.z;
            state.HeadRotX = frame.RawHeadRotation.value.x;
            state.HeadRotY = frame.RawHeadRotation.value.y;
            state.HeadRotZ = frame.RawHeadRotation.value.z;
            state.HeadRotW = frame.RawHeadRotation.value.w;
            state.SpaceId = SpaceIds.NoSpace;
            if (_kernel != null) state.P2 = (float)_kernel.CullRho;
            ISpaceHost host = _host;
            if (host == null || !host.HasSpace) return null;
            ISpaceModule module = host.Module;
            if (module == null) return null;
            state.SpaceId = (byte)host.Current;
            state.WorldVer = host.WorldVersion;
            module.WriteState(ref state);
            return module;
        }

        public void Dispose()
        {
            _panel.Dispose();
            AtlasServices.Unregister(this);
        }
    }
}
