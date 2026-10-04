// PLACEHOLDER (lane A, P1-A06): contract surface only; replaced by lane A's implementation
// Master Plan §8.3: palette, vertex AO, grid and edge lines, fog, the single per-material constant block.
#ifndef ATLAS_CORE_INCLUDED
#define ATLAS_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"   // TransformObjectToWorld for AtlasWarp.hlsl

// The single per-material block (Master Plan §8.3). One block: SRP Batcher compatible.
CBUFFER_START(UnityPerMaterial)
    float4x4 _Anchor;     // lane B, kernel.md §6.2
    float    _WarpMode;   // lane B: 0 none, 1 hyperbolic, 2 spherical
    float4x4 _Gates;      // lane B, kernel.md §6.5
    float4   _Tint;       // lane A (PH-6): rgb multiplier, default (1,1,1,1)
CBUFFER_END

#include "AtlasWarp.hlsl"    // lane B, kernel.md §6.1
#include "AtlasGates.hlsl"   // lane B, kernel.md §6.5

// Globals (PH-7). Lane B sets the fog distances in metres (Master Plan §8.5).
TEXTURE2D(_PaletteTex); SAMPLER(sampler_PaletteTex);   // 128 x 128, Master Plan §8.1
float4 _FogColor;
float  _FogStart, _FogEnd;

struct Attributes
{
    float3 positionOS : POSITION;   // m, object space (chart space for tiles)
    float3 normalOS   : NORMAL;
    float4 color      : COLOR;      // rgb = baked AO, 1 = open (§8.1)
    float2 uv0        : TEXCOORD0;  // palette uv
    float2 uv1        : TEXCOORD1;  // x = distance in m to the nearest edge line
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 positionOS : TEXCOORD1;
    float3 normalOS   : TEXCOORD2;
    float3 ao         : TEXCOORD3;
    float2 uv0        : TEXCOORD4;
    float  edgeDist   : TEXCOORD5;
};

Varyings AtlasVert(Attributes i)
{
    Varyings o;
    o.positionWS = AtlasWarp(i.positionOS);                       // kernel.md §6.1
    o.positionCS = TransformWorldToHClip(o.positionWS);
    o.positionOS = i.positionOS;
    o.normalOS = i.normalOS;
    o.ao = i.color.rgb;
    o.uv0 = i.uv0;
    o.edgeDist = i.uv1.x;
    return o;
}

half4 AtlasFrag(Varyings i) : SV_Target
{
    AtlasGatesClip(i.positionWS);                                  // 1. kernel.md §6.5

    // 2. distance: geodesic from the head in curved modes (kernel.md §6.3), else Euclidean from the camera
    float dist = (_WarpMode > 0.5) ? length(i.positionWS.xz - _HeadPosWS.xz) : distance(i.positionWS, _WorldSpaceCameraPos);

    // 3. Small Planet: discard beyond 2.6 rad (kernel.md §6.3)
    if (_WarpMode > 1.5 && _HypL > 0 && dist / _HypL > 2.6) discard;

    // 4. palette * AO * tint
    float3 c = SAMPLE_TEXTURE2D(_PaletteTex, sampler_PaletteTex, i.uv0).rgb * i.ao.r * _Tint.rgb;

    // 5. lines with fwidth: tile edges and a 0.5 m floor grid (Master Plan §8.1); widths are PH-8
    float edgeW = 0.02;
    float e = 1.0 - smoothstep(edgeW, edgeW + 1.5 * fwidth(i.edgeDist), i.edgeDist);
    float2 g = i.positionOS.xz / 0.5;
    float2 gd = abs(frac(g - 0.5) - 0.5) / max(fwidth(g), 1e-4);
    float gridLine = (1.0 - saturate(min(gd.x, gd.y))) * step(0.5, i.normalOS.y);
    c = lerp(c, c * 1.6 + 0.05, saturate(max(e, gridLine * 0.5)));

    // 6. fog (metres); off while the range is empty
    float fog = (_FogEnd > _FogStart) ? saturate((dist - _FogStart) / (_FogEnd - _FogStart)) : 0.0;
    c = lerp(c, _FogColor.rgb, fog);
    return half4(c, 1);
}

#endif
