using UnityEngine;

namespace Atlas.Telemetry
{
    /// <summary>Ends the client's requests and frees its meshes when the app quits.</summary>
    [AddComponentMenu("")]
    public sealed class AssetClientLifecycle : MonoBehaviour
    {
        AssetClientInstaller.Wiring _wiring;

        internal void Bind(AssetClientInstaller.Wiring wiring) { _wiring = wiring; }

        void OnDestroy()
        {
            if (_wiring != null) _wiring.Shutdown();
            _wiring = null;
        }
    }
}
