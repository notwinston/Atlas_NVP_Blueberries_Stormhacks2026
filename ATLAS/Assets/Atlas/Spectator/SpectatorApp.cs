using System;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Rendering.Room;
using Atlas.Telemetry;
using Atlas.World;
using UnityEngine;

namespace Atlas.Spectator
{
    /// <summary>
    /// The Spectator's composition root (Master Plan section 7.3.1, task P1-D05; lane D contract, section 5.8).
    /// It builds the same look, room content, kernel, parameter registry and space modules as the walker's app,
    /// and in place of the stereo rig, the AR session, Bump Guard and input: one mono camera, a link client of
    /// role spectator and the State it relays. Everything is made in code; nothing is searched in a scene.
    /// </summary>
    public sealed class SpectatorApp
    {
        /// <summary>Material instances created up front, as on the phone (lane A contract, section 11).</summary>
        public const int PoolCapacity = 256;

        bool _shutDown;

        public AtlasBootContext Boot { get; private set; }
        public SpectatorOptions Options { get; private set; }
        public ParamRegistry Registry { get; private set; }
        public MaterialPool Pool { get; private set; }
        public IWalkerKernel WalkerKernel { get; private set; }
        public SpectatorHost Host { get; private set; }
        public SpectatorCore Core { get; private set; }
        public SpectatorLoop Loop { get; private set; }
        public RoomContent RoomContent { get; private set; }
        public AtlasTelemetry TelemetrySystems { get; private set; }
        public SpectatorView View { get; private set; }
        UnityForgeTransport _http;

        SpectatorApp() { }

        public static SpectatorApp Create(SpectatorOptions options)
        {
            var app = new SpectatorApp { Options = options ?? new SpectatorOptions() };
            app.Build();
            return app;
        }

        void Build()
        {
            // Services, the Atlas objects, the boot context. There is no XR origin and no tracked camera.
            AtlasServices.Clear();
            var root = new GameObject("Atlas Spectator");
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(root);
            var boot = new AtlasBootContext
            {
                Root = root.transform,
                Systems = Child(root.transform, "Systems"),
                RoomRoot = Child(root.transform, "Room"),
                SpaceRoot = Child(root.transform, "Space"),
                IsSpectator = true,
                IsDevice = false,
            };
            Boot = boot;

            Registry = new ParamRegistry();
            boot.Params = Registry;
            AtlasServices.Register(Registry);

            // The look and the material pool, as on the phone.
            AtlasLook.Initialise();
            AtlasLook.SetFade(1f);
            Pool = MaterialPool.CreateDefault(PoolCapacity);
            AtlasServices.Register(Pool);

            // The kernel (the flat stub in this build). It is placed from the State and never stepped.
            WalkerKernel = new FlatWalkerKernel();
            AtlasServices.Register(WalkerKernel);

            // The space host, the room source, the mapping from the link, and the frame.
            Host = new SpectatorHost(Registry, WalkerKernel)
            {
                Created = ParentModule,
                Destroy = DestroyModule,
            };
            AtlasServices.Register<ISpaceHost>(Host);
            _http = new UnityForgeTransport();
            Core = new SpectatorCore(Host, Registry, WalkerKernel, new SpectatorRoomSource(_http, RoomFixtures.Load));
            Loop = new SpectatorLoop(Core, WalkerKernel);
            Loop.AddSense(new SpectatorSense(Core));
            AtlasServices.Register<IFrameLoop>(Loop);

            // The mirror camera is the head every lane reads.
            View = SpectatorView.Create(boot.Root, this);
            boot.Head = View.Head;

            // Room content: it rebuilds on the host's RoomChanged and ticks from the upload step once booted.
            RoomContent = RoomContentInstaller.Install(boot, Pool);
            RoomContent content = RoomContent;
            Host.BetweenSpaces = () =>
            {
                AtlasLook.ResetForSpace();
                if (content.IsBuilt) content.ResetPresentation();
            };
            Loop.AddUpload(new LookUpload(WalkerKernel));

            // The spaces' parameters, so that the phone's manifest finds them by name.
            SpaceRegistry.DeclareAllParams(Registry);

            // Link, event log, probe. The installer knows the role from the boot context and adds its hooks
            // to the published loop.
            TelemetrySystems = TelemetryInstaller.Install(boot);
            Host.Events = TelemetrySystems.Events;
            Core.Attach(TelemetrySystems.Link);
            TelemetrySystems.Pump.Start(Options.HubHost, Options.HubPort);

            Registry.PushAll();
            View.enabled = true;
            AtlasServices.MarkBooted();
            Debug.Log("Atlas: Spectator booted. " + Registry.Count + " parameters, " + SpaceRegistry.RegisteredCount +
                      " spaces registered, Hub at " + Options.HubHost + ":" + Options.HubPort);
        }

        void ParentModule(ISpaceModule module)
        {
            var component = module as Component;
            if (component != null && Boot.SpaceRoot != null) component.transform.SetParent(Boot.SpaceRoot, false);
        }

        static void DestroyModule(ISpaceModule module)
        {
            var component = module as Component;
            if (component != null) Kill(component.gameObject);
        }

        /// <summary>
        /// A clean stop: the entered space is left, the link says goodbye, the room content and the pool are
        /// released, the services are cleared and the objects are destroyed. Safe to call twice.
        /// </summary>
        public void Shutdown()
        {
            if (_shutDown) return;
            _shutDown = true;
            if (View != null) View.enabled = false;
            Safely(() => Core.Detach());
            Safely(() => Host.Leave());
            Safely(() => { if (TelemetrySystems != null) TelemetrySystems.Shutdown(); });
            Safely(() => { if (View != null) View.Release(); });
            Safely(() => { if (RoomContent != null) RoomContent.Dispose(); });
            Safely(() => { if (_http != null) _http.Dispose(); });
            if (Boot != null && Boot.Root != null) Kill(Boot.Root.gameObject);
            Safely(() => { if (Pool != null) Pool.Dispose(); });
            AtlasServices.Clear();
        }

        static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        internal static void Kill(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }

        static void Safely(Action step)
        {
            try { step(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Step 6: the look's shader globals, from the walker's head.</summary>
        sealed class LookUpload : IUploadStep
        {
            readonly IWalkerKernel _kernel;
            public LookUpload(IWalkerKernel kernel) { _kernel = kernel; }

            public void Upload(in FrameContext frame)
            {
                AtlasLook.Upload(frame.RawHeadPosition, _kernel.MetresPerUnit, frame.Time, frame.DeltaTime);
            }
        }
    }
}
