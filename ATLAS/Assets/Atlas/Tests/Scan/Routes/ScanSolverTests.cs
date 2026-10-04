using Atlas.Scan.Grid;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Routes
{
    /// <summary>The routes as one calculation: the automatic scan, the manual outline, and the fall-back from one to the other.</summary>
    public class ScanSolverTests
    {
        const float Turn = 0.35f;                        // about 20 degrees
        static readonly float2 Shift = new float2(1.7f, -2.3f);

        static SyntheticRoom MovedRoom(float2[] outline, float2 walker)
        {
            return SyntheticRoom.Polygon(outline, walker).Move(Turn, new float3(Shift.x, 0.4f, Shift.y));
        }

        [Test]
        public void AutomaticScan_ProposesTheOutline()
        {
            SyntheticRoom scan = MovedRoom(ScanTestRooms.LOutline, new float2(1.25f, 1.5f));
            ScanSolution solution = ScanSolver.Solve(ScanTestRooms.Snapshot(scan.Build()), "auto");

            Assert.That(solution.Failure, Is.EqualTo(ScanFailure.None));
            Assert.That(solution.Route, Is.EqualTo(ScanRoute.FloorScan));
            Assert.That(solution.FloorY, Is.EqualTo(0.4f).Within(1e-4f));
            Assert.That(solution.Outline.Length, Is.EqualTo(6));
            foreach (float2 corner in ScanTestRooms.Moved(ScanTestRooms.LOutline, Turn, Shift))
                Assert.That(ScanTestRooms.NearestCorner(corner, solution.Outline), Is.LessThan(0.08f));
            Assert.That(solution.Room.FreeRect.Wf, Is.EqualTo(1.9f).Within(0.051f));
            Assert.That(solution.Room.FreeRect.Df, Is.EqualTo(2.4f).Within(0.051f));
            Assert.That(solution.Room.Quality.Route, Is.EqualTo(ScanRoute.FloorScan));
            Assert.That(solution.Room.Id, Is.EqualTo("auto"));
            Assert.That(solution.Room.Anchor.Position.y, Is.EqualTo(0.4f).Within(1e-4f));
            Assert.That(RoomModelValidator.Errors(solution.Room), Is.Empty);
        }

        [TestCase(3f, 3f)]
        [TestCase(4f, 5f)]
        [TestCase(3.37f, 4.62f)]
        public void ManualAndAutomatic_AgreeOnTheFreeRectangle(float w, float d)
        {
            var walker = new float2(0.5f * w, 0.5f * d);
            SyntheticRoom scan = MovedRoom(ScanTestRooms.Rectangle(w, d), walker);
            ScanSolution automatic = ScanSolver.Solve(ScanTestRooms.Snapshot(scan.Build()), "a");

            // The same room by hand: the four corners tapped to within 2 cm, no mesh.
            var random = new System.Random(5);
            float2[] corners = ScanTestRooms.Moved(ScanTestRooms.Rectangle(w, d), Turn, Shift);
            for (int k = 0; k < corners.Length; k++) corners[k] += ScanTestRooms.Jitter(random, 0.02f);
            var byHand = new ScanSnapshot
            {
                Outline = OutlineBuilder.FromCorners(corners, corners.Length),
                Walker = ScanTestRooms.Moved(walker, Turn, Shift), FloorHeight = 0.4f,
            };
            ScanSolution manual = ScanSolver.Solve(byHand, "m");

            Assert.That(automatic.Failure, Is.EqualTo(ScanFailure.None));
            Assert.That(manual.Failure, Is.EqualTo(ScanFailure.None));
            Assert.That(manual.Route, Is.EqualTo(ScanRoute.ManualOutline));
            Assert.That(manual.Room.FreeRect.Wf, Is.EqualTo(automatic.Room.FreeRect.Wf).Within(0.10f));
            Assert.That(manual.Room.FreeRect.Df, Is.EqualTo(automatic.Room.FreeRect.Df).Within(0.10f));
            Assert.That(math.distance(manual.Room.Anchor.Position, automatic.Room.Anchor.Position), Is.LessThan(0.10f));
            Assert.That(RoomModelValidator.Errors(manual.Room), Is.Empty);
            Assert.That(RoomModelValidator.Errors(automatic.Room), Is.Empty);
        }

        [Test]
        public void TappedOutline_WinsOverTheMesh()
        {
            // The mesh shows a 4 x 5 m room; the walker fences off a 3 x 3 m part of it.
            SyntheticRoom scan = SyntheticRoom.Rectangle(4f, 5f);
            ScanSnapshot snapshot = ScanTestRooms.Snapshot(scan.Build());
            snapshot.Outline = ScanTestRooms.Rectangle(3f, 3f);
            snapshot.Walker = new float2(1.5f, 1.5f);
            ScanSolution solution = ScanSolver.Solve(snapshot, "t");
            Assert.That(solution.Route, Is.EqualTo(ScanRoute.FloorScan));
            Assert.That(solution.Room.FreeRect.Wf, Is.EqualTo(2.4f).Within(1e-4f));
            Assert.That(solution.Room.FreeRect.Df, Is.EqualTo(2.4f).Within(1e-4f));
            Assert.That(solution.Outline.Length, Is.EqualTo(4));
        }

        [Test]
        public void MeshWithNoFloor_FallsBackToTheOutline()
        {
            // Only walls were meshed: route 2 finds nothing to stand on, route 1 still gives the room.
            var scan = new SyntheticRoom();
            scan.AddWalls(ScanTestRooms.Rectangle(3f, 3f), 2.4f);
            ScanSnapshot snapshot = ScanTestRooms.Snapshot(scan.Build());
            snapshot.Outline = ScanTestRooms.Rectangle(3f, 3f);
            snapshot.Walker = new float2(1.5f, 1.5f);
            ScanSolution solution = ScanSolver.Solve(snapshot, "f");
            Assert.That(solution.Failure, Is.EqualTo(ScanFailure.None));
            Assert.That(solution.Route, Is.EqualTo(ScanRoute.ManualOutline));
            Assert.That(solution.Room.FreeRect.Wf, Is.EqualTo(2.4f).Within(1e-4f));
        }

        [Test]
        public void NothingSeen_IsNoFloor()
        {
            ScanSolution solution = ScanSolver.Solve(new ScanSnapshot(), "n");
            Assert.That(solution.Failure, Is.EqualTo(ScanFailure.NoFloor));
            Assert.That(solution.Room, Is.Null);
        }

        [Test]
        public void TinyOutline_IsTooSmall()
        {
            var snapshot = new ScanSnapshot { Outline = ScanTestRooms.Rectangle(0.9f, 0.9f), Walker = new float2(0.45f, 0.45f) };
            Assert.That(ScanSolver.Solve(snapshot, "s").Failure, Is.EqualTo(ScanFailure.TooSmall));
        }

        [Test]
        public void CrossingOutline_IsInvalid()
        {
            var snapshot = new ScanSnapshot
            {
                Outline = new[] { new float2(0f, 0f), new float2(3f, 3f), new float2(3f, 0f), new float2(0f, 3f) },
            };
            Assert.That(ScanSolver.Solve(snapshot, "x").Failure, Is.EqualTo(ScanFailure.OutlineInvalid));
        }

        [Test]
        public void WalkedPath_AloneGivesARoom()
        {
            var path = new System.Collections.Generic.List<float2>();
            float2[] lap = { new float2(0.3f, 0.3f), new float2(3.7f, 0.3f), new float2(3.7f, 4.7f), new float2(0.3f, 4.7f) };
            for (int k = 0; k < 4; k++)
                for (int s = 0; s < 40; s++) path.Add(math.lerp(lap[k], lap[(k + 1) % 4], s / 40f));
            var snapshot = new ScanSnapshot { Path = path.ToArray(), Walker = new float2(2f, 2.5f) };
            ScanSolution solution = ScanSolver.Solve(snapshot, "p");
            Assert.That(solution.Failure, Is.EqualTo(ScanFailure.None));
            Assert.That(solution.Route, Is.EqualTo(ScanRoute.ManualOutline));
            Assert.That(solution.Room.FreeRect.Wf, Is.EqualTo(3.4f).Within(0.10f));
            Assert.That(solution.Room.FreeRect.Df, Is.EqualTo(4.4f).Within(0.10f));
        }

        [Test]
        public void Preview_GivesTheOutlineAndNoRoom()
        {
            SyntheticRoom scan = SyntheticRoom.Rectangle(3f, 4f);
            ScanSolution preview = ScanSolver.Solve(ScanTestRooms.Snapshot(scan.Build()), "v", 2.4f, false);
            Assert.That(preview.Room, Is.Null);
            Assert.That(preview.Outline.Length, Is.EqualTo(4));
            Assert.That(preview.FloorArea, Is.EqualTo(12f).Within(0.5f));
            Assert.That(preview.Grid, Is.Not.Null);
        }

        [Test]
        public void PartlySeenFloor_LeavesUnseenCellsNotFree()
        {
            // Only the south half of a 4 x 5 m room was swept; the walker taps the whole room.
            var scan = new SyntheticRoom();
            scan.AddFloorRect(0f, 0f, 4f, 2.5f);
            scan.AddWalls(ScanTestRooms.Rectangle(4f, 5f), 2.4f);
            ScanSnapshot snapshot = ScanTestRooms.Snapshot(scan.Build());
            snapshot.Outline = ScanTestRooms.Rectangle(4f, 5f);
            snapshot.Walker = new float2(2f, 1.2f);
            ScanSolution solution = ScanSolver.Solve(snapshot, "h");
            Assert.That(solution.Route, Is.EqualTo(ScanRoute.FloorScan));
            Assert.That(solution.Room.FreeRect.Wf, Is.LessThanOrEqualTo(2.2f + 1e-4f));
            Assert.That(solution.Room.Quality.UnknownShare, Is.EqualTo(0.5f).Within(0.02f));
        }

        [Test]
        public void PlanesLaidAsMesh_CountAsRouteOne()
        {
            ScanSnapshot snapshot = ScanTestRooms.Snapshot(SyntheticRoom.Rectangle(3f, 3f).Build());
            snapshot.MeshFromPlanes = true;
            ScanSolution solution = ScanSolver.Solve(snapshot, "sim");
            Assert.That(solution.Failure, Is.EqualTo(ScanFailure.None));
            Assert.That(solution.Route, Is.EqualTo(ScanRoute.ManualOutline));
            Assert.That(solution.Room.FreeRect.Wf, Is.EqualTo(2.4f).Within(1e-4f));
        }

        [Test]
        public void NeutralClasses_GiveTheSameRoom()
        {
            // A 4 x 5 m room with a table, classed by ARKit and classed by geometry alone.
            SyntheticRoom scan = SyntheticRoom.Rectangle(4f, 5f).AddBox(new float2(2f, 2.5f), new float3(1f, 0.75f, 0.6f));
            scan.SetWalker(new float2(2f, 1.1f));
            FloorGridInput input = scan.Build();
            ScanSolution classed = ScanSolver.Solve(ScanTestRooms.Snapshot(input), "c");

            var neutral = new SurfaceClass[input.TriangleClasses.Length];
            NeutralSurfaceClasses.Fill(input.Vertices, input.Indices, 0, neutral.Length, 0f, neutral, 0);
            ScanSnapshot plain = ScanTestRooms.Snapshot(input);
            plain.Classes = neutral;
            ScanSolution byGeometry = ScanSolver.Solve(plain, "g");

            Assert.That(neutral[0], Is.EqualTo(SurfaceClass.Floor));
            Assert.That(byGeometry.Failure, Is.EqualTo(ScanFailure.None));
            Assert.That(byGeometry.Room.FreeRect.Wf, Is.EqualTo(classed.Room.FreeRect.Wf).Within(0.10f));
            Assert.That(byGeometry.Room.FreeRect.Df, Is.EqualTo(classed.Room.FreeRect.Df).Within(0.10f));
            Assert.That(classed.Room.FreeRect.Wf, Is.EqualTo(1.6f).Within(0.051f));
        }

        [Test]
        public void NeutralClasses_ByHeightAndSlope()
        {
            float3 a = new float3(0f, 0f, 0f), b = new float3(1f, 0f, 0f), c = new float3(0f, 0f, 1f);
            float3 up = new float3(0f, 1f, 0f);
            Assert.That(NeutralSurfaceClasses.Classify(a, c, b, 0f), Is.EqualTo(SurfaceClass.Floor));
            Assert.That(NeutralSurfaceClasses.Classify(a + 0.05f * up, c + 0.05f * up, b + 0.05f * up, 0f), Is.EqualTo(SurfaceClass.Floor));
            Assert.That(NeutralSurfaceClasses.Classify(a + 0.75f * up, c + 0.75f * up, b + 0.75f * up, 0f), Is.EqualTo(SurfaceClass.Table));
            Assert.That(NeutralSurfaceClasses.Classify(a + 2.4f * up, b + 2.4f * up, c + 2.4f * up, 0f), Is.EqualTo(SurfaceClass.Ceiling));
            Assert.That(NeutralSurfaceClasses.Classify(a, b, b + up, 0f), Is.EqualTo(SurfaceClass.Wall));
            Assert.That(NeutralSurfaceClasses.Classify(a, b, new float3(0f, 1f, 1f), 0f), Is.EqualTo(SurfaceClass.None), "a 45 degree slope is neither");
            Assert.That(NeutralSurfaceClasses.Classify(a, a, a, 0f), Is.EqualTo(SurfaceClass.None));

            FloorGridInput room = SyntheticRoom.Rectangle(3f, 3f).Move(0f, new float3(0f, -1.3f, 0f)).Build();
            Assert.That(NeutralSurfaceClasses.EstimateFloor(room.Vertices, room.Indices, room.Indices.Length), Is.EqualTo(-1.3f).Within(1e-4f));
        }

        [TestCase(float.NaN, 0f, 2.4f)]
        [TestCase(2.71f, 0f, 2.7f)]
        [TestCase(1.9f, -0.8f, 2.7f)]
        [TestCase(1.2f, 0f, 2.4f)]
        [TestCase(9f, 0f, 2.4f)]
        public void WallHeight_ComesFromThePlanesWhenPlausible(float wallTop, float floorY, float expected)
        {
            Assert.That(ScanSolver.WallHeight(wallTop, floorY, 2.4f), Is.EqualTo(expected).Within(1e-4f));
        }

        [Test]
        public void WallPlanes_SetTheWallHeightOfTheRoom()
        {
            ScanSnapshot snapshot = ScanTestRooms.Snapshot(SyntheticRoom.Rectangle(3f, 3f, 2.75f).Build());
            snapshot.WallTop = 2.75f;
            ScanSolution solution = ScanSolver.Solve(snapshot, "w");
            Assert.That(solution.Room.Walls.Length, Is.EqualTo(4));
            Assert.That(solution.Room.MaxWallHeight, Is.EqualTo(2.75f).Within(1e-4f));
            foreach (RoomWall wall in solution.Room.Walls) Assert.That(wall.Openings, Is.Empty);
        }
    }
}
