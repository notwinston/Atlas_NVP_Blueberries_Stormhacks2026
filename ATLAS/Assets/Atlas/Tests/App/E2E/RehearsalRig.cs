using System;
using System.Collections.Generic;
using System.IO;
using Atlas.App;
using Atlas.Hands;
using Atlas.Input;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Safety;
using Atlas.Scan;
using Atlas.Spaces.AlignedCells;
using Atlas.Spaces.CurvedWalk;
using Atlas.Spaces.Glued;
using Atlas.Spaces.Home;
using Atlas.Spaces.Portal;
using Atlas.Spaces.Slicer;
using Atlas.Spaces.SmallPlanet;
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
    /// the order of the composition root (AtlasApp.Build): registry, kernel, frame loop, input, pose pipeline,
    /// tracked hands, Bump Guard, quality ladder, SpaceLab, parameters, operator commands, link, room hand-over,
    /// then the tail of the frame. Everything that has an engine-free core is that core; only the pose
    /// pipeline's adapter, the input service's and the seven space adapters are replaced by the engine-free
    /// stand-ins of this folder. The Station side is a real LinkHost (the Hub's link) and a Spectator client
    /// behind it.
    /// <para>The tracked hands (documents/ATLAS_Hands_Design.md) are the app's own runtime with no view and no
    /// sounds, on a detector the tests script. They are off until hands.enabled is set, as in the app, and a
    /// rehearsal that leaves the parameter alone is the rehearsal there was before they existed.</para>
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
        public readonly RehearsalInput Input = new RehearsalInput();
        public readonly RehearsalPosePipeline Pipeline = new RehearsalPosePipeline();
        public readonly SyntheticDepthSource Depth = new SyntheticDepthSource();
        public readonly StandingPoseSource Standing = new StandingPoseSource();
        /// <summary>The walker's tracked hands; null in a rig built without them.</summary>
        public readonly HandsRuntime Hands;
        /// <summary>Where the hands' detections come from: <see cref="Script"/>, or the source the rig was given.</summary>
        public readonly IHandsSource HandsSource;
        /// <summary>The scripted detector; null when the rig was given another source, or has no hands.</summary>
        public readonly ScriptedHandsSource Script;
        public readonly BumpGuard Guard = new BumpGuard(false);
        /// <summary>Step 2 of the frame: Bump Guard, handed the hands' depth mask when the rig has hands.</summary>
        public readonly BumpGuardStep GuardStep;
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
        /// <summary>The events that reached the Hub over the link: what its event log holds.</summary>
        public readonly List<EventMessage> HubEvents = new List<EventMessage>(64);
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
        // The walker's raised hands, in the camera's space, as LiDAR is shown them.
        readonly JointSet[] _raised = new JointSet[2];
        readonly bool[] _isRaised = new bool[2];

        /// <param name="hands">False builds the phone as it was before the tracked hands: no hands step, no mask at Bump Guard, no hands.* parameter.</param>
        /// <param name="handsSource">The detector; null for the scripted one (<see cref="Script"/>).</param>
        public RehearsalRig(bool hands = true, IHandsSource handsSource = null)
        {
            BumpCue.Shutdown();     // the cue is static: nothing of an earlier rehearsal may show

            Network = new LoopbackNetwork(Clock);
            Hub = new LinkHost(Network.CreateEndpoint(), Clock) { SessionId = "hub-session", ForgeUrl = "http://10.0.0.2:8000" };
            Spectator = new LinkClient(Network.CreateEndpoint(), Clock, LinkRole.Spectator) { Device = "spectator" };
            _onHubState = OnHubState;
            _onSpectatorState = OnSpectatorState;
            Hub.StateReceived += _onHubState;
            Hub.RoomOutlineReceived += (peer, outline) => HubOutline = outline;
            Hub.JsonReceived += OnHubJson;
            Spectator.StateReceived += _onSpectatorState;

            // 8, 9  Kernel and frame loop.
            Loop = new FrameLoopCore(Kernel) { Warning = Warnings.Add };

            // 10  Input: the field above. Its sense step is added after the pose pipeline's and the hands'.

            // 11  The pose pipeline: step 1, then step 3. The depth is the Editor's ray-cast image (F9).
            Pipeline.Source = Standing;
            Pipeline.Depth = Depth;
            Loop.AddSense(Pipeline);
            Loop.SetPoseFilter(Pipeline);
            Loop.Pipeline = Pipeline;
            // Tracked hands, where the composition root installs them: their sense step after the pose
            // pipeline's and before input's. HandsInstaller itself is not used: it takes the detector from the
            // platform's slot and the event log and the space host from AtlasServices, and publishes the hands
            // there, all of it static, which a rehearsal that may stand beside another does not touch. What it
            // hands the runtime is handed to it here from the rehearsal's own parts, by the same rules; Bump
            // Guard, the event log and the space host are built further down and are reached lazily, as there.
            if (hands)
            {
                if (handsSource == null) handsSource = Script = new ScriptedHandsSource(Pipeline);
                HandsSource = handsSource;
                Hands = new HandsRuntime(new HandsDeps
                {
                    Source = handsSource,
                    Input = Input,
                    Pipeline = Pipeline,
                    Headset = () => InHeadset,
                    Warning = () => Guard.Level != BumpLevel.Clear,
                    HeadFromCamera = () => Pipeline.Source != null && Pipeline.Source.DrivesRig,
                    Events = () => Events,
                    Host = () => Lab,
                    MaskFaults = () => GuardStep != null ? GuardStep.MaskFaults : 0,
                });
                Loop.AddSense(new HandsSenseStep(Hands));
            }
            Loop.AddSense(new InputSenseStep(Input, Pipeline));

            // 12  Bump Guard: step 2 of every frame from the first, with the hands' depth mask when there are hands.
            GuardStep = new BumpGuardStep(Guard, Pipeline, Hands);
            Loop.SetGuard(GuardStep);

            // 15  The quality ladder. Off the device there is no thermal plugin.
            Ladder = new QualityLadder(new QualityApplier(null));
            Ladder.Applier.ThermalOverride = 0;
            Loop.Quality = Ladder;

            // 16  SpaceLab.
            Lab = new SpaceLab(Registry, Kernel, Loop, Loader, Stage);
            Loader.Bind(Lab, Kernel, Loop);
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
            CurvedWalkCore.DeclareParams(Registry);
            AlignedCellsCore.DeclareParams(Registry);
            Registry.Find(AlignedCellsCore.ParamMaze).Set(false);      // classic S3: these walks are not duck-maze runs
            SmallPlanetCore.DeclareParams(Registry);

            // 17  Operator commands as local calls.
            Commands = new CommandRouter(Lab, Loop, Registry, Pipeline);

            // 18  Link and event log, as the telemetry installer makes them.
            Events = new EventLog(Clock, null);
            Events.Logged += Logged.Add;
            Link = new LinkClient(Network.CreateEndpoint(), Clock, LinkRole.Phone, Registry) { Device = "rehearsal", Build = "e2e" };
            Link.SetSession(Session);
            Pump = new LinkPump(Link, Events) { Kernel = Kernel, Commands = Commands, Spaces = Lab };
            Loader.Events = Events;
            Link.Welcomed += welcome =>
            {
                if (welcome.DemoLock) Registry.Set(TelemetryInstaller.ParamDemoLock, 1f, ParamSource.Remote);
            };
            Registry.DeclareBool(TelemetryInstaller.ParamDemoLock, "app", false, on => Link.DemoLock = on);
            Registry.DeclareInt(TelemetryInstaller.ParamStateHz, "link", 10, 60, 60, hz => Link.StateHz = hz);
            Loop.SetStateSink(Pump);
            Loop.AddAfterFrame(Pump);
            Lab.Events = Events;
            // The hands' parameters last, so that no other parameter changes its wire id.
            if (Hands != null) DeclareHandsParams();

            // What follows the room: the pose pipeline and its depth, Bump Guard's map, the link's outline.
            Lab.RoomChanged += room =>
            {
                Depth.Room = room;
                Pipeline.Room = room;
            };
            Lab.RoomChanged += Guard.SetRoom;
            Lab.RoomChanged += Pump.SetRoom;

            HandOver = new RoomHandOver(Lab, Loop) { Events = Events };

            // 20  The tail of the frame after lane D's hook: the hands' edge events first, as in the app.
            if (Hands != null) Loop.AddAfterFrame(new RehearsalHandsAfterFrame(Hands));
            Loop.AddAfterFrame(Ladder);
            Loop.AddAfterFrame(HandOver);
            Loop.AddAfterFrame(Lab);

            Registry.PushAll();
            space.Enabled = true;
            Loop.Seal();
        }

        // HandsSystem.DeclareParams gives the hands' parameters their names, groups, kinds, ranges, defaults and
        // order, so the rehearsal's cannot differ from the app's. Its callbacks drive that system's own runtime,
        // which only HandsInstaller can give it (the setter is internal). So each parameter is declared once
        // more as it stands, with a callback to the rehearsal's runtime: a second declaration keeps the entry
        // and its wire id and takes the new callback.
        void DeclareHandsParams()
        {
            int first = Registry.Count;
            new HandsSystem().DeclareParams(Registry);
            HandsRuntime runtime = Hands;
            for (int i = first; i < Registry.Count; i++)
            {
                ParamEntry entry = Registry[i];
                switch (entry.Name)
                {
                    case HandsSystem.ParamEnabled: Rebind(entry, onBool: on => runtime.Enabled = on); break;
                    case HandsSystem.ParamRate: Rebind(entry, onInt: index => runtime.RateOverride = index == 1 ? 30 : index == 2 ? 15 : 0); break;
                    case HandsSystem.ParamReach: Rebind(entry, onFloat: metres => runtime.Reach = metres); break;
                    case HandsSystem.ParamSound: Rebind(entry, onBool: on => runtime.Sound = on); break;
                    case HandsSystem.ParamDraw: Rebind(entry, onBool: on => runtime.Draw = on); break;
                    case HandsSystem.ParamBench: Rebind(entry, onBool: on => runtime.Bench = on); break;
                    case HandsSystem.ParamRecord: Rebind(entry, onBool: on => runtime.Record = on); break;
                    // hands.editor: the Editor's mouse-and-keys source is not in the rehearsal. And any parameter the
                    // app gains that is not bound here yet. Both are there, and drive nothing;
                    // E2E_Hands_ParametersAreTheAppsOwn names the second kind.
                    default: Rebind(entry); break;
                }
            }
        }

        // The entry as it stands, with the callback given for its kind; with none, it drives nothing.
        void Rebind(ParamEntry entry, Action<bool> onBool = null, Action<int> onInt = null, Action<float> onFloat = null)
        {
            switch (entry.Kind)
            {
                case ParamKind.Bool:
                    Registry.DeclareBool(entry.Name, entry.Group, entry.Default >= 0.5f, onBool, entry.OnRelease);
                    break;
                case ParamKind.Float:
                    Registry.DeclareFloat(entry.Name, entry.Group, entry.Min, entry.Max, entry.Default, onFloat, entry.OnRelease);
                    break;
                case ParamKind.Int:
                    Registry.DeclareInt(entry.Name, entry.Group, (int)entry.Min, (int)entry.Max, (int)entry.Default, onInt, entry.OnRelease);
                    break;
                default:
                    var names = new string[entry.EnumCount];
                    for (int k = 0; k < names.Length; k++) names[k] = entry.EnumName(k);
                    Registry.DeclareEnum(entry.Name, entry.Group, names, (int)entry.Default, onInt, entry.OnRelease);
                    break;
            }
        }

        public void Dispose()
        {
            Lab.Shutdown();
            Pump.Stop();
            if (Hands != null) Hands.Shutdown(Time);
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
            EnterHeadset();
            return StepUntil(() => Lab.State == SpaceHostState.Running && Lab.HasSpace, 240);
        }

        /// <summary>The walker has the headset on: the display mode the hands' gate asks for.</summary>
        public bool InHeadset { get; private set; }

        /// <summary>"Headset on", as RehearsalFlow.EnterHeadset does it: Bump Guard is armed, the display is the headset's, gaze input is live.</summary>
        public void EnterHeadset()
        {
            Guard.Armed = true;
            if (InHeadset) return;
            InHeadset = true;
            Input.PushContext(InputContext.Space);
        }

        /// <summary>
        /// The head is the phone's AR camera, as on the device, and not a replay's: the pose sources of the
        /// rehearsal then say that they drive the rig, which is what the hands' gate asks (decision R8).
        /// False by default; a rehearsal of the hands sets it before it stands or walks.
        /// </summary>
        public bool CameraHead
        {
            get { return Standing.Camera; }
            set { Standing.Camera = value; }
        }

        /// <summary>Plays a recorded walk from the next frame, one sample per frame.</summary>
        public PoseLogReplaySource Play(PoseLog log)
        {
            var replay = new PoseLogReplaySource(log, ReplayMode.EveryFrame);
            if (CameraHead) Pipeline.Source = new CameraPoseSource(replay);
            else Pipeline.Source = replay;
            return replay;
        }

        // ---- the walker's hands -----------------------------------------------------------------------------

        /// <summary>Half the thickness of a hand through the palm and of a finger sideways, and the forearm LiDAR sees below a raised hand. Metres.</summary>
        const float HandHalfThickness = 0.012f, FingerHalfWidth = 0.010f, ForearmLength = 0.25f, ForearmHalfWidth = 0.03f;

        /// <summary>
        /// The walker holds a hand before the camera from the next frame on: its 21 joints in the camera's own
        /// space (+X right, +Y up, +Z forward). The detector is shown the joints and LiDAR the hand itself: the
        /// palm with its four fingers, the thumb, and the forearm from the wrist away from the fingers, each as
        /// a box in the synthetic depth image. The hand is in the depth image whether or not the app tracks it.
        /// </summary>
        public void ShowHand(HandSide side, in JointSet cameraJoints)
        {
            if (Script != null) Script.Show(side, in cameraJoints);
            _raised[(int)side] = cameraJoints;
            _isRaised[(int)side] = true;
            PlaceHandsInDepth();
        }

        /// <summary>The hand goes out of the camera's view from the next frame on.</summary>
        public void HideHand(HandSide side)
        {
            if (Script != null) Script.Hide(side);
            _isRaised[(int)side] = false;
            PlaceHandsInDepth();
        }

        void PlaceHandsInDepth()
        {
            Depth.ClearCameraBoxes();
            for (int side = 0; side < 2; side++)
            {
                if (!_isRaised[side]) continue;
                ref JointSet joints = ref _raised[side];
                float3 wrist = joints[HandJoint.Wrist];
                // The palm and the four fingers; the thumb, which stands out to one side.
                float3 low = wrist, high = wrist;
                for (int joint = (int)HandJoint.IndexMcp; joint < HandJoints.Count; joint++) Grow(ref low, ref high, joints[joint]);
                AddHandBox(low, high, FingerHalfWidth);
                low = high = joints[HandJoint.ThumbCmc];
                for (int joint = (int)HandJoint.ThumbMcp; joint <= (int)HandJoint.ThumbTip; joint++) Grow(ref low, ref high, joints[joint]);
                AddHandBox(low, high, FingerHalfWidth);
                // The forearm, from the wrist away from the fingers.
                float3 away = math.normalizesafe(wrist - joints[HandJoint.MiddleMcp], new float3(0f, -1f, 0f));
                low = high = wrist;
                Grow(ref low, ref high, wrist + away * ForearmLength);
                AddHandBox(low, high, ForearmHalfWidth);
            }
        }

        static void Grow(ref float3 low, ref float3 high, float3 point)
        {
            low = math.min(low, point);
            high = math.max(high, point);
        }

        void AddHandBox(float3 low, float3 high, float halfWidth)
        {
            float3 pad = new float3(halfWidth, halfWidth, math.max(halfWidth, HandHalfThickness));
            Depth.AddCameraBox(0.5f * (low + high), high - low + 2f * pad);
        }

        public T Module<T>() where T : class, ISpaceModule { return Lab.Module as T; }

        /// <summary>Where the kernel holds the walker, as a floor point of the room frame. Metres. Flat kernel only.</summary>
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

        // The Hub writes every event it receives to its log; here the log is a list.
        void OnHubJson(int peerId, byte messageId, JsonValue body)
        {
            if (messageId != MessageId.Event) return;
            var message = new EventMessage();
            message.FromJson(body.Clone());
            HubEvents.Add(message);
        }

        public bool HasEvent(string ev)
        {
            for (int i = 0; i < Logged.Count; i++) if (Logged[i].Ev == ev) return true;
            return false;
        }

        /// <summary>How many events of a name are in a log: the phone's (<see cref="Logged"/>) or the Hub's (<see cref="HubEvents"/>).</summary>
        public static int CountOf(List<EventMessage> log, string ev)
        {
            int count = 0;
            for (int i = 0; i < log.Count; i++) if (log[i].Ev == ev) count++;
            return count;
        }

        /// <summary>The newest event of a name in a log, or null.</summary>
        public static EventMessage LastOf(List<EventMessage> log, string ev)
        {
            for (int i = log.Count - 1; i >= 0; i--) if (log[i].Ev == ev) return log[i];
            return null;
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
