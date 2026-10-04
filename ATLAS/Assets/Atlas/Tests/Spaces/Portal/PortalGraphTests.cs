using System.Collections.Generic;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Spaces.Portal;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Spaces.Tests.Portal
{
    /// <summary>
    /// Impossible Rooms (S5): the room graph of Master Plan section 11.1 (Portal_Graph), the layouts on the
    /// sample rooms, the four right turns, the triangle budgets and what the module writes into State.
    /// </summary>
    public class PortalGraphTests
    {
        const float MaxHall = PortalRoomMeshes.HallMargin;

        /// <summary>A floor given as a polygon with holes, as the scan gives the eroded free floor.</summary>
        sealed class PolygonFloor : IPortalFloor
        {
            readonly WalkablePolygon m_Polygon;
            public PolygonFloor(float2[] outer, params float2[][] holes)
            {
                m_Polygon = new WalkablePolygon { Outer = outer, Holes = holes };
            }
            public bool Contains(float2 xz) { return m_Polygon.Contains(xz); }
        }

        static float2[] Rect(float minX, float minZ, float maxX, float maxZ, bool clockwise = false)
        {
            return clockwise
                ? new[] { new float2(minX, minZ), new float2(minX, maxZ), new float2(maxX, maxZ), new float2(maxX, minZ) }
                : new[] { new float2(minX, minZ), new float2(maxX, minZ), new float2(maxX, maxZ), new float2(minX, maxZ) };
        }

        // Five floor shapes round a free rectangle of wf by df. The last three cannot hold every layout.
        static IPortalFloor Shape(int shape, float wf, float df, System.Random random)
        {
            float hx = 0.5f * wf, hz = 0.5f * df;
            const float e = 0.01f;      // the floor ends a little outside the free rectangle
            switch (shape)
            {
                case 0:     // the free rectangle itself
                    return new PolygonFloor(Rect(-hx - e, -hz - e, hx + e, hz + e));
                case 1:     // an L: the free rectangle and an arm beside it
                {
                    float arm = 0.5f + (float)random.NextDouble() * 2f;
                    return new PolygonFloor(new[]
                    {
                        new float2(-hx - e, -hz - e), new float2(hx + arm, -hz - e), new float2(hx + arm, 0f),
                        new float2(hx + e, 0f), new float2(hx + e, hz + e), new float2(-hx - e, hz + e),
                    });
                }
                case 2:     // a pillar somewhere on the free rectangle
                {
                    float px = ((float)random.NextDouble() - 0.5f) * wf, pz = ((float)random.NextDouble() - 0.5f) * df;
                    return new PolygonFloor(Rect(-hx - e, -hz - e, hx + e, hz + e),
                                            Rect(px - 0.15f, pz - 0.15f, px + 0.15f, pz + 0.15f, true));
                }
                case 3:     // one corner cut off
                {
                    float cut = 0.3f + (float)random.NextDouble() * 0.8f;
                    return new PolygonFloor(new[]
                    {
                        new float2(-hx - e, -hz - e), new float2(hx + e, -hz - e), new float2(hx + e, hz + e),
                        new float2(-hx + cut, hz + e), new float2(-hx - e, hz - cut),
                    });
                }
                default:    // a floor narrower than the free rectangle claims
                {
                    float narrow = hx * (0.3f + 0.6f * (float)random.NextDouble());
                    return new PolygonFloor(Rect(-narrow, -hz - e, narrow, hz + e));
                }
            }
        }

        static void AssertGraph(PortalLayout layout, IPortalFloor floor, string what)
        {
            Assert.That(layout, Is.Not.Null, what);
            Assert.That(layout.RoomCount, Is.GreaterThanOrEqualTo(1), what);
            Assert.That(layout.ReachableCount(), Is.EqualTo(layout.RoomCount), what + ": breadth-first search reaches 100% of rooms");
            Assert.That(layout.EveryPortalHasTwin(), Is.True, what + ": every portal has a twin");
            Assert.That(layout.Portals.Count, Is.EqualTo(2 * layout.Doors.Count), what);
            Assert.That(layout.CellsInside(floor), Is.True, what + ": every room cell inside the eroded free floor");

            // The references a frame can ask for: every doorway of the walker's room at once.
            var slots = new PortalSlotAllocator();
            for (int room = 0; room < layout.RoomCount; room++)
            {
                slots.Begin();
                for (int p = 0; p < layout.Portals.Count; p++)
                {
                    if (layout.Portals[p].From != room) continue;
                    Assert.That(slots.Assign(layout.Portals[p].To), Is.GreaterThan(0), what + ": a doorway without a reference");
                }
                Assert.That(slots.Count, Is.LessThanOrEqualTo(15), what + ": at most 15 stencil references per frame");
            }
            Assert.That(layout.IsValid(floor), Is.True, what);
        }

        [Test]
        public void Portal_Graph()
        {
            var random = new System.Random(20261004);
            int loops = 0, booths = 0, alone = 0, fallbacks = 0;
            for (int shape = 0; shape < 5; shape++)
            {
                for (uint seed = 0; seed < 1000; seed++)
                {
                    float wf = 0.8f + (float)random.NextDouble() * 3.4f;
                    float df = wf + (float)random.NextDouble() * 2.5f;
                    // The free rectangle of a scan is a multiple of 0.05 m.
                    wf = math.round(wf * 20f) / 20f;
                    df = math.round(df * 20f) / 20f;
                    IPortalFloor floor = Shape(shape, wf, df, random);
                    var request = new PortalLayoutRequest
                    {
                        Wf = wf, Df = df, DoorHeight = 2.1f, Seed = seed, Floor = floor,
                        Layout = (seed & 1) == 0 ? PortalLayoutKind.Loop : PortalLayoutKind.BoothHall,
                        LoopRooms = 2 + (int)(seed % 5),
                        Standing = wf < 1.5f,
                    };
                    PortalLayout layout = PortalLayoutGenerator.Generate(in request);
                    string what = "shape " + shape + " seed " + seed + " " + wf + " x " + df;

                    // It passes, or it is the fallback layout, which passes too.
                    AssertGraph(layout, floor, what);
                    if (layout.Kind != request.Layout) Assert.That(layout.IsFallback, Is.True, what + ": another layout is a fallback");
                    else Assert.That(layout.IsFallback, Is.False, what);
                    if (layout.Kind == PortalLayoutKind.Loop)
                    {
                        Assert.That(wf, Is.GreaterThanOrEqualTo(PortalLayoutGenerator.MinLoopSide - 1e-4f), what);
                        Assert.That(layout.RoomCount, Is.EqualTo(1 + request.LoopRooms), what);
                        loops++;
                    }
                    else if (layout.Kind == PortalLayoutKind.BoothHall) booths++;
                    else alone++;
                    if (layout.IsFallback) fallbacks++;

                    // The same request gives the same layout.
                    PortalLayout again = PortalLayoutGenerator.Generate(in request);
                    Assert.That(again.Kind, Is.EqualTo(layout.Kind), what);
                    Assert.That(again.Doors.Count, Is.EqualTo(layout.Doors.Count), what);
                    for (int d = 0; d < layout.Doors.Count; d++)
                        Assert.That(math.distance(again.Doors[d].A, layout.Doors[d].A), Is.EqualTo(0f), what);
                }
            }
            // Every kind of outcome was met, so the checks above were not vacuous.
            Assert.That(loops, Is.GreaterThan(200), "loops");
            Assert.That(booths, Is.GreaterThan(200), "booths");
            Assert.That(alone, Is.GreaterThan(10), "real room alone");
            Assert.That(fallbacks, Is.GreaterThan(100), "fallbacks");
        }

        [Test]
        public void Portal_Graph_APlainRectangleAlwaysHoldsWhatItIsLargeEnoughFor()
        {
            for (float wf = 1.2f; wf <= 4.5f; wf += 0.05f)
            {
                float df = wf + 0.6f;
                IPortalFloor floor = Shape(0, wf, df, new System.Random(1));
                for (int rooms = 2; rooms <= 6; rooms++)
                {
                    var request = new PortalLayoutRequest { Wf = wf, Df = df, DoorHeight = 2.1f, Seed = (uint)rooms, Floor = floor, Layout = PortalLayoutKind.Loop, LoopRooms = rooms };
                    PortalLayout layout = PortalLayoutGenerator.Generate(in request);
                    AssertGraph(layout, floor, "wf " + wf);
                    bool fits = wf >= PortalLayoutGenerator.MinLoopSide - 1e-4f;
                    Assert.That(layout.Kind, Is.EqualTo(fits ? PortalLayoutKind.Loop : PortalLayoutKind.BoothHall), "wf " + wf);
                    Assert.That(layout.IsFallback, Is.EqualTo(!fits), "wf " + wf);
                }
            }
        }

        [Test]
        public void Portal_Graph_AFloorThatHoldsNothingGivesTheRealRoomAlone()
        {
            var request = new PortalLayoutRequest { Wf = 0.6f, Df = 0.9f, DoorHeight = 2.1f, Layout = PortalLayoutKind.Loop, LoopRooms = 6 };
            PortalLayout layout = PortalLayoutGenerator.Generate(in request);
            Assert.That(layout.Kind, Is.EqualTo(PortalLayoutKind.RealRoomOnly));
            Assert.That(layout.IsFallback, Is.True);
            Assert.That(layout.RoomCount, Is.EqualTo(1));
            Assert.That(layout.Doors.Count, Is.EqualTo(0));
            AssertGraph(layout, null, "tiny");

            request = new PortalLayoutRequest { Wf = float.NaN, Df = 3f, Layout = PortalLayoutKind.BoothHall };
            Assert.That(PortalLayoutGenerator.Generate(in request).Kind, Is.EqualTo(PortalLayoutKind.RealRoomOnly));
        }

        // ---- the sample rooms -----------------------------------------------------------------------------

        static readonly string[] s_SampleRooms = { RoomFixtures.Sample3x3, RoomFixtures.SampleL3x4, RoomFixtures.Sample4x5Table };

        [Test]
        public void Layouts_FitTheErodedFreeFloorOfTheThreeSampleRooms()
        {
            foreach (string name in s_SampleRooms)
            {
                RoomModel room = RoomFixtures.LoadFromProject(name);
                var floor = new RoomModelFloor(room);
                foreach (PortalLayoutKind kind in new[] { PortalLayoutKind.BoothHall, PortalLayoutKind.Loop })
                {
                    for (int rooms = 2; rooms <= 6; rooms++)
                    {
                        PortalLayoutRequest request = PortalLayoutRequest.FromRoom(room, kind, rooms, PortalSpaceCore.SeedOf(room));
                        PortalLayout layout = PortalLayoutGenerator.Generate(in request);
                        string what = name + " " + kind + " " + rooms;
                        AssertGraph(layout, floor, what);
                        Assert.That(layout.Kind, Is.Not.EqualTo(PortalLayoutKind.RealRoomOnly), what + ": every sample room holds a layout");
                        Assert.That(layout.DoorHeight, Is.EqualTo(2.1f).Within(1e-5f), what + ": 2.4 m walls take the 2.1 m door");

                        // Inside the free rectangle, and inside the walkable polygon of the file.
                        foreach (LayoutRoom r in layout.Rooms)
                        {
                            foreach (FloorRect cell in r.Cells)
                            {
                                Assert.That(room.FreeRect.Contains(cell.Min) && room.FreeRect.Contains(cell.Max), Is.True, what);
                                Assert.That(room.Walkable.Contains(cell.Centre), Is.True, what);
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void Layouts_TheSampleRoomsGetWhatTheirFreeRectangleAllows()
        {
            // 2.4 x 2.4: the loop, with a corridor of 0.8 m.
            RoomModel square = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            PortalLayout loop = PortalLayoutGenerator.Generate(PortalLayoutRequest.FromRoom(square, PortalLayoutKind.Loop, 6, 1));
            Assert.That(loop.Kind, Is.EqualTo(PortalLayoutKind.Loop));
            Assert.That(loop.IsFallback, Is.False);
            Assert.That(loop.CorridorWidth, Is.EqualTo(0.8f).Within(1e-5f));
            Assert.That(loop.RoomCount, Is.EqualTo(7), "the real room and six corridor rooms");

            // 1.9 x 2.4 and 1.6 x 3.4: too narrow for the loop, so the booth.
            foreach (string name in new[] { RoomFixtures.SampleL3x4, RoomFixtures.Sample4x5Table })
            {
                RoomModel room = RoomFixtures.LoadFromProject(name);
                PortalLayout layout = PortalLayoutGenerator.Generate(PortalLayoutRequest.FromRoom(room, PortalLayoutKind.Loop, 6, 1));
                Assert.That(layout.Kind, Is.EqualTo(PortalLayoutKind.BoothHall), name);
                Assert.That(layout.IsFallback, Is.True, name);
                Assert.That(layout.Booth.Size.x, Is.EqualTo(1f).Within(1e-5f), name);
                Assert.That(layout.Booth.Size.y, Is.EqualTo(1f).Within(1e-5f), name);
                Assert.That(math.distance(layout.Doors[0].A, layout.Doors[0].B), Is.EqualTo(0.9f).Within(1e-5f), name);
            }

            // Standing mode gives the booth, viewed in place.
            RoomModel tiny = RoomFixtures.LoadFromProject(RoomFixtures.TinyStanding);
            PortalLayout standing = PortalLayoutGenerator.Generate(PortalLayoutRequest.FromRoom(tiny, PortalLayoutKind.Loop, 6, 1));
            Assert.That(standing.Kind, Is.EqualTo(PortalLayoutKind.BoothHall));
            AssertGraph(standing, new RoomModelFloor(tiny), "tiny_standing");
        }

        [Test]
        public void Booth_StandsInACorner_DoorTowardsTheMiddle_AndTheHallFillsTheFreeRectangle()
        {
            for (uint seed = 0; seed < 4; seed++)
            {
                var request = new PortalLayoutRequest { Wf = 2.4f, Df = 3.4f, DoorHeight = 2.1f, Seed = seed, Layout = PortalLayoutKind.BoothHall };
                PortalLayout layout = PortalLayoutGenerator.Generate(in request);
                Assert.That(layout.Kind, Is.EqualTo(PortalLayoutKind.BoothHall));
                Assert.That(layout.RoomCount, Is.EqualTo(2));
                FloorRect hall = layout.Rooms[1].Cells[0];
                Assert.That(hall.Size.x, Is.EqualTo(2.4f).Within(1e-5f));
                Assert.That(hall.Size.y, Is.EqualTo(3.4f).Within(1e-5f));
                Assert.That(hall.Size.x * hall.Size.y, Is.GreaterThan(layout.Booth.Size.x * layout.Booth.Size.y * 8f), "bigger inside than outside");

                // In a corner: two of the booth's sides lie on the free rectangle's rim.
                float2 corner = new float2(layout.BoothCorner.x * 1.2f, layout.BoothCorner.y * 1.7f);
                Assert.That(layout.Booth.Contains(corner, 1e-4f), Is.True, "seed " + seed);

                // The doorway's plane faces the middle of the room, where the walker comes from.
                LayoutDoor door = layout.Doors[0];
                Assert.That(door.From, Is.EqualTo(0));
                Assert.That(door.To, Is.EqualTo(1));
                float4 plane = PortalMath.DoorPlane(door.A, door.B);
                Assert.That(PortalMath.Side(plane, new float3(0f, 1.6f, 0f)), Is.GreaterThan(0.5f), "seed " + seed);
                float2 normal = PortalRoomMeshes.DoorNormal(door);
                Assert.That(normal.x, Is.EqualTo(plane.x).Within(1e-6f));
                Assert.That(normal.y, Is.EqualTo(plane.z).Within(1e-6f));
                // And the booth lies behind it.
                float2 inBooth = layout.Booth.Centre;
                Assert.That(PortalMath.Side(plane, new float3(inBooth.x, 1f, inBooth.y)), Is.LessThan(0f), "seed " + seed);
            }
        }

        // ---- four right turns -----------------------------------------------------------------------------

        static PortalLayout Loop(float wf, float df, int rooms, uint seed)
        {
            var request = new PortalLayoutRequest { Wf = wf, Df = df, DoorHeight = 2.1f, Seed = seed, Layout = PortalLayoutKind.Loop, LoopRooms = rooms };
            PortalLayout layout = PortalLayoutGenerator.Generate(in request);
            Assert.That(layout.Kind, Is.EqualTo(PortalLayoutKind.Loop));
            return layout;
        }

        [Test]
        public void FourRightTurns_ArriveInADifferentRoom_OnTheSameFloor()
        {
            for (int rooms = PortalLayoutGenerator.MinLoopRooms; rooms <= PortalLayoutGenerator.MaxLoopRooms; rooms++)
            {
                for (uint seed = 0; seed < 4; seed++)
                {
                    PortalLayout layout = Loop(2.4f + 0.3f * seed, 3.4f, rooms, seed);
                    int turns = 0;
                    for (int r = 1; r < layout.RoomCount; r++)
                    {
                        LayoutRoom start = layout.Rooms[r];
                        Assert.That(start.Turns, Is.InRange(1, 4), "a room never laps itself");
                        turns += start.Turns;

                        int arrived = layout.RoomAfterRightTurns(r, 4);
                        string what = rooms + " rooms, from room " + r;
                        Assert.That(arrived, Is.Not.EqualTo(r), what + ": four right turns arrive in a different room");
                        Assert.That(layout.Rooms[arrived].Kind, Is.EqualTo(LayoutRoomKind.Corridor), what);
                        // The same piece of floor, in another room: that is what cannot be.
                        float2 where = start.Cells[0].Centre;
                        Assert.That(layout.Rooms[arrived].ContainsFloor(where), Is.True, what + ": on the floor the walk started from");
                        Assert.That(layout.Rooms[arrived].Number, Is.Not.EqualTo(start.Number), what + ": the number on the wall differs");

                        Assert.That(layout.RoomAfterRightTurns(r, 8), Is.EqualTo(r), what + ": eight right turns close the loop");
                        Assert.That(layout.RoomAfterRightTurns(r, 0), Is.EqualTo(r), what);
                    }
                    Assert.That(turns, Is.EqualTo(PortalLayoutGenerator.LoopCorners), "two laps of corners in all");
                }
            }
        }

        [Test]
        public void FourRightTurns_WalkedWithTheHead_ThroughTheDoorways()
        {
            // The walker goes round the corridor's middle line; the room changes only by crossing a doorway,
            // with the same test the portal renderer uses.
            for (int rooms = PortalLayoutGenerator.MinLoopRooms; rooms <= PortalLayoutGenerator.MaxLoopRooms; rooms++)
            {
                const float wf = 3f, df = 3.6f;
                PortalLayout layout = Loop(wf, df, rooms, 0);
                float w = layout.CorridorWidth;
                int leg = layout.EntranceLeg;

                // Start in the real room, in the middle, and walk out through the entrance.
                int room = 0;
                var head = new float3(0f, 1.6f, 0f);
                LayoutDoor entrance = layout.Doors[layout.Doors.Count - 1];
                float2 inward = PortalLayoutGenerator.LegInward(leg);
                float2 doorMid = 0.5f * (entrance.A + entrance.B);
                float2 inCorridor = doorMid - inward * (0.5f * w);
                room = Walk(layout, room, ref head, inCorridor);
                Assert.That(room, Is.EqualTo(1), rooms + " rooms: the entrance leads into the first corridor room");

                // Corner j's turning point on the middle line.
                var visited = new List<int>();
                int rightTurns = 0;
                for (int k = 1; k <= 8; k++)
                {
                    int j = leg + k;
                    float2 turn = PortalLayoutGenerator.Corner(wf, df, j)
                                  + 0.5f * w * (PortalLayoutGenerator.LegDirection(j) + PortalLayoutGenerator.LegInward(j));
                    // Every corner turns the walker to the right by a quarter turn.
                    float2 before = PortalLayoutGenerator.LegDirection(j - 1), after = PortalLayoutGenerator.LegDirection(j);
                    Assert.That(before.x * after.y - before.y * after.x, Is.EqualTo(-1f).Within(1e-6f), "a right turn seen from above");
                    room = Walk(layout, room, ref head, turn);
                    rightTurns++;
                    visited.Add(room);
                    Assert.That(layout.Rooms[room].ContainsFloor(new float2(head.x, head.z)), Is.True,
                        rooms + " rooms, corner " + k + ": the head stands on the floor of the room it is in");
                }
                Assert.That(rightTurns, Is.EqualTo(8));
                // After four right turns: the same corner of the floor, a different room.
                Assert.That(visited[4], Is.Not.EqualTo(visited[0]), rooms + " rooms");
                Assert.That(visited[7], Is.Not.EqualTo(visited[3]), rooms + " rooms");
                // The graph agrees: corner k of the walk belongs to the room that covers it.
                var roomOfCorner = new List<int>();
                for (int r = 1; r < layout.RoomCount; r++)
                    for (int t = 0; t < layout.Rooms[r].Turns; t++) roomOfCorner.Add(r);
                for (int k = 1; k <= 8; k++)
                    Assert.That(visited[k - 1], Is.EqualTo(roomOfCorner[k % 8]), rooms + " rooms, corner " + k);
                foreach (int v in visited) Assert.That(v, Is.GreaterThan(0), "never back in the real room on the way");
                // Two laps come back to where the walk met the corridor.
                room = Walk(layout, room, ref head, inCorridor);
                Assert.That(room, Is.EqualTo(1), rooms + " rooms: eight right turns close the loop");
            }
        }

        // Walks the head in a straight line in 2 cm steps and follows it through doorways.
        static int Walk(PortalLayout layout, int room, ref float3 head, float2 target)
        {
            var to = new float3(target.x, head.y, target.y);
            int steps = math.max(1, (int)math.ceil(math.distance(head, to) / 0.02f));
            float3 from = head;
            for (int s = 1; s <= steps; s++)
            {
                float3 next = math.lerp(from, to, (float)s / steps);
                for (int p = 0; p < layout.Portals.Count; p++)
                {
                    LayoutPortal portal = layout.Portals[p];
                    if (portal.From != room) continue;
                    float4 plane = PortalMath.DoorPlane(portal.Left, portal.Right);
                    if (!PortalMath.Crossed(plane, portal.Left, portal.Right, layout.Doors[portal.Door].Height, head, next)) continue;
                    room = portal.To;
                    break;
                }
                head = next;
            }
            return room;
        }

        [Test]
        public void Loop_CorridorWidthAndThresholdFollowThePlan()
        {
            Assert.That(PortalLayoutGenerator.CorridorWidth(2.4f, 3f), Is.EqualTo(0.8f).Within(1e-6f));
            Assert.That(PortalLayoutGenerator.CorridorWidth(1.8f, 3f), Is.EqualTo(0.7f).Within(1e-6f));
            Assert.That(PortalLayoutGenerator.CorridorWidth(3.6f, 5f), Is.EqualTo(1.0f).Within(1e-6f));
            Assert.That(PortalLayoutGenerator.MinLoopSide, Is.EqualTo(2.4f));
            for (int rooms = 2; rooms <= 6; rooms++)
            {
                int total = 0;
                for (int k = 0; k < rooms; k++) total += PortalLayoutGenerator.LoopSpan(k, rooms);
                Assert.That(total, Is.EqualTo(8), rooms + " rooms");
            }
            // The exits face the walker who comes along the corridor.
            PortalLayout layout = Loop(3f, 3.6f, 6, 0);
            for (int r = 1; r < layout.RoomCount; r++)
            {
                LayoutRoom room = layout.Rooms[r];
                LayoutPortal exit = layout.Portals[room.ExitPortal];
                Assert.That(exit.From, Is.EqualTo(r));
                Assert.That(exit.To, Is.EqualTo(r == layout.RoomCount - 1 ? 1 : r + 1));
                int lastLeg = room.FirstCorner + room.Turns - 1;
                float2 mid = 0.5f * (exit.Left + exit.Right) - 0.3f * PortalLayoutGenerator.LegDirection(lastLeg);
                float4 plane = PortalMath.DoorPlane(exit.Left, exit.Right);
                Assert.That(PortalMath.Side(plane, new float3(mid.x, 1.6f, mid.y)), Is.EqualTo(0.3f).Within(1e-4f), "room " + r);
                Assert.That(room.ContainsFloor(mid), Is.True, "room " + r + ": the exit is at the end of its own floor");
                Assert.That(math.distance(exit.Left, exit.Right), Is.EqualTo(layout.CorridorWidth).Within(1e-5f));
            }
        }

        // ---- triangles ------------------------------------------------------------------------------------

        [Test]
        public void Rooms_StayInsideTheirTriangleBudgets()
        {
            Assert.That(PortalRoomMeshes.RoomTriangles, Is.EqualTo(2000));
            var data = new AtlasMeshBuilder(1024, 4096);
            var sizes = new[] { new float2(1.4f, 1.4f), new float2(1.6f, 3.4f), new float2(1.9f, 2.4f), new float2(2.4f, 2.4f), new float2(3.4f, 4.4f), new float2(4.2f, 6.6f) };
            foreach (float2 size in sizes)
            {
                foreach (PortalLayoutKind kind in new[] { PortalLayoutKind.BoothHall, PortalLayoutKind.Loop })
                {
                    for (int rooms = 2; rooms <= 6; rooms++)
                    {
                        var request = new PortalLayoutRequest { Wf = size.x, Df = size.y, DoorHeight = 2.1f, Seed = (uint)rooms, Layout = kind, LoopRooms = rooms };
                        PortalLayout layout = PortalLayoutGenerator.Generate(in request);
                        string what = size + " " + layout.Kind + " " + rooms;

                        data.Clear();
                        PortalRoomMeshes.BuildExterior(layout, data);
                        Assert.That(data.TriangleCount, Is.GreaterThan(0), what + ": the real room shows its doorway");
                        Assert.That(data.TriangleCount, Is.LessThanOrEqualTo(PortalRoomMeshes.BoothTriangles), what + ": booth or entrance");

                        int total = data.TriangleCount;
                        for (int r = 1; r < layout.RoomCount; r++)
                        {
                            data.Clear();
                            PortalRoomMeshes.BuildRoom(layout, r, data);
                            Assert.That(data.TriangleCount, Is.GreaterThan(0), what + " room " + r);
                            Assert.That(data.TriangleCount, Is.LessThanOrEqualTo(PortalRoomMeshes.RoomTriangles), what + " room " + r + ": at most 2,000 triangles");
                            total += data.TriangleCount;

                            // The floor a room draws is the floor of its cells: nothing of a corridor leaves the free rectangle.
                            if (layout.Rooms[r].Kind == LayoutRoomKind.Corridor)
                            {
                                Assert.That(data.BoundsMin.x, Is.GreaterThanOrEqualTo(-0.5f * size.x - 0.01f), what);
                                Assert.That(data.BoundsMax.x, Is.LessThanOrEqualTo(0.5f * size.x + 0.01f), what);
                                Assert.That(data.BoundsMin.z, Is.GreaterThanOrEqualTo(-0.5f * size.y - 0.01f), what);
                                Assert.That(data.BoundsMax.z, Is.LessThanOrEqualTo(0.5f * size.y + 0.01f), what);
                                Assert.That(data.BoundsMin.y, Is.EqualTo(0f).Within(1e-5f), what);
                            }
                            else
                            {
                                Assert.That(data.BoundsMax.x, Is.EqualTo(0.5f * size.x + MaxHall).Within(1e-4f), what + ": the hall is larger than the room");
                            }
                        }
                        Assert.That(total, Is.LessThanOrEqualTo(150000 - 63000), what + ": inside the S5 budget beside the room's own content");
                    }
                }
            }

            data.Clear();
            PortalRoomMeshes.BuildRoom(PortalLayoutGenerator.Generate(new PortalLayoutRequest { Wf = 2.4f, Df = 2.4f }), 0, data);
            Assert.That(data.TriangleCount, Is.EqualTo(0), "room 0 is drawn by the room content");
        }

        // ---- the module's logic -----------------------------------------------------------------------------

        [Test]
        public void Module_DeclaresItsParametersInGroupPortal()
        {
            var registry = new ParamRegistry();
            PortalSpace.DeclareParams(registry);
            ParamEntry layout = registry.Find(PortalSpaceCore.LayoutParam);
            ParamEntry count = registry.Find(PortalSpaceCore.CountParam);
            ParamEntry depth = registry.Find(PortalSpaceCore.DepthParam);
            Assert.That(layout, Is.Not.Null);
            Assert.That(layout.Name, Is.EqualTo("rooms.layout"));
            Assert.That(count.Name, Is.EqualTo("rooms.count"));
            Assert.That(depth.Name, Is.EqualTo("portal.depth"));
            foreach (ParamEntry entry in new[] { layout, count, depth })
                Assert.That(entry.Group, Is.EqualTo(SpaceIds.ParamGroup(SpaceId.Portal)));
            Assert.That(SpaceIds.ParamGroup(SpaceId.Portal), Is.EqualTo("portal"));
            Assert.That(layout.Int, Is.EqualTo(0));
            Assert.That(layout.EnumName(0), Is.EqualTo("booth"));
            Assert.That(layout.EnumName(1), Is.EqualTo("loop"));
            Assert.That(layout.OnRelease, Is.True);
            Assert.That(count.Int, Is.EqualTo(6));
            Assert.That(count.Min, Is.EqualTo(2f));
            Assert.That(count.Max, Is.EqualTo(6f));
            Assert.That(count.OnRelease, Is.True);
            Assert.That(depth.Int, Is.EqualTo(1));
            Assert.That(depth.OnRelease, Is.False);

            // Declared again by Configure: one entry each, the value kept.
            int entries = registry.Count;
            layout.Set(1);
            var core = new PortalSpaceCore();
            core.Configure(RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3), registry);
            Assert.That(registry.Count, Is.EqualTo(entries));
            Assert.That(core.Layout.Kind, Is.EqualTo(PortalLayoutKind.Loop));
        }

        [Test]
        public void Module_ARegistrationFactoryExists_AndTheSpaceIsRegistered()
        {
            SpaceRegistry.Clear();
            try
            {
                PortalRegistration.Register();
                Assert.That(SpaceRegistry.IsRegistered(SpaceId.Portal), Is.True);
                var registry = new ParamRegistry();
                SpaceRegistry.DeclareAllParams(registry);
                Assert.That(registry.Find("portal.depth"), Is.Not.Null);
            }
            finally
            {
                SpaceRegistry.Clear();
            }
        }

        [Test]
        public void Module_ChangingTheLayoutAsksForARebuild_AndTheRebuildFollowsTheParameters()
        {
            var registry = new ParamRegistry();
            var core = new PortalSpaceCore();
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            core.Configure(room, registry);
            Assert.That(core.Layout.Kind, Is.EqualTo(PortalLayoutKind.BoothHall), "the default is the booth");
            Assert.That(core.RebuildPending, Is.False);

            registry.Find(PortalSpaceCore.LayoutParam).Set(1);
            Assert.That(core.RebuildPending, Is.True);
            core.Generate();
            Assert.That(core.RebuildPending, Is.False);
            Assert.That(core.Layout.Kind, Is.EqualTo(PortalLayoutKind.Loop));
            Assert.That(core.Layout.RoomCount, Is.EqualTo(7));

            registry.Find(PortalSpaceCore.CountParam).Set(3);
            Assert.That(core.RebuildPending, Is.True);
            core.Generate();
            Assert.That(core.Layout.RoomCount, Is.EqualTo(4));

            registry.Find(PortalSpaceCore.DepthParam).Set(0);
            Assert.That(core.RebuildPending, Is.False, "portal.depth changes no geometry");
            Assert.That(core.Depth, Is.EqualTo(0));

            // After Exit the registry no longer calls into the module.
            core.Exit(registry);
            registry.Find(PortalSpaceCore.LayoutParam).Set(0);
            Assert.That(core.RebuildPending, Is.False);
            Assert.That(registry.Find(PortalSpaceCore.CountParam).Int, Is.EqualTo(3), "values survive a switch");
        }

        [Test]
        public void Module_WritesTheCurrentRoomIntoState()
        {
            var registry = new ParamRegistry();
            PortalSpace.DeclareParams(registry);
            registry.Find(PortalSpaceCore.LayoutParam).Set(1);
            var core = new PortalSpaceCore();
            core.Configure(RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3), registry);

            StateMessage state = StateMessage.Identity;
            core.WriteState(ref state);
            Assert.That(state.Tile0, Is.EqualTo(0), "the walker starts in the real room");
            Assert.That(state.Sheet, Is.EqualTo(0));
            Assert.That(state.P3, Is.EqualTo(1f), "layout");
            Assert.That(state.P4, Is.EqualTo(7f), "room count");
            Assert.That(state.P5, Is.EqualTo(1f), "portal depth");

            core.OnRoomChanged(0, 1);
            core.OnRoomChanged(1, 2);
            state = StateMessage.Identity;
            core.WriteState(ref state);
            Assert.That(state.Tile0, Is.EqualTo(2));
            Assert.That(state.Sheet, Is.EqualTo(2));
            Assert.That(state.Tile1, Is.EqualTo(0));

            core.SetCurrentRoom(99);
            Assert.That(core.CurrentRoom, Is.EqualTo(6), "a received room is kept inside the layout");
        }

        [Test]
        public void Module_CountsALapWhenFourRightTurnsAreWalked()
        {
            var registry = new ParamRegistry();
            PortalSpace.DeclareParams(registry);
            registry.Find(PortalSpaceCore.LayoutParam).Set(1);
            var core = new PortalSpaceCore();
            core.Configure(RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3), registry);
            PortalLayout layout = core.Layout;     // spans 2, 2, 1, 1, 1, 1

            Assert.That(core.OnRoomChanged(0, 1), Is.False, "the entrance is no turn");
            Assert.That(core.OnRoomChanged(1, 2), Is.False, "two turns");
            Assert.That(core.OnRoomChanged(2, 3), Is.True, "four turns: a lap");
            Assert.That(core.LoopsWalked, Is.EqualTo(1));
            Assert.That(layout.RoomAfterRightTurns(1, 4), Is.EqualTo(3));
            Assert.That(core.OnRoomChanged(3, 4), Is.False);
            Assert.That(core.OnRoomChanged(4, 3), Is.False, "walking back counts nothing");
            Assert.That(core.OnRoomChanged(3, 4), Is.False);
            Assert.That(core.OnRoomChanged(4, 5), Is.False);
            Assert.That(core.OnRoomChanged(5, 6), Is.False);
            Assert.That(core.OnRoomChanged(6, 1), Is.True);
            Assert.That(core.LoopsWalked, Is.EqualTo(2));
        }

        [Test]
        public void Seed_IsTheSameForTheSameRoomId()
        {
            var a = new RoomModel { Id = "room_42" };
            var b = new RoomModel { Id = "room_42" };
            var c = new RoomModel { Id = "room_43" };
            Assert.That(PortalSpaceCore.SeedOf(a), Is.EqualTo(PortalSpaceCore.SeedOf(b)));
            Assert.That(PortalSpaceCore.SeedOf(a), Is.Not.EqualTo(PortalSpaceCore.SeedOf(c)));
        }
    }
}
