using System;
using UnityEngine;

namespace Atlas.Blades.FieldFit
{
    // Master Plan §10.4.5; Brief §5, §8. P3-B03 stub: no texture, Ready = true, empty rects (w = 0 means no badge).
    // P3-B05 fills it. The Create signature there adds the TMP font (DV-B03-a: TextMeshPro absent from the manifest).
    internal sealed class LensBadge : IDisposable
    {
        public const float CentreBelowDeg = 15f, WidthDeg = 8f, HeightDeg = 2f;   // DV11
        public const int TexW = 256, TexH = 64;
        public bool Ready => true;
        public Texture Texture => null;
        public Vector4 RectLeft => Vector4.zero;
        public Vector4 RectRight => Vector4.zero;
        public static LensBadge Create(IEyeGeometry left, IEyeGeometry right, LensLut lut) { return new LensBadge(); }
        public void Dispose() { }
    }
}
