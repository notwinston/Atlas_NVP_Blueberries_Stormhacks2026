using Atlas.Kernel;
using Atlas.Rendering;
using Unity.Mathematics;

namespace Atlas.Spaces.SmallPlanet
{
    // §3.5 pole beacon: octagonal prism, origin at its foot, no bottom cap; 17 vertices, 24 triangles. Engine-free.
    public static class PoleBeaconMesh
    {
        /// <summary>Appends the beacon and returns the triangles added.</summary>
        public static int Build(AtlasMeshBuilder into)
        {
            int n = SmallPlanetConstants.BeaconSides;
            float r = SmallPlanetConstants.BeaconRadius, h = SmallPlanetConstants.BeaconHeight;
            byte lo = (byte)math.round(255f * (float)TileChart.AoMin), hi = 255;
            int first = into.VertexCount;
            into.Reserve(2 * n + 1, 3 * n * 3);
            for (int i = 0; i < n; i++)
            {
                float th = math.radians(22.5f + 360f / n * i);
                into.AddVertex(Vertex(new float3(r * math.cos(th), 0, r * math.sin(th)), lo));
            }
            for (int i = 0; i < n; i++)
            {
                float th = math.radians(22.5f + 360f / n * i);
                into.AddVertex(Vertex(new float3(r * math.cos(th), h, r * math.sin(th)), hi));
            }
            into.AddVertex(Vertex(new float3(0, h, 0), hi));
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                into.AddTriangle(first + i, first + n + i, first + n + j);
                into.AddTriangle(first + i, first + n + j, first + j);
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                into.AddTriangle(first + 2 * n, first + n + j, first + n + i);
            }
            return 3 * n;
        }

        // The shade is baked (COLOR.r); the beacon carries no lines.
        static AtlasVertex Vertex(float3 position, byte shade) => new AtlasVertex
        {
            Position = position,
            Normal = new float3(0, 1, 0),
            Color = AtlasVertex.PackColor(shade, 0),
            Uv0 = SmallPlanetConstants.BeaconUv,
            Uv1 = new float2(0.5f, 0.5f),
        };
    }
}
