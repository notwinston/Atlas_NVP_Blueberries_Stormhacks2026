using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Atlas.Kernel
{
    public interface ITileGraph
    {
        int Q { get; } // tiles per corner (Master Plan §6.5)
        void VisibleSet(TileId centre, double rho, List<VisibleTile> into);
    }

    // kernel.md §4.1. F = F[t]: tile t's frame -> the centre tile's frame (§4.6); kappa = -1.
    public readonly struct VisibleTile
    {
        public VisibleTile(TileId id, Iso2 f, int graphDistance) { Id = id; F = f; GraphDistance = graphDistance; }
        public readonly TileId Id;
        public readonly Iso2 F;
        public readonly int GraphDistance; // edge steps from the centre tile (BFS depth)
    }

    // Tile graph of {4,q}, q = 5 (exact ids, Master Plan A.5) or q = 6 (tolerance-hash variant, R3 §A3). Main thread only; never evicts.
    public sealed class TileGraph : ITileGraph
    {
        public const double CrossingHysteresis = 0.02; // units of L (R3 §A5 HYST)
        const double Sqrt3 = 1.7320508075688772;

        readonly int q;
        readonly double inR, circR, chR, shR, shHyst;
        readonly Iso2[] G, GInv;       // [4*k + j]: S_k * R^j and its inverse
        readonly double3x3[] Gd;
        readonly double3x3[] s6;       // q = 6 step matrices S6_k, snapped

        Dictionary<TileId, int> index = new Dictionary<TileId, int>(4096);
        int cap = 4096, count;
        TileId[] ids; XFrame[] frame5; double3x3[] frame6;
        int[] nbr; byte[] spin; int[] stamp; int[] depth; double3x3[] fmat; int[] queue;
        int stampNow;

        static readonly Iso2[] G5, G5Inv; static readonly double3x3[] G5d;
        static readonly Iso2[] G6, G6Inv; static readonly double3x3[] G6d; static readonly double3x3[] S6;

        static TileGraph()
        {
            G5 = new Iso2[16]; G5Inv = new Iso2[16]; G5d = new double3x3[16];
            for (int k = 0; k < 4; k++)
            {
                var m = XFrame.Identity.Step(k);
                for (int j = 0; j < 4; j++)
                {
                    var iso = m.ToIso2();
                    G5[4 * k + j] = iso; G5Inv[4 * k + j] = iso.Inverse(); G5d[4 * k + j] = iso.M;
                    m = m.Rot1();
                }
            }
            // q = 6: T6 = [[2,0,sqrt3],[0,1,0],[sqrt3,0,2]] (R3 §A3), H = diag(-1,-1,1)
            var T6 = new double3x3(2, 0, Sqrt3, 0, 1, 0, Sqrt3, 0, 2);
            var H = new double3x3(-1, 0, 0, 0, -1, 0, 0, 0, 1);
            var th = math.mul(T6, H);
            S6 = new double3x3[4]; G6 = new Iso2[16]; G6Inv = new Iso2[16]; G6d = new double3x3[16];
            var rk = double3x3.identity;
            for (int k = 0; k < 4; k++)
            {
                S6[k] = Snap(math.mul(rk, th));
                var m = S6[k];
                for (int j = 0; j < 4; j++)
                {
                    var iso = new Iso2(m, -1);
                    G6[4 * k + j] = iso; G6Inv[4 * k + j] = iso.Inverse(); G6d[4 * k + j] = m;
                    m = Rot6(m);
                }
                rk = Rot6(rk);
            }
        }

        public TileGraph(int q)
        {
            if (q != 5 && q != 6) throw new ArgumentOutOfRangeException(nameof(q), "q = 5 or 6 (q = 3 is added by P1-B08)");
            this.q = q;
            // A.3: cosh r = cos(pi/q)/sin(pi/4); r_c = acosh(cot(pi/4) cot(pi/q))
            inR = Math.Acosh(Math.Cos(Math.PI / q) / Math.Sin(Math.PI / 4));
            circR = Math.Acosh(1.0 / Math.Tan(Math.PI / q));
            chR = Math.Cosh(inR); shR = Math.Sinh(inR); shHyst = Math.Sinh(CrossingHysteresis);
            if (q == 5) { G = G5; GInv = G5Inv; Gd = G5d; } else { G = G6; GInv = G6Inv; Gd = G6d; s6 = S6; }
            ids = new TileId[cap]; fmat = new double3x3[cap]; nbr = new int[4 * cap]; spin = new byte[4 * cap];
            stamp = new int[cap]; depth = new int[cap]; queue = new int[cap];
            if (q == 5) frame5 = new XFrame[cap]; else frame6 = new double3x3[cap];
            Array.Fill(nbr, -1);
            if (q == 5) AddNode5(TileId.Zero, XFrame.Identity); else AddNode6(TileId.Zero, double3x3.identity);
        }

        public int Q => q;
        public double InRadius => inR;
        public double CircumRadius => circR;
        internal int Count => count;

        internal bool TryGetFrame(TileId id, out XFrame f)
        {
            if (q == 5 && index.TryGetValue(id, out int i)) { f = frame5[i]; return true; }
            f = default; return false;
        }

        // ---- storage ----
        void Grow()
        {
            int n = cap * 2;
            Array.Resize(ref ids, n); Array.Resize(ref fmat, n); Array.Resize(ref stamp, n);
            Array.Resize(ref depth, n); Array.Resize(ref queue, n); Array.Resize(ref spin, 4 * n);
            int old = nbr.Length; Array.Resize(ref nbr, 4 * n); Array.Fill(nbr, -1, old, nbr.Length - old);
            if (q == 5) Array.Resize(ref frame5, n); else Array.Resize(ref frame6, n);
            cap = n;
        }

        int AddNode5(TileId id, XFrame f)
        {
            if (count == cap) Grow();
            int i = count++; ids[i] = id; frame5[i] = f; index.Add(id, i); return i;
        }

        int AddNode6(TileId id, double3x3 f)
        {
            if (count == cap) Grow();
            int i = count++; ids[i] = id; frame6[i] = f; index.Add(id, i); return i;
        }

        int IndexOf(TileId id)
        {
            if (index.TryGetValue(id, out int i)) return i;
            if (q == 5)
            {
                var f = ExactTiling.CanonicalFrame(id);
                return AddNode5(f.Centre, f);
            }
            var f6 = Resolve6(id);
            return AddNode6(Id6(f6), f6);
        }

        // Neighbour across edge k; the reverse link is not filled (R3 §A5).
        int NeighbourIndex(int t, int k)
        {
            int slot = 4 * t + k;
            int c = nbr[slot];
            if (c >= 0) return c;
            int n, j;
            if (q == 5)
            {
                var raw = frame5[t].Step(k);
                var canon = raw.Canon();
                var m = raw;
                for (j = 0; j < 4 && !m.Equals(canon); j++) m = m.Rot1();
                if (j == 4) throw new InvalidOperationException("no spin matches the canonical frame");
                var id = canon.Centre;
                if (!index.TryGetValue(id, out n)) n = AddNode5(id, canon);
            }
            else
            {
                var raw = Snap(math.mul(frame6[t], s6[k]));
                var canon = Canon6(raw);
                var m = raw;
                for (j = 0; j < 4 && !m.Equals(canon); j++) m = Rot6(m);
                if (j == 4) throw new InvalidOperationException("no spin matches the canonical frame");
                var id = Id6(canon);
                if (!index.TryGetValue(id, out n)) n = AddNode6(id, canon);
            }
            nbr[slot] = n; spin[slot] = (byte)j;
            return n;
        }

        public TileId Neighbour(TileId tile, int edge)
        {
            if (edge < 0 || edge > 3) throw new ArgumentOutOfRangeException(nameof(edge));
            int n = NeighbourIndex(IndexOf(tile), edge); // may grow ids, so read it afterwards
            return ids[n];
        }

        // G[i] of §4.6: neighbour's frame -> tile's frame
        public Iso2 StepMatrix(TileId tile, int edge)
        {
            if (edge < 0 || edge > 3) throw new ArgumentOutOfRangeException(nameof(edge));
            int t = IndexOf(tile);
            NeighbourIndex(t, edge);
            return G[4 * edge + spin[4 * t + edge]];
        }

        // ---- visible set (kernel.md §4.6) ----
        public void VisibleSet(TileId centre, double rho, List<VisibleTile> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            if (double.IsNaN(rho) || double.IsInfinity(rho) || rho < 0) throw new ArgumentOutOfRangeException(nameof(rho));
            into.Clear();
            int c = IndexOf(centre);
            int cur = ++stampNow;
            double keep = Math.Cosh(rho);                       // A.2: cosh d = w of F*o
            double prune = Math.Cosh(rho + circR) + 1e-9;       // tiles within rho are reached through tiles within rho + r_c
            fmat[c] = double3x3.identity; depth[c] = 0; stamp[c] = cur;
            int head = 0, tail = 0;
            queue[tail++] = c;
            while (head < tail)
            {
                int t = queue[head++];
                var ft = fmat[t]; int dt = depth[t];
                if (ft.c2.z <= keep) into.Add(new VisibleTile(ids[t], new Iso2(ft, -1), dt));
                for (int k = 0; k < 4; k++)
                {
                    int n = NeighbourIndex(t, k);
                    if (stamp[n] == cur) continue;
                    stamp[n] = cur;
                    var g = Gd[4 * k + spin[4 * t + k]];
                    double w = ft.c0.z * g.c2.x + ft.c1.z * g.c2.y + ft.c2.z * g.c2.z;
                    if (w > prune) continue;
                    fmat[n] = math.mul(ft, g);                  // F[t] rebuilt, never accumulated (§3.5)
                    depth[n] = dt + 1;
                    queue[tail++] = n;
                }
            }
        }

        // ---- crossing (Master Plan §4.6; R3 §A5) ----
        public int ExitEdge(in Iso2 local)
        {
            var p = local.M.c2;
            double s0 = p.x * chR - p.z * shR, s1 = p.y * chR - p.z * shR;
            double s2 = -p.x * chR - p.z * shR, s3 = -p.y * chR - p.z * shR;
            int k = 0; double s = s0;
            if (s1 > s) { s = s1; k = 1; }
            if (s2 > s) { s = s2; k = 2; }
            if (s3 > s) { s = s3; k = 3; }
            return s > shHyst ? k : -1;
        }

        public bool Reroot(ref WalkerPose pose)
        {
            bool moved = false;
            for (int i = 0; i < 4; i++)
            {
                int k = ExitEdge(pose.Local);
                if (k < 0) break;
                int t = IndexOf(pose.Tile);
                int n = NeighbourIndex(t, k);
                pose.Local = GInv[4 * k + spin[4 * t + k]] * pose.Local; // U = inverse(G[i]) * U (§4.6)
                pose.Tile = ids[n];
                moved = true;
            }
            return moved;
        }

        // ---- q = 6 tolerance-hash variant (R3 §A3: C = 2, S = sqrt3) ----
        static double3x3 Rot6(double3x3 m) => new double3x3(m.c1, -m.c0, m.c2); // M*R

        static double SnapEntry(double v, int row, int col)
        {
            double s = (row == 2) == (col == 2) ? 1.0 : Sqrt3;
            double a = v / s, n = Math.Round(a);
            if (Math.Abs(a - n) > 0.25) throw new InvalidOperationException("q = 6 precision exhausted");
            if (Math.Abs(n) > 2251799813685248.0) throw new OverflowException("q = 6 range exceeded");
            return n * s;
        }

        static double3x3 Snap(double3x3 m) => new double3x3(
            new double3(SnapEntry(m.c0.x, 0, 0), SnapEntry(m.c0.y, 1, 0), SnapEntry(m.c0.z, 2, 0)),
            new double3(SnapEntry(m.c1.x, 0, 1), SnapEntry(m.c1.y, 1, 1), SnapEntry(m.c1.z, 2, 1)),
            new double3(SnapEntry(m.c2.x, 0, 2), SnapEntry(m.c2.y, 1, 2), SnapEntry(m.c2.z, 2, 2)));

        static TileId Id6(double3x3 m) =>
            new TileId((long)Math.Round(m.c2.x / Sqrt3), 0, (long)Math.Round(m.c2.y / Sqrt3), 0, (long)Math.Round(m.c2.z), 0);

        static (long, long, long) Key6(double3x3 m) =>
            ((long)Math.Round(m.c0.x), (long)Math.Round(m.c0.y), (long)Math.Round(m.c0.z / Sqrt3));

        static double3x3 Canon6(double3x3 m)
        {
            if (Id6(m) == TileId.Zero) return double3x3.identity;
            var best = m; var bk = Key6(m); var r = m;
            for (int j = 1; j < 4; j++)
            {
                r = Rot6(r);
                var k = Key6(r);
                if (k.CompareTo(bk) < 0) { best = r; bk = k; }
            }
            return best;
        }

        // Descent as in §3.3 with the relative point in checked longs: R^-1 (x,y) = (y,-x); X' = T - 2X, Y' = -Y, T' = 2T - 3X.
        double3x3 Resolve6(TileId id)
        {
            try
            {
                long x = id.Xu, y = id.Yu, t = id.Tu, last = long.MaxValue;
                if (id.Xv != 0 || id.Yv != 0 || id.Tv != 0) throw new ArgumentException("not a {4,6} tile id");
                var m = double3x3.identity;
                for (int iter = 0; iter < 256; iter++)
                {
                    if (x == 0 && y == 0)
                    {
                        if (t != 1) throw new ArgumentException("not a {4,6} tile id");
                        return Canon6(m);
                    }
                    int k = 0; long best = x;
                    if (y > best) { best = y; k = 1; }
                    if (-x > best) { best = -x; k = 2; }
                    if (-y > best) { k = 3; }
                    m = Snap(math.mul(m, s6[k]));
                    for (int i = 0; i < k; i++) { long nx = y; y = -x; x = nx; }
                    long ux = x;
                    x = checked(t - 2 * ux); y = -y; t = checked(2 * t - 3 * ux);
                    if (t >= last) throw new ArgumentException("not a {4,6} tile id");
                    last = t;
                }
                throw new ArgumentException("not a {4,6} tile id");
            }
            catch (OverflowException) { throw new ArgumentException("not a {4,6} tile id (or beyond range)"); }
        }
    }
}
