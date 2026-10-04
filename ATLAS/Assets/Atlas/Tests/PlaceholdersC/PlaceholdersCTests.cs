// PLACEHOLDER (lane C, P1-X07): contract surface only; replaced by lane C's implementation
using System.IO;
using Atlas.Input;
using Atlas.Safety;
using Atlas.Scan;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.PlaceholdersC.Tests
{
    // Self-tests of the lane C placeholders. Each names the Master Plan criterion it checks and asserts only numbers
    // the plan publishes or numbers read from the fixture files.
    public class PlaceholdersCTests
    {
        static readonly string[] Rooms = { "room_3x3", "room_3x4_L", "room_4x5_table", "budget_room" };

        static string Recordings => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "recordings"));
        static RoomModel Room(string name) => RoomModel.Load(Path.Combine(Recordings, name + ".json"));

        // Master Plan §4.3: the schema and sample rooms load; JSON round trip keeps every field.
        [Test]
        public void Placeholder_RoomModel_RoundTrip([ValueSource(nameof(Rooms))] string name)
        {
            var a = Room(name);
            var b = RoomModel.FromJson(a.ToJson());
            Assert.AreEqual(a.ToJson(), b.ToJson());
            Assert.AreEqual("atlas.room/1", b.schema);
            Assert.AreEqual(a.freeRect.Wf, b.freeRect.Wf);
            Assert.AreEqual(a.cells.aligned.n, b.cells.aligned.n);
            Assert.AreEqual(a.walls.Length, b.walls.Length);
            Assert.AreEqual(a.objects.Length, b.objects.Length);
            Assert.AreEqual(a.sdf.Sample(0, 0), b.sdf.Sample(0, 0));
        }

        // Master Plan §4.4, §11.1 FloorScan_Synthetic: 3 x 3 m room gives s = 2.0 m and L (q = 5) = 1.885 m.
        [Test]
        public void Placeholder_RoomModel_LoopFit()
        {
            var r = Room("room_3x3");
            Assert.AreEqual(2.4f, r.freeRect.Wf, 1e-4f);
            Assert.AreEqual(2.0f, r.lapSquare.s, 1e-4f);
            Assert.AreEqual(1.885f, r.curvature.L5, 0.001f);
        }

        // Master Plan §4.4 loop-fit rule: every fixture's s, L, R follow from its free rectangle.
        [Test]
        public void Placeholder_Fixtures_FollowLoopFitRule([ValueSource(nameof(Rooms))] string name)
        {
            var r = Room(name);
            float s = Mathf.Clamp(Mathf.Min(r.freeRect.Wf, r.freeRect.Df) - 0.4f, 1.0f, 3.2f);
            Assert.AreEqual(s, r.lapSquare.s, 1e-4f);
            Assert.AreEqual(s / 1.0613f, r.curvature.L5, 1e-3f);
            Assert.AreEqual(s / 1.3170f, r.curvature.L6, 1e-3f);
            Assert.AreEqual(2 * s / Mathf.PI, r.curvature.R3, 1e-3f);
            Assert.AreEqual(Mathf.Min(r.freeRect.Wf, r.freeRect.Df) < 1.5f, r.standing);   // §4.4
        }

        // Master Plan §4.3 glued-cell rule and §4.7 grid examples (3 x 3 m room).
        [Test]
        public void Placeholder_Fixture_CellExamples()
        {
            var r = Room("room_3x3");
            Assert.AreEqual(2, r.cells.glued.nW); Assert.AreEqual(2, r.cells.glued.nD);
            Assert.AreEqual(1.20f, r.cells.glued.cellW, 1e-4f);
            Assert.AreEqual(3, r.cells.aligned.n); Assert.AreEqual(3, r.cells.aligned.m);
            Assert.AreEqual(0.8f, r.cells.aligned.g, 1e-4f);
        }

        // Master Plan §4.3: sdf is positive inside free floor, 5 cm grid, within the 160 x 160 budget (§7.4).
        [Test]
        public void Placeholder_Fixture_SdfIsPositiveInsideFreeFloor([ValueSource(nameof(Rooms))] string name)
        {
            var r = Room(name);
            Assert.AreEqual(0.05f, r.sdf.cell, 1e-6f);
            Assert.LessOrEqual(r.sdf.cols, 160); Assert.LessOrEqual(r.sdf.rows, 160);
            Assert.Greater(r.sdf.Sample(0, 0), 0.5f);               // anchor is in the free rectangle
            Assert.Less(r.sdf.Sample(50, 50), 0f);                  // outside the grid is not free
            Assert.Less(r.sdf.Sample(r.walls[0].a.x - 0.2f, r.walls[0].a.z - 0.2f), 0f);
        }

        // Master Plan §4.2: WALK = {SDF > 0.25 m} and the SDF reads 0.05 m less, so the walkable boundary lies 0.30 m from every
        // wall and obstacle; checked on edge midpoints of the outer outline and the holes (nearest cell, 5 cm grid).
        [Test]
        public void Placeholder_Fixture_WalkableMatchesSdfContour([ValueSource(nameof(Rooms))] string name)
        {
            var r = Room(name);
            var rings = new System.Collections.Generic.List<FloorPoint[]> { r.walkable.outer };
            foreach (var h in r.walkable.holes) rings.Add(h.points);
            foreach (var ring in rings)
                for (int i = 0; i < ring.Length; i++)
                {
                    var a = ring[i]; var b = ring[(i + 1) % ring.Length];
                    if (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z) < 0.5f) continue;   // short edges sit at rounded corners
                    float d = r.sdf.Sample((a.x + b.x) / 2, (a.z + b.z) / 2);
                    Assert.AreEqual(0.25f, d, 0.05f, name + " edge " + i);
                }
        }

        // Master Plan §4.5, §8.1 (AtlasSurface culls back faces): the Home wall quads face into the room.
        [Test]
        public void Placeholder_Home_WallsFaceInward([ValueSource(nameof(Rooms))] string name)
        {
            var r = Room(name);
            var m = HomeRebuild.Build(r).Walls;
            var v = m.vertices; var n = m.normals;
            for (int i = 0; i < r.walls.Length; i++)
            {
                var a = r.walls[i].a; var b = r.walls[i].b;
                var mid = new Vector2((a.x + b.x) / 2, (a.z + b.z) / 2);
                var nn = new Vector2(n[4 * i].x, n[4 * i].z).normalized;
                Assert.IsTrue(InsideOutline(r.walls, mid.x + nn.x * 0.2f, mid.y + nn.y * 0.2f), name + " wall " + i);   // furniture can sit against a wall, so test the outline, not the sdf
            }
        }

        static bool InsideOutline(Wall[] w, float x, float z)
        {
            bool inside = false;
            foreach (var e in w)
                if ((e.a.z > z) != (e.b.z > z) && x < (e.b.x - e.a.x) * (z - e.a.z) / (e.b.z - e.a.z) + e.a.x) inside = !inside;
            return inside;
        }

        // Master Plan §4.3: walls form a closed outline; objects carry u8 ids.
        [Test]
        public void Placeholder_Fixture_WallsClosedObjectsValid([ValueSource(nameof(Rooms))] string name)
        {
            var r = Room(name);
            for (int i = 0; i < r.walls.Length; i++)
            {
                var next = r.walls[(i + 1) % r.walls.Length].a;
                Assert.AreEqual(next.x, r.walls[i].b.x, 1e-5f); Assert.AreEqual(next.z, r.walls[i].b.z, 1e-5f);
            }
            foreach (var o in r.objects) Assert.That(o.id, Is.InRange(0, 255));
        }

        // Master Plan §4.3 budget_room: twelve objects of exactly 5,000 triangles, none over 0.25 m an edge: 60,000 in all.
        [Test]
        public void Placeholder_Home_BudgetTriangles()
        {
            var room = Room("budget_room");
            Assert.AreEqual(12, room.objects.Length);
            var home = HomeRebuild.Build(room);
            Assert.AreEqual(12, home.ObjectCount);
            int total = 0;
            for (int i = 0; i < home.ObjectCount; i++)
            {
                var m = home.ObjectMesh(i);
                Assert.AreEqual(5000, m.triangles.Length / 3);
                Assert.LessOrEqual(MaxEdge(m), 0.25f);
                total += m.triangles.Length / 3;
            }
            Assert.AreEqual(60000, total);
        }

        // Master Plan §8.4: Home shell (floor + walls) is at most 3,000 triangles.
        [Test]
        public void Placeholder_Home_ShellBudget([ValueSource(nameof(Rooms))] string name)
        {
            var home = HomeRebuild.Build(Room(name));
            int tris = (home.Floor.triangles.Length + home.Walls.triangles.Length) / 3;
            Assert.Greater(tris, 0);
            Assert.LessOrEqual(tris, 3000);
        }

        // Master Plan §4.5/§6.4: object meshes are base-centred (origin at the centre of the base, +Y up).
        [Test]
        public void Placeholder_Home_ObjectMeshIsBaseCentred()
        {
            var room = Room("room_4x5_table");
            var home = HomeRebuild.Build(room);
            var b = home.ObjectMesh(0).bounds;
            Assert.AreEqual(0f, b.min.y, 1e-4f);
            Assert.AreEqual(room.objects[0].size.y, b.max.y, 1e-4f);
            Assert.AreEqual(0f, b.center.x, 1e-4f);
            Assert.AreEqual(room.objects[0].size.x, b.size.x, 1e-4f);
            Assert.AreEqual("table", home.ObjectRecord(0).category);
        }

        // Master Plan §6.5, §4.10: the placeholder guard is always Clear and Clearance reads the static field.
        [Test]
        public void Placeholder_BumpGuard_AlwaysClear()
        {
            var room = Room("room_3x3");
            var guard = new BumpGuard(room);
            guard.Tick(new SensorFrame());
            Assert.AreEqual(BumpLevel.Clear, guard.Level);
            Assert.IsFalse(guard.RescanAdvised);
            IClearanceQuery q = guard;
            Assert.AreEqual(room.sdf.Sample(0.3f, -0.2f), q.Clearance(new float2(0.3f, -0.2f)));
        }

        // Master Plan §10.1.3 / P1-C10: dwell 0.6-0.8 s; Dwell() raises Dwelled; no gamepad means no action.
        [Test]
        public void Placeholder_Input_GazeDwellAndTwins()
        {
            Assert.That(GazeTarget.DwellSeconds, Is.InRange(0.6f, 0.8f));
            var go = new GameObject("gaze");
            try
            {
                var g = go.AddComponent<GazeTarget>();
                g.Action = AtlasAction.Confirm;
                int n = 0;
                g.Dwelled += () => n++;
                g.Dwell();
                Assert.AreEqual(1, n);
            }
            finally { Object.DestroyImmediate(go); }
            IActionSource src = new GamepadActionSource();
            Assert.DoesNotThrow(() => src.Triggered(AtlasAction.ReturnHome));
            Assert.That(src.Stick.magnitude, Is.InRange(0f, 1.5f));
        }

        // Master Plan App. A.4 / C.8: lap of five sides of the s = 2.0 lap square, 60 Hz, head y = 1.6 m.
        [Test]
        public void Placeholder_PoseLog_Lap5()
        {
            var s = PoseLog.Read(Path.Combine(Recordings, "walk_lap5_3x3.poselog"));
            Assert.Greater(s.Length, 900);
            Assert.AreEqual(-1f, s[0].Position.x, 1e-3f); Assert.AreEqual(-1f, s[0].Position.z, 1e-3f);
            Assert.AreEqual(1.6f, s[0].Position.y, 1e-3f);
            Assert.AreEqual(1.0 / 60, s[1].Time - s[0].Time, 1e-5);
            foreach (var p in s)
            {
                Assert.That(Mathf.Abs(p.Position.x), Is.LessThanOrEqualTo(1.001f));
                Assert.That(Mathf.Abs(p.Position.z), Is.LessThanOrEqualTo(1.001f));
                Assert.AreEqual(0, p.Tracking);
            }
            var end = s[s.Length - 1].Position;
            Assert.AreEqual(1f, end.x, 1e-3f); Assert.AreEqual(-1f, end.z, 1e-3f);   // five sides end one side past the start
        }

        // Master Plan App. C.8: still log, +/- 0.5 mm jitter over 60 s at 60 Hz.
        [Test]
        public void Placeholder_PoseLog_Still60s()
        {
            var s = PoseLog.Read(Path.Combine(Recordings, "walk_still_60s.poselog"));
            Assert.AreEqual(3600, s.Length);
            foreach (var p in s)
            {
                Assert.That(Mathf.Abs(p.Position.x), Is.LessThanOrEqualTo(6e-4f));
                Assert.That(Mathf.Abs(p.Position.y - 1.6f), Is.LessThanOrEqualTo(6e-4f));
            }
        }

        static float MaxEdge(Mesh m)
        {
            var v = m.vertices; var t = m.triangles; float max = 0;
            for (int i = 0; i < t.Length; i += 3)
                for (int k = 0; k < 3; k++)
                    max = Mathf.Max(max, Vector3.Distance(v[t[i + k]], v[t[i + (k + 1) % 3]]));
            return max;
        }
    }
}
