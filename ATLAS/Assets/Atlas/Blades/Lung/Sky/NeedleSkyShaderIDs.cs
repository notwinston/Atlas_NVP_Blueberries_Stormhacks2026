using UnityEngine;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>Property ids of Shaders/NeedleSky.shader (D2: not in AtlasShaderIDs.cs).</summary>
    public static class NeedleSkyShaderIDs
    {
        public static readonly int PhaseTex = Shader.PropertyToID("_PhaseTex");
        public static readonly int RobustTex = Shader.PropertyToID("_RobustTex");
        public static readonly int MmPerUnit = Shader.PropertyToID("_MmPerUnit");
        public static readonly int ThresholdMm = Shader.PropertyToID("_ThresholdMm");
        public static readonly int RobustOn = Shader.PropertyToID("_RobustOn");
        public static readonly int ArcsOn = Shader.PropertyToID("_ArcsOn");
        public static readonly int DebugRaw = Shader.PropertyToID("_DebugRaw");
        public static readonly int PickA = Shader.PropertyToID("_PickA");
        public static readonly int PickB = Shader.PropertyToID("_PickB");
        public static readonly int PickC = Shader.PropertyToID("_PickC");
        public static readonly int Cursor = Shader.PropertyToID("_Cursor");
    }
}
