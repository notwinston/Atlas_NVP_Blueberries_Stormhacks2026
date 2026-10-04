using System;
using System.Collections.Generic;
using Atlas.App;
using Atlas.App.Menu;
using Atlas.Input;
using Atlas.Link;
using Atlas.Kernel;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using static Atlas.Spaces.CurvedWalk.Tests.ReplayLogs;

namespace Atlas.Spaces.CurvedWalk.Tests
{
    // P1-C10 (MP §0.5 rule 5, lane C contract §13.1): the dial's detents and the mode chip are reachable by gaze alone.
    // The real input core runs, with the same dwell as in the headset; the real S2 logic and kernel run behind it.
    public class CurvedWalkTwinsTests
    {
        const float Dt = 1f / 60f;

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

        static quaternion Toward(float3 head, float3 target)
        {
            return quaternion.LookRotation(math.normalize(target - head), new float3(0f, 1f, 0f));
        }

        // A dwell on the right detent steps the dial, once the walker stands still and after the dwell, not before.
        [Test]
        public void DwellOnTheRightDetent_StepsQ_AfterTheDwell()
        {
            using (var h = new Rig(Room(RoomFixtures.Sample3x3), 5))
            {
                var input = new CoreInput();
                input.PushContext(InputContext.Space);
                var twins = new CurvedWalkTwins(input);
                int liveFrame = -1, pressedFrame = -1, presses = 0;
                quaternion look = quaternion.identity;
                List<HeadPose> log = Still(new double2(-1, -1), 240);
                for (int i = 0; i < log.Count; i++)
                {
                    HeadPose s = log[i];
                    // Step 1, 5 and 6 of the frame, in the loop's order.
                    input.Tick((Vector3)s.Position, (Quaternion)look, Dt);
                    h.Input.DialLeft = input.Pressed(AtlasAction.DialLeft);
                    h.Input.DialRight = input.Pressed(AtlasAction.DialRight);
                    if (h.Input.DialRight) { presses++; if (pressedFrame < 0) pressedFrame = i; }
                    h.Step(in s);
                    twins.Place(s.Position, s.Rotation);
                    twins.Update(h.S.SelectedQ, h.S.LastStill);
                    if (liveFrame < 0 && twins.IsLive(CurvedWalkTwins.DialRight)) liveFrame = i;
                    if (liveFrame >= 0) look = Toward(s.Position, twins.Centre(CurvedWalkTwins.DialRight));
                }
                Assert.GreaterOrEqual(liveFrame, 0, "the detents go live once the walker stands still");
                Assert.AreEqual(1, presses, "one dwell, one step");
                Assert.GreaterOrEqual((pressedFrame - liveFrame) * Dt, 0.69f, "not before the dwell");
                Assert.AreEqual(6, h.S.SelectedQ);
                Assert.AreEqual(6, h.Q.Int);
                Assert.IsFalse(twins.IsLive(CurvedWalkTwins.DialRight), "q = 6 is the end of the dial");
                Assert.IsTrue(twins.IsLive(CurvedWalkTwins.DialLeft));
            }
        }

        [Test]
        public void DwellOnTheLeftDetent_StepsQDown()
        {
            using (var h = new Rig(Room(RoomFixtures.Sample3x3), 5))
            {
                var input = new CoreInput();
                input.PushContext(InputContext.Space);
                var twins = new CurvedWalkTwins(input);
                quaternion look = quaternion.identity;
                List<HeadPose> log = Still(new double2(-1, -1), 240);
                for (int i = 0; i < log.Count; i++)
                {
                    HeadPose s = log[i];
                    input.Tick((Vector3)s.Position, (Quaternion)look, Dt);
                    h.Input.DialLeft = input.Pressed(AtlasAction.DialLeft);
                    h.Step(in s);
                    twins.Place(s.Position, s.Rotation);
                    twins.Update(h.S.SelectedQ, h.S.LastStill);
                    if (twins.IsLive(CurvedWalkTwins.DialLeft)) look = Toward(s.Position, twins.Centre(CurvedWalkTwins.DialLeft));
                }
                Assert.AreEqual(4, h.S.SelectedQ);
            }
        }

