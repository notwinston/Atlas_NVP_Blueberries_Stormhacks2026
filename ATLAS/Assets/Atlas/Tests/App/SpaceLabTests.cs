using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;

namespace Atlas.App.Tests
{
    /// <summary>SpaceLab (Master Plan task P1-A08) with stand-in modules: one space at a time, a fade through
    /// black, parameters with the space, and clean refusals.</summary>
    public class SpaceLabTests
    {
        const float Dt = 1f / 60f;

        sealed class Rig
        {
            public readonly List<string> Log = new List<string>();
            public readonly ParamRegistry Registry = new ParamRegistry();
            public readonly FlatWalkerKernel Kernel = new FlatWalkerKernel();
            public readonly FakeWalker Walker = new FakeWalker();
            public readonly FakeStage Stage = new FakeStage();
            public readonly FakeEvents Events = new FakeEvents();
            public readonly FakeLoader Loader;
            public readonly SpaceLab Lab;
            public readonly List<string> Entered = new List<string>();
            public readonly List<string> Exited = new List<string>();
            FrameContext _frame;

            public Rig(params SpaceId[] available)
            {
                Loader = new FakeLoader(Log, available);
                Lab = new SpaceLab(Registry, Kernel, Walker, Loader, Stage) { Events = Events };
                Lab.SpaceEntered += id => Entered.Add(SpaceIds.Code(id));
                Lab.SpaceExited += id => Exited.Add(SpaceIds.Code(id));
            }

            public FakeModule Module { get { return (FakeModule)Lab.Module; } }

            /// <summary>One frame as the loop runs it: tick, upload, state, then the switch advances.</summary>
            public void Frame()
            {
                _frame.FrameIndex++;
                _frame.DeltaTime = Dt;
                Lab.TickSpace(in _frame);
                Lab.UploadSpace(in _frame);
                var state = StateMessage.Identity;
                Lab.WriteState(ref state);
                Lab.AfterFrame(in _frame);
            }

            public void Frames(int count) { for (int i = 0; i < count; i++) Frame(); }

            public void RunUntilRunning()
            {
                for (int i = 0; i < 200 && Lab.State != SpaceHostState.Running; i++) Frame();
                Assert.That(Lab.State, Is.EqualTo(SpaceHostState.Running));
            }

            public void GiveRoom(string id = "room")
            {
                Lab.SetRoom(Rooms.Make(id));
                Walker.RoomResetPending = false;     // the frame loop has placed the walker
            }
        }

        [Test]
        public void NoRoom_IsRefused()
        {
            var rig = new Rig(SpaceId.Home);
            Assert.That(rig.Lab.RequestSwitch(SpaceId.Home), Is.False);
            Assert.That(rig.Lab.State, Is.EqualTo(SpaceHostState.Empty));
            Assert.That(rig.Events.Has("space.refused {\"space\":\"S0\",\"reason\":\"noRoom\"}"), Is.True);
        }

        [Test]
        public void MissingSpace_IsRefusedCleanly_AndTheWalkerStaysWhereHeIs()
        {
            var rig = new Rig(SpaceId.Home, SpaceId.Glued);
            rig.GiveRoom();
            Assert.That(rig.Lab.RequestSwitch(SpaceId.Home), Is.True);
            rig.RunUntilRunning();
            int resets = rig.Walker.Resets;
            ushort version = rig.Lab.WorldVersion;

            Assert.That(rig.Lab.IsAvailable(SpaceId.CurvedWalk), Is.False);
            Assert.That(rig.Lab.RequestSwitch(SpaceId.CurvedWalk), Is.False);
            Assert.That(rig.Lab.RequestSwitch((SpaceId)42), Is.False);
            rig.Frames(60);

            Assert.That(rig.Lab.State, Is.EqualTo(SpaceHostState.Running));
            Assert.That(rig.Lab.Current, Is.EqualTo(SpaceId.Home));
            Assert.That(rig.Module.Exits, Is.EqualTo(0));
            Assert.That(rig.Walker.Resets, Is.EqualTo(resets), "nothing reaches the kernel");
            Assert.That(rig.Lab.WorldVersion, Is.EqualTo(version));
            Assert.That(rig.Loader.Created.Count, Is.EqualTo(1));
            Assert.That(rig.Events.Has("space.refused {\"space\":\"S2\",\"reason\":\"unavailable\"}"), Is.True);
            Assert.That(rig.Stage.Fade, Is.EqualTo(1f));
        }

