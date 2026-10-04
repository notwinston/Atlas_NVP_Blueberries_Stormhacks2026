using System;
using System.Collections.Generic;
using Atlas.Scan.Grid;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Routes
{
    /// <summary>
    /// The profile and the front of every object when the RoomModel is built from a scan (mesh design section
    /// 5.2): a scanned mesh gives each object its profile and turns its box so that local +Z is the front; the
    /// grid, the ids and the triangle targets are what they were for the boxes as scanned.
    /// </summary>
    public class ScanObjectShapeTests
    {
        const float Quarter = 0.5f * math.PI;
        const int N = ObjectProfile.Side;

        // The chair of these tests: wider than deep, the seat at 0.45 m, and a back three profile rows deep
        // on its own -Z that reaches the top of the box.
        static readonly float3 s_chair = new float3(0.6f, 1f, 0.5f);
        const float SeatHeight = 0.45f, BackDepth = 3f * 0.5f / 16f;
        const int SeatByte = 115;                           // floor(255 * 0.45 + 0.5)
        static readonly float2 s_chairAt = new float2(1.1f, 2.9f);
        const float ChairYaw = 0.4f;

        static float2 Front(float yaw)
        {
            return new float2(math.sin(yaw), math.cos(yaw));
        }

        /// <summary>A 4 m x 4 m room as scanned: floor, walls, the outline, the walker on free floor.</summary>
        static SyntheticRoom Room()
        {
            return SyntheticRoom.Rectangle(4f, 4f).SetWalker(new float2(2.4f, 1.6f));
        }

        /// <summary>The chair's scanned surface: the seat and, along its own -Z edge, the back.</summary>
        static void AddChairMesh(SyntheticRoom scan, float2 centre, float yaw)
        {
            scan.AddBox(centre, new float3(s_chair.x, SeatHeight, s_chair.z), yaw, SurfaceClass.Seat);
            float2 back = centre - Front(yaw) * (0.5f * (s_chair.z - BackDepth));
            scan.AddBox(back, new float3(s_chair.x, s_chair.y, BackDepth), yaw, SurfaceClass.None);
        }

        /// <summary>The chair's box as the scan reports it: the same footprint, its axis some quarter turns away from the chair's front.</summary>
        static ObstacleBox ChairBox(float2 centre, float yaw, int turnedBy)
        {
            return new ObstacleBox
            {
                Base = new float3(centre.x, 0f, centre.y),
                Size = (turnedBy & 1) != 0 ? new float3(s_chair.z, s_chair.y, s_chair.x) : s_chair,
                Yaw = yaw - turnedBy * Quarter,
                Category = (int)ObjectCategory.Chair,
                SourceId = 7,
            };
        }

        static SyntheticRoom RoomWithChair(int turnedBy)
        {
            SyntheticRoom scan = Room();
            AddChairMesh(scan, s_chairAt, ChairYaw);
            scan.AddExternalBox(ChairBox(s_chairAt, ChairYaw, turnedBy));
            return scan;
        }

        static RoomModel Solve(SyntheticRoom scan, bool decideFacing = true)
        {
            ScanSolution solution = ScanSolver.Solve(ScanTestRooms.Snapshot(scan.Build(), true), "shapes", RoomModel.DefaultWallHeight, true, decideFacing);
            Assert.That(solution.Failure, Is.EqualTo(ScanFailure.None));
            return solution.Room;
        }

        /// <summary>The grid cells whose centre lies under an object's box.</summary>
        static HashSet<int> CellsUnder(RoomModel room, RoomObject o)
        {
            var cells = new HashSet<int>();
            RoomSdf sdf = room.Sdf;
            float sin, cos;
            math.sincos(o.Yaw, out sin, out cos);
            for (int j = 0; j < sdf.Height; j++)
            {
                for (int i = 0; i < sdf.Width; i++)
                {
                    float2 c = sdf.CellCentre(i, j) - o.Base.xz;
                    float lx = c.x * cos - c.y * sin, lz = c.x * sin + c.y * cos;
                    if (math.abs(lx) <= 0.5f * o.Size.x && math.abs(lz) <= 0.5f * o.Size.z) cells.Add(j * sdf.Width + i);
                }
            }
            return cells;
        }

        static void AssertChairProfile(byte[] profile, string what)
        {
            Assert.That(profile, Is.Not.Null, what);
            Assert.That(profile.Length, Is.EqualTo(256), what);
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                    Assert.That(profile[ObjectProfile.Index(i, j)], Is.EqualTo(j <= 2 ? 255 : SeatByte), what + ", cell (" + i + ", " + j + ")");
        }

        static void AssertValid(RoomModel room)
        {
            var errors = new List<string>();
            Assert.That(RoomModelValidator.Validate(room, errors), Is.True, string.Join("; ", errors));
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void AChair_WithItsScannedBackOnTheBoxsPlusX_IsTurnedSoThatTheBackIsOnLocalMinusZ()
        {
            // Three quarter turns away: the chair's front is on the box's -X, its back on the box's +X.
            SyntheticRoom scan = RoomWithChair(3);
            RoomModel room = Solve(scan);
            RoomModel asScanned = Solve(scan, false);

            Assert.That(room.Quality.Route, Is.EqualTo(ScanRoute.ObjectBoxes));
            Assert.That(room.Objects.Length, Is.EqualTo(1));
            RoomObject chair = room.Objects[0], before = asScanned.Objects[0];

            // Without facing the box is as the scan gave it, and the scanned back fills its three columns on +X.
            Assert.That(before.Facing, Is.EqualTo(ObjectFacingRule.Box));
            Assert.That(math.dot(Front(before.Yaw + asScanned.Anchor.Yaw), Front(ChairYaw - 3f * Quarter)), Is.GreaterThan(0.99999f));
            Assert.That(before.Size.x, Is.EqualTo(s_chair.z).Within(1e-5f));
            Assert.That(before.Size.z, Is.EqualTo(s_chair.x).Within(1e-5f));
            Assert.That(before.Profile, Is.Not.Null);
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                    Assert.That(before.Profile[ObjectProfile.Index(i, j)], Is.EqualTo(i >= 13 ? 255 : SeatByte), "as scanned, cell (" + i + ", " + j + ")");

            // With facing: the profile rule answered, the back is on local -Z, the front where the chair's front is.
            Assert.That(chair.Category, Is.EqualTo(ObjectCategory.Chair));
            Assert.That(chair.Facing, Is.EqualTo(ObjectFacingRule.Profile));
            AssertChairProfile(chair.Profile, "faced");
            Assert.That(chair.Profile, Is.EqualTo(ObjectProfile.Rotate(before.Profile, 3)));
            Assert.That(math.dot(Front(chair.Yaw + room.Anchor.Yaw), Front(ChairYaw)), Is.GreaterThan(0.99999f));
            Assert.That(chair.Size.x, Is.EqualTo(s_chair.x).Within(1e-5f));
            Assert.That(chair.Size.y, Is.EqualTo(s_chair.y).Within(1e-5f));
            Assert.That(chair.Size.z, Is.EqualTo(s_chair.z).Within(1e-5f));
            // The yaw is in (-pi, pi] and at six decimals again, as a RoomModel file holds it.
            Assert.That(chair.Yaw, Is.GreaterThan(-math.PI).And.LessThanOrEqualTo(math.PI));
            Assert.That(chair.Yaw, Is.EqualTo((float)Math.Round((double)chair.Yaw, 6)));

            // The quarter turn changed nothing else: the same place, the same id and target, the same grid.
            Assert.That(chair.Id, Is.EqualTo(before.Id));
            Assert.That(chair.Base, Is.EqualTo(before.Base));
            Assert.That(chair.TriTarget, Is.EqualTo(before.TriTarget));
            Assert.That(chair.TriTarget, Is.EqualTo(15000));
            Assert.That(chair.Volume, Is.EqualTo(before.Volume).Within(1e-6f));
            Assert.That(room.Sdf.Metres, Is.EqualTo(asScanned.Sdf.Metres));
            Assert.That(room.FreeRect.Wf, Is.EqualTo(asScanned.FreeRect.Wf));
            Assert.That(room.FreeRect.Df, Is.EqualTo(asScanned.FreeRect.Df));
            Assert.That(room.Walkable.Outer, Is.EqualTo(asScanned.Walkable.Outer));
            Assert.That(room.Anchor.Yaw, Is.EqualTo(asScanned.Anchor.Yaw));

            // The same cells lie under the box as before, and none of them is free floor (rule R18).
            HashSet<int> under = CellsUnder(room, chair);
            Assert.That(under.Count, Is.GreaterThan(100));
            Assert.That(under.SetEquals(CellsUnder(asScanned, before)), Is.True);
            foreach (int c in under) Assert.That(room.Sdf.Metres[c], Is.LessThanOrEqualTo(0f), "cell " + c);

            AssertValid(room);
            AssertValid(asScanned);

            // The file carries both, and reads back to the same room.
            string text = RoomModelJson.Write(room);
            Assert.That(text, Does.Contain("\"facing\": \"profile\""));
            Assert.That(text, Does.Contain("\"profile\": \"" + Convert.ToBase64String(chair.Profile) + "\""));
            RoomModel back = RoomModelJson.Read(text);
            Assert.That(back.Objects[0].Facing, Is.EqualTo(ObjectFacingRule.Profile));
            Assert.That(back.Objects[0].Profile, Is.EqualTo(chair.Profile));
            Assert.That(RoomModelJson.Write(back), Is.EqualTo(text));
            Assert.That(RoomModelJson.GeometryHash(back), Is.EqualTo(RoomModelJson.GeometryHash(room)));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void AChair_ReportedAnyQuarterTurnAway_EndsTheSame(int turnedBy)
        {
            RoomModel room = Solve(RoomWithChair(turnedBy));
            RoomObject chair = room.Objects[0];
            Assert.That(chair.Facing, Is.EqualTo(ObjectFacingRule.Profile));
            AssertChairProfile(chair.Profile, "turned by " + turnedBy);
            Assert.That(math.dot(Front(chair.Yaw + room.Anchor.Yaw), Front(ChairYaw)), Is.GreaterThan(0.99999f));
            Assert.That(chair.Size.x, Is.EqualTo(s_chair.x).Within(1e-5f));
            Assert.That(chair.Size.z, Is.EqualTo(s_chair.z).Within(1e-5f));
            AssertValid(room);
        }

        [Test]
        public void AScanElsewhereInTheSession_GivesTheSameProfileAndFront()
        {
            // The whole scan turned by 20 degrees, moved, and its floor 0.4 m up: the mesh is in the session
            // frame and the room frame is found from it.
            const float turn = 0.35f;
            SyntheticRoom scan = RoomWithChair(1).Move(turn, new float3(1.7f, 0.4f, -2.3f));
            RoomModel room = Solve(scan);
            RoomObject chair = room.Objects[0];
            Assert.That(room.Anchor.Position.y, Is.EqualTo(0.4f).Within(1e-4f));
            Assert.That(chair.Base.y, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(chair.Facing, Is.EqualTo(ObjectFacingRule.Profile));
            AssertChairProfile(chair.Profile, "moved");
            // The session's turn runs from +X to +Z, a yaw the other way.
            Assert.That(math.dot(Front(chair.Yaw + room.Anchor.Yaw), Front(ChairYaw - turn)), Is.GreaterThan(0.99999f));
            AssertValid(room);
        }

        [Test]
        public void Storage_AgainstAWall_IsTurnedToFaceTheRoom_AndATableKeepsItsAxis()
        {
            // A cupboard on the south wall whose box points along the wall, a table in the room, and the chair.
            SyntheticRoom scan = RoomWithChair(2);
            scan.AddBox(new float2(2f, 0.2f), new float3(0.9f, 1.8f, 0.4f), 0f, SurfaceClass.None);
            scan.AddExternalBox(new ObstacleBox
            {
                Base = new float3(2f, 0f, 0.2f), Size = new float3(0.4f, 1.8f, 0.9f), Yaw = Quarter,
                Category = (int)ObjectCategory.Storage, SourceId = 8,
            });
            scan.AddExternalBox(new ObstacleBox
            {
                Base = new float3(3.2f, 0f, 3f), Size = new float3(0.8f, 0.75f, 1.2f), Yaw = 0.2f,
                Category = (int)ObjectCategory.Table, SourceId = 9,
            });
            RoomModel room = Solve(scan);
            RoomModel asScanned = Solve(scan, false);
            Assert.That(room.Objects.Length, Is.EqualTo(3));

            // Largest first, as before the turns: the table, the cupboard, the chair.
            for (int k = 0; k < 3; k++)
            {
                Assert.That(room.Objects[k].Id, Is.EqualTo(k + 1));
                Assert.That(room.Objects[k].Category, Is.EqualTo(asScanned.Objects[k].Category));
                Assert.That(room.Objects[k].TriTarget, Is.EqualTo(asScanned.Objects[k].TriTarget));
                Assert.That(room.Objects[k].Base, Is.EqualTo(asScanned.Objects[k].Base));
                Assert.That(CellsUnder(room, room.Objects[k]).SetEquals(CellsUnder(asScanned, asScanned.Objects[k])), Is.True, "object " + (k + 1));
                if (k > 0) Assert.That(room.Objects[k].Volume, Is.LessThanOrEqualTo(room.Objects[k - 1].Volume));
            }
            Assert.That(room.Sdf.Metres, Is.EqualTo(asScanned.Sdf.Metres));

            RoomObject table = room.Objects[0], storage = room.Objects[1], chair = room.Objects[2];
            Assert.That(storage.Category, Is.EqualTo(ObjectCategory.Storage));
            Assert.That(storage.Facing, Is.EqualTo(ObjectFacingRule.Clearance));
            Assert.That(math.dot(Front(storage.Yaw + room.Anchor.Yaw), new float2(0f, 1f)), Is.GreaterThan(0.99999f));
            Assert.That(storage.Size.x, Is.EqualTo(0.9f).Within(1e-5f));
            Assert.That(storage.Size.z, Is.EqualTo(0.4f).Within(1e-5f));
            // Its scanned top is one level surface: a profile, and nothing in it that says where the front is.
            Assert.That(storage.Profile, Is.Not.Null);
            Assert.That(ObjectProfile.Coverage(storage.Profile), Is.EqualTo(1f));

            Assert.That(table.Category, Is.EqualTo(ObjectCategory.Table));
            Assert.That(table.Facing, Is.EqualTo(ObjectFacingRule.Box));
            Assert.That(table.Yaw, Is.EqualTo(asScanned.Objects[0].Yaw));
            Assert.That(table.Size, Is.EqualTo(asScanned.Objects[0].Size));
            // Nothing of the table was scanned: no profile, and no key in the file.
            Assert.That(table.Profile, Is.Null);

            Assert.That(chair.Facing, Is.EqualTo(ObjectFacingRule.Profile));
            AssertChairProfile(chair.Profile, "beside the others");
            AssertValid(room);
            Assert.That(RoomModelJson.Write(RoomModelJson.Read(RoomModelJson.Write(room))), Is.EqualTo(RoomModelJson.Write(room)));
        }

        [Test]
        public void ARoomScannedWithoutALidarMesh_HasNoProfile_AndNothingIsTurned()
        {
            SyntheticRoom scan = RoomWithChair(3);
            scan.AddExternalBox(new ObstacleBox
            {
                Base = new float3(2f, 0f, 0.2f), Size = new float3(0.4f, 1.8f, 0.9f), Yaw = Quarter,
                Category = (int)ObjectCategory.Storage, SourceId = 8,
            });
            FloorGridInput input = scan.Build();

            // The same triangles laid over detected planes (XR Simulation, a phone without LiDAR).
            ScanSnapshot planes = ScanTestRooms.Snapshot(input, true);
            planes.MeshFromPlanes = true;
            // And no triangles at all: the tapped outline and the boxes.
            var outlineOnly = new ScanSnapshot { Outline = input.Outline, Boxes = input.ExternalBoxes, Walker = input.Walker, FloorHeight = 0f };

            foreach (ScanSnapshot snapshot in new[] { planes, outlineOnly })
            {
                ScanSolution solution = ScanSolver.Solve(snapshot, "plain");
                Assert.That(solution.Failure, Is.EqualTo(ScanFailure.None));
                RoomModel room = solution.Room;
                Assert.That(room.Objects.Length, Is.EqualTo(2));
                foreach (RoomObject o in room.Objects)
                {
                    Assert.That(o.Profile, Is.Null, o.Category.ToString());
                    Assert.That(o.Facing, Is.EqualTo(ObjectFacingRule.Box), o.Category.ToString());
                }
                // Each box keeps the axis it was given: the cupboard along the wall, the chair three quarters off.
                RoomObject storage = room.Objects[0], chair = room.Objects[1];
                Assert.That(storage.Category, Is.EqualTo(ObjectCategory.Storage));
                Assert.That(math.dot(Front(storage.Yaw + room.Anchor.Yaw), Front(Quarter)), Is.GreaterThan(0.99999f));
                Assert.That(math.dot(Front(chair.Yaw + room.Anchor.Yaw), Front(ChairYaw - 3f * Quarter)), Is.GreaterThan(0.99999f));
                Assert.That(RoomModelJson.Write(room), Does.Not.Contain("\"profile\""));
                Assert.That(RoomModelJson.Write(room), Does.Not.Contain("\"facing\""));
                AssertValid(room);
            }
        }

        [Test]
        public void AScanThroughTheFlow_EndsWithItsObjectsFaced()
        {
            // The flow as the scan session drives it, with the sensors' snapshot in place of the AR session.
            FloorGridInput input = RoomWithChair(3).Build();
            var flow = new ScanFlow(() => ScanTestRooms.Snapshot(input), false);
            var clock = new DateTime(2026, 10, 4, 9, 30, 15, DateTimeKind.Utc);
            var head = new float3(2.4f, 1.4f, 1.6f);
            flow.Begin(ScanOptions.Default, clock);
            flow.Tick(1f / 60f, TrackingState.Normal, head);        // Starting -> Sweeping
            flow.EndSweep();
            flow.Tick(1f / 60f, TrackingState.Normal, head);        // Processing -> AwaitingConfirm
            Assert.That(flow.State, Is.EqualTo(ScanState.AwaitingConfirm));

            // The room the walker is asked to confirm already has the chair turned.
            RoomObject proposed = flow.Proposal.Preview.Objects[0];
            Assert.That(proposed.Facing, Is.EqualTo(ObjectFacingRule.Profile));
            AssertChairProfile(proposed.Profile, "proposed");

            flow.Confirm();
            flow.CompleteFinalise("anchor", clock.AddSeconds(30));
            Assert.That(flow.State, Is.EqualTo(ScanState.Done));
            RoomModel room = flow.Result;
            RoomObject chair = room.Objects[0];
            Assert.That(chair.Category, Is.EqualTo(ObjectCategory.Chair));
            Assert.That(chair.Facing, Is.EqualTo(ObjectFacingRule.Profile));
            AssertChairProfile(chair.Profile, "final");
            Assert.That(math.dot(Front(chair.Yaw + room.Anchor.Yaw), Front(ChairYaw)), Is.GreaterThan(0.99999f));
            AssertValid(room);
        }

        [Test]
        public void TheBuilder_WithNoSnapshot_LeavesTheObjectsAsTheyAre()
        {
            // Build as the fixture tests call it, with no scan: the clearance rule alone would turn this cupboard.
            SyntheticRoom scan = Room();
            scan.AddExternalBox(new ObstacleBox
            {
                Base = new float3(2f, 0f, 0.2f), Size = new float3(0.4f, 1.8f, 0.9f), Yaw = Quarter,
                Category = (int)ObjectCategory.Storage, SourceId = 8,
            });
            FloorGridInput input = scan.Build();
            FloorGridResult grid = FloorScanCore.Run(input);
            Assert.That(grid.Status, Is.EqualTo(FloorGridStatus.Ok));

            RoomModel plain = RoomModelBuilder.Build(grid, "plain", "", ScanRoute.ObjectBoxes);
            Assert.That(plain.Objects[0].Facing, Is.EqualTo(ObjectFacingRule.Box));
            Assert.That(plain.Objects[0].Size.x, Is.EqualTo(0.4f).Within(1e-5f));

            // The same grid with its scan: the cupboard is turned to the room.
            RoomModel faced = RoomModelBuilder.Build(grid, "faced", "", ScanRoute.ObjectBoxes, RoomModel.DefaultWallHeight, null,
                ScanTestRooms.Snapshot(input, true));
            Assert.That(faced.Objects[0].Facing, Is.EqualTo(ObjectFacingRule.Clearance));
            Assert.That(faced.Objects[0].Size.x, Is.EqualTo(0.9f).Within(1e-5f));
            Assert.That(math.dot(Front(faced.Objects[0].Yaw + faced.Anchor.Yaw), new float2(0f, 1f)), Is.GreaterThan(0.99999f));

            // And with the switch off it is not.
            RoomModel off = RoomModelBuilder.Build(grid, "off", "", ScanRoute.ObjectBoxes, RoomModel.DefaultWallHeight, null,
                ScanTestRooms.Snapshot(input, true), false);
            Assert.That(off.Objects[0].Facing, Is.EqualTo(ObjectFacingRule.Box));
            Assert.That(off.Objects[0].Yaw, Is.EqualTo(plain.Objects[0].Yaw));
            Assert.That(RoomModelJson.GeometryHash(off), Is.EqualTo(RoomModelJson.GeometryHash(plain)));
        }

        // The fixture seen as a LiDAR scan: floor everywhere (and beyond the outline), the outline, every object as a box.
        static ScanSnapshot ScanOf(RoomModel fixture)
        {
            float2 lo = fixture.Outline[0], hi = fixture.Outline[0];
            foreach (float2 p in fixture.Outline) { lo = math.min(lo, p); hi = math.max(hi, p); }
            var scan = new SyntheticRoom();
            scan.AddFloorRect(lo.x - 1f, lo.y - 1f, hi.x + 1f, hi.y + 1f);
            scan.SetOutline(fixture.Outline);
            scan.SetWalker(float2.zero);
            foreach (RoomObject o in fixture.Objects)
                scan.AddExternalBox(new ObstacleBox { Base = o.Base, Size = o.Size, Yaw = o.Yaw, Category = (int)o.Category, SourceId = o.Id });
            return ScanTestRooms.Snapshot(scan.Build(), true);
        }

        // The object of a room whose base is where the reference has one.
        static RoomObject At(RoomModel room, float3 basePoint)
        {
            foreach (RoomObject o in room.Objects)
                if (math.distance(o.Base, basePoint) < 1e-4f) return o;
            Assert.Fail("No object at " + basePoint);
            return null;
        }

        [TestCase(RoomFixtures.Sample4x5Table)]
        [TestCase(RoomFixtures.BudgetRoom)]
        [TestCase(RoomFixtures.BudgetRoom120k)]
        public void AReferenceRoom_ScannedWithFacingOff_KeepsEveryBoxAsWritten(string name)
        {
            // The Python reference decides no facing: its boxes keep the axes they were written with. The same
            // room scanned with the switch off comes out with every box as the reference has it.
            RoomModel fixture = RoomFixtures.LoadFromProject(name);
            ScanSolution solution = ScanSolver.Solve(ScanOf(fixture), fixture.Id, RoomModel.DefaultWallHeight, true, false);
            Assert.That(solution.Failure, Is.EqualTo(ScanFailure.None));
            RoomModel room = solution.Room;
            Assert.That(room.Objects.Length, Is.EqualTo(fixture.Objects.Length));
            foreach (RoomObject want in fixture.Objects)
            {
                RoomObject got = At(room, want.Base);
                Assert.That(got.Category, Is.EqualTo(want.Category));
                Assert.That(math.distance(got.Size, want.Size), Is.LessThan(1e-5f), "object " + want.Id);
                Assert.That(got.Yaw, Is.EqualTo(want.Yaw).Within(1e-5f), "object " + want.Id);
                Assert.That(got.Facing, Is.EqualTo(ObjectFacingRule.Box));
                Assert.That(got.Profile, Is.Null);
            }
            Assert.That(room.Sdf.Width, Is.EqualTo(fixture.Sdf.Width));
            Assert.That(room.Sdf.Height, Is.EqualTo(fixture.Sdf.Height));
            for (int c = 0; c < room.Sdf.Metres.Length; c++)
                Assert.That(room.Sdf.Metres[c] > 0f, Is.EqualTo(fixture.Sdf.Metres[c] > 0f), "free or not, cell " + c);
            AssertValid(room);
        }

        [Test]
        public void TheBudgetRoom_ScannedWithFacingOn_TurnsTheFurnitureThatLooksAtAWall()
        {
            // The reference room has a couch on the north wall and a cupboard and a chair on the east wall whose
            // boxes look at the wall behind them. Scanned for real, those three are turned half a turn and
            // nothing else in the room changes.
            RoomModel fixture = RoomFixtures.LoadFromProject(RoomFixtures.BudgetRoom);
            ScanSnapshot scan = ScanOf(fixture);
            RoomModel room = ScanSolver.Solve(scan, "on").Room, asScanned = ScanSolver.Solve(scan, "off", RoomModel.DefaultWallHeight, true, false).Room;
            Assert.That(room.Objects.Length, Is.EqualTo(12));
            Assert.That(room.Sdf.Metres, Is.EqualTo(asScanned.Sdf.Metres));

            var turned = new[] { new float3(-0.6f, 0f, 2.25f), new float3(1.75f, 0f, 0f), new float3(1.75f, 0f, 1.4f) };
            int halfTurns = 0;
            for (int k = 0; k < room.Objects.Length; k++)
            {
                RoomObject o = room.Objects[k], before = asScanned.Objects[k];
                string what = o.Category + " " + o.Id;
                Assert.That(o.Id, Is.EqualTo(before.Id), what);
                Assert.That(o.Base, Is.EqualTo(before.Base), what);
                Assert.That(o.TriTarget, Is.EqualTo(before.TriTarget), what);
                Assert.That(o.Profile, Is.Null, what);       // the scan saw the floor only
                Assert.That(CellsUnder(room, o).SetEquals(CellsUnder(asScanned, before)), Is.True, what);

                bool wasTurned = false;
                foreach (float3 at in turned) wasTurned |= math.distance(at, o.Base) < 1e-4f;
                if (wasTurned)
                {
                    halfTurns++;
                    Assert.That(o.Facing, Is.EqualTo(ObjectFacingRule.Clearance), what);
                    Assert.That(math.dot(Front(o.Yaw), Front(before.Yaw)), Is.LessThan(-0.99999f), what);
                    Assert.That(o.Size, Is.EqualTo(before.Size), what);
                }
                else
                {
                    Assert.That(o.Yaw, Is.EqualTo(before.Yaw), what);
                    Assert.That(o.Size, Is.EqualTo(before.Size), what);
                }

                bool keepsItsAxis = o.Category == ObjectCategory.Table || o.Category == ObjectCategory.Plant ||
                                    o.Category == ObjectCategory.Lamp || o.Category == ObjectCategory.Other;
                if (keepsItsAxis) Assert.That(o.Facing, Is.EqualTo(ObjectFacingRule.Box), what);
                if (o.Facing == ObjectFacingRule.Clearance)
                {
                    // In front of it the floor is free, behind it is the wall.
                    float2 reach = Front(o.Yaw) * (0.5f * o.Size.z + ObjectFacing.ClearanceProbe);
                    Assert.That(room.Sdf.Sample(o.Base.xz + reach) - room.Sdf.Sample(o.Base.xz - reach),
                        Is.GreaterThanOrEqualTo(ObjectFacing.ClearanceMargin), what);
                }
            }
            Assert.That(halfTurns, Is.EqualTo(3));
            Assert.That(RoomModelJson.GeometryHash(room), Is.Not.EqualTo(RoomModelJson.GeometryHash(asScanned)));
            AssertValid(room);
        }
    }
}
