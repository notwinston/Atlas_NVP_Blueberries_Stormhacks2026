using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Rendering;
using Unity.Mathematics;

namespace Atlas.World.Lint
{
    // Master Plan §3.4, §9.5.6 P1-B07, §11.1 WorldLint_All.
    public static class WorldLint
    {
        const double Phi = 1.618033988749895;

        public static WorldLintReport Run(uint seed, RoomFootprint footprint, WorldLintSettings s = null)
        {
            s = s ?? new WorldLintSettings();
            double L = footprint != null ? footprint.L : s.FallbackL;
            if (!(L > 0)) throw new ArgumentException("a footprint (or WorldLintSettings.FallbackL) is required");
            var rep = new WorldLintReport { Seed = seed, L = L };
            var g1 = new WorldGenerator(seed, footprint);
            var g2 = new WorldGenerator(seed, footprint);
            var ball = g1.Ball(s.LintDepth);
            rep.TilesLinted = ball.Count;
            if (ball.Count != 597) rep.Failures.Add($"depth-{s.LintDepth} ball has {ball.Count} tiles, expected 597");
            if (g1.TowerClass < 0) rep.Failures.Add("no tower class avoids the room footprint");
            var meshes = new TileMeshCache(L);

            CheckReachability(g1, ball, s, rep);
            CheckTowers(g1, ball, s, rep);
            CheckWedges(g1, ball, meshes, rep);
            CheckStartSpan(g1, ball, s, rep);
            CheckBudget(g1, ball, L, meshes, s, rep);
            CheckCoincidence(g1, ball, L, meshes, s, rep);
            CheckDeterminism(g1, g2, ball, s, rep);
            if (footprint != null) CheckFootprint(g1, footprint, rep);
            return rep;
        }

        // ---- 1. reachability (§3.4, §11.1) ----
        static void CheckReachability(WorldGenerator g, List<TileId> ball, WorldLintSettings s, WorldLintReport rep)
        {
            int maxClosed = 0;
            foreach (var t in ball) maxClosed = Math.Max(maxClosed, PopCount(g.Content(t).ClosedMask));
            rep.MaxClosedEdges = maxClosed;
            foreach (var t in ball)
            {
                var c = g.Content(t);
                if (c.Archetype != Archetype.Corridor) continue;
                rep.Corridors++;
                int axis = (int)((c.PropSeed >> 8) & 1); // same axis rule as WorldGenerator (§3.4)
                if ((c.ClosedMask & (1 << axis)) != 0 && (c.ClosedMask & (1 << (axis + 2))) != 0) rep.CorridorsTwoClosed++;
                else if ((c.ClosedMask & ((1 << axis) | (1 << (axis + 2)))) != 0) rep.CorridorsOneClosed++;
                for (int e = 0; e < 4; e++) if ((c.KerbMask & (1 << e)) != 0) rep.CorridorKerbs++;
                // §3.4 Corridor: both axis edges closed, unless the edge touches Home or carries a feature edge (D4)
                for (int e = axis; e < 4; e += 2)
                {
                    if ((c.ClosedMask & (1 << e)) != 0) continue;
                    var n = g.Graph.Neighbour(t, e);
                    if (g.IsHome(n) || c.FeatureEdge == e || g.Content(n).FeatureEdge == g.BackEdge(t, e)) continue;
                    rep.CorridorsMissingKerb++;
                }
            }
            if (rep.CorridorsMissingKerb > 0) rep.Failures.Add($"{rep.CorridorsMissingKerb} corridor axis edges lack their kerb (§3.4)");
            rep.Unreachable = CountUnreachable(g, ball, s.ReachDepth, t => g.Content(t).ClosedMask);
            if (rep.Unreachable > 0) rep.Failures.Add($"{rep.Unreachable} tiles unreachable from tile zero");
            if (maxClosed > 3) rep.Failures.Add($"a tile has {maxClosed} closed edges");
        }

        static int PopCount(int m) { int c = 0; for (; m != 0; m &= m - 1) c++; return c; }