        // The dial and the mode switch act only on a still frame (MP §10.1.4): their twins are live only then.
        [Test]
        public void TheTwins_AreLiveOnlyWhileTheWalkerStandsStill()
        {
            using (var h = new Rig(Room(RoomFixtures.Sample3x3), 5))
            {
                var input = new CoreInput();
                input.PushContext(InputContext.Space);
                var twins = new CurvedWalkTwins(input);
                var log = Lap(5, 2, new double2(-1, -1), 0.0);
                int moving = 0, still = 0;
                for (int i = 0; i < log.Count; i++)
                {
                    HeadPose s = log[i];
                    h.Step(in s);
                    twins.Place(s.Position, s.Rotation);
                    twins.Update(h.S.SelectedQ, h.S.LastStill);
                    for (int k = 0; k < CurvedWalkTwins.Count; k++)
                        Assert.AreEqual(h.S.LastStill, twins.IsLive(k), "frame " + i + " twin " + k);
                    if (h.S.LastStill) still++; else moving++;
                }
                Assert.Greater(moving, 500);
                Assert.Greater(still, 0);
            }
        }

        [Test]
        public void TheDetents_StopAtTheEndsOfTheDial()
        {
            var input = new CoreInput();
            input.PushContext(InputContext.Space);
            var twins = new CurvedWalkTwins(input);
            Assert.IsTrue(twins.Update(CurvatureDial.MinQ, true), "the first call reports what is live");
            Assert.IsFalse(twins.IsLive(CurvedWalkTwins.DialLeft));
            Assert.IsTrue(twins.IsLive(CurvedWalkTwins.DialRight));
            Assert.IsFalse(twins.Update(CurvatureDial.MinQ, true), "nothing changed");
            Assert.IsTrue(twins.Update(CurvatureDial.MaxQ, true));
            Assert.IsTrue(twins.IsLive(CurvedWalkTwins.DialLeft));
            Assert.IsFalse(twins.IsLive(CurvedWalkTwins.DialRight));
            Assert.IsTrue(twins.IsLive(CurvedWalkTwins.Mode));
            Assert.IsTrue(twins.Update(CurvatureDial.MaxQ, false));
            Assert.IsFalse(twins.IsLive(CurvedWalkTwins.DialLeft));
            Assert.IsFalse(twins.IsLive(CurvedWalkTwins.Mode));
        }

        // Rule 5 at run time: the three actions the space reads have an enabled twin while they can act.
        [Test]
        public void EveryActionTheSpaceReads_HasAnEnabledGazeTwin_WhileItCanAct()
        {
            var input = new CoreInput();
            input.PushContext(InputContext.Space);
            var twins = new CurvedWalkTwins(input);
            twins.Place(new float3(0f, 1.6f, 0f), quaternion.identity);
            twins.Update(5, true);
            input.Tick(Vector3.zero, Quaternion.identity, Dt);
            input.Pressed(AtlasAction.DialLeft);
            input.Pressed(AtlasAction.DialRight);
            input.Pressed(AtlasAction.Primary);
            var missing = new List<AtlasAction>();
            input.CopyActionsWithoutGazeTwin(missing);
            Assert.That(missing, Is.Empty);

            twins.Dispose();
            input.CopyActionsWithoutGazeTwin(missing);
            Assert.That(missing, Is.EquivalentTo(new[] { AtlasAction.DialLeft, AtlasAction.DialRight, AtlasAction.Primary }),
                "the report is not vacuous, and Dispose removed the targets");
        }

        // ---- S2's chips next to the app's own gaze targets ----

        // Every gaze target that is live while S2 runs: the three chips in the chips' frame, the four SpaceLab signs in
        // the signs' frame and the menu chip in the menu's. Each frame keeps the head's place and a heading of its own.
        struct Spot
        {
            public string Name; public int Frame; public float3 Centre; public float Radius;
            public Spot(string name, int frame, float3 centre, float radius) { Name = name; Frame = frame; Centre = centre; Radius = radius; }
            /// <summary>Half the angle under which the gaze takes the target (GazeDwell.RaySphere).</summary>
            public float Cone { get { return math.max(math.atan(Radius / math.length(Centre)), math.radians(GazeDwell.MinAngleDeg)); } }
        }

        static List<Spot> SpotsOfS2()
        {
            var spots = new List<Spot>
            {
                new Spot("dial-", 0, CurvedWalkChips.DialLeft, CurvedWalkChips.Radius),
                new Spot("dial+", 0, CurvedWalkChips.DialRight, CurvedWalkChips.Radius),
                new Spot("mode", 0, CurvedWalkChips.Mode, CurvedWalkChips.Radius),
                new Spot("menu chip", 2, MenuLayout.OpenChip, MenuLayout.OpenChipRadius),
            };
            for (int i = 0; i < SpaceSignLayout.Count; i++)
                spots.Add(new Spot(SpaceSignLayout.LabelOf(i), 1, SpaceSignLayout.Centre(i), SpaceSignLayout.Radius));
            return spots;
        }

