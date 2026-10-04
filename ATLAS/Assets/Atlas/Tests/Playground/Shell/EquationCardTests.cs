using System;
using System.Text;
using Atlas.World;
using NUnit.Framework;

namespace Atlas.Blades.Playground.Tests
{
    sealed class LaneAStub : IEquationSource
    {
        public SpaceId Space { get { return SpaceId.Slicer; } }
        public string Template { get { return "w0"; } }
        public int MaxRows { get { return 1; } }
        public int Read(EquationRow[] rows) { rows[0] = EquationRow.Number("w0", 1.0, ""); return 1; }
    }

    /// <summary>P2-C06: the equation card, its refresh rate and the sources of S0, S2, S3 and S4.</summary>
    public class EquationCardTests
    {
        static string Sig(double v)
        {
            var sb = new StringBuilder();
            EquationCard.AppendSignificant(sb, v);
            return sb.ToString();
        }

        [TestCase(1.14412, "1.14")]
        [TestCase(22.4999, "22.5")]
        [TestCase(270.0, "270")]
        [TestCase(0.031416, "0.0314")]
        [TestCase(-90.0, "-90.0")]
        [TestCase(9.996, "10.0")]
        [TestCase(999.6, "1000")]
        [TestCase(0.0, "0")]
        [TestCase(2.4, "2.40")]
        [TestCase(1234.5, "1235")]
        public void ValuesAreShownToThreeSignificantFigures(double value, string expected)
        {
            Assert.That(Sig(value), Is.EqualTo(expected));
        }

        [Test]
        public void SignificantFiguresAgreeWithTheStandardG3ForOrdinaryValues()
        {
            var rng = new Random(7);
            for (int i = 0; i < 400; i++)
            {
                double v = (rng.NextDouble() * 2 - 1) * Math.Pow(10, rng.Next(-3, 3));
                double shown = double.Parse(Sig(v), System.Globalization.CultureInfo.InvariantCulture);
                double g3 = double.Parse(v.ToString("G3", System.Globalization.CultureInfo.InvariantCulture),
                                         System.Globalization.CultureInfo.InvariantCulture);
                Assert.That(shown, Is.EqualTo(g3).Within(1e-12), "value " + v);
            }
        }

        [Test]
        public void NotANumberIsADash()
        {
            Assert.That(Sig(double.NaN), Is.EqualTo("-"));
            var sb = new StringBuilder();
            EquationCard.AppendRow(sb, EquationRow.Number("from the start flag", double.NaN, "m"));
            Assert.That(sb.ToString(), Is.EqualTo("from the start flag  -"));
        }

        [TestCase(20f)]
        [TestCase(30f)]
        [TestCase(45f)]
        [TestCase(60f)]
        [TestCase(72f)]
        [TestCase(90f)]
        [TestCase(120f)]
        public void TheCardReadsItsSourceAtLeastTenTimesASecond(float fps)
        {
            var card = new EquationCard();
            card.Bind(new CurvedWalkEquationSource(new FakeWalk()));
            long before = card.Reads;
            int frames = (int)fps * 10;
            float dt = 1f / fps;
            for (int i = 0; i < frames; i++) card.Tick(dt);
            double perSecond = (card.Reads - before) / 10.0;
            Assert.That(perSecond, Is.GreaterThanOrEqualTo(10.0), "reads per second at " + fps + " fps");
        }

        [Test]
        public void ATenHertzFloorHoldsAtTheSlowFrameRateOfTheThrottledPhone()
        {
            // The thermal ladder can drop the frame loop to 20 Hz (Master Plan section 11.2).
            var card = new EquationCard();
            card.Bind(new CurvedWalkEquationSource(new FakeWalk()));
            long before = card.Reads;
            for (int i = 0; i < 20; i++) card.Tick(1f / 20f);
            Assert.That(card.Reads - before, Is.GreaterThanOrEqualTo(10));
        }

        [Test]
        public void ALiveNumberMovesTheTextAndAStillOneDoesNot()
        {
            var walk = new FakeWalk { Armed = true, Corners = 2, DistanceToFlagM = 1.5, WorldTurnDeg = 30 };
            var card = new EquationCard();
            card.Bind(new CurvedWalkEquationSource(walk));
            int rev = card.Revision;
            for (int i = 0; i < 30; i++) card.Tick(1f / 30f);
            Assert.That(card.Revision, Is.EqualTo(rev), "nothing changed: the view is not asked to redraw");

            walk.DistanceToFlagM = 0.75;
            for (int i = 0; i < 4; i++) card.Tick(1f / 30f);
            Assert.That(card.Revision, Is.GreaterThan(rev));
            Assert.That(card.Text.ToString(), Does.Contain("from the start flag  0.750 m"));
        }

