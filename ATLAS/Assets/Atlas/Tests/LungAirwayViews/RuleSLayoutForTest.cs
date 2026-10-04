using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.AirwayViews.Tests
{
    // Test-only Rule S tree (MP 10.2.3, App. A.5): node frame F(a+c) = F(a) S_{3|1} S_2; turn tile F(a) S_{3|1}.
    // Production code uses P2-B01's AirwayTree. Distances are in units of L.
    public static class RuleSLayoutForTest
    {
        public struct Tile { public double3 Centre; public int Depth; }

        static Iso2 S(int k) => Iso2.Rot(k * Math.PI / 2) * Iso2.Boost(new double2(2 * KitTestUtil.RIn, 0), -1) * Iso2.Rot(Math.PI);

        public static List<Tile> Build(int depth)
        {
            var tiles = new List<Tile>();
            var level = new List<Iso2> { Iso2.Identity };
            tiles.Add(new Tile { Centre = Geometry2.BasePoint, Depth = 0 });
            for (int g = 1; g <= depth; g++)
            {
                var next = new List<Iso2>(level.Count * 2);
                foreach (var F in level)
                    foreach (int e in new[] { 3, 1 })
                    {
                        var T = F * S(e); var Fn = T * S(2);
                        tiles.Add(new Tile { Centre = math.mul(T.M, Geometry2.BasePoint), Depth = g });
                        tiles.Add(new Tile { Centre = math.mul(Fn.M, Geometry2.BasePoint), Depth = g });
                        next.Add(Fn);
                    }
                level = next;
            }
            return tiles;
        }

        // max over tree tiles t with depth <= maxDepth of #{tiles c : d(t, c) <= radius}
        public static int MaxCount(List<Tile> tiles, int maxDepth, double radius)
        {
            int best = 0;
            foreach (var t in tiles)
            {
                if (t.Depth > maxDepth) continue;
                int n = 0;
                foreach (var c in tiles) if (Geometry2.Distance(t.Centre, c.Centre, -1) <= radius) n++;
                best = Math.Max(best, n);
            }
            return best;
        }
    }
}
