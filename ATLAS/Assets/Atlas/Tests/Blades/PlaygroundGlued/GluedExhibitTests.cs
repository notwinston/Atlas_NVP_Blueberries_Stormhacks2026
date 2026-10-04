using System.Collections.Generic;
using Atlas.Blades.Playground.Exhibits;
using Atlas.Input;
using Atlas.Rendering.Room;
using Atlas.Spaces.Glued;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
#if !ATLAS_HEADLESS
using UnityEngine;
#endif

namespace Atlas.Blades.Playground.Glued.Tests
{
    /// <summary>
    /// The Glued exhibit (Master Plan tasks P2-C12, P2-C13, P2-C14): the wrap counter and glued.* events, the cell
    /// size from the RoomModel, the chiral kit on the built-in meshes, the gluing switch and the look-up in T3.
    /// None of it needs the hero meshes of kit-hero-assets: the kit works on the cell geometry of P1-A09.
    /// </summary>
    public class GluedExhibitTests
    {
        static readonly string[] s_SampleRooms = { RoomFixtures.Sample3x3, RoomFixtures.SampleL3x4, RoomFixtures.Sample4x5Table };
        static readonly string[] s_AllRooms = RoomFixtures.All;
        static readonly GluedLayout[] s_Layouts = { GluedLayout.Torus, GluedLayout.Klein, GluedLayout.Torus3 };

        sealed class RecordingLog : IEventLog
        {
            public readonly List<string> Names = new List<string>();
            public readonly List<string> Data = new List<string>();
            public void Log(string ev) { Add(ev, "{}"); }
            public void Log(string ev, string key, double value) { Add(ev, "{\"" + key + "\":" + value + "}"); }
            public void Log(string ev, string key, string value) { Add(ev, "{\"" + key + "\":\"" + value + "\"}"); }
            public void Log(string ev, string key1, double value1, string key2, double value2) { Add(ev, "{}"); }
            public void LogJson(string ev, string dataJson) { Add(ev, dataJson); }
            void Add(string ev, string data) { Names.Add(ev); Data.Add(data); }
            public int Count(string ev) { int n = 0; foreach (string name in Names) if (name == ev) n++; return n; }
        }

        static readonly quaternion s_Level = quaternion.identity;

        // Head in the middle of a cell, at eye height.
        static float3 CellHead(in GluedLattice lattice, int col, int row, int layer = 0)
        {
            double3 c = lattice.CellCentre(new GluedCell(col, row, layer));
            return new float3((float)c.x, 1.6f + (float)c.y, (float)c.z);
        }

        // Pitches the head up by `degrees`; Unity's X rotation tips the view down when it is positive.
        static quaternion Pitched(float degrees, float yawDegrees = 0f)
        {
            return math.mul(quaternion.RotateY(math.radians(yawDegrees)), quaternion.RotateX(-math.radians(degrees)));
        }

        // ---- P2-C12: the wrap counter -----------------------------------------------------------------------

        [Test]
        public void Wrap_IsLoggedOnEveryOneOfTenCrossings_AndTheCounterEqualsTheCrossings()
        {
            foreach (string name in s_AllRooms)
            {
                RoomModel room = RoomFixtures.LoadFromProject(name);
                var log = new RecordingLog();
                var core = new GluedExhibitCore(room, log);
                GluedLattice lattice = core.Lattice;

                core.Step(CellHead(in lattice, 0, 0), s_Level, GluedLayout.Torus);
                Assert.That(core.Wraps, Is.EqualTo(0), name + ": placing the walker crosses nothing");
                // Ten crossings: along +X, back, then along +Z, then back.
                int[] cols = { 1, 2, 1, 0, 0, 0, 0, 0, 0, 0 }, rows = { 0, 0, 0, 0, 1, 2, 3, 2, 1, 0 };
                for (int k = 0; k < 10; k++)
                {
                    core.Step(CellHead(in lattice, cols[k], rows[k]), s_Level, GluedLayout.Torus);
                    // Standing still in the cell adds nothing.
                    core.Step(CellHead(in lattice, cols[k], rows[k]) + new float3(0.01f, 0f, 0.01f), s_Level, GluedLayout.Torus);
                    Assert.That(core.Wraps, Is.EqualTo(k + 1), name + " after crossing " + (k + 1));
                    Assert.That(log.Count("glued.wrap"), Is.EqualTo(k + 1), name + ": glued.wrap logged for crossing " + (k + 1));
                }
                Assert.That(core.Wraps, Is.EqualTo(10), name);
                Assert.That(log.Count("glued.wrap"), Is.EqualTo(10), name + ": 10 of 10");
                StringAssert.Contains("\"n\":10", log.Data[log.Names.LastIndexOf("glued.wrap")], name);
            }
        }

