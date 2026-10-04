using System;
using System.IO;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using UnityEngine;

namespace Atlas.Telemetry
{
    /// <summary>
    /// Creates lane D's systems in the running app (contracts F3; Master Plan tasks P1-D03, P1-D04, P1-D11):
    /// session and log files, the event log, the link and its driver, pairing, the perf probe and the HUD.
    /// Everything is made in code; nothing is looked up in a scene.
    /// </summary>
    public static class TelemetryInstaller
    {
        public const string ParamDemoLock = "demoLock";
        public const string ParamLinkEnabled = "link.enabled";
        public const string ParamStateHz = "link.stateHz";
        public const string ParamTelemetryMode = "telemetry.mode";
        public const string ParamHudVisible = "hud.visible";

        /// <summary>Baked parameter defaults (the Console's "Bake"): <c>Resources/AtlasParams/defaults.json</c>.</summary>
        public const string BakedDefaultsResource = "AtlasParams/defaults";

        static readonly string[] s_modeNames = { "live", "phoneCsv", "hud" };

        // Warnings and errors per second that are copied to the phone log; a script that logs every frame
        // must not turn the log into the frame's cost.
        const int LogLinesPerSecond = 20;

        /// <summary>
        /// Creates the systems under <c>boot.Systems</c> and <c>boot.Hud</c>, publishes <see cref="IEventLog"/>,
        /// <see cref="IPerfSource"/>, <see cref="PairingController"/> and <see cref="AtlasTelemetry"/>, and adds
        /// the frame hooks when the frame loop is already published. Call it after the registry exists and
        /// before <c>AtlasServices.MarkBooted</c>.
        /// </summary>
        /// <param name="transport">The network; null means the real one. Tests and the loopback rig pass their own.</param>
        public static AtlasTelemetry Install(AtlasBootContext boot, ILinkTransport transport = null)
        {
            if (boot == null) throw new ArgumentNullException("boot");
            if (boot.Params == null) boot.Params = new ParamRegistry();
            ParamRegistry registry = boot.Params;
            var telemetry = new AtlasTelemetry { Params = registry };

            // 1. Session and files. persistentDataPath is read here, once, on the main thread.
            var clock = new StopwatchClock();
            telemetry.SessionId = SessionId.New();
            if (!boot.IsSpectator)
            {
                telemetry.Files = new LogFileWriter(Path.Combine(Application.persistentDataPath, "logs"));
                telemetry.Files.Open(telemetry.SessionId);
            }

            // 2. Event log.
            var events = new EventLog(clock, telemetry.Files);
            telemetry.Events = events;
            AtlasServices.Replace<IEventLog>(events);

            // 3. Link, driver, pairing. Whoever runs the walker's app is the phone of the link, the Editor
            //    included, so the whole flow can be rehearsed against the Station with no device.
            LinkRole role = boot.IsSpectator ? LinkRole.Spectator : LinkRole.Phone;
            var client = new LinkClient(transport ?? CreateUdpTransport(clock, events), clock, role, boot.IsSpectator ? null : registry)
            {
                Device = SystemInfo.deviceModel ?? string.Empty,
                Build = Application.version ?? string.Empty
            };
            // The session names the phone's files and, through Hello, the Hub's log folder (contract section 5.5).
            if (!boot.IsSpectator) client.SetSession(telemetry.SessionId);
            var pump = new LinkPump(client, events, telemetry.Files);
            telemetry.Link = client;
            telemetry.Pump = pump;
            var pairing = new PairingController(new PlayerPrefsPairingStore(), pump);
            telemetry.Pairing = pairing;
            AtlasServices.Replace(pairing);
            // The registry is the one owner of the demo lock: a Hub that welcomes with the lock switches the
            // parameter, and the parameter's callback switches the client. A Welcome never lifts a lock.
            client.Welcomed += welcome =>
            {
                if (welcome.DemoLock) registry.Set(ParamDemoLock, 1f, ParamSource.Remote);
            };

            // 4. Probe and HUD.
            var probe = new AtlasPerfProbe(pump);
            telemetry.Probe = probe;
            AtlasServices.Replace<IPerfSource>(probe);
            var hud = new AtlasHud(boot.Hud, probe, pump);
            telemetry.Hud = hud;

            // 5. Parameters of lane D, with the start values of this build.
            DeclareParams(registry, telemetry);
            ApplyBakedDefaults(registry);
#if ATLAS_DEMO_LOCK
            registry.Set(ParamDemoLock, 1f, ParamSource.Restore);
#endif
            if (Debug.isDebugBuild) registry.Set(ParamHudVisible, 1f, ParamSource.Restore);

            // 6. Taps: the QR code's deep link, warnings and errors, pause and quit.
            telemetry.DeepLinkTap = url => pairing.TrySetAddress(url);
            Application.deepLinkActivated += telemetry.DeepLinkTap;
            string launchUrl = Application.absoluteURL;
            if (!string.IsNullOrEmpty(launchUrl)) pairing.TrySetAddress(launchUrl);

            long logSecond = 0;
            int logLines = 0;
            telemetry.LogTap = (condition, stackTrace, type) =>
            {
                if (type == LogType.Log) return;
                long second = clock.NowUs / 1000000;
                if (second != logSecond) { logSecond = second; logLines = 0; }
                if (++logLines > LogLinesPerSecond) return;
                events.LogLine(type == LogType.Warning ? "warn" : "error", condition);
            };
            Application.logMessageReceived += telemetry.LogTap;

            if (boot.Systems != null)
            {
                var go = new GameObject("AtlasTelemetry");
                go.transform.SetParent(boot.Systems, false);
                go.AddComponent<TelemetryLifecycle>().Bind(telemetry);
            }

            AtlasServices.Replace(telemetry);

            events.Log("boot", JsonValue.Object()
                .Set("build", client.Build)
                .Set("device", client.Device)
                .Set("os", SystemInfo.operatingSystem ?? string.Empty));
            events.Log("session", "sessionId", telemetry.SessionId);

            // 7. Frame hooks now if the loop exists, and the services that are published after this installer
            //    once the composition root has finished.
            IFrameLoop loop;
            if (AtlasServices.TryGet(out loop)) telemetry.AddHooks(loop);
            AtlasServices.Booted += () => ResolveLate(telemetry);

            // 8. The link, when this phone has been paired before. On a first launch nothing is sent until
            //    onboarding asks (PairingController.BeginPairing): the first packet raises the iOS Local Network
            //    prompt, and it must not come up at boot towards an address the walker never chose.
            if (registry.GetBool(ParamLinkEnabled, true) && pairing.WasPaired) pump.Start();
            return telemetry;
        }

