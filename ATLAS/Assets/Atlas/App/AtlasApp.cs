using System;
using System.Collections.Generic;
using System.IO;
using Atlas.Hands;
using Atlas.Input;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Rendering.Room;
using Atlas.Safety;
using Atlas.Scan;
using Atlas.Telemetry;
using Atlas.World;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;

namespace Atlas.App
{
    /// <summary>The three objects of the boot scene the composition root starts from.</summary>
    public struct BootScene
    {
        public ARSession Session;
        public XROrigin Origin;
        public Camera Camera;

        /// <summary>
        /// The one scene lookup of the app (contract README F3): the AR Session and the XR Origin of the boot
        /// scene, and the origin's camera. What is absent (a sandbox scene, a test) is created in code: an
        /// origin with a camera always, a session only where an XR provider is present.
        /// </summary>
        public static BootScene FindOrCreate()
        {
            // Only the root objects of the loaded scene are looked at: the boot scene holds the session and
            // the origin there and nothing else.
            var scene = new BootScene();
            var roots = new List<GameObject>(8);
            SceneManager.GetActiveScene().GetRootGameObjects(roots);
            for (int i = 0; i < roots.Count; i++)
            {
                if (scene.Session == null) scene.Session = roots[i].GetComponentInChildren<ARSession>(true);
                if (scene.Origin == null) scene.Origin = roots[i].GetComponentInChildren<XROrigin>(true);
            }

            if (scene.Origin == null)
            {
                // Built inactive, so the origin wakes up with its camera in place.
                var originObject = new GameObject("XR Origin");
                originObject.SetActive(false);
                var offset = new GameObject("Camera Offset");
                offset.transform.SetParent(originObject.transform, false);
                var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
                cameraObject.transform.SetParent(offset.transform, false);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.nearClipPlane = StereoRig.NearPlane;
                camera.farClipPlane = StereoRig.FarPlane;
                // The head is the app's one listener: the Bump Guard cue is placed in space around it. A scene
                // that has no origin may still bring a listener of its own (the Editor's default Main Camera);
                // Unity allows one, so that one is switched off.
                cameraObject.AddComponent<AudioListener>();
                for (int i = 0; i < roots.Count; i++)
                {
                    AudioListener[] others = roots[i].GetComponentsInChildren<AudioListener>(true);
                    for (int k = 0; k < others.Length; k++) others[k].enabled = false;
                }
                scene.Origin = originObject.AddComponent<XROrigin>();
                scene.Origin.CameraFloorOffsetObject = offset;
                scene.Origin.Camera = camera;
                originObject.SetActive(true);
            }
            scene.Camera = scene.Origin.Camera;
            if (scene.Camera == null)
            {
                scene.Camera = scene.Origin.GetComponentInChildren<Camera>(true);
                scene.Origin.Camera = scene.Camera;
            }

            if (scene.Session == null && ArBackend.Detect() != XrBackend.None)
                scene.Session = new GameObject("AR Session").AddComponent<ARSession>();
            return scene;
        }
    }

    /// <summary>
    /// Everything the composition root built, in one place (contract README F3). Created by AtlasBootstrap;
    /// published through AtlasServices for the folders that are composed later (menu, onboarding, perf tour).
    /// </summary>
    public sealed class AtlasApp
    {
        /// <summary>Material instances created up front (lane A contract, section 11).</summary>
        public const int PoolCapacity = 256;

        // Layers that the tracked camera must not draw in the hand: the head-locked layer sits a metre in
        // front of the lens and would cover the scan picture (lane A contract, section 12.1).
        const int PortalMaskLayer = 6, HudLayer = 7, SpectatorLayer = 23;

        bool _shutDown;
        /// <summary>Step 2 of the frame; kept so the hands can report a depth mask that threw.</summary>
        BumpGuardStep _guardStep;
        AppParams.SpaceBinding _spaceParam;

