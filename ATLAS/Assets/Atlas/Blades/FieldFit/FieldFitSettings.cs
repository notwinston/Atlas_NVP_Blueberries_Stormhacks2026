using UnityEngine;

namespace Atlas.Blades.FieldFit
{
    // Master Plan §10.4.3, §10.4.6; Brief §5, §12. The shader is reached through this asset (never Shader.Find, Brief §14).
    // DV-B03-a: no badgeFont field yet. TextMeshPro is not in Packages/manifest.json (B05 needs a lead decision).
    [CreateAssetMenu(menuName = "Atlas/FieldFit Settings", fileName = "FieldFitSettings")]
    public sealed class FieldFitSettings : ScriptableObject
    {
        public Shader blitShader;                   // Hidden/Atlas/FieldFitBlit
        public Texture2D fieldLossMask;             // R11: lane D's tunnel preset (512^2), assigned when present; B04 consumes it
        public float maskHalfWidthDeg = 30f;        // DV6, Brief §7.4
        public bool centreEyeSource = false;        // Master Plan §10.4.9 fallback: both eyes rendered from the centre eye
    }
}
