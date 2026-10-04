#ifndef ATLAS_WARP_INCLUDED
#define ATLAS_WARP_INCLUDED

// The warp of the curved spaces (Master Plan section 8.3). One entry point: object position in, world
// position out. Included after URP's Core.hlsl and AtlasCore.hlsl, which declare _WarpMode, _Anchor,
// _HeadPosWS and _HypL. This file declares no uniform and no keyword.

// Values of the per-material _WarpMode.
#define ATLAS_WARP_NONE        0.0
#define ATLAS_WARP_HYPERBOLIC  1.0
#define ATLAS_WARP_SPHERICAL   2.0

// Vertex stage.
//   positionOS  object-space position of the vertex, metres.
//   returns     world-space position, metres, ready for TransformWorldToHClip.
float3 AtlasWarp(float3 positionOS)
{
    return TransformObjectToWorld(positionOS);       // mode none: the identity warp
}

#endif
