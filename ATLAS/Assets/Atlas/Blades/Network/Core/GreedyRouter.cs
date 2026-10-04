using System;

namespace Atlas.Blades.Network.Core
{
    // hypernet.greedy and BFS over a NetworkModel (contract section 5.3). Engine-free; Route and ShortestHops allocate nothing.
    public sealed class GreedyRouter
    {
        public const int Limit = 200;

        readonly int[] _giant;
        readonly int[] _dist, _queue;

        public GreedyRouter(NetworkModel model)
        {
            Model = model;
            int n = model.NodeCount;
            _dist = new int[n]; _queue = new int[n];
            int[] comp = new int[n];
            for (int i = 0; i < n; i++) comp[i] = -1;
            int best = -1, bestSize = 0, id = 0;
            for (int s = 0; s < n; s++)
            {
                if (comp[s] >= 0) continue;
                int head = 0, tail = 0, size = 0;
                _queue[tail++] = s; comp[s] = id;
                while (head < tail)
                {
                    int v = _queue[head++]; size++;
                    for (int k = 0, c = model.NeighbourCount(v); k < c; k++)
                    {
                        int w = model.Neighbour(v, k);
                        if (comp[w] < 0) { comp[w] = id; _queue[tail++] = w; }
                    }
                }
                if (size > bestSize) { bestSize = size; best = id; }   // first (lowest-id) component wins a tie
                id++;
            }
            _giant = new int[bestSize];
            for (int i = 0, g = 0; i < n; i++) if (comp[i] == best) _giant[g++] = i;
        }

        public NetworkModel Model { get; }
        public int GiantCount { get { return _giant.Length; } }
        public int GiantNode(int k) { return _giant[k]; }

        public int Route(int src, int dst, RoutingRule rule, int[] path, out bool success)
        {
            NetworkModel m = Model;
            bool hyper = rule == RoutingRule.Hyperbolic;
            int count = 0, v = src;
            path[count++] = src;
            while (v != dst && count < Limit)
            {
                int c = m.NeighbourCount(v);
                if (c == 0) { success = false; return count; }
                int w = m.Neighbour(v, 0);
                double best = hyper ? m.Distance(w, dst) : m.FlatDistance(w, dst);
                for (int k = 1; k < c; k++)
                {
                    int u = m.Neighbour(v, k);
                    double du = hyper ? m.Distance(u, dst) : m.FlatDistance(u, dst);
                    if (du < best) { best = du; w = u; }
                }
                double here = hyper ? m.Distance(v, dst) : m.FlatDistance(v, dst);
                if (best >= here) { success = false; return count; }
                path[count++] = w; v = w;
            }
            success = v == dst;
            return count;
        }

        public int ShortestHops(int src, int dst)
        {
            if (src == dst) return 0;
            int n = Model.NodeCount;
            for (int i = 0; i < n; i++) _dist[i] = -1;
            int head = 0, tail = 0;
            _queue[tail++] = src; _dist[src] = 0;
            while (head < tail)
            {
                int v = _queue[head++];
                for (int k = 0, c = Model.NeighbourCount(v); k < c; k++)
                {
                    int w = Model.Neighbour(v, k);
                    if (_dist[w] >= 0) continue;
                    _dist[w] = _dist[v] + 1;
                    if (w == dst) return _dist[w];
                    _queue[tail++] = w;
                }
            }
            return -1;
        }

        public static double Stretch(int hops, int shortest) { return (double)hops / shortest; }
    }
}