        [Test]
        public void FirstSpace_FromEmpty_ConfigureThenEnter_ThenFadeIn()
        {
            var rig = new Rig(SpaceId.Home);
            rig.GiveRoom("first");
            Assert.That(rig.Lab.RequestSwitch(SpaceId.Home), Is.True);
            Assert.That(rig.Lab.State, Is.EqualTo(SpaceHostState.Loading), "nothing to fade out from Empty");
            rig.Frame();

            Assert.That(rig.Lab.State, Is.EqualTo(SpaceHostState.FadingIn));
            Assert.That(rig.Lab.HasSpace, Is.True);
            Assert.That(rig.Log, Is.EqualTo(new[] { "configure S0", "enter S0" }));
            Assert.That(rig.Module.Room.Id, Is.EqualTo("first"));
            Assert.That(rig.Module.Ticks, Is.EqualTo(0), "the module ticks from the frame after Enter");
            Assert.That(rig.Walker.Resets, Is.EqualTo(1), "a registered space starts where the head is");
            Assert.That(rig.Stage.Fade, Is.EqualTo(0f));
            Assert.That(rig.Entered, Is.EqualTo(new[] { "S0" }));

            rig.Frame();
            Assert.That(rig.Module.Ticks, Is.EqualTo(1));
            Assert.That(rig.Module.Uploads, Is.EqualTo(1));
            Assert.That(rig.Module.States, Is.EqualTo(1));
            rig.RunUntilRunning();
            Assert.That(rig.Stage.Fade, Is.EqualTo(1f));
            Assert.That(rig.Registry.IsGroupActive("home"), Is.True);
        }

        [Test]
        public void TwentySwitches_EnterAndExitArePaired_ParametersComeAndGo()
        {
            var rig = new Rig(SpaceId.Home, SpaceId.Glued, SpaceId.Portal, SpaceId.Slicer);
            rig.GiveRoom();
            SpaceId[] order = { SpaceId.Home, SpaceId.Glued, SpaceId.Portal, SpaceId.Slicer };
            int rentedOutside = 0;
            for (int i = 0; i < 20; i++)
            {
                SpaceId target = order[i % order.Length];
                Assert.That(rig.Lab.RequestSwitch(target), Is.True, "switch " + i);
                rig.RunUntilRunning();

                Assert.That(rig.Lab.Current, Is.EqualTo(target));
                Assert.That(rig.Lab.Module.Id, Is.EqualTo(target));
                // Exactly one group of the four is active: the entered space's.
                int active = 0;
                for (int k = 0; k < order.Length; k++)
                    if (rig.Registry.IsGroupActive(SpaceIds.ParamGroup(order[k]))) active++;
                Assert.That(active, Is.EqualTo(1));
                Assert.That(rig.Registry.IsGroupActive(SpaceIds.ParamGroup(target)), Is.True);
                ParamEntry entry = rig.Registry.Find(SpaceIds.ParamGroup(target) + ".test");
                Assert.That(entry, Is.Not.Null);
                Assert.That(entry.Active, Is.True);
                rentedOutside += rig.Loader.Created.Count - rig.Loader.Unloads - 1;
            }

            Assert.That(rig.Loader.Created.Count, Is.EqualTo(20));
            Assert.That(rig.Loader.Unloads, Is.EqualTo(19));
            Assert.That(rentedOutside, Is.EqualTo(0), "never more than one module alive");
            int enters = 0, exits = 0;
            for (int i = 0; i < rig.Loader.Created.Count; i++)
            {
                FakeModule m = rig.Loader.Created[i];
                Assert.That(m.Configures, Is.EqualTo(1));
                Assert.That(m.Enters, Is.EqualTo(1));
                Assert.That(m.Exits, Is.EqualTo(i < 19 ? 1 : 0), "module " + i);
                enters += m.Enters;
                exits += m.Exits;
            }
            Assert.That(enters, Is.EqualTo(20));
            Assert.That(exits, Is.EqualTo(19));
            Assert.That(rig.Entered.Count, Is.EqualTo(20));
            Assert.That(rig.Exited.Count, Is.EqualTo(19));
            Assert.That(rig.Lab.SwitchCount, Is.EqualTo(20));
            Assert.That(rig.Stage.Resets, Is.EqualTo(20));

            // The log alternates strictly: configure, enter, exit, configure, enter, exit ...
            for (int i = 0; i + 2 < rig.Log.Count; i += 3)
            {
                Assert.That(rig.Log[i], Does.StartWith("configure"));
                Assert.That(rig.Log[i + 1], Does.StartWith("enter"));
                Assert.That(rig.Log[i + 2], Does.StartWith("exit"));
            }

            // A value tuned in a space is still there when the space is entered again.
            rig.Registry.Set("glued.test", 0.9f);
            rig.Lab.RequestSwitch(SpaceId.Glued);
            rig.RunUntilRunning();
            Assert.That(rig.Registry.GetFloat("glued.test"), Is.EqualTo(0.9f));
        }

