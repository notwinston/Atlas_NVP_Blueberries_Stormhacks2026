using System.Globalization;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.World.Tests
{
    /// <summary>
    /// The reader of the RoomPlan plugin's atlas.room/1 file (docs/plans/standalone-phone/INDEX.md, section 6.2):
    /// ARKit's right-handed world frame into Unity's session frame (z negated, as WallBridgeJson does), the box
    /// rotation from its x and z columns, the attributes verbatim. Written from the plan, not the code.
    /// <para>The rotation follows AR Foundation's conversion of an ARKit pose, which is how box poses reached the
    /// scan before (ARBoundingBox.pose): R_unity = M R_arkit M with M = diag(1, 1, -1). So local +X is the
    /// mirrored x column, local +Y the mirrored y column and local +Z the mirrored z column negated.</para>
    /// </summary>
    public class RoomPlanJsonTests
    {
        const float Eps = 1e-4f;

        // A hand-written capture: two walls, a door, a window, no plain opening, three objects.
        // Object A: a sofa turned 30 degrees about ARKit's +Y. Object B: a chair with no attributes and a
        // general (tilted) rotation, to check the whole quaternion. Object C: a table at identity.
        static string Sample()
        {
            float3 ax, az, bx, bz;
            ArKitColumns(quaternion.RotateY(math.radians(30f)), out ax, out az);
            ArKitColumns(TiltedArKit(), out bx, out bz);
            return "{\"schema\":\"atlas.room/1\",\n" +
                " \"walls\":[{\"c\":[0.0,1.2,-2.0],\"x\":[1.0,0.0,0.0],\"w\":4.0,\"h\":2.4},\n" +
                "            {\"c\":[2.0,1.2,-0.5],\"x\":[0.0,0.0,1.0],\"w\":3.0,\"h\":2.4}],\n" +
                " \"doors\":[{\"c\":[0.5,1.0,-2.0],\"x\":[1.0,0.0,0.0],\"w\":0.9,\"h\":2.0}],\n" +
                " \"windows\":[{\"c\":[2.0,1.5,-1.0],\"x\":[0.0,0.0,1.0],\"w\":1.2,\"h\":1.0}],\n" +
                " \"openings\":[],\n" +
                " \"objects\":[\n" +
                "  {\"id\":\"6F9619FF-8B86-D011-B42D-00C04FC964FF\",\"cat\":\"sofa\",\"c\":[1.0,0.45,-1.5]," +
                "\"x\":" + V(ax) + ",\"z\":" + V(az) + ",\"d\":[2.0,0.9,0.95],\"conf\":\"high\"," +
                "\"attrs\":[\"SofaType.rectangular\",\"seats: 3\"]},\n" +
                "  {\"id\":\"B\",\"cat\":\"chair\",\"c\":[-0.5,0.4,0.25],\"x\":" + V(bx) + ",\"z\":" + V(bz) + "," +
                "\"d\":[0.5,0.8,0.55],\"conf\":\"medium\",\"attrs\":[]},\n" +
                "  {\"id\":\"C\",\"cat\":\"table\",\"c\":[0.0,0.375,0.0],\"x\":[1.0,0.0,0.0],\"z\":[0.0,0.0,1.0]," +
                "\"d\":[1.2,0.75,0.8],\"conf\":\"low\",\"attrs\":[\"TableType.dining\"]}\n" +
                " ],\n" +
                " \"instruction\":\"moveCloseToWall\"}";
        }

        [Test]
        public void TryRead_ReadsEveryList()
        {
            Assert.That(RoomPlanJson.TryRead(Sample(), out RoomPlanCapture capture), Is.True);
            Assert.That(capture.Walls.Length, Is.EqualTo(2));
            Assert.That(capture.Doors.Length, Is.EqualTo(1));
            Assert.That(capture.Windows.Length, Is.EqualTo(1));
            Assert.That(capture.Openings.Length, Is.EqualTo(0));
            Assert.That(capture.Objects.Length, Is.EqualTo(3));
            Assert.That(capture.Instruction, Is.EqualTo("moveCloseToWall"));
        }

        [Test]
        public void Surfaces_AreFlippedIntoUnitysFrame()
        {
            Assert.That(RoomPlanJson.TryRead(Sample(), out RoomPlanCapture capture), Is.True);
            AssertVector(capture.Walls[0].Centre, new float3(0f, 1.2f, 2f), "wall 0 centre");
            AssertVector(capture.Walls[1].Centre, new float3(2f, 1.2f, 0.5f), "wall 1 centre");
            Assert.That(capture.Walls[0].Width, Is.EqualTo(4f).Within(Eps));
            Assert.That(capture.Walls[0].Height, Is.EqualTo(2.4f).Within(Eps));
            AssertVector(capture.Doors[0].Centre, new float3(0.5f, 1.0f, 2f), "door centre");
            Assert.That(capture.Doors[0].Width, Is.EqualTo(0.9f).Within(Eps));
            Assert.That(capture.Doors[0].Height, Is.EqualTo(2.0f).Within(Eps));
            AssertVector(capture.Windows[0].Centre, new float3(2f, 1.5f, 1f), "window centre");
            Assert.That(capture.Windows[0].Width, Is.EqualTo(1.2f).Within(Eps));
            Assert.That(capture.Windows[0].Height, Is.EqualTo(1.0f).Within(Eps));
        }

        [Test]
        public void Objects_KeepTheirIdsCategoriesAndAttributesVerbatim()
        {
            Assert.That(RoomPlanJson.TryRead(Sample(), out RoomPlanCapture capture), Is.True);
            RoomPlanObject a = capture.Objects[0], b = capture.Objects[1], c = capture.Objects[2];
            Assert.That(a.Id, Is.EqualTo("6F9619FF-8B86-D011-B42D-00C04FC964FF"));
            Assert.That(a.Category, Is.EqualTo("sofa"));
            Assert.That(a.Attributes, Is.EqualTo(new[] { "SofaType.rectangular", "seats: 3" }));
            Assert.That(b.Id, Is.EqualTo("B"));
            Assert.That(b.Category, Is.EqualTo("chair"));
            Assert.That(b.Attributes, Is.Not.Null.And.Empty);
            Assert.That(c.Category, Is.EqualTo("table"));
            Assert.That(c.Attributes, Is.EqualTo(new[] { "TableType.dining" }));
            // The category names are RoomPlan's; FromRoomPlan turns them into ours.
            Assert.That(ObjectCategories.FromRoomPlan(a.Category), Is.EqualTo(ObjectCategory.Couch));
        }

        [Test]
        public void Objects_CentreIsFlippedAndSizeIsKept()
        {
            Assert.That(RoomPlanJson.TryRead(Sample(), out RoomPlanCapture capture), Is.True);
            AssertVector(capture.Objects[0].Centre, new float3(1f, 0.45f, 1.5f), "sofa centre");
            AssertVector(capture.Objects[0].Size, new float3(2f, 0.9f, 0.95f), "sofa size");
            AssertVector(capture.Objects[1].Centre, new float3(-0.5f, 0.4f, -0.25f), "chair centre");
            AssertVector(capture.Objects[1].Size, new float3(0.5f, 0.8f, 0.55f), "chair size");
            AssertVector(capture.Objects[2].Centre, new float3(0f, 0.375f, 0f), "table centre");
            AssertVector(capture.Objects[2].Size, new float3(1.2f, 0.75f, 0.8f), "table size");
        }

        [Test]
        public void Objects_RotationComesFromTheXAndZColumns()
        {
            Assert.That(RoomPlanJson.TryRead(Sample(), out RoomPlanCapture capture), Is.True);
            AssertRotation(capture.Objects[0].Rotation, quaternion.RotateY(math.radians(30f)), "sofa");
            AssertRotation(capture.Objects[1].Rotation, TiltedArKit(), "chair");
            AssertRotation(capture.Objects[2].Rotation, quaternion.identity, "table");
        }

        [Test]
        public void Objects_AYawInArKitIsTheOppositeYawInUnity()
        {
            // Turning +30 degrees about ARKit's +Y is turning -30 degrees about Unity's +Y once z is mirrored.
            Assert.That(RoomPlanJson.TryRead(Sample(), out RoomPlanCapture capture), Is.True);
            float3 right = math.mul(capture.Objects[0].Rotation, new float3(1f, 0f, 0f));
            float3 forward = math.mul(capture.Objects[0].Rotation, new float3(0f, 0f, 1f));
            float s = math.sin(math.radians(30f)), c = math.cos(math.radians(30f));
            AssertVector(right, new float3(c, 0f, s), "sofa +X");
            AssertVector(forward, new float3(-s, 0f, c), "sofa +Z");
            AssertVector(math.mul(capture.Objects[0].Rotation, new float3(0f, 1f, 0f)), new float3(0f, 1f, 0f), "sofa +Y");
        }

        [Test]
        public void Objects_RotationIsAUnitQuaternion()
        {
            Assert.That(RoomPlanJson.TryRead(Sample(), out RoomPlanCapture capture), Is.True);
            foreach (RoomPlanObject o in capture.Objects)
                Assert.That(math.length(o.Rotation.value), Is.EqualTo(1f).Within(Eps), o.Id);
        }

        [TestCase("normal")]
        [TestCase("moveCloseToWall")]
        [TestCase("moveAwayFromWall")]
        [TestCase("slowDown")]
        [TestCase("turnOnLight")]
        [TestCase("lowTexture")]
        public void Instruction_IsReadVerbatim(string instruction)
        {
            string text = Sample().Replace("\"moveCloseToWall\"", "\"" + instruction + "\"");
            Assert.That(RoomPlanJson.TryRead(text, out RoomPlanCapture capture), Is.True);
            Assert.That(capture.Instruction, Is.EqualTo(instruction));
        }

        [Test]
        public void TryRead_RefusesWhatIsNotARoomFile()
        {
            Assert.That(RoomPlanJson.TryRead(null, out RoomPlanCapture _), Is.False);
            Assert.That(RoomPlanJson.TryRead("", out RoomPlanCapture _), Is.False);
            Assert.That(RoomPlanJson.TryRead("not json", out RoomPlanCapture _), Is.False);
            Assert.That(RoomPlanJson.TryRead("[1, 2]", out RoomPlanCapture _), Is.False);
            Assert.That(RoomPlanJson.TryRead(Sample().Replace("atlas.room/1", "atlas.room/2"), out RoomPlanCapture _), Is.False);
            Assert.That(RoomPlanJson.TryRead("{\"walls\":[]}", out RoomPlanCapture _), Is.False);
        }

        // ---- helpers -------------------------------------------------------------------------------------

        /// <summary>A rotation that is not about +Y alone, in ARKit's frame.</summary>
        static quaternion TiltedArKit()
        {
            return math.normalize(math.mul(quaternion.RotateY(1.1f), math.mul(quaternion.RotateX(0.3f), quaternion.RotateZ(-0.2f))));
        }

        static void ArKitColumns(quaternion arkit, out float3 x, out float3 z)
        {
            x = math.mul(arkit, new float3(1f, 0f, 0f));
            z = math.mul(arkit, new float3(0f, 0f, 1f));
        }

        static float3 Mirror(float3 v) { return new float3(v.x, v.y, -v.z); }

        /// <summary>Unity's rotation must send its local axes to the mirrored ARKit columns: +X to M x, +Y to M y,
        /// +Z to -M z (AR Foundation's pose conversion).</summary>
        static void AssertRotation(quaternion unity, quaternion arkit, string name)
        {
            float3 x = math.mul(arkit, new float3(1f, 0f, 0f));
            float3 y = math.mul(arkit, new float3(0f, 1f, 0f));
            float3 z = math.mul(arkit, new float3(0f, 0f, 1f));
            AssertVector(math.mul(unity, new float3(1f, 0f, 0f)), Mirror(x), name + " +X");
            AssertVector(math.mul(unity, new float3(0f, 1f, 0f)), Mirror(y), name + " +Y");
            AssertVector(math.mul(unity, new float3(0f, 0f, 1f)), -Mirror(z), name + " +Z");
        }

        static void AssertVector(float3 actual, float3 expected, string name)
        {
            Assert.That(math.distance(actual, expected), Is.LessThan(Eps), name + ": " + actual + " expected " + expected);
        }

        static string V(float3 v)
        {
            return "[" + v.x.ToString("0.0#########", CultureInfo.InvariantCulture) + "," + v.y.ToString("0.0#########", CultureInfo.InvariantCulture) +
                "," + v.z.ToString("0.0#########", CultureInfo.InvariantCulture) + "]";
        }
    }
}
