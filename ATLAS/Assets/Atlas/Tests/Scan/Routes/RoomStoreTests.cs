using System;
using System.Collections.Generic;
using System.IO;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Routes
{
    /// <summary>Saved rooms: room_&lt;id&gt;.json under one folder, written off the caller's thread.</summary>
    public class RoomStoreTests
    {
        string _folder;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "atlas_rooms_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        static RoomModel Room(string id, string created, float w = 3f, float d = 3f)
        {
            RoomModel room = RoomModelBuilder.FromOutline(id, ScanTestRooms.Rectangle(w, d), null, new float2(0.5f * w, 0.5f * d));
            room.CreatedUtc = created;
            return room;
        }

        [Test]
        public void SaveAndLoad_GiveTheSameRoom()
        {
            var store = new RoomStore(_folder);
            RoomModel room = Room("20261004_093015", "2026-10-04T09:30:15Z", 4f, 5f);
            store.Save(room);
            Assert.That(store.WaitForWrites(5000), Is.True);
            Assert.That(store.LastError, Is.Null);
            Assert.That(File.Exists(Path.Combine(_folder, "room_20261004_093015.json")), Is.True);
            Assert.That(File.Exists(store.JsonPath(room.Id) + ".tmp"), Is.False);

            RoomModel back;
            string error;
            Assert.That(new RoomStore(_folder).TryLoad(room.Id, out back, out error), Is.True, error);
            Assert.That(RoomModelJson.Write(back), Is.EqualTo(RoomModelJson.Write(room)));
            Assert.That(RoomModelJson.GeometryHash(back), Is.EqualTo(RoomModelJson.GeometryHash(room)));
        }

        [Test]
        public void LatestSaveWins()
        {
            var store = new RoomStore(_folder);
            for (int k = 0; k < 20; k++) store.Save(Room("r", "2026-10-04T09:30:" + k.ToString("00") + "Z"));
            RoomModel queued;
            string error;
            Assert.That(store.TryLoad("r", out queued, out error), Is.True, error);
            Assert.That(queued.CreatedUtc, Is.EqualTo("2026-10-04T09:30:19Z"), "a room still queued is read as it will be written");
            Assert.That(store.WaitForWrites(5000), Is.True);
            RoomModel back;
            Assert.That(new RoomStore(_folder).TryLoad("r", out back, out error), Is.True, error);
            Assert.That(back.CreatedUtc, Is.EqualTo("2026-10-04T09:30:19Z"));
        }

        [Test]
        public void List_IsNewestFirst_AndSkipsBrokenFiles()
        {
            var store = new RoomStore(_folder);
            var list = new List<RoomSummary>();
            Assert.That(store.List(list), Is.EqualTo(0));

            store.SaveNow(Room("old", "2026-10-01T08:00:00Z"));
            store.SaveNow(Room("new", "2026-10-04T08:00:00Z", 4f, 5f));
            File.WriteAllText(store.JsonPath("broken"), "{ not a room");
            File.WriteAllBytes(store.WorldMapPath("new"), new byte[] { 1, 2, 3 });

            Assert.That(store.List(list), Is.EqualTo(2));
            Assert.That(list[0].Id, Is.EqualTo("new"));
            Assert.That(list[0].HasWorldMap, Is.True);
            Assert.That(list[0].Wf, Is.EqualTo(3.4f).Within(1e-5f));
            Assert.That(list[0].Df, Is.EqualTo(4.4f).Within(1e-5f));
            Assert.That(list[1].Id, Is.EqualTo("old"));
            Assert.That(list[1].HasWorldMap, Is.False);
            Assert.That(list[1].ObjectCount, Is.EqualTo(0));
        }

        [Test]
        public void ARoomSavedUnderTheEarlierTriangleRule_LoadsWithTodaysTargets()
        {
            // The one-table reference room as an earlier build saved it: 60,000 / 1, cut to 5,000. Today one
            // object has all the surface: 120,000, cut to 15,000.
            string today = RoomFixtures.ReadTextFromProject(RoomFixtures.Sample4x5Table);
            Assert.That(today, Does.Contain("\"triTarget\": 15000"));
            string earlier = today.Replace("\"triTarget\": 15000", "\"triTarget\": 5000");

            // Read as it is, that room breaks rule R17 and Home would refuse it.
            RoomModel asSaved = RoomModelJson.Read(earlier);
            Assert.That(asSaved.Objects[0].TriTarget, Is.EqualTo(5000));
            Assert.That(string.Join("; ", RoomModelValidator.Errors(asSaved)), Does.Contain("R17"));

            var store = new RoomStore(_folder);
            Directory.CreateDirectory(_folder);
            File.WriteAllText(store.JsonPath("earlier"), earlier);
            RoomModel room;
            string error;
            Assert.That(store.TryLoad("earlier", out room, out error), Is.True, error);
            Assert.That(room.Objects.Length, Is.EqualTo(1));
            Assert.That(room.Objects[0].TriTarget, Is.EqualTo(15000));
            Assert.That(RoomModelValidator.Errors(room), Is.Empty);
            // Nothing else about the room changed, and loading wrote nothing.
            Assert.That(RoomModelJson.GeometryHash(room), Is.EqualTo(RoomModelJson.GeometryHash(asSaved)));
            Assert.That(RoomModelJson.Write(room), Is.EqualTo(today));
            Assert.That(File.ReadAllText(store.JsonPath("earlier")), Is.EqualTo(earlier));
            Assert.That(Directory.GetFiles(_folder).Length, Is.EqualTo(1));

            // The list of saved rooms shows it too.
            var list = new List<RoomSummary>();
            Assert.That(store.List(list), Is.EqualTo(1));
            Assert.That(list[0].ObjectCount, Is.EqualTo(1));
        }

        [Test]
        public void ARoomOfStandIns_KeepsItsTargets()
        {
            // The stand-in mesh has 5,000 triangles by definition (rule R17), whatever the rule for scanned objects says.
            string text = RoomFixtures.ReadTextFromProject(RoomFixtures.BudgetRoom);
            var store = new RoomStore(_folder);
            Directory.CreateDirectory(_folder);
            File.WriteAllText(store.JsonPath("standins"), text);
            RoomModel room;
            string error;
            Assert.That(store.TryLoad("standins", out room, out error), Is.True, error);
            Assert.That(room.Objects.Length, Is.EqualTo(12));
            foreach (RoomObject o in room.Objects)
            {
                Assert.That(o.Mesh, Is.EqualTo(MeshState.StandIn));
                Assert.That(o.TriTarget, Is.EqualTo(5000));
            }
            Assert.That(RoomModelValidator.Errors(room), Is.Empty);
            Assert.That(RoomModelJson.Write(room), Is.EqualTo(text));
        }

        [Test]
        public void ARoomStillQueuedForWriting_IsRefreshedToo()
        {
            // A room handed to Save with stale targets is read back from the queue with today's.
            RoomModel room = RoomModelBuilder.FromOutline("queued", ScanTestRooms.Rectangle(4f, 5f),
                new[] { new Atlas.Scan.Grid.ObstacleBox { Base = new float3(2f, 0f, 2.5f), Size = new float3(1f, 0.75f, 0.6f), Category = (int)ObjectCategory.Table } },
                new float2(2f, 1.1f));
            Assert.That(room.Objects[0].TriTarget, Is.EqualTo(15000));
            room.Objects[0].TriTarget = 5000;
            var store = new RoomStore(_folder);
            store.Save(room);
            RoomModel back;
            string error;
            Assert.That(store.TryLoad("queued", out back, out error), Is.True, error);
            Assert.That(back.Objects[0].TriTarget, Is.EqualTo(15000));
            Assert.That(store.WaitForWrites(5000), Is.True);
        }

        [Test]
        public void MissingAndInvalid_AreReported()
        {
            var store = new RoomStore(_folder);
            RoomModel room;
            string error;
            Assert.That(store.TryLoad("nothing", out room, out error), Is.False);
            Assert.That(error, Does.Contain("nothing"));
            Assert.That(store.TryLoad("", out room, out error), Is.False);
            Directory.CreateDirectory(_folder);
            File.WriteAllText(store.JsonPath("bad"), "[]");
            Assert.That(store.TryLoad("bad", out room, out error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
            Assert.That(store.Exists("bad"), Is.True);
            Assert.That(store.Exists("nothing"), Is.False);
        }

        [Test]
        public void Delete_RemovesTheRoomAndItsWorldMap()
        {
            var store = new RoomStore(_folder);
            store.SaveNow(Room("gone", "2026-10-04T08:00:00Z"));
            File.WriteAllBytes(store.WorldMapPath("gone"), new byte[] { 1 });
            store.Delete("gone");
            Assert.That(File.Exists(store.JsonPath("gone")), Is.False);
            Assert.That(File.Exists(store.WorldMapPath("gone")), Is.False);
            store.Delete("gone");
            store.Delete(null);
            Assert.That(store.LastError, Is.Null);
        }
    }
}
