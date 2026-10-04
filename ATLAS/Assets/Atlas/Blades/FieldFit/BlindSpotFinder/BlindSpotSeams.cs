using System;
using UnityEngine;

namespace Atlas.Blades.FieldFit.BlindSpotFinder
{
    public enum EyeSide { Left = 0, Right = 1 }

    /// <summary>Lane A, P1-A04/P1-A05 per-eye control.</summary>
    public interface IBlindSpotRig
    {
        Transform EyeTransform(EyeSide eye);        // that eye camera's transform (+-IPD/2 under the tracked head)
        void SetEyeBlank(EyeSide eye, bool blank);  // true: that eye shows black
    }

    /// <summary>Lane A, P1-A06 shared look (Master Plan §8.1, rule 8).</summary>
    public interface IBlindSpotLook
    {
        Material SurfaceMaterial { get; }   // shared AtlasSurface material, used as is, never modified
        Vector2 BrightUv { get; }           // lightest flat palette swatch
        Vector2 DarkUv { get; }             // darkest (base) palette swatch
    }

    /// <summary>Lane C, P1-C10: the confirm action of the one action map (Master Plan §0.5 rule 5).</summary>
    public interface IBlindSpotTap { event Action Tapped; }

    /// <summary>Lane D, App. B Event {t_us, scene, ev, data}.</summary>
    public interface IBlindSpotEvents { void Emit(string ev, object data); }
}