        static float Angle(float3 a, float3 b)
        {
            return math.acos(math.clamp(math.dot(math.normalize(a), math.normalize(b)), -1f, 1f));
        }

        // No chip of S2 lies within the dwell cones of another target, whatever the headings of the frames: the chips'
        // frame turns only after 50 degrees of head yaw while the signs and the menu chip trail the head, so two frames
        // can stand at any angle to each other and only the elevation keeps two targets apart.
        [Test]
        public void NoChip_SharesADwellConeWithAnotherGazeTarget_WhateverTheHeadings()
        {
            List<Spot> spots = SpotsOfS2();
            int pairs = 0;
            for (int a = 0; a < spots.Count; a++)
            {
                for (int b = a + 1; b < spots.Count; b++)
                {
                    Spot p = spots[a], q = spots[b];
                    if (p.Frame != 0 && q.Frame != 0 && p.Frame != q.Frame) continue;   // two of the app's frames: the app's own layout tests
                    float nearest = float.MaxValue, at = 0f;
                    if (p.Frame == q.Frame) nearest = Angle(p.Centre, q.Centre);
                    else
                    {
                        for (float yaw = -180f; yaw <= 180f; yaw += 0.125f)
                        {
                            float angle = Angle(p.Centre, math.mul(quaternion.RotateY(math.radians(yaw)), q.Centre));
                            if (angle < nearest) { nearest = angle; at = yaw; }
                        }
                    }
                    pairs++;
                    Assert.Greater(math.degrees(nearest), math.degrees(p.Cone + q.Cone),
                        p.Name + " and " + q.Name + " are on one gaze ray with the frames " + at + " degrees apart");
                }
            }
            Assert.AreEqual(3 + 3 * 5 + 6, pairs, "chip to chip, chip to sign and menu chip, sign to sign");
        }

        sealed class Host : ISpaceHost
        {
            public readonly List<string> Calls = new List<string>();
            public SpaceHostState State { get { return SpaceHostState.Running; } }
            public bool HasSpace { get { return true; } }
            public SpaceId Current { get { return SpaceId.CurvedWalk; } }
            public ISpaceModule Module { get { return null; } }
            public RoomModel Room { get { return null; } }
            public bool Standing { get { return false; } }
            public ushort WorldVersion { get { return 0; } }
            public bool IsAvailable(SpaceId id) { return id == SpaceId.Home || id == SpaceId.CurvedWalk; }
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

        // The walker looks at the right detent, then at the mode chip, and dwells: Mode 2 is asked for and nobody is sent
        // home. The chips and the app's signs are on one input core, each in its own frame, as in the headset: by the time
        // the gaze leaves the detent the signs have turned to stand under it.
        [Test]
        public void DwellOnTheModeChip_AfterTheRightDetent_FiresPrimary_NotReturnHome()
        {
            var input = new CoreInput();
            input.PushContext(InputContext.Space);
            var host = new Host();
            var registry = new ParamRegistry();
            registry.DeclareBool(CommandRouter.ParamDemoLock, "app", false);
            var actions = new SpaceActions(host, registry);
            var signs = new SpaceSigns(input, actions, host, null);
            var step = new SpaceActionStep(input, actions);
            var twins = new CurvedWalkTwins(input);
            float3 head = new float3(0.3f, 1.6f, -0.4f);
            int frame = 0, primary = 0, dial = 0;
            bool onMode = false, onASign = false;

            void Frames(quaternion look, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    var f = new FrameContext
                    {
                        FrameIndex = frame++, DeltaTime = Dt, RawHeadPosition = head, RawHeadRotation = look,
                        HeadPosition = head, HeadRotation = look
                    };
                    // Steps 1, 5, 6 and 8b of the frame, in the loop's order.
                    input.Tick((Vector3)head, (Quaternion)look, Dt);
                    if (input.Pressed(AtlasAction.Primary)) primary++;
                    if (input.Pressed(AtlasAction.DialLeft) || input.Pressed(AtlasAction.DialRight)) dial++;
                    GazeTargetId focused = input.Gaze.Focused;
                    onMode |= focused.IsValid && focused.Equals(twins.Target(CurvedWalkTwins.Mode));
                    for (int k = 0; k < SpaceSignLayout.Count; k++) onASign |= focused.IsValid && focused.Equals(signs.TargetOf(k));
                    twins.Place(head, look);
                    twins.Update(5, true);
                    signs.Upload(in f);
                    step.AfterFrame(in f);
                    signs.AfterFrame(in f);
                }
            }

