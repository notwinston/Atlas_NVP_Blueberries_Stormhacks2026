using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Rendering
{
    // P1-B09 [B09 D8]: the lap flag and corner beacons of the 3-4-5 walk (Master Plan §3.3, §4.4): an octagonal prism with its
    // origin at the base centre, rows of at most 0.2 m (the warp bends long edges, §8.4), a top cap, AO colour 255, outward normals.
    public static class LapMarkerMesh
    {
        public const int Sides = 8;
        public const float MaxRowM = 0.2f;
        public const float FlagHeightM = 2.0f, BeaconHeightM = 1.2f, RadiusM = 0.1f;   // [B09 D8]
        public static readonly float2 FlagUv = new float2(0.5625f, 0.0625f);           // provisional palette texels like TilePalette.Provisional
        public static readonly float2 BeaconUv = new float2(0.6875f, 0.0625f);

        public static int Build(Mesh target, float heightM, float radiusM, float2 paletteUv)
        {
            int rows = Mathf.Max(1, Mathf.CeilToInt(heightM / MaxRowM - 1e-4f));
            var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var uv = new List<Vector2>(); var col = new List<Color32>();
            var idx = new List<int>();
            var white = new Color32(255, 255, 255, 255);
            var uv2 = new Vector2(paletteUv.x, paletteUv.y);
            for (int r = 0; r <= rows; r++)
            {
                float y = heightM * r / rows;
                for (int i = 0; i < Sides; i++)
                {
                    float th = (22.5f + 360f / Sides * i) * Mathf.Deg2Rad;
                    float c = Mathf.Cos(th), s = Mathf.Sin(th);
                    pos.Add(new Vector3(radiusM * c, y, radiusM * s)); nrm.Add(new Vector3(c, 0, s)); uv.Add(uv2); col.Add(white);
                }
            }
            for (int r = 0; r < rows; r++)
                for (int i = 0; i < Sides; i++)
                {
                    int j = (i + 1) % Sides;
                    int a = r * Sides + i, b = r * Sides + j, c2 = (r + 1) * Sides + i, d = (r + 1) * Sides + j;
                    idx.Add(a); idx.Add(c2); idx.Add(d);
                    idx.Add(a); idx.Add(d); idx.Add(b);
                }
            int capCentre = pos.Count;
            pos.Add(new Vector3(0, heightM, 0)); nrm.Add(Vector3.up); uv.Add(uv2); col.Add(white);
            int capRing = pos.Count;
            for (int i = 0; i < Sides; i++)
            {
                float th = (22.5f + 360f / Sides * i) * Mathf.Deg2Rad;
                pos.Add(new Vector3(radiusM * Mathf.Cos(th), heightM, radiusM * Mathf.Sin(th))); nrm.Add(Vector3.up); uv.Add(uv2); col.Add(white);
            }
            for (int i = 0; i < Sides; i++)
            {
                int j = (i + 1) % Sides;
                idx.Add(capCentre); idx.Add(capRing + j); idx.Add(capRing + i);
            }
            target.Clear();
            target.SetVertices(pos); target.SetNormals(nrm); target.SetUVs(0, uv); target.SetColors(col);
            target.SetTriangles(idx, 0, false);
            target.bounds = new Bounds(new Vector3(0, heightM * 0.5f, 0), new Vector3(2 * radiusM, heightM, 2 * radiusM));
            return idx.Count / 3;
        }
    }
}
