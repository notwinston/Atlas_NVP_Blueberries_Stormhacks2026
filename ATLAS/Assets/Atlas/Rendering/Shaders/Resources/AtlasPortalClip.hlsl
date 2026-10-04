#ifndef ATLAS_PORTAL_CLIP_INCLUDED
#define ATLAS_PORTAL_CLIP_INCLUDED

// Clip against a portal plane (Master Plan decision D16). A room seen through a doorway carries the
// doorway's plane, normal pointing into that room, so its geometry in front of the doorway is not drawn.
// The default plane (0, 0, 0, 1) never clips. Reads _PortalPlane from the block in AtlasCore.hlsl.

// Fragment stage.
//   positionWS  world-space position of the fragment, metres.
void AtlasPortalClip(float3 positionWS)
{
    clip(dot(_PortalPlane.xyz, positionWS) + _PortalPlane.w);
}

#endif
