// [P1-B06] T5: level of detail by graph distance (MP 3.4, 8.4, A.6) [P1-B06 D3, D4].
using System;
using Atlas.Kernel;
using Atlas.Rendering;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Tiles.Tests
{
    public class TileLodTests
    {
        [Test]
        public void Lod_NearTouching_FarOnlyBeyondRho3()
        {
            var rng = new System.Random(TileTestPoses.Seed);
            foreach (int q in new[] { 5, 6 })
            {
                double h = TileChart.HalfWidth(-1, q, 1.0);
                var corners0 = new double3[4];
                int ci = 0;
                for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2) corners0[ci++] = TileChart.KleinToModel(new double2(sx * h, sy * h), -1);
                var sel = new TileSelection(new TileGraph(q), 1.885);
                var never = TileTestPoses.NeverCull();
                foreach (double rho in new[] { 3.0, 3.5 })
                {
                    sel.Rho = rho;
                    for (int it = 0; it < 200; it++)
                    {
                        var U = TileTestPoses.RandomWalker(rng, q);
                        sel.Update(new WalkerPose { Tile = TileId.Zero, Local = U }, never);
                        int near = 0, far = 0;
                        // independent Near set: tiles sharing a corner point with tile zero (Klein corners mapped by F)
                        int touching = 0;
                        var byId = new System.Collections.Generic.Dictionary<TileId, Iso2>();
                        for (int i = 0; i < sel.Candidates.Count; i++) byId[sel.Candidates[i].Id] = sel.Candidates[i].F;
                        for (int k = 0; k < sel.DrawnCount; k++)
                        {
                            var dr = sel.Drawn[k];
                            var F = byId[dr.Id];
                            bool shares = false;
                            for (int a = 0; a < 4 && !shares; a++)
                            {
                                var m = math.mul(F.M, TileChart.KleinToModel(new double2((a & 1) == 0 ? -h : h, (a & 2) == 0 ? -h : h), -1));
                                for (int b = 0; b < 4; b++) if (math.length(m - corners0[b]) < 1e-9) shares = true;
                            }
                            if (shares) touching++;
                            Assert.AreEqual(shares, dr.Lod == TileLod.Near, $"Near iff touching tile zero (q={q}, tile {dr.Id})");
                            if (dr.Lod == TileLod.Near) { near++; if (q == 5) Assert.LessOrEqual(dr.Distance, 3.0, "no Near tile beyond d = 3 (q = 5; at q = 6 the touching tiles reach 2 r_c + r_c = 3.44)"); }
                            if (dr.Lod == TileLod.Far) far++;
                            if (rho > 3.0 && dr.Lod != TileLod.Near) Assert.AreEqual(dr.Distance > 3.0, dr.Lod == TileLod.Far, "Far iff walker distance > 3.0 at rho 3.5");
                        }
                        Assert.AreEqual(q == 5 ? 13 : 17, near, $"Near count q={q}");
                        if (rho <= 3.0) Assert.AreEqual(0, far, "no Far tile at rho <= 3");
                        if (it == 0) LogTriangles(sel, q, rho);
                    }
                }
            }
        }

        // MP 8.5 planning "<= 250k triangles per frame"; logged only.
        static void LogTriangles(TileSelection sel, int q, double rho)
        {
            using var cache = new TileShellCache(q, 1.885);
            long tri = 0;
            for (int k = 0; k < sel.DrawnCount; k++) tri += cache.Get(sel.Drawn[k].Lod).GetIndexCount(0) / 3;
            string msg = $"[P1-B06 T5] q={q} rho={rho}: drawn {sel.DrawnCount}, triangles per eye {tri}, both eyes {2 * tri}";
            Debug.Log(msg); TestContext.Out.WriteLine(msg);
        }
    }
}
