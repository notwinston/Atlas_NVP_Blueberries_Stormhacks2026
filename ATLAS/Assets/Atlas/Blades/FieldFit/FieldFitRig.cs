using Atlas.Rendering;
using UnityEngine;

namespace Atlas.Blades.FieldFit
{
    // Master Plan §10.4.3; Brief §4. P3-B02 ships only the interfaces (so the FakeRig test double compiles);
    // PlaceholderRigBinding (P3-B03) is the only type that names lane A.
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

    /// <summary>
    /// R1-R7 over lane A's StereoRig and LensBlit placeholders (public surface only). Replaced file-for-file when lane A's real
    /// rig lands. Deviations DV-B03-b..e in docs/laneB/fieldfit.md: frustum from Camera.projectionMatrix (R5), rung from
    /// AtlasThermal.State (R7), lateral offset by moving the eye camera (R6), eye size from Screen (R3).
    /// </summary>
    internal sealed class PlaceholderRigBinding : IFieldFitRig
    {
        sealed class Geo : IEyeGeometry
        {
            readonly int eye; readonly Vector4 frustum;
            public Geo(int eye, Vector4 f) { this.eye = eye; frustum = f; }
            public Vector4 Frustum => frustum;
            public Vector2 ScreenToEyeUv(Vector2 q) => LensBlit.SourceUV(eye, q);   // R1: the mapping the ordinary blit applies
        }

        readonly RenderTexture[] targets = new RenderTexture[2];
        readonly float[] tanHalf = new float[2];
        readonly float[] normalX = new float[2];
        readonly float[] normalAspect = new float[2];
        readonly bool[] captured = new bool[2];

        public static bool Available => StereoRig.Current != null;
        static StereoRig Rig => StereoRig.Current;
        Camera Eye(int e) => e == 0 ? Rig.LeftEye : Rig.RightEye;

        public Vector3 HeadPosition => Rig.Head.position;                                    // R4
        public QualityRung Rung => (QualityRung)Mathf.Clamp(AtlasThermal.State, 0, 3);       // R7 (DV-B03-c)
        public Vector2Int EyeScreenSize =>                                                   // R3 (DV-B03-e)
            Screen.width > 0 && Screen.height > 0 ? new Vector2Int(Screen.width / 2, Screen.height) : new Vector2Int(1398, 1290);

        public IEyeGeometry EyeGeometry(int eye)                                             // R1 + R5
        {
            var m = Eye(eye).projectionMatrix;
            float l = (-1f + m[0, 2]) / m[0, 0], r = (1f + m[0, 2]) / m[0, 0];
            float b = (-1f + m[1, 2]) / m[1, 1], t = (1f + m[1, 2]) / m[1, 1];
            return new Geo(eye, new Vector4(l, r, b, t));
        }

        void Capture(int e)
        {
            if (captured[e]) return;
            var c = Eye(e);
            normalX[e] = c.transform.localPosition.x; normalAspect[e] = c.aspect; captured[e] = true;
        }

        public float NormalLateralOffset(int eye) { Capture(eye); return normalX[eye]; }     // R6 (+-IPD/2)

        public void SetEyeOverride(int eye, RenderTexture target, float tanHalfAngle, float lateralOffsetM)   // R6
        {
            Capture(0); Capture(1);
            targets[eye] = target; tanHalf[eye] = tanHalfAngle;
            var c = Eye(eye);
            var p = c.transform.localPosition; p.x = lateralOffsetM; c.transform.localPosition = p;
            if (targets[0] == null || targets[1] == null) return;
            Rig.OverrideEyeTargets(targets[0], targets[1], 2f * Mathf.Atan(tanHalf[0]) * Mathf.Rad2Deg);
            // DV-B03-d: the placeholder keeps its explicit aspect; the wide source is square.
            Rig.LeftEye.ResetAspect(); Rig.RightEye.ResetAspect();
        }

        public void ClearEyeOverrides()                                                      // R6
        {
            if (Rig == null) return;
            Rig.ClearEyeTargetOverride();
            for (int e = 0; e < 2; e++)
            {
                if (!captured[e]) continue;
                var c = Eye(e);
                var p = c.transform.localPosition; p.x = normalX[e]; c.transform.localPosition = p;
                c.aspect = normalAspect[e];
                targets[e] = null;
            }
        }

        public void SetFinalBlitMaterial(Material m) { LensBlit.FinalBlitOverride = m; }     // R2
        public void ClearFinalBlitMaterial() { LensBlit.FinalBlitOverride = null; }          // R2
    }
}
