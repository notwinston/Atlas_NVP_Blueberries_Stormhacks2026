// The text of a panel that stands within reach of the hands (documents/ATLAS_Hands_Design.md section 6.1).
// It is Unity's built-in font shader ("GUI/Text Shader": vertex colour times _Color, alpha from the font
// texture, blended, both faces, no depth write) with one word changed: the depth test is on. The built-in
// shader passes the depth test always, which is right on the head-locked layer and wrong on the world layer,
// where a near panel stands: its text would paint over a finger held in front of it and over the hand.
// Written for the Universal pipeline as the look's other shaders are, with no keyword at all: the built-in
// one carries a stereo keyword set, and the two eye cameras here each draw their own texture.
// Loaded with Resources.Load<Shader>("AtlasNearText"), so it reaches a player build with nothing referencing it.
// The pass has no LightMode tag, so the pipeline draws it as SRPDefaultUnlit, in the transparent queue, after
// the opaque panels have written their depth.
Shader "Atlas/NearText"
{
    Properties
    {
        _MainTex("Font Texture", 2D) = "white" {}
        _Color("Text Colour", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }
        Pass
        {
            Name "NearText"
            ZTest LEqual
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // As the built-in shader: the text's own colour, with no colour-space conversion, so near text
                // looks as the same text does a metre away.
                output.color = input.color * _Color;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 colour = input.color;
                // The font texture holds the glyphs' coverage in its alpha channel.
                colour.a *= SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                return colour;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
