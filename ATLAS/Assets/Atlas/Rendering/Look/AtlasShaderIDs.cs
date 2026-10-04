using UnityEngine;

namespace Atlas.Rendering
{
    /// <summary>
    /// Every shader property id of the look, the warp, the portals and the lens blit (lane A contract,
    /// section 10.6). The list is complete: nobody calls <see cref="Shader.PropertyToID(string)"/> elsewhere.
    /// </summary>
    public static class AtlasShaderIDs
    {
        // Per material, in the UnityPerMaterial block of AtlasCore.hlsl.
        public static readonly int Anchor = Shader.PropertyToID("_Anchor");
        public static readonly int WarpMode = Shader.PropertyToID("_WarpMode");
        public static readonly int Gate0 = Shader.PropertyToID("_Gate0");
        public static readonly int Gate1 = Shader.PropertyToID("_Gate1");
        public static readonly int Gate2 = Shader.PropertyToID("_Gate2");
        public static readonly int Gate3 = Shader.PropertyToID("_Gate3");
        public static readonly int BaseTint = Shader.PropertyToID("_BaseTint");
        public static readonly int LineColor = Shader.PropertyToID("_LineColor");
        public static readonly int LineParams = Shader.PropertyToID("_LineParams");
        public static readonly int LineMajor = Shader.PropertyToID("_LineMajor");
        public static readonly int PortalPlane = Shader.PropertyToID("_PortalPlane");
        public static readonly int SurfaceParams = Shader.PropertyToID("_SurfaceParams");

        // Render state of a material: read by ShaderLab only, not in the block.
        public static readonly int Cull = Shader.PropertyToID("_Cull");
        public static readonly int StencilRef = Shader.PropertyToID("_StencilRef");
        public static readonly int StencilComp = Shader.PropertyToID("_StencilComp");

        // Globals, one value for both eyes.
        public static readonly int Palette = Shader.PropertyToID("_AtlasPalette");
        public static readonly int HeadPosWS = Shader.PropertyToID("_HeadPosWS");
        public static readonly int HypL = Shader.PropertyToID("_HypL");
        public static readonly int FogColor = Shader.PropertyToID("_AtlasFogColor");
        public static readonly int FogParams = Shader.PropertyToID("_AtlasFogParams");
        public static readonly int SkyTop = Shader.PropertyToID("_AtlasSkyTop");
        public static readonly int Shade = Shader.PropertyToID("_AtlasShade");
        public static readonly int LineWidth = Shader.PropertyToID("_AtlasLineWidth");
        public static readonly int Fade = Shader.PropertyToID("_AtlasFade");
        public static readonly int Time = Shader.PropertyToID("_AtlasTime");

        // Lens blit.
        public static readonly int EyeLeft = Shader.PropertyToID("_AtlasEyeLeft");
        public static readonly int EyeRight = Shader.PropertyToID("_AtlasEyeRight");
        public static readonly int LensK = Shader.PropertyToID("_AtlasLensK");
        public static readonly int LensGeometry = Shader.PropertyToID("_AtlasLensGeometry");
        public static readonly int LensFov = Shader.PropertyToID("_AtlasLensFov");
    }
}
