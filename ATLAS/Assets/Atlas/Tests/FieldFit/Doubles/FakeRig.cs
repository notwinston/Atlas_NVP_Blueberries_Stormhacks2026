using UnityEngine;

namespace Atlas.Blades.FieldFit.Tests
{
    // Master Plan §10.4.3; test double for IFieldFitRig (Brief §3).
    internal sealed class FakeRig : IFieldFitRig
    {
        public Vector3 HeadPosition { get; set; }
        public QualityRung Rung { get; set; }
        public Vector2Int EyeScreenSize { get; set; } = TestGeometry.EyeScreen;
        public IEyeGeometry[] Geometry = { TestGeometry.Dtest, TestGeometry.Dtest };
        public float Ipd = 0.063f;
        public Material BlitMaterial;
        public readonly RenderTexture[] Targets = new RenderTexture[2];
        public readonly float[] TanHalf = new float[2];
        public readonly float[] Lateral = new float[2];
        public int SetMaterialCalls, ClearMaterialCalls, SetOverrideCalls, ClearOverrideCalls;

        public IEyeGeometry EyeGeometry(int eye) => Geometry[eye];
        public float NormalLateralOffset(int eye) => eye == 0 ? -Ipd * 0.5f : Ipd * 0.5f;
        public void SetEyeOverride(int eye, RenderTexture target, float tanHalfAngle, float lateralOffsetM)
        { Targets[eye] = target; TanHalf[eye] = tanHalfAngle; Lateral[eye] = lateralOffsetM; SetOverrideCalls++; }
        public void ClearEyeOverrides() { Targets[0] = Targets[1] = null; ClearOverrideCalls++; }
        public void SetFinalBlitMaterial(Material m) { BlitMaterial = m; SetMaterialCalls++; }
        public void ClearFinalBlitMaterial() { BlitMaterial = null; ClearMaterialCalls++; }
    }
}