        [Test]
        public void Switch_FadesThroughBlack_AndTheOldSpaceTicksWhileFadingOut()
        {
            var rig = new Rig(SpaceId.Home, SpaceId.Glued);
            rig.GiveRoom();
            rig.Lab.RequestSwitch(SpaceId.Home);
            rig.RunUntilRunning();
            FakeModule home = rig.Module;
            int ticks = home.Ticks;
            rig.Stage.Fades.Clear();

            Assert.That(rig.Lab.RequestSwitch(SpaceId.Glued), Is.True);
            Assert.That(rig.Lab.State, Is.EqualTo(SpaceHostState.FadingOut));
            rig.Frame();
            Assert.That(home.Ticks, Is.EqualTo(ticks + 1));
            Assert.That(rig.Stage.Fade, Is.LessThan(1f).And.GreaterThan(0f));
            rig.RunUntilRunning();

            Assert.That(home.Exits, Is.EqualTo(1));
            Assert.That(rig.Stage.Fades, Has.Member(0f), "the world is black while the spaces change");
            Assert.That(rig.Stage.Fades[rig.Stage.Fades.Count - 1], Is.EqualTo(1f));
            // Down to black, then up: never a rise before the black frame.
            int black = rig.Stage.Fades.IndexOf(0f);
            for (int i = 1; i <= black; i++) Assert.That(rig.Stage.Fades[i], Is.LessThanOrEqualTo(rig.Stage.Fades[i - 1]));
            // 0.25 s out and 0.25 s in at 60 Hz, plus the black frame: well under a second.
            Assert.That(rig.Stage.Fades.Count, Is.LessThan(45));
            Assert.That(rig.Lab.RequestSwitch(SpaceId.Glued), Is.False, "already there");
        }

        [Test]
        public void RequestDuringASwitch_TheNewestWins()
        {
            var rig = new Rig(SpaceId.Home, SpaceId.Glued, SpaceId.Portal, SpaceId.Slicer);
            rig.GiveRoom();
            rig.Lab.RequestSwitch(SpaceId.Home);
            rig.RunUntilRunning();

            Assert.That(rig.Lab.RequestSwitch(SpaceId.Glued), Is.True);
            rig.Frame();
            Assert.That(rig.Lab.RequestSwitch(SpaceId.Portal), Is.True);
            Assert.That(rig.Lab.RequestSwitch(SpaceId.Slicer), Is.True);
            // The switch under way finishes first; the waiting request starts in the frame it reaches Running.
            rig.Frames(120);
            Assert.That(rig.Lab.State, Is.EqualTo(SpaceHostState.Running));
            Assert.That(rig.Lab.Current, Is.EqualTo(SpaceId.Slicer));
            Assert.That(rig.Entered, Is.EqualTo(new[] { "S0", "S1", "S6" }), "Portal was replaced by the newer request");
        }

