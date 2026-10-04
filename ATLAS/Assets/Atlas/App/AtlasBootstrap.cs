using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Atlas.App
{
    /// <summary>
    /// The composition root's entry point (contract README F3). After the first scene has loaded it builds
    /// everything that is not in Atlas_Boot.unity, in code. It runs only in the boot scene, or in a scene
    /// named "Sandbox_..." that holds a root object named "AtlasBoot"; in any other scene it does nothing,
    /// so tests and unrelated scenes are not disturbed.
    /// </summary>
    public static class AtlasBootstrap
    {
        public const string BootSceneName = "Atlas_Boot";
        public const string SandboxPrefix = "Sandbox_";
        public const string SandboxMarker = "AtlasBoot";

        static AtlasApp s_app;

        /// <summary>The running app, or null.</summary>
        public static AtlasApp App { get { return s_app; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
#if !ATLAS_SPECTATOR
            Scene scene = SceneManager.GetActiveScene();
            if (!ShouldBoot(scene.name, HasMarker(scene))) return;
            Start(BootOptions.Parse(Environment.GetCommandLineArgs()));
#endif
            // The Station's Spectator composes its own root (lane D): the walker's app is not built there.
        }

        /// <summary>The rule of which scene boots the app. Engine-free.</summary>
        public static bool ShouldBoot(string sceneName, bool hasSandboxMarker)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;
            if (sceneName == BootSceneName) return true;
            return hasSandboxMarker && sceneName.StartsWith(SandboxPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Builds the app in the loaded scene. Also the entry point of sandboxes and play-mode tests that
        /// compose the app themselves. A second call while an app runs returns the running one.
        /// </summary>
        public static AtlasApp Start(BootOptions options = null)
        {
            if (s_app != null) return s_app;
            // With domain reload off in the Editor this class outlives a play session: subscribe once.
            Application.quitting -= Stop;
            Application.quitting += Stop;
            try
            {
                s_app = AtlasApp.Create(BootScene.FindOrCreate(), options);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError("Atlas: boot failed. The app is not running.");
                s_app = null;
            }
            return s_app;
        }

        /// <summary>Stops the running app and releases what it made. Safe to call with none running.</summary>
        public static void Stop()
        {
            AtlasApp app = s_app;
            s_app = null;
            if (app != null) app.Shutdown();
        }

        static bool HasMarker(Scene scene)
        {
            if (!scene.IsValid() || !scene.name.StartsWith(SandboxPrefix, StringComparison.Ordinal)) return false;
            var roots = new List<GameObject>(8);
            scene.GetRootGameObjects(roots);
            for (int i = 0; i < roots.Count; i++)
                if (roots[i].name == SandboxMarker) return true;
            return false;
        }
    }
}