        [Test]
        public void Wrap_ADiagonalStepIsOneCrossing_AndAGluingChangeIsNone()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            var log = new RecordingLog();
            var core = new GluedExhibitCore(room, log);
            GluedLattice lattice = core.Lattice;
            core.Step(CellHead(in lattice, 0, 0), s_Level, GluedLayout.Torus);
            core.Step(CellHead(in lattice, 1, 1), s_Level, GluedLayout.Torus);
            Assert.That(core.Wraps, Is.EqualTo(1));

            // The gluing changes while the walker stands, and then they walk one cell: one crossing in all.
            core.Step(CellHead(in lattice, 1, 1), s_Level, GluedLayout.Klein);
            Assert.That(core.Wraps, Is.EqualTo(1));
            Assert.That(core.Layout, Is.EqualTo(GluedLayout.Klein));
            core.Step(CellHead(in lattice, 1, 2), s_Level, GluedLayout.Klein);
            Assert.That(core.Wraps, Is.EqualTo(2));
            Assert.That(log.Count("glued.wrap"), Is.EqualTo(2));
            // Begin logs the torus, the switch logs Klein.
            Assert.That(log.Count("glued.mode"), Is.EqualTo(2));
            StringAssert.Contains("klein", log.Data[log.Names.LastIndexOf("glued.mode")]);
        }