        internal static int CountUnreachable(WorldGenerator g, IReadOnlyList<TileId> tiles, int reachDepth, Func<TileId, int> closedMask)
        {
            var allowed = new HashSet<TileId>(g.Ball(reachDepth));
            var seen = new HashSet<TileId> { TileId.Zero };
            var queue = new Queue<TileId>();
            queue.Enqueue(TileId.Zero);
            while (queue.Count > 0)
            {
                var t = queue.Dequeue();
                int m = closedMask(t);
                for (int e = 0; e < 4; e++)
                {
                    if ((m & (1 << e)) != 0) continue;
                    var n = g.Graph.Neighbour(t, e);
                    if (allowed.Contains(n) && seen.Add(n)) queue.Enqueue(n);
                }
            }
            int bad = 0;
            foreach (var t in tiles) if (!seen.Contains(t)) bad++;
            return bad;
        }

        // ---- 2. towers (§3.4 "within rho = 2.5") ----
        static void CheckTowers(WorldGenerator g, List<TileId> ball, WorldLintSettings s, WorldLintReport rep)
        {
            rep.WorstTowerDistance = WorstTowerDistance(g.Graph, ball, t => g.Content(t).Archetype == Archetype.Tower, s.TowerRadius);
            if (rep.WorstTowerDistance > s.TowerRadius) rep.Failures.Add($"a tile has no tower within {s.TowerRadius} (worst {rep.WorstTowerDistance})");
        }

        // Max over tiles of the distance to the nearest tower; +infinity if none lies within radius.
        internal static double WorstTowerDistance(TileGraph graph, IReadOnlyList<TileId> tiles, Func<TileId, bool> isTower, double radius)
        {
            var list = new List<VisibleTile>();
            double worst = 0;
            foreach (var t in tiles)
            {
                graph.VisibleSet(t, radius, list);
                double best = double.PositiveInfinity;
                foreach (var v in list)
                    if (isTower(v.Id)) best = Math.Min(best, Math.Acosh(Math.Max(1.0, v.F.M.c2.z))); // A.2: cosh d = w
                worst = Math.Max(worst, best);
            }
            return worst;
        }

        // ---- 3. four wedges per tile (§3.4, §3.5, A.6) ----
        static void CheckWedges(WorldGenerator g, List<TileId> ball, TileMeshCache meshes, WorldLintReport rep)
        {
            foreach (var t in ball)
            {
                var c = g.Content(t);
                bool ok = c.PostMask == 0xF && meshes.Get(TileLod.Near, c.KerbMask).AllWedges && meshes.Get(TileLod.Mid, c.KerbMask).AllWedges;
                if (!ok) rep.TilesMissingWedges++;
            }
            if (rep.TilesMissingWedges > 0) rep.Failures.Add($"{rep.TilesMissingWedges} tiles lack one of four corner wedges");
        }

        // ---- 4. start span (§3.5, §10.1.4; D3) ----
        static void CheckStartSpan(WorldGenerator g, List<TileId> ball, WorldLintSettings s, WorldLintReport rep)
        {
            var list = new List<VisibleTile>();
            g.Graph.VisibleSet(TileId.Zero, s.SpanReach + DrawRuleSim.CircumRadius, list);
            var wz = new Dictionary<TileId, double>();
            foreach (var v in list) wz[v.Id] = v.F.M.c2.z;
            double far = 0;
            foreach (var t in ball)
            {
                var c = g.Content(t);
                if (c.Archetype != Archetype.SpanA || c.FeatureEdge < 0) continue;
                var n = g.Graph.Neighbour(t, c.FeatureEdge);
                var cn = g.Content(n);
                if (cn.Archetype != Archetype.SpanB || g.Graph.Neighbour(n, cn.FeatureEdge) != t) continue;
                if (!wz.TryGetValue(t, out double wa) || !wz.TryGetValue(n, out double wb)) continue;
                rep.StartSpanPairs++;
                far = Math.Max(far, Math.Acosh(Math.Max(wa, wb)));
            }
            rep.StartSpanFarCentre = far;
            if (!g.TryGetStartSpan(out _, out _)) rep.Failures.Add("no start span could be placed");
            if (rep.StartSpanPairs < 1) rep.Failures.Add("no spanning object near the start");
        }

