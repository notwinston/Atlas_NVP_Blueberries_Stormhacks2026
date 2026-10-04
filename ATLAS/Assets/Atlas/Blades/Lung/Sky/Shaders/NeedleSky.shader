// Needle sky (MP §10.2.5, brief G7 §5.4, D1): inverted sphere sampling 2 R8 data textures; no keywords; SRP-Batcher compatible.
Shader "Atlas/NeedleSky"
{
    Properties
    {
        [NoScaleOffset] _PhaseTex ("Phase risk (R8)", 2D) = "white" {}
        [NoScaleOffset] _RobustTex ("Robust (R8)", 2D) = "black" {}
        _MmPerUnit ("mm per byte", Float) = 0.2
        _ThresholdMm ("Threshold mm", Float) = 5
        _RobustOn ("Robust layer", Float) = 1
        _ArcsOn ("Arcs", Float) = 0
        _DebugRaw ("Debug raw (GPU test)", Float) = 0
        _ColClear ("Clear", Color) = (0.35, 0.37, 0.40, 1)
        _ColBlocked ("Blocked", Color) = (0.55, 0.10, 0.10, 1)
        _ColRobust ("Robust", Color) = (0.15, 0.55, 0.25, 1)
        _ColMark ("Mark", Color) = (0.95, 0.95, 0.95, 1)
        _PickA ("Pick A", Vector) = (0, 0, 1, 0)
        _PickB ("Pick B", Vector) = (0, 0, 1, 0)
        _PickC ("Pick C", Vector) = (0, 0, 1, 0)
        _Cursor ("Cursor", Vector) = (0, 0, 1, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "NeedleSky"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_PhaseTex);
            TEXTURE2D(_RobustTex);
            SAMPLER(point_clamp_sampler);

            CBUFFER_START(UnityPerMaterial)
                float _MmPerUnit, _ThresholdMm, _RobustOn, _ArcsOn, _DebugRaw;
                float4 _ColClear, _ColBlocked, _ColRobust, _ColMark, _PickA, _PickB, _PickC, _Cursor;
            CBUFFER_END

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            // angle between unit vectors, stable: 2 asin(|d - p| / 2)
            float AngleTo(float3 d, float3 p) { return 2.0 * asin(min(1.0, length(d - p) * 0.5)); }

            // arc P->Q: distance inside the lune to the circle, else to the nearer endpoint (GreatCircleArc.DistanceRad mirror)
            float ArcDist(float3 d, float3 P, float3 Q)
            {
                float3 n = cross(P, Q);
                float nl = length(n);
                if (nl < 1e-6) return min(AngleTo(d, P), AngleTo(d, Q));
                n /= nl;
                bool inLune = dot(d, cross(n, P)) >= 0.0 && dot(d, cross(Q, n)) >= 0.0;
                return inLune ? asin(min(1.0, abs(dot(d, n)))) : min(AngleTo(d, P), AngleTo(d, Q));
            }

            float4 frag(Varyings i) : SV_Target
            {
                // per eye, so features sit at infinity (D11)
                float3 d = normalize(i.positionWS - _WorldSpaceCameraPos);
                float u = atan2(d.x, d.z) / (2.0 * PI) + 0.5;                 // az = atan2(x, z) (D3)
                float v = 0.5 - asin(clamp(d.y, -1.0, 1.0)) / PI;             // file row 0 = el +90 -> v = 0
                float2 uv = float2(u, v);
                float ph = SAMPLE_TEXTURE2D_LOD(_PhaseTex, point_clamp_sampler, uv, 0).r;
                float rb = SAMPLE_TEXTURE2D_LOD(_RobustTex, point_clamp_sampler, uv, 0).r;
                if (_DebugRaw > 0.5) return float4(ph, rb, 0, 1);

                float c = ph * 255.0 * _MmPerUnit;
                bool rob = rb > 0.5 / 255.0;
                float3 col = (_RobustOn > 0.5 && rob) ? _ColRobust.rgb : ((c >= _ThresholdMm - 1e-4) ? _ColClear.rgb : _ColBlocked.rgb);

                // overlays (anti-aliased with fwidth)
                float pick = 0.0;
                float aw;
                float rad = PI / 180.0;
                if (_ArcsOn > 0.5)
                {
                    float a = min(ArcDist(d, _PickA.xyz, _PickB.xyz), min(ArcDist(d, _PickB.xyz, _PickC.xyz), ArcDist(d, _PickC.xyz, _PickA.xyz)));
                    aw = fwidth(a);
                    pick = 1.0 - smoothstep(0.25 * rad - aw, 0.25 * rad + aw, a);   // D10: half width 0.25 deg
                }
                if (_PickA.w > 0.5) { float a = AngleTo(d, _PickA.xyz); aw = fwidth(a); pick = max(pick, 1.0 - smoothstep(0.75 * rad - aw, 0.75 * rad + aw, a)); }
                if (_PickB.w > 0.5) { float a = AngleTo(d, _PickB.xyz); aw = fwidth(a); pick = max(pick, 1.0 - smoothstep(0.75 * rad - aw, 0.75 * rad + aw, a)); }
                if (_PickC.w > 0.5) { float a = AngleTo(d, _PickC.xyz); aw = fwidth(a); pick = max(pick, 1.0 - smoothstep(0.75 * rad - aw, 0.75 * rad + aw, a)); }
                col = lerp(col, _ColMark.rgb, pick);

                if (_Cursor.w > 0.5)
                {
                    float a = AngleTo(d, _Cursor.xyz);
                    aw = fwidth(a);
                    float ring = 1.0 - smoothstep(0.075 * rad - aw, 0.075 * rad + aw, abs(a - 1.5 * rad));
                    col = lerp(col, _ColMark.rgb, ring);
                }
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
