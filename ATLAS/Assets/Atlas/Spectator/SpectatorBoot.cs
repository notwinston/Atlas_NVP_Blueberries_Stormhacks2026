using System;
using System.Collections.Generic;
using Atlas.Telemetry;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Atlas.Spectator
{
    /// <summary>How the Spectator was started: where the Hub is.</summary>
    public sealed class SpectatorOptions
    {
        /// <summary>The Hub runs on the Station itself, next to the Spectator.</summary>
        public const string DefaultHubHost = "127.0.0.1";

        public string HubHost = DefaultHubHost;
        public int HubPort = PairingAddress.DefaultPort;

        /// <summary>Reads <c>-atlasHub 192.168.0.10[:9050]</c> from a command line. Anything else is ignored.</summary>
        public static SpectatorOptions Parse(string[] args)
        {
            var options = new SpectatorOptions();
            if (args == null) return options;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (!string.Equals(args[i], "-atlasHub", StringComparison.OrdinalIgnoreCase)) continue;
                string host;
                int port;
                if (PairingAddress.TryParse(args[i + 1], out host, out port))
                {
                    options.HubHost = host;
                    options.HubPort = port;
                }
            }
            return options;
        }
    }

    /// <summary>
    /// The Spectator's entry point (Master Plan section 7.3.1, task P1-D05; contract README F3). Atlas.App may
    /// not be referenced from here and does not boot in a Station build, so the Spectator composes its own
    /// root after the first scene has loaded. In a build made with ATLAS_SPECTATOR it always starts. In the
    /// Editor it starts in the scene "PC_Spectator", or in any scene that holds a root object named
    /// "AtlasSpectatorBoot"; the scene needs nothing else.
    /// </summary>
    public static class SpectatorBoot
    {
        public const string SceneName = "PC_Spectator";
        public const string Marker = "AtlasSpectatorBoot";

        static SpectatorApp s_app;

        /// <summary>The running Spectator, or null.</summary>
        public static SpectatorApp App { get { return s_app; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
#if ATLAS_SPECTATOR && !UNITY_EDITOR
            Start(SpectatorOptions.Parse(Environment.GetCommandLineArgs()));
#else
            Scene scene = SceneManager.GetActiveScene();
            if (ShouldBoot(scene.name, HasMarker(scene))) Start(SpectatorOptions.Parse(Environment.GetCommandLineArgs()));
#endif
        }

        /// <summary>The rule of which scene starts the Spectator in the Editor. Engine-free.</summary>
        public static bool ShouldBoot(string sceneName, bool hasMarker)
        {
            return hasMarker || string.Equals(sceneName, SceneName, StringComparison.Ordinal);
        }

        /// <summary>Builds the Spectator. A second call while one runs returns the running one.</summary>
        public static SpectatorApp Start(SpectatorOptions options = null)
        {
            if (s_app != null) return s_app;
            Application.quitting -= Stop;
            Application.quitting += Stop;
            try
            {
                s_app = SpectatorApp.Create(options ?? new SpectatorOptions());
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError("Atlas: the Spectator could not start.");
                s_app = null;
            }
            return s_app;
        }

        /// <summary>Stops the running Spectator and releases what it made. Safe to call with none running.</summary>
        public static void Stop()
        {
            SpectatorApp app = s_app;
            s_app = null;
            if (app != null) app.Shutdown();
        }

        static bool HasMarker(Scene scene)
        {
            if (!scene.IsValid()) return false;
            var roots = new List<GameObject>(8);
            scene.GetRootGameObjects(roots);
            for (int i = 0; i < roots.Count; i++)
                if (roots[i].name == Marker) return true;
            return false;
        }

#if UNITY_EDITOR
        // The Editor twin with no scene to author: press Play in an empty scene, then choose this entry.
        [UnityEditor.MenuItem("Atlas/Start Spectator (play mode)")]
        static void StartFromMenu()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Atlas: enter play mode in an empty scene first, then start the Spectator.");
                return;
            }
            if (Atlas.World.AtlasServices.BootCompleted && s_app == null)
            {
                Debug.LogWarning("Atlas: the walker's app is running in this scene; the Spectator needs a scene of its own.");
                return;
            }
            Start(SpectatorOptions.Parse(Environment.GetCommandLineArgs()));
        }
#endif
    }
}
