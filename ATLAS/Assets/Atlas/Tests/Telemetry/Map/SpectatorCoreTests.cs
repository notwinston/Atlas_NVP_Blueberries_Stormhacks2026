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
            public readonly FlatWalkerKernel Kernel = new FlatWalkerKernel();
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

        [Test]
        public void Spectator_places_the_walker_from_the_tile_and_transform_of_a_curved_space()
        {
            var rig = new Rig();
            rig.World(RoomFixtures.Sample3x3, (int)SpaceId.CurvedWalk);
            StateMessage s = StateMessage.Identity;
            s.SpaceId = (byte)SpaceId.CurvedWalk;
            s.HeadPosX = 0.3f;
            s.HeadPosZ = 0.2f;
            s.SetTile(3, 0, -2, 0, 1, 0);
            s.SetXform(2, 0.25);
            s.SetXform(5, -0.5);
            rig.Core.OnState(in s);
            rig.Frame();
            WalkerPose pose = rig.Loop.Current.Walker;
            Assert.That(pose.Tile, Is.EqualTo(FlatTileGraph.Cell(3, -2)));
            Assert.That(pose.Local.M02, Is.EqualTo(0.25).Within(1e-12));
            Assert.That(pose.Local.M12, Is.EqualTo(-0.5).Within(1e-12));
            Assert.That(rig.Kernel.StepCount, Is.EqualTo(0));

            // A broken transform is not handed to the kernel: the walker stands under the head.
            s.SetXform(0, double.NaN);
            rig.Core.OnState(in s);
            rig.Frame();
            Assert.That(rig.Loop.Current.Walker.IsValid, Is.True);
            Assert.That(rig.Loop.Current.Walker.Tile, Is.EqualTo(FlatTileGraph.Cell(0, 0)));
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
