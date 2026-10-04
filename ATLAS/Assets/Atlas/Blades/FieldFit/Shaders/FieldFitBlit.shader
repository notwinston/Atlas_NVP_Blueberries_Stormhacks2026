// Master Plan §10.4.3 (composed final blit), §8.2 (the one final pass); Brief §7.3. No keywords.
// One procedural full-screen triangle over the whole screen; the left eye is uv.x < 0.5.
Shader "Hidden/Atlas/FieldFitBlit"
{
    Properties
    {
        _FFSourceL ("Source L", 2D) = "black" {}
        _FFSourceR ("Source R", 2D) = "black" {}
        _FFLutL ("Lookup L", 2D) = "black" {}
        _FFLutR ("Lookup R", 2D) = "black" {}
        _FFBadge ("Badge", 2D) = "white" {}
        _FFTanE ("tan E", Float) = 1.3763819
        _FFFrustumL ("Frustum L (l,r,b,t)", Vector) = (-10000, 10000, -10000, 10000)
        _FFFrustumR ("Frustum R (l,r,b,t)", Vector) = (-10000, 10000, -10000, 10000)
        _FFBadgeRectL ("Badge rect L (xMin,yMin,w,h)", Vector) = (0, 0, 0, 0)
        _FFBadgeRectR ("Badge rect R (xMin,yMin,w,h)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "FieldFitBlit"
            ZTest Always ZWrite Off Cull Off Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            TEXTURE2D(_FFSourceL); SAMPLER(sampler_FFSourceL);   // bilinear, clamp (set on the RenderTexture)
            TEXTURE2D(_FFSourceR); SAMPLER(sampler_FFSourceR);
            TEXTURE2D(_FFLutL);                                  // Load only, never Gather (DV7)
            TEXTURE2D(_FFLutR);
            TEXTURE2D(_FFBadge); SAMPLER(sampler_FFBadge);
            float _FFTanE;
            float4 _FFFrustumL, _FFFrustumR;
            float4 _FFBadgeRectL, _FFBadgeRectR;

            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                o.uv = GetFullScreenTriangleTexCoord(vertexID);
                return o;
            }

            float2 LoadLut(int eye, int2 c)
            {
                c = clamp(c, 0, 511);
                return eye == 0 ? LOAD_TEXTURE2D(_FFLutL, c).rg : LOAD_TEXTURE2D(_FFLutR, c).rg;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                int eye = i.uv.x >= 0.5 ? 1 : 0;
                float2 q = float2(2.0 * i.uv.x - eye, i.uv.y);               // per-eye screen uv, origin bottom-left

                // 1-2. four lookup texels, bilinear weights
                float2 p = q * 512.0 - 0.5;
                float2 bse = floor(p);
                float2 f = p - bse;
                int2 c0 = (int2)bse;
                float2 v[4];
                v[0] = LoadLut(eye, c0);
                v[1] = LoadLut(eye, c0 + int2(1, 0));
                v[2] = LoadLut(eye, c0 + int2(0, 1));
                v[3] = LoadLut(eye, c0 + int2(1, 1));
                float w[4];
                w[0] = (1.0 - f.x) * (1.0 - f.y); w[1] = f.x * (1.0 - f.y); w[2] = (1.0 - f.x) * f.y; w[3] = f.x * f.y;

                // 3. lost <=> R < -0.75
                bool lost[4];
                int nLost = 0;
                [unroll] for (int k = 0; k < 4; k++) { lost[k] = v[k].x < -0.75; nLost += lost[k] ? 1 : 0; }

                // 4. the uv (the distance rule, DV7)
                bool visible = true;
                float2 uv = 0;
                if (nLost == 0)
                {
                    uv = w[0] * v[0] + w[1] * v[1] + w[2] * v[2] + w[3] * v[3];
                }
                else if (nLost == 4)
                {
                    visible = false;
                }
                else
                {
                    // texel k: 0 = (0,0), 1 = (1,0), 2 = (0,1), 3 = (1,1)
                    float vk[4];
                    [unroll] for (int a = 0; a < 4; a++)
                    {
                        if (lost[a]) { vk[a] = v[a].y; }
                        else
                        {
                            float m = 1e9;
                            [unroll] for (int b = 0; b < 4; b++)
                            {
                                if (!lost[b]) continue;
                                float2 dd = float2(a & 1, a >> 1) - float2(b & 1, b >> 1);
                                float dist = (dd.x != 0.0 && dd.y != 0.0) ? 1.41421356 : 1.0;
                                m = min(m, dist + v[b].y);
                            }
                            vk[a] = m;
                        }
                    }
                    float vv = w[0] * vk[0] + w[1] * vk[1] + w[2] * vk[2] + w[3] * vk[3];
                    float sw = 0; float2 su = 0;
                    [unroll] for (int c = 0; c < 4; c++) { float ww = lost[c] ? 0.0 : w[c]; sw += ww; su += ww * v[c]; }
                    if (vv < 0.0 || sw <= 1e-6) visible = false; else uv = su / sw;
                }

                // 5. tangent against the frustum, uv against [0,1]^2
                float3 col = 0;
                if (visible)
                {
                    float2 t = (2.0 * uv - 1.0) * _FFTanE;
                    float4 fr = eye == 0 ? _FFFrustumL : _FFFrustumR;
                    visible = t.x >= fr.x && t.x <= fr.y && t.y >= fr.z && t.y <= fr.w && all(uv >= 0.0) && all(uv <= 1.0);
                }
                // 6. source (sRGB target: sampled and written in linear)
                if (visible)
                    col = eye == 0 ? SAMPLE_TEXTURE2D_LOD(_FFSourceL, sampler_FFSourceL, uv, 0).rgb
                                   : SAMPLE_TEXTURE2D_LOD(_FFSourceR, sampler_FFSourceR, uv, 0).rgb;

                // 7. badge, composited last so the lens can never hide it (Master Plan §10.4.5)
                float4 rect = eye == 0 ? _FFBadgeRectL : _FFBadgeRectR;
                if (rect.z > 0.0)
                {
                    float2 bq = (q - rect.xy) / rect.zw;
                    if (all(bq >= 0.0) && all(bq <= 1.0))
                    {
                        float4 bd = SAMPLE_TEXTURE2D_LOD(_FFBadge, sampler_FFBadge, bq, 0);
                        col = lerp(col, bd.rgb, bd.a);
                    }
                }
                return float4(col, 1.0);   // 8.
            }
            ENDHLSL
        }
    }
}
