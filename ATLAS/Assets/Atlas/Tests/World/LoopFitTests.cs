using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Atlas.World.Tests
{
    /// <summary>
    /// The loop-fit rule and both cell-grid rules against the worked examples of Master Plan sections 3.2, 4.3,
    /// 4.4 and 4.7, and against the values stored in every RoomModel fixture.
    /// </summary>
    public class LoopFitTests
    {
        const double BodyClearance = 0.30;

        [Test]
        public void TilingConstants_MatchTheClosedForm()
        {
            Assert.That(LoopFit.SpacingQ5, Is.EqualTo(LoopFit.CentreSpacing(5)).Within(1e-14));
            Assert.That(LoopFit.SpacingQ6, Is.EqualTo(LoopFit.CentreSpacing(6)).Within(1e-14));
            Assert.That(LoopFit.SpacingQ5, Is.EqualTo(1.0613).Within(5e-5));
            Assert.That(LoopFit.SpacingQ6, Is.EqualTo(1.3170).Within(5e-5));
            // cosh of the q = 5 spacing is the golden ratio (section 3.2).
            Assert.That(Math.Cosh(LoopFit.SpacingQ5), Is.EqualTo(0.5 * (1.0 + Math.Sqrt(5.0))).Within(1e-12));
            Assert.That(Math.Cosh(LoopFit.SpacingQ6), Is.EqualTo(2.0).Within(1e-12));
            Assert.That(LoopFit.SpacingQ3, Is.EqualTo(Math.PI / 2.0).Within(1e-15));
        }

        // Section 3.2, "Typical values": room wall to wall, s, L at q = 5, R at q = 3, L at q = 6.
        [TestCase(2.5, 2.5, 1.5, 1.41, 0.95, 1.14)]
        [TestCase(3.0, 3.0, 2.0, 1.88, 1.27, 1.52)]
        [TestCase(3.0, 4.0, 2.0, 1.88, 1.27, 1.52)]
        [TestCase(4.0, 5.0, 3.0, 2.83, 1.91, 2.28)]
        [TestCase(6.0, 8.0, 3.2, 3.02, 2.04, 2.43)]
        public void LoopFit_MatchesTheDialTable(double w, double d, double s, double l5, double r3, double l6)
        {
            LoopFitResult fit = LoopFit.Fit(w - 2 * BodyClearance, d - 2 * BodyClearance);
            Assert.That(fit.S, Is.EqualTo(s).Within(1e-9));
            Assert.That(fit.L5, Is.EqualTo(l5).Within(0.006));
            Assert.That(fit.R3, Is.EqualTo(r3).Within(0.006));
            Assert.That(fit.L6, Is.EqualTo(l6).Within(0.006));
            // Both lines of the rule give the same s in an empty rectangular room (section 4.4).
            double fromWalls = Math.Min(Math.Max(Math.Min(w, d) - 1.0, 1.0), 3.2);
            Assert.That(fit.S, Is.EqualTo(fromWalls).Within(1e-9));
            Assert.That(LoopFit.LapSideFromWalls((float)w, (float)d), Is.EqualTo((float)s).Within(1e-6f));
            Assert.That(LoopFit.LapSide((float)(w - 0.6), (float)(d - 0.6)), Is.EqualTo((float)s).Within(1e-6f));
            Assert.That(LoopFit.Radius(5, (float)s), Is.EqualTo((float)fit.L5).Within(1e-6f));
            Assert.That(LoopFit.Radius(6, (float)s), Is.EqualTo((float)fit.L6).Within(1e-6f));
            Assert.That(LoopFit.Radius(3, (float)s), Is.EqualTo((float)fit.R3).Within(1e-6f));
        }

        // Contract page, section 3: the values to six decimals.
        [TestCase(2.4, 2.4, 1.884526, 1.518651, 1.273240)]
        [TestCase(3.4, 4.4, 2.826788, 2.277977, 1.909859)]
        [TestCase(5.4, 7.4, 3.015241, 2.429842, 2.037183)]
        public void LoopFit_ReferenceValuesToSixDecimals(double wf, double df, double l5, double l6, double r3)
        {
            LoopFitResult fit = LoopFit.Fit(wf, df);
            Assert.That(fit.L5, Is.EqualTo(l5).Within(1e-6));
            Assert.That(fit.L6, Is.EqualTo(l6).Within(1e-6));
            Assert.That(fit.R3, Is.EqualTo(r3).Within(1e-6));
        }

        [Test]
        public void LoopFit_ValuesOfTheSyntheticTest()
        {
            Assert.That(LoopFit.Fit(2.4, 2.4).S, Is.EqualTo(2.0).Within(1e-9));
            Assert.That(LoopFit.Fit(2.4, 2.4).L5, Is.EqualTo(1.885).Within(0.001));
            Assert.That(LoopFit.Fit(3.4, 4.4).S, Is.EqualTo(3.0).Within(1e-9));
            Assert.That(LoopFit.Fit(3.4, 4.4).L5, Is.EqualTo(2.83).Within(0.005));
            Assert.That(LoopFit.Fit(2.4, 2.4).R3, Is.EqualTo(4.0 / Math.PI).Within(1e-12));
        }

        [Test]
        public void LapSide_IsClampedAndSymmetric()
        {
            Assert.That(LoopFit.Fit(0.5, 0.5).S, Is.EqualTo(1.0));
            Assert.That(LoopFit.Fit(1.4, 9.0).S, Is.EqualTo(1.0));
            Assert.That(LoopFit.Fit(9.0, 7.0).S, Is.EqualTo(3.2));
            Assert.That(LoopFit.Fit(2.0, 3.0).S, Is.EqualTo(LoopFit.Fit(3.0, 2.0).S));
            Assert.That(LoopFit.Fit(2.0, 3.0).S, Is.EqualTo(1.6).Within(1e-12));
            Assert.That(LoopFit.LapSide(0.5f, 0.5f), Is.EqualTo(LoopFit.LapMin));
            Assert.That(LoopFit.LapSide(9f, 7f), Is.EqualTo(LoopFit.LapMax));
            Assert.That(LoopFit.LapSide(2.4f, 3.4f), Is.EqualTo(2.0f));
        }

        [Test]
        public void Standing_BelowOnePointFiveMetres()
        {
            Assert.That(LoopFit.IsStanding(1.4, 1.4), Is.True);
            Assert.That(LoopFit.IsStanding(1.45, 3.0), Is.True);
            Assert.That(LoopFit.IsStanding(3.0, 1.45), Is.True);
            Assert.That(LoopFit.IsStanding(1.5, 1.5), Is.False);
            Assert.That(LoopFit.IsStanding(2.4, 2.4), Is.False);
            Assert.That(LoopFit.IsStanding(1.45f, 2.4f), Is.True);
            Assert.That(LoopFit.IsStanding(1.5f, 1.5f), Is.False);
        }

        [Test]
        public void DialRadius_UsesTheComfortFloorUnlessStrong()
        {
            LoopFitResult small = LoopFit.Fit(1.9, 2.4);        // s = 1.5: L5 = 1.41, L6 = 1.14, R3 = 0.95
            float s = (float)small.S;
            Assert.That(LoopFit.DialRadius(5, s), Is.EqualTo((float)small.L5));
            Assert.That(LoopFit.DialRadius(6, s), Is.EqualTo(LoopFit.FloorL));
            Assert.That(LoopFit.DialRadius(6, s, true), Is.EqualTo((float)small.L6));
            Assert.That(LoopFit.DialRadius(3, s), Is.EqualTo(LoopFit.FloorR));
            Assert.That(LoopFit.DialRadius(3, s, true), Is.EqualTo((float)small.R3));
            Assert.Throws<ArgumentOutOfRangeException>(() => LoopFit.DialRadius(4, s));
            Assert.Throws<ArgumentOutOfRangeException>(() => LoopFit.Radius(4, s));
        }

        // Section 4.3: empty rooms measured wall to wall.
        [TestCase(3.0, 3.0, 2, 2, 1.20, 1.20)]
        [TestCase(4.0, 5.0, 2, 3, 1.70, 1.47)]
        [TestCase(6.0, 8.0, 4, 5, 1.35, 1.48)]
        public void GluedCells_MatchTheWorkedExamples(double w, double d, int nx, int nz, double sizeX, double sizeZ)
        {
            GluedGrid cells = CellGridRules.Glued(w - 2 * BodyClearance, d - 2 * BodyClearance);
            Assert.That(cells.Nx, Is.EqualTo(nx));
            Assert.That(cells.Nz, Is.EqualTo(nz));
            Assert.That(cells.SizeX, Is.EqualTo(sizeX).Within(0.005));
            Assert.That(cells.SizeZ, Is.EqualTo(sizeZ).Within(0.005));
        }

        [Test]
        public void GluedCells_RoundHalfUp()
        {
            // 2.25 / 1.5 = 1.5 and 3.75 / 1.5 = 2.5 round up, not to the even neighbour.
            Assert.That(CellGridRules.Glued(2.25, 3.75).Nx, Is.EqualTo(2));
            Assert.That(CellGridRules.Glued(2.25, 3.75).Nz, Is.EqualTo(3));
        }

        [Test]
        public void GluedCells_StayBetween112And225InWalkableRooms()
        {
            for (double side = 1.5; side <= 12.0; side += 0.01)
            {
                GluedGrid cells = CellGridRules.Glued(side, side);
                Assert.That(cells.SizeX, Is.InRange(1.12, 2.25 + 1e-9), "side " + side);
                Assert.That(cells.Nx * cells.SizeX, Is.EqualTo(side).Within(1e-9));
            }
        }

        // Section 4.7: empty rooms measured wall to wall.
        [TestCase(3.0, 3.0, 3, 3, 0.80)]
        [TestCase(4.0, 5.0, 3, 3, 1.13)]
        [TestCase(6.0, 8.0, 4, 5, 1.35)]
        public void AlignedGrid_MatchesTheWorkedExamples(double w, double d, int n, int m, double g)
        {
            AlignedGrid grid = CellGridRules.Aligned(w - 2 * BodyClearance, d - 2 * BodyClearance);
            Assert.That(grid.N, Is.EqualTo(n));
            Assert.That(grid.M, Is.EqualTo(m));
            Assert.That(grid.G, Is.EqualTo(g).Within(0.005));
            // The rule does not care which side is which.
            AlignedGrid turned = CellGridRules.Aligned(d - 2 * BodyClearance, w - 2 * BodyClearance);
            Assert.That(turned.N, Is.EqualTo(n));
            Assert.That(turned.M, Is.EqualTo(m));
        }

        [Test]
        public void AlignedGrid_FallsBackToTheCornerDemo()
        {
            AlignedGrid grid = CellGridRules.Aligned(1.9, 2.4);     // 1.9 / 3 = 0.63 m is below 0.75 m
            Assert.That(grid.N, Is.EqualTo(2));
            Assert.That(grid.M, Is.EqualTo(2));
            Assert.That(grid.G, Is.EqualTo(0.95).Within(1e-12));
            Assert.That(CellGridRules.Aligned(2.25, 2.25).N, Is.EqualTo(3));   // exactly 0.75 m keeps the full grid
        }

        [Test]
        public void Fixtures_StoreWhatTheRulesGive()
        {
            string[] files = Directory.GetFiles(FixtureDirectory(), "*.json");
            Assert.That(files.Length, Is.GreaterThanOrEqualTo(4), "RoomModel fixtures in " + FixtureDirectory());
            foreach (string file in files)
            {
                string text = File.ReadAllText(file);
                string name = Path.GetFileName(file);
                double wf = Number(text, "freeRect", "wf"), df = Number(text, "freeRect", "df");
                LoopFitResult fit = LoopFit.Fit(wf, df);
                Assert.That(fit.S, Is.EqualTo(Number(text, "lapSquare", "side")).Within(1e-6), name + " s");
                Assert.That(fit.L5, Is.EqualTo(Number(text, "curvature", "l5")).Within(1e-6), name + " l5");
                Assert.That(fit.L6, Is.EqualTo(Number(text, "curvature", "l6")).Within(1e-6), name + " l6");
                Assert.That(fit.R3, Is.EqualTo(Number(text, "curvature", "r3")).Within(1e-6), name + " r3");
                Assert.That(LoopFit.FloorL, Is.EqualTo((float)Number(text, "curvature", "floorL")), name + " floorL");
                Assert.That(LoopFit.FloorR, Is.EqualTo((float)Number(text, "curvature", "floorR")), name + " floorR");
                GluedGrid glued = CellGridRules.Glued(wf, df);
                Assert.That(glued.Nx, Is.EqualTo((int)Number(text, "glued", "nx")), name + " glued nx");
                Assert.That(glued.Nz, Is.EqualTo((int)Number(text, "glued", "nz")), name + " glued nz");
                Assert.That(glued.SizeX, Is.EqualTo(Number(text, "glued", "sizeX")).Within(1e-6), name + " glued sizeX");
                Assert.That(glued.SizeZ, Is.EqualTo(Number(text, "glued", "sizeZ")).Within(1e-6), name + " glued sizeZ");
                AlignedGrid aligned = CellGridRules.Aligned(wf, df);
                Assert.That(aligned.N, Is.EqualTo((int)Number(text, "aligned", "n")), name + " aligned n");
                Assert.That(aligned.M, Is.EqualTo((int)Number(text, "aligned", "m")), name + " aligned m");
                Assert.That(aligned.G, Is.EqualTo(Number(text, "aligned", "g")).Within(1e-6), name + " aligned g");
                bool standing = Regex.Match(text, "\"standing\"\\s*:\\s*(true|false)").Groups[1].Value == "true";
                Assert.That(LoopFit.IsStanding(wf, df), Is.EqualTo(standing), name + " standing");
            }
        }

        // The number stored under "key" inside the object "section" of a RoomModel file.
        static double Number(string json, string section, string key)
        {
            Match block = Regex.Match(json, "\"" + section + "\"\\s*:\\s*\\{([^{}]*)\\}");
            Assert.That(block.Success, Is.True, "object \"" + section + "\"");
            Match value = Regex.Match(block.Groups[1].Value, "\"" + key + "\"\\s*:\\s*(-?[0-9.eE+-]+)");
            Assert.That(value.Success, Is.True, "number \"" + key + "\" in \"" + section + "\"");
            return double.Parse(value.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        // Unity runs tests with the project folder as the current directory; the headless runner names it.
        static string FixtureDirectory()
        {
            string project = Environment.GetEnvironmentVariable("ATLAS_PROJECT_DIR");
            if (string.IsNullOrEmpty(project)) project = Directory.GetCurrentDirectory();
            return Path.Combine(project, "Assets", "Atlas", "World", "Resources", "AtlasRooms");
        }
    }
}
