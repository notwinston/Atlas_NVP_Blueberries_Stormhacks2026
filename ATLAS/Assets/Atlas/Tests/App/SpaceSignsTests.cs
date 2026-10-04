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
            public bool Visible, Near;
            public int Places, Reaches;
            public float Scale = 1f;
            public float3 Origin;
            public quaternion Frame;
            public readonly bool[] Available = new bool[SpaceSignLayout.Count];
            public void SetVisible(bool visible) { Visible = visible; }
            public void SetAvailable(int sign, bool available) { Available[sign] = available; }
            public void Place(float3 origin, quaternion frame) { Places++; Origin = origin; Frame = frame; }
            public void SetReach(bool near, float scale) { Near = near; Scale = scale; Reaches++; }
        }

        /// <summary>The near layout as Atlas.Hands publishes it, with every number in the test's hands.</summary>
        sealed class Reach : IReachLayout
        {
            public bool Near { get; set; }
            public float3 Position { get; set; }
            public quaternion Rotation { get; set; }
            public float Scale { get; set; }
        }

        Reach _reach;
        IReachLayout _before;

        [SetUp]
        public void StartWithNoLayout()
        {
            // A layout another test left published would move every sign of this one.
            if (AtlasServices.TryGet(out _before)) AtlasServices.Unregister(_before);
            _reach = null;
        }

        [TearDown]
        public void PutTheLayoutBack()
        {
            if (_reach != null) AtlasServices.Unregister<IReachLayout>(_reach);
            if (_before != null) AtlasServices.Register(_before);
        }

        /// <summary>
        /// Publishes the near anchor of a hands session: 0.48 m from the head on a line 25 degrees below the
        /// horizontal, tilted to face the head (+Z away from the walker, +Y up along the panel).
        /// </summary>
        Reach Publish(bool near = true, float scale = 0.75f)
        {
            if (_reach != null) AtlasServices.Unregister<IReachLayout>(_reach);
            quaternion rotation = math.mul(quaternion.RotateY(math.radians(15f)), quaternion.RotateX(math.radians(25f)));
            _reach = new Reach { Near = near, Scale = scale, Rotation = rotation, Position = Head + math.mul(rotation, new float3(0f, 0f, 0.48f)) };
            AtlasServices.Register<IReachLayout>(_reach);
            return _reach;
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

        // ---- a hands session: the signs come within reach (documents/ATLAS_Hands_Design.md section 6.1) -----

        static readonly quaternion Down = quaternion.RotateX(math.radians(25f));

        static quaternion Toward(float3 target)
        {
            return quaternion.LookRotation(math.normalize(target - Head), new float3(0f, 1f, 0f));
        }

        static GazeTargetData Data(Rig w, int sign)
        {
            GazeTargetData data;
            Assert.That(w.Input.Core.Targets.TryGet(w.Signs.TargetOf(sign), out data), Is.True, "sign " + sign + " is registered");
            return data;
        }

        /// <summary>Everything a rig holds about where the signs stand, for comparing two runs number by number.</summary>
        static List<string> Snapshot(Rig w)
        {
            var lines = new List<string>
            {
                "visible " + w.View.Visible + " near " + w.View.Near + " scale " + w.View.Scale.ToString("R") + " at " + w.View.Origin + " " + w.View.Frame,
            };
            for (int i = 0; i < SpaceSignLayout.Count; i++)
            {
                GazeTargetData d = Data(w, i);
                lines.Add("sign " + i + " " + d.Action + " " + d.Shape + " " + d.Context + " tag " + d.Tag + " at " + d.Centre + " r " + d.Radius.ToString("R")
                    + " on " + w.Input.Core.Targets.IsEnabled(w.Signs.TargetOf(i)) + " live " + w.Signs.IsLive(i) + " able " + w.View.Available[i]);
            }
            return lines;
        }

        [Test]
        public void EachSign_HasItsOwnPlaceWithinReach()
        {
            Assert.That(SpaceSignLayout.SlotOf(SpaceSignLayout.Home), Is.EqualTo(ReachSlot.SignHome));
            Assert.That(SpaceSignLayout.SlotOf(SpaceSignLayout.Previous), Is.EqualTo(ReachSlot.SignPrevious));
            Assert.That(SpaceSignLayout.SlotOf(SpaceSignLayout.Menu), Is.EqualTo(ReachSlot.SignMenu));
            Assert.That(SpaceSignLayout.SlotOf(SpaceSignLayout.Next), Is.EqualTo(ReachSlot.SignNext));

            // As a metre away: the beacon in the middle, the three signs in a row under it, previous on the left.
            float3 home = ReachSlots.Centre(ReachSlot.SignHome);
            Assert.That(ReachSlots.Centre(ReachSlot.SignPrevious).x, Is.LessThan(home.x));
            Assert.That(ReachSlots.Centre(ReachSlot.SignNext).x, Is.GreaterThan(home.x));
            for (int i = 1; i < SpaceSignLayout.Count; i++)
                Assert.That(ReachSlots.Centre(SpaceSignLayout.SlotOf(i)).y, Is.LessThan(home.y), "sign " + i + " is under the beacon");

            // At the usual scale: no sign on another or on the menu chip, each within 20 cm of the anchor to
            // each side with its board, and none smaller than 3 cm.
            const float scale = 0.75f;
            float radius = ReachSlots.TargetHalf(SpaceSignLayout.Radius, scale);
            Assert.That(2f * radius, Is.GreaterThanOrEqualTo(ReachSlots.MinTargetMetres));
            float3 chip = ReachSlots.Local(MenuLayout.ChipSlot, scale);
            float chipRadius = ReachSlots.TargetHalf(MenuLayout.OpenChipRadius, scale);
            for (int a = 0; a < SpaceSignLayout.Count; a++)
            {
                float3 c = ReachSlots.Local(SpaceSignLayout.SlotOf(a), scale);
                for (int b = a + 1; b < SpaceSignLayout.Count; b++)
                    Assert.That(math.distance(c, ReachSlots.Local(SpaceSignLayout.SlotOf(b), scale)), Is.GreaterThan(2f * radius + 0.025f),
                        "signs " + a + " and " + b + " stand a fingertip's margin apart");
                Assert.That(math.distance(c, chip), Is.GreaterThan(radius + chipRadius + 0.025f), "sign " + a + " clear of the menu chip");
                Assert.That(math.abs(c.x) + scale * SpaceSignLayout.BoardHalfWidth, Is.LessThanOrEqualTo(0.20f), "sign " + a + " sideways");
                Assert.That(math.abs(c.y) + radius, Is.LessThanOrEqualTo(0.20f), "sign " + a + " up and down");
                Assert.That(ReachSlots.HidesWhileMenuOpen(SpaceSignLayout.SlotOf(a)), Is.True);
            }
        }

        [Test]
        public void Near_TheSignsStandAtTheirSlots_InTheAnchorsFrame_TimesTheScale()
        {
            Reach reach = Publish();
            Rig w = Three();
            w.Settle();
            w.Step(Down, 2);

            Assert.That(w.View.Visible, Is.True);
            Assert.That(w.View.Near, Is.True, "drawn on the world layer, depth-tested");
            Assert.That(w.View.Scale, Is.EqualTo(0.75f), "boards and text times the scale");
            Assert.That(w.View.Origin, Is.EqualTo(reach.Position), "the view stands in the anchor's frame");
            Assert.That(w.View.Frame, Is.EqualTo(reach.Rotation));
            for (int i = 0; i < SpaceSignLayout.Count; i++)
            {
                float3 slot = ReachSlots.Centre(SpaceSignLayout.SlotOf(i));
                float3 centre = reach.Position + math.mul(reach.Rotation, 0.75f * new float3(slot.x, slot.y, -slot.z));
                GazeTargetData d = Data(w, i);
                Assert.That(math.distance(d.Centre, centre), Is.LessThan(1e-6f), "sign " + i + ": " + d.Centre + " against " + centre);
                Assert.That(d.Centre, Is.EqualTo(ReachSlots.World(reach.Position, reach.Rotation, 0.75f, SpaceSignLayout.SlotOf(i))));
                Assert.That(d.Radius, Is.EqualTo(0.75f * SpaceSignLayout.Radius).Within(1e-7f), "its radius times the scale");
                Assert.That(2f * d.Radius, Is.GreaterThanOrEqualTo(ReachSlots.MinTargetMetres), "no target under 3 cm");
                Assert.That(d.Action, Is.EqualTo(SpaceSignLayout.ActionOf(i)));
                Assert.That(d.Tag, Is.EqualTo(i));
                Assert.That(d.Context, Is.EqualTo(InputContext.Space));
                Assert.That(w.Signs.IsLive(i), Is.True, "sign " + i + " is live while it is drawn");
                Assert.That(w.View.Available[i], Is.True);
            }

            // The anchor moves: the signs and their targets move with it.
            GazeTargetId id = w.Signs.TargetOf(SpaceSignLayout.Next);
            reach.Position += new float3(0.1f, -0.05f, 0.2f);
            w.Step(Down);
            Assert.That(w.Signs.TargetOf(SpaceSignLayout.Next), Is.EqualTo(id), "moved, not made again");
            Assert.That(Data(w, SpaceSignLayout.Next).Centre, Is.EqualTo(ReachSlots.World(reach.Position, reach.Rotation, 0.75f, ReachSlot.SignNext)));
            Assert.That(w.View.Origin, Is.EqualTo(reach.Position));

            // The scale moves: a sphere's radius cannot be changed in place.
            reach.Scale = 0.9f;
            w.Step(Down);
            Assert.That(w.Signs.TargetOf(SpaceSignLayout.Next), Is.Not.EqualTo(id));
            Assert.That(Data(w, SpaceSignLayout.Next).Radius, Is.EqualTo(0.9f * SpaceSignLayout.Radius).Within(1e-7f));
            Assert.That(w.View.Scale, Is.EqualTo(0.9f));
            Assert.That(w.Signs.IsLive(SpaceSignLayout.Next), Is.True, "made again, and live again");
        }

        [Test]
        public void Near_EachSign_FiresItsAction_AfterTheDwell_AndNotBefore()
        {
            for (int sign = 0; sign < SpaceSignLayout.Count; sign++)
            {
                Reach reach = Publish();
                Rig w = Three();
                w.Settle();
                quaternion look = Toward(ReachSlots.World(reach.Position, reach.Rotation, reach.Scale, SpaceSignLayout.SlotOf(sign)));

                w.Step(look, FramesFor(0.6f));
                Assert.That(w.Host.Calls, Is.Empty, "sign " + sign + " not before the dwell");
                Assert.That(w.MenuOpened, Is.EqualTo(0));
                Assert.That(w.Input.Gaze.Focused, Is.EqualTo(w.Signs.TargetOf(sign)), "the gaze is on near sign " + sign);

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
        public void Near_ASignIsLiveOnlyWhileItIsDrawn()
        {
            Publish();
            Rig w = Three();
            w.Settle();

            // Level: the signs are not drawn. A metre away nothing can reach a hidden sign; within reach a hand
            // passing through the anchor would press it, so there the hidden signs are not live.
            Assert.That(w.View.Visible, Is.False);
            for (int i = 0; i < SpaceSignLayout.Count; i++)
            {
                Assert.That(w.Signs.IsLive(i), Is.False, "hidden: sign " + i);
                Assert.That(w.Input.Core.Targets.IsEnabled(w.Signs.TargetOf(i)), Is.False, "hidden: sign " + i);
                Assert.That(w.View.Available[i], Is.True, "it can act, and is drawn so when it shows");
            }

            w.Step(Down, 2);
            Assert.That(w.View.Visible, Is.True);
            for (int i = 0; i < SpaceSignLayout.Count; i++)
            {
                Assert.That(w.Signs.IsLive(i), Is.True, "drawn: sign " + i);
                Assert.That(w.Input.Core.Targets.IsEnabled(w.Signs.TargetOf(i)), Is.True, "drawn: sign " + i);
            }

            // Drawn, but refused: dim, and not live.
            w.Registry.Set(CommandRouter.ParamDemoLock, 1f, ParamSource.Remote);
            w.Step(Down);
            Assert.That(w.View.Visible, Is.True);
            for (int i = 0; i < SpaceSignLayout.Count; i++)
            {
                Assert.That(w.Signs.IsLive(i), Is.False, "demo lock: sign " + i);
                Assert.That(w.View.Available[i], Is.False);
            }
            w.Registry.Set(CommandRouter.ParamDemoLock, 0f, ParamSource.Remote);
            w.Step(Down);
            for (int i = 0; i < SpaceSignLayout.Count; i++) Assert.That(w.Signs.IsLive(i), Is.True, "lock off again: sign " + i);

            w.Step(quaternion.identity, 30);   // past the focus grace of the gaze
            Assert.That(w.View.Visible, Is.False);
            for (int i = 0; i < SpaceSignLayout.Count; i++)
                Assert.That(w.Input.Core.Targets.IsEnabled(w.Signs.TargetOf(i)), Is.False, "hidden again: sign " + i);

            // In a menu the signs stand down, as a metre away.
            w.Input.PushContext(InputContext.Menu);
            w.Step(Down, 2);
            Assert.That(w.View.Visible, Is.False);
            for (int i = 0; i < SpaceSignLayout.Count; i++) Assert.That(w.Signs.IsLive(i), Is.False, "menu open: sign " + i);
        }

        /// <summary>A walker who looks level, looks down at the signs, at one of them, and up again.</summary>
        static void Walk(Rig w, Action between)
        {
            w.Settle();
            w.Step(Down, 4);
            between();
            w.Step(w.LookAt(SpaceSignLayout.Home), 12);
            w.Step(Down, 3);
            between();
            w.Step(quaternion.identity, 2);
        }

        [Test]
        public void WithNearFalse_EverythingIsExactlyWhereItIsWithNoLayoutAtAll()
        {
            Rig none = Three();
            Walk(none, () => { });
            List<string> expected = Snapshot(none);

            Publish(near: false);
            Rig far = Three();
            Walk(far, () => { });
            Assert.That(Snapshot(far), Is.EqualTo(expected), "Near false: positions, radii and flags to the last number");
            Assert.That(far.View.Reaches, Is.EqualTo(0), "the view was never asked to change");
            for (int i = 0; i < SpaceSignLayout.Count; i++)
                Assert.That(Data(far, i).Centre, Is.EqualTo(Head + math.mul(quaternion.RotateY(0f), SpaceSignLayout.Centre(i))), "sign " + i);
        }

        [Test]
        public void SwitchingNearOnAndOff_ReturnsEverythingToItsPlace()
        {
            Rig control = Three();
            Walk(control, () => control.Step(Down, 8));
            List<string> expected = Snapshot(control);

            Reach reach = Publish(near: false);
            Rig w = Three();
            bool everNear = false;
            Walk(w, () =>
            {
                reach.Near = true;
                w.Step(Down, 5);
                everNear |= w.View.Near && Data(w, SpaceSignLayout.Home).Radius < SpaceSignLayout.Radius;
                reach.Near = false;
                w.Step(Down, 3);
            });
            Assert.That(everNear, Is.True, "the signs were within reach in between");
            Assert.That(Snapshot(w), Is.EqualTo(expected), "back to the last number: centres, radii, flags, the view's frame");
            Assert.That(w.View.Near, Is.False);
            Assert.That(w.View.Scale, Is.EqualTo(1f));
        }

        [Test]
        public void Near_AFrameAllocatesNothing()
        {
            Reach reach = Publish();
            Rig w = Three();
            w.Settle();
            w.Step(Down, 20);   // warm up
            var f = new FrameContext { DeltaTime = Dt, RawHeadPosition = Head, RawHeadRotation = Down, HeadPosition = Head, HeadRotation = Down };

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                reach.Position += new float3(0.001f, 0f, 0f);
                w.Signs.Upload(in f);
                w.Signs.AfterFrame(in f);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;   // read before the assertion allocates anything
            Assert.That(allocated, Is.EqualTo(0L), "bytes allocated by 100 frames within reach");
            Assert.That(w.View.Near, Is.True);
        }
    }
}
