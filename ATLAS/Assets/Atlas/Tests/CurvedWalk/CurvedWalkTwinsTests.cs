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
    }
}