            Frames(quaternion.identity, 3);
            Assert.IsTrue(signs.IsLive(SpaceSignLayout.Home), "the home beacon is live in a running space");
            Assert.IsTrue(twins.IsLive(CurvedWalkTwins.Mode));
            // 0.4 s on the detent: less than the dwell, and long enough for the signs to come round under it.
            Frames(Toward(head, twins.Centre(CurvedWalkTwins.DialRight)), 24);
            Assert.AreEqual(0, dial, "the detent was only looked at");
            Frames(Toward(head, twins.Centre(CurvedWalkTwins.Mode)), 60);

            Assert.IsTrue(onMode, "the gaze was on the mode chip");
            Assert.IsFalse(onASign, "and never on a sign");
            Assert.That(host.Calls, Is.Empty, "nobody was sent home");
            Assert.AreEqual(1, primary, "Mode 2 was asked for once");
            twins.Dispose();
            signs.Shutdown();
        }

        [Test]
        public void NoInput_StillPlacesAndJudges()
        {
            var twins = new CurvedWalkTwins(null);
            twins.Place(new float3(1f, 1.6f, 2f), quaternion.identity);
            twins.Update(5, true);
            Assert.IsTrue(twins.IsLive(CurvedWalkTwins.DialRight));
            Assert.AreEqual(1f + CurvedWalkChips.DialRight.x, twins.Centre(CurvedWalkTwins.DialRight).x, 1e-5f);
            Assert.DoesNotThrow(twins.Dispose);
        }

        // ---- a hands session: the chips come within reach (documents/ATLAS_Hands_Design.md section 6.1) ----

        static readonly float3 Eyes = new float3(0.3f, 1.6f, -0.4f);

        /// <summary>The near layout as Atlas.Hands publishes it, with every number in the test's hands.</summary>
        sealed class Reach : IReachLayout
        {
            public bool Near { get; set; }
            public float3 Position { get; set; }
            public quaternion Rotation { get; set; }
            public float Scale { get; set; }
        }

        Reach reach;
        IReachLayout layoutBefore;

        [SetUp]
        public void StartWithNoLayout()
        {
            // A layout another test left published would move every chip of this one.
            if (AtlasServices.TryGet(out layoutBefore)) AtlasServices.Unregister(layoutBefore);
            reach = null;
        }

        [TearDown]
        public void PutTheLayoutBack()
        {
            if (reach != null) AtlasServices.Unregister<IReachLayout>(reach);
            if (layoutBefore != null) AtlasServices.Register(layoutBefore);
        }

        /// <summary>
        /// Publishes the near anchor of a hands session: 0.48 m from the eyes on a line 25 degrees below the
        /// horizontal, tilted to face the head (+Z away from the walker, +Y up along the panel).
        /// </summary>
        Reach Publish(bool near = true, float scale = 0.75f)
        {
            if (reach != null) AtlasServices.Unregister<IReachLayout>(reach);
            quaternion rotation = math.mul(quaternion.RotateY(math.radians(15f)), quaternion.RotateX(math.radians(25f)));
            reach = new Reach { Near = near, Scale = scale, Rotation = rotation, Position = Eyes + math.mul(rotation, new float3(0f, 0f, 0.48f)) };
            AtlasServices.Register<IReachLayout>(reach);
            return reach;
        }

        static GazeTargetData Data(CoreInput input, GazeTargetId id)
        {
            Assert.IsTrue(input.Core.Targets.TryGet(id, out GazeTargetData data), "the target is registered");
            return data;
        }

        /// <summary>Everything the twins hold about where the chips stand, for comparing two runs number by number.</summary>
        static List<string> Snapshot(CoreInput input, CurvedWalkTwins twins)
        {
            var lines = new List<string> { "near " + twins.Near + " scale " + twins.Scale.ToString("R") };
            for (int i = 0; i < CurvedWalkTwins.Count; i++)
            {
                GazeTargetData d = Data(input, twins.Target(i));
                lines.Add("chip " + i + " " + d.Action + " " + d.Shape + " " + d.Context + " at " + d.Centre + " centre " + twins.Centre(i)
                    + " r " + d.Radius.ToString("R") + " on " + input.Core.Targets.IsEnabled(twins.Target(i)) + " live " + twins.IsLive(i));
            }
            return lines;
        }

