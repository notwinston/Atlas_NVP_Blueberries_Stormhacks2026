using System;
using System.Collections.Generic;
using Atlas.Input;
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