        [Test]
        public void TheCardTextCarriesTheValueToThreeFiguresAsLogged()
        {
            var walk = new FakeWalk { Armed = true, Corners = 5, Closed = true, DistanceToFlagM = 0.123456, WorldTurnDeg = 89.87654 };
            var card = new EquationCard();
            card.Bind(new CurvedWalkEquationSource(walk));
            string text = card.Text.ToString();
            Assert.That(text, Does.Contain("world turned  89.9 deg"));
            Assert.That(text, Does.Contain("from the start flag  0.123 m"));
            Assert.That(text, Does.Contain("laps  1"));
        }

        [Test]
        public void LapsCountAtQCornersNotAtFour()
        {
            var open = new FakeWalk { Armed = true, Q = 5, Corners = 4, Closed = false };
            var src = new CurvedWalkEquationSource(open);
            var card = new EquationCard();
            card.Bind(src);
            Assert.That(card.Text.ToString(), Does.Contain("laps  0"), "four right turns at q = 5 do not bring you home");
            open.Corners = 5;
            open.Closed = true;
            card.Bind(src);
            Assert.That(card.Text.ToString(), Does.Contain("laps  1"), "a fifth does");
            var six = new FakeWalk { Armed = true, Q = 6, Corners = 5, Closed = false };
            card.Bind(new CurvedWalkEquationSource(six));
            Assert.That(card.Text.ToString(), Does.Contain("laps  0"));
        }

        [Test]
        public void CurvedWalkCardShowsTheLoopRelationWithLiveLengths()
        {
            foreach (int q in new[] { 5, 6 })
            {
                double s = 2.4;
                double l = q == 5 ? s / 1.0613 : s / 1.3170;
                var src = new CurvedWalkEquationSource(new FakeWalk { Q = q, SideM = s, LengthScaleM = l });
                var rows = new EquationRow[src.MaxRows];
                int n = src.Read(rows);
                Assert.That(src.Template, Is.EqualTo("cosh(s / 2L) = sqrt(2) cos(pi / q)"));
                double lhs = double.NaN, rhs = double.NaN;
                for (int i = 0; i < n; i++)
                {
                    if (rows[i].Label == "cosh(s/2L)") lhs = rows[i].Value;
                    if (rows[i].Label == "sqrt(2) cos(pi/q)") rhs = rows[i].Value;
                }
                // The constants 1.0613 and 1.3170 are the plan's four-figure values: the sides agree to 4e-4.
                Assert.That(lhs, Is.EqualTo(rhs).Within(4e-4), "q = " + q);
            }
        }

        [Test]
        public void ExactLoopLengthsMakeBothSidesEqual()
        {
            foreach (int q in new[] { 5, 6 })
            {
                double ratio = 2.0 * Math.Acosh(CardMath.LoopRhs(q));
                Assert.That(CardMath.LoopLhs(q, ratio, 1.0), Is.EqualTo(CardMath.LoopRhs(q)).Within(1e-12));
            }
            // q = 3 on the sphere: s = 2R * pi/4 = pi R / 2 (R = 2s/pi).
            double s = 2.0, r = CardMath.PlanetRadius(s);
            Assert.That(CardMath.LoopLhs(3, s, r), Is.EqualTo(CardMath.LoopRhs(3)).Within(1e-12));
        }

        [Test]
        public void FlatDialIsNeverCalledCurved()
        {
            var walk = new FakeWalk { Q = 4, SideM = 3.0, LengthScaleM = 0 };
            var src = new CurvedWalkEquationSource(walk);
            var rows = new EquationRow[src.MaxRows];
            int n = src.Read(rows);
            var all = new StringBuilder(src.Template);
            for (int i = 0; i < n; i++) EquationCard.AppendRow(all.Append('\n'), in rows[i]);
            Assert.That(all.ToString().ToLowerInvariant(), Does.Not.Contain("curv"));
            Assert.That(src.Template.ToLowerInvariant(), Does.Not.Contain("curv"));
            Assert.That(all.ToString(), Does.Contain("flat"));
        }

