using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.World.Tests
{
    /// <summary>
    /// The five RoomModel fixtures of gate G0: each parses, validates against every consistency rule, writes
    /// back to the same text, and stores exactly what the loop-fit and the cell rules compute.
    /// </summary>
    public class RoomModelFixtureTests
    {
        static readonly string[] Names = RoomFixtures.All;

        // Contract page section 5.1: wf, df, s, l5, l6, r3, glued (nx, nz, sizeX, sizeZ), aligned (n, m, g),
        // standing, objects, sdf grid, first 16 digits of the geometry hash.
        static readonly object[] Reference =
        {
            new object[] { "sample_3x3", 2.4, 2.4, 2.0, 1.884526, 1.518651, 1.273240, 2, 2, 1.2, 1.2, 3, 3, 0.8, false, 0, 70, 70, "1111bf11f2152ef0" },
            new object[] { "sample_L_3x4", 1.9, 2.4, 1.5, 1.413394, 1.138989, 0.954930, 1, 2, 1.9, 1.2, 2, 2, 0.95, false, 0, 90, 70, "9c3eaf728bc2e6e6" },
            new object[] { "sample_4x5_table", 1.6, 3.4, 1.2, 1.130715, 0.911191, 0.763944, 1, 2, 1.6, 1.7, 2, 2, 0.8, false, 1, 110, 90, "61de428db392e65b" },
            new object[] { "budget_room", 2.4, 3.4, 2.0, 1.884526, 1.518651, 1.273240, 2, 2, 1.2, 1.7, 3, 4, 0.8, false, 12, 90, 110, "d7ee2b85f3310cf1" },
            new object[] { "tiny_standing", 1.4, 1.4, 1.0, 0.942263, 0.759326, 0.636620, 1, 1, 1.4, 1.4, 2, 2, 0.7, true, 0, 50, 50, "c11478f2305ddd47" },
        };

        [Test]
        public void All_NamesTheFiveFixturesInOrder()
        {
            Assert.That(RoomFixtures.All, Is.EqualTo(new[]
            {
                "sample_3x3", "sample_L_3x4", "sample_4x5_table", "budget_room", "tiny_standing"
            }));
            Assert.That(RoomFixtures.IsFixture(RoomFixtures.BudgetRoom), Is.True);
            Assert.That(RoomFixtures.IsFixture("kitchen"), Is.False);
            Assert.Throws<ArgumentException>(() => RoomFixtures.LoadFromProject("no_such_room"));
            Assert.Throws<ArgumentException>(() => RoomFixtures.LoadFromProject(null));
        }

        [TestCaseSource(nameof(Names))]
        public void Fixture_ParsesAndPassesEveryRule(string name)
        {
            RoomModel room = RoomFixtures.LoadFromProject(name);
            Assert.That(room.Version, Is.EqualTo(RoomModel.SchemaVersion));
            Assert.That(room.Id, Is.EqualTo(name));
            Assert.That(room.Quality.Route, Is.EqualTo(ScanRoute.Fixture));

            var errors = new List<string>();
            var warnings = new List<string>();
            bool valid = RoomModelValidator.Validate(room, errors, warnings);
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
            Assert.That(valid, Is.True);
            Assert.That(warnings, Is.Empty, string.Join("\n", warnings));
            Assert.That(RoomModelJson.Check(room), Is.Null);
        }

        [TestCaseSource(nameof(Names))]
        public void Fixture_WriteThenReadIsLossless(string name)
        {
            string text = RoomFixtures.ReadTextFromProject(name);
            RoomModel room = RoomModelJson.Read(text);
            string written = RoomModelJson.Write(room);
            Assert.That(written == text, Is.True, "Write(Read(text)) differs from the file for " + name + FirstDifference(text, written));
            Assert.That(System.Text.Encoding.UTF8.GetByteCount(written), Is.LessThanOrEqualTo(RoomModelJson.MaxFileBytes));

            // And field by field, through a second generation.
            RoomModel again = RoomModelJson.Read(written);
            AssertSameRoom(room, again);
            Assert.That(RoomModelJson.Write(again), Is.EqualTo(written));
            Assert.That(RoomModelJson.GeometryHash(again), Is.EqualTo(RoomModelJson.GeometryHash(room)));
        }

        [TestCaseSource(nameof(Reference))]
        public void Fixture_HoldsItsReferenceValues(string name, double wf, double df, double s, double l5, double l6,
            double r3, int nx, int nz, double sizeX, double sizeZ, int n, int m, double g, bool standing, int objects,
            int width, int height, string hash16)
        {
            RoomModel room = RoomFixtures.LoadFromProject(name);
            Assert.That(room.FreeRect.Wf, Is.EqualTo(wf).Within(1e-6));
            Assert.That(room.FreeRect.Df, Is.EqualTo(df).Within(1e-6));
            Assert.That(room.LapSide, Is.EqualTo(s).Within(1e-6));
            Assert.That(room.Curvature.L5, Is.EqualTo(l5).Within(2e-6));
            Assert.That(room.Curvature.L6, Is.EqualTo(l6).Within(2e-6));
            Assert.That(room.Curvature.R3, Is.EqualTo(r3).Within(2e-6));
            Assert.That(room.Glued.Nx, Is.EqualTo(nx));
            Assert.That(room.Glued.Nz, Is.EqualTo(nz));
            Assert.That(room.Glued.SizeX, Is.EqualTo(sizeX).Within(1e-6));
            Assert.That(room.Glued.SizeZ, Is.EqualTo(sizeZ).Within(1e-6));
            Assert.That(room.Aligned.N, Is.EqualTo(n));
            Assert.That(room.Aligned.M, Is.EqualTo(m));
            Assert.That(room.Aligned.G, Is.EqualTo(g).Within(1e-6));
            Assert.That(room.Standing, Is.EqualTo(standing));
            Assert.That(room.Objects.Length, Is.EqualTo(objects));
            Assert.That(room.Sdf.Width, Is.EqualTo(width));
            Assert.That(room.Sdf.Height, Is.EqualTo(height));
            Assert.That(RoomModelJson.GeometryHash(room), Does.StartWith(hash16));
        }

        [TestCaseSource(nameof(Names))]
        public void Fixture_StoresWhatLoopFitAndTheCellRulesCompute(string name)
        {
            RoomModel room = RoomFixtures.LoadFromProject(name);
            double wf = Math.Round((double)room.FreeRect.Wf, 6), df = Math.Round((double)room.FreeRect.Df, 6);

            LoopFitResult fit = LoopFit.Fit(wf, df);
            Assert.That(room.LapSide, Is.EqualTo((float)fit.S));
            Assert.That(room.LapSide, Is.EqualTo(LoopFit.LapSide(room.FreeRect.Wf, room.FreeRect.Df)));
            // The file carries six decimals; the rule's value rounds to the stored one.
            Assert.That(room.Curvature.L5, Is.EqualTo(fit.L5).Within(1e-6));
            Assert.That(room.Curvature.L6, Is.EqualTo(fit.L6).Within(1e-6));
            Assert.That(room.Curvature.R3, Is.EqualTo(fit.R3).Within(1e-6));
            Assert.That(room.Curvature.L5, Is.EqualTo(LoopFit.Radius(5, room.LapSide)).Within(1e-6));
            Assert.That(room.Curvature.L6, Is.EqualTo(LoopFit.Radius(6, room.LapSide)).Within(1e-6));
            Assert.That(room.Curvature.R3, Is.EqualTo(LoopFit.Radius(3, room.LapSide)).Within(1e-6));
            Assert.That(room.Curvature.FloorL, Is.EqualTo(LoopFit.FloorL));
            Assert.That(room.Curvature.FloorR, Is.EqualTo(LoopFit.FloorR));
            Assert.That(room.Standing, Is.EqualTo(LoopFit.IsStanding(wf, df)));
            Assert.That(room.Standing, Is.EqualTo(LoopFit.IsStanding(room.FreeRect.Wf, room.FreeRect.Df)));

            GluedGrid glued = CellGridRules.Glued(wf, df);
            Assert.That(room.Glued.Nx, Is.EqualTo(glued.Nx));
            Assert.That(room.Glued.Nz, Is.EqualTo(glued.Nz));
            Assert.That(room.Glued.SizeX, Is.EqualTo(glued.SizeX).Within(1e-6));
            Assert.That(room.Glued.SizeZ, Is.EqualTo(glued.SizeZ).Within(1e-6));
            AlignedGrid aligned = CellGridRules.Aligned(wf, df);
            Assert.That(room.Aligned.N, Is.EqualTo(aligned.N));
            Assert.That(room.Aligned.M, Is.EqualTo(aligned.M));
            Assert.That(room.Aligned.G, Is.EqualTo(aligned.G).Within(1e-6));

            // ApplyLoopFit on a copy reproduces the stored block and so the same text.
            string before = RoomModelJson.Write(room);
            room.LapSide = 0f;
            room.Curvature = default(RoomCurvature);
            room.Glued = default(GluedGrid);
            room.Aligned = default(AlignedGrid);
            room.Standing = !room.Standing;
            room.ApplyLoopFit();
            Assert.That(RoomModelJson.Write(room), Is.EqualTo(before));
        }

        // Contract page section 5.2: Sample at (0,0), (Wf/2,0), (0,Df/2), outline[0], (Wf/2+0.30,0), (50,0).
        [TestCase("sample_3x3", 1.4250, 0.2500, 0.2500, -0.0747, -0.0500, -48.5500)]
        [TestCase("sample_L_3x4", 1.1750, 0.2500, 0.2500, -0.0747, -0.0375, -47.3055)]
        [TestCase("sample_4x5_table", 1.0250, 0.2500, 0.2500, -0.0747, -0.0500, -48.9500)]
        [TestCase("budget_room", 1.4250, 0.2500, 0.2500, -0.0747, -0.0500, -48.5500)]
        [TestCase("tiny_standing", 0.9250, 0.2500, 0.2500, -0.0747, -0.0500, -49.0500)]
        public void Sdf_SampleMatchesTheProbes(string name, double centre, double halfW, double halfD, double corner,
            double beyond, double far)
        {
            RoomModel room = RoomFixtures.LoadFromProject(name);
            RoomSdf sdf = room.Sdf;
            float wf = room.FreeRect.Wf, df = room.FreeRect.Df;
            Assert.That(sdf.Sample(new float2(0f, 0f)), Is.EqualTo(centre).Within(1e-4));
            Assert.That(sdf.Sample(new float2(0.5f * wf, 0f)), Is.EqualTo(halfW).Within(1e-4));
            Assert.That(sdf.Sample(new float2(0f, 0.5f * df)), Is.EqualTo(halfD).Within(1e-4));
            Assert.That(sdf.Sample(room.Outline[0]), Is.EqualTo(corner).Within(1e-4));
            Assert.That(sdf.Sample(new float2(0.5f * wf + 0.30f, 0f)), Is.EqualTo(beyond).Within(1e-4));
            Assert.That(sdf.Sample(new float2(50f, 0f)), Is.EqualTo(far).Within(1e-4));

            Assert.That(sdf.IsWalkable(float2.zero), Is.True);
            Assert.That(sdf.IsFree(float2.zero), Is.True);
            Assert.That(sdf.IsFree(new float2(50f, 0f)), Is.False);
            Assert.That(room.Walkable.Contains(float2.zero), Is.True);
            Assert.That(room.FreeRect.Contains(float2.zero), Is.True);
            // Near the +X edge of the free rectangle clearance grows towards the middle of the room.
            float2 gradient = sdf.Gradient(new float2(0.5f * wf, 0f));
            Assert.That(gradient.x, Is.LessThan(-0.9f));
            Assert.That(math.length(gradient), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void BudgetRoom_HoldsTwelveStandInsOfFiveThousandTriangles()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.BudgetRoom);
            Assert.That(room.Objects.Length, Is.EqualTo(12));
            int total = 0;
            foreach (RoomObject o in room.Objects)
            {
                Assert.That(o.Mesh, Is.EqualTo(MeshState.StandIn));
                Assert.That(o.AssetHash, Is.Empty);
                total += o.TriTarget;
            }
            Assert.That(total, Is.EqualTo(ObjectMeshRules.FurnitureTriangles));
            Assert.That(room.MeshedObjectCount, Is.EqualTo(12));
            Assert.That(room.TryGetObject(8, out RoomObject screen), Is.True);
            Assert.That(screen.Category, Is.EqualTo(ObjectCategory.Screen));
            Assert.That(screen.Base.y, Is.EqualTo(1.0f));
            Assert.That(screen.BlocksFloor, Is.True);
            Assert.That(room.TryGetObject(99, out RoomObject none), Is.False);
            Assert.That(none, Is.Null);
            Assert.That(room.MaxWallHeight, Is.EqualTo(2.4f));
        }

        [Test]
        public void TableRoom_HasAHoleAnObjectAndOpenings()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample4x5Table);
            Assert.That(room.Walkable.Holes.Length, Is.EqualTo(1));
            Assert.That(WalkablePolygon.SignedArea(room.Walkable.Outer), Is.GreaterThan(0f));
            Assert.That(WalkablePolygon.SignedArea(room.Walkable.Holes[0]), Is.LessThan(0f));
            RoomObject table = room.Objects[0];
            Assert.That(table.Category, Is.EqualTo(ObjectCategory.Table));
            Assert.That(table.Mesh, Is.EqualTo(MeshState.Placeholder));
            Assert.That(table.TriTarget, Is.EqualTo(5000));
            Assert.That(table.Volume, Is.EqualTo(0.45f).Within(1e-6f));
            Assert.That(room.Walkable.Contains(table.Base.xz), Is.False);
            Assert.That(room.Sdf.IsFree(table.Base.xz), Is.False);
            Assert.That(room.Walls[0].Openings[0].Kind, Is.EqualTo(OpeningKind.Door));
            Assert.That(room.Walls[1].Openings[0].Kind, Is.EqualTo(OpeningKind.Window));
            Assert.That(room.Walls[0].Length, Is.EqualTo(4f).Within(1e-6f));
        }

        static string FirstDifference(string a, string b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int k = 0; k < n; k++)
            {
                if (a[k] == b[k]) continue;
                int from = Math.Max(0, k - 30);
                return "\n at character " + k + ":\n file:    " + a.Substring(from, Math.Min(60, a.Length - from)) +
                       "\n written: " + b.Substring(from, Math.Min(60, b.Length - from));
            }
            return "\n lengths " + a.Length + " and " + b.Length;
        }

        static void AssertSameRoom(RoomModel a, RoomModel b)
        {
            Assert.That(b.Version, Is.EqualTo(a.Version));
            Assert.That(b.Id, Is.EqualTo(a.Id));
            Assert.That(b.CreatedUtc, Is.EqualTo(a.CreatedUtc));
            Assert.That(b.Anchor.Id, Is.EqualTo(a.Anchor.Id));
            Assert.That(b.Anchor.Position, Is.EqualTo(a.Anchor.Position));
            Assert.That(b.Anchor.Yaw, Is.EqualTo(a.Anchor.Yaw));
            Assert.That(b.Outline, Is.EqualTo(a.Outline));
            Assert.That(b.Sdf.Width, Is.EqualTo(a.Sdf.Width));
            Assert.That(b.Sdf.Height, Is.EqualTo(a.Sdf.Height));
            Assert.That(b.Sdf.OriginX, Is.EqualTo(a.Sdf.OriginX));
            Assert.That(b.Sdf.OriginZ, Is.EqualTo(a.Sdf.OriginZ));
            Assert.That(b.Sdf.Metres, Is.EqualTo(a.Sdf.Metres));
            Assert.That(b.Walkable.Outer, Is.EqualTo(a.Walkable.Outer));
            Assert.That(b.Walkable.Holes.Length, Is.EqualTo(a.Walkable.Holes.Length));
            for (int k = 0; k < a.Walkable.Holes.Length; k++) Assert.That(b.Walkable.Holes[k], Is.EqualTo(a.Walkable.Holes[k]));
            Assert.That(b.FreeRect.Wf, Is.EqualTo(a.FreeRect.Wf));
            Assert.That(b.FreeRect.Df, Is.EqualTo(a.FreeRect.Df));
            Assert.That(b.LapSide, Is.EqualTo(a.LapSide));
            Assert.That(b.Curvature, Is.EqualTo(a.Curvature));
            Assert.That(b.Glued, Is.EqualTo(a.Glued));
            Assert.That(b.Aligned, Is.EqualTo(a.Aligned));
            Assert.That(b.Standing, Is.EqualTo(a.Standing));
            Assert.That(b.Quality, Is.EqualTo(a.Quality));
            Assert.That(b.Walls.Length, Is.EqualTo(a.Walls.Length));
            for (int k = 0; k < a.Walls.Length; k++)
            {
                Assert.That(b.Walls[k].A, Is.EqualTo(a.Walls[k].A));
                Assert.That(b.Walls[k].B, Is.EqualTo(a.Walls[k].B));
                Assert.That(b.Walls[k].Height, Is.EqualTo(a.Walls[k].Height));
                Assert.That(b.Walls[k].Openings, Is.EqualTo(a.Walls[k].Openings));
            }
            Assert.That(b.Objects.Length, Is.EqualTo(a.Objects.Length));
            for (int k = 0; k < a.Objects.Length; k++)
            {
                RoomObject x = a.Objects[k], y = b.Objects[k];
                Assert.That(y.Id, Is.EqualTo(x.Id));
                Assert.That(y.Category, Is.EqualTo(x.Category));
                Assert.That(y.Base, Is.EqualTo(x.Base));
                Assert.That(y.Size, Is.EqualTo(x.Size));
                Assert.That(y.Yaw, Is.EqualTo(x.Yaw));
                Assert.That(y.Mesh, Is.EqualTo(x.Mesh));
                Assert.That(y.AssetHash, Is.EqualTo(x.AssetHash));
                Assert.That(y.TriTarget, Is.EqualTo(x.TriTarget));
            }
        }
    }
}
