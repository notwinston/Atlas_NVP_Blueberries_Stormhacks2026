using System;
using System.Collections.Generic;
using Atlas.App.Menu;
using Atlas.Input;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.App.Tests
{
    /// <summary>
    /// The gaze twins of the four SpaceLab actions (task P1-C10, Master Plan section 0.5 rule 5; lane C contract
    /// section 13.1): the home beacon, the previous and next signs, the exhibit sign. The real input core runs,
    /// with the same dwell as in the headset; only the devices are absent.
    /// </summary>
    public class SpaceSignsTests
    {
        const float Dt = 1f / 60f;
        static readonly float3 Head = new float3(0.3f, 1.6f, -0.4f);

        /// <summary>The engine-free input core behind the service's interface: no devices, gaze and contexts only.</summary>
        sealed class CoreInput : IAtlasInput
        {
            public readonly AtlasInputCore Core = new AtlasInputCore();
            public void Tick(Vector3 headPosition, Quaternion headRotation, float deltaTime)
            {
                Core.Tick(default(RawInput), new float3(headPosition.x, headPosition.y, headPosition.z),
                    new quaternion(headRotation.x, headRotation.y, headRotation.z, headRotation.w), deltaTime);
            }
            public InputContext Context { get { return Core.Context; } }
            public void PushContext(InputContext context) { Core.PushContext(context); }
            public void PopContext(InputContext context) { Core.PopContext(context); }
            public bool Pressed(AtlasAction action) { return Core.Pressed(action); }
            public bool Held(AtlasAction action) { return Core.Held(action); }
            public float Axis(AtlasAxis axis) { return Core.Axis(axis); }
            public InputSource LastSource(AtlasAction action) { return Core.LastSource(action); }
            public int LastTag(AtlasAction action) { return Core.LastTag(action); }
            public event Action<AtlasAction, InputSource, int> ActionFired
            {
                add { Core.ActionFired += value; }
                remove { Core.ActionFired -= value; }
            }
            public float DwellSeconds { get { return Core.DwellSeconds; } set { Core.DwellSeconds = value; } }
            public GazeTargetId RegisterGazeTarget(in GazeTarget target)
            {
                GazeTargetData data = target.ToData();
                return Core.Targets.Add(in data);
            }
            public void MoveGazeTarget(GazeTargetId id, Vector3 centre) { Core.Targets.Move(id, new float3(centre.x, centre.y, centre.z)); }
            public void SetGazeTargetEnabled(GazeTargetId id, bool enabled) { Core.Targets.SetEnabled(id, enabled); }
            public void UnregisterGazeTarget(GazeTargetId id) { Core.Targets.Remove(id); }
            public GazeState Gaze { get { return new GazeState { Focused = Core.GazeFocused, Progress = Core.GazeProgress }; } }
            public void CopyActionsWithoutGazeTwin(List<AtlasAction> into) { Core.CopyActionsWithoutGazeTwin(into); }
            public bool TryGetTap(out Vector2 screenPosition) { screenPosition = default(Vector2); return false; }
            public bool GamepadPresent { get { return false; } }
            public void SetRumble(float lowFrequency, float highFrequency) { }
            public WalkerInput Walker { get { return default(WalkerInput); } }
        }

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

        sealed class View : ISpaceSignView
        {
            public bool Visible;
            public int Places;
            public readonly bool[] Available = new bool[SpaceSignLayout.Count];
            public void SetVisible(bool visible) { Visible = visible; }
            public void SetAvailable(int sign, bool available) { Available[sign] = available; }
            public void Place(float3 head, quaternion frame) { Places++; }
        }

        /// <summary>The frame loop's steps in their order: input at 1, upload at 6, the SpaceLab actions and then the signs at 8b.</summary>
        sealed class Rig
        {
            public readonly CoreInput Input = new CoreInput();
            public readonly Host Host = new Host();
            public readonly ParamRegistry Registry = new ParamRegistry();
            public readonly SpaceActions Actions;
            public readonly SpaceSigns Signs;
            readonly SpaceActionStep _step;
            public readonly View View = new View();
            public int MenuOpened;
            public bool InHand;
            public int Frame;

            public Rig(params SpaceId[] available)
            {
                for (int i = 0; i < available.Length; i++) Host.Available[(int)available[i]] = true;
                Host.Current = available.Length > 0 ? available[0] : SpaceId.Home;
                Registry.DeclareBool(CommandRouter.ParamDemoLock, "app", false);
                Actions = new SpaceActions(Host, Registry) { InHand = () => InHand, OpenSpaceMenu = () => MenuOpened++ };
                Input.PushContext(InputContext.Space);
                Signs = new SpaceSigns(Input, Actions, Host, View);
                _step = new SpaceActionStep(Input, Actions);
            }

            public void Step(quaternion rotation, int frames = 1)
            {
                for (int i = 0; i < frames; i++)
                {
                    var f = new FrameContext
                    {
                        FrameIndex = Frame++, DeltaTime = Dt, RawHeadPosition = Head, RawHeadRotation = rotation,
                        HeadPosition = Head, HeadRotation = rotation
                    };
                    Input.Tick((Vector3)Head, (Quaternion)rotation, Dt);
                    Signs.Upload(in f);
                    _step.AfterFrame(in f);
                    Signs.AfterFrame(in f);
                }
            }

            /// <summary>The head turned straight at a sign, where the signs have stood since the first frame.</summary>
            public quaternion LookAt(int sign)
            {
                float3 offset = SpaceSignLayout.Centre(sign);
                return quaternion.LookRotation(math.normalize(offset), new float3(0f, 1f, 0f));
            }

            /// <summary>One frame looking level, so the signs take the walker's heading; then the dwell on a sign.</summary>
            public void Settle()
            {
                Step(quaternion.identity, 3);
            }
        }

        static Rig Three()
        {
            var w = new Rig(SpaceId.Home, SpaceId.Glued, SpaceId.Portal);
            w.Host.Current = SpaceId.Glued;
            return w;
        }

        static int FramesFor(float seconds) { return (int)Math.Round(seconds / Dt); }

        // ---- the layout ----

        [Test]
        public void TheFourSigns_AreTheFourActionsAndStandApart()
        {
            Assert.That(SpaceSignLayout.ActionOf(SpaceSignLayout.Home), Is.EqualTo(AtlasAction.ReturnHome));
            Assert.That(SpaceSignLayout.ActionOf(SpaceSignLayout.Next), Is.EqualTo(AtlasAction.NextSpace));
            Assert.That(SpaceSignLayout.ActionOf(SpaceSignLayout.Previous), Is.EqualTo(AtlasAction.PrevSpace));
            Assert.That(SpaceSignLayout.ActionOf(SpaceSignLayout.Menu), Is.EqualTo(AtlasAction.SpaceMenu));

            for (int a = 0; a < SpaceSignLayout.Count; a++)
            {
                Assert.That(SpaceSignLayout.TextOf(a), Is.Not.Empty);
                for (int b = a + 1; b < SpaceSignLayout.Count; b++)
                    Assert.That(math.distance(SpaceSignLayout.Centre(a), SpaceSignLayout.Centre(b)), Is.GreaterThan(2f * SpaceSignLayout.Radius),
                        "signs " + a + " and " + b + " must not overlap");
                Assert.That(math.distance(SpaceSignLayout.Centre(a), MenuLayout.OpenChip), Is.GreaterThan(SpaceSignLayout.Radius + MenuLayout.OpenChipRadius),
                    "sign " + a + " clear of the menu chip");
                // Low in the view, and within the view: looking down by 15 to 35 degrees, and not far to a side.
                float3 c = SpaceSignLayout.Centre(a);
                float pitch = math.degrees(math.atan2(-c.y, c.z));
                Assert.That(pitch, Is.InRange(15f, 35f), "pitch below the horizon of sign " + a);
                Assert.That(math.abs(math.degrees(math.atan2(c.x, c.z))), Is.LessThan(20f));
                Assert.That(SpaceSignLayout.RevealPitch, Is.GreaterThan(math.radians(-pitch)), "revealed before the gaze can reach it");
            }
        }

        // ---- every action fires from its twin, after the dwell and not before ----

        [Test]
        public void EachSign_FiresItsAction_AfterTheDwell_AndNotBefore()
        {
            for (int sign = 0; sign < SpaceSignLayout.Count; sign++)
            {
                Rig w = Three();
                w.Settle();
                Assert.That(w.Signs.IsLive(sign), Is.True, "sign " + sign + " is live in a running space");
                quaternion look = w.LookAt(sign);

                // 0.6 s of looking: the dwell is 0.7 s, so nothing has happened.
                w.Step(look, FramesFor(0.6f));
                Assert.That(w.Host.Calls, Is.Empty, "sign " + sign + " not before the dwell");
                Assert.That(w.MenuOpened, Is.EqualTo(0));
                Assert.That(w.Input.Gaze.Focused, Is.EqualTo(w.Signs.TargetOf(sign)), "the gaze is on sign " + sign);

                w.Step(look, FramesFor(0.25f));
                switch (sign)
                {
                    case SpaceSignLayout.Home:
                        Assert.That(w.Host.Calls, Is.EqualTo(new[] { "home" }));
                        break;
                    case SpaceSignLayout.Next:
                        Assert.That(w.Host.Calls, Is.EqualTo(new[] { "switch " + SpaceIds.Code(SpaceId.Portal) }));
                        break;
                    case SpaceSignLayout.Previous:
                        Assert.That(w.Host.Calls, Is.EqualTo(new[] { "switch " + SpaceIds.Code(SpaceId.Home) }));
                        break;
                    default:
                        Assert.That(w.Host.Calls, Is.Empty);
                        Assert.That(w.MenuOpened, Is.EqualTo(1));
                        break;
                }
                Assert.That(w.Input.LastSource(SpaceSignLayout.ActionOf(sign)), Is.EqualTo(InputSource.Gaze));
            }
        }

        [Test]
        public void TheDwellIsTheInputServices_NotTheSigns()
        {
            Rig w = Three();
            w.Input.DwellSeconds = 0.8f;
            w.Settle();
            quaternion look = w.LookAt(SpaceSignLayout.Home);
            w.Step(look, FramesFor(0.75f));
            Assert.That(w.Host.Calls, Is.Empty);
            w.Step(look, FramesFor(0.1f));
            Assert.That(w.Host.Calls, Is.EqualTo(new[] { "home" }));
        }

        [Test]
        public void LookingAwayFromASign_BeforeTheDwellEnds_FiresNothing()
        {
            Rig w = Three();
            w.Settle();
            w.Step(w.LookAt(SpaceSignLayout.Next), FramesFor(0.5f));
            w.Step(quaternion.identity, FramesFor(1.0f));
            Assert.That(w.Host.Calls, Is.Empty);
        }

        // ---- a sign is live exactly when the action would not be refused ----

        [Test]
        public void TheSigns_FollowWhatTheActionsAccept()
        {
            Rig w = Three();
            w.Settle();
            for (int i = 0; i < SpaceSignLayout.Count; i++) Assert.That(w.Signs.IsLive(i), Is.True);
            Assert.That(w.View.Available, Is.All.True, "the view shows them as able");

            w.InHand = true;
            w.Step(quaternion.identity);
            for (int i = 0; i < SpaceSignLayout.Count; i++) Assert.That(w.Signs.IsLive(i), Is.False, "in the hand: sign " + i);
            w.InHand = false;

            foreach (SpaceHostState state in new[] { SpaceHostState.FadingOut, SpaceHostState.Loading, SpaceHostState.FadingIn })
            {
                w.Host.State = state;
                w.Step(quaternion.identity);
                for (int i = 0; i < SpaceSignLayout.Count; i++) Assert.That(w.Signs.IsLive(i), Is.False, state + ": sign " + i);
            }
            w.Host.State = SpaceHostState.Running;

            w.Registry.Set(CommandRouter.ParamDemoLock, 1f, ParamSource.Remote);
            w.Step(quaternion.identity);
            for (int i = 0; i < SpaceSignLayout.Count; i++) Assert.That(w.Signs.IsLive(i), Is.False, "demo lock: sign " + i);
            w.Registry.Set(CommandRouter.ParamDemoLock, 0f, ParamSource.Remote);
            w.Step(quaternion.identity);
            for (int i = 0; i < SpaceSignLayout.Count; i++) Assert.That(w.Signs.IsLive(i), Is.True, "lock off again: sign " + i);
        }

        [Test]
        public void ARefusedSign_CannotBeDwelt()
        {
            Rig w = Three();
            w.Settle();
            w.Registry.Set(CommandRouter.ParamDemoLock, 1f, ParamSource.Remote);
            w.Step(quaternion.identity);
            w.Step(w.LookAt(SpaceSignLayout.Home), FramesFor(1.2f));
            Assert.That(w.Host.Calls, Is.Empty);
            Assert.That(w.Input.Gaze.Focused.IsValid, Is.False, "a disabled target takes no focus");
        }

        [Test]
        public void WithOneSpaceInTheBuild_OnlyTheBeaconAndTheMenuAreLive()
        {
            var w = new Rig(SpaceId.Home);
            w.Settle();
            Assert.That(w.Signs.IsLive(SpaceSignLayout.Home), Is.True);
            Assert.That(w.Signs.IsLive(SpaceSignLayout.Menu), Is.True);
            Assert.That(w.Signs.IsLive(SpaceSignLayout.Next), Is.False, "no other space to step to");
            Assert.That(w.Signs.IsLive(SpaceSignLayout.Previous), Is.False);
        }

        [Test]
        public void WithNoMenuInstalled_TheExhibitSignIsNotLive()
        {
            Rig w = Three();
            w.Actions.OpenSpaceMenu = null;
            w.Settle();
            Assert.That(w.Signs.IsLive(SpaceSignLayout.Menu), Is.False);
            Assert.That(w.Signs.IsLive(SpaceSignLayout.Home), Is.True);
        }

        [Test]
        public void InTheMenu_TheSignsStandDownAndAreNotDrawn()
        {
            Rig w = Three();
            w.Settle();
            w.Input.PushContext(InputContext.Menu);
            w.Step(w.LookAt(SpaceSignLayout.Home), FramesFor(1.0f));
            Assert.That(w.Host.Calls, Is.Empty, "targets of the Space context are silent while a menu is open");
            Assert.That(w.View.Visible, Is.False);
        }

        // ---- rule 5 ----

        [Test]
        public void EveryActionTheStepReads_HasAnEnabledGazeTwin()
        {
            Rig w = Three();
            w.Settle();
            w.Step(quaternion.identity, 3);

            var missing = new List<AtlasAction>();
            w.Input.CopyActionsWithoutGazeTwin(missing);
            Assert.That(missing, Is.Empty, "without a gaze twin: " + string.Join(", ", missing));

            // And the report is not vacuous: with the signs gone the four are listed.
            w.Signs.Shutdown();
            w.Step(quaternion.identity, 3);
            w.Input.CopyActionsWithoutGazeTwin(missing);
            Assert.That(missing, Is.EquivalentTo(new[]
                { AtlasAction.ReturnHome, AtlasAction.NextSpace, AtlasAction.PrevSpace, AtlasAction.SpaceMenu }));
        }

        [Test]
        public void TheTwinTable_HasNoActionWithoutAGamepadAndAGazePath()
        {
            var actions = new List<AtlasAction>();
            var axes = new List<AtlasAxis>();
            Assert.That(InputTwins.IsComplete(actions, axes), Is.True, "without both: " + string.Join(", ", actions) + " " + string.Join(", ", axes));
            foreach (AtlasAction action in new[] { AtlasAction.ReturnHome, AtlasAction.NextSpace, AtlasAction.PrevSpace, AtlasAction.SpaceMenu, AtlasAction.DialLeft, AtlasAction.DialRight })
            {
                Assert.That(InputTwins.HasGamepadRoute(action), Is.True, action + " gamepad");
                Assert.That(InputTwins.HasGazeRoute(action), Is.True, action + " gaze");
            }
        }

        // ---- the drawing ----

        [Test]
        public void TheSigns_AreDrawnOnlyWhileTheWalkerLooksDown()
        {
            Rig w = Three();
            w.Settle();
            Assert.That(w.View.Visible, Is.False, "level: not in the way");

            w.Step(quaternion.RotateX(math.radians(25f)), 2);   // pitched down
            Assert.That(w.View.Visible, Is.True);
            Assert.That(w.View.Places, Is.GreaterThan(0));

            w.Step(quaternion.identity, 30);   // past the focus grace of the gaze
            Assert.That(w.View.Visible, Is.False);
        }

        [Test]
        public void TheSigns_HoldStill_WhileTheyAreLookedAt()
        {
            Rig w = Three();
            w.Settle();
            quaternion look = w.LookAt(SpaceSignLayout.Next);
            // A sign the walker is dwelling on must not slip away: the focus holds for the whole dwell.
            w.Step(look, FramesFor(0.2f));
            GazeTargetId id = w.Signs.TargetOf(SpaceSignLayout.Next);
            for (int i = 0; i < FramesFor(0.4f); i++)
            {
                w.Step(look);
                Assert.That(w.Input.Gaze.Focused, Is.EqualTo(id));
            }
        }

        [Test]
        public void Shutdown_RemovesTheTargetsAndHidesTheSigns()
        {
            Rig w = Three();
            w.Settle();
            w.Step(quaternion.RotateX(math.radians(25f)), 2);
            Assert.That(w.View.Visible, Is.True);
            w.Signs.Shutdown();
            Assert.That(w.View.Visible, Is.False);
            Assert.That(w.Input.Core.Targets.HasEnabled(AtlasAction.ReturnHome, InputContext.Space), Is.False);
            Assert.That(() => w.Signs.Shutdown(), Throws.Nothing, "twice is harmless");
            Assert.That(() => w.Step(quaternion.identity), Throws.Nothing);
        }
    }
}
