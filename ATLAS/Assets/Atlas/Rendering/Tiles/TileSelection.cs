// P1-B06: candidates, per-tile anchors, the draw rule and the level of detail (kernel.md 4.6, 4.7; MP 8.5, 3.4, A.3).
// Pure C#: no UnityEngine.Object, no allocation after construction.
using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.Rendering
{
    public struct TileDraw { public TileId Id; public Iso2 Anchor; public TileLod Lod; public double Distance; public int GraphDistance; }

    public sealed class TileSelection
    {
        public const int MaxDrawn = 128;               // [P1-B06 D10] >= 106 (MP 8.5, rho = 3.5, any pitch)
        public const int CandidateCapacity = 1024;     // >= 497 (max candidates, q = 6, rho = 3.5)
        public const double CandidateMargin = 0.05;    // units of L [P1-B06 D5]
        public const double NearSlack = 1e-6;          // [P1-B06 D3]
        public const double FarTierRho = 3.0;          // MP 3.4 "n = 4 only on the high tier, beyond rho = 3" [P1-B06 D4]
        public const double RhoMin = 2.0, RhoMax = 3.5, RhoDefault = 3.0;   // MP 12.4 curved.cullRho [P1-B06 D13]

        readonly ITileGraph graph;
        readonly double coshRc, coshOutOfTile, coshAlways, coshNear;
        double rho = RhoDefault, length;
        bool dirty = true;
        TileId centre;
        readonly List<VisibleTile> candidates = new List<VisibleTile>(CandidateCapacity);
        readonly TileDraw[] drawn = new TileDraw[MaxDrawn];

        public TileSelection(ITileGraph graph, double lengthScale)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (graph.Q != 5 && graph.Q != 6) throw new ArgumentOutOfRangeException(nameof(graph), "TileSelection supports q = 5 or 6");
            this.graph = graph;
            Q = graph.Q;
            CircumRadius = TileDrawRule.CircumRadius(Q);
            coshRc = Math.Cosh(CircumRadius);
            coshOutOfTile = Math.Cosh(CircumRadius + CandidateMargin);
            coshAlways = coshRc;                                      // [P1-B06 D2]
            coshNear = Math.Cosh(2 * CircumRadius) + NearSlack;       // q = 5: 2.7888544 (next class 4.2361); q = 6: 5 (next 7) [D3]
            L = lengthScale;
        }

        public int Q { get; }
        public double CircumRadius { get; }

        /// <summary>curved.cullRho, clamped to [2, 3.5]; a change forces a candidate rebuild.</summary>
        public double Rho
        {
            get => rho;
            set { double v = double.IsNaN(value) ? RhoDefault : Math.Min(RhoMax, Math.Max(RhoMin, value)); if (v != rho) { rho = v; dirty = true; } }
        }

        /// <summary>Metres, finite, at least 0.4 (B05 Scale guard).</summary>
        public double L
        {
            get => length;
            set
            {
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.4) throw new ArgumentOutOfRangeException(nameof(value), "L must be finite and at least 0.4 m");
                length = value;
            }
        }

        public TileId Centre => centre;
        public List<VisibleTile> Candidates => candidates;   // concrete List: iterate with for
        public TileDraw[] Drawn => drawn;
        public int DrawnCount { get; private set; }
        public int Overflow { get; private set; }
        public int OutOfTileFrames { get; private set; }
        public long CandidateRebuilds { get; private set; }
        public long UpdateCount { get; private set; }
        public bool LastUpdateRebuilt { get; private set; }

        /// <summary>Once per frame for both eyes (MP 8.5). Returns DrawnCount.</summary>
        public int Update(in WalkerPose pose, in TileViewCone cone)
        {
            UpdateCount++;
            LastUpdateRebuilt = false;
            if (dirty || !pose.Tile.Equals(centre))
            {
                centre = pose.Tile;
                graph.VisibleSet(centre, rho + 2 * CircumRadius + CandidateMargin, candidates);   // kernel.md 4.6 guide [D5]
                dirty = false; LastUpdateRebuilt = true; CandidateRebuilds++;
            }
            Iso2 uInv = pose.Local.Inverse();                                       // once per frame (kernel.md 4.7)
            if (pose.Local.M.c2.z > coshOutOfTile) OutOfTileFrames++;                // the caller failed to re-root; no throw
            double coshReach = Math.Cosh(rho + CircumRadius);                       // MP 8.5 (a)
            double L = length;
            int n = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                VisibleTile t = candidates[i];
                Iso2 A = uInv * t.F;                                                // cast to float happens last, at upload
                double3 p = A.M.c2;                                                 // (x, z, w): .y is the floor z
                if (p.z > coshReach) continue;                                      // (a)
                double d = Math.Acosh(Math.Max(1.0, p.z));
                bool draw = p.z <= coshAlways;                                      // (c) [D2]
                if (!draw)
                {
                    double2 lg = Geometry2.Log(p, -1);                              // d * bearing, units of L
                    double3 c = new double3(cone.Eye.x + L * lg.x, TileDrawRule.FloorY, cone.Eye.z + L * lg.y);   // same placement as AtlasWarp
                    draw = cone.ShellMayShow(c, CircumRadius * L, TileDrawRule.ShellHeight);                      // (b) [D1]
                }
                if (!draw) continue;
                if (n == MaxDrawn) { Overflow++; continue; }                        // BFS order: the farthest tiles drop
                drawn[n++] = new TileDraw { Id = t.Id, Anchor = A, Lod = LodFor(t.F.M.c2.z, d, rho, coshNear), Distance = d, GraphDistance = t.GraphDistance };
            }
            DrawnCount = n;
            return n;
        }

        /// <summary>MP 3.4, 8.4: Near on the current tile and the tiles touching it, Far only beyond rho 3 and 3 units from the walker [D3, D4].</summary>
        public static TileLod LodFor(double coshFromCentreTile, double walkerDistance, double rho, double coshNear)
        {
            if (coshFromCentreTile <= coshNear) return TileLod.Near;
            if (rho > FarTierRho && walkerDistance > 3.0) return TileLod.Far;
            return TileLod.Mid;
        }
    }
}