        [Test]
        public void EachChip_HasItsOwnPlaceWithinReach()
        {
            Assert.AreEqual(ReachSlot.DialLeft, CurvedWalkTwins.SlotOf(CurvedWalkTwins.DialLeft));
            Assert.AreEqual(ReachSlot.DialRight, CurvedWalkTwins.SlotOf(CurvedWalkTwins.DialRight));
            Assert.AreEqual(ReachSlot.Mode, CurvedWalkTwins.SlotOf(CurvedWalkTwins.Mode));
            // As a metre away: the two detents level with each other, left and right, the mode chip above the right one.
            float3 left = ReachSlots.Centre(ReachSlot.DialLeft), right = ReachSlots.Centre(ReachSlot.DialRight), mode = ReachSlots.Centre(ReachSlot.Mode);
            Assert.Less(left.x, 0f); Assert.Greater(right.x, 0f);
            Assert.AreEqual(left.y, right.y);
            Assert.AreEqual(right.x, mode.x);
            Assert.Greater(mode.y, right.y);
            Assert.GreaterOrEqual(2f * CurvedWalkChips.NearRadius(0.75f), ReachSlots.MinTargetMetres);
            Assert.AreEqual(0.75f * CurvedWalkChips.Radius, CurvedWalkChips.NearRadius(0.75f), 1e-7f);
            Assert.AreEqual(0.5f * ReachSlots.MinTargetMetres, CurvedWalkChips.NearRadius(0.1f), "never under 3 cm across");
        }

        // Every gaze target that is live while S2 runs, within reach: S2's three chips, the menu chip and the four signs
        // share one frame, the anchor's, so their places alone keep them apart.
        [Test]
        public void Near_NoTargetOfS2OrOfTheApp_StandsOnAnother_AndAllAreWithinReach()
        {
            var slots = new List<ReachSlot> { ReachSlot.DialLeft, ReachSlot.DialRight, ReachSlot.Mode, MenuLayout.ChipSlot };
            var radii = new List<float> { CurvedWalkChips.Radius, CurvedWalkChips.Radius, CurvedWalkChips.Radius, MenuLayout.OpenChipRadius };
            var names = new List<string> { "dial-", "dial+", "mode", "menu chip" };
            for (int i = 0; i < SpaceSignLayout.Count; i++)
            {
                slots.Add(SpaceSignLayout.SlotOf(i)); radii.Add(SpaceSignLayout.Radius); names.Add(SpaceSignLayout.LabelOf(i));
            }
            Assert.AreEqual(8, slots.Count);

            foreach (float scale in new[] { 0.5f, 0.75f, 1f })
            {
                int pairs = 0;
                for (int a = 0; a < slots.Count; a++)
                {
                    float3 c = ReachSlots.Local(slots[a], scale);
                    float r = ReachSlots.TargetHalf(radii[a], scale);
                    Assert.AreEqual(0f, c.z, names[a] + " lies in the anchor's plane");
                    Assert.GreaterOrEqual(2f * r, ReachSlots.MinTargetMetres, names[a] + " is at least 3 cm across at scale " + scale);
                    Assert.IsTrue(ReachSlots.HidesWhileMenuOpen(slots[a]), names[a] + " stands where the open menu panel stands");
                    for (int b = a + 1; b < slots.Count; b++)
                    {
                        float gap = math.distance(c, ReachSlots.Local(slots[b], scale)) - r - ReachSlots.TargetHalf(radii[b], scale);
                        // At the usual scale and above, a fingertip's margin: the hover band of one target must not reach the next.
                        Assert.Greater(gap, scale >= 0.75f ? 0.025f : 0f, names[a] + " and " + names[b] + " at scale " + scale);
                        pairs++;
                    }
                    if (scale != 0.75f) continue;
                    // The usual scale: within about 20 cm of the anchor to each side.
                    Assert.LessOrEqual(math.abs(c.x) + r, 0.20f, names[a] + " sideways");
                    Assert.LessOrEqual(math.abs(c.y) + r, 0.20f, names[a] + " up and down");
                }
                Assert.AreEqual(28, pairs);
            }
            Assert.IsFalse(ReachSlots.HidesWhileMenuOpen(ReachSlot.MenuPanel));
        }

