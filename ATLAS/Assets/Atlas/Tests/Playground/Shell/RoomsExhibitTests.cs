using System.Collections.Generic;
using Atlas.Blades.Playground.Exhibits;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Spaces.Portal;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Blades.Playground.Tests
{
    /// <summary>
    /// The Impossible Rooms exhibit (Master Plan tasks P2-C15 and P2-C16): the physical fit of every layout on
    /// the three sample RoomModels, the loop rule min(Wf, Df) at least 2.4 m, the room number after four
    /// turns, the signs, and the exhibit's tasks and fallback prompt.
    /// </summary>
    public class RoomsExhibitTests
    {
        static readonly string[] s_SampleRooms = { RoomFixtures.Sample3x3, RoomFixtures.SampleL3x4, RoomFixtures.Sample4x5Table };

        /// <summary>A floor that is a plain rectangle round the origin.</summary>
        sealed class RectFloor : IPortalFloor
        {
            readonly float2 m_Half;
            public RectFloor(float2 half) { m_Half = half; }
            public bool Contains(float2 xz) { return math.abs(xz.x) <= m_Half.x && math.abs(xz.y) <= m_Half.y; }
        }

        static PortalLayout Plain(float wf, float df, PortalLayoutKind kind, int rooms, uint seed, bool standing = false)
        {
            var request = new PortalLayoutRequest
            {
                Wf = wf, Df = df, DoorHeight = 2.1f, Seed = seed, Layout = kind, LoopRooms = rooms, Standing = standing,
                Floor = new RectFloor(new float2(0.5f * wf + 0.01f, 0.5f * df + 0.01f)),
            };
            return PortalLayoutGenerator.Generate(in request);
        }

        // ---- P2-C15: physical fit ---------------------------------------------------------------------------

        [Test]
        public void Fit_EveryFootprintIsInsideTheErodedFloor_ForThreeRoomModels()
        {
            foreach (string name in s_SampleRooms)
            {
                RoomModel room = RoomFixtures.LoadFromProject(name);
                RoomsFitReport report = RoomsFitLint.Check(room);
                Assert.That(report.Layouts, Is.GreaterThan(20), name + ": booth and loop, every room count and seed variant");
                Assert.That(report.Failures, Is.Empty, name);
                Assert.That(report.FitPercent, Is.EqualTo(100f), name + ": physical fit 100%");
                Assert.That(report.AllFit, Is.True, name);
                Assert.That(report.RealRoomOnly, Is.EqualTo(0), name + ": every sample room holds a layout");
            }
        }

        [Test]
        public void Fit_ControlLintCatchesAFootprintOutsideTheFloor()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            PortalLayout loop = Plain(room.FreeRect.Wf, room.FreeRect.Df, PortalLayoutKind.Loop, 6, 1);
            Assert.That(loop.Kind, Is.EqualTo(PortalLayoutKind.Loop));
            string why;
            Assert.That(RoomsFitLint.FootprintsInside(loop, room, new RoomModelFloor(room), out why), Is.True, why);
            // A floor half as wide as the free rectangle claims: the corridor leaves it.
            var narrow = new RectFloor(new float2(0.25f * room.FreeRect.Wf, 0.5f * room.FreeRect.Df));
            Assert.That(RoomsFitLint.FootprintsInside(loop, room, narrow, out why), Is.False);
            Assert.That(why, Does.Contain("eroded floor"));
            // A layout made for another room.
            PortalLayout other = Plain(room.FreeRect.Wf + 1f, room.FreeRect.Df, PortalLayoutKind.Loop, 6, 1);
            Assert.That(RoomsFitLint.FootprintsInside(other, room, new RoomModelFloor(room), out why), Is.False);
        }

        [Test]
        public void Fit_NoRoomModelIsReportedNotThrown()
        {
            RoomsFitReport report = RoomsFitLint.Check(null);
            Assert.That(report.AllFit, Is.False);
            Assert.That(report.Failures, Is.Not.Empty);
        }

        // ---- P2-C16: the fallback rule ----------------------------------------------------------------------

        [Test]
        public void LoopRule_FallbackIsChosenExactlyWhenMinWfDfIsUnder2_4()
        {
            int loops = 0, booths = 0;
            for (float wf = 1.2f; wf <= 4.0f; wf += 0.05f)
            {
                for (float df = wf; df <= 4.6f; df += 0.1f)
                {
                    float w = math.round(wf * 20f) / 20f, d = math.round(df * 20f) / 20f;
                    bool fits = math.min(w, d) >= 2.4f - 1e-4f;
                    Assert.That(RoomsExhibitCore.LoopFits(w, d, false), Is.EqualTo(fits), w + " x " + d);
                    PortalLayout layout = Plain(w, d, PortalLayoutKind.Loop, 6, 7);
                    string what = w + " x " + d;
                    if (fits)
                    {
                        Assert.That(layout.Kind, Is.EqualTo(PortalLayoutKind.Loop), what);
                        Assert.That(layout.IsFallback, Is.False, what);
                        loops++;
                    }
                    else
                    {
                        Assert.That(layout.Kind, Is.EqualTo(PortalLayoutKind.BoothHall), what);
                        Assert.That(layout.IsFallback, Is.True, what + ": the booth is a fallback");
                        booths++;
                    }
                }
            }
            Assert.That(loops, Is.GreaterThan(100));
            Assert.That(booths, Is.GreaterThan(100));
            Assert.That(RoomsExhibitCore.LoopFits(3.2f, 3.2f, true), Is.False, "standing mode never walks the loop");
            Assert.That(RoomsExhibitCore.BlockFor(3.2f, 3.2f, true), Is.EqualTo(RoomsLoopBlock.Standing));
            Assert.That(RoomsExhibitCore.BlockFor(2.3f, 5f, false), Is.EqualTo(RoomsLoopBlock.TooSmall));
            Assert.That(PortalLayoutGenerator.MinLoopSide, Is.EqualTo(2.4f));
        }

        [Test]
        public void LoopRule_SampleRoomsChooseAsTheirFreeRectangleAllows()
        {
            RoomModel square = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            Assert.That(new RoomsExhibitCore(square).LoopAvailable, Is.True, "2.4 x 2.4 holds the loop");
            foreach (string name in new[] { RoomFixtures.SampleL3x4, RoomFixtures.Sample4x5Table, RoomFixtures.TinyStanding })
            {
                RoomModel room = RoomFixtures.LoadFromProject(name);
                var core = new RoomsExhibitCore(room);
                Assert.That(core.LoopAvailable, Is.False, name);
                Assert.That(core.LoopBlock, Is.Not.EqualTo(RoomsLoopBlock.None), name);
            }
        }

        // ---- P2-C16: the room number after four turns -------------------------------------------------------

        // Walks a loop layout the way the stencil walker does: through the entrance, then every exit in turn.
        static List<KeyValuePair<int, int>> LoopWalk(PortalLayout layout, int crossings)
        {
            var walk = new List<KeyValuePair<int, int>>(crossings + 1);
            int room = layout.StartRoom;
            int entrance = -1;
            for (int p = 0; p < layout.Portals.Count; p++)
                if (layout.Portals[p].From == 0) entrance = p;
            Assert.That(entrance, Is.GreaterThanOrEqualTo(0), "the real room has a door");
            walk.Add(new KeyValuePair<int, int>(room, layout.Portals[entrance].To));
            room = layout.Portals[entrance].To;
            for (int k = 0; k < crossings; k++)
            {
                int to = layout.Portals[layout.Rooms[room].ExitPortal].To;
                walk.Add(new KeyValuePair<int, int>(room, to));
                room = to;
            }
            return walk;
        }

        [Test]
        public void Loop_NumberAfterFourTurnsDiffersFromTheStart_10Of10()
        {
            int runs = 0;
            for (int rooms = 2; rooms <= 6; rooms++)
            {
                for (uint seed = 0; seed < 2; seed++)
                {
                    PortalLayout layout = Plain(2.4f + 0.3f * seed, 2.6f, PortalLayoutKind.Loop, rooms, seed);
                    Assert.That(layout.Kind, Is.EqualTo(PortalLayoutKind.Loop));
                    var tracker = new RoomsLoopTracker();
                    tracker.Reset(layout);
                    bool lap = false;
                    foreach (KeyValuePair<int, int> step in LoopWalk(layout, 12))
                    {
                        lap = tracker.OnRoomChanged(step.Key, step.Value);
                        if (lap) break;
                    }
                    string what = rooms + " rooms, seed " + seed;
                    Assert.That(lap, Is.True, what + ": four right turns finish a lap");
                    RoomsLoopLap result = tracker.LastLap;
                    Assert.That(result.StartNumber, Is.EqualTo(1), what);
                    Assert.That(result.NumberChanged, Is.True, what + ": number after four turns differs from the start");
                    // A lap is counted at a doorway: at exactly four turns except where a room's corners carry the count past it.
                    Assert.That(result.TurnsWalked, Is.GreaterThanOrEqualTo(4), what);
                    Assert.That(result.TurnsWalked, Is.EqualTo(rooms == 3 ? 6 : 4), what);
                    Assert.That(result.ArrivalNumber, Is.EqualTo(layout.Rooms[layout.RoomAfterRightTurns(1, result.TurnsWalked)].Number), what);
                    if (result.TurnsWalked == 4) Assert.That(result.ArrivalNumber, Is.EqualTo(RoomsLoopTracker.NumberAfterFourTurns(layout, 1)), what);
                    Assert.That(result.ArrivalNumber, Is.EqualTo(layout.Rooms[result.ArrivalRoom].Number), what);
                    runs++;
                }
            }
            Assert.That(runs, Is.EqualTo(10));
        }

        [Test]
        public void Loop_EveryStartingRoomEndsInAnotherRoomAfterFourTurns()
        {
            for (int rooms = 2; rooms <= 6; rooms++)
            {
                PortalLayout layout = Plain(3.0f, 3.0f, PortalLayoutKind.Loop, rooms, 3);
                for (int start = 1; start < layout.RoomCount; start++)
                    Assert.That(RoomsLoopTracker.NumberAfterFourTurns(layout, start), Is.Not.EqualTo(layout.Rooms[start].Number),
                        rooms + " rooms, from room " + start);
            }
            Assert.That(RoomsLoopTracker.NumberAfterFourTurns(null, 1), Is.EqualTo(0));
            Assert.That(RoomsLoopTracker.NumberAfterFourTurns(Plain(3f, 3f, PortalLayoutKind.Loop, 4, 1), 0), Is.EqualTo(0), "room 0 is not a corridor room");
        }

        [Test]
        public void Loop_TrackerCountsLapsAtTheCrossingWhereThePortalSpaceLogsRoomsLoop()
        {
            // PortalSpace logs rooms.loop {room, laps} when PortalSpaceCore says a lap finished; the tracker agrees on every crossing.
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            var random = new System.Random(20261016);
            for (int rooms = 2; rooms <= 6; rooms++)
            {
                var registry = new ParamRegistry();
                var space = new PortalSpaceCore();
                space.Configure(room, registry);
                registry.Find(PortalSpaceCore.LayoutParam).Set(1);
                registry.Find(PortalSpaceCore.CountParam).Set(rooms);
                PortalLayout layout = space.Generate();
                Assert.That(layout.Kind, Is.EqualTo(PortalLayoutKind.Loop));
                var tracker = new RoomsLoopTracker();
                tracker.Reset(layout);

                int current = layout.StartRoom, laps = 0;
                for (int step = 0; step < 400; step++)
                {
                    // Any doorway of the room: forwards, backwards, in and out of the loop.
                    var options = new List<LayoutPortal>();
                    foreach (LayoutPortal portal in layout.Portals) if (portal.From == current) options.Add(portal);
                    // Mostly forwards, so laps happen.
                    LayoutPortal take = options[random.Next(options.Count)];
                    if (layout.Rooms[current].ExitPortal >= 0 && random.NextDouble() < 0.7) take = layout.Portals[layout.Rooms[current].ExitPortal];
                    bool expected = space.OnRoomChanged(current, take.To);
                    bool actual = tracker.OnRoomChanged(current, take.To);
                    Assert.That(actual, Is.EqualTo(expected), rooms + " rooms, step " + step);
                    if (expected) laps++;
                    current = take.To;
                }
                Assert.That(laps, Is.GreaterThan(5), "laps happened, so the comparison was not vacuous");
                Assert.That(tracker.Laps, Is.EqualTo(space.LoopsWalked));
            }
        }

        // ---- P2-C16: the signs ------------------------------------------------------------------------------

        [Test]
        public void Signs_OneNumeralOnTheFacedWallOfEachCorridorRoom()
        {
            for (int rooms = 2; rooms <= 6; rooms++)
            {
                PortalLayout layout = Plain(2.8f, 3.4f, PortalLayoutKind.Loop, rooms, 5);
                List<RoomSign> signs = RoomSigns.Layout(layout);
                Assert.That(signs.Count, Is.EqualTo(rooms), "a sign for every corridor room");
                for (int k = 0; k < signs.Count; k++)
                {
                    RoomSign sign = signs[k];
                    LayoutRoom r = layout.Rooms[sign.Room];
                    Assert.That(sign.Number, Is.EqualTo(r.Number));
                    Assert.That(sign.Number, Is.EqualTo(k + 1), "numbers run 1 .. n");

                    // On the outer wall of the room's first corner square, inside the corridor, below the wall top.
                    FloorRect corner = PortalLayoutGenerator.CornerCell(layout.Wf, layout.Df, layout.CorridorWidth, r.FirstCorner);
                    float2 xz = new float2(sign.Centre.x, sign.Centre.z);
                    Assert.That(corner.Contains(xz, 0.01f), Is.True, "sign inside the corner square, room " + sign.Room);
                    float2 outer = PortalLayoutGenerator.Corner(layout.Wf, layout.Df, r.FirstCorner);
                    float2 inward = PortalLayoutGenerator.LegInward(r.FirstCorner);
                    float2 along = PortalLayoutGenerator.LegDirection(r.FirstCorner);
                    Assert.That(math.dot(xz - outer, inward), Is.EqualTo(RoomSigns.Lift).Within(1e-5f), "stands on the wall face");
                    Assert.That(math.dot(xz - outer, along), Is.EqualTo(0.5f * layout.CorridorWidth).Within(1e-5f), "centred on the wall");
                    Assert.That(sign.Normal.x, Is.EqualTo(inward.x).Within(1e-6f));
                    Assert.That(sign.Normal.y, Is.EqualTo(inward.y).Within(1e-6f));
                    Assert.That(sign.Centre.y + 0.5f * sign.Height, Is.LessThan(layout.WallHeight));
                    // Above the tally bars (they reach 1.7 m).
                    Assert.That(sign.Centre.y - 0.5f * sign.Height, Is.GreaterThan(1.7f));
                }
            }
        }

        [Test]
        public void Signs_ABoothLayoutOrNoLayoutHasNone()
        {
            Assert.That(RoomSigns.Layout(null), Is.Empty);
            Assert.That(RoomSigns.Layout(Plain(2.0f, 3.0f, PortalLayoutKind.Loop, 6, 1)), Is.Empty, "the fallback booth has no numbers");
            Assert.That(RoomSigns.Layout(Plain(3f, 3f, PortalLayoutKind.BoothHall, 6, 1)), Is.Empty);
        }

        [Test]
        public void Signs_DigitMeshFacesTheReaderStaysOnTheWallAndFitsTheBudget()
        {
            for (int rooms = 2; rooms <= 6; rooms++)
            {
                PortalLayout layout = Plain(2.4f, 2.4f, PortalLayoutKind.Loop, rooms, 2);
                foreach (RoomSign sign in RoomSigns.Layout(layout))
                {
                    var data = new AtlasMeshBuilder(256, 512);
                    RoomSigns.BuildSign(sign, data, AtlasPalette.Beacon);
                    Assert.That(data.TriangleCount, Is.GreaterThan(0));
                    Assert.That(data.TriangleCount, Is.LessThanOrEqualTo(RoomSigns.MaxDigitTriangles));

                    LayoutRoom r = layout.Rooms[sign.Room];
                    FloorRect corner = PortalLayoutGenerator.CornerCell(layout.Wf, layout.Df, layout.CorridorWidth, r.FirstCorner);
                    float2 outer = PortalLayoutGenerator.Corner(layout.Wf, layout.Df, r.FirstCorner);
                    float2 inward = PortalLayoutGenerator.LegInward(r.FirstCorner);
                    for (int v = 0; v < data.VertexCount; v++)
                    {
                        AtlasVertex vertex = data.Vertices[v];
                        // Facing the reader.
                        Assert.That(vertex.Normal.x, Is.EqualTo(inward.x).Within(1e-4f));
                        Assert.That(vertex.Normal.z, Is.EqualTo(inward.y).Within(1e-4f));
                        // In the corner square, on the wall face, in the band of the digit.
                        float2 xz = vertex.Position.xz;
                        Assert.That(corner.Contains(xz, 0.01f), Is.True);
                        Assert.That(math.dot(xz - outer, inward), Is.EqualTo(RoomSigns.Lift).Within(1e-4f));
                        Assert.That(vertex.Position.y, Is.InRange(sign.Centre.y - 0.5f * sign.Height - 1e-4f, sign.Centre.y + 0.5f * sign.Height + 1e-4f));
                    }
                    // The whole room, mesh and sign, stays within the portal room budget.
                    var room = new AtlasMeshBuilder(1024, 4096);
                    PortalRoomMeshes.BuildRoom(layout, sign.Room, room);
                    Assert.That(room.TriangleCount + data.TriangleCount, Is.LessThanOrEqualTo(PortalRoomMeshes.RoomTriangles));
                }
            }
        }

        [Test]
        public void Signs_DigitsReadLeftToRight()
        {
            // A wall that faces south (-z): the reader looks north, so their right is east (+x).
            var two = new RoomSign { Room = 1, Number = 10, Centre = new float3(0f, 2f, 0f), Normal = new float2(0f, -1f), Height = RoomSigns.DigitHeight };
            var data = new AtlasMeshBuilder(256, 512);
            RoomSigns.BuildSign(two, data, AtlasPalette.Beacon);
            Assert.That(RoomSigns.Digits(10), Is.EqualTo(2));
            Assert.That(RoomSigns.Digits(0), Is.EqualTo(1));
            // "1" is two strokes (8 vertices) wholly left of the "0".
            float split = 0f;
            int left = 0;
            float leftMax = float.MinValue, rightMin = float.MaxValue;
            for (int v = 0; v < data.VertexCount; v++)
            {
                float x = data.Vertices[v].Position.x;
                if (x < split) { left++; leftMax = math.max(leftMax, x); }
                else rightMin = math.min(rightMin, x);
            }
            Assert.That(left, Is.EqualTo(8));
            Assert.That(leftMax, Is.LessThan(rightMin));

            // A single digit is centred on the sign.
            data.Clear();
            RoomSigns.BuildSign(new RoomSign { Room = 1, Number = 0, Centre = new float3(0f, 2f, 0f), Normal = new float2(0f, -1f), Height = RoomSigns.DigitHeight },
                                data, AtlasPalette.Beacon);
            float lowestX = float.MaxValue, highestX = float.MinValue;
            for (int v = 0; v < data.VertexCount; v++)
            {
                lowestX = math.min(lowestX, data.Vertices[v].Position.x);
                highestX = math.max(highestX, data.Vertices[v].Position.x);
            }
            Assert.That(0.5f * (lowestX + highestX), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(highestX - lowestX, Is.EqualTo(RoomSigns.DigitWidth).Within(1e-4f));
        }

        // ---- the exhibit's tasks and the fallback prompt ----------------------------------------------------

        static int RoomOfKind(PortalLayout layout, LayoutRoomKind kind)
        {
            for (int r = 0; r < layout.RoomCount; r++) if (layout.Rooms[r].Kind == kind) return r;
            return -1;
        }

        [Test]
        public void Exhibit_BoothThenLoopThenDone_WhereTheRoomHoldsTheLoop()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            var core = new RoomsExhibitCore(room);
            var prompts = new List<string>();
            core.PromptChanged += prompts.Add;
            Assert.That(core.Stage, Is.EqualTo(RoomsStage.Booth));
            Assert.That(core.WantedLayout, Is.EqualTo(PortalLayoutKind.BoothHall));
            Assert.That(core.Prompt, Is.EqualTo(RoomsText.BoothPrompt));

            PortalLayout booth = Plain(room.FreeRect.Wf, room.FreeRect.Df, PortalLayoutKind.BoothHall, 6, 1);
            core.OnLayoutBuilt(booth);
            int hall = RoomOfKind(booth, LayoutRoomKind.Hall);
            core.OnRoomChanged(0, hall);
            Assert.That(core.BoothEntered, Is.True);
            Assert.That(core.Stage, Is.EqualTo(RoomsStage.Loop));
            Assert.That(core.WantedLayout, Is.EqualTo(PortalLayoutKind.Loop));
            Assert.That(core.FallbackActive, Is.False);
            Assert.That(core.Prompt, Is.EqualTo(RoomsText.LoopPrompt));

            PortalLayout loop = Plain(room.FreeRect.Wf, room.FreeRect.Df, PortalLayoutKind.Loop, 6, 1);
            core.OnLayoutBuilt(loop);
            Assert.That(core.FallbackActive, Is.False, "the loop was built");
            var tracker = new RoomsLoopTracker();
            tracker.Reset(loop);
            foreach (KeyValuePair<int, int> step in LoopWalk(loop, 12))
            {
                core.OnRoomChanged(step.Key, step.Value);
                if (core.Completed) break;
            }
            Assert.That(core.Completed, Is.True);
            Assert.That(core.LoopWalked, Is.True);
            Assert.That(core.NumberRead, Is.True);
            Assert.That(core.LastLap.NumberChanged, Is.True);
            Assert.That(core.Prompt, Is.EqualTo(RoomsText.LapPrompt(core.LastLap.StartNumber, core.LastLap.ArrivalNumber)));
            Assert.That(prompts.Count, Is.EqualTo(2), "hall to loop, loop to lap; the booth prompt came before the subscription");
            Assert.That(core.LastLap.TurnsWalked, Is.EqualTo(4));
            Assert.That(core.Prompt, Does.Contain("four right turns"));
        }

        [Test]
        public void Exhibit_FallbackPromptWhereTheRoomIsTooSmall()
        {
            foreach (string name in new[] { RoomFixtures.SampleL3x4, RoomFixtures.Sample4x5Table })
            {
                RoomModel room = RoomFixtures.LoadFromProject(name);
                var core = new RoomsExhibitCore(room);
                PortalLayout booth = PortalLayoutGenerator.Generate(PortalLayoutRequest.FromRoom(room, PortalLayoutKind.BoothHall, 6, 1));
                core.OnLayoutBuilt(booth);
                core.OnRoomChanged(0, RoomOfKind(booth, LayoutRoomKind.Hall));
                Assert.That(core.BoothEntered, Is.True, name);
                Assert.That(core.FallbackActive, Is.True, name);
                Assert.That(core.Completed, Is.True, name);
                Assert.That(core.WantedLayout, Is.EqualTo(PortalLayoutKind.BoothHall), name + ": the booth stays");
                Assert.That(core.Prompt, Is.EqualTo(RoomsText.FallbackTooSmall), name);
                Assert.That(core.Prompt, Does.Contain("2.4 m"), name);
            }
        }

        [Test]
        public void Exhibit_StandingModeHasItsOwnFallbackPrompt()
        {
            RoomModel tiny = RoomFixtures.LoadFromProject(RoomFixtures.TinyStanding);
            var core = new RoomsExhibitCore(tiny);
            PortalLayout booth = PortalLayoutGenerator.Generate(PortalLayoutRequest.FromRoom(tiny, PortalLayoutKind.BoothHall, 6, 1));
            core.OnLayoutBuilt(booth);
            core.OnRoomChanged(0, RoomOfKind(booth, LayoutRoomKind.Hall));
            Assert.That(core.FallbackActive, Is.True);
            Assert.That(core.Prompt, Is.EqualTo(core.LoopBlock == RoomsLoopBlock.Standing ? RoomsText.FallbackStanding : RoomsText.FallbackTooSmall));
        }

        [Test]
        public void Exhibit_ALoopTheFloorCannotHoldIsAFallbackToo()
        {
            // The free rectangle is big enough, but the build came back as the booth: told why, booth kept.
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            var core = new RoomsExhibitCore(room);
            PortalLayout booth = Plain(room.FreeRect.Wf, room.FreeRect.Df, PortalLayoutKind.BoothHall, 6, 1);
            core.OnLayoutBuilt(booth);
            core.OnRoomChanged(0, RoomOfKind(booth, LayoutRoomKind.Hall));
            Assert.That(core.WantedLayout, Is.EqualTo(PortalLayoutKind.Loop));

            var request = new PortalLayoutRequest
            {
                Wf = room.FreeRect.Wf, Df = room.FreeRect.Df, DoorHeight = 2.1f, Seed = 1, Layout = PortalLayoutKind.Loop, LoopRooms = 6,
                Floor = new RectFloor(new float2(0.2f * room.FreeRect.Wf, 0.5f * room.FreeRect.Df)),
            };
            PortalLayout builtAsBooth = PortalLayoutGenerator.Generate(in request);
            Assert.That(builtAsBooth.Kind, Is.Not.EqualTo(PortalLayoutKind.Loop));
            core.OnLayoutBuilt(builtAsBooth);
            Assert.That(core.FallbackActive, Is.True);
            Assert.That(core.Completed, Is.True);
            Assert.That(core.Prompt, Is.EqualTo(RoomsText.FallbackFloor));
            Assert.That(core.WantedLayout, Is.EqualTo(PortalLayoutKind.BoothHall));
        }

        [Test]
        public void Exhibit_BeginStartsAgainAtTheBooth()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample4x5Table);
            var core = new RoomsExhibitCore(room);
            PortalLayout booth = PortalLayoutGenerator.Generate(PortalLayoutRequest.FromRoom(room, PortalLayoutKind.BoothHall, 6, 1));
            core.OnLayoutBuilt(booth);
            core.OnRoomChanged(0, RoomOfKind(booth, LayoutRoomKind.Hall));
            Assert.That(core.Completed, Is.True);
            core.Begin();
            Assert.That(core.Stage, Is.EqualTo(RoomsStage.Booth));
            Assert.That(core.BoothEntered, Is.False);
            Assert.That(core.FallbackActive, Is.False);
            Assert.That(core.Prompt, Is.EqualTo(RoomsText.BoothPrompt));
        }

        [Test]
        public void Exhibit_FloorRatioIsVirtualFloorVisitedOverTheFreeRectangle()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            var core = new RoomsExhibitCore(room);
            Assert.That(core.FreeRectArea, Is.EqualTo(room.FreeRect.Wf * room.FreeRect.Df).Within(1e-5f));
            Assert.That(core.VirtualFloorVisited, Is.EqualTo(0f));

            PortalLayout booth = Plain(room.FreeRect.Wf, room.FreeRect.Df, PortalLayoutKind.BoothHall, 6, 1);
            core.OnLayoutBuilt(booth);
            Assert.That(core.FloorRatio, Is.EqualTo(0f), "the real room alone is not virtual floor");
            core.OnRoomChanged(0, RoomOfKind(booth, LayoutRoomKind.Hall));
            Assert.That(core.FloorRatio, Is.EqualTo(1f).Within(1e-4f), "the hall is the free rectangle");

            PortalLayout loop = Plain(room.FreeRect.Wf, room.FreeRect.Df, PortalLayoutKind.Loop, 6, 1);
            core.OnLayoutBuilt(loop);
            float previous = 0f;
            var visited = new HashSet<int>();
            float expected = 0f;
            foreach (KeyValuePair<int, int> step in LoopWalk(loop, 6))
            {
                core.OnRoomChanged(step.Key, step.Value);
                if (visited.Add(step.Value))
                    foreach (FloorRect cell in loop.Rooms[step.Value].Cells) expected += cell.Size.x * cell.Size.y;
                Assert.That(core.VirtualFloorVisited, Is.GreaterThanOrEqualTo(previous));
                previous = core.VirtualFloorVisited;
            }
            Assert.That(core.VirtualFloorVisited, Is.EqualTo(expected).Within(1e-3f));
            Assert.That(core.FloorRatio, Is.GreaterThan(0.5f));
        }

        [Test]
        public void Exhibit_CaptionIsThePlansWording()
        {
            Assert.That(RoomsText.Caption, Is.EqualTo("Flat rooms joined so that they overlap."));
        }
    }
}