        // ---- 5. budget (§3.4, §8.4, §8.5; D5, D6) ----
        static long PropTris(Archetype a) => a switch
        {
            Archetype.Tower => 1500, Archetype.Garden => 600, Archetype.Doorway => 400,
            Archetype.SpanA => 200, Archetype.SpanB => 200, _ => 0,
        };

        static void CheckBudget(WorldGenerator g, List<TileId> ball, double L, TileMeshCache meshes, WorldLintSettings s, WorldLintReport rep)
        {
            var list = new List<VisibleTile>();
            var c = new double3[1024]; var tris = new long[1024];
            double phi2 = Phi * Phi;
            foreach (var t in ball)
            {
                g.Graph.VisibleSet(t, s.Rho + DrawRuleSim.CircumRadius, list);
                int m = list.Count;
                if (m > c.Length) { c = new double3[m]; tris = new long[m]; }
                for (int i = 0; i < m; i++)
                {
                    double3 P = list[i].F.M.c2;
                    double2 tc = Geometry2.Log(P, -1);
                    c[i] = new double3(L * tc.x, -s.EyeHeight, L * tc.y);
                    var lod = P.z <= phi2 + 1e-9 ? TileLod.Near : TileLod.Mid;
                    var cont = g.Content(list[i].Id);
                    tris[i] = meshes.Get(lod, cont.KerbMask).Triangles + PropTris(cont.Archetype);
                }
                for (int k = 0; k < s.Headings; k++)
                    foreach (double psi in s.PitchesDeg)
                    {
                        double theta = k * 360.0 / s.Headings;
                        int n = DrawRuleSim.Drawn(c, tris, m, theta, psi, L, s, out long sum);
                        int tileDraws = 2 * n, draws = tileDraws + s.RoomDraws;
                        long total = 2 * sum + s.RoomTris;
                        rep.MaxDrawnTiles = Math.Max(rep.MaxDrawnTiles, n);
                        rep.MaxTileDraws = Math.Max(rep.MaxTileDraws, tileDraws);
                        if (total > rep.MaxTriangles) { rep.MaxTriangles = total; rep.WorstPose = $"tile {t} theta={theta} psi={psi}"; }
                        rep.MaxDraws = Math.Max(rep.MaxDraws, draws);
                    }
            }
            if (rep.MaxDrawnTiles > s.MaxDrawnTilesPerEye) rep.Failures.Add($"budget: {rep.MaxDrawnTiles} drawn tiles per eye exceeds {s.MaxDrawnTilesPerEye}");
            if (rep.MaxTileDraws > s.TileDrawCap) rep.Failures.Add($"budget: {rep.MaxTileDraws} tile draws exceeds {s.TileDrawCap}");
            if (rep.MaxDraws > s.DrawCap) rep.Failures.Add($"budget: {rep.MaxDraws} draws exceeds {s.DrawCap}");
            if (rep.MaxDraws > s.MustDrawCap) rep.Failures.Add($"budget: {rep.MaxDraws} draws exceeds must-cap {s.MustDrawCap}");
            if (rep.MaxTriangles > s.TriCap) rep.Failures.Add($"budget: {rep.MaxTriangles} triangles exceeds {s.TriCap} (worst {rep.WorstPose})");
            if (rep.MaxTriangles > s.MustTriCap) rep.Failures.Add($"budget: {rep.MaxTriangles} triangles exceeds must-cap {s.MustTriCap}");
        }

