using UnityEngine;

namespace Atlas.Blades.FieldFit
{
    // Master Plan §10.4.3; Brief §4. P3-B02 ships only the interfaces (so the FakeRig test double compiles);
    // P3-B03 appends PlaceholderRigBinding, the only type that names lane A.
    internal enum QualityRung { Nominal = 0, Fair = 1, Serious = 2, Last = 3, High = 4 }

    internal interface IFieldFitRig
    {
        Vector3 HeadPosition { get; }                                   // R4
        QualityRung Rung { get; }                                       // R7
        Vector2Int EyeScreenSize { get; }                               // R3
        IEyeGeometry EyeGeometry(int eye);                              // R1 + R5; eye 0 = left
        void SetEyeOverride(int eye, RenderTexture target, float tanHalfAngle, float lateralOffsetM); // R6
        void ClearEyeOverrides();                                       // R6
        float NormalLateralOffset(int eye);                             // R6 (+-IPD/2)
        void SetFinalBlitMaterial(Material m);                          // R2
        void ClearFinalBlitMaterial();                                  // R2
    }
}
