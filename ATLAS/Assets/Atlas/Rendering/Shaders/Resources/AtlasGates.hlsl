#ifndef ATLAS_GATES_INCLUDED
#define ATLAS_GATES_INCLUDED

// Per-pixel clip against up to four sight-line gates (Aligned Cells, Master Plan section 4.7).
// The gates _Gate0 .. _Gate3 are reserved in the block in AtlasCore.hlsl. This file declares no uniform
// and no keyword.

// Fragment stage. Discards the fragment when it lies on the hidden side of any enabled gate.
//   positionWS  world-space position of the fragment, metres (interpolated from the vertex stage).
void AtlasGatesClip(float3 positionWS)
{
    // No gate exists yet: nothing is clipped.
}

#endif