        // ---- 6. coincident faces (§3.4) ----
        static void CheckCoincidence(WorldGenerator g, List<TileId> ball, double L, TileMeshCache meshes, WorldLintSettings s, WorldLintReport rep)
        {
            var fc = new FaceCoincidence(g.Graph, L, meshes, s.FaceTolM, s.FaceBandM);
            foreach (var t in ball)
            {
                var ct = g.Content(t);
                if (ct.Archetype == Archetype.Home && (ct.KerbMask != 0 || ct.FeatureEdge != -1)) rep.StructuralConflicts++;
                if (g.IsHome(t) != (ct.Archetype == Archetype.Home)) rep.StructuralConflicts++;
                if (g.IsHome(t) && ct.Archetype == Archetype.Tower) rep.StructuralConflicts++;
                if ((ct.Archetype == Archetype.SpanA || ct.Archetype == Archetype.SpanB))
                {
                    var p = g.Graph.Neighbour(t, ct.FeatureEdge);
                    var cp = g.Content(p);
                    var want = ct.Archetype == Archetype.SpanA ? Archetype.SpanB : Archetype.SpanA;
                    if (cp.Archetype != want || g.Graph.Neighbour(p, cp.FeatureEdge) != t) rep.StructuralConflicts++;
                }
                for (int e = 0; e < 4; e++)
                {
                    var n = g.Graph.Neighbour(t, e);
                    int e2 = g.BackEdge(t, e);
                    var cn = g.Content(n);
                    bool kt = (ct.KerbMask & (1 << e)) != 0, kn = (cn.KerbMask & (1 << e2)) != 0;
                    bool closed = (ct.ClosedMask & (1 << e)) != 0;
                    if (kt && kn) rep.StructuralConflicts++;
                    if (ct.Archetype == Archetype.Doorway && cn.Archetype == Archetype.Doorway && ct.FeatureEdge == e && cn.FeatureEdge == e2) rep.StructuralConflicts++;
                    if (closed && (ct.FeatureEdge == e || cn.FeatureEdge == e2)) rep.StructuralConflicts++;
                    if (closed != ((cn.ClosedMask & (1 << e2)) != 0)) rep.StructuralConflicts++;
                    if (TileOrder.Compare(t, n) < 0) // each shared edge once
                        rep.CoincidentFaces += fc.Count(t, e, ct.KerbMask, n, e2, cn.KerbMask);
                }
            }
            if (rep.StructuralConflicts > 0) rep.Failures.Add($"{rep.StructuralConflicts} structural conflicts between neighbours");
            if (rep.CoincidentFaces > 0) rep.Failures.Add($"{rep.CoincidentFaces} coincident triangle pairs across tile edges");
        }

        // ---- 8. room footprint (P1-B11; MP §3.4, §4.6, §9.5.6 P1-B11 "WorldLint finds no generated prop inside the room footprint") ----
        static void CheckFootprint(WorldGenerator g, RoomFootprint fp, WorldLintReport rep)
        {
            rep.FootprintTilesNotHome = FootprintViolations(g, fp, out int props, out int samples);
            rep.PropTilesInFootprint = props;
            rep.FootprintSamples = samples;
            if (rep.FootprintTilesNotHome > 0) rep.Failures.Add($"{rep.FootprintTilesNotHome} tiles under the room footprint are not flagged home (§3.4)");
            if (props > 0) rep.Failures.Add($"{props} tiles under the room footprint carry generated props (§3.4)");
        }

        public const double FootprintStepM = 0.02;   // interior grid and boundary spacing, finer than the generator's 0.05 m

