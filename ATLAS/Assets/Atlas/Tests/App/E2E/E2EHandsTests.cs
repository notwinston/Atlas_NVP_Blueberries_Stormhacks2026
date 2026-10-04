using System;
using System.Collections.Generic;
using System.IO;
using Atlas.App;
using Atlas.Hands;
using Atlas.Input;
using Atlas.Link;
using Atlas.Safety;
using Atlas.Scan;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.E2E.Tests
{
    /// <summary>
    /// The walker's tracked hands in the end-to-end rehearsal (documents/ATLAS_Hands_Design.md): the app's own
    /// hands runtime between the pose pipeline and input, its depth mask at Bump Guard, its parameters over the
    /// link and its events at the Hub, with no Unity and no phone. The detector is scripted, or a recorded hand
    /// log is played; a scripted hand is also in the depth image Bump Guard reads, as it is before a LiDAR.
    /// </summary>
    public class E2EHandsTests
    {
        /// <summary>250 ms at 60 frames a second: the time a parameter or a command of the Station may take.</summary>
        const int CommandFrames = 15;
        /// <summary>A reach of the fingertip takes this long, and stays this long where it went: 0.3 s and 0.4 s.</summary>
        const int ReachFrames = 18, HoldFrames = 24;
        /// <summary>The recorded logs: 30 results a second, each 80 ms old (recordings/README.md).</summary>
        const string PressLog = "hands_press", PinchLog = "hands_pinch";

        static RoomModel Room() { return RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3); }

        static string HandLog(string name) { return Path.Combine(RehearsalRig.RecordingsDirectory(), name + ".handlog"); }

        /// <summary>
        /// A rig with the Station up, the walker standing at a floor point with the headset on, the head the
        /// phone's own camera, and the room entered at Home. The hands are as the app starts them: off.
        /// </summary>
        static RehearsalRig Enter(RoomModel room, float2 floor, float yaw = 0f, IHandsSource source = null)
        {
            var rig = new RehearsalRig(true, source);
            rig.CameraHead = true;
            rig.StandAt(floor, yaw);
            rig.StartStation();
            Assert.That(rig.StepUntil(() => rig.LinkUp, 300), Is.GreaterThanOrEqualTo(0), "the link never came up");
            Assert.That(rig.ActivateRoom(room), Is.GreaterThanOrEqualTo(0), "Home was never entered");
            Assert.That(rig.Lab.Current, Is.EqualTo(SpaceId.Home));
            Assert.That(rig.Hands.Active, Is.False, "hands are off until hands.enabled is set");
            return rig;
        }

        /// <summary>Sets hands.enabled on the phone and steps until the session runs.</summary>
        static void SwitchOn(RehearsalRig rig)
        {
            Assert.That(rig.Registry.Set(HandsSystem.ParamEnabled, 1f), Is.True);
            Assert.That(rig.StepUntil(() => rig.Hands.Active, CommandFrames), Is.GreaterThanOrEqualTo(0), "the hands session did not start");
        }

        // ---- the walker's hands, in the camera's space -------------------------------------------------------

        /// <summary>Palm frame to camera: fingers up, the back of the hand to the camera, as a walker sees their own raised hand.</summary>
        static readonly quaternion Upright = new quaternion(new float3x3(new float3(-1f, 0f, 0f), new float3(0f, 0f, 1f), new float3(0f, 1f, 0f)));

        /// <summary>An open hand held up with its wrist at a point of the camera's space.</summary>
        static JointSet Open(HandSide side, float3 wrist)
        {
            return HandModel.RestPose(side).Transformed(Upright, wrist);
        }

        /// <summary>A hand held up with its index finger out and the others curled, the index fingertip at a point of the camera's space.</summary>
        static JointSet Pointing(HandSide side, float3 indexTip)
        {
            JointSet shape = HandModel.Pose(side, new float4(0f, 0.95f, 0.95f, 0.95f), 0.55f).Transformed(Upright, float3.zero);
            return shape.Transformed(quaternion.identity, indexTip - shape[HandJoint.IndexTip]);
        }

        /// <summary>Where the two raised hands of the walking tests are: wrists 0.45 m ahead, 0.22 m apart, a hand's length below the eyes.</summary>
        static readonly float3 RaisedLeft = new float3(-0.11f, -0.16f, 0.45f), RaisedRight = new float3(0.11f, -0.16f, 0.45f);

        static void RaiseBothHands(RehearsalRig rig)
        {
            JointSet left = Open(HandSide.Left, RaisedLeft), right = Open(HandSide.Right, RaisedRight);
            rig.ShowHand(HandSide.Left, in left);
            rig.ShowHand(HandSide.Right, in right);
        }

        /// <summary>A point of the room in the camera's space of a walker who stands still.</summary>
        static float3 InCamera(RehearsalRig rig, float3 world)
        {
            return math.mul(math.inverse(rig.Standing.Rotation), world - rig.Standing.Position);
        }

        // ---- targets and presses ---------------------------------------------------------------------------

        static GazeTargetId AddTarget(RehearsalRig rig, AtlasAction action, float3 centre, float radius, int tag, string label,
                                      Action<GazeTargetId, int> callback = null)
        {
            GazeTargetId id = rig.Input.Core.Targets.Add(new GazeTargetData
            {
                Action = action, Space = GazeSpace.World, Shape = GazeShape.Sphere, Centre = centre, Radius = radius,
                Mode = GazeMode.Once, Context = InputContext.Space, Tag = tag, Label = label, Callback = callback,
            });
            Assert.That(id.IsValid, Is.True);
            return id;
        }

        /// <summary>
        /// The right hand points at a sphere target: its index fingertip on the line from the target's centre to
        /// the head, depth metres in front of the target's face (the disc through its centre that faces the
        /// head); negative is through it.
        /// </summary>
        static void PointAt(RehearsalRig rig, float3 centre, float depth)
        {
            float3 towardsHead = math.normalize(rig.Standing.Position - centre);
            JointSet hand = Pointing(HandSide.Right, InCamera(rig, centre + towardsHead * depth));
            rig.ShowHand(HandSide.Right, in hand);
        }

        /// <summary>The fingertip goes from one depth in front of a target's face to another, eased, and stays there.</summary>
        static void Reach(RehearsalRig rig, float3 centre, float from, float to, Action eachFrame = null)
        {
            for (int k = 1; k <= ReachFrames + HoldFrames; k++)
            {
                float x = math.saturate(k / (float)ReachFrames);
                PointAt(rig, centre, math.lerp(from, to, x * x * (3f - 2f * x)));
                rig.Step();
                if (eachFrame != null) eachFrame();
            }
        }

        // ---- what a rehearsal of the hands may not leave behind ------------------------------------------------

        static void AssertHealthy(RehearsalRig rig)
        {
            Assert.That(rig.Warnings, Is.Empty, "frame loop warnings");
            Assert.That(rig.Loop.FaultedSenseCount, Is.EqualTo(0));
            Assert.That(rig.Hands.Faulted, Is.False, "the hands switched themselves off");
            Assert.That(rig.HasEvent("hands.fault"), Is.False);
            Assert.That(rig.GuardStep.MaskFaults, Is.EqualTo(0), "the mask source threw");
            Assert.That(rig.HasEvent("space.error"), Is.False, "a space threw");
        }

        static bool IsHands(EventMessage e) { return e.Ev.StartsWith("hands.", StringComparison.Ordinal); }

        /// <summary>The hands' events of a log, in order, as text: for messages and for comparing two logs.</summary>
        static string HandsEvents(List<EventMessage> log)
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < log.Count; i++)
            {
                if (!IsHands(log[i])) continue;
                text.Append(log[i].Ev).Append(' ').Append(log[i].Data != null ? log[i].Data.ToJson(false) : "{}").Append("; ");
            }
            return text.ToString();
        }

        /// <summary>Every event the hands logged on the phone is in the Hub's log, in the same order with the same data.</summary>
        static void AssertTheHubHasEveryHandsEvent(RehearsalRig rig)
        {
            rig.Steps(3);       // the pump sends at step 8b, the Hub polls after the frame
            Assert.That(HandsEvents(rig.HubEvents), Is.EqualTo(HandsEvents(rig.Logged)), "the Hub's log against the phone's");
        }

        /// <summary>The samples Bump Guard reads of a depth image (every fourth pixel from pixel 2) that are nearer than a mask may ever reach: here, the hands.</summary>
        static int NearSamples(in DepthFrame depth)
        {
            int stride = BumpConstants.DepthStride, first = BumpDepthKernel.FirstSample(stride), count = 0;
            for (int v = first; v < depth.Height; v += stride)
            {
                for (int u = first; u < depth.Width; u += stride)
                {
                    float d = depth.Depth[v * depth.Width + u];
                    if (d >= BumpConstants.DepthMin && d <= BumpConstants.MaskMaxDepth) count++;
                }
            }
            return count;
        }

        // ---- off by default ----------------------------------------------------------------------------------

        struct GuardPrint
        {
            public BumpLevel Level;
            public float Now, Path04, Path08;
            public int Cells, Stamped;
        }

        [Test]
        public void E2E_Hands_OffByDefault()
        {
            RoomModel room = Room();
            PoseLog lap = RehearsalRig.LoadWalk("walk_lap_sample_3x3");
            // On the second side of the lap the walker holds both hands up for two seconds. With the hands off
            // they are what they always were to Bump Guard: something 0.45 m ahead.
            int side = lap.Samples.Length / 4, raiseAt = side + 60, lowerAt = 2 * side - 60;
            var prints = new List<GuardPrint>[2];
            var changes = new List<BumpChange>[2];

            // 0: the rig as it is, hands and all, nothing set. 1: the phone built with no hands at all.
            for (int run = 0; run < 2; run++)
            {
                var rig = new RehearsalRig(run == 0);
                using (rig)
                {
                    rig.CameraHead = true;
                    rig.StandAt(in lap.Samples[0]);
                    rig.StartStation();
                    Assert.That(rig.StepUntil(() => rig.LinkUp, 300), Is.GreaterThanOrEqualTo(0), "the link never came up");
                    Assert.That(rig.ActivateRoom(room), Is.GreaterThanOrEqualTo(0), "Home was never entered");
                    rig.BumpChanges.Clear();
                    int start = rig.Frames;
                    prints[run] = new List<GuardPrint>(lap.Samples.Length);

                    rig.Play(lap);
                    for (int k = 0; k < lap.Samples.Length; k++)
                    {
                        if (k == raiseAt) RaiseBothHands(rig);
                        if (k == lowerAt)
                        {
                            rig.HideHand(HandSide.Left);
                            rig.HideHand(HandSide.Right);
                        }
                        rig.Step();
                        prints[run].Add(new GuardPrint
                        {
                            Level = rig.Guard.Level, Now = rig.Guard.Core.Now, Path04 = rig.Guard.Core.Path04, Path08 = rig.Guard.Core.Path08,
                            Cells = rig.Guard.Core.DynamicCellCount, Stamped = rig.Guard.Core.LastDepth.Stamped,
                        });
                        if (run != 0) continue;

                        string at = "frame " + k;
                        Assert.That(((IHands)rig.Hands).Active, Is.False, at);
                        Assert.That(rig.Hands.Pose(HandSide.Left).Tracked || rig.Hands.Pose(HandSide.Right).Tracked, Is.False, at);
                        Assert.That(rig.Input.LastHands.Active, Is.False, at);
                        Assert.That(rig.Guard.Core.LastDepth.Masked, Is.EqualTo(0), at);
                    }

                    changes[run] = new List<BumpChange>(rig.BumpChanges);
                    for (int i = 0; i < changes[run].Count; i++)
                    {
                        BumpChange c = changes[run][i];
                        c.Frame -= start;
                        changes[run][i] = c;
                    }
                    Assert.That(rig.Warnings, Is.Empty, "frame loop warnings");
                    Assert.That(rig.Loop.FaultedSenseCount, Is.EqualTo(0));
                    if (run != 0)
                    {
                        Assert.That(rig.Hands, Is.Null);
                        Assert.That(rig.Registry.Find(HandsSystem.ParamEnabled), Is.Null);
                        continue;
                    }

                    // Every other row of the gate holds (headset on, no gamepad, a detector, the camera's head,
                    // a cool phone): the parameter alone keeps the hands off.
                    Assert.That(rig.Registry.GetBool(HandsSystem.ParamEnabled, true), Is.False, "hands.enabled defaults to off");
                    Assert.That(rig.InHeadset, Is.True);
                    Assert.That(rig.Pipeline.Source.DrivesRig, Is.True);
                    Assert.That(rig.Script.Available, Is.True);
                    Assert.That(rig.Hands.Gate.Reason, Is.EqualTo(HandsOffReason.Param));
                    // The detector was never started, nothing was handed to Bump Guard, the panels stay where they are.
                    Assert.That(rig.Script.Rate, Is.EqualTo(0));
                    Assert.That(rig.Script.Results, Is.EqualTo(0));
                    DepthMask mask;
                    Assert.That(rig.Hands.TryGetMask(rig.Pipeline.Sensors.Depth.Sequence, out mask), Is.False);
                    Assert.That(mask.Session, Is.EqualTo(0));
                    Assert.That(((IReachLayout)rig.Hands).Near, Is.False);
                    Assert.That(((IReachLayout)rig.Hands).Scale, Is.EqualTo(1f));
                    rig.Steps(3);
                    Assert.That(HandsEvents(rig.Logged), Is.Empty, "the phone's event log");
                    Assert.That(HandsEvents(rig.HubEvents), Is.Empty, "the Hub's event log");
                }
            }

            // Bump Guard did the same thing in every frame, with the hands in the rig and without.
            Assert.That(prints[0].Count, Is.EqualTo(prints[1].Count));
            for (int k = 0; k < prints[0].Count; k++)
            {
                GuardPrint with = prints[0][k], without = prints[1][k];
                string at = "frame " + k;
                Assert.That(with.Level, Is.EqualTo(without.Level), at);
                Assert.That(with.Now, Is.EqualTo(without.Now), at);
                Assert.That(with.Path04, Is.EqualTo(without.Path04), at);
                Assert.That(with.Path08, Is.EqualTo(without.Path08), at);
                Assert.That(with.Cells, Is.EqualTo(without.Cells), at);
                Assert.That(with.Stamped, Is.EqualTo(without.Stamped), at);
            }
            Assert.That(changes[0].Count, Is.EqualTo(changes[1].Count));
            int raised = 0;
            for (int i = 0; i < changes[0].Count; i++)
            {
                Assert.That(changes[0][i].Frame, Is.EqualTo(changes[1][i].Frame), "change " + i);
                Assert.That(changes[0][i].From, Is.EqualTo(changes[1][i].From), "change " + i);
                Assert.That(changes[0][i].To, Is.EqualTo(changes[1][i].To), "change " + i);
                if (changes[0][i].Frame > raiseAt && changes[0][i].Frame <= lowerAt && changes[0][i].To != BumpLevel.Clear) raised++;
            }
            TestContext.WriteLine("lap with the hands off: {0} level changes, {1} of them a rise while the hands were up", changes[0].Count, raised);
            // The comparison is of something: unmasked, the raised hands do cue.
            Assert.That(raised, Is.GreaterThan(0), "raised hands in view never raised the level");
        }

        [Test]
        public void E2E_Hands_ParametersAreTheAppsOwn_DeclaredLast_AndDriveTheRuntime()
        {
            // What the composition root declares: HandsSystem.DeclareParams on a registry of its own.
            var reference = new ParamRegistry();
            new HandsSystem().DeclareParams(reference);
            string[] names =
            {
                HandsSystem.ParamEnabled, HandsSystem.ParamRate, HandsSystem.ParamReach, HandsSystem.ParamSound,
                HandsSystem.ParamDraw, HandsSystem.ParamEditor, HandsSystem.ParamBench, HandsSystem.ParamRecord,
            };
            Assert.That(reference.Count, Is.EqualTo(names.Length),
                "the app declares a hands parameter the rehearsal does not bind: give it a callback in RehearsalRig.DeclareHandsParams");

            using (var plain = new RehearsalRig(false))
            using (var rig = new RehearsalRig())
            {
                // After every other parameter: none of those changes its wire id.
                Assert.That(rig.Registry.Count, Is.EqualTo(plain.Registry.Count + names.Length));
                for (int i = 0; i < plain.Registry.Count; i++)
                {
                    Assert.That(rig.Registry[i].Name, Is.EqualTo(plain.Registry[i].Name), "wire id " + i);
                    Assert.That(rig.Registry[i].Name.StartsWith("hands.", StringComparison.Ordinal), Is.False, "wire id " + i);
                }

                // The same ids, kinds, ranges, defaults and order as the app's.
                for (int k = 0; k < names.Length; k++)
                {
                    ParamEntry entry = rig.Registry[plain.Registry.Count + k], app = reference[k];
                    Assert.That(app.Name, Is.EqualTo(names[k]), "the app's order");
                    Assert.That(entry.Name, Is.EqualTo(app.Name));
                    Assert.That(entry.Id, Is.EqualTo(plain.Registry.Count + k), entry.Name);
                    Assert.That(entry.Group, Is.EqualTo(HandsSystem.Group), entry.Name);
                    Assert.That(entry.Kind, Is.EqualTo(app.Kind), entry.Name);
                    Assert.That(entry.Min, Is.EqualTo(app.Min), entry.Name);
                    Assert.That(entry.Max, Is.EqualTo(app.Max), entry.Name);
                    Assert.That(entry.Default, Is.EqualTo(app.Default), entry.Name);
                    Assert.That(entry.Value, Is.EqualTo(app.Default), entry.Name + " starts at its default");
                    Assert.That(entry.OnRelease, Is.EqualTo(app.OnRelease), entry.Name);
                    Assert.That(entry.EnumCount, Is.EqualTo(app.EnumCount), entry.Name);
                    for (int n = 0; n < app.EnumCount; n++) Assert.That(entry.EnumName(n), Is.EqualTo(app.EnumName(n)), entry.Name);
                }
                Assert.That(rig.Registry.GetBool(HandsSystem.ParamEnabled, true), Is.False, "decision R8: off by default");
                Assert.That(rig.Registry.GetFloat(HandsSystem.ParamReach), Is.EqualTo(HandsConstants.ReachMetres));

                // Each drives the rehearsal's runtime as the app's callback drives the app's.
                HandsRuntime hands = rig.Hands;
                Assert.That(hands.Enabled, Is.False);
                Assert.That(hands.Sound && hands.Draw, Is.True);
                Assert.That(hands.Bench || hands.Record, Is.False);
                Assert.That(hands.Reach, Is.EqualTo(HandsConstants.ReachMetres));
                rig.Registry.Set(HandsSystem.ParamEnabled, 1f);
                Assert.That(hands.Enabled, Is.True);
                rig.Registry.Set(HandsSystem.ParamRate, 1f);
                Assert.That(hands.RateOverride, Is.EqualTo(HandsConstants.RateFull));
                rig.Registry.Set(HandsSystem.ParamRate, 2f);
                Assert.That(hands.RateOverride, Is.EqualTo(HandsConstants.RateReduced));
                rig.Registry.Set(HandsSystem.ParamRate, 0f);
                Assert.That(hands.RateOverride, Is.EqualTo(0));
                rig.Registry.Set(HandsSystem.ParamReach, 0.9f);
                Assert.That(hands.Reach, Is.EqualTo(HandsConstants.ReachMaxMetres), "clamped to its range");
                rig.Registry.Set(HandsSystem.ParamSound, 0f);
                Assert.That(hands.Sound, Is.False);
                rig.Registry.Set(HandsSystem.ParamDraw, 0f);
                Assert.That(hands.Draw, Is.False);
                Assert.That(hands.Hidden, Is.True);
                rig.Registry.Set(HandsSystem.ParamBench, 1f);
                Assert.That(hands.Bench, Is.True);
                // No folder to record into, and no Editor source: the two parameters are there and change nothing.
                Assert.DoesNotThrow(() => rig.Registry.Set(HandsSystem.ParamRecord, 1f));
                Assert.That(hands.Record, Is.False);
                Assert.DoesNotThrow(() => rig.Registry.Set(HandsSystem.ParamEditor, 1f));
            }
        }

        // ---- the session -----------------------------------------------------------------------------------

        [Test]
        public void E2E_Hands_SessionStartsAndStops()
        {
            RoomModel room = Room();
            using (RehearsalRig rig = Enter(room, new float2(0f, -0.5f)))
            {
                Assert.That(rig.Hands.Gate.Reason, Is.EqualTo(HandsOffReason.Param));

                // The Station switches the hands on: a ParamSet over the link, by the parameter's wire id.
                Assert.That(rig.Hub.SendParam(HandsSystem.ParamEnabled, 1f), Is.True, "the Hub has no phone");
                int took = rig.StepUntil(() => rig.Hands.Active, CommandFrames);
                Assert.That(took, Is.GreaterThanOrEqualTo(0), "the session did not start within 250 ms");
                Assert.That(rig.Registry.GetBool(HandsSystem.ParamEnabled), Is.True);
                Assert.That(rig.Script.Rate, Is.EqualTo(HandsConstants.RateFull), "the detector was asked for its full rate");
                AssertTheHubHasEveryHandsEvent(rig);
                Assert.That(RehearsalRig.CountOf(rig.HubEvents, "hands.on"), Is.EqualTo(1));
                Assert.That(RehearsalRig.LastOf(rig.HubEvents, "hands.on").Data.GetInt("hz"), Is.EqualTo(HandsConstants.RateFull));
                // A session with no hand in view: Bump Guard is told of the session, and of no region.
                rig.StepUntil(() => rig.Pipeline.Sensors.HasDepth, 8);
                DepthMask mask;
                Assert.That(rig.Hands.TryGetMask(rig.Pipeline.Sensors.Depth.Sequence, out mask), Is.True);
                Assert.That(mask.Session, Is.EqualTo(1));
                Assert.That(mask.Count, Is.EqualTo(0));

                // A hand comes into view.
                JointSet hand = Open(HandSide.Right, new float3(0.10f, -0.16f, 0.45f));
                rig.ShowHand(HandSide.Right, in hand);
                int entered = rig.StepUntil(() => rig.Hands.Pose(HandSide.Right).Tracked, 12);
                Assert.That(entered, Is.GreaterThanOrEqualTo(0), "the hand was not tracked within 0.2 s");
                int enteredFrame = rig.Frames;
                rig.Steps(60);
                HandPose pose = rig.Hands.Pose(HandSide.Right);
                Assert.That(pose.Tracked, Is.True);
                Assert.That(pose.Opacity, Is.EqualTo(1f), "shown in full a second after it came");
                Assert.That(pose.Confidence, Is.GreaterThanOrEqualTo(HandsConstants.InteractConfidence));
                Assert.That(rig.Hands.Pose(HandSide.Left).Tracked, Is.False);
                // It is where the walker holds it: the camera's joints, in the room.
                float3 tip = rig.Frame.RawHeadPosition + hand[HandJoint.IndexTip];
                Assert.That(math.distance(rig.Hands.Joint(HandSide.Right, HandJoint.IndexTip), tip), Is.LessThan(0.005f), "index fingertip, metres");
                Assert.That(((IReachLayout)rig.Hands).Near, Is.True, "panels come within reach in a hands session");
                AssertTheHubHasEveryHandsEvent(rig);
                Assert.That(RehearsalRig.CountOf(rig.HubEvents, "hands.enter"), Is.EqualTo(1));
                Assert.That(RehearsalRig.LastOf(rig.HubEvents, "hands.enter").Data.GetString("hand"), Is.EqualTo("R"));

                // It goes again, from the middle of the picture: lost, not at an edge.
                rig.HideHand(HandSide.Right);
                int gone = rig.StepUntil(() => !rig.Hands.Pose(HandSide.Right).Tracked, 60);
                TestContext.WriteLine("session on {0} frames after the Station's ParamSet; tracked {1} frames after the hand came, given up {2} frames after it went",
                    took, entered, gone);
                // The result that misses it is two frames old, then the grace period of decision R2.
                Assert.That(gone, Is.InRange(24, 32), "frames from the hand leaving to the end of tracking");
                float visibleMs = (rig.Frames - enteredFrame) * 1000f / 60f;
                AssertTheHubHasEveryHandsEvent(rig);
                Assert.That(RehearsalRig.CountOf(rig.HubEvents, "hands.exit"), Is.EqualTo(1));
                EventMessage exit = RehearsalRig.LastOf(rig.HubEvents, "hands.exit");
                Assert.That(exit.Data.GetString("hand"), Is.EqualTo("R"));
                Assert.That(exit.Data.GetString("cause"), Is.EqualTo("lost"), "a hand that vanished mid-picture");
                Assert.That(exit.Data.GetFloat("visibleMs"), Is.EqualTo(visibleMs).Within(20f));

                // And once more, this time out through the right-hand border of the picture.
                rig.Steps(30);
                const int slide = 40;
                for (int k = 0; k <= slide; k++)
                {
                    JointSet sliding = Open(HandSide.Right, new float3(math.lerp(0.10f, 0.285f, k / (float)slide), -0.16f, 0.45f));
                    rig.ShowHand(HandSide.Right, in sliding);
                    rig.Step();
                }
                Assert.That(rig.Hands.Pose(HandSide.Right).Tracked, Is.True);
                rig.HideHand(HandSide.Right);
                Assert.That(rig.StepUntil(() => !rig.Hands.Pose(HandSide.Right).Tracked, 60), Is.GreaterThanOrEqualTo(0));
                AssertTheHubHasEveryHandsEvent(rig);
                Assert.That(RehearsalRig.CountOf(rig.HubEvents, "hands.enter"), Is.EqualTo(2));
                Assert.That(RehearsalRig.CountOf(rig.HubEvents, "hands.exit"), Is.EqualTo(2));
                Assert.That(RehearsalRig.LastOf(rig.HubEvents, "hands.exit").Data.GetString("cause"), Is.EqualTo("edge"), "a hand that left through the border");

                // The Station switches them off.
                Assert.That(rig.Hub.SendParam(HandsSystem.ParamEnabled, 0f), Is.True);
                Assert.That(rig.StepUntil(() => !rig.Hands.Active, CommandFrames), Is.GreaterThanOrEqualTo(0), "the session did not stop within 250 ms");
                Assert.That(rig.Hands.Gate.Reason, Is.EqualTo(HandsOffReason.Param));
                Assert.That(rig.Script.Rate, Is.EqualTo(0), "the detector was stopped");
                Assert.That(rig.Script.Stops, Is.GreaterThanOrEqualTo(1));
                AssertTheHubHasEveryHandsEvent(rig);
                Assert.That(RehearsalRig.CountOf(rig.HubEvents, "hands.off"), Is.EqualTo(1));
                Assert.That(RehearsalRig.LastOf(rig.HubEvents, "hands.off").Data.GetString("reason"), Is.EqualTo("param"));
                Assert.That(RehearsalRig.CountOf(rig.HubEvents, "hands.stats"), Is.EqualTo(1));
                JsonValue stats = RehearsalRig.LastOf(rig.HubEvents, "hands.stats").Data;
                Assert.That(stats.GetString("source"), Is.EqualTo("script"));
                Assert.That(stats.GetFloat("hz"), Is.InRange(27f, 31f), "detections a second over the session");
                Assert.That(stats.GetFloat("ageMsP50"), Is.EqualTo(1000f * ScriptedHandsSource.DefaultAgeFrames / 60f).Within(0.5f));
                Assert.That(stats.GetInt("presses") + stats.GetInt("pinches"), Is.EqualTo(0));
                Assert.That(stats.GetInt("maskFrames"), Is.GreaterThan(10), "depth images that got a mask");
                Assert.That(stats.GetFloat("visibleR"), Is.InRange(0.2f, 0.9f));
                Assert.That(stats.GetFloat("visibleL"), Is.EqualTo(0f));
                rig.StepUntil(() => rig.Pipeline.Sensors.HasDepth, 8);
                Assert.That(rig.Hands.TryGetMask(rig.Pipeline.Sensors.Depth.Sequence, out mask), Is.False);
                Assert.That(mask.Session, Is.EqualTo(0));
                // The whole story, as the operator reads it in the Console's list.
                TestContext.WriteLine("the Hub's log: {0}", HandsEvents(rig.HubEvents));
                AssertHealthy(rig);
            }
        }

        [Test]
        public void E2E_Hands_AFlickeringHandIsLoggedInWholePairs()
        {
            RoomModel room = Room();
            using (RehearsalRig rig = Enter(room, new float2(0f, -0.5f)))
            {
                SwitchOn(rig);
                rig.Steps(10);
                // In view for 0.2 s and out of it for 0.6 s, three times: a hand at the edge of what the detector finds.
                JointSet hand = Open(HandSide.Right, new float3(0.10f, -0.16f, 0.45f));
                for (int coming = 0; coming < 3; coming++)
                {
                    rig.ShowHand(HandSide.Right, in hand);
                    rig.Steps(12);
                    rig.HideHand(HandSide.Right);
                    rig.Steps(36);
                }
                rig.Steps(60);
                rig.Registry.Set(HandsSystem.ParamEnabled, 0f);
                AssertTheHubHasEveryHandsEvent(rig);

                // One enter and one exit a second for each hand, as a pair (section 8.1). The second coming is 0.8 s
                // after the first: neither it nor its going is in the log, and both are counted. The third is.
                var pairs = new List<string>();
                foreach (EventMessage e in rig.HubEvents)
                    if (e.Ev == "hands.enter" || e.Ev == "hands.exit") pairs.Add(e.Ev);
                Assert.That(pairs, Is.EqualTo(new[] { "hands.enter", "hands.exit", "hands.enter", "hands.exit" }), HandsEvents(rig.HubEvents));
                Assert.That(RehearsalRig.LastOf(rig.HubEvents, "hands.stats").Data.GetInt("quietEvents"), Is.EqualTo(2), "events counted and not logged");
                AssertHealthy(rig);
            }
        }

        // ---- touch -------------------------------------------------------------------------------------------

        [Test]
        public void E2E_Hands_TouchFiresATarget()
        {
            RoomModel room = Room();
            using (RehearsalRig rig = Enter(room, new float2(0f, -0.5f)))
            {
                // A target within reach, 0.5 m from the camera and to one side of where the walker looks: the
                // gaze, straight ahead, is not on it, so only a finger can fire it.
                float3 head = rig.Standing.Position;
                float3 centre = head + new float3(0.12f, 0.02f, 0.48f);
                int calls = 0, calledTag = 0;
                GazeTargetId called = default(GazeTargetId);
                GazeTargetId target = AddTarget(rig, AtlasAction.Primary, centre, 0.05f, 7, "e2e.button", (id, tag) =>
                {
                    calls++;
                    called = id;
                    calledTag = tag;
                });
                SwitchOn(rig);

                // The fingertip rests 7 cm in front of the target's face.
                const float rest = 0.07f;
                PointAt(rig, centre, rest);
                rig.Steps(60);
                Assert.That(rig.Hands.Pose(HandSide.Right).Opacity, Is.EqualTo(1f));
                Assert.That(rig.Guard.Level, Is.EqualTo(BumpLevel.Clear));
                Assert.That(rig.Input.LastHands.Active && rig.Input.LastHands.Right.Valid, Is.True, "the fingertip reached input");
                Assert.That(rig.Input.HandsTouch.Right.Target, Is.EqualTo(target), "the fingertip is over the target");
                Assert.That(rig.Input.Core.GazeFocused.IsValid, Is.False, "the gaze is on nothing");

                // A press that stops 3 cm short of the face, and goes back: nothing.
                bool pressed = false;
                float nearest = float.MaxValue;
                Action watch = () =>
                {
                    pressed |= rig.Input.HandsTouch.Right.Pressed;
                    float3 seen = rig.Input.LastHands.Right.World;
                    nearest = math.min(nearest, math.dot(seen - centre, math.normalize(head - centre)));
                };
                Reach(rig, centre, rest, 0.03f, watch);
                Reach(rig, centre, 0.03f, rest, watch);
                TestContext.WriteLine("a press that stops 3 cm short: the fingertip input was given came to {0:0.0000} m in front of the face", nearest);
                Assert.That(nearest, Is.GreaterThan(0.029f), "input presses with the fingertip where the filter has it, not ahead of it (decision R12)");
                Assert.That(pressed, Is.False);
                Assert.That(rig.Input.Fired, Is.Empty, "a press that stopped short fired something");
                Assert.That(calls, Is.EqualTo(0));
                Assert.That(rig.HasEvent("hands.touch"), Is.False);

                // The press: 4 cm through the face, held, and back.
                int crossedAt = -1, firedAt = -1, frame = 0;
                Action clock = () =>
                {
                    frame++;
                    float x = math.saturate(frame / (float)ReachFrames);
                    float truth = math.lerp(rest, -0.04f, x * x * (3f - 2f * x));
                    if (crossedAt < 0 && truth <= -0.01f) crossedAt = frame;
                    if (firedAt < 0 && rig.Input.Fired.Count > 0) firedAt = frame;
                };
                Reach(rig, centre, rest, -0.04f, clock);
                Assert.That(firedAt, Is.GreaterThan(0), "the press fired nothing");
                TestContext.WriteLine("the press fired {0} frames after the fingertip itself passed the press plane", firedAt - crossedAt);
                Assert.That(firedAt - crossedAt, Is.InRange(0, 9), "frames from the real fingertip passing the press plane to the firing");
                Assert.That(rig.Input.HandsTouch.Right.Pressed, Is.True, "held in: still pressed");
                Reach(rig, centre, -0.04f, rest);
                rig.Steps(30);

                // Once, by the route the gaze takes: the target's own action, tag and callback, with the source Gaze.
                Assert.That(rig.Input.Fired.Count, Is.EqualTo(1), "firings");
                Assert.That(rig.Input.Fired[0].Action, Is.EqualTo(AtlasAction.Primary));
                Assert.That(rig.Input.Fired[0].Source, Is.EqualTo(InputSource.Gaze), "decision R1: a touch fires as the gaze does");
                Assert.That(rig.Input.Fired[0].Tag, Is.EqualTo(7));
                Assert.That(rig.Input.LastSource(AtlasAction.Primary), Is.EqualTo(InputSource.Gaze));
                Assert.That(rig.Input.LastTag(AtlasAction.Primary), Is.EqualTo(7));
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(called, Is.EqualTo(target));
                Assert.That(calledTag, Is.EqualTo(7));
                Assert.That(rig.Input.HandsTouch.Right.Pressed, Is.False, "let go");

                // Logged with the target's label, and at the Hub.
                AssertTheHubHasEveryHandsEvent(rig);
                Assert.That(RehearsalRig.CountOf(rig.HubEvents, "hands.touch"), Is.EqualTo(1));
                JsonValue touch = RehearsalRig.LastOf(rig.HubEvents, "hands.touch").Data;
                Assert.That(touch.GetString("hand"), Is.EqualTo("R"));
                Assert.That(touch.GetString("label"), Is.EqualTo("e2e.button"));
                Assert.That(touch.GetInt("tag"), Is.EqualTo(7));
                Assert.That(rig.HasEvent("hands.pinch"), Is.False);
                Assert.That(rig.BumpChanges, Is.Empty, "Bump Guard stayed clear of the hand");

                rig.Registry.Set(HandsSystem.ParamEnabled, 0f);
                rig.Steps(4);
                Assert.That(RehearsalRig.LastOf(rig.Logged, "hands.stats").Data.GetInt("presses"), Is.EqualTo(1));
                AssertHealthy(rig);
            }
        }

        // ---- pinch -------------------------------------------------------------------------------------------

        /// <summary>The head turned this far from straight ahead no longer has the far target under the gaze. Radians.</summary>
        const float LookingAway = -0.25f;

        /// <summary>
        /// The recorded pinching hand before a walker at the room's centre, and a target 1.2 m straight ahead of
        /// where the recording's head looks: too far to touch. The recording's hand is where it was recorded,
        /// wherever the rehearsal's head turns.
        /// </summary>
        static RehearsalRig EnterWithThePinchingHand(RoomModel room, float yaw, out ReplayHandsSource replay, out GazeTargetId target, List<int> calls)
        {
            replay = ReplayHandsSource.FromFile(HandLog(PinchLog));
            RehearsalRig rig = Enter(room, float2.zero, yaw, replay);
            target = AddTarget(rig, AtlasAction.Confirm, new float3(0f, RehearsalRig.EyeHeight, 1.2f), 0.08f, 11, "e2e.far", (id, tag) => calls.Add(tag));
            return rig;
        }

        [Test]
        public void E2E_Hands_PinchConfirmsTheGazedTarget()
        {
            RoomModel room = Room();
            var calls = new List<int>();
            ReplayHandsSource replay;
            GazeTargetId target;
            using (RehearsalRig rig = EnterWithThePinchingHand(room, LookingAway, out replay, out target, calls))
            {
                SwitchOn(rig);
                const int lookAt = 24;
                int downs = 0, firstDown = -1;
                bool looking = false, firedOtherwise = false;
                for (int frame = 0; frame < 300 && downs < 3; frame++)
                {
                    // The walker looks at the target 0.4 s into the session, the hand open: the first pinch
                    // closes before the dwell is two thirds through. After the second pinch the walker looks away.
                    if (frame == lookAt)
                    {
                        rig.StandAt(float2.zero, 0f);
                        looking = true;
                    }
                    if (looking && downs == 2 && !rig.Hands.Pinch(HandSide.Right).Pinching)
                    {
                        rig.StandAt(float2.zero, LookingAway);
                        looking = false;
                    }
                    int fired = rig.Input.Fired.Count;
                    rig.Step();
                    bool down = rig.Hands.Pinch(HandSide.Right).Down;
                    string at = "frame " + frame;
                    if (down)
                    {
                        downs++;
                        if (firstDown < 0) firstDown = frame;
                    }
                    if (down && downs <= 2)
                    {
                        // At once: in the frame the pinch closes, by the route the gaze takes.
                        Assert.That(rig.Input.Fired.Count, Is.EqualTo(fired + 1), at + ": the pinch fired nothing");
                        InputFiring firing = rig.Input.Fired[fired];
                        Assert.That(firing.Action, Is.EqualTo(AtlasAction.Confirm), at);
                        Assert.That(firing.Source, Is.EqualTo(InputSource.Gaze), at + ", decision R1");
                        Assert.That(firing.Tag, Is.EqualTo(11), at);
                        Assert.That(rig.Input.HandsTouch.PinchFired, Is.True, at);
                        Assert.That(rig.Input.HandsTouch.PinchTarget, Is.EqualTo(target), at);
                    }
                    else if (rig.Input.Fired.Count != fired)
                    {
                        firedOtherwise = true;
                    }
                }
                Assert.That(downs, Is.EqualTo(3), "the recording pinches three times");
                int dwellFrames = (int)math.round(rig.Input.DwellSeconds * 60f);
                TestContext.WriteLine("the first pinch closed {0} frames after the gaze came to the target (the dwell takes {1})", firstDown - lookAt, dwellFrames);
                Assert.That(firstDown - lookAt, Is.InRange(1, dwellFrames - 1), "the pinch did not come before the dwell");
                // Two pinches with the target under the gaze, one with nothing under it; the dwell never got to fire.
                Assert.That(firedOtherwise, Is.False, "the target fired without a pinch on it: by the dwell, or by the pinch made looking away");
                Assert.That(rig.Input.Fired.Count, Is.EqualTo(2));
                Assert.That(calls, Is.EqualTo(new[] { 11, 11 }));

                AssertTheHubHasEveryHandsEvent(rig);
                Assert.That(RehearsalRig.CountOf(rig.HubEvents, "hands.pinch"), Is.EqualTo(2));
                JsonValue pinch = RehearsalRig.LastOf(rig.HubEvents, "hands.pinch").Data;
                Assert.That(pinch.GetString("hand"), Is.EqualTo("R"));
                Assert.That(pinch.GetString("label"), Is.EqualTo("e2e.far"));
                Assert.That(rig.HasEvent("hands.touch"), Is.False);
                AssertHealthy(rig);
            }
        }

        [Test]
        public void E2E_Hands_PinchWithNothingUnderTheGazeFiresNothing()
        {
            RoomModel room = Room();
            var calls = new List<int>();
            ReplayHandsSource replay;
            GazeTargetId target;
            using (RehearsalRig rig = EnterWithThePinchingHand(room, LookingAway, out replay, out target, calls))
            {
                SwitchOn(rig);
                int downs = 0, ups = 0;
                while (!replay.Ended && rig.Frames < 2000)
                {
                    rig.Step();
                    if (rig.Hands.Pinch(HandSide.Right).Down) downs++;
                    if (rig.Hands.Pinch(HandSide.Right).Up) ups++;
                    Assert.That(rig.Input.Core.GazeFocused.IsValid, Is.False, "the gaze came onto the target");
                    Assert.That(rig.Input.HandsTouch.PinchFired, Is.False);
                }
                Assert.That(replay.Ended, Is.True);
                // The hand did pinch, three times, and the pinches were handed to input.
                Assert.That(downs, Is.EqualTo(3));
                Assert.That(ups, Is.EqualTo(3));
                Assert.That(rig.Input.Fired, Is.Empty, "a pinch at nothing fired something");
                Assert.That(calls, Is.Empty);
                rig.Registry.Set(HandsSystem.ParamEnabled, 0f);
                AssertTheHubHasEveryHandsEvent(rig);
                Assert.That(rig.HasEvent("hands.pinch"), Is.False);
                Assert.That(RehearsalRig.LastOf(rig.HubEvents, "hands.stats").Data.GetInt("pinches"), Is.EqualTo(0));
                Assert.That(RehearsalRig.LastOf(rig.HubEvents, "hands.stats").Data.GetString("source"), Is.EqualTo("replay"));
                AssertHealthy(rig);
            }
        }

        // ---- Bump Guard ----------------------------------------------------------------------------------------

        [Test]
        public void E2E_Hands_MaskKeepsBumpGuardClear()
        {
            RoomModel room = Room();
            // 1.2 m along the middle of the room at 0.5 m/s, both hands raised 0.45 m ahead all the way. The far
            // wall is never nearer than a metre: nothing of the room warns on this walk.
            float2 from = new float2(0f, -0.7f), to = new float2(0f, 0.5f);
            PoseLog walk = RehearsalRig.Line(room.Id, from, to, 0.5f);
            const int standFrames = 60;

            // ---- with the session's mask ----
            int images = 0, handSamples = 0;
            using (RehearsalRig rig = Enter(room, from))
            {
                SwitchOn(rig);
                rig.Steps(30);
                RaiseBothHands(rig);
                Func<bool> bothTracked = () => rig.Hands.Pose(HandSide.Left).Tracked && rig.Hands.Pose(HandSide.Right).Tracked;
                Assert.That(rig.StepUntil(bothTracked, 12), Is.GreaterThanOrEqualTo(0), "the raised hands were not tracked within 0.2 s");
                // A second standing: what the hands left in Bump Guard's map before they were known is forgotten.
                rig.Steps(standFrames);
                Assert.That(rig.Guard.Level, Is.EqualTo(BumpLevel.Clear));
                Assert.That(rig.Guard.Core.DynamicCellCount, Is.EqualTo(0), "cells the hands left before they were tracked");
                rig.BumpChanges.Clear();

                float worstOffset = 0f;
                rig.Play(walk);
                for (int k = 0; k < walk.Samples.Length + 60; k++)
                {
                    if (k == walk.Samples.Length) rig.StandAt(in walk.Samples[walk.Samples.Length - 1]);
                    rig.Step();
                    string at = "frame " + k + ", head at z " + rig.HeadFloor.y;
                    Assert.That(rig.Guard.Level, Is.EqualTo(BumpLevel.Clear), at);
                    Assert.That(bothTracked(), Is.True, at);
                    if (!rig.Pipeline.Sensors.HasDepth) continue;

                    // The hands are in the image Bump Guard has just read...
                    images++;
                    DepthFrame depth = rig.Pipeline.Sensors.Depth;
                    int hands = NearSamples(in depth);
                    handSamples += hands;
                    Assert.That(hands, Is.GreaterThan(30), "the hands are not in the depth image, " + at);

                    // ...the mask the guard step handed it is the one built for that very image...
                    DepthMask mask;
                    Assert.That(rig.Hands.TryGetMask(depth.Sequence, out mask), Is.True, at);
                    Assert.That(mask.DepthSequence, Is.EqualTo(depth.Sequence), at);
                    Assert.That(mask.Session, Is.EqualTo(1), at);
                    Assert.That(mask.Count, Is.EqualTo(2), "one region for each hand, " + at);
                    DepthMask other;
                    Assert.That(rig.Hands.TryGetMask(depth.Sequence + 1, out other), Is.True, at);
                    Assert.That(other.Count, Is.EqualTo(0), "no region is offered for another image, " + at);

                    // ...and Bump Guard took out the hands with it: every sample of theirs, and no other.
                    DepthFrameResult result = rig.Guard.Core.LastDepth;
                    Assert.That(result.MaskRefused, Is.False, at);
                    Assert.That(result.Masked, Is.EqualTo(hands), "samples Bump Guard masked against samples on the hands, " + at);
                    Assert.That(rig.Guard.Core.DynamicCellCount, Is.EqualTo(0), "the hands left something in Bump Guard's map, " + at);

                    worstOffset = math.max(worstOffset, AssertCoveredWithHalfAPixelToSpare(rig, in depth, in mask, at));
                }
                Assert.That(images, Is.GreaterThan(walk.Samples.Length / 4));
                Assert.That(rig.BumpChanges, Is.Empty, "the level changed during the walk");
                int grid = (SyntheticDepthSource.ImageWidth / BumpConstants.DepthStride) * (SyntheticDepthSource.ImageHeight / BumpConstants.DepthStride);
                TestContext.WriteLine("masked walk: {0} depth images, {1} hand samples of {2} in each, level Clear throughout; " +
                                      "the mask's rectangles are centred {3:0.00} depth pixels from the hands as Bump Guard reads them",
                    images, handSamples / images, grid, worstOffset);
                AssertHealthy(rig);
            }

            // ---- the same walk with the hands off: they are in the depth image all the same ----
            using (RehearsalRig rig = Enter(room, from))
            {
                rig.Steps(30);
                RaiseBothHands(rig);
                rig.Steps(12 + standFrames);
                Assert.That(rig.Guard.Level, Is.EqualTo(BumpLevel.Clear), "standing, hands 0.45 m ahead are not within 0.30 m");
                rig.BumpChanges.Clear();

                rig.Play(walk);
                for (int k = 0; k < walk.Samples.Length + 60; k++)
                {
                    if (k == walk.Samples.Length) rig.StandAt(in walk.Samples[walk.Samples.Length - 1]);
                    rig.Step();
                    if (!rig.Pipeline.Sensors.HasDepth) continue;
                    DepthFrame depth = rig.Pipeline.Sensors.Depth;
                    Assert.That(NearSamples(in depth), Is.GreaterThan(30), "the hands are not in the depth image");
                    Assert.That(rig.Guard.Core.LastDepth.Masked, Is.EqualTo(0));
                }
                Assert.That(rig.Hands.Active, Is.False);
                Assert.That(rig.BumpChanges.Count, Is.GreaterThan(0), "the walker's own raised hands did not cue with the hands off");
                BumpChange first = rig.BumpChanges[0];
                Assert.That(first.From, Is.EqualTo(BumpLevel.Clear));
                Assert.That(first.To, Is.Not.EqualTo(BumpLevel.Clear));
                Assert.That(first.To, Is.Not.EqualTo(BumpLevel.TrackingLost));
                BumpLevel worst = BumpLevel.Clear;
                foreach (BumpChange c in rig.BumpChanges) if (c.To != BumpLevel.TrackingLost && c.To > worst) worst = c.To;
                TestContext.WriteLine("unmasked walk: {0} at {1:0.00} m into it, worst level {2}, {3} cells left in Bump Guard's map",
                    first.To, math.distance(first.Floor, from), worst, rig.Guard.Core.DynamicCellCount);
            }
        }

        /// <summary>
        /// Every sample on the hands lies inside the mask with half a pixel to spare on every side, read at the
        /// sample's index and at its centre alike. Returns how far the centre of a mask rectangle is from the
        /// centre of the hand it was built for, as Bump Guard reads it, in depth pixels (the larger of the two hands').
        ///
        /// DIFFERS FROM "the pixel conventions agree" (reported; the two files are not this package's). The mask
        /// builder writes a region with the centre of sample (u, v) at (u, v) (HandsMask.cs: u = Cx + x / z * Fx
        /// - 0.5), and Bump Guard tests sample (u, v) at (u + 0.5, v + 0.5) (BumpDepthKernel.Process). In Bump
        /// Guard's reading the mask therefore sits half a depth pixel up and to the left of the hand: 4.8 mm at
        /// 0.45 m in this 64-pixel image, 1.1 mm in the phone's 256. The mask's growth of 35 % hides it. What is
        /// asserted is what must hold whichever of the two conventions is made the common one: the hands are
        /// covered with that half pixel to spare, and the two readings are no farther apart than half a pixel.
        /// </summary>
        static float AssertCoveredWithHalfAPixelToSpare(RehearsalRig rig, in DepthFrame depth, in DepthMask mask, string at)
        {
            int stride = BumpConstants.DepthStride, first = BumpDepthKernel.FirstSample(stride);
            for (int v = first; v < depth.Height; v += stride)
            {
                for (int u = first; u < depth.Width; u += stride)
                {
                    float d = depth.Depth[v * depth.Width + u];
                    if (!(d >= BumpConstants.DepthMin && d <= BumpConstants.MaskMaxDepth)) continue;
                    for (int corner = 0; corner < 4; corner++)
                    {
                        float du = (corner & 1) == 0 ? -0.5f : 1f, dv = (corner & 2) == 0 ? -0.5f : 1f;
                        Assert.That(mask.Contains(u + du, v + dv, d), Is.True, "sample (" + u + ", " + v + ") at " + d + " m, " + at);
                    }
                }
            }

            // The hands' own centres in the image, at their pixels' centres (DepthFrame.Unproject's (u + 0.5, v + 0.5)).
            float worst = 0f;
            for (int side = 0; side < 2; side++)
            {
                DepthMaskRegion region = side == 0 ? mask.Region0 : mask.Region1;
                float2 low = new float2(float.MaxValue), high = new float2(float.MinValue);
                for (int joint = 0; joint < HandJoints.Count; joint++)
                {
                    float3 c = math.mul(math.inverse(depth.Rotation), rig.Hands.Joint((HandSide)side, (HandJoint)joint) - depth.Position);
                    float2 pixel = new float2(depth.Cx + c.x / c.z * depth.Fx, depth.Cy - c.y / c.z * depth.Fy);
                    low = math.min(low, pixel);
                    high = math.max(high, pixel);
                }
                // The rectangle as Bump Guard reads it: a sample is inside when its centre, index + 0.5, is.
                float2 rectangle = 0.5f * new float2(region.MinU + region.MaxU, region.MinV + region.MaxV);
                float2 offset = math.abs(rectangle - 0.5f * (low + high));
                Assert.That(math.cmax(offset), Is.LessThanOrEqualTo(0.55f), "the mask's rectangle against the hand, depth pixels, " + at);
                worst = math.max(worst, math.cmax(offset));
            }
            return worst;
        }

        [Test]
        public void E2E_Hands_HideDuringAWarning()
        {
            RoomModel room = Room();
            // 0.3 m in front of the wall behind the walker: inside Bump Guard's warning distance.
            float2 atTheWall = new float2(0f, -1.2f), clearOfIt = new float2(0f, -0.6f);
            using (RehearsalRig rig = Enter(room, atTheWall))
            {
                rig.Steps(30);
                Assert.That(rig.Guard.Level, Is.EqualTo(BumpLevel.Warn), "0.3 m from a wall is a warning");
                SwitchOn(rig);

                float3 centre = rig.Standing.Position + new float3(0.12f, 0.02f, 0.48f);
                AddTarget(rig, AtlasAction.Primary, centre, 0.05f, 1, "e2e.atTheWall");
                const float rest = 0.07f;
                PointAt(rig, centre, rest);
                rig.Steps(60);

                // While Bump Guard warns: the hand is tracked, and not shown; touch is suspended.
                Assert.That(rig.Hands.Active, Is.True, "a warning hides the hands, it does not end the session");
                Assert.That(rig.Hands.Pose(HandSide.Right).Tracked, Is.True);
                Assert.That(rig.Hands.Pose(HandSide.Right).Opacity, Is.EqualTo(0f));
                Assert.That(rig.Input.LastHands.Active, Is.False);
                Action hidden = () =>
                {
                    Assert.That(rig.Guard.Level, Is.Not.EqualTo(BumpLevel.Clear));
                    Assert.That(rig.Hands.Pose(HandSide.Right).Opacity, Is.EqualTo(0f));
                    Assert.That(rig.Input.LastHands.Active, Is.False);
                    Assert.That(rig.Input.HandsTouch.Right.Target.IsValid, Is.False);
                };
                Reach(rig, centre, rest, -0.04f, hidden);
                Reach(rig, centre, -0.04f, rest, hidden);
                Assert.That(rig.Input.Fired, Is.Empty, "a press fired while Bump Guard warned");
                Assert.That(rig.HasEvent("hands.touch"), Is.False);
                // A hidden hand is still before the LiDAR: it is still masked.
                rig.StepUntil(() => rig.Pipeline.Sensors.HasDepth, 8);
                DepthMask mask;
                Assert.That(rig.Hands.TryGetMask(rig.Pipeline.Sensors.Depth.Sequence, out mask), Is.True);
                Assert.That(mask.Count, Is.EqualTo(1));

                // The walker steps away from the wall, facing the same way. Once the level is Clear the hand
                // comes back over 0.3 s and touch with it.
                int cleared = -1, shown = -1;
                BumpLevel before = rig.Guard.Level;
                PoseLog away = Glide(room.Id, atTheWall, clearOfIt, 0.5f);
                rig.Play(away);
                for (int k = 0; k < away.Samples.Length + 60; k++)
                {
                    if (k == away.Samples.Length) rig.StandAt(clearOfIt);
                    rig.Step();
                    float opacity = rig.Hands.Pose(HandSide.Right).Opacity;
                    string at = "frame " + k;
                    // The hands' step runs before Bump Guard's: it goes by the level of the frame before.
                    if (before != BumpLevel.Clear)
                    {
                        Assert.That(opacity, Is.EqualTo(0f), at);
                        Assert.That(rig.Input.LastHands.Active, Is.False, at);
                    }
                    else
                    {
                        Assert.That(rig.Input.LastHands.Active, Is.True, at);
                    }
                    if (cleared < 0 && rig.Guard.Level == BumpLevel.Clear) cleared = k;
                    if (shown < 0 && opacity >= 1f) shown = k;
                    before = rig.Guard.Level;
                }
                Assert.That(cleared, Is.GreaterThanOrEqualTo(0), "the level never cleared");
                Assert.That(shown, Is.GreaterThan(cleared), "the hand was not shown again");
                TestContext.WriteLine("the level cleared {0:0.00} m from the wall; the hand was back in full {1} frames later", 0.3f + 0.5f * cleared / 60f, shown - cleared);
                Assert.That(shown - cleared, Is.InRange(17, 21), "back over 0.3 s (WarningBackSeconds), a frame after the level");
                Assert.That(rig.Hands.Pose(HandSide.Right).Opacity, Is.EqualTo(1f));

                // Touch is back: the same press, at a target where the walker now stands, fires.
                float3 second = rig.Standing.Position + new float3(0.12f, 0.02f, 0.48f);
                AddTarget(rig, AtlasAction.Secondary, second, 0.05f, 2, "e2e.clearOfTheWall");
                PointAt(rig, second, rest);
                rig.Steps(30);
                Reach(rig, second, rest, -0.04f);
                Reach(rig, second, -0.04f, rest);
                Assert.That(rig.Input.Fired.Count, Is.EqualTo(1), "touch did not come back with the hand");
                Assert.That(rig.Input.Fired[0].Action, Is.EqualTo(AtlasAction.Secondary));
                Assert.That(rig.Guard.Level, Is.EqualTo(BumpLevel.Clear));

                // Back towards the wall: the hand is gone within 0.1 s of the level leaving Clear, and touch at once.
                int warned = -1, gone = -1;
                PoseLog back = Glide(room.Id, clearOfIt, atTheWall, 0.5f);
                rig.Play(back);
                before = rig.Guard.Level;
                for (int k = 0; k < back.Samples.Length + 30; k++)
                {
                    if (k == back.Samples.Length) rig.StandAt(atTheWall);
                    rig.Step();
                    string at = "frame " + k;
                    Assert.That(rig.Input.LastHands.Active, Is.EqualTo(before == BumpLevel.Clear), at);
                    if (warned < 0 && rig.Guard.Level != BumpLevel.Clear) warned = k;
                    if (warned >= 0 && gone < 0 && rig.Hands.Pose(HandSide.Right).Opacity <= 0f) gone = k;
                    if (gone >= 0) Assert.That(rig.Hands.Pose(HandSide.Right).Opacity, Is.EqualTo(0f), at);
                    before = rig.Guard.Level;
                }
                Assert.That(warned, Is.GreaterThanOrEqualTo(0), "walking backwards into the wall did not warn");
                Assert.That(gone - warned, Is.InRange(5, 8), "frames from the level leaving Clear to the hand being gone (WarningOutSeconds, a frame late)");
                Assert.That(rig.Hands.Pose(HandSide.Right).Tracked, Is.True);
                Assert.That(rig.Input.Fired.Count, Is.EqualTo(1));
                Assert.That(rig.HasEvent("hands.off"), Is.False, "the session ran on through both warnings");
                AssertHealthy(rig);
            }
        }

        /// <summary>A walk at a steady speed with the head facing +Z all the way, whichever way it goes: a step back, a step forward.</summary>
        static PoseLog Glide(string roomId, float2 from, float2 to, float speed)
        {
            float length = math.distance(from, to);
            int count = (int)math.ceil(length / speed * 60f) + 1;
            var samples = new HeadPose[count];
            for (int k = 0; k < count; k++)
            {
                float2 p = math.lerp(from, to, math.min(1f, speed * k / 60f / length));
                samples[k] = new HeadPose(k / 60.0, new float3(p.x, RehearsalRig.EyeHeight, p.y), quaternion.identity, TrackingState.Normal);
            }
            return new PoseLog { RoomId = roomId ?? "", Frame = PoseFrameKind.Room, Samples = samples };
        }

        // ---- the gate ------------------------------------------------------------------------------------------

        /// <summary>The Station forces a quality tier; steps until the phone's frame runs at it.</summary>
        static void ForceTier(RehearsalRig rig, QualityTier tier)
        {
            Assert.That(rig.Hub.SendCommand(OperatorCommand.Quality((int)tier)), Is.True, "the Hub has no phone");
            Assert.That(rig.StepUntil(() => rig.Frame.Tier == tier, 75), Is.GreaterThanOrEqualTo(0), tier + " was not in force within a second");
        }

        /// <summary>The gate's own events of a log, in order: "on 30", "rate 15 tier", "off heat".</summary>
        static List<string> GateEvents(List<EventMessage> log)
        {
            var events = new List<string>();
            for (int i = 0; i < log.Count; i++)
            {
                JsonValue data = log[i].Data;
                if (log[i].Ev == "hands.on") events.Add("on " + data.GetInt("hz"));
                else if (log[i].Ev == "hands.rate") events.Add("rate " + data.GetInt("hz") + " " + data.GetString("why"));
                else if (log[i].Ev == "hands.off") events.Add("off " + data.GetString("reason"));
            }
            return events;
        }

        [Test]
        public void E2E_Hands_GiveWayToHeat()
        {
            RoomModel room = Room();
            using (RehearsalRig rig = Enter(room, new float2(0f, -0.5f)))
            {
                JointSet hand = Open(HandSide.Right, new float3(0.10f, -0.16f, 0.45f));
                rig.ShowHand(HandSide.Right, in hand);
                SwitchOn(rig);
                rig.Steps(60);
                Assert.That(rig.Frame.Tier, Is.EqualTo(QualityTier.Nominal));
                Assert.That(rig.Script.Rate, Is.EqualTo(30), "Nominal");
                Assert.That(rig.Hands.Pose(HandSide.Right).Tracked, Is.True);

                // Fair: the detector at half its rate, the hands still there.
                ForceTier(rig, QualityTier.Fair);
                rig.Steps(2);
                Assert.That(rig.Hands.Active, Is.True);
                Assert.That(rig.Script.Rate, Is.EqualTo(15), "Fair");
                int results = rig.Script.Results;
                rig.Steps(60);
                Assert.That(rig.Script.Results - results, Is.EqualTo(15), "results in a second at Fair");
                Assert.That(rig.Hands.Pose(HandSide.Right).Opacity, Is.EqualTo(1f));

                // Serious: the hands are the first thing to go. The panels stay where they are, to be worked by gaze.
                ForceTier(rig, QualityTier.Serious);
                rig.Steps(2);
                Assert.That(rig.Hands.Active, Is.False);
                Assert.That(rig.Hands.Gate.Reason, Is.EqualTo(HandsOffReason.Heat));
                Assert.That(rig.Script.Rate, Is.EqualTo(0), "Serious: the detector is stopped");
                results = rig.Script.Results;
                rig.Steps(90);
                Assert.That(rig.Script.Results, Is.EqualTo(results), "no frame is read by a stopped detector");
                Assert.That(rig.Hands.Pose(HandSide.Right).Tracked, Is.False);
                Assert.That(rig.Hands.Pose(HandSide.Right).Opacity, Is.EqualTo(0f));
                Assert.That(rig.Input.LastHands.Active, Is.False);
                Assert.That(((IReachLayout)rig.Hands).Near, Is.True, "panels stay within reach when hands stop for heat");
                DepthMask mask;
                Assert.That(rig.Hands.TryGetMask(rig.Pipeline.Sensors.Depth.Sequence, out mask), Is.False);

                ForceTier(rig, QualityTier.Critical);
                rig.Steps(30);
                Assert.That(rig.Hands.Active, Is.False);
                Assert.That(rig.Script.Rate, Is.EqualTo(0), "Critical");

                // And back, a rung at a time.
                ForceTier(rig, QualityTier.Fair);
                rig.Steps(2);
                Assert.That(rig.Hands.Active, Is.True);
                Assert.That(rig.Script.Rate, Is.EqualTo(15), "Fair again");
                rig.Steps(30);
                Assert.That(rig.Hands.Pose(HandSide.Right).Tracked, Is.True, "the hand is found again");
                ForceTier(rig, QualityTier.Nominal);
                rig.Steps(2);
                Assert.That(rig.Script.Rate, Is.EqualTo(30), "Nominal again");
                rig.Steps(30);
                Assert.That(rig.Hands.Pose(HandSide.Right).Opacity, Is.EqualTo(1f));

                // Each step is in the log, once, with what caused it; and at the Hub.
                AssertTheHubHasEveryHandsEvent(rig);
                Assert.That(GateEvents(rig.HubEvents), Is.EqualTo(new[] { "on 30", "rate 15 tier", "off heat", "on 15", "rate 30 tier" }));
                Assert.That(RehearsalRig.CountOf(rig.HubEvents, "hands.stats"), Is.EqualTo(1), "one with the one hands.off");
                TestContext.WriteLine("the Hub's log: {0}", HandsEvents(rig.HubEvents));
                AssertHealthy(rig);
            }
        }

        [Test]
        public void E2E_Hands_AGamepadSwitchesThemOff()
        {
            RoomModel room = Room();
            using (RehearsalRig rig = Enter(room, new float2(0f, -0.5f)))
            {
                JointSet hand = Open(HandSide.Right, new float3(0.10f, -0.16f, 0.45f));
                rig.ShowHand(HandSide.Right, in hand);
                SwitchOn(rig);
                rig.Steps(90);
                Assert.That(rig.Hands.Pose(HandSide.Right).Opacity, Is.EqualTo(1f));
                Assert.That(((IReachLayout)rig.Hands).Near, Is.True);

                // A gamepad that is gone again within the second changes nothing.
                rig.Input.GamepadConnected = true;
                rig.Steps(30);
                rig.Input.GamepadConnected = false;
                rig.Steps(90);
                Assert.That(rig.Hands.Active, Is.True);
                Assert.That(rig.HasEvent("hands.off"), Is.False);

                // One that stays ends the session a second later (decision H3): the detector stops, the hands
                // fade, nothing is handed to input or to Bump Guard, and the panels go back.
                rig.Input.GamepadConnected = true;
                int off = rig.StepUntil(() => !rig.Hands.Active, 120);
                Assert.That(off, Is.InRange(60, 63), "frames from the gamepad connecting to the session ending (a second's hold)");
                Assert.That(rig.Hands.Gate.Reason, Is.EqualTo(HandsOffReason.Pad));
                Assert.That(rig.Script.Rate, Is.EqualTo(0));
                int hidden = rig.StepUntil(() => rig.Hands.Pose(HandSide.Right).Opacity <= 0f, 30);
                Assert.That(hidden, Is.InRange(10, 13), "frames of the fade-out (GateOutSeconds)");
                int far = rig.StepUntil(() => !((IReachLayout)rig.Hands).Near, 120);
                Assert.That(far, Is.GreaterThanOrEqualTo(0), "the panels did not go back to their usual distance");
                TestContext.WriteLine("gamepad: session off after {0} frames, hands gone {1} frames later, panels back {2} frames after that", off, hidden, far);
                rig.Steps(60);
                Assert.That(rig.Hands.Pose(HandSide.Right).Tracked, Is.False);
                Assert.That(rig.Input.LastHands.Active, Is.False);
                DepthMask mask;
                Assert.That(rig.Hands.TryGetMask(rig.Pipeline.Sensors.Depth.Sequence, out mask), Is.False);
                Assert.That(mask.Session, Is.EqualTo(0));
                int results = rig.Script.Results;
                rig.Steps(60);
                Assert.That(rig.Script.Results, Is.EqualTo(results), "the detector still ran");

                AssertTheHubHasEveryHandsEvent(rig);
                Assert.That(GateEvents(rig.HubEvents), Is.EqualTo(new[] { "on 30", "off pad" }));
                Assert.That(RehearsalRig.CountOf(rig.HubEvents, "hands.stats"), Is.EqualTo(1));

                // The gamepad is put away: the hands come back the same way.
                rig.Input.GamepadConnected = false;
                int on = rig.StepUntil(() => rig.Hands.Active, 120);
                Assert.That(on, Is.InRange(60, 63));
                rig.Steps(60);
                Assert.That(rig.Hands.Pose(HandSide.Right).Opacity, Is.EqualTo(1f));
                Assert.That(GateEvents(rig.Logged), Is.EqualTo(new[] { "on 30", "off pad", "on 30" }));
                AssertHealthy(rig);
            }
        }

        // ---- a recorded hand -----------------------------------------------------------------------------------

        [Test]
        public void E2E_Hands_ReplayedRecording()
        {
            RoomModel room = Room();
            // A pointing right hand whose fingertip goes from 0.40 m to 0.52 m from the camera and back, three
            // times in 5.5 s, on the line through (0.04, 0.02) m (recordings/README.md); played round and round.
            ReplayHandsSource replay = ReplayHandsSource.FromFile(HandLog(PressLog), true);
            Assert.That(replay.Log.Count, Is.EqualTo(166));
            double lapSeconds = replay.Log.Duration + replay.Log.Duration / (replay.Log.Count - 1);
            int lapFrames = (int)math.round(lapSeconds * 60.0);

            // The recording's head: at the room's centre, eye height 1.6 m, looking along +Z.
            using (RehearsalRig rig = Enter(room, float2.zero, 0f, replay))
            {
                // A small target on the fingertip's line at the default reach. The gaze, straight ahead, is not on it.
                float3 centre = rig.Standing.Position + new float3(0.04f, 0.02f, HandsConstants.ReachMetres);
                AddTarget(rig, AtlasAction.Primary, centre, 0.03f, 3, "e2e.press");
                SwitchOn(rig);

                // ---- the first time through: the hand is tracked all the way and presses three times ----
                int firstTracked = -1, trusted = 0, untrusted = 0;
                float lowestConfidence = 1f;
                for (int k = 0; k < lapFrames; k++)
                {
                    rig.Step();
                    Assert.That(rig.Input.Core.GazeFocused.IsValid, Is.False, "the gaze came onto the target");
                    bool tracked = rig.Hands.Pose(HandSide.Right).Tracked;
                    if (tracked && firstTracked < 0) firstTracked = k;
                    if (firstTracked >= 0)
                    {
                        Assert.That(tracked, Is.True, "the recorded hand was lost in frame " + k);
                        lowestConfidence = math.min(lowestConfidence, rig.Hands.Pose(HandSide.Right).Confidence);
                        if (rig.Input.LastHands.Right.Valid) trusted++;
                        else untrusted++;
                    }
                    Assert.That(rig.Hands.Pose(HandSide.Left).Tracked, Is.False);
                }
                Assert.That(firstTracked, Is.InRange(0, 4), "frames from the session's start to the hand");
                AssertTheHubHasEveryHandsEvent(rig);
                Assert.That(RehearsalRig.CountOf(rig.HubEvents, "hands.on"), Is.EqualTo(1));
                Assert.That(RehearsalRig.CountOf(rig.HubEvents, "hands.enter"), Is.EqualTo(1));
                Assert.That(RehearsalRig.LastOf(rig.HubEvents, "hands.enter").Data.GetString("hand"), Is.EqualTo("R"));
                Assert.That(rig.HasEvent("hands.exit"), Is.False);
                // Each of the three recorded presses fired the target, once, by the fingertip.
                Assert.That(RehearsalRig.CountOf(rig.HubEvents, "hands.touch"), Is.EqualTo(3), "presses of the recording that fired");
                Assert.That(RehearsalRig.LastOf(rig.HubEvents, "hands.touch").Data.GetString("label"), Is.EqualTo("e2e.press"));
                Assert.That(rig.Input.Fired.Count, Is.EqualTo(3));
                foreach (InputFiring firing in rig.Input.Fired) Assert.That(firing.Source, Is.EqualTo(InputSource.Gaze));
                // The recording's results are 80 ms old, which is normal latency and no cause for the self-governor
                // (section 4.4): the detector stays at 30 Hz, and nothing about its rate is logged.
                Assert.That(GateEvents(rig.HubEvents), Is.EqualTo(new[] { "on 30" }));
                Assert.That(rig.HasEvent("hands.rate"), Is.False);
                TestContext.WriteLine("{0}: tracked from frame {1}; 3 presses fired; lowest confidence {2:0.00}; " +
                                      "the fingertip was trusted in {3} frames and not in {4}; hand size {5:0.000}",
                    PressLog, firstTracked, lowestConfidence, trusted, untrusted, rig.Hands.Scale);

                // ---- round again: the same, and the phone's frame allocates nothing ----
                long total = 0, worst = 0;
                int steady = 0, worstFrame = -1;
                for (int k = 0; k < lapFrames; k++)
                {
                    int logged = rig.Logged.Count;
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    rig.PhoneFrame();
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    rig.StationPoll();
                    Assert.That(rig.Hands.Pose(HandSide.Right).Tracked, Is.True, "the hand was lost where the log starts again, frame " + k);
                    // A frame in which something was logged is not steady state: an event is a string.
                    if (rig.Logged.Count != logged) continue;
                    steady++;
                    total += allocated;
                    if (allocated > worst)
                    {
                        worst = allocated;
                        worstFrame = k;
                    }
                }
                TestContext.WriteLine("{0} steady frames with a tracked hand, {1} B in all, worst {2} B (frame {3})", steady, total, worst, worstFrame);
                Assert.That(replay.LoopCount, Is.GreaterThanOrEqualTo(1), "the log did not start again");
                Assert.That(steady, Is.GreaterThanOrEqualTo(lapFrames - 6));
                Assert.That(total, Is.EqualTo(0L), "managed bytes allocated by the phone's frame with a tracked hand");
                Assert.That(RehearsalRig.CountOf(rig.Logged, "hands.touch"), Is.EqualTo(6));
                AssertHealthy(rig);
            }
        }
    }
}
