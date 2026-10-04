using System.Collections.Generic;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Telemetry.Tests
{
    /// <summary>
    /// The asset round trip (docs/contracts/lane-d-link.md section 4.3; Master Plan section 7.4 and the tests
    /// L17 and L19 of section 11.2) with a Forge in memory: the state of every object and of the room, the
    /// order and the content of what is sent, and what the room is asked to show.
    /// </summary>
    public class AssetClientCoreTests
    {
        const float FrameMs = 16.7f;
        const double Step = 0.05;

        // The three boxes the Forge's fixtures were made for.
        const int Table = 1, Chair = 2, Couch = 3;

        sealed class Rig
        {
            public readonly FakeForge Forge = new FakeForge();
            public readonly FakeAssetRoom Room = new FakeAssetRoom();
            public readonly MemoryAssetCache Cache = new MemoryAssetCache();
            public readonly RecordingEvents Events = new RecordingEvents();
            public readonly List<AssetAckMessage> Acks = new List<AssetAckMessage>();
            public readonly AssetClientCore Core;
            public double Time;

            public Rig()
            {
                Core = new AssetClientCore(Forge, Room, Cache, new InlineAssetWorker(), AssetFixtures.LoadFallback(), Events)
                {
                    ForgeUrl = FakeForge.Url
                };
                Core.AckSink = Acks.Add;
                Core.Tick(0.0, FrameMs);
            }

            public void Run(double seconds, float frameMs = FrameMs)
            {
                double end = Time + seconds;
                while (Time < end - 1e-9)
                {
                    Time += Step;
                    Forge.Now = Time;
                    Core.Tick(Time, frameMs);
                }
            }

            /// <summary>A fresh scan: the room is built, announced, and every object's photos are handed in.</summary>
            public void Scan(RoomModel room, bool submit = true)
            {
                Core.RoomBuilt(room);
                Core.BeginRoom(room, "{\"version\":1,\"id\":\"" + room.Id + "\",\"objects\":[]}");
                if (!submit) return;
                foreach (RoomObject o in LargestFirst(room)) if (o.TriTarget > 0) Core.Submit(Photos(o));
                Core.EndRoom();
            }
        }

        static RoomObject Box(int id, ObjectCategory category, float w, float h, float d, float yaw = 0f)
        {
            return new RoomObject { Id = id, Category = category, Base = new float3(id, 0f, 0f), Size = new float3(w, h, d), Yaw = yaw };
        }

        static RoomModel RoomOf(string id, params RoomObject[] objects)
        {
            ObjectMeshRules.Assign(objects);
            return new RoomModel { Id = id, Objects = objects };
        }

        /// <summary>Table, chair and couch with the boxes of forge_table, forge_chair and forge_couch.</summary>
        static RoomModel FixtureRoom(string id = "room_a")
        {
            return RoomOf(id,
                Box(Table, ObjectCategory.Table, 1.2f, 0.75f, 0.8f),
                Box(Chair, ObjectCategory.Chair, 0.5f, 0.9f, 0.5f, 1.570796f),
                Box(Couch, ObjectCategory.Couch, 2.0f, 0.85f, 0.9f, -0.6f));
        }

        static List<RoomObject> LargestFirst(RoomModel room)
        {
            var list = new List<RoomObject>(room.Objects);
            list.Sort((a, b) => b.Volume != a.Volume ? b.Volume.CompareTo(a.Volume) : a.Id.CompareTo(b.Id));
            return list;
        }

        static ObjectPhotoSet Photos(RoomObject o, int views = 1)
        {
            var set = new ObjectPhotoSet
            {
                ObjectId = o.Id, Category = o.Category, Size = o.Size, Yaw = o.Yaw, TriTarget = o.TriTarget, Views = new ObjectView[views]
            };
            for (int i = 0; i < views; i++)
            {
                set.Views[i] = new ObjectView
                {
                    Jpeg = new byte[] { 0xFF, 0xD8, (byte)o.Id, (byte)i, 0x0D, 0x0A, 0xFF, 0xD9 }, Width = 256, Height = 256,
                    Score = 0.75f, AzimuthDeg = 35f, Intrinsics = new float4(612f, 613f, 128f, 129f),
                    CamToBox = new float4x4(1f, 0f, 0f, 0.1f, 0f, 1f, 0f, 0.4f, 0f, 0f, 1f, -1.5f, 0f, 0f, 0f, 1f)
                };
            }
            return set;
        }

        static void FinishAll(Rig rig)
        {
            rig.Forge.Finish(Table, AssetFixtures.Glb("forge_table"));
            rig.Forge.Finish(Chair, AssetFixtures.Glb("forge_chair"));
            rig.Forge.Finish(Couch, AssetFixtures.Glb("forge_couch"));
        }

        // ---- Happy path ----------------------------------------------------------------------------------------

        [Test]
        public void Asset_HappyPath_EveryObjectGetsItsGeneratedMesh()
        {
            var rig = new Rig();
            rig.Scan(FixtureRoom());
            Assert.That(rig.Core.ObjectCount, Is.EqualTo(3));
            Assert.That(rig.Core.PendingCount, Is.EqualTo(3));
            Assert.That(rig.Core.GetState(Couch), Is.EqualTo(ObjectAssetState.Uploading));

            rig.Run(2.0);
            Assert.That(rig.Core.ForgeReachable, Is.True);
            Assert.That(rig.Forge.Rooms, Is.EqualTo(new[] { "room_a" }));
            Assert.That(rig.Forge.Jobs.Count, Is.EqualTo(3));
            foreach (int id in new[] { Table, Chair, Couch })
            {
                Assert.That(rig.Core.GetState(id), Is.EqualTo(ObjectAssetState.Requested));
                Assert.That(rig.Room.Requested.Contains(id), Is.True);
                Assert.That(rig.Room.ShownFor(id), Is.EqualTo("box"));
            }

            FinishAll(rig);
            rig.Run(4.0);
            foreach (int id in new[] { Table, Chair, Couch })
            {
                Assert.That(rig.Core.GetState(id), Is.EqualTo(ObjectAssetState.Ready));
                Assert.That(rig.Room.ShownFor(id), Is.EqualTo("generated"));
                Assert.That(rig.Room.Generated[id], Is.EqualTo(rig.Forge.JobOf(id).Sha));
                Assert.That(rig.Room.GeneratedTriangles[id], Is.InRange(GlbValidator.MinTriangles, 5000));
                Assert.That(rig.Cache.Contains(rig.Forge.JobOf(id).Sha), Is.True, "the GLB is kept for the next start");
            }
            Assert.That(rig.Core.PlacedCount, Is.EqualTo(3));
            Assert.That(rig.Core.PendingCount, Is.EqualTo(0));
            Assert.That(rig.Core.BuiltInCount, Is.EqualTo(0));
            Assert.That(rig.Room.BuiltInTriangles, Is.Empty);

            // One AssetAck per mesh, half a second after it was handed over, with the worst frame of that window.
            Assert.That(rig.Acks.Count, Is.EqualTo(3));
            foreach (AssetAckMessage ack in rig.Acks)
            {
                Assert.That(ack.Ok, Is.True);
                Assert.That(ack.Reason, Is.Empty);
                Assert.That(ack.JobId, Is.EqualTo(rig.Forge.JobOf(ack.BoxId).JobId));
                Assert.That(ack.Sha256, Is.EqualTo(rig.Forge.JobOf(ack.BoxId).Sha));
                Assert.That(ack.WorstFrameMs, Is.EqualTo(FrameMs));
                Assert.That(ack.LoadMs, Is.GreaterThanOrEqualTo(0f));
            }

            // Every step left its event.
            foreach (int id in new[] { Table, Chair, Couch })
                foreach (string step in new[] { "upload", "requested", "ready", "placed" })
                    Assert.That(rig.Events.CountOf(step, id), Is.EqualTo(1), step + " " + id);
            Assert.That(rig.Events.CountOf("builtin"), Is.EqualTo(0));
            Assert.That(rig.Events.CountOf("failed") + rig.Events.CountOf("rejected"), Is.EqualTo(0));
        }

        [Test]
        public void Asset_Upload_CarriesTheContractsMetaAndTheCrops()
        {
            var rig = new Rig();
            RoomModel room = FixtureRoom();
            rig.Core.RoomBuilt(room);
            rig.Core.BeginRoom(room, "{\"version\":1,\"id\":\"room_a\",\"objects\":[]}");
            RoomObject couch = room.Objects[2];
            rig.Core.Submit(Photos(couch, 3));
            rig.Run(1.0);

            FakeForge.Job job = rig.Forge.JobOf(Couch);
            Assert.That(job, Is.Not.Null);
            Assert.That(job.Views, Is.EqualTo(3), "view0, view1 and view2 as image/jpeg files");
            JsonValue meta = job.Meta;
            Assert.That(meta.GetInt("boxId"), Is.EqualTo(Couch));
            Assert.That(meta.GetString("category"), Is.EqualTo("couch"));
            Assert.That(meta["dims"][0].AsFloat(), Is.EqualTo(2.0f));
            Assert.That(meta["dims"][1].AsFloat(), Is.EqualTo(0.85f));
            Assert.That(meta["dims"][2].AsFloat(), Is.EqualTo(0.9f));
            Assert.That(meta.GetFloat("yaw"), Is.EqualTo(-0.6f));
            Assert.That(meta.GetInt("triTarget"), Is.EqualTo(5000));
            Assert.That(meta.GetString("backend"), Is.EqualTo("local"));
            Assert.That(meta["views"].Count, Is.EqualTo(3));
            JsonValue view = meta["views"][0];
            Assert.That(view.GetFloat("score"), Is.EqualTo(0.75f));
            Assert.That(view.GetFloat("azimuthDeg"), Is.EqualTo(35f));
            Assert.That(view["intrinsics"][1].AsFloat(), Is.EqualTo(613f));
            Assert.That(view["intrinsics"][3].AsFloat(), Is.EqualTo(129f));
            // Row by row: the translation is the last entry of each of the first three rows.
            Assert.That(view["camToBox"].Count, Is.EqualTo(16));
            Assert.That(view["camToBox"][3].AsFloat(), Is.EqualTo(0.1f));
            Assert.That(view["camToBox"][7].AsFloat(), Is.EqualTo(0.4f));
            Assert.That(view["camToBox"][11].AsFloat(), Is.EqualTo(-1.5f));
            Assert.That(view["camToBox"][15].AsFloat(), Is.EqualTo(1f));

            // Reachability is asked before each upload attempt.
            Assert.That(rig.Forge.Requests[0], Is.EqualTo("GET /health"));
            Assert.That(rig.Forge.Requests[1], Is.EqualTo("POST /rooms"));
            Assert.That(rig.Forge.Requests[2], Is.EqualTo("GET /health"));
            Assert.That(rig.Forge.Requests[3], Is.EqualTo("POST /rooms/room_a/objects"));
        }

        [Test]
        public void Asset_BackendParameter_IsSentWithEveryJob()
        {
            var rig = new Rig();
            rig.Core.Backend = AssetClientCore.BackendCache;
            rig.Scan(FixtureRoom());
            rig.Run(2.0);
            foreach (FakeForge.Job job in rig.Forge.Jobs) Assert.That(job.Meta.GetString("backend"), Is.EqualTo("cache"));
        }

        // ---- The budget: twenty objects, largest first, triangle targets ---------------------------------------

        [Test]
        public void Asset_Budget_AtMostTwentyObjectsLargestFirst()
        {
            var objects = new RoomObject[25];
            // Volumes in a scrambled order, so that the order sent is the client's and not the array's.
            for (int i = 0; i < 25; i++)
            {
                float side = 0.3f + 0.02f * ((i * 7) % 25);
                objects[i] = Box(i + 1, ObjectCategory.Storage, side, side, side);
            }
            RoomModel room = RoomOf("room_big", objects);
            // Lane C picks twenty; a RoomModel that names more is still held to twenty.
            foreach (RoomObject o in room.Objects) o.TriTarget = 3000;

            var rig = new Rig();
            rig.Core.RoomBuilt(room);
            rig.Core.BeginRoom(room, "{\"version\":1,\"id\":\"room_big\",\"objects\":[]}");
            // Photos arrive in the array's order; the client still serves the largest box first.
            foreach (RoomObject o in room.Objects) rig.Core.Submit(Photos(o));
            rig.Core.EndRoom();
            Assert.That(rig.Core.ObjectCount, Is.EqualTo(20));
            rig.Run(8.0);

            List<RoomObject> order = LargestFirst(room);
            Assert.That(rig.Forge.Jobs.Count, Is.EqualTo(20));
            for (int k = 0; k < 20; k++)
            {
                Assert.That(rig.Forge.Jobs[k].BoxId, Is.EqualTo(order[k].Id), "upload " + k);
                Assert.That(rig.Core.ObjectIdAt(k), Is.EqualTo(order[k].Id));
                Assert.That(rig.Forge.Jobs[k].TriTarget, Is.EqualTo(3000));
                Assert.That(rig.Core.TriTarget(order[k].Id), Is.EqualTo(3000));
            }
            for (int k = 20; k < 25; k++)
            {
                Assert.That(rig.Core.GetState(order[k].Id), Is.EqualTo(ObjectAssetState.Placeholder));
                Assert.That(rig.Core.TriTarget(order[k].Id), Is.EqualTo(0));
                Assert.That(rig.Room.ShownFor(order[k].Id), Is.EqualTo("box"));
            }
        }

        [TestCase(1, 5000)]
        [TestCase(12, 5000)]
        [TestCase(13, 4615)]
        [TestCase(15, 4000)]
        [TestCase(20, 3000)]
        public void Asset_Budget_TriangleTargetIsSixtyThousandOverN(int n, int target)
        {
            var objects = new RoomObject[n];
            for (int i = 0; i < n; i++) objects[i] = Box(i + 1, ObjectCategory.Chair, 0.5f, 0.9f, 0.5f);
            var rig = new Rig();
            rig.Scan(RoomOf("room_n", objects));
            rig.Run(0.5 * n + 2.0);
            Assert.That(rig.Forge.Jobs.Count, Is.EqualTo(n));
            foreach (FakeForge.Job job in rig.Forge.Jobs) Assert.That(job.TriTarget, Is.EqualTo(target));
            Assert.That(n * target, Is.LessThanOrEqualTo(60000));
        }

        [Test]
        public void Asset_ObjectsWithoutATriangleTarget_AreLeftAlone()
        {
            RoomModel room = FixtureRoom();
            room.Objects[1].TriTarget = 0;                     // not picked by lane C
            room.Objects[2].Mesh = MeshState.StandIn;          // budget_room's stand-in meshes
            var rig = new Rig();
            rig.Scan(room);
            rig.Run(2.0);
            Assert.That(rig.Core.ObjectCount, Is.EqualTo(1));
            Assert.That(rig.Forge.Jobs.Count, Is.EqualTo(1));
            Assert.That(rig.Forge.Jobs[0].BoxId, Is.EqualTo(Table));
        }

        // ---- The Forge is away (test L19) ----------------------------------------------------------------------

        [Test]
        public void Asset_ForgeUnreachable_EveryBoxHoldsItsBuiltInMeshWithinTenSeconds()
        {
            var rig = new Rig();
            rig.Forge.Up = false;
            rig.Scan(FixtureRoom());
            rig.Run(8.9);
            Assert.That(rig.Core.BuiltInCount, Is.EqualTo(0), "three attempts are made first: 2 + 1 + 2 + 2 + 2 s");
            rig.Run(1.1);                                       // 10 s after entering Home

            Assert.That(rig.Core.ForgeReachable, Is.False);
            Assert.That(rig.Core.BuiltInCount, Is.EqualTo(3));
            foreach (int id in new[] { Table, Chair, Couch })
            {
                Assert.That(rig.Core.GetState(id), Is.EqualTo(ObjectAssetState.BuiltIn));
                Assert.That(rig.Core.ShowsBuiltIn(id), Is.True);
                Assert.That(rig.Room.ShownFor(id), Is.EqualTo("builtin"));
                Assert.That(rig.Room.BuiltInTriangles[id], Is.InRange(1, 1500));
                Assert.That(rig.Events.Last("builtin", id).GetString("reason"), Is.EqualTo("unreachable"));
                Assert.That(rig.Events.Last("builtin", id).GetBool("placed"), Is.True);
            }
            // Exactly three attempts, each one reachability check of 2 s; nothing else was sent.
            Assert.That(rig.Forge.CountOf("GET /health"), Is.EqualTo(3));
            Assert.That(rig.Forge.CountOf("POST"), Is.EqualTo(0));
            Assert.That(rig.Acks, Is.Empty);

            // After that the Forge is asked every 4 s.
            rig.Run(8.0);
            Assert.That(rig.Forge.CountOf("GET /health"), Is.InRange(4, 5));
        }

        [Test]
        public void Asset_ForgeKnownToBeAway_TheNextRoomTakesBuiltInMeshesAtOnce()
        {
            var rig = new Rig();
            rig.Forge.Up = false;
            rig.Scan(FixtureRoom("room_a"));
            rig.Run(10.0);
            rig.Scan(FixtureRoom("room_b"));
            rig.Run(0.2);
            Assert.That(rig.Core.RoomId, Is.EqualTo("room_b"));
            Assert.That(rig.Core.BuiltInCount, Is.EqualTo(3));
        }

        [Test]
        public void Asset_LateForgeReturn_ReplacesEveryBuiltInMesh()
        {
            var rig = new Rig();
            rig.Forge.Up = false;
            rig.Scan(FixtureRoom());
            rig.Run(10.0);
            Assert.That(rig.Core.BuiltInCount, Is.EqualTo(3));

            // The Station is switched on. The client notices at its next health check and sends what it kept.
            rig.Forge.Up = true;
            rig.Run(8.0);
            Assert.That(rig.Core.ForgeReachable, Is.True);
            Assert.That(rig.Forge.Rooms, Is.EqualTo(new[] { "room_a" }));
            Assert.That(rig.Forge.Jobs.Count, Is.EqualTo(3));
            Assert.That(rig.Forge.Jobs[0].BoxId, Is.EqualTo(Couch), "largest first");
            Assert.That(rig.Core.PendingCount, Is.EqualTo(3));
            Assert.That(rig.Core.BuiltInCount, Is.EqualTo(3), "the built-in meshes stand until the generated ones arrive");

            FinishAll(rig);
            rig.Run(4.0);
            foreach (int id in new[] { Table, Chair, Couch })
            {
                Assert.That(rig.Core.GetState(id), Is.EqualTo(ObjectAssetState.Ready));
                Assert.That(rig.Room.ShownFor(id), Is.EqualTo("generated"));
                Assert.That(rig.Room.Calls.IndexOf("builtin:" + id), Is.LessThan(rig.Room.Calls.IndexOf("generated:" + id)));
            }
            Assert.That(rig.Core.BuiltInCount, Is.EqualTo(0));
            Assert.That(rig.Core.PlacedCount, Is.EqualTo(3));
            Assert.That(rig.Acks.Count, Is.EqualTo(3));
        }

        [Test]
        public void Asset_ForgeLostWhileJobsRun_FallsBackAndResumes()
        {
            var rig = new Rig();
            rig.Scan(FixtureRoom());
            rig.Run(2.0);
            Assert.That(rig.Core.GetState(Table), Is.EqualTo(ObjectAssetState.Requested));

            // Nobody says anything about the jobs for 30 s.
            rig.Forge.Up = false;
            rig.Run(36.0);
            Assert.That(rig.Core.ForgeReachable, Is.False);
            Assert.That(rig.Core.BuiltInCount, Is.EqualTo(3));

            rig.Forge.Up = true;
            FinishAll(rig);
            rig.Run(10.0);
            Assert.That(rig.Core.PlacedCount, Is.EqualTo(3));
            Assert.That(rig.Core.BuiltInCount, Is.EqualTo(0));
            Assert.That(rig.Forge.Jobs.Count, Is.EqualTo(3), "the crops were released after the first upload: no job is sent twice");
        }

        // ---- A job fails, an asset is rejected -----------------------------------------------------------------

        [Test]
        public void Asset_JobFailed_TheObjectTakesItsBuiltInMeshAndKeepsIt()
        {
            var rig = new Rig();
            rig.Scan(FixtureRoom());
            rig.Run(2.0);
            rig.Forge.Fail(Chair, "generate_failed");
            rig.Forge.Finish(Table, AssetFixtures.Glb("forge_table"));
            rig.Forge.Finish(Couch, AssetFixtures.Glb("forge_couch"));
            rig.Run(4.0);

            Assert.That(rig.Core.GetState(Chair), Is.EqualTo(ObjectAssetState.BuiltIn));
            Assert.That(rig.Room.ShownFor(Chair), Is.EqualTo("builtin"));
            Assert.That(rig.Events.Last("failed", Chair).GetString("reason"), Is.EqualTo("generate_failed"));
            Assert.That(rig.Events.Last("builtin", Chair).GetString("reason"), Is.EqualTo("job_failed"));
            Assert.That(rig.Core.GetState(Table), Is.EqualTo(ObjectAssetState.Ready));
            Assert.That(rig.Core.GetState(Couch), Is.EqualTo(ObjectAssetState.Ready));
            Assert.That(rig.Core.PlacedCount, Is.EqualTo(2));

            // A failed job is not retried, even when the object is submitted again.
            int requests = rig.Forge.CountOf("POST");
            rig.Core.Submit(Photos(FixtureRoom().Objects[1]));
            rig.Run(10.0);
            Assert.That(rig.Forge.CountOf("POST"), Is.EqualTo(requests));
            Assert.That(rig.Core.GetState(Chair), Is.EqualTo(ObjectAssetState.BuiltIn));
        }

        [Test]
        public void Asset_JobFailedOverTheLink_NeedsNoPoll()
        {
            var rig = new Rig();
            rig.Scan(FixtureRoom());
            rig.Run(1.0);
            Assert.That(rig.Core.GetState(Couch), Is.EqualTo(ObjectAssetState.Requested));
            rig.Core.OnJobStatus(new JobStatusMessage
            {
                JobId = rig.Forge.JobOf(Couch).JobId, RoomId = "room_a", BoxId = Couch, State = "failed", Error = "clean_failed"
            });
            Assert.That(rig.Core.GetState(Couch), Is.EqualTo(ObjectAssetState.BuiltIn));
            // News about another room is not this room's.
            rig.Core.OnJobStatus(new JobStatusMessage { JobId = "x", RoomId = "room_z", BoxId = Table, State = "failed" });
            Assert.That(rig.Core.GetState(Table), Is.Not.EqualTo(ObjectAssetState.BuiltIn));
        }

        [Test]
        public void Asset_ReadyOverTheLink_StartsTheDownload()
        {
            var rig = new Rig();
            rig.Scan(FixtureRoom());
            rig.Run(1.0);
            string sha = rig.Forge.Finish(Couch, AssetFixtures.Glb("forge_couch"));
            rig.Core.OnAssetReady(new AssetReadyMessage
            {
                JobId = rig.Forge.JobOf(Couch).JobId, RoomId = "room_a", BoxId = Couch, Sha256 = sha, Url = "/assets/" + sha + ".glb"
            });
            Assert.That(rig.Core.GetState(Couch), Is.EqualTo(ObjectAssetState.Downloading));
            rig.Run(0.3);
            Assert.That(rig.Core.GetState(Couch), Is.EqualTo(ObjectAssetState.Ready));
        }

        [TestCase("bad_long_edge", "validate:edge")]
        [TestCase("bad_texture", "validate:no_material")]
        [TestCase("bad_node_transform", "validate:structure")]
        [TestCase("bad_off_the_floor", "validate:origin")]
        public void Asset_RejectedByTheValidator_TheObjectTakesItsBuiltInMesh(string fixture, string reason)
        {
            var rig = new Rig();
            rig.Scan(FixtureRoom());
            rig.Run(2.0);
            string sha = rig.Forge.Finish(Table, AssetFixtures.Glb(fixture));
            rig.Run(3.0);

            Assert.That(rig.Core.GetState(Table), Is.EqualTo(ObjectAssetState.BuiltIn));
            Assert.That(rig.Room.ShownFor(Table), Is.EqualTo("builtin"));
            Assert.That(rig.Room.Generated.ContainsKey(Table), Is.False, "a rejected asset never reaches the room");
            Assert.That(rig.Cache.Contains(sha), Is.False, "and is not kept");
            Assert.That(rig.Acks.Count, Is.EqualTo(1));
            Assert.That(rig.Acks[0].Ok, Is.False);
            Assert.That(rig.Acks[0].BoxId, Is.EqualTo(Table));
            Assert.That(rig.Acks[0].Sha256, Is.EqualTo(sha));
            Assert.That(rig.Acks[0].Reason, Is.EqualTo(reason));
            Assert.That(rig.Events.Last("rejected", Table).GetString("reason"), Is.EqualTo(reason));
        }

        [Test]
        public void Asset_TooManyTriangles_IsRejected()
        {
            var rig = new Rig();
            rig.Scan(FixtureRoom());
            rig.Run(2.0);
            rig.Forge.Finish(Chair, AssetFixtures.Glb("bad_too_many_triangles"));
            rig.Run(3.0);
            Assert.That(rig.Core.GetState(Chair), Is.EqualTo(ObjectAssetState.BuiltIn));
            Assert.That(rig.Acks[0].Reason, Is.EqualTo("validate:tris_max"));
        }

        [Test]
        public void Asset_AnotherFileThanAnnounced_IsRejectedByItsHash()
        {
            var rig = new Rig();
            rig.Scan(FixtureRoom());
            rig.Run(2.0);
            // The Forge announces one hash and serves other bytes under it.
            rig.Forge.Finish(Table, AssetFixtures.Glb("forge_table"), new string('a', 64));
            rig.Run(3.0);
            Assert.That(rig.Core.GetState(Table), Is.EqualTo(ObjectAssetState.BuiltIn));
            Assert.That(rig.Acks[0].Reason, Is.EqualTo("hash_mismatch"));
            Assert.That(rig.Cache.Count, Is.EqualTo(0));
        }

        [Test]
        public void Asset_DownloadFails_ThreeTriesThenBuiltIn()
        {
            var rig = new Rig();
            rig.Scan(FixtureRoom());
            rig.Run(2.0);
            string sha = rig.Forge.Finish(Table, AssetFixtures.Glb("forge_table"));
            rig.Forge.Assets.Remove(sha);                       // the job says ready, the file is gone
            rig.Run(6.0);
            Assert.That(rig.Forge.CountOf("GET /assets/" + sha), Is.EqualTo(3));
            Assert.That(rig.Core.GetState(Table), Is.EqualTo(ObjectAssetState.BuiltIn));
            Assert.That(rig.Acks[0].Reason, Is.EqualTo("download_failed"));
        }

        // ---- No photo, no template, no scan --------------------------------------------------------------------

        [Test]
        public void Asset_NoPhoto_BuiltInAtOnce()
        {
            var rig = new Rig();
            RoomModel room = FixtureRoom();
            rig.Core.RoomBuilt(room);
            rig.Core.BeginRoom(room, "{\"version\":1,\"id\":\"room_a\",\"objects\":[]}");
            rig.Core.ReportNoPhoto(Chair);
            rig.Core.Submit(Photos(room.Objects[0]));
            rig.Core.EndRoom();                                 // the couch was never submitted: it has no photo either
            rig.Run(0.2);

            Assert.That(rig.Room.ShownFor(Chair), Is.EqualTo("builtin"));
            Assert.That(rig.Room.ShownFor(Couch), Is.EqualTo("builtin"));
            Assert.That(rig.Events.Last("builtin", Chair).GetString("reason"), Is.EqualTo("no_photo"));
            rig.Run(2.0);
            Assert.That(rig.Forge.Jobs.Count, Is.EqualTo(1));
            Assert.That(rig.Core.GetState(Table), Is.EqualTo(ObjectAssetState.Requested));
        }

        [Test]
        public void Asset_UnknownCategory_KeepsItsPlaceholderBox()
        {
            RoomModel room = RoomOf("room_u",
                Box(1, ObjectCategory.Unknown, 0.5f, 0.5f, 0.5f), Box(2, ObjectCategory.Other, 0.4f, 0.4f, 0.4f),
                Box(3, ObjectCategory.Storage, 6f, 3f, 6f), Box(4, ObjectCategory.Lamp, 0.3f, 1.5f, 0.3f));
            var rig = new Rig();
            rig.Forge.Up = false;
            rig.Scan(room);
            rig.Run(10.0);

            Assert.That(rig.Room.ShownFor(1), Is.EqualTo("box"));
            Assert.That(rig.Room.ShownFor(2), Is.EqualTo("box"));
            Assert.That(rig.Room.ShownFor(3), Is.EqualTo("box"), "a box too large for 1,500 triangles keeps its placeholder");
            Assert.That(rig.Room.ShownFor(4), Is.EqualTo("builtin"));
            Assert.That(rig.Events.Last("builtin", 1).GetString("kept"), Is.EqualTo("no_template"));
            Assert.That(rig.Events.Last("builtin", 3).GetString("kept"), Is.EqualTo("too_large"));
            Assert.That(rig.Core.BuiltInCount, Is.EqualTo(1));
            Assert.That(rig.Core.GetState(1), Is.EqualTo(ObjectAssetState.BuiltIn));
        }

        [Test]
        public void Asset_RoomFromAFile_TakesBuiltInMeshesAfterTheGrace()
        {
            var rig = new Rig();
            rig.Forge.Latency = 0.0;
            rig.Core.RoomBuilt(FixtureRoom());                  // a fixture or a saved room: no scan announces itself
            rig.Run(1.9);
            Assert.That(rig.Core.BuiltInCount, Is.EqualTo(0));
            rig.Run(0.3);
            Assert.That(rig.Core.BuiltInCount, Is.EqualTo(3));
            Assert.That(rig.Events.Last("builtin", Couch).GetString("reason"), Is.EqualTo("no_scan"));
            Assert.That(rig.Forge.CountOf("POST"), Is.EqualTo(0));
        }

        [Test]
        public void Asset_RoomFromAFile_WithNoForge_DoesNotWaitForTheGrace()
        {
            var rig = new Rig();
            rig.Forge.Up = false;
            rig.Core.RoomBuilt(FixtureRoom());
            rig.Run(2.3);
            Assert.That(rig.Core.BuiltInCount, Is.EqualTo(3));
        }

        [Test]
        public void Asset_SavedRoom_PlacesCachedMeshesWithNoStation()
        {
            // First start: the meshes arrive and are cached.
            var first = new Rig();
            first.Scan(FixtureRoom());
            first.Run(2.0);
            FinishAll(first);
            first.Run(4.0);
            Assert.That(first.Cache.Count, Is.EqualTo(3));

            // Next start: the saved RoomModel names the hashes, and the Station is off.
            RoomModel saved = FixtureRoom();
            foreach (RoomObject o in saved.Objects)
            {
                o.Mesh = MeshState.Ready;
                o.AssetHash = first.Room.Generated[o.Id];
            }
            var forge = new FakeForge { Up = false };
            var room = new FakeAssetRoom();
            var acks = new List<AssetAckMessage>();
            var core = new AssetClientCore(forge, room, first.Cache, new InlineAssetWorker(), AssetFixtures.LoadFallback(), null)
            {
                ForgeUrl = FakeForge.Url, AckSink = acks.Add
            };
            core.Tick(0.0, FrameMs);
            core.RoomBuilt(saved);
            for (double t = Step; t < 1.0; t += Step) { forge.Now = t; core.Tick(t, FrameMs); }

            Assert.That(core.PlacedCount, Is.EqualTo(3));
            foreach (RoomObject o in saved.Objects) Assert.That(room.Generated[o.Id], Is.EqualTo(o.AssetHash));
            Assert.That(forge.CountOf("GET /assets"), Is.EqualTo(0));
            for (double t = 1.0; t < 2.0; t += Step) { forge.Now = t; core.Tick(t, FrameMs); }
            Assert.That(acks, Is.Empty, "a mesh from the cache answers no job");
        }

        [Test]
        public void Asset_SavedRoom_WithAnEmptyCacheAndNoForge_FallsBack()
        {
            RoomModel saved = FixtureRoom();
            saved.Objects[0].Mesh = MeshState.Ready;
            saved.Objects[0].AssetHash = new string('b', 64);
            var rig = new Rig();
            rig.Forge.Up = false;
            rig.Core.RoomBuilt(saved);
            rig.Run(3.0);
            Assert.That(rig.Core.BuiltInCount, Is.EqualTo(3));
            Assert.That(rig.Room.ShownFor(Table), Is.EqualTo("builtin"));
        }

        // ---- forge.backend off, the Forge's cache, rooms -------------------------------------------------------

        [Test]
        public void Asset_BackendOff_SendsNothing()
        {
            var rig = new Rig();
            rig.Core.Backend = AssetClientCore.BackendOff;
            rig.Scan(FixtureRoom());
            rig.Run(5.0);
            Assert.That(rig.Forge.Requests, Is.Empty);
            Assert.That(rig.Core.BuiltInCount, Is.EqualTo(3));
            Assert.That(rig.Events.Last("builtin", Table).GetString("reason"), Is.EqualTo("backend_off"));
        }

        [Test]
        public void Asset_ForgeCacheHit_GoesStraightToTheDownload()
        {
            var rig = new Rig();
            byte[] glb = AssetFixtures.Glb("forge_table");
            string sha = GlbValidator.Sha256Hex(glb, glb.Length);
            rig.Forge.Assets[sha] = glb;
            rig.Forge.Cached[Table] = sha;
            rig.Scan(FixtureRoom());
            rig.Run(3.0);
            Assert.That(rig.Core.GetState(Table), Is.EqualTo(ObjectAssetState.Ready));
            Assert.That(rig.Events.CountOf("cached", Table), Is.EqualTo(1));
            Assert.That(rig.Events.CountOf("requested", Table), Is.EqualTo(0));
            Assert.That(rig.Room.Requested.Contains(Table), Is.False);
        }

        [Test]
        public void Asset_RoomRefusedByTheForge_IsNotSentAgain()
        {
            var rig = new Rig();
            rig.Forge.RoomStatus = 422;
            rig.Scan(FixtureRoom());
            rig.Run(6.0);
            Assert.That(rig.Forge.CountOf("POST /rooms"), Is.EqualTo(1));
            Assert.That(rig.Core.ForgeReachable, Is.True);
            Assert.That(rig.Core.BuiltInCount, Is.EqualTo(3));
        }

        [Test]
        public void Asset_AnotherRoom_EndsThePreviousOne()
        {
            var rig = new Rig();
            rig.Scan(FixtureRoom("room_a"));
            rig.Run(2.0);
            FakeForge.Job old = rig.Forge.JobOf(Table);

            rig.Scan(FixtureRoom("room_b"));
            // A late answer about the first room is dropped.
            rig.Core.OnJobStatus(new JobStatusMessage { JobId = old.JobId, RoomId = "room_a", BoxId = Table, State = "failed" });
            Assert.That(rig.Core.GetState(Table), Is.EqualTo(ObjectAssetState.Uploading));
            rig.Run(2.0);
            Assert.That(rig.Forge.Rooms, Is.EqualTo(new[] { "room_a", "room_b" }));
            Assert.That(rig.Forge.Jobs.Count, Is.EqualTo(6));
            Assert.That(rig.Forge.JobOf(Table).RoomId, Is.EqualTo("room_b"));
        }

        [Test]
        public void Asset_RoomBuiltAgain_ShowsEveryMeshAgain()
        {
            var rig = new Rig();
            RoomModel room = FixtureRoom();
            rig.Scan(room);
            rig.Run(2.0);
            rig.Forge.Fail(Chair, "generate_failed");
            rig.Forge.Finish(Table, AssetFixtures.Glb("forge_table"));
            rig.Forge.Finish(Couch, AssetFixtures.Glb("forge_couch"));
            rig.Run(4.0);
            int posts = rig.Forge.CountOf("POST"), downloads = rig.Forge.CountOf("GET /assets");

            // The room content rebuilt its views (a space was left and Home entered again): all boxes again.
            rig.Room.Shown.Clear();
            rig.Core.RoomBuilt(room);
            rig.Run(1.0);
            Assert.That(rig.Room.ShownFor(Table), Is.EqualTo("generated"));
            Assert.That(rig.Room.ShownFor(Couch), Is.EqualTo("generated"));
            Assert.That(rig.Room.ShownFor(Chair), Is.EqualTo("builtin"));
            Assert.That(rig.Forge.CountOf("POST"), Is.EqualTo(posts), "nothing is uploaded twice");
            Assert.That(rig.Forge.CountOf("GET /assets"), Is.EqualTo(downloads), "the meshes come from the cache");
        }

        [Test]
        public void Asset_ForgetRoom_DeletesItOnTheForgeAndInTheCache()
        {
            var rig = new Rig();
            rig.Scan(FixtureRoom());
            rig.Run(2.0);
            FinishAll(rig);
            rig.Run(4.0);
            Assert.That(rig.Cache.Count, Is.EqualTo(3));

            rig.Core.ForgetRoom("room_a");
            rig.Run(0.5);
            Assert.That(rig.Forge.Requests, Does.Contain("DELETE /rooms/room_a"));
            Assert.That(rig.Forge.Rooms, Is.Empty);
            Assert.That(rig.Cache.Count, Is.EqualTo(0));
            Assert.That(rig.Core.ObjectCount, Is.EqualTo(0));
        }

        [Test]
        public void Asset_Tick_AllocatesNothingWhenIdle()
        {
            var rig = new Rig();
            rig.Scan(FixtureRoom());
            rig.Run(2.0);
            FinishAll(rig);
            rig.Run(4.0);
            Assert.That(rig.Core.PlacedCount, Is.EqualTo(3));
            rig.Run(1.0);

            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 600; i++) rig.Core.Tick(rig.Time + 0.001 * i, FrameMs);
            long after = System.GC.GetAllocatedBytesForCurrentThread();
            Assert.That(after - before, Is.EqualTo(0L), "the frame path of a settled room is free of garbage");
        }
    }
}
