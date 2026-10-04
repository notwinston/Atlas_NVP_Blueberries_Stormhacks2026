using Unity.Mathematics;

namespace Atlas.Spaces.SmallPlanet
{
    // CPU reference for lane A's AtlasCore (kernel.md §6.3); lane A owns the GPU side.
    public static class SmallPlanetShading
    {
        // log map is radially exact, so |dxz| / R is the geodesic distance in rad
        public static float VertexGeodesic(float3 warpedWS, float3 headPosWS, float R)
            => math.length(warpedWS.xz - headPosWS.xz) / R;

        public static bool Discard(float interpolatedGeodesic) => interpolatedGeodesic > (float)SmallPlanetConstants.DiscardRad;

        public static float Fog(float g, float startRad, float opaqueRad) => math.saturate((g - startRad) / (opaqueRad - startRad));
    }
}