        public AtlasBootContext Boot { get; private set; }
        public BootOptions Options { get; private set; }
        public ParamRegistry Registry { get; private set; }
        public MaterialPool Pool { get; private set; }
        public LensModel Lens { get; private set; }
        /// <summary>The rig's own component. Change the display mode through <see cref="Display"/>.</summary>
        public StereoRig Rig { get; private set; }
        /// <summary>
        /// The rig as published in AtlasServices: SetMode(Headset) is refused on a phone with no Bump Guard.
        /// </summary>
        public GuardedStereoRig Display { get; private set; }
        public EyeTextureSet Eyes { get; private set; }
        public LensBlit Blit { get; private set; }
        public AtlasFrameLoop Loop { get; private set; }
        public FrameLoopCore LoopCore { get; private set; }
        public IWalkerKernel WalkerKernel { get; private set; }
        public IAtlasInput InputService { get; private set; }
        public ArPoseSystems Pose { get; private set; }
        public ScanSystems ScanSystems { get; private set; }
        public ScanSettings ScanSettings { get; private set; }
        public BumpGuard Guard { get; private set; }
        /// <summary>False when Bump Guard could not be created: the headset view is refused on a phone.</summary>
        public bool GuardInstalled { get; private set; }
        public RoomContent RoomContent { get; private set; }
        public QualityLadder Ladder { get; private set; }
        public FurnitureLevelPolicy Furniture { get; private set; }
        public SpaceLab Spaces { get; private set; }
        /// <summary>The hexagon reveal between two spaces; null when its shader is missing (spaces fade).</summary>
        public HexTransition Transition { get; private set; }
        public CommandRouter Commands { get; private set; }
        /// <summary>The walk input of the Station's Map page: the router writes it, the input step feeds it to the walker.</summary>
        public RemoteWalkInput RemoteWalk { get; private set; }
        /// <summary>Sends what the phone shows to the Station's View page while the Hub asks for it.</summary>
        public ViewStreamer ViewStream { get; private set; }
        /// <summary>Return home, next and previous space and the space menu, from the gamepad or the gaze.</summary>
        public SpaceActions SpaceActions { get; private set; }
        /// <summary>The gaze twins of the four SpaceLab actions: home beacon, previous, next and exhibit sign.</summary>
        public SpaceSigns Signs { get; private set; }
        public AtlasTelemetry TelemetrySystems { get; private set; }
        public AssetClient Assets { get; private set; }
        public RoomHandOver HandOver { get; private set; }
        public PoseSourceSwitch SourceSwitch { get; private set; }
        public RehearsalFlow Flow { get; private set; }
        public DevOverlay Overlay { get; private set; }
        public IOnboardingFlow Onboarding { get; private set; }
        public IAppMenu Menu { get; private set; }
        public IPerfTour PerfTour { get; private set; }
        /// <summary>The walker's tracked hands (documents/ATLAS_Hands_Design.md). Never null; off until hands.enabled is set.</summary>
        public HandsSystem Hands { get; private set; }

        AtlasApp() { }

        /// <summary>
        /// Builds the app in the binding order of lane A's contract, section 8.2, publishes each system
        /// through AtlasServices, and ends with AtlasServices.MarkBooted. The frame loop runs from the next
        /// Update.
        /// </summary>
        public static AtlasApp Create(BootScene scene, BootOptions options)
        {
            var app = new AtlasApp { Options = options ?? new BootOptions() };
            app.Build(scene);
            return app;
        }

