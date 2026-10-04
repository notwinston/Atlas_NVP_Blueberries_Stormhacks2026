using System;
using System.Collections.Generic;
using System.IO;
using Atlas.Scan.Grid;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests
{
    /// <summary>
    /// Every RoomModel fixture was produced by the Python reference (tools/rooms/roommath.py). Here the same room
    /// goes through the C# pipeline as a scan, and the grid, the distance field, the free rectangle and the
    /// walkable polygon must come out as stored.
    /// </summary>
    public class FloorScanFixtureTests
    {
        static string FixtureDirectory()
        {
            string project = Environment.GetEnvironmentVariable("ATLAS_PROJECT_DIR");
            if (string.IsNullOrEmpty(project)) project = Directory.GetCurrentDirectory();
            return Path.Combine(project, "Assets", "Atlas", "World", "Resources", "AtlasRooms");
        }

        static IEnumerable<string> FixtureNames()
        {
            foreach (string file in Directory.GetFiles(FixtureDirectory(), "*.json"))
                yield return Path.GetFileNameWithoutExtension(file);
        }

        [Test]
        public void FixturesExist()
        {
            var names = new List<string>(FixtureNames());
            Assert.That(names, Does.Contain("sample_3x3"));
            Assert.That(names, Does.Contain("sample_L_3x4"));
            Assert.That(names, Does.Contain("sample_4x5_table"));
            Assert.That(names, Does.Contain("budget_room"));
        }

        [TestCaseSource(nameof(FixtureNames))]
        public void Fixture_IsReproduced(string name)
        {
            object room = MiniJson.Parse(File.ReadAllText(Path.Combine(FixtureDirectory(), name + ".json")));
            List<object> outline = MiniJson.List(room, "outline");
            var points = new float2[outline.Count];
            float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
            for (int k = 0; k < points.Length; k++)
            {
                points[k] = new float2((float)MiniJson.Num(outline[k], 0), (float)MiniJson.Num(outline[k], 1));
                lo = math.min(lo, points[k]);
                hi = math.max(hi, points[k]);
            }
            // The scan saw floor everywhere (and beyond the outline, which must not matter) and every object.
            var scan = new SyntheticRoom();
            scan.AddFloorRect(lo.x - 1f, lo.y - 1f, hi.x + 1f, hi.y + 1f);
            scan.SetOutline(points);
            scan.SetWalker(new float2(0f, 0f));
            List<object> objects = MiniJson.List(room, "objects");
            foreach (object o in objects)
            {
                List<object> b = MiniJson.List(o, "base"), s = MiniJson.List(o, "size");
                scan.AddExternalBox(new ObstacleBox
                {
                    Base = new float3((float)MiniJson.Num(b, 0), (float)MiniJson.Num(b, 1), (float)MiniJson.Num(b, 2)),
                    Size = new float3((float)MiniJson.Num(s, 0), (float)MiniJson.Num(s, 1), (float)MiniJson.Num(s, 2)),
                    Yaw = (float)MiniJson.Num(o, "yaw"),
                    SourceId = (int)MiniJson.Num(o, "id"),
                });
            }

            FloorGridResult result = FloorScanCore.Run(scan.Build());

            Assert.That(result.Status, Is.EqualTo(FloorGridStatus.Ok));
            Dictionary<string, object> rect = MiniJson.Obj(room, "freeRect");
            Assert.That(result.Wf, Is.EqualTo(MiniJson.Num(rect, "wf")).Within(1e-5), "wf");
            Assert.That(result.Df, Is.EqualTo(MiniJson.Num(rect, "df")).Within(1e-5), "df");
            // The fixture is already in the room frame, so the scan must find the same frame again.
            Assert.That(math.length(result.AnchorPosition), Is.LessThan(1e-5f), "anchor");
            Assert.That(result.AnchorYaw, Is.EqualTo(0f).Within(1e-6f), "anchor yaw");
            Assert.That(result.Standing, Is.EqualTo((bool)((Dictionary<string, object>)room)["standing"]));
            Assert.That(result.Fit.S, Is.EqualTo(MiniJson.Num(MiniJson.Obj(room, "lapSquare"), "side")).Within(1e-5));
            Assert.That(result.Fit.L5, Is.EqualTo(MiniJson.Num(MiniJson.Obj(room, "curvature"), "l5")).Within(1e-5));
            Assert.That(result.UnknownShare, Is.EqualTo(MiniJson.Num(MiniJson.Obj(room, "quality"), "unknownShare")).Within(1e-6));

            Dictionary<string, object> sdf = MiniJson.Obj(room, "sdf");
            Assert.That(result.CellSize, Is.EqualTo(MiniJson.Num(sdf, "cell")).Within(1e-7));
            Assert.That(result.Width, Is.EqualTo((int)MiniJson.Num(sdf, "width")), "grid width");
            Assert.That(result.Height, Is.EqualTo((int)MiniJson.Num(sdf, "height")), "grid height");
            Assert.That(result.OriginX, Is.EqualTo(MiniJson.Num(sdf, "originX")).Within(1e-5), "origin x");
            Assert.That(result.OriginZ, Is.EqualTo(MiniJson.Num(sdf, "originZ")).Within(1e-5), "origin z");
            // Signed 16-bit little-endian millimetres, row-major, base64.
            byte[] raw = Convert.FromBase64String((string)sdf["data"]);
            Assert.That(raw.Length, Is.EqualTo(2 * result.Sdf.Length));
            int worst = 0;
            for (int c = 0; c < result.Sdf.Length; c++)
            {
                int stored = (short)(raw[2 * c] | (raw[2 * c + 1] << 8));
                int mine = (int)Math.Floor(result.Sdf[c] * 1000.0 + 0.5);
                worst = Math.Max(worst, Math.Abs(stored - mine));
                // The sign carries "free or not", so it must agree exactly.
                Assert.That(result.Sdf[c] > 0f, Is.EqualTo(stored > 0), "sign of cell " + c);
            }
            Assert.That(worst, Is.LessThanOrEqualTo(1), "distance field, millimetres");

            Dictionary<string, object> walkable = MiniJson.Obj(room, "walkable");
            AssertRing(MiniJson.List(walkable, "outer"), result.WalkableOuter, "outer ring");
            List<object> holes = MiniJson.List(walkable, "holes");
            Assert.That(result.WalkableHoles.Length, Is.EqualTo(holes.Count), "holes");
            for (int k = 0; k < holes.Count; k++) AssertRing((List<object>)holes[k], result.WalkableHoles[k], "hole " + k);

            // Every object comes back as a supplied box, unchanged, and the scan adds none of its own.
            Assert.That(result.Boxes.Length, Is.EqualTo(objects.Count), "boxes");
            for (int k = 0; k < objects.Count; k++)
            {
                Assert.That(result.Boxes[k].FromScan, Is.False);
                Assert.That(result.Boxes[k].SourceId, Is.EqualTo((int)MiniJson.Num(objects[k], "id")));
                Assert.That(result.Boxes[k].Yaw, Is.EqualTo(MiniJson.Num(objects[k], "yaw")).Within(1e-5));
            }
        }

        static void AssertRing(List<object> stored, float2[] mine, string what)
        {
            Assert.That(mine.Length, Is.EqualTo(stored.Count), what + " point count");
            for (int k = 0; k < mine.Length; k++)
            {
                Assert.That(mine[k].x, Is.EqualTo(MiniJson.Num(stored[k], 0)).Within(1e-5), what + " x of point " + k);
                Assert.That(mine[k].y, Is.EqualTo(MiniJson.Num(stored[k], 1)).Within(1e-5), what + " z of point " + k);
            }
        }
    }
}
