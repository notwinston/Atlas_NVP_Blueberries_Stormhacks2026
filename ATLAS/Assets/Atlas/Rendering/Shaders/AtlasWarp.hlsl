// PLACEHOLDER (lane B stub, P1-B03): contract surface only; replaced by lane B's implementation (kernel.md §6.1)
// Stand-in for lane B's AtlasWarp.hlsl (P1-B03, kernel.md §6.1) so that the shared shader compiles; mode 0 behaviour only.
#ifndef ATLAS_WARP_INCLUDED
#define ATLAS_WARP_INCLUDED
float  _HypL;        // metres (kernel.md §6.2)
float3 _HeadPosWS;   // metres, world
float3 AtlasWarp(float3 positionOS) { return TransformObjectToWorld(positionOS); }
#endif
