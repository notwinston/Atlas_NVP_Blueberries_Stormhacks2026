using System;
using UnityEngine;

namespace Atlas.Telemetry
{
    /// <summary>
    /// The view of NET-04: shows a <see cref="NetworkDisc"/> in a <see cref="FlatTwinPanel"/> (the Spectator's map
    /// panel). One mesh, built in code; <see cref="Apply"/> allocates nothing once the buffers have grown.
    /// </summary>
    public sealed class NetworkDiscPanel : IDisposable
    {
        readonly FlatTwinPanel _panel;
        NetworkDisc _disc;

        public NetworkDiscPanel(Transform parent, string name, float size, bool background, float textHeight)
        {
            _panel = new FlatTwinPanel(parent, name, size, background, textHeight);
        }

        public FlatTwinPanel Panel { get { return _panel; } }
        public NetworkDisc Disc { get { return _disc; } }

        public bool Visible
        {
            get { return _panel.Visible; }
            set { _panel.Visible = value; }
        }

        public void Show(NetworkDisc disc)
        {
            _disc = disc;
            _panel.ShowNetwork(disc);
        }

        /// <summary>Redraws and uploads when the disc is set; call once per map refresh.</summary>
        public void Apply()
        {
            if (_disc == null) return;
            _panel.Apply();
        }

        public void Dispose() { _panel.Dispose(); }
    }
}