        void Build(BootScene scene)
        {
            // 1  Services, the Atlas objects, the boot context.
            AtlasServices.Clear();
            var root = new GameObject("Atlas");
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(root);
            var boot = new AtlasBootContext
            {
                Root = root.transform,
                Systems = Child(root.transform, "Systems"),
                RoomRoot = Child(root.transform, "Room"),
                SpaceRoot = Child(root.transform, "Space"),
                XrOrigin = scene.Origin != null ? scene.Origin.transform : null,
                TrackedCamera = scene.Camera != null ? scene.Camera.transform : null,
                IsSpectator = false,
                IsDevice = Application.isMobilePlatform && !Application.isEditor,
            };
            Boot = boot;
            AtlasServices.Register(this);

            // 2  The parameter registry: the one instance.
            Registry = new ParamRegistry();
            boot.Params = Registry;
            AtlasServices.Register(Registry);

            // 3, 4  The look and the material pool.
            AtlasLook.Initialise();
            Pool = MaterialPool.CreateDefault(PoolCapacity);
            AtlasServices.Register(Pool);

            // 5  The stereo rig under the tracked head.
            Lens = new LensModel();
            Lens.SetScreenFromPixels(Screen.width, Screen.height, Screen.dpi);
            Rig = StereoRig.Create(boot.TrackedCamera, Lens);
            boot.Head = Rig.Head;
            boot.Hud = Rig.Hud;
            if (scene.Camera != null)
                scene.Camera.cullingMask &= ~((1 << HudLayer) | (1 << SpectatorLayer) | (1 << PortalMaskLayer));
            // Published behind the headset refusal; locked at step 12 if Bump Guard cannot be created.
            Display = new GuardedStereoRig(Rig) { Refused = OnHeadsetRefused };
            Rig.ModeChanged += OnRigModeChanged;
            AtlasServices.Register<IStereoRig>(Display);
            // The rig itself too, for its eye-target override (FieldFit). The display mode still goes through
            // IStereoRig, which is where the headset refusal is enforced.
            AtlasServices.Register(Rig);

            // 6  Eye textures and the lens blit to the screen.
            Eyes = new EyeTextureSet(Screen.width, Screen.height);
            Eyes.Bind(Rig.GetEyeCamera(Eye.Left), Rig.GetEyeCamera(Eye.Right));
            Blit = LensBlit.Create(root.transform, Rig, Eyes, Lens);

            // 8, 9  The kernel and the frame loop, disabled until the end. One walker for every dial position:
            //       flat until a curved space configures it, and flat again when SpaceLab leaves that space.
            var kernel = new HolonomyWalkerKernel();
            WalkerKernel = kernel;
            AtlasServices.Register<IWalkerKernel>(kernel);
            AtlasServices.Register<IDialKernel>(kernel);
            LoopCore = new FrameLoopCore(WalkerKernel)
            {
                Eyes = new RigEyePositions(Rig),
                Warning = message => Debug.LogWarning("Atlas: " + message),
            };
            Loop = AtlasFrameLoop.Create(boot.Systems, LoopCore);
            AtlasServices.Register<IFrameLoop>(Loop);

            // 10  Input. Its sense step is added after the pose pipeline's, so the gaze sees this frame's head.
            InputService = AtlasInput.Install(boot.Hud, HeadLockedMaterial());
            AtlasServices.Register(InputService);

            // 11  AR session set-up, pose sources and pipeline, scan session and room store.
            string logs = Path.Combine(Application.persistentDataPath, "logs");
            Pose = ArPoseSystems.Create(boot, scene.Session, logs);
            Pose.Publish(Loop);
            LoopCore.Pipeline = Pose.Pipeline;
            ScanSystems = ScanSystems.Create(Pose);
            ScanSystems.Publish(Loop);
            ScanSettings = new ScanSettings();
            RemoteWalk = new RemoteWalkInput();
            // Tracked hands. Their sense step runs after the pose pipeline's and before input's, so a touch or a
            // pinch reaches this frame's input snapshot. Bump Guard, the event log and the space host are built
            // later and are reached lazily (documents/ATLAS_Hands_Design.md section 3.7).
            ArSessionRig handsRig = Pose.Rig;
            Hands = HandsInstaller.Install(new HandsPorts
            {
                Parent = boot.Systems,
                Head = boot.Head,
                Pool = Pool,
                Rig = Display,
                Input = InputService,
                Pipeline = Pose.Pipeline,
                TrackedCamera = boot.TrackedCamera,
                SessionSpace = () => handsRig != null && handsRig.Origin != null ? handsRig.Origin.TrackablesParent : null,
                NativeSession = () => handsRig != null && handsRig.HasAr && handsRig.Session != null && handsRig.Session.subsystem != null
                    ? handsRig.Session.subsystem.nativePtr : IntPtr.Zero,
                Warning = () => Guard != null && Guard.Level != BumpLevel.Clear,
                IsDevice = boot.IsDevice,
                RecordFolder = logs,
                MaskFaults = () => _guardStep != null ? _guardStep.MaskFaults : 0,
            });
            Loop.AddSense(Hands.Sense);
            Loop.AddSense(new InputSenseStep(InputService, Pose.Pipeline, RemoteWalk));

            // 12  Bump Guard and its cue: step 2 of every frame from the first.
            try
            {
                Guard = SafetyInstaller.Install(boot, HeadLockedMaterial());
                // The guard step hands Bump Guard the depth mask of the walker's own hands (section 7.1).
                _guardStep = new BumpGuardStep(Guard, Pose.Pipeline, Hands.Mask);
                Loop.SetGuard(_guardStep);
                // clear(p) of section 4.10 for the code that plans a walk round the room's obstacles.
                AtlasServices.Register<IClearanceQuery>(Guard);
                GuardInstalled = true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (boot.IsDevice) Debug.LogError("Atlas: Bump Guard missing. The headset view is refused.");
                else Debug.LogWarning("Atlas: Bump Guard missing.");
                Display.HeadsetLocked = HeadsetGate.MustWarn(boot.IsDevice, false);
            }

            // 13  Room content.
            RoomContent = RoomContentInstaller.Install(boot, Pool);

            // 14  Upload hooks of the platform: the look's globals, then the eyes' prediction.
            Loop.AddUpload(new LookUploadStep(WalkerKernel));
            Loop.AddUpload(new RigUploadStep(Rig, Pose.Pipeline));
            Loop.AddUpload(Hands.Upload);

            // 15  The quality ladder.
            Ladder = new QualityLadder(new QualityApplier(Eyes));
            LoopCore.Quality = Ladder;
            AtlasServices.Register<IQualityState>(Ladder);
            Furniture = new FurnitureLevelPolicy(RoomContent, Ladder);

            // 16  SpaceLab. While no Home space is in the build, the built-in grid space stands in for it.
            if (!SpaceRegistry.IsRegistered(SpaceId.Home) &&
                !Application.CanStreamedLevelBeLoaded(SpaceIds.SceneName(SpaceId.Home)))
                SpaceRegistry.Register(SpaceId.Home, GridSpace.Create);
            Spaces = new SpaceLab(Registry, WalkerKernel, LoopCore, new SceneSpaceLoader(boot.SpaceRoot),
                new SpaceStage(RoomContent, Furniture));
            LoopCore.Space = Spaces;
            LoopCore.Host = Spaces;
            Transition = HexTransition.Create(Display);
            Spaces.Transition = Transition;
            // The hands stay live through a switch, so they are left out of the transition's stills.
            if (Transition != null)
                Transition.HideFromStill = hide => { HandsView view = Hands.View; if (view != null) view.SetHiddenForStill(hide); };
            AtlasServices.Register<ISpaceHost>(Spaces);

            // The parameters of the assemblies that may not name the registry, in the order of lane D's table.
            DeclareParams();
            SpaceRegistry.DeclareAllParams(Registry);

            // 17  Operator commands as local calls.
            Commands = new CommandRouter(Spaces, Loop, Registry, Pose.Pipeline, walk: RemoteWalk);
            AtlasServices.Register<IOperatorCommands>(Commands);
            // The View page: frames go to the Hub the link is talking to, read when each frame is posted.
            ViewStream = ViewStreamer.Create(boot.Systems, Rig, () =>
                TelemetrySystems != null && TelemetrySystems.Pump != null ? TelemetrySystems.Pump.Host : null);
            ViewStream.DeclareParams(Registry);
            ViewStreamer viewStream = ViewStream;
            Commands.ViewRequested = (on, port) => viewStream.Request(on, port);

            // 18  Link, perf probe, HUD, event log, pairing. The loop is published, so its hooks go in here:
            //     the state sink at 8a; probe, link driver and HUD at 8b.
            TelemetrySystems = TelemetryInstaller.Install(boot);
            // The row's visibleTiles: what the curved world's renderer drew, copied at step 6.
            if (TelemetrySystems != null && TelemetrySystems.Probe != null)
            {
                AtlasPerfProbe probe = TelemetrySystems.Probe;
                Loop.AddUpload(new TileStatsUploadStep(tiles => probe.VisibleTiles = tiles));
            }
            // The asset client: the photo sink of the scan, the Forge round trip and the built-in fallback set.
            // Null in the Spectator, which shows the room as the phone reports it.
            Assets = AssetClientInstaller.Install(boot, TelemetrySystems);
            // The scan's object photos go to it (lane C contract, section 14.1). With no asset client, to a
            // sink published some other way, else to the one that only counts: no crop leaves the phone.
            // The asset client serves each room with the Forge's round trip, or builds its objects on the phone when
            // forge.backend is off (the default; standalone-phone plan section 6.5): it chooses per room.
            IObjectPhotoSink photoSink = Assets;
            if (photoSink == null) AtlasServices.TryGet(out photoSink);
            ScanSystems.Photos.Sink = photoSink;
            if (Assets != null)
            {
                // The scanned LiDAR mesh goes to it too: unknown objects and poor matches are cut out of it (D8).
                ScanSystems.ScanMeshSink = Assets;
                // Once the props are placed, a kept room is written again with them.
                Assets.ObjectsSettled += OnObjectsSettled;
            }
            IEventLog events;
            AtlasServices.TryGet(out events);
            IPerfSource perf;
            if (AtlasServices.TryGet(out perf)) Ladder.Perf = perf;
            Spaces.Events = events;
            if (Transition != null) Transition.Events = events;

            // The room hand-over and the Editor-first path.
            PosePipeline pipeline = Pose.Pipeline;
            HandOver = new RoomHandOver(Spaces, LoopCore, anchor => pipeline.Anchor = anchor as Transform)
            {
                Events = events,
                EntrySpace = Options.EntrySpace,
            };
            AtlasServices.Register(HandOver);
            SourceSwitch.Events = events;
            Flow = new RehearsalFlow(ScanSystems.Session, HandOver, Display, Guard, InputService, ScanSettings)
            {
                Events = events,
                HeadsetAllowed = GuardInstalled || !boot.IsDevice,
                AutoHeadset = !boot.IsDevice && !Options.StayInHand,
            };
            SourceSwitch.ReplayStarted += OnReplayStarted;
            HandOver.Activated += OnRoomActivated;
            Overlay = DevOverlay.Create(boot.Systems, this);
            // A click on the overlay's buttons is not the headset lever (the mouse stands in for it in the Editor).
            DevOverlay overlay = Overlay;
            var pointerInput = InputService as AtlasInput;
            if (pointerInput != null) pointerInput.PointerBlocked = p => overlay.Covers(p.x, p.y);
            Rig.ModeChanged += mode => Overlay.Refresh();
            DeclareDevParams();

            // 19  The on-device menu, onboarding and the perf tour, when their folders are in the build.
            Menu = AppHooks.CreateMenu();
            Onboarding = AppHooks.CreateOnboarding();
            PerfTour = AppHooks.CreatePerfTour();
            Menu.Install(this);
            Onboarding.Install(this);
            PerfTour.Install(this);
            // The hands' parameters last of all, after the perf tour's, so that no existing parameter changes its
            // wire id. The menu reads the registry when it is opened, so it still lists them.
            Hands.DeclareParams(Registry);
            Flow.OwnsFlow = !Onboarding.OwnsFlow;
            HandOver.AutoEnter = !Onboarding.OwnsFlow;

            // 20  The tail of the frame after lane D's hooks: the ladder, the flow, the hand-over, the
            //     SpaceLab actions of the action map, then SpaceLab, which so starts a switch in the frame
            //     that asked for it.
            Loop.AddAfterFrame(Hands.AfterFrame);
            Loop.AddAfterFrame(Ladder);
            Loop.AddAfterFrame(Flow);
            Loop.AddAfterFrame(HandOver);
            StereoRig rig = Rig;
            SpaceActions = new SpaceActions(Spaces, Registry) { InHand = () => rig.Mode != DisplayMode.Headset };
            var menu = Menu as Atlas.App.Menu.OnDeviceMenu;
            if (menu != null) SpaceActions.OpenSpaceMenu = menu.OpenOnSpaces;
            Loop.AddAfterFrame(new SpaceActionStep(InputService, SpaceActions));
            // Their gaze twins, so that every one of them also works with the head alone (rule 5).
            SpaceSignsView signsView = boot.Hud != null && Pool != null ? SpaceSignsView.Create(boot.Hud, Pool) : null;
            Signs = new SpaceSigns(InputService, SpaceActions, Spaces, signsView);
            Loop.AddUpload(Signs);
            Loop.AddAfterFrame(Signs);
            Loop.AddAfterFrame(Spaces);

            // Every property starts from the registry's value; then the app is live.
            Registry.PushAll();
            _spaceParam.Enabled = true;
            Loop.enabled = true;
            AtlasServices.MarkBooted();
            LoopCore.Seal();

            ApplyOptions();
            Onboarding.Begin();
            Debug.Log("Atlas: booted. " + Registry.Count + " parameters, " + SpaceRegistry.RegisteredCount +
                      " spaces registered, head from " + (Pose.Pipeline.Source != null ? Pose.Pipeline.Source.Name : "nothing") +
                      (Onboarding.OwnsFlow ? "" : ", rehearsal flow"));
        }

