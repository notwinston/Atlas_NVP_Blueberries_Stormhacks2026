using System;
using System.IO;
using Atlas.Blades.Lung.AirwayKit;
using Atlas.Rendering;
using NUnit.Framework;

namespace Atlas.Blades.Lung.AirwayViews.Tests
{
    public class KitWingBudgetTests
    {
        static string ProjectDir => Environment.GetEnvironmentVariable("ATLAS_PROJECT_DIR") ?? Directory.GetCurrentDirectory();

        // The evidence file is written only when ATLAS_WRITE_EVIDENCE=1, so a test run does not rewrite tracked documents.
        static void WriteEvidence(string task, string file, string text)
        {
            if (Environment.GetEnvironmentVariable("ATLAS_WRITE_EVIDENCE") != "1") return;
            string d = Path.GetFullPath(Path.Combine(ProjectDir, "..", "docs", "laneB", "evidence", task));
            Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, file), text);
        }

        // Criterion: P2-B02 "wing <= 150 draw calls per frame (both eyes) at rho = 3" (D-9: conservative bound; the real count is taken at integration).
        [Test]
        public void Kit_WingDrawCalls_Rho3()
        {
            double Rc = Math.Acosh(1.0 / Math.Tan(Math.PI / 5));   // 0.8424821
            const double rho = 3;                                    // curved.cullRho default
            var tiles = RuleSLayoutForTest.Build(12);
            int N = RuleSLayoutForTest.MaxCount(tiles, 8, rho + 3 * Rc);
            int N2 = RuleSLayoutForTest.MaxCount(tiles, 8, rho + 2 * Rc);
            int draws = 2 * N + 2 + 2;
            string txt = $"P2-B02 wing draw-call bound (D-9)\ntree tiles built (Rule S, depth 12): {tiles.Count}\nR_c = {Rc:F7}, rho = {rho}\n" +
                         $"N (radius rho + 3 R_c, walker anywhere in a tree tile) = {N}\ndraw calls = 2N + 2 (valve shell) + 2 (endoluminal display) = {draws}  (limit 150)\n" +
                         $"N at radius rho + 2 R_c = {N2}\n";
            WriteEvidence("P2-B02", "wing_bound.txt", txt);
            TestContext.WriteLine("[P2-B02] " + txt);
            Assert.LessOrEqual(draws, 150);
            Assert.AreEqual(51, N, "brief expects N = 51");
            Assert.AreEqual(33, N2, "brief expects 33");
        }

        // Criterion: MP 10.2.5 airway wing row; MP 8.5 (500,000 triangles per frame).
        [Test]
        public void Kit_WingTriangles_FrameBudget()
        {
            var b = KitTestUtil.NewBuilder();
            {
                double Rc = Math.Acosh(1.0 / Math.Tan(Math.PI / 5));
                int N = RuleSLayoutForTest.MaxCount(RuleSLayoutForTest.Build(12), 8, 3 + 3 * Rc);
                double L = KitTestUtil.LFromS(3.2);
                int maxTile = 0, maxShell = 0;
                foreach (var kind in KitTestUtil.Kinds)
                {
                    maxTile = Math.Max(maxTile, b.BuildTile(kind, KitTestUtil.Spec(-1, L, TileLod.Near)).TriangleCount);
                    var sl = b.BuildValveShell(kind, 0, true, -1, L);
                    maxShell = Math.Max(maxShell, sl.IndexCount / 3);
                }
                long total = 2L * N * maxTile + 2L * maxShell + 4;
                TestContext.WriteLine($"[P2-B02] wing triangles/frame = {total} (N {N}, max tile {maxTile}, max shell {maxShell})");
                Assert.LessOrEqual(total, 500000);
            }
        }
    }
}
