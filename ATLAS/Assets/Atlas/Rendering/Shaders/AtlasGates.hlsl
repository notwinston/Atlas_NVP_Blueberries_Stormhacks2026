#ifndef ATLAS_GATES_INCLUDED
#define ATLAS_GATES_INCLUDED
// Master Plan §4.7, §8.3; R1 §F.7; kernel.md §6.5. Needs _Gates (UnityPerMaterial, AtlasCore.hlsl) and URP Core.hlsl.
void AtlasGatesClip(float3 positionWS)
{
    float2 e = _WorldSpaceCameraPos.xz;              // each eye's own camera
    float2 d = positionWS.xz - e;
    [unroll] for (int i = 0; i < 4; i++)
    {
        float4 gate = _Gates[i];                     // (post.x, post.z, side, unused); side 0 = off
        float2 g = gate.xy - e;
        if (gate.z != 0) clip(gate.z * (g.x * d.y - g.y * d.x));
    }
}
#endif
