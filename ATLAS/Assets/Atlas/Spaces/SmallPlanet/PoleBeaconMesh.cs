using System;
using Atlas.Rendering;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Spaces.SmallPlanet
{
    // §3.5 pole beacon: octagonal prism, origin at its foot, no bottom cap; 17 vertices, 24 triangles.
    public static class PoleBeaconMesh
    {
        public static int Build(Mesh target)
        {
            int n = SmallPlanetConstants.BeaconSides;
            float r = SmallPlanetConstants.BeaconRadius, h = SmallPlanetConstants.BeaconHeight;
            var verts = new TileVertex[2 * n + 1];
            var idx = new ushort[(2 * n + n) * 3];
            byte lo = (byte)math.round(255f * (float)TileChart.AoMin), hi = 255;
            for (int i = 0; i < n; i++)
            {
                float th = (22.5f + 360f / n * i) * Mathf.Deg2Rad;
                float x = r * Mathf.Cos(th), z = r * Mathf.Sin(th);
                verts[i] = new TileVertex { Position = new float3(x, 0, z), Color = new Color32(lo, lo, lo, 255), Uv = SmallPlanetConstants.BeaconUv };
                verts[n + i] = new TileVertex { Position = new float3(x, h, z), Color = new Color32(hi, hi, hi, 255), Uv = SmallPlanetConstants.BeaconUv };
            }
            verts[2 * n] = new TileVertex { Position = new float3(0, h, 0), Color = new Color32(hi, hi, hi, 255), Uv = SmallPlanetConstants.BeaconUv };
            int c = 0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                idx[c++] = (ushort)i; idx[c++] = (ushort)(n + i); idx[c++] = (ushort)(n + j);
                idx[c++] = (ushort)i; idx[c++] = (ushort)(n + j); idx[c++] = (ushort)j;
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                idx[c++] = (ushort)(2 * n); idx[c++] = (ushort)(n + j); idx[c++] = (ushort)(n + i);
            }
            target.Clear();
            target.SetVertexBufferParams(verts.Length, TileVertex.Descriptors);
            target.SetVertexBufferData(verts, 0, 0, verts.Length, 0, MeshUpdateFlags.DontRecalculateBounds);
            target.SetIndexBufferParams(idx.Length, IndexFormat.UInt16);
            target.SetIndexBufferData(idx, 0, 0, idx.Length, MeshUpdateFlags.DontRecalculateBounds);
            var bounds = new Bounds(new Vector3(0, h * 0.5f, 0), new Vector3(2 * r, h, 2 * r));
            target.subMeshCount = 1;
            target.SetSubMesh(0, new SubMeshDescriptor(0, idx.Length) { bounds = bounds, vertexCount = verts.Length }, MeshUpdateFlags.DontRecalculateBounds);
            target.bounds = bounds;
            return idx.Length / 3;
        }
    }
}
