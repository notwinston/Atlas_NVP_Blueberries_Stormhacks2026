// The doorway mask of the stencil portals (Master Plan decision D16, lane A contract section 12.2).
// Drawn by the Render Objects feature "Atlas Portal Masks" after the walker's own room, in two steps that
// are two materials of this one pass:
//   mark   writes the doorway's stencil reference where the doorway is visible, at the doorway's depth;
//   reset  then pushes the depth to the far plane wherever the stencil holds that reference, so the room
//          behind the doorway can be drawn there, and the background fills what that room leaves empty.
// No colour is written. One pass, one variant, no keyword.
Shader "Hidden/Atlas/PortalMask"
{
    Properties
    {
        _MaskParams ("x: 1 puts the vertices on the far plane (reset)", Vector) = (0, 0, 0, 0)
        _StencilRef ("Stencil reference (1..15)", Float) = 1
        _StencilComp ("Stencil comparison (8 = always, 3 = equal)", Float) = 8
        _StencilPass ("Stencil pass operation (2 = replace, 0 = keep)", Float) = 2
        _MaskZTest ("Depth test (4 = less or equal, 8 = always)", Float) = 4
        _MaskZWrite ("Depth write (0 off, 1 on)", Float) = 1
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
            Name "AtlasPortalMask"
            Tags { "LightMode" = "UniversalForward" }

            // A doorway is marked from the side the walker is on; the portal box is seen from inside.
            Cull Off
            ZWrite [_MaskZWrite]
            ZTest [_MaskZTest]
            ColorMask 0
            Blend Off

            Stencil
            {
                Ref [_StencilRef]
                Comp [_StencilComp]
                Pass [_StencilPass]
                Fail Keep
                ZFail Keep
                ReadMask 255
                WriteMask 255
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MaskVert
            #pragma fragment MaskFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MaskParams;
            CBUFFER_END

            struct MaskAttributes
            {
                float3 positionOS : POSITION;
            };

            struct MaskVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            MaskVaryings MaskVert(MaskAttributes input)
            {
                MaskVaryings output;
                float4 positionCS = TransformObjectToHClip(input.positionOS);
                // Reset: every fragment lands exactly on the far plane, which is what the background tests against.
                if (_MaskParams.x > 0.5)
                    positionCS.z = UNITY_RAW_FAR_CLIP_VALUE * positionCS.w;
                output.positionCS = positionCS;
                return output;
            }

            half4 MaskFrag(MaskVaryings input) : SV_Target
            {
                return half4(0.0, 0.0, 0.0, 0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
