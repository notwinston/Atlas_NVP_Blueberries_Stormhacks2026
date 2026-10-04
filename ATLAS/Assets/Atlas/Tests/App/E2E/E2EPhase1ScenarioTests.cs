using System;
using Atlas.App;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Safety;
using Atlas.Scan;
using Atlas.Spaces.AlignedCells;
using Atlas.Spaces.CurvedWalk;
using Atlas.Spaces.SmallPlanet;
using Atlas.Telemetry;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.E2E.Tests
{
    /// <summary>
    /// Phase 1 in one session, with no Unity and no phone, through the code of all four lanes (Master Plan
    /// sections 9.5.9 and 11.3, gate G7): a sample room and its loop-fit, the room as tile zero, the app's one
    /// walker kernel in the real frame loop on the 3-4-5 walk, the State through the link to a Spectator that
    /// follows it, Mode 1 to Mode 2 on the same tile, the Small Planet, Home, and Bump Guard's cue at the real
    /// wall in every curved space.
    /// </summary>
    public class E2EPhase1ScenarioTests
    {
        const string WallWalk = "walk_wall_approach";
        const string Lap5Walk = "walk_lap5_3x3";
        /// <summary>The walker's speed between two parts of the session. Metres per second.</summary>
        const float WalkSpeed = 0.7f;

        /// <summary>The phone, the Hub and a Spectator that follows, stepped together and compared in every frame.</summary>
        sealed class Session : IDisposable
        {
            public readonly RoomModel Room;
            public readonly RehearsalRig Rig = new RehearsalRig();
            public readonly RehearsalSpectator Station;
            /// <summary>Frames in which the Station's picture of a space was compared with the phone's, by space.</summary>
            public readonly int[] Followed = new int[SpaceIds.Count];
            // The phone's tile sequence, counted the way the flat-twin trail counts it.
            int _trailSpace = -1;
            long _tilesSeen;
            TileId _lastTile;

            public Session(RoomModel room)
            {
                Room = room;
                Station = new RehearsalSpectator(Rig);
            }

            public void Dispose() { Rig.Dispose(); }

            /// <summary>One frame of the phone, the Hub and the Spectator, then everything that must hold in every frame.</summary>
            public void Step()
            {
                RehearsalRig rig = Rig;
                int hub = rig.HubStates, spectator = rig.SpectatorStates;
                bool linked = rig.LinkUp;
                double curvature = rig.Kernel.CurvatureNow;
                rig.Step();
                Station.Frame(rig);
                string at = "frame " + rig.Frames;

                // Gate G7: the curvature never changes while the head moves faster than 0.1 m/s.
                if (math.length(rig.Frame.FloorDelta) > 0.1f * RehearsalRig.Dt)
                    Assert.That(rig.Kernel.CurvatureNow, Is.EqualTo(curvature), "the curvature changed under a walking head, " + at);
                // Rule 1: Bump Guard is step 2 of every frame, whatever the space and whatever the Station does.
                Assert.That(rig.Loop.HasGuard, Is.True, at);
                if (!linked) return;

                // The State of this frame reached the Hub and the Spectator, and the Spectator's is the phone's.
                Assert.That(rig.HubStates, Is.EqualTo(hub + 1), "States at the Hub, " + at);
                Assert.That(rig.SpectatorStates, Is.EqualTo(spectator + 1), "States at the Spectator, " + at);
                StateMessage sent = rig.Loop.LastState;
                StateMessage got = rig.SpectatorState;
                Assert.That(got.Seq, Is.EqualTo(rig.HubState.Seq), at);
                Assert.That(got.SpaceId, Is.EqualTo(sent.SpaceId), at);
                Assert.That(got.BumpLevel, Is.EqualTo((byte)rig.Guard.Level), "the Station sees Bump Guard's level in its frame, " + at);
                Assert.That(new float3(got.HeadPosX, got.HeadPosY, got.HeadPosZ), Is.EqualTo(rig.Frame.RawHeadPosition), at);
                for (int i = 0; i < StateMessage.TileLength; i++) Assert.That(got.GetTile(i), Is.EqualTo(sent.GetTile(i)), at);
                for (int i = 0; i < StateMessage.XformLength; i++) Assert.That(got.GetXform(i), Is.EqualTo(sent.GetXform(i)), at);
                for (int i = 0; i < StateMessage.ParamLength; i++) Assert.That(got.GetParam(i), Is.EqualTo(sent.GetParam(i)), at);

                // Section 7.6, L7, and the flat twin of P1-D07: the Station holds the phone's tile sequence, every sample of it.
                if (sent.SpaceId != _trailSpace)
                {
                    _trailSpace = sent.SpaceId;
                    _tilesSeen = 0;
                }
                var tile = new TileId(sent.Tile0, sent.Tile1, sent.Tile2, sent.Tile3, sent.Tile4, sent.Tile5);
                if (_tilesSeen == 0 || tile != _lastTile)
                {
                    _tilesSeen++;
                    _lastTile = tile;
                }
                FlatTwinTrail trail = Station.Core.Trail;
                Assert.That(trail.TilesSeen, Is.EqualTo(_tilesSeen), "tiles in the Station's trail, " + at);
                Assert.That(trail.GetTile(trail.TileCount - 1), Is.EqualTo(tile), at);

                // A space left at step 8b put the phone's kernel back to flat after this State was sent: the
                // picture is compared while the phone is still in the space the State names.
                bool inSpace = rig.Lab.HasSpace && (byte)rig.Lab.Current == sent.SpaceId;
                if (inSpace && Station.Host.HasSpace && Station.Host.Current == rig.Lab.Current) Follow(at);
            }

            // The Station's picture of the walker is the phone's, from the State alone: nothing is integrated there.
            void Follow(string at)
            {
                RehearsalRig rig = Rig;
                StationModule module = Station.Module;
                Assert.That(module, Is.Not.Null, at);
                Assert.That(module.Last.Seq, Is.EqualTo(rig.SpectatorState.Seq), "the module read this frame's State, " + at);
                SpaceId space = Station.Host.Current;
                switch (space)
                {
                    case SpaceId.CurvedWalk:
                    case SpaceId.SmallPlanet:
                    {
                        WalkerPose phone = rig.Kernel.Pose, station = Station.Kernel.Pose;
                        Assert.That(Station.Kernel.Q, Is.EqualTo(rig.Kernel.Q), "the Station's tiling, " + at);
                        Assert.That(station.Tile, Is.EqualTo(phone.Tile), "the Station's tile, " + at);
                        AssertSame(in station.Local, in phone.Local, "the Station's walker, " + at);
                        Assert.That(Station.Kernel.MetresPerUnit, Is.EqualTo(rig.Kernel.MetresPerUnit).Within(1e-6), "the Station's L, " + at);
                        if (space == SpaceId.SmallPlanet)
                        {
                            SmallPlanetCore planet = rig.Module<SmallPlanetModule>().Core;
                            Assert.That(module.Planet.Face, Is.EqualTo(planet.Face), at);
                            AssertSame(module.Planet.U, planet.U, "the Station's planet, " + at);
                        }
                        break;
                    }
                    case SpaceId.AlignedCells:
                    {
                        AlignedCellsCore cells = rig.Module<AlignedCellsModule>().Core;
                        Assert.That(module.Cells.Walker.Frame, Is.EqualTo(cells.Walker.Frame), "the Station's cell frame, " + at);
                        Assert.That(module.Cells.Walker.Cell, Is.EqualTo(cells.Walker.Cell), "the Station's cell, " + at);
                        break;
                    }
                }
                Followed[(int)space]++;
            }

            public void Stand(int frames) { for (int i = 0; i < frames; i++) Step(); }

            /// <summary>Steps until the condition holds. Returns the frames it took, or -1 after the limit.</summary>
            public int Until(Func<bool> condition, int maxFrames)
            {
                for (int i = 0; i <= maxFrames; i++)
                {
                    if (condition()) return i;
                    Step();
                }
                return -1;
            }

            /// <summary>Plays a walk to its end, one sample per frame; the walker then stands on its last sample.</summary>
            public void Play(PoseLog log)
            {
                Rig.Play(log);
                for (int k = 0; k < log.Samples.Length; k++) Step();
                Rig.StandAt(in log.Samples[log.Samples.Length - 1]);
            }

            /// <summary>Walks in a straight line from where the head is.</summary>
            public void WalkTo(float2 floor)
            {
                Play(RehearsalRig.Line(Room.Id, Rig.HeadFloor, floor, WalkSpeed));
            }

            /// <summary>Asks for a space the Station's way and waits until it runs.</summary>
            public void Switch(SpaceId space)
            {
                RehearsalRig rig = Rig;
                Assert.That(rig.Hub.SendCommand(OperatorCommand.SwitchSpace((int)space)), Is.True, "the Hub has no phone");
                Assert.That(Until(() => rig.Lab.State == SpaceHostState.Running && rig.Lab.HasSpace && rig.Lab.Current == space, 120),
                    Is.GreaterThanOrEqualTo(0), SpaceIds.Code(space) + " was never entered");
            }

            /// <summary>The Station shows the phone's space, with the phone's room and world.</summary>
            public void AssertStationShows(SpaceId space)
            {
                Assert.That(Station.Core.Banner, Is.EqualTo(SpectatorBanner.None), "the Station's banner in " + SpaceIds.Code(space));
                Assert.That(Station.Host.HasSpace, Is.True);
                Assert.That(Station.Host.Current, Is.EqualTo(space));
                Assert.That(Station.Core.Room, Is.Not.Null);
                Assert.That(Station.Core.Room, Is.Not.SameAs(Room), "the Station holds its own copy of the room");
                Assert.That(Station.Core.Room.Id, Is.EqualTo(Room.Id));
                Assert.That(Station.Core.WorldVerified, Is.True, "the world hash of " + SpaceIds.Code(space) + " is the phone's");
                Assert.That(Station.Host.LastError, Is.Null);
            }

            /// <summary>
            /// The recorded walk at 1 m/s towards the real wall, from the point it begins at. Bump Guard is armed,
            /// its level rises at least 0.6 s before the predicted contact, the cue is asked for, and the Hub and
            /// the Spectator see the level in the frame it rose. Returns the lead time in seconds.
            /// </summary>
            public double WallApproach(PoseLog log, string where)
            {
                RehearsalRig rig = Rig;
                Assert.That(math.distance(rig.HeadFloor, log.Samples[0].Floor), Is.LessThan(1e-4f), "the walk begins where the head stands, " + where);
                Assert.That(Until(() => rig.Guard.Level == BumpLevel.Clear, 120), Is.GreaterThanOrEqualTo(0), "the level before the walk, " + where);
                Assert.That(rig.Guard.Armed, Is.True, "Bump Guard is armed in " + where);
                Assert.That(rig.Guard.Core.HasRoom, Is.True, where);
                int fires = BumpCue.FireCount;

                // Contact is where the path would reach clear() = 0: one 5 cm cell short of the wall (section 4.10).
                float wallZ = Room.Outline[2].y;
                float2 start = log.Samples[0].Floor;
                double speed = math.distance(log.Samples[60].Floor, start);
                double contactSeconds = (wallZ - BumpConstants.StaticInset - start.y) / speed;

                int cueFrame = -1;
                rig.Play(log);
                for (int k = 0; k < log.Samples.Length; k++)
                {
                    Step();
                    Assert.That(rig.Guard.Level, Is.Not.EqualTo(BumpLevel.TrackingLost), where);
                    if (cueFrame >= 0 || rig.Guard.Level == BumpLevel.Clear) continue;
                    cueFrame = k;
                    Assert.That(rig.HubState.BumpLevel, Is.EqualTo((byte)rig.Guard.Level), "the Hub sees the level in the frame it rose, " + where);
                    Assert.That(rig.HubState.CueActive, Is.True, "the level rose but no cue is shown, " + where);
                    Assert.That(rig.SpectatorState.CueActive, Is.True, where);
                }
                rig.StandAt(in log.Samples[log.Samples.Length - 1]);

                Assert.That(cueFrame, Is.GreaterThanOrEqualTo(0), "Bump Guard never warned in " + where);
                double lead = contactSeconds - cueFrame / 60.0;
                TestContext.WriteLine("{0}: cue {1:F3} s into the approach, {2:F3} s before predicted contact", where, cueFrame / 60.0, lead);
                Assert.That(lead, Is.GreaterThanOrEqualTo(0.6), "lead time to predicted contact in " + where);
                Assert.That(BumpCue.FireCount, Is.GreaterThan(fires), "the cue was asked for in " + where);
                Assert.That(BumpCue.Shown, Is.Not.EqualTo(BumpLevel.Clear), where);
                Assert.That(rig.Guard.Level, Is.Not.EqualTo(BumpLevel.Clear), "0.3 m from the wall is still warned, " + where);
                return lead;
            }
        }

        static void AssertSame(in Iso2 actual, in Iso2 expected, string at)
        {
            Assert.That(actual.Kappa, Is.EqualTo(expected.Kappa), at);
            Assert.That(actual.M00, Is.EqualTo(expected.M00).Within(1e-12), at);
            Assert.That(actual.M01, Is.EqualTo(expected.M01).Within(1e-12), at);
            Assert.That(actual.M02, Is.EqualTo(expected.M02).Within(1e-12), at);
            Assert.That(actual.M10, Is.EqualTo(expected.M10).Within(1e-12), at);
            Assert.That(actual.M11, Is.EqualTo(expected.M11).Within(1e-12), at);
            Assert.That(actual.M12, Is.EqualTo(expected.M12).Within(1e-12), at);
            Assert.That(actual.M20, Is.EqualTo(expected.M20).Within(1e-12), at);
            Assert.That(actual.M21, Is.EqualTo(expected.M21).Within(1e-12), at);
            Assert.That(actual.M22, Is.EqualTo(expected.M22).Within(1e-12), at);
        }

        /// <summary>The State's tile words and local frame are the kernel's pose of this frame, at the Spectator.</summary>
        static void AssertStateIsThePose(RehearsalRig rig)
        {
            WalkerPose pose = rig.Kernel.Pose;
            string at = "frame " + rig.Frames;
            StateMessage s = rig.SpectatorState;
            Assert.That(new TileId(s.Tile0, s.Tile1, s.Tile2, s.Tile3, s.Tile4, s.Tile5), Is.EqualTo(pose.Tile), at);
            var rowMajor = new double[StateMessage.XformLength];
            for (int i = 0; i < rowMajor.Length; i++) rowMajor[i] = s.GetXform(i);
            AssertSame(Iso2.FromRowMajor(rowMajor, pose.Local.Kappa), in pose.Local, "State.xform, " + at);
        }

        /// <summary>
        /// The first sides of the recorded five-sided lap, up to the end of the stop at the corner that ends the
        /// last of them. The lap turns left from the corner (-s/2, -s/2), first along +X.
        /// </summary>
        static PoseLog Sides(PoseLog lap, float side, int count)
        {
            float h = 0.5f * side;
            float2[] corners = { new float2(h, -h), new float2(h, h), new float2(-h, h), new float2(-h, -h), new float2(h, -h) };
            int i = 1;
            for (int k = 0; k < count; k++)
            {
                while (i < lap.Samples.Length && math.distance(lap.Samples[i].Floor, corners[k]) > 1e-4f) i++;
                Assert.That(i, Is.LessThan(lap.Samples.Length), "the recording has no corner " + (k + 1));
                while (i + 1 < lap.Samples.Length && math.distance(lap.Samples[i + 1].Floor, corners[k]) <= 1e-4f) i++;
            }
            var samples = new HeadPose[i + 1];
            Array.Copy(lap.Samples, samples, samples.Length);
            return new PoseLog { RoomId = lap.RoomId, Frame = lap.Frame, Samples = samples };
        }

        [Test]
        public void E2E_Phase1_OneSession_RoomAsTileZero_The345Walk_SpectatorFollows_Mode2KeepsTheTile_SmallPlanet_Home()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            PoseLog lap5 = RehearsalRig.LoadWalk(Lap5Walk);
            PoseLog wall = RehearsalRig.LoadWalk(WallWalk);
            Assert.That(lap5.RoomId, Is.EqualTo(room.Id));
            Assert.That(wall.RoomId, Is.EqualTo(room.Id));

            // ---- The RoomModel and its loop-fit (section 4.4): s = 2.0 m, L = 1.885 m, five squares meet at L, three at R.
            LoopFitResult fit = LoopFit.Fit(room.FreeRect.Wf, room.FreeRect.Df);
            Assert.That(room.Standing, Is.False);
            Assert.That(room.LapSide, Is.EqualTo(2.0f).Within(1e-6f));
            Assert.That(fit.S, Is.EqualTo(room.LapSide).Within(1e-6));
            Assert.That(room.Curvature.L5, Is.EqualTo(fit.L5).Within(1e-5));
            Assert.That(room.Curvature.R3, Is.EqualTo(fit.R3).Within(1e-5));
            Assert.That(fit.L5, Is.EqualTo(1.885).Within(1e-3));
            Assert.That(fit.S / fit.L5, Is.EqualTo(LoopFit.SpacingQ5).Within(1e-9));
            Assert.That(fit.S / fit.R3, Is.EqualTo(Math.PI / 2).Within(1e-9));
            double l5 = room.Curvature.ForDial(5, false);
            double r3 = SmallPlanetCore.RadiusFor(room, false);
            float2 lapCorner = lap5.Samples[0].Floor, wallStart = wall.Samples[0].Floor;

            using (var s = new Session(room))
            {
                RehearsalRig rig = s.Rig;
                RehearsalSpectator station = s.Station;

                // ---- The hand-over to Home, with the Station watching.
                rig.StandAt(in lap5.Samples[0]);
                rig.StartStation();
                Assert.That(s.Until(() => rig.LinkUp, 300), Is.GreaterThanOrEqualTo(0), "the link never came up");
                rig.HandOver.Activate(room);
                rig.Guard.Armed = true;         // "headset on"
                Assert.That(s.Until(() => rig.Lab.State == SpaceHostState.Running && rig.Lab.HasSpace, 240), Is.GreaterThanOrEqualTo(0), "Home was never entered");
                Assert.That(rig.Lab.Current, Is.EqualTo(SpaceId.Home));
                Assert.That(rig.Guard.Core.HasRoom, Is.True, "Bump Guard has no map of the room");
                Assert.That(math.distance((float2)rig.WalkerFloor, rig.HeadFloor), Is.LessThan(1e-5f), "the walker was not placed at the head");
                s.Stand(30);
                s.AssertStationShows(SpaceId.Home);

                // ---- S2 Curved Walk: the app's kernel on {4,5} with the room as tile zero, then the ramp.
                s.Switch(SpaceId.CurvedWalk);
                var s2 = rig.Module<CurvedWalkModule>();
                Assert.That(s2, Is.Not.Null);
                Assert.That(rig.Kernel.Q, Is.EqualTo(5));
                Assert.That(rig.Kernel.Pose.Tile, Is.EqualTo(TileId.Zero), "the room is tile zero");
                Assert.That(rig.Kernel.ConfiguredMetresPerUnit, Is.EqualTo(l5).Within(1e-12), "L is the room's loop-fit value");
                Assert.That(s.Until(() => rig.Kernel.CurvatureNow != 0 && !rig.Kernel.Ramping, 420), Is.GreaterThanOrEqualTo(0), "the curvature never arrived");
                Assert.That(rig.Kernel.CurvatureNow, Is.EqualTo(-1.0 / (l5 * l5)).Within(1e-12));
                // Tile zero's chart about the room's centre is the room: the walker stands over the head's floor point.
                double2 chart = Geometry2.Log(rig.Kernel.Pose.Local.M.c2, Curvature.Hyperbolic) * l5;
                Assert.That(math.distance(chart, (double2)rig.HeadFloor), Is.LessThan(1e-6), "the walker in tile zero is not over the head");
                s.Stand(2);
                Assert.That(s2.Core.Walk.Armed, Is.True);
                Assert.That(s2.Core.Walk.Q, Is.EqualTo(5));
                s.AssertStationShows(SpaceId.CurvedWalk);
                Assert.That(station.Module.ContentHash, Is.EqualTo(s2.ContentHash), "the Station generated the phone's world from its own copy of the room");
                Assert.That(s2.ContentHash.Length, Is.EqualTo(16));

                // ---- Five corners: the recorded lap closes on {4,5} within 0.25 m (gates G2 and G7).
                bool left = false;
                long steps = rig.Kernel.StepCount;
                int frames = rig.Frames, followed = s.Followed[(int)SpaceId.CurvedWalk];
                rig.Play(lap5);
                for (int k = 0; k < lap5.Samples.Length; k++)
                {
                    s.Step();
                    AssertStateIsThePose(rig);
                    Assert.That(rig.SpectatorState.GetParam(0), Is.EqualTo((float)l5), "p[0] is L, frame " + rig.Frames);
                    Assert.That(rig.SpectatorState.GetParam(CurvedWalkCore.StateQ), Is.EqualTo(5f));
                    Assert.That(rig.SpectatorState.PoseJumpRejected, Is.False, "frame " + rig.Frames);
                    if (!rig.Kernel.Pose.Tile.Equals(TileId.Zero)) left = true;
                }
                rig.StandAt(in lap5.Samples[lap5.Samples.Length - 1]);
                s.Stand(30);
                Assert.That(rig.Kernel.StepCount - steps, Is.EqualTo(rig.Frames - frames), "one kernel step per frame, by the frame loop alone");
                Assert.That(s.Followed[(int)SpaceId.CurvedWalk] - followed, Is.EqualTo(rig.Frames - frames), "the Station followed every frame of the lap");
                LapWalk walk = s2.Core.Walk;
                TestContext.WriteLine("q = 5: {0} corners, closure {1:0.000} m, world turn {2:0.0} deg", walk.Corners, walk.DistanceToFlagM, walk.WorldTurnDeg);
                Assert.That(left, Is.True, "the lap never left tile zero");
                Assert.That(walk.Closed, Is.True, "five corners close the loop on {4,5}");
                Assert.That(walk.Corners, Is.EqualTo(5));
                Assert.That(walk.DistanceToFlagM, Is.LessThan(0.25), "closure after five corners");
                Assert.That(rig.Kernel.Pose.Tile, Is.EqualTo(TileId.Zero), "back in the room's tile");

                // ---- Four corners: the dial to q = 4 while standing, the curvature ramps to zero, the lap closes flat.
                s.WalkTo(lapCorner);
                Assert.That(s.Until(() => rig.Kernel.Still, 120), Is.GreaterThanOrEqualTo(0));
                s2.Input = new CurvedWalkInput { DialLeft = true };
                s.Step();
                Assert.That(s2.Core.SelectedQ, Is.EqualTo(4));
                Assert.That(rig.Registry.GetInt(CurvedWalkCore.ParamQ), Is.EqualTo(4));
                Assert.That(s.Until(() => rig.Kernel.CurvatureNow == 0 && !rig.Kernel.Ramping && s2.Core.Walk.Armed && s2.Core.Walk.Started, 600),
                    Is.GreaterThanOrEqualTo(0), "the flat walk was never armed");
                Assert.That(s2.Core.Walk.Q, Is.EqualTo(4));
                Assert.That(rig.Kernel.Pose.Tile, Is.EqualTo(TileId.Zero), "at K = 0 the walker is registered to the room again");
                Assert.That(rig.Kernel.MetresPerUnit, Is.EqualTo(1.0), "p[0] at the flat stand");
                s.Play(Sides(lap5, room.LapSide, 4));
                s.Stand(30);
                TestContext.WriteLine("q = 4: {0} corners, closure {1:0.000} m", walk.Corners, walk.DistanceToFlagM);
                Assert.That(walk.Closed, Is.True, "four corners close the loop on the flat floor");
                Assert.That(walk.Corners, Is.EqualTo(4));
                Assert.That(walk.DistanceToFlagM, Is.LessThan(0.25), "closure after four corners");
                Assert.That(rig.Kernel.CurvatureNow, Is.EqualTo(0.0));

                // ---- Back to q = 5, then Bump Guard at the real wall while the walker crosses into another tile.
                s2.Input = new CurvedWalkInput { DialRight = true };
                s.Step();
                Assert.That(s2.Core.SelectedQ, Is.EqualTo(5));
                Assert.That(s.Until(() => rig.Kernel.CurvatureNow != 0 && !rig.Kernel.Ramping, 420), Is.GreaterThanOrEqualTo(0));
                s.WalkTo(wallStart);
                s.WallApproach(wall, "S2");
                WalkerPose mode1 = rig.Kernel.Pose;
                Assert.That(mode1.Tile.Equals(TileId.Zero), Is.False, "the walk to the wall left tile zero");
                s.Stand(30);
                s.AssertStationShows(SpaceId.CurvedWalk);

                // ---- S2 to S3: Mode 1 to Mode 2 keeps the tile id and the pose (sections 4.8 and 4.9). The
                //      walker stands 0.3 m from the wall all through the switch, and Bump Guard says so in every frame.
                Assert.That(rig.Hub.SendCommand(OperatorCommand.SwitchSpace((int)SpaceId.AlignedCells)), Is.True);
                int fade = 0;
                while (!(rig.Lab.State == SpaceHostState.Running && rig.Lab.Current == SpaceId.AlignedCells) && fade++ < 120)
                {
                    s.Step();
                    Assert.That(rig.Guard.Armed, Is.True, "frame " + rig.Frames);
                    Assert.That(rig.Guard.Level, Is.Not.EqualTo(BumpLevel.Clear), "Bump Guard through the switch, frame " + rig.Frames);
                }
                Assert.That(rig.Lab.Current, Is.EqualTo(SpaceId.AlignedCells));
                var s3 = rig.Module<AlignedCellsModule>();
                Assert.That(s3, Is.Not.Null);
                Assert.That(s3.Start.Tile, Is.EqualTo(mode1.Tile), "the pose Mode 1 left, handed over by SpaceLab");
                Assert.That(s3.Core.EnteredFromCurvedWalk, Is.True);
                Assert.That(s3.Core.Walker.Frame.Tile, Is.EqualTo(mode1.Tile), "the same tile id in Mode 2");
                Assert.That(rig.Kernel.Q, Is.EqualTo(4), "Aligned Cells is registered: the kernel is flat again");
                Assert.That(math.distance((float2)rig.WalkerFloor, rig.HeadFloor), Is.LessThan(1e-4f));
                s.Stand(30);
                for (int i = 0; i < StateMessage.TileLength; i++)
                    Assert.That(rig.SpectatorState.GetTile(i), Is.EqualTo(mode1.Tile[i]), "State.tile word " + i + " at the Spectator");
                s.AssertStationShows(SpaceId.AlignedCells);
                s.WalkTo(wallStart);
                s.WallApproach(wall, "S3");
                Assert.That(s3.Core.Crossings, Is.GreaterThan(0), "the walk crossed a cell edge");

                // ---- S4 Small Planet: the same kernel on the sphere, and the three-corner walk.
                s.WalkTo(wallStart);
                s.WalkTo(lapCorner);
                // The lap closes on the room's own planet (R = 2s/pi): planet.R's default, the PlanetKit tiles' R0, is
                // larger than this room's, so the operator sets it to 0 for the three-corner walk.
                rig.Registry.Set(SmallPlanetCore.ParamRadius, 0f);
                s.Switch(SpaceId.SmallPlanet);
                var s4 = rig.Module<SmallPlanetModule>();
                Assert.That(s4, Is.Not.Null);
                Assert.That(rig.Kernel.Q, Is.EqualTo(3));
                Assert.That(rig.Kernel.Kappa, Is.EqualTo(Curvature.Spherical));
                Assert.That(rig.Kernel.MetresPerUnit, Is.EqualTo(r3).Within(1e-12));
                Assert.That(s.Until(() => s4.Core.Walk.Started, 120), Is.GreaterThanOrEqualTo(0), "the three-corner walk never started");
                s.AssertStationShows(SpaceId.SmallPlanet);
                followed = s.Followed[(int)SpaceId.SmallPlanet];
                frames = rig.Frames;
                PoseLog three = Sides(lap5, room.LapSide, 3);
                rig.Play(three);
                for (int k = 0; k < three.Samples.Length; k++)
                {
                    s.Step();
                    AssertStateIsThePose(rig);
                    Assert.That(rig.SpectatorState.GetParam(0), Is.EqualTo((float)r3), "p[0] is R, frame " + rig.Frames);
                }
                rig.StandAt(in three.Samples[three.Samples.Length - 1]);
                s.Stand(30);
                Assert.That(s.Followed[(int)SpaceId.SmallPlanet] - followed, Is.EqualTo(rig.Frames - frames), "the Station followed every frame on the planet");
                walk = s4.Core.Walk;
                TestContext.WriteLine("q = 3: {0} corners, closure {1:0.000} m, world turn {2:0.0} deg", walk.Corners, walk.DistanceToFlagM, walk.WorldTurnDeg);
                Assert.That(walk.Closed, Is.True, "three corners close the loop on the sphere");
                Assert.That(walk.Corners, Is.EqualTo(3));
                Assert.That(walk.DistanceToFlagM, Is.LessThan(0.25), "closure after three corners");
                s.WalkTo(wallStart);
                s.WallApproach(wall, "S4");

                // ---- Home: flat, registered, the walker under the head, on the phone and at the Station.
                s.WalkTo(wallStart);
                Assert.That(rig.Hub.SendCommand(OperatorCommand.Home()), Is.True);
                Assert.That(s.Until(() => rig.Lab.State == SpaceHostState.Running && rig.Lab.Current == SpaceId.Home, 120), Is.GreaterThanOrEqualTo(0));
                s.Stand(30);
                Assert.That(rig.Lab.Module.Registered, Is.True);
                Assert.That(rig.Kernel.Q, Is.EqualTo(4));
                Assert.That(rig.Kernel.Kappa, Is.EqualTo(Curvature.Flat));
                Assert.That(((FlatTileGraph)rig.Kernel.Graph).CentreSpacing * rig.Kernel.MetresPerUnit, Is.EqualTo(room.LapSide).Within(1e-9));
                Assert.That(math.distance((float2)rig.WalkerFloor, rig.HeadFloor), Is.LessThan(1e-4f), "re-registered at the head");
                Assert.That(rig.Guard.Armed, Is.True);
                s.AssertStationShows(SpaceId.Home);

                // ---- The session as a whole.
                Assert.That(rig.Loader.Loads, Is.EqualTo(5), "Home, S2, S3, S4, Home");
                Assert.That(rig.Loader.Unloads, Is.EqualTo(4));
                Assert.That(rig.Warnings, Is.Empty, "frame loop warnings");
                Assert.That(rig.Loop.FaultedSenseCount, Is.EqualTo(0));
                Assert.That(rig.HasEvent("space.error"), Is.False, "a space threw");
                Assert.That(rig.HasEvent("space.loadFailed"), Is.False);
                Assert.That(rig.HasEvent("walk.closed"), Is.True);
                Assert.That(s.Followed[(int)SpaceId.CurvedWalk], Is.GreaterThan(0));
                Assert.That(s.Followed[(int)SpaceId.AlignedCells], Is.GreaterThan(0));
                Assert.That(s.Followed[(int)SpaceId.SmallPlanet], Is.GreaterThan(0));
                for (int i = 0; i < station.Banners.Count; i++)
                {
                    TestContext.WriteLine("Station banner in frame {0}: {1}", station.Banners[i].Key, station.Banners[i].Value);
                    Assert.That(station.Banners[i].Value, Is.Not.EqualTo(SpectatorBanner.WorldMismatch), "frame " + station.Banners[i].Key);
                    Assert.That(station.Banners[i].Value, Is.Not.EqualTo(SpectatorBanner.SpaceMissing), "frame " + station.Banners[i].Key);
                }
                TestContext.WriteLine("{0} frames; the Station followed S2 in {1}, S3 in {2}, S4 in {3}", rig.Frames,
                    s.Followed[(int)SpaceId.CurvedWalk], s.Followed[(int)SpaceId.AlignedCells], s.Followed[(int)SpaceId.SmallPlanet]);
            }
        }

        // ---- the world frame ----------------------------------------------------------------------------------

        /// <summary>The AR camera's stand-in: a head whose pose is in the AR session frame, as on the phone.</summary>
        sealed class SessionHead : IPoseSource
        {
            public float3 Position;
            public quaternion Rotation = quaternion.identity;

            public string Name { get { return "session"; } }
            public PoseFrameKind Frame { get { return PoseFrameKind.Session; } }
            public bool DrivesRig { get { return false; } }

            public bool TryRead(double time, float deltaTime, out HeadPose pose)
            {
                pose = new HeadPose(time, Position, Rotation, TrackingState.Normal);
                return true;
            }
        }

        [Test]
        public void E2E_WorldFrame_IsTheRoomModelsAnchorFrame_WithTheFloorAtYZero()
        {
            // The open point of kernel.md section 9.1 (lane C contract, section 14.2): after the hand-over the
            // frame every lane works in is the RoomModel's anchor frame, with its origin on the floor. The
            // curved spaces assume it: tile zero's centre is the origin and the warp leaves y alone.
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            room.Anchor = new RoomAnchor { Id = "", Position = new float3(2.5f, -1.37f, -4f), Yaw = 0.6f };
            quaternion anchorRotation = quaternion.RotateY(room.Anchor.Yaw);
            Func<float3, float3> toSession = p => room.Anchor.Position + math.mul(anchorRotation, p);
            const float eye = RehearsalRig.EyeHeight;
            var head = new SessionHead();

            using (var rig = new RehearsalRig())
            {
                // The phone is held over the anchor, looking along the room's +Z.
                head.Position = toSession(new float3(0f, eye, 0f));
                head.Rotation = anchorRotation;
                rig.Pipeline.Source = head;
                rig.Steps(30);
                Assert.That(math.distance(rig.Frame.RawHeadPosition, head.Position), Is.LessThan(1e-5f), "with no room the frame is the session's");

                Assert.That(rig.ActivateRoom(room), Is.GreaterThanOrEqualTo(0), "Home was never entered");
                rig.Steps(2);
                Assert.That(math.distance(rig.Frame.RawHeadPosition, new float3(0f, eye, 0f)), Is.LessThan(1e-4f), "the anchor is the origin, and the floor under it is y = 0");
                Assert.That(math.distance(rig.Frame.HeadPosition, new float3(0f, eye, 0f)), Is.LessThan(1e-4f));
                Assert.That(math.distance(math.mul(rig.Frame.HeadRotation, new float3(0f, 0f, 1f)), new float3(0f, 0f, 1f)), Is.LessThan(1e-4f), "the anchor's yaw is the room's +Z");
                Assert.That(math.length((float2)rig.WalkerFloor), Is.LessThan(1e-4f), "the walker stands on the room's origin");

                // A walk of the session frame is the same walk in the room frame, on the floor all the way.
                var target = new float3(0.6f, eye, -0.8f);
                for (int k = 1; k <= 120; k++)
                {
                    head.Position = toSession(target * new float3(k / 120f, 1f, k / 120f));
                    rig.Step();
                    Assert.That(rig.Frame.RawHeadPosition.y, Is.EqualTo(eye).Within(1e-4f), "frame " + rig.Frames);
                    Assert.That(rig.Frame.PoseJumpRejected, Is.False, "frame " + rig.Frames);
                }
                Assert.That(math.distance(rig.Frame.HeadPosition, target), Is.LessThan(1e-4f));
                Assert.That(room.Sdf.IsWalkable(target.xz), Is.True);
                Assert.That(math.distance((float2)rig.WalkerFloor, target.xz), Is.LessThan(1e-4f), "the kernel's walker is under the head, in the room frame");
                // Bump Guard works in the same frame: the nearest wall of the 3 x 3 m room is 0.7 m from here.
                Assert.That(rig.Guard.Clearance(target.xz), Is.EqualTo(0.7f).Within(0.11f));
                Assert.That(rig.Guard.Level, Is.EqualTo(BumpLevel.Clear));

                // Curved Walk registers the room as tile zero in that frame: at K = 0 the walker's frame is the
                // translation to the head's floor point, in metres (section 4.6).
                rig.Commands.SwitchSpace(SpaceId.CurvedWalk, null);
                Assert.That(rig.StepUntil(() => rig.Lab.HasSpace && rig.Lab.Current == SpaceId.CurvedWalk, 120), Is.GreaterThanOrEqualTo(0));
                WalkerPose start = rig.Kernel.Pose;
                Iso2 registered = TileZeroPlacement.Registered((double2)target.xz, Curvature.Flat, 1.0);
                Assert.That(start.Tile, Is.EqualTo(TileId.Zero));
                Assert.That(rig.Kernel.CurvatureNow, Is.EqualTo(0.0));
                Assert.That(start.Local.M02, Is.EqualTo(registered.M02).Within(1e-4));
                Assert.That(start.Local.M12, Is.EqualTo(registered.M12).Within(1e-4));
                Assert.That(start.Local.M00, Is.EqualTo(1.0).Within(1e-12), "tile zero's axes are the room's axes");
                Assert.That(start.Local.M01, Is.EqualTo(0.0).Within(1e-12));
                Assert.That(rig.Warnings, Is.Empty);
            }
        }
    }
}
