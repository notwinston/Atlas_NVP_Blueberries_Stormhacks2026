// PLACEHOLDER (lane A, P1-A06): contract surface only; replaced by lane A's implementation
// Master Plan §8.3: one shader for every opaque surface. One variant, no keywords, no MaterialPropertyBlock.
Shader "Atlas/AtlasSurface"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _WarpMode ("Warp mode (0 none, 1 hyperbolic, 2 spherical)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "AtlasForward"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma vertex AtlasVert
            #pragma fragment AtlasFrag
            #include "AtlasCore.hlsl"
            ENDHLSL
        }
    }
}
