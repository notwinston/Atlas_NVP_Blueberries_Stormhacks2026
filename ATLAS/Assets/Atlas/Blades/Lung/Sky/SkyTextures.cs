using System;
using UnityEngine;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>The 11 R8 data textures (MP §8.1 data textures; §10.2.5): created and uploaded once, never again.</summary>
    public sealed class SkyTextures : IDisposable
    {
        readonly Texture2D[] _phase = new Texture2D[SkyMaps.Phases];
        Texture2D _robust;

        public SkyTextures(SkyMaps maps)
        {
            for (int k = 0; k < SkyMaps.Phases; k++) _phase[k] = Make(maps.RiskBytes(k), "NeedleSky_risk_p" + k);
            _robust = Make(maps.RobustBytes, "NeedleSky_robust");
            Uploads = SkyMaps.Phases + 1;
        }

        static Texture2D Make(byte[] bytes, string name)
        {
            var t = new Texture2D(SkyMaps.Width, SkyMaps.Height, TextureFormat.R8, false, true)
            { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            t.LoadRawTextureData(bytes);                       // file row 0 -> v = 0, no flip (brief §5.1)
            t.Apply(false, true);
            return t;
        }

        public Texture2D Phase(int k) => _phase[k];
        public Texture2D Robust => _robust;
        public int Uploads { get; }

        public void Dispose()
        {
            for (int k = 0; k < _phase.Length; k++) { Kill(_phase[k]); _phase[k] = null; }
            Kill(_robust); _robust = null;
        }

        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
