using System;
using System.IO;
using Atlas.Link;
using Atlas.World;
using UnityEngine;

namespace Atlas.Telemetry
{
    /// <summary>
    /// Creates the asset client in the running app (contract README F3; Master Plan tasks P1-D10 and P1-D12)
    /// and wires it to the link, to pairing, to the room content and to the frame loop. Everything is made in
    /// code. The composition root calls <see cref="Install"/> after <see cref="TelemetryInstaller.Install"/>
    /// and before <c>AtlasServices.MarkBooted</c>.
    /// </summary>
    public static class AssetClientInstaller
    {
        public const string ParamForgeBackend = "forge.backend";
        public const int ForgePort = 8000;

        static readonly string[] s_backends =
        {
            AssetClientCore.BackendLocal, AssetClientCore.BackendHosted, AssetClientCore.BackendCache, AssetClientCore.BackendOff
        };

        /// <summary>forge.backend's default: <c>off</c>, the on-phone path (standalone-phone plan D3). AppParams declares the same.</summary>
        public const int DefaultBackend = 3;

        /// <summary>
        /// Creates the client with the Forge's round trip and the on-phone path, publishes it as
        /// <see cref="IObjectPhotoSink"/> and as <see cref="AssetClient"/>, declares <c>forge.backend</c> (default off),
        /// and once <c>AtlasServices.Booted</c> is raised follows the room content and adds itself to step 8b of the
        /// frame loop. Returns null in the Spectator, which shows
        /// the room as the phone reports it and asks the Forge for nothing.
        /// </summary>
        public static AssetClient Install(AtlasBootContext boot, AtlasTelemetry telemetry)
        {
            if (boot == null) throw new ArgumentNullException("boot");
            if (telemetry == null) throw new ArgumentNullException("telemetry");
            if (boot.IsSpectator) return null;

            // persistentDataPath and Resources are read here, once, on the main thread.
            var cache = new FileAssetCache(Path.Combine(Application.persistentDataPath, "assets"));
            FallbackSet fallback = FallbackSet.Load(ReadFallbackResource);
            if (fallback.LastError != null) telemetry.Events.LogLine("warn", "assets: " + fallback.LastError, false);

            // The prop library of the on-phone path (standalone-phone plan section 6.3). A missing or partial library
            // is not fatal: an object with no prop of its category keeps its first shape.
            PropSet props = PropSet.Load(ReadPropResource);
            if (props.Error != null) telemetry.Events.LogLine("warn", "assets: " + props.Error, false);

            var transport = new UnityForgeTransport();
            AssetClient client = AssetClient.Create(transport, cache, new ThreadPoolAssetWorker(), fallback, telemetry.Events, props);
            AssetClientCore core = client.Core;
            // The Station is optional (plan D3): with nothing declared, the phone builds its objects itself.
            core.Backend = s_backends[DefaultBackend];

            // The link: job news in, acknowledgements out. Without a Station the client polls the Forge itself.
            LinkClient link = telemetry.Link;
            PairingController pairing = telemetry.Pairing;
            if (link != null)
            {
                link.JobStatusReceived += client.OnJobStatus;
                link.AssetReadyReceived += client.OnAssetReady;
                link.Welcomed += welcome => core.ForgeUrl = ForgeUrl(welcome, pairing);
                core.AckSink = ack => { if (link.IsReady) link.Send(ack); };
            }
            if (pairing != null)
            {
                pairing.AddressChanged += address => core.ForgeUrl = ForgeUrl(link != null ? link.Welcome : null, pairing);
                core.ForgeUrl = ForgeUrl(link != null ? link.Welcome : null, pairing);
            }

            // forge.backend (section 1.4, id 45): what is sent in meta.backend; `off` sends nothing at all and the
            // objects are built on the phone (standalone-phone plan section 6.5).
            ParamRegistry registry = telemetry.Params ?? boot.Params;
            if (registry != null)
            {
                ParamEntry backend;
                if (!registry.TryGet(ParamForgeBackend, out backend))
                    backend = registry.DeclareEnum(ParamForgeBackend, "forge", s_backends, DefaultBackend);
                core.Backend = s_backends[Mathf.Clamp(backend.Int, 0, s_backends.Length - 1)];
                registry.Changed += (entry, source) =>
                {
                    if (ReferenceEquals(entry, backend)) core.Backend = s_backends[Mathf.Clamp(entry.Int, 0, s_backends.Length - 1)];
                };
            }

            AtlasServices.Replace<IObjectPhotoSink>(client);
            AtlasServices.Replace(client);

            var wiring = new Wiring(client, pairing, transport, telemetry.Events, telemetry.Probe);
            // Step 8b now if the loop is published, as lane D's other hooks; the room content once all is built.
            IFrameLoop loop;
            if (AtlasServices.TryGet(out loop)) wiring.AddTo(loop);
            AtlasServices.Booted += wiring.OnBooted;
            if (boot.Systems != null)
            {
                var go = new GameObject("AtlasAssetClient");
                go.transform.SetParent(boot.Systems, false);
                go.AddComponent<AssetClientLifecycle>().Bind(wiring);
            }
            return client;
        }

