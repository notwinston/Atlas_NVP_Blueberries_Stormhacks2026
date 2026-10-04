using Atlas.Scan.Grid;
using Atlas.Scan.Tests.Routes;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Objects
{
    /// <summary>The facing rule (mesh design section 5.2, brief S4): after it, local +Z is the front of every object.</summary>
    public class ObjectFacingTests
    {
        const float Quarter = 0.5f * math.PI;
        const int N = ObjectProfile.Side;

        /// <summary>The floor direction of local +Z for a yaw.</summary>
        static float2 Front(float yaw)
        {
            return new float2(math.sin(yaw), math.cos(yaw));
        }

        static float AngleBetween(float a, float b)
        {
            return math.acos(math.clamp(math.dot(Front(a), Front(b)), -1f, 1f));
        }

        static byte[] Filled(byte value)
        {
            var profile = new byte[ObjectProfile.Length];
            for (int c = 0; c < profile.Length; c++) profile[c] = value;
            return profile;
        }

        static void Fill(byte[] profile, int i0, int i1, int j0, int j1, byte value)
        {
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                    profile[ObjectProfile.Index(i, j)] = value;
        }

        static RoomObject Table(float x, float z, float w = 1.2f, float d = 0.8f, float yaw = 0f)
        {
            return new RoomObject { Id = 9, Category = ObjectCategory.Table, Base = new float3(x, 0f, z), Size = new float3(w, 0.75f, d), Yaw = yaw };
        }

        /// <summary>A 3 m x 4 m room with one box in it, as the builder makes it; the object comes back in the room frame.</summary>
        static RoomModel RoomWith(ObjectCategory category, float3 basePoint, float3 size, float yaw)
        {
            var box = new ObstacleBox { Base = basePoint, Size = size, Yaw = yaw, Category = (int)category, SourceId = 1 };
            RoomModel room = RoomModelBuilder.FromOutline("facing", ScanTestRooms.Rectangle(3f, 4f), new[] { box }, new float2(1.5f, 2.6f));
            Assert.That(room, Is.Not.Null);
            Assert.That(room.Objects.Length, Is.EqualTo(1));
            return room;
        }

        // ---- the profile rule ----

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void Chair_WithItsScannedBackOnAnySide_EndsWithTheBackOnLocalMinusZ(int side)
        {
            // The real chair: wider than deep, its front along a yaw of 0.4.
            var size = new float3(0.75f, 1f, 0.5f);
            var basePoint = new float3(1.5f, 0f, -0.75f);
            const float yaw = 0.4f;
            ScannedShapes chair = ScannedShapes.Chair(size.x, size.y, size.z, 0.4375f, 0.0625f).Place(basePoint, yaw);
            byte[] expected = ObjectProfile.Compute(chair.Vertices, chair.Indices, chair.Classes, basePoint, size, yaw);

            // The box as the scan reports it: the same footprint, its axis some quarter turns away from the chair's front.
            float3 boxSize = (side & 1) != 0 ? new float3(size.z, size.y, size.x) : size;
            float boxYaw = yaw - side * Quarter;
            byte[] profile = ObjectProfile.Compute(chair.Vertices, chair.Indices, chair.Classes, basePoint, boxSize, boxYaw);

            ObjectFacingRule rule;
            int turns = ObjectFacing.Decide(ObjectCategory.Chair, basePoint, boxSize, boxYaw, profile, null, null, out rule);
            Assert.That(rule, Is.EqualTo(ObjectFacingRule.Profile));
            Assert.That(turns, Is.EqualTo(side));

            ObjectFacing.Apply(turns, ref boxYaw, ref boxSize, ref profile);
            Assert.That(AngleBetween(boxYaw, yaw), Is.LessThan(1e-3f));
            Assert.That(boxYaw, Is.GreaterThan(-math.PI).And.LessThanOrEqualTo(math.PI));
            Assert.That(boxSize, Is.EqualTo(size));
            Assert.That(profile, Is.EqualTo(expected));
            // The back stands on local -Z, at the height of the box; the seat is in front of it.
            for (int i = 0; i < N; i++)
            {
                Assert.That(profile[ObjectProfile.Index(i, 0)], Is.EqualTo(255));
                Assert.That(profile[ObjectProfile.Index(i, 1)], Is.EqualTo(255));
                Assert.That(profile[ObjectProfile.Index(i, 15)], Is.EqualTo(112));       // floor(255 * 0.4375 + 0.5)
            }
        }

        [Test]
        public void Profile_OfVectorA_TurnedEachWay_IsTurnedBack()
        {
            byte[] a = ScannedShapes.VectorAProfile();
            for (int given = 0; given < 4; given++)
            {
                byte[] profile = ObjectProfile.Rotate(a, given);
                int turns;
                Assert.That(ObjectFacing.TryProfile(profile, out turns), Is.True, "turned by " + given);
                Assert.That(turns, Is.EqualTo((4 - given) % 4), "turned by " + given);
                Assert.That(ObjectProfile.Rotate(profile, turns), Is.EqualTo(a));
            }

            // The strips of vector A: the back two rows full and one at the seat; the sides two cells of back in sixteen.
            int seen;
            Assert.That(ObjectFacing.StripMean(a, 0, out seen), Is.EqualTo((2f * 255f + 128f) / (3f * 255f)).Within(1e-6f));
            Assert.That(seen, Is.EqualTo(48));
            Assert.That(ObjectFacing.StripMean(a, 1, out seen), Is.EqualTo((2f * 255f + 14f * 128f) / (16f * 255f)).Within(1e-6f));
            Assert.That(ObjectFacing.StripMean(a, 2, out seen), Is.EqualTo(128f / 255f).Within(1e-6f));
            Assert.That(ObjectFacing.StripMean(a, 3, out seen), Is.EqualTo((2f * 255f + 14f * 128f) / (16f * 255f)).Within(1e-6f));
        }

        [Test]
        public void Profile_WithNoClearBack_GivesNoAnswer()
        {
            int turns;
            // A stool: one level top.
            Assert.That(ObjectFacing.TryProfile(Filled(200), out turns), Is.False);
            // A back that stands 0.05 of the box above the seat: under the 0.15 the rule asks for.
            byte[] low = Filled(128);
            Fill(low, 0, 15, 0, 1, 141);
            Assert.That(ObjectFacing.TryProfile(low, out turns), Is.False);
            // Two backs of the same height, on opposite sides.
            byte[] two = ScannedShapes.VectorAProfile();
            Fill(two, 0, 15, 14, 15, 255);
            Assert.That(ObjectFacing.TryProfile(two, out turns), Is.False);
            Assert.That(ObjectFacing.TryProfile(null, out turns), Is.False);
            Assert.That(ObjectFacing.TryProfile(new byte[16], out turns), Is.False);
        }

        [Test]
        public void Profile_WithThinCoverage_IsNotUsed()
        {
            // A back and both arms seen, the strips clear, but only 56 of 256 cells: under 0.30.
            var thin = new byte[ObjectProfile.Length];
            Fill(thin, 3, 12, 0, 1, 255);
            Fill(thin, 0, 2, 0, 5, 64);
            Fill(thin, 13, 15, 0, 5, 64);
            Assert.That(ObjectProfile.Coverage(thin), Is.EqualTo(56f / 256f));
            int turns;
            Assert.That(ObjectFacing.TryProfile(thin, out turns), Is.False);

            // The same scan with some of the seat: 86 cells, and the same strips now answer.
            byte[] enough = (byte[])thin.Clone();
            Fill(enough, 3, 12, 2, 4, 64);
            Assert.That(ObjectProfile.Coverage(enough), Is.GreaterThanOrEqualTo(ObjectFacing.MinCoverage));
            Assert.That(ObjectFacing.TryProfile(enough, out turns), Is.True);
            Assert.That(turns, Is.EqualTo(0));
        }

        [Test]
        public void Profile_WhoseHighestStripIsBarelySeen_GivesNoAnswer()
        {
            // The middle of the seat seen well, and eight cells of something tall on one edge: under a quarter of that strip.
            var profile = new byte[ObjectProfile.Length];
            Fill(profile, 3, 12, 3, 12, 64);
            Fill(profile, 4, 11, 0, 0, 255);
            Assert.That(ObjectProfile.Coverage(profile), Is.GreaterThanOrEqualTo(ObjectFacing.MinCoverage));
            int turns;
            Assert.That(ObjectFacing.TryProfile(profile, out turns), Is.False);

            // Twelve cells of it are a quarter of the strip.
            Fill(profile, 4, 7, 1, 1, 255);
            Assert.That(ObjectFacing.TryProfile(profile, out turns), Is.True);
            Assert.That(turns, Is.EqualTo(0));
        }

        // ---- the table rule ----

        [TestCase(0f, 1f, 0)]
        [TestCase(1f, 0f, 1)]
        [TestCase(0f, -1f, 2)]
        [TestCase(-1f, 0f, 3)]
        public void Stool_BesideATable_FacesTheTable(float dx, float dz, int expectedTurns)
        {
            // A backless stool: its profile is one level top, so the profile rule has nothing to say.
            var basePoint = new float3(2f, 0f, 2f);
            var size = new float3(0.4f, 0.45f, 0.4f);
            float yaw = 0f;
            byte[] profile = Filled(255);
            RoomObject[] objects = { Table(2f + 0.9f * dx, 2f + 0.9f * dz) };

            ObjectFacingRule rule;
            int turns = ObjectFacing.Decide(ObjectCategory.Chair, basePoint, size, yaw, profile, null, objects, out rule);
            Assert.That(rule, Is.EqualTo(ObjectFacingRule.Table));
            Assert.That(turns, Is.EqualTo(expectedTurns));
            ObjectFacing.Apply(turns, ref yaw, ref size, ref profile);
            Assert.That(math.dot(Front(yaw), new float2(dx, dz)), Is.GreaterThan(0.999f));
        }

        [Test]
        public void Stool_AtAnyYaw_TurnsItsNearestSideToTheTable()
        {
            var basePoint = new float3(-1f, 0f, 0.5f);
            var size = new float3(0.4f, 0.45f, 0.4f);
            RoomObject[] objects = { Table(-0.2f, 1.1f, 1.2f, 0.8f, 0.6f) };
            float2 towards = math.normalize(objects[0].Base.xz - basePoint.xz);
            for (int step = 0; step < 16; step++)
            {
                float yaw = -3f + 0.39f * step;
                ObjectFacingRule rule;
                int turns = ObjectFacing.Decide(ObjectCategory.Chair, basePoint, size, yaw, null, null, objects, out rule);
                Assert.That(rule, Is.EqualTo(ObjectFacingRule.Table));
                byte[] none = null;
                float3 turned = size;
                ObjectFacing.Apply(turns, ref yaw, ref turned, ref none);
                // Of the four sides, the one within 45 degrees of the table.
                Assert.That(math.dot(Front(yaw), towards), Is.GreaterThanOrEqualTo(0.7071f - 1e-4f), "step " + step);
                Assert.That(none, Is.Null);
            }
        }

        [Test]
        public void Table_IsFoundByTheGapBetweenTheBoxes_UpToOneMetre()
        {
            var basePoint = new float3(0f, 0f, 0f);
            var size = new float3(0.4f, 0.45f, 0.4f);
            int turns;

            // A long table: its centre is 2.2 m away, its end 0.5 m.
            RoomObject[] longTable = { Table(2.2f, 0f, 3f, 0.9f) };
            Assert.That(ObjectFacing.TryTable(basePoint, size, 0f, longTable, out turns), Is.True);
            Assert.That(turns, Is.EqualTo(1));

            // 0.95 m of floor between the boxes is near enough, 1.05 m is not.
            Assert.That(ObjectFacing.TryTable(basePoint, size, 0f, new[] { Table(0f, -1.55f) }, out turns), Is.True);
            Assert.That(turns, Is.EqualTo(2));
            Assert.That(ObjectFacing.TryTable(basePoint, size, 0f, new[] { Table(0f, -1.65f) }, out turns), Is.False);

            // Only tables count, and the nearest of two decides.
            var couch = new RoomObject { Category = ObjectCategory.Couch, Base = new float3(0.8f, 0f, 0f), Size = new float3(0.8f, 0.8f, 2f) };
            Assert.That(ObjectFacing.TryTable(basePoint, size, 0f, new[] { couch }, out turns), Is.False);
            Assert.That(ObjectFacing.TryTable(basePoint, size, 0f, new[] { couch, Table(0f, 1.4f), null, Table(-1.1f, 0f, 0.8f, 0.8f) }, out turns), Is.True);
            Assert.That(turns, Is.EqualTo(3));
            Assert.That(ObjectFacing.TryTable(basePoint, size, 0f, null, out turns), Is.False);
            Assert.That(ObjectFacing.TryTable(basePoint, size, 0f, new RoomObject[0], out turns), Is.False);
        }

        [Test]
        public void FootprintDistance_BetweenTurnedBoxes()
        {
            var unit = new float3(1f, 1f, 1f);
            Assert.That(ObjectFacing.FootprintDistance(float2.zero, unit, 0f, new float2(2f, 0f), unit, 0f), Is.EqualTo(1f).Within(1e-5f));
            // Corner to corner.
            Assert.That(ObjectFacing.FootprintDistance(float2.zero, unit, 0f, new float2(2f, 2f), unit, 0f), Is.EqualTo(math.sqrt(2f)).Within(1e-5f));
            // A square turned an eighth points a corner at the other: 2 - 0.5 - sqrt(0.5).
            Assert.That(ObjectFacing.FootprintDistance(float2.zero, unit, 0f, new float2(2f, 0f), unit, 0.25f * math.PI),
                Is.EqualTo(1.5f - math.sqrt(0.5f)).Within(1e-5f));
            // Yaw swaps which side reaches out.
            var plank = new float3(2f, 1f, 0.2f);
            Assert.That(ObjectFacing.FootprintDistance(float2.zero, unit, 0f, new float2(3f, 0f), plank, 0f), Is.EqualTo(1.5f).Within(1e-5f));
            Assert.That(ObjectFacing.FootprintDistance(float2.zero, unit, 0f, new float2(3f, 0f), plank, Quarter), Is.EqualTo(2.4f).Within(1e-5f));
            // Touching, overlapping and one inside the other.
            Assert.That(ObjectFacing.FootprintDistance(float2.zero, unit, 0f, new float2(1f, 0f), unit, 0f), Is.EqualTo(0f));
            Assert.That(ObjectFacing.FootprintDistance(float2.zero, unit, 0f, new float2(0.6f, 0.3f), unit, 0.5f), Is.EqualTo(0f));
            Assert.That(ObjectFacing.FootprintDistance(float2.zero, new float3(3f, 1f, 3f), 0.3f, new float2(0.2f, 0.1f), unit, 1f), Is.EqualTo(0f));
        }

        // ---- the clearance rule ----

        // The middle of each wall of the 3 m x 4 m room, and the direction from that wall into the room.
        static readonly float3[] s_wallPoints = { new float3(1.5f, 0f, 0f), new float3(3f, 0f, 2f), new float3(1.5f, 0f, 4f), new float3(0f, 0f, 2f) };
        static readonly float2[] s_intoRoom = { new float2(0f, 1f), new float2(-1f, 0f), new float2(0f, -1f), new float2(1f, 0f) };

        // A box of the given footprint (along the wall, out from the wall) that stands with its back to a wall,
        // reported with its axis some quarter turns away from the room side. Returns the yaw after facing, in
        // the frame the room was described in.
        static float FacedYawAtWall(ObjectCategory category, int wall, float along, float depth, int turnedBy, out ObjectFacingRule rule, out float3 size)
        {
            float2 into = s_intoRoom[wall];
            float3 basePoint = s_wallPoints[wall] + new float3(into.x, 0f, into.y) * (0.5f * depth);
            float trueYaw = math.atan2(into.x, into.y);
            float3 boxSize = (turnedBy & 1) != 0 ? new float3(depth, 1.8f, along) : new float3(along, 1.8f, depth);
            RoomModel room = RoomWith(category, basePoint, boxSize, trueYaw - turnedBy * Quarter);

            RoomObject obj = room.Objects[0];
            int turns = ObjectFacing.Decide(obj.Category, obj.Base, obj.Size, obj.Yaw, null, room.Sdf, room.Objects, out rule);
            float yaw = obj.Yaw;
            size = obj.Size;
            byte[] none = null;
            ObjectFacing.Apply(turns, ref yaw, ref size, ref none);
            return yaw + room.Anchor.Yaw;
        }

        [Test]
        public void Storage_AgainstAWall_FacesTheRoom()
        {
            for (int wall = 0; wall < 4; wall++)
            {
                for (int turnedBy = 0; turnedBy < 4; turnedBy++)
                {
                    ObjectFacingRule rule;
                    float3 size;
                    float yaw = FacedYawAtWall(ObjectCategory.Storage, wall, 0.9f, 0.4f, turnedBy, out rule, out size);
                    string where = "wall " + wall + ", turned by " + turnedBy;
                    Assert.That(rule, Is.EqualTo(ObjectFacingRule.Clearance), where);
                    Assert.That(math.dot(Front(yaw), s_intoRoom[wall]), Is.GreaterThan(0.999f), where);
                    // Its width runs along the wall again.
                    Assert.That(size.x, Is.EqualTo(0.9f).Within(1e-5f), where);
                    Assert.That(size.z, Is.EqualTo(0.4f).Within(1e-5f), where);
                }
            }
        }

        [Test]
        public void ADeepAppliance_AgainstAWall_FacesTheRoom()
        {
            // As deep as it is wide: the floor reads equally free in front of it and at both its sides, and
            // only the front has a wall opposite.
            for (int wall = 0; wall < 4; wall++)
            {
                for (int turnedBy = 0; turnedBy < 4; turnedBy++)
                {
                    ObjectFacingRule rule;
                    float3 size;
                    float yaw = FacedYawAtWall(ObjectCategory.Refrigerator, wall, 0.7f, 0.7f, turnedBy, out rule, out size);
                    string where = "wall " + wall + ", turned by " + turnedBy;
                    Assert.That(rule, Is.EqualTo(ObjectFacingRule.Clearance), where);
                    Assert.That(math.dot(Front(yaw), s_intoRoom[wall]), Is.GreaterThan(0.999f), where);
                }
            }
        }

        [Test]
        public void Storage_InTheMiddleOfTheRoom_KeepsItsAxis()
        {
            RoomModel room = RoomWith(ObjectCategory.Storage, new float3(1.5f, 0f, 1.6f), new float3(0.9f, 1.8f, 0.4f), 0.3f);
            RoomObject obj = room.Objects[0];
            int turns;
            Assert.That(ObjectFacing.TryClearance(obj.Base, obj.Size, obj.Yaw, room.Sdf, out turns), Is.False);
            ObjectFacingRule rule;
            Assert.That(ObjectFacing.Decide(obj.Category, obj.Base, obj.Size, obj.Yaw, null, room.Sdf, room.Objects, out rule), Is.EqualTo(0));
            Assert.That(rule, Is.EqualTo(ObjectFacingRule.Box));
        }

        [Test]
        public void Clearance_WithNoDistanceField_GivesNoAnswer()
        {
            int turns;
            Assert.That(ObjectFacing.TryClearance(float3.zero, new float3(1f, 1f, 1f), 0f, null, out turns), Is.False);
            ObjectFacingRule rule;
            Assert.That(ObjectFacing.Decide(ObjectCategory.Storage, float3.zero, new float3(1f, 1f, 1f), 0f, null, null, null, out rule), Is.EqualTo(0));
            Assert.That(rule, Is.EqualTo(ObjectFacingRule.Box));
        }

        // ---- the clearance rule in a corner, and beside other furniture ----

        /// <summary>A 4 m x 5 m room with the given boxes, as the builder makes it.</summary>
        static RoomModel Room4x5(params ObstacleBox[] boxes)
        {
            RoomModel room = RoomModelBuilder.FromOutline("corner", ScanTestRooms.Rectangle(4f, 5f), boxes, new float2(2f, 2.5f));
            Assert.That(room, Is.Not.Null);
            Assert.That(room.Objects.Length, Is.EqualTo(boxes.Length));
            return room;
        }

        /// <summary>
        /// A box whose true front looks north (the room's +Z in the frame it is described in), reported with
        /// its axis some quarter turns away. along is its side along the south wall, depth the side out from it.
        /// </summary>
        static ObstacleBox FacingNorth(ObjectCategory category, float x, float z, float along, float height, float depth, int turnedBy = 0)
        {
            return new ObstacleBox
            {
                Base = new float3(x, 0f, z),
                Size = (turnedBy & 1) != 0 ? new float3(depth, height, along) : new float3(along, height, depth),
                Yaw = -turnedBy * Quarter, Category = (int)category, SourceId = 1,
            };
        }

        static RoomObject Find(RoomModel room, ObjectCategory category)
        {
            foreach (RoomObject o in room.Objects)
                if (o.Category == category) return o;
            Assert.Fail("No " + category + " in the room");
            return null;
        }

        /// <summary>The object's front after facing, in the frame the room was described in; rule is the rule that answered.</summary>
        static float2 FrontAfterFacing(RoomModel room, RoomObject obj, byte[] profile, out ObjectFacingRule rule)
        {
            int turns = ObjectFacing.Decide(obj.Category, obj.Base, obj.Size, obj.Yaw, profile, room.Sdf, room.Objects, out rule);
            return Front(obj.Yaw + turns * Quarter + room.Anchor.Yaw);
        }

        // The south-west corner of the room: the object's back on the south wall, its side on the west wall or
        // a few centimetres off it. A bed and a toilet come with a level profile, which says nothing.
        static readonly object[] s_cornerRows =
        {
            new object[] { "a refrigerator 0.6 x 0.6, flush", ObjectCategory.Refrigerator, 0.6f, 1.8f, 0.6f, 0f, false },
            new object[] { "a cupboard 0.5 x 0.6, flush", ObjectCategory.Storage, 0.5f, 1.8f, 0.6f, 0f, false },
            new object[] { "a bed 1.4 x 2.0, its head on one wall and its side on the other", ObjectCategory.Bed, 1.4f, 0.5f, 2.0f, 0f, true },
            new object[] { "a toilet 0.4 x 0.7, flush to the side wall", ObjectCategory.Toilet, 0.4f, 0.8f, 0.7f, 0f, true },
            new object[] { "a refrigerator 0.6 x 0.7, 3 cm off the side wall", ObjectCategory.Refrigerator, 0.6f, 1.8f, 0.7f, 0.03f, false },
            new object[] { "a washer 0.6 x 0.6, 10 cm off the side wall", ObjectCategory.WasherDryer, 0.6f, 0.85f, 0.6f, 0.10f, false },
            new object[] { "storage 1.2 x 0.4, flush", ObjectCategory.Storage, 1.2f, 1.8f, 0.4f, 0f, false },
        };

        [TestCaseSource(nameof(s_cornerRows))]
        public void InARoomCorner_ABoxThatIsRight_IsNotTurned(string what, ObjectCategory category, float along, float height, float depth,
            float offTheSideWall, bool levelProfile)
        {
            RoomModel room = Room4x5(FacingNorth(category, offTheSideWall + 0.5f * along, 0.5f * depth, along, height, depth));
            RoomObject obj = room.Objects[0];
            ObjectFacingRule rule;
            float2 front = FrontAfterFacing(room, obj, levelProfile ? Filled(200) : null, out rule);
            Assert.That(math.dot(front, new float2(0f, 1f)), Is.GreaterThan(0.999f), what + ": the front must still look into the room");
        }

        [TestCaseSource(nameof(s_cornerRows))]
        public void InARoomCorner_TheFloorCannotTell_AndGivesNoAnswer(string what, ObjectCategory category, float along, float height, float depth,
            float offTheSideWall, bool levelProfile)
        {
            // Two sides have free floor in front and a wall behind. Whichever way the box was reported, it keeps its axis.
            for (int turnedBy = 0; turnedBy < 4; turnedBy++)
            {
                RoomModel room = Room4x5(FacingNorth(category, offTheSideWall + 0.5f * along, 0.5f * depth, along, height, depth, turnedBy));
                RoomObject obj = room.Objects[0];
                int turns;
                Assert.That(ObjectFacing.TryClearance(obj.Base, obj.Size, obj.Yaw, room.Sdf, out turns), Is.False, what + ", turned by " + turnedBy);
                ObjectFacingRule rule;
                turns = ObjectFacing.Decide(category, obj.Base, obj.Size, obj.Yaw, levelProfile ? Filled(200) : null, room.Sdf, room.Objects, out rule);
                Assert.That(rule, Is.EqualTo(ObjectFacingRule.Box), what + ", turned by " + turnedBy);
                Assert.That(turns, Is.EqualTo(0), what + ", turned by " + turnedBy);
            }
        }

        [Test]
        public void InEveryCornerOfTheRoom_TheFloorGivesNoAnswer()
        {
            // The flush refrigerator in each of the four corners, its front along each of the two free directions.
            var corners = new[] { new float2(0.3f, 0.3f), new float2(3.7f, 0.3f), new float2(3.7f, 4.7f), new float2(0.3f, 4.7f) };
            foreach (float2 corner in corners)
            {
                for (int quarter = 0; quarter < 4; quarter++)
                {
                    var box = new ObstacleBox
                    {
                        Base = new float3(corner.x, 0f, corner.y), Size = new float3(0.6f, 1.8f, 0.6f), Yaw = quarter * Quarter,
                        Category = (int)ObjectCategory.Refrigerator, SourceId = 1,
                    };
                    RoomModel room = Room4x5(box);
                    RoomObject obj = room.Objects[0];
                    int turns;
                    Assert.That(ObjectFacing.TryClearance(obj.Base, obj.Size, obj.Yaw, room.Sdf, out turns), Is.False, "corner " + corner + ", yaw " + quarter);
                }
            }
        }

        [Test]
        public void ANeighbourStandingAgainstOneSide_MakesACorner()
        {
            // A cupboard on the south wall with a second one flush against its west side: free floor in front of it
            // and to the east, a wall behind and furniture to the west. The floor cannot tell north from east.
            for (int turnedBy = 0; turnedBy < 4; turnedBy++)
            {
                RoomModel room = Room4x5(
                    FacingNorth(ObjectCategory.Storage, 2f, 0.2f, 0.9f, 1.8f, 0.4f, turnedBy),
                    FacingNorth(ObjectCategory.Refrigerator, 1.2f, 0.35f, 0.7f, 1.8f, 0.7f));
                RoomObject obj = Find(room, ObjectCategory.Storage);
                int turns;
                Assert.That(ObjectFacing.TryClearance(obj.Base, obj.Size, obj.Yaw, room.Sdf, out turns), Is.False, "turned by " + turnedBy);
            }
        }

        [Test]
        public void ANeighbourHalfAMetreOff_DoesNotStopTheAnswer()
        {
            // The same cupboard with the other piece 0.5 m from its side: the floor between them is free, so it
            // stands against the wall only, and the front is the room side whichever way the box was reported.
            for (int turnedBy = 0; turnedBy < 4; turnedBy++)
            {
                RoomModel room = Room4x5(
                    FacingNorth(ObjectCategory.Storage, 2f, 0.2f, 0.9f, 1.8f, 0.4f, turnedBy),
                    FacingNorth(ObjectCategory.Refrigerator, 0.7f, 0.35f, 0.7f, 1.8f, 0.7f));
                ObjectFacingRule rule;
                float2 front = FrontAfterFacing(room, Find(room, ObjectCategory.Storage), null, out rule);
                Assert.That(rule, Is.EqualTo(ObjectFacingRule.Clearance), "turned by " + turnedBy);
                Assert.That(math.dot(front, new float2(0f, 1f)), Is.GreaterThan(0.999f), "turned by " + turnedBy);
            }
        }

        [Test]
        public void BetweenTwoNeighbours_OnlyTheRoomSideIsFree_AndIsTheFront()
        {
            // An oven in a row of kitchen units: furniture 10 cm from both its sides, the wall behind.
            for (int turnedBy = 0; turnedBy < 4; turnedBy++)
            {
                RoomModel room = Room4x5(
                    FacingNorth(ObjectCategory.Oven, 2f, 0.3f, 0.6f, 0.9f, 0.6f, turnedBy),
                    FacingNorth(ObjectCategory.Storage, 1.2f, 0.3f, 0.8f, 0.9f, 0.6f),
                    FacingNorth(ObjectCategory.Refrigerator, 2.75f, 0.35f, 0.7f, 1.8f, 0.7f));
                ObjectFacingRule rule;
                float2 front = FrontAfterFacing(room, Find(room, ObjectCategory.Oven), null, out rule);
                Assert.That(rule, Is.EqualTo(ObjectFacingRule.Clearance), "turned by " + turnedBy);
                Assert.That(math.dot(front, new float2(0f, 1f)), Is.GreaterThan(0.999f), "turned by " + turnedBy);
            }
        }

        [TestCase(0.05f, true)]
        [TestCase(0.25f, true)]
        [TestCase(0.45f, false)]
        [TestCase(1.0f, false)]
        public void StandingOffTheWall_TheAnswerNeedsTheWallWithinReach(float gap, bool answers)
        {
            // The probe behind the box is 0.30 m out: a wall within about that is what the object stands against.
            RoomModel room = Room4x5(FacingNorth(ObjectCategory.Storage, 2f, gap + 0.2f, 0.9f, 1.8f, 0.4f, 2));
            RoomObject obj = room.Objects[0];
            int turns;
            Assert.That(ObjectFacing.TryClearance(obj.Base, obj.Size, obj.Yaw, room.Sdf, out turns), Is.EqualTo(answers));
            if (answers) Assert.That(math.dot(Front(obj.Yaw + turns * Quarter + room.Anchor.Yaw), new float2(0f, 1f)), Is.GreaterThan(0.999f));
        }

        // ---- the lists ----

        [Test]
        public void ATable_AndAnUnknownObject_AreUntouched()
        {
            // Everything the other rules would answer to: a clear back in the profile, a table beside it, a wall behind it.
            RoomModel room = RoomWith(ObjectCategory.Unknown, new float3(1.5f, 0f, 0.3f), new float3(0.6f, 0.9f, 0.6f), Quarter);
            RoomObject obj = room.Objects[0];
            RoomObject[] objects = { obj, Table(obj.Base.x + 0.9f, obj.Base.z) };
            byte[] profile = ObjectProfile.Rotate(ScannedShapes.VectorAProfile(), 1);
            int turns;
            Assert.That(ObjectFacing.TryProfile(profile, out turns), Is.True);
            Assert.That(ObjectFacing.TryTable(obj.Base, obj.Size, obj.Yaw, objects, out turns), Is.True);
            Assert.That(ObjectFacing.TryClearance(obj.Base, obj.Size, obj.Yaw, room.Sdf, out turns), Is.True);

            foreach (ObjectCategory category in new[]
                     {
                         ObjectCategory.Table, ObjectCategory.Unknown, ObjectCategory.Other, ObjectCategory.Lamp, ObjectCategory.Plant,
                         ObjectCategory.Stairs, ObjectCategory.Keyboard, ObjectCategory.Mouse, ObjectCategory.Laptop
                     })
            {
                ObjectFacingRule rule;
                turns = ObjectFacing.Decide(category, obj.Base, obj.Size, obj.Yaw, profile, room.Sdf, objects, out rule);
                Assert.That(rule, Is.EqualTo(ObjectFacingRule.Box), category.ToString());
                Assert.That(turns, Is.EqualTo(0), category.ToString());
            }

            float yaw = obj.Yaw;
            float3 size = obj.Size;
            byte[] kept = profile;
            ObjectFacing.Apply(0, ref yaw, ref size, ref kept);
            Assert.That(yaw, Is.EqualTo(obj.Yaw));
            Assert.That(size, Is.EqualTo(obj.Size));
            Assert.That(kept, Is.SameAs(profile));
        }

        [Test]
        public void AChairWithAThinScan_FallsThroughToTheNextRule()
        {
            // The scan saw the back and the arms only (coverage 0.22): the profile is not used.
            var thin = new byte[ObjectProfile.Length];
            Fill(thin, 3, 12, 0, 1, 255);
            Fill(thin, 0, 2, 0, 5, 64);
            Fill(thin, 13, 15, 0, 5, 64);

            // In a room, against the south wall, its box axis pointing east along the wall, with a table to the west.
            RoomModel room = RoomWith(ObjectCategory.Chair, new float3(1.5f, 0f, 0.25f), new float3(0.5f, 0.9f, 0.5f), Quarter);
            RoomObject chair = room.Objects[0];
            float2 west = -Front(Quarter - room.Anchor.Yaw);       // the -X of the frame the room was described in
            RoomObject[] withTable = { chair, Table(chair.Base.x + 0.9f * west.x, chair.Base.z + 0.9f * west.y) };

            // The table is the next rule in a chair's list.
            ObjectFacingRule rule;
            int turns = ObjectFacing.Decide(ObjectCategory.Chair, chair.Base, chair.Size, chair.Yaw, thin, room.Sdf, withTable, out rule);
            Assert.That(rule, Is.EqualTo(ObjectFacingRule.Table));
            Assert.That(turns, Is.EqualTo(2));
            Assert.That(math.dot(Front(chair.Yaw + turns * Quarter), west), Is.GreaterThan(0.999f));

            // With no table near, the free floor decides: the front turns away from the wall.
            turns = ObjectFacing.Decide(ObjectCategory.Chair, chair.Base, chair.Size, chair.Yaw, thin, room.Sdf, room.Objects, out rule);
            Assert.That(rule, Is.EqualTo(ObjectFacingRule.Clearance));
            Assert.That(turns, Is.EqualTo(3));
            Assert.That(math.dot(Front(chair.Yaw + turns * Quarter + room.Anchor.Yaw), new float2(0f, 1f)), Is.GreaterThan(0.999f));

            // With neither, the box keeps its axis.
            turns = ObjectFacing.Decide(ObjectCategory.Chair, chair.Base, chair.Size, chair.Yaw, thin, null, null, out rule);
            Assert.That(rule, Is.EqualTo(ObjectFacingRule.Box));
            Assert.That(turns, Is.EqualTo(0));

            // A good scan of the same chair is used before any of them.
            byte[] good = ObjectProfile.Rotate(ScannedShapes.VectorAProfile(), 3);
            turns = ObjectFacing.Decide(ObjectCategory.Chair, chair.Base, chair.Size, chair.Yaw, good, room.Sdf, withTable, out rule);
            Assert.That(rule, Is.EqualTo(ObjectFacingRule.Profile));
            Assert.That(turns, Is.EqualTo(1));
        }

        [Test]
        public void ACouch_UsesItsBackThenTheFreeFloor_NeverTheTable()
        {
            var basePoint = new float3(0f, 0f, 0f);
            var size = new float3(2f, 0.8f, 0.9f);
            RoomObject[] objects = { Table(0f, 1.2f) };
            ObjectFacingRule rule;

            int turns = ObjectFacing.Decide(ObjectCategory.Couch, basePoint, size, 0f, ObjectProfile.Rotate(ScannedShapes.VectorAProfile(), 2), null, objects, out rule);
            Assert.That(rule, Is.EqualTo(ObjectFacingRule.Profile));
            Assert.That(turns, Is.EqualTo(2));

            // A level scan (a bed, a bench): the coffee table in front of it is not asked.
            turns = ObjectFacing.Decide(ObjectCategory.Couch, basePoint, size, 0f, Filled(140), null, objects, out rule);
            Assert.That(rule, Is.EqualTo(ObjectFacingRule.Box));
            Assert.That(turns, Is.EqualTo(0));
        }

        [Test]
        public void Tries_FollowsTheListsOfTheDesign()
        {
            for (int c = 0; c < ObjectCategories.Count; c++)
            {
                var category = (ObjectCategory)c;
                bool profile, table, clearance;
                switch (category)
                {
                    case ObjectCategory.Chair:
                        profile = table = clearance = true;
                        break;
                    case ObjectCategory.Couch:
                    case ObjectCategory.Bed:
                    case ObjectCategory.Toilet:
                        profile = clearance = true;
                        table = false;
                        break;
                    case ObjectCategory.Storage:
                    case ObjectCategory.Screen:
                    case ObjectCategory.Refrigerator:
                    case ObjectCategory.Oven:
                    case ObjectCategory.Dishwasher:
                    case ObjectCategory.WasherDryer:
                    case ObjectCategory.Stove:
                    case ObjectCategory.Fireplace:
                    case ObjectCategory.Sink:
                    case ObjectCategory.Bathtub:
                        clearance = true;
                        profile = table = false;
                        break;
                    default:
                        profile = table = clearance = false;
                        break;
                }
                Assert.That(ObjectFacing.Tries(category, ObjectFacingRule.Profile), Is.EqualTo(profile), category + " profile");
                Assert.That(ObjectFacing.Tries(category, ObjectFacingRule.Table), Is.EqualTo(table), category + " table");
                Assert.That(ObjectFacing.Tries(category, ObjectFacingRule.Clearance), Is.EqualTo(clearance), category + " clearance");
                Assert.That(ObjectFacing.Tries(category, ObjectFacingRule.Box), Is.True, category + " box");
            }
            // Nine categories keep the box's axis, ten ask the floor only.
            int boxOnly = 0, floorOnly = 0;
            for (int c = 0; c < ObjectCategories.Count; c++)
            {
                bool clearance = ObjectFacing.Tries((ObjectCategory)c, ObjectFacingRule.Clearance);
                bool profile = ObjectFacing.Tries((ObjectCategory)c, ObjectFacingRule.Profile);
                if (!clearance) boxOnly++;
                else if (!profile) floorOnly++;
            }
            Assert.That(boxOnly, Is.EqualTo(9));
            Assert.That(floorOnly, Is.EqualTo(10));
        }

        // ---- applying the answer ----

        [Test]
        public void Apply_TurnsTheYawTheSizeAndTheProfileTogether()
        {
            byte[] a = ScannedShapes.VectorAProfile();

            float yaw = 0.25f;
            var size = new float3(0.6f, 1f, 0.4f);
            byte[] profile = a;
            ObjectFacing.Apply(1, ref yaw, ref size, ref profile);
            Assert.That(yaw, Is.EqualTo(0.25f + Quarter).Within(1e-6f));
            Assert.That(size, Is.EqualTo(new float3(0.4f, 1f, 0.6f)));
            Assert.That(profile, Is.EqualTo(ObjectProfile.Rotate(a, 1)));
            Assert.That(a, Is.EqualTo(ScannedShapes.VectorAProfile()));       // the array handed in is not changed

            // Two turns keep the sides; the yaw comes back into (-pi, pi].
            yaw = 3f;
            size = new float3(0.6f, 1f, 0.4f);
            profile = a;
            ObjectFacing.Apply(2, ref yaw, ref size, ref profile);
            Assert.That(yaw, Is.EqualTo(3f - math.PI).Within(1e-6f));
            Assert.That(size, Is.EqualTo(new float3(0.6f, 1f, 0.4f)));
            Assert.That(profile, Is.EqualTo(ObjectProfile.Rotate(a, 2)));

            yaw = 3f;
            ObjectFacing.Apply(3, ref yaw, ref size, ref profile);
            Assert.That(yaw, Is.EqualTo(3f - Quarter).Within(1e-6f));
            Assert.That(size, Is.EqualTo(new float3(0.4f, 1f, 0.6f)));

            yaw = -3f;
            ObjectFacing.Apply(-1, ref yaw, ref size, ref profile);       // the same as three turns
            Assert.That(yaw, Is.EqualTo(-3f - Quarter + 2f * math.PI).Within(1e-6f));
            Assert.That(size, Is.EqualTo(new float3(0.6f, 1f, 0.4f)));

            // Half a turn from zero looks the other way and stays in (-pi, pi]. What a RoomModel then stores is
            // the builder's business (RoomModelBuilder.StoredYaw): pi itself does not survive six decimals.
            yaw = 0f;
            ObjectFacing.Apply(2, ref yaw, ref size, ref profile);
            Assert.That(math.dot(Front(yaw), Front(0f)), Is.LessThan(-0.999999f));
            Assert.That(yaw, Is.GreaterThan(-math.PI).And.LessThanOrEqualTo(math.PI));
        }
    }
}
