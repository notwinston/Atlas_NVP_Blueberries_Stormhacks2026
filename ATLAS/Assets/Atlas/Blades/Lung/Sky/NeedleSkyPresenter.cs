using System;
using UnityEngine;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>Product presenter: sphere view + read-out panel.</summary>
    public sealed class NeedleSkyPresenter : INeedleSkyPresenter, IDisposable
    {
        readonly NeedleSkyView _view;
        readonly NeedleSkyPanel _panel;

        public NeedleSkyPresenter(SkyMaps maps, NeedleSkySettings settings, Transform parent)
        {
            _view = new NeedleSkyView(maps, settings.skyShader, parent);   // textures are uploaded here, once
            _panel = new NeedleSkyPanel(settings.font, parent);
        }

        public NeedleSkyView View => _view;
        public NeedleSkyPanel Panel => _panel;
        public void SetVisible(bool visible) { _view.SetVisible(visible); _panel.SetVisible(visible); }
        public void Present(in NeedleSkyFrame frame) { _view.Present(frame); _panel.Present(frame); }
        public void Dispose() { _view.Dispose(); _panel.Dispose(); }
    }
}
