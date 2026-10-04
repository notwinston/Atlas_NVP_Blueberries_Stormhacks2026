using System;
using System.Collections.Generic;
using Atlas.Input;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;

namespace Atlas.App.Tests
{
    /// <summary>The SpaceLab actions of the action map as local calls (Master Plan section 11.3, gate G5).</summary>
    public class SpaceActionsTests
    {
        sealed class Host : ISpaceHost
        {
            public readonly List<string> Calls = new List<string>();
            public readonly bool[] Available = new bool[SpaceIds.Count];
            public SpaceHostState State { get; set; } = SpaceHostState.Running;
            public bool HasSpace { get; set; } = true;
            public SpaceId Current { get; set; }
            public ISpaceModule Module { get { return null; } }
            public RoomModel Room { get { return null; } }
            public bool Standing { get { return false; } }
            public ushort WorldVersion { get { return 0; } }
            public bool IsAvailable(SpaceId id) { return SpaceIds.IsDefined(id) && Available[(int)id]; }
            public bool RequestSwitch(SpaceId target) { Calls.Add("switch " + SpaceIds.Code(target)); return true; }
            public bool ReturnHome() { Calls.Add("home"); return true; }
            public void SetRoom(RoomModel room) { }
            public void BumpWorldVersion() { }
#pragma warning disable 67
            public event Action<SpaceId> SpaceEntered;
            public event Action<SpaceId> SpaceExited;
            public event Action<RoomModel> RoomChanged;
#pragma warning restore 67
        }

        sealed class Rig
        {
            public readonly Host Host = new Host();
            public readonly ParamRegistry Registry = new ParamRegistry();
            public readonly SpaceActions Actions;
            public int MenuOpened;
            public bool InHand;

            public Rig(params SpaceId[] available)
            {
                for (int i = 0; i < available.Length; i++) Host.Available[(int)available[i]] = true;
                Registry.DeclareBool(CommandRouter.ParamDemoLock, "app", false);
                Actions = new SpaceActions(Host, Registry)
                {
                    InHand = () => InHand,
                    OpenSpaceMenu = () => MenuOpened++,
                };
            }
        }

        [Test]
        public void EachAction_MapsToItsCall()
        {
            var rig = new Rig(SpaceId.Home, SpaceId.Glued, SpaceId.Portal);
            rig.Host.Current = SpaceId.Glued;

            Assert.That(rig.Actions.Do(AtlasAction.ReturnHome, InputContext.Space), Is.True);
            Assert.That(rig.Actions.Do(AtlasAction.NextSpace, InputContext.Space), Is.True);
            Assert.That(rig.Actions.Do(AtlasAction.PrevSpace, InputContext.Space), Is.True);
            Assert.That(rig.Actions.Do(AtlasAction.SpaceMenu, InputContext.Space), Is.True);
            Assert.That(rig.Host.Calls, Is.EqualTo(new[] { "home", "switch " + SpaceIds.Code(SpaceId.Portal), "switch " + SpaceIds.Code(SpaceId.Home) }));
            Assert.That(rig.MenuOpened, Is.EqualTo(1));

            // An action that is not SpaceLab's is left alone.
            Assert.That(rig.Actions.Do(AtlasAction.Confirm, InputContext.Space), Is.False);
            Assert.That(rig.Host.Calls.Count, Is.EqualTo(3));
        }

        [Test]
        public void Stepping_VisitsAllSevenSpacesInOrder_BothWays()
        {
            var rig = new Rig(SpaceId.Home, SpaceId.Glued, SpaceId.CurvedWalk, SpaceId.AlignedCells, SpaceId.SmallPlanet,
                SpaceId.Portal, SpaceId.Slicer);
            SpaceId target;
            rig.Host.Current = SpaceId.Home;
            for (int i = 1; i <= SpaceIds.Count; i++)
            {
                Assert.That(SpaceActions.TryNeighbour(rig.Host, 1, out target), Is.True);
                Assert.That(target, Is.EqualTo((SpaceId)(i % SpaceIds.Count)), "S0 to S6 and round to Home");
                rig.Host.Current = target;
            }
            for (int i = SpaceIds.Count - 1; i >= 0; i--)
            {
                Assert.That(SpaceActions.TryNeighbour(rig.Host, -1, out target), Is.True);
                Assert.That(target, Is.EqualTo((SpaceId)i));
                rig.Host.Current = target;
            }
        }

