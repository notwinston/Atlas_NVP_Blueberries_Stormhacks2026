using System;
using Atlas.World;
using UnityEngine;

namespace Atlas.Blades.Network
{
    /// <summary>
    /// The network as an exhibit in Home (contract NET-08, section 6.6): shows and hides the floor map that
    /// <see cref="NetworkInstaller"/> built. Begin sets Visible, End clears it; neither touches the RoomModel.
    /// </summary>
    public sealed class NetworkExhibit : IHomeExhibit
    {
        readonly Func<NetworkFloorView> m_View;

        public NetworkExhibit() : this(() => NetworkInstaller.View) { }

        /// <summary>A view source other than the installer (tests).</summary>
        public NetworkExhibit(Func<NetworkFloorView> view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            m_View = view;
        }

        public string Label { get { return "Network"; } }

        public bool Ready { get { return m_View() != null; } }

        public bool Active
        {
            get { NetworkFloorView v = m_View(); return v != null && v.Visible; }
        }

        public bool Begin()
        {
            NetworkFloorView v = m_View();
            if (v == null) return false;
            v.Visible = true;
            return true;
        }

        public void End()
        {
            NetworkFloorView v = m_View();
            if (v != null) v.Visible = false;
        }

        // Its own entry, apart from the installer's: the composition root clears AtlasServices when it starts, in no
        // fixed order against this one, so a light poller waits for BootCompleted and then publishes.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoRegister()
        {
            var go = new GameObject("Network Exhibit Registrar") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Registrar>();
        }

        sealed class Registrar : MonoBehaviour
        {
            void Update()
            {
                if (!AtlasServices.BootCompleted) return;
                AtlasServices.Register<IHomeExhibit>(new NetworkExhibit());
                Destroy(gameObject);
            }
        }
    }
}