        [Test]
        public void Wrap_PayloadCarriesTheCellTheEdgeAndTheDeterminantOfTheCopy()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            var log = new RecordingLog();
            var core = new GluedExhibitCore(room, log);
            GluedLattice lattice = new GluedLattice(room, GluedLayout.Klein);
            core.Step(CellHead(in lattice, 0, 0), s_Level, GluedLayout.Klein);
            core.Step(CellHead(in lattice, 0, 1), s_Level, GluedLayout.Klein);
            string data = log.Data[log.Names.LastIndexOf("glued.wrap")];
            StringAssert.Contains("\"edge\":\"z\"", data);
            StringAssert.Contains("\"det\":-1", data);
            StringAssert.Contains("\"row\":1", data);
        }

        [Test]
        public void CellSides_EqualTheRoomModelCellsValue()
        {
            foreach (string name in s_AllRooms)
            {
                RoomModel room = RoomFixtures.LoadFromProject(name);
                var core = new GluedExhibitCore(room, null);
                Assert.That(room.Glued.SizeX, Is.GreaterThan(0.0), name);
                Assert.That(core.CellSizeX, Is.EqualTo(room.Glued.SizeX).Within(1e-9), name);
                Assert.That(core.CellSizeZ, Is.EqualTo(room.Glued.SizeZ).Within(1e-9), name);
                Assert.That(core.Lattice.Nx, Is.EqualTo(room.Glued.Nx), name);
                Assert.That(core.Lattice.Nz, Is.EqualTo(room.Glued.Nz), name);
                // The gluing never changes the cell: all three layouts read the same sides.
                core.Step(float3.zero, s_Level, GluedLayout.Torus3);
                Assert.That(core.CellSizeX, Is.EqualTo(room.Glued.SizeX).Within(1e-9), name);
            }
        }

        [Test]
        public void Card_XModAndZMod_AreThePlaceInsideTheCell()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            var core = new GluedExhibitCore(room, null);
            GluedLattice lattice = core.Lattice;
            core.Step(CellHead(in lattice, 1, 0), s_Level, GluedLayout.Torus);
            GluedCardValues card = core.Card(CellHead(in lattice, 1, 0));
            Assert.That(card.A, Is.EqualTo(lattice.SizeX).Within(1e-9));
            Assert.That(card.XMod, Is.EqualTo(0.5 * lattice.SizeX).Within(1e-4), "the middle of a cell is a / 2 along");
            Assert.That(card.ZMod, Is.EqualTo(0.5 * lattice.SizeZ).Within(1e-4));
            Assert.That(card.Parity, Is.EqualTo(1));
        }

        // ---- the tasks --------------------------------------------------------------------------------------

        [Test]
        public void Tasks_RunInOrder_AndTheWrapWalkIsSkippedInAStandingRoom()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            var log = new RecordingLog();
            var core = new GluedExhibitCore(room, log);
            GluedLattice lattice = core.Lattice;
            Assert.That(core.WrapWalkAvailable, Is.True);
            Assert.That(core.Task, Is.EqualTo(GluedTask.Wrap));
            Assert.That(core.Prompt, Is.EqualTo(GluedText.WrapPrompt));

            core.Step(CellHead(in lattice, 0, 0), s_Level, GluedLayout.Torus);
            core.Step(CellHead(in lattice, 1, 0), s_Level, GluedLayout.Torus);
            Assert.That(core.Task, Is.EqualTo(GluedTask.Wave));
            core.Acknowledge();
            Assert.That(core.Task, Is.EqualTo(GluedTask.Klein));
            Assert.That(core.Prompt, Is.EqualTo(GluedText.KleinPrompt));

            // Klein, then a row edge: the walker stands in a mirrored copy and compares the landmarks.
            core.Step(CellHead(in lattice, 1, 0), s_Level, GluedLayout.Klein);
            Assert.That(core.Prompt, Is.EqualTo(GluedText.ChiralPrompt));
            core.Step(CellHead(in lattice, 1, 1), s_Level, GluedLayout.Klein);
            Assert.That(core.Task, Is.EqualTo(GluedTask.Torus3));

            core.Step(CellHead(in lattice, 1, 1), s_Level, GluedLayout.Torus3);
            Assert.That(core.Prompt, Is.EqualTo(GluedText.LookUpPrompt));
            core.Step(CellHead(in lattice, 1, 1), Pitched(70f), GluedLayout.Torus3);
            Assert.That(core.Task, Is.EqualTo(GluedTask.Done));
            Assert.That(core.LookedUp, Is.True);
            Assert.That(log.Count("glued.task"), Is.EqualTo(4));

            core.Begin();
            Assert.That(core.Task, Is.EqualTo(GluedTask.Wrap));
            Assert.That(core.Wraps, Is.EqualTo(0));
            Assert.That(core.Layout, Is.EqualTo(GluedLayout.Torus));

            RoomModel tiny = RoomFixtures.LoadFromProject(RoomFixtures.TinyStanding);
            var standing = new GluedExhibitCore(tiny, null);
            Assert.That(standing.WrapWalkAvailable, Is.False);
            Assert.That(standing.Task, Is.EqualTo(GluedTask.Wave));
            Assert.That(standing.Prompt, Is.EqualTo(GluedText.WrapSkipped));
        }

        // ---- P2-C13: the chiral kit -------------------------------------------------------------------------

        [Test]
        public void Chiral_DeterminantTest_PassesForEveryCopy_InEveryLayout_FromEveryWalkerCell()
        {
            foreach (string name in s_SampleRooms)
            {
                RoomModel room = RoomFixtures.LoadFromProject(name);
                foreach (GluedLayout layout in s_Layouts)
                {
                    var lattice = new GluedLattice(room, layout);
                    for (int row = -3; row <= 3; row++)
                    {
                        for (int col = -3; col <= 3; col++)
                        {
                            GluedChiralReport report = GluedChiralKit.DeterminantTest(in lattice, new GluedCell(col, row), 5);
                            Assert.That(report.Copies, Is.EqualTo(layout == GluedLayout.Torus3 ? 27 : 25), name);
                            Assert.That(report.Correct, Is.EqualTo(report.Copies), name + " " + layout + " (" + col + ", " + row + ")");
                            Assert.That(report.AllCorrect, Is.True);
                        }
                    }
                }
            }
        }

        [Test]
        public void Chiral_OnlyKleinHasMirroredCopies_AndTheLandmarkHasAHand()
        {
            var klein = new GluedLattice(3.0, 3.0, GluedLayout.Klein);
            // Rows -2 to 2 round row 0: two odd rows of five copies.
            Assert.That(GluedChiralKit.DeterminantTest(in klein, new GluedCell(0, 0), 5).Mirrored, Is.EqualTo(10));
            Assert.That(GluedChiralKit.DeterminantTest(in klein, new GluedCell(0, 1), 5).Mirrored, Is.EqualTo(15));
            foreach (GluedLayout layout in new[] { GluedLayout.Torus, GluedLayout.Torus3 })
            {
                var lattice = new GluedLattice(3.0, 3.0, layout);
                Assert.That(GluedChiralKit.DeterminantTest(in lattice, new GluedCell(0, 0), 5).Mirrored, Is.EqualTo(0), layout.ToString());
            }

            // The landmark has a hand: the stair's triple product is not zero, and a mirror in X reverses it.
            float own = GluedChiralKit.Handedness(float3x3.identity);
            float mirrored = GluedChiralKit.Handedness(new float3x3(-1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f));
            Assert.That(math.abs(own), Is.GreaterThan(1e-4f));
            Assert.That(mirrored * own, Is.LessThan(0f));
            // A turn about the vertical axis keeps it.
            Assert.That(GluedChiralKit.Handedness(new float3x3(quaternion.RotateY(1.1f))) * own, Is.GreaterThan(0f));
        }

        [Test]
        public void Chiral_AvatarIsPresentInTheTwentyFourOtherCopies_OfTheFiveByFiveLattice()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            var into = new GluedAvatarCopy[GluedRules.MaxCells * 3];
            foreach (GluedLayout layout in s_Layouts)
            {
                var lattice = new GluedLattice(room, layout);
                var walker = new GluedCell(1, 1);
                float3 head = CellHead(in lattice, 1, 1);
                int count = GluedChiralKit.AvatarCopies(in lattice, walker, 5, head, 0.7f, into);
                Assert.That(count, Is.EqualTo(layout == GluedLayout.Torus3 ? 26 : 24), layout.ToString());

                var seen = new HashSet<GluedCell>();
                for (int i = 0; i < count; i++)
                {
                    GluedAvatarCopy copy = into[i];
                    Assert.That(copy.Cell.Equals(walker), Is.False, "no copy in the walker's own cell");
                    Assert.That(seen.Add(copy.Cell), Is.True, "one copy per cell");
                    Assert.That(copy.Mirrored, Is.EqualTo(lattice.Between(walker, copy.Cell).Mirror));
                    Assert.That(copy.Heading, Is.EqualTo(copy.Mirrored ? -0.7f : 0.7f).Within(1e-6f));
                    float3 expected = lattice.ImageOf(walker, copy.Cell, head, out bool _);
                    Assert.That(math.distance(copy.Position, expected), Is.LessThan(1e-4f));
                }
            }
            // The Klein copies in odd rows are mirrored, the rest are not.
            var klein = new GluedLattice(room, GluedLayout.Klein);
            int n = GluedChiralKit.AvatarCopies(in klein, new GluedCell(0, 0), 5, CellHead(in klein, 0, 0), 0f, into);
            int mirroredCopies = 0;
            for (int i = 0; i < n; i++) if (into[i].Mirrored) mirroredCopies++;
            Assert.That(mirroredCopies, Is.EqualTo(10));
        }

        // ---- P2-C14: the gluing switch and T3 ---------------------------------------------------------------

        [Test]
        public void GluingSwitch_BumpersAndSign_StepRoundTheThreeModes()
        {
            Assert.That(GluedGluingSwitch.Next(AtlasAction.VariantNext, InputSource.Gamepad, 0, false), Is.EqualTo(1));
            Assert.That(GluedGluingSwitch.Next(AtlasAction.VariantNext, InputSource.Gaze, 1, false), Is.EqualTo(2));
            Assert.That(GluedGluingSwitch.Next(AtlasAction.VariantNext, InputSource.Gamepad, 2, false), Is.EqualTo(0));
            Assert.That(GluedGluingSwitch.Next(AtlasAction.VariantPrev, InputSource.Gamepad, 0, false), Is.EqualTo(2));
            Assert.That(GluedGluingSwitch.Next(AtlasAction.VariantPrev, InputSource.Gamepad, 2, false), Is.EqualTo(1));
            Assert.That(GluedGluingSwitch.Next(AtlasAction.Back, InputSource.Gamepad, 1, true), Is.EqualTo(-1));
        }

        [Test]
        public void GluingSwitch_LeverClick_StepsOnlyOnceTheWaveIsOver()
        {
            Assert.That(GluedGluingSwitch.Next(AtlasAction.Confirm, InputSource.Lever, 0, false), Is.EqualTo(-1));
            Assert.That(GluedGluingSwitch.Next(AtlasAction.Confirm, InputSource.Lever, 0, true), Is.EqualTo(1));
            Assert.That(GluedGluingSwitch.Next(AtlasAction.Confirm, InputSource.Gamepad, 0, true), Is.EqualTo(-1), "A is the prompt's");
        }

        [Test]
        public void T3_LookingUpPastSixtyDegrees_ShowsTheAvatar()
        {
            var lattice = new GluedLattice(3.0, 3.0, GluedLayout.Torus3);
            var walker = new GluedCell(0, 0);
            float3 head = CellHead(in lattice, 0, 0);
            foreach (float yaw in new[] { 0f, 40f, 135f, -90f })
            {
                Assert.That(GluedLookUp.AvatarVisible(in lattice, walker, head, Pitched(61f, yaw)), Is.True, "61 degrees, yaw " + yaw);
                Assert.That(GluedLookUp.AvatarVisible(in lattice, walker, head, Pitched(75f, yaw)), Is.True, "75 degrees");
                Assert.That(GluedLookUp.AvatarVisible(in lattice, walker, head, Pitched(90f, yaw)), Is.True, "straight up");
                Assert.That(GluedLookUp.AvatarVisible(in lattice, walker, head, Pitched(59f, yaw)), Is.False, "59 degrees, yaw " + yaw);
                Assert.That(GluedLookUp.AvatarVisible(in lattice, walker, head, Pitched(0f, yaw)), Is.False, "level");
                Assert.That(GluedLookUp.AvatarVisible(in lattice, walker, head, Pitched(-70f, yaw)), Is.False, "looking down is not looking up");
            }
            Assert.That(GluedLookUp.PitchOf(Pitched(61f)), Is.EqualTo(61f).Within(1e-3f));
            Assert.That(GluedLookUp.PitchOf(Pitched(-20f)), Is.EqualTo(-20f).Within(1e-3f));
        }

        [Test]
        public void T3_LookUp_FromOtherLayers_AndOtherLayouts()
        {
            var t3 = new GluedLattice(3.0, 3.0, GluedLayout.Torus3);
            Assert.That(GluedLookUp.AvatarVisible(in t3, new GluedCell(2, -1, 3), CellHead(in t3, 2, -1, 3), Pitched(80f)), Is.True);
            // The torus and the Klein bottle have no copy above.
            foreach (GluedLayout layout in new[] { GluedLayout.Torus, GluedLayout.Klein })
            {
                var lattice = new GluedLattice(3.0, 3.0, layout);
                Assert.That(GluedLookUp.AvatarVisible(in lattice, new GluedCell(0, 0), CellHead(in lattice, 0, 0), Pitched(90f)), Is.False, layout.ToString());
            }
            Assert.That(GluedExhibitCore.LookUpPitchDeg, Is.EqualTo(60f));
        }

        [Test]
        public void T3_TheLookUpIsOnlyCountedInTheThreeTorus()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            var core = new GluedExhibitCore(room, null);
            GluedLattice lattice = core.Lattice;
            core.Step(CellHead(in lattice, 0, 0), Pitched(90f), GluedLayout.Torus);
            Assert.That(core.LookedUp, Is.False);
            core.Step(CellHead(in lattice, 0, 0), Pitched(90f), GluedLayout.Klein);
            Assert.That(core.LookedUp, Is.False);
            core.Step(CellHead(in lattice, 0, 0), Pitched(90f), GluedLayout.Torus3);
            Assert.That(core.LookedUp, Is.True);
        }

        // S1 stays within its rows of section 8.5. The exhibit adds no geometry (the sign is a gaze target), so the
        // rows are those of the space. The phone's frame time and draw calls are PENDING a run in a headset.
        [Test]
        public void S1_WithTheExhibit_StaysWithinItsRowsOfSection85_InTheCountedModel()
        {
            foreach (string name in s_AllRooms)
            {
                // budget_room_120k is the full furniture load, which only Home and Curved Walk draw (mesh design
                // section 5.4). S1 shows the light copies, and stand-ins have none, so that room is no measure of S1.
                if (name == RoomFixtures.BudgetRoom120k) continue;
                RoomModel room = RoomFixtures.LoadFromProject(name);
                foreach (GluedLayout layout in s_Layouts)
                {
                    GluedDrawCost space = GluedBudget.Space(room, layout, 5);
                    Assert.That(GluedBudget.SpaceFits(layout, in space), Is.True, name + " " + layout);
                }
            }
        }

#if !ATLAS_HEADLESS
        [Test, Category("Unity")]
        public void Exhibit_BindRefusesNothing()
        {
            var go = new GameObject("GluedExhibit test");
            try
            {
                var exhibit = go.AddComponent<GluedExhibit>();
                Assert.That(exhibit.Prompt, Is.Empty);
                Assert.That(exhibit.Caption, Is.EqualTo(GluedText.Caption));
                Assert.Throws<System.ArgumentNullException>(() => exhibit.Bind(null, null, null));
                exhibit.Unbind();
                exhibit.Restart();
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
#endif
    }
}