        /// <summary>
        /// Samples the polygon (interior points on a 0.02 m grid, boundary points every at most 0.02 m), maps each with P = Exp(p / L, -1)
        /// and finds the containing tile as the Voronoi cell of the tile centres (minimum w of F_t^-1 P, A.2 cosh d = w); every tile within
        /// 1e-9 w of that minimum counts (a point on an edge belongs to both tiles). Returns the number of distinct such tiles that are not
        /// Home; propTiles is the number whose archetype is not Home.
        /// </summary>
        public static int FootprintViolations(WorldGenerator g, RoomFootprint fp, out int propTiles, out int samples)
        {
            if (g == null) throw new ArgumentNullException(nameof(g));
            if (fp == null) throw new ArgumentNullException(nameof(fp));
            var pts = fp.Outline;
            double L = fp.L, maxR = 0;
            double2 mn = pts[0], mx = pts[0];
            for (int i = 0; i < pts.Count; i++) { maxR = Math.Max(maxR, math.length(pts[i])); mn = math.min(mn, pts[i]); mx = math.max(mx, pts[i]); }
            var list = new List<VisibleTile>();
            g.Graph.VisibleSet(TileId.Zero, maxR / L + g.Graph.CircumRadius + 0.05, list);
            int n = list.Count;
            var rowW = new double3[n];   // third row of F_t^-1: w = row . P
            for (int i = 0; i < n; i++)
            {
                var inv = list[i].F.Inverse().M;
                rowW[i] = new double3(inv.c0.z, inv.c1.z, inv.c2.z);
            }
            var notHome = new HashSet<TileId>(); var props = new HashSet<TileId>();
            int count = 0;

            void Sample(double2 p)
            {
                count++;
                double3 P = Geometry2.Exp(p / L, -1);
                double best = double.MaxValue;
                for (int i = 0; i < n; i++) { double w = math.dot(rowW[i], P); if (w < best) best = w; }
                double tol = 1e-9 * best;
                for (int i = 0; i < n; i++)
                {
                    if (math.dot(rowW[i], P) > best + tol) continue;
                    TileId id = list[i].Id;
                    if (!g.IsHome(id)) notHome.Add(id);
                    if (g.Content(id).Archetype != Archetype.Home) props.Add(id);
                }
            }

            int nx = (int)Math.Floor((mx.x - mn.x) / FootprintStepM), nz = (int)Math.Floor((mx.y - mn.y) / FootprintStepM);
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= nz; j++)
                {
                    var p = new double2(mn.x + i * FootprintStepM, mn.y + j * FootprintStepM);
                    if (InsidePolygon(pts, p)) Sample(p);
                }
            for (int i = 0; i < pts.Count; i++)
            {
                double2 a = pts[i], b = pts[(i + 1) % pts.Count];
                int m = Math.Max(1, (int)Math.Ceiling(math.length(b - a) / FootprintStepM));
                for (int s = 0; s <= m; s++) Sample(a + (b - a) * ((double)s / m));
            }
            propTiles = props.Count;
            samples = count;
            return notHome.Count;
        }

        static bool InsidePolygon(IReadOnlyList<double2> poly, double2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                double2 a = poly[i], b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        // ---- 7. determinism (§3.4, §11.1) ----
        static void CheckDeterminism(WorldGenerator g1, WorldGenerator g2, List<TileId> ball, WorldLintSettings s, WorldLintReport rep)
        {
            var seenRegions = new bool[Regions.Count];
            for (int i = ball.Count - 1; i >= 0; i--)
            {
                var t = ball[i];
                var a = g1.Content(t); var b = g2.Content(t);
                if (Regions.RegionOf(t) != b.Region || a.Region != Regions.RegionOf(t)) rep.RegionMismatches++;
                if (a != b) rep.ContentMismatches++;
                seenRegions[a.Region] = true;
            }
            foreach (bool r in seenRegions) if (r) rep.RegionsPresent++;
            rep.WorldHash1 = g1.WorldHash(s.LintDepth);
            rep.WorldHash2 = g2.WorldHash(s.LintDepth);
            if (rep.RegionMismatches > 0) rep.Failures.Add($"{rep.RegionMismatches} tiles have a region that differs from Regions.RegionOf or between two runs");
            if (rep.ContentMismatches > 0) rep.Failures.Add($"{rep.ContentMismatches} tiles differ in content between two generator runs");
            if (rep.RegionsPresent != Regions.Count) rep.Failures.Add($"only {rep.RegionsPresent} regions present");
            if (rep.WorldHash1 != rep.WorldHash2) rep.Failures.Add("world hash differs between two runs");
        }
    }
}
