using System.Collections.Generic;
using System.Text;
using Atlas.Rendering;
using Atlas.Spaces.Glued;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Spaces.Tests
{
    /// <summary>
    /// The info card of S1 (Master Plan sections 3.1 and 10.1.3): the S1 card's numbers in words, the lattice plan as the
    /// flat twin, the step lines, the waist anchor, and the card counted in the S1 rows of section 8.5.
    /// </summary>
    public class GluedCardTests
    {
        static readonly GluedLayout[] s_Layouts = { GluedLayout.Torus, GluedLayout.Klein, GluedLayout.Torus3 };

        static GluedReadoutState State(GluedLayout layout, GluedCell cell, double xMod, double zMod, int wraps, int parity)
        {
            return new GluedReadoutState
            {
                Layout = layout,
                Copies = 5,
                Cell = cell,
                Card = new GluedCardValues { A = 1.5, B = 1.4, XMod = xMod, ZMod = zMod, Wraps = wraps, Parity = parity },
                Task = GluedTask.Wave,
                Step = GluedText.CardStep(GluedTask.Wave, layout, false),
                ShowTour = true,
            };
        }

        static void AssertFacesAgreeWithNormals(AtlasMeshBuilder mesh)
        {
            Assert.That(mesh.IndexCount % 3, Is.EqualTo(0));
            for (int t = 0; t < mesh.IndexCount; t += 3)
            {
                var a = mesh.Vertices[mesh.Indices[t]];
                var b = mesh.Vertices[mesh.Indices[t + 1]];
                var c = mesh.Vertices[mesh.Indices[t + 2]];
                float3 front = math.cross(b.Position - a.Position, c.Position - a.Position);
                Assert.That(math.length(front), Is.GreaterThan(1e-12f), "degenerate triangle " + t / 3);
                Assert.That(math.dot(front, a.Normal + b.Normal + c.Normal), Is.GreaterThan(0f), "triangle " + t / 3);
                Assert.That(a.Normal.z, Is.LessThan(0f), "the card faces -z, towards the walker");
            }
        }

        // ---- the words of the card -------------------------------------------------------------------------------

        [Test]
        public void Readout_ShowsTheGluing_TheNumbersOfTheS1Card_AndTheStep()
        {
            var sb = new StringBuilder();
            GluedReadout.Build(sb, State(GluedLayout.Klein, new GluedCell(1, 3), 0.824, 1.1, 7, -1));
            string text = sb.ToString();
            StringAssert.Contains("KLEIN BOTTLE", text);
            StringAssert.Contains("2 of 3", text);
            StringAssert.Contains("x mod a  </color>0.82 m", text);
            StringAssert.Contains("z mod b  </color>1.10 m", text);
            StringAssert.Contains("1.50 × 1.40 m", text);
            StringAssert.Contains("(1, 3)", text);
            StringAssert.Contains("wraps  </color>7", text);
            StringAssert.Contains("−1  mirrored", text);
            StringAssert.Contains("Step 2 of 4", text);

            GluedReadout.Build(sb, State(GluedLayout.Torus3, new GluedCell(0, 0, -1), 0.2, 0.3, 0, 1));
            text = sb.ToString();
            StringAssert.Contains("3-TORUS", text);
            StringAssert.Contains("(0, 0, -1)", text, "the 3-torus shows the layer");
            StringAssert.Contains("+1  ", text);
            StringAssert.Contains("same hand", text);

            // The Station runs no steps: no step line.
            GluedReadoutState station = State(GluedLayout.Torus, new GluedCell(0, 0), 0.2, 0.3, 0, 1);
            station.ShowTour = false;
            GluedReadout.BuildStep(sb, station);
            Assert.That(sb.Length, Is.EqualTo(0));
        }

        [Test]
        public void Readout_KeysChange_ExactlyWhenAShownDigitDoes()
        {
            GluedReadoutState s = State(GluedLayout.Torus, new GluedCell(2, 1), 0.501, 0.7, 3, 1);
            long numbers = GluedReadout.NumbersKey(in s), header = GluedReadout.HeaderKey(in s);

            s.Card.XMod = 0.5049;     // still 0.50 on the card
            Assert.That(GluedReadout.NumbersKey(in s), Is.EqualTo(numbers));
            s.Card.XMod = 0.512;
            Assert.That(GluedReadout.NumbersKey(in s), Is.Not.EqualTo(numbers));
            numbers = GluedReadout.NumbersKey(in s);

            s.Card.Wraps = 4;
            Assert.That(GluedReadout.NumbersKey(in s), Is.Not.EqualTo(numbers));
            numbers = GluedReadout.NumbersKey(in s);
            s.Cell = new GluedCell(2, 2);
            Assert.That(GluedReadout.NumbersKey(in s), Is.Not.EqualTo(numbers));
            numbers = GluedReadout.NumbersKey(in s);
            s.Card.Parity = -1;
            Assert.That(GluedReadout.NumbersKey(in s), Is.Not.EqualTo(numbers));

            Assert.That(GluedReadout.HeaderKey(in s), Is.EqualTo(header), "walking moves no header digit");
            s.Layout = GluedLayout.Klein;
            Assert.That(GluedReadout.HeaderKey(in s), Is.Not.EqualTo(header));
        }

        [Test]
        public void RulesAndNumbers_FitOneLineOfTheCard()
        {
            foreach (GluedLayout layout in s_Layouts)
                Assert.That(GluedReadout.Rule(layout).Length, Is.LessThanOrEqualTo(GluedReadout.MaxRuleLength), layout.ToString());

            // The widest numbers: two-digit cells, many wraps, a mirrored row in the 3-torus's layer form.
            var sb = new StringBuilder();
            GluedReadoutState s = State(GluedLayout.Torus3, new GluedCell(-12, -13, -2), 2.25, 2.25, 9999, -1);
            s.Card.A = 2.25;
            s.Card.B = 2.25;
            GluedReadout.BuildNumbers(sb, s);
            foreach (string line in sb.ToString().Split('\n'))
            {
                string shown = System.Text.RegularExpressions.Regex.Replace(line, "<[^>]*>", "");
                Assert.That(shown.Length, Is.LessThanOrEqualTo(GluedReadout.MaxNumbersLength), shown);
            }
        }

        [Test]
        public void StepLines_FitTwoLinesOfTheCard_ForEveryStepAndGluing()
        {
            var lines = new HashSet<string>();
            for (int task = 0; task <= (int)GluedTask.Done; task++)
            {
                foreach (GluedLayout layout in s_Layouts)
                {
                    foreach (bool skipped in new[] { false, true })
                    {
                        string line = GluedText.CardStep((GluedTask)task, layout, skipped);
                        Assert.That(line, Is.Not.Empty);
                        // About 49 characters a line at the card's width and the step's size.
                        Assert.That(line.Length, Is.LessThanOrEqualTo(90), line);
                        Assert.That(ReferenceEquals(line, GluedText.CardStep((GluedTask)task, layout, skipped)), Is.True,
                                    "the card compares step lines by reference: they are constants");
                        lines.Add(line);
                    }
                }
            }
            Assert.That(lines.Count, Is.GreaterThanOrEqualTo(8));
            StringAssert.Contains("Klein", GluedText.CardStep(GluedTask.Klein, GluedLayout.Torus, false));
            StringAssert.Contains("mirror", GluedText.CardStep(GluedTask.Klein, GluedLayout.Klein, false));
            StringAssert.Contains("up", GluedText.CardStep(GluedTask.Torus3, GluedLayout.Torus3, false));
        }

        // ---- the lattice plan, the flat twin ---------------------------------------------------------------------

        [Test]
        public void Plan_HasASquareAndAHandPerCopy_FramesTheFundamentalCell_AndFacesTheWalker()
        {
            var plan = new AtlasMeshBuilder(1024, 4096);
            foreach (GluedLayout layout in s_Layouts)
            {
                var lattice = new GluedLattice(3.0, 4.2, layout);
                int n = GluedRules.CopiesPerSide(layout, 5);

                // From the fundamental cell itself: its frame is in the plan.
                plan.Clear();
                GluedPlan.Build(plan, in lattice, new GluedCell(0, 0), 5, 0.15f);
                Assert.That(plan.TriangleCount, Is.EqualTo(n * n * 6 + 2), layout.ToString());
                Assert.That(plan.TriangleCount, Is.LessThanOrEqualTo(GluedPlan.MaxTriangles));
                AssertFacesAgreeWithNormals(plan);
                Assert.That(plan.BoundsSize.x, Is.LessThanOrEqualTo(0.15f + 1e-4f));
                Assert.That(plan.BoundsSize.y, Is.LessThanOrEqualTo(0.15f + 1e-4f));

                // Far from it: no frame.
                plan.Clear();
                GluedPlan.Build(plan, in lattice, new GluedCell(10, 10), 5, 0.15f);
                Assert.That(plan.TriangleCount, Is.EqualTo(n * n * 6), layout.ToString());
            }

            // Seven copies a side, the most the ladder allows, stay inside the budget of the plan's mesh.
            plan.Clear();
            var big = new GluedLattice(3.0, 3.0, GluedLayout.Klein);
            GluedPlan.Build(plan, in big, new GluedCell(0, 0), GluedRules.MaxCopies, 0.15f);
            Assert.That(plan.TriangleCount, Is.LessThanOrEqualTo(GluedPlan.MaxTriangles));
        }

        [Test]
        public void Plan_KeepsTheCellsAspect_AndTheDotSitsAtThePlaceInsideTheCell()
        {
            var lattice = new GluedLattice(3.0, 4.5, GluedLayout.Torus);     // cells 1.5 x 1.5
            var wide = new GluedLattice(4.4, 3.0, GluedLayout.Torus);        // cells 2.2 x 1.5
            float2 square = GluedPlan.Pitch(in lattice, 5, 0.15f);
            Assert.That(square.x, Is.EqualTo(0.03f).Within(1e-6f));
            Assert.That(square.y, Is.EqualTo(0.03f).Within(1e-6f));
            float2 pitch = GluedPlan.Pitch(in wide, 5, 0.15f);
            Assert.That(pitch.x / pitch.y, Is.EqualTo(wide.SizeX / wide.SizeZ).Within(1e-4));

            var middle = new GluedCardValues { A = lattice.SizeX, B = lattice.SizeZ, XMod = 0.5 * lattice.SizeX, ZMod = 0.5 * lattice.SizeZ };
            float2 dot = GluedPlan.Dot(in lattice, 5, 0.15f, in middle);
            Assert.That(math.length(dot), Is.LessThan(1e-6f), "the middle of the cell is the middle of the plan");

            var corner = new GluedCardValues { A = lattice.SizeX, B = lattice.SizeZ, XMod = lattice.SizeX, ZMod = 0.0 };
            dot = GluedPlan.Dot(in lattice, 5, 0.15f, in corner);
            Assert.That(dot.x, Is.GreaterThan(0f));
            Assert.That(dot.y, Is.LessThan(0f));
            Assert.That(math.abs(dot.x), Is.LessThanOrEqualTo(0.5f * square.x));
            Assert.That(math.abs(dot.y), Is.LessThanOrEqualTo(0.5f * square.y));

            // The arrow sits at the dot and points the way the walker faces: +y (the room's +Z) at heading 0, +x (the room's
            // +X) at a quarter turn; its winding stays a front face either way.
            var arrow = new AtlasMeshBuilder(8, 8);
            var at = new float2(0.01f, -0.02f);
            GluedPlan.BuildDot(arrow, 0.016f, AtlasPalette.Beacon, at, 0f);
            AssertFacesAgreeWithNormals(arrow);
            float3 tip = arrow.Vertices[0].Position;
            Assert.That(tip.x, Is.EqualTo(at.x).Within(1e-6f));
            Assert.That(tip.y, Is.EqualTo(at.y + 0.008f).Within(1e-6f));
            arrow.Clear();
            GluedPlan.BuildDot(arrow, 0.016f, AtlasPalette.Beacon, at, 0.5f * math.PI);
            AssertFacesAgreeWithNormals(arrow);
            tip = arrow.Vertices[0].Position;
            Assert.That(tip.x, Is.EqualTo(at.x + 0.008f).Within(1e-6f));
            Assert.That(tip.y, Is.EqualTo(at.y).Within(1e-6f));
        }

        [Test]
        public void CardValues_AreThePlaceInsideTheCell_AndTheParityOfItsRow()
        {
            var klein = new GluedLattice(3.0, 3.0, GluedLayout.Klein);
            double3 centre = klein.CellCentre(new GluedCell(1, 1));
            var head = new float3((float)centre.x + 0.2f, 1.6f, (float)centre.z - 0.3f);
            GluedCardValues v = GluedCardValues.Of(in klein, new GluedCell(1, 1), head, 2);
            Assert.That(v.XMod, Is.EqualTo(0.5 * klein.SizeX + 0.2).Within(1e-5));
            Assert.That(v.ZMod, Is.EqualTo(0.5 * klein.SizeZ - 0.3).Within(1e-5));
            Assert.That(v.Parity, Is.EqualTo(-1), "row 1 of the Klein bottle is mirrored");
            Assert.That(v.Wraps, Is.EqualTo(2));
            Assert.That(GluedCardValues.Of(in klein, new GluedCell(1, 2), head, 2).Parity, Is.EqualTo(1));
        }

        // ---- where the card is carried ---------------------------------------------------------------------------

        [Test]
        public void Anchor_CarriesTheCardAtTheWaist_AndTurnsOnlyOnceTheHeadingHasLeftItFar()
        {
            var anchor = new GluedCardAnchor();
            var head = new float3(1f, 1.6f, -0.5f);
            float3 at = anchor.Step(head, 0f, 0.016f);
            Assert.That(at.x, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(at.z, Is.EqualTo(-0.5f + GluedCardAnchor.Reach).Within(1e-5f));
            Assert.That(at.y, Is.EqualTo(1.6f - GluedCardAnchor.Drop).Within(1e-5f));

            // Looking round 30 degrees leaves it where it is; walking carries it along.
            at = anchor.Step(head + new float3(0f, 0f, 1f), math.radians(30f), 0.016f);
            Assert.That(anchor.Yaw, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(at.z, Is.EqualTo(0.5f + GluedCardAnchor.Reach).Within(1e-5f));

            // Turning 90 degrees: it follows, and settles on the heading within a second.
            for (int i = 0; i < 60; i++) anchor.Step(head, math.radians(90f), 1f / 60f);
            Assert.That(anchor.Yaw, Is.EqualTo(math.radians(90f)).Within(1e-4f));

            // Across the back (170 to -170 degrees) it takes the short way round.
            for (int i = 0; i < 120; i++) anchor.Step(head, math.radians(170f), 1f / 60f);
            float before = anchor.Yaw;
            anchor.Step(head, math.radians(-120f), 1f / 60f);
            float step = math.degrees(anchor.Yaw - before);
            Assert.That(step, Is.GreaterThan(0f), "170 to 240 degrees is the short way");

            // Crouching low never puts the card on the floor.
            anchor.Reset();
            at = anchor.Step(new float3(0f, 0.6f, 0f), 0f, 0.016f);
            Assert.That(at.y, Is.EqualTo(GluedCardAnchor.MinHeight).Within(1e-5f));
        }

        // ---- the tour starts in the gluing the walker arrives in ---------------------------------------------------

        sealed class RecordingLog : IEventLog
        {
            public readonly List<string> Lines = new List<string>();
            public void Log(string ev) { Lines.Add(ev); }
            public void Log(string ev, string key, double value) { Lines.Add(ev + " " + value); }
            public void Log(string ev, string key, string value) { Lines.Add(ev + " " + value); }
            public void Log(string ev, string key1, double value1, string key2, double value2) { Lines.Add(ev); }
            public void LogJson(string ev, string dataJson) { Lines.Add(ev + " " + dataJson); }
        }

        [Test]
        public void Tour_StartsInTheGluingGiven_AndLogsThatOne()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            var log = new RecordingLog();
            var tour = new GluedTour(room, log, GluedLayout.Klein);
            Assert.That(tour.Layout, Is.EqualTo(GluedLayout.Klein));
            Assert.That(log.Lines, Is.EqualTo(new[] { "glued.mode klein" }));
            Assert.That(tour.CardStep, Is.EqualTo(GluedText.CardStep(GluedTask.Wrap, GluedLayout.Klein, false)));
            tour.Begin(GluedLayout.Torus3);
            Assert.That(tour.Layout, Is.EqualTo(GluedLayout.Torus3));
            Assert.That(log.Lines[log.Lines.Count - 1], Is.EqualTo("glued.mode t3"));
        }

        // ---- the card in the draw count --------------------------------------------------------------------------

        [Test]
        public void Budget_TheSpaceCountsTheCard_AndStaysWithinTheS1Rows()
        {
            foreach (string name in new[] { RoomFixtures.Sample3x3, RoomFixtures.SampleL3x4, RoomFixtures.Sample4x5Table })
            {
                RoomModel room = RoomFixtures.LoadFromProject(name);
                foreach (GluedLayout layout in s_Layouts)
                {
                    GluedDrawCost lattice = GluedBudget.Lattice(room, layout, 5);
                    GluedDrawCost space = GluedBudget.Space(room, layout, 5);
                    Assert.That(space.DrawCallsPerEye - lattice.DrawCallsPerEye, Is.GreaterThanOrEqualTo(GluedBudget.CardDrawCallsPerEye));
                    Assert.That(GluedBudget.SpaceFits(layout, in space), Is.True,
                                name + " " + layout + ": " + space.DrawCallsPerFrame + " draw calls, " + space.TrianglesPerFrame + " triangles");
                }
            }
        }
    }
}
