using System.Diagnostics;
using System.IO;
using Atlas.Kernel;
using Atlas.World;
using Atlas.World.Lint;
using NUnit.Framework;
using UnityEngine;

namespace Atlas.WorldGen.Tests
{
    // P1-B11: "WorldLint finds no generated prop inside the room footprint" (MP §3.4, §9.5.6 P1-B11, §11.1 WorldLint_All)
    public class FootprintLintTests
    {
        static RoomModel Room(string name) =>
            RoomModel.Load(Path.Combine(Application.dataPath, "../../recordings", name));

        static RoomFootprint Footprint(RoomModel room)
        {
            double L = CurvatureDial.Target(5, room.curvature.L5, room.curvature.L6, room.curvature.R3, false).LengthScaleM;
            return RoomFootprint.FromRoomModel(room, L);
        }

        // Footprint_NoProps
        [TestCase("room_3x3.json", 1u)] [TestCase("room_3x3.json", 2u)] [TestCase("room_3x3.json", 3u)]
        [TestCase("room_3x4_L.json", 1u)] [TestCase("room_3x4_L.json", 2u)] [TestCase("room_3x4_L.json", 3u)]
        [TestCase("room_4x5_table.json", 1u)] [TestCase("room_4x5_table.json", 2u)] [TestCase("room_4x5_table.json", 3u)]
        [TestCase("budget_room.json", 1u)] [TestCase("budget_room.json", 2u)] [TestCase("budget_room.json", 3u)]
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
            if (name == "room_3x3.json") Assert.Greater(samples, 10000);
        }

        // WorldLint.Run reports the same fields (MP §11.1 WorldLint_All) and fails when either count is positive
        [Test]
        public void Run_ReportsFootprintFields()
        {
            var room = Room("room_3x3.json");
            var fp = Footprint(room);
            var rep = WorldLint.Run(1u, fp);
            Assert.AreEqual(0, rep.FootprintTilesNotHome); Assert.AreEqual(0, rep.PropTilesInFootprint);
            Assert.Greater(rep.FootprintSamples, 10000);
            StringAssert.Contains("FootprintSamples=", rep.ToString());
            foreach (var f in rep.Failures) StringAssert.DoesNotContain("room footprint", f);
        }

        // Control: a generator that ignores the footprint is caught
        [Test]
        public void Footprint_Control_NullFootprintFails()
        {
            var fp = Footprint(Room("room_3x3.json"));
            var g = new WorldGenerator(1u, null);
            int notHome = WorldLint.FootprintViolations(g, fp, out int props, out int samples);
            Assert.Greater(notHome, 0);
            Assert.Greater(props, 0);
        }
    }
}
