using System;
using TMPro;
using UnityEngine;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>Head-locked read-out (MP §10.2.2 beat g numbers; D12). TextMeshPro, allocation-free SetText, only when a number changes (MP §8.1).</summary>
    public sealed class NeedleSkyPanel : IDisposable
    {
        const string Format = "Clearance {0:1} mm, minimum over 10 phases\nRobust windows {1:1} % of the sky (at least 5 mm in every phase; teaching threshold)\nMarked {2:0} of 3";
        const string FormatDone = "Clearance {0:1} mm, minimum over 10 phases\nRobust windows {1:1} % of the sky (at least 5 mm in every phase; teaching threshold)\nTriangle angle sum {2:1}\u00b0";
        readonly GameObject _go;
        readonly Transform _tf;
        readonly TextMeshPro _tmp;
        float _a = float.NaN, _b = float.NaN, _c = float.NaN;
        bool _done;
        static readonly float Down = Mathf.Sin(NeedleSkyConstants.PanelBelowDeg * Mathf.Deg2Rad), Fwd = Mathf.Cos(NeedleSkyConstants.PanelBelowDeg * Mathf.Deg2Rad);

        public NeedleSkyPanel(TMP_FontAsset fontOrNull, Transform parent)
        {
            _go = new GameObject("NeedleSkyPanel");
            _tf = _go.transform;
            if (parent != null) _tf.SetParent(parent, false);
            _tmp = _go.AddComponent<TextMeshPro>();
            if (fontOrNull != null) _tmp.font = fontOrNull;
            _tmp.fontSize = NeedleSkyConstants.PanelFontSize;
            _tmp.richText = false;
            _tmp.alignment = TextAlignmentOptions.Center;
            _tmp.textWrappingMode = TextWrappingModes.NoWrap;
            _go.SetActive(false);
        }

        public TextMeshPro Text => _tmp;
        public void SetVisible(bool v) { if (_go != null && _go.activeSelf != v) _go.SetActive(v); }

        public void Present(in NeedleSkyFrame f)
        {
            Quaternion q = f.HeadRotation;
            Vector3 p = (Vector3)f.HeadPosition + q * new Vector3(0f, -Down, Fwd) * NeedleSkyConstants.PanelDistanceM;
            _tf.SetPositionAndRotation(p, q);
            var r = f.Readout;
            bool done = f.PickCount == NeedleSkyConstants.PicksPerCheck && !float.IsNaN(r.AngleSumShownDeg);
            float c = done ? r.AngleSumShownDeg : r.Picks;
            if (r.ClearanceShownMm != _a || r.RobustSharePercent != _b || c != _c || done != _done)
            {
                _a = r.ClearanceShownMm; _b = r.RobustSharePercent; _c = c; _done = done;
                _tmp.SetText(done ? FormatDone : Format, _a, _b, _c);
            }
        }

        public void Dispose() { if (_go == null) return; if (Application.isPlaying) UnityEngine.Object.Destroy(_go); else UnityEngine.Object.DestroyImmediate(_go); }
    }
}
