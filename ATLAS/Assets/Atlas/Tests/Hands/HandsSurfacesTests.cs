using System;
using System.Collections.Generic;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Hands.Tests
{
    /// <summary>
    /// The room's touchable surfaces (documents/ATLAS_Hands_Design.md section 6.5): the walls with their
    /// openings left out, the floor and the furniture boxes, and the point and normal a fingertip is given.
    /// The expected numbers are worked out by hand on the sample rooms; the sweep at the end checks the whole
    /// thing against a cloud of points sampled from the same RoomModel by a different route.
    ///
    /// sample_4x5_table, room frame, metres: the walls run x = 1.1 (normal -X; a door at z -1.6 to -0.7, to
    /// 2.0 m), z = 2.0 (normal -Z; a window at x -2.0 to -0.8, 0.9 to 2.1 m), x = -3.9 (normal +X) and
    /// z = -2.0 (normal +Z), all 2.4 m high. One table, turned a quarter turn, stands at x -1.7 to -1.1,
    /// z -0.5 to 0.5, 0.75 m high.
    /// </summary>
    public class HandsSurfacesTests
    {
        const float Tolerance = 1e-4f;

        static RoomModel Table() { return RoomFixtures.LoadFromProject(RoomFixtures.Sample4x5Table); }

        static RoomSurfaces Surfaces(RoomModel room)
        {
            var surfaces = new RoomSurfaces();
            surfaces.SetRoom(room);
            return surfaces;
        }

        static void AssertNear(float3 expected, float3 actual, string what, float tolerance = Tolerance)
        {
            Assert.That(math.distance(expected, actual), Is.LessThanOrEqualTo(tolerance),
                what + ": expected " + expected + ", got " + actual);
        }

        /// <summary>Asks, requires an answer, and checks the point, the normal and the unit length.</summary>
        static void AssertClosest(RoomSurfaces surfaces, float3 query, float maxDistance, float3 expectedPoint,
                                  float3 expectedNormal, string what, float tolerance = Tolerance)
        {
            float3 point, normal;
            Assert.That(surfaces.TryClosest(query, maxDistance, out point, out normal), Is.True, what + ": found nothing");
            AssertNear(expectedPoint, point, what + " point", tolerance);
            AssertNear(expectedNormal, normal, what + " normal", tolerance);
            Assert.That(math.length(normal), Is.EqualTo(1f).Within(1e-5f), what + " normal length");
        }

        static void AssertNothing(RoomSurfaces surfaces, float3 query, float maxDistance, string what)
        {
            float3 point, normal;
            Assert.That(surfaces.TryClosest(query, maxDistance, out point, out normal), Is.False,
                what + ": found " + point + " with normal " + normal);
            Assert.That(point, Is.EqualTo(float3.zero), what + ": point of nothing");
            Assert.That(normal, Is.EqualTo(float3.zero), what + ": normal of nothing");
        }

        // ---- Walls ---------------------------------------------------------------------------------------------

        [Test]
        public void Hands_Surfaces_Closest_WallFace()
        {
            RoomSurfaces s = Surfaces(Table());
            // Three centimetres inside the room from each of the four walls, away from every opening.
            AssertClosest(s, new float3(1.07f, 1.0f, 0.5f), 0.08f, new float3(1.1f, 1.0f, 0.5f), new float3(-1f, 0f, 0f), "wall x = 1.1");
            AssertClosest(s, new float3(0.5f, 1.2f, 1.97f), 0.08f, new float3(0.5f, 1.2f, 2.0f), new float3(0f, 0f, -1f), "wall z = 2.0");
            AssertClosest(s, new float3(-3.87f, 1.2f, 0.3f), 0.08f, new float3(-3.9f, 1.2f, 0.3f), new float3(1f, 0f, 0f), "wall x = -3.9");
            AssertClosest(s, new float3(-1.0f, 1.2f, -1.97f), 0.08f, new float3(-1.0f, 1.2f, -2.0f), new float3(0f, 0f, 1f), "wall z = -2.0");
        }

        [Test]
        public void Hands_Surfaces_Closest_BehindAWallTheNormalStillPointsIntoTheRoom()
        {
            RoomSurfaces s = Surfaces(Table());
            // A fingertip 10 cm through the wall (the scan and the hand disagree): the mark goes on the wall's room side.
            AssertClosest(s, new float3(1.2f, 1.0f, 0.5f), 0.2f, new float3(1.1f, 1.0f, 0.5f), new float3(-1f, 0f, 0f), "through wall x = 1.1");
            AssertClosest(s, new float3(0.5f, 1.2f, 2.1f), 0.2f, new float3(0.5f, 1.2f, 2.0f), new float3(0f, 0f, -1f), "through wall z = 2.0");
        }

        [Test]
        public void Hands_Surfaces_Closest_TheGapOfAnOpeningGivesNoWall()
        {
            RoomSurfaces s = Surfaces(Table());

            // The door, in the wall's own plane at 1.0 m: 0.4 m from the jamb at z = -1.6, 0.5 m from the other.
            float3 inTheDoor = new float3(1.1f, 1.0f, -1.2f);
            AssertNothing(s, inTheDoor, 0.39f, "the middle of the door");
            AssertClosest(s, inTheDoor, 0.41f, new float3(1.1f, 1.0f, -1.6f), new float3(-1f, 0f, 0f), "the jamb");
            // Off the plane, still in the gap.
            AssertNothing(s, new float3(1.0f, 1.0f, -1.15f), 0.3f, "the door's gap, a step inside the room");
            AssertNothing(s, new float3(1.2f, 1.0f, -1.15f), 0.3f, "the door's gap, a step outside");
            // The lintel above the door is wall again, from 2.0 m up.
            AssertClosest(s, new float3(1.07f, 2.2f, -1.15f), 0.08f, new float3(1.1f, 2.2f, -1.15f), new float3(-1f, 0f, 0f), "the lintel");
            AssertClosest(s, new float3(1.07f, 1.97f, -1.15f), 0.08f, new float3(1.1f, 2.0f, -1.15f), new float3(-1f, 0f, 0f), "under the lintel");

            // The window: wall x -2.0 to -0.8, between 0.9 and 2.1 m, in the wall z = 2.0.
            AssertNothing(s, new float3(-1.4f, 1.5f, 1.97f), 0.5f, "the middle of the window");
            AssertNothing(s, new float3(-1.4f, 1.5f, 2.0f), 0.5f, "the window, in the wall's plane");
            AssertClosest(s, new float3(-1.4f, 0.85f, 1.97f), 0.08f, new float3(-1.4f, 0.85f, 2.0f), new float3(0f, 0f, -1f), "the sill");
            AssertClosest(s, new float3(-1.4f, 2.15f, 1.97f), 0.08f, new float3(-1.4f, 2.15f, 2.0f), new float3(0f, 0f, -1f), "above the window");
            // Just inside the gap above the sill: the wall's nearest point is the sill's top edge.
            AssertClosest(s, new float3(-1.4f, 0.93f, 1.97f), 0.08f, new float3(-1.4f, 0.9f, 2.0f), new float3(0f, 0f, -1f), "the sill's edge");
            // Beside the window, the wall is whole.
            AssertClosest(s, new float3(-2.2f, 1.5f, 1.97f), 0.08f, new float3(-2.2f, 1.5f, 2.0f), new float3(0f, 0f, -1f), "beside the window");
        }

        // ---- Floor ---------------------------------------------------------------------------------------------

        [Test]
        public void Hands_Surfaces_Closest_Floor()
        {
            RoomSurfaces s = Surfaces(Table());
            AssertClosest(s, new float3(-0.5f, 0.05f, -1.0f), 0.08f, new float3(-0.5f, 0f, -1.0f), new float3(0f, 1f, 0f), "above the floor");
            // A fingertip that sank 2 cm into the floor still marks it, from above.
            AssertClosest(s, new float3(-0.5f, -0.02f, -1.0f), 0.08f, new float3(-0.5f, 0f, -1.0f), new float3(0f, 1f, 0f), "below the floor");
            AssertNothing(s, new float3(-0.5f, 0.2f, -1.0f), 0.08f, "20 cm above the floor");
        }

        [Test]
        public void Hands_Surfaces_Closest_TheFloorEndsAtTheOutline()
        {
            RoomSurfaces s = Surfaces(Table());
            AssertNothing(s, new float3(3.0f, 0.03f, 0.0f), 0.3f, "outside the room");

            // The L-shaped room: its notch (x 1.25 to 2.75, z 0 to 1.5) is not floor.
            RoomSurfaces l = Surfaces(RoomFixtures.LoadFromProject(RoomFixtures.SampleL3x4));
            AssertNothing(l, new float3(2.0f, 0.03f, 0.75f), 0.5f, "above the notch");
            AssertClosest(l, new float3(1.2f, 0.04f, 0.05f), 0.08f, new float3(1.2f, 0f, 0.05f), new float3(0f, 1f, 0f), "inside the inner corner");
            // Just outside the inner corner the wall at x = 1.25 is nearer (0.05) than the floor's rim (0.064).
            AssertClosest(l, new float3(1.3f, 0.04f, 0.07f), 0.08f, new float3(1.25f, 0.04f, 0.07f), new float3(-1f, 0f, 0f), "behind the inner corner");
            // Down the long leg, away from the notch.
            AssertClosest(l, new float3(0f, 0.05f, 1.0f), 0.08f, new float3(0f, 0f, 1.0f), new float3(0f, 1f, 0f), "the long leg");
        }

        // ---- Boxes ---------------------------------------------------------------------------------------------

        [Test]
        public void Hands_Surfaces_Closest_BoxFace()
        {
            RoomSurfaces s = Surfaces(Table());
            AssertClosest(s, new float3(-1.4f, 0.80f, 0.0f), 0.08f, new float3(-1.4f, 0.75f, 0.0f), new float3(0f, 1f, 0f), "the top");
            AssertClosest(s, new float3(-1.07f, 0.4f, 0.2f), 0.08f, new float3(-1.1f, 0.4f, 0.2f), new float3(1f, 0f, 0f), "the +x face");
            AssertClosest(s, new float3(-1.73f, 0.4f, -0.2f), 0.08f, new float3(-1.7f, 0.4f, -0.2f), new float3(-1f, 0f, 0f), "the -x face");
            // The table is turned a quarter turn: its long side runs along z, so these faces are at z = +-0.5.
            AssertClosest(s, new float3(-1.4f, 0.4f, 0.53f), 0.08f, new float3(-1.4f, 0.4f, 0.5f), new float3(0f, 0f, 1f), "the +z face");
            AssertClosest(s, new float3(-1.4f, 0.4f, -0.53f), 0.08f, new float3(-1.4f, 0.4f, -0.5f), new float3(0f, 0f, -1f), "the -z face");
            // Unturned, the table would end at z = +-0.3 and reach out to x = -1.9; turned, it does neither.
            AssertNothing(s, new float3(-1.4f, 0.4f, 0.65f), 0.08f, "beyond the table's end");
            AssertNothing(s, new float3(-1.93f, 0.4f, 0.0f), 0.02f, "where an unturned table would end");
        }

        [Test]
        public void Hands_Surfaces_Closest_BoxEdge()
        {
            RoomSurfaces s = Surfaces(Table());
            // 3 cm out from the +x face and 4 cm over the top: the top edge, and the normal is the way the finger lies.
            float3 query = new float3(-1.07f, 0.79f, 0.2f);
            AssertClosest(s, query, 0.06f, new float3(-1.1f, 0.75f, 0.2f), new float3(0.6f, 0.8f, 0f), "the top edge", 1e-3f);
            AssertNothing(s, query, 0.049f, "just out of reach of the top edge");
            // A vertical edge: 3 cm out from the +x face and 3 cm out from the +z face.
            AssertClosest(s, new float3(-1.07f, 0.4f, 0.53f), 0.06f, new float3(-1.1f, 0.4f, 0.5f),
                new float3(0.70710678f, 0f, 0.70710678f), "the vertical edge", 1e-3f);
        }

        [Test]
        public void Hands_Surfaces_Closest_BoxCorner()
        {
            RoomSurfaces s = Surfaces(Table());
            float c = 0.57735027f;
            AssertClosest(s, new float3(-1.08f, 0.77f, 0.52f), 0.06f, new float3(-1.1f, 0.75f, 0.5f), new float3(c, c, c), "the top corner", 1e-3f);
            // The same corner seen from the other side of the table.
            AssertClosest(s, new float3(-1.72f, 0.77f, -0.52f), 0.06f, new float3(-1.7f, 0.75f, -0.5f), new float3(-c, c, -c), "the far top corner", 1e-3f);
        }

        [Test]
        public void Hands_Surfaces_Closest_InsideABoxTheNearestFaceIsUsed()
        {
            RoomSurfaces s = Surfaces(Table());
            // A fingertip that sank into the box: the nearest face, with the normal pointing out of the box.
            AssertClosest(s, new float3(-1.4f, 0.74f, 0.0f), 0.08f, new float3(-1.4f, 0.75f, 0.0f), new float3(0f, 1f, 0f), "1 cm under the top");
            AssertClosest(s, new float3(-1.12f, 0.4f, 0.0f), 0.08f, new float3(-1.1f, 0.4f, 0.0f), new float3(1f, 0f, 0f), "2 cm inside the +x face");
            // Deep inside nothing is near.
            AssertNothing(s, new float3(-1.4f, 0.4f, 0.0f), 0.08f, "the middle of the table's box");
        }

        [Test]
        public void Hands_Surfaces_Closest_ABoxOnTheFloorHasNoUndersideToTouch()
        {
            RoomSurfaces s = Surfaces(Table());
            // 1 cm above the floor inside the table's footprint: the floor, facing up, not the box's own bottom face.
            AssertClosest(s, new float3(-1.4f, 0.01f, 0.0f), 0.08f, new float3(-1.4f, 0f, 0f), new float3(0f, 1f, 0f), "above the floor under the table");
            AssertClosest(s, new float3(-1.4f, -0.02f, 0.0f), 0.08f, new float3(-1.4f, 0f, 0f), new float3(0f, 1f, 0f), "below the floor under the table");
        }

        [Test]
        public void Hands_Surfaces_Closest_ATurnedBoxAboveTheFloorHasAnUnderside()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            var shelf = new RoomObject { Id = 1, Category = ObjectCategory.Storage, Base = new float3(0.5f, 1.0f, -0.5f), Size = new float3(0.6f, 0.3f, 0.4f), Yaw = 0.5f };
            room.Objects = new[] { shelf };
            RoomSurfaces s = Surfaces(room);

            AssertClosest(s, new float3(0.5f, 0.95f, -0.5f), 0.08f, new float3(0.5f, 1.0f, -0.5f), new float3(0f, -1f, 0f), "under the shelf");
            AssertClosest(s, new float3(0.5f, 1.35f, -0.5f), 0.08f, new float3(0.5f, 1.3f, -0.5f), new float3(0f, 1f, 0f), "on the shelf");

            // The shelf's local +X, turned by its yaw: (cos, 0, -sin) in the room.
            float3 along = new float3(math.cos(0.5f), 0f, -math.sin(0.5f));
            float3 centre = new float3(0.5f, 1.15f, -0.5f);
            AssertClosest(s, centre + along * (0.3f + 0.03f), 0.08f, centre + along * 0.3f, along, "the shelf's +X end");
            AssertClosest(s, centre - along * (0.3f + 0.03f), 0.08f, centre - along * 0.3f, -along, "the shelf's -X end");
            // Local +Z is (sin, 0, cos).
            float3 across = new float3(math.sin(0.5f), 0f, math.cos(0.5f));
            AssertClosest(s, centre + across * (0.2f + 0.03f), 0.08f, centre + across * 0.2f, across, "the shelf's +Z side");
        }

        // ---- Reach, switches, room -----------------------------------------------------------------------------

        [Test]
        public void Hands_Surfaces_Closest_NothingBeyondMaxDistance()
        {
            RoomSurfaces s = Surfaces(RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3));
            float3 query = new float3(0f, 1.2f, 0f);    // 1.2 m above the floor, 1.5 m from every wall
            AssertNothing(s, query, 1.0f, "short reach");
            AssertNothing(s, query, 1.19f, "just short");
            AssertClosest(s, query, 1.21f, new float3(0f, 0f, 0f), new float3(0f, 1f, 0f), "just enough");
            AssertNothing(s, query, 0f, "no reach");
            AssertNothing(s, query, -1f, "negative reach");
            AssertNothing(s, query, float.NaN, "NaN reach");
            AssertNothing(s, new float3(float.NaN, 0.01f, 0f), 1f, "NaN point");
        }

        [Test]
        public void Hands_Surfaces_Closest_NothingWhenDisabledOrWithNoRoom()
        {
            var empty = new RoomSurfaces();
            Assert.That(empty.Enabled, Is.True, "a new set of surfaces is on");
            AssertNothing(empty, new float3(0f, 0.01f, 0f), 1f, "no room yet");

            RoomSurfaces s = Surfaces(Table());
            float3 onAWall = new float3(1.07f, 1.0f, 0.5f);
            AssertClosest(s, onAWall, 0.08f, new float3(1.1f, 1.0f, 0.5f), new float3(-1f, 0f, 0f), "enabled");
            s.Enabled = false;
            AssertNothing(s, onAWall, 0.08f, "disabled");
            AssertNothing(s, new float3(-0.5f, 0.01f, -1f), 1f, "disabled, floor");
            s.Enabled = true;
            AssertClosest(s, onAWall, 0.08f, new float3(1.1f, 1.0f, 0.5f), new float3(-1f, 0f, 0f), "enabled again");

            s.SetRoom(null);
            AssertNothing(s, onAWall, 0.08f, "room cleared");
            AssertNothing(s, new float3(-0.5f, 0.01f, -1f), 1f, "room cleared, floor");
        }

        [Test]
        public void Hands_Surfaces_Closest_SetRoomReplacesTheRoom()
        {
            RoomSurfaces s = Surfaces(Table());
            AssertClosest(s, new float3(1.07f, 1.0f, 0.5f), 0.08f, new float3(1.1f, 1.0f, 0.5f), new float3(-1f, 0f, 0f), "first room");
            s.SetRoom(RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3));
            // The first room's wall is gone; the second room's walls stand at +-1.5.
            AssertNothing(s, new float3(1.07f, 1.0f, 0.5f), 0.08f, "the first room's wall");
            AssertClosest(s, new float3(1.47f, 1.0f, 0.5f), 0.08f, new float3(1.5f, 1.0f, 0.5f), new float3(-1f, 0f, 0f), "second room");
        }

        [Test]
        public void Hands_Surfaces_Closest_ARoomWithNoWallsOrObjectsStillHasAFloor()
        {
            var room = new RoomModel { Outline = new[] { new float2(-1f, -1f), new float2(1f, -1f), new float2(1f, 1f), new float2(-1f, 1f) } };
            RoomSurfaces s = Surfaces(room);
            AssertClosest(s, new float3(0.2f, 0.03f, 0.2f), 0.08f, new float3(0.2f, 0f, 0.2f), new float3(0f, 1f, 0f), "floor");
            AssertNothing(s, new float3(1.5f, 0.03f, 0.0f), 0.3f, "beyond the outline");
            s.SetRoom(new RoomModel());
            AssertNothing(s, new float3(0.2f, 0.03f, 0.2f), 1f, "no outline at all");
        }

        // ---- Everything at once --------------------------------------------------------------------------------

        static readonly string[] Rooms = { RoomFixtures.Sample4x5Table, RoomFixtures.SampleL3x4, RoomFixtures.BudgetRoom };

        /// <summary>
        /// A cloud of points on every surface of the room, from the RoomModel's own numbers by a different route
        /// from the one under test: walls with their openings cut out, floor cells inside the outline, and the six
        /// faces of every box.
        /// </summary>
        static List<float3> SampleSurfaces(RoomModel room, float step)
        {
            var cloud = new List<float3>();
            foreach (RoomWall wall in room.Walls)
            {
                float length = wall.Length;
                float2 along = (wall.B - wall.A) / length;
                for (float u = 0f; u <= length + 1e-4f; u += step)
                {
                    for (float v = 0f; v <= wall.Height + 1e-4f; v += step)
                    {
                        bool inAnOpening = false;
                        foreach (WallOpening o in wall.Openings)
                            if (u > o.Offset + 1e-4f && u < o.Offset + o.Width - 1e-4f && v > o.Bottom + 1e-4f && v < o.Top - 1e-4f)
                                inAnOpening = true;
                        if (!inAnOpening) cloud.Add(new float3(wall.A.x + along.x * u, v, wall.A.y + along.y * u));
                    }
                }
            }

            float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
            foreach (float2 corner in room.Outline) { lo = math.min(lo, corner); hi = math.max(hi, corner); }
            for (float x = lo.x; x <= hi.x; x += step)
                for (float z = lo.y; z <= hi.y; z += step)
                    if (WalkablePolygon.RingContains(room.Outline, new float2(x, z))) cloud.Add(new float3(x, 0f, z));
            // The floor's rim, where the walls meet it.
            foreach (RoomWall wall in room.Walls)
            {
                float2 along = (wall.B - wall.A) / wall.Length;
                for (float u = 0f; u <= wall.Length; u += step) cloud.Add(new float3(wall.A.x + along.x * u, 0f, wall.A.y + along.y * u));
            }

            foreach (RoomObject box in room.Objects)
            {
                float3 half = box.Size * 0.5f;
                for (float a = -half.x; a <= half.x + 1e-4f; a += step)
                {
                    for (float b = -half.y; b <= half.y + 1e-4f; b += step)
                    {
                        AddBoxPoint(cloud, box, new float3(a, b, half.z));
                        AddBoxPoint(cloud, box, new float3(a, b, -half.z));
                    }
                    for (float c = -half.z; c <= half.z + 1e-4f; c += step)
                    {
                        AddBoxPoint(cloud, box, new float3(a, half.y, c));
                        AddBoxPoint(cloud, box, new float3(a, -half.y, c));
                    }
                }
                for (float b = -half.y; b <= half.y + 1e-4f; b += step)
                {
                    for (float c = -half.z; c <= half.z + 1e-4f; c += step)
                    {
                        AddBoxPoint(cloud, box, new float3(half.x, b, c));
                        AddBoxPoint(cloud, box, new float3(-half.x, b, c));
                    }
                }
            }
            return cloud;
        }

        static void AddBoxPoint(List<float3> cloud, RoomObject box, float3 local)
        {
            cloud.Add(box.Base + new float3(0f, 0.5f * box.Size.y, 0f) + math.mul(box.Rotation, local));
        }

        static float NearestSample(List<float3> cloud, float3 q)
        {
            float best = float.MaxValue;
            for (int i = 0; i < cloud.Count; i++) best = math.min(best, math.distancesq(cloud[i], q));
            return math.sqrt(best);
        }

        static bool InsideABox(RoomModel room, float3 q)
        {
            foreach (RoomObject box in room.Objects)
            {
                float3 local = math.mul(math.conjugate(box.Rotation), q - (box.Base + new float3(0f, 0.5f * box.Size.y, 0f)));
                if (math.all(math.abs(local) <= box.Size * 0.5f)) return true;
            }
            return false;
        }

        [TestCaseSource(nameof(Rooms))]
        public void Hands_Surfaces_Closest_AgreesWithASampledCloudOfTheSameSurfaces(string name)
        {
            RoomModel room = RoomFixtures.LoadFromProject(name);
            RoomSurfaces s = Surfaces(room);
            const float step = 0.02f, reach = 0.3f;
            // A sample is at most this far from the nearest true point of a surface: half a cell's diagonal, and a
            // little more where an opening's edge cuts a cell.
            const float sampling = 0.035f;
            List<float3> cloud = SampleSurfaces(room, step);

            float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
            foreach (float2 corner in room.Outline) { lo = math.min(lo, corner); hi = math.max(hi, corner); }
            var random = new System.Random(12345);
            int found = 0, checkedNormals = 0;
            for (int k = 0; k < 400; k++)
            {
                float3 q = new float3(
                    math.lerp(lo.x - 0.4f, hi.x + 0.4f, (float)random.NextDouble()),
                    math.lerp(-0.1f, 2.6f, (float)random.NextDouble()),
                    math.lerp(lo.y - 0.4f, hi.y + 0.4f, (float)random.NextDouble()));
                float sampled = NearestSample(cloud, q);
                float3 point, normal;
                bool hit = s.TryClosest(q, reach, out point, out normal);
                string at = name + " at " + q;
                if (!hit)
                {
                    // Nothing within reach: and every sample, lying on a surface, is at least as far as the surface.
                    Assert.That(sampled, Is.GreaterThan(reach - 1e-4f), at + ": the cloud has a surface the query missed");
                    continue;
                }
                found++;
                float distance = math.distance(q, point);
                Assert.That(distance, Is.LessThanOrEqualTo(reach + 1e-5f), at);
                // No sample is nearer than the true closest point, and one is not far from it.
                Assert.That(sampled, Is.GreaterThanOrEqualTo(distance - 1e-4f), at + ": a sample is nearer than the answer");
                Assert.That(sampled, Is.LessThanOrEqualTo(distance + sampling), at + ": the answer is nearer than any surface");
                // The point lies on a surface (and not in the gap of an opening).
                Assert.That(NearestSample(cloud, point), Is.LessThanOrEqualTo(sampling), at + ": the point is on no surface");
                Assert.That(math.length(normal), Is.EqualTo(1f).Within(1e-4f), at + ": normal length");

                bool inTheRoom = WalkablePolygon.RingContains(room.Outline, q.xz) && q.y >= 0f;
                if (!inTheRoom) continue;
                if (InsideABox(room, q)) continue;
                // In the room and out of the furniture: the open side is the side the fingertip is on.
                Assert.That(math.dot(q - point, normal), Is.GreaterThanOrEqualTo(-1e-4f), at + ": the normal points away from the fingertip");
                checkedNormals++;
            }
            Assert.That(found, Is.GreaterThan(40), name + ": the sweep found too few surfaces to mean anything");
            Assert.That(checkedNormals, Is.GreaterThan(20), name + ": too few normals checked");
        }

        [TestCaseSource(nameof(Rooms))]
        public void Hands_Surfaces_Closest_NormalsPointToTheOpenSide(string name)
        {
            RoomModel room = RoomFixtures.LoadFromProject(name);
            RoomSurfaces s = Surfaces(room);
            float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
            foreach (float2 corner in room.Outline) { lo = math.min(lo, corner); hi = math.max(hi, corner); }

            // A fingertip in every 5 cm of the room's floor plane and every 10 cm of its height, out of the
            // furniture: wherever a surface answers, the normal has unit length and the fingertip is on its open side.
            int answered = 0;
            string firstFault = null;
            for (float x = lo.x + 0.013f; x < hi.x; x += 0.05f)
            {
                for (float z = lo.y + 0.017f; z < hi.y; z += 0.05f)
                {
                    if (!WalkablePolygon.RingContains(room.Outline, new float2(x, z))) continue;
                    for (float y = 0.011f; y < 2.4f; y += 0.1f)
                    {
                        float3 q = new float3(x, y, z);
                        if (InsideABox(room, q)) continue;
                        float3 point, normal;
                        if (!s.TryClosest(q, 0.5f, out point, out normal)) continue;
                        answered++;
                        float side = math.dot(q - point, normal);
                        bool unit = math.abs(math.length(normal) - 1f) <= 1e-4f;
                        if (firstFault == null && (!unit || side < -1e-4f))
                            firstFault = "at " + q + ": point " + point + ", normal " + normal + ", side " + side;
                    }
                }
            }
            Assert.That(firstFault, Is.Null, name + " " + firstFault);
            Assert.That(answered, Is.GreaterThan(500), name + ": too few answers to mean anything");
        }

        // ---- Allocation ----------------------------------------------------------------------------------------

        [Test]
        public void Hands_Surfaces_Closest_AllocatesNothing()
        {
            RoomSurfaces s = Surfaces(RoomFixtures.LoadFromProject(RoomFixtures.BudgetRoom));
            float3 point, normal;
            float sum = 0f;
            for (int i = 0; i < 400; i++)   // warm up: the first calls compile
            {
                s.TryClosest(new float3(i * 0.01f - 2f, 0.5f, i * 0.012f - 2.4f), 0.3f, out point, out normal);
                sum += point.x + normal.y;
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 4000; i++)
            {
                float3 q = new float3((i % 97) * 0.045f - 2.1f, (i % 31) * 0.08f, (i % 113) * 0.047f - 2.6f);
                if (s.TryClosest(q, 0.3f, out point, out normal)) sum += point.x + normal.y;
                if (s.TryClosest(q, 0.02f, out point, out normal)) sum += point.z;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0), "bytes allocated by 8000 queries");
            Assert.That(float.IsNaN(sum), Is.False);
        }
    }
}
