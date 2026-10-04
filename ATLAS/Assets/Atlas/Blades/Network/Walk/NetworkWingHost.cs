using System;
using Atlas.Blades.Network.Core;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Telemetry;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Blades.Network.Walk
{
    // V3 walk the network (design section 4 V3), registered with Curved Walk like AirwayWingHost. The visitor stands at a
    // node; the neighbours' bearings come from NetworkBeacon; a hop (NetworkHop) fades, re-roots the app's kernel at the
    // neighbour through IFrameLoop.ResetWalker, and fades back in.
    public sealed class NetworkWingModule : ISpaceModule
    {
        readonly NetworkModel _model;
        readonly NetworkBeacon _beacon;
        readonly NetworkHop _hop = new NetworkHop();
        readonly double[] _bearings = new double[NetworkBeaconView.MaxArrows];
        readonly int[] _target = new int[NetworkBeaconView.MaxArrows];
        int _node;

        public NetworkWingModule(NetworkModel model, TileGraph graph)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _beacon = new NetworkBeacon(graph, model, new NetworkTilePlacement(graph, model));
            _node = 0;
            for (int i = 1; i < model.NodeCount; i++) if (model.Degree(i) > model.Degree(_node)) _node = i;   // start at the hub
        }

        public SpaceId Id { get { return SpaceId.CurvedWalk; } }
        public bool Registered { get { return false; } }          // the wing starts in Mode 1 and never matches the room
        public NetworkBeacon Beacon { get { return _beacon; } }
        public NetworkHop Hop { get { return _hop; } }
        public int Node { get { return _node; } }
        public NetworkBeaconView View { get; set; }
        // Set by the caller that wires events (NetworkEvents.Hop, NET-07): (from, to, distance), at the re-root.
        public Action<int, int, double> HopLogged { get; set; }
        // The one seam for the world fade, in AtlasLook's convention (1 = world visible, 0 = black; AtlasCore.hlsl). How a blade
        // applies the world fade is open: contract section 9 C6 (AtlasLook.SetFade is documented SpaceLab only). The lead's
        // decision changes this one line. Replaced in tests; null skips the fade.
        public Action<float> FadeSink { get; set; } = AtlasLook.SetFade;
        // Replaced in tests; null leaves the kernel alone.
        public Action<WalkerPose> ResetWalker { get; set; } = pose => { IFrameLoop loop; if (AtlasServices.TryGet(out loop)) loop.ResetWalker(in pose); };

        public void Configure(RoomModel room, ParamRegistry registry) { }

        public void Enter(in WalkerPose start)
        {
            if (ResetWalker != null) ResetWalker(_beacon.PoseAt(_node));
            Rebuild(_beacon.PoseAt(_node));
        }

        // Starts a hop to neighbour `to` of the current node (dwell on its beacon). False if not a neighbour or one is running.
        public bool RequestHop(int to, double nowSeconds)
        {
            bool neighbour = false;
            for (int k = 0, n = _model.NeighbourCount(_node); k < n; k++) if (_model.Neighbour(_node, k) == to) neighbour = true;
            return neighbour && _hop.Begin(_node, to, nowSeconds);
        }

        public void Tick(in FrameContext frame)
        {
            if (_hop.Active)
            {
                bool reroot = _hop.Tick(frame.Time);
                if (reroot)
                {
                    int from = _node;
                    _node = _hop.To;
                    WalkerPose pose = _beacon.PoseAt(_node);
                    if (ResetWalker != null) ResetWalker(pose);
                    if (HopLogged != null) HopLogged(from, _node, _model.Distance(from, _node));
                    Rebuild(pose);
                }
                if (FadeSink != null) FadeSink(1f - (float)_hop.Level);   // NetworkHop.Level: 0 clear, 1 black
            }
        }

        void Rebuild(in WalkerPose pose)
        {
            int count = math.min(_model.NeighbourCount(_node), NetworkBeaconView.MaxArrows), shown = 0;
            for (int k = 0; k < count; k++)
            {
                double bearing, distance;
                if (!_beacon.TryNeighbourBearing(in pose, _node, k, out bearing, out distance)) continue;
                _bearings[shown] = bearing; _target[shown] = _model.Neighbour(_node, k); shown++;
            }
            if (View != null) { View.SetBearings(_bearings, shown, float3.zero); View.SetVisible(shown > 0); }
        }

        public void Exit() { if (FadeSink != null) FadeSink(1f); if (View != null) View.SetVisible(false); }   // leave the world visible

        public void WriteState(ref StateMessage state) { }
    }

    public sealed class NetworkWingHost : MonoBehaviour
    {
        public NetworkWingModule Module { get; private set; }

        // The module is built once and handed to SpaceRegistry as S2's factory.
        public NetworkWingModule Register(NetworkModel model)
        {
            Module = new NetworkWingModule(model, new TileGraph(5));
            SpaceRegistry.Register(SpaceId.CurvedWalk, () => Module);
            return Module;
        }
    }
}
