using System;
using System.Collections.Generic;
using Atlas.App;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Safety;
using Atlas.Scan;
using Atlas.Spaces.Glued;
using Atlas.Spaces.Portal;
using Atlas.Spaces.Slicer;
using Atlas.Telemetry;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.E2E.Tests
{
    /// <summary>
    /// The end-to-end rehearsal with no Unity and no phone: a fixture room, a recorded walk, the pose filter,
    /// the flat kernel, Bump Guard with a ray-cast depth image, the cores of the four built spaces, the State
    /// of every frame through the link to a Hub and on to a Spectator (Master Plan sections 6.3, 7.6, 11.1).
    /// </summary>
    public class E2ERehearsalTests
    {
        const string LapWalk = "walk_lap_sample_3x3";
        const string WallWalk = "walk_wall_approach";
        const string JumpWalk = "walk_jump_and_loss";
        /// <summary>Sample of the recorded jump, and the first of its sixty samples with limited tracking (recordings/README.md).</summary>
        const int JumpSample = 90, LossFirstSample = 150, LossSamples = 60;
        /// <summary>250 ms at 60 frames a second: the time an operator command or a slider may take (section 7.6, L9 and L10).</summary>
        const int CommandFrames = 15;

        static RoomModel Room(string name) { return RoomFixtures.LoadFromProject(name); }

        /// <summary>A rig with the Station up, the walker standing at a pose, and the room entered at Home.</summary>
        static RehearsalRig Enter(RoomModel room, in HeadPose start, bool station = true)
        {
            var rig = new RehearsalRig();
            rig.StandAt(in start);
            if (station)
            {
                rig.StartStation();
                Assert.That(rig.StepUntil(() => rig.LinkUp, 300), Is.GreaterThanOrEqualTo(0), "the link never came up");
            }
            else
            {
                rig.Steps(30);
            }
            Assert.That(rig.ActivateRoom(room), Is.GreaterThanOrEqualTo(0), "Home was never entered");
            Assert.That(rig.Lab.Current, Is.EqualTo(SpaceId.Home));
            return rig;
        }

        /// <summary>Asks for a space the Station's way and waits until it runs.</summary>
        static int Switch(RehearsalRig rig, SpaceId space)
        {
            Assert.That(rig.Hub.SendCommand(OperatorCommand.SwitchSpace((int)space)), Is.True, "the Hub has no phone");
            int frames = rig.StepUntil(() => rig.Lab.State == SpaceHostState.Running && rig.Lab.HasSpace && rig.Lab.Current == space, 120);
            Assert.That(frames, Is.GreaterThanOrEqualTo(0), SpaceIds.Code(space) + " was never entered");
            return frames;
        }

        /// <summary>The State the Hub decoded is the State the frame loop filled at step 8a.</summary>
        static void AssertDecoded(RehearsalRig rig, in StateMessage received, string who)
        {
            StateMessage sent = rig.Loop.LastState;
            string at = who + ", frame " + rig.Frames;
            Assert.That(received.HeadPosX, Is.EqualTo(sent.HeadPosX), at);
            Assert.That(received.HeadPosY, Is.EqualTo(sent.HeadPosY), at);
            Assert.That(received.HeadPosZ, Is.EqualTo(sent.HeadPosZ), at);
            Assert.That(received.HeadRotX, Is.EqualTo(sent.HeadRotX), at);
            Assert.That(received.HeadRotY, Is.EqualTo(sent.HeadRotY), at);
            Assert.That(received.HeadRotZ, Is.EqualTo(sent.HeadRotZ), at);
            Assert.That(received.HeadRotW, Is.EqualTo(sent.HeadRotW), at);
            Assert.That(received.SpaceId, Is.EqualTo(sent.SpaceId), at);
            Assert.That(received.SceneId, Is.EqualTo(sent.SceneId), at);
            Assert.That(received.WorldVer, Is.EqualTo(sent.WorldVer), at);
            Assert.That(received.BumpLevel, Is.EqualTo(sent.BumpLevel), at);
            Assert.That(received.Tracking, Is.EqualTo(sent.Tracking), at);
            Assert.That(received.PoseJumpRejected, Is.EqualTo(sent.PoseJumpRejected), at);
            Assert.That(received.CueActive, Is.EqualTo(sent.CueActive), at);
            Assert.That(received.QualityTier, Is.EqualTo(sent.QualityTier), at);
            Assert.That(received.DemoLock, Is.EqualTo(rig.Link.DemoLock), at);
            Assert.That(received.Sheet, Is.EqualTo(sent.Sheet), at);
            for (int i = 0; i < StateMessage.TileLength; i++) Assert.That(received.GetTile(i), Is.EqualTo(sent.GetTile(i)), at);
            for (int i = 0; i < StateMessage.XformLength; i++) Assert.That(received.GetXform(i), Is.EqualTo(sent.GetXform(i)), at);
            for (int i = 0; i < StateMessage.ParamLength; i++) Assert.That(received.GetParam(i), Is.EqualTo(sent.GetParam(i)), at);

            // And it is the pose of this frame, not of an older one.
            FrameContext frame = rig.Loop.Current;
            Assert.That(new float3(received.HeadPosX, received.HeadPosY, received.HeadPosZ), Is.EqualTo(frame.RawHeadPosition), at);
        }

        /// <summary>One frame, then: exactly one State reached the Hub and the Spectator, and both decode to the phone's.</summary>
        static void StepAndCheckState(RehearsalRig rig)
        {
            int hub = rig.HubStates, spectator = rig.SpectatorStates;
            rig.Step();
            Assert.That(rig.HubStates, Is.EqualTo(hub + 1), "States at the Hub in frame " + rig.Frames);
            Assert.That(rig.SpectatorStates, Is.EqualTo(spectator + 1), "States at the Spectator in frame " + rig.Frames);
            AssertDecoded(rig, in rig.HubState, "Hub");
            AssertDecoded(rig, in rig.SpectatorState, "Spectator");
            Assert.That(rig.SpectatorState.Seq, Is.EqualTo(rig.HubState.Seq), "the relay keeps the phone's sequence number");
        }

        static void AssertOnTheFloor(RehearsalRig rig, RoomModel room)
        {
            float2 head = rig.HeadFloor;
            string at = "frame " + rig.Frames + " at " + head;
            Assert.That(room.FreeRect.Contains(head), Is.True, "the head left the free rectangle, " + at);
            Assert.That(room.Sdf.IsWalkable(head), Is.True, "the head left the walkable floor, " + at);
            float2 walker = (float2)rig.WalkerFloor;
            Assert.That(room.FreeRect.Contains(walker), Is.True, "the walker left the free rectangle, " + at);
            Assert.That(room.Sdf.IsWalkable(walker), Is.True, "the walker left the walkable floor, " + at);
        }

        static void AssertHealthy(RehearsalRig rig)
        {
            Assert.That(rig.Warnings, Is.Empty, "frame loop warnings");
            Assert.That(rig.Loop.FaultedSenseCount, Is.EqualTo(0));
            Assert.That(rig.HasEvent("space.error"), Is.False, "a space threw");
            Assert.That(rig.HasEvent("space.loadFailed"), Is.False);
        }

        // ---- the whole flow on every fixture ----------------------------------------------------------------

        static readonly string[] Fixtures = RoomFixtures.All;

        [TestCaseSource(nameof(Fixtures))]
        public void E2E_Flow_EveryFixture_HomeThenEverySpace_WithTheStationWatching(string fixture)
        {
            RoomModel room = Room(fixture);
            PoseLog lap = RehearsalRig.Lap(room);

            var rig = new RehearsalRig();
            using (rig)
            {
                rig.StandAt(in lap.Samples[0]);
                rig.StartStation();
                Assert.That(rig.StepUntil(() => rig.LinkUp, 300), Is.GreaterThanOrEqualTo(0), "the link never came up");

                // Before the room: no space, and the Station knows it.
                StepAndCheckState(rig);
                Assert.That(rig.HubState.SpaceId, Is.EqualTo(SpaceIds.NoSpace));
                Assert.That(rig.HubState.SceneId, Is.EqualTo(SpaceIds.NoScene));
                Assert.That(rig.Guard.Core.HasRoom, Is.False);

                // The hand-over: room frame, walker placed, Bump Guard's map, the outline at the Station, Home.
                int frames = rig.ActivateRoom(room);
                Assert.That(frames, Is.GreaterThanOrEqualTo(0), "Home was never entered");
                TestContext.WriteLine("{0}: Home runs {1} frames after the hand-over", fixture, frames);
                Assert.That(rig.Lab.Current, Is.EqualTo(SpaceId.Home));
                Assert.That(rig.Lab.Module.Registered, Is.True);
                Assert.That(rig.Pipeline.Sensors.RoomActive, Is.True);
                Assert.That(rig.Guard.Core.HasRoom, Is.True, "Bump Guard has no map of the room");
                Assert.That(rig.Guard.Armed, Is.True);
                Assert.That(((FlatTileGraph)rig.Kernel.Graph).CentreSpacing * rig.Kernel.MetresPerUnit,
                    Is.EqualTo(room.LapSide).Within(1e-9), "the kernel's tile is the lap square");
                Assert.That(math.distance((float2)rig.WalkerFloor, rig.HeadFloor), Is.LessThan(1e-5f), "the walker was not placed at the head");
                Assert.That(rig.HubOutline, Is.Not.Null, "the Station never got the room outline");
                Assert.That(rig.HubOutline.RectW, Is.EqualTo(room.FreeRect.Wf));
                Assert.That(rig.HubOutline.RectD, Is.EqualTo(room.FreeRect.Df));
                Assert.That(rig.Link.RoomId, Is.EqualTo(room.Id));
                Assert.That(rig.HasEvent("room.active"), Is.True);
                Assert.That(rig.HasEvent("space.entered"), Is.True);

                // One lap of the room's lap square at Home.
                rig.Play(lap);
                for (int k = 0; k < lap.Samples.Length; k++)
                {
                    StepAndCheckState(rig);
                    AssertOnTheFloor(rig, room);
                    Assert.That(math.distance((float2)rig.WalkerFloor, rig.HeadFloor), Is.LessThan(1e-4f), "frame " + rig.Frames);
                    Assert.That(rig.HubState.SpaceId, Is.EqualTo((byte)SpaceId.Home));
                    Assert.That(rig.HubState.PoseJumpRejected, Is.False, "frame " + rig.Frames);
                    Assert.That(rig.Guard.Level, Is.Not.EqualTo(BumpLevel.TrackingLost), "frame " + rig.Frames);
                }
                rig.StandAt(in lap.Samples[lap.Samples.Length - 1]);

                // Every built space, asked for the Station's way, through black.
                SpaceId[] spaces = { SpaceId.Glued, SpaceId.Portal, SpaceId.Slicer };
                for (int s = 0; s < spaces.Length; s++)
                {
                    ushort version = rig.Lab.WorldVersion;
                    rig.Stage.WentBlack = false;
                    int took = Switch(rig, spaces[s]);
                    TestContext.WriteLine("{0}: {1} runs {2} frames after the command", fixture, SpaceIds.Code(spaces[s]), took);
                    Assert.That(rig.Stage.WentBlack, Is.True, "the switch did not pass through black");
                    Assert.That(rig.Stage.Fade, Is.EqualTo(1f));
                    Assert.That(rig.Lab.WorldVersion, Is.Not.EqualTo(version));
                    Assert.That(rig.Registry.IsGroupActive(SpaceIds.ParamGroup(spaces[s])), Is.True);
                    for (int k = 0; k < 30; k++) StepAndCheckState(rig);
                    Assert.That(rig.HubState.SpaceId, Is.EqualTo((byte)spaces[s]));
                    Assert.That(rig.HubState.SceneId, Is.EqualTo(SpaceIds.SceneId(spaces[s])));
                    Assert.That(rig.Link.Scene, Is.EqualTo(SpaceIds.Code(spaces[s])));
                    Assert.That(rig.Registry.GetInt(AppParams.Space), Is.EqualTo((int)spaces[s]), "space.id was not written back");
                    Assert.That(math.distance((float2)rig.WalkerFloor, rig.HeadFloor), Is.LessThan(1e-4f), "a registered space starts the walker at the head");
                }

                // A space of lane B is not built: refused, and the walker stays where he is.
                Assert.That(rig.Hub.SendCommand(OperatorCommand.SwitchSpace((int)SpaceId.CurvedWalk)), Is.True);
                rig.Steps(CommandFrames);
                Assert.That(rig.Lab.Current, Is.EqualTo(SpaceId.Slicer));
                Assert.That(rig.Lab.State, Is.EqualTo(SpaceHostState.Running));
                Assert.That(rig.HasEvent("space.refused"), Is.True);

                // Home again.
                Assert.That(rig.Hub.SendCommand(OperatorCommand.Home()), Is.True);
                Assert.That(rig.StepUntil(() => rig.Lab.State == SpaceHostState.Running && rig.Lab.Current == SpaceId.Home, 120),
                    Is.GreaterThanOrEqualTo(0));
                StepAndCheckState(rig);
                Assert.That(rig.HubState.SpaceId, Is.EqualTo((byte)SpaceId.Home));
                Assert.That(rig.Loader.Loads, Is.EqualTo(5));
                Assert.That(rig.Loader.Unloads, Is.EqualTo(4));
                AssertHealthy(rig);
            }
        }

        // ---- the recorded lap ---------------------------------------------------------------------------------

        [Test]
        public void E2E_Lap_StaysOnTheFloor_BumpGuardSilentOnTheStraightSides_StateDecodesEveryFrame()
        {
            RoomModel room = Room(RoomFixtures.Sample3x3);
            PoseLog log = RehearsalRig.LoadWalk(LapWalk);
            Assert.That(log.RoomId, Is.EqualTo(room.Id));
            Assert.That(log.Frame, Is.EqualTo(PoseFrameKind.Room));

            using (RehearsalRig rig = Enter(room, in log.Samples[0]))
            {
                rig.BumpChanges.Clear();
                float h = 0.5f * room.LapSide;
                float2[] corners = { new float2(-h, -h), new float2(-h, h), new float2(h, h), new float2(h, -h) };
                double path = 0.0;
                float2 previous = rig.HeadFloor;
                int depthImages = 0;

                PoseLogReplaySource replay = rig.Play(log);
                for (int k = 0; k < log.Samples.Length; k++)
                {
                    StepAndCheckState(rig);
                    Assert.That(replay.SampleIndex, Is.EqualTo(k));
                    AssertOnTheFloor(rig, room);
                    // Home is registered: the kernel's walker is under the head all the way round.
                    Assert.That(math.distance((float2)rig.WalkerFloor, rig.HeadFloor), Is.LessThan(1e-4f), "frame " + rig.Frames);
                    Assert.That(rig.Frame.PoseJumpRejected, Is.False, "frame " + rig.Frames);
                    Assert.That(rig.Frame.Kernel.Rejected, Is.False, "frame " + rig.Frames);
                    Assert.That(rig.Frame.Tracking, Is.EqualTo(TrackingState.Normal));
                    path += math.length(rig.Frame.FloorDelta);
                    Assert.That(math.distance(rig.HeadFloor - previous, rig.Frame.FloorDelta), Is.LessThan(1e-6f));
                    previous = rig.HeadFloor;
                    if (rig.Pipeline.Sensors.HasDepth) depthImages++;
                    Assert.That(rig.HubState.BumpLevel, Is.EqualTo((byte)rig.Guard.Level));
                }

                Assert.That(path, Is.EqualTo(4.0 * room.LapSide).Within(1e-3), "the geometry stream's path length");
                Assert.That(math.distance(rig.HeadFloor, log.Samples[0].Floor), Is.LessThan(1e-4f), "a closed lap");
                Assert.That(rig.Pipeline.JumpCount, Is.EqualTo(0));
                // 15 images a second from the ray-cast depth: Bump Guard's live layer was fed all the way.
                Assert.That(depthImages, Is.InRange(log.Samples.Length / 4 - 2, log.Samples.Length / 4 + 2));
                Assert.That(rig.Guard.Core.DynamicCellCount, Is.EqualTo(0), "a mapped wall is not a dynamic obstacle");
                Assert.That(rig.Guard.RescanAdvised, Is.False);

                int cornerCues = 0;
                foreach (BumpChange c in rig.BumpChanges)
                {
                    Assert.That(c.To, Is.Not.EqualTo(BumpLevel.Strong), "STRONG on the lap at " + c.Floor);
                    Assert.That(c.To, Is.Not.EqualTo(BumpLevel.TrackingLost));
                    if (c.From != BumpLevel.Clear) continue;
                    float nearest = float.MaxValue;
                    foreach (float2 corner in corners) nearest = math.min(nearest, math.distance(c.Floor, corner));
                    Assert.That(nearest, Is.LessThanOrEqualTo(0.5f), "WARN on a straight side at " + c.Floor);
                    cornerCues++;
                }
                TestContext.WriteLine("lap: {0} frames, {1} depth images, {2} corner cues", log.Samples.Length, depthImages, cornerCues);
                AssertHealthy(rig);
            }
        }

        // ---- the wall approach ----------------------------------------------------------------------------------

        [Test]
        public void E2E_WallApproach_BumpGuardWarnsAtLeast600msBeforeContact_AndTheStationSeesIt()
        {
            RoomModel room = Room(RoomFixtures.Sample3x3);
            PoseLog log = RehearsalRig.LoadWalk(WallWalk);

            using (RehearsalRig rig = Enter(room, in log.Samples[0]))
            {
                rig.BumpChanges.Clear();
                Assert.That(rig.Guard.Level, Is.EqualTo(BumpLevel.Clear));
                int fires = BumpCue.FireCount;

                // Contact is where the path would reach clear() = 0 had the walker not stopped: one 5 cm cell
                // short of the wall (section 4.10). The speed is read from the recording itself.
                float wallZ = room.Outline[2].y;
                float2 start = log.Samples[0].Floor;
                double speed = math.distance(log.Samples[60].Floor, start);
                double contactSeconds = (wallZ - BumpConstants.StaticInset - start.y) / speed;

                int startFrame = rig.Frames;
                int cueFrame = -1, hubCueFrame = -1;
                rig.Play(log);
                for (int k = 0; k < log.Samples.Length; k++)
                {
                    StepAndCheckState(rig);
                    if (cueFrame < 0 && rig.Guard.Level != BumpLevel.Clear) cueFrame = k;
                    if (hubCueFrame < 0 && rig.HubState.BumpLevel != 0)
                    {
                        hubCueFrame = k;
                        Assert.That(rig.HubState.CueActive, Is.True, "the level rose but no cue is shown");
                    }
                }

                Assert.That(cueFrame, Is.GreaterThanOrEqualTo(0), "Bump Guard never warned");
                double lead = contactSeconds - cueFrame / 60.0;
                TestContext.WriteLine("wall approach at {0:F2} m/s: cue at {1:F3} s, contact at {2:F3} s, lead {3:F3} s, {4} frames after the walk began",
                    speed, cueFrame / 60.0, contactSeconds, lead, rig.Frames - startFrame);
                Assert.That(lead, Is.GreaterThanOrEqualTo(0.6), "lead time to predicted contact");
                Assert.That(hubCueFrame, Is.EqualTo(cueFrame), "the Station sees the level in the frame it rose");
                Assert.That(BumpCue.FireCount, Is.GreaterThan(fires), "armed, the cue was asked for");
                Assert.That(BumpCue.Shown, Is.Not.EqualTo(BumpLevel.Clear));
                Assert.That(rig.BumpChanges[0].From, Is.EqualTo(BumpLevel.Clear));
                // Standing 0.3 m from the wall is inside the 0.30 m warning distance less the cell: still warned.
                Assert.That(rig.Guard.Level, Is.Not.EqualTo(BumpLevel.Clear));
                Assert.That(rig.Guard.Level, Is.Not.EqualTo(BumpLevel.TrackingLost));
                Assert.That(rig.HasEvent("bump.level"), Is.True, "the rise is in the event log");
                AssertHealthy(rig);
            }
        }

        [Test]
        public void E2E_WallApproach_StationCablePulled_BumpGuardCueIsIdentical()
        {
            RoomModel room = Room(RoomFixtures.Sample3x3);
            PoseLog log = RehearsalRig.LoadWalk(WallWalk);
            var runs = new List<BumpChange>[3];

            // 0: the Station up all the way. 1: its cable pulled half a second into the walk. 2: never there.
            for (int run = 0; run < 3; run++)
            {
                using (RehearsalRig rig = Enter(room, in log.Samples[0], run != 2))
                {
                    rig.BumpChanges.Clear();
                    int start = rig.Frames;
                    rig.Play(log);
                    for (int k = 0; k < log.Samples.Length; k++)
                    {
                        if (run == 1 && k == 30) rig.Network.Cut = true;
                        rig.Step();
                    }
                    var changes = new List<BumpChange>(rig.BumpChanges);
                    for (int i = 0; i < changes.Count; i++)
                    {
                        BumpChange c = changes[i];
                        c.Frame -= start;
                        c.Time = 0.0;
                        changes[i] = c;
                    }
                    runs[run] = changes;
                    Assert.That(rig.Frames - start, Is.EqualTo(log.Samples.Length), "a frame was lost");
                    if (run == 1) Assert.That(rig.HubStates, Is.LessThan(rig.Frames), "the cable was not pulled");
                    AssertHealthy(rig);
                }
            }

            Assert.That(runs[0].Count, Is.GreaterThan(0));
            for (int run = 1; run < 3; run++)
            {
                Assert.That(runs[run].Count, Is.EqualTo(runs[0].Count), "run " + run);
                for (int i = 0; i < runs[0].Count; i++)
                {
                    Assert.That(runs[run][i].Frame, Is.EqualTo(runs[0][i].Frame), "run " + run + ", change " + i);
                    Assert.That(runs[run][i].To, Is.EqualTo(runs[0][i].To), "run " + run + ", change " + i);
                }
            }
        }

        // ---- the jump and the loss ------------------------------------------------------------------------------

        [Test]
        public void E2E_JumpAndLoss_TheJumpIsAbsentFromGeometry_PresentInBumpGuardsInput_AndFlaggedAtTheStation()
        {
            RoomModel room = Room(RoomFixtures.Sample3x3);
            PoseLog log = RehearsalRig.LoadWalk(JumpWalk);
            float jump = math.distance(log.Samples[JumpSample].Floor, log.Samples[JumpSample - 1].Floor);
            Assert.That(jump, Is.GreaterThan(0.49f), "the recording holds no jump at sample " + JumpSample);

            using (RehearsalRig rig = Enter(room, in log.Samples[0]))
            {
                float jumpEvents = 0f;
                int lostEvents = 0, regainedEvents = 0;
                rig.Pipeline.JumpRejectedRaised += metres => jumpEvents += metres;
                rig.Pipeline.TrackingLostRaised += () => lostEvents++;
                rig.Pipeline.TrackingRegained += () => regainedEvents++;

                double2 integrated = rig.WalkerFloor;
                float2 offset = float2.zero;        // head minus walker: what the geometry never took
                bool sawTrackingLost = false;
                rig.Play(log);
                for (int k = 0; k < log.Samples.Length; k++)
                {
                    double2 before = rig.WalkerFloor;
                    float2 previousHead = rig.HeadFloor;
                    StepAndCheckState(rig);
                    FrameContext frame = rig.Frame;
                    bool limited = k >= LossFirstSample && k < LossFirstSample + LossSamples;
                    string at = "sample " + k;

                    // Raw (safety) stream: Bump Guard was handed exactly the recorded pose, jump included.
                    Assert.That(rig.Pipeline.Sensors.Head.Position, Is.EqualTo(log.Samples[k].Position), at);
                    Assert.That(frame.RawHeadPosition, Is.EqualTo(log.Samples[k].Position), at);

                    if (k == JumpSample)
                    {
                        Assert.That(math.distance(rig.HeadFloor, previousHead), Is.EqualTo(jump).Within(1e-5f));
                        Assert.That(rig.Guard.Core.PoseJumped, Is.True, "Bump Guard did not see the jump");
                        // Geometry stream: no displacement, the kernel does not move, the flag goes out.
                        Assert.That(frame.PoseJumpRejected, Is.True);
                        Assert.That(frame.FloorDelta, Is.EqualTo(float2.zero));
                        Assert.That(frame.Kernel.Rejected, Is.True);
                        Assert.That(rig.WalkerFloor, Is.EqualTo(before), "the kernel integrated the jump");
                        Assert.That(rig.HubState.PoseJumpRejected, Is.True, "State.flags bit 2 at the Hub");
                        Assert.That(rig.SpectatorState.PoseJumpRejected, Is.True);
                        offset += rig.HeadFloor - previousHead;
                    }
                    else
                    {
                        Assert.That(frame.PoseJumpRejected, Is.False, at);
                        Assert.That(rig.HubState.PoseJumpRejected, Is.False, at);
                        // Bump Guard takes no velocity from a pose it cannot trust, so the ground covered during
                        // the blind second reaches it as one more jump when tracking returns: the position is
                        // taken at once and no speed is made of it (section 4.10).
                        Assert.That(rig.Guard.Core.PoseJumped, Is.EqualTo(k == LossFirstSample + LossSamples), at);
                    }

                    if (limited)
                    {
                        Assert.That(frame.FloorDelta, Is.EqualTo(float2.zero), "no geometry step while tracking is limited, " + at);
                        Assert.That(rig.WalkerFloor, Is.EqualTo(before), at);
                        Assert.That(rig.HubState.Tracking, Is.EqualTo((int)TrackingStates.ToWire(TrackingState.Limited)), at);
                        offset += rig.HeadFloor - previousHead;
                        if (rig.Guard.Level == BumpLevel.TrackingLost) sawTrackingLost = true;
                    }
                    else
                    {
                        Assert.That(rig.HubState.Tracking, Is.EqualTo((int)TrackingStates.ToWire(TrackingState.Normal)), at);
                    }

                    if (k != JumpSample && !limited && k != LossFirstSample + LossSamples)
                        integrated += (double2)(rig.HeadFloor - previousHead);
                    else if (k == LossFirstSample + LossSamples)
                        offset += rig.HeadFloor - previousHead;      // the kernel drops the first step after a loss as well
                }

                Assert.That(jumpEvents, Is.EqualTo(jump).Within(1e-5f), "one jump event with the jump's length");
                Assert.That(rig.Pipeline.JumpCount, Is.EqualTo(1));
                Assert.That(lostEvents, Is.EqualTo(1), "tracking lost after half a second of limited tracking");
                Assert.That(regainedEvents, Is.EqualTo(1));
                Assert.That(sawTrackingLost, Is.True, "Bump Guard never showed TRACKING LOST");
                Assert.That(rig.HasEvent("pose.jump"), Is.True);
                Assert.That(rig.HasEvent("tracking.lost"), Is.True);

                // The walker holds what was really walked: the jump and the blind second are not in it.
                TestContext.WriteLine("walker {0}, head {1}, never integrated {2}", rig.WalkerFloor, rig.HeadFloor, offset);
                Assert.That(math.distance(rig.HeadFloor - (float2)rig.WalkerFloor, offset), Is.LessThan(1e-3f));
                Assert.That(math.abs(offset.y), Is.GreaterThan(0.49f), "the 0.5 m jump is missing from the offset");

                // Recentre, the Station's way: the walker is under the head again.
                rig.StandAt(in log.Samples[log.Samples.Length - 1]);
                Assert.That(rig.Hub.SendCommand(OperatorCommand.Recentre()), Is.True);
                int took = rig.StepUntil(() => math.distance((float2)rig.WalkerFloor, rig.HeadFloor) < 1e-4f, CommandFrames);
                Assert.That(took, Is.GreaterThanOrEqualTo(0), "recentre did not bring the walker back within 250 ms");
                AssertHealthy(rig);
            }
        }

        // ---- Glued ------------------------------------------------------------------------------------------------

        [Test]
        public void E2E_Glued_TheCellIndexChangesExactlyWhenACellEdgeIsCrossed()
        {
            RoomModel room = Room(RoomFixtures.Sample3x3);
            PoseLog log = RehearsalRig.LoadWalk(LapWalk);

            using (RehearsalRig rig = Enter(room, in log.Samples[0]))
            {
                Switch(rig, SpaceId.Glued);
                rig.Steps(2);
                GluedModule glued = rig.Module<GluedModule>();
                Assert.That(glued, Is.Not.Null);
                double sizeX = room.Glued.SizeX, sizeZ = room.Glued.SizeZ;
                Assert.That(glued.Lattice.SizeX, Is.EqualTo(sizeX));
                Assert.That(rig.HubState.P5, Is.EqualTo((float)sizeX));
                Assert.That(rig.HubState.P6, Is.EqualTo((float)sizeZ));
                Assert.That(glued.CellsDrawn, Is.EqualTo(GluedRules.CellCount(glued.Lattice.Layout, rig.Registry.GetInt(GluedSpace.ParamCopies))));

                long col = rig.HubState.Tile0, row = rig.HubState.Tile1;
                float2 previous = rig.HeadFloor;
                int changes = 0, crossings = 0;
                ushort version = rig.Lab.WorldVersion;
                rig.Play(log);
                for (int k = 0; k < log.Samples.Length; k++)
                {
                    StepAndCheckState(rig);
                    float2 head = rig.HeadFloor;
                    // The edges of the lattice are the lines x = -wf/2 + i * sizeX and z = -df/2 + j * sizeZ:
                    // an edge is crossed when the head's cell coordinate passes a whole number.
                    bool crossedX = Math.Floor((previous.x + 0.5 * room.FreeRect.Wf) / sizeX) != Math.Floor((head.x + 0.5 * room.FreeRect.Wf) / sizeX);
                    bool crossedZ = Math.Floor((previous.y + 0.5 * room.FreeRect.Df) / sizeZ) != Math.Floor((head.y + 0.5 * room.FreeRect.Df) / sizeZ);
                    bool crossed = crossedX || crossedZ;
                    bool changed = rig.HubState.Tile0 != col || rig.HubState.Tile1 != row;
                    Assert.That(changed, Is.EqualTo(crossed), "sample " + k + " at " + head + ": cell (" + rig.HubState.Tile0 + ", " + rig.HubState.Tile1 + ")");
                    if (crossed) crossings++;
                    if (changed)
                    {
                        changes++;
                        Assert.That(math.abs(rig.HubState.Tile0 - col) + math.abs(rig.HubState.Tile1 - row), Is.EqualTo(1L), "one edge, one neighbour");
                    }
                    col = rig.HubState.Tile0;
                    row = rig.HubState.Tile1;
                    Assert.That(glued.Lattice.InRoom(glued.Walker), Is.True, "the walker's cell lies outside the room at " + head);
                    previous = head;
                }

                TestContext.WriteLine("glued lap: {0} cell changes", changes);
                Assert.That(crossings, Is.EqualTo(4), "the lap crosses the two mid lines twice each");
                Assert.That(changes, Is.EqualTo(4));
                Assert.That(glued.Crossings, Is.EqualTo(4));
                Assert.That(rig.Lab.WorldVersion, Is.EqualTo(version), "walking does not change the world");

                // The Klein bottle, asked for with the command's variant: a new world version, same cells.
                Assert.That(rig.Hub.SendParam(GluedSpace.ParamMode, 1f), Is.True);
                Assert.That(rig.StepUntil(() => rig.HubState.P3 == 1f, CommandFrames), Is.GreaterThanOrEqualTo(0), "glued.mode did not reach the State in 250 ms");
                Assert.That(rig.Lab.WorldVersion, Is.Not.EqualTo(version));
                Assert.That(glued.Lattice.Layout, Is.EqualTo(GluedLayout.Klein));
                AssertHealthy(rig);
            }
        }

        // ---- Impossible Rooms -----------------------------------------------------------------------------------

        [Test]
        public void E2E_Portal_TheRoomChangesWhenTheHeadGoesThroughADoorway_AndTheRoomLeftIsHeld()
        {
            RoomModel room = Room(RoomFixtures.Sample3x3);
            using (RehearsalRig rig = Enter(room, new HeadPose(0.0, new float3(0f, RehearsalRig.EyeHeight, 0f), quaternion.identity, TrackingState.Normal)))
            {
                Switch(rig, SpaceId.Portal);
                PortalModule portal = rig.Module<PortalModule>();
                Assert.That(portal, Is.Not.Null);
                PortalLayout layout = portal.Core.Layout;
                Assert.That(layout.IsFallback, Is.False, "a 3 x 3 m room has space for the generated rooms");
                Assert.That(layout.AllReachable, Is.True);
                Assert.That(layout.EveryPortalHasTwin(), Is.True);
                Assert.That(layout.CellsInside(new RoomModelFloor(room)), Is.True, "a generated room leaves the walkable floor");
                rig.Steps(2);
                Assert.That(rig.HubState.Sheet, Is.EqualTo((short)layout.StartRoom));
                Assert.That(rig.HubState.P4, Is.EqualTo((float)layout.RoomCount));

                // The first doorway out of the start room, walked through along its normal and back.
                int index = -1;
                for (int k = 0; k < layout.Doors.Count && index < 0; k++)
                    if (layout.Doors[k].From == layout.StartRoom || layout.Doors[k].To == layout.StartRoom) index = k;
                Assert.That(index, Is.GreaterThanOrEqualTo(0), "no doorway leaves the start room");
                LayoutDoor door = layout.Doors[index];
                bool forward = door.From == layout.StartRoom;
                int target = forward ? door.To : door.From;
                float4 plane = PortalMath.DoorPlane(door.A, door.B);
                if (!forward) plane = PortalMath.Flip(plane);
                float2 middle = 0.5f * (door.A + door.B), normal = plane.xz;
                float2 near = middle + 0.5f * normal, far = middle - 0.5f * normal;

                rig.StandAt(near);
                Assert.That(rig.Hub.SendCommand(OperatorCommand.Recentre()), Is.True);
                rig.Steps(CommandFrames);
                Assert.That(portal.CurrentRoom, Is.EqualTo(layout.StartRoom));

                for (int leg = 0; leg < 2; leg++)
                {
                    int from = leg == 0 ? layout.StartRoom : target, to = leg == 0 ? target : layout.StartRoom;
                    float4 viewer = leg == 0 ? plane : PortalMath.Flip(plane);
                    PoseLog walk = RehearsalRig.Line(room.Id, leg == 0 ? near : far, leg == 0 ? far : near, 0.5f);
                    float3 previous = rig.Frame.RawHeadPosition;
                    int crossedAt = -1;
                    rig.Play(walk);
                    for (int k = 0; k < walk.Samples.Length; k++)
                    {
                        StepAndCheckState(rig);
                        float3 head = rig.Frame.RawHeadPosition;
                        bool through = PortalMath.Side(viewer, previous) > 0f && PortalMath.Side(viewer, head) <= 0f;
                        if (through)
                        {
                            Assert.That(crossedAt, Is.LessThan(0), "the plane was passed twice");
                            crossedAt = rig.Frame.FrameIndex;
                        }
                        // The room at the Station changes in the frame the head passes the plane, not before, not after.
                        Assert.That(rig.HubState.Sheet, Is.EqualTo((short)(crossedAt < 0 ? from : to)), "leg " + leg + ", sample " + k);
                        Assert.That(rig.HubState.Tile0, Is.EqualTo((long)rig.HubState.Sheet));
                        Assert.That(portal.IsOn(portal.CurrentRoom), Is.True, "the walker's room is never switched off");
                        previous = head;
                    }
                    Assert.That(crossedAt, Is.GreaterThanOrEqualTo(0), "leg " + leg + " never reached the doorway");
                    Assert.That(portal.LastDoorwayFrame, Is.EqualTo(crossedAt));
                    Assert.That(portal.CurrentRoom, Is.EqualTo(to));
                    Assert.That(portal.Core.CurrentRoom, Is.EqualTo(to));
                    Assert.That(portal.SwappedInOverlap, Is.False, "a room was switched off under the walker");
                    if (portal.LastSwapFrame >= crossedAt && portal.LastSwappedRoom == from)
                    {
                        TestContext.WriteLine("leg {0}: room {1} switched off {2} frames after the doorway", leg, from, portal.LastSwapFrame - crossedAt);
                        Assert.That(portal.LastSwapFrame - crossedAt, Is.GreaterThanOrEqualTo(PortalSwapRule.HoldFrames - 1),
                            "the room that was left went dark before the hold was over");
                    }
                    rig.StandAt(leg == 0 ? far : near);
                }
                Assert.That(portal.Doorways, Is.EqualTo(2));
                Assert.That(rig.HasEvent("space.error"), Is.False);
            }
        }

        // ---- Slicer -----------------------------------------------------------------------------------------------

        [Test]
        public void E2E_Slicer_WalkingTheLaneMovesTheCut_AndTheStationsSliderTakesItOver()
        {
            RoomModel room = Room(RoomFixtures.Sample3x3);
            PoseLog log = RehearsalRig.LoadWalk(WallWalk);

            using (RehearsalRig rig = Enter(room, in log.Samples[0]))
            {
                Switch(rig, SpaceId.Slicer);
                SlicerModule slicer = rig.Module<SlicerModule>();
                Assert.That(slicer, Is.Not.Null);
                Assert.That(slicer.Layout.HasLane, Is.True, "a 2.4 m free rectangle has a lane");
                rig.Steps(2);
                float startCut = rig.HubState.P3;

                float last = startCut;
                bool laneOwned = false;
                int laneFrom = -1;
                rig.Play(log);
                for (int k = 0; k < log.Samples.Length; k++)
                {
                    StepAndCheckState(rig);
                    float z = rig.HeadFloor.y;
                    Assert.That(rig.HubState.P3, Is.EqualTo(slicer.Controller.W));
                    if (slicer.Controller.Owner == SlicerCutOwner.Lane)
                    {
                        if (laneFrom < 0) laneFrom = k;
                        Assert.That(rig.HubState.P3, Is.EqualTo(slicer.Layout.CutAt(z)).Within(1e-6f), "sample " + k);
                        // Once the lane owns the cut, walking forward never moves it back.
                        if (laneOwned) Assert.That(rig.HubState.P3, Is.GreaterThanOrEqualTo(last - 1e-6f), "sample " + k);
                        laneOwned = true;
                    }
                    else
                    {
                        Assert.That(laneOwned, Is.False, "the lane gave the cut up with nobody asking, sample " + k);
                        Assert.That(rig.HubState.P3, Is.EqualTo(startCut), "sample " + k);
                    }
                    last = rig.HubState.P3;
                    Assert.That(rig.Registry.GetFloat(SlicerParams.W), Is.EqualTo(rig.HubState.P3), "slicer.w follows the cut live");
                }
                Assert.That(slicer.Controller.Owner, Is.EqualTo(SlicerCutOwner.Lane), "the lane never took the cut");
                // The lane takes the cut once the walker has moved 5 cm: within a few frames at 1 m/s.
                Assert.That(laneFrom, Is.InRange(1, 10));
                Assert.That(rig.HubState.P3, Is.EqualTo(slicer.Layout.CutAt(rig.HeadFloor.y)).Within(1e-6f));
                Assert.That(rig.HubState.P3, Is.GreaterThan(startCut));

                // The Console's slider: in the State within 250 ms, and held while the walker stands.
                rig.StandAt(in log.Samples[log.Samples.Length - 1]);
                Assert.That(rig.Hub.SendParam(SlicerParams.W, 0.25f), Is.True);
                int took = rig.StepUntil(() => rig.HubState.P3 == 0.25f, CommandFrames);
                Assert.That(took, Is.GreaterThanOrEqualTo(0), "slicer.w did not reach the State in 250 ms");
                rig.Steps(60);
                Assert.That(rig.HubState.P3, Is.EqualTo(0.25f));
                Assert.That(slicer.Controller.Owner, Is.EqualTo(SlicerCutOwner.Parameter));
                AssertHealthy(rig);
            }
        }

        // ---- operator commands and sliders -------------------------------------------------------------------------

        [Test]
        public void E2E_OperatorCommands_EachOfTheSixTakesEffectWithin250ms_ThroughTheLink()
        {
            RoomModel room = Room(RoomFixtures.Sample3x3);
            var start = new HeadPose(0.0, new float3(0f, RehearsalRig.EyeHeight, 0f), quaternion.identity, TrackingState.Normal);
            using (RehearsalRig rig = Enter(room, in start))
            {
                // 1  Switch: the fade starts in the frame the command arrives; the space runs half a second later.
                Assert.That(rig.Hub.SendCommand(OperatorCommand.SwitchSpace((int)SpaceId.Glued, "klein")), Is.True);
                Assert.That(rig.StepUntil(() => rig.Lab.State != SpaceHostState.Running, CommandFrames), Is.GreaterThanOrEqualTo(0), "the switch did not start in 250 ms");
                Assert.That(rig.StepUntil(() => rig.Lab.State == SpaceHostState.Running && rig.Lab.Current == SpaceId.Glued, 120), Is.GreaterThanOrEqualTo(0));
                Assert.That(rig.Registry.GetInt(GluedSpace.ParamMode), Is.EqualTo(1), "the variant of the command names the gluing");
                rig.Steps(2);
                Assert.That(rig.HubState.P3, Is.EqualTo(1f));

                // 2  Home.
                Assert.That(rig.Hub.SendCommand(OperatorCommand.Home()), Is.True);
                Assert.That(rig.StepUntil(() => rig.Lab.State != SpaceHostState.Running, CommandFrames), Is.GreaterThanOrEqualTo(0), "Home did not start in 250 ms");
                Assert.That(rig.StepUntil(() => rig.Lab.State == SpaceHostState.Running && rig.Lab.Current == SpaceId.Home, 120), Is.GreaterThanOrEqualTo(0));

                // 3  Recentre: the geometry stream starts again from the head.
                int resets = rig.Pipeline.GeometryResets;
                Assert.That(rig.Hub.SendCommand(OperatorCommand.Recentre()), Is.True);
                Assert.That(rig.StepUntil(() => rig.Pipeline.GeometryResets > resets, CommandFrames), Is.GreaterThanOrEqualTo(0), "recentre did not arrive in 250 ms");

                // 4  Quality: the pin is set at once, and the tier is in force within a second.
                Assert.That(rig.Hub.SendCommand(OperatorCommand.Quality(2)), Is.True);
                Assert.That(rig.StepUntil(() => rig.Registry.GetInt(AppParams.PinTier) == 2, CommandFrames), Is.GreaterThanOrEqualTo(0), "the tier pin did not arrive in 250 ms");
                Assert.That(rig.StepUntil(() => rig.HubState.QualityTier == 2, 75), Is.GreaterThanOrEqualTo(0), "tier 2 was not in the State within a second");
                Assert.That(rig.Frame.Tier, Is.EqualTo(Atlas.World.QualityTier.Serious));
                // Automatic again: the pin goes at once; the ladder then climbs one rung per dwell time, never by a jump.
                Assert.That(rig.Hub.SendCommand(OperatorCommand.Quality(OperatorCommand.TierAuto)), Is.True);
                Assert.That(rig.StepUntil(() => rig.Registry.GetInt(AppParams.PinTier) == OperatorCommand.TierAuto, CommandFrames),
                    Is.GreaterThanOrEqualTo(0), "the pin was not lifted in 250 ms");
                int back = rig.StepUntil(() => rig.HubState.QualityTier == 0, 60 * 90);
                Assert.That(back, Is.GreaterThanOrEqualTo(0), "a cool phone never came back to the nominal tier");
                TestContext.WriteLine("ladder: nominal again {0:F1} s after the pin was lifted", back / 60.0);

                // 5  STOP: the cue at STRONG, and the world does not change.
                ushort version = rig.Lab.WorldVersion;
                Assert.That(rig.HubState.CueActive, Is.False);
                Assert.That(rig.Hub.SendCommand(OperatorCommand.Stop()), Is.True);
                Assert.That(rig.StepUntil(() => rig.HubState.CueActive, CommandFrames), Is.GreaterThanOrEqualTo(0), "the cue did not show in 250 ms");
                Assert.That(BumpCue.Shown, Is.EqualTo(BumpLevel.Strong));
                Assert.That(rig.Guard.Level, Is.EqualTo(BumpLevel.Clear), "an operator cue is not Bump Guard's level");
                Assert.That(rig.Lab.Current, Is.EqualTo(SpaceId.Home));
                Assert.That(rig.Lab.WorldVersion, Is.EqualTo(version));
                Assert.That(rig.StepUntil(() => !rig.HubState.CueActive, 60 * 5), Is.GreaterThanOrEqualTo(0), "the operator cue never ended");

                // 6  Lock: in the State, and a slider is ignored while it holds.
                float rho = rig.HubState.P2;
                Assert.That(rig.Hub.SendCommand(OperatorCommand.Lock(true)), Is.True);
                Assert.That(rig.StepUntil(() => rig.HubState.DemoLock, CommandFrames), Is.GreaterThanOrEqualTo(0), "the lock did not show in 250 ms");
                rig.Hub.SendParam(AppParams.CullRho, 2.5f);
                rig.Steps(CommandFrames);
                Assert.That(rig.HubState.P2, Is.EqualTo(rho), "a parameter was changed under demo lock");
                // A command still works under lock.
                Assert.That(rig.Hub.SendCommand(OperatorCommand.Lock(false)), Is.True);
                Assert.That(rig.StepUntil(() => !rig.HubState.DemoLock, CommandFrames), Is.GreaterThanOrEqualTo(0), "the lock could not be lifted");

                // A slider, clamped by the phone: rho 99 becomes the top of its range.
                Assert.That(rig.Hub.SendParam(AppParams.CullRho, 99f), Is.True);
                Assert.That(rig.StepUntil(() => rig.HubState.P2 != rho, CommandFrames), Is.GreaterThanOrEqualTo(0), "the slider did not reach the State in 250 ms");
                Assert.That(rig.HubState.P2, Is.EqualTo(rig.Registry.Find(AppParams.CullRho).Max));
                Assert.That(rig.Kernel.CullRho, Is.EqualTo((double)rig.Registry.Find(AppParams.CullRho).Max));

                Assert.That(rig.Pump.CommandsApplied, Is.EqualTo(8));
                AssertHealthy(rig);
            }
        }

        // ---- no allocation ----------------------------------------------------------------------------------------

        static readonly SpaceId[] BuiltSpaces = { SpaceId.Home, SpaceId.Glued, SpaceId.Portal, SpaceId.Slicer };

        [TestCaseSource(nameof(BuiltSpaces))]
        public void E2E_SteadyState_ThePhonesFrameAllocatesNothing(SpaceId space)
        {
            RoomModel room = Room(RoomFixtures.Sample3x3);
            PoseLog log = RehearsalRig.LoadWalk(LapWalk);

            using (RehearsalRig rig = Enter(room, in log.Samples[0]))
            {
                if (space != SpaceId.Home) Switch(rig, space);
                rig.Play(log);
                // The first side warms everything up; the second is measured between its corners, where no
                // level changes, no cell edge of the lattice is met twice and no event is due.
                int corner = log.Samples.Length / 4;
                for (int k = 0; k < corner + 70; k++) rig.Step();

                long worst = 0, total = 0;
                int frames = 0, worstFrame = -1, events = rig.Logged.Count;
                for (int k = 0; k < 100; k++)
                {
                    int logged = rig.Logged.Count;
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    rig.PhoneFrame();
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    rig.StationPoll();
                    // A frame in which something was logged is not steady state: an event is a string.
                    if (rig.Logged.Count != logged) continue;
                    frames++;
                    total += allocated;
                    if (allocated > worst)
                    {
                        worst = allocated;
                        worstFrame = k;
                    }
                }
                TestContext.WriteLine("{0}: {1} steady frames, {2} B in all, worst {3} B (frame {4}), {5} events in the window",
                    SpaceIds.Code(space), frames, total, worst, worstFrame, rig.Logged.Count - events);
                Assert.That(frames, Is.GreaterThanOrEqualTo(90), "the window was not steady");
                Assert.That(rig.HubStates, Is.GreaterThan(0));
                Assert.That(total, Is.EqualTo(0L), "managed bytes allocated by the phone's frame in steady state");
            }
        }
    }
}