        [Test]
        public void Near_TheChipsStandAtTheirSlots_InTheAnchorsFrame_TimesTheScale()
        {
            Reach r = Publish();
            var input = new CoreInput();
            input.PushContext(InputContext.Space);
            var twins = new CurvedWalkTwins(input);
            twins.Place(Eyes, quaternion.identity);
            twins.Update(5, true);

            Assert.IsTrue(twins.Near);
            Assert.AreEqual(0.75f, twins.Scale);
            for (int i = 0; i < CurvedWalkTwins.Count; i++)
            {
                float3 slot = ReachSlots.Centre(CurvedWalkTwins.SlotOf(i));
                float3 centre = r.Position + math.mul(r.Rotation, 0.75f * new float3(slot.x, slot.y, -slot.z));
                GazeTargetData d = Data(input, twins.Target(i));
                Assert.Less(math.distance(d.Centre, centre), 1e-6f, "chip " + i + ": " + d.Centre + " against " + centre);
                Assert.AreEqual(ReachSlots.World(r.Position, r.Rotation, 0.75f, CurvedWalkTwins.SlotOf(i)), d.Centre);
                Assert.AreEqual(d.Centre, twins.Centre(i), "the drawn chip stands on its target");
                Assert.AreEqual(0.75f * CurvedWalkChips.Radius, d.Radius, 1e-7f, "its radius times the scale");
                Assert.GreaterOrEqual(2f * d.Radius, ReachSlots.MinTargetMetres, "no target under 3 cm");
                Assert.AreEqual(InputContext.Space, d.Context);
                Assert.IsTrue(twins.IsLive(i));
                Assert.IsTrue(input.Core.Targets.IsEnabled(twins.Target(i)), "a target made again takes its flag again");
            }
            Assert.AreEqual(AtlasAction.DialLeft, Data(input, twins.Target(CurvedWalkTwins.DialLeft)).Action);
            Assert.AreEqual(AtlasAction.DialRight, Data(input, twins.Target(CurvedWalkTwins.DialRight)).Action);
            Assert.AreEqual(AtlasAction.Primary, Data(input, twins.Target(CurvedWalkTwins.Mode)).Action);

            // The anchor moves: the chips move with it, and the head's turning moves nothing.
            GazeTargetId id = twins.Target(CurvedWalkTwins.Mode);
            r.Position += new float3(0.1f, -0.05f, 0.2f);
            twins.Place(Eyes + new float3(1f, 0f, 1f), quaternion.RotateY(math.radians(120f)));
            Assert.AreEqual(id, twins.Target(CurvedWalkTwins.Mode), "moved, not made again");
            Assert.AreEqual(ReachSlots.World(r.Position, r.Rotation, 0.75f, ReachSlot.Mode), twins.Centre(CurvedWalkTwins.Mode));

            // The scale moves at the end of the dial: the targets are made again, and a dead detent stays dead.
            twins.Update(CurvatureDial.MaxQ, true);
            r.Scale = 0.9f;
            twins.Place(Eyes, quaternion.identity);
            Assert.AreNotEqual(id, twins.Target(CurvedWalkTwins.Mode), "a sphere's radius cannot be changed in place");
            Assert.AreEqual(0.9f * CurvedWalkChips.Radius, Data(input, twins.Target(CurvedWalkTwins.Mode)).Radius, 1e-7f);
            Assert.AreEqual(0.9f, twins.Scale);
            Assert.IsFalse(input.Core.Targets.IsEnabled(twins.Target(CurvedWalkTwins.DialRight)), "q = 6 is the end of the dial");
            Assert.IsTrue(input.Core.Targets.IsEnabled(twins.Target(CurvedWalkTwins.DialLeft)));
            Assert.IsTrue(input.Core.Targets.IsEnabled(twins.Target(CurvedWalkTwins.Mode)));
        }

