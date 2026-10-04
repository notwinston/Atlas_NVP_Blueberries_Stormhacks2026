// The one surface shader of ATLAS (Master Plan sections 8.1 and 8.3, decision D23): palette colour, baked
// vertex shade, fwidth lines, fog to the one fog colour, emissive accents. One pass, one variant, no keyword.
// Materials come from MaterialPool only; per-tile, per-room and per-object values live in UnityPerMaterial.
Shader "Atlas/Surface"
{
    Properties
    {
        _BaseTint ("Tint (rgb, linear) and opacity (a)", Vector) = (1, 1, 1, 1)
        _LineColor ("Line colour (rgb, linear)", Vector) = (0.159, 0.392, 0.578, 1)
        _LineParams ("Lines: minor width, major width, minor strength, major strength", Vector) = (1, 2, 0, 0)
        _LineMajor ("Major line period (xy)", Vector) = (0, 0, 0, 0)
        _PortalPlane ("Portal plane (n.xyz, d)", Vector) = (0, 0, 0, 1)
        _Gate0 ("Gate 0 (post.x, post.z, sign)", Vector) = (0, 0, 0, 0)
        _Gate1 ("Gate 1 (post.x, post.z, sign)", Vector) = (0, 0, 0, 0)
        _Gate2 ("Gate 2 (post.x, post.z, sign)", Vector) = (0, 0, 0, 0)
        _Gate3 ("Gate 3 (post.x, post.z, sign)", Vector) = (0, 0, 0, 0)
        _SurfaceParams ("Emissive gain, fog weight, near fade (m), fade weight", Vector) = (1, 1, 0, 1)
        _WarpMode ("Warp mode (0 none, 1 hyperbolic, 2 spherical, 3 spherical 3-space)", Float) = 0
        _Cull ("Cull (0 off, 1 front, 2 back)", Float) = 2
        _StencilRef ("Stencil reference (0..15)", Float) = 0
        _StencilComp ("Stencil comparison (8 = always, 3 = equal)", Float) = 8
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "AtlasForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            Blend Off

            // Never writes the stencil: the doorway mask does. A Render Objects stencil override replaces this block.
            Stencil
            {
                Ref [_StencilRef]
                Comp [_StencilComp]
                Pass Keep
                Fail Keep
                ZFail Keep
                ReadMask 255
                WriteMask 255
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex AtlasVert
            #pragma fragment AtlasFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "AtlasCore.hlsl"
            #include "AtlasWarp.hlsl"
            #include "AtlasPortalClip.hlsl"
            #include "AtlasGates.hlsl"

            struct AtlasAttributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                float2 uv0        : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
            };

            struct AtlasVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv0        : TEXCOORD1;
                float2 lineCoord  : TEXCOORD2;
                half4  color      : COLOR;
            };

            AtlasVaryings AtlasVert(AtlasAttributes input)
            {
                AtlasVaryings output;
                float3 positionWS = AtlasWarp(input.positionOS);          // the only place a position is made
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.uv0        = input.uv0;
                output.lineCoord  = input.uv1;
                output.color      = input.color;
                return output;
            }

            half4 AtlasFrag(AtlasVaryings input) : SV_Target
            {
                AtlasGatesClip(input.positionWS);                         // first, for every material
                AtlasPortalClip(input.positionWS);
                AtlasWarpClip(input.positionWS);                          // the far side of the sphere
                return AtlasShade(input.positionWS, input.uv0, input.lineCoord, input.color, input.positionCS);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
