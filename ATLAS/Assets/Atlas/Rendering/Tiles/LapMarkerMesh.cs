using Atlas.Kernel;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Rendering
{
    // P1-B09 [B09 D8]: the lap flag and corner beacons of the 3-4-5 walk (Master Plan §3.3, §4.4): an octagonal prism with its
    // origin at the base centre, rows of at most 0.2 m (the warp bends long edges, §8.4), a top cap, AO colour 255, outward normals.
    // Written in the one vertex layout of the look shader (AtlasVertex), with no lines.
    public static class LapMarkerMesh
    {
        public const int Sides = 8;
        public const float MaxRowM = 0.2f;
        public const float FlagHeightM = 2.0f, BeaconHeightM = 1.2f, RadiusM = 0.1f;   // [B09 D8]
        public static readonly float2 FlagUv = AtlasPalette.Uv(AtlasPalette.Flag);     // emissive accents of the palette (lane A contract §10.7)
        public static readonly float2 BeaconUv = AtlasPalette.Uv(AtlasPalette.LapMarker);

        public static int Build(Mesh target, float heightM, float radiusM, float2 paletteUv)
        {
            int rows = Mathf.Max(1, Mathf.CeilToInt(heightM / MaxRowM - 1e-4f));
            var data = new AtlasMeshBuilder((rows + 2) * Sides + 1, (2 * rows + 1) * Sides * 3);
            uint lit = AtlasVertex.PackColor(255, 0);   // shade 255, no lines
            for (int r = 0; r <= rows; r++)
            {
                float y = heightM * r / rows;
                for (int i = 0; i < Sides; i++)
                {
                    float th = (22.5f + 360f / Sides * i) * Mathf.Deg2Rad;
                    float c = Mathf.Cos(th), s = Mathf.Sin(th);
                    data.AddVertex(new AtlasVertex { Position = new float3(radiusM * c, y, radiusM * s), Normal = new float3(c, 0, s), Color = lit, Uv0 = paletteUv });
                }
            }
            for (int r = 0; r < rows; r++)
                for (int i = 0; i < Sides; i++)
                {
                    int j = (i + 1) % Sides;
                    int a = r * Sides + i, b = r * Sides + j, c2 = (r + 1) * Sides + i, d = (r + 1) * Sides + j;
                    data.AddTriangle(a, c2, d);
                    data.AddTriangle(a, d, b);
                }
            int capCentre = data.AddVertex(new AtlasVertex { Position = new float3(0, heightM, 0), Normal = new float3(0, 1, 0), Color = lit, Uv0 = paletteUv });
            int capRing = data.VertexCount;
            for (int i = 0; i < Sides; i++)
            {
                float th = (22.5f + 360f / Sides * i) * Mathf.Deg2Rad;
                data.AddVertex(new AtlasVertex { Position = new float3(radiusM * Mathf.Cos(th), heightM, radiusM * Mathf.Sin(th)), Normal = new float3(0, 1, 0), Color = lit, Uv0 = paletteUv });
            }
            for (int i = 0; i < Sides; i++)
            {
                int j = (i + 1) % Sides;
                data.AddTriangle(capCentre, capRing + j, capRing + i);
            }
            AtlasMesh.Apply(data, target);
            target.bounds = new Bounds(new Vector3(0, heightM * 0.5f, 0), new Vector3(2 * radiusM, heightM, 2 * radiusM));
            return data.TriangleCount;
        }
    }
}
