#ifndef ATLAS_WARP_INCLUDED
#define ATLAS_WARP_INCLUDED
// Lane B (P1-B03), kernel.md §6. The includer (AtlasCore.hlsl, lane A) declares _Anchor (float4x4) and _WarpMode (float)
// in UnityPerMaterial and provides TransformObjectToWorld (URP Core.hlsl) before including this file.
float  _HypL;       // global, metres: L (mode 1) or R (mode 2)
float3 _HeadPosWS;  // global, metres, world

// Master Plan A.2 Exp at o (kappa = -1); sinh and cosh from one exp (§8.3)
float3 ExpH2(float2 t)
{
    float r = length(t);
    float e = exp(r);
    float ie = 1.0 / e;
    float sOverR = r > 1e-6 ? 0.5 * (e - ie) / r : 1.0;
    return float3(t * sOverR, 0.5 * (e + ie));
}
// Master Plan A.2 Log at o (kappa = -1); asinh(s) = log(s + sqrt(s^2 + 1)), HLSL has no asinh
float2 LogH2(float3 p)
{
    float s = length(p.xy);
    return s > 1e-6 ? p.xy * (log(s + sqrt(s * s + 1.0)) / s) : p.xy;
}
// Master Plan A.2 Exp at o (kappa = +1)
float3 ExpS2(float2 t)
{
    float r = length(t);
    float sn, cs;
    sincos(r, sn, cs);
    float sOverR = r > 1e-6 ? sn / r : 1.0;
    return float3(t * sOverR, cs);
}
// Master Plan A.2 Log at o (kappa = +1); atan2 form of acos(p_w) (kernel.md §4.3)
float2 LogS2(float3 p)
{
    float s = length(p.xy);
    if (s > 1e-6) return p.xy * (atan2(s, p.z) / s);
    return p.z < 0.0 ? float2(3.14159265, 0.0) : p.xy; // A.2 at the cut point: distance pi, as Geometry2.Log
}
// Master Plan §4.6 / §8.3: object position in, world position out
float3 AtlasWarp(float3 positionOS)
{
    [branch] if (_WarpMode < 0.5) return TransformObjectToWorld(positionOS);
    float3x3 a = (float3x3)_Anchor;
    float2 t;
    [branch] if (_WarpMode < 1.5) t = LogH2(mul(a, ExpH2(positionOS.xz / _HypL))) * _HypL;
    else                          t = LogS2(mul(a, ExpS2(positionOS.xz / _HypL))) * _HypL;
    return float3(_HeadPosWS.x + t.x, positionOS.y, _HeadPosWS.z + t.y);
}
#endif
