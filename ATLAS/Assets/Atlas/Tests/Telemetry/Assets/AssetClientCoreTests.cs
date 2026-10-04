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
                Assert.That(rig.Room.ShownFor(id), Is.EqualTo("builtin"), "the first shape stands in while the Forge works");
                Assert.That(rig.Core.ShowsBuiltIn(id), Is.True);
            }
            Assert.That(rig.Core.BuiltInCount, Is.EqualTo(3));

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
            Assert.That(rig.Room.BuiltInTriangles.Count, Is.EqualTo(3), "only the first shapes were ever placed");

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
            Assert.That(rig.Events.CountOf("builtin"), Is.EqualTo(3), "one first shape each, and no fall-back");
            foreach (int id in new[] { Table, Chair, Couch })
                Assert.That(rig.Events.Last("builtin", id).GetString("reason"), Is.EqualTo(AssetClientCore.FirstShapeReason));
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
            // The three boxes are so large that each takes the ceiling of 15,000 (120000 * area / total area is
            // 37,485, 17,523 and 64,990 before the clamp); the light target is 60000 / 3 = 20000, held to 5,000.
            Assert.That(meta.GetInt("triTarget"), Is.EqualTo(15000));
            Assert.That(meta.GetInt("triTargetLight"), Is.EqualTo(5000), "the room's light target, as the photo set carried none");
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
        public void Asset_Budget_AtMostThirtyTwoObjectsLargestFirst()
        {
            var objects = new RoomObject[40];
            // Volumes in a scrambled order, so that the order sent is the client's and not the array's.
            for (int i = 0; i < 40; i++)
            {
                float side = 0.3f + 0.02f * ((i * 7) % 40);
                objects[i] = Box(i + 1, ObjectCategory.Storage, side, side, side);
            }
            RoomModel room = RoomOf("room_big", objects);
            // The room names 40 targets by hand; a RoomModel that names more than 32 is still held to 32.
            foreach (RoomObject o in room.Objects) o.TriTarget = 3000;

            var rig = new Rig();
            rig.Core.RoomBuilt(room);
            rig.Core.BeginRoom(room, "{\"version\":1,\"id\":\"room_big\",\"objects\":[]}");
            // Photos arrive in the array's order; the client still serves the largest box first.
            foreach (RoomObject o in room.Objects) rig.Core.Submit(Photos(o));
            rig.Core.EndRoom();
            Assert.That(rig.Core.ObjectCount, Is.EqualTo(32));
            rig.Run(20.0);

            List<RoomObject> order = LargestFirst(room);
            Assert.That(rig.Forge.Jobs.Count, Is.EqualTo(32));
            for (int k = 0; k < 32; k++)
            {
                Assert.That(rig.Forge.Jobs[k].BoxId, Is.EqualTo(order[k].Id), "upload " + k);
                Assert.That(rig.Core.ObjectIdAt(k), Is.EqualTo(order[k].Id));
                Assert.That(rig.Forge.Jobs[k].TriTarget, Is.EqualTo(3000));
                Assert.That(rig.Core.TriTarget(order[k].Id), Is.EqualTo(3000));
            }
            for (int k = 32; k < 40; k++)
            {
                // Beyond the 32: no Forge job, but the first shape like every other object.
                Assert.That(rig.Core.GetState(order[k].Id), Is.EqualTo(ObjectAssetState.BuiltIn));
                Assert.That(rig.Core.TriTarget(order[k].Id), Is.EqualTo(0));
                Assert.That(rig.Room.ShownFor(order[k].Id), Is.EqualTo("builtin"));
            }
        }

        // Expected numbers are the test vectors of the brief (S5), worked by hand. Five 1 m cubes: 120000 / 5 is
        // 24,000, held to 15,000; light 60000 / 5 = 12,000, held to 5,000. Twelve boxes of 1.8 x 0.8 x 0.9 are equal,
        // so 120000 / 12 = 10,000 each; light 60000 / 12 = 5,000. Eight 1.5 x 1 x 1 boxes (area 8,000,000 mm2 each) and
        // twenty-four 0.2 m cubes (240,000 mm2 each): the total is 69,760,000, so the big ones get 13,761 and the small
        // 412, held to 3,000; the sum 182,088 is over 120,000, so the part above 3,000 is scaled by
        // (120000 - 32 * 3000) / (8 * 10,761) = 24,000 / 86,088, which gives 6,000 and 3,000. Light: 60000 / 32 = 1,875.
        [Test]
        public void Asset_Budget_EqualBoxesShareTheTriangles()
        {
            AssertTargets(Cubes(5, 1f, 1f, 1f), 15000, 5000);
            AssertTargets(Cubes(12, 1.8f, 0.8f, 0.9f), 10000, 5000);
        }

        [Test]
        public void Asset_Budget_UnequalBoxesGetTheirOwnTargets()
        {
            var objects = new RoomObject[32];
            for (int i = 0; i < 8; i++) objects[i] = Box(i + 1, ObjectCategory.Storage, 1.5f, 1.0f, 1.0f);
            for (int i = 8; i < 32; i++) objects[i] = Box(i + 1, ObjectCategory.Storage, 0.2f, 0.2f, 0.2f);
            var rig = new Rig();
            rig.Scan(RoomOf("room_n", objects));
            rig.Run(0.5 * 32 + 4.0);
            Assert.That(rig.Forge.Jobs.Count, Is.EqualTo(32));
            foreach (FakeForge.Job job in rig.Forge.Jobs)
            {
                Assert.That(job.TriTarget, Is.EqualTo(job.BoxId <= 8 ? 6000 : 3000), "box " + job.BoxId);
                Assert.That(job.Meta.GetInt("triTargetLight"), Is.EqualTo(1875), "the room's light target");
                Assert.That(rig.Core.TriTarget(job.BoxId), Is.EqualTo(job.TriTarget));
            }
        }

        static RoomObject[] Cubes(int n, float w, float h, float d)
        {
            var objects = new RoomObject[n];
            for (int i = 0; i < n; i++) objects[i] = Box(i + 1, ObjectCategory.Storage, w, h, d);
            return objects;
        }

        static void AssertTargets(RoomObject[] objects, int full, int light)
        {
            int n = objects.Length;
            var rig = new Rig();
            rig.Scan(RoomOf("room_n", objects));
            rig.Run(0.5 * n + 4.0);
            Assert.That(rig.Forge.Jobs.Count, Is.EqualTo(n));
            foreach (FakeForge.Job job in rig.Forge.Jobs)
            {
                Assert.That(job.TriTarget, Is.EqualTo(full), n + " boxes");
                Assert.That(job.Meta.GetInt("triTargetLight"), Is.EqualTo(light), n + " boxes");
            }
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
            Assert.That(rig.Core.BuiltInCount, Is.EqualTo(3), "the first shapes are there at once; the Forge is not given up on yet");
            Assert.That(rig.Core.GetState(Table), Is.Not.EqualTo(ObjectAssetState.BuiltIn), "three attempts are made first: 2 + 1 + 2 + 2 + 2 s");
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
            Assert.That(rig.Core.BuiltInCount, Is.EqualTo(3), "the first shapes come with the room, whatever the Forge");
            Assert.That(rig.Core.GetState(Couch), Is.EqualTo(ObjectAssetState.Placeholder));
            Assert.That(rig.Events.Last("builtin", Couch).GetString("reason"), Is.EqualTo(AssetClientCore.FirstShapeReason));
            rig.Run(0.3);
            Assert.That(rig.Core.BuiltInCount, Is.EqualTo(3));
            Assert.That(rig.Core.GetState(Couch), Is.EqualTo(ObjectAssetState.BuiltIn));
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

        // ---- First shapes ---------------------------------------------------------------------------------------

        static byte[] ProfileOf(int value)
        {
            var profile = new byte[256];
            for (int i = 0; i < 256; i++) profile[i] = (byte)value;
            return profile;
        }

        [Test]
        public void Asset_FirstShapes_AreShownAtOnceWithTheForgeReachable()
        {
            RoomObject unknown = Box(4, ObjectCategory.Unknown, 0.6f, 0.5f, 0.6f);
            unknown.Profile = ProfileOf(200);
            RoomObject other = Box(5, ObjectCategory.Other, 0.6f, 0.5f, 0.6f);          // no profile, no template: the box
            RoomObject lamp = Box(6, ObjectCategory.Lamp, 0.3f, 1.5f, 0.3f);
            RoomObject chair = Box(7, ObjectCategory.Chair, 0.5f, 0.9f, 0.5f);
            chair.Profile = ProfileOf(130);
            RoomModel room = RoomOf("room_first", unknown, other, lamp, chair, Box(8, ObjectCategory.Table, 1.2f, 0.75f, 0.8f));
            lamp.TriTarget = 0;                                                         // beyond the meshed objects

            var rig = new Rig();
            rig.Scan(room);
            rig.Run(1.0);
            Assert.That(rig.Core.ForgeReachable, Is.True);
            Assert.That(rig.Core.GetState(unknown.Id), Is.EqualTo(ObjectAssetState.Requested), "the Forge is working on it");

            Assert.That(rig.Room.ShownFor(8), Is.EqualTo("builtin"), "a template");
            Assert.That(rig.Room.ShownFor(7), Is.EqualTo("builtin"), "a fitted template");
            Assert.That(rig.Room.ShownFor(4), Is.EqualTo("builtin"), "cut from the scan");
            Assert.That(rig.Room.ShownFor(6), Is.EqualTo("builtin"), "an object with no triangle target has one as well");
            Assert.That(rig.Room.ShownFor(5), Is.EqualTo("box"), "no template and no profile: the only box left");
            Assert.That(rig.Events.Last("builtin", 8).GetString("shape"), Is.EqualTo("template"));
            Assert.That(rig.Events.Last("builtin", 7).GetString("shape"), Is.EqualTo("fitted"));
            Assert.That(rig.Events.Last("builtin", 4).GetString("shape"), Is.EqualTo("scancut"));
            Assert.That(rig.Events.Last("builtin", 4).GetString("reason"), Is.EqualTo(AssetClientCore.FirstShapeReason));
            Assert.That(rig.Events.Last("builtin", 5).GetBool("placed"), Is.False);
            Assert.That(rig.Events.Last("builtin", 5).GetString("kept"), Is.EqualTo("no_template"));
            foreach (int id in new[] { 4, 6, 7, 8 }) Assert.That(rig.Room.BuiltInTriangles[id], Is.InRange(1, 1500));
            Assert.That(rig.Core.BuiltInCount, Is.EqualTo(4));
            Assert.That(rig.Core.TriTarget(6), Is.EqualTo(0));
            Assert.That(rig.Forge.Jobs.Count, Is.EqualTo(4), "the lamp has no job");

            // The generated mesh replaces the first shape when it arrives.
            rig.Forge.Finish(8, AssetFixtures.Glb("forge_table"));
            rig.Run(3.0);
            Assert.That(rig.Core.GetState(8), Is.EqualTo(ObjectAssetState.Ready));
            Assert.That(rig.Room.ShownFor(8), Is.EqualTo("generated"));
            Assert.That(rig.Core.ShowsBuiltIn(8), Is.False);
            Assert.That(rig.Room.ShownFor(6), Is.EqualTo("builtin"), "the lamp keeps its first shape");
        }

        [Test]
        public void Asset_FirstShapes_AreBuiltAgainWhenTheRoomContentIsBuiltAgain()
        {
            var rig = new Rig();
            RoomModel room = FixtureRoom();
            rig.Scan(room);
            rig.Run(2.0);
            FinishAll(rig);
            rig.Run(4.0);
            Assert.That(rig.Core.PlacedCount, Is.EqualTo(3));

            // The views are new; the cached meshes come back, and a first shape stands in until they do.
            rig.Room.Shown.Clear();
            rig.Core.RoomBuilt(room);
            rig.Run(0.2);
            Assert.That(rig.Room.ShownFor(Table), Is.Not.EqualTo("box"));
            rig.Run(2.0);
            foreach (int id in new[] { Table, Chair, Couch }) Assert.That(rig.Room.ShownFor(id), Is.EqualTo("generated"), "object " + id);
        }

        [Test]
        public void Asset_Meta_CarriesTheProfileAndTheLightTargetOnlyWhenThereAreOnes()
        {
            RoomObject chair = Box(Chair, ObjectCategory.Chair, 0.5f, 0.9f, 0.5f);
            chair.TriTarget = 9000;
            ObjectPhotoSet photos = Photos(chair);
            JsonValue plain = ForgeProtocol.Meta(photos, 9000, "local");
            Assert.That(plain.Has("profile"), Is.False);
            Assert.That(plain.Has("triTargetLight"), Is.False);
            Assert.That(plain.GetInt("triTarget"), Is.EqualTo(9000));

            photos.TriTargetLight = 3000;
            photos.Profile = ProfileOf(77);
            JsonValue meta = ForgeProtocol.Meta(photos, 9000, "local");
            Assert.That(meta.GetInt("triTargetLight"), Is.EqualTo(3000));
            byte[] decoded = System.Convert.FromBase64String(meta.GetString("profile"));
            Assert.That(decoded, Is.EqualTo(photos.Profile));
            Assert.That(meta.GetString("profile").Length, Is.EqualTo(344));
            // The rest of the meta is what it was.
            Assert.That(meta.GetInt("boxId"), Is.EqualTo(Chair));
            Assert.That(meta["views"].Count, Is.EqualTo(1));

            photos.TriTargetLight = 0;
            Assert.That(ForgeProtocol.Meta(photos, 9000, "local").Has("triTargetLight"), Is.False);
        }

        [Test]
        public void Asset_ThePhonesOwnTargetIsSentAndExpected()
        {
            // The target is the object's own (design section 5.4), not a function of how many objects there are.
            RoomModel room = FixtureRoom();
            room.Objects[0].TriTarget = 7000;
            room.Objects[1].TriTarget = 4000;
            room.Objects[2].TriTarget = 15000;
            var rig = new Rig();
            rig.Scan(room);
            rig.Run(2.0);
            Assert.That(rig.Forge.JobOf(Table).TriTarget, Is.EqualTo(7000));
            Assert.That(rig.Forge.JobOf(Chair).TriTarget, Is.EqualTo(4000));
            Assert.That(rig.Forge.JobOf(Couch).TriTarget, Is.EqualTo(15000));
            Assert.That(rig.Core.TriTarget(Chair), Is.EqualTo(4000));
        }

        // ---- The light copy -------------------------------------------------------------------------------------

        // One box the size of TestGlb's files: the full target is 15,000 and the room's light target 5,000.
        static RoomModel LightRoom() { return RoomOf("room_l", Box(1, ObjectCategory.Storage, 1.0f, 0.5f, 1.0f)); }

        static Rig LightRig(byte[] full, byte[] light, string claimedLightSha = null, bool run = true)
        {
            var rig = new Rig();
            rig.Scan(LightRoom());
            rig.Run(2.0);
            rig.Forge.FinishWithLight(1, full, light, claimedLightSha);
            if (run) rig.Run(6.0);
            return rig;
        }

        static void AssertFullStillShown(Rig rig)
        {
            Assert.That(rig.Core.GetState(1), Is.EqualTo(ObjectAssetState.Ready));
            Assert.That(rig.Room.ShownFor(1), Is.EqualTo("generated"));
            Assert.That(rig.Room.GeneratedLight, Is.Empty, "no light mesh was handed over");
            Assert.That(rig.Core.PlacedCount, Is.EqualTo(1));
        }

        [Test]
        public void Asset_LightCopy_IsFetchedAfterTheFullOneAndHandedOverOnce()
        {
            byte[] full = TestGlb.Make(6000), light = TestGlb.Make(2000);
            Rig rig = LightRig(full, light);
            string fullSha = rig.Forge.JobOf(1).Sha, lightSha = rig.Forge.JobOf(1).LightSha;

            Assert.That(rig.Core.GetState(1), Is.EqualTo(ObjectAssetState.Ready));
            Assert.That(rig.Room.ShownFor(1), Is.EqualTo("generated"));
            Assert.That(rig.Room.Generated[1], Is.EqualTo(fullSha));
            Assert.That(rig.Room.GeneratedTriangles[1], Is.EqualTo(6000));
            Assert.That(rig.Room.GeneratedLight[1], Is.EqualTo(lightSha));
            Assert.That(rig.Room.GeneratedLightTriangles[1], Is.EqualTo(2000));
            Assert.That(rig.Room.Calls.FindAll(c => c.StartsWith("light:")).Count, Is.EqualTo(1));
            Assert.That(rig.Room.Calls.IndexOf("generated:1"), Is.LessThan(rig.Room.Calls.IndexOf("light:1")), "full first");

            Assert.That(rig.Forge.Requests.IndexOf("GET /assets/" + fullSha + ".glb"), Is.GreaterThanOrEqualTo(0));
            Assert.That(rig.Forge.Requests.IndexOf("GET /assets/" + fullSha + ".glb"),
                Is.LessThan(rig.Forge.Requests.IndexOf("GET /assets/" + lightSha + ".glb")), "two downloads, in order");
            Assert.That(rig.Forge.CountOf("GET /assets/"), Is.EqualTo(2));
            Assert.That(rig.Cache.Contains(fullSha), Is.True);
            Assert.That(rig.Cache.Contains(lightSha), Is.True, "the light copy is cached by its hash");
            Assert.That(rig.Events.CountOf("light_placed", 1), Is.EqualTo(1));
            Assert.That(rig.Acks.Count, Is.EqualTo(1), "only the full copy is acknowledged");
            Assert.That(rig.Core.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Asset_NoLightFields_MeansNoSecondDownload()
        {
            var rig = new Rig();
            rig.Scan(LightRoom());
            rig.Run(2.0);
            rig.Forge.Finish(1, TestGlb.Make(6000));
            rig.Run(6.0);
            AssertFullStillShown(rig);
            Assert.That(rig.Forge.CountOf("GET /assets/"), Is.EqualTo(1));
            Assert.That(rig.Events.CountOf("light_placed"), Is.EqualTo(0));
            Assert.That(rig.Events.CountOf("light_dropped"), Is.EqualTo(0));
        }

        [Test]
        public void Asset_LightCopy_WithAWrongHash_IsDroppedAndTheFullMeshStays()
        {
            string claimed = new string('e', 64);
            Rig rig = LightRig(TestGlb.Make(6000), TestGlb.Make(2000), claimed);
            AssertFullStillShown(rig);
            Assert.That(rig.Events.Last("light_dropped", 1).GetString("reason"), Is.EqualTo("hash_mismatch"));
            Assert.That(rig.Forge.CountOf("GET /assets/" + claimed), Is.EqualTo(1), "a rejected file is not fetched again");
            Assert.That(rig.Cache.Contains(claimed), Is.False);
            Assert.That(rig.Events.CountOf("rejected"), Is.EqualTo(0), "the full copy's record is untouched");
            Assert.That(rig.Acks.Count, Is.EqualTo(1));
            Assert.That(rig.Acks[0].Ok, Is.True);
        }

        [Test]
        public void Asset_LightCopy_OverItsLevelsLimit_IsDroppedAndTheFullMeshStays()
        {
            // 5,001 triangles pass the full copy's limit (15,000) but not the light one's (5,000).
            Rig rig = LightRig(TestGlb.Make(6000), TestGlb.Make(5001));
            AssertFullStillShown(rig);
            Assert.That(rig.Events.Last("light_dropped", 1).GetString("reason"), Is.EqualTo("validate:tris_max"));
        }

        [Test]
        public void Asset_LightCopy_OverTheRoomsLightTarget_IsDroppedAndTheFullMeshStays()
        {
            // 13 objects: the light target is 60000 / 13 = 4,615. Box 1 is the largest of equal boxes.
            var objects = new RoomObject[13];
            for (int i = 0; i < 13; i++) objects[i] = Box(i + 1, ObjectCategory.Storage, 1.0f, 0.5f, 1.0f);
            var rig = new Rig();
            rig.Scan(RoomOf("room_13", objects));
            rig.Run(10.0);
            Assert.That(rig.Forge.JobOf(1).Meta.GetInt("triTargetLight"), Is.EqualTo(4615));
            rig.Forge.FinishWithLight(1, TestGlb.Make(6000), TestGlb.Make(4616));
            rig.Run(6.0);
            Assert.That(rig.Core.GetState(1), Is.EqualTo(ObjectAssetState.Ready));
            Assert.That(rig.Room.GeneratedLight, Is.Empty);
            Assert.That(rig.Events.Last("light_dropped", 1).GetString("reason"), Is.EqualTo("validate:tris_target"));
        }

        [Test]
        public void Asset_LightCopy_ThatCannotBeDownloaded_IsDroppedAfterTheUsualRetries()
        {
            Rig rig = LightRig(TestGlb.Make(6000), TestGlb.Make(2000), null, false);
            rig.Forge.Assets.Remove(rig.Forge.JobOf(1).LightSha);          // the Forge answers 404
            rig.Run(10.0);
            AssertFullStillShown(rig);
            Assert.That(rig.Forge.CountOf("GET /assets/" + rig.Forge.JobOf(1).LightSha), Is.EqualTo(AssetClientCore.MaxAttempts));
            Assert.That(rig.Events.Last("light_dropped", 1).GetString("reason"), Is.EqualTo("download_failed"));
        }

        [Test]
        public void Asset_LightCopy_TheRoomContentRefuses_IsDroppedAndTheFullMeshStays()
        {
            var rig = new Rig();
            rig.Room.RefuseLight = true;
            rig.Scan(LightRoom());
            rig.Run(2.0);
            rig.Forge.FinishWithLight(1, TestGlb.Make(6000), TestGlb.Make(2000));
            rig.Run(6.0);
            AssertFullStillShown(rig);
            Assert.That(rig.Events.Last("light_dropped", 1).GetString("reason"), Is.EqualTo("load_failed"));
        }

        [Test]
        public void Asset_LightCopy_ARoomBuiltAgainMidDownload_HandsNoStaleMeshOver()
        {
            var rig = new Rig();
            RoomModel room = LightRoom();
            rig.Scan(room);
            rig.Run(2.0);
            rig.Forge.Latency = 1.0;
            rig.Forge.FinishWithLight(1, TestGlb.Make(6000), TestGlb.Make(2000));
            string lightSha = rig.Forge.JobOf(1).LightSha;
            // Run until the light copy is on the wire.
            for (int i = 0; i < 400 && rig.Forge.CountOf("GET /assets/" + lightSha) == 0; i++) rig.Run(0.05);
            Assert.That(rig.Forge.CountOf("GET /assets/" + lightSha), Is.EqualTo(1));
            Assert.That(rig.Room.GeneratedLight, Is.Empty);

            // The room content builds its views again: the generated mesh and anything meant for it are gone.
            rig.Room.Shown.Clear();
            rig.Room.Calls.Clear();
            rig.Core.RoomBuilt(room);
            rig.Run(0.2);
            Assert.That(rig.Room.Calls, Does.Not.Contain("light:1"), "nothing is handed to views that have no full mesh");
            rig.Run(8.0);
            Assert.That(rig.Room.ShownFor(1), Is.EqualTo("generated"));
            Assert.That(rig.Room.Calls.FindAll(c => c.StartsWith("light:")).Count, Is.EqualTo(1), "the light copy follows the full one again");
            Assert.That(rig.Room.Calls.IndexOf("generated:1"), Is.LessThan(rig.Room.Calls.IndexOf("light:1")));
            Assert.That(rig.Room.GeneratedLight[1], Is.EqualTo(lightSha));
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
