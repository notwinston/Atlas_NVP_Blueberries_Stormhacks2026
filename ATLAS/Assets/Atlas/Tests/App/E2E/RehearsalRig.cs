using System;
using System.Collections.Generic;
using System.IO;
using Atlas.App;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Safety;
using Atlas.Scan;
using Atlas.Spaces.Glued;
using Atlas.Spaces.Home;
using Atlas.Spaces.Portal;
using Atlas.Spaces.Slicer;
using Atlas.Telemetry;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.E2E.Tests
{
    /// <summary>A change of Bump Guard's level during a rehearsal.</summary>
    struct BumpChange
    {
        public int Frame;
        public double Time;
        public BumpLevel From, To;
        public float2 Floor;
    }

    /// <summary>
    /// The phone and the Station on one in-memory network, with no engine. The phone side is put together in
    /// the order of the composition root (AtlasApp.Build): registry, kernel, frame loop, pose pipeline, Bump
    /// Guard, quality ladder, SpaceLab, parameters, operator commands, link, room hand-over, then the tail of
    /// the frame. Everything that has an engine-free core is that core; only the pose pipeline's adapter and
    /// the four space adapters are replaced by the engine-free stand-ins of this folder. The Station side is a
    /// real LinkHost (the Hub's link) and a Spectator client behind it.
    /// </summary>
    sealed class RehearsalRig : IDisposable
    {
        public const float Dt = 1f / 60f;
        public const double FrameSeconds = 1.0 / 60.0;
        public const float EyeHeight = 1.6f;
        public const int Port = 9050;
        public const string Session = "e2e-rehearsal";
        const long StartUs = 1000000;

        // ---- the Station ----
        public readonly ManualClock Clock = new ManualClock(StartUs);
        public readonly LoopbackNetwork Network;
        public readonly LinkHost Hub;
        public readonly LinkClient Spectator;

        // ---- the phone ----
        public readonly ParamRegistry Registry = new ParamRegistry();
        public readonly HolonomyWalkerKernel Kernel = new HolonomyWalkerKernel();
        public readonly FrameLoopCore Loop;
        public readonly RehearsalPosePipeline Pipeline = new RehearsalPosePipeline();
        public readonly SyntheticDepthSource Depth = new SyntheticDepthSource();
        public readonly StandingPoseSource Standing = new StandingPoseSource();
        public readonly BumpGuard Guard = new BumpGuard(false);
        public readonly QualityLadder Ladder;
        public readonly RehearsalSpaceLoader Loader = new RehearsalSpaceLoader();
        public readonly RehearsalStage Stage = new RehearsalStage();
        public readonly SpaceLab Lab;
        public readonly CommandRouter Commands;
        public readonly EventLog Events;
        public readonly LinkClient Link;
        public readonly LinkPump Pump;
        public readonly RoomHandOver HandOver;

        // ---- what the rehearsal saw ----
        public readonly List<string> Warnings = new List<string>();
        public readonly List<EventMessage> Logged = new List<EventMessage>(64);
        public readonly List<BumpChange> BumpChanges = new List<BumpChange>(32);
        /// <summary>The last State the Hub decoded, and how many it has decoded.</summary>
        public StateMessage HubState;
        public int HubStates;
        /// <summary>The last State the Spectator decoded from the Hub's relay.</summary>
        public StateMessage SpectatorState;
        public int SpectatorStates;
        public RoomOutlineMessage HubOutline;
        public int Frames;
        BumpLevel _lastLevel;
        readonly LinkHostStateHandler _onHubState;
        readonly LinkStateHandler _onSpectatorState;

        public RehearsalRig()
        {
            BumpCue.Shutdown();     // the cue is static: nothing of an earlier rehearsal may show

            Network = new LoopbackNetwork(Clock);
            Hub = new LinkHost(Network.CreateEndpoint(), Clock) { SessionId = "hub-session", ForgeUrl = "http://10.0.0.2:8000" };
            Spectator = new LinkClient(Network.CreateEndpoint(), Clock, LinkRole.Spectator) { Device = "spectator" };
            _onHubState = OnHubState;
            _onSpectatorState = OnSpectatorState;
            Hub.StateReceived += _onHubState;
            Hub.RoomOutlineReceived += (peer, outline) => HubOutline = outline;
            Spectator.StateReceived += _onSpectatorState;

            // 8, 9  Kernel and frame loop.
            Loop = new FrameLoopCore(Kernel) { Warning = Warnings.Add };

            // 11  The pose pipeline: step 1, then step 3. The depth is the Editor's ray-cast image (F9).
            Pipeline.Source = Standing;
            Pipeline.Depth = Depth;
            Loop.AddSense(Pipeline);
            Loop.SetPoseFilter(Pipeline);
            Loop.Pipeline = Pipeline;

            // 12  Bump Guard: step 2 of every frame from the first.
            Loop.SetGuard(new BumpGuardStep(Guard, Pipeline));

            // 15  The quality ladder. Off the device there is no thermal plugin.
            Ladder = new QualityLadder(new QualityApplier(null));
            Ladder.Applier.ThermalOverride = 0;
            Loop.Quality = Ladder;

            // 16  SpaceLab.
            Lab = new SpaceLab(Registry, Kernel, Loop, Loader, Stage);
            Loader.Bind(Lab);
            Loop.Space = Lab;
            Loop.Host = Lab;

            // The parameters, in the composition root's order, then the spaces' own.
            AppParams.SpaceBinding space = AppParams.DeclareSpace(Registry, Lab);
            AppParams.DeclareRender(Registry, Ladder, Kernel, null, null, null, false, null, null);
            AppParams.DeclareUnownedKillSwitches(Registry);
            AppParams.DeclarePose(Registry, Pipeline, source => { }, speed => { }, loop => { }, record => { });
            HomeSpaceCore.DeclareParams(Registry);
            GluedSpace.DeclareParams(Registry);
            PortalSpaceCore.DeclareParams(Registry);
            SlicerParams.Declare(Registry);

            // 17  Operator commands as local calls.
            Commands = new CommandRouter(Lab, Loop, Registry, Pipeline);

            // 18  Link and event log, as the telemetry installer makes them.
            Events = new EventLog(Clock, null);
            Events.Logged += Logged.Add;
            Link = new LinkClient(Network.CreateEndpoint(), Clock, LinkRole.Phone, Registry) { Device = "rehearsal", Build = "e2e" };
            Link.SetSession(Session);
            Pump = new LinkPump(Link, Events) { Kernel = Kernel, Commands = Commands };
            Link.Welcomed += welcome =>
            {
                if (welcome.DemoLock) Registry.Set(TelemetryInstaller.ParamDemoLock, 1f, ParamSource.Remote);
            };
            Registry.DeclareBool(TelemetryInstaller.ParamDemoLock, "app", false, on => Link.DemoLock = on);
            Registry.DeclareInt(TelemetryInstaller.ParamStateHz, "link", 10, 60, 60, hz => Link.StateHz = hz);
            Loop.SetStateSink(Pump);
            Loop.AddAfterFrame(Pump);
            Lab.Events = Events;

            // What follows the room: the pose pipeline and its depth, Bump Guard's map, the link's outline.
            Lab.RoomChanged += room =>
            {
                Depth.Room = room;
                Pipeline.Room = room;
            };
            Lab.RoomChanged += Guard.SetRoom;
            Lab.RoomChanged += Pump.SetRoom;

            HandOver = new RoomHandOver(Lab, Loop) { Events = Events };

            // 20  The tail of the frame after lane D's hook.
            Loop.AddAfterFrame(Ladder);
            Loop.AddAfterFrame(HandOver);
            Loop.AddAfterFrame(Lab);

            Registry.PushAll();
            space.Enabled = true;
            Loop.Seal();
        }

        public void Dispose()
        {
            Lab.Shutdown();
            Pump.Stop();
            Spectator.Stop();
            Hub.Stop();
            Guard.Dispose();
            BumpCue.Shutdown();
        }

        // ---- the frame ------------------------------------------------------------------------------------

        public double Time { get { return Frames * FrameSeconds; } }
        public ref readonly FrameContext Frame { get { return ref Loop.Current; } }
        public float2 HeadFloor { get { return Loop.Current.RawHeadPosition.xz; } }

        /// <summary>One frame of the phone: steps 1 to 6, then 8a and 8b. Nothing of the Station runs here.</summary>
        public void PhoneFrame()
        {
            Frames++;
            Clock.NowUs = StartUs + (long)Math.Round(Frames * FrameSeconds * 1000000.0);
            Loop.RunUpdate(Frames * FrameSeconds, Dt);
            Loop.RunState();
            Loop.RunAfterFrame();

            BumpLevel level = Guard.Level;
            if (level != _lastLevel)
            {
                BumpChanges.Add(new BumpChange { Frame = Frames, Time = Time, From = _lastLevel, To = level, Floor = HeadFloor });
                _lastLevel = level;
            }
        }

        /// <summary>The Hub's loop and the Spectator's, as they run within a millisecond of the phone's send.</summary>
        public void StationPoll()
        {
            Hub.Poll();
            Spectator.Poll();
            Hub.Poll();
        }

        public void Step()
        {
            PhoneFrame();
            StationPoll();
        }

        public void Steps(int frames) { for (int i = 0; i < frames; i++) Step(); }

        /// <summary>Steps until the condition holds. Returns the frames it took, or -1 after the limit.</summary>
        public int StepUntil(Func<bool> condition, int maxFrames)
        {
            for (int i = 0; i <= maxFrames; i++)
            {
                if (condition()) return i;
                Step();
            }
            return -1;
        }

        // ---- the flow -------------------------------------------------------------------------------------

        /// <summary>Starts the Hub, the Spectator and the phone's link.</summary>
        public void StartStation()
        {
            Hub.Start(Port);
            Spectator.Start("10.0.0.2", Port);
            Pump.Start("10.0.0.2", Port);
        }

        public bool LinkUp { get { return Link.IsReady && Link.State == LinkState.Connected && Spectator.IsReady; } }

        /// <summary>The head stands at a floor point, looking along +Z turned by yaw.</summary>
        public void StandAt(float2 floor, float yaw = 0f)
        {
            Standing.Position = new float3(floor.x, EyeHeight, floor.y);
            Standing.Rotation = quaternion.RotateY(yaw);
            Standing.Tracking = TrackingState.Normal;
            Pipeline.Source = Standing;
        }

        public void StandAt(in HeadPose pose)
        {
            Standing.Position = pose.Position;
            Standing.Rotation = pose.Rotation;
            Standing.Tracking = TrackingState.Normal;
            Pipeline.Source = Standing;
        }

        /// <summary>
        /// The room hand-over as the scan or a fixture makes it, then "headset on": Bump Guard is armed.
        /// Steps until the entry space runs. Returns the frames it took.
        /// </summary>
        public int ActivateRoom(RoomModel room)
        {
            HandOver.Activate(room);
            Guard.Armed = true;
            return StepUntil(() => Lab.State == SpaceHostState.Running && Lab.HasSpace, 240);
        }

        /// <summary>Plays a recorded walk from the next frame, one sample per frame.</summary>
        public PoseLogReplaySource Play(PoseLog log)
        {
            var replay = new PoseLogReplaySource(log, ReplayMode.EveryFrame);
            Pipeline.Source = replay;
            return replay;
        }

        public T Module<T>() where T : class, ISpaceModule { return Lab.Module as T; }

        /// <summary>Where the kernel holds the walker, as a floor point of the room frame. Metres.</summary>
        public double2 WalkerFloor
        {
            get
            {
                WalkerPose pose = Kernel.Pose;
                long i, j;
                FlatTileGraph.TryGetCell(pose.Tile, out i, out j);
                double a = ((FlatTileGraph)Kernel.Graph).CentreSpacing;
                return new double2(i * a + pose.Local.M02, j * a + pose.Local.M12) * Kernel.MetresPerUnit;
            }
        }

        void OnHubState(int peerId, in StateMessage state, byte[] raw, int offset, int length)
        {
            HubState = state;
            HubStates++;
        }

        void OnSpectatorState(in StateMessage state)
        {
            SpectatorState = state;
            SpectatorStates++;
        }

        public bool HasEvent(string ev)
        {
            for (int i = 0; i < Logged.Count; i++) if (Logged[i].Ev == ev) return true;
            return false;
        }

        // ---- walks ----------------------------------------------------------------------------------------

        /// <summary>/recordings in the repository: beside the Unity project folder.</summary>
        public static string RecordingsDirectory()
        {
            string project = Environment.GetEnvironmentVariable(RoomFixtures.ProjectDirVariable);
            if (string.IsNullOrEmpty(project)) project = Directory.GetCurrentDirectory();
            return Path.GetFullPath(Path.Combine(project, "..", "recordings"));
        }

        public static PoseLog LoadWalk(string name)
        {
            string path = Path.Combine(RecordingsDirectory(), name + ".poselog");
            using (StreamReader reader = File.OpenText(path)) return PoseLog.Read(reader);
        }

        /// <summary>A straight walk at a steady speed, 60 samples a second, looking where it goes.</summary>
        public static PoseLog Line(string roomId, float2 from, float2 to, float speed)
        {
            float length = math.distance(from, to);
            int count = (int)math.ceil(length / speed * 60f) + 1;
            float2 direction = math.normalizesafe(to - from, new float2(0f, 1f));
            quaternion rotation = quaternion.RotateY(math.atan2(direction.x, direction.y));
            var samples = new HeadPose[count];
            for (int k = 0; k < count; k++)
            {
                float2 p = from + direction * math.min(length, speed * k / 60f);
                samples[k] = new HeadPose(k / 60.0, new float3(p.x, EyeHeight, p.y), rotation, TrackingState.Normal);
            }
            return new PoseLog { RoomId = roomId ?? "", Frame = PoseFrameKind.Room, Samples = samples };
        }

        /// <summary>One lap of a room's lap square at 0.5 m/s from the corner (-s/2, -s/2), along +Z first.</summary>
        public static PoseLog Lap(RoomModel room)
        {
            float h = 0.5f * room.LapSide;
            float2[] corners = { new float2(-h, -h), new float2(-h, h), new float2(h, h), new float2(h, -h) };
            var samples = new List<HeadPose>(1024);
            for (int side = 0; side < 4; side++)
            {
                PoseLog leg = Line(room.Id, corners[side], corners[(side + 1) % 4], 0.5f);
                // The corner sample belongs to the next side, which looks along it.
                int take = side == 3 ? leg.Samples.Length : leg.Samples.Length - 1;
                for (int k = 0; k < take; k++)
                {
                    HeadPose s = leg.Samples[k];
                    samples.Add(new HeadPose(samples.Count / 60.0, s.Position, s.Rotation, s.Tracking));
                }
            }
            return new PoseLog { RoomId = room.Id, Frame = PoseFrameKind.Room, Samples = samples.ToArray() };
        }
    }
}