        [Test]
        public void Near_DwellOnTheRightDetent_FiresDialRight_AfterTheDwell_AndNotBefore()
        {
            Reach r = Publish();
            var input = new CoreInput();
            input.PushContext(InputContext.Space);
            var twins = new CurvedWalkTwins(input);
            int presses = 0, pressedFrame = -1;
            quaternion look = quaternion.identity;
            for (int i = 0; i < 90; i++)
            {
                // Steps 1 and 6 of the frame, in the loop's order.
                input.Tick((Vector3)Eyes, (Quaternion)look, Dt);
                if (input.Pressed(AtlasAction.DialRight)) { presses++; if (pressedFrame < 0) pressedFrame = i; }
                Assert.IsFalse(input.Pressed(AtlasAction.DialLeft) || input.Pressed(AtlasAction.Primary), "frame " + i);
                twins.Place(Eyes, look);
                twins.Update(5, true);
                look = Toward(Eyes, twins.Centre(CurvedWalkTwins.DialRight));
            }
            Assert.AreEqual(ReachSlots.World(r.Position, r.Rotation, 0.75f, ReachSlot.DialRight), twins.Centre(CurvedWalkTwins.DialRight));
            Assert.AreEqual(1, presses, "one dwell, one step");
            Assert.GreaterOrEqual(pressedFrame * Dt, 0.69f, "not before the dwell");
            Assert.AreEqual(InputSource.Gaze, input.LastSource(AtlasAction.DialRight));
        }

        [Test]
        public void Near_TheChipsHideWhileAMenuIsOpen_AndAMetreAwayTheyDoNot()
        {
            var input = new CoreInput();
            input.PushContext(InputContext.Space);
            var twins = new CurvedWalkTwins(input);

            // A metre away, as always: a menu takes the input, and the chips stay as they are.
            twins.Place(Eyes, quaternion.identity);
            Assert.IsTrue(twins.Update(5, true));
            input.PushContext(InputContext.Menu);
            twins.Place(Eyes, quaternion.identity);
            Assert.IsFalse(twins.Update(5, true), "nothing changed");
            for (int i = 0; i < CurvedWalkTwins.Count; i++) Assert.IsTrue(twins.IsLive(i), "far, menu open: chip " + i);
            input.PopContext(InputContext.Menu);

            // Within reach the open menu panel stands on the chips' places: they are neither drawn nor live.
            Publish();
            twins.Place(Eyes, quaternion.identity);
            Assert.IsFalse(twins.Update(5, true), "within reach, no menu: the same chips are live");
            input.PushContext(InputContext.Menu);
            twins.Place(Eyes, quaternion.identity);
            Assert.IsTrue(twins.Update(5, true), "the caller redraws: the chips go");
            for (int i = 0; i < CurvedWalkTwins.Count; i++)
            {
                Assert.IsFalse(twins.IsLive(i), "near, menu open: chip " + i);
                Assert.IsFalse(input.Core.Targets.IsEnabled(twins.Target(i)));
            }
            input.PopContext(InputContext.Menu);
            twins.Place(Eyes, quaternion.identity);
            Assert.IsTrue(twins.Update(5, true), "and come back when the menu closes");
            for (int i = 0; i < CurvedWalkTwins.Count; i++) Assert.IsTrue(twins.IsLive(i), "near, menu closed: chip " + i);
        }

        /// <summary>A walker who stands, turns past the chips' follow angle, steps aside and reaches the end of the dial.</summary>
        static void Walk(CurvedWalkTwins twins, Action<float3, quaternion> between)
        {
            quaternion ahead = quaternion.identity, turned = quaternion.RotateY(math.radians(70f));
            twins.Place(Eyes, ahead);
            twins.Update(5, true);
            between(Eyes, ahead);
            twins.Place(Eyes, turned);
            twins.Update(5, false);
            between(Eyes, turned);
            twins.Place(Eyes + new float3(0.4f, 0f, 0.2f), quaternion.RotateY(math.radians(80f)));
            twins.Update(CurvatureDial.MaxQ, true);
        }

        [Test]
        public void WithNearFalse_EverythingIsExactlyWhereItIsWithNoLayoutAtAll()
        {
            var plain = new CoreInput();
            plain.PushContext(InputContext.Space);
            var none = new CurvedWalkTwins(plain);
            Walk(none, (head, look) => { });
            List<string> expected = Snapshot(plain, none);

            Publish(near: false);
            var input = new CoreInput();
            input.PushContext(InputContext.Space);
            var far = new CurvedWalkTwins(input);
            Walk(far, (head, look) => { });
            Assert.That(Snapshot(input, far), Is.EqualTo(expected), "Near false: positions, radii and flags to the last number");
            Assert.AreEqual(1f, far.Scale);
            Assert.IsFalse(far.Near);
        }

