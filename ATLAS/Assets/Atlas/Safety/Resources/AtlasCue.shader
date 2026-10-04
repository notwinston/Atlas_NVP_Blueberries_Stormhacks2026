// The Bump Guard warning cue: flat vertex colour, drawn over everything on the head-locked HUD layer
// (Master Plan section 4.10, task P1-C14). Atlas.Safety references no rendering assembly, so the cue brings
// this one small shader. Loaded with Resources.Load<Shader>("AtlasCue"), so it reaches a player build with
// nothing referencing it. The pass has no LightMode tag, so URP draws it as SRPDefaultUnlit; the queue is
// opaque so that the HUD's opaque feature picks it up. No fog and no fade: a warning is never dimmed.
Shader "Atlas/Cue"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Cue"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                half3 colour = input.color.rgb;
                // Vertex colours are authored in sRGB; the project renders in linear space.
                #ifndef UNITY_COLORSPACE_GAMMA
                colour = SRGBToLinear(colour);
                #endif
                output.color = half4(colour, 1.0);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return input.color;
            }
            ENDHLSL
        }
    }
}
