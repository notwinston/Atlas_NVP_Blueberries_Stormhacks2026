using UnityEngine;

namespace Atlas.App.Menu
{
    /// <summary>
    /// Announces the on-device menu to the composition root (Master Plan task P1-D13): before the first scene
    /// loads, the menu's factory is put into the app's hook, and the root creates and installs the menu at row
    /// 19 of its creation order. The Spectator composes its own root and never runs the hook.
    /// </summary>
    public static class MenuInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Announce()
        {
            AppHooks.Menu = Create;
        }

        /// <summary>The menu, not yet installed. Also for sandboxes and tests that compose the app themselves.</summary>
        public static IAppMenu Create()
        {
            return new OnDeviceMenu();
        }
    }
}