        [Test]
        public void ReturnHome_InHome_ResetsTheWalkerWithNoFade_ElsewhereItSwitches()
        {
            var rig = new Rig(SpaceId.Home, SpaceId.Glued);
            rig.GiveRoom();
            rig.Lab.RequestSwitch(SpaceId.Home);
            rig.RunUntilRunning();
            int resets = rig.Walker.Resets;

            Assert.That(rig.Lab.ReturnHome(), Is.True);
            Assert.That(rig.Lab.State, Is.EqualTo(SpaceHostState.Running));
            Assert.That(rig.Walker.Resets, Is.EqualTo(resets + 1));

            rig.Lab.RequestSwitch(SpaceId.Glued);
            rig.RunUntilRunning();
            Assert.That(rig.Lab.ReturnHome(), Is.True);
            Assert.That(rig.Lab.State, Is.EqualTo(SpaceHostState.FadingOut));
            rig.RunUntilRunning();
            Assert.That(rig.Lab.Current, Is.EqualTo(SpaceId.Home));
        }

        [Test]
        public void LoadFailure_FallsBackToHome_AndWithNoHomeEndsEmpty()
        {
            var rig = new Rig(SpaceId.Home, SpaceId.Glued, SpaceId.Portal);
            rig.GiveRoom();
            rig.Loader.Broken.Add(SpaceId.Glued);
            Assert.That(rig.Lab.RequestSwitch(SpaceId.Glued), Is.True);
            rig.RunUntilRunning();
            Assert.That(rig.Lab.Current, Is.EqualTo(SpaceId.Home));
            Assert.That(rig.Events.Has("space.loadFailed space=S1"), Is.True);

            // A module that throws in Configure counts as a failed load.
            rig.Loader.OnCreate = m => m.ThrowInConfigure = m.Id == SpaceId.Portal;
            rig.Lab.RequestSwitch(SpaceId.Portal);
            rig.RunUntilRunning();
            Assert.That(rig.Lab.Current, Is.EqualTo(SpaceId.Home));

            rig.Loader.Broken.Add(SpaceId.Home);
            rig.Lab.RequestSwitch(SpaceId.Glued);
            rig.Frames(60);
            Assert.That(rig.Lab.State, Is.EqualTo(SpaceHostState.Empty));
            Assert.That(rig.Lab.HasSpace, Is.False);
            Assert.That(rig.Lab.Module, Is.Null);
            Assert.That(rig.Stage.Fade, Is.EqualTo(1f), "the room content stays visible");
        }

        [Test]
        public void SceneLoad_OverSeveralFrames_StaysBlackUntilTheModuleIsThere()
        {
            var rig = new Rig(SpaceId.Home);
            rig.Loader.LoadFrames = 5;
            rig.GiveRoom();
            rig.Lab.RequestSwitch(SpaceId.Home);
            for (int i = 0; i < 5; i++)
            {
                rig.Frame();
                Assert.That(rig.Lab.State, Is.EqualTo(SpaceHostState.Loading));
                Assert.That(rig.Lab.HasSpace, Is.False);
                Assert.That(rig.Stage.Fade, Is.EqualTo(0f));
            }
            rig.RunUntilRunning();
            Assert.That(rig.Lab.Current, Is.EqualTo(SpaceId.Home));
        }

