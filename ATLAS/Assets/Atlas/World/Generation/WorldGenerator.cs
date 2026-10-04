using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.World
{
    // Master Plan §3.4 world generator, {4,5} only. Main thread only (TileGraph is not thread-safe).
    // Hand-over: (1) the world depends on the whole footprint (TowerClass is the first class with no Home tile), so the Spectator
    // needs the same footprint as the phone, not only the seed; WorldHash covers HomeTiles and TowerClass to detect a mismatch.
    // (2) The memo dictionaries allocate on first query of a tile: P1-B06 must pre-warm Content for the tiles it will pool, or cache content per pooled tile.
    public sealed class WorldGenerator
    {
        const int CodeSpanProposal = 100, CodeStartA = 101, CodeStartB = 102;
        const double CircumRadius = 0.8425; // §8.5 literal
        const double StartReach = 2.0;      // §10.1.4 "within rho <= 2 of entry"

        struct Node { public int Arch; public int Feature; }

        readonly TileGraph graph = new TileGraph(5);
        readonly HashSet<TileId> home = new HashSet<TileId>();
        readonly List<TileId> homeSorted;
        readonly int towerClass;
        readonly bool hasStart;
        readonly TileId startA, startB;
        readonly int startEdgeA, startEdgeB;

        readonly Dictionary<TileId, int> baseMemo = new Dictionary<TileId, int>();
        readonly Dictionary<TileId, Node> spanMemo = new Dictionary<TileId, Node>();
        readonly Dictionary<TileId, Node> doorMemo = new Dictionary<TileId, Node>();
        readonly Dictionary<TileId, int> parentMemo = new Dictionary<TileId, int>();
        readonly Dictionary<TileId, TileContent> contentMemo = new Dictionary<TileId, TileContent>();

        public WorldGenerator(uint seed, RoomFootprint footprint)
        {
            Seed = seed; Footprint = footprint;
            home.Add(TileId.Zero);
            if (footprint != null) AddFootprintTiles(footprint);
            homeSorted = new List<TileId>(home);
            homeSorted.Sort(TileOrder.Compare);

            // Tower class: first class from the seed-selected index that has no Home tile (D2).
            towerClass = -1;
            var homeClasses = new bool[TowerLattice.ClassCount];
            foreach (var h in home) homeClasses[TowerLattice.ClassOf(h)] = true;
            for (int i = 0; i < TowerLattice.ClassCount; i++)
            {
                int idx = (int)(((ulong)seed + (ulong)i) % TowerLattice.ClassCount);
                if (!homeClasses[idx]) { towerClass = idx; break; }
            }

            hasStart = FindStartSpan(out startA, out startB);
            if (hasStart)
            {
                startEdgeA = EdgeTo(startA, startB);
                startEdgeB = EdgeTo(startB, startA);
            }
        }

        public uint Seed { get; }
        public RoomFootprint Footprint { get; }
        public TileGraph Graph => graph;
        public int TowerClass => towerClass;
        public IReadOnlyList<TileId> HomeTiles => homeSorted;
        public bool IsHome(TileId id) => home.Contains(id);

        bool IsTower(TileId id) => towerClass >= 0 && !home.Contains(id) && TowerLattice.ClassOf(id) == towerClass;

        public bool TryGetStartSpan(out TileId a, out TileId b) { a = startA; b = startB; return hasStart; }

        // ---- Home set (Master Plan §3.4, §4.6) ----
        void AddFootprintTiles(RoomFootprint fp)
        {
            const double step = 0.05;
            var pts = fp.Outline;
            double2 mn = pts[0], mx = pts[0];
            for (int i = 1; i < pts.Count; i++) { mn = math.min(mn, pts[i]); mx = math.max(mx, pts[i]); }
            int nx = (int)Math.Floor((mx.x - mn.x) / step), nz = (int)Math.Floor((mx.y - mn.y) / step);
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= nz; j++)
                {
                    var p = new double2(mn.x + i * step, mn.y + j * step);
                    if (Inside(pts, p)) Locate(p, fp.L);
                }
            for (int i = 0; i < pts.Count; i++)
            {
                double2 a = pts[i], b = pts[(i + 1) % pts.Count];
                int n = Math.Max(1, (int)Math.Ceiling(math.length(b - a) / step));
                for (int s = 0; s <= n; s++) Locate(a + (b - a) * ((double)s / n), fp.L);
            }
        }

        static bool Inside(IReadOnlyList<double2> poly, double2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                double2 a = poly[i], b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        // Descent with TileGraph.ExitEdge's test (R3 §A5) and no hysteresis; P = exp(p / L) (A.2).
        void Locate(double2 p, double L)
        {
            double ch = Math.Cosh(graph.InRadius), sh = Math.Sinh(graph.InRadius);
            double3 P = Geometry2.Exp(p / L, -1);
            var t = TileId.Zero;
            var s = new double4();
            for (int step = 0; step < 64; step++)
            {
                s = new double4(P.x * ch - P.z * sh, P.y * ch - P.z * sh, -P.x * ch - P.z * sh, -P.y * ch - P.z * sh);
                int k = 0; double best = s.x;
                if (s.y > best) { best = s.y; k = 1; }
                if (s.z > best) { best = s.z; k = 2; }
                if (s.w > best) { best = s.w; k = 3; }
                if (best <= 1e-9) break;
                P = math.mul(graph.StepMatrix(t, k).Inverse().M, P);
                t = graph.Neighbour(t, k);
            }
            home.Add(t);
            for (int k = 0; k < 4; k++)
                if (Math.Abs(s[k]) <= 1e-9) home.Add(graph.Neighbour(t, k)); // robust to last-bit platform differences
        }

        // ---- graph helpers ----
        public int BackEdge(TileId id, int edge)
        {
            var n = graph.Neighbour(id, edge);
            for (int e = 0; e < 4; e++) if (graph.Neighbour(n, e) == id) return e;
            throw new InvalidOperationException("no back edge");
        }

        int EdgeTo(TileId t, TileId n)
        {
            for (int e = 0; e < 4; e++) if (graph.Neighbour(t, e) == n) return e;
            throw new InvalidOperationException("tiles are not adjacent");
        }

        public List<TileId> Ball(int depth)
        {
            var list = new List<TileId> { TileId.Zero };
            var seen = new HashSet<TileId> { TileId.Zero };
            int start = 0;
            for (int d = 0; d < depth; d++)
            {
                int end = list.Count;
                for (int i = start; i < end; i++)
                    for (int k = 0; k < 4; k++)
                    {
                        var n = graph.Neighbour(list[i], k);
                        if (seen.Add(n)) list.Add(n);
                    }
                start = end;
            }
            return list;
        }

        // Parent = neighbour with the smallest T, compared exactly (T strictly decreases towards tile zero; kernel.md §9.2).
        // A geometric direction towards tile zero only: the edge to the parent may carry a kerb, so it is not a walkable route home.
        int ParentEdgeOf(TileId t)
        {
            if (t == TileId.Zero) return -1;
            if (parentMemo.TryGetValue(t, out int e)) return e;
            int best = 0; var bn = graph.Neighbour(t, 0);
            for (int k = 1; k < 4; k++)
            {
                var n = graph.Neighbour(t, k);
                if (ZPhiMath.Compare(n.Tu, n.Tv, bn.Tu, bn.Tv) < 0) { best = k; bn = n; }
            }
            parentMemo[t] = best;
            return best;
        }

        public TileId Parent(TileId id)
        {
            if (id == TileId.Zero) throw new ArgumentException("tile zero has no parent", nameof(id));
            return graph.Neighbour(id, ParentEdgeOf(id));
        }

        // ---- start span (Master Plan §3.4, §3.5, §10.1.4; D3) ----
        bool FindStartSpan(out TileId a, out TileId b)
        {
            a = b = TileId.Zero;
            var list = new List<VisibleTile>();
            graph.VisibleSet(TileId.Zero, StartReach + CircumRadius, list);
            var set = new HashSet<TileId>();
            foreach (var v in list) set.Add(v.Id);
            bool found = false; long bu = 0, bv = 0; ulong bx = 0; TileId ba = default, bb = default;
            foreach (var v in list)
            {
                var x = v.Id;
                if (home.Contains(x) || IsTower(x)) continue;
                for (int k = 0; k < 4; k++)
                {
                    var y = graph.Neighbour(x, k);
                    if (!set.Contains(y) || home.Contains(y) || IsTower(y) || TileOrder.Compare(x, y) >= 0) continue;
                    long su = x.Tu + y.Tu, sv = x.Tv + y.Tv;
                    ulong xr = x.ContentSeed(Seed) ^ y.ContentSeed(Seed);
                    bool better;
                    if (!found) better = true;
                    else
                    {
                        int c = ZPhiMath.Compare(su, sv, bu, bv);
                        if (c != 0) better = c < 0;
                        else if (xr != bx) better = xr < bx;
                        else
                        {
                            int c1 = TileOrder.Compare(x, ba);
                            better = c1 != 0 ? c1 < 0 : TileOrder.Compare(y, bb) < 0;
                        }
                    }
                    if (better) { found = true; bu = su; bv = sv; bx = xr; ba = x; bb = y; }
                }
            }
            if (!found) return false;
            int ord = ZPhiMath.Compare(ba.Tu, ba.Tv, bb.Tu, bb.Tv);
            bool aFirst = ord != 0 ? ord < 0 : TileOrder.Compare(ba, bb) < 0;
            a = aFirst ? ba : bb; b = aFirst ? bb : ba;
            return true;
        }

        // ---- staged content (Master Plan §3.4 table; D8) ----
        static int Roll(ulong h) => (int)(h & 15);
        static int CorridorAxis(ulong h) => (int)((h >> 8) & 1);
        static int SpanEdge(ulong h) => (int)((h >> 9) & 3);

        int Base(TileId t)
        {
            if (baseMemo.TryGetValue(t, out int r)) return r;
            if (home.Contains(t)) r = (int)Archetype.Home;
            else if (IsTower(t)) r = (int)Archetype.Tower;
            else if (hasStart && t == startA) r = CodeStartA;
            else if (hasStart && t == startB) r = CodeStartB;
            else
            {
                int roll = Roll(t.ContentSeed(Seed));
                r = roll < 8 ? (int)Archetype.Plaza
                  : roll < 11 ? (int)Archetype.Corridor
                  : roll < 13 ? (int)Archetype.Garden
                  : roll < 15 ? (int)Archetype.Doorway
                  : CodeSpanProposal;
            }
            baseMemo[t] = r;
            return r;
        }

        // Smallest (TileOrder) neighbour m of n that proposes a span onto n; edge = n's edge facing m.
        bool MinProposer(TileId n, out TileId proposer, out int edge)
        {
            proposer = default; edge = -1; bool found = false;
            for (int e = 0; e < 4; e++)
            {
                var m = graph.Neighbour(n, e);
                if (Base(m) != CodeSpanProposal) continue;
                if (graph.Neighbour(m, SpanEdge(m.ContentSeed(Seed))) != n) continue;
                if (!found || TileOrder.Compare(m, proposer) < 0) { proposer = m; edge = e; found = true; }
            }
            return found;
        }

        Node SpanStage(TileId t)
        {
            if (spanMemo.TryGetValue(t, out var node)) return node;
            int b = Base(t);
            if (b == CodeStartA) node = new Node { Arch = (int)Archetype.SpanA, Feature = startEdgeA };
            else if (b == CodeStartB) node = new Node { Arch = (int)Archetype.SpanB, Feature = startEdgeB };
            else if (b == CodeSpanProposal)
            {
                int se = SpanEdge(t.ContentSeed(Seed));
                var n = graph.Neighbour(t, se);
                int bn = Base(n);
                bool valid = (bn == (int)Archetype.Plaza || bn == (int)Archetype.Garden) && MinProposer(n, out var p, out _) && p == t;
                node = valid ? new Node { Arch = (int)Archetype.SpanA, Feature = se } : new Node { Arch = (int)Archetype.Plaza, Feature = -1 };
            }
            else if ((b == (int)Archetype.Plaza || b == (int)Archetype.Garden) && MinProposer(t, out _, out int fe))
                node = new Node { Arch = (int)Archetype.SpanB, Feature = fe };
            else node = new Node { Arch = b, Feature = -1 };
            spanMemo[t] = node;
            return node;
        }

        // Doorway = region gate: its door faces a neighbour of another region (never a Home tile).
        int WantDoor(TileId t)
        {
            int r = Regions.RegionOf(t);
            for (int k = 0; k < 4; k++)
            {
                var n = graph.Neighbour(t, k);
                if (!home.Contains(n) && Regions.RegionOf(n) != r) return k;
            }
            return -1;
        }

        Node DoorStage(TileId t)
        {
            if (doorMemo.TryGetValue(t, out var node)) return node;
            node = SpanStage(t);
            if (node.Arch == (int)Archetype.Doorway)
            {
                int w = WantDoor(t);
                if (w < 0) node = new Node { Arch = (int)Archetype.Plaza, Feature = -1 };
                else
                {
                    var n = graph.Neighbour(t, w);
                    bool yield = false;
                    if (SpanStage(n).Arch == (int)Archetype.Doorway)
                    {
                        int wn = WantDoor(n);
                        yield = wn >= 0 && graph.Neighbour(n, wn) == t && TileOrder.Compare(n, t) < 0;
                    }
                    node = yield ? new Node { Arch = (int)Archetype.Plaza, Feature = -1 } : new Node { Arch = (int)Archetype.Doorway, Feature = w };
                }
            }
            doorMemo[t] = node;
            return node;
        }

        // Corridor = 0.9 m kerbs on two opposite edges (§3.4/§3.5); a kerb is never doubled,
        // never next to Home and never on a feature edge (D4).
        bool WantKerb(TileId t, int e) => DoorStage(t).Arch == (int)Archetype.Corridor && (e & 1) == CorridorAxis(t.ContentSeed(Seed));

        // Edge wanted closed by a kerb on either side. 'final' selects the corridors that survived KerbStage; otherwise the
        // base candidates (DoorStage) are used, which is what the local connectivity rule below looks at.
        bool WantsClosed(TileId t, int e, bool final, out bool ownedByT)
        {
            ownedByT = false;
            var n = graph.Neighbour(t, e);
            int e2 = BackEdge(t, e);
            if (home.Contains(t) || home.Contains(n)) return false;
            if (DoorStage(t).Feature == e || DoorStage(n).Feature == e2) return false;
            bool wt = WantKerb(t, e), wn = WantKerb(n, e2);
            if (final) { wt = wt && KerbStage(t).Arch == (int)Archetype.Corridor; wn = wn && KerbStage(n).Arch == (int)Archetype.Corridor; }
            if (!wt && !wn) return false;
            ownedByT = wt && (!wn || TileOrder.Compare(t, n) < 0);
            return true;
        }

        // Local reachability rule (§3.4, §11.1 "100% of tiles reachable"), the same everywhere in the world. A wanted edge is kept
        // closed only if at one of its two end corners none of the other four edges of that corner's 5-cycle is wanted closed:
        // then those four edges stay open and walk around the corner, so every closed edge has an open detour of length 4 and the
        // open graph stays connected. A pure function of the neighbourhood (no plan ball, no tree).
        bool CornerFree(TileId t, int e, int d)
        {
            var cur = t; int x = e;
            for (int i = 0; i < 5; i++)
            {
                if (i > 0 && WantsClosed(cur, x, false, out _)) return false;
                int b = BackEdge(cur, x);
                cur = graph.Neighbour(cur, x);
                x = (b + d) & 3;
            }
            if (cur != t || x != e) throw new InvalidOperationException("corner walk did not close after 5 tiles");
            return true;
        }

        bool RulePass(TileId t, int e) => CornerFree(t, e, 1) || CornerFree(t, e, 3);

        // A Corridor that cannot keep both of its kerbs (an axis edge neither exempt nor passing the rule) is demoted to Plaza,
        // so every Corridor in the world has the kerbs that define it (§3.4). Deviation D14.
        readonly Dictionary<TileId, Node> kerbMemo = new Dictionary<TileId, Node>();
        Node KerbStage(TileId t)
        {
            if (kerbMemo.TryGetValue(t, out var node)) return node;
            node = DoorStage(t);
            if (node.Arch == (int)Archetype.Corridor)
            {
                int axis = CorridorAxis(t.ContentSeed(Seed));
                for (int e = axis; e < 4; e += 2)
                    if (WantsClosed(t, e, false, out _) && !RulePass(t, e)) { node = new Node { Arch = (int)Archetype.Plaza, Feature = -1 }; break; }
            }
            kerbMemo[t] = node;
            return node;
        }

        bool EdgeClosed(TileId t, int e, out bool ownedByT)
        {
            ownedByT = false;
            if (!WantsClosed(t, e, true, out ownedByT)) return false;
            if (RulePass(t, e)) return true;
            ownedByT = false;
            return false;
        }

        public TileContent Content(TileId id)
        {
            if (contentMemo.TryGetValue(id, out var c)) return c;
            var node = KerbStage(id);
            int kerb = 0, closed = 0;
            for (int e = 0; e < 4; e++)
                if (EdgeClosed(id, e, out bool own)) { closed |= 1 << e; if (own) kerb |= 1 << e; }
            ulong h = id.ContentSeed(Seed);
            c = new TileContent(id, (Archetype)node.Arch, (byte)Regions.RegionOf(id), (byte)kerb, (byte)closed, 0xF,
                (sbyte)node.Feature, (sbyte)ParentEdgeOf(id), h);
            contentMemo[id] = c;
            return c;
        }

        // ---- world hash (SplitMix64 as TileId.Mix, kernel.md §5.7); integers only ----
        static ulong Mix(ulong h, long w)
        {
            unchecked
            {
                h += (ulong)w + 0x9E3779B97F4A7C15UL;
                h = (h ^ (h >> 30)) * 0xBF58476D1CE4E5B9UL;
                h = (h ^ (h >> 27)) * 0x94D049BB133111EBUL;
                return h ^ (h >> 31);
            }
        }

        static ulong MixId(ulong h, TileId t)
        {
            h = Mix(h, t.Xu); h = Mix(h, t.Xv); h = Mix(h, t.Yu); h = Mix(h, t.Yv); h = Mix(h, t.Tu); h = Mix(h, t.Tv);
            return h;
        }

        public ulong WorldHash(int depth = 6)
        {
            unchecked
            {
                ulong h = 0x9E3779B97F4A7C15UL ^ Seed;
                h = Mix(h, towerClass);
                h = Mix(h, homeSorted.Count);
                foreach (var t in homeSorted) h = MixId(h, t);
                var ball = Ball(depth);
                ball.Sort(TileOrder.Compare);
                foreach (var t in ball)
                {
                    var c = Content(t);
                    h = MixId(h, t);
                    long packed = (long)c.Archetype | ((long)c.Region << 8) | ((long)c.KerbMask << 16) | ((long)c.ClosedMask << 24)
                                | ((long)c.PostMask << 32) | ((long)(c.FeatureEdge + 1) << 40) | ((long)(c.ParentEdge + 1) << 48);
                    h = Mix(h, packed);
                }
                return h;
            }
        }
    }
}