        void DeclareParams()
        {
            _spaceParam = AppParams.DeclareSpace(Registry, Spaces);
            LensBlit blit = Blit;
            AppParams.DeclareRender(Registry, Ladder, WalkerKernel, Eyes, Rig, Lens, blit.Distortion,
                on => blit.Distortion = on, AtlasLook.SetLineWidth, Furniture);
            AppParams.DeclareUnownedKillSwitches(Registry);

            // Recorded walks: the phone's own logs, and the repository's recordings folder next to the project.
            string logs = Path.Combine(Application.persistentDataPath, "logs");
            string recordings = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "recordings"));
            SourceSwitch = new PoseSourceSwitch(Pose, Application.isEditor ? recordings : logs, Application.isEditor ? logs : null)
            {
                ReplayPath = Options.Replay,
            };
            PoseLogRecorder recorder = Pose.Recorder;
            AppParams.DeclarePose(Registry, Pose.Pipeline, SourceSwitch.SetSource, SourceSwitch.SetSpeed,
                SourceSwitch.SetLoop, on => recorder.Enabled = on);
            AppParams.DeclareScan(Registry, ScanSettings);
            AppParams.DeclareInput(Registry, InputService);
        }

        void DeclareDevParams()
        {
            // With no AR provider there is nothing to scan: a fixture room is the default, so pressing Play
            // in the Editor walks the whole flow. A phone waits for its scan.
            int room = 0;
            if (Options.RoomIsFixture) room = Array.IndexOf(RoomFixtures.All, Options.Room) + 1;
            else if (Options.Room == null && !Pose.Rig.HasAr) room = Array.IndexOf(RoomFixtures.All, RoomFixtures.Sample3x3) + 1;
            bool overlay = Application.isEditor || Debug.isDebugBuild;
            DevOverlay view = Overlay;
            AppParams.DeclareDev(Registry, room, Flow.SetRoomChoice, overlay, on => view.Visible = on);
        }

        void ApplyOptions()
        {
            if (Options.Room != null && !Options.RoomIsFixture) Flow.UseRoomFile(Options.Room);
            if (Options.Source >= 0) Registry.Set(AppParams.PoseSource, Options.Source);
        }

        void OnHeadsetRefused(string message)
        {
            Debug.LogError("Atlas: " + message + ". The headset view is refused.");
            if (Overlay != null) Overlay.Refresh();
        }

        // The last line of the refusal: a caller that holds the rig's own component and not the published
        // one is brought back to the hand in the same call.
        void OnRigModeChanged(DisplayMode mode)
        {
            if (mode != DisplayMode.Headset || Display == null || !Display.HeadsetLocked) return;
            Debug.LogError("Atlas: " + HeadsetGate.Message + ". The headset view is refused.");
            Rig.SetMode(DisplayMode.Hand);
        }

        // A recorded walk names the room it was made in: with no room active, that fixture becomes the room.
        void OnReplayStarted(PoseLog log)
        {
            if (log == null || Spaces.Room != null || !Flow.OwnsFlow) return;
            int index = Array.IndexOf(RoomFixtures.All, log.RoomId);
            if (index >= 0 && !Registry.Set(AppParams.DevRoom, index + 1)) Flow.SetRoomChoice(index + 1);
        }

        // A team pre-scan is kept for a later session; any other walker's room is never written (lane C
        // contract, section 12). Onboarding does this itself when it owns the flow.
        void OnRoomActivated(RoomModel room)
        {
            if (!Flow.OwnsFlow || !ScanSettings.TeamPrescan || room.Quality.Route == ScanRoute.Fixture) return;
            ScanSystems.SaveResult();
        }

        // A kept room (a team pre-scan, saved at its hand-over) is saved again once its objects have their props, so
        // that it reloads with the same props, cells and orientations (plan section 6.5). Any other room stays unsaved.
        void OnObjectsSettled(RoomModel room)
        {
            if (room == null || !ScanSettings.TeamPrescan || room.Quality.Route == ScanRoute.Fixture) return;
            ScanSystems.Rooms.SaveIfKept(room);
        }

        // One pooled instance for a head-locked user (reticle, cue): no fog, and it ignores the world fade.
        Material HeadLockedMaterial()
        {
            Material material = Pool.Rent();
            MaterialPool.SetSurface(material, 1f, 0f, 0f, 0f);
            return material;
        }

        static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        /// <summary>
        /// A clean stop: the entered space is left, the link says goodbye and the files are closed, native
        /// memory and textures are released, the services are cleared and the Atlas objects are destroyed.
        /// Safe to call twice.
        /// </summary>
        public void Shutdown()
        {
            if (_shutDown) return;
            _shutDown = true;

            if (Loop != null) Loop.enabled = false;
            Safely(() => { if (PerfTour != null) PerfTour.Shutdown(); });
            Safely(() => { if (Onboarding != null) Onboarding.Shutdown(); });
            Safely(() => { if (Menu != null) Menu.Shutdown(); });
            Safely(() => { if (Signs != null) Signs.Shutdown(); });
            Safely(() => { if (Flow != null) Flow.LeaveHeadset(); });
            Safely(() => { if (Spaces != null) Spaces.Shutdown(); });
            Safely(() => { if (Transition != null) Transition.Dispose(); });
            // The hands stop before the telemetry does: their last hands.stats must reach an event log that still writes.
            Safely(() => { if (Hands != null) Hands.Shutdown(); });
            Safely(() => { if (TelemetrySystems != null) TelemetrySystems.Shutdown(); });
            // Key frames of a scan in progress and crops not yet handed over are let go.
            Safely(() => { if (ScanSystems != null && ScanSystems.Photos != null) ScanSystems.Photos.Cancel(); });
            Safely(() => { if (Pose != null) Pose.Dispose(); });
            Safely(() => { if (RoomContent != null) RoomContent.Dispose(); });

            Transform root = Boot != null ? Boot.Root : null;
            Transform head = Boot != null ? Boot.Head : null;
            // Bump Guard, the cue and the lens blit end with their objects; the textures and the pool after
            // the cameras and renderers that use them.
            if (head != null) AppObjects.Kill(head.gameObject);
            if (root != null) AppObjects.Kill(root.gameObject);
            Safely(() => { if (Eyes != null) Eyes.Dispose(); });
            Safely(() => { if (Pool != null) Pool.Dispose(); });
            AtlasServices.Clear();
        }

        static void Safely(Action step)
        {
            try { step(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
