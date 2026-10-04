using Atlas.Scan;
using Atlas.Telemetry;
using Atlas.World;
using UnityEngine;

namespace Atlas.App.Onboarding
{
    /// <summary>
    /// Onboarding as the composition root installs it (contract README F3; lane A contract, section 8.2, row
    /// 19): the engine-free OnboardingFlow, its adapters to the app's systems and the screen. It announces
    /// itself through AppHooks before the first scene loads, in the way a space does with SpaceRegistry.
    /// <para>It owns the flow unless the command line names a room or a recorded walk (-atlasRoom,
    /// -atlasReplay): those are rehearsals with no walker to ask, and stay with the rehearsal flow.</para>
    /// </summary>
    public sealed class OnboardingInstaller : IOnboardingFlow, IAfterFrameStep
    {
        AtlasApp _app;
        WorldMapStore _worldMaps;
        OnboardingView _view;
        Menu.OnDeviceMenu _menu;

        /// <summary>The state machine; null when the rehearsal flow owns the scan.</summary>
        public OnboardingFlow Flow { get; private set; }
        public bool OwnsFlow { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            AppHooks.Onboarding = Create;
        }

        static IOnboardingFlow Create()
        {
            return new OnboardingInstaller();
        }

        public void Install(AtlasApp app)
        {
            _app = app;
            BootOptions options = app.Options;
            OwnsFlow = !app.Boot.IsSpectator && options.Room == null && options.Replay == null;
            if (!OwnsFlow) return;

            ScanSystems scan = app.ScanSystems;
            ArSessionRig rig = app.Pose.Rig;
            _worldMaps = new WorldMapStore(scan.Rooms, rig);
            AtlasTelemetry telemetry = app.TelemetrySystems;

            Flow = new OnboardingFlow(
                new UnityCameraPermission(rig),
                new StationPairing(telemetry != null ? telemetry.Pairing : null),
                new SessionScan(scan.Session, app.ScanSettings),
                new AppRooms(scan, _worldMaps, app.HandOver, app.LoopCore, app.ScanSettings),
                new AppHeadset(app.Flow, app.HandOver))
            {
                // A fixture room with one click, where there is nobody to protect from it (contract README F9).
                SamplesAllowed = Application.isEditor || Debug.isDebugBuild,
            };
            IEventLog events;
            if (AtlasServices.TryGet(out events)) Flow.Events = events;

            Transform tracked = app.Boot.TrackedCamera;
            Camera camera = tracked != null ? tracked.GetComponent<Camera>() : null;
            _view = new OnboardingView(app.Boot.Systems, Flow, scan.Session, camera) { Overlay = app.Overlay };

            // "Rescan the room" in the menu: onboarding owns the scan, so the row comes here and not to the
            // rehearsal flow, which nobody ticks while onboarding owns the flow (lane C contract, section 14.2).
            _menu = app.Menu as Menu.OnDeviceMenu;
            if (_menu != null) _menu.RescanRequested += OnRescan;
            // The tail of the frame, before the hand-over and SpaceLab: a room activated here is picked up
            // by them in the same frame.
            app.Loop.AddAfterFrame(this);
        }

        public void Begin()
        {
            if (Flow == null) return;
            Flow.Begin();
            _view.Refresh(0f);
        }

        public void AfterFrame(in FrameContext frame)
        {
            if (Flow == null) return;
            Flow.Tick(frame.DeltaTime);
            _view.Refresh(frame.DeltaTime);
        }

        void OnRescan()
        {
            if (Flow == null) return;
            // Out of the headset and to the room choice; the screen follows in this frame's Refresh.
            Flow.Rescan();
        }

        public void Shutdown()
        {
            if (_menu != null) _menu.RescanRequested -= OnRescan;
            _menu = null;
            if (_worldMaps != null) _worldMaps.Cancel();
            if (_view != null) _view.Dispose();
            _view = null;
            _worldMaps = null;
            Flow = null;
            _app = null;
        }
    }
}
