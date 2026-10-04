using System;
using Atlas.Blades.Network.Core;
using Atlas.Link;
using Atlas.Telemetry;

namespace Atlas.Blades.Network
{
    /// <summary>net.file: which bundled network (seed 7 or seed 83).</summary>
    public enum NetworkFile { Seed7 = 0, Seed83 = 1 }

    /// <summary>net.view: the floor map (V2) or the walk (V3).</summary>
    public enum NetworkView { Floor = 0, Walk = 1 }

    /// <summary>
    /// The five parameters of contract section 6.3, declared with [AtlasParam] and bound like <c>NeedleSkyBeat</c>.
    /// <see cref="Apply"/> writes them to the view's plain fields; no view reads the registry.
    /// </summary>
    public sealed class NetworkParams : IDisposable
    {
        [AtlasParam("net.file", 0)] NetworkFile _file = NetworkFile.Seed7;
        [AtlasParam("net.view", 0)] NetworkView _view = NetworkView.Floor;
        [AtlasParam("net.rule", 0)] RoutingRule _rule = RoutingRule.Hyperbolic;
        [AtlasParam("net.recentreSeconds", 0.3f, 1.5f, 0.6f)] float _recentreSeconds = 0.6f;
        [AtlasParam("net.maxGazeNodes", 16f, 64f, 64f)] int _maxGazeNodes = 64;

        readonly ParamRegistry _registry;
        readonly ParamBinding _binding;

        public NetworkParams(ParamRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _registry = registry;
            _binding = ParamBinder.Bind(this, registry);
        }

        public NetworkFile File { get { return _file; } }
        public NetworkView View { get { return _view; } }
        public RoutingRule Rule { get { return _rule; } }
        public float RecentreSeconds { get { return _recentreSeconds; } }
        public int MaxGazeNodes { get { return _maxGazeNodes; } }

        /// <summary>Changes net.rule through the registry (the X twin), so the Console sees it and the field follows.</summary>
        public void SetRule(RoutingRule rule)
        {
            ParamEntry e = _registry.Find("net.rule");
            if (e != null) e.Set((int)rule);
            _rule = rule;
        }

        /// <summary>Copies the values to the view's fields.</summary>
        public void Apply(NetworkFloorView view)
        {
            if (view == null) return;
            view.RecentreSeconds = _recentreSeconds;
            view.MaxGazeNodes = _maxGazeNodes;
            view.Rule = _rule;
            view.File = (int)_file;
        }

        public void Dispose() { _binding.Dispose(); }
    }
}