        [Test]
        public void Stepping_SkipsUnavailableSpaces_AndWraps()
        {
            var rig = new Rig(SpaceId.Home, SpaceId.Glued, SpaceId.Portal);
            SpaceId target;

            rig.Host.Current = SpaceId.Home;
            Assert.That(SpaceActions.TryNeighbour(rig.Host, 1, out target), Is.True);
            Assert.That(target, Is.EqualTo(SpaceId.Glued));
            Assert.That(SpaceActions.TryNeighbour(rig.Host, -1, out target), Is.True);
            Assert.That(target, Is.EqualTo(SpaceId.Portal), "back from the first space wraps to the last one in the build");

            rig.Host.Current = SpaceId.Glued;
            Assert.That(SpaceActions.TryNeighbour(rig.Host, 1, out target), Is.True);
            Assert.That(target, Is.EqualTo(SpaceId.Portal), "the spaces between that this build lacks are skipped");

            rig.Host.Current = SpaceId.Portal;
            Assert.That(SpaceActions.TryNeighbour(rig.Host, 1, out target), Is.True);
            Assert.That(target, Is.EqualTo(SpaceId.Home), "forward from the last space wraps to Home");
            Assert.That(SpaceActions.TryNeighbour(rig.Host, -1, out target), Is.True);
            Assert.That(target, Is.EqualTo(SpaceId.Glued));

            // A full round in each direction visits exactly the spaces of the build.
            var seen = new List<SpaceId>();
            rig.Host.Current = SpaceId.Home;
            for (int i = 0; i < 3; i++)
            {
                Assert.That(SpaceActions.TryNeighbour(rig.Host, 1, out target), Is.True);
                seen.Add(target);
                rig.Host.Current = target;
            }
            Assert.That(seen, Is.EqualTo(new[] { SpaceId.Glued, SpaceId.Portal, SpaceId.Home }));

            // With no space entered the round starts from Home.
            rig.Host.HasSpace = false;
            rig.Host.Current = SpaceId.Portal;
            Assert.That(SpaceActions.TryNeighbour(rig.Host, 1, out target), Is.True);
            Assert.That(target, Is.EqualTo(SpaceId.Glued));
        }

        [Test]
        public void Stepping_WithOneSpaceInTheBuild_DoesNothing()
        {
            var rig = new Rig(SpaceId.Home);
            SpaceId target;
            Assert.That(SpaceActions.TryNeighbour(rig.Host, 1, out target), Is.False);
            Assert.That(SpaceActions.TryNeighbour(rig.Host, -1, out target), Is.False);
            Assert.That(rig.Actions.Do(AtlasAction.NextSpace, InputContext.Space), Is.False);
            Assert.That(rig.Host.Calls, Is.Empty);
        }

        static readonly AtlasAction[] s_all =
        {
            AtlasAction.ReturnHome, AtlasAction.NextSpace, AtlasAction.PrevSpace, AtlasAction.SpaceMenu
        };

        static void AssertAllRefused(Rig rig, InputContext context, string why)
        {
            for (int i = 0; i < s_all.Length; i++)
                Assert.That(rig.Actions.Do(s_all[i], context), Is.False, s_all[i] + ": " + why);
            Assert.That(rig.Host.Calls, Is.Empty, why);
            Assert.That(rig.MenuOpened, Is.EqualTo(0), why);
        }

        [Test]
        public void InTheHand_NothingFires()
        {
            var rig = new Rig(SpaceId.Home, SpaceId.Glued);
            AssertAllRefused(rig, InputContext.InHand, "the input context is in the hand");

            // Onboarding holds the phone in the hand whatever the context stack says.
            rig.InHand = true;
            AssertAllRefused(rig, InputContext.Space, "the display is in the hand");
            rig.InHand = false;
            Assert.That(rig.Actions.Do(AtlasAction.ReturnHome, InputContext.Space), Is.True);
        }

        [Test]
        public void DuringASwitch_NothingFires()
        {
            var rig = new Rig(SpaceId.Home, SpaceId.Glued);
            foreach (SpaceHostState state in new[] { SpaceHostState.FadingOut, SpaceHostState.Loading, SpaceHostState.FadingIn })
            {
                rig.Host.State = state;
                AssertAllRefused(rig, InputContext.Space, "a switch is under way (" + state + ")");
            }
            rig.Host.State = SpaceHostState.Running;
            Assert.That(rig.Actions.Do(AtlasAction.NextSpace, InputContext.Space), Is.True);
        }

        [Test]
        public void UnderTheDemoLock_NothingFires()
        {
            var rig = new Rig(SpaceId.Home, SpaceId.Glued);
            rig.Registry.Set(CommandRouter.ParamDemoLock, 1f, ParamSource.Remote);
            AssertAllRefused(rig, InputContext.Space, "the demo lock is on");
            AssertAllRefused(rig, InputContext.Menu, "the demo lock is on");

            rig.Registry.Set(CommandRouter.ParamDemoLock, 0f, ParamSource.Remote);
            Assert.That(rig.Actions.Do(AtlasAction.SpaceMenu, InputContext.Space), Is.True);
            Assert.That(rig.MenuOpened, Is.EqualTo(1));
        }

