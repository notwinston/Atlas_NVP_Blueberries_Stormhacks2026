using Unity.Mathematics;

namespace Atlas.Spaces.SmallPlanet
{
    // CPU reference for the look's fog and for AtlasWarpClip in AtlasWarp.hlsl (kernel.md §6.3); the GPU side is the shared shader's.
    public static class SmallPlanetShading
    {
        // log map is radially exact, so |dxz| / R is the geodesic distance in rad
        public static float VertexGeodesic(float3 warpedWS, float3 headPosWS, float R)
            => math.length(warpedWS.xz - headPosWS.xz) / R;

        public static bool Discard(float interpolatedGeodesic) => interpolatedGeodesic > (float)SmallPlanetConstants.DiscardRad;

        public static float Fog(float g, float startRad, float opaqueRad) => math.saturate((g - startRad) / (opaqueRad - startRad));
    }
}
