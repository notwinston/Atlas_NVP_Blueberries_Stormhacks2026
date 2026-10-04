using System;
using System.IO;
using Atlas.Blades.Network.Core;
using Atlas.Rendering;
using Atlas.World;
using UnityEngine;

namespace Atlas.Blades.Network
{
    /// <summary>
    /// The blade's composition (contract NET-06): builds the floor map in code, hidden, for Home. This is the only
    /// registration in Home; Home's own files are not edited. NET-08's exhibit shows it through <see cref="View"/>.
    /// </summary>
    public static class NetworkInstaller
    {
        static NetworkFloorView s_View;

        /// <summary>The installed view, or null before the composition root has published its services.</summary>
        public static NetworkFloorView View { get { return s_View != null ? s_View : null; } }

        /// <summary>Loads the default network (seed 7, section 3), builds the view hidden, joins the frame loop and follows the room.</summary>
        public static NetworkFloorView Install(ISpaceHost host, IFrameLoop loop, MaterialPool pool)
        {
            if (s_View != null) return s_View;
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (loop == null) throw new ArgumentNullException(nameof(loop));
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            NetworkModel model;
            try
            {
                model = NetworkModel.Load(Path.Combine(Application.streamingAssetsPath, "networks", NetworkModel.FileName(NetworkModel.BundledSeeds[0])));
            }
            catch (Exception e)
            {
                Debug.LogError("net.load: " + e.Message);
                return null;
            }
            NetworkFloorView view = NetworkFloorView.Create(model, pool);
            view.Place(host.Room);
            host.RoomChanged += view.Place;
            view.Attach(loop);
            s_View = view;
            return view;
        }

        // The composition root clears AtlasServices when it starts, in no fixed order against this entry, so a light
        // poller waits for the services instead of a Booted subscription that a Clear would drop.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInstall()
        {
            if (s_View != null) return;
            var go = new GameObject("Network Installer") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Poller>();
        }

        sealed class Poller : MonoBehaviour
        {
            void Update()
            {
                if (s_View != null) { Destroy(gameObject); return; }
                if (!AtlasServices.BootCompleted) return;
                if (!AtlasServices.TryGet(out ISpaceHost host) || !AtlasServices.TryGet(out IFrameLoop loop) ||
                    !AtlasServices.TryGet(out MaterialPool pool)) return;
                Install(host, loop, pool);
                Destroy(gameObject);
            }
        }
    }
}
