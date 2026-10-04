using System;
using System.Collections.Generic;
using System.Text;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Telemetry.Tests
{
    /// <summary>Helpers shared by the Spectator and flat-twin tests.</summary>
    static class SpectatorTestRooms
    {
        /// <summary>The outline message the phone sends for a room (the link driver's SetRoom).</summary>
        public static RoomOutlineMessage OutlineOf(RoomModel room)
        {
            var outline = new RoomOutlineMessage();
            outline.Clear();
            for (int i = 0; i < room.Outline.Length; i++) outline.AddPoint(room.Outline[i].x, room.Outline[i].y);
            outline.Height = RoomModel.DefaultWallHeight;
            outline.RectW = room.FreeRect.Wf;
            outline.RectD = room.FreeRect.Df;
            for (int i = 0; i < room.Objects.Length; i++)
            {
                RoomObject o = room.Objects[i];
                if (o == null) continue;
                var box = new RoomOutlineBox
                {
                    Id = (byte)o.Id, Category = (byte)o.Category,
                    Cx = o.Base.x, Cz = o.Base.z, W = o.Size.x, D = o.Size.z, H = o.Size.y, Yaw = o.Yaw
                };
                outline.AddBox(in box);
            }
            return outline;
        }

        /// <summary>The world hash the phone computes for a room and a space on the flat kernel.</summary>
        public static string HashOf(RoomModel room, int space)
        {
            return WorldHash.Compute(space, 4, SpectatorCore.WorldSeed, SpectatorCore.GeneratorVersion,
                RoomModelJson.GeometryHash(room), WorldHash.Millimetres(KernelConstants.FlatMetresPerUnit));
        }
    }

    /// <summary>
    /// The Spectator's engine-free core (Master Plan section 7.3.1, task P1-D05): what it shows for what the
    /// link delivers. A space switch, a room change, a parameter change, a world that does not match, and the
    /// frame built from a State with nothing integrated.
    /// </summary>
    public class SpectatorCoreTests
    {
        /// <summary>A space module that records what its host did to it.</summary>
        sealed class FakeModule : ISpaceModule, ISpaceStateReader, IUploadStep
        {
            public readonly List<string> Calls;
            public RoomModel Room;
            public StateMessage LastRead;
            public FrameContext LastFrame;
            public int Reads, Ticks, Uploads;
            public bool ThrowOnTick;

            public FakeModule(SpaceId id, List<string> calls) { Id = id; Calls = calls; }

            public SpaceId Id { get; private set; }
            public bool Registered { get { return true; } }
            public void Configure(RoomModel room, ParamRegistry registry) { Room = room; Calls.Add("configure " + SpaceIds.Code(Id) + " " + room.Id); }
            public void Enter(in WalkerPose start) { Calls.Add("enter " + SpaceIds.Code(Id)); }
            public void ReadState(in StateMessage state) { LastRead = state; Reads++; Calls.Add("read"); }
            public void Tick(in FrameContext frame)
            {
                if (ThrowOnTick) throw new InvalidOperationException("broken");
                LastFrame = frame;
                Ticks++;
                Calls.Add("tick");
            }
            public void Upload(in FrameContext frame) { Uploads++; Calls.Add("upload"); }
            public void Exit() { Calls.Add("exit " + SpaceIds.Code(Id)); }
            public void WriteState(ref StateMessage state) { }
        }

        /// <summary>A room source that answers from the fixtures, at once or when told to.</summary>
        sealed class FakeRooms : ISpectatorRooms
        {
            public readonly List<string> Asked = new List<string>();
            public bool Hold;
            public RoomModel Override;
            string _pending;
            bool _waiting;

            public void Begin(string roomId, string forgeUrl, RoomOutlineMessage outline)
            {
                Asked.Add(roomId);
                _pending = roomId;
                _waiting = true;
            }

            public bool TryTake(out RoomModel room)
            {
                room = null;
                if (!_waiting || Hold) return false;
                _waiting = false;
                if (Override != null) room = Override;
                else if (RoomFixtures.IsFixture(_pending)) room = RoomFixtures.LoadFromProject(_pending);
                return true;
            }
        }

        sealed class Rig
        {
            public readonly ParamRegistry Registry = new ParamRegistry();
            // The Station's kernel is the phone's (SpectatorApp): flat until a State says otherwise.
            public readonly HolonomyWalkerKernel Kernel = new HolonomyWalkerKernel();
            public readonly FakeRooms Rooms = new FakeRooms();
            public readonly List<string> Calls = new List<string>();
            public readonly List<FakeModule> Modules = new List<FakeModule>();
            public readonly List<string> RoomChanges = new List<string>();
            public readonly SpectatorHost Host;
            public readonly SpectatorCore Core;
            public readonly SpectatorLoop Loop;
            public readonly ParamEntry Copies, Marks;
            public double Now = 10.0;
            public bool GluedMissing;

            public Rig(ISpectatorRooms rooms = null)
            {
                Copies = Registry.DeclareInt("glued.copies", "glued", 3, 7, 5);
                Marks = Registry.DeclareBool("home.marks", "home", true);
                Host = new SpectatorHost(Registry, Kernel)
                {
                    Create = id =>
                    {
                        if (GluedMissing && id == SpaceId.Glued) return null;
                        var module = new FakeModule(id, Calls);
                        Modules.Add(module);
                        return module;
                    },
                    Destroy = module => Calls.Add("destroy " + SpaceIds.Code(module.Id)),
                    BetweenSpaces = () => Calls.Add("between"),
                };
                Host.RoomChanged += room => RoomChanges.Add(room != null ? room.Id : "none");
                Core = new SpectatorCore(Host, Registry, Kernel, rooms ?? Rooms);
                Loop = new SpectatorLoop(Core, Kernel) { LinkUp = true };
                Loop.AddSense(new SpectatorSense(Core));
            }

            public FakeModule Module { get { return Modules[Modules.Count - 1]; } }

            public void Frame(double dt = 1.0 / 60.0)
            {
                Now += dt;
                Loop.Run(Now, (float)dt);
                Loop.RunAfterFrame();
            }

            public void World(string roomId, int space, string hash = null, bool withGeometry = true)
            {
                RoomModel room = RoomFixtures.IsFixture(roomId) ? RoomFixtures.LoadFromProject(roomId) : null;
                JsonValue data = JsonValue.Object()
                    .Set("worldVer", 1)
                    .Set("worldHash", hash ?? (room != null ? SpectatorTestRooms.HashOf(room, space) : string.Empty))
                    .Set("space", space)
                    .Set("roomId", roomId);
                if (withGeometry && room != null) data.Set("geomHash", RoomModelJson.GeometryHash(room));
                Core.OnEvent(new EventMessage(1, "S" + space, LinkEvents.World, data));
            }

            public void State(byte space, float x = 0.4f, float z = -0.3f, ushort worldVer = 1)
            {
                StateMessage s = StateMessage.Identity;
                s.SpaceId = space;
                s.SceneId = space;
                s.HeadPosX = x;
                s.HeadPosY = 1.6f;
                s.HeadPosZ = z;
                s.WorldVer = worldVer;
                s.TimeUs = (long)(Now * 1e6);
                s.BumpLevel = 1;
                s.QualityTier = 2;
                Core.OnState(in s);
            }
        }

        [Test]
        public void Spectator_shows_nothing_until_the_hub_the_phone_and_the_room_are_there()
        {
            var rig = new Rig();
            rig.Loop.LinkUp = false;
            rig.Frame();
            Assert.That(rig.Core.Banner, Is.EqualTo(SpectatorBanner.NoHub));

            rig.Loop.LinkUp = true;
            rig.Frame();
            Assert.That(rig.Core.Banner, Is.EqualTo(SpectatorBanner.NoPhone));
            Assert.That(rig.Loop.Current.RawHeadPosition, Is.EqualTo(float3.zero), "with no State the frame stays at the origin");

            // A phone that is still in onboarding: States with no space and no room.
            rig.State(SpaceIds.NoSpace);
            rig.Frame();
            Assert.That(rig.Core.Banner, Is.EqualTo(SpectatorBanner.NoRoom));
            Assert.That(rig.Host.HasSpace, Is.False);
            Assert.That(rig.Rooms.Asked, Is.Empty, "no room is asked for before the phone names one");
            Assert.That(rig.Modules, Is.Empty, "and no world is made up");
        }

        [Test]
        public void Spectator_fetches_the_phones_room_and_enters_its_space()
        {
            var rig = new Rig();
            rig.Core.OnWelcome(new WelcomeMessage { SessionId = "s", ForgeUrl = "http://10.0.0.2:8000" });
            rig.World(RoomFixtures.Sample3x3, 0);
            rig.State((byte)SpaceId.Home);
            rig.Rooms.Hold = true;
            rig.Frame();
            Assert.That(rig.Rooms.Asked, Is.EqualTo(new[] { RoomFixtures.Sample3x3 }));
            Assert.That(rig.Core.Banner, Is.EqualTo(SpectatorBanner.FetchingRoom));
            Assert.That(rig.Host.HasSpace, Is.False);

            rig.Rooms.Hold = false;
            rig.Frame();
            Assert.That(rig.RoomChanges, Is.EqualTo(new[] { RoomFixtures.Sample3x3 }), "the room content rebuilds on RoomChanged");
            Assert.That(rig.Core.WorldVerified, Is.True);
            Assert.That(rig.Host.HasSpace, Is.True);
            Assert.That(rig.Host.Current, Is.EqualTo(SpaceId.Home));
            Assert.That(rig.Core.Banner, Is.EqualTo(SpectatorBanner.None));
            Assert.That(rig.Module.Room.Id, Is.EqualTo(RoomFixtures.Sample3x3), "the module is configured from the phone's room");
            Assert.That(rig.Registry.IsGroupActive("home"), Is.True);
            Assert.That(rig.Rooms.Asked.Count, Is.EqualTo(1));

            // The module is driven from the State: read, tick, upload, in that order, on every frame.
            rig.Calls.Clear();
            rig.State((byte)SpaceId.Home, 0.7f, 0.2f, 1);
            rig.Frame();
            Assert.That(rig.Calls, Is.EqualTo(new[] { "read", "tick", "upload" }));
            Assert.That(rig.Module.LastRead.HeadPosX, Is.EqualTo(0.7f));
            Assert.That(rig.Module.LastFrame.RawHeadPosition, Is.EqualTo(new float3(0.7f, 1.6f, 0.2f)), "the camera pose is the State's head pose");
            Assert.That(rig.Module.LastFrame.HeadPosition, Is.EqualTo(rig.Module.LastFrame.RawHeadPosition), "no pose filter");
            Assert.That(rig.Module.LastFrame.LeftEyePosition, Is.EqualTo(rig.Module.LastFrame.RawHeadPosition), "one mono camera");
            Assert.That(rig.Module.LastFrame.FloorDelta, Is.EqualTo(float2.zero));
            Assert.That(rig.Module.LastFrame.Tier, Is.EqualTo(QualityTier.Serious), "the phone's tier");
            Assert.That(rig.Host.WorldVersion, Is.EqualTo(1), "the phone's world version");
            Assert.That(rig.Kernel.StepCount, Is.EqualTo(0), "the kernel is placed, never stepped: nothing is integrated");
            long ci, cj;
            Assert.That(FlatTileGraph.TryGetCell(rig.Module.LastFrame.Walker.Tile, out ci, out cj), Is.True);
        }

        [Test]
        public void Spectator_follows_a_space_switch()
        {
            var rig = new Rig();
            rig.World(RoomFixtures.Sample3x3, 0);
            rig.State((byte)SpaceId.Home);
            rig.Frame();
            Assert.That(rig.Host.Current, Is.EqualTo(SpaceId.Home));
            var entered = new List<SpaceId>();
            var exited = new List<SpaceId>();
            rig.Host.SpaceEntered += entered.Add;
            rig.Host.SpaceExited += exited.Add;

            // The State changes space first; the world event of the new space follows on the reliable channel.
            rig.Calls.Clear();
            rig.State((byte)SpaceId.Glued, 0.1f, 0.1f, 2);
            rig.Frame();
            Assert.That(rig.Calls, Is.EqualTo(new[]
            {
                "exit S0", "destroy S0", "between", "configure S1 sample_3x3", "enter S1", "read", "tick", "upload"
            }));
            Assert.That(rig.Host.Current, Is.EqualTo(SpaceId.Glued));
            Assert.That(exited, Is.EqualTo(new[] { SpaceId.Home }));
            Assert.That(entered, Is.EqualTo(new[] { SpaceId.Glued }));
            Assert.That(rig.Registry.IsGroupActive("home"), Is.False);
            Assert.That(rig.Registry.IsGroupActive("glued"), Is.True);
            Assert.That(rig.Host.WorldVersion, Is.EqualTo(2));
            Assert.That(rig.RoomChanges.Count, Is.EqualTo(1), "the room stays");

            rig.World(RoomFixtures.Sample3x3, 1);
            rig.Frame();
            Assert.That(rig.Host.Current, Is.EqualTo(SpaceId.Glued));
            Assert.That(rig.Host.EnterCount, Is.EqualTo(2), "the matching world event changes nothing");
            Assert.That(rig.Core.Banner, Is.EqualTo(SpectatorBanner.None));

            // The walker leaves every space (a new scan): the Spectator leaves too and says so.
            rig.State(SpaceIds.NoSpace);
            rig.Frame();
            Assert.That(rig.Host.HasSpace, Is.False);
            Assert.That(rig.Core.Banner, Is.EqualTo(SpectatorBanner.NoSpace));

            // A space this build cannot make is named, and is not tried again on every frame.
            rig.GluedMissing = true;
            int made = rig.Modules.Count;
            rig.State((byte)SpaceId.Glued);
            rig.Frame();
            rig.Frame();
            Assert.That(rig.Core.Banner, Is.EqualTo(SpectatorBanner.SpaceMissing));
            Assert.That(rig.Modules.Count, Is.EqualTo(made));
            Assert.That(rig.Host.HasSpace, Is.False);
        }

        [Test]
        public void Spectator_follows_a_room_change()
        {
            var rig = new Rig();
            rig.World(RoomFixtures.Sample3x3, 1);
            rig.State((byte)SpaceId.Glued);
            rig.Frame();
            Assert.That(rig.Module.Room.Id, Is.EqualTo(RoomFixtures.Sample3x3));

            // The walker scans another room and enters the same space there.
            rig.World(RoomFixtures.BudgetRoom, 1);
            rig.State((byte)SpaceId.Glued, 0f, 0f, 5);
            rig.Frame();
            rig.Frame();
            Assert.That(rig.Rooms.Asked, Is.EqualTo(new[] { RoomFixtures.Sample3x3, RoomFixtures.BudgetRoom }));
            Assert.That(rig.RoomChanges, Is.EqualTo(new[] { RoomFixtures.Sample3x3, "none", RoomFixtures.BudgetRoom }));
            Assert.That(rig.Host.Room.Id, Is.EqualTo(RoomFixtures.BudgetRoom));
            Assert.That(rig.Host.HasSpace, Is.True);
            Assert.That(rig.Module.Room.Id, Is.EqualTo(RoomFixtures.BudgetRoom), "the space is made again from the new room");
            Assert.That(rig.Calls, Does.Contain("exit S1"));
            Assert.That(rig.Core.Room.Id, Is.EqualTo(RoomFixtures.BudgetRoom));
            Assert.That(rig.Core.Banner, Is.EqualTo(SpectatorBanner.None));
        }

        [Test]
        public void Spectator_applies_the_manifest_and_parameter_changes()
        {
            var rig = new Rig();
            // The phone's registry: the same names, other values, and one name this build does not know.
            var phone = new ParamRegistry();
            ParamEntry copies = phone.DeclareInt("glued.copies", "glued", 3, 7, 5);
            phone.DeclareBool("home.marks", "home", true);
            phone.DeclareFloat("render.fogEnd", "render", 1f, 30f, 14f);
            phone.DeclareInt("link.stateHz", "link", 10, 60, 60).Set(10);
            ParamEntry ownHz = rig.Registry.DeclareInt("link.stateHz", "link", 10, 60, 60);
            copies.Set(7);
            phone.SetGroupActive("glued", false);
            rig.Core.OnManifest(ParamManifestMessage.FromRegistry(phone));
            Assert.That(rig.Copies.Int, Is.EqualTo(7));
            Assert.That(rig.Marks.Bool, Is.True);
            Assert.That(rig.Registry.IsGroupActive("glued"), Is.False, "the manifest's group state");
            Assert.That(ownHz.Int, Is.EqualTo(60), "the Station's own link settings are not the phone's");

            // One param event per change, applied by name.
            copies.Set(3);
            rig.Core.OnEvent(LinkEvents.ParamChanged(5, "S1", copies, ParamSource.Remote));
            Assert.That(rig.Copies.Int, Is.EqualTo(3));
            ParamEntry marks = phone.Find("home.marks");
            marks.Set(false);
            rig.Core.OnEvent(LinkEvents.ParamChanged(6, "S0", marks, ParamSource.Local));
            Assert.That(rig.Marks.Bool, Is.False);
            Assert.That(rig.Core.ParamsApplied, Is.EqualTo(3));

            // A value out of this registry's range is clamped, an unknown name and another event are ignored.
            rig.Core.OnEvent(new EventMessage(7, "S1", LinkEvents.Param,
                JsonValue.Object().Set("id", 0).Set("name", "glued.copies").Set("value", 99f).Set("source", "remote")));
            Assert.That(rig.Copies.Int, Is.EqualTo(7));
            rig.Core.OnEvent(new EventMessage(8, "S1", LinkEvents.Param,
                JsonValue.Object().Set("id", 9).Set("name", "render.fogEnd").Set("value", 9f).Set("source", "remote")));
            rig.Core.OnEvent(new EventMessage(9, "S1", "bump.level", JsonValue.Object().Set("level", 2)));
            Assert.That(rig.Core.ParamsApplied, Is.EqualTo(4));
        }

        [Test]
        public void Spectator_enters_no_space_on_a_world_mismatch_and_asks_again_every_two_seconds()
        {
            var rig = new Rig();
            // The phone's world is hashed from another room than the one this id fetches.
            RoomModel other = RoomFixtures.LoadFromProject(RoomFixtures.BudgetRoom);
            rig.World(RoomFixtures.Sample3x3, 0, SpectatorTestRooms.HashOf(other, 0), false);
            rig.State((byte)SpaceId.Home);
            rig.Frame();
            Assert.That(rig.Core.Banner, Is.EqualTo(SpectatorBanner.WorldMismatch));
            Assert.That(rig.Host.HasSpace, Is.False);
            Assert.That(rig.Host.Room, Is.Null);
            Assert.That(rig.Core.Room, Is.Null);
            Assert.That(rig.Modules, Is.Empty, "no world is drawn");
            Assert.That(rig.Rooms.Asked.Count, Is.EqualTo(1));

            for (int i = 0; i < 60; i++) { rig.State((byte)SpaceId.Home); rig.Frame(); }
            Assert.That(rig.Rooms.Asked.Count, Is.EqualTo(1), "not before two seconds");
            for (int i = 0; i < 90; i++) { rig.State((byte)SpaceId.Home); rig.Frame(); }
            Assert.That(rig.Rooms.Asked.Count, Is.EqualTo(2));
            Assert.That(rig.Core.Banner, Is.EqualTo(SpectatorBanner.WorldMismatch));

            // The phone's next world event agrees: the room is taken on the next fetch.
            rig.World(RoomFixtures.Sample3x3, 0);
            for (int i = 0; i < 130; i++) { rig.State((byte)SpaceId.Home); rig.Frame(); }
            Assert.That(rig.Host.HasSpace, Is.True);
            Assert.That(rig.Core.WorldVerified, Is.True);
            Assert.That(rig.Core.Banner, Is.EqualTo(SpectatorBanner.None));

            // A held room that stops matching is dropped at once.
            rig.World(RoomFixtures.Sample3x3, 0, "0123456789abcdef");
            rig.State((byte)SpaceId.Home);
            rig.Frame();
            Assert.That(rig.Host.HasSpace, Is.False);
            Assert.That(rig.Host.Room, Is.Null);
            Assert.That(rig.Core.Banner, Is.EqualTo(SpectatorBanner.WorldMismatch));
        }

        [Test]
        public void Spectator_verifies_a_curved_world_with_the_tiling_seed_and_length_the_phone_names()
        {
            // Curved Walk on the phone: {4,5}, the generator's seed 1, L of the room's loop. The Spectator's own
            // kernel is flat; the room is what it must agree on.
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            const int space = (int)SpaceId.CurvedWalk, q = 5, genVer = 1;
            const long seed = 1;
            int lMm = WorldHash.Millimetres(room.Curvature.ForDial(5, false));
            string geometry = RoomModelJson.GeometryHash(room);
            string hash = WorldHash.Compute(space, q, seed, genVer, geometry, lMm);
            Assert.That(hash, Is.Not.EqualTo(SpectatorTestRooms.HashOf(room, space)), "not the hash of a flat world");

            var rig = new Rig();
            JsonValue data = JsonValue.Object().Set("worldVer", 1).Set("worldHash", hash).Set("space", space)
                .Set("roomId", RoomFixtures.Sample3x3).Set("geomHash", geometry)
                .Set("q", q).Set("seed", seed).Set("genVer", genVer).Set("lMm", lMm);
            rig.Core.OnEvent(new EventMessage(1, "S2", LinkEvents.World, data));
            rig.State((byte)SpaceId.CurvedWalk);
            rig.Frame();
            Assert.That(rig.Core.WorldVerified, Is.True);
            Assert.That(rig.Core.Banner, Is.Not.EqualTo(SpectatorBanner.WorldMismatch));
            Assert.That(rig.Host.Room, Is.Not.Null);

            // The same statement about another room does not hold for the room held here.
            RoomModel other = RoomFixtures.LoadFromProject(RoomFixtures.BudgetRoom);
            var wrong = new Rig();
            data = JsonValue.Object().Set("worldVer", 1).Set("space", space).Set("roomId", RoomFixtures.Sample3x3)
                .Set("worldHash", WorldHash.Compute(space, q, seed, genVer, RoomModelJson.GeometryHash(other), lMm))
                .Set("q", q).Set("seed", seed).Set("genVer", genVer).Set("lMm", lMm);
            wrong.Core.OnEvent(new EventMessage(1, "S2", LinkEvents.World, data));
            wrong.State((byte)SpaceId.CurvedWalk);
            wrong.Frame();
            Assert.That(wrong.Core.Banner, Is.EqualTo(SpectatorBanner.WorldMismatch));
            Assert.That(wrong.Host.Room, Is.Null);
        }

        [Test]
        public void Spectator_takes_a_welcome_hash_for_any_space_and_marks_a_silent_phone()
        {
            var rig = new Rig();
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.SampleL3x4);
            // A Spectator that joins late: the Welcome names the room and the hash of the phone's space.
            rig.Core.OnWelcome(new WelcomeMessage
            {
                SessionId = "s", RoomId = room.Id, WorldHash = SpectatorTestRooms.HashOf(room, (int)SpaceId.Slicer)
            });
            rig.State((byte)SpaceId.Slicer);
            rig.Frame();
            Assert.That(rig.Host.Current, Is.EqualTo(SpaceId.Slicer));
            Assert.That(rig.Core.WorldVerified, Is.True);
            Assert.That(rig.Core.StatesReceived, Is.EqualTo(1));

            // An empty Welcome (the Hub's answer before the phone is back) unsays nothing.
            rig.Core.OnWelcome(new WelcomeMessage { SessionId = "s2" });
            rig.State((byte)SpaceId.Slicer);
            rig.Frame();
            Assert.That(rig.Host.HasSpace, Is.True);

            // No State for more than two seconds: the last picture stays, marked.
            for (int i = 0; i < 150; i++) rig.Frame();
            Assert.That(rig.Core.Banner, Is.EqualTo(SpectatorBanner.NoPhone));
            Assert.That(rig.Host.HasSpace, Is.True);
            rig.State((byte)SpaceId.Slicer);
            rig.Frame();
            Assert.That(rig.Core.Banner, Is.EqualTo(SpectatorBanner.None));
        }

        [Test]
        public void Spectator_leaves_a_module_that_throws()
        {
            var rig = new Rig();
            rig.World(RoomFixtures.Sample3x3, 0);
            rig.State((byte)SpaceId.Home);
            rig.Frame();
            rig.Module.ThrowOnTick = true;
            rig.State((byte)SpaceId.Home);
            rig.Frame();
            Assert.That(rig.Host.HasSpace, Is.False);
            Assert.That(rig.Host.LastError, Does.Contain("broken"));
            int made = rig.Modules.Count;
            rig.State((byte)SpaceId.Home);
            rig.Frame();
            Assert.That(rig.Modules.Count, Is.EqualTo(made), "a broken space is not made again on every frame");
        }

        // ---- curved spaces: the phone's side of the wire ------------------------------------------------------------

        const double StepMetres = 0.05;

        /// <summary>A space module with a generated world, as Curved Walk is one.</summary>
        sealed class FakeWorldModule : ISpaceModule, ISpaceWorld
        {
            public FakeWorldModule(SpaceId id) { Id = id; }
            public SpaceId Id { get; private set; }
            public bool Registered { get { return false; } }
            public long WorldSeed { get; set; }
            public int GeneratorVersion { get; set; }
            public string ContentHash { get; set; }
            public void Configure(RoomModel room, ParamRegistry registry) { }
            public void Enter(in WalkerPose start) { }
            public void Tick(in FrameContext frame) { }
            public void Exit() { }
            public void WriteState(ref StateMessage state) { }
        }

        // The State as the phone fills it for a space that walks the kernel (FrameLoopCore.FillState: the tile's
        // six words, Local row-major, p[0] = metres per unit, p[1..2] from rho), then through the wire's bytes.
        static StateMessage Sent(IDialKernel phone, byte space, double2 head, double time)
        {
            StateMessage s = StateMessage.Identity;
            s.SpaceId = space;
            s.SceneId = space;
            s.WorldVer = 1;
            s.TimeUs = (long)(time * 1e6);
            s.HeadPosX = (float)head.x;
            s.HeadPosY = 1.6f;
            s.HeadPosZ = (float)head.y;
            WalkerPose pose = phone.Pose;
            TileId tile = pose.Tile;
            s.SetTile(tile.Xu, tile.Xv, tile.Yu, tile.Yv, tile.Tu, tile.Tv);
            Span<double> m = stackalloc double[9];
            pose.Local.CopyTo(m);
            for (int i = 0; i < 9; i++) s.SetXform(i, m[i]);
            float rho = (float)phone.CullRho;
            s.P0 = (float)phone.MetresPerUnit;
            s.P1 = rho - 1f;
            s.P2 = rho;
            // What the space adds (CurvedWalkCore.WriteState, SmallPlanetCore.WriteState): the dial and the curvature.
            s.P3 = phone.Q;
            s.P4 = (float)phone.CurvatureNow;

            var bytes = new byte[StateMessage.Size];
            Assert.That(s.WriteTo(bytes, 0), Is.EqualTo(StateMessage.Size));
            StateMessage received = default(StateMessage);
            Assert.That(received.ReadFrom(bytes, 0, bytes.Length), Is.True);
            return received;
        }

        // The world event as the link driver logs it for a space on the phone's kernel (LinkPump.ComputeWorldHash).
        static string SendWorld(Rig rig, RoomModel room, int space, IDialKernel phone, long seed, string content = null)
        {
            int q = phone.Graph.Q, lMm = WorldHash.Millimetres(phone.ConfiguredMetresPerUnit);
            string geometry = RoomModelJson.GeometryHash(room);
            string hash = WorldHash.Compute(space, q, seed, 1, geometry, lMm);
            JsonValue data = JsonValue.Object().Set("worldVer", 1).Set("worldHash", hash).Set("space", space)
                .Set("roomId", RoomFixtures.Sample3x3).Set("geomHash", geometry)
                .Set("q", q).Set("seed", seed).Set("genVer", 1).Set("lMm", lMm);
            if (content != null) data.Set("genHash", content);
            rig.Core.OnEvent(new EventMessage(1, "S" + space, LinkEvents.World, data));
            return hash;
        }

        static void AssertSamePose(WalkerPose station, WalkerPose phone, string where)
        {
            Assert.That(station.Tile, Is.EqualTo(phone.Tile), where + ": tile");
            Span<double> a = stackalloc double[9], b = stackalloc double[9];
            station.Local.CopyTo(a);
            phone.Local.CopyTo(b);
            for (int i = 0; i < 9; i++) Assert.That(a[i], Is.EqualTo(b[i]).Within(1e-9), where + ": xform " + i);
            Assert.That(station.Local.Kappa, Is.EqualTo(phone.Local.Kappa), where + ": kappa");
        }

        [TestCase(5)]
        [TestCase(6)]
        public void Spectator_follows_a_lap_walked_in_curved_walk_from_the_bytes_of_its_states(int q)
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            double l = room.Curvature.ForDial(q, false), side = room.LapSide;
            const byte space = (byte)SpaceId.CurvedWalk;

            // The phone: Curved Walk puts the kernel on the dial's tiling, registers the walker flat under the
            // head and ramps the curvature in while the walker stands (CurvedWalkCore.Enter, Control).
            IDialKernel phone = new HolonomyWalkerKernel();
            phone.Configure(KernelConfig.Curved(q, l));
            var head = new double2(-0.5 * side, -0.5 * side);
            phone.ResetToFloorFlat(head);
            phone.CullRho = 2.5;

            var rig = new Rig();
            string hash = SendWorld(rig, room, space, phone, 1);
            double time = 0.0;
            var tiles = new List<TileId>();
            Action send = () =>
            {
                StateMessage s = Sent(phone, space, head, time);
                rig.Core.OnState(in s);
                rig.Frame();
                WalkerPose pose = phone.Pose;
                if (tiles.Count == 0 || tiles[tiles.Count - 1] != pose.Tile) tiles.Add(pose.Tile);
                AssertSamePose(rig.Kernel.Pose, pose, "t = " + time);
                AssertSamePose(rig.Loop.Current.Walker, pose, "frame, t = " + time);
            };

            // K = 0: the Station stands in the flat room on the same tiling.
            send();
            Assert.That(rig.Kernel.Q, Is.EqualTo(q));
            Assert.That(rig.Kernel.Kappa, Is.EqualTo(0.0));
            Assert.That(rig.Kernel.MetresPerUnit, Is.EqualTo(1.0));
            Assert.That(rig.Host.Current, Is.EqualTo(SpaceId.CurvedWalk));
            Assert.That(rig.Core.Banner, Is.EqualTo(SpectatorBanner.None));

            // The ramp, four seconds of standing: the Station's curvature and scale are the phone's of each frame.
            phone.SetCurvatureTarget(Curvature.Hyperbolic, l);
            bool midRamp = false;
            for (int i = 0; i < 300; i++)
            {
                time += 1.0 / 60.0;
                phone.Step(head, true, time);
                send();
                Assert.That(rig.Kernel.CurvatureNow, Is.EqualTo(phone.CurvatureNow).Within(1e-6 * Math.Abs(phone.CurvatureNow)));
                Assert.That(rig.Kernel.MetresPerUnit, Is.EqualTo(phone.MetresPerUnit).Within(1e-6 * phone.MetresPerUnit));
                midRamp |= phone.Ramping && phone.CurvatureNow != 0.0;
            }
            Assert.That(midRamp, Is.True, "States were sent from inside the ramp");
            Assert.That(phone.Ramping, Is.False);
            Assert.That(rig.Kernel.Kappa, Is.EqualTo(Curvature.Hyperbolic));

            // q sides of the lap square, the lap of Master Plan section 3.3, and then straight out of the room's
            // tile: the head leaves the room in this test, which a kernel does not mind.
            double2[] corner =
            {
                new double2(-0.5 * side, -0.5 * side), new double2(0.5 * side, -0.5 * side),
                new double2(0.5 * side, 0.5 * side), new double2(-0.5 * side, 0.5 * side),
            };
            for (int k = 1; k <= q + 1; k++)
            {
                double2 from = head, to = k <= q ? corner[k % 4] : head + new double2(2.3, 3.1);
                int steps = (int)Math.Ceiling(math.length(to - from) / StepMetres);
                for (int i = 1; i <= steps; i++)
                {
                    time += 1.0 / 60.0;
                    head = math.lerp(from, to, (double)i / steps);
                    phone.Step(head, true, time);
                    send();
                }
            }
            Assert.That(tiles.Count, Is.GreaterThan(2), "tiles were crossed");
            Assert.That(phone.Pose.Tile, Is.Not.EqualTo(TileId.Zero));

            // The tile sequence the Station kept is the phone's.
            FlatTwinTrail trail = rig.Core.Trail;
            Assert.That(trail.TilesSeen, Is.EqualTo(tiles.Count));
            int kept = trail.TileCount;
            for (int k = 0; k < kept; k++) Assert.That(trail.GetTile(k), Is.EqualTo(tiles[tiles.Count - kept + k]), "trail tile " + k);

            // Nothing was integrated here, and the module was handed the placed walker.
            Assert.That(rig.Kernel.StepCount, Is.EqualTo(0));
            AssertSamePose(rig.Module.LastFrame.Walker, phone.Pose, "module");
            Assert.That(rig.Kernel.CullRho, Is.EqualTo(2.5), "rho is the phone's (p[2])");
            Assert.That(rig.Kernel.Candidates.Count, Is.EqualTo(phone.Candidates.Count));

            // The world hash: the Station's own kernel and room give the phone's.
            Assert.That(rig.Core.WorldVerified, Is.True);
            Assert.That(rig.Core.Banner, Is.EqualTo(SpectatorBanner.None));
            string own = WorldHash.Compute(space, rig.Kernel.Graph.Q, 1, 1, RoomModelJson.GeometryHash(rig.Core.Room),
                WorldHash.Millimetres(rig.Kernel.ConfiguredMetresPerUnit));
            Assert.That(own, Is.EqualTo(hash));
            Assert.That(own, Is.Not.EqualTo(SpectatorTestRooms.HashOf(room, space)), "not the hash of a flat world");
        }

        [Test]
        public void Spectator_follows_a_walk_round_the_small_planet_and_goes_back_to_flat()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            double r = room.Curvature.ForDial(3, false);
            const byte space = (byte)SpaceId.SmallPlanet;
            IDialKernel phone = new HolonomyWalkerKernel();
            phone.Configure(KernelConfig.Curved(3, r));
            double2 head = double2.zero;
            phone.ResetToFloor(head);

            var rig = new Rig();
            SendWorld(rig, room, space, phone, 0);
            // Once round the planet in a straight line: four faces, and back on face 0.
            int steps = (int)Math.Ceiling(2.0 * Math.PI * r / StepMetres);
            var faces = new List<int>();
            for (int i = 0; i <= steps; i++)
            {
                if (i > 0)
                {
                    head.x += 2.0 * Math.PI * r / steps;
                    phone.Step(head, true, i / 60.0);
                }
                StateMessage s = Sent(phone, space, head, i / 60.0);
                rig.Core.OnState(in s);
                rig.Frame();
                AssertSamePose(rig.Kernel.Pose, phone.Pose, "step " + i);
                int face = CubeTiling.FaceOf(phone.Pose.Tile);
                if (faces.Count == 0 || faces[faces.Count - 1] != face) faces.Add(face);
            }
            Assert.That(faces.Count, Is.EqualTo(5), "four faces and the first again");
            Assert.That(faces[4], Is.EqualTo(0));
            Assert.That(rig.Kernel.Q, Is.EqualTo(3));
            Assert.That(rig.Kernel.Kappa, Is.EqualTo(Curvature.Spherical));
            Assert.That(rig.Kernel.MetresPerUnit, Is.EqualTo((double)(float)r), "_HypL of the Station is the phone's R");
            Assert.That(rig.Kernel.StepCount, Is.EqualTo(0));
            Assert.That(rig.Core.Banner, Is.EqualTo(SpectatorBanner.None));
            Assert.That(rig.Host.Current, Is.EqualTo(SpaceId.SmallPlanet));

            // Home again: the kernel is the flat one of the room, the walker on the floor point under the head.
            rig.World(RoomFixtures.Sample3x3, (int)SpaceId.Home);
            rig.State((byte)SpaceId.Home, 0.4f, -0.3f);
            rig.Frame();
            Assert.That(rig.Kernel.Q, Is.EqualTo(4));
            Assert.That(rig.Kernel.Graph.CentreSpacing, Is.EqualTo((double)room.LapSide).Within(1e-12));
            WalkerPose flat = rig.Loop.Current.Walker;
            Assert.That(flat.Tile, Is.EqualTo(FlatTileGraph.Cell(0, 0)));
            Assert.That(flat.Local.M02, Is.EqualTo(0.4f).Within(1e-6));
            Assert.That(flat.Local.M12, Is.EqualTo(-0.3f).Within(1e-6));
        }

        [Test]
        public void Spectator_keeps_the_kernel_flat_in_aligned_cells()
        {
            // S3 walks a tile graph of its own: its State names a {4,5} tile and a quarter-turn, and the frame
            // loop's kernel stays flat and registered on the phone. So it does here.
            var rig = new Rig();
            rig.World(RoomFixtures.Sample3x3, (int)SpaceId.AlignedCells);
            StateMessage s = StateMessage.Identity;
            s.SpaceId = (byte)SpaceId.AlignedCells;
            s.HeadPosX = 0.3f;
            s.HeadPosZ = 0.2f;
            TileId tile = new TileGraph(5).Neighbour(TileId.Zero, 1);
            s.SetTile(tile.Xu, tile.Xv, tile.Yu, tile.Yv, tile.Tu, tile.Tv);
            s.Sheet = 4;
            s.Xform0 = 0; s.Xform1 = 1; s.Xform3 = -1; s.Xform4 = 0;
            rig.Core.OnState(in s);
            rig.Frame();
            Assert.That(rig.Host.Current, Is.EqualTo(SpaceId.AlignedCells));
            Assert.That(rig.Kernel.Q, Is.EqualTo(4));
            WalkerPose pose = rig.Loop.Current.Walker;
            Assert.That(pose.Tile, Is.EqualTo(FlatTileGraph.Cell(0, 0)));
            Assert.That(pose.Local.M02, Is.EqualTo(0.3f).Within(1e-6));
            Assert.That(pose.Local.M12, Is.EqualTo(0.2f).Within(1e-6));
            Assert.That(rig.Module.LastRead.Sheet, Is.EqualTo(4), "the module reads its own tile from the State");
        }

        [Test]
        public void Spectator_keeps_the_last_good_place_when_a_curved_state_is_broken()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            double l = room.Curvature.ForDial(5, false);
            const byte space = (byte)SpaceId.CurvedWalk;
            IDialKernel phone = new HolonomyWalkerKernel();
            phone.Configure(KernelConfig.Curved(5, l));
            phone.ResetToFloor(double2.zero);
            var head = new double2(0.0, 0.0);
            for (int i = 1; i <= 40; i++)
            {
                head.x = i * StepMetres;
                phone.Step(head, true);
            }
            Assert.That(phone.Pose.Tile, Is.Not.EqualTo(TileId.Zero));

            var rig = new Rig();
            SendWorld(rig, room, space, phone, 1);

            // A first State that is no place on the tiling: the walker stands under the head on the flat kernel.
            StateMessage broken = Sent(phone, space, head, 0.0);
            broken.SetXform(0, double.NaN);
            rig.Core.OnState(in broken);
            rig.Frame();
            Assert.That(rig.Loop.Current.Walker.IsValid, Is.True);
            Assert.That(rig.Kernel.Q, Is.EqualTo(4));

            StateMessage good = Sent(phone, space, head, 0.1);
            rig.Core.OnState(in good);
            rig.Frame();
            AssertSamePose(rig.Kernel.Pose, phone.Pose, "good");

            // Broken again, and six words that are no tile of {4,5}: the last good place stays, on the same tiling.
            rig.Core.OnState(in broken);
            rig.Frame();
            AssertSamePose(rig.Kernel.Pose, phone.Pose, "after a broken transform");
            StateMessage wrong = good;
            wrong.SetTile(7, 3, -2, 5, 1, 9);
            rig.Core.OnState(in wrong);
            rig.Frame();
            AssertSamePose(rig.Kernel.Pose, phone.Pose, "after a foreign tile");
            Assert.That(rig.Kernel.Q, Is.EqualTo(5));
            Assert.That(rig.Host.HasSpace, Is.True);
            Assert.That(rig.Kernel.StepCount, Is.EqualTo(0));
        }

        [Test]
        public void Spectator_shows_no_world_that_its_own_generator_made_differently()
        {
            // Master Plan section 7.3.1, test L8: the same room, the same tiling, another generated world.
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            const byte space = (byte)SpaceId.CurvedWalk;
            IDialKernel phone = new HolonomyWalkerKernel();
            phone.Configure(KernelConfig.Curved(5, room.Curvature.ForDial(5, false)));
            phone.ResetToFloor(double2.zero);

            var rig = new Rig();
            var made = new List<FakeWorldModule>();
            rig.Host.Create = id =>
            {
                var module = new FakeWorldModule(id) { WorldSeed = 1, GeneratorVersion = 1, ContentHash = "00000000000000aa" };
                made.Add(module);
                return module;
            };
            Action frame = () =>
            {
                StateMessage s = Sent(phone, space, double2.zero, rig.Now);
                rig.Core.OnState(in s);
                rig.Frame();
            };

            // The phone's generator and this one agree: seed, version and what they made.
            SendWorld(rig, room, space, phone, 1, "00000000000000aa");
            for (int i = 0; i < 60; i++) frame();
            Assert.That(rig.Core.Banner, Is.EqualTo(SpectatorBanner.None));
            Assert.That(rig.Host.HasSpace, Is.True);
            Assert.That(rig.Core.WorldVerified, Is.True);

            // The phone now names another seed. Its hash is the hash of that seed, so the room still verifies;
            // what this build would generate is not the walker's world.
            SendWorld(rig, room, space, phone, 2, "00000000000000aa");
            frame();
            Assert.That(rig.Host.HasSpace, Is.True, "an event and a State may be a frame apart");
            for (int i = 0; i < 60; i++) frame();
            Assert.That(rig.Core.Banner, Is.EqualTo(SpectatorBanner.WorldMismatch));
            Assert.That(rig.Host.HasSpace, Is.False);
            Assert.That(rig.Host.Room, Is.Not.Null, "the room itself was not wrong");
            int entered = made.Count;
            for (int i = 0; i < 30; i++) frame();
            Assert.That(made.Count, Is.EqualTo(entered), "not entered again on every frame");

            // The same seed and another content (another footprint, another L): the same verdict.
            SendWorld(rig, room, space, phone, 1, "00000000000000bb");
            for (int i = 0; i < 60; i++) frame();
            Assert.That(rig.Core.Banner, Is.EqualTo(SpectatorBanner.WorldMismatch));
            Assert.That(rig.Host.HasSpace, Is.False);

            // The phone's world is this one again.
            SendWorld(rig, room, space, phone, 1, "00000000000000aa");
            for (int i = 0; i < 10; i++) frame();
            Assert.That(rig.Core.Banner, Is.EqualTo(SpectatorBanner.None));
            Assert.That(rig.Host.Current, Is.EqualTo(SpaceId.CurvedWalk));
        }

        // ---- the room source --------------------------------------------------------------------------------------

        sealed class FakeForge : IForgeTransport
        {
            public readonly List<string> Requests = new List<string>();
            public int Status = 200;
            public byte[] Body;
            public bool Answered = true;
            public int Cancelled;

            public int Begin(string method, string url, byte[] body, string contentType, float timeoutSeconds)
            {
                Requests.Add(method + " " + url);
                return Requests.Count;
            }

            public bool TryTake(int handle, out int status, out byte[] body)
            {
                status = Status;
                body = Body;
                return Answered;
            }

            public void Cancel(int handle) { Cancelled++; }
        }

        [Test]
        public void SpectatorRoomSource_reads_fixtures_by_name_and_other_rooms_from_the_forge()
        {
            var forge = new FakeForge { Body = Encoding.UTF8.GetBytes(RoomFixtures.ReadTextFromProject(RoomFixtures.Sample4x5Table)) };
            var source = new SpectatorRoomSource(forge, RoomFixtures.LoadFromProject);
            RoomModel room;
            Assert.That(source.TryTake(out room), Is.False, "nothing was begun");

            source.Begin(RoomFixtures.TinyStanding, "http://10.0.0.2:8000", null);
            Assert.That(source.TryTake(out room), Is.True);
            Assert.That(room.Id, Is.EqualTo(RoomFixtures.TinyStanding));
            Assert.That(forge.Requests, Is.Empty, "a fixture needs no Forge");
            Assert.That(source.TryTake(out room), Is.False, "a result is handed over once");

            forge.Answered = false;
            source.Begin("room_7f3a", "http://10.0.0.2:8000/", null);
            Assert.That(forge.Requests, Is.EqualTo(new[] { "GET http://10.0.0.2:8000/rooms/room_7f3a" }));
            Assert.That(source.TryTake(out room), Is.False, "the request is still out");
            forge.Answered = true;
            Assert.That(source.TryTake(out room), Is.True);
            Assert.That(room, Is.Not.Null);
            Assert.That(room.Outline.Length, Is.EqualTo(4));

            // 404, no answer and a body that is no RoomModel all end with no room and a reason.
            forge.Status = 404;
            source.Begin("room_gone", "http://10.0.0.2:8000", null);
            Assert.That(source.TryTake(out room), Is.True);
            Assert.That(room, Is.Null);
            Assert.That(source.LastError, Does.Contain("404"));
            forge.Status = 200;
            forge.Body = Encoding.UTF8.GetBytes("{\"id\":");
            source.Begin("room_bad", "http://10.0.0.2:8000", null);
            Assert.That(source.TryTake(out room), Is.True);
            Assert.That(room, Is.Null);

            // No Forge named: nothing is asked.
            int before = forge.Requests.Count;
            source.Begin("room_7f3a", string.Empty, null);
            Assert.That(source.TryTake(out room), Is.True);
            Assert.That(room, Is.Null);
            Assert.That(forge.Requests.Count, Is.EqualTo(before));
        }

        [Test]
        public void Spectator_knows_a_replayed_fixture_room_by_its_outline_and_never_makes_one_up()
        {
            // A replayed walk in a fixture room, recorded without a room id: the outline names the room.
            var rig = new Rig(new SpectatorRoomSource(null, RoomFixtures.LoadFromProject));
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample4x5Table);
            rig.Core.OnRoomOutline(SpectatorTestRooms.OutlineOf(room));
            rig.State((byte)SpaceId.Home);
            rig.Frame();
            rig.Frame();
            Assert.That(rig.Host.Room, Is.Not.Null);
            Assert.That(rig.Host.Room.Id, Is.EqualTo(RoomFixtures.Sample4x5Table));
            Assert.That(rig.Host.HasSpace, Is.True);
            Assert.That(rig.Core.WorldVerified, Is.False, "taken on its floor alone: no hash was given");
            Assert.That(rig.Core.OutlineRevision, Is.EqualTo(1));

            // The synthetic walk of ReplayPhone: a 3 m by 4 m floor that is no fixture. Only the outline is shown.
            var synthetic = new Rig(new SpectatorRoomSource(null, RoomFixtures.LoadFromProject));
            var outline = new RoomOutlineMessage();
            outline.Clear();
            outline.AddPoint(-1.5f, -2f);
            outline.AddPoint(1.5f, -2f);
            outline.AddPoint(1.5f, 2f);
            outline.AddPoint(-1.5f, 2f);
            outline.RectW = 3f;
            outline.RectD = 4f;
            synthetic.Core.OnRoomOutline(outline);
            synthetic.State((byte)SpaceId.Home);
            synthetic.Frame();
            synthetic.Frame();
            Assert.That(synthetic.Host.Room, Is.Null);
            Assert.That(synthetic.Host.HasSpace, Is.False);
            Assert.That(synthetic.Core.Banner, Is.EqualTo(SpectatorBanner.NoRoom));
            Assert.That(synthetic.Core.Outline.PointCount, Is.EqualTo(4), "the flat twin still has the phone's outline");
            Assert.That(synthetic.Loop.Current.RawHeadPosition.x, Is.EqualTo(0.4f), "and the mirror camera still follows the head");

            // An outline that contradicts the room fetched by id is a mismatch.
            var wrong = new Rig();
            wrong.World(RoomFixtures.Sample3x3, 0);
            wrong.Core.OnRoomOutline(outline);
            wrong.State((byte)SpaceId.Home);
            wrong.Frame();
            Assert.That(wrong.Core.Banner, Is.EqualTo(SpectatorBanner.WorldMismatch));
            Assert.That(wrong.Host.HasSpace, Is.False);
        }
    }
}