        [Test]
        public void HomeCardIsFlatAndAlwaysSays180()
        {
            var src = new HomeEquationSource(new FakeWalk { Q = 4, SideM = 2.6, Armed = true, DistanceToFlagM = 0.4, WorldTurnDeg = 0 });
            var card = new EquationCard();
            card.Bind(src);
            string text = card.Text.ToString();
            Assert.That(text.ToLowerInvariant(), Does.Not.Contain("curv"));
            Assert.That(text, Does.Contain("triangle angle sum  180 deg"));
            Assert.That(text, Does.Contain("side s  2.60 m"));
            Assert.That(text, Does.Contain("world turned  0 deg"));
        }

        [Test]
        public void AlignedCellsCardHasMinus90AndPointEightNineG()
        {
            var src = new AlignedCellsEquationSource(new FakeCells { CellSideM = 1.1, Crossings = 4 });
            var rows = new EquationRow[src.MaxRows];
            int n = src.Read(rows);
            Assert.That(n, Is.EqualTo(5));
            Assert.That(rows[1].Value, Is.EqualTo(-90.0).Within(1e-9));
            Assert.That(rows[3].Value / 1.1, Is.EqualTo(0.892).Within(1e-3));
            Assert.That(rows[3].Value / 1.1, Is.EqualTo(0.89).Within(0.005), "the card says 0.89 g");
            Assert.That(rows[4].Value, Is.EqualTo(4));
        }

        [Test]
        public void SmallPlanetCardHasRadiusAngleSumAndTheHomeDistance()
        {
            double s = 2.4;
            var src = new SmallPlanetEquationSource(new FakeWalk { Q = 3, SideM = s, Armed = true, Corners = 2, DistanceToFlagM = 1.0 });
            var rows = new EquationRow[src.MaxRows];
            int n = src.Read(rows);
            Assert.That(n, Is.EqualTo(6));
            Assert.That(rows[1].Value, Is.EqualTo(2 * s / Math.PI).Within(1e-12));
            Assert.That(rows[2].Value, Is.EqualTo(270.0));
            Assert.That(rows[3].Value, Is.EqualTo(4 * s).Within(1e-12));
            Assert.That(rows[3].Value, Is.EqualTo(2 * Math.PI * rows[1].Value).Within(1e-9), "2 pi R = 4 s");
        }

        [Test]
        public void TheRegistryHoldsTheFourSourcesAndLaneAAddsTheRest()
        {
            EquationSources.Clear();
            var walk = new FakeWalk();
            EquationSources.Register(new HomeEquationSource(walk));
            EquationSources.Register(new CurvedWalkEquationSource(walk));
            EquationSources.Register(new AlignedCellsEquationSource(new FakeCells()));
            EquationSources.Register(new SmallPlanetEquationSource(walk));

            var card = new EquationCard();
            foreach (SpaceId id in new[] { SpaceId.Home, SpaceId.CurvedWalk, SpaceId.AlignedCells, SpaceId.SmallPlanet })
            {
                Assert.That(card.BindSpace(id), Is.True, id.ToString());
                Assert.That(card.Space, Is.EqualTo(id));
                Assert.That(card.Text.Length, Is.GreaterThan(0));
            }
            // S1, S5 and S6 belong to lane A (P2-A03): no source yet, no card, no failure.
            Assert.That(card.BindSpace(SpaceId.Glued), Is.False);
            Assert.That(card.IsBound, Is.False);
            Assert.That(card.Text.Length, Is.EqualTo(0));

            EquationSources.Register(new LaneAStub());
            Assert.That(card.BindSpace(SpaceId.Slicer), Is.True);
            EquationSources.Clear();
        }

        [Test]
        public void ReadingAndFormattingAllocateNothingOnceWarm()
        {
            var walk = new FakeWalk { Armed = true, Corners = 3, DistanceToFlagM = 1.0, WorldTurnDeg = 45 };
            var card = new EquationCard();
            card.Bind(new CurvedWalkEquationSource(walk));
            for (int i = 0; i < 100; i++) card.Tick(0.07f);       // warm up the JIT and the buffers
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                walk.DistanceToFlagM = 0.5 + (i % 7) * 0.1;
                card.Tick(0.07f);
            }
            long grown = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(grown, Is.LessThan(2048), "bytes allocated by 1000 refreshes");
        }
    }
}
