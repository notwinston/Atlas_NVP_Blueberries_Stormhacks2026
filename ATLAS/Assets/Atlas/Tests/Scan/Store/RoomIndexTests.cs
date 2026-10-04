using System;
using System.Collections.Generic;
using System.IO;
using Atlas.Scan.Tests.Routes;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Store
{
    /// <summary>The saved-room index and the world map file beside a room (lane C section 12).</summary>
    public class RoomIndexTests
    {
        string _folder;
        RoomStore _store;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "atlas_store_" + Guid.NewGuid().ToString("N"));
            _store = new RoomStore(_folder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        static RoomModel Room(string id, string created)
        {
            RoomModel room = RoomModelBuilder.FromOutline(id, ScanTestRooms.Rectangle(3f, 4f), null, new float2(1.5f, 2f));
            room.CreatedUtc = created;
            return room;
        }

        [Test]
        public void EmptyFolder_HasNoMostRecentRoom()
        {
            var list = new List<RoomSummary>();
            RoomSummary summary;
            Assert.That(RoomIndex.TryMostRecent(_store, "", list, out summary), Is.False);
            RoomModel room;
            string error;
            Assert.That(RoomIndex.TryLoadMostRecent(_store, "anything", out room, out error), Is.False);
            Assert.That(room, Is.Null);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void SaveListAndMostRecent()
        {
            _store.SaveNow(Room("a", "2026-10-01T08:00:00Z"));
            _store.SaveNow(Room("b", "2026-10-03T08:00:00Z"));
            _store.SaveNow(Room("c", "2026-10-02T08:00:00Z"));

            var list = new List<RoomSummary>();
            Assert.That(_store.List(list), Is.EqualTo(3));
            Assert.That(list[0].Id, Is.EqualTo("b"));
            Assert.That(list[1].Id, Is.EqualTo("c"));
            Assert.That(list[2].Id, Is.EqualTo("a"));
            Assert.That(RoomIndex.Find(list, "c"), Is.EqualTo(1));
            Assert.That(RoomIndex.Find(list, "zz"), Is.EqualTo(-1));
            Assert.That(RoomIndex.Find(list, null), Is.EqualTo(-1));

            RoomSummary summary;
            Assert.That(RoomIndex.TryMostRecent(_store, "", list, out summary), Is.True);
            Assert.That(summary.Id, Is.EqualTo("b"), "with nothing remembered, the newest room");
            Assert.That(RoomIndex.TryMostRecent(_store, "a", list, out summary), Is.True);
            Assert.That(summary.Id, Is.EqualTo("a"), "the remembered room wins over a newer one");
            Assert.That(RoomIndex.TryMostRecent(_store, "deleted", list, out summary), Is.True);
            Assert.That(summary.Id, Is.EqualTo("b"), "a remembered room that is gone falls back to the newest");

            RoomModel room;
            string error;
            Assert.That(RoomIndex.TryLoadMostRecent(_store, "a", out room, out error), Is.True, error);
            Assert.That(room.Id, Is.EqualTo("a"));
            Assert.That(RoomIndex.TryLoadMostRecent(_store, null, out room, out error), Is.True, error);
            Assert.That(room.Id, Is.EqualTo("b"));
        }

        [Test]
        public void Delete_MovesMostRecentOn()
        {
            _store.SaveNow(Room("a", "2026-10-01T08:00:00Z"));
            _store.SaveNow(Room("b", "2026-10-03T08:00:00Z"));
            string error;
            Assert.That(RoomIndex.TryWriteWorldMap(_store, "b", new byte[] { 9, 8, 7 }, out error), Is.True, error);

            _store.Delete("b");
            Assert.That(RoomIndex.HasWorldMap(_store, "b"), Is.False, "the world map goes with its room");
            RoomModel room;
            Assert.That(RoomIndex.TryLoadMostRecent(_store, "b", out room, out error), Is.True, error);
            Assert.That(room.Id, Is.EqualTo("a"));

            _store.Delete("a");
            Assert.That(RoomIndex.TryLoadMostRecent(_store, "b", out room, out error), Is.False);
        }

        [Test]
        public void CorruptFiles_AreTolerated()
        {
            _store.SaveNow(Room("good", "2026-10-01T08:00:00Z"));
            File.WriteAllText(_store.JsonPath("newer_but_broken"), "{ \"schema\": ");
            File.WriteAllText(_store.JsonPath("empty"), "");
            File.WriteAllBytes(_store.JsonPath("binary"), new byte[] { 0, 255, 1, 254 });

            var list = new List<RoomSummary>();
            RoomSummary summary;
            Assert.That(RoomIndex.TryMostRecent(_store, "newer_but_broken", list, out summary), Is.True);
            Assert.That(summary.Id, Is.EqualTo("good"));
            Assert.That(list.Count, Is.EqualTo(1));

            RoomModel room;
            string error;
            Assert.That(RoomIndex.TryLoadMostRecent(_store, "newer_but_broken", out room, out error), Is.True, error);
            Assert.That(room.Id, Is.EqualTo("good"));

            // Only broken files: no room, and no exception.
            _store.Delete("good");
            Assert.That(RoomIndex.TryLoadMostRecent(_store, "empty", out room, out error), Is.False);
            Assert.That(room, Is.Null);
        }

        [Test]
        public void WorldMapBytes_RoundTrip()
        {
            var bytes = new byte[4096];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 31 + 7);
            string error;
            byte[] back;

            Assert.That(RoomIndex.HasWorldMap(_store, "r"), Is.False);
            Assert.That(RoomIndex.TryReadWorldMap(_store, "r", out back, out error), Is.False);
            Assert.That(back, Is.Null);
            Assert.That(error, Does.Contain("r"));

            // The folder does not exist yet: writing creates it.
            Assert.That(RoomIndex.TryWriteWorldMap(_store, "r", bytes, out error), Is.True, error);
            Assert.That(File.Exists(_store.WorldMapPath("r") + ".tmp"), Is.False);
            Assert.That(RoomIndex.HasWorldMap(_store, "r"), Is.True);
            Assert.That(RoomIndex.TryReadWorldMap(_store, "r", out back, out error), Is.True, error);
            Assert.That(back, Is.EqualTo(bytes));

            // A second save replaces the first.
            Assert.That(RoomIndex.TryWriteWorldMap(_store, "r", new byte[] { 1, 2 }, out error), Is.True, error);
            Assert.That(RoomIndex.TryReadWorldMap(_store, "r", out back, out error), Is.True, error);
            Assert.That(back, Is.EqualTo(new byte[] { 1, 2 }));

            _store.SaveNow(Room("r", "2026-10-01T08:00:00Z"));
            var list = new List<RoomSummary>();
            _store.List(list);
            Assert.That(list[0].HasWorldMap, Is.True);
        }

        [Test]
        public void WorldMap_EmptyOrMissingInput_IsRefused()
        {
            string error;
            byte[] back;
            Assert.That(RoomIndex.TryWriteWorldMap(_store, "r", null, out error), Is.False);
            Assert.That(RoomIndex.TryWriteWorldMap(_store, "r", new byte[0], out error), Is.False);
            Assert.That(RoomIndex.TryWriteWorldMap(_store, "", new byte[] { 1 }, out error), Is.False);
            Assert.That(Directory.Exists(_folder), Is.False, "a refused save writes nothing");

            Directory.CreateDirectory(_folder);
            File.WriteAllBytes(_store.WorldMapPath("hollow"), new byte[0]);
            Assert.That(RoomIndex.HasWorldMap(_store, "hollow"), Is.False);
            Assert.That(RoomIndex.TryReadWorldMap(_store, "hollow", out back, out error), Is.False);
            Assert.That(back, Is.Null);
            Assert.That(RoomIndex.TryReadWorldMap(_store, null, out back, out error), Is.False);
        }
    }
}
