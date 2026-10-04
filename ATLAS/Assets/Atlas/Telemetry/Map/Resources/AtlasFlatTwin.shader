// The flat-twin mini-map and the Station's indicator panels: flat vertex colour, drawn over everything on the
// head-locked HUD layer or on the Spectator layer (Master Plan rule 6, task P1-D07). Atlas.Telemetry references
// no rendering assembly, so the map brings this one small shader. Loaded with
// Resources.Load<Shader>("AtlasFlatTwin"), so it reaches a player build with nothing referencing it. The pass
// has no LightMode tag, so URP draws it as SRPDefaultUnlit; the queue is opaque so that the HUD's opaque
// feature picks it up. Triangles are drawn in mesh order with no depth test: later entries lie over earlier
// ones. No fog and no fade.
Shader "Atlas/FlatTwin"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "FlatTwin"
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
