using System.Diagnostics;
using Atlas.Kernel;
using Atlas.World;
using Atlas.World.Lint;
using NUnit.Framework;

namespace Atlas.WorldGen.Tests
{
    // P1-B11: "WorldLint finds no generated prop inside the room footprint" (MP §3.4, §9.5.6 P1-B11, §11.1 WorldLint_All)
    public class FootprintLintTests
    {
        // Lane C's fixtures (World/Resources/AtlasRooms), read from the project folder so the test also runs headless.
        static RoomModel Room(string name) => RoomFixtures.LoadFromProject(name);

        static RoomFootprint Footprint(RoomModel room)
        {
            double L = CurvatureDial.Target(5, room.Curvature.L5, room.Curvature.L6, room.Curvature.R3, false).LengthScaleM;
            return RoomFootprint.FromRoomModel(room, L);
        }

        // Footprint_NoProps
        [TestCase(RoomFixtures.Sample3x3, 1u)] [TestCase(RoomFixtures.Sample3x3, 2u)] [TestCase(RoomFixtures.Sample3x3, 3u)]
        [TestCase(RoomFixtures.SampleL3x4, 1u)] [TestCase(RoomFixtures.SampleL3x4, 2u)] [TestCase(RoomFixtures.SampleL3x4, 3u)]
        [TestCase(RoomFixtures.Sample4x5Table, 1u)] [TestCase(RoomFixtures.Sample4x5Table, 2u)] [TestCase(RoomFixtures.Sample4x5Table, 3u)]
        [TestCase(RoomFixtures.BudgetRoom, 1u)] [TestCase(RoomFixtures.BudgetRoom, 2u)] [TestCase(RoomFixtures.BudgetRoom, 3u)]
        public void Footprint_NoProps(string name, uint seed)
        {
            var room = Room(name);
            var fp = Footprint(room);
            var sw = Stopwatch.StartNew();
            var g = new WorldGenerator(seed, fp);
            int notHome = WorldLint.FootprintViolations(g, fp, out int props, out int samples);
            TestContext.WriteLine($"{name} seed {seed}: samples {samples}, tiles not home {notHome}, prop tiles {props}, homes {g.HomeTiles.Count}, {sw.ElapsedMilliseconds} ms");
            Assert.AreEqual(0, notHome, "FootprintTilesNotHome");
            Assert.AreEqual(0, props, "PropTilesInFootprint");
            if (name == RoomFixtures.Sample3x3) Assert.Greater(samples, 10000);
        }

#if !ATLAS_HEADLESS
        // WorldLint.Run reports the same fields (MP §11.1 WorldLint_All) and fails when either count is positive
        [Test, Category("Unity")]
        public void Run_ReportsFootprintFields()
        {
            var room = Room(RoomFixtures.Sample3x3);
            var fp = Footprint(room);
            var rep = WorldLint.Run(1u, fp);
            Assert.AreEqual(0, rep.FootprintTilesNotHome); Assert.AreEqual(0, rep.PropTilesInFootprint);
            Assert.Greater(rep.FootprintSamples, 10000);
            StringAssert.Contains("FootprintSamples=", rep.ToString());
            foreach (var f in rep.Failures) StringAssert.DoesNotContain("room footprint", f);
        }
#endif

        // Control: a generator that ignores the footprint is caught
        [Test]
        public void Footprint_Control_NullFootprintFails()
        {
            var fp = Footprint(Room(RoomFixtures.Sample3x3));
            var g = new WorldGenerator(1u, null);
            int notHome = WorldLint.FootprintViolations(g, fp, out int props, out int samples);
            Assert.Greater(notHome, 0);
            Assert.Greater(props, 0);
        }
    }
}
