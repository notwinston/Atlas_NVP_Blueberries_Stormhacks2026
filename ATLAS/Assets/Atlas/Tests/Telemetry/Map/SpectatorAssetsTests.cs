using System;
using System.Collections.Generic;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Telemetry.Tests
{
    /// <summary>
    /// The Spectator's furniture (lane D contract, section 5.8; Master Plan section 7.3.1): beside a phone that
    /// runs the real asset round trip, the Station shows the same generated and built-in meshes from the
    /// phone's relayed events and the Hub's job news; joined late, it takes them from the Forge's job list;
    /// and it never sends anything but GET.
    /// </summary>
    public class SpectatorAssetsTests
    {
        const double Step = 0.05;
        const int Table = 1, Chair = 2, Couch = 3, Plant = 4;

        /// <summary>The phone's event log as the Hub relays it: every asset event reaches the Station at once.</summary>
        sealed class Relay : IEventLog
        {
            public Action<EventMessage> To;
            public void Log(string ev) { }
            public void Log(string ev, string key, double value) { }
            public void Log(string ev, string key, string value) { }
            public void Log(string ev, string key1, double value1, string key2, double value2) { }
            public void LogJson(string ev, string dataJson)
            {
                if (To != null) To(new EventMessage(0, "S0", ev, AtlasJson.Parse(dataJson)));
            }
        }

        sealed class Rig
        {
            // The phone and the Station ask the same Forge; the Station's requests are kept apart to be counted.
            public readonly FakeForge Forge = new FakeForge();
            public readonly FakeForge Mirror = new FakeForge();
            public readonly FakeAssetRoom PhoneRoom = new FakeAssetRoom();
            public readonly FakeAssetRoom Room = new FakeAssetRoom();
            public readonly MemoryAssetCache Cache = new MemoryAssetCache();
            public readonly Relay Events = new Relay();
            public readonly AssetClientCore Phone;
            public readonly SpectatorAssetsCore Station;
            public double Time;

            public Rig(bool relay = true, string forgeUrl = FakeForge.Url)
            {
                FallbackSet fallback = AssetFixtures.LoadFallback();
                Phone = new AssetClientCore(Forge, PhoneRoom, new MemoryAssetCache(), new InlineAssetWorker(), fallback, Events)
                {
                    ForgeUrl = FakeForge.Url
                };
                Station = new SpectatorAssetsCore(Mirror, Room, Cache, new InlineAssetWorker(), fallback) { ForgeUrl = forgeUrl };
                if (relay) Events.To = Station.OnEvent;
                Phone.Tick(0.0, 16.7f);
                Station.Tick(0.0);
            }

            public void Run(double seconds)
            {
                double end = Time + seconds;
                while (Time < end - 1e-9)
                {
                    Time += Step;
                    Forge.Now = Time;
                    Phone.Tick(Time, 16.7f);
                    // What the phone's Forge holds is what the Station's requests find.
                    Mirror.Jobs.Clear();
                    Mirror.Jobs.AddRange(Forge.Jobs);
                    foreach (KeyValuePair<string, byte[]> asset in Forge.Assets) Mirror.Assets[asset.Key] = asset.Value;
                    Mirror.Now = Time;
                    Station.Tick(Time);
                }
            }

            public void PhoneScans(RoomModel room)
            {
                Phone.RoomBuilt(room);
                Phone.BeginRoom(room, "{\"version\":1,\"id\":\"" + room.Id + "\",\"objects\":[]}");
                foreach (RoomObject o in room.Objects) if (o.Id != Plant) Phone.Submit(Photos(o));
                Phone.EndRoom();
            }

            public void AssertOnlyGets()
            {
                foreach (string request in Mirror.Requests)
                    Assert.That(request, Does.StartWith("GET "), "the Station uploads nothing");
            }
        }

        static RoomObject Box(int id, ObjectCategory category, float w, float h, float d, float yaw = 0f)
        {
            return new RoomObject { Id = id, Category = category, Base = new float3(id, 0f, 0f), Size = new float3(w, h, d), Yaw = yaw };
        }

        /// <summary>Table, chair and couch with the boxes of the Forge's fixtures, and a plant nobody photographed.</summary>
        static RoomModel RoomOf(string id = "room_a")
        {
            var objects = new[]
            {
                Box(Table, ObjectCategory.Table, 1.2f, 0.75f, 0.8f),
                Box(Chair, ObjectCategory.Chair, 0.5f, 0.9f, 0.5f, 1.570796f),
                Box(Couch, ObjectCategory.Couch, 2.0f, 0.85f, 0.9f, -0.6f),
                Box(Plant, ObjectCategory.Plant, 0.4f, 1.1f, 0.4f),
            };
            ObjectMeshRules.Assign(objects);
            return new RoomModel { Id = id, Objects = objects };
        }

        static ObjectPhotoSet Photos(RoomObject o)
        {
            var set = new ObjectPhotoSet
            {
                ObjectId = o.Id, Category = o.Category, Size = o.Size, Yaw = o.Yaw, TriTarget = o.TriTarget, Views = new ObjectView[1]
            };
            set.Views[0] = new ObjectView
            {
                Jpeg = new byte[] { 0xFF, 0xD8, (byte)o.Id, 0x0D, 0x0A, 0xFF, 0xD9 }, Width = 256, Height = 256,
                Score = 0.75f, AzimuthDeg = 35f, Intrinsics = new float4(612f, 613f, 128f, 129f),
                CamToBox = float4x4.identity
            };
            return set;
        }

        static void AssertSameAsThePhone(Rig rig)
        {
            foreach (int id in new[] { Table, Chair, Couch, Plant })
            {
                Assert.That(rig.Room.ShownFor(id), Is.EqualTo(rig.PhoneRoom.ShownFor(id)), "object " + id);
                if (rig.PhoneRoom.ShownFor(id) == "generated")
                    Assert.That(rig.Room.Generated[id], Is.EqualTo(rig.PhoneRoom.Generated[id]), "the same GLB for object " + id);
            }
        }

        [Test]
        public void Spectator_shows_the_phones_furniture_as_it_arrives_and_sends_nothing()
        {
            var rig = new Rig();
            rig.Station.RoomBuilt(RoomOf());
            rig.PhoneScans(RoomOf());
            Assert.That(rig.Station.ObjectCount, Is.EqualTo(4));

            // The jobs are on their way: boxes on both sides, except the plant, which had no photo.
            rig.Run(3.0);
            Assert.That(rig.Forge.Jobs.Count, Is.EqualTo(3));
            AssertSameAsThePhone(rig);
            Assert.That(rig.Room.ShownFor(Plant), Is.EqualTo("builtin"));
            Assert.That(rig.Room.ShownFor(Table), Is.EqualTo("box"));

            // Two meshes arrive and one job fails: generated, generated, built-in.
            rig.Forge.Finish(Table, AssetFixtures.Glb("forge_table"));
            rig.Forge.Finish(Couch, AssetFixtures.Glb("forge_couch"));
            rig.Forge.Fail(Chair, "generator crashed");
            rig.Run(SpectatorAssetsCore.QuietSeconds + 6.0);
            Assert.That(rig.PhoneRoom.ShownFor(Table), Is.EqualTo("generated"));
            Assert.That(rig.PhoneRoom.ShownFor(Chair), Is.EqualTo("builtin"));
            AssertSameAsThePhone(rig);
            Assert.That(rig.Station.GeneratedCount, Is.EqualTo(2));
            Assert.That(rig.Station.BuiltInCount, Is.EqualTo(2));
            Assert.That(rig.Station.PendingCount, Is.EqualTo(0));
            Assert.That(rig.Station.Shows(Couch), Is.EqualTo(SpectatorObjectShows.Generated));
            Assert.That(rig.Cache.Count, Is.EqualTo(2), "the GLBs are kept for the next start");
            Assert.That(rig.Mirror.CountOf("GET /assets/"), Is.EqualTo(2));
            rig.AssertOnlyGets();
        }

        [Test]
        public void Spectator_starts_a_download_on_AssetReady_and_takes_a_failed_JobStatus_as_built_in()
        {
            var rig = new Rig(relay: false);
            rig.Station.RoomBuilt(RoomOf());
            byte[] glb = AssetFixtures.Glb("forge_table");
            string sha = GlbValidator.Sha256Hex(glb, glb.Length);
            rig.Forge.Assets[sha] = glb;

            rig.Station.OnJobStatus(new JobStatusMessage { JobId = "j1", RoomId = "room_a", BoxId = Table, State = "generating" });
            rig.Station.OnJobStatus(new JobStatusMessage { JobId = "j2", RoomId = "room_a", BoxId = Chair, State = "failed", Error = "x" });
            rig.Station.OnAssetReady(new AssetReadyMessage { JobId = "j9", RoomId = "another_room", BoxId = Couch, Sha256 = sha });
            rig.Run(1.0);
            Assert.That(rig.Room.ShownFor(Table), Is.EqualTo("box"), "a job on its way keeps the box");
            Assert.That(rig.Room.ShownFor(Chair), Is.EqualTo("builtin"));
            Assert.That(rig.Room.ShownFor(Couch), Is.EqualTo("box"), "news of another room is not this room's");

            rig.Station.OnAssetReady(new AssetReadyMessage { JobId = "j1", RoomId = "room_a", BoxId = Table, Sha256 = sha, Url = "/assets/" + sha + ".glb" });
            rig.Run(1.0);
            Assert.That(rig.Room.ShownFor(Table), Is.EqualTo("generated"));
            Assert.That(rig.Room.Generated[Table], Is.EqualTo(sha));
            rig.AssertOnlyGets();
        }

        [Test]
        public void Spectator_that_joins_late_takes_the_furniture_from_the_forges_job_list()
        {
            // The phone has finished before the Station is there: no event of it is ever relayed.
            var rig = new Rig(relay: false);
            rig.PhoneScans(RoomOf());
            rig.Run(3.0);
            rig.Forge.Finish(Table, AssetFixtures.Glb("forge_table"));
            rig.Forge.Fail(Chair, "generator crashed");
            rig.Run(5.0);
            Assert.That(rig.PhoneRoom.ShownFor(Table), Is.EqualTo("generated"));
            Assert.That(rig.Mirror.Requests, Is.Empty, "no room, nothing asked");

            rig.Station.RoomBuilt(RoomOf());
            rig.Run(2.0);
            Assert.That(rig.Mirror.CountOf("GET /jobs?room=room_a"), Is.EqualTo(1));
            Assert.That(rig.Room.ShownFor(Table), Is.EqualTo("generated"));
            Assert.That(rig.Room.ShownFor(Chair), Is.EqualTo("builtin"), "a failed job");
            Assert.That(rig.Room.ShownFor(Couch), Is.EqualTo("box"), "a job still on its way");
            Assert.That(rig.Room.ShownFor(Plant), Is.EqualTo("box"), "not decided before the room has been quiet");

            rig.Run(SpectatorAssetsCore.QuietSeconds);
            Assert.That(rig.Room.ShownFor(Plant), Is.EqualTo("builtin"), "no job: the phone shows its built-in mesh");
            Assert.That(rig.Room.ShownFor(Couch), Is.EqualTo("box"));

            // The last job ends while the Station's link is silent: the list is asked again.
            rig.Forge.Finish(Couch, AssetFixtures.Glb("forge_couch"));
            rig.Run(SpectatorAssetsCore.RepollSeconds + 2.0);
            AssertSameAsThePhone(rig);
            Assert.That(rig.Room.ShownFor(Couch), Is.EqualTo("generated"));
            int asked = rig.Mirror.Requests.Count;
            rig.Run(3 * SpectatorAssetsCore.RepollSeconds);
            Assert.That(rig.Mirror.Requests.Count, Is.EqualTo(asked), "nothing is on its way: the Forge is left alone");
            rig.AssertOnlyGets();
        }

        [Test]
        public void Spectator_with_no_forge_shows_the_built_in_set_and_what_its_cache_holds()
        {
            var rig = new Rig(relay: false, forgeUrl: string.Empty);
            byte[] glb = AssetFixtures.Glb("forge_couch");
            string sha = GlbValidator.Sha256Hex(glb, glb.Length);
            rig.Cache.Write(sha, glb);
            RoomModel room = RoomOf();
            room.Objects[2].AssetHash = sha;            // a saved room names the mesh it had

            rig.Station.RoomBuilt(room);
            rig.Run(1.0);
            Assert.That(rig.Room.ShownFor(Couch), Is.EqualTo("generated"));
            Assert.That(rig.Room.ShownFor(Table), Is.EqualTo("box"));
            rig.Run(SpectatorAssetsCore.QuietSeconds);
            foreach (int id in new[] { Table, Chair, Plant }) Assert.That(rig.Room.ShownFor(id), Is.EqualTo("builtin"), "object " + id);
            Assert.That(rig.Room.ShownFor(Couch), Is.EqualTo("generated"));
            Assert.That(rig.Mirror.Requests, Is.Empty);

            // The room content builds the same room again: everything is shown again, still with no request.
            rig.Room.Shown.Clear();
            rig.Station.RoomBuilt(room);
            rig.Run(1.0);
            Assert.That(rig.Room.ShownFor(Couch), Is.EqualTo("generated"));
            Assert.That(rig.Room.ShownFor(Chair), Is.EqualTo("builtin"), "what was known of the room is kept");
            Assert.That(rig.Mirror.Requests, Is.Empty);
        }

        [Test]
        public void Spectator_rejects_a_file_that_is_not_the_named_asset_and_follows_the_phone_to_the_built_in_mesh()
        {
            var rig = new Rig(relay: false);
            rig.Station.RoomBuilt(RoomOf());
            byte[] glb = AssetFixtures.Glb("forge_table");
            string claimed = new string('a', 64);
            rig.Forge.Assets[claimed] = glb;            // the bytes do not hash to the name

            rig.Station.OnAssetReady(new AssetReadyMessage { JobId = "j1", RoomId = "room_a", BoxId = Table, Sha256 = claimed });
            rig.Run(SpectatorAssetsCore.RetryLaterSeconds + 5.0);
            Assert.That(rig.Room.ShownFor(Table), Is.EqualTo("box"));
            Assert.That(rig.Mirror.CountOf("GET /assets/"), Is.EqualTo(1), "a rejected file is not fetched again");
            Assert.That(rig.Cache.Count, Is.EqualTo(0));

            var builtIn = new EventMessage(0, "S0", AssetClientCore.EventName,
                JsonValue.Object().Set("step", "builtin").Set("boxId", Table).Set("reason", "rejected").Set("placed", true));
            rig.Station.OnEvent(builtIn);
            rig.Run(0.5);
            Assert.That(rig.Room.ShownFor(Table), Is.EqualTo("builtin"));

            // A mesh the Forge does not have is asked for again later, not for ever in a row.
            string missing = new string('b', 64);
            rig.Station.OnAssetReady(new AssetReadyMessage { JobId = "j2", RoomId = "room_a", BoxId = Couch, Sha256 = missing });
            rig.Run(10.0);
            Assert.That(rig.Mirror.CountOf("GET /assets/" + missing), Is.EqualTo(SpectatorAssetsCore.MaxAttempts));
            Assert.That(rig.Room.ShownFor(Couch), Is.EqualTo("builtin"), "it keeps what it showed");
            rig.Run(SpectatorAssetsCore.RetryLaterSeconds);
            Assert.That(rig.Mirror.CountOf("GET /assets/" + missing), Is.GreaterThan(SpectatorAssetsCore.MaxAttempts));
            rig.AssertOnlyGets();
        }
    }
}