        // Atlas.Telemetry does not reference LiteNetLib, and the UDP transport implements one of its interfaces,
        // so the compiler cannot see the type from here. It is created by name; link.xml next to this file keeps
        // it through managed stripping. If it cannot be made, the app runs with a link that never connects.
        static ILinkTransport CreateUdpTransport(ILinkClock clock, EventLog events)
        {
            try
            {
                Type type = typeof(LinkClient).Assembly.GetType("Atlas.Link.LiteNetTransport");
                var udp = type != null ? Activator.CreateInstance(type) as ILinkTransport : null;
                if (udp != null) return udp;
                events.LogLine("error", "link: the UDP transport is missing from this build; running without a Station", false);
            }
            catch (Exception e)
            {
                events.LogLine("error", "link: the UDP transport could not be created: " + e.Message, false);
            }
            return new LoopbackNetwork(clock).CreateEndpoint();
        }

        static void DeclareParams(ParamRegistry registry, AtlasTelemetry t)
        {
            LinkClient client = t.Link;
            LinkPump pump = t.Pump;
            AtlasHud hud = t.Hud;
            PairingController pairing = t.Pairing;

            registry.DeclareBool(ParamDemoLock, "app", false, on => client.DemoLock = on);
            registry.DeclareBool(ParamLinkEnabled, "link", true, on =>
            {
                if (!on) pump.Stop("link.enabled off");
                else if (pairing.WasPaired) pump.Start();
                hud.LinkDotEnabled = on;
            });
            registry.DeclareInt(ParamStateHz, "link", 10, 60, 60, hz => client.StateHz = hz);
            registry.DeclareEnum(ParamTelemetryMode, "link", s_modeNames, 0, mode =>
            {
                pump.Mode = (TelemetryMode)mode;
                hud.ForceVisible = mode == (int)TelemetryMode.Hud;
            });
            registry.DeclareBool(ParamHudVisible, "hud", false, on => hud.Visible = on);
        }

        // "Bake" on the Console writes a preset into the project; a build started with no Station then runs
        // with those values (Master Plan section 7.3.2). Values of parameters declared later are applied as
        // they are declared.
        static void ApplyBakedDefaults(ParamRegistry registry)
        {
            var asset = Resources.Load<TextAsset>(BakedDefaultsResource);
            JsonValue baked;
            if (asset == null || !AtlasJson.TryParse(asset.text, out baked) || !baked.IsObject) return;
            registry.Restore(baked, ParamSource.Restore);
            JsonValue values = baked["values"].IsObject ? baked["values"] : baked;
            registry.Declared += entry =>
            {
                JsonValue v;
                if (!values.TryGet(entry.Name, out v)) return;
                if (v.IsNumber) registry.Set(entry, v.AsFloat(), ParamSource.Restore);
                else if (v.IsBool) registry.Set(entry, v.AsBool() ? 1f : 0f, ParamSource.Restore);
            };
        }

        static void ResolveLate(AtlasTelemetry t)
        {
            IFrameLoop loop;
            if (!t.Hooked && AtlasServices.TryGet(out loop)) t.AddHooks(loop);

            IWalkerKernel kernel;
            if (t.Pump.Kernel == null && AtlasServices.TryGet(out kernel)) t.Pump.Kernel = kernel;

            IOperatorCommands commands;
            if (t.Pump.Commands == null && AtlasServices.TryGet(out commands)) t.Pump.Commands = commands;

            ISpaceHost host;
            if (t.SpaceHost == null && AtlasServices.TryGet(out host))
            {
                t.SpaceHost = host;
                t.RoomTap = t.Pump.SetRoom;
                host.RoomChanged += t.RoomTap;
                if (host.Room != null) t.Pump.SetRoom(host.Room);
            }

            IQualityState quality;
            if (t.QualityState == null && AtlasServices.TryGet(out quality))
            {
                t.QualityState = quality;
                t.Probe.Quality = quality;
                EventLog events = t.Events;
                QualityTier last = quality.Tier;
                t.TierTap = tier =>
                {
                    events.Log("quality.tier", JsonValue.Object()
                        .Set("from", (int)last).Set("to", (int)tier).Set("thermal", quality.ThermalState));
                    last = tier;
                };
                quality.TierChanged += t.TierTap;
            }
        }
    }
}