        [Test]
        public void TheLockDeclaredLater_IsStillSeen()
        {
            var host = new Host();
            host.Available[0] = true;
            var registry = new ParamRegistry();
            var actions = new SpaceActions(host, registry);
            Assert.That(actions.Do(AtlasAction.ReturnHome, InputContext.Space), Is.True, "no lock declared: not locked");
            registry.DeclareBool(CommandRouter.ParamDemoLock, "app", false);
            registry.Set(CommandRouter.ParamDemoLock, 1f, ParamSource.Remote);
            Assert.That(actions.Do(AtlasAction.ReturnHome, InputContext.Space), Is.False);
        }

        [Test]
        public void InTheMenu_OnlyTheSpaceMenuAnswers()
        {
            var rig = new Rig(SpaceId.Home, SpaceId.Glued);
            Assert.That(rig.Actions.Do(AtlasAction.ReturnHome, InputContext.Menu), Is.False);
            Assert.That(rig.Actions.Do(AtlasAction.NextSpace, InputContext.Menu), Is.False);
            Assert.That(rig.Actions.Do(AtlasAction.PrevSpace, InputContext.Menu), Is.False);
            Assert.That(rig.Host.Calls, Is.Empty);
            Assert.That(rig.Actions.Do(AtlasAction.SpaceMenu, InputContext.Menu), Is.True);
            Assert.That(rig.MenuOpened, Is.EqualTo(1));
        }

        [Test]
        public void WithNoMenuInstalled_TheSpaceMenuDoesNothing()
        {
            var rig = new Rig(SpaceId.Home);
            rig.Actions.OpenSpaceMenu = null;
            Assert.That(rig.Actions.Do(AtlasAction.SpaceMenu, InputContext.Space), Is.False);
        }
    }

    /// <summary>The development overlay's box on the screen: a press inside it is not the headset lever.</summary>
    public class DevOverlayLayoutTests
    {
        [Test]
        public void TheBox_IsInTheTopLeftCorner_AtTheReferenceHeight()
        {
            // 960 x 540: one unit is one pixel. Screen y runs up from the bottom.
            Assert.That(DevOverlayLayout.Contains(100f, 540f - 100f, 540f, false), Is.True);
            Assert.That(DevOverlayLayout.Contains(8f, 540f - 8f, 540f, false), Is.True, "the corner of the box");
            Assert.That(DevOverlayLayout.Contains(258f, 540f - 532f, 540f, false), Is.True, "the opposite corner");
            Assert.That(DevOverlayLayout.Contains(4f, 440f, 540f, false), Is.False, "left of the box");
            Assert.That(DevOverlayLayout.Contains(100f, 537f, 540f, false), Is.False, "above the box");
            Assert.That(DevOverlayLayout.Contains(100f, 4f, 540f, false), Is.False, "below the box");
            Assert.That(DevOverlayLayout.Contains(259f, 440f, 540f, false), Is.False, "right of the box");
            Assert.That(DevOverlayLayout.Contains(480f, 270f, 540f, false), Is.False, "the middle of the screen is the lever's");
            Assert.That(DevOverlayLayout.Contains(900f, 30f, 540f, false), Is.False);
        }

        [Test]
        public void TheBox_ScalesWithTheScreenHeight()
        {
            // 1080 pixels tall: the box is twice as large, 16 .. 516 across and 16 .. 1064 down.
            Assert.That(DevOverlayLayout.Contains(500f, 1080f - 1000f, 1080f, false), Is.True);
            Assert.That(DevOverlayLayout.Contains(520f, 1080f - 1000f, 1080f, false), Is.False);
            Assert.That(DevOverlayLayout.Contains(300f, 1080f - 300f, 1080f, false), Is.True);
            Assert.That(DevOverlayLayout.Contains(300f, 10f, 1080f, false), Is.False);
            Assert.That(DevOverlayLayout.Contains(960f, 540f, 1080f, false), Is.False);
        }

        [Test]
        public void TheWarningBox_IsOnlyItsFewLines()
        {
            Assert.That(DevOverlayLayout.Contains(100f, 540f - 40f, 540f, true), Is.True);
            Assert.That(DevOverlayLayout.Contains(100f, 540f - 100f, 540f, true), Is.False);
        }

        [Test]
        public void NoScreen_NoBox()
        {
            Assert.That(DevOverlayLayout.Contains(10f, 10f, 0f, false), Is.False);
        }
    }
}