        [Test]
        public void SwitchingNearOnAndOff_ReturnsEverythingToItsPlace()
        {
            var plain = new CoreInput();
            plain.PushContext(InputContext.Space);
            var control = new CurvedWalkTwins(plain);
            Walk(control, (head, look) => control.Place(head, look));
            List<string> expected = Snapshot(plain, control);

            Reach r = Publish(near: false);
            var input = new CoreInput();
            input.PushContext(InputContext.Space);
            var twins = new CurvedWalkTwins(input);
            bool everNear = false;
            Walk(twins, (head, look) =>
            {
                // The same frame as the control's, within reach.
                r.Near = true;
                twins.Place(head, look);
                everNear |= twins.Near && Data(input, twins.Target(0)).Radius < CurvedWalkChips.Radius;
                r.Near = false;
            });
            Assert.IsTrue(everNear, "the chips were within reach in between");
            Assert.That(Snapshot(input, twins), Is.EqualTo(expected), "back to the last number: centres, radii, flags");
        }

        [Test]
        public void Near_PlacingAllocatesNothing()
        {
            Reach r = Publish();
            var input = new CoreInput();
            input.PushContext(InputContext.Space);
            var twins = new CurvedWalkTwins(input);
            for (int i = 0; i < 20; i++) { twins.Place(Eyes, quaternion.identity); twins.Update(5, i % 2 == 0); }   // warm up

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                r.Position += new float3(0.001f, 0f, 0f);
                twins.Place(Eyes, quaternion.identity);
                twins.Update(5, i % 2 == 0);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;   // read before the assertion boxes anything
            Assert.AreEqual(0L, allocated, "bytes allocated by 100 frames within reach");
        }

#if !ATLAS_HEADLESS
        // The chips as CurvedWalkHud draws them, in a hands session: each text stands on its twin's slot about the near
        // anchor, on the world layer, where the eye cameras test it against depth; it goes while a menu holds the input,
        // and with Near off it is back where it has always been.
        [Test, Category("Unity")]
        public void Near_TheDrawnChips_StandOnTheirSlots_OnTheWorldLayer_AndHideWhileAMenuIsOpen()
        {
            Reach r = Publish();
            var input = new CoreInput();
            input.PushContext(InputContext.Space);
            var world = new GameObject("S2 world");
            var hud = new CurvedWalkHud(null, world.transform, input);
            try
            {
                var chips = new Transform[CurvedWalkTwins.Count];
                for (int i = 0; i < chips.Length; i++) chips[i] = world.transform.Find("CurvedWalkChip" + i);
                Assume.That(chips[0], Is.Not.Null, "the project's TextMeshPro font: without it S2 draws no chip");

                hud.Upload(Eyes, quaternion.identity, 5, null);
                for (int i = 0; i < chips.Length; i++)
                {
                    Assert.IsTrue(chips[i].gameObject.activeSelf, "chip " + i + " is drawn while it is live");
                    Vector3 slot = (Vector3)ReachSlots.World(r.Position, r.Rotation, 0.75f, CurvedWalkTwins.SlotOf(i));
                    Assert.Less(Vector3.Distance(slot, chips[i].position), 1e-5f, "chip " + i + " stands at its slot");
                    Assert.AreEqual(Atlas.Rendering.PortalStencilTable.WorldLayer, chips[i].gameObject.layer, "depth-tested: the world layer");
                }

                input.PushContext(InputContext.Menu);
                hud.Upload(Eyes, quaternion.identity, 5, null);
                for (int i = 0; i < chips.Length; i++) Assert.IsFalse(chips[i].gameObject.activeSelf, "chip " + i + " is hidden while the menu panel is open");
                input.PopContext(InputContext.Menu);
                hud.Upload(Eyes, quaternion.identity, 5, null);
                for (int i = 0; i < chips.Length; i++) Assert.IsTrue(chips[i].gameObject.activeSelf, "chip " + i + " is back");

                // A gamepad is connected: everything is exactly where it was.
                r.Near = false;
                hud.Upload(Eyes, quaternion.identity, 5, null);
                float3[] far = { CurvedWalkChips.DialLeft, CurvedWalkChips.DialRight, CurvedWalkChips.Mode };
                for (int i = 0; i < chips.Length; i++)
                {
                    Assert.Less(Vector3.Distance((Vector3)(Eyes + far[i]), chips[i].position), 1e-5f, "chip " + i + " a metre away again");
                    Assert.AreEqual(Atlas.Rendering.PortalStencilTable.WorldLayer, chips[i].gameObject.layer);
                }
            }
            finally
            {
                hud.Dispose();
                UnityEngine.Object.DestroyImmediate(world);
            }
        }
#endif
    }
}
