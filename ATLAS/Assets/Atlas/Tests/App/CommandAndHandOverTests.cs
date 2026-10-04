using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Safety;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.App.Tests
{
    /// <summary>The six operator commands as local calls (Master Plan section 7.3.2).</summary>
    public class CommandRouterTests
    {
        sealed class Rig
        {
            public readonly FakeHost Host = new FakeHost();
            public readonly FakeLoop Loop = new FakeLoop();
            public readonly FakePipeline Pipeline = new FakePipeline();
            public readonly ParamRegistry Registry = new ParamRegistry();
            public readonly List<string> Seen = new List<string>();
            public int Cues;
            public readonly CommandRouter Router;

            public Rig(bool realCue = false)
            {
                Registry.DeclareInt(CommandRouter.ParamPinTier, "render", -1, 4, -1, v => Seen.Add("pin " + v));
                Registry.DeclareBool(CommandRouter.ParamDemoLock, "app", false, v => Seen.Add("lock " + v));
                Registry.DeclareEnum("glued.mode", "glued", new[] { "T2", "Klein", "T3" }, 0, v => Seen.Add("mode " + v));
                Router = realCue
                    ? new CommandRouter(Host, Loop, Registry, Pipeline)
                    : new CommandRouter(Host, Loop, Registry, Pipeline, () => Cues++);
            }
        }

        [Test]
        public void EachCommand_MapsToItsLocalCall()
        {
            var rig = new Rig();
            rig.Router.SwitchSpace(SpaceId.Portal, null);
            rig.Router.Home();
            rig.Router.Recentre();
            rig.Router.SetQuality(2);
            rig.Router.Cue();
            rig.Router.SetLock(true);

            Assert.That(rig.Host.Calls, Is.EqualTo(new[] { "switch S5", "home" }));
            Assert.That(rig.Loop.Calls, Is.EqualTo(new[] { "reset" }));
            Assert.That(rig.Pipeline.Resets, Is.EqualTo(1), "recentre also restarts the geometry stream");
            Assert.That(rig.Seen, Is.EqualTo(new[] { "pin 2", "lock True" }));
            Assert.That(rig.Cues, Is.EqualTo(1));
        }

        [Test]
        public void SwitchSpace_SetsTheVariant_First()
        {
            var rig = new Rig();
            rig.Router.SwitchSpace(SpaceId.Glued, "klein");
            Assert.That(rig.Seen, Is.EqualTo(new[] { "mode 1" }));
            Assert.That(rig.Host.Calls, Is.EqualTo(new[] { "switch S1" }));
            rig.Router.SwitchSpace(SpaceId.Glued, "2");
            Assert.That(rig.Registry.GetInt("glued.mode"), Is.EqualTo(2));
            // A space whose variant parameter is not declared ignores a variant and is still switched to.
            rig.Router.SwitchSpace(SpaceId.Slicer, "anything");
            Assert.That(rig.Host.Calls[rig.Host.Calls.Count - 1], Is.EqualTo("switch S6"));
            // So does a space that has no variants at all.
            rig.Router.SwitchSpace(SpaceId.SmallPlanet, "1");
            Assert.That(rig.Host.Calls[rig.Host.Calls.Count - 1], Is.EqualTo("switch S4"));
        }

        [Test]
        public void SwitchSpace_SetsEachSpacesOwnVariantParameter()
        {
            // Lane A contract section 14: booth / loop are rooms.layout, the bodies are slicer.object.
            var rig = new Rig();
            rig.Registry.DeclareEnum("rooms.layout", "rooms", new[] { "booth", "loop" }, 0, null, true);
            rig.Registry.DeclareEnum("slicer.object", "slicer", new[] { "tesseract", "cell5", "cell16", "cell24", "tube", "ball" }, 0, null, true);

            rig.Router.SwitchSpace(SpaceId.Portal, "loop");
            Assert.That(rig.Registry.GetInt("rooms.layout"), Is.EqualTo(1));
            rig.Router.SwitchSpace(SpaceId.Slicer, "cell16");
            Assert.That(rig.Registry.GetInt("slicer.object"), Is.EqualTo(2));
            rig.Router.SwitchSpace(SpaceId.Slicer, "TUBE");
            Assert.That(rig.Registry.GetInt("slicer.object"), Is.EqualTo(4), "names match without regard to case");
            rig.Router.SwitchSpace(SpaceId.Portal, "0");
            Assert.That(rig.Registry.GetInt("rooms.layout"), Is.EqualTo(0), "or by index");
            rig.Router.SwitchSpace(SpaceId.Portal, "no-such-layout");
            Assert.That(rig.Registry.GetInt("rooms.layout"), Is.EqualTo(0), "an unknown name changes nothing");
            Assert.That(rig.Registry.GetInt("glued.mode"), Is.EqualTo(0), "and no other space's parameter is touched");
            Assert.That(rig.Host.Calls, Is.EqualTo(new[] { "switch S5", "switch S6", "switch S6", "switch S5", "switch S5" }));
        }

        [Test]
        public void SetQuality_IsClamped_AndAutoIsMinusOne()
        {
            var rig = new Rig();
            rig.Router.SetQuality(9);
            Assert.That(rig.Registry.GetInt(CommandRouter.ParamPinTier), Is.EqualTo(4));
            rig.Router.SetQuality(-5);
            Assert.That(rig.Registry.GetInt(CommandRouter.ParamPinTier), Is.EqualTo(-1));
        }

        [Test]
        public void Cue_IsTheOperatorStop_HeldAgainstBumpGuardsOwnClear()
        {
            BumpCue.Initialise((IBumpCueView)null, null);
            try
            {
                var rig = new Rig(true);
                rig.Router.Cue();
                Assert.That(BumpCue.Shown, Is.EqualTo(BumpLevel.Strong));

                // Bump Guard reports Clear every frame; the operator's cue stays for its hold.
                for (int i = 0; i < 60; i++)
                {
                    BumpCue.Fire(BumpLevel.Clear, float2.zero, CueSource.Guard);
                    BumpCue.Advance(10.0 + i / 60.0, 1f / 60f);
                }
                Assert.That(BumpCue.Shown, Is.EqualTo(BumpLevel.Strong));

                for (int i = 60; i < 60 * 5; i++)
                {
                    BumpCue.Fire(BumpLevel.Clear, float2.zero, CueSource.Guard);
                    BumpCue.Advance(10.0 + i / 60.0, 1f / 60f);
                }
                Assert.That(BumpCue.Shown, Is.EqualTo(BumpLevel.Clear));
            }
            finally
            {
                BumpCue.Shutdown();
            }
        }
    }

    /// <summary>The room hand-over: anchor, room frame, Bump Guard, room content, then the entry space.</summary>
    public class RoomHandOverTests
    {
        const float Dt = 1f / 60f;

        sealed class Rig
        {
            public readonly List<string> Log = new List<string>();
            public readonly ParamRegistry Registry = new ParamRegistry();
            public readonly FlatWalkerKernel Kernel = new FlatWalkerKernel();
            public readonly FakePipeline Pipeline = new FakePipeline();
            public readonly FakeStage Stage = new FakeStage();
            public readonly FakeEvents Events = new FakeEvents();
            public readonly FakeSense Pose;
            public readonly FakeLoader Loader;
            public readonly FrameLoopCore Loop;
            public readonly SpaceLab Lab;
            public readonly RoomHandOver HandOver;
            double _time;

            public Rig(params SpaceId[] available)
            {
                Loader = new FakeLoader(Log, available);
                Loop = new FrameLoopCore(Kernel) { Pipeline = Pipeline };
                Pose = new FakeSense(null, "pose") { Position = new float3(4f, 1.6f, 4f) };
                Loop.AddSense(Pose);
                Lab = new SpaceLab(Registry, Kernel, Loop, Loader, Stage) { Events = Events };
                Loop.Space = Lab;
                Loop.Host = Lab;
                HandOver = new RoomHandOver(Lab, Loop, anchor => Log.Add("anchor " + (anchor ?? "none"))) { Events = Events };

                // The installers' subscriptions, in creation order: rows 11, 12, 13 and 18.
                Lab.RoomChanged += room => { Pipeline.Room = null; PendingRoom = room; Log.Add("room frame"); };
                Lab.RoomChanged += room => Log.Add("bump guard");
                Lab.RoomChanged += room => Log.Add("room content");
                Lab.RoomChanged += room => Log.Add("link");
                Loop.AddAfterFrame(HandOver);
                Loop.AddAfterFrame(Lab);
            }

            /// <summary>The room the pipeline applies from its next read.</summary>
            public RoomModel PendingRoom;

            public void Frame()
            {
                // The pose pipeline puts the head in the room frame from the frame after the room was set.
                if (PendingRoom != null)
                {
                    Pipeline.Room = PendingRoom;
                    PendingRoom = null;
                    Pose.Position = new float3(0.25f, 1.6f, 0.5f);
                }
                _time += Dt;
                Loop.RunUpdate(_time, Dt);
                Loop.RunState();
                Loop.RunAfterFrame();
            }
        }

        [Test]
        public void HandOver_Order_AnchorRoomFrameGuardContent_ThenTheEntrySpace()
        {
            var rig = new Rig(SpaceId.Home);
            rig.Frame();
            rig.HandOver.Activate(Rooms.Make("scan", 2f), "arAnchor");

            Assert.That(rig.Log, Is.EqualTo(new[] { "anchor arAnchor", "room frame", "bump guard", "room content", "link" }));
            Assert.That(rig.Lab.Room.Id, Is.EqualTo("scan"));
            Assert.That(rig.Lab.State, Is.EqualTo(SpaceHostState.Empty), "no space is asked for in the frame of SetRoom");
            Assert.That(rig.Loop.RoomResetPending, Is.True);
            Assert.That(rig.HandOver.EnterPending, Is.True);
            Assert.That(rig.Events.Has("room.active id=scan"), Is.True);

            rig.Frame();
            Assert.That(rig.Loop.RoomResetPending, Is.False, "the walker was placed in the first frame of the room frame");
            Assert.That(rig.Kernel.Pose.Local.M02, Is.EqualTo(0.25).Within(1e-6));
            Assert.That(rig.Kernel.Pose.Local.M12, Is.EqualTo(0.5).Within(1e-6));
            Assert.That(rig.Log.Contains("configure S0"), Is.True);
            Assert.That(rig.Log.IndexOf("configure S0"), Is.GreaterThan(rig.Log.IndexOf("link")));
            Assert.That(rig.Log[rig.Log.Count - 1], Is.EqualTo("enter S0"));
            Assert.That(rig.Lab.HasSpace, Is.True);
            Assert.That(rig.Lab.Current, Is.EqualTo(SpaceId.Home));
            Assert.That(rig.HandOver.EnterPending, Is.False);

            for (int i = 0; i < 30; i++) rig.Frame();
            Assert.That(rig.Lab.State, Is.EqualTo(SpaceHostState.Running));
            Assert.That(rig.Loop.LastState.SpaceId, Is.EqualTo(0));
            Assert.That(rig.Loop.LastState.Sheet, Is.EqualTo(10), "the module's part of the State");
        }

        [Test]
        public void HandOver_WaitsWhileTheHeadIsNotInTheRoomFrame()
        {
            var rig = new Rig(SpaceId.Home);
            rig.Frame();
            rig.HandOver.Activate(Rooms.Make("late"));
            RoomModel held = rig.PendingRoom;
            rig.PendingRoom = null;                 // the pipeline has not taken the room yet
            for (int i = 0; i < 5; i++) rig.Frame();
            Assert.That(rig.Lab.HasSpace, Is.False);
            Assert.That(rig.Lab.State, Is.EqualTo(SpaceHostState.Empty));
            Assert.That(rig.Loader.Created.Count, Is.EqualTo(0));

            rig.PendingRoom = held;
            rig.Frame();
            Assert.That(rig.Lab.HasSpace, Is.True);
        }

        [Test]
        public void HandOver_WithASpaceEntered_ReloadsIt_AndAsksForNoOther()
        {
            var rig = new Rig(SpaceId.Home, SpaceId.Glued);
            rig.HandOver.Activate(Rooms.Make("one"));
            for (int i = 0; i < 40; i++) rig.Frame();
            rig.Lab.RequestSwitch(SpaceId.Glued);
            for (int i = 0; i < 60; i++) rig.Frame();
            Assert.That(rig.Lab.Current, Is.EqualTo(SpaceId.Glued));

            rig.HandOver.Activate(Rooms.Make("two"));
            Assert.That(rig.HandOver.EnterPending, Is.False);
            for (int i = 0; i < 60; i++) rig.Frame();
            Assert.That(rig.Lab.Current, Is.EqualTo(SpaceId.Glued));
            Assert.That(((FakeModule)rig.Lab.Module).Room.Id, Is.EqualTo("two"));
        }

        [Test]
        public void HandOver_ForOnboarding_EntersOnlyWhenAsked_AndOtherEntrySpace()
        {
            var rig = new Rig(SpaceId.Home, SpaceId.Slicer);
            rig.HandOver.AutoEnter = false;
            rig.HandOver.EntrySpace = SpaceId.Slicer;
            Assert.That(rig.HandOver.EnterNow(), Is.False, "no room yet");
            rig.HandOver.Activate(Rooms.Make("prompt"));
            for (int i = 0; i < 10; i++) rig.Frame();
            Assert.That(rig.Lab.HasSpace, Is.False, "onboarding has not shown its headset prompt yet");

            Assert.That(rig.HandOver.EnterNow(), Is.True);
            for (int i = 0; i < 40; i++) rig.Frame();
            Assert.That(rig.Lab.Current, Is.EqualTo(SpaceId.Slicer));
        }

        [Test]
        public void HandOver_NoEntrySpaceBuilt_StaysEmpty_AndSaysSo()
        {
            var rig = new Rig();
            rig.HandOver.Activate(Rooms.Make("bare"));
            for (int i = 0; i < 10; i++) rig.Frame();
            Assert.That(rig.Lab.State, Is.EqualTo(SpaceHostState.Empty));
            Assert.That(rig.Events.Has("room.noEntrySpace space=S0"), Is.True);
        }
    }
}