        /// <summary>The Forge's base URL: what the Hub announced, else port 8000 on the paired Station.</summary>
        public static string ForgeUrl(WelcomeMessage welcome, PairingController pairing)
        {
            if (welcome != null && !string.IsNullOrEmpty(welcome.ForgeUrl)) return welcome.ForgeUrl;
            if (pairing != null && !string.IsNullOrEmpty(pairing.Host))
                return "http://" + pairing.Host + ":" + ForgePort.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return string.Empty;
        }

        // "manifest" and "<id>.glb": the prop library (Resources/AtlasProps), read once at install.
        static byte[] ReadPropResource(string name)
        {
            var asset = Resources.Load<TextAsset>(PropSet.ResourceFolder + "/" + name);
            if (asset == null) return null;
            byte[] bytes = asset.bytes;
            Resources.UnloadAsset(asset);
            return bytes;
        }

        // "manifest" and "<category>.glb": the files tools/gen_fallback_set.py writes next to this assembly.
        static byte[] ReadFallbackResource(string name)
        {
            var asset = Resources.Load<TextAsset>(FallbackSet.ResourceFolder + "/" + name);
            if (asset == null) return null;
            byte[] bytes = asset.bytes;
            Resources.UnloadAsset(asset);
            return bytes;
        }

        internal sealed class Wiring : IAfterFrameStep
        {
            readonly AssetClient _client;
            readonly PairingController _pairing;
            readonly UnityForgeTransport _transport;
            readonly EventLog _events;
            readonly AtlasPerfProbe _probe;
            bool _hooked, _errorLogged;

            public Wiring(AssetClient client, PairingController pairing, UnityForgeTransport transport, EventLog events,
                AtlasPerfProbe probe = null)
            {
                _probe = probe;
                _client = client;
                _pairing = pairing;
                _transport = transport;
                _events = events;
            }

            public void AddTo(IFrameLoop loop)
            {
                if (_hooked) return;
                _hooked = true;
                loop.AddAfterFrame(this);
            }

            public void OnBooted()
            {
                IRoomContent content;
                if (AtlasServices.TryGet(out content)) _client.Bind(content);
                IFrameLoop loop;
                if (AtlasServices.TryGet(out loop)) AddTo(loop);
            }

            public void AfterFrame(in FrameContext frame)
            {
                _client.AfterFrame(in frame);
                // For the telemetry row and line 5 of the HUD. The probe runs earlier in step 8b, so a row sees
                // the counts of the frame before, which is fine for a figure taken once per second.
                if (_probe != null)
                {
                    _probe.AssetsPlaced = (byte)Math.Min(255, _client.PlacedCount);
                    _probe.AssetsPending = (byte)Math.Min(255, _client.PendingCount);
                }
                // Onboarding and the menu read the Forge's state from pairing.
                if (_pairing != null) _pairing.ForgeReachable = _client.ForgeReachable;
                if (!_errorLogged && _transport.LastError != null)
                {
                    _errorLogged = true;
                    if (_events != null) _events.LogLine("error", "assets: the Forge cannot be asked: " + _transport.LastError);
                }
            }

            public void Shutdown()
            {
                _client.Dispose();
                _transport.Dispose();
            }
        }
    }
}
