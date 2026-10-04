using TMPro;
using UnityEngine;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>References NeedleSky.shader so the player build includes it (brief §11.16). font null = TMP default.</summary>
    public sealed class NeedleSkySettings : ScriptableObject
    {
        public Shader skyShader;
        public TMP_FontAsset font;
    }
}
