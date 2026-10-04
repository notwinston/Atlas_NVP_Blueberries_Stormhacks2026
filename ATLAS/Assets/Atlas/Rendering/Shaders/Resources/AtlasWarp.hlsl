#ifndef ATLAS_WARP_INCLUDED
#define ATLAS_WARP_INCLUDED
// Lane B (P1-B03), kernel.md §6. Included after URP's Core.hlsl (TransformObjectToWorld) and AtlasCore.hlsl (lane A), which
// declares _Anchor (float4x4) and _WarpMode (float) in UnityPerMaterial and the globals _HypL (metres: L in mode 1, R in
// modes 2 and 3) and _HeadPosWS (xyz metres, world). This file declares no uniform and no keyword.

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
// Mode 3 (Small Planet's "Hyperbolica look"): S3, the round 3-space of radius R, whose floor is the great 2-sphere w = 0.
// p = the vertex's floor point (unit, head-relative, the head's floor point at (0,0,1)); hr, er = vertex and eye heights / R.
// q = (cos hr p, sin hr) and the eye E = (0, 0, cos er, sin er); returns Log_E(q) in units of R as (right, up, forward)
// with right = (1,0,0,0), forward = (0,1,0,0), up = (0,0,-sin er, cos er): world x, y, z. One sincos for both heights.
// Every sight line meets the floor again: past a quarter turn the ground rises above the horizon and things grow.
float3 LogS3(float3 p, float hr, float er)
{
    float2 sn, cs;
    sincos(float2(hr, er), sn, cs);                                       // (sin hr, sin er), (cos hr, cos er)
    float3 v = float3(cs.x * p.x, cs.y * sn.x - sn.y * cs.x * p.z, cs.x * p.y);   // q - (q.E) E in that basis
    float c = cs.x * cs.y * p.z + sn.x * sn.y;                            // q.E
    float s = length(v);
    if (s > 1e-6) return v * (atan2(s, c) / s);
    return c < 0.0 ? float3(0.0, 3.14159265, 0.0) : v;                   // at -E: distance pi, straight up
}
// Master Plan §4.6 / §8.3: object position in, world position out
float3 AtlasWarp(float3 positionOS)
{
    [branch] if (_WarpMode < 0.5) return TransformObjectToWorld(positionOS);
    float3x3 a = (float3x3)_Anchor;
    // _Anchor._m13 is the height of the object's base above the floor (0 for tiles and the shell): the Transform is not read here
    float h = positionOS.y + _Anchor._m13;
    float2 t;
    [branch] if (_WarpMode < 1.5) t = LogH2(mul(a, ExpH2(positionOS.xz / _HypL))) * _HypL;
    else
    {
        float3 p = mul(a, ExpS2(positionOS.xz / _HypL));
        // The floor is world y = 0 (modes 1 and 2 put it there), so the eye is _HeadPosWS.y above it
        [branch] if (_WarpMode > 2.5) return _HeadPosWS.xyz + LogS3(p, h / _HypL, _HeadPosWS.y / _HypL) * _HypL;
        t = LogS2(p) * _HypL;
    }
    return float3(_HeadPosWS.x + t.x, h, _HeadPosWS.z + t.y);
}
// kernel.md §6.3 (P1-B08), fragment stage: on the sphere, discard beyond 2.6 rad of geodesic distance from the head
// (smeared triangles near the antipode). The log map puts positionWS at its true distance from _HeadPosWS: on the
// floor in mode 2, in 3D in mode 3.
void AtlasWarpClip(float3 positionWS)
{
    if (_WarpMode > 2.5 && _HypL > 0) clip(2.6 - length(positionWS - _HeadPosWS.xyz) / _HypL);
    else if (_WarpMode > 1.5 && _HypL > 0) clip(2.6 - length(positionWS.xz - _HeadPosWS.xz) / _HypL);
}
#endif
