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
            AssertRefused(text.Replace("\"triTarget\": 5000", "\"triTarget\": 5000.5"), "objects[0].triTarget");
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
        public void TriTargets_FollowTheBudgetRule()
        {
            Assert.That(ObjectMeshRules.TriTarget(0), Is.EqualTo(0));
            Assert.That(ObjectMeshRules.TriTarget(1), Is.EqualTo(5000));
            Assert.That(ObjectMeshRules.TriTarget(12), Is.EqualTo(5000));
            Assert.That(ObjectMeshRules.TriTarget(13), Is.EqualTo(4615));
            Assert.That(ObjectMeshRules.TriTarget(20), Is.EqualTo(3000));
            Assert.That(ObjectMeshRules.TriTarget(40), Is.EqualTo(3000));

            var objects = new RoomObject[22];
            for (int k = 0; k < objects.Length; k++)
                objects[k] = new RoomObject { Id = k + 1, Size = new float3(1f, 1f, k < 2 ? 0.1f : 1f) };
            ObjectMeshRules.Assign(objects);
            // The two thin boxes are the smallest: they stay placeholders, the other twenty share the budget.
            Assert.That(objects[0].TriTarget, Is.EqualTo(0));
            Assert.That(objects[1].TriTarget, Is.EqualTo(0));
            for (int k = 2; k < objects.Length; k++) Assert.That(objects[k].TriTarget, Is.EqualTo(3000));
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
