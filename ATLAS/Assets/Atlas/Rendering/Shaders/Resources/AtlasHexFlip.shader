// The hexagon reveal between two spaces (HexTransition). Each tile of the dome is a card: its front shows
// the still of the old space, its back the still of the new one, both painted where they stood seen from
// the head, so a tile face on cannot be told from the world it covers. A tile turns about its axis over
// FLIP_SECONDS once the reveal reaches its delay (_TileDelays: its ring in the ripple from the tile the walker
// looks at), and when it has turned over it is dropped: the live space behind it looks the same as its back. One mesh, one draw, two texture reads per pixel at most, no fog and
// no fade. Loaded with Resources.Load<Shader>("AtlasHexFlip").
//
// The dome draws before the world, with its depth in a thin slab next to the near plane: everything after it
// fails the depth test where a tile stands (and costs nothing there), and the tiles still sort among
// themselves while they turn. The warning cue draws with ZTest Always, so it stays on top.
Shader "Hidden/Atlas/HexFlip"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry-100" }
        Pass
        {
            Name "HexFlip"
            ZTest LEqual
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // HexDome.FlipSeconds and HexDome.TileCount.
            #define FLIP_SECONDS 0.5
            #define TILE_COUNT 92

            TEXTURE2D_ARRAY(_OldStill);
            SAMPLER(sampler_OldStill);
            TEXTURE2D_ARRAY(_NewStill);
            SAMPLER(sampler_NewStill);

            float4 _OldCentre;      // world position the old still was taken from
            float4 _NewCentre;      // and the new one
            float _TileDelays[TILE_COUNT];  // seconds after the reveal begins that each tile starts to turn
            float _RevealTime;      // seconds since the reveal began; negative before
            float _SeamGain;        // 0..1: the seams come up as the cover goes on

            // The six faces of a still, as HexTransition renders them: forward and up of each camera.
            static const float3 kForward[6] =
            {
                float3(1, 0, 0), float3(-1, 0, 0), float3(0, 1, 0), float3(0, -1, 0), float3(0, 0, 1), float3(0, 0, -1)
            };
            static const float3 kUp[6] =
            {
                float3(0, 1, 0), float3(0, 1, 0), float3(0, 0, -1), float3(0, 0, 1), float3(0, 1, 0), float3(0, 1, 0)
            };

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 axis : NORMAL;           // the tile's turning axis, unit, object space
                float4 centre : TEXCOORD0;      // xyz: the tile's centre, object space; w: the tile's index
                float2 rim : TEXCOORD1;         // x: 0 at the centre, 1 on the edge
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 frontDir : TEXCOORD2;
                float3 backDir : TEXCOORD3;
                float2 rimTurn : TEXCOORD4;     // x: rim, y: turn 0..1
            };

            float3 Rotate(float3 v, float3 axis, float s, float c)
            {
                return v * c + cross(axis, v) * s + axis * dot(axis, v) * (1.0 - c);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 centre = input.centre.xyz;
                float3 axis = input.axis;
                float delay = _TileDelays[(int)(input.centre.w + 0.5)];
                float x = saturate((_RevealTime - delay) / FLIP_SECONDS);
                float turn = x * x * (3.0 - 2.0 * x);
                float s, c;
                sincos(PI * turn, s, c);

                float3 v = input.positionOS - centre;
                float3 turned = centre + Rotate(v, axis, s, c);
                // Where this point of the back lands when the tile has turned over: the new still is read there.
                float3 landed = centre + 2.0 * dot(axis, v) * axis - v;
                float3 normalOS = Rotate(-normalize(centre), axis, s, c);

                output.positionWS = TransformObjectToWorld(turned);
                output.normalWS = TransformObjectToWorldDir(normalOS, false);
                output.frontDir = TransformObjectToWorld(input.positionOS) - _OldCentre.xyz;
                output.backDir = TransformObjectToWorld(landed) - _NewCentre.xyz;
                output.rimTurn = float2(input.rim.x, turn);

                float4 positionCS = TransformWorldToHClip(output.positionWS);
                // In front of everything, in order among the tiles: a slab of depth by the near plane.
                float d = saturate(positionCS.w / 20.0);
                #if UNITY_REVERSED_Z
                positionCS.z = (0.99 - 0.09 * d) * positionCS.w;
                #else
                positionCS.z = (-0.99 + 0.09 * d) * positionCS.w;
                #endif
                // Turned over: dropped outside the clip volume, the live space shows.
                output.positionCS = turn >= 0.999 ? float4(2.0, 2.0, 2.0, 1.0) : positionCS;
                return output;
            }

            float3 FaceUv(float3 d)
            {
                float3 a = abs(d);
                int face = a.x >= a.y && a.x >= a.z ? (d.x > 0.0 ? 0 : 1)
                         : (a.y >= a.z ? (d.y > 0.0 ? 2 : 3) : (d.z > 0.0 ? 4 : 5));
                float3 f = kForward[face];
                float3 u = kUp[face];
                float3 r = cross(u, f);
                float2 uv = float2(dot(d, r), dot(d, u)) / dot(d, f);
                return float3(uv * 0.5 + 0.5, face);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                bool front = dot(input.normalWS, _WorldSpaceCameraPos - input.positionWS) >= 0.0;
                half3 colour;
                if (front)
                {
                    float3 uvf = FaceUv(input.frontDir);
                    colour = SAMPLE_TEXTURE2D_ARRAY(_OldStill, sampler_OldStill, uvf.xy, uvf.z).rgb;
                }
                else
                {
                    float3 uvf = FaceUv(input.backDir);
                    colour = SAMPLE_TEXTURE2D_ARRAY(_NewStill, sampler_NewStill, uvf.xy, uvf.z).rgb;
                }
                // A light seam round each tile, and a turning tile darkens a little as it goes edge on.
                half seam = smoothstep(0.88, 0.98, input.rimTurn.x) * _SeamGain;
                colour = lerp(colour, half3(1.0, 1.0, 1.0), seam * 0.22);
                colour *= 1.0 - 0.35 * sin(PI * input.rimTurn.y);
                return half4(colour, 1.0);
            }
            ENDHLSL
        }
    }
}
