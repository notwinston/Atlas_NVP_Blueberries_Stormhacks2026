using System;
using System.Collections.Generic;
using Atlas.Blades.Network.Core;
using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.Blades.Network.Walk
{
    // V3 beacons (design section 4 V3): the bearing to a neighbour is the kernel's log map, as in the Lung's AirwayBeacon.
    // The walker's frame in tile-zero coordinates is TileFrame(pose.Tile) * pose.Local; the neighbour's hyperboloid point P
    // (NetworkModel.HyperboloidPoint) taken into that frame and through Geometry2.Log gives the geodesic: its length is the
    // distance, its direction the bearing. Engine-free; the per-frame calls allocate nothing.
    public sealed class NetworkBeacon
    {
        const int MaxSteps = 4096;

        readonly TileGraph _graph;
        readonly NetworkModel _model;
        readonly NetworkTilePlacement _placement;
        readonly Dictionary<TileId, Iso2> _frames = new Dictionary<TileId, Iso2>();

        // Built once (a load-time cost): the frame of every node's tile and of its four neighbours, from tile zero.
        public NetworkBeacon(TileGraph graph, NetworkModel model, NetworkTilePlacement placement)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _placement = placement ?? throw new ArgumentNullException(nameof(placement));
            if (graph.Q != 5) throw new ArgumentException("the network is placed on {4,5}", nameof(graph));
            for (int i = 0; i < model.NodeCount; i++) Descend(model.HyperboloidPoint(i));
        }

        public NetworkModel Model { get { return _model; } }
        public NetworkTilePlacement Placement { get { return _placement; } }

        static double Cosh(double3 c, double3 p) { return c.z * p.z - c.x * p.x - c.y * p.y; }

        // The same descent as NetworkTilePlacement.Snap, keeping every frame it passes (and the neighbours').
        void Descend(double3 p)
        {
            TileId tile = TileId.Zero;
            double3x3 frame = double3x3.identity;
            double best = Cosh(frame.c2, p);
            for (int step = 0; step <= MaxSteps; step++)
            {
                Remember(tile, frame);
                int pick = -1;
                double3x3 pickFrame = frame;
                for (int k = 0; k < 4; k++)
                {
                    double3x3 f = math.mul(frame, _graph.StepMatrix(tile, k).M);
                    Remember(_graph.Neighbour(tile, k), f);
                    double c = Cosh(f.c2, p);
                    if (c < best) { best = c; pick = k; pickFrame = f; }
                }
                if (pick < 0) break;
                tile = _graph.Neighbour(tile, pick);
                frame = pickFrame;
            }
        }

        void Remember(TileId tile, double3x3 frame)
        {
            if (!_frames.ContainsKey(tile)) _frames.Add(tile, new Iso2(frame, -1));
        }

        // The pose of a walker standing at the node, in the node's tile, unturned in the tile's frame.
        public WalkerPose PoseAt(int node)
        {
            WalkerPose pose;
            pose.Tile = _placement.Tile(node);
            pose.Local = Iso2.Boost(_placement.Offset(node), -1);
            return pose;
        }

        // False when the walker's tile is not one the placement knows (a node's tile or its neighbour): no beacons then.
        public bool TryWalkerFrame(in WalkerPose pose, out Iso2 global)
        {
            Iso2 tile;
            if (!_frames.TryGetValue(pose.Tile, out tile)) { global = Iso2.Identity; return false; }
            global = tile * pose.Local;
            return true;
        }

        // The log map: the tangent vector (hyperbolic units) in the walker's frame at which the geodesic reaches P.
        // Its x and y are the world x and z (kernel.md section 4.5); its length is the distance.
        public static double2 LogVector(in Iso2 walkerGlobal, double3 p)
        {
            return Geometry2.Log(walkerGlobal.Inverse().Apply(p), -1);
        }

        // Bearing in radians clockwise from the walker's forward (+y of the log vector, world +z).
        public static double BearingOf(double2 logVector) { return Math.Atan2(logVector.x, logVector.y); }

        public bool TryBearing(in WalkerPose pose, int to, out double bearingRadians, out double distance)
        {
            Iso2 g;
            if (!TryWalkerFrame(in pose, out g)) { bearingRadians = 0; distance = 0; return false; }
            double2 v = LogVector(in g, _model.HyperboloidPoint(to));
            bearingRadians = BearingOf(v);
            distance = math.length(v);
            return true;
        }

        // Neighbour k (ascending ids) of the node the walker is at.
        public bool TryNeighbourBearing(in WalkerPose pose, int node, int k, out double bearingRadians, out double distance)
        {
            return TryBearing(in pose, _model.Neighbour(node, k), out bearingRadians, out distance);
        }
    }
}
