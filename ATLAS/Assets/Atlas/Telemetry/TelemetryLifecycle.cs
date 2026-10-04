using UnityEngine;

namespace Atlas.Telemetry
{
    /// <summary>
    /// Brings pause, resume and quit to the telemetry systems. It has no <c>Update</c>: all per-frame work of
    /// the link and the probe runs from the frame loop's step 8.
    /// </summary>
    public sealed class TelemetryLifecycle : MonoBehaviour
    {
        AtlasTelemetry _telemetry;

        internal void Bind(AtlasTelemetry telemetry) { _telemetry = telemetry; }

        void OnApplicationPause(bool paused)
        {
            if (_telemetry == null) return;
            if (paused) _telemetry.OnPause();
            else _telemetry.OnResume();
        }

        void OnApplicationQuit()
        {
            if (_telemetry != null) _telemetry.Shutdown();
        }

        void OnDestroy()
        {
            if (_telemetry != null) _telemetry.Shutdown();
        }
    }
}
