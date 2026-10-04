// The background of every space (Master Plan section 8.1): a vertical gradient made in the shader, with no
// texture. At and below the horizon it equals the fog colour, so geometry at the fog radius cannot be told
// from the background. Used as RenderSettings.skybox, so URP draws it once per eye, after the opaque
// geometry and only where nothing was drawn.
Shader "Hidden/Atlas/Background"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
        }

        Pass
        {
            Name "AtlasBackground"

            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex BackgroundVert
            #pragma fragment BackgroundFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "AtlasCore.hlsl"

            struct BackgroundAttributes
            {
                float3 positionOS : POSITION;
            };

            struct BackgroundVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 viewDirWS  : TEXCOORD0;
            };

            BackgroundVaryings BackgroundVert(BackgroundAttributes input)
            {
                BackgroundVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                // The sky mesh is centred on the camera, so its object position is the view direction.
                output.viewDirWS = TransformObjectToWorldDir(input.positionOS, false);
                return output;
            }

            half4 BackgroundFrag(BackgroundVaryings input) : SV_Target
            {
                return half4(AtlasBackground(normalize(input.viewDirWS)), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
