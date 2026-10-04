// The single final pass of the headset view (Master Plan section 8.2, task P1-A05): samples both eye textures
// with the per-eye barrel pre-distortion of the lens, combined with the upscale to the screen.
// Lives under Resources so that a player build includes it with nothing referencing it.
// The arithmetic repeats LensModel.ScreenToEyeUv; keep the two in step.
Shader "Atlas/LensBlit"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "LensBlit"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Vert, Varyings and _BlitScaleBias (which already corrects V for the target's origin).
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_EyeL);
            TEXTURE2D(_EyeR);

            float4 _LensScreen;     // (screen width, screen height, half lens separation, vertical offset) / screen-to-lens
            float4 _LensK;          // K1, K2, distortion on (1) or plain side by side (0), write sRGB by hand (1)
            float4 _LensFrustumL;   // tanLeft, tanDown, 1 / (tanLeft + tanRight), 1 / (tanDown + tanUp)
            float4 _LensFrustumR;

            half4 Frag(Varyings input) : SV_Target0
            {
                float2 uv = input.texcoord;                 // (0, 0) bottom left of the screen, (1, 1) top right
                bool rightEye = uv.x >= 0.5;

                float2 eyeUv;
                if (_LensK.z > 0.5)
                {
                    // Tangent of the view angle before the lens, about this eye's lens centre.
                    float2 p = (uv - 0.5) * _LensScreen.xy;
                    float2 centre = float2(rightEye ? _LensScreen.z : -_LensScreen.z, _LensScreen.w);
                    float2 r = p - centre;
                    // Tangent of the angle the eye sees through the lens.
                    float r2 = dot(r, r);
                    float2 seen = r * (1.0 + r2 * (_LensK.x + _LensK.y * r2));
                    // Where that direction lies in the eye camera's off-axis frustum.
                    float4 f = rightEye ? _LensFrustumR : _LensFrustumL;
                    eyeUv = (seen + f.xy) * f.zw;
                }
                else
                {
                    eyeUv = float2(uv.x * 2.0 - (rightEye ? 1.0 : 0.0), uv.y);
                }

                float3 colour;
                if (rightEye)
                    colour = SAMPLE_TEXTURE2D_LOD(_EyeR, sampler_LinearClamp, eyeUv, 0).rgb;
                else
                    colour = SAMPLE_TEXTURE2D_LOD(_EyeL, sampler_LinearClamp, eyeUv, 0).rgb;

                // Black outside the eye texture: beyond the lens's field of view.
                float2 inside = step(0.0, eyeUv) * step(eyeUv, 1.0);
                colour *= inside.x * inside.y;

                if (_LensK.w > 0.5)
                    colour = LinearToSRGB(colour);
                return half4(colour, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
