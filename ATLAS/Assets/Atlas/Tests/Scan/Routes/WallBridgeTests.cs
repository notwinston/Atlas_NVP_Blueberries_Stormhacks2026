using System;
using System.Collections.Generic;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Routes
{
    /// <summary>
    /// Route 4: the walls file of the RoomPlan bridge is read, and its walls, doors and windows are laid onto
    /// the walls of a scanned room. With no file, or one that does not fit the room, the room keeps the walls
    /// of its outline.
    /// </summary>
    public class WallBridgeTests
    {
        // A 4 x 3 m room scanned as the rectangle (0,0) to (4,3) of the session frame, floor at y = 0. The
        // file is in ARKit's frame: z has the other sign. Three walls were seen (the east one 2 cm out, the
        // north one with its width axis the other way round), the west wall was not, and one "wall" stands
        // in the middle of the room. A door in the south wall, a window in the east wall, and an opening at
        // the east end of the north wall that runs 0.2 m past the corner.
        const string RoomFile =
            "{\"schema\":\"atlas.walls/1\"," +
            "\"walls\":[" +
            "{\"c\":[2,1.25,0],\"x\":[1,0,0],\"w\":4,\"h\":2.5}," +
            "{\"c\":[4.02,1.25,-1.5],\"x\":[0,0,-1],\"w\":3,\"h\":2.5}," +
            "{\"c\":[2,1.3,-3],\"x\":[-1,0,0],\"w\":4,\"h\":2.6}," +
            "{\"c\":[2,1.25,-1.5],\"x\":[1,0,0],\"w\":1,\"h\":2.5}]," +
            "\"doors\":[{\"c\":[1,1,0],\"x\":[1,0,0],\"w\":0.9,\"h\":2}]," +
            "\"windows\":[{\"c\":[4,1.5,-2],\"x\":[0,0,1],\"w\":1.2,\"h\":1}]," +
            "\"openings\":[{\"c\":[3.9,1.05,-3],\"x\":[1,0,0],\"w\":0.6,\"h\":2.1}]}";

        // The same three walls a metre to the side and turned by 30 degrees: another room.
        const string OtherRoomFile =
            "{\"schema\":\"atlas.walls/1\"," +
            "\"walls\":[" +
            "{\"c\":[3,1.25,1],\"x\":[0.866,0,0.5],\"w\":4,\"h\":2.5}," +
            "{\"c\":[5,1.25,-1.5],\"x\":[0.5,0,-0.866],\"w\":3,\"h\":2.5}]," +
            "\"doors\":[{\"c\":[1,1,0],\"x\":[1,0,0],\"w\":0.9,\"h\":2}]," +
            "\"windows\":[],\"openings\":[]}";

        const string EmptyRoomFile = "{\"schema\":\"atlas.walls/1\",\"walls\":[],\"doors\":[],\"windows\":[],\"openings\":[]}";

        static RoomModel Room()
        {
            RoomModel room = RoomModelBuilder.FromOutline("walls", ScanTestRooms.Rectangle(4f, 3f), null, new float2(2f, 1.5f));
            Assert.That(room, Is.Not.Null);
            return room;
        }

        // A floor point of the room frame in the session frame the room was scanned in.
        static float2 ToSession(RoomModel room, float2 p)
        {
            float3 s = room.Anchor.Position + math.mul(quaternion.RotateY(room.Anchor.Yaw), new float3(p.x, 0f, p.y));
            return s.xz;
        }

        // The wall of the room that runs from one session point to another.
        static RoomWall Wall(RoomModel room, float2 a, float2 b)
        {
            foreach (RoomWall wall in room.Walls)
                if (math.distance(ToSession(room, wall.A), a) < 1e-3f && math.distance(ToSession(room, wall.B), b) < 1e-3f) return wall;
            Assert.Fail("the room has no wall from " + a + " to " + b);
            return null;
        }

        static void AssertOpening(WallOpening o, OpeningKind kind, float offset, float width, float bottom, float top)
        {
            Assert.That(o.Kind, Is.EqualTo(kind));
            Assert.That(o.Offset, Is.EqualTo(offset).Within(1e-4f), "offset");
            Assert.That(o.Width, Is.EqualTo(width).Within(1e-4f), "width");
            Assert.That(o.Bottom, Is.EqualTo(bottom).Within(1e-4f), "bottom");
            Assert.That(o.Top, Is.EqualTo(top).Within(1e-4f), "top");
        }

        // ---- the file -------------------------------------------------------------------------------------

        [Test]
        public void Parse_ReadsEverySurfaceIntoTheSessionFrame()
        {
            BridgeSurface[] surfaces;
            int skipped;
            string error;
            Assert.That(WallBridgeJson.TryParse(RoomFile, out surfaces, out skipped, out error), Is.True, error);
            Assert.That(error, Is.Null);
            Assert.That(skipped, Is.EqualTo(0));
            Assert.That(surfaces.Length, Is.EqualTo(7));
            var kinds = new List<BridgeSurfaceKind>();
            foreach (BridgeSurface s in surfaces) kinds.Add(s.Kind);
            Assert.That(kinds, Is.EqualTo(new[]
            {
                BridgeSurfaceKind.Wall, BridgeSurfaceKind.Wall, BridgeSurfaceKind.Wall, BridgeSurfaceKind.Wall,
                BridgeSurfaceKind.Door, BridgeSurfaceKind.Window, BridgeSurfaceKind.Opening,
            }));
            // ARKit's z is Unity's -z, for the centre and for the direction.
            BridgeSurface east = surfaces[1];
            Assert.That(math.distance(east.Centre, new float3(4.02f, 1.25f, 1.5f)), Is.LessThan(1e-6f));
            Assert.That(math.distance(east.Along, new float2(0f, 1f)), Is.LessThan(1e-6f));
            Assert.That(east.Width, Is.EqualTo(3f));
            Assert.That(east.Height, Is.EqualTo(2.5f));
        }

        [Test]
        public void Parse_NormalisesTheDirection()
        {
            BridgeSurface[] surfaces;
            int skipped;
            string error;
            string file = "{\"schema\":\"atlas.walls/1\",\"walls\":[{\"c\":[0,1,0],\"x\":[0.6,0.02,-1.6],\"w\":2,\"h\":2,\"extra\":true}]}";
            Assert.That(WallBridgeJson.TryParse(file, out surfaces, out skipped, out error), Is.True, error);
            Assert.That(surfaces.Length, Is.EqualTo(1));
            Assert.That(math.length(surfaces[0].Along), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(surfaces[0].Along.y, Is.GreaterThan(0f));
        }

        [TestCase("{\"c\":[0,1,0],\"x\":[1,0,0],\"w\":0,\"h\":2}", TestName = "Parse_Skips_NoWidth")]
        [TestCase("{\"c\":[0,1,0],\"x\":[1,0,0],\"w\":2,\"h\":40}", TestName = "Parse_Skips_TooHigh")]
        [TestCase("{\"c\":[0,1,0],\"x\":[0,1,0],\"w\":2,\"h\":2}", TestName = "Parse_Skips_LyingFlat")]
        [TestCase("{\"c\":[0,1,0],\"x\":[0,0,0],\"w\":2,\"h\":2}", TestName = "Parse_Skips_NoDirection")]
        [TestCase("{\"c\":[0,1],\"x\":[1,0,0],\"w\":2,\"h\":2}", TestName = "Parse_Skips_ShortCentre")]
        [TestCase("{\"c\":[0,1,1e9],\"x\":[1,0,0],\"w\":2,\"h\":2}", TestName = "Parse_Skips_FarAway")]
        [TestCase("{\"c\":[0,1,0],\"x\":[1,0,0],\"w\":\"2\",\"h\":2}", TestName = "Parse_Skips_TextForANumber")]
        [TestCase("{\"c\":[0,1,0],\"x\":[1,0,0],\"h\":2}", TestName = "Parse_Skips_MissingWidth")]
        [TestCase("7", TestName = "Parse_Skips_NotAnObject")]
        public void Parse_SkipsASurfaceThatIsNotUsable(string surface)
        {
            BridgeSurface[] surfaces;
            int skipped;
            string error;
            string file = "{\"schema\":\"atlas.walls/1\",\"walls\":[" + surface + ",{\"c\":[0,1,0],\"x\":[1,0,0],\"w\":2,\"h\":2}]}";
            Assert.That(WallBridgeJson.TryParse(file, out surfaces, out skipped, out error), Is.True, error);
            Assert.That(surfaces.Length, Is.EqualTo(1));
            Assert.That(skipped, Is.EqualTo(1));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("{\"schema\":\"atlas.walls/1\",\"walls\":[")]
        [TestCase("[]")]
        [TestCase("{}")]
        [TestCase("{\"schema\":\"atlas.walls/2\",\"walls\":[]}")]
        [TestCase("{\"schema\":\"atlas.walls/1\",\"walls\":{}}")]
        public void Parse_RefusesAFileItCannotRead(string file)
        {
            BridgeSurface[] surfaces;
            int skipped;
            string error;
            Assert.That(WallBridgeJson.TryParse(file, out surfaces, out skipped, out error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
            Assert.That(surfaces, Is.Empty);
        }

        [Test]
        public void Parse_TakesAFileWithNoSurface()
        {
            BridgeSurface[] surfaces;
            int skipped;
            string error;
            Assert.That(WallBridgeJson.TryParse(EmptyRoomFile, out surfaces, out skipped, out error), Is.True, error);
            Assert.That(surfaces, Is.Empty);
            Assert.That(WallBridgeJson.TryParse("{\"schema\":\"atlas.walls/1\"}", out surfaces, out skipped, out error), Is.True, error);
            Assert.That(surfaces, Is.Empty);
        }

        // ---- the merge ------------------------------------------------------------------------------------

        [Test]
        public void Merge_TakesHeightsDoorsAndWindows()
        {
            RoomModel room = Room();
            float2[] outline = room.Outline;
            RoomWall[] before = room.Walls;

            WallBridgeReport report = WallBridgeMerge.Merge(room, RoomFile);

            Assert.That(report.Tried, Is.True);
            Assert.That(report.Error, Is.Null);
            Assert.That(report.Merged, Is.EqualTo(3));
            Assert.That(report.Unmatched, Is.EqualTo(1));
            Assert.That(report.Openings, Is.EqualTo(3));
            Assert.That(report.DroppedOpenings, Is.EqualTo(0));
            Assert.That(report.Skipped, Is.EqualTo(0));
            Assert.That(report.MaxOffset, Is.EqualTo(0.02f).Within(1e-3f));
            Assert.That(report.OffLine, Is.EqualTo(0));
            Assert.That(report.Text, Does.Contain("3 walls merged"));
            Assert.That(room.Quality.Route, Is.EqualTo(ScanRoute.WallBridge));

            // The walls stay on the outline, in its order.
            Assert.That(room.Outline, Is.SameAs(outline));
            Assert.That(room.Walls.Length, Is.EqualTo(before.Length));
            for (int k = 0; k < before.Length; k++)
            {
                Assert.That(room.Walls[k].A, Is.EqualTo(before[k].A));
                Assert.That(room.Walls[k].B, Is.EqualTo(before[k].B));
            }

            RoomWall south = Wall(room, new float2(0f, 0f), new float2(4f, 0f));
            Assert.That(south.Height, Is.EqualTo(2.5f).Within(1e-5f));
            Assert.That(south.Openings.Length, Is.EqualTo(1));
            AssertOpening(south.Openings[0], OpeningKind.Door, 0.55f, 0.9f, 0f, 2f);

            RoomWall east = Wall(room, new float2(4f, 0f), new float2(4f, 3f));
            Assert.That(east.Height, Is.EqualTo(2.5f).Within(1e-5f));
            Assert.That(east.Openings.Length, Is.EqualTo(1));
            AssertOpening(east.Openings[0], OpeningKind.Window, 1.4f, 1.2f, 1f, 2f);

            // The opening is cut at the corner the wall starts from.
            RoomWall north = Wall(room, new float2(4f, 3f), new float2(0f, 3f));
            Assert.That(north.Height, Is.EqualTo(2.6f).Within(1e-5f));
            Assert.That(north.Openings.Length, Is.EqualTo(1));
            AssertOpening(north.Openings[0], OpeningKind.Opening, 0f, 0.4f, 0f, 2.1f);

            // The wall RoomPlan did not see is the outline's.
            RoomWall west = Wall(room, new float2(0f, 3f), new float2(0f, 0f));
            Assert.That(west.Height, Is.EqualTo(RoomModel.DefaultWallHeight));
            Assert.That(west.Openings, Is.Empty);

            Assert.That(RoomModelValidator.Errors(room), Is.Empty);
        }

        [Test]
        public void Merge_SurvivesTheRoomFile()
        {
            RoomModel room = Room();
            WallBridgeMerge.Merge(room, RoomFile);
            RoomModel read = RoomModelJson.Read(RoomModelJson.Write(room));
            Assert.That(read.Quality.Route, Is.EqualTo(ScanRoute.WallBridge));
            Assert.That(read.Walls.Length, Is.EqualTo(room.Walls.Length));
            for (int k = 0; k < read.Walls.Length; k++)
            {
                Assert.That(read.Walls[k].Height, Is.EqualTo(room.Walls[k].Height));
                Assert.That(read.Walls[k].Openings, Is.EqualTo(room.Walls[k].Openings));
            }
        }

        [Test]
        public void Merge_FollowsARoomThatWasScannedTurned()
        {
            // The same room and the same capture, both turned and shifted in the session frame.
            const float angle = 0.6f;
            var shift = new float2(-1.5f, 2.25f);
            RoomModel room = RoomModelBuilder.FromOutline("walls", ScanTestRooms.Moved(ScanTestRooms.Rectangle(4f, 3f), angle, shift),
                null, ScanTestRooms.Moved(new float2(2f, 1.5f), angle, shift));
            Assert.That(room, Is.Not.Null);

            BridgeSurface[] surfaces;
            int skipped;
            string error;
            Assert.That(WallBridgeJson.TryParse(RoomFile, out surfaces, out skipped, out error), Is.True, error);
            for (int k = 0; k < surfaces.Length; k++)
            {
                float2 c = ScanTestRooms.Moved(surfaces[k].Centre.xz, angle, shift);
                surfaces[k].Centre = new float3(c.x, surfaces[k].Centre.y, c.y);
                surfaces[k].Along = ScanTestRooms.Moved(surfaces[k].Along, angle, float2.zero);
            }

            WallBridgeReport report = WallBridgeMerge.Merge(room, surfaces);

            Assert.That(report.Merged, Is.EqualTo(3));
            Assert.That(report.Openings, Is.EqualTo(3));
            RoomWall south = Wall(room, ScanTestRooms.Moved(new float2(0f, 0f), angle, shift), ScanTestRooms.Moved(new float2(4f, 0f), angle, shift));
            Assert.That(south.Openings.Length, Is.EqualTo(1));
            AssertOpening(south.Openings[0], OpeningKind.Door, 0.55f, 0.9f, 0f, 2f);
            Assert.That(RoomModelValidator.Errors(room), Is.Empty);
        }

        [Test]
        public void Merge_LeavesTheRoomAlone_WhenTheCaptureDisagreesWithTheOutline()
        {
            RoomModel room = Room();
            RoomWall[] before = room.Walls;
            ScanRoute route = room.Quality.Route;

            WallBridgeReport report = WallBridgeMerge.Merge(room, OtherRoomFile);

            Assert.That(report.Tried, Is.True);
            Assert.That(report.Merged, Is.EqualTo(0));
            Assert.That(report.Unmatched, Is.EqualTo(2));
            Assert.That(report.Openings, Is.EqualTo(0));
            Assert.That(room.Walls, Is.SameAs(before));
            Assert.That(room.Quality.Route, Is.EqualTo(route));
            Assert.That(report.Text, Does.Contain("walls from the outline"));
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("not json", true)]
        [TestCase("{}", true)]
        [TestCase(EmptyRoomFile, true)]
        public void Merge_LeavesTheRoomAlone_WhenThereIsNothingToRead(string file, bool tried)
        {
            RoomModel room = Room();
            RoomWall[] before = room.Walls;
            ScanRoute route = room.Quality.Route;

            WallBridgeReport report = WallBridgeMerge.Merge(room, file);

            Assert.That(report.Tried, Is.EqualTo(tried));
            Assert.That(report.Merged, Is.EqualTo(0));
            Assert.That(room.Walls, Is.SameAs(before));
            Assert.That(room.Quality.Route, Is.EqualTo(route));
            Assert.That(report.Text, Is.Not.Empty);
        }

        [Test]
        public void Merge_ReportsAWallBeyondThreeCentimetres()
        {
            RoomModel room = Room();
            // The south wall 5 cm outside the outline: merged all the same, and counted.
            string file = "{\"schema\":\"atlas.walls/1\",\"walls\":[{\"c\":[2,1.25,0.05],\"x\":[1,0,0],\"w\":4,\"h\":2.5}]}";
            WallBridgeReport report = WallBridgeMerge.Merge(room, file);
            Assert.That(report.Merged, Is.EqualTo(1));
            Assert.That(report.OffLine, Is.EqualTo(1));
            Assert.That(report.MaxOffset, Is.EqualTo(0.05f).Within(1e-3f));
            Assert.That(RoomModelValidator.Errors(room), Is.Empty);
        }

        [Test]
        public void Merge_KeepsTheOutlineHeight_WhenOnlyTheFootOfAWallWasSeen()
        {
            RoomModel room = Room();
            string file = "{\"schema\":\"atlas.walls/1\",\"walls\":[{\"c\":[2,0.6,0],\"x\":[1,0,0],\"w\":4,\"h\":1.2}]," +
                          "\"doors\":[{\"c\":[1,1.5,0],\"x\":[1,0,0],\"w\":0.9,\"h\":3}]}";
            WallBridgeReport report = WallBridgeMerge.Merge(room, file);
            Assert.That(report.Merged, Is.EqualTo(1));
            RoomWall south = Wall(room, new float2(0f, 0f), new float2(4f, 0f));
            Assert.That(south.Height, Is.EqualTo(RoomModel.DefaultWallHeight));
            // A door higher than the wall ends at the wall's top.
            Assert.That(south.Openings.Length, Is.EqualTo(1));
            AssertOpening(south.Openings[0], OpeningKind.Door, 0.55f, 0.9f, 0f, RoomModel.DefaultWallHeight);
            Assert.That(RoomModelValidator.Errors(room), Is.Empty);
        }

        [Test]
        public void Merge_SeparatesOpeningsThatRunIntoEachOther_AndDropsWhatHasNoWall()
        {
            RoomModel room = Room();
            string file = "{\"schema\":\"atlas.walls/1\",\"walls\":[{\"c\":[2,1.25,0],\"x\":[1,0,0],\"w\":4,\"h\":2.5}]," +
                          // Two doors that share 0.2 m, and a third wholly inside the first.
                          "\"doors\":[{\"c\":[1,1,0],\"x\":[1,0,0],\"w\":1,\"h\":2},{\"c\":[1.8,1,0],\"x\":[1,0,0],\"w\":1,\"h\":2}," +
                          "{\"c\":[1,1,0],\"x\":[1,0,0],\"w\":0.5,\"h\":2}]," +
                          // A window in the east wall, which RoomPlan gave no wall for, and one 4 cm high.
                          "\"windows\":[{\"c\":[4,1.5,-2],\"x\":[0,0,1],\"w\":1.2,\"h\":1},{\"c\":[3,1.5,0],\"x\":[1,0,0],\"w\":0.5,\"h\":0.04}]}";

            WallBridgeReport report = WallBridgeMerge.Merge(room, file);

            Assert.That(report.Merged, Is.EqualTo(1));
            Assert.That(report.Skipped, Is.EqualTo(1));             // the 4 cm window is no surface at all
            Assert.That(report.Openings, Is.EqualTo(2));
            Assert.That(report.DroppedOpenings, Is.EqualTo(2));     // the swallowed door and the window with no wall
            RoomWall south = Wall(room, new float2(0f, 0f), new float2(4f, 0f));
            Assert.That(south.Openings.Length, Is.EqualTo(2));
            AssertOpening(south.Openings[0], OpeningKind.Door, 0.5f, 1f, 0f, 2f);
            AssertOpening(south.Openings[1], OpeningKind.Door, 1.5f, 0.8f, 0f, 2f);
            Assert.That(Wall(room, new float2(4f, 0f), new float2(4f, 3f)).Openings, Is.Empty);
            Assert.That(RoomModelValidator.Errors(room), Is.Empty);
        }

        [Test]
        public void Merge_HoldsAtMostSixteenOpeningsInAWall()
        {
            RoomModel room = Room();
            var doors = new List<string>();
            for (int k = 0; k < 20; k++)
                doors.Add("{\"c\":[" + (0.1 + 0.19 * k).ToString(System.Globalization.CultureInfo.InvariantCulture) + ",1,0],\"x\":[1,0,0],\"w\":0.15,\"h\":2}");
            string file = "{\"schema\":\"atlas.walls/1\",\"walls\":[{\"c\":[2,1.25,0],\"x\":[1,0,0],\"w\":4,\"h\":2.5}],\"doors\":[" +
                          string.Join(",", doors) + "]}";
            WallBridgeReport report = WallBridgeMerge.Merge(room, file);
            Assert.That(report.Openings, Is.EqualTo(WallBridgeMerge.MaxOpeningsPerWall));
            Assert.That(report.DroppedOpenings, Is.EqualTo(4));
            Assert.That(RoomModelValidator.Errors(room), Is.Empty);
            Assert.That(RoomModelJson.Read(RoomModelJson.Write(room)).Walls.Length, Is.EqualTo(room.Walls.Length));
        }

        // ---- one scan's capture ---------------------------------------------------------------------------

        sealed class FakeBridge : IWallBridge
        {
            public readonly List<string> Calls = new List<string>();
            public bool CanStart = true, IsAvailable = true;
            public string Json = RoomFile;

            public string Name { get { return "fake"; } }
            public bool Available { get { return IsAvailable; } }
            public bool Start(IntPtr nativeSession) { Calls.Add("start"); return CanStart; }
            public string TakeJson() { Calls.Add("take"); return Json; }
            public void Stop() { Calls.Add("stop"); }
        }

        static readonly IntPtr Session = new IntPtr(1);

        [TearDown]
        public void RestoreTheBridge()
        {
            WallBridge.Register(null);
            WallBridge.Enabled = false;
        }

        [Test]
        public void Bridge_IsNeutralAndOff_UntilRegisteredAndEnabled()
        {
            Assert.That(WallBridge.Enabled, Is.False);
            Assert.That(WallBridge.Current, Is.Not.Null);
            Assert.That(WallBridge.Current.Available, Is.False);
            Assert.That(WallBridge.Current.Start(Session), Is.False);
            Assert.That(WallBridge.Current.TakeJson(), Is.EqualTo(""));

            // No bridge: a scan's calls do nothing and the room keeps the walls of its outline.
            WallBridge.Enabled = true;
            var capture = new WallBridgeCapture();
            Assert.That(capture.Begin(Session), Is.False);
            capture.End();
            RoomModel room = Room();
            RoomWall[] before = room.Walls;
            Assert.That(capture.Apply(room).Tried, Is.False);
            Assert.That(room.Walls, Is.SameAs(before));
        }

        [Test]
        public void Capture_DoesNotStart_WhenRouteFourIsOff()
        {
            var bridge = new FakeBridge();
            WallBridge.Register(bridge);
            var capture = new WallBridgeCapture();
            Assert.That(capture.Begin(Session), Is.False);
            Assert.That(capture.Running, Is.False);
            RoomModel room = Room();
            RoomWall[] before = room.Walls;
            Assert.That(capture.Apply(room).Tried, Is.False);
            Assert.That(room.Walls, Is.SameAs(before));
            Assert.That(bridge.Calls, Is.Empty);
        }

        [Test]
        public void Capture_DoesNotStart_WithoutASessionOrWhereRoomPlanIsMissing()
        {
            var bridge = new FakeBridge();
            WallBridge.Register(bridge);
            WallBridge.Enabled = true;
            var capture = new WallBridgeCapture();
            Assert.That(capture.Begin(IntPtr.Zero), Is.False);
            Assert.That(capture.Begin((ArSessionRig)null), Is.False);
            bridge.IsAvailable = false;
            Assert.That(capture.Begin(Session), Is.False);
            Assert.That(bridge.Calls, Is.Empty);
            bridge.IsAvailable = true;
            bridge.CanStart = false;
            Assert.That(capture.Begin(Session), Is.False);
            Assert.That(capture.Running, Is.False);
            Assert.That(bridge.Calls, Is.EqualTo(new[] { "start" }));
        }

        [Test]
        public void Capture_ReadsTheWallsBeforeItStops_AndMergesThemOnce()
        {
            var bridge = new FakeBridge();
            WallBridge.Register(bridge);
            WallBridge.Enabled = true;
            var capture = new WallBridgeCapture();
            Assert.That(capture.Begin(Session), Is.True);
            Assert.That(capture.Running, Is.True);
            capture.End();
            capture.End();
            Assert.That(capture.Running, Is.False);
            Assert.That(bridge.Calls, Is.EqualTo(new[] { "start", "take", "stop" }));

            RoomModel room = Room();
            WallBridgeReport report = capture.Apply(room);
            Assert.That(report.Merged, Is.EqualTo(3));
            Assert.That(room.Quality.Route, Is.EqualTo(ScanRoute.WallBridge));

            // What was seen belongs to that one room.
            RoomModel next = Room();
            Assert.That(capture.Apply(next).Tried, Is.False);
            Assert.That(next.Quality.Route, Is.EqualTo(ScanRoute.ManualOutline));
        }

        [Test]
        public void Capture_EndsARunningCapture_WhenTheRoomIsConfirmed()
        {
            var bridge = new FakeBridge();
            WallBridge.Register(bridge);
            WallBridge.Enabled = true;
            var capture = new WallBridgeCapture();
            capture.Begin(Session);
            RoomModel room = Room();
            Assert.That(capture.Apply(room).Merged, Is.EqualTo(3));
            Assert.That(bridge.Calls, Is.EqualTo(new[] { "start", "take", "stop" }));
        }

        [Test]
        public void Capture_ForgetsTheLastScan_WhenTheNextOneStarts()
        {
            var bridge = new FakeBridge();
            WallBridge.Register(bridge);
            WallBridge.Enabled = true;
            var capture = new WallBridgeCapture();
            capture.Begin(Session);
            capture.End();                              // a scan that was cancelled: its walls were never applied
            Assert.That(capture.Begin((ArSessionRig)null), Is.False);   // the next scan runs with no capture
            RoomModel room = Room();
            RoomWall[] before = room.Walls;
            Assert.That(capture.Apply(room).Tried, Is.False);
            Assert.That(room.Walls, Is.SameAs(before));
        }
    }
}