        [Test]
        public void SetRoom_ConfiguresTheKernel_LatchesTheReset_ThenRaisesRoomChanged_AndReloads()
        {
            var rig = new Rig(SpaceId.Home);
            rig.Walker.Log = rig.Log;
            ushort version = rig.Lab.WorldVersion;
            rig.Lab.RoomChanged += room =>
            {
                if (room == null) return;
                rig.Log.Add("roomChanged " + room.Id);
                Assert.That(rig.Lab.Room, Is.SameAs(room));
                Assert.That(((FlatTileGraph)rig.Kernel.Graph).CentreSpacing, Is.EqualTo(2.4).Within(1e-6));
                Assert.That(rig.Walker.RoomResetPending, Is.True);
            };
            rig.Lab.SetRoom(Rooms.Make("a", 2.4f, true));
            Assert.That(rig.Log, Is.EqualTo(new[] { "walker.latch", "roomChanged a" }));
            Assert.That(rig.Lab.WorldVersion, Is.EqualTo((ushort)(version + 1)));
            Assert.That(rig.Lab.Standing, Is.True);
            Assert.That(rig.Lab.State, Is.EqualTo(SpaceHostState.Empty), "SetRoom enters no space by itself");

            // No space is loaded while the head is not in the room frame.
            Assert.That(rig.Lab.RequestSwitch(SpaceId.Home), Is.True);
            rig.Frames(5);
            Assert.That(rig.Lab.HasSpace, Is.False);
            Assert.That(rig.Loader.Created.Count, Is.EqualTo(0));
            rig.Walker.RoomResetPending = false;
            rig.RunUntilRunning();
            FakeModule first = rig.Module;

            // A new room with a space entered: the space is loaded again with it.
            rig.Lab.SetRoom(Rooms.Make("b", 2.4f));
            rig.Walker.RoomResetPending = false;
            Assert.That(rig.Lab.State, Is.EqualTo(SpaceHostState.FadingOut));
            rig.RunUntilRunning();
            Assert.That(first.Exits, Is.EqualTo(1));
            Assert.That(rig.Module, Is.Not.SameAs(first));
            Assert.That(rig.Module.Room.Id, Is.EqualTo("b"));
            Assert.That(rig.Lab.Standing, Is.False);

            // No room at all: the space is left.
            rig.Lab.SetRoom(null);
            rig.Frames(60);
            Assert.That(rig.Lab.State, Is.EqualTo(SpaceHostState.Empty));
            Assert.That(rig.Lab.HasSpace, Is.False);
            Assert.That(rig.Lab.RequestSwitch(SpaceId.Home), Is.False);
        }

        [Test]
        public void ModuleThatThrowsOnTheFramePath_IsLeft_AndHomeIsEntered()
        {
            var rig = new Rig(SpaceId.Home, SpaceId.Glued);
            rig.GiveRoom();
            rig.Lab.RequestSwitch(SpaceId.Glued);
            rig.RunUntilRunning();
            FakeModule glued = rig.Module;
            glued.ThrowInTick = true;

            Assert.DoesNotThrow(() => rig.Frames(3));
            rig.RunUntilRunning();
            Assert.That(glued.Exits, Is.EqualTo(1));
            Assert.That(rig.Lab.Current, Is.EqualTo(SpaceId.Home));
            Assert.That(rig.Events.Has("space.error Tick="), Is.True);
        }

        [Test]
        public void WriteState_OnlyWhileASpaceIsEntered_AndShutdownLeavesIt()
        {
            var rig = new Rig(SpaceId.Home);
            var state = StateMessage.Identity;
            rig.Lab.WriteState(ref state);
            Assert.That(state.Sheet, Is.EqualTo(0));

            rig.GiveRoom();
            rig.Lab.RequestSwitch(SpaceId.Home);
            rig.RunUntilRunning();
            rig.Lab.WriteState(ref state);
            Assert.That(state.Sheet, Is.EqualTo(10));

            FakeModule home = rig.Module;
            rig.Lab.Shutdown();
            Assert.That(home.Exits, Is.EqualTo(1));
            Assert.That(rig.Lab.HasSpace, Is.False);
            Assert.That(rig.Lab.RequestSwitch(SpaceId.Home), Is.False);
        }

        [Test]
        public void SpaceGroups_AreInactive_UntilTheirSpaceIsEntered()
        {
            var rig = new Rig(SpaceId.Home);
            for (int i = 0; i < SpaceIds.Count; i++)
                Assert.That(rig.Registry.IsGroupActive(SpaceIds.ParamGroup((SpaceId)i)), Is.False);
            Assert.That(rig.Registry.IsGroupActive("render"), Is.True);
        }
    }
}
