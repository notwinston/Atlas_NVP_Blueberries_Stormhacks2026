using System;
using Atlas.Blades.Network.Core;
using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.Blades.Network.Walk
{
    // V3 node placement (design section 4 V3): the point P of each node is snapped to the {4,5} tile graph by stepping from
    // tile zero to the neighbour whose centre is nearest P, until no neighbour is nearer. The node's offset inside that tile
    // goes into the tile chart (log map of P in the tile's own frame). Engine-free; Snap allocates nothing once the graph has
    // grown its tiles.
    public sealed class NetworkTilePlacement
    {
        const int MaxSteps = 4096;

        readonly TileGraph _graph;
        readonly TileId[] _tile;
        readonly double2[] _offset;

        public NetworkTilePlacement(TileGraph graph, NetworkModel model)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (graph.Q != 5) throw new ArgumentException("the network is placed on {4,5}", nameof(graph));
            int n = model.NodeCount;
            _tile = new TileId[n];
            _offset = new double2[n];
            for (int i = 0; i < n; i++) Snap(graph, model.HyperboloidPoint(i), out _tile[i], out _offset[i]);
        }

        public int NodeCount { get { return _tile.Length; } }
        public TileId Tile(int node) { return _tile[node]; }
        // The node's position in its tile's chart (hyperbolic units, the log map at the tile's centre).
        public double2 Offset(int node) { return _offset[node]; }

        // cosh d(c, p) = -<c, p>; monotone in d, so it orders centres without an acosh.
        static double Cosh(double3 c, double3 p) { return c.z * p.z - c.x * p.x - c.y * p.y; }

        public static void Snap(TileGraph graph, double3 p, out TileId tile, out double2 offset)
        {
            tile = TileId.Zero;
            double3x3 frame = double3x3.identity;               // tile frame in tile zero's coordinates
            double best = Cosh(frame.c2, p);
            for (int step = 0; step < MaxSteps; step++)
            {
                int pick = -1;
                double3x3 pickFrame = frame;
                for (int k = 0; k < 4; k++)
                {
                    double3x3 f = math.mul(frame, graph.StepMatrix(tile, k).M);   // neighbour frame = F * G (kernel §4.6)
                    double c = Cosh(f.c2, p);
                    if (c < best) { best = c; pick = k; pickFrame = f; }
                }
                if (pick < 0) break;
                tile = graph.Neighbour(tile, pick);
                frame = pickFrame;
            }
            double3 local = new Iso2(frame, -1).Inverse().Apply(p);
            offset = Geometry2.Log(local, -1);
        }
    }
}
