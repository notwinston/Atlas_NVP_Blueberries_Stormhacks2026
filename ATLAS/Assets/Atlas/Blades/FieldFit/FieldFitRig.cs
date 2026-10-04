using Atlas.Rendering;
using Atlas.World;
using UnityEngine;

namespace Atlas.Blades.FieldFit
{
    // Master Plan §10.4.3; Brief §4. StereoRigBinding is the only type that names lane A.
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
    /// R1-R7 over lane A's StereoRig and LensBlit. Deviations DV-B03-b..e in docs/laneB/fieldfit.md: frustum from the lens
    /// model (R5), rung from AtlasThermal.State (R7), lateral offset by moving the eye camera (R6), eye size from Screen (R3).
    /// The eye-target override is a member of the concrete StereoRig, not of IStereoRig: the rig is the published
    /// IStereoRig's own "Atlas Head" component (or a StereoRig service when one is published).
    /// </summary>
    internal sealed class StereoRigBinding : IFieldFitRig
    {
        sealed class Geo : IEyeGeometry
        {
            readonly int eye; readonly Vector4 frustum;
            public Geo(int eye, Vector4 f) { this.eye = eye; frustum = f; }
            public Vector4 Frustum => frustum;
            public Vector2 ScreenToEyeUv(Vector2 q) => LensBlit.SourceUV(eye, q);   // R1: the mapping the ordinary blit applies
        }

        readonly StereoRig rig;
        readonly RenderTexture[] targets = new RenderTexture[2];
        readonly float[] tanHalf = new float[2];
        readonly float[] normalX = new float[2];
        readonly bool[] captured = new bool[2];

        StereoRigBinding(StereoRig rig) { this.rig = rig; }

        /// <summary>The concrete rig behind the published services; false when the app has none yet.</summary>
        public static bool TryCreate(out StereoRigBinding binding)
        {
            StereoRig r;
            if (!AtlasServices.TryGet(out r) && AtlasServices.TryGet(out IStereoRig published) && published.Head != null)
                r = published.Head.GetComponent<StereoRig>();   // GuardedStereoRig forwards Head: the concrete rig's own transform
            binding = r != null ? new StereoRigBinding(r) : null;
            return binding != null;
        }

        static Eye Side(int e) => e == 0 ? Eye.Left : Eye.Right;
        Camera Cam(int e) => rig.GetEyeCamera(Side(e));

        public Vector3 HeadPosition => rig.Head.position;                                    // R4
        public QualityRung Rung => (QualityRung)Mathf.Clamp(AtlasThermal.State, 0, 3);       // R7 (DV-B03-c)
        public Vector2Int EyeScreenSize =>                                                   // R3 (DV-B03-e)
            Screen.width > 0 && Screen.height > 0 ? new Vector2Int(Screen.width / 2, Screen.height) : new Vector2Int(1398, 1290);

        public IEyeGeometry EyeGeometry(int eye)                                             // R1 + R5
        {
            // The lens model's tangents, not the camera's projection: that one is the symmetric wide source while overridden.
            EyeFrustum f = rig.GetEyeFrustum(Side(eye));
            return new Geo(eye, new Vector4(-f.TanLeft, f.TanRight, -f.TanDown, f.TanUp));
        }

        void Capture(int e)
        {
            if (captured[e]) return;
            normalX[e] = Cam(e).transform.localPosition.x; captured[e] = true;
        }

        public float NormalLateralOffset(int eye) { Capture(eye); return normalX[eye]; }     // R6 (+-IPD/2)

        public void SetEyeOverride(int eye, RenderTexture target, float tanHalfAngle, float lateralOffsetM)   // R6
        {
            Capture(0); Capture(1);
            targets[eye] = target; tanHalf[eye] = tanHalfAngle;
            var t = Cam(eye).transform;
            var p = t.localPosition; p.x = lateralOffsetM; t.localPosition = p;
            if (targets[0] == null || targets[1] == null) return;
            // The rig's projection is explicit: a symmetric frustum of this field of view and each target's own aspect.
            rig.OverrideEyeTargets(targets[0], targets[1], 2f * Mathf.Atan(tanHalf[0]) * Mathf.Rad2Deg);
        }

        public void ClearEyeOverrides()                                                      // R6
        {
            if (rig == null) return;
            rig.ClearEyeTargetOverride();
            for (int e = 0; e < 2; e++)
            {
                targets[e] = null;
                if (!captured[e]) continue;
                var t = Cam(e).transform;
                var p = t.localPosition; p.x = normalX[e]; t.localPosition = p;
            }
        }

        public void SetFinalBlitMaterial(Material m) { LensBlit.FinalBlitOverride = m; }     // R2
        public void ClearFinalBlitMaterial() { LensBlit.FinalBlitOverride = null; }          // R2
    }
}
