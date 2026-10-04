// PLACEHOLDER (lane A, P1-A05): contract surface only; replaced by lane A's implementation
using UnityEngine;

namespace Atlas.Rendering
{
    /// <summary>Lens pre-distortion final-blit hook (Master Plan §8.2 final pass, §10.4.3 render path; deviation PH-11).</summary>
    public static class LensBlit
    {
        /// <summary>Non-null: the one final blit uses this material (FieldFitBlit, P3-B03).</summary>
        public static Material FinalBlitOverride { get; set; }

        /// <summary>The uv in the eye texture that the ordinary blit samples for this point of that eye's screen half. eye 0 = left, 1 = right. Placeholder: identity distortion.</summary>
        public static Vector2 SourceUV(int eye, Vector2 screenUV) => screenUV;
    }
}
