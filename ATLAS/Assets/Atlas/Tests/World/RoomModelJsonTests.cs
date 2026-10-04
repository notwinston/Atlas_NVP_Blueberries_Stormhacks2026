using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.World.Tests
{
    /// <summary>The RoomModel reader's refusals, the canonical number text, and the validator catching broken rooms.</summary>
    public class RoomModelJsonTests
    {
        static string Sample() { return RoomFixtures.ReadTextFromProject(RoomFixtures.Sample4x5Table); }

        [TestCase(0.0, "0.0")]
        [TestCase(-0.0, "0.0")]
        [TestCase(-0.0000001, "0.0")]
        [TestCase(2.4, "2.4")]
        [TestCase(2.0, "2.0")]
        [TestCase(1.8845264, "1.884526")]
        [TestCase(-1.5707963, "-1.570796")]
        [TestCase(-4.15, "-4.15")]
        [TestCase(12.0, "12.0")]
        public void FormatNumber_IsCanonical(double value, string text)
        {
            Assert.That(RoomModelJson.FormatNumber(value), Is.EqualTo(text));
        }

        [Test]
        public void FormatNumber_RefusesNonFiniteValues()
        {
            Assert.Throws<ArgumentException>(() => RoomModelJson.FormatNumber(double.NaN));
            Assert.Throws<ArgumentException>(() => RoomModelJson.FormatNumber(double.PositiveInfinity));
        }

        [Test]
        public void Read_NamesWhatIsWrong()
        {
            string text = Sample();
            AssertRefused("not json", "JSON");
            AssertRefused("[1, 2]", "not an object");
            AssertRefused(text.Replace("\"version\": 1", "\"version\": 2"), "version");
            AssertRefused(text.Replace("  \"standing\": false,\n", ""), "standing");
            AssertRefused(text.Replace("\"wf\": 1.6", "\"wf\": \"wide\""), "freeRect.wf");
            AssertRefused(text.Replace("\"wf\": 1.6", "\"wf\": 99.0"), "freeRect.wf");
            AssertRefused(text.Replace("\"width\": 110", "\"width\": 111"), "sdf.data");
            AssertRefused(text.Replace("\"category\": \"table\"", "\"category\": \"piano\""), "objects[0].category");
            AssertRefused(text.Replace("\"kind\": \"door\"", "\"kind\": \"hatch\""), "walls[0].openings[0].kind");
            AssertRefused(text.Replace("\"mesh\": \"placeholder\"", "\"mesh\": \"melted\""), "objects[0].mesh");
            AssertRefused(text.Replace("\"id\": \"sample_4x5_table\"", "\"id\": \"no spaces allowed\""), "id");
            AssertRefused(text.Replace("\"triTarget\": 15000", "\"triTarget\": 15000.5"), "objects[0].triTarget");
            AssertRefused(text.Replace("\"triTarget\": 15000", "\"triTarget\": 15001"), "objects[0].triTarget");
        }

        [Test]
        public void Read_IgnoresUnknownKeys()
        {
            string text = Sample().Replace("  \"standing\": false,\n", "  \"standing\": false,\n  \"futureKey\": {\"a\": [1, 2]},\n");
            RoomModel room = RoomModelJson.Read(text);
            Assert.That(RoomModelJson.Write(room), Is.EqualTo(Sample()));
        }

        [Test]
        public void TryRead_ReturnsFalseInsteadOfThrowing()
        {
            Assert.That(RoomModelJson.TryRead("{}", out RoomModel room, out string error), Is.False);
            Assert.That(room, Is.Null);
            Assert.That(error, Does.Contain("version"));
            Assert.That(RoomModelJson.TryRead(Sample(), out room, out error), Is.True);
            Assert.That(room, Is.Not.Null);
            Assert.That(error, Is.Null);
        }

        [Test]
        public void GeometryHash_FollowsGeometryAndIgnoresMeshState()
        {
            RoomModel room = RoomModelJson.Read(Sample());
            string hash = RoomModelJson.GeometryHash(room);
            Assert.That(hash.Length, Is.EqualTo(64));
            Assert.That(hash, Does.Match("^[0-9a-f]{64}$"));

            room.Objects[0].Mesh = MeshState.Ready;
            room.Objects[0].AssetHash = new string('a', 64);
            room.Quality.UnknownShare = 0.5f;
            room.Id = "another";
            Assert.That(RoomModelJson.GeometryHash(room), Is.EqualTo(hash));

            room.Objects[0].Base.x += 0.01f;
            Assert.That(RoomModelJson.GeometryHash(room), Is.Not.EqualTo(hash));
        }

        [Test]
        public void MeshStateAndAssetHash_RoundTrip()
        {
            RoomModel room = RoomModelJson.Read(Sample());
            room.Objects[0].Mesh = MeshState.Ready;
            room.Objects[0].AssetHash = new string('5', 64);
            RoomModel again = RoomModelJson.Read(RoomModelJson.Write(room));
            Assert.That(again.Objects[0].Mesh, Is.EqualTo(MeshState.Ready));
            Assert.That(again.Objects[0].AssetHash, Is.EqualTo(new string('5', 64)));
            Assert.That(RoomModelJson.Check(again), Is.Null);

            foreach (MeshState state in new[] { MeshState.Placeholder, MeshState.Requested, MeshState.BuiltIn, MeshState.StandIn })
            {
                room.Objects[0].Mesh = state;
                room.Objects[0].AssetHash = "";
                Assert.That(RoomModelJson.Read(RoomModelJson.Write(room)).Objects[0].Mesh, Is.EqualTo(state));
            }
        }

        [Test]
        public void Check_ReportsTheCheapRules()
        {
            RoomModel room = RoomModelJson.Read(Sample());
            Assert.That(RoomModelJson.Check(room), Is.Null);

            room.LapSide = 2.0f;
            Assert.That(RoomModelJson.Check(room), Does.StartWith("R10"));
            room.ApplyLoopFit();
            Assert.That(RoomModelJson.Check(room), Is.Null);

            room.Standing = true;
            Assert.That(RoomModelJson.Check(room), Does.StartWith("R13"));
            room.Standing = false;

            room.Glued.Nx = 3;
            Assert.That(RoomModelJson.Check(room), Does.StartWith("R12"));
            room.ApplyLoopFit();

            room.Objects[0].AssetHash = new string('0', 64);
            Assert.That(RoomModelJson.Check(room), Does.StartWith("R16"));
            room.Objects[0].AssetHash = "";

            room.Objects[0].TriTarget = 3000;
            Assert.That(RoomModelJson.Check(room), Does.StartWith("R17"));
            room.Objects[0].TriTarget = 5000;

            room.FreeRect = new FreeRect(3.4f, 1.6f);
            Assert.That(RoomModelJson.Check(room), Does.StartWith("R6"));
        }

        [Test]
        public void Validator_CatchesBrokenRooms()
        {
            Assert.That(RuleOf(r => Array.Reverse(r.Outline)), Does.Contain("R1"));
            Assert.That(RuleOf(r => r.Sdf.Metres[r.Sdf.Width * 40 + 60] += 0.2f), Does.Contain("R5"));
            Assert.That(RuleOf(r => r.FreeRect = new FreeRect(1.4f, 3.4f)), Does.Contain("R8"));
            Assert.That(RuleOf(r => r.Walkable.Holes = new float2[0][]), Does.Contain("R9"));
            Assert.That(RuleOf(r => r.Curvature.L5 += 0.01f), Does.Contain("R11"));
            Assert.That(RuleOf(r => r.Walls[2].A = new float2(-3.0f, 1.0f)), Does.Contain("R14"));
            Assert.That(RuleOf(r => r.Walls[0].Openings[0].Width = 9f), Does.Contain("R15"));
            Assert.That(RuleOf(r => r.Walls[0].Openings[0].Bottom = 0.3f), Does.Contain("R15"));
            Assert.That(RuleOf(r => r.Objects[0].Base = new float3(-3.0f, 0f, 0f)), Does.Contain("R18"));
            Assert.That(RuleOf(r => r.Objects[0].Base = new float3(30f, 0f, 0f)), Does.Contain("R16"));

            var warnings = new List<string>();
            RoomModel scanned = RoomModelJson.Read(Sample());
            scanned.Quality.Route = ScanRoute.FloorScan;
            Assert.That(RoomModelValidator.Validate(scanned, new List<string>(), warnings), Is.True);
            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(warnings[0], Does.StartWith("R19"));
        }

        [Test]
        public void SignedDistance_OfOneFreeBlockIsExact()
        {
            // A 4 x 4 block of free cells in a 12 x 12 grid: centre cells are 1.5 and 0.5 cells from the edge.
            const int n = 12;
            var free = new bool[n * n];
            for (int j = 4; j < 8; j++) for (int i = 4; i < 8; i++) free[j * n + i] = true;
            double[] d = RoomModelValidator.SignedDistance(free, n, n);
            Assert.That(d[5 * n + 5], Is.EqualTo(0.075).Within(1e-12));
            Assert.That(d[4 * n + 4], Is.EqualTo(0.025).Within(1e-12));
            Assert.That(d[4 * n + 3], Is.EqualTo(-0.025).Within(1e-12));
            Assert.That(d[4 * n + 0], Is.EqualTo(-0.175).Within(1e-12));
            // Diagonally off the corner: half a cell each way to the nearest free square.
            Assert.That(d[3 * n + 3], Is.EqualTo(-0.05 * Math.Sqrt(0.5)).Within(1e-12));
        }

        [Test]
        public void TriTarget_IsTheEqualAreaCase()
        {
            Assert.That(ObjectMeshRules.TriTarget(0), Is.EqualTo(0));
            Assert.That(ObjectMeshRules.TriTarget(1), Is.EqualTo(15000));
            Assert.That(ObjectMeshRules.TriTarget(8), Is.EqualTo(15000));
            Assert.That(ObjectMeshRules.TriTarget(12), Is.EqualTo(10000));
            Assert.That(ObjectMeshRules.TriTarget(20), Is.EqualTo(6000));
            Assert.That(ObjectMeshRules.TriTarget(32), Is.EqualTo(3750));
            Assert.That(ObjectMeshRules.TriTarget(60), Is.EqualTo(3000));
            Assert.That(ObjectMeshRules.LightTarget(0), Is.EqualTo(0));
            Assert.That(ObjectMeshRules.LightTarget(5), Is.EqualTo(5000));
            Assert.That(ObjectMeshRules.LightTarget(12), Is.EqualTo(5000));
            Assert.That(ObjectMeshRules.LightTarget(20), Is.EqualTo(3000));
            Assert.That(ObjectMeshRules.LightTarget(32), Is.EqualTo(1875));
            Assert.That(ObjectMeshRules.LightTarget(60), Is.EqualTo(1500));
        }

        // The test vectors of the brief (section S5); tools/rooms/roommath.py reproduces the same six.
        [Test]
        public void Targets_ReproduceEveryVectorOfTheRule()
        {
            var five = new RoomObject[5];
            for (int k = 0; k < five.Length; k++) five[k] = Box(k + 1, 1.0f, 1.0f, 1.0f);
            AssertVector(five, Repeat(15000, 5), 5000);

            AssertVector(new[] { Box(1, 2.0f, 0.8f, 0.9f), Box(2, 0.5f, 0.9f, 0.5f), Box(3, 1.2f, 0.75f, 0.6f) },
                new[] { 15000, 15000, 15000 }, 5000);

            var mixed = new RoomObject[32];
            var mixedWant = new int[32];
            for (int k = 0; k < 32; k++)
            {
                mixed[k] = k < 8 ? Box(k + 1, 1.5f, 1.0f, 1.0f) : Box(k + 1, 0.2f, 0.2f, 0.2f);
                mixedWant[k] = k < 8 ? 6000 : 3000;
            }
            AssertVector(mixed, mixedWant, 1875);

            var ramp = new RoomObject[40];
            for (int k = 0; k < 40; k++) ramp[k] = Box(k + 1, 0.3f + 0.05f * (k + 1), 0.5f, 0.4f);
            var rampWant = new List<int>();
            for (int k = 0; k < 8; k++) rampWant.Add(0);
            for (int k = 0; k < 9; k++) rampWant.Add(3000);
            rampWant.AddRange(new[] { 3044, 3134, 3226, 3316, 3407, 3498, 3589, 3679, 3770, 3861, 3952, 4043, 4134, 4225,
                4315, 4407, 4497, 4588, 4679, 4770, 4861, 4952, 5043 });
            AssertVector(ramp, rampWant.ToArray(), 1875);
            int total = 0;
            foreach (int t in rampWant) total += t;
            Assert.That(total, Is.EqualTo(119990));

            AssertVector(new[] { Box(7, 0.6f, 0.45f, 0.6f) }, new[] { 15000 }, 5000);

            var twelve = new RoomObject[12];
            for (int k = 0; k < twelve.Length; k++) twelve[k] = Box(k + 1, 1.8f, 0.8f, 0.9f);
            AssertVector(twelve, Repeat(10000, 12), 5000);
        }

        [Test]
        public void Targets_RankByVolumeAndBreakTiesToTheLowerId()
        {
            // Forty equal boxes: the thirty-two with the lowest ids are meshed, however the array is ordered.
            var objects = new RoomObject[40];
            for (int k = 0; k < objects.Length; k++) objects[k] = Box(40 - k, 1f, 1f, 1f);
            int[] targets = ObjectMeshRules.Targets(objects);
            for (int k = 0; k < objects.Length; k++)
                Assert.That(targets[k], Is.EqualTo(objects[k].Id <= 32 ? 3750 : 0), "id " + objects[k].Id);

            ObjectMeshRules.Assign(objects);
            for (int k = 0; k < objects.Length; k++) Assert.That(objects[k].TriTarget, Is.EqualTo(targets[k]));
            Assert.That(ObjectMeshRules.Targets(new RoomObject[0]), Is.Empty);
        }

        [Test]
        public void OptionalKeys_RoundTrip()
        {
            RoomModel room = RoomModelJson.Read(Sample());
            string plain = RoomModelJson.Write(room);
            Assert.That(plain, Does.Not.Contain("assetHashLight"));
            Assert.That(plain, Does.Not.Contain("\"facing\""));
            Assert.That(plain, Does.Not.Contain("\"profile\""));
            string hash = RoomModelJson.GeometryHash(room);

            var profile = new byte[256];
            for (int k = 0; k < profile.Length; k++) profile[k] = (byte)k;
            room.Objects[0].AssetHashLight = new string('c', 64);
            room.Objects[0].Facing = ObjectFacingRule.Clearance;
            room.Objects[0].Profile = profile;
            string text = RoomModelJson.Write(room);
            Assert.That(text, Does.Contain("\"triTarget\": 15000, \"assetHashLight\": \"" + new string('c', 64) +
                "\", \"facing\": \"clearance\", \"profile\": \""));
            Assert.That(RoomModelJson.GeometryHash(room), Is.EqualTo(hash));

            RoomModel again = RoomModelJson.Read(text);
            Assert.That(again.Objects[0].AssetHashLight, Is.EqualTo(new string('c', 64)));
            Assert.That(again.Objects[0].Facing, Is.EqualTo(ObjectFacingRule.Clearance));
            Assert.That(again.Objects[0].Profile, Is.EqualTo(profile));
            Assert.That(RoomModelJson.Write(again), Is.EqualTo(text));
            Assert.That(RoomModelJson.Check(again), Is.Null);
            Assert.That(RoomModelValidator.Errors(again), Is.Empty);
            Assert.That(RoomModelJson.GeometryHash(again), Is.EqualTo(hash));

            foreach (ObjectFacingRule rule in new[] { ObjectFacingRule.Profile, ObjectFacingRule.Table })
            {
                room.Objects[0].Facing = rule;
                Assert.That(RoomModelJson.Read(RoomModelJson.Write(room)).Objects[0].Facing, Is.EqualTo(rule));
            }

            // A reader accepts "box", and writes nothing for it.
            RoomModel boxed = RoomModelJson.Read(text.Replace("\"clearance\"", "\"box\""));
            Assert.That(boxed.Objects[0].Facing, Is.EqualTo(ObjectFacingRule.Box));
            Assert.That(RoomModelJson.Write(boxed), Does.Not.Contain("\"facing\""));

            // A file without the keys reads and writes byte for byte as before.
            room.Objects[0].AssetHashLight = "";
            room.Objects[0].Facing = ObjectFacingRule.Box;
            room.Objects[0].Profile = null;
            Assert.That(RoomModelJson.Write(room), Is.EqualTo(plain));
            Assert.That(RoomModelJson.Write(RoomModelJson.Read(plain)), Is.EqualTo(plain));
        }

        [Test]
        public void OptionalKeys_AreRefusedWhenMalformed()
        {
            RoomModel room = RoomModelJson.Read(Sample());
            room.Objects[0].Facing = ObjectFacingRule.Table;
            string text = RoomModelJson.Write(room);
            AssertRefused(text.Replace("\"facing\": \"table\"", "\"facing\": \"sideways\""), "facing");
            room.Objects[0].Facing = ObjectFacingRule.Box;
            room.Objects[0].AssetHashLight = new string('a', 64);
            AssertRefused(RoomModelJson.Write(room).Replace(new string('a', 64), "ABC"), "assetHashLight");
            room.Objects[0].AssetHashLight = "";
            room.Objects[0].Profile = new byte[256];
            AssertRefused(RoomModelJson.Write(room).Replace("AAAA", "A*AA"), "profile");
        }

        [Test]
        public void R21_ARoomProfileIsExactly256Bytes()
        {
            Assert.That(RuleOf(r => r.Objects[0].Profile = new byte[256]), Is.Empty);
            Assert.That(RuleOf(r => r.Objects[0].Profile = new byte[255]), Does.Contain("R21"));
            Assert.That(RuleOf(r => r.Objects[0].Profile = new byte[257]), Does.Contain("R21"));
            Assert.That(RuleOf(r => r.Objects[0].Profile = new byte[0]), Does.Contain("R21"));

            // The same refusal from a file: the reader keeps the length, the validator names the rule.
            RoomModel room = RoomModelJson.Read(Sample());
            room.Objects[0].Profile = new byte[255];
            RoomModel again = RoomModelJson.Read(RoomModelJson.Write(room));
            Assert.That(again.Objects[0].Profile.Length, Is.EqualTo(255));
            Assert.That(string.Join("|", RoomModelValidator.Errors(again)), Does.Contain("R21"));
        }

        [Test]
        public void R17_FollowsTheSurfaceAreaRule()
        {
            Assert.That(RuleOf(r => r.Objects[0].TriTarget = 5000), Does.Contain("R17 object 1: triTarget = 5000, expected 15000"));
            Assert.That(RuleOf(r => r.Objects[0].TriTarget = 15000), Is.Empty);
        }

        static RoomObject Box(int id, float w, float h, float d)
        {
            return new RoomObject { Id = id, Size = new float3(w, h, d) };
        }

        static int[] Repeat(int value, int count)
        {
            var list = new int[count];
            for (int k = 0; k < count; k++) list[k] = value;
            return list;
        }

        static void AssertVector(RoomObject[] objects, int[] want, int light)
        {
            Assert.That(ObjectMeshRules.Targets(objects), Is.EqualTo(want));
            int meshed = 0;
            foreach (int t in want) if (t > 0) meshed++;
            Assert.That(ObjectMeshRules.LightTarget(meshed), Is.EqualTo(light));
        }

        [Test]
        public void ObjectCategories_NamesAndBits()
        {
            Assert.That(ObjectCategories.Count, Is.EqualTo(23));
            for (int k = 0; k < ObjectCategories.Count; k++)
            {
                var category = (ObjectCategory)k;
                Assert.That(ObjectCategories.TryParse(ObjectCategories.Name(category), out ObjectCategory parsed), Is.True);
                Assert.That(parsed, Is.EqualTo(category));
                Assert.That(ObjectCategories.TintIndex(category), Is.EqualTo(k));
            }
            Assert.That(ObjectCategories.Name(ObjectCategory.WasherDryer), Is.EqualTo("washerDryer"));
            Assert.That(ObjectCategories.TryParse("piano", out _), Is.False);
            Assert.That(ObjectCategories.FromBoundingBoxBits(0u), Is.EqualTo(ObjectCategory.Unknown));
            Assert.That(ObjectCategories.FromBoundingBoxBits(1u << 0), Is.EqualTo(ObjectCategory.Couch));
            Assert.That(ObjectCategories.FromBoundingBoxBits(1u << 8), Is.EqualTo(ObjectCategory.Chair));
            Assert.That(ObjectCategories.FromBoundingBoxBits(1u << 20), Is.EqualTo(ObjectCategory.Laptop));
            Assert.That(ObjectCategories.FromBoundingBoxBits(1u << 31), Is.EqualTo(ObjectCategory.Other));
            Assert.That(ObjectCategories.FromBoundingBoxBits((1u << 5) | (1u << 1)), Is.EqualTo(ObjectCategory.Table));
        }

        [Test]
        public void RoomTypes_SmallHelpers()
        {
            var rect = new FreeRect(2.4f, 3.4f);
            Assert.That(rect.Min, Is.EqualTo(new float2(-1.2f, -1.7f)));
            Assert.That(rect.Max, Is.EqualTo(new float2(1.2f, 1.7f)));
            Assert.That(rect.Contains(new float2(1.2f, -1.7f)), Is.True);
            Assert.That(rect.Contains(new float2(1.3f, 0f)), Is.False);

            var curvature = new RoomCurvature { L5 = 1.41f, L6 = 1.14f, R3 = 0.95f, FloorL = 1.4f, FloorR = 1.2f };
            Assert.That(curvature.ForDial(5), Is.EqualTo(1.41f));
            Assert.That(curvature.ForDial(6), Is.EqualTo(1.4f));
            Assert.That(curvature.ForDial(6, true), Is.EqualTo(1.14f));
            Assert.That(curvature.ForDial(3), Is.EqualTo(1.2f));
            Assert.That(curvature.ForDial(3, true), Is.EqualTo(0.95f));
            Assert.Throws<ArgumentOutOfRangeException>(() => curvature.ForDial(4));

            var lamp = new RoomObject { Base = new float3(0f, 2.2f, 0f), Size = new float3(0.3f, 0.2f, 0.3f) };
            Assert.That(lamp.BlocksFloor, Is.False);
            var rug = new RoomObject { Base = float3.zero, Size = new float3(2f, 0.02f, 1f) };
            Assert.That(rug.BlocksFloor, Is.False);
            var chair = new RoomObject { Base = float3.zero, Size = new float3(0.5f, 0.9f, 0.5f), Yaw = 0.5f };
            Assert.That(chair.BlocksFloor, Is.True);
            Assert.That(math.mul(chair.Rotation, new float3(0f, 0f, 1f)).x, Is.EqualTo(math.sin(0.5f)).Within(1e-6f));

            Assert.That(new RoomModel().MaxWallHeight, Is.EqualTo(2.4f));
            Assert.Throws<ArgumentException>(() => new RoomSdf(12, 12, 0f, 0f, new float[10]));
        }

        static void AssertRefused(string json, string named)
        {
            var e = Assert.Throws<RoomModelFormatException>(() => RoomModelJson.Read(json));
            Assert.That(e.Message, Does.Contain(named));
        }

        static string RuleOf(Action<RoomModel> damage)
        {
            RoomModel room = RoomModelJson.Read(Sample());
            damage(room);
            return string.Join(" | ", RoomModelValidator.Errors(room));
        }
    }
}
