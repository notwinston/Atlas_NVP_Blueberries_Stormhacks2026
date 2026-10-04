Shader "Hidden/Atlas/Tests/AlignedCellsGates"
{
    // The gates include on its own, against the four gate vectors of the frozen block (lane A contract §10.1).
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Gate0;
                float4 _Gate1;
                float4 _Gate2;
                float4 _Gate3;
            CBUFFER_END
            #include "Assets/Atlas/Rendering/Shaders/Resources/AtlasGates.hlsl"

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                AtlasGatesClip(i.positionWS);
                return 1;
            }
            ENDHLSL
        }
    }
}
